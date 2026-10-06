A ordem mantém o build verde a cada grupo:

- **Grupo 1:** o provider de client credentials ganha os motivos e o log.
- **Grupo 2:** os testes contra o F&O real e o gravador deixam o Azure CLI.
- **Grupo 3:** o fallback é apagado. Só depois do grupo 2, para nada deixar de compilar no meio.
- **Grupo 4:** a falha de ponta a ponta.
- **Grupos 5 a 7:** a coluna da execução, da descoberta até a tela.
- **Grupo 8:** a mutação.
- **Grupo 9:** os docs e o ADR.
- **Grupo 10:** a verificação final e as provas contra o fiscosysdev.

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. O grupo da tela termina com o
`npm test` e o `npm run build`. Uma tarefa só recebe `[x]` com evidência.

## 1. O client credentials: os três motivos e o log da identidade (D2, D3; `d365-change-feed`)

- [x] 1.1 Teste primeiro, no `D365TokenProviderTests`, dos três motivos:
  - **sem `auth`:** o motivo diz que a credencial do ERP não está configurada;
  - **`auth` incompleto, numa `Theory` com cada combinação de campo vazio ou em branco:** o motivo nomeia só os que
    faltam, pelo nome da tela (Tenant do Entra ID, Client ID, Client Secret), na ordem;
  - **a referência sem o segredo no cofre:** o motivo diz que o Client Secret não está no cofre;
  - **os três textos são distintos entre si;**
  - **nos três casos:** nenhuma credencial é criada, e nenhum cofre é lido nos dois primeiros;
  - **sem segredo e sem token no motivo;**
  - **a referência sem `kv:` e a referência fora do prefixo** continuam com os motivos de hoje.

  O `Missing_or_incomplete_auth_is_a_configuration_error` de hoje é absorvido por estes testes.
- [x] 1.2 Teste primeiro, no mesmo arquivo, do log da identidade, com o `ListLogger` do `D365ChangeFeedTests`:
  - **uma vez por tenant:** três tokens do tenant-a com `app-a` e `entra-a` dão uma linha só, com `tenant-a`, `app-a`,
    `entra-a` e "credencial do próprio tenant", e sem o segredo;
  - **dois tenants:** duas linhas;
  - **a identidade nova:** a troca do client id para `app-b` dá uma linha nova;
  - **o segredo trocado:** o mesmo app com segredo novo não dá linha nova;
  - **sem credencial utilizável:** nenhuma linha;
  - **o teste de credencial:** o `GetFreshTokenAsync` não escreve linha de identidade.

  Estes testes cobrem o que o `D365DevelopmentTokenProviderTests` cobria do log, e ele sai na 3.3.
- [x] 1.3 Implementar:
  - **o motivo do incompleto:** o `RequireComplete()` monta a lista do que falta;
  - **os outros textos:** os do D2, no `ResolveCredentialAsync`;
  - **o log:** o `ILogger<ClientCredentialsD365TokenProvider>` no construtor e a linha do D3 no `GetTokenAsync`. O texto
    é o mesmo da linha "completa" de hoje;
  - **o DI:** passa o logger nos dois registros do `D365PollServiceCollectionExtensions`.
- [x] 1.4 O `D365InboundSettingsTests.Incomplete_auth_is_a_configuration_error_for_client_credentials` passa a afirmar o
  campo que falta em cada caso. O comentário "o dev com Azure CLI não precisa de auth…" passa a dizer que o perfil sem
  `auth` se lê, e a falta vira motivo na hora do token.
- [x] 1.5 O `D365CredentialTestTests` afirma o motivo `Incomplete` de cada caso: sem `auth`, sem o segredo no perfil e
  sem o segredo no cofre. O comentário "(nunca o Azure CLI)" do topo sai.
- [x] 1.6 `dotnet build` com 0 warnings e `dotnet test` verde.

  **Feito (2026-10-05):** 0 warnings. 1.156 testes passaram, 0 falharam, e 3 foram pulados: os dois contra o F&O real e o
  do emulador do cofre. Na 1.3, o cofre que devolve o segredo vazio também conta como ausente, como o
  `D365DevelopmentTokenProvider` já contava. Na mesma tarefa, o comentário do `D365InboundSettings.Auth` (da 3.4) já
  mudou, porque estava ao lado.

