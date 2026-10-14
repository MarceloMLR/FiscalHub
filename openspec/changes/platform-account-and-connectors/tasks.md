A ordem segue o desenho: **o ambiente sai primeiro** (é a dimensão que mais atravessa o código), **a conta e o conector
depois**, **as regras da coleção depois**, e **módulos e integrações por último**. Antes de qualquer migração, entra o
harness que as prova contra SQL Server de verdade.

- **Grupo 1:** o ambiente entra pela configuração do host, sem padrão silencioso.
- **Grupo 2:** o harness de migração.
- **Grupo 3:** o ambiente sai do perfil, das settings e do teste de credencial (migração M1).
- **Grupo 4:** a conta e o conector viram linhas; a credencial e o endpoint de token sobem para a conta (migração M2).
- **Grupo 5:** as regras da coleção e o mapa de URLs base por versão.
- **Grupo 6:** módulos, integrações, o caminho do envio e o `/info` (migração M3).
- **Grupo 7:** a tela do canvas — painel do conector em abas.
- **Grupo 8:** docs.
- **Grupo 9:** verificação final.

Cada grupo de código termina com `dotnet build` com 0 warnings (TreatWarningsAsErrors) e `dotnet test` verde — o que
inclui o teste do modelo contra o snapshot das migrações, e por isso cada grupo que muda a forma persistida traz a sua
migração (design D13). Os grupos que tocam a tela terminam também com `npm test` e `npm run build`: a parte obrigatória da
tela entra no mesmo grupo que a forma de dado que ela segue (design D16). Uma tarefa só recebe `[x]` com evidência; uma
tarefa de migração, só com a saída de uma execução do harness com o SQL Server no ar — pulado não é prova (design D12).

## 1. O ambiente é da instância (`platform-account-connectors`)

- [x] 1.1 Teste primeiro: o host recusa subir sem `Connector:Environment`, e com um valor que não é `Sandbox` nem
  `Production`; a mensagem nomeia a chave e, no segundo caso, os dois valores aceitos.
- [x] 1.2 Porta na Application que devolve o ambiente da instância, lida de `Connector:Environment` na subida — a chave
  que já existe no `appsettings.json`; não criar outra. **Não é o `IHostEnvironment`:** um teste prova que um host em
  Development com `Sandbox` configurado devolve `Sandbox`, e que nada deriva um eixo do outro.
- [x] 1.3 O `/info` passa a devolver o ambiente da instância, e sai o fallback `profile?.Environment ??
  cfg["Connector:Environment"] ?? "Sandbox"` do `Program.cs`. Teste do contrato.
- [x] 1.4 O selo do dashboard já lê o ambiente do `/info`; o que sai é o padrão `info?.environment ?? 'Sandbox'` do
  `App.tsx`. Antes de o `/info` responder, ou se ele falhou, o selo não afirma ambiente nenhum. Teste da tela.
- [x] 1.5 O `RUNNING.md` documenta a chave existente. Subir sem ela falha, e é esse o ponto.

## 2. O harness de migração (design D12)

- [x] 2.1 Teste de integração contra o SQL Server do `docker compose`, opt-in por variável de ambiente no padrão do
  `KeyVaultEmulatorFact`: pulado sem ela, sem tocar a rede no CI. Um banco descartável por execução.
- [x] 2.2 O harness aplica as migrações até uma migração-alvo, grava linhas na forma daquele ponto, aplica o resto e
  entrega o banco às asserções. Ele também desfaz até uma migração-alvo, para as provas de volta: a falha do `Down` da
  M1 (3.7) e a ida e volta da M2 (4.3) e da M3 (6.3).
- [x] 2.3 Prova do harness com o que existe hoje: todas as migrações atuais aplicadas num banco vazio, sem mudança de
  modelo pendente. Evidência: a saída da execução com o SQL Server no ar.
  **Feito (2026-10-13):** com `FISCALHUB_SQL_HARNESS` apontando para o `sql` do compose, os 2 testes do harness
  passaram (8 s e 3 s); sem a variável, aparecem pulados; nenhum banco descartável sobrou no servidor.
- [x] 2.4 O `RUNNING.md` diz como rodar o harness: subir o `sql` do compose e definir a variável.

## 3. O ambiente sai do perfil, das settings e do teste de credencial (`platform-account-connectors`, `connector-secret-references`, `connector-credential-test`)

- [x] 3.1 Teste primeiro, no `SecretNames`: o nome derivado não tem segmento de ambiente, e o mesmo tenant deriva o
  mesmo nome numa instância de `Sandbox` e numa de `Production`.
- [x] 3.2 O `AvalaraOutboundSettings.Read` para de entrar na seção do ambiente: as settings de saída são um nível só, e o
  ambiente que ele cita vem da porta da instância, e não do perfil.
- [x] 3.3 O `PlatformEstablishmentResolver` tira o ambiente da chave da listagem guardada e da recusa lembrada
  (`platform-establishment-resolution`).
- [x] 3.4 A chave do cache de token perde o ambiente (`AvalaraTokenProvider`, e o ambiente que a credencial resolvida e
  o token carregam no `ToString`).
- [x] 3.5 `ConnectorProfileRequest`, `ConnectorProfileView` e `TenantConnectorProfile` perdem o `Environment`. Teste de
  que um corpo com `"environment"` é **aceito** e o campo é ignorado, e não 400.
- [x] 3.6 A cadeia do teste de credencial sem ambiente: o `CredentialTestRequest` perde o campo (corpo antigo aceito, campo
  ignorado); o `ConnectorCredentialTestService` deixa de exigi-lo na saída; o `CredentialTestBrake` chaveia por tenant e
  lado; o `CredentialTestLog` e a linha de log do host levam o ambiente da instância; o
  `IConnectorCredentialTest.TestAsync` perde o parâmetro, no `AvalaraCredentialTest` — que deixa de sobrescrever o ambiente
  do perfil — e no `D365CredentialTest`. Teste: dentro do intervalo, um corpo antigo com `"environment"` cai no freio.
