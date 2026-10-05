# STATUS — FiscalHub

Documento de handoff entre sessões/máquinas. Atualizado ao fim de cada expediente.
Para retomar: leia este arquivo + os [ADRs](adr/) + o [brief de infra](infrastructure-brief.md).
(O "como trabalhamos" — Modo Mentor — vem do prompt inicial; re-cole ao abrir uma sessão nova.)

**Última atualização:** 2026-10-05

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
- [ ] **O `CompanyCode` significa coisas diferentes conforme a origem.** (change `company-root-in-directory`, design D8;
  ADR-0034)
  - **O caso:** a mesma coluna do registro guarda dois conceitos.
    - **No caminho de XML** (`GoodsInvoiceMetadataExtractor.FromIssuer`), a empresa é a raiz do CNPJ do emitente
      (`12345678`), e a filial é a ordem (`0001`).
    - **No D365** (`D365HeaderReference`), a empresa é o CNPJ completo do estabelecimento (`44278225000180`), e a filial é o
      código dele (`Matriz`).
  - **Por que é tolerável hoje:** um tenant usa uma origem ou outra. O ADR-0034 não piora: a empresa do D365 continua
    sendo o CNPJ completo, e a descoberta já compara as duas formas pela raiz (`TaxIdentifiers.IsSameCompany`). Os
    grupos e os filtros dos cards ainda comparam por igualdade.
  - **Falta:** decidir uma chave só para o registro, ou guardar a raiz e o estabelecimento em colunas separadas, antes de
    haver tenant com as duas origens, ou consulta que atravesse as duas.
  - **Prova:** um tenant com notas do D365 e do XML do mesmo estabelecimento, e os cards, o modal e uma consulta por empresa
    conferidos sobre as duas.
  - **Sintoma:** a mesma empresa vira duas linhas nos cards (uma pela raiz, do XML, e uma pelo CNPJ completo, do D365). E um
    filtro por igualdade da empresa acha as notas de uma origem e não as da outra, sem erro.

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
  - **Desde 2026-09-28 (change `establishment-and-readable-dashboard`, D7 e D8):** com o mapa `errors`, o motivo é
    um resumo ("6 campos com erro: …"), sem o `title`. A tela lê a lista inteira da foto, campo a campo, e o corte em
    1000 caracteres não a atinge mais.
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

- [ ] **Mesmo CNPJ em mais de um contribuinte na plataforma.** (resolucao automatica do de/para, 2026-10-02)
  - **Falta:** o codigo do estabelecimento na plataforma passa a ser resolvido casando o CNPJ do ERP com o
    `cnpj` devolvido por `/taxcompliance/v2/contribuinte`. Se o mesmo CNPJ casar com mais de um
    contribuinte, nao ha criterio de desempate e a escolha fica por conta da ordem do retorno.
  - **Por que fica aberto e nao tratado agora:** no sandbox existem empresas `Padrao`, `QA` e `SPL` ao
    lado das reais, o que torna a duplicidade possivel la. Em producao a expectativa e que nao ocorra
    (Marcelo, 2026-10-02); nao foi observada em conta de producao.
  - **Prova:** uma conta com o mesmo CNPJ em duas empresas, e o despacho recusando com motivo em vez de
    escolher sozinho.
  - **Sintoma - o pior tipo, porque nao falha:** a nota e aceita pela plataforma e escriturada no
    contribuinte errado. Nao ha rejeicao, nao ha log, e so aparece na conferencia da apuracao.
  - **Tratativa pretendida:** se o CNPJ casar com mais de um contribuinte, recusar com motivo legivel
    nomeando os candidatos, em vez de escolher. Tela de de/para so se a duplicidade se mostrar comum.

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
- [x] **Diretório de empresas por tenant.** (ADR-0028)
  - **Falta:** a porta `ICompanyDirectory` não recebe tenant, e o adapter JSON de dev devolve a mesma lista a todos.
  - **Prova:** o adapter real (ERP ou Avalara) escopado pelo tenant do login, e a porta ganha o tenant.
  - **Sintoma (vazamento):** o dropdown da integração manual mostra empresas de outro cliente.
  - **Implementado (2026-10-02, change `erp-company-directory-and-card-filters`, ADR-0032); falta a prova manual (grupo
    10 da change).**
    - **A porta:** recebe o tenant de quem está logado, e a implementação é a do adapter de entrada do perfil.
    - **O D365:** lê o cadastro de estabelecimentos do próprio tenant (`FiscalEstablishments`).
    - **O mock:** o `companies.json` só existe em Development, como fallback do tenant cujo ERP não tem diretório. Fora
      de Development, o ERP sem diretório responde "não tem diretório de empresas no hub".
    - **A prova por teste:** `CompanyDirectoryQueryTests`, `JsonCompanyDirectoryTests` e `D365CompanyDirectoryTests`.
  - **Fechado em 2026-10-02, com a prova manual (tarefas 10.2, 10.3 e 10.9 da change):**
    - **a role:** deploy feito pelo Marcelo em 2026-10-02. No `host-erp-directory-3.log` (fora do git), o tenant-a autentica pelo app do próprio perfil
      (linha 145), e a `FiscalEstablishments` responde 200 (linha 16389);
    - **o dropdown:** lista os quatro estabelecimentos da `brmf`, com o `RJ-01`, que tem 0 registros de documento no banco;
    - **o fallback:** em Development, o tenant-b vê o mock, e o tenant-a não o vê.
    - **O que foi só conferência visual do usuário:** o dropdown e o fallback.
- [ ] **O `/ingest` deve existir em produção?** (ADR-0028)
  - **Falta:** decisão de escopo. O gatilho real é o drop, o feed e o Event Grid, e o `/ingest` é conveniência
    manual. A correção do locator vale de qualquer forma.
  - **Prova:** decidir antes do deploy do primeiro cliente. Se não existir, mapear a rota só em `Development`.
  - **Sintoma:** uma porta de ingestão manual aberta em produção sem uso previsto.

### Lacunas conhecidas (registradas em 2026-09-27)

