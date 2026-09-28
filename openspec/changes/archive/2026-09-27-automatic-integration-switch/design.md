## Context

A motivação está no proposal.md (Why). O estado do código, conferido:

- **O `Realtime` atravessa tudo e não controla nada.** É `required bool` no `TenantConnectorProfile`, parâmetro do
  `ConnectorProfileRequest` e do `ConnectorProfileView` e coluna `bit` do `ConnectorProfileRow`. O
  `ConnectorProfileService` o copia do pedido para o perfil. O `/info` (`Program.cs`) devolve
  `realtime = profile?.Realtime ?? false`, e o `App.tsx` lê `info?.realtime ?? true`, ou seja, enquanto o `/info`
  carrega, o selo diz "Tempo real ligado". Nenhum código de coleta o lê.
- **Quem liga o coletor é o `poll.enabled`.** O `ChangeFeedPoller`, a cada passada, lista os perfis pelo adapter de
  entrada (`ListByInboundAdapterAsync(_feed.Origin)`), direto no banco e sem cache, e faz o parse da seção `poll` com
  o `ChangeFeedPollSettings.Parse`:
  - `enabled: false` com a seção presente é desligado de propósito, e o poller fica calado;
  - a seção ausente é desligado por falta de configuração, e o poller lista o tenant em `PollNotConfigured`. O
    `ChangeFeedPollingService` avisa de hora em hora.
- **A tela já preserva a seção `poll`.** A `ConnectorsPage` guarda as `inboundSettings` inteiras em `inboundValues` e
  só troca os campos do schema do adapter, e o resto (empresas, `poll`) volta intacto no `PUT`. O `setPath` dela grava
  texto, e o `ChangeFeedPollSettings` lê `enabled` como `bool?`: um `"true"` em texto quebra o parse, e o tenant passa a
  registrar falha a cada intervalo.
- **O `PUT /connector` não valida a seção `poll`.** O `ConnectorProfileService.SaveAsync` confere só se as settings
  são objeto JSON e trata os campos de segredo.
- **Os schemas dos adapters moram no front** (`adapterSchemas.ts`), com o comentário de que, no modelo de produção,
  viriam do backend. O único `IDocumentChangeFeed` registrado é o do D365 (`AddD365ChangeFeed`, scoped, origem
  `Dynamics365`).
- **O seed discorda de si mesmo.** O tenant-a nasce com `Realtime = true` e `poll.enabled = false`; o tenant-b
  (iScala), com `Realtime = false` e sem seção `poll`.
- **O registro de publicação já zera no rebobinamento, mas só por regressão.** O `ChangeFeedPublicationLog.BeginPull`
  descarta os pares quando a marca que vai ser lida é menor que a última vista (`LastWatermark`). Há teste pela
  variante do `UPDATE` na marca. A do `DELETE` no cursor, que é a do RUNNING §6, não tem teste (D5 traz a hipótese
  da leitura, ainda não confirmada).
- **Não há projeto de teste do Host nem do dashboard.** O que precisa de teste mora na Application.
- **Os testes de Infrastructure criam o banco com `EnsureCreated`** (SQLite), e não pelas migrações. A migração só
  roda contra o SQL Server, quando o host sobe (`Migrate`).

## Goals / Non-Goals

**Goals:**

- Uma única gravação do estado do coletor (`poll.enabled`), e toda leitura derivada dela.
- O rebobinamento reproduzido por teste antes de corrigido, e corrigido pela causa que o teste mostrar (D5).
- Nenhuma porta nova e nenhum esquema novo de idempotência: o registro de publicação continua sendo o filtro de
  tráfego do ADR-0025, só com mais uma condição de descarte.

**Non-Goals:**

- Mudar o motor do poll: seleção, intervalo, lease e fencing ficam como estão, fora o descarte do D5.
- Interromper uma leitura em curso quando o poll é desligado (D7).
- Mover os schemas dos adapters para o backend (D3).
- Renomear o `RealTime` do modo de integração. Está registrado como item próprio (D12).

## Decisions

### D1. O `poll.enabled` é a única fonte, e o `Realtime` sai com a coluna

O `Realtime` sai do domínio do perfil, dos contratos de leitura e gravação, da linha do EF e do seed. Uma migração
remove a coluna.

