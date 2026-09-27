## Context

A motivação está no proposal.md; os requisitos, nas specs `inbound-source-resolution`,
`discovery-queue-consumer` e `d365-document-assembly`, e nos deltas de `change-feed-polling` e
`d365-change-feed`. Aqui fica o estado atual que molda o desenho.

**Código.**

- **Um source só.** A `DocumentPipeline<TDocument>` recebe um único `IInboundSource<TDocument>` no
  construtor. O Host registra o `XmlGoodsInvoiceSource` como singleton. Registrar um segundo
  `IInboundSource<GoodsInvoice>` faria o DI entregar o último registrado para todos os caminhos.
- **Perfil do tenant-a.** O perfil diz `Dynamics365`, e o mesmo tenant roda o demo XML:
  - `/ingest` e BlobDrop;
  - `LocalDocumentDiscovery`, cuja `Origin` é `"Local"` e cujos locators apontam para XML no Blob;
  - reprocesso das notas 123 e 456.

  O tenant-b é `iScala`, sem adapter.
- **Fila de descoberta sem consumidor.** A `documents-discovered` foi registrada só com envio
  (`AddServiceBusDiscoveryQueue`). As cascas `ServiceBusTriggerService` e `DeadLetterTriggerService`
  leem o nome da fila de `ServiceBusOptions.QueueName`, ou seja, só a `documents-in`, e o
  `QueuedDocumentProcessor` chama `IDocumentPipeline<GoodsInvoice>` direto. No emulador, as duas filas
  têm `MaxDeliveryCount = 5`, TTL de 1 hora e `LockDuration` de 1 minuto.
- **Locator de hoje.** O `D365ChangeFeed` monta `d365/<dataAreaId>/<Voucher>`. O `$select` do feed já
  traz o `FiscalDocumentRecId`.
- **Página do feed e poller.**
  - A `ChangeFeedPage` traz só `References` e `HighWatermark`. O carimbo de cada documento
    (`SysModifiedDateTime`) fica no adapter e não chega ao poller.
  - O `ChangeFeedPoller` é registrado scoped, e o `ChangeFeedPollingService` abre um escopo por tick de
    15s. Qualquer estado guardado no poller morre a cada tick.
  - Nas respostas vistas até aqui, o literal de `SysModifiedDateTime` não tem fração de segundo
    (`2017-01-21T21:23:19Z`); confirmar nas fixtures.
- **Repetição da sobreposição.** O ADR-0024 rejeitou filtrar os repetidos no poller, por ser "quarto
  esquema de idempotência". A premissa era que "a fatia de roteamento pode absorver os repetidos barato".
  Esta fatia mostrou que não pode: a `DocumentPipeline` busca antes de checar o hash, e tem que ser
  nessa ordem, porque o hash é do que foi buscado.
- **Domínio `Goods`.** O item só modela IBS/CBS/IS (`ReformTaxes`, obrigatório), e não há lugar para
  ICMS/IPI/PIS/COFINS/II, retenção ou encargo. O `NfeXmlParser` exige o grupo `IBSCBS`, e o
  `GoodsInvoiceValidator` exige CST e `cClassTrib` da Reforma em todo item.
- **Adapter da Avalara.** O `GoodsInvoiceToAvalara` lê só o destinatário e, dos itens, número, CFOP,
  total e `ReformTaxes`. O `DispatchContext.Operation` (`Issued`/`Cancelled`) existe, mas o adapter da
  Avalara não o lê: despacho de cancelamento não existe.
- **Status gravado como texto.** O `ProcessedDocument.Status` é gravado com `HasConversion<string>` e
  tamanho 20. Um valor novo no enum não pede migration.
- **Retry.** Vale o nativo do Service Bus (ADR-0004): exceção abandona a mensagem, a reentrega é
  imediata e, no limite, ela vai para a dead-letter. O `DeadLetterTriggerService` grava o
  `DeadLetterReason`, que numa mensagem esgotada é `MaxDeliveryCountExceeded`, e não a causa.

**Entidades (metadado do pacote, `d365/model/AxDataEntityView`).**

- `FSFiscalDocumentTaxTransBR` tem:
  - `FiscalDocumentTaxTransRecId`, `FiscalDocumentLineRecId`, `FiscalDocumentMiscChargeRecId` e
    `TaxTransRecId`;
  - `FiscalTaxType` (← `Type`), `TaxationCode`, `TaxBaseAmount`, `TaxValue`, `TaxAmount` e
    `RetainedTax`;
  - `FiscalDocumentRecId` (pela linha) e `MiscChargeFiscalDocumentRecId` (pelo encargo → linha).

  **Não tem `CClassTrib`.**
- `FSTaxTransBR` tem `CClassTrib`, mas é FK para `CClassTribTable_BR.RecId` (d365/04 §4.3), sem entidade
  que resolva o código.
- `FSFiscalDocumentMiscChargeBR` tem `FiscalDocumentMiscChargeRecId`, `FiscalDocumentLineRecId`,
  `FiscalDocumentRecId`, `ChargeNum`, `MiscChargeType`, `Amount` e `Txt`.
- O cabeçalho traz o snapshot das partes (`FiscalEstablishment*`, `ThirdParty*`, endereços por RecId).
  A linha traz `FiscalClassification` (NCM) e `CFOP`.
- No F&O, FK vazia vem como `0`, e enum vem no JSON pelo nome (`"ImportTax"`, `"Yes"`, `"Approved"`).

**Dados (medidos no fiscosysdev; evidência, não retestar).**

| Dado | Valor |
|---|---|
| Documentos | 83: `01` = 69, `SE` = 9, `55` = 5 (todos de 2016) |
| Impostos | 547, os 83 documentos alcançados, zero órfãos, 6,6 por nota |
| Encargos | 14, todos `Others`, 14 de 14 ligados a linha e documento, `MarkupTrans` nulo |
| Retenções | 12 (11 IRRF, 1 ISS), na mesma chamada dos impostos |
| Fiscal × contábil | 547 casadas 1:1, 439 idênticas, 351 com sinal invertido, 54 com CST do IPI diferente (`01→51`, `05→55`) |
| Zerados de um lado | 54: 26 na fiscal (`ImportTax` 8, IPI 05 6, PIS/COFINS 98/99 8, ICMS/ICMSDiff 90 4), 28 na contábil (PIS/COFINS CST 98) |
| Contábil sem par fiscal | 0: as 547 linhas contábeis dos 83 vouchers casam 1:1 com a fiscal |
| Imposto → encargo | nenhum dos 547 aponta para encargo |

## Goals / Non-Goals

**Goals:**

