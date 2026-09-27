# STATUS — FiscalHub

Documento de handoff entre sessões/máquinas. Atualizado ao fim de cada expediente.
Para retomar: leia este arquivo + os [ADRs](adr/) + o [brief de infra](infrastructure-brief.md).
(O "como trabalhamos" — Modo Mentor — vem do prompt inicial; re-cole ao abrir uma sessão nova.)

**Última atualização:** 2026-09-27

## Ferramentas da sessão

- **`gh` CLI autenticado** — o Claude cria PR, mostra o diff e mergeia (sempre com aval do Marcelo).
- **Windows MCP (PowerShell)** — o Claude roda `dotnet build`/`dotnet test` e comandos git direto.
- **Azure MCP** — consulta de recursos, Bicep, best practices e **preços** (útil na fase de infra).

## Onde estamos

**Núcleo (Marco 1) completo e testado, sem Azure:** domínio `GoodsInvoice` (NF-e 55) com a Reforma
(IBS/CBS/IS), envelope fino, 5 portas, esteira `ProcessAsync` (idempotência → busca → validação →
envio → registro), `NfeXmlParser`, `GoodsInvoiceToAvalara`, `GoodsInvoiceValidator`.

**Adapters e infra reais:**
- `XmlGoodsInvoiceSource` — lê XML do Blob via `IBlobReader`.
- `AvalaraComplianceDispatcher` + mock, tradução de status, 204, **token cache** por tenant (semáforo).
- `SqlProcessingStore` — EF Core, idempotência por índice único `(TenantId, NaturalKey)`.
- Composição no `FiscalHub.Host` + `docker-compose` (Azurite + SQL).

**E2E local (Etapa 1) RODANDO:** `POST /ingest` → lê XML do Blob → valida → despacha pro mock → grava
`Submitted` no SQL. Idempotência confirmada ao vivo (2º POST não duplica linha).

**35 testes verdes. 5 ADRs.** `gh` + Windows MCP em uso (Claude roda build/test/git/docker).

## Próximos passos

1. **Etapa 2 — Service Bus:** emulador do Service Bus + trigger (a casca chama `ProcessAsync`) +
   ingresso (drop de XML no Blob → Event Grid → enfileira). Retry/DLQ nativos.
2. Poll worker de status (limite de consulta + status `Unconfirmed` — ver brief).
3. Roteamento por tipo no composition root; Dashboard React.
4. Marco 2: CT-e (57) e NFS-e como tipos novos (prova de extensibilidade).

## Decisões recentes

- Estilo de envio: chamada direta no pipeline (fila de saída fica como evolução) — ADR-0004.
- 204 no `CheckStatus` = ainda pendente (`Submitted`), não erro.
- Poll terá limite (deadline + tentativas) e status `Unconfirmed` para "sem retorno da plataforma"
  (≠ rejeição de negócio) — a implementar.
- Commits em PT (sem acento); código/identificadores em inglês; termos fiscais BR mantidos.

## Threads abertas

- Documento na DLQ não grava `IntegrationError` no store — resolver na fatia de dashboard/DLQ.
- Seção "como construí com agentes" no README (narrativa do diferencial).
- (Opcional) mock simular 204 num primeiro GET, para demonstrar o fluxo assíncrono localmente.

---

## Checklist do primeiro cliente (registro de risco)

A demonstração roda na base do fiscosysdev: 83 notas de 2016, sendo 5 NF-e 55, e contra o mock, que aceita
tudo. Por isso há caminhos que nenhum dado real exercitou. Para a demo isso basta. Para o primeiro cliente,
esta lista é o registro de risco.

Cada item traz três coisas: o que falta provar, como se prova e o sintoma se ninguém provar. Os sintomas
**silenciosos** são os mais caros: a nota chega, mas errada, e nada acusa.

**Regras da lista:**

- **Lugar único.** Tudo o que "a base não exercita" entra aqui, e não mais espalhado em design ou d365/04.
- **Fechamento com evidência:** marcar `[x]` com o link do teste, da gravação ou da execução que provou.
- **Teste com fixture derivada não fecha o item.** Ele prova o código, e não o dado.

As fontes estão entre parênteses:

- **ADR-0025:** montagem do D365;
- **CNV:** change `connector-not-validator`, cujo design tem o detalhe;
- **d365/04:** d365/04 §11;
- **ADR-0028:** limite de tenant (parte 1 da change `connect-avalara-sandbox`);
- **ADR-0027:** credencial por tenant, segredo no cofre e a quarta foto (parte 2 da mesma change).

### Captura (feed de mudanças do D365)

- [ ] **Horizonte estável e diferença de relógio.** (ADR-0025 §9)
  - **Falta:** a supressão de republicação nunca rodou com nota mudando durante o poll. A margem de 10 s
    supõe que a diferença entre o relógio do web server (header `Date`) e o que carimba o
    `SysModifiedDateTime` é de milissegundos.
  - **Prova:** com o poll ligado, criar e aprovar uma nota em dois passos no mesmo segundo, e conferir
    que ela é montada como `Approved`. Medir a diferença gravando um registro e lendo o `Date` da resposta
    contra o carimbo.
  - **Sintoma (silencioso):** a nota autorizada nunca é enviada. O registro fica `Ignored` com "status
    Created", ou nem aparece, e nada acusa erro.
- [ ] **Linha alterada sem tocar o cabeçalho.** (d365/04)
  - **Falta:** saber se o `SysModifiedDateTime` do cabeçalho muda quando só a linha muda. O feed só olha o
    cabeçalho.
  - **Prova:** alterar uma linha fiscal de nota lançada, se o F&O permitir, e ver se o cabeçalho reaparece
    na janela.
  - **Sintoma (silencioso):** a correção feita no ERP nunca é reenviada.

