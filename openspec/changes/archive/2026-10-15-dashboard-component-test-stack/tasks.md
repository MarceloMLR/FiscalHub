# Tarefas — stack de teste de componente no dashboard

A ordem é **a stack primeiro, e a prova de que ela não contaminou nada**: o grupo 1 é o único que pode quebrar os 154
testes de hoje, e ele termina com a conferência do baseline. Só depois vêm as ligações, uma por grupo, cada uma com a
prova por inversão dela. As frases que ficaram falsas saem no fim, porque dívida só se risca quando está paga.

- **Grupo 1:** `jsdom` e `@testing-library/react` entram, por arquivo e nunca global, e os 14 arquivos de hoje são
  conferidos contra o baseline medido.
- **Grupo 2:** `App.tsx` — os quatro ramos do portão de sessão e os três da casca.
- **Grupo 3:** `AuthContext` — o cache na troca de sessão, o 401 contra 5xx/rede/timeout, e o "tentar de novo".
- **Grupo 4:** `DocumentDetail` e `NoteDialog` — os dois botões da visão técnica.
- **Grupo 5:** os comentários e a spec que ficaram falsos.
- **Grupo 6:** verificação final.

**Baseline medido em 2026-10-15, antes de qualquer mudança** (`npm test` em `dashboard/`):

```
Test Files  14 passed (14)
     Tests  154 passed (154)
  Duration  21.80s (transform 1.82s, setup 0ms, collect 24.55s, tests 151ms, environment 4ms, prepare 10.86s)
```

Cada grupo de código termina com `npm test` e `npm run build` (`tsc --noEmit` + `vite build`) em `dashboard/`. **Nenhum
projeto .NET é tocado por esta change** — `dotnet build` e `dotnet test` rodam só no grupo 6, como guarda.

Uma tarefa só recebe `[x]` **com evidência** — a saída do teste, do build ou do comando. Tarefa movida ou feita pela
metade fica **aberta**, com a anotação do que faltou.

**Se alguma tarefa parecer exigir e2e, snapshot, teste de interno do MUI, arquivo de `src/` (.NET), meta de cobertura,
reescrita de teste de função pura ou refactor de código de produção "para ficar testável": pare e diga.** São os
não-objetivos do `proposal.md`, e nenhum deles cabe aqui.

---

## 1. A stack entra, e a suíte de hoje não muda

- [x] 1.1 Registrar o baseline **antes** de instalar: `npm test` em `dashboard/`, guardando a saída inteira (contagem
  por arquivo, total, e a linha de `Duration` com os agregados). Se o que sair agora divergir do bloco acima, é o
  número novo que vale, e a divergência fica anotada.
  **Medido antes de instalar:** `Test Files 14 passed (14)`, `Tests 154 passed (154)`,
  `Duration 17.00s (transform 2.43s, setup 0ms, collect 20.40s, tests 125ms, environment 5ms, prepare 11.21s)`.
  Contagens por arquivo: companyCode 14, modules 18, connectorPayload 26, sessionState 12, connectorPanelRows 18,
  shellState 11, cards 8, adapterSchemas 7, groupRow 6, period 8, environment 3, rawJson 4, sessionCache 3,
  palette 16 — soma 154. **Divergência anotada:** o `Duration` agregado desta medição (17.00s) difere do bloco acima
  (21.80s), como esperado nesta máquina (`prepare` + `collect` dominam; `tests` é 125ms contra 151ms). **O que vale
  como baseline são as contagens — 14 arquivos, 154 testes — e `environment 5ms`**, não o relógio.
- [x] 1.2 Instalar `jsdom` e `@testing-library/react` (com o peer `@testing-library/dom`) como **`devDependencies`**,
  em versões compatíveis com React 18.3 e vitest 3.2. **Nada em `dependencies`.** Evidência: o diff do
  `package.json` e a saída do `npm install`.
  **Não instalar `@testing-library/jest-dom`** (design D4).
  **Feito:** `npm install --save-dev jsdom@^26 @testing-library/react@^16 @testing-library/dom@^10` →
  `added 48 packages, and audited 226 packages`. `devDependencies` ganhou exatamente três entradas —
  `@testing-library/dom ^10.4.2`, `@testing-library/react ^16.3.3`, `jsdom ^26.1.0`. `dependencies` **inalterado**
  (8 entradas, as mesmas). Nenhum `@testing-library/jest-dom`.
