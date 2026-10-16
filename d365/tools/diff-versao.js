#!/usr/bin/env node
// Radar de versao: o que mudou entre a versao base do pacote e uma versao mais nova, filtrado pelo que importa
// para o conector (o que expomos, as tabelas que lemos e a localizacao Brasil).
//
// Dois modos (na raiz do repositorio):
//   node d365/tools/diff-versao.js
//       modo ambiente (padrao): baixa o $metadata do ambiente (que ja esta na versao nova) e compara com o metadado local
//       da versao base. Ve entidades publicas, campos de entidade e valores de enum. Precisa de "az login".
//   node d365/tools/diff-versao.js --modo local --nova 10.0.2790.46
//       modo local: compara duas versoes baixadas no VS (Tools > Download Dynamics 365 FinOps assets). Ve campo de
//       TABELA (o que o modo ambiente nao alcanca) nas tabelas que as nossas entidades leem e nas do Brasil, e os enums delas.
// Opcoes: --base 10.0.2527.160 (padrao: versaoBase do d365.config.json)  --ambiente <url>  --json <arquivo>
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const D = require('./lib/d365');
const X = require('./lib/xml');

const a = D.args();
const base = a.base || D.cfg.versaoBase;
const BR = /(_BR$|_BR[._]|^FB|Fiscal|Brazil|Brasil|Reform|CBS|IBS|Selective|Seletiv|ClassTrib|NFSe|NFe|SPED|EFDoc)/i;
const REFORMA = /(Selective|Seletiv|SplitPaym|Reform|ClassTrib|CBS|IBS|ImpostoSel)/; // sem /i: 'CaptureFormName' e 'TribSubstitution' nao sao reforma

// tabelas e campos que as nossas entidades leem (do repositorio)
function usoDoPacote() {
  const tabelas = new Set(); const pares = new Set();
  const dir = path.join(D.pastaModeloRepo(), 'AxDataEntityView');
  for (const f of fs.readdirSync(dir).filter(x => x.endsWith('.xml'))) {
    const ent = X.filho(X.parse(D.ler(path.join(dir, f))), 'AxDataEntityView');
    const fontes = new Map();
    for (const ds of [...X.todos(ent, 'AxQuerySimpleRootDataSource'), ...X.todos(ent, 'AxQuerySimpleEmbeddedDataSource')]) {
      fontes.set(X.texto(ds, 'Name'), X.texto(ds, 'Table'));
      if (X.texto(ds, 'Table')) tabelas.add(X.texto(ds, 'Table'));
    }
    for (const c of X.filhos(X.filho(ent, 'Fields') || { filhos: [] }, 'AxDataEntityViewField')) {
      const t = fontes.get(X.texto(c, 'DataSource')); const df = X.texto(c, 'DataField');
      if (t && df) pares.add((t + '.' + df).toLowerCase());
    }
  }
  return { tabelas, pares };
}

function imprimir(titulo, itens, max = 200) {
  console.log(`\n## ${titulo} (${itens.length})`);
  if (!itens.length) console.log('  (nada)');
  itens.slice(0, max).forEach(x => console.log('  ' + x));
  if (itens.length > max) console.log(`  ... mais ${itens.length - max}`);
}

