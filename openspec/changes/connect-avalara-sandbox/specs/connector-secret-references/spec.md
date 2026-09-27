## Purpose

Deixar o Admin do cliente configurar os segredos dos adapters pela tela de conectores, sem nunca guardar o valor
em claro. Vale para credencial de ERP, de plataforma de compliance e de chamados. A tela é a porta de entrada e o
cofre de segredos é o destino:

- o perfil do tenant guarda só a referência;
- a leitura do perfil nunca devolve o valor;
- o valor não fica no banco, no disco nem no repositório;
- a falta dele é falha alta, que diz o que falta e onde configurar.

## ADDED Requirements

### Requirement: O segredo entra pela tela e vai para o cofre

A gravação do perfil de conector MUST aceitar o valor de um segredo como **campo de escrita**, nas settings de
entrada, de saída e de chamados. É campo de escrita todo campo cujo nome designa um segredo: `clientSecret`,
`secret`, `password`, `senha`, `apiKey`, `token` ou `accessToken`, em qualquer nível do JSON. O nome é comparado
sem distinguir maiúscula e sem `_` ou `-`. Para cada campo de escrita com valor não vazio, o servidor MUST:

1. gravar o valor no cofre de segredos, sob o nome que o próprio servidor deriva (ver "A referência é do
   servidor");
2. tirar o valor das settings e pôr no lugar o campo `<campo>Ref`, com a referência `kv:<nome>`.

Um campo de escrita ausente mantém o segredo que já estava configurado naquele caminho. A lista de nomes vale para
o que é **gravado**, e não para o que é recebido. Recebido, o valor é o caminho certo. Gravado no perfil, ele
nunca aparece.

A validação das settings acontece antes de qualquer escrita. Settings que não são JSON válido, ou que falham em
qualquer regra desta capacidade, são recusadas com HTTP 400, e nada é gravado, nem no cofre nem no perfil.

Nenhuma mensagem de resposta, de log ou de erro MUST repetir o valor recebido.

#### Scenario: Admin grava o Client Secret pela tela
- **WHEN** um Admin do tenant-a grava as settings de saída com `"sandbox": {"clientId": "abc", "clientSecret": "s3cr3t"}`
- **THEN** o cofre passa a ter o valor `s3cr3t` sob o nome derivado do tenant-a, da saída, do `sandbox` e do
  `clientSecret`
- **AND** as settings gravadas no perfil trazem `"clientSecretRef": "kv:<nome derivado>"` e nenhum campo
  `clientSecret`
- **AND** nem o banco nem a resposta contêm `s3cr3t`

#### Scenario: Campo de escrita ausente mantém o segredo
- **WHEN** o tenant-a já tem o Client Secret configurado no `sandbox`, e um Admin grava as settings de novo sem o
  campo `clientSecret`
- **THEN** o perfil gravado mantém a referência, e o cofre não é tocado

#### Scenario: Settings inválidas não gravam nada
- **WHEN** um Admin grava settings de saída que não são JSON válido, e com um `clientSecret` dentro
- **THEN** a gravação responde 400
- **AND** nem o cofre nem o perfil mudam, e a resposta não contém o valor

#### Scenario: Falha do cofre
- **WHEN** o cofre recusa ou não responde à gravação do segredo
- **THEN** a gravação do perfil falha com uma mensagem que diz que o cofre não aceitou o segredo, sem o valor
- **AND** o perfil gravado continua o anterior

### Requirement: A referência é do servidor

O nome do segredo no cofre MUST ser derivado pelo servidor a partir de quatro coisas:

- o tenant do usuário logado;
- o tipo de settings (entrada, saída, chamados);
- o caminho no JSON;
- o campo.

O nome MUST usar só o que o cofre aceita: letras ASCII, dígitos e hífen, com até 127 caracteres. Um exemplo é
`fh-tenant-a-outbound-sandbox-clientsecret`. Todo nome do tenant começa pelo prefixo dele.

- **Na gravação:** um campo `*Ref` vindo da requisição MUST ser recusado com HTTP 400. A referência não é dado do
  cliente, e aceitá-la deixaria um tenant apontar para o segredo de outro.
- **Na leitura:** o adapter MUST recusar uma referência fora do prefixo do tenant do perfil, como configuração do
  conector, sem ler o cofre.

#### Scenario: Referência de outro tenant na requisição
- **WHEN** um Admin do tenant-b grava `"sandbox": {"clientSecretRef": "kv:fh-tenant-a-outbound-sandbox-clientsecret"}`
- **THEN** a gravação responde 400, e o perfil do tenant-b não muda

#### Scenario: Referência de outro tenant gravada direto no banco
- **WHEN** as settings do tenant-b, gravadas por SQL, apontam para `kv:fh-tenant-a-outbound-sandbox-clientsecret`, e a
  esteira envia uma nota do tenant-b
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector
- **AND** o cofre não é lido, e nenhuma requisição sai com a credencial do tenant-a

### Requirement: A leitura do perfil nunca devolve o valor

A leitura do perfil de conector (`GET /connector`) MUST NOT devolver o valor de nenhum segredo nem a referência. Para
cada segredo, ela devolve se está configurado e quando foi gravado pela última vez, sem ler o valor no cofre.
Um segredo cuja referência existe no perfil, mas que o cofre não tem, aparece como não configurado.

#### Scenario: Leitura depois da gravação
- **WHEN** um Admin grava o Client Secret do `sandbox` e depois abre a tela de conectores
- **THEN** a resposta da leitura diz que o Client Secret do `sandbox` está configurado, com a data da gravação
- **AND** a resposta não contém o valor, nem parte dele, nem a referência

#### Scenario: Cofre sem o valor
- **WHEN** o perfil tem a referência do Client Secret do `sandbox`, mas o cofre não tem o segredo
- **THEN** a leitura diz que o Client Secret do `sandbox` não está configurado

### Requirement: Nada em claro no perfil, no banco ou no disco

As settings persistidas MUST carregar um segredo só como referência `kv:<nome>`. Um campo de escrita nunca é
persistido.

- **Leitura do adapter:** se as settings trouxerem um campo de escrita com valor, ou uma referência malformada,
  mesmo que tenham chegado ao banco por outro caminho (SQL direto, seed), o adapter MUST recusá-las como
  configuração do conector. A recusa vem antes de qualquer requisição, e o motivo não contém o valor.
- **Cofre de desenvolvimento:** MUST NOT gravar segredo em claro em disco.

#### Scenario: Settings gravadas direto no banco com segredo em claro
- **WHEN** as settings de saída do tenant, gravadas por SQL, trazem `clientSecret` em claro na seção do ambiente
  ativo, e a esteira envia uma nota
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que cita o campo
- **AND** nenhuma requisição é feita à plataforma, e o motivo não contém o valor

#### Scenario: Referência malformada no banco
- **WHEN** as settings persistidas trazem `"clientSecretRef": "s3cr3t"`
- **THEN** o adapter recusa as settings como configuração do conector, sem repetir o valor

### Requirement: O mesmo cofre e o mesmo caminho em todos os ambientes

O caminho do segredo MUST ser o mesmo em desenvolvimento e em produção: da tela para a gravação do perfil, dela
para o cofre, e do cofre para o uso no envio.

- **Em produção:** o cofre é o Key Vault do ambiente.
- **Em desenvolvimento:** é um emulador da mesma API, acessado pelo mesmo cliente e pelo mesmo adapter, que
  guarda os segredos só em memória.
- **O que muda entre os dois:** só o endereço do cofre, a credencial e o certificado do emulador. Esses três
  valores de desenvolvimento MUST ser recusados fora do endereço de loopback.
- **A verificação do desafio de autenticação:** é a conferência de que o recurso do desafio bate com o domínio
  do cofre. Ela MUST ficar ligada para qualquer cofre fora do loopback. Só é desligada quando o endereço do
  cofre é de loopback, e essa escolha é tirada do próprio endereço. Nenhuma configuração a desliga.

Não existe modo em que as settings aceitem o valor cru persistido, em nenhum ambiente.

#### Scenario: Ida e volta em desenvolvimento
- **WHEN** em desenvolvimento, um Admin grava o Client Secret pela tela, e a esteira envia uma nota
- **THEN** o segredo usado no pedido de token é o gravado, lido do emulador pelo mesmo adapter de produção

#### Scenario: Emulador reiniciado
- **WHEN** o emulador do cofre reinicia e perde os segredos da memória
- **THEN** a leitura do perfil mostra o segredo como não configurado
- **AND** o envio seguinte falha alto, apontando para a tela, e não segue sem credencial

#### Scenario: Configuração de emulador fora do loopback
- **WHEN** a configuração do host aponta a credencial do emulador para um cofre fora do loopback
- **THEN** o host recusa a configuração na subida

#### Scenario: Cofre de produção com a verificação ligada
- **WHEN** o host é configurado com o cofre `https://<cofre>.vault.azure.net/`
- **THEN** o cliente do cofre é montado com a verificação do desafio de autenticação ligada

#### Scenario: Emulador com a verificação desligada
- **WHEN** o host é configurado com o cofre `https://localhost:8443/`
- **THEN** o cliente do cofre é montado com a verificação do desafio desligada, e só nesse caso

### Requirement: A identidade do host só alcança segredos de conector

Em produção, a permissão da identidade do host no cofre MUST se limitar a ler, gravar e ler os metadados de
segredos cujo nome começa por `fh-`. É requisito de provisionamento de cada ambiente de cliente, e tem três
partes:

- um papel com só essas três ações;
- uma condição de acesso pelo nome, com o prefixo `fh-`;
- um cofre dedicado aos segredos de conector, como defesa em profundidade.

O host MUST recusar, antes de chamar o cofre, a leitura ou gravação de um nome fora do prefixo `fh-`.

#### Scenario: Gravação fora do prefixo, em staging
- **WHEN** a identidade do host tenta gravar ou ler, no cofre de staging, um segredo chamado `jwt-signing-key`
- **THEN** o cofre recusa a operação por falta de permissão

#### Scenario: Segredo de conector, em staging
- **WHEN** a identidade do host grava e lê `fh-tenant-a-outbound-sandbox-clientsecret` no cofre de staging
- **THEN** as duas operações funcionam

#### Scenario: Nome fora do prefixo no código
- **WHEN** um defeito do nosso código pede ao adaptador do cofre um nome que não começa por `fh-`
- **THEN** o adaptador recusa sem chamar o cofre

### Requirement: Segredo ausente falha alto e aponta para a tela

Quando o segredo não está configurado, a operação que precisa dele MUST falhar antes de qualquer requisição que
levaria a credencial. Isso vale para duas situações: a referência não existe no perfil, ou o cofre não tem o
valor. A mensagem MUST nomear:

- o campo, com o caminho das settings;
- o tenant e o ambiente;
- onde configurar: a tela de conectores, com o adapter, o ambiente e o campo.

A operação MUST NOT seguir sem a credencial. A mensagem MUST NOT conter valor de segredo, nem trazer comando de
linha de comando. Um valor vazio conta como ausente.

#### Scenario: Segredo não configurado no envio
- **WHEN** a esteira envia uma nota do tenant-a no ambiente sandbox, e o Client Secret do sandbox não está
  configurado
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que cita
  `OutboundSettings.sandbox.clientSecret`, `tenant-a`, `sandbox` e "Configurações → Conectores → Avalara → Sandbox
  → Client Secret"
- **AND** nenhuma requisição é feita ao endpoint de token nem à plataforma

#### Scenario: Referência existe, cofre não tem o valor
- **WHEN** o perfil tem a referência, mas o cofre não tem o segredo
- **THEN** o resultado é o mesmo do segredo não configurado, e o motivo diz que o cofre não tem o valor

### Requirement: Nada no repositório

O perfil de conector semeado para desenvolvimento MUST carregar só referências no prefixo do próprio tenant, e
nenhum campo de escrita. Um teste MUST provar que os perfis semeados passam pela mesma regra da leitura do
adapter. O repositório MUST NOT conter arquivo de persistência ou de exportação do emulador do cofre.

#### Scenario: Seed de desenvolvimento
- **WHEN** os perfis semeados para desenvolvimento passam pela regra de leitura das settings
- **THEN** nenhum é recusado, e toda referência está no prefixo do tenant do perfil
