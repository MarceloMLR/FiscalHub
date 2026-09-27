# d365-document-assembly Specification

## Purpose

Montar a NF-e de mercadoria completa a partir das entidades OData do D365 F&O, a partir da referência
publicada pelo feed de mudanças. O resultado é o documento no modelo de domínio e uma impressão estável
do conteúdo, entregues à esteira existente. A entidade fiscal de impostos é a fonte. O lado contábil só
completa o que se sabe que a fiscal não traz.

## Requirements

### Requirement: Localização do documento pelo Locator

O adapter MUST aceitar referências cujo Locator tem a forma `d365/<dataAreaId>/<FiscalDocumentRecId>`,
com o RecId inteiro e positivo. Qualquer outra forma MUST falhar com erro claro, sem chamar o F&O. Isso
inclui o formato antigo, `d365/<dataAreaId>/<Voucher>`, e locators de outra origem. A URL e a
autenticação do ambiente MUST vir das settings do adapter `Dynamics365` no perfil do tenant, as mesmas
do feed de mudanças. Settings inválidas MUST falhar como erro de configuração, sem chamar a rede.

#### Scenario: Locator válido
- **WHEN** chega a referência com Locator `d365/brmf/5637148912`
- **THEN** o cabeçalho é consultado por `FiscalDocumentRecId eq 5637148912`

#### Scenario: Locator no formato antigo
- **WHEN** chega a referência com Locator `d365/brmf/BRMF21-10000027`
- **THEN** a montagem falha com erro que cita o formato esperado, sem nenhuma chamada ao F&O

#### Scenario: Locator de outra origem
- **WHEN** o adapter do D365 recebe uma referência com Locator `nfe/nfe-exemplo.xml`
- **THEN** a montagem falha com erro claro, sem nenhuma chamada ao F&O

### Requirement: Consultas da montagem

Cada montagem MUST fazer, em sequência, uma consulta por entidade. Nenhuma usa `$batch`, e todas usam
`cross-company=true` e `$select` explícito:

1. cabeçalho: `FSFiscalDocumentBRs` com `$filter=FiscalDocumentRecId eq {rec}`;
2. linhas: `FSFiscalDocumentLineBRs` com `$filter=FiscalDocumentRecId eq {rec}`;
3. impostos: `FSFiscalDocumentTaxTransBRs` com
   `$filter=FiscalDocumentRecId eq {rec} or MiscChargeFiscalDocumentRecId eq {rec}`. São dois termos
   fixos: o imposto de linha chega pelo primeiro, o de encargo pelo segundo;
4. encargos: `FSFiscalDocumentMiscChargeBRs` com `$filter=FiscalDocumentRecId eq {rec}`.

Outras regras:

- **Enum.** Nenhum `$filter` da montagem MUST usar valor de enum; a classificação por tipo é feita em
  memória.
- **Paginação.** Nas coleções de linhas, impostos e encargos, o adapter MUST seguir o `@odata.nextLink`
  até o fim, porque o conjunto é o de um único documento lançado, e não uma janela móvel.
- **Throttling.** 429 e 503 com `Retry-After` MUST seguir a mesma regra do feed de mudanças: espera
  honrada até o teto, número máximo de tentativas e, acima disso, falha da montagem.

#### Scenario: As quatro consultas de uma nota sem complemento
- **WHEN** a nota de RecId 5637148912 é montada e nenhum imposto dela precisa de complemento contábil
- **THEN** são feitas exatamente quatro requisições, na ordem cabeçalho, linhas, impostos e encargos,
  todas com `cross-company=true`
- **AND** o filtro de impostos é
  `FiscalDocumentRecId eq 5637148912 or MiscChargeFiscalDocumentRecId eq 5637148912`

#### Scenario: Filtro sem enum
- **WHEN** qualquer consulta da montagem é feita
- **THEN** o `$filter` não contém valor de enum (`Microsoft.Dynamics.DataEntities.*'...'`)

### Requirement: Verificação do cabeçalho

Antes de ler o resto, o adapter MUST conferir o cabeçalho:

