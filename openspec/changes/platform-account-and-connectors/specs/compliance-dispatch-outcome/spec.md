## ADDED Requirements

### Requirement: O envio e a consulta de status usam o caminho da integração

O envio de um documento e a consulta de status dele MUST montar a URL com a base da versão que a integração usa, no
conector do próprio adapter, e o **caminho da integração** (`platform-account-connectors`):

- **o envio:** a base da versão, mais o caminho da integração;
- **a consulta de status:** a base da versão, mais o caminho da integração, o identificador que a plataforma devolveu e
  `/status`.

A configuração do host (`Avalara:DocumentsPath`) MUST NOT entrar na URL do envio nem na da consulta. Ela só preenche, uma
vez, o caminho que a migração deixou à espera (`platform-account-connectors`).

#### Scenario: O caminho do envio vem da integração, e não do host
- **WHEN** a integração de notas fiscais do tenant-a tem o caminho `taxcompliance/v2/fiscal/dfe`, e o host tem
  `Avalara:DocumentsPath` = `documents`
- **THEN** o envio vai à base da versão da integração mais `taxcompliance/v2/fiscal/dfe`
- **AND** nenhuma requisição vai a um caminho `documents`

#### Scenario: A consulta de status segue o caminho da integração
- **WHEN** a nota foi enviada pela integração de caminho `taxcompliance/v2/fiscal/dfe` e recebeu o identificador `abc`
- **THEN** a consulta de status vai à base da versão da integração mais `taxcompliance/v2/fiscal/dfe/abc/status`

## MODIFIED Requirements

### Requirement: Rejeição síncrona da plataforma

Quando a plataforma responde ao envio com HTTP 400 ou 422, o documento MUST ser registrado como rejeitado
(`IntegrationError`), com as regras a seguir:

- **Motivo:** é um resumo curto do texto da plataforma, identificado como vindo dela e extraído do corpo já redigido
  (`platform-response-trace`). A lista completa, campo a campo, fica na foto da resposta, e é de lá que o detalhe do
  documento a mostra.
- **Omissões:** as omissões declaradas pelo hub MUST NOT entrar no motivo da rejeição. Elas ficam na foto da resposta
  do envio (ver "Omissão visível no registro e no dashboard").
- **Sem retentativa:** a mensagem da fila MUST ser concluída. Retentativa não conserta conteúdo.

As respostas de autenticação no envio têm regra própria:

- **403:** o documento MUST ser registrado como impossibilidade do lado do conector (a credencial não tem
  acesso), sem retentativa. O motivo cita o tenant, o ambiente da instância e o texto da plataforma.
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

- **Mapa de erros por campo** (o `errors` do ProblemDetails): o motivo diz quantos campos a plataforma recusou e
  nomeia os três primeiros, na ordem da resposta. Os demais aparecem como "e mais N". O `title` do ProblemDetails
  MUST NOT entrar no motivo.
- **Outras mensagens reconhecidas:** vale o texto das mensagens de erro do corpo, nos formatos comuns (lista de
  mensagens, `ProblemDetails` sem mapa de campos).
- **Sem formato reconhecido:** vale o corpo como texto.
- **Corpo vazio:** o motivo cita o status HTTP.
- **Tamanho:** o motivo tem tamanho máximo, e o texto é cortado nesse limite.

#### Scenario: Plataforma recusa no envio
- **WHEN** a plataforma responde ao envio com HTTP 400 e o corpo `{"mensagens":["codigoEmpresa não cadastrado"]}`
- **THEN** o documento é registrado como rejeitado, com motivo que identifica a plataforma e contém
  "codigoEmpresa não cadastrado"
- **AND** foi feita uma única requisição de envio, e a mensagem não volta para a fila

#### Scenario: Recusa por campo vira resumo
- **WHEN** a plataforma responde ao envio com HTTP 400 e o ProblemDetails gravado do sandbox, com `errors` para
  `operacao`, `tipoPagamento`, `parceiro.Codigo`, `itens[0].Item.TipoItem`, `itens[0].UnidadeMedida.Descricao` e
  `itens[0].Item.UnidadeMedida.Descricao`
- **THEN** o motivo identifica a plataforma e diz que ela recusou 6 campos: `operacao`, `tipoPagamento`,
  `parceiro.Codigo` e mais 3
- **AND** o motivo não contém "One or more validation errors occurred."

#### Scenario: Muitos itens não cortam a lista
- **WHEN** a plataforma recusa uma nota de três itens com 12 campos no mapa de erros
- **THEN** o motivo diz 12 campos e nomeia três, e cabe no tamanho máximo
- **AND** a foto da resposta do envio traz os 12 campos, com todas as mensagens

