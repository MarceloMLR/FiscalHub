Esta change não muda comportamento, e por isso o `tasks.md` é diferente do normal: não há teste escrito antes (design,
D6). Cada capability tem um grupo com três passos:

- **ler o código** de novo contra a spec delta;
- **fechar a spec** com a decisão do Marcelo sobre os achados dela;
- **validar.**

O último grupo apaga o rascunho, alinha o STATUS e roda as guardas.

**A regra dos achados.** Divergência nova encontrada no caminho vira achado no `design.md`, e é trazida ao Marcelo.
Ela não é ajustada nem na spec nem no código sem aval. Se o Marcelo decidir trazer uma correção de código para esta
change, o apply para. O `proposal.md` e este arquivo são revistos com `/opsx:update`, com o teste antes, e só então o
apply segue.

Uma tarefa só recebe `[x]` com evidência.

## 1. `integration-status-poll` (ADR-0007, ADR-0010; achados A1, A2, A3 e A4)

- [x] 1.1 Ler de novo, contra `specs/integration-status-poll/spec.md`:
  - `Application/Pipeline/StatusPoller.cs` e `Application/Outbound/IntegrationStatus.cs`;
  - `Host/StatusPollingService.cs` e `Host/Program.cs:168-171`;
  - o `ListPendingAsync`, o `MarkPolledAsync` e o `AlreadyProcessedAsync` do `SqlProcessingStore.cs`;
  - o `CheckStatusAsync` do `AvalaraComplianceDispatcher.cs`.

  Uma divergência nova vira achado, pela regra acima.

  **Feito (2026-10-09).** Nenhuma divergência nova. O `git status` não mostra mudança em `src/` desde a conferência
  do propose. Cada frase da spec foi conferida, com a linha que a sustenta:
  - **Em voo:** é `Submitted` com id externo, na ordem do `Id` e sem filtro de tenant (`SqlProcessingStore.cs:97-109`).
  - **O tenant da consulta:** é o do documento (`StatusPoller.cs:52`).
  - **O motivo da recusa:** é a mensagem que a plataforma devolve (`StatusPoller.cs:68`).
  - **O reenvio:** zera as tentativas (`SqlProcessingStore.cs:163`).
  - **A reentrada:** o `Unconfirmed` não a bloqueia (`SqlProcessingStore.cs:25-31`).
  - **O cancelamento:** interrompe a passada (`StatusPoller.cs:32` e `:38`).
- [x] 1.2 Registrar aqui a decisão do Marcelo sobre A1, A2, A3 e A4, com a data, e aplicá-la na spec:
  - **Achado que vira change própria:** a spec fica como está, e a change é citada aqui pelo nome.
  - **Achado aceito como está:** a spec fica como está.
  - **Leitura corrigida pelo Marcelo:** a spec muda, e o achado no `design.md` registra a versão final.

  **Decisão do Marcelo (2026-10-09).** A spec não muda.
  - **A1:** vira change de correção própria, com o teste antes. É a **primeira** da fila. A change ainda não existe. O
    nome dela fica para quando for proposta, e o STATUS a registra em "Próximos passos" e no "Defeitos conhecidos" D1.
    Quando ela for arquivada, a frase que saiu da spec ("se a falha persistir, o limite a move para `Unconfirmed`")
    volta, por delta, no requisito "Uma falha não derruba o lote".
  - **A2:** vai junto na change do A1, porque são os mesmos arquivos. Se ela ligar o lote e o limite à configuração, o
    delta dela troca o "por padrão" por "configurável".
  - **A3:** confirmado. A casca é do host.
  - **A4:** confirmado. Nas ADRs, "item em aberto" quer dizer "não bloqueia a reentrada", e isso vale para os três
    estados. Fica uma questão de produto, fora desta change: o card "Com erro" soma `Unconfirmed`, `DeadLettered` e
    `IntegrationError`, e assim "a plataforma não respondeu" lê como "a nota foi rejeitada". O STATUS a registra como
    decisão em aberto.
- [x] 1.3 Validar:
  - conferir a tabela da `integration-status-poll` na seção "Cobertura" do `design.md` contra a spec final, cenário por
    cenário;
  - rodar `openspec validate document-implemented-capabilities --strict`, que deve sair verde.

  **Feito (2026-10-09).** A spec final é a do propose. A tabela cobre os 11 cenários e as duas regras sem cenário
  próprio, a ordem com os tenants e os padrões. O `openspec validate document-implemented-capabilities --strict` saiu
  "Change 'document-implemented-capabilities' is valid".

## 2. `dead-letter-visibility` (ADR-0010; achados A4, A5 e A6)