### Montagem do D365

- [ ] **NF-e 55 cancelada.** (ADR-0025)
  - **Falta:** a base não tem nenhuma; as 2 canceladas são modelo 01. O caminho só passou por cabeçalho
    derivado.
  - **Prova:** cancelar uma NF-e 55 autorizada no ambiente e conferir que o registro vai para `Ignored`,
    com o motivo e o `ExternalId` preservado.
  - **Sintoma:** a nota cancelada continua "Confirmada" no dashboard. Mesmo provado, o cancelamento não é
    despachado: a plataforma segue com a nota como emitida até a fatia de cancelamento.
- [ ] **Retenção em NF-e 55.** (ADR-0025, CNV D8)
  - **Falta:** as 12 retenções da base estão em notas 01 e SE.
  - **Prova:** montar e enviar uma NF-e 55 de entrada com IRRF ou INSS retido. O esperado é a retenção fora
    dos blocos de imposto, declarada como omissão ("Enviado sem"). O ISS retido é o único que vai, nos
    campos `*ISSRetido`.
  - **Sintoma (silencioso):** retenção somada como imposto do item, ou omitida sem aviso.
- [ ] **Encargo com imposto em cima.** (ADR-0025, d365/04)
  - **Falta:** nenhum dos 547 impostos da base aponta para encargo, e o caminho só passou por fixture
    derivada.
  - **Prova:** nota com frete ou despesa tributada, com o imposto ligado ao encargo e não à linha.
  - **Sintoma:** a montagem falha ("imposto aponta para o encargo…, fora do documento"), e a nota vai para
    a dead-letter com motivo genérico. O pior caso é o imposto do encargo sair como imposto do item, sem
    aviso.
- [ ] **Encargo de frete ou seguro.** (acrescentado nesta consolidação)
  - **Falta:** o `MiscChargeType` só mostrou `Others` nos 14 encargos da base, e só `Others` está mapeado.
  - **Prova:** nota com frete e com seguro. Gravar os nomes do enum e mapear.
  - **Sintoma:** a montagem falha ("tipo sem mapeamento") e a nota vai para a dead-letter. Frete é comum, e
    no primeiro cliente isso aparece no primeiro dia.
- [ ] **Encargo com `MarkupTrans` preenchido.** (ADR-0025, d365/04)
  - **Falta:** os 14 encargos da base têm `MarkupTrans` nulo. O campo não é lido, então o risco é o encargo
    vindo de pedido chegar com tipo ou vínculo diferente.
  - **Prova:** nota faturada de pedido com encargo.
  - **Sintoma:** falha de montagem por tipo ou por encargo órfão.
- [ ] **Imposto zerado na fiscal com valor na contábil.** (ADR-0025, d365/04)
  - **Falta:** dos 26 casos da base, só o `ImportTax` é completado pela contábil, e os demais (IPI 05,
    PIS/COFINS 98/99, ICMS 90, ICMSDiff) seguem zerados. A hipótese de que "a fiscal zera o que a nota não
    tributa" não tem confirmação fiscal.
  - **Prova:** confirmação do fiscal, e a mesma medição sobre a base do cliente.
  - **Sintoma (silencioso):** imposto com valor zero no payload quando o livro tem valor.
- [ ] **CST do IPI.** (ADR-0025, d365/04)
  - **Falta:** confirmar que o `51`/`55` da fiscal é o correto, contra o `01`/`05` da contábil.
  - **Prova:** confirmação do fiscal.
  - **Sintoma:** a plataforma recusa, ou escritura o CST errado.
- [ ] **Divergência fiscal × contábil em base de cliente.** (ADR-0025)
  - **Falta:** na base, as 547 casam sem divergência fora do padrão. As regras de divergência só passaram
    por fixture derivada.
  - **Prova:** rodar sobre a base do cliente a varredura que a suíte já faz sobre o snapshot gravado.
  - **Sintoma:** notas na dead-letter por "divergem em …", com motivo genérico no dashboard.
- [ ] **IBS/CBS com dado real.** (ADR-0025)
  - **Falta:** nenhuma nota da base tem `CBS`, `IBSState` ou `IBSCity`. O grupo só passou por fixture
    derivada, e o `cClassTrib` segue sem entidade.
  - **Prova:** nota de 2026 com IBS/CBS lançada no ambiente, montada e enviada.
  - **Sintoma:** a montagem falha ("grupo incompleto", CST ou base diferentes), ou o grupo vai sem
    classificação e a plataforma recusa.
- [ ] **Origem da mercadoria sem tradução.** (CNV D12)
  - **Falta:** a base só mostrou dois valores, gravados em 2026-09-26: `National` → `0` e `DirectImport` → `1`
    (d365/04 §3.3). Qualquer outro nome deixa a `TabA` ausente.
  - **Prova:** gravar o `Origin` de notas com as demais origens (adquirida no mercado interno, conteúdo de
    importação, CAMEX…) e cobrir todo valor presente na base do cliente.
  - **Sintoma:** a plataforma recusa pela `TabA` ausente. Se ela ler ausente como `0` (nacional), é
    **silencioso**: mercadoria importada escriturada como nacional.
- [ ] **Data de entrada/saída na nota de saída.** (CNV D3)
  - **Falta:** o `AccountingDate` é "data de entrada" pela d365/04. Na saída, a data de lançamento pode ser
    diferente da data de saída.
  - **Prova:** confirmação do fiscal, e nota de saída lançada em data diferente da emissão.
  - **Sintoma (silencioso):** período de escrituração errado.

