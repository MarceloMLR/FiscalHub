## MODIFIED Requirements

### Requirement: Mapeamento do cabeçalho para referência

Cada registro da `FSFiscalDocumentBRs` MUST virar uma referência do tenant com:
- `NaturalKey = <dataAreaId>|<Voucher>` (ex.: `brmf|BRMF21-10000027`);
- `Locator = d365/<dataAreaId>/<FiscalDocumentRecId>`, com o `dataAreaId` codificado para URL. É o
  contrato com a montagem: o RecId é a chave primária do cabeçalho, e o `Voucher` não lidera nenhum
  índice da `FiscalDocument_BR`;
- tipo de documento resolvido pelo `Model` num mapa configurável por tenant, com padrão `55` → NF-e de
  mercadoria, `57` → CT-e, `SE` → NFS-e;
- o grupo da nota, lido do mesmo registro:
  - empresa: o `FiscalEstablishmentCNPJCPF`, normalizado (`tax-identifier-normalization`): sem pontuação, com as letras
    e a caixa como vieram;
  - filial: o `FiscalEstablishment`, como veio;
  - data de referência: o dia do `FiscalDocumentDate`, como veio, sem conversão de fuso. É o mesmo campo e a mesma
    regra da montagem;
  - número: o `FiscalDocumentNumber`;
  - modelo: o `Model`, como veio.

O grupo MUST NOT ter valor padrão. Ele é o que a origem traz.

O mesmo mapeamento MUST valer para a descoberta por período do D365 (`period-discovery`): o mesmo cabeçalho dá a mesma
referência, pelos dois caminhos.

Registro sem `Voucher`, ou com `Model` fora do mapa, MUST NOT ser enfileirado nem interromper a leitura.
Ele MUST ser registrado em log de aviso com empresa, voucher e modelo, e a leitura segue.

#### Scenario: Nota de mercadoria
- **WHEN** a consulta traz `dataAreaId = brmf`, `Voucher = BRMF21-10000027`, `Model = 55` e
  `FiscalDocumentRecId = 5637148912`
- **THEN** a referência tem `NaturalKey = brmf|BRMF21-10000027`, `Locator = d365/brmf/5637148912` e
  tipo NF-e de mercadoria

#### Scenario: Nota de serviço
- **WHEN** a consulta traz `dataAreaId = brmf`, `Voucher = BRMF21-10000019`, `Model = SE`
- **THEN** a referência tem tipo NFS-e

#### Scenario: Grupo lido da descoberta
- **WHEN** a consulta traz `Model = SE`, `FiscalDocumentNumber = 000123`, `FiscalDocumentDate = 2026-08-07T12:00:00Z`,
  `FiscalEstablishmentCNPJCPF = 442782250002-60` e `FiscalEstablishment = SP-01`
- **THEN** a referência leva empresa `44278225000260`, filial `SP-01`, data de referência 2026-08-07, número
  `000123` e modelo `SE`

#### Scenario: Grupo com CNPJ alfanumérico
- **WHEN** a consulta traz `FiscalEstablishmentCNPJCPF = 12.ABC.345/01DE-35`
- **THEN** a referência leva a empresa `12ABC34501DE35`, e não `123450135`

#### Scenario: Voucher com caractere especial
- **WHEN** a consulta traz `dataAreaId = brmf`, `Voucher = NF/2017 01` e `FiscalDocumentRecId = 5637149001`
- **THEN** a referência tem `NaturalKey = brmf|NF/2017 01` e `Locator = d365/brmf/5637149001`

#### Scenario: Modelo fora do mapa
- **WHEN** a consulta traz um registro com `Model = 65` e o mapa do tenant não tem `65`
- **THEN** esse registro não vira referência e um aviso é registrado com empresa, voucher e modelo
- **AND** os demais registros da página seguem normalmente, e a marca alta da página não muda por causa
  dele

#### Scenario: Registro sem Voucher
- **WHEN** a consulta traz um registro com `Voucher` vazio
- **THEN** esse registro não vira referência e um aviso é registrado
