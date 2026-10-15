A ordem é **a permissão primeiro**: ela é a parte que protege alguma coisa, não depende da barra lateral e tem o teste
como pré-requisito explícito (ADR-0039, Consequências). Depois vem a declaração do adapter, que a árvore do `/info`
precisa; depois o `/info`; e por último a barra lateral, que é a única parte puramente de tela.

- **Grupo 1:** a visão técnica é permissão do servidor — harness de teste por papel, teste antes, a lista num lugar só,
  o `/info` publicando a resposta e a tela seguindo.
- **Grupo 2:** o adapter declara o rótulo do conector e se ele agenda.
- **Grupo 3:** o `/info` responde a árvore de conectores, e a lista plana sai.
- **Grupo 4:** a guarda da chave do conector no host, e a barra lateral por conector, com o Agendamento dentro, o
  grupo que nomeia a ausência — e o que ela **não** afirma antes de o `/info` responder.
- **Grupo 5:** docs.
- **Grupo 6:** verificação final.

Cada grupo de código termina com `dotnet build` com 0 warnings (TreatWarningsAsErrors) e `dotnet test` verde. Os grupos
que tocam a tela terminam também com `npm test` e `npm run build`. Uma tarefa só recebe `[x]` **com evidência** — a
saída do teste, do build ou da execução; tarefa movida ou feita pela metade fica aberta, com a anotação do que faltou.

**Se alguma tarefa parecer exigir fila por integração, carga, erro agrupado por motivo, AvaTax, wizard de
provisionamento ou URL declarada pelo adapter: pare e diga.** São os não-objetivos do `proposal.md`, e nenhum deles
cabe aqui.

## 1. A visão técnica é permissão do servidor (`platform-response-trace`)

- [x] 1.1 Harness de teste de endpoint por papel (design D8): um `TestServer` com a mesma autenticação JWT do host, que
  emite token de um papel e de um tenant, sem SQL, sem Blob e sem Service Bus. Ele é o lugar onde o próximo endpoint
  com regra de papel se prova — não um arquivo avulso. Evidência: um teste de fumaça que pega 401 sem token e 200 num
  endpoint trivial do próprio harness.
  → `tests/FiscalHub.Host.Tests/` (projeto novo, no `FiscalHub.slnx`), com `RoleEndpointHarness` e
  `RoleEndpointHarnessSmokeTests` (401 sem token e 200 com token no `/harness/ping`). O harness sobe a autenticação do
  host pela extensão `AddFiscalHubJwtAuthentication` e emite o token com o `JwtTokenIssuer` do host
  (`InternalsVisibleTo`), em vez de uma cópia.
- [x] 1.2 **Teste primeiro, e é o teste que a fatia existe para escrever:** no `/trace` e no download do zip, um Viewer
  do tenant-a recebe **403** numa nota do tenant-a **que tem fotos**, e o armazenamento das fotos **não é lido** (o
  duplo conta as leituras, e a contagem é zero); um Admin do mesmo tenant recebe o conteúdo. É teste de **papel**, e
  não de tenant. Ele MUST falhar antes de 1.3 e passar depois — rodar e guardar as duas saídas.
  → `tests/FiscalHub.Host.Tests/RawTraceRoleTests.cs`. **Vermelho antes de 1.3:** `error CS0246: "RawTraceRoles" não
  pode ser encontrado` — o ponto único não existia. **Verde depois:** 14/14. E, porque a regra já existia no
  `Program.cs`, o teste foi provado por inversão: apagado o `RequireRole` da extensão, o Viewer passou a receber 200 e
  o armazenamento foi lido (5 falhas, entre elas as duas do Viewer); restaurado e conferido por hash
  (`c0aa16ce7505cc52eb3684d702026c24dd418f70b399f4723ea729b804382466` em `RawTraceEndpoints.cs`), voltou a 14/14.
- [x] 1.3 O mapeamento dos dois endpoints e a lista de papéis saem do corpo do `Program.cs` para um ponto único no Host
  (extensão + lista ao lado), que o `Program.cs` chama. Sem mudança de comportamento: as mesmas rotas, o mesmo
  `RequireRole`, os mesmos 404 e a mesma mensagem.
  → `src/FiscalHub.Host/RawTraceEndpoints.cs`: `RawTraceRoles` (a lista, com `Default = ["Admin"]` e `Allows`) e
  `MapRawTraceEndpoints`. O `NoTraceMessage` virou constante pública dali, e o `/reading` do `Program.cs` a usa.
- [x] 1.4 Teste primeiro: o `/info` responde, para o usuário logado, se ele pode ver as fotos cruas — verdadeiro para o
  Admin, falso para o Viewer —, derivado da **mesma** lista de 1.3. Teste com a lista de dois papéis provando que um
  papel acrescentado no servidor chega à resposta sem mais nada.
  → `tests/FiscalHub.Host.Tests/InfoRawJsonTests.cs`, com `RawTraceRoles(["Admin", "Suporte"])` no harness: o Suporte
  recebe `canViewRawJson: true` **e** 200 no `/trace` (`RawTraceRoleTests.A_role_added_to_the_server_list_...`), sem
  nenhuma outra mudança. Vermelho primeiro pelo mesmo `CS0246`; e provado por inversão (`!rawTrace.Allows(user)` →
  3 falhas), restaurado e conferido por hash
  (`e3f5c8c53115fee330be8a1b68efebc53a0947af1665091d53ca2388ba95d803` em `InstanceInfoEndpoint.cs`).
- [x] 1.5 O `/info` passa a responder o campo.
  → `InstanceInfo` ganha `CanViewRawJson`, e o `InstanceInfo.For` o recebe já respondido — quem conhece a lista é o
  Host, e a Application não decide permissão. O mapeamento do `/info` saiu do `Program.cs` para
  `src/FiscalHub.Host/InstanceInfoEndpoint.cs`, pelo mesmo motivo de D8: o teste tem que subir o registro de verdade, e
  não uma cópia dele. `InstanceInfoTests` passou a exigir os cinco campos, na ordem.
- [x] 1.6 Teste da tela: o "Visualizar JSON" (`DocumentDetail`) e o "Baixar arquivos" (`NoteDialog`) aparecem conforme
  o `/info`, e **não aparecem** antes de ele responder nem se ele falhar, para papel nenhum.
  → `dashboard/src/features/rawJson.test.ts` (4 casos), vermelho primeiro (`Cannot find module './rawJson'`).
  **Desvio anotado:** o dashboard não tem stack de teste de componente (sem `@testing-library/react`, sem jsdom; toda a
  suíte é de módulo puro). O teste é do módulo que **decide** — `canViewRawJson(info)` —, no mesmo molde do
  `environment.ts`/`environment.test.ts`, que o próprio design cita como o análogo. Que os dois botões usam esse módulo
  está garantido pelo `tsc` e pela ausência de qualquer outra fonte de decisão no front (1.7); a renderização em si
  continua coberta pela prova manual de 6.4. Trazer `@testing-library/react` seria dependência nova, fora do escopo.