### Entrada XML

- [ ] **XML sem o grupo IBSCBS.** (CNV D2)
  - **Falta:** os XMLs reais processados até aqui têm o grupo.
  - **Prova:** mandar pelo `/ingest` uma NF-e real anterior à Reforma.
  - **Sintoma:** a nota vai para a dead-letter por falha de leitura, se a regra regredir.
- [ ] **Exportação: parte sem CNPJ nem CPF.** (acrescentado nesta consolidação)
  - **Falta:** o parser exige CNPJ ou CPF em toda parte, e um destinatário estrangeiro vem com
    `idEstrangeiro`.
  - **Prova:** NF-e de exportação real pelo `/ingest`.
  - **Sintoma:** a nota vai para a dead-letter com "Parte sem CNPJ ou CPF". É julgamento de conteúdo no
    parser, fora da linha do ADR-0026.

### Contrato e plataforma (Avalara)

Os itens desta seção são provados no teste manual contra o sandbox (parte 2 da change `connect-avalara-sandbox`,
tarefas 16 a 18). A resposta de cada envio fica na quarta foto, no zip da nota.

- [ ] **Campo omitido virando 0 na Avalara.** (CNV)
  - **Falta:** `finalidadeNotaFiscal`, `operacao`, `tipoPagamento` e outros códigos vão omitidos. Se a API
    ler omitido como `0`, `0` é um código válido.
  - **Prova:** enviar ao sandbox e ler de volta o documento gravado na plataforma.
  - **Sintoma (silencioso):** nota aceita e escriturada com finalidade, operação ou pagamento errados. É o
    item mais perigoso da lista.
  - **Evidência parcial (2026-09-27):** o sandbox recusou as 5 NF-e 55 exigindo `operacao` e `tipoPagamento`, entre
    outros campos. Para esses dois, omitir falha alto. O `finalidadeNotaFiscal` não apareceu na recusa, e o item
    continua aberto para ele: o experimento do campo omitido (tarefa 17 da change) não rodou, e foi movido para a
    próxima fatia.
- [ ] **Blocos tirados do schema.** (CNV D6)
  - **Falta:** IPI, II, ICMS-ST, ISS retido, a `TabB` e as bases isenta e em "outras" vêm do schema, e não
    de JSON aceito.
  - **Prova:** envio ao sandbox de notas com esses tributos.
  - **Sintoma:** recusa visível, ou pior, os campos ignorados sem erro e o IPI ausente na escrituração.
  - **Evidência (2026-09-27): não respondido.** A recusa das 5 NF-e 55 parou nos campos obrigatórios, e nenhum bloco
    foi citado, o que não prova que sejam aceitos ([relatório do primeiro envio](avalara-sandbox-primeiro-envio.md)).
- [ ] **Totais de imposto.** (CNV D3)
  - **Falta:** saber se `totais.icms`, `pis` e `cofins` são exigidos. Hoje vão omitidos, e o hub não soma.
  - **Prova:** envio ao sandbox.
  - **Sintoma:** toda nota recusada.
  - **Evidência (2026-09-27): não respondido.** A recusa não citou os totais, mas parou nos campos obrigatórios
    ([relatório do primeiro envio](avalara-sandbox-primeiro-envio.md)).
- [ ] **Formato real do erro.** (CNV D10)
  - **Provado (2026-09-27), a recusa síncrona:** ProblemDetails, com o mapa `errors` do caminho do campo para as
    mensagens. A `PlatformMessage` o transforma em texto legível, provado sobre a resposta gravada
    (`SandboxFixtureTests.Platform_message_on_the_real_refusal_is_readable_text`, [relatório do primeiro envio](avalara-sandbox-primeiro-envio.md)).
  - **Falta:** a consulta de status com erro, que não aconteceu (nenhuma nota aceita).
  - **Prova:** gravar uma consulta com erro no sandbox.
  - **Sintoma:** motivo ilegível no dashboard, como JSON cru ou texto demais.
- [ ] **Reenvio: atualiza ou duplica?** (CNV D17)
  - **Falta:** saber o que a plataforma faz com o mesmo `codigoReferenciaIntegracao` enviado de novo, numa
    correção ou num reprocesso.
  - **Prova:** enviar duas vezes ao sandbox.
  - **Sintoma (silencioso):** documento duplicado na plataforma.
  - **Evidência (2026-09-27): não respondido.** Nenhuma nota foi aceita.
- [ ] **CST que não é número.** (CNV D6)
  - **Falta:** dado real. No D365, todo CST medido é numérico, então o risco vem de outro ERP ou do XML.
  - **Prova:** coberto por teste. Fecha com a medição na base do cliente.
  - **Sintoma:** recusa visível ("Contrato do destino: CST…"). Falha alto, o que é bom.
- [ ] **Dois impostos do mesmo bloco no mesmo item.** (CNV D6)
  - **Falta:** dado real. A base não tem o caso.
  - **Prova:** medir na base do cliente (item × tipo, fora os retidos).
  - **Sintoma:** notas recusadas pelo hub ("tributo repetido no item"). É visível, mas é rejeição nossa.
- [ ] **Estabelecimentos do cliente e transferência entre filiais por XML.** (CNV D4, D5)
  - **Falta:** a tabela de `establishments` de um cliente com várias filiais, e o XML de transferência, em
    que as duas partes estão na tabela.
  - **Prova:** configurar todas as filiais antes da virada e mandar um XML de transferência.
  - **Sintoma:** rejeição em massa "sem tradução" na virada, e o XML de transferência rejeitado como
    ambíguo.
