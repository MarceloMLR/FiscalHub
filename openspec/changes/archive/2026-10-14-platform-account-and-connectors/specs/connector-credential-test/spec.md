## MODIFIED Requirements

### Requirement: O teste usa a credencial gravada

O teste MUST usar a credencial gravada no perfil do tenant do usuário logado, com o segredo lido do cofre no servidor. A
API é `POST /connector/test`, só para Admin.

- **A requisição:** diz só o lado — a entrada (ERP) ou a conta de plataforma. Ela MUST NOT levar o ambiente, que é o da
  instância, nem credencial, e o servidor não usa nenhum outro campo que venha nela. Um corpo que ainda traga o
  ambiente tem o campo ignorado.
- **Quem tem teste:** o `Dynamics365`, na entrada, e a `Avalara`, na conta de plataforma. Para outro adapter ou outra
  plataforma, a resposta é 400, e diz que não tem teste de credencial.
- **Credencial incompleta:** falta um campo da credencial — na conta de plataforma, o `clientId` ou o `tokenUrl` —, o
  segredo não está configurado, ou o cofre não tem o valor. Nesses casos, a resposta MUST dizer que não funcionou, e o
  log nomeia o que falta, sem nenhuma requisição à plataforma nem ao ERP.
- **A tela:** com uma edição pendente no formulário de Configurações, o botão MUST pedir para salvar antes, e não chama
  o teste. O teste usa a credencial gravada, e testar a da tela daria uma resposta sobre outra credencial. Com a
  credencial gravada da conta incompleta — sem o Client ID, o Client Secret ou o endpoint de token —, o botão MUST ficar
  desabilitado, dizendo o que falta, e não chama o teste: a resposta do servidor diria só que não funcionou, e o detalhe
  ficaria no log.

#### Scenario: Testar a credencial do ERP
- **WHEN** o Admin do tenant-a, sem edição pendente, clica em "Testar credencial" na aba do ERP
- **THEN** o servidor testa o Client ID, o tenant do Entra e o Client Secret gravados para o tenant-a

#### Scenario: Edição pendente
- **WHEN** o Admin trocou o Client ID na tela e ainda não salvou, e clica em "Testar credencial"
- **THEN** a tela pede para salvar antes de testar
- **AND** nenhuma requisição de teste é enviada

#### Scenario: Conta sem o endpoint de token
- **WHEN** a conta `Avalara` do tenant-a está gravada sem o `tokenUrl`, como a migração a deixa
- **THEN** o botão de testar a conta fica desabilitado, dizendo que falta o endpoint de token
- **AND** nenhuma requisição de teste é enviada

#### Scenario: Segredo não configurado
- **WHEN** o perfil do tenant-a não tem o Client Secret da conta `Avalara`, e o Admin testa a conta
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log diz que o Client Secret da conta não está
  configurado
- **AND** nenhuma requisição vai ao endpoint de token

#### Scenario: Requisição com o ambiente é aceita e o campo é ignorado
- **WHEN** um dashboard antigo chama `POST /connector/test` com `"environment": "Production"`, numa instância de
  `Sandbox`
- **THEN** o teste roda contra a credencial gravada da instância, e o campo é ignorado

#### Scenario: Adapter sem teste
- **WHEN** o adapter de entrada gravado do tenant-b é o `iScala`, e o Admin testa a entrada
- **THEN** a resposta é 400, dizendo que o `iScala` não tem teste de credencial

#### Scenario: Viewer não testa
- **WHEN** um Viewer do tenant-a chama `POST /connector/test`
- **THEN** a resposta é 403, e nenhuma requisição vai à plataforma nem ao ERP

### Requirement: O teste da Avalara pega o token

O teste da Avalara MUST trocar um token novo no endpoint de token da **conta de plataforma**, com a credencial gravada
nela. A troca MUST NOT usar o token em cache, pelo mesmo motivo do D365: o teste responde se a credencial
funciona agora. O teste para no token.

- **Os desfechos,** com o detalhe que vai para o log:
  - **o token saiu:** funcionou;
  - **o endpoint recusa a credencial (400 ou 401):** não funcionou, e o detalhe pede para conferir o Client ID e o Client
    Secret da conta;
  - **um endereço errado (404, 403, 405, ou o host que não existe):** não funcionou, e o detalhe pede para conferir o
    endpoint de token da conta. O teste não usa a URL base de nenhum conector;
  - **tempo esgotado, 408, 429, 5xx, ou a conexão recusada:** indisponível;
  - **a resposta vem sem token:** não funcionou, e o detalhe diz isso.
