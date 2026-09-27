# ADR-0028: Limite de tenant — o tenant vem de quem está logado, nunca da requisição

- **Status:** Aceito
- **Data:** 2026-09-27
- **Revisa:**
  - **ADR-0018:** fecha a ressalva dos endpoints de debug (`/trace`, `/drop`, download), que "não foram endurecidos com
    o claim". Estende a regra do escopo por tenant a todo endpoint e à ingestão.
  - **ADR-0009:** o drop deixa de ter tenant padrão. O tenant tirado do caminho do arquivo só é confiável enquanto só
    processos nossos escrevem no drop.
- **Change OpenSpec:** `openspec/changes/connect-avalara-sandbox` (parte 1, grupos 1 a 4), capacidade `tenant-boundary`.
- **Validado no ambiente:** 2026-09-27, com o host e o mock locais, logado como tenant-a e como tenant-b (ver o fim do
  documento).

## Contexto

O ADR-0018 pôs o tenant no claim do token e fez as consultas de leitura filtrarem por ele. A integração manual e a
agendada já usavam o tenant do usuário logado, e não o do corpo. O próprio ADR anotou uma ressalva: o `/trace`, o
`/drop` e o download recebiam o tenant na rota e "não foram endurecidos com o claim — a UI só os alcança via dados
já escopados". O argumento não protege nada, porque quem ataca não usa a UI.

Uma varredura de todos os endpoints que recebem tenant pelo corpo, pela rota ou pela query achou quatro furos:

| Furo | Onde | Classe |
|---|---|---|
| O `/trace` e o download leem o Blob pelo tenant da rota | `GET /trace/{tenantId}/{key}`, `GET /documents/{tenantId}/{key}/download` | vazamento |
| O `/ingest` aceita o `TenantId` do corpo, e o `/drop` de dev grava fixo no tenant-a | `POST /ingest`, `POST /drop/{key}` | injeção |
| O locator do `/ingest` é um caminho de blob livre, em qualquer container, inclusive `traces/` | `POST /ingest` | leitura alheia lavada pela esteira |
| O `deactivate` busca o agendamento só pelo id | `POST /schedules/{id}/deactivate` | interferência |

## Decisão

**Toda requisição autenticada age sobre o tenant de quem está logado, e nunca sobre um tenant vindo da requisição.**
Um fluxo legítimo que precise agir sobre outro tenant (integração interna, serviço a serviço) ganha um papel
explícito, com autorização própria, e nunca um campo aberto no corpo. A varredura não achou nenhum: os gatilhos
internos (drop, feed do D365, agendador, poll) não passam pelo HTTP, e vão direto à fila ou ao store, como sistema.

### 1. Vazamento, injeção, leitura lavada e interferência

As quatro classes justificam o recorte, e têm pesos diferentes:

- **Vazamento** (o download e o `/trace`). É confidencialidade: um usuário lê a fonte crua, o domínio e o payload de
  outro cliente. O dado sai, e nada muda do lado de lá.
- **Injeção** (o `/ingest` com o tenant no corpo, e o `/drop` fixo no tenant-a). É integridade, com efeito fora do
  hub. Um documento fiscal é gravado em nome de outro cliente, e a esteira faz o resto sozinha: monta, despacha para a
  plataforma de compliance **daquele** cliente, com a credencial **dele**, e registra no dashboard **dele**. O dano
  chega à escrituração do outro cliente, fora do nosso sistema, e nenhum reprocesso nosso desfaz. É mais grave que o
  vazamento.
- **Leitura alheia lavada pela esteira** (o locator livre). O locator aponta para `traces/{outro}/…` ou `nfe/{outro}/…`,
  e o hub monta aquele conteúdo sob o tenant de quem pediu. É vazamento feito pela própria esteira: o documento
  alheio vira foto, domínio e payload do tenant errado. Fixar o tenant pelo login tira a injeção, mas não isto.
- **Interferência** (o `deactivate`). É integridade de configuração: desliga em silêncio a integração agendada de
  outro cliente.

Os quatro são alcançáveis hoje por qualquer usuário autenticado, e por isso entram. O que fica só registrado (o
diretório de empresas, e o drop e a fila abertos a cliente) não é alcançável hoje, ou é dado de demonstração.

### 2. As fotos só para o tenant dono

