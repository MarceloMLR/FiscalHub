# Design — a mensagem envenenada na dead-letter

O defeito é de **uma linha** (`DeadLetterHandler.cs:18-19`), mas a correção não é de uma linha, porque a linha errada
não é a que o STATUS.md aponta. Tudo aqui é sobre separar duas coisas que hoje chegam à casca disfarçadas da mesma:
**"a mensagem é inválida"** e **"o tratamento falhou"**.

O código citado foi lido em 2026-10-16, no worktree `FiscalHub-B`, branch `fix/dead-letter-poison`.

---

## D0 — Por que "concluir sempre" é o conserto errado

Antes de qualquer decisão, o descarte da alternativa óbvia, porque é a que alguém vai propor de novo daqui a seis
meses.

Mover `CompleteMessageAsync` para antes do `HandleAsync`, ou envolvê-lo num `finally`, **para o laço**. E cria dois
defeitos no lugar de um:

1. **O corpo inválido some em silêncio.** Hoje ele pelo menos repete no log. Concluído sem registro, some de vez — e o
   STATUS.md já tem o padrão "defeito que sobrevive porque ninguém o vê".
2. **A falha transitória vira perda de documento.** SQL reiniciando por trinta segundos passa a concluir e descartar
   cada mensagem que chegar nessa janela. A nota fica sem registro de dead-letter e ninguém sabe que ela existiu. É
   **pior** do que o D2: o D2 é barulhento e recuperável; isto é silencioso e definitivo.

A ordem da chamada, portanto, **não muda**. O que muda é o que chega a ela como exceção.

---

## D1 — A decisão sai da casca e vai para o handler

**Decisão.** `DeadLetterHandler.HandleAsync` deixa de receber um motivo já resolvido e passa a receber **os fatos da
mensagem**, num registro próprio, e a devolver **o desfecho**:

```csharp
internal readonly record struct DeadLetterMessage(
    BinaryData Body, string? Reason, string? ErrorDescription,
    string MessageId, long SequenceNumber, DateTimeOffset EnqueuedAt, string Queue);

internal enum DeadLetterOutcome { Recorded, Discarded }

Task<DeadLetterOutcome> HandleAsync(DeadLetterMessage message, CancellationToken ct);
```

A casca fica com plumbing puro: monta o `DeadLetterMessage` a partir do `args.Message`, chama, conclui.

**Por quê.** Porque a casca é o único lugar do caminho **sem teste**, e vai continuar sem — ver D2. Toda decisão que
mora nela é uma decisão que ninguém prova. Hoje moram lá duas: a cadeia do motivo (D5 do STATUS.md) e a escolha de
concluir. As duas defeituosas. Não é coincidência.

É o mesmo diagnóstico da change `adapter-key-guard-proof` ("a função tem teste, quem a chama não tem") com a resposta
invertida: lá o jeito de fechar foi testar a chamada; aqui é **esvaziar** a chamada até não haver o que testar nela.
Mais barato, e no mesmo espírito do comentário que já está no `DeadLetterHandler`: "Separado da casca pra ser testável."

**O que fica na casca, e é aceito sem teste:** três atribuições de campo e um `CompleteMessageAsync`. Apagar o
`CompleteMessageAsync` volta a produzir o D2 sem quebrar teste nenhum — é o limite honesto desta fatia, e é exatamente
o que a tarefa opcional do emulador (tasks, grupo 4) existe para fechar. Se ela não entrar, o limite fica escrito.

---

## D2 — A casca **não** ganha teste unitário

**Decisão.** Nenhum teste novo sobre `DeadLetterTriggerService.OnMessageAsync`.

**Por quê.** Ele recebe `ProcessMessageEventArgs`, que carrega um `ServiceBusReceiver` vivo — `CompleteMessageAsync`
vai ao receiver. Para testá-lo seriam precisos ou (a) uma biblioteca de mock, e o repositório não tem nenhuma
(conferido: nem `Moq` nem `NSubstitute` em `.csproj` de teste algum; o padrão é duplo escrito à mão, como o `FakeStore`
do próprio `DeadLetterHandlerTests`), ou (b) o emulador de verdade.

Trazer uma dependência nova de teste para provar quatro linhas de plumbing é caro. O emulador prova mais, custa menos
em estrutura, e já tem precedente no repositório — vai para o D7.

---

## D3 — A mensagem inválida é descartada, e o que fica dela é um registro de log

**Decisão.** Corpo inválido → **um** `LogError` com o corpo inteiro (limitado) e a identidade da mensagem → conclui.
Nada no store.

