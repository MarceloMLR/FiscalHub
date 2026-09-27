# ADR-0027: Credencial da plataforma por tenant, segredo pela tela no cofre, e a resposta como quarta foto

- **Status:** Aceito
- **Data:** 2026-09-27
- **Revisa:**
  - **ADR-0026 §2:** o 403 e o 401 com token recém-emitido deixam de ser falha transitória, e o aceite sem
    identificador deixa de ser retentado.
  - **ADR-0006:** quatro fotos, com a resposta da plataforma gravada por melhor esforço.
  - **ADR-0019:** o segredo entra pela tela e vai para o cofre; a referência é do servidor, e o valor cru persistido é
    recusado na leitura.
- **Change OpenSpec:** `openspec/changes/connect-avalara-sandbox` (parte 2, grupos 5 a 18), capacidades
  `connector-secret-references`, `avalara-tenant-authentication`, `platform-response-trace` e o delta de
  `compliance-dispatch-outcome`.
- **Premissa pendente:** a forma de autenticação da plataforma (ver §3) é assumida, e só é confirmada no teste manual
  contra o sandbox (tarefa 15.3). O resultado entra no fim deste documento.

## Contexto

O adapter Avalara mandava tudo sem autenticação. O `NoOpAvalaraTokenProvider` era o padrão, o provider real existia
mas ninguém o registrava, e a credencial dele era uma só, global, em `AvalaraOptions`. Contra o mock, que não exigia
token, isso passava despercebido. Contra a plataforma real, nenhuma nota chegaria.

Três faltas apareciam juntas:

- **A credencial não era do tenant.** Cada cliente tem a própria conta na plataforma, por ambiente. Uma credencial
  global mandaria as notas do tenant-b com a conta do tenant-a.
- **O segredo não tinha caminho.** O ADR-0019 dizia que as settings guardam `kv:...` "resolvidas no Key Vault em
  produção", mas nada escrevia no cofre, nada lia em dev, e o Admin do cliente não abre terminal. Se a configuração do
  conector é de tela, o segredo também é.
- **A resposta da plataforma se perdia.** O hub guardava a fonte, o domínio e o payload (ADR-0006), mas não o que a
  plataforma respondeu. No primeiro envio real, a resposta é justamente o que precisa ser visto na íntegra.

## Decisão

**Cada envio e cada consulta levam o token da credencial do próprio tenant, no ambiente ativo. O segredo entra pela
tela, vai para o cofre, e o perfil guarda só a referência que o servidor deriva. A resposta da plataforma é a quarta
foto, já redigida.**

### 1. Provider real por padrão

- `AddAvalaraComplianceDispatcher` registra o provider real, singleton (o cache vive o processo).
- "Sem autenticação" só por pedido explícito, `UseAvalaraWithoutAuthentication()`, que loga um aviso ao compor. O Host
  nunca chama. O `AddAvalaraTokenProvider()` sai.
- O no-op devolve `AvalaraAccessToken.None`, um "sem token" explícito, e não mais a cadeia vazia. Na composição padrão
  não há caminho que leve a uma requisição sem autorização: nem a falta de credencial, nem a do segredo, nem uma falha
  do endpoint de token.

### 2. Credencial e URLs pela seção do ambiente, sem fallback

A seção do ambiente ativo em `OutboundSettings` traz `baseUrl`, `tokenUrl` (opcional; sem ele, `baseUrl` +
`TokenPath`), `clientId` e `clientSecretRef`. É a mesma leitura (`AvalaraOutboundSettings`) que já dava os códigos da
empresa. A consulta de status usa a mesma seção.

- **Sem fallback.** `AvalaraOptions.BaseUrl`, `ClientId`, `ClientSecret`, `ResolveBaseAddress` e `Avalara:BaseUrl`
  saem, e os clientes HTTP ficam sem `BaseAddress`. Um fallback para uma URL que não é do tenant é o caminho pelo qual
  o segredo de um tenant chega a outro endereço. A seção `Avalara` do Host fica só com a forma da API
  (`DocumentsPath`, `TokenPath`, margens), igual para todos os clientes.
