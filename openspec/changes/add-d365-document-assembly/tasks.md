## 1. Gravação das fixtures e ADR

- [x] 1.1 Criar `tools/d365-fixtures/Record-D365Fixtures.ps1` e um `README.md` ao lado (design D15).
  - **Parâmetros:** URL do ambiente, empresa e pasta de saída. O padrão da pasta é
    `tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests/Fixtures/d365/`.
  - **Token** pelo `az account get-access-token --resource <url>`.
  - **Gravação byte a byte** (`Invoke-WebRequest … .Content`, sem reformatar).
  - **Por nota modelo `55` da empresa:**
    - as 4 consultas exatas da montagem (cabeçalho, linhas, impostos com os dois termos, encargos),
      com o `$select` do design D5;
    - a `FSTaxTransBRs` do voucher;
    - os endereços e cidades referenciados pelo cabeçalho.
  - **Snapshots inteiros:** os 83 cabeçalhos, as linhas, `FSFiscalDocumentTaxTransBRs` e
    `FSFiscalDocumentMiscChargeBRs`.
  - **A `FSTaxTransBRs` só dos vouchers fiscais**, em blocos de 20. A entidade inteira tem 297 mil
    linhas de todas as empresas (design D15).
  - **Fora do escopo:** cabeçalho e linhas das notas canceladas e de uma `SE`.
  - **O README diz:** como rodar, que só vale para o fiscosysdev (dado de demonstração) e **nunca** para
    ambiente de cliente, e a convenção `.derived.json` com a edição descrita.
- [x] 1.2 Rodar o script contra o fiscosysdev (`brmf`) e versionar as fixtures. Na mesma rodada, medir e
  registrar em `d365/04-mapeamento-de-entidades.md` §11:
  - há linha da `FSTaxTransBRs` de um voucher fiscal sem linha correspondente na fiscal? É o sentido
    inverso do casamento 547/547;
  - há nota `55` cancelada, e ela mantém as linhas?
  - qual campo de data de emissão vem preenchido (`FiscalDocumentDateTime` / `FiscalDocumentDate`);
  - quais valores o `MiscChargeType` assume;
  - em quais notas estão as 12 retenções e os `ImportTax` zerados na fiscal;
  - qual a resolução do `SysModifiedDateTime` nas respostas: segundo ou fração. A margem de
    assentamento do design D16 cobre os dois casos; isso só diz se ela pode encolher.

  Se a data ou os nomes do enum diferirem do design D14, corrigir a tabela do D14 antes da fatia D
  (seção 6).
- [x] 1.3 Escrever `docs/adr/0025-montagem-do-documento-d365-e-origem-na-referencia.md` no template `0000`,
  com o conteúdo do design D17:
  - origem na referência com fallback no perfil;
  - roteamento por tipo e desfecho `Ignored`;
  - Locator com RecId;
  - só nota autorizada, e vazio é falha;
  - domínio aditivo, com IBS/CBS opcional;
  - fiscal como fonte, complemento por tipo (`ImportTax`) e divergência que para o documento;
  - hash canônico com versão;
  - cache de cadastros;
  - supressão de republicação no poller:
    - tabela de custo (~6× o tráfego, ~27 GETs por nota, teto de ~45 notas/min no F&O e de ~8 no
      consumidor serial sem a supressão);
    - regra do par assentado;
    - estado em memória;
    - por que não é um quarto esquema de idempotência;
  - o "o que a base não exercita";
  - o desfecho esperado no fiscosysdev: 0 enviadas.

  Acrescentar a nota de 2026-09 no topo do ADR-0024, apontando para o 0025, com três pontos:
  - o Locator novo;
  - a fila com consumidor;
  - a revisão da alternativa "filtrar os repetidos da sobreposição", antes rejeitada e agora adotada
    como filtro de tráfego, porque a premissa "o roteamento absorve barato" não se sustentou.

  Acrescentar a linha 0025 no `docs/adr/README.md`.

## 2. Fatia A — Origem na referência e resolver (test-first)

