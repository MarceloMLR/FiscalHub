## ADDED Requirements

### Requirement: Os módulos vêm dos conectores do tenant

Os módulos que um tenant tem MUST ser a **união dos módulos ligados nos conectores dele** (`platform-account-connectors`),
e MUST NOT ser uma lista gravada no tenant.

- **Os valores aceitos:** `Fiscal`, `Contabil` e `Inventario`. Quem os declara é o adapter de cada conector; o perfil
  liga os contratados.
- **O padrão:** um tenant sem conector nenhum, ou sem módulo ligado em conector algum, tem só o `Fiscal`. É o
  comportamento de antes desta mudança.
- **A forma da resposta não muda:** o `/info` MUST continuar respondendo `modules` com uma **lista plana**, para
  qualquer papel, porque é por ela que a barra lateral é montada. A leitura do perfil (`GET /connector`) devolve os
  módulos dentro de cada conector, que é onde eles agora moram.
- **A ordem:** a lista MUST vir na ordem Fiscal, Contábil, Inventário, qualquer que seja a ordem dos conectores. Um
  módulo ligado em mais de um conector conta uma vez só.
- **A gravação:** o `PUT /connector` MUST NOT aceitar uma lista de módulos no nível do tenant. Um corpo que ainda traga
  o campo MUST ser **recusado** com HTTP 400, que diz que os módulos são ligados em cada conector e manda recarregar a
  tela — como a seção por ambiente nas settings. Aceitar e descartar faria o operador acreditar que configurou algo. Um
  corpo sem o campo mantém os módulos dos conectores.
- **O tenant:** vale só para o tenant do usuário logado.

#### Scenario: Tenant sem conector
- **WHEN** um tenant não tem conector nenhum, e um Viewer dele chama o `/info`
- **THEN** a resposta traz `modules = ["Fiscal"]`

#### Scenario: Dois conectores, módulos somados
- **WHEN** o tenant-a tem um conector com o Fiscal ligado e outro com o Fiscal e o Inventário
- **THEN** o `/info` responde `modules = ["Fiscal", "Inventario"]`

#### Scenario: Admin liga o Inventário num conector
- **WHEN** o Admin do tenant-a liga o módulo Inventário no conector `Avalara` e salva
- **THEN** o `/info` do tenant-a passa a responder `modules = ["Fiscal", "Inventario"]`
- **AND** a barra lateral mostra Inventário, sem recarregar a página

#### Scenario: Corpo antigo com a lista do tenant
- **WHEN** uma gravação chega com `modules = ["Fiscal", "Contabil"]` no nível do tenant
- **THEN** a gravação responde 400, dizendo que os módulos são ligados em cada conector e mandando recarregar a tela
- **AND** nada é gravado, e o `/info` continua respondendo a união dos módulos dos conectores

#### Scenario: Só o tenant do usuário
- **WHEN** o Admin do tenant-a desliga o Contábil no conector dele, e o tenant-c tem o Contábil ligado
- **THEN** o `/info` do tenant-c continua trazendo `Contabil`

## MODIFIED Requirements

### Requirement: A marcação em Configurações

Em Configurações, o Admin MUST marcar os módulos **dentro do painel de cada conector**, e não numa aba do nível do
cliente. Cada conector mostra os módulos que o adapter dele declara, com uma caixa de marcar cada, marcadas conforme o
perfil gravado.

- **A aba "Módulos" do nível do cliente deixa de existir.** Com dois conectores ela mentiria sobre a quem se aplica: o
  mesmo módulo pode estar ligado num conector e não no outro.
- **Um módulo que o adapter do conector não declara** MUST aparecer apagado, dizendo que aquele conector não o atende —
  e não some da lista. Apagado com motivo responde a pergunta; ausente faz procurar.
- **A gravação** continua pelo mesmo botão de salvar do perfil. Os módulos marcados só valem depois de salvos.

#### Scenario: Marcar e salvar
- **WHEN** o Admin abre o painel do conector `Avalara`, marca o Contábil e salva
- **THEN** o conector passa a ter o Contábil, e a barra lateral o mostra

#### Scenario: Marcar sem salvar
- **WHEN** o Admin marca o Contábil no painel do conector e o fecha sem salvar
- **THEN** o perfil e a barra lateral ficam como estavam

#### Scenario: Módulo que o conector não atende
- **WHEN** o Admin abre o painel de um conector cujo adapter não declara o Inventário
- **THEN** o Inventário aparece apagado, dizendo que aquele conector não o atende
- **AND** a caixa dele não pode ser marcada

### Requirement: Esconder um módulo é apresentação, e não permissão

Os módulos decidem o que a barra lateral mostra, e não o acesso à API. A API MUST continuar respondendo para um módulo
escondido, com as mesmas regras de papel e de tenant de antes. Restringir o acesso por módulo é outra fatia.

O módulo continua sem comportamento próprio (ADR-0037 §1). Quem decide o envio é a **integração** ligada no conector
(`platform-account-connectors`), e uma integração só fica ligada junto com o módulo dela. Por isso, desligar um módulo
num conector desliga as integrações dele, e um envio que dependia de uma delas passa a ser rejeitado como configuração
do conector. Isso é efeito da integração desligada, e não do módulo escondido: a leitura do que já foi enviado continua
respondendo.

#### Scenario: Fiscal escondido
- **WHEN** o tenant-a tem só o módulo Inventário, e um usuário do tenant-a chama o `/groups` direto
- **THEN** a resposta é a mesma de um tenant com o Fiscal, com os grupos do tenant-a
- **AND** a barra lateral do tenant-a não mostra o Fiscal

#### Scenario: O Fiscal desligado no conector
- **WHEN** o Admin do tenant-a desliga o módulo Fiscal no conector `Avalara` e salva, e uma nota do tenant-a é
  despachada
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia a integração de
  notas fiscais e aponta a tela
- **AND** nenhuma requisição é feita à plataforma
- **AND** o `/groups` do tenant-a continua respondendo os grupos já gravados

## REMOVED Requirements

### Requirement: Os módulos do tenant ficam no perfil

**Reason**: os módulos eram uma lista gravada no tenant e marcada numa aba do nível do cliente. Com uma coleção de
conectores (ADR-0036) e módulos declarados por adapter (ADR-0037 §7), isso deixa de fazer sentido: o AvaTax não atende
nenhum módulo do Contábil e o Tax Compliance atende os três, e uma lista no tenant não sabe dizer a qual conector cada
módulo pertence. Os cenários "Perfil sem módulos", "Admin marca o Inventário", "Cliente antigo sem o campo", "Valor
desconhecido", "Lista vazia" e "Só o tenant do Admin" descreviam a gravação dessa lista.

**Migration**: substituído por "Os módulos vêm dos conectores do tenant", que mantém a **forma da resposta do `/info`**
— a mesma lista plana, na mesma ordem — e troca só a fonte. A barra lateral não muda nesta fatia; o reagrupamento por
conector é do ADR-0039. A migração do perfil leva os módulos gravados no tenant para o conector único que ela cria
(`platform-account-connectors`).
