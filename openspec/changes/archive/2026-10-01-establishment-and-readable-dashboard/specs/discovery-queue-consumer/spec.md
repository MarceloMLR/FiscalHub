## MODIFIED Requirements

### Requirement: Desfecho "ignorado" registrado e visível

O desfecho "ignorado" MUST ser gravado no registro do documento como um estado próprio, com o motivo.
Ele MUST aparecer no dashboard com rótulo próprio e MUST NOT contar como falha. Receber de novo a mesma
referência MUST regravar o mesmo desfecho, sem erro.

O registro da nota ignorada MUST levar:

- o grupo que a referência trouxe da descoberta (empresa, filial, data de referência, número e modelo);
- o modo da integração.

Com isso, a nota ignorada entra no grupo e nos cards da data fiscal dela (`document-grouping`). A referência sem o
grupo, publicada antes desta mudança, grava o desfecho sem ele.

Quando o documento já tinha um desfecho anterior, o estado passa a "ignorado" com o motivo novo e o
identificador externo já gravado é preservado. O grupo já gravado pela montagem também é preservado.

#### Scenario: Motivo gravado
- **WHEN** uma NFS-e do tenant-a é ignorada
- **THEN** o registro do documento tem estado "ignorado" e motivo "ignorado: tipo fora do escopo"
  seguido do tipo

#### Scenario: Ignorada com o grupo da descoberta
- **WHEN** uma NFS-e do tenant-a, descoberta com data fiscal 2026-08-07, é ignorada
- **THEN** o registro tem a empresa, a filial, a data de referência 2026-08-07, o número e o modelo `SE` que a
  referência trouxe, e o modo `Automatic`
- **AND** a nota aparece na tabela de grupos, na data 2026-08-07

#### Scenario: Repetição regrava sem erro
- **WHEN** a mesma referência de NFS-e chega de novo depois de um reinício do poller
- **THEN** o registro continua um só, em estado "ignorado", e nenhuma entrega falha

#### Scenario: Ignorado não é falha no dashboard
- **WHEN** o dashboard lista os documentos do tenant-a e um deles está ignorado
- **THEN** ele aparece com o rótulo "Ignorado" e fica fora do filtro de falhas

### Requirement: Dead-letter da fila de descoberta visível

Uma mensagem da fila de descoberta que esgotou as entregas MUST virar registro visível do documento,
com estado de dead-letter e o motivo do transporte, igual à fila de entrada (ADR-0010). Quando a referência traz o
grupo da descoberta e o registro ainda não tem grupo, o registro MUST levar esse grupo e o modo. Assim a nota que
falhou antes da montagem aparece no grupo e no card "Com erro" da data fiscal dela.

#### Scenario: Documento esgota as entregas
- **WHEN** uma referência da fila de descoberta falha em todas as entregas e vai para a dead-letter
- **THEN** o registro do documento fica em estado de dead-letter, com o motivo informado pelo transporte

#### Scenario: Dead-letter com o grupo da descoberta
- **WHEN** uma NF-e 55 descoberta com data fiscal 2016-03-05 falha na montagem em todas as entregas
- **THEN** o registro em dead-letter tem a empresa, a filial e a data de referência 2016-03-05 que a referência
  trouxe