- [x] 2.1 Escrever `InboundSourceResolverTests` (Application.Tests) antes do código, com sources e store
  de perfil falsos:
  - origem preenchida resolve por ela e ignora o perfil;
  - sem origem, cai no `InboundAdapter` do perfil;
  - origem sem adapter falha com `InboundSourceNotFoundException`, cuja mensagem cita origem e tenant;
  - sem origem e sem perfil, falha com mensagem que cita o tenant;
  - duas referências do tenant-a, uma `Xml` e uma `Dynamics365`, vão cada uma ao seu source;
  - comparação exata (`dynamics365` não casa com `Dynamics365`).
- [x] 2.2 Implementar em `FiscalHub.Application/Inbound`, com XML doc em português no padrão das portas:
  - `DocumentReference.Origin` (opcional, com comentário no estilo do `Trigger`);
  - `IInboundSourceResolver<TDocument>`, `InboundSourceResolver<TDocument>` e
    `InboundSourceNotFoundException`.

  Até os testes da 2.1 passarem.
- [x] 2.3 Fazer a `DocumentPipeline<TDocument>` receber o resolver no lugar do source e resolver na
  primeira linha do `ProcessAsync` (design D1). Ajustar `DocumentPipelineTests` e acrescentar dois testes:
  - o source usado é o resolvido para aquela referência;
  - falha de resolução propaga antes de qualquer chamada ao store ou ao trace.
- [x] 2.4 No `ChangeFeedPoller`, enfileirar com `Origin = _feed.Origin`. Teste em `ChangeFeedPollerTests`:
  referência do feed falso sem origem chega à fila com a origem do feed, e o gatilho continua `Event`.
- [x] 2.5 Preencher `Origin = "Xml"` nos produtores XML, cada um com teste afirmando a origem:
  - `LocalDocumentDiscovery.DiscoverAsync` e `FindByKeyAsync`, em `LocalDocumentDiscoveryTests`;
  - BlobDrop: extrair a montagem da referência do `BlobDropWatcher` para um método testável e cobrir
    tenant, chave, locator, tipo e origem;
  - `/ingest` no `Program.cs`.
- [x] 2.6 Registrar o resolver no Host (`IInboundSourceResolver<>` → `InboundSourceResolver<>`, scoped) e
  adicionar um teste de que `XmlGoodsInvoiceSource.Origin == "Xml"` em `XmlGoodsInvoiceSourceTests`.
- [x] 2.7 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes. Smoke local: um drop de XML do
  tenant-a, cujo perfil é `Dynamics365`, continua processado pelo source XML.

## 3. Fatia A2 — Supressão de republicação no poller (test-first)

- [x] 3.1 Escrever os testes do `ChangeFeedPublicationLog` (Application.Tests) antes do código:
  - par registrado é reconhecido;
  - o mesmo documento com outro carimbo não é reconhecido;
  - isolamento por (tenant, origem);
  - poda remove os pares com `ChangedAt ≤ desde`, e só eles;
  - marca menor que a última vista zera aquele (tenant, origem), e só ele;
  - registro concorrente de várias threads sem exceção nem perda.
- [x] 3.2 Implementar em `FiscalHub.Application/Inbound` (design D16), até os testes da 3.1 passarem:
  - `ChangeFeedItem(Reference, ChangedAt)`;
  - `ChangeFeedPage.Items`, que substitui `References`, e `ChangeFeedPage.StableThrough`;
  - `ChangeFeedPublicationLog`;
  - `ChangeFeedPassSummary.ReferencesSuppressed`.
- [x] 3.3 Escrever os testes do poller em `ChangeFeedPollerTests` antes do código, com um log real e um
  feed falso, um por cenário do delta `change-feed-polling`:
  - par assentado não é republicado, e a passada conta uma suprimida;
  - gravação no mesmo segundo depois da leitura: o par quente é republicado na passada seguinte, e só
    então suprimido;
  - página sem horizonte não registra nem suprime;
  - documento alterado dentro da janela (carimbo novo) é republicado;
  - reinício (log novo) republica;
  - rebobinamento da marca republica tudo;
  - falha no meio da página: as referências já enfileiradas são suprimidas na releitura, e o resto é
    enfileirado;
  - poda pela janela;
  - página inteira suprimida ainda avança a marca pela marca alta.

  Ajustar os testes existentes à troca de `References` por `Items`.