- [ ] **Lugares ainda sem tradução.** (CNV D7, D8)
  - **Falta:** diferencial de alíquota, IS, encargo e retenções que não são de ISS vão como omissão.
  - **Prova:** tabela de códigos e lugares com a Avalara.
  - **Sintoma:** escrituração incompleta na plataforma. É visível como "Enviado sem", mas incompleta.

### Credencial e cofre

- [ ] **Provisionamento do cofre de conectores.** (ADR-0027 §6)
  - **Falta:** a identidade do host grava no cofre, e a política do ambiente do cliente precisa limitar essa escrita
    aos segredos de conector. Três partes: um papel sob medida, só com `getSecret`, `setSecret` e `readMetadata`
    (nada de apagar, expurgar, backup ou restore); a condição ABAC `fh-` na atribuição (o `setSecret` pelo atributo da
    requisição, o `getSecret` e o `readMetadata` pelo do recurso); e um cofre dedicado aos segredos de conector, sem
    a chave do JWT, o SQL ou o Service Bus.
  - **Prova:** em staging, antes do primeiro cliente, a identidade do host grava e lê um `fh-…` e recebe
    `ForbiddenByRbac` ao gravar ou ler outro nome (por exemplo, `jwt-signing-key`). Conferir junto que o
    `DescribeAsync` (as versões de um nome conhecido) passa com a condição.
  - **Sintoma:** um host comprometido sobrescreve segredos que não são de conector; ou, com a condição errada, a tela
    de conectores falha ao gravar com `ForbiddenByRbac`.
- [ ] **Recusa lembrada e token com mais de uma instância.** (ADR-0027 §7)
  - **Falta:** o cache de token e a recusa lembrada são por processo. Salvar o perfil esquece só na instância que
    atendeu o `PUT`.
  - **Prova:** com duas réplicas, recusar a credencial, corrigir pela tela e contar os pedidos de token por réplica.
  - **Sintoma:** até 5 minutos de "credencial recusada" nas outras réplicas depois da correção. É visível e se
    desfaz sozinho.

### Limite de tenant

Hoje, todo endpoint age sobre o tenant de quem está logado. Os itens abaixo são pré-condições para abrir uma
entrada a cliente, e não defeitos de hoje: nenhum é alcançável sem essa abertura.

- [ ] **Drop aberto a cliente.** (ADR-0028 §7)
  - **Falta:** o watcher tira o tenant do caminho do arquivo, que é escolhido por quem escreve. Hoje só o `/drop`
    de dev escreve, com o tenant do login.
  - **Prova:** antes de dar escrita no drop a um cliente, a credencial de escrita presa ao tenant (container por
    tenant, ou SAS de diretório com namespace hierárquico), e o tenant tirado dessa ligação.
  - **Sintoma (injeção):** um cliente grava em `drop/{outro tenant}/…`, e a esteira monta, despacha e registra a nota
    no outro tenant.
- [ ] **Fila aberta a cliente (SAS send-only).** (ADR-0028 §7, d365/03)
  - **Falta:** os consumidores confiam no tenant e no locator do corpo da mensagem. Hoje só processos nossos
    publicam.
  - **Prova:** antes de emitir a primeira SAS, fila ou tópico por cliente, com o tenant tirado da entidade, e não
    do corpo.
  - **Sintoma (injeção):** um cliente publica uma referência com o tenant de outro. A regra do locator, na busca,
    segura a leitura de XML alheio, mas não a injeção.
- [ ] **Diretório de empresas por tenant.** (ADR-0028)
  - **Falta:** a porta `ICompanyDirectory` não recebe tenant, e o adapter JSON de dev devolve a mesma lista a todos.
  - **Prova:** o adapter real (ERP ou Avalara) escopado pelo tenant do login, e a porta ganha o tenant.
  - **Sintoma (vazamento):** o dropdown da integração manual mostra empresas de outro cliente.
- [ ] **O `/ingest` deve existir em produção?** (ADR-0028)
  - **Falta:** decisão de escopo. O gatilho real é o drop, o feed e o Event Grid, e o `/ingest` é conveniência
    manual. A correção do locator vale de qualquer forma.
  - **Prova:** decidir antes do deploy do primeiro cliente. Se não existir, mapear a rota só em `Development`.
  - **Sintoma:** uma porta de ingestão manual aberta em produção sem uso previsto.

### Lacunas conhecidas (registradas em 2026-09-27)

- [ ] **O interruptor "Integração em tempo real" é decorativo.** (tela de conectores)
  - **Falta:** o booleano `Realtime` do perfil só desenha o selo "Tempo real ligado" na barra lateral, e não controla
    nada. Quem liga o coletor de verdade é o `poll.enabled` das `InboundSettings`, que não tem tela e só se muda por SQL.
  - **Correção pretendida (próxima fatia):** renomear para "integração automática", e o interruptor passa a ligar e
    desligar o worker do tenant, gravando no `poll.enabled`. O `Realtime` deixa de ser campo gravado, para não haver
    dois lugares dizendo coisas diferentes. Mais adiante, ligar o interruptor abre as opções de configuração do coletor
    (intervalo de busca e afins), hoje sem tela. Rebobinar o `startFrom` continua sendo operação por SQL, de propósito.
  - **Sintoma:** o Admin liga o interruptor e nenhuma nota nova é descoberta; ou o desliga, e o coletor segue rodando.
