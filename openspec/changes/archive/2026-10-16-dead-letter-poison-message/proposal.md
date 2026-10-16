## Why

O **D2** do `docs/STATUS.md` ("Defeitos conhecidos", prioridade alta): uma mensagem cujo corpo não desserializa fica
presa na dead-letter **para sempre**, sendo reentregue sem fim, com uma linha de log por tentativa.

O diagnóstico escrito no STATUS.md é que o `DeadLetterTriggerService` "só chama `CompleteMessageAsync` **depois** do
`HandleAsync`". **O sintoma está certo; a causa, não.** Concluir depois do tratamento é a regra correta, e é a mesma do
consumidor normal (`ServiceBusTriggerService.cs:58-59`): exceção = não concluiu = reentrega. É essa regra que salva uma
falha transitória — SQL fora do ar, Blob indisponível —, porque a próxima entrega é exatamente o que a conserta.

A causa real é **uma só linha**, no `DeadLetterHandler.cs:18-19`:

```csharp
DocumentReference reference = body.ToObjectFromJson<DocumentReference>(DocumentQueueSerialization.Options)
    ?? throw new InvalidOperationException("Mensagem de dead-letter sem referência de documento.");
```

Um corpo inválido **lança a mesma exceção** que uma falha de infraestrutura. A casca não tem como distinguir as duas, e
trata as duas do único jeito que conhece: não conclui. Para a falha transitória isso é certo. Para o corpo inválido é o
laço infinito — porque nenhuma reentrega vai tornar o corpo legível.

**É por isso que a correção não é "concluir sempre".** Concluir sempre faz o corpo inválido sumir em silêncio, e também
descarta a falha transitória que a reentrega salvaria: trocaria um defeito por dois. E é por isso que o coração desta
fatia não é mover uma chamada, e sim **ensinar o código a distinguir "a mensagem é inválida" de "o tratamento falhou"**.

Vai junto o **D5**, que o próprio STATUS.md manda entrar aqui: um `DeadLetterReason` **vazio** (não nulo) grava motivo
vazio, porque a cadeia de `??` do `DeadLetterTriggerService.cs:56-58` só testa nulo. A nota aparece no painel parada e
sem dizer por quê.