**Alternativas descartadas:**

- **Manter o `Realtime` como espelho**, sincronizado na gravação pela tela. Foi descartada porque são dois lugares: a
  primeira edição por SQL, pelo roteiro do RUNNING, os separa, e a tela volta a dizer "ligado" com o coletor parado.
- **Deixar a coluna sem uso, sem migração.** Foi descartada porque coluna morta convida a reuso. E o pedido foi
  explícito: não pode sobrar um segundo lugar.

### D2. O interruptor grava dentro das `InboundSettings`, pelo mesmo `PUT`, sem campo novo

O interruptor é uma vista do `poll.enabled` que está nas `inboundValues` da tela:

- **Estado:** vem de `inboundValues.poll?.enabled === true`.
- **Mudança:** troca só o `enabled`, preservando o resto da seção.

  ```ts
  { ...inboundValues, poll: { ...asObj(inboundValues.poll), enabled: checked } }
  ```

- **Tipo:** o valor é **booleano JSON**, e não passa pelo `setPath`, que grava texto.
- **Envio:** o `PUT /connector` leva as settings como já leva hoje. O pedido perde o `realtime` e não ganha nada.

**Por que no formulário, e não gravando na hora.** A tela é um formulário com um "Salvar" só. Uma gravação imediata
do interruptor salvaria junto as outras edições pendentes, ou precisaria de um merge próprio do JSON no servidor.
Quando "ligar abre as opções" (intervalo, sobreposição), as opções também serão do formulário.

**Alternativas descartadas:**

- **Um campo `automaticIntegration` no pedido, aplicado pelo servidor no JSON.** Foi descartada porque, no mesmo
  pedido, haveria dois jeitos de gravar o mesmo valor (o campo e o `poll.enabled` das settings), com regra de
  precedência. É o "segundo lugar" dentro do mesmo corpo.
- **Um endpoint próprio, com gravação imediata** (`PUT /connector/automatic-integration`). Fica para quando a tela
  ganhar as opções do coletor, se o formulário não bastar.

### D3. "Varrer" é ter feed registrado. A tela marca o adapter no schema dele

- **No backend:** um adapter de entrada varre quando o host tem um `IDocumentChangeFeed` com aquela `Origin`. O `/info`
  resolve os feeds registrados (`IEnumerable<IDocumentChangeFeed>`) e usa as origens. É a mesma fonte que o poller
  consome, e não uma lista paralela. Construir o `D365ChangeFeed` só guarda as dependências e pede um `HttpClient` à
  factory, e o `/info` é lido uma vez por carga de tela e depois de salvar.
- **Na tela:** o adapter que varre é marcado no próprio schema, em `adapterSchemas.ts`, ao lado dos campos. O formato
  exato fica para a implementação: um `scans: true` na entrada ou um conjunto exportado. A tela já declara ali tudo o
  que sabe de cada adapter, e a troca de ERP no dropdown precisa da resposta antes de salvar.

**Alternativa descartada: o backend devolver a lista de adapters que varrem** (no `GET /connector`, por exemplo). É o
destino certo, junto com a mudança dos schemas para o backend. Fazer só esta parte agora deixaria metade do schema de
cada lado. O risco da divergência está nos riscos.

### D4. A derivação do estado mora na Application, e o `/info` só compõe

- **Uma função pura na Application (`Inbound`):** recebe o perfil e as origens que varrem, e diz se a integração
  automática está ligada. A regra é "adapter varre e `poll.enabled`".
- **A leitura que não lança:** a função usa uma leitura do `ChangeFeedPollSettings` que não lança (`TryParse`, ou
  equivalente). Settings ilegíveis contam como desligado. O poller continua usando o `Parse`, que lança e vira a falha
  registrada do tenant.
- **O `/info`:** passa a devolver `{ environment, automaticIntegration }` e continua aberto a qualquer usuário
  autenticado. Ele devolve só o booleano, sem nada das settings.
- **Os testes:** ficam na Application, porque não há projeto de teste do Host.

### D5. Rebobinamento: a hipótese primeiro, a regra depois