- [ ] **O seed de dev roda em qualquer ambiente.** (risco de primeiro cliente, e não dívida de estilo)
  - **Falta:** o seed de usuários, tenants e perfis de conector não tem guarda de `IsDevelopment()`; o único gate é a
    tabela vazia, e um banco de produção novo é justamente um banco vazio. O `LocalSeed` também sobe os XMLs de exemplo
    no Blob, em qualquer ambiente. (A demonstração, desde 2026-09-27, é opt-in por `Seed:DemoData`.)
  - **Prova:** subir o host fora de Development contra um banco vazio, e conferir que nenhum usuário, tenant, perfil ou
    blob de exemplo é criado. Precisa de uma guarda antes do primeiro deploy de cliente.
  - **Sintoma:** num banco de produção novo, subir o host cria `admin@fiscalhub.local` com a senha conhecida
    `Fiscal@123`, mais cinco usuários, e perfis de conector apontando para localhost e para o `fiscosysdev`.
- [ ] **O `CompanyCode` mostra o fornecedor numa nota de terceiro.** (metadados do documento)
  - **Falta:** o `CompanyCode` sai dos 8 primeiros dígitos do CNPJ do emitente, e o `BranchCode`, dos 4 seguintes. Numa
    nota emitida por terceiro, isso é o fornecedor, e não o estabelecimento próprio. A origem do D365 traz os dois campos
    certos: `FiscalEstablishmentCNPJCPF` (o CNPJ completo do estabelecimento próprio, lido hoje só para montar a parte) e
    `FiscalEstablishment` (o código do estabelecimento, que não é lido).
  - **Correção pretendida:** o `CompanyCode` passa a ser o CNPJ completo do estabelecimento próprio. Encosta no banco,
    nos filtros do dashboard, nos agendamentos e no contrato do `/ingest`.
  - **Sintoma:** filtros, KPIs e agendamentos por empresa agrupam as notas de entrada pelo fornecedor.

### Operação

- [ ] **Mudança de versão do canônico com base grande.** (CNV D17)
  - **Falta:** o hash de transição, que não está implementado.
  - **Prova:** não se prova: é pré-requisito. Implementar antes da primeira mudança de versão com tenant em
    produção.
  - **Sintoma:** rebobinar a marca ou fazer backfill depois do deploy reenvia cada nota relida. Em cem mil
    notas, isso dá mais de 30 horas de fila e reenvios em massa à plataforma.
- [ ] **Throttling do F&O queimando entregas.** (ADR-0025)
  - **Falta:** carga do tamanho de um cliente. A reentrega do Service Bus é imediata, e sob throttling longo
    uma mensagem esgota as 5 entregas.
  - **Prova:** backfill numa base grande, observando os 429.
  - **Sintoma:** notas na dead-letter com `MaxDeliveryCountExceeded` nos picos.
- [ ] **Vazão do consumidor serial.** (ADR-0025)
  - **Falta:** volume real. São cerca de 43 notas por minuto, e um backfill de 10 mil notas leva umas 3
    horas.
  - **Prova:** medir no volume do cliente, no fechamento do mês.
  - **Sintoma:** fila crescendo e notas atrasadas no fechamento.
- [ ] **Reinício ou troca de réplica.** (ADR-0025 §9)
  - **Falta:** o registro de publicações é em memória.
  - **Prova:** reiniciar o host com nota mudando e contar os GETs.
  - **Sintoma:** por uma janela de sobreposição, o tráfego no F&O volta a cerca de 6 vezes por nota. O hash
    impede o reenvio.

**Funcionalidades que ainda não existem** (entram por fatia, e não por prova):

- a correção do payload a partir das respostas reais do sandbox (a fatia depois da D18; a entrada está na última
  sessão, abaixo);
- despacho de cancelamento;
- nota de serviço;
- entidade de `CClassTribTable_BR`;
- CT-e.

Ver os próximos passos da sessão mais recente abaixo.

---

## Sessão 2026-08/09 — Conector D365 F&O + adoção de OpenSpec (handoff)

**Conector D365 (X++) — estado atual.** Os 3 objetos no model `FiscalHubIntegration`
(pasta `Fiscal/BusinessEvents`) foram **reforçados** e validados:
- `FS_FiscalDocument_BR_Extension` (CoC na tabela) cobre **`update()` E `doUpdate()`** e dispara na
  **transição** de status para **Approved** e **Cancelled** (compara `this.orig().Status`).
- `FS_FiscalDocStatusChangedContract` — payload enriquecido: `Company, FiscalDocumentRecId (RefRecId),
  FiscalDocumentNumber, FiscalDocumentSeries, FiscalDocumentDate (ISO), Status`.
- `FS_FiscalDocStatusChangedBusinessEvent` — `newFromDoc(FiscalDocument_BR)` guarda o buffer;
  `buildContract()` monta o contract.
- Campos reais: status = **`Status`** (enum `FiscalDocumentStatus_BR`; cancelamento simples =
  **`Cancelled`**, há `CancelledBySubstitution` à parte, fora por ora), número = `FiscalDocumentNumber`,
  série = `FiscalDocumentSeries`, empresa = `dataAreaId`.
- **Testado** (table browser, two-step Created→Approved) na **entrada** (nota 000001) e na **saída** —
  evento caiu no endpoint **`test`**. Compila sem problemas. **Deployable package já gerado.**
- Cancelamento: código pronto, **teste em runtime pendente** (deixado pro fluxo completo).

**Ambiente D365 (importante).** `FiscosysDev` é **Unified Developer** (gerenciado pelo **PPAC**, **sem
projeto LCS**). Deploy de dev = **"Deploy Models to Online Environment"**. **Não existe** LCS Asset
Library / runbook aqui; aplicar package de verdade exige um **Sandbox Standard**. Detalhes em
`d365/03-deploy-e-promocao.md`.

