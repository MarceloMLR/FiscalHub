# STATUS — FiscalHub

Documento de handoff entre sessões/máquinas. Atualizado ao fim de cada expediente.
Para retomar: leia este arquivo + os [ADRs](adr/) + o [brief de infra](infrastructure-brief.md).
(O "como trabalhamos" — Modo Mentor — vem do prompt inicial; re-cole ao abrir uma sessão nova.)

**Última atualização:** 2026-09-26

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
- **d365/04:** d365/04 §11.

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

Os itens desta seção são provados na fatia do sandbox real, a próxima.

- [ ] **Campo omitido virando 0 na Avalara.** (CNV)
  - **Falta:** `finalidadeNotaFiscal`, `operacao`, `tipoPagamento` e outros códigos vão omitidos. Se a API
    ler omitido como `0`, `0` é um código válido.
  - **Prova:** enviar ao sandbox e ler de volta o documento gravado na plataforma.
  - **Sintoma (silencioso):** nota aceita e escriturada com finalidade, operação ou pagamento errados. É o
    item mais perigoso da lista.
- [ ] **Blocos tirados do schema.** (CNV D6)
  - **Falta:** IPI, II, ICMS-ST, ISS retido, a `TabB` e as bases isenta e em "outras" vêm do schema, e não
    de JSON aceito.
  - **Prova:** envio ao sandbox de notas com esses tributos.
  - **Sintoma:** recusa visível, ou pior, os campos ignorados sem erro e o IPI ausente na escrituração.
- [ ] **Totais de imposto.** (CNV D3)
  - **Falta:** saber se `totais.icms`, `pis` e `cofins` são exigidos. Hoje vão omitidos, e o hub não soma.
  - **Prova:** envio ao sandbox.
  - **Sintoma:** toda nota recusada.
- [ ] **Formato real do erro.** (CNV D10)
  - **Falta:** a extração do motivo é tolerante, mas foi escrita sem uma resposta real.
  - **Prova:** gravar uma recusa síncrona e uma consulta com erro no sandbox.
  - **Sintoma:** motivo ilegível no dashboard, como JSON cru ou texto demais.
- [ ] **Reenvio: atualiza ou duplica?** (CNV D17)
  - **Falta:** saber o que a plataforma faz com o mesmo `codigoReferenciaIntegracao` enviado de novo, numa
    correção ou num reprocesso.
  - **Prova:** enviar duas vezes ao sandbox.
  - **Sintoma (silencioso):** documento duplicado na plataforma.
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

- ligação com o sandbox real da Avalara;
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
