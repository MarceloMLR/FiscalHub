## REMOVED Requirements

### Requirement: Autenticação no F&O

**Reason**: Em Development, o host autenticava pela sessão do Azure CLI de quem o roda quando o perfil não tinha
credencial utilizável, e a integração funcionava em silêncio com uma identidade que não é a do tenant. Era falso
positivo: quem roda concluía que a credencial estava configurada. O requisito permitia esse modo, e os cenários "Azure CLI
em desenvolvimento" e "Azure CLI fora de desenvolvimento" o descreviam.

**Migration**: Substituído por "Autenticação no F&O pela credencial do perfil", que mantém o client credentials, o
segredo por referência, o reaproveitamento do token e o detalhe dentro do adapter, sem a exceção de Development. Quem roda
local preenche o Tenant do Entra ID, o Client ID e o Client Secret em Configurações → Conectores → Entrada.

## ADDED Requirements

### Requirement: Autenticação no F&O pela credencial do perfil

O feed MUST autenticar cada requisição com um bearer token do Entra ID para o recurso do ambiente F&O do
tenant (escopo `<url>/.default`). O token MUST vir do fluxo client credentials, com tenant do Entra, client id e
referência ao segredo nas settings do tenant, em qualquer ambiente do host, Development inclusive. A credencial do perfil
MUST ser a única identidade do conector contra o F&O: nem a sessão do Azure CLI de quem roda o host, nem outra credencial
da máquina, autentica no lugar dela. O segredo em si MUST NOT ficar nas settings: só a referência (`kv:<nome>`),
resolvida fora do banco. O token MUST ser reaproveitado enquanto válido. O detalhe de autenticação MUST NOT vazar do
adapter.

- **Sem credencial utilizável:** a leitura MUST falhar com erro de configuração, antes de pedir token ao Entra ID e sem
  chamar o F&O. O motivo MUST dizer qual destes três casos é, e os três MUST ter textos distintos:
  - **o perfil não tem a credencial:** as settings não têm `auth`;
  - **a credencial está incompleta:** o motivo nomeia cada campo que falta, pelo nome da tela (Tenant do Entra ID,
    Client ID, Client Secret), e só os que faltam;
  - **o segredo não está no cofre:** a referência existe, e o cofre não tem o segredo.

  Os três dizem onde corrigir: Configurações → Conectores → Entrada. Nenhum traz o segredo. A referência que aponta para
  fora do prefixo do tenant continua sendo o erro de configuração que é hoje, com o motivo dela.
- **O caminho do erro:** é o de qualquer erro de configuração. No coletor, vira a falha registrada do tenant, que o
  painel da integração automática mostra como último erro, e a marca não avança. Na integração manual, na agendada e no
  diretório de empresas, vira o motivo da recusa.
- **A identidade no log:** o adapter MUST registrar com que identidade o tenant autentica no F&O: o client id do app e o
  tenant do Entra. A linha MUST sair uma vez por tenant, e de novo só quando a identidade dele muda. Ela MUST NOT sair a
  cada token, e MUST NOT conter o segredo. Trocar o segredo e manter o app não é trocar de identidade. Sem credencial
  utilizável, nenhuma linha de identidade sai.

#### Scenario: Client credentials
- **WHEN** o tenant está configurado com tenant do Entra, client id e `clientSecretRef = kv:d365-a-secret`
- **THEN** o feed obtém o token por client credentials usando o segredo resolvido da referência e o
  envia como `Authorization: Bearer`

#### Scenario: Segredo em claro é recusado
- **WHEN** as settings trazem o segredo em claro, sem o prefixo `kv:`
- **THEN** a leitura falha com erro de configuração, sem chamar o F&O

#### Scenario: Em Development, o perfil sem credencial
- **WHEN** o host roda em Development, quem roda o host tem uma sessão do Azure CLI com acesso ao F&O, e o perfil do
  tenant-a não tem `auth`
- **THEN** a leitura falha com erro de configuração que diz que o perfil não tem a credencial
- **AND** nenhum token é pedido, nem ao Entra ID nem ao Azure CLI, e nenhuma requisição sai ao F&O

#### Scenario: A credencial incompleta nomeia o que falta
- **WHEN** o `auth` do tenant-a tem a referência do segredo, e o tenant do Entra e o client id estão vazios
- **THEN** a leitura falha com erro de configuração que nomeia o Tenant do Entra ID e o Client ID
- **AND** o motivo não cita o Client Secret

#### Scenario: O segredo ausente do cofre
- **WHEN** o `auth` do tenant-a está completo, com a referência `kv:fh-tenant-a--inbound--auth--clientsecret`, e o cofre
  não tem esse segredo
- **THEN** a leitura falha com erro de configuração que diz que o Client Secret não está no cofre
- **AND** o motivo é diferente do motivo do perfil sem `auth` e do motivo da credencial incompleta

#### Scenario: A falha aparece no painel da integração automática
- **WHEN** a integração automática do tenant-a está ligada, e o perfil dele não tem `auth`
- **THEN** a passada registra a falha do tenant-a com o motivo do perfil sem credencial
- **AND** o painel mostra esse motivo como último erro, e a marca não avança

#### Scenario: A identidade sai uma vez por tenant
- **WHEN** o tenant-a autentica três vezes seguidas com o app `app-a` do tenant do Entra `entra-a`
- **THEN** o log tem uma linha de identidade do tenant-a, com `app-a` e `entra-a`, e sem o segredo

#### Scenario: A identidade muda
- **WHEN** o tenant-a já autenticou com o app `app-a`, e o Admin troca o Client ID para `app-b`
- **THEN** sai uma linha nova de identidade do tenant-a, com `app-b`

#### Scenario: O segredo trocado não é identidade nova
- **WHEN** o tenant-a já autenticou com o app `app-a`, e o Admin grava um Client Secret novo para o mesmo app
- **THEN** nenhuma linha nova de identidade sai

## MODIFIED Requirements

### Requirement: Teste contra o ambiente real opt-in

Um teste de integração MUST rodar o feed contra um ambiente F&O real, com a mesma autenticação do conector: client
credentials, com o tenant do Entra, o client id e o client secret dados por variáveis de ambiente. Ele MUST NOT usar outra
identidade, como a sessão do Azure CLI. O teste MUST ser pulado a menos que a variável do ambiente e as três da
credencial estejam definidas, de modo que `dotnet test` em qualquer máquina sem elas (CI inclusive) fique verde sem tocar
a rede. O motivo do skip MUST nomear cada variável que falta.

#### Scenario: Sem a variável de ambiente
- **WHEN** `dotnet test` roda numa máquina sem a variável do ambiente F&O
- **THEN** o teste de integração aparece como pulado, e nenhuma chamada de rede é feita

#### Scenario: O ambiente sem o segredo
- **WHEN** a variável do ambiente F&O está definida, e a do client secret não
- **THEN** o teste de integração aparece como pulado, e o motivo nomeia a variável do client secret, e só ela
- **AND** nenhuma chamada de rede é feita

#### Scenario: Contra o fiscosysdev
- **WHEN** a variável aponta `https://fiscosysdev.operations.dynamics.com`, a empresa é `brmf`, as variáveis da
  credencial trazem o app do conector, `pageSize = 20` e o instante pedido é 2015-01-01T00:00:00Z
- **THEN** o feed lê os 83 cabeçalhos da empresa `brmf` em 5 páginas por keyset, com 83
  `FiscalDocumentRecId` distintos (nenhum repetido, nenhum pulado)
- **AND** cada cabeçalho vira referência com NaturalKey `brmf|<Voucher>` ou aviso de modelo fora do mapa