- [x] 1.7 O dashboard passa a ler o `/info`, e o `dashboard/src/features/auth/roles.ts` **deixa de existir** — nenhuma
  lista de papéis sobra no front. Conferir que nada mais o importa.
  → `roles.ts` apagado. `DocumentDetail` e `NoteDialog` passaram a `useInfo()` + `canViewRawJson(info)` e deixaram de
  importar o `useAuth`. `grep` por `auth/roles` e `RAW_JSON_ROLES` em `dashboard/src`: nenhum importador — só sobrava um
  comentário no `RawJsonModal.tsx`, corrigido.
- [x] 1.8 `dotnet build` (0 warnings), `dotnet test`, `npm test` e `npm run build`. Evidência: a saída dos quatro.
  → `dotnet build`: "Compilação com êxito. 0 Aviso(s), 0 Erro(s)". `dotnet test`: **1356 aprovados, 0 com falha**,
  42 ignorados (os de sempre: 40 de SQL na Infrastructure e 2 do D365 ao vivo). `npm test`: 114 aprovados, 11 arquivos.
  `npm run build`: `tsc --noEmit` limpo e `✓ built in 36.59s`.
  **Nota de ambiente:** o `MockComplianceApi` está rodando e trava o `.exe` dele, o que faz o `dotnet build` da raiz
  falhar no `copy`. Os comandos foram rodados com `--artifacts-path obj/guard` (dentro do repo, e `obj/` é ignorado).

## 2. O adapter declara o rótulo do conector e se ele agenda (`platform-account-connectors`)

- [x] 2.1 Teste primeiro: a declaração da Avalara traz o rótulo `Tax Compliance` e diz que o conector agenda; o rótulo
  não entra em chave, nome de segredo nem comparação — a identidade continua sendo o adapter, comparado exato.
  → `AvalaraDeclarationTests`: três testes novos. O do nome de segredo prova os dois lados — o nome derivado é
  `fh-tenant-a--connector--avalara--apikey` (sai do **adapter**), e o rótulo nem serviria de segmento: o `SecretNames`
  o recusa com `ArgumentException`, porque tem espaço e maiúsculas. Vermelho primeiro: `error CS1061: 'AvalaraDeclaration'
  não contém uma definição para "Label"`.
- [x] 2.2 `IAdapterDeclaration` ganha `Label` e `Scheduling`; a `AvalaraDeclaration` os declara. Nada é gravado no
  perfil, e a gravação continua ignorando o que não é dela.
  → `IAdapterDeclaration.Label` e `.Scheduling`; `AvalaraDeclaration` declara `Tax Compliance` e `true`. O
  `PlatformConnector` **não** ganhou campo nenhum, e o `PlatformConnectorRequest` tampouco: não há onde gravar.
- [x] 2.3 Teste de que uma gravação que traga rótulo ou agendamento dentro de um conector tem o campo **ignorado**, e a
  leitura continua devolvendo o declarado.
  → `ConnectorProfileServiceTests.A_body_with_the_label_or_the_scheduling_inside_a_connector_has_the_fields_ignored`:
  um corpo JSON com `"label":"O Meu Nome","scheduling":false` dentro do conector é aceito (`Saved`), nada de `label`,
  `scheduling` nem `O Meu Nome` aparece no perfil gravado, e a declaração continua dizendo `Tax Compliance` e que
  agenda. Vermelho primeiro pelo mesmo `CS1061`; e provado por inversão — acrescentado um `Label` ao `PlatformConnector`
  e trocado o rótulo declarado para `Avalara`, caíram 1 + 3 testes; restaurado e conferido por hash
  (`4b578f25…aceca6` em `PlatformAccount.cs`, `cef5546f…b2d260` em `AvalaraDeclaration.cs`).
  **Nota de escopo:** "a leitura" aqui é a da declaração, pelo `IAdapterDeclaration`. O rótulo e o agendamento só
  chegam à resposta do `/info` no grupo 3, que é quem troca a lista plana pela árvore.
- [x] 2.4 `dotnet build` (0 warnings) e `dotnet test`. Evidência: a saída dos dois.
  → `dotnet build`: "Compilação com êxito. 0 Aviso(s), 0 Erro(s)". `dotnet test`: **1360 aprovados, 0 com falha**,
  42 ignorados (os mesmos de sempre). Rodados com `--artifacts-path obj/guard` — ver a nota de ambiente em 1.8.

## 3. O `/info` responde a árvore de conectores (`module-navigation`)

- [x] 3.1 Teste primeiro do contrato do `/info`: uma entrada por conector do tenant, na ordem gravada no perfil, com o
  adapter, o rótulo, o agendamento e os módulos ligados naquele conector na ordem Fiscal, Contábil, Inventário, sem
  repetir; dois conectores dão duas entradas, e o Fiscal ligado nos dois aparece em cada um; um tenant sem conector dá
  a lista **vazia**; e a resposta **não traz** lista de módulos do nível do tenant.
  → `tests/FiscalHub.Application.Tests/InstanceInfoTests.cs`, reescrito: 15 casos (eram 7). A ordem gravada é provada
  nos dois eixos — conectores dentro de uma conta e contas dentro do perfil. O caso do JSON confere os cinco campos da
  resposta **na ordem** (`environment`, `automaticIntegration`, `inboundScans`, `connectors`, `canViewRawJson`) e os
  quatro de cada conector (`adapter`, `label`, `scheduling`, `modules`); a lista plana não está entre eles.
  **Vermelho antes de 3.3:** `error CS0246: "ConnectorInfo" não pode ser encontrado`, `error CS1061: 'InstanceInfo' não
  contém uma definição para "Connectors"` e `error CS1501: nenhuma sobrecarga para o método "For" leva 5 argumentos`.
