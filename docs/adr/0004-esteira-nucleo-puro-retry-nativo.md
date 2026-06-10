# ADR-0004: Esteira — núcleo puro, retry/DLQ nativos, envio direto

- **Status:** Aceito
- **Data:** 2026-06-09

## Contexto

A esteira processa cada documento de forma resiliente (idempotência, retry, dead-letter) e
deve servir a qualquer tipo de documento e a qualquer destino. Precisamos decidir onde mora a
lógica, quem cuida de retry/DLQ e onde acontece o envio ao destino.

## Decisão

- **Núcleo puro na Application.** A orquestração (`DocumentPipeline.ProcessAsync`) fala só
  pelas portas (`IInboundSource`, `IComplianceDispatcher`, `IProcessingStore`) e não conhece
  Service Bus nem Azure. É testável com implementações falsas.
- **Retry e DLQ nativos do Service Bus.** A casca (Azure Function disparada pela fila) apenas
  chama o núcleo; uma exceção propaga e o Service Bus reconta a entrega e, no limite, move a
  mensagem para a DLQ. Não escrevemos lógica de retry.
- **Idempotência por envio registrado.** A checagem é "já enviei com sucesso?", não "já vi".
  Assim uma falha permite retry; um sucesso evita reenvio na reentrega.
- **Envio por chamada direta** dentro do pipeline (não numa fila de saída separada).

## Alternativas consideradas

- **Lógica acoplada ao Service Bus** — o handler da fila seria a orquestração. Rejeitado: solda
  o núcleo ao Azure e impede testar sem subir infraestrutura.
- **Retry/DLQ próprios** — reinventa o que o Service Bus já oferece.
- **Fila de saída própria (envio desacoplado)** — isola backpressure e rate limit do destino em
  relação à origem. É o desenho de produção, mas adiciona uma fila. Diferido: como o envio está
  atrás da porta `IComplianceDispatcher`, migrar para esse modelo é mudança de fiação no
  composition root, sem tocar no núcleo.

## Consequências

- O núcleo é testável isoladamente; a casca de Service Bus é fina e vem em fatia separada.
- A consulta de status (poll) assíncrona é uma etapa/fila separada, independente desta decisão.
- A janela "envio concluído no destino mas processo morre antes de gravar" exige, no futuro,
  uma chave de idempotência na requisição ao destino.
- Trocar para fila de saída própria, quando o volume exigir, não impacta o núcleo.
