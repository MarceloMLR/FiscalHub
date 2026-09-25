## Purpose

Implementar o feed de mudanças do Dynamics 365 F&O sobre a entidade `FSFiscalDocumentBRs`, por janela
de data em `SysModifiedDateTime`. Cada cabeçalho de documento fiscal que mudou vira uma referência
leve, sem a montagem do documento. A variação entre clientes fica toda em configuração por tenant.

## ADDED Requirements

### Requirement: Consulta de descoberta sobre FSFiscalDocumentBRs

O feed MUST consultar o endpoint OData do ambiente F&O do tenant em `/data/FSFiscalDocumentBRs`, com
`cross-company=true`, filtro `SysModifiedDateTime gt <instante pedido>` (literal DateTimeOffset em UTC),
ordenação ascendente por `SysModifiedDateTime,FiscalDocumentRecId` e `$top` igual ao tamanho de página
do perfil. A consulta MUST projetar só os campos necessários à referência e à paginação (`$select`,
incluindo `FiscalDocumentRecId`). Quando o perfil do tenant listar empresas, o feed MUST restringir a
consulta a elas por `dataAreaId`, combinando com `or`, porque o F&O não suporta `in`. Sem lista, vale
toda empresa que o usuário de integração enxerga. O feed MUST NOT filtrar `Status`, `Model` ou
`Direction` no servidor: esse filtro é decisão do hub (ADR-0023).

#### Scenario: Montagem da URL sem empresas
- **WHEN** o feed é consultado para um tenant sem lista de empresas, com instante 2015-01-01T00:00:00Z e
  `pageSize = 500`
- **THEN** a primeira requisição é `GET <url>/data/FSFiscalDocumentBRs` com `cross-company=true`,
  `$filter=SysModifiedDateTime gt 2015-01-01T00:00:00Z`,
  `$orderby=SysModifiedDateTime,FiscalDocumentRecId`, `$top=500` e `$select` contendo
  `FiscalDocumentRecId`
- **AND** o filtro não menciona `Status`, `Model` nem `Direction`

#### Scenario: Montagem da URL com empresas
- **WHEN** o perfil do tenant lista as empresas `brmf` e `brsp`
- **THEN** o filtro é
  `SysModifiedDateTime gt <instante> and (dataAreaId eq 'brmf' or dataAreaId eq 'brsp')`

### Requirement: Paginação por keyset composto

O feed MUST paginar por keyset em (`SysModifiedDateTime`, `FiscalDocumentRecId`), entregando uma página
por resposta.

- **Âncora.** A partir da segunda página, o filtro MUST ancorar na última linha lida `(T, R)`:
  `(SysModifiedDateTime gt T) or (SysModifiedDateTime eq T and FiscalDocumentRecId gt R)`. Quando houver
  lista de empresas, o filtro de empresas entra combinado com `and`. `T` MUST ser o literal de
  `SysModifiedDateTime` exatamente como veio na resposta.
- **Fim da leitura.** A leitura MUST terminar na primeira página com menos registros que o tamanho de
  página pedido.
- **O que não usar.** O feed MUST NOT seguir `@odata.nextLink`, que é paginação por offset
  (`$skip`/`$top`), nem usar `$skip`. Também MUST NOT refazer a consulta a partir da marca d'água no
  meio de uma leitura.

Atualizações concorrentes na origem MUST NOT fazer uma linha ainda não lida ser pulada. Uma linha
atualizada durante a leitura pode reaparecer mais adiante, como repetição.

#### Scenario: Três páginas por keyset
- **WHEN** `pageSize = 2` e a origem tem 5 registros depois do instante pedido
- **THEN** o feed entrega três páginas, com 2, 2 e 1 referências, e para depois da página com 1
- **AND** o `$filter` da segunda requisição ancora no `SysModifiedDateTime` e no `FiscalDocumentRecId`
  do último registro da primeira página, e o da terceira, no último da segunda

#### Scenario: Âncora combinada com empresas
- **WHEN** o perfil lista a empresa `brmf` e a última linha da página anterior tem
  `SysModifiedDateTime = 2017-01-21T21:23:19Z` e `FiscalDocumentRecId = 5637148912`
- **THEN** o filtro da próxima requisição é
  `((SysModifiedDateTime gt 2017-01-21T21:23:19Z) or (SysModifiedDateTime eq 2017-01-21T21:23:19Z and FiscalDocumentRecId gt 5637148912)) and (dataAreaId eq 'brmf')`

#### Scenario: Empate de timestamp na fronteira da página
- **WHEN** três registros têm o mesmo `SysModifiedDateTime`, `pageSize = 2`, e a primeira página termina
  no segundo deles
- **THEN** a segunda página começa no terceiro registro do empate, sem repetir nem pular nenhum

#### Scenario: Atualização concorrente não pula linha
- **WHEN** um registro já entregue numa página anterior é atualizado na origem antes da próxima
  requisição
