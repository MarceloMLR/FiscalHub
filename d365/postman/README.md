# Postman — Consulta OData do D365 F&O

Collection pronta pra consultar **data entities** do Dynamics 365 Finance & Operations via OData, com a URL do ambiente em variável e **token do Entra ID obtido automaticamente**.

## Arquivos

- `D365-FnO-OData.postman_collection.json` — a collection (requests + auto-token).
- `FiscosysDev.postman_environment.json` — environment com as variáveis (URL, tenant, app id/secret, empresa).

## Como importar

1. Postman → **Import** → arraste os dois arquivos.
2. No canto superior direito, selecione o environment **"D365 F&O — FiscosysDev"**.

## O que preencher (uma vez)

No environment (ou nas variáveis da collection):

| Variável | O que é | Onde pegar |
|---|---|---|
| `environmentUrl` | URL do ambiente F&O (sem barra no fim) | ex.: `https://fiscosysdev.operations.dynamics.com` |
| `tenantId` | Directory (tenant) ID do Entra | Entra ID → Overview |
| `clientId` | Application (client) ID do app registration | Entra ID → App registrations → seu app |
| `clientSecret` | Secret do app registration | App registration → Certificates & secrets → New client secret |
| `company` | Empresa (dataAreaId) pra filtrar | ex.: `brmf` |
| `companySemCentroCusto` | Empresa sem dimensão de centro de custo nos Parâmetros do Brasil (teste do contábil) | ex.: `usmf` |
| `inventDataCorte` | Data de corte do saldo na data (teste do inventário), no formato `aaaa-mm-dd` | ex.: `2016-12-31` |
| `entityName` | Entity set pra query genérica (nome no **plural**) | ex.: `FSFiscalDocumentBRs` |
| `periodoInicio` | Primeiro dia fiscal da descoberta por período, no formato `aaaa-mm-dd` | ex.: `2026-08-07` |
| `periodoFim` | Último dia fiscal da descoberta por período, inclusive | ex.: `2026-08-07` |
| `estabelecimento` | Código do estabelecimento (`FiscalEstablishmentId`) da descoberta por período | ex.: `SP-01` |

## Pré-requisito no lado do F&O (importante)

Um token válido do Entra **não basta**: o app precisa estar **mapeado a um usuário do F&O**, senão a query em `/data` volta **401**.

1. **Entra ID (portal.azure.com):** crie um **App registration** (single tenant). Anote `tenantId`, `clientId` e crie um **client secret**. (Não precisa de API permission interativa pra client_credentials no F&O; o vínculo é feito no passo 2.)
2. **No F&O:** *System administration → Setup → Microsoft Entra ID applications* → **New**:
   - **Client Id** = o `clientId` do app.
   - **Name** = um nome qualquer.
   - **User ID** = um usuário do F&O (ex.: um usuário de integração/admin). O token vai agir como esse usuário.

## Como usar

- **Não precisa** pegar token manualmente: um *pre-request script* na collection busca o token (client_credentials, `resource = environmentUrl`) e o reaproveita até ~1 min antes de expirar.
- Rode qualquer request da pasta **OData**:
  - **$metadata** — confirma quais entidades estão publicadas (procure pelo nome da sua).
  - **Query entidade (genérico)** — troca `{{entityName}}` e consulta qualquer entidade.
  - **Query por empresa (dataAreaId)** — filtra por `{{company}}` (no FiscosysDev os dados fiscais de teste estão na **brmf**; a **DAT** está vazia).
  - **$count** e **Service document**.
- Rode a pasta **FiscalHub — entidades (14)** inteira pelo **Runner** para o smoke test completo (ver abaixo).
- Tem também **Auth → Get Token (manual)** se quiser inspecionar o token na mão.

## Pasta `FiscalHub — entidades (14)`

Uma requisição por entidade custom do pacote `FiscalHubIntegration`. Cada uma faz
`GET /data/{entitySet}?cross-company=true&$top={{top}}` e valida:

- **HTTP 200**
- resposta OData válida (`value` é array)
- loga no console quantas linhas vieram; avisa quando vem **0**

As 14: `FSFiscalDocumentBRs`, `FSFiscalDocumentLineBRs`, `FSTaxTransBRs`, `FSTaxWithholdBRs`,
`FSTaxTableBRs`, `FSMarkupTransBRs`, `FSFiscalDocModelBRs`, `FSItemBRs`, `FSUnitOfMeasureBRs`,
`FSAddressCityBRs`, `FSCountryRegionBRs`, `FSPostalAddressBRs`, `FSCustomerBRs`, `FSVendorBRs`.

