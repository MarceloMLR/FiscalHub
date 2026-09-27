## Purpose

Autenticar cada envio e cada consulta à plataforma de compliance com a credencial do próprio tenant, no
ambiente ativo dele. Nunca degradar para "sem autenticação" em silêncio, e nunca mandar a credencial para um
endereço de outro ambiente ou de outro tenant.

## ADDED Requirements

### Requirement: Credencial por tenant e por ambiente

A credencial MUST vir das settings de saída do tenant, na seção do ambiente ativo. A seção traz:

- a URL base do ambiente (`baseUrl`);
- o identificador do cliente (`clientId`);
- a referência ao segredo (`clientSecretRef`);
- opcionalmente, o endpoint de token (`tokenUrl`). Sem ele, o endpoint é a URL base mais o caminho de token
  da plataforma.

Nenhuma configuração global do adapter fornece credencial ou URL. Faltando a URL base, o identificador ou a
referência, o envio MUST ser rejeitado como configuração do conector, nomeando os campos e o caminho, sem
nenhuma requisição.

#### Scenario: Dois tenants, duas credenciais
- **WHEN** o tenant-a e o tenant-b enviam notas, cada um com `clientId` e `clientSecretRef` próprios
- **THEN** o pedido de token de cada um leva o próprio `clientId` e o próprio segredo, ao endpoint da própria
  seção

#### Scenario: Troca de ambiente
- **WHEN** o tenant-a passa o ambiente ativo de Sandbox para Production
- **THEN** o envio seguinte usa a credencial e as URLs da seção `production`

#### Scenario: Seção sem credencial
- **WHEN** a seção do ambiente ativo não tem `clientId` nem o Client Secret configurado
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia
  `clientId` e o Client Secret em `OutboundSettings.<ambiente>` e aponta a tela de conectores
- **AND** nenhuma requisição é feita

#### Scenario: Seção sem URL base
- **WHEN** a seção do ambiente ativo tem credencial, mas não tem `baseUrl`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração que nomeia `baseUrl`
- **AND** nenhuma requisição é feita, e não há fallback para outra URL

### Requirement: A credencial só vai ao endereço do próprio ambiente

O pedido de token e o token MUST ir só às URLs da mesma seção do mesmo tenant. As URLs MUST usar `https`.
A exceção é o endereço de loopback, que é o mock local. Uma URL fora disso MUST ser rejeitada como
configuração do conector, antes de qualquer requisição.

#### Scenario: URL sem TLS fora do loopback
- **WHEN** a seção do ambiente ativo traz `"baseUrl": "http://sandbox.exemplo.com/"`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração que cita a exigência de
  `https`
- **AND** nenhuma requisição é feita

#### Scenario: Mock local
- **WHEN** a seção traz `"baseUrl": "http://localhost:5100/"`
- **THEN** a URL é aceita

### Requirement: Autenticação real por padrão

A composição padrão do adapter MUST autenticar todo envio e toda consulta de status com o token do tenant.
O envio sem autenticação MUST acontecer só quando a composição do host o pede explicitamente, e o host da
aplicação não pede. Na composição padrão, nenhum caminho leva a uma requisição sem autorização: nem a falta
de credencial, nem a falta do segredo, nem uma falha do endpoint de token.

#### Scenario: Composição padrão
- **WHEN** o adapter é composto sem nenhum pedido explícito
- **THEN** todo envio e toda consulta levam o cabeçalho de autorização com o token do tenant

#### Scenario: Sem autenticação por pedido explícito
- **WHEN** a composição pede explicitamente o envio sem autenticação
- **THEN** as requisições saem sem o cabeçalho de autorização, e nenhuma credencial é lida

#### Scenario: Falta de segredo na composição padrão
- **WHEN** o segredo do tenant não resolve
- **THEN** o envio falha como configuração do conector
- **AND** nenhuma requisição sai sem o cabeçalho de autorização

### Requirement: Token por credencial, com cache e margem de renovação

O token MUST ser reusado enquanto for válido além da margem de renovação, e renovado dentro dela. Sob
concorrência, cada credencial MUST ter uma única busca em andamento. O cache MUST distinguir:

- o tenant;
- o ambiente;
- o endpoint de token;
- o identificador do cliente;
- a versão do segredo. A rotação do segredo gera um token novo.

Dois tenants nunca compartilham um token, nem com credenciais idênticas. Um token sem validade informada
MUST ser usado na requisição e não entrar no cache.

#### Scenario: Reuso
- **WHEN** o tenant-a envia duas notas seguidas, e o token vale por mais tempo que a margem
- **THEN** o endpoint de token é chamado uma vez

#### Scenario: Renovação dentro da margem
- **WHEN** o token do tenant-a vence em menos tempo que a margem de renovação
- **THEN** o envio seguinte busca um token novo

#### Scenario: Concorrência
- **WHEN** vinte envios do tenant-a pedem token ao mesmo tempo, com o cache vazio
- **THEN** o endpoint de token é chamado uma vez, e todos usam o mesmo token

#### Scenario: Tenants não compartilham token
- **WHEN** o tenant-a e o tenant-b têm credenciais diferentes e enviam notas
- **THEN** cada um recebe o próprio token, e nenhum envio de um leva o token do outro

