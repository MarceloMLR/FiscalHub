## Context

Ver `proposal.md` — Why. O que o código obriga a saber antes de decidir:

- **O ponto único de saída existe.** `DocumentPipeline.ProcessAsync` (`src/FiscalHub.Application/Pipeline/DocumentPipeline.cs:86`)
  é o **único** chamador de `IComplianceDispatcher.SubmitAsync` no repositório. Automática, manual, agendada
  e reprocesso convergem todas na mesma fila e no mesmo pipeline.
- **A montagem do payload de destino NÃO é ponto único.** Ela vive dentro de cada adapter de saída. No
  `AvalaraComplianceDispatcher.SubmitAsync`, a sequência é: ler o perfil → resolver estabelecimentos →
  mapear → `SaveOutboundAsync` (a foto do payload, linha 91) → **POST** (linha ~99). Os primeiros quatro
  passos são a preparação; o quinto é o envio. Hoje eles são um método só.
- **A consulta de status é outro caminho:** `StatusPoller` chama `CheckStatusAsync`, não `SubmitAsync`.
- **A tradução para a Avalara pode fazer requisição de leitura** (a listagem de estabelecimentos da
  plataforma), quando a nota não diz a emissão.
- **A faixa de status é código, não configuração:** `SqlDocumentQueries` tem três arrays (`Finished`,
  `Processing`, `Failed`) que alimentam cards e grupos.
- **Trabalho concorrente:** outra sessão está no worktree `FiscalHub-B`, branch `fix/dead-letter-poison`,
  consertando o D2 no `DeadLetterTriggerService`. Nenhum arquivo dessa área entra aqui.

## Goals / Non-Goals

**Goals:**

- Que o corte do envio seja **estrutural**, e não uma lembrança que cada adapter novo precise ter.
- Que o payload de destino fique gravado mesmo sem envio, pelo mesmo caminho que o grava hoje — sem uma
  segunda rotina de montagem que possa divergir da de produção.
- Que o vocabulário de status novo atravesse os leitores de uma vez, sem deixar tela mentindo.
- Que o padrão seja o comportamento de hoje: subir esta versão não muda nada para quem não mexer na chave.

**Non-Goals (de design):**

- Impedir, em tempo de compilação, que um adapter mal escrito poste no lugar errado. Não é alcançável com o
  que temos; §1 diz o que fica no lugar.
- Reorganizar o `IComplianceDispatcher` além do necessário para a separação preparar/enviar.
- Tocar no `DeadLetterTriggerService`, no `DeadLetterHandler` ou nos testes deles.

---

## 1. A decisão estrutural: o despacho vira dois passos

Esta é **a** decisão desta change. O payload de destino só existe dentro do adapter de saída, uma linha
antes do POST. Para gravá-lo sem postar, o corte desce para lá — e o risco imediato é que a regra vire
"cada adapter que se lembre de olhar a chave". Um adapter novo que esquecesse enviaria, e o esquecimento só
apareceria em produção, com nota fiscal.

### O desenho escolhido

O `IComplianceDispatcher<TDocument>` deixa de ter `SubmitAsync` e passa a ter dois métodos:

```
PreparedDispatch<TDocument> PrepareAsync(TDocument document, DispatchContext context, ct)
IntegrationReceipt          SendAsync(PreparedDispatch<TDocument> prepared, DispatchContext context, ct)
```

- **`PrepareAsync`** faz tudo o que hoje acontece antes do POST: lê a configuração do tenant, resolve os
  estabelecimentos, mapeia o documento e **grava a foto do payload de destino**. Devolve um
  `PreparedDispatch` — um token opaco para a Application, que carrega o que o `SendAsync` daquele adapter
  precisa (no caso da Avalara: o payload, a URI de envio, as settings lidas e as omissões do mapeamento).
  As rejeições permanentes que hoje saem antes do POST (`DispatchRejectedException`) saem daqui.
- **`SendAsync`** faz o POST e classifica a resposta. **Só ele** faz requisição de envio.
- **O pipeline** chama `PrepareAsync` sempre; chama `SendAsync` **só se a chave estiver ligada**. Com a
  chave desligada, grava "Salva" com o motivo e termina.
