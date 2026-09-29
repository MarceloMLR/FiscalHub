A ordem vai da origem para a tela:

- **Grupos 1 a 3, o dado.** O estabelecimento na montagem, o grupo na descoberta e na ignorada, e o modo
  "Automática". O grupo 1 começa pelas fixtures, porque a montagem passa a exigir o `FiscalEstablishment` na resposta.
- **Grupos 4 e 5, o desfecho e o que a tela consome.** O motivo, a foto, o mock, o selo e a exclusão de agendamento.
- **Grupo 6, a tela.**
- **Grupo 7, a documentação.**
- **Grupo 8, o JSON num modal próprio e as fotos cruas só para Admin.** Entrou depois do grupo 7, por pedido de
  2026-09-28, e revisa o que o 6.4 e o 7.1 fizeram (D8 e D11 revisados).
- **Grupo 9, a prova manual do critério de saída.**

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. O grupo da tela termina com o
`npm run build`.

## 1. O estabelecimento próprio e o dia fiscal, da montagem ao grupo (D1, D3, D5, `d365-document-assembly`, `document-grouping`)

- [ ] 1.1 Fixtures e tamanho do código:
  - `tools/d365-fixtures/Record-D365Fixtures.ps1`: acrescentar `FiscalEstablishment` ao `$select` do cabeçalho da
    montagem, e `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e `FiscalEstablishment` ao da descoberta;
  - regravar contra o fiscosysdev (opt-in, `az login`) `notes/*`, `scope/*` e `snapshot/headers`, e refazer as
    derivadas (`*.derived.json`), pela edição descrita no `README.md` das fixtures;
  - anotar no `README.md` das fixtures os valores do `FiscalEstablishment` na `brmf`;
  - conferir nos metadados do F&O o tamanho do EDT do `FiscalEstablishmentId`, e anotar o valor no D5 do design

  **Parcial (2026-09-27).**
  - **O que foi feito e verificado:**
    - o `$select` do cabeçalho ganhou o `FiscalEstablishment`;
    - a regravação contra o fiscosysdev só acrescentou esse campo, conferido arquivo a arquivo nos 9 cabeçalhos;
    - os valores (`Matriz`, `SP-01`, `SAL-01`) estão anotados no `tools/d365-fixtures/README.md`.
  - **Onde a tarefa não se aplica ao pé da letra:**
    - o script não tem consulta de descoberta própria. O `snapshot/headers` já traz os campos da descoberta, e o
      comentário do script diz isso;
    - não existem derivadas em arquivo, porque os testes as montam em memória. Não houve o que refazer.
  - **O que ficou aberto:** o tamanho do EDT. Ele não é publicado no `$metadata` do OData nem no CDM, e fica para uma
    conferência no AOT. A migração usa 20 (D5)
- [x] 1.2 Teste primeiro (`GoodsInvoiceMetadataExtractorTests`):
  - **com o estabelecimento:** empresa é o CNPJ de 14 dígitos, e filial é o código. Vale para nota de terceiro, com o
    fornecedor como emitente, e para nota própria;
  - **sem o estabelecimento:** a derivação de hoje (8 + 4 dígitos do emitente);
  - **código vazio:** a filial fica vazia, sem derivar do CNPJ;
  - **com a data fiscal:** a data de referência é ela, mesmo com uma `IssueDate` de `2026-08-08T01:30:00Z` e a data
    fiscal 2026-08-07;
  - **sem a data fiscal (XML):** a data do `dhEmi` no fuso dele. `2026-06-01T22:30:00-03:00` e
    `2026-06-01T23:30:00-04:00` dão 2026-06-01
- [x] 1.3 Teste primeiro (`D365GoodsInvoiceAssemblerTests`, sobre as fixtures regravadas):
  - a nota de entrada de terceiro tem o estabelecimento com CNPJ `44278225000180`, só dígitos, e o código lido. Esse
    CNPJ é o do destinatário, e não o do emitente;
  - um cabeçalho com `FiscalDocumentDateTime = 2026-08-08T01:30:00Z` e `FiscalDocumentDate = 2026-08-07T12:00:00Z` dá
    data fiscal 2026-08-07 e `IssueDate` 2026-08-08T01:30:00Z;
  - com o `FiscalDocumentDateTime` vazio (1900), a data fiscal é a do `FiscalDocumentDate`, como hoje
- [x] 1.4 Teste primeiro (`D365CanonicalizerTests`): o canônico traz `v = 3`, e o cabeçalho traz o
  `FiscalEstablishment`
- [x] 1.5 Implementar:
  - Domain: o record do estabelecimento (`TaxId`, `Code`), a `GoodsInvoice.Establishment` e a
    `GoodsInvoice.FiscalDate` (`DateOnly?`), as duas opcionais;
  - `D365GoodsInvoiceSource.HeaderSelect` com o `FiscalEstablishment`, e o assembler preenchendo o `Establishment` e
    a `FiscalDate`. O dia sai do `FiscalDocumentDate` por uma função só do adapter, a mesma que a descoberta usa no
    grupo 2, sem conversão de fuso;
  - `D365Canonicalizer.Version = 3`, com o comentário da versão dizendo o que mudou e que o comentário do `$select`
    vale para a montagem (D2);
  - o extrator com a regra do D1, estabelecimento e dia, e o comentário dele atualizado. O comentário diz qual fuso
    define o dia: o de quem emitiu, sem conversão
- [x] 1.6 `dotnet build` com 0 warnings e `dotnet test` verde
  - **Como rodou:** o host estava de pé (PID 82444) e travava a pasta `bin` dele. O build e os testes rodaram por uma
    solução sem o host, e o host compilou numa pasta de saída à parte. Deu 0 warnings, e os testes passaram

## 2. O grupo na descoberta e na nota ignorada (D2, D4, D5, D6, `d365-change-feed`, `discovery-queue-consumer`, `document-grouping`)

- [x] 2.1 Teste primeiro (`D365ChangeFeedTests`):
  - o `$select` contém `FiscalDocumentRecId`, `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e
    `FiscalEstablishment`;
  - a referência de uma NFS-e com `FiscalDocumentDate = 2026-08-07T12:00:00Z`,
    `FiscalEstablishmentCNPJCPF = 442782250002-60` e `FiscalEstablishment = SP-01` leva empresa `44278225000260`,
    filial `SP-01`, data 2026-08-07, o número e o modelo `SE`
- [x] 2.2 Teste primeiro, do critério único do dia (`FiscalHub.Adapters.Ingress.D365Poll.Tests`): o mesmo
  cabeçalho, com `FiscalDocumentDateTime = 2026-08-08T01:30:00Z` e `FiscalDocumentDate = 2026-08-07T12:00:00Z`, dá
  2026-08-07 pelos dois caminhos:
  - pela descoberta, no `Metadata` da referência;
  - pela montagem, com o `GoodsInvoiceMetadataExtractor` sobre o documento montado
- [x] 2.3 Teste primeiro, no projeto de teste do Service Bus: a referência com o `Metadata` faz o round-trip pela
  serialização da fila. Uma mensagem sem o campo desserializa com `Metadata` nulo
- [x] 2.4 Teste primeiro (`SqlProcessingStoreTests`):
  - **ignorada com grupo:** o `RecordIgnoredAsync` de uma referência com `Metadata` cria a linha com empresa, filial,
    data, número, modelo e `Trigger`;
  - **modo manual:** com `SourceMode = Manual`, o `Trigger` gravado é `Manual`;
  - **a montagem prevalece:** depois do `RecordMetadataAsync`, uma rejeição com `Metadata` diferente não troca o grupo
    gravado;
  - **linha sem grupo:** uma linha existente sem grupo o recebe da referência;
  - **referência antiga:** sem `Metadata`, a linha fica sem grupo, e nada falha;
  - **dead-letter:** o `RecordDeadLetterAsync` com `Metadata` grava o grupo
- [x] 2.5 Teste primeiro, da consulta de grupos (`SqlDocumentQueries`, no projeto de Infrastructure): uma NF-e
  confirmada e uma NFS-e ignorada, com a mesma data. A ignorada entra no grupo do dia dela, conta no `Total` e fica
  fora do `ComErro` e do `EmProcessamento`
- [x] 2.6 Teste (`DocumentRouterTests`): a NFS-e ignorada pelo tipo chega ao store com a referência inteira, com o
  `Metadata`
- [x] 2.7 Implementar:
  - `DocumentReference.Metadata` (`DocumentMetadata?`), com o comentário do contrato (visto na descoberta, para a nota
    que não chega à montagem);
  - `D365ChangeFeed`: o `Select`, o `Row` com os três campos, e o `Map` preenchendo o `Metadata` sem valor padrão. O
    comentário do `Select` passa a dizer que ele não entra na impressão;
  - `SqlProcessingStore.UpsertAsync`: o `Trigger` na criação, e o grupo da referência só quando a linha não tem grupo
- [x] 2.8 Gerar a migração `WidenBranchCode`, que leva o `BranchCode` a `nvarchar(20)`, ou ao tamanho anotado no 1.1,
  nas três tabelas. O `Down` volta a `nvarchar(10)`, e o snapshot acompanha
- [x] 2.9 `dotnet build` com 0 warnings e `dotnet test` verde

## 3. "Tempo real" vira "Automática" (D13, `document-grouping`)

- [x] 3.1 Teste primeiro (`SqlProcessingStoreTests`): a referência sem `SourceMode` grava `Trigger = Automatic`; o
  grupo com `Trigger` nulo é servido como `Automatic`
- [x] 3.2 Implementar:
  - `SqlProcessingStore` e `SqlDocumentQueries` com `Automatic`;
  - o seed de demonstração sem `RealTime`;
  - os comentários do `ProcessedDocument` e do `DocumentGroup`, com `Automatic · Manual · ScheduledDaily ·
    ScheduledOnce`
- [x] 3.3 Criar a migração `RenameRealTimeTrigger`, só com SQL:
  - `Up`: `UPDATE ProcessedDocuments SET [Trigger] = 'Automatic' WHERE [Trigger] = 'RealTime'`;
  - `Down`: o inverso.

  Conferir que o snapshot não muda

  **Feito:** o `Designer` da migração é igual ao snapshot, e o diff do snapshot é só o da `WidenBranchCode`
- [x] 3.4 `grep` por `RealTime` em `src`, `tests`, `tools` e `dashboard/src`. Sobra só o `Down` da migração
  - **Feito depois do grupo 6:** sobram só a migração (o `Up`, o `Down`, o nome e o comentário) e um comentário do
    `ConnectorProfileServiceTests` sobre o campo de perfil removido pelo ADR-0029, que não é o modo
- [x] 3.5 `dotnet build` com 0 warnings e `dotnet test` verde

## 4. O motivo, a foto e o mock (D7, D9, D10, D15, `compliance-dispatch-outcome`, `platform-response-trace`)

- [x] 4.1 Teste primeiro (`PlatformMessageTests` e `SandboxFixtureTests`, sobre o `recusa-no-envio.json`):
  - **o resumo:** diz 6 campos e nomeia `operacao`, `tipoPagamento` e `parceiro.Codigo`, com "e mais 3";
  - **o `title`:** não contém "One or more validation errors occurred.";
  - **muitos campos:** com 12, diz 12 e nomeia três, dentro do limite;
  - **até três campos:** sem o "e mais";
  - **sem mapa:** `{"mensagens": [...]}` e o ProblemDetails só com `title` seguem como hoje
- [x] 4.2 Teste primeiro, da função do D10:
  - **a fixture:** perde o `type`, o `title` e o `status` 400, e guarda o `errors` e o `traceId`;
  - **status diferente:** um `status` 422 numa resposta 400 fica;
  - **sem `errors`:** o corpo fica intacto, e a resposta de status não perde o `status` nativo;
  - **não é JSON:** o corpo fica como texto
- [x] 4.3 Teste primeiro (`AvalaraComplianceDispatcherTests`):
  - **recusa 400 com omissão:** o motivo não contém "Enviado sem", e a foto do envio traz `request.omissions`;
  - **aceite com omissão:** a foto traz as omissões, e o recibo continua com elas;
  - **envio sem omissão:** a foto não tem o campo;
  - **a foto do sandbox:** tem o método, a URL e o `traceId`, e não tem o `type` nem o `title`
- [x] 4.4 Teste primeiro (`SqlProcessingStoreTests`, `MarkPolledAsync`):
  - **`IntegrationError` sobre um enviado com ressalva:** o `Reason` é só o motivo novo;
  - **`Unconfirmed`:** idem;
  - **`Confirmed`:** preserva a ressalva.

  Ajustar o teste atual que espera a junção `motivo | ressalva`
- [x] 4.5 Implementar:
  - a `PlatformMessage` com o caso do mapa (D7);
  - a função do ruído;
  - o `PlatformResponseEnvelope.Build` recebendo o corpo da foto e as omissões, opcionais, com a sonda passando o
    corpo cru e nenhuma omissão;
  - o dispatcher: a foto sem o ruído e com as omissões, e a recusa sem o `| Enviado sem`;
  - o `MarkPolledAsync` com a regra do D9
- [x] 4.6 Mock (D15): o `rejeitar` devolve o ProblemDetails do sandbox, com `type`, `title`, `status`, `traceId` e
  `errors`. Atualizar o comentário do topo, que diz que o formato é presumido
- [x] 4.7 Ponta a ponta (`DispatchToMockTests`, caso "rejeitar"): o motivo é o resumo, sem `title`; a foto do envio
  não tem `type`, `title` nem `status`; e, numa nota com omissão, a foto traz as omissões
- [x] 4.8 `dotnet build` com 0 warnings e `dotnet test` verde

## 5. O selo e a exclusão de agendamento (D12, D14, `automatic-integration`, `integration-schedules`)

- [x] 5.1 Teste primeiro (`AutomaticIntegrationTests`): o `Scans` é verdadeiro para o `Dynamics365` e falso para o
  `iScala` e sem perfil. O `IsOn` continua passando os casos de hoje
- [x] 5.2 Implementar o `Scans` e o `/info` com `inboundScans`
- [x] 5.3 Teste primeiro (`SqlScheduleStoreTests`):
  - o `DeleteAsync` do próprio tenant devolve `true`, e o agendamento some da lista e do `ListDueAsync`;
  - o id de outro tenant devolve `false`, e o agendamento fica;
  - o id inexistente devolve `false`;
  - as execuções do agendamento excluído continuam no `ListRecentAsync`, com os dados delas
- [x] 5.4 Implementar o `IScheduleStore.DeleteAsync`, o `SqlScheduleStore` e o `DELETE /schedules/{id:int}` (`204` ou
  `404`), com a mesma regra de acesso do `deactivate`. Atualizar o comentário dos endpoints de agendamento
- [x] 5.5 Teste (`IntegrationSchedulerTests`): um agendamento excluído entre o `ListDueAsync` e o `RescheduleAsync` não
  quebra a passada
- [x] 5.6 `dotnet build` com 0 warnings e `dotnet test` verde

## 6. A tela (D6, D8, D9, D11, D12, D13, D14)

- [x] 6.1 `types.ts` e `client.ts`:
  - o `/info` com `inboundScans`;
  - o `deleteSchedule(id)`;
  - o comentário do `DocumentGroup.trigger` com `Automatic`
- [x] 6.2 `GroupsPage`:
  - o `TRIGGER_LABEL` com `Automatic: 'Automática'`, e a queda padrão "Automática";
  - a `rowId` com o tipo e o modo;
  - a máscara de CNPJ na coluna "Empresa", só na exibição;
  - o comentário dos KPIs: a data fiscal é o critério, de propósito, no fuso de quem emitiu e comparada com o "hoje"
    do navegador. As notas de 2016 do fiscosysdev ficam fora
- [x] 6.3 `GroupModal`: o título com a máscara de CNPJ, e a consulta com o código em dígitos
- [x] 6.4 `DocumentDetail`:
  - **o cabeçalho:** sem a linha "Rastreabilidade: origem → domínio → destino";
  - **na falha:** a lista de campos do D8, a partir da foto (`status` quando ela tem o mapa, senão `submit`), com a
    queda para o `doc.reason`;
  - **na nota aceita com `reason`:** a marca "Enviado com ressalvas", que abre a lista das omissões da foto, com a
    queda para o `reason`;
  - **na ignorada:** o aviso de hoje;
  - **o JSON cru:** as quatro abas atrás de "Visualizar JSON", só para os papéis de `RAW_JSON_ROLES`
    (`['Admin']`). O comentário junto da constante diz duas coisas (D11):
    - o gancho do Suporte;
    - que é apresentação, e não autorização. O `/trace` e o zip seguem acessíveis a qualquer usuário do tenant, Viewer
      incluído, e restringir de fato é outra fatia
- [x] 6.5 `App.tsx`: o selo do D12. Nada enquanto carrega, nada sem `inboundScans`, verde ligado, vermelho desligado
- [x] 6.6 `IntegrationsPage`: o "Excluir" em cada agendamento, com a confirmação do D14, e a invalidação da lista no
  sucesso. Cancelar não chama a API
- [x] 6.7 `npm run build` verde, com o `tsc --noEmit` sem erro

## 7. Documentação (D16)

- [x] 7.1 Escrever `docs/adr/0030-grupo-pelo-estabelecimento-e-motivo-pela-foto.md`, pelo template. O cabeçalho diz
  "Revisa: ADR-0026, ADR-0027". Acrescentar a linha no `docs/adr/README.md`, com as marcas de revisão no 0026 e no
  0027, e as notas de revisão:
  - no ADR-0026, no item da omissão visível;
  - no ADR-0027, no §8.

  O ADR registra o que o D16 lista, incluindo o fuso do dia (o de quem emitiu, sem conversão, com o mesmo critério nos
  dois caminhos). Registra também que o "Visualizar JSON" é apresentação, e não autorização
- [x] 7.2 `docs/STATUS.md`:
  - **fechar** "O `CompanyCode` mostra o fornecedor numa nota de terceiro" e "O rótulo 'Tempo real' da lista de
    grupos", só com a evidência dos testes e da prova do grupo 9;
  - **abrir** "Filtros dos cards": período (dia, 7, 15 e 30 dias, com o dia como padrão) e modelo. Com eles, o
    contador próprio das ignoradas deixa de ser necessário;
  - **abrir** "O modal do grupo não filtra pelo tipo e pelo modo" (risco do design);
  - **anotar** no item da versão do canônico que a v3 subiu sem tenant em produção, e que o hash de transição continua
    pré-requisito;
  - **anotar** que a descoberta por período com D365, quando existir, precisa do diretório com o CNPJ de 14 dígitos
    (D5).

  Registrar o critério dos cards como comportamento correto, para não ser "corrigido":
  - a data fiscal, no fuso de quem emitiu, sem conversão, com o mesmo critério para a nota montada e para a ignorada;
  - o recorte do dia;
  - as notas de 2016 fora.

  Nenhum item adia o dia fiscal da nota montada: ele entra nesta change (D1). Usar o formato **Falta / Prova /
  Sintoma**

  **Feito.**
  - **Aberto em 2026-09-28:**
    - os itens novos estão abertos, no formato **Falta / Prova / Sintoma**: os filtros dos cards, com o critério escrito
      como comportamento correto; o modal que não filtra pelo tipo e pelo modo; o tamanho do código no AOT, da 1.1; e o
      diretório com o CNPJ de 14 dígitos;
    - as notas da v3 do canônico e do formato do erro estão feitas.
  - **Fechado em 2026-09-29:** os dois itens, com a prova do grupo 9.
- [x] 7.3 `docs/RUNNING.md`:
  - o critério dos cards, na seção do dashboard;
  - a exclusão de agendamento;
  - o "Visualizar JSON" só para Admin;
  - as duas migrações novas, na subida do host
- [x] 7.4 `docs/avalara-sandbox-primeiro-envio.md`: uma nota nos achados. O corte em 1000 caracteres e o `title` no
  motivo foram resolvidos nesta change, e a lista inteira vem da foto

## 8. O JSON num modal próprio e as fotos cruas só para Admin (D8 e D11 revisados, `platform-response-trace`, `tenant-boundary`)

- [x] 8.1 Teste primeiro, da leitura do desfecho (Application.Tests), sobre as fotos da fixture do sandbox e do mock:
  - **a recusa no envio:** 6 campos, na ordem da resposta, com as mensagens, e nada além de `fields` e `omissions`;
  - **a recusa na consulta:** com o mapa na foto da consulta, a lista é a dela, e não a do envio;
  - **as omissões:** saem do `request.omissions` da foto do envio, e ficam vazias sem ele;
  - **sem mapa, ou corpo que não é JSON:** a lista vem vazia;
  - **na consulta da leitura:** o documento de outro tenant e o sem fotos dão "não encontrado", pela regra do
    `DocumentTraceQuery`
- [x] 8.2 Implementar a leitura na Application (`Tracing`) e o `GET /documents/{tenant}/{chave}/reading` no host,
  aberto a qualquer papel do tenant, com o mesmo 404 do `/trace`
- [x] 8.3 Host: o `/trace` e o `/documents/{tenant}/{chave}/download` exigem os papéis de uma lista só
  (`RawTraceRoles`, hoje `["Admin"]`). O comentário dela aponta para o `RAW_JSON_ROLES` da tela e diz que é o gancho do
  Suporte
- [x] 8.4 `dotnet build` com 0 warnings e `dotnet test` verde
- [x] 8.5 Tela:
  - o `RAW_JSON_ROLES` num módulo só, usado pelo detalhe e pelo `NoteDialog`, com o comentário apontando para o
    `RawTraceRoles` do servidor;
  - o `client.ts` com a leitura (`reading`), e o detalhe tirando dela a lista e as omissões (o `platformReason`
    passa a só humanizar o caminho, e a cair para o `reason`);
  - o "Visualizar JSON" abre um modal próprio com as quatro abas, e o `useTrace` só roda com ele aberto;
  - o "Baixar arquivos" do `NoteDialog` só para os papéis da lista;
  - o `Modal` fecha no Esc só o de cima
- [x] 8.6 `npm run build` verde, com o `tsc --noEmit` sem erro
- [x] 8.7 Docs:
  - **ADR-0030:** o item 8 passa a dizer que o `/trace` e o zip exigem o papel, e que a primeira vista vem da leitura;
  - **STATUS:** abre o item "O que o chamado de suporte anexa quando quem o abre não pode ver as fotos cruas";
  - **RUNNING:** o "Visualizar JSON" e o "Baixar arquivos" só para Admin, e o 403 do Viewer no `/trace` e no zip

## 9. Prova manual do critério de saída

**Como a prova foi lida (2026-09-29).** O `host-fatia3.log` é a saída do `dotnet run` por `Tee-Object`, fora do git, em
UTF-16, e as linhas citadas são as do arquivo convertido para UTF-8. Ele tem duas subidas (linhas 108 e 4625). O banco e
a API foram conferidos depois, contra o host de pé desde 2026-09-29 14:05, já com o código do grupo 8. O que é da tela
ficou como conferência visual do usuário, sem linha de log.

- [x] 9.1 Preparar: `docker compose up -d`, `az login` e o host e o dashboard de pé. As migrações `WidenBranchCode` e
  `RenameRealTimeTrigger` aplicadas no log da subida. Rebobinar o tenant-a pelo RUNNING §6, para as 14 notas da `brmf`
  serem relidas
  - **As migrações:** aplicadas nas linhas 35 e 68, e no `__EFMigrationsHistory`.
  - **O rebobinamento:** o cursor foi recriado (linha 6175), e a passada deu 14 referências na fila de descoberta, com 0
    suprimidas (linha 6382). As 5 NF-e 55 foram ao sandbox (linhas 6528 a 6945).
- [x] 9.2 Numa passada contra o fiscosysdev, a tabela de grupos mostra:
  - o CNPJ completo do estabelecimento próprio, formatado, e o código de filial vindos do 365;
  - as 14 notas nas datas fiscais delas, com as 9 NFS-e ignoradas incluídas;
  - o "Tipo" como "Automática".

  Os cards de hoje mostram 0, que é o correto: nenhuma nota tem data fiscal de hoje. Conferir no banco que nenhuma das
  14 linhas ficou com data nula, e que a data de referência de cada uma é o `FiscalDocumentDate` dela no F&O, nas
  montadas e nas ignoradas
  - **No banco (2026-09-29):**
    - as 14 linhas da `brmf` têm empresa `44278225000180` (Matriz) ou `44278225000260` (`SP-01`), onde antes havia só a
      raiz `44278225` e a filial `0001`;
    - as 9 NFS-e ignoradas têm modelo `SE` e data de referência, antes nulos;
    - o modo é `Automatic` em todas, e não há mais nenhuma linha com `RealTime` na base;
    - a data de referência bate com o `FiscalDocumentDate` do snapshot gravado nas 14 (14 de 14).
  - **Na tela:** a tabela, a máscara do CNPJ, a filial, o "Automática" e os cards em 0 foram conferência visual do
    usuário, sem linha de log.
- [x] 9.3 Abrir uma NF-e 55 recusada como Viewer:
  - **o motivo:** uma lista de campos, com as mensagens em português, sem "One or more validation errors occurred." e
    sem "Enviado sem";
  - **os botões:** não há "Visualizar JSON" nem "Baixar arquivos", e o "Abrir chamado" continua;
  - **pela API, com o token do Viewer:** o `/trace` e o `/documents/{tenant}/{chave}/download` dão 403, e a
    `/documents/{tenant}/{chave}/reading` dá a lista

  **Feito (2026-09-29).**
  - **Pela API, com o Viewer do seed (`carlos.dias`), na `BRMF06-110000027`:**
    - o `/trace` e o download deram 403;
    - a leitura deu 200, com 6 campos (o primeiro `operacao`, "'Operacao' não pode ser nulo."), a omissão do
      `IcmsDiff` do item 1, e só as propriedades `fields` e `omissions`.
  - **No banco:** o motivo das 5 NF-e recusadas é o resumo ("… recusou: 6 campos com erro: operacao, tipoPagamento,
    parceiro.Codigo e mais 3"; 12 e 9 campos nas notas de mais itens), sem o `title` e sem "Enviado sem".
  - **Na tela:** a lista e os botões foram conferência visual do usuário, sem linha de log.
- [x] 9.4 Abrir a mesma nota como Admin:
  - **o modal do JSON:** o "Visualizar JSON" abre um modal próprio, por cima do detalhe, com as quatro abas. Fechar ou
    apertar Esc volta ao detalhe, que continua aberto;
  - **a foto da resposta:** tem o método, a URL do sandbox e o `traceId`, não tem `type`, `title` nem `status` no
    corpo, e traz as omissões da `BRMF06-110000027` e da `BRMF06-110000031`;
  - **o "Baixar arquivos":** aparece e baixa o zip

  **Feito (2026-09-29).**
  - **Pela API, com o Admin:** o `/trace` e o download deram 200.
  - **A foto do envio da `BRMF06-110000027` e da `BRMF06-110000031`:**
    - o corpo tem só `errors` e `traceId`;
    - a URL é `https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/fiscal/dfe`;
    - o `request.omissions` traz o `IcmsDiff` do item 1 numa, e o encargo `Other` de 416,25 na outra.
  - **Na tela:** o modal por cima do detalhe e o Esc foram conferência visual do usuário, sem linha de log.
- [x] 9.5 O selo: verde com o poll ligado e vermelho depois de desligar pela tela, sem recarregar. Com um adapter que
  não varre, some
  - **As gravações do perfil pela tela:** estão nas linhas 1072, 6053 e 9382 (`UPDATE [ConnectorProfiles]`).
  - **As cores e o sumiço:** foram conferência visual do usuário, sem linha de log.
  - **O que o banco mostrou depois:** as `InboundSettings` do tenant-a ficaram `{}`, sem URL, empresas, `auth` nem
    `poll`. Parece efeito de trocar o ERP na tela para um adapter que não varre e voltar, mas isso não está provado.
    Está registrado no STATUS, junto com o achado do `startFrom`.
- [ ] 9.6 Criar um agendamento, deixá-lo disparar ou rodar uma execução ligada a ele, e excluí-lo pela tela,
  confirmando. O agendamento some, e a execução continua na aba de execuções

  **Parcial (2026-09-29).**
  - **O que foi feito e verificado:** a exclusão. O agendamento foi criado (linha 798, `INSERT INTO
    [ScheduledIntegrations]`) e excluído (linha 953, `DELETE FROM [ScheduledIntegrations]`), e o sumiço da lista foi
    conferência visual do usuário.
  - **O que não foi exercitado:** o histórico. A `IntegrationExecutions` está vazia, então o agendamento não disparou,
    e não havia execução para continuar na aba. Que as execuções de um agendamento excluído ficam, com os dados delas,
    está provado só por teste
    (`SqlScheduleStoreTests.Executions_of_a_deleted_schedule_stay_in_the_history_with_their_own_data`).
- [x] 9.7 Registrar a prova no STATUS, com as linhas do log e o que foi só conferência visual, como na change anterior