- [x] 2.1 Ler de novo, contra `specs/dead-letter-visibility/spec.md`:
  - o `DeadLetterHandler.cs` e o `DeadLetterTriggerService.cs`;
  - o `ServiceBusMessagingServiceCollectionExtensions.cs`;
  - o `RecordDeadLetterAsync` do `SqlProcessingStore.cs`;
  - o `DocumentReference.cs`.

  Conferir também que o requisito "Dead-letter da fila de descoberta visível" da `discovery-queue-consumer` continua
  consistente com a spec nova (design, D4).

  **Feito (2026-10-09).** Nenhuma divergência nova. A conferência:
  - **O registro:** é um upsert, que cria a linha quando ela não existe (`SqlProcessingStore.cs:43-44` e `:133-176`).
  - **A conclusão:** a mensagem só é concluída depois do registro (`DeadLetterTriggerService.cs:60-61`). Nada é
    publicado de volta: o handler só chama o store.
  - **A assinatura:** é uma por fila (`ServiceBusMessagingServiceCollectionExtensions.cs:63-69`).
  - **O corpo que não desserializa:** lança antes do store (`DeadLetterHandler.cs:18-19`).
  - **A `discovery-queue-consumer`:** consistente. Ela pede registro com "estado de dead-letter e o motivo do
    transporte, igual à fila de entrada", que é o que a spec nova descreve para as duas filas. O grupo da descoberta
    continua só nela.
- [x] 2.2 Registrar aqui a decisão do Marcelo sobre A5 e A6, com a data, e aplicá-la na spec como na 1.2. O A4 já foi
  decidido na 1.2: só conferir que as duas specs dizem a mesma coisa sobre a reentrada.

  **Decisão do Marcelo (2026-10-09).** A spec não muda.
  - **A5:** vira change de correção própria, com o teste antes. Ele sai de "aceitar" e entra na fila, e o STATUS o
    registra como D2, de prioridade alta. A ordem entre ele e o A7 não foi decidida. Só o A1 tem lugar marcado: o
    primeiro. Quando a correção for arquivada, o delta dela completa o cenário "Corpo
    irreconhecível" com o destino da mensagem.
  - **A6:** vai junto na change do A5, porque são os mesmos arquivos.
  - **A4:** as duas specs dizem a mesma coisa. "MUST NOT bloquear a reentrada", "como `IntegrationError`", está no
    requisito "Unconfirmed é estado próprio e não bloqueia a reentrada" e no "DeadLettered é estado próprio e não
    bloqueia a reentrada".
- [x] 2.3 Validar:
  - conferir a tabela da `dead-letter-visibility` na seção "Cobertura" contra a spec final;
  - rodar `openspec validate document-implemented-capabilities --strict`, que deve sair verde.

  **Feito (2026-10-09).** A tabela tem uma linha para cada um dos 9 cenários da spec final. O validate saiu verde.

## 3. `support-ticket` (ADR-0021; achados A7, A8 e A9)

- [x] 3.1 Ler de novo, contra `specs/support-ticket/spec.md`:
  - `Application/Support/*`;
  - `Adapters/Support/*`;
  - `Infrastructure/Support/BlobNoteTraceReader.cs`;
  - o `ListByKeysAsync` do `SqlDocumentQueries.cs`;
  - `Host/Program.cs:792-828`.

  Conferir também que a spec não contradiz o "Abrir chamado" da `platform-response-trace`.

  **Feito (2026-10-09).** A spec confere com o código:
  - **O escopo de tenant:** está na busca das notas (`SqlDocumentQueries.cs:145-163`) e na leitura das fotos, pelo
    prefixo do tenant (`BlobNoteTraceReader.cs:27`).
  - **O nome do adapter:** é comparado sem maiúscula (`SupportTicketService.cs:62-63`).
  - **O nome do zip:** sai do `SafeName` (`:174-183`).
  - **O anexo extra vazio:** é pulado (`:97-100`).
  - **A recusa:** responde 400 (`Program.cs:817-820`).
  - **O teto:** vai na resposta da estimativa (`Program.cs:827`).
  - **A `platform-response-trace`:** sem conflito. Ela diz que qualquer papel abre chamado e que o zip vai para o
    suporte. A spec nova não restringe papel e não devolve o zip.

  Há uma correção, mas **no design, e não na spec**. O A7 dizia que o `apiKey` é gravado "pela tela", e a tela de
  Conectores não tem os campos de chamados: o caminho é o `PUT /connector`. O A7 foi corrigido no `design.md`, e a
  conclusão não muda. A mesma leitura achou o S5 no STATUS (ver 4.1).
- [x] 3.2 Registrar aqui a decisão do Marcelo sobre A7, A8 e A9, com a data, e aplicá-la na spec como na 1.2. O A7 não
  muda a spec. A decisão dele é sobre a change de correção.

  **Decisão do Marcelo (2026-10-09).** A spec não muda.
  - **A7:** vira change de correção própria, com o teste antes. O STATUS o registra como D3. A correção também precisa
    decidir para onde a mensagem do segredo ausente aponta (design, A7).
  - **A8 e A9:** aceitos, como dívida cosmética. Ficam registrados na tabela de achados do `design.md`. O A9 continua
    escrito na spec como o código faz. Se um dia a frase virar condicional, o requisito "A descrição leva o estado de
    cada nota" muda por delta.
