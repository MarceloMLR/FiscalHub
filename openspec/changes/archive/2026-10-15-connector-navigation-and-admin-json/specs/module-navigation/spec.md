## Purpose

Organizar a barra lateral do dashboard por **conector** do tenant, com os módulos ligados em cada um (Fiscal, Contábil,
Inventário) abaixo dele, e o Agendamento junto deles no conector cujo adapter declara que ele agenda. O adapter de cada
conector declara o rótulo que aparece no menu, os módulos que atende e se ele agenda; o Admin liga os contratados no
painel do conector, em Configurações. A integração não é item de menu: ela é coluna e filtro dentro da página do
módulo. O `/info` responde a **árvore** que a barra lateral desenha, e um tenant sem conector recebe a árvore **vazia**
— nesse caso é a tela que mostra um grupo nomeando a ausência, com o Fiscal e o Agendamento. Esconder um módulo é
apresentação, e não permissão; a **visão técnica** (os JSONs crus e os logs) é permissão, conferida no servidor
(`platform-response-trace`).

*Esta seção substitui o Purpose publicado, que dizia "por módulo de integração (Fiscal, Contábil, Inventário e
Agendamento)", "O tenant sem conector mostra o Fiscal" e "É apresentação, e não permissão" — os três contrariados pelos
requisitos abaixo: o eixo passa a ser o conector, o servidor passa a responder a árvore vazia (o Fiscal do tenant não
provisionado vira regra de tela) e a frase sobre permissão passa a valer só para o módulo.*

## ADDED Requirements

### Requirement: O eixo da barra lateral é o conector

A barra lateral MUST agrupar por **conector**, com os módulos ligados naquele conector abaixo dele, em **dois níveis**:
conector e módulo. A integração MUST NOT ser item de menu — ela é coluna e filtro dentro da página do módulo
(ADR-0039 §1). Com três integrações no Contábil e outras tantas no Inventário, a árvore completa daria mais de uma
dúzia de linhas numa barra de 232 px.

- **O grupo** é um conector do tenant, rotulado pelo **rótulo que o adapter dele declara**
  (`platform-account-connectors`) — e nunca pela plataforma nem pela chave do adapter.
- **Os itens** de um grupo são os módulos ligados naquele conector, na ordem Fiscal, Contábil, Inventário, seguidos do
  **Agendamento**, quando o adapter daquele conector declara que ele agenda. Não existe agendamento para todo conector:
  cálculo e emissão são disparados pelo ERP, e não por horário (ADR-0039 §2).
- **A ordem dos grupos** MUST ser a ordem gravada no perfil, a mesma que Configurações mostra.
- **Um conector que não produz item nenhum** — sem módulo ligado e sem agendamento — MUST NOT virar grupo: cabeçalho
  sem item é ruído. O que o Admin faz com ele é em Configurações.
- **O nível do conector MUST aparecer mesmo havendo um conector só**, e MUST NOT ser colapsado conforme o cliente. Um
  cliente com um conector e um módulo ganha um nível de menu que não usa, e esse custo é aceito: colapsar faria a
  interface ter duas formas, e mudar de forma sozinha no dia em que o segundo conector fosse ligado (ADR-0039,
  Consequências).
- **O bloco "Administração"** (Configurações e Usuários) continua como está, abaixo dos grupos de conector e só para
  Admin: ele é do tenant, e não de conector nenhum.
- **Cada item MUST declarar a tela que abre**, e MUST abrir essa tela, com o título e o subtítulo dela na topbar. O
  Fiscal abre a tela de documentos fiscais, com os cards, os grupos e o detalhe; o Contábil e o Inventário abrem o
  painel de módulo reservado; o Agendamento abre a tela de integração manual, com os agendamentos e as execuções.
- **A tela aberta ao entrar** é o primeiro item do primeiro grupo.
- **O item selecionado que deixa de existir** — o módulo que o Admin acabou de desligar, o conector que saiu — MUST
  cair na tela de entrada.

#### Scenario: Um conector, um módulo
- **WHEN** um tenant tem um conector cujo adapter declara o rótulo `Tax Compliance` e que ele agenda, com só o módulo
  Fiscal ligado
