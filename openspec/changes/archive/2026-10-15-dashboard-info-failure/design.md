## Context

A motivação está no `proposal.md`. Aqui ficam o estado do código que molda o desenho e as escolhas que a decisão do
Marcelo não precisava fixar. O levantamento foi **conferido no código** (revisão de 2026-10-15).

**A consulta** — `dashboard/src/features/useInfo.ts`, seis linhas:

```ts
export function useInfo() {
  return useQuery({ queryKey: ['info'], queryFn: api.info, staleTime: Infinity });
}
```

O `api.info` usa o `getJson`, que trata o **401** chamando o `handleUnauthorized()` (limpa o token e avisa a app) e
lança para qualquer outro status não-ok. O `QueryClient` do `main.tsx` é criado sem opções: vale o **retry padrão do
React Query — 3 tentativas** com espera crescente. "Falhou", então, já é "falhou depois de tentar quatro vezes".

**Os cinco leitores do `/info`**, e o que cada um faz com o `undefined`:

| Leitor | O que lê | Com `undefined` |
|---|---|---|
| `App.tsx:126` | `info.connectors` | **afirma a ausência** — o defeito |
| `App.tsx:112` | `info.environment` | `environmentBadge(undefined) → null`: não afirma ambiente |
| `App.tsx:115-116` | `inboundScans`, `automaticIntegration` | `=== true` → o selo não aparece |
| `DocumentDetail:53`, `NoteDialog:38` | `canViewRawJson(info)` | `false`: os botões não aparecem |
| `ConnectorsPage:130` | `info` no painel do conector | não afirma |

Quatro dos cinco já tratam o não-saber como não-afirmar. **O quinto é o único que responde.** Isso é o
nulo-contra-vazio do projeto; o que falta é o terceiro caso.

**O que a casca é hoje:** um componente só, o `Dashboard()` do `App.tsx`, que desenha a barra lateral, a topbar e a área
de conteúdo. Todas as telas de módulo e os dois diálogos que leem o `/info` são filhos dela. Já existe o
`if (groups.length === 0) return <Loading />` na linha 130 — isto é, a casca **já sabe não se desenhar**; ela só não
sabe por quê.

**O portão de sessão, e o que o erro carrega** (conferido no código em 2026-10-15, depois do achado da prova manual).
O `AuthProvider` restaura a sessão com uma **promise solta num `useEffect`** — `api.me()` **não** é consulta do React
Query, então não há retry automático, não há `refetch`, e o `catch` é a única coisa que decide. Ele é genérico: 401,
500, rede e timeout caem todos nele, e todos apagam o token.

E o erro que chega lá **não diz o status**: o `getJson` do `api/client.ts` lança `new Error('Sessão expirada.')` no 401
e `new Error(\`${res.status} ${res.statusText}\`)` no resto — um `Error` comum, com o status **só dentro da string da
mensagem**. Ler o status de volta por `parseInt` da mensagem seria contrato por acidente. Daí a D13.

## Goals / Non-Goals

**Goals**

- a falha do `/info` deixa de se disfarçar de resposta vazia;
- a falha da **checagem de sessão** deixa de se disfarçar de sessão inválida — e, com isso, a tela de falha passa a ser
  alcançável numa queda real;
- a falha tem tela própria, com ação de tentar de novo, e uma mensagem que não afirma nada sobre a configuração do
  cliente;
- a escolha do estado vira função pura, com nome e teste — tanto a da casca quanto a classificação do erro da checagem.

**Non-Goals** — estão no `proposal.md`. Em uma frase: nada de 401, nada de `staleTime`/invalidações, nada de servidor,
nada nos outros consumidores e nada de stack de teste de componente.

## Decisions

### D1 — Sem o `/info`, o painel não é desenhado

**Decisão do Marcelo, 2026-10-15.** A alternativa seria degradar por pedaço: barra lateral sem grupo, selo ausente,
botões escondidos, conteúdo tentando carregar. Ela foi recusada, e a razão é que o `/info` não é um enfeite da tela: ele
diz **o ambiente da instância**, **o que o tenant tem** e **o que o usuário logado pode ver**. Sem ele, cada afordância
da tela vira um chute independente — e a tela passa a ter tantos jeitos de errar quantos forem os leitores.

O ganho estrutural é o mesmo que a change anterior perseguiu duas vezes: **um lugar só**. Um consumidor novo do `/info`
não tem como esquecer de tratar a falha, porque ele só existe depois que a casca decidiu que há resposta. É o oposto do
`roles.ts` (duas listas que um comentário pedia para andarem juntas) e do cache que sobrevivia à troca de usuário.

