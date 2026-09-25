# ADR-0023: Descoberta por polling com change tracking como mecanismo principal de captura no D365 F&O

- **Status:** Aceito
- **Data:** 2026-09-18
- **Validado no ambiente:** 2026-09-25 — as 14 entidades publicadas e a cadeia completa de
  descoberta → montagem exercitada contra dado real (nota de mercadoria e de serviço). Ver
  `d365/05-achados-de-metadata-e-ciclo-de-deploy.md`, seção 7.
- **Revisa:** ADR-0022 (mantém o pacote de data entities; muda o mecanismo de captura) e a decisão
  registrada em `d365/02-business-event-status-changed.md` (CoC em `update()`/`doUpdate()` como gatilho
  principal).

## Contexto

O requisito é capturar **todo documento fiscal que entra no ERP do cliente** — mercadoria, serviço,
transferência, qualquer modelo válido — nos estados **Approved** e **Cancelled**.

A sessão de 2026-08/09 adotou como gatilho principal uma **CoC na `FiscalDocument_BR`** cobrindo
`update()` e `doUpdate()`. Uma verificação na documentação oficial da Microsoft mostrou que essa decisão
foi tomada sem evidência suficiente.

### 1. O furo do hook de tabela é real e documentado

- `doUpdate`/`doInsert`/`doDelete` **pulam toda a lógica**, incluindo os database event handlers **e a
  própria chain-of-command** (`onUpdate()`). A doc classifica o uso como má prática, mas ele existe no
  código base.
- `update_recordset` cai para linha-a-linha **quando `update()` está estendido** — o que sustentava a
  hipótese da CoC. Porém a mesma tabela de conversão lista **`skipDataMethods`** como override: quem
  chama isso volta ao set-based puro e pula as data methods.
- A doc de business events é categórica: *"Therefore, don't implement business events at the table
  level."* E explica o motivo — captura em nível de tabela perde contexto de processo, é difícil de
  manter targeted e noiseless, e não dispara quando a gravação vem de fora da lógica X++.

### 2. O teste existente não cobre o caminho que importa

A validação foi feita no table browser, em transição two-step `Created → Approved`, que chama `update()`
**record-based**. O caminho do posting real e do retorno da SEFAZ nunca foi exercitado.

### 3. Não existe um único write-path a interceptar

| Origem | Como o registro aparece | Como muda depois |
|---|---|---|
| Saída NF-e 55 | posting | job export/import NF-e (assíncrono, lote) |
| Saída transferência | ship (doc de saída) + receipt (doc de entrada) | job export/import NF-e |
| Saída serviço NFS-e | posting | **ISV, campo não-padrão** |
| Saída município não-aderente | customização de parceiro | **desconhecido** |
| Entrada fornecedor contribuinte | posting da fatura, **já aprovada** | inquiry tardia da SEFAZ |
| Entrada não-contribuinte | posting | job export/import NF-e |
| Complementar cancelado | — | **novo documento negativo** + flag no original |

Pontos críticos dessa matriz:

- **Entrada nasce aprovada.** Não há transição `Created → Approved`; uma CoC que compara
  `orig().Status != Status` nunca dispara. O evento relevante é o **insert**, não o update.
- **Serviço não tem status de aprovação no padrão.** A doc de NFS-e é explícita: submissão e assinatura
  **estão fora do escopo**; só a parte do DPS é suportada; **não há campo padrão para a chave de acesso
  de 50 caracteres** — deve ser persistido por extensão. Quem transmite e recebe a autorização é um ISV,
  gravando em campo não-padrão que varia por cliente.
- **Municípios fora do padrão nacional** não são gerados pela localização: *"Partners must customize the
  requirements or behavior for unsupported fiscal document models."*
- **Cancelamento tardio é inerente ao processo.** A consulta final da SEFAZ acontece **depois** de
  vencido o prazo de cancelamento do fornecedor. O documento já terá sido despachado.

### 4. Existe um ponto de convergência — mas no dado, não no evento

