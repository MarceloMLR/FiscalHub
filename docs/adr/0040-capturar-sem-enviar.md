# ADR-0040: Capturar sem enviar — o despacho em dois passos e a chave do tenant

- **Status:** Aceito
- **Data:** 2026-10-16

## Contexto

Alguns clientes não querem que a nota vá para a plataforma de compliance: querem **só o JSON gravado**, para olhar,
validar e conferir antes de mandar. O hub não sabia fazer isso — capturar e enviar eram o mesmo movimento, e a única
forma de não enviar era desligar a integração automática, o que também parava a **captura**.

Duas forças faziam disso mais que um `if`:

1. **O JSON que interessa é o payload de destino**, e ele só existe dentro do adapter de saída, uma linha antes do POST.
   Gravar só a foto do domínio não responde "o que a plataforma vai receber". Então não dava para o pipeline simplesmente
   não chamar o adapter.
2. **A montagem do payload não é ponto único.** O `DocumentPipeline.ProcessAsync` é o único chamador do despacho — as
   quatro origens (automática, manual, agendada, reprocesso) convergem nele —, mas o payload vive em cada adapter de
   saída. Se a regra descesse para lá, ela viraria "cada adapter que se lembre de olhar a chave", e o esquecimento de um
   adapter novo só apareceria em produção, com nota fiscal.

## Decisão

**O envio vira dois passos no contrato, e a chave é do tenant, numa coluna própria.**

O `IComplianceDispatcher<TDocument>` deixa de ter `SubmitAsync` e passa a ter:

```
Task<PreparedDispatch<TDocument>> PrepareAsync(TDocument document, DispatchContext context, ct)
Task<IntegrationReceipt>          SendAsync(PreparedDispatch<TDocument> prepared, DispatchContext context, ct)
```

- **`PrepareAsync`** faz tudo o que antecede o POST: lê a configuração do tenant, resolve os estabelecimentos, mapeia o
  documento e **grava a foto do payload de destino**. As recusas permanentes que se descobrem antes do envio saem daqui.
- **`SendAsync`** faz o POST e classifica a resposta. **Só ele** envia.
- **O pipeline** chama `PrepareAsync` sempre, lê a chave, e chama `SendAsync` só se ela estiver ligada. Desligada, grava
  o estado **"Salva"** com o motivo e termina.
- **O adapter nunca recebe a chave.** Nem por parâmetro, nem por contexto, nem por injeção.

A chave mora em **`ConnectorProfiles.OutboundDispatchEnabled`** (`bit NOT NULL DEFAULT 1`), é **do tenant** e
**independente de conector**, e chega à tela por **duas** respostas: o `GET /connector`, só do Admin, que desenha e grava
o formulário; e o `GET /info`, de qualquer papel, porque o botão de reprocessar está numa tela que todo mundo abre e o
perfil é 403 para o Viewer.

### O que este desenho garante, e o que não garante

Escrito literalmente porque é o ponto que precisa ser conferido, não presumido.

**Garante:**

- **Um adapter novo não consegue ignorar a chave por esquecimento, porque não há nada para lembrar.** A chave não chega
  ao adapter. A decisão é tomada num lugar só, e esse lugar é o mesmo por onde as quatro origens passam.
- **Não dá para enviar sem ter preparado e fotografado.** `SendAsync` só aceita um `PreparedDispatch`, que só
  `PrepareAsync` produz. **De fora do assembly do adapter isso é absoluto:** o tipo é abstrato e a subclasse concreta de
  cada adapter é `internal`, então a esteira não tem como fabricar um.
- **Um adapter novo não compila com a forma antiga.** É quebra de interface, e `TreatWarningsAsErrors` não deixa passar
  de raspão: ninguém herda silenciosamente o comportamento errado.

**NÃO garante — e isto é mitigado, não garantido:**

- **Nada impede que um adapter novo faça o POST dentro do `PrepareAsync`.** Nenhum tipo, nenhuma regra de compilação e
  nenhum guard de startup detecta isso: é um `HttpClient` chamado no método errado, e o compilador não distingue "montar"
  de "postar".
- **De dentro do assembly do adapter, a ordem também não é garantida.** Quem mexe no próprio adapter pode montar um
  `AvalaraPreparedDispatch` na mão e chamar o `SendAsync` com ele, pulando a foto — `internal` é visível para o adapter
  inteiro. É a **mesma classe de violação** do POST no método errado, com a mesma mitigação: nome, documentação do
  contrato e revisão de código. A suíte de conformidade **não** alcança este caso: ela exercita o adapter pela porta, e
  um preparado montado na mão é código que ninguém chamou.
