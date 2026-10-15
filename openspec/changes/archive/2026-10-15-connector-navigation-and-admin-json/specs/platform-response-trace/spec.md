## MODIFIED Requirements

### Requirement: As fotos cruas só para quem pode ver o JSON

A inspeção do trace (`/trace`) e o download do zip das fotos de um documento MUST exigir um papel que pode ver as fotos
cruas. Hoje, só o Admin pode.

**A decisão é do servidor, e não da tela** (ADR-0039 §5). A resposta tem credencial do ERP, endereço de plataforma e
payload de documento: esconder o botão não protege nada, porque o endpoint continua respondendo a quem souber a URL.
Portanto:

- **A lista desses papéis MUST morar num lugar só no servidor**, que é o gancho para um papel de Suporte. O dashboard
  MUST NOT ter cópia dela: duas listas que precisam de um comentário para não divergir são uma lista a mais.
- **O `/info` MUST responder, para o usuário logado, se ele pode ver as fotos cruas**, derivado dessa mesma lista. É um
  booleano, e não a lista: o servidor já sabe quem está logado, e responde a pergunta em vez de mandar os dados para a
  tela decidir de novo.
- **A tela MUST usar essa resposta** para mostrar os botões, e MUST NOT mostrá-los antes de o `/info` responder, nem se
  ele falhar — como o selo de ambiente, que não afirma ambiente nenhum antes da resposta. Um botão que promete o que o
  servidor vai recusar com 403 é promessa quebrada.
- **A resposta é do usuário logado, e só dele.** A **troca de sessão** — sair e entrar como outro usuário, sem
  recarregar a página — MUST descartar a resposta guardada, e a tela volta ao estado de antes da resposta até o `/info`
  do usuário novo chegar. Uma resposta guardada não é do usuário: é do token que estava valendo quando ela foi feita.
  Sem isso, o booleano do servidor estaria certo e a tela mostraria o do usuário anterior.
- **A regra de papel MUST ter teste automatizado do endpoint, por papel, e não só por tenant.** O limite de tenant já é
  provado em `tenant-boundary`; o papel nunca foi. O teste prova a frase inteira: o papel sem permissão recebe 403 **e
  o armazenamento das fotos não é lido**.

**Usuário sem o papel:** recebe 403, com o documento existindo ou não, e o armazenamento das fotos MUST NOT ser lido.
**Admin do tenant:** o comportamento de hoje, inclusive o 404 igual para o documento de outro tenant e para o sem fotos
(`tenant-boundary`).

O chamado de suporte continua anexando os zips das notas no servidor, para qualquer papel. O zip vai para o suporte, e
não volta a quem abriu o chamado.

#### Scenario: Viewer pede o trace
- **WHEN** um Viewer do tenant-a pede o `/trace` de uma nota do tenant-a, que tem fotos
- **THEN** a resposta é 403, e as fotos não são lidas

#### Scenario: Viewer pede o zip
- **WHEN** um Viewer do tenant-a pede o download das fotos de uma nota do tenant-a
- **THEN** a resposta é 403

#### Scenario: Admin pede o zip
- **WHEN** um Admin do tenant-a pede o download das fotos de uma nota do tenant-a
- **THEN** a resposta é o zip com as fotos do documento, como hoje

#### Scenario: Viewer abre chamado
- **WHEN** um Viewer abre um chamado de suporte para uma nota
- **THEN** o chamado vai com o zip da nota anexado, como hoje

#### Scenario: O /info diz quem vê
- **WHEN** um Admin e um Viewer do mesmo tenant chamam o `/info`
- **THEN** a resposta do Admin diz que ele pode ver as fotos cruas, e a do Viewer diz que não

#### Scenario: Troca de usuário sem recarregar a página
- **WHEN** um Admin sai do dashboard e um Viewer do mesmo tenant entra, na mesma página, sem recarregá-la
- **THEN** os dois botões não aparecem para o Viewer
- **AND** a tela não decide por nenhuma resposta guardada do usuário anterior: ela espera o `/info` do usuário novo

#### Scenario: Um papel novo na lista do servidor
- **WHEN** um papel é acrescentado à lista do servidor, e um usuário desse papel chama o `/info` e o `/trace`
- **THEN** o `/info` diz que ele pode ver as fotos cruas, e o `/trace` responde o conteúdo
- **AND** nada precisou ser mudado no dashboard para os botões aparecerem

### Requirement: A resposta no detalhe do documento

O detalhe do documento no dashboard MUST mostrar primeiro o que se lê, e só depois, a pedido, o JSON cru.

**A primeira vista** traz:

