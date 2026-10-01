# automatic-integration Specification

## Purpose

Deixar o Admin do tenant ligar e desligar pela tela a descoberta automática de notas no ERP, com um único lugar
guardando esse estado, e fazer toda a tela mostrar o que o coletor de fato fará.

## Requirements

### Requirement: O interruptor grava no poll do adapter de entrada

O interruptor "Integração automática" da tela de conectores MUST ler e gravar o `poll.enabled` das settings do adapter
de entrada do tenant. É o único lugar onde esse estado existe: o perfil MUST NOT guardar outro campo que diga se a
integração é automática ou em tempo real, nem na gravação nem na leitura.

- **Ligar:** grava `poll.enabled = true` e MUST preservar os demais campos da seção `poll` (intervalo, sobreposição,
  marca inicial) e o resto das settings. Num perfil sem a seção `poll`, a gravação cria a seção só com
  `enabled = true`, e os padrões do coletor valem.
- **Desligar:** grava `poll.enabled = false` e MUST manter a seção `poll`. Desligar não remove a seção.
- **Quando vale:** o interruptor segue o formulário. O estado só muda quando o perfil é salvo.
- **Tenant:** a gravação vale só para o tenant do Admin logado.
- **Valor inválido escrito:** a gravação MUST recusar um valor da seção `poll` que ela está escrevendo e que o coletor
  não conseguiria ler. "Escrevendo" é o campo que a gravação introduz, ou que difere do gravado para o mesmo adapter.
  A mensagem MUST nomear o campo, o que veio e a forma aceita, e nada é gravado.
- **Valor inválido já gravado:** um valor inválido que já estava gravado e volta igual MUST NOT impedir a gravação do
  resto do perfil. Ele fica como estava, e o coletor continua registrando a falha que o nomeia.

#### Scenario: Ligar preserva a seção
- **WHEN** o tenant-a tem `poll = {"enabled": false, "intervalSeconds": 300, "overlapSeconds": 600, "startFrom": "2015-01-01T00:00:00Z"}`,
  e o Admin liga o interruptor e salva
- **THEN** o perfil fica com `poll = {"enabled": true, "intervalSeconds": 300, "overlapSeconds": 600, "startFrom": "2015-01-01T00:00:00Z"}`
- **AND** a URL, as empresas e as referências de segredo das settings de entrada ficam como estavam

#### Scenario: Ligar num perfil sem a seção poll
- **WHEN** as settings de entrada do tenant-a não têm a seção `poll`, e o Admin liga o interruptor e salva
- **THEN** as settings ganham `poll = {"enabled": true}`, sem outro campo na seção
- **AND** o coletor do tenant-a usa o intervalo de 60 segundos e a sobreposição de 300 segundos

#### Scenario: Desligar mantém a seção
- **WHEN** o tenant-a tem `poll = {"enabled": true, "intervalSeconds": 300}`, e o Admin desliga o interruptor e salva
- **THEN** o perfil fica com `poll = {"enabled": false, "intervalSeconds": 300}`

#### Scenario: Sem salvar, nada muda
- **WHEN** o Admin mexe no interruptor e sai da tela sem salvar
- **THEN** o perfil gravado e o coletor do tenant ficam como estavam

#### Scenario: Nenhum segundo lugar guarda o estado
- **WHEN** uma gravação do perfil chega com um campo `realtime`, de um cliente antigo
- **THEN** o campo é ignorado, e nada além do `poll.enabled` passa a dizer se a integração é automática
- **AND** a leitura do perfil não traz campo `realtime`

#### Scenario: Só o tenant do Admin
- **WHEN** o Admin do tenant-a desliga o interruptor e salva, e o tenant-c tem o coletor ligado
- **THEN** o coletor do tenant-c continua ligado

#### Scenario: Valor novo ilegível é recusado
- **WHEN** a gravação muda `poll.enabled` para `"sim"`, ou muda `poll.overlapSeconds` de 300 para 0
- **THEN** a gravação é recusada com uma mensagem que nomeia o campo, o que veio e a forma aceita
- **AND** nada é gravado, nem no cofre nem no perfil

#### Scenario: Valor inválido já gravado não tranca a tela
- **WHEN** o perfil gravado do tenant-a tem `poll.overlapSeconds = 0`, posto por SQL, e o Admin troca o Client Secret e
  liga o interruptor, sem mexer nesse campo, e salva
- **THEN** a gravação é aceita, com o segredo novo no cofre e `poll.enabled = true`
- **AND** `poll.overlapSeconds` continua 0, e o coletor continua registrando a falha que cita esse campo

### Requirement: O interruptor só aparece para adapter de entrada que varre

A tela MUST mostrar o interruptor só quando o adapter de entrada escolhido varre a origem, ou seja, quando o host tem um
feed de mudanças para ele. Hoje só o `Dynamics365` varre. Para um adapter que não varre, a tela MUST NOT mostrar o
interruptor e MUST NOT acrescentar a seção `poll` às settings.

