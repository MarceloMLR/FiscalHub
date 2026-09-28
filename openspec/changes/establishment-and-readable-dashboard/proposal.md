## Why

O primeiro envio real ao sandbox da Avalara (2026-09-27) mostrou uma tela que não é apresentável nem acionável:

- **A empresa e a filial saem do lugar errado.** Numa nota de entrada de terceiro, o que aparece é o fornecedor, e
  não o estabelecimento próprio.
- **O motivo da recusa não se lê.** É uma linha de 1000 caracteres, cortada, com o `title` do ProblemDetails no fim
  e a omissão do hub misturada ao erro da plataforma.
- **O JSON cru vem na primeira vista.**
- **As ignoradas somem.** As 9 NFS-e do fiscosysdev não entram em nenhum grupo nem em nenhum card, porque a nota
  ignorada não guarda empresa, filial, data nem modelo.

A tela precisa mostrar o dado certo, na língua de quem a usa, antes da fatia seguinte, cujo critério de saída é uma
nota aceita.

## What Changes

- **Empresa, filial e dia vêm do D365.**
  - **O que muda no domínio:** a `GoodsInvoice` ganha dois dados opcionais:
    - o estabelecimento próprio: o CNPJ completo (`FiscalEstablishmentCNPJCPF`, só dígitos) e o código
      (`FiscalEstablishment`);
    - a data fiscal (`FiscalDocumentDate`): o dia, sem hora e sem fuso.
  - **Quem preenche:** o source do D365.
  - **Quem usa:** o extrator, quando o dado existe. O `CompanyCode` passa a ser o CNPJ de 14 dígitos, o `BranchCode`, o
    código do estabelecimento, e a data de referência, a data fiscal.
  - **O caminho de XML:** mantém a derivação pelo emitente (8 + 4 dígitos). É andaime de dev.
  - **Efeito na impressão:** o `FiscalEstablishment` entra no `$select` da montagem, o canônico muda, e a `Version`
    sobe de 2 para 3. O efeito, dito antes de aplicar, está no design (D3). A data fiscal já estava no `$select` e não
    muda nada.
- **O dia de uma nota é a data fiscal, no fuso de quem emitiu (D1).**
  - **A regra:** o hub não converte a data para UTC nem para um fuso fixo. O dia é a data que o próprio documento
    registra:
    - no D365, o `FiscalDocumentDate`, na nota montada e na ignorada;
    - no XML, a data do `dhEmi` no fuso que ele traz.
  - **O que isso corrige:** hoje, a nota montada do D365 cai no dia UTC da emissão, e a ignorada, no dia fiscal. Num
    fuso UTC-3, toda nota emitida depois das 21h cai no dia seguinte. Duas notas da mesma noite iriam para dias
    diferentes nos cards, conforme fossem processadas ou ignoradas. Não é caso de borda, é rotina.
- **A nota ignorada guarda o próprio grupo.**
  - **O que muda na descoberta:** o `$select` ganha `FiscalDocumentDate`, `FiscalEstablishmentCNPJCPF` e
    `FiscalEstablishment`.
  - **O que a referência leva:** empresa, filial, data de referência, número e modelo.
  - **Quem grava:** o registro do documento, inclusive quando ele é ignorado sem montagem. Assim, toda nota do D365
    entra no grupo e nos cards da data fiscal dela.
  - **Sem valor padrão:** a data de referência nunca falta na origem. Não há valor padrão, nem queda para a data de
    processamento, nem ramo para a ausência.
  - **O que não sobe a `Version`:** o `$select` da descoberta não entra no canônico (D2).
- **O critério dos cards fica escrito.** Os cards contam pela data de referência (a data fiscal), com recorte do dia.
  As notas de 2016 do fiscosysdev ficarem fora é o comportamento correto. O registro está na spec, no código e no
  STATUS.
- **O motivo da recusa vira lista legível.**
  - **De onde a tela lê:** da foto da resposta, que tem o mapa `errors` do ProblemDetails, com a lista de mensagens
    por campo. É ele que vira a lista de campos na tela. Isso resolve o corte em 1000 caracteres.
  - **O `Reason`:** vira resumo curto, com a contagem e os primeiros campos.
  - **O `title`:** o "One or more validation errors occurred." sai do motivo.
- **A omissão sai do erro e vira ressalva.**
  - **Na nota recusada:** o "Enviado sem: …" sai do motivo, tanto na recusa do envio quanto na da consulta.
  - **Na nota aceita:** continua visível, como a marca discreta "Enviado com ressalvas", que abre o detalhe.
  - **Na foto:** a omissão passa a ser gravada na foto da resposta do envio, em todos os casos. Hoje ela não está em
    nenhuma foto, só no `Reason` (design, Context).
- **A foto perde o ruído.**
  - **O que sai:** no ProblemDetails com o mapa `errors`, saem o `type` (link fixo de RFC), o `title` (genérico) e o
    `status` repetido.
  - **O que fica:** o método, a URL e o `traceId`. A URL prova para qual ambiente a nota foi, e o `traceId` é como se
    abre chamado na Avalara.
