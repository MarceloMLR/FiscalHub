## Why

Em alguns casos o cliente não quer que a nota vá para a plataforma de compliance: quer **só o JSON
gravado**, para olhar, validar e conferir antes de mandar. Hoje o hub não sabe fazer isso — captura e
envia são o mesmo movimento, e a única forma de não enviar é desligar a integração automática, o que
também para a **captura**. Falta a chave que separa as duas coisas: capturar sempre, enviar quando o
tenant mandar.

## What Changes

- **Uma chave em Configurações, do tenant e independente de conector:** "Enviar ao conector de saída".
  Ligada (o padrão, e o comportamento de hoje) = salva e envia. Desligada = salva e **não envia**.
- **Uma situação própria, "Salva":** a nota que parou na chave não é "Finalizado" nem "Em processamento".
  "Em processamento" quer dizer *aguardando retorno do compliance*, e uma nota que nunca foi enviada não
  aguarda nada.
- **O JSON gravado é o payload de destino** — o que iria para a Avalara, o mesmo que o botão "Visualizar
  JSON" já mostra —, e não só o modelo de domínio interno. Conferir sobre o modelo interno não responde
  "o que a plataforma vai receber".
- **O corte vira contrato, e não boa vontade de cada adapter.** O `IComplianceDispatcher` passa a ter dois
  passos — preparar (traduzir, fotografar, **sem** postar) e enviar. Quem lê a chave e decide é o pipeline;
  o adapter não vê a chave e não tem o que esquecer. O que esse desenho garante e o que não garante está
  escrito, com todas as letras, no `design.md` §1 — é a decisão estrutural desta change.
- **Com a chave desligada, o reprocesso fica indisponível para QUALQUER nota** — não só as que pararam em
  "Salva" — e independente da origem (automática, manual ou agendada). A chave é uma porta única para toda a
  saída: o clique manual não passa por cima dela. Na nota que **tem** reprocesso, indisponível é o botão
  **visível e desativado, com o motivo nele** (alcançável por mouse, teclado e toque); na que nunca teve
  botão, não há botão nem explicação. Ver `design.md` §7.
- **Um quarto card "Salvas"** na tela principal, com contagem própria na API (`DocumentGroup` e
  `ModelTotals`) e um estado novo no selo do grupo — que hoje cairia em "Finalizado" com as três faixas
  zeradas.
- **"Salva" passa a ser terminal para a idempotência por conteúdo** (ADR-0016), com o reprocesso manual
  furando, como sempre.
- **BREAKING (interno):** o `IComplianceDispatcher<TDocument>.SubmitAsync` é substituído por
  `PrepareAsync` + `SendAsync`. A porta é interna ao projeto; não há consumidor externo. O `AvalaraComplianceDispatcher`
  é o único implementador hoje.

## Decisões tomadas nesta proposta

Decisões que decorrem do que foi pedido e que valem a leitura antes da implementação — são o tipo de
coisa que, errada, só aparece em produção.

### A consulta de status continua rodando com o envio desligado

**Decisão:** desligar a chave MUST NOT parar o `StatusPoller`.

**Por quê:** uma nota despachada **antes** de a chave ser desligada está esperando resposta da plataforma.
Se o poll parar, ela fica em "Em processamento" para sempre — exatamente a mentira de tela que as changes
`dashboard-info-failure` e `connector-navigation-and-admin-json` existiram para apagar. **Consultar o
status do que já foi enviado não é enviar.** A implementação acompanha: o poll chama `CheckStatusAsync`,
que é outro caminho — o corte fica em `SendAsync`, e não encosta nele.

### Religar a chave não dispara envio automático do que está em "Salva"

**Decisão:** ligar a chave MUST NOT reenviar, por si, nenhuma nota em "Salva". Elas esperam o clique no
reprocessar.

**Por quê:** mandar um lote acumulado no instante em que alguém vira uma chave é surpresa, e surpresa com
nota fiscal é pior ainda.

### "Salva" é terminal para a idempotência por conteúdo

**Decisão:** `AlreadyProcessedAsync` passa a tratar `Saved` como estado terminal, ao lado de `Submitted` e
`Confirmed`. O gatilho manual continua furando (ADR-0016), que é como o reprocesso envia.

