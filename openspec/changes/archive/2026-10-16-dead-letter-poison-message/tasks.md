# Tarefas — a mensagem envenenada na dead-letter

A fatia é **uma decisão**: separar "a mensagem é inválida" de "o tratamento falhou". A ordem é a do risco: primeiro a
baseline (grupo 1), depois os testes **vermelhos antes de verdes** sobre o handler, que é onde a decisão passa a morar
(grupo 2), só então a produção (grupo 3), depois a prova por inversão — que é o que justifica a change ter teste
(grupo 4), o ponta a ponta opcional (grupo 5), e por fim a spec, os comentários que ficaram falsos e o `STATUS.md`
(grupo 6).

- **Grupo 1:** baseline medida — suíte de hoje, build limpo.
- **Grupo 2:** os testes do handler, escritos contra a assinatura nova, vermelhos.
- **Grupo 3:** a produção, até os testes do grupo 2 ficarem verdes.
- **Grupo 4:** a prova por inversão — desfeita cada metade do conserto, o teste **tem** que ficar vermelho.
- **Grupo 5:** (opcional) o ponta a ponta contra o emulador do Service Bus, pulado por padrão.
- **Grupo 6:** a spec, os comentários, o `STATUS.md` e a verificação final.

Cada grupo de código termina com `dotnet build` (**0 avisos** — `TreatWarningsAsErrors`) e `dotnet test`. Com o Host
rodando, usar `--artifacts-path obj/guard` (dentro do repositório, nunca fora).

Uma tarefa só recebe `[x]` **com evidência** — a saída do teste, do build ou do comando. Tarefa movida ou feita pela
metade fica **aberta**, com a anotação do que faltou.

**Pare e diga se alguma tarefa parecer exigir:** gravar a mensagem descartada no store ou no Blob; tabela, fila ou
contador novo; mexer no `ServiceBusTriggerService`, no `QueuedDocumentProcessor`, no `IProcessingStore`, no
`SqlProcessingStore`, no `Program.cs`, em migration, em `.csproj` de produção ou em qualquer arquivo de `dashboard/`;
trazer biblioteca de mock; ou tocar no `StatusPoller` (D1) e nos gateways de suporte (D3). São os não-objetivos do
`proposal.md`, e nenhum deles cabe aqui.

**A decisão em aberto que o `design.md` tomou sozinho** (Open Question 1 — a mensagem descartada fica **só no log**,
sem artefato durável e sem tela): se o Marcelo decidir o contrário **antes** da implementação, este arquivo muda do
grupo 2 em diante. Conferir antes de começar.

---

## Baseline a confirmar

| O quê | Esperado |
|---|---|
| `dotnet build` da solução | **0 avisos, 0 erros** |
| `dotnet test` da solução | verde |
| `FiscalHub.Adapters.Messaging.ServiceBus.Tests` | **7 aprovados** (3 em `DeadLetterHandlerTests` + 4 em `DiscoveryQueueRegistrationTests`) |

Divergindo nesta execução, **vale o número novo**, e a divergência fica anotada.

**Divergência medida (2026-10-16):** o projeto tem **12 aprovados**, não 7. A tabela esqueceu o terceiro arquivo do
projeto, `QueuedDocumentProcessorTests.cs` (**5 testes**). A conta é 3 (`DeadLetterHandlerTests`) + 4
(`DiscoveryQueueRegistrationTests`) + 5 (`QueuedDocumentProcessorTests`) = 12. Nada mudou nos três testes do
`DeadLetterHandlerTests`, que continuam sendo os que a fatia reescreve. Vale **12**.

## 1. Baseline

- [x] 1.1 Rodar `dotnet build` na solução e anotar tempo, avisos e erros. Tem que estar **0/0** antes de qualquer
      mudança — baseline que não é limpa não prova nada depois.
      **Evidência:** `Compilação com êxito. 0 Aviso(s) 0 Erro(s) — Tempo Decorrido 00:03:56.37`.
- [x] 1.2 Rodar `dotnet test` só no projeto `FiscalHub.Adapters.Messaging.ServiceBus.Tests` e anotar o total. Conferir
      contra os **7** da tabela.
      **Evidência:** `Com falha: 0, Aprovado: 12, Ignorado: 0, Total: 12`. Diverge dos 7 — ver a nota da tabela acima.
- [x] 1.3 Rodar `dotnet test` na solução inteira e anotar o total (aprovados / falhas / ignorados). É o número contra o
      qual o grupo 6 vai comparar.
      **Evidência:** **1408 aprovados / 0 falhas / 42 ignorados / 1450 no total**, em 11 projetos:
      Application 502, Host 24, Directory.Json 3, Discovery.Local 12, Ingress.BlobDrop 9, Inbound.Xml 24,
      Integration 31, Infrastructure 159 (+40 ignorados), **Messaging.ServiceBus 12**, Ingress.D365Poll 244
      (+2 ignorados), Outbound.Avalara 388.
