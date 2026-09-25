## Context

A motivação está no proposal.md; os requisitos, em `specs/change-feed-polling` e `specs/d365-change-feed`.
Aqui fica só o estado atual que molda o desenho.

- **Descoberta existente é por período.** O `IDocumentDiscovery` serve o manual e o agendado pelo
  `IntegrationRunner`: recebe um período, devolve referências e não guarda estado. O único adapter é o
  `LocalDocumentDiscovery`, com catálogo fixo.
- **Padrão de laço de fundo.** O `IntegrationScheduler` é lógica pura com `TimeProvider`, e o
  `SchedulerHostedService` é só um `PeriodicTimer` que abre um escopo e chama a lógica. Uma falha num
  item não derruba os outros, e o estado que não avançou é retentado na próxima passada.
- **Ticks UTC.** O `ScheduledIntegrationRow.NextRunTicks` guarda `long` porque `DateTimeOffset` não
  traduz no `WHERE` do SQLite (ADR-0017). Os testes de store rodam em SQLite em memória.
- **Uma fila só.** `AddServiceBusDocumentQueue` registra um `IDocumentQueue` singleton para
  `documents-in`, e o consumidor chama a `DocumentPipeline<GoodsInvoice>`, cujo único
  `IInboundSource` é o `XmlGoodsInvoiceSource` (lê Blob pelo `Locator`). O emulador tem
  `MaxDeliveryCount = 5` e TTL de 1 hora.
- **Perfil de conector.** O `IConnectorProfileStore` só tem `GetAsync(tenantId)` e `UpsertAsync`; não
  lista. O seed de dev do tenant-a usa `InboundAdapter = "Dynamics365"`, com settings de placeholder.
  Não existe resolvedor de segredo: as settings guardam `kv:...`, e o Freshdesk anota "resolvida
  upstream".
- **Token.** O `IAvalaraTokenProvider` é `internal`: um provider real registrado como singleton
  substitui um no-op via `Replace` no DI (`AddAvalaraTokenProvider`).
- **F&O (documentação).** A OData do F&O pagina server-driven até 10.000 registros por página, aceita
  `$filter`, `$orderby`, `$select`, `$top` e `$skip`, não aceita `in`, e responde 429 com
  `Retry-After` (6.000 requisições a cada 300 segundos por usuário/app/servidor web).
- **F&O (verificado no fiscosysdev, 83 cabeçalhos).** Estes fatos são evidência e não precisam ser
  retestados.
  - Funcionam na `FSFiscalDocumentBRs`:
    - `$filter` com `SysModifiedDateTime gt <literal UTC>`, `dataAreaId eq 'brmf'`,
      `FiscalDocumentRecId gt <int64>` e o composto
      `(SysModifiedDateTime gt T) or (SysModifiedDateTime eq T and FiscalDocumentRecId gt R)`;
    - `$orderby=SysModifiedDateTime`, `$orderby=SysModifiedDateTime,FiscalDocumentRecId` e
      `$orderby=SysModifiedDateTime,dataAreaId,Voucher`;
    - `$select` dos 8 campos do D8;
    - header `Date` em toda resposta;
    - `Prefer: odata.maxpagesize=20` **honrado** (20 registros mais `@odata.nextLink`), com a varredura
      completa em 5 páginas de 20.
  - Não funcionam:
    - `Voucher gt 'X'`, porque string não aceita comparação relacional;
    - `Preference-Applied`, que volta vazio mesmo quando o `maxpagesize` é honrado.
  - O `@odata.nextLink` tem a forma `…&$orderby=SysModifiedDateTime&$select=…&$skip=20&$top=20`. É
    paginação por **offset**: não há `$skiptoken` nem snapshot.

## Goals / Non-Goals

**Goals:**

- As regras invioláveis do ADR-0023 (falha não avança, lease por tenant, sobreposição) vivem na
  lógica pura e são provadas por teste unitário, sem rede nem banco.
- O motor não conhece D365: outro ERP com consulta por janela entra escrevendo só um adapter.
- A marca d'água progride sempre. Nenhuma combinação de volume ou timestamps iguais pode travar um
  tenant repetindo a mesma página.
- Em regime, a repetição causada pela sobreposição é **limitada**, não infinita.
- `dotnet test` verde e sem rede em qualquer máquina; o teste real é opt-in.