- **THEN** a barra lateral mostra o grupo "Tax Compliance" com os itens "Fiscal" e "Agendamento"
- **AND** o nível do conector aparece, mesmo havendo um conector só

#### Scenario: Os módulos de um conector, na ordem
- **WHEN** o conector tem o Inventário e o Fiscal ligados, nessa ordem no perfil
- **THEN** o grupo dele mostra "Fiscal", "Inventário" e "Agendamento", nessa ordem

#### Scenario: Dois conectores, dois grupos
- **WHEN** o tenant tem dois conectores, o primeiro com o Fiscal ligado e o segundo com o Fiscal e o Contábil
- **THEN** a barra lateral mostra dois grupos, na ordem gravada no perfil, cada um com os módulos dele
- **AND** o Fiscal aparece uma vez em cada grupo, e não uma vez só

#### Scenario: Conector que não agenda
- **WHEN** o adapter de um dos conectores do tenant declara que ele não agenda
- **THEN** o grupo dele não tem o item "Agendamento"
- **AND** o grupo do conector que agenda continua tendo

#### Scenario: Conector que não produz item
- **WHEN** um tenant tem dois conectores, e um deles não tem módulo ligado e o adapter dele declara que não agenda
- **THEN** a barra lateral mostra só o grupo do outro conector

#### Scenario: A integração não é item de menu
- **WHEN** um conector tem o módulo Contábil ligado com as integrações de plano de contas, lançamentos e saldos
- **THEN** o grupo dele mostra o item "Contábil", e nenhum item por integração

#### Scenario: Cada item abre a tela dele
- **WHEN** o usuário abre, um a um, todos os itens da barra lateral
- **THEN** cada item abre a tela que ele declara: o Fiscal, os documentos fiscais; o Contábil e o Inventário, o painel
  reservado; o Agendamento, a integração manual e os agendamentos
- **AND** o título da topbar é o do item aberto

#### Scenario: O painel reservado
- **WHEN** o usuário abre o Contábil de um conector
- **THEN** a tela diz que o módulo ainda não está disponível
- **AND** nenhum documento fiscal, card ou contagem aparece nela

#### Scenario: A tela aberta ao entrar
- **WHEN** um usuário entra no dashboard, e o primeiro grupo é "Tax Compliance", cujo primeiro item é o Fiscal
- **THEN** a tela aberta é o Fiscal daquele conector

#### Scenario: O módulo desligado enquanto estava aberto
- **WHEN** o Admin desliga, no conector, o módulo que o usuário está vendo
- **THEN** a barra lateral deixa de mostrar aquele item, e a tela passa a ser a de entrada

#### Scenario: O bloco Administração
- **WHEN** um Admin olha a barra lateral
- **THEN** abaixo dos grupos de conector aparece o bloco "Administração", com Configurações e Usuários
- **AND** um Viewer do mesmo tenant não vê esse bloco

### Requirement: O tenant sem conector vê um grupo que nomeia a ausência

Quando o `/info` respondeu e **nenhum conector do tenant produz item** — porque não há conector, ou porque nenhum deles
tem módulo ligado nem agendamento —, a barra lateral MUST mostrar **um grupo**, sem conector, cujo rótulo diz que não
há conector configurado, com os itens Fiscal e Agendamento: os mesmos de antes desta mudança, abrindo as mesmas telas.

- Havendo resposta, a barra lateral MUST NOT ficar sem grupo nenhum, e MUST NOT mostrar os itens soltos, fora de grupo:
  a estrutura é uma só, para qualquer cliente.
- O rótulo MUST **nomear a ausência**, em vez de inventar um conector. Para o Admin, o conserto está em Configurações,
  no bloco logo abaixo; para os demais papéis, é a informação de que a configuração ainda não chegou.
- O grupo MUST sumir assim que algum conector passar a produzir item.
- **Antes da resposta do `/info`**, a barra lateral MUST NOT mostrar esse grupo e MUST NOT afirmar ausência nenhuma:
  enquanto não se sabe que conectores o tenant tem, a tela espera a resposta, como já espera para os botões da visão
  técnica e para o selo de ambiente. A árvore **vazia** é uma resposta, e é ela que nomeia a ausência: "ainda não
  respondeu" e "respondeu vazio" são estados diferentes.

