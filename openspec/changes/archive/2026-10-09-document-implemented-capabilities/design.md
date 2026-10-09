## Context

A motivação está no `proposal.md`, e os requisitos, nas três specs desta change. Aqui ficam o método da conferência, as
decisões de escrita e, principalmente, os achados: o que o rascunho afirma e o código não faz.

**Como a conferência foi feita (2026-10-09).** Cada requisito do `docs/specs-retroativas-rascunho.md` foi lido contra
o código, os testes e as ADRs que o rascunho cita. O rascunho lista os arquivos principais. A conferência precisou de
mais estes:

- **Poll:**
  - o `StatusPollingService.cs`, que fica no **Host**, e não num adapter;
  - o `Program.cs:168-171`;
  - o `ListPendingAsync` e o `MarkPolledAsync` do `SqlProcessingStore.cs`;
  - o `CheckStatusAsync` do `AvalaraComplianceDispatcher.cs`, onde nascem as exceções da consulta.
- **Dead-letter:**
  - o `ServiceBusMessagingServiceCollectionExtensions.cs`, que registra uma assinatura por fila;
  - o `DocumentReference.cs`, cujos campos `required` decidem o que desserializa.
- **Os estados no dashboard:** o `SqlDocumentQueries.cs:16` e o `StatusChip.tsx:9-17`.
- **Chamado:**
  - o `FreshdeskSupportGateway.cs` e o `LocalSupportGateway.cs`;
  - o `BlobNoteTraceReader.cs`;
  - o `ConnectorProfileService.cs`, que guarda o segredo dos chamados como referência;
  - o `Program.cs:792-828`.
- **Testes:** `StatusPollerTests`, `SqlProcessingStoreTests`, `DeadLetterHandlerTests`, `DiscoveryQueueRegistrationTests`
  e `SupportTicketServiceTests`.

**Specs que encostam nestas.** São três, e nenhuma muda (D4):

- **`discovery-queue-consumer`:** o requisito "Dead-letter da fila de descoberta visível";
- **`document-grouping`:** o card "Com erro" conta rejeitadas, sem retorno e dead-letter juntas;
- **`connector-secret-references`:** vale também para o segredo dos chamados. É o que o achado A7 contradiz.

## Goals / Non-Goals

**Goals:**

- Cada frase das três specs é verificável no código de hoje.
- Cada diferença entre o rascunho e a spec tem um achado com o texto antigo, o comportamento real e os arquivos.
- O Marcelo decide cada achado antes do apply, e o `tasks.md` aplica a decisão, qualquer que seja.

**Non-Goals:**

- Decidir os achados aqui. O design só os registra e diz o que cada caminho implica.

## Decisions

### D1. A spec descreve o comportamento verificado, e não o rascunho

O rascunho é o ponto de partida, e não a fonte. Quando ele e o código divergem, a spec diz o que o código faz, ou cala,
pela D2. A divergência vira achado.

- **Alternativa: copiar o rascunho e corrigir depois.** Recusada. O archive levaria uma spec falsa para
  `openspec/specs/`, e a próxima change confiaria nela.

### D2. Frase que descreveria um defeito sai, e não vira requisito

Quando o código faz algo que parece defeito, a spec não o transforma em contrato, e também não repete a promessa do
rascunho. A frase sai, e o achado guarda as duas versões. O caso é o A1.

- **Alternativa: escrever o comportamento atual como requisito.** Recusada. Um defeito virando contrato faz a correção
  parecer quebra de spec.
- **Alternativa: manter a frase do rascunho.** Recusada. A spec afirmaria algo que o código não faz.

### D3. "Item em aberto" vira o comportamento que dá para testar

O rascunho usa "item em aberto, e não rejeição" para `Unconfirmed` e `DeadLettered`. A expressão não tem
comportamento próprio no código. As specs dizem o que existe:

- os dois são estados distintos de `IntegrationError`, com motivo próprio;
- nenhum dos dois bloqueia a reentrada da nota, igual ao `IntegrationError`.

O achado A4 explica por quê.

- **Alternativa: manter a expressão.** Recusada. Não dá cenário testável, e contradiz a ADR-0007.

### D4. As specs vizinhas ficam como estão

O "Dead-letter da fila de descoberta visível" da `discovery-queue-consumer` repete parte da `dead-letter-visibility`
para uma fila. Ele continua lá, porque também trata do grupo trazido pela referência, que é próprio da descoberta.