- [x] **O interruptor "Integração em tempo real" é decorativo.** (tela de conectores) — fechado em 2026-09-27, com teste,
  prova manual pelo log e conferência visual do usuário
  - **Falta:** o booleano `Realtime` do perfil só desenha o selo "Tempo real ligado" na barra lateral, e não controla
    nada. Quem liga o coletor de verdade é o `poll.enabled` das `InboundSettings`, que não tem tela e só se muda por SQL.
  - **Correção pretendida (próxima fatia):** renomear para "integração automática", e o interruptor passa a ligar e
    desligar o worker do tenant, gravando no `poll.enabled`. O `Realtime` deixa de ser campo gravado, para não haver
    dois lugares dizendo coisas diferentes. Mais adiante, ligar o interruptor abre as opções de configuração do coletor
    (intervalo de busca e afins), hoje sem tela. Rebobinar o `startFrom` continua sendo operação por SQL, de propósito.
  - **Sintoma:** o Admin liga o interruptor e nenhuma nota nova é descoberta; ou o desliga, e o coletor segue rodando.
  - **Implementado (2026-09-27, change `automatic-integration-switch`, ADR-0029).**
    - **A tela:** o interruptor "Integração automática" grava o `poll.enabled`, só aparece para adapter que varre, e o
      selo da barra lateral vem do `/info`.
    - **O `Realtime`:** saiu do perfil e da tabela (migração `RemoveConnectorProfileRealtime`).
    - **O `/info`:** deriva o `automaticIntegration` do adapter que varre e do `poll.enabled` (`AutomaticIntegrationTests`).
    - **A gravação:** recusa o valor da seção `poll` que ela escreve e que o coletor não leria, sem trancar a tela por
      um valor inválido já gravado (`ConnectorProfileServiceTests`, design D8).
  - **Prova manual pela tela (2026-09-27, `host.log` fora do git).** Um processo só, e sem relógio no log: o tick de 15
    segundos do poller serve de relógio. O detalhe, com as linhas, está nas tarefas 6.x da change.
    - **Ligar: sustentado.** O perfil é gravado (linha 1439). O tick seguinte consulta o tenant (1502), ou seja, em até
      15 segundos. A passada dá 14 referências na fila, com 0 suprimidas (1830). As 5 NF-e 55 chegam ao sandbox da
      Avalara e são recusadas por validação, com HTTP 400 (1985 a 2425).
    - **Desligar pela tela: sustentado.** O perfil é gravado (linhas 14207 a 14222). O tick anterior ainda consulta o
      tenant (14179), e o seguinte já não (14241). Dali até as 23:02, 24 ticks listam o tenant sem consultá-lo, sem
      linha do feed, aviso ou falha. O banco confirma `enabled: false`, com o resto da seção preservado e o cursor de
      pé. O primeiro desligado do log (1832 a 2601) veio de fora da tela, e não conta.
    - **Religar retomando da marca: sustentado.** O perfil é gravado (linhas 15278 a 15293). O tick seguinte consulta o
      tenant (15336) sem recriar o cursor, e a passada dá 0 referências e 0 suprimidas (15426). A leitura do
      `startFrom` devolveu 14 nas duas vezes anteriores, então esta leu só a janela da marca. O banco confirma
      `enabled: true` e a marca avançando de 22:55:24 para 23:12:58.
    - **O selo (ausente, aparecendo e sumindo) e a guarda do `GET /connector` (RUNNING §8):** conferência visual do
      usuário, sem linha de log.
  - **Próximo passo, fora desta change:** ligar o interruptor abre as opções do coletor (intervalo, sobreposição,
    `startFrom`). É por ali que um campo da seção `poll` quebrado por SQL passa a ser corrigível pela tela. Hoje ele não
    tranca a gravação, mas só se corrige por SQL.
- [x] **Rebobinar pelo roteiro não reprocessa nada.** (RUNNING §6) — fechado em 2026-09-27, com teste e prova manual
  - **Falta:** o `ChangeFeedPublicationLog` é um singleton em memória. O rebobinamento do §6 do RUNNING (apagar o cursor
    para o `startFrom` valer de novo) não reprocessa nada enquanto o processo continua de pé: a passada roda e reporta
    tudo como "suprimida(s) por já publicadas". O procedimento está documentado, não funciona e não avisa.
  - **Correção pretendida (próxima fatia, junto com o interruptor):** quando a marca d'água andar para trás, ou quando o
    cursor do tenant não existir, o poller descarta o registro de publicação daquele tenant. Corrige sozinho, sem passo
    manual.
  - **Antes de corrigir:** o `BeginPull` já zera o registro quando a marca fica abaixo da última vista, com teste pela
    variante do `UPDATE` na marca (`Watermark_rewind_republishes_everything_in_the_reread_window`). A variante do
    `DELETE`, que é a do roteiro, não tem teste, e a leitura do código não isolou por que ela escapa. O primeiro passo é
    reproduzir o sintoma num teste por essa variante.
  - **Caminho confirmado por teste (2026-09-27, change `automatic-integration-switch`, design D5).** Os testes rodaram
    contra o código de antes da correção:
    - o `DELETE` entre passadas já funcionava: `Cursor_deleted_between_passes_republishes_from_startFrom` passou;
    - o `DELETE` que cai durante a primeira passada, antes do primeiro avanço da marca, escapa:
      `Cursor_deleted_mid_first_pass_republishes_that_page_from_startFrom` falhou. O avanço não acha a linha e vira
      "lease perdido", e a última marca vista fica igual ao `startFrom`. Na passada seguinte, o `BeginPull` não vê
      regressão, e a primeira página sai suprimida;
    - o cursor recriado sem marca por uma falha escapa pelo mesmo motivo:
      `Cursor_without_watermark_forgets_the_publications`.

    A correção: sob o lease, o cursor ausente ou sem marca faz o poller esquecer o registro do (tenant, origem) antes da
    leitura. Os três testes passam.

    Não está provado que foi esse o caminho do sintoma visto no dev. Ele o explica se o `DELETE` caiu durante a primeira
    passada, que no dev é longa.
  - **Prova manual do roteiro (2026-09-27, `host.log` fora do git, tarefa 6.6 da change).** O cursor foi apagado com o
    host de pé.
    - **O `DELETE`:** foi fora do host e não aparece no log.
    - **O efeito dele:** o host recria a linha (linha 2724), e esse `INSERT` só acontece quando o cursor não existe.
    - **A redescoberta:** a passada seguinte, no mesmo processo, dá 14 referências na fila, com 0 suprimidas (linha
      2944).
    - **A variante exercitada:** foi a do `DELETE` entre passadas, que já funcionava antes da correção. O caminho
      corrigido, o do `DELETE` no meio da passada, só tem prova por teste.
    - **Efeito que confirma o fora de escopo da change:** a redescoberta reenviou as 5 NF-e 55 ao sandbox (linhas 3057
      a 3483, todas com HTTP 400). Rebobinar põe documentos reais de volta na plataforma, e por isso um botão de
      "reprocessar" é decisão de produto.
  - **Sintoma (silencioso):** quem rebobina para repetir o teste vê a passada rodar sem erro e nenhuma nota voltar à fila.
