# connector-credential-test Specification

## Purpose

Deixar o Admin testar pela tela a credencial gravada do ERP (D365) e da plataforma de compliance (Avalara), com uma
mensagem curta na tela e o motivo detalhado no log, sem expor token nem segredo, e sem virar um caminho de sondagem sem
limite.

## Requirements

### Requirement: O teste usa a credencial gravada

O teste MUST usar a credencial gravada no perfil do tenant do usuário logado, com o segredo lido do cofre no servidor. A
API é `POST /connector/test`, só para Admin.

- **A requisição:** diz só o lado, entrada ou saída, e, na saída, o ambiente (`Sandbox` ou `Production`). Ela MUST NOT
  levar credencial, e o servidor não usa nenhum outro campo que venha nela.
- **Quem tem teste:** o `Dynamics365`, na entrada, e a `Avalara`, na saída. Para outro adapter, a resposta é 400, e diz
  que ele não tem teste de credencial.
- **Credencial incompleta:** falta um campo da credencial, o segredo não está configurado, ou o cofre não tem o valor.
  Nesses casos, a resposta MUST dizer que não funcionou, e o log nomeia o que falta, sem nenhuma requisição à plataforma
  nem ao ERP.
- **A tela:** com uma edição pendente no formulário de Configurações, o botão MUST pedir para salvar antes, e não chama
  o teste. O teste usa a credencial gravada, e testar a da tela daria uma resposta sobre outra credencial.

#### Scenario: Testar a credencial do ERP
- **WHEN** o Admin do tenant-a, sem edição pendente, clica em "Testar credencial" na aba do ERP
- **THEN** o servidor testa o Client ID, o tenant do Entra e o Client Secret gravados para o tenant-a

#### Scenario: Edição pendente
- **WHEN** o Admin trocou o Client ID na tela e ainda não salvou, e clica em "Testar credencial"
- **THEN** a tela pede para salvar antes de testar
- **AND** nenhuma requisição de teste é enviada

#### Scenario: Segredo não configurado
- **WHEN** o perfil do tenant-a não tem o Client Secret do `Sandbox`, e o Admin testa a saída no `Sandbox`
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log diz que o Client Secret do `Sandbox` não está
  configurado
- **AND** nenhuma requisição vai ao endpoint de token

#### Scenario: Adapter sem teste
- **WHEN** o adapter de entrada gravado do tenant-b é o `iScala`, e o Admin testa a entrada
- **THEN** a resposta é 400, dizendo que o `iScala` não tem teste de credencial

#### Scenario: Viewer não testa
- **WHEN** um Viewer do tenant-a chama `POST /connector/test`
- **THEN** a resposta é 403, e nenhuma requisição vai à plataforma nem ao ERP

### Requirement: A resposta é curta, e o detalhe vai para o log

A resposta do teste MUST trazer só:

- se funcionou;
- a mensagem da tela;
- quando o freio está valendo, o instante em que um novo teste é aceito.

A mensagem MUST ser uma destas, igual para qualquer adapter:

| Desfecho | Mensagem |
|---|---|
| funcionou | "Credenciais e conexão válidas" |
| recusa da credencial, do token ou da permissão, entidade inexistente, ou credencial incompleta | "Credenciais ou ambiente inválidos" |
| tempo esgotado, 5xx ou 429 | "Não foi possível conectar agora. Tente novamente em instantes" |

A indisponibilidade tem mensagem própria de propósito: dizer "inválidos" a uma plataforma fora do ar levaria o Admin a
trocar um segredo certo.

**O detalhe do motivo** vai para uma linha de log do host, e MUST NOT ir para a resposta. Exemplos: o código `AADSTS`, o
status HTTP, o campo que falta. A linha leva o tenant, o adapter, o ambiente, o veredito e se a resposta veio do freio.

A resposta MUST NOT conter:

- o token, nem parte dele;
- o segredo, nem parte dele;
- um cabeçalho com valor, como `Authorization` ou `Cookie`.

Nenhuma linha de log do teste MUST conter o token ou o segredo.

