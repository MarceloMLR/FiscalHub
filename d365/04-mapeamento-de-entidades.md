# Fase 4 — Mapeamento de entidades e campos

O que o FiscalHub precisa ler do D365 F&O, e quais entidades o **nosso pacote** precisa expor.

**Princípio:** o pacote leva **todas** as entidades de que precisamos. Nada de depender do que o
implementador do cliente expôs — é exatamente o problema descrito no ADR-0022.

**Fontes.** Cruzamento entre o conector legado (`CallFiscalCompletoAsync` e auxiliares) e os
**metadados reais** do ambiente (`PackagesLocalDirectory`, versão 10.0.2527.160). Onde divergem, vale o
metadado: o legado tem relacionamentos frágeis (seção 9) e busca no cadastro atual dados que a nota já
carrega (seção 3.3).

> **Leia junto:** [`05-achados-de-metadata-e-ciclo-de-deploy.md`](05-achados-de-metadata-e-ciclo-de-deploy.md)
> registra o que quebrou na publicação dessas entidades — campos escondidos por `AccessModifier`,
> `JoinMode` aninhado, e o ciclo build/deploy/sync. Este documento diz **o que** ler; aquele diz
> **o que dá errado** ao expor.

> **Parte II — contábil** (seções 12 a 18): o que o módulo contábil precisa ler, decidido pelo método do
> [`07`](07-como-decidir-quais-entidades-criar.md). Publicadas e aceitas em 2026-10-01 (§17.1).

> **Parte III — inventário** (seções 19 a 25): decidida pelo mesmo método. Publicadas e aceitas em 2026-10-01 (§24.1).

---

## 0. Antes de tudo: referência de package

As tabelas fiscais BR **não estão em `ApplicationSuite`**. Estão no package **`FiscalBooks`**:

```
PackagesLocalDirectory\FiscalBooks\FiscalBooks\AxTable\FiscalDocument_BR.xml
PackagesLocalDirectory\FiscalBooks\FiscalBooks\AxTable\FiscalDocumentLine_BR.xml
```

`TaxTrans`, `TaxTrans_BR`, `MarkupTrans`, `TaxTable` e `TaxWithholdTrans` estão em
`ApplicationSuite\Foundation\AxTable`.

> O model `FiscalHubIntegration` precisa referenciar **`FiscalBooks`** além de `ApplicationSuite`.
> O guia `01-criar-e-publicar-data-entity.md` menciona só `ApplicationSuite` — corrigir.

---

## 1. Resumo — entidades do pacote

| # | Entidade | Origem | Papel na montagem |
|---|---|---|---|
| 1 | `FSFiscalDocumentBR` | `FiscalDocument_BR` | cabeçalho |
| 2 | `FSFiscalDocumentLineBR` | `FiscalDocumentLine_BR` | linhas |
| 3 | `FSFiscalDocumentTaxTransBR` | `FiscalDocumentTaxTrans_BR` | **impostos e retenções da nota** (secão 9) |
| 4 | `FSFiscalDocumentMiscChargeBR` | `FiscalDocumentMiscCharge_BR` | **encargos da nota** (secão 9) |
| 5 | `FSTaxTransBR` | `TaxTrans` + `TaxTrans_BR` | imposto contábil — fora da montagem (9.3) |
| 6 | `FSMarkupTransBR` | `MarkupTrans` | encargo de origem — fora da montagem (9.5) |
| 7 | `FSTaxWithholdBR` | `TaxWithholdTrans` | retenção de pagamento — fora da montagem (9.4) |
| 8 | `FSTaxTableBR` | `TaxTable` | cadastro |
| 9 | `FSPostalAddressBR` | `LogisticsPostalAddress` | cadastro |
| 10 | `FSCustomerBR` | `CustTable` + party | cadastro — escopo reduzido (3.3) |
| 11 | `FSVendorBR` | `VendTable` + party | cadastro — escopo reduzido (3.3) |
| 12 | `FSItemBR` | `InventTable` | cadastro — escopo reduzido (3.3); unidade de estoque e nome na Parte III (22.4) |
| 13 | `FSUnitOfMeasureBR` | `UnitOfMeasure` + tradução | cadastro |
| 14 | `FSAddressCityBR` | `LogisticsAddressCity` | cadastro |
| 15 | `FSCountryRegionBR` | `LogisticsAddressCountryRegion` | cadastro |
| 16 | `FSFiscalDocModelBR` | `FiscalDocModel_BR` | cadastro de modelos |
| 17 | `FSGeneralJournalLineBR` | `GeneralJournalAccountEntry` + cabeçalho | lançamentos contábeis — Parte II (15.1) |
| 18 | `FSMainAccountBR` | `MainAccount` | plano de contas — Parte II (15.2) |
| 19 | `FSCostCenterBR` | `BrazilParameters` → valores da dimensão | centros de custo — Parte II (15.3) |
| 20 | `FSInventOnHandBR` | `InventSum` | saldo atual — Parte III (22.1) |
| 21 | `FSInventTransBR` | `InventTrans` + `InventDim` + `InventTransOrigin` | movimentos depois da data — Parte III (22.2) |
| 22 | `FSInventLocationBR` | `InventLocation` → estabelecimento fiscal | armazém, estabelecimento e propriedade — Parte III (22.3) |

Todas criadas, publicadas e validadas contra o ambiente.
Todas `IsReadOnly = Yes` e `Is Public = Yes`.
**Data management só na `FSFiscalDocumentBR`**, e talvez nem isso (seção 8).

---

## 2. `FSFiscalDocumentBR` — cabeçalho

Tabela com **88 campos**. Projetamos ~30.

### 2.1 ⚠️ `RecId` vs `RefRecId` — não confundir

| Campo | O que é |
|---|---|
| `RecId` | **chave primária da própria tabela**. Campo de *sistema* — não aparece na lista de Fields do designer, mas existe e é por ele que as linhas se ligam |
| `RefRecId` + `RefTableId` | **ponteiro polimórfico para o documento de ORIGEM** (fatura de venda, fatura de compra, journal de estoque…). Não tem relação declarada justamente por apontar para várias tabelas |

O par `RefRecId`/`RefTableId` do cabeçalho tem índice próprio (`Reference`, não-único).

### 2.2 Como expor o RecId — precisa de alias

`RecId` é nome **reservado** na data entity (a entidade já tem o RecId dela, o da view). Tentar criar um
campo com esse nome é rejeitado.

**Passo a passo (evita o erro):**

1. Designer da entidade → botão direito no nó **Fields** *da entidade* → **New** → **Mapped Field**
2. Na janela **Properties** do campo novo, nesta ordem:
   - **Name** → `FiscalDocumentRecId`
   - **Data Source** → `FiscalDocument_BR`
   - **Data Field** → `RecId`
3. Build + Synchronize database

> Arrastar o `RecId` do data source para os Fields também funciona, mas o designer cria já com o nome
> `RecId` e dá erro — daí criar já nomeado é mais limpo.

Na entidade de **linha**, o campo `FiscalDocument` é campo normal (sem conflito). **Dê o mesmo nome**
`FiscalDocumentRecId` para o filtro ficar simétrico:

```
/data/FSFiscalDocumentLineBR?$filter=FiscalDocumentRecId eq 5637148912
```

O alias é obrigatório: a tabela de linha **não tem `Voucher`** (seção 3.1), então não há caminho
alternativo sem join.

### 2.3 Chave natural (`ReplacementKey`)

A tabela declara chave alternativa própria — índice `FiscalDocument_BR`, único:

```
FiscalEstablishment, Direction, Status, FiscalDocumentSeries,
FiscalDocumentNumber, Voucher, ThirdPartyCNPJCPF
```

Usar como `EntityKey` da entidade. É o desenho que o F&O espera.

> ⚠️ **`Status` faz parte da chave única.** O próprio F&O trata *(documento + status)* como identidade.
> Conversa direto com a discussão de chave natural e versionamento do ADR-0023. **Verificar no table
> browser** se uma nota aprovada e depois cancelada gera **duas linhas** ou se é só índice defensivo — se
> gerar duas linhas, muda o desenho do store.

Outros índices úteis: `AccessKeyIdx` (chave de acesso), `FiscalDocumentIssuerSeriesIdx`.

### 2.4 Descoberta e roteamento (ADR-0023, passos 1 e 2)

| Campo | Uso |
|---|---|
| `FiscalDocumentRecId` *(alias de `RecId`)* | correlação com as linhas |
| `Voucher` | `cod_referencia_integracao`; join com `TaxTrans`/`MarkupTrans` |
| `DataAreaId` | empresa |
| `FiscalDocumentNumber` / `FiscalDocumentSeries` | número e série |
| `Model` | **modelo** — roteamento por tenant |
| `Direction` | **entrada / saída** — roteamento |
| `Status` | estado (2.6) |
| `FiscalDocumentIssuer` | `OwnEstablishment` / terceiro |
| `FiscalDocumentDate` | data de emissão |
| `TotalGoodsAmount` | valor das mercadorias. **Lido** desde a change `connector-not-validator`: `totais.valorMercadorias` |
| `AccountingDate` | data contábil (= data de entrada). **Lido** desde a change `connector-not-validator`: vira `dataEntradaSaida` e, pelo mês, `periodoEscrituracao` no payload da Avalara. Na saída, confirmar com o fiscal |
| `ModifiedDateTime` | ✅ **existe** — habilita o poll por janela (seção 8) |
| `FiscalEstablishment` | estabelecimento fiscal |

### 2.5 🔎 Dados desnormalizados — a nota carrega seu próprio snapshot

O cabeçalho **já traz** dados do estabelecimento, do parceiro e dos endereços:

| Campo | O que evita |
|---|---|
| `FiscalEstablishmentCNPJCPF` / `IE` / `Name` / `CCMNum` | consulta ao estabelecimento |
| `FiscalEstablishmentPostalAddress` | FK → `LogisticsPostalAddress.RecId` |
| `ThirdPartyCNPJCPF` | **CNPJ/CPF do parceiro** |
| `ThirdPartyPostalAddress` | FK → `LogisticsPostalAddress.RecId` — **endereço do parceiro na nota** |
| `DeliveryCNPJCPF` / `DeliveryLogisticsPostalAddress` | local de entrega |
| `SalesCarrierLogisticsPostalAddress` | transportadora |
| `CityWhereServicePerformed` | FK → `LogisticsAddressCity.RecId` — município de execução do serviço |

**Por que isso é melhor, não só mais rápido.** O legado lê o cadastro **atual** do parceiro para
preencher o endereço de uma nota **passada**. Se o cliente mudou de endereço desde a emissão, a nota
histórica sai com o endereço errado. Os campos do documento são o snapshot do momento da emissão — que é
o que o fisco espera.

→ Daí a entidade `FSPostalAddressBR` na lista, e a redução de escopo de `FSCustomerBR`/`FSVendorBR`.

### 2.6 Enum `FiscalDocumentStatus_BR`

| Valor | Nome | Legado aceita? | `cod_sit` |
|---|---|---|---|
| 0 | `Blank` | não | — |
| 1 | `Approved` | ✅ | `00` (regular) |
| 2 | `Cancelled` | ✅ | `02` (cancelado) |
| 3 | `Created` | não | — |
| 4 | `Denied` | não | — |
| 5 | `Discarded` | ✅ (entrada: só se `OwnEstablishment`) | `05` (inutilizado) |
| 6 | `Rejected` | ✅ | `05` |
| 7 | `RejectedNoFix` | ✅ | `05` |
| 8 | `Reversed` | ❌ excluído | — |
| 9 | `CancelledBySubstitution` | ❌ **não tratado** | — |

> ⚠️ **Lacuna:** `CancelledBySubstitution` (9) não entra em nenhum filtro do legado. É cancelamento real
> — a nota foi substituída e deixou de valer. Precisa ser tratado junto com `Cancelled`.

As regras de filtro **não vão para a query** — viram configuração de tenant no roteamento (ADR-0023).

### 2.7 Montagem — totais e chaves

`AccessKey`, `SubstitutionAccessKey`, `CancelAccessKey`, `FiscalDocumentAccountNum`, `TotalAmount`,
`TotalGoodsAmount`, `TotalServicesAmount`, `TotalDiscountAmount`, `TotalMarkupOtherAmount`,
`TotalMarkupFreightAmount`, `TotalMarkupInsuranceAmount`.

### 2.8 Relações úteis do cabeçalho

