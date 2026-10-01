## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código, conferido, incluindo onde a leitura corrigiu uma
premissa do pedido.

- **O agrupamento sai do emitente.** O `GoodsInvoiceMetadataExtractor` pega o `Issuer.TaxId` e corta 8 + 4 dígitos. O
  D365 já lê o `FiscalEstablishmentCNPJCPF` no `HeaderSelect` da montagem, mas só para montar a parte. O
  `FiscalEstablishment` não é lido em lugar nenhum. O valor chega formatado (`442782250001-80` nas fixtures), e o
  assembler já o reduz a dígitos para a parte.
- **O dia da nota montada sai em UTC.** O extrator grava `DateOnly.FromDateTime(IssueDate.Date)`, a data do instante no
  fuso que ele carrega.
  - **No XML:** o `dhEmi` traz o fuso de quem emitiu (`2026-06-01T10:00:00-03:00`), e o parser o preserva
    (`DateTimeOffset.Parse`). O dia sai certo.
  - **No D365:** o assembler monta a `IssueDate` pelo `FiscalDocumentDateTime`, que o F&O guarda e devolve em UTC
    (`2026-08-07T18:47:00Z`), e só cai no `FiscalDocumentDate` quando a data e hora vem vazia (1900).
  - **O resultado:** num fuso UTC-3, toda nota montada emitida depois das 21h cai no dia seguinte. A ignorada, que pega
    o dia do `FiscalDocumentDate` pela descoberta (D4), fica no dia certo. Duas notas da mesma noite iriam para dias
    diferentes nos cards, conforme o desfecho.
  - **No fiscosysdev:** hoje não aparece, porque as NF-e 55 têm `FiscalDocumentDateTime` vazio. Mas qualquer nota
    emitida com data e hora cai nisso.
- **O tamanho das colunas não é o que o pedido supõe.**
  - **`CompanyCode`:** já é `nvarchar(20)` nas três tabelas (`ProcessedDocuments`, `IntegrationExecutions`,
    `ScheduledIntegrations`). Os 14 dígitos cabem sem migração.
  - **`BranchCode`:** é `nvarchar(10)`. O código do estabelecimento do F&O (EDT do `FiscalEstablishmentId`) não tem
    tamanho registrado no repositório (D5).
- **O `/ingest` não carrega empresa.** O `IngestRequest` é `{ naturalKey, locator }`. Quem carrega `companyCode` são
  o `/integrations/manual` e o `/schedules`, como texto livre, sem regra de tamanho. A tela também não tem nenhuma regra
  de 8 dígitos.
- **A nota ignorada não guarda nada do grupo.** O `RecordIgnoredAsync` passa pelo `UpsertAsync`, que cria a linha só
  com tenant, chave, tipo, estado e motivo. Faltam a empresa, a filial, a data, o modelo e também o `Trigger`. O
  `ListGroupsAsync` filtra `CompanyCode != null && ReferenceDate != null`, então a ignorada não entra em grupo nenhum,
  nem nos cards, que são a soma dos grupos do dia. É por isso que a descoberta passa a ler o estabelecimento, e não só
  a data e o modelo, como decidido com o usuário.
- **Onde a ignorada é decidida.** A NFS-e é ignorada no `DocumentRouter`, pelo tipo, antes de qualquer busca. O que o
  hub sabe dela é o que a descoberta leu. A nota fora de escopo na montagem (`DocumentOutOfScopeException`, no
  `HeaderAsync`) é ignorada antes do `RecordMetadataAsync`.
- **A chave de linha da tabela colide.** O `ListGroupsAsync` agrupa por (empresa, filial, data, tipo, modo), mas a
  `GroupsPage` usa `rowId = empresa:filial:data`. Com as ignoradas agrupadas, uma NFS-e e uma NF-e do mesmo dia e do
  mesmo estabelecimento viram linhas com o mesmo id.
- **A omissão não está em foto nenhuma.** O pedido diz que ela "continua gravada na foto", mas hoje ela está só no
  `Reason`: o `IntegrationReceipt.Omissions` vira "Enviado sem: …" no `RecordSubmissionAsync`, e a recusa do envio a
  junta ao motivo (`| Enviado sem:`). O payload de destino e o envelope da resposta não a trazem. Tirar a omissão do
  motivo da recusa sem gravá-la antes numa foto a apagaria (D9).
- **O motivo junta tudo.** A `PlatformMessage.FindMessages` varre `title` junto com `errors` (`title` está nas
  `MessageProperties`) e junta com `"; "`, cortando em 1000 caracteres. Na recusa real, o `title` fecha a lista e a
  omissão vem depois de `" | "`.
- **O mock recusa no formato presumido.** O `rejeitar` do `MockComplianceApi` devolve `{"mensagens": [...]}`, e não o
  ProblemDetails que o sandbox mostrou (`Fixtures/sandbox/recusa-no-envio.json`).
- **O selo não tem como ficar vermelho.** O `/info` devolve só `automaticIntegration`, que é falso tanto para
  "desligado" quanto para "não varre".
- **Os agendamentos não têm chave estrangeira.** O `ProcessingDbContext` não declara relação nenhuma, e as migrações
  não criam FK. A `IntegrationExecutionRow` guarda o `ScheduleId` como `int?` solto, junto com os próprios `Mode`,
  `CompanyCode`, `BranchCode`, `PeriodStart`, `PeriodEnd`, `DiscoveredCount` e `CreatedAt`. O `SqlExecutionQueries`
  não faz join com os agendamentos, e o `ExecutionSummary` nem expõe o `ScheduleId`.
- **O "Tempo real" é o `Trigger`.** O `SqlProcessingStore` grava `reference.SourceMode ?? "RealTime"`, e o
  `SqlDocumentQueries` serve `Trigger ?? "RealTime"`. O `RealTime` não viaja na fila: a referência de evento vai com
  `SourceMode = null`. Ele aparece no seed de demonstração e no `TRIGGER_LABEL` da `GroupsPage`.
