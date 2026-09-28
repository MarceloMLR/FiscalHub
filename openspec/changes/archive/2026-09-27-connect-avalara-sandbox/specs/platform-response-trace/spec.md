## Purpose

Guardar com o documento o que a plataforma de compliance respondeu. É a quarta foto, ao lado da fonte, do
domínio e do payload de destino. Com ela, aceite ou rejeição são conferidos na íntegra, e nenhuma
credencial ou token fica exposto.

## ADDED Requirements

### Requirement: Resposta do envio fotografada

Toda tentativa de envio que recebe uma resposta HTTP MUST deixar a foto da resposta para o documento e o
destino. Vale para qualquer status: 2xx, 4xx ou 5xx. A foto traz:

- o status HTTP e o momento da resposta;
- o método e a URL da requisição, sem query string;
- os cabeçalhos de resposta da lista permitida;
- o corpo: como JSON quando for JSON, e como texto nos outros casos.

Uma nova tentativa do mesmo documento sobrescreve a foto.

#### Scenario: Aceite
- **WHEN** a plataforma responde 200 ao envio
- **THEN** a foto da resposta do envio traz o status 200 e o corpo da resposta

#### Scenario: Recusa no envio
- **WHEN** a plataforma responde 400 com `{"mensagens":["codigoEmpresa não cadastrado"]}`
- **THEN** a foto traz o status 400 e o corpo
- **AND** o motivo registrado no documento sai do mesmo corpo

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

O detalhe do documento no dashboard MUST mostrar as fotos da resposta (a do envio e a da consulta) numa aba
própria, separada das outras três. Cada foto MUST aparecer só na própria aba: a resposta nunca ocupa o lugar
do payload de destino, e a fonte em JSON (a do D365) aparece como fonte.

#### Scenario: Documento enviado e consultado
- **WHEN** o usuário abre o detalhe de um documento que tem a fonte, o domínio, o payload de destino e as duas
  respostas
- **THEN** a aba de destino mostra o payload enviado, e a aba de resposta mostra as duas respostas

#### Scenario: Fonte do D365 em JSON
- **WHEN** o usuário abre o detalhe de um documento do D365, cuja fonte é JSON
- **THEN** a fonte aparece na aba de origem, e não na de destino

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