| Relação | Para | Constraint | Serve para |
|---|---|---|---|
| `FiscalDocModel_BR` | `FiscalDocModel_BR` | `Model → Model` | **catálogo de modelos** — ler do ERP em vez de hardcodar |
| `ComplementedFiscalDocument` | `FiscalDocument_BR` | `→ RecId` | **auto-relação** — complementar aponta para o complementado |
| `FiscalEstablishment` | `FiscalEstablishment_BR` | `→ FiscalEstablishmentId` | por ID, não RecId |
| `ThirdPartyPostalAddress` | `LogisticsPostalAddress` | `→ RecId` | endereço do parceiro |
| `LogisticsAddresssCity` | `LogisticsAddressCity` | `CityWhereServicePerformed → RecId` | município do serviço |

### 2.9 CT-e (modelo 57) — só se entrar no escopo

`ThirdPartyAddressCity`, `ThirdPartyAddressState`, `ThirdPartyAddressCountryRegionId`,
`CityKeyWhereServicePerformed`. No legado está comentado.

---

## 3. `FSFiscalDocumentLineBR` — linhas

Tabela com **41 campos**.

### 3.1 ⚠️ Correção de relacionamento

Relação real, pelo metadado:

```
FiscalDocumentLine_BR.FiscalDocument  →  FiscalDocument_BR.RecId
```

**Não** é pelo `Voucher` — e a tabela de linha **não tem campo `Voucher`**. O legado filtra por
`FiscalDocument_Voucher` porque a entidade traz o cabeçalho como **data source adicional** só para
projetar o `Voucher` dele. Isso força um join com a tabela do cabeçalho apenas para filtrar.

**O certo:** expor o campo `FiscalDocument` (FK int64) direto na linha, com alias `FiscalDocumentRecId`.
Some o data source do cabeçalho, some o join, e some o warning `FiscalDocument_Direction` que apareceu
no build.

### 3.2 Campos do payload

| Campo | Uso / destino |
|---|---|
| `FiscalDocumentRecId` *(alias de `FiscalDocument`)* | **filtro** pelo cabeçalho |
| `RefRecId` + `RefTableId` | ref. à linha do documento de origem — join com impostos/encargos (seção 9) |
| `LineNum` | `NUM_ITEM` |
| `ItemId` | `COD_ITEM` |
| `Description` | `DESCR_COMPL` (truncar em 255) |
| `CFOP` | `CFOP`, origem de crédito (1º char), natureza de receita |
| `ServiceCode` | `COD_SERV_MUNIC` |
| `Quantity` | `QTD` |
| `Unit` | `UNID` — relação `Unit → UnitOfMeasure.Symbol`. **Lido**: `unidadeMedida.codigo` (a descrição do `FSUnitOfMeasureBR` depende do idioma, não vai) |
| `UnitPrice` | `VALOR_UNIDADE` / `VL_UNID` (10 casas) |
| `LineAmount` | `VL_ITEM` (2 casas) |
| `Origin` | origem da mercadoria. **Lido**: a Tabela A do CST do ICMS (`situacaoTributariaICMSTabA`), pela tradução do §3.3 |
| `AccountingAmount` | `VL_CONTABIL_ITEM`. **Lido**: `valorContabil` |
| `LineDiscount` | `VL_DESCONTO` |
| `FinancialLedgerDimension` | FK → `DimensionAttributeValueCombination.RecId` |
| `FinancialLedgerDimensionDisplayValue` | `COD_CTA` = `Split('|')[0]` |
| `InventTransId` | rastreio do movimento de estoque |

### 3.3 🔎 A linha carrega o snapshot fiscal — o legado ignora

Vários campos que o legado **hardcoda vazio** ou **busca no cadastro atual do item** existem na própria
linha fiscal:

| No legado | Na `FiscalDocumentLine_BR` |
|---|---|
| `COD_BENEF_FISCAL = ""` | **`BenefitCode`** |
| `ex_tipi = ""` | **`ExceptionCode`** |
| `peso_brt = "0"` / `peso_liq = "0"` (no cabeçalho!) | **`GrossWeight`** / **`NetWeight`** (por linha) |
| NCM do cadastro do item | **`FiscalClassification`** |
| origem do cadastro do item | **`Origin`** |
| tipo do item do cadastro | **`ItemType`** |
| `NUM_DOC_IMP` / `COD_DOC_IMP = ""` | **`DIAddition`** |
| — | `FCINumber`, `TaxSubstitutionCode`, `ApproximateTaxAmount`, `CNPJ`, `FreightNature`, `AssetId` |
| — | `SuframaDiscountICMS` / `PIS` / `COFINS`, `ScaleIndicator`, `ServiceTaxationTypeValue` |
| — | `ICMSSTCollectionPaymentMode`, `RespWithholdingICMSST`, `ICMSSTCollectionPaymentNumber` |

**Mesmo argumento do endereço:** NCM, origem e tipo do item mudam ao longo do tempo. Ler o cadastro
atual para preencher uma nota passada produz documento errado. A linha fiscal tem o valor do momento da
emissão.

**Consequência:** a `FSItemBR` encolhe muito — provavelmente só descrição e unidade de estoque. E vários
campos que hoje saem vazios no payload passam a ter valor real.

> **Verificar no table browser** se esses campos vêm populados no ambiente — alguns podem depender de
> configuração.

**Origem da mercadoria (`Origin`) → Tabela A do CST do ICMS** (change `connector-not-validator`, D12). A OData
entrega o nome do enum. A tradução para o dígito de 0 a 8 da tabela de origem do leiaute só tem as linhas que
a base mostrou, gravadas em 2026-09-26 nas 5 NF-e 55 da `brmf`:

| `Origin` no F&O | Dígito | Tabela de origem do leiaute |
|---|---|---|
| `National` | `0` | Nacional, exceto as indicadas nos códigos 3, 4, 5 e 8 |
| `DirectImport` | `1` | Estrangeira, importação direta, exceto a indicada no código 6 |

Qualquer outro nome deixa a origem **ausente** no domínio, e nunca `0`. Cada valor novo entra com evidência
gravada. O risco está no checklist do primeiro cliente (`docs/STATUS.md`).

### 3.4 Relações da linha

| Relação | Para | Constraint |
|---|---|---|
| `FiscalDocument` | `FiscalDocument_BR` | `FiscalDocument → RecId` |
| `UnitOfMeasure` | `UnitOfMeasure` | `Unit → Symbol` |
| `DimensionAttributeValueCombination` | idem | `FinancialLedgerDimension → RecId` |
| `FBSpedADCRBillCollectionCodeTable_BR` | idem | `BillCollectionCode → Code` |

---

## 4. `FSTaxTransBR` — impostos (`TaxTrans` + `TaxTrans_BR`)

> **Não é a fonte dos impostos da nota.** A montagem usa a `FSFiscalDocumentTaxTransBR`.
> Esta entidade continua útil para o que só existe do lado contábil — ver 9.3.

Relação: `TaxTrans_BR.TaxTrans → TaxTrans.RecId`. Fundir elimina o N+1 que o legado contorna com cache.

### 4.1 De `TaxTrans`

| Campo | Uso |
|---|---|
| `TaxTransRecId` *(alias de `RecId`)* | liga ao `TaxTrans_BR` |
| `Voucher` | filtro pelo voucher do cabeçalho |
| `SourceRecId` + `SourceTableId` | join com a linha ou com o encargo (seção 9) |
| `SourceDocumentLine` | link do source document framework — **avaliar como join melhor** |
| `TaxCode` | agrupamento; join com `TaxTable` |
| `TaxValue` | alíquota |
| `TaxAmount` | valor |
| `TaxBaseAmount` | base |
| `SourceBaseAmountCurRegulated` | base alternativa (ICMS/PIS/COFINS: usa se > 0) |
| `TransDate` | data |

### 4.2 De `TaxTrans_BR`

`TaxType_BR`, `TaxationOrigin_BR`, `TaxationCode_BR`, `TaxBaseAmountExempt_BR`,
`TaxBaseAmountOther_BR`, `TaxAmountOther_BR`, `TaxReductionPct_BR`, `TaxSubstitution_BR`,
`IsICMSDifferenceTax_BR`, `FiscalIndicator_BR`, **`CClassTrib`**, **`CClassTribSuspension`**,
`SourceDeferredAmount_BR`, `DeferredAmountCur_BR` / `MST_BR` / `Rep_BR`.

### 4.3 🔎 Reforma Tributária — já está aqui

Enum `TaxType_BR` na versão do ambiente:

```
Blank, IPI, PIS, ICMS, COFINS, ISS, IRRF, INSS, ImportTax, OtherTax,
INSSRetained, CSLL, ICMSST, ICMSDiff, INSSCPRB, CBS, IBSCity, IBSState
```

**IBS e CBS fluem pela mesma estrutura**, discriminados por `TaxType_BR`. Não é preciso estrutura nova.

1. **IBS vem dividido em `IBSCity` e `IBSState`** (municipal e estadual). O `Goods` do domínio precisa
   tratar os dois.
2. **`CClassTrib`** é o classificador tributário da Reforma, FK → `CClassTribTable_BR.RecId`. Campo
   obrigatório do layout — entra na projeção, e provavelmente exige entidade de apoio para resolver o
   código a partir do RecId.

> ⚠️ **Imposto Seletivo (IS) não aparece** em `TaxType_BR` nesta versão. Verificar se vem em `OtherTax`,
> em outra estrutura, ou se ainda não foi implementado.

O legado **não mapeia nada disso** — é pré-Reforma.

---

## 5. `FSMarkupTransBR` — encargos

> **Não é o encargo da nota.** A montagem usa a `FSFiscalDocumentMiscChargeBR` — ver 9.5.

| Campo | Uso |
|---|---|
| `Voucher` | filtro |
| `TransRecId` + `TransTableId` | join com a linha (seção 9) |
| `MarkupTransRecId` *(alias de `RecId`)* | join com `TaxTrans.SourceRecId` (impostos sobre o encargo) |
| `MarkupClassification_BR` | `Others` / `Freight` / `Insurance` |
| `Value` | valor |
| `TransDate` | data |

> O legado usa `IdxRecId` como chave do encargo. No metadado a identidade é o `RecId`. Confirmar o que a
> entidade exposta no ambiente Volcafe chamava de `IdxRecId`.

---

## 6. Demais entidades

### `FSTaxTableBR` (`TaxTable`)
`TaxCode`, `RetainedTax_BR` (separa imposto retido — condição em quase toda regra), `RevenueCode_BR`.

### `FSTaxWithholdBR` (`TaxWithholdTrans`)
Só nota de serviço de entrada. `VoucherInvoice`, `TaxWithholdCode`, `InvoiceTaxWithholdAmount`,
`InvoiceWithholdBaseAmount`, `TransDate`.

> O legado identifica o imposto por `Contains()` no código — frágil. Verificar se há
> `TaxWithholdType_BR` na `TaxWithholdTable` para discriminar por enum.

### `FSPostalAddressBR` (`LogisticsPostalAddress`)
Resolve os endereços referenciados pelo cabeçalho (2.5). Campos: `RecId` (alias), logradouro, número,
complemento, bairro, CEP, cidade, UF, país.

### `FSCustomerBR` / `FSVendorBR` — **escopo reduzido**
Com CNPJ e endereço vindo do documento (2.5), sobra o que o documento não carrega: **nome/razão social**,
**IE**, **IM** e flags de cadastro. Confirmar caso a caso o que o payload da Avalara exige.

**Usar os mesmos nomes de campo nas duas** — as padrão divergem (`AddressStateId` vs `AddressState`,
`VendorAccountNumber` vs `CustomerAccount`), e alinhar elimina o `qf.Count > 0 ? ... : ...` repetido 12
vezes no legado.

**Por que não usar as padrão:** a doc da Microsoft sobre `CustomersV3` diz *"Do not use where high
performance is required"* e *"Single thread only"* — ~80 campos a mais que as alternativas.

### `FSItemBR` — **escopo reduzido**
Com NCM, origem e tipo vindo da linha (3.3), sobra: `ItemId`, `DataAreaId`, `Description`, `BOMUnitId`
(unidade da lista de materiais, e **não** a de estoque: ver 21.3). Avaliar se `CClassTrib` também vive no item.

### `FSUnitOfMeasureBR`
`Symbol`, `Description`, `TranslatedDescription` (se vazia, usa `"Unidade " + símbolo`).