**Non-Goals:**

- Minimizar a repetição até zero. A repetição limitada é o custo aceito da sobreposição, e o dedupe
  por conteúdo (ADR-0016) segue como garantia.
- Hospedagem fora do Host (Container Apps/Functions). A lógica já sai pronta para isso, mas o wiring
  desta fatia é in-process.

## Decisions

### D1. Porta `IDocumentChangeFeed` que entrega páginas assíncronas

```csharp
public interface IDocumentChangeFeed
{
    string Origin { get; }   // casa com TenantConnectorProfile.InboundAdapter ("Dynamics365")
    IAsyncEnumerable<ChangeFeedPage> PullAsync(string tenantId, DateTimeOffset since, CancellationToken ct = default);
}

public sealed record ChangeFeedPage(IReadOnlyList<DocumentReference> References, DateTimeOffset HighWatermark);

public sealed class ChangeFeedThrottledException(TimeSpan retryAfter, string message) : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
```

A página expõe "até onde está garantido" (`HighWatermark`). O worker enfileira e só então avança a
marca, página por página. Uma exceção no enumerador vale como falha, e a marca fica na última página
confirmada. `since` já chega com a sobreposição descontada pelo worker (D2).

**Alternativas.** (a) `PullAsync` devolvendo tudo de uma vez: memória sem teto numa marca rebobinada,
e uma falha persistente na página N impediria qualquer progresso. (b) Página + token de continuação
devolvido ao worker: vaza para a Application o mecanismo de continuação da origem (a âncora keyset do
D4), e o worker passa a guardar um cursor que não é dele. A paginação fica inteira dentro do adapter. (c) Estender o `IDocumentDiscovery`: descartado na decisão do pedido, porque a
semântica é outra (delta, estado, falha que não avança).

**Nome do worker.** `ChangeFeedPoller`, e não `D365PollWorker`: a classe só conhece a porta, mora na
Application ao lado do `StatusPoller` e do `IntegrationScheduler`, e o ADR-0023 registra que o mesmo
worker serve qualquer ERP com consulta por janela. O nome D365 fica no adapter
(`Ingress.D365Poll`).

### D2. Sobreposição e "falha não avança" no worker, não no adapter

O worker calcula `since = marca − sobreposição`, aplica `marca = max(marca, página.HighWatermark)` e
decide quando gravar. O adapter só traduz "mudou depois de X" em OData. As regras invioláveis ficam num
lugar só, testável com um feed falso, e valem para qualquer feed futuro.

### D3. Marca alta pelo relógio do lado F&O na última página

Se a marca fosse só o maior `SysModifiedDateTime` visto, num tenant parado ela congelaria no último
documento. Toda passada devolveria de novo tudo o que está em `(marca − sobreposição, marca]`. Numa
madrugada sem movimento, os últimos documentos do dia seriam reenfileirados a cada 60 segundos, cerca
de 700 vezes cada, e na fatia de montagem cada repetição custa quatro ou mais consultas ao F&O.

Por isso a última página de uma leitura completa (a página curta do D4) informa como marca alta
`max(maior SysModifiedDateTime, Date da primeira resposta)`. `Date` é o relógio do **web server** do
F&O que respondeu ao início da varredura. Como a varredura não tem limite superior e foi até o fim, tudo
o que tinha `SysModifiedDateTime ≤ Date` e já estava visível foi entregue. Gravação tardia com timestamp
anterior cai na sobreposição da próxima passada. Com sobreposição O e intervalo I, cada documento é
relido cerca de ⌈O/I⌉ + 1 vezes: 6 com os padrões de 300s/60s. Depois disso ele sai da janela.

A premissa "foi até o fim, então tudo foi entregue" depende da paginação por keyset do D4. Com o
offset do nextLink, uma linha pulada na fronteira de página quebraria essa premissa, e a marca saltaria
por cima dela.

**Precisão sobre os relógios.** O `Date` vem do relógio do web server do F&O. Ele não é,
necessariamente, o mesmo relógio que carimba o `ModifiedDateTime` na gravação. Os dois ficam na mesma
região e sincronizados por NTP, então a diferença é de milissegundos, contra 300 segundos de
sobreposição: irrelevante na prática, mas são dois relógios. A regra "a marca é nossa, o relógio é do
AOS" vale no sentido de que o valor gravado é nosso e sai de um relógio do lado F&O, nunca do relógio do
FiscalHub. A sobreposição absorve também essa diferença.