- [x] **O rótulo "Tempo real" da lista de grupos.** (dashboard, `GroupsPage`; design D12 da change
  `automatic-integration-switch`)
  - **Falta:** a palavra é a mesma do antigo interruptor, mas o conceito é outro. O `RealTime` do modo de integração diz
    como o documento entrou: é o gatilho gravado no documento processado (`Trigger`), ao lado de `Manual`,
    `ScheduledDaily` e `ScheduledOnce`, e a referência sem modo cai nele. O interruptor diz se o conector roda sozinho.
    Uma nota pode ter entrado em "tempo real" com a integração automática hoje desligada, e o contrário também vale.
    Renomear mexe em contrato:
    - o valor gravado em `Trigger` nos documentos já processados;
    - o modelo servido pelo `IDocumentQueries`;
    - o rótulo e o valor padrão da `GroupsPage`.
  - **Prova:** a fatia que renomear migra o valor gravado e o contrato juntos, e a lista de grupos continua agrupando e
    filtrando os documentos antigos pelo modo certo.
  - **Sintoma:** o usuário lê "Tempo real" num grupo de notas e conclui que a integração automática está ligada, ou que
    há integração por evento, que nenhum ERP nosso faz hoje.
  - **Fechado em 2026-09-29, com teste e prova manual (change `establishment-and-readable-dashboard`, D13).**
    - **O valor gravado:** passa de `RealTime` a `Automatic`, com a migração de dados `RenameRealTimeTrigger`.
    - **O rótulo:** "Automática".
    - **O modo nos outros desfechos:** também a nota ignorada e a da dead-letter gravam o modo.
    - **A prova por teste:** `SqlProcessingStoreTests.Reference_without_source_mode_is_recorded_as_automatic`, o
      `Group_without_mode_is_served_as_automatic` e o `Ignored_note_of_a_manual_run_keeps_the_manual_mode`.
    - **A prova manual (`host-fatia3.log`, fora do git):**
      - a migração aplicada na subida (linha 68);
      - no banco, as 14 notas da `brmf` com modo `Automatic`, e nenhuma linha com `RealTime` na base;
      - na tela, a coluna "Tipo" com "Automática": conferência visual do usuário, sem linha de log.
- [ ] **O seed de dev roda em qualquer ambiente.** (risco de primeiro cliente, e não dívida de estilo)
  - **Falta:** o seed de usuários, tenants e perfis de conector não tem guarda de `IsDevelopment()`; o único gate é a
    tabela vazia, e um banco de produção novo é justamente um banco vazio. O `LocalSeed` também sobe os XMLs de exemplo
    no Blob, em qualquer ambiente. (A demonstração, desde 2026-09-27, é opt-in por `Seed:DemoData`.)
  - **Prova:** subir o host fora de Development contra um banco vazio, e conferir que nenhum usuário, tenant, perfil ou
    blob de exemplo é criado. Precisa de uma guarda antes do primeiro deploy de cliente.
  - **Sintoma:** num banco de produção novo, subir o host cria `admin@fiscalhub.local` com a senha conhecida
    `Fiscal@123`, mais cinco usuários, e perfis de conector apontando para localhost e para o `fiscosysdev`.
- [x] **O `CompanyCode` mostra o fornecedor numa nota de terceiro.** (metadados do documento)
  - **Falta:** o `CompanyCode` sai dos 8 primeiros dígitos do CNPJ do emitente, e o `BranchCode`, dos 4 seguintes. Numa
    nota emitida por terceiro, isso é o fornecedor, e não o estabelecimento próprio. A origem do D365 traz os dois campos
    certos: `FiscalEstablishmentCNPJCPF` (o CNPJ completo do estabelecimento próprio, lido hoje só para montar a parte) e
    `FiscalEstablishment` (o código do estabelecimento, que não é lido).
  - **Correção pretendida:** o `CompanyCode` passa a ser o CNPJ completo do estabelecimento próprio. Encosta no banco,
    nos filtros do dashboard, nos agendamentos e no contrato do `/ingest`.
  - **Sintoma:** filtros, KPIs e agendamentos por empresa agrupam as notas de entrada pelo fornecedor.
  - **Fechado em 2026-09-29, com teste e prova manual (change `establishment-and-readable-dashboard`, D1 a D5).**
    - **O grupo:** a empresa é o CNPJ de 14 dígitos do estabelecimento próprio, e a filial, o código dele no F&O
      (`Matriz`, `SP-01`, `SAL-01` na `brmf`). O dia é a data fiscal, no fuso de quem emitiu, sem conversão.
    - **A prova por teste:** `GoodsInvoiceMetadataExtractorTests`, `D365GoodsInvoiceAssemblerTests` e
      `D365ChangeFeedTests.Discovery_and_assembly_give_the_same_day_for_the_same_header`.
    - **O que a leitura do código corrigiu no pedido:**
      - o `CompanyCode` já tinha 20 caracteres, e os 14 dígitos couberam sem migração;
      - o `/ingest` não carrega empresa;
      - o que precisou de migração foi o `BranchCode` (`WidenBranchCode`, de 10 para 20).
    - **A prova manual (`host-fatia3.log`, fora do git), depois de uma passada limpa contra o fiscosysdev:**
      - a passada deu 14 referências na fila, com 0 suprimidas (linha 6382);
      - no banco, a empresa é o CNPJ completo, `44278225000180` e `44278225000260`: dois estabelecimentos distintos onde
        antes havia só a raiz `44278225`;
      - a filial vem do 365, `Matriz` e `SP-01`, no lugar de `0001`;
      - as 9 NFS-e ignoradas passaram a ter modelo (`SE`) e data de referência, antes nulos;
      - a data de referência bate com o `FiscalDocumentDate` do F&O nas 14 notas, montadas e ignoradas;
      - na tela, a tabela, a máscara do CNPJ e os cards em 0 (nenhuma nota tem data fiscal de hoje): conferência visual
        do usuário, sem linha de log.

