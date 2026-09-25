## Why

O ADR-0023 fez da descoberta por polling sobre a `FSFiscalDocumentBR` a garantia de captura do D365 F&O,
mas ainda não existe código que pergunte ao ERP o que mudou. O change tracking (`odata.track-changes`)
não está disponível: exige a entidade no Data management workspace, o DMF está desligado nas 14
entidades (d365/05 §4) e o row version change tracking reprova nas regras de validação. O mecanismo
é **janela por data sobre `SysModifiedDateTime`, com marca d'água nossa**. Esta é a fatia 1:
**descobrir e enfileirar**. A montagem do documento fica para a fatia seguinte.

## What Changes

- **Porta nova `IDocumentChangeFeed`** (Application/Inbound), separada do `IDocumentDiscovery`. A
  descoberta existente trabalha por período e atende o manual e o agendado. A nova trabalha por delta
  a partir de uma marca d'água, e falha não avança a marca. `PullAsync(tenantId, since, ct)` devolve
  páginas; cada página traz as referências e a marca alta que ela garante.
- **Marca d'água persistida por (tenant, origem)** em ticks UTC (precedente do ADR-0017). Sobrevive a
  restart. Guarda também o último poll, as falhas consecutivas, o último erro e o "não antes de" de
  throttling.
- **Lease por tenant** (porta `ILeaseStore`, genérica), para que duas réplicas não façam poll duplo.
  Resolve a pendência anotada nas consequências do ADR-0017 para o poll; o agendador pode adotá-la
  depois.
- **Worker de polling como lógica pura** (`ChangeFeedPoller`, Application/Inbound), sem timer e
  testável. Uma passada percorre os tenants com o adapter ativo e o poll ligado. Para cada um que está
  vencido: toma o lease, lê a marca, puxa o delta com sobreposição, enfileira cada referência e avança a
  marca página a página. O pedido chamava o worker de `D365PollWorker`. O nome ficou genérico porque a
  lógica só conhece a porta; o ADR-0023 já registra que o mesmo worker serve qualquer ERP com consulta
  por janela.
- **`BackgroundService` fino no Host** (`ChangeFeedPollingService`), no mesmo padrão do
  `SchedulerHostedService`.
- **Adapter `FiscalHub.Adapters.Ingress.D365Poll`** implementando a porta sobre
  `GET /data/FSFiscalDocumentBRs?cross-company=true&$filter=SysModifiedDateTime gt <since>&$orderby=SysModifiedDateTime,FiscalDocumentRecId&$top=<página>`.
  A paginação é por **keyset composto** em (`SysModifiedDateTime`, `FiscalDocumentRecId`): a partir da
  segunda página, o filtro ancora na última linha lida. **Não usa o `@odata.nextLink`**, que no F&O é
  offset (`$skip`/`$top`, verificado no fiscosysdev). Offset sobre tabela viva pode pular uma linha
  quando uma nota já lida é atualizada, e em backfill isso é perda permanente (ver design D4). O adapter
  trata 429 honrando `Retry-After`.
- **Token do D365 interno ao adapter** (`ID365TokenProvider`, no espelho do `IAvalaraTokenProvider`),
  com duas implementações: client credentials (produção) e token do Azure CLI (só em desenvolvimento,
  para rodar local antes da app registration existir).
- **Configuração por tenant no `TenantConnectorProfile` (ADR-0019)**, nas `InboundSettings` do adapter
  `Dynamics365`: intervalo (padrão 60s), sobreposição, tamanho da página, empresas, marca inicial e
  autenticação. Segredo só por referência (`kv:`).
- **Fila própria de descoberta** (`documents-discovered`), usando a mesma porta `IDocumentQueue` numa
  segunda instância e **sem consumidor nesta fatia**. Enfileirar em `documents-in` levaria as
  referências D365 ao `XmlGoodsInvoiceSource`, que não sabe buscá-las, e daí para a DLQ. A fatia de
  roteamento/montagem (ADR-0023, passos 2 e 3) liga o consumidor.
- **Identidade do documento D365**: `NaturalKey = <empresa>|<Voucher>`. Ajusta o ADR-0023 §5, onde
  empresa|número|série colide em nota de entrada (dois fornecedores com a mesma NF e série na mesma
  empresa).
- **`IConnectorProfileStore.ListByInboundAdapterAsync`**: consulta de sistema que lista os perfis de um
  adapter de entrada (o worker precisa varrer os tenants).
- **ADR-0024** registra:
  - a porta;
  - a marca d'água pelo relógio do lado F&O (header `Date` do web server);
  - a paginação por keyset;
  - o lease com fencing do cursor;
  - a fila de descoberta;
  - a NaturalKey.

  Uma nota no ADR-0023 aponta para ele.

