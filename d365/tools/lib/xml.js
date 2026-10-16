// Parser de XML minimo, so para o metadado do F&O (elementos e texto; ignora CDATA, comentarios e declaracao).
'use strict';
function parse(xml) {
  const limpo = xml.replace(/^﻿/, '')
    .replace(/<!\[CDATA\[[\s\S]*?\]\]>/g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<\?[\s\S]*?\?>/g, '');
  const raiz = { nome: '#raiz', filhos: [], texto: '', pai: null };
  let atual = raiz;
  const re = /<(\/?)([\w:.\-]+)((?:\s+[\w:.\-]+\s*=\s*"[^"]*")*)\s*(\/?)>|([^<]+)/g;
  let m;
  while ((m = re.exec(limpo))) {
    if (m[5] !== undefined) { atual.texto += m[5]; continue; }
    const [, fecha, nome, attrs, auto] = m;
    if (fecha) { if (atual.pai) atual = atual.pai; continue; }
    const no = { nome, attrs: {}, filhos: [], texto: '', pai: atual };
    for (const a of (attrs || '').matchAll(/([\w:.\-]+)\s*=\s*"([^"]*)"/g)) no.attrs[a[1]] = a[2];
    atual.filhos.push(no);
    if (!auto) atual = no;
  }
  return raiz;
}
const filho = (no, nome) => (no && no.filhos.find(f => f.nome === nome)) || null;
const filhos = (no, nome) => (no ? no.filhos.filter(f => f.nome === nome) : []);
const texto = (no, nome) => { const f = filho(no, nome); return f ? f.texto.trim() : null; };
function todos(no, nome, acc = []) {
  for (const f of no.filhos) { if (f.nome === nome) acc.push(f); todos(f, nome, acc); }
  return acc;
}
module.exports = { parse, filho, filhos, texto, todos };
