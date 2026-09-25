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

| # | Entidade | Origem | Status |
|---|---|---|---|
| 1 | `FSFiscalDocumentBR` | `FiscalDocument_BR` | ✅ criada — revisar campos |
| 2 | `FSFiscalDocumentLineBR` | `FiscalDocumentLine_BR` | ✅ criada — **corrigir join** |
| 3 | `FSTaxTransBR` | `TaxTrans` + `TaxTrans_BR` | criar |
| 4 | `FSMarkupTransBR` | `MarkupTrans` | criar |
| 5 | `FSTaxTableBR` | `TaxTable` | criar |
| 6 | `FSTaxWithholdBR` | `TaxWithholdTrans` | criar |
| 7 | `FSPostalAddressBR` | `LogisticsPostalAddress` | criar — endereços do documento |
| 8 | `FSCustomerBR` | `CustTable` + party | criar — **escopo reduzido** (3.3) |
| 9 | `FSVendorBR` | `VendTable` + party | criar — **escopo reduzido** (3.3) |
| 10 | `FSItemBR` | `InventTable` | criar — **escopo reduzido** (3.3) |
| 11 | `FSUnitOfMeasureBR` | `UnitOfMeasure` + tradução | criar |
| 12 | `FSAddressCityBR` | `LogisticsAddressCity` | criar |
| 13 | `FSCountryRegionBR` | `LogisticsAddressCountryRegion` | criar |
| 14 | `FSFiscalDocModelBR` | `FiscalDocModel_BR` | criar — catálogo de modelos |

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
| `AccountingDate` | data contábil (= data de entrada) |
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
| `Unit` | `UNID` — relação `Unit → UnitOfMeasure.Symbol` |
| `UnitPrice` | `VALOR_UNIDADE` / `VL_UNID` (10 casas) |
| `LineAmount` | `VL_ITEM` (2 casas) |
| `AccountingAmount` | `VL_CONTABIL_ITEM` |
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

### 3.4 Relações da linha

| Relação | Para | Constraint |
|---|---|---|
| `FiscalDocument` | `FiscalDocument_BR` | `FiscalDocument → RecId` |
| `UnitOfMeasure` | `UnitOfMeasure` | `Unit → Symbol` |
| `DimensionAttributeValueCombination` | idem | `FinancialLedgerDimension → RecId` |
| `FBSpedADCRBillCollectionCodeTable_BR` | idem | `BillCollectionCode → Code` |

---

## 4. `FSTaxTransBR` — impostos (`TaxTrans` + `TaxTrans_BR`)

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

## 9. ⚠️ O relacionamento frágil do legado

**O legado junta impostos e encargos à linha usando só o RecId, ignorando o TableId.**

| Tabela | Par de identificação |
|---|---|
| `FiscalDocumentLine_BR` | `RefRecId` + **`RefTableId`** |
| `TaxTrans` | `SourceRecId` + **`SourceTableId`** |
| `MarkupTrans` | `TransRecId` + **`TransTableId`** |

O `TableId` existe porque os campos são **polimórficos**: apontam para linhas de tabelas diferentes
conforme a origem do documento. RecId não é único entre tabelas — só dentro de cada uma.

O cenário de colisão está no próprio código:

```csharp
taxTransList   = allTaxTrans.Where(x => x.SourceRecId == docite.RefRecId);   // impostos da linha
taxTransForMkt = allTaxTrans.Where(x => x.SourceRecId == mkt.IdxRecId);      // impostos do encargo
```

No mesmo voucher existem `TaxTrans` apontando para **tabelas diferentes** (a linha e o `MarkupTrans`).
Se um RecId de encargo coincidir com o `RefRecId` de uma linha, o imposto é contado duas vezes — em
silêncio, num valor fiscal.

**Correção — feita e validada.** Os `TableId` estão expostos e o join usa o par completo. Testado
contra o ambiente na nota `BRMF21-10000027`: `SourceRecId` + `SourceTableId` atribuíram 4 impostos a
cada uma das 2 linhas, somando os mesmos 8 que o filtro por `Voucher` devolve em bloco. Ver `05`,
seção 7.

**A investigar:** `TaxTrans.SourceDocumentLine` é o link do *source document framework*. Se estiver
populado nos documentos fiscais, é um join mais limpo e não-polimórfico.

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

### Em aberto

- [ ] `TaxTrans.SourceDocumentLine` está populado? Se sim, é o join preferido — não-polimórfico
- [ ] Uma nota **com encargo** (frete/seguro) para exercitar o join de `FSMarkupTransBR`; o caminho
      por `TransRecId` + `TransTableId` está validado, mas as notas testadas têm markup zerado
- [ ] `SysModifiedDateTime` também na `FSFiscalDocumentLineBR`? Só importa se a linha puder mudar
      sem o cabeçalho ser tocado
- [ ] O que a entidade do ambiente Volcafe chamava de `MarkupTrans.IdxRecId` — é o `RecId`?
- [ ] `TaxWithholdTable` tem `TaxWithholdType_BR` para substituir o `Contains()`?
- [ ] **Imposto Seletivo**: onde aparece? Não está em `TaxType_BR` nesta versão
- [ ] `CClassTrib` também existe no item (`InventTable`)?
- [ ] `CClassTribTable_BR` — campos necessários para resolver o código a partir do RecId
- [ ] CT-e (modelo 57) entra no escopo agora?
