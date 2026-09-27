## Purpose

Consumir a fila de descoberta, onde o feed de mudanças publica uma referência por documento que mudou na
origem, e levar cada referência à esteira existente ou a um desfecho explícito de "ignorado". É um
segundo gatilho sobre a mesma esteira, sem salto nem armazenamento intermediário.

## ADDED Requirements

### Requirement: Consumo da fila de descoberta

O sistema MUST consumir a fila `documents-discovered`. Cada mensagem traz uma referência de documento
(claim-check). O contexto de envio MUST levar o tenant, a chave natural, o identificador de correlação da
mensagem (ou um novo, se ela não tiver) e a operação "emitida".

- **Sucesso:** processar a referência até um desfecho conclui a mensagem. Os desfechos são enviado,
  rejeitado, ignorado ou já processado.
- **Falha:** a exceção devolve a mensagem ao transporte, que a reentrega e, no limite de entregas, a move
  para a dead-letter (ADR-0004).

#### Scenario: NF-e descoberta chega à esteira
- **WHEN** a fila de descoberta entrega uma referência de NF-e de mercadoria do tenant-a
- **THEN** a esteira é chamada com essa referência e com o contexto do tenant-a e da chave natural
- **AND** a mensagem é concluída quando a esteira termina

#### Scenario: Falha na esteira devolve a mensagem
- **WHEN** a esteira lança exceção ao processar a referência
- **THEN** a mensagem não é concluída e o transporte a reentrega

### Requirement: Roteamento por tipo de documento

Só a NF-e de mercadoria (modelo 55) MUST ir para a esteira. Qualquer outro tipo MUST sair com o desfecho
"ignorado: tipo fora do escopo", sem buscar nada na origem e sem envio ao destino. Hoje isso vale para a
NFS-e e o CT-e.

#### Scenario: NFS-e é ignorada sem tocar a origem
- **WHEN** a fila de descoberta entrega uma referência de NFS-e
- **THEN** o documento fica registrado como ignorado, com motivo que cita o tipo fora do escopo
- **AND** nenhuma chamada é feita à origem e nada é enviado ao destino
- **AND** a mensagem é concluída

#### Scenario: CT-e é ignorado
- **WHEN** a fila de descoberta entrega uma referência de CT-e
- **THEN** o documento fica registrado como ignorado, com motivo que cita o tipo fora do escopo

### Requirement: Desfecho "ignorado" registrado e visível

O desfecho "ignorado" MUST ser gravado no registro do documento como um estado próprio, com o motivo.
Ele MUST aparecer no dashboard com rótulo próprio e MUST NOT contar como falha. Receber de novo a mesma
referência MUST regravar o mesmo desfecho, sem erro.

Quando o documento já tinha um desfecho anterior, o estado passa a "ignorado" com o motivo novo e o
identificador externo já gravado é preservado.

#### Scenario: Motivo gravado
- **WHEN** uma NFS-e do tenant-a é ignorada
- **THEN** o registro do documento tem estado "ignorado" e motivo "ignorado: tipo fora do escopo"
  seguido do tipo

#### Scenario: Repetição regrava sem erro
- **WHEN** a mesma referência de NFS-e chega de novo depois de um reinício do poller
- **THEN** o registro continua um só, em estado "ignorado", e nenhuma entrega falha

#### Scenario: Ignorado não é falha no dashboard
- **WHEN** o dashboard lista os documentos do tenant-a e um deles está ignorado
- **THEN** ele aparece com o rótulo "Ignorado" e fica fora do filtro de falhas

### Requirement: Fora de escopo detectado na montagem

Quando o adapter de entrada, ao buscar o documento, constatar que ele saiu do escopo desde a descoberta,
o consumidor MUST registrar o desfecho "ignorado" com o motivo informado pelo adapter e concluir a
mensagem, sem retentativa. Os casos são modelo diferente de 55 ou status diferente de autorizado.

#### Scenario: Modelo mudou entre a descoberta e a montagem
- **WHEN** a referência foi descoberta como NF-e, mas o cabeçalho lido na montagem traz modelo `SE`
- **THEN** o documento fica registrado como ignorado, com motivo que cita o modelo `SE`
- **AND** a mensagem é concluída sem retentativa

### Requirement: Dead-letter da fila de descoberta visível

Uma mensagem da fila de descoberta que esgotou as entregas MUST virar registro visível do documento,
com estado de dead-letter e o motivo do transporte, igual à fila de entrada (ADR-0010).

#### Scenario: Documento esgota as entregas
- **WHEN** uma referência da fila de descoberta falha em todas as entregas e vai para a dead-letter
- **THEN** o registro do documento fica em estado de dead-letter, com o motivo informado pelo transporte

### Requirement: Repetição absorvida pela idempotência existente

O consumidor MUST NOT filtrar referências repetidas. A supressão de republicação do poller corta a
maior parte da repetição causada pela sobreposição. A repetição que resta MUST ser absorvida pela
idempotência por conteúdo da esteira (ADR-0016): mesmo conteúdo em estado enviado ou confirmado não é
reenviado. Sobram o par ainda não assentado, o reinício, a troca de réplica e o rebobinamento.

As mensagens MUST ser processadas uma por vez, para que duas cópias do mesmo documento não passem juntas
pela checagem de idempotência e gerem dois envios.

#### Scenario: Mesma nota chega duas vezes
- **WHEN** a mesma referência de NF-e chega duas vezes e o conteúdo na origem não mudou
- **THEN** a nota é montada nas duas vezes, mas enviada ao destino uma vez só

#### Scenario: Cópias não correm em paralelo
- **WHEN** duas cópias da mesma referência estão na fila ao mesmo tempo
- **THEN** a segunda só começa depois que a primeira terminou
