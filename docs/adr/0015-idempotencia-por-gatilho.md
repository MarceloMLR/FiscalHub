# ADR-0015: Idempotência por gatilho (evento dedupa, manual recarrega)

- **Status:** Aceito
- **Data:** 2026-07-24
- **Refina:** ADR-0007 (idempotência por estado), ADR-0014 (integração manual)

## Contexto

A idempotência por estado (ADR-0007) bloqueia reenvio de notas em estado terminal
(`Submitted`/`Confirmed`). Isso é correto para gatilhos **por evento**, mas errado para a
**integração manual**: o cliente que recarrega um período de propósito (porque corrigiu algo na
origem) espera que as notas sejam reprocessadas, mesmo já confirmadas. A mesma regra não serve aos
dois gatilhos.

O cenário que fecha o raciocínio, no gatilho por evento: o cliente lança uma nota, o evento dispara,
a nota integra e fica `Confirmed`. Depois o evento redispara a **mesma chave de acesso**. Fato do
domínio: uma **NF-e autorizada é imutável** — a chave identifica aquele documento de forma única.
Então o redisparo da mesma chave é um **evento duplicado** (fontes de evento entregam
*at-least-once*), e reenviar duplicaria na plataforma de destino. Se o cliente de fato mudou o
conteúdo fiscal, isso vira **outra nota, com outra chave** (cancelamento + reemissão) ou uma Carta
de Correção — documento distinto, que não cai no bloqueio.

## Decisão

- A `DocumentReference` carrega o **gatilho** (`IngestionTrigger`): `Event` (padrão) ou `Manual`.
- A esteira decide a política por gatilho:
  - **`Event`** → mantém a idempotência por estado: nota já `Submitted`/`Confirmed` não reentra
    (protege contra entrega duplicada de evento; a NF-e autorizada é imutável).
  - **`Manual`** → **fura** o bloqueio e reprocessa mesmo em estado terminal (intenção humana
    explícita de recarregar).
- O endpoint `POST /integrations/manual` marca `Trigger = Manual`; drop/Event Grid/fila seguem
  `Event`. Mensagens antigas, sem o campo, caem no padrão `Event`.

## Alternativas consideradas

- **Regra única para todos os gatilhos** — ou bloquearia a recarga manual legítima, ou deixaria o
  evento duplicado reenviar e duplicar no destino. Nenhum extremo atende os dois casos.
- **Endpoint manual que apaga o registro antes de reenfileirar** — perderia o histórico/rastreio da
  nota e mascararia que houve reprocesso. Furar o bloqueio preserva a linha e só ressubmete.

## Consequências

- Recarregar um período pela integração manual **sempre reprocessa** — verificado ao vivo: uma nota
  `Confirmed` recarregada gerou nova submissão (novo `ExternalId`).
- O gatilho por evento segue protegido contra duplicatas — sem mudança de comportamento.
- O gatilho fica disponível na referência para, adiante, o dashboard exibir o **modo** (Em Tempo
  Real / Manual / Agendada) — persistir isso fica para a fatia do dashboard.
- Ressalva de escopo: no caminho manual, uma reentrega do transporte (retry após falha no meio do
  processamento) pode ressubmeter, pois o guard está desligado para `Manual`. Aceitável na demo;
  em produção séria, um controle de reenvio por execução (id da recarga) evitaria isso.
