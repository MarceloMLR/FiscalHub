# ADR-0008: Gatilho por Service Bus (fila + claim-check)

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

Na Etapa 1 o `/ingest` processava a esteira **de forma síncrona** — bom para provar o fluxo, ruim
para produção: um pico de notas trava o request, e uma falha transitória (Blob, SQL, Avalara) perde
o trabalho ou exige retry manual. Integração fiscal precisa de um buffer durável entre receber e
processar.

## Decisão

- O `/ingest` passa a **enfileirar** uma mensagem fina (claim-check): só a `DocumentReference`
  (tenant, tipo, chave, locator). Responde `202 Accepted`. O documento pesado continua no Blob.
- Um **consumidor** (`ServiceBusTriggerService`, hosted service com `ServiceBusProcessor`) lê a
  mensagem e chama a esteira via `IDocumentPipeline<GoodsInvoice>`. A lógica de desserializar e
  montar o contexto fica num `QueuedDocumentProcessor` testável, fora da casca.
- **Retry e dead-letter são nativos do Service Bus** (ADR-0004): sucesso completa a mensagem;
  exceção a abandona → reentrega → no limite (`MaxDeliveryCount=5`) vai pra dead-letter. Não
  reinventamos retry.
- Portas novas na Application: `IDocumentQueue` (enfileirar) e `IDocumentPipeline<T>` (interface da
  esteira, para desacoplar e testar os disparadores).
- Local: emulador do Service Bus em container (depende do SQL que já temos), com a fila
  `documents-in` declarada em `docker/servicebus/Config.json`.

## Alternativas consideradas

- **Manter síncrono** — sem buffer nem retry durável; não é produção.
- **Fila própria em tabela SQL** — reinventa retry, visibilidade, DLQ e concorrência que o Service
  Bus já dá pronto.
- **Storage Queue** — mais simples, mas sem sessions, DLQ rica, nem tópicos; o Service Bus é o
  alvo de produção e o emulador cobre o local.

## Consequências

- O `/ingest` fica desacoplado do processamento: um pico vira profundidade de fila, não latência de
  request. A idempotência (ADR-0005) protege contra a reentrega processar duas vezes.
- O consumidor está atado a `GoodsInvoice` por ora; rotear por tipo de documento entra com o
  segundo tipo.
- O emulador não persiste entidades entre restarts e não tem SLA — é só para dev/teste. O ingress
  automático (Blob → Event Grid → fila) fica como próxima fatia; hoje o `/ingest` é o ponto de
  entrada que enfileira.
