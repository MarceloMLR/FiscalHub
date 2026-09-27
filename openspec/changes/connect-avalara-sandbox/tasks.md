A change tem duas partes (design D19):

- **Parte 1 (grupos 1 a 4): o limite de tenant.** São correções de segurança que não dependem da Avalara. A parte
  é commitada e mergeada sozinha, no fim do grupo 4 (#58).
- **Parte 2 (grupos 5 em diante): a Avalara.** Não há portão antes do código. A premissa de autenticação
  (`client_credentials`, segredo no corpo do pedido) é assumida e só se confirma no teste manual, na tarefa 15.3.
  Se estiver errada, o retrabalho fica nos grupos de autenticação (8, 9, 10 e o mock do 12), e a parte 1 já está
  na `main`.

## 1. Fotos só para o tenant dono (D10, `tenant-boundary`)

- [x] 1.1 Testes primeiro (Application.Tests) do `TraceArchive.Zip`: todos os arquivos entram, com o nome de
  cada um. Os testes do `SupportTicketService` continuam verdes sobre a função extraída
- [x] 1.2 Extrair do `SupportTicketService.BuildNoteZipAsync` o `TraceArchive.Zip`, em `Application/Support`,
  e usá-lo no suporte
- [x] 1.3 Testes primeiro (Application.Tests) do `DocumentTraceQuery`, com um `INoteTraceReader` falso que conta
  as chamadas e um `ITenantContext` falso:
  - outro tenant dá "não encontrado", com o leitor nunca chamado;
  - o próprio tenant recebe os arquivos;
  - o documento sem fotos dá o mesmo "não encontrado"
- [x] 1.4 Criar o `DocumentTraceQuery` em `Application/Tracing`, sobre o `INoteTraceReader` que já existe.
  Nenhuma porta nova
- [x] 1.5 Host:
  - o `/trace/{tenantId}/{naturalKey}` e o `/documents/{tenantId}/{naturalKey}/download` passam pelo
    `DocumentTraceQuery`;
  - a listagem manual do Blob sai do `Program.cs`;
  - os dois respondem o mesmo 404 e o mesmo corpo para outro tenant e para documento inexistente;
  - o download monta o zip pelo `TraceArchive.Zip`
- [x] 1.6 `dotnet build` com 0 warnings e `dotnet test` verde

## 2. Ingestão no tenant do login, com o locator restrito (D18, `tenant-boundary`)

- [x] 2.1 Testes primeiro (Inbound.Xml.Tests) do `CheckLocator` do source XML:
  - `traces/tenant-a/…` recusado numa referência do tenant-a, e também numa do tenant-b, pela regra própria;
  - `nfe/tenant-a/x.xml` recusado numa referência do tenant-b;
  - o outro container é recusado;
  - `nfe/tenant-b/../tenant-a/x.xml` é recusado, e também o `.`, o `\` e o arquivo vazio;
  - `nfe/tenant-b/x.xml` é aceito numa referência do tenant-b
- [x] 2.2 Testes primeiro: o `FetchAsync` do XML confere o locator antes de ler. Na recusa, o leitor de blob
  nunca é chamado, e a exceção nomeia a regra
- [x] 2.3 Acrescentar `CheckLocator` ao `IInboundSource<T>`:
  - no XML, a regra do D18, com o container de entrada como opção do adapter;
  - no D365, pelo `D365DocumentLocator.Parse`, com um teste;
  - no `FetchAsync` do XML, a conferência antes da leitura
- [x] 2.4 Testes primeiro (Application.Tests) da `ManualIngestion`, com fila, resolvedor e `ITenantContext`
  falsos:
  - a referência sai com o tenant do contexto e a origem `Xml`;
  - o locator recusado devolve a regra e não enfileira
- [x] 2.5 Implementar a `ManualIngestion` em `Application/Inbound`. O `/ingest` passa por ela e responde 400
  com a regra, ou 202. O `TenantId` sai do `IngestRequest`
- [x] 2.6 Seed e catálogo em `nfe/tenant-a/…`: `LocalSeed` (e a leitura da amostra no `/drop`) e os locators do
  `LocalDocumentDiscovery`, com os testes da descoberta ajustados
- [x] 2.7 Testes primeiro (BlobDrop.Tests): `{tenant}/{chave}.xml` é ingerido; a raiz e os três segmentos não
  são, e ficam no drop, com o aviso. O teste "sem barra, usa o tenant padrão" passa a afirmar o contrário
- [x] 2.8 Implementar no `DropBlobNaming` e no `BlobDropWatcher`: sem `DefaultTenant`, com o aviso uma vez por
  nome, por processo. O `/drop/{key}` passa a gravar em `drop/{tenant do login}/{key}.xml`
- [x] 2.9 `dotnet build` com 0 warnings e `dotnet test` verde

## 3. Recursos por identificador e campos mortos (D18)

- [x] 3.1 Teste primeiro (Infrastructure.Tests, `SqlScheduleStoreTests`): o `DeactivateAsync` com o contexto do
  tenant-b, sobre um agendamento do tenant-a, devolve "não encontrado" e deixa o agendamento ativo
- [x] 3.2 `DeactivateAsync` filtrando pelo tenant do contexto, como o `UpdateAsync` e o `ReactivateAsync`, e
  devolvendo se achou. O `POST /schedules/{id}/deactivate` responde 404 quando não acha
- [x] 3.3 Tirar o `TenantId` do `ManualIntegrationRequest` e do `ScheduleRequest`, e o comentário "tenant nulo
  cai no de dev". No dashboard, tirar o `tenantId` do `ManualIntegrationRequest` e do `CreateScheduleRequest`
  em `types.ts`
- [x] 3.4 `dotnet build` com 0 warnings, `dotnet test` verde e o build do dashboard

## 4. Limite de tenant: documentação, conferência e merge sozinho (D16, D18, D19)

- [x] 4.1 Escrever `docs/adr/0028-limite-de-tenant.md`, pelo template:
  - a regra;
  - a diferença entre vazamento, injeção, leitura lavada pela esteira e interferência;
  - a varredura;
  - a regra do locator, com `traces` nunca como origem;
  - o drop sem tenant padrão;
  - as pré-condições do drop e da fila;
  - a pergunta do `/ingest` em produção;
  - o cabeçalho "Revisa: ADR-0018; ADR-0009"
- [x] 4.2 ADR-0018 e ADR-0009:
  - a linha "Revisado por" no cabeçalho;
  - a nota em citação no ponto exato (tabela do 0028 no D16). No 0018, a nota vai logo depois da ressalva dos
    endpoints de debug;
  - no índice, "Aceito — revisado pelo 0028", e a linha do 0028
- [x] 4.3 `docs/RUNNING.md`: o §4 com `nfe/tenant-a/nfe-exemplo.xml`, sem `tenantId` e com o token do login; e o
  aviso de que arquivo na raiz do drop não é ingerido
- [x] 4.4 `docs/STATUS.md`:
  - a parte 1 entregue;
  - as pré-condições para abrir o drop e a fila a cliente;
  - a pergunta "o `/ingest` deve existir em produção?";
  - o diretório de empresas sem tenant
- [x] 4.5 Conferência manual com o usuário do tenant-b (`beta@fiscalhub.local`):
  - o download e o `/trace` de uma chave do tenant-a dão o mesmo 404 de uma chave inexistente;
  - o `/ingest` com `nfe/tenant-a/…` ou `traces/…` dá 400;
  - o `/ingest` com `tenantId: tenant-a` no corpo cai no tenant-b;
  - o `deactivate` de um agendamento do tenant-a dá 404 e não desativa;
  - o `/drop` grava no prefixo do tenant-b
- [x] 4.6 Ponto de merge da parte 1:
  - `dotnet build` com 0 warnings, `dotnet test` verde e o build do dashboard;
  - commit e PR só com os grupos 1 a 4, e com a pasta da change, que segue ativa;
  - merge na `main`;
  - a parte 2 continua num branch a partir da `main` atualizada

## 5. Prova do emulador do cofre (D3)

Manual e sem código.

- [x] 5.5 Emulador do cofre (D3):
  - subir o Lowkey Vault numa versão fixada, sem volume, sem import e sem export;
  - provar pela API REST do Key Vault, com curl e uma credencial falsa, que funcionam o set, o get e a leitura
    das versões (os metadados sem o valor);
  - reiniciar o container e conferir que o segredo sumiu, ou seja, que nada ficou em disco;
  - anotar a versão, a porta e a impressão do certificado no design (D3).

  Se o Lowkey não fizer a ida e volta, repetir com o plano B (james-gould, `Persist=false`). Se nenhum servir,
  `/opsx:update` antes de qualquer código da parte 2

## 6. A porta do cofre, com escrita, e o mesmo adapter em dev e em produção (D3, D4)

- [x] 6.1 Testes primeiro (Application.Tests):
  - o `SecretReference` aceita `kv:<nome>` e recusa a referência sem prefixo, o nome vazio, o nome com mais
    de 127 caracteres e o caractere fora de `[0-9A-Za-z-]`;
  - o `SecretNames.For(tenant, settings, caminho, campo)` deriva `fh-tenant-a--outbound--sandbox--clientsecret`, só
    com o que o cofre aceita, em até 127 caracteres, sempre no prefixo do tenant
- [x] 6.2 Criar em `Application/Connectors`:
  - o `SecretReference` e o `SecretNames`, puros;
  - o `ISecretStore`, com `GetAsync` (vazio conta como ausente), `SetAsync` e `DescribeAsync` (existe e a data,
    sem o valor)
- [x] 6.3 Testes primeiro (Infrastructure.Tests) do `KeyVaultSecretStore`, sobre um `SecretClient` escrito à mão:
  - o set grava uma versão;
  - o get devolve o valor, e o ausente devolve `null`;
  - o describe usa só os metadados, sem ler o valor;
  - o valor lido fica em cache pelo intervalo, e o set invalida o cache;
  - a falha do cofre vira exceção sem o valor na mensagem;
  - o store recusa gravar ou ler nome fora do prefixo `fh-`, antes de chamar o cofre
- [x] 6.4 Implementar o `KeyVaultSecretStore` na Infrastructure, com o `Azure.Security.KeyVault.Secrets` e o log
  de conteúdo do SDK desligado
- [x] 6.5 Testes primeiro (Infrastructure.Tests) da montagem das opções do cliente do cofre:
  - `DisableChallengeResourceVerification` é `true` só com URI de loopback (`localhost`, `127.0.0.1`, `::1`);
  - com `https://<cofre>.vault.azure.net/`, ela é `false`;
  - nenhuma chave de configuração a liga;
  - a credencial `Emulator` ou a impressão fixada com URI fora do loopback é recusada na subida
- [x] 6.6 Host:
  - a seção `SecretStore` (`VaultUri`, `Credential`, `EmulatorCertificateThumbprint`, `ValueCacheSeconds`);
  - a verificação do desafio derivada da URI, e nunca de configuração;
  - em dev, no `appsettings.Development.json`, o emulador em loopback com o certificado fixado;
  - registrar o `KeyVaultSecretStore`
- [x] 6.7 `docker-compose.yml`: o emulador do cofre na versão conferida na 5.5, sem volume, com um comentário
  dizendo por que a persistência fica desligada
- [x] 6.8 Teste de integração opt-in (só com a variável de ambiente, no padrão do teste de integração do D365):
  a ida e volta set → describe → get contra o emulador, pelo `KeyVaultSecretStore`
- [x] 6.9 Migrar o adapter D365, no mesmo comportamento e com os testes ajustados à porta falsa:
  - o `D365InboundSettings` e o `ClientCredentialsD365TokenProvider` passam a usar o parser e o
    `ISecretStore.GetAsync`;
  - o `SecretReference` interno do D365 sai
- [x] 6.10 `dotnet build` com 0 warnings e `dotnet test` verde

## 7. O segredo pela tela: gravação e leitura do perfil (D3, D5, D7)

- [x] 7.1 Testes primeiro (Application.Tests) do `ConnectorProfileService.SaveAsync`, com cofre, store e observador
  falsos e um logger que captura:
  - `"sandbox": {"clientSecret": "s3cr3t"}` grava `s3cr3t` no cofre com o nome derivado, e o perfil gravado tem
    só `clientSecretRef`, sem campo de escrita;
  - os nomes da lista (`clientSecret`, `secret`, `password`, `senha`, `apiKey`, `token`, `accessToken`, sem
    distinguir maiúscula nem `_`/`-`), em qualquer nível, são campos de escrita nas três settings;
  - o campo de escrita ausente mantém a referência já gravada naquele caminho, sem tocar o cofre;
  - um `*Ref` na requisição (inclusive um do prefixo de outro tenant) dá a lista de problemas, sem escrita
    nenhuma;
  - o JSON inválido dá a lista de problemas, sem escrita no cofre;
  - a falha do cofre deixa o perfil intacto, com mensagem sem o valor;
  - os observadores são avisados depois da escrita, e também quando o upsert falha depois de uma escrita no
    cofre;
  - a gravação recusada na validação não avisa;
  - nenhum log nem mensagem contém `s3cr3t`
- [x] 7.2 Testes primeiro (Application.Tests) da leitura do perfil:
  - as settings voltam sem os `*Ref`;
  - o mapa `secrets` traz `configured: true` e a data da versão atual;
  - a referência sem valor no cofre dá `configured: false`;
  - a resposta nunca contém o valor, nem parte dele, nem a referência
- [x] 7.3 Implementar em `Application/Connectors` o `IConnectorProfileObserver` e o `ConnectorProfileService`, com a
  gravação e a leitura. Registrar no Host
- [x] 7.4 Host:
  - o `PUT /connector` pelo `SaveAsync`, com 400 `{ message }` para a lista não vazia;
  - o `GET /connector` pela leitura mascarada;
  - o `ToString` do `ConnectorProfileRequest` sem as settings, com um teste;
  - nenhum log de corpo de requisição ligado
- [x] 7.5 Dashboard:
  - no `adapterSchemas.ts`, os campos "de referência" viram campos de escrita (`secret: true`), de todos os
    adapters;
  - a tela de conectores mostra cada um como campo de senha, sem preenchimento: "configurado em <data>" ou
    "não configurado", pelo mapa `secrets`;
  - o valor só é enviado quando digitado, e nunca é lido de volta;
  - a mensagem do 400 aparece ao salvar
- [x] 7.6 Teste (Infrastructure.Tests): os perfis do `EnsureDevConnectorProfilesAsync`, em SQLite, não têm campo de
  escrita, e toda referência está no prefixo do tenant do perfil
- [x] 7.7 `dotnet build` com 0 warnings, `dotnet test` verde e o build do dashboard

## 8. Credencial e URLs pela seção do ambiente (D2)

- [x] 8.1 Testes primeiro (`AvalaraOutboundSettingsTests`):
  - a credencial e as URLs da seção ativa;
  - `tokenUrl` ausente = `baseUrl` + `TokenPath`;
  - faltam `baseUrl`, `clientId` ou o Client Secret: rejeição nomeando o campo e apontando para a tela;
  - `http` fora do loopback é recusado, e o loopback é aceito;
  - campo de escrita com valor na seção persistida é recusado, e o motivo não traz o valor;
  - referência malformada ou fora do prefixo do tenant é recusada sem ler o cofre;
  - `clientTokenRef` é ignorado
- [x] 8.2 Estender o `AvalaraOutboundSettings`:
  - credencial, `BaseUri` obrigatória e endpoint de token;
  - a regra `https`/loopback;
  - a recusa do valor cru persistido e da referência fora do prefixo (D5)
- [x] 8.3 `AvalaraOptions` sem `ClientId`, `ClientSecret` e `BaseUrl`. Tirar o `ResolveBaseAddress` e o
  fallback do `BaseOf`. Os clientes HTTP ficam sem `BaseAddress`, com URIs absolutas. O `DocumentsPath` continua
  opção do adapter, e o valor real é conferido no teste manual (15.3)
- [x] 8.4 `appsettings.json` sem `Avalara:BaseUrl`, e o `Program.cs` sem o `options.BaseUrl`
- [x] 8.5 Seed de dev:
  - as seções com `clientId` e as referências no formato `fh-{tenant}--…`, sem `clientTokenRef`;
  - o sandbox do tenant-a apontando para o mock, com `clientId: "mock-client"`;
  - nenhum valor de segredo, nem de mentira
- [x] 8.6 Dashboard (`adapterSchemas.ts`): o Avalara com `baseUrl`, `tokenUrl`, `clientId` e `clientSecret` (campo
  de escrita), sem `clientTokenRef`
- [x] 8.7 Ajustar os testes do dispatcher e o ponta a ponta ao perfil com credencial. `dotnet build` com
  0 warnings e `dotnet test` verde

## 9. Provider de token por credencial (D1, D6, D7)

- [x] 9.1 Testes primeiro (`AvalaraTokenProviderTests`, adaptados):
  - reuso;
  - renovação dentro da margem;
  - concorrência com uma única busca;
  - dois tenants com credenciais diferentes não compartilham token, e dois tenants com a mesma credencial
    também não;
  - a troca de ambiente e a rotação do segredo buscam token novo;
  - sem `expires_in`, o token não entra no cache;
  - `Invalidate`;
  - `IsFresh` verdadeiro só na busca;
  - o pedido de token vai com `client_secret` no corpo do formulário, a premissa assumida (D13)
- [x] 9.2 Testes primeiro das falhas do endpoint de token:
  - 400 e 401 viram `DispatchRejectedException` com tenant, ambiente e código do erro;
  - 5xx e 429 viram exceção transitória;
  - 2xx sem token vira rejeição;
  - a recusa é lembrada: cinco chamadas fazem um único pedido;
  - uma credencial nova não herda a recusa;
  - o intervalo vence e a recusa se desfaz;
  - o segredo ausente, o vazio e o que o cofre não tem dão rejeição que aponta para a tela, e zero pedidos
- [x] 9.3 Testes primeiro do `Forget(tenantId)`:
  - depois de uma recusa, o `Forget("tenant-a")` faz a chamada seguinte pedir token na hora, dentro do
    intervalo e com a mesma credencial;
  - os tokens do tenant-a em cache também são esquecidos;
  - a recusa e os tokens do tenant-b continuam
- [x] 9.4 Refazer o `IAvalaraTokenProvider` e o `AvalaraTokenProvider`:
  - a assinatura por `AvalaraOutboundSettings`;
  - `AvalaraAccessToken` (`Value`, `IsFresh`, chave);
  - a chave com a impressão SHA-256 do segredo;
  - `CredentialRefusalHold` em `AvalaraOptions`;
  - `Forget`;
  - o no-op devolvendo "sem token" explícito
- [x] 9.5 Observador do adapter: um `IConnectorProfileObserver` que chama `Forget(tenantId)`, registrado em
  `AddAvalaraComplianceDispatcher`. Um teste de DI prova que o `ConnectorProfileService` composto com o
  adapter chega ao `Forget`
- [x] 9.6 Testes: o `ToString` do `AvalaraAccessToken` e da credencial resolvida não contém o valor
- [x] 9.7 DI:
  - o provider real passa a ser o padrão em `AddAvalaraComplianceDispatcher`;
  - `UseAvalaraWithoutAuthentication()` troca pelo no-op e loga um aviso;
  - o `AddAvalaraTokenProvider()` sai;
  - `RedactLoggedHeaders(_ => true)` nos dois clientes.

  Com um teste de DI para o padrão e para o pedido explícito
- [x] 9.8 `dotnet build` com 0 warnings e `dotnet test` verde

## 10. Dispatcher: autenticação e desfecho (D7, delta de `compliance-dispatch-outcome`)

- [x] 10.1 Testes primeiro (`AvalaraComplianceDispatcherTests`):
  - o `Authorization` de cada envio é o do tenant;
  - o segredo ausente não faz nenhuma requisição;
  - o 403 vira rejeição de configuração com o motivo da plataforma, com um único POST;
  - o 401 com token fresco vira rejeição de configuração;
  - o 401 com token do cache invalida e lança transitório, e a tentativa seguinte busca token novo;
  - o 401 na consulta invalida;
  - o 2xx sem identificador (corpo vazio, não JSON, JSON sem `id`) vira rejeição "pode ter sido aceito", com
    um único POST
- [x] 10.2 Implementar no `AvalaraComplianceDispatcher`:
  - o token com `IsFresh`;
  - a classificação do D7;
  - o corpo lido uma vez como texto
- [x] 10.3 `dotnet build` com 0 warnings e `dotnet test` verde

## 11. Quarta foto, redação e a aba Resposta (D8, D9, D10)

- [x] 11.1 Testes primeiro do `SensitiveText`:
  - por valor (token e segredo);
  - `Bearer <valor>` em texto e em JSON;
  - as propriedades de nome sensível, em qualquer nível;
  - a contagem de redações;
  - o texto sem nada sensível fica igual
- [x] 11.2 Implementar o `SensitiveText` no adapter. A `PlatformMessage` passa a receber o corpo já redigido
- [x] 11.3 Acrescentar à porta e às duas implementações:
  - `SaveResponseAsync` no `IProcessingTrace` e no `NoOpProcessingTrace`;
  - `TracePaths` na Infrastructure, com os nomes `{destino}.response.submit.json` e
    `{destino}.response.status.json`;
  - o `BlobProcessingTrace` gravando pelo `TracePaths`.

  Com testes de que os nomes são distintos e ficam sob o prefixo do documento (Infrastructure.Tests), e de que o
  `TraceArchive.Zip` leva as duas respostas com nomes distintos (Application.Tests)
- [x] 11.4 Testes primeiro (dispatcher, com um trace que grava e um logger que captura):
  - a foto do envio em 200, 400 e 503, com status, URL sem query, os cabeçalhos da lista e o corpo;
  - `Set-Cookie` e `WWW-Authenticate` ausentes;
  - a foto da consulta com corpo sobrescreve, e o 204 não;
  - a plataforma ecoa o `Bearer` e manda `access_token` no corpo: a foto, o motivo, o log e a mensagem da
    exceção não têm o token;
  - o endpoint de token recusado com o segredo ecoado na descrição: nem o motivo nem o log têm o segredo;
  - o trace que falha ao gravar a resposta de um aceite: o recibo sai normal, com um único POST e um aviso
    no log sem conteúdo
- [x] 11.5 Implementar no dispatcher o envelope do D8 e a gravação por melhor esforço, com
  `ILogger<AvalaraComplianceDispatcher>`
- [x] 11.6 Dashboard:
  - o `shape()` do `useTrace.ts` reconhece, nesta ordem, `source.*`, `domain.json`, `*.response.submit.json`,
    `*.response.status.json` e o `<destino>.json`;
  - o `DocumentDetail` ganha a aba "Resposta", com os dois envelopes;
  - o `DocumentTrace` ganha as respostas;
  - se houver suíte de front, um teste do `shape()` com a fonte do D365 e as duas respostas
- [x] 11.7 `dotnet build` com 0 warnings, `dotnet test` verde e o build do dashboard

## 12. Mock com autenticação e ponta a ponta em memória (D11)

- [ ] 12.1 Mock:
  - `POST /oauth/token`, na forma assumida (D13): `client_credentials`, com o segredo no corpo;
  - `/admin/token/{aceitar|recusar}`;
  - `/documents*` exigindo o `Bearer` emitido pelo mock, com 401 sem ele;
  - `/admin/*` e a inspeção continuam abertos
- [ ] 12.2 `DispatchToMockTests` com a composição padrão (provider real), um `ISecretStore` em memória e o perfil
  com credencial. O segredo do teste entra pelo `ConnectorProfileService`, como pela tela. Os casos que já
  existem continuam verdes
- [ ] 12.3 Novos casos no ponta a ponta:
  - o token recusado pelo mock: rejeição com motivo e zero POSTs de documento;
  - salvar o perfil pelo `ConnectorProfileService` e reprocessar: com o mock já aceitando, o token é pedido na
    hora e a nota é enviada;
  - o segredo ausente: rejeição e zero pedidos;
  - com um trace que grava, as fotos de resposta do envio e da consulta presentes
- [ ] 12.4 `dotnet build` com 0 warnings e `dotnet test` verde

## 13. Sonda do sandbox (D12)

- [ ] 13.1 Criar `tools/AvalaraSandboxProbe`:
  - um console que referencia o adapter Avalara (`InternalsVisibleTo`) e a Infrastructure;
  - lê o perfil do banco de dev e o segredo pelo `KeyVaultSecretStore`, com a configuração de cofre do Host
    (em dev, o emulador). Só lê: nunca grava no cofre;
  - entra na solução
- [ ] 13.2 Comandos:
  - `token`: diz se obteve e o `expires_in`, e nunca imprime o token;
  - `send`, com `--omit`, `--set`, `--ref-suffix`, `--label` e `--poll`;
  - `get`: leitura de volta.

  Todos gravam em `out/` o envelope redigido
- [ ] 13.3 Pôr `tools/AvalaraSandboxProbe/out/` no `.gitignore`
- [ ] 13.4 Rodar a sonda contra o mock (`token`, `send` com `--omit` e com `--set`, `get`) e conferir que os
  arquivos de `out/` saem redigidos e não entram no `git status`
- [ ] 13.5 `dotnet build` com 0 warnings e `dotnet test` verde

## 14. Documentação da parte Avalara antes do teste manual (D3, D13, D16)

- [ ] 14.1 Escrever `docs/adr/0027-credencial-por-tenant-e-resposta-da-plataforma.md`, pelo template:
  - as decisões do D16, com o requisito de provisionamento do cofre do D3: o papel sob medida com só
    `getSecret`, `setSecret` e `readMetadata`, a condição ABAC `fh-`, o cofre dedicado e a verificação em staging;
  - o cabeçalho "Revisa: ADR-0026 §2; ADR-0006; ADR-0019"
- [ ] 14.2 ADR-0026, no formato que o 0025 recebeu do 0026:
  - a linha "Revisado por: ADR-0027 (§2)" no cabeçalho;
  - a nota `> **Revisado pelo ADR-0027 (data).**` no §2, logo depois de "**Falha transitória.** Continua como
    exceção…", dizendo que essa frase cobria o 401 e o 403 sem nomeá-los, o que mudou, por quê, e apontando
    o CNV D10 (design arquivado) como a decisão explícita revertida;
  - o índice: "Aceito — §2 revisado pelo 0027"
- [ ] 14.3 ADR-0006 e ADR-0019:
  - a linha "Revisado por" no cabeçalho;
  - a nota em citação no ponto exato (tabela do 0027 no D16);
  - no índice, "Aceito — revisado pelo 0027", e a linha do 0027
- [ ] 14.4 `docs/RUNNING.md`:
  - o emulador do cofre no `docker compose up`, em memória, e por que a persistência fica desligada;
  - o Client Secret digitado na tela, até contra o mock, e de novo a cada reinício do emulador;
  - o SQL para regravar as `OutboundSettings` do tenant-a em banco existente, com as referências `fh-{tenant}--…`;
  - o roteiro do sandbox depois da implementação (D13): a verificação da premissa pelo hub, a correção pela
    tela, as 5 notas, a conferência do zip;
  - o roteiro da sonda;
  - a credencial do sandbox distribuída fora do repositório e do chat;
  - o aviso de esperar o poll fechar antes de trocar de ambiente
- [ ] 14.5 `docs/STATUS.md`:
  - a parte 2 em andamento, sem portão antes do código;
  - corrigir as menções ao portão no `STATUS.md` (o "Próximo passo" da sessão da parte 1) e na linha reservada do
    0027 no índice de ADRs, que dizem que a parte 2 começa pelo portão;
  - o `clientTokenRef` retirado;
  - no checklist do primeiro cliente, o item de provisionamento do cofre: papel sob medida, condição ABAC `fh-`,
    cofre dedicado. A prova é em staging: a identidade do host grava e lê `fh-…` e recebe `ForbiddenByRbac` em
    outro nome

## 15. Teste manual: configuração e correção pela tela (D13)

- [ ] 15.1 Na tela de conectores, preencher a seção `sandbox` do tenant-a: `baseUrl`, `tokenUrl`, `clientId` e o
  Client Secret real, digitado no campo de escrita. O segredo real nunca vai para arquivo do repositório,
  terminal compartilhado ou chat
- [ ] 15.2 Conferir o caminho do segredo:
  - a tela mostra "configurado em <data>", e o `GET /connector` não traz o valor, nem parte dele, nem a
    referência;
  - a linha do perfil no SQL tem só o `clientSecretRef` `fh-tenant-a--…`;
  - um `PUT` feito à mão com `clientSecretRef` no corpo dá 400;
  - reiniciar o emulador faz a tela mostrar "não configurado", e o envio falha apontando para a tela
- [ ] 15.3 Verificação da premissa de autenticação (design D13, passo 1):
  - `probe token --tenant tenant-a`, e preencher a tabela "Resultado da verificação da premissa" no Context do
    design, sem credencial e sem token;
  - no primeiro envio, conferir que o caminho `documents` existe (qualquer status diferente de 404);
  - aplicar a regra do D13: um ajuste de forma (Basic, margem, `DocumentsPath`) entra com teste. Um fluxo
    estruturalmente outro para o teste manual e leva a `/opsx:update`, com o retrabalho nos grupos de
    autenticação
- [ ] 15.4 Correção pela tela:
  - com o segredo errado de propósito, uma nota recusa com o motivo;
  - salvar o perfil com o certo e reprocessar faz o token ser pedido na hora, sem esperar o intervalo

## 16. Teste manual: as 5 NF-e 55 contra o sandbox (D13, passos 3 a 5)

- [ ] 16.1 Rodar a passada das 5 NF-e 55 pelo hub, como no §7 do RUNNING.md, contra o sandbox, sem mudar o
  payload
- [ ] 16.2 Conferir no dashboard o desfecho e o motivo de cada nota, e a aba "Resposta". Nenhuma nota pode
  ficar sem motivo legível quando rejeitada, e a aba Destino continua mostrando o payload
- [ ] 16.3 Baixar o zip de cada nota e conferir as cinco fotos. Anotar toda foto com `redactions > 0`
- [ ] 16.4 Conferir nos logs do host que nenhum token, segredo ou valor de cabeçalho aparece

## 17. Experimento do campo omitido (D14)

- [ ] 17.1 Escolher a nota com menos motivos de recusa na passada 16 e baixar o payload de destino dela
- [ ] 17.2 Enviar pela sonda as variantes A (`--omit finalidadeNotaFiscal`), B (`--set finalidadeNotaFiscal=1`) e C
  (`--set finalidadeNotaFiscal=0`), cada uma com `--ref-suffix exp-<letra>` e `--poll`, e com `get` se houver
  leitura de volta
- [ ] 17.3 Aplicar a tabela de interpretação do D14. Na duplicidade por chave, registrar no item "Reenvio" e
  refazer sobre a `BRMF06-110000027`

## 18. Evidência, relatório e checklist (D13, D14, D15)

- [ ] 18.1 Curar as respostas reais, no formato do envelope e redigidas, em
  `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/Fixtures/sandbox/`: aceite, recusa no envio,
  consulta com erro e recusa de credencial, os que tiverem acontecido
- [ ] 18.2 Teste de varredura das fixtures: sem `Bearer` com valor, sem JWT, sem `access_token` ou
  `client_secret` com valor
- [ ] 18.3 Testes de reprodução:
  - o stub devolve cada resposta gravada, e o dispatcher e o ponta a ponta registram o desfecho do spec;
  - a recusa real fica `IntegrationError`, com o motivo dela, um único POST e nenhuma exceção;
  - a `PlatformMessage` sobre o formato real dá texto legível.

  Se nenhuma recusa real ocorreu, registrar como não exercitado
- [ ] 18.4 Escrever `docs/avalara-sandbox-primeiro-envio.md`:
  - ambiente, sem credencial;
  - o resultado da verificação da premissa (15.3);
  - uma linha por nota;
  - cada motivo classificado pela regra do D13 (nosso: contrato; nosso: configuração; característica do dado;
    indeterminado);
  - o experimento com as três respostas e a conclusão pela tabela;
  - as respostas às perguntas do CNV D18, só as que a evidência sustenta
- [ ] 18.5 `docs/STATUS.md`:
  - o item "Campo omitido virando 0" fechado ou reescrito com a evidência e o link;
  - os itens "Formato real do erro", "Blocos tirados do schema", "Totais de imposto" e "Reenvio" atualizados
    só pelo que a evidência mostrou;
  - a próxima fatia (correção do payload e da leitura da resposta) com as rejeições classificadas como
    "nosso"
- [ ] 18.6 `dotnet build` com 0 warnings e `dotnet test` verde. Conferir `git status` e `git diff` sem nenhum
  segredo, token ou arquivo de `out/`
