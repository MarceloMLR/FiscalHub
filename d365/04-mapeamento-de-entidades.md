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
| 12 | `FSItemBR` | `InventTable` | cadastro — escopo reduzido (3.3) |
| 13 | `FSUnitOfMeasureBR` | `UnitOfMeasure` + tradução | cadastro |
| 14 | `FSAddressCityBR` | `LogisticsAddressCity` | cadastro |
| 15 | `FSCountryRegionBR` | `LogisticsAddressCountryRegion` | cadastro |
| 16 | `FSFiscalDocModelBR` | `FiscalDocModel_BR` | cadastro de modelos |

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
(unidade de estoque, base da conversão). Avaliar se `CClassTrib` também vive no item.

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

**Estado atual (implementado e validado):** as 14 entidades estão com
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