**Write-path (razão do reforço).** Requisito: disparar sempre que a nota ficar Approved/Cancelled, não
importa como. `update()`/`doUpdate()` cobrem record-based; set-based costuma degradar pra linha-a-linha
quando `update()` está estendido. Furo residual (SQL cru / skipDataMethods) → **rede de segurança = poll
de reconciliação** (backlog). **Isto ATUALIZA a recomendação antiga do `d365/02`** (que apontava data
event/polling como caminho principal).

**Promoção estilo cliente (ISV).** 1 código → 1 build → 1 package → **N clientes**; variação por cliente
é **configuração** (endpoint do Service Bus, conta Avalara), não código. Build no Azure DevOps gera o
package; release aplica no ambiente do cliente via **service connection** (credencial que o cliente
autoriza). Sandbox costuma ser automatizado; **produção** o cliente aplica/aprova (gate). Ver
`d365/03-deploy-e-promocao.md`.

**OpenSpec adotado (SDD).** `openspec/` (config.yaml com contexto do FiscalHub) + `.claude/commands/opsx/*`
(gitignored, local) + `CLAUDE.md` na raiz. Fluxo: **alinhar spec com a IA → `/opsx:propose` no Claude Code
implementa → revisar**. Adoção **incremental** (não backfillar specs do legado). Existe uma skill de
bootstrap de projeto (`discovery-project-sdd`, salva na conta do usuário) que gera discovery.md +
apresentacao.html (visual p/ time funcional) + config.yaml + CLAUDE.md.

**Auditoria do repo (sem viés).** Base forte (hexagonal, 22 ADRs, conventional commits, PRs, ~89 testes).
Lacunas: **(1) sem CI** (`.github/workflows` vazio — maior gap); (2) testes finos em `Inbound.Xml` (só 4)
e **sem teste no Host**; (3) X++ sem teste automatizado. Maior retorno: **montar CI** (build + test + 0
warnings no PR).

**Pendências.** Poll de reconciliação (backlog); definir promoção real (pipeline Azure DevOps / sandbox);
validar pós-deploy; **montar CI**; teste runtime do Cancelled; **entidades novas em andamento** (Marcelo
criando no VS).

---

## Sessão 2026-09-26 — Montagem do D365 (fatia 2, change `add-d365-document-assembly`)

**Entregue.** A fila `documents-discovered` ganhou consumidor. Cada referência vai a um roteador:

- **NF-e 55:** o source do D365 monta a nota (4 GETs por entidade, com a contábil só para o `ImportTax` zerado) e
  a entrega à mesma esteira do XML;
- **NFS-e e CT-e:** saem como `Ignored`, com o motivo.

Outras mudanças da fatia:

- a origem viaja na referência, com fallback no perfil, e um resolver escolhe o source por documento;
- o Locator carrega o RecId;
- o hash é calculado sobre um JSON canônico, que também é a foto da fonte;
- o poller deixou de republicar o par (documento, carimbo) já publicado e assentado. Isso corta a
  repetição da sobreposição de ~6× para ~1,15×.

Decisões no ADR-0025. As fixtures são respostas gravadas do fiscosysdev (`tools/d365-fixtures`).

**Desfecho real hoje: nenhuma nota do D365 chega à Avalara.** As 5 notas 55 da base são de 2016, sem IBS/CBS,
e são rejeitadas na validação; as 9 `SE` são ignoradas. Mesmo com IBS/CBS, o `cClassTrib` não é resolvível
sem entidade nova.

**Próximos passos (fatias):**
1. **Nota de serviço (NFS-e):** domínio de serviço com CCM, município de prestação, ISS e item da lista de
   serviços. É a fatia seguinte.
2. **Entidade de `CClassTribTable_BR`** no pacote D365, para resolver o `cClassTrib` (RecId → código).
3. **Despacho de cancelamento**, com os status configuráveis por tenant do ADR-0023. Hoje nota cancelada vira
   `Ignored`.
4. **Impostos, retenções e encargos no parser do XML.** Hoje só o D365 preenche os campos novos do domínio.
5. **Renomear o projeto `Ingress.D365Poll`.** Ele agora contém também o source.
6. **Dead-letter imediata com motivo** para erro permanente de montagem. Hoje o motivo gravado é
   `MaxDeliveryCountExceeded`, e a causa fica no log e na foto da fonte.
7. **Concorrência do consumidor com serialização por documento.** Hoje é serial por segurança: ~43 notas/min.
8. **Agrupamento do dashboard em nota de entrada.** O extrator usa o CNPJ do emitente, então a entrada de
   terceiro agrupa pelo fornecedor.
9. **Persistir o registro de publicações do poller**, se reinício ou troca de réplica pesarem.

---

## Sessão 2026-09-27 — Conector, não validador (change `connector-not-validator`)

**Entregue.** O hub deixou de julgar conteúdo fiscal (ADR-0026). Ele rejeita só o que impede a requisição de
existir:

- nota sem item;
- tenant sem a tradução do estabelecimento;
- dado que o contrato não representa.

O resto vai para a plataforma, e a resposta dela aparece no dashboard com o motivo dela. Outras mudanças:

- **Contrato da Avalara reescrito pelos JSONs reais:**
  - os códigos da empresa vêm da tabela `establishments` das `OutboundSettings`, por ambiente, e nunca do ERP;
  - o parceiro passou a ser a contraparte;
  - os clássicos vão no bloco `imposto`, e o array `impostos` fica só para o IBS/CBS, em estadual e municipal;
  - onde os JSONs são silenciosos, vale o schema completo (IPI, II, ICMS-ST, ISS retido).