- **Alternativa: um delta MODIFIED na `discovery-queue-consumer`, apontando para a spec nova.** Recusada. Mexer num
  contrato existente não cabe numa change de documentação. A repetição é pequena e consistente.

### D5. Acréscimos ao rascunho

A spec acrescenta comportamento que o rascunho não tinha só quando um achado precisa dele para ser lido, ou quando um
teste já o prova. São estes:

- **Poll:**
  - "em voo" é `Submitted` com id externo;
  - o lote vem na ordem de entrada e atravessa os tenants (A1 depende disso);
  - o reenvio zera as tentativas;
  - a passada devolve o tamanho do lote, inclusive o documento que falhou.
- **Dead-letter:**
  - o registro é criado quando não existia;
  - o `DeadLettered` sai do lote do poll;
  - o texto do motivo padrão.
- **Chamado:**
  - o tenant é o do usuário logado;
  - a recusa responde HTTP 400;
  - a comparação do nome do adapter ignora maiúscula;
  - o exemplo do nome do zip;
  - o anexo extra vazio;
  - a nota sem número;
  - a estimativa devolve também o teto.

### D6. O `tasks.md` sem teste antes

Não há mudança de comportamento, e por isso não há teste a escrever primeiro. Cada grupo relê o código contra a spec,
aplica a decisão do Marcelo sobre os achados da capability e valida. A validação aponta, para cada cenário, o teste
que o prova, ou anota "sem teste" na seção "Cobertura" abaixo.

## Achados

| # | Capability | Severidade | Resumo | Decisão do Marcelo (2026-10-09) |
|---|---|---|---|---|
| A1 | poll | **alta** | Falha persistente na consulta nunca chega a `Unconfirmed`, e 50 delas travam o poll de todos os tenants | Change de correção própria, com o teste antes. A primeira da fila |
| A7 | chamado | **alta** | O Freshdesk não lê o segredo do cofre: configurado pela API, sempre falha | Change de correção própria, com o teste antes |
| A4 | poll e dead-letter | média | "Item em aberto, e não rejeição" não é o que as ADRs e o código dizem | Leitura confirmada. A soma no card "Com erro" fica como questão de produto, para depois |
| A5 | dead-letter | média → **alta** | Corpo irreconhecível: outra mensagem de erro, e a mensagem volta a ser entregue sem fim | Promovido a change de correção própria, com o teste antes |
| A2 | poll | baixa | "Configurável" só editando o `Program.cs` | Vai junto na change do A1 |
| A3 | poll | baixa | A casca fica no Host, e não no adapter | Correção confirmada |
| A6 | dead-letter | baixa | Motivo vazio passa como vazio, e o registro fica sem motivo | Vai junto na change do A5 |
| A8 | chamado | baixa | O teto de 20 MB é fixo para qualquer provider, e repetido no `Program.cs` | Aceito, como dívida cosmética |
| A9 | chamado | baixa | A descrição sempre diz que os logs seguem anexados, mesmo sem nenhum anexo | Aceito, como dívida cosmética |

As seções abaixo guardam o "A decidir" como estava no propose. A decisão vale pela tabela e pelo `tasks.md`.

### A1. Falha persistente na consulta nunca chega a Unconfirmed

- **O rascunho diz:** "O documento fica para a passada seguinte; se a falha persistir, o limite de tentativas o move
  para `Unconfirmed` como qualquer outro."
- **O código faz:** o `catch` do `StatusPoller.cs:38-42` engole a exceção antes do `MarkPolledAsync`. A tentativa não
  é contada, e o documento volta igual na passada seguinte. O teste afirma isso: `StatusPollerTests.cs:86`, "o que
  estourou não foi marcado". Uma falha que persiste deixa o documento `Submitted` para sempre, no card "Em
  processamento".
- **Quando a consulta lança:**
  - 401, no `AvalaraComplianceDispatcher.cs:161-164`, por exemplo com a credencial do tenant trocada;
  - 5xx, no `EnsureSuccessStatusCode` da linha 174;
  - perfil do tenant ilegível, na linha 145.
- **O agravante:** o `ListPendingAsync` traz os 50 mais antigos de todos os tenants (`SqlProcessingStore.cs:97-109`).
  O documento que falha nunca sai do topo. Com 50 ou mais, o lote inteiro é deles a cada passada, e nenhum outro
  documento, de tenant nenhum, volta a ser consultado. Um tenant com 50 notas em voo e a credencial trocada para o
  poll de todos.
