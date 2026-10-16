#!/usr/bin/env node
// Exporta os pull requests do GitHub para `docs/historico-prs.md`.
//
// Por que existe: a importacao de um repositorio para o Azure Repos leva o git -- commits, branches, tags -- e NAO
// leva os pull requests. Neste repositorio a descricao de cada PR e onde ficou escrito por que a mudanca e assim, o
// que a conferencia em tela achou que a suite nao pegou, e o resultado de cada prova por inversao. Nada disso esta
// na mensagem de commit. Entao os corpos vem para dentro do repositorio, que e a unica coisa que migra.
//
//   node tools/exportar-prs.js
//
// Rode DE NOVO na vespera da migracao: o arquivo e uma fotografia, e todo PR aberto depois dela fica de fora.
// Precisa do `gh` autenticado (`gh auth status`). Depois da migracao o script perde a serventia.
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const raiz = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
const destino = path.join(raiz, 'docs', 'historico-prs.md');

const campos = 'number,title,body,state,createdAt,mergedAt,author,headRefName,baseRefName,mergeCommit,comments,reviews,url';
const bruto = execFileSync('gh', ['pr', 'list', '--state', 'all', '--limit', '300', '--json', campos], {
  cwd: raiz, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024,
});
const prs = JSON.parse(bruto).sort((a, b) => a.number - b.number);
if (!prs.length) {
  console.error('O gh nao devolveu PR nenhum. Confira `gh auth status` e o remote.');
  process.exitCode = 1;
  return;
}

const dia = (iso) => (iso ? new Date(iso).toISOString().slice(0, 10) : '—');
const hoje = new Date().toISOString().slice(0, 10);

// Comentarios e reviews com texto: se houver, o cabecalho precisa deixar de dizer que nao ha.
const comTexto = prs.reduce((s, p) =>
  s + (p.comments || []).filter((c) => (c.body || '').trim()).length
    + (p.reviews || []).filter((r) => (r.body || '').trim()).length, 0);

const L = [];
L.push('# Histórico dos pull requests — GitHub `MarceloMLR/FiscalHub`');
L.push('');
L.push(`Exportado em ${hoje}, antes da migração para o Azure DevOps, por \`node tools/exportar-prs.js\`.`);
L.push('');
L.push('**Por que este arquivo existe.** A importação de um repositório para o Azure Repos leva o git — commits,');
L.push('branches, tags — e **não leva os pull requests**. No nosso caso isso não é detalhe de ferramenta: a descrição');
L.push('de cada PR é onde ficou escrito *por que* a mudança é assim, o que a conferência em tela achou que a suíte não');
L.push('pegou, e o resultado de cada prova por inversão. Nada disso está na mensagem de commit. Então os corpos vêm');
L.push('para dentro do repositório, que é a única coisa que migra.');
L.push('');
L.push(`**O que está aqui:** os ${prs.length} PRs, do #${prs[0].number} ao #${prs[prs.length - 1].number}, com título,`);
L.push('estado, datas, branch, commit de merge, o link original e o corpo **na íntegra e sem edição** — inclusive os');
L.push('títulos internos deles, que por isso aparecem nos seus níveis originais de cabeçalho.');
L.push('');
if (comTexto === 0) {
  L.push('**O que não está, e não se perdeu:** nenhum PR deste repositório tem comentário ou review com texto');
  L.push('(conferido nesta exportação). A revisão sempre aconteceu na conversa e virou corpo de PR ou comentário de');
  L.push('código. Então o que esta exportação não traz é o que nunca existiu.');
} else {
  L.push(`**Atenção:** esta exportação encontrou **${comTexto}** comentários ou reviews com texto, que ela NÃO traz.`);
  L.push('Quando este arquivo foi criado não havia nenhum. Exporte-os também antes de arquivar o GitHub.');
}
L.push('');
L.push('**O que continua só no GitHub:** os diffs renderizados e os links `https://github.com/...`. Os diffs estão no');
L.push('git, que migra; os links viram históricos.');
L.push('');
L.push('---');
L.push('');
L.push('## Índice');
L.push('');
L.push('| PR | Título | Mesclado | Branch |');
L.push('|---|---|---|---|');
for (const p of prs) {
  const t = (p.title || '').replace(/\|/g, '\\|');
  const estado = p.state === 'MERGED' ? dia(p.mergedAt) : `_${p.state.toLowerCase()}_`;
  L.push(`| [#${p.number}](#pr-${p.number}) | ${t} | ${estado} | \`${p.headRefName || '—'}\` |`);
}
L.push('');
L.push('---');
L.push('');
for (const p of prs) {
  const mc = (p.mergeCommit || {}).oid || '';
  const meta = [
    `**Estado:** ${p.state.toLowerCase()}`,
    `**Aberto:** ${dia(p.createdAt)}`,
    p.mergedAt ? `**Mesclado:** ${dia(p.mergedAt)}` : null,
    `**Branch:** \`${p.headRefName || '—'}\` → \`${p.baseRefName || '—'}\``,
    mc ? `**Commit de merge:** \`${mc.slice(0, 7)}\`` : null,
    `**Autor:** ${(p.author || {}).login || '—'}`,
    p.url ? `[original](${p.url})` : null,
  ].filter(Boolean).join(' · ');

  L.push(`<a id="pr-${p.number}"></a>`);
  L.push('');
  L.push(`## PR #${p.number} — ${p.title}`);
  L.push('');
  L.push(meta);
  L.push('');
  L.push((p.body || '').trim() || '_Sem descrição._');
  L.push('');
  L.push('---');
  L.push('');
}

fs.writeFileSync(destino, L.join('\n').replace(/\s+$/, '') + '\n', 'utf8');
console.log(`${prs.length} PRs (#${prs[0].number}–#${prs[prs.length - 1].number}) em docs/historico-prs.md`);
if (comTexto > 0) console.log(`ATENCAO: ${comTexto} comentarios/reviews com texto NAO foram exportados.`);
