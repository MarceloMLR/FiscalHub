## Why

O FiscalHub nasceu com **um** destino. O `TenantConnectorProfile` tem um `OutboundAdapter`, um blob de settings e uma
lista de módulos marcada no tenant; as settings de saída aninham `{ "sandbox": {…}, "production": {…} }`; e o nome do
segredo no cofre é `fh-{tenant}--outbound--sandbox--clientsecret`. A singularidade do destino e a escolha de ambiente
vazaram para a **forma do dado** e para o **nome da chave do cofre**.

O que o levantamento com o Marcelo mostrou (ADRs 0036 e 0037):

- **o Tax Compliance não recebe só nota fiscal.** Recebe plano de contas, lançamento e saldo (Contábil) e os registros
  do Bloco K (Inventário) — cada um num endpoint próprio, numa **base diferente** da do `/dfe`;
- **a credencial é por ambiente, e não por ferramenta.** Um `clientId`/`clientSecret` por ambiente serve todos os
  produtos da plataforma;
- **cada ambiente vira um deploy**, com grupo de recursos e host próprios;
- **nem todo cliente tem todos os módulos**, e quem configura isso é a Fiscosys, não o cliente.

O primeiro cliente real entra agora. **Config viva é a coisa mais cara de migrar depois:** o que hoje é uma migração EF
numa base sem cliente vira, com cliente em produção, janela e risco.

## What Changes

Esta change faz **a forma de dado**, e o mínimo de comportamento que a forma obriga: o envio passa a ler o caminho da
integração, e a tela de configuração passa a gravar a forma nova. Nenhuma fila nova, nenhum AvaTax, nenhum endpoint real
de Contábil ou de Inventário. O que da tela é obrigatório, e o que veio do desenho do canvas, está no §7.

### 1. O perfil ganha uma conta de plataforma e uma coleção de conectores

```
tenant
└── conta de plataforma: Avalara         ← clientId + referência do segredo + endpoint de token
    └── conector: Avalara                ← URLs base por versão
        ├── módulo Fiscal      → integração Notas Fiscais   (endpoint, sem lote)
        ├── módulo Contábil    → plano de contas, lançamento, saldo
        └── módulo Inventário  → K200, K210/K215, K230
```

- a **conta** guarda a credencial e o endpoint de token; uma por plataforma, por tenant;
- o **conector** é identificado pelo adapter, único dentro da conta, e guarda **uma URL base por versão de API** que o
  adapter declara. A versão é um rótulo que escolhe a base, e nunca entra na URL: o prefixo da API fica no caminho;
- o **módulo** é o domínio contratado, e vem do conector — não mais do tenant;
- a **integração** é a unidade operacional: endpoint próprio e, onde a API aceita, tamanho de lote próprio.

### 2. O adapter declara, o perfil liga

O adapter declara quais módulos atende, quais integrações cada módulo tem, qual versão de base cada integração usa e
**se aquela integração aceita lote**. O perfil liga o que o cliente contratou e preenche o que é dele: o caminho e o
tamanho do lote. A chave da integração é vocabulário do hub, e não do adapter.

**No máximo um conector ligado por integração, por tenant.** Dois é erro de configuração, recusado na gravação.

**Uma integração sem caminho fica inativa:** ela existe e não é despachada. Bloquear a gravação produziria conector pela
metade.

### 3. A credencial sobe para a conta

Nenhum conector tem a credencial da plataforma, e o cache de token chaveia pela **conta** — senão dois conectores pedem
dois tokens para a mesma credencial, e a mesma referência `kv:` acaba duplicada em dois lugares da config. O endpoint de
token (`tokenUrl`) é **obrigatório** na conta: não existe derivação a partir da URL base de um conector.

### 4. O ambiente sai do perfil e vira propriedade da instância

O `Environment` sai do `ConnectorProfileRequest`, da requisição do teste de credencial e do formulário; as settings de
saída perdem o aninhamento `sandbox`/`production`; o caminho do segredo perde o segmento de ambiente; e o host recusa
subir sem o ambiente configurado, na chave que já existe (`Connector:Environment`). Saem também os padrões silenciosos
de hoje, no `/info` e no selo da tela.

### 5. O envio lê o caminho da integração

O adapter acha o **próprio** conector pelo nome do adapter, e a integração dentro dele: o envio e a consulta de status
usam a base da versão dela e o caminho dela, e não mais o `Avalara:DocumentsPath` do host. Não entra seletor de
despachante: com um conector por adapter, escolher entre conectores é operação nula.

### 6. Os módulos saem do tenant e passam para o conector

