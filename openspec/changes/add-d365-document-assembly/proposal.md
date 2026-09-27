## Why

A fatia `add-d365-change-feed-polling` já publica na fila `documents-discovered` uma referência por
documento que mudou no F&O, mas essa fila ainda não tem consumidor. Esta fatia fecha o caminho: consome
a fila, monta o documento completo a partir das entidades do D365 e o entrega à esteira que já existe.

Duas coisas destravaram a montagem agora:

- **O PR #53** expôs impostos e encargos da nota por chave estrangeira declarada
  (`FSFiscalDocumentTaxTransBR`, `FSFiscalDocumentMiscChargeBR`). Com isso somem o par polimórfico
  RecId + TableId, a montagem em duas fases e a cadeia de `or` (d365/04 §9).
- **O `DocumentPipeline<TDocument>`** já faz idempotência por hash, foto do domínio, validação, envio e
  registro, e busca o documento por `IInboundSource<TDocument>`.

Por isso a montagem cabe inteira num `FetchAsync`, e o consumidor da fila de descoberta é só um segundo
gatilho sobre a mesma esteira. Não há segundo salto nem armazenamento intermediário.

## What Changes

- **Origem na referência.** `DocumentReference` ganha `Origin`, opcional, no mesmo desenho do
  `Trigger`: quando ausente, vale o `InboundAdapter` do perfil do tenant, o que mantém compatível
  qualquer mensagem já na fila. Quem publica preenche: o `ChangeFeedPoller` põe a origem do feed
  (`IDocumentChangeFeed.Origin`), e o BlobDrop, o `/ingest` e a descoberta local põem `Xml`. O perfil
  continua dizendo **o que varrer** para o tenant (`ListByInboundAdapterAsync`); a referência diz **de
  onde o documento veio**.
- **Supressão de republicação no poller.** Hoje a sobreposição (300s) sobre o intervalo (60s)
  redescobre cada nota cerca de 6 vezes, e cada redescoberta vira mensagem e montagem completa.
  - **O hash não evita isso:** a esteira busca antes de checar a idempotência, e tem que ser nessa ordem.
  - **O custo:** cerca de 27 GETs por nota em vez de 4 a 5. O teto pelo limite do F&O cai para cerca de
    45 notas/min, e o do consumidor serial para cerca de 8 notas/min.
  - **O que muda:** a página do feed passa a trazer o carimbo de alteração por referência e o horizonte
    estável da leitura. O `ChangeFeedPoller` descarta o par (tenant, origem, documento, carimbo) já
    publicado e assentado. Isso vale para qualquer feed.
  - **Estado:** em memória, por réplica, podado pela janela e zerado quando a marca regride. Perder o
    estado só volta ao comportamento de hoje. Em regime, cada alteração vira cerca de 1,15 mensagem.
  - **ADR-0024:** a alternativa que ele rejeitou volta. A premissa era "o roteamento absorve os
    repetidos barato", e ela não vale, porque a busca vem antes do hash.
- **Resolver de origem** (`IInboundSourceResolver<TDocument>`, Application). Devolve o
  `IInboundSource` cuja `Origin` casa com a da referência. Falha de forma clara, nomeando a origem,
  quando nenhum adapter registrado atende. A esteira passa a resolver o source por documento em todos
  os caminhos. O seed de dev e o fluxo XML ficam como estão.
- **Consumidor da `documents-discovered`** (Messaging.ServiceBus), com roteamento por tipo na
  Application:
  - NF-e de mercadoria vai para a esteira;
  - NFS-e e CT-e saem com o desfecho explícito **"ignorado: tipo fora do escopo"**;
  - a dead-letter da fila de descoberta também vira registro visível (ADR-0010).
- **Desfecho `Ignored`** no `IntegrationStatus` e no `IProcessingStore`, com o motivo. O dashboard ganha
  o rótulo, fora da lista de falhas.
- **Source do D365** (`IInboundSource<GoodsInvoice>`, origem `Dynamics365`). Monta a nota em 4 GETs por
  documento, sem `$batch`:
  - cabeçalho;
  - linhas;
  - impostos, com filtro de dois termos fixos:
    `FiscalDocumentRecId eq {rec} or MiscChargeFiscalDocumentRecId eq {rec}`;
  - encargos.

  Os cadastros de referência ficam em cache por tenant, com expiração por tempo.