- **`https` obrigatório,** `http` só em loopback (o mock). O pedido de token leva o segredo no corpo.
- **O `clientTokenRef` sai.** Nenhum fluxo o lia. O leitor o ignora, como a qualquer campo desconhecido.
- Faltando algo, é `DispatchRejectedException` "Configuração do conector: …", antes de qualquer requisição, nomeando o
  campo e o caminho na tela ("Configurações → Conectores → Avalara → Sandbox → Client Secret").

### 3. Token por credencial, e a premissa assumida

- **Chave do cache:** tenant, ambiente, endpoint de token, `clientId` e a impressão SHA-256 do segredo. Dois tenants
  nunca dividem um token, nem com a mesma credencial, e a rotação do segredo gera entrada nova sem guardar o segredo
  como chave. Uma busca por chave sob concorrência, e a `TokenRenewalMargin` de antes.
- **Sem `expires_in` utilizável,** o token é usado e não entra no cache, com um aviso por chave. Inventar validade seria
  chutar a regra da plataforma.
- **A premissa:** o pedido é OAuth `client_credentials`, com `client_id` e `client_secret` no corpo do formulário. É a
  forma mais comum, mas não foi confirmada com a plataforma. Ela é conferida no teste manual (a sonda `token` e o envio
  pelo hub). Se estiver errada, o retrabalho fica no provider e no mock, e o resto desta decisão não muda.

### 4. O segredo pela tela, e a referência do servidor

```
tela ──PUT /connector com clientSecret (campo de escrita)──▶ ConnectorProfileService
     ──ISecretStore.SetAsync──▶ cofre              (o valor)
     ──IConnectorProfileStore.UpsertAsync──▶ banco (só a referência kv:<nome>)
GET /connector ──▶ settings sem referências + secrets: { caminho → configurado, data }   (nunca o valor)
```

- **Campo de escrita:** `clientSecret`, `secret`, `password`, `senha`, `apiKey`, `token`, `accessToken`, em qualquer
  nível das três settings, sem distinguir maiúscula nem `_`/`-`. A lista vale para o que é **gravado**, e não para o que
  é recebido: recebido, o valor é o caminho certo; gravado, ele nunca aparece.
- **A ordem:** validar as três settings antes de escrever qualquer coisa (JSON válido, nenhum `*Ref` na requisição, o
  caminho cabe num nome de cofre); gravar cada campo com valor no cofre; trocar o campo por `<campo>Ref`; manter a
  referência gravada de cada campo ausente (só para o mesmo adapter, porque as de outro adapter são de outro schema);
  gravar o perfil; avisar os observadores.
- **A referência é do servidor:** `fh-{tenant}--{inbound|outbound|support}--{caminho}--{campo}`, em minúsculas. O
  separador é duplo porque os ids de tenant têm hífen: com hífen simples, `fh-tenant-` seria prefixo de `fh-tenant-a-…`,
  e a checagem de dono aceitaria o segredo de outro tenant. Nenhum segmento pode conter `--`, então `fh-{tenant}--` é
  exato. Um `*Ref` vindo da requisição é recusado com 400: aceitá-lo deixaria o tenant-b apontar para a credencial do
  tenant-a, o que é injeção pela credencial, no espírito do ADR-0028.
- **A leitura nunca devolve o valor, nem parte dele, nem a referência.** O mapa `secrets` diz se está configurado e a
  data da versão atual, pelos metadados, sem ler o valor. Os últimos 4 exigiriam ler o segredo a cada `GET`, ou
  persistir um fragmento dele.
- **A recusa do valor cru persistido:** o adapter recusa, na seção ativa, um campo de escrita com valor, uma referência
  malformada e uma referência fora do prefixo do tenant, mesmo que tenham chegado ao banco por SQL ou seed. O motivo
  cita o campo e nunca repete o valor, nem a referência (que nomearia o outro tenant).
- **Os tipos que carregam o valor** (`ConnectorProfileRequest`, a credencial resolvida, o token) não o imprimem no
  `ToString`. O host não liga log de corpo de requisição.
- **Chamados ausentes do `PUT`** mantêm o adapter e as settings gravados. Antes, salvar pela tela apagava o suporte.

### 5. O cofre: a porta com escrita, e o mesmo adapter em dev e em produção

