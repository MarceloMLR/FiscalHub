#!/usr/bin/env node
// Valida entidades publicadas, DEPOIS do build, deploy e sync (d365/06 secao 8 e d365/05 secao 9, passo 7).
// Para cada entidade: responde? quantas linhas? todos os campos do XML aparecem no OData (campo Private some)?
// quais campos vem sempre vazios na amostra? a chave e unica? e, se pedido, a contagem bate com a entidade padrao?
//
// Uso (na raiz do repositorio, com "az login" feito):
//   node d365/tools/validar-ambiente.js FSInventJournalBR FSProductionOrderBR
//   --empresa brmf          filtra por dataAreaId (entidade sem empresa e lida inteira)
//   --amostra 200           linhas lidas para medir campo vazio e chave (padrao 200)
//   --comparar ProductionOrderHeaders   conta a entidade padrao com o mesmo filtro (so com uma entidade)
//   --filtro "ProdType eq Microsoft.Dynamics.DataEntities.ProdType'Standard'"   filtro extra na nossa (e na padrao, se nao houver --filtro-padrao)
//   --filtro-padrao "..."  filtro so da entidade padrao (os nomes de campo costumam ser outros)
//   --ambiente https://outro.operations.dynamics.com
'use strict';
const fs = require('fs');
const path = require('path');
const D = require('./lib/d365');
const X = require('./lib/xml');

const a = D.args();
const env = (a.ambiente || D.cfg.environmentUrl).replace(/\/$/, '');
const amostra = parseInt(a.amostra || '200', 10);
if (!a._.length) { console.log('Informe as entidades. Ex.: node d365/tools/validar-ambiente.js FSInventJournalBR'); process.exit(2); }

const VAZIO = v => v === null || v === undefined || v === '' || (typeof v === 'string' && v.startsWith('1900-01-01'));

function doRepo(nome) {
  const arq = path.join(D.pastaModeloRepo(), 'AxDataEntityView', nome + '.xml');
  if (!fs.existsSync(arq)) return null;
  const ent = X.filho(X.parse(D.ler(arq)), 'AxDataEntityView');
  const campos = X.filhos(X.filho(ent, 'Fields') || { filhos: [] }, 'AxDataEntityViewField')
    .filter(f => X.texto(f, 'AccessModifier') !== 'Private' && X.texto(f, 'AccessModifier') !== 'Internal')
    .map(f => X.texto(f, 'Name'));
  const pk = X.texto(ent, 'PrimaryKey');
  const k = X.filhos(X.filho(ent, 'Keys') || { filhos: [] }, 'AxDataEntityViewKey').find(x => X.texto(x, 'Name') === pk);
  const chave = k ? X.todos(k, 'AxDataEntityViewKeyField').map(f => X.texto(f, 'DataField')) : [];
  return { campos, chave, colecao: X.texto(ent, 'PublicCollectionName') };
}

function montar(set, partes) {
  const q = ['cross-company=true', ...partes.filter(Boolean)];
  return `${env}/data/${set}?${q.join('&')}`;
}
const filtroDe = (empresa, extra) => {
  const f = [empresa ? `dataAreaId eq '${empresa}'` : null, extra || null].filter(Boolean).join(' and ');
  return f ? '$filter=' + encodeURIComponent(f) : null;
};

async function contar(tok, set, empresa, extra) {
  const f = filtroDe(empresa, extra);
  const r = await D.odataGet(`${env}/data/${set}/$count?cross-company=true${f ? '&' + f : ''}`, tok);
  return r.ok ? parseInt(r.text, 10) : { erro: r.status };
}