- **A entidade fiscal de impostos é a fonte.** Sinal, CST, base, alíquota, valor e a classificação
  (linha, encargo, retenção) vêm prontos dela. O complemento contábil (`FSTaxTransBR`, pela ponte
  `TaxTransRecId`) entra só para tipos de imposto conhecidos, em que a fiscal vem zerada. O único caso
  hoje é o `ImportTax`. Divergência não prevista entre as duas falha o documento de forma visível, e
  nunca escolhe um lado em silêncio.
- **Hash de conteúdo canônico.** O D365 não tem um "cru" único, e sim N respostas JSON. A impressão é
  calculada sobre um JSON canônico:
  - entidades em ordem fixa;
  - coleções ordenadas pelo próprio RecId;
  - **sem** `@odata.*` e sem `SysModifiedDateTime`.

  Um toque de auditoria no registro não reenvia a nota. Esse mesmo canônico é a foto da fonte (ADR-0006).
- **Locator do D365 carrega o RecId**: `d365/<dataAreaId>/<FiscalDocumentRecId>`. O cabeçalho passa a
  ser lido pela chave primária, e não por `Voucher`, que não lidera nenhum índice. O Locator é o contrato
  que a fatia anterior deixou para esta.
- **Domínio `Goods` cresce de forma aditiva**:
  - impostos do item além de IBS/CBS;
  - retenções do item, separadas dos impostos;
  - encargos do item, com os impostos deles.

  O grupo IBS/CBS do item passa a ser opcional: nota sem IBS/CBS é montada sem o grupo, e não com zeros
  inventados. O `GoodsInvoiceValidator` rejeita esse caso com motivo claro.
- **Só nota autorizada é montada.** Com `Model ≠ 55` ou `Status ≠ Approved` na hora da montagem, a nota
  sai como ignorada, com o motivo. Documento que sumiu vai para a dead-letter.
- **ADR-0025** registra essas decisões.

## Capabilities

### New Capabilities

- `inbound-source-resolution`: a origem opcional na referência, quem a preenche, o resolver (origem da
  referência, depois o perfil), o erro claro para origem sem adapter e a esteira resolvendo o source por
  documento.
- `discovery-queue-consumer`: o consumidor da `documents-discovered`, o roteamento por tipo, o desfecho
  "ignorado" registrado, o fora-de-escopo detectado na montagem e a dead-letter visível da fila de
  descoberta.
- `d365-document-assembly`: o `IInboundSource<GoodsInvoice>` do D365. Cobre:
  - o Locator e as 4 chamadas;
  - a verificação do cabeçalho (sumiu, chave, modelo, status);
  - imposto de linha, de encargo e retenção no lugar certo;
  - o complemento contábil e a regra de divergência;
  - o cache de cadastros;
  - o hash canônico e a foto da fonte;
  - o documento sem IBS/CBS rejeitado na validação;
  - os testes contra respostas gravadas do ambiente.

### Modified Capabilities

- `change-feed-polling`:
  - no requisito "Enfileiramento na fila de descoberta", a referência passa a levar a origem do feed, e
    a fila deixa de ser "sem consumidor";
  - no requisito "Sobreposição na janela de consulta", o repetido deixa de ser reenfileirado sem
    filtro;
  - entra o requisito "Supressão de par já publicado".
- `d365-change-feed`:
  - no requisito "Mapeamento do cabeçalho para referência", o Locator passa a
    `d365/<dataAreaId>/<FiscalDocumentRecId>`;
  - entra o requisito "Carimbo de alteração e horizonte estável".

## Non-goals

- **Nota de serviço (NFS-e).** Sai como "ignorado: tipo fora do escopo". Entra no roadmap como **fatia
  própria, a seguinte**: domínio de serviço com CCM, município de prestação, ISS e item da lista de
  serviços.
- **Modelo 01.** Continua fora do mapa de modelos, com aviso no feed (ADR-0024).
- **App registration e client credentials.** O código existe, mas nunca rodou; todo teste real usa a
  identidade delegada do Azure CLI.
- **Qualquer mudança no payload da Avalara.** O objetivo é o documento chegar montado à esteira. O
  mapeamento para a Avalara não lê os campos novos do domínio.
- **Despacho de cancelamento e de outros status.** A esteira só conhece a operação "emitida", e o
  despacho de cancelamento não existe no adapter da Avalara. Status configurável por tenant (ADR-0023)
  vai junto com essa fatia.
- **Resolver o `cClassTrib`.** A entidade fiscal de impostos não o traz. Na `FSTaxTransBR` ele é um
  RecId para `CClassTribTable_BR`, sem entidade que o resolva. Precisa de entidade nova no pacote D365.
