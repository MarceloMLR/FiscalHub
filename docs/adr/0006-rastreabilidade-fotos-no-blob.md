# ADR-0006: Rastreabilidade por "fotos" no Blob

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

Quando um cliente abre um chamado ("falta um campo", "veio errado"), precisamos saber **onde** a
informação se perdeu, sem adivinhação: no ERP de origem, no mapeamento origem→domínio, ou no
mapeamento domínio→destino (Avalara). Sem isso, todo chamado vira investigação no escuro.

## Decisão

Guardar três **fotos** por documento e usá-las como busca binária da falha:

1. **Fonte crua** (XML/JSON do cliente) — fotografada pelo adapter de entrada, antes do parse.
2. **Domínio** (`GoodsInvoice` em JSON) — fotografado pela esteira, após a busca.
3. **Destino** (payload Avalara em JSON) — fotografado pelo adapter de saída, antes do envio.

- Porta fina `IProcessingTrace` (`SaveSourceAsync`, `SaveDomainAsync`, `SaveOutboundAsync`) na
  Application; default `NoOpProcessingTrace` (rastreio desligado quando não configurado).
- **Cada camada fotografa o artefato que é dona**: a entrada tira a foto da fonte (antes do parse,
  então um XML que falha no parse já fica salvo); a esteira tira a do domínio (antes de validar,
  então até uma nota rejeitada registra o que entendemos dela); a saída tira a do destino.
- Implementação `BlobProcessingTrace` no Infrastructure. Layout `{tenant}/{aaaaMM}/{chave}/source.{fmt}`,
  `.../domain.json` e `.../{destino}.json`; reprocessar sobrescreve.

## Alternativas consideradas

- **Guardar as fotos em colunas do SQL** — infla o banco com payloads grandes; contraria o ADR de
  Blob-para-payload, SQL-para-metadado.
- **Só logar (App Insights)** — logs de payload inteiro são caros e efêmeros; ruins para consulta
  histórica por documento.
- **Rastrear só em erro** — não cobre o caso de nota integrada "com sucesso" mas com dado errado,
  que é justamente o chamado difícil.

## Consequências

- **"Muitos arquivos" não é problema:** Blob é object storage — milhões de objetos são o uso normal.
  O crescimento se controla por **lifecycle policy** no container (apaga trace com mais de N dias),
  nativa e sem código. Opcional: gzip corta ~80% do tamanho.
- Acopla os adapters (entrada e saída) e a esteira à porta de trace — aceitável, é uma porta da
  Application. A foto do domínio na esteira cobre qualquer destino de graça; cada adapter só
  fotografa o artefato que produz.
- O `BlobProcessingTrace` é I/O antes do envio: uma falha ao gravar a foto hoje interrompe o
  despacho. Tornar o trace best-effort (não derrubar o envio) fica como hardening.