- [x] 3.7 Migração M1: a seção do ambiente gravado vira as settings de saída, a outra é descartada, e a coluna
  `Environment` sai. **É irreversível:** o `Down` lança, com mensagem. Testes no harness: um perfil de `Sandbox` e um de
  `Production`; e desfazer a M1 falha com a mensagem e deixa o banco como estava. Evidência: a saída do harness.
  **Feito (2026-10-13):** `20261013160736_MoveEnvironmentToInstance`. Com `FISCALHUB_SQL_HARNESS` no `sql` do compose,
  os 3 testes da M1 passaram (a seção do ambiente gravado vira as settings, a outra some, a raiz que não é seção fica, o
  JSON inválido não é tocado; desfazer lança e o banco fica igual), junto com o teste do modelo sem mudança pendente.
- [x] 3.8 Os motivos param de citar `OutboundSettings.<ambiente>.…` e o `→ {ambiente}` do caminho da tela, no
  `AvalaraOutboundSettings`, no despachante, na listagem e no provedor de token; passam a citar o ambiente da instância
  como contexto.
- [x] 3.9 A sonda (`tools/AvalaraSandboxProbe`) acompanha a leitura nova: settings achatadas, ambiente da instância. Ela
  compila na solução, e o `dotnet build` do grupo a cobre.
- [x] 3.10 O dashboard deixa de mandar o ambiente e as seções: sai o seletor de ambiente, sai o `splitOutbound` do
  `connectorPayload.ts`, a saída vira um bloco só; o `CredentialTestButton` e o cliente da API deixam de mandar
  `environment`. Testes da tela: o payload não tem `environment` nem `sandbox`/`production`; salvar sem digitar não leva
  o segredo.
- [x] 3.11 O seed de desenvolvimento e todos os testes que atribuem `Environment` ao perfil — cerca de 25 arquivos,
  inclusive os do D365 e os de integração —, e não só os que montam settings com `sandbox`/`production`. É mecânico, e é
  onde um descuido passa.
- [x] 3.12 A trava da M1 contra dado ambíguo, e a seção achada sem distinguir maiúscula (revisão de 2026-10-13). Testes
  no harness, os quatro casos que destroem dado:
  - **chave capitalizada:** `"Sandbox"` com `Environment = 'Sandbox'` acha a seção e achata certo, e o perfil não fica
    `{}`;
  - **ambiente em branco** com as duas seções: a migração falha, e o banco fica como estava, com a coluna `Environment`
    ainda lá. O nulo do pedido original não chega ao banco: a coluna é NOT NULL desde a `AddConnectorProfiles`;
  - **`Environment = 'Hml'`**, sem seção correspondente: falha igual;
  - **perfil já achatado**, sem seção nenhuma, com `Environment` qualquer: passa intacto, e não falha.

  **Feito (2026-10-13):** os quatro passaram contra o `sql` do compose, com os sete da M1 e do harness — 11 de 11, junto
  com o teste do modelo. O teste antigo que esperava `{"extra":"fica"}` de um perfil de `Sandbox` só com a seção
  `production` afirmava a conversão no escuro: virou dois, o JSON inválido intocado e esse perfil recusado. O SQL da trava
  precisou de um `COLLATE Latin1_General_BIN2` no lado do `Environment`: a chave do `OPENJSON` é BIN2 e a coluna tem a
  collation do banco, e sem ele o batch é recusado até num banco vazio.

## 4. A conta e o conector viram linhas (`platform-account-connectors`, `avalara-tenant-authentication`, `connector-credential-test`)

- [x] 4.1 Teste primeiro: um `clientId` ou um `clientSecret` nas settings de um conector é recusado com 400 que nomeia o
  campo e diz que a credencial é da conta — o `clientId` não é campo de segredo, e a recusa dele é regra nova; um
  `apiKey` no conector é aceito e vai para o cofre; duas contas com a mesma plataforma são recusadas.
  **Feito (2026-10-13):** os testes entraram no `ConnectorProfileServiceTests` e falharam antes do código; os testes
  antigos do segredo genérico das settings de saída passaram a usar um `apiKey` no conector.
- [x] 4.2 A conta e o conector viram linhas próprias: a conta com `Platform`, `ClientId`, `ClientSecretRef` e `TokenUrl`
  tipados; o conector sob ela, com o adapter como chave, o mapa de URLs base e o resto das settings. Uma gravação sem a
  conta ou sem a lista de conectores mantém o gravado.
- [x] 4.3 Migração M2: a conta nasce do `OutboundAdapter`, com o `clientId` e o `tokenUrl` das settings achatadas quando
  elas os têm, e **sem** copiar a `clientSecretRef`; o conector nasce com a `baseUrl` como base da versão `v2` (chave
  fixa no SQL, design D8) e o resto das settings. **Tem `Down`:** remonta as settings achatadas e o `OutboundAdapter` a
  partir da conta e do conector. Testes no harness: o segredo aparece como **não configurado** depois dela, e a migração
  não fala com o cofre; settings sem `tokenUrl` dão a conta sem endpoint de token; e a ida e volta — a forma depois do
  `Down` é a de antes do `Up`. Evidência: a saída do harness.
  **Feito (2026-10-13):** `20261013170710_AddPlatformAccountsAndConnectors`. Com `FISCALHUB_SQL_HARNESS` no `sql` do
  compose, os 7 testes da M2 passaram: a conta e o conector nascem das settings achatadas; o segredo lê como não
  configurado mesmo com o nome antigo ainda no cofre; settings sem `tokenUrl` dão a conta sem endpoint; o JSON inválido
  vai ao conector como veio, e a conta fica sem credencial; o `Down` remonta as settings achatadas, e mantém a referência
  que a conta ganhou depois; e settings de saída sem `OutboundAdapter` fazem a migração falhar, com o banco como estava.
  Junto com os da M1, do harness e o do modelo sem mudança pendente: 18 de 18.
  **Revisão (2026-10-13):** nada é removido que não tenha sido lido. O `Up` passou a recusar a chave da credencial ou da
  base grafada em outra caixa e o `baseUrls` já nas settings; a exclusão compara em binário; a junção da conta é por
  tenant e plataforma. O `Down` passou a recusar, antes de qualquer escrita, a forma nova usada além do que o perfil
  achatado comporta (D13). Os testes novos entraram e falharam antes do SQL (12 casos); depois dele, os testes de migração
  deram 34 de 34 contra o `sql` do compose, inclusive o nome migrado achado pelas comparações exatas do adapter Avalara
  de verdade. O índice único de `ConnectorProfiles` em `TenantId` impede dois perfis por tenant: o caso ficou no D13, e
  não em teste. Na segunda rodada, o `Up` passou a recusar também o `clientSecret` em texto (segredo vazado, a
  rotacionar) e o `OutboundAdapter` que não é exatamente `Avalara` (trava, e não normalização); o teste que afirmava que
  `avalara` migrava invisível virou o da recusa, com `avalara` e `Mock`. Os novos falharam antes do SQL (4 casos), e os
  testes de migração deram 39 de 39, inclusive o que prova que a trava da M2 numa subida deixa a M1 aplicada. Decidido
  manter assim, e não repetir as travas na M1 (D13): cada uma das cinco travas do `Up` tem a remediação provada no
  harness (a trava dispara, o banco fica na forma da M1, o acerto por SQL, a subida seguinte aplica a M2), e o pré-voo
  só leitura `docs/preflight/platform-account-and-connectors.sql` roda antes do deploy — provado no harness sobre a forma
  de antes da change, e rodado contra o banco de dev, sem nenhuma linha.