### `FSAddressCityBR`
`CityKey`, `CountryRegionId`, `BrazilCityCode` (IBGE).

### `FSCountryRegionBR`
`CountryRegion`, `BrazilCentralBankCountryCode`.

### `FSFiscalDocModelBR`
Catálogo de modelos válidos (relação `Model → Model` do cabeçalho). Alimenta o filtro de modelos por
tenant sem hardcode.

---

## 7. Fora do pacote

**`MainAccounts` / `GeneralJournalEntries`** — o `recidGjr` é calculado e **nunca usado**; o bloco de
conta contábil está comentado e a conta vem do `FinancialLedgerDimensionDisplayValue`. Se a dimensão
resolve, saem do escopo.

**`TaxServiceCodeEntities`** — a query devolve o valor que o código já tinha. Provável descarte.

**`FiscalEstablishments`** — dados desnormalizados no cabeçalho (2.5). Pode sair, exceto para resolver o
`DataAreaId` a partir do CNPJ na configuração do tenant.

---

## 8. Change tracking e data management

**Estado atual (implementado e validado):** as 22 entidades (as 16 fiscais e as 6 do contábil e do inventário) estão com
`DataManagementEnabled = No` e **sem staging table** — ver `05`, seção 4. O cabeçalho expõe o campo
de data como **`SysModifiedDateTime`** (alias de `ModifiedDateTime`, seguindo a convenção das
entidades padrão da Microsoft); ver `05`, seção 5.

`FiscalDocument_BR` e `FiscalDocumentLine_BR` têm **`ModifiedDateTime = Yes`** (confirmado no
metadado — a tabela vive no pacote `FiscalBooks`, não no `ApplicationSuite`):

| Opção | Data management | Staging | Como funciona |
|---|---|---|---|
| **A — change tracking** | necessário no cabeçalho | 1 tabela | `odata.track-changes`, delta do servidor |
| **B — janela por data** | **dispensável** | **nenhuma** | `$filter=SysModifiedDateTime gt <último>` |

A opção **B deixa o pacote 100% sem staging** e sem configuração de runtime no cliente. Em troca, a
janela precisa de folga para relógio/latência e pode devolver repetidos — o que a idempotência absorve.

**Recomendação:** começar pela **B**; manter a A como evolução se o volume justificar.
Revisa o item 5 das notas de implementação do ADR-0023.

A **B está destravada**: o `SysModifiedDateTime` responde no ambiente. Falta decidir se a
`FSFiscalDocumentLineBR` também precisa do campo — só faz diferença se a linha puder mudar sem o
cabeçalho ser tocado.

---

## 9. Relacionamento entre documento, impostos e encargos

> Até 2026-09-25 esta seção descrevia um join polimórfico como o caminho da montagem. Ele saiu.

### 9.1 O problema original

O legado junta impostos e encargos à linha usando só o RecId, ignorando o TableId.

| Tabela | Par de identificação |
|---|---|
| `FiscalDocumentLine_BR` | `RefRecId` + **`RefTableId`** |
| `TaxTrans` | `SourceRecId` + **`SourceTableId`** |
| `MarkupTrans` | `TransRecId` + **`TransTableId`** |

RecId não é único entre tabelas — só dentro de cada uma. Se um RecId de encargo coincidir com o
`RefRecId` de uma linha, o imposto é contado duas vezes, em silêncio, num valor fiscal.

Não é hipótese. No levantamento de 2026-09-26, um `MarkupTrans.RecId` da base vale `5637144578` —
exatamente o mesmo número que o RecId de um documento fiscal da mesma base.

### 9.2 A solução: as tabelas fiscais têm FK declarada

A Microsoft modelou isso desde a 10.0.13, em tabelas que **nenhuma entidade padrão expõe no OData**
(conferido: 4.513 entity sets do ambiente, nenhum com `FiscalDocumentTax` nem `MiscCharge`).

| Tabela | Campo | Aponta para |
|---|---|---|
| `FiscalDocumentTaxTrans_BR` | `FiscalDocumentLine` | `FiscalDocumentLine_BR.RecId` |
| | `FiscalDocumentMiscCharge` | `FiscalDocumentMiscCharge_BR.RecId` |
| | `TaxTrans` | `TaxTrans.RecId` |
| `FiscalDocumentMiscCharge_BR` | `FiscalDocumentLine` | `FiscalDocumentLine_BR.RecId` |
| | `MarkupTrans` | `MarkupTrans.RecId` |

São **duas colunas distintas** para linha e encargo. Não há o que desempatar: ou o imposto tem linha
preenchida, ou tem encargo. A colisão de RecId deixa de ser possível.

Daí as entidades `FSFiscalDocumentTaxTransBR` e `FSFiscalDocumentMiscChargeBR`, que expõem
`FiscalDocumentRecId` pelo caminho da linha. O filtro é direto pelo documento — uma chamada, sem
cadeia de `or` e sem montagem em duas fases.

### 9.3 O que muda nos valores (validado em 2026-09-25)

As 547 linhas da `FSFiscalDocumentTaxTransBR` comparadas uma a uma com as correspondentes da
`FSTaxTransBR`, casadas pelo `TaxTransRecId`:

| | |
|---|---|
| casadas 1:1 | 547 de 547 |
| idênticas | 439 |
| **sinal invertido** | **351** |
| **CST do IPI diferente** | **54** |
| valor zerado de um lado só | 54 |

**Sinal.** A contábil grava o imposto de uma saída como crédito, negativo. A fiscal grava positivo,
como a nota apresenta. Pela `FSTaxTransBR` mandaríamos valor negativo para a plataforma, ou
precisaríamos de uma regra de sinal por direção.

**CST do IPI.** Sempre no mesmo par: `01 → 51` (48 registros) e `05 → 55` (6). São os códigos de
entrada contra os de saída equivalentes — em documento de saída a contábil carrega a família de
entrada. **Confirmar com o fiscal**, mas se procede é rejeição na SEFAZ.

**Zerado de um lado.** Nunca dois valores diferentes: sempre zero contra valor. 26 zerados na fiscal
(`ImportTax`, IPI CST 05) e 28 zerados na contábil (PIS/COFINS CST 98). Ou seja, **a tabela fiscal é
a fonte do que vai no documento, mas não é superconjunto** — o imposto de importação só existe do
lado contábil. O `TaxTransRecId` exposto é a ponte para buscar o que falta.

### 9.4 Retenção — não precisa de tabela separada

Não existe tabela de retenção ligada ao documento fiscal, e não é omissão: a `FBTaxWithholdTrans_BR`
é apuração para o SPED, amarrada a período de escrituração e estabelecimento, e a `TaxWithholdTrans`
padrão tem relações declaradas para `CustTrans` e `VendTrans`. O modelo prende retenção à
**transação financeira**, não ao documento.

O que pertence à nota está no flag `RetainedTax` da própria `FiscalDocumentTaxTrans_BR`. Na base: 12
linhas com `RetainedTax = Yes` — 11 de IRRF e 1 de ISS, com base e valor, ligadas à linha pela mesma
FK dos outros impostos. Vem na mesma chamada.

**A `FSTaxWithholdBR` sai do caminho de montagem.** Das 264 linhas, `Source` é `VendPayment` (261) e
`CustPayment` (3) — retenção apurada no pagamento. Zero casam com o `Voucher` do documento fiscal.

### 9.5 Encargo — mesma história

`FSFiscalDocumentMiscChargeBR`, validada em 2026-09-26: 14 encargos, todos `Type = Others`, 14 de 14
chegam à linha e ao documento, em 12 notas.

**A `FSMarkupTransBR` também sai do caminho de montagem.** As 12 linhas dela têm vouchers `INV-*` e
`JPMF-*` — encargos de faturas de venda e journals, não de documentos fiscais. E o campo `MarkupTrans`
dos 14 encargos fiscais está **nulo**; como é coluna da própria tabela fiscal e não do join, é o dado,
não falha de ligação.

### 9.6 O resultado

| ligação | como |
|---|---|
| linha → cabeçalho | FK |
| imposto → linha | FK |
| retenção | mesma FK, flag `RetainedTax` |
| encargo → linha → documento | FK |
| imposto → encargo | FK |
| imposto fiscal → imposto contábil | FK |

Nenhum ponteiro polimórfico na montagem. O `RefRecId`/`RefTableId` continua existindo, mas só para
rastrear a fatura de origem.

Custo por documento: **cabeçalho, linhas, impostos, encargos — 4 chamadas**, mais os cadastros em
cache.

---

## 10. Performance — o que não repetir

1. **`.ToList()` antes do `.Where()`** — puxa a tabela inteira e filtra em memória
   (`FiscalEstablishments`, `DocumentTaxTables`, filtro de `DataAreaId` dos parceiros).
2. **N+1 em `TaxTrans_BR`** — resolvido fundindo na entidade.
3. **Duas queries por item** — resolvido com `FSItemBR`, que agora nem precisa ser consultado na maioria
   dos campos (3.3).
4. **`UnitsOfMeasure` no loop de linhas** — cadastro pequeno e estável, cabe cache por ciclo no hub.
5. **`cross-company=true` em tudo** seguido de filtro em memória por `DataAreaId`.
6. **Join do cabeçalho na entidade de linha** só para filtrar por `Voucher` — usar a FK.
7. **Consulta de cadastro para dados que a nota já tem** — endereço, NCM, origem, tipo (2.5 e 3.3).

---

## 11. Pendências de verificação

### Resolvidas

- [x] **O join polimórfico de impostos e encargos.** Substituído por FK declarada, via
      `FiscalDocumentTaxTrans_BR` e `FiscalDocumentMiscCharge_BR`. A colisão de RecId deixou de ser
      possível. Ver seção 9.
- [x] **Existe tabela de retenção ligada ao documento?** **Não, e não precisa.** O flag
      `RetainedTax` da tabela fiscal de impostos marca as 12 linhas retidas (11 IRRF, 1 ISS). Ver 9.4.

- [x] **`Status` na chave única gera duas linhas?** **Não.** Levantamento sobre as 83 notas do
      ambiente: zero vouchers com mais de uma linha de cabeçalho. Nota cancelada é a mesma linha com
      `Status` alterado. Confirma o desenho do store — identidade estável na `NaturalKey`, status
      como atributo mutável, tentativas numa relação 1:N. Ver `05`, seção 8.
- [x] **`ThirdPartyCNPJCPF` + `ThirdPartyPostalAddress` dispensam a consulta de parceiro?** Para
      nome, CNPJ/CPF e IE do terceiro, **sim** — vêm no cabeçalho. Para o endereço, **parcialmente**:
      `ThirdPartyPostalAddress` e `FiscalEstablishmentPostalAddress` vêm preenchidos, mas
      `DeliveryLogisticsPostalAddress` e `SalesCarrierLogisticsPostalAddress` vêm zerados. Quando o
      endereço de entrega importar, cair para o `PrimaryAddressLocation` do parceiro.
- [x] **Campos fiscais da linha vêm populados?** `FiscalClassification` (NCM), `Origin`, `ItemType`,
      `Unit`, `CFOP` e `ServiceCode` confirmados com valor em notas de mercadoria e de serviço.
      `GrossWeight`, `NetWeight`, `BenefitCode`, `ExceptionCode` e `DIAddition` **não** foram
      verificados — as notas testadas não os exercitam.

**Medido na gravação das fixtures da montagem (2026-09-26, `tools/d365-fixtures`):**

- [x] **Contábil sem par fiscal.** O casamento 547/547 partia da fiscal. No sentido inverso, as linhas
      da `FSTaxTransBRs` dos 83 vouchers fiscais são 547, e **zero** ficam sem linha na fiscal. O
      casamento é 1:1 nos dois sentidos.
- [x] **Nota cancelada mantém as linhas?** Sim. As 2 canceladas da base (`BRMF12-30000000`,
      `BRMF06-110000030`) são **modelo `01`** e têm 1 linha cada. Não há nota `55` cancelada.
- [x] **Data de emissão.** O `FiscalDocumentDateTime` vem com o valor vazio `1900-01-01T00:00:00Z` em
      todas as notas, menos nas `SE` de 2026. O `FiscalDocumentDate` vem sempre preenchido, serializado
      às 12:00 UTC (`2016-03-01T12:00:00Z`).
