## Context

A motivação está no proposal.md. Os requisitos estão nas specs:

- `connector-secret-references`;
- `avalara-tenant-authentication`;
- `platform-response-trace`;
- `tenant-boundary`;
- o delta de `compliance-dispatch-outcome`.

Aqui fica o estado que molda o desenho.

**Código hoje.**

- **Registro no DI.** `AddAvalaraComplianceDispatcher` faz `TryAddSingleton<IAvalaraTokenProvider,
  NoOpAvalaraTokenProvider>`. O `AddAvalaraTokenProvider()` troca pelo provider real, mas o `Program.cs` só
  chama o primeiro, com `options.BaseUrl = cfg["Avalara:BaseUrl"]`.
- **Provider real.** O `AvalaraTokenProvider`:
  - faz client credentials por formulário (`grant_type`, `client_id` e `client_secret` no corpo);
  - guarda cache e trava por `tenantId`, com dupla verificação, e renova pela `TokenRenewalMargin` (5 min);
  - lê `ClientId` e `ClientSecret` de `AvalaraOptions`, que são globais e ninguém preenche;
  - usa um cliente nomeado cuja `BaseAddress` é a `BaseUrl` global.
- **Dispatcher.** O `AvalaraComplianceDispatcher`:
  - lê o perfil uma vez por envio, pelo `AvalaraOutboundSettings.Read`, que olha a seção do ambiente ativo;
  - resolve a URL por tenant em `BaseOf(settings)`, com fallback para a `BaseUrl` global;
  - põe o Bearer só quando o token não é vazio;
  - trata 400 e 422 como recusa, com `DispatchRejectedException`. O resto sai por `EnsureSuccessStatusCode`
    e vai para o retry nativo;
  - quando o 2xx vem sem `id`, lança `InvalidOperationException`, que também vai para o retry.
- **Segredo por referência.** O `SecretReference` é `internal` ao adapter D365:
  - valida o prefixo `kv:`;
  - resolve o nome pelo `IConfiguration`;
  - lança `ConnectorSettingsException` com "não encontrado na configuração (user-secrets/Key Vault)".

  O `D365InboundSettings` já recusa `auth.clientSecret` em claro. O Host não tem `UserSecretsId`. Sem ele, o
  `WebApplication.CreateBuilder` não carrega user-secrets nem em Development, e o comentário "no local vem
  de user-secrets" nunca foi verdade para o host.
- **Perfil de dev.** As `OutboundSettings` do seed trazem, por ambiente, `baseUrl`, `clientSecretRef`,
  `clientTokenRef` e `establishments`. A tela (`adapterSchemas.ts`) oferece `baseUrl`, `clientSecretRef` e
  `clientTokenRef`, e nenhum `clientId`. O `PUT /connector` grava as settings como vieram, sem nenhuma
  validação.
- **Trace.** O `IProcessingTrace` tem três fotos. O `BlobProcessingTrace` grava em
  `{tenant}/{aaaaMM}/{chave}/{arquivo}`: `source.<fmt>`, `domain.json` e `<destino>.json`.
  - **Leitura:** o `/trace` e o `/documents/{tenant}/{key}/download` listam tudo o que tem `/{chave}/` sob o
    prefixo do tenant, e o zip usa o último segmento do nome.
  - **Endpoints:** nenhum dos dois confere o tenant do usuário. Qualquer usuário autenticado baixa a fonte
    crua, o domínio e o payload de outro tenant, e com esta fatia também a resposta da plataforma. O D10
    corrige isso. O `/reprocess` já faz essa conferência.
  - **Falha:** o ADR-0006 registra que uma falha ao gravar foto interrompe o processamento.
- **Mock.** Não tem endpoint de token e não confere autorização.
- **Poll.** O `StatusPoller` captura exceção por documento e desiste em `MaxAttempts` (10), marcando
  `Unconfirmed`.
- **Dead-letter.** O motivo registrado é o do transporte (`MaxDeliveryCountExceeded`), e não o texto da
  exceção. Na dead-letter, o motivo da plataforma se perde.

**Premissas sobre a Avalara real**, que o repositório não prova:

- o fluxo de autenticação é o client credentials, como o provider já faz (o pedido afirma isso);
- o caminho de envio (`documents`), o de status (`documents/{id}/status`), o campo `id` do aceite e os valores
  `carregado`/`erro` vieram do mock.

*(Resultado em 2026-09-27: a autenticação e o caminho de envio foram verificados contra o sandbox. O caminho de status,
o `id` do aceite e os valores `carregado`/`erro` seguem sem verificar: nenhuma nota foi aceita. Ver a tabela abaixo e o
resultado no D13.)*

**Premissa de autenticação assumida (decisão de 2026-09-27).** A parte Avalara do desenho (D2, D6, D7, D11, D12)
assume três coisas:

- a autenticação é `client_credentials`, com o `client_secret` no corpo do pedido. *(Corrigido em 2026-09-27: o
  corpo é JSON, e não formulário, pela coleção do Postman do cliente. Ver a tabela abaixo.)*
- a resposta de token traz `access_token` e `expires_in`, este numérico e em segundos;
- o caminho de envio é `documents`.

A premissa **não é verificada antes do código**. Ela é confirmada no teste manual contra o sandbox, no fim da
fatia (tarefa 15.3), junto com o resto do ponta a ponta. Havia um portão antes do código, que foi retirado por
decisão explícita.

- **Se estiver errada:** o retrabalho fica nos grupos de autenticação, passando por `/opsx:update`. São eles o 8
  (credencial e URLs), o 9 (provider de token), o 10 (dispatcher) e o endpoint de token do mock, no 12.
- **O que não depende dela:** o cofre (6), a tela (7), a quarta foto (11) e a parte 1, que já está na `main`.

O que divergir na leitura da resposta do envio não é corrigido aqui (Non-goals). Esta fatia garante duas
coisas: a divergência fica visível e não vira reenvio.

**Resultado da verificação da premissa** (preenchido na tarefa 15.3):

| Item | Esperado pelo desenho | Observado |
|---|---|---|
| Método de autenticação do cliente | `client_secret` no corpo do formulário (post) | **O fluxo bateu, o formato não.** `client_credentials` com corpo JSON (`application/json`): `grant_type` fixo em `client_credentials`, `client_id`, `client_secret` e `disableTokenRefresh: true`. Tirado da coleção do Postman do cliente e **verificado contra o sandbox em 2026-09-27**. O `disableTokenRefresh` vem só da coleção, sem documentação: o sandbox o aceitou, e o efeito dele não foi verificado. Pela regra do passo 1 abaixo, é ajuste de forma: o provider (grupo 9) e o mock (grupo 12) passaram a JSON, com teste. |
| HTTP do pedido de token | 200 | 200, verificado contra o sandbox em 2026-09-27 |
| Campos da resposta de token | `access_token`, `token_type`, `expires_in` | Os três, verificados contra o sandbox em 2026-09-27, com `token_type` `bearer`. A resposta traz também `refresh_token`, `sessionId`, `userId`, `subId`, `appId` e `login` (com o nome da empresa). O provider não lê nenhum deles; a redação da troca de token os cobre (D9). |
| `expires_in` | número, em segundos, maior que a margem de 5 min | cerca de 86400 s (24 h), verificado contra o sandbox em 2026-09-27: bem acima da margem, e o token entra no cache |
| Escopo ou audiência exigidos | nenhum | nenhum: o fluxo não exige nem devolve escopo ou audiência. Verificado contra o sandbox em 2026-09-27 |
| Host do sandbox e do endpoint de token | — | `api-gateway.sandbox.avalarabrasil.com.br`, com o token em `/oauth/token` (o `TokenPath` padrão). Verificado contra o sandbox em 2026-09-27 |
| Recusa do endpoint de token | 400 ou 401, com o código do OAuth (`invalid_client`…) em `error` e o `error_description` | HTTP 400 com `{"error": "<texto livre>"}`, sem `error_description`. Verificado contra o sandbox em 2026-09-27. O `error` **não** é o código do OAuth: um `client_secret` errado volta como "client_id invalid". O `RefusalDetail` não muda: o caso só com `error` já cobre a resposta real |
| Primeiro envio ao caminho de envio | qualquer status diferente de 404 (o caminho existe) | **O caminho existe e está correto.** `taxcompliance/v2/fiscal/dfe`, montado pela `baseUrl` do perfil mais o `Avalara:DocumentsPath` do appsettings. As 5 NF-e 55 foram enviadas, e a plataforma respondeu com recusa de validação. Verificado contra o sandbox em 2026-09-27 |
| Tradução do estabelecimento | os códigos da empresa pelos `establishments` do perfil | Verificada contra o sandbox em 2026-09-27: o envio levou os códigos da tabela `establishments` do perfil, e a recusa não foi sobre eles |
| Consulta ao caminho de status | a resposta de status | **não verificado.** Nenhuma nota foi aceita, e sem `id` não houve consulta. O hub monta `{DocumentsPath}/{id}/status`, convenção que veio do mock. Se o caminho não existir, o sintoma é o documentado: a nota fica em "enviado" até virar "sem retorno" (o 404 da consulta é pendente, e não rejeição) |

**A mensagem de recusa nomeia o Client ID e o Client Secret, e não segue o texto da plataforma.** O `error` não aponta o campo certo: um `client_secret` errado volta como "client_id invalid". Uma mensagem que seguisse o texto da plataforma mandaria o administrador conferir o Client ID quando o errado é o segredo. Por isso a mensagem de recusa continua nomeando os dois campos ("Confira o Client ID e o Client Secret na tela de conectores"), e não deve ser "corrigida" pelo texto da plataforma.

**O que segue sem verificar:** o caminho de consulta de status. Ele só é exercitado quando a plataforma aceitar uma
nota, o que depende da correção do payload (a próxima fatia).

## Goals / Non-Goals

**Goals:**

- **Nenhum caminho envia sem autenticação** na composição do host. O "sem autenticação" existe só como
  pedido explícito de composição.
- **Credencial é dado do tenant.** A seção do ambiente ativo traz URL, identificador e referência. Nada de
  credencial ou URL global.
- **O segredo entra pela tela, vai para o cofre e nunca volta.** Nunca fica em claro no perfil, no banco, no
  disco ou no repositório, e o caminho é o mesmo em dev e em produção. A falta dele é falha alta, e aponta
  para a tela.
- **A resposta real da plataforma fica guardada e baixável,** redigida. Guardá-la nunca provoca um
  reenvio.
- **Nenhuma requisição age sobre outro tenant** (D18). As fotos só chegam ao tenant dono, e nenhuma ingestão
  grava ou lê documento de outro tenant.
- **Corrigir a credencial na tela surte efeito na hora,** sem esperar o intervalo da recusa lembrada.
- **`dotnet test` continua verde e sem rede.** O caminho real de autenticação é exercitado contra o mock em
  memória.

**Non-Goals** (além dos do proposal):

- **Configurar o fluxo de token por tenant** (Basic ou post, escopo, audiência). Entra com evidência do teste
  manual (D13, tarefa 15.3), no provider, para todos.
- **Circuit breaker genérico.** A recusa lembrada do D7 é específica da credencial recusada e existe para
  não bloquear a conta. Não é retentativa nem esquema novo de falha.

## Decisions

### D1. Provider real por padrão; sem autenticação só por pedido explícito

- **Registro padrão.** `AddAvalaraComplianceDispatcher` passa a registrar o provider real (singleton, pelo
  mesmo motivo de hoje: o cache precisa viver o processo).
- **Pedido explícito.** `UseAvalaraWithoutAuthentication()` troca pelo `NoOpAvalaraTokenProvider` e loga um
  aviso na composição. O Host nunca chama.
- **O que sai.** O `AddAvalaraTokenProvider()` deixa de existir, porque o real passa a ser o padrão.
- **O que muda no contrato.** O no-op devolve "sem token" como valor explícito, e não mais como cadeia
  vazia. Na composição padrão, a cadeia vazia não é resultado possível: o provider real ou devolve um token
  ou lança.

**Alternativa.** Manter o no-op padrão e ligar o real no Host, o que seria só acrescentar a chamada que
falta. Foi descartada: o padrão silencioso é exatamente o defeito de hoje. Qualquer composição nova, seja
um worker ou um teste esquecido, herdaria o "sem autenticação".

### D2. Credencial e URLs pela seção do ambiente, reaproveitando a leitura por tenant

