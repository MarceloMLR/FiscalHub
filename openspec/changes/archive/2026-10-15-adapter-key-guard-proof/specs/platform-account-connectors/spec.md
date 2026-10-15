## ADDED Requirements

### Requirement: A chave do conector é única entre as declarações do deploy

O **nome do adapter** é a chave do conector, e ele MUST ser único entre **todas** as declarações registradas num mesmo
deploy — não apenas dentro de uma conta de plataforma. Duas declarações com o mesmo nome de adapter MUST **derrubar a
subida do host**, com motivo que nomeia o adapter repetido e as plataformas que o declaram.

Uma chave ambígua escolhida em silêncio na primeira leitura é pior do que uma subida que falha dizendo qual pacote
tirar: o `/info` responderia duas entradas com o mesmo `adapter`, e a barra lateral (`module-navigation`) ficaria com
duas chaves iguais — dois grupos indistinguíveis, e o item de um abrindo a tela do outro.

- **A comparação é exata**, como todas as outras do par (plataforma, adapter): `avalara` não é `Avalara`. A chave é o
  nome como o adapter o escreve.
- **Dois adapters da mesma plataforma continuam valendo.** É o caminho de crescimento escrito na própria declaração —
  com o AvaTax, a plataforma é `Avalara` e os adapters são dois. O que se recusa é o **nome repetido**, e não a
  plataforma repetida.
- **A unicidade por conta não substitui esta.** A gravação do perfil já recusa o mesmo adapter duas vezes dentro de uma
  conta; isso é por conta, e não entre as declarações do deploy. São duas regras, e a de fora é esta.
- **A recusa MUST acontecer na subida**, sobre as declarações já reunidas, e MUST NOT depender de um documento chegar.
  Uma chave ambígua descoberta no primeiro despacho é tarde: a tela já desenhou.
- **A recusa MUST vir antes de qualquer infraestrutura** — banco, blob, fila ou cofre. Um deploy com a chave ambígua
  não chega a migrar schema nem a escrever em lugar nenhum: ele para na conferência.

#### Scenario: Duas plataformas declaram o mesmo adapter
- **WHEN** o deploy registra duas declarações com o mesmo nome de adapter, de plataformas diferentes
- **THEN** o host não sobe
- **AND** o motivo nomeia o adapter repetido e as duas plataformas que o declaram, na ordem em que foram registradas

#### Scenario: Dois adapters da mesma plataforma
- **WHEN** uma mesma plataforma declara dois adapters, com nomes diferentes
- **THEN** o host sobe normalmente, e os dois conectores existem

#### Scenario: A comparação da chave é exata
- **WHEN** duas declarações trazem `Avalara` e `avalara` como nome de adapter
- **THEN** elas são adapters diferentes, e o host sobe

#### Scenario: A recusa vem antes da infraestrutura
- **WHEN** o deploy registra duas declarações com o mesmo nome de adapter, e o banco, o blob, a fila e o cofre estão
  todos inacessíveis
- **THEN** a subida falha com a recusa da chave repetida, e não por falta de infraestrutura
- **AND** nada é gravado em banco, blob ou fila
