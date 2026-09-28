A ordem vai da origem para a tela:

- **Grupos 1 a 3, o dado.** O estabelecimento na montagem, o grupo na descoberta e na ignorada, e o modo
  "Automática". O grupo 1 começa pelas fixtures, porque a montagem passa a exigir o `FiscalEstablishment` na resposta.
- **Grupos 4 e 5, o desfecho e o que a tela consome.** O motivo, a foto, o mock, o selo e a exclusão de agendamento.
- **Grupo 6, a tela.**
- **Grupos 7 e 8, a documentação e a prova manual do critério de saída.**

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
- [ ] 7.2 `docs/STATUS.md`:
  - **fechar** "O `CompanyCode` mostra o fornecedor numa nota de terceiro" e "O rótulo 'Tempo real' da lista de
    grupos", só com a evidência dos testes e da prova do grupo 8;
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

  **Parcial (2026-09-28).**
  - **Feito:**
    - os itens novos estão abertos, no formato **Falta / Prova / Sintoma**: os filtros dos cards, com o critério escrito
      como comportamento correto; o modal que não filtra pelo tipo e pelo modo; o tamanho do código no AOT, da 1.1; e o
      diretório com o CNPJ de 14 dígitos;
    - as notas da v3 do canônico e do formato do erro estão feitas.
  - **Aberto:** o fechamento dos dois itens. Eles estão anotados como "implementado, falta a prova manual do grupo 8",
    porque a tarefa pede a evidência do grupo 8 para fechar
- [x] 7.3 `docs/RUNNING.md`:
  - o critério dos cards, na seção do dashboard;
  - a exclusão de agendamento;
  - o "Visualizar JSON" só para Admin;
  - as duas migrações novas, na subida do host
- [x] 7.4 `docs/avalara-sandbox-primeiro-envio.md`: uma nota nos achados. O corte em 1000 caracteres e o `title` no
  motivo foram resolvidos nesta change, e a lista inteira vem da foto

## 8. Prova manual do critério de saída

- [ ] 8.1 Preparar: `docker compose up -d`, `az login` e o host e o dashboard de pé. As migrações `WidenBranchCode` e
  `RenameRealTimeTrigger` aplicadas no log da subida. Rebobinar o tenant-a pelo RUNNING §6, para as 14 notas da `brmf`
  serem relidas
- [ ] 8.2 Numa passada contra o fiscosysdev, a tabela de grupos mostra:
  - o CNPJ completo do estabelecimento próprio, formatado, e o código de filial vindos do 365;
  - as 14 notas nas datas fiscais delas, com as 9 NFS-e ignoradas incluídas;
  - o "Tipo" como "Automática".

  Os cards de hoje mostram 0, que é o correto: nenhuma nota tem data fiscal de hoje. Conferir no banco que nenhuma das
  14 linhas ficou com data nula, e que a data de referência de cada uma é o `FiscalDocumentDate` dela no F&O, nas
  montadas e nas ignoradas
- [ ] 8.3 Abrir uma NF-e 55 recusada como Viewer: o motivo é uma lista de campos, com as mensagens em português, sem
  "One or more validation errors occurred.", sem "Enviado sem" e sem botão "Visualizar JSON". Como o mesmo Viewer,
  conferir que o "Baixar arquivos" ainda traz o zip com as fotos. É o esperado: o botão é apresentação, e não
  autorização (D11)
- [ ] 8.4 Abrir a mesma nota como Admin: o "Visualizar JSON" abre as quatro abas. A foto da resposta tem o método, a
  URL do sandbox e o `traceId`, não tem `type`, `title` nem `status` no corpo, e traz as omissões da
  `BRMF06-110000027` e da `BRMF06-110000031`
- [ ] 8.5 O selo: verde com o poll ligado e vermelho depois de desligar pela tela, sem recarregar. Com um adapter que
  não varre, some
- [ ] 8.6 Criar um agendamento, deixá-lo disparar ou rodar uma execução ligada a ele, e excluí-lo pela tela,
  confirmando. O agendamento some, e a execução continua na aba de execuções
- [ ] 8.7 Registrar a prova no STATUS, com as linhas do log e o que foi só conferência visual, como na change anterior
