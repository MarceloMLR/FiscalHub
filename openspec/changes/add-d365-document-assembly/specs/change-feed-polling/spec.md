## ADDED Requirements

### Requirement: Supressão de par já publicado

O sistema MUST NOT reenfileirar uma referência cujo par (tenant, origem, chave natural, carimbo de
alteração) já foi enfileirado por esta réplica, desde que o carimbo estivesse **assentado** quando o par
foi publicado.

- **Assentado:** o carimbo é menor ou igual ao horizonte estável que a página informa, ou seja, nenhuma
  gravação futura na origem recebe carimbo até esse instante. Página sem horizonte não assenta nada.
- **Carimbo na chave:** a chave MUST incluir o carimbo. Documento alterado de verdade tem carimbo novo e
  é enfileirado. Deduplicar só pelo documento perderia alteração real.
- **Par quente:** um par publicado ainda não assentado não entra no registro. Ele é enfileirado de novo
  na passada seguinte, uma vez, e só então passa a ser suprimido. Isso cobre duas gravações do mesmo
  documento no mesmo instante de carimbo.
- **Estado em memória:** o registro dos pares publicados fica em memória, por réplica. Perdê-lo, seja
  por reinício ou por troca de réplica, MUST só fazer os repetidos voltarem a ser enfileirados, nunca
  deixar de enfileirar uma alteração.
- **Poda:** o registro esquece os pares cujo carimbo saiu da janela de consulta (carimbo ≤ marca −
  sobreposição).
- **Rebobinamento:** quando a marca d'água do (tenant, origem) regride, o registro desse par MUST ser
  descartado.
- **Avanço da marca:** uma referência suprimida já foi enfileirada numa passada anterior e conta como
  enfileirada para o avanço da marca.
- **Resumo da passada:** a passada MUST informar quantas referências suprimiu.

A supressão é filtro de tráfego, e não garantia: nada depende dela para correção. A garantia contra
reenvio ao destino continua sendo a idempotência por conteúdo da esteira (ADR-0016).

#### Scenario: Par assentado não é republicado
- **WHEN** uma passada enfileirou o documento A com carimbo 11:58:00, numa página com horizonte 11:59:50,
  e a passada seguinte recebe A de novo com o mesmo carimbo
- **THEN** A não é enfileirado de novo
- **AND** a passada conta uma referência suprimida

#### Scenario: Gravação no mesmo segundo, depois da leitura
- **WHEN** uma passada lê A com carimbo 12:00:00 numa página com horizonte 11:59:50, e A é gravado de novo
  na origem às 12:00:00.8, ficando com o mesmo carimbo 12:00:00
- **THEN** a passada seguinte enfileira A de novo
- **AND** só a partir daí A passa a ser suprimido

#### Scenario: Página sem horizonte
- **WHEN** a origem entrega uma página sem horizonte estável
- **THEN** nenhuma referência dessa página entra no registro, e nenhuma é suprimida por causa dela

#### Scenario: Reinício do processo
- **WHEN** o processo reinicia e a primeira passada recebe de novo o documento A, com o carimbo já
  publicado antes do reinício
- **THEN** A é enfileirado de novo

#### Scenario: Rebobinamento da marca
- **WHEN** a marca d'água do tenant-a é rebobinada para 2015-01-01T00:00:00Z
- **THEN** todos os documentos da janela relida são enfileirados de novo, inclusive os já publicados
  antes do rebobinamento

#### Scenario: Falha no meio da página
- **WHEN** uma página tem 5 referências assentadas, as 3 primeiras são enfileiradas e a fila recusa a 4ª
- **THEN** na passada seguinte as 3 primeiras são suprimidas, e a 4ª e a 5ª são enfileiradas

#### Scenario: Registro podado pela janela
- **WHEN** a marca avança e o carimbo de um par registrado fica menor ou igual a marca − sobreposição
- **THEN** o par sai do registro

## MODIFIED Requirements

### Requirement: Sobreposição na janela de consulta

O sistema MUST consultar a origem a partir da marca d'água **menos a sobreposição** configurada no
perfil. O padrão é **300 segundos**.

- **Por quê:** a marca é nossa e o relógio é o da origem. A sobreposição absorve diferença de relógio e
  gravação que fica visível com atraso. Registros com o mesmo timestamp da marca podem ter ficado para
  uma página não lida, e só a sobreposição os relê.
- **Mínimo:** a sobreposição MUST ser de pelo menos 1 segundo; zero ou negativo é erro de configuração
  do tenant.
- **Documento devolvido de novo pela sobreposição:** com o mesmo carimbo de alteração já publicado,
  segue a regra de supressão (requisito "Supressão de par já publicado"). Com carimbo novo, MUST ser
  enfileirado.
- **Custo:** a sobreposição MUST NOT ser encurtada para economizar tráfego.
- **Garantia:** contra reenvio ao destino, continua sendo a idempotência por conteúdo da esteira
  (ADR-0016).

#### Scenario: Consulta parte da marca menos a sobreposição
- **WHEN** a marca do tenant-a está em 12:00:00Z e a sobreposição é 300 segundos
- **THEN** o feed é consultado com "mudou depois de 11:55:00Z"

#### Scenario: Sobreposição gera repetido
- **WHEN** a passada anterior enfileirou o documento A com carimbo assentado, e a passada atual recebe A de
  novo, com o mesmo carimbo, porque A está dentro da janela de sobreposição
- **THEN** a origem devolveu o repetido, mas A não é enfileirado de novo: ele é suprimido
- **AND** a marca não regride

#### Scenario: Documento alterado dentro da janela
- **WHEN** a passada anterior enfileirou o documento A com carimbo 11:58:00, A foi alterado na origem e a
  passada atual o recebe com carimbo 11:59:30
- **THEN** A é enfileirado de novo

#### Scenario: Sobreposição zero é recusada
- **WHEN** o perfil do tenant-a define `poll.overlapSeconds = 0`
- **THEN** o poll do tenant-a falha com erro de configuração registrado, sem consultar a origem
- **AND** os demais tenants seguem

### Requirement: Enfileiramento na fila de descoberta

Cada referência descoberta MUST ser publicada como mensagem leve (claim-check, sem o documento) numa
fila de descoberta própria, separada da fila de entrada da esteira. Quem consome essa fila é a montagem
(capability `discovery-queue-consumer`). A referência MUST ir:

- com o gatilho idempotente (`Event`), nunca com o de recarga manual;
- com a origem do feed que a descobriu, com o mesmo identificador que casa com o adapter de entrada do
  perfil (ex.: `Dynamics365`). É por essa origem que a esteira escolhe o adapter que busca o documento.

#### Scenario: Referência vai para a fila de descoberta
- **WHEN** o poll do tenant-a descobre o documento `brmf|BRMF21-10000027`
- **THEN** uma mensagem com a referência é publicada na fila `documents-discovered`
- **AND** nada é publicado na fila `documents-in`

#### Scenario: Gatilho idempotente
- **WHEN** uma referência descoberta pelo poll é enfileirada
- **THEN** o gatilho da referência é `Event`

#### Scenario: Origem do feed na referência
- **WHEN** o feed de origem `Dynamics365` descobre um documento do tenant-a
- **THEN** a referência enfileirada tem origem `Dynamics365`, mesmo que o adapter do feed não a tenha
  preenchido