- **A promessa falsa aparece também no código:** no comentário do `StatusPoller.cs:40-41` e no do
  `AvalaraComplianceDispatcher.cs:160`, "limite em MaxAttempts". A ADR-0010 sabe da diferença. Nas alternativas, ela
  diz que, só com o try/catch, "o documento nunca avançaria de estado".
- **O que a spec ficou:** sem a frase (D2). O requisito "Uma falha não derruba o lote" diz só o que é verdade: os
  outros seguem, e o que falhou continua em voo.
- **A decidir:**
  1. **Change de correção própria,** test-first. Esta fica só documentação. É a recomendação: a correção muda
     comportamento, e o comentário do Avalara também.
  2. **Corrigir aqui.** A change deixa de ser só documentação.
  3. **Aceitar o comportamento atual e escrevê-lo na spec.** Não recomendado, pelo agravante.

### A2. "Configurável" só editando o código

- **O rascunho diz:** "O tamanho do lote MUST ser configurável; o padrão é 50" e "O limite MUST ser configurável; o
  padrão é 10 consultas".
- **O código faz:** os dois são propriedades do `StatusPollerOptions` (`StatusPoller.cs:84-91`). O host, porém,
  registra `new StatusPollerOptions()` (`Program.cs:169`), sem ler configuração nenhuma. Mudar exige editar o código e
  fazer deploy. O intervalo de 15 s também é fixo (`StatusPollingService.cs:16`).
- **O que a spec ficou:** "MUST ser, por padrão, 50" e "por padrão, de 10". A palavra "configurável" saiu.
- **A decidir:** manter assim, ou ligar os três valores ao `appsettings` numa change própria.

### A3. A casca fica no Host

- **O rascunho diz:** "A casca que chama a passada num intervalo é um `BackgroundService`, e fica no adapter."
- **O código faz:** a casca fica em `src/FiscalHub.Host/StatusPollingService.cs`.
- **O que a spec ficou:** "Quem a repete num intervalo é o host." O nome da classe saiu, porque a spec não nomeia
  classe.
- **A decidir:** só confirmar a correção.

### A4. "Item em aberto" não é o que o rascunho diz

- **O rascunho diz:**
  - "`Unconfirmed` MUST ser tratado como item em aberto, e nunca como rejeição de negócio";
  - "`DeadLettered` é item em aberto, distinto de `IntegrationError`, que é rejeição da plataforma".
- **Nas ADRs, "item em aberto" quer dizer:** um estado que **não bloqueia a reentrada**. E o `IntegrationError` está
  do mesmo lado:
  - ADR-0007: "`IntegrationError` e `Unconfirmed` são itens em aberto e liberam o reprocesso";
  - ADR-0010: "Como erro e unconfirmed, `DeadLettered` não bloqueia um reenvio".
- **O código concorda com as ADRs:** o `AlreadyProcessedAsync` só bloqueia `Submitted` e `Confirmed`
  (`SqlProcessingStore.cs:25-31`).
- **No dashboard, os três contam juntos:** são a faixa "Com erro" (`SqlDocumentQueries.cs:16`; spec `document-grouping`)
  e a lista `FAILURE_STATUSES` (`StatusChip.tsx:17`). Eles só se distinguem no chip ("Rejeitado", "Sem retorno",
  "Falha") e no motivo.
- **O que a spec ficou:** estados distintos, com motivo próprio, que não bloqueiam a reentrada (D3).
- **A decidir:** confirmar a leitura. Se a intenção era outra, por exemplo um card próprio para os itens em aberto,
  isso é change de comportamento, e muda a `document-grouping`.

### A5. Corpo irreconhecível na dead-letter

- **O rascunho diz:** "o consumo falha com erro dizendo que falta a referência".
- **O código faz:** a mensagem "sem referência de documento" só aparece com o corpo JSON `null`
  (`DeadLetterHandler.cs:18-19`). É o único caso testado (`DeadLetterHandlerTests.cs:43-49`). Corpo que não é JSON, ou
  sem um campo `required` do `DocumentReference.cs:10-22`, lança `JsonException`, com a mensagem do
  `System.Text.Json`.
- **O que acontece depois:** a mensagem não é concluída, e volta a ser entregue. A dead-letter não tem dead-letter
  própria, e então a reentrega não tem fim. Com `MaxConcurrentCalls = 1` (`DeadLetterTriggerService.cs:42`), cada volta
  ocupa o único consumidor. **Não verificado ao vivo.** Isto vem do comportamento documentado do SDK: abandono, ou
  expiração do lock.
