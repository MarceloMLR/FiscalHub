# compliance-dispatch-outcome Specification

## Purpose

Registrar o desfecho do envio à plataforma de compliance tão claro quanto a resposta dela. Quem julga o
conteúdo fiscal é a plataforma, e o motivo dela precisa aparecer no registro do documento e no dashboard,
com a mesma clareza que uma rejeição do próprio hub.

## Requirements

### Requirement: Rejeição síncrona da plataforma

Quando a plataforma responde ao envio com HTTP 400 ou 422, o documento MUST ser registrado como rejeitado
(`IntegrationError`), com as regras a seguir:

- **Motivo:** é o texto da plataforma, identificado como vindo dela. Quando houver omissões declaradas,
  elas vêm depois desse texto.
- **Sem retentativa:** a mensagem da fila MUST ser concluída. Retentativa não conserta conteúdo.

As demais respostas sem sucesso seguem o retry nativo do transporte e a dead-letter, como hoje (ADR-0004).
São elas: 5xx, 429, 401, 403, 404 e falha de rede.

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

### Requirement: Rejeição assíncrona com o motivo da plataforma

Quando a consulta de status devolve o estado nativo de erro, o documento MUST passar a rejeitado
(`IntegrationError`). O motivo segue as regras de extração da rejeição síncrona, aplicadas à resposta da
consulta. Se a resposta não trouxer mensagem, o motivo MUST dizer que a plataforma rejeitou sem informar a
causa. O status nativo MUST continuar fora do registro: o que chega é o texto da plataforma, e o status
fica normalizado (ADR-0003).

#### Scenario: Rejeição da Avalara registrada com o motivo dela
- **WHEN** a consulta de status de um documento enviado devolve erro com a mensagem "CFOP 1556
  incompatível com a operação"
- **THEN** o documento é registrado como rejeitado, com motivo que identifica a plataforma e contém "CFOP
  1556 incompatível com a operação"

#### Scenario: Rejeição sem mensagem
- **WHEN** a consulta de status devolve erro sem nenhuma mensagem
- **THEN** o documento é registrado como rejeitado, com motivo que diz que a plataforma não informou a
  causa

### Requirement: Impossibilidade do lado do conector

Quando o conector não consegue montar a requisição, o documento MUST ser registrado como rejeitado antes
de qualquer requisição à plataforma. Isso acontece quando falta configuração do tenant ou quando um campo
exigido pelo contrato não pode ser representado. O motivo MUST identificar o problema como do conector,
seja configuração, seja contrato do destino, e a mensagem da fila MUST ser concluída sem retentativa.

Depois de corrigida a causa, o reprocessamento manual do documento MUST enviá-lo normalmente.

#### Scenario: Tenant sem os códigos da empresa
- **WHEN** a esteira envia uma nota de um tenant cujas settings de saída não têm a tabela de
  estabelecimentos no ambiente ativo
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o
  que falta
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Reprocessar depois de corrigir a configuração
- **WHEN** a configuração que faltava é incluída e o documento rejeitado é reprocessado manualmente
- **THEN** o documento é enviado à plataforma e registrado como enviado

### Requirement: Omissão visível no registro e no dashboard

Quando um documento é enviado com omissões declaradas pelo adapter de saída, o registro MUST guardá-las:

- **Enviado:** o motivo do registro traz as omissões, abertas por "Enviado sem:".
- **Confirmado:** a confirmação da plataforma MUST preservar esse texto.
- **Rejeitado depois, na consulta:** o motivo da plataforma vem primeiro, e as omissões ficam depois dele.

O dashboard MUST mostrar o motivo de um documento que não falhou como aviso, e não como erro. Esse
documento MUST NOT contar entre as falhas.

Um documento enviado sem omissões continua com o motivo vazio.

#### Scenario: Enviado com omissão
- **WHEN** uma nota é enviada sem o diferencial de alíquota do ICMS do item 1 e sem o encargo do item 2
- **THEN** o registro fica como enviado, com motivo que começa por "Enviado sem:" e cita os dois

#### Scenario: Confirmação preserva a omissão
- **WHEN** a plataforma confirma essa nota
- **THEN** o registro fica como confirmado, e o motivo com as omissões continua lá

#### Scenario: Rejeição depois do envio com omissão
- **WHEN** a plataforma rejeita essa nota na consulta de status com uma mensagem
- **THEN** o motivo do registro traz primeiro a mensagem da plataforma, e depois as omissões

#### Scenario: Aviso no dashboard
- **WHEN** o usuário abre no dashboard um documento confirmado que tem omissões no motivo
- **THEN** o motivo aparece como aviso, e não como erro
- **AND** o documento não aparece no filtro de falhas

#### Scenario: Enviado sem omissões
- **WHEN** uma nota é enviada sem nenhuma omissão declarada
- **THEN** o registro fica como enviado, com o motivo vazio
