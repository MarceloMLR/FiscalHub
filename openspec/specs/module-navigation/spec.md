# module-navigation Specification

## Purpose

Organizar a barra lateral do dashboard por **conector** do tenant, com os módulos ligados em cada um (Fiscal, Contábil,
Inventário) abaixo dele, e o Agendamento junto deles no conector cujo adapter declara que ele agenda. O adapter de cada
conector declara o rótulo que aparece no menu, os módulos que atende e se ele agenda; o Admin liga os contratados no
painel do conector, em Configurações. A integração não é item de menu: ela é coluna e filtro dentro da página do
módulo. O `/info` responde a **árvore** que a barra lateral desenha, e um tenant sem conector recebe a árvore **vazia**
— nesse caso é a tela que mostra um grupo nomeando a ausência, com o Fiscal e o Agendamento. Esconder um módulo é
apresentação, e não permissão; a **visão técnica** (os JSONs crus e os logs) é permissão, conferida no servidor
(`platform-response-trace`).

## Requirements

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

### Requirement: A marcação em Configurações

Em Configurações, o Admin MUST marcar os módulos **dentro do painel de cada conector**, e não numa aba do nível do
cliente. Cada conector mostra os módulos que o adapter dele declara, com uma caixa de marcar cada, marcadas conforme o
perfil gravado.

- **A aba "Módulos" do nível do cliente deixa de existir.** Com dois conectores ela mentiria sobre a quem se aplica: o
  mesmo módulo pode estar ligado num conector e não no outro.
- **Um módulo que o adapter do conector não declara** MUST aparecer apagado, dizendo que aquele conector não o atende —
  e não some da lista. Apagado com motivo responde a pergunta; ausente faz procurar.
- **A gravação** continua pelo mesmo botão de salvar do perfil. Os módulos marcados só valem depois de salvos.

#### Scenario: Marcar e salvar
- **WHEN** o Admin abre o painel do conector `Avalara`, marca o Contábil e salva
- **THEN** o conector passa a ter o Contábil, e a barra lateral o mostra

#### Scenario: Marcar sem salvar
- **WHEN** o Admin marca o Contábil no painel do conector e o fecha sem salvar
- **THEN** o perfil e a barra lateral ficam como estavam

#### Scenario: Módulo que o conector não atende
- **WHEN** o Admin abre o painel de um conector cujo adapter não declara o Inventário
- **THEN** o Inventário aparece apagado, dizendo que aquele conector não o atende
- **AND** a caixa dele não pode ser marcada

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
  respondeu", "respondeu vazio" e "falhou" são **três** estados diferentes, e só o do meio nomeia a ausência.
- **A falha do `/info` MUST NOT chegar a este requisito.** Sem resposta não há painel, e uma barra lateral que não é
  desenhada não afirma nada (ver "Sem saber quem é o usuário e o que o tenant tem, o dashboard não desenha o painel").
  Traduzir falha para árvore vazia faria a tela acusar o cliente de não ter conector por causa de um 5xx.

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

#### Scenario: O /info falhou
- **WHEN** o `/info` de um usuário falha com 5xx, esgotadas as tentativas, sem nenhuma resposta anterior
- **THEN** a barra lateral não é desenhada, e nada afirma que não há conector configurado
- **AND** a tela é a de falha, com a ação de tentar de novo

#### Scenario: O grupo some quando o conector é configurado
- **WHEN** o Admin liga o módulo Fiscal no conector `Avalara` e salva
- **THEN** a barra lateral passa a mostrar o grupo do conector, com o Fiscal
- **AND** o grupo que nomeia a ausência deixa de aparecer

### Requirement: Sem saber quem é o usuário e o que o tenant tem, o dashboard não desenha o painel

O painel só existe depois que **duas** perguntas tiverem resposta: **quem é o usuário** — a checagem da sessão guardada,
no `/auth/me` — e **o que o tenant tem** — o `/info`, que diz o ambiente da instância, os conectores e o que o usuário
logado pode ver. Sem as duas, toda afordância da tela seria chute. Por isso o painel — barra lateral, topbar, selos e
conteúdo — MUST NOT ser desenhado antes de haver resposta, e a **falha de qualquer uma delas** MUST levar à mesma tela
própria, que diz que não foi possível carregar e oferece a ação de tentar de novo.