- **O que a spec ficou:** dois cenários. Corpo `null` falha com a mensagem que o código dá. Corpo irreconhecível
  falha, não grava nada e não é concluído. O que acontece depois ficou fora.
- **A decidir:** aceitar, ou numa change própria mover a mensagem envenenada para um lugar morto de verdade, ou
  concluí-la com log.

### A6. Motivo vazio

- **O rascunho diz:** "O registro nunca fica sem motivo."
- **O código faz:** `DeadLetterReason ?? DeadLetterErrorDescription ?? "Mensagem movida para dead-letter."`
  (`DeadLetterTriggerService.cs:56-58`). O `??` só pula o nulo. Um `DeadLetterReason` vazio grava motivo vazio. É raro,
  porque o Service Bus preenche o motivo nos casos dele. O fallback inteiro fica na casca e não tem teste.
- **O que a spec ficou:** "Sem ele…", sem a frase absoluta.
- **A decidir:** aceitar, ou trocar o `??` por um teste de vazio numa change própria.

### A7. O Freshdesk não lê o segredo do cofre

O rascunho não trata disso, mas o defeito fica dentro da capability, e contradiz outra spec.

- **O caminho oficial:** o `apiKey` dos chamados entra pelo `PUT /connector` (`Program.cs:670`). A tela de
  Configurações → Conectores não tem os campos de chamados: o dashboard só traz o `supportAdapter` e o
  `supportSettings` no tipo (`types.ts:173-174`), e o `ConnectorsPage.tsx` não os edita. Pela API, a
  `connector-secret-references` guarda o valor no cofre e deixa no perfil só `apiKeyRef: kv:fh-<tenant>--support--apikey`
  (`ConnectorProfileService.cs:51-54`; `ConnectorProfileServiceTests.cs:428`, 559 e 570).

  *Corrigido no apply (2026-10-09).* O propose dizia que o Admin grava "pela tela", e não há tela para isso. A
  conclusão não muda: o caminho que existe para configurar os chamados é a API, e por ela o Freshdesk nunca recebe a
  chave.
- **O gateway:** o `FreshdeskSupportGateway` só lê o `ApiKey` (`FreshdeskSupportGateway.cs:26-27`). O `ApiKeyRef`
  existe no record com o comentário "resolvida upstream" (linha 104), mas ninguém o resolve. O `SupportTicketService`
  passa as settings cruas (`SupportTicketService.cs:119`), e o gateway não recebe o `ISecretStore`. O
  `AvalaraTokenProvider` e o `ClientCredentialsD365TokenProvider` recebem.
- **O resultado:** configurado pelo caminho oficial, o Freshdesk sempre falha com "Freshdesk: 'apiKey' ausente nas
  settings do conector." O único jeito de funcionar é o `apiKey` em claro no perfil, gravado por SQL. A
  `connector-secret-references` proíbe isso ("Nada em claro no perfil, no banco ou no disco").
- **Duas regras quebradas da `connector-secret-references`:**
  - a mensagem não segue o "Segredo ausente falha alto e aponta para a tela": não cita o tenant nem a tela;
  - o gateway não tem teste nenhum. O CLAUDE.md pede teste para todo adapter.
- **Por que ninguém viu:** o seed de dev usa o adapter `Local` (`InfrastructureServiceCollectionExtensions.cs:196`),
  e o Freshdesk nunca rodou de ponta a ponta. **Não verificado contra uma conta Freshdesk.** A conclusão vem da
  leitura do código, e é determinística.
- **O que a spec ficou:** nada muda. A `support-ticket` é agnóstica de provider, e o segredo é assunto da
  `connector-secret-references`.
- **A decidir:** uma change de correção, com o gateway recebendo o `ISecretStore`, a mensagem e os testes do adapter.
  E corrigir o STATUS (S1). A correção também precisa decidir para onde a mensagem aponta. A
  `connector-secret-references` pede "a tela de conectores", e ela não tem os campos de chamados.

### A8. O teto de 20 MB é fixo

- **O rascunho diz:** "um teto único de 20 MB, que é o limite do provider".
- **O código faz:** a constante fica no serviço, que é agnóstico de provider (`SupportTicketService.cs:16`). Ela vale
  para qualquer provider, o `Local` incluído. É o limite do Freshdesk, em MiB: 20 × 1024 × 1024. O valor também está
  repetido como literal no `Program.cs:827`, na resposta da estimativa.
