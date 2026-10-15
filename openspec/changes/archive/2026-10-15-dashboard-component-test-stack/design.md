# Design — stack de teste de componente no dashboard

## Context

O que está no repositório hoje, conferido em 2026-10-15 e não presumido.

**A suíte.** `dashboard/package.json` tem como dependências de teste apenas `vitest@^3.2.7`. Não há `jsdom`, não há
`@testing-library/*`. `dashboard/vite.config.ts` não declara `test.environment` — o vitest cai no padrão **`node`**. A
única configuração de teste ali é `test: { css: { include: [/tokens\.css/] } }`, para o `palette.test.ts`.

**Baseline medido** (`npm test`, 2026-10-15, nesta máquina):

```
Test Files  14 passed (14)
     Tests  154 passed (154)
  Duration  21.80s (transform 1.82s, setup 0ms, collect 24.55s, tests 151ms, environment 4ms, prepare 10.86s)
```

`environment 4ms` é o número que importa: no ambiente `node` o vitest praticamente não monta ambiente nenhum. Com jsdom
global, os 14 arquivos passariam a pagar esse custo.

**Os 14 arquivos são todos de função pura ou de dado.** Nenhum importa React. Os seis que o escopo desta change promete
não tocar são `shellState`, `sessionState`, `rawJson`, `modules`, `sessionCache` e `connectorPanelRows`.

**O que não é testável hoje.** `App.tsx`, `AuthContext.tsx`, `DocumentDetail.tsx` e `NoteDialog.tsx` são componentes
React: sem DOM e sem renderizador, nenhuma asserção os alcança.

**O fato técnico que condiciona tudo.** `api/client.ts:31` executa `const BASE = resolveApiBase()` **no import**, e a
`resolveApiBase` lê `window.__FISCALHUB__` e `window.location.hostname` (`api/config.ts:24,29`). No ambiente `node` o
import do `client.ts` quebra no carregamento. Foi exatamente por isso que a `ApiError` nasceu em `api/ApiError.ts`, e
não dentro do `client.ts` — está escrito no D13 do `design.md` de `dashboard-info-failure`.

**A regra de dependência do repositório não é afetada.** Tudo aqui vive em `dashboard/`. Nenhum projeto .NET, nenhuma
porta, nenhum adapter.

---

## Decisions

### D1 — A lista de ligações é fechada, e o critério dela é verificável

A lista não é "o que seria bom testar". É **onde um limite foi anotado por escrito, ou onde um defeito real passou**:

| Ligação | Origem |
|---|---|
| `App.tsx`, o portão de sessão e os ramos da casca | tarefas 1.6, 4.0, 4.8 e 4.10 de `connector-navigation-and-admin-json`; tarefas 3.4 e o limite de `dashboard-info-failure` |
| `AuthContext` — cache na troca de sessão | defeito 1 de 2026-10-15 (prova manual 6.4) |
| `AuthContext` — 401 contra 5xx/rede/timeout, e o "tentar de novo" | defeito 2 de 2026-10-15 (prova manual da `dashboard-info-failure`) |
| `DocumentDetail` e `NoteDialog` — os dois botões | defeito 1, do lado da tela |

O critério é o que torna a lista **fechável**: quando um item da lista não tem mais origem escrita, ele sai; quando
alguém quiser acrescentar um, precisa mostrar o limite ou o defeito. Sem isso, "stack de teste de componente" vira
convite aberto para testar o dashboard inteiro, que é outra fatia e outro custo.

### D2 — jsdom **por arquivo**, nunca global

`// @vitest-environment jsdom` na primeira linha de cada arquivo novo. **Nenhuma chave `test.environment` no
`vite.config.ts`.**

Não é gosto. São três consequências concretas de globalizar:

1. **Custo.** Os 14 arquivos de hoje passariam a montar um DOM que nenhum deles usa.
2. **A razão do `ApiError.ts` evapora.** Com jsdom global, importar o `client.ts` num teste deixaria de quebrar, e o
   D13 da change anterior viraria uma decisão sem motivo aparente — alguém "limparia" a classe de volta para dentro do
   `client.ts`, e o próximo teste em ambiente `node` quebraria de novo, sem ninguém entender por quê.
3. **O ambiente deixaria de ser visível no arquivo.** Com o pragma, quem abre o teste vê em que mundo ele roda. Com a
   chave global, o mundo é invisível e silenciosamente diferente do que o nome do arquivo sugere.

**Consequência aceita:** quatro linhas de pragma repetidas. É o preço, e é barato.