- [x] 3.2 Teste primeiro: o `/info` responde a árvore para **qualquer papel** do tenant, e só para o tenant de quem
  está logado.
  → `tests/FiscalHub.Host.Tests/InfoConnectorsTests.cs` (5 casos), no endpoint de verdade: Admin e Viewer do tenant-a
  recebem a mesma árvore; o tenant-b recebe a dele (`AvaTax`/`Contabil`) e não a do tenant-a; o tenant sem perfil recebe
  a árvore vazia; e a resposta não tem a propriedade `modules`. O `RoleEndpointHarness` ganhou dois parâmetros —
  `profiles` e `declarations` — e o `EmptyConnectorProfileStore` virou `StubConnectorProfileStore`, com um perfil por
  tenant: é por ele que o limite de tenant da árvore se prova. **Vermelho antes de 3.3:** 5 com falha, 14 aprovados —
  `System.Collections.Generic.KeyNotFoundException` em `JsonElement.GetProperty("connectors")`.
- [x] 3.3 O `InstanceInfo` passa a montar a árvore e perde a lista plana; o `TenantModules` segue dizendo a ordem e os
  valores aceitos dos módulos. Conferir que nenhum outro leitor do servidor dependia da lista plana.
  → `InstanceInfo` troca `Modules` por `Connectors`, uma lista de `ConnectorInfo(Adapter, Label, Scheduling, Modules)`,
  e o `For` passa a receber as declarações: o rótulo e o agendamento saem delas, achadas pelo par (plataforma, adapter)
  comparado exato, e nada disso é gravado no perfil. O `TenantModules` ganhou o `Ordered`, que é a ordem da barra
  lateral sem repetir e só com os aceitos; o `Of(profile)` passou a usá-lo e continua servindo o `GET /connector`, que
  é outra resposta e não muda. O `/info` do `InstanceInfoEndpoint` injeta `IEnumerable<IAdapterDeclaration>`.
  **Nenhum outro leitor do servidor:** o `InstanceInfo.For` tinha dois chamadores (o endpoint e o teste), e o campo
  `Modules` sumiu do tipo — qualquer leitor sobrando quebraria o build. `grep` por `"modules"` em `src`, `tests` e
  `tools`: só o corpo do `PUT /connector` (a lista do tenant, que já é recusada com 400) e os módulos **dentro** do
  conector. Provado também por inversão: ordenado por rótulo em vez da ordem gravada, rótulo e agendamento tirados do
  conector em vez da declaração e módulos sem o `Ordered` → 6 falhas no `InstanceInfoTests` e 3 no `InfoConnectorsTests`
  (as duas do papel e a do tenant); restaurado e conferido por hash
  (`467089b23704750ebfa8b24a31dcde902f4fb517051fb5f4eec274d3c3f983fb` em `InstanceInfo.cs` e
  `b3bf4a87275afb74a68a25c7ade9f40455ccd293e035552a8bf4912847d5bf88` em `TenantModules.cs`).
  **Decisão anotada:** um conector cujo adapter ninguém declara continua na resposta, com o adapter no lugar do rótulo e
  sem agendamento. A gravação recusa adapter não declarado, então a sobra só vem de um adapter que saiu do deploy ou de
  uma linha semeada por SQL; some-lo esconderia configuração que existe, e é a mesma tolerância que a leitura do perfil
  (`ConnectorProfileService.IntegrationsOf`) já tem. Os artefatos não decidem esse caso — fica registrado.
- [x] 3.4 `dotnet build` (0 warnings) e `dotnet test`. Evidência: a saída dos dois.
  → `dotnet build`: "Compilação com êxito. 0 Aviso(s), 0 Erro(s)". `dotnet test`: **1373 aprovados, 0 com falha**,
  42 ignorados (os de sempre: 40 de SQL na Infrastructure e 2 do D365 ao vivo). Os 13 a mais que o grupo 2 são os 8 do
  `InstanceInfoTests` e os 5 do `InfoConnectorsTests`. Rodados com `--artifacts-path obj/guard` — ver a nota de ambiente
  em 1.8.
  **Estado intermediário anotado:** o dashboard ainda não foi tocado (é o grupo 4). Ele lê `info?.modules ??
  DEFAULT_MODULES`; sem o campo, cai no padrão e a barra lateral mostra Fiscal e Agendamento para qualquer tenant, até o
  grupo 4 trocá-la pela árvore. Não quebra: `npm test` continua **114 aprovados, 11 arquivos**.

## 4. A barra lateral por conector (`module-navigation`)

- [x] 4.0 **A guarda da unicidade do adapter entre as declarações, no host.** Não é requisito novo: é a trava de uma
  invariante que os artefatos já afirmam e que ninguém garante. Onde ela está afirmada:
  `IAdapterDeclaration.Adapter` — "O nome do adapter: **a chave do conector**"; `IAdapterDeclaration.Label` — "A
  identidade do conector continua sendo o `Adapter`, **comparado exato**"; o design D3 — "O que existe é a chave do
  conector, que é o **nome do adapter**"; e o D10 — "A identidade do item é o par (conector, módulo)".
  **O que não garante:** a unicidade do adapter só é conferida **dentro de uma conta** — o `ConnectorProfileService`
  recusa "o adapter '…' aparece N vezes, e a conta tem um conector por adapter" por conta, e não por tenant nem entre
  declarações. Duas declarações de **plataformas diferentes** com o mesmo nome de adapter são aceitas hoje sem
  reclamação. Acontecendo, o `/info` devolve duas entradas com o mesmo `adapter`, e a barra lateral do grupo 4 fica com
  duas chaves iguais: dois grupos indistinguíveis, e o item (conector, módulo) de um abrindo a tela do outro —
  silencioso e errado, que é o descuido que o ADR-0039 avisa.
  **Por que agora:** não é alcançável hoje (uma plataforma, um adapter), e o caminho de crescimento escrito na própria
  interface mantém os nomes distintos ("com o AvaTax, a plataforma é Avalara e os adapters são dois"). É barato travar
  enquanto é verdade.
  **Teste primeiro:** duas declarações registradas com o mesmo `Adapter` e plataformas diferentes **derrubam a subida
  do host**, com mensagem que nomeia o adapter repetido e as duas plataformas. Comparação exata, como o resto. O lugar
  é onde as declarações são reunidas no host.
  → `src/FiscalHub.Host/AdapterDeclarationGuard.cs`: `EnsureDistinctAdapterDeclarations()`, uma extensão do
  `IServiceProvider` que o `Program.cs` chama logo depois do `builder.Build()`, antes do middleware e dos seeds — o
  contêiner é onde as declarações se reúnem. Repetido, `InvalidOperationException` com o adapter e as plataformas que o
  declaram, na ordem do registro. `tests/FiscalHub.Host.Tests/AdapterDeclarationGuardTests.cs` (4 casos): o repetido
  derruba; **dois adapters da mesma plataforma passam** (o caminho do AvaTax que a interface escreve); `avalara` não é
  `Avalara` (comparado exato); uma declaração e nenhuma passam. **Vermelho primeiro:** `error CS1061: 'ServiceProvider'
  não contém uma definição para "EnsureDistinctAdapterDeclarations"`. **Provado por inversão:** trocado o
  `g.Count() > 1` por `> 2` (aceitar o repetido), 1 com falha de 23 —
  `The_same_adapter_declared_by_two_platforms_brings_the_host_down`; restaurado e conferido por hash
  (`eb7d2510c176a791fbfe53fad64754192c0bbe3dd90bb3b01e8fab6a44b764b1`), 23/23.
  **Limite anotado:** o teste cobre a guarda, e não a chamada dela no `Program.cs` — ao contrário do D8, em que parar de
  chamar a extensão some com as rotas, aqui parar de chamar não quebra nada. É o mesmo limite que o D8 já assume para a
  composição do host.
