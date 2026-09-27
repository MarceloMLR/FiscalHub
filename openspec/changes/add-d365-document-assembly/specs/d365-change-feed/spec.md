## ADDED Requirements

### Requirement: Carimbo de alteração e horizonte estável

O feed MUST informar o que o motor precisa para a supressão de republicação:

- **Carimbo por referência:** cada referência MUST vir com o seu carimbo de alteração, que é o
  `SysModifiedDateTime` do registro.
- **Horizonte por página:** cada página MUST informar o horizonte estável, que é o instante do header
  `Date` da **primeira** resposta da leitura menos a margem de assentamento. A margem é configurável,
  com padrão de **10 segundos** e mínimo de 1 segundo. Ela cobre a resolução de segundo do
  `SysModifiedDateTime` e a diferença entre o relógio do web server e o que carimba o registro.
- **Sem `Date`:** a página MUST vir sem horizonte.

#### Scenario: Horizonte pela primeira resposta
- **WHEN** a primeira resposta da leitura trouxe `Date: Fri, 25 Sep 2026 15:00:00 GMT` e a margem é a
  padrão
- **THEN** todas as páginas dessa leitura informam horizonte estável 2026-09-25T14:59:50Z

#### Scenario: Resposta sem Date
- **WHEN** a primeira resposta da leitura não traz o header `Date`
- **THEN** as páginas dessa leitura vêm sem horizonte estável

#### Scenario: Carimbo da referência
- **WHEN** a consulta traz um registro com `SysModifiedDateTime = 2017-01-21T21:23:19Z`
- **THEN** a referência desse registro vem com carimbo de alteração 2017-01-21T21:23:19Z

#### Scenario: Margem abaixo do mínimo
- **WHEN** a margem de assentamento é configurada com 0 segundo
- **THEN** o registro do feed falha com erro de configuração

## MODIFIED Requirements

### Requirement: Mapeamento do cabeçalho para referência

Cada registro da `FSFiscalDocumentBRs` MUST virar uma referência do tenant com:
- `NaturalKey = <dataAreaId>|<Voucher>` (ex.: `brmf|BRMF21-10000027`);
- `Locator = d365/<dataAreaId>/<FiscalDocumentRecId>`, com o `dataAreaId` codificado para URL. É o
  contrato com a montagem: o RecId é a chave primária do cabeçalho, e o `Voucher` não lidera nenhum
  índice da `FiscalDocument_BR`;
- tipo de documento resolvido pelo `Model` num mapa configurável por tenant, com padrão `55` → NF-e de
  mercadoria, `57` → CT-e, `SE` → NFS-e.

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
