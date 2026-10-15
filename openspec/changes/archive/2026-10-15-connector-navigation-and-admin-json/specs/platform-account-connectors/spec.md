## MODIFIED Requirements

### Requirement: O adapter declara os módulos e as integrações; o perfil liga o que foi contratado

Cada adapter de saída MUST **declarar**, em código: o **rótulo** do conector na tela, se o conector **tem
agendamento**, os módulos que atende, as integrações de cada módulo, a versão de base que cada integração usa, e se a
API daquela integração **aceita lote**. Essa declaração é característica da plataforma, MUST NOT ser gravada no perfil
e MUST NOT ser editável por quem configura.

**O rótulo** é o nome do conector na barra lateral e nas telas (`module-navigation`) — na Avalara, `Tax Compliance`.
Ele é texto de tela: MUST NOT entrar em chave, nome de segredo, URL nem comparação. A identidade do conector continua
sendo o **adapter**, comparado exato. O rótulo é declarado, e não um mapa de nomes do lado da tela, para que um adapter
novo não nasça sem nome no menu.

**O agendamento** diz se aquele conector aparece com o item de Agendamento (ADR-0039 §2). Não é derivado de uma
integração ligada: um cliente que contratou o conector e ainda não preencheu a integração continua precisando do
Agendamento, que é por onde ele dispara a primeira carga manual.

**A chave de cada integração é do vocabulário do hub, e não do adapter.** Dois adapters que atendem a mesma integração
MUST declará-la com a mesma chave. É essa chave que a regra de um conector por integração compara.

O perfil MUST **ligar** os módulos e as integrações que o cliente contratou.

- **Ligar um módulo ou uma integração que o adapter não declara** MUST ser recusado com HTTP 400, nomeando o adapter e o
  que foi pedido.
- **Ligar uma integração cujo módulo não está ligado** MUST ser recusado com HTTP 400.
- **Um módulo ligado sem nenhuma integração ligada** MUST ser aceito: o cliente contratou o domínio e ainda não definiu
  o que carrega dele.

#### Scenario: Integração que o adapter não declara
- **WHEN** a gravação liga, no conector `Avalara`, uma integração que o adapter dele não declara
- **THEN** a gravação responde 400, nomeando o adapter e a integração

#### Scenario: Integração sem o módulo
- **WHEN** a gravação liga a integração de plano de contas sem ligar o módulo Contábil
- **THEN** a gravação responde 400

#### Scenario: Módulo ligado e vazio
- **WHEN** a gravação liga o módulo Inventário sem ligar nenhuma integração dele
- **THEN** a gravação é aceita
- **AND** o `/info` passa a trazer o Inventário entre os módulos daquele conector

#### Scenario: O rótulo e o agendamento são do adapter
- **WHEN** um Viewer do tenant-a chama o `/info`, e o tenant tem o conector do adapter `Avalara`
- **THEN** o conector vem com o rótulo `Tax Compliance` e dizendo que ele agenda
- **AND** nem o rótulo nem o agendamento estão gravados no perfil

#### Scenario: O rótulo não é editável
- **WHEN** uma gravação traz um rótulo, ou o agendamento, dentro de um conector
- **THEN** o campo é ignorado, como qualquer parte da declaração que o perfil não guarda
- **AND** a leitura continua devolvendo o rótulo e o agendamento declarados pelo adapter

### Requirement: O perfil de um conector só é convertido na migração

A migração MUST converter o perfil existente sem perder o que a instância usa:

- a **conta** nasce do `OutboundAdapter` gravado, com o `clientId` e o `tokenUrl` da seção do ambiente gravado no
  perfil, quando a seção os tem;
- **um conector** nasce com aquele mesmo adapter. A URL base da seção vira a base da versão `v2`, e o resto das settings
  daquela seção, inclusive a tabela `establishments`, vai para as settings do conector;