A doc do fiscal document framework: *"ensures that all of the posted fiscal documents conform to a
specific structure, **regardless of the process that is used to generate the fiscal document**."*

Nenhum gancho de código cobre a matriz acima. A tabela `FiscalDocument_BR` cobre.

## Decisão

**Inverter os papéis: a descoberta por polling sobre a `FiscalDocument_BR` passa a ser o mecanismo
principal e a garantia de completude. O business event fica como otimização opcional de latência,
ativável por cliente.**

### 1. Worker de descoberta (`Ingress.D365Poll`)

Adapter de ingresso, irmão do `Ingress.BlobDrop`, implementando `IDocumentDiscovery`. Roda **no
FiscalHub (Azure)** — o cliente não executa nada. Hospedagem: Worker Service em Container Apps
(`BackgroundService`), com **um ciclo por tenant**, não um ciclo global.

Estado persistido em SQL, por tenant:

| Campo | Para quê |
|---|---|
| `DeltaToken` | continuar de onde parou |
| `LastSuccessfulCycleAt` | fallback por janela de data |
| `ConsecutiveFailures` | backoff e alerta |
| `IntervalSeconds` | config por tenant (default 30s) |
| `Enabled` | desligar um tenant sem deploy |

Três regras inegociáveis:

1. **Falha não avança o token.** O próximo ciclo repete o mesmo delta. É o que torna o poll
   auto-recuperável; a idempotência absorve o reprocesso.
2. **Lock por tenant.** Ciclo mais lento que o intervalo, ou duas instâncias do worker, causam corrida
   pelo token. Lease em SQL ou blob lease.
3. **Fallback por janela.** Token rejeitado (expirado / fora do período de retenção do change tracking)
   → varredura por janela de data e obtenção de token novo.

### 2. Consulta por change tracking

Change tracking habilitado na entidade de cabeçalho, opção **"Enable primary table"** (os campos de
decisão vivem no cabeçalho). A consulta usa o header `odata.track-changes` no endpoint OData e devolve
**apenas o delta**, não uma varredura.

**Não filtrar status no servidor.** Filtro server-side por `Status eq Approved` perderia a transição para
`Cancelled` e qualquer estado que passe a interessar. O delta vem inteiro; o filtro é decisão do hub.

Custo: com intervalo de 30s, são 10 requisições por janela deslizante de 5 minutos, contra o limite de
**6.000 por usuário/aplicação/web server**. Aproximadamente 0,17% do orçamento. Mesmo a 10s (30 por
janela) o custo é irrelevante; o limitador prático é execução sobreposta e o limite baseado em recurso
em ambiente carregado, não a cota de requisições.

### 3. Três passos: descoberta, roteamento, montagem

```
1. Descoberta (poll)     delta → 1 mensagem leve por documento → Service Bus
                         (identidade + campos de decisão; sem payload)

2. Roteamento (barato)   decide com o que está na mensagem, sem chamar o D365:
                         despacha | ignora | já processado
                         GRAVA a decisão e o motivo, sempre

3. Montagem (caro)       só para o que passou:
                         cabeçalho + linhas + impostos + encargos
                         → Envelope → validação → despacho → registro
```

A separação existe porque montar o documento é caro (várias queries: linhas, `TaxTrans`, `TaxTrans_BR`,
`MarkupTrans`). Um documento descartado deve custar uma comparação em memória, não quatro queries.

Gravar a decisão do passo 2 é requisito de auditoria: responde *"por que esta nota não foi para a
plataforma de compliance?"* com regra, data e hora.

### 4. Quatro fontes de descoberta, uma esteira

| Fonte | Intenção | Idempotência |
|---|---|---|
| Poll contínuo (delta) | descobrir | sim — não reenvia o que já foi |
| Agendado (janela diária) | conferir | sim |
| Business event (opcional) | descobrir com baixa latência | sim |
| **Manual** | **reprocessar** | **não — sempre cria nova tentativa** |