**Por que nada no store.** O store é indexado por `(TenantId, NaturalKey)` — é a chave do `UpsertAsync` e do
`AlreadyProcessedAsync`. Um corpo inválido não tem nem um nem outro. As saídas seriam:

| Alternativa | Por que não |
|---|---|
| inventar tenant/chave (`"desconhecido"`, o `MessageId`) | a linha apareceria no painel de **algum** tenant. Numa instalação multi-tenant isso é vazamento de limite (`tenant-boundary`, ADR-0028): a nota de um cliente aparecendo para outro. Inaceitável |
| gravar com tenant/chave vazios | linha órfã que nenhuma consulta com `WHERE TenantId = @t` encontra. Grava e não mostra: o pior dos dois mundos |
| tabela nova de quarentena | esquema novo, que o CLAUDE.md §9 proíbe. E sem tela, uma tabela que ninguém lê é um log mais caro |

**Por que o log é suficiente aqui, e não é "sumir em silêncio".** Porque o que falta hoje não é o canal, é o
**conteúdo**. Hoje a exceção sobe ao `OnErrorAsync`, que loga `args.Exception` e `args.EntityPath` — sem o corpo, sem o
`MessageId`, sem nada que permita reconstruir a mensagem —, e repete por entrega. Depois, o corpo inteiro fica escrito
**uma vez**, com a identidade, num evento com nome próprio. Quem opera ganha informação e perde ruído.

**O que vai no registro, e por quê cada campo:** o corpo (é o dado — sem ele não há o que reconstruir); a fila (há duas,
`documents-in` e `documents-discovered`, e elas têm donos diferentes); o `MessageId` e o `SequenceNumber` (identificam
a mensagem sem ambiguidade); o `EnqueuedTime` (situa no tempo, para cruzar com o que mais aconteceu); e os **dois**
campos de motivo do transporte, **crus** (dizem por que ela caiu na dead-letter antes de ser descartada — as duas
coisas não são a mesma). Por que crus, e não com o piso do D5 aplicado, é o **D8**.

**Limite de tamanho.** O corpo entra cortado num teto (a sugestão é 4 KB) e o registro diz o tamanho original. O corpo
normal é uma `DocumentReference`, que são centenas de bytes — o claim-check mantém o documento no Blob (ADR-0006), e
não na mensagem. O teto existe para o corpo **anormal**, que é justamente o caso que chega aqui; uma mensagem do
Premium pode ter 100 MB, e uma linha de log de 100 MB derruba o que a está lendo.

**Sobre dado sensível no log.** O corpo da fila é referência, não documento: tenant, chave natural, locator, grupo. Não
carrega segredo nem conteúdo fiscal, e nenhum caminho de produção põe payload lá. Vale dizer explicitamente porque
"logar o corpo" é o tipo de decisão que merece ser revisada se o contrato da fila mudar.

---

## D4 — O que conta como inválido, e por que o tenant/chave em branco entra

**Decisão.** Inválido = o corpo não vira uma referência utilizável. Três portas:

1. **não desserializa** — não é JSON, ou é JSON que não casa. Como `DocumentReference` tem quatro propriedades
   `required`, o `System.Text.Json` lança `JsonException` quando falta qualquer uma. `{}` **não** produz um objeto
   meio-vazio: lança. Isso é bom, e é o que torna a porta 1 larga;
2. **desserializa para `null`** — o corpo literal `null`, que é o caso do teste `Handle_throws_on_empty_body` de hoje;
3. **desserializa, mas `TenantId` ou `NaturalKey` está em branco** — `{"tenantId":"","type":"GoodsInvoice55",
   "naturalKey":"","locator":""}` passa pelo `required` (a propriedade está presente) e **hoje grava uma linha com
   tenant e chave vazios**.

**Por que a terceira entra nesta fatia, e não é escopo que cresceu.** É a mesma pergunta do D5 — "vazio não é nulo" —
aplicada à outra ponta da mesma mensagem, e é a **mesma linha de código** que decide. Deixá-la de fora significaria
consertar o motivo vazio e manter o tenant vazio, que é o furo maior dos dois: um grava texto ruim num campo, o outro
grava uma linha inalcançável no banco. E a resposta é a mesma do D3: sem tenant e sem chave, não há documento.

**O que NÃO é inválido:** `Locator` em branco, `Type` qualquer, `Metadata` nulo. São referências ruins, mas
**atribuíveis** — viram linha do tenant certo, com o motivo da dead-letter, e aparecem no painel de quem precisa vê-las.
É precisamente o que o ADR-0010 quer.

