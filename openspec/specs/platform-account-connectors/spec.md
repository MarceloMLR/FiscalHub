# platform-account-connectors Specification

## Purpose
Dar ao perfil de conector a forma que comporta mais de um destino: a conta de plataforma com a credencial, a coleção de
conectores com as URLs base por versão, e os módulos e as integrações de cada conector, com o caminho e o lote de cada
integração. O ambiente deixa de ser campo do perfil e passa a ser da instância.

## Requirements

### Requirement: A conta de plataforma guarda a credencial do tenant

O perfil de conector MUST ter uma **conta de plataforma** por plataforma de compliance, e a credencial MUST morar nela,
e não no conector. A conta traz, em campos tipados:

- a plataforma (`platform`), que é a identidade dela e não se repete no tenant;
- o identificador do cliente (`clientId`);
- a referência ao segredo (`clientSecretRef`);
- o endpoint de token (`tokenUrl`).

A credencial é uma só para todos os conectores da conta: o cliente tem um par `clientId`/`clientSecret` por ambiente, e
o token emitido vale para todos os produtos daquela plataforma naquele ambiente (ADR-0036 §2). O endpoint de token
também é da conta, e nunca é derivado da URL base de um conector (`avalara-tenant-authentication`).

- **A credencial da plataforma não vai no conector.** Um `clientId` ou um `clientSecret` nas settings de um conector
  MUST ser recusado na gravação, com HTTP 400 que nomeia o campo e diz que a credencial é da conta. O `clientId` não é
  campo de segredo (`connector-secret-references`): a recusa dele é regra desta capacidade, e não da regra de nomes de
  segredo.
- **Os outros segredos do conector continuam valendo.** Um segredo que é do produto, e não da plataforma — um `apiKey`,
  por exemplo —, segue `connector-secret-references`: vai para o cofre, e as settings guardam a referência.
- **A entrada (ERP) e os chamados continuam com a credencial deles**, cada um nas suas settings.

#### Scenario: Dois conectores, uma credencial
- **WHEN** o tenant-a tem a conta `Avalara` com `clientId`, o segredo e o `tokenUrl` configurados, e dois conectores sob
  ela
- **THEN** os dois usam o mesmo `clientId`, o mesmo segredo e o mesmo endpoint de token
- **AND** nenhum dos dois tem credencial nas próprias settings

#### Scenario: O segredo da plataforma dentro do conector é recusado
- **WHEN** um Admin grava um conector com `"clientSecret"` nas settings dele
- **THEN** a gravação responde 400, nomeando o campo e dizendo que a credencial é da conta de plataforma
- **AND** nada é gravado, nem no cofre nem no perfil

#### Scenario: O clientId dentro do conector é recusado
- **WHEN** um Admin grava um conector com `"clientId": "abc"` nas settings dele
- **THEN** a gravação responde 400, nomeando o `clientId` e dizendo que a credencial é da conta de plataforma
- **AND** nada é gravado

#### Scenario: Um segredo do produto no conector é aceito
- **WHEN** um Admin grava um conector com `"apiKey": "k3y"` nas settings dele
- **THEN** a gravação é aceita, o valor vai para o cofre, e as settings do conector guardam a referência
  (`connector-secret-references`)

#### Scenario: Duas contas da mesma plataforma
- **WHEN** uma gravação traz duas contas com `platform` igual a `Avalara`
- **THEN** a gravação responde 400, e nada é gravado

### Requirement: O perfil tem uma coleção de conectores, identificados pelo adapter

O perfil MUST ter, sob cada conta, uma **lista de conectores**. Cada conector traz o **adapter**, que é a chave e MUST
ser único dentro da conta, mais as URLs base e as settings do produto.

A chave MUST servir como segmento de nome de segredo: só minúsculas, dígitos e hífen, sem hífen nas pontas e sem `--`.
Um adapter repetido dentro da mesma conta MUST ser recusado com HTTP 400.

A entrada (ERP) e os chamados continuam **um cada** no perfil. Só a saída deixa de ser singular.