- [x] 4.4 O `ConnectorSettingsKind` troca `Outbound` por `Platform` e `Connector`, e o `SecretNames` deriva
  `fh-{tenant}--platform--{plataforma}--clientsecret` e `fh-{tenant}--connector--{adapter}--{campo}`. Teste dos nomes.
- [x] 4.5 Os dois leitores do `OutboundAdapter` passam a achar o conector na coleção, pela comparação exata do nome do
  adapter: o `PlatformEstablishmentResolver` e o `ConnectorCredentialTestService`. Não entra resolução por integração.
  **Feito (2026-10-13):** o resolvedor recebe o adapter que despacha e só lista com o conector dele no tenant. O teste de
  credencial é da conta: testa a conta cuja plataforma tem teste, pelo nome exato; sem nenhuma, responde que a primeira
  não tem teste.
- [x] 4.6 O `tokenUrl` é obrigatório: o endpoint de token é o da conta, sem derivação da URL base; o `Avalara:TokenPath`
  sai das opções do adapter e da sonda. Teste: conta sem `tokenUrl` é rejeitada nomeando o campo, sem requisição a
  nenhum endpoint montado a partir da base (`avalara-tenant-authentication`).
- [x] 4.7 O cache de token chaveia por (tenant, conta, `tokenUrl`, `clientId`, versão do segredo), e **não** por
  conector. Teste: dois conectores sob a mesma conta pedem um token só.
- [x] 4.8 O teste de credencial passa a ser da conta: o lado "conta de plataforma" no lugar de `outbound`, e o 404 do
  endpoint de token pede para conferir o endpoint de token da conta (`connector-credential-test`).
- [x] 4.9 A tabela `establishments` passa a ser lida do conector, também na regra do estabelecimento próprio
  (`avalara-document-contract`, `platform-establishment-resolution`).
  **Prova dos testes da 4.6 à 4.9 (2026-10-13):** eles foram escritos depois do código, e cada um passou pelo vermelho
  invertendo o comportamento de produção e desfazendo: o endpoint derivado da base (4.6, 6 falhas); o conector na chave
  do cache (4.7, 3) e o endpoint fora dela (4.7, 1); o teste na base do conector (4.8, 7), o motivo do 404 sem o
  endpoint da conta (4.8, 7), o lado `Inbound` (4.8, 1) e o serviço sem o lado `platform` (4.8, 13); e o primeiro conector
  no lugar do conector do adapter (4.9, 3). Nenhum teste passou com o comportamento invertido.
- [x] 4.10 O dashboard grava a forma nova, na parte obrigatória: o bloco da conta, com `clientId`, Client Secret,
  `tokenUrl` e o botão de testar; os campos do conector, com as URLs base por versão. Testes da tela: salvar sem digitar
  não leva o segredo; o payload não tem `outboundAdapter` nem `outboundSettings`.
  **Feito (2026-10-13):** os testes do `connectorPayload` entraram e falharam antes do código; `npm test` 76 de 76, e
  `npm run build` verde. A tela mostra a conta da plataforma escolhida e o conector de mesmo nome; as outras contas e
  conectores do perfil voltam intactos, e olhar outra plataforma não cria conta. A tela não foi conferida no navegador.
- [x] 4.11 O seed semeia a conta e o conector, com o `tokenUrl` do mock (`http://localhost:5100/oauth/token`), e os
  testes que montam o perfil passam à forma nova.

## 5. As regras da coleção e o mapa de URLs base por versão (`platform-account-connectors`)

- [x] 5.1 A porta que expõe a **declaração** do adapter: o nome dele e a plataforma a que pertence (D17), módulos,
  integrações, versão de base de cada uma e quais aceitam lote. O `Avalara` a implementa. A declaração **não** é
  gravada.
  **Feito (2026-10-13):** `IAdapterDeclaration` na Application; a `AvalaraDeclaration` registrada no DI declara o Fiscal
  (notas, `v2`, sem lote), o Contábil (três integrações, `v1`, lote) e o Inventário (três do Bloco K, `v1`, lote), como na
  ADR-0037. A leitura da Avalara passou a achar a conta pela plataforma e o conector pelo adapter, que hoje coincidem.
  Os testes vieram depois da declaração e passaram pelo vermelho por inversão: lote na integração de notas, e a
  declaração fora do DI.
- [x] 5.2 O vocabulário das integrações na Application, como os nomes de módulo: a chave da integração é do hub, e os
  adapters a usam ao declarar.
  **Feito (2026-10-13):** `ConnectorIntegrations`, sete chaves, únicas e só com minúsculas, dígitos e hífen (testado); a
  declaração da Avalara só usa chaves dele e módulos de `TenantModules` (testado).
- [x] 5.3 Teste primeiro: a gravação resolve a plataforma de cada conta e o adapter de cada conector contra os nomes
  declarados pelas implementações registradas (5.1), casando sem distinguir caixa, e grava a forma declarada (D17). Hoje
  ela grava o texto pedido com `Trim()` e acha o gravado sem distinguir caixa, e o store renomeia a linha no lugar: um
  `PUT` com `avalara` deixa a conta invisível às leituras exatas, sem erro e sem log. Testes: `avalara` com o conector
  `AVALARA` é gravado como `Avalara`, e achado pela leitura exata; a plataforma que ninguém declara (`Mock`) é recusada
  com 400 nomeando a pedida e as declaradas; o adapter que a plataforma não declara é recusado do mesmo jeito; na recusa,
  nada é gravado. O "Mock" sai do seletor de plataforma do dashboard, com o teste da tela. Dívida do grupo 4,
  consertada nesta change.
  **Feito (2026-10-13):** os seis casos do serviço e o da tela falharam antes do código. A gravação casa a plataforma e
  o adapter contra as declarações e grava a forma declarada; o pedido em outra caixa não renomeia o gravado.