---

## D5 — A falha de tratamento não ganha log novo

**Decisão.** O caminho da exceção fica como está: ela sobe, a casca não conclui, o `OnErrorAsync` loga. Nada de
`try/catch` novo na casca, nada de log adicional.

**Por quê.** Um `catch` que loga e relança **duplica** a linha (o SDK entrega a exceção do handler ao
`ProcessErrorAsync` de qualquer forma). Um `catch` que loga e engole é a forma do D1 do STATUS.md — "engole a exceção
antes de gravar" —, e não se conserta um defeito importando outro. E o que falta no log da falha transitória é pouco: a
exceção já diz o que quebrou, e ela se repete por desenho.

**Consequência que vale registrar: não abandonar explicitamente é melhor do que abandonar.** Com
`AutoCompleteMessages = false`, a exceção faz o lock expirar — a reentrega vem depois do tempo de lock, e não
imediatamente. Um `AbandonMessageAsync` devolveria a mensagem na hora e transformaria uma falha persistente em laço
quente. A espera do lock é o amortecedor, e ele é de graça.

---

## D6 — A foto no Blob foi avaliada e recusada

**Decisão.** O corpo descartado **não** vai para o Blob.

**Por quê.** O `IProcessingTrace` existe e seria o lugar natural de "um dado que alguém precisa ver" — mas os quatro
métodos dele são `(tenantId, naturalKey, …)`, e o `TracePaths.DocumentPrefix` é `{tenant}/{aaaaMM}/{chave}/`. Guardar
um corpo sem tenant e sem chave exigiria **um método novo na porta** e **um layout reservado**, o que: arrasta
`NoOpProcessingTrace`, `BlobProcessingTrace`, `TracePaths` e o `DocumentTraceQuery` que varre o prefixo; cria fotos que
o `/trace` e o zip não sabem listar (eles são por documento); e, sem tela, entrega um arquivo que ninguém abre. É
outra fatia, com outra tela, e está na Open Question 1.

A régua: **esta change conserta um laço infinito.** O que ela precisa entregar é que o corpo não se perca e que a
reentrega pare. O log entrega os dois.

---

## D7 — O ponta a ponta contra o emulador: opcional, pulado por padrão

**Decisão.** Um teste que sobe o emulador do Service Bus (já no `docker-compose.yml`), publica um corpo lixo na fila
real, deixa a entrega esgotar até a dead-letter, e afirma que a mensagem **é consumida uma vez e some**. Marcado com o
padrão que o repositório já usa para teste que precisa de infraestrutura: atributo próprio com `Skip` quando a variável
de ambiente não está definida — `KeyVaultEmulatorIntegrationTests`, `D365ChangeFeedIntegrationTests`,
`SqlServerHarnessFactAttribute`.

**Por que opcional.** `dotnet test` tem que ficar verde sem container. Um teste que exige infra acaba pulado de
qualquer jeito; melhor que ele **diga** que foi pulado, e por quê, do que que ele quebre a suíte de quem não subiu o
compose.

**Por que vale mesmo assim.** É a única coisa que prova o que a change promete — "não volta a ser entregue sem fim" —
e a única que cobre as quatro linhas da casca (D1). O resto da fatia prova a decisão; só ele prova o efeito.

**E ele mede o que não sabemos.** Duas perguntas ficam em aberto sobre o comportamento do transporte, e o teste é onde
elas se respondem, se ele entrar: (a) o `DeliveryCount` de uma mensagem **já na dead-letter** incrementa a cada
reentrega? (b) a reentrega na dead-letter respeita o tempo de lock, como o D5 supõe? Nenhuma das duas é afirmada por
esta change — a (a) alimenta a Open Question 2, e a (b) é a razão de não abandonar explicitamente. **Se o teste não
entrar, as duas continuam não afirmadas**, e nenhum requisito depende delas.

### Medido na implementação (2026-10-16, emulador do `docker-compose.yml`, fila `documents-in`, `MaxDeliveryCount` 5)

O teste entrou, e as duas perguntas foram medidas por uma sonda descartável, fora da suíte. **Nenhuma destas medidas
virou requisito nem asserção** — elas vivem aqui, como o D7 manda.