- A esteira não muda de forma: idempotência, fotos, validação, envio e registro seguem iguais. O que
  muda é de onde vem o source e o que chega nele.
- Nenhum valor fiscal é escolhido em silêncio. Onde fiscal e contábil divergem fora do padrão conhecido,
  o documento para, de forma visível.
- A mesma nota dá a mesma impressão enquanto nada fiscal mudar.
- O que a base real exercita é testado com a resposta real; o que ela não exercita é dito, não fingido
  (ver "O que a base não exercita").
- `dotnet test` verde e sem rede; o teste contra o ambiente é opt-in.
- Em regime, uma alteração na origem vira cerca de uma mensagem e uma montagem, sem encolher a
  sobreposição. A supressão não pode engolir uma alteração real enquanto a diferença entre os relógios
  da origem ficar dentro da margem (D16).

**Non-Goals:**

- Fazer uma nota do D365 chegar à Avalara nesta fatia. As notas `55` da base são anteriores à Reforma, e
  as que tiverem IBS/CBS ficam sem `cClassTrib` (D7). O desfecho visível hoje é "rejeitada na validação".
- Zerar a repetição em qualquer cenário. Depois de reinício, troca de réplica ou rebobinamento, a
  repetição volta por uma janela (D16). Persistir o registro fica para quando isso pesar.

## Decisions

### D1. A origem viaja na referência; o perfil é o fallback

Esta é a decisão do usuário (resposta à pergunta de resolver), e fica registrada aqui com os detalhes de
implementação.

```csharp
public sealed record DocumentReference
{
    // ... campos atuais ...
    /// Origem que sabe buscar o documento (ex.: "Dynamics365", "Xml"). Ausente = InboundAdapter do perfil.
    public string? Origin { get; init; }
}

public interface IInboundSourceResolver<TDocument>
{
    Task<IInboundSource<TDocument>> ResolveAsync(DocumentReference reference, CancellationToken ct = default);
}
```

- **Implementação única** (`InboundSourceResolver<TDocument>`, Application). Recebe
  `IEnumerable<IInboundSource<TDocument>>` e `IConnectorProfileStore`. A origem é
  `reference.Origin ?? profile?.InboundAdapter`, com comparação exata contra `IInboundSource.Origin`.
  Sem origem nem perfil, ou sem source que atenda, lança `InboundSourceNotFoundException`, com origem e
  tenant na mensagem. É erro permanente, mas segue o retry nativo; sem esquema novo.
- **A `DocumentPipeline`** passa a receber o resolver no lugar do source e resolve na primeira linha do
  `ProcessAsync`. É a única mudança na esteira.
- **Quem preenche a origem:**
  - o `ChangeFeedPoller`, com `reference with { Trigger = Event, Origin = _feed.Origin }`. É genérico:
    qualquer feed ganha a origem sem depender do adapter;
  - o BlobDrop, o `/ingest` e o `LocalDocumentDiscovery` (`DiscoverAsync` e `FindByKeyAsync`), com
    `"Xml"`.

  A origem do `LocalDocumentDiscovery` (`"Local"`) **não** é usada: ela identifica a descoberta, e o
  documento que ela aponta é XML.
- **O literal `"Xml"` nos três produtores.** BlobDrop e Discovery.Local não podem referenciar o adapter
  Inbound.Xml, e um tipo de "origens conhecidas" na Application levaria nomes de adapter para o núcleo.
  Cada produtor tem teste afirmando `Origin == "Xml"`, e o teste do resolver afirma que o
  `XmlGoodsInvoiceSource.Origin` é `"Xml"`. Um erro de digitação vira falha clara do resolver, e não
  desvio silencioso.
- **O perfil continua respondendo "o que varrer"** (`ListByInboundAdapterAsync`), sem mudança.

**Alternativas.** Foram consideradas: resolver só no gatilho de descoberta (duas composições da
esteira); e perfil estrito em todos os caminhos, que obrigaria a corrigir o seed de dev. O usuário
escolheu a origem na referência: o perfil diz o que varrer, a referência diz de onde veio.

### D2. Roteamento por tipo na Application, com desfecho `Ignored`

```csharp
public interface IDocumentRouter
{
    Task RouteAsync(DocumentReference reference, DispatchContext context, CancellationToken ct = default);
}
```

O `DocumentRouter` (Application/Pipeline) roteia assim:

- `GoodsInvoice55` vai para `IDocumentPipeline<GoodsInvoice>.ProcessAsync`;
- qualquer outro tipo vai para `IProcessingStore.RecordIgnoredAsync(reference, "ignorado: tipo fora do escopo (<Type>)")`,
  sem tocar a origem;
- a chamada à esteira fica num `try/catch` de `DocumentOutOfScopeException` (Application/Inbound), que
  qualquer source lança com o motivo. Ao pegá-la, o router grava `Ignored` com o motivo e retorna
  normalmente, e a mensagem é concluída.

O `FetchAsync` é o primeiro passo da esteira, então nada foi gravado antes da exceção.

- **`IntegrationStatus.Ignored`** entra no fim do enum. O `SqlProcessingStore.RecordIgnoredAsync` usa o
  `UpsertAsync` existente, que preserva o `ExternalId` e zera as tentativas. O `AlreadyProcessedAsync`
  não muda: só `Submitted`/`Confirmed` bloqueiam.
- **Os dois consumidores usam o router** (`documents-in` e `documents-discovered`). Para o XML nada muda,
  porque todo produtor XML publica `GoodsInvoice55`. E o reprocesso futuro de nota D365 pela
  `documents-in` herda o tratamento de fora do escopo.

**Alternativas.**

- Checar o tipo dentro do consumidor do Service Bus: é regra de caso de uso no adapter de transporte.
- Gravar "ignorado" como `IntegrationError` com motivo: polui os KPIs de falha (`FAILURE_STATUSES` do
  dashboard).
- Só logar, como o modelo `01` hoje no feed: o ADR-0024 pede desfecho explícito gravado, e a NFS-e,
  diferente do `01`, está no mapa e chega à fila.

### D3. Consumidor e dead-letter da fila de descoberta: a mesma casca, parametrizada

As cascas `ServiceBusTriggerService` e `DeadLetterTriggerService` passam a receber o nome da fila no
construtor. Cada fila ganha a sua instância, e o `QueuedDocumentProcessor` passa a chamar o
`IDocumentRouter`. O `AddServiceBusDiscoveryQueue` passa a registrar o consumidor e a dead-letter da
`documents-discovered`, e o comentário "sem consumidor" sai.