Uma gravação que não traz a conta, ou não traz a lista de conectores, MUST manter o que está gravado, como os chamados
ausentes já mantêm: um dashboard que não recarregou não apaga a saída do tenant.

#### Scenario: Adapter repetido na mesma conta
- **WHEN** uma gravação traz dois conectores com o adapter `Avalara` na conta `Avalara`
- **THEN** a gravação responde 400, nomeando o adapter repetido
- **AND** nada é gravado

#### Scenario: Leitura do perfil com um conector
- **WHEN** um Admin do tenant-a abre a tela de conectores, e o tenant tem a conta `Avalara` com um conector `Avalara`
- **THEN** a leitura devolve a conta, com o `clientId`, o `tokenUrl` e o estado do segredo, e a lista com aquele
  conector, seus módulos, suas integrações e suas URLs base
- **AND** a resposta não contém o valor do segredo, nem parte dele, nem a referência

#### Scenario: Gravação sem a saída
- **WHEN** o tenant-a tem a conta `Avalara` e um conector, e uma gravação chega só com a entrada, sem a conta e sem a
  lista de conectores
- **THEN** a gravação é aceita, e a conta e o conector continuam como estavam

### Requirement: A plataforma e o adapter gravados são os nomes declarados

A gravação MUST resolver a plataforma de cada conta e o adapter de cada conector contra os nomes **declarados** pelas
implementações de adapter registradas, casando sem distinguir maiúscula, e MUST gravar a forma declarada. Cada adapter
declara o próprio nome e a plataforma a que pertence: plataforma e adapter são nomes diferentes, que hoje coincidem só
porque a Avalara tem um adapter só. As leituras comparam o nome exato; gravar a forma declarada é o que as mantém
achando o que foi gravado.

- **Plataforma que nenhuma implementação declara** MUST ser recusada com HTTP 400, nomeando a pedida e as declaradas.
- **Adapter que a plataforma da conta não declara** MUST ser recusado com HTTP 400, nomeando o pedido e os adapters
  declarados para aquela plataforma.
- Na recusa, nada é gravado, nem no cofre nem no perfil.

#### Scenario: Nome em outra caixa é gravado como o declarado
- **WHEN** um Admin grava a conta `avalara` com o conector `AVALARA`
- **THEN** a gravação é aceita, e o perfil guarda a conta `Avalara` com o conector `Avalara`
- **AND** o envio, a listagem e o teste de credencial acham a conta e o conector

#### Scenario: Plataforma que ninguém declara
- **WHEN** um Admin grava a conta `Mock`
- **THEN** a gravação responde 400, nomeando `Mock` e as plataformas declaradas
- **AND** nada é gravado

#### Scenario: Adapter que a plataforma não declara
- **WHEN** um Admin grava, na conta `Avalara`, um conector `Sovos`
- **THEN** a gravação responde 400, nomeando `Sovos` e os adapters que a plataforma `Avalara` declara
- **AND** nada é gravado

### Requirement: O conector tem uma URL base por versão de API

Um conector MUST guardar **um mapa de URLs base, com uma entrada por versão de API** que o adapter declara — e MUST NOT
ter um campo de URL base único, nem campos fixos por versão. A Avalara usa duas versões; outro adapter pode usar uma ou
três, e o nome de cada versão é do adapter.

**A chave da versão é um rótulo.** Ela escolhe qual base usar, e MUST NOT entrar na URL: nada concatena a chave na base
nem no caminho. A base guardada é a que a plataforma publica, e o prefixo da API fica no caminho da integração. Na
Avalara, a base da `v2` é a raiz do host, e o caminho do envio fiscal continua `taxcompliance/v2/fiscal/dfe`.

- **Na gravação:** uma versão que o adapter **não declara** MUST ser recusada com HTTP 400, nomeando a versão e as
  declaradas. Uma versão exigida por alguma integração ligada e **ausente** MUST ser recusada, nomeando a versão e as
  integrações que a usam.
- **As URLs**, as bases e o `tokenUrl` da conta, seguem a regra de endereço de `avalara-tenant-authentication`: `https`,
  ou `http` só em loopback. Uma URL fora da regra MUST ser recusada **já na gravação**, com HTTP 400 que nomeia o campo,
  e não só no envio.