## Capabilities

### New Capabilities

- `change-feed-polling`: motor genérico de descoberta por feed de mudanças. Cobre a seleção dos tenants
  e o intervalo por tenant, a marca d'água persistida, a sobreposição, o avanço por página e a regra
  "falha não avança". Cobre também o lease por tenant, o enfileiramento na fila de descoberta, o
  isolamento de falha entre tenants e o adiamento por throttling.
- `d365-change-feed`: implementação do feed para o D365 F&O. Cobre a consulta sobre
  `FSFiscalDocumentBRs`, a paginação por keyset, a marca alta pelo relógio do lado F&O e o mapeamento da
  linha para `DocumentReference` (NaturalKey, Locator, tipo pelo modelo). Cobre ainda o 429 com
  `Retry-After`, o token (client credentials / Azure CLI), o schema de settings por tenant e o teste
  contra o ambiente real opt-in.

### Modified Capabilities

Nenhuma. `openspec/specs/` ainda está vazio, e o que existe não recebe spec retroativa.

## Non-goals

- **Montagem do documento** (linhas, impostos, encargos, parceiros) e o `IInboundSource` do D365: próxima
  fatia.
- **Consumidor da fila `documents-discovered`**, **roteamento** (despacha/ignora/já processado) e
  gravação da decisão: fatias seguintes do ADR-0023.
- **Business event, CoC e Service Bus do lado do ERP**: fora do roadmap (ADR-0023).
- **Infra no Azure** (Container Apps, Functions, App Service, Key Vault): local primeiro. O worker roda no
  processo do Host, como o agendador.
- **App registration** no Entra e atribuição da role no F&O.
- **Descoberta por período do D365** (`IDocumentDiscovery` para manual/agendado) e reprocessamento.
- **Backoff exponencial por falhas consecutivas e alerta**. Esta fatia só registra as falhas; o único
  adiamento é o do throttling (`Retry-After`).
- **Tela ou endpoint para ver e rebobinar a marca d'água**. Nesta fatia o rebobinamento é por
  `startFrom` (cursor inexistente) ou SQL.
- **Adotar o lease no `IntegrationScheduler`**: a porta fica pronta, mas o agendador não muda aqui.
- **Resolver genérico de segredos** compartilhado entre adapters.

## Impact

- **Application**: `Inbound/IDocumentChangeFeed`, `ChangeFeedPage`, `ChangeFeedThrottledException`,
  `IChangeFeedCursorStore`, `ChangeFeedCursor`, `ChangeFeedPollSettings`, `ChangeFeedPoller`,
  `ChangeFeedPollerOptions`. `Coordination/ILeaseStore` (pasta nova). `Connectors/IConnectorProfileStore`
  ganha `ListByInboundAdapterAsync`. Nenhuma dependência nova: só o BCL (`System.Text.Json`).
- **Infrastructure**:
  - tabelas `ChangeFeedCursors` e `Leases` (migration EF);
  - `SqlChangeFeedCursorStore`, cujo avanço da marca é condicionado ao lease na mesma instrução;
  - `SqlLeaseStore`.

  Ambos usam `UPDATE` condicional atômico via `ExecuteUpdateAsync`, portável SQLite/SQL Server.
  Também: `SqlConnectorProfileStore.ListByInboundAdapterAsync`. O seed de dev do tenant-a passa ao
  schema novo de settings, com o poll desligado.
- **Adapters**: projeto novo `src/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll`, com dependência
  nova `Azure.Identity`, restrita a ele. `Messaging.ServiceBus` ganha `AddServiceBusDiscoveryQueue`
  (`IDocumentQueue` por chave, sem consumidor).
- **Host**: `ChangeFeedPollingService`, wiring do feed, do poller e da fila de descoberta. Em
  Development, token pelo Azure CLI.
- **Infra local**: fila `documents-discovered` em `docker/servicebus/Config.json`.
- **Testes**: `ChangeFeedPollerTests` (Application), testes SQLite dos stores (Infrastructure) e projeto
  novo `tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests`, com um stub HTTP sequencial
  no padrão do `StubHttpMessageHandler` do Avalara e o teste de integração opt-in.
- **Docs**: `docs/adr/0024-*.md`, nota no ADR-0023, índice `docs/adr/README.md` e roteiro do teste manual
  em `docs/RUNNING.md`.
- **Sistema externo**: leitura OData no F&O do cliente, cerca de 1 requisição por minuto por tenant em
  regime (o limite é 6.000 a cada 5 minutos). Sem escrita.