- [x] 3.4 Implementar a supressão no `ChangeFeedPoller`, até os testes da 3.3 passarem:
  - log recebido no construtor;
  - no início do poll de cada (tenant, origem), checar o rebobinamento e podar;
  - pular o par já registrado;
  - registrar depois do `EnqueueAsync` bem-sucedido, só se `ChangedAt ≤ StableThrough`.

  Incluir `ReferencesSuppressed` no log do resumo da passada no `ChangeFeedPollingService`.
- [x] 3.5 No `D365ChangeFeed`:
  - `Items` com `ChangedAt` pelo `SysModifiedDateTime` já lido;
  - `StableThrough` = `Date` da primeira resposta da leitura − `StampSettleMargin`, em todas as páginas
    da leitura;
  - sem `Date`, `null`;
  - `D365ChangeFeedOptions.StampSettleMargin`, com padrão de 10s; abaixo de 1s, o `AddD365ChangeFeed`
    falha com erro de configuração.

  Ajustar `D365ChangeFeedTests` a `Items` e acrescentar os cenários do delta `d365-change-feed`:
  - horizonte pela primeira resposta em todas as páginas;
  - sem `Date`, sem horizonte;
  - carimbo da referência;
  - margem 0 recusada.
- [x] 3.6 No Host, registrar o `ChangeFeedPublicationLog` como singleton e passá-lo na factory do
  `ChangeFeedPoller`, que continua scoped. Comentar por que o log não pode viver no poller.
- [x] 3.7 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 4. Fatia B — Roteador por tipo e desfecho `Ignored` (test-first)

- [x] 4.1 Acrescentar `IntegrationStatus.Ignored` no fim do enum e
  `IProcessingStore.RecordIgnoredAsync(reference, reason, ct)`. Atualizar todos os fakes de
  `IProcessingStore` dos testes, para o build seguir verde.
- [x] 4.2 Escrever os testes em `SqlProcessingStoreTests` (SQLite) antes do código:
  - cria a linha como `Ignored`, com o motivo;
  - sobre linha `Confirmed` com `ExternalId`, passa a `Ignored` e preserva o `ExternalId`;
  - repetição regrava sem erro, com uma linha só;
  - `AlreadyProcessedAsync` é `false` para `Ignored`.

  Implementar `SqlProcessingStore.RecordIgnoredAsync` sobre o `UpsertAsync`. Conferir que não precisa
  de migration (status texto, 20 caracteres).
- [x] 4.3 Escrever `DocumentRouterTests` (Application.Tests) antes do código:
  - `GoodsInvoice55` chama a esteira com a referência e o contexto;
  - `ServiceNfse` e `Transport57` gravam `Ignored` com "ignorado: tipo fora do escopo (<tipo>)", sem
    chamar a esteira;
  - `DocumentOutOfScopeException` vinda da esteira grava `Ignored` com o motivo da exceção e não relança;
  - qualquer outra exceção propaga.
- [x] 4.4 Implementar `DocumentOutOfScopeException` (Application/Inbound, com `Reason`), `IDocumentRouter`
  e `DocumentRouter` (Application/Pipeline), até os testes da 4.3 passarem.
- [x] 4.5 Fazer o `QueuedDocumentProcessor` chamar o `IDocumentRouter` no lugar da esteira (design D2).
  Ajustar `QueuedDocumentProcessorTests` (router falso) e registrar o `DocumentRouter` scoped no Host. Para
  o XML nada muda: todos os produtores publicam `GoodsInvoice55`.
- [x] 4.6 Dashboard:
  - `'Ignored'` em `dashboard/src/types.ts`;
  - rótulo "Ignorado", com tom neutro, no `StatusChip`;
  - fora de `FAILURE_STATUSES`;
  - conferir filtros ou listas de status que precisem da entrada nova (`grep` por `DeadLettered` em
    `dashboard/src`).

  Rodar o build do dashboard (`npm run build`), verde.
- [x] 4.7 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 5. Fatia C — Domínio aditivo e validador (test-first)

- [x] 5.1 Criar em `FiscalHub.Domain/Goods` os tipos `TaxKind`, `ChargeKind`, `TaxLine` e `ItemCharge`
  (design D7), com XML doc em português. No `GoodsInvoiceItem`:
  - `ReformTaxes` passa a opcional;
  - acrescentar `Taxes`, `Withholdings` e `Charges`, com lista vazia por padrão.

  O Domain continua sem dependência.
