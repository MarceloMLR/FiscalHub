## ADDED Requirements

### Requirement: A listagem é declarada pelo conector

A resolução pela plataforma MUST acontecer só quando o adapter que despacha declara que sabe listar os estabelecimentos da
plataforma, e o tenant tem o conector desse adapter. O conector é o do **próprio** adapter, achado na coleção do tenant
pela comparação exata do nome do adapter (`platform-account-connectors`). Escolher entre conectores não é desta mudança.
O contrato de despacho MUST NOT exigir a listagem de nenhum destino.

- **Destino que não sabe listar:** a sobreposição `establishments` do conector é a única fonte, como antes desta
  mudança. O estabelecimento sem entrada é rejeitado com o motivo de falta de tradução, que cita o tenant, o ambiente da
  instância, o CNPJ e os campos que faltam.
- **Destino que sabe listar:** a ausência da tabela `establishments` no conector MUST NOT ser motivo de rejeição.
  Ela vale como uma sobreposição vazia.

#### Scenario: Destino que não sabe listar
- **WHEN** o adapter do conector não declara a listagem, e o CNPJ do estabelecimento próprio não está na tabela
  `establishments` dele
- **THEN** o envio é rejeitado por falta de tradução, com motivo que cita o tenant, o ambiente e o CNPJ
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Destino que sabe listar, sem a tabela
- **WHEN** o adapter do conector declara a listagem, o conector não tem `establishments`, e a plataforma tem
  um único contribuinte com o CNPJ do estabelecimento próprio
- **THEN** o envio segue com os códigos desse contribuinte

#### Scenario: O conector é achado pelo nome do adapter
- **WHEN** o tenant-a tem o conector `Avalara`, e o adapter `Avalara`, que declara a listagem, resolve um estabelecimento
- **THEN** a listagem usa o conector `Avalara` do tenant-a: a base dele e a credencial da conta dele

### Requirement: Uma listagem por janela de despacho, por conector

A listagem MUST ser guardada por tenant e por conector, por uma validade configurável. Sem configuração, a validade é de
10 minutos. Uma validade zero ou negativa MUST impedir o host de subir, porque seria a listagem por nota. A janela de
despacho é essa validade: dentro dela, toda resolução do mesmo tenant e conector MUST usar a mesma listagem, sem nova
requisição.

**O ambiente não entra na chave:** a instância atende um ambiente só, e um segmento de ambiente diria sempre a mesma
coisa. A separação entre sandbox e produção é a separação entre instâncias, com bancos e caches distintos.

- **A concorrência:** com várias notas do mesmo tenant e conector ao mesmo tempo, MUST haver uma listagem só em
  andamento. As outras resoluções recebem o desfecho dela, sucesso ou falha.
- **O vencimento:** a primeira resolução depois da validade lista de novo.
- **O isolamento:** dois tenants nunca dividem uma listagem, e dois conectores do mesmo tenant também não.

#### Scenario: N notas, uma listagem
- **WHEN** 50 notas do mesmo estabelecimento do tenant-a são despachadas dentro da validade, e a plataforma
  tem 3 empresas, com cada lista cabendo numa página
- **THEN** a plataforma recebe 2 requisições de empresas e 6 de contribuintes (a página com os itens e a vazia, por
  lista), e não 50 listagens
- **AND** as 50 notas levam os mesmos códigos

#### Scenario: Notas ao mesmo tempo
- **WHEN** 10 notas do tenant-a pedem a resolução ao mesmo tempo, sem listagem guardada
- **THEN** a plataforma recebe uma listagem só

#### Scenario: Depois da validade
- **WHEN** a validade da listagem do tenant-a venceu, e uma nota do tenant-a é despachada
- **THEN** a plataforma é listada de novo

#### Scenario: Dois tenants
- **WHEN** o tenant-a e o tenant-b despacham notas dentro da validade
- **THEN** cada um tem a própria listagem, pedida com a própria credencial

#### Scenario: Dois conectores do mesmo tenant
- **WHEN** o tenant-a tem dois conectores cujos adapters sabem listar, e os dois resolvem estabelecimentos dentro da
  validade
- **THEN** cada conector tem a própria listagem

#### Scenario: Validade zero
- **WHEN** a configuração do host traz a validade da listagem igual a zero
- **THEN** o host não sobe, com motivo que nomeia a configuração

## MODIFIED Requirements

### Requirement: A sobreposição manual ganha da plataforma

Quando a tabela `establishments` do conector tem uma entrada para o CNPJ normalizado do estabelecimento próprio, os
códigos MUST ser os da entrada. Nesse caso a listagem MUST NOT ser pedida para resolver os códigos daquela nota.

Uma entrada incompleta MUST ser rejeitada nomeando o campo que falta. Ela MUST NOT ser completada pela plataforma: quem
escreveu a entrada disse qual é a tradução, e completar seria escolher por ele.