- [x] 4.1 Teste primeiro do modelo de navegação (design D10): a árvore de grupos e itens montada a partir do `/info`,
  em que **cada item carrega a identidade (conector, módulo), o rótulo, o título, o subtítulo e a tela que abre**. Os
  casos: um conector e um módulo (o grupo aparece mesmo assim); os módulos na ordem; dois conectores, dois grupos, com
  o Fiscal em cada um; o conector que não agenda, sem o item de Agendamento; o conector sem módulo e sem agendamento,
  que não vira grupo; e **a varredura da árvore inteira conferindo item a item a tela que cada um abre** — é esse teste
  que trava o descuido que o ADR-0039 avisa.
  → `dashboard/src/features/modules/modules.test.ts`, reescrito: 14 casos (eram 4), os seis do 4.1 entre eles. Os dois
  Fiscais de dois conectores são provados **itens diferentes** (chaves distintas, `connector` diferente). A varredura
  compara com uma tabela escrita à mão — comparar o modelo com ele mesmo não provaria nada — e confere que ela cobriu
  os quatro rótulos. **Vermelho antes de 4.4:** 14 com falha, `TypeError: navigationGroups is not a function`.
  **Provado por inversão:** trocada a `view` do Inventário para `accounting` no `MODULE_VIEWS` (o item apontando para a
  tela que não é a dele), **só a varredura falhou** — 1 com falha de 124; restaurado e conferido por hash
  (`3208277ee42f3019fe8245312346d4ee2060ac19e3478eda3b4b5c3db7881647` em `modules.ts`), 124/124.
- [x] 4.2 Teste primeiro do grupo que nomeia a ausência: tenant sem conector, e tenant cujo único conector não produz
  item, mostram um grupo com Fiscal e Agendamento, com o Fiscal como tela de entrada; o grupo some quando um conector
  passa a produzir item.
  → Os quatro casos do bloco `4.2 o grupo que nomeia a ausência`, no mesmo arquivo. O rótulo é o `NO_CONNECTOR_LABEL`
  (`Sem conector configurado`), e um caso confere que ele **nomeia a ausência** em vez de inventar um conector.
- [x] 4.3 Teste primeiro da entrada e do item que some: a tela aberta é o primeiro item do primeiro grupo; o item
  selecionado que deixa de existir cai na tela de entrada.
  → Os quatro casos do bloco `4.3 a tela de entrada e o item que some`: a entrada é o primeiro item do primeiro grupo
  (inclusive com o Fiscal depois do Contábil no perfil, porque a ordem é a da tabela); sem o Fiscal, o primeiro que o
  conector tem; o módulo desligado enquanto estava aberto cai na entrada; e o **conector que saiu** leva o item junto.
- [x] 4.4 O `features/modules/modules.ts` vira o modelo de navegação. **Cuidado conferido:** o
  `features/connectors/connectorPanelRows.ts` importa o `MODULE_VIEWS` para montar as linhas de módulo do painel do
  conector — os rótulos dos módulos continuam numa tabela compartilhada, e Configurações não pode quebrar.
  → `modules.ts` passa a expor `NavGroup`/`NavItem`, `navigationGroups`, `navigationItems`, `entryItem` e `shownItem`;
  `visibleModuleViews`, `entryView`, `isModuleView` e `DEFAULT_MODULES` saem (a lista plana que os alimentava não
  existe mais). O `MODULE_VIEWS` **fica**, agora com `title` e `subtitle` ao lado do `label`: é a tabela única que a
  navegação e o painel do conector leem. O `connectorPanelRows.ts` não foi tocado — ele usa `.module` e `.label`, que
  continuam —, e os 18 testes dele seguem passando. No `types.ts`, o `InfoResponse.modules` virou
  `connectors: ConnectorInfo[]`; o `ConnectorProfile.modules` (do `GET /connector`) continua, que é outra resposta.
- [x] 4.5 O `App.tsx` desenha grupos e itens a partir do modelo, e abre a tela que o item declara — sem a cadeia de
  ternários por nome de tela e sem a tabela de títulos separada. O bloco "Administração" continua abaixo, só para
  Admin, e os selos do rodapé ficam como estão.
  → A tabela `titles` **sumiu**: o título e o subtítulo vêm do item. A cadeia de ternários do conteúdo virou
  `admin ? admin.screen : MODULE_SCREENS[item.view]`, com `MODULE_SCREENS: Record<ModuleView, ReactNode>` — o
  `Record` é exaustivo pelo `tsc`, então uma `view` nova sem tela não compila. O bloco Administração virou a tabela
  `ADMIN_VIEWS` (rótulo, título, subtítulo, ícone e tela), desenhada abaixo dos grupos e só para Admin (D12). O estado
  passou a ser uma `Selection` — item do eixo (pela chave do par conector/módulo) ou tela de Administração —, em vez
  do nome da tela. Os selos do rodapé e a topbar não foram tocados.
