#!/usr/bin/env node
// Guarda da numeracao dos ADRs. Duas frentes trabalham em paralelo neste repositorio e cada uma escolhe o
// "proximo numero livre" olhando para o main que ela tem; quando as duas escrevem um ADR no mesmo dia, as duas
// escolhem o mesmo numero e ninguem descobre ate o merge. Foi o que aconteceu com o 0040 em 2026-10-16.
//
// Resolver antes do PR custa um `git mv` e um replace. Depois do merge custa caro: um ADR ja publicado e citado
// nao pode ser renumerado sem quebrar o link de quem ja leu. Por isso a conferencia e mecanica, e nao combinada.
//
//   node tools/conferir-adr.js            confere, e diz qual e o proximo numero livre
//   node tools/conferir-adr.js --proximo  imprime so o proximo numero livre, para usar num script
//
// Sai com codigo 1 se achar problema. Confere tres coisas:
//   1. dois arquivos com o mesmo numero aqui;
//   2. um numero que o origin/main ja deu a OUTRO arquivo (a colisao que chega no merge);
//   3. o indice do README: todo ADR tem linha, e toda linha aponta para arquivo que existe.
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const raiz = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
const pasta = path.join(raiz, 'docs', 'adr');
const soProximo = process.argv.includes('--proximo');

const ehAdr = (n) => /^(\d{4})-.+\.md$/.exec(n);

// Os ADRs daqui, por numero.
const locais = new Map();
for (const nome of fs.readdirSync(pasta)) {
  const m = ehAdr(nome);
  if (!m || m[1] === '0000') continue;   // o 0000 e o template
  if (!locais.has(m[1])) locais.set(m[1], []);
  locais.get(m[1]).push(nome);
}

// Os ADRs que o origin/main ja tem. Sem rede: le a referencia que o ultimo fetch deixou.
const remotos = new Map();
let temRemoto = true;
try {
  const saida = execFileSync('git', ['ls-tree', '-r', '--name-only', 'origin/main', 'docs/adr/'], { encoding: 'utf8', cwd: raiz });
  for (const linha of saida.split('\n')) {
    const nome = path.basename(linha.trim());
    const m = ehAdr(nome);
    if (m && m[1] !== '0000') remotos.set(m[1], nome);
  }
} catch {
  temRemoto = false;   // repositorio sem origin/main (clone novo, CI sem fetch): os outros dois testes continuam valendo
}

const problemas = [];

// 1. Dois arquivos com o mesmo numero aqui.
for (const [numero, nomes] of locais) {
  if (nomes.length > 1) {
    problemas.push(`ADR ${numero} esta em ${nomes.length} arquivos: ${nomes.join(', ')}`);
  }
}

// 2. Numero que o origin/main ja deu a outro arquivo.
for (const [numero, nomes] of locais) {
  const remoto = remotos.get(numero);
  if (remoto && !nomes.includes(remoto)) {
    problemas.push(
      `ADR ${numero} colide com o origin/main: aqui e "${nomes[0]}", la ja e "${remoto}".\n` +
      `   O numero que ja esta no main fica com ele. Renumere o daqui: git mv, e acerte as citacoes\n` +
      `   (ADR-${numero} no codigo, nos testes, nas specs e no STATUS) antes de abrir o PR.`
    );
  }
}

// 3. O indice do README: toda linha aponta para arquivo que existe, e todo ADR tem linha.
const readme = fs.readFileSync(path.join(pasta, 'README.md'), 'utf8');
const citados = new Set();
for (const m of readme.matchAll(/\]\((\d{4}-[^)]+\.md)\)/g)) {
  citados.add(m[1]);
  if (!fs.existsSync(path.join(pasta, m[1]))) {
    problemas.push(`o README aponta para "${m[1]}", que nao existe`);
  }
}
for (const nomes of locais.values()) {
  for (const nome of nomes) {
    if (!citados.has(nome)) problemas.push(`"${nome}" nao tem linha no README`);
  }
}

const usados = new Set([...locais.keys(), ...remotos.keys()].map(Number));
let proximo = 1;
while (usados.has(proximo)) proximo++;
const proximoTexto = String(proximo).padStart(4, '0');

if (soProximo) {
  console.log(proximoTexto);
  process.exitCode = 0;
} else if (problemas.length) {
  console.error('Numeracao de ADR com problema:\n');
  for (const p of problemas) console.error(` - ${p}`);
  console.error('');
  process.exitCode = 1;
} else {
  const quantos = [...locais.values()].reduce((s, n) => s + n.length, 0);
  console.log(`Numeracao de ADR ok: ${quantos} ADRs, sem colisao${temRemoto ? ' com o origin/main' : ' (sem origin/main para comparar)'}.`);
  console.log(`Proximo numero livre: ${proximoTexto}`);
  process.exitCode = 0;
}