#### Scenario: Dynamics365 mostra o interruptor
- **WHEN** o Admin abre a tela de conectores com o ERP `Dynamics365`
- **THEN** o interruptor "Integração automática" aparece, ligado ou desligado conforme o `poll.enabled` gravado

#### Scenario: Adapter que não varre não mostra
- **WHEN** o ERP escolhido é o `iScala`
- **THEN** o interruptor não aparece
- **AND** salvar não acrescenta a seção `poll` às settings de entrada

#### Scenario: Trocar de ERP na tela
- **WHEN** o Admin troca o ERP de `Dynamics365` para `iScala`
- **THEN** o interruptor some
- **AND** ao voltar para o `Dynamics365` gravado, o interruptor reaparece com o estado gravado

### Requirement: O estado mostrado deriva do que o coletor fará

O endpoint de informações do tenant (`/info`) MUST responder dois valores derivados do perfil:

- **`inboundScans`:** verdadeiro quando o adapter de entrada do tenant varre, ou seja, tem um feed de mudanças
  registrado no host. É falso sem perfil.
- **`automaticIntegration`:** verdadeiro só quando o adapter de entrada varre e o `poll.enabled` das settings dele está
  ligado. Nos demais casos, a resposta MUST ser falsa:
  - o tenant não tem perfil;
  - o poll está desligado;
  - a seção `poll` está ausente;
  - o adapter de entrada não varre;
  - as settings de entrada não se deixam ler.

Settings ilegíveis MUST NOT fazer o endpoint falhar. A resposta MUST NOT trazer mais o campo `realtime`.

O selo da barra lateral mostra o estado, e não só o ligado:

- **Adapter que varre, integração ligada:** o selo MUST aparecer verde, com "Integração automática ligada".
- **Adapter que varre, integração desligada:** o selo MUST aparecer vermelho, com "Integração automática desligada".
  Isso inclui as settings ilegíveis.
- **Adapter que não varre:** o selo MUST NOT aparecer.
- **Enquanto a resposta não chegou, ou quando a leitura falhou:** o selo MUST NOT aparecer, nem verde nem vermelho.

#### Scenario: Ligado
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll.enabled = true`
- **THEN** o `/info` responde `inboundScans = true` e `automaticIntegration = true`
- **AND** a barra lateral mostra, em verde, "Integração automática ligada"

#### Scenario: Desligado
- **WHEN** o tenant-a tem adapter de entrada `Dynamics365` e `poll.enabled = false`
- **THEN** o `/info` responde `inboundScans = true` e `automaticIntegration = false`
- **AND** a barra lateral mostra, em vermelho, "Integração automática desligada"

#### Scenario: Seção poll ausente
- **WHEN** as settings de entrada do tenant-a não têm a seção `poll`
- **THEN** o `/info` responde `inboundScans = true` e `automaticIntegration = false`
- **AND** a barra lateral mostra o selo vermelho

#### Scenario: Adapter que não varre, com poll ligado por SQL
- **WHEN** o tenant-b tem adapter de entrada `iScala` e as settings dele têm `poll.enabled = true`
- **THEN** o `/info` responde `inboundScans = false` e `automaticIntegration = false`
- **AND** a barra lateral não mostra selo nenhum

#### Scenario: Settings ilegíveis
- **WHEN** as settings de entrada do tenant-a não são um JSON válido
- **THEN** o `/info` responde normalmente, com `inboundScans = true` e `automaticIntegration = false`

#### Scenario: Enquanto carrega
- **WHEN** o dashboard abre e o `/info` ainda não respondeu
- **THEN** a barra lateral não mostra o selo, nem verde nem vermelho

#### Scenario: Salvar atualiza o selo
- **WHEN** o Admin desliga o interruptor e salva
- **THEN** o selo da barra lateral passa de verde a vermelho, sem recarregar a página

### Requirement: O efeito vale na passada seguinte, sem banco e sem reinício

Com o host rodando, a gravação do interruptor MUST valer na passada seguinte do coletor, sem acesso ao banco e sem
reiniciar o processo. As passadas rodam a cada 15 segundos.

- **Desligado:** a passada seguinte MUST NOT consultar a origem do tenant.
- **Leitura em curso:** uma leitura que já estava em curso quando a gravação aconteceu termina normalmente, e a
  seguinte não começa.
- **Religado:** o tenant volta a ser consultado na primeira passada depois de vencido o intervalo, contado do último
  poll dele.

#### Scenario: Desligar pela tela
- **WHEN** o coletor do tenant-a está ligado e o Admin desliga o interruptor e salva
- **THEN** em até 15 segundos nenhuma passada consulta a origem do tenant-a
- **AND** o log do coletor não registra passada, falha nem aviso de configuração para o tenant-a

#### Scenario: Ligar pela tela
- **WHEN** o coletor do tenant-a está desligado há mais que o intervalo, e o Admin liga o interruptor e salva
- **THEN** em até 15 segundos uma passada consulta a origem do tenant-a e registra o resumo no log
