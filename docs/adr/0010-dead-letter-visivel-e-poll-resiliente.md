# ADR-0010: Dead-letter visível e poll resiliente

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

Duas lacunas apareceram ao rodar o fluxo assíncrono de ponta a ponta:

1. Uma mensagem cujo **processamento falha repetidamente** (locator inválido, SQL/Blob fora) esgota
   as tentativas e o Service Bus a move pra dead-letter (ADR-0008) — mas ela **sumia de vista**:
   nada no store refletia a falha.
2. O poll de status consultava os documentos em voo em lote, mas uma falha na consulta de **um**
   documento (ex.: um GUID que a plataforma devolve 404) estourava a passada inteira e **travava a
   confirmação de todos os outros**.

## Decisão

- **DLQ visível:** um `DeadLetterTriggerService` assina a dead-letter da fila e registra cada
  mensagem esgotada como `IntegrationStatus.DeadLettered`, com o motivo do Service Bus. A lógica
  fica num `DeadLetterHandler` testável. Não reprocessa — só torna a falha rastreável. Como erro e
  unconfirmed, `DeadLettered` **não bloqueia** um reenvio (é item em aberto).
- **404 = pendente:** o adapter da Avalara trata `404` como o `204` — a plataforma ainda não conhece
  o identificador → segue pendente, não é erro. A consulta se repete e, no limite, vira
  `Unconfirmed`. Um GUID problemático deixa de estourar exceção.
- **Isolar falha por documento:** o `StatusPoller` envolve a consulta de cada documento num
  try/catch. Uma falha inesperada em um não derruba o lote — os outros seguem sendo confirmados.

## Alternativas consideradas

- **Reprocessar automaticamente da DLQ** — arriscado sem entender a causa da falha; podia entrar em
  loop. Tornar visível e deixar o reenvio ser uma decisão (manual ou corrigida) é mais seguro.
- **Deixar o 404 estourar e confiar só no try/catch do poll** — o try/catch resolveria o travamento
  do lote, mas o documento nunca avançaria de estado; tratar 404 como pendente o leva a `Unconfirmed`
  pelo caminho normal. Os dois juntos são cinto e suspensório.

## Consequências

- Falhas de processamento persistentes ficam visíveis como `DeadLettered` (com motivo), prontas pro
  dashboard e pra um reenvio consciente.
- O poll fica resiliente: nenhum documento isolado trava a confirmação do lote.
- O `DeadLetterHandler` grava a referência mesmo que o documento nunca tenha tido linha (falhou antes
  do registro) — um upsert por `(tenant, chave)`.
