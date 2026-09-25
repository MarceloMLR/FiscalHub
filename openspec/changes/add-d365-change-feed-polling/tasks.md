## 1. Spike no ambiente e ADR

- [x] 1.1 Registrar numa seção nova do `d365/05-achados-de-metadata-e-ciclo-de-deploy.md` os fatos já verificados no fiscosysdev (lista do Context do design), sem retestar:
  - filtros e ordenações que funcionam, inclusive o keyset composto;
  - `$select` dos 8 campos;
  - header `Date`;
  - `maxpagesize` honrado;
  - `Voucher gt` recusado;
  - `Preference-Applied` vazio;
  - nextLink por offset (`$skip`/`$top`).
- [x] 1.2 Spike do que continua desconhecido, sem código de produção. Nada disso bloqueia as fatias A–E; o resultado entra na mesma seção do d365/05.
  - (a) `FiscalDocumentRecId` em `$orderby` e no `$filter` keyset cross-company, sem filtro de empresa e em cada empresa presente no ambiente, não só na brmf;
  - (b) comportamento sob volume real: latência de uma página keyset (`$top=500`, `$orderby=SysModifiedDateTime,FiscalDocumentRecId`, filtro com `or`) num ambiente com milhares ou milhões de cabeçalhos, e se existe índice que cubra `ModifiedDateTime` na `FiscalDocument_BR`. Se não houver ambiente com volume agora, deixar registrado como pendência antes do primeiro cliente.
- [x] 1.3 Escrever `docs/adr/0024-feed-de-mudancas-por-janela-de-data-no-d365.md` no template `0000`, registrando:
  - a porta `IDocumentChangeFeed`;
  - a marca d'água pelo relógio do lado F&O: header `Date` do **web server**, que não é necessariamente o relógio que carimba o `ModifiedDateTime`; diferença de milissegundos contra 300s de sobreposição;
  - a paginação por keyset composto (`SysModifiedDateTime`, `FiscalDocumentRecId`) com `$top`, e por que não o nextLink: é offset e perde linha em backfill sob escrita concorrente;
  - o lease genérico em SQL, com o avanço da marca condicionado ao lease na mesma instrução;
  - a fila `documents-discovered`;
  - a `NaturalKey = empresa|Voucher`, que ajusta o §5 do ADR-0023, com o risco do voucher que reinicia por exercício e o `FiscalDocumentRecId` como desempate;
  - o intervalo padrão de 60s.

  Revisa os §1 e §2 do ADR-0023.
- [x] 1.4 Acrescentar a nota de 2026-09 no topo do ADR-0023 apontando para o ADR-0024, no formato da nota do ADR-0022. Atualizar o índice `docs/adr/README.md` com as linhas 0022, 0023 (que faltam) e 0024.
- [ ] 1.5 Verificar com o cliente, ou com o consultor funcional, o escopo da sequência numérica do voucher dos documentos fiscais: contínua ou reinicia por exercício fiscal. Registrar a resposta no ADR-0024.
  - Não bloqueia esta fatia, mas precisa fechar **antes** da fatia de montagem, quando a `NaturalKey` vira contrato do store.
  - Se reiniciar, trocar para uma chave com o `FiscalDocumentRecId` como desempate.

## 2. Fatia A — Porta e worker na Application (test-first)

- [x] 2.1 Criar os tipos em `FiscalHub.Application/Inbound`: `IDocumentChangeFeed`, `ChangeFeedPage`, `ChangeFeedThrottledException`, `IChangeFeedCursorStore` (com o avanço da marca recebendo o par recurso/dono do lease e devolvendo `false` quando recusado, design D5), `ChangeFeedCursor` e `ChangeFeedPollSettings`. Criar a pasta nova `FiscalHub.Application/Coordination` com `ILeaseStore`. Tudo com XML doc em português, no padrão das portas existentes, e sem dependência nova.
- [x] 2.2 Adicionar `ListByInboundAdapterAsync(string inboundAdapter, CancellationToken)` ao `IConnectorProfileStore`. No mesmo passo:
  - implementar em `SqlConnectorProfileStore`, como consulta de sistema sem filtro de tenant logado, com teste SQLite em `SqlConnectorProfileStoreTests`;
  - atualizar os fakes em `AvalaraComplianceDispatcherTests` e `SupportTicketServiceTests`, para o build seguir verde.