- [x] 1.4 Confirmar no código que o diagnóstico vale: `DeadLetterHandler.cs:18-19` lança por corpo inválido, e
      `DeadLetterTriggerService.cs:56-58` resolve o motivo com `??`. Se o código já tiver mudado (outra sessão,
      rebase), **pare e diga** — a fatia inteira é escrita sobre essas duas linhas.
      **Evidência:** as duas linhas estão **exatamente** onde a proposta as cita.
      `DeadLetterHandler.cs:18-19` = `body.ToObjectFromJson<DocumentReference>(…) ?? throw new InvalidOperationException(…)`;
      `DeadLetterTriggerService.cs:56-58` = `args.Message.DeadLetterReason ?? args.Message.DeadLetterErrorDescription ?? "Mensagem movida para dead-letter."`.
      O rebase em cima do main novo **não** tocou nenhum dos dois arquivos de produção; tocou só o duplo `FakeStore`
      do teste, que ganhou o `RecordSavedAsync` da fatia `capture-without-dispatch`.

## 2. Os testes do handler, vermelhos

Tudo neste grupo é teste. Nenhuma linha de `src/` muda aqui, e no fim dele a suíte está **vermelha de propósito**.

- [x] 2.1 Criar `tests/Adapters/Messaging/…ServiceBus.Tests/CapturingLogger.cs`: duplo de `ILogger<T>` que guarda nível,
      mensagem formatada e exceção de cada entrada. Copiar a forma do `…Avalara.Tests/CapturingLogger.cs` — projeto de
      teste não referencia projeto de teste, então é cópia, e o comentário de cabeçalho diz de onde veio.
      **Evidência:** arquivo criado; guarda `(LogLevel, string Text, Exception? Error)` e expõe `Entries`, `Errors` e
      `All`. O cabeçalho cita a origem da cópia.
- [x] 2.2 Adaptar os 3 testes existentes de `DeadLetterHandlerTests` à assinatura nova (`DeadLetterMessage` entrando,
      `DeadLetterOutcome` saindo). Os dois primeiros — motivo gravado, referência da fila de descoberta — **mantêm a
      afirmação**: continuam provando que a referência válida vira registro. O terceiro,
      `Handle_throws_on_empty_body`, **inverte**: passa a afirmar descarte, e o nome muda junto.
      **Evidência:** `Handle_records_dead_letter_with_reason` e `Discovery_queue_reference_is_recorded_as_dead_lettered_too`
      mantêm as asserções e ganham `Assert.Equal(DeadLetterOutcome.Recorded, outcome)`;
      `Handle_throws_on_empty_body` → **`Null_body_is_discarded_and_never_throws`**.
- [x] 2.3 Teste: **corpo que não é JSON** → devolve `Discarded`, nada no `FakeStore`, e **uma** entrada de nível erro no
      `CapturingLogger`.
      **Evidência:** `Body_that_is_not_json_is_discarded_and_logged_once` (`Assert.Single(logger.Entries)` — um registro
      por mensagem, não um por entrega).
- [x] 2.4 Teste: **corpo `null`** → mesmo desfecho (é o antigo `Handle_throws_on_empty_body`, invertido).
      **Evidência:** `Null_body_is_discarded_and_never_throws`.
- [x] 2.5 Teste: **JSON sem campo obrigatório** (ex.: `{}` e `{"tenantId":"t"}`) → `Discarded`. Confirma que o
      `required` do `DocumentReference` lança em vez de produzir objeto meio-vazio (design, D4, porta 1).
      **Evidência:** `Json_missing_a_required_field_is_discarded`, Theory com 3 casos (`{}`, `{"tenantId":"t"}` e um
      sem `locator`).
- [x] 2.6 Teste: **`TenantId` em branco** e **`NaturalKey` em branco** (vazio e só-espaços) → `Discarded`, e **nenhuma**
      gravação no `FakeStore`. É a porta 3 do D4, e hoje grava linha órfã.
      **Evidência:** `Blank_tenant_or_natural_key_is_discarded_and_nothing_is_stored`, Theory com 5 casos (vazio e
      só-espaços em cada posição, mais os dois vazios).
- [x] 2.7 Teste: o registro do descarte **contém** o corpo, a fila, o `MessageId`, o `SequenceNumber`, o instante de
      enfileiramento e o motivo do transporte. Afirmar campo a campo, e não "logou alguma coisa" — a asserção frouxa
      passaria com o log de hoje.
      **Evidência:** `The_discard_record_carries_the_body_the_queue_and_the_message_identity` — seis `Assert.Contains`,
      um por campo.
