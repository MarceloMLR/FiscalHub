# FiscalHub — Pacote de integração D365 F&O

Este diretório guarda o **lado Dynamics 365 Finance & Operations** da integração: as **data entities**
que publicamos (contrato de nome fixo, ADR-0022), o metadado versionado do modelo, e o **deployable
package** que o cliente instala. Não faz parte do build .NET do middleware — é metadado do F&O, com
toolchain própria (Visual Studio + F&O dev tools, PPAC/Azure DevOps).

## Por que existe

O dado fiscal vem sempre da localização BR padrão (`FiscalDocument_BR`), mas ela normalmente não está
exposta no OData; cada implementador cria uma entidade pública com nome variável (`fiscaldocument_br2`…).
Em vez de customizar o adapter por cliente, **nós publicamos** entidades de **nome fixo** (prefixo `FS`)
como projeção sobre as tabelas padrão. O adapter sempre lê o mesmo nome.

## As 14 entidades

Todas públicas, somente leitura, sem Data Management, no modelo `FiscalHubIntegration`:

| Grupo | Entidades |
|---|---|
| Documento | `FSFiscalDocumentBR`, `FSFiscalDocumentLineBR` |
| Impostos | `FSTaxTransBR`, `FSTaxWithholdBR`, `FSTaxTableBR` |
| Encargos | `FSMarkupTransBR` |
| Cadastros | `FSFiscalDocModelBR`, `FSItemBR`, `FSUnitOfMeasureBR`, `FSAddressCityBR`, `FSCountryRegionBR`, `FSPostalAddressBR` |
| Parceiros | `FSCustomerBR`, `FSVendorBR` |

O entity set no OData é o nome no plural: `/data/FSFiscalDocumentBRs`.

Segurança: privilégio de leitura por entidade + a role `FSFiscalHubIntegration`
("FiscalHub - integração (somente leitura)"), que precisa ser atribuída ao usuário da app
registration no F&O.

## Guias

| Doc | Assunto |
|---|---|
| [`00-setup-e-conexao-do-visual-studio.md`](00-setup-e-conexao-do-visual-studio.md) | Preparar o VS e conectar ao ambiente UDE |
| [`01-criar-e-publicar-data-entity.md`](01-criar-e-publicar-data-entity.md) | Criar uma data entity e publicá-la no OData |
| [`02-business-event-status-changed.md`](02-business-event-status-changed.md) | Business event no status (papel revisto pelo ADR-0023 — ver aviso no topo do doc) |
| [`03-deploy-e-promocao.md`](03-deploy-e-promocao.md) | Deployable package e promoção para o cliente |
| [`04-mapeamento-de-entidades.md`](04-mapeamento-de-entidades.md) | **O que** ler de cada entidade: campos, relações, armadilhas do legado |
| [`05-achados-de-metadata-e-ciclo-de-deploy.md`](05-achados-de-metadata-e-ciclo-de-deploy.md) | **O que dá errado** ao expor: campos escondidos, JoinMode, ciclo build/deploy/sync |
| [`glossario-x++-fno.md`](glossario-x++-fno.md) | Termos de X++ e F&O |

`model/` guarda a cópia versionada do metadado (AOT) das entidades, privilégios e role.
`postman/` tem a collection de teste do OData, com smoke test das 14 e testes de regressão.

## Status

- [x] **Fase 1 — Entidades.** 14 publicadas, respondendo HTTP 200 no OData, cadeia completa do
      conector validada ponta a ponta com nota de mercadoria (NF-e 55) e de serviço (modelo SE).
- [ ] **Fase 2 — Gatilho.** Service Bus + business event. Rebaixado pelo ADR-0023 a otimizador de
      latência; a garantia é o polling.
- [ ] **Fase 3 — Pacote.** Deployable package + import validado no nosso ambiente primeiro.

Pendências de mapeamento: seção 11 do `04`. Pendência operacional: atribuir a role ao usuário de
integração.