- **Papéis.** Existem `Admin` e `Viewer` (`UserRole`). O detalhe do documento não olhava papel nenhum, e o `/trace` e o
  zip eram abertos a qualquer usuário do tenant.
- **Sem teste de Host e de dashboard.** O que precisa de teste mora na Application, na Infrastructure e nos adapters. O
  dashboard termina no `npm run build` e na prova manual.

## Goals / Non-Goals

**Goals:**

- Uma regra de grupo para toda nota do D365, em qualquer desfecho, sem valor padrão.
- Um critério de dia só: o mesmo documento cai no mesmo dia, montado ou ignorado (D1).
- A tela lê o motivo da foto, e o `Reason` volta a ser um resumo que cabe no banco e numa linha.
- A omissão nunca se perde: fica na foto em todos os casos, e só a nota aceita a mostra como ressalva.
- Nenhum esquema novo de idempotência. O canônico sobe de versão pela regra que já existe (ADR-0026 D17).

**Non-Goals:**

- Mudar o que o destino recebe. O payload da Avalara não muda nesta fatia.
- Mover para o backend a humanização dos caminhos de campo (D8).

## Decisions

### D1. O estabelecimento próprio e a data fiscal entram no domínio, e o extrator os prefere

- **O domínio:** a `GoodsInvoice` ganha `Establishment`, um record opcional com `TaxId` (só dígitos) e `Code` (como
  veio). Os dois andam juntos: a origem que conhece um conhece o outro. Um par evita o estado "CNPJ sem código", que
  obrigaria a misturar fontes (a filial do fornecedor ao lado da empresa própria).
- **Quem preenche:** o `D365GoodsInvoiceAssembler`, a partir do `FiscalEstablishmentCNPJCPF` (pelo mesmo `Digits` da
  parte) e do `FiscalEstablishment`. É o mesmo campo que já monta a parte do estabelecimento. Por isso, o
  `Establishment.TaxId` e o CNPJ da parte escolhida pela `Issuance` concordam por construção. O parser de XML deixa
  nulo.
- **O extrator:** com `Establishment`, vale `CompanyCode = TaxId` e `BranchCode = Code`. Sem ele, vale a derivação de
  hoje pelo emitente.
- **Código vazio:** se o F&O devolver o `FiscalEstablishment` vazio, a filial fica vazia, e não é derivada do CNPJ. O
  campo é parte da chave da entidade (`EntityKey`), e a gravação das fixtures (tarefa 1.1) confirma que vem preenchido.

**O dia da nota: a data fiscal, no fuso de quem emitiu, sem conversão.**

Não há fuso do hub. O dia de uma nota é a data que o próprio documento registra, no fuso de quem o emitiu, e o hub não
a converte:

- **D365:** o `FiscalDocumentDate`, que é um campo de data do F&O, sem hora e sem fuso. O OData o devolve como
  `yyyy-MM-ddT12:00:00Z`, e o dia é a parte da data, como veio. O `FiscalDocumentDateTime`, em UTC, não define o dia.
- **XML:** a data do `dhEmi` no fuso que ele traz. É o que o extrator já faz.

Para o D365 cair no mesmo critério nos dois caminhos:

- **O domínio:** a `GoodsInvoice` ganha `FiscalDate` (`DateOnly?`), "o dia fiscal como a origem o registra, quando ela
  o guarda separado do instante da emissão".
- **Quem preenche:** o assembler, a partir do `FiscalDocumentDate`. O campo já está no `HeaderSelect`, então não mexe
  no canônico. O parser de XML deixa nulo.
- **O extrator:** com `FiscalDate`, vale ela. Sem ela, vale a data da `IssueDate` no fuso dela (XML).
- **A descoberta** (D4) lê o mesmo `FiscalDocumentDate`, com a mesma regra. A nota montada e a ignorada saem do mesmo
  campo, pela mesma leitura.
- **A `IssueDate`:** continua sendo o instante da emissão. O adapter de saída a usa como hoje, e nada muda no payload.
- **O "hoje" dos cards:** é o dia no relógio de quem vê o dashboard, comparado com esse dia fiscal (D6).

**Por que a data fiscal da origem, e não um fuso escolhido pelo hub:**

- **É o dia que a escrituração usa.** O `FiscalDocumentDate` é a data com que o ERP lança a nota, e o `dhEmi`
  carrega a data de emissão no fuso de quem emitiu, pelo leiaute. Os dois são o "dia" do documento fiscal.
- **O Brasil tem quatro fusos.** Um fuso fixo erraria o dia de estabelecimentos do Amazonas, do Acre, de Rondônia e
  dos dois Mato Grosso (UTC-4 e UTC-5), e de Noronha.
- **Não há o que configurar.** O documento já diz o dia. Um fuso por tenant ou por estabelecimento seria configuração
  para chegar ao que a origem já registra.

**Alternativas descartadas para o dia:**

- **UTC.** É o defeito que esta decisão corrige: depois das 21h em UTC-3, a nota vai para o dia seguinte.
- **Brasília fixo (`America/Sao_Paulo`)** sobre o instante da emissão. Acerta no Sudeste e erra nos outros fusos. Além
  disso, exige base de fusos no host e ainda pode divergir do `FiscalDocumentDate` que o ERP gravou.
- **Converter a `IssueDate` do D365 para o fuso local na montagem.** O F&O não diz o fuso do estabelecimento, e a
  data já existe pronta no `FiscalDocumentDate`.

**Alternativas descartadas para o estabelecimento:**

- **Derivar o CNPJ da `Issuance` e das partes, e acrescentar só o código.** Evitaria um campo, mas a regra "qual parte
  é a própria" ficaria no extrator, duplicando o `PartiesOf` do adapter de saída. O pedido foi explícito: os dois
  campos no domínio.
- **Dois campos soltos na `GoodsInvoice`.** Foi descartada porque permitiria um sem o outro.