- **O adapter nunca recebe a chave.** Nem por parâmetro, nem por contexto, nem por injeção.

### O que este desenho garante, e o que não garante

Escrito literalmente porque é o ponto que precisa ser conferido, não presumido.

**Garante:**

- **Um adapter novo não consegue ignorar a chave por esquecimento, porque não há nada para lembrar.** A
  chave não chega ao adapter. Ele não tem `if`, não tem flag, não tem campo no contexto. A decisão é tomada
  num lugar só, no pipeline, e esse lugar é o mesmo por onde as quatro origens passam.
- **Não dá para enviar sem ter preparado e fotografado.** `SendAsync` só aceita um `PreparedDispatch`, que
  só `PrepareAsync` produz. O compilador recusa a chamada sem ele. Isso fecha a ordem: a foto do payload
  sempre existe antes do envio. **De fora do assembly do adapter isso é absoluto** — `PreparedDispatch` é
  abstrato e a subclasse concreta de cada adapter é `internal`, então a esteira não tem como fabricar um.
- **Um adapter novo não compila com a forma antiga.** Trocar `SubmitAsync` por dois métodos é uma quebra de
  interface: um implementador que não se mexa não compila, e `TreatWarningsAsErrors` não deixa passar de
  raspão. Ninguém herda silenciosamente o comportamento errado.

**NÃO garante — e isto não está garantido, é mitigado:**

- **Nada impede que um adapter novo faça o POST dentro do `PrepareAsync`.** Nenhum tipo, nenhuma regra de
  compilação e nenhum guard de startup detecta isso: é um `HttpClient` chamado no método errado. O
  compilador não distingue "montar" de "postar".
- **De dentro do assembly do adapter, a ordem também não é garantida.** Quem mexe no próprio adapter pode
  montar um `AvalaraPreparedDispatch` na mão e chamar o `SendAsync` com ele, pulando a foto do payload — a
  subclasse é `internal`, e `internal` é visível para o adapter inteiro. É a **mesma classe de violação** do
  POST no método errado, com a mesma natureza (contrato quebrado por quem escreveu o adapter, e não
  esquecimento de quem não foi avisado) e a mesma mitigação: nome, documentação do contrato e revisão de
  código. A suíte de conformidade **não** alcança este caso: ela exercita o adapter pela porta, e um
  preparado montado na mão é código que ninguém chamou.
- O que muda é a **natureza da falha**, e vale ser honesto sobre o tamanho disso: deixa de ser um
  **esquecimento** (não escrevi o `if` que ninguém me lembrou de escrever) e passa a ser uma **violação de
  contrato** (escrevi o envio no método que diz, no nome e na documentação, que não envia). A segunda é
  visível em revisão de código e cabe num teste; a primeira não aparecia em lugar nenhum.
- **A mitigação é um teste de conformidade**, não uma garantia: uma classe base
  (`ComplianceDispatcherContractTests<T>`) que roda `PrepareAsync` com um `HttpMessageHandler` que **falha
  o teste se for chamado** — o `StubHttpMessageHandler` do projeto de teste da Avalara já dá a base. O
  adapter da Avalara passa a herdá-la nesta change.
- **A mitigação é ela própria esquecível, e hoje nem é compartilhada.** Não existe projeto de teste
  compartilhado entre adapters no repositório; a classe base nasce **dentro** do projeto de teste da
  Avalara, porque é o único adapter de saída que existe. Um segundo adapter de saída precisará de alguém
  que se lembre de promovê-la a uma biblioteca comum **e** de herdá-la. Nada no build obriga nenhuma das
  duas coisas — o `AdapterDeclarationGuard` é guarda de *startup* sobre declarações registradas, e não
  alcança comportamento de método. Promover a classe base é a primeira tarefa do dia em que o segundo
  adapter de saída aparecer, e está nos Próximos passos por isso.

**Veredito honesto:** o desenho elimina a classe de falha "esqueci de olhar a chave" por construção, e
reduz a classe "postei no lugar errado" a uma violação de contrato nomeada, detectável em revisão e coberta
por um teste que o próximo adapter precisa lembrar de herdar. Não é garantia total. Se garantia total for o
requisito, o caminho seria outro — ver "Alternativa C" abaixo —, e ele tem um custo que esta fatia não
paga.