**Alternativas.** (a) Relógio do FiscalHub: mistura dois relógios, e a diferença entre eles vira buraco
ou repetição. (b) Guardar o conjunto de (chave, timestamp) já emitido dentro da janela e filtrar: zera a
repetição, mas é um quarto esquema de idempotência, que o CLAUDE.md proíbe. (c) Detecção de duplicata
nativa do Service Bus com `MessageId = tenant:chave:timestamp`: mesma objeção. Fica anotada para a
fatia de roteamento.

### D4. Paginação por keyset composto em (`SysModifiedDateTime`, `FiscalDocumentRecId`)

**Mecanismo.** Uma leitura é uma sequência de consultas independentes:

- toda página leva `$top=<pageSize>` e `$orderby=SysModifiedDateTime,FiscalDocumentRecId`;
- a primeira filtra `SysModifiedDateTime gt <since>`;
- a partir da segunda, o filtro ancora na última linha lida `(T, R)`:
  `(SysModifiedDateTime gt T) or (SysModifiedDateTime eq T and FiscalDocumentRecId gt R)`;
- quando há empresas, o filtro de empresas entra com `and` nas duas formas;
- o `$select` ganha `FiscalDocumentRecId`;
- a leitura termina na primeira página com **menos** linhas que `pageSize`. Uma página cheia seguida
  de uma vazia também termina.

O adapter ignora qualquer `@odata.nextLink` que venha na resposta.

Dois detalhes do mecanismo:

- **Âncora com o literal cru.** A âncora reusa o literal de `SysModifiedDateTime` **exatamente como
  veio na resposta**, para o `eq` não errar por formatação ou precisão.
- **Teto de 10.000 no `pageSize`.** O `pageSize` fica limitado ao teto de página do servidor.
  Acima dele, o F&O truncaria a página em 10.000, ela seria confundida com a página curta final, e a
  marca do D3 saltaria linhas não lidas.

**Por que não o nextLink.**

- **O offset pula linha.** O nextLink do F&O é `$skip`/`$top` (ver Context). Offset sobre tabela viva,
  com ordenação não total, pode **pular** linha. Uma nota já varrida que sofre update ganha timestamp
  novo e migra para o fim da ordenação. Tudo depois dela desloca uma casa, e exatamente uma linha na
  fronteira da página se perde. Update de `Status` é a nossa carga principal.
- **Em backfill, a perda é permanente.** Em regime, a sobreposição de 300s recupera a linha perdida.
  Em **backfill** não: ao rebobinar para 2015 (o próprio teste manual do Migration Plan), a linha
  pulada pode ser de 2016 e a marca final, de 2026.
- **O keyset é imune.** A âncora é valor, não posição, e nenhum insert ou update desloca o que ainda
  não foi lido. Uma linha atualizada no meio da leitura ganha timestamp novo e reaparece mais adiante ou
  na próxima passada: repetição, nunca perda.
- **O keyset também elimina o travamento por empate.** `FiscalDocumentRecId` é único na tabela, entre
  empresas, então a âncora composta é estritamente crescente e única: nunca devolve a mesma página duas
  vezes, mesmo com milhares de linhas no mesmo segundo.
- **Bônus.** Deixa de depender de o `maxpagesize` ser honrado e do formato do nextLink.

**A rejeição original de `$top` continua certa para o mecanismo que ela considerava.** Aquele
mecanismo refazia a consulta a partir de `marca − sobreposição` a cada página e travava quando havia N
ou mais linhas na janela, como no job de export/import de NF-e em lote (ADR-0023 §3). `$top` **com
âncora keyset** é outro mecanismo e não tem esse defeito.

**Entre passadas.** O worker grava a marca a cada página (D2). Uma passada interrompida, por falha ou
pelo teto de 20 páginas, recomeça na passada seguinte de `marca − sobreposição`. Linhas com o mesmo
timestamp da última linha gravada, que ficaram para a página seguinte, são relidas graças à
sobreposição. Por isso a sobreposição precisa ser maior que zero, e isso é validado. O teto só impede
o progresso se houver mais de `teto × pageSize` linhas (10.000 no padrão) dentro da janela de
sobreposição. Nesse caso o worker registra um aviso ao terminar a passada no teto sem que a marca passe
do início da janela.