Todas produzem a **mesma mensagem** e passam pelo **mesmo componente de roteamento**, com as mesmas
regras por tenant. Não há regra duplicada entre automático e manual.

**Manual não é descoberta, é reprocessamento.** O usuário só dispara carga manual porque corrigiu algo e
quer a nota refeita. Portanto ignora o eixo de idempotência por conteúdo e sempre cria uma tentativa
nova. Continua respeitando o filtro de modelos do tenant, e responde explicitamente quando não fez nada
(*"modelo SE não está no perfil deste tenant"*).

**Manual não encosta no delta token.** É uma leitura paralela com filtro próprio; dois ponteiros
independentes sobre a mesma entity.

O caminho manual acumula três papéis: investigação pontual, reprocessamento e **carga inicial de
onboarding**. O terceiro é o único onde os limites de throttling importam de verdade — exige paginação,
tratamento de 429 honrando `Retry-After` e, de preferência, janela de baixo movimento no ERP.

### 5. Modelo de documento e tentativas no store

O índice único `(TenantId, NaturalKey)` na linha de processamento força 1:1 onde precisa ser 1:N.

```
Documento          NaturalKey = empresa|número|série     ← identidade, 1 registro
  └─ Tentativa 1   status Approved,  hash A, despachado, resultado
  └─ Tentativa 2   status Cancelled, hash B, despachado, resultado
  └─ Tentativa 3   manual,           hash B, redespachado, resultado
```

A `NaturalKey` permanece a **identidade do documento** — alinhada com o que a plataforma de destino
considera identidade. O versionamento é do hub, para trilha de auditoria.

A idempotência **por conteúdo (hash do cru)** já faz a coisa certa: cancelamento tem conteúdo diferente
→ hash diferente → nova tentativa, sem tratamento especial. Só é preciso soltar o 1:1.

### 6. Porta de saída expressa intenção, não operação

A porta não tem `Create` e `Update` separados: expressa *"despachar este documento neste estado"*. O
adapter da Avalara resolve com um endpoint único que faz upsert (duplica apenas quando os campos-chave —
número, série — diferem). Uma plataforma futura que distinga create/update resolve dentro do seu próprio
adapter. O núcleo não muda.

### 7. Escopo do pacote entregue ao cliente

O deployable package passa a conter **apenas metadado de leitura**:

- Data entities de nome fixo (cabeçalho, linhas, parceiros), **read-only**
- Security role de leitura para o usuário de integração

Sem classes X++, sem business event, sem CoC. Risco de regressão em upgrade do F&O cai para perto de
zero, e a conversa com o ALM do cliente deixa de envolver código que roda dentro de transação do ERP.

O business event e a CoC ficam preservados em branch, como **pacote opcional de baixa latência** para
cliente que peça — vendido como diferencial, não carregado como dependência.

## Alternativas consideradas

- **Manter a CoC como gatilho principal (decisão anterior).** Não cobre entrada (nasce aprovada, é
  insert), não cobre serviço (não há status de aprovação no padrão), não cobre gravação por ISV nem
  `skipDataMethods`, e exige X++ na instalação de todo cliente. Descartada como garantia; preservada
  como otimização.
- **Data event (change tracking + catálogo de eventos).** Pega gravação set-based porque olha o banco.
  Mas exige **Power Platform integration habilitado**, **não dispara quando a primary data source da
  entity é uma view**, não pega virtual fields, e tem teto de 5.000 eventos / 5 min e 50.000 / hora por
  ambiente. Adiciona dependência de plataforma sem resolver serviço nem ISV. Descartada.
- **CoC no processo de negócio (recomendação da Microsoft).** Tecnicamente a melhor captura por evento —
  inclusive o Source document framework oferece `SourceDocumentStateInProcess.getBusinessEvent` como
  ponto pronto. Mas continua sendo captura por evento: não cobre o que é gravado por ISV nem o
  cancelamento tardio, e continua exigindo X++ por cliente. Fica anotada como caminho preferido **se** a
  otimização opcional for construída.