A porta `ISecretStore` (Application) tem `GetAsync`, `SetAsync` e `DescribeAsync`. O único adapter é o
`KeyVaultSecretStore`, sobre o `SecretClient` oficial, com cache curto do valor (`ValueCacheSeconds`, 300) invalidado
na gravação. Ele recusa, antes de chamar o cofre, qualquer nome fora de `fh-`.

A decisão de dev teve dois critérios: **o caminho da tela é idêntico em dev e em produção**, e **o que está em disco ou
no banco nunca é o segredo em claro**.

| Opção | Mesmo caminho | Nada em claro | Veredito |
|---|---|---|---|
| Emulador da API do Key Vault, em memória, pelo mesmo adapter | sim | sim | **escolhida** (Lowkey Vault, versão fixada) |
| Store cifrado com Data Protection, no SQL ou em disco | não: outro adapter em dev | parcial: o anel de chaves em disco | recusada |
| Key Vault de dev real, no Azure | sim | sim | recusada para o dia a dia; fica como a prova de staging |
| user-secrets, variável de ambiente | não: a aplicação não escreve neles | não: texto puro no perfil do usuário | recusada |
| "Em dev, aceita valor cru" | não | não | recusada |

- **O que muda entre dev e produção** são três configurações (`SecretStore:VaultUri`, `Credential`,
  `EmulatorCertificateThumbprint`), e as duas de emulador são recusadas na subida fora do loopback.
- **A verificação do desafio de autenticação** do SDK (o recurso do desafio bate com o domínio do cofre) não é
  configuração: é desligada só quando a URI do cofre é de loopback, e ligada para qualquer outro endereço.
- **A validação de TLS nunca é desligada:** em loopback, aceita só o certificado padrão do emulador, fixado pela
  impressão.
- **O emulador roda sem volume, sem import e sem export,** porque a persistência dele grava os segredos em claro no
  disco. O preço: reiniciar o container apaga os segredos. A tela mostra "não configurado", o envio falha apontando
  para ela, e o dev digita de novo.

### 6. Requisito de provisionamento: a escrita do host só alcança segredos de conector

A identidade do host passa a gravar no cofre. Um host comprometido poderia sobrescrever os segredos que alcança, e por
isso a política nunca pode alcançar um segredo que não seja de conector. Cada ambiente de cliente MUST ter:

1. **um papel sob medida,** e não o "Key Vault Secrets Officer" inteiro, com só `…/secrets/getSecret/action`,
   `…/secrets/setSecret/action` e `…/secrets/readMetadata/action`. Sem apagar, expurgar, backup nem restore;
2. **uma condição ABAC na atribuição,** que restringe as três ações ao prefixo `fh-`: o `setSecret` pelo atributo da
   requisição (o segredo pode não existir ainda), e o `getSecret` e o `readMetadata` pelo do recurso. Em minúsculas,
   porque o Key Vault normaliza os nomes;
3. **um cofre dedicado aos segredos de conector,** sem a chave do JWT, o SQL ou o Service Bus. É defesa em profundidade,
   e o plano B se a condição (em preview) não puder ser usada;
4. **a verificação em staging, antes do primeiro cliente:** a identidade grava e lê um `fh-…` e recebe `ForbiddenByRbac`
   em outro nome. A conferência inclui o `DescribeAsync`, que lê as versões de um nome conhecido e não lista o cofre.

A guarda `fh-` do adapter não substitui a política. Ela serve para um defeito nosso aparecer em teste, e não como
`ForbiddenByRbac` em produção.

### 7. As falhas de autenticação

| Onde | Resposta | Desfecho |
|---|---|---|
| Endpoint de token | 400 ou 401 | rejeição "Configuração do conector: a plataforma recusou a credencial do tenant 'x' no ambiente 'y' (HTTP 401: invalid_client — <descrição redigida>)" |
| Endpoint de token | 2xx sem `access_token` | rejeição "… respondeu sem token" |
| Endpoint de token | 5xx, 429, rede | exceção, retry nativo |
| Envio | 403, ou 401 com token recém-emitido | rejeição "a plataforma negou acesso ao tenant 'x' no ambiente 'y' (HTTP …): <motivo>" |
| Envio ou consulta | 401 com token do cache | `Invalidate` e exceção, retry nativo; a próxima tentativa pede outro token |
| Envio | 2xx sem identificador reconhecível | rejeição "a plataforma respondeu HTTP 201 com sucesso, mas sem identificador… pode ter sido aceito e não será reenviado automaticamente" |