### D2 — Três estados, e o nulo continua não sendo o vazio

```
ainda não respondeu  →  Carregando…      (como hoje)
respondeu            →  o painel         (com a árvore que veio, vazia ou não)
falhou               →  a tela de falha  (nova)
```

A árvore **vazia** continua sendo uma resposta, e continua nomeando a ausência — a regra de `module-navigation` não
muda. O que muda é que a falha para de ser traduzida para ela. A frase que a spec publicada já tem ("'ainda não
respondeu' e 'respondeu vazio' são estados diferentes") ganha o terceiro membro.

### D3 — A escolha é função pura, e não ternário na tela

O dashboard não tem `@testing-library/react` nem jsdom (conferido no `package.json`: as dependências de teste são
`vitest` e nada mais). A consequência é documentada quatro vezes nas tarefas da change anterior — **1.6, 4.0, 4.8 e
4.10** —, sempre com a mesma frase: o teste cobre o módulo, e não a linha do `App.tsx` que o usa. Quatro limites
anotados são um padrão, e não um azar.

A resposta desta fatia não é adotar a stack (ver Q2): é **mover a escolha para onde o teste já alcança**. A função
recebe o estado da consulta e devolve o que mostrar; o `App.tsx` faz `switch` sobre o resultado e não decide nada. O que
sobra sem teste é a renderização — três ramos triviais —, e não a regra.

### D4 — Quais sinais a função lê, e em que ordem

No React Query v5, depois de um erro o `status` **continua** `'error'` durante a nova tentativa: `isError` e
`isFetching` ficam verdadeiros ao mesmo tempo, e o `isLoading` (que é `isPending && isFetching`) já é falso. Ler só
`isLoading`/`isError` daria "falha" durante o "tentar de novo" — exatamente o que o requisito 4 proíbe. A ordem, então,
é esta:

1. **tem resposta em mãos** → o painel (mesmo que uma recarga esteja em curso, ou tenha falhado — ver D6);
2. **está buscando** → carregando (é isto que faz o "tentar de novo" mostrar carregando, e não falha);
3. **terminou em erro** → a tela de falha;
4. **o resto** → carregando (o primeiro render, e a pausa por offline, em que o `fetchStatus` é `'paused'`).

A função é testável porque não depende do React Query: ela recebe os três sinais como dados.

### D5 — "Falhou" é a consulta esgotada, e o retry padrão fica como está

Não se cria política de retry nova (o `proposal.md` já põe `staleTime` e invalidações fora). O botão "Tentar de novo" é
um `refetch()` — o mesmo caminho da invalidação, e não um segundo mecanismo. Pelo retry padrão, a tela de falha só
aparece depois de quatro tentativas, o que já filtra o soluço de rede.

### D6 — Uma resposta já recebida continua valendo

Com `staleTime: Infinity` e o cache do React Query, uma recarga que falha **não** apaga o dado anterior. A regra
acompanha: a tela de falha é para quem **não tem resposta nenhuma**. Se o Admin salva em Configurações, o `['info']` é
invalidado e a recarga falha, o painel continua de pé com a árvore anterior — ele fica **velho**, e velho não é mentira;
derrubar uma sessão inteira que está funcionando por causa de uma recarga seria o dano maior. (Suposição S4: a
alternativa está escrita lá.)

### D7 — O que a mensagem não pode dizer

A mensagem fala da **falha de carregar o painel**. Ela MUST NOT nomear conector, módulo, agendamento, ambiente ou
permissão — nem para dizer que não há. A frase que o defeito de hoje produz é "Sem conector configurado", e a change
existe para apagá-la: trocá-la por "não encontramos seus conectores" seria o mesmo defeito com outra fonte.

O texto é "Não foi possível carregar o painel." com o botão "Tentar de novo" (S3 — o texto exato é de produto).

> **Atualizado em 2026-10-15 (decisão do Marcelo).** O texto era "Não foi possível carregar a configuração do painel.",
> e a palavra "configuração" saiu. Ela nasceu quando a fatia era só do `/info`, onde descrevia bem o que não veio;
> depois que a mesma tela passou a servir a checagem de sessão (D12, D16), ficou imprecisa — quando o `/auth/me` não
> responde, **nenhuma configuração chegou a ser pedida**. E é justamente a palavra que este requisito se esforça para
> manter longe da cabeça do usuário: quem lê "não foi possível carregar a configuração" pode ir conferir as próprias
> configurações, que é o lugar errado. A frase mais curta é verdadeira nas duas portas, continua não afirmando nada
> sobre o que o tenant tem, e o ícone de desconectado já carrega a ideia de alcance.

### D8 — O 401 não muda

O `getJson` já chama o `handleUnauthorized()` no 401: limpa o token e a app volta ao login. Um 401 **não** chega à tela
de falha, e a tela de falha **não** oferece "entrar de novo" como conserto — ela é para 5xx, rede e timeout. Misturar os
dois faria a sessão expirada virar "o servidor está fora".

### D9 — Onde o requisito mora

Vai em **`module-navigation`**, como uma requirement nova, mais um delta na requirement publicada "O tenant sem conector
vê um grupo que nomeia a ausência". Os três candidatos, e por quê:

> **Atualizado em 2026-10-15 (D12).** Com o portão de sessão dentro da fatia, o título da requirement nova cresceu —
> de "Sem o `/info`, o dashboard não desenha o painel" para **"Sem saber quem é o usuário e o que o tenant tem, o
> dashboard não desenha o painel"**. O lugar não mudou, e a razão de não virar uma requirement **irmã** está no
> `proposal.md`: a regra é uma e a tela é uma; separadas, o "é a mesma tela" teria de ser dito duas vezes. Não existe
> capability do portão do dashboard para disputar o lugar — o `/auth/me` só aparece no ADR-0018, que é sobre o JWT e o
> escopo por tenant, e não sobre o que a tela desenha.

- **`module-navigation`** — escolhido. Ela já é a dona da casca que o `App.tsx` desenha: os grupos, o bloco
  Administração, a tela aberta ao entrar, o título da topbar. E já é a dona da distinção que esta change estende: o
  parágrafo "Antes da resposta do `/info`" é onde a frase dos dois estados está escrita. Pôr o terceiro estado em outro
  lugar deixaria a frase publicada incompleta no arquivo em que ela está.
- **`platform-response-trace`** — descartada, e **sem delta**. Ela é dona do `canViewRawJson`, isto é, da **permissão**,
  e não da casca. A regra dela ("A tela MUST usar essa resposta… e MUST NOT mostrá-los antes de o `/info` responder, nem
  se ele falhar") continua **literalmente verdadeira**: com a casca não desenhada, o detalhe do documento não existe, e
  o ramo "se ele falhar" vira defesa em profundidade — útil no dia em que alguma tela nascer fora da casca. Custo
  evitado: um `## MODIFIED Requirements` ali carregaria as sete cenários da requirement "A resposta no detalhe do
  documento" para acrescentar uma remissão.
- **Capability própria** — descartada. Teria um requisito só, cujo conteúdo é a condição de borda de um requisito de
  `module-navigation`; as duas precisariam ser lidas juntas para dizer o que a tela faz.

A requirement "O eixo da barra lateral é o conector" **não** recebe delta: ela descreve uma barra lateral que existe, e
a requirement nova é um portão **antes** dela. Nenhuma frase de lá fica falsa.

### D10 — Por que não há ADR

A premissa arquitetural já está registrada: quem decide o que a tela pode mostrar é o **servidor** (ADR-0039 §5), e o
`/info` é o canal. Esta change não escolhe arquitetura nova — ela aplica a consequência de tela de uma decisão de
produto, e o lugar permanente dela é a spec, que é a fonte da verdade do projeto. Se na implementação aparecer uma
escolha estrutural (por exemplo, a casca virar um componente com contrato próprio para outras consultas críticas), aí
sim cabe ADR, e isso fica anotado nas tarefas.

### D11 — A tela de falha tem o próprio botão "Sair", e não mantém a topbar (resolve a Q1)

**Decisão do Marcelo, 2026-10-15.** É o caminho do meio que a Q1 já tinha anotado: a saída existe, e não nasce um
terceiro layout de casca.

A razão, dos dois lados:

- **a saída precisa existir:** usuário preso numa falha persistente sem poder trocar de conta é armadilha. E o
  `logout()` é **local** — ele só apaga o token e o estado de sessão —, então funciona com o servidor fora, que é
  exatamente a situação desta tela. Negar a saída não protegeria nada;
- **a topbar não:** mantê-la de verdade significaria exibi-la num estado em que o título e o subtítulo não têm o que
  mostrar (não há item aberto) e em que o papel exibido veio do **token**, e não do `/info`. Seria uma terceira forma
  de tela para manter e testar — painel, falha e falha-com-topbar —, que é o risco anotado logo abaixo. Um botão
  próprio resolve a saída sem inventar meia casca.

O que isso fixa para a implementação: a tela de falha tem a mensagem, o "Tentar de novo" e um "Sair". Não tem topbar,
nem barra lateral, nem menu do usuário.

### D12 — O portão de sessão entra nesta fatia, e com a mesma regra

**Decisão do Marcelo, 2026-10-15**, depois do achado da prova manual (`proposal.md`). Duas razões, e a segunda é a que
não dava para adiar:

- **é o mesmo defeito.** "Não consegui perguntar" tratado como resposta. Consertar um e deixar o outro seria publicar a
  regra e quebrá-la no arquivo ao lado;
- **sem isso a fatia fica vazia.** Numa queda real o usuário é deslogado no portão **antes** de o `/info` chegar a
  falhar. A tela de falha só apareceria se o `/info` falhasse **enquanto** o `/auth/me` responde — um caso estreito.
  O conserto do portão é o que faz a tela que esta change constrói existir no mundo.

O que **não** muda: o `setUnauthorizedHandler` (o 401 de qualquer chamada, depois de logado) continua como está, e o
401 na checagem continua derrubando a sessão. O que muda é que o portão passa a **saber** que era 401.

### D13 — O erro do cliente de API passa a carregar o status, e isso não muda nenhuma chamada

O `getJson` lança `Error` comum (ver Context). Sem o status, a classificação não tem como distinguir 401 de 500 — e
sem a classificação, o conserto não existe. Então o status vai junto do erro:

- **uma classe `ApiError extends Error`, com `status`**, lançada pelo `getJson` nos dois caminhos de erro — o 401 e o
  não-ok. **A mensagem é exatamente a de antes**, nos dois casos, e `ApiError` é um `Error`: quem hoje faz `catch` e
  mostra `e.message` não percebe diferença. É o que mantém o não-objetivo "não mude o comportamento de nenhuma outra
  chamada";
- **só o `getJson`.** Os outros ajudantes (`sendJson`, `getJsonWithMessage`, `sendAdmin`, os `fetch` soltos) continuam
  lançando `Error`: ninguém precisa distinguir status neles hoje, e mexer onde não é preciso é diff sem motivo. Quando
  precisarem, a classe já existe;
- **a classe mora em `api/ApiError.ts`, e não dentro do `client.ts`.** Motivo concreto, e não gosto: o `client.ts`
  resolve a URL base **no import** (`const BASE = resolveApiBase()`), que lê o `window`; os testes do dashboard rodam
  no ambiente **node** do vitest (não há jsdom — é o mesmo fato que justifica D3), e importar o cliente num teste
  quebraria no carregamento. Num arquivo próprio, o teste usa o erro de verdade, e não um dublê parecido.

### D14 — Quatro situações, um estado com nome: o `ready` sai

O `ready` booleano só diz "já tentei". As situações são quatro — **checando**, **autenticado**, **anônimo** e
**falhou** —, e duas delas (anônimo e falhou) são justamente as que o defeito confundia. Representá-las por dois
booleanos recriaria a confusão com mais passos.

Então o contexto de autenticação passa a expor **um** estado discriminado, e o portão do `App.tsx` escolhe por ele:
carregando, tela de falha, login ou painel. Consequência deliberada: quem está **dentro** do painel não recebe mais um
usuário que pode ser nulo — lá dentro, por construção, **há** usuário. É o mesmo movimento do `shellState`, que
entrega a resposta junto do `panel` em vez de deixar a tela perguntar a um `info?.` se ela chegou.

### D15 — Tentar de novo quando não há `refetch`

O `/info` é consulta do React Query: tentar de novo é `refetch()` (D5). A **checagem de sessão não é consulta** — é uma
promise solta num `useEffect` —, então não há nem retry automático nem `refetch` para chamar. Resolvido explicitamente:
o efeito que faz a checagem passa a depender de um **número de tentativa** guardado no provider, e a ação de tentar de
novo só **incrementa** esse número; o efeito roda de novo, o estado volta para "checando" e a tela mostra carregando.

É um mecanismo, e não dois: a checagem continua num lugar só — o efeito —, e não há caminho paralelo que possa
divergir dele. A alternativa, extrair a função e chamá-la do botão, teria o mesmo efeito com duas portas de entrada
para o mesmo estado.

### D16 — Uma tela de falha, e o nome dela

O requisito manda que a tela seja **a mesma** nas duas portas, e o componente é literalmente o mesmo — ele só recebe o
que "tentar de novo" faz. Com isso, `InfoFailure` ficou com nome errado: ela não é a falha do `/info`, é a falha da
**casca**. Passa a `ShellFailure`, ao lado do `shellState`, que é quem decide mostrá-la. O arquivo nasceu nesta mesma
fatia e ainda não foi publicado, então renomear não custa história nenhuma.

A mensagem é **uma só** nas duas portas: "Não foi possível carregar o painel." — é a falha de carregar o painel, e ela
não afirma nada sobre a configuração do cliente (D7). Mantê-la idêntica é o que o requisito pede ao dizer "a mesma
mensagem". (Foi este grupo que motivou a revisão do texto registrada em D7: servindo às duas portas, a frase antiga
falava de uma configuração que, na falha da checagem de sessão, ninguém chegou a pedir.)

## Risks / Trade-offs

- **Risco: o painel inteiro some por causa de uma consulta só.** É o preço explícito de D1, e a mitigação está em D5
  (quatro tentativas antes de desistir) e em D6 (resposta em mãos continua valendo). Quem está trabalhando só perde a
  tela se o `/info` falhar **e** nunca tiver respondido.
- **Risco: a mensagem envelhecer para o lado errado.** Alguém "melhora" o texto e volta a afirmar configuração. É por
  isso que o MUST NOT entra na spec, e não só no código.
- **Trade-off: a função pura é testada, a renderização não.** O limite herdado de 1.6/4.0/4.8/4.10 não desaparece —
  encolhe. Anotado nas tarefas como limite, e não como pendência silenciosa.
- **Risco: manter o token numa falha que não passa.** Se o servidor ficar fora por muito tempo, o usuário fica na tela
  de falha com um token guardado que ninguém conseguiu validar. É o preço de não destruir sessão por indisponibilidade,
  e a saída é o botão "Sair" (D11), que é local e funciona justamente aí. Um token que de fato não vale mais cai no
  primeiro 401 que o servidor conseguir responder.
- **Risco: a tela de falha virar uma terceira forma de casca.** Se ela mantiver meia topbar, passa a existir um layout a
  mais para manter. É o que D11 evita: a saída vira um botão próprio, e a topbar não é desenhada.

## Suposições (a confirmar, baratas de reverter antes da implementação)

- **S1 — "falhou" é a consulta esgotada depois do retry padrão** (3 tentativas), sem política nova (D5). A alternativa
  seria desligar o retry para o `/info` e falhar na primeira — mais rápido para ver o problema, pior para o soluço de
  rede.
- **S2 — a tela de falha vale para qualquer papel**, inclusive Admin. Não há versão "de diagnóstico" com o status HTTP
  para o Admin: isso recriaria a decisão na tela, e o Admin do produto é a Fiscosys (ADR-0039 §5), que tem os logs.
- **S3 — o texto é "Não foi possível carregar o painel." e o botão é "Tentar de novo"** (D7). O texto exato é de
  produto, e foi decidido pelo Marcelo em 2026-10-15 (a nota em D7 diz por que a palavra "configuração" saiu); o
  requisito é o que ele **não** pode dizer.
- **S4 — a falha de recarga não derruba o painel** (D6). A alternativa — qualquer falha do `/info` leva à tela de falha,
  mesmo com resposta em mãos — é mais simples de explicar e muito pior de usar: salvar em Configurações passaria a poder
  derrubar a sessão.
- **S5 — a tela de falha ocupa a área inteira**, como o `Loading` de hoje, e não um painel dentro da casca.
- **S6 — nada muda nos outros consumidores**, e o `canViewRawJson(undefined) → false` fica como está, como defesa em
  profundidade (D9).
- **S7 — não entra stack de teste de componente nesta fatia** (D3). É o que justifica a função pura.
- **S8 — a classificação olha o status, e erro sem status é "falhou"** (D13). Falha de rede e timeout não têm resposta
  HTTP, logo não têm status: cair em "falhou" é o certo, e é também o **lado seguro** — na dúvida, mantém o token. Só
  um 401 vindo do cliente de API derruba a sessão.
- **S9 — a mesma mensagem serve às duas portas** (D16). O texto é de produto (S3); o requisito é que a tela seja uma
  só e que a mensagem não afirme configuração.

## Open Questions

### ~~Q1 — A tela de falha mantém a topbar com o menu do usuário?~~ **Respondida (Marcelo, 2026-10-15): não — ela tem o próprio botão "Sair"**

O caminho do meio que a pergunta já anotava. A razão está em **D11**, e a regra está na spec (bullet e cenário da
requirement "Sem o `/info`, o dashboard não desenha o painel"). Não bloqueia mais o grupo 3.

### Q2 — Adotar stack de teste de componente (não bloqueia)

`@testing-library/react` + jsdom resolveriam o limite de 1.6/4.0/4.8/4.10 na raiz, em vez de contorná-lo com função
pura. É fatia própria, com custo próprio (dependências, configuração do vitest, tempo de suíte), e não cabe numa change
cujo diff é de um punhado de linhas de tela.