- o **módulo Fiscal** nasce ligado, com a integração de notas fiscais ligada e o caminho **à espera do host** (ver "O
  caminho que a migração não sabe vem do host, uma vez");
- os **módulos gravados no tenant** passam para esse conector, sem integrações ligadas. O Fiscal fica ligado **sempre**,
  mesmo quando a lista do tenant não o tinha: é a integração de notas fiscais que mantém o envio, e ela só fica ligada
  com o módulo dela;
- a seção do **outro** ambiente é descartada: ela pertence a outro deploy;
- a seção do ambiente gravado é achada **sem distinguir maiúscula** (`sandbox` ou `Sandbox`);
- settings já sem seção nenhuma passam inteiras;
- a entrada e os chamados passam sem mudança.

**Dado ambíguo para a migração.** Um perfil com seção por ambiente cujo ambiente gravado não nomeia nenhuma delas — em
branco, ou um valor que não é `Sandbox` nem `Production` — MUST fazer a migração falhar antes de qualquer escrita, com uma
mensagem que diz isso. Escolher uma seção ou esvaziar o perfil seria decidir no escuro, numa conversão que não tem volta;
a decisão é de quem conhece o cliente. Do mesmo jeito, nada é removido sem ter sido lido, e nada é adivinhado: uma
chave da credencial ou da base grafada em outra caixa (`"BaseUrl"`), que a conversão não reconheceria, um `baseUrls` já
nas settings, que ela sobrescreveria, e um adapter de saída que não é exatamente `Avalara`, que o adapter não acharia,
MUST fazer a migração da conta e do conector falhar antes de qualquer escrita. Na migração dos módulos e das
integrações, o mesmo: uma lista de módulos do tenant fora da grafia aceita, um tenant com mais de um conector, um conector
de adapter que não é exatamente `Avalara`, e um tenant sem conector com módulos além do Fiscal MUST fazer a migração
falhar antes de qualquer escrita.

**Segredo em texto.** Um `clientSecret` com valor nas settings de saída é segredo vazado, e MUST fazer a migração falhar
antes de qualquer escrita, com uma mensagem que diz o que fazer: rotacionar o segredo na plataforma, tirar o valor
invalidado das settings, migrar e gravar o segredo novo na conta pela tela. Apagá-lo em silêncio destruiria a evidência
do vazamento. Um `clientSecret` vazio não é segredo, e não trava.

**O segredo não é copiado, nem a referência dele.** O nome do segredo muda, e a migração MUST NOT falar com o cofre:
uma migração de schema que depende de um serviço externo no ar falha pela metade. A referência gravada antes também MUST
NOT ser copiada para a conta: copiada, ela apontaria para o nome antigo, que continua no prefixo do tenant, e o segredo
apareceria configurado sob um nome que nenhuma gravação nova produz. Depois da migração o segredo aparece como **não
configurado**, e o envio falha alto apontando o campo.

**A conversão do ambiente é de ida só; o resto tem volta.** Desfazer a conversão do ambiente MUST falhar com uma
mensagem que diz isso, sem mexer no banco: depois dela, nenhuma linha guarda o ambiente, e não há de onde reconstruir a
seção descartada (ADR-0036). Os passos seguintes da conversão MUST ter volta: desfazer a conta e o conector remonta as
settings de saída achatadas a partir deles, e desfazer os módulos e as integrações devolve ao tenant a lista de módulos
do conector. A volta da conta e do conector vale enquanto a forma nova não tiver sido usada além do que as settings
achatadas comportam: com mais de uma conta no tenant, mais de um conector na conta, um conector de adapter diferente da
plataforma, uma base de versão que não a `v2`, ou uma chave da forma achatada nas settings do conector, desfazer MUST
falhar antes de qualquer escrita, dizendo que desfazer perderia dado. A volta dos módulos e das integrações vale do
mesmo jeito: com uma integração ligada além da de notas fiscais, um tamanho de lote gravado, ou mais de um conector no
tenant, desfazer MUST falhar antes de qualquer escrita. O caminho da integração de notas fiscais não volta, e não precisa:
antes dela, o envio lê o caminho da configuração do host.

#### Scenario: Perfil com dois ambientes, instância de sandbox
- **WHEN** o perfil do tenant-a tem `Environment` = `Sandbox`, settings com as seções `sandbox` e `production`, e
  `modules = ["Fiscal", "Contabil"]`, e a migração roda
- **THEN** o tenant-a passa a ter a conta da plataforma com o `clientId` da seção `sandbox` e sem referência de segredo,
  e um conector cuja base da `v2` é a `baseUrl` da seção `sandbox`, com o resto daquela seção
- **AND** o módulo Fiscal fica ligado com a integração de notas fiscais, e o Contábil fica ligado sem integrações
- **AND** nada da seção `production` fica gravado

#### Scenario: Ambiente gravado que não nomeia seção nenhuma
- **WHEN** o perfil do tenant-a tem as seções `sandbox` e `production`, e o ambiente gravado é `Hml`, ou está em branco, e
  a migração roda
- **THEN** a migração falha, com uma mensagem que diz que o ambiente do perfil não nomeia nenhuma seção
- **AND** o banco continua como estava: o perfil, as duas seções e o ambiente gravado

#### Scenario: Seção com a chave capitalizada
- **WHEN** o perfil do tenant-a tem `Environment` = `Sandbox` e as settings com a chave `"Sandbox"`, e a migração roda
- **THEN** a seção é achada e achatada como a minúscula, e o perfil não fica vazio

#### Scenario: Chave da conversão grafada em outra caixa
- **WHEN** as settings de saída do tenant-a têm `"BaseUrl"` no lugar de `"baseUrl"`, e a migração da conta e do conector
  roda
- **THEN** a migração falha, com uma mensagem que diz que a chave está grafada em caixa diferente da esperada
- **AND** o banco continua como estava

#### Scenario: Segredo em texto nas settings de saída
- **WHEN** as settings de saída do tenant-a têm `"clientSecret"` com valor, e a migração da conta e do conector roda
- **THEN** a migração falha, com uma mensagem que manda rotacionar o segredo na plataforma, sem repetir o valor
- **AND** o banco continua como estava

#### Scenario: Adapter de saída grafado diferente
- **WHEN** o adapter de saída gravado do tenant-a é `avalara`, ou `Mock`, e a migração da conta e do conector roda
- **THEN** a migração falha, com uma mensagem que diz que o adapter de saída não é exatamente `Avalara`
- **AND** o banco continua como estava

#### Scenario: O segredo depois da migração
- **WHEN** a migração converte um perfil cujo Client Secret estava configurado
- **THEN** a leitura do perfil mostra o Client Secret como não configurado
- **AND** o cofre não foi lido nem escrito pela migração

#### Scenario: Seção sem endpoint de token
- **WHEN** a seção do ambiente gravado não tem `tokenUrl`, e a migração roda
- **THEN** a conta nasce sem endpoint de token, e a leitura do perfil o mostra como não configurado
- **AND** um envio é rejeitado com motivo de configuração do conector que nomeia o `tokenUrl` da conta

#### Scenario: Tenant sem o Fiscal na lista
- **WHEN** o perfil do tenant-a tem `modules = ["Inventario"]`, e a migração roda
- **THEN** o conector fica com o Fiscal ligado, com a integração de notas fiscais, e com o Inventário ligado sem
  integrações
- **AND** o `/info` do tenant-a, que antes trazia só o Inventário, passa a trazer o Fiscal e o Inventário entre os
  módulos daquele conector

#### Scenario: Entrada e chamados atravessam a migração
- **WHEN** a migração converte um perfil com adapter de entrada, settings de entrada e adapter de chamados
- **THEN** os três continuam iguais depois dela

#### Scenario: Desfazer a conversão do ambiente
- **WHEN** o banco está com as settings de saída já achatadas, e alguém pede para desfazer a conversão do ambiente
- **THEN** o pedido falha com uma mensagem que diz que a conversão do ambiente é de ida só
- **AND** o banco continua como estava

#### Scenario: Desfazer a conta e o conector
- **WHEN** o tenant-a tem a conta `Avalara`, com `clientId` e `tokenUrl`, e o conector `Avalara`, com a base da `v2` e a
  tabela `establishments`, e alguém desfaz a conta e o conector
- **THEN** o perfil volta a ter o adapter de saída `Avalara` e as settings de saída achatadas, com o `clientId`, o
  `tokenUrl`, a `baseUrl` e a tabela `establishments`

#### Scenario: Desfazer a conta e o conector depois de usar a forma nova
- **WHEN** o conector do tenant-a tem a base da `v1` gravada, e alguém desfaz a conta e o conector
- **THEN** o pedido falha, com uma mensagem que diz que desfazer perderia dado
- **AND** as contas e os conectores continuam como estavam

#### Scenario: Desfazer os módulos e as integrações
- **WHEN** o conector do tenant-a tem o Fiscal e o Inventário ligados, e alguém desfaz os módulos e as integrações
- **THEN** o perfil do tenant-a volta a ter `modules = ["Fiscal", "Inventario"]`

#### Scenario: Desfazer os módulos e as integrações depois de usar a forma nova
- **WHEN** o conector do tenant-a tem a integração de plano de contas ligada, e alguém desfaz os módulos e as integrações
- **THEN** o pedido falha, com uma mensagem que diz que desfazer perderia dado
- **AND** os conectores, os módulos e as integrações continuam como estavam

#### Scenario: Desfazer a change inteira
- **WHEN** alguém pede para voltar o banco a antes daquela mudança, e a forma nova não foi usada além do que a de antes
  comporta
- **THEN** os módulos e as integrações, e depois a conta e o conector, são desfeitos
- **AND** o pedido para na conversão do ambiente, com a mensagem que diz que ela é de ida só, e o banco fica com as
  settings de saída achatadas