- **THEN** os registros ainda não lidos continuam todos sendo entregues
- **AND** o registro atualizado pode aparecer de novo, com o timestamp novo

#### Scenario: Página cheia seguida de página vazia
- **WHEN** `pageSize = 2`, a origem tem exatamente 2 registros e a segunda requisição volta vazia
- **THEN** o feed entrega a página com 2 referências, depois uma página vazia, e encerra a leitura

#### Scenario: nextLink na resposta é ignorado
- **WHEN** uma resposta traz `@odata.nextLink`
- **THEN** a próxima requisição, se houver, é montada pela âncora keyset, e não pela URL do nextLink

### Requirement: Marca alta de cada página

Cada página MUST informar a marca alta que garante.

- **Página intermediária** (página cheia): a marca alta é o maior `SysModifiedDateTime` que a página
  contém.
- **Última página da leitura** (a página curta): a marca alta MUST ser o maior entre esse valor e o
  instante do header `Date` da primeira resposta da leitura. Esse instante é o relógio do web server do
  F&O no início da varredura, que não é necessariamente o mesmo relógio que carimba `ModifiedDateTime`;
  a sobreposição absorve a diferença. Assim a marca acompanha o relógio do lado F&O mesmo sem mudança, e
  um documento sai da janela de sobreposição depois de um número limitado de passadas.
- **Sem `Date` na resposta:** vale só o maior `SysModifiedDateTime` da página.
- **Leitura vazia, sem registros e sem `Date`:** a marca não avança.

#### Scenario: Página intermediária
- **WHEN** uma página cheia traz registros com `SysModifiedDateTime` até 2017-01-21T21:23:19Z
- **THEN** a marca alta dessa página é 2017-01-21T21:23:19Z

#### Scenario: Última página usa o relógio do servidor
- **WHEN** a primeira resposta da leitura trouxe `Date: Fri, 25 Sep 2026 15:00:00 GMT` e a última página
  traz registros até 2017-01-21T21:23:19Z
- **THEN** a marca alta da última página é 2026-09-25T15:00:00Z

#### Scenario: Leitura sem mudanças
- **WHEN** a consulta devolve zero registros com `Date: Fri, 25 Sep 2026 15:00:00 GMT`
- **THEN** o feed entrega uma página vazia com marca alta 2026-09-25T15:00:00Z

### Requirement: Mapeamento do cabeçalho para referência

Cada registro da `FSFiscalDocumentBRs` MUST virar uma referência do tenant com:
- `NaturalKey = <dataAreaId>|<Voucher>` (ex.: `brmf|BRMF21-10000027`);
- `Locator = d365/<dataAreaId>/<Voucher>`, com cada segmento codificado para URL (contrato com a fatia
  de montagem);
- tipo de documento resolvido pelo `Model` num mapa configurável por tenant, com padrão `55` → NF-e de
  mercadoria, `57` → CT-e, `SE` → NFS-e.

Registro sem `Voucher`, ou com `Model` fora do mapa, MUST NOT ser enfileirado nem interromper a leitura.
Ele MUST ser registrado em log de aviso com empresa, voucher e modelo, e a leitura segue.

#### Scenario: Nota de mercadoria
- **WHEN** a consulta traz `dataAreaId = brmf`, `Voucher = BRMF21-10000027`, `Model = 55`
- **THEN** a referência tem `NaturalKey = brmf|BRMF21-10000027`,
  `Locator = d365/brmf/BRMF21-10000027` e tipo NF-e de mercadoria

#### Scenario: Nota de serviço
- **WHEN** a consulta traz `dataAreaId = brmf`, `Voucher = BRMF21-10000019`, `Model = SE`
- **THEN** a referência tem tipo NFS-e

#### Scenario: Modelo fora do mapa
- **WHEN** a consulta traz um registro com `Model = 65` e o mapa do tenant não tem `65`
- **THEN** esse registro não vira referência e um aviso é registrado com empresa, voucher e modelo
- **AND** os demais registros da página seguem normalmente, e a marca alta da página não muda por causa
  dele

#### Scenario: Registro sem Voucher
- **WHEN** a consulta traz um registro com `Voucher` vazio
- **THEN** esse registro não vira referência e um aviso é registrado

### Requirement: Throttling com Retry-After

Diante de HTTP 429 (ou 503 com `Retry-After`), o feed MUST esperar o intervalo indicado em `Retry-After`,
em segundos ou data HTTP, e repetir a mesma requisição. Sem `Retry-After`, a espera MUST crescer de forma
exponencial. As retentativas MUST ter um número máximo. Se a espera pedida passar do teto configurado,
ou se as retentativas se esgotarem, o feed MUST falhar a leitura informando o tempo de espera pedido pela
origem, para que o motor adie o tenant sem avançar a marca. Outras respostas de erro MUST falhar a
leitura.

#### Scenario: 429 com Retry-After curto
- **WHEN** a primeira requisição recebe 429 com `Retry-After: 5` e a repetição recebe 200
- **THEN** o feed espera 5 segundos, repete a mesma URL e entrega a página normalmente

