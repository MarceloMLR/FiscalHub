# Histórico dos pull requests — GitHub `MarceloMLR/FiscalHub`

Exportado em 2026-10-16, antes da migração para o Azure DevOps, por `node tools/exportar-prs.js`.

**Por que este arquivo existe.** A importação de um repositório para o Azure Repos leva o git — commits,
branches, tags — e **não leva os pull requests**. No nosso caso isso não é detalhe de ferramenta: a descrição
de cada PR é onde ficou escrito *por que* a mudança é assim, o que a conferência em tela achou que a suíte não
pegou, e o resultado de cada prova por inversão. Nada disso está na mensagem de commit. Então os corpos vêm
para dentro do repositório, que é a única coisa que migra.

**O que está aqui:** os 87 PRs, do #1 ao #87, com título,
estado, datas, branch, commit de merge, o link original e o corpo **na íntegra e sem edição** — inclusive os
títulos internos deles, que por isso aparecem nos seus níveis originais de cabeçalho.

**O que não está, e não se perdeu:** nenhum PR deste repositório tem comentário ou review com texto
(conferido nesta exportação). A revisão sempre aconteceu na conversa e virou corpo de PR ou comentário de
código. Então o que esta exportação não traz é o que nunca existiu.

**O que continua só no GitHub:** os diffs renderizados e os links `https://github.com/...`. Os diffs estão no
git, que migra; os links viram históricos.

---

## Índice

