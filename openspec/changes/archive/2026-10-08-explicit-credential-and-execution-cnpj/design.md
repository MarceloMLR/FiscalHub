## Context

A motivação está no `proposal.md`, e os requisitos, nas specs da change. Aqui ficam o estado do código que molda o
desenho e as escolhas.

**O token do D365 hoje.**

- **Os registros:** o `AddShared` registra o `ClientCredentialsD365TokenProvider` como `ID365TokenProvider` (singleton).
  Em Development, o `Program.cs:186-191` chama o `UseD365AzureCliFallback()`, que troca esse registro pelo
  `D365DevelopmentTokenProvider`.
- **O que o `D365DevelopmentTokenProvider` faz:** decide por chamada.
  - Com o `auth` completo e o segredo no cofre, chama o client credentials.
  - Sem `auth`, com o `auth` incompleto ou sem o segredo no cofre, chama o `AzureCliD365TokenProvider`.
  - Com a referência fora do prefixo do tenant, chama o client credentials, que recusa.
- **O log da identidade:** é o `D365DevelopmentTokenProvider` que o escreve, e por isso ele só existe em Development.
- **O teste de credencial:** já usa uma instância própria do `ClientCredentialsD365TokenProvider`, pelo
  `GetFreshTokenAsync`, e nunca passou pelo fallback.

**Os três motivos já existem no client credentials,** no `ResolveCredentialAsync`, como `ConnectorSettingsException`:

- `auth` nulo;
- `RequireComplete()`;
- o cofre sem o segredo.

Só o incompleto perde informação. Ele diz "informe tenantId, clientId e clientSecretRef", e não qual campo falta. O
`D365DevelopmentTokenProvider` dizia.

**O caminho do `ConnectorSettingsException` até a tela já existe:**

- **o coletor:** o `ChangeFeedPoller` grava o `ex.Message` como falha do cursor, e o painel da integração automática a
  mostra como último erro;
- **as leituras a pedido de uma tela:** o `D365ReadFailures.GuardAsync` repassa a exceção como veio;
- **a integração manual:** o endpoint responde 502 com "Configuração do ERP: …";
- **o diretório:** o `CompanyDirectoryQuery` mostra o motivo no lugar do dropdown;
- **o teste de credencial:** devolve `Incomplete`.

**A descoberta e a execução.**

- **O escopo:** o `D365DocumentDiscovery.DiscoverAsync` resolve o
  `IReadOnlyList<D365Establishment>? scope` antes de ler qualquer nota. Cada estabelecimento traz o `Cnpj` já
  normalizado.
- **A porta:** o `IDocumentDiscovery.DiscoverAsync` devolve só `IReadOnlyList<DocumentReference>`.
- **Quem implementa a porta:** o D365, o `LocalDocumentDiscovery` e um fake nos testes do runner.
- **Os providers do banco:** a tabela `IntegrationExecutions` vive em dois.
  - **SQL Server:** pelas migrações, aplicadas na subida do host pelo `MigrateProcessingSchemaAsync`.
  - **SQLite:** nos testes de Infrastructure, pelo `EnsureCreated` do modelo.

## Goals / Non-Goals

**Goals:**

- Um provider de token só no adapter do D365, sem ramo por ambiente, nem no adapter, nem no host.
- Os três motivos distintos e legíveis, e o incompleto nomeando o que falta.
- O log da identidade com o mesmo texto da linha "credencial do próprio tenant" de hoje. Assim o `grep` das provas já
  fechadas continua achando a linha.
- O CNPJ do estabelecimento resolvido chega à execução também com zero notas, sem leitura nova ao ERP.

**Non-Goals:**

- Unificar as duas instâncias do `ClientCredentialsD365TokenProvider`, a do coletor e a do teste de credencial.
- Mudar o seed de dev: o `auth` com o tenant e o client id vazios continua. Ele passa a dar o motivo "falta", e é isso
  que se quer.
- Mostrar a identidade no painel. O log basta, e o painel mostra a falha.

## Decisions

### D1. O fallback sai inteiro, e o client credentials é o único provider

**O que sai:**

- `AzureCliD365TokenProvider.cs`;
- `D365DevelopmentTokenProvider.cs`;
- o `UseD365AzureCliFallback()`;
- o bloco `if (builder.Environment.IsDevelopment())` do `Program.cs`, com a chamada.

