# compliance-dispatch-outcome Specification

## Purpose

Registrar o desfecho do envio à plataforma de compliance tão claro quanto a resposta dela. Quem julga o
conteúdo fiscal é a plataforma, e o motivo dela precisa aparecer no registro do documento e no dashboard,
com a mesma clareza que uma rejeição do próprio hub.

## Requirements

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

### Requirement: Rejeição assíncrona com o motivo da plataforma

Quando a consulta de status devolve o estado nativo de erro, o documento MUST passar a rejeitado
(`IntegrationError`). O motivo segue as regras de extração da rejeição síncrona, aplicadas à resposta da
consulta. Se a resposta não trouxer mensagem, o motivo MUST dizer que a plataforma rejeitou sem informar a
causa. O status nativo MUST continuar fora do registro: o que chega é o texto da plataforma, e o status
fica normalizado (ADR-0003).

As omissões declaradas no envio MUST NOT entrar no motivo da rejeição. Elas continuam na foto da resposta do envio.

#### Scenario: Rejeição da Avalara registrada com o motivo dela
- **WHEN** a consulta de status de um documento enviado devolve erro com a mensagem "CFOP 1556
  incompatível com a operação"
- **THEN** o documento é registrado como rejeitado, com motivo que identifica a plataforma e contém "CFOP
  1556 incompatível com a operação"

#### Scenario: Rejeição sem mensagem
- **WHEN** a consulta de status devolve erro sem nenhuma mensagem
- **THEN** o documento é registrado como rejeitado, com motivo que diz que a plataforma não informou a
  causa

#### Scenario: Rejeição de nota enviada com omissão
- **WHEN** uma nota enviada com a observação "Enviado sem: item 1: IcmsDiff não enviado (sem lugar no contrato)" é
  rejeitada na consulta de status com a mensagem "CFOP 1556 incompatível com a operação"
- **THEN** o motivo registrado é só o da plataforma, sem "Enviado sem"
- **AND** a foto da resposta do envio continua trazendo a omissão

### Requirement: Impossibilidade do lado do conector

Quando o conector não consegue fazer a requisição do documento, o documento MUST ser registrado como
rejeitado.

Antes de qualquer requisição à plataforma, nos casos a seguir:

- falta configuração do tenant, incluindo a credencial, a referência do segredo, o segredo resolvido e a
  URL do ambiente, ou a entrada da tabela de estabelecimentos tem um código faltando;
- um campo exigido pelo contrato não pode ser representado;
- a URL do ambiente não é aceita;
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
- o caminho de envio não existe na URL montada (404 no envio). O motivo aponta a URL base do perfil e o
  `Avalara:DocumentsPath` do host, que são as duas partes da URL.

O motivo MUST identificar o problema como do conector (configuração ou contrato do destino). A mensagem da
fila MUST ser concluída sem retentativa.

Depois de corrigida a causa, o reprocessamento manual do documento MUST enviá-lo normalmente.

#### Scenario: Tenant sem os códigos da empresa
- **WHEN** a esteira envia uma nota de um tenant cujo destino não sabe listar, e cujas settings de saída não têm a tabela
  de estabelecimentos no ambiente ativo
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o
  que falta
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

### Requirement: Omissão visível no registro e no dashboard

Quando um documento é enviado com omissões declaradas pelo adapter de saída, as omissões MUST ficar gravadas na foto
da resposta do envio (`platform-response-trace`), em qualquer desfecho. No registro do documento, a omissão é ressalva
de nota aceita, e não parte de um motivo de falha:

- **Enviado:** o motivo do registro traz as omissões, abertas por "Enviado sem:".
- **Confirmado:** a confirmação da plataforma MUST preservar esse texto.
- **Rejeitado no envio, rejeitado depois na consulta, ou sem retorno:** o motivo é só o da falha. A omissão MUST NOT
  entrar nele e continua na foto.

No dashboard, uma nota aceita (enviada ou confirmada) com omissões MUST mostrar a marca discreta "Enviado com
ressalvas". A marca abre o detalhe, que lista as omissões. Essa nota MUST NOT contar entre as falhas, e a ressalva
MUST NOT aparecer como erro.

Um documento enviado sem omissões continua com o motivo vazio e sem a marca.

#### Scenario: Enviado com omissão
- **WHEN** uma nota é enviada sem o diferencial de alíquota do ICMS do item 1 e sem o encargo do item 2
- **THEN** o registro fica como enviado, com motivo que começa por "Enviado sem:" e cita os dois
- **AND** a foto da resposta do envio traz as duas omissões

#### Scenario: Confirmação preserva a omissão
- **WHEN** a plataforma confirma essa nota
- **THEN** o registro fica como confirmado, e o motivo com as omissões continua lá

#### Scenario: Rejeição depois do envio com omissão
- **WHEN** a plataforma rejeita essa nota na consulta de status com uma mensagem
- **THEN** o motivo do registro traz só a mensagem da plataforma, sem as omissões
- **AND** a foto da resposta do envio continua trazendo as omissões

#### Scenario: Sem retorno depois do envio com omissão
- **WHEN** essa nota esgota as consultas de status sem desfecho
- **THEN** o motivo do registro diz só que não houve retorno, sem as omissões

#### Scenario: Aviso no dashboard
- **WHEN** o usuário abre no dashboard um documento confirmado que tem omissões
- **THEN** aparece a marca "Enviado com ressalvas", e não um banner de erro
- **AND** abrir a marca mostra a lista das omissões
- **AND** o documento não aparece no filtro de falhas

#### Scenario: Enviado sem omissões
- **WHEN** uma nota é enviada sem nenhuma omissão declarada
- **THEN** o registro fica como enviado, com o motivo vazio e sem a marca

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