### Alternativas consideradas

**A. Uma flag no `DispatchContext` (`Dispatch = false`), e cada adapter decide.** Rejeitada. É exatamente o
"cada adapter que se lembre": um adapter que ignore o campo compila, roda e envia. A falha é silenciosa e
só aparece com nota enviada que não devia.

**B. O pipeline não chama o adapter quando a chave está desligada.** Rejeitada pela resposta à pergunta 1:
sem chamar o adapter, o payload de destino nunca é montado, e o que fica gravado é só o modelo de domínio —
que não responde "o que a plataforma vai receber".

**C. Isolar o envio num tipo que só o pipeline consegue construir** (ex.: o adapter devolve uma descrição
de requisição e quem executa o HTTP é um componente da Application). Isso **garantiria** o que o desenho
escolhido só mitiga: o adapter deixaria de ter `HttpClient`. Rejeitada por custo e por risco: hoje o
`AvalaraComplianceDispatcher` lê a resposta uma vez, redige, fotografa e classifica num bloco só
(ADR-0027), e a classificação depende de status, cabeçalhos e corpo. Mover o HTTP para fora desmonta essa
unidade e obriga a reconstruir a redação e a fotografia da resposta noutro lugar — uma refatoração maior
que a chave que foi pedida, no caminho mais sensível do produto. Fica registrado como o caminho se a
garantia total virar requisito.

**D. Um analisador de Roslyn que proíba `HttpClient` dentro de `PrepareAsync`.** Rejeitada para esta fatia:
é infraestrutura nova de build para um adapter só, e um analisador assim é contornável por indireção (um
helper chamado de dentro do método). Dá a sensação de garantia sem a garantia.

---

## 2. Onde a chave mora

**Decisão:** coluna própria em `ConnectorProfiles` (`OutboundDispatchEnabled`, `bit NOT NULL`, padrão `1`),
exposta como `TenantConnectorProfile.OutboundDispatchEnabled`.

**Por que não dentro de JSON de settings:**

- **`poll.enabled` das settings de entrada** é de outro eixo (o coletor), e a spec `automatic-integration`
  proíbe explicitamente um segundo campo que diga aquilo. Enfiar a chave de **saída** na configuração do
  adapter de **entrada** confundiria dois interruptores que o usuário já precisa distinguir.
- **As settings de conector** são por conector. A chave é independente de conector, por decisão do produto.
  Guardá-la lá obrigaria a inventar uma regra de "qual conector manda" assim que houvesse dois.

**O precedente contrário, respondido:** a migração `RemoveConnectorProfileRealtime` **tirou** um booleano do
perfil. Tirou porque o estado que ele guardava já existia noutro lugar — o `poll.enabled` que o coletor de
fato lia —, e duas fontes para a mesma verdade criam a pergunta de qual vale. Aqui não há lugar existente:
nenhum campo de hoje é lido pelo despacho. A coluna é a primeira e única fonte.

**Padrão ligado:** a coluna nasce `1`, e o `NOT NULL DEFAULT 1` cobre as linhas existentes. Um tenant sem
perfil nenhum também envia — a leitura trata a ausência de perfil como "ligada", que é o comportamento de
hoje.

## 3. Por onde a chave chega à tela

**Decisão:** pelas duas respostas, com papéis diferentes.

| Resposta | Papel | Para quê |
|---|---|---|
| `GET`/`PUT /connector` | só Admin | desenhar e gravar a chave no formulário de Configurações |
| `GET /info` | qualquer papel | decidir se o botão de reprocessar sai habilitado ou desativado-com-motivo |

O botão de reprocessar está no detalhe do documento, que **qualquer papel** abre, e esse componente já lê o
`/info` (`useInfo`). Pôr a chave só no `/connector` deixaria o Viewer sem como saber por que o botão está
morto — e o `/connector` é 403 para ele. É a mesma divisão que o `automaticIntegration` já segue, pela mesma
razão.

**Nome na resposta:** `outboundDispatch` (booleano) nas duas. Não reusar `automaticIntegration` nem nome
parecido: são chaves diferentes, e o custo de confundi-las é alto.

**Invalidação:** salvar em Configurações já invalida o `['info']`; a chave entra de carona nisso.