- [x] 4.6 Conferir a barra lateral em **todas** as telas, inclusive as que não mudam de conteúdo (Configurações,
  Usuários, Agendamento, módulo reservado): é mecânico e é onde o descuido passa.
  → A barra lateral é um componente só, no `App.tsx`, desenhado fora do `switch` de conteúdo: ela é a mesma nas seis
  telas. Conferido uma a uma: **Fiscal** (`GroupsPage`), **Contábil** e **Inventário** (`ReservedModulePage`, com o
  rótulo certo em cada), **Agendamento** (`IntegrationsPage`), **Configurações** (`ConnectorsPage`) e **Usuários**
  (`UsersPage`). Nas duas de Administração, `active={!admin && item.key === navItem.key}` garante que **nenhum item de
  conector fica aceso** enquanto Configurações ou Usuários está selecionado — era o desalinhamento mais provável, já
  que o `item` continua sendo calculado nelas. Para o Viewer, o bloco Administração não é desenhado e uma `Selection`
  de Administração cai no eixo (o `isAdmin &&` do `admin`), sem tela branca. O espaçamento do cabeçalho de grupo é o
  do bloco de antes: sem `paddingTop` no primeiro grupo, 14 nos seguintes e no de Administração. E o
  `ConnectorsPage` já invalida o `['info']` ao salvar, então ligar um módulo redesenha a barra lateral **sem recarregar
  a página**, como o cenário pede.
- [x] 4.7 `npm test`, `npm run build`, `dotnet build` (0 warnings) e `dotnet test`. Evidência: a saída dos quatro.
  → `npm test`: **124 aprovados**, 11 arquivos (eram 114; os 10 a mais são os do modelo de navegação).
  `npm run build`: `tsc --noEmit` limpo e `✓ built in 25.61s`. `dotnet build`: "Compilação com êxito. 0 Aviso(s),
  0 Erro(s)". `dotnet test`: **1377 aprovados, 0 com falha**, 42 ignorados (os de sempre: 40 de SQL na Infrastructure e
  2 do D365 ao vivo); os 4 a mais que o grupo 3 são os do `AdapterDeclarationGuardTests`. Rodados com
  `--artifacts-path obj/guard` — ver a nota de ambiente em 1.8.
  **Decisão anotada (como a de 3.3):** enquanto o `/info` não responde, ou se ele falhar, o `navigationGroups` recebe
  `undefined` e desenha o grupo que nomeia a ausência — Fiscal e Agendamento, os mesmos itens que o
  `DEFAULT_MODULES` mostrava antes desta change. Os artefatos não decidem o estado "ainda não se sabe": a spec descreve
  "nenhum conector produz item", e o design só exige que a tela não afirme nada antes da resposta no caso dos **botões
  do JSON** (D7) e do **selo de ambiente**. Tratar o `undefined` como lista vazia preserva o que o usuário via e não
  inventa um terceiro rótulo de produto; o custo é que o cabeçalho diz "Sem conector configurado" por um instante — e
  o diz enquanto durar uma falha do `/info`. Um rótulo próprio para o carregando seria texto de produto novo, e por
  isso fica registrado em vez de escolhido.
  **→ Decidido pelo Marcelo em 2026-10-15, e corrigido na 4.8:** a metade do "ainda não respondeu" não precisa de texto
  novo — reusa o `Carregando…` que já existe — e a metade da **falha** foi para os Próximos passos do `docs/STATUS.md`.

- [x] 4.8 **A barra lateral não afirma ausência antes de saber.** É a regressão que esta change criou e que o 4.7
  registrou como decisão em aberto: antes dela, enquanto o `/info` não chegava, a barra mostrava Fiscal e Agendamento
  pelo `DEFAULT_MODULES`, sem cabeçalho e sem afirmar nada; depois dela, passou a desenhar o grupo "Sem conector
  configurado" nesse intervalo — uma afirmação sobre a configuração do cliente feita **antes da resposta**, que é
  exatamente o que o D7 evita nos botões do JSON e o selo de ambiente evita no rodapé (decisão do Marcelo, 2026-10-15).
  **Teste primeiro:** enquanto o `/info` não respondeu, a barra **não** mostra o grupo que nomeia a ausência e não
  afirma ausência nenhuma; depois da resposta, o comportamento já especificado vale sem mudança, **inclusive a árvore
  vazia**, que continua mostrando o grupo da ausência.
  → `dashboard/src/features/modules/modules.test.ts`, bloco `4.8 enquanto o /info não respondeu` (4 casos).
  **Vermelho primeiro:** 3 com falha de 128 — `expected [ Array(1) ] to deeply equal []`, com o `["Sem conector
  configurado", "Fiscal", "Agendamento"]` recebido. **Verde depois:** 128/128.
  **A correção, em dois lugares.** No `modules.ts`, **o nulo não é o vazio**: `navigationGroups(undefined)` é "o
  `/info` ainda não respondeu" e devolve a árvore **sem grupo nenhum**; a árvore **vazia** é uma resposta, e continua
  virando o grupo que nomeia a ausência. No `App.tsx`, o `useInfo()` passa a devolver também o `isLoading` — como
  `ConnectorsPage`, `GroupsPage` e `RawJsonModal` já fazem —, e sem árvore a tela mostra o **mesmo `Carregando…` do
  porteiro de sessão** (extraído para o componente `Loading`, reusado pelos dois): nenhum texto de produto novo foi
  inventado. O `useEffect` do menu subiu para antes da saída antecipada, porque hook não pode ficar depois dela.
  **Provado por inversão:** desfeita a guarda do nulo (`(connectors ?? [])` de volta), **3 com falha de 128**, e as
  três são as do 4.8 — o quarto caso, o de "depois da resposta", continuou verde, que é o esperado: a inversão só
  atinge o estado que a tarefa trata. Restaurado e conferido por hash
  (`e4a44ffb8f6789e757f403c51323311a9ff96aba907af0a18a3b71f7be668c13` em `modules.ts`), 128/128.
  **O caso de FALHA do `/info` não foi tocado**, de propósito: a aplicação não tem tratamento de erro para o `/info` em
  lugar nenhum, isso é anterior a esta change e maior que a barra lateral. A falha continua chegando ao modelo como
  resposta vazia e continua nomeando a ausência — está escrito na linha do `App.tsx` e virou o **item 2 dos Próximos
  passos do `docs/STATUS.md`** (5.2).
  **Limite anotado, o mesmo de 1.6 e 4.0:** o dashboard não tem stack de teste de componente, então o teste é do módulo
  que decide, e a linha do `App.tsx` que passa o `isLoading` não tem teste — o `tsc` garante a forma, não a escolha.
  **Spec:** o delta do `module-navigation` ganhou o bullet "Antes da resposta do `/info`" e o cenário "Antes de o /info
  responder", no requisito "O tenant sem conector vê um grupo que nomeia a ausência", cujo primeiro parágrafo e cujo
  bullet "MUST NOT ficar sem grupo nenhum" passaram a dizer **"havendo resposta"**. Sem isso o delta afirmaria o
  contrário do código. **Isto não estava na lista de 4.8** — foi julgamento meu, e está aqui para ser desfeito se o
  Marcelo preferir.
