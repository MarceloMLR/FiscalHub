#!/usr/bin/env node
// Confere o pacote ANTES do build: os tres lugares (repositorio, UDE, .rnrproj), privilegio e role,
// as regras de XML do d365/05 e d365/06, e se cada tabela e campo existe no metadado da versao base.
//
// Uso (na raiz do repositorio):
//   node d365/tools/conferir-pacote.js                 -> todas as entidades
//   node d365/tools/conferir-pacote.js FSInventJournalBR FSProductionOrderBR
//   --sem-metadado    pula a checagem de tabela/campo contra o PackagesLocalDirectory (mais rapido)
//   --versao 10.0.2527.160   checa contra outra versao baixada (padrao: a configuracao ativa do VS)
'use strict';
const fs = require('fs');
const path = require('path');
const D = require('./lib/d365');
const X = require('./lib/xml');

const a = D.args();
const cfg = D.cfg;
let erros = 0, avisos = 0;
const saida = [];
const log = (nivel, ent, msg) => { saida.push(`${nivel.padEnd(5)} ${ent ? ent + ': ' : ''}${msg}`); if (nivel === 'ERRO') erros++; if (nivel === 'AVISO') avisos++; };
const norm = s => s.replace(/^﻿/, '').replace(/\r\n/g, '\n').replace(/[ \t]+$/gm, '').trim();

let mc = null;
try { mc = D.metadataConfig(); } catch (e) { log('AVISO', '', e.message); }
if (mc) {
  console.log(`Configuracao ativa do VS: ${path.basename(mc.arquivo)} (versao ${mc.versao})`);
  if (mc.versao !== cfg.versaoBase) log('AVISO', '', `a configuracao ativa do VS e ${mc.versao}, mas o pacote e compilado na ${cfg.versaoBase} (${cfg.versaoBaseNome}). Antes de gerar pacote para cliente, volte para a ${cfg.versaoBase} em Extensions > Dynamics 365 > Configure Metadata.`);
}

const repo = D.pastaModeloRepo();
const ude = mc ? D.pastaModeloUDE(mc) : null;
if (ude && !fs.existsSync(ude)) log('ERRO', '', `pasta do modelo no UDE nao existe: ${ude}`);
const rnr = D.rnrproj();
const rnrTxt = rnr ? D.ler(rnr) : null;
if (!rnr) log('AVISO', '', 'nao achei o .rnrproj (defina D365_RNRPROJ); a checagem do projeto do VS foi pulada.');
else console.log(`Projeto do VS: ${rnr}`);

const pastaEnt = path.join(repo, 'AxDataEntityView');
const todas = fs.readdirSync(pastaEnt).filter(f => f.endsWith('.xml')).map(f => f.slice(0, -4));
const alvo = a._.length ? a._ : todas;

// --- metadado da versao base (tabelas e relacoes)
let idx = null;
if (!a['sem-metadado']) {
  let pld = null;
  try { pld = a.versao ? D.pldDaVersao(a.versao) : (mc ? mc.pld : D.pldDaVersao(cfg.versaoBase)); } catch (e) { log('AVISO', '', e.message); }
  if (pld) {
    console.log(`Metadado de referencia: ${pld}`);
    idx = D.indexar([pld, mc && mc.custom], D.TIPOS_INDICE);
  }
}
const cacheCampos = new Map();
const campos = t => { const k = t.toLowerCase(); if (!cacheCampos.has(k)) cacheCampos.set(k, D.camposDaTabela(idx, t)); return cacheCampos.get(k); };
const tabelaCompartilhada = t => { const arq = ((idx.AxTable && idx.AxTable.get(t.toLowerCase())) || [])[0]; return arq ? /<SaveDataPerCompany>No<\/SaveDataPerCompany>/.test(D.ler(arq)) : false; };