- [x] 5.4 Teste primeiro: dois conectores com o mesmo adapter na mesma conta são recusados com 400 que nomeia o adapter.
  **Feito (2026-10-13):** os dois casos (`Avalara` e `avalara`, que é o mesmo depois da forma declarada) falharam antes
  do código.
- [x] 5.5 O mapa de URLs base contra a declaração: versão não declarada pelo adapter → 400 nomeando a versão e as
  declaradas; versão declarada que ninguém usa → aceita vazia. O caso da versão exigida por integração ligada é do
  grupo 6.
  **Feito (2026-10-13):** a chave é exata (`V2` é recusada, porque a leitura acha a `v2`); um `baseUrls` que não é
  objeto de versão para texto também é recusado. Os casos de recusa falharam antes do código; os de aceite (vazia, nula,
  mapa vazio, sem mapa) já passavam, e ficam como guarda contra uma regra estrita demais.
- [x] 5.6 O `https` na gravação, que hoje só existe na leitura: uma URL base ou um `tokenUrl` fora da regra → 400
  nomeando o campo, e nada é gravado.
  **Feito (2026-10-13):** a regra mora em `ConnectorUrls`, na Application, e a leitura da Avalara passou a usá-la: uma
  regra só, nos dois lados. Os cinco casos de recusa falharam antes do código; os de aceite (https e loopback) já
  passavam.
- [x] 5.7 A versão é rótulo, na listagem: com a base da `v2` na raiz do host, a listagem vai a
  `<base>taxcompliance/v2/empresa`, sem prefixo duplicado (`avalara-establishment-listing`).
  **Feito (2026-10-13):** o comportamento já vinha do grupo 4; o teste passou pelo vermelho por inversão (a chave da
  versão concatenada no caminho).
- [x] 5.8 Salvar o perfil esquece a listagem, a recusa lembrada e os tokens de **todos** os conectores do tenant. Teste.
  **Feito (2026-10-13):** o comportamento já existia; entraram os testes da recusa da listagem em dois adapters, do token
  e das recusas compartilhados pelos conectores da conta, e do freio dos dois lados do teste de credencial. Cada um
  passou pelo vermelho por inversão.

## 6. Módulos, integrações, o caminho do envio e o `/info` (`platform-account-connectors`, `compliance-dispatch-outcome`, `module-navigation`)

- [x] 6.1 Teste primeiro: ligar módulo ou integração que o adapter não declara é 400; integração sem o módulo é 400;
  módulo ligado e vazio é aceito.
  **Feito (2026-10-14):** os oito casos falharam antes do código. O conector na gravação traz a lista de módulos e a de
  integrações; módulo e integração são casados contra a declaração sem distinguir caixa e gravados como declarados
  (identidade, como em D17); a integração repetida no conector também é 400; os dois ausentes mantêm o gravado.
- [x] 6.2 Módulo e integração viram linhas sob o conector, com `Endpoint` texto anulável, `BatchSize` anulável e o
  `TenantId` repetido na linha da integração; índice único em `(TenantId, chave da integração)` como rede de segurança. A
  coluna `Modules` do perfil sai.
  **Feito (2026-10-14):** `ConnectorModules` e `ConnectorIntegrations`, com o store casando por chave e atualizando no
  lugar (testado no SQLite, inclusive o índice recusando a mesma integração em dois conectores). Sair a coluna obrigou a
  derivação da 6.11 a vir junto. Até a 6.13, a aba "Módulos" do dashboard manda a lista do tenant, que o servidor
  ignora: a tela fica sem efeito nesse ponto, dentro do grupo.
- [x] 6.3 Migração M3: liga o módulo Fiscal com a integração de notas e o caminho **nulo**, à espera do host; leva os
  módulos do tenant para o conector, sem integrações; o Fiscal fica ligado sempre. **Tem `Down`:** devolve a coluna
  `Modules` ao tenant a partir dos módulos do conector. Testes no harness: um perfil com `["Fiscal", "Contabil"]`, e um
  com `["Inventario"]`, cujo `/info` passa a `["Fiscal", "Inventario"]`; e a ida e volta da M3. Evidência: a saída do
  harness.
  **Feito (2026-10-14):** `20261014051204_AddConnectorModulesAndIntegrations`, com travas no `Up` e a condição no `Down`
  (D13). Os 16 casos de comportamento falharam contra a migração só de schema; com o SQL, os 17 da M3 passaram contra o
  `sql` do compose — os dois perfis da tarefa, o nulo, a ida e volta, o caminho de notas preenchido que não impede a
  volta, as três recusas do `Down` e as seis remediações das quatro travas do `Up`. O pré-voo ganhou as duas travas que
  a forma de antes mostra, e roda sem linha no banco de dev. Na suíte inteira, a Infrastructure deu 209 passados.
- [x] 6.4 A unicidade: dois conectores na mesma integração é 400 nomeando os dois — a validação é a regra; duas
  integrações distintas do mesmo módulo em conectores diferentes é aceito; uma escrita que escape da validação é
  recusada pelo índice.
  **Feito (2026-10-14):** o caso da recusa falhou antes do código; o do aceite precisou de uma correção no próprio teste
  (ele mandava ao segundo conector uma versão que o adapter dele não declara). O índice recusando a escrita que escapa da
  validação está no teste do store (6.2).
- [x] 6.5 A versão exigida por integração ligada e ausente do mapa → 400 nomeando a versão e as integrações que a usam.
  **Feito (2026-10-14):** a integração ligada exige a base, com caminho ou sem; o módulo ligado sem integração não exige.
  O caso da recusa falhou antes do código.
- [x] 6.6 O tamanho de lote: aceito onde o adapter declara lote, recusado com 400 onde não declara, ausente da leitura
  nesse caso.
  **Feito (2026-10-14):** também recusado quando não é positivo. Os casos da gravação falharam antes do código. O da
  leitura passava com o comportamento invertido — a gravação já impede o lote, e o campo nulo some de todo jeito —, e foi
  reescrito com o lote posto por fora da gravação; reescrito, a inversão o derruba.