## 2. Os testes contra o F&O real e o gravador pedem a credencial (D8; `d365-change-feed`, `d365-document-assembly`)

- [x] 2.1 Teste primeiro, de unidade e sem rede, de um `D365IntegrationEnvironment` no projeto de testes do D365. Ele
  calcula, a partir de uma função de leitura, quais das quatro variáveis faltam e o motivo do skip:
  - **nenhuma definida:** faltam as quatro, na ordem `FISCALHUB_D365_URL`, `FISCALHUB_D365_ENTRA_TENANT_ID`,
    `FISCALHUB_D365_CLIENT_ID`, `FISCALHUB_D365_CLIENT_SECRET`;
  - **só a URL:** faltam as três da credencial;
  - **todas, menos o secret:** o motivo nomeia só o `FISCALHUB_D365_CLIENT_SECRET`;
  - **o valor em branco** conta como falta;
  - **todas:** nada falta, e não há skip.
- [x] 2.2 Implementar o `D365IntegrationEnvironment`, e o `D365IntegrationFactAttribute` passa a usá-lo. Ele monta o
  `ClientCredentialsD365TokenProvider` real, com o segredo num cofre em memória sob
  `fh-tenant-a--inbound--auth--clientsecret`, e o perfil com o `auth` e a referência `kv:`.
- [x] 2.3 O `D365ChangeFeedIntegrationTests` deixa o `AzureCliD365TokenProvider` (linha 41) e o skip da linha 91. O
  `D365GoodsInvoiceSourceIntegrationTests` deixa o da linha 23. Os XML docs das duas classes trocam o "logado no Azure
  CLI" pelas variáveis.
- [x] 2.4 O `tools/d365-fixtures/Record-D365Fixtures.ps1`:
  - lê as três variáveis da credencial, e falha nomeando as que faltam;
  - pede o token por client credentials no `login.microsoftonline.com`, com o escopo `{url}/.default`;
  - não imprime o segredo nem o token;
  - troca o `.EXAMPLE` e o comentário do topo.
- [x] 2.5 O `tools/d365-fixtures/README.md`, em "Como rodar", troca o `az login` pelas variáveis. Ele diz que elas
  ficam na sessão (`$env:`), nunca em arquivo versionado, e que o app é o do conector.
- [x] 2.6 `dotnet build` com 0 warnings e `dotnet test` verde. Os dois testes contra o F&O real aparecem pulados, com o
  motivo novo, colado aqui.

  **Feito (2026-10-05):** 0 warnings. 1.162 testes passaram, 0 falharam, e 3 foram pulados. O motivo, sem nenhuma das
  quatro variáveis:

  ```
  Integração com F&O real: falta FISCALHUB_D365_URL, FISCALHUB_D365_ENTRA_TENANT_ID, FISCALHUB_D365_CLIENT_ID e FISCALHUB_D365_CLIENT_SECRET. Defina FISCALHUB_D365_URL, FISCALHUB_D365_ENTRA_TENANT_ID, FISCALHUB_D365_CLIENT_ID e FISCALHUB_D365_CLIENT_SECRET (o app do conector) para rodar.
  ```

  O gravador, só com o tenant do Entra definido, parou antes de qualquer requisição, sem criar a pasta de saída:

  ```
  Falta a credencial do app do conector: FISCALHUB_D365_CLIENT_ID, FISCALHUB_D365_CLIENT_SECRET. Defina FISCALHUB_D365_ENTRA_TENANT_ID, FISCALHUB_D365_CLIENT_ID, FISCALHUB_D365_CLIENT_SECRET ($env:) antes de gravar.
  ```

  As mensagens do gravador ficaram sem acento: o arquivo é UTF-8 sem BOM, e o PowerShell 5.1 o lê como ANSI. Com acento,
  "sessão" saía quebrado.

## 3. O fallback sai (D1, D4; `d365-change-feed`, `connector-credential-test`)

