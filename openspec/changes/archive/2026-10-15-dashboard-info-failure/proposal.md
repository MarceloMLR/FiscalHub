## Why

O dashboard **não trata a falha do `/info` em lugar nenhum**, e por isso uma falha se disfarça de resposta. O
levantamento abaixo foi conferido no código (revisão de 2026-10-15), e não presumido.

**A consulta** (`dashboard/src/features/useInfo.ts`): `useQuery({ queryKey: ['info'], queryFn: api.info, staleTime:
Infinity })`. Nenhum consumidor lê o `isError` — nem o `App.tsx`, nem o `ConnectorsPage`, nem o `DocumentDetail`, nem o
`NoteDialog`.

**A linha que decide** (`dashboard/src/App.tsx:126`):

```ts
const groups = navigationGroups(infoLoading ? undefined : (info?.connectors ?? []));
```

Carregando vira `undefined`, que a `navigationGroups` trata como "ainda não se sabe": nenhum grupo é desenhado e a tela
mostra `Carregando…`. Mas a **falha cai no mesmo `else`**: com `isLoading` falso e `info` indefinido, o `?? []` entrega
a árvore **vazia** — que é uma **resposta**, e árvore vazia **nomeia a ausência**. O resultado é a barra lateral
afirmando ao cliente, com todas as letras, que ele **não tem conector configurado**
(`module-navigation`, "O tenant sem conector vê um grupo que nomeia a ausência").

Os outros dois efeitos da mesma falha são o lado seguro, e não precisam de conserto: o selo de ambiente some
(`environmentBadge(undefined) → null`) e os botões da visão técnica somem (`canViewRawJson(undefined) → false`). O
defeito é só o terceiro, e é o que **afirma** algo.

