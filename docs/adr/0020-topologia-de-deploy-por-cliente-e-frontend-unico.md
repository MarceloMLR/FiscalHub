# ADR-0020: Topologia de deploy — backend por-cliente, frontend único, roteamento por subdomínio

- **Status:** Aceito
- **Data:** 2026-07-31

## Contexto

Cada cliente roda uma combinação própria de adapters (entrada/domínio/saída) e, em fiscal, o
isolamento de dados pesa: residência, compliance, e a possibilidade de manter um cliente numa versão
enquanto outro avança. Ao longo de um ciclo de vida haverá mudanças em quatro pontos, com alcances
diferentes: (1) adapter de ERP (um ou vários clientes), (1.5) customização de um cliente específico,
(2) domínio/conector — **sempre todos**, e (3) adapter de saída (só quem usa aquele adapter).

A pergunta que fechou a decisão: com esses alcances, como dar suporte sem cair no trabalho manual de
"atualizei o adapter Avalara, agora descubro na mão quais clientes usam e deployo só neles"?

## Decisão

**Backend: uma instância por cliente (modelo silo).** Cada cliente tem sua própria instância de API +
banco. O isolamento entre tenants é **físico** (bancos separados), não só lógico. Fica aberta a opção
de migrar pro compartilhado depois — o código já escopa tudo por tenant, então a topologia é decisão
de deploy, não de código.

**Um único codebase; adapters selecionados por config.** Todos os adapters vão empacotados na mesma
imagem; o `TenantConnectorProfile` (ADR-0019) decide em runtime quais entram. Atualizar o adapter
Avalara = **um PR na main → uma versão → rollout**, nunca N bases de código. O medo do trabalho manual
se resolve com **metadado + pipeline**: o perfil de conector já é a fonte da verdade de "quem usa qual
adapter", então um pipeline de release consulta isso e faz **fan-out seletivo** pras instâncias que
casam — automático, sem descobrir na mão. Mudança de domínio (alcance = todos) sai em **ondas/canário**
pra controlar o raio de impacto. Customização de um cliente fica atrás de uma porta/estratégia (plugin
daquele cliente), não espalhada no core — assim só mexe naquele cliente.

**Frontend: artefato único (não por-cliente).** O SPA não tem regra de negócio nem isolamento de
dados — é UI + design system compartilhado consumindo uma API já escopada pelo claim. Fazer N deploys
do frontend só recriaria o fan-out sem ganho. Então: **um build**, servido num host estático sob DNS
curinga `*.fiscalhub.com`. Uma mudança de UI = **um deploy**, pra todos.

**Roteamento por subdomínio + API base resolvida em runtime.** `acme.fiscalhub.com` e
`tmsa.fiscalhub.com` carregam o mesmo bundle; o app resolve a API do host em runtime
(`resolveApiBase()` em `dashboard/src/api/config.ts`), na ordem: `VITE_API_BASE_URL` (dev) →
`window.__FISCALHUB__.apiBase` (config injetada por host, recomendada em produção) → convenção
`api.<tenant>.<root>` → `localhost`. **A URL da API não é assada no build** — é o que permite o bundle
único. Adicionar cliente = apontar mais um subdomínio pro mesmo host estático + subir a instância dele.

## Consequências

- **Login cruzado é impossível por construção.** Credenciais da TMSA em `acme.fiscalhub.com` batem na
  API/banco da ACME, que não tem aquele usuário → falha. Não é trava de tela; é isolamento físico. (No
  **dev atual**, com um banco só e dois tenants escopados por claim, o isolamento é lógico: autentica,
  mas só vê o próprio tenant. A diferença some em produção por-cliente.)
- **Suporte sem trabalho manual:** um codebase + adapters por config + o perfil de conector como fonte
  da verdade + pipeline de fan-out por metadado dão o isolamento do silo **sem** a dor de "quem usa o
  quê".
- **Version pinning** fica possível (o silo permite cliente A na vX e B na vY) — se necessário, o
  pipeline guarda a versão implantada por instância.
- **Migração pro compartilhado** continua barata: o código escopa por tenant; mudaria só a topologia
  (um deploy atualiza todos) e aí valeria fixar "subdomínio → tenant" como trava extra.

## Alternativas consideradas

- **Frontend por-cliente (espelhando o backend):** recusado — recria o fan-out do frontend sem
  isolamento a ganhar. Só se justifica se um cliente exigir os estáticos dentro da própria rede/VPC, e
  ainda assim é o **mesmo artefato copiado**, não código separado.
- **Assar `VITE_API_BASE_URL` no build:** forçaria um build por cliente. Trocado por resolução em
  runtime pra manter o bundle único.
- **Backend compartilhado (multi-tenant pooled) já agora:** adiado — o isolamento físico pesa mais no
  fiscal neste momento; a porta pra migrar segue aberta.