- [x] 3.1 Teste primeiro, no `D365PollRegistrationTests`, as três afirmações do D4:
  - **com os quatro `Add*`:** cada registro de `ID365TokenProvider` resolve o `ClientCredentialsD365TokenProvider`, e há
    um só;
  - **o assembly do adapter:** o único tipo concreto que implementa `ID365TokenProvider` é o
    `ClientCredentialsD365TokenProvider`;
  - **a superfície pública:** os métodos estáticos públicos do `D365PollServiceCollectionExtensions` são exatamente os
    quatro `Add*`.

  Antes de apagar, a segunda e a terceira têm de falhar. Colar a falha aqui.

  **Feito (2026-10-05):** antes de apagar, as duas falharam:

  ```
  Failed The_only_token_provider_in_the_adapter_is_client_credentials
  Expected: [typeof(...ClientCredentialsD365TokenProvider)]
  Actual:   [typeof(...AzureCliD365TokenProvider), typeof(...ClientCredentialsD365TokenProvider), typeof(...D365DevelopmentTokenProvider)]
  Failed The_registration_surface_is_exactly_the_four_add_methods
  Expected: ["AddD365ChangeFeed", "AddD365CompanyDirectory", "AddD365DocumentDiscovery", "AddD365GoodsInvoiceSource"]
  Actual:   ["AddD365ChangeFeed", "AddD365CompanyDirectory", "AddD365DocumentDiscovery", "AddD365GoodsInvoiceSource", "UseD365AzureCliFallback"]
  ```

  Depois de apagar, as três passam.
- [x] 3.2 Apagar:
  - o `AzureCliD365TokenProvider.cs`;
  - o `D365DevelopmentTokenProvider.cs`;
  - o `UseD365AzureCliFallback()`, com a menção a ele no XML doc do `AddD365ChangeFeed`;
  - o bloco `if (builder.Environment.IsDevelopment())` do `Program.cs`, com a chamada.
- [x] 3.3 Apagar:
  - o `D365DevelopmentTokenProviderTests.cs` inteiro;
  - o `Azure_cli_token_is_cached_until_close_to_expiry` do `D365TokenProviderTests`;
  - o `Azure_cli_fallback_enters_only_when_asked`.

  Atualizar o XML doc das duas classes de teste, que ainda citam o Azure CLI.
- [x] 3.4 Os comentários do D1 deixam de citar o Azure CLI como caminho vivo: `ID365TokenProvider`,
  `D365InboundSettings.Auth`, `D365CredentialTest`, o registro do teste no `D365PollServiceCollectionExtensions`,
  `InboundAdapterChoice` e o seed de dev em `InfrastructureServiceCollectionExtensions`.
- [x] 3.5 Prova por busca:
  - **sem nenhuma ocorrência:** `AzureCli`, `UseD365AzureCliFallback`, `D365DevelopmentTokenProvider` e `az login`, em
    `src`, `tests` e `tools`;
  - **"Azure CLI" ainda aparece só em lugares permitidos:**
    - os comentários que dizem que ele não entra;
    - `docs/STATUS.md:1150`;
    - o ADR-0032;
    - `openspec/changes/archive/`;
    - `d365/postman/README.md`;
    - `d365/06-receita-criar-entidade-na-mao.md`.

  Colar a saída.

  **Feito (2026-10-05):**
  - **`AzureCli`, `UseD365AzureCliFallback`, `D365DevelopmentTokenProvider` e `az login` em `src`, `tests` e `tools`:**
    nenhuma ocorrência;
  - **"Azure CLI" em `.md`, `.cs`, `.ps1`, `.ts` e `.tsx`,** fora de `openspec/changes/archive/` e desta change:
    - `d365/postman/README.md`, o ADR-0032 e `docs/STATUS.md`: permitidos;
    - `tools/d365-fixtures/README.md` e o `Record-D365Fixtures.ps1`: os comentários que dizem que ele não entra;
    - `openspec/specs/*`: as specs vivas, que os deltas desta change mudam no arquivamento;
    - `docs/RUNNING.md`: muda na 9.1;
    - `openspec/changes/platform-establishment-resolution/tasks.md`: a prova da 6.x daquela change, que diz "e não com o
      Azure CLI". É registro de outra change, e não muda.

  O `d365/06-receita-criar-entidade-na-mao.md` não cita "Azure CLI": ele usa `az account get-access-token`, e fica.