**O que fica:** o `AddShared` continua registrando o `ClientCredentialsD365TokenProvider` como o único
`ID365TokenProvider`. O pacote `Azure.Identity` fica: o `ClientSecretCredential` é dele.

**Os comentários que citam o Azure CLI como caminho vivo mudam,** sem mudar comportamento:

- `ID365TokenProvider`, de "duas implementações" para uma;
- `D365InboundSettings.Auth`, que dizia "opcional quando o host usa o Azure CLI";
- `D365CredentialTest`;
- o comentário do registro do teste em `D365PollServiceCollectionExtensions`;
- `InboundAdapterChoice`, que citava "o desenho do Azure CLI do D365";
- o comentário do seed de dev, que dizia que "em dev o token vem do Azure CLI".

**O `D365InboundSettings.Parse` continua aceitando o perfil sem `auth`.** A tela grava a URL antes da credencial, e a
falta vira o motivo na hora do token (D2), num lugar só.

**Alternativas:**

- **O fallback por opt-in de configuração** (`D365:AllowAzureCli`). Rejeitada: recria, com uma chave a mais, o caminho
  silencioso que é o defeito. Quem liga esquece de desligar.
- **Manter o fallback e mostrar a identidade no painel.** Rejeitada: o painel diria a verdade, mas a credencial do
  perfil continuaria sem prova até o primeiro deploy. E manteria dois providers e um ramo por ambiente.

### D2. Sem credencial, quem recusa é o provider, com `ConnectorSettingsException`

O `ClientCredentialsD365TokenProvider` continua lançando a `ConnectorSettingsException` nos três casos, antes de criar
credencial ou pedir token. Ninguém recusa antes dele.

**Por que no provider, e não numa recusa antes:**

- **É o ponto por onde todos passam:** os quatro leitores (o feed, o source, o diretório e a descoberta) e o teste de
  credencial. Uma checagem antes dele teria de ser repetida em cada leitor, ou no `D365ODataClient`, e o teste de
  credencial continuaria dependendo da exceção do provider.
- **O caminho até a tela já existe** para essa exceção, nos cinco destinos do Context. Nada novo a ligar.
- **Os três ramos já estão no `ResolveCredentialAsync`.** Mudam só os textos.

**A ordem das checagens é a de hoje:** `auth` nulo; os campos vazios ou em branco; o formato `kv:`; o prefixo do tenant;
o cofre. Os textos, com os nomes da tela:

| Caso | Motivo |
|---|---|
| sem `auth` | "A credencial do ERP não está configurada: o perfil não tem Tenant do Entra ID, Client ID nem Client Secret. Configure em Configurações → Conectores → Entrada." |
| `auth` incompleto | "A credencial do ERP está incompleta: falta {campos}. Configure em Configurações → Conectores → Entrada." Os campos são só os que faltam, na ordem Tenant do Entra ID, Client ID, Client Secret, como "Tenant do Entra ID e Client ID". |
| o cofre sem o segredo | "O Client Secret do ERP do tenant '{tenant}' não está no cofre. Grave o Client Secret de novo em Configurações → Conectores → Entrada." |

**O incompleto mora no `D365AuthSettings.RequireComplete()`,** que passa a montar a lista. O `clientSecretRef` ausente
aparece como "Client Secret": pela tela, a referência só nasce quando o segredo é gravado.

**Os dois motivos de referência errada** (sem `kv:` e fora do prefixo) não mudam. Eles são credencial errada, e não
falta de credencial.

### D3. O log da identidade vai para o `ClientCredentialsD365TokenProvider.GetTokenAsync`

**Quando sai:** depois de resolver a credencial e antes de pedir o token. É o mesmo ponto do
`D365DevelopmentTokenProvider` de hoje. Uma credencial recusada pelo Entra ID também deixa a linha, e ela diz qual app foi
recusado.

**A regra:** um `ConcurrentDictionary<string, string>` por tenant, como hoje. A identidade é o par (client id, tenant do
Entra). A troca do segredo não entra na chave, então não reloga.

**O texto** é o da linha "completa" de hoje, para os `grep` das provas fechadas continuarem valendo:

```
D365: o tenant {Tenant} autentica no F&O com a credencial do próprio tenant (client credentials: app {ClientId}, tenant do Entra {EntraTenant}).
```

**O construtor ganha um `ILogger<ClientCredentialsD365TokenProvider>`.** O DI o passa nos dois registros, o do coletor e
o do teste de credencial. Os testes usam um logger de lista.

