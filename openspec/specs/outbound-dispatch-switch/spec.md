# outbound-dispatch-switch Specification

## Purpose
TBD - created by archiving change capture-without-dispatch. Update Purpose after archive.

## Requirements

### Requirement: A chave de envio é do tenant e vale para toda a saída

O perfil do tenant MUST guardar, num **único lugar**, uma chave que diz se o hub envia ao conector de saída
ou apenas salva. A chave é **do tenant** e **independente de conector**: ela não é por conector, nem por
módulo, nem por integração. Nenhum segundo campo MUST dizer a mesma coisa.

- **Ligada:** o comportamento de hoje — o hub salva e envia.
- **Desligada:** o hub salva e **não envia**.
- **Padrão:** ligada. Um tenant que já existe, e um perfil que nunca viu esta chave, MUST se comportar como
  se ela estivesse ligada.
- **Quem muda:** só o Admin, pela tela de Configurações, e só para o tenant dele. A chave segue o formulário:
  o estado só muda quando o perfil é salvo.
- **Escopo:** desligar a chave de um tenant MUST NOT afetar a saída de outro.

#### Scenario: O padrão é enviar
- **WHEN** o tenant-a tem perfil de conector e nunca mexeu na chave
- **THEN** o hub envia as notas dele ao conector de saída, como antes desta mudança

#### Scenario: Sem salvar, nada muda
- **WHEN** o Admin desliga a chave e sai da tela sem salvar
- **THEN** o perfil gravado continua como estava, e a esteira continua enviando

#### Scenario: Só o tenant do Admin
- **WHEN** o Admin do tenant-a desliga a chave e salva, e o tenant-c está com a chave ligada
- **THEN** as notas do tenant-c continuam sendo enviadas

#### Scenario: Nenhum segundo lugar guarda o estado
- **WHEN** a leitura do perfil do tenant-a é pedida
- **THEN** exatamente um campo diz se o hub envia, e nenhum campo de conector, de módulo ou de integração
  repete essa informação

### Requirement: A chave chega à tela pelas duas respostas, e por razões diferentes

O estado da chave MUST aparecer em duas respostas:

- **Na leitura do perfil (`GET /connector`), só do Admin:** é por ela que o formulário de Configurações
  desenha a chave com o estado gravado, e é por ela que a gravação a devolve.
- **Nas informações do tenant (`GET /info`), para qualquer papel:** é por ela que a tela do documento sabe
  se o botão de reprocessar está disponível. O botão está numa tela que qualquer papel abre; sem a chave
  aqui, um Viewer não teria como saber por que o botão está desativado.

A gravação do perfil MUST atualizar as duas leituras sem recarregar a página.

Enquanto a resposta de `/info` não chegou, ou quando a leitura dela falhou, a tela MUST NOT afirmar nenhum
dos dois estados: o botão de reprocessar MUST ficar **ausente** — nem habilitado, nem desativado com o
motivo. Só com a resposta em mãos ele aparece, de um jeito ou do outro.

#### Scenario: O Admin lê e grava pelo perfil
- **WHEN** o Admin do tenant-a abre Configurações com a chave desligada no perfil
- **THEN** a chave aparece desligada
- **AND** ao ligá-la e salvar, a leitura do perfil passa a dizer que o envio está ligado

#### Scenario: Qualquer papel lê pelo /info
- **WHEN** o Viewer do tenant-a abre o dashboard, e o tenant está com o envio desligado
- **THEN** o `/info` responde que o envio está desligado

#### Scenario: Salvar atualiza as duas sem recarregar
- **WHEN** o Admin desliga a chave e salva
- **THEN** o `/info` e a leitura do perfil passam a dizer que o envio está desligado, sem recarregar a página

#### Scenario: Enquanto o /info não respondeu
- **WHEN** o dashboard abre e o `/info` ainda não respondeu
- **THEN** a tela do documento não mostra o botão de reprocessar — nem habilitado, nem desativado com o motivo

### Requirement: Com o envio desligado, a nota é salva com o payload de destino e não é enviada

