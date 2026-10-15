## Why

O dashboard **não tem como testar o que acontece dentro de um componente React**. A consequência não é teórica: ela
está escrita, repetidamente, nas duas changes arquivadas em 2026-10-15, e os dois defeitos reais do dia passaram
inteiros pela suíte.

### O limite, anotado seis vezes em duas fatias

Em `connector-navigation-and-admin-json`:

| Tarefa | O que ficou sem teste |
|---|---|
| 1.6 | "o dashboard não tem stack de teste de componente (sem `@testing-library/react`, sem jsdom)… Trazer `@testing-library/react` seria dependência nova, fora do escopo" |
| 4.0 | o mesmo limite, repetido |
| 4.8 | "o teste é do módulo que decide, e a linha do `App.tsx` que passa o `isLoading` não tem teste — o `tsc` garante a forma, não a escolha" |
| 4.10 | "o teste é **da função que limpa**, com um `QueryClient` de verdade" — a chamada dela, não |

Em `dashboard-info-failure`, o mesmo limite reaparece no `design.md` (D3, S7 e a Open Question **Q2 — Adotar stack de
teste de componente**) e no `proposal.md` ("é justamente a ausência dela que justifica a função pura").

E está **no código de produção**, em dois comentários, e **na spec publicada**:

- `dashboard/src/features/shellState.ts:6-7` — "O dashboard não tem stack de teste de componente (sem
  `@testing-library/react`, sem jsdom), e por isso a linha que *escolhe* ficou sem teste quatro vezes na change
  anterior";
- `dashboard/src/features/auth/sessionState.ts:8-9` — a mesma frase;
- `openspec/specs/module-navigation/spec.md:282` — dentro de um **MUST publicado**.

### Os dois defeitos que a suíte deixou passar

Nenhum dos dois foi pego por teste. Os dois caíram na prova manual de tela.

1. **Trocar de usuário sem recarregar deixava o Viewer vendo os botões da visão técnica.** A `canViewRawJson` estava
   certa e tem teste (`features/rawJson.test.ts`, 4 casos). O `/info` do servidor estava certo — confirmado lendo o
   corpo da resposta na rede. Quem errava era o cache do React Query por trás da função: `queryKey: ['info']`,
   `staleTime: Infinity`, e a chave não tem nada do usuário.
2. **Com a API fora, o `catch` genérico do `/auth/me` apagava o token e destruía a sessão.** A classificação do erro
   nem existia — e nenhum teste alcançava o `AuthContext`.

Nos dois casos o que faltava era **teste da ligação**: do componente que chama a função pura. A função pura move a
escolha para onde o teste alcança — e foi a decisão certa —, mas ela não cobre a fiação, e é na fiação que os dois
defeitos moravam.

### Por que agora

O Contábil e o Inventário (itens 2 e 3 dos "Próximos passos" do `docs/STATUS.md`) vão acrescentar telas. O momento
barato de ter isto é antes delas.

## What Changes

### 1. A stack entra: `jsdom` + `@testing-library/react`

Duas dependências de desenvolvimento no `dashboard/package.json`, compatíveis com React 18.3 e vitest 3.2. Nada mais.

### 2. O ambiente é por arquivo, e não global

Os testes de hoje rodam no ambiente **`node`** do vitest, e isso é **de propósito**: o `api/client.ts` resolve a URL
base **no import** (`const BASE = resolveApiBase()`, `client.ts:31`), lendo `window.__FISCALHUB__` e
`window.location.hostname`. É esse fato que fez a `ApiError` nascer em arquivo próprio (`api/ApiError.ts`), e está
escrito no D13 do `design.md` da `dashboard-info-failure`.

Portanto: `// @vitest-environment jsdom` no topo de cada arquivo novo. **Nenhuma chave `test.environment` no
`vite.config.ts`.** A suíte existente não muda de ambiente, não muda de comportamento e não paga o custo do jsdom.

### 3. Uma lista nomeada de ligações, e nada além

| # | Ligação | Arquivo de teste |
|---|---|---|
| 1 | `App.tsx` — o portão de sessão (checando, falhou, anônimo, autenticado) e os três ramos do `shellState` (carregando, painel, falha), cada um levando à tela certa; e o `onRetry` da tela de falha **do portão**, que refaz a **checagem**, e não o `/info` | `src/App.test.tsx` |
| 2 | `AuthContext` — limpa o cache no login e no logout; só o 401 apaga o token, e 5xx/rede/timeout mantêm; o "tentar de novo" re-executa a checagem | `src/features/auth/AuthContext.test.tsx` |
| 3 | `DocumentDetail` e `NoteDialog` — mostram ou escondem os dois botões da visão técnica conforme o `canViewRawJson` do `/info` | `src/features/documents/DocumentDetail.test.tsx`, `src/features/groups/NoteDialog.test.tsx` |