- [x] 3.6 `dotnet build` com 0 warnings e `dotnet test` verde.

  **Feito (2026-10-05):** 0 warnings. 1.157 testes passaram, 0 falharam, e 3 foram pulados. No projeto do D365, de 242
  para 237. Saíram 8 testes: os 6 do `D365DevelopmentTokenProviderTests` (uma `Theory` de três casos conta como três), o
  do cache do Azure CLI e o do fallback. Entraram os 3 do registro.

## 4. Sem credencial, a integração falha com motivo (D2; `d365-change-feed`)

- [x] 4.1 Teste de ponta a ponta no `FiscalHub.Integration.Tests`:
  - **a composição:** o `ChangeFeedPoller` sobre o feed do D365 registrado pelo `AddD365ChangeFeed`, com o provider real,
    o cursor, o lease e a fila em memória, e um handler HTTP que conta as requisições;
  - **os perfis:** a integração automática ligada, em três perfis — sem `auth`, com o `auth` incompleto e com o segredo
    ausente do cofre;
  - **o que se afirma:** a falha registrada no cursor (o último erro do painel) traz o motivo de cada caso, e os três são
    distintos; a marca não avança; nenhuma requisição sai ao F&O.

  **Feito (2026-10-05):** o `D365CredentialRequiredTests` tem uma `Theory` com os três perfis e um teste dos motivos
  distintos. O caso incompleto usa o `auth` do seed de dev: o tenant do Entra e o client id vazios. O F&O é trocado pelo
  `ConfigurePrimaryHttpMessageHandler` do cliente nomeado `d365-odata`, e o resto do registro é o do host.
- [x] 4.2 No `D365DocumentDiscoveryTests` e no `D365CompanyDirectoryTests`, o perfil sem `auth` falha com o motivo do
  perfil sem credencial, sem requisição. Os testes de hoje afirmam só o tipo da exceção, e passam a afirmar o texto.

  **Feito (2026-10-05), de outro jeito:** os harnesses dos dois arquivos usam um token falso, que não conhece o perfil.
  Por isso entrou um teste novo em cada arquivo, com o provider de produção e um cofre vazio (`EmptyVault`, ao lado dos
  outros falsos do `D365ChangeFeedTests`). Os testes de settings inválidas continuam como estavam.
- [x] 4.3 `dotnet build` com 0 warnings e `dotnet test` verde.

  **Feito (2026-10-05):** 0 warnings. 1.163 testes passaram, 0 falharam, e 3 foram pulados.

## 5. A descoberta devolve o estabelecimento que resolveu (D5; `period-discovery`)

- [x] 5.1 Teste primeiro, no `D365DocumentDiscoveryTests`, sobre as fixtures da `brmf`:
  - **a `SP-01`:** dá o `EstablishmentTaxId` `44278225000260`;
  - **a `SP-01` num período sem nota:** zero referências e o mesmo `44278225000260`;
  - **"todas" de `44278225000180`:** quatro estabelecimentos, sem CNPJ;
  - **a raiz com um estabelecimento só, pedida com "todas":** num cadastro sintético, dá o CNPJ dele;
  - **a filial fora do cadastro:** zero referências, sem CNPJ, e nenhuma leitura de notas;
  - **a nota da `SP-01` com `442782250099-99`:** dá `44278225000260`, o do cadastro;
  - **o período invertido:** sem CNPJ e sem leitura.

  **Feito (2026-10-05):** sete testes novos, um por caso. Entrou também o CNPJ alfanumérico (`12.ABC.345/01DE-35` dá
  `12ABC34501DE35`, com as letras). O teste do período invertido, que já existia, passou a afirmar o CNPJ vazio.
- [x] 5.2 No `LocalDocumentDiscoveryTests`, o catálogo local devolve sem CNPJ.
- [x] 5.3 Implementar:
  - o `DiscoveryResult` na Application (`Inbound`);
  - a assinatura nova do `IDocumentDiscovery.DiscoverAsync`;
  - o `D365DocumentDiscovery`, com o CNPJ calculado logo depois do escopo e devolvido em toda saída posterior;
  - o `LocalDocumentDiscovery`;
  - o fake do `IntegrationRunnerTests`;
  - as chamadas dos testes, que passam a ler `.References`.

  O `FindByKeyAsync` não muda.
- [x] 5.4 `dotnet build` com 0 warnings e `dotnet test` verde.

  **Feito (2026-10-05):** 0 warnings. 1.171 testes passaram, 0 falharam, e 3 foram pulados (somados por script, nos 10
  projetos de teste).