#### Scenario: Resposta de um teste que funcionou
- **WHEN** o teste da Avalara do tenant-a recebe o token `eyJ...abc`, com o segredo `s3cr3t`
- **THEN** a resposta diz "Credenciais e conexão válidas"
- **AND** nem a resposta nem o log contêm `eyJ...abc`, `s3cr3t`, `Bearer` ou `Authorization`

#### Scenario: Resposta de um teste recusado
- **WHEN** o endpoint de token recusa o segredo `s3cr3t`, e o corpo da recusa repete o segredo
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e nem ela nem o log contêm `s3cr3t`

#### Scenario: Plataforma fora do ar
- **WHEN** o endpoint de token responde 503 ao teste
- **THEN** a resposta diz "Não foi possível conectar agora. Tente novamente em instantes", e não "inválidos"

### Requirement: O teste do D365 pega o token e lê

O teste do D365 MUST pegar o token pelo client credentials do tenant e, com ele, fazer uma leitura mínima: o primeiro
registro da `FSFiscalDocumentBRs`, com `$top=1`, entre empresas, só com a chave. O token prova a credencial, e não a
permissão. Quem prova a permissão é a leitura.

- **A identidade:** o teste MUST usar a credencial do tenant, e nunca o Azure CLI, nem em desenvolvimento. O teste
  responde sobre a credencial gravada, e não sobre outra identidade.
- **O token novo:** cada teste MUST pedir um token novo ao Entra ID, sem reusar nenhum token em cache, nem o do coletor.
  O teste responde se a credencial funciona **agora**. Um teste que passasse com um segredo já revogado no Entra ID,
  porque reusou um token emitido antes, mentiria para quem está tentando decidir se o problema é a credencial.
- **O coletor não muda:** o coletor MUST continuar com o cache de token dele. O teste não lê, não troca e não apaga esse
  cache.
- **Os desfechos:**

  | O que acontece | Funcionou? | O detalhe no log diz |
  |---|---|---|
  | a leitura devolve um registro | sim | o token saiu e a entidade respondeu |
  | a leitura devolve vazio | sim | a entidade respondeu sem nota. Entre empresas, isso não prova o acesso às empresas |
  | o Entra ID recusa a credencial, ou falha sem causa de rede | não | o Entra ID recusou a credencial, com o código `AADSTS` quando há, procurado em toda a cadeia de exceções |
  | o tenant do Entra ou o Client ID em formato inválido | não | o formato não é válido |
  | o host do F&O não existe | não | o endereço do ambiente não existe |
  | o F&O responde 401 | não | o F&O não aceitou o token. Conferir o cadastro do app no F&O |
  | o F&O responde 403 | não | o app não tem o privilégio de leitura das entidades `FS` |
  | o F&O responde 404 | não | a entidade não existe no ambiente, e o pacote `FS` não está implantado |
  | tempo esgotado, 5xx, 429, ou uma causa de rede na cadeia | não | a origem está indisponível agora, e o teste pode ser repetido |

  Uma credencial errada MUST NOT ser lida como indisponibilidade. Só uma causa de rede o é: conexão recusada ou caída,
  tempo esgotado.

#### Scenario: Credencial e permissão certas
- **WHEN** o Entra ID emite o token do tenant-a, e a leitura da `FSFiscalDocumentBRs` devolve um registro
- **THEN** a resposta diz que funcionou

#### Scenario: Token sem permissão
- **WHEN** o Entra ID emite o token, e a leitura responde 403
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log diz que o app não tem o privilégio de leitura das
  entidades `FS`

#### Scenario: Segredo errado
- **WHEN** o Entra ID recusa a credencial com `AADSTS7000215`
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log diz que o Entra ID recusou a credencial
  (`AADSTS7000215`)
- **AND** nenhuma leitura vai ao F&O

#### Scenario: Leitura vazia
- **WHEN** a leitura responde 200 sem registro
- **THEN** a resposta diz "Credenciais e conexão válidas", e o log avisa que isso não prova o acesso às empresas