- [x] 2.3 Testes de `ChangeFeedPollSettings.Parse`:
  - padrões com `poll` ausente (desligado, 60s, 300s, sem `startFrom`);
  - valores explícitos;
  - `startFrom` em ISO-8601 UTC;
  - `overlapSeconds` zero ou negativo gerando erro de configuração;
  - JSON malformado gerando erro de configuração.
- [x] 2.4 Escrever `ChangeFeedPollerTests` antes do código, com fakes manuais de feed, cursor store, lease store, fila e relógio, e um cenário por requisito do `change-feed-polling`:
  - seleção de tenants: adapter certo, poll ligado, desligado e outro adapter;
  - intervalo: dentro, vencido, por tenant e `NotBefore` de throttling;
  - marca inicial por `startFrom` e por "agora";
  - `since = marca − sobreposição`;
  - sobreposição gerando repetido, reenfileirado e sem regressão da marca;
  - várias páginas avançando página a página;
  - falha na consulta sem avanço;
  - falha na segunda página preservando a primeira;
  - falha no enfileiramento sem avanço;
  - página vazia avançando pela marca alta;
  - teto de páginas por passada;
  - lease já tomado sem consulta;
  - lease perdido na renovação sem gravar a página;
  - avanço da marca recusado pelo store (lease tomado entre renovar e gravar), encerrando sem contar falha;
  - avanço não chamado quando a marca alta da página não supera a marca atual;
  - lease liberado depois de falha;
  - referência enfileirada com `Trigger = Event`;
  - um tenant falhando e o outro seguindo;
  - sucesso zerando as falhas;
  - cancelamento não contando como falha;
  - releitura do cursor sob o lease (outra réplica acabou de rodar).
- [x] 2.5 Implementar `ChangeFeedPoller` e `ChangeFeedPollerOptions` (TTL do lease 2 min, teto de 20 páginas, id do dono) conforme o design D11, até os testes passarem. Incluir o aviso em log quando a passada termina no teto sem sair da janela de sobreposição.
- [x] 2.6 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 3. Fatia B — Persistência do cursor e do lease (Infrastructure)

- [x] 3.1 Escrever `SqlLeaseStoreTests` antes do código, em SQLite em memória:
  - aquisição livre;
  - disputa entre dois donos (o segundo recebe `false`);
  - expiração permitindo a retomada;
  - renovação só pelo dono;
  - renovação depois de tomado por outro devolvendo `false`;
  - liberação só pelo dono;
  - reaquisição pelo próprio dono.
- [x] 3.2 Escrever `SqlChangeFeedCursorStoreTests` antes do código:
  - cria com a marca inicial;
  - lê de volta os ticks com o instante UTC exato;
  - avanço da marca **condicionado ao lease**: aceito com lease válido do dono; recusado com lease de outro dono, expirado ou inexistente; recusado quando a marca nova não é maior que a atual (monotônico);
  - registra sucesso (zera falhas e erro) e falha (incrementa, guarda o erro truncado em 500 e o `NotBefore`);
  - isola por (tenant, origem).