**Por quê:** sem isso, duas coisas quebram. Primeiro, a nota em "Salva" não é terminal, então **cada
passada do coletor dentro da janela de sobreposição a reprocessaria**, regravando as fotos de domínio e de
destino sem nada ter mudado. Segundo, e pior: **ao religar a chave, a primeira passada com sobreposição a
enviaria sozinha** — contradizendo a decisão de cima. Fazer "Salva" terminal resolve as duas, e mantém o
reprocesso manual como o único caminho de saída dessas notas, que é o que foi pedido.

### A chave desligada não esconde rejeição

**Decisão:** validação, roteamento e contrato do destino continuam valendo com a chave desligada. Uma nota
inválida vira `IntegrationError`; uma NFS-e vira `Ignored`; uma que o contrato do destino não representa
vira `IntegrationError` com a lista de campos. Só a nota que **chegaria ao POST** vira "Salva".

**Por quê:** o uso que motivou a chave é "olhar, validar e conferir". Uma chave que silenciasse as recusas
entregaria o contrário: um mar de notas "Salva" e nenhuma pista de que metade delas não passaria.

### O selo do grupo conserta "Salva", e deixa "Ignorado" como estava

O selo do grupo (`GroupStatusChip`) deriva das contagens: erro → "Com pendências", processamento →
"Processando", senão → "Finalizado". Com um estado novo para "Salvas", um grupo de notas salvas deixa de
mentir.

**A ponta que sobra:** o mesmo defeito já existe hoje com **"Ignorado"** — um grupo de 10 NFS-e ignoradas
mostra "Finalizado" com 10 Documentos e 0 em toda parte. Esta change **não** o conserta: `Ignored` não
ganha contagem própria nem estado no selo. O conserto é estruturalmente o mesmo (uma contagem `ignoradas`
em `DocumentGroup`/`ModelTotals`, um estado no selo, e a decisão de produto sobre um quinto card), e fica
registrado em **Próximos passos** em vez de ampliar o escopo desta fatia.

## Capabilities

### New Capabilities
- `outbound-dispatch-switch`: a chave de envio do tenant — onde mora, por qual resposta chega à tela, onde
  corta a saída, a situação "Salva", o que acontece com a nota em voo e com a nota salva quando a chave
  vira, e a indisponibilidade do reprocesso com o aviso que a explica.

### Modified Capabilities
- `document-grouping`: o requisito "Os cards e a tabela contam pela data da execução" enumera o que cada
  card conta. Ganha o quarto card, "Salvas", e a regra de que a nota salva não conta em finalizadas, em
  processamento nem com erro.

## Impact

**Onde a chave mora.** No perfil de conector do tenant (`ConnectorProfiles`), como **coluna própria**, e
não dentro de JSON de settings. O perfil já é por tenant e já é o lugar da configuração de integração. Os
dois candidatos óbvios não servem: `poll.enabled` é das settings do **adapter de entrada** (e a spec
`automatic-integration` proíbe explicitamente um segundo lugar para aquele estado — é outra chave, de outro
eixo), e as settings de conector são **por conector**, enquanto esta é independente de conector. Há um
precedente a observar e responder: a migração `RemoveConnectorProfileRealtime` tirou um booleano do perfil —
mas tirou porque aquele estado **já existia** noutro lugar que o coletor lia, o que não é o caso aqui;
nenhum lugar existente é lido pelo despacho.

**Por qual resposta ela chega à tela:** pelas **duas**, e por razões diferentes. O `GET /connector` é a
leitura do perfil, só do Admin, que desenha o formulário de Configurações — é por ele que a chave é lida e
gravada. O `GET /info` é o que **qualquer papel** lê para a tela dizer a verdade, e é o que o detalhe do
documento já consulta (`useInfo`): o botão de reprocessar está numa tela de qualquer papel, e sem a chave no
`/info` o Viewer não teria como saber que o botão deve estar desativado. É a mesma divisão que o
`automaticIntegration` já segue.