Com a chave desligada, a esteira MUST percorrer o documento até o ponto imediatamente anterior ao envio,
MUST gravar o **payload de destino** — o mesmo artefato que o envio gravaria, o que a plataforma receberia —,
e MUST NOT fazer a requisição de envio.

- **O que é gravado:** o payload de destino, além da foto do domínio que a esteira já grava. Gravar só o
  modelo de domínio NÃO satisfaz este requisito: conferir sobre o modelo interno não responde "o que a
  plataforma vai receber".
- **O que não é gravado:** não há resposta da plataforma, porque não houve requisição de envio; não há
  identificador externo.
- **O desfecho:** a nota fica na situação "Salva", com um motivo que diz que o envio está desligado.
- **A tela do JSON:** o botão "Visualizar JSON" MUST mostrar o payload de destino gravado, como mostra o de
  uma nota enviada.

#### Scenario: A nota para antes do POST, com o JSON gravado
- **WHEN** o tenant-a está com o envio desligado e uma NF-e válida entra na esteira
- **THEN** nenhuma requisição de envio vai à plataforma
- **AND** o payload de destino fica gravado, e o "Visualizar JSON" o mostra
- **AND** a nota fica em "Salva", com motivo que diz que o envio está desligado

#### Scenario: Sem identificador externo e sem resposta
- **WHEN** a nota do cenário acima é aberta no detalhe
- **THEN** ela não tem identificador externo, e não há foto de resposta da plataforma

#### Scenario: A chave desligada vale para as três origens
- **WHEN** o tenant-a está com o envio desligado e uma nota entra pela integração automática, outra pela
  manual e outra pela agendada
- **THEN** nenhuma das três é enviada, e as três ficam em "Salva"

### Requirement: A chave desligada não esconde rejeição

Desligar o envio MUST NOT suprimir nenhum desfecho que a esteira já dá antes do envio. Com a chave
desligada:

- a nota que falha na validação de integração MUST continuar sendo registrada como rejeitada, com o motivo;
- a nota de tipo fora do escopo MUST continuar sendo registrada como ignorada, com o motivo;
- a nota que o contrato do destino não consegue representar, e a que esbarra na configuração do tenant que a
  **preparação** precisa, MUST continuar sendo registrada como rejeitada, com o motivo que nomeia o problema
  (`compliance-dispatch-outcome`). São elas: o perfil ou o conector de saída ausente; o caminho da integração
  de notas ausente ou inativo; a URL base do conector inválida; o segredo gravado em claro nas settings; o
  estabelecimento sem tradução na tabela e sem contribuinte na plataforma; e os códigos da empresa
  incompletos ou duplicados.

Só a nota que **chegaria à requisição de envio** vira "Salva".

**As recusas de autenticação são a exceção, e são quatro:** a conta de plataforma ausente, a credencial
(o Client ID ou a referência do segredo) ausente, o endpoint de token da conta ausente, e o valor do segredo
ausente no cofre. Com a chave desligada, essas quatro MUST NOT ser procuradas: elas se descobrem ao pedir o
token, e pedir token é parte do envio. A nota que esbarraria numa delas fica em "Salva".

**Por que a exceção, e onde essa classe de problema aparece.** As quatro são condições **do tenant, e não da
nota**: credencial incompleta reprova 100% das notas, não metade, então rejeitar nota a nota não acrescenta
informação nenhuma — só ruído com o nome de rejeição. O que esta decisão evita — um mar de notas "Salva" sem
pista de que parte delas não passaria — é recusa de **conteúdo**, e essa continua saindo na preparação,
nota a nota, como as listas acima dizem.

O lugar que responde "a credencial deste tenant presta?" é o **"Testar credencial"** em Configurações →
Conectores, e ele responde no nível certo — o da conta de plataforma, que é onde a credencial mora, e não o
do documento. Ele MUST continuar dizendo "incompleta", **com o mesmo motivo** que a rejeição do envio traria
e sem fazer requisição nenhuma, nas quatro; e "recusada" quando a plataforma nega o token. A chave de envio
MUST NOT afetá-lo: testar credencial não é enviar, pela mesma razão que consultar status não é.