- **Preencher os campos novos do domínio a partir do XML.** Isso fica para outra fatia, e o parser não
  muda.
- **Descoberta por período e reprocesso manual do D365** (`IDocumentDiscovery` do D365).
- **`$batch`, chamadas em paralelo e ajuste de concorrência do consumidor.**
- **Cadastros que nenhum campo do domínio consome ainda**: `FSItemBR`, `FSUnitOfMeasureBR`,
  `FSCountryRegionBR` e `FSFiscalDocModelBR`. O cache é genérico, e cada cadastro entra com o campo que
  o usa.
- **Renomear o projeto `Ingress.D365Poll`**, que passa a conter também o source.
- **Dead-letter imediato com motivo** para erro permanente. Continua valendo o retry nativo do ADR-0004.
- **Persistir o registro de pares publicados.** A primeira versão fica em memória: um reinício ou uma
  troca de réplica volta, por uma janela, ao custo de hoje, que é caro e correto. Persistir sobrevive a
  isso, ao custo de uma tabela a mais.
- **Encurtar a sobreposição para economizar tráfego.** Isso troca custo por risco de perder nota.
- **Mover a checagem de idempotência para antes da busca.** O hash depende do conteúdo buscado.

## Impact

- **Domain**: `Goods` ganha `TaxLine`, `TaxKind`, `ItemCharge` e `ChargeKind`. `GoodsInvoiceItem` ganha
  `Taxes`, `Withholdings` e `Charges`, que começam vazios. `ReformTaxes` deixa de ser obrigatório no
  item. Nenhuma dependência nova.
- **Application**:
  - `Inbound`: `DocumentReference.Origin`, `IInboundSourceResolver<T>` com a implementação por perfil,
    `InboundSourceNotFoundException` e `DocumentOutOfScopeException`;
  - `ChangeFeedPoller` passa a preencher a origem e a suprimir o par já publicado;
  - `ChangeFeedPage` troca `References` por `Items` (referência mais carimbo) e ganha o horizonte
    estável;
  - entram `ChangeFeedItem` e `ChangeFeedPublicationLog` (singleton, em memória);
  - `ChangeFeedPassSummary` ganha `ReferencesSuppressed`;
  - `Pipeline`: a `DocumentPipeline` passa a receber o resolver, e entra o `DiscoveredDocumentRouter`;
  - `IProcessingStore.RecordIgnoredAsync` e `IntegrationStatus.Ignored`;
  - `GoodsInvoiceValidator` rejeita item sem IBS/CBS.
- **Infrastructure**: `SqlProcessingStore.RecordIgnoredAsync`. O status é gravado como texto
  (`HasConversion<string>`), então não há migration.
- **Adapters**:
  - `Ingress.D365Poll`:
    - `D365GoodsInvoiceSource`;
    - cliente OData compartilhado com o feed (a lógica de envio e throttling sai do `D365ChangeFeed`);
    - cache de cadastros;
    - canonicalizador;
    - Locator novo no feed;
    - carimbo e horizonte estável no feed, com `D365ChangeFeedOptions.StampSettleMargin` (padrão 10s).
  - `Messaging.ServiceBus`: consumidor e dead-letter da `documents-discovered`, com a casca parametrizada
    por fila.
  - `BlobDrop` e `Discovery.Local`: preenchem `Origin = Xml`.
- **Host**: registra o source D365, o resolver, o consumidor da fila de descoberta e o
  `ChangeFeedPublicationLog` como singleton. O `/ingest` põe `Origin = Xml`.
- **Dashboard**: status `Ignored` em `types.ts` e `StatusChip`.
- **Testes**:
  - fixtures gravadas do fiscosysdev por script opt-in, byte a byte;
  - fixtures derivadas, marcadas como tal, onde a base não tem o caso;
  - testes do source, do resolver, do roteador, do consumidor e do validador;
  - integração ponta a ponta (mensagem na fila de descoberta → documento montado → esteira chamada).
- **Docs**: `docs/adr/0025-*.md` e índice, nota no ADR-0024, `d365/04` §11 (pendências
  resolvidas e novas), roteiro em `docs/RUNNING.md` e próximos passos em `docs/STATUS.md`.
- **Sistema externo**: leitura OData no F&O. São 4 GETs por montagem, mais 1 quando há complemento
  contábil, e os cadastros no primeiro uso. Com a supressão, cada alteração é montada cerca de 1,15 vez
  em regime. Depois de reinício ou troca de réplica, volta a cerca de 6 vezes por uma janela de
  sobreposição. Sem escrita.