#### Scenario: Retry-After acima do teto
- **WHEN** a requisição recebe 429 com `Retry-After: 600` e o teto de espera é 30 segundos
- **THEN** o feed não espera: falha a leitura informando 600 segundos de espera pedida
- **AND** nenhuma página daquela resposta é entregue

#### Scenario: Retentativas esgotadas
- **WHEN** todas as tentativas permitidas recebem 429
- **THEN** o feed falha a leitura como throttling

#### Scenario: Erro não transitório
- **WHEN** a requisição recebe 401 ou 400
- **THEN** o feed falha a leitura sem repetir

### Requirement: Autenticação no F&O

O feed MUST autenticar cada requisição com um bearer token do Entra ID para o recurso do ambiente F&O do
tenant (escopo `<url>/.default`). Em produção, o token MUST vir do fluxo client credentials, com tenant
do Entra, client id e referência ao segredo nas settings do tenant. O segredo em si MUST NOT ficar nas
settings: só a referência (`kv:<nome>`), resolvida fora do banco. Em desenvolvimento, e só quando o host
habilitar explicitamente esse modo, o token MUST vir da sessão do Azure CLI do desenvolvedor. O token
MUST ser reaproveitado enquanto válido. O detalhe de autenticação MUST NOT vazar do adapter.

#### Scenario: Client credentials
- **WHEN** o tenant está configurado com tenant do Entra, client id e `clientSecretRef = kv:d365-a-secret`
- **THEN** o feed obtém o token por client credentials usando o segredo resolvido da referência e o
  envia como `Authorization: Bearer`

#### Scenario: Segredo em claro é recusado
- **WHEN** as settings trazem o segredo em claro, sem o prefixo `kv:`
- **THEN** a leitura falha com erro de configuração, sem chamar o F&O

#### Scenario: Azure CLI em desenvolvimento
- **WHEN** o host roda em Development com o modo Azure CLI habilitado e o desenvolvedor fez `az login`
- **THEN** o feed usa o token da sessão do Azure CLI para o recurso do ambiente

#### Scenario: Azure CLI fora de desenvolvimento
- **WHEN** o host não habilitou o modo Azure CLI
- **THEN** o feed usa client credentials, e nenhum token do Azure CLI é obtido

### Requirement: Configuração por tenant

As settings do adapter `Dynamics365` no perfil de conector do tenant MUST conter:

- URL do ambiente F&O;
- autenticação;
- empresas (opcional);
- tamanho da página (padrão 500, entre 1 e 10.000, o teto de página do servidor F&O);
- mapa de modelos (opcional, com o padrão acima);
- seção de poll: ligado, intervalo, sobreposição e marca inicial.

Nenhum valor específico de cliente MUST existir em código. Settings inválidas de um tenant MUST falhar
só o poll desse tenant, com erro de configuração registrado. São inválidas: URL ausente ou não
absoluta, JSON malformado, autenticação incompleta e tamanho de página fora de 1 a 10.000.

#### Scenario: Settings mínimas
- **WHEN** o perfil do tenant-a tem só a URL, a autenticação e `poll.enabled = true`
- **THEN** o poll usa página de 500, intervalo de 60s, sobreposição de 300s, todas as empresas e o mapa
  de modelos padrão

#### Scenario: URL inválida
- **WHEN** as settings do tenant-a têm `url` vazia
- **THEN** o poll do tenant-a falha com erro de configuração registrado, sem chamar a rede
- **AND** os demais tenants seguem

#### Scenario: Tamanho de página acima do teto
- **WHEN** as settings do tenant-a têm `pageSize = 20000`
- **THEN** o poll do tenant-a falha com erro de configuração registrado, sem chamar a rede

### Requirement: Teste contra o ambiente real opt-in

Um teste de integração MUST rodar o feed contra um ambiente F&O real, e MUST ser pulado a menos
que a variável de ambiente que aponta o ambiente esteja definida, de modo que `dotnet test` em
qualquer máquina sem essa variável (CI inclusive) fique verde sem tocar a rede.

#### Scenario: Sem a variável de ambiente
- **WHEN** `dotnet test` roda numa máquina sem a variável do ambiente F&O
- **THEN** o teste de integração aparece como pulado, e nenhuma chamada de rede é feita

#### Scenario: Contra o fiscosysdev
- **WHEN** a variável aponta `https://fiscosysdev.operations.dynamics.com`, a empresa é `brmf`, o
  desenvolvedor está logado no Azure CLI, `pageSize = 20` e o instante pedido é 2015-01-01T00:00:00Z
- **THEN** o feed lê os 83 cabeçalhos da empresa `brmf` em 5 páginas por keyset, com 83
  `FiscalDocumentRecId` distintos (nenhum repetido, nenhum pulado)
- **AND** cada cabeçalho vira referência com NaturalKey `brmf|<Voucher>` ou aviso de modelo fora do mapa