Para rodar tudo de uma vez: clique na pasta → **Run folder** → *Run*. O Runner mostra o resultado
requisição a requisição.

## Pasta `FiscalHub — regressão`

Dois testes de `$count` que travam um defeito real encontrado em **24/09/2026**:

> A `FSFiscalDocumentBR` voltava `$count = 0` enquanto a `FSFiscalDocumentLineBR` voltava `150`.
> Causa: os quatro data sources `LogisticsLocation` aninhados sob os `LogisticsPostalAddress`
> estavam **sem a tag `<JoinMode>`**, o que no F&O equivale a **InnerJoin**. Um InnerJoin pendurado
> embaixo de um OuterJoin anula o OuterJoin — como nenhuma nota tem os quatro endereços
> preenchidos, toda linha do cabeçalho era eliminada.
> Correção: `<JoinMode>OuterJoin</JoinMode>` nos nós `Location`, `Location1`, `Location2` e `Location3`.

Se o teste do cabeçalho voltar a falhar, vá direto conferir o `JoinMode` dos data sources aninhados
da entidade antes de procurar em outro lugar.

## Pasta `FiscalHub — contábil (3)`

As três entidades do módulo contábil (`d365/04`, Parte II), em subpastas:

| Subpasta | Entidade | Filtro por empresa |
|---|---|---|
| `Lançamentos` | `FSGeneralJournalLineBRs` | `DataArea eq 'brmf'` |
| `Plano de contas` | `FSMainAccountBRs` | `DataArea eq 'brmf'` |
| `Centro de custo` | `FSCostCenterBRs` | `dataAreaId eq 'brmf'` |

Lançamento e plano de contas saem de tabelas compartilhadas: a empresa vem do `CompanyInfo`, no campo `DataArea`. Com
`PrimaryCompanyContext = DataArea`, o F&O também expõe `dataAreaId` nessas duas, com o mesmo valor, e filtrar por um ou
pelo outro dá o mesmo resultado. A collection usa `DataArea`, o campo declarado na entidade.

**Rode a pasta inteira pelo Runner, na ordem.** Os requests de contagem guardam variáveis que os de aceite usam.

O que cada parte testa:

- **Publicação:** as três aparecem no service document. Em toda a pasta, `404` falha com "entidade não publicada" e
  `401`/`403` falha com "sem acesso". Se a entidade não responde, os testes de conteúdo são pulados, para a falha não se
  espalhar.
- **Smoke:** cada entidade responde e expõe os campos da decisão. Campo `Private` some em silêncio, e é aqui que aparece.
- **Aceite estrutural (`d365/04`, seção 17):** prova a junção, valha o dado de teste o que valer.
  - a contagem da entidade é igual à da tabela raiz, por uma entidade padrão da Microsoft (`GeneralJournalAccountEntryBiEntities`, `MainAccounts` do plano da empresa, `FinancialDimensionValues` da dimensão declarada);
  - a chave é única;
  - cada lançamento soma zero;
  - as junções externas (conta e período) resolvem em todas as linhas;
  - a árvore de contas é fechada, e toda conta usada nos lançamentos existe no plano;
  - a empresa sem dimensão de centro de custo nos Parâmetros do Brasil (`companySemCentroCusto`, padrão `usmf`) devolve 0.

Os valores em si não entram nos testes: o `fiscosysdev` é base de demonstração.

## Pasta `FiscalHub — inventário (3)`

As três entidades do inventário (`d365/04`, Parte III), em subpastas, mais os dois campos novos da `FSItemBR`:

| Subpasta | Entidade | O que guarda para a próxima |
|---|---|---|
| `Armazéns` | `FSInventLocationBRs` | a lista de armazéns |
| `Saldo atual` | `FSInventOnHandBRs` | o saldo financeiro por site e item, e o físico por armazém e item |
| `Movimentos` | `FSInventTransBRs` | o histórico financeiro até `inventDataCorte` |
| `Item (FSItemBR)` | `FSItemBRs` | — |

Filtro por empresa: `dataAreaId eq 'brmf'` nas três. **Rode a pasta inteira pelo Runner, na ordem.**

O que cada parte testa:

- **Publicação, 404 e acesso:** igual ao contábil.
- **Smoke:** cada entidade responde e expõe os campos da decisão.
- **Aceite estrutural (`d365/04`, seção 24):**
  - a contagem da entidade é igual à da tabela raiz (`InventLocationBiEntities`, `InventoryOnHandForAI`, `InventTransBiEntities`, `InventTableBiEntities`);
  - a chave é única;
  - todo armazém tem estabelecimento fiscal, e todo armazém do saldo e dos movimentos está na `FSInventLocationBRs`;
  - a quantidade física do saldo atual bate com a `WarehousesOnHandV2` por armazém e item;
  - todo item tem `InventUnitId` e `ProductName`.
- **Saldo na data por dois caminhos:** o request usa o filtro que a carga do hub vai usar, só os movimentos depois de
  `inventDataCorte`. O saldo atual menos esses movimentos tem de dar o mesmo que a soma do histórico até a data, em cada site e
  item. Os dois caminhos leem tabelas diferentes (`InventSum` e `InventTrans`).

**A data vem no OData às 12:00Z.** `DateFinancial` de 31/12 chega como `...-12-31T12:00:00Z`: um filtro `gt ...T00:00:00Z`
traria o próprio dia. O request filtra com `ge` na data de corte e o script descarta o dia; o teste confere que nenhum movimento
ficou de fora.

## Pasta `FiscalHub — diretório e descoberta por período`

O que o hub lê do D365 para a integração manual e o agendamento (change `erp-company-directory-and-card-filters`):

| Request | Entidade | O que confere |
|---|---|---|
| `FiscalEstablishments (diretorio)` | `FiscalEstablishments`, a entidade **padrão da Microsoft** | os quatro campos (`dataAreaId`, `FiscalEstablishmentId`, `CNPJ`, `Name`) e que todo estabelecimento tem CNPJ e código |
| `Descoberta por periodo (FSFiscalDocumentBRs pelo dia fiscal)` | `FSFiscalDocumentBRs` | que toda nota tem o dia fiscal entre `periodoInicio` e `periodoFim`, é do `estabelecimento`, e vem na ordem do `FiscalDocumentRecId` |

- **O diretório não é entidade do pacote.** A role `FSFiscalHubIntegration` referencia o privilégio padrão
  `FiscalEstablishmentEntityView`. Um `403` no primeiro request é esse privilégio sem deploy, ou a role não atribuída ao
  app. O console diz isso.
- **A empresa do hub** é o `CNPJ` sem ponto, barra, hífen e espaço, com as letras. O console mostra cada uma.
- **O filtro de data** usa os limites `T00:00:00Z` e `T23:59:59Z`, que cobrem o `FiscalDocumentDate` às 12:00Z.
  Verificado em 2026-10-01: com `2026-08-07` e `SP-01`, o request traz `BRMF06-110000034` e `BRMF06-110000035`, as duas
  NFS-e daquele dia.

## Atalho pra teste rápido: token pela sua própria identidade

Se o `clientSecret` não estiver preenchido (é o caso no repositório) e você só quiser conferir se as
entidades respondem, dá pra usar sua sessão do Azure CLI em vez do app registration:

```powershell
az login   # se ainda não estiver logado
$env:FH_TOKEN = az account get-access-token --resource "https://fiscosysdev.operations.dynamics.com" --query accessToken -o tsv
Invoke-RestMethod -Uri "https://fiscosysdev.operations.dynamics.com/data/FSFiscalDocumentBRs?cross-company=true&`$top=1" `
  -Headers @{ Authorization = "Bearer $env:FH_TOKEN" }
```

Isso usa **sua** identidade (delegada), não a do app. Serve para validar que a entidade existe e
responde — **não** valida se a role `FiscalHub - integração (somente leitura)` foi atribuída ao
usuário de integração. Esse teste só a autenticação por `client_credentials` faz.

## Dicas

- `cross-company=true` traz de todas as empresas; sem isso, só a empresa default da sessão do usuário de integração.
- O token usa o endpoint **v1.0** (`/oauth2/token` com `resource`), que é o mais simples pro recurso do F&O. Se precisar do v2.0, use `scope = {environmentUrl}/.default` no `/oauth2/v2.0/token`.
- Segredo: o `clientSecret` está como tipo **secret** no environment — não commite valores reais. Deixe em branco no repositório.