O `/trace` e o download passam pelo caso de uso `DocumentTraceQuery` (Application), sobre o `INoteTraceReader`, que o
chamado de suporte já usava. Com tenant diferente do do usuário, a resposta é a mesma de um documento sem fotos (404,
com o mesmo corpo), e o Blob do outro tenant nem é lido: não se confirma a existência de nota alheia. É a mesma regra
que o `/reprocess` já seguia. O zip sai de `TraceArchive.Zip`, a função que o chamado de suporte também usa.

### 3. A ingestão manual no tenant do login

O `/ingest` passa pelo caso de uso `ManualIngestion` (Application). O tenant vem do `ITenantContext`, e o corpo não o
carrega. O `IngestRequest` só tem a chave e o locator.

### 4. O locator mora em quem o lê

O `IInboundSource<T>` ganha `CheckLocator`: quem interpreta o locator é o adapter da origem. A ingestão manual confere
antes de enfileirar (400 com a regra), e a busca confere de novo antes de ler, o que cobre qualquer caminho até a
esteira (drop, descoberta, reprocesso, mensagem na fila). A falha na busca é exceção, e segue o retry e a dead-letter:
ela só acontece se algo contornou a entrada.

- **XML:** o locator é `nfe/{tenant da referência}/{arquivo}`. Sem segmento vazio, `.`, `..` ou barra invertida, porque
  o `System.Uri` normaliza `..` ao montar a URI do blob, e um `nfe/tenant-b/../tenant-a/x.xml` passaria na conferência
  de prefixo feita no texto e seria lido como `nfe/tenant-a/x.xml`.
- **O armazenamento de fotos (`traces`) nunca é origem de ingestão, em nenhum tenant, nem no próprio.** Trace é saída,
  e não entrada. É uma regra própria, conferida antes da do prefixo, e testada à parte, para valer mesmo que a do
  prefixo seja afrouxada um dia.
- **D365:** o formato `d365/{empresa}/{recId}`. O locator do D365 é lido no ERP do próprio tenant, com a credencial
  dele, então o formato é a regra inteira.

Os XMLs de exemplo do seed e o catálogo da descoberta passaram para `nfe/tenant-a/…`, o que alinha o `/ingest` com o
drop, que já gravava em `nfe/{tenant}/`.

### 5. O drop sem tenant padrão

O nome do arquivo no drop é exatamente `{tenant}/{chave}.xml`. Fora disso (na raiz, ou com mais segmentos), o arquivo
fica no drop e não é ingerido, e o watcher avisa uma vez por nome. Antes, a raiz caía no tenant padrão, `tenant-a`: era
o "tenant nulo cai no de dev" no caminho de produção. O `/drop` de dev grava no prefixo do tenant do login.

### 6. Recursos por identificador e campos mortos

O `DeactivateAsync` filtra pelo tenant do contexto, como o `UpdateAsync` e o `ReactivateAsync` já filtravam, e o id de
outro tenant dá 404. O `TenantId` sai do `ManualIntegrationRequest` e do `ScheduleRequest`, que o aceitavam e o
ignoravam, junto com o comentário "tenant nulo cai no de dev", que não era mais verdade. Nenhum fluxo quebra: um JSON
que ainda mande o campo continua aceito, porque propriedade desconhecida é ignorada.

### 7. Os caminhos de produção, conferidos

| Caminho | De onde vem o tenant | De onde vem o locator | Veredito |
|---|---|---|---|
| Drop / Event Grid | do caminho do arquivo, escolhido por quem escreve | do nosso processo, que move para `nfe/{tenant}/{chave}.xml` | **Locator:** conferido. **Tenant:** seguro enquanto só processos nossos escrevem no drop. |
| Feed do D365 | da varredura dos perfis (sistema) | montado pelo feed a partir das linhas do ERP | conferido |
| Fila (`documents-in`, `documents-discovered`) | do corpo da mensagem | do corpo da mensagem | hoje só processos nossos publicam |
| Descoberta (manual, reprocesso) | do login | do catálogo (dado de sistema), filtrado por tenant | conferido |
| Agendador e poll | das linhas gravadas (sistema) | do registro | conferido |

**Pré-condições, antes de abrir a um cliente:**

- **o drop:** a credencial de escrita presa ao tenant (container por tenant, ou SAS de diretório com namespace
  hierárquico), e o tenant tirado dessa ligação, e não do caminho;
