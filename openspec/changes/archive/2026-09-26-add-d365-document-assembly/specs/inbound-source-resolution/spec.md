## Purpose

Decidir, para cada documento que entra na esteira, qual adapter de entrada sabe buscá-lo. A referência
diz de onde o documento veio; o perfil do tenant é só o fallback para referências antigas, sem origem.
Assim o mesmo tenant pode receber documentos de origens diferentes na mesma execução.

## ADDED Requirements

### Requirement: Origem opcional na referência

A referência de documento MUST aceitar uma origem opcional, que identifica o adapter de entrada capaz de
buscá-la. O identificador MUST ser o mesmo que a origem de um feed de mudanças usa (ex.: `Dynamics365`),
e `Xml` para o adapter de XML. Uma mensagem sem o campo MUST continuar válida e ser lida como "origem
ausente", sem erro. É o mesmo desenho do gatilho, em que o campo ausente cai no padrão.

#### Scenario: Mensagem antiga sem origem
- **WHEN** a esteira recebe uma mensagem publicada antes desta mudança, sem o campo de origem
- **THEN** a referência é lida normalmente, com origem ausente

#### Scenario: Origem viaja com a mensagem
- **WHEN** uma referência com origem `Dynamics365` é publicada e consumida
- **THEN** a referência consumida tem origem `Dynamics365`

### Requirement: Gatilhos de XML publicam com origem Xml

Todo gatilho que publica referência para um XML no Blob MUST preencher a origem `Xml`. São eles:

- a ingestão por drop;
- o endpoint de ingestão;
- a descoberta local por período, que atende a integração manual e a agendada;
- a busca por chave, usada no reprocesso.

#### Scenario: Drop no Blob
- **WHEN** um XML é solto na zona de drop do tenant-a
- **THEN** a referência publicada tem origem `Xml`

#### Scenario: Endpoint de ingestão
- **WHEN** o endpoint de ingestão recebe tenant, chave e locator
- **THEN** a referência publicada tem origem `Xml`

#### Scenario: Reprocesso de nota do catálogo local
- **WHEN** o usuário pede o reprocesso da nota 123 do tenant-a
- **THEN** a referência reenfileirada tem origem `Xml` e gatilho `Manual`

### Requirement: Resolução do adapter pela origem

Para cada documento, a esteira MUST escolher o adapter de entrada antes de buscar o conteúdo:

- com origem na referência, vale a origem da referência, e o perfil do tenant é ignorado;
- sem origem, vale o adapter de entrada do perfil do tenant;
- a comparação com a origem dos adapters registrados MUST ser exata.

#### Scenario: Origem da referência prevalece sobre o perfil
- **WHEN** o perfil do tenant-a tem adapter de entrada `Dynamics365` e chega uma referência do tenant-a
  com origem `Xml`
- **THEN** o documento é buscado pelo adapter de XML

#### Scenario: Sem origem, vale o perfil
- **WHEN** chega uma referência do tenant-a sem origem e o perfil do tenant-a tem adapter de entrada
  `Dynamics365`
- **THEN** o documento é buscado pelo adapter do D365

#### Scenario: Duas origens para o mesmo tenant na mesma execução
- **WHEN** na mesma execução o tenant-a tem um XML ingerido pelo drop e um documento descoberto pelo feed
  do D365
- **THEN** o XML é buscado pelo adapter de XML e o documento do D365 pelo adapter do D365
- **AND** nenhum dos dois passa pelo adapter do outro

### Requirement: Origem sem adapter falha de forma clara

Quando nenhum adapter registrado atende a origem resolvida, o processamento do documento MUST falhar
com um erro que nomeia a origem e o tenant. Nenhuma busca na origem MUST ser tentada. O mesmo vale
quando a referência não tem origem e o tenant não tem perfil. A falha segue o caminho normal de
retentativa e dead-letter do transporte.

#### Scenario: Origem sem adapter registrado
- **WHEN** chega uma referência do tenant-b sem origem e o perfil do tenant-b tem adapter de entrada
  `iScala`, que não tem adapter registrado
- **THEN** o processamento falha com um erro que cita `iScala` e `tenant-b`
- **AND** nenhum adapter de entrada é chamado

#### Scenario: Sem origem e sem perfil
- **WHEN** chega uma referência sem origem de um tenant que não tem perfil de conector
- **THEN** o processamento falha com um erro que cita o tenant e diz que não há origem nem perfil

### Requirement: Resolução em todos os caminhos da esteira

A resolução por origem MUST valer para toda entrada da esteira: a fila de entrada e a fila de
descoberta. O resto da esteira não muda. Continuam iguais a idempotência por conteúdo, a foto do
domínio, os metadados, a validação, o envio e o registro.

#### Scenario: Demo XML do tenant-a continua funcionando
- **WHEN** o perfil do tenant-a tem adapter de entrada `Dynamics365` e um XML do tenant-a entra pela
  fila de entrada com origem `Xml`
- **THEN** a nota é buscada no Blob, validada e enviada como antes desta mudança