- [x] 5.2 Escrever os testes do `GoodsInvoiceValidator` antes do código:
  - item com `ReformTaxes` nulo gera "Item N: tributos da Reforma (IBS/CBS) ausentes.", e não as
    mensagens de CST e `cClassTrib`;
  - item com o grupo e `ClassTrib` vazio continua gerando "cClassTrib ausente";
  - os testes existentes seguem verdes.

  Implementar.
- [x] 5.3 No `GoodsInvoiceToAvalara`, um guard que lança `InvalidOperationException` se `ReformTaxes`
  chegar nulo. O caso é inalcançável depois da validação, e o payload não muda. Teste do guard em
  `GoodsInvoiceToAvalaraTests`, e os testes de payload seguem iguais.
- [x] 5.4 Conferir que o `NfeXmlParser` e os testes dele compilam e passam sem mudança de comportamento.
- [x] 5.5 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 6. Fatia D — Source do D365 (test-first, sobre as fixtures)

- [x] 6.1 Locator com RecId no feed: `d365/<dataAreaId codificado>/<FiscalDocumentRecId>`. Ajustar
  `D365ChangeFeedTests`:
  - nota de mercadoria com o Locator novo;
  - voucher com caractere especial fica só na `NaturalKey`;
  - o teste antigo de voucher codificado no Locator sai.

  Atualizar os comentários do contrato no feed e no `DocumentReference`.
- [x] 6.2 Extrair o `D365ODataClient` (internal) do `D365ChangeFeed.SendAsync`, sem mudar o comportamento:
  bearer, `Accept`, 429/503 com `Retry-After`, teto e tentativas. Acrescentar `GetAllAsync`, que segue o
  `@odata.nextLink`. Os testes do feed passam sem alteração. Testes novos: `GetAllAsync` junta duas
  páginas por nextLink, e o throttling vale também na página seguinte.
- [x] 6.3 Escrever os testes do `D365DocumentLocator` antes do código e implementar:
  - Locator válido, com `dataAreaId` decodificado;
  - formato antigo (voucher);
  - RecId zero, negativo ou não numérico;
  - locator de outra origem (`nfe/...`).

  Cada caso inválido traz o formato esperado na mensagem.
- [x] 6.4 Escrever os testes do `D365Canonicalizer` antes do código, sobre uma nota gravada:
  - duas canonicalizações da mesma resposta dão o mesmo hash;
  - mudar só `SysModifiedDateTime` e `@odata.etag` não muda o hash;
  - mudar um `TaxAmount` muda;
  - embaralhar as linhas de imposto e a ordem das propriedades não muda;
  - número com texto `100.50` preservado como veio;
  - `accounting` vazio contra preenchido;
  - `"v":1` presente;
  - nenhuma anotação `@odata.*` no resultado.

  Implementar (design D11).
- [x] 6.5 Escrever os testes do `D365ReferenceDataCache` antes do código, com `TimeProvider` falso e loader
  contado:
  - acerto dentro da expiração não chama o loader;
  - depois da expiração chama de novo;
  - tenants diferentes com o mesmo RecId não compartilham;
  - não encontrado não fica em cache;
  - RecId `0` não chama.

  Implementar (design D13) e criar `D365AssemblyOptions` (público, `ReferenceDataTtl` com padrão de
  1 hora).