async function modoAmbiente() {
  const env = (a.ambiente || D.cfg.environmentUrl).replace(/\/$/, '');
  const pld = D.pldDaVersao(base);
  const dirCache = path.join(os.tmpdir(), 'fh-versao'); fs.mkdirSync(dirCache, { recursive: true });
  const hoje = new Date().toISOString().slice(0, 10);
  const cache = path.join(dirCache, `metadata-${new URL(env).hostname}-${hoje}.xml`);
  if (!fs.existsSync(cache)) {
    const r = await fetch(env + '/data/$metadata', { headers: { Authorization: 'Bearer ' + D.token(env) } });
    if (!r.ok) throw new Error(`$metadata HTTP ${r.status}: ${D.dicaStatus(r.status)}`);
    fs.writeFileSync(cache, Buffer.from(await r.arrayBuffer()));
  }
  const m = fs.readFileSync(cache, 'utf8');
  console.log(`Ambiente: ${env} ($metadata ${(m.length / 1e6).toFixed(1)} MB, cache ${cache})\nBase local: ${base}`);

  const oEnt = new Map(), oEnum = new Map();
  for (const b of m.matchAll(/<EntityType Name="([^"]+)"[^>]*>([\s\S]*?)<\/EntityType>/g)) oEnt.set(b[1], [...b[2].matchAll(/<Property Name="([^"]+)" Type="([^"]+)"/g)].map(x => [x[1], x[2]]));
  for (const b of m.matchAll(/<EnumType Name="([^"]+)"[^>]*>([\s\S]*?)<\/EnumType>/g)) oEnum.set(b[1], [...b[2].matchAll(/<Member Name="([^"]+)"/g)].map(x => x[1]));

  let mc = null; try { mc = D.metadataConfig(); } catch { /* sem VS */ }
  const idx = D.indexar([pld, mc && mc.custom], ['AxDataEntityView', 'AxDataEntityViewExtension', 'AxEnum', 'AxEnumExtension']);
  const pub = new Map(), campos = new Map(), enums = new Map();
  for (const [k, arqs] of idx.AxDataEntityView) {
    const s = new Set();
    for (const f of [...arqs, ...(idx.AxDataEntityViewExtension.get(k) || [])]) {
      const t = D.ler(f);
      const p = D.tag(t, 'PublicEntityName'); if (p && arqs.includes(f)) pub.set(p.toLowerCase(), k);
      for (const x of t.matchAll(/<AxDataEntityViewField[^>]*>\s*<Name>([^<]+)<\/Name>/g)) s.add(x[1].toLowerCase());
    }
    campos.set(k, s);
  }
  for (const [k, arqs] of idx.AxEnum) {
    const s = new Set();
    for (const f of [...arqs, ...(idx.AxEnumExtension.get(k) || [])]) for (const x of D.ler(f).matchAll(/<AxEnumValue>\s*<Name>([^<]+)<\/Name>/g)) s.add(x[1].toLowerCase());
    enums.set(k, s);
  }

  // enums que as nossas entidades FS* expoem
  const enumsDoPacote = new Map();
  for (const [e, props] of oEnt) if (/^FS/.test(e)) for (const [p, t] of props) if (t.startsWith('Microsoft.Dynamics.DataEntities.')) {
    const en = t.slice('Microsoft.Dynamics.DataEntities.'.length);
    if (!enumsDoPacote.has(en)) enumsDoPacote.set(en, []);
    enumsDoPacote.get(en).push(e + '.' + p);
  }

  const novasEnt = [], novosCampos = [], novosEnums = [], novosValores = [], afeta = [];
  for (const [e, props] of oEnt) {
    const aot = pub.get(e.toLowerCase());
    if (!aot) { novasEnt.push(e); continue; }
    const s = campos.get(aot) || new Set();
    for (const [p, t] of props) if (!s.has(p.toLowerCase()) && p !== 'dataAreaId') novosCampos.push(`${e}.${p} (${t.replace('Microsoft.Dynamics.DataEntities.', '')})`);
  }
  for (const [n, mem] of oEnum) {
    const s = enums.get(n.toLowerCase());
    if (!s) { novosEnums.push(`${n} [${mem.join(', ')}]`); continue; }
    for (const x of mem) if (!s.has(x.toLowerCase())) {
      novosValores.push(`${n}.${x}`);
      if (enumsDoPacote.has(n)) afeta.push(`${enumsDoPacote.get(n).join(', ')}: valor novo ${n}.${x}`);
    }
  }
  const reforma = [];
  for (const [e, props] of oEnt) for (const [p] of props) if (REFORMA.test(p)) reforma.push(`${e}.${p}`);
  for (const [n, mem] of oEnum) for (const x of mem) if (REFORMA.test(x) || REFORMA.test(n)) reforma.push(`enum ${n}.${x}`);

  console.log(`\nResumo: ${novasEnt.length} entidades novas, ${novosCampos.length} campos novos, ${novosEnums.length} enums novos, ${novosValores.length} valores de enum novos.`);
  imprimir('AFETA O PACOTE: valor novo em enum que expomos (o hub precisa reconhecer; nao precisa recompilar)', afeta);
  imprimir('Brasil: entidades novas', novasEnt.filter(x => BR.test(x)));
  imprimir('Brasil: campos novos em entidades padrao', novosCampos.filter(x => BR.test(x)));
  imprimir('Brasil: enums novos', novosEnums.filter(x => BR.test(x.split(' ')[0])));
  imprimir('Brasil: valores de enum novos', novosValores.filter(x => BR.test(x)));
  imprimir('Palavras da reforma no ambiente (novo ou nao)', reforma);
  console.log('\nLimite: a API so mostra entidades publicas e enums. Campo novo em TABELA: use --modo local com a versao nova baixada.');
  if (a.json) fs.writeFileSync(a.json, JSON.stringify({ modo: 'ambiente', env, base, afeta, novasEnt, novosCampos, novosEnums, novosValores, reforma }, null, 1));
}