- **O JSON cru sai da primeira vista, e só o Admin o vê.**
  - **Onde fica:** num modal próprio, aberto pelo botão "Visualizar JSON", com as abas de cada foto.
  - **Quem vê:** o botão e o "Baixar arquivos" aparecem só para Admin, com o gancho para um papel de Suporte, sem
    criá-lo agora.
  - **É autorização, e não só tela:** o `/trace` e o zip passam a exigir o papel, com 403 para os demais (D11).
  - **A primeira vista:** a lista do motivo e as omissões vêm de uma leitura do desfecho, aberta a qualquer usuário do
    tenant, que devolve só isso (D8).
  - **O cabeçalho:** "Rastreabilidade: origem → domínio → destino" sai do detalhe.
- **O selo mostra o estado.**
  - **Quando aparece:** sempre que o adapter de entrada varre.
  - **Cor:** verde quando ligado, vermelho quando desligado.
  - **Adapter que não varre:** o selo some.
  - **Contrato:** o `/info` ganha o `inboundScans`.
- **Agendamento ganha exclusão**, na API (`DELETE /schedules/{id}`) e na tela, com confirmação. As execuções guardam os
  próprios dados e não têm chave estrangeira, então o histórico fica (D14).
- **"Tempo real" vira "Automática".**
  - **O valor gravado também muda:** o `Trigger` passa de `RealTime` para `Automatic`, e uma migração de dados
    reescreve as linhas existentes.
  - **As ignoradas:** passam a gravar o `Trigger` também.
- **O mock imita a recusa verificada.** O "rejeitar" passa a devolver o ProblemDetails do sandbox, e não o formato
  presumido `{"mensagens": [...]}`.
- **ADR-0030.** Registra o grupo pelo estabelecimento próprio e o motivo a partir da foto. Revisa o ADR-0026 (a
  omissão) e o ADR-0027 §8 (a foto).
- **BREAKING (contrato do front e dado gravado):**
  - o `DocumentGroup.trigger` troca `RealTime` por `Automatic`;
  - o `/info` ganha o `inboundScans`;
  - o `Reason` da recusa muda de forma;
  - a foto da resposta muda de forma (sem o ruído, e com as omissões);
  - o `/trace` e o zip passam a dar 403 para quem não é Admin, e a primeira vista passa a usar a leitura do desfecho.

  Front e back sobem juntos, e não há cliente em produção.

## Capabilities

### New Capabilities

- `document-grouping`: de onde saem a empresa, a filial, a data de referência, o modelo e o modo de cada nota,
  inclusive a ignorada. Diz também qual fuso define o dia de uma nota: o de quem emitiu, sem conversão, com o mesmo
  critério para a nota montada e para a ignorada. Cobre também o que os cards e a tabela de grupos contam e mostram: o critério da data fiscal
  com recorte do dia, o CNPJ formatado na tela e o rótulo "Automática".
- `integration-schedules`: a exclusão de um agendamento, na API e na tela, sem perder o histórico de execuções. Só a
  exclusão entra, sem backfill do resto.

### Modified Capabilities

- `compliance-dispatch-outcome`:
  - na rejeição síncrona, o motivo vira resumo curto, sem o `title` do ProblemDetails e sem as omissões;
  - na rejeição assíncrona, o motivo sai sem as omissões;
  - a omissão visível passa a ser ressalva de nota aceita. Ela sai do motivo de falha e fica na foto.
- `platform-response-trace`:
  - a foto do envio perde o ruído do ProblemDetails e passa a levar as omissões do pedido;
  - o detalhe do documento mostra primeiro o motivo como lista e a ressalva, vindos de uma leitura do desfecho aberta a
    qualquer usuário do tenant;
  - o JSON cru abre num modal próprio, pelo "Visualizar JSON";
  - o `/trace` e o zip passam a exigir Admin, e só o Admin vê os dois botões;
  - o cabeçalho do detalhe perde a linha "Rastreabilidade".
- `tenant-boundary`: nas "Fotos só para o tenant do usuário", o mesmo tenant sem o papel recebe 403.
- `automatic-integration`: o estado mostrado passa a ter as duas cores. Verde é ligado, vermelho é desligado, e não
  aparece nada para adapter que não varre. O `/info` diz se o adapter varre.
- `d365-change-feed`: o `$select` da descoberta ganha a data fiscal e o estabelecimento, e a referência passa a levar
  o grupo da nota.
- `d365-document-assembly`: o documento montado passa a levar o estabelecimento próprio (CNPJ e código) e a data
  fiscal.
- `discovery-queue-consumer`: a nota ignorada passa a ser registrada com o grupo que a referência trouxe e com o modo.

## Non-goals

- **Os seis campos obrigatórios da recusa da Avalara** (`operacao`, `tipoPagamento`, `parceiro.Codigo`,
  `itens[].Item.TipoItem` e as duas `UnidadeMedida.Descricao`). Vão na fatia seguinte, cujo critério de saída é uma
  nota aceita.
- **Os filtros dos cards.**
  - **O que fica registrado:** o período (dia, 7, 15 e 30 dias, com o dia como padrão) e o modelo.
  - **Onde:** no STATUS, como próximo passo. Com eles, um contador separado para as ignoradas deixa de ser necessário.