Isto **não é "testar o dashboard"**. É exatamente o que ficou anotado como limite, e nada mais.

### 4. Cada teste novo tem que falhar quando a ligação que ele cobre for desligada

Não basta vermelho de compilação. A change só se justifica se os testes pegarem **o desligamento da fiação**. É o
mesmo método já usado neste repositório — a prova por inversão das tarefas 1.2, 4.8 e 4.10 de
`connector-navigation-and-admin-json`, com restauração conferida por hash. Aqui ela é **tarefa escrita**, e não boa
intenção: cada arquivo novo tem uma tarefa de inversão própria, com a contagem de falhas registrada como evidência.

### 5. As frases que ficam falsas saem

Três lugares afirmam que o dashboard não tem stack de teste de componente. Depois desta change, os três mentem:
`shellState.ts`, `sessionState.ts` e a spec publicada `module-navigation`.

## Capabilities

### Modified Capabilities

- **`module-navigation`**: o requisito publicado **"Sem saber quem é o usuário e o que o tenant tem, o dashboard não
  desenha o painel"** é modificado, por dois motivos, e **só** por eles.

**Motivo 1 — uma frase da spec fica falsa.** O bullet das funções puras (`spec.md:279-284`) justifica um MUST com um
fato sobre o repositório: *"Nenhuma das duas pode viver como condicional dentro do componente: **o dashboard não tem
stack de teste de componente**, e a linha que escolhe ficaria sem teste."* Esta change torna essa premissa falsa. Deixar
como está não é cosmético: um leitor futuro ou acredita na frase e reinventa o contorno, ou percebe a contradição e
passa a desconfiar da spec — e, pior, pode concluir que, caída a premissa, caiu a regra. A regra **não** cai. Ela
continua certa por uma razão que sobrevive: a escolha se prova caso a caso, de graça, quando é um valor que entra e um
valor que sai; dentro do componente ela só se prova montando a tela. A modificação **re-ancora o MUST** nessa razão.

**Motivo 2 — o requisito tem um buraco que os dois defeitos expuseram.** Como publicado, ele exige teste da **escolha**
e é silencioso sobre a **ligação**. Código que o satisfaz por inteiro ainda pode ter os dois defeitos de 2026-10-15 —
de fato, o código tinha. O bullet novo fecha isso: a ligação também tem teste, e esse teste MUST falhar quando a
ligação for desligada.

**Precedente, para que isto não pareça processo enfiado em spec de produto.** A obrigação de teste já existe como MUST
publicado neste repositório, no mesmo gênero: `platform-response-trace` diz *"A regra de papel MUST ter teste
automatizado do endpoint, por papel, e não só por tenant"* (`spec.md:214-216`), e `module-navigation` já tem dois
cenários "provada por teste" (`spec.md:321` e `365`). O projeto põe a obrigação de teste na spec quando **o teste é a
garantia**. Aqui ele é a garantia — é a change inteira.

### Capabilities que NÃO mudam, e por quê

A regra que eu apliquei é estreita e verificável: **o delta vai exatamente onde uma frase publicada fica falsa, ou onde
o requisito deixa de fora o que o defeito atravessou.** Fora disso, não há delta — e isso é conclusão, não esquecimento.

