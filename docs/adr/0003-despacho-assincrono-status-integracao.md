# ADR-0003: Despacho assíncrono e status de integração normalizado

- **Status:** Aceito
- **Data:** 2026-06-08

## Contexto

A integração com a plataforma de compliance nem sempre é síncrona. A Avalara, por exemplo, é
assíncrona: recebe o JSON e devolve um identificador (GUID); o resultado final (carregado ou
erro) precisa ser consultado depois, em outro endpoint. Outras plataformas podem ser síncronas
ou notificar por webhook. O dashboard, porém, deve ser agnóstico de plataforma.

## Decisão

A porta de saída (`IComplianceDispatcher<TDocument>`) opera em **duas fases**:

1. `SubmitAsync` — envia e devolve um recibo (`IntegrationReceipt`): id externo + status inicial.
2. `CheckStatusAsync` — consulta o status final pelo id externo (`IntegrationResult`).

Cada adapter de destino **normaliza** o status nativo da plataforma para um vocabulário comum
(`IntegrationStatus`: Pending, Submitted, Confirmed, IntegrationError) — uma camada anticorrupção
no eixo de status. O dashboard e o store de rastreio giram em torno desse status comum.

Distinção importante: o **status fiscal** da nota (emitida/cancelada) não é o eixo da UI. Ele é
insumo de roteamento (carga x cancelamento) e vai no payload; o eixo do dashboard é o **status
de integração**.

## Alternativas consideradas

- **Despacho de fase única** ("envia e pronto") — não modela o caso assíncrono; perderíamos o
  resultado final da Avalara.
- **Expor o status nativo da plataforma na UI** — acoplaria o dashboard ao formato de cada
  destino (o GUID da Avalara, etc.). A normalização mantém a UI agnóstica.

## Consequências

- A esteira trata o despacho como operação de duas fases: enviar → rastrear → confirmar.
- Plataformas síncronas implementam a fase 2 como trivial; webhooks chegam por um callback.
- O store de rastreio guarda o status de integração e o id externo por documento.
- Adicionar um destino novo é implementar a porta e o mapeamento de status — sem tocar na UI.
