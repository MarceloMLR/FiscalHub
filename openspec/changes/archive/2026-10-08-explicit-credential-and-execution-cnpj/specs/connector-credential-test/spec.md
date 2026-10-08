## MODIFIED Requirements

### Requirement: O teste do D365 pega o token e lê

O teste do D365 MUST pegar o token pelo client credentials do tenant e, com ele, fazer uma leitura mínima: o primeiro
registro da `FSFiscalDocumentBRs`, com `$top=1`, entre empresas, só com a chave. O token prova a credencial, e não a
permissão. Quem prova a permissão é a leitura.

- **A identidade:** o teste MUST usar a credencial gravada no perfil do tenant, e nenhuma outra, em qualquer ambiente do
  host. O teste responde sobre a credencial gravada, e não sobre quem roda o host: uma identidade da máquina com acesso
  ao F&O, como uma sessão do Azure CLI, não muda a resposta.
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
- **WHEN** o host roda em Development, quem roda o host tem uma identidade com acesso ao F&O, como uma sessão do Azure
  CLI, e o perfil do tenant-a não tem o Client Secret
- **THEN** a resposta diz "Credenciais ou ambiente inválidos", e o log diz que o Client Secret não está configurado
- **AND** nenhum token é pedido, nem ao Entra ID nem a outra fonte, e nenhuma leitura vai ao F&O