- **Uma versão declarada que nenhuma integração ligada usa** MUST ser aceita vazia, e não exigida.

#### Scenario: Só o Fiscal ligado
- **WHEN** o conector `Avalara` do tenant-a tem só o módulo Fiscal ligado, cuja integração usa a versão `v2`, e a
  gravação traz só a base da `v2`
- **THEN** a gravação é aceita

#### Scenario: Liga o Contábil sem a base que ele usa
- **WHEN** a gravação liga o módulo Contábil, cujas integrações usam a versão `v1`, e o mapa de bases não tem a `v1`
- **THEN** a gravação responde 400, nomeando a `v1` e as integrações do Contábil que a usam
- **AND** nada é gravado

#### Scenario: Versão que o adapter não declara
- **WHEN** a gravação traz uma base para a versão `v3`, e o adapter declara só `v1` e `v2`
- **THEN** a gravação responde 400, nomeando a `v3` e as versões declaradas

#### Scenario: URL sem TLS na gravação
- **WHEN** a gravação traz a base da `v2` como `http://sandbox.exemplo.com/`
- **THEN** a gravação responde 400, nomeando a base da `v2` e a exigência de `https`
- **AND** nada é gravado

#### Scenario: A versão escolhe a base e não entra na URL
- **WHEN** a base da `v2` é `https://api.exemplo.com/`, e a integração de notas fiscais usa a `v2` com o caminho
  `taxcompliance/v2/fiscal/dfe`
- **THEN** o envio vai a `https://api.exemplo.com/taxcompliance/v2/fiscal/dfe`
- **AND** nenhum segmento `v2` é acrescentado pela chave da versão

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

### Requirement: No máximo um conector ligado por integração

Dentro de um tenant, uma integração MUST ter no máximo um conector com ela ligada, contando todas as contas.

- **Na gravação:** dois conectores ligados na mesma integração MUST ser recusados com HTTP 400, nomeando a integração e
  os dois conectores. Não é escolha em runtime nem "o primeiro ganha". A regra é essa validação; o banco guarda a mesma
  unicidade, por tenant e integração, como rede de segurança, e uma escrita que escape da validação falha em vez de
  gravar a duplicidade.
- **Em runtime:** o adapter que despacha MUST achar o **próprio** conector na coleção do tenant, pela comparação exata
  do nome do adapter, e a integração que a operação precisa dentro dele. Escolher entre conectores — qual deles atende
  uma integração — não é desta mudança: com um conector por adapter, essa escolha é nula.
- **O conector do adapter ausente, ou sem a integração ligada,** MUST ser tratado como configuração do conector, pelo
  mesmo caminho da credencial ausente: o documento é registrado como rejeitado, com motivo que aponta a tela, e nenhuma
  requisição sai.

Integrações distintas do mesmo módulo MUST poder estar em conectores distintos — é o que permite um cliente capturar
notas por um conector e emiti-las por outro.

#### Scenario: Dois conectores na mesma integração
- **WHEN** uma gravação liga a integração de notas fiscais em dois conectores do tenant-a
- **THEN** a gravação responde 400, nomeando a integração e os dois conectores
- **AND** nada é gravado

#### Scenario: O despacho usa o próprio conector
- **WHEN** o tenant-a tem o conector `Avalara` com a integração de notas fiscais ligada e com caminho, e uma nota é
  despachada pelo adapter `Avalara`
- **THEN** o envio usa aquele conector: a base da versão que a integração usa, e o caminho dela

#### Scenario: O conector sem a integração
- **WHEN** o conector `Avalara` do tenant-a não tem a integração de notas fiscais ligada, e uma nota é despachada
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia a integração e
  aponta a tela
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Sem o conector do adapter
- **WHEN** o tenant-a não tem conector do adapter `Avalara`, e uma nota é despachada por ele
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que aponta a tela
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Duas integrações do mesmo módulo em conectores diferentes
- **WHEN** o tenant-a tem duas integrações declaradas do módulo Fiscal, cada uma ligada num conector diferente
- **THEN** a gravação é aceita

