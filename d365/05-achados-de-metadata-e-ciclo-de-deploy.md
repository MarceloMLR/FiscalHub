# Fase 5 — Achados de metadata das entidades e ciclo de deploy

Registro do que quebrou, por que quebrou e como se detecta, durante a publicação e validação das
14 entidades do pacote `FiscalHubIntegration` no ambiente `fiscosysdev`.

Tudo aqui foi verificado contra o ambiente real via OData, não inferido do legado nem da
documentação. Quando a conclusão mudou no meio do caminho, o doc registra a versão final e diz o
que estava errado antes — a primeira leitura errada costuma ser mais instrutiva que o acerto.

---

## 1. `AccessModifier = Private` esconde o campo do OData, em silêncio

**O mais importante deste documento.**

O wizard de data entity marca alguns campos como `Private` ao gerar a entidade. Campo privado:

- **não aparece** na resposta do OData, nem como propriedade vazia;
- **quebra `$select` e `$filter`** com HTTP 400 e corpo de erro vazio;
- **não gera erro de compilação** — o build passa limpo.

Ou seja: a entidade parece publicada e funcional, e alguns campos simplesmente não existem para
quem consome. Descobrimos porque um join não fechava, não porque alguma ferramenta avisou.

Na `FSFiscalDocumentBR` eram **9 campos**, na `FSFiscalDocumentLineBR` eram **3**:

| Entidade | Campo | Por que importa |
|---|---|---|
| `FSFiscalDocumentBR` | `RefRecId` | liga o cabeçalho ao documento de origem (`SalesTable`/`PurchTable`) |
| | `ThirdPartyPostalAddress` | endereço do terceiro na nota |
| | `DeliveryLogisticsPostalAddress` | endereço de entrega |
| | `FiscalEstablishmentPostalAddress` | endereço do estabelecimento emissor |
| | `SalesCarrierLogisticsPostalAddress` | endereço do transportador |
| | `CityWhereServicePerformed` | **município da prestação — obrigatório na NFS-e** |
| | `ComplementedFiscalDocument` | vínculo da nota complementar |
| | `ImportDeclaration` | DI, para importação |
| | `FiscalDocumentFormat` | formato do documento |
| `FSFiscalDocumentLineBR` | `RefRecId` | liga a linha fiscal à linha de origem (`SalesLine`/`PurchLine`) |
| | `FiscalDocumentRecId` (← `FiscalDocument`) | FK da linha para o cabeçalho |
| | `FinancialLedgerDimension` | dimensão financeira |

O caso mais grave é o `CityWhereServicePerformed`: um conector construído sobre a versão anterior
emitiria NFS-e sem município de prestação, e isso só apareceria na rejeição da prefeitura.

### A exceção: campos de dimensão

Tornar **todos** públicos foi um exagero. O Best Practice reprova:

```
[DataEntityDimensionFieldIncorrectAccessModifier]
The 'FinancialLedgerDimension' entity field must have the Access Modifier property
set to Private or Internal.
```

O RecId cru de uma `LedgerDimension` **tem** que ficar Private ou Internal. Quem é público é o
`FinancialLedgerDimensionDisplayValue` (mapeado para `DisplayValue`), que já existia na entidade.

**Regra:** libere os campos privados, exceto os de dimensão financeira. O BP pega o resto.

### Como auditar

```powershell
$m = "<model store>\FiscalHubIntegration\FiscalHubIntegration\AxDataEntityView"
Get-ChildItem "$m\*.xml" | ForEach-Object {
  $x = [xml](Get-Content $_.FullName)
  $priv = @($x.AxDataEntityView.Fields.AxDataEntityViewField |
            Where-Object { $_.AccessModifier -eq 'Private' })
  if ($priv.Count) { "{0}: {1}" -f $x.AxDataEntityView.Name, (($priv | % { $_.Name }) -join ', ') }
}
```

Vale rodar **toda vez** que uma entidade for criada ou regenerada pelo wizard.

---