- **`platform-response-trace` — nenhum delta.** É a dona dos dois botões da visão técnica (item 3 da lista), e o
  comportamento que os testes novos vão cobrir **já é MUST publicado, com cenário**: *"A tela MUST usar essa resposta
  para mostrar os botões, e MUST NOT mostrá-los antes de o `/info` responder, nem se ele falhar"* (`spec.md:207-209`),
  com os cenários "O /info diz quem vê" (`241`) e "Troca de usuário sem recarregar a página" (`245`). Nada ali é falso
  e nada está faltando — aquela spec nunca se justificou pela ausência da stack (conferido: a frase "stack de teste de
  componente" aparece em `openspec/specs/` **uma única vez**, em `module-navigation:282`). Os testes do item 3 apenas
  **honram** um requisito que já existe. Acrescentar "e tem teste de componente" lá seria texto novo num requisito que
  não está quebrado nem incompleto — exatamente o requisito forçado que esta proposta foi instruída a não escrever.
- **Nenhuma capability nova.** Uma spec `dashboard-testing` teria como conteúdo "quais pacotes de teste o dashboard
  usa", que é detalhe de build, não capacidade do produto — e separaria a regra ("a escolha é pura, a ligação é
  testada") do requisito que a motiva, obrigando a ler dois documentos para entender um.
- **Nenhuma spec de servidor.** Nenhum arquivo de `src/` (.NET) é tocado.

### Sobre ADR

**Não há ADR nesta change**, e a razão é estrutural, não preguiça. A regra do repositório é ADR para decisão de
arquitetura; o índice de `docs/adr/` guarda decisões cuja reversão custa algo estrutural (polling contra evento, limite
de tenant, perfil de conector). Esta change não move nenhuma fronteira: vive inteira dentro de `dashboard/`, não toca
porta, adapter, domínio nem host, e se reverte com uma edição de `package.json`. A única decisão com consequência a
jusante — **jsdom por arquivo, não global**, porque o `client.ts` lê `window` no import — já está registrada no D13 do
`design.md` de `dashboard-info-failure`, e é reafirmada no `design.md` desta change (D2). Se o Marcelo discordar, o ADR
é barato de escrever depois; está nas Open Questions.

## Non-goals

- **Playwright, Cypress ou qualquer e2e.** Outra conversa, outra fatia, outro custo.
- **Teste de snapshot.** Snapshot não prova ligação: ele congela markup e quebra em qualquer refactor de estilo, que é
  o oposto do que esta change quer.
- **Testar interno de MUI.** O `Modal`, o `DataGrid` e os ícones são de terceiro e já têm os testes deles.
- **Qualquer arquivo de `src/` (.NET).** Em particular, a chamada do `AdapterDeclarationGuard` no `Program.cs`
  continua sem teste. **Está registrada como pendência no `tasks.md`, e não como tarefa** — não se resolve aqui.
- **Cobertura como meta numérica.** Nenhum limiar, nenhum relatório, nenhuma porta de CI por percentual. A meta é a
  lista nomeada do item 3, e ela termina.
- **Reescrever o que já tem teste.** As funções puras — `shellState`, `sessionState`, `rawJson`, `modules`,
  `sessionCache`, `connectorPanelRows` — ficam **exatamente como estão**, com os testes que têm. Teste de componente
  cobre só o que função pura não alcança. Duplicata piora: dois lugares para manter a mesma verdade.
- **Refatorar código de produção para "ficar testável".** Nenhuma extração, nenhum prop novo, nenhuma injeção. Se uma
  ligação da lista não puder ser testada sem mexer no componente, isso **vira achado para o Marcelo**, não um refactor
  silencioso.
- **jsdom global.** O `vite.config.ts` não ganha `test.environment`.
- **As outras telas.** `GroupsPage`, `ConnectorsPage`, `UsersPage`, `IntegrationsPage`, agendamentos, suporte,
  `LoginPage`, `RawJsonModal` e `TicketModal` não entram. A lista é fechada.
- **Texto de produto novo.** Os testes afirmam sobre o texto que já existe.
- **`@testing-library/jest-dom`.** Dependência a mais por açúcar de matcher; o vitest já afirma o que precisamos
  (ver `design.md`, D4).

## Impact

- **`dashboard/package.json`**: **três** entradas novas em `devDependencies` — `jsdom`, `@testing-library/react` e o
  peer dele, `@testing-library/dom`, que é entrada própria no arquivo. Nada em `dependencies` — nada vai para o
  bundle.
- **`dashboard/vite.config.ts`**: **inalterado**, e isso é requisito, não acaso.
- **Arquivos de teste novos (4)**: `src/App.test.tsx`, `src/features/auth/AuthContext.test.tsx`,
  `src/features/documents/DocumentDetail.test.tsx`, `src/features/groups/NoteDialog.test.tsx`.
- **Ajudante de teste novo (1)**: `src/test/renderWithProviders.tsx` — a árvore de providers do `main.tsx`, para os
  testes que precisam dela. Fica em `src/` porque o `tsconfig.json` só inclui `src`; não é importado por nada de
  produção, então não entra no bundle.
- **Comentários de código que ficaram falsos**: `features/shellState.ts` e `features/auth/sessionState.ts` — a frase
  sobre a ausência da stack sai; a razão da função pura fica, re-ancorada.
- **Spec**: um delta, `module-navigation`, um requisito modificado.
- **Docs**: `docs/STATUS.md` ganha a sessão desta change e perde, se houver, a anotação do limite como dívida aberta.
- **Servidor**: nada. Nenhum `.cs`, nenhum `.csproj`, nenhum endpoint. `dotnet build` e `dotnet test` não são tocados
  por esta change — rodados mesmo assim na verificação final, como guarda.
- **Suíte**: cresce em 4 arquivos. Os 14 de hoje continuam no ambiente `node`, com as mesmas 154 asserções. Como isso é
  conferido está no grupo 1 do `tasks.md`, com os números de baseline medidos em 2026-10-15.