- [x] **Valores do `MiscChargeType`.** Só `Others` nos 14 encargos.
- [x] **Onde estão os casos da montagem:**
  - as 12 retenções estão em notas `01` e `SE`, nenhuma em `55`. Três IRRF retidos das `SE` de 2026 têm
    valor zero;
  - os 8 `ImportTax` estão em notas `01`, menos um, na nota `55` de importação `BRMF06-110000031`;
  - o `ImportTax` zerado na fiscal mantém a alíquota (30) e zera só a base e o valor.
- [x] **Resolução do `SysModifiedDateTime`.** Segundo, sem fração, nos 83 cabeçalhos.
- [x] **Os 26 zerados na fiscal com valor na contábil, por tipo e CST:**

  | Tipo | CST | Quantos |
  |---|---|---|
  | `ImportTax` | vazio | 8 |
  | IPI | 05 | 6 |
  | PIS/COFINS | 98 | 6 |
  | PIS/COFINS | 99 | 2 |
  | ICMS | 90 | 3 |
  | ICMSDiff | 90 | 1 |

  Todos de entrada ou com CST de "outras operações", suspensão ou não tributada. A montagem só completa
  o `ImportTax`; os demais seguem zerados, como a fiscal os apresenta.

### Em aberto

> Os itens desta lista que são risco para o primeiro cliente, os caminhos que a base não exercita, estão
> consolidados no **checklist do primeiro cliente** em [`docs/STATUS.md`](../docs/STATUS.md), com como se
> prova e qual o sintoma. Um caso novo entra lá; aqui ficam as investigações de metadado.

- [ ] `TaxTrans.SourceDocumentLine` está populado? Se sim, é o join preferido — não-polimórfico
- [ ] Uma nota **com encargo que tenha imposto em cima**. A `FSFiscalDocumentMiscChargeBR` está
      validada (14 encargos, 14 de 14 chegam ao documento), mas zero dos 547 impostos aponta para
      encargo, então o caminho imposto → encargo nunca passou dado
- [ ] Uma nota com encargo cujo `MarkupTrans` esteja preenchido — nos 14 da base está nulo
- [ ] **Os 26 impostos zerados na tabela fiscal e com valor na contábil** (distribuição acima).
      Levantei a hipótese de que reapareciam como encargo e testei: nenhum dos 14 encargos casa com
      um IPI da mesma nota. O padrão por CST (98/99, 90, 05) sugere que a fiscal zera o que a nota não
      tributa, mas isso continua sem confirmação fiscal
- [ ] **CST do IPI**: confirmar com o fiscal que `51`/`55` (saída) é o correto e que `01`/`05` da
      `FSTaxTransBR` seria rejeitado na SEFAZ (9.3)
- [ ] `SysModifiedDateTime` também na `FSFiscalDocumentLineBR`? Só importa se a linha puder mudar
      sem o cabeçalho ser tocado
- [ ] O que a entidade do ambiente Volcafe chamava de `MarkupTrans.IdxRecId` — é o `RecId`?
- [ ] `TaxWithholdTable` tem `TaxWithholdType_BR` para substituir o `Contains()`?
- [ ] **Imposto Seletivo**: onde aparece? Não está em `TaxType_BR` nesta versão
- [ ] `CClassTrib` também existe no item (`InventTable`)?
- [ ] `CClassTribTable_BR` — campos necessários para resolver o código a partir do RecId. Deixou de ser
      pré-requisito da demonstração (ADR-0026 §7)
- [ ] CT-e (modelo 57) entra no escopo agora?

**Pendências de tradução para o payload da Avalara** (change `connector-not-validator`, design D3). Cada item
tem fonte provável no F&O, mas não tem tradução com evidência. Enquanto isso, o campo **não vai** no payload:

- [ ] **Valores do enum `Purpose`** (cabeçalho), para `finalidadeNotaFiscal`. O `1` do exemplo real bate com o
      `finNFe` "normal", mas é um exemplo só.
- [ ] **Valores do enum `PaymentMethod`** (cabeçalho), para `tipoPagamento`.
- [ ] **`ItemType` da linha ou `InventProductType` do item**, para `tipoItem`. Os exemplos batem com o TIPO_ITEM
      do SPED; falta saber qual campo corresponde e com que valores.
- [ ] **Formato do `CreditSourceCode`** (linha), para `origemCredito`. O legado derivava do 1º dígito do CFOP, o
      que seria regra fiscal nossa.
- [ ] **`FiscalDocumentAccountNum`** (cabeçalho) como fonte de `parceiro.codigo`, para a fatia de enriquecimento
      do parceiro.
- [ ] **Idioma do `FSUnitOfMeasureBR`** para a descrição da unidade: a entidade tem `LanguageId`.
- [ ] **`AccountingDate` na nota de saída:** é mesmo a data de saída? Na entrada, é a data de entrada.

---

# Parte II — Módulo contábil

> **Status: publicadas e aceitas em 2026-10-01.** Decidido em 2026-09-30 pelos passos 1 a 5 do
> [`07`](07-como-decidir-quais-entidades-criar.md). XML, privilégios, role, `.rnrproj`, build, deploy e sync em
> 2026-10-01. O aceite do §17 passou inteiro (§17.1), e as três entraram na tabela do §1.
>
> **Como foi medido.** As entidades candidatas não estavam publicadas. As medições usam as entidades padrão que
> projetam as mesmas tabelas 1:1 (`GeneralJournalEntryBiEntities`, `GeneralJournalAccountEntryBiEntities`,
> `GeneralJournalAccountEntryWBiEntities`, `FiscalCalendarPeriodBiEntities`, `MainAccounts`, `OperatingUnits`,
> `FinancialDimensionValues`, `BrazilParameters`) no `fiscosysdev`, empresa `brmf`. Os números do §17 viram o critério
> de aceite das entidades `FS` depois do sync.
>
> **O desenho se apoia no metadado, e não no dado.** Cada junção é uma relação declarada na tabela ou foi copiada de uma
> entidade da Microsoft que funciona (§15.0). O `brmf` é dado de demonstração: serve para provar que as junções trazem
> linha sem multiplicar nem perder (§17), e não para decidir regra de negócio.

## 12. O que o negócio precisa (passo 1)

Fonte: o conector contábil legado da Superior (`Dynamics365Superior`): `CallCentrodeCustoAsync`,
`CallPlanodeContasAsync`, `CallGrupoContabilAsync`, `CallLancamentoContabilAsync` e `CallSaldoContabilAsync`. Ele lê
entidades do implementador (`SESIGeneralJournalEntries`, `SESIGeneralJournalAccountEntries`) e padrão (`MainAccounts`,
`OperatingUnits`, `FiscalEstablishments`).

| Carga | O que vai para a Avalara | De onde o legado lê | O que está errado |
|---|---|---|---|
| Centro de custo | código, descrição, data de atualização | `OperatingUnits` cross-company: **todas** as unidades operacionais, de todos os tipos | código = `PartyNumber`; data fixa `1999-01-01` |
| Plano de contas | código, descrição, natureza, S/A, nível, conta superior e a natureza dela | `MainAccounts` cross-company: **todos os planos de contas** do ambiente | natureza por `ContabilSuporte.IdentificaNatureza(código)`; S/A por `MainAccountType == Total`; nível nulo |
| Grupo contábil | código, descrição, vigência | nada — sintético, `matriz + "_Dynamcis365"` | não lê o ERP |
| Lançamento | código, data, histórico, D/C, conta, sequência, tipo, valor, estabelecimento | cabeçalhos por data e `SubledgerVoucherDataAreaId`, depois **uma chamada por cabeçalho** para as linhas | ver 12.1 |
| Saldo | saldo inicial e final, débitos, créditos, situação, período | `SESIGeneralJournalAccountEntries` filtrada por `HistoricalExchangeRateDate` | **nunca foi entregue**: o método está fora de uso e monta todos os campos vazios |

### 12.1 O lançamento do legado, campo a campo

- **Conta:** `LedgerAccount.Substring(0, IndexOf("|"))`. Depende do delimitador de dimensão que o cliente configurou.
  A FK `GeneralJournalAccountEntry.MainAccount` existe, e o próprio legado a usa no fiscal (`CallItemNota`).
- **D/C:** pelo `IsCredit`.
- **Número:** `SubledgerVoucher`. A sequência reinicia a cada troca de voucher, numa lista ordenada por
  `SubledgerJournalEntry`, e não por voucher.
- **Tipo:** sempre `N`. Abertura e encerramento saem como lançamento normal.
- **Estabelecimento:** o do parâmetro da chamada, em todas as linhas. O `COD_CCUS = filial` é montado e não vai no
  payload.
- **Valor zero:** descartado.
- **Custo extra:** `FiscalEstablishments` inteira em memória para achar a empresa pelo CNPJ, e um
  `MainAccounts.ToList()` que não é usado.

`ContabilSuporte.IdentificaNatureza` não veio no código enviado. Não sei qual regra ela aplica; sei que é uma regra sobre
o **código** da conta (13 e 15.2).

## 13. O caminho padrão (passo 2)

A referência de "como o produto liga essas coisas" no contábil BR não está na documentação de entidades: está no gerador
do SPED ECD da própria localização, `FBSpedFileCreator_Contabil_BR` (e subclasses `_v300` a `_v900`), em
`ApplicationSuite\Foundation\AxClass`. A documentação confirma as duas configurações que ele lê: a dimensão de centro de
custo em **Parâmetros do Brasil** e o conjunto de dimensões do SPED ECD em **Parâmetros de declarações fiscais**.

| O quê | Como o SPED ECD da Microsoft faz | Como o legado faz |
|---|---|---|
| Empresa do lançamento | `GeneralJournalEntry.Ledger == Ledger::current()` | `SubledgerVoucherDataAreaId` |
| Plano da empresa | `MainAccount.LedgerChartOfAccounts == Ledger.ChartOfAccounts`, excluindo `Type = Reporting` | todos os planos |
| Natureza (`COD_NAT`) | `MainAccount.NatureCode_BR` com a feature `MainAccountUpdateNatureCodeFeature_BR` ligada; senão pelo `Type` da conta mais profunda (`getType()`): Asset e BalanceSheet → 01, Liability → 02, Equity → 03, ProfitAndLoss, Expense e Revenue → 04, o resto → 09 | pelo código |
| Sintética/analítica | `S` se tem conta filha pela `ParentMainAccount`, senão `A` | `Type == Total` |
| Nível | profundidade na cadeia `ParentMainAccount` | nulo |
| Conta superior | `ParentMainAccount` | `ParentMainAccountId`, que é o mesmo campo |
| Centro de custo | a dimensão apontada por `BrazilParameters.CostCenterDimensionAttribute` | todas as unidades operacionais |
| Um lançamento (`I200`) | um `GeneralJournalEntry`; número = `SubledgerVoucher`, ou `JournalNumber` quando vazio | um voucher |
| Tipo (`IND_LCTO`) | período `Closing` → `E`. Período `Opening` **não é lançamento**: entra só no saldo inicial | sempre `N` |
| Encerramento | só as linhas com `GeneralJournalAccountEntry_W.IsAccountingClosing_BR = Yes` | todas |
| D/C (`I250`) | **sinal** do `AccountingCurrencyAmount` | `IsCredit` |
| Histórico vazio | nome da conta | vazio |
| Saldo (`I155`) | processo de balancete (`LedgerTrialBalance`) sobre o conjunto de dimensões do SPED, camada `Current` | não entregue |

**Tabelas.** `GeneralJournalEntry` e `GeneralJournalAccountEntry` (pacote `GeneralLedger`), `MainAccount` e `Ledger`
(pacote `Ledger`), `GeneralJournalAccountEntry_W` e `BrazilParameters` (`ApplicationSuite`). As quatro primeiras são
**compartilhadas**, sem `DataAreaId`. A empresa chega por `Ledger.PrimaryForLegalEntity → CompanyInfo.DataArea`, que é
exatamente o que a `GeneralJournalAccountEntryEntity` padrão (não pública) faz, com `PrimaryCompanyContext = DataArea`.

**Chaves.** `GeneralJournalEntry` tem índice único `JournalNumber + FiscalCalendarYear + Ledger`. `MainAccount` tem
chave alternativa `MainAccountId + LedgerChartOfAccounts`.

**Referências do model.** O descritor do `FiscalHubIntegration` já referencia `GeneralLedger`, `Ledger`, `Dimensions` e
`Directory`. Nada a acrescentar.

## 14. O que a medição mostrou (passo 3)

`fiscosysdev`, `brmf`, 2026-09-30. O `brmf` é base de demonstração: os números provam o caminho, e não o volume de um
cliente.

