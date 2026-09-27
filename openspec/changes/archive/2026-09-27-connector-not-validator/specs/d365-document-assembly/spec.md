## MODIFIED Requirements

### Requirement: Mapeamento do cabeçalho e das linhas

O documento montado MUST refletir o que a nota carrega, sem consultar cadastro para dado que a nota já
tem (d365/04 §2.5 e §3.3).

- **Cabeçalho:** chave de acesso, modelo, série, número, data de emissão, valor total, valor das
  mercadorias (`TotalGoodsAmount`) e data de entrada/saída (`AccountingDate`). Se o F&O devolver a data
  vazia (`1900-01-01`), a data de entrada/saída fica ausente.
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
- **Formato:** CNPJ/CPF, NCM e CFOP MUST ir só com dígitos, como o XML da NF-e os entrega.
- **Linha fracionária:** um `LineNum` fracionário MUST falhar a montagem.

#### Scenario: Nota de saída própria
- **WHEN** o cabeçalho tem `FiscalDocumentIssuer = OwnEstablishment` e `Direction = Outgoing`
- **THEN** o emitente é o estabelecimento e o destinatário é o terceiro
- **AND** o documento é de emissão própria

#### Scenario: Nota de entrada de terceiro
- **WHEN** o cabeçalho tem o terceiro como emitente e `Direction = Incoming`
- **THEN** o emitente é o terceiro e o destinatário é o estabelecimento
- **AND** o documento é de emissão de terceiros

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

### Requirement: Grupo IBS/CBS do item

- **Nenhum dos três tipos** (`CBS`, `IBSState`, `IBSCity`): o item MUST sair sem o grupo IBS/CBS, e
  não com valores zerados.
- **Os três presentes:** o grupo MUST ser montado com:
  - CST pelo `TaxationCode`;
  - base;
  - alíquota e valor de cada parcela;
  - valor total do IBS como soma das parcelas estadual e municipal, pela definição do leiaute.
- **Classificação tributária (`cClassTrib`):** MUST ficar vazia, porque a entidade fiscal não a traz
  (ver non-goals).
- **Presença parcial:** MUST falhar a montagem. O mesmo vale para CST ou base diferentes entre as três
  parcelas e para um desses tipos com `RetainedTax = Yes`.

A esteira MUST NOT rejeitar o item nem por falta do grupo, nem por falta da classificação. A nota segue
para o envio, e o julgamento do conteúdo fica com a plataforma de compliance (ADR-0026).

#### Scenario: Nota anterior à Reforma
- **WHEN** nenhum imposto de um item é `CBS`, `IBSState` ou `IBSCity`
- **THEN** o item é montado sem o grupo IBS/CBS
- **AND** a nota não é rejeitada na validação e segue para o envio

#### Scenario: Grupo completo sem classificação
- **WHEN** um item tem `CBS`, `IBSState` e `IBSCity` com o mesmo CST e a mesma base
- **THEN** o grupo IBS/CBS é montado com esses valores e sem `cClassTrib`
- **AND** a nota não é rejeitada na validação e segue para o envio

#### Scenario: Grupo parcial
- **WHEN** um item tem `CBS`, mas não tem `IBSState` nem `IBSCity`
- **THEN** a montagem falha com erro que cita o item e os tipos presentes

### Requirement: Cadastros de referência em cache

Os dados de cada parte que vêm do endereço referenciado pelo cabeçalho (`FiscalEstablishmentPostalAddress`,
`ThirdPartyPostalAddress`, em `FSPostalAddressBRs`) são:

- logradouro (`Street`);
- número (`StreetNumber`);
- bairro (`DistrictName`);
- CEP (`ZipCode`).

O código IBGE do município MUST vir do mesmo endereço, e deste para a cidade (`FSAddressCityBRs`).

Endereço e cidade MUST ficar em cache por tenant, pela chave RecId, com expiração absoluta configurável e
padrão de 1 hora. A invalidação é só por tempo.

- Chave estrangeira vazia MUST NOT gerar consulta; o endereço e o município da parte ficam ausentes.
- Registro não encontrado também deixa o endereço e o município ausentes, com aviso em log, e a montagem
  segue.
- Campo de endereço vazio fica ausente na parte.

#### Scenario: Segunda nota do mesmo cliente
- **WHEN** duas notas do mesmo estabelecimento e do mesmo terceiro são montadas dentro da expiração
- **THEN** endereço e cidade são consultados só na primeira montagem

#### Scenario: Expiração
- **WHEN** a mesma nota é montada de novo depois da expiração do cache
- **THEN** endereço e cidade são consultados de novo

#### Scenario: Cache separado por tenant
- **WHEN** o tenant-a e o tenant-c referenciam o mesmo RecId de endereço
- **THEN** cada tenant consulta o seu, sem reaproveitar o do outro

#### Scenario: Endereço do terceiro na parte
- **WHEN** o endereço do terceiro tem logradouro, número, bairro e CEP preenchidos
- **THEN** a parte do terceiro no documento leva os quatro, junto com o município
