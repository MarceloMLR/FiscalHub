# ADR-0016: Idempotência por conteúdo (nota de entrada pode ser corrigida)

- **Status:** Aceito
- **Data:** 2026-07-24
- **Refina:** ADR-0007 (idempotência por estado), ADR-0015 (idempotência por gatilho)

## Contexto

O ADR-0015 justificou o dedupe do gatilho por evento na **imutabilidade da NF-e autorizada**. Isso
só vale para **notas de saída** (emitidas): uma vez autorizada, o documento não muda; corrigir é
cancelar e reemitir (chave nova) ou mandar uma CC-e.

Mas o conector também trata **notas de entrada**. O cliente **dá entrada** dessas notas no ERP dele —
os dados que alimentam a integração vêm do que ele digitou. Se ele errou um valor e depois corrige,
é a **mesma chave de acesso com conteúdo diferente**. Num ERP em tempo real, o gatilho de alteração
vai redisparar essa nota, e ela **deve reintegrar** com o valor certo — mesmo já existindo/confirmada.
A regra "chave já confirmada → ignora" barraria exatamente a correção legítima.

## Decisão

- Idempotência deixa de ser por **existência/estado** e passa a ser por **conteúdo**: guardamos uma
  impressão (`ContentHash`, SHA-256) do **cru** exatamente como veio da origem (XML ou JSON), e
  comparamos por ela.
- A esteira **busca primeiro** (para conhecer o cru) e só então decide:
  - Já existe registro terminal (`Submitted`/`Confirmed`) com o **mesmo hash** → duplicata de
    entrega (evento *at-least-once*) → **ignora**.
  - Hash **diferente** → o cliente corrigiu a nota → **reintegra** (regrava o hash novo).
- A fonte (`IInboundSource.FetchAsync`) devolve `FetchResult { Document, ContentHash }`; o hash é do
  texto cru, sem normalizar.
- Recarga **manual** (ADR-0015) segue furando a idempotência de propósito.

Assim os dois casos se resolvem sem olhar entrada/saída na hora de decidir: a nota de saída imutável
nunca muda de hash (dedupa sozinha); a de entrada corrigida muda de hash (reintegra). A direção
(entrada/saída) vira informação de exibição, não regra.

## Alternativas consideradas

- **Hash do domínio (JSON traduzido)** em vez do cru — pegaria só os campos que usamos, mas
  esconderia mudanças em campos que hoje ignoramos e amanhã podem importar; o cru é a fonte fiel do
  que o cliente mandou.
- **Versão/carimbo de tempo da origem** — ideal quando o ERP entrega um número de versão; fica como
  evolução (se a origem expuser versão, dá pra usar no lugar do hash). Sem isso, o hash do cru é
  autossuficiente.
- **Manter dedupe por estado** — barraria a correção de nota de entrada; foi o que este ADR corrige.

## Consequências

- Reintegração de correção funciona: mesma chave, cru diferente → nova submissão. Duplicata de
  evento (cru idêntico) continua ignorada. (Verificado: re-drop do mesmo conteúdo manteve o
  `ExternalId`; teste cobre o cru diferente reintegrando.)
- A esteira agora **sempre busca** antes de decidir (precisa do cru pra calcular o hash) — perde-se
  o atalho de pular sem buscar; custo baixo e correção exige.
- Coluna `ContentHash` no rastreio (migration). Linhas antigas ficam com hash nulo; uma reentrega
  delas conta como conteúdo diferente e reintegra uma vez — comportamento aceitável na transição.