- **Omissão visível:** o que o documento tem e o contrato não leva fica no registro ("Enviado sem: …") e aparece
  como aviso no dashboard.
- **Recusa da plataforma com o motivo dela:** na hora do envio (400/422) e na consulta de status, sem retentativa.
- **D365 lê mais campos:** data de entrada/saída, valor das mercadorias, unidade, valor contábil, origem e
  endereço das partes. O canônico passou à v2.
- **Mock:** `rejeitar` e motivo no "erro". O ponta a ponta roda contra o mock em memória.

**Desfecho esperado no fiscosysdev:** 5 NF-e 55 enviadas e confirmadas pelo mock, com o `ICMSDiff` e o encargo da
nota de importação como omissão visível, e 9 NFS-e ignoradas.

**Próximos passos (fatias):**

1. **Ligação com o sandbox real da Avalara.** É a próxima fatia, explícita (design D18 da change):
   - credenciais por tenant e por ambiente, pelas referências `kv:` das `OutboundSettings`;
   - token por tenant, com o provedor refeito para o fluxo real e ligado no host;
   - `BaseUrl` do sandbox no perfil de dev;
   - teste manual das 5 NF-e 55 contra o sandbox, e não contra o mock;
   - respostas reais gravadas (aceite, recusa no envio, consulta com erro), que viram teste da extração do motivo;
   - respostas às perguntas que só o envio real responde: formato do erro, campo omitido lido como `0`, blocos do
     schema aceitos, reenvio que atualiza ou duplica.
2. **Tabelas de código com a Avalara:**
   - `finalidadeNotaFiscal`, `operacao`, `tipoPagamento`, `tipoItem`, `origemCredito`, `tipoImposto` da retenção;
   - os lugares do diferencial de alíquota e do IS;
   - o bloco estruturado ou outro lugar, se algum bloco do schema for recusado.
3. **Nota de serviço (NFS-e).**
4. **Hash de transição do canônico** antes da primeira mudança de versão com tenant em produção (ADR-0026 §6).

A entidade de `CClassTribTable_BR` deixou de ser pré-requisito da demonstração (ADR-0026 §7). Os caminhos que a
demonstração não prova estão no checklist do primeiro cliente, acima.

---

## Sessão 2026-09-27 — Limite de tenant (parte 1 da change `connect-avalara-sandbox`)

**Entregue.** Toda requisição autenticada age sobre o tenant de quem está logado, e nunca sobre um tenant vindo da
requisição (ADR-0028). A varredura dos endpoints achou quatro furos, todos fechados:

- **Vazamento:** o `/trace` e o download comparam o tenant da rota com o do usuário. Outro tenant dá o mesmo 404 de
  documento inexistente, e o Blob dele nem é lido.
- **Injeção:** o `/ingest` usa o tenant do login, e o corpo não leva tenant. O `/drop` de dev grava no prefixo do
  tenant do login.
- **Leitura alheia pela esteira:** o locator de XML tem de estar em `nfe/{tenant}/…`, sem `..`. O `traces` nunca é
  origem, nem no próprio tenant. A regra mora no source, e vale na ingestão manual e na busca.
- **Interferência:** o `deactivate` de agendamento filtra pelo tenant.

Junto: o drop não tem mais tenant padrão, e os campos `TenantId` mortos saíram dos corpos da integração manual e dos
agendamentos. Os XMLs do seed e o catálogo da descoberta passaram a `nfe/tenant-a/…`, e o RUNNING §4 mudou junto.

**Por que antes da Avalara.** São correções de segurança que valem independente da plataforma. A change foi
ordenada para esta parte ser mergeada sozinha (design D19), antes da parte da Avalara.

**Próximo passo:** a parte 2 da change. *(Corrigido depois, na mesma data: o portão contra o sandbox, previsto como
grupo 5, saiu da change. A parte 2 começa pela prova do emulador do cofre, e a forma de autenticação da plataforma é
assumida e confirmada só no teste manual. Ver a sessão seguinte.)*

**Pendências registradas no checklist:** o drop e a fila abertos a cliente, o diretório de empresas por tenant e a
pergunta sobre o `/ingest` em produção.

---

## Sessão 2026-09-27 — Credencial por tenant e a quarta foto (parte 2 da change `connect-avalara-sandbox`, concluída)

**Sem portão antes do código.** A change foi revisada no mesmo dia: o `curl` contra o sandbox (as tarefas 5.1 a 5.4) e a
regra que bloqueava a parte 2 até ele fechar saíram. Fica assumido OAuth `client_credentials`, com o segredo no corpo do
pedido, e a premissa é confirmada só no teste manual (tarefa 15.3). Se estiver errada, o retrabalho fica no provider de
token e no mock (ADR-0027 §3). A prova do emulador do cofre (5.5) ficou, e fechou.

**Entregue (grupos 5.5 a 14, na branch `feat/connect-avalara-sandbox-parte-2`, com PR aberto):**

- **Autenticação real por padrão.** Cada envio e cada consulta levam o token da credencial do tenant no ambiente
  ativo. O "sem autenticação" só existe por pedido explícito, e o host não pede.
- **Credencial e URLs pela seção do ambiente,** sem fallback global e com `https` (http só em loopback). O
  `AvalaraOptions` ficou só com a forma da API, e o `Avalara:BaseUrl` saiu do `appsettings`.
- **O segredo pela tela.** O `PUT /connector` recebe o Client Secret como campo de escrita, grava no cofre e persiste
  só `kv:fh-{tenant}--…`. O `GET` devolve as settings sem referência e "configurado em <data>". Em dev, o cofre é o
  Lowkey Vault em memória, pelo mesmo adapter de produção.