## 6. A execução grava o CNPJ (D5, D6; `period-discovery`)

- [x] 6.1 Teste primeiro, no `IntegrationRunnerTests`, que a execução gravada leva:
  - o `EstablishmentTaxId` que a descoberta devolveu;
  - nulo, quando a descoberta devolve nulo;
  - o CNPJ, com zero referências.
- [x] 6.2 Teste primeiro, no `SqlExecutionStoreTests` (SQLite):
  - **o CNPJ:** gravado e listado de volta;
  - **sem o CNPJ:** gravado e listado nulo;
  - **uma linha inserida sem a coluna, como a gravada antes da migração:** listada com ela nula.
- [x] 6.3 Teste primeiro, no `FiscalHub.Infrastructure.Tests`, do modelo:
  - o modelo não tem mudança pendente contra o SQL Server (`HasPendingModelChanges()`, sem conexão);
  - a `IntegrationExecutionRow` tem o `EstablishmentTaxId` anulável, com 20 caracteres;
  - a `ScheduledIntegrationRow` não o tem.

  **Feito (2026-10-05):**
  - **antes do campo:** o teste de mudança pendente passou, com o snapshot em dia;
  - **com o campo no modelo e sem a migração:** falhou (`Assert.False() Failure`);
  - **com a migração:** passou.

  A linha antiga entra por um `INSERT` cru, sem a coluna.
- [x] 6.4 Implementar a cadeia:
  - o campo em `IntegrationExecution`, `IntegrationExecutionRow`, `SqlExecutionStore`, `SqlExecutionQueries` e
    `ExecutionSummary`;
  - o `HasMaxLength(20)` no `ProcessingDbContext`;
  - o `IntegrationRunner`, que grava o campo.
- [x] 6.5 Gerar a migração `AddExecutionEstablishmentTaxId` com o `dotnet ef migrations add`. Conferir e colar aqui:
  - o `Up` só adiciona a coluna anulável, sem default, em `IntegrationExecutions`;
  - o `Down` só a derruba;
  - o snapshot mudou só nessa entidade.

  **Feito (2026-10-05):** `20261005191907_AddExecutionEstablishmentTaxId`.

  ```csharp
  // Up
  migrationBuilder.AddColumn<string>(name: "EstablishmentTaxId", table: "IntegrationExecutions",
      type: "nvarchar(20)", maxLength: 20, nullable: true);
  // Down
  migrationBuilder.DropColumn(name: "EstablishmentTaxId", table: "IntegrationExecutions");
  ```

  O snapshot ganhou só o `b.Property<string>("EstablishmentTaxId").HasMaxLength(20).HasColumnType("nvarchar(20)")`, na
  entidade `IntegrationExecutionRow`.
- [x] 6.6 `dotnet build` com 0 warnings e `dotnet test` verde.

  **Feito (2026-10-05):** 0 warnings. 1.179 testes passaram, 0 falharam, e 3 foram pulados.

## 7. A coluna Empresa da tabela de execuções (D7; `company-directory`)

- [x] 7.1 Teste primeiro, no `companyCode.test.ts`, do `formatExecutionCompany`:
  - **com o CNPJ `44278225000260`:** "44.278.225/0002-60";
  - **nulo, sem o campo (a resposta antiga) ou vazio:** a empresa `44278225000180`, como "44.278.225/0001-80";
  - **a empresa de 8 caracteres do catálogo local, sem CNPJ:** "12.345.678".

  **Feito (2026-10-05):** os 5 casos falharam antes de a função existir.
- [x] 7.2 Implementar:
  - o `formatExecutionCompany` ao lado do `formatCompany`;
  - o `establishmentTaxId?: string | null` no `ExecutionSummary` do `types.ts`;
  - a coluna Empresa da `executionColumns` do `IntegrationsPage.tsx`.

  A coluna dos agendamentos não muda.
- [x] 7.3 `npm test` e `npm run build` verdes no `dashboard/`.

  **Feito (2026-10-05):** 50 testes em 6 arquivos passaram, e o build saiu. O `npm run build` avisa de um chunk acima de
  500 kB, aviso do Vite sobre o tamanho do pacote. Esta mudança não acrescenta dependência.