- **O papel de Suporte.** Só o gancho entra (D11).
- **O hash de transição do canônico** (CNV D17). A subida para a v3 acontece sem tenant em produção, e o item do
  STATUS continua aberto como pré-requisito da primeira subida com cliente (D3).
- **Tradução dos nomes de campo da plataforma** (um dicionário `tipoPagamento` → "Tipo de pagamento"). As mensagens já
  vêm em português. A tela humaniza o caminho de forma genérica, sem conhecer a Avalara (D8).
- **O que o chamado de suporte anexa quando quem o abre não pode ver as fotos cruas.** O chamado continua anexando os
  zips no servidor. Se o portal de chamados mostrar os anexos a quem abriu, é decisão de produto (STATUS).
- **Papel exigido nos agendamentos.** A exclusão segue a regra do desativar, sem papel. Restringir o agendamento a
  Admin é outra fatia.
- **Mudar o `ICompanyDirectory`, o `companies.json` e a descoberta local.** São o caminho de XML de dev, com o código
  de 8 dígitos.

## Impact

- **Domain:** a `GoodsInvoice` ganha o estabelecimento próprio e a data fiscal, os dois opcionais. Nenhuma dependência
  nova.
- **Application:**
  - `Metadata`: o `GoodsInvoiceMetadataExtractor` usa o estabelecimento e a data fiscal quando eles existem;
  - `Inbound`:
    - a `DocumentReference` ganha o `Metadata` opcional (o grupo visto na descoberta);
    - o `AutomaticIntegration` ganha a leitura "varre";
  - `Integrations`: o `IScheduleStore` ganha o `DeleteAsync`;
  - `Tracing`: a leitura do desfecho, uma função pura sobre as fotos que o `DocumentTraceQuery` já devolve.

  Nenhuma porta nova.
- **Infrastructure:**
  - o `SqlProcessingStore` grava o grupo e o modo nas linhas criadas fora da montagem, e deixa a ressalva fora do
    motivo de falha;
  - o `SqlDocumentQueries` troca o valor padrão para `Automatic`;
  - o `SqlScheduleStore` ganha a exclusão;
  - duas migrações: uma reescreve o `Trigger` de `RealTime` para `Automatic`, e a outra alarga o `BranchCode` (D5);
  - o seed troca `RealTime` por `Automatic`.
- **Adapters:**
  - `Ingress.D365Poll`: o `$select` da descoberta e o da montagem, a referência com o grupo, o estabelecimento no
    domínio, o `D365Canonicalizer.Version = 3` e as fixtures regravadas;
  - `Outbound.Avalara`:
    - a `PlatformMessage` passa a fazer o resumo sem o `title`;
    - o `PlatformResponseEnvelope` passa a tirar o ruído e a levar as omissões;
    - o `AvalaraComplianceDispatcher` deixa de juntar a omissão na recusa;
  - `Messaging.ServiceBus`: o round-trip da referência com o `Metadata`.
- **Host:**
  - o `/info` ganha o `inboundScans`;
  - entra o `DELETE /schedules/{id}`;
  - o `/trace` e o zip exigem os papéis do `RawTraceRoles` (hoje, Admin);
  - entra o `GET /documents/{tenant}/{chave}/reading`, a leitura do desfecho.
- **Dashboard:**
  - `DocumentDetail`: a lista do motivo e a ressalva pela leitura, o "Visualizar JSON" num modal próprio, e o cabeçalho
    sem a linha;
  - `NoteDialog`: o "Baixar arquivos" só para Admin;
  - `Modal`: o Esc fecha só o modal de cima;
  - `GroupsPage`: "Automática", o CNPJ formatado, a chave de linha com o tipo e o modo, e o comentário do critério;
  - `GroupModal`: o CNPJ formatado;
  - `App.tsx`: o selo de duas cores;
  - `IntegrationsPage`: "Excluir", com confirmação;
  - `types.ts` e `client.ts`.
- **Tools:**
  - `MockComplianceApi`: a recusa no formato do sandbox;
  - `d365-fixtures`: o `$select` novo e as gravações refeitas.
- **Testes:**
  - o extrator;
  - a montagem e o canônico v3;
  - o feed com o `$select` e a referência;
  - o roteador e o store com a ignorada agrupada;
  - a consulta de grupos, com a ignorada no dia dela;
  - a `PlatformMessage` e o envelope sobre a fixture real;
  - o dispatcher sem a omissão na recusa;
  - o `MarkPolledAsync`;
  - a exclusão do agendamento, com as execuções intactas;
  - o `AutomaticIntegration`;
  - o ponta a ponta contra o mock.
- **Docs:**
  - `docs/adr/0030-*.md`, com o índice e as notas no 0026 e no 0027;
  - `docs/STATUS.md`:
    - fecha "O `CompanyCode` mostra o fornecedor" e "O rótulo 'Tempo real'", com evidência;
    - abre os filtros dos cards;
  - `docs/RUNNING.md`: o critério dos cards e a exclusão de agendamento.
