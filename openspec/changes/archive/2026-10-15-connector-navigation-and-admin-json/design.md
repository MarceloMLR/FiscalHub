## Context

A motivação está no `proposal.md`; o porquê, no **ADR-0039** (§1, §2 e §5, e as Consequências). Aqui ficam o estado do
código que molda o desenho e as escolhas que o ADR não precisava fixar. O levantamento abaixo foi **conferido no
código** (revisão de 2026-10-14), e não presumido.

**A barra lateral hoje** (`dashboard/src/App.tsx` e `dashboard/src/features/modules/modules.ts`):

- um bloco `NavSection` fixo, "Integrações", com um `NavItem` por `ModuleView` — `documents`, `accounting`,
  `inventory`, `integrations` —, filtrados por `visibleModuleViews(modules)`, em que `modules` é a lista plana do
  `/info` e o `integrations` (Agendamento) tem `module: null`, isto é, **aparece sempre**;
- abaixo, o bloco "Administração" (Configurações, Usuários), só para Admin;
- no rodapé, o selo de ambiente e o selo da integração automática (`automatic-integration`), que esta fatia não toca;
- a tela é escolhida por uma cadeia de ternários sobre `shown`, no mesmo arquivo, e `titles[shown]` dá o título e o
  subtítulo da topbar. **O item e a tela não estão ligados por dado nenhum:** são duas tabelas separadas que o descuido
  do ADR-0039 ("um item apontando para uma tela que não era a dele") desalinharia sem que nada acusasse.

**O `/info` hoje** (`InstanceInfo`, em `src/FiscalHub.Application/Connectors/`): `environment`,
`automaticIntegration`, `inboundScans` e `modules` — a lista plana, derivada por `TenantModules.Of(profile)` como a
união dos módulos ligados nos conectores, na ordem Fiscal, Contábil, Inventário. Responde para qualquer papel do tenant
logado.

**A árvore do perfil já existe** (`PlatformAccount` → `PlatformConnector` → `ConnectorModule` → `ConnectorIntegration`),
entregue na `platform-account-and-connectors`. O conector é identificado pelo `Adapter`; a declaração
(`IAdapterDeclaration`) traz `Adapter`, `Platform` e `Modules`, e **nenhum rótulo de tela**.

**A permissão da visão técnica já é do servidor.** No `Program.cs`:

- `string[] rawTraceRoles = ["Admin"];` com o comentário que diz ser o gancho do papel de Suporte;
- `/trace/{tenantId}/{naturalKey}` e `/documents/{tenantId}/{naturalKey}/download` com
  `.RequireAuthorization(policy => policy.RequireRole(rawTraceRoles))`;
- `/documents/{tenantId}/{naturalKey}/reading` sem papel, para a primeira vista.

Isso é o que o `platform-response-trace` já exige ("As fotos cruas só para quem pode ver o JSON"). O que **não**
existe:

- **nenhum teste de endpoint por papel.** A suíte não tem `WebApplicationFactory` nem `TestHost`; o `rawTraceRoles` só
  aparece no `Program.cs`, e nenhum teste o cita. Um `RequireRole` apagado por engano hoje passa verde;
- **a lista num lugar só.** O `dashboard/src/features/auth/roles.ts` tem `RAW_JSON_ROLES = ['Admin']`, usado pelo
  `DocumentDetail` ("Visualizar JSON") e pelo `NoteDialog` ("Baixar arquivos"), com um comentário pedindo que as duas
  listas andem juntas. Duas listas que precisam de um comentário para não divergir são uma lista a mais.

## Goals / Non-Goals

**Goals**

- o eixo da barra lateral passa a ser o conector, com os módulos abaixo, em dois níveis;
- o Agendamento vira item do conector que o declara;
- a regra de papel da visão técnica ganha **teste de endpoint por papel**, e a tela deixa de ter lista própria;
- todo item de menu declara a tela que abre, e isso é provado por teste.

**Non-Goals** — estão no `proposal.md`. Em uma frase: nada de AvaTax, de carga, de fila por integração, de wizard e de
URLs declaradas.

## Decisions

### D1 — A permissão é do servidor, e não da tela

**É a decisão central da fatia, e o ADR-0039 §5 a chama de "a primeira permissão de verdade do produto".**

A resposta do `/trace` e do zip não é "a mesma tela com mais detalhe". Ela contém:

- **credencial do ERP** — a foto da fonte do D365 é o que o conector leu, e o motivo e os cabeçalhos de uma falha de
  autenticação passam por ali (o `platform-response-trace` redige token e segredo exatamente porque eles chegariam lá);