#### Scenario: A credencial incompleta não vira rejeição nota a nota
- **WHEN** o tenant-a está com o envio desligado, a credencial da conta de plataforma está incompleta (sem o
  valor do segredo no cofre, por exemplo), e uma NF-e válida entra na esteira
- **THEN** a nota fica em "Salva", e não é registrada como rejeitada
- **AND** o "Testar credencial" em Configurações diz que a credencial está incompleta, com o motivo que
  nomeia o que falta

#### Scenario: Inválida continua rejeitada
- **WHEN** o tenant-a está com o envio desligado e uma NF-e sem um campo obrigatório entra na esteira
- **THEN** ela é registrada como rejeitada, com o motivo da validação, e não fica em "Salva"

#### Scenario: Fora do escopo continua ignorada
- **WHEN** o tenant-a está com o envio desligado e uma NFS-e entra na esteira
- **THEN** ela é registrada como ignorada, com o motivo do tipo fora do escopo, e não fica em "Salva"

#### Scenario: Contrato do destino continua recusando
- **WHEN** o tenant-a está com o envio desligado e uma nota que o contrato do destino não representa entra
  na esteira
- **THEN** ela é registrada como rejeitada, com a lista de campos, e não fica em "Salva"

### Requirement: "Salva" é situação própria na tela

"Salva" MUST ser uma situação própria do documento, distinta de "Finalizado" e de "Em processamento".

- **Não é "Finalizado":** a nota não foi confirmada por plataforma nenhuma.
- **Não é "Em processamento":** "Em processamento" quer dizer *aguardando retorno do compliance*, e uma nota
  que nunca foi enviada não aguarda nada.
- **Não é falha:** ela MUST NOT contar como erro e MUST ficar fora do filtro de falhas, como a ignorada
  (`discovery-queue-consumer`).
- **O rótulo na tela** é "Salva".

#### Scenario: O rótulo na lista
- **WHEN** o dashboard lista as notas do tenant-a e uma delas parou com o envio desligado
- **THEN** ela aparece com o rótulo "Salva"

#### Scenario: Salva não é falha
- **WHEN** a nota em "Salva" é listada
- **THEN** ela fica fora do filtro de falhas, e não é contada como erro

### Requirement: Com o envio desligado, o reprocesso fica indisponível, e a tela diz por quê

Enquanto o envio está desligado, o reprocesso MUST ficar indisponível para **qualquer** nota — não só as que
pararam em "Salva" — e **independente da origem** da nota (automática, manual ou agendada). A chave é uma
porta única para toda a saída: o clique manual MUST NOT passar por cima dela.

**Indisponível é o botão visível e desativado, e não o botão removido.** Na nota que **tem** reprocesso (ver
o requisito seguinte), o botão MUST continuar visível, MUST ficar desativado, e o clique nele MUST NOT
disparar o reprocesso. Um botão que some deixa o usuário procurando o que não existe; um botão desativado
sem explicação o deixa olhando um botão morto.

**A explicação mora no próprio botão, e lugar nenhum mais.** Ela MUST dizer que o envio ao conector de saída
está desligado para este tenant e MUST dizer o caminho de religá-lo. Ela MUST ser alcançável pelas três
entradas — mouse (apontar), teclado (foco) e clique/toque —, porque um usuário sem mouse também precisa dela,
e MUST estar ligada ao botão como descrição acessível, para o leitor de tela anunciá-la com ele.

**A explicação MUST flutuar por cima da tela, sem mexer no layout de quem a hospeda.** Abrir a explicação MUST
NOT mudar o tamanho, a posição ou a rolagem do que está em volta — o detalhe da nota abre num modal mais baixo
do que ela, e uma explicação que ocupasse espaço no fluxo faria aparecer barra de rolagem só por ser aberta.

**A tela MUST NOT explicar a chave onde não há botão.** Numa nota que nunca teve reprocesso — "Finalizado",
"Ignorado" —, a tela MUST NOT mostrar explicação nenhuma sobre o envio desligado: ali não há nada
indisponível, e dizer que a chave indisponibilizou algo atribui a ela uma ausência que não é dela.