#### Scenario: Segredo revogado, com token do coletor em cache
- **WHEN** o coletor do tenant-a pegou um token às 10:00 com o segredo S, o segredo S foi revogado no Entra ID às 10:10,
  e o Admin testa a credencial às 10:20
- **THEN** o teste pede um token novo ao Entra ID, que recusa, e a resposta diz que não funcionou
- **AND** o cache de token do coletor continua como estava

#### Scenario: Dois testes, dois pedidos
- **WHEN** o Admin testa a credencial do D365 do tenant-a duas vezes seguidas, e as duas funcionam
- **THEN** cada teste faz o próprio pedido de token ao Entra ID

#### Scenario: Em desenvolvimento, sem o segredo
- **WHEN** o host roda em Development, o `az login` está ativo, e o perfil do tenant-a não tem o Client Secret
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log diz que o Client Secret não está configurado
- **AND** o teste não usa a sessão do Azure CLI

### Requirement: O teste da Avalara pega o token

O teste da Avalara MUST trocar um token novo no endpoint de token do ambiente escolhido, com a credencial gravada
daquela seção. A troca MUST NOT usar o token em cache, pelo mesmo motivo do D365: o teste responde se a credencial
funciona agora. O teste para no token.

- **Os desfechos,** com o detalhe que vai para o log:
  - **o token saiu:** funcionou;
  - **o endpoint recusa a credencial (400 ou 401):** não funcionou, e o detalhe pede para conferir o Client ID e o Client
    Secret do ambiente;
  - **um endereço errado (404, 403, 405, ou o host que não existe):** não funcionou, e o detalhe pede para conferir a URL
    base;
  - **tempo esgotado, 408, 429, 5xx, ou a conexão recusada:** indisponível;
  - **a resposta vem sem token:** não funcionou, e o detalhe diz isso.
- **O ambiente:** o `Sandbox` e o `Production` são duas credenciais, e cada um tem o próprio teste.

#### Scenario: Token no Sandbox
- **WHEN** o Admin do tenant-a testa a saída no `Sandbox`, e o endpoint de token do `Sandbox` emite o token
- **THEN** a resposta diz que funcionou

#### Scenario: Credencial recusada
- **WHEN** o endpoint de token do `Production` responde 400 ao teste
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log pede para conferir o Client ID e o Client Secret de
  Produção

### Requirement: O freio fica no endpoint de teste

O endpoint de teste MUST lembrar, por 5 minutos, um teste recusado, por tenant e por adapter. Na Avalara, a chave inclui
o ambiente, porque o `Sandbox` e o `Production` são credenciais diferentes. Vale igual para a Avalara e para o D365.

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
- **O isolamento:** o freio de um tenant MUST NOT afetar outro tenant, e o de um adapter MUST NOT afetar o outro.

#### Scenario: Segundo clique dentro do intervalo
- **WHEN** o teste do D365 do tenant-a foi recusado às 10:00, e o Admin testa de novo às 10:02
- **THEN** a resposta diz "Credenciais ou ambiente inválidos" e que um novo teste é aceito às 10:05
- **AND** o log registra o motivo das 10:00, vindo do freio
- **AND** nenhuma requisição vai ao Entra ID nem ao F&O

#### Scenario: Depois do intervalo
- **WHEN** o teste do D365 do tenant-a foi recusado às 10:00, e o Admin testa de novo às 10:06
- **THEN** o teste vai ao Entra ID e ao F&O de novo

#### Scenario: Salvar esquece o freio
- **WHEN** o teste da Avalara do tenant-a no `Sandbox` foi recusado, e o Admin corrige o Client Secret e salva dentro do
  intervalo
- **THEN** o teste seguinte vai ao endpoint de token na hora

#### Scenario: Indisponível não trava
- **WHEN** o teste do D365 do tenant-a teve tempo esgotado, e o Admin testa de novo logo depois
- **THEN** o teste vai ao Entra ID e ao F&O de novo

#### Scenario: Outro adapter e outro tenant
- **WHEN** o teste do D365 do tenant-a está no freio
- **THEN** o teste da Avalara do tenant-a vai ao endpoint de token
- **AND** o teste do D365 do tenant-c vai ao Entra ID