- **Sem cabeçalho:** a montagem MUST falhar com erro que cita empresa e RecId e lembra que resultado
  vazio também é o sintoma de falta de acesso do usuário de integração à empresa. A falha segue para
  retentativa e dead-letter; um vazio nunca é tratado como "ignorado".
- **Chave diferente:** se `dataAreaId|Voucher` do cabeçalho não bater com a chave natural da referência,
  a montagem MUST falhar com erro que mostra as duas chaves.
- **Modelo diferente de `55`:** o documento MUST ser informado como fora do escopo, com motivo que cita o
  modelo.
- **Status diferente de `Approved`:** o documento MUST ser informado como fora do escopo, com motivo que
  cita o status. Isso inclui `Cancelled` e `CancelledBySubstitution`. Só nota autorizada é montada e
  despachada.

Nos casos de fora do escopo, nenhuma outra consulta é feita depois do cabeçalho.

#### Scenario: Documento sumiu
- **WHEN** a consulta do cabeçalho pelo RecId volta vazia
- **THEN** a montagem falha com erro que cita a empresa, o RecId e a hipótese de permissão
- **AND** o documento não é registrado como ignorado

#### Scenario: Chave natural não confere
- **WHEN** a referência tem chave `brmf|BRMF21-10000027` e o cabeçalho lido tem voucher `BRMF21-10000031`
- **THEN** a montagem falha com erro que mostra as duas chaves

#### Scenario: Nota cancelada
- **WHEN** o cabeçalho tem `Model = 55` e `Status = Cancelled`
- **THEN** o documento é informado como fora do escopo, com motivo que cita `Cancelled`
- **AND** nenhuma consulta de linhas, impostos ou encargos é feita

#### Scenario: Nota enviada e depois cancelada no F&O
- **WHEN** uma nota já confirmada no destino é cancelada no F&O e redescoberta pelo feed
- **THEN** ela não é reenviada como emitida
- **AND** o registro dela passa a "ignorado", com motivo que cita o status, e preserva o identificador
  externo

### Requirement: Mapeamento do cabeçalho e das linhas

O documento montado MUST refletir o que a nota carrega, sem consultar cadastro para dado que a nota já
tem (d365/04 §2.5 e §3.3).

- **Cabeçalho:** chave de acesso, modelo, série, número, data de emissão e valor total.
- **Emitente e destinatário** pelo `FiscalDocumentIssuer`: com `OwnEstablishment`, o emitente é o
  estabelecimento (`FiscalEstablishment*`) e o destinatário é o terceiro (`ThirdParty*`); com terceiro
  emitente, o contrário. Os dados de cada parte vêm desses mesmos campos: CNPJ/CPF, nome e IE.
- **Itens:** um por linha, ordenados por `LineNum`. Cada item leva número (`LineNum`), código
  (`ItemId`), descrição, NCM (`FiscalClassification`), CFOP, quantidade, valor unitário e valor total.
- **Formato:** CNPJ/CPF, NCM e CFOP MUST ir só com dígitos, como o XML da NF-e os entrega.
- **Linha fracionária:** um `LineNum` fracionário MUST falhar a montagem.

#### Scenario: Nota de saída própria
- **WHEN** o cabeçalho tem `FiscalDocumentIssuer = OwnEstablishment` e `Direction = Outgoing`
- **THEN** o emitente é o estabelecimento e o destinatário é o terceiro

#### Scenario: Nota de entrada de terceiro
- **WHEN** o cabeçalho tem o terceiro como emitente e `Direction = Incoming`
- **THEN** o emitente é o terceiro e o destinatário é o estabelecimento

#### Scenario: CFOP formatado
- **WHEN** a linha traz CFOP `5.102`
- **THEN** o item do documento tem CFOP `5102`

#### Scenario: Cabeçalho sem linha
- **WHEN** o cabeçalho existe e a consulta de linhas volta vazia
- **THEN** o documento é montado sem itens e a esteira o rejeita na validação com "A nota não possui
  itens."

### Requirement: Imposto de linha, imposto de encargo e retenção no lugar certo

Cada linha de `FSFiscalDocumentTaxTransBRs` MUST ir para um único lugar do documento, pela chave
estrangeira. Chave vazia é `0` ou `null`, indistintamente.