**Resultado das tarefas 1.2 e 1.3: hipótese confirmada na lógica do poller.** Os dois testes rodaram contra o código
de antes da correção, em `ChangeFeedPollerTests`, com o cursor falso recusando o avanço sem a linha, como o SQL:

- **`Cursor_deleted_between_passes_republishes_from_startFrom` passou.** O `DELETE` entre passadas já funcionava, como
  a leitura previa.
- **`Cursor_deleted_mid_first_pass_republishes_that_page_from_startFrom` falhou.** O que o teste mostrou:
  - a primeira passada terminou sem marca e com "lease perdido";
  - a segunda partiu do `startFrom` e suprimiu o A, em vez de enfileirá-lo.

O que isso não prova: que foi esse o caminho do sintoma visto no dev. Ele o explica se o `DELETE` caiu durante a
primeira passada, que no dev é longa. O `DELETE` entre passadas, que é o do critério de saída, é provado no manual (6.6).
A spec delta fica como está.

**O texto abaixo é o de antes dos testes, mantido como registro.**

**Isto é hipótese, e não fato.** A causa abaixo saiu da leitura do código e nunca foi observada. O sintoma foi visto
(a passada reporta tudo como "suprimida"), mas o caminho que o produz não foi reproduzido. As tarefas 1.2 e 1.3
decidem. Até lá, é provisória a parte da spec delta que depende da causa, no requisito "Supressão de par já
publicado": o segundo caso do bullet "Rebobinamento" (cursor ausente ou sem marca) e o cenário "Cursor sem marca". Essa
parte só se fixa depois das duas tarefas.

Os cenários "Cursor apagado entre passadas" e "Cursor apagado no meio de uma passada" descrevem o comportamento que o
critério de saída exige, qualquer que seja a causa, e ficam em todos os resultados.

**O que o código mostra.** O `BeginPull` só zera o registro quando a marca lida é **menor** que a última vista, e há
teste para a variante do `UPDATE` na marca. A do `DELETE` no cursor, que é a do RUNNING §6, não tem teste.

**A hipótese.** A leitura achou um caminho em que o `DELETE` pode escapar da regra da regressão:

1. **A primeira passada começa.** Ela parte do `startFrom`, e o `BeginPull` anota `LastWatermark = startFrom`.
2. **O operador apaga o cursor.** Isso aconteceria enquanto a primeira página ainda é lida. No dev, a primeira
   passada é longa: token do Azure CLI, 5 páginas e 69 avisos de modelo fora do mapa.
3. **A primeira página é enfileirada e registrada.**
4. **O avanço da marca não acha a linha.** O `TryAdvanceWatermarkAsync` atualiza zero linhas e devolve `false`, e o
   poller trata isso como lease perdido. O `Advanced` não roda, e a `LastWatermark` fica igual ao `startFrom`.
5. **A passada seguinte não vê regressão.** O `StartAsync` recria o cursor no `startFrom`, e o `BeginPull` compara o
   `startFrom` com a `LastWatermark`. Como são iguais, o registro fica, e a primeira página sai como "suprimida".

**O `DELETE` entre passadas não deveria escapar.** A `LastWatermark` já seria o relógio do F&O, bem acima do
`startFrom`. Além disso, a partir da segunda passada, a poda (`carimbo ≤ marca − sobreposição`) já teria tirado do
registro os pares antigos. Se a hipótese estiver certa, só o 1.3 falha antes da correção.

**O que cada resultado decide.** O cursor falso da tarefa 1.1 recusa o avanço sem a linha, como o SQL: um resultado no
falso vale como resultado sobre a lógica do poller.

| 1.2 (entre passadas) | 1.3 (meio da passada) | Leitura | O grupo 1 |
|---|---|---|---|
| passa | falha | A hipótese se confirma. | Entra a regra abaixo. A spec fica como está, e o STATUS registra o caminho. |
| falha | passa ou falha | Há outra causa, pelo menos para o `DELETE` entre passadas. | O grupo para antes de implementar. A causa é investigada, este D5 é reescrito com ela, e a spec e as tarefas são ajustadas pelo `/opsx:update`. A correção passa a ser a dessa causa. A regra do cursor ausente só fica se for ela que corrige o que se achou, e o D5 diz por quê. |
| passa | passa | A hipótese é desmentida: a lógica do poller não reproduz o sintoma. | Nenhuma regra nova entra. O sintoma é reproduzido contra o store SQL: um teste de Infrastructure com o poller sobre o `SqlChangeFeedCursorStore` e, se preciso, o roteiro manual do grupo 6. O que se achar volta a este D5, pelo `/opsx:update`. Se nada reproduzir, o item do STATUS fica aberto, com o que foi tentado, e saem da spec o caso do cursor ausente e o cenário "Cursor sem marca". |