### D2. O comentário do `D365Canonicalizer` vale só para o `$select` da montagem

Esta é a confirmação pedida antes de mexer:

- **O que o comentário diz:** "Mudar este formato (ou o `$select`) muda a impressão de toda nota redescoberta: suba
  `Version`".
- **O que entra no canônico:** o `Canonicalize` recebe o `D365DocumentRows`, que é feito das respostas da montagem no
  `D365GoodsInvoiceSource`. São o `HeaderSelect`, o `LineSelect`, o `TaxSelect`, o `ChargeSelect` e o
  `TaxTransSelect`.
- **O que não entra:** a descoberta (`D365ChangeFeed.Select`) desserializa as linhas num `Row` tipado e só monta a
  referência. Nada dela chega ao canônico. O `PostalAddressSelect` também fica fora: é cadastro, em cache, e a spec
  exclui cadastro da impressão.

Conclusão: acrescentar `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e `FiscalEstablishment` ao `$select` da
descoberta não muda impressão nenhuma, e não sobe a `Version`. O comentário do `D365Canonicalizer` ganha essa precisão
("o `$select` da montagem"), e o do `D365ChangeFeed.Select` passa a dizer que ele não entra no hash.

### D3. O `$select` da montagem ganha `FiscalEstablishment`, e o canônico vai para a v3

O código do estabelecimento só existe no cabeçalho. Lê-lo na montagem muda o canônico, e por isso a `Version` sobe de
2 para 3.

**O efeito, dito antes de aplicar.** Pela regra do ADR-0026 D17, a impressão só é recalculada quando a nota é
buscada. Depois do deploy, cada nota do D365 relida tem impressão nova uma vez. Uma nota relida é a tocada no ERP, a
de um rebobinamento ou a de um backfill.

- **Nota já `Submitted` ou `Confirmed`:** a idempotência por conteúdo não a reconhece, e ela é reenviada uma vez à
  plataforma.
- **Nota rejeitada ou ignorada:** nada muda. O `AlreadyProcessedAsync` só barra `Submitted` e `Confirmed`, então a
  rejeitada já é reprocessada a cada releitura. A NFS-e é ignorada no roteamento e nem é buscada.
- **No fiscosysdev:** zero efeito. As 5 NF-e 55 estão `IntegrationError` e as 9 NFS-e, `Ignored`. Nenhuma foi aceita.
- **Em produção:** não há tenant. O item do STATUS "Mudança de versão do canônico com base grande" continua aberto: o
  hash de transição é pré-requisito da primeira subida com tenant real, e não desta.

**Alternativas descartadas:**

- **Tirar o `FiscalEstablishment` do canônico** (tratá-lo como volátil, igual ao `SysModifiedDateTime`). Evitaria a
  subida, mas a foto da fonte, que é o canônico, deixaria de mostrar o campo que decide o grupo da nota. E uma troca de
  estabelecimento na nota é mudança de conteúdo, e não de auditoria.
- **Ler o código na descoberta e levá-lo à montagem pela referência.** Evitaria a subida, mas a nota montada passaria a
  ter uma parte do cabeçalho vinda de outra leitura, feita em outro instante. A foto da fonte deixaria de ser a nota
  inteira.

### D4. A referência leva o grupo visto na descoberta, e quem grava é o store

- **A referência:** a `DocumentReference` ganha `Metadata`, opcional, do tipo `DocumentMetadata` que o extrator já
  produz. São empresa, filial, data de referência, número e modelo.
- **Quem preenche:** o `D365ChangeFeed.Map`, com a mesma normalização da montagem: CNPJ só com dígitos, e data como o
  dia do `FiscalDocumentDate`, que vem como `yyyy-MM-ddT12:00:00Z`, sem conversão de fuso (D1). A leitura do dia fica
  numa função só, usada pela descoberta e pelo assembler, para os dois caminhos não divergirem.
- **O `$select` da descoberta:** ganha `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e `FiscalEstablishment`. O
  `FiscalDocumentNumber` e o `Model` já estão lá.
- **A fila:** o `DocumentQueueSerialization` já serializa o record inteiro. O `DateOnly` sai como `yyyy-MM-dd`. Uma
  mensagem antiga, sem o campo, desserializa com `Metadata = null`.

**Quem grava.** O `SqlProcessingStore.UpsertAsync`, que serve a ignorada, a rejeição, a dead-letter e a submissão,
passa a:

- **na criação da linha:** gravar o `Trigger` (`reference.SourceMode ?? "Automatic"`) e, se a referência tem
  `Metadata`, o grupo;
- **na atualização:** gravar o grupo da referência só quando a linha ainda não tem grupo (`CompanyCode` nulo). O grupo
  da montagem, gravado antes pelo `RecordMetadataAsync`, nunca é trocado pelo da descoberta.

Nenhuma porta muda: o `RecordIgnoredAsync(reference, reason)` já recebe a referência.

**Sem valor padrão.** O `Map` não completa um campo ausente. A data nunca falta num documento autorizado ou cancelado
(ver o pedido), e o parse da data segue a regra do `SysModifiedDateTime`: um valor ilegível é falha da leitura, e não
um dia inventado. As 9 linhas de dev com data nula são de passadas anteriores e somem na próxima limpeza. A coluna
continua anulável por causa delas e do caminho de XML.

**Alternativas descartadas:**

- **Campos soltos na referência** (`ReferenceDate`, `DocumentModel`, …). Foi descartada porque o `DocumentMetadata` já
  é o contrato do grupo, e reusá-lo mantém uma forma só entre o extrator e a descoberta.
- **Gravar o grupo no roteador**, com um método novo na porta (`RecordGroupAsync`). Foi descartada porque é uma
  gravação a mais por nota e uma porta que engorda. O store já recebe a referência.
- **Deixar a descoberta sobrescrever sempre.** Foi descartada porque a gravação da rejeição, que vem depois da
  montagem, trocaria o grupo da montagem pelo da descoberta.

### D5. Colunas, filtros, agendamentos e `/ingest`