- **Imposto de linha:** com `FiscalDocumentLineRecId` preenchido e `FiscalDocumentMiscChargeRecId`
  vazio, vai para o item daquela linha.
- **Imposto de encargo:** com `FiscalDocumentLineRecId` vazio e `FiscalDocumentMiscChargeRecId`
  preenchido, vai para o encargo correspondente.
- **Retenção:** com `RetainedTax = Yes`, vai para as retenções do item ou do encargo e MUST NOT
  aparecer entre os impostos.
- **IBS/CBS:** os tipos `CBS`, `IBSState` e `IBSCity` vão para o grupo IBS/CBS do item.

Os valores MUST ser os da entidade fiscal, como vieram. Isso vale para tipo, CST (`TaxationCode`),
base, alíquota (`TaxValue`) e valor. Nenhuma regra de sinal ou de CST é aplicada pelo adapter.

A montagem MUST falhar, com erro que cita o RecId do imposto, quando o imposto:

- tem as duas chaves vazias;
- tem as duas chaves preenchidas;
- aponta para linha ou encargo que não está no documento;
- tem tipo vazio (`Blank`).

#### Scenario: Imposto de linha
- **WHEN** um ICMS tem `FiscalDocumentLineRecId` igual ao RecId da linha 1 e o encargo vazio
- **THEN** o ICMS aparece nos impostos do item 1 e em nenhum outro lugar

#### Scenario: Imposto de encargo
- **WHEN** um imposto tem `FiscalDocumentLineRecId` nulo e `FiscalDocumentMiscChargeRecId` igual ao RecId
  de um encargo da linha 2
- **THEN** o imposto aparece nos impostos daquele encargo do item 2, e não nos impostos do item 2

#### Scenario: Retenção não é imposto normal
- **WHEN** um IRRF da linha 1 tem `RetainedTax = Yes`, com base e valor
- **THEN** o IRRF aparece nas retenções do item 1, com base e valor
- **AND** não aparece nos impostos do item 1

#### Scenario: Sinal e CST vêm da fiscal
- **WHEN** a fiscal traz IPI com valor positivo e CST `51`, e a `FSTaxTransBR` correspondente traz o
  valor negativo e CST `01`
- **THEN** o documento tem o IPI com valor positivo e CST `51`

#### Scenario: Imposto órfão
- **WHEN** um imposto aponta para uma linha que não veio na consulta de linhas
- **THEN** a montagem falha com erro que cita o RecId do imposto e o da linha

### Requirement: Encargos do item

Cada linha de `FSFiscalDocumentMiscChargeBRs` MUST virar um encargo do item cuja linha ela referencia
(`FiscalDocumentLineRecId`). O encargo leva número (`ChargeNum`), tipo (`MiscChargeType`), valor
(`Amount`) e os impostos e retenções dele.

A montagem MUST falhar, com erro que cita os RecIds, quando o encargo aponta para linha fora do
documento ou traz tipo fora dos valores conhecidos.

#### Scenario: Encargo na linha certa
- **WHEN** um encargo `Others` de valor 10,00 aponta para a linha 2
- **THEN** o item 2 tem esse encargo, com tipo "outras despesas" e valor 10,00

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

A validação da esteira MUST rejeitar o item sem o grupo com "tributos da Reforma (IBS/CBS) ausentes", e
o item com o grupo sem classificação com "cClassTrib ausente".

#### Scenario: Nota anterior à Reforma
- **WHEN** nenhum imposto de um item é `CBS`, `IBSState` ou `IBSCity`
- **THEN** o item é montado sem o grupo IBS/CBS
- **AND** a esteira registra a nota como rejeitada, com motivo que cita os tributos da Reforma ausentes

#### Scenario: Grupo completo sem classificação
- **WHEN** um item tem `CBS`, `IBSState` e `IBSCity` com o mesmo CST e a mesma base
- **THEN** o grupo IBS/CBS é montado com esses valores e sem `cClassTrib`
- **AND** a esteira rejeita a nota com "cClassTrib ausente"

#### Scenario: Grupo parcial
- **WHEN** um item tem `CBS`, mas não tem `IBSState` nem `IBSCity`
- **THEN** a montagem falha com erro que cita o item e os tipos presentes