## 8. Mutação nas quatro provas do STATUS

- [x] 8.1 Para cada mutação: aplicar, rodar a suíte afetada, anotar aqui o teste que caiu e desfazer.

  | Mutação | Prova do STATUS | Tem de cair |
  |---|---|---|
  | M1 — no D365, `scope is { Count: 1 }` vira `scope is { Count: > 0 }`, com o primeiro do escopo | 2, "Todas" | o teste de "todas" |
  | M2 — no D365, o CNPJ sempre nulo | 1, `SP-01` | o teste da `SP-01` |
  | M3 — no D365, o CNPJ deduzido das referências achadas (o caminho 2) | 1, com zero notas | o teste da `SP-01` sem nota |
  | M4 — o runner ignora o `EstablishmentTaxId` do resultado | 1 | o teste do runner |
  | M5 — o store não grava a coluna | 1 e 3 | o teste do store |
  | M6a — o `formatExecutionCompany` sempre devolve a empresa | 1 | o teste do CNPJ na tela |
  | M6b — o `formatExecutionCompany` devolve o CNPJ sem cair na empresa | 3, a linha antiga | o teste do nulo na tela |
  | M7 — o CNPJ gravado também no agendamento (a propriedade na `ScheduledIntegrationRow`) | 4 | o teste do modelo |

  Uma mutação que não derruba nenhum teste quer dizer que o teste não existe. Ele é escrito antes de fechar esta
  tarefa, e a mutação é refeita.

  **Feito (2026-10-05):** as oito caíram. Um script aplicou cada mutação, rodou o projeto de testes afetado e desfez. O
  `git diff --stat` dos arquivos mutados saiu igual ao de antes.

  | Mutação | O que caiu |
  |---|---|
  | M1 | `The_whole_company_with_four_establishments_gives_no_cnpj` |
  | M2 | `The_sp01_gives_its_establishment_cnpj`, `The_sp01_without_any_note_still_gives_its_establishment_cnpj`, `A_root_with_a_single_establishment_asked_without_branch_gives_its_cnpj`, `The_alphanumeric_establishment_gives_its_cnpj_with_the_letters`, `A_note_with_another_cnpj_of_the_same_root_does_not_change_the_establishment_cnpj` |
  | M3 | `The_sp01_without_any_note_still_gives_its_establishment_cnpj`, `The_whole_company_with_four_establishments_gives_no_cnpj`, `A_note_with_another_cnpj_of_the_same_root_does_not_change_the_establishment_cnpj`, `A_root_with_a_single_establishment_asked_without_branch_gives_its_cnpj`, `The_alphanumeric_establishment_gives_its_cnpj_with_the_letters` |
  | M4 | `The_execution_records_the_establishment_the_discovery_resolved`, `A_branch_without_notes_still_records_its_establishment` |
  | M5 | `The_establishment_cnpj_is_recorded_and_listed_back` |
  | M6a | "a execução da SP-01 mostra o CNPJ do estabelecimento, e não o da matriz" |
  | M6b | "a execução de todas as filiais, sem o CNPJ, mostra a empresa", "a execução gravada antes do campo, sem ele na resposta, mostra a empresa", "o CNPJ vazio conta como ausente", "a empresa de 8 caracteres do catálogo local, sem o CNPJ, ganha a máscara da raiz" |
  | M7 | `Only_the_execution_has_the_establishment_cnpj`, `The_model_has_no_change_pending_against_the_sql_server_migrations` |

  Na M3, o teste da `SP-01` com notas passa, porque as duas notas trazem o CNPJ da `SP-01`. É o teste sem nota que pega
  o caminho 2, como o design previa.

## 9. Os docs e o ADR (D9, D10)