- [x] 6.7 A integração inativa: caminho vazio é aceito na gravação, aparece na leitura como sem caminho, e em runtime
  vira configuração do conector sem nenhuma requisição.
  **Feito (2026-10-14):** a leitura traz `withoutPath`; o envio com a integração sem caminho é rejeitado sem requisição,
  nem a da listagem (testado no despachante). Os testes passaram pelo vermelho por inversão.
- [x] 6.8 O envio lê o caminho da integração, no próprio conector: o `AvalaraOutboundSettings` acha o conector pelo nome
  do adapter e a integração de notas dentro dele; o envio vai à base da versão mais o caminho, e a consulta de status à
  base mais o caminho, o identificador e `/status`. Conector ausente ou integração não ligada → configuração do conector,
  sem requisição. Teste: a URL do envio não traz a chave da versão (`compliance-dispatch-outcome`).
  **Feito (2026-10-14):** a integração é conferida antes de qualquer requisição, inclusive a da listagem. Os testes foram
  escritos antes do código, mas só falhavam por compilar; passaram pelo vermelho por inversão (a chave da versão no
  caminho).
- [x] 6.9 O `Avalara:DocumentsPath` sai do envio, da consulta de status e do motivo do 404, que passa a apontar a base da
  versão e o caminho da integração na tela de conectores. Os consumidores: o `AvalaraComplianceDispatcher`, o motivo no
  `AvalaraOutboundSettings`, a sonda, o `DispatchToMockTests` e o `RUNNING.md`. Teste: com `Avalara:DocumentsPath` =
  `documents` e a integração em `taxcompliance/v2/fiscal/dfe`, nenhuma requisição vai a `documents`.
  **Feito (2026-10-14):** o motivo do 404 aponta as duas partes, as duas na tela, e não cita o appsettings; a sonda manda
  e consulta pelo caminho da integração; o `DispatchToMockTests` grava o caminho na integração. Duas inversões (o envio
  de volta ao `DocumentsPath`, o motivo de volta ao appsettings), as duas pegas.
- [x] 6.10 O host preenche, uma vez, o caminho nulo da integração de notas com o `Avalara:DocumentsPath`, na subida e em
  qualquer ambiente, sem depender do seed. Testes: o nulo é preenchido; o texto vazio da tela não é; o preenchido não é
  trocado.
  **Feito (2026-10-14):** `FillPendingIntegrationPathAsync`, na Infrastructure, chamado pelo host logo depois da
  migração, com o `DocumentsPath` das opções vinculadas — o padrão do código incluído. O teste no SQLite falhou antes do
  código e cobre também o outro adapter e a outra integração, que não são tocados, e a segunda subida, que não muda nada;
  um teste do harness prova o mesmo contra o SQL Server, sobre a marca da M3.
- [x] 6.11 O `/info` passa a derivar `modules` dos conectores. **A forma da resposta não muda**, e nenhuma tela de
  navegação é tocada. Testes: tenant sem conector → `["Fiscal"]`; dois conectores → união na ordem Fiscal, Contábil,
  Inventário, sem repetir; corpo antigo com `modules` no nível do tenant é recusado com 400 que manda recarregar a tela.
  **Feito (2026-10-14), junto da 6.2:** a coluna saiu, e o `TenantModules.Of` passou a derivar dos conectores. Os três
  testes estão no `TenantModulesTests` e no `ConnectorProfileServiceTests`; a forma da resposta continua provada no
  `InstanceInfoTests`. **Revisão (2026-10-14):** o corpo antigo com a lista do tenant passou a ser recusado com 400 que
  manda recarregar a tela, e não aceito e ignorado (spec `module-navigation`); o teste foi invertido antes do código. O
  tenant sem conector responde só o Fiscal (D7), com teste no `/info` e na migração.
- [x] 6.12 Desligar o Fiscal no conector: a nota é rejeitada como configuração do conector, nomeando a integração de
  notas, e o `/groups` continua respondendo (`module-navigation`).
  **Feito (2026-10-14):** a recusa tem teste na leitura das settings e no despachante (sem requisição), e passou pelo
  vermelho por inversão. O `/groups` lê só as consultas de documentos, e nenhum leitor de módulo além do `/info`.
- [x] 6.13 O dashboard grava módulos e integrações, na parte obrigatória: os módulos marcados por conector, no lugar da
  aba "Módulos" do cliente; as integrações ligadas, com o caminho e, onde houver, o lote; a pendência das que estão sem
  caminho. Testes da tela: o payload não tem `modules` no nível do tenant.
  **Feito (2026-10-14):** a aba "Módulos" saiu; o bloco do conector ganhou os módulos e as integrações que o adapter
  declara, com o caminho, o lote onde a API aceita e a pendência. O caminho que espera o host não vai no payload, para o
  servidor o manter. Os sete casos novos falharam antes do código; `npm test` 84 de 84, e `npm run build` verde. A tela
  não foi conferida no navegador.
  **Revisão (2026-10-14), com a tela aberta pelo Marcelo:** o `tokenUrl` e o `clientId` vazios ganharam a marca de
  pendência com o efeito; o botão de testar a conta fica desabilitado dizendo o que falta na credencial gravada; os campos
  de URL e de GUID ocupam a linha inteira, e os grids e a linha da integração empilham abaixo de 900px. Os testes da
  pendência e do botão falharam antes do código; `npm test` 87 de 87. O layout não tem teste: a conferência é na tela.
  **Revisão (2026-10-14), na verificação final:** os exemplos do endpoint de token e das URLs base eram os de produção,
  fixos, numa instância Sandbox. Passaram a seguir o ambiente do `/info` (`fieldPlaceholder`), sem exemplo antes de ele
  responder, com as URLs dos dois ambientes escritas por inteiro (D16, e um cenário novo na spec). Os cinco casos — os
  dois ambientes, o `/info` ausente ou desconhecido, nenhuma URL da Avalara fixa, e o campo sem exemplo por ambiente —
  falharam antes do código; `npm test` 110 de 110.
- [x] 6.14 O seed semeia o módulo Fiscal e a integração de notas com o caminho do sandbox (`taxcompliance/v2/fiscal/dfe`),
  que o mock atende.
  **Feito (2026-10-14):** o teste do seed falhou antes do código.

