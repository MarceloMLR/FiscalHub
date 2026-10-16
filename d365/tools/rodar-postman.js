#!/usr/bin/env node
// Roda a collection do d365/postman pelo newman, com o token da sua sessao do az (delegado).
// O token vai so numa copia temporaria da collection, apagada no fim. O repositorio continua sem segredo.
//
// Uso (na raiz do repositorio, com "az login" feito):
//   node d365/tools/rodar-postman.js                          -> a collection inteira
//   node d365/tools/rodar-postman.js --pasta "FiscalHub — produção, Bloco K e cadastros próprios"
//   --ambiente https://outro.operations.dynamics.com
// Esperado: o request "Get Token (manual)" falha (ele usa o clientSecret, vazio no repositorio).
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const D = require('./lib/d365');

const a = D.args();
const env = (a.ambiente || D.cfg.environmentUrl).replace(/\/$/, '');
const origem = path.join(D.DIR_D365, 'postman', 'D365-FnO-OData.postman_collection.json');
// copias de execucoes interrompidas (que podem ter token) saem antes de tudo
for (const n of fs.readdirSync(os.tmpdir())) if (n.startsWith('fh-postman-') && !n.endsWith('.txt')) { try { fs.rmSync(path.join(os.tmpdir(), n), { recursive: true, force: true }); } catch { /* segue */ } }
const tmp = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'fh-postman-')), 'collection.json');

try {
  const c = JSON.parse(fs.readFileSync(origem, 'utf8').replace(/^﻿/, ''));
  const set = (k, v) => { const x = c.variable.find(z => z.key === k); if (x) x.value = v; else c.variable.push({ key: k, value: v }); };
  set('access_token', D.token(env));
  set('token_expires_at', String(Date.now() + 50 * 60 * 1000));
  set('environmentUrl', env);
  fs.writeFileSync(tmp, JSON.stringify(c));
  const argsNewman = ['-y', 'newman@6', 'run', tmp, '--reporters', 'cli', '--color', 'off', '--disable-unicode', '--reporter-cli-no-console'];
  if (a.pasta) argsNewman.push('--folder', a.pasta);
  console.log(`Ambiente: ${env}${a.pasta ? ' | pasta: ' + a.pasta : ''}\nRodando newman (o primeiro uso baixa o pacote)...`);
  // npx pelo proprio node, sem shell: o nome da pasta (com espaco e travessao) vai intacto.
  const npxCli = path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npx-cli.js');
  const r = fs.existsSync(npxCli)
    ? spawnSync(process.execPath, [npxCli, ...argsNewman], { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 })
    : spawnSync('npx', argsNewman.map(x => JSON.stringify(x)), { encoding: 'utf8', shell: true, maxBuffer: 256 * 1024 * 1024 });
  const out = (r.stdout || '') + (r.stderr || '');
  const linhas = out.split(/\r?\n/);
  // resumo: a tabela final e as falhas
  const iTab = linhas.findIndex(l => /executed\s*\|\s*failed/i.test(l));
  const iFal = linhas.findIndex(l => /^\s*#\s+failure\s+detail/i.test(l));
  if (iTab >= 0) console.log(linhas.slice(Math.max(0, iTab - 2), iTab + 14).join('\n'));
  if (iFal >= 0) console.log('\n' + linhas.slice(iFal, iFal + 120).join('\n'));
  if (iTab < 0) console.log(linhas.slice(-60).join('\n'));
  const relatorio = path.join(os.tmpdir(), 'fh-postman-ultimo.txt');
  fs.writeFileSync(relatorio, out);
  console.log(`\nSaida completa: ${relatorio}`);
  process.exitCode = r.status || 0;
} catch (e) {
  console.error('ERRO: ' + e.message);
  process.exitCode = 2;
} finally {
  try { fs.rmSync(path.dirname(tmp), { recursive: true, force: true }); } catch { /* nada */ }
}