- [x] 9.1 O `docs/RUNNING.md`:
  - **o pré-requisito "A identidade no F&O" (linhas 297-306):** reescrito com a credencial pela tela, a linha de log
    nova e o que acontece sem ela;
  - **o SQL do passo 1:** passa a usar `JSON_MODIFY` só no `pageSize`, no `poll.enabled` e no `poll.startFrom`.

  Rodar o SQL novo contra o SQL do docker, com o `auth` preenchido. Colar o `InboundSettings` antes e depois, com o
  `auth` igual.

  **Feito (2026-10-05).** O SQL rodou contra o SQL do docker dentro de uma transação com `ROLLBACK`, para não mudar o banco
  de dev. O `auth` é o que a tela gravou em 2026-10-01; abrevio os GUIDs:

  ```
  ANTES  {"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"],"pageSize":500,"auth":{"tenantId":"79b8dfc5-…","clientId":"88e5c98e-…","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"},"poll":{"enabled":true,"intervalSeconds":60,"overlapSeconds":300}}
  DEPOIS {"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"],"pageSize":20,"auth":{"tenantId":"79b8dfc5-…","clientId":"88e5c98e-…","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"},"poll":{"enabled":false,"intervalSeconds":60,"overlapSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}}
  ```

  Depois do `ROLLBACK`, o perfil voltou igual ao ANTES.

  **Mudança em relação ao design:** o teste mostrou que, sem a seção `poll`, o `JSON_MODIFY` deixa o `enabled` e o
  `startFrom` de fora, sem erro. O SQL ganhou uma primeira troca, que cria a seção quando ela falta. Com um perfil sem
  `poll`, numa tabela temporária, o resultado foi `"poll":{"enabled":false,"startFrom":"2015-01-01T00:00:00Z"}`.
- [x] 9.2 O `docs/adr/0035-credencial-do-perfil-e-o-cnpj-da-execucao.md`, pelo `0000-template.md`, com as duas decisões
  do D10 e as alternativas do D1 e do D5.

  **Feito (2026-10-05):** além do ADR, o índice (`docs/adr/README.md`) ganhou a linha dele. O ADR-0034 ganhou o
  "Revisado por" no cabeçalho e a nota na "Piora", como os ADRs revistos antes (o 0027 e o 0032).
- [x] 9.3 Conferir pelo `git diff --stat` que não mudaram: `docs/STATUS.md:1150`, `openspec/changes/archive/`,
  `d365/postman/README.md`, `d365/06-receita-criar-entidade-na-mao.md` e o ADR-0032.

  **Feito (2026-10-05):** o `git diff --stat` desses caminhos saiu vazio. O mesmo vale para
  `openspec/changes/platform-establishment-resolution`. O `docs/STATUS.md` inteiro está intocado até a 10.7.

## 10. Verificação final e provas contra o fiscosysdev

- [x] 10.1 `dotnet build -warnaserror` limpo e `dotnet test` da solução inteira verde. Colar as contagens.

  **Feito (2026-10-05):**
  - `dotnet build -warnaserror --no-incremental`: 0 warnings e 0 erros;
  - `dotnet test`: 1.179 testes passaram, 0 falharam e 3 foram pulados (os dois contra o F&O real e o do emulador do
    cofre), nos 10 projetos de teste;
  - `npm test` no dashboard: 50 testes em 6 arquivos passaram.
- [x] 10.2 `openspec validate --all --strict` verde.

  **Feito (2026-10-05):** 23 passaram e 0 falharam (as 21 specs e as 2 changes ativas).
- [ ] 10.3 Os testes contra o F&O real, com as quatro variáveis:
  - o do feed lê os 83 cabeçalhos da `brmf`;
  - o da montagem monta as NF-e 55;
  - sem o `FISCALHUB_D365_CLIENT_SECRET`, os dois aparecem pulados, e o motivo nomeia só ele.
- [ ] 10.4 O gravador com `-DirectoryOnly` e as variáveis: o `git diff` das fixtures sai vazio. Se não sair, investigar
  antes de versionar.