**Alternativas.**
(a) nextLink server-driven (`Prefer: odata.maxpagesize`): funciona e foi verificado (5 páginas de 20),
mas é offset e perde linha em backfill sob escrita concorrente.
(b) `$top` refazendo a consulta pela marca, sem âncora: trava no empate, como acima.
(c) Keyset em (`SysModifiedDateTime`, `dataAreaId`, `Voucher`): o `$orderby` funciona, mas
`Voucher gt 'X'` não é aceito (string não compara com `gt`), então não dá para ancorar.
(d) `$skip` manual: é o mesmo offset do nextLink.

### D5. Estado em duas tabelas: cursor e lease

```
ChangeFeedCursors   Id · TenantId(100) · Origin(50) · WatermarkTicks · LastPolledTicks?
                    · NotBeforeTicks? · ConsecutiveFailures · LastError(500)? · UpdatedAt
                    índice único (TenantId, Origin)

Leases              Resource(200, PK) · Owner(200) · ExpiresTicks
```

`ILeaseStore` fica numa pasta nova, `Application/Coordination`, e é **genérica** por recurso. Aqui o
recurso é `changefeed:{origin}:{tenant}`; o agendador pode adotá-la depois (pendência do ADR-0017).

```csharp
Task<bool> TryAcquireAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct);
Task<bool> RenewAsync(string resource, string owner, TimeSpan ttl, CancellationToken ct);
Task ReleaseAsync(string resource, string owner, CancellationToken ct);
```

- **Atomicidade.** A aquisição é um `UPDATE Leases SET Owner=@me, ExpiresTicks=@exp WHERE Resource=@r
  AND (Owner=@me OR ExpiresTicks < @now)` via `ExecuteUpdateAsync`: uma instrução, atômica em SQL
  Server e SQLite. Se não existe linha, o `INSERT` é tentado, e a violação de PK significa que outro
  chegou primeiro.
- **Fencing do cursor.** O avanço da marca é condicionado à posse do lease **na mesma instrução**:

  ```sql
  UPDATE ChangeFeedCursors SET WatermarkTicks = @w, UpdatedAt = @now
   WHERE TenantId = @t AND Origin = @o AND WatermarkTicks < @w
     AND EXISTS (SELECT 1 FROM Leases
                  WHERE Resource = @r AND Owner = @me AND ExpiresTicks > @nowTicks)
  ```

  - **EF.** A instrução sai via `ExecuteUpdateAsync`, com `db.Leases.Any(...)` no `Where`, que traduz
    para `EXISTS` em SQL Server e SQLite.
  - **Porta.** A porta do cursor recebe o par (recurso, dono) do lease e devolve `false` quando nenhuma
    linha foi atualizada.
  - **Por que "nenhuma linha" basta para parar.** O worker só chama o avanço quando a marca nova é maior
    que a que ele leu. Então "nenhuma linha" significa que o lease não é mais dele, ou que outra réplica
    já avançou além, o que também implica ter tomado o lease. Nos dois casos o worker encerra o poll do
    tenant sem contar falha.
  - **Acoplamento.** A implementação SQL do cursor conhece a tabela `Leases` (mesmo `DbContext`), e o
    `ILeaseStore` continua genérico.
  - **Por que não basta renovar antes de gravar.** Renovar e depois gravar só estreita a janela: uma
    pausa de GC entre as duas operações ainda deixaria duas réplicas escreverem.
  - **Renovação por página.** Continua, mas só para manter o lease vivo durante uma leitura longa; não é
    ela que protege o cursor.
  - **Condição monotônica.** `WatermarkTicks < @w` impede que uma escrita atrasada faça a marca
    regredir.
- **Relógio do lease.** É o `TimeProvider` do FiscalHub, porque o lease coordena as nossas réplicas.
- **Dono.** `{MachineName}:{ProcessId}:{Guid}`, gerado uma vez por processo.
- **Teto da espera por 429 (D10) menor que o TTL.** Assim a espera inline nunca faz o lease expirar
  na mão de quem espera.

**Alternativas.** (a) Blob lease: exige Azurite nos testes e tem TTL de 15 a 60 segundos ou infinito.
O SQL já está no processo e testa em SQLite. (b) Lease como colunas do cursor: acopla coordenação a
dado e impede o reuso pelo agendador. (c) `sp_getapplock`: não existe em SQLite nem é portável.

