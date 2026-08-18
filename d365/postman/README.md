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
| `entityName` | Entidade pra query genérica | ex.: `FS_FiscalDocumentBR` |

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
  - **$count**, **Service document**, e exemplos **FS_FiscalDocumentBR** / **FS_FiscalDocumentLine_Br**.
- Tem também **Auth → Get Token (manual)** se quiser inspecionar o token na mão.

## Dicas

- `cross-company=true` traz de todas as empresas; sem isso, só a empresa default da sessão do usuário de integração.
- O token usa o endpoint **v1.0** (`/oauth2/token` com `resource`), que é o mais simples pro recurso do F&O. Se precisar do v2.0, use `scope = {environmentUrl}/.default` no `/oauth2/v2.0/token`.
- Segredo: o `clientSecret` está como tipo **secret** no environment — não commite valores reais. Deixe em branco no repositório.