- [x] 3.3 Implementar `ChangeFeedCursorRow`, `LeaseRow`, o mapeamento no `ProcessingDbContext` (índice único `(TenantId, Origin)`, PK `Resource`, tamanhos do design D5), `SqlLeaseStore` e `SqlChangeFeedCursorStore`. O lease usa `UPDATE` condicional atômico via `ExecuteUpdateAsync`, mais `INSERT` que trata a violação de PK. O avanço da marca é **uma** instrução `ExecuteUpdateAsync` com `WatermarkTicks < @w` e `db.Leases.Any(...)` (lease do dono, não expirado) no `Where`, traduzida para `EXISTS`. Conferir o SQL gerado em SQLite e em SQL Server. Registrar os dois em `AddSqlProcessingStore`.
- [x] 3.4 Gerar a migration EF `AddChangeFeedCursorsAndLeases` em `src/FiscalHub.Infrastructure/Migrations` e conferir que só adiciona tabelas.
- [x] 3.5 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 4. Fatia C — Fila de descoberta (Messaging.ServiceBus)

- [x] 4.1 Fazer o `ServiceBusDocumentQueue` receber o nome da fila por parâmetro, sem mudar o registro de `documents-in`. Criar `AddServiceBusDiscoveryQueue(Action<...>)`, que registra `IDocumentQueue` por chave (`"discovery"`) sem trigger consumidor e reusa o `ServiceBusClient` já registrado.
- [x] 4.2 Teste de DI em `FiscalHub.Adapters.Messaging.ServiceBus.Tests`: o `IDocumentQueue` com chave `"discovery"` resolve, o `IDocumentQueue` sem chave continua sendo o de `documents-in` e nenhum hosted service novo é registrado. Sem conectar no bus.
- [x] 4.3 Adicionar a fila `documents-discovered` em `docker/servicebus/Config.json`, com as mesmas propriedades da `documents-in`.
- [x] 4.4 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 5. Fatia D — Adapter `Ingress.D365Poll` (test-first)

- [x] 5.1 Criar `src/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll`, sem repetir o que está no `Directory.Build.props`:
  - referências a Application e Domain;
  - `Microsoft.Extensions.Http` e `Azure.Identity`;
  - `InternalsVisibleTo` para os testes.

  Criar também `tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests` e adicionar os dois ao `FiscalHub.slnx`, nas pastas `/src/Adapters/Ingress/` e `/tests/Adapters/Ingress/`.
- [x] 5.2 Criar, no projeto de teste, um stub HTTP sequencial no estilo do `StubHttpMessageHandler` do Avalara (fila de respostas com status, corpo e headers; registro de todas as requisições), sem biblioteca de mock.
- [x] 5.3 Criar as settings internas do adapter (url, companies, pageSize, modelTypes, auth), com parse e validação e com testes:
  - settings mínimas com os padrões;
  - URL vazia ou relativa;
  - `pageSize` fora de 1 a 10.000;
  - segredo sem `kv:`;
  - auth incompleta;
  - mapa de modelos padrão e sobrescrito.
- [x] 5.4 Escrever os testes do feed antes do código, com token falso:
  - primeira página sem empresas e com empresas:
    - `or` de `dataAreaId`;
    - literal DateTimeOffset UTC;
    - `$orderby=SysModifiedDateTime,FiscalDocumentRecId`;
    - `$top=<pageSize>`;
    - `$select` com os 9 campos, incluindo `FiscalDocumentRecId`;
    - nenhum filtro de `Status`, `Model` ou `Direction`;
  - keyset:
    - três páginas (`pageSize=2`, 5 registros), terminando na página curta;
    - o `$filter` da página N ancorado na última linha da página N−1, reusando o literal cru de `SysModifiedDateTime`;
    - âncora combinada com o filtro de empresas;
    - empate de timestamp na fronteira sem repetir nem pular;
    - página cheia seguida de página vazia encerrando;
    - `@odata.nextLink` na resposta ignorado;
  - marca alta da página cheia, da página curta final com o `Date` da **primeira** resposta, e da leitura vazia;
  - mapeamento `55` e `SE`, com `NaturalKey`, `Locator` codificado e tipo;
  - modelo fora do mapa e voucher vazio, com aviso e sem interromper;
  - `Authorization: Bearer`.
