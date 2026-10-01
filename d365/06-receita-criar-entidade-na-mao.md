# Fase 6 - Receita: criar ou alterar uma entidade editando o XML na mao

Este documento existe para quem chega sem contexto nenhum e precisa criar uma entidade nova
no modelo `FiscalHubIntegration`. Ele cobre o caminho que a gente usa de verdade: **editar o
arquivo `.xml` diretamente, com o Visual Studio fechado**.

O caminho do assistente do Visual Studio esta na fase 1 (`01-criar-e-publicar-data-entity.md`).
Use o assistente quando quiser criar do zero com a interface aberta; use esta receita quando a
entidade ja existe, quando o VS esta fechado, ou quando o agente esta escrevendo o arquivo.

---

## 1. Onde o arquivo mora - sao DOIS lugares

Esta e a primeira coisa que ninguem adivinha. O mesmo arquivo precisa ficar identico em:

```
1. no repositorio (versionado):
   d365\model\AxDataEntityView\<Nome>.xml

2. no metadado do UDE (o que o build le):
   C:\CustomXppMetadata*\FiscalHubIntegration\FiscalHubIntegration\AxDataEntityView\<Nome>.xml
```

Existe um terceiro caminho, o "espelho", e nele voce **nao** grava:

```
C:\CustomXppMetadata*\FiscalHubIntegration\XppMetadata\FiscalHubIntegration\AxDataEntityView\<Nome>.xml
```

Quem escreve ali e o build: sao stubs gerados a partir do item 2. As tres entidades contabeis foram gravadas so nos
dois primeiros lugares e ganharam o stub no primeiro build (2026-10-01).

O sufixo da pasta `CustomXppMetadata*` muda por maquina. Descubra com:

```powershell
Get-ChildItem C:\ -Directory -Filter "CustomXppMetadata*"
```

Gravar so no repositorio **nao** afeta o build. Gravar so no UDE faz a mudanca sumir no proximo
pull. Grave nos dois.

## 2. O `.rnrproj` fica FORA do repositorio

O projeto do Visual Studio nao e versionado e vive em outro lugar:

```
C:\Users\<voce>\Desktop\Projects 2026\Dynamics365\FiscalHubIntegration\FiscalHubIntegration.rnrproj
```

Repare no **2026** - o repositorio esta em `Projects 2027`. Sao pastas diferentes.

**Entidade nova precisa de uma entrada `<Content>` nesse arquivo**, ou ela nao entra no build e
voce fica procurando por que o deploy "nao fez nada". Entidade que ja existe nao precisa de nada:
alterar o XML basta.

```xml
<Content Include="AxDataEntityView\FSNomeDaEntidade">
  <SubType>Content</SubType>
  <Name>FSNomeDaEntidade</Name>
  <Link>Fiscal\DataEntities\<Grupo>\FSNomeDaEntidade</Link>
</Content>
```

## 3. Anatomia do XML

Duas secoes importam: `<Fields>`, que diz o que o OData expoe, e `<ViewMetadata>`, que diz de
onde cada campo vem.

### Um campo mapeado

```xml
<AxDataEntityViewField xmlns=""
    i:type="AxDataEntityViewMappedField">
    <Name>NomeNoOData</Name>
    <CountryRegionCodes>BR</CountryRegionCodes>
    <DataField>NomeNaTabela</DataField>
    <DataSource>NomeDaFonteDeDados</DataSource>
</AxDataEntityViewField>
```

### Uma fonte de dados aninhada

```xml
<AxQuerySimpleEmbeddedDataSource>
    <Name>TaxTrans_BR</Name>
    <DynamicFields>Yes</DynamicFields>
    <IsReadOnly>Yes</IsReadOnly>
    <Table>TaxTrans_BR</Table>
    <DataSources />
    <DerivedDataSources />
    <Fields />
    <Ranges />
    <JoinMode>OuterJoin</JoinMode>
    <Relations>
        <AxQuerySimpleDataSourceRelation>
            <Name>TaxTrans_BR</Name>
            <JoinRelationName>TaxTrans</JoinRelationName>
        </AxQuerySimpleDataSourceRelation>
    </Relations>
</AxQuerySimpleEmbeddedDataSource>
```

Fontes aninham dentro de `<DataSources>` da fonte pai. Quando a pai nao tem filha, o elemento
aparece fechado: `<DataSources />`. Para aninhar, troque por `<DataSources>...</DataSources>`.

## 4. As quatro regras que custaram ciclo de build

**`JoinRelationName` e o nome da TABELA RELACIONADA, nao do campo.** Para ligar a
`FiscalDocumentLine_BR`, o valor e `FiscalDocumentLine_BR` - mesmo que o campo na tabela se chame
`FiscalDocumentLine`. Errar aqui da `Failed to locate table relation` no build.

**`<JoinMode>` ausente significa InnerJoin, e anula o OuterJoin do pai.** Toda fonte aninhada
precisa do elemento explicito, inclusive as de segundo nivel.