- O que muda é a **natureza da falha**: deixa de ser um **esquecimento** (não escrevi o `if` que ninguém me lembrou de
  escrever) e passa a ser uma **violação de contrato** (escrevi o envio no método que diz, no nome e na documentação, que
  não envia). A segunda é visível em revisão e cabe num teste; a primeira não aparecia em lugar nenhum.
- **A mitigação é um teste de conformidade, e ela própria é esquecível.** A
  `ComplianceDispatcherContractTests<TDocument>` roda `PrepareAsync` contra uma plataforma falsa e **falha se qualquer
  requisição sair**. Ela nasce **dentro** do projeto de teste da Avalara, porque não há projeto de teste compartilhado
  entre adapters e a Avalara é o único adapter de saída. Um segundo adapter de saída precisará de alguém que se lembre de
  promovê-la a biblioteca comum **e** de herdá-la; nada no build obriga nenhuma das duas coisas (o `AdapterDeclarationGuard`
  é guarda de *startup* sobre declarações registradas, e não alcança comportamento de método). Está nos Próximos passos
  do `docs/STATUS.md` por isso.

**Veredito honesto:** o desenho elimina a classe de falha "esqueci de olhar a chave" por construção, e reduz a classe
"postei no lugar errado" a uma violação de contrato nomeada, detectável em revisão e coberta por um teste que o próximo
adapter precisa lembrar de herdar. Não é garantia total.

### As recusas de autenticação não aparecem com a chave desligada

A chave é lida **depois** do `PrepareAsync`, de propósito: é isso que faz "a chave desligada não esconde rejeição" cair de
graça. Validação, contrato do destino, estabelecimento e configuração que a **preparação** precisa continuam produzindo
os desfechos de hoje, com a chave em qualquer posição; só a nota que chegaria ao POST vira "Salva".

**Menos quatro recusas, e isso é consequência de onde o token é pedido.** O `ApplyAuthAsync` está uma linha antes do POST,
no `SendAsync`, então a conta de plataforma ausente, a credencial ausente, o endpoint de token ausente e o valor do
segredo ausente no cofre **não são procurados** com a chave desligada: a nota fica em "Salva".

Aceitamos isso porque as quatro são condições **do tenant, e não da nota**: credencial incompleta reprova 100% das notas,
não metade, então rejeitar nota a nota não acrescenta informação — só ruído com o nome de rejeição. O risco que a decisão
original evitava (um mar de notas "Salva" sem pista de que parte delas não passaria) é recusa de **conteúdo**, e essa
continua saindo na preparação, nota a nota. Quem responde "a credencial deste tenant presta?" é o **"Testar credencial"**
de Configurações → Conectores, no nível certo — o da conta de plataforma —, com o mesmo motivo que a rejeição do envio
traria e sem fazer requisição nenhuma.

### "Salva" é terminal para a idempotência por conteúdo

`AlreadyProcessedAsync` passa a tratar `Saved` como terminal, ao lado de `Submitted` e `Confirmed`. Sem isso, duas coisas
quebram — e as duas em silêncio: a nota em "Salva" seria reprocessada a cada passada do coletor dentro da janela de
sobreposição (ADR-0023), regravando as fotos sem nada ter mudado; e, ao religar a chave, a primeira passada com
sobreposição a **enviaria sozinha**, contra a decisão de que ligar o envio não reenvia o acumulado. O gatilho manual
continua furando (ADR-0016), e é por ele — e só por ele — que uma nota sai de "Salva".

**E isso tem uma consequência na tela que não é decorativa:** a nota em "Salva" **tem** botão de reprocessar, com a
chave ligada. A elegibilidade do reprocesso é um predicado próprio (`REPROCESSABLE_STATUSES` = as falhas **mais**
`Saved`), e **não** a lista de falhas (`FAILURE_STATUSES`): "Salva" não é falha — fica fora do filtro de falhas e da
contagem de erro, como a ignorada —, mas é a única nota cujo único caminho de saída é o clique. Unificar as duas listas
quebra de um lado ou do outro, em silêncio, e por isso elas ficam lado a lado no código, com o porquê escrito ali.