## 4. O corte no pipeline, e o que ele não corta

```
resolver origem → buscar → idempotência → foto do domínio → metadados → validar
        → PrepareAsync  (traduz + foto do payload de destino; pode rejeitar)
        → chave ligada?  ── não ──→ RecordSavedAsync("Salva")
                         └─ sim ──→ SendAsync → RecordSubmissionAsync
```

A chave é lida **depois** do `PrepareAsync`, de propósito: é isso que faz a decisão "a chave desligada não
esconde rejeição" (ver spec) cair de graça. Validação, roteamento, contrato do destino e configuração
faltando continuam produzindo os desfechos de hoje, com a chave em qualquer posição. Só a nota que chegaria
ao POST vira "Salva".

**Menos as recusas de autenticação, e isso é consequência, não descuido.** O pedido do token é o
`ApplyAuthAsync`, uma linha antes do POST, e por isso ficou no `SendAsync`. As quatro recusas de credencial
— conta de plataforma ausente, Client ID ou referência do segredo ausentes, endpoint de token ausente, e
valor do segredo ausente no cofre — não são procuradas com a chave desligada, e a nota fica em "Salva". A
spec diz quais recusas aparecem e quais não, e por que essas quatro não precisam aparecer nota a nota: elas
são condição **do tenant**, e o lugar que responde por elas é o "Testar credencial" de Configurações. A
alternativa — descer a leitura da credencial, ou o próprio segredo do cofre, para o `PrepareAsync` — foi
considerada e recusada: cobriria três dos quatro casos sem um método novo na porta do token provider, e
deixaria a spec meio verdadeira, que é pior de explicar do que claramente escopada.

**A leitura de estabelecimentos continua acontecendo com a chave desligada.** `PrepareAsync` pode precisar
da listagem da plataforma para montar o payload. É requisição de **leitura**, e a mesma regra que vale para
a consulta de status vale aqui: ler não é enviar. Sem ela o payload não existe, e não haveria o que
conferir — que é o uso que motivou a chave.

**Onde a chave é lida:** o pipeline recebe uma porta nova da Application, `IOutboundDispatchSwitch`, com
`Task<bool> IsOnAsync(tenantId, ct)`, implementada sobre o `IConnectorProfileStore`. O pipeline não ganha
dependência de Infrastructure, e a leitura fica testável sem banco.

## 5. "Salva" no vocabulário de status

`IntegrationStatus` ganha `Saved`. Rótulo na tela: **"Salva"**.

- **Gravação:** `IProcessingStore.RecordSavedAsync(reference, reason, ct)`, com o mesmo upsert do
  `RecordIgnoredAsync` — preserva o `ExternalId` já gravado, se houver (uma nota que já foi enviada um dia e
  é recapturada com o envio desligado não perde o rastro do envio antigo).
- **Motivo gravado:** texto fixo dizendo que o envio ao conector de saída está desligado. É o que a tela
  mostra na faixa do motivo da nota — a mesma faixa de qualquer desfecho, e não um aviso sobre a chave: ele
  diz **por que esta nota está neste estado**, e não o estado presente da chave (§7).
- **Idempotência:** `AlreadyProcessedAsync` passa a aceitar `Saved` ao lado de `Submitted` e `Confirmed`.
  A razão está na proposta e virou requisito.
- **Consulta de status:** nada a fazer. `ListPendingAsync` filtra `Submitted` **com** `ExternalId`; nota
  salva não tem identificador externo e não entra.
- **Tom na tela:** reusa o token `pending`, o mesmo do "Pendente" e do "Ignorado". Decisão deliberada: a
  spec `dashboard-brand-identity` proíbe mexer nas cores de status, e um token novo exigiria medir
  contraste nos dois temas por uma distinção que o rótulo já faz. "Salva" é parada deliberada, não alarme —
  neutro é a cor certa.

**Os leitores e o que muda em cada um** estão tabelados em `proposal.md` — Impact.

## 6. O quarto card e o selo do grupo

`DocumentGroup` e `ModelTotals` ganham `Salvas`. `SqlDocumentQueries` ganha a quarta faixa, `Saved`, fora
das outras três. O card "Salvas" entra na linha de KPIs do `GroupsPage`; `cardSums` soma mais um campo.