## 2. Data source aninhado sem `<JoinMode>` é InnerJoin — e anula o OuterJoin do pai

A `FSFiscalDocumentBR` respondia HTTP 200 com **zero registros**, enquanto a
`FSFiscalDocumentLineBR` devolvia 150 linhas com `Voucher` preenchido — prova de que o cabeçalho
existia na tabela.

Causa: os quatro data sources de endereço estavam corretamente em `OuterJoin`, mas cada um tinha um
`LogisticsLocation` aninhado **sem a tag `<JoinMode>`**. Ausente equivale a `InnerJoin`, e um
InnerJoin pendurado embaixo de um OuterJoin anula o OuterJoin.

```xml
<AxQuerySimpleEmbeddedDataSource>
  <Name>DeliveryLogisticsPostalAddress</Name>
  <Table>LogisticsPostalAddress</Table>
  <DataSources>
    <AxQuerySimpleEmbeddedDataSource>
      <Name>Location</Name>
      <Table>LogisticsLocation</Table>
      ...
      <JoinMode>OuterJoin</JoinMode>   <!-- estava ausente = InnerJoin -->
      <Relations>...</Relations>
    </AxQuerySimpleEmbeddedDataSource>
  </DataSources>
  <JoinMode>OuterJoin</JoinMode>
  ...
</AxQuerySimpleEmbeddedDataSource>
```

Como **nenhuma** nota tem os quatro endereços preenchidos, toda linha era eliminada. Depois da
correção nos quatro nós (`Location`, `Location1`, `Location2`, `Location3`), o `$count` foi de
**0 para 83**.

Confirmado depois: numa nota real, `ThirdPartyPostalAddress` e `FiscalEstablishmentPostalAddress`
vêm preenchidos; `Delivery` e `SalesCarrier` vêm zerados. Dois de quatro — exatamente o cenário que
o InnerJoin matava.

> **Nota de método.** Num primeiro momento eu concluí que os endereços estavam vazios na base.
> Estava errado: eles vinham vazios porque eram **privados** (achado 1). Os dois achados se
> mascaravam. A lição é não tirar conclusão sobre o *dado* enquanto a *entidade* não estiver
> provadamente correta.

A tag de `JoinMode` fica entre `<Ranges />` e `<Relations>` — a posição importa na serialização.

---

## 3. `RefRecId` do cabeçalho não aceita `$select`

Depois de tornado público, o `RefRecId` da `FSFiscalDocumentBR`:

- **vem no payload completo** (o cabeçalho passou de 74 para 84 propriedades) e com valor;
- **falha em `$select` e `$filter`** com HTTP 400.

O mesmo campo, com o mesmo nome, na `FSFiscalDocumentLineBR` funciona normalmente nos dois. É uma
particularidade da entidade de cabeçalho.

**Impacto hoje: nenhum.** A descoberta é por `SysModifiedDateTime` e a montagem lê o registro
inteiro, então o valor chega. Só não dá para projetar nem filtrar por ele naquela entidade.

**Se incomodar:** criar alias — `Name` = `SourceRefRecId`, `DataField` = `RefRecId`. É o mesmo
truque do `FiscalDocumentRecId` (ver `04-mapeamento-de-entidades.md`, seção 2.2). Classificado como
melhoria, não correção.

---

## 4. Data Management desligado nas 14

A `FSFiscalDocumentBR` era a única com `DataManagementEnabled = Yes` e staging table, herdado do
wizard. Ao adicionar o `SysModifiedDateTime`, o BP reprovou:

```
[DataEntityPublicFieldEdtMatchOnStagingTableCheck]
The data entity 'FSFiscalDocumentBR' has public field 'SysModifiedDateTime' with EDT '',
but corresponding field in the staging table 'FSFiscalDocumentBRStaging' has EDT 'ModifiedDateTime'.
```

Não é contornável mexendo na staging: o campo da entidade aponta para o `ModifiedDateTime` da
tabela, que é **campo de sistema** e não resolve EDT nenhum.

