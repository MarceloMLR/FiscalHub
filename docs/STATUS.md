# STATUS — FiscalHub

Documento de handoff entre sessões/máquinas. Atualizado ao fim de cada expediente.
Para retomar: leia este arquivo + os [ADRs](adr/) + o [brief de infra](infrastructure-brief.md).
(O "como trabalhamos" — Modo Mentor — vem do prompt inicial; re-cole ao abrir uma sessão nova.)

**Última atualização:** 2026-09-01

## Ferramentas da sessão

- **`gh` CLI autenticado** — o Claude cria PR, mostra o diff e mergeia (sempre com aval do Marcelo).
- **Windows MCP (PowerShell)** — o Claude roda `dotnet build`/`dotnet test` e comandos git direto.
- **Azure MCP** — consulta de recursos, Bicep, best practices e **preços** (útil na fase de infra).

## Onde estamos

**Núcleo (Marco 1) completo e testado, sem Azure:** domínio `GoodsInvoice` (NF-e 55) com a Reforma
(IBS/CBS/IS), envelope fino, 5 portas, esteira `ProcessAsync` (idempotência → busca → validação →
envio → registro), `NfeXmlParser`, `GoodsInvoiceToAvalara`, `GoodsInvoiceValidator`.

**Adapters e infra reais:**
- `XmlGoodsInvoiceSource` — lê XML do Blob via `IBlobReader`.
- `AvalaraComplianceDispatcher` + mock, tradução de status, 204, **token cache** por tenant (semáforo).
- `SqlProcessingStore` — EF Core, idempotência por índice único `(TenantId, NaturalKey)`.
- Composição no `FiscalHub.Host` + `docker-compose` (Azurite + SQL).

**E2E local (Etapa 1) RODANDO:** `POST /ingest` → lê XML do Blob → valida → despacha pro mock → grava
`Submitted` no SQL. Idempotência confirmada ao vivo (2º POST não duplica linha).

**35 testes verdes. 5 ADRs.** `gh` + Windows MCP em uso (Claude roda build/test/git/docker).

## Próximos passos

1. **Etapa 2 — Service Bus:** emulador do Service Bus + trigger (a casca chama `ProcessAsync`) +
   ingresso (drop de XML no Blob → Event Grid → enfileira). Retry/DLQ nativos.
2. Poll worker de status (limite de consulta + status `Unconfirmed` — ver brief).
3. Roteamento por tipo no composition root; Dashboard React.
4. Marco 2: CT-e (57) e NFS-e como tipos novos (prova de extensibilidade).

## Decisões recentes

- Estilo de envio: chamada direta no pipeline (fila de saída fica como evolução) — ADR-0004.
- 204 no `CheckStatus` = ainda pendente (`Submitted`), não erro.
- Poll terá limite (deadline + tentativas) e status `Unconfirmed` para "sem retorno da plataforma"
  (≠ rejeição de negócio) — a implementar.
- Commits em PT (sem acento); código/identificadores em inglês; termos fiscais BR mantidos.

## Threads abertas

- Documento na DLQ não grava `IntegrationError` no store — resolver na fatia de dashboard/DLQ.
- Seção "como construí com agentes" no README (narrativa do diferencial).
- (Opcional) mock simular 204 num primeiro GET, para demonstrar o fluxo assíncrono localmente.

---

## Sessão 2026-08/09 — Conector D365 F&O + adoção de OpenSpec (handoff)

**Conector D365 (X++) — estado atual.** Os 3 objetos no model `FiscalHubIntegration`
(pasta `Fiscal/BusinessEvents`) foram **reforçados** e validados:
- `FS_FiscalDocument_BR_Extension` (CoC na tabela) cobre **`update()` E `doUpdate()`** e dispara na
  **transição** de status para **Approved** e **Cancelled** (compara `this.orig().Status`).
- `FS_FiscalDocStatusChangedContract` — payload enriquecido: `Company, FiscalDocumentRecId (RefRecId),
  FiscalDocumentNumber, FiscalDocumentSeries, FiscalDocumentDate (ISO), Status`.
- `FS_FiscalDocStatusChangedBusinessEvent` — `newFromDoc(FiscalDocument_BR)` guarda o buffer;
  `buildContract()` monta o contract.
- Campos reais: status = **`Status`** (enum `FiscalDocumentStatus_BR`; cancelamento simples =
  **`Cancelled`**, há `CancelledBySubstitution` à parte, fora por ora), número = `FiscalDocumentNumber`,
  série = `FiscalDocumentSeries`, empresa = `dataAreaId`.
- **Testado** (table browser, two-step Created→Approved) na **entrada** (nota 000001) e na **saída** —
  evento caiu no endpoint **`test`**. Compila sem problemas. **Deployable package já gerado.**
- Cancelamento: código pronto, **teste em runtime pendente** (deixado pro fluxo completo).

**Ambiente D365 (importante).** `FiscosysDev` é **Unified Developer** (gerenciado pelo **PPAC**, **sem
projeto LCS**). Deploy de dev = **"Deploy Models to Online Environment"**. **Não existe** LCS Asset
Library / runbook aqui; aplicar package de verdade exige um **Sandbox Standard**. Detalhes em
`d365/03-deploy-e-promocao.md`.

**Write-path (razão do reforço).** Requisito: disparar sempre que a nota ficar Approved/Cancelled, não
importa como. `update()`/`doUpdate()` cobrem record-based; set-based costuma degradar pra linha-a-linha
quando `update()` está estendido. Furo residual (SQL cru / skipDataMethods) → **rede de segurança = poll
de reconciliação** (backlog). **Isto ATUALIZA a recomendação antiga do `d365/02`** (que apontava data
event/polling como caminho principal).

**Promoção estilo cliente (ISV).** 1 código → 1 build → 1 package → **N clientes**; variação por cliente
é **configuração** (endpoint do Service Bus, conta Avalara), não código. Build no Azure DevOps gera o
package; release aplica no ambiente do cliente via **service connection** (credencial que o cliente
autoriza). Sandbox costuma ser automatizado; **produção** o cliente aplica/aprova (gate). Ver
`d365/03-deploy-e-promocao.md`.

**OpenSpec adotado (SDD).** `openspec/` (config.yaml com contexto do FiscalHub) + `.claude/commands/opsx/*`
(gitignored, local) + `CLAUDE.md` na raiz. Fluxo: **alinhar spec com a IA → `/opsx:propose` no Claude Code
implementa → revisar**. Adoção **incremental** (não backfillar specs do legado). Existe uma skill de
bootstrap de projeto (`discovery-project-sdd`, salva na conta do usuário) que gera discovery.md +
apresentacao.html (visual p/ time funcional) + config.yaml + CLAUDE.md.

**Auditoria do repo (sem viés).** Base forte (hexagonal, 22 ADRs, conventional commits, PRs, ~89 testes).
Lacunas: **(1) sem CI** (`.github/workflows` vazio — maior gap); (2) testes finos em `Inbound.Xml` (só 4)
e **sem teste no Host**; (3) X++ sem teste automatizado. Maior retorno: **montar CI** (build + test + 0
warnings no PR).

**Pendências.** Poll de reconciliação (backlog); definir promoção real (pipeline Azure DevOps / sandbox);
validar pós-deploy; **montar CI**; teste runtime do Cancelled; **entidades novas em andamento** (Marcelo
criando no VS).