- **`CompanyCode`:** sem migração. O `nvarchar(20)` cabe os 14 dígitos nas três tabelas.
- **`BranchCode`:** passa de `nvarchar(10)` para `nvarchar(20)` nas três tabelas, numa migração
  (`WidenBranchCode`).
  - **Por quê:** um código de estabelecimento acima de 10 caracteres faria o `INSERT` falhar por truncamento. A nota
    ficaria na dead-letter, sem motivo legível.
  - **O tamanho:** 20 é o tamanho usual dos identificadores do F&O. A tarefa 1.1 confere o tamanho do EDT do
    `FiscalEstablishmentId` nos metadados, e, se for maior, a migração usa o tamanho dele.
  - **Resultado da 1.1, parcial:** o tamanho não é publicado. O `$metadata` do OData do fiscosysdev declara a
    propriedade só como `Edm.String`, sem `MaxLength`, e o esquema CDM da Microsoft também não o traz. Os valores da
    `brmf` são `Matriz`, `SP-01` e `SAL-01`, com no máximo 6 caracteres. A migração usa 20, e a conferência no AOT fica
    em aberto.
  - **Resultado da 1.1, fechado (2026-10-01):** no AOT, o EDT `FiscalEstablishmentId_BR` tem String Size 10. O
    `nvarchar(20)` cabe com folga, e a migração fica.
  - **Risco:** alargar é inofensivo, e não perde dado.
- **Filtros do dashboard:** nada muda na regra. A tela não tem regra de 8 dígitos, e o filtro da grade é por texto. A
  mudança é só de apresentação: a máscara de CNPJ (D6).
- **Agendamentos e integração manual:** nada muda. O `companyCode` desses contratos é texto livre, e o diretório
  (`companies.json`) e a descoberta local são o caminho de XML de dev, com o código de 8 dígitos. Com D365, não há
  descoberta por período ainda. Quando houver, o código do diretório terá de ser o mesmo CNPJ de 14 dígitos: fica
  anotado no STATUS.
- **`/ingest`:** nada muda. Ele não carrega empresa.

### D6. Cards, tabela e o registro do critério

- **O critério fica escrito em três lugares:**
  - **na spec:** `document-grouping`, "Os cards e a tabela contam pela data fiscal";
  - **no código:** um comentário na `GroupsPage`, onde os KPIs filtram `referenceDate === today`, que diz que é a data
    fiscal de propósito e que as notas de 2016 do fiscosysdev ficam fora;
  - **no STATUS:** junto com o próximo passo.
- **O "hoje":** é o `todayIso()` da `GroupsPage`, o dia no relógio do navegador de quem vê. Ele é comparado com o dia
  fiscal da nota (D1), que não passa por conversão de fuso.
- **A ignorada nos cards:** entra no "Documentos" pela soma de `total` dos grupos. Os outros três cards já a excluem. A
  consulta não muda de forma: a ignorada passa a entrar porque passa a ter grupo (D4).
- **A chave de linha:** a `rowId` da `GroupsPage` passa a incluir o tipo e o modo, e deixa de colidir. O modal do
  grupo segue listando as notas do dia do estabelecimento (`ListByGroupAsync` por empresa, filial e data). O título
  mostra o total da linha clicada, e a lista pode trazer notas de outro tipo do mesmo dia. Isso fica nos riscos.
- **A máscara:** uma função de apresentação formata a empresa de 14 dígitos como CNPJ, na grade e no título do modal. O
  valor da linha, a URL do grupo e o filtro seguem com dígitos.
- **A prova do critério de saída**, como decidido com o usuário:
  - **na passada contra o fiscosysdev:** a tabela mostra as 14 notas nas datas fiscais, as 9 ignoradas incluídas, e os
    cards mostram 0, o que é correto;
  - **por teste:** um teste da `SqlDocumentQueries` prova que a ignorada com grupo entra no grupo do dia dela, conta no
    `Total` e fica fora do `ComErro`.
- **O próximo passo, registrado e não implementado:** os filtros dos cards, por período (dia, 7, 15 e 30 dias, com o
  dia como padrão) e por modelo. A ignorada já tem data e modelo, e com esses filtros um contador próprio para ela
  deixa de ser necessário.

### D7. O `Reason` vira resumo, sem o `title`

A `PlatformMessage` passa a tratar o mapa de erros por campo como um caso próprio:

- **Com `errors` objeto no corpo** (ValidationProblemDetails):
  - o motivo é `"{N} campos com erro: a, b, c e mais {N-3}"` (no singular, "1 campo com erro"), com os caminhos na ordem da resposta. Sem o
    "e mais" até três;
  - o `title` não entra, nem nenhuma outra propriedade do topo.
- **Sem o mapa:** a extração de hoje, com o `title` ainda contando. Num ProblemDetails sem `errors`, ele pode ser a
  única mensagem.
- **O prefixo e o limite:** o dispatcher mantém o "Plataforma de compliance recusou: …". O limite de 1000 continua
  como guarda. O resumo, com três caminhos, fica muito abaixo dele.
- **A consulta de status:** o `FindMessages` usa a mesma regra, então uma recusa assíncrona com mapa também vira
  resumo.

**Alternativa descartada: tirar só o `title` e manter a lista inteira no `Reason`.** Foi descartada porque o corte em
1000 continuaria, e a lista inteira já está na foto, de onde a tela a lê (D8). Duas cópias da lista divergiriam no
primeiro corte.

### D8. A lista sai das fotos no servidor, por uma leitura do desfecho, e a tela só a humaniza

**Revisado em 2026-09-28.** A primeira versão lia a lista da foto no front, pelo `useTrace`. Com o `/trace` restrito ao
Admin (D11), o Viewer não teria mais de onde lê-la, e a alternativa que tinha sido descartada, a do backend servir a
lista pronta, passou a ser a decisão.