- [x] 10.5 A identidade, com o host em Development e uma sessão do `az login` ativa:
  - **com a credencial gravada pela tela:** o log tem "D365: o tenant tenant-a autentica no F&O com a credencial do
    próprio tenant (client credentials: app …)" uma vez;
  - **com o Client ID apagado pela tela:** o dropdown de empresas e o quadro Situação mostram "falta Client ID", e o log
    não tem linha de identidade nem requisição ao F&O;
  - **a volta:** o Client ID restaurado. Comparar o `GET /connector` de antes e de depois.

  **Feito (2026-10-05), com o host local em Development** (`dotnet run`, porta 5200), com a migração
  `AddExecutionEstablishmentTaxId` aplicada na subida. A sessão do `az` do desenvolvedor estava ativa, no mesmo tenant do
  Entra do app.
  - **Com a credencial gravada pela tela:** na primeira passada do coletor, o log teve uma linha só de identidade:

    ```
    D365: o tenant tenant-a autentica no F&O com a credencial do próprio tenant (client credentials: app 88e5c98e-…, tenant do Entra 79b8dfc5-…).
    ```

  - **Com o Client ID apagado:** o `PUT /connector` levou o `GET` inteiro com só o `auth.clientId` vazio (204, às
    19:40:30Z).
    - **O quadro Situação** (`GET /connector/automatic`), na passada seguinte: `consecutiveFailures: 1`, `lastError: "A
      credencial do ERP está incompleta: falta Client ID. Configure em Configurações → Conectores → Entrada."`. A marca
      não andou.
    - **O dropdown** (`GET /companies`): HTTP 502, `"Configuração do ERP: A credencial do ERP está incompleta: falta
      Client ID. Configure em Configurações → Conectores → Entrada."`.
    - **O log:** as requisições ao F&O ("Start processing HTTP request GET https://fiscosysdev…") ficaram em 1, e as
      linhas de identidade em 1. A sessão do `az` não foi usada.
  - **A volta:**
    - o `PUT /connector` com o `GET` de antes (204, às 19:41:47Z);
    - o `GET /connector` de depois saiu idêntico ao de antes, byte a byte (1.442 caracteres);
    - na passada seguinte, as falhas voltaram a 0, a marca andou e houve requisição nova ao F&O;
    - nenhuma linha de identidade nova, porque o app é o mesmo.
- [ ] 10.6 A coluna, contra o fiscosysdev e com a saída apontada para o mock:
  - **a `SP-01`:** uma execução da empresa `44278225000180` com a filial `SP-01` mostra `44.278.225/0002-60`;
  - **"Todas":** a execução mostra `44.278.225/0001-80`;
  - **a linha antiga:** a execução 16, gravada antes da coluna, continua com ela nula e mostra `44.278.225/0001-80`;
  - **os agendamentos:** a tabela de agendamentos está igual à de antes da change. Comparar o `GET /schedules` e a tela;
  - **o banco:** colar o `SELECT Id, CompanyCode, BranchCode, EstablishmentTaxId FROM IntegrationExecutions ORDER BY Id
    DESC` com as três linhas.

  **Parcial (2026-10-05):** provados o dado, o banco e o formatador; falta a conferência visual da tela. Escolha do
  Marcelo: o período foi 2026-08-07, em que a `SP-01` só tem 2 NFS-e, que o roteamento ignora. A saída do perfil não foi
  apontada para o mock, e nada chegou a ela: o log teve 0 chamadas à Avalara antes e depois, e as duas NFS-e ficaram
  `Ignored` às 19:42:38Z, com o gatilho `Manual`.
  - **as execuções:** a integração manual de `44278225000180` com a `SP-01` virou a execução 19, e a de "Todas" virou a
    20. As duas acharam 2 notas;
  - **o banco:**

    ```
    Id|Mode|CompanyCode|BranchCode|EstablishmentTaxId|PeriodStart|PeriodEnd|DiscoveredCount
    20|Manual|44278225000180|NULL|NULL|2026-08-07|2026-08-07|2
    19|Manual|44278225000180|SP-01|44278225000260|2026-08-07|2026-08-07|2
    16|Manual|44278225000180|SP-01|NULL|2015-01-01|2026-08-31|7
    ```

  - **o `GET /executions`:** traz o `establishmentTaxId` `44278225000260` na 19, e vazio na 20 e na 16;
  - **a coluna:** o `formatExecutionCompany` dá "44.278.225/0002-60" para a 19 e "44.278.225/0001-80" para a 20 e a 16.
    São os mesmos valores dos testes da 7.1;
  - **os agendamentos:** o `GET /schedules` de depois saiu idêntico ao de antes (o agendamento 2, da `Matriz`, inativo).

  **Falta:** abrir a tela Agendamento → Execuções e ver a coluna Empresa das execuções 19, 20 e 16, e a tabela de
  agendamentos.
- [ ] 10.7 Fechar no `docs/STATUS.md` o item "A coluna Empresa das tabelas de agendamento e de execução mostra o CNPJ da
  matriz", com a data e as provas da 10.6. A linha 1150 não muda.