- [x] 2.8 Teste: **corpo maior que o teto** → o registro traz o começo do corpo e **diz o tamanho original**.
      **Evidência:** `A_body_past_the_limit_is_cut_and_the_record_says_the_original_size` — afirma que o corpo inteiro
      **não** está no registro, que os primeiros `BodyLogLimit` caracteres estão, e que o tamanho original aparece.
- [x] 2.9 Teste: **o store lança** (`FakeStore` configurado para falhar) → a exceção **propaga** do handler. É a metade
      "o tratamento falhou" da distinção, e é o teste que impede que alguém conserte isto com `catch`.
      **Evidência:** `A_store_that_fails_propagates_and_is_not_swallowed` — e também `Assert.Empty(logger.Errors)`,
      para o `catch` que loga e engole não passar.
- [x] 2.10 Testes do motivo (D5), sobre a função pura: `DeadLetterReason` preenchido → ele; vazio e só-espaços com
      descrição preenchida → a descrição; os dois em branco → "Mensagem movida para dead-letter."; os dois nulos → o
      mesmo texto padrão. Quatro casos, cobrindo nulo **e** vazio em cada posição.
      **Evidência:** `Blank_counts_as_absent_when_the_reason_is_resolved`, Theory com **8** casos (as quatro formas
      pedidas, cada uma em nulo e em branco), sobre `DeadLetterMessage.ResolveReason`. Mais
      `A_blank_reason_never_reaches_the_store`, que prova o piso chegando ao store.
- [x] 2.11 Rodar o projeto de teste e confirmar que ele **não compila ou falha** — vermelho esperado, porque a
      assinatura nova ainda não existe. Anotar a saída. Um grupo 2 que fica verde é sinal de que o teste não está
      afirmando o que devia.
      **Evidência:** `dotnet build` do projeto de teste → `2 Erro(s)`:
      `DeadLetterHandlerTests.cs(222,20): error CS0246: O nome do tipo ou do namespace "DeadLetterMessage" não pode ser
      encontrado` (e a mesma em 229,20). Vermelho de compilação, como previsto.

## 3. A produção

- [x] 3.1 Criar `src/Adapters/Messaging/…ServiceBus/DeadLetterMessage.cs`: o record dos fatos da mensagem, o enum
      `DeadLetterOutcome` e a função pura do motivo (ausente **ou em branco** cai para o próximo; o texto padrão é o
      piso). Tudo `internal`. Comentário de decisão em português, dizendo por que em branco e ausente são a mesma coisa
      **neste** campo (design, D5 do STATUS.md / `proposal.md` §3).
      **Evidência:** arquivo criado com `internal readonly record struct DeadLetterMessage`,
      `internal enum DeadLetterOutcome { Recorded, Discarded }`, `DeadLetterMessage.ResolveReason(string?, string?)` e
      a const `DefaultReason`. O comentário do `ResolveReason` traz a justificativa de nulo × vazio **neste** campo.
- [x] 3.2 `DeadLetterHandler`: recebe `DeadLetterMessage`, devolve `DeadLetterOutcome`, ganha `ILogger<DeadLetterHandler>`.
      Corpo inválido → log de erro + `Discarded`, **sem lançar**. Corpo válido → resolve o motivo pela função pura,
      grava, devolve `Recorded`. Exceção do store **continua propagando** (não envolver em `try`).
      **Evidência:** o `try/catch` cobre **só** o `ToObjectFromJson` e pega **só** `JsonException`; a chamada
      `_store.RecordDeadLetterAsync` está fora dele. O DI não mudou (`services.AddScoped<DeadLetterHandler>()` já
      resolve o `ILogger<>`).
- [x] 3.3 `DeadLetterTriggerService.OnMessageAsync`: monta o `DeadLetterMessage` a partir do `args.Message` e do
      `_queueName`, chama o handler, e **conclui a mensagem depois, como hoje**. A cadeia de `??` sai daqui. Conferir
      que `CompleteMessageAsync` **não se moveu de lugar** — mover é o conserto errado (design, D0).
      **Evidência:** `git diff` do arquivo mostra a cadeia de `??` substituída pela montagem do `DeadLetterMessage`;
      as duas últimas linhas do método continuam na mesma ordem (`HandleAsync` e depois `CompleteMessageAsync`), e
      nenhum `try`/`finally` entrou.