- **A leitura do desfecho:** `GET /documents/{tenant}/{chave}/reading`, aberta a qualquer papel do tenant e com a regra de
  tenant do ADR-0028 (o de outro tenant, ou o documento sem fotos, dá o mesmo 404). Ela devolve só:
  - `fields`: a lista de campos da recusa, `{ path, messages }`, na ordem da resposta;
  - `omissions`: as omissões do envio.

  Nada mais das fotos sai por ela: nem o payload, nem a fonte, nem o domínio, nem o corpo da resposta.
- **Onde se lê:** na Application (`Tracing`), numa função pura sobre os arquivos que o `DocumentTraceQuery` já devolve:
  - **a foto:** a da consulta de status (`*.response.status.json`) quando ela tem o mapa, e senão a do envio
    (`*.response.submit.json`);
  - **a lista:** sai do `response.body.errors`, o mapa do ProblemDetails (RFC 9110), que é um formato padrão, e não da
    Avalara;
  - **as omissões:** saem do `request.omissions` da foto do envio (D9).

  O nome dos arquivos é a convenção do `TracePaths`, a mesma que a tela usava para classificar as fotos.
- **Como mostra:** a tela humaniza o caminho de forma genérica. O `itens[0]` vira "item 1", e os segmentos são
  separados por " › ". As mensagens vêm como a plataforma as escreveu, e já estão em português.
- **Sem lista, ou sem leitura:** vale o `doc.reason`, como texto.

**Alternativas descartadas:**