Com a chave **desligada**, o botão dessa nota — e o de qualquer nota que tenha reprocesso — continua **visível e
desativado**, com o motivo no próprio botão, alcançável por mouse, por teclado e por toque. A nota que nunca teve
reprocesso ("Finalizado", "Ignorado") não ganha botão nem explicação: a chave não indisponibilizou nada ali.

## Alternativas consideradas

- **A. Uma flag no `DispatchContext`, e cada adapter decide.** Rejeitada. É exatamente o "cada adapter que se lembre": um
  adapter que ignore o campo compila, roda e envia. A falha é silenciosa e só aparece com nota enviada que não devia.
- **B. O pipeline não chama o adapter quando a chave está desligada.** Rejeitada: sem chamar o adapter, o payload de
  destino nunca é montado, e o que fica gravado é só o modelo de domínio — que não responde "o que a plataforma vai
  receber", que é o uso que motivou a chave.
- **C. Isolar o envio num tipo que só o pipeline consegue construir** (o adapter devolve uma descrição de requisição, e
  quem executa o HTTP é um componente da Application). **Garantiria** o que o desenho escolhido só mitiga: o adapter
  deixaria de ter `HttpClient`. Rejeitada por custo e risco — hoje o `AvalaraComplianceDispatcher` lê a resposta uma vez,
  redige, fotografa e classifica num bloco só (ADR-0027), e a classificação depende de status, cabeçalhos e corpo. Mover
  o HTTP para fora desmonta essa unidade no caminho mais sensível do produto. **Fica registrada como o caminho se a
  garantia total virar requisito.**
- **D. Um analisador de Roslyn que proíba `HttpClient` dentro de `PrepareAsync`.** Rejeitada: infraestrutura nova de
  build para um adapter só, e contornável por indireção (um helper chamado de dentro do método). Dá a sensação de
  garantia sem a garantia.
- **E. A chave dentro de JSON de settings.** Rejeitada. O `poll.enabled` é das settings do adapter de **entrada**, de
  outro eixo, e a spec `automatic-integration` proíbe explicitamente um segundo lugar para aquele estado; as settings de
  conector são **por conector**, e esta chave é independente de conector. A migração `RemoveConnectorProfileRealtime`
  tirou um booleano do perfil, mas tirou porque o estado dele **já existia** noutro lugar que o coletor lia — o que não é
  o caso aqui: nenhum campo de hoje é lido pelo despacho. A coluna é a primeira e única fonte.

## Consequências

**Melhora:**

- O corte do envio é **estrutural**, e não uma lembrança que cada adapter novo precise ter.
- O payload de destino fica gravado mesmo sem envio, **pelo mesmo caminho que o grava hoje** — sem uma segunda rotina de
  montagem que pudesse divergir da de produção.
- "Salva" é situação própria na tela, com card e selo de grupo próprios: um estoque de notas paradas fica **visível** em
  vez de sumir no total ou mentir "Finalizado".
- O padrão é o comportamento de hoje: subir esta versão não muda nada para quem não mexer na chave.

**Piora, ou fica em aberto:**

- O `IComplianceDispatcher` tem **dois métodos onde tinha um**, e a ordem entre eles é contrato, não só convenção.
- A suíte de conformidade precisa ser **promovida e herdada** no dia do segundo adapter de saída, e nada no build obriga.
- Uma nota em "Salva" pode ficar esquecida para sempre: religar a chave não a reenvia, por decisão de produto (surpresa
  com nota fiscal é pior do que esperar um clique). O card "Salvas" existe justamente para isso não sumir de vista.
- Com a chave desligada, o hub **ainda faz requisição de leitura** à plataforma (a listagem de estabelecimentos, quando a
  nota não diz a emissão). Ler não é enviar, pela mesma razão que vale para a consulta de status — e sem ela não haveria
  payload para conferir.
- O selo do grupo **continua mentindo para "Ignorado"**: um grupo de 10 NFS-e ignoradas ainda diz "Finalizado" com tudo
  zerado. O conserto é estruturalmente o mesmo desta change e está nos Próximos passos, não feito aqui por decisão
  explícita de não alargar o escopo.

**Rollback:** a coluna é aditiva e tem default; voltar o código não exige voltar o banco. Um tenant que tenha desligado a
chave volta a enviar na versão anterior, e as notas em "Salva" ficam com um status que o código antigo não conhece — a
tela antiga as mostra com o rótulo cru (o `StatusChip` já tem fallback) e elas não entram em nenhuma das três faixas.
Degradação legível, não quebra.