## 7. A tela do canvas (`platform-account-connectors`, `module-navigation`)

Veio do desenho do canvas (ADR-0039), e não é obrigado pela forma do dado (design D16). Se o grupo sair desta fatia, o
cenário "Módulo que o conector não atende", de `module-navigation`, sai da spec junto.

- [x] 7.1 A lista de conectores, e o painel de um conector com as três abas: Conexão (URLs base por versão), Módulos e
  integrações, Endpoints (caminho e, onde houver, tamanho de lote).
  **Feito (2026-10-14):** `ConnectorPanel.tsx` (a lista à esquerda, empilhada abaixo de 900px, e as três abas) no lugar
  do bloco "Conector {plataforma}" e do `ConnectorModules` do grupo 6, que saiu: o painel o substitui. O que o painel
  mostra está em `connectorPanelRows.ts` (`connectorList`, `selectedConnector`, `moduleRows`, `endpointRows`), testado
  em `connectorPanelRows.test.ts`; os testes falharam antes do código (o módulo ausente, depois `selectedConnector is not
  a function`). A escolha do conector, invertida de dois jeitos (sem o retorno ao da plataforma; um conector em branco),
  foi pega nas duas vezes. A aba Conexão usa os campos e o prefixo de segredo do conector escolhido.
  **Revisão (2026-10-14), com a tela aberta pelo Marcelo:** o resumo do conector na lista escolhe singular ou plural pelo
  número ("3 módulos · 1 integração"), no `connectorSummary`, e a coluna da lista foi de 200px a 240px para o resumo mais
  longo caber numa linha. Os sete casos (0, 1 e N dos dois lados) falharam antes do código; `npm test` 105 de 105.
  **Revisão (2026-10-14), na verificação final:** entre 900 e 1000px, a aba "Endpoints" sumia atrás da seta e a URL base
  aparecia cortada. Com a barra lateral de 232px e a página de no máximo 900px, a lista ao lado das abas só cabe quando a
  página chega à largura máxima: o lado a lado passou de 900px (`md`) a 1200px (`lg`), e abaixo disso o painel empilha. O
  layout não tem teste: a conferência é na tela.
- [x] 7.2 Um módulo que o adapter não declara aparece apagado, dizendo que aquele conector não o atende, e a caixa dele
  não marca.
  **Feito (2026-10-14):** `moduleRows` traz os três módulos do hub, e o que o adapter não declara vem com o motivo "O
  conector X não atende o módulo Y."; a caixa vai desabilitada, e o `toggleModule` ignora o módulo não declarado. O teste
  usa uma declaração falsa só com o Fiscal, porque a Avalara declara os três: com o único adapter de hoje, nenhum módulo
  aparece apagado na tela. O `disabled` da caixa está no componente, que não tem teste: a conferência é na tela.
- [x] 7.3 Testes da tela: o painel monta o mesmo payload que a tela do grupo 6; salvar sem digitar continua não levando o
  segredo.
  **Feito (2026-10-14):** as mesmas mudanças, pelo conector do painel e pelo `shownConnector` da tela do grupo 6, dão o
  mesmo payload (`toEqual`), e esse teste falhou com o conector em branco da inversão; um segundo confere o payload
  esperado de ponta a ponta; salvar pelo painel sem digitar não leva `clientSecret`. `npm test` 98 de 98, `npm run build`
  verde (o aviso de chunk acima de 500 kB já existia), `dotnet build` 0 warnings. A tela não foi conferida no navegador.

## 8. Docs

- [x] 8.1 `docs/STATUS.md`: fechar o item 2 dos Próximos passos, com as provas.
  **Feito (2026-10-14):** o item 2 deixou de dizer que a change "precisa ser reescrita" e passou a apontar o que falta
  para fechá-la — a verificação final (grupo 9) e o archive. As provas estão na sessão nova, "A conta de plataforma e os
  conectores", que diz que o grupo 9 está pendente; quando ele rodar, a sessão ganha o resultado. O "Onde estamos" passou
  a descrever a conta, o conector e o ambiente da instância, e a pendência da sobreposição `establishments` registra
  onde ela está agora (conferido no banco de dev: continua gravada, nas settings do conector do tenant-a).
- [x] 8.2 `docs/RUNNING.md`: o roteiro perde o seletor de ambiente e documenta `Connector:Environment`; a conta ganha o
  `tokenUrl`; o caminho do envio passa a ser o da integração, e o `Avalara:DocumentsPath` fica só como o preenchimento de
  uma vez; sai o `Avalara:TokenPath`; a seção da sonda acompanha.
  **Feito (2026-10-14):** o `Connector:Environment` já estava documentado desde o grupo 1. Saíram as seções Sandbox e
  Produção, a referência `--outbound--sandbox--` e o `tokenUrl` "por SQL"; entraram a conta com o endpoint de token
  obrigatório, uma seção do conector (as três abas, a URL do envio e do status, a integração inativa, o preenchimento de
  uma vez do `DocumentsPath`), o freio sem ambiente e o aviso do poll trocado pelo da base. O SQL que esvazia os
  `establishments` foi reescrito para o conector e compilado contra o banco de dev com `SET NOEXEC ON`, sem executar —
  os `establishments` do tenant-a continuaram gravados; o script de comparação da seção 8 rodou contra o host, com três
  `ok`. A sonda, a limpeza da seção 10 e a mensagem da recusa do token acompanham.
- [x] 8.3 O `Purpose` da spec `module-navigation` ainda diz que a barra lateral é montada "a partir dos módulos que cada
  tenant tem, marcados pelo Admin em Configurações". O delta troca os requisitos, não o `Purpose`: ajustar à mão no
  archive.
  **Aberta de propósito (2026-10-14):** é passo do archive, que é outra conversa — um delta não alcança o cabeçalho da
  capability. **A ordem, para quem arquivar:** (1) editar à mão o `Purpose` de `openspec/specs/module-navigation/spec.md`,
  como parte do archive, para dizer que a barra lateral vem dos módulos ligados nos conectores do tenant; (2) marcar esta
  8.3, com a frase nova como evidência; (3) só então rodar o `openspec archive`. Com a 8.3 aberta, um archive que exija
  todas as tarefas marcadas trava aqui.
  **Feito (2026-10-14), como parte do archive:** o `Purpose` passou a dizer que a barra lateral é montada "a partir dos
  módulos ligados nos conectores do tenant: o adapter de cada conector declara os módulos que atende, e o Admin os liga
  no painel do conector, em Configurações. O tenant sem conector mostra o Fiscal." O "É apresentação, e não permissão"
  ficou, porque o requisito modificado o mantém.
