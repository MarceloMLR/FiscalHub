# avalara-tenant-authentication Specification

## Purpose

Autenticar cada envio e cada consulta à plataforma de compliance com a credencial do próprio tenant, no
ambiente ativo dele. Nunca degradar para "sem autenticação" em silêncio, e nunca mandar a credencial para um
endereço de outro ambiente ou de outro tenant.

## Requirements

### Requirement: A credencial só vai ao endereço do próprio ambiente

O pedido de token MUST ir só ao endpoint de token da conta de plataforma do próprio tenant, e o token, só às URLs base
dos conectores dessa conta. As URLs MUST usar `https`. A exceção é o endereço de loopback, que é o mock local. Uma URL
fora disso MUST ser rejeitada como configuração do conector, antes de qualquer requisição — e já é recusada na
gravação (`platform-account-connectors`), o que esta regra repete na leitura para o que chegou ao banco por outro
caminho.

Como cada instância atende um ambiente só, não existe caminho em que a credencial de uma instância alcance o endereço
configurado em outra.

#### Scenario: URL sem TLS fora do loopback
- **WHEN** a base da versão que a integração usa, gravada por SQL, é `http://sandbox.exemplo.com/`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração que cita a exigência de `https`
- **AND** nenhuma requisição é feita

#### Scenario: Endpoint de token sem TLS
- **WHEN** o `tokenUrl` da conta, gravado por SQL, é `http://login.exemplo.com/oauth/token`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração que cita a exigência de `https`
- **AND** nenhuma requisição é feita, nem ao endpoint de token

#### Scenario: Mock local
- **WHEN** a base da versão que a integração usa é `http://localhost:5100/`, e o `tokenUrl` da conta é
  `http://localhost:5100/oauth/token`
- **THEN** as duas URLs são aceitas

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
- a conta de plataforma;
- o endpoint de token da conta;
- o identificador do cliente;
- a versão do segredo. A rotação do segredo gera um token novo.

**O cache MUST NOT distinguir o conector.** A credencial e o endpoint de token são da conta, e dois conectores sob ela
compartilham o token: chavear por conector, ou por um endpoint derivado da base de cada um, faria a plataforma receber
dois pedidos para a mesma credencial, sem motivo. **O ambiente também não entra na chave**, porque a instância é de um
ambiente só.

Dois tenants nunca compartilham um token, nem com credenciais idênticas. Um token sem validade informada
MUST ser usado na requisição e não entrar no cache.

#### Scenario: Reuso
- **WHEN** o tenant-a envia duas notas seguidas, e o token vale por mais tempo que a margem
- **THEN** o endpoint de token é chamado uma vez

#### Scenario: Dois conectores compartilham o token
- **WHEN** dois conectores do tenant-a, sob a mesma conta, fazem requisições seguidas dentro da validade do token
- **THEN** o endpoint de token é chamado uma vez, e os dois usam o mesmo token

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
e os tokens em cache daquele tenant, em todas as contas e todos os conectores. Isso vale mesmo dentro do intervalo, e
mesmo quando a credencial gravada é igual à anterior, porque a correção pode ter sido feita do lado da
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

### Requirement: O teste de credencial conversa com a recusa lembrada

O teste de credencial da Avalara (capability `connector-credential-test`) troca o token sem passar pela recusa lembrada
do envio. Quem freia o botão é o endpoint de teste. O resultado do teste MUST valer para o envio:

- **Um teste que dá certo:** MUST esquecer, na hora, a recusa lembrada do tenant e os tokens dele em cache, como o
  salvar do perfil já faz. O token novo pode ser guardado no cache.
- **Um teste recusado:** a recusa MUST passar a valer como recusa lembrada do envio daquela credencial, pelo mesmo
  intervalo. Assim, as notas em voo não viram, cada uma, uma tentativa de login com a credencial errada.
- **Os outros tenants:** MUST NOT ser afetados.

O teste é da **conta**, e não de um conector: é a credencial da conta que ele troca por um token, no endpoint de token
da conta.