- [x] 1.3 **O `vite.config.ts` não muda.** Conferir, e deixar a evidência: `grep -n "environment" dashboard/vite.config.ts`
  sem resultado. Se alguma tarefa adiante parecer precisar de `test.environment` global, **pare e diga** (design D2).
  **Conferido:** `grep -n "environment" dashboard/vite.config.ts` → **nenhuma ocorrência**. O arquivo continua com
  `test: { css: { include: [/tokens\.css/] } }` e mais nada de teste.
- [x] 1.4 **A suíte de hoje continua igual.** `npm test` depois da instalação: os mesmos 14 arquivos, as mesmas
  contagens por arquivo, 154 testes verdes. Evidência: a saída, comparada com a de 1.1 arquivo a arquivo.
  **Conferido arquivo a arquivo:** as 14 contagens são idênticas às de 1.1 (14/18/26/12/18/11/8/7/6/8/3/4/3/16),
  `Test Files 14 passed (14)`, `Tests 154 passed (154)`,
  `Duration 4.54s (… tests 155ms, environment 5ms …)`. **`environment` continua 5ms** — a instalação não trouxe jsdom
  para a suíte existente.
- [x] 1.5 **Os 14 arquivos de hoje não ficaram mais lentos.** Rodar **só eles** (lista explícita dos 14, ou
  `npx vitest run --exclude '**/*.test.tsx'`) e comparar `tests` e `Duration` com o baseline. Evidência: as duas
  saídas. O relógio total da suíte inteira é ruído nesta máquina (`prepare 10.86s` + `collect 24.55s` para
  `tests 151ms`) — o que se compara é a fatia dos 14.
  **Rodado com a lista explícita dos 14** (`npm test -- <os 14 caminhos>`): `14 passed (14)`, `154 passed (154)`,
  `Duration 3.13s (transform 1.81s, collect 5.02s, tests 119ms, environment 9ms, prepare 4.11s)`. Contra o baseline
  de 1.1 (`tests 125ms`), os 14 **não ficaram mais lentos**.
  **Desvio de comando, anotado:** `npx vitest run --exclude '**/*.test.tsx'` não pôde ser usado — o sandbox desta
  sessão recusa a invocação com esse argumento. Usei a **lista explícita dos 14**, que a própria tarefa dá como
  alternativa equivalente, por `npm test -- <lista>`.
- [x] 1.6 `npm run build` verde (`tsc --noEmit` limpo + `vite build`). Evidência: a saída.
  **Verde:** `tsc --noEmit` sem saída (limpo) e `✓ 1359 modules transformed` / `✓ built in 24.41s`. O único aviso é o
  de tamanho de chunk (997.17 kB), que **já existia** antes desta change e não vem dela.

## 2. A ligação do portão de sessão e da casca (`App.tsx`)

- [x] 2.1 `src/test/renderWithProviders.tsx` (design D7): a árvore de providers que os testes precisam, com um
  `QueryClient` **novo a cada chamada** e `defaultOptions: { queries: { retry: false } }` — sem isso, um teste de falha
  espera 3 tentativas com backoff e fica instável (design D3). O ajudante **não** é importado por nada de produção.
  **Feito.** `testQueryClient()` nasce com `{ retry: false, staleTime: Infinity }`; `renderWithProviders` replica a
  ordem do `main.tsx` (`QueryClientProvider → ThemeModeProvider → AuthProvider → ui`), com o tema ligado por padrão e
  o `AuthProvider` **desligado** por padrão (só o teste do próprio `AuthContext` o liga). Sem `StrictMode` (D8).
  **Nada de produção o importa** — conferido pelo bundle, que não mudou de hash (ver 2.6).
