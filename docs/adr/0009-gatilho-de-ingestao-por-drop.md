# ADR-0009: Gatilho de ingestão por drop no Blob (local)

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

O `/ingest` manual (ADR-0008) prova a fila, mas o fluxo real de produção é **hands-off**: um arquivo
chega no armazenamento e o processamento começa sozinho. No Azure isso é Event Grid — o Blob emite
um evento quando um arquivo é criado, e o evento enfileira. O problema local: o **Azurite não emite
eventos**, então não dá pra reproduzir o Event Grid na máquina.

## Decisão

- Um **adapter de gatilho** `BlobDropWatcher` (hosted service): observa uma zona de drop no Blob e,
  para cada arquivo novo, move pro container durável (claim-check) e enfileira a referência via
  `IDocumentQueue`. Como o Azurite não emite eventos, a varredura é por **polling** (intervalo curto).
- O gatilho é **agnóstico de formato**: não abre o XML; deriva `tenant` e `chave` do nome do arquivo
  (`{tenant}/{chave}.xml`). A lógica de nome fica num `DropBlobNaming` testável.
- O arquivo é **movido** (drop → container durável) antes de enfileirar, então o locator na mensagem
  aponta pra um lugar estável e a zona de drop esvazia (sem reprocesso do mesmo arquivo).
- No cloud, troca-se `BlobDropWatcher` por um gatilho de **Event Grid** (uma function). O resto —
  fila, consumidor, esteira — não muda. A origem do gatilho é uma borda plugável.

## Alternativas consideradas

- **Manter só o `/ingest` manual** — não demonstra o hands-off; menos "real".
- **Simular Event Grid localmente** — não há emulador; qualquer imitação seria mais complexa que o
  watcher e ainda assim não seria o Event Grid.
- **Enfileirar sem mover o arquivo** — o locator apontaria pra zona de drop, e evitar reprocesso
  exigiria um índice de "já visto"; mover é mais simples e deixa o estado visível (drop vazia).

## Consequências

- Localmente o fluxo fica hands-off de verdade: dropa um arquivo, ele anda até `Confirmed` sozinho.
  O endpoint `POST /drop/{chave}` (dev) copia o XML de exemplo pra zona de drop pra facilitar o teste.
- Polling tem latência (o intervalo) e relista a zona a cada passada — irrelevante no volume de dev,
  e no cloud o Event Grid é event-driven, sem polling.
- Janela pequena de falha: se o processo cair entre mover e enfileirar, o arquivo fica no container
  durável sem ter sido enfileirado (órfão visível). Reconciliação fica como hardening.
- O tipo do documento é fixo (`GoodsInvoice55`); rotear por tipo entra com o segundo tipo.