### D3 — Como cada teste alimenta a API: três mecanismos, escolhidos por ligação

Não há um mecanismo só, porque as três ligações precisam de coisas diferentes. A regra é **usar o mais real que
resolva**:

| Precisa de | Mecanismo | Onde |
|---|---|---|
| uma **resposta** já em mãos | `qc.setQueryData(['info'], …)` no `QueryClient` do teste | ramo `panel` do `App`; os dois botões |
| uma **falha** ou uma espera | `vi.stubGlobal('fetch', …)`, com `retry: false` no `QueryClient` do teste | ramo `failure` e `loading` do `App` |
| o **erro de verdade**, com status | `vi.stubGlobal('fetch', …)` — o `getJson` real roda e produz a `ApiError` real | `AuthContext`: 401 contra 5xx |

**Por que `fetch` e não `vi.mock('../api/client')`.** No `AuthContext` a coisa sob teste **é** a cadeia: `fetch` →
`getJson` → `ApiError(401)` → `sessionFromCheckError` → `tokenStore.set(null)`. Mockar o módulo do cliente cortaria
justamente o pedaço onde o defeito morava, e o teste passaria a provar o dublê. Stubando o `fetch`, o `tokenStore` real
grava no `localStorage` real do jsdom, e a asserção é sobre o token de verdade.

**Por que `setQueryData` onde dá.** É o idioma que o `sessionCache.test.ts` já usa (`sessionCache.test.ts:26`), é
instantâneo e não tem rede nenhuma para esperar.

**Armadilha a evitar, escrita aqui porque custa caro descobrir depois:** o `QueryClient` padrão **repete 3 vezes com
backoff exponencial**. Um teste de falha do `/info` com o cliente padrão esperaria segundos e ficaria instável. Todo
`QueryClient` de teste nasce com `defaultOptions: { queries: { retry: false } }`. Isso é configuração **do teste** —
nada em produção muda, e o `useInfo` não ganha `retry`.

### D4 — Sem `@testing-library/jest-dom`

Ele dá açúcar (`toBeInTheDocument`). O que precisamos afirmar é "o botão existe" e "o botão não existe", e
`expect(screen.queryByRole('button', { name: /Visualizar JSON/ })).toBeNull()` já diz isso com o que o vitest tem. Uma
dependência a menos, e uma a menos para manter alinhada com a versão do vitest.

### D5 — No `App.test.tsx`, o `useAuth` é dublê; no `AuthContext.test.tsx`, o provider é real

Os dois arquivos testam ligações **diferentes**, e misturá-los faria cada teste provar duas coisas e falhar por três
razões.

- **`App.test.tsx`** prova *"dado o `session.kind`, qual tela"*. São quatro ramos e uma tabela. Dirigir isso pelo
  `AuthProvider` de verdade significaria encenar quatro cenários assíncronos de rede para afirmar quatro linhas
  síncronas. Então `vi.mock` do `useAuth`, e a tabela fica de quatro casos.
- **`AuthContext.test.tsx`** prova *"dado o que a API responde, qual `session.kind` e o que acontece com o token e com
  o cache"*. Aqui o provider é o real, com um filho-sonda que só lê o contexto e escreve o estado na tela. Sem `App`,
  sem casca, sem MUI.

**Custo aceito, dito com todas as letras:** nenhum dos dois prova que o `App` está ligado ao `AuthProvider` de
verdade — isso é uma linha do `main.tsx`, e ela continua sem teste. É um limite menor que os seis que a change fecha, e
fica anotado no `tasks.md` em vez de virar e2e.

### D6 — O ramo `authenticated` monta o `Dashboard` inteiro, e é o maior custo da change

`App()` no estado autenticado devolve `<Dashboard />`, que puxa barra lateral, topbar, ícones do MUI e
`MODULE_SCREENS` — incluindo o `GroupsPage` e, por baixo dele, o `@mui/x-data-grid`. É a montagem mais cara da suíte
inteira.

Mitigação, e não evasão:

- o `/info` é **semeado** (D3), então não há rede a esperar;
- a asserção é **mínima e estável**: o `tenantId` da barra lateral e o rótulo de um item de navegação conhecido — e
  **não** o conteúdo da tabela, que é do `GroupsPage` e não desta change;
- as consultas do `GroupsPage` recebem um `fetch` stubado que responde lista vazia, para não ficarem pendentes.