- [ ] **O `startFrom` que some e o cursor que nasce em "agora" sem avisar.** (achado da change
  `establishment-and-readable-dashboard`, 2026-09-28)
  - **O que aconteceu:** o `startFrom` sumiu da seção `poll` do perfil em algum momento da prova, e a causa não foi
    investigada.
    - **O sintoma:** foi indistinguível de "não há nota nova": nenhuma falha, a marca avançando e zero documento. Sem o
      `startFrom`, o cursor criado do zero nasce no instante atual, e o histórico fica de fora em silêncio.
    - **A frequência:** é a terceira vez que um campo invisível da seção `poll` custa investigação.
  - **Direção:** avisar quando o cursor é criado do zero sem `startFrom`. O desenho é o do aviso de poll ausente que já
    existe (`PollNotConfiguredNotices`): avisa na primeira vez e diz o que fazer (`docs/RUNNING.md` §6). O aviso nomeia
    o tenant e o instante em que a marca nasceu.
  - **O `startFrom` segue sem edição pela tela.** (registrado em 2026-09-29, no planejamento da change
    `module-navigation-and-integration-panel`)
    - **A causa:** foi a ausência dele que causou a investigação de 29/09: nenhum erro, a marca avançando e zero
      documento.
    - **O que a change trouxe (implementada em 2026-09-29; falta a prova manual, grupo 10):** o painel da integração
      automática mostra a marca e o `startFrom`, como leitura. Com a marca ausente, o painel diz de onde a primeira
      passada vai começar: do `startFrom`, ou do instante em que ela rodar, com o histórico de fora. O Admin rebobina a
      marca pela tela. O aviso no log, que é a direção deste item, continua por fazer.
    - **O que continua fora:** editar o `startFrom` continua exigindo SQL. Definir o ponto de partida é outra operação,
      que não é rebobinar: o rebobinamento move a marca de um cursor que existe, e o `startFrom` decide onde nasce um
      cursor que ainda não existe.
  - **Um segundo sintoma da mesma família, visto no banco em 2026-09-29, depois da prova:** as `InboundSettings` do
    tenant-a ficaram `{}`, sem URL, empresas, `auth` nem `poll`.
    - **A hipótese:** é o efeito de trocar o ERP na tela de conectores para um adapter que não varre (a tarefa 9.5 da
      change) e voltar para o `Dynamics365`. A troca zera as settings, e voltar não as restaura.
    - **O que não se sabe:** não foi reproduzido.
    - **O que acontece com o perfil assim:** o poller cai no aviso de poll ausente, e não em silêncio. Mas o perfil perde
      tudo o que a tela não mostra.
  - **Prova:** apagar o `startFrom` e o cursor com o host de pé, e ver o aviso na passada seguinte. Trocar o ERP na tela
    e voltar, e conferir as settings no banco.
  - **Sintoma:** quem rebobina ou liga o coletor de um tenant novo vê a passada rodar limpa e nenhuma nota entrar.
- [ ] **O rebobinamento pela tela com mais de uma réplica.** (ADR-0031; change `module-navigation-and-integration-panel`,
  design, Risks)
  - **O caso:** o registro de publicações é em memória, por réplica, e a regra que o zera compara a marca lida com a
    última vista por aquela réplica. Uma réplica cuja última marca vista é anterior ao alvo do rebobinamento não vê a
    regressão.
  - **O efeito:** ela ainda pode suprimir os pares da faixa de sobreposição acima do alvo, e algumas notas dessa faixa não
    voltam à fila. O rebobinamento por SQL tem o mesmo limite.
  - **Hoje:** o host roda uma réplica, e o caso não acontece.
  - **Direção:** uma geração do rebobinamento, persistida no cursor, que cada réplica compara no `BeginPull`.
- [ ] **O freio do teste de credencial é em memória, por réplica.** (ADR-0031, D12)
  - **O efeito:** com N réplicas, o limite vira N testes recusados por 5 minutos, por chave. É o mesmo desenho da recusa
    lembrada da Avalara.
  - **Hoje:** uma réplica. É aceito.
- [ ] **A seção `poll` ilegível esconde o painel e o selo.** (change `module-navigation-and-integration-panel`, design,
  Risks)
  - **O caso:** o painel e o selo seguem o `/info`, que conta uma seção `poll` ilegível como desligada. O coletor,
    enquanto isso, registra a falha no cursor.
  - **Como se chega lá:** só por SQL. A gravação pela tela recusa um valor novo que o coletor não lê.
  - **Direção:** mostrar o painel quando o adapter varre e a seção não se lê, com o erro de leitura.
- [ ] **A restrição de acesso por módulo na API.** (ADR-0031 §5)
  - **Hoje:** os módulos só montam a barra lateral. A API continua respondendo para um módulo escondido, com as regras de
    papel e de tenant de sempre.
  - **Direção:** uma fatia própria, se um cliente precisar de restrição de fato.
- [ ] **As fatias de Contábil e de Inventário.** (ADR-0031 §5; design D1)
  - **Hoje:** os dois são lugares reservados na barra lateral, com um painel vazio. Não são filtros do Fiscal.
  - **O que cada uma precisa:** domínio, portas e adapters próprios.
  - **O que já está decidido:** a carga é manual, pelo Agendamento. A integração automática continua só Fiscal, por
    decisão de produto.
- [x] **Filtros dos cards.** (dashboard, `GroupsPage`; próximo passo da change `establishment-and-readable-dashboard`)
  - **Comportamento correto, e não defeito:** os cards contam as notas cuja data de referência é hoje.
    - **Qual data:** a data fiscal, no fuso de quem emitiu, sem conversão, com o mesmo critério para a nota montada e para
      a ignorada.
    - **Qual "hoje":** o do navegador.
    - **O que fica fora:** as notas de 2016 do fiscosysdev, o que é o correto. Não "corrigir" para a data de
      processamento.
  - **Falta:** os filtros, por período (dia, 7, 15 e 30 dias, com o dia como padrão) e por modelo. A nota ignorada já
    grava data e modelo, e com eles um contador próprio para as ignoradas deixa de ser necessário.
  - **Prova:** com o filtro de 30 dias, as NFS-e de 2026-08-07 da `brmf` entram nos cards de 2026-09-06, e as de 2016
    não.
  - **Sintoma:** hoje, quem quer ver as notas da semana só tem a tabela.
  - **Implementado (2026-10-02, change `erp-company-directory-and-card-filters`, ADR-0032); falta a prova manual (grupo
    10 da change).**
    - **A janela:** o dia, 7, 15 e 30 dias. Os últimos N dias são hoje e os N−1 anteriores, pelo dia do navegador.
    - **O modelo:** todos, ou um dos modelos da janela.
    - **A contagem vai para o servidor** (`GET /groups/totals`), sobre todas as notas da janela. Antes, o navegador somava
      os 200 grupos mais recentes, o que numa janela de 30 dias truncaria.
    - **A prova por teste:** `SqlDocumentQueriesTests`, e o `period.test.ts` e o `cards.test.ts` do dashboard.
    - **A conta da prova acima erra por um dia nesta regra:** em 2026-09-06, a janela de 30 dias vai de 08-08 a 09-06. O
      teste usa 2026-09-05, em que ela começa em 2026-08-07.
    - **A prova manual não fecha com o dado de hoje:** a nota mais recente da `brmf` é de 2026-08-07, a 55 dias de
      2026-10-02, fora de qualquer janela. Ela precisa de uma nota com data fiscal recente, lançada no fiscosysdev.
    - **Revisto na conferência na tela (2026-10-02, D15 da change, ADR-0032 §8):** os cards e a tabela passam a contar
      pelo dia da execução que trouxe a nota, e não pela data fiscal.
      - **O que deixa de valer:** a regra acima, "não corrigir para a data de processamento", por pedido do usuário. A
        integração imediata de hoje, para 2016, aparecia em 02/09/2016. As provas pela data fiscal (10.4 e 10.6 da
        change) também deixam de valer.
      - **O que continua:** a data fiscal continua gravada, e é o critério da descoberta por período.
      - **A tabela:** segue o mesmo filtro dos cards, e mostra o período integrado ("—" na automática).
      - **A prova manual nova:** a do grupo 13 da change. Com o dia da execução, as notas que o coletor trouxe hoje entram
        no filtro do dia.
  - **Fechado em 2026-10-02, com a prova manual pelo dia da execução (tarefas 10.4, 10.6 e 13.9 da change):**
    - os filtros do dia, de 30 dias e do modelo valem para os cards e para a tabela, por conferência visual do usuário;
    - a integração imediata da Matriz (execução 8) ficou na linha `Manual` de 2026-10-02, com o período 2016-09-01 a
      2016-10-02;
    - na fumaça da API, o `/groups` do dia trouxe 4 linhas, e o de 2016-09-02, a data fiscal, nenhuma.
