## Purpose

Manter cada requisição e cada ingestão dentro do tenant de quem a faz. O tenant vem de quem está logado, e
nunca da requisição. Nenhum usuário lê, grava ou altera dado fiscal ou configuração de outro tenant, nem
diretamente nem através da esteira.

## ADDED Requirements

### Requirement: O tenant vem de quem está logado

Todo endpoint autenticado MUST agir sobre o tenant do usuário logado.

- **Tenant na requisição:** um tenant no corpo, na rota ou na query MUST NOT decidir sobre qual tenant a
  requisição age. O endpoint compara o tenant da requisição com o do usuário, ou não o aceita.
- **Recurso por identificador:** um agendamento ou um usuário referenciado por identificador MUST ser
  procurado só entre os recursos do tenant do usuário.
- **Agir sobre outro tenant:** se um fluxo legítimo precisar disso (integração interna, serviço a serviço),
  MUST existir um papel explícito, com autorização própria. Nunca um campo aberto na requisição.

#### Scenario: Tenant de outro no corpo
- **WHEN** um usuário do tenant-b chama um endpoint mandando `"tenantId": "tenant-a"` no corpo
- **THEN** a requisição age sobre o tenant-b, ou é recusada, e nada muda no tenant-a

#### Scenario: Agendamento de outro tenant por identificador
- **WHEN** um usuário do tenant-b desativa o agendamento de id 7, que é do tenant-a
- **THEN** a resposta é 404, e o agendamento do tenant-a continua ativo

### Requirement: Fotos só para o tenant do usuário

A inspeção do trace e o download das fotos de um documento MUST comparar o tenant da rota com o tenant do
usuário logado:

- **Tenants diferentes:** a resposta MUST ser a mesma de um documento sem fotos (HTTP 404, com o mesmo
  corpo), e o armazenamento das fotos do outro tenant MUST NOT ser lido.
- **Mesmo tenant:** o comportamento é o de hoje.

#### Scenario: Download de documento de outro tenant
- **WHEN** um usuário do tenant-b pede o download das fotos de `tenant-a/<chave>`, que existe
- **THEN** a resposta é 404, com o mesmo corpo de um documento inexistente
- **AND** o armazenamento das fotos do tenant-a não é lido

#### Scenario: Inspeção de documento de outro tenant
- **WHEN** um usuário do tenant-b pede a inspeção do trace de `tenant-a/<chave>`
- **THEN** a resposta é 404, com o mesmo corpo de um documento inexistente
- **AND** o armazenamento das fotos do tenant-a não é lido

#### Scenario: Download do próprio tenant
- **WHEN** um usuário do tenant-a pede o download das fotos de `tenant-a/<chave>`
- **THEN** a resposta é o zip com as fotos do documento, incluindo as respostas da plataforma

#### Scenario: Documento inexistente do próprio tenant
- **WHEN** um usuário do tenant-a pede o download de `tenant-a/<chave>`, que não tem fotos
- **THEN** a resposta é 404, com o mesmo corpo do caso de outro tenant

### Requirement: Ingestão manual no tenant do usuário

A ingestão manual (`POST /ingest`) MUST enfileirar a referência no tenant do usuário logado. O corpo MUST NOT
carregar o tenant: um `tenantId` no corpo é ignorado e não muda o destino. O locator da referência MUST passar
pela regra de locator da origem antes de enfileirar. Um locator recusado dá HTTP 400 com a regra, e nada é
enfileirado.

#### Scenario: Ingestão com tenant de outro no corpo
- **WHEN** um usuário do tenant-b chama `/ingest` com `"tenantId": "tenant-a"` e o locator `nfe/tenant-b/nfe-1.xml`
- **THEN** a referência enfileirada é do tenant-b
- **AND** nada é enfileirado para o tenant-a

#### Scenario: Locator de outro tenant
- **WHEN** um usuário do tenant-b chama `/ingest` com o locator `nfe/tenant-a/nfe-1.xml`
- **THEN** a resposta é 400, com a regra do espaço do tenant, e nada é enfileirado

### Requirement: Locator dentro do espaço do tenant

Para documento de origem XML, o locator MUST apontar para o espaço de entrada do próprio tenant da
referência, no formato `nfe/{tenant}/{arquivo}`. A regra vale na ingestão manual e na busca do documento, e
por isso cobre qualquer caminho que leve a referência à esteira: ingestão manual, zona de drop, descoberta,
reprocesso ou mensagem na fila. O locator é recusado nos casos a seguir:

- aponta para outro container;
- aponta para o prefixo de outro tenant;
- tem segmento `.` ou `..`, ou barra invertida;
- não tem arquivo depois do prefixo.

Na busca, o locator recusado falha sem ler o armazenamento.

**O armazenamento de fotos (`traces`) MUST NOT ser origem de ingestão, em nenhum tenant, nem no próprio.** O
trace é saída, e não entrada. Esta regra é separada da regra do prefixo e vale mesmo que a do prefixo seja
afrouxada.

#### Scenario: Foto de outro tenant como locator
- **WHEN** uma referência do tenant-b chega à busca com o locator `traces/tenant-a/202609/<chave>/source.xml`
- **THEN** a busca falha pela regra do armazenamento de fotos, sem ler o armazenamento

#### Scenario: Foto do próprio tenant como locator
- **WHEN** uma referência do tenant-a chega à busca com o locator `traces/tenant-a/202609/<chave>/source.xml`
- **THEN** a busca falha pela regra do armazenamento de fotos, sem ler o armazenamento

#### Scenario: Travessia de caminho
- **WHEN** uma referência do tenant-b chega com o locator `nfe/tenant-b/../tenant-a/nfe-1.xml`
- **THEN** a busca falha pela regra do espaço do tenant, sem ler o armazenamento

#### Scenario: Locator do próprio espaço
- **WHEN** uma referência do tenant-a chega com o locator `nfe/tenant-a/nfe-exemplo.xml`
- **THEN** o documento é lido e segue a esteira

### Requirement: Zona de drop sem tenant padrão

A zona de drop MUST tirar o tenant do caminho do arquivo, no formato `{tenant}/{chave}.xml`, com exatamente
dois segmentos. Um arquivo fora desse formato (na raiz ou com mais segmentos) MUST NOT ser ingerido. Ele fica
na zona de drop, e o hub registra um aviso que cita o formato esperado. Não há tenant padrão.

O endpoint de desenvolvimento que simula o drop (`POST /drop/{key}`) MUST gravar o arquivo no prefixo do
tenant do usuário logado.

#### Scenario: Arquivo na raiz do drop
- **WHEN** um arquivo `nfe-600.xml` cai na raiz da zona de drop
- **THEN** ele não é ingerido, e nenhum documento é criado em tenant nenhum

#### Scenario: Arquivo no prefixo de um tenant
- **WHEN** um arquivo `tenant-b/nfe-600.xml` cai na zona de drop
- **THEN** ele é ingerido no tenant-b, com a chave `nfe-600`

#### Scenario: Drop simulado por um usuário
- **WHEN** um usuário do tenant-b chama `POST /drop/nfe-700`
- **THEN** o arquivo cai em `tenant-b/nfe-700.xml`, e não no prefixo de outro tenant
