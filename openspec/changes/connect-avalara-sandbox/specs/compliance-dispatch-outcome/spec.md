## MODIFIED Requirements

### Requirement: Rejeição síncrona da plataforma

Quando a plataforma responde ao envio com HTTP 400 ou 422, o documento MUST ser registrado como rejeitado
(`IntegrationError`), com as regras a seguir:

- **Motivo:** é o texto da plataforma, identificado como vindo dela e extraído do corpo já redigido
  (`platform-response-trace`). Quando houver omissões declaradas, elas vêm depois desse texto.
- **Sem retentativa:** a mensagem da fila MUST ser concluída. Retentativa não conserta conteúdo.

As respostas de autenticação no envio têm regra própria:

- **403:** o documento MUST ser registrado como impossibilidade do lado do conector (a credencial não tem
  acesso), sem retentativa. O motivo cita o tenant, o ambiente e o texto da plataforma.
- **401 com token recém-emitido:** o mesmo desfecho do 403. O token acabou de ser emitido e mesmo assim foi
  recusado, e outra tentativa não muda isso.
- **401 com token do cache:** o token é invalidado (`avalara-tenant-authentication`), e a mensagem segue o
  retry nativo.

O **404 no envio** também tem regra própria: o caminho de envio não existe na URL montada, e o documento MUST ser
registrado como impossibilidade do lado do conector, sem retentativa (ver "Impossibilidade do lado do conector"). O
**404 na consulta de status** não muda: é o documento ainda não indexado logo depois do envio, e segue pendente.

As demais respostas sem sucesso seguem o retry nativo do transporte e a dead-letter, como hoje (ADR-0004).
São elas: 5xx, 429 e falha de rede.

Regras para extrair o motivo:

- **Mensagens reconhecidas:** vale o texto das mensagens de erro do corpo, nos formatos comuns (lista de
  mensagens, `ProblemDetails`).
- **Sem formato reconhecido:** vale o corpo como texto.
- **Corpo vazio:** o motivo cita o status HTTP.
- **Tamanho:** o motivo tem tamanho máximo, e o texto é cortado nesse limite.

#### Scenario: Plataforma recusa no envio
- **WHEN** a plataforma responde ao envio com HTTP 400 e o corpo `{"mensagens":["codigoEmpresa não cadastrado"]}`
- **THEN** o documento é registrado como rejeitado, com motivo que identifica a plataforma e contém
  "codigoEmpresa não cadastrado"
- **AND** foi feita uma única requisição de envio, e a mensagem não volta para a fila

#### Scenario: Indisponibilidade da plataforma
- **WHEN** a plataforma responde ao envio com HTTP 503
- **THEN** o envio falha, e a mensagem segue o retry nativo, como hoje

#### Scenario: Corpo sem formato reconhecido
- **WHEN** a plataforma responde com HTTP 400 e o corpo em texto puro `Documento inválido`
- **THEN** o motivo contém `Documento inválido`

#### Scenario: Corpo vazio
- **WHEN** a plataforma responde com HTTP 422 sem corpo
- **THEN** o motivo cita o status 422

#### Scenario: Credencial sem acesso
- **WHEN** a plataforma responde ao envio com HTTP 403 e o corpo `{"message":"cliente sem acesso à empresa"}`
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que cita o
  tenant, o ambiente e "cliente sem acesso à empresa"
- **AND** foi feita uma única requisição de envio, e a mensagem não volta para a fila

#### Scenario: Token recém-emitido recusado
- **WHEN** o token acabou de ser obtido do endpoint de token e a plataforma responde 401 ao envio
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector
- **AND** a mensagem não volta para a fila

#### Scenario: Caminho de envio inexistente
- **WHEN** a plataforma responde 404 ao envio
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que diz que o caminho de
  envio não existe nessa URL e aponta as duas partes dela: a URL base do ambiente, no perfil do tenant (tela de
  conectores), e o caminho de envio, na configuração do host (`Avalara:DocumentsPath`)
- **AND** foi feita uma única requisição de envio, e a mensagem não volta para a fila

#### Scenario: Documento ainda não indexado na consulta
- **WHEN** a consulta de status responde 404 logo depois do envio
- **THEN** o documento segue pendente, e a consulta se repete na próxima passada

#### Scenario: Token do cache recusado
- **WHEN** o token veio do cache e a plataforma responde 401 ao envio
- **THEN** o envio falha e segue o retry nativo
- **AND** a tentativa seguinte usa um token novo

### Requirement: Impossibilidade do lado do conector

Quando o conector não consegue fazer a requisição do documento, o documento MUST ser registrado como
rejeitado. Isso acontece antes de qualquer requisição à plataforma nos casos a seguir:

- falta configuração do tenant, incluindo a credencial, a referência do segredo, o segredo resolvido e a
  URL do ambiente;
- um campo exigido pelo contrato não pode ser representado;
- a URL do ambiente não é aceita;
- o endpoint de token recusa a credencial.

Há ainda dois casos depois da requisição, como na rejeição síncrona:

- a plataforma nega a credencial (403, ou 401 com token recém-emitido);
- o caminho de envio não existe na URL montada (404 no envio). O motivo aponta a URL base do perfil e o
  `Avalara:DocumentsPath` do host, que são as duas partes da URL.

O motivo MUST identificar o problema como do conector (configuração ou contrato do destino). A mensagem da
fila MUST ser concluída sem retentativa.

Depois de corrigida a causa, o reprocessamento manual do documento MUST enviá-lo normalmente.

#### Scenario: Tenant sem os códigos da empresa
- **WHEN** a esteira envia uma nota de um tenant cujas settings de saída não têm a tabela de
  estabelecimentos no ambiente ativo
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o
  que falta
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Tenant sem o segredo
- **WHEN** a esteira envia uma nota de um tenant cujo segredo não está configurado, ou cujo cofre não tem o
  valor
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o campo
  e aponta a tela de conectores onde configurá-lo
- **AND** nenhuma requisição é feita ao endpoint de token nem à plataforma

#### Scenario: Reprocessar depois de corrigir a configuração
- **WHEN** a configuração que faltava é incluída e o documento rejeitado é reprocessado manualmente
- **THEN** o documento é enviado à plataforma e registrado como enviado

## ADDED Requirements

### Requirement: Aceite sem identificador não é reenviado

Quando o envio recebe 2xx sem identificador reconhecível, o documento MUST ser registrado como rejeitado,
sem retentativa. Isso vale para corpo vazio, corpo que não é JSON e JSON sem o campo do identificador. O
motivo MUST dizer três coisas:

- que a plataforma respondeu com sucesso sem identificador;
- que o documento pode ter sido aceito;
- que ele não será reenviado automaticamente.

A resposta inteira fica na foto da resposta do envio (`platform-response-trace`). Retentar mandaria de novo
à plataforma um documento talvez já aceito.

#### Scenario: Sucesso com corpo sem identificador
- **WHEN** a plataforma responde 201 ao envio com `{"protocolo":"123"}` e sem o campo do identificador
- **THEN** o documento é registrado como rejeitado, com motivo que diz que a plataforma respondeu com
  sucesso sem identificador e que o documento não será reenviado
- **AND** foi feita uma única requisição de envio, e a mensagem não volta para a fila
- **AND** a foto da resposta do envio traz `{"protocolo":"123"}`
