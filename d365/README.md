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

## As 32 entidades

Todas públicas, somente leitura, sem Data Management, no modelo `FiscosysHubIntegration`:

| Grupo | Entidades |
|---|---|
| Documento | `FSFiscalDocumentBR`, `FSFiscalDocumentLineBR`, `FSFiscalDocumentReferencedBR`, `FSImportDeclarationBR` |
| Impostos e encargos da nota | `FSFiscalDocumentTaxTransBR`, `FSFiscalDocumentMiscChargeBR` |
| Contábil (fora da montagem) | `FSTaxTransBR`, `FSTaxWithholdBR`, `FSMarkupTransBR`, `FSTaxTableBR` |
| Cadastros | `FSFiscalDocModelBR`, `FSItemBR`, `FSUnitOfMeasureBR`, `FSAddressCityBR`, `FSCountryRegionBR`, `FSPostalAddressBR`, `FSFiscalEstablishmentBR`, `FSTaxServiceCodeBR` |
| Parceiros | `FSCustomerBR`, `FSVendorBR` |
| Módulo contábil (Parte II do `04`) | `FSGeneralJournalLineBR`, `FSMainAccountBR`, `FSCostCenterBR` |
| Inventário (Parte III do `04`) | `FSInventOnHandBR`, `FSInventTransBR`, `FSInventLocationBR`, `FSInventJournalBR` |
| Produção e Bloco K (Parte IV do `04`) | `FSProductionOrderBR`, `FSProductionBOMLineBR`, `FSProductionCoProductBR`, `FSFiscalProductionBR`, `FSFiscalProductionLineBR` |

O entity set no OData é o nome no plural: `/data/FSFiscalDocumentBRs`.

**Versão.** O pacote (modelo 1.0.0.0) é compilado contra o metadado da **10.0.47** (aplicação 10.0.2527.160, compilador
7.0.7858). Roda no fiscosysdev, que a Microsoft atualizou sozinha para a **10.0.49** (10.0.2790.46) em 06/10/2026: aceite
de 2026-10-15 (`04`, §31.1). A Microsoft garante o pacote compilado numa versão nas versões **iguais ou maiores**; num
ambiente abaixo da 10.0.47 o deploy pode falhar. Antes de instalar num cliente, conferir a versão dele.

Segurança: privilégio de leitura por entidade + a role `FiscosysHubIntegration`
("FiscosysHub - integração (somente leitura)"), que precisa ser atribuída ao usuário da app
registration no F&O.

**Nome do pacote (14/10/2026).** O pacote e o modelo se chamavam `FiscalHubIntegration`, e a role,
`FSFiscalHubIntegration`. Os dois viraram `FiscosysHubIntegration` antes do primeiro cliente. As entidades, os
privilégios e os nomes no OData não mudaram. Num ambiente que já tem o pacote antigo, ele sai antes do deploy do novo
(os objetos têm o mesmo nome e não convivem em dois módulos), e a role nova precisa ser atribuída de novo ao usuário da
app.

**Reforma e notas referenciadas (14/10/2026).** Campos novos em quatro entidades (CBS/IBS sem contabilização, CST da
cClassTrib, local da prestação do serviço com IBGE, motivo do NIF, NBS e indicador da operação), a entidade nova
`FSFiscalDocumentReferencedBR`. A natureza de rendimento dos retidos entrou primeiro pela entidade padrão
`TaxServiceCodeEntities` e, em 15/10, pela `FSTaxServiceCodeBR`. O detalhe está no `04`, seção 4.4.

**Pacote autossuficiente e Bloco K (15/10/2026).** O hub só lê entidades `FS*`, e a role só referencia privilégios
nossos (`04`, §26): o cliente instala o pacote, atribui a role e não libera mais nada. Entraram 8 entidades — a produção
e o Bloco K inteiro (OP, lista da OP, coprodutos, diários de estoque, produção dos Livros fiscais) e as substitutas das
padrão `FiscalEstablishments` e `TaxServiceCodeEntities` — e três campos na `FSInventTransBR`. O detalhe está na Parte IV
do `04`.

**Transição: a entidade padrão que o hub ainda lê.** O diretório de empresas e a descoberta por período (ADR-0032) leem a
`FiscalEstablishments`, e a role ainda referencia o privilégio padrão dela, o `FiscalEstablishmentEntityView` (Read).
Quando o hub passar a ler a `FSFiscalEstablishmentBRs` (mesmos nomes de campo), o privilégio sai da role. O que o hub lê
dela está no `04`, §7.

**Como conferir pelo hub.** O botão **Testar credencial** da aba do ERP, em Configurações, pede um token novo ao Entra ID
e lê `/data/FSFiscalDocumentBRs?$top=1&$select=FiscalDocumentRecId&cross-company=true` (ADR-0031). Um 403 é a role ou
o privilégio da entidade que falta. Um 401 é o app que não está cadastrado no F&O, em Aplicativos do Microsoft Entra
ID. Uma leitura vazia não prova o acesso às empresas: entre empresas, a falta de acesso também devolve vazio.

## Guias

**Vai mapear um módulo novo (contábil, estoque, outro ERP)?** Comece pelo
[`07-como-decidir-quais-entidades-criar.md`](07-como-decidir-quais-entidades-criar.md). Ele é o
método que veio antes das 16 entidades fiscais: ler o conector que o cliente já tem, procurar na
documentação o caminho padrão, **medir os dois contra dado real**, escrever a decisão, e só então
criar. Pular essa fase é o jeito de herdar os erros do conector antigo.