### 14.1 Lançamento

| Medição | Resultado |
|---|---|
| Cabeçalhos por `Ledger` × por `SubledgerVoucherDataAreaId` | 216 × 216, mesmo conjunto. No `brmf` os dois filtros concordam; o do legado é o que não segue o modelo |
| Linhas desses cabeçalhos | 1.307, nenhuma zerada |
| **Conta pelo legado** (`Substring` até o `\|`) | **0 de 1.307 corretas**. O delimitador do ambiente é `-`: o legado mandaria `1.1.4.1.04-Matriz-` como código da conta |
| Conta pela FK `MainAccount` | 1.307 de 1.307 resolvidas no plano `Brasil` |
| `IsCredit` × sinal do valor | 0 divergências; 0 linhas com `IsCorrection = Yes`. O caminho do estorno não foi exercitado |
| Balanceamento | 216 de 216 cabeçalhos somam zero |
| Camada | 216 de 216 em `Current` |
| Tipo de período | 214 cabeçalhos `Operating` (1.198 linhas), 1 `Opening` (39 linhas), 1 `Closing` (70 linhas) |
| `IsAccountingClosing_BR = Yes` | 0. Pela regra da Microsoft, as 70 linhas de encerramento **ficariam fora** do `I200` |
| Voucher × cabeçalho | 215 vouchers para 216 cabeçalhos. O `AB2013` junta o encerramento de 2015-12-31 (`BRMF-000171`, 70 linhas) e a abertura de 2016-01-01 (`BRMF-000172`, 39 linhas) |
| `JournalNumber + FiscalCalendarYear` repetido | 0 |
| Histórico vazio | 56 de 1.307 linhas |
| Centro de custo nas linhas | 150 de 1.307, um valor só (`071`) |

**O que isso quer dizer para o legado.** Numa carga de janeiro de 2016, as 39 linhas de abertura iriam como lançamento
normal, e a plataforma contaria o saldo de abertura duas vezes. Numa carga de dezembro a janeiro, o `AB2013` viraria um
lançamento só, com duas datas e 109 linhas.

### 14.2 Plano de contas

| Medição | Resultado |
|---|---|
| `MainAccounts` cross-company × plano do `brmf` (`Brasil`) | **5.236 × 326**. O ambiente tem 13 planos; o legado manda todos |
| Contas com `ParentMainAccount` | 318 de 326 |
| Nível pela cadeia | 1 = 8, 2 = 13, 3 = 27, 4 = 73, 5 = 205 |
| `NatureCode_BR` preenchido | 0 de 326. A feature está desligada; vale a regra do `Type` |
| Natureza pela regra da Microsoft | 01 = 96, 02 = 51, 03 = 20, 04 = 158, 09 = 1 |
| Uma regra pelo 1º dígito do código | erraria 59 das 219 analíticas: as `2.4.x` são patrimônio líquido (`Equity`) e as `3.x` são despesa (`Expense`) |
| Sintética: legado (`Total`) × Microsoft (tem filha) | 107 × 106. Diverge em `1.1.2.2`: `Total` sem filha |
| Contas usadas em lançamento | 45, todas analíticas pelos dois critérios |

### 14.3 Centro de custo

| Medição | Resultado |
|---|---|
| Dimensão de centro de custo nos Parâmetros do Brasil | `brmf` = `CostCenter`. As outras quatro empresas com o parâmetro: vazio |
| Valores da dimensão `CostCenter` | 20, incluindo os três do Brasil (`071`, `072`, `073`) |
| Unidades operacionais que o legado manda | 91: 49 canais de varejo, 20 centros de custo, 11 departamentos, 10 unidades de negócio, 1 fluxo de valor |
| **`PartyNumber` = `OperatingUnitNumber`** | **0 de 91**. O `071` dos lançamentos casa com o `OperatingUnitNumber` e com o valor da dimensão; com o `PartyNumber`, que é o código do legado, não casa nenhum |

**Isso é estrutura, e não dado de teste.** A view `DimAttributeOMCostCenter` (pacote `SourceDocumentation`), que dá o
valor da dimensão quando o centro de custo é unidade operacional, projeta `Value ← OMOperatingUnit.OMOperatingUnitNumber`
com `OMOperatingUnitType = OMCostCenter`, e `Name ← DirPartyTable.Name`. O `PartyNumber` é outro campo, de outra
sequência. O código do legado não casa com o lançamento em base nenhuma.

### 14.4 Saldo

`GeneralLedgerMainAccountBalanceCDREntities` e `TrialBalanceFiscalYearSnapshots` respondem **0 linhas** no ambiente. Não
há tabela de saldo pronta para ler: a Microsoft calcula o saldo do SPED num processo.

## 15. As entidades (passo 5)

Três entidades. Saldo e grupo contábil ficam sem entidade, de propósito (15.4 e 15.5).

**Diferença de desenho em relação ao fiscal.** O fiscal separa cabeçalho e linhas porque o acesso é por documento. O
contábil é carga manual por período, pelo Agendamento (design D1 da change `module-navigation-and-integration-panel`).
Cabeçalho e linha separados obrigariam a buscar as linhas por cabeçalho — o N+1 do legado — ou por cadeia de `or`. Por
isso a linha carrega o cabeçalho achatado, como a `GeneralJournalAccountEntryEntity` padrão.

**Empresa.** O centro de custo tem `dataAreaId`, como as fiscais. Lançamento e plano de contas saem de tabelas
compartilhadas, sem `DataAreaId`: a empresa vem do `CompanyInfo`, num campo `DataArea`, como na
`GeneralJournalAccountEntryEntity` da Microsoft (`PrimaryCompanyContext = DataArea`). O filtro é
`cross-company=true&$filter=DataArea eq 'brmf'`.

Com esse contexto, o F&O também expõe `dataAreaId` nas duas, com o mesmo valor. Os dois vêm em maiúsculas (`BRMF`), o
filtro não diferencia caixa e filtrar por um ou pelo outro dá o mesmo resultado: 1.307 linhas e 326 contas no `brmf`. O
`DataArea` segue como o filtro documentado, por ser o campo declarado na entidade.

### 15.0 As junções

Cada junção das três entidades, de onde vem e por que não multiplica nem perde linha. A cardinalidade sai dos índices
únicos do metadado, e não do dado.

| Entidade | Junção | Relação no metadado | Cardinalidade | Modo | Copiada de |
|---|---|---|---|---|---|
| Lançamento | linha → cabeçalho | `GeneralJournalAccountEntry.GeneralJournalEntry → GeneralJournalEntry.RecId` | N:1 | interna | `GeneralJournalAccountEntryEntity` |
| Lançamento | cabeçalho → razão | `GeneralJournalEntry.Ledger → Ledger.RecId` | N:1 | interna | idem |
| Lançamento | razão → empresa | `Ledger.PrimaryForLegalEntity → CompanyInfo.RecId` | 1:1 (índice único `PrimaryLegalEntity`) | interna | idem |
| Lançamento | cabeçalho → período | `GeneralJournalEntry.FiscalCalendarPeriod → FiscalCalendarPeriod.RecId` | N:1 | externa (campo não obrigatório) | SPED ECD da Microsoft, em X++ |
| Lançamento | linha → conta | `GeneralJournalAccountEntry.MainAccount → MainAccount.RecId` | N:1 | externa (campo não obrigatório) | sem exemplo; o nome da relação é o da tabela |
| Lançamento | linha → extensão BR | `GeneralJournalAccountEntry_W.GeneralJournalAccountEntry → GeneralJournalAccountEntry.RecId` | 1:0..1 (chave alternativa única) | externa | SPED ECD da Microsoft, em X++ |
| Plano | conta → plano | `MainAccount.LedgerChartOfAccounts → LedgerChartOfAccounts.RecId` | N:1 | interna | `MainAccountEntity` |
| Plano | plano → razão | `Ledger.ChartOfAccounts → LedgerChartOfAccounts.RecId` | 1:N, uma linha por empresa que usa o plano | interna | formato `RecId → campo` da `MainAccountEntity` |
| Plano | razão → empresa | `Ledger.PrimaryForLegalEntity → CompanyInfo.RecId` | 1:1 | interna | `GeneralJournalAccountEntryEntity` |
| Plano | conta → superior | `MainAccount.ParentMainAccount → MainAccount.RecId` | N:0..1 | externa | `MainAccountEntity` |
| Centro de custo | parâmetro → dimensão | `BrazilParameters.CostCenterDimensionAttribute → DimensionAttribute.RecId` | N:1 | interna | `BrazilParametersEntity`, onde é externa |
| Centro de custo | dimensão → valores | `DimensionAttribute.Name = FinancialDimensionValueEntityView.DimensionAttribute` | 1:N (`Name` é chave alternativa única) | interna | **nenhuma: é a única junção sem relação declarada**, porque a view não tem FK |

**Por que interna onde é interna.** Na cadeia até a empresa, linha sem razão ou razão sem empresa não pertence a ninguém,
e a entidade padrão da Microsoft faz igual. No centro de custo, empresa sem a dimensão declarada não tem centro de custo.

### 15.1 `FSGeneralJournalLineBR` — linhas de lançamento

**Papel:** a carga de lançamentos, e a base de um saldo calculado no hub (15.4).

**Origem:** `GeneralJournalAccountEntry` → `GeneralJournalEntry` → `Ledger` → `CompanyInfo` (cadeia copiada da
`GeneralJournalAccountEntryEntity`, com `PrimaryCompanyContext = DataArea`); `GeneralJournalEntry` →
`FiscalCalendarPeriod`; `GeneralJournalAccountEntry` → `MainAccount` e → `GeneralJournalAccountEntry_W` (externa).

| Campo | De | Por quê |
|---|---|---|
| `GeneralJournalAccountEntryRecId` | `GeneralJournalAccountEntry.RecId` | identidade da linha; ordem da sequência dentro do lançamento, como no `I200` |
| `GeneralJournalEntryRecId` | `GeneralJournalAccountEntry.GeneralJournalEntry` | agrupa as linhas: um lançamento = um cabeçalho |
| `JournalNumber` | `GeneralJournalEntry` | número quando o voucher vem vazio; único por exercício e razão |
| `SubledgerVoucher` | `GeneralJournalEntry` | número do lançamento, como no SPED |
| `AccountingDate` | `GeneralJournalEntry` | data do lançamento; filtro do período |
| `FiscalPeriodType` | `FiscalCalendarPeriod.Type` | separa abertura (saldo inicial), encerramento (`E`) e normal (`N`) |
| `IsAccountingClosing` | `GeneralJournalAccountEntry_W.IsAccountingClosing_BR` | a marca de encerramento que o SPED da Microsoft exige |
| `PostingLayer` | `GeneralJournalEntry` | o saldo da Microsoft usa só `Current` |
| `MainAccountId` | `MainAccount.MainAccountId`, pela FK | conta sem depender do delimitador do cliente |
| `AccountingCurrencyAmount` | `GeneralJournalAccountEntry` | valor com sinal; o D/C sai do sinal |
| `Description` | `GeneralJournalAccountEntry.Text` | histórico; mesmo nome da entidade padrão da Microsoft |
| `DataArea` | `CompanyInfo.DataArea` | empresa; é o contexto de empresa da entidade |

**Deixados de fora de propósito:**

- `IsCredit` e `IsCorrection`: o D/C sai do sinal, como no SPED. Com estorno, `IsCredit` dá o lado errado.
- `LedgerAccount`: é texto com o delimitador do cliente. A conta vem pela FK.
- `LedgerDimension` e centro de custo por linha: o lançamento do contrato não leva centro de custo. Acrescentar depois é
  compatível; tirar não é.
- `SubledgerVoucherDataAreaId`: a empresa vem do razão.
- `PostingType`, `JournalCategory`, moeda da transação, moeda de relatório, quantidade, `DocumentNumber`,
  `DocumentDate`: nenhum campo do contrato usa.
- Data de criação: a carga é manual por período, não há poll.

### 15.2 `FSMainAccountBR` — plano de contas

**Papel:** o plano de contas da empresa, e a natureza, o nível e a conta superior que o hub calcula pela árvore.

**Origem:** `MainAccount` → `LedgerChartOfAccounts` (copiada da `MainAccountEntity`) → `Ledger`, pela FK
`Ledger.ChartOfAccounts` → `CompanyInfo`, com `PrimaryCompanyContext = DataArea`. Filtrar por empresa devolve exatamente
o plano dela. Autojunção externa `ParentMainAccount → RecId`, copiada da `MainAccountEntity`, para trazer o código da
superior.