- **Registro.** Duas instâncias do mesmo tipo de hosted service MUST ser registradas com
  `AddSingleton<IHostedService>(sp => ...)`. O `AddHostedService<T>(factory)` usa `TryAddEnumerable`
  e descarta o segundo registro do mesmo tipo.
- **`MaxConcurrentCalls = 1`.** Continua como na `documents-in`, agora por um motivo explícito: as cópias
  residuais da sobreposição chegam em sequência na mesma fila. São as que a supressão do D16 não corta:
  par quente, reinício, troca de réplica e rebobinamento. Em paralelo, duas cópias passariam juntas pelo
  `AlreadyProcessedAsync`, com nenhuma ainda terminal, e a nota iria duas vezes para o destino. Subir a
  concorrência exige antes serializar por documento (sessão por chave natural, por exemplo): é
  non-goal.

### D4. O Locator carrega o RecId, e a chave natural é conferida

O Locator passa a ser `d365/<dataAreaId>/<FiscalDocumentRecId>`, com o `dataAreaId` codificado.

- **Por quê.** O cabeçalho passa a ser lido pela chave primária (`RecId`, índice clusterizado). Por
  `dataAreaId + Voucher` seria varredura: o `Voucher` é o 6º campo do índice único da `FiscalDocument_BR`
  e não lidera nenhum outro. Além disso, o voucher pode repetir entre exercícios (ADR-0024 §6).
- **Conferência.** O source confere `dataAreaId|Voucher` do cabeçalho contra a `NaturalKey` da
  referência. Isso protege contra uma mensagem no formato antigo cujo voucher seja só dígitos e seria
  lido como RecId. O formato antigo com voucher alfanumérico falha já na leitura do Locator.
- **Mensagens antigas.** Não há o que migrar: a fila nunca teve consumidor e o TTL é de 1 hora.

**Alternativa.** Manter o Locator e ler por voucher: sem mudança de contrato, mas é varredura por
documento e fica ambíguo se o voucher repetir.

### D5. O source vive no adapter do D365, com cliente OData compartilhado

`D365GoodsInvoiceSource : IInboundSource<GoodsInvoice>`, com `Origin = D365ChangeFeed.OriginName`
(`"Dynamics365"`), entra no projeto `FiscalHub.Adapters.Ingress.D365Poll`. É o mesmo adapter do perfil
(`InboundAdapter = "Dynamics365"`), com as mesmas settings, o mesmo token e a mesma rotina de envio com
throttling. Renomear o projeto é mecânico e fica fora desta fatia, para não misturar rename com
feature.

- **`D365ODataClient` (internal).** Extraído do `D365ChangeFeed.SendAsync`, com o mesmo comportamento:
  - bearer;
  - `Accept: application/json`;
  - 429 e 503 com `Retry-After` até o teto, e um máximo de tentativas.

  Ganha `GetAllAsync`, que segue o `@odata.nextLink` para as coleções filhas. O conjunto é o de um
  documento lançado, e não uma janela móvel; o argumento do ADR-0024 contra offset não se aplica. O feed
  passa a usar o cliente, e os testes dele não mudam.
- **Peças internas, cada uma testável sozinha:**
  - `D365DocumentLocator` (leitura do Locator);
  - `D365Canonicalizer` (D11);
  - `D365ReferenceDataCache` (D13);
  - `D365GoodsInvoiceAssembler`: função pura das linhas de resposta mais os lookups de cadastro →
    `GoodsInvoice`. Cobre D7 a D10.
- **Ordem do `FetchAsync`:**
  1. Locator e settings.
  2. Cabeçalho e verificação (D6).
  3. Linhas, impostos e encargos.
  4. Complemento, se elegível (D9).
  5. Canônico e hash.
  6. Foto da fonte.
  7. Cadastros e montagem.
- **Chamadas em sequência**, sem paralelo e sem `$batch`: a ordem fica determinística para o stub HTTP
  sequencial que os testes já usam (`SequencedHttpMessageHandler`). Paralelizar é otimização de latência
  e fica para depois de medir.
- **Nenhum enum em `$filter`.** Os quatro filtros são por RecId (int64), e o do complemento é por
  voucher e empresa (string). A classificação por `FiscalTaxType`, `RetainedTax` e `MiscChargeType` é em
  memória. Se algum filtro futuro precisar de enum, é pelo nome qualificado
  (`Microsoft.Dynamics.DataEntities.TaxType_BR'IPI'`).

**`$select` por entidade** (o que a montagem lê, mais as chaves; é também o que entra no hash):

| Entidade | Campos |
|---|---|
| `FSFiscalDocumentBRs` | `FiscalDocumentRecId`, `dataAreaId`, `Voucher`, `Model`, `Status`, `Direction`, `FiscalDocumentIssuer`, `AccessKey`, `FiscalDocumentSeries`, `FiscalDocumentNumber`, `FiscalDocumentDate`, `FiscalDocumentDateTime`, `FiscalEstablishmentCNPJCPF`, `FiscalEstablishmentName`, `FiscalEstablishmentIE`, `FiscalEstablishmentPostalAddress`, `ThirdPartyCNPJCPF`, `ThirdPartyName`, `ThirdPartyIE`, `ThirdPartyPostalAddress`, `TotalAmount` |
| `FSFiscalDocumentLineBRs` | `FiscalDocumentLineRecId`, `FiscalDocumentRecId`, `LineNum`, `ItemId`, `Description`, `FiscalClassification`, `CFOP`, `Quantity`, `UnitPrice`, `LineAmount` |
| `FSFiscalDocumentTaxTransBRs` | `FiscalDocumentTaxTransRecId`, `FiscalDocumentLineRecId`, `FiscalDocumentMiscChargeRecId`, `TaxTransRecId`, `FiscalTaxType`, `TaxationCode`, `TaxBaseAmount`, `TaxBaseAmountExempt`, `TaxBaseAmountOther`, `TaxValue`, `TaxAmount`, `RetainedTax` |
| `FSFiscalDocumentMiscChargeBRs` | `FiscalDocumentMiscChargeRecId`, `FiscalDocumentLineRecId`, `ChargeNum`, `MiscChargeType`, `Amount`, `Txt` |
| `FSTaxTransBRs` (complemento) | `TaxTransRecId`, `Voucher`, `TaxType`, `TaxBaseAmount`, `TaxValue`, `TaxAmount` |
| `FSPostalAddressBRs` (cache) | `PostalAddressRecId`, `CityRecId` |
| `FSAddressCityBRs` (cache) | `AddressCityRecId`, `IBGECode` |

### D6. Verificação do cabeçalho: vazio é falha, e só a nota autorizada segue