Em nenhum resultado a regra entra "por via das dúvidas": filtro de tráfego a mais é código sem motivo, e esconderia a
causa real.

**A regra, se a hipótese se confirmar.** Sob o lease, o poller já relê o cursor para conferir o intervalo. Se o cursor
não existe, ou existe com `Watermark` nula, o poller chama um descarte explícito do registro daquele (tenant, origem)
**antes** do `StartAsync` e do `BeginPull`. A regra da regressão fica como está, e cobre o `UPDATE` na marca.

**Alternativas descartadas para a regra:**

- **Comparar com `<=` no `BeginPull`.** Foi descartada porque a marca igual à última vista é o caso normal de uma
  passada sem avanço, como uma página vazia sem `Date` ou o teto de páginas. Zerar aí despejaria as repetições da
  sobreposição a cada passada, e o filtro deixaria de existir.
- **O `StartAsync` dizer se criou ou preencheu a marca.** É mais exato: fecha a janela de milissegundos entre a
  releitura sob o lease e o `StartAsync`. Mas muda a porta, o store SQL e os fakes para cobrir um `DELETE` que caia
  exatamente nesse intervalo **e** numa marca igual à última vista. Fica registrado nos riscos.
- **Um endpoint ou comando para limpar o registro.** Foi descartada porque é passo manual, e o pedido é que a
  correção seja automática.

### D6. Desligar é pausa. Ligar pela primeira vez não varre o histórico

- **Desligar:** mexe só nas settings. O cursor, a marca e o registro de publicação ficam.
- **Religar:** retoma da marca, e o que mudou nesse meio-tempo é lido. O teto de páginas por passada (20) limita a
  recuperação, como em qualquer atraso.
- **Ligar pela primeira vez:** vale a regra de hoje. Sem `startFrom`, a marca nasce no instante atual, e o histórico
  fica de fora. Um backfill continua sendo `startFrom` por SQL, fora desta fatia.
- **A frase sob o interruptor:** diz isso em uma linha. A redação é da implementação. Sugestão: "Busca sozinha as
  notas novas no ERP. Desligada, a busca pausa e, religada, retoma de onde parou."

### D7. O efeito vale na passada seguinte. A leitura em curso termina

- **Onde o poller confere:** o `poll.enabled` é conferido no começo do poll de cada tenant, a cada passada, lido do
  banco sem cache. O tick é de 15 segundos. Nada precisa avisar o poller: o perfil gravado já é o que a passada
  seguinte lê.
- **A leitura em curso termina.** Ela não confere o `enabled` entre páginas. O motivo é que a leitura já tem limite:
  20 páginas e o lease de 2 minutos. Interrompê-la no meio mexeria na regra "página inteira enfileirada antes de
  avançar".
- **Consequência:** num backfill grande em curso, o "parar em até 15 segundos" pode passar disso. A spec diz
  explicitamente que a leitura em curso termina.

### D8. A gravação recusa o valor da seção `poll` que ela está escrevendo e que o coletor não leria

**Decisão: a guarda julga o que o `PUT` escreve, e não o documento inteiro depois do merge.** O
`ConnectorProfileService.SaveAsync` compara a seção `poll` do pedido com a gravada e julga só os campos que o pedido
introduz ou muda.

- **Julgado:** o campo presente no `poll` do pedido que não existe no gravado, ou que difere dele (igualdade de JSON).
  Um `poll` que não é objeto e difere do gravado também é julgado, e é recusado.
- **Não julgado:**
  - o campo que volta igual ao gravado;
  - o campo ausente do pedido, que não está sendo escrito.