**E há um agravante de processo.** O `DeadLetterTriggerService` **não tem nenhum teste de comportamento** — só o
registro no DI é provado. É a mesma causa estrutural que o STATUS.md registra para o D3 ("os gateways Freshdesk e Local
não têm nenhum teste. Foi por isso que o D3 sobreviveu") e a mesma que a change `adapter-key-guard-proof` fechou
no Host: **a função tem teste, quem a chama não tem.** Ver "O que existe de teste em volta", abaixo.

## O que existe de teste em volta (conferido em 2026-10-16, lendo o código)

Esta seção não é enfeite: ela é o que decide o tamanho da fatia, e o pedido foi que ela estivesse na proposta, e não
descoberta na implementação.

| O que | Teste hoje | Quem |
|---|---|---|
| `DeadLetterHandler.HandleAsync` | **3 testes** | `tests/Adapters/Messaging/…ServiceBus.Tests/DeadLetterHandlerTests.cs` |
| `DeadLetterTriggerService` — **registro** (uma assinatura por fila) | **1 teste** | `…/DiscoveryQueueRegistrationTests.cs:52` (`Each_queue_gets_one_consumer_and_one_dead_letter_listener`) |
| `DeadLetterTriggerService` — **comportamento** (a cadeia do motivo, o `CompleteMessageAsync`) | **nenhum** | — |
| `SqlProcessingStore.RecordDeadLetterAsync` | **2 testes** | `tests/FiscalHub.Infrastructure.Tests/SqlProcessingStoreTests.cs:172,303` |
| Fluxo ponta a ponta pela dead-letter | **nenhum** | os `FiscalHub.Integration.Tests` só implementam `RecordDeadLetterAsync` em store falso |

**Achados que mudam o desenho:**

1. **O caminho do defeito está inteiramente descoberto.** As duas linhas que contêm o D2 e o D5 vivem no
   `DeadLetterTriggerService.OnMessageAsync`, e nenhum teste as executa. O terceiro teste que existe hoje,
   `Handle_throws_on_empty_body`, **prova o defeito** — ele afirma que o corpo vazio lança. Não é teste a corrigir por
   acidente: é o requisito publicado hoje, e ele muda nesta change (ver "Capabilities").
2. **A casca é intestável como está.** `OnMessageAsync` recebe um `ProcessMessageEventArgs`, que carrega um
   `ServiceBusReceiver` vivo; e o repositório não tem biblioteca de mock (`Moq`/`NSubstitute` não aparecem em nenhum
   `.csproj` de teste — o padrão é duplo escrito à mão). Testar a casca exigiria ou uma biblioteca nova ou o emulador.
   **A saída é não ter o que testar na casca:** toda a decisão sai dela e vai para onde já há teste (design, D1).
3. **Os gateways Freshdesk e Local continuam sem teste, e isso não é desta fatia.** Eles são o D3; nada no caminho
   desta change passa por `src/Adapters/Support/`. Registrado para que a conferência não confunda as duas.
4. **O emulador do Service Bus já está no `docker-compose.yml`**, e o repositório já tem o padrão de teste de
   integração opcional, pulado por variável de ambiente (`KeyVaultEmulatorIntegrationTests`,
   `D365ChangeFeedIntegrationTests`, `SqlServerHarnessFact`). É o único jeito de provar "não volta a ser entregue sem
   fim" de verdade. Entra como tarefa **opcional e pulada por padrão** (tasks, grupo 4): a suíte normal não pode
   depender de container.

## What Changes

### 1. Três desfechos onde hoje há dois

A decisão sai da casca e vira o retorno do `DeadLetterHandler`, que passa a receber os fatos da mensagem em vez de um
motivo já mastigado:

| Situação | Hoje | Depois |
|---|---|---|
| corpo desserializa | grava `DeadLettered` + conclui | **igual** — grava e conclui |
| corpo **não desserializa** (não é JSON, é `null`, falta campo obrigatório) | lança → **nunca conclui** → laço infinito | **descarta:** um `LogError` com o corpo e a identidade da mensagem, e **conclui** |
| referência desserializa mas vem com `TenantId` ou `NaturalKey` **em branco** | grava linha com tenant/chave vazios | **descarta**, pelo mesmo caminho |
| store/infra falha (SQL fora, Blob fora) | lança → não conclui → reentrega | **igual** — e é o certo |

**A chamada de `CompleteMessageAsync` não se move.** Ela continua depois do `HandleAsync`, e continua sendo pulada por
exceção. O que muda é que corpo inválido **deixa de ser exceção**. Isso é o oposto de "concluir sempre": a falha
transitória continua protegida pela reentrega, e só o que nunca vai dar certo é descartado.

### 2. O que fica registrado de uma mensagem descartada, e como alguém fica sabendo

Um `LogError`, com evento estável, carregando: **o corpo** (como texto, limitado em tamanho), a fila, o `MessageId`, o
`SequenceNumber`, o `EnqueuedTime` e o motivo que o Service Bus deu. Com isso a mensagem se reconstrói e se republica à
mão. **Nada vai para o store** — e isso é decisão, não omissão: sem `TenantId` e sem `NaturalKey` não existe documento
a que atribuir a linha, e inventar um atribuiria a um tenant uma nota que pode não ser dele. O raciocínio completo, e a
alternativa recusada (guardar o corpo no Blob), estão no `design.md`, D3 e D6.

**Por que isto não é "sumir em silêncio".** Silêncio é o que acontece hoje com quem olha o painel: a nota some e o log
repete a mesma exceção sem o corpo. Depois, o corpo inteiro fica escrito uma vez, com a identidade da mensagem, num
evento com nome próprio — e o laço acaba.

### 3. O motivo em branco deixa de ser motivo (D5)

A cadeia de `??` vira uma função pura que trata **ausente e em branco como a mesma coisa**:
`DeadLetterReason` → `DeadLetterErrorDescription` → o texto padrão "Mensagem movida para dead-letter.".

**Decisão sobre nulo vs. vazio, que o pedido manda justificar.** No **registro**, nenhum dos dois é aceitável. A
distinção nulo/vazio é real e o projeto a respeita onde ela tem leitor com duas perguntas — o override vazio das URLs,
o `?? []` que não pode virar árvore vazia, o segredo "não configurado" vs. "em branco". Aqui o leitor é um só e a
pergunta é uma só: **"por que essa nota parou?"**. Para quem lê o painel, "foi dito que não há motivo" e "nunca foi
dito" produzem a mesma tela vazia. Logo os dois colapsam no mesmo tratamento, e o texto padrão é o piso: o campo
`Reason` de um documento `DeadLettered` nunca fica vazio. Só-espaços conta como em branco. A distinção continua existindo
na origem (o Service Bus), onde ela de fato significa coisas diferentes — ela só não atravessa para o registro.

**Honestidade sobre o alcance:** nenhum código nosso chama `DeadLetterMessageAsync` (conferido), então hoje o motivo em
branco não é produzido por nós — o broker preenche `MaxDeliveryCountExceeded`. O furo é de **contrato**: o campo é
texto livre, o vazio é legal, e a cadeia de `??` não o pega. É guarda barata, não defeito observado em produção.

### 4. **BREAKING** (de spec, não de API): um requisito publicado passa a dizer o contrário

`dead-letter-visibility` publica hoje: *"a mensagem MUST NOT ser concluída"* para corpo irreconhecível. **Esse requisito
descreve o defeito** — ele saiu do backfill da change `document-implemented-capabilities`, que documentou o que o código
fazia. Ele é removido e substituído. Detalhe e justificativa em "Capabilities".

Nenhuma API pública, nenhum contrato de fila, nenhum schema de banco muda. `IProcessingStore` fica intacta.

## Capabilities

### New Capabilities

Nenhuma. A capacidade existe e tem dona.

### Modified Capabilities

- **`dead-letter-visibility`** — três mexidas, e a justificativa de cada uma:

| Bloco | Requisito | Por quê |
|---|---|---|
| `## REMOVED` | "Mensagem sem referência de documento é erro" | A frase *"a mensagem MUST NOT ser concluída"* é o defeito publicado. Não dá para emendar: o título e o corpo afirmam o contrário do que passa a valer. O repositório não usa `RENAMED` (conferido no arquivo); o par é REMOVED + ADDED, como em `platform-account-connectors` e `module-navigation`. |
| `## ADDED` | "Mensagem inválida é descartada, com registro no log" | O desfecho novo: o que é inválido, o que fica escrito, e que ela é concluída. |
| `## ADDED` | "Falha no tratamento não conclui a mensagem" | A outra metade da distinção. Hoje **não há requisito nenhum** sobre a falha transitória — e sem ele, "concluir sempre" continuaria passando no `openspec validate`. É o requisito que impede a correção errada. |
| `## MODIFIED` | "A mensagem esgotada vira registro visível" | É dele o parágrafo da cadeia do motivo (`spec.md:17-18`), que ganha "ou em branco" e o piso do texto padrão (D5). Carrega os **3 cenários publicados** mais um novo, "Motivo em branco". |

**Por que tudo mora em `dead-letter-visibility`, e não em outra spec.** Ela é a dona: o `Purpose` dela é exatamente
"assina a dead-letter de cada fila e transforma cada mensagem esgotada num registro visível". Os dois requisitos novos
falam do **desfecho de uma mensagem na dead-letter**, que é o assunto dela e de mais ninguém.

### Capabilities que NÃO mudam, e por quê

A régua é a das changes anteriores: **o delta vai onde uma frase publicada fica falsa, ou onde o requisito deixa de
fora o que o defeito atravessa.** Fora disso não há delta, e isso é conclusão, não esquecimento.

- **`discovery-queue-consumer` — nenhum delta.** Ela publica a dead-letter da fila de descoberta (`spec.md:97-107`),
  mas só o caso do **corpo válido**: registro com estado de dead-letter e o grupo vindo da referência. Nada ali fica
  falso, e o que ela deixa de fora é o caso inválido, que é da dona. Uma verdade, uma dona.
- **`document-grouping` — nenhum delta.** O card "Com erro" soma as da dead-letter (`spec.md:249`). Uma mensagem
  descartada **não vira linha nenhuma**, então nenhum card muda de conta. Além disso o dashboard é de outra sessão
  agora (ver Non-goals).
- **`compliance-dispatch-outcome`, `integration-status-poll`, `support-ticket` — nenhum delta.** O vocabulário de
  status não muda: nenhum `IntegrationStatus` nasce, some ou troca de significado.
- **`platform-response-trace` — nenhum delta.** As fotos do Blob continuam sendo por `(tenant, chave)`, e o corpo
  descartado não tem nem um nem outro. A alternativa de guardá-lo lá foi avaliada e recusada (design, D6).
- **Nenhuma capability nova.** Uma "quarentena de mensagens" só existiria se o corpo descartado virasse artefato
  durável com tela — que é a Open Question 1, e outra fatia.

### Sobre ADR

**Não há ADR nesta change.** A regra do repositório é ADR para decisão de arquitetura, e nenhuma fronteira se move:
nenhuma porta nasce, nenhum adapter troca de lado, `IProcessingStore` não ganha método, Domain e Application não são
tocados. O ADR-0010 continua valendo inteiro — "não reprocessa, só torna visível" — e esta change o **cumpre melhor**,
porque hoje o corpo inválido não fica visível em lugar nenhum. A decisão com consequência a jusante — que a distinção
inválido/falhou vive no handler e não na casca — fica no `design.md` (D1 a D7). Se o Marcelo discordar, o ADR é barato
depois; está nas Open Questions.

## Non-goals

- **Concluir a mensagem sempre.** É o conserto que para o sintoma e cria dois defeitos. Recusado explicitamente.
- **Reprocessar da dead-letter.** O ADR-0010 já recusou, e continua recusado. Nada volta para fila nenhuma.
- **Esquema novo de idempotência, retry ou DLQ** (CLAUDE.md §9). Nenhuma fila nova, nenhum contador próprio, nenhuma
  tabela de quarentena, nenhuma política de retry nossa. O retry continua sendo o nativo do transporte (ADR-0004).
- **Limitar quantas vezes uma falha de tratamento é reentregue.** É a terceira categoria — "falhou tanto que já não é
  transitório" — e é decisão de produto própria. Fica na Open Question 2, com o risco escrito.
- **Mexer no `ServiceBusTriggerService`** (o consumidor da esteira). O corpo inválido lá tem outro desfecho correto:
  falha → recontagem → dead-letter. É o caminho que alimenta esta capability; mudá-lo seria mudar o problema.
- **D1 e D3.** Cada um é change própria, como o STATUS.md determina. O `StatusPoller`, o `StatusPollerOptions` e o
  `FreshdeskSupportGateway` não são tocados.
- **Qualquer arquivo de `dashboard/`.** E nada de despacho de saída, perfil de conector ou vocabulário de status —
  é o escopo da sessão que roda em paralelo no worktree principal.
- **Cobrir os gateways Freshdesk e Local.** O buraco existe e está registrado; é do D3.
- **Cobertura como meta numérica.** Nenhum limiar, nenhuma porta de CI.

## Impact

### Arquivos que a implementação vai tocar

Esta lista é para ser conferida contra a da outra sessão **antes de liberar a implementação**.

**Produção (3 arquivos, todos no mesmo adapter):**

| Arquivo | O quê |
|---|---|
| `src/Adapters/Messaging/FiscalHub.Adapters.Messaging.ServiceBus/DeadLetterHandler.cs` | passa a receber os fatos da mensagem, a devolver o desfecho, a logar o descarte e a nunca lançar por corpo inválido |
| `src/Adapters/Messaging/FiscalHub.Adapters.Messaging.ServiceBus/DeadLetterTriggerService.cs` | a cadeia de `??` sai (vai para a função pura); `OnMessageAsync` monta os fatos e chama o handler. `CompleteMessageAsync` **fica onde está** |
| `src/Adapters/Messaging/FiscalHub.Adapters.Messaging.ServiceBus/DeadLetterMessage.cs` **(novo)** | o registro dos fatos da mensagem (corpo, motivo, descrição, identidade, fila), o enum do desfecho e a função pura do motivo. Arquivo novo, `internal`, nada público |

**Testes (3 arquivos):**

| Arquivo | O quê |
|---|---|
| `tests/Adapters/Messaging/…ServiceBus.Tests/DeadLetterHandlerTests.cs` | os 3 testes de hoje seguem a nova assinatura; `Handle_throws_on_empty_body` **inverte** (passa a afirmar descarte + log + conclusão); entram os casos novos |
| `tests/Adapters/Messaging/…ServiceBus.Tests/CapturingLogger.cs` **(novo)** | duplo de `ILogger<T>` que guarda as entradas, copiado do padrão já existente em `…Avalara.Tests/CapturingLogger.cs`. Projeto de teste não referencia projeto de teste |
| `tests/Adapters/Messaging/…ServiceBus.Tests/DeadLetterEmulatorTests.cs` **(novo, opcional)** | o ponta a ponta contra o emulador do Service Bus, **pulado por padrão** por variável de ambiente, no padrão do `KeyVaultEmulatorIntegrationTests`. Se a tarefa não couber, o arquivo não nasce (tasks, grupo 4) |

**Spec (1 arquivo):** `openspec/changes/dead-letter-poison-message/specs/dead-letter-visibility/spec.md` — 1 removido,
2 adicionados, 1 modificado.

**Docs (1 arquivo):** `docs/STATUS.md` — o **D2** sai de "Defeitos conhecidos" e a metade do **D5** que fala do
`DeadLetterReason` vazio também, com evidência. A metade do D5 que é do `StatusPollerOptions` **fica**: é do D1.

**O que NÃO é tocado, e vale dizer por ser vizinho:** `ServiceBusTriggerService.cs`, `QueuedDocumentProcessor.cs`,
`ServiceBusMessagingServiceCollectionExtensions.cs`, `DocumentQueueSerialization.cs`, `IProcessingStore.cs`,
`SqlProcessingStore.cs`, `Program.cs`, qualquer `.csproj`, qualquer migration, qualquer arquivo de `dashboard/`.

### Resto

- **Banco:** nada. Nenhuma coluna, nenhuma migration. Uma mensagem descartada não grava linha; uma registrada grava a
  mesma linha de hoje, com o motivo garantidamente não vazio.
- **Contrato de fila:** nada. O corpo continua sendo a `DocumentReference` em JSON.
- **Dependências:** nenhum pacote novo. `Microsoft.Extensions.Logging` já é referenciado pelo adapter, e o duplo de
  logger é escrito à mão, como o resto do repositório.
- **Operação:** a dead-letter para de acumular mensagem imortal. Quem acompanha log passa a ver **um** `LogError` por
  mensagem inválida, com o corpo, em vez de N por minuto sem ele.
- **Suíte:** `FiscalHub.Adapters.Messaging.ServiceBus.Tests` cresce de 7 para ~13 testes (+1 pulado, se o do emulador
  entrar). Nenhum outro projeto de teste muda.