- [x] 6.6 Escrever os testes do `D365GoodsInvoiceAssembler` antes do código. Usar fixtures gravadas; onde a
  fixture for derivada, o teste diz isso no nome ou num comentário (design, "O que a base não exercita").
  - **Montagem completa** de uma nota `55` gravada, com cabeçalho, partes, itens e impostos afirmados
    campo a campo contra a resposta. Para as outras notas `55` gravadas, montagem sem erro e invariantes:
    um item por linha e todo imposto num lugar só.
  - **Mapeamento:**
    - emitente e destinatário por `FiscalDocumentIssuer`, nos dois sentidos;
    - CFOP, NCM e CNPJ só com dígitos;
    - `LineNum` fracionário falha;
    - zero linhas dá zero itens.
  - **Distribuição:**
    - imposto de linha;
    - imposto de encargo com FK de linha nula (derivada);
    - retenção `RetainedTax = Yes` nas `Withholdings` e fora dos `Taxes`, com linhas gravadas do
      snapshot;
    - órfão, duas FKs vazias, duas preenchidas e tipo `Blank` falham, com os RecIds na mensagem
      (derivadas).
  - **Varredura da base gravada:** os 547 impostos sobre as linhas e os encargos do snapshot, com zero
    órfãos, 12 retenções nas `Withholdings` e nenhuma nos `Taxes`.
  - **Sinal e CST:** o par IPI gravado (fiscal `51` positivo, contábil `01` negativo) sai com `51`
    positivo.
  - **Grupo IBS/CBS** (derivadas):
    - nenhum tipo dá grupo nulo;
    - os três dão o grupo com `vIBS = UF + Mun` e `ClassTrib` vazio;
    - parcial falha;
    - CST ou base divergente falha;
    - IBS retido falha.
  - **Complemento:**
    - `ImportTax` zerado na fiscal com valor na contábil sai com os valores contábeis, campo a campo
      (gravada, se houver no snapshot; senão, derivada);
    - IPI CST 05 zerado fica zerado.
  - **Divergências** (derivadas): ponte sem par, tipo diferente, contábil negativa e dois valores
    diferentes. Cada uma falha com voucher, RecIds e campo na mensagem.
- [x] 6.7 Implementar o `D365GoodsInvoiceAssembler` (design D7 a D10 e D14), até os testes da 6.6 passarem.
- [x] 6.8 Escrever os testes do `D365GoodsInvoiceSource` antes do código, com o `SequencedHttpMessageHandler`
  servindo as fixtures, token falso, trace falso e store de perfil falso:
  - **Quatro consultas** em ordem, com `cross-company=true`, o `$select` do D5 e o filtro de impostos de
    dois termos.
  - **Nenhum `$filter`** contém enum.
  - **Complemento:** a quinta consulta (`FSTaxTransBRs` por voucher e empresa) só acontece com
    `ImportTax` zerado.
  - **Cabeçalho vazio:** exceção com empresa, RecId e a hipótese de permissão, sem `DocumentOutOfScopeException`.
  - **Chave natural diferente:** exceção com as duas chaves.
  - **Fora do escopo:** `Model = SE` e `Status = Cancelled` lançam `DocumentOutOfScopeException` com o
    motivo, e nenhuma outra consulta é feita depois do cabeçalho.
  - **Foto da fonte:**
    - salva com o canônico, formato `json`, antes da montagem;
    - presente quando a montagem falha por divergência;
    - ausente quando para no cabeçalho;
    - o hash da foto é igual ao `ContentHash` devolvido.
  - **Cache:** duas notas com as mesmas partes consultam endereço e cidade uma vez só.
  - **Entrada inválida:** settings inválidas e Locator inválido falham sem rede.
  - **Throttling:** 429 com `Retry-After` curto repete e segue.
- [x] 6.9 Implementar o `D365GoodsInvoiceSource` (`Origin = D365ChangeFeed.OriginName`, ordem do
  `FetchAsync` do design D5) e a extensão pública `AddD365GoodsInvoiceSource(Action<D365AssemblyOptions>?)`,
  que registra o source scoped e o cache singleton. Teste de registro: o source resolve junto com o do
  XML em `IEnumerable<IInboundSource<GoodsInvoice>>`, com origem `Dynamics365`.
- [x] 6.10 Criar o teste de integração opt-in com `[D365IntegrationFact]` (`FISCALHUB_D365_URL`,
  `FISCALHUB_D365_COMPANY`). Com o Azure CLI, monta cada nota `55` da `brmf` sem erro, e duas montagens
  seguidas da mesma nota dão a mesma impressão. Confirmar que aparece como pulado sem a variável.
- [x] 6.11 Rodar `dotnet build` (0 warnings) e `dotnet test`, ambos verdes.

## 7. Fatia E — Consumidor da fila de descoberta, Host e ponta a ponta