- **Sem cabeçalho → exceção**, que vai para retentativa e dead-letter, e **não** "ignorado". Com
  `cross-company=true`, a OData devolve vazio, e não 403, quando o usuário de integração perde acesso à
  empresa. Um "ignorado" por vazio esconderia uma quebra de permissão atrás de centenas de desfechos
  normais. A mensagem da exceção diz isso.
- **`Model ≠ "55"` → `DocumentOutOfScopeException("ignorado: tipo fora do escopo (modelo <X>)")`.**
- **`Status ≠ "Approved"` → `DocumentOutOfScopeException("ignorado: status <X> fora do escopo (só nota autorizada é despachada)")`.**
  A esteira só conhece "emitida" (`Operation = Issued` fixo no consumidor), e a Avalara não despacha
  cancelamento. Montar uma cancelada e mandar como emitida seria erro fiscal. Os status que o legado
  aceita (`Discarded`, `Rejected`, `RejectedNoFix` como `cod_sit 05`) e a regra por tenant do ADR-0023
  entram com a fatia de cancelamento.
- **Nota enviada e depois cancelada.** O hash muda, porque o `Status` está no canônico, e a nota é
  redescoberta. O source a declara fora do escopo, e o registro passa de `Confirmed` a `Ignored`, com
  motivo e o `ExternalId` preservado. É o desfecho mais honesto possível sem despacho de cancelamento
  (ver Riscos).
- **Cabeçalho sem linha** não é caso especial: o documento é montado com zero itens, e o validador
  existente rejeita com "A nota não possui itens." O F&O grava cabeçalho e linhas na mesma transação, e
  a leitura OData é comprometida; não há janela transitória a absorver.

### D7. O domínio cresce de forma aditiva, e IBS/CBS ausente não vira zero

```csharp
public enum TaxKind { Icms, IcmsSt, IcmsDiff, Ipi, Pis, Cofins, ImportTax, Iss, Irrf, Inss, InssRetained, InssCprb, Csll, Other }
public enum ChargeKind { Freight, Insurance, Other }

public sealed record TaxLine
{
    public required TaxKind Kind { get; init; }
    public string? Cst { get; init; }
    public required decimal TaxBase { get; init; }
    public required decimal Rate { get; init; }
    public required decimal Amount { get; init; }
    public decimal ExemptBase { get; init; }
    public decimal OtherBase { get; init; }
}

public sealed record ItemCharge
{
    public required int Number { get; init; }
    public required ChargeKind Kind { get; init; }
    public required decimal Amount { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<TaxLine> Taxes { get; init; } = [];
    public IReadOnlyList<TaxLine> Withholdings { get; init; } = [];
}

// GoodsInvoiceItem ganha:
public ReformTaxes? ReformTaxes { get; init; }                 // deixa de ser required
public IReadOnlyList<TaxLine> Taxes { get; init; } = [];        // exceto IBS/CBS
public IReadOnlyList<TaxLine> Withholdings { get; init; } = []; // RetainedTax = Yes
public IReadOnlyList<ItemCharge> Charges { get; init; } = [];
```

- **`TaxKind` é 1:1 com o `TaxType_BR`**, menos `CBS`, `IBSCity` e `IBSState`, que vão para o grupo
  da Reforma, e menos `Blank`, que falha. Não há fusão (`INSSRetained` não vira `Inss`), porque fundir
  seria inventar regra. São nomes de tributos nacionais, não do F&O, e por isso podem ficar no domínio.
- **Retenção em coleção separada** faz com que seja impossível somá-la aos impostos por engano. É isso
  que a spec pede com "não imposto normal".
- **Encargo no item**, e não na nota: a FK do encargo é a linha (`FiscalDocumentLineRecId`), e a NF-e
  também leva frete, seguro e outras despesas por item.
- **Sem proveniência no domínio.** Nenhum campo diz "veio do contábil". A rastreabilidade disso é a foto
  da fonte (D12), que traz os dois lados; o domínio segue sem saber de onde veio.