#### Scenario: Liberação do lado da plataforma, provada pelo teste
- **WHEN** o endpoint de token recusou a credencial do tenant-a no envio, a plataforma liberou o cliente, e o Admin
  testa a credencial dentro do intervalo, sem mudar nada
- **THEN** o teste vai ao endpoint de token e funciona
- **AND** o envio seguinte do tenant-a pede token na hora, sem esperar o intervalo

#### Scenario: Recusa no teste vale para o envio
- **WHEN** o teste da credencial da conta `Avalara` do tenant-a é recusado, e uma nota do tenant-a vai ser enviada logo
  depois
- **THEN** o envio falha com a recusa lembrada, sem pedir token

#### Scenario: Outro tenant não é afetado pelo teste
- **WHEN** o tenant-a e o tenant-b têm credenciais recusadas, e o teste do tenant-a dá certo
- **THEN** o envio seguinte do tenant-b continua falhando com a recusa lembrada, sem pedir token

### Requirement: Credencial da conta de plataforma, URL base da versão da integração

A credencial e o endpoint de token MUST vir da **conta de plataforma** do tenant, e a URL base, do **conector** do
adapter que despacha (`platform-account-connectors`). O ambiente é o da instância, e não um campo do perfil.

- **Na conta:** o identificador do cliente (`clientId`), a referência ao segredo (`clientSecretRef`) e o endpoint de
  token (`tokenUrl`). O endpoint de token é **obrigatório**: não existe derivação a partir da URL base de um conector,
  nem de caminho de token na configuração do host. A credencial é da conta, e o endereço que a troca por um token
  também.
- **No conector:** o mapa de URLs base, com uma entrada por versão de API declarada pelo adapter, e o resto das
  settings do produto. Cada integração declara **qual versão usa**, e o envio toma a base dessa versão. A chave da versão
  escolhe a base e não entra na URL (`platform-account-connectors`).

A credencial é uma só para todos os conectores da conta: o token emitido vale para todos os produtos daquela
plataforma naquele ambiente (ADR-0036 §2). Como o endpoint de token é da conta, ele é o mesmo para todos os conectores
dela, qualquer que seja a versão de base que cada integração usa.

Nenhuma configuração global do adapter fornece credencial ou URL. Faltando o `clientId`, a referência do segredo, o
`tokenUrl` ou a URL base da versão que a integração usa, o envio MUST ser rejeitado como configuração do conector,
nomeando os campos e o lugar deles — a conta ou o conector —, sem nenhuma requisição.

#### Scenario: Dois tenants, duas credenciais
- **WHEN** o tenant-a e o tenant-b enviam notas, cada um com a sua conta, com `clientId` e `clientSecretRef` próprios
- **THEN** o pedido de token de cada um leva o próprio `clientId` e o próprio segredo, ao endpoint da própria conta

#### Scenario: Dois conectores, um token
- **WHEN** o tenant-a tem dois conectores sob a mesma conta, e os dois precisam falar com a plataforma
- **THEN** os dois usam a credencial da conta
- **AND** o endpoint de token não é chamado uma vez por conector

#### Scenario: Conta sem credencial
- **WHEN** a conta de plataforma do tenant não tem `clientId` nem o Client Secret configurado
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia `clientId` e o
  Client Secret na conta e aponta a tela de conectores
- **AND** nenhuma requisição é feita

#### Scenario: Conta sem endpoint de token
- **WHEN** a conta de plataforma do tenant tem `clientId` e o Client Secret configurados, e não tem `tokenUrl`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o `tokenUrl` na
  conta e aponta a tela de conectores
- **AND** nenhuma requisição é feita, nem a um endpoint de token montado a partir da URL base do conector

#### Scenario: Conector sem URL base
- **WHEN** a conta tem credencial, mas o conector não tem base para a versão que a integração do despacho usa
- **THEN** o documento é registrado como rejeitado, com motivo de configuração que nomeia a versão, a integração e o
  conector
- **AND** nenhuma requisição é feita, e não há fallback para outra URL

#### Scenario: Produção é outra instância
- **WHEN** o cliente precisa integrar em produção
- **THEN** isso é outra instância, com o próprio cofre e a própria credencial de produção
- **AND** nenhuma troca de ambiente acontece dentro de uma instância