### Requirement: Complemento contábil

O complemento contábil MUST valer só para os tipos de imposto em que se sabe que a entidade fiscal vem
zerada. Hoje o único tipo é o `ImportTax`.

- **Elegível:** a linha fiscal desse tipo com valor zero e `TaxTransRecId` preenchido.
- **Consulta:** havendo linha elegível, o adapter MUST fazer uma consulta a mais, em
  `FSTaxTransBRs` com `$filter=Voucher eq '<voucher>' and dataAreaId eq '<empresa>'`. Casa as linhas
  pelo `TaxTransRecId`, em memória.
- **Sem linha elegível:** MUST NOT consultar a `FSTaxTransBRs`.
- **Valores, campo a campo** (base, alíquota e valor): quando a fiscal é diferente de zero, vale a
  fiscal; quando é zero, vale a contábil.
- **O resto vem da fiscal:** tipo, CST, lugar no documento e indicador de retenção.

#### Scenario: Imposto de importação zerado na fiscal
- **WHEN** a linha 1 tem `ImportTax` com valor e base zerados na fiscal, e a `FSTaxTransBR` do mesmo
  `TaxTransRecId` tem base 1.000,00, alíquota 14 e valor 140,00
- **THEN** o item 1 tem o imposto de importação com base 1.000,00, alíquota 14 e valor 140,00
- **AND** a `FSTaxTransBRs` foi consultada uma vez só, pelo voucher e pela empresa

#### Scenario: Sem imposto elegível, sem consulta contábil
- **WHEN** nenhum `ImportTax` da nota está zerado na fiscal
- **THEN** a `FSTaxTransBRs` não é consultada

#### Scenario: IPI zerado na fiscal fica zerado
- **WHEN** um IPI com CST `55` está zerado na fiscal
- **THEN** o documento tem o IPI zerado, como a fiscal traz
- **AND** esse IPI não dispara consulta contábil

### Requirement: Divergência não prevista entre fiscal e contábil

Toda comparação feita no complemento que fuja do padrão conhecido MUST falhar a montagem, em vez de
escolher um dos lados. Os casos são:

- linha elegível cujo `TaxTransRecId` não aparece entre as linhas contábeis do voucher;
- linha contábil casada com tipo de imposto diferente do da fiscal;
- linha contábil com valor negativo;
- campo em que fiscal e contábil são ambos diferentes de zero e diferentes entre si.

O erro MUST citar o voucher, os dois RecIds e os campos divergentes. A foto da fonte, com os dois lados,
MUST já estar salva quando a falha acontece.

#### Scenario: Ponte sem par contábil
- **WHEN** um `ImportTax` zerado na fiscal tem `TaxTransRecId` que não está entre as linhas contábeis do
  voucher
- **THEN** a montagem falha com erro que cita o voucher e o `TaxTransRecId`

#### Scenario: Dois valores diferentes
- **WHEN** um `ImportTax` elegível (valor zero na fiscal) tem base 4.000,00 na fiscal e 4.500,00 na contábil do
  mesmo `TaxTransRecId`
- **THEN** a montagem falha com erro que cita o campo base e os dois números
- **AND** a nota não é enviada ao destino

### Requirement: Cadastros de referência em cache

O código IBGE do município de cada parte MUST vir do endereço referenciado pelo cabeçalho
(`FiscalEstablishmentPostalAddress`, `ThirdPartyPostalAddress`, em `FSPostalAddressBRs`), e deste para a
cidade (`FSAddressCityBRs`). Endereço e cidade MUST ficar em cache por tenant, pela chave RecId, com
expiração absoluta configurável e padrão de 1 hora. A invalidação é só por tempo.

- Chave estrangeira vazia MUST NOT gerar consulta; o município da parte fica ausente.
- Registro não encontrado também deixa o município ausente, com aviso em log, e a montagem segue.

#### Scenario: Segunda nota do mesmo cliente
- **WHEN** duas notas do mesmo estabelecimento e do mesmo terceiro são montadas dentro da expiração
- **THEN** endereço e cidade são consultados só na primeira montagem

#### Scenario: Expiração
- **WHEN** a mesma nota é montada de novo depois da expiração do cache
- **THEN** endereço e cidade são consultados de novo

