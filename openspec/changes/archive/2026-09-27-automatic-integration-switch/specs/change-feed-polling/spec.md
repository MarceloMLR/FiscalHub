## MODIFIED Requirements

### Requirement: Seleção dos tenants a consultar

A cada passada, o sistema MUST considerar apenas os tenants cujo perfil de conector usa, como adapter
de entrada, a origem do feed e que têm o poll ligado nas settings desse adapter. Poll desligado ou
ausente MUST significar "não consultar". Um tenant sem perfil ou com outro adapter de entrada MUST ser
ignorado. O poll desligado de propósito (`poll.enabled = false`, com a seção presente) MUST ficar em
silêncio: a passada não registra consulta, falha nem aviso para esse tenant.

#### Scenario: Tenant com o adapter e o poll ligado é consultado
- **WHEN** a passada roda e o tenant-a tem adapter de entrada `Dynamics365` com `poll.enabled = true`
- **THEN** o sistema consulta o feed de mudanças do tenant-a

#### Scenario: Poll desligado não consulta
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll.enabled` é `false` ou está ausente
- **THEN** o sistema não consulta a origem desse tenant nem mexe na marca d'água dele

#### Scenario: Desligado de propósito fica em silêncio
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll = {"enabled": false}`
- **THEN** o resumo da passada não conta o tenant-a como consultado
- **AND** nenhuma falha e nenhum aviso de configuração são registrados para o tenant-a

#### Scenario: Tenant de outro adapter é ignorado
- **WHEN** o tenant-b tem adapter de entrada `iScala`
- **THEN** o feed do D365 não é consultado para o tenant-b

### Requirement: Marca d'água persistida por tenant e origem

O sistema MUST persistir uma marca d'água por par (tenant, origem). Ela é o instante até o qual tudo o
que mudou na origem já foi enfileirado. Ela MUST sobreviver a restart do processo e MUST ser gravada em
ticks UTC, de modo a comparar e ordenar igual em SQL Server e SQLite. Na primeira consulta de um par
sem marca, o sistema MUST criá-la com o valor de `poll.startFrom` do perfil, se houver. Sem
`startFrom`, a marca nasce no instante atual. A marca MUST nunca regredir. Desligar o poll MUST NOT
mexer na marca: religado, o tenant retoma da marca preservada.

#### Scenario: Marca sobrevive a restart
- **WHEN** a marca do tenant-a está em 2026-09-25T12:00:00Z e o processo reinicia
- **THEN** a primeira passada depois do restart consulta a partir dessa marca (menos a sobreposição)

#### Scenario: Primeira consulta com startFrom
- **WHEN** o tenant-a não tem marca e o perfil define `poll.startFrom = 2015-01-01T00:00:00Z`
- **THEN** a marca é criada em 2015-01-01T00:00:00Z e a consulta parte dela (menos a sobreposição)

#### Scenario: Primeira consulta sem startFrom
- **WHEN** o tenant-a não tem marca nem `startFrom`
- **THEN** a marca é criada no instante atual, e o histórico anterior não é varrido pelo poll

#### Scenario: Marca não regride
- **WHEN** a marca está em 12:00:00Z e uma página devolvida pela origem tem marca alta 11:58:00Z
- **THEN** a marca continua em 12:00:00Z

#### Scenario: Religado retoma da marca
- **WHEN** a marca do tenant-a está em 12:00:00Z, o poll fica desligado por duas horas e é religado, e o
  perfil define `poll.startFrom = 2015-01-01T00:00:00Z`
- **THEN** a primeira consulta depois de religado parte de 12:00:00Z menos a sobreposição, e não do
  `startFrom` nem do instante atual
- **AND** o que mudou na origem enquanto o poll estava desligado é enfileirado

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
- **Rebobinamento:** o registro do (tenant, origem) MUST ser descartado antes da leitura em dois casos:
  - quando a marca d'água regride desde a última vista por esta réplica;
  - quando, no início do poll, o par não tem cursor, ou tem cursor sem marca. É o caso do cursor
    apagado para o `startFrom` valer de novo.

  Nos dois casos, o descarte MUST valer com o processo de pé, sem reinício e sem passo manual.
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

#### Scenario: Cursor apagado entre passadas
- **WHEN** uma passada enfileirou o documento A com carimbo assentado, o cursor do tenant-a é apagado com o
  processo de pé, e o perfil define `poll.startFrom = 2015-01-01T00:00:00Z`
- **THEN** a passada seguinte cria a marca em 2015-01-01T00:00:00Z e enfileira A de novo
- **AND** a passada não conta A como suprimido

#### Scenario: Cursor apagado no meio de uma passada
- **WHEN** a primeira passada do tenant-a parte do `startFrom`, enfileira a primeira página, e o cursor é
  apagado antes de a marca avançar
- **THEN** essa passada termina sem gravar a marca da página
- **AND** a passada seguinte parte do `startFrom` e enfileira de novo as referências daquela página

#### Scenario: Cursor sem marca
- **WHEN** o registro tem pares publicados do tenant-a e o cursor dele existe sem marca, porque foi recriado
  por uma falha registrada depois de apagado
- **THEN** o registro do tenant-a é descartado, e a marca nasce do `startFrom`

#### Scenario: Falha no meio da página
- **WHEN** uma página tem 5 referências assentadas, as 3 primeiras são enfileiradas e a fila recusa a 4ª
- **THEN** na passada seguinte as 3 primeiras são suprimidas, e a 4ª e a 5ª são enfileiradas

#### Scenario: Registro podado pela janela
- **WHEN** a marca avança e o carimbo de um par registrado fica menor ou igual a marca − sobreposição
- **THEN** o par sai do registro