- [x] 4.9 `npm test` e `npm run build` da 4.8 (os `dotnet` não são tocados por ela; rodados no grupo 6 de qualquer
  forma). Evidência: `npm test` **128 aprovados**, 11 arquivos (eram 124; os 4 a mais são os do 4.8);
  `npm run build`: `tsc --noEmit` limpo e `✓ built in 18.01s`.

- [x] 4.10 **Trocar de usuário não pode manter o cache do usuário anterior.** *É a tarefa que o registro da prova
  manual, em 6.4, chama de "a tarefa 4.9": aquele número já era o da guarda de build da 4.8, e por isso ela entrou
  como 4.10 — renumerar uma tarefa já fechada com evidência seria pior. Se o Marcelo preferir, é trocar os dois
  números.*
  **O achado (prova manual de 6.4, 15/10/2026):** logado como Admin do `tenant-a`, deslogado e logado como Viewer do
  **mesmo** tenant, **sem recarregar a página**: a barra lateral veio certa (sem o bloco Administração), mas os dois
  botões da visão técnica — "Visualizar JSON" e "Baixar arquivos" — continuaram aparecendo para o Viewer. O servidor
  estava certo: depois de recarregar, o mesmo `/info` respondeu `canViewRawJson: false` para aquele usuário —
  confirmado lendo o corpo da resposta na rede (antes do reload, `true`; depois, `false`).
  **A causa, em três lugares:** o `useInfo.ts` guarda com `queryKey: ['info']` e `staleTime: Infinity` — a chave não
  tem nada do usuário, e o dado nunca envelhece; o `logout` do `AuthContext.tsx` faz `tokenStore.set(null)` e
  `setUser(null)`, e mais nada (o mesmo vale para a queda por 401, que passa pelo `setUnauthorizedHandler`); e o
  `QueryClient` do `main.tsx` é criado uma vez no módulo e vive enquanto a página viver, sem ninguém chamar `clear()`.
  A resposta do usuário anterior continua valendo para o próximo.
  **O que é e o que não é:** não é vazamento de dado — o servidor continua barrando (o `/trace` responde 403 para
  Viewer, e os dados são escopados por tenant). É **afirmação de tela errada**. O cache velho é anterior a esta change
  (o selo de ambiente já sofria disso); o que mudou é que agora há uma **permissão** e a **árvore de navegação** andando
  em cima dele, e as duas são desta fatia — por isso o conserto é aqui.
  **Teste primeiro** → `dashboard/src/features/auth/sessionCache.test.ts` (3 casos): depois da troca de sessão, o
  `/info` guardado some e `canViewRawJson` volta a `false`; a árvore guardada some e o `navigationGroups` volta ao
  estado "ainda não respondeu" da 4.8; e **não sobra consulta nenhuma**, nem de chave composta — enumerar as chaves
  deixaria a próxima consulta nova vazando por esquecimento. O cliente do teste usa o mesmo `staleTime: Infinity` do
  `useInfo`, para provar que a limpeza não depende de o dado estar velho. **Vermelho primeiro:** `Error: Cannot find
  module './sessionCache'`.
  **Nível do teste, e o que ele NÃO cobre (o mesmo limite de 1.6, 4.0 e 4.8):** o dashboard não tem stack de teste de
  componente (sem `@testing-library/react`, sem jsdom). O teste é **da função que limpa**, com um `QueryClient` de
  verdade — e não do React: ele prova que `resetSessionCache` esvazia o cache e que, esvaziado, a tela volta a não
  afirmar nada. **Não** prova que o `AuthContext` a chama no `login` e no `logout` (o `tsc` garante a forma, não a
  escolha), nem a renderização dos botões, nem o fluxo real de sair e entrar no navegador. Essas duas linhas do
  `AuthContext.tsx` e a troca de usuário na tela continuam dependendo da prova manual de 6.4.
  **O conserto** → `dashboard/src/features/auth/sessionCache.ts`: `resetSessionCache(queryClient)`, que chama
  `queryClient.clear()`. O `AuthContext` passou a pegar o `useQueryClient()` (ele já está dentro do
  `QueryClientProvider`, pelo `main.tsx`) e a chamar a limpeza nos **dois** lados: no `login`, depois de guardar o
  token e **antes** do `setUser` — é o que cobre a queda por 401, que leva à tela de login sem passar pelo `logout` —, e
  no `logout`, **depois** do `setUser(null)`, porque limpar antes faria as consultas ainda montadas refazerem a busca
  sem token.
  **Não foi tocado, de propósito (item 4 da instrução):** o `staleTime: Infinity` do `useInfo` e as invalidações que já
  existem. O salvamento em Configurações continua invalidando o `['info']` (`ConnectorsPage.tsx:194`), e o cenário "o
  grupo some quando o conector é configurado" depende disso. São coisas diferentes: a invalidação é de dado que mudou
  **dentro** da mesma sessão; a limpeza é de **sessão que mudou**.
  **Provado por inversão:** o corpo do `resetSessionCache` trocado por um no-op (`void queryClient`), **3 com falha de
  131**, e as três são as do arquivo novo (`expected { environment: 'Sandbox', …(4) } to be undefined`). Restaurado e
  conferido por hash (`8e7aad5be01d8394b0695da7090cdee401e932fb9dae0281e14bbe97dea23311` em `sessionCache.ts` e
  `c3d4f078dda43571146fa060e740645ed3432d51a48bc2d390e13abe28a64ed0` em `AuthContext.tsx`), 131/131.
  **Spec:** o delta do `platform-response-trace` já afirmava "a tela MUST usar essa resposta" para os botões — e isso só
  é verdade com o cache limpo, porque a resposta guardada é do token anterior, e não do usuário. Ganhou o bullet "A
  resposta é do usuário logado, e só dele" e o cenário "Troca de usuário sem recarregar a página". **O
  `module-navigation` não precisou de texto novo:** com o cache limpo, a troca de sessão devolve a barra lateral ao
  estado "ainda não respondeu", que a 4.8 já especificou ("Antes da resposta do `/info`"); escrever a mesma regra nas
  duas specs criaria duas fontes para ela.
  **Guarda:** `npm test` **131 aprovados**, 12 arquivos (eram 128 em 11; os 3 a mais e o arquivo novo são os desta
  tarefa). `npm run build`: `tsc --noEmit` limpo e `✓ built in 30.55s`. `dotnet build`: "Compilação com êxito.
  **0 Aviso(s), 0 Erro(s)**". `dotnet test`: **1377 aprovados, 0 com falha**, 42 ignorados (os de sempre) — o mesmo
  número de 6.1, como esperado: a tarefa é só do dashboard. Rodados com `--artifacts-path obj/guard` (ver a nota de
  ambiente em 1.8).