> **Todas as junções são FK declaradas.** A primeira versão ligava `MainAccount` direto ao `Ledger` por igualdade de
> campo, sem relação no metadado. Passando pelo `LedgerChartOfAccounts`, cada salto é uma relação que existe na tabela.
> Sem filtro de empresa, cada conta aparece uma vez por empresa que usa o plano: é o desenho, e não duplicação.

| Campo | De | Por quê |
|---|---|---|
| `MainAccountRecId` | `MainAccount.RecId` | identidade |
| `MainAccountId` | `MainAccount` | código |
| `Name` | `MainAccount` | descrição |
| `MainAccountType` | `MainAccount.Type` | natureza pela regra da Microsoft; o hub exclui `Reporting` |
| `ParentMainAccountId` | superior, pela `ParentMainAccount` | conta superior, sintética/analítica e nível, todos derivados da árvore |
| `NatureCode` | `MainAccount.NatureCode_BR` | natureza declarada, quando a feature estiver ligada |
| `DataArea` | `CompanyInfo.DataArea` | empresa; com `MainAccountRecId`, forma a chave, porque a mesma conta aparece em cada empresa do plano |

**Deixados de fora de propósito:** nome do plano (o filtro por empresa já resolve), categoria, controles de lançamento,
moeda, contas de abertura e consolidação. **Data de alteração:** o legado manda `1999-01-01`; o SPED usa o `ActiveFrom`
do valor de dimensão da conta. Fica fora até o contrato pedir (§16).

**Regras que ficam no hub, e não no XML:** a tradução `Type → COD_NAT` (com a evidência do `getAccountNatureCode` da
Microsoft), a descida até a folha para a natureza da sintética, sintética = tem filha, e o nível pela cadeia.

### 15.3 `FSCostCenterBR` — centros de custo

**Papel:** o cadastro de centros de custo, pela dimensão que a própria empresa declara como centro de custo.

**Origem:** `BrazilParameters` (`PrimaryCompanyContext = DataAreaId`) → `DimensionAttribute`, pela
`CostCenterDimensionAttribute` (bloco copiado da `BrazilParametersEntity`) → `FinancialDimensionValueEntityView`, pelo
nome da dimensão. A view é a mesma que a `FinancialDimensionValues` pública usa, e cobre dimensão de lista existente
(unidade operacional, por exemplo) e de lista personalizada.

> **Junção sem exemplo para copiar:** `DimensionAttribute.Name` → `FinancialDimensionValueEntityView.DimensionAttribute`,
> relação de campo sobre texto. Os dois blocos anteriores vêm de entidades que já funcionam.

| Campo | De | Por quê |
|---|---|---|
| `CostCenterCode` | `DimensionValue` da view | código, o mesmo valor que aparece nas linhas de lançamento |
| `Description` | `Description` da view | descrição |

**Deixados de fora de propósito:** vigência, suspenso, bloqueado, `LegalEntityId` (os valores são compartilhados) e o
nome da dimensão.

**Por que não `OMOperatingUnit`:** só cobre centro de custo que seja unidade operacional, e só com o filtro de tipo que o
legado não faz. **Comportamento esperado:** empresa sem a dimensão nos Parâmetros do Brasil devolve zero linhas. É o
ERP dizendo que não declarou centro de custo.

**Limite conhecido:** na parte de lista existente, a view só traz valores que já têm `DimensionAttributeValue`, que o F&O
cria no primeiro uso. No `brmf`, 20 de 20.

### 15.4 Saldo — sem entidade agora

- O legado nunca entregou saldo, então não há referência do que o negócio aceita.
- A Microsoft calcula o saldo do SPED num processo de balancete, e não lê uma tabela; as tabelas de saldo estão vazias no
  ambiente (14.4).
- A `FSGeneralJournalLineBR` já traz o que um saldo calculado precisa: camada, tipo de período e as linhas de abertura.

**Antes de decidir:** comparar um saldo agregado das linhas com o **Balancete** do `brmf` na tela do F&O.

### 15.5 Grupo contábil — sem entidade

É montado pelo legado sem ler o ERP. Se continuar existindo, é dado do hub.

### 15.6 Fora do pacote

| Entidade | Por que sai |
|---|---|
| `SESIGeneralJournalEntries` e `SESIGeneralJournalAccountEntries` | são do implementador da Superior — o problema do ADR-0022 |
| `OperatingUnits` | substituída pela `FSCostCenterBR` (14.3) |
| `FiscalEstablishments` | a empresa vem do `dataAreaId`, não do CNPJ lido em memória |
| `MainAccounts` | substituída pela `FSMainAccountBR`, que filtra pelo plano da empresa |
| `GeneralJournal*BiEntities` | usadas para medir. São projeções de análise, sem `dataAreaId` (a de linha calcula um JSON de dimensões por linha) e não entram no papel `FSFiscalHubIntegration` |

## 16. Custo da montagem (passo 4)

Carga do histórico inteiro do `brmf`:

| Carga | Legado | Proposto |
|---|---|---|
| Centro de custo | 1 chamada, 91 linhas | 1 chamada, 20 linhas |
| Plano de contas | 1 chamada, 5.236 linhas | 1 chamada, 326 linhas |
| Lançamento | 1 (`FiscalEstablishments`) + 1 (cabeçalhos) + 216 (uma por cabeçalho) + 1 (`MainAccounts` sem uso) = **219** | **1** página de 1.307 linhas |
| **Total** | **~221 chamadas** | **3 chamadas** |

Filtro do lançamento: dois termos fixos, empresa e período. O índice `LedgerAccountingDateIdx` do `GeneralJournalEntry`
começa por `Ledger, AccountingDate`.

Para comparar: só as linhas, buscadas por cabeçalho com cadeia de 20 `or`, custaram 11 chamadas na medição, e mais 11
para a `_W`.

## 17. Critério de aceite depois do sync (§8 do `06`)

O que prova que campos e junções funcionam, valha o dado de teste o que valer:

| Prova | Como | Que erro de junção ela pega |
|---|---|---|
| Contagem da raiz | a entidade na empresa conta o mesmo que a tabela raiz por uma projeção padrão: 1.307 linhas, 326 contas e 20 centros de custo no `brmf` | junção que multiplica aumenta; interna que devia ser externa diminui |
| Chave única | `GeneralJournalAccountEntryRecId` sem repetição; `DataArea + MainAccountId` e `dataAreaId + CostCenterCode` também | duplicação que a contagem total não mostra |
| Lançamento fecha | a soma de `AccountingCurrencyAmount` por `GeneralJournalEntryRecId` é zero nos 216 cabeçalhos | é invariante contábil: linha duplicada ou perdida desequilibra o cabeçalho, com dado real ou de teste |
| Externas resolvem | `MainAccountId` e `FiscalPeriodType` preenchidos em todas as linhas | relação errada, ou campo `Private` escondido (`05`, seção 1) |
| Árvore fechada | todo `ParentMainAccountId` existe no resultado da mesma empresa | autojunção ou filtro por plano errado |
| Empresa sem parâmetro | `FSCostCenterBRs` no `usmf` devolve 0 | junção pelos Parâmetros do Brasil não está filtrando por empresa |

Os valores em si, como quais contas e quais datas, não entram no aceite. Se a contagem mudar ao acrescentar uma junção, a
junção está errada, mesmo com o build verde.

### 17.1 Resultado (2026-10-01)

Rodado depois do build (0 erros), do deploy e do sync, pela pasta `FiscalHub — contábil (3)` do Postman (14 requests,
73 testes, 0 falhas) e conferido por fora com consultas diretas ao OData.

| Prova | Resultado |
|---|---|
| Contagem da raiz | 427.230 = 427.230 entre empresas; no `brmf`, 1.307 linhas, 326 = 326 contas e 20 = 20 centros de custo |
| Chave única | sem repetição nas três |
| Lançamento fecha | os 216 cabeçalhos somam zero |
| Externas resolvem | nenhuma linha sem `MainAccountId` ou `FiscalPeriodType`. Períodos: 1.198 `Operating`, 70 `Closing`, 39 `Opening` |
| Árvore fechada | 318 contas com superior, todas dentro do plano; toda conta usada nos lançamentos existe no plano |
| Empresa sem parâmetro | `FSCostCenterBRs` no `usmf` devolve 0 |

Todos os campos da decisão aparecem no OData; nenhum sumiu por `AccessModifier`.

## 18. Em aberto

As cinco primeiras são regra do hub: dependem do contador e da plataforma, e não mudam o XML. As demais são caminhos que
o dado do `brmf` não exercita.

- [ ] **Abertura como lançamento.** O legado manda as 39 linhas de abertura como `N`; para a Microsoft, elas são saldo
      inicial. Confirmar com o contador o que a plataforma espera.
- [ ] **Encerramento sem a marca.** As 70 linhas do encerramento do `brmf` têm `IsAccountingClosing_BR = No`. A
      Microsoft as tiraria do `I200`. Confirmar se vão como `E` ou não vão.
- [ ] **Chave do lançamento.** Voucher ou cabeçalho, pelo caso `AB2013`. Depende de como a plataforma trata o
      `CodigoLancamento` repetido.
- [ ] **Saldo:** agregado no hub ou entidade própria, depois de comparar com o Balancete (15.4).
- [ ] **Data de alteração** da conta e do centro de custo: o legado manda fixo; o SPED usa o `ActiveFrom`.
- [ ] **`NatureCode_BR` com a feature ligada:** não exercitado; no ambiente, 0 de 326.
- [ ] **Estorno:** 0 linhas com `IsCorrection = Yes`; o D/C pelo sinal não passou por dado real.
- [ ] **Camadas além de `Current`:** 0 no ambiente.
- [ ] **A conta `1.1.2.2`:** `Total` sem filha. Analítica e natureza 09 pela Microsoft, sintética pelo legado.
- [ ] **Volume de cliente real:** linhas por página e tempo da carga de um mês.

**Achado lateral, do fiscal.** O §3.2 diz `COD_CTA = Split('|')[0]` sobre o `FinancialLedgerDimensionDisplayValue`. No
`fiscosysdev` o delimitador é `-`, e 145 das 150 linhas fiscais do `brmf` vêm com o campo vazio. Não afeta o hub hoje —
ele não lê esse campo —, mas a regra escrita não vale neste ambiente.

---

# Parte III — Inventário

> **Status: publicadas e aceitas em 2026-10-01.** Passos 1 a 5 do [`07`](07-como-decidir-quais-entidades-criar.md) feitos em
> 2026-10-01. A D1 do §25 foi decidida pela proposta do §22; as outras ficam para depois, porque não mudam as entidades. XML,
> privilégios, role, `.rnrproj`, build, deploy e sync no mesmo dia. O aceite do §24 passou inteiro (§24.1), e as três
> entraram na tabela do §1.
>
> **Como foi medido.** Com as entidades padrão que projetam as tabelas 1:1 (`InventTransBiEntities`,
> `InventTransOriginBiEntities`, `InventDimBiEntities`, `InventSettlementBiEntities`, `InventTableModuleBiEntities`,
> `InventLocationBiEntities`, `InventTransPostingBiEntities`, `InventoryOnHandForAI`, `WarehousesOnHandV2`,
> `OperationalSitesV2`) e com as FS do fiscal e do contábil, no `fiscosysdev`. O `brmf` tem 260 movimentos de estoque,
> todos financeiros: prova junção e regra, e não volume. Onde o `brmf` não exercita um caminho, a medição usa o `usmf`
> (9.372 movimentos), e a tabela diz.

## 19. O que o negócio precisa (passo 1)

Fonte: `CallInventarioEstoqueAsync`, do conector legado da Superior. Ele lê a `SESIInventTrans` (entidade do
implementador sobre a `InventTrans`) e a `SESIInventTables`, e chama a `CallItemFiscosys` e a `CallParceiro`, que já servem
ao fiscal.

**O que vai para a Avalara:** uma linha por item, com conta contábil, data, estabelecimento, indicador de propriedade,
item (código, NCM, origem, tipo, unidade de estoque), parceiro, quantidade, unidade, valor unitário e valor total. É o
registro `H010` do SPED Fiscal.

**Como o legado monta:**

