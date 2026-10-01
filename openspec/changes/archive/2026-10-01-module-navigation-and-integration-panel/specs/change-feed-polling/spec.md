## MODIFIED Requirements

### Requirement: Marca d'água persistida por tenant e origem

O sistema MUST persistir uma marca d'água por par (tenant, origem). Ela é o instante até o qual tudo o
que mudou na origem já foi enfileirado. Ela MUST sobreviver a restart do processo e MUST ser gravada em
ticks UTC, de modo a comparar e ordenar igual em SQL Server e SQLite. Na primeira consulta de um par
sem marca, o sistema MUST criá-la com o valor de `poll.startFrom` do perfil, se houver. Sem
`startFrom`, a marca nasce no instante atual. Desligar o poll MUST NOT mexer na marca: religado, o tenant retoma da
marca preservada.

O coletor MUST nunca fazer a marca regredir. O único caminho do sistema que a faz regredir é o rebobinamento do Admin
pela tela (capability `automatic-integration-panel`). Ele toma o mesmo lease do coletor e grava a marca condicionada a
ele. A regressão que ele grava é reconhecida pela regra de rebobinamento da "Supressão de par já publicado", como
qualquer outra.

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

#### Scenario: Rebobinamento pelo Admin
- **WHEN** a marca do tenant-a está em 12:00:00Z, e o Admin a rebobina pela tela para 2026-09-01T00:00:00Z
- **THEN** a marca passa a 2026-09-01T00:00:00Z
- **AND** a passada seguinte parte dela, menos a sobreposição, e a marca volta a só avançar