- [x] 3.3 Validar:
  - conferir a tabela da `support-ticket` na seção "Cobertura" contra a spec final;
  - rodar `openspec validate document-implemented-capabilities --strict`, que deve sair verde.

  **Feito (2026-10-09).** A tabela tem os 15 cenários da spec final e duas regras sem cenário próprio: o tenant com o
  HTTP 400, e os gateways. O validate saiu verde.

## 4. Fechamento

- [x] 4.1 No `docs/STATUS.md`, aplicar só as correções S1, S2, S3 e S4 que o Marcelo aprovou (design, seção
  "STATUS.md"). Uma correção recusada fica anotada aqui, com o motivo.

  **Fechado (2026-10-09).** O Marcelo aprovou o S5 e o S6, com o texto dele, e os dois estão aplicados:
  - **S5:** a última frase do D3 virou o agravante. O chamado está exposto na tela, e o provider só entra pelo
    `PUT /connector`.
  - **S6:** o D4 ficou com o A8 e o A9. O A2 e o A6 foram para um D5 novo.

  Nenhuma correção foi recusada.

  **O registro de quando a tarefa estava em aberto:**
  - **S1 a S4: feitos pelo próprio Marcelo**, e conferidos contra o código:
    - S4 na linha 21 ("24 projetos (22 fora de `tools/`)");
    - S3 nas linhas 39-42 ("fechado só em parte");
    - S1 nas linhas 44-46 (o Freshdesk "não funciona hoje");
    - S2 nas linhas 75-77 (três estados próprios, nenhum bloqueia a reentrada, e o card "Com erro" como decisão em
      aberto).
  - **S5 e S6 aguardam aval.** São duas divergências novas, na seção "Defeitos conhecidos" que ele acrescentou, e o
    design as descreve:
    - **S5:** o D3 diz que "o dashboard não tem tela de chamados", mas o modal de abrir chamado existe. O que falta
      são os campos de chamados em Conectores.
    - **S6:** o D4 dá como "aceitos" o A2 e o A6, que vão nas correções do D1 e do D2.

  A tarefa fecha quando o Marcelo aprovar ou recusar os dois.
- [x] 4.2 Apagar o `docs/specs-retroativas-rascunho.md`. No STATUS, o item 1 de "Próximos passos" aponta para ele: o
  item sai, ou passa a apontar para as três specs. A prova é o
  `grep -r specs-retroativas-rascunho --exclude-dir=openspec`, que deve voltar vazio. As citações dentro desta change
  ficam, como histórico.

  **Feito (2026-10-09).**
  - **O rascunho:** não estava no git, e por isso apagar seria sem volta. Antes, confirmei que ele não mudou desde a
    leitura do propose (é das 13:24) e guardei uma cópia no scratchpad da sessão.
  - **O item 1 de "Próximos passos":** passou a ser "Corrigir os defeitos conhecidos D1, D2 e D3", cada um numa
    change própria, com o teste antes, e o D1 primeiro, pela decisão do Marcelo. Ele cita as três specs pelo nome, sem
    link. O caminho em `openspec/specs/` só existe depois do archive.
  - **A prova:** o grep, sem o `openspec/`, `node_modules`, `.git`, `bin` e `obj`, voltou vazio.
- [x] 4.3 Rodar `openspec validate --all --strict`, que deve sair verde.

  **Feito (2026-10-09).** "Totals: 25 passed, 0 failed (25 items)": as 24 specs e esta change.
- [x] 4.4 Rodar as guardas:
  - `dotnet build`, com 0 warnings, e `dotnet test`, verdes;
  - `git diff --stat main` só mostra arquivos em `openspec/` e `docs/`, e nada em `src/`, `tests/`, `tools/` ou
    `dashboard/`.

  **Feito (2026-10-09).**
  - **O `dotnet build` comum falhou.** Os 22 erros eram só MSB3021 e MSB3027, e as 220 warnings só MSB3026: cópia de
    DLL travada pelo `FiscalHub.Host` em execução (PID 56244). Não houve nenhum erro nem warning de compilador. O Host
    não foi derrubado.
  - **A guarda rodou com a saída noutro diretório.** Foram o `dotnet build --artifacts-path obj/guard` e o
    `dotnet test --artifacts-path obj/guard --no-build`. O `obj/guard` fica na raiz, é ignorado pelo git e foi
    apagado depois.
    - **O build:** 0 warnings e 0 erros.
    - **Os testes:** 1.212 passaram, 3 foram pulados e nenhum falhou, nos 10 projetos.
  - **Uma tentativa anterior, fora do repo, deu falso negativo.** A saída foi para o scratchpad, e falharam 98 testes,
    todos de ambiente:
    - os fixtures do D365, por caminho acima de 260 caracteres;
    - o `DevSeedTests` e o `SandboxFixtureTests`, que não acharam a raiz do repositório.
  - **O diff:** o `git diff --stat main` mostra só o `docs/STATUS.md`. O `git status` acrescenta só a pasta desta
    change. O rascunho não aparece porque nunca esteve no git.