#### Scenario: Tenant sem conector
- **WHEN** um Viewer de um tenant sem conector nenhum entra no dashboard
- **THEN** a barra lateral mostra um grupo que diz não haver conector configurado, com os itens "Fiscal" e
  "Agendamento"
- **AND** a tela aberta é o Fiscal

#### Scenario: O único conector não produz item
- **WHEN** o único conector do tenant não tem módulo ligado e o adapter dele declara que não agenda
- **THEN** aquele conector não vira grupo
- **AND** a barra lateral mostra o grupo que diz não haver conector configurado, com o Fiscal e o Agendamento

#### Scenario: Antes de o /info responder
- **WHEN** um usuário entra no dashboard e o `/info` ainda não respondeu
- **THEN** a barra lateral não mostra o grupo que nomeia a ausência, e não afirma que não há conector configurado
- **AND** quando a resposta chega, a barra lateral mostra a árvore do tenant — ou, se ela vier vazia, o grupo que
  nomeia a ausência

#### Scenario: O grupo some quando o conector é configurado
- **WHEN** o Admin liga o módulo Fiscal no conector `Avalara` e salva
- **THEN** a barra lateral passa a mostrar o grupo do conector, com o Fiscal
- **AND** o grupo que nomeia a ausência deixa de aparecer

## MODIFIED Requirements

### Requirement: Esconder um módulo é apresentação, e não permissão

Os módulos decidem o que a barra lateral mostra, e não o acesso à API. A API MUST continuar respondendo para um módulo
escondido, com as mesmas regras de papel e de tenant de antes. Restringir o acesso por módulo é outra fatia.

O módulo continua sem comportamento próprio (ADR-0037 §1). Quem decide o envio é a **integração** ligada no conector
(`platform-account-connectors`), e uma integração só fica ligada junto com o módulo dela. Por isso, desligar um módulo
num conector desliga as integrações dele, e um envio que dependia de uma delas passa a ser rejeitado como configuração
do conector. Isso é efeito da integração desligada, e não do módulo escondido: a leitura do que já foi enviado continua
respondendo.

**A frase vale para o módulo, e não para a visão técnica.** Os JSONs do ERP, do nosso domínio e da plataforma, e os
logs, no detalhe de um documento, são do Admin por **permissão**, conferida no servidor (`platform-response-trace`,
ADR-0039 §5). A diferença é essa: um módulo escondido continua respondendo a quem chamar o endpoint; a visão técnica
recusa. Esconder o botão não é o controle — o endpoint é.

#### Scenario: Fiscal escondido
- **WHEN** o tenant-a tem só o módulo Inventário, e um usuário do tenant-a chama o `/groups` direto
- **THEN** a resposta é a mesma de um tenant com o Fiscal, com os grupos do tenant-a
- **AND** a barra lateral do tenant-a não mostra o Fiscal

#### Scenario: O Fiscal desligado no conector
- **WHEN** o Admin do tenant-a desliga o módulo Fiscal no conector `Avalara` e salva, e uma nota do tenant-a é
  despachada
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia a integração de
  notas fiscais e aponta a tela
- **AND** nenhuma requisição é feita à plataforma
- **AND** o `/groups` do tenant-a continua respondendo os grupos já gravados

#### Scenario: A visão técnica não é apresentação
- **WHEN** um Viewer do tenant-a pede direto o endpoint das fotos cruas de uma nota do tenant-a
- **THEN** a resposta é 403, e não o conteúdo — ao contrário do módulo escondido, cujo endpoint continua respondendo

### Requirement: Os módulos vêm dos conectores do tenant

Os módulos que um tenant tem MUST ser os **ligados nos conectores dele** (`platform-account-connectors`), e MUST NOT
ser uma lista gravada no tenant.

- **Os valores aceitos:** `Fiscal`, `Contabil` e `Inventario`. Quem os declara é o adapter de cada conector; o perfil
  liga os contratados.
- **A forma da resposta:** o `/info` MUST responder a **árvore que a barra lateral desenha**, para qualquer papel: uma
  entrada por conector do tenant, na ordem gravada no perfil, com o adapter, o rótulo declarado, se ele agenda, e os
  módulos ligados nele, na ordem Fiscal, Contábil, Inventário, sem repetir. A leitura do perfil (`GET /connector`)
  continua devolvendo os módulos dentro de cada conector, que é onde eles moram.