- [x] 5.5 Escrever os testes de throttling antes do código, com relógio falso e sem dormir:
  - `Retry-After` em segundos e em data HTTP, repetindo a mesma URL com a mesma âncora keyset;
  - sem header, espera exponencial;
  - acima do teto, `ChangeFeedThrottledException` com o `RetryAfter` pedido;
  - tentativas esgotadas;
  - 401 e 400 sem repetir.
- [x] 5.6 Implementar o feed (`Origin = "Dynamics365"`), com paginação keyset (design D4) e a rotina única de envio e retry (design D10), até os testes das tarefas 5.4 e 5.5 passarem.
- [x] 5.7 Implementar o `ID365TokenProvider` interno:
  - client credentials sobre `ClientSecretCredential`, com credencial em cache por (tenant do Entra, clientId) e segredo resolvido de `kv:<nome>` via `IConfiguration`;
  - Azure CLI sobre `AzureCliCredential`.

  Testar com uma fábrica de credencial falsa: escopo `<url>/.default`, reuso do cache, resolução do segredo e referência que não resolve.
- [x] 5.8 Criar a extensão de DI pública, único ponto público do adapter:
  - `AddD365ChangeFeed(...)`: feed scoped, provider de client credentials singleton e `HttpClient` nomeado;
  - `UseD365AzureCliToken()`: troca o provider via `Replace`, no precedente do `AddAvalaraTokenProvider`.
- [x] 5.9 Criar o teste de integração opt-in com `[D365IntegrationFact]`, que só roda quando `FISCALHUB_D365_URL` está definida, lendo também `FISCALHUB_D365_COMPANY`. Com Azure CLI, `pageSize = 20` e `since = 2015-01-01T00:00:00Z`, os 83 cabeçalhos da brmf devem ser lidos em 5 páginas, com 83 `FiscalDocumentRecId` distintos. Confirmar que o teste aparece como pulado sem a variável.
- [x] 5.10 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 6. Fatia E — Host, seed e ponta a ponta local

- [x] 6.1 Criar `ChangeFeedPollingService` (`PeriodicTimer` de 15s, escopo por tick, log do resumo da passada, tratamento de cancelamento e exceção) no padrão do `SchedulerHostedService`.
- [x] 6.2 Fazer o wiring no `Program.cs`:
  - `AddD365ChangeFeed`;
  - `UseD365AzureCliToken()` só em `IsDevelopment()`;
  - `AddServiceBusDiscoveryQueue`, com a chave `ServiceBus:DiscoveryQueue` e padrão `documents-discovered`;
  - `ChangeFeedPollerOptions`;
  - `ChangeFeedPoller` scoped por factory, com a fila de chave `"discovery"`;
  - `AddHostedService<ChangeFeedPollingService>()`.
- [x] 6.3 Levar o seed do perfil do tenant-a em `EnsureDevConnectorProfilesAsync` ao schema novo de settings: url do fiscosysdev, `companies: ["brmf"]`, auth por referência `kv:` e `poll.enabled: false`.
- [x] 6.4 Escrever em `docs/RUNNING.md` a seção "Poll do D365 (fatia 1)" com o roteiro do design:
  - `az login`;
  - `PUT /connector` com `pageSize: 20`, `poll.enabled` e `startFrom` 2015;
  - onde ver o log e o cursor;
  - como rebobinar (apagar o cursor ou `UPDATE WatermarkTicks`);
  - o aviso do TTL de 1 hora do emulador.
- [x] 6.5 Executar o roteiro contra o fiscosysdev. Registrar no PR:
  - que a leitura veio em 5 páginas;
  - quantas referências foram enfileiradas e quantos avisos saíram dos 83 cabeçalhos;
  - a marca final;
  - que uma segunda passada logo depois não reenfileira nada fora da janela de sobreposição.
- [x] 6.6 Rodar `dotnet build` (0 warnings), `dotnet test` e `openspec validate add-d365-change-feed-polling --strict`, tudo verde.