- **Nota rejeitada pela plataforma:** o motivo como lista de campos, vinda da leitura do desfecho (ver "A leitura do
  desfecho para qualquer usuário do tenant"). A lista traz:
  - uma entrada por caminho de campo;
  - o caminho legível, com o índice de item contado a partir de 1 (`itens[0].Item.TipoItem` vira item 1, `Item`,
    `TipoItem`);
  - abaixo do caminho, as mensagens da plataforma, como vieram.

  Sem lista na leitura, vale o motivo registrado, como texto.
- **Nota aceita com omissões:** a marca "Enviado com ressalvas" e, ao abri-la, a lista das omissões, também vinda da
  leitura do desfecho.
- **Nota ignorada ou com outra falha:** o motivo registrado.

**O JSON cru** abre num modal próprio, por cima do detalhe, pelo botão "Visualizar JSON". O modal traz as abas de
origem, domínio, destino e resposta, e a aba escolhida mostra aquela foto. Fechar o modal do JSON MUST voltar ao
detalhe, sem fechar o detalhe. O Esc fecha só o modal de cima.

- **Quem vê o botão "Visualizar JSON" e o "Baixar arquivos":** MUST ser quem o **`/info` disser** que pode ver as fotos
  cruas (ver "As fotos cruas só para quem pode ver o JSON"). A tela MUST NOT decidir por conta própria, a partir do
  papel do usuário, e MUST NOT guardar a lista de papéis.
- **Antes de o `/info` responder, ou se ele falhar:** nenhum dos dois botões aparece, para papel nenhum.
- **Os demais papéis:** o detalhe mostra só a primeira vista. O "Abrir chamado" continua para todos.

Dentro do modal do JSON, as abas seguem como hoje:

- as fotos da resposta (a do envio e a da consulta) ficam numa aba própria, separada das outras três;
- cada foto MUST aparecer só na própria aba. A resposta nunca ocupa o lugar do payload de destino, e a fonte em JSON
  (a do D365) aparece como fonte.

O cabeçalho do detalhe traz a chave da nota, sem a linha "Rastreabilidade: origem → domínio → destino".

#### Scenario: Recusa lida como lista
- **WHEN** um Viewer abre o detalhe de uma nota recusada no envio com o ProblemDetails gravado do sandbox
- **THEN** o detalhe mostra 6 campos, entre eles "parceiro › Codigo" com as mensagens "'Codigo' não pode ser nulo." e
  "'Codigo' deve ser informado."
- **AND** nenhum JSON aparece, e não há botão "Visualizar JSON" nem "Baixar arquivos"

#### Scenario: Admin abre o JSON
- **WHEN** um Admin abre o mesmo detalhe e clica em "Visualizar JSON"
- **THEN** abre um modal próprio por cima do detalhe, com as abas de origem, domínio, destino e resposta
- **AND** trocar de aba mostra a foto daquela aba
- **AND** fechar o modal do JSON, ou apertar Esc, volta ao detalhe, que continua aberto

#### Scenario: O detalhe antes do /info
- **WHEN** um Admin abre o detalhe de uma nota e o `/info` ainda não respondeu, ou falhou
- **THEN** nem o "Visualizar JSON" nem o "Baixar arquivos" aparecem
- **AND** eles aparecem assim que o `/info` responder que ele pode ver as fotos cruas

#### Scenario: Nota aceita com ressalvas
- **WHEN** o usuário abre o detalhe de uma nota confirmada que foi enviada sem o `IcmsDiff` do item 1
- **THEN** o detalhe mostra a marca "Enviado com ressalvas", e abri-la lista a omissão do `IcmsDiff` do item 1

#### Scenario: Sem mapa de campos
- **WHEN** a foto da resposta de uma nota rejeitada traz `{"mensagens":["codigoEmpresa não cadastrado"]}`
- **THEN** a leitura do desfecho não traz lista, e o detalhe mostra o motivo registrado como texto

#### Scenario: Documento enviado e consultado
- **WHEN** um Admin abre o JSON de um documento que tem a fonte, o domínio, o payload de destino e as duas respostas
- **THEN** a aba de destino mostra o payload enviado, e a aba de resposta mostra as duas respostas

#### Scenario: Fonte do D365 em JSON
- **WHEN** um Admin abre o JSON de um documento do D365, cuja fonte é JSON
- **THEN** a fonte aparece na aba de origem, e não na de destino

#### Scenario: Cabeçalho sem a linha de rastreabilidade
- **WHEN** o usuário abre o detalhe de qualquer documento
- **THEN** o cabeçalho mostra a chave da nota, e não a linha "Rastreabilidade: origem → domínio → destino"