### Requirement: Cada integração ligada tem endpoint, e tamanho de lote onde a API aceita

Uma integração ligada MUST guardar o **caminho do endpoint**, como texto, relativo à URL base da versão que ela usa. A
chave da versão escolhe a base e não entra no caminho (ver "O conector tem uma URL base por versão de API").

O caminho de uma integração MUST vir da integração, e de nenhum outro lugar: nem de código, nem da configuração do
host. O envio e a consulta de status leem o caminho da integração (`compliance-dispatch-outcome`). A única leitura da
configuração do host é o preenchimento de uma vez que a migração deixa pendente (ver "O caminho que a migração não sabe
vem do host, uma vez").

Onde o adapter declara que a integração **aceita lote**, a integração MUST guardar também o **tamanho do lote** —
quantos registros vão por requisição. Onde o adapter declara que não aceita:

- a gravação de um tamanho de lote MUST ser recusada com HTTP 400, nomeando a integração;
- a leitura MUST NOT devolver o campo.

O tamanho do lote MUST ser por integração, e MUST NOT ser um valor único do conector: o que cabe num lote depende do
tamanho do registro.

#### Scenario: Lote por integração
- **WHEN** o Admin grava duas integrações com lote, uma com 100 registros e outra com 50
- **THEN** as duas são gravadas com os tamanhos próprios

#### Scenario: Lote numa integração que não aceita
- **WHEN** a gravação traz um tamanho de lote na integração de notas fiscais, que o adapter declara sem lote
- **THEN** a gravação responde 400, nomeando a integração
- **AND** nada é gravado

#### Scenario: A leitura de uma integração sem lote
- **WHEN** um Admin lê o perfil, e a integração de notas fiscais não aceita lote
- **THEN** a resposta traz o caminho dela e nenhum campo de tamanho de lote

### Requirement: Uma integração sem caminho fica inativa

Uma integração ligada com o caminho **vazio** MUST ser tratada como **inativa**, e a gravação MUST ser aceita assim.

- **A gravação MUST NOT ser bloqueada** por caminho em branco: quem configura raramente tem todos os caminhos na
  primeira sessão, e bloquear produz conector pela metade.
- **A leitura MUST dizer quais integrações ligadas estão sem caminho**, para a tela mostrar a pendência.
- **Em runtime**, uma integração inativa MUST ser tratada como configuração do conector, pelo mesmo caminho da
  credencial ausente, e MUST NOT gerar requisição.
- **Não existe campo "ativa".** O estado é derivado do caminho.

#### Scenario: Gravar com caminho em branco
- **WHEN** o Admin liga o módulo Contábil com duas integrações e deixa os dois caminhos em branco
- **THEN** a gravação é aceita
- **AND** a leitura do perfil diz que as duas integrações estão sem caminho

#### Scenario: Despachar por uma integração inativa
- **WHEN** uma operação precisa de uma integração ligada cujo caminho está em branco
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia a integração e
  aponta a tela
- **AND** nenhuma requisição é feita

### Requirement: O ambiente é da instância, e nunca do perfil

Cada instância do hub atende **um cliente e um ambiente** (ADR-0036 §3, revisando o ADR-0020). O ambiente MUST vir da
configuração do host, em `Connector:Environment`, e MUST NOT ser campo do perfil de conector.

- **Na subida:** o host MUST recusar subir sem o ambiente configurado, ou com um valor que não é `Sandbox` nem
  `Production`.
- **Não existe padrão silencioso**, em nenhum leitor: o host não sobe com um valor presumido, o `/info` não completa o
  valor que falta, e a tela MUST NOT afirmar ambiente nenhum antes de o `/info` responder.
- **Os exemplos da tela seguem o ambiente:** o exemplo de uma URL da plataforma — o endpoint de token, as URLs base —
  MUST ser o do ambiente da instância, e MUST NOT haver exemplo antes de o `/info` responder. Numa instância de sandbox,
  um exemplo de produção é o que se copia com pressa, e autentica contra produção.
- **Não é o ambiente do ASP.NET.** O eixo Development/Staging/Production diz como o host se comporta; este diz contra
  que ambiente da plataforma de compliance o cliente integra. Os dois MUST ser independentes, e nada MUST derivar um do
  outro.
- **Na leitura:** o `/info` MUST devolver o ambiente da instância.
- **Na gravação:** o `PUT /connector` MUST NOT aceitar o ambiente como dado da requisição. Um corpo que ainda traga o
  campo tem o campo **ignorado**, e não recusado.
- **As settings de saída MUST NOT ter seção por ambiente.**

#### Scenario: Host sem o ambiente configurado
- **WHEN** o host sobe sem `Connector:Environment`
- **THEN** a subida falha com uma mensagem que nomeia `Connector:Environment`

#### Scenario: Host com um ambiente que não existe
- **WHEN** o host sobe com o ambiente configurado como `Homologacao`
- **THEN** a subida falha, e a mensagem diz quais são os dois valores aceitos

#### Scenario: Development atendendo o Sandbox
- **WHEN** o host roda em Development com o ambiente configurado como `Sandbox`
- **THEN** o `/info` devolve `Sandbox`
- **AND** nada no comportamento do conector é derivado do ambiente do ASP.NET

#### Scenario: A tela antes do /info
- **WHEN** o dashboard abre e o `/info` ainda não respondeu, ou falhou
- **THEN** o selo de ambiente não mostra `Sandbox` nem `Production`

#### Scenario: O exemplo do endpoint de token numa instância de sandbox
- **WHEN** o Admin abre a conta `Avalara` numa instância cujo `/info` responde `Sandbox`
- **THEN** o exemplo do endpoint de token é `https://api-gateway.sandbox.avalarabrasil.com.br/oauth/token`, e o das
  URLs base é `https://api-gateway.sandbox.avalarabrasil.com.br/`
- **AND** numa instância de `Production`, os exemplos são os de produção, e nenhum dos dois aparece no outro ambiente

#### Scenario: Corpo antigo com o ambiente
- **WHEN** uma gravação chega com `"environment": "Production"` no corpo, numa instância de `Sandbox`
- **THEN** a gravação é aceita, o campo é ignorado
- **AND** o perfil gravado não guarda ambiente nenhum, e a instância continua em `Sandbox`

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

### Requirement: O caminho que a migração não sabe vem do host, uma vez

A migração não lê a configuração do host, e o caminho do envio fiscal mora hoje nela (`Avalara:DocumentsPath`). Por isso
a migração deixa o caminho da integração de notas fiscais **à espera do host**, e o host, na subida, MUST preenchê-lo com
o valor de `Avalara:DocumentsPath` — o mesmo que o envio usa antes desta mudança.

- **Só o caminho à espera:** um caminho já preenchido MUST NOT ser sobrescrito, e um caminho em branco gravado pela tela
  MUST NOT ser preenchido. Em branco é a integração inativa que alguém escolheu.
- **Uma vez:** preenchido, o caminho passa a ser só da tela, e a configuração do host deixa de valer para ele.
- **Fora desse preenchimento**, nada no envio nem na consulta de status lê `Avalara:DocumentsPath`.

#### Scenario: A primeira subida depois da migração
- **WHEN** a migração deixou à espera o caminho da integração de notas fiscais do tenant-a, e o host sobe com
  `Avalara:DocumentsPath` = `taxcompliance/v2/fiscal/dfe`
- **THEN** a integração passa a ter o caminho `taxcompliance/v2/fiscal/dfe`
- **AND** a leitura do perfil o mostra, e o envio seguinte vai a ele

#### Scenario: O caminho em branco escolhido na tela não volta
- **WHEN** o Admin gravou o caminho da integração de notas fiscais em branco, e o host sobe de novo
- **THEN** o caminho continua em branco, e a integração continua inativa

#### Scenario: O caminho preenchido não é trocado
- **WHEN** a integração de notas fiscais tem o caminho `taxcompliance/v2/fiscal/dfe`, e o host sobe com
  `Avalara:DocumentsPath` = `documents`
- **THEN** o caminho continua `taxcompliance/v2/fiscal/dfe`

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