function modoLocal() {
  if (!a.nova) throw new Error(`Informe --nova <versao>. Baixadas: ${D.versoesBaixadas().join(', ') || '(nenhuma)'}`);
  const pB = D.pldDaVersao(base), pN = D.pldDaVersao(a.nova);
  console.log(`Base: ${base}\nNova: ${a.nova}`);
  const tipos = ['AxTable', 'AxTableExtension', 'AxEnum', 'AxEnumExtension'];
  const iB = D.indexar([pB], tipos), iN = D.indexar([pN], tipos);
  const uso = usoDoPacote();
  const escopo = new Set([...uso.tabelas].map(t => t.toLowerCase()));
  for (const k of iN.AxTable.keys()) if (BR.test(k)) escopo.add(k);
  const tabelasNovas = [...iN.AxTable.keys()].filter(k => !iB.AxTable.has(k) && BR.test(k));

  const camposNovos = [], camposSairam = [], noPacote = [], enumsUsados = new Set();
  for (const k of escopo) {
    const cB = iB.AxTable.has(k) ? D.camposDaTabela(iB, k) : null;
    const cN = iN.AxTable.has(k) ? D.camposDaTabela(iN, k) : null;
    for (const f of [...(iN.AxTable.get(k) || []), ...(iN.AxTableExtension.get(k) || [])]) for (const x of D.ler(f).matchAll(/<EnumType>([^<]+)<\/EnumType>/g)) enumsUsados.add(x[1].toLowerCase());
    if (!cB || !cN) continue;
    const nome = path.basename(iN.AxTable.get(k)[0], '.xml');
    const lido = [...uso.tabelas].some(t => t.toLowerCase() === k);
    for (const c of cN) if (!cB.has(c)) camposNovos.push(`${nome}.${cN.nomes.get(c) || c}${lido ? '  <- tabela que o pacote le' : ''}`);
    for (const c of cB) if (!cN.has(c)) { const o = cB.nomes.get(c) || c; camposSairam.push(`${nome}.${o}`); if (uso.pares.has(`${k}.${c}`)) noPacote.push(`${nome}.${o} SAIU e o pacote usa`); }
  }
  for (const k of iN.AxEnum.keys()) if (BR.test(k)) enumsUsados.add(k);
  const valoresNovos = [];
  const valores = (idx, k) => { const s = new Set(); for (const f of [...(idx.AxEnum.get(k) || []), ...(idx.AxEnumExtension.get(k) || [])]) for (const x of D.ler(f).matchAll(/<AxEnumValue>\s*<Name>([^<]+)<\/Name>/g)) s.add(x[1]); return s; };
  for (const k of enumsUsados) {
    if (!iB.AxEnum.has(k) || !iN.AxEnum.has(k)) continue;
    const vB = new Set([...valores(iB, k)].map(v => v.toLowerCase()));
    for (const v of valores(iN, k)) if (!vB.has(v.toLowerCase())) valoresNovos.push(`${path.basename(iN.AxEnum.get(k)[0], '.xml')}.${v}`);
  }
  imprimir('QUEBRA: campo que o pacote usa e saiu da tabela', noPacote);
  imprimir('Campos novos (tabelas que o pacote le e tabelas do Brasil)', camposNovos.sort());
  imprimir('Tabelas novas do Brasil', tabelasNovas);
  imprimir('Valores novos nos enums dessas tabelas e do Brasil', valoresNovos.sort());
  imprimir('Campos que sairam (informativo)', camposSairam.sort(), 50);
  if (a.json) fs.writeFileSync(a.json, JSON.stringify({ modo: 'local', base, nova: a.nova, noPacote, camposNovos, tabelasNovas, valoresNovos, camposSairam }, null, 1));
}

(async () => {
  if ((a.modo || 'ambiente') === 'local') modoLocal(); else await modoAmbiente();
})().catch(e => { console.error('ERRO: ' + e.message); process.exitCode = 2; });
