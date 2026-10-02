## MODIFIED Requirements

### Requirement: Mapeamento do cabeçalho e das linhas

O documento montado MUST refletir o que a nota carrega, sem consultar cadastro para dado que a nota já
tem (d365/04 §2.5 e §3.3).

- **Cabeçalho:** chave de acesso, modelo, série, número, data de emissão, valor total, valor das
  mercadorias (`TotalGoodsAmount`) e data de entrada/saída (`AccountingDate`). Se o F&O devolver a data
  vazia (`1900-01-01`), a data de entrada/saída fica ausente.
- **Data fiscal:** o dia do `FiscalDocumentDate`, como veio, sem hora e sem conversão de fuso. Ela fica ao lado da data
  de emissão, que continua sendo o instante (`FiscalDocumentDateTime`, ou o `FiscalDocumentDate` quando o F&O devolve a
  data e hora vazia). É a data fiscal, e não a data de emissão em UTC, que define o dia da nota (`document-grouping`).
- **Estabelecimento próprio:** o CNPJ (`FiscalEstablishmentCNPJCPF`, normalizado) e o código
  (`FiscalEstablishment`, como veio), em qualquer direção e em qualquer emissão. É o estabelecimento que escritura a
  nota, e é por ele que a nota é agrupada (`document-grouping`).
- **Emissão própria ou de terceiros** pelo `FiscalDocumentIssuer`: `OwnEstablishment` é emissão própria, e
  `ThirdParty` é emissão de terceiros.
- **Emitente e destinatário** pelo mesmo `FiscalDocumentIssuer`: com `OwnEstablishment`, o emitente é o
  estabelecimento (`FiscalEstablishment*`) e o destinatário é o terceiro (`ThirdParty*`); com terceiro
  emitente, o contrário. Os dados de cada parte vêm desses mesmos campos: CNPJ/CPF, nome e IE.
- **Itens:** um por linha, ordenados por `LineNum`. Cada item leva:
  - número (`LineNum`), código (`ItemId`) e descrição;
  - NCM (`FiscalClassification`) e CFOP;
  - quantidade, valor unitário e valor total;
  - unidade (`Unit`) e valor contábil (`AccountingAmount`);
  - origem da mercadoria (`Origin`), como o dígito de 0 a 8 da tabela de origem do leiaute.

  Unidade vazia fica ausente. Origem cujo valor não tem tradução para essa tabela fica ausente, e nunca
  vira `0`.
- **Formato:**
  - o CNPJ e o CPF, das partes e do estabelecimento, MUST ir normalizados (`tax-identifier-normalization`): sem
    pontuação, com as letras e a caixa como vieram, como o XML da NF-e entrega o CNPJ alfanumérico;
  - o NCM e o CFOP MUST ir só com dígitos.
- **Linha fracionária:** um `LineNum` fracionário MUST falhar a montagem.

#### Scenario: Nota de saída própria
- **WHEN** o cabeçalho tem `FiscalDocumentIssuer = OwnEstablishment` e `Direction = Outgoing`
- **THEN** o emitente é o estabelecimento e o destinatário é o terceiro
- **AND** o documento é de emissão própria

#### Scenario: Nota de entrada de terceiro
- **WHEN** o cabeçalho tem o terceiro como emitente e `Direction = Incoming`
- **THEN** o emitente é o terceiro e o destinatário é o estabelecimento
- **AND** o documento é de emissão de terceiros

#### Scenario: Estabelecimento próprio numa nota de terceiro
- **WHEN** o cabeçalho tem `FiscalDocumentIssuer = ThirdParty`, `FiscalEstablishmentCNPJCPF = 442782250001-80` e
  `FiscalEstablishment = Matriz`
- **THEN** o documento tem o estabelecimento próprio com CNPJ `44278225000180` e código `Matriz`
- **AND** esse CNPJ é o do destinatário, e não o do emitente

#### Scenario: Estabelecimento com CNPJ alfanumérico
- **WHEN** o cabeçalho tem `FiscalEstablishmentCNPJCPF = 12.ABC.345/01DE-35`, e a nota é de emissão própria
- **THEN** o estabelecimento próprio e o emitente têm o CNPJ `12ABC34501DE35`

#### Scenario: Data fiscal separada da emissão em UTC
- **WHEN** o cabeçalho traz `FiscalDocumentDateTime = 2026-08-08T01:30:00Z` e
  `FiscalDocumentDate = 2026-08-07T12:00:00Z`
- **THEN** o documento tem data fiscal 2026-08-07, e data de emissão 2026-08-08T01:30:00Z

#### Scenario: CFOP formatado
- **WHEN** a linha traz CFOP `5.102`
- **THEN** o item do documento tem CFOP `5102`

#### Scenario: Data de entrada/saída e valor das mercadorias
- **WHEN** o cabeçalho traz `AccountingDate` 2016-09-02 e `TotalGoodsAmount` 1.000,00
- **THEN** o documento tem data de entrada/saída 2016-09-02 e valor das mercadorias 1.000,00

#### Scenario: Data de entrada/saída vazia no F&O
- **WHEN** o cabeçalho traz `AccountingDate` igual a `1900-01-01`
- **THEN** o documento fica sem data de entrada/saída

#### Scenario: Unidade e valor contábil do item
- **WHEN** a linha traz `Unit = un` e `AccountingAmount` 3.500,00
- **THEN** o item tem unidade `un` e valor contábil 3.500,00

#### Scenario: Origem sem tradução
- **WHEN** a linha traz em `Origin` um valor que não tem tradução para a tabela de origem do leiaute
- **THEN** o item fica sem origem, e a montagem não falha

#### Scenario: Cabeçalho sem linha
- **WHEN** o cabeçalho existe e a consulta de linhas volta vazia
- **THEN** o documento é montado sem itens e a esteira o rejeita na validação com "A nota não possui
  itens."
