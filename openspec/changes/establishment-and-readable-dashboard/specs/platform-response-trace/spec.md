## MODIFIED Requirements

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

### Requirement: A resposta no detalhe do documento

O detalhe do documento no dashboard MUST mostrar primeiro o que se lê, e só depois, a pedido, o JSON cru.

**A primeira vista** traz:

- **Nota rejeitada pela plataforma:** o motivo como lista de campos. A lista sai do mapa de erros por campo da foto da
  resposta: da consulta de status, quando foi ela que rejeitou, e do envio nos outros casos. A lista traz:
  - uma entrada por caminho de campo;
  - o caminho legível, com o índice de item contado a partir de 1 (`itens[0].Item.TipoItem` vira item 1, `Item`,
    `TipoItem`);
  - abaixo do caminho, as mensagens da plataforma, como vieram.

  Sem mapa de campos na foto, ou sem a foto, vale o motivo registrado, como texto.
- **Nota aceita com omissões:** a marca "Enviado com ressalvas" e, ao abri-la, a lista das omissões.
- **Nota ignorada ou com outra falha:** o motivo registrado.

**O JSON cru** fica atrás do botão "Visualizar JSON". São as abas de origem, domínio, destino e resposta. O botão MUST
aparecer só para quem pode ver o JSON cru, que hoje é só o Admin. Para os demais papéis, o detalhe mostra só a
primeira vista.

Dentro do JSON cru, as abas seguem como hoje:

- as fotos da resposta (a do envio e a da consulta) ficam numa aba própria, separada das outras três;
- cada foto MUST aparecer só na própria aba. A resposta nunca ocupa o lugar do payload de destino, e a fonte em JSON
  (a do D365) aparece como fonte.

O cabeçalho do detalhe traz a chave da nota, sem a linha "Rastreabilidade: origem → domínio → destino".

Esconder o botão é apresentação, e não autorização. O `/trace` e o zip de download MUST continuar acessíveis a
qualquer usuário do tenant, Viewer incluído (`tenant-boundary`), porque a lista do motivo e o chamado de suporte
dependem deles. O JSON cru não fica protegido do Viewer: restringi-lo de fato é outra mudança.

#### Scenario: O Viewer ainda lê o JSON pela API
- **WHEN** um Viewer do tenant-a pede o `/trace` ou o zip de uma nota do tenant-a
- **THEN** a resposta traz as fotos, como para o Admin

#### Scenario: Recusa lida como lista
- **WHEN** um Viewer abre o detalhe de uma nota recusada no envio com o ProblemDetails gravado do sandbox
- **THEN** o detalhe mostra 6 campos, entre eles "parceiro › Codigo" com as mensagens "'Codigo' não pode ser nulo." e
  "'Codigo' deve ser informado."
- **AND** nenhum JSON aparece, e não há botão "Visualizar JSON"

#### Scenario: Admin abre o JSON
- **WHEN** um Admin abre o mesmo detalhe e clica em "Visualizar JSON"
- **THEN** aparecem as abas de origem, domínio, destino e resposta, com as fotos

#### Scenario: Nota aceita com ressalvas
- **WHEN** o usuário abre o detalhe de uma nota confirmada que foi enviada sem o `IcmsDiff` do item 1
- **THEN** o detalhe mostra a marca "Enviado com ressalvas", e abri-la lista a omissão do `IcmsDiff` do item 1

#### Scenario: Sem mapa de campos
- **WHEN** a foto da resposta de uma nota rejeitada traz `{"mensagens":["codigoEmpresa não cadastrado"]}`
- **THEN** o detalhe mostra o motivo registrado como texto

#### Scenario: Documento enviado e consultado
- **WHEN** um Admin abre o JSON de um documento que tem a fonte, o domínio, o payload de destino e as duas respostas
- **THEN** a aba de destino mostra o payload enviado, e a aba de resposta mostra as duas respostas

#### Scenario: Fonte do D365 em JSON
- **WHEN** um Admin abre o JSON de um documento do D365, cuja fonte é JSON
- **THEN** a fonte aparece na aba de origem, e não na de destino

#### Scenario: Cabeçalho sem a linha de rastreabilidade
- **WHEN** o usuário abre o detalhe de qualquer documento
- **THEN** o cabeçalho mostra a chave da nota, e não a linha "Rastreabilidade: origem → domínio → destino"

## ADDED Requirements

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