- **A lista plana sai.** O `/info` MUST NOT responder uma lista de módulos do nível do tenant. Ela existia porque era
  por ela que a barra lateral era montada, e a barra lateral passou a ser montada pela árvore; duas representações da
  mesma verdade na mesma resposta criariam a pergunta de qual vale.
- **O padrão:** um tenant sem conector nenhum MUST receber a árvore **vazia**. Não existe conector presumido: o que a
  barra lateral desenha nesse caso é regra de tela (ver "O tenant sem conector vê um grupo que nomeia a ausência"), e
  não um conector que o servidor inventa.
- **A gravação:** o `PUT /connector` MUST NOT aceitar uma lista de módulos no nível do tenant. Um corpo que ainda traga
  o campo MUST ser **recusado** com HTTP 400, que diz que os módulos são ligados em cada conector e manda recarregar a
  tela. Aceitar e descartar faria o operador acreditar que configurou algo. Um corpo sem o campo mantém os módulos dos
  conectores.
- **O tenant:** vale só para o tenant do usuário logado.

#### Scenario: Tenant sem conector
- **WHEN** um tenant não tem conector nenhum, e um Viewer dele chama o `/info`
- **THEN** a resposta traz a lista de conectores vazia
- **AND** não traz lista de módulos nenhuma no nível do tenant

#### Scenario: Dois conectores, módulos somados
- **WHEN** o tenant-a tem um conector com o Fiscal ligado e outro com o Fiscal e o Inventário
- **THEN** o `/info` responde dois conectores, o primeiro com `["Fiscal"]` e o segundo com `["Fiscal", "Inventario"]`
- **AND** os módulos **não** são somados numa lista do tenant: cada conector traz os seus, na ordem Fiscal, Contábil,
  Inventário

#### Scenario: Admin liga o Inventário num conector
- **WHEN** o Admin do tenant-a liga o módulo Inventário no conector `Avalara` e salva
- **THEN** o `/info` do tenant-a passa a trazer `["Fiscal", "Inventario"]` nos módulos daquele conector
- **AND** a barra lateral mostra o Inventário sob aquele conector, sem recarregar a página

#### Scenario: Corpo antigo com a lista do tenant
- **WHEN** uma gravação chega com `modules = ["Fiscal", "Contabil"]` no nível do tenant
- **THEN** a gravação responde 400, dizendo que os módulos são ligados em cada conector e mandando recarregar a tela
- **AND** nada é gravado, e o `/info` continua respondendo os módulos dos conectores

#### Scenario: Só o tenant do usuário
- **WHEN** o Admin do tenant-a desliga o Contábil no conector dele, e o tenant-c tem o Contábil ligado
- **THEN** o `/info` do tenant-c continua trazendo o Contábil no conector dele

## REMOVED Requirements

### Requirement: O bloco Integrações da barra lateral

**Reason**: o bloco era um só, fixo, chamado "Integrações", com os módulos do tenant como sub-blocos e o Agendamento
sempre ao lado deles. Com o eixo no conector (ADR-0039 §1), não existe mais um bloco: existe um grupo por conector, e o
Agendamento desce para dentro do conector que o declara (§2). A regra "o Agendamento MUST aparecer sempre, qualquer que
seja o conjunto de módulos do tenant" deixa de valer: ela nasceu num mundo de um conector só, e o ADR-0039 §2 registra
que um agendamento global ofereceria tipos de integração que não se agendam. Os cenários "Tenant só com o Fiscal", "Os
quatro sub-blocos" e "Sem o Fiscal" descreviam a montagem por módulo do tenant.

**Migration**: substituído por "O eixo da barra lateral é o conector", que mantém **as mesmas telas e os mesmos
rótulos de módulo**, mudando só o agrupamento e a origem de cada item, e por "O tenant sem conector vê um grupo que
nomeia a ausência", que preserva o comportamento de hoje do tenant não provisionado — Fiscal e Agendamento, com o
Fiscal como tela de entrada. O cenário "O painel reservado" segue, palavra por palavra, dentro do requisito novo.