A regra é uma só, nas duas portas: **"não consegui perguntar" não é uma resposta.** Traduzi-la para uma afirmação é o
defeito que este requisito existe para apagar — no `/info` a afirmação seria "o tenant não tem conector"; na checagem
de sessão, "a sua sessão não vale".

**São três estados, e não dois:**

| Estado | O que a tela mostra |
|---|---|
| ainda não respondeu | a tela de carregando |
| respondeu | o painel, com a árvore que veio — vazia ou não |
| falhou | a tela de falha |

**"Falhou"** é a chamada terminada em erro **sem resposta em mãos**: 5xx, rede e timeout. O **401 não é esse caso** —
ele é a resposta de que a sessão realmente não vale, e continua levando ao login, como hoje; a tela de falha MUST NOT
oferecer "entrar de novo" como conserto.

- **O portão de sessão MUST distinguir "a sua sessão não vale" de "não consegui perguntar".** Um **401** na checagem
  derruba a sessão: o token guardado é apagado e a tela é a de login. **Qualquer outra falha** — 5xx, rede, timeout —
  MUST **manter o token** e levar à tela de falha. Uma indisponibilidade do servidor MUST NOT destruir a sessão do
  usuário: ele não pediu para sair, e o servidor não disse que ele não pode entrar.
- **A tela de falha é uma só para a casca inteira**, qualquer que seja a chamada que não respondeu: a mesma mensagem,
  o mesmo "tentar de novo" e o mesmo "sair". O usuário não precisa saber qual chamada falhou — para ele, o que há é um
  painel que não carregou.
- **As escolhas MUST ser funções puras, com nome e teste automatizado**, e a tela MUST só renderizar o que elas
  disserem. São duas: a da casca, entre os três estados, e a **classificação do erro da checagem de sessão** — dado o
  erro, "anônimo" (apaga o token, vai ao login) ou "falhou" (mantém o token, tela de falha). Nenhuma das duas pode
  viver como condicional dentro do componente: o dashboard não tem stack de teste de componente, e a linha que escolhe
  ficaria sem teste — foi o que aconteceu quatro vezes na change `connector-navigation-and-admin-json` (limites
  anotados nas tarefas 1.6, 4.0, 4.8 e 4.10).
- **O estado do portão de sessão MUST ter nome, e ser um só.** São **quatro** situações — checando, autenticado,
  anônimo e falhou —, e elas MUST NOT ser representadas por booleanos soltos: um "já tentei" só distingue duas, e é
  por caber duas numa que a falha se disfarçava de sessão inválida.
- **A mensagem de falha MUST NOT afirmar nada sobre a configuração do cliente.** Ela MUST NOT dizer que não há conector,
  módulo, agendamento, ambiente ou permissão — nem para negar. Ela fala da **falha de carregar**, e não do que o tenant
  tem. Afirmar ausência a partir de uma falha é exatamente o defeito que este requisito existe para apagar.
- **A ação de tentar de novo MUST refazer a chamada que faltou** — o `/info` **e também** a checagem de sessão.
  Enquanto refaz, o estado MUST ser o de **carregando**, e não o de falha. Falhando de novo, a tela de falha volta.
- **Uma resposta já recebida continua valendo.** A falha de uma **recarga** — a invalidação do `/info` ao salvar em
  Configurações, por exemplo — MUST NOT derrubar o painel: a tela de falha é para quem não tem resposta nenhuma. O
  painel fica velho, e velho não é mentira.
- **Os demais leitores do `/info` não mudam.** O detalhe do documento, o diálogo da nota e Configurações vivem dentro do
  painel: sem painel, eles não chegam a existir. A regra de `platform-response-trace` — nenhum botão da visão técnica
  antes de o `/info` responder, nem se ele falhar — continua valendo como está, agora como defesa em profundidade.