As entidades da própria Microsoft que expõem esse campo resolvem assim:

| Entidade padrão | `DataManagementEnabled` | `IsReadOnly` |
|---|---|---|
| `InventTransBiEntity` | No | Yes |
| `CustTableBiEntity` | No | Yes |
| `SalesTableBiEntity` | No | Yes |

Mesmo formato do nosso: projeção plana, somente leitura, para consumo externo. Nenhuma tem staging,
então a regra nunca dispara.

**Decisão:** `DataManagementEnabled = No` nas 14, staging removida do projeto. O conector é OData
somente leitura e nunca usa DMF. Ganho colateral: adicionar campo deixou de exigir *Update staging
table* e de esbarrar em EDT incompatível.

Custo: essas entidades não aparecem no *Data management workspace* para import/export.

---

## 5. `SysModifiedDateTime` — o nome importa

O ADR-0023 prevê janela de data como fallback da descoberta quando o change tracking falha. O
cabeçalho não expunha `ModifiedDateTime`.

Duas verificações antes de adicionar:

1. **A tabela rastreia?** `FiscalDocument_BR` e `FiscalDocumentLine_BR` estão com
   `ModifiedDateTime = Yes`, `CreatedDateTime = Yes`, `ModifiedBy = Yes`, `CreatedBy = Yes`.
   Atenção: a tabela vive no pacote **`FiscalBooks`**, não no `ApplicationSuite`.
2. **Que nome usar?** `ModifiedDateTime` puro é campo de sistema e correria o risco do `RecId`. As
   entidades padrão da Microsoft usam **`SysModifiedDateTime`** (26 ocorrências numa amostra de 60
   entidades do pacote `BusinessIntelligence`). Seguimos a convenção.

```xml
<AxDataEntityViewField xmlns=""
    i:type="AxDataEntityViewMappedField">
    <Name>SysModifiedDateTime</Name>
    <DataField>ModifiedDateTime</DataField>
    <DataSource>FiscalDocument_BR</DataSource>
</AxDataEntityViewField>
```

Sem `AccessModifier`, para nascer público. Validado no ambiente: retorna
`2017-01-21T21:23:19Z` na nota de mercadoria e `2016-11-28T20:58:29Z` na de serviço.

Ainda **não** foi adicionado à `FSFiscalDocumentLineBR`, que também rastreia o campo. Só faz falta
se a linha puder mudar sem o cabeçalho ser tocado.

---

## 6. Ciclo de publicação no Unified Developer Environment

No UDE tudo é local: compilador X++, ferramentas e model store ficam na máquina do dev. O AOS está
na nuvem. Por isso são **três** etapas, não duas:

```
Build  →  Deploy  →  Sync
(local)   (leva pro    (aplica DDL no
          ambiente)     banco do ambiente)
```

Duas armadilhas documentadas pela Microsoft:

1. **Sync sem build dá sucesso falso.** *"the Visual Studio database synchronization tool displays a
   message that synchronization completed successfully, when in fact, the synchronization wasn't
   successful."*
2. **`Deploy model for project…` (botão direito no projeto) não sincroniza.** O código sobe e a view
   no SQL continua a antiga — o sintoma é "subi e não mudou nada".

Como data entity vira **view no SQL**, é o sync que a recria. Foi por isso que a correção do
JoinMode só passou a valer depois do sync, e não no build.

### Configuração recomendada do projeto

| Propriedade | Valor |
|---|---|
| `Deploy changes to online environment` | `true` |
| `Synchronize Database on build` | `true` |

Com as duas ligadas, um rebuild do projeto faz as três etapas, e o deploy é **incremental** — só o
que mudou desde o último deploy bem-sucedido.

**Full deploy + full sync** ficam para: módulos binários de terceiros/ISV, aplicação de licença (que
exige Full DB Sync), e primeira publicação depois de mexer nas referências de pacote do modelo.

---

## 7. Validação ponta a ponta

Simulação do fluxo do conector para uma nota específica, contra dado real.

### Mercadoria — NF-e 55, `BRMF21-10000027`

