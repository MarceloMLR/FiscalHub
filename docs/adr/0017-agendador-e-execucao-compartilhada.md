# ADR-0017: Agendador in-process + execução compartilhada

- **Status:** Aceito
- **Data:** 2026-07-24

## Contexto

Além dos gatilhos por evento (drop/Event Grid), por fila e manual, o cliente precisa de integrações
**agendadas**: uma recorrente diária que processa o dia anterior (**D-1**) e uma **única** marcada
para uma data/hora futura sobre um período explícito. O painel precisa mostrar cada disparo como uma
**execução** (modo, empresa/filial, período, quantas notas), numa tela separada — Documentos segue
como está.

## Decisão

- **Runner compartilhado** (`IIntegrationRunner`): concentra o disparo — descobre as notas do
  período, enfileira cada referência (claim-check) e registra a execução. Usado tanto pela
  integração manual quanto pelo agendador; um só caminho, sem duplicar lógica.
- **Agendamentos persistidos** (`ScheduledIntegration`, tabela própria) — sobrevivem a restart.
- **Executor** = `IntegrationScheduler` (lógica pura, testável) chamado por um `BackgroundService`
  num timer. A cada passada: pega os vencidos (`NextRunAt <= now`), calcula o período (D-1 = dia
  anterior ao disparo em BRT; único = período explícito), dispara pelo runner e reprograma (diário:
  +1 dia; único: desativa). No cloud, o `BackgroundService` vira um **timer trigger de Functions** —
  a lógica não muda. Falha de um agendamento não derruba os outros (fica pra próxima passada).
- **Idempotência do agendado = por conteúdo, não fura** (diferente do manual): o lote agendado é uma
  **rede de segurança** pro que o tempo-real por acaso perdeu; o dedupe por conteúdo (ADR-0016)
  garante que ele não reintegra o que já foi resolvido. Só o manual, ação explícita, fura.
- `NextRunAt` é gravado em **ticks UTC** (inteiro) — compara e ordena igual em SQLite e SQL Server
  (o `DateTimeOffset` não traduz no `WHERE` do SQLite).

## Alternativas consideradas

- **Hangfire / Quartz / cron externo** — poderosos, mas overkill pro escopo; um `BackgroundService`
  com timer basta e é portável direto pra Functions.
- **Vincular cada nota à execução que a disparou** (execution_id na nota, pra contagens de
  processadas/erro por execução) — mais completo; adiado. Por ora a execução guarda quantas notas
  descobriu, e o desfecho por nota fica na tela de Documentos.

## Consequências

- Agendamentos e execuções ficam persistidos; a tela de Agendamentos lê `GET /schedules` e a de
  Execuções lê `GET /executions`.
- O timer é **in-process, uma instância**. Escalar pra várias instâncias exigiria lock/leasing pra
  não disparar o mesmo agendamento em paralelo — anotado como evolução (no cloud, o timer de
  Functions já resolve isso com singleton).
- D-1 é relativo à **data do disparo** em horário de Brasília; um disparo atrasado ainda processa o
  dia anterior à data agendada, não à data em que efetivamente rodou.