**Se mesmo assim este caso ficar lento ou instável**, a saída já decidida é afirmar o ramo `authenticated` pela
**ausência** das outras três telas (nem carregando, nem falha, nem login) em vez de pela presença do painel completo —
o que ainda pega o desligamento da linha que escolhe, que é o que a change existe para pegar. A troca fica registrada
como desvio na tarefa, com o motivo.

### D7 — `renderWithProviders` duplica a árvore do `main.tsx`, de propósito

O `main.tsx` monta `QueryClientProvider → ThemeModeProvider → AuthProvider → App`. Os testes precisam de parte disso. A
alternativa — extrair a árvore do `main.tsx` para um componente reutilizável — seria **refatorar produção para servir o
teste**, que é não-objetivo declarado.

Então: um ajudante em `src/test/renderWithProviders.tsx`, com um `QueryClient` novo a cada chamada (`retry: false`,
`staleTime: Infinity`) e os providers que o caso pedir. **Risco aceito:** se o `main.tsx` ganhar um provider, o
ajudante não acompanha sozinho. É duplicação pequena, visível, e o teste quebra ruidosamente quando um provider
faltar — que é o modo bom de descobrir.

### D8 — Sem `StrictMode` nos testes

O `main.tsx` usa `StrictMode`, que em dev monta, desmonta e remonta — o efeito de restauração de sessão do
`AuthContext` roda duas vezes. Testar sob `StrictMode` provaria também a reentrância do efeito, o que é tentador.

Fica **fora**, por dois motivos: duplicar o efeito duplica as respostas que o `fetch` stubado precisa dar, e a
asserção passa a depender de ordem de montagem — ruído que não é a ligação sob teste. A reentrância do efeito é
coberta pelo `cancelled` do próprio `AuthContext` (`AuthContext.tsx:43,51,64`) e não é defeito conhecido. Se virar um,
é fatia própria.

### D9 — A prova por inversão é tarefa, e a evidência é a contagem

Um teste que passa não prova nada sobre o que ele pegaria. Então, para cada arquivo novo:

1. **desligar a ligação** no código de produção — e não o teste;
2. rodar `npm test` e **registrar quantos testes falham e quais**;
3. **restaurar** e conferir o arquivo por **hash**;
4. rodar de novo e registrar o verde.

É o método que este repositório já usa (tarefas 1.2, 4.8 e 4.10 de `connector-navigation-and-admin-json`, com hash
anotado). O desligamento de cada arquivo está escrito na tarefa, para não ser escolhido na hora de um jeito que o
teste já pegaria por acaso:

| Arquivo | O desligamento |
|---|---|
| `App.test.tsx` (portão) | trocar `if (session.kind === 'failed')` por um ramo que cai no login |
| `App.test.tsx` (casca) | passar `isError: false` fixo ao `shellState` no `App.tsx` |
| `AuthContext.test.tsx` (classificação) | trocar `sessionFromCheckError(error)` por `{ kind: 'anonymous' }` com `tokenStore.set(null)` — o `catch` genérico de antes |
| `AuthContext.test.tsx` (cache) | apagar a chamada `resetSessionCache(queryClient)` do `login` e do `logout` |
| `AuthContext.test.tsx` (tentar de novo) | tirar o `attempt` da lista de dependências do efeito |
| `DocumentDetail.test.tsx` / `NoteDialog.test.tsx` | trocar `canViewRawJson(info) && …` por `true && …` |

**Vermelho de compilação não conta.** Se o desligamento não compilar, ele está errado: troque por um que compile e
produza o comportamento defeituoso.

### D10 — Como se confere que a suíte de hoje não mudou nem ficou mais lenta

Quatro verificações, nessa ordem. Os números de referência são os do **Context**.

1. **Comportamento.** Os 14 arquivos continuam verdes, com as mesmas contagens por arquivo. O total passa de 154 para
   154 + os novos; se algum dos 14 mudar de contagem, alguma coisa vazou.
2. **Ambiente.** `grep` no `vite.config.ts` mostrando que não existe `test.environment`, e `grep` mostrando que as
   únicas declarações de ambiente são os 4 pragmas `// @vitest-environment jsdom`.
3. **Custo do ambiente.** O agregado `environment` do `npm test` sai de `4ms` e sobe. O que se exige é que ele seja
   **atribuível aos 4 arquivos novos**: com jsdom global, os 14 antigos também pagariam. Registra-se o número.