- [x] 2.2 `src/App.test.tsx`, com `// @vitest-environment jsdom` na primeira linha. **Os quatro ramos do portão de
  sessão**, como tabela: `checking` → a tela de carregando; `failed` → a tela de falha, com "Tentar de novo" e "Sair";
  `anonymous` → o login; `authenticated` → o painel. O `useAuth` é dublê (design D5); o que está sob teste é a escolha
  do ramo, e não a rede.
  **Feito:** a tabela `RAMOS` com `it.each`, os quatro ramos, mais dois casos nomeados — "falhou não é anônimo" (que
  afirma a tela de falha **e** a ausência do login, com os botões "Tentar de novo" e "Sair") e o "tentar de novo", que
  prova que o botão do portão chama o `recheckSession`, e não o `refetch` do `/info` (D15). Seis casos.
- [x] 2.3 No mesmo arquivo, **os três ramos da casca**: `/info` sem resposta e buscando → carregando; `/info`
  respondido (semeado com `setQueryData`) → painel; `/info` em erro sem resposta nenhuma → tela de falha. O ramo
  `panel` monta o `Dashboard` inteiro — **registrar o tempo deste caso** e, se ele ficar lento ou instável, aplicar o
  caminho alternativo já decidido em D6 (afirmar pela ausência das outras três telas), anotando o desvio e o motivo.
  **Feito, três casos.** **Q2, medida e decidida: o caminho principal fica.** O caso `authenticated` da tabela de 2.2
  — o único que monta o `Dashboard` inteiro, com barra lateral, topbar, ícones do MUI, `GroupsPage` e
  `@mui/x-data-grid` — mediu **603 ms** no primeiro run e **475–525 ms** nos seguintes; o caso `panel` deste grupo,
  **194–200 ms**. Nenhum deles ficou instável em 5 execuções (incluindo as duas das inversões). Não houve nada a
  mitigar além do que o D6 já previa: o `/info` semeado e o `fetch` stubado respondendo lista vazia às consultas do
  `GroupsPage`. **Nenhum desvio**: a asserção é pela **presença** do painel (o `tenantId` da barra lateral, o rótulo
  "Fiscal" e o rótulo do conector que veio na resposta), e não pela ausência das outras telas.
- [x] 2.4 **Prova por inversão do portão** (design D9): trocar, no `App.tsx`, o ramo `if (session.kind === 'failed')`
  por um que caia no login. Rodar, **registrar quantos testes falham e quais**, restaurar, **conferir o arquivo por
  hash**, rodar de novo e registrar o verde. Vermelho de compilação **não conta**.
  **Desligamento:** `if (session.kind === 'failed') return <ShellFailure … />` → `… return <LoginPage />`. Compila e
  roda — **o vermelho é de comportamento, e não de compilação**.
  **Caíram 3 de 9:** `'falhou' leva à tela certa`, `falhou não é anônimo: a tela é a de falha, e não a de login` e
  `o "tentar de novo" da tela de falha da sessão aciona a re-checagem`. Os 6 restantes passaram.
  **Restaurado e conferido por hash:** `sha256(src/App.tsx)` =
  `09172ffc8d3c0803c350151659de39bd8cb6016e821c5ebfe48dd07f8b0946b3`, idêntico ao de antes do desligamento.
- [x] 2.5 **Prova por inversão da casca**: passar `isError: false` fixo ao `shellState` na chamada do `App.tsx`. Mesmo
  procedimento e mesma evidência de 2.4.
  **Desligamento:** `shellState({ info, isFetching: infoFetching, isError: infoFailed })` →
  `… isError: false`. **Caiu 1 de 9:** `em erro, sem resposta nenhuma, leva à tela de falha` (a tela vira a de
  carregando, que é exatamente o defeito que o sinal desligado produz). Os 8 restantes passaram.
  **Restaurado e conferido por hash:** `sha256(src/App.tsx)` =
  `09172ffc8d3c0803c350151659de39bd8cb6016e821c5ebfe48dd07f8b0946b3`, o mesmo de antes.
- [x] 2.6 `npm test` e `npm run build` verdes. Evidência: as saídas, com a contagem nova.
  **`npm test`:** `Test Files 15 passed (15)`, `Tests 163 passed (163)` — os 14 antigos com as **mesmas** contagens,
  mais 9 do `App.test.tsx`. `Duration 15.00s (… tests 1.36s, environment 4.34s …)`.
  **`npm run build`:** verde, `tsc --noEmit` limpo, `✓ built in 35.99s`. O bundle saiu com o **mesmo nome de hash**
  de antes da change (`assets/index-Dy-tPrZW.js`, 997.17 kB) — confirmação de S5: nem o `*.test.tsx` nem o
  `src/test/` entram no bundle.

