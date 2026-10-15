## MODIFIED Requirements

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
  erro, "anônimo" (apaga o token, vai ao login) ou "falhou" (mantém o token, tela de falha). As duas MUST NOT viver
  como condicional dentro do componente — e a razão **não** é falta de ferramenta: é que uma escolha se prova
  caso a caso, de graça, quando é um valor que entra e um valor que sai, e dentro do componente ela só se prova
  montando a tela. A regra continua valendo depois que o dashboard passou a ter stack de teste de componente: ganhar a
  ferramenta não é motivo para mover a escolha para dentro da tela.
- **A ligação MUST ter teste automatizado também — o componente que chama a função pura.** Testar a escolha e não a
  ligação deixa de fora exatamente onde os dois defeitos de 2026-10-15 moravam: no primeiro, a função que decidia a
  visão técnica estava certa e testada, e quem errava era o cache por trás dela; no segundo, nenhum teste alcançava o
  portão de sessão. Código que satisfaz o bullet anterior por inteiro ainda pode ter os dois. Portanto o teste MUST
  cobrir, além da escolha, **que o componente chama a escolha certa e renderiza o que ela disser** — e MUST **falhar
  quando essa ligação for desligada**. Um teste que só passa com o código certo não prova nada sobre o que ele
  pegaria.
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

#### Scenario: A ligação do portão de sessão, provada por teste
- **WHEN** o teste do portão recebe, um a um, os quatro estados de sessão — checando, falhou, anônimo e autenticado
- **THEN** a tela renderizada é, nessa ordem, a de carregando, a de falha, a de login e o painel
- **AND** desligada a linha que escolhe — o ramo de "falhou" passando a cair no login —, o teste falha

#### Scenario: A ligação da casca, provada por teste
- **WHEN** o teste da casca recebe o `/info` em cada um dos três estados — sem resposta e buscando, respondido, e em
  erro sem resposta nenhuma
- **THEN** a tela é, nessa ordem, a de carregando, o painel e a de falha
- **AND** desligada a linha que passa os sinais da consulta à função que escolhe, o teste falha

#### Scenario: A ligação da classificação do erro, provada por teste
- **WHEN** o teste deixa a checagem de sessão falhar com 401, e depois com 5xx
- **THEN** o 401 apaga o token guardado e leva ao login, e o 5xx mantém o token guardado e leva à tela de falha
- **AND** trocada a classificação por um tratamento genérico que apaga o token em qualquer falha, o teste falha

#### Scenario: A ligação do tentar de novo, provada por teste
- **WHEN** o teste aciona a ação de tentar de novo na tela de falha que veio da checagem de sessão
- **THEN** a checagem é refeita, e a tela deixa de ser a de falha
- **AND** desligado o mecanismo que re-executa a checagem, o teste falha