- [x] 7.1 Parametrizar pelo nome da fila, no construtor, o `ServiceBusTriggerService` e o
  `DeadLetterTriggerService`.
  - Registrar as instâncias com `AddSingleton<IHostedService>(sp => …)`, e não com `AddHostedService`,
    que descarta a segunda do mesmo tipo (design D3).
  - O `AddServiceBusDiscoveryQueue` passa a registrar o consumidor e a dead-letter da
    `documents-discovered`, com `MaxConcurrentCalls = 1` e o motivo comentado.
  - O comentário "sem consumidor" sai.
- [x] 7.2 Atualizar `DiscoveryQueueRegistrationTests`, sem conectar no bus:
  - existem consumidor e dead-letter para `documents-in` e para `documents-discovered`, um de cada por
    fila;
  - o `IDocumentQueue` sem chave continua sendo o da `documents-in`.

  Teste do `DeadLetterHandler` com uma referência vinda da fila de descoberta: grava `DeadLettered` com o
  motivo.
- [x] 7.3 Wiring no `Program.cs`:
  - `AddD365GoodsInvoiceSource()`;
  - o source XML segue registrado;
  - resolver e router scoped.

  Atualizar os comentários do bloco do feed: a fila de descoberta agora tem consumidor.
- [x] 7.4 Criar `tests/FiscalHub.Integration.Tests` (no `FiscalHub.slnx`), com `InternalsVisibleTo` nos
  adapters Messaging.ServiceBus, Ingress.D365Poll e Inbound.Xml. A composição é real:
  - consumidor → router → esteira com resolver → sources D365 e XML reais;
  - HTTP do F&O por fixture e leitor de Blob falso;
  - store, trace e despachante falsos.

  Os testes:
  - **NF-e descoberta:** a mensagem da `documents-discovered` com uma nota `55` gravada chega montada à
    esteira. A foto do domínio tem os itens e impostos, e a nota fica rejeitada por "tributos da Reforma
    ausentes". Esse é o desfecho real da base, e o despachante não é chamado.
  - **NFS-e descoberta:** fica `Ignored`, com zero requisições HTTP.
  - **Duas origens no tenant-a na mesma execução:** o XML pela `documents-in` com origem `Xml` e o D365
    pela `documents-discovered` vão cada um ao seu source.
  - **Mensagem sem origem** do tenant-a (perfil `Dynamics365`) vai para o source do D365.
  - **Mesma referência duas vezes, sem mudança:** o mesmo hash nas duas montagens.
- [x] 7.5 Documentação:
  - **`docs/RUNNING.md`:** seção "Montagem do D365 (fatia 2)" com o roteiro:
    - ligar o poll do tenant-a, como na fatia 1;
    - acompanhar o consumo da `documents-discovered`;
    - o desfecho esperado no fiscosysdev: 5 notas `55` rejeitadas, 9 `SE` ignoradas, 0 enviadas, e por
      quê;
    - onde ver a foto da fonte canônica;
    - a supressão de republicação: onde ver o `ReferencesSuppressed` no log da passada, e quando a
      repetição volta (reinício, troca de réplica, rebobinamento).
  - **`docs/STATUS.md`, próximos passos:**
    - **fatia de nota de serviço** (domínio de serviço: CCM, município de prestação, ISS, item da lista);
    - entidade de `CClassTribTable_BR` no pacote D365;
    - despacho de cancelamento, com os status configuráveis do ADR-0023;
    - impostos e encargos no parser do XML;
    - rename do projeto `Ingress.D365Poll`;
    - dead-letter imediata com motivo;
    - concorrência do consumidor com serialização por documento;
    - agrupamento do dashboard em nota de entrada;
    - persistir o registro de publicações do poller, se reinício ou troca de réplica pesarem.
- [x] 7.6 Executar o roteiro contra o fiscosysdev. Registrar no PR:
  - por passada, referências publicadas contra suprimidas;
  - quantas mensagens a `documents-discovered` recebeu e quantas montagens houve por nota em regime,
    com meta de ~1 (eram ~6 sem a supressão);
  - os desfechos por tipo;
  - quantos GETs por montagem, com e sem complemento;
  - que as montagens repetidas da mesma nota, depois de um reinício, deram o mesmo hash.
- [x] 7.7 Rodar `dotnet build` (0 warnings), `dotnet test` e `openspec validate add-d365-document-assembly --strict`,
  tudo verde.