**O `GetFreshTokenAsync`, o do teste de credencial, não loga a identidade.** A instância do teste é outra. O desfecho do
teste já tem linha própria no `ConnectorCredentialTestService`, e a linha de identidade fala do conector, e não do teste.

**Alternativa:** um decorator só de log em volta do provider. Rejeitada: seria uma classe para uma linha, e o provider é
o único que conhece o par resolvido sem ler o cofre de novo.

### D4. A prova de que nenhum provider de Azure CLI existe, no `D365PollRegistrationTests`

O teste `Azure_cli_fallback_enters_only_when_asked` sai. Entram três afirmações:

1. **Com os quatro registros do adapter juntos,** cada registro de `ID365TokenProvider` resolve um
   `ClientCredentialsD365TokenProvider`, e há um só. Os quatro são `AddD365ChangeFeed`, `AddD365GoodsInvoiceSource`,
   `AddD365CompanyDirectory` e `AddD365DocumentDiscovery`, como no host.
2. **O único tipo concreto do assembly do adapter que implementa `ID365TokenProvider` é o
   `ClientCredentialsD365TokenProvider`.** É o que torna "em Development inclusive" verdade por construção: o adapter não
   lê o ambiente, e não sobra implementação para um ramo de ambiente escolher.
3. **Os métodos estáticos públicos do `D365PollServiceCollectionExtensions` são exatamente os quatro `Add*`.** Não sobra
   nenhum `Use*` que um host chamaria sob `IsDevelopment()`.

O `Program.cs` não tem teste de host: a solução não sobe o host em teste. A prova 3 cobre o que ele poderia chamar, e a
prova manual (tarefas) confere o log da identidade com o host em Development.

### D5. A porta `IDocumentDiscovery` devolve o escopo resolvido (o caminho 1)

**A forma nova:** um `DiscoveryResult` na Application (`Inbound`).

```csharp
public sealed record DiscoveryResult(IReadOnlyList<DocumentReference> References, string? EstablishmentTaxId = null);

Task<DiscoveryResult> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken ct = default);
```

O `FindByKeyAsync` não muda.

**O D365:** calcula `scope is { Count: 1 } ? scope[0].Cnpj : null` logo depois de resolver o escopo, e devolve esse
valor em toda saída posterior, inclusive na leitura que não achou nota. O `return []` do escopo vazio vira
`new DiscoveryResult([])`.

- **O período invertido sai antes do escopo,** e devolve sem CNPJ. O pedido é inválido, e resolver o escopo custaria uma
  leitura ao ERP para nada.

**O `LocalDocumentDiscovery`:** devolve `new DiscoveryResult(matches)`. O catálogo não conhece o CNPJ do estabelecimento.

**O `IntegrationRunner`:** enfileira `result.References` e grava `result.EstablishmentTaxId` na `IntegrationExecution`.

**Os três caminhos pesados:**

- **1. A porta devolve o escopo (escolhido).**
  - **A favor:** é explícito, e o tipo diz o que a descoberta sabe. Funciona com zero notas. O CNPJ é o do
    estabelecimento que a descoberta realmente procurou, pela mesma leitura e na mesma regra (`IsSameCompany` e o
    código).
  - **O custo:** é mecânico. Dois adapters, o fake do runner e cerca de 27 chamadas nos testes, que passam a ler
    `.References`.
- **2. O runner deduz pelo `CompanyCode` das referências.**
  - **Não funciona com zero notas:** a execução da filial que não achou nada fica nula e volta a mostrar a matriz. É o
    defeito que a change fecha.
  - **Depende do D8:** o `CompanyCode` é o CNPJ completo no D365 e a raiz no XML.
  - **Erra quando há mais de um `CompanyCode`:** dá nulo com o escopo resolvido num só, como na nota da `SP-01` com
    outro CNPJ da mesma raiz (o cenário "Outro CNPJ da mesma raiz" de `period-discovery`).
  - **Não serve ao catálogo local:** as referências dele não têm metadata.
- **3. O runner pergunta ao `ICompanyDirectory.ListBranchesAsync`.**
  - **É uma segunda leitura ao ERP por execução:** o agendador roda sem ninguém olhando, e o throttling do F&O é
    orçamento (ADR-0025).
  - **Duplica a regra do escopo no runner:** a raiz e o código, que vivem no adapter.
  - **As duas leituras podem discordar:** o CNPJ gravado não seria, com certeza, o do estabelecimento procurado.
  - **Abre uma pergunta sem resposta boa:** o diretório falha depois de a descoberta enfileirar. Falhar a execução já
    enfileirada, ou gravar nulo em silêncio?