- **endereço de plataforma** — o método e a URL do envio, que são a prova de para qual ambiente a nota foi;
- **payload de documento** — a nota inteira, com partes, itens, valores e impostos.

Esconder o botão não protege nada disso: o endpoint continua respondendo a quem souber a URL, e o dashboard é um
cliente como outro qualquer. A diferença entre uma interface e um controle de acesso é **quem recusa**. Por isso:

1. **quem decide é o endpoint**, pela lista de papéis do servidor;
2. **a tela segue o servidor**, perguntando a ele quem é que vê (D7) — e nunca o contrário;
3. **a regra tem teste de papel**, e não só de tenant (D8). O limite de tenant já é testado em outro lugar
   (`tenant-boundary`); o que nunca foi testado é o papel, e é ele que esta fatia trava.

O custo de errar é assimétrico. Uma tela que esconde demais produz um chamado; um endpoint que responde demais entrega
credencial e documento a quem não deveria vê-los, **em silêncio**, sem nada no log que pareça errado.

### D2 — O cliente com um conector e um módulo ganha um nível de menu que não usa

Com um conector só e o Fiscal só, a barra lateral mostra **"Tax Compliance" → "Fiscal"**: duas linhas para uma página,
onde hoje há uma. É o caso do primeiro cliente, e é a regra, não a exceção, durante um bom tempo.

**Aceitamos o custo, e a razão precisa estar escrita** (ADR-0039, Consequências):

- **colapsar quando há um conector só faria a interface ter duas formas conforme o cliente.** O usuário que aprendeu o
  produto num cliente com um conector reaprende ao ver o segundo; o suporte passa a ter duas telas para descrever; e
  cada tela nova precisa decidir em qual das duas formas ela vive;
- **o colapso é um estado que muda sozinho.** No dia em que a Fiscosys liga o AvaTax para aquele cliente, o menu dele
  muda de forma — não ganha um item, muda de **estrutura** — sem que ninguém tenha mexido na tela;
- **o nível a mais é honesto.** Ele diz que o Fiscal daquele cliente é o Fiscal do Tax Compliance, e não "o Fiscal".
  Quando existirem dois, essa frase é a que evita a pergunta "por que a nota aparece duas vezes";
- **o preço é pequeno e visível:** uma linha de cabeçalho de grupo, 232 px de largura, sem rolagem a mais.

O que **não** aceitamos é esconder o grupo quando há um conector só e mostrá-lo quando há dois. Preferimos uma
estrutura só.

### D3 — O rótulo do conector é declarado pelo adapter

A barra lateral precisa de um nome para o grupo. O que existe é a chave do conector, que é o **nome do adapter**
(`Avalara`) — e "Avalara" é a plataforma, não o produto. Com o AvaTax, os dois conectores seriam `Avalara` e algo como
`AvalaraAvaTax`: dois grupos com o mesmo começo, e nenhum deles com o nome que o ADR-0039 usa ("Tax Compliance" e
"AvaTax").

**Decisão:** `IAdapterDeclaration` ganha `Label`, e a Avalara declara `Tax Compliance`.

- é **declaração**, como os módulos e as integrações: característica da plataforma, em código, que quem configura não
  edita (ADR-0037). Não vira campo do perfil, e com isso não vira variação por cliente;
- a alternativa — um mapa de rótulos no dashboard, por nome de adapter — poria o conhecimento do adapter do lado da
  tela, e um adapter novo nasceria sem nome no menu até alguém lembrar de editar o mapa;
- o `Label` é texto de tela e **não** entra em chave, nome de segredo, URL nem comparação: a identidade continua sendo
  o `Adapter`, comparado exato.

**O que fica de fora:** Configurações continua nomeando o conector pelo adapter. Alinhar os dois nomes é cosmético,
mexe na tela que acabou de ser entregue e não é obrigado por nada desta fatia. Fica anotado como ponto em aberto.

### D4 — Quem agenda é o adapter que declara, e não a integração ligada

O Agendamento precisa aparecer sob um conector e não sob outro. Três caminhos foram considerados:

1. **sob o conector que tem a integração `notas-fiscais` ligada.** Rejeitado: um cliente que contratou o Tax Compliance
   e ainda não preencheu a integração perderia o Agendamento inteiro — e é justamente pelo Agendamento que ele dispara
   a primeira carga manual. Hoje o Agendamento aparece **sempre**, e essa garantia não pode sumir por uma configuração
   pela metade;