## 5. Docs

- [x] 5.1 ADR-0039: preencher o campo "Change OpenSpec", hoje "a propor", com o nome desta change.
  → `docs/adr/0039-dashboard-por-conector-e-o-tecnico-do-admin.md:13`. O campo nomeia a change, as três capacidades
  (`module-navigation`, `platform-account-connectors`, `platform-response-trace`) e **quais seções ela cobre**: os §1,
  §2 e §5. Os §3 e §4 (a linha como o que a ação opera e o erro da carga lido por motivo) continuam **a propor**, porque
  só aparecem com o Contábil e o Inventário — escrever "a change do ADR-0039" sem isso faria o ADR parecer implementado
  por inteiro.
- [x] 5.2 `docs/STATUS.md`: o item 2 dos Próximos passos sai (ou vira o registro do que foi feito), e as contagens de
  specs e changes acompanham.
  → **O item 2 virou outro item 2**, e não sumiu: o que ele pedia está feito e foi para "Onde estamos" (um parágrafo
  novo, com a barra lateral por conector, o Agendamento dentro do conector, a árvore do `/info`, o 403 do servidor, a
  lista de papéis num lugar só e a guarda do adapter repetido). O lugar dele nos Próximos passos foi ocupado pelo item
  que a 4.8 manda escrever: **o `/info` não tem tratamento de falha em tela nenhuma**, e enquanto não tiver, uma falha
  dele faz a barra lateral afirmar que não há conector configurado. Manter o número 2 evitou renumerar os itens 3 a 10,
  que outros documentos citam; a frase do fim da seção que apontava "a fatia cruzada do item 2" passou a nomear a
  change.
  → **As contagens:** `24 projetos (22 fora de tools/)` → **`25 (23 fora de tools/)`**, pelo projeto novo
  `tests/FiscalHub.Host.Tests` (1.1). As **specs continuam 28** e as **changes arquivadas continuam 15**: a change toca
  três specs existentes por delta, não cria capacidade nova e **não foi arquivada**. Isso ficou escrito ali, para que a
  conferência seguinte não leia o número parado como esquecimento. "Última atualização" → 2026-10-15.
- [x] 5.3 Conferir se algum trecho do `docs/RUNNING.md` descreve a barra lateral por módulo; se descrever, acertar.
  **Feita em 15/10/2026**, depois de o Marcelo liberar o arquivo (a outra frente, do pacote de entidades do D365,
  já subiu). O trecho da aba "Módulos e integrações" dizia *"O menu Integrações mostra os módulos ligados nos
  conectores do tenant; o tenant sem conector nenhum mostra só o Fiscal"* — as duas metades falsas — e passou a
  descrever o grupo por conector, rotulado pelo rótulo declarado, com o Agendamento de quem agenda, a integração
  que não é item de menu, o conector que não vira grupo e o grupo que nomeia a ausência. As linhas 133-136 e 411,
  sobre os selos, foram conferidas e continuam certas.
  → **Conferido, descreve, e NÃO foi acertado.** O `docs/RUNNING.md` está na lista de arquivos em que o Marcelo pediu
  para não encostar nesta frente (instrução de 2026-10-15), e ela é posterior a esta tarefa. O achado, para quem for
  corrigir:
  - **`docs/RUNNING.md:181-183`**, na aba "Módulos e integrações": *"O menu Integrações mostra os módulos ligados nos
    conectores do tenant; o tenant sem conector nenhum mostra só o Fiscal."* As duas metades ficaram falsas: **não
    existe mais o menu "Integrações"** (existe um grupo por conector, rotulado pelo rótulo que o adapter declara), e o
    tenant sem conector mostra o **grupo que nomeia a ausência, com o Fiscal e o Agendamento** — e não só o Fiscal.
  - **O que está certo e não precisa mexer:** as linhas 133-136 e 411, sobre o **selo** da barra lateral (ambiente e
    integração automática), que esta change não tocou.

## 6. Verificação final

- [x] 6.1 `dotnet build` com 0 warnings e `dotnet test` verde, na raiz.
  → `dotnet build`: "Compilação com êxito. **0 Aviso(s), 0 Erro(s)**", 1min56s. `dotnet test`: **1377 aprovados, 0 com
  falha**, 42 ignorados (os de sempre: 40 de SQL na Infrastructure e 2 do D365 ao vivo), em 11 assemblies. Mesmo número
  do 4.7, como esperado — a 4.8 é só do dashboard. Rodados com `--artifacts-path obj/guard` (ver a nota de ambiente em
  1.8).
- [x] 6.2 `npm test` e `npm run build` no `dashboard/`.
  → `npm test`: **128 aprovados, 11 arquivos** (114 antes da change, 124 no 4.7, 128 com a 4.8). `npm run build`:
  `tsc --noEmit` limpo e `✓ built in 18.01s`. O aviso de chunk > 500 kB é o de sempre do Vite, anterior à change.
- [x] 6.3 `openspec validate --all --strict` sem falha.
  → **`Totals: 29 passed, 0 failed (29 items)`** — as 28 specs publicadas mais a `change/connector-navigation-and-admin-json`.