- **`ReformTaxes` opcional.**
  - O zero inventado (alíquota 0, valor 0, CST vazio) ficaria na foto do domínio como se a nota tivesse
    IBS/CBS zerado.
  - O `GoodsInvoiceValidator` rejeita o item com `ReformTaxes == null` ("tributos da Reforma (IBS/CBS)
    ausentes").
  - O `GoodsInvoiceToAvalara` ganha um guard que lança se chegar nulo. O caso é inalcançável depois da
    validação, e o payload não muda.
  - O `NfeXmlParser` continua preenchendo o grupo sempre.
- **Grupo IBS/CBS vindo do D365.**
  - Com os três tipos presentes: CST do `TaxationCode` (igual nos três, senão falha), base (igual nos
    três, senão falha), parcelas por `TaxValue`/`TaxAmount`, e `IbsTotalAmount = IBSState + IBSCity`,
    que é a definição do `vIBS` no leiaute.
  - `ClassTrib = ""`: a entidade fiscal não tem o campo, e a contábil tem só um RecId sem entidade que o
    resolva. O validador rejeita com "cClassTrib ausente".
  - Presença parcial falha.

**Alternativa.** Uma lista genérica de impostos com flag `Withheld`: menos tipos, mas soma errada fica a
um `Where` esquecido de distância.

### D8. Onde cada imposto cai

O `D365GoodsInvoiceAssembler` indexa linhas e encargos pelo RecId e distribui cada imposto:

1. **Chaves.** Com as duas FKs vazias ou as duas preenchidas, falha.
2. **Destino.** O destino é a linha, pela FK de linha, ou o encargo, pela FK de encargo. Se não estiver
   no documento, falha (órfão).
3. **Reforma.** `CBS`, `IBSState` e `IBSCity` vão para o grupo IBS/CBS. Se um deles estiver retido ou
   ligado a encargo, falha: é caso não previsto.
4. **Retenção.** Com `RetainedTax == "Yes"`, vai para as `Withholdings` do destino.
5. **Senão**, vai para os `Taxes` do destino.

Os valores são os da fiscal, sem conversão de sinal nem de CST. Cada falha é uma exceção com os RecIds
envolvidos.

### D9. Complemento contábil: por tipo conhecido, sob demanda, campo a campo

- **Tipos elegíveis.** São uma constante no adapter, `{ ImportTax }`, e não settings de tenant. É
  comportamento do produto F&O, igual para todo cliente. Acrescentar um tipo exige evidência nova e uma
  nota no ADR-0025.
- **Linha elegível.** É a linha fiscal de tipo elegível com `TaxAmount == 0` e `TaxTransRecId ≠ 0`.
  Havendo pelo menos uma, o source faz **uma** consulta a mais:
  `FSTaxTransBRs?cross-company=true&$filter=Voucher eq '<voucher>' and dataAreaId eq '<empresa>'`.
  Casa pelo `TaxTransRecId`, em memória.
  - Por voucher, e não por `TaxTransRecId eq A or …`: a forma do filtro é fixa, sem cadeia de `or`, e o
    `TaxTrans` tem índice por voucher.
  - Só as linhas contábeis casadas entram no canônico.
- **Merge campo a campo** (`TaxBaseAmount`, `TaxValue`, `TaxAmount`): vale a fiscal se for diferente de
  zero, senão a contábil. Tudo o mais vem da fiscal. A regra é a tradução direta da evidência: "nunca
  dois valores diferentes, sempre zero contra valor".
- **IPI CST 05 zerado na fiscal não é elegível** e fica zerado. A fiscal é a fonte e, pela hipótese a
  confirmar com o fiscal junto com o par 51/55, IPI com CST de suspensão tem valor zero na nota. Um
  complemento genérico ("zero na fiscal → pega da contábil") levaria valor de IPI suspenso para o
  documento.
- **Sem complemento, 4 chamadas.** Nota sem `ImportTax` zerado não paga a quinta.

**Alternativas.**

- Buscar sempre a `FSTaxTransBR` (5 chamadas por nota): permitiria comparar tudo, mas custa 25% a mais
  em toda nota, para detectar divergências que não temos como tratar além de parar.
- Complemento genérico por zero: descartado pelo caso do IPI.

### D10. Divergência não prevista para o documento

Estas são as comparações que o complemento faz, e o que fugir do padrão para o documento com exceção
(spec "Divergência não prevista"):

- ponte sem par;
- tipo diferente;
- contábil negativa. Aceitar só não negativo evita inventar regra de sinal: a fiscal apresenta imposto
  positivo, e o `ImportTax` é de entrada;
- dois valores diferentes de zero e diferentes entre si.

A exceção segue o retry nativo até a dead-letter. Retry não conserta dado, mas a dead-letter é o caminho
visível que já existe (ADR-0010), e dead-letter imediata com motivo seria um refinamento do transporte
(non-goal). A causa fica no log do processador e na foto da fonte, salva antes da montagem, com os dois
lados.

Só se detecta divergência onde se busca a contábil. Os demais tipos não são comparados (D9).

### D11. Impressão canônica

```json
{"v":1,"header":{…},"lines":[…],"taxes":[…],"charges":[…],"accounting":[…]}
```

- **Estrutura.** Chaves de topo em ordem fixa, e `accounting` sempre presente (vazio sem complemento).
- **Registros.** Cada registro é reescrito com as propriedades em ordem ordinal, sem `@odata.*` e sem
  `SysModifiedDateTime`.
- **Coleções** ordenadas pelo próprio RecId: `FiscalDocumentLineRecId`, `FiscalDocumentTaxTransRecId`,
  `FiscalDocumentMiscChargeRecId` e `TaxTransRecId`.
- **Valores.** São escritos pelo `JsonElement.WriteTo`, que preserva o texto cru do número. As strings
  são reescapadas pelo `Utf8JsonWriter`, de forma determinística.
- **Hash.** `ContentFingerprint.Of(canonical)` (SHA-256), o mesmo usado pelo XML.
- **Cadastros fora.** A nota emitida é imutável, e o endereço e a cidade da época já estão no snapshot
  da nota. Mudança de cadastro não deve reenviar nota.
- **O `$select` explícito (D5) é o limite do hash.** Campo que a montagem não lê não entra na
  impressão. Mudar o `$select` ou o formato muda a impressão de toda nota redescoberta depois, que
  reintegra uma vez. O `"v"` torna essa mudança deliberada: quem mexe no formato sobe a versão e registra
  no ADR-0025.

### D12. Foto da fonte é o canônico

`IProcessingTrace.SaveSourceAsync(tenant, key, canonical, "json")` é chamado depois das consultas e antes
da montagem, como o XML salva antes do parse. O que foi fotografado é exatamente o que foi impresso:
diante de um "por que reenviou?", basta comparar duas fotos. Não há foto quando o source para no
cabeçalho, porque não há conteúdo.

A foto é regravada a cada montagem repetida (ver Riscos, "Montagem repetida pela sobreposição"), como
já acontece com o XML reentregue.

### D13. Cache de cadastros

- **O que entra.** Só o que o domínio consome: `FSPostalAddressBRs` (endereço → `CityRecId`) e
  `FSAddressCityBRs` (cidade → `IBGECode`), para o `Party.MunicipalityCode`.
  - `FSItemBR`, `FSUnitOfMeasureBR`, `FSCountryRegionBR` e `FSFiscalDocModelBR` não têm campo no domínio
    hoje. O NCM, a origem e o tipo vêm da linha (d365/04 §3.3), e o escopo por modelo é `Model eq '55'`.
    Cada um entra com o campo que o usar, pelo mesmo cache.
- **Chave e isolamento.** `(tenant, entity set, RecId)`. O cache é por tenant, mesmo que dois tenants
  apontem para o mesmo ambiente, porque credencial e permissão são por tenant.
- **Expiração.** Cache em memória singleton, com expiração absoluta sobre o `TimeProvider` do DI,
  `D365AssemblyOptions.ReferenceDataTtl` (padrão 1 hora), e só por tempo. O endereço no F&O é versionado
  (`ValidFrom`/`ValidTo`), e um RecId não muda de conteúdo; a cidade quase não muda. Uma hora é
  conservador.
- **Implementação.** Vale o `IMemoryCache`, que varre os vencidos, se ele aceitar o relógio injetado.
  Senão, um dicionário próprio com expiração e varredura. O requisito é TTL testável sem dormir e memória
  que não cresce com entrada vencida.
- **Não encontrado não vai para o cache.** O município fica ausente, com aviso, e a montagem segue.
- **Carga preguiçosa por RecId**, e não pré-carga da tabela de cidades: é simples, o custo frio é de até
  4 GETs por parceiro novo e o custo quente é zero.

### D14. Mapeamento cabeçalho/linhas → `GoodsInvoice`

| Domínio | Origem |
|---|---|
| `AccessKey`, `Model`, `Series`, `Number`, `TotalAmount` | `AccessKey`, `Model`, `FiscalDocumentSeries`, `FiscalDocumentNumber`, `TotalAmount` |
| `IssueDate` | `FiscalDocumentDateTime`; se vier o valor vazio do F&O (`1900-01-01`), `FiscalDocumentDate` como vem (o F&O serializa a data às 12:00 UTC). Medido na gravação: as notas `55` da base só têm o `FiscalDocumentDate` |
| `Issuer` / `Recipient` | com `FiscalDocumentIssuer = OwnEstablishment`, o estabelecimento emite e o terceiro recebe; com terceiro emitente, o contrário |
| `Party.TaxId` / `Name` / `StateRegistration` | `*CNPJCPF` (só dígitos) / `*Name` / `*IE` |
| `Party.MunicipalityCode` | endereço (`*PostalAddress`) → cidade → `IBGECode` (D13) |
| `IbsCbsTaxableMunicipality` | ausente (não há campo) |
| `Item.Number` | `LineNum`; se for fracionário, falha |
| `Item.ProductCode` / `Description` | `ItemId` / `Description` |
| `Item.Ncm` / `Cfop` | `FiscalClassification` / `CFOP`, só dígitos |
| `Item.Quantity` / `UnitAmount` / `TotalAmount` | `Quantity` / `UnitPrice` / `LineAmount` |
| `ItemCharge.Kind` | `MiscChargeType`: `Others` → outras. É o único valor na base; outro valor falha a montagem até ser mapeado com evidência |

Tirar a formatação de CNPJ, NCM e CFOP é apresentação, e não regra fiscal. O validador exige CFOP com 4
dígitos, e o XML os entrega só com dígitos.

### D15. Fixtures: gravadas e derivadas

- **Gravação.** O script opt-in `tools/d365-fixtures/Record-D365Fixtures.ps1` pega o token do
  `az account get-access-token` e grava as respostas byte a byte (`Invoke-WebRequest … .Content`) em
  `tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests/Fixtures/d365/`:
  - por nota modelo `55` da `brmf`, as 4 consultas exatas da montagem, a `FSTaxTransBRs` do voucher
    (gravada sempre, para os testes de complemento) e os endereços e cidades referenciados;
  - snapshot inteiro da empresa: os 83 cabeçalhos, as linhas, `FSFiscalDocumentTaxTransBRs` e
    `FSFiscalDocumentMiscChargeBRs`. Serve aos testes que precisam de linha real de qualquer nota, como
    as retenções, que estão todas em notas `01`/`SE`;
  - a `FSTaxTransBRs` só dos vouchers fiscais, em blocos de 20 vouchers. A entidade inteira tem 297 mil
    linhas de todas as empresas, e só as dos vouchers fiscais interessam ao complemento e à medição do
    casamento nos dois sentidos.

  Só do fiscosysdev, que tem dado de demonstração; **nunca** de ambiente de cliente.
- **Derivadas.** Uma derivada é uma cópia de uma gravada com a edição mínima descrita num `README.md`
  ao lado ("campo X da linha RecId Y trocado de A para B, porque …"). O nome do arquivo termina em
  `.derived.json`.
- **Varredura da base gravada.** Um teste distribui os 547 impostos reais sobre as linhas e os encargos
  reais do snapshot e confere: zero órfãos, 12 retenções nas `Withholdings`, nenhuma retenção nos
  `Taxes`. Reproduz offline a validação que o usuário fez no ambiente.
- **Medições durante a gravação**, registradas em d365/04 §11 (resultados na seção "O que a base não
  exercita" e no Context):
  - há imposto contábil do voucher sem linha fiscal? O casamento 547/547 partiu da fiscal; o sentido
    inverso não foi medido;
  - há nota `55` cancelada, e ela mantém as linhas?
  - qual campo de data está preenchido;
  - quais nomes o `MiscChargeType` assume.

### D16. Supressão de republicação no poller

**O custo que motiva a decisão.** O poller começa cada passada em `marca − sobreposição`. Com os
padrões (intervalo de 60s, sobreposição de 300s), cada documento é redescoberto por volta de 6 vezes
enquanto está na janela. Cada redescoberta vira uma mensagem e uma montagem completa de 4 a 5 GETs. O
hash **não** evita isso: a esteira busca antes de checar a idempotência, e tem que ser nessa ordem. O
hash protege contra reenvio para a Avalara, e não contra o tráfego no F&O.

| | sem supressão | com supressão |
|---|---|---|
| publicações por alteração | ~6 | ~1,15 (1 + margem/intervalo, abaixo) |
| GETs por nota (4,5 por montagem) | ~27 | ~5 |
| teto pelo limite do F&O (6.000 a cada 5 min = 1.200/min) | ~45 notas/min | ~230 notas/min |
| teto do consumidor serial (~50 montagens/min, a ~1,2 s cada) | **~8 notas/min** | ~43 notas/min |

Na base atual isso não aparece, porque as 83 notas estão paradas e quase nada cai na janela. Aparece
com cliente de verdade. O teto que morde primeiro é o do consumidor serial (D3), e não o do F&O.

**Decisão.** O `ChangeFeedPoller` descarta a referência cujo par (tenant, origem, chave natural,
carimbo de alteração) ele mesmo já publicou e cujo carimbo estava assentado quando foi publicado. A
supressão vale para qualquer feed. Para isso, a página do feed passa a carregar a informação que só a
origem tem:

```csharp
public sealed record ChangeFeedItem(DocumentReference Reference, DateTimeOffset ChangedAt);

public sealed record ChangeFeedPage
{
    public required IReadOnlyList<ChangeFeedItem> Items { get; init; }   // substitui References
    public DateTimeOffset? HighWatermark { get; init; }
    /// Carimbos até aqui são definitivos: nenhuma gravação futura na origem recebe carimbo ≤ StableThrough.
    /// null = nada nesta página é definitivo, e nada entra no registro.
    public DateTimeOffset? StableThrough { get; init; }
}
```

- **Por que o carimbo na chave, e não só o documento.** Se a nota muda de verdade dentro da janela, o
  carimbo muda e ela precisa ser republicada. Deduplicar pelo documento perderia a alteração.
- **Por que "assentado", e não só o par.** O `SysModifiedDateTime` tem resolução de segundo, então duas
  gravações no mesmo segundo ficam com o mesmo carimbo. Exemplo: o cabeçalho nasce `Created` às
  12:00:00.2 e passa a `Approved` às 12:00:00.8, numa segunda transação. Se a passada ler entre as duas:
  - o par `(doc, 12:00:00)` é publicado com status `Created`, e a montagem o ignora;
  - a passada seguinte devolve o mesmo par, agora `Approved`;
  - suprimido, a nota autorizada nunca seria montada.

  É o mesmo bug da chave só pelo documento, numa janela menor. Por isso só entra no registro o par com
  `ChangedAt ≤ StableThrough`. O par quente é publicado sem registro, reaparece na passada seguinte, é
  publicado de novo, agora com o conteúdo final, e só então é registrado.
- **Quem calcula o horizonte é a origem.** Só ela sabe a resolução do próprio carimbo e qual relógio o
  carimba. No D365, o horizonte é o `Date` da primeira resposta da leitura menos
  `D365ChangeFeedOptions.StampSettleMargin` (padrão 10s, mínimo 1s).
  - A margem cobre a resolução de 1s e a diferença entre o relógio do web server e o do AOS/SQL, que o
    ADR-0024 estimou em milissegundos.
  - Usar o `Date` da primeira resposta, e não o da página, é conservador.
  - Sem `Date`, não há horizonte, e nada é suprimido.
  - Um feed com carimbo mais grosso, só data por exemplo, informa um horizonte mais recuado; o poller não
    muda.
- **Quanto a margem custa.** Uma nota alterada a menos de 10s de uma leitura é publicada duas vezes. Com
  intervalo de 60s, isso dá cerca de 1 + 10/60 ≈ 1,15 publicação por alteração.

**Estado.** O `ChangeFeedPublicationLog` (Application/Inbound) é em memória, por réplica e singleton. O
poller é scoped por tick e perderia um conjunto guardado nele. É seguro para acesso concorrente, e o
poller o recebe no construtor.

- **Registro:** o par entra depois do `EnqueueAsync` bem-sucedido, e só se estiver assentado.
- **Poda:** no início do poll de cada (tenant, origem), saem os pares com `ChangedAt ≤ marca −
  sobreposição`. A consulta é `gt` e não os devolve mais. O feed do D365 arredonda o "desde" para baixo,
  ao segundo, então um par podado pode reaparecer uma vez. Isso é seguro: vira uma repetição, nunca uma
  perda.
- **Rebobinamento:** o log guarda a última marca que viu por (tenant, origem). Se a marca lida no início
  do poll for menor, o log daquele par é zerado. Quem rebobina quer tudo de volta na fila.
- **Reinício e troca de réplica:** o conjunto começa vazio e a repetição volta por uma janela, que é o
  comportamento de hoje, caro e correto. Registros velhos de uma réplica que reassume um tenant só
  suprimem pares que ela mesma publicou.
- **Dimensão:** são os pares com carimbo dentro da janela. A 100 notas/min com 300s, dá cerca de 500
  pares. Num backfill, cabe no máximo uma passada (teto de páginas × página), podada na passada
  seguinte.
- **Avanço da marca:** a referência suprimida conta como enfileirada; ela foi enfileirada numa passada
  anterior. `ChangeFeedPassSummary.ReferencesSuppressed` entra no log da passada.

**Por que não é um quarto esquema de idempotência.** Não decide se um documento é processado; só evita
republicar exatamente o que esta réplica já publicou. Nada depende dele para correção: sem o estado, o
sistema volta ao comportamento de hoje. A única garantia contra reenvio continua sendo o hash por
conteúdo (ADR-0016). A alternativa que o ADR-0024 rejeitou é adotada aqui porque a premissa da rejeição
não se sustentou (ver Context). O ADR-0025 registra a revisão.

**Alternativas.**

- **(b) Supressão dentro do `D365ChangeFeed`:** não mexe no contrato, mas cada feed novo teria que
  reimplementar. A sobreposição é conceito do poller, então a supressão pertence a ele.
- **Chave só pelo documento:** perde alteração real dentro da janela.
- **Encurtar a sobreposição:** troca custo por risco de perder nota, que é exatamente o que a margem
  existe para evitar.
- **Duplicate detection do Service Bus:** a janela é do broker, e o `MessageId` é `tenant:chave`. Isso é
  deduplicar só pelo documento, e perderia alteração real. Pôr o carimbo no `MessageId` resolveria a
  chave, mas não o par quente: o mesmo par volta com conteúdo novo e seria descartado pelo broker.
- **Registro persistido em tabela:** sobrevive a reinício e a troca de réplica, ao custo de uma tabela e
  de uma escrita por referência. Fica para quando o reinício pesar; nesta versão, perder o estado não
  quebra nada.
- **Mover a idempotência para antes da busca:** o hash depende do conteúdo buscado.

### O que a base não exercita

É dito aqui para não passar por cobertura:

| Caminho | Situação na base | Como o teste cobre |
|---|---|---|
| Imposto → encargo | Nenhum dos 547 aponta para encargo | Fixture **derivada**: um imposto real com a FK de linha zerada e a de encargo apontando para um encargo real da mesma nota. O caminho nunca passou dado real |
| Encargo com `MarkupTrans` | Os 14 têm `MarkupTrans` nulo | Não é lido pela montagem (non-goal); nada a cobrir |
| `ImportTax` zerado na fiscal | Os 26 zerados na fiscal com valor na contábil não têm explicação fechada; a hipótese "reaparece como encargo" foi testada e descartada (nenhum dos 14 casa com IPI da mesma nota). A nota `55` de importação `BRMF06-110000031` tem um: fiscal 0 / 30 / 0, contábil 4.500 / 30 / 1.350 | **Gravada**: a nota `55` de importação. A regra de complemento é do usuário, e não explica os 26 |
| CST do IPI `51`/`55` contra `01`/`05` | Não confirmado por especialista fiscal | O teste prova que o CST vem da fiscal, e não que o CST está certo |
| IBS/CBS (grupo da Reforma) | Nenhuma nota da base tem `CBS`/`IBS*` (notas de 2016) | Fixtures **derivadas**: completo, parcial, CST divergente. Nada disso passou dado real |
| Divergência fiscal × contábil fora do padrão | Não existe na base (é o que "547 casadas, zero contra valor" diz) | Fixtures **derivadas**, uma por caso da spec |
| Nota `55` cancelada | Não existe. As 2 canceladas da base são modelo `01` e mantêm a linha | Cabeçalho **derivado** de uma nota `55` gravada, com `Status` trocado para `Cancelled`. A cancelada `01` gravada cobre o caso de modelo fora do escopo |
| Retenção em nota `55` | As 12 retenções estão em notas `01`/`SE` | Linhas **gravadas** do snapshot, aplicadas ao distribuidor, que não depende do modelo |

### D17. ADR-0025

`docs/adr/0025-montagem-do-documento-d365-e-origem-na-referencia.md` registra:

- D1 (origem na referência);
- D2 (desfecho `Ignored`);
- D4 (Locator com RecId);
- D6 (só autorizada; vazio é falha);
- D7 (domínio aditivo, IBS/CBS opcional);
- D9/D10 (fiscal é a fonte, complemento por tipo, divergência para);
- D11 (canônico e versão);
- D13 (cache de cadastros);
- D16 (supressão de republicação, com a tabela de custo e a regra do par assentado).

Uma nota no ADR-0024 aponta para ele por três motivos: o Locator novo, a fila que ganhou consumidor e a
revisão da alternativa "filtrar os repetidos da sobreposição". Essa alternativa, antes rejeitada, agora é
adotada como filtro de tráfego, porque a premissa "o roteamento absorve barato" não se sustentou.

## Risks / Trade-offs

- **[Nenhuma nota do D365 chega à Avalara nesta fatia]** As 5 notas `55` do fiscosysdev são de 2016, sem
  IBS/CBS, e as que tiverem IBS/CBS ficam sem `cClassTrib`. O desfecho visível esperado ponta a ponta no
  fiscosysdev é: 5 rejeitadas ("tributos da Reforma ausentes", mais "chave de acesso" se vier vazia), 9
  ignoradas (`SE`) e 0 enviadas. → Dito no RUNNING.md e no ADR. A montagem é provada por fixture. A
  entidade de `CClassTribTable_BR` fica como próxima fatia do pacote D365.
- **[Supressão engole alteração real]** É o risco que a supressão introduz. → Três proteções:
  - o carimbo faz parte da chave;
  - só o par assentado (carimbo ≤ horizonte da origem) é registrado, o que cobre a resolução de segundo;
  - o registro é zerado quando a marca regride.

  O que sobra é diferença entre o relógio do web server e o que carimba o registro maior que a margem
  (10s), contra milissegundos estimados no ADR-0024. A margem é configurável, e o teste do "mesmo
  segundo" prova a regra.
- **[Repetição volta depois de reinício ou troca de réplica]** O registro é em memória. Por uma janela de
  sobreposição, cada nota volta a cerca de 6 montagens, e o hash corta o reenvio. → Aceito na primeira
  versão (non-goal persistir). `ReferencesSuppressed` no log da passada mostra quando a supressão está
  atuando.
- **[Vazão com concorrência 1]** A cerca de 1,2 s por montagem, o consumidor faz umas 50 montagens/min:
  cerca de 43 notas/min com a supressão, contra cerca de 8 sem ela. Um backfill de 10 mil notas leva
  umas 3 horas. → Aceito nesta fatia (D3); a concorrência exige serializar por documento antes.
- **[Throttling queima entregas]** A reentrega do Service Bus é imediata. Sob throttling longo, uma
  mensagem pode esgotar as 5 entregas e ir para a dead-letter. → A espera dentro da chamada honra o
  `Retry-After` até o teto, e o processador renova o lock. A dead-letter é visível e reprocessável.
  Reentrega agendada fica para depois.
- **[Motivo da dead-letter é genérico]** O registro mostra `MaxDeliveryCountExceeded`, e não "ponte sem
  par contábil". → A causa está no log do processador e na foto da fonte. Dead-letter imediata com
  motivo é non-goal.
- **[Mensagem antiga sem origem]** Uma mensagem XML sem origem, de tenant cujo perfil é `Dynamics365`,
  cai no source do D365 e falha na leitura do Locator. → Janela limitada ao TTL da fila (1 hora); o erro
  é claro.
- **[Cancelada depois de confirmada]** O registro troca `Confirmed` por `Ignored`. O fato de a Avalara
  ter a nota como emitida só aparece no motivo e no `ExternalId` preservado. → Aceito até a fatia de
  cancelamento, que transforma esse caso em despacho.
- **[Agrupamento do dashboard em nota de entrada]** O `GoodsInvoiceMetadataExtractor` usa o CNPJ do
  emitente como empresa: nota de entrada do D365 agrupa pelo fornecedor. É comportamento existente,
  exposto agora. → Registrar para a fatia de dashboard; não muda aqui.
- **[Campos novos do domínio só pelo D365]** "Xml e D365 produzem o mesmo `GoodsInvoice`" vale para o
  que já existia. Impostos, retenções e encargos só são preenchidos pelo D365 nesta fatia. → Non-goal
  explícito; o parser do XML ganha isso numa fatia própria.
- **[Mudança de formato reintegra]** Mudar o `$select` ou o canônico muda a impressão das notas
  redescobertas. → Versão `"v"` no canônico e registro no ADR.

## Migration Plan

1. Sem migration de banco: o status é texto, e o registro de publicações do D16 é só memória. Sem
   mudança no `docker/servicebus/Config.json`, porque a fila já existe.
2. Mensagens em voo:
   - sem `Origin`: seguem pelo perfil;
   - na `documents-discovered` com Locator antigo: falham na leitura do Locator e vão para a
     dead-letter, visível. Em dev, basta esperar o TTL de 1 hora ou reiniciar o emulador.
3. **Rollback:** reverter o deploy. O código antigo ignora o campo `Origin` (o `System.Text.Json` ignora
   propriedade desconhecida) e não tem consumidor da fila de descoberta. As linhas `Ignored` já gravadas
   ficam como texto que o código antigo não conhece. Antes de reverter em ambiente compartilhado,
   convertê-las para `IntegrationError` com o motivo preservado.

## Open Questions

Nada disto muda spec, desenho ou tarefas; fecha com o fiscal.

- O CST do IPI `51`/`55` (fiscal) contra `01`/`05` (contábil).
- Os 26 impostos zerados na fiscal com valor na contábil: por que existem, além do `ImportTax`. O padrão
  por CST (98/99, 90, 05) está em d365/04 §11.
- **A base do `ImportTax` complementado aparece em dois campos.** Na fiscal, o `ImportTax` zerado guarda a base
  em `TaxBaseAmountOther` (4.500 na nota de importação), com `TaxBaseAmount` e valor zerados. O merge campo a
  campo (D9) completa `TaxBaseAmount` e valor pela contábil (4.500 / 1.350) e mantém o `OtherBase` da fiscal.
  O documento fica com base 4.500 em `TaxBase` e em `OtherBase`. Não é divergência pela regra, mas quem vier
  a consumir o II (payload da Avalara, fatia futura) precisa decidir com o fiscal qual dos dois vale.

Fechadas na gravação das fixtures (d365/04 §11):

- a data de emissão vem só no `FiscalDocumentDate` (D14);
- o `MiscChargeType` só assume `Others` (D14);
- a nota cancelada mantém as linhas, mas as canceladas da base são `01`;
- o `SysModifiedDateTime` tem resolução de segundo, o que confirma a necessidade da regra do par
  assentado (D16).
