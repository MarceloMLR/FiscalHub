# ADR-0007: Poll de status e idempotência por estado

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

O despacho é assíncrono (ADR-0003): o envio devolve um GUID e o documento fica `Submitted`. O
resultado real (carregado/erro) só se conhece **consultando** o GUID. Sem ninguém consultando, o
documento ficava `Submitted` para sempre — e, pior, a idempotência (que bloqueia em `Submitted`)
impedia o cliente de **reenviar uma nota que a plataforma rejeitou** depois de corrigi-la.

## Decisão

- **`StatusPoller` (lógica pura)** lista os documentos em voo, consulta cada um via
  `CheckStatusAsync` e grava o desfecho: `Confirmed`, `IntegrationError`, ou — se a plataforma não
  responde após um limite de consultas (o **204 eterno**) — `Unconfirmed`. Um `BackgroundService`
  num timer só chama `PollOnceAsync`; a lógica fica testável, fora da casca.
- **Contador de tentativas** (`Attempts`) por documento; ao atingir `MaxAttempts`, marca
  `Unconfirmed` em vez de consultar para sempre.
- **Idempotência por estado, não por existência:** `Confirmed` e `Submitted` (em voo) bloqueiam
  reenvio; `IntegrationError` e `Unconfirmed` são **itens em aberto** e liberam o reprocesso. Um
  reenvio faz upsert na mesma linha `(tenant, chave)` e **reinicia** `Attempts`.
- Lista ordenada por `Id` (FIFO). `DateTimeOffset` não ordena no SQLite dos testes; `Id` ordena nos
  dois providers.

## Alternativas consideradas

- **Webhook/callback da plataforma** — não temos controle sobre a Avalara para receber push; o
  modelo real é consulta.
- **Marcar tudo como confirmado no envio** — perde o resultado real; a nota rejeitada passaria como
  ok.
- **Consultar no mesmo request do envio** — trava a esteira esperando um resultado que pode demorar
  muito (ou nunca vir).

## Consequências

- Exige o poll rodando (hosted service local; na Etapa de Service Bus pode virar uma function
  agendada). O ciclo é resiliente: exceção numa passada é logada e a próxima segue.
- O poll está atado a `GoodsInvoice` por ora (um poller por tipo). Generalizar por tipo de documento
  fica para quando entrar o segundo tipo.
- O mock local sempre devolve `carregado`, então ao vivo tudo confirma sozinho. Exercitar erro e
  reprocesso ao vivo exige um jeito de forçar `erro` no mock (hoje coberto por teste unitário).