- [x] **O modal do grupo não filtra pelo tipo e pelo modo.** (dashboard, `GroupModal`; risco do design da change
  `establishment-and-readable-dashboard`)
  - **Falta:** a linha da tabela é por empresa, filial, dia, tipo e modo, e a consulta do modal
    (`/groups/{empresa}/{filial}/{dia}/documents`) é só pelos três primeiros.
  - **Prova:** um estabelecimento com uma NF-e e uma NFS-e ignorada no mesmo dia mostra, nas duas linhas, as duas notas.
  - **Sintoma:** o título do modal diz "1 nota", e a lista traz duas.
  - **Implementado (2026-10-02, change `erp-company-directory-and-card-filters`, ADR-0032); falta a prova manual (grupo
    10 da change).**
    - **O grupo:** ganha o modelo, e a tabela ganha a coluna "Modelo".
    - **O modal:** consulta a linha inteira, com o tipo, o modelo e o modo dela. O modo `Automatic` casa também o modo
      nulo do registro antigo.
    - **A prova por teste:** `SqlDocumentQueriesTests` (`Modal_lists_only_the_notes_of_the_row_type_and_model` e
      `Modal_count_matches_the_row_total`).
    - **O período (D15):** a linha ganha o período integrado, e o modal o recebe (`period=none` na automática, ou
      `aaaa-mm-dd_aaaa-mm-dd`). A prova por teste: `Modal_with_the_period_lists_only_that_execution_and_none_lists_the_automatic`.
  - **Fechado em 2026-10-02, com a prova manual (tarefa 10.6 da change):** o modal de cada linha lista só as notas dela,
    com o mesmo número do título, por conferência visual do usuário. Na fumaça da API, o modal das 4 linhas do dia devolveu
    o total de cada uma.
- [x] **O tamanho do código do estabelecimento no F&O.** (change `establishment-and-readable-dashboard`, tarefa 1.1)
  - **Falta:** conferir no AOT o tamanho do EDT do `FiscalEstablishmentId`. O `$metadata` do OData declara a
    propriedade só como `Edm.String`, sem `MaxLength`, e o CDM da Microsoft também não o traz. O `BranchCode` foi
    alargado para 20.
  - **O dado real que temos cabe com folga:** os códigos de estabelecimento da `brmf` no fiscosysdev são `Matriz`,
    `SP-01` e `SAL-01`, com no máximo 6 caracteres. Foram lidos na regravação das fixtures de 2026-09-27 e estão
    anotados em `tools/d365-fixtures/README.md`. A conferência no AOT continua aberta, porque um cliente pode usar
    códigos mais longos que os da base de demonstração.
  - **Prova:** o EDT com tamanho até 20, ou uma migração nova que o acompanhe.
  - **Sintoma:** um código de estabelecimento acima de 20 caracteres faz o `INSERT` falhar, e a nota vai para a
    dead-letter.
  - **Fechado em 2026-10-01:** no AOT, o EDT `FiscalEstablishmentId_BR` tem String Size 10, conferido pelo usuário no
    Visual Studio. O `nvarchar(20)` do `BranchCode` cabe com folga, e nenhum código do F&O passa de 10.
- [x] **O diretório de empresas com o CNPJ de 14 dígitos.** (quando houver descoberta por período com D365)
  - **Falta:** o `ICompanyDirectory` (`companies.json`) e a descoberta local são o caminho de XML de dev, com o
    código de 8 dígitos. Uma integração manual ou agendada do D365 vai precisar do mesmo código do grupo, que é o CNPJ
    de 14 dígitos.
  - **Prova:** a primeira descoberta por período do D365 filtra pela empresa de 14 dígitos.
  - **Sintoma:** o dropdown mostra uma empresa que não casa com nenhum grupo da tabela.
  - **Visto na prova de 2026-10-01:** a tela de Agendamento, e a integração manual com ela, mostra as empresas
    fictícias do `companies.json` (`12345678`, "Empresa Emitente LTDA", e `98765432`, "Distribuidora Beta SA"), e não as
    do 365. A execução descobre pelo catálogo fixo da `LocalDocumentDiscovery`, os XMLs de exemplo do Blob. Só a
    integração automática lê o 365 hoje.
  - **Cuidado enquanto isso:** o tenant-a tem tradução de estabelecimento para o CNPJ de exemplo `12345678000190`.
    Uma integração manual ou agendada para a Empresa Emitente LTDA manda a nota de exemplo ao sandbox da Avalara de
    verdade.
  - **Implementado (2026-10-02, change `erp-company-directory-and-card-filters`, ADR-0032); falta a prova manual (grupo
    10 da change).**
    - **A chave:** a empresa é o CNPJ do estabelecimento sem a pontuação e com as letras (o CNPJ alfanumérico), como
      texto, e não "14 dígitos". A filial é o código.
    - **O diretório do D365:** é o cadastro de estabelecimentos (`FiscalEstablishments`), com o RJ-01, que não tem nota.
    - **A descoberta por período do D365:** lê pelo dia fiscal e pelo estabelecimento, com a mesma referência do coletor,
      e publica na fila de descoberta.
    - **O que isso faz com a ressalva acima:** a "Empresa Emitente LTDA" sai do dropdown do tenant-a, pelo caminho da
      tela. A tradução do `12345678000190` fica no seed, para o `/ingest` e o `/drop`.
    - **A prova por teste:** `D365DocumentDiscoveryTests`, `IntegrationRunnerTests`, `DocumentReprocessTests` e
      `TaxIdentifiersTests`. O CNPJ alfanumérico de ponta a ponta está no `DispatchToMockTests`.
  - **Fechado em 2026-10-02, com a prova manual (tarefas 10.3, 10.5 e 10.8 da change):**
    - **o dropdown:** mostra o cadastro do 365, e a "Empresa Emitente LTDA" saiu do dropdown do tenant-a;
    - **o agendamento:** o único da Matriz (`44278225000180`), de 2016-09-01 a 2016-10-02, achou 1 nota pela descoberta do
      D365 (execução 7), e o banco tem 0 chaves duplicadas. A prova pedia a `SP-01` cobrindo 2026-08-07; o usuário deu a
      da Matriz por suficiente, porque o mecanismo é o mesmo;
    - **o reprocesso:** a `brmf|BRMF06-110000027` tem 1 reprocesso e continua `IntegrationError`, a recusa do sandbox. O
      `host-erp-directory-3.log` (fora do git) tem duas chamadas ao `fiscal/dfe` do sandbox (linhas 16245 e 16689): a da integração imediata e a do
      reprocesso.
    - **Não exercitado no ambiente:** o CNPJ alfanumérico, porque o fiscosysdev não tem estabelecimento alfanumérico. A
      prova é a dos testes (tarefa 10.7, aberta).