#### Scenario: A entrada ganha da plataforma
- **WHEN** a tabela traduz o CNPJ `44278225000180` para `codigoEmpresa = MANUAL-E` e `codigoContribuinte = MANUAL-C`, e a
  plataforma tem um contribuinte com esse CNPJ, com outros códigos
- **THEN** o payload leva `MANUAL-E` e `MANUAL-C`

#### Scenario: Com a entrada, a listagem não é pedida
- **WHEN** uma nota de emissão própria tem o estabelecimento próprio na tabela
- **THEN** nenhuma requisição de listagem é feita à plataforma para essa nota

#### Scenario: A entrada incompleta não cai na plataforma
- **WHEN** a entrada do estabelecimento tem `codigoEmpresa` e não tem `codigoContribuinte`, e a plataforma tem um único
  contribuinte com esse CNPJ
- **THEN** o envio é rejeitado com motivo que nomeia `codigoContribuinte`
- **AND** os códigos da plataforma não são usados

### Requirement: Salvar o perfil esquece a listagem do tenant

Toda gravação do perfil de conector do tenant, ou de um segredo dele, MUST esquecer a listagem guardada e a recusa lembrada
da listagem do tenant, em todos os conectores dele. Salvar sem mudar nada também esquece: a correção pode ter sido feita na
plataforma. A listagem dos outros tenants MUST NOT ser afetada. Uma gravação recusada na validação não esquece nada.

#### Scenario: Cadastrar na plataforma e salvar
- **WHEN** uma nota foi rejeitada porque o CNPJ não tinha contribuinte, o contribuinte é cadastrado na plataforma, e o
  perfil do tenant é salvo sem mudanças
- **THEN** a próxima resolução do tenant lista a plataforma de novo
- **AND** a nota reprocessada é enviada com os códigos do contribuinte novo

#### Scenario: Os outros tenants
- **WHEN** o perfil do tenant-a é salvo
- **THEN** a listagem guardada do tenant-b continua valendo

### Requirement: A falha da listagem

A falha da listagem MUST seguir os desfechos que já existem para o envio (`compliance-dispatch-outcome`):

- **A indisponibilidade** (tempo esgotado, rede, 5xx, 429, ou o token do cache recusado): a nota segue o retry nativo e a
  dead-letter. A falha MUST NOT ser guardada: a próxima tentativa lista de novo.
- **A recusa** (a credencial negada, o caminho que não existe, o formato fora do verificado, a paginação que não avança, o
  teto de páginas):
  a nota é
  rejeitada com o motivo da listagem, sem retentativa. A recusa MUST ser lembrada por tenant e conector, por um intervalo
  configurável. O ambiente não entra na chave, pelo mesmo motivo da listagem guardada. Sem configuração, o intervalo é de
  5 minutos, o mesmo padrão da recusa lembrada da credencial (`avalara-tenant-authentication`). Nesse intervalo, as notas
  do tenant que dependem daquele conector são rejeitadas com o mesmo motivo, sem nova requisição de listagem.

#### Scenario: A plataforma fora do ar
- **WHEN** a listagem responde HTTP 503
- **THEN** a nota segue o retry nativo
- **AND** a próxima tentativa pede a listagem de novo

#### Scenario: A recusa lembrada
- **WHEN** a listagem do conector `Avalara` do tenant-a é recusada com HTTP 403, e mais 20 notas do tenant-a são
  despachadas dentro do intervalo
- **THEN** as 21 notas são rejeitadas com o mesmo motivo
- **AND** a plataforma recebe uma requisição de listagem só

#### Scenario: Salvar esquece a recusa
- **WHEN** a listagem do tenant-a está com a recusa lembrada, e o perfil do tenant-a é salvo
- **THEN** a próxima nota do tenant-a pede a listagem de novo

## REMOVED Requirements

### Requirement: A listagem é uma capacidade declarada pelo adapter de saída

**Reason**: o perfil tinha **um** adapter de saída, e a declaração era comparada contra ele. Com uma coleção de
conectores sob a conta de plataforma (ADR-0036), "o adapter de saída" do perfil deixou de existir: o adapter que
despacha acha o próprio conector na coleção, e é a declaração dele que vale.

**Migration**: substituído por "A listagem é declarada pelo conector", com a mesma regra e a mesma comparação exata do
nome do adapter. A tabela `establishments` passa da seção do ambiente para o conector.

### Requirement: Uma listagem por janela de despacho

**Reason**: a listagem era guardada por (tenant, ambiente), e o cenário "A troca de ambiente" descrevia o perfil
passando de sandbox para produção dentro da mesma instância. Com uma instância por (cliente, ambiente), o ambiente é
constante e a dimensão que passa a importar é o conector.

**Migration**: substituído por "Uma listagem por janela de despacho, por conector". A validade, a concorrência e o
isolamento entre tenants não mudam.