- **4. Outros:** um canal lateral, como um `IProgress<string>` no critério ou um campo que a descoberta preenche no
  `DiscoveryCriteria`. Rejeitados: mudam o contrato da porta do mesmo jeito, só que sem o tipo dizer.

**O D8:** o caminho 1 não lê nem escreve o `CompanyCode` das referências, então não o toca. O CNPJ da execução é o do
cadastro, e não o da nota.

### D6. A coluna `EstablishmentTaxId`, anulável, só em `IntegrationExecutions`

**A cadeia:** o campo `string? EstablishmentTaxId` passa por todo o caminho:

- `IntegrationExecution`;
- `IntegrationExecutionRow`;
- `SqlExecutionStore`, que grava;
- `SqlExecutionQueries`, que projeta;
- `ExecutionSummary`, que vira o `establishmentTaxId` do `GET /executions`.

**O modelo:** `HasMaxLength(20)`, como o `CompanyCode`. O CNPJ normalizado tem 14 caracteres.

**A migração:** `AddExecutionEstablishmentTaxId`, gerada pelo `dotnet ef migrations add`. É um `AddColumn` anulável, sem
default, e o `Down` derruba a coluna. A linha existente fica nula.

**Os dois providers:**

- **SQL Server:** a migração.
- **SQLite:** o mesmo modelo, pelo `EnsureCreated`.
- **Um teste guarda a migração:** o modelo não tem mudança pendente contra o SQL Server (`HasPendingModelChanges()`, sem
  conexão). Sem ele, a migração esquecida só apareceria na subida do host.

**O que não muda:**

- o seed de demonstração: as execuções semeadas ficam nulas, que é o caso da linha antiga;
- o `ScheduledIntegrationRow` e a tabela `ScheduledIntegrations`.

**O nome:** `EstablishmentTaxId`, e não `BranchTaxId`. O valor existe também na execução de "todas" cuja raiz tem um
estabelecimento só, sem filial gravada. E o diretório chama o CNPJ de `TaxId`.

### D7. A coluna Empresa da tabela de execuções

**A regra mora num formatador só.** O `companyCode.ts` ganha o `formatExecutionCompany(execution)`, que devolve
`formatCompany(execution.establishmentTaxId || execution.companyCode)`. O `||` trata o vazio como ausente. Ele fica ao
lado do `formatCompany`, que é o formatador único da tela, e tem teste no `companyCode.test.ts`.

**A tela:** a `executionColumns` do `IntegrationsPage.tsx` passa a usá-lo. A coluna de agendamentos não muda. O
`ExecutionSummary` do `types.ts` ganha `establishmentTaxId?: string | null`.

**Sem consulta ao diretório:** a tela só lê o que o `GET /executions` traz.

### D8. Os testes contra o F&O real e o gravador pedem a credencial por variável de ambiente

**As variáveis:**

| Variável | O quê |
|---|---|
| `FISCALHUB_D365_URL` | o ambiente F&O (já existe) |
| `FISCALHUB_D365_ENTRA_TENANT_ID` | o tenant do Entra do app |
| `FISCALHUB_D365_CLIENT_ID` | o client id do app |
| `FISCALHUB_D365_CLIENT_SECRET` | o client secret do app |

**Por que não `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` e `AZURE_CLIENT_SECRET`:** o `EnvironmentCredential` e o
`DefaultAzureCredential` leem essas variáveis em qualquer processo da máquina. Defini-las para rodar um teste mudaria em
silêncio a identidade de outras ferramentas, que é o tipo de defeito que esta change fecha.

**Por que `ENTRA_TENANT_ID`, e não `TENANT_ID`:** o hub tem tenant próprio (`tenant-a`).

**Os testes:**

- **O pulo:** o `D365IntegrationFactAttribute` pula quando falta qualquer uma das quatro. O motivo nomeia as que faltam:
  "Integração com F&O real: falta FISCALHUB_D365_CLIENT_SECRET. Defina FISCALHUB_D365_URL,
  FISCALHUB_D365_ENTRA_TENANT_ID, FISCALHUB_D365_CLIENT_ID e FISCALHUB_D365_CLIENT_SECRET (o app do conector) para
  rodar."
