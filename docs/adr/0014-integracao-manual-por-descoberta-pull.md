# ADR-0014: Integração manual por descoberta pull (porta genérica, adapter local no dev)

- **Status:** Aceito
- **Data:** 2026-07-24

## Contexto

Além dos gatilhos por evento (drop no Blob / Event Grid) e por fila, o cliente precisa disparar uma
integração **manual**: escolhe empresa/filial e um **período**, e o conector processa as notas
daquele recorte. A fonte da verdade de "quais notas existem no período" é a **origem** (Avalara/ERP)
— não o nosso banco de rastreio, que só conhece o que já passou por aqui.

## Decisão

- Reusar a porta **`IDocumentDiscovery`** (modo pull, já desenhada no ADR-0002): recebe
  `DiscoveryCriteria` (tenant + período obrigatórios; empresa/filial/tipo opcionais) e devolve
  `IReadOnlyList<DocumentReference>` — só as referências, sem o conteúdo (claim-check).
- O endpoint **`POST /integrations/manual`** monta o critério a partir da escolha do cliente, chama
  `DiscoverAsync` e **enfileira cada referência** via `IDocumentQueue`. Daí pra frente é o mesmo
  caminho de sempre: consumidor do Service Bus → fetch → esteira → dispatch → poll. **Nenhuma etapa
  nova de processamento.**
- Adapter de dev: **`FiscalHub.Adapters.Discovery.Local`**, com catálogo fixo que espelha os XMLs
  semeados (chave de acesso como `NaturalKey`, `Locator` apontando pro Blob de seed) e filtra por
  tenant/empresa/filial/período. Em produção, a **Avalara/ERP vira outro adapter da mesma porta** —
  o endpoint não muda.

## Alternativas consideradas

- **Derivar a lista do banco de rastreio** (notas já processadas) — fonte errada: a integração
  manual serve justamente pra puxar o que **ainda não** entrou. Só listaria o que já existe.
- **Endpoint que recebe a lista de chaves pronta do front** — empurra pro cliente a
  responsabilidade de saber quais notas existem no período; é exatamente o trabalho da descoberta.
- **Processar síncrono no próprio request** — perderia retry/DLQ nativos da fila e seguraria a
  resposta. Enfileirar mantém o request rápido (202) e o processamento resiliente.

## Consequências

- Reprocessar o mesmo período é **idempotente**: a mesma chave de acesso cai na regra por estado
  (ADR-0007) — `Submitted`/`Confirmed` bloqueiam reenvio; `IntegrationError`/`Unconfirmed`/
  `DeadLettered` liberam. (Verificado ao vivo: uma nota em `DeadLettered` foi reenviada e chegou a
  `Confirmed`.)
- Trocar a origem = escrever o adapter Avalara/ERP e trocar o registro no DI; endpoint, fila e
  esteira não mudam.
- O `POST /integrations/manual` já entrega o backend da tela de integração manual (Fase 2); os
  dropdowns consomem o diretório do ADR-0013.
