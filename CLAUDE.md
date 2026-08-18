# CLAUDE.md — FiscalHub

Guia para agentes de IA (Claude Code e afins) trabalharem neste repositório. Leia isto antes
de propor ou escrever código. Escrito em português porque toda a documentação do projeto é em
português.

---

## 1. O que é o FiscalHub

Middleware de integração fiscal **plugável**, em .NET, preparado para a Reforma Tributária
(IBS/CBS/IS). Recebe notas fiscais **já emitidas** (de um ERP ou de arquivos XML) e as
**despacha** para uma plataforma de compliance (Avalara e afins), traduzindo cada documento
para um **modelo de domínio interno** no caminho.

**Escopo — o que ele faz:** recebe, traduz e despacha.
**Fora do escopo — o que ele NÃO faz:** não emite notas, não assina com certificado, não
transmite à SEFAZ e não calcula imposto. Se um pedido implicar uma dessas, pare e sinalize —
provavelmente está no lugar errado.

---

## 2. Arquitetura e regra de dependência (inegociável)

**Ports & adapters (hexagonal).** A direção da dependência é sagrada:

```
Domain  ←  Application  ←  Infrastructure / Adapters  ←  Host
(puro)     (portas + casos de uso)   (implementações)      (wiring/DI)
```

- **Domain** não depende de nada. Sem EF, sem Azure, sem framework. Só tipos e regras.
- **Application** define **portas** (interfaces) e casos de uso. Depende só do Domain.
- **Infrastructure / Adapters** *implementam* as portas (EF/SQL, Service Bus, Avalara, Blob…).
  Dependem da Application; **nunca** o contrário.
- **Host** é o único que conhece todos e faz a injeção de dependência.

Regra prática: se você precisa importar algo de Infrastructure/Adapters dentro de Application
ou Domain, **está errado** — inverta com uma porta.

---

## 3. Mapa do repositório

| Caminho | O que é |
|---|---|
| `src/FiscalHub.Domain` | Núcleo puro. `Envelope` (documento fiscal interno), `Goods` (mercadoria, IBS/CBS/IS). |
| `src/FiscalHub.Application` | Portas + casos de uso: `Admin`, `Auth`, `Connectors`, `Directory`, `Inbound`, `Integrations`, `Metadata`, `Outbound`, `Pipeline`, `Queries`, `Support`, `Tracing`, `Validation`. |
| `src/FiscalHub.Infrastructure` | Implementações: `Persistence` (EF), `Migrations`, `Auth`, `Admin`, `Support`, `Tracing`. |
| `src/Adapters/*` | Adapters plugáveis: `Inbound.Xml`, `Ingress.BlobDrop`, `Messaging.ServiceBus`, `Outbound.Avalara`, `Directory.Json`, `Discovery.Local`, `Support`. |
| `src/FiscalHub.Host` | Host da API. Faz o DI e expõe os endpoints. |
| `tests/*` | Espelham a estrutura de `src`. Todo adapter/caso de uso tem teste. |
| `dashboard/` | Front-end React + Vite + TypeScript. |
| `d365/` | **Lado Dynamics 365 F&O (X++)** — docs e artefatos do conector no ERP. Ver seção 7. |
| `docs/adr/` | Architecture Decision Records (numerados, template `0000`). O "porquê" de cada decisão. |
| `docs/RUNNING.md` | Como rodar tudo localmente ("Azure sem Azure"). |
| `tools/MockComplianceApi` | Mock local da Avalara para dev/testes. |

---

## 4. Convenções e não-negociáveis

- **.NET 10 / C# 14.** `Nullable` habilitado, `ImplicitUsings` habilitado.
- **`TreatWarningsAsErrors = true`** (em `Directory.Build.props`). Warning quebra o build.
  Código que você entregar tem que compilar **sem warnings**.
- **Configuração compartilhada fica em `Directory.Build.props`** — não repita `TargetFramework`
  etc. em cada `.csproj`.
- **Solução:** `FiscalHub.slnx`.
- **Idioma:** código e identificadores podem ser em inglês; docs, ADRs e comentários de decisão
  em português (siga o padrão existente).

---

## 5. Como rodar, buildar e testar

Da raiz do repositório:

```bash
dotnet build                 # compila a solução (tem que ficar verde, 0 warnings)
dotnet test                  # roda todos os testes
docker compose up -d         # sobe infra local: Azurite (Blob) + SQL Server
```

- Mock de compliance: rodar `tools/MockComplianceApi` (ver `docs/RUNNING.md`).
- Dashboard: `cd dashboard && npm install && npm run dev` (Vite).
- Detalhes completos de "rodar de ponta a ponta sem Azure": `docs/RUNNING.md`.

---

## 6. Padrões recorrentes (use os que já existem, não reinvente)

- **Esteira assíncrona** entre entrada e saída: fila + **claim-check** (payload grande no Blob,
  referência na mensagem).
- **Idempotência** em três eixos já implementados: por conteúdo (hash do cru), por gatilho, e
  no banco. Ao adicionar caminho novo, reuse — não crie um quarto esquema.
- **Retry + dead-letter (DLQ) visível.** Falha isolada por documento; a esteira não morre por
  causa de um doc ruim.
- **Poll de status** para confirmar integração de forma idempotente por estado.
- **Multi-tenant por configuração**, não por código: perfil de conector por tenant, resolução
  do adapter em runtime, JWT com escopo por tenant. Variação de cliente é **dado**, não branch.
- **Rastreabilidade por "fotos" no Blob** (snapshots de domínio + outbound).

Antes de criar abstração nova, procure a porta/analógico que já existe (`grep` nas pastas de
`Application` e `Adapters`).

---

## 7. Lado Dynamics 365 F&O (X++) — em `d365/`

O conector do ERP é um **hook fino e genérico**, propositalmente burro:

- **Data entity** OData (`FS_FiscalDocument_BR*`) expõe a nota.
- **Business event** (`FS_FiscalDocStatusChangedBusinessEvent` + contract) manda a **identidade**
  da nota (empresa, RecId, número, série, data, status) para o **nosso Service Bus** quando o
  status muda.
- **CoC** (`FS_FiscalDocument_BR_Extension`) intercepta `update()`/`doUpdate()` da tabela
  `FiscalDocument_BR` e dispara o evento na transição para `Approved` (e depois `Canceled`).

Princípios:
- **Nenhuma lógica de cliente** no X++. Toda variabilidade (endpoints, conta Avalara, roteamento)
  vive no FiscalHub, por configuração. Um mesmo pacote serve todos os clientes.
- O Service Bus é **nosso**, não do cliente; cada cliente recebe uma **SAS send-only** própria.
- Ambiente atual: **Unified Developer** (gerenciado pelo PPAC, sem LCS). Deploy de dev =
  "Deploy Models to Online Environment". Promoção estilo cliente = deployable package + o ALM
  do cliente (pipeline ou apply manual). **1 código → 1 build → N clientes.**

Docs de referência: `d365/02-business-event-status-changed.md`, `d365/glossario-x++-fno.md`.

---

## 8. Fluxo de trabalho (como contribuir uma mudança)

- **Fatias verticais finas.** Cada fatia termina com **build verde + testes**. Nada de PR gigante.
- **ADR para decisão relevante.** Se a mudança envolve uma escolha de arquitetura, registre em
  `docs/adr/NNNN-*.md` (siga o `0000-template.md`).
- **Teste junto** (o projeto é test-first por fatia). Adapter novo → teste do adapter.
- **Spec-Driven Development (OpenSpec), incremental.** Ao adotar: spec primeiro via `/opsx:propose`
  para a mudança que você vai fazer; **não** faça backfill de spec do que já existe. As specs
  crescem uma mudança por vez, e vivem em `openspec/` (fonte da verdade — não duplique em doc
  paralelo).

---

## 9. O que NÃO fazer

- Não faça Domain/Application dependerem de Infrastructure/Adapters.
- Não introduza warning (o build trata warning como erro).
- Não coloque lógica específica de cliente no código (X++ ou .NET) — vai em configuração.
- Não emita/assine/transmita nota nem calcule imposto (fora do escopo).
- Não crie esquema novo de idempotência/retry/DLQ — reuse os existentes.
- Não converta o código legado em spec de uma vez ao adotar OpenSpec.
