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

O motivo MUST sair do `DeadLetterReason`. Ausente **ou em branco**, MUST sair do `DeadLetterErrorDescription`. Os dois
ausentes ou em branco, MUST ser o texto padrão "Mensagem movida para dead-letter.". Conta como em branco o texto vazio
e o que só tem espaços.

**O motivo de um documento `DeadLettered` nunca MUST ficar vazio.** Neste registro, "foi dito que não há motivo" e
"nunca foi dito" não se distinguem: quem lê tem uma pergunta só — por que a nota parou — e a tela vazia responde igual
nos dois casos. Por isso ausente e em branco recebem o mesmo tratamento, e o texto padrão é o piso.

O piso vale **neste registro**, e não em todo lugar onde o motivo apareça. Onde o leitor distingue os dois casos — o
registro do descarte, cujo leitor reconstrói a mensagem —, eles MUST NOT colapsar, e o texto padrão MUST NOT entrar no
lugar do que o transporte disse.

#### Scenario: Mensagem esgotada com motivo
- **WHEN** uma mensagem chega à dead-letter com o `DeadLetterReason` `MaxDeliveryCountExceeded`
- **THEN** o documento é registrado como `DeadLettered`, com o motivo `MaxDeliveryCountExceeded`

#### Scenario: Mensagem sem motivo
- **WHEN** a mensagem chega sem `DeadLetterReason` e sem `DeadLetterErrorDescription`
- **THEN** o documento é registrado com o motivo "Mensagem movida para dead-letter."

#### Scenario: Motivo em branco
- **WHEN** a mensagem chega com o `DeadLetterReason` vazio, ou só com espaços, e com a `DeadLetterErrorDescription`
  preenchida
- **THEN** o documento é registrado com o texto da `DeadLetterErrorDescription`, e não com o motivo vazio

#### Scenario: Motivo e descrição em branco
- **WHEN** a mensagem chega com o `DeadLetterReason` e a `DeadLetterErrorDescription` vazios ou só com espaços
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

### Requirement: Mensagem inválida é descartada, com registro no log

Uma mensagem da dead-letter cujo corpo não descreve um documento MUST ser **descartada**: concluída na dead-letter,
para parar de ser reentregue, e registrada no log de erro antes disso. Ela MUST NOT virar registro no store.

Uma mensagem é inválida quando o corpo não desserializa numa referência de documento — não é JSON, é `null`, ou falta
um campo obrigatório —, e também quando desserializa mas chega **sem tenant** ou **sem chave natural**, considerando
em branco o que só tem espaços. Nos dois casos nenhuma reentrega a tornaria tratável.

Nada é gravado no store porque não há documento a que atribuir o registro: sem tenant e sem chave natural, qualquer
linha criada seria atribuída a um tenant que pode não ser o dono da mensagem.

O registro no log MUST ser de nível erro e MUST conter, de uma vez: **o corpo da mensagem** como texto, a fila de
origem, o identificador da mensagem, o número de sequência, o instante de enfileiramento e **os dois campos de motivo
do transporte** — o motivo e a descrição do erro. É com isso que a mensagem é reconstruída e republicada à mão. O corpo
MUST ter o tamanho limitado no registro, e quando for cortado o registro MUST dizer o tamanho original.

Os campos de motivo MUST aparecer **como chegaram do transporte**. Quando um deles vier ausente ou em branco, o
registro MUST dizê-lo de um jeito que não possa ser lido como texto vindo do transporte, e MUST NOT escrever ali o
texto padrão do motivo. O leitor deste registro está reconstruindo a mensagem, e para ele "o transporte não informou
motivo" é informação que o texto padrão esconderia — o registro não MUST atribuir ao transporte uma frase que ele não
disse. O texto padrão continua valendo para o registro no store, e só para ele.

O descarte MUST produzir **um** registro por mensagem — e não um por entrega.

#### Scenario: Corpo que não é uma referência de documento
- **WHEN** chega à dead-letter uma mensagem cujo corpo não desserializa numa referência de documento
- **THEN** a mensagem é concluída, e não volta a ser entregue
- **AND** nada é gravado no store
- **AND** sai um registro de erro com o corpo da mensagem, a fila, o identificador, o número de sequência, o instante
  de enfileiramento e os dois campos de motivo do transporte, como chegaram

#### Scenario: O transporte não informou motivo nenhum
- **WHEN** uma mensagem inválida chega com o motivo e a descrição do erro ausentes, vazios ou só com espaços
- **THEN** o registro do descarte diz que o transporte não informou cada um deles
- **AND** não traz ali o texto padrão "Mensagem movida para dead-letter.", que é texto nosso e não do transporte

#### Scenario: Corpo nulo
- **WHEN** o corpo da mensagem é `null`
- **THEN** a mensagem é descartada pelo mesmo caminho: concluída, nada no store, e um registro de erro com o corpo

#### Scenario: Referência sem tenant ou sem chave natural
- **WHEN** o corpo desserializa numa referência cujo tenant, ou cuja chave natural, está vazio ou só com espaços
- **THEN** a mensagem é descartada: concluída, nada no store, e um registro de erro
- **AND** nenhuma linha é criada com tenant ou chave vazios

#### Scenario: O corpo grande é cortado no registro
- **WHEN** o corpo de uma mensagem inválida passa do tamanho que o registro aceita
- **THEN** o registro traz o começo do corpo e diz o tamanho original

#### Scenario: A mensagem descartada não volta
- **WHEN** uma mensagem inválida é descartada
- **THEN** ela não é entregue de novo, e o log não repete o registro dela

### Requirement: Falha no tratamento não conclui a mensagem

Uma falha ao tratar uma mensagem **válida** — o store indisponível, o banco fora, qualquer erro de infraestrutura —
MUST NOT concluir a mensagem. Ela continua na dead-letter e volta a ser entregue: a reentrega é o que conserta uma
falha transitória. A falha MUST ir para o log de erro.

Esta é a distinção que a capability sustenta: **mensagem inválida nunca vai dar certo e é descartada; tratamento que
falhou pode dar certo na próxima entrega e é preservado.** Concluir as duas do mesmo jeito MUST NOT acontecer.

#### Scenario: O store está fora
- **WHEN** o corpo é uma referência válida e a gravação no store falha
- **THEN** a mensagem não é concluída
- **AND** a falha vai para o log de erro

#### Scenario: A próxima entrega conserta
- **WHEN** a gravação falha numa entrega e o store volta antes da seguinte
- **THEN** a entrega seguinte grava o registro e conclui a mensagem

#### Scenario: Inválida e falha não têm o mesmo desfecho
- **WHEN** uma mensagem tem corpo inválido, e outra tem corpo válido cuja gravação falha
- **THEN** a primeira é concluída e descartada, e a segunda não é concluída