| O quê | Medido |
|---|---|
| Entregas até cair na dead-letter | **5** abandonos; `DeliveryCount` **5** na última entrega da fila principal |
| O que o transporte preenche | `DeadLetterReason` = `MaxDeliveryCountExceeded`; `DeadLetterErrorDescription` = `Message could not be consumed after 5 delivery attempts.` |
| **(a)** `DeliveryCount` na dead-letter | **6 na primeira entrega, e 6 em todas as seguintes** — não incrementa, nem por abandono explícito (duas vezes) nem por expiração de lock |
| **(b)** Reentrega na dead-letter respeita o lock | **Sim: 60,0 s**, exatamente o `LockDuration` `PT1M` da fila |

**Consequência para a Open Question 2.** A saída que ela sugeria — "ler o `DeliveryCount` nativo da mensagem na
dead-letter" — **não funciona**: o contador congela ao entrar na dead-letter. Uma terceira categoria ("falhou tantas
vezes que já não é transitório") precisaria de um contador **nosso**, que é esquema novo, e o CLAUDE.md §9 proíbe
criar um sem decisão própria. Isso **aumenta** o peso da Open Question 2, em vez de resolvê-la.

**Consequência para o D5.** O amortecedor do lock é real e medido: a reentrega na dead-letter espera o `LockDuration`
inteiro. Não abandonar explicitamente continua sendo a escolha certa — um `AbandonMessageAsync` devolveria a mensagem
na hora e trocaria uma espera de 60 s por um laço quente.

**O limite honesto da medida:** foi feita contra o **emulador**, e não contra um namespace do Azure de verdade. As
duas são afirmações sobre este emulador; se uma decisão de produto for depender delas, remeça contra o Azure.

---

## D8 — O colapso nulo/vazio é **por leitor**, e não global

**Decisão.** O log do descarte (D3) carrega os campos do transporte **crus** — `DeadLetterReason` e
`DeadLetterErrorDescription`, os dois, como chegaram. Ausente ou em branco aparece como `(não informado)`, e **nunca**
como o texto padrão do piso. O `ResolvedReason` do D5 continua existindo e continua indo para o store, intocado.

Isto é correção de implementação, medida depois do grupo 3: o log passava `ResolvedReason`. Com o transporte calado, o
registro escrevia "Mensagem movida para dead-letter." **ao lado do rótulo do motivo do transporte** — ou seja,
**afirmava que o transporte disse uma frase que ele não disse**. O piso é texto nosso; apresentá-lo como palavra do
transporte é dado inventado, não valor-padrão.

**Por que o piso não vale aqui.** Porque o piso do D5 foi decidido para **um leitor** e **uma pergunta**:

| | Registro no store (D5) | Log do descarte (D3) |
|---|---|---|
| Quem lê | quem opera a nota | quem reconstrói a mensagem |
| O que pergunta | "por que essa nota parou?" | "o que exatamente chegou, para eu republicar isto?" |
| "foi dito que não há motivo" × "nunca foi dito" | mesma tela vazia → **colapsam**, o piso é a resposta | respostas **diferentes** → não colapsam, "não informou" é informação |

No store, os dois casos produziriam a mesma tela vazia, e por isso colapsar é certo — **isso não muda**. No log, a
distinção é justamente parte do que se está tentando reconstruir: "o transporte não informou motivo" é um fato sobre a
mensagem, e o texto padrão o esconde.

Então a regra do projeto não é "nulo e vazio colapsam neste campo", e sim **"nulo e vazio colapsam quando o leitor não
distingue os dois"** — o campo não decide, o leitor decide. É a regra ficando mais afiada, e não uma exceção a ela: é a
mesma que nesta semana apareceu três vezes em tela (o subtítulo do card afirmando "envio desligado" com a chave ligada;
a faixa atribuindo à chave uma indisponibilidade que não era dela). Em todas, o defeito é o mesmo: **afirmar o que não
se sabe.** Um valor-padrão é resposta legítima quando é só preenchimento de tela; vira defeito no instante em que é
apresentado como palavra de outro.

**Por que `(não informado)`, e não campo vazio.** Um campo vazio no meio de uma linha de log se lê como erro de
formatação, e um `null` cru se lê como bug. O texto entre parênteses e em minúscula não se confunde com nada que o
Service Bus produza — os valores dele são `PascalCase` sem espaço (`MaxDeliveryCountExceeded`) ou frase com ponto final
(`Message could not be consumed after 5 delivery attempts.`). Se um dia o transporte mandar literalmente
`(não informado)`, o log fica ambíguo; é improvável o bastante para não valer escapar nada.

**O que prende isto.** Dois testes, e os dois falham se alguém devolver o resolvido para o log:

- `A_transport_that_said_nothing_is_not_quoted_saying_the_default_reason` — com o transporte calado (nulo, vazio e
  só-espaços), o registro **não contém** `DefaultReason`, e traz `(não informado)` nos dois campos;