**Onde o envio é cortado — existe ponto único.** `DocumentPipeline.ProcessAsync` é o **único** chamador de
`IComplianceDispatcher.SubmitAsync` em todo o repositório. As três origens convergem nele: a automática
(coletor → fila de descoberta), a manual e a agendada (`IntegrationRunner` → a mesma fila) e o reprocesso
(`DocumentReprocess` → a mesma fila), todas por `QueuedDocumentProcessor` → `DocumentRouter` →
`DocumentPipeline`. **A regra tem um lugar só para ser respeitada.** O que *não* é ponto único é a
montagem do payload de destino, que vive dentro de cada adapter de saída — e é por isso que o corte precisa
virar contrato (design §1).

**O vocabulário de status — todos os leitores levantados.** `IntegrationStatus` ganha `Saved`. Quem lê:

| Leitor | Muda? | O quê |
|---|---|---|
| `IntegrationStatus` (Application/Outbound) | **sim** | o valor novo |
| `SqlProcessingStore.AlreadyProcessedAsync` | **sim** | `Saved` entra nos terminais |
| `SqlProcessingStore` (gravação) | **sim** | `RecordSavedAsync`, preservando o `ExternalId` como o `Ignored` faz |
| `SqlDocumentQueries` (faixas dos cards e dos grupos) | **sim** | faixa nova `Saved`, fora de finalizadas/processamento/erro |
| `StatusPoller` | **não** | só lista `Submitted` com `ExternalId`; nota salva não tem id externo |
| `DocumentRouter` | **não** | roteia por tipo, não por status |
| `AvalaraComplianceDispatcher` (tradução de status nativo) | **não** | `Saved` nunca vem da plataforma |
| `dashboard/src/types.ts` | **sim** | o literal `'Saved'` e os campos de contagem |
| `StatusChip.tsx` (rótulo e tom) | **sim** | "Salva"; **fora** de `FAILURE_STATUSES` |
| `DocumentDetail.tsx` | **sim** | o botão e o aviso; "Salva" não é falha nem aceita |
| `GroupStatusChip.tsx` | **sim** | o estado novo do selo |
| `cards.ts` / `GroupsPage.tsx` | **sim** | a soma e o quarto card |
| `GroupModal.tsx` | **não** | usa o `StatusChip`, que já traz o rótulo novo |
| `RewindMark.tsx` | **não** | cita status em texto, não no vocabulário |

**O que acontece com a nota em voo e com a nota salva.** Nota já despachada quando a chave é desligada:
continua sendo consultada e chega ao desfecho dela (`Confirmed`, `IntegrationError`, `Unconfirmed`) —
decisão de cima. Nota em "Salva" quando a chave é ligada: fica como está até alguém clicar em reprocessar —
decisão de cima.

**Código afetado.** A lista completa e nominal dos arquivos que a implementação vai tocar está em
`design.md`, seção "Arquivos que a implementação vai tocar", para conferência contra o trabalho em
andamento no worktree `FiscalHub-B`.

**Banco.** Uma migração, uma coluna, com padrão ligado — tenant existente não muda de comportamento.

**ADR.** A decisão estrutural (o despacho em dois passos) é de arquitetura e ganha um ADR novo.

## Non-goals

- **O defeito D1** (o poll de status que trava para todos os tenants): é change própria, está nos Próximos
  passos, e **não** se conserta de passagem mesmo encostando no `StatusPoller`.
- **O defeito D2 e o `DeadLetterTriggerService`:** estão sendo consertados noutro worktree
  (`FiscalHub-B`, branch `fix/dead-letter-poison`). Nenhum arquivo deles entra nesta change.
- **Consertar o selo do grupo para "Ignorado"** — registrado como pendência acima.
- **Uma chave por conector, ou por módulo:** esta é do tenant e vale para toda a saída. Se um dia houver
  mais de um conector de saída ligado, a granularidade é outra decisão.
- **Reenvio em lote** do que está em "Salva" (um botão "enviar todas"): não foi pedido, e a decisão de não
  surpreender aponta para o contrário.
- **AvaTax, Contábil e Inventário.**
- **Emitir, assinar, transmitir à SEFAZ ou calcular imposto** — fora do escopo do produto.