- [x] 8.4 Conferir que os ADRs 0036 a 0039 cobrem o que foi feito, e anotar neles qualquer desvio que a implementação
  tenha revelado (ADR é imutável: desvio vira nota de revisão, não reescrita). Já se sabe de dois: o "não aparece no
  agendamento" do ADR-0037 §2 fica para a fatia do agendamento por integração; e o "a migração é irreversível" do
  ADR-0036 ficou valendo só para a conversão do ambiente (M1) — a M2 e a M3 têm volta (design D13).
  **Feito (2026-10-14):** nota de revisão no topo do ADR-0036 (só a credencial da conta é recusada no conector, e o
  `apiKey` vai ao cofre com escopo de conector; a conta guarda o endpoint de token; só a M1 é irreversível, e o `Up`
  recusa dado ambíguo) e do ADR-0037 (o agendamento; a tela mostra todas as bases declaradas, e a exigência ficou na
  gravação; o tenant sem conector mostra o Fiscal; a declaração carrega a plataforma e o nome, e a gravação canoniza; o
  caminho nulo à espera do host, e o `poll.enabled` que não desceu). O "Change OpenSpec: a propor" dos dois passou a
  apontar esta change. O ADR-0038 não tem nada implementado aqui, e o ADR-0039 só deu o desenho do painel do grupo 7, que
  segue o §6 (nada apagado sem motivo): nenhum dos dois tem desvio a anotar.

## 9. Verificação final

- [x] 9.1 `dotnet build` com 0 warnings e `dotnet test` verde na solução inteira, sonda inclusive.
  **Feito (2026-10-14):** com o host de dev de pé, pela guarda (`--artifacts-path obj/guard`). O `dotnet build` da solução
  inteira — a sonda e o mock de `tools/` inclusive — deu 0 warnings e 0 erros. O `dotnet test`, com o harness ligado, não
  teve falha: Application 472, Avalara 383, Infrastructure 217 (1 pulado: o do emulador do Key Vault), Integration 31,
  D365Poll 244 (2 pulados: os opt-in contra o F&O), Inbound.Xml 24, ServiceBus 12, Discovery.Local 12, BlobDrop 9 e
  Directory.Json 3. A sonda não tem projeto de teste: entra pelo build. Nenhum banco descartável do harness sobrou.
- [x] 9.2 `npm test` e `npm run build` no dashboard.
  **Feito (2026-10-14):** `npm test` 105 de 105; `npm run build` verde, com o aviso de chunk acima de 500 kB que já
  existia.
- [x] 9.3 `openspec validate --all --strict`.
  **Feito (2026-10-14):** 28 de 28.
- [x] 9.4 O harness com o SQL Server no ar: as três migrações em sequência, a partir da forma de antes desta change.
  **Feito (2026-10-14):** nenhum teste atravessava as três de uma vez — cada um partia da forma da migração anterior, ou
  esperava a trava. Entrou o `MigrationSequenceTests`: dois perfis na forma de antes (as duas seções; Sandbox com os três
  módulos, Production com `Modules` nulo), um `Migrate` só, como o da subida, e o estado final inteiro — as quatro
  colunas velhas fora, a conta com a credencial da seção do ambiente e sem a referência do segredo, o conector só com
  `baseUrls` e `establishments`, os módulos, a integração de notas com o caminho nulo e, depois do preenchimento da
  subida, com o caminho. Passou contra o `sql` do compose. Escrito depois do código, passou pelo vermelho em três
  inversões, uma por migração — a M1 com a seção do outro ambiente, a M2 copiando a referência do segredo, a M3 com o
  caminho vazio no lugar do nulo —, as três pegas, com o hash de cada migração conferido na volta.
