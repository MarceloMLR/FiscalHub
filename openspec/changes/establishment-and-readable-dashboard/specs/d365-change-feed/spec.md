## MODIFIED Requirements

### Requirement: Consulta de descoberta sobre FSFiscalDocumentBRs

O feed MUST consultar o endpoint OData do ambiente F&O do tenant em `/data/FSFiscalDocumentBRs`, com
`cross-company=true`, filtro `SysModifiedDateTime gt <instante pedido>` (literal DateTimeOffset em UTC),
ordenação ascendente por `SysModifiedDateTime,FiscalDocumentRecId` e `$top` igual ao tamanho de página
do perfil. A consulta MUST projetar só os campos necessários à referência, ao grupo da nota e à paginação
(`$select`). Entre eles:

- `FiscalDocumentRecId`, para a paginação e o locator;
- `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e `FiscalEstablishment`, para o grupo.

Quando o perfil do tenant listar empresas, o feed MUST restringir a consulta a elas por `dataAreaId`, combinando com
`or`, porque o F&O não suporta `in`. Sem lista, vale toda empresa que o usuário de integração enxerga. O feed MUST NOT
filtrar `Status`, `Model` ou `Direction` no servidor: esse filtro é decisão do hub (ADR-0023).

Os campos da descoberta MUST NOT entrar na impressão de conteúdo do documento. Ampliar este `$select` não muda a
impressão de nota nenhuma.

#### Scenario: Montagem da URL sem empresas
- **WHEN** o feed é consultado para um tenant sem lista de empresas, com instante 2015-01-01T00:00:00Z e
  `pageSize = 500`
- **THEN** a primeira requisição é `GET <url>/data/FSFiscalDocumentBRs` com `cross-company=true`,
  `$filter=SysModifiedDateTime gt 2015-01-01T00:00:00Z`,
  `$orderby=SysModifiedDateTime,FiscalDocumentRecId`, `$top=500` e `$select` contendo
  `FiscalDocumentRecId`, `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e `FiscalEstablishment`
- **AND** o filtro não menciona `Status`, `Model` nem `Direction`

#### Scenario: Montagem da URL com empresas
- **WHEN** o perfil do tenant lista as empresas `brmf` e `brsp`
- **THEN** o filtro é
  `SysModifiedDateTime gt <instante> and (dataAreaId eq 'brmf' or dataAreaId eq 'brsp')`

### Requirement: Mapeamento do cabeçalho para referência

Cada registro da `FSFiscalDocumentBRs` MUST virar uma referência do tenant com:
- `NaturalKey = <dataAreaId>|<Voucher>` (ex.: `brmf|BRMF21-10000027`);
- `Locator = d365/<dataAreaId>/<FiscalDocumentRecId>`, com o `dataAreaId` codificado para URL. É o
  contrato com a montagem: o RecId é a chave primária do cabeçalho, e o `Voucher` não lidera nenhum
  índice da `FiscalDocument_BR`;
- tipo de documento resolvido pelo `Model` num mapa configurável por tenant, com padrão `55` → NF-e de
  mercadoria, `57` → CT-e, `SE` → NFS-e;
- o grupo da nota, lido do mesmo registro:
  - empresa: o `FiscalEstablishmentCNPJCPF`, só com dígitos;
  - filial: o `FiscalEstablishment`, como veio;
  - data de referência: o dia do `FiscalDocumentDate`, como veio, sem conversão de fuso. É o mesmo campo e a mesma
    regra da montagem;
  - número: o `FiscalDocumentNumber`;
  - modelo: o `Model`, como veio.

O grupo MUST NOT ter valor padrão. Ele é o que a origem traz.

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