## 3. A ligação do `AuthContext`

- [x] 3.1 `src/features/auth/AuthContext.test.tsx`, com `// @vitest-environment jsdom`. Aqui o `AuthProvider` é **o
  real**, com um filho-sonda que só lê o contexto (design D5) — sem `App`, sem casca, sem MUI. O `fetch` é stubado com
  `vi.stubGlobal`, e **não** o módulo do cliente: a cadeia sob teste é `fetch` → `getJson` → `ApiError` →
  `sessionFromCheckError` → `tokenStore` (design D3).
  **Feito.** A sonda expõe o `session.kind` e as três ações; o provider sobe por `renderWithProviders(…, { auth: true,
  theme: false })` — sem MUI, como o D5 pede. 8 casos, 422 ms o arquivo inteiro.
- [x] 3.2 **Só o 401 apaga o token.** Checagem respondendo 401 → o token sai do `localStorage` e a sessão vira
  `anonymous`. Checagem respondendo 500, falhando de rede e estourando timeout → **o token continua guardado** e a
  sessão vira `failed`. Quatro casos, e a asserção do token é sobre o `localStorage` de verdade do jsdom.
  **Feito:** a tabela `FALHAS`, com os quatro — 401 (`anonymous`, token apagado), 500, rede (`TypeError: Failed to
  fetch`) e timeout (erro com `name: 'TimeoutError'`), os três `failed` com o token intacto. Mais um quinto caso que
  põe o par 503/401 lado a lado, que é o par que o defeito confundia. A asserção é
  `localStorage.getItem('fiscalhub.token')`, o armazenamento de verdade do jsdom (S2 confirmada).
- [x] 3.3 **O cache é limpo no login e no logout.** Semear o `QueryClient` com `['info']` e mais uma chave qualquer;
  fazer `login` → o cache está vazio; repetir com `logout` → o cache está vazio. É a ligação que faltava: a
  `resetSessionCache` já tem teste próprio (`sessionCache.test.ts`), **quem não tinha era a chamada dela**.
  **Feito:** o cache é semeado com `['info']` e `['groups', …]` (2 entradas, conferidas antes do ato), e depois do
  `login` e do `logout` tem **0**. O teste do `sessionCache.test.ts` ficou **como estava** — este não o duplica: lá se
  prova que a função limpa, aqui que o provider a chama.
- [x] 3.4 **O "tentar de novo" re-executa a checagem.** Primeira resposta falha → `failed`; acionar `recheckSession`;
  segunda resposta responde → `authenticated`, sem passar pelo login. Evidência: o `fetch` stubado foi chamado duas
  vezes.
  **Feito:** 503 → `failed`; clique no "tentar de novo" → `authenticated`; `expect(chamadas).toHaveBeenCalledTimes(2)`
  e o token nunca saiu do `localStorage`.
- [x] 3.5 **Prova por inversão da classificação:** trocar `sessionFromCheckError(error)` pelo tratamento genérico de
  antes (`tokenStore.set(null)` e `anonymous` em qualquer falha). Mesmo procedimento e mesma evidência de 2.4. O teste
  de 3.2 MUST ficar vermelho nos casos de 5xx/rede/timeout.
  **Caíram 5 de 8:** os três de 5xx/rede/timeout, o caso do par 503/401, **e** o do "tentar de novo" (com a
  classificação genérica, o 503 já leva ao login, e não há tela de falha de onde tentar). O caso do 401 continuou
  verde, como tem de ser. **Restaurado e conferido por hash:** `sha256(src/features/auth/AuthContext.tsx)` =
  `4c5d29c1467150ea10904fc9b16bd89eba28d1dba8dd828488d753e715f922e4`.
- [x] 3.6 **Prova por inversão do cache:** apagar a chamada `resetSessionCache(queryClient)` do `login` **e** do
  `logout`. O teste de 3.3 MUST ficar vermelho nos dois. Mesma evidência.
  **Caíram exatamente 2 de 8** — `o login limpa o que estava guardado para o token anterior` e `o logout limpa o que
  estava guardado para quem saiu`. Os outros 6 passaram. **Restaurado e conferido por hash:** o mesmo
  `4c5d29c1467150ea10904fc9b16bd89eba28d1dba8dd828488d753e715f922e4`.