2. **um `if` por nome de adapter na tela.** Rejeitado: é lógica de produto escrita na tela, e o repositório proíbe
   variação por nome em código (CLAUDE.md §9);
3. **o adapter declara.** Escolhido.

`IAdapterDeclaration` ganha `Scheduling` (bool). A Avalara declara `true`; o AvaTax, quando existir, declara `false`, e
o ADR-0039 §2 já diz por quê — cálculo e emissão são disparados pelo ERP, não por horário.

**Nota honesta sobre o que o Agendamento é hoje:** a tela de agendamento dispara a **captura no ERP** (integração
manual, diária D-1, agendada), que é lado de entrada. Pendurá-la num conector de saída é uma escolha de apresentação do
ADR-0039, e não um vínculo técnico. Ela se sustenta porque a captura existe para alimentar aquele conector; se um dia
houver dois conectores que capturam, o Agendamento precisará dizer **o quê** ele agenda — e aí vira o seletor de
integração que o ADR-0037 prevê e que esta fatia não faz.

### D5 — O tenant sem conector ganha um grupo que nomeia a ausência

Hoje, um tenant sem conector nenhum vê o Fiscal e o Agendamento (`TenantModules.Default`). Com o eixo no conector não
há grupo onde pendurá-los.

**Decisão:** a barra lateral mostra **um grupo**, sem conector, rotulado como "Sem conector", com o Fiscal e o
Agendamento dentro — o mesmo conjunto de hoje, na mesma ordem, abrindo as mesmas telas.

- **nada some.** O tenant não provisionado continua entrando no produto e vendo o que via;
- **a estrutura é uma só** (D2): um grupo com itens, como qualquer outro;
- **o rótulo nomeia o que falta**, em vez de inventar um conector que não existe. Para o Admin, a saída está em
  Configurações, no bloco de baixo da mesma barra; para os demais papéis, é informação de que a configuração ainda não
  chegou. É a mesma linha da recusa que nomeia o que falta em vez de calar.

A alternativa — mostrar os itens soltos, sem grupo, só nesse caso — é exatamente a interface com duas formas que o D2
rejeita.

**Esta decisão SUPERSEDE o D7 da change arquivada `platform-account-and-connectors`.** Aquele D7 decidiu que o `/info`
de um tenant sem conector responde a lista plana `["Fiscal"]`, e escreveu a razão: "A lista vazia deixaria a barra
lateral só com o Agendamento, escondendo o Fiscal e as notas já gravadas; o padrão mantém o que esse tenant via antes
desta change". A razão continua válida; **o mecanismo muda de camada**. A partir desta fatia:

- **o servidor responde a árvore vazia** (D6): não existe conector presumido, e o `/info` não inventa um para pendurar
  o Fiscal. O padrão `["Fiscal"]` do D7 some junto com a lista plana que o carregava;
- **quem decide o que desenhar é a tela**: é ela que mostra o grupo que nomeia a ausência, com o Fiscal e o
  Agendamento, abrindo as mesmas telas.

**O resultado que o usuário vê é o mesmo** que o D7 protegia: o Fiscal continua alcançável e as notas já gravadas
continuam visíveis para o tenant não provisionado. O que mudou foi **onde** a garantia mora — era um padrão do
servidor, passa a ser uma regra de tela. Registrado aqui explicitamente porque, sem isso, quem comparar as duas changes
lerá a árvore vazia como regressão do que a fatia anterior tinha decidido.

### D6 — O `/info` responde a árvore, e a lista plana sai

O `/info` passa a trazer `connectors`: uma entrada por conector do tenant, com `adapter`, `label`, `scheduling` e
`modules` (os ligados naquele conector, na ordem Fiscal, Contábil, Inventário). O campo `modules`, plano, **sai**.

- **a justificativa da lista plana morreu.** O `platform-account-connectors` a manteve com uma razão escrita: "porque é
  por ela que a barra lateral é montada". Não é mais;
- **duas representações da mesma verdade na mesma resposta** criam a pergunta "e quando divergem?" — a mesma pergunta
  que o ADR-0037 evitou ao não gravar a declaração do adapter no perfil;
- **campo sem leitor é o que o `STATUS.md` critica** na ordem das fatias ("configuração que nada lê").

**A ordem dos conectores** é a ordem gravada no perfil (contas na ordem, conectores na ordem dentro de cada conta) —
a mesma que Configurações mostra. Uma ordem só, num lugar só, sob controle de quem configura.