- [ ] **O que o chamado de suporte anexa quando quem o abre não pode ver as fotos cruas.** (change
  `establishment-and-readable-dashboard`, D11)
  - **Falta:** decidir. O `/trace` e o zip passaram a exigir Admin, mas o chamado de suporte continua anexando os zips
    das notas no servidor, para qualquer papel. O zip vai para o suporte, e o pedido devolve só o id e o link do chamado.
  - **Prova:** abrir um chamado como Viewer e conferir, no portal de chamados, se o solicitante vê os anexos.
  - **Sintoma:** se o portal mostrar os anexos ao solicitante, o Viewer baixa por lá as fotos que a tela e a API lhe
    negam.
- [ ] **A tela da tradução de estabelecimentos.** (ADR-0032 §7; change `erp-company-directory-and-card-filters`, D12)
  - **Hoje:** o `establishments` das `OutboundSettings` não tem tela. Entra pelo seed e por SQL, e a tela o preserva ao
    salvar.
  - **O que foi decidido:** ele não é semeado pelo diretório. Os códigos são da plataforma e nunca vêm do ERP
    (ADR-0026 §3), e o diretório só daria a chave.
  - **Direção:** uma tela com uma linha por estabelecimento do diretório e os dois códigos digitados pelo Admin.
  - **Sintoma:** com o diretório do ERP, a integração manual da `SAL-01` ou do `RJ-01` com NF-e 55 é rejeitada por "não
    tem tradução para o estabelecimento …". É o desfecho certo, e só se corrige por SQL.
- [ ] **O estabelecimento removido do cadastro não é achado pela descoberta por período.** (ADR-0032; change
  `erp-company-directory-and-card-filters`, design, Risks)
  - **O caso:** a descoberta resolve a empresa e a filial pelo cadastro. As notas de um estabelecimento que saiu do
    cadastro não entram na integração manual nem na agendada.
  - **Hoje:** o coletor continua achando as alterações delas.
  - **Prova:** remover um estabelecimento com nota no fiscosysdev e agendar o período dela.
- [ ] **O agendamento de um tenant sem descoberta retenta a cada passada.** (ADR-0032; change
  `erp-company-directory-and-card-filters`, design, Risks)
  - **O caso:** o agendador captura a falha e não avança o próximo disparo, como em qualquer falha. Um agendamento de um
    tenant cujo ERP não tem descoberta, fora de Development, tenta de novo a cada passada.
  - **Hoje:** a tela não deixa criar um, porque sem diretório não há empresa.
  - **Direção:** registrar a execução com o motivo, e desativar o agendamento.
- [ ] **O CNPJ alfanumérico em minúsculas não casa com a chave em maiúsculas.** (ADR-0032 §5)
  - **O caso:** a normalização preserva a caixa, como o pedido diz. `12abc…` vindo do ERP não acha uma chave `12ABC…` no
    `establishments`.
  - **O efeito:** uma rejeição visível por falta de tradução, e não uma junção silenciosa. As letras do CNPJ são
    maiúsculas pela regra da Receita.
  - **Prova:** quando houver CNPJ alfanumérico em base de cliente, conferir a caixa que o F&O devolve.
- [ ] **O teste de credencial não lê a `FiscalEstablishments`.** (ADR-0032; change `erp-company-directory-and-card-filters`,
  Non-goals)
  - **O caso:** o teste do D365 lê a `FSFiscalDocumentBRs`. O privilégio do cadastro, o `FiscalEstablishmentEntityView`,
    só aparece faltando no dropdown de empresas, com o 403.
  - **Direção:** o teste ler as duas entidades, se a falta do privilégio padrão aparecer em cliente.
- [ ] **A coluna Empresa das tabelas de agendamento e de execução mostra o CNPJ da matriz.** (ADR-0034; change
  `company-root-in-directory`, design, Risks)
  - **O caso:** a empresa gravada é a que o dropdown oferece, o CNPJ da matriz (`44278225000180`). Numa linha cujo escopo
    é outra filial, como a `SP-01`, o CNPJ exibido não é o do estabelecimento daquelas notas (`44.278.225/0002-60`). A
    coluna Filial desambigua, mas quem lê só o CNPJ lê errado.
  - **É regressão de leitura:** antes desta change, a linha mostrava o CNPJ do próprio estabelecimento. Só as linhas que
    não são da matriz são afetadas. A linha com "Todas" mostra a empresa, e está certa.
  - **Falta:** gravar o CNPJ do estabelecimento **só na execução**, numa coluna anulável. Ela é preenchida no momento da
    descoberta, quando o escopo já resolveu um estabelecimento só. A tabela de execuções mostra esse CNPJ quando ele
    existe, e a empresa quando não, sem consultar o diretório: o histórico não pode depender do ERP no ar.
    - **No agendamento, não.** Ele não executou, e a única fonte seria o formulário. Um CNPJ que envelhece é pior que
      nenhum. Execuções registram fato; agendamentos registram critério. A tabela de agendamentos continua mostrando a
      empresa pedida, que é o critério.
    - **Quando:** decidido em 2026-10-05 para a próxima correção pequena, junto com a do fallback do Azure CLI. Não entra
      na `company-root-in-directory`.
  - **Prova:**
    - uma execução da empresa `44278225000180` com a filial `SP-01` mostra `44.278.225/0002-60` na coluna Empresa;
    - uma execução com "Todas", cujo escopo tem mais de um estabelecimento, mostra `44.278.225/0001-80`;
    - uma execução gravada antes da coluna, com ela nula, continua mostrando a empresa;
    - a tabela de agendamentos não muda.
  - **Sintoma (silencioso):** a linha de uma execução da `SP-01` mostra `44.278.225/0001-80`. Quem confere o CNPJ da nota
    contra o da tabela vê dois números diferentes, e nada acusa erro. No dev, a execução 16 (2026-10-05) é um exemplo.