#### Scenario: Rotação do segredo
- **WHEN** o valor do segredo do tenant-a muda na configuração, com o token antigo ainda válido
- **THEN** o envio seguinte busca um token novo com o segredo novo

#### Scenario: Token sem validade informada
- **WHEN** o endpoint de token responde com o token, sem o tempo de validade
- **THEN** o token é usado naquele envio, e o envio seguinte busca outro

### Requirement: 401 invalida o token do cache

Quando o envio ou a consulta recebe HTTP 401 com um token vindo do cache, a entrada MUST ser invalidada, e
a tentativa seguinte busca um token novo. O desfecho dessa tentativa segue o retry nativo. O 401 com um
token recém-emitido é tratado em `compliance-dispatch-outcome`.

#### Scenario: Token do cache revogado
- **WHEN** a plataforma responde 401 a um envio feito com o token do cache
- **THEN** o token é invalidado, e a mensagem segue o retry nativo
- **AND** a tentativa seguinte busca um token novo antes de enviar

### Requirement: Falhas do endpoint de token

O endpoint de token pode falhar de três formas:

- **Credencial recusada** (HTTP 400 ou 401): o documento MUST ser registrado como impossibilidade do lado
  do conector, sem retentativa e sem requisição de envio. O motivo cita o tenant, o ambiente, o status e o
  código e a descrição do erro da plataforma, já redigidos. Retentar pode bloquear a conta, e não conserta
  a credencial.
- **Indisponibilidade** (5xx, 429, falha de rede): segue o retry nativo do transporte.
- **Sucesso sem token:** o documento MUST ser registrado como impossibilidade do lado do conector, com
  motivo que diz que o endpoint respondeu sem token.

A recusa da credencial MUST ser lembrada por aquela versão da credencial durante um intervalo configurável.
Nesse intervalo, os envios e as consultas seguintes do tenant falham com o mesmo motivo, sem chamar o
endpoint de token. Uma credencial nova (outro `clientId` ou outro segredo) não herda a recusa.

#### Scenario: Recusa lembrada
- **WHEN** o endpoint de token recusa a credencial do tenant-a, e a esteira processa em seguida mais quatro
  notas do tenant-a
- **THEN** o endpoint de token recebeu um único pedido, e as cinco notas foram registradas como rejeitadas
  com o mesmo motivo

#### Scenario: Credencial corrigida
- **WHEN** o segredo do tenant-a é trocado depois da recusa, e a nota é reprocessada dentro do intervalo
- **THEN** o endpoint de token recebe um pedido novo, com o segredo novo

#### Scenario: Credencial recusada
- **WHEN** o endpoint de token responde 401 com `{"error":"invalid_client"}`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que cita o
  tenant, o ambiente e `invalid_client`
- **AND** o endpoint de token recebeu um único pedido, a plataforma não recebeu nenhum envio, e a mensagem
  não volta para a fila

#### Scenario: Endpoint de token indisponível
- **WHEN** o endpoint de token responde 503
- **THEN** o envio falha, e a mensagem segue o retry nativo

#### Scenario: Sucesso sem token
- **WHEN** o endpoint de token responde 200 sem o token
- **THEN** o documento é registrado como rejeitado, com motivo que diz que o endpoint respondeu sem token

### Requirement: Salvar o perfil esquece a recusa e os tokens do tenant

Gravar o perfil de conector de um tenant pela tela de conectores MUST esquecer, na hora, a recusa lembrada
e os tokens em cache daquele tenant, em todos os ambientes. Isso vale mesmo dentro do intervalo, e mesmo
quando a credencial gravada é igual à anterior, porque a correção pode ter sido feita do lado da
plataforma. Os outros tenants MUST NOT ser afetados. Uma gravação recusada na validação, antes de qualquer
escrita, não esquece nada.

A gravação de um segredo no cofre conta como gravação, mesmo que a do perfil falhe depois. O nome no cofre é
fixo por caminho, então o segredo já mudou.

#### Scenario: Correção do segredo pela tela
- **WHEN** o endpoint de token recusou a credencial do tenant-a, e um Admin do tenant-a grava o perfil com
  o segredo corrigido dentro do intervalo
- **THEN** o envio seguinte do tenant-a pede token ao endpoint na hora, sem esperar o intervalo

#### Scenario: Correção do lado da plataforma
- **WHEN** o endpoint de token recusou a credencial do tenant-a, a plataforma liberou o cliente, e um Admin
  grava o perfil do tenant-a sem mudar nada, dentro do intervalo
- **THEN** o envio seguinte do tenant-a pede token ao endpoint com a mesma credencial

#### Scenario: Outro tenant não é afetado
- **WHEN** o tenant-a e o tenant-b têm credenciais recusadas, e um Admin do tenant-a grava o perfil
- **THEN** o envio seguinte do tenant-b continua falhando com a recusa lembrada, sem pedir token

#### Scenario: Gravação recusada não esquece
- **WHEN** a gravação do perfil do tenant-a é recusada na validação, por trazer uma referência `*Ref` na
  requisição
- **THEN** a recusa lembrada do tenant-a continua valendo

#### Scenario: Segredo gravado, perfil falhou
- **WHEN** um Admin do tenant-a grava um Client Secret novo, o cofre aceita, e a gravação do perfil falha logo
  depois
- **THEN** a recusa e os tokens do tenant-a são esquecidos mesmo assim, e o envio seguinte pede token com o
  segredo novo