- **Por que a rejeição, e não a dead-letter:** a dead-letter registra `MaxDeliveryCountExceeded`, e não o motivo da
  plataforma. Retentar credencial pode bloquear a conta, e não muda permissão. É a revisão do §2 do ADR-0026.
- **O aceite sem identificador não é retentado:** retentar mandaria de novo um documento talvez já aceito. A resposta
  inteira fica na foto.
- **A recusa lembrada.** A recusa da credencial fica guardada pela chave do §3 por `CredentialRefusalHold` (5 min). Sem
  ela, cada nota em voo seria uma tentativa de login com a credencial errada, que é o padrão que bloqueia conta. Uma
  credencial nova (outro `clientId` ou outro segredo) não herda a recusa.
- **Esquecida ao salvar.** Gravar o perfil do tenant (ou só um segredo dele, mesmo que o upsert falhe depois) avisa os
  `IConnectorProfileObserver`. O adapter esquece a recusa e os tokens daquele tenant, em todos os ambientes. Salvar sem
  mudar nada também vale, porque a correção pode ter sido do lado da plataforma. Os outros tenants não são afetados.
  Uma gravação recusada na validação não esquece nada.

### 8. A quarta foto

- **Porta:** `IProcessingTrace.SaveResponseAsync(tenant, chave, destino, troca, json)`, com as trocas `submit` e `status`.
  Os nomes são `{destino}.response.submit.json` e `{destino}.response.status.json`, sob o prefixo do documento
  (`TracePaths`), e por isso o `/trace` e o zip as incluem sem regra nova.
- **Quando:** toda resposta do envio, em qualquer status, logo depois do `SendAsync` e antes de classificar; toda
  consulta com corpo (204 e o 404 pendente não sobrescrevem). O endpoint de token nunca é fotografado.
- **Envelope:** `request` com só o método e a URL sem query; `response` com status, momento, a lista fechada de
  cabeçalhos (`Content-Type`, `Date`, `X-Correlation-Id`, `X-Request-Id`, `Request-Id`, `traceparent`) e o corpo;
  `redactions`. É o mesmo `PlatformResponseEnvelope` que a sonda do sandbox grava.
- **Redação num ponto só,** o `SensitiveText`, aplicado ao corpo cru antes da foto, do motivo e de qualquer log: por
  valor (o token em uso; no endpoint de token, o segredo), por padrão (`Bearer <valor>`) e, no JSON, por nome
  (`authorization`, `access_token`, `token`, `refresh_token`, `id_token`, `client_secret`, `secret`, `password`, `senha`,
  `api_key`). O custo aceito é esconder um campo legítimo chamado `token`; o marcador fica visível e a contagem sobe.
- **Logs HTTP:** os dois clientes (envio e token) usam `RedactLoggedHeaders(_ => true)`, sem depender do padrão do
  framework.
- **Melhor esforço, e a assimetria.** A foto da resposta é a primeira gravação depois da requisição. Se a falha nela
  propagasse, o Service Bus reentregaria, e a esteira reenviaria um documento que a plataforma talvez já aceitou. Por
  isso ela é envolvida em `try/catch`, logada sem conteúdo, e o desfecho segue. A foto do payload de destino continua
  antes do envio e continua interrompendo, porque nada foi mandado ainda.
- **O dashboard** classifica as fotos pelo nome, nesta ordem: `source.*` (a fonte do D365 é JSON), `domain.json`, as duas
  respostas e o `<destino>.json`. O detalhe do documento ganha a aba "Resposta".
- **Só o tenant dono vê as fotos,** pelo ADR-0028.

### 9. A sonda do sandbox

`tools/AvalaraSandboxProbe` é a ferramenta do teste manual e do experimento do campo omitido, e não a esteira: manda
variantes do payload que o mapper não produz. Reusa o perfil do banco de dev, o cofre (só leitura), o provider, a
redação e o envelope, com a configuração do Host. `token` diz se obteve, os campos e o `expires_in`, sem imprimir o token;
`send` aplica `--omit`/`--set` no topo, acrescenta `#sufixo` à referência e grava a resposta; `get` lê de volta. A saída
fica em `out/`, redigida e fora do Git.