### Operação

- [ ] **Mudança de versão do canônico com base grande.** (CNV D17)
  - **Falta:** o hash de transição, que não está implementado.
  - **Prova:** não se prova: é pré-requisito. Implementar antes da primeira mudança de versão com tenant em
    produção.
  - **Sintoma:** rebobinar a marca ou fazer backfill depois do deploy reenvia cada nota relida. Em cem mil
    notas, isso dá mais de 30 horas de fila e reenvios em massa à plataforma.
  - **A v3 (2026-09-28, change `establishment-and-readable-dashboard`, D3):** subiu com o `FiscalEstablishment` no
    cabeçalho, sem tenant em produção e sem nota aceita no fiscosysdev, então sem efeito. O hash de transição continua
    pré-requisito da primeira subida com cliente.
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
- [ ] **Reinício ou troca de réplica.** (ADR-0025 §9; fatia de nuvem)
  - **Falta:** o registro de publicações é em memória e vale por processo. O lease por tenant impede duas réplicas de
    varrer o mesmo tenant ao mesmo tempo. Mas, quando o lease muda de mão, a réplica nova não sabe o que a anterior
    publicou e republica a janela de sobreposição inteira. O mesmo acontece a cada reinício.
  - **Direção:** registro persistido, podado pelo horizonte estável.
  - **Prova:** reiniciar o host com nota mudando e contar os GETs. Com duas réplicas, forçar a troca do lease e contar
    de novo.
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

Junto, das lacunas conhecidas do checklist: o interruptor de integração automática gravando no `poll.enabled`, com o
rebobinamento que se corrige sozinho, e o `CompanyCode` pelo estabelecimento próprio. O seed de dev sem guarda de ambiente é risco de primeiro cliente, e precisa
fechar antes do primeiro deploy.

Também para a próxima fatia, do teste manual:

- **O motivo cortado em 1000 caracteres.** Numa nota com muitos itens (a `BRMF21-10000026`), o motivo gravado para no
  limite da `PlatformMessage`, e o resto só aparece na foto. Na fatia que vem, que é justamente corrigir esses campos, ver
  a lista inteira na tela importa.
- **O host em dev não escreve arquivo de log.** A saída vai só para a console, e foi o que impediu a conferência dos logs
  (tarefa 16.4); vai impedir de novo. Resolve uma saída para arquivo no Development, ou a instrução no RUNNING de
  redirecionar a saída do `dotnet run`.
  - **Contorno de 2026-09-27 (prova manual da `automatic-integration-switch`):** redirecionar a saída do `dotnet run`
    com `Tee-Object`, por exemplo `dotnet run --project src/FiscalHub.Host 2>&1 | Tee-Object -FilePath host.log`.
    Resolve na mão, mas a conferência de log já ficou sem exercitar duas vezes por falta de arquivo. O item continua
    aberto.

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

---

## Sessão 2026-09-28/29 — Estabelecimento próprio e a tela legível (change `establishment-and-readable-dashboard`)

**Entregue.** A tela mostra o dado certo, na língua de quem usa (ADR-0030):

- **O grupo:** a empresa e a filial vêm do estabelecimento próprio no 365 (o CNPJ completo e o código). O dia é a data
  fiscal, no fuso de quem emitiu, sem conversão, na nota montada e na ignorada. O canônico sobe para a v3.
- **A ignorada:** a descoberta leva o grupo, e a nota ignorada entra na tabela e nos cards da data fiscal dela.
- **O motivo:** vira resumo, sem o `title` do ProblemDetails e sem a omissão. A lista inteira vem da leitura do desfecho,
  tirada das fotos no servidor.
- **A omissão:** é ressalva de nota aceita ("Enviado com ressalvas"), gravada na foto do envio.
- **A foto:** perde o ruído do ProblemDetails e guarda a URL e o `traceId`.
- **As fotos cruas:** só para Admin, de fato. O `/trace` e o zip dão 403 aos demais, e o JSON abre num modal próprio.
- **A tela:** o selo de duas cores, a exclusão de agendamento, e "Automática" no lugar de "Tempo real".

**Prova manual (2026-09-29).** O detalhe, com as linhas do `host-fatia3.log`, está nas tarefas 9.x da change.

- **Sustentado:**
  - a passada limpa, com 14 referências;
  - o grupo no banco, com as 14 datas iguais ao `FiscalDocumentDate`;
  - o 403 do Viewer no `/trace` e no zip, e o 200 da leitura, com 6 campos;
  - a foto sem o ruído, com as omissões;
  - a criação e a exclusão de um agendamento.
- **Conferência visual do usuário, sem linha de log:** a tela inteira.
- **Não exercitado:** o histórico de execução de um agendamento excluído, porque nenhum agendamento disparou. Só o teste
  o prova (tarefa 9.6, parcial).

**Ficou aberto na change:** a 1.1 (o tamanho do código do estabelecimento no AOT) e a 9.6. As duas fecharam em
2026-10-01: o EDT tem String Size 10, e a execução de um agendamento excluído ficou no histórico (a sessão de 2026-10-01).

**Achados:**

- **O `startFrom` que some:** o cursor nasce em "agora" sem avisar (item próprio acima).
- **As `InboundSettings` do tenant-a em `{}`:** vistas depois da prova, no mesmo item.
- **O SQL fora do ar:** no fim do `host-fatia3.log`, consultas ficaram cerca de 28 minutos sem resposta. É o caso das
  bases órfãs do emulador do Service Bus, que o `scripts/up.ps1` passou a limpar na subida (`docs/RUNNING.md` §1).