- **O que a spec ficou:** "20 MB (20 × 1024 × 1024 bytes), qualquer que seja o provider".
- **A decidir:** aceitar. As duas cópias podem divergir. Juntá-las é refactor de uma linha, numa change própria.

### A9. A frase final da descrição

- **O rascunho diz, e o código faz:** a descrição sempre termina com "Os logs de cada nota (origem/domínio/destino)
  seguem anexados, zipados por nota." (`SupportTicketService.cs:169`). Isso vale mesmo quando nenhuma nota tem log, e
  o chamado vai sem anexo.
- **O que a spec ficou:** como o rascunho, porque é o que o código faz.
- **A decidir:** aceitar, ou tornar a frase condicional numa change própria. Neste caso, este requisito muda.

### STATUS.md

O `docs/STATUS.md` do Marcelo foi conferido contra o código.

**Bate com o código:**

- a thread da DLQ, removida: ela está resolvida, com o `RecordDeadLetterAsync` e o `DeadLettered`;
- a linha da mensageria;
- 35 ADRs, 24 specs e 13 changes arquivadas.

**Diverge:**

- **S1 (por causa do A7).** "Dois gateways: Freshdesk (real) e Local (mock de dev)." O Freshdesk faz chamada real, mas
  não consegue a chave pelo caminho oficial.
- **S2 (por causa do A4).** Duas frases das "Decisões recentes":
  - "itens em aberto, distintos de `IntegrationError`, que é recusa de negócio": pela ADR-0007, o `IntegrationError`
    também é item em aberto;
  - "Eixos separados no dashboard": os três somam no mesmo card, "Com erro", e se separam só no chip.
- **S3 (por causa do A1).** "Ciclo assíncrono fechado… sem resposta da plataforma o documento vira `Unconfirmed`." Vale
  para o "sem resposta": 204, 404 ou status desconhecido. Não vale para a falha na consulta. "Fechado" é forte demais
  enquanto o A1 existir.
- **S4 (menor).** "22 projetos." A `FiscalHub.slnx` tem 24: 12 em `src/`, 10 em `tests/` e 2 em `tools/`. São 22 só sem
  o `tools/`.

**No archive:** "24 specs" vira 27, e "13 changes arquivadas" vira 14. O "Próximos passos" 1 aponta para o rascunho, que
será apagado. O item sai, ou passa a apontar para as specs.

**No apply (2026-10-09).** O Marcelo aplicou o S1, o S2, o S3 e o S4 no STATUS, e acrescentou a seção "Defeitos
conhecidos", com o D1 ao D4. A conferência dela contra o código achou duas divergências, que aguardam aval:

- **S5, no D3.**
  - "O dashboard não tem tela de chamados" não confere. O modal de abrir chamado existe (`TicketModal.tsx`), e abre
    pelo detalhe da nota (`NoteDialog.tsx:30`) e pelo modal do grupo (`GroupModal.tsx:61`). O que falta são os campos
    de chamados em Configurações → Conectores.
  - "O seed grava `apiKeyRef`" é verdade, mas o seed usa o adapter `Local`, e por isso não mostra o defeito. O caminho
    que leva ao defeito é o `PUT /connector`, que troca o `apiKey` pela referência.
  - **Sugestão:** "O `PUT /connector` guarda o `apiKey` no cofre e deixa só o `apiKeyRef`, que nada resolve… e a tela de
    Conectores não tem os campos de chamados, então não há caminho de UI para configurar nem para contornar."
- **S6, no D4.** "Menores, aceitos por ora" junta quatro itens com destinos diferentes. Pela decisão, o
  `StatusPollerOptions` (A2) vai na correção do D1, e o motivo vazio (A6) vai na correção do D2. Só o teto repetido
  (A8) e a frase da descrição (A9) foram aceitos, como dívida cosmética.

**Aplicados pelo Marcelo (2026-10-09), com o texto dele.**

- **S5:** a última frase do D3 virou o agravante. O chamado é exposto ao usuário em dois lugares da tela, e o provider
  só entra pela API. Assim, quem configura o Freshdesk pelo caminho oficial vê o botão funcionar e o chamado falhar,
  sem tela onde corrigir.
- **A gravidade:** a correção aumenta a gravidade do A7. O texto antigo tratava o recurso como inacessível, e na
  verdade ele está exposto.
- **S6:** o D4 ficou só com o A8 e o A9. O A2 e o A6 foram para um D5 novo, "menores, que entram nas correções acima".