- **a fila:** o d365/03 e o CLAUDE.md preveem uma SAS send-only por cliente. Antes de emitir a primeira, fila ou tópico
  por cliente, com o tenant tirado da entidade, e não do corpo. A regra do locator na busca já vale para qualquer
  mensagem.

## Alternativas consideradas

- **Um `if` no endpoint, sem caso de uso.** Resolveria em uma linha, mas o `Program.cs` não tem projeto de teste, e a
  regra ficaria sem teste. Nos casos de uso, ela é testada na Application, inclusive a prova de que o Blob do outro
  tenant não é lido.
- **Conferir o locator só no `/ingest`.** Uma mensagem publicada direto na fila contornaria a regra. Ela mora no source,
  que é quem lê.
- **Uma implementação padrão "aceita tudo" no `CheckLocator`.** Pouparia os fakes de teste, mas um source novo herdaria
  a falta da regra em silêncio. O membro é abstrato.
- **Rejeitar com 400 o corpo que ainda traz `tenantId`.** Seria mais barulhento, mas exigiria manter o campo só para
  recusá-lo. O campo sai, e o tenant do login prevalece.

## Consequências

**Melhora**

- **Nenhuma requisição age sobre outro tenant:** as fotos, a ingestão, o drop e os agendamentos respeitam o login.
- **A regra do locator vale em todo caminho até a esteira,** e não só na porta manual.
- **As pré-condições do drop e da fila estão escritas** antes de alguém abri-los a cliente.

**Piora**

- **O fluxo de dev mudou de caminho:** o XML de exemplo agora é `nfe/tenant-a/nfe-exemplo.xml`, e o `/ingest` do
  RUNNING §4 não manda mais `tenantId`.
- **Um arquivo na raiz do drop deixa de ser ingerido.** Nenhum fluxo documentado fazia isso.

**Pendências** (registradas em `docs/STATUS.md`)

- **O `/ingest` deve existir em produção?** O gatilho real é o drop, o feed e o Event Grid, e ele é conveniência manual.
- **O diretório de empresas não é por tenant:** a porta não recebe tenant, e o adapter JSON é de dev.
- **As pré-condições do drop e da fila,** na fatia que os abrir a cliente.

## Validado no ambiente

2026-09-27, com o host e o mock locais (Azurite, SQL e o emulador do Service Bus pelo `docker compose`), logado como
`admin@fiscalhub.local` (tenant-a) e `beta@fiscalhub.local` (tenant-b). As chaves usadas começam por `d18-probe-`.

- **Preparo:** uma nota do tenant-a pelo `/ingest`, com `nfe/tenant-a/nfe-exemplo.xml`, ficou `Confirmed`. O
  `/trace` do admin trouxe as fotos, e o zip, `avalara.json`, `domain.json` e `source.xml`.
- **Fotos de outro tenant:** o beta pediu o `/trace` e o download dessa nota e recebeu 404
  `{"message":"sem fotos para esse documento."}`. É o mesmo corpo que ele recebe para uma chave inexistente do
  próprio tenant.
- **Locator alheio:** o `/ingest` do beta respondeu 400 com a regra nos três casos:
  - `nfe/tenant-a/…`: "o locator precisa estar no espaço do tenant: nfe/tenant-b/<arquivo>";
  - `traces/…`: "o armazenamento de fotos (traces) nunca é origem de ingestão";
  - `nfe/tenant-b/../tenant-a/…`: a regra do `..`.
- **Tenant no corpo:** o `/ingest` do beta com `"tenantId":"tenant-a"` respondeu 202 com
  `Location: /trace/tenant-b/…`. A linha no SQL ficou no **tenant-b**, e acabou em dead-letter porque o blob de teste
  não existe.
- **Agendamento de outro tenant:** o `deactivate` do beta sobre o agendamento 29 do tenant-a respondeu 404, e o
  agendamento continuou ativo.
- **Drop:** o `/drop` do beta gravou `tenant-b/d18-probe-drop.xml`. O watcher ingeriu no **tenant-b**, onde a nota foi
  rejeitada por falta de `establishments`, que é a configuração do tenant-b.
- **Não exercitado ao vivo:** um arquivo na raiz do drop. Está coberto por teste do `DropBlobNaming`.