O botão MUST voltar a ficar habilitado, nas mesmas notas de antes, quando o envio for ligado.

Com o envio **desligado**, um pedido de reprocesso que chegue por fora da tela MUST NOT resultar em envio:
a nota reentra na esteira e volta a parar em "Salva".

#### Scenario: Nota com erro, envio desligado
- **WHEN** o tenant-a está com o envio desligado e o usuário abre uma nota rejeitada pela plataforma
- **THEN** o botão de reprocessar está visível e desativado, e o clique nele não reprocessa
- **AND** a explicação está alcançável no próprio botão, e diz que o envio ao conector de saída está
  desligado e como religá-lo

#### Scenario: Nota salva, envio desligado
- **WHEN** o usuário abre uma nota em "Salva", com o envio desligado
- **THEN** o botão de reprocessar está visível e desativado, com a mesma explicação alcançável nele

#### Scenario: A origem não muda a regra
- **WHEN** o envio está desligado e o usuário abre uma nota que veio da integração manual, e depois uma da
  agendada, e depois uma da automática
- **THEN** o botão está visível e desativado nas três

#### Scenario: A explicação alcança o teclado e o toque
- **WHEN** o envio está desligado, o usuário chega ao botão desativado **pelo teclado**, e depois outro
  usuário **toca** nele num aparelho sem mouse
- **THEN** a explicação aparece nos dois casos, como aparece ao apontar com o mouse

#### Scenario: A explicação flutua, e não empurra o que está em volta
- **WHEN** o usuário abre a explicação do botão desativado numa nota aberta num modal mais baixo do que ela
- **THEN** a explicação aparece por cima do que está em volta, e nada em volta muda de tamanho nem ganha barra
  de rolagem por causa dela

#### Scenario: Ligar devolve o botão
- **WHEN** o Admin liga o envio e salva, e o usuário abre uma nota rejeitada pela plataforma
- **THEN** o botão de reprocessar está habilitado, e nenhuma explicação de envio desligado aparece

#### Scenario: A nota que nunca teve reprocesso não ganha explicação
- **WHEN** o envio está desligado e o usuário abre uma nota "Finalizado", e depois uma "Ignorado"
- **THEN** nenhuma das duas mostra botão de reprocessar, e nenhuma das duas mostra explicação sobre o envio
  desligado

#### Scenario: O pedido por fora da tela também não envia
- **WHEN** o envio está desligado e um pedido de reprocesso de uma nota rejeitada chega direto à API
- **THEN** a nota reentra na esteira e para em "Salva", e nenhuma requisição de envio vai à plataforma

### Requirement: A nota em "Salva" é reprocessável, e isso não a torna falha

Com o envio **ligado**, a tela MUST oferecer o reprocesso na nota em "Salva", habilitado, e o clique MUST
pedir o reprocesso dela. O reprocesso manual é o **único** caminho de saída desse estado: ligar a chave não
reenvia o que já está salvo, e a idempotência por conteúdo trata "Salva" como terminal. Sem o botão, a nota
fica presa, e a chave vira uma porta de mão única.

**A elegibilidade do reprocesso MUST ser um predicado próprio, e MUST NOT ser a lista de falhas.** São duas
listas, de propósito, e unificá-las quebra de um lado ou do outro:

- "Salva" na lista de **falhas** a faria entrar no filtro de falhas e na contagem de erro, contra o requisito
  `"Salva" é situação própria na tela` — a nota foi gravada de propósito, não falhou;
- a lista de **falhas** usada como elegibilidade tira o reprocesso da nota em "Salva", que é a única nota que
  depende dele para sair do lugar.

As duas listas MUST ficar lado a lado no código, com o porquê de serem diferentes escrito ali.

O servidor MUST aceitar o reprocesso de uma nota em "Salva" sem guarda de status: quem decide se o envio
acontece é a chave, no pipeline, e não o estado em que a nota estava.

#### Scenario: Salva e reprocessável
- **WHEN** o envio está ligado e o usuário abre uma nota em "Salva"
- **THEN** o botão de reprocessar está visível e habilitado
- **AND** o clique nele pede o reprocesso dessa nota