O selo do grupo (`groupStatus`) passa a:

```
comErro > 0        → "Com pendências"
emProcessamento > 0 → "Processando"
salvas > 0         → "Salvas"            (novo)
senão              → "Finalizado"
```

O erro continua tendo precedência: um grupo com 7 salvas e 1 rejeitada diz "Com pendências", porque é a
rejeitada que precisa de alguém.

**A ponta que sobra, e por que sobra:** este conserto **não** cobre o "Ignorado". Um grupo de 10 NFS-e
ignoradas continua dizendo "Finalizado" com tudo zerado, como hoje. O conserto é estruturalmente o mesmo —
contagem `Ignoradas`, estado no selo, e a decisão de produto sobre um quinto card —, mas é mudança de
comportamento numa faixa que esta fatia não precisa tocar. Vai para os Próximos passos em vez de alargar o
escopo. A alternativa barata que existe — trocar o `senão` por "Finalizado só quando `finalizadas ===
total`" — resolveria os dois de uma vez, mas exige inventar o rótulo do caso restante, que é decisão de
produto; fica registrada para a conversa.

## 7. O botão de reprocessar e o motivo nele

Duas perguntas diferentes, e misturá-las foi o defeito que a conferência em tela de 2026-10-16 achou:
**quem tem botão** sai do status da nota; **como o botão sai** — habilitado ou desativado-com-motivo — sai da
chave.

### Quem tem botão: um predicado próprio, e não `isFailure`

`DocumentDetail` mostrava o botão quando `isFailure(status)`. Isso deixava a nota em **"Salva" sem botão**,
mesmo com a chave ligada — e o reprocesso manual é o **único** caminho de saída de "Salva" (ADR-0040: religar
não reenvia, e a idempotência por conteúdo a trata como terminal). O servidor sempre aceitou: o
`DocumentReprocess.ReprocessAsync` não tem guarda de status. Só a tela não oferecia.

A elegibilidade passa a ser um predicado próprio, ao lado do de falha, em `StatusChip.tsx`:

```
FAILURE_STATUSES      = ['IntegrationError', 'Unconfirmed', 'DeadLettered']      // isto é erro
REPROCESSABLE_STATUSES = [...FAILURE_STATUSES, 'Saved']                          // isto o reprocesso tira do lugar
```

**As duas listas são diferentes de propósito, e isso fica escrito no código**, porque unificá-las quebra de um
lado ou do outro, e em silêncio nos dois: `'Saved'` dentro de `FAILURE_STATUSES` põe a nota salva no filtro de
falhas e na contagem de erro (contra a spec: "Salva" não é falha); e `isFailure` como elegibilidade repete o
defeito de origem. A alternativa de uma lista só com um `exclude` em cada leitor foi recusada — seria a mesma
decisão espalhada por N chamadores, em vez de um nome.

### Como o botão sai: visível e desativado, com o motivo nele

Com a chave desligada, o botão **continua visível** e fica **desativado**; a explicação mora **no próprio
botão**. Não há faixa na página. A primeira versão removia o botão e punha uma faixa, e tinha dois problemas:

- a faixa aparecia em **qualquer** nota — inclusive numa "Ignorado" ou "Finalizado", que nunca teve
  reprocesso —, atribuindo à chave uma indisponibilidade que não é dela;
- a própria spec já dizia que o caso a resolver é o "botão desativado sem explicação", e não o botão ausente.

A explicação só existe onde o botão existe. Nota sem botão, nenhuma explicação.

**Acessibilidade, que é o que decide a forma.** Um `<button disabled>` não recebe hover nem foco em vários
navegadores — um `title` nele simplesmente nunca chega ao usuário. Então:

- o desativado é **`aria-disabled="true"`** mais uma **guarda no `onClick`** (que não dispara a mutation), e o
  botão continua focável;
- o motivo é um `role="tooltip"` próprio, ligado ao botão por **`aria-describedby`**, que aparece nas **três**
  entradas: apontar (hover), **foco por teclado** e **clique/toque** (o clique alterna, que é o que serve a
  quem não tem mouse);
- sem dependência nova: `dashboard/src/components/` só tem `Controls`, `FhDataGrid`, `Modal` e `StatusChip`, e
  não há biblioteca de tooltip no projeto. É um componente pequeno no próprio arquivo.

**E ela flutua: portal para o `document.body`, não `absolute` no fluxo.** A primeira versão era um balão
`position: absolute` dentro do detalhe. O detalhe mora no corpo do modal, que é `overflow: auto`
(`components/Modal.tsx`), e o cartão é `overflow: hidden` — então o balão não flutuava por cima: ele crescia o
`scrollHeight` do corpo e fazia **aparecer barra de rolagem** no modal (conferência em tela de 2026-10-16). O
modal do detalhe é mais baixo do que a dica, e um tooltip não mexe no layout de quem o hospeda. A dica passa a
ser renderizada com `createPortal` (o `react-dom` já é dependência) para o `document.body`, em
`position: fixed`, posicionada a partir do `getBoundingClientRect()` do próprio botão: abaixo dele e alinhada à
direita, acima se não couber abaixo, presa à janela dos dois lados, e `zIndex` acima do modal (50 → 60).
Enquanto está aberta, fecha na **rolagem** (escutada em **captura**, que é como se pega a rolagem do corpo do
modal) e no **resize**: uma dica ancorada num botão que se moveu é pior do que uma dica que fechou.

O portal muda o lugar no **DOM**, e não na **árvore React** — o clique na dica continua subindo até o
`stopPropagation` do cartão do modal, então ele não fecha. É conferido por teste, e não presumido.

**Texto:** *"O envio ao conector de saída está desligado para este tenant. Ligue-o em Configurações →
Conectores → Saída para voltar a enviar."* Diz a causa e o caminho; não repete "o reprocesso está
indisponível", porque disso o botão desativado já é a prova.

### Os três estados do `/info`

**Enquanto o `/info` não respondeu** (`info === undefined`), ou se a leitura falhou, a tela não afirma nenhum
dos dois: o botão fica **ausente** — nem habilitado, nem desativado-com-motivo. Só com a resposta em mãos ele
aparece. É a mesma regra que o `canViewRawJson` já segue: na dúvida, a tela não afirma.

**No servidor:** o endpoint de reprocesso **não** ganha uma segunda checagem. A porta é o pipeline: um
pedido que chegue por fora da tela reenfileira a nota, que reentra na esteira e volta a parar em "Salva".
Pôr a regra em dois lugares é criar o segundo lugar onde ela pode divergir — exatamente o que a §1 evita.

## Risks / Trade-offs

- **[Um adapter futuro posta dentro do `PrepareAsync`]** → nome do método, documentação do contrato e o
  teste de conformidade compartilhado. Não é garantia; §1 diz o que fica de pé e o que não fica.
- **[Quebrar o `IComplianceDispatcher` é mudança larga no adapter mais sensível]** → a separação é
  mecânica: o corpo atual do `SubmitAsync` é dividido no ponto que já existe (a linha do
  `SaveOutboundAsync`). Os testes do adapter cobrem os desfechos, e eles não mudam. Nenhum comportamento
  observável de envio muda nesta fatia.
- **[Nota em "Salva" fica terminal e pode ficar esquecida para sempre]** → é a decisão de produto (religar
  não reenvia). O quarto card existe justamente para que um estoque de notas salvas seja visível em vez de
  sumir no total.
- **[A chave desligada ainda faz requisição de leitura à plataforma]** → consistente com a decisão da
  consulta de status, e necessária para montar o payload. Fica escrita na spec para não virar surpresa.
- **[Confusão entre as duas chaves — "Integração automática" e "Enviar ao conector de saída"]** → ficam em
  abas diferentes de Configurações (entrada × saída) e com nomes que dizem o eixo. Risco real, mitigado por
  rótulo; vale conferir na tela antes de fechar a fatia.
- **[O selo do grupo continua mentindo para "Ignorado"]** → conhecido, registrado nos Próximos passos, e
  **não** consertado aqui, por decisão explícita de não alargar o escopo.

## Migration Plan

1. Migração EF com a coluna `OutboundDispatchEnabled bit NOT NULL DEFAULT 1` em `ConnectorProfiles`. Sem
   backfill: o default cobre as linhas existentes.
2. Subir a versão não muda comportamento de nenhum tenant — todos sobem com a chave ligada.
3. **Rollback:** a coluna é aditiva e tem default; voltar o código não exige voltar o banco. Um tenant que
   tenha desligado a chave volta a enviar ao rodar a versão anterior, e as notas em "Salva" ficam com um
   status que o código antigo não conhece — a tela antiga as mostraria com o rótulo cru (o `StatusChip` já
   tem fallback para status desconhecido) e elas não entrariam em nenhuma das três faixas. Degradação
   legível, não quebra. Vale citar isso no PR.

## Open Questions

- ~~**O texto exato do aviso do botão.**~~ Decidido na conferência de 2026-10-16: o motivo mora no próprio
  botão, com o texto da §7. Fica de pé a leitura na tela do **tooltip** — se ele cabe, se não tapa nada e se
  o contraste do tom `warn` serve nos dois temas. A primeira leitura (2026-10-16) achou o balão empurrando o
  layout do modal; consertado com o portal da §7, e a releitura em tela continua pendente.
- **A posição do quarto card na linha de KPIs** (antes ou depois de "Em processamento"). Decidir na tela,
  com o contraste conferido nos dois temas.
- **Se o estoque de notas em "Salva" merece um filtro próprio na tabela** além do card. Não foi pedido;
  depende de ver a tela com dado real.

---

## Arquivos que a implementação vai tocar

Lista nominal, para conferência contra o worktree `FiscalHub-B` (branch `fix/dead-letter-poison`) antes de
liberar a implementação. **Nenhum arquivo de dead-letter aparece aqui** — `DeadLetterTriggerService.cs`,
`DeadLetterHandler.cs` e os testes deles estão fora, por decisão.

**Application — portas e casos de uso**

| Arquivo | O quê |
|---|---|
| `src/FiscalHub.Application/Outbound/IntegrationStatus.cs` | o valor `Saved` |
| `src/FiscalHub.Application/Outbound/IComplianceDispatcher.cs` | `SubmitAsync` → `PrepareAsync` + `SendAsync` |
| `src/FiscalHub.Application/Outbound/Integration.cs` | o tipo `PreparedDispatch<TDocument>` |
| `src/FiscalHub.Application/Outbound/IOutboundDispatchSwitch.cs` | **novo** — a porta de leitura da chave |
| `src/FiscalHub.Application/Outbound/OutboundDispatchSwitch.cs` | **novo** — a leitura sobre o `IConnectorProfileStore` |
| `src/FiscalHub.Application/Pipeline/DocumentPipeline.cs` | o corte: preparar sempre, enviar sob a chave |
| `src/FiscalHub.Application/Pipeline/IProcessingStore.cs` | `RecordSavedAsync` |
| `src/FiscalHub.Application/Connectors/TenantConnectorProfile.cs` | `OutboundDispatchEnabled` |
| `src/FiscalHub.Application/Connectors/ConnectorProfileRequest.cs` | o campo na gravação e na leitura |
| `src/FiscalHub.Application/Connectors/ConnectorProfileService.cs` | ler e gravar o campo |
| `src/FiscalHub.Application/Connectors/InstanceInfo.cs` | `outboundDispatch` no `/info` |
| `src/FiscalHub.Application/Queries/IDocumentQueries.cs` | `Salvas` em `DocumentGroup` e `ModelTotals` |

**Infrastructure**

| Arquivo | O quê |
|---|---|
| `src/FiscalHub.Infrastructure/Persistence/ConnectorProfileRow.cs` | a coluna |
| `src/FiscalHub.Infrastructure/Persistence/ProcessingDbContext.cs` | o mapeamento da coluna |
| `src/FiscalHub.Infrastructure/Persistence/SqlConnectorProfileStore.cs` | ler/gravar a coluna |
| `src/FiscalHub.Infrastructure/Persistence/SqlProcessingStore.cs` | `RecordSavedAsync`; `Saved` nos terminais do `AlreadyProcessedAsync` |
| `src/FiscalHub.Infrastructure/Persistence/SqlDocumentQueries.cs` | a quarta faixa e a contagem `Salvas` |
| `src/FiscalHub.Infrastructure/Migrations/*_AddOutboundDispatchEnabled.cs` | **novo** — a migração (+ `.Designer.cs`) |
| `src/FiscalHub.Infrastructure/Persistence/ProcessingDbContextModelSnapshot.cs` | regenerado pela migração |

**Adapter de saída**

| Arquivo | O quê |
|---|---|
| `src/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara/AvalaraComplianceDispatcher.cs` | dividir em `PrepareAsync` + `SendAsync` |

**Host**

| Arquivo | O quê |
|---|---|
| `src/FiscalHub.Host/Program.cs` | DI do `IOutboundDispatchSwitch`; o campo no `PUT /connector` |

**Dashboard**

| Arquivo | O quê |
|---|---|
| `dashboard/src/types.ts` | `'Saved'`, `outboundDispatch`, `salvas` |
| `dashboard/src/features/documents/StatusChip.tsx` | rótulo "Salva", tom `pending`, fora de `FAILURE_STATUSES`; `REPROCESSABLE_STATUSES`/`isReprocessable` ao lado |
| `dashboard/src/features/documents/DocumentDetail.tsx` | botão por `isReprocessable`, desativado-com-motivo sob a chave |
| `dashboard/src/features/groups/GroupStatusChip.tsx` | o estado "Salvas" |
| `dashboard/src/features/groups/cards.ts` | `salvas` em `CardSums` e na soma; `CARD_NOTES`, os subtítulos |
| `dashboard/src/features/groups/GroupsPage.tsx` | o quarto card, com o subtítulo vindo de `CARD_NOTES` |
| `dashboard/src/features/connectors/ConnectorsPage.tsx` | o interruptor em Configurações |
| `dashboard/src/features/connectors/useConnector.ts` | o campo no estado do formulário |
| `dashboard/src/features/connectors/connectorPayload.ts` | o campo no corpo do `PUT` |

**Testes**

| Arquivo | O quê |
|---|---|
| `tests/FiscalHub.Application.Tests/DocumentPipelineTests.cs` | o corte, as duas posições da chave |
| `tests/FiscalHub.Application.Tests/GoodsInvoicePipelineTests.cs` | ajuste ao contrato novo |
| `tests/FiscalHub.Application.Tests/OutboundDispatchSwitchTests.cs` | **novo** |
| `tests/FiscalHub.Application.Tests/ConnectorProfileServiceTests.cs` | ler/gravar a chave |
| `tests/FiscalHub.Application.Tests/InstanceInfoTests.cs` | `outboundDispatch` no `/info` |
| `tests/FiscalHub.Application.Tests/StatusPollerTests.cs` | a nota salva não é consultada |
| `tests/FiscalHub.Infrastructure.Tests/SqlProcessingStoreTests.cs` | `RecordSavedAsync` e o terminal novo |
| `tests/FiscalHub.Infrastructure.Tests/SqlDocumentQueriesTests.cs` | a quarta faixa |
| `tests/FiscalHub.Infrastructure.Tests/SqlConnectorProfileStoreTests.cs` | a coluna |
| `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/AvalaraComplianceDispatcherTests.cs` | a divisão preparar/enviar |
| `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/AvalaraDispatcherPlatformCodesTests.cs` | ajuste ao contrato novo |
| `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/ComplianceDispatcherContractTests.cs` | **novo** — "`PrepareAsync` não faz requisição" |
| `dashboard/src/features/documents/DocumentDetail.test.tsx` | o botão: quem o tem, habilitado × desativado-com-motivo, e as três entradas do motivo |
| `dashboard/src/features/groups/cards.test.ts` | a soma com `salvas`; os subtítulos dos cards |
| `dashboard/src/features/groups/GroupStatusChip.test.tsx` | **novo** — o selo |
| `dashboard/src/features/connectors/connectorPayload.test.ts` | o campo no corpo |

**Docs**

| Arquivo | O quê |
|---|---|
| `docs/adr/0040-capturar-sem-enviar.md` | **novo** — o despacho em dois passos e onde a chave mora |
| `docs/adr/README.md` | o índice |
| `docs/STATUS.md` | as duas pendências nos Próximos passos (o selo do "Ignorado"; promover a suíte de conformidade) |
| `tools/MockComplianceApi/*` | conferir se o mock precisa acompanhar a divisão (provavelmente não: ele é o outro lado do HTTP) |
