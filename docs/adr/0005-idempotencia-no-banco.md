# ADR-0005: Idempotência no banco (índice único) para o rastreio

- **Status:** Aceito
- **Data:** 2026-07-21

## Contexto

O `IProcessingStore` precisa garantir que o mesmo documento não seja processado duas vezes,
mesmo com reentrega do Service Bus e workers concorrentes.

## Decisão

- `ProcessedDocument` persistido em SQL (EF Core), com **índice ÚNICO em `(TenantId, NaturalKey)`**.
  O banco — não a memória — garante uma linha por documento.
- `AlreadySubmittedAsync` verifica se existe linha com status `Submitted`/`Confirmed`.
- `RecordSubmissionAsync`/`RecordRejectionAsync` fazem **upsert**.
- Enums persistidos como texto (legíveis para auditoria).
- Testes em **SQLite in-memory** (respeita o índice único de verdade); produção em SQL Server + migrations.

## Alternativas consideradas

- **Checar em memória** — não sobrevive a múltiplos workers/processos.
- **EF InMemory nos testes** — não aplica a constraint única; não provaria a idempotência.

## Consequências

- **TOCTOU conhecido:** o fluxo checar → despachar → gravar permite, sob concorrência real, dois
  despachos do mesmo documento (a constraint pega só na gravação). Blindagem total seria
  *reserve-first* (inserir `Pending` antes do despacho) + chave de idempotência na requisição ao
  destino. Fica como hardening de produção, fora do escopo de demo.
- Trocar SQLite (teste) por SQL Server (produção) não muda o código do store — só o provider e as migrations.