- **O ambiente:** é o da instância, e não uma escolha do teste. Uma credencial de `Sandbox` e uma de `Production` são
  duas credenciais, em duas instâncias, cada uma com o próprio teste.

#### Scenario: Token no Sandbox
- **WHEN** o Admin do tenant-a testa a conta de plataforma numa instância de `Sandbox`, e o endpoint de token emite o
  token
- **THEN** a resposta diz que funcionou

#### Scenario: Credencial recusada
- **WHEN** o endpoint de token responde 400 ao teste, numa instância de `Production`
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log pede para conferir o Client ID e o Client Secret
  da conta

#### Scenario: Endpoint de token errado
- **WHEN** o endpoint de token da conta de plataforma responde 404 ao teste
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log pede para conferir o endpoint de token da conta

### Requirement: O freio fica no endpoint de teste

O endpoint de teste MUST lembrar, por 5 minutos, um teste recusado, por tenant e por lado: o adapter de entrada, ou a
conta de plataforma. O ambiente não entra na chave: a instância atende um só, e o `Sandbox` e o `Production` são
credenciais de instâncias diferentes. Vale igual para a Avalara e para o D365.

O freio tem dois motivos:

- o botão é o caminho sem limite: o coletor tenta uma vez por intervalo, e uma pessoa clica quantas vezes quiser;
- cada teste é um pedido real ao emissor do token, ao Entra ID ou ao endpoint de token da Avalara, porque nenhum teste
  reusa token em cache.

- **O que é recusado:** a outra ponta respondeu que não. É a recusa da credencial, do token ou da permissão, ou a
  entidade inexistente.
- **O que não entra no freio:**
  - a indisponibilidade (tempo esgotado, 5xx, 429);
  - a credencial incompleta, que não chega a fazer requisição;
  - o sucesso.
- **Dentro do intervalo:** um novo teste da mesma chave MUST responder que não funcionou, com a mesma mensagem e o
  instante em que um novo teste é aceito, sem requisição à plataforma nem ao ERP. O log registra o motivo lembrado e que a
  resposta veio do freio.
- **Salvar o perfil esquece o freio** do tenant na hora, mesmo sem mudar nada, porque a correção pode ter sido feita do
  outro lado.
- **O isolamento:** o freio de um tenant MUST NOT afetar outro tenant, e o de um lado MUST NOT afetar o outro.

#### Scenario: Segundo clique dentro do intervalo
- **WHEN** o teste do D365 do tenant-a foi recusado às 10:00, e o Admin testa de novo às 10:02
- **THEN** a resposta diz "Credenciais ou ambiente inválidos" e que um novo teste é aceito às 10:05
- **AND** o log registra o motivo das 10:00, vindo do freio
- **AND** nenhuma requisição vai ao Entra ID nem ao F&O

#### Scenario: Depois do intervalo
- **WHEN** o teste do D365 do tenant-a foi recusado às 10:00, e o Admin testa de novo às 10:06
- **THEN** o teste vai ao Entra ID e ao F&O de novo

#### Scenario: Salvar esquece o freio
- **WHEN** o teste da conta `Avalara` do tenant-a foi recusado, e o Admin corrige o Client Secret e salva dentro do
  intervalo
- **THEN** o teste seguinte vai ao endpoint de token na hora

#### Scenario: Indisponível não trava
- **WHEN** o teste do D365 do tenant-a teve tempo esgotado, e o Admin testa de novo logo depois
- **THEN** o teste vai ao Entra ID e ao F&O de novo

#### Scenario: Outro adapter e outro tenant
- **WHEN** o teste do D365 do tenant-a está no freio
- **THEN** o teste da conta `Avalara` do tenant-a vai ao endpoint de token
- **AND** o teste do D365 do tenant-c vai ao Entra ID

#### Scenario: A chave do freio não tem ambiente
- **WHEN** o teste da conta `Avalara` do tenant-a foi recusado, e um dashboard antigo testa de novo dentro do intervalo
  mandando `"environment": "Production"`
- **THEN** a resposta vem do freio, com o motivo lembrado
- **AND** nenhuma requisição vai ao endpoint de token