// --- role
const roleRepoArq = path.join(repo, 'AxSecurityRole', cfg.role + '.xml');
const roleTxt = fs.existsSync(roleRepoArq) ? D.ler(roleRepoArq) : '';
const roleRefs = new Set([...roleTxt.matchAll(/<AxSecurityPrivilegeReference>\s*<Name>([^<]+)<\/Name>/g)].map(m => m[1]));
if (!roleTxt) log('ERRO', '', `role nao encontrada no repositorio: ${roleRepoArq}`);
if (ude && roleTxt) {
  const roleUde = path.join(D.pastaModeloUDE(mc), 'AxSecurityRole', cfg.role + '.xml');
  if (!fs.existsSync(roleUde)) log('ERRO', '', `role nao existe no UDE: ${roleUde}`);
  else if (norm(D.ler(roleUde)) !== norm(roleTxt)) log('ERRO', '', 'a role do repositorio e a do UDE estao diferentes (grave nos dois lugares).');
}
if (rnrTxt && !rnrTxt.includes(`Include="AxSecurityRole\\${cfg.role}"`)) log('ERRO', '', `a role ${cfg.role} nao esta no .rnrproj.`);
for (const r of roleRefs) {
  const temArq = fs.existsSync(path.join(repo, 'AxSecurityPrivilege', r + '.xml'));
  if (!temArq && !cfg.privilegiosPadraoNaRole.includes(r)) log('ERRO', '', `a role referencia ${r}, que nao e privilegio do pacote nem esta em privilegiosPadraoNaRole.`);
  if (!temArq && cfg.privilegiosPadraoNaRole.includes(r)) log('INFO', '', `a role ainda referencia o privilegio padrao ${r} (transicao, d365/04 secao 26).`);
}

// --- sobras no UDE
if (ude && fs.existsSync(ude)) {
  for (const tipo of ['AxDataEntityView', 'AxSecurityPrivilege', 'AxSecurityRole']) {
    const d = path.join(ude, tipo);
    if (!fs.existsSync(d)) continue;
    for (const f of fs.readdirSync(d).filter(f => f.endsWith('.xml'))) {
      if (!fs.existsSync(path.join(repo, tipo, f))) log('AVISO', '', `${tipo}\\${f} existe no UDE e nao no repositorio.`);
    }
  }
}