### D6. Settings: seção `poll` do worker, resto do adapter

Uma só `InboundSettings` por adapter (ADR-0019). A seção `poll` é o contrato do **worker** (genérica,
vale para qualquer feed) e é lida por `ChangeFeedPollSettings.Parse` na Application, só com
`System.Text.Json`. O resto é do adapter.

```json
{
  "url": "https://fiscosysdev.operations.dynamics.com",
  "companies": ["brmf"],
  "pageSize": 500,
  "modelTypes": { "55": "GoodsInvoice55", "57": "Transport57", "SE": "ServiceNfse" },
  "auth": { "tenantId": "<entra-tenant>", "clientId": "<app-id>", "clientSecretRef": "kv:d365-a-secret" },
  "poll": { "enabled": true, "intervalSeconds": 60, "overlapSeconds": 300, "startFrom": "2015-01-01T00:00:00Z" }
}
```

Padrões: `enabled=false`, 60s, 300s, sem `startFrom`, `pageSize=500`, mapa de modelos acima. Validação:
`overlapSeconds ≥ 1` (D4) e `pageSize` entre 1 e 10.000 (D4). Fora disso, é erro de configuração do
tenant. O adapter
lê as settings pelo `IConnectorProfileStore` (porta da Application) e por isso é registrado como
scoped. A assinatura `PullAsync(tenantId, since, ct)` do pedido fica como está.

**Alternativa.** A porta expor um método para o worker perguntar ao adapter o intervalo e a
sobreposição: dá mais cerimônia para o mesmo resultado, e cada adapter repetiria o parse de `poll`.

### D7. Fila de descoberta: `IDocumentQueue` por chave, sem consumidor

`AddServiceBusDiscoveryQueue(o => o.QueueName = "documents-discovered")` registra
`AddKeyedSingleton<IDocumentQueue>("discovery", ...)` com o mesmo `ServiceBusDocumentQueue`, agora com
nome de fila por parâmetro, e **sem** trigger consumidor. O Host constrói o `ChangeFeedPoller` por
factory, com a fila da chave `"discovery"`. `documents-discovered` entra no
`docker/servicebus/Config.json`. O `MessageId` segue `{tenant}:{naturalKey}`, e a detecção de
duplicata segue desligada (D3c).

**Alternativas.** (a) `documents-in`: as referências D365 iriam para a DLQ, porque o
`XmlGoodsInvoiceSource` não sabe buscá-las. Rejeitado na decisão do pedido. (b) Uma porta nova
`IDiscoveryQueue`: duplica o `IDocumentQueue` sem diferença de contrato.

### D8. Identidade, localizador e tipo

- `NaturalKey = {dataAreaId}|{Voucher}` (decisão do pedido). Evidência: zero vouchers duplicados em
  83 cabeçalhos (d365/05 §8). O `Voucher` é o caminho de busca já validado (`$filter=Voucher eq`) e o
  `cod_referencia_integracao` do legado. Cabe no limite de 64 caracteres do
  `ProcessedDocuments.NaturalKey`: `dataAreaId` tem até 4 e o EDT do voucher, 20.
- `Locator = d365/{dataAreaId}/{Uri.EscapeDataString(Voucher)}`: contrato com a fatia de montagem, que
  busca o cabeçalho por `dataAreaId` + `Voucher`.