- [x] 6.4 Prova manual pela tela, com o host e o dashboard de pé, e o registro do que foi conferido:
  - a barra lateral com o grupo do conector e os itens dele; cada item abrindo a tela certa;
  - o Agendamento dentro do grupo do conector;
  - um Viewer sem o "Visualizar JSON" e sem o "Baixar arquivos", e o `403` ao chamar o `/trace` direto;
  - um Admin com os dois botões, e o JSON abrindo.
  → **Aberta: é conferência visual, e depende do Marcelo na tela.** Nesta sessão o ambiente não permitiu nem subir o
  host nem sondar porta local, então não há o que registrar — e marcar sem evidência seria pior que deixar aberta. O
  que já está provado por teste automatizado, e que portanto **não** é o que a prova manual precisa achar:
  - a **árvore de grupos e itens** e a tela que cada item abre: `modules.test.ts`, inclusive a varredura item a item
    contra a tabela escrita à mão (4.1);
  - o **Agendamento só no conector que declara que agenda**: `modules.test.ts` (4.1) e `InstanceInfoTests` (3.1);
  - o **403 do `/trace` para o Viewer, com o armazenamento não lido**, e o 200 para o Admin: `RawTraceRoleTests` (1.2),
    no endpoint de verdade;
  - a **decisão dos dois botões** a partir do `/info`: `rawJson.test.ts` (1.6).
  **O que só a tela prova**, e é o que vale conferir: que os dois botões de fato **aparecem e somem** com o papel (a
  renderização, e não a decisão — ver o desvio de 1.6), que o item aceso acompanha o selecionado nas seis telas (4.6), e
  que ligar um módulo em Configurações redesenha a barra lateral sem recarregar a página.
  **Acrescentar à prova, por causa da 4.8:** entrar com a rede estrangulada e conferir que, antes de o `/info`
  responder, aparece o `Carregando…` — e **não** o cabeçalho "Sem conector configurado".
  **Conferido na tela em 15/10/2026**, com o host subido do build desta fatia na 5200 e o dashboard servido por um
  proxy local temporário (removido depois), logado como Admin do `tenant-a`:
  - a barra lateral desenha **um grupo, "Tax Compliance"** — o rótulo que o adapter declara, e não a plataforma
    "Avalara" —, com os itens Fiscal, Contábil, Inventário e Agendamento, nessa ordem, e o bloco Administração abaixo,
    com Configurações e Usuários. Os selos do rodapé (Ambiente ativo/Sandbox e Integração automática) ficaram como
    estavam;
  - **os seis itens foram abertos um a um** e cada um abriu a tela que declara, com o título e o subtítulo dele na
    topbar: Fiscal → "Notas fiscais integradas e seus status"; Contábil e Inventário → "Módulo reservado", com a tela
    dizendo que o módulo ainda não está disponível e sem card ou contagem nenhuma; Agendamento → "Dispare agora ou
    agende, e acompanhe as execuções"; Configurações → "Conector, adapters e ambiente deste tenant"; Usuários → "Quem
    acessa este tenant e o cadastro do cliente". A barra lateral permaneceu inteira em todas, **inclusive nas duas de
    Administração**, que é onde o descuido passaria;
  - a **tela de entrada** foi o Fiscal, primeiro item do primeiro grupo;
  - **o estado "ainda não respondeu" (4.8)** foi provado atrasando o `/info` em 8s no proxy: a tela mostrou só o
    `Carregando…`, **sem** o cabeçalho "Sem conector configurado", e a árvore completa apareceu quando a resposta
    chegou;
  - **os dois botões da visão técnica aparecem para o Admin** ("Visualizar JSON" e "Baixar arquivos"), decididos pelo
    `canViewRawJson` do `/info` novo — o caminho que o `roles.ts` apagado fazia na tela.

  **Fica por conferir na tela:** o Viewer sem os dois botões (precisa de um login de Viewer) e o JSON com conteúdo — a
  nota aberta era `Ignorado`, sem foto gravada, e o `/trace` respondeu 404, que é o caminho vazio. Os dois estão
  provados por teste (`rawJson.test.ts` e `RawTraceRoleTests`); o que falta é só a confirmação visual.

  **Conferido como Viewer do mesmo tenant (`Carlos Dias`), em 15/10/2026:**
  - a barra lateral mostrou **a mesma árvore** ("Tax Compliance" com Fiscal, Contábil, Inventário e Agendamento) e
    **não mostrou o bloco Administração** — a árvore é de qualquer papel do tenant (3.2) e o bloco é só do Admin (D12);
  - **os dois botões da visão técnica apareceram, e não deviam.** O servidor estava certo: depois de recarregar a
    página, o `/info` respondeu `canViewRawJson: false` para esse usuário. Errada estava a tela, por **cache**: o
    `useInfo` é `queryKey: ['info']` com `staleTime: Infinity`, o `logout` só limpa o token e o usuário, e ninguém
    limpa o cache do React Query — que é um singleton do `main.tsx` e vive enquanto a página viver. Trocar de usuário
    sem recarregar deixa a resposta do usuário anterior valendo. Ver a tarefa 4.9.

  **Conferida a correcao da 4.10, na tela, em 15/10/2026:** logado o Admin por cima da sessao do Viewer, **sem
  recarregar a pagina**, o app **refez o `/info`** — com `staleTime: Infinity` e cache populado isso nao aconteceria,
  e e a prova de que a limpeza roda no `login` —, a resposta veio `canViewRawJson: true` e o bloco Administracao
  voltou. Antes da correcao, a mesma troca ao contrario (Admin para Viewer) mantinha os botoes do usuario anterior.
  Isto fecha justamente a linha que o teste da 4.10 nao cobre: que o `AuthContext` chama a limpeza.

  **6.4 fechada.** O unico item nao conferido visualmente e o JSON com conteudo: a nota aberta era `Ignorado`, sem
  foto gravada, e o `/trace` respondeu 404 — o caminho vazio, que tem teste proprio. Os botoes, o 403 e a decisao
  estao provados.

- [x] 6.5 Ao sincronizar as specs, **ajustar o `## Purpose` do `module-navigation`**: o publicado ainda diz "Organizar a
  barra lateral por módulo de integração", "O tenant sem conector mostra o Fiscal" e "É apresentação, e não permissão",
  e os três são contrariados pelo delta. O texto novo já está escrito na seção `## Purpose` do delta desta change —
  conferir que a sincronização o levou para `openspec/specs/module-navigation/spec.md`, e escrevê-lo à mão se não
  tiver levado.
  → **Cumprida no arquivamento, em 2026-10-15, e o Purpose teve mesmo de ser escrito à mão.** O
  `openspec archive connector-navigation-and-admin-json` sincronizou as três specs (2 requisitos acrescentados, 6
  modificados, 1 removido) e **avisou explicitamente** que o Purpose do delta foi descartado: *"module-navigation —
  delta Purpose ignored; module-navigation already has one. Edit …/openspec/specs/module-navigation/spec.md directly to
  change it."* O `## Purpose` publicado foi então substituído à mão pelo texto do delta — o parágrafo em itálico que
  explica **o que** ele substitui ficou de fora, de propósito: ele é a nota da change para o revisor, e não o texto da
  spec. Conferido depois: o Purpose publicado é o do conector, o requisito removido "O bloco Integrações da barra
  lateral" não está mais lá, e `openspec validate --all --strict` deu **`Totals: 28 passed, 0 failed (28 items)`**
  (28, e não 29, porque a change saiu da lista ao ser arquivada).
  **Anotado para quem vier:** o `openspec archive` **não leva o Purpose do delta** quando a spec publicada já tem um.
  Mudança de Purpose é sempre edição à mão depois do sync.