- **Os testes do D365 num caminho longo:** o projeto de testes do D365 falha num worktree de caminho longo. O
  `D365Fixtures` monta os caminhos com `/`, e o Windows os recusa com o prefixo `\\?\`. Não afeta o repositório no
  caminho de hoje, mas pode afetar um CI que clone fundo.

**Próxima fatia:** os seis campos obrigatórios da recusa da Avalara:

- `operacao`;
- `tipoPagamento`;
- `parceiro.Codigo`;
- `itens[].Item.TipoItem`;
- as duas `UnidadeMedida.Descricao`.

O critério de saída é uma nota aceita.

---

## Sessão 2026-09-29 a 10-01 — Navegação por módulo e o painel da integração automática (change `module-navigation-and-integration-panel`)

**Entregue (ADR-0031).**

- **Os módulos:** o bloco Integrações da barra lateral tem Fiscal, Contábil, Inventário e Agendamento, montado pelos
  módulos do tenant, que ficam gravados no perfil. É apresentação, e não permissão. Contábil e Inventário são lugares
  reservados.
- **O segredo:** aparece mascarado, com 32 bolinhas na cor do texto. A máscara é placeholder, e nunca valor, e o servidor
  recusa um valor feito só de máscara.
- **A situação da integração automática:** última busca, falhas consecutivas, último erro e "sincronizado até", e o
  ponto de partida quando ainda não houve busca. Aparece assim que o interruptor é ligado.
- **Buscar novamente desde uma data:** é o rebobinamento pela tela, sob o lease do coletor, pela mesma regra que zera o
  registro de publicações.
- **O teste de credencial:** cobre o D365 e a Avalara.
  - **A credencial e o token:** usa a gravada, com um token novo a cada teste.
  - **A tela:** mostra "Credenciais e conexão válidas" ou "Credenciais ou ambiente inválidos". O detalhe vai para o log
    do host.
  - **O freio:** fica no botão, por 5 minutos depois de uma recusa, e salvar o desfaz.
- **A URL do token:** saiu da tela da Avalara.

**Prova manual.** Não houve log em arquivo, porque o host rodou sem o `Tee-Object`. A evidência é do banco, da API e da
conferência visual do usuário.

- **Visto no banco e na API:**
  - a migração `AddConnectorProfileModules` aplicada;
  - os módulos gravados pela tela;
  - o `/info` com os módulos, para o Admin e para o Viewer;
  - o Viewer com 403 no painel, no teste e no rebobinamento, e 200 no `/groups`;
  - o Client Secret do Sandbox intacto depois de um salvar sem digitar;
  - as falhas do coletor zeradas, com o Tenant corrigido;
  - buscar novamente desde 2015 trouxe de volta as 14 notas da `brmf`, com as 5 NF-e reenviadas ao sandbox (a recusa
    conhecida) e as 9 NFS-e ignoradas.
- **Conferência visual do usuário, sem linha de log:**
  - os testes de credencial do D365 e da Avalara, certos e errados;
  - a máscara;
  - a aparência do painel.
- **O painel sem marca (10.2, passo 5):** o cursor apagado foi recriado pela busca seguinte, com a marca nascendo no
  instante dela, sem `startFrom`. Nada fica aberto na change.
- **Não distinguível pela tela:** o "0 suprimidas" depois do rebobinamento. O host tinha reiniciado, e o registro de
  publicações já estava vazio. A regra está provada pelo teste 5.5.

**Achados:**

- **As `InboundSettings` do tenant-a em `{}`, de novo:** o mesmo achado de 29/09. Sem URL nem `poll`, a tela não tinha
  painel nem teste do D365. Foi refeito pela tela.
- **O Tenant do Entra ID com um caractere a mais** (`…5cfa30778d93a`, 13 caracteres no último bloco, contra os 12 de um
  GUID): com o segredo certo, o teste falhava. O coletor falhou 25 vezes seguidas, com o erro do Entra ID vazio, sem
  código AADSTS. Daí veio a correção da classificação: só uma causa de rede é indisponibilidade (`NetworkFailure`).
  - **Direção possível:** recusar no salvar um tenant com cara de GUID e tamanho errado. O tenant também pode ser um
    domínio, então não dá para exigir GUID sempre.
- **O host não reiniciado:** o `dotnet run` de 10:31 seguiu de pé, e a tela mostrava o texto antigo. Antes de concluir
  que um ajuste não funcionou, conferir a hora em que o host subiu.
- **O `vitest` 3.2.7:** trouxe um alerta moderado (GHSA-82fw-gwwq-j7x9) no redirect de mocks, que a suíte não usa. A
  correção é o vitest 5, que pede um vite mais novo. É só dependência de desenvolvimento.

**Próxima fatia:** continua a dos seis campos obrigatórios da recusa da Avalara, com uma nota aceita como critério de
saída.

## Sessão 2026-10-01/02 — O diretório do ERP e os filtros dos cards (change `erp-company-directory-and-card-filters`)

**Entregue (ADR-0032). A prova manual está pendente** (grupo 10 da change).

- **O diretório:** a integração manual e o agendamento oferecem os estabelecimentos do cadastro do D365 do tenant
  (`FiscalEstablishments`, a entidade padrão da Microsoft). A empresa é o CNPJ e a filial é o código, a mesma chave dos
  grupos. O mock só existe em Development, para o tenant cujo ERP não tem diretório.
- **A descoberta por período do D365:** pelo dia fiscal e pelo estabelecimento, com a mesma referência do coletor. Ela, a
  execução manual, a agendada e o reprocesso publicam na fila de descoberta. A nota do D365 passa a ser reprocessável.
- **O CNPJ:** sem a pontuação e com as letras, numa função do Domain, em todo lugar que o lê. A máscara da tela é pelo
  tamanho.
- **Os cards:** período (dia, 7, 15 e 30 dias) e modelo, contados no servidor. O grupo ganha o modelo, e o modal lista a
  linha inteira.
- **A role:** a `FSFiscalHubIntegration` referencia o privilégio padrão `FiscalEstablishmentEntityView`, que precisa de
  build e deploy, sem sync.

**Verificado contra o fiscosysdev em 2026-10-01, com a sessão do Azure CLI:**

- **O cadastro:** a `FiscalEstablishments` da `brmf` tem quatro estabelecimentos: `Matriz`, `SP-01`, `SAL-01` e `RJ-01`.
- **O filtro por dia fiscal:** com 2026-08-07 e `SP-01`, a consulta deu HTTP 200, com as duas NFS-e daquele dia
  (`BRMF06-110000034` e `BRMF06-110000035`).
- **As fixtures:** a pasta `directory/` foi gravada pelo `Record-D365Fixtures.ps1 -DirectoryOnly`.

**Achados:**

- **Duas provas da fatia não fecham com o dado de hoje:** a do "30 dias" e a do "modelo bate com o modal". A nota mais
  recente da `brmf` é de 2026-08-07, a 55 dias, fora de qualquer janela.
- **O host da prova anterior seguia de pé** (desde 2026-10-01 11:52) e travava o `bin` do host. O `dotnet build` da
  solução falhava ao copiar as DLLs. A verificação foi o host compilado em outra saída, mais cada projeto de teste com o
  próprio `dotnet test`.