- **Além de tentar de novo, a tela de falha MUST oferecer a saída — um botão de sair próprio —, e MUST NOT desenhar a
  topbar do painel.** Ficar preso numa falha persistente sem poder trocar de conta é armadilha, e sair é **local**:
  apaga o token e o estado de sessão, e por isso funciona justamente com o servidor fora, que é a situação desta tela.
  Manter a topbar, por outro lado, obrigaria a desenhá-la sem título nem subtítulo — não há item aberto — e com o papel
  vindo do token, e não do `/info`: uma terceira forma de casca para manter e testar, por uma saída que um botão
  resolve. (Decisão do Marcelo, 2026-10-15; `design.md` D11.)

#### Scenario: A API fora antes de o dashboard saber quem é o usuário
- **WHEN** um usuário com sessão guardada abre o dashboard e a checagem da sessão falha com 5xx, rede ou timeout
- **THEN** o token guardado continua guardado, e a tela não é a de login
- **AND** a tela é a de falha, com a ação de tentar de novo — a mesma de quando o `/info` falha

#### Scenario: A sessão que realmente não vale
- **WHEN** a checagem da sessão responde 401
- **THEN** o token guardado é apagado e a tela é a de login
- **AND** a tela de falha não aparece

#### Scenario: Tentar de novo refaz a checagem de sessão
- **WHEN** o usuário clica em tentar de novo na tela de falha que veio da checagem de sessão
- **THEN** a checagem é refeita, e enquanto a resposta não chega a tela é a de carregando
- **AND** respondendo, o painel é desenhado sem que o usuário precise entrar de novo

#### Scenario: A classificação do erro da checagem, provada por teste
- **WHEN** o teste da função que classifica o erro da checagem recebe um 401, e depois um 5xx, uma falha de rede e um
  timeout
- **THEN** o 401 devolve "anônimo" e os demais devolvem "falhou"
- **AND** "401" e "5xx" não devolvem o mesmo resultado

#### Scenario: A falha no primeiro carregamento
- **WHEN** um usuário entra no dashboard e o `/info` falha com 5xx, esgotadas as tentativas, sem nenhuma resposta
  anterior
- **THEN** o painel não é desenhado: não há barra lateral, nem selo de ambiente, nem tela de módulo
- **AND** a tela mostra que não foi possível carregar o painel, com a ação de tentar de novo

#### Scenario: A mensagem não afirma configuração
- **WHEN** a tela de falha é mostrada
- **THEN** a mensagem não diz que não há conector configurado, nem nomeia módulo, agendamento, ambiente ou permissão
- **AND** nenhum grupo da barra lateral aparece — em particular, não o que nomeia a ausência de conector

#### Scenario: Tentar de novo enquanto refaz
- **WHEN** o usuário clica em tentar de novo na tela de falha
- **THEN** o `/info` é refeito, e enquanto a resposta não chega a tela é a de carregando, e não a de falha

#### Scenario: Tentar de novo e conseguir
- **WHEN** o `/info` responde depois de o usuário tentar de novo
- **THEN** o painel é desenhado com a árvore que veio, e a tela de entrada é a do primeiro item do primeiro grupo

#### Scenario: Tentar de novo e falhar de novo
- **WHEN** a nova tentativa também falha
- **THEN** a tela volta a ser a de falha, com a mesma ação de tentar de novo

#### Scenario: Sair da tela de falha
- **WHEN** a tela de falha é mostrada
- **THEN** o usuário tem uma ação de sair, que encerra a sessão ali mesmo e leva ao login
- **AND** a topbar do painel não aparece, nem o menu do usuário que vive nela

#### Scenario: Sessão expirada não é a tela de falha
- **WHEN** o `/info` responde 401
- **THEN** o dashboard volta ao login, como hoje
- **AND** a tela de falha não aparece

#### Scenario: A falha de uma recarga, com resposta em mãos
- **WHEN** o Admin salva em Configurações, o `/info` é invalidado e a recarga falha
- **THEN** o painel continua desenhado com a resposta anterior
- **AND** a tela de falha não aparece

#### Scenario: Os três estados, provados por teste
- **WHEN** o teste da função que escolhe o estado recebe, um a um, "ainda não respondeu", "respondeu" e "falhou"
- **THEN** ela devolve carregando, painel e falha, nessa ordem
- **AND** "falhou" e "respondeu vazio" devolvem resultados diferentes