```
Outgoing · Approved · NF 000002/02 · Cliente BRMF-000002 · total 15.927,50
RefRecId 35637189623 · RefTableId 2579 (SalesTable)
SysModifiedDateTime 2017-01-21T21:23:19Z

ln=1 BRMF010 NCM 8518.22.00 CFOP 5.101 16 pcs x 350 = 5.600  refRec 35637359463
ln=2 BRMF020 NCM 8518.22.00 CFOP 5.101 15 pcs x 550 = 8.250  refRec 35637359464
```

Impostos atribuídos **por linha**, via `SourceRecId` + `SourceTableId`:

| Linha | ICMS | IPI | PIS | COFINS |
|---|---|---|---|---|
| 1 | 1.008,00 | 840,00 | 92,40 | 425,60 |
| 2 | 1.485,00 | 1.237,50 | 136,13 | 627,00 |

4 + 4 = 8, o mesmo total que o filtro por `Voucher` devolve em bloco — agora com atribuição correta.
Os valores fecham: 13.850 de mercadoria + 2.077,50 de IPI = **15.927,50**, o total do cabeçalho
(IPI está `IncludedTax = No` na `FSTaxTableBR`; ICMS, PIS e COFINS estão `Yes`).

### Serviço — modelo SE, `BRMF21-10000019`

```
Outgoing · Approved · NF 000003/03 · Cliente BRMF-000002 · total 38.500 (100% serviços)
SysModifiedDateTime 2016-11-28T20:58:29Z
CityWhereServicePerformed 22565694955 → São Paulo/SP, IBGE 3550308
CCM do estabelecimento 2541 · emissor OwnEstablishment

ln=1 BRMF040 ItemType=Service código 14.06 110 hr x 350 = 38.500 (sem CFOP, correto)
ISS 1.925,00 · PIS 635,25 · COFINS 2.926,00 · IRRF 577,50
```

### Cadeia completa exercitada

| # | Passo | Como |
|---|---|---|
| 1 | Descoberta | `FSFiscalDocumentBRs` ordenado por data / `SysModifiedDateTime` |
| 2 | Cabeçalho | `$filter=Voucher eq '...'` |
| 3 | Linhas | `$filter=FiscalDocumentRecId eq <recid>` |
| 4 | Impostos por linha | `$filter=SourceRecId eq <linha.RefRecId> and SourceTableId eq <linha.RefTableId>` |
| 5 | Retenções | `FSTaxWithholdBRs` por `Voucher` |
| 6 | Encargos | `$filter=TransRecId eq <cab.RefRecId> and TransTableId eq <cab.RefTableId>` |
| 7 | Parceiro | `FSCustomerBRs` / `FSVendorBRs` por `AccountNum` |
| 8 | Cadastros | modelo, item, unidade |
| 9–11 | Endereço → município → país | `FSPostalAddressBRs` → `FSAddressCityBRs` → `FSCountryRegionBRs` |

Os encargos (passo 6) voltaram 0 registros nessa nota — os três campos de markup do cabeçalho estão
zerados, então o caminho está validado mas falta uma nota **com** encargo para exercitar de verdade.

---

## 8. Nota cancelada não gera linha nova

Pendência que estava aberta no `04-mapeamento-de-entidades.md` e que decide o desenho do store.

`Status` faz parte da chave única da entidade, o que levantava a dúvida: nota aprovada e depois
cancelada vira **duas** linhas?

Levantamento sobre as 83 notas do ambiente:

```
cabeçalhos: 83
status: Approved=81, Cancelled=2
vouchers com mais de uma linha de cabeçalho: 0
```

**Não gera.** É a mesma linha com `Status` alterado. Confirma o desenho: identidade do documento
estável na `NaturalKey`, status como atributo mutável, e as tentativas/versões numa relação 1:N à
parte.

Ressalva honesta: o ambiente só tem 2 notas canceladas e não dá para provar que alguma delas passou
por `Approved` antes. A ausência de voucher duplicado em 83 registros é evidência forte, não prova.