## Cobertura

O cenário que nenhum teste prova fica anotado, e não ganha teste nesta change (proposal, Non-goals).

**`integration-status-poll`**

| Cenário ou regra | Teste |
|---|---|
| Uma passada com documentos em voo | `StatusPollerTests.cs:17`, `:77` |
| Nada em voo | `StatusPollerTests.cs:67` |
| Documento fora de voo | `SqlProcessingStoreTests.cs:69`, `:82`. O "sem id externo" não tem teste |
| A ordem de entrada e o atravessar os tenants | sem teste |
| Os padrões 50 e 10 | sem teste: nenhum teste afirma os valores |
| A plataforma confirma | `StatusPollerTests.cs:17` |
| A plataforma rejeita | `StatusPollerTests.cs:30` |
| Ainda processando, dentro do limite | `StatusPollerTests.cs:42` |
| O limite estoura | `StatusPollerTests.cs:54` afirma só que há motivo. O texto, só no teste do store, `SqlProcessingStoreTests.cs:503` |
| O reenvio zera a contagem | `SqlProcessingStoreTests.cs:119` |
| Nota sem retorno entra de novo | sem teste. Só a leitura do `SqlProcessingStore.cs:25-31` |
| Um documento falha na consulta | `StatusPollerTests.cs:77` |
| Cancelamento | sem teste |

**`dead-letter-visibility`**

| Cenário ou regra | Teste |
|---|---|
| Mensagem esgotada com motivo | `DeadLetterHandlerTests.cs:16` grava o motivo recebido. A leitura do `DeadLetterReason` fica na casca, sem teste |
| Mensagem sem motivo | sem teste (casca) |
| Documento que nunca foi registrado | `SqlProcessingStoreTests.cs:299` |
| Nada volta para a fila | sem teste. Vale por construção: o handler só chama o store |
| Os dois desfechos ficam distintos | `SqlProcessingStoreTests.cs:95` e `:167`, em separado |
| Nota da dead-letter entra de novo | `SqlProcessingStoreTests.cs:167` |
| As duas filas cobertas | `DiscoveryQueueRegistrationTests.cs:52` |
| Corpo vazio | `DeadLetterHandlerTests.cs:43` |
| Corpo irreconhecível | sem teste |

**`support-ticket`**

| Cenário ou regra | Teste |
|---|---|
| Seleção vazia | `SupportTicketServiceTests.cs:48` afirma só o tipo da exceção, e não o texto |
| Título ou descrição em branco | `SupportTicketServiceTests.cs:56` afirma só o tipo |
| Chave de outro tenant | sem teste. O fake ignora o tenant, e o `ListByKeysAsync` real não tem teste |
| O tenant do usuário logado, e o HTTP 400 | sem teste: o endpoint não tem teste |
| Tenant sem chamados configurados | `SupportTicketServiceTests.cs:69` |
| Adapter que não existe | `SupportTicketServiceTests.cs:79` afirma só o tipo, e não o nome |
| Notas com rastreabilidade | `SupportTicketServiceTests.cs:16`, com duas notas |
| O nome do zip | sem teste. Só o `EndsWith(".zip")` |
| Nota sem arquivos | `SupportTicketServiceTests.cs:34` |
| Logs passam do teto | sem teste |
| Anexos do usuário passam do teto | `SupportTicketServiceTests.cs:91` |
| Anexo extra vazio | sem teste |
| Duas notas no chamado | `SupportTicketServiceTests.cs:16` afirma o texto do usuário e o número "101" |
| Nota sem número | sem teste |
| Estimativa antes de abrir | `SupportTicketServiceTests.cs:102`. O teto na resposta fica no endpoint, sem teste |
| Estimativa sem seleção | `SupportTicketServiceTests.cs:102` |
| Os gateways Freshdesk e Local | sem teste nenhum |

## Risks / Trade-offs

- **[A spec sai mais estreita que o rascunho]** → Cada frase que saiu está num achado, com o texto original. Ela volta
  quando a correção correspondente for arquivada.
- **[Arquivar com achado aberto]** → Cada grupo do `tasks.md` só fecha depois da decisão sobre os achados dele.
- **[O A1 e o A7 estão em produção hoje]** → Esta change só os registra. A mitigação é decidir antes do apply e abrir as
  changes de correção.
- **[A repetição com a `discovery-queue-consumer`]** → É consistente hoje (D4). Uma mudança futura na dead-letter
  precisa tocar as duas specs.