**O que já existe e é reaproveitado.** O dispatcher já resolve a URL por tenant: `AvalaraOutboundSettings.Read`
lê a seção `profile.Environment` e o `BaseOf(settings)` usa a `baseUrl` dela. O que falta é o token usar a
mesma resolução.

- **Mesma leitura.** O provider recebe o mesmo `AvalaraOutboundSettings` que o dispatcher já leu. É uma
  leitura de perfil por envio, e não duas.
- **Endpoint de token.** Deriva da mesma seção.
- **Fallback.** A `BaseUrl` global sai (ver "Por que não há fallback").

**A seção:**

```json
{
  "sandbox": {
    "baseUrl": "https://<sandbox da plataforma>/",
    "tokenUrl": "https://<endpoint de token>/",
    "clientId": "<identificador do cliente>",
    "clientSecretRef": "kv:fh-tenant-a--outbound--sandbox--clientsecret",
    "establishments": { "44278225000180": { "codigoEmpresa": "…", "codigoContribuinte": "…" } }
  }
}
```

É a forma **persistida**. Na tela e no `PUT`, o Admin preenche o Client Secret no campo de escrita
`clientSecret`, e o servidor grava o valor no cofre e põe o `clientSecretRef` no lugar (D3, D5).

- **`tokenUrl` é opcional.** Sem ele, o endpoint é `baseUrl` + `AvalaraOptions.TokenPath`. O caminho é
  forma da API da plataforma, igual para todos os clientes, e por isso continua como opção do adapter. A
  URL é do tenant.
- **`clientId` fica em claro,** como o `auth.clientId` do D365. É identificador, e não segredo.
- **`clientTokenRef` sai do seed e da tela.** Nenhum fluxo conhecido o usa, e guardar uma credencial que
  ninguém lê é o defeito que esta fatia corrige. O leitor ignora o campo, se ele existir, como ignora
  qualquer campo desconhecido. Se o sandbox exigir um segundo segredo, ele entra com evidência, como um
  novo `*Ref` lido de fato.
- **O que falha, e como:** a falta de `baseUrl`, `clientId` ou do Client Secret configurado, uma URL inválida
  ou uma URL `http` fora do loopback lançam `DispatchRejectedException`, antes de qualquer requisição. É a
  mesma família de mensagem do D4 da change anterior, e aponta para a tela:
  - regra: "Configuração do conector: o tenant 'tenant-a', no ambiente 'sandbox', não tem clientId e Client
    Secret configurados (OutboundSettings.sandbox). Configure em Configurações → Conectores → Avalara →
    Sandbox.";
  - exemplo de URL: "Configuração do conector: OutboundSettings.sandbox.baseUrl precisa ser https
    (http só em loopback)."
- **A consulta de status usa a mesma seção.** Com isso, um documento enviado no sandbox é consultado no
  sandbox, enquanto o ambiente ativo não muda. A troca de ambiente com documentos em voo está nos riscos.

**Por que não há fallback.** O token e o Bearer carregam a credencial do tenant. Um fallback para uma URL
que não é do tenant é o caminho pelo qual o segredo do tenant-a chega a outro endereço. Com a credencial
obrigatória por seção, o fallback fica sem uso, e tirá-lo simplifica: `AvalaraOptions.BaseUrl`,
`ResolveBaseAddress` e `Avalara:BaseUrl` do `appsettings` saem. Os clientes HTTP ficam sem `BaseAddress`, e
todas as URIs passam a ser absolutas.

**Por que `https`.** O pedido de token leva o segredo no corpo. Uma `baseUrl` digitada com `http` mandaria o
segredo em claro pela rede. O loopback é a exceção, porque é o mock no dev e no teste em memória.

### D3. O segredo entra pela tela e vai para o cofre; em dev, o cofre é um emulador em memória

**O requisito.** O Admin do cliente não abre terminal. Se a configuração do conector é de tela, o segredo também
é: o `clientId`, o Client Secret e a URL, no sandbox e na produção. A tela é a porta de entrada, e o cofre é o
destino:

```
tela ──PUT /connector com clientSecret (campo de escrita)──▶ ConnectorProfileService
     ──ISecretStore.SetAsync──▶ cofre              (o valor)
     ──IConnectorProfileStore.UpsertAsync──▶ banco (só a referência kv:<nome>)
GET /connector ──▶ "configurado: sim/não" e a data   (nunca o valor, nem parte dele, nem a referência)
```

**A consequência.** A porta do cofre passa a ter escrita, e não só leitura. Em produção, o cofre é o Key Vault.
Em dev não há Key Vault, e o user-secrets não serve, porque a aplicação não escreve nele em tempo de execução. A
decisão de dev tem dois critérios:

1. **O caminho da tela é idêntico em dev e em produção.** Se o fluxo de dev for outro, o fluxo real nunca é
   testado.
2. **O que estiver em disco ou no banco nunca é o segredo em claro.**

**As opções:**

| Opção | Critério 1: mesmo caminho | Critério 2: nada em claro | Veredito |
|---|---|---|---|
| **Emulador da API do Key Vault, em memória, pelo mesmo adapter** | Sim. O mesmo `KeyVaultSecretStore` sobre o mesmo `SecretClient` do SDK oficial. Mudam só a URI, a credencial e o certificado, por configuração. | Sim. O segredo vive só na memória do container, e a persistência do emulador fica proibida. | **Escolhida.** É o mesmo padrão do Azurite e do emulador do Service Bus, que já estão no `docker-compose.yml`: "Azure sem Azure". |
| Store cifrado com Data Protection, no SQL ou em disco | Não. Em dev roda outro adapter, e o código do Key Vault só roda em produção, que é o que o critério 1 proíbe. | Parcial. O banco guarda texto cifrado, mas o anel de chaves fica em disco, protegido por DPAPI no Windows e **sem proteção** em Linux ou container. | Recusada. Tem a vantagem de persistir entre reinícios, mas testa um caminho que produção não usa. |
| Key Vault de dev real, no Azure | Sim, e com fidelidade total (RBAC, soft-delete, limites). | Sim. | Recusada **para o dia a dia**: quebra o "Azure sem Azure", e exige assinatura, rede e papel no RBAC para cada dev. Fica como a conferência de staging, antes do primeiro cliente (riscos). |
| user-secrets, variável de ambiente | Não. A aplicação não escreve neles em tempo de execução, e a tela não teria para onde gravar. | Não. O `secrets.json` do user-secrets é texto puro no perfil do usuário. | Recusada. Era a decisão anterior deste D3, e a correção de requisito a derruba. |
| Exceção "em dev aceita valor cru" | Não. | Não. O valor ficaria no banco, e o seed é código versionado. | Recusada, como antes. |

**Escolha do emulador.** Não existe emulador oficial da Microsoft para o Key Vault. Dois projetos da comunidade
falam a mesma API REST e aceitam o `SecretClient` oficial:

- o [Lowkey Vault](https://github.com/nagyesta/lowkey-vault) (`nagyesta/lowkey-vault`);
- o [Azure Key Vault Emulator](https://github.com/james-gould/azure-keyvault-emulator)
  (`jamesgoulddev/azure-keyvault-emulator`).

Os dois guardam só em memória por padrão. A persistência de cada um é opcional e **grava em disco**: o
Lowkey, por import/export em JSON; o james-gould, por um SQLite, e o README dele não diz que esse SQLite é
cifrado.

- **Escolhido: o Lowkey Vault.** Guarda em memória por padrão, exige credencial por padrão (como o real) e
  aceita o `SecretClient` com `DisableChallengeResourceVerification`. A imagem é fixada por versão, e nunca
  `:latest`. Import, export e persistência ficam **proibidos**: o `docker-compose.yml` não os liga, e o
  RUNNING.md diz por quê.
- **Plano B: o james-gould, com `Persist=false`.** Entra se a prova do emulador (tarefa 5.5) mostrar que o Lowkey não faz a ida e
  volta de que precisamos.
- **Conferido na tarefa 5.5 (2026-09-27).** O Lowkey fez a ida e volta, e o plano B não foi preciso.
  - **Imagem:** `nagyesta/lowkey-vault:7.3.112` (digest `sha256:2636ad677e0e…`), subida sem volume, sem import e sem
    export.
  - **Portas:** `8443` é a API do Key Vault (HTTPS). `8080` é a de metadados, com o `/ping` e o endpoint simulado de
    identidade gerenciada (`/metadata/identity/oauth2/token?resource=…`), que emite o token do emulador. A credencial
    `Emulator` pode tirar o token dali, sem valor fixo.
  - **API:** `api-version=7.4`. O set e o get responderam 200. A lista de versões trouxe a versão sem o valor e com
    `attributes.updated`, e é o que o `DescribeAsync` usa. Um segredo inexistente deu 404.
  - **Memória:** depois de `docker restart`, o segredo deu 404, e nada ficou em disco.
  - **Certificado:** a impressão SHA-1 é `56BED2BF3C0766AF85BDFCCACC99F67094EE2030`. É o certificado padrão do Lowkey,
    o mesmo em toda instalação e público. Por isso ele é fixado só em loopback, e nunca instalado como confiável no
    sistema.

**O que muda entre dev e produção.** São três valores de configuração do cofre e uma opção do cliente derivada
da URI. Nenhum código muda:

| Item | Dev | Produção |
|---|---|---|
| `SecretStore:VaultUri` | `https://localhost:<porta do emulador>/` | `https://<cofre>.vault.azure.net/` |
| `SecretStore:Credential` | `Emulator` (credencial fixa, que o emulador só confere se existe) | `Default` (identidade gerenciada) |
| `SecretStore:EmulatorCertificateThumbprint` | a impressão do certificado do emulador, fixada: o cliente aceita **só** esse certificado | ausente |
| `SecretClientOptions.DisableChallengeResourceVerification` | `true`, **derivado** da URI de loopback | `false`, **derivado** da URI fora do loopback |

Os valores de dev ficam no `appsettings.Development.json`, e nenhum deles é segredo: a URI de loopback, o nome do
modo e a impressão de um certificado público.

- **A recusa na subida:** o host recusa a credencial `Emulator` ou a impressão fixada quando a URI não é de
  loopback. Assim, uma configuração de dev não vaza para produção.
- **A verificação do desafio de autenticação:** o SDK confere se o recurso do desafio de autenticação bate com
  o domínio do Key Vault. O emulador exige desligar essa conferência, e produção nunca pode rodar sem ela. Por
  isso ela **não é configuração**: a montagem das opções do cliente liga o `DisableChallengeResourceVerification`
  só quando a URI do cofre é de loopback (`localhost`, `127.0.0.1`, `::1`), e nenhuma chave de configuração a
  altera. A trava fica na URI e também na flag, e um teste prova as duas.
- **A validação de TLS nunca é desligada:** em dev, ela aceita um certificado conhecido, e só em loopback.

**O trade-off, explícito.**

- **Reiniciar o emulador apaga os segredos.** É o preço do critério 2. O efeito é visível e alto:
  - a tela mostra "não configurado";
  - o envio falha com o motivo apontando para a tela.

  O dev digita de novo na tela, o que também exercita o caminho real a cada vez. Com o mock, qualquer valor
  serve.
- **Fidelidade de terceiro.** O emulador não é da Microsoft. O RBAC (403), o soft-delete, os limites e detalhes
  de versão podem diferir. O que o nosso código precisa, a regra de nome, nós conferimos do nosso lado. O resto
  entra no checklist do primeiro cliente, como prova num Key Vault real de staging.
- **Cadeia de suprimento.** É uma imagem de terceiro, só em dev, com a versão fixada. Nunca vai para
  produção, e nunca vê segredo de produção.

**Como cada regra do pedido é cumprida:**

1. **Campo de escrita, que vai do navegador ao servidor e nunca volta.** O `PUT` aceita o valor. O `GET` nunca o
   devolve (D5). A tela de conectores usa campo de senha, sem preenchimento: mostra "configurado em <data>", e
   um valor novo só é enviado quando digitado.
2. **O `GET` responde "configurado: sim/não" e a data, jamais o valor.**
   - **O que ele não mostra:** os últimos 4. Mostrá-los exigiria ler o segredo no cofre a cada `GET`, ou
     persistir um fragmento dele.
   - **Por que a data basta:** ela responde o que o Admin quer saber, "a minha troca pegou?".
   - **Como o `GET` sabe:** a porta descreve o segredo pelos metadados da versão atual, sem ler o valor.
3. **Nada de texto puro no perfil, no banco ou no seed.** A lista de nomes (`clientSecret`, `secret`, `password`,
   `senha`, `apiKey`, `token`, `accessToken`) vale para o que é **gravado**: o servidor converte o campo em
   `<campo>Ref` antes do upsert. O adapter recusa na leitura o que tiver chegado por outro caminho. O seed só
   tem referências no prefixo do tenant (D5).
4. **Não aparece em log, em mensagem de erro nem nas fotos do trace.**
   - **Log HTTP:** o host não liga o log de corpo de requisição. Se um dia ligar, o `/connector` fica fora, e o
     ADR registra.
   - **Tipos:** o `ConnectorProfileRequest` e os tipos que carregam o valor não o imprimem no `ToString`.
   - **Mensagens:** as de falha do cofre citam o campo e o status, nunca o valor. O SDK do Azure não loga
     corpo por padrão, e a opção fica desligada.
   - **Fotos:** a troca com o endpoint de token nunca é fotografada. A redação por valor do D9 cobre um eco
     do segredo.
5. **Trocar o segredo esquece o token em cache e a recusa lembrada daquele tenant.** O `ConnectorProfileService`
   avisa os observadores depois de qualquer escrita no cofre, mesmo que o upsert do perfil falhe em seguida
   (D5, D7).

**A mensagem do segredo ausente** aponta para a tela, e não para um comando. Ela cobre as duas situações (a
referência não existe, ou o cofre não tem o valor):

> Configuração do conector: o Client Secret do ambiente 'sandbox' do tenant 'tenant-a' não está configurado
> (OutboundSettings.sandbox.clientSecret). Configure em Configurações → Conectores → Avalara → Sandbox → Client
> Secret.

Quando a referência existe e o cofre não tem o valor, a mensagem acrescenta "(o cofre não tem o valor; o
emulador de dev pode ter reiniciado)".

**Permissão de escrita em produção: requisito de provisionamento.** A identidade do host passa a precisar de
escrita no cofre, e não só de leitura. Um host comprometido poderia sobrescrever os segredos a que tem acesso.
A mitigação é a política de acesso nunca alcançar um segredo que não seja de conector. O provisionamento de
cada ambiente de cliente MUST ter:

1. **Um papel sob medida,** e não o "Key Vault Secrets Officer" inteiro. Ele tem só as três ações de dados que o
   host usa: `Microsoft.KeyVault/vaults/secrets/getSecret/action`, `…/setSecret/action` e
   `…/readMetadata/action`. Não tem apagar, expurgar, backup nem restore.
2. **Uma condição ABAC na atribuição do papel,** que restringe as três ações ao prefixo `fh-`:
   - o `setSecret` pelo atributo da requisição (`@Request[Microsoft.KeyVault/vaults/secrets:name] StringStartsWith
     'fh-'`), porque o segredo ainda pode não existir;
   - o `getSecret` e o `readMetadata` pelo atributo do recurso (`@Resource[…secrets:name] StringStartsWith 'fh-'`).

   O prefixo vai em minúsculas, porque o Key Vault normaliza os nomes.
3. **Um cofre dedicado aos segredos de conector,** sem os segredos de infraestrutura (a chave do JWT, o SQL, o
   Service Bus). É defesa em profundidade, e é também o plano B se a condição não puder ser usada.
4. **A verificação em staging, antes do primeiro cliente:** a identidade do host grava e lê um `fh-…`, e recebe
   `ForbiddenByRbac` ao gravar ou ler outro nome.

**Por que os três mecanismos, e não só a condição:**

- **Preview:** a condição ABAC do Key Vault é [preview](https://learn.microsoft.com/azure/key-vault/general/rbac-abac).
  Pode mudar antes da disponibilidade geral, e pode não ser aceita na política do tenant do cliente.
- **O que ela não cobre:** só vale para as ações de segredo. A documentação avisa que uma condição de nome
  bloqueia as chamadas de listagem do cofre, e o host não lista: o `DescribeAsync` lê as versões de um nome
  conhecido. Isso também é conferido em staging.
- **O cofre dedicado:** dá o mesmo escopo efetivo, "só segredos de conector", sem depender do preview.
- **Do nosso lado:** o `KeyVaultSecretStore` recusa gravar ou ler nome fora de `fh-`, antes de chamar o cofre.
  Isso não substitui a política, que é quem vale contra um host comprometido. Serve para que um defeito nosso
  apareça em teste, e não como `ForbiddenByRbac` em produção.

Fica registrado no ADR-0027 e no checklist do primeiro cliente, em STATUS.md.

**A credencial do sandbox entre desenvolvedores** é digitada na tela por quem roda o teste. Ela é distribuída
fora do repositório e fora do chat, pelo canal de segredos da equipe.

### D4. A porta do cofre, com escrita

- **Application/Connectors:**
  - `SecretReference`: o parser puro do `kv:<nome>`, com a regra de nome do cofre;
  - `ISecretStore`, a porta:
    - `GetAsync(nome)` devolve o valor ou `null`, e vazio conta como `null`;
    - `SetAsync(nome, valor)` grava uma versão nova;
    - `DescribeAsync(nome)` devolve se existe e a data da versão atual, sem ler o valor;
  - `SecretNames.For(tenant, settings, caminho, campo)`: a derivação do nome (D5).

  Nada disso depende de framework.
- **Infrastructure:** `KeyVaultSecretStore`, sobre o `SecretClient` (`Azure.Security.KeyVault.Secrets`). É o
  **único** adapter do cofre, em dev e em produção.
  - Guarda o valor lido por pouco tempo (`SecretStore:ValueCacheSeconds`, padrão 300), para o D6 não ler o cofre a
    cada envio.
  - O `SetAsync` invalida essa entrada na hora.
  - A rotação feita direto no cofre, fora da tela, é vista em até 5 minutos.
- **Host:** registra o `KeyVaultSecretStore` com as três configurações do D3, e recusa na subida a combinação de
  dev fora do loopback. A `DisableChallengeResourceVerification` sai da URI, e nunca da configuração.
- **Guarda de nome:** o `KeyVaultSecretStore` recusa gravar ou ler nome fora de `fh-`, antes de chamar o cofre
  (D3, provisionamento).
- **Adapter D365:** o `ClientCredentialsD365TokenProvider` e o `D365InboundSettings` passam a usar o parser e o
  `GetAsync` da porta, e o `SecretReference` interno do D365 sai. A mudança é mecânica: o modo Azure CLI do dev
  não muda, e a app registration continua fora de escopo.
- **Testes:**
  - a porta falsa em memória, para a Application e os adapters;
  - o `KeyVaultSecretStore` com um `SecretClient` escrito à mão (o SDK expõe construtor protegido e métodos
    virtuais para isso), sem biblioteca de mock;
  - um teste de integração **opt-in** contra o emulador, que roda só com a variável de ambiente, no mesmo
    padrão do teste de integração do D365, e fica fora do `dotnet test` padrão.

**Alternativa.** Manter o provider de configuração do Key Vault (os segredos carregados no `IConfiguration` na
subida). Foi descartada: ele é só leitura e só recarrega em intervalo, então a gravação pela tela não teria
efeito até o próximo recarregamento.

### D5. O caminho de escrita, e a recusa do valor cru persistido

A gravação do perfil passa a ser o caso de uso `ConnectorProfileService.SaveAsync(tenant, request)`, na
Application. O `PUT /connector` só chama o caso de uso e traduz o resultado em HTTP.

**Ordem.**

1. **Validar tudo antes de escrever qualquer coisa.** Nas três settings (entrada, saída, chamados):
   - o JSON tem de ser válido;
   - nenhum campo `*Ref` pode vir da requisição, porque a referência é do servidor;
   - os campos de escrita são localizados: os nomes da lista, em qualquer nível, sem distinguir maiúscula nem
     `_` ou `-`.

   Um problema devolve a lista, e nada é gravado, nem no cofre nem no perfil.
2. **Gravar cada campo de escrita com valor não vazio no cofre**, com o nome derivado. Uma falha do cofre
   interrompe aqui, com mensagem sem o valor, e o perfil não muda.
3. **Montar as settings persistidas:**
   - cada campo de escrita gravado vira `<campo>Ref: "kv:<nome>"`, e o valor sai;
   - cada campo de escrita **ausente** mantém a referência que o perfil gravado já tinha naquele caminho.
4. **Upsert do perfil.**
5. **Avisar os `IConnectorProfileObserver`.** Isso acontece sempre que houve escrita no cofre ou upsert, mesmo
   que o upsert tenha falhado depois de uma escrita no cofre. O nome é fixo por caminho, então o segredo já
   mudou (D7).

**A referência é do servidor.** O nome é
`fh-{tenant}--{inbound|outbound|support}--{caminho}--{campo}`, em minúsculas e só com o que o cofre aceita, com até
127 caracteres. Um exemplo é `fh-tenant-a--outbound--sandbox--clientsecret`.

- **Por que o separador é duplo:** os ids de tenant têm hífen. Com hífen simples, o prefixo seria ambíguo: um tenant
  `tenant` teria o prefixo `fh-tenant-`, que também é prefixo de `fh-tenant-a-…`, e a checagem de dono aceitaria o
  segredo do tenant-a. Com `--` entre os segmentos, e nenhum segmento podendo conter `--` (nem o id do tenant, nem
  uma chave do JSON), o prefixo `fh-{tenant}--` é exato. A condição ABAC `fh-` do provisionamento não muda.

- **Por que o cliente não manda a referência:** se mandasse, um Admin do tenant-b poderia gravar
  `clientSecretRef: kv:fh-tenant-a--…`. O hub despacharia as notas do tenant-b com a credencial da Avalara do
  tenant-a, o que é injeção pela credencial, no espírito do D18.
- **O que isso também dá:** a operação pode provisionar um segredo direto no cofre, pelo nome previsível, sem
  passar pela tela.
- **Na leitura:** o adapter recusa uma referência fora de `fh-{tenant do perfil}--`, antes de ler o cofre. Isso
  cobre o SQL direto e o seed.

**Leitura do perfil (`GET /connector`).**

- **As settings voltam sem os campos `*Ref`.** A referência é detalhe do servidor, e a tela não precisa dela
  para gravar de novo, porque o passo 3 preserva o que não veio.
- **Ao lado das settings, um mapa `secrets`:** caminho → `{ configured, updatedOn }`, pelo `DescribeAsync`. O valor
  nunca é lido.
- **Referência sem segredo no cofre:** aparece como `configured: false`. É o caso do emulador reiniciado.

**A regra genérica.** A conversão olha nomes de campo, e não o schema de um adapter. Por isso ela fica na
Application e vale para as três settings, sem o Host conhecer nenhum adapter. Esta fatia exercita a Avalara. A
tela troca todos os campos "de referência" (`reference: true` no `adapterSchemas.ts`) por campos de escrita,
para que a tela tenha um modelo só.

**Recusa na leitura.** O `AvalaraOutboundSettings` recusa, na seção do ambiente ativo, três coisas: um campo de
escrita com valor, uma referência malformada e uma referência fora do prefixo do tenant. As três viram
`DispatchRejectedException` "Configuração do conector: …".

**Alternativa.** Um validador por adapter, registrado por nome. Conheceria o schema inteiro, mas é uma porta a
mais e um registro por adapter para uma regra que só olha nomes. Se um adapter precisar de validação de schema
na escrita, a porta entra com ele.

### D6. Token por credencial

```csharp
internal interface IAvalaraTokenProvider
{
    Task<AvalaraAccessToken> GetTokenAsync(AvalaraOutboundSettings settings, CancellationToken ct = default);
    void Invalidate(AvalaraAccessToken token);
    void Forget(string tenantId);   // esquece a recusa e os tokens do tenant; chamado ao salvar o perfil (D7)
}
// Value, IsFresh (acabou de vir do endpoint) e a chave do cache. ToString não mostra o valor.
internal sealed class AvalaraAccessToken { … }
```

- **Resolução.** O provider real pede a credencial à seção (D2), lê o segredo pelo `ISecretStore.GetAsync` (D4),
  que tem cache curto e é invalidado na gravação pela tela, e calcula a chave.
- **Chave do cache:** `tenant | ambiente | endpoint de token | clientId | SHA-256(segredo)`. A impressão do
  segredo segue o precedente do `ClientCredentialsD365TokenProvider`: a rotação gera entrada nova sem guardar
  o segredo como chave. A chave nunca vai para log.
- **Trava e dupla verificação.** Continuam como hoje, agora por chave, e não por tenant.
- **Margem.** A `TokenRenewalMargin` de hoje (5 min) é reaproveitada.
- **Sem `expires_in` (ou `expires_in` ≤ margem).** O token é usado e não entra no cache, e um aviso é logado
  uma vez por chave. Inventar uma validade seria chutar a regra da plataforma.
- **`Invalidate`.** Remove a entrada, se ela ainda for a mesma. O dispatcher chama no 401 com token do cache
  (D7).
- **`ToString`.** Os tipos que carregam token ou segredo (`AvalaraAccessToken` e a credencial resolvida) não
  são `record` com o valor impresso. O `ToString` traz só o tenant e o ambiente. Um log descuidado do objeto
  não vaza nada.

### D7. Classificação das falhas de autenticação

| Onde | Resposta | Desfecho | Por quê |
|---|---|---|---|
| Endpoint de token | 400 ou 401. No sandbox, 400 com `{"error": "<texto livre>"}` (D13) | `DispatchRejectedException` "Configuração do conector: a plataforma recusou a credencial do tenant 'x' no ambiente 'y' (HTTP 400: client_id invalid). Confira o Client ID e o Client Secret na tela de conectores." | Retentar não conserta a credencial e pode bloquear a conta. Na dead-letter, o motivo se perde. A mensagem nomeia os dois campos porque o texto da plataforma não aponta o certo (D13). |
| Endpoint de token | 2xx sem `access_token` | `DispatchRejectedException` "… o endpoint de token respondeu sem token." | É contrato inesperado, e retentar repete a mesma resposta. |
| Endpoint de token | 5xx, 429, rede | exceção, retry nativo | Transitório (ADR-0004). |
| Envio | 403 | `DispatchRejectedException` "Configuração do conector: a plataforma negou acesso ao tenant 'x' no ambiente 'y' (HTTP 403): <motivo>." | A credencial não tem acesso, e a retentativa não muda permissão. |
| Envio | 401 com token recém-emitido | igual ao 403 | O token acabou de sair do endpoint e foi recusado, e outra tentativa não muda isso. |
| Envio | 401 com token do cache | `Invalidate` + exceção, retry nativo | Token vencido ou revogado. A próxima tentativa busca outro. |
| Envio | 404 | `DispatchRejectedException` "Configuração do conector: o caminho de envio não existe nessa URL (HTTP 404 em POST <url>). A URL tem duas partes: a URL base do ambiente … (OutboundSettings.<ambiente>.baseUrl, na tela) e o caminho de envio (… em Avalara:DocumentsPath, no appsettings do host)." | Retentar repete o 404 até a dead-letter, sem motivo legível. O caminho do sandbox vem da URL do cliente e só é exercitado no teste manual: se estiver errado, tem de aparecer como mensagem. Na consulta de status, o 404 continua pendente (documento ainda não indexado). |
| Envio | 2xx sem identificador reconhecível | `DispatchRejectedException` "Conector: a plataforma respondeu HTTP 201 com sucesso, mas sem identificador reconhecível. O documento pode ter sido aceito e não será reenviado automaticamente. Veja a resposta gravada." | Retentar reenviaria um documento talvez aceito. É a pergunta "reenvio: atualiza ou duplica?", que ninguém respondeu. |
| Consulta | 401 com token do cache | `Invalidate` + exceção | O poll repete na próxima passada (limite em `MaxAttempts`). |

**Recusa lembrada.** A recusa da credencial fica guardada pela chave do D6, por `CredentialRefusalHold`
(padrão 5 min, opção do adapter).

- **Por que existe:** sem ela, cada nota em voo e cada consulta do poll faria um pedido ao endpoint de token
  com a credencial errada. São N tentativas de login em sequência, que é o padrão que bloqueia conta em
  provedor de identidade.
- **Como se desfaz:** a chave contém a impressão do segredo e o `clientId`. Corrigir a credencial muda a
  chave, e o pedido novo sai na hora. O intervalo cobre o caso em que a plataforma corrige a conta sem que a
  credencial mude.
- **O que não é:** não é retentativa, porque não repete nada. É o contrário: evita repetir.

**Esquecida na correção.** Salvar o perfil do tenant pela tela de conectores (`PUT /connector`) esquece, na
hora, a recusa e os tokens em cache daquele tenant, em todos os ambientes.

- **Por quê:** sem isso, quem corrige o segredo na tela espera até cinco minutos achando que não funcionou.
  Com o segredo novo, a chave muda e a recusa não se aplicaria. Mas a correção pode ser do outro lado: a
  plataforma libera o cliente, e o admin salva o perfil sem mudar nada para tentar de novo. Salvar é o
  sinal explícito de "mudei a configuração, tente de novo".
- **Como:** o `ConnectorProfileService` avisa os `IConnectorProfileObserver` depois de qualquer escrita no cofre
  ou upsert, mesmo que o upsert falhe depois de uma escrita no cofre (D5). O adapter
  Avalara registra um observador que chama `IAvalaraTokenProvider.Forget(tenantId)`.
- **Por que os tokens também:** recomeçar a autenticação do tenant do zero custa um pedido de token, e tira
  qualquer estado velho do caminho.
- **O que não muda:** os outros tenants não são afetados.
- **Limites:**
  - uma mudança feita por SQL direto não avisa ninguém, e aí vale o intervalo;
  - o estado é em memória, por processo. Com mais de uma instância do host, as outras esquecem pelo
    intervalo (ver os riscos).

**Revisão do CNV D10.** O design da change anterior descartou "tratar 401 e 403 como rejeição", com o
argumento de que a autenticação quebrada é do tenant inteiro e deve seguir a dead-letter visível. Esta
fatia reverte isso para o 403 e para o 401 com token fresco, com três argumentos que não existiam quando o
token nem estava ligado:

- a dead-letter registra `MaxDeliveryCountExceeded`, e não o motivo da plataforma. É exatamente a "rejeição
  cujo motivo não chega ao dashboard" que o pedido não aceita;
- retentar credencial pode bloquear a conta;
- "é do tenant inteiro" também vale para a falta de `establishments`, que já é rejeição com motivo.

O 5xx, o 429 e a rede continuam no retry nativo. O 404 do envio virou rejeição de configuração (a tabela acima), e o
da consulta de status continua pendente.

**Onde a decisão revertida está registrada, e onde fica a nota.** O ADR-0026 não cita o 401 nem o 403. O §2
dele diz só "Falha transitória. Continua como exceção, com retry nativo e dead-letter (ADR-0004)", e é essa
frase que os cobria. A decisão explícita está em dois lugares:

- **no design arquivado da CNV** (D10, "Alternativas"). O arquivo não se edita;
- **na spec principal `compliance-dispatch-outcome`** ("São elas: 5xx, 429, 401, 403, 404 e falha de rede").
  O delta desta change a modifica.

A nota de revisão vai no ADR-0026, no formato que o 0025 recebeu do 0026 e o 0024 recebeu do 0025. O D16
diz exatamente onde.

### D8. A quarta foto

**Porta.**

```csharp
Task SaveResponseAsync(string tenantId, string naturalKey, string destination, string exchange, string json,
    CancellationToken ct = default);
```

- `exchange` vale `submit` ou `status`.
- O `NoOpProcessingTrace` acompanha.
- Cada camada continua fotografando o que produz (ADR-0006). A resposta é do adapter de saída, que é quem
  fala com a plataforma.

**Nomes.** A regra do layout fica num lugar só (`TracePaths`, na Infrastructure):

- `{destino}.response.submit.json`: a resposta da última tentativa de envio;
- `{destino}.response.status.json`: a última consulta com corpo.

O prefixo é o mesmo das outras três fotos, e por isso o `/trace` e o zip as incluem sem mudança de regra.
Os nomes são distintos no zip.

**Envelope** (montado pelo adapter e já redigido, D9):

```json
{
  "exchange": "submit",
  "request": { "method": "POST", "url": "https://<host>/documents" },
  "response": {
    "status": 400,
    "receivedAt": "2026-10-01T12:00:00Z",
    "headers": { "Content-Type": "application/json; charset=utf-8" },
    "body": { "mensagens": ["…"] }
  },
  "redactions": 0
}
```

- **`request`:** só método e URL sem query string. Nenhum cabeçalho de requisição.
- **`headers`:** lista fechada: `Content-Type`, `Date`, `X-Correlation-Id`, `X-Request-Id`, `Request-Id` e
  `traceparent`. A lista cresce com evidência, se o sandbox usar outro identificador de correlação.
- **`body`:** entra como JSON quando é JSON, e como texto nos outros casos.
- **`redactions`:** conta o que foi redigido. Um valor acima de zero é sinal de que a plataforma ecoou algo
  sensível, e isso vira achado no relatório.

**Quando se grava.**

- **Envio:** logo depois do `SendAsync`, em qualquer status, antes de classificar. O corpo é lido uma vez,
  como texto, e redigido. A foto, o motivo e a leitura do identificador saem desse mesmo texto.
- **Consulta:** toda resposta com corpo sobrescreve. Isso custa uma escrita por consulta, limitada por
  `MaxAttempts` (10) por documento. No primeiro envio real, é justamente a resposta de status não
  reconhecida que se precisa ver. Uma resposta sem corpo (204, 404 pendente) não sobrescreve.
- **Endpoint de token:** nunca é fotografado.

**Melhor esforço, e por quê.** A foto da resposta é a primeira gravação depois da requisição. Se a falha
nela propagasse, o Service Bus reentregaria, e a esteira reenviaria à plataforma um documento que ela talvez
já aceitou. Por isso, a gravação é envolvida em `try/catch`:

- a falha é logada com tenant, chave e `exchange`, sem o conteúdo;
- o desfecho segue como se a gravação tivesse dado certo.

A foto do payload de destino continua antes do envio e continua interrompendo, porque nada foi mandado
ainda. A assimetria é deliberada e fica no ADR-0027.

### D9. Redação: um ponto só, antes de qualquer uso

`SensitiveText.Redact(texto, valoresConhecidos)` devolve `(texto redigido, contagem)`. É aplicado ao corpo cru
antes da foto, do motivo (`PlatformMessage`) e de qualquer log. Faz três passadas:

1. **Por valor:** toda ocorrência do token em uso vira `[redigido]`. No endpoint de token, o segredo e o
   token também.
   - **Na troca de token** (o motivo da recusa e o `out/token.json` da sonda; o trace nunca a fotografa), a regra é
     o `TokenExchangeRedaction`. As credenciais da resposta (`access_token`, `refresh_token`…) entram na redação por
     valor, e os identificadores da sessão e da conta (`sessionId`, `userId`, `subId`, `appId`, o `login`, com o nome
     da empresa, e o `email`) viram `[mascarado]`, por nome e por valor. Um valor curto só é mascarado pelo nome, para não apagar
     dígitos de outro campo.
2. **Por padrão:** `Bearer <valor>` vira `Bearer [redigido]`, em qualquer texto.
3. **Por nome, quando é JSON:** o valor das propriedades de nome sensível vira `[redigido]`, em qualquer
   nível. Os nomes são `authorization`, `access_token`, `token`, `refresh_token`, `id_token`,
   `client_secret`, `secret`, `password`, `senha` e `api_key`, normalizados sem maiúscula, `_` ou `-`.

**Os outros canais:**

- **Logs HTTP:** os dois clientes (envio e token) usam `RedactLoggedHeaders(_ => true)`. Nenhum valor de
  cabeçalho aparece no log do `IHttpClientFactory`, qualquer que seja o nível. É defesa explícita, que não
  depende do padrão do framework.
- **Exceções:** as mensagens que o adapter monta nunca interpolam token, segredo ou corpo de pedido de
  token. As do `EnsureSuccessStatusCode` trazem só o status.
- **`ToString`:** ver D6.

**Custo aceito.** A redação por nome pode esconder um campo legítimo chamado `token` numa resposta da
plataforma (um protocolo, por exemplo). O marcador fica visível e a contagem sobe. Se isso acontecer, o
relatório registra, e a lista se ajusta com evidência.

### D10. `/trace` e zip: só para o tenant dono, e com montagem testável

**Tenant dono.** Hoje o `/trace/{tenantId}/{naturalKey}` e o `/documents/{tenantId}/{naturalKey}/download` leem o
Blob pelo tenant da rota, sem compará-lo com o do usuário. Num sistema multi-tenant com documento fiscal,
isso deixa qualquer usuário autenticado baixar a fonte crua, o domínio, o payload e, agora, a resposta da
plataforma de outro cliente. Vira problema real no dia em que existir o segundo tenant de verdade, e o
endpoint que entrega a foto desta fatia é justamente um deles.

**Reuso: a porta de leitura já existe.** O chamado de suporte já lê as fotos de uma nota pelo
`INoteTraceReader` (`Application/Support`), implementado pelo `BlobNoteTraceReader`. É a mesma varredura por
`{tenant}/` e `/{chave}/` que o `Program.cs` repete à mão. O `SupportTicketService.BuildNoteZipAsync` já monta o
zip desses arquivos. Nada de porta nova:

- **Caso de uso:** `DocumentTraceQuery.GetAsync(tenantId, naturalKey)`, em `Application/Tracing`, sobre o
  `INoteTraceReader` e o `ITenantContext`.
  - Se o tenant da rota difere do tenant do usuário, devolve "não encontrado" **sem chamar o leitor**. O Blob
    do outro tenant não é tocado.
  - Um documento sem fotos também devolve "não encontrado".
- **Zip:** o trecho de montagem do `BuildNoteZipAsync` vira `TraceArchive.Zip(arquivos)`, uma função pura em
  `Application/Support`, ao lado do `TraceFile`. O suporte e o download passam a usar a mesma função. A
  entrada do zip continua com o nome do arquivo.
- **Endpoints:** ficam finos. O `/trace` monta o JSON dos `TraceFile`, e o download chama o `TraceArchive.Zip`.
  Os dois respondem com o mesmo 404 e o mesmo corpo para "é de outro tenant" e para "não existe". É a regra
  do `/reprocess`: não confirmar a existência de nota de outro tenant.
  - As chaves do JSON do `/trace` passam do caminho inteiro do blob para o nome do arquivo, que é o que o
    leitor devolve. O dashboard classifica pelo fim do nome (`endsWith`) e continua funcionando.
  - Quando a mesma foto existe em dois períodos (reprocesso em outro mês), o leitor mantém a ordem da
    listagem, e a do período mais recente vem por último e prevalece no JSON.
- **Por que um caso de uso, e não só um `if` no endpoint:** o `if` no `Program.cs` não tem teste, porque não
  há projeto de teste de host, e o pedido exige teste. No caso de uso, a regra é testada na Application com
  um leitor falso, e o teste prova também que o leitor não é chamado.

**O dashboard e a quarta foto.** O `shape()` do `useTrace.ts` classifica como "destino" todo `.json` que não é
`domain.json`. Com a quarta foto, `avalara.response.submit.json` e `avalara.response.status.json` sobrescreveriam o
payload na aba Destino, em silêncio. O mesmo ramo já pega hoje a fonte do D365, que é `source.json`, e não
`source.xml`.

- **Classificação:** o `shape()` passa a reconhecer, nesta ordem, `source.*`, `domain.json`,
  `*.response.submit.json`, `*.response.status.json` e, por fim, o `<destino>.json`.
- **Tela:** o `DocumentDetail` ganha a aba "Resposta", com os dois envelopes como JSON, na mesma
  visualização das outras abas. A resposta da plataforma aparece no dashboard, e não só no zip.

**O resto do limite de tenant** (o `/ingest`, o locator, o drop, os recursos por identificador) está no D18.

### D11. Mock com autenticação

- **`POST /oauth/token`** (corpo JSON, como na coleção do cliente; formulário é recusado): aceita qualquer `client_id`
  e `client_secret` não vazios, aceita sem exigir o `disableTokenRefresh`, e devolve a forma da resposta do sandbox
  (`access_token`, `token_type` `bearer`, `expires_in` 86400 e os identificadores, com valores de mentira), com um token
  aleatório guardado em memória.
- **`/admin/token/{aceitar|recusar}`:** força a recusa na forma do sandbox, `400 {"error":"client_id invalid"}`, para o roteiro local
  do D7.
- **Os caminhos de envio** (`/taxcompliance/v2/fiscal/dfe`, o do sandbox, e `/documents`), com o status em
  `{caminho}/{id}/status`: exigem `Bearer` emitido pelo mock. Sem ele, respondem
  `401 {"mensagens":["token ausente ou inválido (mock)"]}`. Um caminho que o mock não conhece dá 404, como a plataforma.
- **`/admin/*` e `GET {caminho}/{id}`** (inspeção) continuam abertos, porque são ferramenta de dev.

Com isso, o mock nunca mais aceita envio sem autenticação, e o `DispatchToMockTests` exercita o caminho real
de token em memória.

### D12. Sonda do sandbox (`tools/AvalaraSandboxProbe`)

**Por que uma ferramenta, e não a esteira.** O experimento do D14 manda variantes de um payload que o
mapper não produz, como `finalidadeNotaFiscal` presente. Pôr variante na esteira seria lógica de
experimento no caminho de produção, ou correção de payload, que é a próxima fatia.

**Por que não um script com `curl`.** O script teria de ler o segredo e escrever o próprio OAuth, e a
redação seria outra, diferente da do adapter. A evidência gravada seria redigida por uma regra que o teste
não cobre.

**O que é.** Um console .NET em `tools/`, como o `MockComplianceApi`. Referencia o adapter Avalara
(`InternalsVisibleTo`) e a Infrastructure, e reusa:

- o perfil do tenant, pelo `IConnectorProfileStore` do banco de dev;
- o `KeyVaultSecretStore`, com a configuração de cofre do Host. Em dev, é o emulador, e o segredo é o que foi
  gravado pela tela. A sonda só lê: nunca grava no cofre;
- o provider de token;
- `SensitiveText` e o envelope do D8.

**Comandos:**

- `token --tenant tenant-a`: é a verificação da premissa de autenticação (D13). Diz se obteve o token, os campos da
  resposta e o `expires_in`, e nunca imprime o token.
- `send --tenant tenant-a --payload <avalara.json> [--omit campo] [--set campo=valor] --ref-suffix <s> --label <l> [--poll]`:
  - parte do payload de destino baixado no zip do hub;
  - aplica a variante no nível de topo;
  - acrescenta `#<s>` ao `codigoReferenciaIntegracao`;
  - envia, com o mesmo `DocumentsPath`;
  - grava `out/<l>.submit.json` e, com `--poll`, `out/<l>.status.json`, os dois já redigidos.
- `get --tenant tenant-a --id <id> --label <l>`: faz a leitura de volta, se a plataforma a oferecer (o mock
  oferece; a real, não se sabe). Grava `out/<l>.readback.json`.

A sonda nunca escreve no banco do hub. A pasta `tools/AvalaraSandboxProbe/out/` entra no `.gitignore`. Só o
que for curado para evidência sai de lá (D15).

### D13. O teste manual contra o sandbox

**Expectativa honesta.** O primeiro envio real provavelmente volta rejeitado, e isso é o que a fatia quer
medir. As 5 notas são da Contoso de demonstração, de 2016, sem IBS/CBS, e podem ser recusadas por motivo
alheio ao nosso mapeamento:

- período de escrituração de 2016;
- chave de acesso que nunca foi autorizada na SEFAZ, se a plataforma a conferir lá;
- estabelecimento de demonstração;
- a importação com participante sem CNPJ.

Corrigir o payload a partir dessas rejeições é a próxima fatia.

**Premissa de autenticação: assumida, e confirmada no roteiro abaixo** (Context). Não há portão antes do código.
O passo 1 do roteiro é onde a premissa se confirma, e a regra do que fazer se ela não bater está nele.

**Roteiro depois da implementação** (o RUNNING.md ganha a seção):

1. **A verificação da premissa.** Na tela de conectores, preencher a seção `sandbox` do tenant-a: `baseUrl`,
   `tokenUrl` se houver, `clientId` e o Client Secret, digitado no campo de escrita. Depois:
   - conferir que a tela mostra "configurado em <data>", e que o `GET /connector` não traz o valor;
   - rodar `probe token`, e preencher a tabela "Resultado da verificação da premissa" do Context, sem credencial
     e sem token;
   - no primeiro envio (passo 3, ou um `probe send`), conferir que o caminho `documents` existe: qualquer status
     diferente de 404.

   Pela tabela, há dois desfechos:

   - **Bate:** client credentials com o segredo no corpo, com `access_token` e `expires_in` numérico em segundos.
     Verificado em 2026-09-27, com corpo JSON (tabela da premissa, no Context).
     - Com `expires_in` menor que a margem, a margem é revista no D6.
     - Com 404 no caminho de envio, a nota é rejeitada com o motivo que aponta a URL base e o
       `Avalara:DocumentsPath` (D7). O caminho certo, pela documentação do sandbox, entra nessa opção do adapter, sem
       código.
   - **Não bate** (outro fluxo, escopo ou audiência obrigatórios, token por empresa, mTLS, resposta sem
     `access_token` ou `expires_in` reconhecível): o teste manual para, e a change volta a `/opsx:update`. O
     retrabalho fica nos grupos de autenticação (Context).
2. **A correção pela tela.** Com um segredo errado de propósito:
   - a primeira nota recusa com o motivo;
   - salvar o segredo certo na tela faz a nota seguinte, reprocessada na hora, pedir token de novo, sem
     esperar o intervalo.
3. **As 5 NF-e 55** pelo hub, como no §7 do RUNNING.md, apontando para o sandbox.
4. **Conferência:**
   - o dashboard mostra o desfecho e o motivo de cada nota;
   - o zip de cada uma traz as cinco fotos;
   - nenhuma foto tem `redactions > 0` sem explicação.
5. **Registro:** as respostas, pelo zip, vão para o relatório e para as fixtures (D15).

**Resultado do teste manual (2026-09-27).** O ponta a ponta rodou contra o sandbox real, e a expectativa honesta se
confirmou: o primeiro envio real voltou recusado, por conteúdo, e não por autenticação nem por caminho.

- **A descoberta:** as 14 referências do `fiscosysdev` foram descobertas pelo feed.
- **O roteamento:** as 9 NFS-e viraram "ignorado", sem nenhuma chamada ao F&O.
- **A montagem e o envio:** as 5 NF-e 55 foram montadas e enviadas, com a autenticação `client_credentials` de corpo
  JSON, no caminho `taxcompliance/v2/fiscal/dfe` e com a tradução do estabelecimento pelos `establishments` do perfil.
- **A resposta:** recusa de validação. A plataforma exigiu seis campos que a montagem não preenche: `operacao`, `tipoPagamento`, `parceiro.Codigo`, `itens[].Item.TipoItem`, `itens[].UnidadeMedida.Descricao` e `itens[].Item.UnidadeMedida.Descricao`. É a
  entrada da próxima fatia (a correção do payload), registrada no `docs/STATUS.md`.
- **O que não foi exercitado:** a consulta de status (nenhuma nota aceita, nenhum `id`).

**Relatório** (`docs/avalara-sandbox-primeiro-envio.md`):

- **Cabeçalho:** o host do sandbox, a data e o commit, sem nenhuma credencial.
- **Uma linha por nota:** chave natural, HTTP do envio, status final e o motivo literal (redigido).
- **Classificação de cada motivo, por uma regra fixada antes de ler:**
  - **nosso: contrato ou mapeamento.** Outro payload, montado a partir do mesmo domínio, resolveria. São
    exemplos o campo que o domínio tem e não mandamos, o formato e o bloco errado;
  - **nosso: configuração.** Códigos da empresa, credencial, endpoint;
  - **característica do dado.** Só outra nota resolveria. São exemplos a data de 2016, a ausência de
    IBS/CBS exigida, a chave não autorizada, o participante sem CNPJ e a empresa de teste;
  - **indeterminado.** A evidência não separa. Vira pergunta à Avalara, e nunca palpite.
- **Respostas às perguntas do CNV D18,** só o que a evidência mostrar: formato do erro, blocos do schema,
  totais, reenvio. O resto fica "não respondido".

### D14. O experimento do campo numérico omitido

É tarefa própria, depois da passada principal, porque precisa de uma nota e de uma resposta de base.

- **Campo:** `finalidadeNotaFiscal`. É o item mais perigoso do checklist. O JSON real de mercadoria traz
  `1`, e o hub o omite.
- **Documento:** a nota da passada principal com menos motivos de recusa, para que o campo não seja
  mascarado por outra recusa.
- **Variantes:** partem do payload de destino dessa nota (zip) e diferem só no campo. Cada uma vai com
  `codigoReferenciaIntegracao` + `#exp-<letra>`:

| Variante | Campo |
|---|---|
| A | omitido (o payload do hub, como foi) |
| B | `1` (o valor do JSON real) |
| C (controle) | `0` |

**Por que C.** O pedido fala em duas variantes. Com só A e B, "A difere de B" não separa "omitido é lido
como 0" de "omitido é lido como ausente". O controle com `0` é o que torna a comparação decidível.

**Leitura:** a resposta do envio, a da consulta e a leitura de volta, se a plataforma oferecer.

**Tabela de interpretação** (fixada aqui, antes do envio):

| Observação | Conclusão | Item do checklist |
|---|---|---|
| A é recusada citando o campo, e B não tem essa queixa | O campo é exigido. Omitido não vira 0, e falha alto. | Fecha com evidência: omitir não é silencioso para este campo. A próxima fatia precisa mandá-lo. |
| A se comporta como C (mesma resposta ou mesma leitura de volta), e B difere | Omitido é lido como 0. | Reescrito: o risco silencioso é real. A próxima fatia não pode omitir campo numérico de código. |
| A, B e C têm a mesma resposta, e há leitura de volta | Decide pela leitura de volta, pelas mesmas duas linhas acima. | Conforme a leitura. |
| A, B e C têm a mesma resposta, e não há leitura de volta | Inconclusivo pela resposta. | Reescrito com o que foi testado e por que não decide. Continua aberto, com a pergunta direta à Avalara. |
| Todas recusadas por motivo alheio ao campo | O campo foi mascarado. | Repetir sobre a próxima nota com menos recusas. Se nenhuma servir, fica aberto com a evidência. |
| B ou C recusada por duplicidade (mesma chave) | Confusão com o reenvio. | O dado vai para o item "Reenvio: atualiza ou duplica?". O experimento é refeito sobre a `BRMF06-110000027`, que não tem chave de acesso. |

**Registro:**

- as três respostas vão para o relatório;
- o item do checklist recebe `[x]` com o link, ou é reescrito com a evidência, conforme a tabela. Pela regra
  do checklist, teste com fixture não fecha o item: só a execução no sandbox fecha.

### D15. Respostas reais viram testes

As respostas curadas vão para `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/Fixtures/sandbox/`,
no formato do envelope e já redigidas. Um caso por forma de resposta:

- aceite;
- recusa no envio;
- consulta com erro;
- recusa de credencial, se houver.

**Testes:**

- **Varredura:** nenhum arquivo da pasta tem `Bearer` seguido de valor, JWT (três segmentos base64url começando por `eyJ`), `access_token` ou
  `client_secret` com valor.
- **Reprodução:** o stub HTTP devolve o status e o corpo gravados, e o dispatcher (e a esteira, no ponta a
  ponta) registra o desfecho do spec. A recusa real fica `IntegrationError`, com o motivo dela, uma única
  requisição e nenhuma exceção, ou seja, sem dead-letter. É o "reexercitar contra resposta de verdade" do
  pedido.
- **Extração do motivo:** a `PlatformMessage` sobre o formato real produz texto legível. Isso fecha o item
  "Formato real do erro".

**Se nenhuma recusa real acontecer,** o caso fica registrado como não exercitado. Não se fabrica uma.

### D16. ADR-0027 e as revisões

**`docs/adr/0027-credencial-por-tenant-e-resposta-da-plataforma.md`** registra:

- o provider real por padrão;
- a credencial e as URLs por seção, sem fallback e com `https`;
- o segredo pela tela e o cofre como destino: o campo de escrita, a referência derivada pelo servidor, o `GET`
  sem o valor;
- a decisão do cofre de dev (D3): as cinco opções pelos dois critérios, o emulador em memória pelo mesmo
  adapter, as três configurações recusadas fora do loopback, a verificação do desafio derivada da URI e o
  trade-off do reinício;
- a porta do cofre com escrita, e a recusa do valor cru persistido na leitura;
- o requisito de provisionamento da escrita do host: o papel com get, set e readMetadata, a condição ABAC `fh-*`, o
  cofre dedicado e a verificação em staging;
- a verificação do desafio de autenticação derivada da URI, desligada só em loopback;
- a classificação das falhas de autenticação e a recusa lembrada;
- o aceite sem identificador;
- a quarta foto, o envelope, a redação e a gravação por melhor esforço.

O ADR também registra a conferência de tenant no `/trace` e no download (D10) e o esquecimento da recusa na
gravação do perfil (D7).

**Cabeçalho do 0027:** "**Revisa:** ADR-0026 §2 (401/403 e aceite sem identificador); ADR-0006 (quatro
fotos, gravação da resposta por melhor esforço); ADR-0019 (resolução do segredo em dev, recusa na escrita)".

**Notas de revisão**, no formato que o 0025 recebeu do 0026 e o 0024 recebeu do 0025. Cada ADR revisado
ganha três coisas: uma linha **Revisado por** no cabeçalho, uma nota em citação no ponto exato do texto e a
mudança de status no índice.

| ADR | Linha "Revisado por" no cabeçalho | Nota em citação (`> **Revisado pelo ADR-0027 (data).** …`), no ponto exato | Índice (`docs/adr/README.md`) |
|---|---|---|---|
| **0026** | "[ADR-0027](…). No §2, o 403 e o 401 com token recém-emitido deixam de ser falha transitória, e o aceite sem identificador deixa de ser retentado." | No §2, logo depois do marcador "**Falha transitória.** Continua como exceção…". Diz que essa frase cobria o 401 e o 403 sem nomeá-los; que agora o 403 e o 401 com token fresco são impossibilidade do conector com motivo e sem retentativa; que o 401 com token do cache invalida e segue o retry; que o 2xx sem identificador não é retentado; e por quê: o motivo se perde na dead-letter e a retentativa pode bloquear a conta. Aponta a decisão explícita revertida, que é o D10 do design arquivado da CNV. | "Aceito — §2 revisado pelo 0027" |
| **0006** | "[ADR-0027](…). Quatro fotos: a resposta da plataforma entra ao lado da fonte, do domínio e do destino." | Na lista das três fotos e no parágrafo da falha de gravação: a foto da resposta é a exceção, gravada por melhor esforço porque vem depois do envio. | "Aceito — revisado pelo 0027" |
| **0019** | "[ADR-0027](…). O segredo entra pela tela e vai para o cofre; a referência é do servidor." | No marcador "Segredos NÃO ficam em claro". Diz que o valor entra pela tela, como campo de escrita, e vai para o cofre; que o perfil guarda só a referência derivada pelo servidor, no prefixo do tenant; que o `GET` nunca devolve o valor; e que em dev o cofre é um emulador da API, em memória, pelo mesmo adapter. | "Aceito — revisado pelo 0027" |

O design arquivado da CNV não é editado. A reversão fica rastreável pela nota no 0026 e pelo delta da spec
`compliance-dispatch-outcome`.

**`docs/adr/0028-limite-de-tenant.md`** é um ADR próprio, porque a decisão é de outro eixo, a identidade e o
isolamento, e não a credencial da plataforma. Registra:

- a regra do D18 e o papel explícito para agir sobre outro tenant;
- a diferença entre vazamento, injeção, leitura lavada pela esteira e interferência, que justifica o recorte;
- a varredura dos endpoints;
- a regra do locator em quem o lê, com o `traces` nunca como origem;
- o drop sem tenant padrão;
- as pré-condições do drop e da fila antes de abri-los a cliente;
- a pergunta sobre o `/ingest` em produção.

**Cabeçalho do 0028:** "**Revisa:** ADR-0018 (fecha a ressalva dos endpoints de debug e estende a regra a
todos); ADR-0009 (sem tenant padrão no drop; o tenant vem de quem escreve)".