**Confirmado pelo domínio (2026-09-25).** O voucher do documento fiscal é **único e imutável**. Com
isso, a `NaturalKey = empresa|voucher` passa a ter duas evidências independentes: o levantamento acima e
a regra de domínio. A transição `Approved → Cancelled` mantém a mesma chave: é o mesmo documento com
uma nova tentativa (ADR-0024 §6).

O que fica em aberto é por cliente, não por nota. A sequência numérica do voucher é configurada em
cada F&O. Se a de algum cliente reiniciar por exercício fiscal, o voucher repetiria entre anos. Isso é
conferido no onboarding, e o desempate (`empresa|RecId`) já vem no `$select` do keyset.

---

## 9. Checklist para criar ou alterar uma entidade

1. Auditar `AccessModifier = Private` (script da seção 1) e liberar tudo, **menos** dimensão financeira.
2. Conferir `JoinMode` em **todo** data source aninhado — ausente significa InnerJoin.
3. `IsPublic = Yes`, `IsReadOnly = Yes`, `DataManagementEnabled = No`.
4. Campo de sistema recebe alias com prefixo `Sys` (`SysModifiedDateTime`, `SysDataAreaId`).
5. RecId exposto recebe alias com nome próprio (`FiscalDocumentRecId`), nunca `RecId`.
6. Build → Deploy → Sync, nessa ordem, com as duas propriedades do projeto ligadas.
7. Depois do sync, validar **no ambiente**: `$count > 0`, `$select` em cada campo novo, e um
   `$top=1` sem `$select` conferindo se o campo aparece no payload.

O passo 7 é o que pega o que o build não pega. Nenhum dos achados deste documento gerou erro de
compilação.

---

## 10. Paginação da descoberta: o nextLink é offset

Levantamento feito para o worker de descoberta por polling (ADR-0024), contra a `FSFiscalDocumentBRs`
no `fiscosysdev`, com 83 cabeçalhos.

### O que funciona

| Consulta | Resultado |
|---|---|
| `$filter=SysModifiedDateTime gt <literal UTC>` | ok |
| `$filter=dataAreaId eq 'brmf'` | ok |
| `$filter=FiscalDocumentRecId gt <int64>` | ok |
| `$filter=(SysModifiedDateTime gt T) or (SysModifiedDateTime eq T and FiscalDocumentRecId gt R)` | ok: é o keyset composto |
| `$orderby=SysModifiedDateTime` | ok |
| `$orderby=SysModifiedDateTime,FiscalDocumentRecId` | ok |
| `$orderby=SysModifiedDateTime,dataAreaId,Voucher` | ok |
| `$select` de `dataAreaId,Voucher,Model,Direction,Status,FiscalDocumentNumber,FiscalDocumentSeries,SysModifiedDateTime` | ok |
| header `Date` na resposta | presente em toda resposta |
| `Prefer: odata.maxpagesize=20` | **honrado**: 20 registros mais `@odata.nextLink`; varredura completa em 5 páginas de 20 |

### O que não funciona

| Consulta | Resultado |
|---|---|
| `$filter=Voucher gt 'X'` | recusado: string não aceita comparação relacional |
| header `Preference-Applied` | volta **vazio** mesmo quando o `maxpagesize` é honrado. Não serve para detectar se a preferência foi aceita |

### O formato do nextLink

```
…&$orderby=SysModifiedDateTime&$select=…&$skip=20&$top=20
```

É paginação por **offset**. Não existe `$skiptoken`, e a leitura não é um snapshot.

**Por que importa.** Offset sobre tabela viva, com ordenação não total, pode pular linha:

1. Uma nota já varrida sofre update.
2. Ela ganha timestamp novo e migra para o fim da ordenação.
3. Tudo o que vinha depois dela desloca uma casa, e a linha que estava na fronteira da página fica
   para trás sem ser lida.

Update de `Status` é justamente a carga principal da descoberta. Em regime, a sobreposição da janela
recupera a linha. Numa marca rebobinada (backfill), a perda é permanente.