- [x] 9.5 Ponta a ponta contra o `fiscosysdev`: subir com `Connector:Environment=Sandbox`, configurar pela tela a
  credencial e o `tokenUrl` da conta, preencher a base da `v2` e conferir o caminho do `/dfe` na integração, e despachar
  uma das 5 NF-e 55. Provar que o envio sai com o token da conta, que a URL é a base mais o caminho da integração, e que
  o nome do segredo no cofre é o novo.
  **Roteiro (2026-10-14), para o Marcelo, nesta ordem.** Nada daqui roda sem ele: o conector do tenant-a aponta para o
  sandbox real, e a credencial só se digita na tela.

  **O ponto de partida**, lido no banco de dev em 2026-10-14:
  - a conta `Avalara` do tenant-a tem o Client ID; o `ClientSecretRef` e o `TokenUrl` estão nulos, porque a M2 não copia
    a referência (D8) e não deriva o endpoint (D11);
  - a base da `v2` é `https://api-gateway.sandbox.avalarabrasil.com.br/`, e a integração de notas tem
    `taxcompliance/v2/fiscal/dfe`, preenchido pelo host;
  - as 5 NF-e 55 da `brmf` estão `IntegrationError`, e por isso têm o "Reprocessar".

  1. **Abrir a tela, antes de digitar.** Com o host de pé (`Connector:Environment=Sandbox`), em Configurações →
     Conectores → Saída (compliance), olhar:
     - o selo diz Sandbox;
     - o Client ID preenchido; o Client Secret "não configurado"; o endpoint de token vazio, com "Sem o endpoint de token,
       o conector não despacha";
     - o "Testar credencial" desabilitado, com "Faltam o Client Secret e o endpoint de token. Grave-os para testar.";
     - a coluna da lista, de 240px: "Avalara" e "3 módulos · 1 integração", numa linha só;
     - o painel: na aba Conexão, a base da `v2` do sandbox; na aba Endpoints, "Notas fiscais" com
       `taxcompliance/v2/fiscal/dfe` e sem pendência.
  2. **Digitar e salvar.** Na conta de plataforma, o Client Secret e o endpoint de token do sandbox — a URL inteira, que o
     hub não deriva da base. Salvar. Olhar: o Client Secret volta mascarado, com "configurado em <data>"; a marca do
     endpoint some; o botão de testar habilita.
     *Depois, eu confiro, só lendo:* a conta com o `ClientSecretRef` `kv:fh-tenant-a--platform--avalara--clientsecret`
     (o nome novo) e o `TokenUrl` gravado; o `GET /connector` com o `platform.Avalara.clientSecret` configurado e sem
     valor.
  3. **Testar a credencial.** Olhar: "Credenciais e conexão válidas". No console do host, a linha do `HttpClient`
     `avalara-token` com o POST no endpoint de token digitado, e não na base do conector. Se recusar: o motivo
     detalhado está na linha "Teste de credencial do tenant …" do log, e um 404 pede para conferir o endpoint de token. A
     recusa fica lembrada 5 minutos; salvar libera.
  4. **Despachar uma nota.** No Fiscal, abrir `brmf|BRMF06-110000027` e clicar "Reprocessar". Olhar, no console: o POST do
     `avalara-token` no endpoint da conta, e o POST do despachante em
     `https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/fiscal/dfe` — a base da `v2` mais o caminho da
     integração, sem a chave da versão. Os cabeçalhos saem redigidos no log. O desfecho: uma recusa da plataforma com o
     motivo dela é resultado aceitável (o payload é de 2016, sem IBS/CBS); um 401 seria o token, e um 404, o caminho.
     *Depois, eu confiro, só lendo:* o status e o motivo da nota, e o `url` da foto `avalara.response.submit.json`, que
     tem de ser a base mais o caminho.

  **Feito (2026-10-14), contra o sandbox real da Avalara, no banco de dev, com o Marcelo digitando o Client Secret na
  tela** (o endpoint de token também foi gravado pela tela). Os três pontos:
  1. **O nome do segredo é o novo:** a conta do tenant-a tem o `ClientSecretRef`
     `kv:fh-tenant-a--platform--avalara--clientsecret` — com `--platform--` e sem segmento de ambiente.
  2. **O token sai do endpoint da conta:** a linha do `HttpClient` `avalara-token` foi o `POST
     https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token`, que é o `TokenUrl` gravado, e não um derivado da
     base. O teste de credencial registrou: "Teste de credencial do tenant tenant-a (Avalara, ambiente Sandbox): Worked. A
     plataforma emitiu um token novo para a credencial da conta 'Avalara' (Sandbox)."
  3. **O envio vai à base mais o caminho:** `POST https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/fiscal/dfe`
     — a base da `v2` mais o caminho da integração, sem nenhum `v2` acrescentado pela chave da versão.

  **O desfecho:** `brmf|BRMF06-110000027` ficou `IntegrationError`, com `ReprocessCount = 2`, recusada pela plataforma —
  "Plataforma de compliance recusou: 6 campos com erro: operacao, tipoPagamento, parceiro.Cod…". É o resultado aceitável
  do roteiro: o payload é de 2016, sem IBS/CBS.

  **Um episódio no caminho, fora desta change:** a primeira tentativa morreu antes da Avalara. A busca no F&O respondeu
  400 na `FSFiscalDocumentTaxTransBRs` ("Cannot select a record in Sub Query (Subquery)"), a mensagem foi retentada até o
  limite e a nota virou `DeadLettered`, com o motivo `MaxDeliveryCountExceeded`. A segunda tentativa, na mesma nota,
  funcionou: foi intermitência do F&O, e não defeito da entidade. O motivo genérico escondeu que a causa era o ERP — caso
  vivo para a change do defeito da dead-letter —, e a intermitência entrou nos Riscos do `STATUS.md`.
- [x] 9.6 Provar a migração sobre uma cópia da base de dev que ainda tenha as duas seções de ambiente e os módulos no
  tenant: a conta, o conector, o módulo Fiscal e a integração de notas nascem corretos; o caminho da integração é
  preenchido pelo host na primeira subida; os módulos do tenant viram módulos do conector; e a seção do outro ambiente
  não ficou em lugar nenhum.
  **Feito (2026-10-14), sobre o backup da base de dev de antes da M1** (`/tmp/FiscalHub-pre-platform-account-20261014-0921.bak`,
  no container do SQL):
  - **A cópia:** restaurada como `FiscalHub_9_6`, com `WITH MOVE` para arquivos próprios e sem `REPLACE`. Ela estava na
    `AddExecutionEstablishmentTaxId`, com os dois tenants em `Sandbox`, as duas seções nas settings de saída, o tenant-a
    com `["Fiscal","Contabil","Inventario"]` e o tenant-b com `Modules` nula.
  - **O pré-voo contra ela:** zero linhas. É a primeira execução dele contra dado real da forma de antes.
  - **A migração:** pelo `MigrateProcessingSchemaAsync`, a chamada da subida do host, num teste temporário que só rodava
    contra o banco `FiscalHub_9_6` e foi apagado ao fim — sem subir o host, cujos serviços de fundo disputariam as filas
    com o host de dev. Uma foto de antes, em `tempdb`, deixou a cópia intocada até a migração.
  - **O estado final, comparado campo a campo com a foto:** as três migrações aplicadas; nenhuma das quatro colunas
    velhas no perfil; em cada tenant, uma conta e um conector `Avalara`, com o Client ID da seção do ambiente gravado, o
    `ClientSecretRef` nulo (D8), o `TokenUrl` nulo (a seção não o tinha) e a base da `v2` da seção — a do sandbox no
    tenant-a, a do mock no tenant-b; os `establishments` iguais; nenhuma chave além de `baseUrls`; nenhum valor da seção
    `production` (a base e a referência do segredo) na conta ou no conector, nem chave `sandbox` ou `production`; os
    módulos `Fiscal,Contabil,Inventario` no tenant-a e `Fiscal` no tenant-b; a integração de notas sob o Fiscal, com o
    caminho `NULL` e sem lote.
  - **A primeira subida:** o `FillPendingIntegrationPathAsync`, com o `DocumentsPath` lido do `appsettings.Development.json`
    do host, preencheu `taxcompliance/v2/fiscal/dfe` nos dois tenants.
  - **A limpeza:** a cópia e as fotos em `tempdb` foram apagadas, e não sobrou arquivo da cópia no diretório de dados. O
    `.bak` ficou onde estava, com os 729088 bytes, e a cópia em `_backups` também. O `FiscalHub` não foi tocado: segue na
    M3. Os scripts copiados para o `/tmp` do container ficaram lá — o `rm` foi bloqueado pela guarda da ferramenta.