- **Polling por janela de data sem change tracking.** Mais simples (dispensa data management e staging),
  mas depende de a `FiscalDocument_BR` ter `ModifiedDateTime` habilitado — a verificar. Permanece como
  **mecanismo de fallback** quando o delta token é rejeitado.
- **Arquitetura dupla desde o início (evento + poll).** É o destino provável, mas começar pelos dois dobra
  a superfície de teste e de implantação sem necessidade. O poll sozinho já atende o requisito.

## Consequências

**Melhora**

- **Cobertura completa.** Qualquer write-path, qualquer modelo, qualquer direção, incluindo gravação por
  ISV e cancelamento tardio.
- **Zero código X++ executável no cliente.** O pacote vira projeção de leitura. Onboarding deixa de
  depender de janela de deploy e de pipeline de ALM do cliente para instalar lógica.
- **Comportamento uniforme** entre saída, entrada, serviço e transferência.
- **Recuperação trivial.** Worker fora por horas: volta, pede o delta, recupera.
- **Trilha de auditoria completa** — todo documento fiscal que apareceu no ERP tem registro, inclusive os
  descartados, com o motivo.
- **Regras de tenant retroativas.** Cliente inclui um modelo novo no perfil → carga manual do período
  traz os documentos. Sem deploy.
- **Model X++ menor**, o que também alivia a lentidão do Visual Studio.

**Piora**

- **Latência** sobe de segundos para o intervalo do poll (30s default, média 15s). Aceitável para
  despacho a plataforma de compliance, cujo prazo se mede em dias; inaceitável seria em pagamento ou
  estoque em tempo real.
- **Um serviço novo para operar** — worker com estado, lock e backoff.
- **Change tracking precisa ser habilitado em runtime** no ambiente do cliente (workspace de Data
  management), o que é um passo a mais no roteiro de implantação.

**Portas que abre**

- O mesmo worker serve qualquer ERP que exponha delta ou consulta por janela — o desenho deixa de ser
  específico de D365.
- O business event opcional vira item de catálogo comercial ("tempo real") em vez de pré-requisito.

## Notas de implementação

### Lado D365 (metadado)

1. **Remover** do model `FiscalHubIntegration`: `FS_FiscalDocStatusChangedBusinessEvent`,
   `FS_FiscalDocStatusChangedContract`, `FS_FiscalDocument_BR_Extension`. Preservar em branch.
2. **Naming — decidido e implantado.** Prefixo `FS` sem underscore: `FSFiscalDocumentBR`,
   `FSFiscalDocumentLineBR`, `FSCustomerBR` etc. Entity set no plural (`/data/FSFiscalDocumentBRs`).
   Lista completa em `d365/README.md`.
3. **Label único por entidade.** Label duplicado faz entidades sumirem após o *Refresh entity list*, e
   não tem conserto em produção.
4. **`IsReadOnly = Yes`** — aplicado nas 14.
5. **Data management / staging — revisto.** Desabilitado nas **14**, sem nenhuma staging table.
   A intenção original era manter staging no cabeçalho por causa do change tracking, mas isso
   colidiu com o `SysModifiedDateTime` (campo de sistema não resolve EDT, e o BP
   `DataEntityPublicFieldEdtMatchOnStagingTableCheck` reprova). As entidades padrão da Microsoft que
   expõem esse campo também vêm sem staging. Detalhe em `d365/05`, seção 4.
   - Atenção: o transporte do UDE ("Deploy Models to Online Environment") é **aditivo** — remover o
     objeto do projeto não o remove de um ambiente já implantado.
6. **Campos de decisão na projeção do cabeçalho** (além da identidade): **modelo do documento**,
   **direção** (entrada/saída) e **tipo/natureza**. Sem eles o passo 2 precisa de query extra.
6-b. **Campos escondidos.** Auditar `AccessModifier = Private` em toda entidade nova — campo privado
   some do OData sem erro de compilação. Ver `d365/05`, seção 1.