## Alternativas consideradas

- **Manter o no-op como padrão e ligar o real no Host.** Seria uma linha, mas o padrão silencioso é exatamente o defeito:
  qualquer composição nova (um worker, um teste esquecido) herdaria o "sem autenticação".
- **O segredo por user-secrets ou variável de ambiente em dev.** Era a decisão anterior. A aplicação não escreve neles,
  a tela não teria para onde gravar, e o fluxo de dev seria outro que o de produção.
- **O provider de configuração do Key Vault** (segredos carregados no `IConfiguration` na subida). É só leitura e só
  recarrega em intervalo: a gravação pela tela não teria efeito.
- **O cliente mandar a referência.** Deixaria um tenant apontar para o segredo de outro.
- **Um validador de settings por adapter, registrado por nome.** Conheceria o schema inteiro, mas é uma porta a mais para
  uma regra que só olha nomes. Entra com o primeiro adapter que precisar de validação de schema na escrita.
- **Tratar 401 e 403 como transitórios, como antes.** O motivo se perde na dead-letter, e a retentativa pode bloquear a
  conta.
- **Um script com `curl` no lugar da sonda.** Teria o próprio OAuth e a própria redação, e a evidência seria redigida
  por uma regra que o teste não cobre.

## Consequências

**Melhora**

- **Nenhum envio sai sem autenticação,** e cada um leva a credencial do próprio tenant, só para o endereço do ambiente
  dele.
- **O Admin configura o segredo pela tela,** em dev e em produção, pelo mesmo caminho. Nada em claro no banco, no disco,
  no seed ou no repositório.
- **O motivo de uma recusa de credencial chega ao dashboard,** em vez de sumir na dead-letter, e a conta não é
  martelada.
- **A resposta da plataforma é vista na íntegra,** no dashboard e no zip, sem token nem credencial.

**Piora**

- **A identidade do host grava no cofre,** o que exige o provisionamento do §6 em cada cliente.
- **Reiniciar o emulador de dev apaga os segredos.** É visível e alto, e o dev digita de novo.
- **Um emulador de terceiro no dev.** RBAC, soft-delete e limites podem diferir do real; a prova é em staging.
- **O estado da recusa e do cache é por processo.** Com mais de uma instância, as outras esquecem pelo intervalo.
- **Um banco de dev existente** guarda as referências antigas (`kv:avalara-a-…`), que o adapter recusa. Basta gravar o
  segredo pela tela, ou regravar as settings (docs/RUNNING.md).

**Pendências** (registradas em `docs/STATUS.md`)

- **A premissa de autenticação** (§3), no teste manual.
- **O provisionamento do cofre** (§6), no checklist do primeiro cliente, com a prova em staging.
- **A correção do payload** a partir das respostas reais é a próxima fatia.

## Conferido no ambiente local

2026-09-27, com o host, o mock autenticado, o SQL e o emulador do cofre locais, logado como `admin@fiscalhub.local`
(tenant-a).

- **O segredo pela tela:** o `PUT /connector` com `sandbox.clientSecret` respondeu 204. O banco ficou só com
  `"clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret"`, sem o valor. O `GET /connector` trouxe as
  settings sem nenhuma referência e `outbound.sandbox.clientSecret: { configured: true, updatedOn }`, sem o valor. As
  referências antigas do banco de dev apareceram como "não configurado", sem ir ao cofre.
- **O envio autenticado:** uma nota pelo `/ingest` foi enviada ao mock com o token emitido por ele, e o `/trace` trouxe
  `avalara.response.submit.json` com status 200 e o identificador.
- **A sonda contra o mock:** `token` obteve o token (campos `access_token`, `token_type`, `expires_in`; 3600), sem
  imprimi-lo; `send` com `--omit` e com `--set`, com `--poll`, e `get`. Os arquivos de `out/` saíram redigidos (o
  `access_token` como `[redigido]`) e fora do `git status`.
- **Os logs do host** não tiveram o segredo nem `Bearer` com valor.

## Validação no sandbox

Pendente: a verificação da premissa (tarefa 15.3), as 5 NF-e 55 (16) e o experimento do campo omitido (17). O resultado
entra aqui.