- **Contra o quê se compara:** contra as settings gravadas do mesmo adapter, pela mesma regra que já mantém as
  referências de segredo (`StoredFor`). Sem perfil gravado, ou com outro adapter, todo campo da seção é novo e é
  julgado.
- **Como se julga:** os campos julgados passam pelo mesmo `ChangeFeedPollSettings.Parse` do poller, numa seção montada
  só com eles. Os tipos e os limites são os do coletor, e não uma segunda cópia deles. As regras da seção são por campo,
  e não há regra que cruze dois campos.
- **Na recusa:** devolve `Invalid`, e nada é gravado, nem no cofre nem no perfil. A mensagem nomeia o campo, o que veio
  e a forma aceita, por exemplo: "InboundSettings.poll.enabled precisa ser verdadeiro ou falso (veio texto)." Os valores
  da seção `poll` não são segredo, e a mensagem pode citá-los.
- **Onde vale:** para qualquer adapter de entrada, porque o contrato da seção é o mesmo para qualquer origem
  (ADR-0019).
- **Sem seção:** continua válido.

**Por que a guarda existe.** O D2 põe a tela para gravar um booleano dentro de um JSON, e não há teste de dashboard. Sem
a guarda, um `"true"` em texto viraria falha registrada a cada intervalo, e não "ligado". Com ela, o erro aparece no
"Salvar", onde quem errou está olhando.

**Por que só o que o pedido escreve.** A tela devolve a seção `poll` gravada inteira (D2), e o Admin só mexe no
`enabled`: intervalo, sobreposição e `startFrom` não têm tela. Um valor inválido gravado por SQL, como
`overlapSeconds = 0`, voltaria em todo `PUT`.

- **Se o documento inteiro fosse julgado:** o Admin ficaria trancado fora da tela inteira, inclusive para trocar um
  Client Secret, por causa de um campo que ele não vê e não consegue editar.
- **Julgando só o que o pedido escreve:**
  - o Admin salva o resto, e o campo quebrado fica como estava;
  - o coletor continua registrando a falha, com o nome do campo, no log e no `LastError` do cursor;
  - o `/info` continua dizendo "desligada", porque não consegue ler a seção (D4).

**Alternativas descartadas:**

- **Julgar o documento inteiro depois do merge, com uma mensagem dizendo o que fazer.** Foi descartada porque a única
  mensagem honesta seria "peça à operação para corrigir por SQL". Isso é trancar o Admin com outra redação: o Admin do
  cliente não tem acesso ao banco. E recusar não conserta nada, porque o perfil já estava ilegível antes do "Salvar".
- **Confiar na tela.** Foi descartada porque não há como provar a tela por teste neste repositório.

### D9. Selo e carregamento

- **O selo:** o `App.tsx` passa a ler `info?.automaticIntegration ?? false`, e o selo passa a dizer "Integração
  automática ligada".
- **Depois de salvar:** a `ConnectorsPage` já invalida a query `['info']`, e o selo acompanha sem recarregar.
- **O que o selo não é:** não é saúde do coletor. Ligado com falha continua "ligado", e a falha está no log e no cursor.

### D10. Migração

- **Como se gera:** `dotnet ef migrations add RemoveConnectorProfileRealtime`, com a remoção do campo no mesmo commit.
  O modelo do EF e o snapshot têm de bater.
- **`Up`:** remove a coluna.
- **`Down`:** recria a coluna `bit NOT NULL DEFAULT 0`. O valor antigo se perde, e isso não importa, porque ele não
  controlava nada.
- **Como se prova:** os testes criam o banco por `EnsureCreated`, então a migração é provada subindo o host contra o
  SQL Server do `docker compose`, que aplica o `Migrate`.

### D11. ADR-0029

`docs/adr/0029-integracao-automatica-e-o-poll.md`, pelo template. O ADR registra:

- o `poll.enabled` como fonte única;
- a saída do flag de tempo real do perfil;
- "varrer" como ter feed registrado (D3), com a marca provisória no schema do front;
- o desligar como pausa (D6).

O cabeçalho diz "Revisa: ADR-0019", e o ADR-0019 ganha a nota de revisão no formato das anteriores, no ponto em que
lista o `realtime` entre os campos comuns tipados.

