## ADDED Requirements

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

## MODIFIED Requirements

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