#### Scenario: Cache separado por tenant
- **WHEN** o tenant-a e o tenant-c referenciam o mesmo RecId de endereço
- **THEN** cada tenant consulta o seu, sem reaproveitar o do outro

### Requirement: Impressão de conteúdo canônica

A impressão do conteúdo que alimenta a idempotência (ADR-0016) MUST ser calculada sobre um JSON
canônico das respostas do documento. As regras do canônico:

- entidades em ordem fixa: cabeçalho, linhas, impostos, encargos e linhas contábeis casadas no
  complemento (vazio quando não houve complemento);
- cada coleção ordenada pelo próprio RecId;
- propriedades de cada registro em ordem ordinal do nome;
- valores exatamente como vieram, sem reformatar números;
- exclusão de toda anotação `@odata.*` (inclusive `@odata.etag` e `@odata.context`) e de
  `SysModifiedDateTime`.

Cadastros de referência MUST NOT entrar na impressão: mudança de cadastro não altera a nota emitida.

#### Scenario: Duas montagens da mesma nota
- **WHEN** a mesma nota é montada duas vezes sem mudança na origem
- **THEN** as duas impressões são iguais

#### Scenario: Só o carimbo de auditoria mudou
- **WHEN** entre duas montagens só mudaram `SysModifiedDateTime` e `@odata.etag`
- **THEN** as duas impressões são iguais, e a esteira não reenvia a nota

#### Scenario: Valor fiscal mudou
- **WHEN** entre duas montagens o valor de um imposto mudou
- **THEN** as impressões são diferentes

#### Scenario: Ordem da resposta não importa
- **WHEN** a origem devolve as linhas de imposto em outra ordem
- **THEN** a impressão é a mesma

#### Scenario: Cadastro mudou
- **WHEN** entre duas montagens só mudou o código IBGE da cidade do terceiro
- **THEN** as impressões são iguais

### Requirement: Foto da fonte

O JSON canônico MUST ser salvo como foto da fonte do documento, no formato `json` (ADR-0006), depois das
consultas e antes do mapeamento para o domínio. Assim, uma falha de mapeamento ou de divergência deixa a
foto salva, e a impressão pode ser recalculada a partir dela. Não há foto quando a montagem para no
cabeçalho, seja por sumiço, chave, modelo ou status.

#### Scenario: Foto antes da divergência
- **WHEN** a montagem falha por divergência não prevista no complemento
- **THEN** a foto da fonte daquela nota já está salva, com as linhas fiscais e contábeis

#### Scenario: Impressão reproduzível
- **WHEN** a foto da fonte de uma nota montada é lida
- **THEN** a impressão calculada sobre ela é a mesma gravada no registro do documento

### Requirement: Testes contra respostas gravadas do ambiente

Os testes da montagem MUST usar respostas gravadas do ambiente real, byte a byte, por um procedimento
opt-in de gravação. Os casos que a base não tem MUST usar uma fixture **derivada** de uma gravação. A
derivada tem que ser identificada como tal, e a edição, descrita junto do arquivo. Hoje são dois casos:
imposto apontando para encargo e divergência. `dotnet test` MUST rodar sem rede.

#### Scenario: Montagem completa de nota real
- **WHEN** o teste monta uma NF-e modelo 55 da base a partir das respostas gravadas
- **THEN** o documento montado bate com o esperado, item a item e imposto a imposto

#### Scenario: Suíte sem rede
- **WHEN** `dotnet test` roda numa máquina sem acesso ao F&O
- **THEN** todos os testes da montagem passam sem chamada de rede

### Requirement: Teste contra o ambiente real opt-in

Um teste de integração MUST montar as NF-e modelo 55 do ambiente real. Ele MUST ser pulado a menos que a
variável de ambiente que aponta o ambiente esteja definida, no mesmo padrão do teste do feed de
mudanças.

#### Scenario: Contra o fiscosysdev
- **WHEN** a variável aponta o fiscosysdev, a empresa é `brmf` e o desenvolvedor está logado no Azure CLI
- **THEN** cada NF-e modelo 55 da base é montada sem erro, e duas montagens seguidas da mesma nota dão a
  mesma impressão
