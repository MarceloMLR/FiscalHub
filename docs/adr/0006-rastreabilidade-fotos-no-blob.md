# ADR-0006: Rastreabilidade por "fotos" no Blob

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

Quando um cliente abre um chamado ("falta um campo", "veio errado"), precisamos saber **onde** a
informação se perdeu, sem adivinhação: no ERP de origem, no mapeamento origem→domínio, ou no
mapeamento domínio→destino (Avalara). Sem isso, todo chamado vira investigação no escuro.

## Decisão

Guardar três **fotos** por documento e usá-las como busca binária da falha:

1. **Fonte crua** (XML) — já fica no Blob pelo claim-check.
2. **Domínio** (`GoodsInvoice` em JSON) — o nosso padrão.
3. **Destino** (payload Avalara em JSON) — o que foi enviado.

- Porta fina `IProcessingTrace` (`SaveDomainAsync`, `SaveOutboundAsync`) na Application; default
  `NoOpProcessingTrace` (rastreio desligado quando não configurado).
- O **dispatcher** emite as duas fotos (domínio e destino) antes do POST — é onde os dois artefatos
  coexistem, então não precisa vazar payload para a esteira.
- Implementação `BlobProcessingTrace` no Infrastructure. Layout `{tenant}/{aaaaMM}/{chave}/domain.json`
  e `.../{destino}.json`; reprocessar sobrescreve.

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
- Acopla o dispatcher à porta de trace (aceitável — adapter depende da Application). Se surgirem
  vários destinos, a foto do domínio pode virar um decorator; a do destino segue no adapter (só ele
  tem o payload final).
- O `BlobProcessingTrace` é I/O antes do envio: uma falha ao gravar a foto hoje interrompe o
  despacho. Tornar o trace best-effort (não derrubar o envio) fica como hardening.
