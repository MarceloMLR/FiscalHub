# integration-status-poll Specification

## Purpose

Fechar o ciclo assíncrono do despacho: consultar na plataforma o status dos documentos em voo, pelo id externo, e
gravar o desfecho. Quando a plataforma não responde depois de um limite de consultas, o "204 eterno", o documento vira
`Unconfirmed`, um estado próprio, distinto da recusa da plataforma.

## Requirements

### Requirement: A passada consulta os documentos em voo

Uma passada do poll MUST pedir ao store os documentos em voo, em lote, e consultar cada um na plataforma pelo id
externo. Em voo é o documento `Submitted` que tem id externo.

- **A ordem:** o lote MUST vir na ordem de entrada no store, do mais antigo para o mais novo.
- **O tamanho:** o lote MUST ter, por padrão, 50 documentos.
- **Os tenants:** a passada MUST atravessar todos os tenants. Cada consulta usa o tenant do próprio documento.

A passada MUST ser uma operação avulsa, sem timer. Quem a repete num intervalo é o host.

A passada MUST devolver quantos documentos o lote trouxe, inclusive o que falhou na consulta.

#### Scenario: Uma passada com documentos em voo
- **WHEN** há documentos em voo e a passada roda
- **THEN** cada um é consultado na plataforma pelo id externo
- **AND** a passada devolve quantos documentos o lote trouxe

#### Scenario: Nada em voo
- **WHEN** não há documento em voo
- **THEN** a passada não consulta nada e devolve zero

#### Scenario: Documento fora de voo
- **WHEN** um documento está `Confirmed`, ou está `Submitted` sem id externo
- **THEN** a passada não o consulta

### Requirement: O desfecho da consulta vira estado

Confirmado pela plataforma MUST gravar `Confirmed`. Rejeitado MUST gravar `IntegrationError`, com a mensagem da
plataforma como motivo. Os dois MUST gravar a contagem de tentativas, que é a anterior mais um.

#### Scenario: A plataforma confirma
- **WHEN** a consulta devolve confirmado, para um documento com zero tentativas
- **THEN** o documento fica `Confirmed`, com uma tentativa

#### Scenario: A plataforma rejeita
- **WHEN** a consulta devolve rejeitado, com mensagem
- **THEN** o documento fica `IntegrationError`, e a mensagem da plataforma é gravada como motivo

### Requirement: Sem resposta, o limite move para Unconfirmed

Enquanto a plataforma responder "ainda processando", o documento MUST continuar `Submitted`, com a tentativa contada.
Ao atingir o limite de consultas, o documento MUST virar `Unconfirmed`, com o motivo "Sem resposta da plataforma após
o limite de consultas.".

O limite MUST ser, por padrão, de 10 consultas. A contagem MUST recomeçar do zero a cada envio: um reenvio da nota
zera as tentativas.

#### Scenario: Ainda processando, dentro do limite
- **WHEN** a consulta devolve "ainda processando", e a tentativa nova fica abaixo do limite
- **THEN** o documento continua `Submitted`, e a tentativa é contada

#### Scenario: O limite estoura
- **WHEN** a consulta devolve "ainda processando", e a tentativa nova atinge o limite
- **THEN** o documento vira `Unconfirmed`
- **AND** o motivo gravado diz que não houve resposta da plataforma após o limite de consultas

#### Scenario: O reenvio zera a contagem
- **WHEN** um documento em voo tem três tentativas, e a nota é enviada de novo
- **THEN** o documento volta ao lote com zero tentativas

### Requirement: Unconfirmed é estado próprio e não bloqueia a reentrada

`Unconfirmed` MUST ser um estado distinto de `IntegrationError`. É a plataforma que não respondeu, e não a nota que
foi recusada. O motivo gravado MUST dizer isso.

Como `IntegrationError`, o `Unconfirmed` MUST NOT bloquear a reentrada da nota. O mesmo conteúdo, chegando de novo, é
processado, e não tratado como já processado.

#### Scenario: Nota sem retorno entra de novo
- **WHEN** uma nota está `Unconfirmed`, e o mesmo conteúdo chega de novo à esteira
- **THEN** a esteira a processa, sem tratá-la como já processada

### Requirement: Uma falha não derruba o lote

Uma exceção ao consultar UM documento MUST NOT interromper a passada. Os outros documentos do lote seguem sendo
consultados e têm o desfecho gravado. O documento que falhou continua em voo, para a passada seguinte.

O cancelamento é a exceção: um cancelamento pedido MUST interromper a passada.

#### Scenario: Um documento falha na consulta
- **WHEN** a consulta de um documento lança, e os outros do lote respondem
- **THEN** os outros são processados normalmente
- **AND** o documento que falhou continua em voo, para a passada seguinte

#### Scenario: Cancelamento
- **WHEN** a passada é cancelada no meio
- **THEN** ela para, e os documentos não consultados continuam em voo