**Consequência assumida:** dois cenários de outras specs citam a lista plana e vêm no delta junto (um em "O adapter
declara os módulos e as integrações", outro na migração). É o preço de não deixar uma frase falsa de pé.

### D7 — A tela pergunta ao servidor quem vê as fotos cruas

O `/info` ganha `canViewRawJson`: um booleano, para o **usuário logado**, derivado da mesma lista de papéis que o
`RequireRole` do `/trace` e do zip usa. O `dashboard/src/features/auth/roles.ts` deixa de existir.

- **é a lista do servidor, não uma cópia dela.** O `platform-response-trace` já exige que a lista more num lugar só no
  servidor e que a tela use a mesma; hoje são duas listas e um comentário. Com o `/info`, o dia em que um papel de
  Suporte entrar na lista do servidor é o dia em que os botões aparecem para ele, sem tocar no dashboard;
- **booleano, e não a lista.** Mandar os papéis para a tela a faria decidir de novo. O servidor já sabe quem está
  logado: ele responde a pergunta, não os dados para a pergunta;
- **antes de o `/info` responder, nenhum botão aparece.** É a mesma regra do selo de ambiente, que não afirma nada
  antes da resposta (`platform-account-connectors`): um botão que aparece e depois some é pior do que um que aparece
  um instante depois, e um botão que promete o que o servidor vai recusar com 403 é uma promessa quebrada.

### D8 — O teste de papel sem subir o host inteiro

O host **não** sobe em teste hoje: o `Program.cs` faz `await app.Services.MigrateProcessingSchemaAsync()`,
`FillPendingIntegrationPathAsync`, `LocalSeed.RunAsync` e `SeedDevDataAsync` na subida, e registra três
`HostedService`. Um `WebApplicationFactory<Program>` exigiria SQL Server, Azurite e Service Bus no ar para provar um
`403`. Testes que precisam de infra acabam `[Skip]`, e um teste pulado não prova nada — é a mesma lição do harness de
migração da fatia anterior.

**Decisão:** o mapeamento dos endpoints das fotos cruas e a lista de papéis saem do corpo do `Program.cs` para um ponto
único no Host (uma extensão `MapRawTraceEndpoints` e a lista ao lado dela), que o `Program.cs` **chama**. O teste sobe
um `TestServer` mínimo: a mesma autenticação JWT, a mesma extensão, e um `IProcessingTrace` de mentira no lugar do
Blob.

- **o que o teste cobre é o registro de verdade.** Se alguém apagar o `RequireRole` da extensão, o teste quebra. Se
  alguém parar de chamar a extensão no `Program.cs`, o build quebra, porque as rotas somem;
- **o que o teste não cobre** é a composição inteira do host (ordem de middleware, DI real). Isso continua sendo
  provado pelo ponta a ponta e pela prova manual, como hoje;
- **e ele prova a frase inteira do requisito:** o `403` do Viewer com o armazenamento das fotos **não lido** — o duplo
  de teste conta as leituras, e a contagem é zero. Esconder o 403 depois de ler seria vazamento por tempo de resposta e
  por log.

**Esse harness é o começo de um lugar**, não um arquivo avulso: é onde o próximo endpoint com regra de papel se prova.

### D9 — A integração não vira item de menu

O ADR-0039 §1 rejeita o terceiro nível: com três integrações no Contábil e outras tantas no Inventário, a árvore daria
mais de uma dúzia de linhas numa barra de 232 px. A integração é **coluna e filtro dentro da página do módulo**.

Nesta fatia isso é uma **restrição**, e não uma entrega: o Fiscal tem uma integração só (`notas-fiscais`), e uma coluna
com um valor repetido não informa nada. A coluna nasce com o Contábil, que é quem tem três.

### D10 — Item de menu e tela andam juntos, em dado

O descuido que o ADR-0039 avisa é um item apontando para a tela errada. Hoje isso é possível porque o item (`MODULE_VIEWS`),
o título (`titles`) e a tela (a cadeia de ternários do `App.tsx`) são três tabelas separadas.

**Decisão:** o modelo de navegação passa a ser uma árvore de grupos e itens em que **cada item carrega a identidade, o
rótulo, o título, o subtítulo e a tela que abre**. O `App.tsx` desenha o que o modelo diz e abre o que o item diz, sem
ternário por nome de tela. Um teste percorre a árvore inteira e confere item a item.

**A identidade do item é o par (conector, módulo)**, e não o módulo sozinho: com dois conectores existem dois Fiscais,
e eles abrem páginas diferentes. Hoje só existe um, e a tela é escolhida pelo módulo — mas a chave já nasce completa,
para que o segundo conector não precise reabrir o modelo.

**Um cuidado conferido no código:** o `MODULE_VIEWS` de hoje não serve só à barra lateral — o
`features/connectors/connectorPanelRows.ts` o importa para montar as linhas de módulo do painel do conector, em
Configurações. Os **rótulos dos módulos** (Fiscal, Contábil, Inventário) continuam, por isso, numa tabela
compartilhada, que a árvore de navegação e o painel do conector leem. O que vira árvore é a navegação; a tabela de
rótulos não some junto.

### D11 — A tela de entrada e o item que some

Continuam como hoje, traduzidos para a árvore:

- **a entrada** é o primeiro item do primeiro grupo. Com a ordem de D6 e os módulos na ordem Fiscal, Contábil,
  Inventário, isso dá o Fiscal do primeiro conector — o mesmo que hoje;
- **o item selecionado que some** (um módulo que o Admin acabou de desligar, um conector removido) cai na tela de
  entrada, como hoje cai.

### D12 — O bloco Administração fica fora do eixo

Configurações e Usuários não são de conector nenhum: são do tenant, e só para Admin. Continuam num bloco próprio,
abaixo dos conectores, como hoje. O eixo do ADR-0039 é o das **integrações**, e não o da administração.

## Risks / Trade-offs

- **Risco: a fatia parece de tela e tem regra de servidor dentro.** Mitigação: o grupo 1 das tarefas é o servidor, e
  ele termina antes de a tela ser tocada. O teste de papel entra antes do código, por exigência explícita.
- **Risco: tirar a lista plana do `/info` quebra um leitor esquecido.** Mitigação conferida: os únicos leitores de
  `info.modules` são o `App.tsx` e o `modules.ts`, ambos reescritos nesta fatia. Fora deles, `modules` só aparece no
  `GET /connector`, que é outra resposta e não muda.
- **Risco: um nível de menu a mais em todo cliente.** Aceito e justificado em D2; é consequência escrita do ADR-0039.
- **Trade-off: o harness de teste de endpoint não é o host inteiro** (D8). Ele prova a regra de papel, que é o que o
  ADR pede, e não a composição do host.
- **Risco: o rótulo declarado diverge do nome que Configurações mostra** (D3). Aceito nesta fatia, anotado como ponto
  em aberto.

## Suposições (sessão sem interlocutor)

Estas decisões foram tomadas sem poder perguntar. Todas são a leitura **conservadora** do ADR-0039 e do código, e todas
são baratas de reverter antes da implementação.

- **S1 — o rótulo da Avalara é `Tax Compliance`** (D3), que é como o ADR-0039 e o canvas nomeiam o conector. Se o nome
  de produto for outro, muda uma constante no adapter.
- **S2 — o grupo do tenant sem conector se chama "Sem conector"** (D5). O texto exato é de produto; o requisito é que o
  grupo exista, nomeie a ausência e não esconda os itens.
- **S3 — a ordem dos conectores é a gravada no perfil** (D6). A alternativa seria ordenar por rótulo; a gravada deixa a
  ordem na mão de quem configura e casa com Configurações.
- **S4 — a lista plana `modules` sai do `/info`** (D6), em vez de conviver com a árvore. A alternativa — manter as duas
  — evitaria os dois deltas em specs vizinhas, ao preço de um campo sem leitor.
- **S5 — antes de o `/info` responder, os botões de JSON e de download não aparecem** (D7), inclusive para o Admin. A
  alternativa seria mostrá-los otimistamente pelo papel do JWT, o que recria a decisão na tela.
- **S6 — o Agendamento do conector abre a mesma `IntegrationsPage` de hoje**, sem filtro por conector. Com um conector
  que agenda, filtrar não muda nada; o seletor é da fatia que tiver dois.
- **S7 — Configurações continua nomeando o conector pelo adapter** (D3), e não pelo rótulo.
- **S8 — o `/info` continua aberto a qualquer papel do tenant**, agora com a árvore de conectores. Ela não traz
  credencial, endereço nem segredo: traz os nomes dos produtos que o próprio tenant contratou, que é o que a barra
  lateral já mostra.

## Open Questions

- **O rótulo em Configurações.** Alinhar o painel do conector com o rótulo declarado (S7) é cosmético e cabe na
  primeira fatia que tocar aquela tela.
- **O Agendamento com dois conectores que capturam** (D4). Vira o seletor de integração do ADR-0037 quando existir o
  segundo.
- **O papel de Suporte** (ADR-0030). Quando existir, entra na lista do servidor e o `/info` o leva à tela sem mudança
  no dashboard — é o ganho de D7, e a prova disso é um teste com a lista de dois papéis.