4. **Tempo, medido onde dá para medir.** `Duration` nesta máquina é dominada por `prepare 10.86s` e `collect 24.55s`
   para `tests 151ms` — é ruído, e fingir um limiar sobre ele seria teatro. A medida honesta é rodar **só os 14
   arquivos de hoje** depois da instalação (`npx vitest run src/features src/theme --exclude '**/*.test.tsx'`, ou a
   lista explícita) e comparar com o baseline. Se os 14 sozinhos não ficaram mais lentos, a instalação não os onerou, e
   o resto do relógio é dos arquivos novos — que é o custo que a change está comprando conscientemente.

---

## Assumptions

- **S1 — `@testing-library/react` v16 com React 18.3 exige o peer `@testing-library/dom`.** As versões exatas se
  resolvem na instalação; a restrição é compatibilidade com React 18.3 e vitest 3.2, e nenhum pacote entra em
  `dependencies`.
- **S2 — o jsdom dá `localStorage` e `sessionStorage`**, que é o que o `tokenStore` usa (`client.ts:37-44`). Se não
  desse, a ligação 2 não teria como ser testada sem mexer em produção — e aí seria achado, não refactor.
- **S3 — nenhum arquivo de produção muda de comportamento.** As únicas edições em `src/` do dashboard são dois
  comentários que ficaram falsos (`shellState.ts`, `sessionState.ts`). Se um teste exigir mais que isso, **pare e
  diga** (não-objetivo declarado no `proposal.md`).
- **S4 — o `tsconfig.json` inclui `src` inteiro**, então os arquivos de teste e o `renderWithProviders` são
  type-checked pelo `npm run build`, sob `strict`, `noUnusedLocals` e `noUnusedParameters`. Eles têm que passar no
  `tsc --noEmit` como qualquer outro arquivo.
- **S5 — nada de `dashboard/` entra no bundle por existir.** O Vite empacota o que o `index.html` alcança; `*.test.tsx`
  e `src/test/` não são importados por produção.
- **S6 — a lista nomeada é a fatia inteira.** Se ao escrever um teste aparecer um defeito novo, ele é **achado**, e não
  conserto desta change: consertar aqui repetiria o erro de misturar fatias que a `dashboard-info-failure` já pagou uma
  vez.

---

## Open Questions

### Q1 — O requisito novo cabe mesmo na spec publicada? *(decidido; reversível)*

**Decisão tomada:** sim, e em **um** lugar só — o requisito "Sem saber quem é o usuário e o que o tenant tem, o
dashboard não desenha o painel", de `module-navigation`. A razão está no `proposal.md`, em "Capabilities": uma frase
publicada fica **falsa**, e o requisito tem um buraco que os dois defeitos atravessaram. O precedente de obrigação de
teste em spec já existe (`platform-response-trace:214`).

**O que é discutível:** se o Marcelo achar que obrigação de teste não é texto de spec de produto, o delta inteiro sai e
a change vira **tarefas + docs**; nesse caso a frase falsa da spec precisa sair do mesmo jeito, só que como correção de
texto sem bullet novo. Os dois caminhos estão escritos; trocar é apagar um bullet e um punhado de cenários.

### Q2 — O ramo `authenticated` vale o que custa? *(medida, e respondida: vale)*

É o único caso que monta o dashboard inteiro (D6). O caminho principal e o alternativo estão decididos; o que não dá
para saber antes de medir é se o principal fica rápido o bastante. **Não bloqueia** — a tarefa registra o tempo e o
desvio, se houver.

**Medido em 2026-10-15 (tarefa 2.3): o caminho principal fica, sem desvio.** O caso que monta o `Dashboard` inteiro
— barra lateral, topbar, ícones do MUI, `GroupsPage` e, por baixo dele, o `@mui/x-data-grid` — custou **603 ms** no
primeiro run e **378–525 ms** nos seguintes, sem instabilidade em cinco execuções (as duas das inversões incluídas).
As duas mitigações previstas bastaram: o `/info` semeado e o `fetch` stubado respondendo lista vazia às consultas do
`GroupsPage`. Nenhuma montagem precisou de polyfill (o `@mui/x-data-grid` subiu no jsdom sem `ResizeObserver`
próprio), e a asserção ficou como o D6 queria: pela **presença** do painel — o `tenantId` da barra lateral, o rótulo
de um item de navegação e o rótulo do conector que veio na resposta —, e não pela ausência das outras telas.

### Q3 — A linha do `main.tsx` que liga o `App` ao `AuthProvider` continua sem teste

Consequência direta de D5. Testá-la exigiria montar `main.tsx` inteiro, ou e2e — os dois fora de escopo. Fica anotado
como limite conhecido, e não como dívida a pagar nesta fatia.