#### Scenario: Reprocessável sem ser falha
- **WHEN** a nota em "Salva" do cenário acima é listada
- **THEN** ela continua fora do filtro de falhas e fora da contagem de erro, e ainda assim o detalhe dela
  oferece o reprocesso

### Requirement: A chave não para a consulta de status

Desligar o envio MUST NOT parar a consulta de status dos documentos em voo. Uma nota despachada **antes** de
a chave ser desligada continua sendo consultada e MUST chegar ao desfecho dela — confirmada, rejeitada ou
sem retorno (`integration-status-poll`).

**Consultar o status do que já foi enviado não é enviar.** Parar o poll deixaria essa nota em "Em
processamento" para sempre, que é mentira de tela.

Uma nota em "Salva" MUST NOT entrar na consulta de status: ela não tem identificador externo e não há nada
a consultar.

#### Scenario: A nota em voo chega ao desfecho
- **WHEN** uma NF-e do tenant-a foi enviada e está aguardando confirmação, e o Admin desliga o envio
- **THEN** a consulta de status continua consultando essa nota
- **AND** quando a plataforma confirma, ela passa a "Finalizado"

#### Scenario: A nota salva não é consultada
- **WHEN** o tenant-a tem notas em "Salva" e a passada de consulta de status roda
- **THEN** nenhuma delas é consultada

### Requirement: Ligar a chave não reenvia o que está em "Salva"

Ligar o envio MUST NOT disparar, por si, o envio de nenhuma nota que ficou em "Salva". Essas notas esperam o
clique no reprocessar.

Mandar um lote acumulado no instante em que alguém vira uma chave é surpresa, e surpresa com nota fiscal é
pior ainda.

#### Scenario: Religar não manda o lote
- **WHEN** o tenant-a tem 40 notas em "Salva" e o Admin liga o envio e salva
- **THEN** nenhuma requisição de envio vai à plataforma por causa da gravação
- **AND** as 40 notas continuam em "Salva"

#### Scenario: O clique envia uma
- **WHEN** o envio está ligado e o usuário clica em reprocessar numa das notas em "Salva"
- **THEN** só essa nota é reenviada, e as demais continuam em "Salva"

### Requirement: "Salva" é terminal para a idempotência por conteúdo

A idempotência por conteúdo (ADR-0016) MUST tratar "Salva" como estado terminal, ao lado de "enviada" e
"confirmada": a mesma nota, com o mesmo conteúdo cru, reentregue à esteira, MUST ser ignorada. O gatilho
manual continua furando a idempotência, que é como o reprocesso envia uma nota salva.

Sem isso, duas coisas quebram: a nota em "Salva" seria reprocessada a cada passada do coletor dentro da
janela de sobreposição, regravando as fotos sem nada ter mudado; e, ao religar a chave, a primeira passada
com sobreposição a enviaria sozinha, contra o requisito acima.

Um conteúdo **diferente** continua não sendo bloqueado: a nota corrigida no ERP reentra na esteira
normalmente.

#### Scenario: A passada seguinte não regrava
- **WHEN** o tenant-a está com o envio desligado, uma nota ficou em "Salva", e a passada seguinte do coletor
  traz a mesma nota com o conteúdo cru idêntico
- **THEN** a esteira a ignora, e as fotos gravadas não mudam

#### Scenario: Religar não envia pela sobreposição
- **WHEN** uma nota ficou em "Salva", o Admin liga o envio, e a passada seguinte do coletor traz a mesma
  nota com o conteúdo cru idêntico
- **THEN** a esteira a ignora, e nenhuma requisição de envio vai à plataforma

#### Scenario: O reprocesso manual fura
- **WHEN** o envio está ligado e o usuário reprocessa uma nota em "Salva", cujo conteúdo cru não mudou
- **THEN** a nota é enviada à plataforma

#### Scenario: A nota corrigida reentra
- **WHEN** uma nota ficou em "Salva" e o cliente a corrige no ERP, mudando o conteúdo cru
- **THEN** a esteira a processa de novo