O `Modules` deixa de ser uma lista marcada no tenant. **O `/info` continua devolvendo a mesma lista plana** — agora
calculada como a união dos módulos ligados nos conectores do cliente. A barra lateral não muda nesta fatia; o
reagrupamento por conector é do ADR-0039.

### 7. A tela de configuração

A tela muda por força da forma do dado. **Obrigatório:** sai o seletor de ambiente e saem os dois blocos; entra o bloco
da conta (`clientId`, Client Secret, `tokenUrl` e o teste); entram os campos do conector (URLs base por versão), os
módulos marcados por conector no lugar da aba "Módulos" do cliente, e as integrações com caminho, lote e pendência. Cada
parte entra junto com a forma de dado que ela segue.

**Veio do desenho do canvas** (ADR-0039), e não é obrigado pela forma do dado: a lista de conectores, o painel do
conector em três abas e o módulo não declarado mostrado apagado, com o motivo. Fica num grupo próprio, no fim, e pode
sair desta fatia sem afetar o resto.

### 8. A migração

A base existente tem um perfil com um conector, dois blocos de ambiente e uma lista de módulos. A migração monta a
conta a partir do `OutboundAdapter`, com o `clientId` e o `tokenUrl` da seção do ambiente gravado; cria **um** conector
com a URL base da seção como base da `v2` e o resto das settings; descarta a seção do outro ambiente; liga o módulo
Fiscal com a integração de notas; e passa os módulos gravados do tenant para esse conector.

- **O segredo não é copiado, nem a referência dele:** o nome muda, e uma migração de schema não fala com o cofre.
  Depois dela o segredo aparece como não configurado, e o envio falha alto apontando o campo.
- **O caminho da integração de notas vem do host:** a migração o deixa à espera, e o host o preenche na subida, uma
  vez, a partir do `Avalara:DocumentsPath`.
- **São três migrações EF**, e cada uma tem o porquê no `design.md` (D13). **Só a conversão do ambiente é de ida
  só:** desfazê-la falha com uma mensagem que diz isso. A conta e o conector, e os módulos e as integrações, têm volta.

## Capabilities

### New Capabilities

- `platform-account-connectors`: a forma do perfil. A conta, sua credencial e seu endpoint de token; a coleção de
  conectores com URLs base por versão; os módulos e as integrações declarados pelo adapter e ligados pelo perfil; o
  endpoint e o tamanho de lote por integração; a regra de um conector por integração; a integração inativa; o ambiente
  lido da instância; a migração; e o caminho que o host preenche uma vez.

### Modified Capabilities

- `module-navigation`: os módulos deixam de ser uma lista do tenant e passam a vir dos conectores. O `/info` continua
  com a mesma forma de resposta, e a marcação sai da aba "Módulos" e vai para o painel do conector. A regra de que
  módulo é apresentação ganha a ressalva do envio: desligar o módulo desliga as integrações dele.
- `connector-secret-references`: o escopo do nome do segredo passa a ser `platform` ou `connector`, e o segmento de
  ambiente some. A credencial da plataforma é da conta; os outros segredos do conector seguem a regra de sempre.
- `avalara-tenant-authentication`: a credencial e o endpoint de token vêm da conta, sem derivação; a URL base vem do
  conector, **na versão que a integração declara**. O cenário "Troca de ambiente" sai.
- `connector-credential-test`: a requisição deixa de carregar o ambiente e passa a dizer o lado — entrada ou conta de
  plataforma; o freio chaveia por tenant e lado, sem ambiente.
- `avalara-document-contract`: a tabela `establishments` passa da seção do ambiente para o conector, também na regra do
  estabelecimento próprio.
- `platform-establishment-resolution`: o conector é o do próprio adapter, achado pelo nome; a chave da listagem guardada
  e a da recusa lembrada perdem o ambiente e ganham o conector.
- `avalara-establishment-listing`: a listagem usa a URL base da versão que ela declara, e a credencial e o endpoint de
  token da conta.
- `compliance-dispatch-outcome`: o envio e a consulta de status usam o caminho da integração, e o motivo do 404 aponta
  as duas partes da URL na tela de conectores, e não mais o `Avalara:DocumentsPath`.

## Non-goals

- **As filas por integração.** A fila própria por integração é o ADR-0037 §4 e entra na fatia seguinte. O despacho muda
  só na origem do caminho (§5).
- **A resolução entre conectores.** Um seletor de despachante que escolha, pela integração, qual conector atende. Com um
  conector por adapter, é operação nula; entra na fatia do segundo conector.
- **A barra lateral por conector, a carga como linha, o erro por motivo e a permissão do Admin.** São o ADR-0039.
- **O agendamento por integração.** O ADR-0037 agrupa o dropdown do agendamento por conector e módulo; hoje o
  agendamento não tem seletor de integração, e isso é outra fatia.