**`AccessModifier = Private` esconde o campo do OData em silencio.** Sem erro de build, sem aviso:
o campo simplesmente nao aparece. Veja a secao 1 do documento 05 para auditar.

**Campo de sistema e RecId precisam de alias.** `SysModifiedDateTime`, `SysDataAreaId`, e RecId
sempre com nome proprio (`FiscalDocumentRecId`), nunca `RecId` puro.

## 5. A regra de ouro: copie uma fonte que ja funciona

Antes de escrever uma fonte de dados nova, procure nas 16 entidades existentes uma que ja ligue
as mesmas tabelas, e **copie o bloco inteiro**. O `JoinRelationName` correto e a informacao mais
dificil de adivinhar e a mais facil de copiar.

Exemplo real: ao ligar `TaxTrans_BR` sob `TaxTrans` na `FSFiscalDocumentTaxTransBR`, o bloco veio
copiado da `FSTaxTransBR`, que ja fazia essa ligacao em producao. Funcionou de primeira.

Quando nao houver exemplo, a convencao observada e: **o nome da relacao e o nome da tabela
relacionada**. Funcionou nas cinco vezes em que foi testada, inclusive com a `CClassTribTable_BR`.

## 6. Propriedades da entidade

```xml
<IsPublic>Yes</IsPublic>              <!-- expoe no OData -->
<IsReadOnly>Yes</IsReadOnly>          <!-- so leitura -->
<DataManagementEnabled>No</DataManagementEnabled>
<PrimaryCompanyContext>DataAreaId</PrimaryCompanyContext>
<PublicCollectionName>FSNomeDaEntidades</PublicCollectionName>  <!-- plural -->
```

Entidade nova tambem precisa de um privilegio de leitura em `AxSecurityPrivilege\` e da inclusao
dele na role `FSFiscalHubIntegration` (`AxSecurityRole\FSFiscalHubIntegration.xml`). Sem isso,
o OData responde 401 para o usuario da integracao.

## 7. Build, deploy e sync

Nessa ordem, no Visual Studio, com o host do FiscalHub **parado** se ele estiver rodando (ele
trava a pasta `bin`).

Alterar so o XML de uma entidade existente costuma pedir apenas build e sync. Entidade nova pede
o ciclo completo.

## 8. Conferir no ambiente - o passo que pega o que o build nao pega

Nenhum dos achados dos documentos 04 e 05 gerou erro de compilacao. A conferencia e no OData.

```powershell
$tok = az account get-access-token --resource "https://fiscosysdev.operations.dynamics.com" --query accessToken -o tsv

# os campos que vieram
$u = "https://fiscosysdev.operations.dynamics.com/data/FSNomeDaEntidades?`$top=1&cross-company=true"
$r = Invoke-RestMethod -Uri $u -Headers @{Authorization="Bearer $tok"}
$r.value[0].PSObject.Properties.Name | Where-Object { $_ -notmatch '^@' } | Sort-Object
```

Confira tres coisas:

1. **os campos novos aparecem** no payload (campo `Private` some em silencio);
2. **a contagem nao mudou** depois de acrescentar uma fonte de dados - junção nova pode duplicar
   ou filtrar linha:

```powershell
$u = "https://fiscosysdev.operations.dynamics.com/data/FSNomeDaEntidades?`$count=true&`$top=1&`$filter=dataAreaId eq 'brmf'&cross-company=true"
(Invoke-RestMethod -Uri $u -Headers @{Authorization="Bearer $tok"}).'@odata.count'
```

3. **um valor de verdade** em cada campo novo, e nao so a presenca da chave.

Exemplo real: ao acrescentar duas fontes aninhadas na `FSFiscalDocumentTaxTransBR`, a contagem
seguiu em 547 linhas para a `brmf` - mesma de antes. Se tivesse mudado, a junção estaria errada
mesmo com o build verde.

## 9. Enum no `$filter` precisa do nome qualificado

```
$filter=FiscalTaxType eq Microsoft.Dynamics.DataEntities.TaxType_BR'IPI'
```

O nome curto nao funciona.

## 10. Ambiente fora do ar

O F&O responde **503** durante sync, manutencao ou quando o ambiente esta parado. Antes de
investigar o conector, teste o ambiente direto:

```powershell
Invoke-WebRequest -Uri "https://fiscosysdev.operations.dynamics.com/data/FSFiscalDocumentBRs?`$top=1&cross-company=true" -Headers @{Authorization="Bearer $tok"} -UseBasicParsing
```

---

## Referencias

- `01-criar-e-publicar-data-entity.md` - o caminho pelo assistente do Visual Studio
- `04-mapeamento-de-entidades.md` - o que cada entidade expoe e por que
- `05-achados-de-metadata-e-ciclo-de-deploy.md` - os achados em detalhe, com os scripts de auditoria
- `README.md` - as 16 entidades e o contrato de nome fixo