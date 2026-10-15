A ordem é **a decisão antes da tela**: primeiro a pergunta em aberto, que é a única coisa que bloqueia; depois a função
pura, que é a parte testável e o coração da fatia; depois o que a consulta precisa expor; e por último a tela, que é a
parte sem teste automatizado. Docs no fim, porque a dívida só pode ser riscada quando estiver paga.

- **Grupo 0:** a Open Question Q1, respondida e registrada na spec.
- **Grupo 1:** a função pura que escolhe o estado, com teste antes.
- **Grupo 2:** o `useInfo` expõe o que a decisão precisa.
- **Grupo 3:** a tela de falha, e o `App.tsx` renderizando o que a função disser.
- **Grupo 6:** o portão de sessão — o achado da prova manual (ver abaixo).
- **Grupo 4:** docs.
- **Grupo 5:** verificação final.

O **grupo 6** nasceu depois, do achado da prova manual de 2026-10-15 (`proposal.md`, "O mesmo defeito, uma camada
acima"; `design.md` D12 a D16). Ele fica **antes** dos grupos 4 e 5 na ordem de execução — docs e verificação final só
fecham com ele pronto —, e por isso aparece aqui fora de ordem numérica: o número 6 é o próximo livre, e **nada do que
já estava fechado com evidência foi renumerado**.

Esta change **não toca em nenhum projeto .NET**. Cada grupo que mexe no dashboard termina com `npm test` e
`npm run build` (que roda `tsc --noEmit` antes do Vite). **Se alguma tarefa parecer exigir mudança em `src/`, em
endpoint, no `staleTime`, nas invalidações, no `sessionCache`, no tratamento do 401 ou nos outros consumidores do
`/info`: pare e diga.** São os não-objetivos do `proposal.md`.

Uma tarefa só recebe `[x]` **com evidência** — a saída do teste, do build ou da execução. Tarefa movida ou feita pela
metade fica **aberta**, com a anotação do que faltou.

## 0. A pergunta que bloqueia a tela

- [x] 0.1 Levar a **Q1** do `design.md` ao Marcelo: *a tela de falha mantém a topbar com o menu do usuário, ou seja, dá
  para sair estando preso nela?* Os dois lados e o caminho do meio (botão "Sair" próprio, sem topbar) estão escritos
  lá. Registrar a resposta como **um bullet e um cenário** na requirement "Sem o `/info`, o dashboard não desenha o
  painel", em `specs/module-navigation/spec.md`, e revalidar com `openspec validate --all --strict`.
  **Bloqueia o grupo 3.** Os grupos 1 e 2 não dependem dela.
  **Respondida (Marcelo, 2026-10-15): a tela de falha tem o próprio botão "Sair" e não mantém a topbar.** Registrado
  como **D11** no `design.md` (com a razão: sair é local e funciona com o servidor fora; a topbar não teria título nem
  subtítulo para mostrar, e viraria uma terceira forma de casca), e na spec como **um bullet** ("Além de tentar de
  novo, a tela de falha MUST oferecer a saída… e MUST NOT desenhar a topbar do painel") **e um cenário** ("Sair da tela
  de falha"). A Q1 saiu das Open Questions, riscada, apontando para D11. Evidência:
  `openspec validate --all --strict` → `Totals: 29 passed, 0 failed`. **O grupo 3 está desbloqueado.**

## 1. A função pura que escolhe o estado

- [x] 1.1 **Teste primeiro.** Um teste de tabela sobre a função, em `dashboard/src/features/`, cobrindo os quatro ramos
  do design D4 e, explicitamente, os **três estados** do requisito: "ainda não respondeu" → carregando; "respondeu com
  árvore vazia" → painel; "falhou" → falha. O caso que prova a fatia: **"falhou" e "respondeu vazio" devolvem
  resultados diferentes** — é o defeito de hoje virando teste. Mais: "está buscando depois de um erro" (o tentar de
  novo) → **carregando**, e "erro com resposta em mãos" → **painel** (D6). Ele MUST falhar antes de 1.2 e passar
  depois: rodar e guardar as duas saídas.
  Feito em `dashboard/src/features/shellState.test.ts`: tabela de 8 casos (os quatro ramos do D4, com e sem resposta em
  mãos) + 3 testes nomeados — os três estados, `"falhou" ≠ "respondeu vazio"`, e o painel carregando a resposta.
  **Vermelho antes de 1.2:** `1 failed | 12 passed (13)`, `Cannot find module './shellState'`. **Verde depois:**
  `13 passed (13)`, `Tests 142 passed` (eram 131).
  **A ordem está travada, provado invertendo a produção** (e restaurando com conferência de hash
  SHA256 `0915B49C…31FC3E`, idêntico antes e depois):
  - `isError` no topo → quebram 2 casos: "buscando depois de um erro — o tentar de novo" e "recarga falhou, com
    resposta em mãos" (`Tests 2 failed | 9 passed`);
  - `isError` só antes de `isFetching` → quebra 1: "buscando depois de um erro" (`Tests 1 failed | 10 passed`).
- [x] 1.2 A função, pura e com nome, no mesmo diretório: recebe os sinais da consulta (a resposta, se está buscando, se
  terminou em erro) e devolve o que mostrar. Sem importar React, sem importar React Query — é o que a torna testável
  (D3). Comentário de decisão em português, citando por que a escolha não vive no componente.
  `dashboard/src/features/shellState.ts`: `shellState({ info, isFetching, isError })` devolve
  `{ kind: 'panel'; info } | { kind: 'loading' } | { kind: 'failure' }`. O `panel` **carrega a resposta**, e por isso o
  `App.tsx` desenha a partir dela, e não de um `info?.` que ninguém sabe se chegou. Nenhum import de React nem de
  React Query.
- [x] 1.3 `npm test` verde e `npm run build` sem erro. Evidência: as duas saídas.
  `npm test` → `Test Files 13 passed (13)`, `Tests 142 passed (142)`. `npm run build` → `tsc --noEmit` sem erro,
  `✓ 1353 modules transformed`, `✓ built in 32.37s`. (O aviso de chunk acima de 500 kB é anterior a esta change e não
  vem do diff dela.)

## 2. O `useInfo` expõe o que a decisão precisa

- [x] 2.1 `features/useInfo.ts` passa a devolver, além do que já devolve, o erro, se está buscando e a ação de refazer
  (`refetch`). **Não mexer no `staleTime: Infinity` nem na `queryKey`** — a change anterior deixou isso correto.
  Ele agora devolve `{ data, isFetching, isError, refetch }` — explícito, em vez do objeto inteiro do React Query: o
  que a casca precisa sai nomeado, e o resto não vaza para a tela. `staleTime: Infinity` e `queryKey: ['info']`
  intactos (diff de `useInfo.ts`: só o destructuring, o retorno e o comentário).
- [x] 2.2 Conferir que os outros consumidores (`ConnectorsPage`, `DocumentDetail`, `NoteDialog`) continuam compilando
  **sem mudança nenhuma** — eles leem só o `data`. Evidência: `npm run build` e o diff, que não os toca.
  `git diff --stat` = `dashboard/src/App.tsx` e `dashboard/src/features/useInfo.ts`, e mais nada; os três arquivos não
  aparecem. `npm run build` (com o `tsc --noEmit`) verde, o que prova que o `const { data: info } = useInfo()` dos três
  continua válido com o retorno estreitado.

## 3. A tela de falha e o `App.tsx`

**Depende do grupo 0.** — resolvido em 0.1 (D11: botão "Sair" próprio, sem topbar).

- [x] 3.1 O componente da tela de falha: a mensagem ("Não foi possível carregar o painel.", S3) e o
  botão "Tentar de novo", que chama o `refetch`. Ocupa a área inteira, como o `Loading` de hoje (S5). O que mais ele
  tem — menu do usuário, botão de sair — vem da resposta de 0.1.
  **A revisão do texto é parte da tarefa:** ele não pode nomear conector, módulo, agendamento, ambiente nem permissão
  (D7).
  `dashboard/src/features/InfoFailure.tsx`: área inteira sobre o `--page`, um cartão centrado com o ícone de
  desconectado, a frase **"Não foi possível carregar o painel."** e dois botões — "Tentar de novo"
  (`onRetry`, ligado ao `refetch`) e "Sair" (`onLogout`, ligado ao `logout`). Sem topbar, sem barra lateral, sem menu
  do usuário (D11). Texto revisto: a tela inteira tem **três** frases de produto ("Não foi possível carregar o
  painel", "Tentar de novo", "Sair") e nenhuma delas nomeia conector, módulo, agendamento, ambiente ou
  permissão — nem para negar. Nenhum "entrar de novo" (D8).
  **A frase mudou depois das verificações de tela** (decisão do Marcelo, 2026-10-15): ela era "Não foi possível
  carregar a configuração do painel." e perdeu a palavra "configuração". A razão está em D7 — nascida quando a fatia
  era só do `/info`, ela ficou imprecisa ao servir também a checagem de sessão, onde configuração nenhuma chega a ser
  pedida, e mandava o usuário conferir as próprias configurações. O que 3.4 e 6.7 observaram na tela **não muda**: o
  que aquelas verificações provaram é que a tela de falha aparece, que nenhum `<nav>` é desenhado e que "Sem conector"
  não aparece em lugar nenhum — e isso continua verdadeiro, com a frase mais curta. A troca é de texto, e o diff dela
  é uma linha do `ShellFailure.tsx`.
- [x] 3.2 O `App.tsx` passa a chamar a função de 1.2 e a renderizar o que ela disser. A linha 126 de hoje
  (`navigationGroups(infoLoading ? undefined : (info?.connectors ?? []))`) deixa de existir na forma atual: a árvore é
  montada **só no ramo em que há resposta**, e o `?? []` que traduzia falha para vazio sai.
  Agora: `const shell = shellState({ info, isFetching, isError })`, com `loading → <Loading />`,
  `failure → <InfoFailure …/>` e, só depois, `const config = shell.info` e
  `const groups = navigationGroups(config.connectors)`. O `?? []` e o `infoLoading ? undefined :` saíram; o
  `if (groups.length === 0) return <Loading />` também, porque havendo resposta a árvore nunca é vazia (a vazia vira o
  grupo que nomeia a ausência). Os `info?.` do selo de ambiente e dos selos da integração viraram `config.`, que não
  precisa perguntar se chegou.
- [x] 3.3 O comentário de dívida do `App.tsx` (hoje nas linhas 122-125, "uma **falha** do /info, porém, chega aqui como
  resposta vazia…") **sai**, porque deixou de ser verdade. No lugar, uma linha curta dizendo que a escolha mora na
  função, e por quê.
  Saiu inteiro, com a remissão ao `docs/STATUS.md` que ele carregava. No lugar, duas notas curtas: uma sobre a escolha
  morar no `shellState` (e por que a ordem dos sinais importa) e outra marcando que, dali para baixo, **há resposta** —
  e que era o `?? []` que fazia a barra lateral afirmar ausência de conector. **Nenhuma linha do arquivo descreve mais
  o comportamento que esta fatia apagou.**
- [x] 3.4 Verificação **manual**, que é o que substitui o teste de componente que não existe: subir o dashboard com a
  API desligada e conferir, com evidência (descrição do que apareceu, ou captura): (a) a tela de falha aparece e nenhum
  grupo de barra lateral é desenhado; (b) o botão tentar de novo mostra carregando; (c) com a API de volta, o painel
  desenha a árvore do tenant. **Limite anotado, o mesmo de 1.6, 4.0, 4.8 e 4.10 da change anterior:** a renderização
  continua sem teste automatizado — o que esta fatia garante por teste é a **escolha**, e não o desenho.
  **Aberta por decisão do Marcelo (2026-10-15): a verificação na tela é dele, com o host fora.** O código de 3.1 a 3.3
  está pronto e com a guarda verde; falta só a conferência manual.
- [x] 3.5 `npm test` verde e `npm run build` sem erro. Evidência: as duas saídas.
  Ao fim do grupo 6, com tudo junto: `npm test` → `Test Files 14 passed (14)`, `Tests 154 passed (154)`;
  `npm run build` → `tsc --noEmit` sem erro, `✓ 1359 modules transformed`, `✓ built in 20.41s`. (O aviso de chunk acima
  de 500 kB é anterior a esta change.) O componente desta tarefa mudou de nome no 6.5 — é `ShellFailure`, porque passou
  a servir às duas portas do portão; o que ele desenha é o mesmo.

## 6. O portão de sessão (achado da prova manual, 2026-10-15)

A tela que os grupos 1 a 3 construíram **não aparece** numa queda real: o `AuthContext` apaga o token em qualquer falha
do `/auth/me`, e o usuário cai no login antes de o `/info` chegar a falhar. É o mesmo defeito desta change, uma camada
acima. **Aprovado pelo Marcelo para entrar nesta fatia** (`design.md` D12).

Vale tudo o que vale para os outros grupos, e mais dois limites próprios: **não entra** refresh de token, renovação de
sessão nem política de expiração; e **o `setUnauthorizedHandler` não muda**. Se alguma tarefa parecer exigir isso,
**pare e diga**.

- [x] 6.1 Conferir, no `dashboard/src/api/client.ts`, **o que o erro do `api.me()` carrega** — se ele chega ao
  `AuthContext` com o status HTTP ou como `Error` genérico. Registrar o achado aqui. Se o status não chegar, fazê-lo
  chegar é parte da tarefa: classe de erro com `status`, lançada pelo caminho que o `api.me()` usa, **sem mudar a
  mensagem nem o fluxo de nenhuma outra chamada** (D13). A classe mora em arquivo próprio, e não dentro do `client.ts`,
  porque o `client.ts` resolve a URL base no import (lê o `window`) e os testes rodam em node.
  **O achado: o status não chegava.** O `api.me()` usa o `getJson`, que lançava `new Error('Sessão expirada.')` no 401
  e `new Error(\`${res.status} ${res.statusText}\`)` no resto — um `Error` comum, com o status **só dentro da string da
  mensagem**. Para a classificação, 401 e 500 eram indistinguíveis sem fazer `parseInt` da mensagem, que seria
  contrato por acidente. **O que mudou:** `dashboard/src/api/ApiError.ts`, com `ApiError extends Error` e um campo
  `status`, lançado pelo `getJson` nos dois caminhos de erro, **com as mensagens idênticas às de antes**. Diff do
  `client.ts`: o import, um comentário e as duas linhas de `throw` — nada mais. Os demais ajudantes (`sendJson`,
  `getJsonWithMessage`, `sendAdmin` e os `fetch` soltos) **não foram tocados**. Como `ApiError` é um `Error` e nenhum
  arquivo do dashboard faz `instanceof` (conferido por busca), quem já mostrava `e.message` não vê diferença.
- [x] 6.2 **Teste primeiro.** Teste de tabela da classificação do erro da checagem, em `dashboard/src/features/auth/`:
  401 → **anônimo**; 5xx, falha de rede, timeout e erro sem status → **falhou**. O caso que prova a fatia: **"401" e
  "5xx" não devolvem o mesmo resultado** — é o `catch` genérico de hoje virando teste. Ele MUST falhar antes de 6.3 e
  passar depois: rodar e guardar as duas saídas. **Vermelho de compilação é fraco aqui** (o módulo nem existe): provar
  a ordem invertendo a produção depois do verde — fazer a classificação devolver sempre "anônimo", que é literalmente
  o comportamento de hoje —, e restaurar conferindo o hash.
  Feito em `dashboard/src/features/auth/sessionState.test.ts`: tabela de 10 casos (401; 500, 502, 503 e 403; rede
  (`TypeError`); timeout (`AbortError`); erro sem status; objeto qualquer com `status: 401` que não veio do cliente;
  e a rejeição sem erro) + 2 testes nomeados — `"401" e "5xx" não dão no mesmo resultado` e `nenhuma falha que não
  seja 401 leva a anônimo`. **Vermelho antes de 6.3:** `Test Files 1 failed | 13 passed (14)`,
  `Cannot find module './sessionState'`. **Verde depois:** `Tests 154 passed (154)` (eram 142).
  **A regra está travada, provada invertendo a produção** (restaurada com conferência de hash SHA256
  `e12ab8ab…2766ec`, idêntico antes e depois):
  - devolver sempre `anonymous` — que é **literalmente** o `catch` genérico de hoje → `Tests 11 failed | 143 passed`;
  - devolver sempre `failed` → `Tests 2 failed | 152 passed` (o caso do 401 e o do contraste), o que prova que o ramo
    que **derruba** a sessão também está coberto, e não só o que a preserva.
- [x] 6.3 A classificação pura, com nome, e o **estado com nome** da sessão no mesmo módulo: checando, autenticado,
  anônimo e falhou (D14). Sem importar React. Comentário de decisão em português dizendo por que a escolha não vive no
  componente e por que o 401 é o único que derruba.
  `dashboard/src/features/auth/sessionState.ts`: `SessionState` com os quatro membros (`checking`, `authenticated` com
  o usuário junto, `anonymous`, `failed`) e `sessionFromCheckError(error)` → `{ kind: 'anonymous' }` só quando
  `error instanceof ApiError && error.status === 401`; todo o resto, `{ kind: 'failed' }`. Nenhum import de React.
- [x] 6.4 O `AuthContext` passa a usar o estado com nome: o `catch` genérico sai, **só o 401 apaga o token**, qualquer
  outra falha vira "falhou" com o token **intacto**, e o booleano `ready` sai. Entra a ação de **refazer a checagem**
  (D15 — o `api.me()` não é consulta do React Query: não há retry automático nem `refetch`, então o efeito depende de
  um número de tentativa que a ação incrementa). O `setUnauthorizedHandler` fica como está.
  O contexto passa a expor `{ session, login, logout, recheckSession }` — o `user` nulável e o `ready` saíram. No erro
  da checagem, quem decide é o `sessionFromCheckError`: **só** o ramo `anonymous` chama `tokenStore.set(null)`. O
  `recheckSession` incrementa o `attempt`, de que o efeito depende; o efeito volta a `checking`, e um `cancelled`
  ignora a resposta da tentativa anterior. O `setUnauthorizedHandler` ficou **num efeito próprio**, com a limpeza que
  antes era inalcançável quando não havia token guardado (o `return` precoce passava por cima dela). Para quem está
  **dentro** do painel nasceu o `useAuthUser()`, que devolve o usuário não nulo — lá dentro, por construção, há
  usuário.
- [x] 6.5 **Uma tela de falha só** para a casca inteira (D16): o componente do 3.1 passa a servir às duas portas, com o
  nome que o trabalho dele pede, e o portão do `App.tsx` escolhe entre carregando, falha, login e painel pelo estado do
  6.3. O "tentar de novo" da falha de sessão refaz a **checagem**; o da falha do `/info` refaz o `/info`. Mesma
  mensagem, mesmos dois botões.
  `InfoFailure.tsx` → `ShellFailure.tsx` (`InfoFailure` → `ShellFailure`): o nome passou a dizer o trabalho — não é a
  falha do `/info`, é a falha da **casca**. Arquivo da própria fatia, ainda não publicado, então renomear não custou
  história. O portão do `App.tsx` virou quatro ramos sobre o `session.kind`: `checking → <Loading />`,
  `failed → <ShellFailure onRetry={recheckSession} …/>`, `anonymous → <LoginPage />`, autenticado → `<Dashboard />`.
  Dentro do painel, o ramo do `/info` continua: `failure → <ShellFailure onRetry={refetchInfo} …/>`. **Mensagem,
  botões e layout, idênticos** — o componente é um só, e o que muda é só a função do `onRetry`. Os `user?.` do
  `App.tsx` e o `me?.email` do `UsersPage` viraram acesso direto, porque o usuário ali não pode ser nulo.
- [x] 6.6 `npm test` verde e `npm run build` sem erro. Evidência: as duas saídas.
  `npm test` → `Test Files 14 passed (14)`, `Tests 154 passed (154)`. `npm run build` → `tsc --noEmit` sem erro,
  `✓ 1359 modules transformed`, `✓ built in 20.41s`.
- [x] 6.7 Verificação **manual**, a do Marcelo, na tela, com a API desligada: (a) com token guardado e API fora, a tela
  é a de **falha**, e não o login; (b) o `localStorage` **continua** com o `fiscalhub.token`; (c) "Tentar de novo"
  mostra carregando e, com a API de volta, o painel desenha **sem pedir senha**; (d) "Sair" na tela de falha leva ao
  login e aí sim apaga o token; (e) com a sessão realmente inválida (token adulterado → 401), a tela é o **login**, e
  não a de falha. **Limite anotado, o mesmo de 3.4:** a renderização continua sem teste automatizado.
  **Aberta por decisão do Marcelo (2026-10-15): a verificação na tela é dele, com o host fora.** O código de 6.1 a 6.5
  está pronto e com a guarda verde; falta só a conferência manual.


  **Conferido na tela em 15/10/2026**, com o host subido do build atual na 5200, o dashboard servido por um proxy local
  temporario (removido depois) e a sessao do usuario de desenvolvimento do `docs/RUNNING.md`. Seis cenarios:

  1. **API fora, com token guardado** — a tela e a **de falha**, e nao o login, e o `fiscalhub.token` **continua** no
     `localStorage`. Antes do conserto, esse mesmo cenario apagava o token e mandava para o login.
  2. **Carregando antes da falha** — com a chamada atrasada 3s no proxy (sem atraso a recusa de conexao e instantanea
     demais para observar), a tela mostra `Carregando...` e so depois a de falha.
  3. **"Tentar de novo" com a API ainda fora** — volta para `Carregando...` e depois para a falha.
  4. **"Tentar de novo" com a API de volta** — o painel desenha, com a arvore do tenant, **sem pedir senha**: a sessao
     sobreviveu a queda.
  5. **"Sair" na tela de falha** — volta ao login e **apaga** o token, com a API ainda falhando. E o D11 na pratica.
  6. **Token invalido com a API no ar** — o `/auth/me` responde 401 e a tela e o **login**, com o token apagado. O ramo
     que derruba a sessao continua derrubando.

  E o que esta tarefa pede especificamente, com **so o `/info` em 503** e o resto da API no ar: a tela e a de falha,
  nao existe `<nav>` nenhum no documento, e o texto "Sem conector" nao aparece em lugar algum — que era exatamente a
  afirmacao falsa que esta fatia veio apagar.

## 4. Docs

- [x] 4.1 `docs/STATUS.md`: o item 2 dos "Próximos passos" ("O `/info` não tem tratamento de falha, em tela nenhuma")
  **sai**, e a sessão do dia registra o que foi decidido — inclusive a resposta da Q1 e o **achado do portão de
  sessão** (grupo 6): o que a prova manual mostrou, por que entrou nesta fatia e o que passou a valer. Conferir que
  nenhum outro ponto do arquivo ainda descreve a falha como "a barra lateral afirma que não há conector configurado".
  O item 2 saiu e a lista foi **renumerada** (eram 10, ficaram 9). Duas remissões internas foram conferidas: a do item
  do wizard, que citava "(item 5)" e passou a "(item 4)" — a mesma entrada, a das URLs declaradas; e a frase "que saiu
  deste item 2 por estar feita", lá na partição por domínio, que falava do item do Contábil e **voltou a apontar para
  ele** com a renumeração (estava deslocada desde que o item do `/info` foi inserido no meio da lista). Busca por
  "tratamento de falha" e "afirma que não há conector" no arquivo: só na sessão nova, e no passado, descrevendo o
  defeito apagado. A sessão `2026-10-15` entrou no fim do arquivo com a decisão de produto, a resposta da Q1, o achado
  do portão de sessão e a guarda — e dizendo que a change **não está arquivada**, porque as duas verificações de tela
  (3.4 e 6.7) são do Marcelo.
- [x] 4.2 Conferir se a implementação trouxe alguma escolha estrutural que mereça ADR (D10 prevê o caso de a casca
  virar um contrato para outras consultas críticas). **Se não trouxe, dizer isso explicitamente aqui** — a ausência de
  ADR é uma conclusão, e não um esquecimento.
  **Não trouxe, e o ADR não nasce aqui.** As três candidatas foram pesadas:
  - **a casca virar contrato para outras consultas críticas** (o caso que o D10 previa) **não aconteceu**: o portão
    cresceu de uma chamada para duas, e as duas são as que dizem *quem é o usuário* e *o que ele pode ver*. Nenhuma
    outra consulta foi promovida a crítica, e a regra de que cada tela trata a falha da sua continua valendo;
  - **o `ApiError`** é mecanismo dentro do cliente de API do dashboard, não decisão de arquitetura do hub: não cruza
    fronteira de Domain/Application/Infrastructure e não muda contrato de endpoint nenhum. Ele está registrado onde
    pertence — no `design.md` D13 e no comentário do próprio arquivo;
  - **o estado nomeado da sessão** é a consequência de tela de uma premissa já registrada (ADR-0039 §5: quem decide o
    que a tela pode mostrar é o servidor; ADR-0018: o JWT e o escopo por tenant). O lugar permanente dela é a spec,
    que é a fonte da verdade — e a spec recebeu a regra.

## 5. Verificação final

- [x] 5.1 `npm test` e `npm run build` no `dashboard/`, as duas saídas guardadas.
  `npm test` → `Test Files 14 passed (14)`, `Tests 154 passed (154)` — eram 131 antes da change e 142 antes do grupo
  6. `npm run build` → `tsc --noEmit` sem erro, `✓ 1359 modules transformed`, `✓ built in 19.20s`. O aviso de chunk
  acima de 500 kB é anterior à change.
- [x] 5.2 `openspec validate --all --strict` verde.
  `Totals: 29 passed, 0 failed (29 items)`.
- [x] 5.3 Reler os não-objetivos do `proposal.md` contra o diff: nenhum arquivo de `src/`, nenhum endpoint, nenhuma
  mudança em `staleTime`, invalidações, `sessionCache` ou nos três outros consumidores do `/info`; nada de refresh de
  token, renovação de sessão ou política de expiração; e, no cliente de API, **nenhuma mudança de mensagem ou de fluxo
  de outra chamada** — o 401 continua derrubando a sessão, só que agora identificado pelo status (D13).
  Conferido contra `git status` e `git diff`. **Tocados:** `dashboard/src/App.tsx`, `dashboard/src/api/client.ts`,
  `dashboard/src/features/auth/AuthContext.tsx`, `dashboard/src/features/useInfo.ts`,
  `dashboard/src/features/users/UsersPage.tsx`, `docs/STATUS.md`; novos: `api/ApiError.ts`, `features/ShellFailure.tsx`
  (renomeado de `InfoFailure.tsx`), `features/shellState.ts(.test.ts)`, `features/auth/sessionState.ts(.test.ts)`.
  **Nenhum arquivo de `src/`** (.NET), nenhum endpoint, nenhuma linha de `staleTime`, invalidação, `sessionCache` ou do
  `setUnauthorizedHandler`; nada de refresh de token, renovação ou expiração. **Os três outros consumidores do `/info`**
  — `DocumentDetail`, `NoteDialog` e `ConnectorsPage` — não aparecem no diff. O `UsersPage` aparece, e **não** por
  causa do `/info`: ele lia o usuário pelo `useAuth()`, que mudou de forma no 6.4, e passou a lê-lo pelo
  `useAuthUser()` — duas linhas. No `client.ts`, o diff é o import, um comentário e os dois `throw` do `getJson`, com
  as mensagens intactas. (O `dashboard/vite.local-proxy.config.ts` não rastreado é anterior a esta fatia, da montagem
  da prova manual, e não foi tocado.)
