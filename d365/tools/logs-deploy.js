#!/usr/bin/env node
// Le o historico de deploy/sync do UDE pelo Dataverse (tabela msprov_operationhistories) e diagnostica a falha.
// E o mesmo que o app "Finance and Operation Package Manager" > Operation History mostra (d365/05 secao 12).
//
// Uso (na raiz do repositorio, com "az login" feito):
//   node d365/tools/logs-deploy.js                 -> as ultimas 10 operacoes (deploy, sync), com status e id
//   node d365/tools/logs-deploy.js <id>            -> baixa o operationlogs.zip dessa operacao, extrai e procura os erros
//   node d365/tools/logs-deploy.js --ultima-falha  -> o mesmo, para a falha mais recente
//   --ultimas 20   --dataverse https://outro.api.crm2.dynamics.com
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const D = require('./lib/d365');

const a = D.args();
const dv = (a.dataverse || D.cfg.dataverseUrl).replace(/\/$/, '');
const API = dv + '/api/data/v9.0/';
const FMT = { Prefer: 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"' };
const fv = (x, k) => x[k + '@OData.Community.Display.V1.FormattedValue'] || x[k];

// padrao do log -> o que fazer (os casos do d365/05)
const DIAGNOSTICOS = [
  [/Duplicate metadata element|MetadataReadCrossAssemblyDuplicateException/i, 'Mesmo objeto em dois modulos (ex.: modulo renomeado com o antigo ainda no ambiente). O AOS nao sobe e o UDE desfaz. Saida: publicar o modulo antigo vazio no mesmo deploy (d365/05 secao 12; d365/tools/modulo-vazio-*).'],
  [/DirectoryNotFoundException[\s\S]{0,200}\\bin/i, 'O deploy exige a pasta bin do modulo: rodar o Build models dele antes (ou compilar com o xppc; d365/05 secao 12).'],
  [/Cannot select a record in Sub Query/i, 'Views das entidades nao montadas: a sincronizacao foi parcial. Rodar Extensions > Dynamics 365 > Synchronize database (completa).'],
  [/syncmode\W+PartialList/i, 'O deploy rodou sincronizacao parcial (PartialList). Se alguma entidade nova ou alterada der erro de SQL, rodar a sincronizacao completa.'],
  [/Failed to start services|FailedDependency/i, 'O AOS nao subiu depois de aplicar o pacote: o motivo esta no AXHostStartupTrace*.etl (linhas abaixo).'],
  [/Failed to locate table relation/i, 'JoinRelationName errado: use o Name ou o RelatedTableRole da relacao (costuma ser o nome da tabela relacionada; d365/06 secao 4).'],
  [/Database synchronization (failed|error)|SyncEngine[\s\S]{0,80}(error|fail)/i, 'Falha na sincronizacao do banco: procure abaixo a tabela/view citada.']
];
const PALAVRAS = /exception|error|fail|duplicat|could not|unable|invalid|conflict|not found|denied|rollback|shutting down/i;
// ruido: manifesto do ETW e nomes de flight (uma palavra so)
const RUIDO = s => /<task name=|\$\(string\.|<keyword name=|<opcode name=|<event value=|<template tid=|<data name=/.test(s) || !/\s/.test(s.trim());

async function listar(tok, n) {
  const url = `${API}msprov_operationhistories?$top=${n}&$orderby=createdon desc&$select=msprov_name,msprov_description,statuscode,statecode,msprov_startedon,modifiedon,msprov_operationhistoryid,msprov_logfilename`;
  const r = await D.odataGet(url, tok, FMT);
  if (!r.ok) throw new Error(`Dataverse HTTP ${r.status}: ${r.text.slice(0, 200)}`);
  return r.json.value;
}

function extrair(zip, destino) {
  fs.mkdirSync(destino, { recursive: true });
  let r = spawnSync('tar', ['-xf', zip, '-C', destino], { encoding: 'utf8' });
  if (r.status !== 0) r = spawnSync('powershell', ['-NoProfile', '-Command', `Expand-Archive -LiteralPath '${zip}' -DestinationPath '${destino}' -Force`], { encoding: 'utf8' });
  if (r.status !== 0) throw new Error('nao consegui extrair ' + zip);
}

function arquivos(d) {
  const out = [];
  for (const it of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, it.name);
    if (it.isDirectory()) out.push(...arquivos(p)); else out.push(p);
  }
  return out;
}

// O .etl e binario e o provider nao tem manifesto na maquina: as mensagens saem lendo as strings.
function strings(buf) {
  const s = [];
  for (const [enc, re] of [['utf16le', /[\x20-\x7EÀ-ÿ\r\n\t]{12,}/g], ['latin1', /[\x20-\x7E\r\n\t]{20,}/g]]) {
    const t = buf.toString(enc); let m;
    while ((m = re.exec(t))) s.push(m[0].replace(/\s+/g, ' ').trim());
  }
  return s;
}

async function diagnosticar(tok, id) {
  const r = await fetch(`${API}msprov_operationhistories(${id})/msprov_logs/$value`, { headers: { Authorization: 'Bearer ' + tok } });
  if (!r.ok) throw new Error(`download do log HTTP ${r.status} (a operacao tem log? o id esta certo?)`);
  const base = path.join(os.tmpdir(), 'fh-deploy-logs', id);
  fs.rmSync(base, { recursive: true, force: true });
  fs.mkdirSync(base, { recursive: true });
  const zip = path.join(base, 'operationlogs.zip');
  fs.writeFileSync(zip, Buffer.from(await r.arrayBuffer()));
  const dest = path.join(base, 'extraido');
  extrair(zip, dest);
  for (const z of arquivos(dest).filter(f => f.toLowerCase().endsWith('.zip'))) { try { extrair(z, z.slice(0, -4)); } catch { /* segue */ } }
  console.log(`Log extraido em: ${dest}`);

  const achados = []; const vistos = new Set();
  for (const f of arquivos(dest)) {
    const ext = path.extname(f).toLowerCase();
    if (ext === '.zip') continue;
    let linhas = [];
    if (ext === '.etl') linhas = strings(fs.readFileSync(f));
    else if (['.log', '.txt', '.json', '.xml', '.csv', '.out', '.err', ''].includes(ext) && fs.statSync(f).size < 50 * 1024 * 1024) linhas = fs.readFileSync(f, 'utf8').split(/\r?\n/);
    for (const l of linhas) {
      if (!PALAVRAS.test(l) || RUIDO(l)) continue;
      const k = l.slice(0, 160);
      if (vistos.has(k)) continue;
      vistos.add(k);
      achados.push({ arq: path.relative(dest, f), l: l.trim() });
    }
  }
  const tudo = achados.map(x => x.l).join('\n');
  const diag = DIAGNOSTICOS.filter(([re]) => re.test(tudo)).map(([, m]) => m);
  console.log('\nDiagnostico provavel:');
  console.log(diag.length ? diag.map(m => ' - ' + m).join('\n') : ' - nenhum padrao conhecido; leia as linhas abaixo.');
  const prioridade = x => (DIAGNOSTICOS.some(([re]) => re.test(x.l)) ? 0 : 1);
  achados.sort((p, q) => prioridade(p) - prioridade(q));
  console.log(`\nLinhas com erro (${achados.length}; as que batem com o diagnostico primeiro; ate 60):`);
  for (const x of achados.slice(0, 60)) console.log(`[${path.basename(x.arq)}] ${x.l.slice(0, 400)}`);
}

(async () => {
  const tok = D.token(dv);
  if (a._[0]) return diagnosticar(tok, a._[0]);
  const ops = await listar(tok, parseInt(a.ultimas || (a['ultima-falha'] ? '30' : '10'), 10));
  if (a['ultima-falha']) {
    const f = ops.find(x => !/succeeded/i.test(fv(x, 'statuscode') || ''));
    if (!f) { console.log('Nenhuma falha nas ultimas operacoes.'); return; }
    console.log(`Ultima falha: ${f.msprov_name} (${f.msprov_description}) em ${fv(f, 'msprov_startedon')} -> ${fv(f, 'statuscode')}`);
    return diagnosticar(tok, f.msprov_operationhistoryid);
  }
  console.log(`Dataverse: ${dv}\n`);
  for (const x of ops) {
    console.log(`${(fv(x, 'msprov_startedon') || '').padEnd(17)} ${(x.msprov_name || '').padEnd(8)} ${(fv(x, 'statuscode') || '').padEnd(10)} ${(x.msprov_description || '').padEnd(28)} id=${x.msprov_operationhistoryid}${x.msprov_logfilename ? '' : ' (sem log)'}`);
  }
  console.log('\nPara diagnosticar: node d365/tools/logs-deploy.js <id>   ou   --ultima-falha');
})().catch(e => { console.error('ERRO: ' + e.message); process.exitCode = 2; });
