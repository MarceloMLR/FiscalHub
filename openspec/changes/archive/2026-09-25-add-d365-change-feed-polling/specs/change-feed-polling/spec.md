## Purpose

Descobrir de forma contínua os documentos fiscais que mudaram na origem de cada tenant. O motor
pergunta à origem o que mudou desde uma marca d'água nossa e enfileira uma referência leve por
documento (claim-check). Ele é auto-recuperável: falha não avança a marca, e só uma réplica faz o
poll de um tenant por vez.

## ADDED Requirements

### Requirement: Seleção dos tenants a consultar

A cada passada, o sistema MUST considerar apenas os tenants cujo perfil de conector usa, como adapter
de entrada, a origem do feed e que têm o poll ligado nas settings desse adapter. Poll desligado ou
ausente MUST significar "não consultar". Um tenant sem perfil ou com outro adapter de entrada MUST ser
ignorado.

#### Scenario: Tenant com o adapter e o poll ligado é consultado
- **WHEN** a passada roda e o tenant-a tem adapter de entrada `Dynamics365` com `poll.enabled = true`
- **THEN** o sistema consulta o feed de mudanças do tenant-a

#### Scenario: Poll desligado não consulta
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll.enabled` é `false` ou está ausente
- **THEN** o sistema não consulta a origem desse tenant nem mexe na marca d'água dele

#### Scenario: Tenant de outro adapter é ignorado
- **WHEN** o tenant-b tem adapter de entrada `iScala`
- **THEN** o feed do D365 não é consultado para o tenant-b

### Requirement: Intervalo de poll por tenant

O sistema MUST consultar cada tenant no máximo uma vez por intervalo configurado no perfil. O padrão é
**60 segundos**. O intervalo MUST ser contado a partir do fim do último poll daquele tenant, com ou sem
sucesso. Quando a origem pedir espera por throttling, o sistema MUST adiar o próximo poll desse tenant
até o instante pedido, mesmo que o intervalo já tenha vencido.

#### Scenario: Ainda dentro do intervalo
- **WHEN** o último poll do tenant-a terminou há 30 segundos e o intervalo é 60 segundos
- **THEN** a passada não consulta o tenant-a

#### Scenario: Intervalo vencido
- **WHEN** o último poll do tenant-a terminou há 61 segundos e o intervalo é 60 segundos
- **THEN** a passada consulta o tenant-a

#### Scenario: Intervalo configurado por tenant
- **WHEN** o perfil do tenant-a define `poll.intervalSeconds = 300`
- **THEN** o tenant-a é consultado no máximo a cada 300 segundos, e os demais tenants seguem o
  próprio intervalo

#### Scenario: Throttling adia o tenant
- **WHEN** o poll do tenant-a termina porque a origem respondeu "tente de novo em 10 minutos"
- **THEN** o tenant-a só volta a ser consultado depois desses 10 minutos
- **AND** os demais tenants não são afetados

### Requirement: Marca d'água persistida por tenant e origem

O sistema MUST persistir uma marca d'água por par (tenant, origem). Ela é o instante até o qual tudo o
que mudou na origem já foi enfileirado. Ela MUST sobreviver a restart do processo e MUST ser gravada em
ticks UTC, de modo a comparar e ordenar igual em SQL Server e SQLite. Na primeira consulta de um par
sem marca, o sistema MUST criá-la com o valor de `poll.startFrom` do perfil, se houver. Sem
`startFrom`, a marca nasce no instante atual. A marca MUST nunca regredir.

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

### Requirement: Sobreposição na janela de consulta

O sistema MUST consultar a origem a partir da marca d'água **menos a sobreposição** configurada no
perfil. O padrão é **300 segundos**. A marca é nossa e o relógio é o da origem; a sobreposição absorve
diferença de relógio e gravação que fica visível com atraso. A sobreposição MUST ser de pelo menos 1
segundo; zero ou negativo é erro de configuração do tenant. Registros com o mesmo timestamp da marca
podem ter ficado para uma página não lida, e só a sobreposição os relê. Documentos devolvidos de novo
por causa da sobreposição MUST ser reenfileirados sem filtro no motor. O descarte de repetidos é da
idempotência por conteúdo da esteira (ADR-0016).

#### Scenario: Consulta parte da marca menos a sobreposição
- **WHEN** a marca do tenant-a está em 12:00:00Z e a sobreposição é 300 segundos
- **THEN** o feed é consultado com "mudou depois de 11:55:00Z"

#### Scenario: Sobreposição gera repetido
- **WHEN** a passada anterior enfileirou o documento A, e a passada atual o recebe de novo porque A
  está dentro da janela de sobreposição
- **THEN** o documento A é enfileirado de novo
- **AND** a marca não regride

#### Scenario: Sobreposição zero é recusada
- **WHEN** o perfil do tenant-a define `poll.overlapSeconds = 0`
- **THEN** o poll do tenant-a falha com erro de configuração registrado, sem consultar a origem
- **AND** os demais tenants seguem

### Requirement: Avanço da marca por página e falha que não avança

O sistema MUST enfileirar todas as referências de uma página **antes** de avançar a marca d'água até a
marca alta dessa página. Qualquer falha MUST interromper o poll do tenant sem avançar a marca além da
última página totalmente enfileirada: na consulta à origem, no enfileiramento ou na gravação da marca.
Na próxima passada, a consulta MUST repetir a partir da marca preservada.

#### Scenario: Falha na consulta não avança a marca
- **WHEN** a marca está em 12:00:00Z e a consulta à origem falha
- **THEN** a marca continua em 12:00:00Z
- **AND** a próxima passada consulta de novo a partir de 12:00:00Z menos a sobreposição

#### Scenario: Várias páginas avançam página a página
- **WHEN** a origem devolve três páginas com marcas altas 12:01, 12:02 e 12:03
- **THEN** as referências das três páginas são enfileiradas, em ordem
- **AND** a marca termina em 12:03

#### Scenario: Falha na segunda página preserva a primeira
- **WHEN** a primeira página (marca alta 12:01) é enfileirada e a leitura da segunda página falha
- **THEN** a marca fica em 12:01
- **AND** as referências da primeira página continuam enfileiradas

#### Scenario: Falha no enfileiramento não avança
- **WHEN** a fila recusa uma referência no meio de uma página
- **THEN** a marca não avança para a marca alta dessa página

#### Scenario: Página vazia
- **WHEN** a origem devolve uma página sem referências, com marca alta 12:05
- **THEN** nada é enfileirado
- **AND** a marca avança para 12:05, sem regredir

### Requirement: Limite de páginas por passada

O sistema MUST limitar quantas páginas consome de um tenant numa única passada, para que um tenant com
muito delta (por exemplo, uma marca rebobinada) não monopolize a passada. Ao atingir o limite, o sistema
MUST parar de ler com a marca na última página enfileirada e seguir para o próximo tenant. O resto é
lido nas passadas seguintes.

#### Scenario: Limite atingido
- **WHEN** o limite é 20 páginas e a origem tem 50 páginas de delta para o tenant-a
- **THEN** a passada enfileira as 20 primeiras páginas, avança a marca até a 20ª e segue para o próximo
  tenant

### Requirement: Lease por tenant

Antes de consultar um tenant, o sistema MUST obter um lease exclusivo por (tenant, origem), com prazo de
validade, de modo que duas réplicas não façam poll do mesmo tenant ao mesmo tempo.

- Com o lease de outra réplica ainda válido, o tenant MUST ser pulado nesta passada, sem consultar a
  origem.
- O lease MUST ser renovado a cada página, para seguir válido durante uma leitura longa, e liberado ao
  fim do poll do tenant, com ou sem sucesso.
- **Fencing.** A gravação da marca d'água MUST ser condicionada à posse do lease válido **na mesma
  operação atômica** da gravação, e não numa verificação anterior. Se o lease não for mais do worker,
  seja porque a renovação falhou ou porque a gravação foi recusada, o sistema MUST parar sem gravar a
  marca daquela página e sem contar falha.
- Uma réplica que perde o lease pode ter enfileirado a página antes de perceber. O efeito admitido é
  repetição, nunca avanço da marca além do que foi enfileirado.
- Um lease expirado MUST poder ser tomado por outra réplica.

#### Scenario: Lease já tomado
- **WHEN** a réplica 2 roda a passada enquanto a réplica 1 detém o lease válido do tenant-a
- **THEN** a réplica 2 não consulta a origem do tenant-a nem mexe na marca dele

#### Scenario: Lease expirado é retomado
- **WHEN** a réplica 1 caiu segurando o lease do tenant-a e o prazo expirou
- **THEN** a próxima réplica que rodar a passada toma o lease e consulta o tenant-a

#### Scenario: Lease perdido no meio do poll
- **WHEN** a réplica 1 terminou de enfileirar uma página, mas o lease dela expirou e foi tomado pela
  réplica 2
- **THEN** a réplica 1 não grava a marca dessa página e encerra o poll do tenant

#### Scenario: Gravação recusada por perda do lease entre renovar e gravar
- **WHEN** a réplica 1 renovou o lease, sofreu uma pausa longa, e nesse intervalo o lease expirou e foi
  tomado pela réplica 2
- **THEN** a gravação da marca pela réplica 1 é recusada e a marca continua a que a réplica 2 deixou
- **AND** a réplica 1 encerra o poll do tenant sem contar falha

#### Scenario: Lease liberado depois de falha
- **WHEN** o poll do tenant-a falha
- **THEN** o lease do tenant-a é liberado
- **AND** a falha não deixa o tenant travado até o prazo expirar

### Requirement: Enfileiramento na fila de descoberta

Cada referência descoberta MUST ser publicada como mensagem leve (claim-check, sem o documento) numa
fila de descoberta própria, separada da fila de entrada da esteira. Nesta fatia essa fila não tem
consumidor. A referência MUST ir com o gatilho idempotente (`Event`), nunca com o de recarga manual.

#### Scenario: Referência vai para a fila de descoberta
- **WHEN** o poll do tenant-a descobre o documento `brmf|BRMF21-10000027`
- **THEN** uma mensagem com a referência é publicada na fila `documents-discovered`
- **AND** nada é publicado na fila `documents-in`

#### Scenario: Gatilho idempotente
- **WHEN** uma referência descoberta pelo poll é enfileirada
- **THEN** o gatilho da referência é `Event`

### Requirement: Isolamento de falha e registro do estado do poll

A falha de um tenant MUST NOT impedir o poll dos demais tenants na mesma passada. Para cada tenant, o
sistema MUST registrar o desfecho do último poll: o instante, o número de falhas consecutivas (zerado no
sucesso) e a mensagem do último erro. O registro serve para diagnóstico. Cancelamento por desligamento
do processo MUST NOT contar como falha.

#### Scenario: Um tenant falha, o outro segue
- **WHEN** na mesma passada a consulta do tenant-a falha e a do tenant-c funciona
- **THEN** o tenant-c tem as referências enfileiradas e a marca avançada
- **AND** o tenant-a fica com a marca intacta, falhas consecutivas incrementadas e o erro registrado

#### Scenario: Sucesso zera as falhas
- **WHEN** o tenant-a tinha 3 falhas consecutivas e o poll atual termina com sucesso
- **THEN** as falhas consecutivas do tenant-a voltam a 0 e o último erro é limpo

#### Scenario: Desligamento não conta como falha
- **WHEN** o processo é desligado no meio do poll do tenant-a
- **THEN** a marca não avança além da última página enfileirada
- **AND** as falhas consecutivas do tenant-a não são incrementadas