Isso está anotado como dívida consciente em dois lugares: no comentário do próprio `App.tsx:122-125` e no item 2 dos
"Próximos passos" do `docs/STATUS.md` ("O `/info` não tem tratamento de falha, em tela nenhuma… **uma falha do `/info`
faz a barra lateral afirmar que não há conector configurado**"). Esta change vem pagar essa dívida.

**A decisão de produto, já tomada pelo Marcelo (2026-10-15):** sem o `/info`, o dashboard **não desenha o painel**. Ele
mostra uma tela que diz que não conseguiu carregar o painel, com uma ação de tentar de novo. A razão: o `/info` é
o contrato que diz o ambiente, o que o tenant tem e o que o usuário logado pode ver; sem ele, toda afordância da tela é
chute. E é **um lugar só** — nenhum consumidor novo pode esquecer de tratar, que é exatamente a classe de bug que mordeu
duas vezes na change anterior (o `roles.ts` decidindo papel na tela, e o cache sobrevivendo à troca de usuário).

## O mesmo defeito, uma camada acima — achado na prova manual (2026-10-15)

A prova de tela desta change, com a API desligada, mostrou que **o dashboard não chega à tela de falha**: ele mostra o
**login**, e o `localStorage` fica só com o `fiscalhub.theme` — o token foi **apagado**. Uma indisponibilidade do
servidor destruiu a sessão do usuário.

A causa está em `dashboard/src/features/auth/AuthContext.tsx`, na restauração da sessão:

```ts
api.me().then(setUser).catch(() => { tokenStore.set(null); setUser(null); }).finally(() => setReady(true));
```

O `catch` é **genérico**: 401, 500, rede e timeout caem todos nele, e todos apagam o token. É exatamente o defeito que
esta change existe para apagar, uma camada acima — tratar "não consegui perguntar" como se fosse uma resposta. No
`/info` a mentira era "o tenant não tem conector"; aqui é "a sua sessão não vale".

E há uma consequência que **esvazia a fatia**: numa queda real o usuário é deslogado no portão **antes** de o `/info`
chegar a falhar, então a tela de falha só apareceria no caso estreito de o `/info` falhar enquanto o `/auth/me`
responde. Entregar a change sem isto seria entregar uma tela que quase ninguém vê.

**O Marcelo aprovou consertar nesta fatia (2026-10-15).**

## What Changes

### 1. Três estados, e não dois

"Ainda não respondeu", "respondeu vazio" e "falhou" passam a ser **três estados distintos**. Hoje a falha se disfarça de
vazio justamente porque só havia dois. É o mesmo **nulo-contra-vazio** que o dashboard já aplica em quatro pontos — a
árvore (`navigationGroups(undefined)` vs `[]`), o selo de ambiente (`environmentBadge(undefined)`), o selo da integração
automática (`info?.inboundScans === true`) e os botões da visão técnica (`canViewRawJson(undefined)`) —, agora com um
terceiro caso que nenhum deles tinha.

### 2. A decisão vira uma função com nome, pura, e a tela só renderiza o que ela diz

A escolha entre carregar, desenhar o painel e mostrar a falha **não** fica como ternário no `App.tsx`. Motivo técnico
concreto: o dashboard não tem stack de teste de componente (sem `@testing-library/react`, sem jsdom), e por isso a linha
que **escolhe** ficou sem teste quatro vezes na change anterior — limites anotados nas tarefas 1.6, 4.0, 4.8 e 4.10 de
`connector-navigation-and-admin-json`. Uma função pura move a escolha para onde o teste alcança.

### 3. A tela de falha, que não afirma nada sobre a configuração do cliente

A mensagem fala da **falha de carregar**, e não do que o tenant tem. Ela MUST NOT dizer que não há conector, módulo,
agendamento ou ambiente — é a frase que o defeito de hoje produz, e a que a change existe para apagar.

### 4. Tentar de novo

A ação refaz a chamada que faltou — o `/info` **e também** a checagem de sessão. **Enquanto refaz, o estado é o de
carregando, e não o de falha.** Falhando de novo, a tela de falha volta.

### 5. O portão de sessão distingue "não vale" de "não consegui perguntar"

O **401 continua derrubando**: token apagado, tela de login — a sessão realmente não vale. **Qualquer outra falha**
(5xx, rede, timeout) **mantém o token** e leva à tela de falha, que é **a mesma** tela: a mesma mensagem, o mesmo
"Tentar de novo" e o mesmo "Sair". O usuário não precisa saber qual chamada não respondeu.

Para isso, duas coisas mudam de forma:

- **a classificação do erro vira função pura, com nome e teste** — a mesma razão do item 2: a linha que escolhe não
  pode viver como condicional dentro do componente;
- **o booleano `ready` sai.** Ele só dizia "já tentei", e agora há **quatro** situações — checando, autenticado,
  anônimo e falhou. Vira um estado com nome, e não dois booleanos.

O `setUnauthorizedHandler` (401 em qualquer chamada, depois de logado) **continua como está**.

## Capabilities

### Modified Capabilities

- `module-navigation`: ganha o requisito **"Sem saber quem é o usuário e o que o tenant tem, o dashboard não desenha o
  painel"** — o portão inteiro da casca: a checagem de sessão e o `/info`, a tela de falha única, a ação de tentar de
  novo, a mensagem que não afirma configuração, a distinção entre 401 e "não consegui perguntar", e as escolhas como
  funções puras testadas. O requisito publicado "O tenant sem conector vê um grupo que nomeia a ausência" passa de dois
  estados para três: a árvore vazia continua nomeando a ausência, e a falha deixa de se disfarçar dela.

**Por que o título cresceu, e por que não virou um requisito irmão** (2026-10-15). O título anterior era "Sem o `/info`,
o dashboard não desenha o painel", e ele ficou **estreito demais** para o que a change faz: o portão tem duas portas, e
a de dentro (`/info`) nem chega a ser alcançada quando a de fora (a checagem de sessão) cai. Um requisito **irmão** foi
descartado pelo mesmo motivo que a capability própria já tinha sido (D9): a regra é **uma** — "não consegui perguntar"
não é uma resposta —, e a tela também é **uma**. Separados, o "é a mesma tela, com a mesma mensagem e as mesmas duas
ações" teria de ser escrito duas vezes, ou virar uma remissão que só faz sentido lendo os dois documentos juntos. Quem
lê o requisito precisa saber o que a tela mostra sem precisar de um segundo arquivo.

**Por que em `module-navigation`, e não numa capability de autenticação** (além de D9): não existe capability que
descreva o portão do dashboard — o `/auth/me` só aparece no ADR-0018, que é sobre o JWT e o escopo por tenant, e não
sobre o que a tela desenha. O que este requisito governa é **o que o dashboard desenha**, que é justamente a casca de
que `module-navigation` já é dona.

**Por que aqui, e não em outra capability** (detalhado no `design.md`, D9):

- **`module-navigation`** já é a dona da casca que o `App.tsx` desenha (os grupos, o bloco Administração, a tela aberta
  ao entrar, o título da topbar) **e** da distinção nulo-contra-vazio: o requisito do tenant sem conector já diz, hoje,
  que "'ainda não respondeu' e 'respondeu vazio' são estados diferentes". O terceiro estado nasce ao lado dos outros
  dois, no mesmo parágrafo, e não num documento que precisaria ser lido junto para a frase fazer sentido.
- **`platform-response-trace`** é dona do `canViewRawJson` — da **permissão**, e não da casca. A regra dela ("antes de o
  `/info` responder, ou se ele falhar, nenhum dos dois botões aparece") continua **verdadeira e inalterada**: com a
  casca não desenhada, o detalhe nem existe, e a regra segue valendo como defesa em profundidade. Não há delta lá.
- **Capability própria** foi descartada: ela teria um requisito só, cujo conteúdo é a condição de borda de um requisito
  de `module-navigation`. Duas specs que precisam ser lidas juntas para dizer o que a tela faz são uma spec a mais.

## Non-goals

- **O 401.** Ele já cai para o login pelo `setUnauthorizedHandler` do `api/client.ts`, e está certo. Esta change é para
  **5xx, rede e timeout**. O que ela acrescenta é que o portão de sessão passe a **saber** que o erro era 401 — e não
  que o 401 passe a ser tratado de outro jeito.
- **Refresh de token, renovação de sessão e política de expiração.** É outra conversa. Esta change decide o que fazer
  com a **falha da checagem**, e não quanto tempo a sessão dura.
- **O comportamento das outras chamadas do cliente de API.** Se o cliente precisar mudar para o status chegar à
  classificação, a mudança MUST ser invisível para quem já o usa — mesma mensagem, mesmo fluxo.
- **O `staleTime: Infinity` e as invalidações.** A change anterior deixou isso correto: o `ConnectorsPage.tsx:194`
  invalida o `['info']` ao salvar, e a limpeza de cache na troca de sessão está em `features/auth/sessionCache.ts`.
  Nada disso é tocado.
- **Os outros consumidores do `/info`** (`DocumentDetail`, `NoteDialog`, `ConnectorsPage`) **não mudam**: se a casca não
  renderiza, eles não chegam a existir. Fica dito para que ninguém "conserte" o que não precisa.
- **O servidor.** Nenhum endpoint, nenhuma resposta, nenhum projeto .NET muda. É só dashboard.
- **Texto de produto novo** além da mensagem de falha e do rótulo do botão.
- **Stack de teste de componente** (`@testing-library/react`, jsdom). É outra fatia; é justamente a ausência dela que
  justifica a função pura (ver 2 acima).
- **A falha das outras consultas** (`/groups`, `/connector`, `/executions`…). Cada tela já trata a sua — o
  `ConnectorsPage` tem o `isError` dele. O item 2 do `STATUS.md` é sobre o `/info`, e as três telas que ele cita
  (barra lateral, selo de ambiente, botões da visão técnica) ficam resolvidas de uma vez, porque as três vivem dentro da
  casca.

## Impact

- **Dashboard:**
  - `features/useInfo.ts`: passa a expor o que a decisão precisa (a resposta, o erro, se está buscando, e a ação de
    refazer), em vez de só `data` e `isLoading`;
  - **módulo novo** com a função pura que escolhe o estado, e o teste dela (vitest);
  - **componente novo** da tela de falha, com a mensagem e os botões — **um só**, usado nas duas portas do portão;
  - `App.tsx`: deixa de montar a decisão na linha 126 e passa a renderizar o que a função disser; o comentário de dívida
    das linhas 122-125 sai, porque a dívida foi paga;
  - `api/*`: o erro de uma resposta HTTP não-ok passa a carregar o **status**, que hoje se perde — sem ele a
    classificação não tem como distinguir 401 de 500. Mensagens e fluxo das demais chamadas, inalterados;
  - **módulo novo** com o estado nomeado da sessão (checando, autenticado, anônimo, falhou) e a função pura que
    classifica o erro da checagem, com o teste dela;
  - `features/auth/AuthContext.tsx`: o `catch` genérico e o booleano `ready` saem; entra o estado com nome e uma ação
    de refazer a checagem.
- **Servidor:** nada.
- **Docs:** `docs/STATUS.md` — o item 2 dos "Próximos passos" sai.
- **Testes:** só vitest, no dashboard. Limite conhecido e herdado: a renderização em si continua sem teste (não há stack
  de componente); o que esta change garante é que a **escolha** deixe de estar lá.