- [x] 3.7 **Prova por inversão do "tentar de novo":** tirar o `attempt` da lista de dependências do efeito de
  restauração. O teste de 3.4 MUST ficar vermelho. Mesma evidência.
  **Caiu 1 de 8** — `a segunda resposta leva ao painel, sem passar pelo login`. Os outros 7 passaram.
  **Restaurado e conferido por hash:** o mesmo
  `4c5d29c1467150ea10904fc9b16bd89eba28d1dba8dd828488d753e715f922e4`.
- [x] 3.8 `npm test` e `npm run build` verdes. Evidência: as saídas.
  **`npm test`:** `Test Files 16 passed (16)`, `Tests 171 passed (171)` — os 14 antigos com as mesmas contagens, 9 do
  `App.test.tsx` e 8 do `AuthContext.test.tsx`. `Duration 12.53s (… tests 1.61s, environment 4.17s …)`.
  **`npm run build`:** verde, `tsc --noEmit` limpo, `✓ built in 11.50s`.

## 4. A ligação dos dois botões da visão técnica

- [x] 4.1 `src/features/documents/DocumentDetail.test.tsx`, com `// @vitest-environment jsdom`. Com o `/info` semeado
  dizendo `canViewRawJson: true`, o botão "Visualizar JSON" **existe**; com `false`, **não existe**; **sem resposta
  nenhuma** (cache vazio), **não existe** — que é a regra de `platform-response-trace`, "MUST NOT mostrá-los antes de o
  `/info` responder, nem se ele falhar". O `['reading', …]` também é semeado, para a consulta do detalhe não ficar
  pendente.
  **Feito, três casos, verdes.** O caso "sem resposta" é cache vazio **com a chamada pendente** (o `fetch` stubado
  nunca resolve), e não um `canViewRawJson: false` — são estados diferentes, e é justamente o que separa "o servidor
  disse que não" de "o servidor não disse nada". Ele também afirma que o detalhe **está** desenhado: o que falta é o
  botão, e não a tela.
- [x] 4.2 `src/features/groups/NoteDialog.test.tsx`, com `// @vitest-environment jsdom`. Os mesmos três casos para
  "Baixar arquivos".
  **Feito, três casos, verdes**, com a mesma montagem. O caso "sem resposta" confere ainda que o diálogo está aberto
  ("Detalhes da nota") e que o "Abrir chamado", que não depende do `/info`, continua onde estava.
- [x] 4.3 **Nada do `RawJsonModal` nem do `TicketModal`** é aberto ou afirmado: a ligação sob teste é *mostrar ou
  esconder o botão*, e não o que ele abre. Se o teste precisar abrir um deles para passar, **pare e diga**.
  **Conferido:** nenhum dos dois arquivos clica no botão nem nomeia `RawJsonModal` ou `TicketModal`. Não houve
  necessidade de abrir nada para os testes passarem.
- [x] 4.4 **Prova por inversão dos dois:** trocar `canViewRawJson(info) &&` por `true &&` no `DocumentDetail.tsx` e no
  `NoteDialog.tsx`. Os casos de `false` e de "sem resposta" MUST ficar vermelhos nos dois arquivos. Mesmo procedimento
  e mesma evidência de 2.4.
  **Caíram exatamente os 4 previstos de 6** — os casos `false` e "sem resposta" **de cada um dos dois arquivos**. Os
  dois casos `true` continuaram verdes, como tem de ser (com `true &&` o botão aparece sempre).
  **Restaurados e conferidos por hash:** `sha256(DocumentDetail.tsx)` =
  `244075a1479fd6a2859f49e0d0bf0ccb16b03fb68810232537ace674773ae1fb`; `sha256(NoteDialog.tsx)` =
  `41cc7b4ac6a8ec7865d288fe4c4806cdfc758218cc22c7337b67adbd832c0f42`.