// --- por entidade
for (const nome of alvo) {
  const arqRepo = path.join(pastaEnt, nome + '.xml');
  if (!fs.existsSync(arqRepo)) { log('ERRO', nome, `nao existe em ${pastaEnt}`); continue; }
  const txt = D.ler(arqRepo);
  const antes = erros + avisos;

  // tres lugares
  if (ude) {
    const arqUde = path.join(ude, 'AxDataEntityView', nome + '.xml');
    if (!fs.existsSync(arqUde)) log('ERRO', nome, 'nao existe no UDE (o build le de la).');
    else if (norm(D.ler(arqUde)) !== norm(txt)) {
      const maisNovo = fs.statSync(arqUde).mtimeMs > fs.statSync(arqRepo).mtimeMs ? 'UDE' : 'repositorio';
      log('ERRO', nome, `repositorio e UDE diferentes (o mais novo e o do ${maisNovo}).`);
    }
  }
  const priv = nome + 'View';
  const arqPriv = path.join(repo, 'AxSecurityPrivilege', priv + '.xml');
  if (!fs.existsSync(arqPriv)) log('ERRO', nome, `falta o privilegio ${priv} no repositorio.`);
  else {
    const p = D.ler(arqPriv);
    if (!new RegExp(`<Read>Allow</Read>\\s*</Grant>\\s*<Name>${nome}</Name>`).test(p)) log('ERRO', nome, `o privilegio ${priv} nao da Read para ${nome}.`);
    if (ude) {
      const pu = path.join(ude, 'AxSecurityPrivilege', priv + '.xml');
      if (!fs.existsSync(pu)) log('ERRO', nome, `privilegio ${priv} nao existe no UDE.`);
      else if (norm(D.ler(pu)) !== norm(p)) log('ERRO', nome, `privilegio ${priv}: repositorio e UDE diferentes.`);
    }
  }
  if (!roleRefs.has(priv)) log('ERRO', nome, `a role ${cfg.role} nao referencia ${priv} (o OData responde 403).`);
  if (rnrTxt) {
    if (!rnrTxt.includes(`Include="AxDataEntityView\\${nome}"`)) log('ERRO', nome, 'nao esta no .rnrproj (nao entra no build).');
    if (!rnrTxt.includes(`Include="AxSecurityPrivilege\\${priv}"`)) log('ERRO', nome, `o privilegio ${priv} nao esta no .rnrproj.`);
  }

  // propriedades
  const doc = X.parse(txt);
  const ent = X.filho(doc, 'AxDataEntityView');
  if (!ent) { log('ERRO', nome, 'XML sem AxDataEntityView.'); continue; }
  const prop = n => X.texto(ent, n);
  if (prop('Name') !== nome) log('ERRO', nome, `Name no XML e ${prop('Name')}, diferente do arquivo.`);
  if (prop('PublicEntityName') !== nome) log('AVISO', nome, `PublicEntityName e ${prop('PublicEntityName')} (convencao: igual ao Name).`);
  if (prop('PublicCollectionName') !== nome + 's') log('AVISO', nome, `PublicCollectionName e ${prop('PublicCollectionName')} (convencao: ${nome}s).`);
  if (prop('IsPublic') !== 'Yes') log('ERRO', nome, 'IsPublic precisa ser Yes (senao nao aparece no OData).');
  if (prop('IsReadOnly') !== 'Yes') log('ERRO', nome, 'IsReadOnly precisa ser Yes (o pacote e so leitura).');
  if (prop('DataManagementEnabled') === 'Yes') log('ERRO', nome, 'DataManagementEnabled precisa ser No (d365/05 secao 4); ausente vale No.');
  if (prop('CountryRegionCodes') !== 'BR') log('AVISO', nome, 'sem CountryRegionCodes BR na entidade.');
  if (!nome.startsWith('FS')) log('ERRO', nome, 'entidade do pacote comeca com FS.');

  // fontes de dados
  const vm = X.filho(ent, 'ViewMetadata');
  const fontes = new Map(); // nome -> { tabela, pai, no }
  const andar = (no, pai) => {
    for (const f of no.filhos) {
      if (f.nome === 'AxQuerySimpleRootDataSource' || f.nome === 'AxQuerySimpleEmbeddedDataSource') {
        const dsNome = X.texto(f, 'Name');
        fontes.set(dsNome, { tabela: X.texto(f, 'Table'), pai, no: f });
        const sub = X.filho(f, 'DataSources');
        if (sub) andar(sub, dsNome);
      }
    }
  };
  if (vm) andar(X.filho(vm, 'DataSources') || vm, null);
  if (!fontes.size) log('ERRO', nome, 'sem fonte de dados no ViewMetadata.');
  const raiz = [...fontes.entries()].find(([, v]) => v.pai === null);

  for (const [dsNome, ds] of fontes) {
    if (ds.pai === null) continue;
    const jm = X.texto(ds.no, 'JoinMode');
    if (!jm) {
      const intencional = (cfg.juncoesInternasIntencionais || []).some(j => j.toLowerCase() === (ds.tabela || '').toLowerCase() || j.toLowerCase() === (nome + ':' + dsNome).toLowerCase());
      if (intencional) log('INFO', nome, `fonte ${dsNome} (${ds.tabela}) sem JoinMode = InnerJoin, de proposito (juncoesInternasIntencionais).`);
      else log('AVISO', nome, `fonte ${dsNome} (${ds.tabela}) sem <JoinMode>: vale InnerJoin e anula o OuterJoin do pai (d365/05 secao 2). Confirme se e de proposito.`);
    }
    if (idx) {
      const tabPai = fontes.get(ds.pai) && fontes.get(ds.pai).tabela;
      for (const rel of X.todos(X.filho(ds.no, 'Relations') || { filhos: [] }, 'AxQuerySimpleDataSourceRelation')) {
        const jr = X.texto(rel, 'JoinRelationName');
        if (jr) {
          const temFilha = D.relacoesDaTabela(idx, ds.tabela).has(jr.toLowerCase());
          const temPai = tabPai && D.relacoesDaTabela(idx, tabPai).has(jr.toLowerCase());
          if (!temFilha && !temPai) log('ERRO', nome, `fonte ${dsNome}: relacao "${jr}" nao existe em ${ds.tabela} nem em ${tabPai} (o JoinRelationName e o Name ou o RelatedTableRole de uma relacao das duas tabelas; o RelatedTableRole costuma ser o nome da tabela relacionada; d365/06 secao 4).`);
        }
        const fPai = X.texto(rel, 'Field'), fFilha = X.texto(rel, 'RelatedField'), jds = X.texto(rel, 'JoinDataSource');
        if (fPai && fFilha) {
          const tP = (fontes.get(jds) || fontes.get(ds.pai) || {}).tabela;
          const cP = tP && campos(tP), cF = ds.tabela && campos(ds.tabela);
          if (cP && !cP.has(fPai.toLowerCase())) log('ERRO', nome, `fonte ${dsNome}: campo ${fPai} nao existe em ${tP} (Field e o campo da fonte pai).`);
          if (cF && !cF.has(fFilha.toLowerCase())) log('ERRO', nome, `fonte ${dsNome}: campo ${fFilha} nao existe em ${ds.tabela} (RelatedField e o campo da tabela filha).`);
        }
      }
    }
  }
  if (idx) {
    for (const [dsNome, ds] of fontes) if (ds.tabela && !campos(ds.tabela)) log('ERRO', nome, `fonte ${dsNome}: tabela/view ${ds.tabela} nao existe no metadado de referencia.`);
    if (raiz && raiz[1].tabela && campos(raiz[1].tabela) && tabelaCompartilhada(raiz[1].tabela) && prop('PrimaryCompanyContext') === 'DataAreaId') {
      log('ERRO', nome, `a tabela raiz ${raiz[1].tabela} e compartilhada (sem empresa): tire o PrimaryCompanyContext DataAreaId ou use o campo da empresa da junção.`);
    }
  }

  // campos
  const nomes = new Set();
  const flds = X.filhos(X.filho(ent, 'Fields') || { filhos: [] }, 'AxDataEntityViewField');
  for (const f of flds) {
    const fn = X.texto(f, 'Name'), df = X.texto(f, 'DataField'), ds = X.texto(f, 'DataSource'), am = X.texto(f, 'AccessModifier');
    const tipo = f.attrs['i:type'] || '';
    if (nomes.has((fn || '').toLowerCase())) log('ERRO', nome, `campo ${fn} duplicado.`);
    nomes.add((fn || '').toLowerCase());
    if (/^(RecId|DataAreaId|Partition|RecVersion|ModifiedDateTime|CreatedDateTime|ModifiedBy|CreatedBy)$/i.test(fn || '')) log('ERRO', nome, `campo ${fn}: campo de sistema precisa de alias (ex.: ${nome.replace(/^FS|BR$/g, '')}RecId, SysModifiedDateTime).`);
    const dimensao = /Dimension$/i.test(df || '') && !/DisplayValue/i.test(fn || '');
    if (am === 'Private' && !dimensao) log('ERRO', nome, `campo ${fn} esta Private: some do OData em silencio (d365/05 secao 1).`);
    if (dimensao && am !== 'Private' && am !== 'Internal') log('AVISO', nome, `campo ${fn} e RecId de dimensao: o Best Practice pede Private ou Internal; exponha o DisplayValue.`);
    if (tipo.includes('Mapped') && !tipo.includes('Unmapped')) {
      if (!fontes.has(ds)) log('ERRO', nome, `campo ${fn}: fonte ${ds} nao existe na entidade.`);
      else if (idx) {
        const t = fontes.get(ds).tabela; const c = t && campos(t);
        if (c && df && !c.has(df.toLowerCase())) log('ERRO', nome, `campo ${fn}: ${t}.${df} nao existe na versao de referencia.`);
      }
    }
  }
  // chave
  const pk = prop('PrimaryKey');
  const chave = X.filhos(X.filho(ent, 'Keys') || { filhos: [] }, 'AxDataEntityViewKey').find(k => X.texto(k, 'Name') === pk);
  if (!chave) log('ERRO', nome, `PrimaryKey ${pk} nao existe em <Keys>.`);
  else for (const kf of X.todos(chave, 'AxDataEntityViewKeyField')) {
    const c = X.texto(kf, 'DataField');
    if (!nomes.has((c || '').toLowerCase())) log('ERRO', nome, `chave ${pk}: campo ${c} nao existe na entidade.`);
  }
  if (erros + avisos === antes) saida.push(`OK    ${nome} (${flds.length} campos, ${fontes.size} fontes)`);
}

console.log('');
saida.forEach(l => console.log(l));
console.log(`\n${alvo.length} entidade(s) | ${erros} erro(s) | ${avisos} aviso(s)`);
process.exitCode = erros ? 1 : 0;