- [x] 3.4 Atualizar os comentários de decisão dos dois arquivos: o do `DeadLetterHandler` ("Separado da casca pra ser
      testável") ganha a distinção inválido × falhou; o da casca diz que toda decisão mora no handler, e por quê.
      **Evidência:** o `<summary>` do handler ganhou o parágrafo da distinção; o da casca, o parágrafo "Nenhuma
      decisão mora aqui", com o motivo (é o único trecho sem teste unitário). Mais o comentário em cima do
      `CompleteMessageAsync`, dizendo por que a ordem não se move.
- [x] 3.5 Rodar `dotnet build` (0 avisos) e o projeto de teste. Os testes do grupo 2 ficam **verdes**. Anotar.
      **Evidência:** `Compilação com êxito. 0 Aviso(s) 0 Erro(s)` e
      `Com falha: 0, Aprovado: 33, Ignorado: 0, Total: 33` no projeto da mensageria.
- [x] 3.6 Rodar `dotnet test` na solução e comparar com 1.3. Só o projeto da mensageria muda de número; qualquer outro
      projeto mexendo é sinal de efeito colateral — **pare e diga**.
      **Evidência:** **1429 aprovados / 0 falhas / 42 ignorados / 1471 no total**. Contra o 1.3: **Messaging.ServiceBus
      12 → 33**; os outros dez projetos idênticos (Application 502, Host 24, Directory.Json 3, Discovery.Local 12,
      Ingress.BlobDrop 9, Inbound.Xml 24, Integration 31, Infrastructure 159+40, Ingress.D365Poll 244+2,
      Outbound.Avalara 388). Nenhum efeito colateral.

## 4. Prova por inversão

Cada item desfaz **uma** metade do conserto e confere que a suíte acusa. Sem isso, os testes do grupo 2 podem estar
passando por acidente. Restaurar e conferir (por hash ou `git diff` limpo) depois de cada um.

Hash de referência dos três arquivos de produção, medido antes da primeira inversão e reconferido depois da última:

| Arquivo | SHA-256 |
|---|---|
| `DeadLetterHandler.cs` | `f86e89ca54e6a8d7d3abb469b693a8c66952a5a7e8df9bb122cfdb3cfc9dc2bc` |
| `DeadLetterMessage.cs` | `b9dad1a7826f2a3b67dd7061e61b4dc53b97189a55819a01e4f5fe9acec75105` |
| `DeadLetterTriggerService.cs` | `f9f174c64307b4bc4a1b12c54eb1dd0e86d80c56379761e62ef8f1c323999712` |

- [x] 4.1 Trocar o descarte por `throw` de novo (volta o D2): os testes 2.3 a 2.8 **têm** que ficar vermelhos. Anotar
      quantos falharam.
      **Evidência:** `Com falha: 12, Aprovado: 21, Total: 33`. Os 12 são exatamente os de 2.3 a 2.8:
      `Body_that_is_not_json_is_discarded_and_logged_once`, `Null_body_is_discarded_and_never_throws`,
      `Json_missing_a_required_field_is_discarded` (3 casos),
      `Blank_tenant_or_natural_key_is_discarded_and_nothing_is_stored` (5 casos),
      `The_discard_record_carries_the_body_the_queue_and_the_message_identity`,
      `A_body_past_the_limit_is_cut_and_the_record_says_the_original_size`.
- [x] 4.2 Envolver a chamada do store num `try/catch` que devolve `Discarded` (o conserto errado, "concluir sempre"):
      o teste 2.9 **tem** que ficar vermelho. É o teste que mais importa desta change.
      **Evidência:** `Com falha: 1, Aprovado: 32, Total: 33` — e o único vermelho é
      `A_store_that_fails_propagates_and_is_not_swallowed`. O conserto errado **não** passa na suíte.
- [x] 4.3 Voltar a função do motivo para a cadeia de `??` (volta o D5): os casos de 2.10 com vazio **têm** que ficar
      vermelhos; os com nulo continuam verdes. Essa assimetria é a prova de que o teste cobre vazio, e não só nulo.
      **Evidência:** `Com falha: 5, Aprovado: 28, Total: 33`. Vermelhos: os **4** casos em branco do
      `Blank_counts_as_absent_when_the_reason_is_resolved` (`""`/descrição, `"   "`/descrição, `""`/`""`,
      `"  "`/`"   "`) mais o `A_blank_reason_never_reaches_the_store`. Os **4** casos com nulo e com motivo
      preenchido continuaram **verdes** — a assimetria pedida, medida.
- [x] 4.4 Tirar a guarda de tenant/chave em branco: o teste 2.6 **tem** que ficar vermelho.
      **Evidência:** `Com falha: 5, Aprovado: 28, Total: 33` — os 5 casos do
      `Blank_tenant_or_natural_key_is_discarded_and_nothing_is_stored`, e nenhum outro.
- [x] 4.5 Confirmar a árvore limpa depois das quatro inversões (`git status` e `git diff` vazios em `src/`).
      **Evidência:** os três SHA-256 reconferidos batem com a tabela acima, byte a byte. O `git status --porcelain src/`
      lista **só** os três arquivos desta change (`M DeadLetterHandler.cs`, `M DeadLetterTriggerService.cs`,
      `?? DeadLetterMessage.cs`) e nada mais — nenhum resíduo de inversão. Suíte de volta ao verde:
      `Com falha: 0, Aprovado: 33`.

## 5. Ponta a ponta contra o emulador (opcional, pulado por padrão)

Esta é a única tarefa que prova o **efeito** ("não volta a ser entregue sem fim") e as quatro linhas da casca. Se não
couber, **não marque como feita**: deixe aberta, com a anotação do que faltou, e registre o limite no `proposal.md`
e no `STATUS.md` (tarefa 6.4).

- [x] 5.1 Subir o emulador (`docker compose up -d servicebus sql`) e confirmar que as filas do
      `docker/servicebus/Config.json` existem, com `MaxDeliveryCount` conhecido.
      **Evidência:** `docker ps` mostra `fiscalhub-servicebus-1` e `fiscalhub-sql-1` no ar (não foi preciso subir — já
      estavam). `MaxDeliveryCount` **5** declarado no `Config.json` para as duas filas, e **medido**: a mensagem da
      sonda saiu da `documents-in` depois de **5** entregas, com
      `DeadLetterErrorDescription = "Message could not be consumed after 5 delivery attempts."`.
- [x] 5.2 Criar `tests/Adapters/Messaging/…ServiceBus.Tests/DeadLetterEmulatorTests.cs` com atributo próprio que põe
      `Skip` quando a variável de ambiente não está definida — copiar a forma do
      `tests/FiscalHub.Infrastructure.Tests/KeyVaultEmulatorIntegrationTests.cs` (a mensagem do `Skip` diz **o que
      subir** e **qual variável definir**).
      **Evidência:** `DeadLetterEmulatorFactAttribute` põe `Skip` sem `FISCALHUB_SB_EMULATOR`, com a mensagem
      "suba o 'servicebus' do docker compose (docker compose up -d servicebus sql) e defina FISCALHUB_SB_EMULATOR=1".
- [x] 5.3 O teste: publica um corpo lixo na fila, deixa a entrega esgotar até a dead-letter, sobe a casca, e afirma que
      a mensagem é consumida **uma vez** e **some** da dead-letter (contagem da fila morta volta a zero).
      **Evidência:** `A_poison_message_is_consumed_once_and_leaves_the_dead_letter`. Ele publica um corpo lixo, esgota a
      entrega por abandono (o caminho de produção — ninguém nosso chama `DeadLetterMessageAsync`), confirma a mensagem
      **na** dead-letter, sobe o `DeadLetterTriggerService` de verdade, e afirma: ela some da dead-letter, há **um** só
      registro de descarte com o `MessageId` dela, a casca **não** registrou erro nenhum, e nada foi ao store.
- [x] 5.4 Rodar com a variável definida e anotar a saída. Rodar **sem** ela e confirmar que o teste aparece como
      ignorado, e não falho.
      **Evidência:** com `-e FISCALHUB_SB_EMULATOR=1` → `Com falha: 0, Aprovado: 1, Total: 1, Duração: 8 s`.
      Sem ela → `Ignorado …DeadLetterEmulatorTests.A_poison_message_is_consumed_once_and_leaves_the_dead_letter` e
      `Com falha: 0, Aprovado: 33, Ignorado: 1, Total: 34`.
- [x] 5.5 Medir e anotar as duas perguntas em aberto do design (D7): o `DeliveryCount` de uma mensagem já na
      dead-letter incrementa a cada reentrega? a reentrega lá respeita o tempo de lock? **As respostas vão para a
      Open Question 2 do `design.md`**, e não viram requisito nesta change.
      **Evidência:** medido por sonda descartável (criada, rodada e **apagada** — `git status` de `tests/` só lista os
      três arquivos da fatia). **(a) NÃO:** o `DeliveryCount` entra na dead-letter em **6** e fica em 6, por abandono
      explícito (duas vezes) e por expiração de lock. **(b) SIM:** a reentrega esperou **60,0 s**, exatamente o
      `LockDuration` `PT1M`. As duas foram para o `design.md` (D7, seção "Medido na implementação", e Open Question 2),
      **sem** virar requisito nem asserção. Consequência registrada: a saída barata da Open Question 2 — ler o
      contador nativo — **não existe**.
- [x] 5.6 Confirmar que `dotnet test` da solução, **sem** container de pé, continua verde (com +1 ignorado).
      **Evidência:** **1429 aprovados / 0 falhas / 43 ignorados / 1472 no total** — exatamente **+1 ignorado** sobre o
      3.6. **Desvio honesto:** o container **não** foi derrubado para esta conferência; ele está no ar há 10 dias e é
      o ambiente de dev do Marcelo. A conferência vale assim mesmo porque o `Skip` olha **só** a variável de ambiente,
      e não a disponibilidade do container — é a mesma condição que vale no CI, onde nem variável nem container existem.

## 6. Spec, comentários e STATUS

- [x] 6.1 Conferir o delta `openspec/changes/dead-letter-poison-message/specs/dead-letter-visibility/spec.md` contra o
      que foi implementado de fato. Divergência entre o que a spec diz e o que o código faz **é achado**, e vai para o
      Marcelo — não se conserta a spec em silêncio para ela casar com o código.
      **Resultado: nenhuma frase da spec ficou falsa, e a spec não foi tocada.** A conferência, requisito a requisito:

      | Frase da spec | Onde está no código | Teste |
      |---|---|---|
      | inválida = não é JSON / é `null` / falta campo obrigatório | `catch (JsonException)` + `reference is null` | 2.3, 2.4, 2.5 |
      | inválida = sem tenant ou sem chave natural, em branco contando só-espaços | `IsNullOrWhiteSpace` nos dois | 2.6 |
      | descartada: concluída, e registrada no log de erro **antes** disso | `Discard(message)` e depois `return Discarded`; a casca conclui | 2.3, 2.4 + emulador |
      | nada no store | o `RecordDeadLetterAsync` fica depois da guarda | 2.3 a 2.6 |
      | o registro traz corpo, fila, identificador, sequência, enfileiramento e motivo | um `LogError` com os seis | 2.7 |
      | corpo limitado, e o tamanho original dito quando cortado | `BodyLogLimit` 4096 | 2.8 |
      | **um** registro por mensagem, e não um por entrega | — | `Assert.Single` em 2.3/2.4 + emulador |
      | falha no tratamento **não** conclui | a exceção propaga; o `CompleteMessageAsync` é pulado | 2.9 |
      | motivo: `Reason` → `ErrorDescription` → padrão, ausente **ou em branco** | `ResolveReason` | 2.10 |

      **Dois achados, nenhum deles divergência — os dois vão para o Marcelo:**

      1. **Leitura feita sobre "o motivo que o transporte deu" (registro do descarte).** O log carrega o motivo **com
         o piso aplicado** (`ResolvedReason`), e não o campo cru. Quando o transporte não deu motivo nenhum, o
         registro diz "Mensagem movida para dead-letter." em vez de deixar o campo vazio. É a mesma regra do D5,
         aplicada também ao log, e lê a frase da spec como compatível. **Se a intenção era o campo cru**, é uma
         linha — mas aí o registro de descarte pode sair com motivo vazio.
         **Decidido pelo Marcelo em 2026-10-16: era defeito, e o conserto é o grupo 7.** O log passa a carregar os
         dois campos crus, e ausente aparece como `(não informado)` — nunca o texto padrão. O piso é por leitor, e
         não global; a regra ficou escrita no D8 do `design.md`.
      2. **O teto do corpo é medido em caracteres, e não em bytes.** A spec diz "tamanho", sem unidade; o design
         sugeriu "4 KB" e chamou o número de chute informado. Ficou **4096 caracteres**, e o registro diz a unidade
         ("N caracteres"). Para o corpo anormal, que é o caso, a diferença não muda o efeito.
- [x] 6.2 `openspec validate --all --strict` — verde.
      **Evidência:** `Totals: 30 passed, 0 failed (30 items)`, incluindo `✓ change/dead-letter-poison-message` e
      `✓ spec/dead-letter-visibility`. Reconferido depois das mexidas no `STATUS.md`.
- [x] 6.3 `docs/STATUS.md`: tirar o **D2** de "Defeitos conhecidos" e a metade do **D5** que fala do `DeadLetterReason`
      vazio, **com a evidência** (a saída da inversão do 4.1 e do 4.3). A metade do D5 que é do `StatusPollerOptions`
      **fica**: é do D1.
      **Evidência:** o D2 está riscado e marcado "Fechado em 2026-10-16", com os nomes dos 12 testes da inversão 4.1,
      o teste único da 4.2 e o do emulador. A metade do D5 do `DeadLetterReason` está riscada, com os 4 casos em
      branco e os 4 em nulo da inversão 4.3; a metade do `StatusPollerOptions` ficou intacta. O registro também diz
      que **o diagnóstico original do D2 estava meio errado** — o sintoma era real, a causa não era a ordem do
      `CompleteMessageAsync`.
      **Desvio anotado:** duas outras frases do mesmo arquivo ficaram falsas e foram corrigidas junto —
      "Corrigir os defeitos conhecidos D1, D2 e D3" nos Próximos passos, e a pendência da
      `FSFiscalDocumentTaxTransBRs`, que apontava "é caso vivo para a change do defeito da dead-letter" (lá ficou
      escrito que esta change **não** resolveu aquela parte).
- [x] 6.4 `docs/STATUS.md`: registrar o que **continua** descoberto, para não parecer resolvido — a casca
      (`OnMessageAsync`) se o grupo 5 não entrar, e a Open Question 2 (falha determinista também é laço). Se o grupo 5
      entrar, registrar o contrário, com o número.
      **Evidência:** a seção "O que o conserto do D2 **não** cobriu, e continua aberto", com quatro itens: (1) a casca
      ganhou **1** teste, opt-in — numa execução normal as quatro linhas dela seguem descobertas, e apagar o
      `CompleteMessageAsync` não quebra teste nenhum; (2) falha determinista de gravação também é laço, amortecido
      pelo lock **medido em 60,0 s**; (3) o `DeliveryCount` **congela em 6** na dead-letter, então a saída barata da
      Open Question 2 não existe; (4) a mensagem descartada vive só no log.
- [x] 6.5 Verificação final: `dotnet build` (0 avisos) e `dotnet test` na solução. Anotar os números e comparar com
      1.1 e 1.3. Conferir a lista de arquivos tocados contra a do `proposal.md` → "Arquivos que a implementação vai
      tocar"; **arquivo fora da lista é achado**, e vai para o Marcelo antes do commit — há outra sessão trabalhando
      em paralelo.
      **Evidência — build:** `0 Aviso(s) 0 Erro(s)`, 00:00:55 (contra 00:03:56 da 1.1, que era build frio).
      **Evidência — testes:** **1429 aprovados / 0 falhas / 43 ignorados / 1472 no total**, contra os
      **1408 / 0 / 42 / 1450** da 1.3. A diferença é inteira do projeto da mensageria (**12 → 33 aprovados, +1
      ignorado**); os outros dez projetos estão idênticos ao número da baseline.
      **Evidência — arquivos tocados:** `git status --porcelain` lista **exatamente** os 8 caminhos previstos, e
      nenhum a mais:
      produção `M DeadLetterHandler.cs`, `M DeadLetterTriggerService.cs`, `?? DeadLetterMessage.cs`;
      testes `M DeadLetterHandlerTests.cs`, `?? CapturingLogger.cs`, `?? DeadLetterEmulatorTests.cs`;
      docs `M docs/STATUS.md`; e a pasta da change em `openspec/changes/`.
      **Nada fora da lista** — nenhum `.csproj`, nenhuma migration, nenhum arquivo de `dashboard/`, nenhum arquivo da
      outra sessão. A sonda temporária do 5.5 foi apagada e não aparece.

## 7. O log do descarte carrega o motivo cru (achado 1 da 6.1, decidido)

O achado 1 da tarefa 6.1 voltou do Marcelo como **defeito**, e não como leitura compatível: o log do descarte passava
`ResolvedReason`, então com o transporte calado ele escrevia o texto padrão do piso **atribuindo-o ao transporte** —
afirmava que ele disse uma frase que não disse. A decisão é que o colapso nulo/vazio é **por leitor**, e não global: o
piso continua inteiro no caminho do store, e o log passa a carregar o cru.

- [x] 7.1 `DeadLetterHandler.Discard`: trocar o `{TransportReason}` pelos dois campos crus — `DeadLetterReason` e
      `DeadLetterErrorDescription` — e fazer ausente ou em branco aparecer de um jeito que **não** se confunda com
      texto vindo do transporte. Nunca o texto padrão do piso.
      **Evidência:** o template agora tem `DeadLetterReason: {DeadLetterReason}, DeadLetterErrorDescription:
      {DeadLetterErrorDescription}`, alimentados por `DeadLetterMessage.AsReported(message.Reason)` e
      `…(message.ErrorDescription)`. `AsReported` devolve o texto que chegou, ou a const `NotReported` =
      `(não informado)`.
- [x] 7.2 `ResolvedReason` e o piso **continuam como estão** no caminho do registro: nada muda no store.
      **Evidência:** `HandleAsync` continua chamando `_store.RecordDeadLetterAsync(reference, message.ResolvedReason, ct)`,
      e `ResolveReason` não foi tocado na lógica — só o comentário dele, que agora diz que o colapso é do registro do
      store e aponta para o `AsReported`. `A_blank_reason_never_reaches_the_store` continua verde.
- [x] 7.3 Teste que falha se alguém devolver o resolvido para o log. Dois casos, e a asserção frouxa ("tem alguma coisa
      no campo") **não** serve: com o transporte calado, o registro **não contém** o texto padrão; com motivo, ele
      aparece igual ao que chegou.
      **Evidência:** `A_transport_that_said_nothing_is_not_quoted_saying_the_default_reason` (Theory com 3 casos:
      nulo/nulo, vazio/vazio, só-espaços/tab) — `Assert.DoesNotContain(DeadLetterMessage.DefaultReason, …)` mais os dois
      campos em `(não informado)`; e `The_discard_record_carries_the_raw_transport_fields_and_not_the_resolved_reason`,
      que afirma os dois campos preenchidos iguais ao que chegou.
- [x] 7.4 Prova por inversão: devolver `ResolvedReason` ao log e mostrar os testes **nomeados** vermelhos. Restaurar e
      conferir por hash.
      **Evidência:** hash dos dois arquivos antes da inversão —
      `DeadLetterHandler.cs` `c3d6d95a31026071cd2a9391d9692e08d6db67c6f925fa878c86e5829c66c2dc`,
      `DeadLetterMessage.cs` `ba67c940a07333fac180f3e2b3c24fc6a2b9f1dbf0e6e73aaebf94729997e7eb`.
      **Inversão A** (só o primeiro campo volta ao resolvido): `Com falha: 3, Aprovado: 34, Total: 38` — os 3 casos de
      `A_transport_that_said_nothing_is_not_quoted_saying_the_default_reason`, todos em `Assert.DoesNotContain`.
      **Inversão B** (os dois campos pelo resolvido, que é o defeito original): `Com falha: 4, Aprovado: 33, Total: 38`
      — os 3 de cima mais `The_discard_record_carries_the_raw_transport_fields_and_not_the_resolved_reason`
      (`Assert.Contains` da descrição, que a cadeia do piso descarta). Restaurado: os **dois hashes batem byte a byte**.
- [x] 7.5 `design.md`: o raciocínio do colapso **por leitor** fica escrito, como regra do projeto mais afiada e não
      como exceção. E a spec, se alguma frase ficar falsa.
      **Evidência:** novo **D8 — "O colapso nulo/vazio é por leitor, e não global"**, com a tabela dos dois leitores,
      o porquê de `(não informado)` e os nomes dos dois testes que prendem a regra; o D3 passou a dizer "os dois campos
      crus" e a apontar para o D8. Na spec: o requisito do descarte ganhou o parágrafo dos campos **como chegaram** e o
      cenário "O transporte não informou motivo nenhum"; o requisito do registro ganhou a frase de que o piso vale
      **naquele** registro, e não onde o leitor distingue os dois casos.
- [x] 7.6 Guarda: build e teste do projeto da mensageria, depois a solução inteira, e `openspec validate --all --strict`.
      **Evidência — mensageria:** `0 Aviso(s) 0 Erro(s)` e `Com falha: 0, Aprovado: 37, Ignorado: 1, Total: 38`
      (33 → 37; os 4 novos são 1 `Fact` + 3 casos da `Theory`).
      **Evidência — solução:** `0 Aviso(s) 0 Erro(s)`, 00:01:15; **1433 aprovados / 0 falhas / 43 ignorados / 1476 no
      total**, contra os **1429 / 0 / 43 / 1472** da 6.5. A diferença é inteira do projeto da mensageria; os outros dez
      idênticos (Application 502, Host 24, Directory.Json 3, Discovery.Local 12, Ingress.BlobDrop 9, Inbound.Xml 24,
      Integration 31, Infrastructure 159+40, Ingress.D365Poll 244+2, Outbound.Avalara 388).
      **Evidência — openspec:** `Totals: 30 passed, 0 failed (30 items)`, com
      `✓ change/dead-letter-poison-message` e `✓ spec/dead-letter-visibility`.
      **Evidência — arquivos tocados:** `git status --porcelain` lista **os mesmos 8 caminhos** da 6.5, e nenhum a
      mais. Esta correção tocou 5 deles: `DeadLetterHandler.cs`, `DeadLetterMessage.cs`, `DeadLetterHandlerTests.cs`,
      `docs/STATUS.md` e a pasta da change (`design.md`, `spec.md`, este arquivo). Sem `npm test`: a change não toca
      `dashboard/`.
