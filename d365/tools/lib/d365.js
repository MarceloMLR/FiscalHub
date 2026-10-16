// Funcoes comuns das ferramentas do d365/tools.
// Regras: o token nunca e impresso nem gravado em arquivo; ele vem do `az login` de quem roda.
'use strict';
const fs = require('fs');
const path = require('path');
const os = require('os');
const { execSync } = require('child_process');

const DIR_TOOLS = path.resolve(__dirname, '..');
const DIR_D365 = path.resolve(DIR_TOOLS, '..');
const DIR_REPO = path.resolve(DIR_D365, '..');
const cfg = JSON.parse(fs.readFileSync(path.join(DIR_TOOLS, 'd365.config.json'), 'utf8').replace(/^﻿/, ''));

function args(argv = process.argv.slice(2)) {
  const out = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const k = a.slice(2);
      const v = argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[++i] : true;
      out[k] = v;
    } else out._.push(a);
  }
  return out;
}

// Token do Entra ID pela sessao do Azure CLI. Fica so na memoria do processo.
function token(resource) {
  const r = resource || cfg.environmentUrl;
  try {
    const t = execSync(`az account get-access-token --resource "${r}" --query accessToken -o tsv`,
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
    if (!t) throw new Error('vazio');
    return t;
  } catch (e) {
    throw new Error(`Sem token do az para ${r}. Rode "az login" com um usuario que tenha acesso ao ambiente.`);
  }
}

function regValue(name) {
  try {
    const o = execSync(`reg query "HKCU\\Software\\Microsoft\\Dynamics\\AX7\\Development\\Configurations" /v ${name}`,
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    const m = o.match(new RegExp(name + '\\s+REG_\\w+\\s+(.+)'));
    return m ? m[1].trim() : null;
  } catch { return null; }
}

// Configuracao de metadado ativa no Visual Studio (Extensions > Dynamics 365 > Configure Metadata).
function metadataConfig() {
  const arquivo = process.env.D365_XPPCONFIG || regValue('CurrentMetadataConfig');
  if (!arquivo || !fs.existsSync(arquivo)) {
    throw new Error('Nao achei a configuracao de metadado ativa do VS. Defina D365_XPPCONFIG com o caminho do .json em %LOCALAPPDATA%\\Microsoft\\Dynamics365\\XPPConfig.');
  }
  const j = JSON.parse(fs.readFileSync(arquivo, 'utf8').replace(/^﻿/, ''));
  const versao = (path.basename(arquivo).match(/___([\d.]+)\.json$/) || [])[1] || null;
  const pld = (j.ReferencePackagesPaths && j.ReferencePackagesPaths[0]) || j.FrameworkDirectory;
  const custom = process.env.D365_CUSTOM_METADATA || j.ModelStoreFolder;
  return { arquivo, versao, pld, custom };
}

// PackagesLocalDirectory de uma versao baixada (ex.: 10.0.2527.160).
function pldDaVersao(versao) {
  const p = path.join(process.env.LOCALAPPDATA || '', 'Microsoft', 'Dynamics365', versao, 'PackagesLocalDirectory');
  if (!fs.existsSync(p)) throw new Error(`Versao ${versao} nao baixada: ${p} nao existe.`);
  return p;
}

function versoesBaixadas() {
  const base = path.join(process.env.LOCALAPPDATA || '', 'Microsoft', 'Dynamics365');
  if (!fs.existsSync(base)) return [];
  return fs.readdirSync(base).filter(n => /^\d+\.\d+\.\d+\.\d+$/.test(n) && fs.existsSync(path.join(base, n, 'PackagesLocalDirectory')));
}

// Pasta do modelo no UDE: <custom>\<pacote>\<pacote>
function pastaModeloUDE(mc = metadataConfig()) {
  return path.join(mc.custom, cfg.pacote, cfg.pacote);
}

function pastaModeloRepo() { return process.env.D365_REPO_MODEL || path.join(DIR_D365, 'model'); }

// O .rnrproj fica fora do repositorio (d365/06, secao 2).
function rnrproj() {
  if (process.env.D365_RNRPROJ) return fs.existsSync(process.env.D365_RNRPROJ) ? process.env.D365_RNRPROJ : null;
  const desk = path.join(os.homedir(), 'Desktop');
  const alvo = cfg.pacote.toLowerCase() + '.rnrproj';
  const fila = [];
  try { for (const n of fs.readdirSync(desk)) if (/^Projects/i.test(n)) fila.push([path.join(desk, n), 0]); } catch { return null; }
  while (fila.length) {
    const [d, prof] = fila.shift();
    let itens = [];
    try { itens = fs.readdirSync(d, { withFileTypes: true }); } catch { continue; }
    for (const it of itens) {
      const p = path.join(d, it.name);
      if (it.isFile() && it.name.toLowerCase() === alvo) return p;
      if (it.isDirectory() && prof < 4 && !/^(node_modules|\.git|bin|obj|\.vs)$/i.test(it.name)) fila.push([p, prof + 1]);
    }
  }
  return null;
}

async function odataGet(url, tok, headers = {}) {
  const r = await fetch(url, { headers: Object.assign({ Authorization: 'Bearer ' + tok, Accept: 'application/json' }, headers) });
  const text = await r.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* texto puro, ex.: $count */ }
  return { status: r.status, ok: r.ok, json, text };
}

function dicaStatus(status) {
  return ({
    400: 'campo ou filtro desconhecido (campo Private, pacote antigo sem o campo, ou enum sem o nome qualificado)',
    401: 'o app/usuario nao esta cadastrado no F&O (Aplicativos do Microsoft Entra ID) ou o token expirou',
    403: 'falta a role FiscosysHubIntegration no usuario ou o privilegio da entidade na role',
    404: 'entidade nao publicada (deploy/sync nao rodou, ou o nome do entity set esta errado: e o plural)',
    500: 'erro do servidor: depois de deploy, costuma ser sincronizacao parcial (rodar a sincronizacao completa)',
    503: 'ambiente fora do ar (sync, manutencao ou parado)'
  })[status] || '';
}

// Indice do metadado: nome (minusculo) -> lista de arquivos, para os tipos pedidos.
function indexar(raizes, tipos) {
  const idx = {};
  for (const t of tipos) idx[t] = new Map();
  for (const raiz of raizes) {
    if (!raiz || !fs.existsSync(raiz)) continue;
    for (const pkg of fs.readdirSync(raiz, { withFileTypes: true })) {
      if (!pkg.isDirectory()) continue;
      let modelos = [];
      try { modelos = fs.readdirSync(path.join(raiz, pkg.name), { withFileTypes: true }).filter(x => x.isDirectory()); } catch { continue; }
      for (const mo of modelos) {
        for (const t of tipos) {
          const dir = path.join(raiz, pkg.name, mo.name, t);
          let arqs = [];
          try { arqs = fs.readdirSync(dir); } catch { continue; }
          for (const a of arqs) {
            if (!a.endsWith('.xml')) continue;
            const nome = a.slice(0, -4);
            const chave = (t.endsWith('Extension') ? nome.split('.')[0] : nome).toLowerCase();
            if (!idx[t].has(chave)) idx[t].set(chave, []);
            idx[t].get(chave).push(path.join(dir, a));
          }
        }
      }
    }
  }
  return idx;
}

const ler = p => fs.readFileSync(p, 'utf8').replace(/^﻿/, '');
const tag = (xml, nome) => { const m = xml.match(new RegExp('<' + nome + '>([^<]*)</' + nome + '>')); return m ? m[1] : null; };

// Campos de uma tabela (ou view, ou data entity usada como fonte), com extensoes e heranca (Extends).
const CAMPOS_SISTEMA = ['recid', 'dataareaid', 'partition', 'recversion', 'tableid', 'modifieddatetime', 'createddatetime', 'modifiedby', 'createdby', 'modifiedtransactionid', 'createdtransactionid', 'relationtype', 'instancerelationtype'];
// (?=[\s>]) evita casar <AxTableFieldGroup>: nome de grupo de campos nao e campo.
const TIPOS_FONTE = [
  ['AxTable', 'AxTableExtension', /<AxTableField(?=[\s>])[^>]*>\s*<Name>([^<]+)<\/Name>/g],
  ['AxView', 'AxViewExtension', /<AxViewField(?=[\s>])[^>]*>\s*<Name>([^<]+)<\/Name>/g],
  ['AxDataEntityView', 'AxDataEntityViewExtension', /<AxDataEntityViewField(?=[\s>])[^>]*>\s*<Name>([^<]+)<\/Name>/g]
];
const TIPOS_INDICE = TIPOS_FONTE.flatMap(([t, e]) => [t, e]);
function camposDaTabela(idx, tabela, visitadas = new Set()) {
  const k = tabela.toLowerCase();
  if (visitadas.has(k)) return new Set();
  visitadas.add(k);
  for (const [tipo, ext, re] of TIPOS_FONTE) {
    const arqs = idx[tipo] && idx[tipo].get(k);
    if (!arqs) continue;
    const s = new Set(CAMPOS_SISTEMA);
    s.nomes = new Map(); // minusculo -> nome original
    for (const a of [...arqs, ...((idx[ext] && idx[ext].get(k)) || [])]) {
      const x = ler(a);
      for (const m of x.matchAll(re)) { s.add(m[1].toLowerCase()); s.nomes.set(m[1].toLowerCase(), m[1]); }
      const base = tag(x, 'Extends');
      if (base) { const b = camposDaTabela(idx, base, visitadas); if (b) { b.forEach(f => s.add(f)); if (b.nomes) b.nomes.forEach((v, k) => s.nomes.set(k, v)); } }
    }
    return s;
  }
  return null;
}

// Nomes que o JoinRelationName aceita: o Name e o RelatedTableRole de cada relacao
// (o RelatedTableRole costuma ser o nome da tabela relacionada; e ele que o query usa).
function relacoesDaTabela(idx, tabela) {
  const s = new Set();
  const k = tabela.toLowerCase();
  for (const [tipo, ext] of TIPOS_FONTE) {
    for (const a of [...((idx[tipo] && idx[tipo].get(k)) || []), ...((idx[ext] && idx[ext].get(k)) || [])]) {
      for (const m of ler(a).matchAll(/<AxTableRelation[\s\S]*?<\/AxTableRelation>/g)) {
        const n = tag(m[0], 'Name'), r = tag(m[0], 'RelatedTableRole');
        if (n) s.add(n.toLowerCase());
        if (r) s.add(r.toLowerCase());
      }
    }
  }
  return s;
}
module.exports = {
  cfg, DIR_TOOLS, DIR_D365, DIR_REPO, args, token, metadataConfig, pldDaVersao, versoesBaixadas,
  pastaModeloUDE, pastaModeloRepo, rnrproj, odataGet, dicaStatus, indexar, ler, tag, camposDaTabela, relacoesDaTabela, TIPOS_INDICE
};
