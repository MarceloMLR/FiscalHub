# STATUS — FiscalHub

Documento de handoff entre sessões/máquinas. Atualizado ao fim de cada expediente.
Para retomar: leia este arquivo + os [ADRs](adr/) + o [brief de infra](infrastructure-brief.md).
(O "como trabalhamos" — Modo Mentor — vem do prompt inicial; re-cole ao abrir uma sessão nova.)

**Última atualização:** 2026-07-23

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