- **Falhas de autenticação com motivo:** a recusa da credencial, o 403 e o 401 com token recém-emitido viram rejeição
  com o motivo da plataforma; a recusa fica lembrada por 5 minutos, e salvar o perfil a esquece na hora. O 2xx sem
  identificador não é reenviado.
- **A quarta foto:** a resposta do envio e da consulta, redigida, no `/trace`, no zip e na aba Resposta do dashboard.
- **O mock exige token,** e o ponta a ponta usa o provider real.
- **A sonda do sandbox** (`tools/AvalaraSandboxProbe`): `token`, `send` com variantes e `get`, com a saída redigida.
- **O `clientTokenRef` saiu** do seed e da tela: nenhum fluxo o lia. O leitor o ignora, se ele ainda estiver no banco.
- **Achados corrigidos no caminho:** o `PUT /connector` apagava a configuração de chamados (agora ela fica, quando o
  corpo não a traz), e a tela de conectores reenviava só os campos que mostra, apagando `establishments`, `companies`
  e `poll` (agora ela preserva o resto).

**Conferido no ambiente local** (ADR-0027, fim): o segredo pela API da tela, o banco só com a referência, o envio
autenticado ao mock com a foto da resposta no `/trace`, e a sonda contra o mock, com `out/` redigido e fora do Git.

**Banco de dev existente:** as referências antigas (`kv:avalara-a-…`, `kv:d365-a-secret`) são recusadas pelo adapter
como fora do prefixo do tenant. Basta digitar o Client Secret na tela, ou regravar as settings (RUNNING §3).

**O teste manual contra o sandbox (2026-09-27).** O ponta a ponta rodou contra o sandbox real:

- as 14 referências do `fiscosysdev` foram descobertas;
- as 9 NFS-e viraram "ignorado", sem nenhuma chamada ao F&O;
- as 5 NF-e 55 foram montadas e enviadas à Avalara, que respondeu com recusa de validação.

Verificado: a autenticação `client_credentials` com corpo JSON; o envio em `taxcompliance/v2/fiscal/dfe`, montado pela
`baseUrl` do perfil mais o `Avalara:DocumentsPath` do appsettings; a tradução do estabelecimento pelos `establishments`
do perfil; e o roteamento da NFS-e sem tocar no F&O. Isso fecha a tarefa 15.3. **Segue sem verificar:** o caminho de
consulta de status. Nenhuma nota foi aceita, e sem `id` não houve consulta. Se ele não existir, a nota fica em "enviado"
até virar "sem retorno".

**Próxima fatia: a correção do payload.** Entrada, da recusa do sandbox — os seis campos que a Avalara exigiu e a
montagem não preenche, todos classificados como **nosso: contrato ou mapeamento** ([relatório do primeiro envio](avalara-sandbox-primeiro-envio.md)). As duas
`UnidadeMedida.Descricao` o domínio atual já resolve; os outros quatro pedem enriquecer o domínio ou a leitura da origem,
e o valor esperado de cada um é pergunta à Avalara:

- `operacao`;
- `tipoPagamento`;
- `parceiro.Codigo`;
- `itens[].Item.TipoItem`;
- `itens[].UnidadeMedida.Descricao`;
- `itens[].Item.UnidadeMedida.Descricao`.

Junto, das lacunas conhecidas do checklist: o interruptor de integração automática gravando no `poll.enabled`, e o
`CompanyCode` pelo estabelecimento próprio. O seed de dev sem guarda de ambiente é risco de primeiro cliente, e precisa
fechar antes do primeiro deploy.

Também para a próxima fatia, do teste manual:

- **O motivo cortado em 1000 caracteres.** Numa nota com muitos itens (a `BRMF21-10000026`), o motivo gravado para no
  limite da `PlatformMessage`, e o resto só aparece na foto. Na fatia que vem, que é justamente corrigir esses campos, ver
  a lista inteira na tela importa.
- **O host em dev não escreve arquivo de log.** A saída vai só para a console, e foi o que impediu a conferência dos logs
  (tarefa 16.4); vai impedir de novo. Resolve uma saída para arquivo no Development, ou a instrução no RUNNING de
  redirecionar a saída do `dotnet run`.

**Movido da change para a próxima fatia:**

- as duas conferências que faltam da 15.2: o `PUT` à mão com `clientSecretRef` no corpo dando 400, e o reinício do
  emulador levando a "não configurado";
- a 15.4, a correção pela tela com o segredo errado de propósito;
- a 16.3, os zips e as cinco fotos de cada nota;
- o experimento do campo omitido (17), porque o `finalidadeNotaFiscal` não apareceu na recusa e ele fica mais informativo
  depois que uma nota for aceita;
- do 18.5, o item "Campo omitido virando 0", que depende do experimento.

**Ficou sem marcar na change:** a 16.2, parcial (o motivo das 5 notas conferido no registro; as abas "Resposta" e
"Destino" na tela, não), e a 16.4, não exercitada (o host rodou numa console, sem arquivo de log). A evidência (18.1 a
18.4) está feita: a recusa real como fixture, os testes de reprodução e o [relatório do primeiro envio](avalara-sandbox-primeiro-envio.md).

**Achados do teste, já resolvidos:**

- a resposta de token do sandbox traz um `email` (vazio neste tenant) que a máscara da troca de token não cobria; ele
  passou a ser mascarado, como os outros identificadores da conta;
- a foto da recusa não teve `Content-Type` porque a resposta do endpoint de envio do sandbox não o traz. A foto lê os
  cabeçalhos de conteúdo, e o capturou na resposta de token do mesmo sandbox e nas do mock.