- `The_discard_record_carries_the_raw_transport_fields_and_not_the_resolved_reason` — com os dois campos preenchidos,
  os dois aparecem iguais ao que chegou. É o que pega a outra metade: o `ResolvedReason` entrega **um** só, porque a
  cadeia do piso descarta a descrição quando o `Reason` está preenchido.

A asserção frouxa — "tem alguma coisa no campo de motivo" — passa com e sem o conserto, e por isso não serve.

---

## Open Questions

1. **O corpo descartado precisa de artefato durável e de tela?** A decisão desta change é **não** (D3, D6): log, e nada
   no store. O raciocínio é que uma mensagem sem tenant não tem usuário a quem mostrar — ela é problema de operação,
   não de cliente, e qualquer tela dela seria do Admin, que é a Fiscosys (ADR-0039). **Se o Marcelo quiser o
   contrário**, muda o tamanho da fatia: nasce uma capability de quarentena, com onde guardar, por quanto tempo, quem
   vê e o que acontece quando alguém reprocessa à mão. É change própria, e o dashboard está com a outra sessão agora.
   A decisão está tomada aqui para que a implementação não pare; é a que mais vale uma palavra antes de liberar.

2. **Uma falha de tratamento que nunca passa também é um laço.** Esta change protege a reentrega de propósito (D0, D5)
   — mas se a gravação falhar **deterministicamente** para uma mensagem (uma chave natural maior que a coluna, por
   exemplo), o resultado é o D2 por outra porta: reentrega sem fim, uma linha de log por tentativa. O amortecedor do
   lock (D5) torna o laço lento, não finito. O conserto seria uma terceira categoria — "falhou tantas vezes que já não
   é transitório" —, provavelmente lendo o `DeliveryCount` nativo da mensagem na dead-letter. **Não entra aqui**
   porque é decisão de produto própria (quantas vezes? e o que acontece com ela depois?) e porque depende de medir se
   aquele contador se mexe na dead-letter (D7). Fica escrita para não ser descoberta como surpresa.

   **Medido na implementação (ver D7): o contador não se mexe.** O `DeliveryCount` congela em 6 ao entrar na
   dead-letter e fica lá, por abandono explícito ou por expiração de lock. Ou seja, a saída barata que esta pergunta
   supunha **não existe**: contar as tentativas exigiria contador nosso, que é esquema novo (CLAUDE.md §9). A
   pergunta continua aberta, e agora mais cara do que parecia.

3. **Vale um ADR?** A proposta diz que não, e o raciocínio está lá: nenhuma fronteira se move e o ADR-0010 continua
   valendo inteiro. Mas a distinção "inválido × falhou" é o tipo de regra que vai ser consultada em outros consumidores
   — a fila de descoberta, as filas por integração do Contábil (ADR-0037) — e isso é argumento para ADR. Se o Marcelo
   quiser, é barato: um arquivo, depois, sem mexer em código.

## Suposições

Escritas porque, se alguma for falsa, a implementação muda — e é melhor que ela pare e pergunte do que que ela
improvise.

- **O corpo da mensagem da dead-letter é sempre a `DocumentReference` em JSON.** Conferido: só o
  `ServiceBusDocumentQueue` publica nas duas filas, e o `DocumentQueueSerialization` é compartilhado pelos dois lados.
  Logo um corpo inválido só aparece por mensagem de versão antiga, por publicação externa, ou por corrupção.
- **O motivo em branco não é produzido por nós.** Nenhum código chama `DeadLetterMessageAsync` (conferido); o broker
  preenche `MaxDeliveryCountExceeded`. O D5 é guarda de contrato, não defeito observado — a proposta diz isso com
  todas as letras para que ninguém procure o rastro em produção.
- **`ServiceBusReceivedMessage` expõe `MessageId`, `SequenceNumber` e `EnqueuedTime` para mensagem da dead-letter.**
  São propriedades do broker, preenchidas no enfileiramento original. Se alguma vier vazia, o registro a omite — e
  isso não altera requisito nenhum.
- **A mensagem concluída na dead-letter desaparece de vez.** Não há dead-letter da dead-letter no Service Bus; é o que
  torna "descartar" uma decisão séria, e o que obriga o log a carregar o corpo.
- **O limite de 4 KB para o corpo no log é chute informado, não medida.** O corpo normal é muito menor; o teto é para o
  anormal. Se na implementação aparecer razão para outro número, troca-se o número — não é requisito, está no
  `design.md` de propósito.