- **Uma classe pequena, `D365IntegrationEnvironment`, faz a conta do que falta** a partir de uma função de leitura. Ela
  tem teste de unidade, que roda sem rede e sem variável.
- **O token vem do caminho de produção:** os dois testes passam a usar o `ClientCredentialsD365TokenProvider` real. O
  segredo fica num cofre em memória, sob `fh-tenant-a--inbound--auth--clientsecret`, e o perfil leva o `auth` com a
  referência `kv:`. A regra do prefixo e do cofre fica exercitada.

**O gravador (`Record-D365Fixtures.ps1`):**

- lê as mesmas três variáveis da credencial. A URL e a empresa continuam por parâmetro;
- pede o token por client credentials, com `Invoke-RestMethod` no
  `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` e o escopo `{url}/.default`;
- falha nomeando as variáveis que faltam;
- nunca imprime o segredo nem o token. O segredo não vai em parâmetro, para não ficar no histórico do shell.

**Gravar com o app é mais fiel** que gravar com a identidade delegada do desenvolvedor: a resposta é a que o conector
recebe, com a role `FSFiscalHubIntegration`.

### D9. O `RUNNING.md` ensina a credencial pela tela, e o SQL do poll deixa o `auth` em paz

**O pré-requisito "A identidade no F&O" é reescrito:**

- o Tenant do Entra ID, o Client ID e o Client Secret vão pela tela (Configurações → Conectores → Entrada);
- a linha de log nova;
- sem a credencial, o painel e o dropdown mostram o motivo, e a integração não lê o F&O.

**O SQL do passo 1:** hoje ele grava o `InboundSettings` inteiro, com o `auth` vazio, e apagaria a credencial gravada
pela tela. Ele passa a usar `JSON_MODIFY` só no `pageSize`, no `poll.enabled` e no `poll.startFrom`. A ordem entre o
SQL e a tela deixa de importar, e o passo não depende de lembrança.

**O README do gravador** troca o `az login` pelas variáveis.

**O que não muda:** `docs/STATUS.md:1150`, o ADR-0032 §3, `openspec/changes/archive/`, o README do Postman e a receita
do `d365/06`. Os dois últimos falam com as entidades direto, sem o conector.

### D10. ADR-0035

O ADR registra as duas decisões de arquitetura:

- a credencial do perfil é a única identidade do conector contra o F&O, sem modo de desenvolvimento;
- a porta de descoberta devolve o escopo resolvido, e a execução grava o fato, e não o critério.

O ADR-0032 não muda. O §3 dele cita "o desenho do Azure CLI" como o padrão que o mock seguiu, e o mock continua.

## Risks / Trade-offs

- **[Um banco novo de dev para de ler o F&O até alguém preencher a tela]** → É o comportamento pedido. Mitigação: o
  motivo nomeia o que falta e onde corrigir, e o `RUNNING.md` traz o passo.
- **[O segredo do app numa variável de ambiente da máquina de dev]** → Fica no escopo da sessão (`$env:`), nunca em
  arquivo versionado. O README do gravador e o comentário do teste dizem isso.
- **[O gravador com o app pode ver dados diferentes dos que o desenvolvedor via]** → A prova manual regrava só o
  `-DirectoryOnly` e confere que o `git diff` das fixtures sai vazio. Se não sair, a diferença é investigada antes de
  versionar.
- **[A execução 16 e as anteriores continuam mostrando o CNPJ da matriz]** → Foi decidido: elas não são preenchidas
  depois, e a prova 3 do STATUS é exatamente essa.
- **[A nota com outro CNPJ da mesma raiz mostra, na execução, o CNPJ do cadastro]** → É o fato da descoberta: o
  estabelecimento procurado. O cenário de `period-discovery` o fixa.
- **[A mudança da porta quebra implementações de fora do repositório]** → Não há: as duas implementações são deste
  repositório.
- **[A linha de identidade sai de novo a cada reinício do host]** → É o comportamento de hoje: o registro é em memória.

## Migration Plan

1. **A subida:** o host aplica a migração na subida (`MigrateProcessingSchemaAsync`). A coluna nasce anulável, e as
   linhas existentes ficam nulas.
2. **Em dev:** o banco do docker recebe a migração na primeira subida do host. Quem usava o `az login` grava a
   credencial pela tela.
3. **O rollback:** o `Down` derruba a coluna. O código anterior não a lê, e as execuções gravadas no meio perdem só o
   CNPJ. Não há tenant em produção.