**Decisão:** o worker não segue o nextLink. Ele pagina por **keyset composto** em
(`SysModifiedDateTime`, `FiscalDocumentRecId`), com `$top` e a âncora na última linha lida. A âncora é
valor, não posição, e nenhum insert ou update desloca o que ainda não foi lido. Detalhes e alternativas
estão no ADR-0024.

### Keyset exercitado cross-company

Leitura keyset completa, sem filtro de empresa: `cross-company=true`, `$top=20`,
`$orderby=SysModifiedDateTime,FiscalDocumentRecId`, `since = 2015-01-01T00:00:00Z`.

```
empresas no ambiente: brmf (83)   ← só existe uma
páginas: 20 · 20 · 20 · 20 · 3    (150–310 ms cada; nenhuma com nextLink, porque $top ≤ página)
linhas: 83 · FiscalDocumentRecId distintos: 83   ← nenhum repetido, nenhum pulado
Model: 01 = 69 · SE = 9 · 55 = 5
```

**Distribuição por modelo e ano: leia antes de tirar conclusão.**

| Modelo | Notas | Anos |
|---|---|---|
| `01` | 69 | 2015, 2016, 2017 |
| `SE` | 9 | 2015, 2016, 2026 |
| `55` | 5 | 2016 |
| **No mapa padrão (`55`/`57`/`SE`)** | **14 de 83** | |

- **O que o número parece dizer.** Que o modelo `01` é a maioria.
- **O que ele diz de fato.** O modelo `01` é a Nota Fiscal modelo 1/1A, um formulário em papel
  substituído pela NF-e (modelo 55). As 69 notas `01` do ambiente são **inteiramente dado de
  demonstração antigo** (2015–2017). Em cliente real, a incidência é próxima de zero. As únicas notas
  recentes do ambiente são as `SE` de 2026.
- **Efeito hoje.** O mapa padrão do adapter deixa as 69 como "modelo fora do mapa": aviso em log, fora
  da fila.
- **Recomendação (a decisão é do roteamento, ADR-0024).** Manter o `01` fora do mapa. O roteamento
  grava "ignorado: modelo fora do escopo" como desfecho explícito. Se um cliente tiver modelo `01` de
  verdade, basta uma linha no `modelTypes` das settings do tenant, sem código de domínio.

### Ainda a verificar

- [x] `FiscalDocumentRecId` em `$orderby` e no `$filter` keyset **cross-company**, sem filtro de empresa.
      Funciona (bloco acima). O ambiente só tem a brmf; repetir quando houver ambiente com mais de uma
      empresa.
- [ ] **Volume real.** O ambiente tem 83 cabeçalhos. Falta medir a latência de uma página keyset
      (`$top=500`, `$orderby=SysModifiedDateTime,FiscalDocumentRecId`, filtro com `or`) numa base com
      milhares ou milhões de documentos. Falta também confirmar se algum índice da `FiscalDocument_BR`
      cobre `ModifiedDateTime`. Sem índice, cada página pode virar varredura mais ordenação. Pendência
      antes do primeiro cliente.

---

## Referências

- [Build operations — Synchronize the database at each build](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/build-operations#synchronize-the-database-at-each-build)
- [Workflow to write, deploy, debug — Deploy code and synchronize the database](https://learn.microsoft.com/power-platform/developer/unified-experience/finance-operations-innerloop#deploy-code-and-synchronize-the-database)
- [Tutorial: Write, deploy, and debug X++ code](https://learn.microsoft.com/power-platform/developer/unified-experience/finance-operations-debug#deploy-the-class)
- `04-mapeamento-de-entidades.md` — campos e relacionamentos de cada entidade
- `docs/adr/0023-descoberta-por-polling-com-change-tracking-no-d365.md` — por que a descoberta é por polling
- `docs/adr/0024-feed-de-mudancas-por-janela-de-data-no-d365.md` — janela por data, keyset e lease do worker de descoberta
