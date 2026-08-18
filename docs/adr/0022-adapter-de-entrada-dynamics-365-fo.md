# ADR-0022: Adapter de entrada Dynamics 365 F&O (Business Events → Service Bus → OData, com pacote de data entities padronizadas)

- **Status:** Proposto (em refino)
- **Data:** 2026-08-03

## Contexto

Precisamos integrar as notas fiscais que nascem no **Dynamics 365 Finance & Operations (F&O)**. F&O é
um mundo diferente do D365 CE/Dataverse: a integração se faz por **Business Events** (eventos de
negócio que disparam para um endpoint) e **data entities** expostas via **OData**, e não por plugins +
managed solution como no Dataverse.

Dois requisitos de captura: **tempo real por status** (quando a nota atinge um status específico,
integrar) e **por período/nota** (backfill, conferência, reprocesso).

O problema central que motiva este ADR é de **naming/exposição**. O dado fiscal vem sempre da
localização BR padrão (`fiscaldocument_br`), mas essa tabela **normalmente não está exposta** como
data entity pública. Cada implementador acaba criando **uma entidade pública própria** sobre ela, com
nome variável por cliente (`fiscaldocument_br2`, ou qualquer nome customizado). Resultado: o adapter
teria que ser customizado por cliente só por causa do nome/shape da entidade — insustentável.

Do nosso lado, já temos a infraestrutura que serve a este fluxo: consumidor de **Service Bus** com
**dead-letter**, gatilho `Event`, **idempotência por conteúdo** (ADR-0016), a esteira, e o **perfil de
conector por tenant** (ADR-0019).

## Decisão

**1. Contrato publicado — o pacote das data entities de nome fixo.** O FiscalHub distribui um
**deployable package (de metadado)** que o cliente instala uma vez, contendo:

- **Data entities de nome fixo** (ex.: `FS_FISCALDOCUMENT_BR` e as relacionadas), **projeções** sobre
  as tabelas padrão (`fiscaldocument_br`…). Como data entity é view/projeção, **não há cópia nem
  sincronização** — lê ao vivo. A projeção expõe exatamente os campos que o conector precisa pra montar
  o JSON de saída (id, status, campos fiscais).
- Um **security role** concedendo leitura nessas entidades ao usuário de integração (app registration).
- Um **business event** que dispara no status alvo, referenciando a tabela/entidade padrão.

Assim o adapter sempre lê **o mesmo nome**, independente de quem implementou o cliente — a variação de
naming deixa de existir. E padroniza também o **gatilho**, não só a leitura.

**2. Adapter `Dynamics365FO` — config-driven, atrás das portas existentes.** Implementa
`IInboundSource`/`IDocumentDiscovery` (igual iScala/Xml). O **nome da entidade OData + o mapa de campos
vêm do perfil de conector** (ADR-0019), com **default `FS_FISCALDOCUMENT_BR`**. Mesmo código de adapter
serve os dois modos de instalação (ver alternativa do fallback).

**3. Captura em tempo real.** O Business Event dispara no status → **endpoint = fila do Azure Service
Bus** (tipo de endpoint nativo do F&O). O payload leva o **ID/chave** da nota. Nosso **consumidor de
Service Bus (já existente)** recebe, enfileira um `DocumentReference` com `Trigger = Event`, e a esteira
chama o adapter, que **busca a nota completa por ID via OData** e monta o domínio.

**4. Captura por período/nota.** O adapter descobre via **OData `$filter`** (data/status/número) — é o
caminho de descoberta `Manual`/`ScheduledDaily` que já existe, reaproveitado para backfill e reprocesso.

**5. Credenciais por referência.** OAuth2/Entra (app registration) para o OData e a connection string do
Service Bus entram como referência `kv:` no perfil, resolvidas no Key Vault — como já fazemos com
Avalara/iScala. Nunca em claro.

**6. Deploy do pacote no cliente.** No modelo novo, via **PPAC (Unified Developer Experience)** ou
**Azure DevOps** (a LCS está sendo aposentada, com PPAC mandatório para novas implementações de F&O). É
metadado, sem X++ procedural, então o pacote é leve e a instalação é única, versionada pelo FiscalHub.

## Consequências

- **Adapter agnóstico de cliente no código.** A variação vive em **config (perfil)** + **pacote
  opcional** — nunca em código específico por cliente.
- **Padroniza leitura e gatilho.** Nome fixo de entidade e de business event elimina a dependência do
  naming do implementador.
- **Reaproveita 100% da infra.** Service Bus + DLQ + gatilho `Event` + idempotência + esteira não mudam;
  só pluga a fonte nova.
- **Dois modos no mesmo adapter.** Evento por ID (tempo real) e filtro por período (backfill), espelhando
  `Event` vs `Manual/Scheduled`.
- **Custo:** instalação única do pacote por cliente (PPAC/Azure DevOps) + um security role; versionar o
  pacote quando a entidade mudar. Aceitável — troca "código de adapter por cliente para sempre" por
  "install único versionado".
- **Premissa:** o nome da **fonte padrão** (`fiscaldocument_br`) é estável entre clientes (localização BR
  padrão). O que variava era só a entidade pública deles; a base é a mesma.

## Alternativas consideradas

- **Só config, sem pacote (ler a entidade do cliente direto).** Perfil aponta para a entidade que o
  cliente já expõe (`_br2`) + mapa de campos. Mais leve (zero deploy), mas acoplado ao naming/exposição
  deles, e o **business event fica variável** (na tabela deles). Fica como **modo fallback**, para
  clientes que não deixam deployar — não como padrão.
- **Plugins + managed solution (Dataverse/CE).** Não se aplica a F&O; é o caminho de um adapter separado
  para clientes CE/Dataverse.
- **Dual-write / virtual entities (F&O ↔ Dataverse) para usar o eventing do Dataverse.** Mais setup e
  latência, e traz o Dataverse pra dentro sem necessidade. Descartado agora.
- **Webhook HTTP direto** em vez de Service Bus. Sem buffer/retry/dead-letter e acopla o F&O à
  disponibilidade do nosso endpoint. Service Bus vence — é endpoint nativo do F&O e **já consumimos**.
- **Recurring integrations / DMF (batch de arquivos).** Pesado, orientado a volume; não serve ao gatilho
  de tempo real por status.

## Notas de implementação (próximos passos)

1. **Fatia 1 (nosso código):** adapter `FiscalHub.Adapters.Inbound.Dynamics365FO` config-driven (fetch
   por ID + por período via OData), com um **mock local** para testar sem F&O, plugado no gatilho
   `Event`/Service Bus que já existe.
2. **Fatia 2 (lado F&O):** pasta `/d365/` no repo com o **pacote** (data entities de nome fixo + security
   role + business event) e um README de build/deploy (PPAC/Azure DevOps) + o passo do **app
   registration** no Entra.
3. **Validação:** testar no ambiente F&O real (UDE/PPAC) — evento de status → Service Bus → adapter busca
   por ID → domínio → Avalara; e o backfill por período.
