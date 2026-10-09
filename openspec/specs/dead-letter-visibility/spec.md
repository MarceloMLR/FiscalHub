# dead-letter-visibility Specification

## Purpose

Uma nota cuja mensagem falha em todas as entregas vai para a dead-letter da fila, e sumiria de vista. Esta capability
assina a dead-letter de cada fila e transforma cada mensagem esgotada num registro visível no store, com o motivo que
o Service Bus deu. Torna a falha rastreável e não reprocessa (ADR-0010).

## Requirements

### Requirement: A mensagem esgotada vira registro visível

Cada mensagem que chega à dead-letter MUST virar o registro do documento no store, no estado `DeadLettered`, com o
motivo vindo do Service Bus. Se o documento ainda não tinha registro, porque falhou antes de ser registrado, o
registro MUST ser criado.

O motivo MUST sair do `DeadLetterReason`. Sem ele, MUST sair do `DeadLetterErrorDescription`. Sem os dois, MUST ser o
texto padrão "Mensagem movida para dead-letter.".

#### Scenario: Mensagem esgotada com motivo
- **WHEN** uma mensagem chega à dead-letter com o `DeadLetterReason` `MaxDeliveryCountExceeded`
- **THEN** o documento é registrado como `DeadLettered`, com o motivo `MaxDeliveryCountExceeded`

#### Scenario: Mensagem sem motivo
- **WHEN** a mensagem chega sem `DeadLetterReason` e sem `DeadLetterErrorDescription`
- **THEN** o documento é registrado com o motivo "Mensagem movida para dead-letter."

#### Scenario: Documento que nunca foi registrado
- **WHEN** a mensagem de um documento sem registro no store chega à dead-letter
- **THEN** o registro é criado, já como `DeadLettered`

### Requirement: A dead-letter não reprocessa

Esta capability MUST NOT reenviar, reprocessar ou devolver a mensagem para a fila. Ela só torna a falha visível. Depois
de registrada, a mensagem MUST ser concluída na dead-letter.

#### Scenario: Nada volta para a fila
- **WHEN** uma mensagem é consumida da dead-letter e registrada
- **THEN** ela é concluída
- **AND** nenhuma mensagem é publicada de volta, em fila nenhuma

### Requirement: DeadLettered é estado próprio e não bloqueia a reentrada

O documento MUST ficar `DeadLettered`, um estado distinto de `IntegrationError`. Um é falha nossa de processamento; o
outro é recusa da plataforma.

Como `IntegrationError`, o `DeadLettered` MUST NOT bloquear a reentrada da nota. O documento também MUST sair do lote
do poll de status.

#### Scenario: Os dois desfechos ficam distintos
- **WHEN** um documento vai para a dead-letter, e outro é rejeitado pela plataforma
- **THEN** o primeiro fica `DeadLettered`, e o segundo, `IntegrationError`

#### Scenario: Nota da dead-letter entra de novo
- **WHEN** uma nota enviada vai para a dead-letter, e o mesmo conteúdo chega de novo à esteira
- **THEN** a esteira a processa, sem tratá-la como já processada
- **AND** a nota não está mais no lote do poll de status

### Requirement: Uma assinatura por fila

Cada fila do sistema, a da esteira e a da descoberta, MUST ter a sua própria assinatura da dead-letter. Uma assinatura
atende uma fila só.

#### Scenario: As duas filas cobertas
- **WHEN** o host sobe com a fila da esteira e a da descoberta
- **THEN** existe uma assinatura de dead-letter para cada uma

### Requirement: Mensagem sem referência de documento é erro

Uma mensagem de dead-letter cujo corpo não desserializa numa referência de documento MUST fazer o consumo falhar, e
MUST NOT ser engolida em silêncio. Nada é gravado no store, a mensagem MUST NOT ser concluída, e a falha MUST ir para o
log.

#### Scenario: Corpo vazio
- **WHEN** o corpo da mensagem é `null`
- **THEN** o consumo falha com erro dizendo que a mensagem não tem referência de documento

#### Scenario: Corpo irreconhecível
- **WHEN** o corpo da mensagem não é uma referência de documento
- **THEN** o consumo falha, e nada é gravado no store
- **AND** a mensagem não é concluída