- **Um dicionário de rótulos** (`tipoPagamento` → "Tipo de pagamento"). Foi descartada porque é o contrato da Avalara
  dentro do front, que é único para todos os clientes (ADR-0020). As mensagens já dizem o campo em português ("'Tipo
  Pagamento' não pode ser nulo.").
- **Continuar lendo a foto no front.** Foi a primeira versão. Ela exige que o `/trace` fique aberto ao Viewer, o que o
  D11 revisado fecha.
- **A leitura no adapter da Avalara.** Foi descartada porque ela só lê o formato padrão do ProblemDetails e o envelope
  do hub (ADR-0027 §8). Não há contrato de destino nela, e na Application ela serve a qualquer destino que responda
  nesse formato.

### D9. A omissão sai do motivo de falha e vai para a foto do envio

- **Na foto:** o envelope do envio ganha `request.omissions`, a lista das omissões que o mapeamento declarou para
  aquela requisição, ao lado do `method` e da `url`. Sem omissão, o campo não aparece. A foto é gravada antes de
  classificar a resposta, então a omissão fica gravada em todo envio que recebeu resposta: aceito ou recusado.
- **No dispatcher:** a recusa 400/422 deixa de juntar `| Enviado sem: …` ao motivo. O `IntegrationReceipt.Omissions`
  do aceite não muda, e o `RecordSubmissionAsync` continua gravando "Enviado sem: …" como ressalva.
- **No `MarkPolledAsync`:**
  - **status novo de falha** (`IntegrationError`, `Unconfirmed`): o `Reason` é só o motivo novo, e a ressalva sai;
  - **`Confirmed`, ou ainda `Submitted`:** a ressalva fica, como hoje.

  A regra "enquanto aceita, o `Reason` só pode ser a ressalva" continua valendo, e deixa a tela distinguir sem
  analisar texto.
- **Na tela:**
  - **nota aceita** (`Submitted` ou `Confirmed`) com `reason`: a marca discreta "Enviado com ressalvas", no lugar do
    banner de aviso. Ao abrir, ela lista as omissões da foto (`request.omissions`), com o `reason` como texto se a foto
    faltar;
  - **nota ignorada:** mantém o aviso com o motivo;
  - **falha:** mostra a lista do D8.

**Alternativas descartadas:**

- **Uma coluna própria para a ressalva** (`Remarks`). Seria mais explícito, mas é migração e contrato novos, para um
  valor que o estado já separa: aceita tem ressalva, e falha tem motivo.
- **Uma foto nova, só das omissões** (`{destino}.omissions.json`). Seria mais um arquivo no layout, no zip e na
  classificação do `useTrace`, para uma lista que pertence à requisição que o envelope já descreve.
- **A omissão na foto do payload.** Foi descartada porque essa foto é o JSON exato enviado, e deixaria de ser.

### D10. A foto perde o ruído do ProblemDetails, e a sonda fica crua

- **A regra:** uma função pura no adapter de saída recebe o corpo já redigido e o status HTTP. Se o corpo é um objeto
  com `errors` objeto, ela tira do topo:
  - o `type`;
  - o `title`;
  - o `status`, quando é número igual ao status HTTP.

  O resto fica, com o `traceId`. Sem `errors`, o corpo fica como veio: a resposta de status (`{ id, status: "erro",
  mensagens }`) nunca perde o status nativo.
- **Onde se aplica:** só no dispatcher, na foto do trace, nas duas trocas. A sonda (`AvalaraSandboxProbe`) continua
  gravando o corpo cru, porque ela existe para registrar a forma da plataforma, e o `type` e o `title` são parte dessa
  evidência.

  O `PlatformResponseEnvelope.Build` continua sendo um só. Ele passa a receber o corpo que vai para a foto e as
  omissões, que são opcionais, e a forma do envelope segue igual para os dois chamadores.
- **A redação:** vem antes, como hoje. A contagem de redações não muda.
- **O motivo:** sai do corpo redigido inteiro, e não do corpo sem o ruído. O D7 já ignora o `title` quando há mapa.
- **A fixture:** o `Fixtures/sandbox/recusa-no-envio.json` fica como foi gravado, porque é a evidência. Os testes
  derivam dele a foto sem o ruído.

**Alternativa descartada: aplicar a regra dentro do `Build` para todos.** Foi descartada porque a sonda perderia a
evidência do formato. Uma próxima mudança do formato da recusa só seria vista pela diferença entre o que o sandbox
manda e o que a foto mostra.

### D11. As fotos cruas só para Admin, de fato: o `/trace` e o zip exigem o papel, e o JSON abre num modal próprio

**Revisado em 2026-09-28.** A primeira versão tornava o "Visualizar JSON" só apresentação: o `/trace` e o zip continuavam
abertos a qualquer usuário do tenant. O pedido passou a ser que o usuário comum não tenha acesso, e a restrição agora é
de autorização.

- **No servidor:**
  - o `/trace` e o `/documents/{tenant}/{chave}/download` exigem um dos papéis de uma lista só no `Program.cs`
    (`RawTraceRoles`, hoje `["Admin"]`);
  - quem não tem o papel recebe 403 antes do handler, e o armazenamento das fotos nem é lido;
  - o Admin segue com a regra do ADR-0028, com o 404 igual para outro tenant e para o documento sem fotos.
- **O gancho do Suporte:** criar o papel é acrescentar `"Support"` ao `RawTraceRoles` do servidor, ao `RAW_JSON_ROLES` da
  tela e ao `UserRole`. O papel não é criado agora.
- **Na tela:**
  - a lista de papéis da tela (`RAW_JSON_ROLES`, num módulo só) decide os botões "Visualizar JSON" e "Baixar arquivos";
  - quem não está na lista não vê nenhum dos dois, e a tela nem chama o `/trace`;
  - a primeira vista vem da leitura do desfecho (D8), para todos os papéis.
- **O modal do JSON:**
  - **o que abre:** o "Visualizar JSON" abre um modal próprio, por cima do detalhe, com as quatro abas (origem, domínio,
    destino e resposta);
  - **quando carrega:** o `useTrace` só é chamado com o modal aberto;
  - **como fecha:** fechar o modal volta ao detalhe. O `Modal` passa a fechar no Esc só o de cima, porque hoje cada modal
    aberto fecha no mesmo Esc.
- **O chamado de suporte:** continua anexando os zips no servidor, para qualquer papel. O zip vai para o suporte, e o
  pedido devolve só o id e o link do chamado. Se o portal de chamados mostrar os anexos a quem abriu, o Viewer os vê por
  lá. Isso fica nos riscos e no STATUS.

**Alternativas descartadas:**

- **Só esconder os botões.** Foi a primeira versão. O Viewer continuaria baixando o zip e lendo o `/trace` pela API.
- **Bloquear só o zip.** O `/trace` entrega as mesmas fotos, e o bloqueio seria só aparente.

### D12. O selo de duas cores e o `inboundScans`

- **A Application:** o `AutomaticIntegration` ganha o `Scans(profile, scanningOrigins)`, puro e ao lado do `IsOn`. O
  `IsOn` passa a usá-lo.
- **O `/info`:** passa a devolver `{ environment, automaticIntegration, inboundScans }`. Só booleanos, e continua
  aberto a qualquer usuário autenticado.
- **O `App.tsx`:**

  | Estado | Selo |
  |---|---|
  | `/info` ainda não respondeu, ou falhou | nenhum |
  | `inboundScans` falso | nenhum |
  | varre, com a integração ligada | verde (`--ok-text`), "Integração automática ligada" |
  | varre, com a integração desligada | vermelho (`--error-text`), "Integração automática desligada" |

  A invalidação do `['info']` ao salvar o perfil já existe.

**Alternativa descartada: trocar o `automaticIntegration` por um estado de três valores** (`"on"`, `"off"`, `null`).
É mais compacto, mas muda o tipo de um campo que existe. Um booleano novo é aditivo.

### D13. "Automática" também no valor gravado: `Automatic`, com migração de dados

**Decisão: o valor gravado muda com o rótulo.**

- **O motivo:** o `RealTime` gravado continuaria dizendo "tempo real" a quem lê o banco, os logs do seed ou o
  contrato do `DocumentGroup`. É a confusão que o D12 da fatia anterior registrou. Um rótulo que diz uma coisa sobre um
  valor que diz outra seria a próxima.
- **O conceito:** `Automatic` é "entrou sem ação humana" (coletor, drop, evento). É o que o `SourceMode = null` já
  significa.
- **Um detalhe:** o nome coincide com o do interruptor, mas o conceito continua distinto, e o D12 da fatia anterior
  segue valendo.

**Onde muda:**

- **Na escrita:** `SqlProcessingStore` (`?? "Automatic"`), também na criação de linha do `UpsertAsync` (D4).
- **Na leitura:** o `SqlDocumentQueries` serve `Trigger ?? "Automatic"`.
- **No seed:** o de demonstração troca `RealTime` por `Automatic`.
- **Na tela:**
  - `TRIGGER_LABEL`: `Automatic: 'Automática'`, e a queda padrão também é "Automática";
  - o comentário de `types.ts` acompanha.

**A migração `RenameRealTimeTrigger`:**

- **`Up`:** `UPDATE ProcessedDocuments SET [Trigger] = 'Automatic' WHERE [Trigger] = 'RealTime'`.
- **`Down`:** o inverso.
- **O que não muda:** o esquema. O snapshot do EF não muda, e a migração é só SQL.
- **Como se prova:** os testes criam o banco por `EnsureCreated`, então ela é provada subindo o host contra o SQL
  Server do `docker compose`.
- **Na fila:** nada a migrar, porque o `RealTime` nunca viajou nela.

**Alternativa descartada: trocar só o rótulo.** Custa menos agora e empurra a divergência para frente. Não há cliente
em produção, e front e back sobem juntos: este é o momento mais barato.

### D14. Exclusão de agendamento: `DELETE` físico, escopado ao tenant, e o histórico fica

- **Confirmado no código:**
  - não há FK entre `IntegrationExecutions` e `ScheduledIntegrations`;
  - a execução guarda o modo, a empresa, a filial, o período, a contagem e a data;
  - a leitura das execuções não depende do agendamento.

  O histórico não se perde.
- **O que sobra:** o `ScheduleId` da execução passa a apontar para um id que não existe. Não é exposto em lugar nenhum.
  No SQL Server, o `IDENTITY` não reusa o id, então ele nunca vai apontar para outro agendamento. No SQLite dos testes,
  o id pode ser reusado, o que não afeta nada exposto.
- **A porta:** o `IScheduleStore` ganha `DeleteAsync(int id)`, que devolve `bool` e é escopado ao tenant, como o
  `DeactivateAsync`. O id de outro tenant dá `false`.
- **A API:** `DELETE /schedules/{id:int}` responde `204` ou `404`, com a mesma regra de acesso do `deactivate`.
- **Corrida com o agendador:** o `IntegrationScheduler` lista os vencidos e depois chama o `RescheduleAsync`. Se o
  agendamento foi excluído no meio, o `RescheduleAsync` não acha a linha e retorna, como já faz. A execução em curso
  termina e fica no histórico.
- **A tela:** o botão "Excluir" em cada linha da lista de agendamentos, com confirmação: "Excluir o agendamento? A
  exclusão não se desfaz, e o histórico de execuções fica." A lista é invalidada no sucesso.

**Alternativa descartada: exclusão lógica** (`Deleted = true`). Foi descartada porque é um terceiro estado ao lado do
`Active`, que já é a pausa, para um dado que ninguém lê depois de excluído. O histórico que importa está nas
execuções.

### D15. O mock recusa como o sandbox

- **O `rejeitar`:** passa a devolver 400 com o ProblemDetails do sandbox:
  - `type`, `title` "One or more validation errors occurred.", `status` 400 e um `traceId`;
  - `errors`, com o motivo do toggle num campo. A sugestão é `{"documento": ["<motivo>"]}`.
- **O `erro` da consulta:** continua `{ id, status: "erro", mensagens }`. A consulta de status não foi exercitada no
  sandbox, e não se fabrica forma.
- **O ponta a ponta** (`DispatchToMockTests`, caso "rejeitar"):
  - o motivo é resumo, sem `title`;
  - a foto não tem `type`, `title` nem `status`;
  - as omissões estão na foto.

É a regra da memória do projeto: o que foi verificado no sandbox entra no mock na mesma fatia.

### D16. ADR-0030 e as revisões

`docs/adr/0030-grupo-pelo-estabelecimento-e-motivo-pela-foto.md`, pelo template, registra:

- **O grupo:** a empresa e a filial vêm do estabelecimento próprio, e a descoberta leva o grupo para a nota que não
  chega à montagem (D1, D4);
- **O dia:** o dia da nota é a data fiscal, no fuso de quem emitiu, sem conversão, com o mesmo critério para a nota
  montada e para a ignorada (D1);
- **As fotos cruas:** o `/trace` e o zip só para Admin, e a primeira vista pela leitura do desfecho (D8, D11);
- **O critério:** os cards contam pela data fiscal, com recorte do dia, de propósito (D6);
- **O canônico:** a subida para a v3 sem tenant em produção (D2, D3);
- **O motivo:** vira resumo, a lista mora na foto, e a foto perde o ruído, com a sonda crua (D7, D8, D10);
- **A omissão:** é ressalva da nota aceita, e fica gravada na foto do envio (D9).

**As revisões**, no formato das anteriores:

- **ADR-0026:** nota no item da omissão visível. A omissão sai do motivo de falha e fica na foto.
- **ADR-0027 §8:** nota na quarta foto. O corpo com mapa de erros perde o ruído, e o envelope do envio leva as
  omissões.

O `docs/adr/README.md` ganha a linha do 0030 e as marcas de revisão.

### D17. Testes

| Pedido | Onde |
|---|---|
| Empresa e filial do estabelecimento; XML pela derivação | `GoodsInvoiceMetadataExtractorTests` |
| O dia pela data fiscal; XML no fuso do `dhEmi` (`23:30-04:00` fica no dia) | `GoodsInvoiceMetadataExtractorTests` |
| Estabelecimento na montagem, CNPJ só dígitos, numa nota de terceiro | `D365GoodsInvoiceAssemblerTests`, sobre as fixtures regravadas |
| Data fiscal 2026-08-07 com emissão `2026-08-08T01:30:00Z` | `D365GoodsInvoiceAssemblerTests` |
| O mesmo cabeçalho dá o mesmo dia pela descoberta e pela montagem com o extrator | teste no `FiscalHub.Adapters.Ingress.D365Poll.Tests` |
| Canônico v3 com `FiscalEstablishment` | `D365CanonicalizerTests` (`v = 3`) |
| `$select` da descoberta e o grupo na referência | `D365ChangeFeedTests` |
| Round-trip da referência com `Metadata`, e mensagem antiga sem ele | `QueuedDocumentProcessorTests` ou o teste de serialização do adapter |
| Ignorada com grupo e modo; grupo da montagem preservado; referência antiga | `SqlProcessingStoreTests`, `DocumentRouterTests` |
| Ignorada no dia dela: conta no `Total`, fora do `ComErro` | `SqlProcessingStoreTests` ou um teste da `SqlDocumentQueries` |
| `RealTime` → `Automatic` na escrita e na leitura | `SqlProcessingStoreTests` |
| Resumo sem `title`; muitos campos; sem mapa | `PlatformMessageTests`, `SandboxFixtureTests` |
| Foto sem ruído, com `traceId` e URL; status nativo preservado | teste da função do D10 e `AvalaraComplianceDispatcherTests` |
| Omissões na foto; recusa sem "Enviado sem" | `AvalaraComplianceDispatcherTests` |
| Ressalva sai na falha da consulta e no sem retorno | `SqlProcessingStoreTests` (`MarkPolledAsync`) |
| `inboundScans` | `AutomaticIntegrationTests` |
| Exclusão escopada; execuções intactas | `SqlScheduleStoreTests`, `SqlExecutionStoreTests` |
| Ponta a ponta com a recusa do sandbox | `DispatchToMockTests` |
| Leitura do desfecho: a lista da consulta antes da do envio, as omissões, e nada mais das fotos | teste da leitura na Application, sobre a fixture do sandbox |
| Leitura de outro tenant ou sem fotos é "não encontrado" | teste da consulta da leitura, com o leitor falso |

A tela (lista, marca, modal do JSON, botões por papel, selo, "Excluir", máscara) não tem suíte de componente. Ela é
provada no `npm run build` e na passada manual do grupo final. A exigência de papel no `/trace` e no zip mora no host,
que não tem projeto de teste, e é provada no manual: 403 para o Viewer e 200 para o Admin.

## Risks / Trade-offs

- **[Subir a v3 reenvia uma vez cada nota já aceita que for relida]** → Hoje, nenhuma nota foi aceita, e não há tenant
  em produção (D3). O item do STATUS do hash de transição continua aberto, como pré-requisito da primeira subida com
  cliente.
- **[Uma nota já gravada com o dia UTC muda de dia na remontagem]** → Uma NF-e do D365 com data e hora de emissão
  depois das 21h de Brasília, gravada antes desta mudança, passa a ter o dia fiscal quando for remontada. É a correção.
  No fiscosysdev, nenhuma nota tem a data e hora preenchida, então nada muda de dia.
- **[O "hoje" do navegador e o dia fiscal de outro fuso]** → Quem vê o dashboard em Brasília e tem estabelecimento em
  Manaus compara o seu "hoje" com o dia fiscal de Manaus. Entre 0h e 1h de Brasília, a nota de Manaus emitida às 23h
  ainda é "ontem" para o card. É o dia da nota como o documento o registra, e não um erro de conversão. Os filtros por
  período (STATUS) abrem a janela.
- **[A invariante da data quebrada trava o feed do tenant]** → Sem ramo para a ausência (D4), um `FiscalDocumentDate`
  nulo ou ilegível faz a leitura da página falhar. O efeito é o de um `SysModifiedDateTime` ilegível: a falha é
  registrada no cursor (`LastError`), com o RecId, e a passada seguinte tenta de novo. É alto de propósito. A
  invariante é da origem (documento autorizado ou cancelado tem data de emissão), e um dia inventado esconderia a
  quebra.
- **[O código do estabelecimento é maior que 20]** → A tarefa 1.1 confere o EDT antes de gerar a migração (D5).
- **[O modal do grupo lista notas de outro tipo do mesmo dia]** → A consulta do modal é por empresa, filial e data, e
  a linha agora é por tipo e modo também. O efeito é uma lista maior que o total da linha, só quando o mesmo
  estabelecimento tem tipos diferentes no mesmo dia. Filtrar o modal pelo tipo e pelo modo muda a rota
  `/groups/{c}/{b}/{d}/documents` e fica como item do STATUS.
- **[O portal de chamados mostrar os zips a quem abriu o chamado]** → O chamado anexa os zips no servidor, para
  qualquer papel (D11). Se o portal do Freshdesk mostrar os anexos ao solicitante, o Viewer vê as fotos por lá. Decidir
  o que o chamado anexa quando quem o abre não pode ver as fotos cruas fica como item do STATUS.
- **[A lista de papéis em dois lugares]** → O `RawTraceRoles` no servidor e o `RAW_JSON_ROLES` na tela precisam andar
  juntos. O servidor é quem manda: uma divergência só esconde ou mostra um botão que o servidor recusa. O comentário de
  cada lista aponta para a outra.
- **[A leitura do desfecho conhece o nome dos arquivos da foto]** → A convenção é do `TracePaths` (Infrastructure). A
  leitura, na Application, a usa pelo sufixo, como a tela fazia. Um teste da leitura fixa os nomes.
- **[A humanização genérica não traduz o nome do campo]** → As mensagens da plataforma já dizem o campo em português.
  O dicionário por destino fica como alternativa (D8).
- **[A lista da tela e o resumo do `Reason` saem de lugares diferentes]** → Os dois saem da mesma resposta redigida: a
  foto e o motivo, pela regra do ADR-0027. O resumo só conta e nomeia, e não repete a lista.
- **[A ressalva da nota aceita depende de "aceita tem só ressalva no `Reason`"]** → A regra já existe
  (`MarkPolledAsync`) e passa a valer também na falha (D9). Um teste do store a fixa.
- **[Contrato do front muda, e o frontend é único (ADR-0020)]** → Não há cliente em produção, e front e back sobem
  juntos.

## Migration Plan

1. **Deploy:** as duas migrações rodam na subida do host (`Migrate`):
   - `WidenBranchCode`: esquema;
   - `RenameRealTimeTrigger`: dados.

   Front e back sobem na mesma versão.
2. **Fixtures:** o `Record-D365Fixtures.ps1` ganha o `$select` novo da descoberta e o `FiscalEstablishment` na
   montagem, e regrava contra o fiscosysdev. A gravação é opt-in, com `az login`. As derivadas são refeitas.
3. **Dev:** a passada seguinte contra o fiscosysdev regrava as 14 notas:
   - **as 9 NFS-e:** ganham o grupo pela descoberta;
   - **as 5 NF-e 55:** ganham o grupo pela montagem, com empresa de 14 dígitos e o dia fiscal.

   As linhas antigas com empresa de 8 dígitos, que são o fornecedor, são sobrescritas pelo `RecordMetadataAsync` na
   remontagem, porque a impressão v3 é nova e a nota rejeitada é remontada de qualquer jeito. Para rever o `startFrom`,
   o rebobinamento do RUNNING §6.
4. **Rollback:** reverter o commit e aplicar os dois `Down`. As linhas já regravadas com o grupo novo continuam
   legíveis pela versão anterior: são só texto nas mesmas colunas.

## Open Questions

- **A redação da confirmação do "Excluir" e da marca "Enviado com ressalvas".** As sugestões estão no D14 e no D9. A
  redação final pode mudar na revisão da tela, sem mexer na spec.
- **O campo do mapa `errors` no mock** (D15). A sugestão é `documento`, e qualquer caminho serve ao teste.