1. Conta as linhas da `SESIInventTrans` com `DatePhysical <= dtfinal`, empresa `Seb` fixa e um filtro de status.
2. Baixa todas, de todo o histórico da empresa até a data, em páginas por `RecIdCopy1 > último`.
3. Agrupa em memória por `ItemId` e soma `Qty` e `CostAmountPosted + CostAmountAdjustment`. Fica com o que tem quantidade
   e valor positivos.
4. Para cada item, chama a `SESIInventTables` e a `CallItemFiscosys`.
### 19.1 O que está errado

| Ponto | O legado | Efeito |
|---|---|---|
| Empresa | `DataAreaId == "Seb"` e `company=SEB` fixos | ignora a `companhia` do parâmetro |
| Estabelecimento | o do parâmetro (`filial`) em todas as linhas; agrupa só por item | o estoque da empresa inteira sai num estabelecimento só, e o SPED Fiscal é por estabelecimento |
| Data | `DatePhysical` | o valor somado é financeiro (`CostAmountPosted`), cuja data é a `DateFinancial` |
| Status | tira `OnOrder`, `ReservPhysical`, `QuotationIssue`, `ReservOrdered`, `Ordered` e `Received` | deixa passar `Deducted`, `Picked`, `Registered` e `QuotationReceipt`, e tira o `Received`. A quantidade mistura físico e financeiro; o valor é só financeiro |
| Propriedade | `IND_PROP = "0"` e `COD_PARC = ""` fixos | o estoque de terceiros em poder da empresa sai como próprio, e o próprio em poder de terceiros também |
| Conta | `"1.1.02.01.0004"` fixa | uma conta para todos os itens |
| Unidade | `UN_MED ← InventTable.BOMUnitId` | é a unidade da lista de materiais, e não a de estoque. O payload acaba usando a unidade que a `CallItemFiscosys` devolve, e o `UN_MED` nem vai |
| Item | `SESIInventTables` cross-company, filtrada só por `ItemId`, com `FirstOrDefault()` | pode trazer o item de outra empresa |
| Item reaproveitado | `itemie` declarado fora do laço | se a `CallItemFiscosys` não acha o item, a linha sai com o item da linha anterior |
| Parceiro | baixa `AddressCountryRegions`, `VendorsV2` e `CustomersV3` inteiras | sem uso: o `COD_PARC` é sempre vazio |
| Paginação | `RecIdCopy1 > último`, sem `$orderby`, contra uma contagem tirada antes | sem ordem garantida, uma página pode pular linha; se a contagem não bater com o que as páginas trazem, o `Max()` sobre uma página vazia lança exceção |
| Valor unitário | calculado com 6 casas e arredondado para 3 no payload | o SPED da Microsoft grava o `VL_UNIT` com 6 |
| Descarte | posição com quantidade ou valor `<= 0` some sem aviso | a Microsoft corta igual (§20), mas estoque negativo é erro de dado e não aparece em lugar nenhum |
| Erro | `catch` geral devolve `ERRO`; o `retorno` guarda só o último envio | lotes já enviados ficam, e uma falha no meio some |
| Autenticação | usuário e senha (`aadUserName`, `aadRPassword`) | fluxo de senha; o hub usa client credentials |
## 20. O caminho padrão (passo 2)

A referência é o SPED Fiscal da própria localização: `FBSpedFileCreator_Fiscal_BR.createRecordH010` lê a
`FBInventBalance_BR` ("Fiscal books - Inventory on hand information" no Common Data Model), que a
`FBInventBalanceBookProcessor_BR` calcula por período de escrituração (`FBBookingPeriod_BR`, um por estabelecimento). A
página de escopo da localização confirma o Bloco H (`H001`, `H005`, `H010`, `H020`, `H030`, `H990`), com o `H005` só para
os motivos 01, 05 (RS) e 06. A página do SPED Fiscal confirma que o arquivo é por estabelecimento.

| O quê | Como a Microsoft faz | Como o legado faz |
|---|---|---|
| Saldo na data | `InventSumDateEngine`, o motor do relatório de estoque físico por dimensão com data: saldo atual da `InventSum` menos os movimentos com `DateFinancial` depois da data | soma todo o histórico até a data, por `DatePhysical` |
| Quantidade do `H010` | `PostedQty`, só a financeira. Guarda a física (`PostedQty + Received - Deducted`) em `PhysicalOnHandQty`, sem reportar | mistura físico e financeiro (19.1) |
| Valor | `CostAmountPosted + CostAmountAdjustment` | igual |
| Estabelecimento | site do armazém → `FiscalEstablishmentInventSite_BR` → `FiscalEstablishment_BR` | o do parâmetro |
| Propriedade (`IND_PROP`) | `InventLocation.InventCountingGroup_BR` do armazém: `OwnStock` → 0, `OwnStockInOtherPower` → 1, `OtherStock` → 2 | `0` fixo |
| Parceiro (`COD_PART`) | `InventLocation.CustAccount_BR`, senão `VendAccount`, quando o estoque não é próprio | vazio |
| Unidade | `InventTableModule` do módulo `Invent` (`inventTableModuleInvent().UnitId`) | `BOMUnitId` |
| Conta (`COD_CTA`) | perfil de lançamento do item (`InventPosting`): `PurchReceipt` do item, `SalesRevenue` do item, depois `PurchReceipt`, `SalesRevenue` e `InventReceipt` do grupo ou de todos | fixa |
| Corte | só posição com quantidade e valor positivos | igual |
| Data do inventário | 31/12, reportado no SPED de fevereiro (`getInventoryReportingDate`) | `dtfinal` |

**O caminho padrão depende dos Livros fiscais.** A `FBInventBalance_BR` só é calculada para quem roda os Livros fiscais
no D365: período de escrituração e apuração de IPI ou ICMS (`processAssessmentPeriodInventory`). Depois que a apuração
fecha, o recálculo é bloqueado. Quem manda o SPED para a Avalara provavelmente não roda. A tabela não tem entidade pública
e o table browser pede login: não medi se ela tem linha no `fiscosysdev`.

**Duas notas do metadado.**

- A relação `FBInventBalance_BR → InventTable` da Microsoft liga `ItemId` a `dataAreaId`, uma restrição errada. Uma
  entidade sobre ela teria de levar a empresa pelo `FBBookingPeriod_BR.FiscalEstablishmentDataArea`.
- A `InventSum` tem `InventSiteId` e `InventLocationId` desnormalizados. A `InventTrans` não tem, e precisa da `InventDim`.

**O que a documentação avisa.** A limpeza das linhas zeradas da `InventSum`, pela verificação de consistência no modo de
correção ou pelos jobs de limpeza, apaga linhas de que os relatórios "na data" dependem. A Microsoft lista o relatório de
estoque físico por dimensão entre os afetados. O motor dela parte das linhas da `InventSum`: uma posição que zerou e teve a
linha apagada some do passado.

**O que não serve.** O *Inventory value report storage* guarda o relatório de valor de estoque numa entidade, mas ela é de
Data management, não está no service document e depende de alguém rodar o relatório.
## 21. O que a medição mostrou (passo 3)

`fiscosysdev`, 2026-10-01.

### 21.1 Estabelecimento

| Medição | Resultado |
|---|---|
| Sites do `brmf` × estabelecimentos (`OperationalSitesV2`, que lê a `FiscalEstablishmentInventSite_BR`) | 4 × 4, um para um: `Matriz`, `SP-01`, `SAL-01` e `RJ-01` |
| Estabelecimento da nota × site do movimento de estoque da linha | 66 de 66 linhas concordam (52 `Matriz`, 14 `SAL-01`) |
| Posições com saldo em 2016-12-31 | 10 (estabelecimento, item): `Matriz` com 7 itens e R$ 41.198,22; `SAL-01` com 3 itens e R$ 56.535,87 |
| O legado em 2016-12-31 | 8 linhas e R$ 97.734,09, todas no estabelecimento do parâmetro. O total bate; a divisão, não: `BRMF010` e `BRMF020` têm saldo nos dois estabelecimentos, e o `BRMF030` só no `SAL-01` |

### 21.2 Data e status

| Medição | Resultado |
|---|---|
| Status no `brmf` | 101 `Purchased` e 159 `Sold`; nenhum movimento só físico. A diferença de status do legado não aparece aqui |
| Status no `usmf` que o filtro do legado deixa passar | `Deducted`: 298 linhas, −20.939,2 unidades, custo físico −924.108,53, financeiro 0. `Registered`: 18, +2.421. `QuotationReceipt`: 3, +18, e é cotação. `Picked`: 2 |
| Status no `usmf` que o legado tira | `Received`: 322 linhas, +24.399,2 unidades, custo físico 2.622.792,11 |
| `DatePhysical` ≠ `DateFinancial` nas linhas financeiras | `brmf` 0; `usmf` 1.764 |
| Total do legado × saldo financeiro por item no `brmf` | iguais em 2015-12-31, 2016-12-31 e hoje. Com todo movimento financeiro e as duas datas iguais, as duas regras coincidem |
| Saldo financeiro por armazém hoje × `WarehousesOnHandV2` | 12 de 12 iguais |
| Data do ajuste de custo | 122 liquidações com ajuste lançado em mês posterior ao da data financeira do movimento. O ajuste fica no movimento (`CostAmountAdjustment`), qualquer que seja a data da liquidação: o valor de 31/12 calculado em fevereiro já carrega os ajustes de janeiro. A Microsoft faz igual. Em 2015-12-31, no `brmf`, esses ajustes somam 0 |

### 21.3 Propriedade, unidade e conta

| Medição | Resultado |
|---|---|
| Armazéns do `brmf` | 12: 10 de estoque próprio, 1 `OwnStockInOtherPower` (`C-000002`, cliente `BRMF-000002`) e 2 de trânsito (`999` e `888`). Nenhum movimento no `C-000002`: o caminho do `IND_PROP = 1` existe na configuração e não foi exercitado |
| Movimentos sem armazém | 22, todos `SummedUp` (fechamento de estoque), somando 0 em quantidade e valor. A Microsoft os perde, porque liga o saldo ao armazém por junção interna |
| `BOMUnitId` × unidade de estoque (`InventTableModule`, `Invent`) | 11 de 12 itens do `brmf` com `BOMUnitId` vazio. Só o `BRMF080` tem, e é igual à de estoque |
| Conta das entradas e baixas financeiras de estoque | 5 contas, pelo tipo de item: `1.1.3.1.01` Mercadorias para revenda, `1.1.3.2.01` Produtos acabados, `1.1.3.3.01` Matérias-primas, `1.1.3.8.01` Estoques diversos e `1.1.3.9.01` Importações em andamento |
| A conta fixa do legado (`1.1.02.01.0004`) no plano do `brmf` | não existe |

**Consequência para o fiscal.** O §6 chamava a `FSItemBR.BOMUnitId` de unidade de estoque, e ela não é. O hub não a usa (a
unidade da nota vem da `FSFiscalDocumentLineBR.Unit`), mas o inventário precisa da unidade de estoque de verdade.
## 22. As entidades (passo 5) — proposta

Decidido pela D1 do §25, em 2026-10-01. É o caminho que não depende dos Livros fiscais: o hub reconstrói o saldo na data
como o motor da Microsoft, saldo atual menos os movimentos depois da data, e a regra fica no hub, e não em X++.

**Uma diferença de propósito em relação à Microsoft.** O motor dela parte das linhas da `InventSum` e perde a posição cuja
linha a limpeza apagou (§20). O hub junta as duas listas pela chave (item, site, armazém): uma posição que zerou e não tem
mais linha na `InventSum` volta pelos movimentos.

### 22.0 As junções

| Entidade | Junção | Relação no metadado | Cardinalidade | Modo |
|---|---|---|---|---|
| Saldo atual | nenhuma | a `InventSum` já traz site e armazém | — | — |
| Movimento | movimento → dimensão | `InventTrans.inventDimId → InventDim.inventDimId` | N:1 | interna |
| Movimento | movimento → origem | `InventTrans.InventTransOrigin → InventTransOrigin.RecId` | N:1 | interna |
| Armazém | armazém → vínculo do site | `InventLocation.InventSiteId` = `FiscalEstablishmentInventSite_BR.InventSite`, as duas declaradas contra `InventSite.SiteId` | N:0..1 (índice único `InventSiteIdx`) | externa |
| Armazém | vínculo → estabelecimento | `FiscalEstablishmentInventSite_BR.FiscalEstablishment_BR → FiscalEstablishment_BR.RecId` | N:1 | externa |
| Item | item → módulo de estoque | relação `InventTable` da `InventTableModule`, com `ModuleType = Invent` | 1:1 (índice único `ItemModuleIdx`) | externa |
| Item | item → idioma do sistema | `InventTable.Product → EcoResProductSystemLanguage.Product` (view sobre `EcoResProduct` e `SystemParameters`) | 1:1 | externa |
| Item | item → nome | `InventTable.Product → EcoResProductTranslation.Product` e `EcoResProductSystemLanguage.SystemLanguageId → EcoResProductTranslation.LanguageId` | 1:0..1 (índice único `ProductLanguageIdx`) | externa |