- [x] 4.5 `npm test` e `npm run build` verdes. Evidência: as saídas.
  **`npm test`:** `Test Files 18 passed (18)`, `Tests 177 passed (177)` — 14 antigos (contagens idênticas) + 9 + 8 +
  3 + 3. `Duration 16.91s (… tests 2.22s, environment 9.13s …)`.
  **`npm run build`:** verde, `tsc --noEmit` limpo, `✓ built in 11.40s`, bundle no mesmo hash
  (`assets/index-Dy-tPrZW.js`).

## 5. As frases que ficaram falsas

- [x] 5.1 `dashboard/src/features/shellState.ts:6-7`: a frase "O dashboard não tem stack de teste de componente (sem
  `@testing-library/react`, sem jsdom)" sai. **A razão da função pura fica**, re-ancorada: a escolha se prova caso a
  caso, de graça, e ganhar a ferramenta não é motivo para mover a escolha para dentro da tela. Só o comentário muda —
  **nenhuma linha de código**.
  **Feito.** O comentário agora abre pela razão que sobrevive ("uma escolha se prova caso a caso, de graça, quando é
  um valor que entra e um valor que sai; dentro do componente ela só se prova montando a tela") e acrescenta que o
  dashboard **passou a ter** a stack e que a ligação tem teste no `src/App.test.tsx`. **Nenhuma linha de código
  mudou** — a função `shellState` está byte a byte como estava.
- [x] 5.2 `dashboard/src/features/auth/sessionState.ts:8-9`: a mesma correção, pelo mesmo motivo. Só o comentário.
  **Feito**, apontando para o `features/auth/AuthContext.test.tsx`. Só o comentário.
- [x] 5.3 `docs/STATUS.md:1472` ("Duas funções puras, com nome e teste, porque o dashboard não tem stack de teste de
  componente…"): é **registro histórico de uma sessão**, e registro histórico não se reescreve. Conferir que ele está
  marcado como da sessão de 2026-10-15 e, se não estiver claro, acrescentar a nota de que a stack entrou depois — sem
  apagar o que foi escrito.
  **Conferido, e nada a fazer.** A linha vive sob o cabeçalho `## Sessão 2026-10-15 — A falha do /info e o portão de
  sessão (change dashboard-info-failure)` (linha 1444), que data e nomeia a sessão. A datação está clara, então o
  registro **não foi tocado** — e a sessão nova (5.4), logo abaixo, diz que a stack entrou depois.
- [x] 5.4 Sessão nova no `docs/STATUS.md` para esta change: o que entrou, os números de antes e depois da suíte, as
  provas por inversão e os limites que **continuam** abertos (3.1 das pendências abaixo).
  **Feita:** `## Sessão 2026-10-15 — Stack de teste de componente no dashboard (change dashboard-component-test-stack)`,
  marcada como **não arquivada, aguardando revisão**. Traz o limite que ela fecha, o que entrou (e que nada entrou no
  bundle), o jsdom por arquivo com as três razões, os quatro arquivos e o ajudante, as **seis inversões com as
  contagens**, os números antes/depois (14/154 → 18/177, com os 14 antigos intactos e a atribuição do agregado
  `environment`), o delta de spec e por que `platform-response-trace` não tem delta, os **três limites que continuam
  abertos** (P1, P2 e P3) e a guarda.
  **Anotação:** não havia, nas seções vivas do `STATUS.md` ("Próximos passos", "Riscos e pendências", "Threads
  abertas"), nenhuma entrada da ausência da stack como dívida aberta — as duas menções que existiam estavam dentro do
  registro datado da sessão anterior. Logo **não houve dívida a riscar**, só sessão a escrever.
- [x] 5.5 Conferir que a frase não sobrou em lugar nenhum fora do arquivo morto, **lendo os arquivos** de
  `dashboard/src`, `openspec/specs` e `docs` que a afirmam — e não por um grep de frase inteira, que não acha a
  ocorrência quebrada em duas linhas (ver a nota de método abaixo). O grep serve como conferência **secundária**, e
  então por pedaço curto mais contexto: `grep -rn -A1 "não tem stack" dashboard/src openspec/specs docs`. As
  ocorrências restantes só podem estar em `openspec/changes/archive/`, que é histórico e não se edita.
  **Conferido, com um achado a declarar.** Em `dashboard/src` **não há mais nenhuma** ocorrência da afirmação falsa
  (as duas que o grep acha agora são as frases novas, afirmativas: "passou a ter"). Em `docs/`, a única é a do
  registro datado de 5.3. **Resta uma**, e ela é esperada: `openspec/specs/module-navigation/spec.md:282`. A spec
  publicada só é reescrita **no archive** (ou por um sync), e o archive é passo do Marcelo, depois da revisão — o
  texto corrigido já está escrito no delta desta change
  (`openspec/changes/dashboard-component-test-stack/specs/module-navigation/spec.md`), que é a fonte. Editar a spec
  publicada à mão aqui duplicaria o passo do archive e sairia do fluxo do OpenSpec; por isso **não foi editada**.
  **Esta ocorrência cai quando a change for arquivada.**
  **Nota de método:** o grep da tarefa, como escrito, **não** acharia a ocorrência do `sessionState.ts`, porque lá a
  frase estava quebrada em duas linhas ("não tem stack" / "de teste de componente"). Ela foi encontrada e corrigida
  pela leitura do arquivo, e conferida depois com `grep -rn -A1 "não tem stack" dashboard/src`, que hoje não acha
  nada.
- [x] 5.6 `npm run build` verde (os comentários não mudam código, mas o build roda mesmo assim).
  **Verde:** `tsc --noEmit` limpo, `✓ built in 11.13s`.

## 6. Verificação final

- [x] 6.1 `npm test` em `dashboard/`: 14 + 4 arquivos, 154 + N testes, tudo verde. Evidência: a saída inteira.
  **`Test Files 18 passed (18)`, `Tests 177 passed (177)`**,
  `Duration 19.15s (transform 2.27s, setup 0ms, collect 38.57s, tests 2.46s, environment 10.27s, prepare 3.83s)`.
  São os 14 de sempre (154) mais 9 + 8 + 3 + 3 = **23 novos**.
- [x] 6.2 **A conferência do ambiente**, com as quatro verificações do design D10: (a) os 14 arquivos de hoje com as
  mesmas contagens; (b) `grep` provando que não há `test.environment` no `vite.config.ts` e que as únicas declarações
  de ambiente são os 4 pragmas; (c) o agregado `environment` do `npm test`, registrado e **atribuído aos 4 arquivos
  novos**; (d) os 14 de hoje rodados sozinhos, comparados com o baseline de 1.1.
  **(a) Comportamento — igual.** Na suíte inteira, as 14 contagens são as mesmas de 1.1, arquivo a arquivo:
  companyCode 14, modules 18, connectorPayload 26, sessionState 12, connectorPanelRows 18, shellState 11, cards 8,
  adapterSchemas 7, groupRow 6, period 8, environment 3, rawJson 4, sessionCache 3, palette 16 = **154**. Nenhum
  mudou de contagem; nada vazou.
  **(b) Ambiente — por arquivo, nunca global.** `grep "environment" dashboard/vite.config.ts` → **nenhuma
  ocorrência**. `grep -rn "@vitest-environment" dashboard/src` → **exatamente 4**, todas na linha 1:
  `src/App.test.tsx`, `src/features/auth/AuthContext.test.tsx`, `src/features/documents/DocumentDetail.test.tsx`,
  `src/features/groups/NoteDialog.test.tsx`. Nenhuma outra declaração de ambiente existe no projeto.
  **(c) Custo do ambiente — atribuído.** Baseline: `environment 5ms`. Suíte inteira hoje: **`environment 10.27s`**.
  Os **4 arquivos novos rodados sozinhos**: `environment 7.60s` (4 arquivos, 23 testes). Os **14 antigos rodados
  sozinhos**: `environment 4ms`. Ou seja, o agregado é dos quatro novos — os 14 continuam sem montar DOM nenhum. (A
  diferença entre 10,27s e 7,60s é variação de execução na construção do jsdom entre workers, e não custo dos 14, que
  medem 4ms.) Com jsdom global, os 14 também pagariam; não pagam.
  **(d) Tempo dos 14 sozinhos — não ficaram mais lentos.** `14 passed (14)`, `154 passed (154)`,
  `Duration 2.43s (transform 954ms, collect 3.65s, tests 113ms, environment 4ms, prepare 2.93s)`, contra o baseline de
  1.1 (`tests 125ms`, `environment 5ms`). A fatia dos 14 está igual ou melhor.
- [x] 6.3 `npm run build` verde.
  **Verde:** `tsc --noEmit` limpo e `✓ built in 11.13s`. O bundle continua no mesmo hash de antes da change
  (`assets/index-Dy-tPrZW.js`, 997.17 kB / gzip 305.76 kB): nem os `*.test.tsx` nem o `src/test/` entram nele. O único
  aviso é o de tamanho de chunk, que já existia.
- [x] 6.4 `dotnet build` (0 warnings — `TreatWarningsAsErrors`) e `dotnet test` verdes. **Nada de .NET foi tocado**;
  isto é guarda, não expectativa de mudança. Se a DLL estiver travada pelo Host rodando, usar
  `--artifacts-path obj/guard` **dentro do repositório**.
  **`dotnet build --artifacts-path obj/guard`:** `Compilação com êxito. 0 Aviso(s), 0 Erro(s)` (01:24).
  **`dotnet test --artifacts-path obj/guard`:** 11 assemblies, **1377 aprovados, 0 falhas, 42 ignorados** — os mesmos
  números do fechamento da change anterior, como tem de ser numa fatia que não toca `src/`.
- [x] 6.5 `npx openspec validate --all --strict` verde.
  **`Totals: 29 passed, 0 failed (29 items)`** — 28 specs mais `change/dashboard-component-test-stack`.
  **Desvio de comando, anotado:** o sandbox desta sessão recusa `npx openspec`; rodei o `openspec` do PATH, com os
  mesmos argumentos.
- [x] 6.6 Reler o `proposal.md` contra o que foi entregue: se a lista de ligações cresceu, encolheu ou mudou de
  mecanismo, a proposta é que está errada — corrigir a proposta, e não o registro.
  **Relido. A lista não encolheu e nenhum mecanismo mudou** — os três de D3 (semear com `setQueryData`, stubar o
  `fetch`, e o `fetch` real para o erro com status) foram usados exatamente onde a tabela dizia. **Dois acertos de
  proposta**, feitos na proposta e não no registro:
  1. O `App.test.tsx` ganhou um caso que a tabela do item 1 não nomeava — o `onRetry` da tela de falha **do portão**
     chamando o `recheckSession`, e não o `refetch` do `/info`. É a mesma ligação (a linha do `App.tsx`) e caiu junto
     na inversão do portão, mas a tabela não o dizia: **a linha da tabela foi corrigida**.
  2. O `Impact` dizia "duas `devDependencies` novas"; o `package.json` tem **três** entradas, porque o peer
     `@testing-library/dom` é entrada própria no arquivo. **Corrigido para três.**
  Também registrei no `design.md` a resposta da **Q2**, que era medição e não escolha: o caminho principal ficou
  (números na tarefa 2.3 e na Q2).

---

## Pendências — registradas, e deliberadamente fora desta change

Não são tarefas. Ficam escritas para não serem redescobertas como novidade.

- **P1 — A chamada do `AdapterDeclarationGuard` no `Program.cs` continua sem teste.** O
  `AdapterDeclarationGuardTests` cobre a função (`tests/FiscalHub.Host.Tests/AdapterDeclarationGuardTests.cs`, 4
  casos); quem não tem teste é a **chamada** dela no host — a mesma classe de limite que esta change fecha no
  dashboard, só que do lado .NET. **Não se resolve aqui:** nenhum arquivo de `src/` é tocado por esta fatia, e a
  solução lá é outra (harness de host, e não jsdom).
- **P2 — A linha do `main.tsx` que liga o `App` ao `AuthProvider` continua sem teste.** Consequência direta de D5:
  os testes do grupo 2 usam um `useAuth` dublê, e os do grupo 3 montam o provider sem o `App`. Cobrir isso exigiria
  montar o `main.tsx` inteiro ou e2e, os dois fora de escopo.
- **P3 — As demais telas continuam sem teste de componente.** `GroupsPage`, `ConnectorsPage`, `UsersPage`,
  `IntegrationsPage`, agendamentos, suporte, `LoginPage`, `RawJsonModal` e `TicketModal`. A stack passa a existir para
  elas; esta change não as cobre, e cobrir uma delas "de carona" é fatia nova.