### D12. O "Tempo real" da lista de grupos fica, registrado como item

A palavra é a mesma, mas os conceitos são diferentes:

- **O `RealTime` do modo de integração diz como o documento entrou.** É o gatilho gravado no documento processado
  (`ProcessedDocument.Trigger`), servido pelo `IDocumentQueries` e rotulado "Tempo real" na `GroupsPage`. Ao lado dele
  estão `Manual`, `ScheduledDaily` e `ScheduledOnce`. Referência sem modo cai em `RealTime` (`SqlProcessingStore`,
  "sem modo = chegou por evento"), e é assim que ficam marcadas as notas que vieram do feed.
- **O interruptor diz se o conector do tenant roda sozinho, daqui para a frente.**

Um não implica o outro. Uma nota pode ter entrado em "tempo real" com a integração automática hoje desligada, e um
tenant pode estar com ela ligada sem nenhuma nota nova.

**Por que fica fora.** Renomear o valor mexe em contrato:

- o valor gravado na coluna `Trigger` dos documentos já processados;
- o modelo servido pelo `IDocumentQueries`;
- o rótulo e o valor padrão da `GroupsPage`.

Por isso vai para outra fatia, mas fica escrito. A tarefa 5.4 abre o item no STATUS com esta distinção, para a fatia
que renomear não confundir os dois.

## Risks / Trade-offs

- **[A marca do schema no front diverge do feed registrado no back]** → Hoje há um só adapter que varre. O ADR registra
  que a marca é provisória, até os schemas irem para o backend. Um adapter novo que varre já exige mexer no front,
  para declarar os campos dele.
- **[Se a regra do D5 entrar: o `DELETE` cai entre a releitura sob o lease e o `StartAsync`, numa marca igual à última
  vista]** → É uma janela de milissegundos, e o efeito é só repetição suprimida numa passada. A saída exata (o
  `StartAsync` dizer que semeou) está no D5, para quando valer o custo.
- **[O `DELETE` no meio da passada loga "lease perdido"]** → Isso vale em qualquer resultado do D5. O
  `TryAdvanceWatermarkAsync` devolve `false` tanto para o lease perdido quanto para o cursor sumido, e o log culpa o
  lease. É inofensivo, porque não conta falha. Fica anotado no RUNNING §6, junto do rebobinamento. Separar os dois
  casos muda a porta e fica fora.
- **[Campo da seção `poll` quebrado por SQL: o interruptor mostra o `enabled` gravado, e o selo mostra "desligada"]** →
  A tela não tem como consertar um campo sem tela, e o D8 não a tranca por causa dele. Onde o campo aparece:
  - no log do coletor e no `LastError` do cursor;
  - no RUNNING §6, junto do SQL.

  Quando as opções do coletor ganharem tela, o campo quebrado passa a ser editável ali.
- **[Contrato do front muda, e o frontend é único para todos os clientes (ADR-0020)]** → Não há cliente em produção, e
  front e back sobem juntos. Com cliente, uma troca assim pediria compatibilidade: devolver os dois campos por uma
  versão.
- **[Backfill em curso segue depois de desligar]** → A leitura tem limite de 20 páginas por passada, e a spec diz que
  a leitura em curso termina (D7).
- **[Perfis existentes com `Realtime = true` e poll desligado passam a mostrar "desligada"]** → É a correção, e não
  uma regressão: a tela passa a dizer o que o coletor faz.
- **[O selo "ligada" com o coletor falhando]** → Está fora do escopo (proposal, Non-goals). O selo diz "configurada para
  rodar".

## Migration Plan

1. **Deploy:** a migração roda na subida do host (`Migrate`). Front e back sobem na mesma versão.
2. **Dev:** o seed do tenant-a continua com `poll.enabled = false`. Depois da fatia, a barra lateral não mostra o selo,
   que é o estado real. Liga-se pela tela.
3. **Rollback:** reverter o commit e aplicar o `Down`, que recria a coluna com `false`. O `poll.enabled` gravado pela
   tela continua valendo, porque era ele que mandava antes também.

## Open Questions

- **A frase sob o interruptor.** A sugestão está no D6. A redação final pode mudar na revisão da tela, sem mexer na
  spec.
