## MODIFIED Requirements

### Requirement: As empresas e os contribuintes da conta do tenant

O adapter da Avalara MUST declarar a listagem de estabelecimentos. A listagem MUST ler, na URL base do conector na
**versão que ela declara usar** — a mesma do despacho fiscal. A chave da versão só escolhe a base: os caminhos abaixo são
relativos a ela, e já trazem o prefixo da API, sem nada acrescentado pela chave (`platform-account-connectors`):

- **as empresas:** em `taxcompliance/v2/empresa`, com `$select=empresaId,codigoCIA,descricao` e `$orderby=empresaId`;
- **os contribuintes de cada empresa:** em `taxcompliance/v2/contribuinte?empresaId=<empresaId>`, com o `empresaId` como
  veio, `$select=contribuinteId,codigo,cnpj` e `$orderby=contribuinteId`.

A resposta de cada página, nos dois endpoints, vem numa das duas formas do requisito "As duas formas de uma lista". Nenhum
endpoint tem uma forma fixa.

O `empresaId` é obrigatório nos contribuintes, e por isso a leitura é por empresa: não há como pedir os contribuintes de um
CNPJ na conta inteira. Uma empresa pode ter vários contribuintes, ou nenhum. Um campo que venha além dos pedidos no
`$select` é ignorado.

A `descricao` da empresa serve só ao motivo da duplicidade (`platform-establishment-resolution`). Ela MUST NOT participar
do casamento nem ir no payload.

A listagem MUST NOT filtrar por tenant: a credencial da conta de plataforma do tenant já a limita à conta dele. Ela MUST
NOT mandar o `subscriptionId`, que é opcional nos dois endpoints.

#### Scenario: Os dois formatos
- **WHEN** a plataforma devolve `[{"empresaId": 9101, "codigoCIA": "017", "descricao": "EMPRESA EXEMPLO"}]` em empresas,
  e `{"value": [{"contribuinteId": 50001, "codigo": "031", "cnpj": "11222333000181"}]}` nos contribuintes da empresa `9101`
- **THEN** o CNPJ `11222333000181` é listado com o código de empresa `017`, a descrição `EMPRESA EXEMPLO` e o código de
  contribuinte `031`

#### Scenario: O `$select` e o `$orderby` nos dois pedidos
- **WHEN** a listagem pede as empresas e os contribuintes da empresa `9101`
- **THEN** o pedido de empresas leva `$select=empresaId,codigoCIA,descricao` e `$orderby=empresaId`
- **AND** o de contribuintes leva `empresaId=9101`, `$select=contribuinteId,codigo,cnpj` e `$orderby=contribuinteId`

#### Scenario: Campos além do `$select`
- **WHEN** a plataforma ignora o `$select` e devolve também `idPortalCompany` nas empresas e `razao` nos contribuintes
- **THEN** a listagem segue com o mesmo resultado

#### Scenario: Uma empresa com vários contribuintes
- **WHEN** a empresa `017` tem três contribuintes, com três CNPJs
- **THEN** os três são listados, cada um com o código de empresa `017` e o próprio código

#### Scenario: Uma empresa sem contribuinte
- **WHEN** a empresa `LAB` devolve a lista vazia de contribuintes, `{"value": []}` ou `[]`
- **THEN** a listagem segue, sem estabelecimento dessa empresa

### Requirement: A credencial e o token do envio

A listagem MUST usar a URL base da versão que ela declara e a credencial e o token da conta de plataforma do tenant
(`avalara-tenant-authentication`):

- **o token:** o mesmo cache, chaveado pela conta, e pedido ao endpoint de token da conta. Com um token válido guardado,
  a listagem MUST NOT pedir outro;
- **o endereço:** a mesma regra. O pedido de token só vai ao endpoint de token da conta, e o token só às URLs base do
  conector, por `https`, ou por `http` só em loopback;
- **a redação:** a mesma do envio. O corpo das respostas é redigido antes de qualquer motivo ou log. A listagem MUST NOT
  ser gravada como foto do documento.

#### Scenario: Um token para a listagem e para o envio
- **WHEN** a primeira nota do tenant-a é despachada sem token guardado, e a resolução lista a plataforma
- **THEN** o endpoint de token recebe um pedido só, para a listagem e para o envio