| ADR | Linha "Revisado por" | Nota em citação, no ponto exato | Índice |
|---|---|---|---|
| **0018** | "[ADR-0028](…). A regra vale para todo endpoint e para a ingestão, e os endpoints de debug foram endurecidos." | Logo depois da ressalva "Os endpoints de debug (`/trace`, `/drop`, download) … não foram endurecidos com o claim — a UI só os alcança via dados já escopados". Diz que a ressalva está fechada, que o argumento da UI não protege, porque quem ataca não usa a UI, e que o `/ingest` e o `deactivate` foram achados na varredura. | "Aceito — revisado pelo 0028" |
| **0009** | "[ADR-0028](…). Sem tenant padrão; a pré-condição de quem escreve no drop." | No marcador "deriva `tenant` e `chave` do nome do arquivo". Diz que não há mais tenant padrão, e que o tenant do caminho só é confiável enquanto só processos nossos escrevem no drop. | "Aceito — revisado pelo 0028" |

O índice ganha as linhas do 0027 e do 0028.

### D17. Testes

| Pedido | Onde |
|---|---|
| Provider real registrado, e o stub só quando pedido explicitamente | Avalara.Tests: o DI resolve o provider real por padrão e o no-op só com `UseAvalaraWithoutAuthentication()`. O ponta a ponta usa a composição padrão. |
| Token obtido, cacheado e renovado por tenant, pela margem | Avalara.Tests (provider): reuso, margem, concorrência, sem `expires_in`, `Invalidate`. |
| Dois tenants com credenciais diferentes não compartilham token | provider (chaves distintas) e dispatcher (o `Authorization` de cada envio é o do tenant). Também: troca de ambiente e rotação do segredo. |
| Segredo ausente: falha alta e nomeada | dispatcher: zero requisições (nem token nem envio). O motivo traz o campo, o tenant, o ambiente e o caminho na tela, e nenhum comando. São dois casos: a referência ausente e o cofre sem o valor. Também o ponta a ponta. |
| O segredo não volta no `GET` | Application.Tests (`ConnectorProfileService`, a leitura): depois de gravar `s3cr3t`, a resposta traz `configured: true` e a data, e não contém `s3cr3t`, nem parte dele, nem a referência. A referência sem valor no cofre dá `configured: false`. |
| O segredo gravado pela tela vai só para o cofre | Application.Tests (`ConnectorProfileService`, com cofre e store falsos): o cofre recebe o valor com o nome derivado, e o perfil gravado tem só o `clientSecretRef`, sem o campo de escrita. O campo ausente mantém a referência. `*Ref` na requisição dá 400, sem escrita. JSON inválido dá 400, sem escrita no cofre. A falha do cofre deixa o perfil intacto, com mensagem sem o valor. |
| O segredo fora do log e das fotos | `ConnectorProfileService` com um logger que captura, e com o cofre falso que lança: nenhum log nem mensagem contém o valor. O `ToString` do `ConnectorProfileRequest` não traz as settings. O dispatcher e o provider: o segredo ecoado pela plataforma sai redigido da foto e do motivo (D9). |
| Gravar pela tela esquece o token e a recusa | Application.Tests: a escrita no cofre avisa os observadores, e também quando o upsert falha depois dela. O ponta a ponta: com o token recusado pelo mock, gravar o segredo pelo `ConnectorProfileService` e reprocessar faz o token ser pedido na hora. |
| O mesmo adapter em dev e em produção | Infrastructure.Tests: o `KeyVaultSecretStore` sobre um `SecretClient` escrito à mão (set, get, describe sem ler o valor, cache invalidado no set, e a recusa de nome fora de `fh-` antes de chamar o cofre). O host recusa a configuração de emulador fora do loopback. Um teste de integração opt-in faz a ida e volta contra o emulador. |
| A verificação do desafio só desligada em loopback | Infrastructure.Tests (a montagem das opções do cliente): `DisableChallengeResourceVerification` é `true` com `localhost`, `127.0.0.1` e `::1`, e `false` com `https://<cofre>.vault.azure.net/`; nenhuma chave de configuração a liga. |
| A escrita do host restrita a `fh-*` | Provisionamento, fora do `dotnet test`: a verificação em staging do D3 (grava e lê `fh-…`, e recebe `ForbiddenByRbac` em outro nome), registrada no checklist do primeiro cliente. |
| Segredo e token fora da foto, do log e das mensagens de erro | `SensitiveText`, e o dispatcher com a plataforma ecoando o `Bearer` e com `access_token` no corpo. O dispatcher usa um trace que grava, um logger que captura e a exceção capturada. O provider recusado com o segredo ecoado na descrição. `ToString` dos tipos. A varredura das fixtures. |
| Resposta gravada como quarta foto e presente no zip | dispatcher (trace que grava, os dois `exchange`), `TracePaths` (Infrastructure.Tests), `TraceArchive.Zip` (Application.Tests, e o suporte continua verde sobre ela) e o ponta a ponta. |
| Rejeição real com o motivo dela, sem dead-letter | reprodução das fixtures do sandbox (D15). |
| A recusa lembrada é esquecida na correção (D7) | Application.Tests (`ConnectorProfileService`): o salvamento válido avisa os observadores com o tenant, e o recusado não grava nem avisa. Avalara.Tests: depois de `Forget("tenant-a")`, a chamada seguinte pede token na hora, dentro do intervalo e com a mesma credencial; a recusa do tenant-b continua; os tokens do tenant-a em cache também foram esquecidos. |
| Download e `/trace` conferem o tenant (D10) | Application.Tests (`DocumentTraceQuery`): outro tenant dá "não encontrado" com o leitor nunca chamado; o próprio tenant recebe os arquivos; o documento sem fotos dá o mesmo "não encontrado". O mesmo corpo de 404 para os dois casos é conferido no roteiro manual. |
| `/ingest` no tenant do login, com o locator restrito (D18) | Application.Tests (`ManualIngestion`, com fila, resolvedor e contexto falsos): a referência sai com o tenant do contexto; o locator recusado não enfileira e devolve a regra. Inbound.Xml.Tests (`CheckLocator` e `FetchAsync`): o `traces/` do próprio tenant e o de outro são recusados pela regra própria; o prefixo de outro tenant, o outro container, o `..`, o `.`, o `\` e o arquivo vazio são recusados; o `nfe/{tenant}/x.xml` é aceito; na recusa, o leitor de blob nunca é chamado. D365Poll.Tests: `CheckLocator` pelo `Parse`. |
| Drop sem tenant padrão (D18) | BlobDrop.Tests: a raiz e os três segmentos não são ingeridos; `{tenant}/{chave}.xml` é. |
| Agendamento de outro tenant (D18) | Infrastructure.Tests (`SqlScheduleStore`): o `DeactivateAsync` do tenant-b sobre um id do tenant-a devolve "não encontrado" e não desativa. |
| Dashboard com a quarta foto (D10) | se houver teste de front: o `shape()` põe as respostas na aba própria, a `source.json` do D365 na Origem e o payload no Destino. Sem suíte de front, isso é conferido no roteiro manual. |

**Outros testes:**

- `SecretReference`, `SecretNames.For` (a regra de nome do cofre e o prefixo do tenant) e o seed (Application.Tests,
  Infrastructure.Tests);
- `AvalaraOutboundSettings`: a referência fora do prefixo do tenant é recusada sem ler o cofre;
- o `ClientCredentialsD365TokenProvider` sobre a porta;
- `AvalaraOutboundSettings`: credencial, `https` e loopback, segredo em claro na seção;
- a classificação do D7, linha a linha;
- a recusa lembrada, e a credencial nova que não a herda;
- a foto por melhor esforço: o trace falha e há um único POST, com o registro de enviado;
- o ponta a ponta com o token recusado pelo mock: rejeição e zero envios.

Não há projeto de teste de host. Por isso, as regras dos endpoints (a gravação do perfil e o acesso às
fotos) moram em casos de uso da Application, e são testadas lá (D5, D10). O endpoint só traduz o resultado
em HTTP, e essa tradução é conferida no roteiro manual.

### D18. Limite de tenant: o tenant vem de quem está logado

**A regra.** Toda requisição autenticada age sobre o tenant de quem está logado, e nunca sobre um tenant
vindo da requisição, pelo corpo, pela rota ou pela query. O ADR-0018 já dizia isso para a integração manual e
a agendada. Ele próprio anotou, como ressalva, que o `/trace`, o `/drop` e o download "não foram endurecidos
com o claim", porque "a UI só os alcança via dados já escopados". Esse argumento não protege nada, porque
quem ataca não usa a UI. O `/ingest` nem aparece lá. Esta fatia fecha a ressalva e aplica a regra ao resto.

**Vazamento, injeção e interferência: por que o recorte é este.**

- **Vazamento** (o download e o `/trace`). É confidencialidade: um usuário lê o documento fiscal de outro
  cliente. O dado sai, e nada muda do lado de lá.
- **Injeção** (o `/ingest` com o tenant no corpo, e o `/drop` fixo no tenant-a). É integridade, com efeito
  fora do hub. Um documento fiscal é gravado em nome de outro cliente, e a esteira faz o resto sozinha:
  - monta o documento;
  - despacha para a plataforma de compliance **daquele** cliente, com a credencial **dele**;
  - registra no dashboard **dele**.

  O dano chega à escrituração do outro cliente, fora do nosso sistema, e nenhum reprocesso nosso desfaz.
  É por isso que a injeção é mais grave que o vazamento, embora os dois entrem.
- **Leitura alheia lavada pela esteira** (o `/ingest` com o locator livre). O locator aponta para
  `traces/{outro}/…` ou `nfe/{outro}/…`, e o hub monta aquele conteúdo sob o tenant de quem pediu. É vazamento,
  mas feito pela própria esteira: o documento alheio vira foto, domínio e payload do tenant errado, e ainda é
  despachado com a credencial dele. Fixar o tenant pelo login tira a injeção, mas não isto.
- **Interferência** (o `deactivate` por identificador). É integridade de configuração: desliga em silêncio a
  integração agendada de outro cliente.

Os quatro entram porque qualquer usuário autenticado os alcança hoje, e a consequência é fiscal ou
operacional. O que fica só registrado (o diretório de empresas, o drop e a fila abertos a cliente) não é
alcançável hoje, ou é dado de demonstração.

**Varredura dos endpoints.** Cobre todos os mapeados no `Program.cs`, que é o único host com rotas. O mock é
ferramenta de dev e não tem tenant.

| Endpoint | De onde vem o tenant | Hoje | Nesta change |
|---|---|---|---|
| `POST /ingest` | corpo (`TenantId`), e `Locator` livre | injeção e leitura alheia | tenant do login e locator pela regra da origem, com 400 fora dela (caso de uso `ManualIngestion`) |
| `GET /trace/{tenantId}/{key}` | rota | vazamento | `DocumentTraceQuery` (D10) |
| `GET /documents/{tenantId}/{key}/download` | rota | vazamento | `DocumentTraceQuery` (D10) |
| `POST /documents/{tenantId}/{key}/reprocess` | rota | já compara com o `ITenantContext` (404) | nada |
| `POST /drop/{key}` | fixo no `tenant-a` | injeção no tenant-a por qualquer usuário | prefixo do tenant do login |
| `POST /schedules/{id}/deactivate` | identificador, e o `DeactivateAsync` não filtra por tenant | interferência entre tenants | o store filtra pelo tenant, e o endpoint dá 404 como o `reactivate` |
| `POST /integrations/manual` | `TenantId` no corpo, ignorado; e `CompanyCode` | tenant do login; a descoberta filtra o `CompanyCode` dentro do tenant | tirar o campo morto e o comentário "tenant nulo cai no de dev", sem efeito de comportamento |
| `POST /schedules`, `PUT /schedules/{id}` | `TenantId` no corpo, ignorado | tenant do login; o `UpdateAsync` filtra | tirar o campo morto |
| `POST /schedules/{id}/reactivate` | identificador | filtra (lista do tenant e `ReactivateAsync` escopado) | nada |
| `GET /documents`, `/groups`, `/groups/{c}/{b}/{d}/documents`, `/executions`, `/schedules` | `ITenantContext` nas queries | escopado | nada |
| `/connector`, `/info`, `/tenant`, `/users`, `/users/{id}`, `/users/{id}/reset-password` | `ITenantContext`; o serviço filtra o id pelo tenant | escopado | nada |
| `/support/tickets`, `/support/tickets/estimate` | `ITenantContext`; as chaves passam pelo `ListByKeysAsync(tenant)` | escopado | nada |
| `/companies`, `/companies/{code}/branches` | nenhum: a porta não recebe tenant | a mesma lista (JSON de dev) para todos | registrado (riscos, STATUS) |
| `/auth/*`, `/` | anônimo, ou o claim | sem tenant de entrada | nada |

**Os campos mortos.** O `ManualIntegrationRequest.TenantId` e o `ScheduleRequest.TenantId` são aceitos e
ignorados. O comentário do primeiro ainda diz "tenant nulo cai no de dev", o que não é mais verdade.

- **Por que tirar não quebra nada:**
  - o handler já usa o tenant do login;
  - o dashboard não preenche os campos (os tipos de TS saem junto);
  - um JSON que ainda mande o campo continua aceito, porque propriedade desconhecida é ignorada.
- **O `IngestRequest.TenantId`:** vale o mesmo, e o RUNNING §4 deixa de mandá-lo.

**Nenhum fluxo legítimo age sobre outro tenant.** Os gatilhos internos (drop, feed do D365, agendador e poll)
não passam pelo HTTP. Vão direto à fila ou ao store, como sistema. Se um dia existir um chamador serviço a
serviço, ele ganha um papel explícito com autorização própria, e não um campo no corpo.

**A regra do locator mora em quem o lê.**

- **Onde fica:** o `IInboundSource<T>` ganha `string? CheckLocator(DocumentReference reference)`, que devolve o
  problema ou `null`. Quem interpreta o locator é o adapter da origem, e o desenho reaproveita o
  `IInboundSourceResolver`, que já escolhe o source pela origem. Não há porta nova.
- **XML:** o locator é `nfe/{reference.TenantId}/{arquivo}`.
  - O prefixo `traces/` nunca vale. É uma regra própria, conferida antes da do prefixo e testada à parte.
    Assim, ela continua valendo se a do prefixo for afrouxada.
  - Sem segmento `.` ou `..`, sem `\`, e com arquivo não vazio.
  - O container de entrada (`nfe`) é opção do adapter, o mesmo `InboxContainer` do drop.
- **D365:** o formato `d365/{empresa}/{recId}`, pelo `D365DocumentLocator.Parse` que já existe. O locator do
  D365 é interpretado no ERP do próprio tenant, com a credencial dele.
- **Onde a regra roda:**
  - **na busca:** o `XmlGoodsInvoiceSource.FetchAsync` confere antes de ler. Isso cobre o drop, a descoberta, o
    reprocesso e a mensagem na fila. A falha é exceção, e segue o retry e a dead-letter. Ela só acontece se
    algo contornou a entrada, e o log nomeia a regra;
  - **na ingestão manual:** o caso de uso `ManualIngestion` (`Application/Inbound`) resolve o source pela
    origem `Xml` e confere antes de enfileirar. O `/ingest` traduz o problema em 400. A regra fica testada na
    Application, sem projeto de teste de host.
- **Por que a travessia importa:** o `AzureBlobReader` monta a URI do blob a partir do locator, e o `System.Uri`
  normaliza o `..`. Um `nfe/tenant-b/../tenant-a/x.xml` passaria na conferência de prefixo feita no texto e
  viraria `nfe/tenant-a/x.xml` na leitura.

**O seed e o catálogo.** Os XMLs de exemplo passam de `nfe/nfe-exemplo*.xml` para `nfe/tenant-a/nfe-exemplo*.xml`.
Mudam o `LocalSeed`, o `LocalDocumentDiscovery` e o `/drop`, que lê a amostra. O RUNNING §4 também muda. O fluxo
de dev continua, com o caminho novo, e o `/ingest` fica alinhado com o drop, que já grava em `nfe/{tenant}/`.

**Zona de drop.**

- **Formato:** o `DropBlobNaming` deixa de ter tenant padrão e passa a exigir `{tenant}/{chave}.xml`, com dois
  segmentos. Fora disso, o arquivo fica no drop, e o watcher avisa uma vez por nome, por processo.
- **O que sai:** o `DefaultTenant` sai do `BlobDropOptions`.
- **O que muda de lado:** o teste "sem barra, usa o tenant padrão" passa a afirmar o contrário. Nenhum fluxo
  documentado põe arquivo na raiz do drop.

**Caminhos de produção, conferidos.**

| Caminho | De onde vem o tenant | De onde vem o locator | Veredito |
|---|---|---|---|
| Drop / Event Grid | do caminho do arquivo no drop, escolhido por quem escreve | do nosso processo: o watcher move para `nfe/{tenant}/{chave}.xml` e monta o locator | **Locator:** conferido, vem do próprio evento. **Tenant:** seguro enquanto só processos nossos escrevem no drop (hoje, o `/drop` de dev, agora com o tenant do login). **Pré-condição** antes de um cliente escrever no drop: a credencial de escrita presa ao tenant (container por tenant, ou SAS de diretório com namespace hierárquico), e o tenant tirado dessa ligação, e não do caminho. |
| Feed do D365 | da varredura dos perfis (sistema) | montado pelo feed a partir das linhas do ERP | Conferido. O documento é buscado no ERP do próprio tenant, com a credencial dele, e nenhum dado de usuário entra no caminho. |
| Fila (`documents-in`, `documents-discovered`) | do corpo da mensagem | do corpo da mensagem | Hoje só processos nossos publicam. O d365/03 e o CLAUDE.md preveem uma SAS send-only por cliente. **Pré-condição** antes de emitir a primeira: fila ou tópico por cliente, com o tenant tirado da entidade, e não do corpo. A regra do locator na busca já vale para qualquer mensagem. |
| Descoberta (manual, reprocesso) | do login, pelo runner e pelo `/reprocess` | do catálogo (dado de sistema), filtrado por tenant | Conferido. |
| Agendador e poll | das linhas gravadas (sistema) | do registro | Conferido. |

**Pergunta de escopo, sem pressa, registrada no STATUS.md:** o `/ingest` deve existir em produção? O gatilho
real é o drop, o feed e o Event Grid, e ele é conveniência manual. A correção do locator vale de qualquer
forma, porque em dev o problema também é problema.

### D19. Sequência: o limite de tenant primeiro, e sozinho

**A regra.** O trabalho do limite de tenant (D10 na parte do acesso às fotos, e D18) vem nos primeiros grupos das
tarefas (1 a 4). Ele é commitado e mergeado na `main` sozinho, no fim do grupo 4, e isso já foi feito no #58. A
parte Avalara vem depois, do grupo 5 em diante, sem portão: a premissa de autenticação é confirmada no teste
manual (Context, D13).

**Por quê.** As correções de tenant são de segurança e valem independente da Avalara:

- o vazamento no download e no `/trace`;
- a injeção no `/ingest` e no `/drop`;
- a leitura alheia pelo locator;
- a interferência no `deactivate`.

A parte 2 se apoia numa premissa sobre a autenticação de um terceiro. Se ela não batesse e a change voltasse
para `/opsx:update`, as correções de tenant ficariam presas atrás de algo que não tem nada a ver com elas. É um
acoplamento que não deveria existir.

**O que é independente, conferido.** Nenhum item dos grupos 1 a 4 usa código da parte 2:

| Item da parte 1 | Depende da parte 2? |
|---|---|
| `DocumentTraceQuery`, sobre o `INoteTraceReader` que já existe | não |
| `TraceArchive.Zip`, extraído do suporte | não. O teste de que ele leva as duas respostas fica na parte 2, com a quarta foto (grupo 11) |
| `CheckLocator`, `ManualIngestion`, seed e catálogo, drop sem tenant padrão | não |
| `deactivate` escopado e os campos `TenantId` mortos | não |
| ADR-0028 e as notas no 0018 e no 0009 | não. O 0027 e as notas no 0006, no 0019 e no 0026 são da parte 2 |

**O que fica na parte 2, embora seja de fronteira de tenant:** a referência de segredo derivada pelo servidor, e o
`*Ref` recusado na requisição (D5). Depende da porta do cofre, que é da parte 2.

**A spec e a change.** A change continua uma só. A parte 1 entra na `main` com a pasta da change, que segue
ativa, e a spec `tenant-boundary` só vai para `openspec/specs` no arquivamento.

- **Se a parte 2 travar por muito tempo:** o delta `tenant-boundary` pode ser levado para uma change própria e
  arquivado sozinho. Isso acontece, por exemplo, se a premissa de autenticação não bater no teste manual. É uma
  decisão para esse momento, e não agora.

## Risks / Trade-offs

- **[O fluxo real de autenticação pode não ser o client credentials assumido]** O formato do pedido já divergiu uma
  vez: o corpo é JSON, e não formulário, pela coleção do cliente, e o ajuste coube no provider e no mock. A premissa é assumida, e só
  se confirma no teste manual, no fim da fatia (Context, D13, tarefa 15.3). O código de autenticação é todo
  escrito e testado contra o mock antes dessa confirmação. → Um ajuste de forma (margem, `DocumentsPath`)
  cabe no provider ou na configuração, com teste. Um fluxo estruturalmente outro leva a `/opsx:update`, e o
  retrabalho fica nos grupos de autenticação: o 8, o 9, o 10 e o endpoint de token do mock, no 12. O cofre, a
  tela, a quarta foto e a parte 1, já mergeada, não dependem da premissa. É um risco aceito de propósito, em
  troca de não bloquear o código num passo manual.
- **[Caminhos e respostas vieram do mock]** O caminho de envio e o de status, o `id` e os valores
  `carregado`/`erro` podem não bater. → A quarta foto mostra a resposta. O aceite sem identificador não
  reenvia (D7). O status não reconhecido fica `Submitted` e vira `Unconfirmed` no limite, com a última
  resposta fotografada. O ajuste da leitura é a próxima fatia.
- **[Documento duplicado na plataforma]** A sonda envia a mesma nota mais de uma vez. → Cada variante leva
  um `codigoReferenciaIntegracao` próprio, e a duplicidade por chave está prevista na tabela do D14. Isso é
  sandbox, e não produção.
- **[403 transitório de um gateway vira rejeição]** → O motivo aparece, e o reprocesso manual resolve. É
  mais barato que cinco reenvios que perdem o motivo.
- **[Recusa lembrada segura um tenant já corrigido]** → Salvar o perfil na tela esquece a recusa na hora
  (D7). Sem salvar, a correção do nosso lado muda a chave, e a do lado da plataforma espera o intervalo
  (5 min, configurável).
- **[Mais de uma instância do host]** A recusa e os tokens estão em memória, por processo. O `PUT` esquece só
  na instância que o atendeu. → As outras esquecem pelo intervalo, e o pior caso é esperar os 5 minutos em
  parte das notas. Um estado compartilhado (Redis, tabela) entra quando o host tiver mais de uma instância.
  Hoje ele tem uma só.
- **[Redação por nome esconde um campo legítimo]** → O marcador é visível, a contagem sobe e a lista se
  ajusta com evidência (D9).
- **[Foto de status a cada consulta]** Em volume, são escritas no Blob por documento em voo. → O limite é
  `MaxAttempts` (10). Se pesar em produção, a foto passa a ser só na mudança de status. Não pesa nesta
  fatia.
- **[Troca de ambiente com documentos em voo]** A consulta usa o ambiente ativo, e um documento do sandbox
  seria consultado na produção (404, depois `Unconfirmed`). → O comportamento já é esse hoje. A troca de
  ambiente é rara e deliberada, e o RUNNING.md avisa para esperar o poll fechar.
- **[O emulador perde os segredos ao reiniciar]** Depois de um `docker compose down` ou de reiniciar o
  container, todo segredo gravado pela tela some, até o do mock. → É o preço de não ter nada em claro em disco
  (D3). O efeito é visível: a tela mostra "não configurado", e o envio falha apontando para ela. O RUNNING.md
  avisa. A persistência do emulador continua proibida, porque grava em disco.
- **[Fidelidade de um emulador de terceiro]** O RBAC, o soft-delete, os limites e detalhes de versão podem
  diferir do Key Vault real. → A regra de nome é conferida no nosso código. O resto entra no checklist do
  primeiro cliente: uma ida e volta num Key Vault real de staging, com a identidade do host, antes do deploy
  do cliente.
- **[O host com escrita no cofre]** Um host comprometido poderia sobrescrever os segredos a que tem acesso. →
  É um requisito de provisionamento (D3): um papel só com get, set e readMetadata, uma condição ABAC que o
  restringe a `fh-*` e um cofre dedicado como defesa em profundidade. A verificação é em staging, e o registro
  fica no ADR-0027 e no checklist.
- **[A condição ABAC do Key Vault é preview]** → Pode mudar, ou não ser aceita no tenant do cliente. O cofre
  dedicado dá o mesmo escopo efetivo sem ela, e a verificação em staging mostra qual dos dois vale.
- **[A parte 1 mergeada com a change ainda ativa]** A spec `tenant-boundary` só vai para `openspec/specs` quando a
  change for arquivada. → Se a parte 2 travar por muito tempo, o delta `tenant-boundary` pode ser
  levado para uma change própria e arquivado sozinho (D19). Até lá, a spec vive na pasta da change, que está na
  `main`.
- **[Rotação direto no cofre, fora da tela]** O cache curto do valor lido atrasa a rotação até 5 minutos. → A
  gravação pela tela invalida na hora. A rotação por fora é rara, e o intervalo é configurável.
- **[Wiring do Key Vault real no deploy]** A URI e a identidade do cofre de produção vêm com o deploy do cliente
  (fora de escopo). → O adapter é o mesmo do dev, e sem a configuração o host recusa a subida, em vez de subir
  sem cofre.
- **[A zona de drop e a fila confiam no tenant de quem escreve]** O drop tira o tenant do caminho do arquivo,
  e o consumidor da fila tira o tenant do corpo da mensagem. Hoje só processos nossos escrevem nos dois. →
  Pré-condição registrada no D18, no ADR-0028 e no STATUS.md: antes de dar a um cliente acesso de escrita ao
  drop, ou a SAS send-only da fila (d365/03), o tenant precisa vir da credencial de quem escreve, e não do
  caminho nem do corpo.
- **[Mover os XMLs do seed quebra referências antigas no dev]** Uma referência já enfileirada, ou um
  documento de dev registrado com `nfe/nfe-exemplo.xml`, passa a falhar pela regra do locator. → É só dev. O
  seed regrava os XMLs no caminho novo a cada subida, o catálogo da descoberta aponta para ele, e o
  reprocesso relê pelo catálogo.
- **[O diretório de empresas não é por tenant]** O `/companies` devolve a mesma lista a todos. A porta não
  recebe tenant, e o adapter JSON é de dev. → Registrado no STATUS.md: o adapter real precisa escopar, e a
  porta ganha o tenant nessa fatia.
- **[As notas de 2016 são recusadas por motivo do dado]** → Isso é resultado válido. O relatório separa por
  regra (D13), e a próxima fatia só trata o que for "nosso".

## Migration Plan

1. **Código:** deploy da fatia. Não há migration de banco.
2. **Dev, contra o mock:**
   - `docker compose up -d` passa a subir também o emulador do cofre, em memória;
   - na tela de conectores, digitar um Client Secret qualquer no `sandbox` do tenant-a, porque o mock aceita
     qualquer credencial. Isso vale a cada reinício do emulador;
   - num banco novo, o seed já traz `clientId: "mock-client"` e as referências no prefixo do tenant, sem
     `clientTokenRef`;
   - num banco existente, o passo do RUNNING.md regrava as `OutboundSettings` do tenant-a (o mesmo padrão de SQL
     por arquivo dos `establishments`), com as referências no formato novo.
3. **Dev, caminhos do seed (D18):** os XMLs de exemplo passam a `nfe/tenant-a/…`, e o seed os regrava a cada
   subida. O `/ingest` do RUNNING §4 passa a usar `nfe/tenant-a/nfe-exemplo.xml`, sem `tenantId` no corpo. Os
   blobs antigos em `nfe/nfe-exemplo*.xml` ficam inofensivos, porque nenhum locator válido aponta para eles.
4. **Dev, contra o sandbox:** o roteiro do D13. A URL, o `clientId` e o Client Secret, pela tela. O segredo real
   fica só na memória do emulador.
5. **Ambiente de cliente:**
   - o provisionamento do cofre do D3: o papel sob medida, a condição ABAC `fh-`, o cofre dedicado e a
     verificação em staging;
   - a configuração `SecretStore:VaultUri` e `Credential = Default`;
   - o Admin do cliente preenche, pela tela, cada seção de ambiente usada (`baseUrl`, `clientId`, Client Secret);
   - sem isso, os envios são rejeitados com motivo que aponta para a tela, e são reprocessáveis depois de
     corrigido.
6. **Rollback:** reverter o deploy.
   - O código antigo lê só `baseUrl` e `establishments`, e ignora `clientId`, `tokenUrl` e `clientSecretRef`.
     Volta a enviar sem autenticação, o que só o mock aceita.
   - As fotos de resposta ficam no Blob como arquivos a mais, e o zip antigo as inclui.
   - Os documentos rejeitados por configuração ficam `IntegrationError`, reprocessáveis.

## Open Questions

Nada disto muda spec, abordagem ou tarefas. São respostas que o sandbox dá e que viram uma linha de opção,
de lista ou de teste.

- **Nome do cabeçalho de correlação da Avalara:** entra na lista permitida do D8.
- **`expires_in` típico do token:** confirma se a margem de 5 min é adequada.
- **Formato real do erro de token** (`error`/`error_description` do OAuth ou outro): a mensagem do D7 extrai
  de forma tolerante, pela mesma `PlatformMessage`.
- **Se a plataforma oferece a leitura de volta do documento gravado:** decide a linha usada na tabela do
  D14.