A cadeia site → estabelecimento é a da `InventOperationalSiteV2Entity` (`OperationalSitesV2`). O módulo de estoque é copiado da `EcoResReleasedProductV2Entity`, e o nome, da `EcoResProductV2Entity`.

### 22.1 `FSInventOnHandBR` — saldo atual

**Papel:** o ponto de partida do saldo na data.

**Origem:** `InventSum`, com `PrimaryCompanyContext = DataAreaId`, sem junção. **Chave:** `ItemId + InventDimId` (índice
único `ItemDimIdx`).

**Campos:** `ItemId`, `InventDimId`, `InventSiteId`, `InventLocationId`, `PostedQty`, `PostedValue`, `Received`,
`Deducted`, `Registered`, `Picked`, `PhysicalValue` e `Closed`.

- `PostedQty` e `PostedValue` são o saldo financeiro, que é a regra da Microsoft.
- `Received`, `Deducted`, `Registered`, `Picked` e `PhysicalValue` deixam o hub calcular o físico, se a D2 pedir.
- `Closed` diz se a linha está fechada. A Microsoft só lê linha fechada quando há movimento depois da data; o hub lê todas.

**Fora:** reservas, pedidos e cotações (`ReservPhysical`, `OnOrder`, `Ordered`, `Arrived` e os de cotação), que não são
estoque; e as dimensões de rastreio (lote, série, localização), que o `H010` não usa.

### 22.2 `FSInventTransBR` — movimentos depois da data

**Papel:** o que desfazer do saldo atual para chegar à data. A carga filtra `DateFinancial gt <data> or DatePhysical gt
<data>`, e o volume é o que aconteceu depois da data, e não o histórico inteiro. As duas datas têm índice que começa por
elas. A data volta no OData às 12:00Z (`...-12-31T12:00:00Z`): o corte tem de ser depois do dia, e não depois da
meia-noite, ou o próprio dia entra como "depois".

**Origem:** `InventTrans` → `InventDim` (site e armazém) → `InventTransOrigin` (referência). **Chave:**
`InventTransRecId`.

**Campos:** `InventTransRecId`, `ItemId`, `InventSiteId`, `InventLocationId`, `Qty`, `StatusReceipt`, `StatusIssue`,
`DatePhysical`, `DateFinancial`, `CostAmountPosted`, `CostAmountAdjustment`, `CostAmountPhysical`, `PackingSlipReturned`,
`ReferenceCategory` e `ReferenceId`.

- Quantidade, status, datas, custo financeiro, ajuste e `PackingSlipReturned` são o que o `InventSumDateEngine` usa para
  desfazer. O `PackingSlipReturned` tem tratamento próprio lá.
- `CostAmountPhysical` serve à regra física (D2).
- A referência rastreia a posição até o documento.

**Fora:** os campos `_RU` e os de liquidação (`ValueOpen`, `QtySettled`, `CostAmountSettled`), que são do fechamento de
estoque, e não do saldo.
### 22.3 `FSInventLocationBR` — armazém, estabelecimento e propriedade

**Papel:** leva a posição (site, armazém) ao estabelecimento e diz de quem é o estoque.

**Origem:** `InventLocation` → `FiscalEstablishmentInventSite_BR` (pelo site, externa) → `FiscalEstablishment_BR`
(externa). **Chave:** `InventLocationId`.

**Campos:** `InventLocationId`, `Name`, `InventSiteId`, `InventLocationType`, `FiscalEstablishmentId`,
`InventCountingGroup` (de `InventCountingGroup_BR`), `CustAccount` (de `CustAccount_BR`) e `VendAccount`.

- **Externa de propósito:** armazém de site sem estabelecimento ainda é estoque da empresa. O hub mostra a posição sem
  estabelecimento, em vez de perdê-la, como faria uma junção interna.
- `InventLocationType` separa o armazém de trânsito (D6).
- O parceiro do estoque em poder de terceiros se resolve pelas `FSCustomerBR` e `FSVendorBR`, que já existem.

### 22.4 `FSItemBR` — o que muda

- **`InventUnitId`**, de `InventTableModule.UnitId` com `ModuleType = Invent`: a unidade de estoque, que é a do `H010` e
  do `0200` na Microsoft.
- **`ProductName`:** a `FSItemBR` não tinha nome, só `NameAlias`, e o inventário não tem nota de onde tirar a descrição.
  Vem da tradução do produto no idioma do sistema (`EcoResProductTranslation` pela view `EcoResProductSystemLanguage`),
  como na `EcoResProductV2Entity` e no SPED da Microsoft com o parâmetro `UseProductName`. A tradução é única por produto e
  idioma, então não multiplica a linha. No `fiscosysdev` o idioma do sistema é `en-us`, e os 12 itens do `brmf` têm
  tradução nele, com o nome em português; 9 têm também `zh-hans`, que a junção deixa de fora.
- **`BOMUnitId` fica**, porque tirar quebra o contrato. O §6 deixa de chamá-la de unidade de estoque.
- **`ItemGroupId`**, só se a D3 for pelo perfil de lançamento.

Acrescentar campo não quebra quem já consome a `FSItemBR`, mas pede build e deploy.

### 22.5 Fora, de propósito

| Tabela ou entidade | Por quê |
|---|---|
| `FBInventBalance_BR` | é o caminho da Microsoft, mas só existe para quem roda os Livros fiscais (D1) |
| `SESIInventTrans` e `SESIInventTables` | são do implementador; nome e campos mudam por cliente |
| `InventSettlement` | a data do ajuste não muda o saldo, nem na Microsoft (21.2) |
| `AddressCountryRegions`, `VendorsV2` e `CustomersV3` | o parceiro sai do armazém, e o cadastro já tem `FSCustomerBR` e `FSVendorBR` |
| `WarehousesOnHandV2` e `InventoryOnHandForAI` | só quantidade, sem valor |
| *Inventory value report storage* | é de Data management e depende de rodar o relatório |
| `InventPosting` | depende da D3 |

## 23. Custo da montagem (passo 4)

| Carga | Legado | Proposta |
|---|---|---|
| `brmf` em 2016-12-31 | 1 contagem + 1 página (259 movimentos, o histórico inteiro) + 8 itens × 2 (`SESIInventTables` e `CallItemFiscosys`) + 3 cadastros inteiros = **21**, sem contar o que a `CallItemFiscosys` chama por dentro | 1 saldo atual (24 linhas) + 1 movimentos depois da data (1) + 1 armazéns (12) + 1 itens = **4**. Mais `FSCustomerBR` ou `FSVendorBR` só se houver saldo em armazém de terceiros |
| Cliente com anos de histórico | cresce com o histórico inteiro, uma página por 10 mil movimentos, mais duas chamadas por item | cresce com o saldo atual e com o que aconteceu depois da data |

## 24. Critério de aceite depois do sync

| Prova | Como | Que erro ela pega |
|---|---|---|
| Contagem da raiz | `FSInventOnHandBRs` conta o mesmo que a `InventoryOnHandForAI`, que é a `InventSum` sem filtro (24 no `brmf`); `FSInventTransBRs`, o mesmo que a `InventTransBiEntities` (260) | junção que multiplica ou corta |
| Chave única | `ItemId + InventDimId`, `InventTransRecId` e `InventLocationId` sem repetição | duplicação que a contagem não mostra |
| Externas resolvem | todo movimento com site; os 12 armazéns do `brmf` com estabelecimento | relação errada |
| Saldo na data | o hub reconstrói 2016-12-31 e chega às 10 posições do 21.1, R$ 97.734,09 no total | a regra de desfazer |
| Saldo de hoje | a quantidade física da `FSInventOnHandBRs` (`PostedQty + Received − Deducted + Registered − Picked`) igual à `OnHandQuantity` da `WarehousesOnHandV2`, por armazém e item (12 de 12) | campo errado da `InventSum` |
| Item | `FSItemBRs` continua com 12 itens no `brmf`; `InventUnitId` e `ProductName` preenchidos nos 12 | junção do módulo ou da tradução que multiplica ou não resolve |

### 24.1 Resultado (2026-10-01)

Rodado depois do build, do deploy e do sync, pela pasta `FiscalHub — inventário (3)` do Postman (17 requests, 93 testes,
0 falhas), e conferido por fora com consultas diretas ao OData.

| Prova | Resultado |
|---|---|
| Contagem da raiz | no `brmf`, 24 = 24 linhas de saldo, 260 = 260 movimentos, 12 = 12 armazéns e 12 = 12 itens |
| Chave única | sem repetição nas quatro |
| Externas resolvem | os 260 movimentos com site e referência; os 12 armazéns com estabelecimento; todo armazém do saldo e dos movimentos está na `FSInventLocationBRs` |
| Propriedade | 9 armazéns próprios, 2 de trânsito (`999` e `888`) e 1 com estoque próprio em poder de terceiro (`C-000002`, `OwnStockInOtherPower`, conta `BRMF-000002`) |
| Saldo de hoje | 12 de 12 combinações armazém × item iguais à `WarehousesOnHandV2` |
| Saldo na data | 1 movimento financeiro depois de 2016-12-31 (compra `BRMF-000038` em 2017-01-15: 10 × `BRMF030`, R$ 5.850,00), e o filtro com `ge` trouxe esse mesmo. Saldo atual menos esse movimento é igual ao histórico até a data em todas as posições: 10 posições, R$ 97.734,09 (`Matriz`: 7 itens, R$ 41.198,22; `SAL-01`: 3 itens, R$ 56.535,87). Por fora: R$ 103.584,09 de saldo atual − R$ 5.850,00 = R$ 97.734,09 |
| Item | `FSItemBRs` com 12 itens no `brmf`; `InventUnitId` (`pcs`, `hr`, `un`) e `ProductName` preenchidos nos 12 |

Todos os campos da decisão aparecem no OData; nenhum sumiu por `AccessModifier`. A collection inteira também rodou depois
do deploy (54 requests): fiscal, regressão e contábil continuam verdes. A única falha foi o `Get Token`, que nessa execução
roda sem segredo porque o token vem do `az`.

## 25. Decisões antes do XML

- [x] **D1 — De onde vem o saldo.** Decidido em 2026-10-01: (b).
  - (a) `FBInventBalance_BR`: o `H010` pronto da Microsoft, por estabelecimento, com propriedade, parceiro, unidade e
    conta, numa chamada. Só existe se o cliente roda os Livros fiscais no D365.
  - (b) A proposta do §22: não depende dos Livros fiscais, e o hub reconstrói a data.
  - Recomendo (b): quem manda o SPED para a Avalara provavelmente não roda os Livros fiscais, e (a) não pôde ser medida.
- [ ] **D2 — Quantidade financeira ou física.** A Microsoft reporta só a financeira (`PostedQty`) e guarda a física à
  parte; o legado mistura as duas. Mercadoria recebida sem a nota lançada entra no inventário? É pergunta para o contador.
  As entidades levam as duas, e o hub começa pela financeira.
- [ ] **D3 — Conta contábil (`COD_CTA`).**
  - (a) Perfil de lançamento, na ordem da Microsoft: uma entidade sobre a `InventPosting` e o grupo do item na `FSItemBR`.
  - (b) Configuração por tenant no hub.
  - (c) Fora, como no legado, que manda uma conta que nem existe no `brmf`.
- [ ] **D4 — Motivo do inventário (`H005`).** O legado não manda. A Microsoft gera o motivo 01 (31/12) e, por estado, o 05
  e o 06. É regra do hub e não muda entidade.
- [ ] **D5 — Estoque negativo e posição zerada.** A Microsoft e o legado descartam sem aviso. O hub deve mostrar o que
  ficou de fora.
- [ ] **D6 — Trânsito.** Mercadoria em armazém de trânsito (`999` e `888` no `brmf`) na data cai no estabelecimento do site
  do armazém de trânsito, como na Microsoft. Confirmar com o contador. Não exercitado: nenhum saldo em trânsito nas datas
  medidas.