- O tipo sai de `Model` pelo mapa das settings. Modelo fora do mapa, ou voucher vazio, gera aviso em
  log e fica fora da fila. Não é falha: um registro ruim não pode travar a marca do tenant ("falha
  isolada por documento"). A fatia de roteamento transforma isso em decisão gravada.
- `Trigger = Event` (idempotente). `SourceMode = null`, o rótulo de tempo real no painel.
- `$select=dataAreaId,Voucher,Model,Direction,Status,FiscalDocumentNumber,FiscalDocumentSeries,SysModifiedDateTime,FiscalDocumentRecId`.
  - **Os 8 primeiros:** já verificados com `$select` no fiscosysdev. `Direction`, `Status`, número e série
    não entram na referência nesta fatia; vêm no select porque entram no log de aviso.
  - **`FiscalDocumentRecId`:** é a âncora do keyset (D4). É também o desempate disponível para a
    `NaturalKey`, caso o voucher se revele repetível (ver Risks).

### D9. Token: `ID365TokenProvider` sobre Azure.Identity

```csharp
internal interface ID365TokenProvider
{
    Task<string> GetTokenAsync(D365Connection connection, CancellationToken ct = default);
}
```

- **Produção:** `ClientSecretCredential(entraTenant, clientId, secret)`, com escopo `{url}/.default`.
  Uma instância de credencial por (tenant do Entra, clientId), num singleton; o Azure.Identity guarda o
  token em memória por instância e renova antes de expirar.
- **Dev:** `AzureCliCredential`, com o mesmo escopo. Só entra no DI quando o Host chama
  `UseD365AzureCliToken()`, e o Host só chama em `IsDevelopment()`. Segue o precedente do
  `AddAvalaraTokenProvider`, que troca o registro via `Replace`.
- **Segredo:** `kv:<nome>` é lido como `IConfiguration[<nome>]` dentro do adapter. No local, o valor
  vem de user-secrets ou variável de ambiente. Em produção, o provider de configuração do Key Vault
  põe os segredos no `IConfiguration` pelo nome, e o código é o mesmo. Valor sem `kv:`, ou referência
  que não resolve, dá erro de configuração do tenant.
- **Teste:** uma fábrica de `TokenCredential` injetável permite provar o escopo, o cache por tenant e a
  resolução do segredo sem rede.

**Alternativas.** (a) OAuth na mão, como o `AvalaraTokenProvider`. Aquele existe porque a Avalara não é
Entra; para o Entra seria reescrever o MSAL. (b) Chamar `az account get-access-token` por processo: o
`AzureCliCredential` já faz isso e cobre as diferenças de plataforma (`az.cmd` no Windows). (c) Porta
compartilhada `ISecretResolver`: adiada até um segundo adapter precisar de segredo por tenant.

### D10. 429 com `Retry-After` dentro do adapter

Uma rotina única de envio no adapter repete a **mesma** URL, com a mesma âncora keyset, em 429 ou em
503 com `Retry-After`. A espera honra `Retry-After`, em segundos ou data HTTP. Sem o header, usa 1, 2, 4 e 8
segundos. São no máximo 5 tentativas, com teto de 30 segundos por espera, menor que o TTL do lease.
Espera pedida acima do teto, ou tentativas esgotadas, lançam `ChangeFeedThrottledException(retryAfter)`.
O worker grava `NotBefore = agora + retryAfter` no cursor, e o tenant só volta depois disso. Continuar
batendo durante o 429 alonga o `Retry-After` do F&O, como diz a documentação de throttling. A espera
usa `Task.Delay(TimeSpan, TimeProvider, ct)`, e os testes passam um relógio falso que registra as
esperas sem dormir.

### D11. A passada do worker e a casca no Host

```
RunOnceAsync(ct):
  para cada perfil em ListByInboundAdapterAsync(feed.Origin):
    settings = Parse(poll)                     → erro: registra falha do tenant e segue
    se !enabled ou !vencido(cursor): segue
    se !TryAcquire(lease): segue               → conta "lease tomado"
    try:
      relê o cursor; se !vencido: segue        (outra réplica acabou de rodar)
      cursor ??= cria com startFrom ?? agora
      W = cursor.Watermark
      await foreach página in feed.PullAsync(tenant, W − overlap):   (keyset dentro do adapter, D4)
        enfileira cada referência (Trigger=Event) na fila de descoberta
        se !Renew(lease): para, sem gravar e sem contar falha      (mantém o lease vivo)
        se página.HighWatermark > W:
          se !TryAdvanceWatermark(tenant, origin, página.HighWatermark, lease):
            para, sem contar falha                                  (fencing, D5)
          W = página.HighWatermark
        se páginas == MaxPagesPerPass: para
      registra sucesso (LastPolled=agora, falhas=0, erro=null)
    catch Throttled t: registra falha + NotBefore = agora + t.RetryAfter
    catch Exception quando !ct.IsCancellationRequested: registra falha
    finally: Release(lease) com CancellationToken.None
```

"Vencido" significa `agora ≥ max(LastPolled + intervalo, NotBefore)`. O método devolve um resumo
(tenants consultados, referências enfileiradas, falhas, leases tomados) para o log da casca.
O `ChangeFeedPollingService` é um `PeriodicTimer` de 15 segundos (resolução do intervalo por tenant)
que abre um escopo, resolve o `ChangeFeedPoller` e chama `RunOnceAsync`. Opções globais
(`ChangeFeedPollerOptions`): TTL do lease (2 minutos), teto de páginas por passada (20) e id do dono.

### D12. Testes

- **Application:** `ChangeFeedPollerTests`, com feed, cursor store, lease store, fila e relógio falsos,
  todos manuais, sem biblioteca de mock (padrão do repo). Cobre um cenário por requisito do
  `change-feed-polling`.
- **Infrastructure:** `SqlLeaseStoreTests` e `SqlChangeFeedCursorStoreTests` em SQLite em memória.
  Cobrem aquisição, disputa, expiração, renovação por dono, liberação e ida e volta dos ticks. Cobrem
  também o avanço da marca **condicionado ao lease**: aceito com lease válido do dono; recusado com
  lease de outro, expirado ou ausente; recusado quando não é maior que a marca atual. Mais o novo
  `ListByInboundAdapterAsync`.
- **Adapter:** projeto novo `FiscalHub.Adapters.Ingress.D365Poll.Tests`, com um stub HTTP
  **sequencial** no mesmo estilo do `StubHttpMessageHandler` do Avalara: fila de respostas, registro de
  todas as requisições. O stub do Avalara serve uma resposta só e é `internal` àquele projeto. Os testes
  de paginação conferem o `$filter` de cada requisição: a âncora da página N é a última linha da página
  N−1, com o literal cru.
- **Integração:** `[D365IntegrationFact]`, um `FactAttribute` que preenche `Skip` quando
  `FISCALHUB_D365_URL` não está definida. Usa `AzureCliCredential` e `FISCALHUB_D365_COMPANY`. Com
  `pageSize=20`, confere 83 registros em 5 páginas e 83 `FiscalDocumentRecId` distintos.

## Risks / Trade-offs

- **[Comportamento sob volume real]** → O fiscosysdev tem 83 cabeçalhos. Numa base de cliente com
  milhões, cada página keyset ordena por `SysModifiedDateTime,FiscalDocumentRecId` com um `or` no filtro.
  Se a `FiscalDocument_BR` não tiver índice que cubra `ModifiedDateTime`, cada página pode virar
  varredura mais ordenação. O spike (1.2) mede isso num ambiente com volume. Se degradar, as opções são
  página maior, janela com limite superior ou índice por extensão de tabela (metadado, sem código X++).
  A decisão fica para quando houver medição.
- **[`FiscalDocumentRecId` não responder a `$orderby`/`$filter` fora da brmf]** → Só foi verificado com
  `dataAreaId eq 'brmf'`. O spike (1.2) repete a consulta keyset cross-company sem filtro de empresa e
  em cada empresa presente.
- **[Voucher repetível: `NaturalKey = empresa|Voucher` se apoia em 83 cabeçalhos sem duplicata]** → É
  evidência, não prova.
  - **Cenário de risco:** uma sequência numérica de voucher que **reinicia por exercício fiscal** faria
    o voucher repetir entre anos. A nota do ano novo colidiria na identidade com a do ano anterior: seria
    tratada como versão dela, sobrescreveria o rastreio da antiga, e a idempotência poderia suprimir uma
    nota legítima.
  - **Mitigação:** item de verificação com o cliente (tarefa 1.5) antes de a `NaturalKey` virar contrato
    do store, na fatia de montagem. Se reiniciar, o desempate disponível é o `FiscalDocumentRecId`, que
    já está no `$select` por causa do keyset. A troca custa zero enquanto a fila de descoberta não tem
    consumidor.
  - **Voucher vazio**, em fluxo de ISV ou customização, gera aviso e fica fora da fila.
  - **Resolvido em 2026-09-25 (tarefa 1.5).** Pelo domínio, o voucher é **único e imutável**.
    - A `NaturalKey` está confirmada, e `Approved → Cancelled` mantém a mesma chave (documento 1:N
      tentativas).
    - Fica só o risco residual por cliente: a sequência é configuração de cada F&O. Ele é verificado no
      onboarding, com o desempate `empresa|ano|voucher` ou `empresa|RecId` já disponível (ADR-0024 §6).
- **[Réplica atrasada depois de perder o lease (pausa de GC, rede)]** → A gravação da marca é
  condicionada ao lease na mesma instrução (D5), então a réplica atrasada não avança o cursor. Ela pode
  ter enfileirado a página antes de descobrir: o modo de falha é **duplicação, nunca perda**. Cada
  réplica só avança a marca sobre o que ela mesma enfileirou, e a condição monotônica impede regressão.
  A janela residual dentro da instrução depende da diferença de relógio entre réplicas, que é muito
  menor que o TTL.
- **[Gravação visível só depois de passada a sobreposição]** → Documento perdido pelo poll. A
  sobreposição padrão é generosa (300s) e configurável por tenant. A rede de segurança é a descoberta
  D-1 agendada (futuro `IDocumentDiscovery` do D365).
- **[Repetição limitada (~6 por documento no padrão) na fila de descoberta]** → Custo zero nesta fatia,
  porque não há consumidor. A fatia de roteamento absorve barato comparando (chave,
  `SysModifiedDateTime`) antes de montar. Como a fila é nova e sem consumidor, o contrato da mensagem
  pode evoluir sem migração.
- **[Travamento se houver mais de `teto × pageSize` linhas na janela de sobreposição]** → Aviso em log
  quando a passada termina no teto sem sair da janela. O ajuste é por `pageSize` ou pelo teto.
- **[Diferença de relógio entre réplicas no lease]** → TTL de 2 minutos contra diferença de NTP na casa
  dos milissegundos.
- **[Token do Azure CLI em produção por engano]** → O provider só é registrado quando o Host chama
  `UseD365AzureCliToken()` dentro de `IsDevelopment()`.
- **[TTL de 1 hora do emulador]** → Mensagens de descoberta expiram sem consumidor. Aceitável nesta
  fatia: o teste manual espia a fila logo depois da passada.
- **[Seed de dev só roda em banco vazio]** → Bancos de dev existentes mantêm as settings antigas, sem
  `poll`, logo com o poll desligado, o que é inofensivo. O roteiro do teste manual configura o perfil
  por `PUT /connector` ou SQL.

## Migration Plan

1. A migration EF só **adiciona** as tabelas `ChangeFeedCursors` e `Leases`, e o
   `MigrateProcessingSchemaAsync` a aplica no startup.
2. Com o poll desligado por padrão (`poll.enabled` ausente = `false`), subir o código não chama nenhum
   F&O.
3. Teste manual (roteiro em `docs/RUNNING.md`):
   - `docker compose up -d`
   - `az login` com um usuário que tem acesso ao fiscosysdev
   - `PUT /connector` do tenant-a com `url`, `companies: ["brmf"]`, `pageSize: 20` (exercita 5 páginas
     por keyset), `poll.enabled: true` e `poll.startFrom: "2015-01-01T00:00:00Z"`
   - rodar o Host e conferir no log a passada que lê os 83 cabeçalhos em 5 páginas e enfileira as
     referências
   - conferir a mensagem em `documents-discovered` e o cursor no SQL (`WatermarkTicks` ≈ agora)
   - para repetir: apagar a linha do cursor (o `startFrom` volta a valer) ou atualizar
     `WatermarkTicks` para 2015
4. **Rollback:** `poll.enabled=false` no perfil para o poll do tenant na próxima passada. Tirar o
   registro do Host desliga tudo. As tabelas novas não interferem em nada.

## Open Questions

- **Campos de decisão na mensagem de descoberta** (`Model`, `Direction`, `Status`,
  `SysModifiedDateTime`), para o roteamento decidir sem consultar o D365. A resposta fica para a fatia
  de roteamento; o contrato da fila nova pode evoluir sem custo.
- **Tenant desativado** (`Tenants.Active = false`): hoje só `poll.enabled` governa. Cruzar com
  `Tenants` quando o ciclo de vida de tenant for automatizado.
- **`SysModifiedDateTime` na `FSFiscalDocumentLineBR`**: só importa se uma linha puder mudar sem tocar
  o cabeçalho (pendência do d365/04 §11).
- **Adotar o `ILeaseStore` no `IntegrationScheduler`** e extrair um `ISecretResolver` compartilhado:
  melhorias independentes, fora desta fatia.