#### Scenario: Recusa de nota com omissão
- **WHEN** uma nota com o `IcmsDiff` do item 1 sem lugar no contrato é recusada pela plataforma no envio
- **THEN** o motivo registrado não contém "Enviado sem"
- **AND** a foto da resposta do envio traz a omissão do `IcmsDiff` do item 1

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
  tenant, o ambiente da instância e "cliente sem acesso à empresa"
- **AND** foi feita uma única requisição de envio, e a mensagem não volta para a fila

#### Scenario: Token recém-emitido recusado
- **WHEN** o token acabou de ser obtido do endpoint de token e a plataforma responde 401 ao envio
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector
- **AND** a mensagem não volta para a fila

#### Scenario: Caminho de envio inexistente
- **WHEN** a plataforma responde 404 ao envio
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que diz que o caminho de
  envio não existe nessa URL e aponta as duas partes dela, as duas na tela de conectores: a URL base da versão que a
  integração usa, no conector, e o caminho da integração
- **AND** o motivo não cita `Avalara:DocumentsPath`
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
rejeitado.

Antes de qualquer requisição à plataforma, nos casos a seguir:

- falta configuração do tenant: na conta de plataforma, a credencial, a referência do segredo, o segredo resolvido ou
  o endpoint de token; no perfil, o conector do adapter que despacha, a integração que a operação precisa, o caminho
  dela (a integração inativa) ou a URL base da versão que ela usa; ou a entrada da tabela de estabelecimentos do
  conector tem um código faltando (`platform-account-connectors`);
- um campo exigido pelo contrato não pode ser representado;
- uma URL da conta ou do conector não é aceita;
- o endpoint de token recusa a credencial.

Depois da listagem de estabelecimentos da plataforma, e antes do envio (`platform-establishment-resolution`):

- o estabelecimento próprio não tem tradução: não tem entrada na tabela, e não tem contribuinte na plataforma, ou o destino
  não sabe listar;
- o CNPJ do estabelecimento próprio casa com mais de um contribuinte na plataforma;
- o único contribuinte que casa não tem um dos códigos;
- a listagem é recusada: a credencial negada, o caminho que não existe, o formato fora do verificado, a paginação que
  não avança ou o teto de páginas (`avalara-establishment-listing`).

Depois da requisição de envio, como na rejeição síncrona:

- a plataforma nega a credencial (403, ou 401 com token recém-emitido);
- o caminho de envio não existe na URL montada (404 no envio). O motivo aponta as duas partes da URL, as duas na tela
  de conectores: a URL base da versão que a integração usa, no conector, e o caminho da integração.

O motivo MUST identificar o problema como do conector (configuração ou contrato do destino). A mensagem da
fila MUST ser concluída sem retentativa.

Depois de corrigida a causa, o reprocessamento manual do documento MUST enviá-lo normalmente.

#### Scenario: Tenant sem os códigos da empresa
- **WHEN** a esteira envia uma nota de um tenant cujo destino não sabe listar, e cujo conector não tem a tabela de
  estabelecimentos
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o
  que falta
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Integração sem caminho
- **WHEN** a esteira envia uma nota de um tenant cuja integração de notas fiscais está ligada e com o caminho em branco
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia a integração e
  aponta a tela de conectores
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Estabelecimento sem contribuinte na plataforma
- **WHEN** a esteira envia uma nota cujo estabelecimento próprio não está na tabela, e a listagem completa da plataforma não
  tem contribuinte com esse CNPJ
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que cita o CNPJ
- **AND** nenhuma requisição de envio é feita
- **AND** a mensagem da fila é concluída sem retentativa

#### Scenario: Estabelecimento com mais de um contribuinte
- **WHEN** a esteira envia uma nota cujo estabelecimento próprio não está na tabela, e a plataforma tem dois contribuintes
  com esse CNPJ
- **THEN** o documento é registrado como rejeitado, com motivo que nomeia os dois candidatos
- **AND** nenhuma requisição de envio é feita

#### Scenario: Tenant sem o segredo
- **WHEN** a esteira envia uma nota de um tenant cujo segredo não está configurado, ou cujo cofre não tem o
  valor
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o campo
  e aponta a tela de conectores onde configurá-lo
- **AND** nenhuma requisição é feita ao endpoint de token nem à plataforma

#### Scenario: Reprocessar depois de corrigir a configuração
- **WHEN** a configuração que faltava é incluída e o documento rejeitado é reprocessado manualmente
- **THEN** o documento é enviado à plataforma e registrado como enviado

#### Scenario: Reprocessar depois de cadastrar o contribuinte
- **WHEN** o documento foi rejeitado porque o CNPJ não tinha contribuinte, o contribuinte é cadastrado na plataforma, o
  perfil do tenant é salvo e o documento é reprocessado manualmente
- **THEN** o documento é enviado com os códigos do contribuinte novo e registrado como enviado