| PR | Título | Mesclado | Branch |
|---|---|---|---|
| [#1](#pr-1) | docs: reescreve README com diagrama e adiciona contrato Avalara de re… | 2026-06-08 | `docs/readme` |
| [#2](#pr-2) | feat(application): portas de entrada/saida + nomes padronizados em ingles | 2026-06-09 | `feat/application-ports` |
| [#3](#pr-3) | feat(pipeline): nucleo da esteira (ProcessAsync) + porta de rastreio/… | 2026-06-10 | `feat/pipeline` |
| [#4](#pr-4) | test(pipeline): cobre fluxo feliz e idempotencia do DocumentPipeline | 2026-06-11 | `test/pipeline-core` |
| [#5](#pr-5) | feat(xml-adapter): parser de NF-e (modelo 55) com bloco IBS/CBS/IS | 2026-06-11 | `feat/xml-adapter` |
| [#6](#pr-6) | refactor: agrupa adapters e seus testes em pastas Inbound/Outbound | 2026-06-11 | `refactor/group-adapters` |
| [#7](#pr-7) | feat(avalara-adapter): mapper GoodsInvoice -> documento Avalara | 2026-06-11 | `feat/avalara-adapter` |
| [#8](#pr-8) | feat(validation): validacao de integracao da NF-e (completude + mapea… | 2026-06-11 | `feat/integration-validation` |
| [#9](#pr-9) | feat(pipeline): liga validacao de integracao no ProcessAsync | 2026-06-11 | `feat/pipeline-validation` |
| [#10](#pr-10) | Feat/avalara dispatcher | 2026-06-11 | `feat/avalara-dispatcher` |
| [#11](#pr-11) | docs: status de handoff, brief de infra e normalizacao de fim de linha | 2026-07-21 | `docs/handoff` |
| [#12](#pr-12) | feat(avalara): cache de token por tenant com renovacao e protecao de concorrencia | 2026-07-23 | `feat/avalara-token-cache` |
| [#13](#pr-13) | feat(infra): SqlProcessingStore com idempotencia no indice unico | 2026-07-23 | `feat/sql-processing-store` |
| [#14](#pr-14) | feat(xml): XmlGoodsInvoiceSource le blob e delega ao parser | 2026-07-23 | `feat/xml-blob-source` |
| [#15](#pr-15) | feat(host): composicao e e2e local (XML->esteira->mock->SQL) | 2026-07-23 | `feat/local-e2e-composition` |
| [#16](#pr-16) | docs(dx): guia de execucao local + mock expondo o json | 2026-07-23 | `feat/dx-running-doc` |
| [#17](#pr-17) | feat: rastreabilidade de payloads (fotos dominio/destino no Blob) | 2026-07-23 | `feat/traceability` |
| [#18](#pr-18) | feat: poll de status assincrono + idempotencia por estado | 2026-07-23 | `feat/status-poll` |
| [#19](#pr-19) | feat: gatilho por Service Bus (fila claim-check + consumidor) | 2026-07-23 | `feat/service-bus` |
| [#20](#pr-20) | feat: gatilho de ingestao por drop no Blob (Event Grid no cloud) | 2026-07-23 | `feat/blob-drop-ingress` |
| [#21](#pr-21) | feat: dead-letter visivel + poll resiliente | 2026-07-23 | `feat/dlq-hardening` |
| [#22](#pr-22) | feat: toggle de erro no mock (demo de rejeicao/reprocesso) | 2026-07-23 | `feat/mock-error-toggle` |
| [#23](#pr-23) | feat: GET /documents + porta de queries (base do dashboard) | 2026-07-23 | `feat/dashboard-read-api` |
| [#24](#pr-24) | feat: dashboard (Vite/React/MUI) — lista + rastreabilidade | 2026-07-24 | `feat/dashboard` |
| [#25](#pr-25) | feat: EF Migrations no lugar de EnsureCreated | 2026-07-24 | `feat/ef-migrations` |
| [#26](#pr-26) | feat: numero (nNF) e modelo reais da nota | 2026-07-24 | `feat/note-number-model` |
| [#27](#pr-27) | feat(directory): porta ICompanyDirectory + adapter JSON (dev local) | 2026-07-24 | `feat/company-directory` |
| [#28](#pr-28) | feat(discovery): integracao manual por descoberta pull | 2026-07-24 | `feat/manual-integration` |
| [#29](#pr-29) | fix(pipeline): idempotencia por gatilho (manual recarrega, evento dedupa) | 2026-07-24 | `fix/manual-reload-idempotency` |
| [#30](#pr-30) | feat(dashboard): tela de integracao manual | 2026-07-24 | `feat/manual-integration-ui` |
| [#31](#pr-31) | feat(pipeline): idempotencia por conteudo (hash do cru) | 2026-07-24 | `feat/content-idempotency` |
| [#32](#pr-32) | docs(adr): 0016 regra unica de conteudo (sem distinguir entrada/saida) | 2026-07-24 | `docs/idempotency-neutral` |
| [#33](#pr-33) | fix(dashboard): centraliza e aumenta o card da integracao manual | 2026-07-24 | `fix/manual-ui-centered` |
| [#34](#pr-34) | feat(integrations): execucao + IntegrationRunner compartilhado | 2026-07-24 | `feat/integration-executions` |
| [#35](#pr-35) | feat: agendador + tela de Integracoes (imediata/agendada + nota por numero) | 2026-07-24 | `feat/scheduler` |
| [#36](#pr-36) | feat: autenticacao JWT + escopo por tenant (backend e front) | 2026-07-27 | `feat/auth` |
| [#37](#pr-37) | feat(connectors): perfil de conector por tenant (B1) | 2026-07-27 | `feat/connector-profile` |
| [#38](#pr-38) | feat(connectors): resolucao por tenant em runtime + tela admin (B2/B3) | 2026-07-27 | `feat/connector-runtime-ui` |
| [#39](#pr-39) | feat(dashboard): tela de conectores schema-driven (tabs + inputs por adapter) | 2026-07-27 | `feat/connector-schema-form` |
| [#40](#pr-40) | feat(design): design system v3 (tokens/fontes) + Login redesenhado | 2026-07-27 | `feat/design-system-v3` |
| [#41](#pr-41) | feat(design): aplica o tema v3 em todo o app (petroleo + Manrope + neutros) | 2026-07-27 | `feat/design-v3-theme` |
| [#42](#pr-42) | feat(ui): tema claro/escuro + shell reorganizado (design v3) | 2026-07-31 | `feat/design-v3-dark-shell` |
| [#43](#pr-43) | feat(auth): comportamento do login — manter conectado + esqueci/redefinir senha | 2026-07-31 | `feat/login-behavior` |
| [#44](#pr-44) | refactor(ui): modais de grupo e detalhe da nota no design system | 2026-07-31 | `feat/note-detail-modal` |
| [#45](#pr-45) | feat(admin): gestao de usuarios por tenant + cadastro do tenant | 2026-07-31 | `feat/users-admin` |
| [#46](#pr-46) | docs(adr-0020): topologia de deploy + API base em runtime (frontend unico) | 2026-07-31 | `docs/deploy-topology` |
| [#47](#pr-47) | fix(ui): numeros dos paineis + chip Inativo em vermelho | 2026-07-31 | `fix/ui-kpi-numbers-e-chip-inativo` |
| [#48](#pr-48) | fix(ui): tipografia normal no codigo da empresa e no numero da nota | 2026-07-31 | `fix/ui-tipografia-normal` |
| [#49](#pr-49) | feat(support): adapter de chamados de suporte (Freshdesk) [ADR-0021] | 2026-08-03 | `feat/support-ticket-freshdesk` |
| [#50](#pr-50) | test+chore: testes de suporte/admin + limpeza de telas orfas | 2026-08-03 | `chore/cleanup-tests` |
| [#51](#pr-51) | docs(d365): fecha a Fase 1 — 14 entidades validadas, achados de metadata e limpeza de roadmap | 2026-09-25 | `docs/d365-entidades-achados-metadata` |
| [#52](#pr-52) | feat: descoberta de documentos fiscais no D365 por polling | 2026-09-25 | `feat/d365-change-feed-polling` |
| [#53](#pr-53) | feat(d365): montagem da nota por chave estrangeira, sem ponteiro polimorfico | 2026-09-26 | `feat/d365-entidades-fiscais-por-fk` |
| [#54](#pr-54) | feat: montagem do documento D365 a partir da fila de descoberta | 2026-09-27 | `feat/d365-document-assembly` |
| [#55](#pr-55) | docs(openspec): arquiva add-d365-document-assembly e sincroniza as specs | 2026-09-27 | `docs/archive-d365-document-assembly` |
| [#56](#pr-56) | feat: conector, nao validador - contrato pelos JSONs reais da Avalara | 2026-09-27 | `feat/connector-not-validator` |
| [#57](#pr-57) | docs(openspec): arquiva connector-not-validator e sincroniza as specs | 2026-09-27 | `docs/archive-connector-not-validator` |
| [#58](#pr-58) | feat(tenant): o tenant vem de quem esta logado, nunca da requisicao (D18, parte 1) | 2026-09-27 | `feat/connect-avalara-sandbox` |
| [#59](#pr-59) | feat(avalara): credencial por tenant, segredo pela tela no cofre e a quarta foto (D18, parte 2) | 2026-09-27 | `feat/connect-avalara-sandbox-parte-2` |
| [#60](#pr-60) | docs(openspec): arquiva connect-avalara-sandbox e sincroniza as specs | 2026-09-28 | `docs/archive-connect-avalara-sandbox` |
| [#61](#pr-61) | feat: integracao automatica pela tela, pelo poll.enabled, e o rebobinamento que reprocessa | 2026-09-28 | `feat/automatic-integration-switch` |
| [#62](#pr-62) | docs(openspec): arquiva automatic-integration-switch e sincroniza as specs | 2026-09-28 | `docs/archive-automatic-integration-switch` |
| [#63](#pr-63) | feat: empresa, filial e dia do 365, o motivo legivel e as fotos cruas so para Admin | 2026-09-29 | `feat/establishment-and-readable-dashboard` |
| [#64](#pr-64) | Navegação por módulo, a situação da integração automática e o teste de credencial | 2026-10-01 | `feat/module-navigation-and-integration-panel` |
| [#65](#pr-65) | Arquiva module-navigation-and-integration-panel e sincroniza as specs | 2026-10-01 | `docs/archive-module-navigation-and-integration-panel` |
| [#66](#pr-66) | Arquiva establishment-and-readable-dashboard e sincroniza as specs | 2026-10-01 | `docs/archive-establishment-and-readable-dashboard` |
| [#67](#pr-67) | D365: entidades do contábil e do inventário | 2026-10-01 | `feat/d365-accounting-and-inventory-entities` |
| [#68](#pr-68) | O diretório do ERP, a descoberta por período do D365 e os filtros dos cards | 2026-10-02 | `feat/erp-company-directory-and-card-filters` |
| [#69](#pr-69) | fix: a empresa do diretorio e o CNPJ completo, comparada pela raiz | 2026-10-05 | `feat/company-root-in-directory` |
| [#70](#pr-70) | feat: o de/para do estabelecimento vem da listagem da plataforma | 2026-10-05 | `feat/platform-establishment-resolution` |
| [#71](#pr-71) | fix: a listagem de estabelecimentos aceita as duas formas da plataforma | 2026-10-08 | `feat/platform-listing-shape` |
| [#72](#pr-72) | chore(openspec): arquiva as duas changes e fecha o vazamento de identificadores reais | 2026-10-08 | `chore/archive-establishment-resolution` |
| [#73](#pr-73) | feat: a credencial do perfil e o CNPJ do estabelecimento na execucao | 2026-10-08 | `feat/explicit-credential-and-execution-cnpj` |
| [#74](#pr-74) | fix(test): a conexao avulsa do teste da montagem leva a credencial, e a change e arquivada | 2026-10-08 | `fix/integration-test-auth` |
| [#75](#pr-75) | feat: rebranding do dashboard para a identidade Fiscosys, o produto vira FiscosysHub | 2026-10-09 | `feat/rebrand-dashboard-to-fiscosys` |
| [#76](#pr-76) | docs(openspec): arquiva rebrand-dashboard-to-fiscosys | 2026-10-09 | `docs/archive-rebrand-dashboard-to-fiscosys` |
| [#77](#pr-77) | docs(openspec): documenta três capabilities que já rodavam sem spec | 2026-10-09 | `docs/document-implemented-capabilities` |
| [#78](#pr-78) | feat(conectores): a conta de plataforma, os conectores com módulos e integrações, e o ambiente da instância | 2026-10-14 | `feat/platform-account-and-connectors` |
| [#79](#pr-79) | docs(openspec): arquiva platform-account-and-connectors e cria a spec platform-account-connectors | 2026-10-14 | `docs/archive-platform-account-and-connectors` |
| [#80](#pr-80) | Navegacao por conector e visao tecnica decidida no servidor | 2026-10-15 | `feat/connector-navigation-and-admin-json` |
| [#81](#pr-81) | O dashboard para de tratar falha como resposta | 2026-10-15 | `feat/dashboard-info-failure` |
| [#82](#pr-82) | A ligacao passa a ter teste, e nao so a escolha | 2026-10-15 | `test/dashboard-component-test-stack` |
| [#83](#pr-83) | A chamada da trava da chave do conector passa a ter teste | 2026-10-16 | `test/adapter-key-guard-proof` |
| [#84](#pr-84) | D365: pacote com 32 entidades, ferramentas, /agent-d365 e ADR-0040 (proposto) | 2026-10-16 | `feat/d365-pacote-fiscosys-e-reforma` |
| [#85](#pr-85) | feat(hub): capturar sem enviar - a chave de envio ao conector de saida | 2026-10-16 | `feat/capture-without-dispatch` |
| [#86](#pr-86) | chore(tools): guarda da numeracao dos ADRs, para a colisao aparecer antes do merge | _open_ | `chore/conferir-numeracao-adr` |
| [#87](#pr-87) | fix(servicebus): a dead-letter aprende a diferenca entre invalida e falhou | _open_ | `fix/dead-letter-poison` |

---

<a id="pr-1"></a>

## PR #1 — docs: reescreve README com diagrama e adiciona contrato Avalara de re…

**Estado:** merged · **Aberto:** 2026-06-08 · **Mesclado:** 2026-06-08 · **Branch:** `docs/readme` → `main` · **Commit de merge:** `0c9dd39` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/1)

_Sem descrição._

---

<a id="pr-2"></a>

## PR #2 — feat(application): portas de entrada/saida + nomes padronizados em ingles

**Estado:** merged · **Aberto:** 2026-06-09 · **Mesclado:** 2026-06-09 · **Branch:** `feat/application-ports` → `main` · **Commit de merge:** `91ef546` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/2)

_Sem descrição._

---

<a id="pr-3"></a>

## PR #3 — feat(pipeline): nucleo da esteira (ProcessAsync) + porta de rastreio/…

**Estado:** merged · **Aberto:** 2026-06-10 · **Mesclado:** 2026-06-10 · **Branch:** `feat/pipeline` → `main` · **Commit de merge:** `8199ebf` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/3)

…idempotencia

- DocumentPipeline<T>.ProcessAsync: idempotencia -> busca -> envio -> registro
- IProcessingStore: idempotencia por envio registrado + gravacao do resultado
- retry/DLQ delegados ao Service Bus (casca vem depois)
- ADR-0004

---

<a id="pr-4"></a>

## PR #4 — test(pipeline): cobre fluxo feliz e idempotencia do DocumentPipeline

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `test/pipeline-core` → `main` · **Commit de merge:** `4be5d24` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/4)

_Sem descrição._

---

<a id="pr-5"></a>

## PR #5 — feat(xml-adapter): parser de NF-e (modelo 55) com bloco IBS/CBS/IS

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `feat/xml-adapter` → `main` · **Commit de merge:** `d12ef6d` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/5)

- NfeXmlParser: XML -> GoodsInvoice via LINQ-to-XML 
- NfeParseException para campo obrigatorio ausente
- testes TDD: fluxo valido + campo faltante (fixtures sinteticas NT 2025.002)

---

<a id="pr-6"></a>

## PR #6 — refactor: agrupa adapters e seus testes em pastas Inbound/Outbound

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `refactor/group-adapters` → `main` · **Commit de merge:** `63cceea` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/6)

_Sem descrição._

---

<a id="pr-7"></a>

## PR #7 — feat(avalara-adapter): mapper GoodsInvoice -> documento Avalara

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `feat/avalara-adapter` → `main` · **Commit de merge:** `9d4092b` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/7)

- DTOs internal do contrato (subconjunto NF-e 55) + InternalsVisibleTo p/ testes
- GoodsInvoiceToAvalara: IBS/CBS/IS explicito -> array generico impostos[]
- testes TDD: cabecalho/parceiro + reforma no impostos[]

---

<a id="pr-8"></a>

## PR #8 — feat(validation): validacao de integracao da NF-e (completude + mapea…

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `feat/integration-validation` → `main` · **Commit de merge:** `8d497d9` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/8)

…bilidade)

- IDocumentValidator<T> + ValidationResult
- GoodsInvoiceValidator: CFOP/NCM/CST/cClassTrib usaveis e >=1 item
- testes TDD

---

<a id="pr-9"></a>

## PR #9 — feat(pipeline): liga validacao de integracao no ProcessAsync

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `feat/pipeline-validation` → `main` · **Commit de merge:** `51d3a98` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/9)

- documento invalido e rejeitado (registrado com motivo) e nao enviado
- IProcessingStore.RecordRejectionAsync
- teste do pipeline cobre o caso invalido

---

<a id="pr-10"></a>

## PR #10 — Feat/avalara dispatcher

**Estado:** merged · **Aberto:** 2026-06-11 · **Mesclado:** 2026-06-11 · **Branch:** `feat/avalara-dispatcher` → `main` · **Commit de merge:** `6f6d8c4` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/10)

_Sem descrição._

---

<a id="pr-11"></a>

## PR #11 — docs: status de handoff, brief de infra e normalizacao de fim de linha

**Estado:** merged · **Aberto:** 2026-07-21 · **Mesclado:** 2026-07-21 · **Branch:** `docs/handoff` → `main` · **Commit de merge:** `6a97379` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/11)

Adiciona docs/STATUS.md (handoff entre sessoes e maquinas), versiona o brief de infraestrutura, adiciona .gitattributes para normalizar fim de linha, ignora a saida do BMAD e inclui o launchSettings do mock. Nenhuma mudanca de codigo.

---

<a id="pr-12"></a>

## PR #12 — feat(avalara): cache de token por tenant com renovacao e protecao de concorrencia

**Estado:** merged · **Aberto:** 2026-07-21 · **Mesclado:** 2026-07-23 · **Branch:** `feat/avalara-token-cache` → `main` · **Commit de merge:** `85c087f` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/12)

Implementa o provedor de token real (OAuth client credentials) com cache por tenant. Caminho rapido sem trava, semaforo por tenant, dupla verificacao apos a espera, margem de renovacao antes da expiracao e release no finally. Registrado no DI como singleton. Testes incluem corrida com 20 chamadas simultaneas provando uma unica busca. 28 testes verdes.

---

<a id="pr-13"></a>

## PR #13 — feat(infra): SqlProcessingStore com idempotencia no indice unico

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/sql-processing-store` → `main` · **Commit de merge:** `aca6d69` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/13)

Implementa o IProcessingStore em EF Core. Idempotencia garantida por indice unico (TenantId, NaturalKey) no banco - mais forte que checagem em memoria. Upsert no record, AlreadySubmitted checa Submitted/Confirmed. Testes em SQLite in-memory (respeita a constraint). Pin do SQLitePCLRaw 3.0.4 por vulnerabilidade no transitivo (NU1903 com warnings-as-errors). ADR-0005 documenta a decisao e o TOCTOU como hardening futuro. 33 testes verdes.

---

<a id="pr-14"></a>

## PR #14 — feat(xml): XmlGoodsInvoiceSource le blob e delega ao parser

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/xml-blob-source` → `main` · **Commit de merge:** `40ebb68` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/14)

Implementa IInboundSource<GoodsInvoice> para XML: le o XML da NF-e do Blob (pelo Locator da referencia) e converte via NfeXmlParser (ja testado). O SDK do Azure fica atras de IBlobReader (costura de testabilidade), com AzureBlobReader como implementacao real. Testes usam um blob fake, sem Azurite. 35 testes verdes.

---

<a id="pr-15"></a>

## PR #15 — feat(host): composicao e e2e local (XML->esteira->mock->SQL)

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/local-e2e-composition` → `main` · **Commit de merge:** `552611a` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/15)

Compoe todos os adapters + store + esteira num host (FiscalHub.Host) e sobe a infra local com docker-compose (Azurite + SQL). Adiciona as extensoes de DI AddXmlGoodsInvoiceSource e AddSqlProcessingStore. O fluxo roda de ponta a ponta local: POST /ingest le o XML do Blob (Azurite), valida, despacha pro mock de compliance, e grava Submitted + GUID no SQL. Idempotencia confirmada ao vivo (2o POST nao duplica). Etapa 1 do e2e; Service Bus vem na Etapa 2. 35 testes verdes.

---

<a id="pr-16"></a>

## PR #16 — docs(dx): guia de execucao local + mock expondo o json

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/dx-running-doc` → `main` · **Commit de merge:** `ac324ba` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/16)

Adiciona docs/RUNNING.md com o passo a passo para rodar o e2e local (compose up, mock, host, POST /ingest, consultar SQL e ver o JSON gerado). Atualiza o README (quick-start + link, estado atual). Incrementa o mock para guardar o corpo recebido e expor GET /documents/{id}, permitindo inspecionar o payload que o hub envia a Avalara.

---

<a id="pr-17"></a>

## PR #17 — feat: rastreabilidade de payloads (fotos dominio/destino no Blob)

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/traceability` → `main` · **Commit de merge:** `350c5c6` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/17)

Guarda tres fotos por documento (fonte XML via claim-check, dominio, destino) pra isolar em qual mapeamento um campo se perde num chamado. IProcessingTrace + BlobProcessingTrace; dispatcher emite dominio/destino antes do POST. NaturalKey no DispatchContext. Retencao por lifecycle policy. ADR-0006. 36 testes verdes.

---

<a id="pr-18"></a>

## PR #18 — feat: poll de status assincrono + idempotencia por estado

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/status-poll` → `main` · **Commit de merge:** `5a55298` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/18)

Fecha o ciclo assincrono (ADR-0003/0007): StatusPoller lista os documentos em voo, consulta o GUID e grava Confirmed/IntegrationError/Unconfirmed (limite pro 204 eterno). BackgroundService num timer. Idempotencia agora por estado: erro/unconfirmed liberam reenvio; reenvio reinicia Attempts. 45 testes verdes.

---

<a id="pr-19"></a>

## PR #19 — feat: gatilho por Service Bus (fila claim-check + consumidor)

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/service-bus` → `main` · **Commit de merge:** `844da24` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/19)

Etapa 2: /ingest enfileira a referencia (202); consumidor le da fila e chama a esteira. Retry/DLQ nativos (MaxDeliveryCount=5). Portas IDocumentQueue + IDocumentPipeline; adapter FiscalHub.Adapters.Messaging.ServiceBus; emulador no docker-compose + Config.json. ADR-0008. 48 testes verdes.

---

<a id="pr-20"></a>

## PR #20 — feat: gatilho de ingestao por drop no Blob (Event Grid no cloud)

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/blob-drop-ingress` → `main` · **Commit de merge:** `3c6efa3` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/20)

Watcher observa a zona de drop, move pro container duravel e enfileira (claim-check). Adapter trocavel por Event Grid no cloud. POST /drop/{key} (dev) simula o arquivo caindo. ADR-0009. 51 testes verdes. Depende do #19 (ja na main).

---

<a id="pr-21"></a>

## PR #21 — feat: dead-letter visivel + poll resiliente

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/dlq-hardening` → `main` · **Commit de merge:** `22741aa` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/21)

Torna falhas de processamento rastreaveis: DeadLetterTriggerService registra a mensagem esgotada como DeadLettered com o motivo. Hardening do poll: Avalara trata 404 como pendente (igual 204) e o StatusPoller isola falha por documento (um GUID ruim nao trava o lote). Descoberto rodando o e2e ao vivo. ADR-0010. 56 testes verdes.

---

<a id="pr-22"></a>

## PR #22 — feat: toggle de erro no mock (demo de rejeicao/reprocesso)

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/mock-error-toggle` → `main` · **Commit de merge:** `33c5fc1` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/22)

POST /admin/result/{value} força carregado/erro nos proximos documentos, pra demonstrar o caminho de rejeicao da Avalara e o reprocesso ao vivo. Fecha a ultima fatia do 'ciclo async'. Collection do Postman atualizada.

---

<a id="pr-23"></a>

## PR #23 — feat: GET /documents + porta de queries (base do dashboard)

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-23 · **Branch:** `feat/dashboard-read-api` → `main` · **Commit de merge:** `4cf66d9` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/23)

Lado de leitura separado do IProcessingStore (IDocumentQueries + SqlDocumentQueries). GET /documents lista status recentes; CORS pro front local. 58 testes verdes.

---

<a id="pr-24"></a>

## PR #24 — feat: dashboard (Vite/React/MUI) — lista + rastreabilidade

**Estado:** merged · **Aberto:** 2026-07-23 · **Mesclado:** 2026-07-24 · **Branch:** `feat/dashboard` → `main` · **Commit de merge:** `d0fa3b6` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/24)

Painel read-only: DataGrid com documentos/status e painel com as 3 fotos por documento. TanStack Query com auto-refetch. Host serializa enums como texto + CORS. ADR-0011. Front roda com npm install && npm run dev (5173) contra a API na 5200; nao entra no build .NET.

---

<a id="pr-25"></a>

## PR #25 — feat: EF Migrations no lugar de EnsureCreated

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/ef-migrations` → `main` · **Commit de merge:** `6fe4fbd` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/25)

Schema versionado: tool manifest + dotnet-ef, pacote Design, design-time factory, migracao InitialCreate. Host migra no startup (MigrateAsync). Provado com banco novo. ADR-0012. Fim do drop de banco a cada mudanca de modelo. 59 testes verdes.

---

<a id="pr-26"></a>

## PR #26 — feat: numero (nNF) e modelo reais da nota

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/note-number-model` → `main` · **Commit de merge:** `4cbe3d7` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/26)

Extrai nNF + modelo da NF-e, guarda via migration AddDocumentNumberModel (aplicada no startup SEM dropar banco), mostra na tabela de notas do grupo. Estreia do fluxo de migrations. 59 testes verdes.

---

<a id="pr-27"></a>

## PR #27 — feat(directory): porta ICompanyDirectory + adapter JSON (dev local)

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/company-directory` → `main` · **Commit de merge:** `861793c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/27)

Empresa/filial dos dropdowns da integracao manual passam por uma porta (ICompanyDirectory), nao pela Avalara direto.

- Porta na Application devolve modelo padrao Company/Branch.
- Adapter Directory.Json le companies.json do content-root (seed casa com os CNPJs dos XMLs de exemplo).
- Host: GET /companies e GET /companies/{code}/branches.
- Avalara entra como outro adapter da mesma porta quando tiver credencial.
- ADR-0013.

60 testes verdes (1 novo do adapter JSON). Verificado ao vivo: 2 empresas, filiais da matriz/SP, [] pra empresa inexistente.

---

<a id="pr-28"></a>

## PR #28 — feat(discovery): integracao manual por descoberta pull

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/manual-integration` → `main` · **Commit de merge:** `bbbf228` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/28)

Integracao manual (modo pull), reusando a porta IDocumentDiscovery.

- POST /integrations/manual: monta DiscoveryCriteria (empresa/filial/periodo), descobre as notas na origem e enfileira cada ref via IDocumentQueue - segue o caminho normal (consumidor -> fetch -> esteira -> dispatch -> poll).
- Adapter Discovery.Local: catalogo casando os XMLs semeados; chave de acesso como NaturalKey.
- Avalara/ERP entra como outro adapter da mesma porta no cloud.
- ADR-0014; endpoints no Postman (companies + integracao manual).

64 testes verdes. Verificado ao vivo ponta a ponta: descobriu 1 nota, enfileirou, processou ate Confirmed; reenvio de nota DeadLettered confirmou idempotencia por estado.

---

<a id="pr-29"></a>

## PR #29 — fix(pipeline): idempotencia por gatilho (manual recarrega, evento dedupa)

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `fix/manual-reload-idempotency` → `main` · **Commit de merge:** `ab4c8c0` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/29)

Idempotencia depende do gatilho.

- Evento (drop/Event Grid/fila): mantem o bloqueio por estado - nota ja Submitted/Confirmed nao reentra (protege contra evento duplicado; NF-e autorizada e imutavel).
- Manual: fura o bloqueio e reprocessa mesmo confirmada (recarga explicita do cliente).
- DocumentReference carrega IngestionTrigger; POST /integrations/manual marca Manual; mensagens antigas caem no default Event.
- ADR-0015.

65 testes verdes (novo: Manual_reload_reprocesses_even_if_already_submitted). Verificado ao vivo: nota Confirmed recarregada gerou nova submissao (ExternalId mudou).

---

<a id="pr-30"></a>

## PR #30 — feat(dashboard): tela de integracao manual

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/manual-integration-ui` → `main` · **Commit de merge:** `5a3f24f` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/30)

Tela de integracao manual no dashboard.

- Nova view (nav por estado): dropdowns empresa/filial vindos de /companies, seletor de periodo, botao que dispara POST /integrations/manual e mostra o resultado.
- Filial 'Todas' por padrao; periodo default do mes anterior ate hoje.
- fix: npm run build estava quebrado (tsc -b) - trocado por tsc --noEmit + vite build; corrigido erro de tipo pre-existente no slot loadingOverlay do DataGrid.

Verificado ao vivo no Chrome: tela renderiza, dropdown carrega as 2 empresas, disparo retorna '1 nota enfileirada'. Front builda limpo.

---

<a id="pr-31"></a>

## PR #31 — feat(pipeline): idempotencia por conteudo (hash do cru)

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/content-idempotency` → `main` · **Commit de merge:** `9afb6ce` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/31)

Idempotencia por conteudo, nao por estado.

Motivacao (correcao do ADR-0015): nota de ENTRADA o cliente da entrada no ERP; se corrige um valor, e a mesma chave com conteudo diferente e deve reintegrar. A imutabilidade so vale pra nota de saida.

- Fonte devolve FetchResult{Document, ContentHash} (SHA-256 do cru).
- Esteira busca antes de decidir; pula so se ja processou ESTE hash em estado terminal. Hash diferente reintegra; manual continua furando.
- Coluna ContentHash + migration.
- ADR-0016 refinando 0007/0015.

66 testes verdes (novo: correcao com hash diferente reintegra). Verificado ao vivo: re-drop do mesmo conteudo NAO reprocessou (mesmo ExternalId).

---

<a id="pr-32"></a>

## PR #32 — docs(adr): 0016 regra unica de conteudo (sem distinguir entrada/saida)

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `docs/idempotency-neutral` → `main` · **Commit de merge:** `e203c0c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/32)

Alinha o texto do ADR-0016 ao principio: a idempotencia por conteudo nao distingue nota de entrada/saida nem policia se a nota pode mudar. Se o cliente alterou no ERP, integra o que veio. Codigo ja era uniforme; so o texto se apoiava na imutabilidade da nota de saida.

---

<a id="pr-33"></a>

## PR #33 — fix(dashboard): centraliza e aumenta o card da integracao manual

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `fix/manual-ui-centered` → `main` · **Commit de merge:** `9177ffe` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/33)

Card da integracao manual estava encostado no canto superior esquerdo. Agora centralizado na area principal, maior (maxWidth 860) e com respiro do topo. Verificado no Chrome.

---

<a id="pr-34"></a>

## PR #34 — feat(integrations): execucao + IntegrationRunner compartilhado

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/integration-executions` → `main` · **Commit de merge:** `124ee46` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/34)

Fatia 1 da Fase 3 (agendador). Extrai o disparo de integracao num runner compartilhado (descobre -> enfileira -> registra execucao), usado pelo manual e, na proxima fatia, pelo agendador.

- IntegrationRunner + RunRequest + IntegrationMode (Manual/ScheduledDaily/ScheduledOnce).
- Manual fura idempotencia; agendado dedupa por conteudo (ADR-0016).
- Entidade IntegrationExecution (modo/empresa/filial/periodo/qtd) + migration; IExecutionStore/Queries.
- GET /executions; /integrations/manual agora usa o runner e registra a execucao.

69 testes verdes (runner + store de execucao). Verificado ao vivo: manual registra execucao (Manual 98765432 2026-06-01..2026-06-30 n=1).

---

<a id="pr-35"></a>

## PR #35 — feat: agendador + tela de Integracoes (imediata/agendada + nota por numero)

**Estado:** merged · **Aberto:** 2026-07-24 · **Mesclado:** 2026-07-24 · **Branch:** `feat/scheduler` → `main` · **Commit de merge:** `f582786` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/35)

Fase 3 + consolidacao da UI, sobre a fatia 1 (execucoes/runner, ja no main).

Backend:
- Agendador (D-1 recorrente + unico): IntegrationScheduler + BackgroundService, runner compartilhado, dedupe por conteudo, ticks UTC, /schedules + migration. ADR-0017.
- Descoberta por numero: LocalDiscovery filtra por DocumentNumber, propagado por RunRequest/manual (nota dentro do periodo).

Front:
- Uma tela 'Integracoes' junta imediata + agendamentos: modo Imediata/Diaria/Agendada, escopo Periodo ou Nota especifica, listas de agendamentos e execucoes. Remove as telas separadas.

71 testes verdes. Verificado ao vivo: agendamento roda/desativa; diaria criada fica Ativa; integrar nota por numero (456) enfileira 1.

---

<a id="pr-36"></a>

## PR #36 — feat: autenticacao JWT + escopo por tenant (backend e front)

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-27 · **Branch:** `feat/auth` → `main` · **Commit de merge:** `9ad8193` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/36)

Autenticacao (JWT proprio) + escopo por tenant, backend e front.

Backend:
- Tabela Users (senha PBKDF2, sem dependencia), /auth/login emite JWT HS256 com claims (inclui tenant), fallback policy protege tudo menos login/health, /auth/me restaura sessao.
- ITenantContext le o tenant do claim; consultas (documentos/grupos/execucoes/agendamentos) filtram por ele; manual/agendado usam o tenant do usuario. Usuarios dev semeados (tenant-a/tenant-b).

Front:
- Login, sessao restaurada via /auth/me, Bearer em todas as chamadas, 401 volta ao login. Menu de conta + Sair. Download via fetch+blob.

ADR-0018 (JWT proprio + PBKDF2 + localStorage; httpOnly em producao). 73 testes verdes.

Verificado ao vivo: sem token 401; admin (tenant-a) ve 5 docs; beta (tenant-b) ve 0; senha errada 401; UI login->painel->logout.

---

<a id="pr-37"></a>

## PR #37 — feat(connectors): perfil de conector por tenant (B1)

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-27 · **Branch:** `feat/connector-profile` → `main` · **Commit de merge:** `3ae8ae9` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/37)

Primeira fatia do modelo real de multi-tenant por conector. TenantConnectorProfile por tenant: adapter de entrada (ERP) e saida (compliance), ambiente, tempo real, e settings JSON por adapter (segredos por referencia kv:, resolvidos no Key Vault em producao). Store + migration + seed (tenant-a Dynamics365/realtime, tenant-b iScala/sem realtime, ambos Avalara). GET/PUT /connector so pra Admin, escopado ao tenant; /info le o ambiente do perfil. ADR-0019. Verificado: admin le o perfil, viewer 403, /info do perfil. Proximo: resolucao do adapter por tenant em runtime (B2) e tela admin (B3).

---

<a id="pr-38"></a>

## PR #38 — feat(connectors): resolucao por tenant em runtime + tela admin (B2/B3)

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-27 · **Branch:** `feat/connector-runtime-ui` → `main` · **Commit de merge:** `a6181ce` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/38)

Fecha o ciclo do perfil de conector por tenant (B2 + B3), sobre o B1 (modelo/config, ja no main).

B2 - resolucao em runtime: o dispatcher da Avalara le o perfil do tenant (via context.TenantId, que vem da mensagem, nao do HTTP) e monta URIs absolutas a partir da baseUrl do ambiente ativo; sem perfil, cai na config. A config virou comportamento.

B3 - tela admin de Conectores: so pro Admin (nav filtrado por role), edita o perfil (ambiente, tempo real, adapters de entrada/saida, settings JSON). Segredos como referencia kv:.

79 testes verdes (Avalara +1: base do perfil). Verificado: drop tenant-a -> Confirmed usando a base do perfil; tela carrega e edita o perfil; viewer nem ve a aba.

---

<a id="pr-39"></a>

## PR #39 — feat(dashboard): tela de conectores schema-driven (tabs + inputs por adapter)

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-27 · **Branch:** `feat/connector-schema-form` → `main` · **Commit de merge:** `1985dc8` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/39)

No lugar dos textareas de JSON, a tela de Conectores agora tem tabs (Entrada/Saida) e inputs tipados por adapter: seleciona o ERP (Dynamics365/iScala) e aparecem os campos dele; seleciona a plataforma de saida (Avalara/Mock) e aparecem os campos por ambiente (sandbox/producao). Segredos como referencia (kv:). O front serializa pro mesmo blob JSON do backend. Verificado ao vivo nos dois tabs, preenchido do perfil do tenant. (No modelo mais fiel, o schema viria do proprio adapter no backend.)

---

<a id="pr-40"></a>

## PR #40 — feat(design): design system v3 (tokens/fontes) + Login redesenhado

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-27 · **Branch:** `feat/design-system-v3` → `main` · **Commit de merge:** `be79bb8` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/40)

Adota o design system exportado do Claude Design (v3): Manrope + DM Mono, acento petroleo #0B5C7A, neutros azulados, chips de status por status do backend, raio 8px/controle 32px. tokens.css com as variaveis + primitivas base. Login reconstruido sem MUI. Referencia do design em design/. Fundacao pra propagar as demais telas retirando o MUI.

---

<a id="pr-41"></a>

## PR #41 — feat(design): aplica o tema v3 em todo o app (petroleo + Manrope + neutros)

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-27 · **Branch:** `feat/design-v3-theme` → `main` · **Commit de merge:** `283e96c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/41)

Mapeia os tokens do design system v3 no tema do MUI (paleta petroleo + semanticas, neutros azulados, Manrope, densidade, raio 8, overrides). Restila todas as telas de uma vez. Avatar do shell no acento. Verificado ao vivo no dashboard. Proximo: fidelidade fina (tabela densa, chips ponto+borda) tela a tela.

---

<a id="pr-42"></a>

## PR #42 — feat(ui): tema claro/escuro + shell reorganizado (design v3)

**Estado:** merged · **Aberto:** 2026-07-27 · **Mesclado:** 2026-07-31 · **Branch:** `feat/design-v3-dark-shell` → `main` · **Commit de merge:** `63995e4` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/42)

Fatia 1 da reforma para o design system v3.

## O que muda
- **Modo claro/escuro** com botão de alternância na topbar; preferência persiste em localStorage e cai no prefers-color-scheme do SO no primeiro acesso.
- **Tokens dark** em html[data-theme=dark] (extraídos da variante Dark do export v3) + tema MUI dinâmico (createAppTheme(mode)), então tanto o shell custom quanto as telas MUI viram junto.
- **Shell reconstruído fiel ao export**: marca, seções Operação/Administração, card de Ambiente ativo.
- **Reorganização do nav**: Conectores foi para dentro de **Configurações**; **Métricas** removida.
- /info passa a expor ealtime por tenant (alimenta o card de ambiente).

## Verificação
Rodado no Chrome nos dois temas (claro e escuro) e navegando Documentos e Configurações. Backend compila (erros do build local eram apenas lock de DLL da API em execução).

## Próximas fatias
Fidelidade fina das telas Documentos (KPIs/tabela/status chips) e Integrações ao export.

---

<a id="pr-43"></a>

## PR #43 — feat(auth): comportamento do login — manter conectado + esqueci/redefinir senha

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-07-31 · **Branch:** `feat/login-behavior` → `main` · **Commit de merge:** `d95240c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/43)

Implementa o comportamento por trás dos elementos do novo login.

## Manter conectado
- Marcado: token no localStorage (persiste ao fechar o navegador). Desmarcado: sessionStorage (some ao fechar a aba). O tokenStore lê dos dois.

## Esqueci / redefinir senha
- Porta IPasswordResetService + SqlPasswordResetService: token de uso único (guarda só o hash SHA-256), validade de 30 min, troca a senha via PBKDF2.
- Endpoints anônimos POST /auth/forgot-password (sempre 200, não vaza se o e-mail existe) e /auth/reset-password.
- UserRow ganha ResetTokenHash + ResetTokenExpiresAt (migration AddPasswordReset).
- Sem servidor de e-mail no dev: o forgot devolve o token e a tela mostra 'Redefinir agora' (atalho de dev). Em produção iria por e-mail.
- LoginPage com modos login/forgot/reset; lê ?reset=token da URL (para o link de e-mail).

## Verificação
20/20 e 19/19 testes. Ciclo forgot -> reset -> login com a nova senha verificado ao vivo (200).

---

<a id="pr-44"></a>

## PR #44 — refactor(ui): modais de grupo e detalhe da nota no design system

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-07-31 · **Branch:** `feat/note-detail-modal` → `main` · **Commit de merge:** `8a4659f` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/44)

Reconstroi os modais de nota fora do MUI Dialog, no design system v3 (dark-aware).

## O que muda
- **Modal** (novo, componente compartilhado): overlay + cartao com tokens (surface/borda/raio 12/sombra), cabecalho com titulo/subtitulo e X, corpo rolavel e rodape opcional. Fecha no Esc e no clique fora.
- **GroupModal**: a lista de notas do grupo vira uma tabela em grid de tokens (Numero/Modelo/Status/Consultas/Atualizado) com StatusChip; linha clicavel abre o detalhe.
- **NoteDialog**: usa o Modal; rodape com Fechar + Baixar arquivos.
- **DocumentDetail**: abas Origem/Dominio/Destino e visor de codigo agora em tokens (sai o MUI Tabs e o code block cinza fixo); o motivo da falha e o botao Reprocessar continuam.

## Verificacao
Aberto grupo -> nota -> abas/reprocessar/baixar, no claro e no escuro.

---

<a id="pr-45"></a>

## PR #45 — feat(admin): gestao de usuarios por tenant + cadastro do tenant

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-07-31 · **Branch:** `feat/users-admin` → `main` · **Commit de merge:** `beda62b` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/45)

## O que

Tela **Usuarios** (Admin) com dois blocos:
- **Dados do tenant** — razao social e CNPJ editaveis (cadastro fino; no modelo por-cliente o tenant e basicamente config da instancia).
- **Usuarios do tenant** — tabela (DataGrid v3) com criar, editar papel/nome, redefinir senha e ativar/desativar. Anti-lockout: o admin nao pode desativar nem rebaixar a propria conta.

## Backend
- Entidade \Tenant\ (fina) + coluna \User.Active\ (auth recusa inativo).
- \IUserAdminService\ / \ITenantAdminService\ + impls SQL, sempre filtrando pelo tenant do claim.
- Endpoints \/users\ (GET/POST/PUT + reset-password) e \/tenant\ (GET/PUT), todos \RequireRole(Admin)\.
- Migration \AddTenantsAndUserActive\ (backfill \Active=true\ p/ usuarios existentes) + seeds de dev.

## Verificacao
- Backend: build verde, migration aplicada, smoke test de login+/users+/tenant OK.
- Front: \	sc --noEmit\ limpo; tela verificada no Chrome (card do tenant com ACME Fiscal + CNPJ, 5 usuarios, chips de papel/status, anti-lockout no proprio admin, modal de novo usuario).

---

<a id="pr-46"></a>

## PR #46 — docs(adr-0020): topologia de deploy + API base em runtime (frontend unico)

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-07-31 · **Branch:** `docs/deploy-topology` → `main` · **Commit de merge:** `7b15083` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/46)

## O que

Registra a topologia de deploy que decidimos e prepara o frontend pra ela.

**ADR-0020** — backend **por-cliente** (isolamento fisico entre tenants), frontend **unico** servido sob \*.fiscalhub.com\, **roteamento por subdominio** e **API base resolvida em runtime** (nao assada no build). Um bundle serve todos: \^Gcme.fiscalhub.com\ fala com a API da ACME, \	msa.fiscalhub.com\ com a da TMSA. Consequencia: login cruzado e impossivel por construcao (a credencial de outro tenant nem existe naquela instancia).

**Front** — novo \esolveApiBase()\ (\dashboard/src/api/config.ts\), precedencia: \VITE_API_BASE_URL\ (dev) -> \window.__FISCALHUB__.apiBase\ (config por host, recomendada em prod) -> convencao \^Gpi.<tenant>.<root>\ -> localhost. O \client.ts\ usa ele no lugar do \VITE_API_BASE_URL\ de build.

## Verificacao
- \	sc --noEmit\ limpo.
- App recarregado no Chrome: resolucao cai em localhost:5200 (dev), login pela UI OK e 11 linhas carregadas — caminho cliente->API via runtime confirmado.

## Nota
So docs + config de frontend; sem mudanca de backend. Independente do #45.

---

<a id="pr-47"></a>

## PR #47 — fix(ui): numeros dos paineis + chip Inativo em vermelho

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-07-31 · **Branch:** `fix/ui-kpi-numbers-e-chip-inativo` → `main` · **Commit de merge:** `0b7fc7a` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/47)

Dois ajustes de UI:

1. **Numeros dos paineis** (KPIs de Documentos + contador de Integracoes): removido \ontVariantNumeric: tabular-nums\ (a variante tabular da Manrope deixava os digitos com espacamento feio) e afrouxado o \letter-spacing\ de ~-0.03em para -0.01em. Agora usam os algarismos proporcionais padrao da fonte.
2. **Chip de usuario Inativo**: de cinza apagado para os tokens de erro (vermelho), coerente com 'sem acesso'.

Verificado no Chrome: '15 agendamentos ativos' com digitos limpos e o 'Inativo' do Pedro em vermelho. \	sc --noEmit\ limpo.

---

<a id="pr-48"></a>

## PR #48 — fix(ui): tipografia normal no codigo da empresa e no numero da nota

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-07-31 · **Branch:** `fix/ui-tipografia-normal` → `main` · **Commit de merge:** `4ff016c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/48)

Remove o destaque (fonte mono + negrito) do **codigo da empresa** (tabelas de Documentos e Integracoes), do **numero da nota** (modal do grupo) e do cabecalho do detalhe da nota. Passam a usar a tipografia padrao (sans, peso normal), sem realce. Verificado no Chrome: coluna Empresa igual as demais. tsc limpo.

---

<a id="pr-49"></a>

## PR #49 — feat(support): adapter de chamados de suporte (Freshdesk) [ADR-0021]

**Estado:** merged · **Aberto:** 2026-07-31 · **Mesclado:** 2026-08-03 · **Branch:** `feat/support-ticket-freshdesk` → `main` · **Commit de merge:** `cbbe17b` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/49)

## O que

Abrir chamado de suporte direto da tela, para 1..N notas, com os logs anexados.

**ADR-0021.** Porta \ISupportTicketGateway\ (provider-agnostica) + \SupportTicketService\: para cada nota le os arquivos de rastreabilidade, monta **um zip por nota** (cabe no teto de 20MB do Freshdesk) e compoe a descricao com as infos de integracao (numero, status, motivo, external id). Adapter **Freshdesk real** (POST /api/v2/tickets multipart + Basic auth, credenciais do perfil do tenant) e adapter **Local** (mock dev). Campo \SupportAdapter\/\SupportSettings\ no perfil de conector + migration. Endpoint \POST /support/tickets\ escopado ao tenant (qualquer usuario autenticado).

**UI.** Checkbox nas notas do modal do grupo + barra 'Abrir chamado (N)'; botao avulso no detalhe da nota; \TicketModal\ interativo (titulo pre-preenchido, descricao, lista das notas com status, aviso de anexos zipados) com resultado (id/link).

## Verificacao
- Backend: build verde, migration aplicada, smoke test via mock (chamado aberto com 3 zips, ~4.3KB).
- Front: \	sc\ limpo; fluxo testado ao vivo no Chrome (selecao -> modal -> chamado LOCAL-... aberto com logs anexados).

## Follow-up
- Expor a config do adapter (Local/Freshdesk + dominio/apiKey) na tela de Configuracoes (hoje via perfil).
- Freshdesk real precisa de conta + API key na config do tenant (adapter ja implementado).

---

<a id="pr-50"></a>

## PR #50 — test+chore: testes de suporte/admin + limpeza de telas orfas

**Estado:** merged · **Aberto:** 2026-08-03 · **Mesclado:** 2026-08-03 · **Branch:** `chore/cleanup-tests` → `main` · **Commit de merge:** `d45b08e` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/50)

## Testes
- **SupportTicketService** (Application, 10 testes): um zip por nota, teto de 20MB (anexo extra rejeitado), adapter escolhido pelo perfil, validacao (nota/assunto/descricao), suporte nao configurado / adapter nao registrado, estimativa de bytes.
- **Admin em SQLite in-memory** (Infrastructure, 5 testes): usuarios (criacao + validacao de email/papel/senha, duplicidade, alterar papel/ativo, reset de senha, **isolamento por tenant**), cadastro do tenant (criado sob demanda + editado), e **login recusa usuario inativo**.

## Limpeza
Remove telas orfas em MUI fora da navegacao: \DocumentsPage.tsx\, \KpiCards.tsx\, \useDocuments.ts\ (nao referenciadas; o dashboard usa \GroupsPage\).

## Verificacao
30 testes Application + 24 Infrastructure verdes; \	sc --noEmit\ limpo.

---

<a id="pr-51"></a>

## PR #51 — docs(d365): fecha a Fase 1 — 14 entidades validadas, achados de metadata e limpeza de roadmap

**Estado:** merged · **Aberto:** 2026-09-25 · **Mesclado:** 2026-09-25 · **Branch:** `docs/d365-entidades-achados-metadata` → `main` · **Commit de merge:** `c2439a3` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/51)

## O que é

Fecha a **Fase 1** do pacote D365: as 14 data entities publicadas, validadas ponta a ponta contra o ambiente, com o metadado versionado no repo e os achados documentados.

## Por que importa

A publicação expôs **cinco armadilhas de metadata, e nenhuma delas gera erro de compilação**:

1. **`AccessModifier = Private` esconde o campo do OData em silêncio.** Eram 12 campos, incluindo o `CityWhereServicePerformed` — município da prestação, obrigatório na NFS-e. Um conector construído sobre a versão anterior emitiria NFS-e sem ele, e isso só apareceria na rejeição da prefeitura.
2. **Data source aninhado sem `<JoinMode>` vale InnerJoin** e anula o OuterJoin do pai. Era a causa do `$count = 0` no cabeçalho; foi para **83** depois da correção.
3. **`RefRecId` do cabeçalho** vem no payload mas não aceita `$select`/`$filter`.
4. **Data Management colide com campo de sistema** — desligado nas 14, como nas entidades equivalentes da Microsoft.
5. **`SysModifiedDateTime`** é o nome que a Microsoft usa para expor `ModifiedDateTime`, e é o que destrava o fallback por janela de data do ADR-0023.

## Commits

| | |
|---|---|
| `d4343b0` | versiona o metadado AOT (14 entidades + 14 privilégios + role) |
| `7e971d2` | doc novo `d365/05` com os achados e o ciclo build → deploy → sync |
| `1f21ad4` | atualiza mapeamento, índice e ADR-0023 com o estado validado |
| `377ff71` | smoke test das 14 no Postman + higiene do repo |
| `9a81b5f` | nota no ADR-0022 sobre nomes reais |
| `6074fcb` | tira o gatilho por evento do roadmap |

## Validação

Cadeia completa do conector exercitada contra dado real, em duas notas:

- **Mercadoria** (NF-e 55, `BRMF21-10000027`): imposto atribuído por linha via `SourceRecId` + `SourceTableId`, 4+4 = 8, o mesmo total do filtro por voucher. Valores fecham: 13.850 + IPI 2.077,50 = 15.927,50.
- **Serviço** (modelo SE, `BRMF21-10000019`): `CityWhereServicePerformed` resolvendo para São Paulo/SP (IBGE 3550308), código de serviço 14.06, ISS/PIS/COFINS/IRRF.

Também fechada a pendência que decidia o desenho do store: **nota cancelada não gera linha nova** (83 cabeçalhos, zero voucher duplicado).

## Mudanças de decisão

- **ADR-0023** de Proposto para **Aceito**, com o item de staging revisto.
- **Gatilho por evento sai do roadmap.** Não cobre todos os caminhos de escrita e exige X++ num pacote que hoje é metadado puro. Gatilho para reabrir registrado no ADR: intervalo de poll passar de 1 minuto.

## Notas

`.gitattributes` fixa CRLF no metadado do AOT — sem isso o checkout no Windows sai em LF e não bate byte a byte com o model store.

Sem impacto no build .NET: só `d365/`, `docs/adr/` e `CLAUDE.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-52"></a>

## PR #52 — feat: descoberta de documentos fiscais no D365 por polling

**Estado:** merged · **Aberto:** 2026-09-25 · **Mesclado:** 2026-09-25 · **Branch:** `feat/d365-change-feed-polling` → `main` · **Commit de merge:** `f4d31c3` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/52)

Fecha a change `add-d365-change-feed-polling` (SDD): o worker que varre o D365 F&O em intervalo configuravel, descobre documentos fiscais novos ou alterados e publica cada um na fila de descoberta.

## O que entra

| camada | o que |
| --- | --- |
| Application | `IDocumentChangeFeed`, `ChangeFeedPoller`, cursor, `ILeaseStore`, validacao das settings |
| Infrastructure | stores de cursor e lease com fencing + migration |
| Messaging | fila `documents-discovered`, separada da fila de processamento |
| Adapter `D365Poll` | OData sobre `FSFiscalDocumentBRs`, keyset, 429 com Retry-After, dois token providers |
| Host | `ChangeFeedPollingService`, 60s por padrao, poll desligado em appsettings |

## As duas decisoes que moldaram o desenho

**Janela de data no lugar de change tracking.** As duas formas do F&O foram descartadas em campo: o DMF esta desligado nas 14 entidades (staging table conflita com o EDT) e o row version reprovou na validacao (outer joins, 11 data sources, tabelas da Microsoft). A descoberta usa `SysModifiedDateTime`, e o ADR-0023 ganhou a nota apontando para o ADR-0024.

**Keyset no lugar do `@odata.nextLink`.** O nextLink do F&O e offset (`$skip`/`$top`), nao skiptoken: uma atualizacao durante a varredura desloca as linhas e faz o offset pular exatamente um registro. O cursor `(SysModifiedDateTime, FiscalDocumentRecId)` elimina isso. Em regime permanente a diferenca seria pequena; num backfill, nao.

## Validacao

Suite: **222 aprovados, 1 ignorado, 0 avisos** com `-warnaserror`. `openspec validate --specs --strict`: 2 specs, 0 falhas.

Conferido contra o ambiente real: 83 cabecalhos em 5 paginas de 20, filtro keyset composto aceito, `maxpagesize` respeitado, header `Date` presente. `Voucher gt` e recusado pelo F&O por ser string — foi o que derrubou a ideia inicial de paginar por voucher.

Fluxo ponta a ponta exercitado na mao: le o F&O, cai na fila, o consumidor busca as entidades relacionadas. O roteiro esta em `docs/RUNNING.md` secao 6.

## Chave natural

`empresa|voucher`, confirmada pelo levantamento e pela regra de dominio: o voucher e unico e imutavel. Approved -> Cancelled mantem a mesma chave, o que confirma o modelo documento 1:N tentativas.

Risco que sobra: a sequencia do voucher e configurada por cliente. Confere-se uma vez no onboarding; se alguma reiniciar por exercicio, o desempate `empresa|ano|voucher` ou `empresa|RecId` ja esta disponivel sem consulta extra ao ERP.

## Dois pontos de atencao

O primeiro commit (`610cc9a`) e a correcao do contexto do D365 no `openspec/config.yaml` — esse arquivo e injetado no prompt de todo artefato, entao a correcao era pre-requisito desta change e veio junto em vez de virar PR separado.

Nada aqui foi exercitado com a app registration: todo o teste usou a identidade delegada do Azure CLI. O caminho `client_credentials` esta implementado mas nao rodou, e depende da role "FiscalHub - integracao (somente leitura)" ser atribuida no F&O.

## Fora do escopo, registrado

Modelo 01 fica fora do mapa, com "ignorado: modelo fora do escopo" gravado pelo roteamento — as 69 notas sao dado de demonstracao de 2015 a 2017. Quem precisar dele adiciona uma linha em `modelTypes`.

Proxima fatia: consumidor da fila e `IInboundSource` para o D365, que e o que abre o trabalho no payload da Avalara.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-53"></a>

## PR #53 — feat(d365): montagem da nota por chave estrangeira, sem ponteiro polimorfico

**Estado:** merged · **Aberto:** 2026-09-26 · **Mesclado:** 2026-09-26 · **Branch:** `feat/d365-entidades-fiscais-por-fk` → `main` · **Commit de merge:** `4179eea` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/53)

Duas entidades novas sobre as tabelas fiscais do D365, para que a montagem de uma nota deixe de depender de ponteiro polimórfico.

## O problema

A montagem chegava aos impostos e encargos pelo par `RecId` + `TableId`, apontando para o documento de **origem**. RecId não é único entre tabelas, só dentro de cada uma — se um RecId de encargo coincidisse com o `RefRecId` de uma linha, o imposto seria contado duas vezes, em silêncio, num valor fiscal.

Não é hipótese: na base do ambiente existe um `MarkupTrans.RecId` que vale `5637144578`, exatamente o mesmo número que o RecId de um documento fiscal.

## O que entra

| entidade | tabela | campos |
| --- | --- | --- |
| `FSFiscalDocumentTaxTransBR` | `FiscalDocumentTaxTrans_BR` | 31 |
| `FSFiscalDocumentMiscChargeBR` | `FiscalDocumentMiscCharge_BR` | 20 |

São tabelas que a Microsoft modelou na 10.0.13 com **FK declarada** para a linha fiscal e para o encargo — colunas distintas, nada a desempatar. Nenhuma entidade padrão as publica no OData (conferido: 4.513 entity sets do ambiente).

Cada uma expõe `FiscalDocumentRecId` pelo caminho da linha, então o filtro é direto pelo documento: uma chamada, sem cadeia de `or`, sem as duas fases que a falta desse campo obrigava.

## O que os dados mostraram

As 547 linhas da entidade nova comparadas uma a uma com as da `FSTaxTransBR`, casadas pelo `TaxTransRecId`:

| | |
| --- | --- |
| casadas 1:1 | 547 de 547 |
| idênticas | 439 |
| **sinal invertido** | **351** |
| **CST do IPI diferente** | **54** |
| zerado de um lado só | 54 |

**Sinal.** A contábil grava imposto de saída como crédito, negativo. A fiscal grava positivo, como a nota apresenta.

**CST do IPI.** Sempre no mesmo par: `01 -> 51` (48) e `05 -> 55` (6) — código de entrada contra o de saída. Em documento de saída a contábil carrega a família de entrada. Precisa de confirmação do fiscal, mas se procede seria rejeição na SEFAZ. Pela entidade antiga teríamos mandado assim.

**Zerado de um lado.** Nunca dois valores diferentes — sempre zero contra valor, nos dois sentidos. A tabela fiscal é a fonte do que vai no documento, mas **não é superconjunto**: o imposto de importação só existe do lado contábil. O `TaxTransRecId` exposto é a ponte.

## Retenção e encargo saem do caminho de montagem

Não existe tabela de retenção ligada ao documento, e não precisa: o flag `RetainedTax` marca as 12 linhas retidas (11 IRRF, 1 ISS), na mesma chamada dos outros impostos. A `FBTaxWithholdTrans_BR` é apuração do SPED e a `TaxWithholdTrans` padrão se liga a `CustTrans`/`VendTrans` — o modelo prende retenção à transação financeira.

Com isso, `FSTaxWithholdBR` (264 linhas, `Source` = `VendPayment`/`CustPayment`) e `FSMarkupTransBR` (12 linhas, vouchers `INV-*` e `JPMF-*`) deixam de ser fonte da nota. Continuam no pacote, em outro papel.

## Validação

Feita contra o ambiente após build, deploy e sync:

- `FSFiscalDocumentTaxTransBR`: 547 linhas, 32 campos (nenhum escondido), **83 de 83 documentos** alcançados, zero órfãos, média de 6,6 impostos por nota
- `FSFiscalDocumentMiscChargeBR`: 14 encargos, 21 campos, **14 de 14** chegam à linha e ao documento, em 12 notas
- Na `BRMF21-10000027`: 8 impostos pelos dois caminhos, mesmos códigos e bases — o novo em 1 chamada, o antigo em 2

Resultado: nenhum ponteiro polimórfico na montagem, e 4 chamadas por documento (cabeçalho, linhas, impostos, encargos) mais os cadastros em cache.

## Em aberto

Dois caminhos existem mas nunca passou dado por eles: encargo com imposto em cima (zero dos 547 impostos aponta para encargo) e encargo com `MarkupTrans` preenchido (nulo nos 14). Só fecham com uma nota que tenha frete ou despesa lançada.

E os 26 impostos zerados na fiscal com valor na contábil continuam sem explicação — levantei a hipótese de que reapareciam como encargo e testei: nenhum dos 14 casa com um IPI da mesma nota.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-54"></a>

## PR #54 — feat: montagem do documento D365 a partir da fila de descoberta

**Estado:** merged · **Aberto:** 2026-09-27 · **Mesclado:** 2026-09-27 · **Branch:** `feat/d365-document-assembly` → `main` · **Commit de merge:** `e0a7d96` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/54)

Fecha a change `add-d365-document-assembly` (SDD). A fila `documents-discovered`, que desde a fatia anterior recebia uma referencia por nota alterada no F&O e nao tinha consumidor, passa a ser consumida: a NF-e e montada a partir das entidades fiscais do D365 e entregue a mesma esteira do XML, sem salto nem armazenamento intermediario. Decisoes no ADR-0025.

## O que entra

| camada | o que |
| --- | --- |
| Application | origem na referencia + `InboundSourceResolver`; `DocumentRouter` com o desfecho `Ignored`; supressao da republicacao no `ChangeFeedPoller` (`ChangeFeedPublicationLog`) |
| Domain | impostos, retencoes e encargos no item (`TaxLine`, `ItemCharge`); `ReformTaxes` opcional |
| Infrastructure | `RecordIgnoredAsync`; status e texto, sem migration |
| Adapter D365 | `D365GoodsInvoiceSource`, montador, JSON canonico, cache de cadastros, cliente OData compartilhado com o feed, Locator com RecId, carimbo e horizonte estavel no feed |
| Messaging | consumidor e dead-letter da `documents-discovered`, na mesma casca da fila de entrada, parametrizada por fila |
| Host / dashboard | wiring do source e do roteador; status "Ignorado" fora do filtro de falhas |
| Testes | fixtures gravadas do fiscosysdev (`tools/d365-fixtures`, byte a byte) e o projeto `FiscalHub.Integration.Tests`, ponta a ponta |

## As decisoes que moldaram o desenho

**A origem viaja na referencia.** O perfil do tenant-a diz `Dynamics365`, e o mesmo tenant roda o fluxo XML. Resolver o source so pelo perfil mandaria o XML para o adapter do D365. `DocumentReference.Origin` e opcional, no desenho do `Trigger`: sem o campo, vale o perfil, e as mensagens antigas continuam validas. O perfil diz o que varrer; a referencia diz de onde veio.

**A entidade fiscal e a fonte.** Sinal, CST, base, aliquota e valor vem da `FSFiscalDocumentTaxTransBR`, sem regra de sinal nem de CST no adapter. O complemento contabil (pela ponte `TaxTransRecId`) vale so para o `ImportTax`, campo a campo. Divergencia fora do padrao conhecido para o documento, em vez de escolher um lado.

**Hash sobre um JSON canonico.** O D365 nao tem um "cru" unico. A impressao e o SHA-256 das respostas em ordem fixa, com colecoes pelo RecId e sem `@odata.*` nem `SysModifiedDateTime`, para um toque de auditoria nao reenviar a nota. O canonico e tambem a foto da fonte.

**Supressao da republicacao da sobreposicao.** Sem ela, cada nota era redescoberta ~6 vezes: ~27 GETs por nota, teto de ~45 notas/min pelo limite do F&O e ~8 notas/min pelo consumidor serial. O hash nao evita isso, porque a busca vem antes da idempotencia. O poller descarta o par (documento, carimbo) ja publicado. So entra no registro o par assentado, porque o `SysModifiedDateTime` tem resolucao de segundo e duas gravacoes no mesmo segundo teriam o mesmo carimbo. Com isso, ~1,15 publicacao por alteracao. Revisa uma alternativa que o ADR-0024 tinha rejeitado, cuja premissa nao se sustentou.

**So nota autorizada; cabecalho vazio e falha.** A esteira so conhece "emitida", e montar uma cancelada seria manda-la como emitida. Vazio vai para a dead-letter, e nao para "ignorado", porque com `cross-company=true` a perda de acesso a empresa tambem devolve vazio.

**IBS/CBS ausente nao vira zero.** Nota sem o grupo sai sem ele, e o validador a rejeita com motivo proprio. Zerar inventaria aliquota e valor na foto do dominio.

## Validacao

Suite: **347 aprovados, 2 pulados (os opt-in), 0 avisos.** `openspec validate --strict` ok.

Teste de integracao opt-in contra o fiscosysdev: as 5 NF-e 55 da `brmf` montadas duas vezes cada, com a mesma impressao nas duas.

Teste manual (host + emulador, roteiro em `docs/RUNNING.md` secao 7; evidencia tambem no cabecalho do ADR-0025):

- primeira passada: 14 referencias na fila de descoberta, 0 suprimidas, 69 avisos de modelo `01`;
- **5 NF-e 55 montadas uma vez cada e rejeitadas** ("tributos da Reforma (IBS/CBS) ausentes"; uma tambem pela chave de acesso vazia), **9 NFS-e ignoradas, nada enviado** ao mock;
- **4 GETs por montagem** (cabecalho, linhas, impostos, encargos), **mais 1 da contabil so na nota de importacao**. Cadastros: 5 enderecos para 10 consultas e 2 cidades;
- a impressao gravada e o SHA-256 da foto `source.json` no Blob;
- **segunda passada, um minuto depois: 0 referencias.**

## Achados

- **Os 26 impostos zerados na fiscal vao alem de `ImportTax` e IPI 05**: tambem PIS/COFINS 98/99 e ICMS 90. **So o `ImportTax` e completado**; os outros seguem zerados, como a fiscal os apresenta.
- **O `ImportTax` zerado guarda a base em `TaxBaseAmountOther`**, e o complemento preenche `TaxBase` pela contabil: o documento fica com **4.500 nos dois campos**. Nao e divergencia pela regra, mas e **questao aberta para o fiscal** antes de o payload da Avalara ler o II.
- **A supressao de republicacao nao apareceu ao vivo.** Com a base parada, depois da primeira passada nada fica na janela de sobreposicao. Ela e **coberta por teste, mas sem prova em campo**.
- **A base nao tem nota 55 cancelada nem retencao em nota 55.** Esses testes usam dado derivado de uma resposta gravada (ou linhas reais de notas `01`/`SE`), e **cada teste diz isso**.
- O casamento fiscal x contabil fecha 1:1 nos dois sentidos (547/547, zero linha contabil sem par fiscal).

## Pontos de atencao

**Nenhuma nota do D365 chega a Avalara nesta fatia.** As notas 55 da base sao de 2016, sem IBS/CBS. E mesmo uma nota com IBS/CBS seria rejeitada por "cClassTrib ausente": a entidade fiscal nao traz o campo, e na contabil ele e um RecId sem entidade que o resolva.

**Os commits sao fatias logicas, em ordem de dependencia.** O branch inteiro esta verde, mas nao ha garantia de que cada commit compile sozinho.

**`client_credentials` continua sem rodar.** Todo teste real usou a identidade delegada do Azure CLI.

O archive da change vem depois do merge.

## Proximas fatias (registradas no `docs/STATUS.md`)

Nota de servico (CCM, municipio de prestacao, ISS, item da lista), que e a seguinte; entidade de `CClassTribTable_BR` no pacote D365; despacho de cancelamento; impostos e encargos no parser do XML; dead-letter imediata com motivo; concorrencia do consumidor com serializacao por documento.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-55"></a>

## PR #55 — docs(openspec): arquiva add-d365-document-assembly e sincroniza as specs

**Estado:** merged · **Aberto:** 2026-09-27 · **Mesclado:** 2026-09-27 · **Branch:** `docs/archive-d365-document-assembly` → `main` · **Commit de merge:** `fe3a278` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/55)

Arquiva a change `add-d365-document-assembly`, que foi mergeada no #54.

- Move a change para `openspec/changes/archive/2026-09-26-add-d365-document-assembly`.
- Sincroniza os deltas em `openspec/specs`:
  - **Criadas:** `d365-document-assembly`, `discovery-queue-consumer` e `inbound-source-resolution`.
  - **Atualizadas:** `change-feed-polling` e `d365-change-feed`.

Só documentação de specs. Não há código.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-56"></a>

## PR #56 — feat: conector, nao validador - contrato pelos JSONs reais da Avalara

**Estado:** merged · **Aberto:** 2026-09-27 · **Mesclado:** 2026-09-27 · **Branch:** `feat/connector-not-validator` → `main` · **Commit de merge:** `cd2ce8c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/56)

Implementa a change `connector-not-validator` (SDD). O hub deixa de julgar conteudo fiscal: rejeita so o que impede a requisicao de existir, e o resto vai para a plataforma de compliance, cuja resposta aparece no dashboard com o motivo dela. Na mesma fatia, o contrato da Avalara passa a vir dos JSONs reais de integracao, e nao do mock. Decisoes no ADR-0026, que revisa o §6 do ADR-0025 e refina o ADR-0003.

## Por que

Julgar conteudo fiscal punha o hub como segunda fonte de verdade fiscal. Teriamos que perseguir as regras da plataforma para sempre, e cada divergencia entre o nosso julgamento e o dela virava nota que o cliente jurava ter mandado e que nao chegou. Quem aceita ou rejeita conteudo fiscal e a plataforma; o papel do hub e entregar e mostrar a resposta dela.

## O que entra

| camada | o que |
| --- | --- |
| Application | validador so com "a nota nao possui itens"; `DispatchRejectedException` e a esteira registrando a recusa permanente no envio sem retentativa; omissoes no `IntegrationReceipt` |
| Domain | emissao propria ou de terceiros (IND_EMIT), data de entrada/saida, valor das mercadorias, endereco da parte, unidade, valor contabil e origem do item; tudo opcional |
| Infrastructure | a omissao vira observacao no `Reason`, preservada na confirmacao e posta depois do motivo numa recusa; seed com a traducao dos estabelecimentos |
| Adapter Avalara | contrato pelos JSONs reais: codigos da empresa pela tabela `establishments` por ambiente, parceiro como contraparte, classicos no bloco `imposto`, array `impostos` so para IBS/CBS, campo sem fonte omitido e nunca zerado; recusa sincrona (400/422) e motivo na consulta de status |
| Adapter D365 | `$select` ampliado, canonico v2, fixtures regravadas do fiscosysdev |
| Adapter XML | nota sem `IBSCBS` lida com o grupo ausente |
| Mock / dashboard | `rejeitar` e motivo no `erro`; observacao de documento que nao falhou aparece como aviso |
| Testes | 425 verdes, 0 warnings; ponta a ponta com o dispatcher real contra o mock em memoria |

## Validacao

Teste manual pelo roteiro do `docs/RUNNING.md` §7 (host, emulador do Service Bus, mock e poll do tenant-a a partir de 2015, contra o fiscosysdev):

- **5 NF-e 55 `Confirmed` e 9 NFS-e `Ignored`** contra o mock.
- **Duas notas com omissao registrada apos a confirmacao**, com o `SqlProcessingStore` real:
  - `BRMF06-110000027`: "Enviado sem: item 1: IcmsDiff nao enviado (sem lugar no contrato)";
  - `BRMF06-110000031`: o encargo de 416,25.
- **Payload conferido no mock:**
  - a nota de entrada vai sem `chaveNFe` e com o fornecedor como parceiro;
  - ICMS com `TabA`, `TabB` e base em "outras";
  - II em `imposto.ii` (4.500 / 30 / 1.350);
  - sem array `impostos` e sem `null`.
- **Mock em `erro` e em `rejeitar`:** `IntegrationError` com o motivo da plataforma, sem `ExternalId` e sem dead-letter. Na recusa, a omissao vem depois do motivo, separada por ` | `.
- **Teste opt-in contra o fiscosysdev:** as 5 NF-e 55 montam duas vezes com a mesma impressao (v2).

## Ressalvas

Sao o limite do que foi provado:

- **O envio e contra o mock, que aceita tudo.** "5 enviadas" prova o caminho e o contrato montado, e nao a aceitacao da Avalara. A ligacao com a Avalara real e a proxima fatia (design D18): credenciais por tenant, `BaseUrl` do sandbox e token.
- **O ponta a ponta usa store falso,** para nao levar a Infrastructure ao projeto de integracao. A preservacao da observacao na confirmacao e provada pelos testes do `SqlProcessingStore` e pelo teste **manual**. Esse passo do RUNNING.md nao e opcional, e tem que ser reexecutado quando o store mudar.
- **A traducao da origem cobre so `National` -> 0 e `DirectImport` -> 1,** os dois valores que a base tem. Os demais ficam ausentes, e nao zerados. E item aberto no checklist do primeiro cliente.

## Checklist do primeiro cliente

O `docs/STATUS.md` passa a ter o **checklist do primeiro cliente**: o registro de risco, num lugar so, de tudo o que a base de demonstracao nao exercita. Cada item diz o que falta provar, como se prova e o sintoma se ninguem provar. Um item so fecha com evidencia; teste com fixture derivada prova o codigo, e nao o dado, entao nao fecha.

## Configuracao (quebra)

Sem `establishments` nas `OutboundSettings` do ambiente ativo, todo envio do tenant e recusado, com motivo que diz o que falta. O seed de banco novo ja traz o campo; banco de dev existente precisa do passo novo da secao 3 do RUNNING.md. O archive da change vem depois do merge.

---

<a id="pr-57"></a>

## PR #57 — docs(openspec): arquiva connector-not-validator e sincroniza as specs

**Estado:** merged · **Aberto:** 2026-09-27 · **Mesclado:** 2026-09-27 · **Branch:** `docs/archive-connector-not-validator` → `main` · **Commit de merge:** `21a3b6a` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/57)

Arquiva a change `connector-not-validator`, que foi mergeada no #56.

- Move a change para `openspec/changes/archive/2026-09-27-connector-not-validator`.
- Sincroniza os deltas em `openspec/specs`:
  - **Criadas:** `avalara-document-contract`, `compliance-dispatch-outcome` e `integration-validation`.
  - **Atualizada:** `d365-document-assembly`.

Só documentação de specs. Não há código.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-58"></a>

## PR #58 — feat(tenant): o tenant vem de quem esta logado, nunca da requisicao (D18, parte 1)

**Estado:** merged · **Aberto:** 2026-09-27 · **Mesclado:** 2026-09-27 · **Branch:** `feat/connect-avalara-sandbox` → `main` · **Commit de merge:** `2bba539` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/58)

Parte 1 da change `connect-avalara-sandbox`: o limite de tenant (ADR-0028). Vem antes da parte Avalara e é mergeada sozinha (design D19). São correções de segurança que valem independente da plataforma, e não devem ficar presas atrás do portão contra o sandbox.

## O que muda

A varredura de todos os endpoints que recebem tenant pelo corpo, pela rota ou pela query achou quatro furos:

| Furo | Classe | Correção |
|---|---|---|
| `/trace` e download leem o Blob pelo tenant da rota | vazamento | `DocumentTraceQuery` sobre o `INoteTraceReader` do suporte. Outro tenant dá o mesmo 404 de documento inexistente, sem ler o Blob dele |
| `/ingest` aceita o `TenantId` do corpo; `/drop` grava fixo no tenant-a | injeção | `ManualIngestion` com o tenant do login; `/drop` no prefixo do tenant do login |
| locator do `/ingest` livre, em qualquer container, inclusive `traces/` | leitura alheia pela esteira | `IInboundSource.CheckLocator`. O XML só vale em `nfe/{tenant}/…`, sem `..`, e `traces` nunca é origem (regra própria). A busca confere de novo antes de ler |
| `deactivate` busca o agendamento só pelo id | interferência | filtra pelo tenant, e dá 404 fora dele |

Junto:

- o drop não tem mais tenant padrão: um arquivo fora de `{tenant}/{chave}.xml` fica no drop, com aviso;
- os campos `TenantId` mortos saem dos corpos da integração manual e dos agendamentos;
- o seed e o catálogo da descoberta passam a `nfe/tenant-a/…`.

Nenhum fluxo legítimo age sobre outro tenant. Os caminhos de produção (drop, feed, fila, descoberta, agendador) foram conferidos. As pré-condições para abrir o drop e a fila a cliente estão no ADR-0028 e no STATUS.md.

**BREAKING (dev):** o `/ingest` do RUNNING §4 passa a `nfe/tenant-a/nfe-exemplo.xml`, sem `tenantId` e com o token do login.

## Docs

- ADR-0028, com as notas de revisão no 0018 (a ressalva dos endpoints de debug) e no 0009 (o tenant padrão do drop). O 0027 fica reservado para a parte 2.
- `RUNNING.md` §4 e `STATUS.md` (checklist do primeiro cliente).
- A pasta da change `connect-avalara-sandbox`, que segue ativa. A parte 2 começa pelo portão (grupo 5).

## Testes

- `dotnet build`: 0 warnings. `dotnet test`: 457 passando (eram 427) e 2 pulados, os opt-in do D365. `npm run build` do dashboard: verde.
- Novos:
  - `TraceArchive` e `DocumentTraceQuery` (com outro tenant, o leitor nunca é chamado);
  - `ManualIngestion`;
  - `XmlLocator`: `traces` do próprio tenant e de outro, prefixo alheio, `..`, `.`, `\`, arquivo vazio;
  - a busca recusando sem ler o blob, e o `CheckLocator` do D365;
  - `DropBlobNaming` sem tenant padrão, e o `deactivate` de outro tenant.
- Conferência manual com host e mock locais, como tenant-a e tenant-b, registrada no fim do ADR-0028. Deixou três linhas `d18-probe-*` no banco de dev.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-59"></a>

## PR #59 — feat(avalara): credencial por tenant, segredo pela tela no cofre e a quarta foto (D18, parte 2)

**Estado:** merged · **Aberto:** 2026-09-27 · **Mesclado:** 2026-09-27 · **Branch:** `feat/connect-avalara-sandbox-parte-2` → `main` · **Commit de merge:** `b6bc9e1` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/59)

Parte 2 da change `connect-avalara-sandbox` (ADR-0027): a Avalara de verdade. Cada envio e cada consulta levam o token da credencial do próprio tenant, no ambiente ativo; o segredo entra pela tela e vai para o cofre; e a resposta da plataforma é a quarta foto. O ponta a ponta rodou contra o sandbox real em 2026-09-27.

## O que muda

**Autenticação real por padrão.** O `NoOpAvalaraTokenProvider` deixa de ser o padrão; "sem autenticação" só por `UseAvalaraWithoutAuthentication()`, que o host nunca chama.

- **Credencial e URLs pela seção do ambiente** (`baseUrl`, `tokenUrl` opcional, `clientId`, `clientSecretRef`), sem fallback global e com `https` (http só em loopback). A `baseUrl` é guardada com barra final. `AvalaraOptions.BaseUrl/ClientId/ClientSecret` e `Avalara:BaseUrl` saem.
- **Token por credencial:** a chave do cache é tenant, ambiente, endpoint, cliente e SHA-256 do segredo; uma busca por chave, margem de renovação, `Invalidate` no 401 do cache. O pedido é `client_credentials` com corpo JSON (a forma da coleção do cliente, verificada no sandbox).
- **Recusa lembrada:** a recusa da credencial fica guardada por 5 min, para não martelar o login; salvar o perfil na tela a esquece na hora.

**O segredo pela tela, e o cofre.**

- O `PUT /connector` recebe o Client Secret como campo de escrita, grava no cofre e persiste só `kv:fh-{tenant}--…`, o nome que o servidor deriva. Um `*Ref` vindo da requisição é recusado. O `GET` devolve as settings sem referências e "configurado em <data>", nunca o valor.
- `ISecretStore` (Application) e um único adapter, o `KeyVaultSecretStore`, em dev e em produção. Em dev, o Lowkey Vault em memória no `docker compose`, sem volume. A verificação do desafio do SDK só é desligada em loopback, tirada da URI.
- O adapter recusa na leitura o valor cru persistido e a referência fora do prefixo do tenant.

**Desfecho da autenticação e do envio.**

| Resposta | Antes | Agora |
|---|---|---|
| Token: 400/401 | retry até a dead-letter | rejeição "a plataforma recusou a credencial…", nomeando Client ID **e** Client Secret (o texto do sandbox não aponta o campo certo) |
| Envio: 403, ou 401 com token recém-emitido | retry | rejeição de configuração com o motivo da plataforma |
| Envio: 401 com token do cache | retry | `Invalidate` e retry |
| Envio: 404 | retry até a dead-letter | rejeição "o caminho de envio não existe nessa URL", apontando a `baseUrl` do perfil e o `Avalara:DocumentsPath` |
| Envio: 2xx sem identificador | exceção, retry | rejeição "pode ter sido aceito", sem reenvio |
| Consulta: 404 | pendente | pendente (sem mudança) |

**A quarta foto.** `{destino}.response.submit.json` e `{destino}.response.status.json`, no `/trace`, no zip e na nova aba "Resposta" do dashboard. É um envelope com status, URL sem query, cabeçalhos de uma lista fechada e o corpo redigido (`SensitiveText`: por valor, por `Bearer` e por nome). A gravação é por melhor esforço, porque vem depois do envio. A troca de token nunca é fotografada, e onde é ecoada (motivo da recusa, sonda) tem regra própria: credenciais `[redigido]`, sessão, conta e login `[mascarado]`.

**Ferramentas.** O mock imita o que o sandbox mostrou: token com corpo JSON, o caminho `taxcompliance/v2/fiscal/dfe`, a recusa 400 `{"error": …}`. A sonda `tools/AvalaraSandboxProbe` (`token`, `send` com `--omit`/`--set`, `get`) grava em `out/` só envelope redigido, fora do Git.

**Achados no caminho.**

- O `PUT /connector` apagava a configuração de chamados.
- A tela de conectores apagava `establishments`, `companies` e `poll` ao salvar.
- O coletor ficava desligado em silêncio sem a seção `poll`; agora há aviso de hora em hora.
- Em dev, o D365 sempre usava o `az login`; agora usa a credencial do tenant quando o perfil tem uma, e loga qual identidade autenticou.
- O seed de demonstração passou a opt-in (`Seed:DemoData`, padrão `false`).

## Verificado contra o sandbox (2026-09-27)

- Autenticação `client_credentials` com corpo JSON, em `api-gateway.sandbox.avalarabrasil.com.br/oauth/token`: `access_token`, `token_type` `bearer`, `expires_in` ~86400 s, sem escopo nem audiência. A recusa é 400 `{"error": "<texto livre>"}`.
- O ponta a ponta:
  - as 14 referências do `fiscosysdev` foram descobertas;
  - as 9 NFS-e viraram "ignorado" sem chamada ao F&O;
  - as 5 NF-e 55 foram montadas e enviadas em `taxcompliance/v2/fiscal/dfe`, com a tradução do estabelecimento pelos `establishments` do perfil.
- A Avalara respondeu com **recusa de validação**, como o design previa. Os seis campos exigidos (`operacao`, `tipoPagamento`, `parceiro.Codigo`, `itens[].Item.TipoItem` e as duas `UnidadeMedida.Descricao`) são a entrada da próxima fatia.

**Segue sem verificar:** o caminho de consulta de status. Sem nota aceita, não houve `id`.

## BREAKING (dev)

- `docker compose up -d` sobe o `keyvault`. O Client Secret é digitado na tela, até contra o mock, e de novo a cada reinício do emulador.
- As referências antigas do banco de dev (`kv:avalara-a-…`, `kv:d365-a-secret`) são recusadas como fora do prefixo do tenant: grave o segredo pela tela, ou use o SQL do RUNNING §3.
- O `appsettings.Development.json` ganha `Avalara:DocumentsPath` (o caminho do sandbox) e `Seed:DemoData: false`. A demonstração não é mais semeada por padrão (RUNNING §10).

## Docs

- ADR-0027, com as notas de revisão no 0026 (§2), no 0006 (quatro fotos) e no 0019 (o segredo pela tela).
- `RUNNING.md`: o cofre, o Client Secret pela tela, a recusa da credencial no mock, o roteiro do sandbox (§8) com a guarda de antes e depois de salvar, a sonda (§9) e a limpeza da base de demonstração (§10).
- `STATUS.md`:
  - a sessão;
  - no checklist do primeiro cliente, o provisionamento do cofre (papel sob medida, ABAC `fh-`, cofre dedicado) e três lacunas conhecidas: o interruptor de tempo real decorativo, o seed de dev sem guarda de ambiente (**risco de primeiro cliente**) e o `CompanyCode` pelo emitente;
  - a entrada da próxima fatia.

## Testes

- `dotnet build`: 0 warnings. `dotnet test`: 687 passando (eram 457 no #58) e 3 pulados, os opt-in (2 do D365, 1 do emulador do cofre). `npm run build` do dashboard: verde.
- Cobertura nova, entre outros:
  - o `ConnectorProfileService` (gravação e leitura mascarada);
  - o `KeyVaultSecretStore` sobre um `SecretClient` escrito à mão;
  - o provider de token (cache, concorrência, recusa lembrada, `Forget`, corpo JSON);
  - a classificação do dispatcher;
  - `SensitiveText` e `TokenExchangeRedaction`;
  - o ponta a ponta contra o mock autenticado;
  - o seed;
  - a reprodução da recusa real do sandbox (a fixture `Fixtures/sandbox/recusa-no-envio.json`), no dispatcher e no ponta a ponta, e a varredura das fixtures.

## Antes de arquivar a change

O teste manual está registrado em `docs/avalara-sandbox-primeiro-envio.md`. Das 101 tarefas, 92 estão feitas; as 9 abertas
foram decididas, uma a uma, e ficam abertas no arquivamento de propósito.

**Verificado contra o sandbox (2026-09-27):**

- a autenticação (15.3): `client_credentials` com corpo JSON, a resposta de token e a forma da recusa;
- o envio (16.1): as 5 NF-e 55 em `taxcompliance/v2/fiscal/dfe`, recusadas por validação; e o roteamento das 9 NFS-e sem
  tocar no F&O;
- a seção `sandbox` do perfil (15.1): `baseUrl`, `tokenUrl`, `clientId` e o `clientSecretRef` `fh-tenant-a--…`;
- a evidência (18.1 a 18.4, 18.6): a recusa real como fixture, os testes de reprodução e o relatório, com os seis motivos
  classificados como "nosso: contrato ou mapeamento".

**Parciais (o que foi verificado está anotado na tarefa):**

- 15.2: a linha do perfil no SQL tem só o `clientSecretRef`, sem valor nem parte dele;
- 16.2: as 5 NF-e têm motivo legível gravado; as abas "Resposta" e "Destino" na tela não foram conferidas;
- 18.5: o checklist atualizado só pelo que a evidência mostrou. "Formato real do erro" ficou com a recusa síncrona provada e
  a consulta com erro em aberto; "Blocos", "Totais" e "Reenvio", "não respondido".

**Não exercitado:**

- 16.4: os logs do host. Ele rodou numa console interativa, sem arquivo de log, e a saída não pôde ser varrida (o log em
  arquivo no dev está registrado para a próxima fatia);
- o aceite, a consulta de status e a recusa de credencial pelo hub: não houve, e não viraram fixture.

**Movido para a próxima fatia** (registrado no `docs/STATUS.md`):

- o resto da 15.2: o `PUT` à mão com `clientSecretRef` dando 400, e o reinício do emulador levando a "não configurado";
- 15.4: a correção pela tela com o segredo errado de propósito;
- 16.3: os zips e as cinco fotos de cada nota;
- 17: o experimento do campo omitido, porque o `finalidadeNotaFiscal` não apareceu na recusa e ele fica mais informativo
  depois que uma nota for aceita;
- do 18.5, o item "Campo omitido virando 0", que depende do experimento.

A change é arquivada depois do merge, com essas 9 tarefas abertas como estão.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-60"></a>

## PR #60 — docs(openspec): arquiva connect-avalara-sandbox e sincroniza as specs

**Estado:** merged · **Aberto:** 2026-09-28 · **Mesclado:** 2026-09-28 · **Branch:** `docs/archive-connect-avalara-sandbox` → `main` · **Commit de merge:** `1013af2` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/60)

Move a change `connect-avalara-sandbox` para `openspec/changes/archive/2026-09-27-connect-avalara-sandbox` e leva os deltas para `openspec/specs`:

- cria `avalara-tenant-authentication`, `connector-secret-references`, `platform-response-trace` e `tenant-boundary`;
- atualiza `compliance-dispatch-outcome`.

No `docs/STATUS.md`, registra a lacuna do rebobinamento que não reprocessa com o processo de pé, e põe o registro de publicação persistido na fatia de nuvem.

Só documentação e specs; nenhum código muda.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-61"></a>

## PR #61 — feat: integracao automatica pela tela, pelo poll.enabled, e o rebobinamento que reprocessa

**Estado:** merged · **Aberto:** 2026-09-28 · **Mesclado:** 2026-09-28 · **Branch:** `feat/automatic-integration-switch` → `main` · **Commit de merge:** `7fa1ca6` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/61)

Change `automatic-integration-switch` (ADR-0029): o interruptor da tela de conectores passa a ligar e desligar o coletor do tenant, e o rebobinamento pelo cursor apagado volta a reprocessar com o processo de pé.

## O que muda

**O interruptor controla o coletor.** "Integração em tempo real" vira "Integração automática" e grava o `poll.enabled` das `InboundSettings`, e em nenhum outro lugar.

- **Ligar e desligar:** ligar preserva o resto da seção (`intervalSeconds`, `overlapSeconds`, `startFrom`). Desligar grava `false` e mantém a seção, para o poller não avisar que o poll "não está configurado".
- **Pausa, e não reset:** desligar não mexe no cursor, e religar retoma da marca.
- **Onde aparece:** só para adapter de entrada que varre. Hoje é o `Dynamics365`.

**O `Realtime` sai.**

- **Onde:** do perfil, dos contratos do `/connector`, do seed e da tabela (migração `RemoveConnectorProfileRealtime`).
- **O `/info`:** devolve `automaticIntegration`, derivado do adapter que varre (origens dos `IDocumentChangeFeed` registrados) e do `poll.enabled`. Settings ilegíveis contam como desligada.
- **O selo:** passa a "Integração automática ligada", e não afirma nada enquanto o `/info` carrega.

**A guarda da seção `poll` no `PUT /connector`.** A gravação julga só o que ela escreve, pelo mesmo `Parse` do poller.

- **O valor novo que o coletor não leria:** dá 400, com o campo, o que veio e a forma aceita.
- **O inválido já gravado:** se volta igual, passa. A tela devolve a seção inteira e não pode ficar trancada por um campo que não edita (design D8).

**O rebobinamento.** Um teste de reprodução confirmou a hipótese do design D5.

- **O que escapava:** o `DELETE` do cursor no meio da primeira passada, antes do primeiro avanço. A última marca vista ficava igual ao `startFrom`, e a passada seguinte suprimia a primeira página.
- **O `DELETE` entre passadas:** já funcionava.
- **A correção:** sob o lease, o cursor ausente ou sem marca faz o poller esquecer o registro de publicação do tenant antes da leitura.

## BREAKING

- **`/info`:** troca `realtime` por `automaticIntegration`.
- **`GET /connector`:** perde o `realtime`.
- **`PUT /connector`:** o `realtime`, se vier de um cliente antigo, é ignorado.

Não há cliente em produção, e front e back sobem juntos.

## Verificado

- **`dotnet test` na ponta:** 221 testes na Application e as demais suítes verdes.
- **Commits intermediários:** o C1 e o C2 foram compilados e testados sozinhos, em worktree: 0 warnings e os 10 assemblies verdes.
- **Dashboard:** `npm run build` verde.
- **Prova manual pela tela (2026-09-27), registrada nas tarefas 6.x e no STATUS com as linhas do `host.log`:**
  - **Ligar:** sustentado. A passada seguinte deu 14 referências na fila, e as 5 NF-e 55 foram recusadas pelo sandbox com HTTP 400.
  - **Cursor apagado com o host de pé:** sustentado. O cursor foi recriado, e a passada deu 14 referências e 0 suprimidas, sem reiniciar.
  - **Desligar pela tela:** não sustentado. O silêncio do desligado aparece no log, mas o desligamento não veio pela tela.
  - **Religar da marca:** não exercitado.
  - **O selo e a guarda do `GET /connector`:** não aparecem no log.

## O que fica aberto

- **Tarefas:** 6.1 e 6.2 (só o que é visual), 6.3, 6.4 e 6.5. O item do interruptor no STATUS fica aberto até desligar pela tela e religar com o cursor de pé. O item do rebobinamento fechou.
- **Fora de escopo:**
  - um botão de "reprocessar" na tela (a redescoberta reenviou as 5 NF-e ao sandbox, o que confirma que é decisão de produto);
  - a tela das demais opções do coletor;
  - o registro de publicação persistido;
  - o rótulo "Tempo real" da lista de grupos, que é outro conceito e ganhou item próprio no STATUS.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-62"></a>

## PR #62 — docs(openspec): arquiva automatic-integration-switch e sincroniza as specs

**Estado:** merged · **Aberto:** 2026-09-28 · **Mesclado:** 2026-09-28 · **Branch:** `docs/archive-automatic-integration-switch` → `main` · **Commit de merge:** `c29dbff` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/62)

Move a change `automatic-integration-switch` para `openspec/changes/archive/2026-09-27-automatic-integration-switch` e leva os deltas para `openspec/specs`:

- cria `automatic-integration` (4 requisitos: o interruptor grava no `poll.enabled`, só aparece para adapter que varre, o estado mostrado deriva do que o coletor fará, e o efeito vale na passada seguinte);
- atualiza `change-feed-polling`: "Seleção dos tenants a consultar", "Marca d'água persistida por tenant e origem" e "Supressão de par já publicado". Os outros 7 requisitos ficam intactos.

**Prova manual, que fecha as 37 tarefas.** Ela está nas tarefas 6.x da change, com as linhas do `host.log`, que fica fora do git.

- **Pelo log:** o ligar, o desligar e o religar pela tela, este retomando da marca, sem recriar o cursor e sem reenfileirar o histórico.
- **Pelo banco:** o `poll.enabled` e o avanço da marca.
- **O selo e o `GET /connector`:** conferência visual do usuário, sem linha de log.

No `docs/STATUS.md`, o item do interruptor fecha.

Só documentação e specs; nenhum código muda. `openspec validate --specs --strict`: 13 de 13.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-63"></a>

## PR #63 — feat: empresa, filial e dia do 365, o motivo legivel e as fotos cruas so para Admin

**Estado:** merged · **Aberto:** 2026-09-29 · **Mesclado:** 2026-09-29 · **Branch:** `feat/establishment-and-readable-dashboard` → `main` · **Commit de merge:** `84d3c33` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/63)

Change `establishment-and-readable-dashboard` (ADR-0030): a tela mostra o dado certo, na língua de quem usa. A fatia nasce do primeiro envio real ao sandbox da Avalara, em que o que apareceu na tela não era apresentável nem acionável.

## O que muda

**Empresa, filial e dia vêm do 365.**

- **Empresa e filial:** saem do estabelecimento próprio, e não mais do emitente:
  - a empresa é o CNPJ completo (`FiscalEstablishmentCNPJCPF`, 14 dígitos);
  - a filial é o código do estabelecimento (`FiscalEstablishment`, como `Matriz` e `SP-01`).

  Numa nota de entrada de terceiro, o grupo deixa de ser o do fornecedor.
- **O dia da nota:** é a data fiscal, no fuso de quem emitiu, sem conversão para UTC nem para Brasília. Vale na nota montada e na ignorada.
- **O caminho de XML:** mantém a derivação pelo emitente.
- **O canônico:** sobe para a v3, porque o `FiscalEstablishment` entrou no `$select` da montagem. O `$select` da descoberta não entra no canônico. Não há tenant em produção, e o hash de transição continua pré-requisito da primeira subida com cliente.

**A nota ignorada tem grupo.** A descoberta lê a data fiscal e o estabelecimento, e a referência leva o grupo (`DocumentReference.Metadata`).

- **Onde entra:** a ignorada e a da dead-letter entram na tabela e nos cards da data fiscal delas.
- **Quem prevalece:** o grupo da montagem, que nunca é trocado pelo da descoberta.
- **O critério dos cards:** a data fiscal com recorte do dia. Fica escrito como comportamento correto: as notas de 2016 do fiscosysdev ficam fora.

**O motivo da recusa se lê.**

- **O motivo gravado:** vira resumo ("6 campos com erro: operacao, tipoPagamento, parceiro.Codigo e mais 3"), sem o `title` do ProblemDetails e sem a omissão. O corte em 1000 caracteres deixa de pesar.
- **A lista inteira:** vem de `GET /documents/{tenant}/{chave}/reading`, aberta a qualquer papel. Ela tira das fotos só a lista de campos e as omissões.
- **A omissão:** vira ressalva de nota aceita ("Enviado com ressalvas") e fica gravada na foto do envio (`request.omissions`).
- **A foto:** perde o `type`, o `title` e o `status` repetido, e guarda a URL e o `traceId`. A sonda do sandbox continua crua.
- **O mock:** recusa no formato verificado no sandbox.

**As fotos cruas são só para Admin, de fato.**

- **No servidor:** o `/trace` e o zip exigem o papel (`rawTraceRoles`) e dão 403 aos demais. O gancho para um papel de Suporte fica pronto, sem criá-lo.
- **Na tela:** o "Visualizar JSON" abre um modal próprio, com uma aba por foto. Ele e o "Baixar arquivos" só aparecem para Admin. O `Modal` fecha no Esc só o de cima.

**O resto da tela:**

- o selo da barra lateral mostra o estado: verde ligado, vermelho desligado, e some em adapter que não varre (`/info` com `inboundScans`);
- o agendamento ganha exclusão (`DELETE /schedules/{id}`), e as execuções ficam no histórico;
- "Tempo real" vira "Automática", no rótulo e no valor gravado (`RealTime` → `Automatic`);
- o CNPJ aparece com máscara.

**Migrações:** `WidenBranchCode` (o `BranchCode` vai a 20) e `RenameRealTimeTrigger` (só dado).

## BREAKING

- **O modo do `DocumentGroup.trigger`:** passa de `RealTime` a `Automatic`. A migração regrava o que já está no banco.
- **O `/info`:** ganha o `inboundScans`.
- **O `/trace` e o zip:** dão 403 para quem não é Admin. A primeira vista passa a usar o `/reading`.
- **O `Reason` da recusa e a foto da resposta:** mudam de forma.

Não há cliente em produção, e front e back sobem juntos.

## Também neste PR

Estas duas mudanças estavam na árvore e entram em commits próprios. São de outros assuntos:

- **`fix(dev)`:** o `scripts/up.ps1`, que limpa as bases órfãs do emulador do Service Bus antes de subi-lo, e a seção 1 do RUNNING.
- **`feat(d365)`:** os campos do `cClassTrib` na `FSFiscalDocumentTaxTransBR`. O hub ainda não os lê.

## Verificado

- **`dotnet test` na ponta:** as suítes verdes, com 246 testes na Application, 146 no D365, 209 na Avalara, 105 na Infrastructure e 18 de integração.
- **Build:** 0 warnings.
- **Commits intermediários:** os cinco commits de código do meio foram compilados e testados sozinhos, em worktree, com 0 warnings.
  - **A ressalva:** os testes do D365 só passam num caminho curto, por uma quebra antiga das fixtures com caminho longo, registrada no STATUS.
- **Dashboard:** `npm run build` verde.
- **Prova manual (2026-09-29).** A evidência está nas tarefas 9.x e no STATUS, com as linhas do `host-fatia3.log`:
  - **A passada limpa:** 14 referências na fila, com 0 suprimidas.
  - **No banco:**
    - empresa `44278225000180` e `44278225000260`, e filial `Matriz` e `SP-01`;
    - as 9 ignoradas com modelo `SE` e data;
    - as 14 datas iguais ao `FiscalDocumentDate` do F&O;
    - o modo `Automatic` em todas.
  - **Pela API:**
    - o Viewer tem 403 no `/trace` e no zip, e 200 no `/reading`, com 6 campos e a omissão;
    - o Admin tem 200 nos dois;
    - a foto só com `errors` e `traceId`, e as omissões.
  - **A tela:** conferência visual do usuário, sem linha de log.

## O que fica aberto

- **Tarefas, as duas parciais:**
  - a 1.1: o tamanho do código do estabelecimento no AOT, que o OData não publica. Os códigos reais têm até 6 caracteres;
  - a 9.6: o histórico de execução de um agendamento excluído, provado só por teste, porque nenhum agendamento disparou.
- **Novos no STATUS:**
  - os filtros dos cards (período e modelo);
  - o modal do grupo, que não filtra pelo tipo;
  - o diretório com o CNPJ de 14 dígitos;
  - o que o chamado de suporte anexa para quem não vê as fotos cruas;
  - o `startFrom` que some. O cursor nasce em "agora" sem aviso, e o sintoma é igual a "não há nota nova". Junto, as `InboundSettings` do tenant-a vistas em `{}` depois da prova.
- **Próxima fatia:** os seis campos obrigatórios da recusa da Avalara, com a nota aceita como critério de saída.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-64"></a>

## PR #64 — Navegação por módulo, a situação da integração automática e o teste de credencial

**Estado:** merged · **Aberto:** 2026-10-01 · **Mesclado:** 2026-10-01 · **Branch:** `feat/module-navigation-and-integration-panel` → `main` · **Commit de merge:** `84ac9df` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/64)

Change `module-navigation-and-integration-panel` (ADR-0031). O Admin passa a ver e corrigir pela tela o que antes exigia abrir o banco: a situação da integração automática, a busca de novo desde uma data e o teste da credencial. A barra lateral fica organizada por módulo.

## O que muda

**A navegação por módulo.**

- **A barra lateral:** o bloco **Integrações** tem Fiscal, Contábil, Inventário e Agendamento.
- **Os módulos do tenant:** montam o bloco, ficam gravados no perfil (coluna nova `ConnectorProfiles.Modules`) e são marcados em Configurações → Módulos. Sem módulos gravados, vale só o Fiscal, como antes.
- **Contábil e Inventário:** são lugares reservados, com um painel vazio. Não são filtros do Fiscal, e cada um vira uma fatia própria.
- **É apresentação, e não permissão:** a API continua respondendo para um módulo escondido.

**O segredo mascarado.**

- **Na tela:** o segredo configurado mostra 32 bolinhas na cor do texto, ao lado de "configurado em".
- **A segurança:** a máscara é placeholder, e nunca valor. Salvar sem digitar não envia o segredo, e um teste do front prova isso. É a primeira suíte do dashboard, com `vitest`, só de desenvolvimento.
- **No servidor:** um valor feito só de máscara é recusado com 400, sem tocar o cofre.

**A situação da integração automática.**

- **O que mostra:** a última busca, as falhas consecutivas, o último erro e "sincronizado até". Antes da primeira busca, mostra de onde ela vai começar.
- **Quando aparece:** assim que o interruptor é ligado, antes de salvar.
- **A API:** `GET /connector/automatic`, só para Admin.

**Buscar novamente desde uma data.**

- **O que é:** o rebobinamento da marca d'água pela tela (`POST /connector/automatic/rewind`, só Admin).
- **Como grava:** pelo mesmo lease do coletor e pelo mesmo fencing do avanço, com a comparação invertida. O registro de publicações é zerado pela regra que já existia, sem passo novo.
- **Os limites:** só para trás, e só depois da primeira busca.
- **A confirmação:** diz o efeito e o custo em quatro frases: as notas alteradas são relidas, o que foi enviado e não mudou não volta, e cada NF-e custa pelo menos 4 consultas ao ERP.

**O teste da credencial** (`POST /connector/test`, só Admin), para o D365 e a Avalara.

- **O que testa:** a credencial gravada, com um token novo a cada teste.
  - **No D365:** uma credencial nova do Entra ID e uma leitura `$top=1` da `FSFiscalDocumentBRs`. O cache do coletor não muda, e o Azure CLI nunca entra.
  - **Na Avalara:** uma troca sem o cache e sem a recusa lembrada.
- **A tela:** mostra "Credenciais e conexão válidas" ou "Credenciais ou ambiente inválidos", e o detalhe (AADSTS, status HTTP) vai para o log do host.
  - **A classificação:** só uma causa de rede conta como "fora do ar". A credencial errada e o endereço errado são inválidos.
- **O freio:** fica no botão. São 5 minutos depois de uma recusa, por tenant e adapter, e salvar o desfaz. Por que as proteções são diferentes dentro dos adapters e iguais no botão está no ADR-0031.

**A URL do token** sai da tela da Avalara. O hub a monta pela URL base, e um `tokenUrl` já gravado continua valendo.

**Migração:** `AddConnectorProfileModules`, uma coluna anulável, sem preencher nada.

## BREAKING

- **O `/info`:** ganha `modules`.
- **O `/connector`:** ganha `modules`, na leitura e na gravação. A gravação sem o campo mantém os módulos gravados.

Front e back sobem juntos, e não há cliente em produção.

## Fora deste PR

O trabalho do D365 de contábil e inventário está na árvore e não entra aqui. São as 6 entidades com privilégios e role, o `FSItemBR`, o `04`, os guias `06` e `07`, o Postman e o resto do `d365/README.md`, mais o `*.log` no `.gitignore`. Do `d365/README.md`, só entrou o parágrafo de como conferir a permissão pelo botão de teste.

## Verificado

- **`dotnet build`:** 0 warnings. O build da ponta foi feito sem o host, que estava de pé e travava a pasta `bin` dele, e o host compilou numa saída à parte.
- **`dotnet test`:**

  | Suíte | Antes | Agora |
  |---|---|---|
  | Application | 246 | 306 |
  | Infrastructure | 105 | 117 |
  | Avalara | 209 | 235 |
  | D365 | 146 | 168 |

  Todas verdes.
- **Dashboard:** `npm test` (14) e `npm run build` verdes.
- **Commits intermediários:** os quatro commits de código foram compilados e testados sozinhos, num worktree de caminho curto. Todos com 0 warnings e os testes verdes, com a Application subindo de 267 para 285 e depois 306 conforme as partes entram. O commit do dashboard foi verificado só na ponta (`npm test` e `npm run build`), e o dos docs não tem código.
- **Prova manual (2026-09-30 a 10-01).** O registro está na sessão do STATUS e nas tarefas 10.x e 11.x. Não houve log em arquivo, então a evidência é do banco, da API e da conferência visual.
  - **Pelo banco e pela API:**
    - a migração aplicada;
    - os módulos gravados pela tela, e o `/info` com eles para Admin e Viewer;
    - o Viewer com 403 no painel, no teste e no rebobinamento, e 200 no `/groups`;
    - o Client Secret intacto depois de um salvar sem digitar;
    - as falhas do coletor de 25 para 0, com o Tenant do Entra ID corrigido;
    - buscar novamente desde 2015 trouxe de volta as 14 notas da `brmf`: as 5 NF-e reenviadas ao sandbox, com a recusa conhecida, e as 9 NFS-e ignoradas.
  - **Conferência visual do usuário:**
    - os testes de credencial do D365 e da Avalara, certos e errados;
    - a máscara;
    - o painel.
  - **Os ajustes pedidos na prova:** estão no design (D14) e nas tarefas 11.1 a 11.12.

## O que fica aberto

- **A prova manual:** todas as tarefas fechadas. O passo 5 do 10.2 foi feito com o cursor apagado e recriado pela busca seguinte, com a marca nascendo no instante dela.
- **Não distinguível pela tela:** o "0 suprimidas" depois de buscar novamente. O host tinha reiniciado, e o registro de publicações já estava vazio. A regra está provada pelo teste 5.5.
- **Novos no STATUS:**
  - o rebobinamento com mais de uma réplica;
  - o freio em memória;
  - a seção `poll` ilegível, que esconde o painel;
  - a restrição por módulo na API;
  - as fatias de Contábil e Inventário.
- **Os achados:**
  - as `InboundSettings` do tenant-a em `{}` de novo;
  - o Tenant do Entra ID com um caractere a mais, que fazia o segredo certo falhar;
  - o alerta do `vitest` 3 (GHSA-82fw-gwwq-j7x9), só no redirect de mocks, que a suíte não usa.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-65"></a>

## PR #65 — Arquiva module-navigation-and-integration-panel e sincroniza as specs

**Estado:** merged · **Aberto:** 2026-10-01 · **Mesclado:** 2026-10-01 · **Branch:** `docs/archive-module-navigation-and-integration-panel` → `main` · **Commit de merge:** `9e038de` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/65)

Arquiva a change `module-navigation-and-integration-panel` (PR #64, ADR-0031) e sincroniza as specs principais.

## Specs

- **Novas:** `module-navigation` (4 requisitos), `automatic-integration-panel` (3) e `connector-credential-test` (5).
- **`avalara-tenant-authentication`:** +1, o teste de credencial conversa com a recusa lembrada.
- **`connector-secret-references`:** +2, a máscara é placeholder e nunca vira segredo.
- **`change-feed-polling`:** o requisito da marca d'água muda. A marca só regride pelo rebobinamento do Admin, sob o lease do coletor, com o cenário novo. Os outros cenários ficam.

`openspec validate --specs --strict`: 16 de 16.

## Também neste PR

O fechamento da tarefa 10.2, o painel sem marca na tela, no STATUS e nas tarefas. Ele foi feito no branch da fatia depois do merge do #64 (commit `9f40580`) e não chegou à `main`.

## Fora deste PR

O trabalho do D365 de contábil e inventário segue na árvore, sem commit, para um PR próprio.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-66"></a>

## PR #66 — Arquiva establishment-and-readable-dashboard e sincroniza as specs

**Estado:** merged · **Aberto:** 2026-10-01 · **Mesclado:** 2026-10-01 · **Branch:** `docs/archive-establishment-and-readable-dashboard` → `main` · **Commit de merge:** `d8d944a` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/66)

Arquiva a change `establishment-and-readable-dashboard` (PR #63, ADR-0030) e sincroniza as specs principais.

## As duas tarefas que estavam parciais

- **1.1, o tamanho do código do estabelecimento:** no AOT, o EDT `FiscalEstablishmentId_BR` tem String Size 10, conferido pelo usuário no Visual Studio. O `nvarchar(20)` do `BranchCode` cabe com folga, e a migração `WidenBranchCode` fica.
- **9.6, o histórico de um agendamento excluído:** um agendamento "uma vez" disparou e foi excluído pela tela. O `/schedules` ficou vazio, e o `/executions` manteve a execução com os dados dela (`ScheduledOnce`, `ScheduleId` 1, `98765432`/`0001`).

## Specs

- **Novas:** `document-grouping` (6 requisitos) e `integration-schedules` (1).
- **`platform-response-trace`:** +3 requisitos e 2 modificados.
- **Modificados:** `compliance-dispatch-outcome` (3), `discovery-queue-consumer` (2), `d365-change-feed` (2), `d365-document-assembly` (1), `automatic-integration` (1) e `tenant-boundary` (1). Nenhum cenário some.

`openspec validate --specs --strict`: 15 de 15 neste branch. Ele sai da `main` e ainda não tem as 3 specs novas do #65.

## STATUS

- **O tamanho do código do estabelecimento:** o item fecha.
- **O diretório de empresas:** o item ganha o sintoma da prova. A tela de Agendamento mostra as empresas fictícias do `companies.json` e descobre pelo catálogo de dev, e não pelo 365. Ganha também o cuidado com a Empresa Emitente LTDA, que tem tradução no tenant-a e manda a nota de exemplo ao sandbox.

## Ordem com o #65

Os dois PRs de archive tocam specs diferentes. No `docs/STATUS.md`, mexem em trechos diferentes. Podem entrar em qualquer ordem.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-67"></a>

## PR #67 — D365: entidades do contábil e do inventário

**Estado:** merged · **Aberto:** 2026-10-01 · **Mesclado:** 2026-10-01 · **Branch:** `feat/d365-accounting-and-inventory-entities` → `main` · **Commit de merge:** `e274cd4` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/67)

As entidades do D365 para os módulos Contábil e Inventário, no modelo `FiscalHubIntegration`, com o mapeamento que as decidiu, a receita e o método para criar entidades, e as pastas de teste no Postman.

## As entidades

Seis entidades novas, cada uma com um privilégio de leitura. Como as fiscais, são públicas, somente leitura e sem Data Management.

| Módulo | Entidade | Conjunto OData | Fontes |
|---|---|---|---|
| Contábil | `FSGeneralJournalLineBR` (lançamentos) | `FSGeneralJournalLineBRs` | `GeneralJournalAccountEntry`, `GeneralJournalEntry`, `Ledger`, `CompanyInfo`, `FiscalCalendarPeriod`, `MainAccount` |
| Contábil | `FSMainAccountBR` (plano de contas) | `FSMainAccountBRs` | `MainAccount`, `LedgerChartOfAccounts`, `Ledger`, `CompanyInfo` |
| Contábil | `FSCostCenterBR` (centros de custo) | `FSCostCenterBRs` | `BrazilParameters`, `DimensionAttribute`, `FinancialDimensionValueEntityView` |
| Inventário | `FSInventOnHandBR` (saldo atual) | `FSInventOnHandBRs` | `InventSum` |
| Inventário | `FSInventTransBR` (movimentos) | `FSInventTransBRs` | `InventTrans`, `InventDim`, `InventTransOrigin` |
| Inventário | `FSInventLocationBR` (armazéns) | `FSInventLocationBRs` | `InventLocation`, `FiscalEstablishmentInventSite_BR`, `FiscalEstablishment_BR` |

- **A role `FSFiscalHubIntegration`:** ganha os seis privilégios.
- **A `FSItemBR`:** ganha `InventUnitId`, a unidade de estoque, e `ProductName`, o nome do produto na língua do sistema.
- **O pacote:** continua metadado puro, sem X++. São 22 entidades, 22 privilégios e uma role.

## Os documentos

- **O `04`, Partes II (contábil) e III (inventário):** cada parte segue os passos do `07`:
  - o que o negócio precisa e o caminho padrão;
  - a medição com dado real;
  - as entidades e as junções;
  - o custo da montagem e o critério de aceite depois do sync.
- **O `06`:** a receita para criar ou alterar uma entidade editando o XML, com o VS fechado.
- **O `07`:** o método para decidir o que criar antes de escrever XML.
- **O `README` do `d365`:** as 22 entidades, os guias novos, a convenção de uma pasta por conector de ERP, e o status.

## Postman

- **As pastas novas:** `FiscalHub — contábil (3)` e `FiscalHub — inventário (3)`. Cada uma tem o teste de publicação, o smoke dos campos e o aceite estrutural das junções. No inventário, entra também o saldo na data de corte por dois caminhos.
- **As variáveis novas:** `companySemCentroCusto` e `inventDataCorte`.
- **As credenciais:** continuam vazias no repositório.

## Estado

Publicadas e aceitas no fiscosysdev em 2026-10-01, segundo o registro do `04` (§17.1 e §24.1).

## Também neste PR

- **A contagem atual de entidades:** passa a 22 entidades e 22 privilégios no `CLAUDE.md` (seção 7, que também ganha os guias `06` e `07` nos docs de referência), no `openspec/config.yaml` (o contexto que o OpenSpec passa aos agentes, que dizia 14), no estado atual da seção 8 do `04` e no `06`. Os registros datados ficam como estão: os ADRs 0022 a 0024, o `05`, e o "16 entidades fiscais" do README e do `07`.
- **O `*.log` no `.gitignore`:** os logs do host gravados pelo `Tee-Object` deixam de aparecer como arquivos não rastreados.

## Fora deste PR

- **O hub ainda não lê estas entidades.** Contábil e Inventário aparecem na barra lateral como lugares reservados (ADR-0031), e cada um é uma fatia própria, com carga manual pelo Agendamento.

## Verificado

- **Os XMLs:** as 6 entidades, os 6 privilégios, a role e a `FSItemBR` estão bem formados.
- **A collection do Postman:** é JSON válido.
- **O `openspec/config.yaml`:** o OpenSpec carrega o contexto novo.
- **Varredura de segredos:** nenhum token, client secret, senha ou chave. `clientSecret` e `access_token` estão vazios na collection.
- **O build, o deploy e o sync:** não rodam no repositório. São feitos no Visual Studio do ambiente UDE, e o resultado está no registro de aceite do `04`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-68"></a>

## PR #68 — O diretório do ERP, a descoberta por período do D365 e os filtros dos cards

**Estado:** merged · **Aberto:** 2026-10-02 · **Mesclado:** 2026-10-02 · **Branch:** `feat/erp-company-directory-and-card-filters` → `main` · **Commit de merge:** `80d1ce9` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/68)

A integração manual e o agendamento passam a usar as empresas e as notas do ERP do tenant, e não as do mock. O CNPJ mantém as letras em todo o hub. O dashboard ganha os filtros de período e de modelo, e a linha passa a ser a da execução que trouxe a nota. Change OpenSpec: `erp-company-directory-and-card-filters`. Decisão: [ADR-0032](docs/adr/0032-diretorio-do-erp-descoberta-por-periodo-e-filtros-dos-cards.md).

## O diretório e a descoberta vêm do ERP

- **O diretório de empresas:** no D365, é a entidade padrão `FiscalEstablishments`, lida entre empresas com o token do conector do tenant. A empresa é o CNPJ do estabelecimento, e a filial é o código. Um estabelecimento sem nota aparece, como o `RJ-01`.
- **A descoberta por período do D365:** serve a integração manual e a agendada. Ela lê pelo dia fiscal, por keyset, e monta a mesma referência do coletor. A nota cai no mesmo registro, sem linha duplicada.
- **A escolha da implementação:** é pelo adapter de entrada do perfil do tenant. Um ERP sem implementação responde 404 no diretório e 409 na execução manual, e o hub não inventa uma.
- **O mock:** o `companies.json` e o catálogo local passam a existir só em Development, como fallback do tenant cujo ERP não tem implementação.
- **A fila:** a execução manual, a agendada e o reprocesso publicam na fila de descoberta, a mesma do coletor.
- **O reprocesso:** a nota do D365 passa a ser reprocessável. Na falha, a origem responde 502 com um motivo curto, sem token nem corpo.

## O CNPJ com as letras

Uma função pura no Domain (`TaxIdentifiers.Normalize`) tira só o ponto, a barra, o hífen e o espaço, e mantém as letras e a caixa. Ela é usada no grupo, na montagem, no diretório, na tradução e no parceiro da Avalara, e na derivação do XML. Na tela, a máscara é pelo tamanho. O CNPJ numérico fica igual ao de antes.

## O dashboard

- **Os filtros:** o período (o dia, 7, 15 ou 30 dias, ou o personalizado) e o modelo. Valem para os cards e para a tabela.
- **A contagem:** é feita no servidor (`GET /groups/totals`), sobre todas as notas da janela. O `GET /groups` aceita a mesma janela e o mesmo modelo.
- **A data da linha:** é o dia em que a integração rodou, em Brasília. A coluna "Período integrado" mostra o período da imediata, da diária e da agendada (`2016-09-01 – 2016-10-02`), e a automática mostra "—". A data fiscal continua gravada e segue como o critério da descoberta.
- **A nota repetida:** fica na linha da última entrada. O reprocesso não a move, e a nota que a idempotência pula fica onde estava.
- **O modal:** lista exatamente a linha (tipo, modelo, modo e período), e mostra a contagem de reprocessos ao lado das consultas.

## O lado D365

A role `FSFiscalHubIntegration` passa a referenciar o privilégio padrão `FiscalEstablishmentEntityView`. Isso exige build e deploy, e não sync. O deploy foi feito no fiscosysdev em 2026-10-02. O pacote continua metadado puro.

## O que muda para quem consome a API

- **Duas migrações aditivas, aplicadas na subida do host:**
  - a `AddProcessedDocumentReprocessCount`;
  - a `AddProcessedDocumentExecution`, que preenche o dia do registro antigo pelo `CreatedAt`.
- **A rota do modal:** passa a ser `/groups/{empresa}/{filial}/{dia da execução}/documents`, com o `period` opcional (`none` ou `aaaa-mm-dd_aaaa-mm-dd`). O front e o back sobem juntos.

## Documentos

- **ADR-0032:** oito decisões, com as alternativas descartadas.
- **Linhas de revisão:** nos ADRs 0013, 0014, 0028 e 0030.
- **Também atualizados:** o `RUNNING`, o `STATUS`, o `CLAUDE.md` §7, a coleção do Postman e a receita `06` do `d365`.

## Verificado

- **O build:** 0 warnings.
- **Os testes:** 1001 testes .NET passam (3 pulados, os de sempre), mais os 39 do dashboard e o build do Vite.
- **O SQL Server:** a tradução das consultas dos cards e da tabela tem um teste.
- **O OpenSpec:** `openspec validate --strict` passa.
- **A prova manual contra o fiscosysdev, em 2026-10-02 (grupos 10 e 13 das tasks):**
  - o tenant-a lê a `FiscalEstablishments` com a credencial do próprio perfil, e a resposta é 200;
  - o dropdown lista os quatro estabelecimentos, com o `RJ-01`, que tem 0 registros no banco;
  - o agendamento da Matriz achou a nota pela descoberta do D365, e o banco tem 0 chaves duplicadas;
  - o reprocesso de uma NF-e 55 foi um envio real ao sandbox, que a recusou como antes;
  - os filtros, o modal e o fallback do tenant-b foram conferidos na tela.
- **Varredura de segredos no diff:** nenhum token, client secret, senha ou chave.

## Também neste PR: o arquivamento da change

- **A change:** está arquivada em `openspec/changes/archive/2026-10-02-erp-company-directory-and-card-filters`, com 76 tarefas feitas e a 10.7 aberta e anotada.
- **As specs principais:** foram sincronizadas, e `openspec validate --specs --strict` passa nas 21 specs.
  - **As capacidades novas:** `company-directory`, `period-discovery` e `tax-identifier-normalization`.
  - **A `document-grouping`:** ganha a data da execução, o período integrado e o modal pela linha, e perde a contagem pela data fiscal.
  - **As outras quatro:** `avalara-document-contract`, `d365-change-feed`, `d365-document-assembly` e `inbound-source-resolution` ganham os cenários do CNPJ alfanumérico.

## Fora deste PR

- **O CNPJ alfanumérico no ambiente (tarefa 10.7, aberta):** o fiscosysdev não tem estabelecimento alfanumérico. A prova é pelos testes, de ponta a ponta até o mock.
- **Limites conhecidos, no STATUS:**
  - a tela da tradução de estabelecimentos;
  - o estabelecimento removido do cadastro, que a descoberta por período não acha;
  - o agendamento de um tenant sem descoberta, que retenta a cada passada;
  - o teste de credencial, que não lê a `FiscalEstablishments`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-69"></a>

## PR #69 — fix: a empresa do diretorio e o CNPJ completo, comparada pela raiz

**Estado:** merged · **Aberto:** 2026-10-05 · **Mesclado:** 2026-10-05 · **Branch:** `feat/company-root-in-directory` → `main` · **Commit de merge:** `e16970f` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/69)

A empresa do diretorio voltou a ser o CNPJ completo na tela e no armazenamento; o que mudou foi a comparacao, que passou a ser pela raiz.

**O defeito.** O `D365CompanyDirectory` agrupava as empresas pelo CNPJ completo do estabelecimento. Os quatro estabelecimentos da `brmf` compartilham a raiz `44278225` — sao filiais da mesma empresa — entao o dropdown listava quatro empresas onde existe uma, e o `ListBranchesAsync` filtrava por igualdade exata, deixando cada "empresa" com uma filial so. O "Todas as filiais" existia e nunca podia significar mais de uma.

**A regra.** `IsSameCompany(a, b)` substitui o `IsOfCompany`: verdadeira quando os dois CNPJs tem a mesma raiz, e um lado vazio nao casa com nada. Vale nas filiais do diretorio, no escopo da descoberta e na guarda dos documentos devolvidos.

**Sem migracao.** O agendamento gravado (`44278225000180` + `Matriz`) continua com um estabelecimento so no escopo. Foi verificado no banco antes: nao existe agendamento com CNPJ completo e filial nula, que era o unico caso que a mudanca quebraria.

**Prova manual contra o fiscosysdev**, com a credencial do tenant e a saida no mock, zero envios ao sandbox real:

- dropdown Empresa: uma empresa, `44278225000180`
- dropdown Filial: Matriz, RJ-01, SAL-01 e SP-01, cada uma com o CNPJ
- Todas as filiais: 14 notas de dois estabelecimentos
- Filial SP-01: 7 notas, todas dela
- agendamento gravado: resultado identico ao de antes

ADR-0034. Reverte o ADR-0032 parag. 1. Change arquivada em `2026-10-05-company-root-in-directory`.

Fica registrado no STATUS: a coluna Empresa das tabelas passa a mostrar o CNPJ da matriz, que numa linha de outra filial nao e o do estabelecimento daquelas notas. A tratativa (gravar o CNPJ do estabelecimento na execucao) fica para a proxima rodada.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-70"></a>

## PR #70 — feat: o de/para do estabelecimento vem da listagem da plataforma

**Estado:** merged · **Aberto:** 2026-10-05 · **Mesclado:** 2026-10-05 · **Branch:** `feat/platform-establishment-resolution` → `main` · **Commit de merge:** `419421d` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/70)

Fecha o de/para do estabelecimento: o codigo que vai para a plataforma deixa de sair de um JSON e passa a vir da listagem da propria plataforma, casada pelo CNPJ.

**O problema.** No legado, o `companies.json` guardava o `codigoCIA` e o codigo do contribuinte a mao. O cliente cadastra esses codigos na plataforma e eles nao precisam ser o CNPJ, entao o arquivo envelhecia em silencio: uma filial nova na Avalara e o conector mandava um codigo que nao existe mais, e a nota era rejeitada la, nao aqui.

**O que passa a valer (ADR-0033).** A porta `IPlatformEstablishmentDirectory` lista empresa e contribuinte na plataforma e casa pelo CNPJ, sem mascara. O adapter da Avalara le `/taxcompliance/v2/empresa` (array puro) e, para cada empresa, `/taxcompliance/v2/contribuinte?empresaId=X` — o `empresaId` e obrigatorio no contrato, entao nao ha filtro por CNPJ valendo para a conta inteira e o 1+N fica. A listagem e paginada ate a pagina vazia, com o `\` somando **itens recebidos**, nunca o `\` pedido, e guardada por janela por tenant.

**A recusa fica onde haveria escolha.** Se a listagem falha ou vem vazia, o dropdown fica vazio e o usuario nao seleciona nada — sem erro na tela de quem so esta olhando. A mensagem nao nomeia a Avalara: o conector e generico e a origem das empresas muda por cliente.

**A tabela `establishments` vira sobreposicao.** Ela deixa de ser a fonte e passa a corrigir casos pontuais sobre o que a plataforma listou.

**O que fica aberto.** As provas manuais **6.1 e 6.2**, contra o sandbox real, nao foram rodadas. A change segue ativa em `openspec/changes/platform-establishment-resolution` e o archive vem no PR seguinte, quando elas fecharem — mesmo caminho do `connect-avalara-sandbox` (#59). Anotado tambem o gap de ambiguidade: mesmo CNPJ em mais de um contribuinte, que aparece no sandbox e nao deve existir em producao.

Merge do `main` (ADR-0034) integrado nesta branch: conflito so no indice de ADRs, resolvido mantendo 0032 com a nota de revisao, 0033 e 0034. `dotnet build -warnaserror` e a suite inteira passam.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-71"></a>

## PR #71 — fix: a listagem de estabelecimentos aceita as duas formas da plataforma

**Estado:** merged · **Aberto:** 2026-10-08 · **Mesclado:** 2026-10-08 · **Branch:** `feat/platform-listing-shape` → `main` · **Commit de merge:** `fead545` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/71)

Todo o despacho do tenant estava parado. As cinco NF-e 55 ficavam em `IntegrationError` com o mesmo motivo:

    Contrato do destino: a listagem de empresas (taxcompliance/v2/empresa) nao veio no
    formato verificado, um array puro (veio um objeto).

Como a listagem e tudo-ou-nada, um desencontro de forma derrubava a saida inteira. Nao eram cinco problemas.

**A causa.** O adapter trata 401, 403, 404, os demais 4xx e o `EnsureSuccessStatusCode` **antes** de parsear, entao a mensagem so e alcancavel com HTTP 2xx: nao era corpo de erro mal interpretado. O que muda e a URL. O hub chama o endpoint com opcoes de query (`\`, `\`, `\`, `\`); a forma registrada no README do fixture foi colhida do endpoint **sem** opcoes. Com opcoes, o mesmo endpoint devolve envelope. Confirmado contra o sandbox real, com tres chamadas diretas:

- `?\=5&\=empresaId` -> envelope, 5 empresas em ordem, com `idPortalCompany` a mais;
- `?\=2&\=empresaId&\=2` -> a 3a e a 4a empresas: o `\` e respeitado e a ordem e estavel;
- `?\=5&\=empresaId&\=999` -> `{"value":[]}`: **a pagina vazia tambem vem em envelope**, entao a condicao de parada dependia do mesmo defeito.

Corrobora o `/contribuinte`: ele sempre foi chamado com query, e sempre esteve marcado como envelope. Nem a plataforma mudou, nem a verificacao estava errada — o endpoint tem duas respostas, e amostramos uma e chamamos a outra.

**A correcao.** O campo `Envelope` do `ListPath` sai. As duas listas aceitam array puro ou objeto com um array em `value`; qualquer outra coisa segue sendo recusa de contrato. Trocar so o `/empresa` para `Envelope: true` seria trocar um palpite por outro, porque a forma passa a depender de a chamada ter opcoes de query — detalhe de implementacao do adapter. A guarda que importa nao muda: itens sao objetos, `empresaId` presente, `\` respeitado, teto de paginas. A mensagem de recusa passa a nomear as propriedades de primeiro nivel recebidas, nunca os valores.

**Fixtures.** O `Fixtures/listing/` e o mock diziam-se "de mentira" e carregavam a razao social, o `empresaId` e o `codigoCIA` de uma empresa real da conta de sandbox. Agora sao inventados por inteiro, com as propriedades deliberadas preservadas (o `codigoCIA` nao acompanha a ordem do `empresaId`; os codigos de contribuinte nao seguem a ordem do CNPJ). O `Fixtures/sandbox/` recebe as tres respostas reais, com `descricao` e `idPortalCompany` mascarados, e uma varredura automatizada recusa qualquer valor real fora dessa pasta e qualquer CNPJ em qualquer fixture.

**Provas contra o sandbox real.** A sonda leu 8 empresas em 2 paginas e 14 contribuintes em 16, sem recusa, parando na pagina vazia. O de/para de um contribuinte conhecido casou com um unico registro, com os codigos que o sandbox mostra. Pela esteira, com a sobreposicao retirada, a nota foi recusada por "nao tem contribuinte cadastrado na plataforma", e nao mais pela forma; com a saida no mock, a mesma nota chegou a `Confirmed`. Sem a correcao, 49 testes caem com o motivo visto em dev.

**O que fica aberto.** A 6.2 da `platform-establishment-resolution` (`\` e `\` pelo caminho do hub) depende deste merge para ser possivel. O `20247332000182` encontrado em dois testes foi conferido contra os 14 contribuintes do sandbox e nao e de nenhum: fica onde esta.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-72"></a>

## PR #72 — chore(openspec): arquiva as duas changes e fecha o vazamento de identificadores reais

**Estado:** merged · **Aberto:** 2026-10-08 · **Mesclado:** 2026-10-08 · **Branch:** `chore/archive-establishment-resolution` → `main` · **Commit de merge:** `5353b4e` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/72)

Fecha as duas changes abertas e deixa o repositorio sem nenhuma change ativa.

**A 6.2 da `platform-establishment-resolution`**, que so foi possivel depois do conserto da listagem (PR #71). A mesma resolucao rodou duas vezes contra o sandbox real, pelo caminho do hub:

| Configuracao | Empresas | Contribuintes |
|---|---|---|
| `ListingPageSize` padrao (100) | 8, em 2 paginas | 14, em 16 paginas |
| `ListingPageSize` 2 | 8, em 5 paginas | 14, em 19 paginas |

Mesmas empresas e mesmos contribuintes nas duas: como o log so mostra contagens, a comparacao foi por uma impressao da listagem inteira, calculada pela sonda com um trecho temporario ja revertido — `ED43014D120FC4D2` nas duas. Isso prova o `\` e a estabilidade da ordem do `\`: com ordem instavel as duas rodadas divergiriam. As 19 paginas para 14 contribuintes so fecham com alguma empresa de 3 ou mais. O desfecho foi a recusa nomeando o CNPJ, que e o esperado: nenhum CNPJ da `brmf` existe naquela conta.

**Os valores reais sairam do material inventado.** Saiu a razao social das empresas da conta, sairam os `empresaId` reais e saiu o CNPJ `94082641000186`, que era um registro copiado do sandbox. Nas tarefas ja feitas trocou-se so o valor, sem reescrever o relato.

**E o buraco que isso revelou.** O D10 afirmava que os `contribuinteId` eram inventados, sem nunca ter conferido — o `/contribuinte` jamais fora chamado direto, entao a busca de conferencia foi montada so com o que tinha aparecido nas chamadas. Conferido agora pela sonda, quatro deles eram reais: 10001 a 10004. A conta inteira tem 8 empresas (7330–7413) e 14 contribuintes (9709, 9992–10004).

O conserto tem duas pernas, que cobrem falhas diferentes:

- **a varredura** passa a conhecer a conta inteira. Os identificadores reais ficam gravados em `Fixtures/sandbox/identificadores-da-conta.json`, so numeros, e o `SandboxFixtureTests` recusa qualquer um deles nas fixtures e no mock, lendo dali. A sonda ganhou `listing --ids` para regravar. Provado repondo o 10002 numa fixture e o 10004 no mock: o teste caiu nomeando os dois;
- **a faixa reservada** (2.000.000.000 + o numero antigo) e o que faz um valor novo ser seguro **para sempre**. A varredura pega o que e real hoje, mas le uma gravacao que ninguem e obrigado a regravar; todo numero abaixo da faixa esta no caminho da sequencia e um dia vira real sozinho, sem que a varredura acuse. Por isso tambem sairam o 10005 a 10009, que ainda nao existem mas sao os proximos que a conta vai emitir.

Os inventados antigos fora da faixa (`empresaId` 8120 em diante, `contribuinteId` 20001 em diante) ficam: nao sao seguros — estao no mesmo caminho de crescimento, so mais longe — e sim **detectaveis**. Migram quando alguem mexer naqueles arquivos por outro motivo. A falha da varredura passa a dizer a data da gravacao, para quem bater nela saber se esta vendo uma lista de dois dias ou de seis meses.

**O que nao foi tocado:** as copias em `openspec/changes/archive/`. A regra vale para material vivo; a spec canonica em `openspec/specs/` esta limpa e e ela que governa. O unico acrescimo no arquivado foi uma correcao datada no D10, onde ele afirmava uma regra falsa — anotar nao e reescrever o registro historico.

Build `-warnaserror` limpo, suite inteira verde, `npm test` (45) e `npm run build` no dashboard, e `openspec validate --all --strict` com 23 specs e nenhuma change ativa.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-73"></a>

## PR #73 — feat: a credencial do perfil e o CNPJ do estabelecimento na execucao

**Estado:** merged · **Aberto:** 2026-10-08 · **Mesclado:** 2026-10-08 · **Branch:** `feat/explicit-credential-and-execution-cnpj` → `main` · **Commit de merge:** `9cddea8` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/73)

Fecha a change `explicit-credential-and-execution-cnpj`, com 43 das 45 tarefas. As duas que ficam abertas estao anotadas e dependem do Client Secret do app.

**O fallback do Azure CLI sai.** O `ClientCredentialsD365TokenProvider` passa a ser o unico provider de token do D365, em Development e em producao, sem ramo por ambiente nem no adapter nem no host. O defeito que isso fecha: ligar a integracao automatica **sem preencher credencial nenhuma** funcionava, porque o perfil sem `auth` caia na sessao do `az login` do desenvolvedor. Era falso positivo — o usuario concluia que a credencial estava configurada quando nao estava.

Sem credencial, quem recusa agora e o proprio provider, com `ConnectorSettingsException`, que ja tinha caminho ate a tela pelos cinco destinos (o cursor do coletor, o `D365ReadFailures`, o `CompanyDirectoryQuery`, os endpoints e o teste de credencial). Os tres motivos ficam distintos, e o de credencial incompleta passa a nomear o campo que falta, com o nome da tela: "A credencial do ERP esta incompleta: falta Client ID." O log da identidade move para o provider com o mesmo texto, para as provas ja fechadas continuarem achando a linha.

**A porta de descoberta devolve o escopo resolvido.** `IDocumentDiscovery.DiscoverAsync` passa a devolver `DiscoveryResult`, com o CNPJ do estabelecimento quando o escopo resolveu um so — decidido antes de ler as notas, e nao pelas notas achadas, entao vale tambem com zero notas. A execucao grava esse CNPJ na coluna `EstablishmentTaxId`, anulavel. O agendamento nao grava: ele registra criterio, e a execucao registra fato.

Pesados e recusados: deduzir o CNPJ das referencias achadas (nao funciona com zero notas, depende do item D8 do STATUS e erra quando ha mais de um `CompanyCode`) e perguntar ao diretorio (segunda leitura ao ERP por execucao, e as duas leituras podem discordar).

**As provas.**

- **10.5, a identidade:** com o host em Development e uma sessao do `az login` ativa, o log teve uma linha so de identidade, do app do perfil. Com o Client ID apagado pela tela, o quadro Situacao e o dropdown mostraram "falta Client ID", nao houve requisicao ao F&O e a sessao do `az` nao foi usada. Restaurado, o `GET /connector` saiu identico byte a byte.
- **10.6, a coluna, conferida na tela:**

  | Execucao | Filial | `EstablishmentTaxId` | A coluna Empresa mostra |
  |---|---|---|---|
  | 2 | `SP-01` | `44278225000260` | `44.278.225/0002-60` |
  | 3 | todas | nulo | `44.278.225/0001-80` |
  | 4 | `SP-01` | nulo | `44.278.225/0001-80` |

  A execucao 4 reproduz uma linha gravada antes da coluna. O periodo foi 2026-08-07, cujas unicas notas sao NFS-e que o roteamento ignora: nada chegou a plataforma.
- **10.7:** o item do `docs/STATUS.md` foi fechado com essa evidencia.

**O que fica aberto:** a 10.3 (os dois testes de integracao contra o F&O real) e a 10.4 (o gravador de fixtures). As duas pedem o Client Secret do app em variavel de ambiente. Na suite elas aparecem como os 2 ignorados da `D365Poll.Tests`, que e o comportamento especificado sem as variaveis.

ADR-0035. Build `-warnaserror` limpo, 1.208 testes verdes e `openspec validate --all --strict` com 24 itens.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-74"></a>

## PR #74 — fix(test): a conexao avulsa do teste da montagem leva a credencial, e a change e arquivada

**Estado:** merged · **Aberto:** 2026-10-08 · **Mesclado:** 2026-10-08 · **Branch:** `fix/integration-test-auth` → `main` · **Commit de merge:** `8ddc703` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/74)

Fecha a 10.3 da `explicit-credential-and-execution-cnpj` e arquiva a change. O repositorio fica com **zero changes ativas**.

**A prova achou um defeito.** A 10.3 — os dois testes de integracao contra o F&O real — rodou pela primeira vez com as quatro variaveis definidas, e o teste da montagem falhou:

    A credencial do ERP nao esta configurada: o perfil nao tem Tenant do Entra ID, Client ID nem Client Secret.

O perfil dele estava certo, e o `D365GoodsInvoiceSource` autenticava. Mas o teste tambem le os cabecalhos por um `D365ODataClient` avulso, fora do source, e essa conexao tinha ficado com `Auth: null`. Funcionava enquanto o fallback do `az login` existia, porque o provider antigo ignorava o `auth` da conexao e usava a sessao da maquina; com o client credentials sendo o unico caminho, virou recusa.

Era residuo exatamente do tipo que a change existia para eliminar, e passou porque **o teste ficou pulado desde que foi escrito**: sem as quatro variaveis ele nao roda, e ninguem o viu rodar. A tarefa 2.3 nomeava so o `D365ChangeFeedIntegrationTests`; o da montagem teve o provider trocado, mas nao a conexao. Nem a suite nem o build pegariam isso — 1.208 testes verdes nao pegaram.

**O conserto, duas linhas:** o `D365IntegrationEnvironment` ganhou `Auth()`, que devolve o `D365AuthSettings` com a referencia `kv:`, e a conexao do teste passou a usa-lo. Com ele: total 2, falhou 0, **ignorado 0**, com o feed lendo os cabecalhos da `brmf` e a montagem montando as NF-e 55.

**O archive.** A change vai para `openspec/changes/archive/2026-10-08-explicit-credential-and-execution-cnpj` com **44 de 45** tarefas, e as specs sao sincronizadas (+2 requisitos, ~4 modificados, -1 removido, em `d365-change-feed`, `d365-document-assembly`, `period-discovery` e `connector-credential-test`).

Fica aberta so a **10.4**, o gravador de fixtures, anotada: depende do mesmo Client Secret, e um `git diff` nao vazio abriria investigacao de tamanho desconhecido. Precedente do `connect-avalara-sandbox`, arquivado com nove em aberto.

Build `-warnaserror` limpo, a suite inteira verde e `openspec validate --all --strict` com 23 specs e nenhuma change ativa.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-75"></a>

## PR #75 — feat: rebranding do dashboard para a identidade Fiscosys, o produto vira FiscosysHub

**Estado:** merged · **Aberto:** 2026-10-09 · **Mesclado:** 2026-10-09 · **Branch:** `feat/rebrand-dashboard-to-fiscosys` → `main` · **Commit de merge:** `1cff80b` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/75)

Fecha a change `rebrand-dashboard-to-fiscosys`, com as 56 tarefas feitas e verificadas. A capability nova é a `dashboard-brand-identity`.

**O que é.** O dashboard passa às cores, ao logo e à tipografia da Fiscosys. O nome de exibição passa a ser **FiscosysHub**, e os namespaces C#, os projetos e o repositório continuam `FiscalHub`. Não é redesenho: fora do login, nenhuma mudança de layout, de estrutura de componente ou de comportamento. As decisões e o porquê de cada uma estão em `docs/rebranding-fiscosys.md`. O CSS do login e o checklist estão em `docs/rebranding-fiscosys-execucao.md`.

**A paleta estava em três lugares.** São o `tokens.css`, o espelho MUI do `theme.ts` (Configurações é toda MUI) e o objeto `C` do login. Os três mudam juntos. Um teste novo, o `palette.test.ts`, prende o `theme.ts` ao `tokens.css` nos dois temas, e prende os contrastes dos tokens novos acima de 4,5:1. Para ele ler o `tokens.css`, o `vite.config.ts` ganha `test.css.include`: o Vitest troca todo CSS por vazio, até o `?raw`.

**Três defeitos de contraste anteriores ao rebranding.** Os três eram texto branco sobre o acento do tema escuro, a 3,05–3,08:1. Com o `--accent-ink` (`#071020`), vão a 6,25:1:
- o `.fh-btn`;
- o avatar da topbar;
- os botões MUI `contained`, como o "Salvar" de Configurações. O MUI escolhia o branco sozinho, porque aceita a partir de 3:1. O `primary.contrastText` fica fixo.

**Um buraco que a paleta nova abriria.** O `#0072ce` sobre o tint dá 4,24:1. O item ativo da sidebar, o chip Admin e o rádio marcado usam o `--accent-on-tint`, no texto e no ícone: 5,94:1 no claro e 7,26:1 no escuro.

**A tipografia.**
- **Corpo:** Inter. A Raleway do manual tem dígitos old-style e não tem `tnum`. A Inter foi medida com `fontTools`: é lining e tem `tnum`.
- **Títulos e números de KPI:** Exo 2. **Mono:** DM Mono.
- **O carregamento:** um `<link>` só, no `index.html`. O `@import` sai do `tokens.css`.

**O login.**
- **O painel:** animado em CSS, com o gradiente, os brilhos, o grid, a marca d'água, as trilhas e os nós. Com "reduzir movimento", fica parado.
- **As colunas:** 55/45.
- **As trilhas:** de 760 a 1080 px de altura, nenhuma corta texto ou card.
- **Os rótulos dos cards:** têm chave própria, a `stepLabel`, porque ficam sobre o brilho e o fundo translúcido do card. Foi a terceira tentativa, e a causa estrutural está no D12 do design. O pior caso medido é 4,84:1.
- **O autofill do Chrome:** mantém as cores do tema. Há uma regra global, o espelho do MUI e a regra do login, que é sempre claro.

**As provas.**
- **Os números:** os da tela foram medidos no Chrome headless, com os estilos computados, o pixel do fundo e a geometria, nos dois temas. Estão anotados por tarefa no `tasks.md`.
- **O autofill:** foi simulado. Cada regra `:-webkit-autofill` foi duplicada com uma classe, com a mesma especificidade e a mesma ordem.
- **Os testes:** 66 do dashboard, de 66, e o build passou. O .NET não mudou: o `dotnet build` dá 0 warnings, e o `dotnet test` dá 1.212 verdes e 3 pulados, os de sempre.

**Este PR leva também um commit à parte do `d365/`,** a pedido: o `bc31f7d`. A `FSFiscalDocumentTaxTransBR` passa a tirar o cClassTrib da `FiscalDocumentTaxTrans_BR`, com o código por junção na `CClassTribTable_BR`, e o `04-mapeamento` é atualizado. O hub ainda não lê esses campos.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-76"></a>

## PR #76 — docs(openspec): arquiva rebrand-dashboard-to-fiscosys

**Estado:** merged · **Aberto:** 2026-10-09 · **Mesclado:** 2026-10-09 · **Branch:** `docs/archive-rebrand-dashboard-to-fiscosys` → `main` · **Commit de merge:** `840d834` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/76)

Arquiva a change `rebrand-dashboard-to-fiscosys`, cujo código entrou no #75.

- **A change** vai para `openspec/changes/archive/2026-10-09-rebrand-dashboard-to-fiscosys/`, com as 56 tarefas feitas e verificadas. Nenhuma ficou aberta.
- **A capability nova, `dashboard-brand-identity`,** passa a `openspec/specs/`, com os 8 requisitos e o Purpose da delta. São eles: o nome de exibição, a paleta, os contrastes, a tipografia, o logo da sidebar, o painel do login, o autofill e o favicon.
- **Validação:** o `openspec validate --all --strict` passa, com 24 itens.
- **Sem ADR:** a change não teve decisão de arquitetura. O registro das decisões de marca é o `docs/rebranding-fiscosys.md`, que entrou no #75.

O archive avisou que a seção Why do proposal passa de 1000 caracteres. É aviso, e não bloqueia. O texto ficou como foi aprovado.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-77"></a>

## PR #77 — docs(openspec): documenta três capabilities que já rodavam sem spec

**Estado:** merged · **Aberto:** 2026-10-09 · **Mesclado:** 2026-10-09 · **Branch:** `docs/document-implemented-capabilities` → `main` · **Commit de merge:** `a62c3dd` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/77)

Fecha a change `document-implemented-capabilities`: três capabilities que já rodavam sem spec ganham spec. Não há código, e por isso o PR é um só, com as specs, o archive e o STATUS.

- **As specs novas, em `openspec/specs/`:**
  - `integration-status-poll`, com 5 requisitos (ADR-0007 e ADR-0010);
  - `dead-letter-visibility`, com 5 (ADR-0010);
  - `support-ticket`, com 6 (ADR-0021).
- **O método:** cada frase do rascunho foi conferida contra o código, os testes e as ADRs. Onde o rascunho afirmava o que o código não faz, a spec diz só o verificado. Se o verificado parecia defeito, a frase saiu em vez de virar requisito.
- **Os nove achados** ficam no `design.md` da change, com a decisão de cada um. Dois são altos:
  - **A1:** uma falha que persiste na consulta de status nunca chega a `Unconfirmed`. Com 50 delas, o poll trava para todos os tenants.
  - **A7:** o Freshdesk não lê o segredo do cofre, e o provider só se configura pelo `PUT /connector`.
- **O `docs/STATUS.md`:**
  - a seção "Defeitos conhecidos", com o D1 ao D5;
  - "Próximos passos" começa pelas correções, e o D1 vai primeiro;
  - as contagens sobem para 27 specs e 14 changes arquivadas.
- **O rascunho** `docs/specs-retroativas-rascunho.md` saiu. Ele nunca foi commitado, e por isso não aparece no diff.

**As verificações:**

- `openspec validate --all --strict` passa, com 27 itens.
- `dotnet build` com 0 warnings, e `dotnet test` com 1.212 testes passando e 3 pulados. Os dois rodaram com `--artifacts-path obj/guard`, porque o Host em execução travava as DLLs do `bin/`.

**O que não entra:** nada muda em `src/`, `tests/`, `tools/` nem `dashboard/`, e não há ADR, porque não houve decisão de arquitetura.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-78"></a>

## PR #78 — feat(conectores): a conta de plataforma, os conectores com módulos e integrações, e o ambiente da instância

**Estado:** merged · **Aberto:** 2026-10-14 · **Mesclado:** 2026-10-14 · **Branch:** `feat/platform-account-and-connectors` → `main` · **Commit de merge:** `61402c8` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/78)

A forma do conector dos ADRs 0036 e 0037, antes do primeiro cliente: a conta de plataforma com a credencial, os conectores com as URLs base por versão, os módulos e as integrações de cada conector, e o ambiente como propriedade da instância. Change OpenSpec `platform-account-and-connectors`, que fica **ativa, com as 67 tarefas feitas**; o archive vem no PR seguinte, com base neste.

## Os dois commits

- **`7c88b46` fix(deps):** fixa o `System.Security.Cryptography.Pkcs` 9.0.21. É commit próprio, decidido à parte: o `Microsoft.Data.SqlClient` 6.1.1 traz o 9.0.4, com a GHSA-846f-f764-76mx e a GHSA-2fmv-rv2c-59x7, e a auditoria do NuGet derrubava o build limpo. A mensagem traz a saída do `dotnet list package --vulnerable` antes e depois. Ele não foi empurrado direto ao `main`: entra por aqui.
- **`0713524` feat(conectores):** a change inteira.

## O que muda

- **O ambiente é da instância:** vem de `Connector:Environment` (`Sandbox` ou `Production`), sem padrão; o host não sobe sem ele. Saiu do perfil, das settings, do nome do segredo, do cache de token e do teste de credencial.
- **A conta de plataforma** guarda o Client ID, o Client Secret e o endpoint de token, obrigatório e sem derivação da base. O segredo vai a `fh-{tenant}--platform--{plataforma}--clientsecret`, e o cache de token é por conta.
- **O conector**, sob a conta, guarda as URLs base por versão. A gravação usa os nomes que o adapter declara (porta `IAdapterDeclaration`).
- **Módulos e integrações** viram linhas sob o conector. O envio vai à base da versão mais o caminho da integração; o `Avalara:DocumentsPath` só preenche, uma vez, o caminho que a migração deixa à espera. O `/info` deriva os módulos dos conectores.
- **Três migrações**, cada uma na sua transação: `MoveEnvironmentToInstance` (de ida só), `AddPlatformAccountsAndConnectors` e `AddConnectorModulesAndIntegrations`, com `Down` que recusa o que a forma anterior não comporta. O `Up` recusa o dado ambíguo em vez de adivinhar.
- **A tela:** o bloco da conta, com as marcas de pendência e o "Testar credencial" que diz o que falta; o painel do conector em três abas (canvas do ADR-0039); os exemplos de URL seguindo o ambiente do `/info`.
- **Docs:** os ADRs 0036 a 0039, com nota de revisão nos dois implementados; o `RUNNING.md`; a sessão no `STATUS.md`; o pré-voo em `docs/preflight/`.

## Como revisar

1. O `design.md` da change (D1 a D17) e os ADRs 0036 e 0037: o porquê de cada escolha.
2. `src/FiscalHub.Application/Connectors`: as regras da gravação e a declaração do adapter.
3. As três migrações em `src/FiscalHub.Infrastructure/Migrations` e os testes do harness em `tests/FiscalHub.Infrastructure.Tests/Migrations`.
4. O adapter Avalara (leitura das settings, despachante, token) e o `Program.cs`.
5. O dashboard, em `dashboard/src/features/connectors`.

## As provas

- `dotnet build` com 0 warnings e `dotnet test` verde na solução inteira, com o harness contra o SQL Server e a auditoria do NuGet ligados.
- `npm test` 110 de 110 e `npm run build` verde; `openspec validate --all --strict` com 28 de 28.
- As três migrações num `Migrate` só, a partir da forma de antes da change, num teste que pegou uma inversão em cada migração.
- A migração sobre uma cópia do backup da base de dev de antes da M1: o pré-voo deu zero linhas contra dado real, e o estado final bateu campo a campo com a forma de antes.
- O ponta a ponta contra o sandbox real da Avalara: o segredo no nome novo, o token saindo do endpoint da conta, e o envio indo à base mais o caminho da integração.

O detalhe de cada prova está nas tarefas da change.

## Depois do merge, em cada ambiente

- **Antes de subir o host novo:** rodar o pré-voo `docs/preflight/platform-account-and-connectors.sql`, que é só leitura. Ele deve devolver zero linhas. O banco de dev já foi migrado.
- **Depois da subida:** a conta de cada tenant fica sem o Client Secret e, onde as settings não o tinham, sem o endpoint de token (design D8 e D11). Os dois se digitam na tela; a tela marca o que falta.

## Fora deste PR

- **O archive:** é o PR seguinte, com base neste.
- **`d365/04` e `d365/06`:** são mudanças de outra frente de trabalho (a junção do `cClassTrib` na `FSFiscalDocumentTaxTransBR`). Ficaram na cópia de trabalho, fora dos dois PRs.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-79"></a>

## PR #79 — docs(openspec): arquiva platform-account-and-connectors e cria a spec platform-account-connectors

**Estado:** merged · **Aberto:** 2026-10-14 · **Mesclado:** 2026-10-14 · **Branch:** `docs/archive-platform-account-and-connectors` → `main` · **Commit de merge:** `93c69a1` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/79)

O archive da change `platform-account-and-connectors`. **A base deste PR é o branch do PR do código**, para o diff mostrar só o archive. Quando o PR do código for mergeado e o branch dele apagado, o GitHub muda a base deste para o `main`.

## O que muda

- **A change** vai para `openspec/changes/archive/2026-10-14-platform-account-and-connectors`, como renomeação pura: nenhuma linha muda.
- **A capability nova**, `platform-account-connectors`, passa a `openspec/specs`, com 11 requisitos.
- **Os deltas** entram em outras oito specs. Ao todo, nas nove, são 17 requisitos adicionados (11 deles na spec nova), 25 modificados e 5 removidos.
- **O `Purpose` do `module-navigation`** foi ajustado à mão, como a tarefa 8.3 manda: um delta não alcança o cabeçalho da capability.
- **O `STATUS.md`:**
  - as contagens vão a 28 specs e 15 changes arquivadas;
  - o item do archive sai dos Próximos passos, e a sessão da change diz "entregue e arquivada";
  - os Próximos passos ganham a ordem decidida: a fatia cruzada (a barra lateral por conector e a aba Técnico do Admin), o Contábil e o Inventário antes do AvaTax, com os motivos escritos junto da lista;
  - entra um item novo, as URLs da Avalara declaradas pelo adapter, e o item do wizard diz o que de fato é do cliente novo.

## A prova

`openspec validate --all --strict` dá 28 de 28: as 28 specs, sem change ativa.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

---

<a id="pr-80"></a>

## PR #80 — Navegacao por conector e visao tecnica decidida no servidor

**Estado:** merged · **Aberto:** 2026-10-15 · **Mesclado:** 2026-10-15 · **Branch:** `feat/connector-navigation-and-admin-json` → `main` · **Commit de merge:** `4b718c4` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/80)

Fecha a change `connector-navigation-and-admin-json` (ADR-0039), arquivada neste mesmo PR.

## O que muda

**O eixo da navegacao passa a ser o conector.** O `/info` deixa de mandar uma lista plana de modulos do tenant e passa a responder a **arvore**: uma entrada por conector, com o adapter, o rotulo, o agendamento e os modulos ligados naquele conector. O rotulo e o agendamento vem da **declaracao do adapter**, e nao do perfil (ADR-0037, "o adapter declara, o perfil habilita"). A lista plana sai: a justificativa dela era montar a barra lateral, e duas representacoes da mesma verdade na mesma resposta criariam a pergunta de qual vale.

**A barra lateral desenha essa arvore.** Um grupo por conector, rotulado pelo rotulo declarado — na Avalara, "Tax Compliance", e nunca a plataforma —, com os modulos abaixo e o Agendamento so em quem declara que agenda. Cada item carrega a tela que abre, com titulo e subtitulo: a cadeia de ternarios por nome de tela e a tabela de titulos separada sumiram, e o `tsc` passa a garantir que nenhum item fique sem tela.

**A visao tecnica vira permissao de servidor.** O `roles.ts` do dashboard foi apagado; quem decide se o usuario ve os JSONs crus e o servidor, que responde `canViewRawJson` no `/info` a partir da mesma lista de papeis que o `/trace` usa. O servidor responde a pergunta em vez de mandar os dados para a tela decidir de novo.

## Tres coisas achadas na revisao, e consertadas aqui

1. **A chave do conector nao era garantida.** A interface do adapter afirma que a chave do conector e o nome do adapter, mas a unicidade so era conferida dentro de uma conta. Duas plataformas declarando um adapter de mesmo nome passariam, e a barra lateral ficaria com duas chaves iguais — dois grupos indistinguiveis, e o Fiscal de um abrindo a tela do outro. Agora isso **derruba a subida do host**, nomeando o adapter e as duas plataformas. Dois adapters da mesma plataforma continuam passando: e o caminho do AvaTax.

2. **A barra afirmava ausencia antes de saber.** Enquanto o `/info` nao respondia, ela mostrava "Sem conector configurado" — afirmacao sobre a configuracao do cliente feita antes da resposta, o mesmo erro que o D7 evita nos botoes de JSON e o selo de ambiente evita no rodape. Agora espera, com o mesmo `Carregando...` do resto do app. E o nulo-contra-vazio: "ainda nao respondeu" e "respondeu vazio" sao estados diferentes.

3. **Trocar de usuario mantinha o cache do anterior.** Achado na prova de tela: um Viewer continuava vendo os dois botoes da visao tecnica. O servidor estava certo o tempo todo — depois de recarregar, o `/info` respondia `canViewRawJson: false`. O `useInfo` e `['info']` com `staleTime: Infinity` e o `logout` nunca limpava o cache do React Query. Nao vazava dado (o `/trace` responde 403 e os dados sao escopados por tenant), mas a tela afirmava permissao errada, e com um login de outro tenant mostraria a arvore do tenant anterior. Agora a troca de sessao limpa o cache, no `login` e no `logout`.

## Como foi verificado

Alem dos testes, cada grupo teve o vermelho provado por inversao do comportamento de producao, com restauracao conferida por hash. A **prova manual** esta registrada na tarefa 6.4 da change: os seis itens da barra abertos um a um contra a tela que cada um declara, o estado de carregamento provado atrasando o `/info` em 8s, e o ciclo Admin/Viewer/Admin que achou e depois confirmou o conserto do item 3.

| guarda | resultado |
|---|---|
| `dotnet build` | 0 avisos, 0 erros |
| `dotnet test` | 1.377 aprovados, 0 com falha, 42 ignorados |
| `npm test` | 131 aprovados |
| `npm run build` | `tsc --noEmit` limpo |
| `openspec validate --all --strict` | 28 passed, 0 failed |

## O que deliberadamente nao entrou

**O `/info` nao tem tratamento de falha em tela nenhuma.** E anterior a esta change e maior que a barra lateral, e consertar exigiria texto de produto novo. Ficou registrado no `STATUS.md`, com o efeito: enquanto nao tiver, uma falha do `/info` faz a barra afirmar que nao ha conector configurado.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-81"></a>

## PR #81 — O dashboard para de tratar falha como resposta

**Estado:** merged · **Aberto:** 2026-10-15 · **Mesclado:** 2026-10-15 · **Branch:** `feat/dashboard-info-failure` → `main` · **Commit de merge:** `9d93b00` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/81)

Fecha a change `dashboard-info-failure`, arquivada neste mesmo PR. Paga a divida que a change anterior deixou anotada no `STATUS.md`: o `/info` nao tinha tratamento de falha em tela nenhuma.

## O defeito, e o que a revisao achou alem dele

**O que estava escrito como divida:** uma falha do `/info` chegava a barra lateral como arvore **vazia**. Arvore vazia e uma resposta, e resposta vazia nomeia a ausencia — entao um 5xx fazia a tela **acusar o cliente de nao ter conector configurado**. Estava ate comentado no `App.tsx` como divida consciente.

**O que a prova manual achou depois**, e que esvaziava a fatia: com a API fora, o dashboard nem chegava la. O `catch` da restauracao de sessao era generico —

    api.me().then(setUser).catch(() => { tokenStore.set(null); setUser(null); })

— entao 401, 5xx, rede e timeout **apagavam o token** do mesmo jeito. Conferido na tela: com a API fora, o `localStorage` ficava so com `fiscalhub.theme`. Uma indisponibilidade do servidor **destruia a sessao do usuario**, e a tela de falha que esta change acabara de construir ficava inalcancavel justamente no caso comum — o usuario era deslogado no portao antes de o `/info` chegar a falhar.

E o mesmo defeito nas duas portas: tratar "nao consegui perguntar" como se fosse uma resposta. Numa a mentira era *"o tenant nao tem conector"*; na outra, *"a sua sessao nao vale"*.

## O que muda

**Sao tres estados, e nao dois.** "Ainda nao respondeu", "respondeu" e "falhou". A falha se disfarcava de vazio porque so havia dois. E o mesmo nulo-contra-vazio que o projeto ja aplica em quatro lugares, agora com um terceiro caso.

**Sem resposta, o painel nao e desenhado.** No lugar vai uma tela de falha, **uma so para as duas portas** — o usuario nao precisa saber qual chamada nao respondeu. Ela tem "Tentar de novo", que refaz a chamada que falhou, e "Sair": ficar preso numa falha persistente sem poder trocar de conta e armadilha, e o logout e local, entao funciona justamente com o servidor fora, que e a situacao da tela. Ela **nao** mantem a topbar — meia casca seria uma terceira forma de tela, com titulo e subtitulo sem ter o que mostrar.

**So o 401 derruba a sessao.** Qualquer outra falha mantem o token.

**As duas escolhas viraram funcoes puras** (`shellState` e `sessionState`), e nao condicionais dentro do componente. Razao concreta: o dashboard nao tem stack de teste de componente, e por isso a linha que *escolhe* ficou sem teste quatro vezes na change anterior. Uma funcao pura move a escolha para onde o teste alcanca.

**O `ApiError`** nasceu porque o status HTTP so existia dentro da string da mensagem — sem ele nao da para distinguir 401 de 500. As mensagens continuam identicas, e nenhum consumidor faz `instanceof`.

## Como foi verificado

O vermelho foi provado por **inversao do comportamento de producao**, e nao so por modulo ausente. Duas inversoes no `sessionState`: devolver sempre `anonymous` — que e literalmente o `catch` de hoje — quebrou **11 testes**; devolver sempre `failed` quebrou **2**, provando que o ramo que derruba a sessao tambem esta coberto. No `shellState`, a **ordem** foi invertida duas vezes, quebrando 2 e 1 casos nomeados. Restauracoes conferidas por hash.

**Na tela**, com host de verdade e a sessao do usuario de desenvolvimento do `RUNNING.md` — seis cenarios:

1. API fora com token guardado → tela de **falha**, e o token **continua** la (antes: login, token apagado);
2. com a chamada atrasada 3s, da para ver o `Carregando...` antes da falha;
3. "Tentar de novo" com a API ainda fora → carregando e falha de novo;
4. "Tentar de novo" com a API de volta → o painel desenha, **sem pedir senha**;
5. "Sair" na tela de falha → login, token apagado, com a API ainda fora;
6. token invalido com a API no ar → 401 → **login**, nao a tela de falha.

E o caso da porta de dentro: com **so o `/info` em 503** e o resto da API no ar, a tela e a de falha, nao existe `<nav>` nenhum no documento e o texto "Sem conector" nao aparece em lugar algum.

| guarda | resultado |
|---|---|
| `npm test` | 154 aprovados (eram 131) |
| `npm run build` | `tsc --noEmit` limpo |
| `dotnet build` | 0 avisos, 0 erros |
| `dotnet test` | 1.377 aprovados, 0 com falha |
| `openspec validate --all --strict` | 28 passed, 0 failed |

## Nota sobre o texto

A frase da tela nasceu "Nao foi possivel carregar **a configuracao** do painel", quando a fatia era so do `/info`. Depois que a mesma tela passou a servir a checagem de sessao, ficou imprecisa — quando o `/auth/me` nao responde, nenhuma configuracao chegou a ser pedida — e "configuracao" e a palavra que manda o usuario conferir o lugar errado. Ficou **"Nao foi possivel carregar o painel."**

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-82"></a>

## PR #82 — A ligacao passa a ter teste, e nao so a escolha

**Estado:** merged · **Aberto:** 2026-10-15 · **Mesclado:** 2026-10-15 · **Branch:** `test/dashboard-component-test-stack` → `main` · **Commit de merge:** `96c939c` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/82)

Fecha a change `dashboard-component-test-stack`, arquivada neste mesmo PR.

## Por que, com numero

O dashboard nao tinha como testar o que acontece **dentro** de um componente. O limite esta anotado **seis vezes** nas duas changes arquivadas hoje — "a linha que escolhe nao tem teste" — e cobrou o preco **duas**:

1. depois de trocar de usuario sem recarregar, um Viewer continuava vendo os botoes da visao tecnica;
2. com a API fora, o `catch` generico do `/auth/me` apagava o token e destruia a sessao.

Nos dois casos a funcao que **decidia** estava certa e testada. Errava quem a **chamava** — e so a prova manual de tela pegou. Isso nao escala: o Contabil e o Inventario vao acrescentar telas.

## O que entra, e o escopo e fechado

`jsdom` e `@testing-library/react` em `devDependencies`, declarados **por arquivo** com `// @vitest-environment jsdom`. O `vite.config.ts` nao muda: a suite existente continua em `node`, porque o `features/api/client.ts` resolve a URL base no import, lendo `window`.

Quatro arquivos, numa lista nomeada — nao "testar o dashboard":

| Arquivo | Casos | O que prova |
|---|---|---|
| `App.test.tsx` | 9 | os quatro ramos do portao, e os tres da casca |
| `features/auth/AuthContext.test.tsx` | 8 | so o 401 apaga o token; cache limpo no login e no logout; o "tentar de novo" refazendo a checagem |
| `features/documents/DocumentDetail.test.tsx` | 3 | "Visualizar JSON": pode, nao pode, e **ainda nao respondeu** |
| `features/groups/NoteDialog.test.tsx` | 3 | o mesmo para "Baixar arquivos" |

**O que ja tinha teste nao foi reescrito.** As funcoes puras (`shellState`, `sessionState`, `rawJson`, `modules`, `sessionCache`) ficam com os testes delas. Teste de componente cobre so a ligacao.

## A prova de que a ferramenta vale

Cada arquivo foi provado **desligando a ligacao que ele cobre**, e nao por vermelho de compilacao:

| Desligamento | Cairam |
|---|---|
| portao `failed` → login | 3 de 9 |
| `isError: false` fixo no `shellState` | 1 de 9 |
| classificacao do erro → `catch` generico | **5 de 8** (e o 401 ficou **verde**) |
| `resetSessionCache` fora do login e do logout | 2 de 8 |
| `attempt` fora das dependencias do efeito | 1 de 8 |
| `canViewRawJson(info) &&` → `true &&` | 4 de 6 |

O terceiro e o que importa: trocar a classificacao pelo `catch` de antes da change anterior derruba 5 casos **e deixa o 401 verde** — exatamente como tem de ser. O teste pega o defeito real, nao a forma.

**Nenhum arquivo de producao mudou de comportamento.** As unicas edicoes em `dashboard/src/` sao dois comentarios que ficaram falsos, e o bundle sai no **mesmo hash** (`index-Dy-tPrZW.js`).

## O isolamento do jsdom, conferido de quatro jeitos

(a) as 14 contagens por arquivo identicas ao baseline; (b) `grep` provando que nao ha `test.environment` no `vite.config.ts` e que ha exatamente 4 pragmas; (c) o agregado `environment` atribuido aos 4 arquivos novos; (d) os 14 antigos rodados sozinhos: `environment 4ms`, contra 5ms do baseline.

## O achado de brinde

A spec do `module-navigation` justificava a regra de funcao pura com *"o dashboard nao tem stack de teste de componente, e a linha que escolhe ficaria sem teste"*. Essa premissa **morre com esta change** — e premissa morta e convite para alguem concluir que a regra caiu junto. Ela nao cai: vale por uma razao que sobrevive a ferramenta, e o texto foi re-ancorado nela.

E o requisito tinha um buraco que os dois defeitos atravessaram: exigia teste da **escolha** e era silencioso sobre a **ligacao**. Codigo que o satisfazia por inteiro ainda podia ter os dois defeitos — e tinha. Entrou o bullet que fecha isso, com o MUST de o teste falhar quando a ligacao for desligada.

A mesma frase falsa apareceu numa secao **viva** do `STATUS.md` e tambem foi corrigida.

| guarda | resultado |
|---|---|
| `npm test` | 177 aprovados em 18 arquivos (eram 154 em 14) |
| `npm run build` | `tsc --noEmit` limpo, bundle no mesmo hash |
| `dotnet build` | 0 avisos, 0 erros |
| `dotnet test` | 1.377 aprovados, 0 com falha |
| `openspec validate --all --strict` | 28 passed, 0 failed |

## Pendencias registradas, fora do escopo

**P1** — a **chamada** do `AdapterDeclarationGuard` no `Program.cs` continua sem teste, e esta stack **nao resolve**: jsdom nao alcanca `src/`, e a solucao la e harness de host. Ganhou entrada propria em "Riscos e pendencias de higiene". **P2** — a linha do `main.tsx` que liga `App` ao `AuthProvider`. **P3** — as demais telas.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-83"></a>

## PR #83 — A chamada da trava da chave do conector passa a ter teste

**Estado:** merged · **Aberto:** 2026-10-16 · **Mesclado:** 2026-10-16 · **Branch:** `test/adapter-key-guard-proof` → `main` · **Commit de merge:** `093fa54` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/83)

Fecha a pendencia **P1**, registrada em "Riscos e pendencias de higiene" do `STATUS.md`. Arquiva a change `adapter-key-guard-proof` neste mesmo PR.

## O problema

A trava `EnsureDistinctAdapterDeclarations` existe porque o desenho afirma que **a chave do conector e o nome do adapter**, mas a unicidade so era conferida **dentro de uma conta**. Dois adapters de mesmo nome em plataformas diferentes passariam, e a barra lateral ficaria com duas chaves iguais: dois grupos indistinguiveis, e o Fiscal de um abrindo a tela do outro.

A funcao tinha teste. **A chamada nao.** Ela e uma linha no `Program.cs`, e apagar essa linha nao quebrava teste nenhum — a trava parava de existir em silencio. E o mesmo padrao que mordeu duas vezes no dashboard em 2026-10-15: a funcao que decide esta certa e testada, e quem a chama nao tem teste.

## O caminho foi escolhido medindo, nao supondo

Antes de propor, um spike descartavel respondeu a pergunta factual — da para compor o host pelo caminho real do `Program.cs` num teste, sem SQL, Blob e Service Bus?

| Experimento | Resultado |
|---|---|
| `WebApplicationFactory<Program>` + `ConfigureAppConfiguration` | o `Program.cs` real executou, e morreu no cofre |
| idem, com config por variavel de ambiente e declaracao em conflito | excecao **da trava real**, mensagem completa, ~0,6 s |
| idem, com SQL, Blob, Service Bus e Key Vault em **portas mortas** | mesma excecao, zero infraestrutura |

O que importava era saber se o entry point roda **alem do `builder.Build()`** — se o harness capturasse o host na construcao, a linha nunca rodaria e o teste seria verde-fraude.

## Duas decisoes de desenho que vieram de achados, e nao de gosto

**Portas mortas.** Numa das inversoes, com a linha apagada e a infra de desenvolvimento **de pe**, o teste vermelho subiu o host inteiro, migrou e **escreveu no banco de dev**. Com portas mortas o verde custa 0,7 s sem infraestrutura nenhuma, e o vermelho morre em conexao recusada sem tocar em nada.

**Asserçao por tipo E mensagem.** Com a linha apagada, o host morre de `SqlException` logo depois — entao um `Assert.ThrowsAny<Exception>` ficaria **verde exatamente no cenario que o teste existe para pegar**. Isso foi **demonstrado**, e nao argumentado: uma sonda frouxa rodou lado a lado com a estrita, e passou enquanto a outra falhava. A sonda foi apagada em seguida.

## A prova

Linha da chamada apagada → `SqlException` de conexao recusada, pilha parando na migracao, **nada escrito em lugar nenhum**. Restaurada, `sha256` identico antes e depois, suite de volta ao verde.

## A spec

Entra **um requisito novo** em `platform-account-connectors` (`ADDED`, +41 linhas, de 11 para 12 requisitos): *"A chave do conector e unica entre as declaracoes do deploy"*, com a recusa na subida, a recusa **antes** de qualquer infraestrutura, e os quatro cenarios.

Sobre o `CLAUDE.md` §8 (*"nao faca backfill de spec do que ja existe"*): o alvo da regra e a documentacao retroativa em massa na adocao do OpenSpec, e este caso nao e esse. A trava **nasceu em 2026-10-15**, numa change cujo delta cobriu a arvore de navegacao e deixou de fora a invariante de que ela depende — publicar agora corrige uma omissao daquela fatia. O requisito descreve **comportamento** (o host se recusa a subir), nao processo. E e o unico que fecha o segundo caminho de morte da trava: apagar trava **e** teste na mesma fatia, contra o que hoje nao ha nada escrito.

| guarda | resultado |
|---|---|
| `dotnet build` | 0 avisos, 0 erros |
| `dotnet test` | **1.378** aprovados (eram 1.377), 0 com falha |
| `npm test` | 177 aprovados |
| `npm run build` | verde, bundle no mesmo hash |
| `openspec validate --all --strict` | 28 passed, 0 failed |

`git diff -- src/` **vazio**: nenhuma linha de producao mudou.

## Pendencias que ficam registradas

- **A conversa estrutural, com gatilho:** quando nascer um segundo caminho de composicao (outro host, um worker), vale revisitar se a conferencia deve acontecer como consequencia de registrar as declaracoes, em vez de uma linha separada — ai o teste de um caminho deixa de cobrir o outro.
- **`UseSetting`:** medido nesta execucao, ele chega a tempo na configuracao do host (ao contrario do que o design supunha) e dispensaria a colecao sem paralelismo. **Nao adotado** porque o ganho nao paga reprovar o caminho ja provado de ponta a ponta. Registrado para que quem mexer depois saiba que o caminho mais simples foi visto e medido, e nao ignorado.
- **A tecnica** de compor o host inteiro num teste ficou no cabecalho do proprio arquivo de teste, onde a proxima pessoa olha.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-84"></a>

## PR #84 — D365: pacote com 32 entidades, ferramentas, /agent-d365 e ADR-0040 (proposto)

**Estado:** merged · **Aberto:** 2026-10-16 · **Mesclado:** 2026-10-16 · **Branch:** `feat/d365-pacote-fiscosys-e-reforma` → `main` · **Commit de merge:** `be46f95` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/84)

## O que entra

O pacote D365 `FiscosysHubIntegration` passa de 22 para **32 entidades**, com aceite no fiscosysdev (10.0.49), mais as ferramentas, o agente `/agent-d365` e a proposta de pipelines no Azure DevOps.

**Pacote (metadado X++ em `d365/model`):**
- **Nome:** o pacote e a role passam a se chamar `FiscosysHubIntegration` (`38a730b`). Junto vai o módulo antigo `FiscalHubIntegration` vazio, para o deploy do rename subir (`d365/tools/modulo-vazio-FiscalHubIntegration`, `05` §12).
- **Reforma tributária e notas referenciadas:** os campos da reforma e a `FSFiscalDocumentReferencedBR` (`b3d899d`). A `FSMainAccountBR` ganha o nome e a descrição do plano de contas (`93d56c4`).
- **Pacote autossuficiente e Bloco K inteiro** (`d365/04` Parte IV): produção, lista de materiais, coprodutos, diários de estoque, produção dos Livros fiscais, estabelecimento fiscal e código de serviço próprios (`86f38dc`).
- **Lacunas do legado dos clientes** (`d365/04` Parte V), em `802d160`:
  - `FSImportDeclarationBR` (nova);
  - `CostCenter` na linha de lançamento;
  - categoria da conta;
  - `WithholdBaseAmount`;
  - `ProjCategoryId`;
  - nome do produto em pt-BR.

**Ferramentas e agente:**
- `d365/tools/`: conferir o pacote antes do build, validar no ambiente, rodar o Postman, ler o log do deploy e comparar versões (`a4e3f43`). Inclui a correção no `camposDaTabela`, que contava nome de grupo de campos como campo.
- `.claude/skills/agent-d365`: o agente, chamado com `/agent-d365` (`e8229ba`).

**Documentação:**
- `d365/03`: o pacote do Visual Studio no UDE sai sem os binários. Explica os dois modelos de ambiente do cliente (LCS e PPAC), o cuidado com o all-in-one e o que perguntar ao cliente (`d25c3db`).
- `d365/05` §13: os avisos de boas práticas do build que não bloqueiam (`5b2cddc`).
- **ADR-0040 (proposto):** um pipeline por artefato no Azure DevOps, e o pacote D365 com build de verificação e release manual versionado, guardado num feed (`f47d9f7`). Ficam em aberto o layout do D365 no repositório, o agente do build e o destino do GitHub.

**Fora do D365:**
- `src/.../D365ReadFailures.cs` e o teste correspondente trocam o nome da role na mensagem para `FiscosysHubIntegration`. É a única mudança no hub.

## Como foi testado

- `node d365/tools/conferir-pacote.js`: 32 entidades, 0 erro, 1 aviso conhecido (a junção da `FSUnitOfMeasureBR`).
- **Build, deploy e sincronização completa no fiscosysdev** (10.0.49), feitos em 2026-10-15 e 2026-10-16.
- **Postman, collection inteira:** 80 requests, 348 asserções. Só falha o `Get Token`, como esperado: o token vem do `az`.
- **Aceite das lacunas** (`d365/04` §32.8):
  - contagens iguais às da raiz (itens 2.901, lançamentos 427.230);
  - as 8 notas com declaração de importação acham a declaração pelo RecId;
  - `CostCenter` confere com a dimensão de centro de custo da linha em 150/150;
  - categoria igual à da `MainAccounts` em todas as 326 contas.

## Depois deste PR

- O repositório vai para o projeto FiscosysHub no Azure DevOps (organização adsnit). Lá entram os pipelines do ADR-0040 e o primeiro release do pacote D365.
- **Antes de mandar o pacote para o cliente:** aplicar o release no fiscosysdev com `pac package deploy`, como um cliente aplicaria.
- **Hub:** ler as entidades novas e saber a versão do pacote de cada cliente (regra do ADR-0040).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01UEMfj95ZaRAo6rNeTRC31F

---

<a id="pr-85"></a>

## PR #85 — feat(hub): capturar sem enviar - a chave de envio ao conector de saida

**Estado:** merged · **Aberto:** 2026-10-16 · **Mesclado:** 2026-10-16 · **Branch:** `feat/capture-without-dispatch` → `main` · **Commit de merge:** `3215fe5` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/85)

Uma chave do tenant decide se a esteira **envia** ao conector de saída ou apenas **grava** o JSON que iria à plataforma. Desligada, a nota percorre tudo até o ponto imediatamente anterior ao envio, para em **"Salva"**, e nada vai à plataforma. A chave é do tenant e independente de conector.

## O desenho

**O envio vira dois passos no contrato** (`PrepareAsync` / `SendAsync`). O adapter não recebe a chave — nem por parâmetro, nem por contexto, nem por injeção — e por isso não tem o que esquecer de olhar: quem decide é a esteira, no único chamador do despacho. Ler não é enviar, então o `Prepare` pode consultar a plataforma para montar o payload; postar de dentro dele é violação de contrato, e a suíte de conformidade pega.

**"Salva" é situação própria, e não falha.** Fica fora do filtro de falhas e da contagem de erro, e ganha um card próprio entre "Em processamento" e "Com erro". Religar o envio não reenvia o acumulado: o reprocesso manual é o único caminho de saída dessas notas.

**A coluna sobe com `DEFAULT 1`**, e não com o `0` que o EF geraria do default do CLR — o tenant que já existe continua enviando, que é o comportamento de sempre. O padrão de uma linha nova fica no CLR, para o EF não omitir a coluna justamente num INSERT com a chave desligada.

ADR-0040.

## Quatro defeitos achados na conferência em tela, e não pela suíte

1. **A nota em "Salva" não tinha como sair.** `FAILURE_STATUSES` não inclui `Saved` e o botão era `failed && ...`, então a nota salva não tinha botão de reprocessar nem com a chave ligada — contra o ADR-0040 ("é por ele, e só por ele, que uma nota sai de 'Salva'") e contra um cenário publicado no próprio delta. O servidor aceitava; só a tela não oferecia. Nenhum teste pegou porque todo teste de "Salva" tinha sido escrito com a chave desligada. A elegibilidade do reprocesso virou predicado próprio (`isReprocessable` = falhas **+** `Saved`), ao lado de `FAILURE_STATUSES`, que alimenta o filtro de erro e não foi tocada.
2. **O botão sumia em vez de ficar desativado**, contra o texto da própria spec. Agora fica visível e desativado, com o motivo no próprio botão, alcançável por mouse, teclado e toque (`aria-disabled` mais guarda no `onClick`, porque `disabled` não recebe hover nem foco).
3. **A faixa de aviso aparecia em nota que nunca teve reprocesso** (Finalizado, Ignorado), atribuindo à chave uma indisponibilidade que não era dela. A faixa saiu: a explicação só existe onde o botão existe.
4. **O subtítulo do card afirmava um estado ao vivo** ("salvas sem enviar: envio desligado") mesmo com a chave ligada. Virou definição de categoria, como os irmãos dele.

E um quinto, na releitura: a dica era `position: absolute` dentro do corpo `overflow: auto` do modal, e o modal do detalhe é mais **baixo** do que ela — abrir a explicação criava **barra de rolagem** no modal. Agora é um portal para o `document.body`, `position: fixed`, ancorada no retângulo do botão, com trava nos dois lados da janela.

## Guarda

`npm test` **200/200** (eram 177 antes da fatia) · `dotnet build` verde · `openspec validate --all --strict` **29/29** · prova por inversão de cada comportamento novo, com hash conferido após cada restauração. Uma inversão expôs uma asserção que passava por acidente (a mutation do React Query não chama o servidor dentro do clique); o teste foi endurecido com a inversão ainda no lugar.

## Conferido em tela, com dado real

Com uma captura real feita com a chave desligada: o painel mostra DOCUMENTOS 1 e SALVAS 1, com **zero** nas outras três faixas; a nota traz o selo "Salva", o motivo gravado e o botão "Reprocessar" presente e desativado; religando a chave, o mesmo botão volta a ficar habilitado. O clique com a chave ligada não foi exercido de propósito — ele envia a nota de verdade ao sandbox da plataforma.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-86"></a>

## PR #86 — chore(tools): guarda da numeracao dos ADRs, para a colisao aparecer antes do merge

**Estado:** open · **Aberto:** 2026-10-16 · **Branch:** `chore/conferir-numeracao-adr` → `main` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/86)

Duas frentes trabalham em paralelo neste repositório e cada uma escolhe o próximo número de ADR olhando para o `main` que ela tem. Quando as duas escrevem um ADR no mesmo dia, as duas escolhem o mesmo número — e o git só acusa no merge. Foi o **0040 de 2026-10-16** (#84 e #85), resolvido renumerando o desta frente para 0041 e acertando 36 citações.

Resolver antes do PR custa um `git mv`. Depois do merge custa caro: um ADR já publicado e citado não se renumera sem quebrar o link de quem já leu.

## Por que uma guarda, e não uma regra

Uma linha no `CLAUDE.md` dizendo "confira o número" não teria evitado nada, porque **ninguém violou regra nenhuma** — as duas frentes simplesmente não se enxergam. É o mesmo princípio que o projeto já aplica ao código: prova por inversão em vez de confiança, e a tela não afirmando o que não sabe. Aqui vale para o processo.

Também descartei **faixas por frente** (hub 0040+, D365 0100+): obrigaria classificar todo ADR novo, e o próprio 0040 do #84 mostra o problema — "um pipeline por artefato" é infra do repositório inteiro, não é de nenhuma das duas frentes. Seria trocar uma colisão rara por um atrito constante.

## O que o script confere

`node tools/conferir-adr.js` sai com código 1 se achar problema, e diz qual é o próximo número livre:

1. **dois arquivos com o mesmo número aqui;**
2. **um número que o `origin/main` já deu a OUTRO arquivo** — a colisão que chega no merge;
3. **o índice do README**: todo ADR tem linha, e toda linha aponta para arquivo que existe. Esta é justamente a que uma resolução descuidada de conflito deixa quebrada.

Lê a referência que o último `fetch` deixou, sem rede. Num clone sem `origin/main`, o teste 2 se desliga e os outros dois continuam valendo.

## Conferido pelo avesso

Um caso de cada vez, com o repositório restaurado depois. Os três disparam quando devem.

O do `origin/main` foi provado **reproduzindo o estado real da branch antes do merge** — só o `0040-capturar-sem-enviar.md` presente, o do Azure DevOps fora, o README com a minha linha no número 0040. Sem isso ele passaria por acidente: com os dois arquivos presentes, quem dispara é a checagem de duplicata local, e a comparação com o `origin/main` nunca seria exercida.

```
 - ADR 0040 colide com o origin/main: aqui e "0040-capturar-sem-enviar.md",
   la ja e "0040-azure-devops-pipelines-independentes-e-versoes-do-pacote-d365.md".
   O numero que ja esta no main fica com ele. Renumere o daqui: git mv, e acerte as citacoes
   (ADR-0040 no codigo, nos testes, nas specs e no STATUS) antes de abrir o PR.
```

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---

<a id="pr-87"></a>

## PR #87 — fix(servicebus): a dead-letter aprende a diferenca entre invalida e falhou

**Estado:** open · **Aberto:** 2026-10-16 · **Branch:** `fix/dead-letter-poison` → `main` · **Autor:** MarceloMLR · [original](https://github.com/MarceloMLR/FiscalHub/pull/87)

Uma mensagem cujo corpo não desserializa ficava presa na dead-letter **para sempre**, reentregue sem fim, com uma linha de log por tentativa (**D2** do `STATUS.md`).

## A causa não era a que estava escrita

O `STATUS.md` dizia que a casca "só chama `CompleteMessageAsync` **depois** do `HandleAsync`". **O sintoma estava certo; a causa, não.** Concluir depois é a regra correta — é a mesma do consumidor normal, e é ela que salva uma falha transitória, porque exceção = não concluiu = reentrega, e a próxima entrega é o conserto.

A causa era **uma linha**: um corpo inválido lançava a **mesma exceção** que SQL fora do ar. A casca não tinha como distinguir as duas e tratava as duas do único jeito que conhecia — não concluir. Para a falha transitória isso é certo. Para o corpo inválido é o laço infinito, porque nenhuma reentrega torna um corpo ilegível tratável.

**Por isso o conserto não é "concluir sempre".** Isso faria o corpo inválido sumir em silêncio *e* descartaria a falha transitória junto: trocaria um defeito por dois. O conserto é ensinar o código a distinguir. Corpo inválido deixa de ser exceção e vira desfecho próprio — descartado, com um `LogError` que carrega o corpo e a identidade da mensagem, e **concluído**. A chamada de `CompleteMessageAsync` **não se move**.

Há um requisito novo, `Falha no tratamento não conclui a mensagem`, que existe precisamente para que "concluir sempre" **não passe** no `openspec validate`. E o requisito publicado que descrevia o defeito — *"a mensagem MUST NOT ser concluída"* — foi `REMOVED`: ele veio do backfill que documentou o que o código fazia.

## O nulo contra o vazio, afiado: o colapso é por leitor

Vai junto a metade do **D5** desta frente: um `DeadLetterReason` em branco deixava motivo vazio no registro, porque a cadeia de `??` só testava nulo.

No **registro**, ausente e em branco colapsam. O leitor é um só e a pergunta é uma só — *"por que essa nota parou?"* — e "foi dito que não há motivo" e "nunca foi dito" produzem a mesma tela vazia.

No **log do descarte**, a distinção atravessa inteira. Aquele leitor pergunta outra coisa: *"o que exatamente chegou, para eu republicar isto à mão?"*, e para ele "o transporte não informou" **é** informação. O log carrega os campos crus, e não o resolvido — senão citaria o transporte dizendo uma frase nossa que ele nunca disse. O teste que fecha isso se chama `A_transport_that_said_nothing_is_not_quoted_saying_the_default_reason`.

Isso não é exceção à regra do projeto; é a regra ficando mais precisa. **O colapso nulo/vazio é por leitor, e não global.**

## Medido contra o emulador

- O **`DeliveryCount` na dead-letter não incrementa** — entra em 6 e congela, inclusive por abandono explícito e por expiração de lock. Isso **mata a saída barata** que a Open Question 2 supunha: contar tentativas exigiria contador nosso, que é esquema novo.
- A reentrega **respeita o lock: 60,0 s**, igual ao `LockDuration`.
- Limite honesto: medido no **emulador**, não no Azure.

## Guarda

Mensageria `0/0` e **37 aprovados + 1 pulado**; solução **1433 / 0 falhas / 43 ignorados** (era 1429/0/43 — os +4 são todos da mensageria); `openspec validate --all --strict` **30/30** antes do archive, **29/29** depois.

Seis inversões, com os hashes de produção batendo byte a byte após cada restauração:

| Inversão | Vermelhos |
|---|---|
| descarte → `throw` (volta o D2) | **12**, exatamente os casos novos |
| `try/catch` no store devolvendo descarte (o "concluir sempre") | **1**: `A_store_that_fails_propagates_and_is_not_swallowed` |
| `ResolveReason` → cadeia de `??` (volta o D5) | **5**: os 4 em branco + o do store. Os **4 com nulo ficaram verdes** — a assimetria é o que prova que o conserto é sobre *em branco* |
| guarda de tenant/chave em branco | **5** |
| um campo cru de volta ao resolvido | **3** |
| os dois campos de volta ao resolvido | **4** |

## O que fica aberto, dito na cara

Numa execução normal, **as quatro linhas da casca seguem descobertas**: apagar o `CompleteMessageAsync` não quebra nenhum teste da suíte padrão. Quem as cobre é o teste do emulador, que é opt-in por `FISCALHUB_SB_EMULATOR`. É o mesmo padrão "a função tem teste, quem a chama não tem" que motivou a change `adapter-key-guard-proof` no Host — e, como lá, merece fatia própria em vez de alargar esta. Está registrado no `STATUS.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01Diy9PB4XnXFjDeNAP1ZfMj

---