- **O AvaTax inteiro** — cálculo, emissão, estados novos, cancelamento, carta de correção (ADR-0038).
- **A recusa por registro numa carga** (ADR-0037 §6). Não há carga implementada; sem ela, não há o que especificar.
- **Os endpoints reais do Contábil e do Inventário.** A spec exige que o caminho seja **configurável**; ela não afirma
  qual é. O que não se sabe não vira requisito, vira campo.
- **O `poll.enabled` descer de nível.** Ele vive nas settings de entrada, que esta change não toca.
- **O wizard de cinco passos.** A configuração continua pela tela de conectores.
- **Provisionar as duas instâncias no Azure e o DNS.** Uma instância sozinha continua funcionando: ela é a do ambiente
  que a configuração do host diz.
- **Os defeitos D1, D2 e D3.** Cada um tem a sua change.

## Impact

- **Application:**
  - o `TenantConnectorProfile`, o `ConnectorProfileRequest` e o `ConnectorProfileView` perdem o `Environment` e os
    `Modules`, e trocam o par `OutboundAdapter`/`OutboundSettings` pela conta e pela lista de conectores com módulos e
    integrações;
  - o `ConnectorProfileService` ganha as regras de gravação: a credencial da plataforma fora do conector, o adapter
    único, o mapa de versões contra a declaração, o `https` na gravação, os módulos e as integrações declarados, a
    unicidade por integração e o lote;
  - o `ConnectorSettingsKind` troca `Outbound` por `Platform` e `Connector`, e o `SecretNames` perde o segmento de
    ambiente;
  - o ambiente vem de uma porta que a instância responde;
  - a cadeia do teste de credencial: o `ConnectorCredentialTestService` (lado sem ambiente, teste da conta), o
    `CredentialTestBrake` (chave sem ambiente), o `CredentialTestLog` e a porta `IConnectorCredentialTest` (assinatura
    sem ambiente);
  - o `PlatformEstablishmentResolver` acha o conector do adapter e chaveia por conector, sem ambiente;
  - o vocabulário das integrações e a porta da declaração do adapter;
  - a lista de módulos do `/info` passa a ser derivada.
- **Adapters:**
  - `Outbound.Avalara`: declara os módulos, as integrações, a versão de base de cada uma e quais aceitam lote; acha o
    próprio conector pelo nome; a credencial e o endpoint de token vêm da conta, e a URL base do conector, por versão; o
    envio, a consulta de status e o motivo do 404 usam o caminho da integração; saem o `Avalara:DocumentsPath` do envio
    e o `Avalara:TokenPath`; a chave do cache de token e a da listagem perdem o ambiente; a tabela `establishments` vem
    do conector; o `AvalaraCredentialTest` deixa de sobrescrever o ambiente;
  - `Ingress.D365Poll`: o `D365CredentialTest` acompanha a assinatura nova da porta do teste.
- **Infrastructure:** três migrações EF (D13) — o ambiente sai; a conta e o conector; módulos e integrações, com a saída
  da coluna `Modules`; o seed de desenvolvimento passa a semear a árvore inteira.
- **Host:** o ambiente entra por `Connector:Environment` e o host recusa subir sem ele; sai o `?? "Sandbox"` do `/info`;
  o host preenche, na subida, o caminho que a migração deixou à espera; o `PUT`/`GET /connector` mudam de forma; o
  endpoint e o log do teste de credencial deixam de receber o ambiente.
- **Tools:** a `tools/AvalaraSandboxProbe`, que compila na solução e usa o `AvalaraOutboundSettings`, o
  `Avalara:DocumentsPath` e o `Avalara:TokenPath`.
- **Dashboard:** sai o `?? 'Sandbox'` do selo; Configurações perde o seletor de ambiente, os dois blocos e a aba
  Módulos; o `splitOutbound` deixa de existir; o `CredentialTestButton` e o cliente da API deixam de mandar o ambiente;
  entram o bloco da conta, os campos do conector e as integrações (§7).
- **Testes:**
  - cerca de 25 arquivos atribuem `Environment` ao perfil, inclusive os do D365 e os de integração;
  - os que montam settings com `sandbox`/`production` mudam de forma;
  - o `DispatchToMockTests` deixa de esperar o `Avalara:DocumentsPath` no motivo;
  - entram o harness de migração contra SQL Server de verdade (D12) e os testes das regras novas.
- **Docs:** os ADRs 0036 a 0039 (já escritos), `docs/STATUS.md` e o `RUNNING.md` — inclusive a seção da sonda e a
  menção ao `Avalara:DocumentsPath`.