(async () => {
  const tok = D.token(env);
  console.log(`Ambiente: ${env}\n`);
  let falhas = 0;
  for (const arg of a._) {
    const nome = arg.replace(/s$/, '');
    const info = doRepo(nome);
    const set = (info && info.colecao) || (arg.endsWith('s') ? arg : arg + 's');
    console.log(`== ${set}`);
    let empresa = a.empresa || null;

    // 1. responde
    let r = await D.odataGet(montar(set, [`$top=${amostra}`, filtroDe(empresa, a.filtro)]), tok);
    if (r.status === 400 && empresa) {
      const r2 = await D.odataGet(montar(set, [`$top=${amostra}`, filtroDe(null, a.filtro)]), tok);
      if (r2.ok) { console.log('   (entidade sem dataAreaId: lida sem filtro de empresa)'); r = r2; empresa = null; }
    }
    if (!r.ok) {
      falhas++;
      const msg = r.json && r.json.error ? (r.json.error.message || '') : r.text.slice(0, 200);
      console.log(`   FALHA HTTP ${r.status}: ${D.dicaStatus(r.status)}${msg ? ' | ' + msg : ''}\n`);
      continue;
    }
    const linhas = (r.json && r.json.value) || [];
    const total = await contar(tok, set, empresa, a.filtro);
    console.log(`   HTTP 200 | linhas: ${typeof total === 'number' ? total : 'erro ' + total.erro + ' no $count'}${empresa ? ` (empresa ${empresa})` : ''} | amostra: ${linhas.length}`);

    // 2. campos do XML expostos
    if (info) {
      const sel = await D.odataGet(montar(set, ['$top=1', '$select=' + info.campos.join(',')]), tok);
      if (sel.ok) console.log(`   campos: os ${info.campos.length} do XML respondem no $select`);
      else {
        const faltam = [];
        for (const c of info.campos) {
          const s1 = await D.odataGet(montar(set, ['$top=1', '$select=' + c]), tok);
          if (!s1.ok) faltam.push(c);
        }
        falhas++;
        console.log(`   FALHA campos que o OData recusa no $select (${faltam.length}): ${faltam.join(', ')} -> Private, pacote sem o deploy, ou sync parcial`);
      }
    } else console.log('   (entidade fora do repositorio: sem checagem de campos do XML)');

    // 3. vazios e chave na amostra
    if (linhas.length) {
      const nomesCampos = info ? info.campos : Object.keys(linhas[0]).filter(k => !k.startsWith('@'));
      const vazios = nomesCampos.filter(c => linhas.every(l => VAZIO(l[c])));
      console.log(vazios.length ? `   sempre vazios na amostra (${vazios.length}): ${vazios.join(', ')}` : '   nenhum campo sempre vazio na amostra');
      if (info && info.chave.length) {
        const vistos = new Set(); let dup = 0;
        for (const l of linhas) { const k = [l.dataAreaId, ...info.chave.map(c => l[c])].join('|'); if (vistos.has(k)) dup++; vistos.add(k); }
        console.log(dup ? `   FALHA chave ${info.chave.join('+')} repetida ${dup}x na amostra (junção multiplicando linha?)` : `   chave ${info.chave.join('+')} unica na amostra`);
        if (dup) falhas++;
      }
    } else console.log('   sem linhas: campos vazios e chave nao conferidos (normal para recurso que o ambiente nao usa)');

    // 4. comparacao com a padrao
    if (a.comparar && a._.length === 1) {
      const p = await contar(tok, a.comparar, empresa, a['filtro-padrao'] !== undefined ? a['filtro-padrao'] : a.filtro);
      if (typeof p === 'number') {
        const diff = typeof total === 'number' ? total - p : null;
        console.log(`   padrao ${a.comparar}: ${p} | diferenca: ${diff === 0 ? '0 (bate)' : diff}`);
      } else console.log(`   padrao ${a.comparar}: HTTP ${p.erro} (${D.dicaStatus(p.erro)})`);
    }
    console.log('');
  }
  console.log(falhas ? `${falhas} falha(s).` : 'Tudo certo.');
  process.exitCode = falhas ? 1 : 0;
})().catch(e => { console.error('ERRO: ' + e.message); process.exitCode = 2; });