**Já sabe qual entidade quer e vai escrever o XML?** Vá para o
[`06-receita-criar-entidade-na-mao.md`](06-receita-criar-entidade-na-mao.md). Ele é a receita
operacional: onde o arquivo mora (são três lugares), a anatomia do XML, as quatro regras que
custaram ciclo de build, e como conferir no ambiente depois do sync. Os documentos `04` e `05`
são a referência por trás dele.

**Convenção de pastas.** Cada conector de ERP tem a sua própria pasta na raiz, com README e
documentos numerados próprios — `d365/` é a do Dynamics 365 F&O. Um conector novo segue a mesma
forma, em vez de misturar documentos em uma pasta comum.

**Agente: `/agent-d365`.** A skill `.claude/skills/agent-d365/SKILL.md` junta as regras e os fluxos deste diretório para o
Claude (Claude Code no repositório e Cowork): criar ou alterar entidade, validar depois de publicar, diagnosticar deploy e
acompanhar as versões do D365. Ela usa as ferramentas de `tools/`. Um conector novo ganha o seu agente no mesmo molde (ex.: `/agent-oracle`).

| Doc | Assunto |
|---|---|
| [`00-setup-e-conexao-do-visual-studio.md`](00-setup-e-conexao-do-visual-studio.md) | Preparar o VS e conectar ao ambiente UDE |
| [`01-criar-e-publicar-data-entity.md`](01-criar-e-publicar-data-entity.md) | Criar uma data entity e publicá-la no OData |
| [`02-business-event-status-changed.md`](02-business-event-status-changed.md) | Business event no status (papel revisto pelo ADR-0023 — ver aviso no topo do doc) |
| [`03-deploy-e-promocao.md`](03-deploy-e-promocao.md) | Deployable package e promoção para o cliente |
| [`04-mapeamento-de-entidades.md`](04-mapeamento-de-entidades.md) | **O que** ler de cada entidade: campos, relações, armadilhas do legado |
| [`05-achados-de-metadata-e-ciclo-de-deploy.md`](05-achados-de-metadata-e-ciclo-de-deploy.md) | **O que dá errado** ao expor: campos escondidos, JoinMode, ciclo build/deploy/sync |
| [`06-receita-criar-entidade-na-mao.md`](06-receita-criar-entidade-na-mao.md) | **Receita passo a passo** para criar ou alterar uma entidade editando o XML, com o VS fechado |
| [`07-como-decidir-quais-entidades-criar.md`](07-como-decidir-quais-entidades-criar.md) | **Como decidir o que criar** antes de escrever XML: ler o conector antigo, achar o caminho padrão, medir com dado real |
| [`glossario-x++-fno.md`](glossario-x++-fno.md) | Termos de X++ e F&O |
| [`tools/README.md`](tools/README.md) | **Ferramentas:** conferir o pacote antes do build, validar no ambiente, rodar o Postman, ler o log de deploy e o radar de versão |

`model/` guarda a cópia versionada do metadado (AOT) das entidades, privilégios e role.
`postman/` tem a collection de teste do OData: smoke test das 14 fiscais, testes de regressão e as pastas do contábil e do inventário, com publicação e aceite estrutural das 3 de cada.

## Status

- [x] **Fase 1 — Entidades.** 14 publicadas, respondendo HTTP 200 no OData, cadeia completa do
      conector validada ponta a ponta com nota de mercadoria (NF-e 55) e de serviço (modelo SE).
- [x] **Contábil.** 3 entidades publicadas e aceitas em 2026-10-01 (seção 17.1 do `04`).
- [x] **Inventário.** 3 entidades publicadas e aceitas em 2026-10-01 (seção 24.1 do `04`); a `FSItemBR` ganhou
      `InventUnitId` e `ProductName`.
- [x] **Produção e Bloco K.** 8 entidades publicadas e aceitas em 2026-10-15 (§31.1 do `04`).
- [x] **Lacunas do legado.** A `FSImportDeclarationBR` e 7 campos em 4 entidades, publicados e aceitos em 2026-10-16
      (§32.8 do `04`).
- [ ] **Fase 2 — Pacote.** Deployable package + import validado no nosso ambiente primeiro.

### Fora do roadmap: gatilho por evento

A antiga fase 2 (CoC + business event disparando no status) **saiu do roadmap**. Dois motivos:
não cobre todos os caminhos de escrita, então nunca poderia ser a garantia (ADR-0023); e exige X++
no pacote, que hoje é **metadado puro** — 32 entidades, 32 privilégios, uma role (que ainda referencia o privilégio
padrão `FiscalEstablishmentEntityView`, até o hub ler a `FSFiscalEstablishmentBR`), zero código.

Evento só compra **latência**, e com poll de 10s a detecção fica em 5s na média. Para despachar nota
já aprovada, isso não é gargalo.

**Gatilho para reabrir:** se o intervalo de poll precisar passar de **1 minuto** (por contagem de
tenants, limites de service protection, ou SLA contratual de tempo quase real), avaliar **Data event**
— não o CoC. Ver ADR-0023, seção "Gatilho por evento: fora do roadmap".

Pendências de mapeamento: seção 11 do `04`. Pendência operacional: atribuir a role ao usuário de
integração.