7. **Pós-deploy, no ambiente:** habilitar change tracking na entidade de cabeçalho
   (Data management → Data entities → Change tracking → *Enable primary table*) e definir a prioridade de
   throttling da integração.

### Lado FiscalHub

8. Adapter `Ingress.D365Poll` (worker, estado por tenant, lock, fallback por janela).
9. Componente de roteamento único, com perfil de modelos/direção por tenant, gravando as três decisões.
10. Soltar o 1:1 no `SqlProcessingStore`: documento 1:N tentativas.
11. Caminho manual: sem consulta ao hash, respeitando o filtro do tenant, sem tocar no delta token.
12. Relatório do caminho manual com os desfechos explícitos.

## Gatilho por evento: fora do roadmap (2026-09-25)

Este ADR rebaixou o business event a "otimizador de latência opcional". Na prática isso manteve uma
fase 2 no roadmap que ninguém deveria implementar. Fica resolvido aqui: **saiu**.

**Por que o business event por CoC não volta.** Além do furo de cobertura já descrito, ele exige X++
no pacote do cliente. O `FiscalHubIntegration` hoje é metadado puro — 14 entidades, 14 privilégios,
uma role, zero código — e é isso que o torna revisável e suportável em N clientes. Um mecanismo que
não garante nada não justifica esse custo.

**Se push virar necessário, a forma é Data event, não CoC.** É configuração (aba *Data event
catalog*), baseado em **change tracking** — o mesmo mecanismo do poll, logo a mesma cobertura — e
não leva código no pacote.

**Mesmo assim ele não substitui o worker.** A documentação da Microsoft é explícita:

- entrega **assíncrona e sem ordem garantida** entre emissão e endpoint;
- teto por ambiente de **5.000 eventos / 5 min** e **50.000 / hora**, somando todas as entidades, e
  eventos de **update** são os mais caros — que é o nosso caso, já que aprovação é update de `Status`;
- exige **Power Platform integration** habilitado no ambiente do cliente.

**A verificar antes de qualquer plano:** data event não dispara quando a entidade usa *view* como
data source primário. A `FSFiscalDocumentBR` tem tabela na raiz, mas junta seis data sources — não
está confirmado se alteração em `LogisticsPostalAddress` dispararia evento de documento.

**Gatilho para reabrir a discussão:** intervalo de poll precisar passar de **1 minuto**, ou SLA
contratual de tempo quase real. Fora disso, o poll resolve.

Referências: [Data events](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/business-events/data-events)
e [Limitations](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/business-events/data-events#limitations).

## Pendências de verificação no ambiente

- [x] **Nome técnico dos campos de modelo e direção:** `Model` e `Direction` (valores `Incoming` /
      `Outgoing`), ambos expostos na `FSFiscalDocumentBR`.
- [x] **A tabela tem `ModifiedDateTime` habilitado?** Sim — `FiscalDocument_BR` e
      `FiscalDocumentLine_BR` estão com `ModifiedDateTime`, `CreatedDateTime`, `ModifiedBy` e
      `CreatedBy` em `Yes`. O fallback por janela **é viável** e o campo está exposto como
      `SysModifiedDateTime`.
- [x] **Nota cancelada gera linha nova?** Não — é a mesma linha com `Status` alterado (83 cabeçalhos,
      zero vouchers duplicados). Confirma o modelo documento 1:N tentativas do item 10.
- [ ] No posting de nota de **entrada**, o campo `Status` já nasce `Approved`, ou existe estado
      intermediário ligado à validação da chave de acesso? (Table browser, entrada de teste.)
- [ ] Período de **retenção do change tracking** no caminho OData não-BYOD.
- [ ] Como os clientes trazem hoje as notas de entrada — nativo (XML por e-mail) ou ISV? Se ISV, o
      `FiscalDocument_BR` pode ser criado por integração, o que reforça a decisão deste ADR.
