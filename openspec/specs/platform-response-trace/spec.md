# platform-response-trace Specification

## Purpose

Guardar com o documento o que a plataforma de compliance respondeu. É a quarta foto, ao lado da fonte, do
domínio e do payload de destino. Com ela, aceite ou rejeição são conferidos na íntegra, e nenhuma
credencial ou token fica exposto.

## Requirements

### Requirement: Resposta do envio fotografada

Toda tentativa de envio que recebe uma resposta HTTP MUST deixar a foto da resposta para o documento e o
destino. Vale para qualquer status: 2xx, 4xx ou 5xx. A foto traz:

- o status HTTP e o momento da resposta;
- o método e a URL da requisição, sem query string. A URL é o que prova para qual ambiente a nota foi;
- as omissões que o hub declarou para essa requisição, isto é, o que o documento tem e o contrato do destino não
  levou. Sem omissão, o campo não aparece;
- os cabeçalhos de resposta da lista permitida;
- o corpo: como JSON quando for JSON, e como texto nos outros casos, sem o ruído do ProblemDetails (ver "O corpo da
  foto sem o ruído do ProblemDetails").

Uma nova tentativa do mesmo documento sobrescreve a foto.

#### Scenario: Aceite
- **WHEN** a plataforma responde 200 ao envio
- **THEN** a foto da resposta do envio traz o status 200 e o corpo da resposta

#### Scenario: Recusa no envio
- **WHEN** a plataforma responde 400 com `{"mensagens":["codigoEmpresa não cadastrado"]}`
- **THEN** a foto traz o status 400 e o corpo
- **AND** o motivo registrado no documento sai do mesmo corpo

#### Scenario: Omissões na foto
- **WHEN** o hub envia uma nota sem o `IcmsDiff` do item 1, por falta de lugar no contrato, e a plataforma responde
  400 ou 200
- **THEN** a foto da resposta do envio traz a omissão do `IcmsDiff` do item 1, ao lado do método e da URL

#### Scenario: Envio sem omissão
- **WHEN** o hub envia uma nota sem nenhuma omissão declarada
- **THEN** a foto da resposta do envio não traz o campo das omissões

#### Scenario: Indisponibilidade seguida de nova tentativa
- **WHEN** a plataforma responde 503, e a nova tentativa recebe 200
- **THEN** a foto da resposta do envio é a do 200

### Requirement: Resposta da consulta de status fotografada

Toda consulta de status cuja resposta tem corpo MUST sobrescrever a foto da consulta, que é sempre a da
última consulta. Uma resposta sem corpo (204, ou o 404 do identificador ainda desconhecido) não sobrescreve.

#### Scenario: Consulta com erro
- **WHEN** a consulta devolve o estado de erro com a mensagem da plataforma
- **THEN** a foto da consulta traz a resposta com essa mensagem

#### Scenario: Consulta sem corpo
- **WHEN** a consulta seguinte devolve 204
- **THEN** a foto da consulta continua a da resposta anterior

### Requirement: A quarta foto na inspeção e no download

As fotos da resposta MUST aparecer junto com as outras três na inspeção do trace e no zip de download do
documento. Cada uma tem um nome próprio no zip.

#### Scenario: Download depois do envio e da consulta
- **WHEN** um documento foi enviado e consultado, e o usuário baixa o zip do documento
- **THEN** o zip traz a fonte, o domínio, o payload de destino, a resposta do envio e a resposta da
  consulta, com nomes distintos

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

- **Quem vê o botão "Visualizar JSON" e o "Baixar arquivos":** MUST ser só quem pode ver as fotos cruas, que hoje é só
  o Admin (ver "As fotos cruas só para quem pode ver o JSON").
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

### Requirement: Nenhum canal expõe credencial ou token

A regra vale para quatro canais: a foto, o motivo registrado, os logs e as mensagens de exceção. Nenhum
deles MUST conter cabeçalho de autorização, token de acesso, segredo ou valor de credencial.

- **Cabeçalhos:** a foto MUST NOT trazer cabeçalho de requisição. Da resposta, só os da lista permitida:
  tipo de conteúdo, data e identificadores de correlação da requisição. `Set-Cookie` e `WWW-Authenticate`
  ficam fora.
- **Corpo:** é redigido antes de gravar e antes de extrair o motivo:
  - propriedade cujo nome designa credencial ou token recebe um marcador no lugar do valor. Os nomes são
    `authorization`, `access_token`, `token`, `refresh_token`, `id_token`, `client_secret`, `secret`,
    `password`, `senha` e `api_key`, comparados sem distinguir maiúscula, `_` ou `-`;
  - toda ocorrência do token ou do segredo em uso, e todo valor precedido de `Bearer`, recebe o marcador
    no texto, seja ele JSON ou não.
- **Logs:** os registros das requisições HTTP MUST NOT mostrar valores de cabeçalho.
- **Endpoint de token:** a troca com ele nunca é fotografada.

#### Scenario: A plataforma ecoa o cabeçalho de autorização
- **WHEN** a plataforma responde 400 com um corpo que repete `Bearer <token em uso>`
- **THEN** a foto, o motivo registrado e os logs trazem o marcador de redação, e não o token

#### Scenario: Token numa propriedade do corpo
- **WHEN** a resposta traz `{"access_token":"abc","mensagens":["x"]}`
- **THEN** a foto traz `access_token` com o marcador de redação, e o motivo contém "x" e não "abc"

#### Scenario: Cabeçalhos sensíveis
- **WHEN** a resposta traz `Set-Cookie` e `WWW-Authenticate`
- **THEN** nenhum dos dois aparece na foto

#### Scenario: Mensagem de erro do endpoint de token
- **WHEN** o endpoint de token recusa a credencial
- **THEN** o motivo registrado e os logs não contêm o segredo nem o corpo do pedido de token

#### Scenario: Respostas reais versionadas
- **WHEN** um teste varre as respostas reais gravadas e versionadas no repositório
- **THEN** nenhuma contém `Bearer` seguido de valor, JWT, `access_token` com valor ou `client_secret` com valor

### Requirement: Gravar a resposta não muda o desfecho

A foto da resposta é gravada depois da requisição. Uma falha ao gravá-la MUST NOT mudar o desfecho
registrado do documento, e MUST NOT provocar um novo envio. A falha é registrada em log, sem o conteúdo. A
foto do payload de destino continua antes do envio, e uma falha nela continua interrompendo o envio, porque
nada foi mandado ainda.

#### Scenario: Falha ao gravar a resposta de um aceite
- **WHEN** a plataforma aceita o envio e a gravação da foto da resposta falha
- **THEN** o documento é registrado como enviado, com o identificador da plataforma
- **AND** a plataforma recebeu um único envio

### Requirement: As fotos cruas só para quem pode ver o JSON

A inspeção do trace (`/trace`) e o download do zip das fotos de um documento MUST exigir um papel que pode ver as fotos
cruas. Hoje, só o Admin pode. A lista desses papéis MUST morar num lugar só no servidor, que é o gancho para um papel de
Suporte, e a tela MUST usar a mesma lista para mostrar os botões.

- **Usuário sem o papel:** recebe 403, com o documento existindo ou não, e o armazenamento das fotos MUST NOT ser lido.
- **Admin do tenant:** o comportamento de hoje, inclusive o 404 igual para o documento de outro tenant e para o sem
  fotos (`tenant-boundary`).

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

### Requirement: A leitura do desfecho para qualquer usuário do tenant

A primeira vista do detalhe MUST vir de uma leitura do desfecho, aberta a qualquer usuário do tenant e escopada a ele. A
leitura traz só o que a primeira vista usa, tirado das fotos da resposta no servidor:

- **a lista de campos da recusa:** uma entrada por caminho, com as mensagens da plataforma, na ordem da resposta. Ela
  sai do mapa de erros por campo da foto da consulta de status, quando essa foto o tem, e senão da foto do envio;
- **as omissões do envio:** as que a foto da resposta do envio registrou.

A leitura MUST NOT trazer mais nada das fotos: nem o payload, nem a fonte, nem o domínio, nem o corpo da resposta.

- **Sem mapa de campos:** a lista vem vazia.
- **Sem omissão:** as omissões vêm vazias.
- **Documento de outro tenant, ou sem fotos:** a mesma resposta de "não encontrado" (ADR-0028).

#### Scenario: Leitura de uma recusa do sandbox
- **WHEN** um Viewer pede a leitura de uma nota recusada no envio com o ProblemDetails gravado do sandbox
- **THEN** a leitura traz 6 campos, o primeiro `operacao` com a mensagem "'Operacao' não pode ser nulo."
- **AND** não traz o `traceId`, a URL, o payload nem o corpo da resposta

#### Scenario: Leitura de uma nota aceita com omissão
- **WHEN** um Viewer pede a leitura de uma nota confirmada que foi enviada sem o `IcmsDiff` do item 1
- **THEN** a leitura traz a lista de campos vazia e a omissão do `IcmsDiff` do item 1

#### Scenario: Recusa na consulta de status
- **WHEN** a nota foi aceita no envio e rejeitada na consulta de status, com o mapa de erros por campo
- **THEN** a lista de campos é a da foto da consulta

#### Scenario: Leitura de nota de outro tenant
- **WHEN** um usuário do tenant-b pede a leitura de uma nota do tenant-a
- **THEN** a resposta é a de "não encontrado", igual à de um documento sem fotos

### Requirement: O corpo da foto sem o ruído do ProblemDetails

Quando o corpo de uma resposta da plataforma é um ProblemDetails com mapa de erros por campo (`errors`), a foto MUST
tirar dele o que não informa nada:

- o `type`, que é o link fixo da RFC;
- o `title`, que é o texto genérico da validação;
- o `status`, quando repete o status HTTP da resposta.

Todo o resto do corpo MUST ficar, e em particular o `errors` e o `traceId`. O `traceId` é como se abre chamado na
plataforma. O método, a URL e o status HTTP continuam no envelope.

O corpo que não tem o mapa `errors` MUST ficar como veio. É o caso da resposta de status, com o status nativo. Aí o
`title` pode ser a única mensagem.

A regra vale para a foto. A resposta que a plataforma devolve não muda, e o mock de compliance MUST devolver a recusa
no formato completo verificado no sandbox (`type`, `title`, `status`, `traceId` e `errors`).

#### Scenario: Recusa do sandbox fotografada
- **WHEN** a plataforma responde 400 com o ProblemDetails gravado do sandbox
- **THEN** o corpo da foto traz o `errors` e o `traceId`
  `00-bb582c93ed4d2544429b0af1cbf7fd05-c04a22f297974e99-00`, e não traz `type`, `title` nem `status`
- **AND** o envelope traz o método `POST`, a URL
  `https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/fiscal/dfe` e o status 400

#### Scenario: Status diferente do HTTP
- **WHEN** o corpo é um ProblemDetails com `errors` e `status` 422, numa resposta HTTP 400
- **THEN** o `status` 422 fica no corpo da foto

#### Scenario: Resposta de status com o status nativo
- **WHEN** a consulta de status responde `{"id": "…", "status": "erro", "mensagens": ["x"]}`
- **THEN** a foto da consulta traz o corpo inteiro, com o `status` `erro`

#### Scenario: ProblemDetails sem mapa de campos
- **WHEN** a plataforma responde 400 com `{"title": "Documento duplicado", "status": 400}`
- **THEN** o corpo da foto fica como veio, com o `title`
