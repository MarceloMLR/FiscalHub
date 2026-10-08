# platform-establishment-resolution Specification

## Purpose

Traduzir o estabelecimento próprio da nota para os códigos da plataforma de compliance sem cadastro manual. O hub casa o
CNPJ com o que a própria plataforma lista, e recusa onde a tradução não existe ou onde haveria escolha. Uma escolha
errada não falharia: a nota seria escriturada no contribuinte errado.

## Requirements

### Requirement: A listagem é uma capacidade declarada pelo adapter de saída

A resolução pela plataforma MUST acontecer só quando o adapter de saída do perfil do tenant declara que sabe listar os
estabelecimentos da plataforma. A declaração é do adapter, pela comparação exata do nome gravado no perfil, como no teste
de credencial (`connector-credential-test`). O contrato de despacho MUST NOT exigir a listagem de nenhum destino.

- **Destino que não sabe listar:** a sobreposição `establishments` é a única fonte, como antes desta mudança. O
  estabelecimento sem entrada é rejeitado com o motivo de falta de tradução, que cita o tenant, o ambiente, o CNPJ e os
  campos que faltam.
- **Destino que sabe listar:** a ausência da tabela `establishments` na seção do ambiente MUST NOT ser motivo de rejeição.
  Ela vale como uma sobreposição vazia.

#### Scenario: Destino que não sabe listar
- **WHEN** o adapter de saída do perfil não declara a listagem, e o CNPJ do estabelecimento próprio não está na tabela
  `establishments` do ambiente ativo
- **THEN** o envio é rejeitado por falta de tradução, com motivo que cita o tenant, o ambiente e o CNPJ
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Destino que sabe listar, sem a tabela
- **WHEN** o adapter de saída declara a listagem, a seção do ambiente ativo não tem `establishments`, e a plataforma tem
  um único contribuinte com o CNPJ do estabelecimento próprio
- **THEN** o envio segue com os códigos desse contribuinte

### Requirement: A sobreposição manual ganha da plataforma

Quando a tabela `establishments` do ambiente ativo tem uma entrada para o CNPJ normalizado do estabelecimento próprio, os
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

### Requirement: O casamento é pelo CNPJ normalizado dos dois lados

O CNPJ do estabelecimento próprio e o CNPJ de cada contribuinte da plataforma MUST ser normalizados pela mesma regra
(`tax-identifier-normalization`) e comparados exatamente, com as letras e a caixa como vieram.

- **O contribuinte sem CNPJ** nunca casa.
- **Só o CNPJ participa.** Os códigos, os identificadores da plataforma e a ordem do retorno MUST NOT participar do
  casamento.

#### Scenario: O F&O com traço, a plataforma só com dígitos
- **WHEN** o estabelecimento próprio vem do F&O como `442782250001-80`, e a plataforma devolve um contribuinte com
  `cnpj = 44278225000180`
- **THEN** os dois casam

#### Scenario: CNPJ alfanumérico nas duas pontas
- **WHEN** o estabelecimento próprio é `12.ABC.345/01DE-35`, e a plataforma devolve um contribuinte com
  `cnpj = 12ABC34501DE35`
- **THEN** os dois casam, com o valor `12ABC34501DE35` nas duas pontas

#### Scenario: A caixa não é convertida
- **WHEN** o estabelecimento próprio é `12ABC34501DE35`, e o único contribuinte parecido na plataforma tem
  `cnpj = 12abc34501de35`
- **THEN** os dois não casam, e o envio é rejeitado como estabelecimento sem contribuinte na plataforma

### Requirement: Nenhum contribuinte com o CNPJ é recusa nomeando o CNPJ

Quando nem a sobreposição nem a listagem completa da plataforma têm o CNPJ do estabelecimento próprio, o envio MUST ser
rejeitado antes da requisição de envio. A rejeição é da tradução, e não do conteúdo fiscal (ADR-0026). O motivo MUST:

- identificar o problema como configuração do conector;
- citar o tenant, o ambiente e o CNPJ normalizado;
- dizer que a plataforma não tem contribuinte com esse CNPJ;
- apontar as duas saídas: cadastrar o contribuinte na plataforma, ou traduzir o CNPJ na tabela `establishments` do
  ambiente;
- dizer que salvar o perfil do conector faz o hub reler a plataforma.

O motivo MUST NOT ser um erro genérico, nem o texto cru de uma exceção.

#### Scenario: CNPJ sem cadastro na plataforma
- **WHEN** o estabelecimento próprio é `44278225000341`, a tabela não o tem, e nenhum contribuinte da plataforma tem esse
  CNPJ
- **THEN** o envio é rejeitado com motivo de configuração do conector que cita `44278225000341`, o tenant e o ambiente
- **AND** a requisição de envio não é feita
- **AND** a mensagem da fila é concluída sem retentativa

### Requirement: Mais de um contribuinte com o mesmo CNPJ é recusa nomeando os candidatos

Os candidatos de um CNPJ são os contribuintes distintos da plataforma com esse CNPJ normalizado.

- **A identidade:** é o identificador do contribuinte na plataforma. O mesmo contribuinte listado duas vezes conta uma
  vez. Uma entrada sem identificador conta como um contribuinte à parte.
- **A recusa:** com mais de um candidato, o envio MUST ser rejeitado antes da requisição de envio, e nada é despachado.
- **O motivo:** MUST citar o tenant, o ambiente, o CNPJ e o número de candidatos. Ele nomeia até cinco candidatos e diz
  quantos ficaram de fora. Ele aponta as duas saídas: remover a duplicidade na plataforma, ou traduzir o CNPJ na tabela
  `establishments`. Cada candidato é nomeado assim:
  - **a empresa:** pelo código e pela descrição, que bastam para um humano;
  - **o contribuinte:** pelo código e pelo identificador na plataforma, no formato `#<contribuinteId>`. Dois contribuintes
    podem ter o mesmo código na mesma empresa, e é justamente o caso em que a duplicidade mais importa. O identificador
    também é endereço: a plataforma tem a leitura do contribuinte por ele.

  O identificador da empresa MUST NOT entrar no motivo: depois do código e da descrição, ele não acrescenta nada legível.
- **Não há desempate.** O hub MUST NOT escolher entre os candidatos por nenhum critério:
  - a ordem do retorno;
  - a empresa, pelo código ou pela descrição, inclusive as de teste (`Padrão`, `QA`, `SPL`);
  - o código do contribuinte coincidir com a ordem do CNPJ;
  - um candidato ter os códigos completos e o outro não.

#### Scenario: O mesmo CNPJ em duas empresas
- **WHEN** a plataforma tem o CNPJ `44278225000180` no contribuinte `001` (`#50001`) da empresa `017` (`EMPRESA
  EXEMPLO`) e no contribuinte `001` (`#60001`) da empresa `009` (`LABORATORIO`), e a tabela não o tem
- **THEN** o envio é rejeitado com motivo que cita `44278225000180` e nomeia os dois candidatos, como
  `empresa '017' (EMPRESA EXEMPLO), contribuinte '001' (#50001)` e `empresa '009' (LABORATORIO), contribuinte '001' (#60001)`
- **AND** o `empresaId` não aparece no motivo
- **AND** nenhuma requisição de envio é feita

#### Scenario: Dois candidatos com o mesmo código na mesma empresa
- **WHEN** a empresa `017` tem dois contribuintes de código `001` com o mesmo CNPJ, `#50001` e `#50002`
- **THEN** o motivo nomeia os dois, distintos pelo `#50001` e pelo `#50002`

#### Scenario: O código que coincide com a ordem do CNPJ não desempata
- **WHEN** o CNPJ `44278225000260` (ordem `0002`) casa com um contribuinte de código `002` e com outro de código `007`
- **THEN** o envio é rejeitado como duplicidade, nomeando os dois

#### Scenario: O mesmo contribuinte listado duas vezes
- **WHEN** a listagem traz duas vezes o contribuinte de identificador `10001`, com o mesmo CNPJ
- **THEN** ele é um candidato só, e o envio segue com os códigos dele

#### Scenario: A sobreposição resolve a duplicidade
- **WHEN** a plataforma tem dois contribuintes com o CNPJ do estabelecimento próprio, e a tabela `establishments` tem
  uma entrada para esse CNPJ
- **THEN** o envio segue com os códigos da entrada

### Requirement: O contribuinte único precisa dos dois códigos

Quando há um único candidato, mas a empresa dele não tem código, ou o contribuinte não tem código, o envio MUST ser
rejeitado antes da requisição de envio. O motivo nomeia o campo que falta, a empresa e o CNPJ. O código que falta MUST NOT
ser preenchido com outro valor: nem o CNPJ, nem o identificador numérico, nem um dado do ERP.

#### Scenario: Contribuinte sem código
- **WHEN** o único contribuinte com o CNPJ do estabelecimento próprio vem sem `codigo`
- **THEN** o envio é rejeitado com motivo que nomeia o código do contribuinte, a empresa e o CNPJ

### Requirement: A listagem é completa, ou não é usada

A resolução MUST usar só uma listagem completa: todas as empresas e os contribuintes de todas elas, em todas as páginas.
Se qualquer parte da listagem falhar, inclusive uma página, nenhuma nota MUST ser resolvida por ela. A listagem parcial
MUST NOT ser usada para dar os códigos, para declarar que um CNPJ não tem contribuinte, nem para declarar que ele tem um
só. O desfecho da nota é o da falha.

A listagem parcial poderia esconder a duplicidade: o segundo contribuinte estaria justamente na empresa que falhou.

#### Scenario: A terceira empresa falha
- **WHEN** a plataforma lista três empresas, e a leitura dos contribuintes da terceira falha
- **THEN** nenhuma nota do tenant é resolvida pela plataforma, nem a de um CNPJ da primeira empresa
- **AND** o desfecho de cada nota é o da falha da listagem

#### Scenario: A duplicidade na empresa que falhou
- **WHEN** um CNPJ tem um contribuinte na primeira empresa e outro na empresa cuja leitura falhou
- **THEN** a nota desse CNPJ não é enviada com os códigos da primeira empresa

### Requirement: Uma listagem por janela de despacho

A listagem MUST ser guardada por tenant e por ambiente, por uma validade configurável. Sem configuração, a validade é de
10 minutos. Uma validade zero ou negativa MUST impedir o host de subir, porque seria a listagem por nota. A janela de
despacho é essa validade: dentro dela, toda resolução do mesmo tenant e ambiente MUST usar a mesma listagem, sem nova
requisição.

- **A concorrência:** com várias notas do mesmo tenant e ambiente ao mesmo tempo, MUST haver uma listagem só em
  andamento. As outras resoluções recebem o desfecho dela, sucesso ou falha.
- **O vencimento:** a primeira resolução depois da validade lista de novo.
- **O isolamento:** dois tenants nunca dividem uma listagem, e dois ambientes do mesmo tenant também não.

#### Scenario: N notas, uma listagem
- **WHEN** 50 notas do mesmo estabelecimento do tenant-a, no sandbox, são despachadas dentro da validade, e a plataforma
  tem 3 empresas, com cada lista cabendo numa página
- **THEN** a plataforma recebe 2 requisições de empresas e 6 de contribuintes (a página com os itens e a vazia, por
  lista), e não 50 listagens
- **AND** as 50 notas levam os mesmos códigos

#### Scenario: Notas ao mesmo tempo
- **WHEN** 10 notas do tenant-a, no sandbox, pedem a resolução ao mesmo tempo, sem listagem guardada
- **THEN** a plataforma recebe uma listagem só

#### Scenario: Depois da validade
- **WHEN** a validade da listagem do tenant-a no sandbox venceu, e uma nota do tenant-a é despachada
- **THEN** a plataforma é listada de novo

#### Scenario: Dois tenants
- **WHEN** o tenant-a e o tenant-b despacham notas dentro da validade
- **THEN** cada um tem a própria listagem, pedida com a própria credencial

#### Scenario: A troca de ambiente
- **WHEN** o tenant-a tem a listagem do sandbox guardada, e o perfil passa ao ambiente de produção
- **THEN** a primeira nota em produção lista a plataforma de produção

#### Scenario: Validade zero
- **WHEN** a configuração do host traz a validade da listagem igual a zero
- **THEN** o host não sobe, com motivo que nomeia a configuração

### Requirement: Salvar o perfil esquece a listagem do tenant

Toda gravação do perfil de conector do tenant, ou de um segredo dele, MUST esquecer a listagem guardada e a recusa lembrada
da listagem do tenant, em todos os ambientes. Salvar sem mudar nada também esquece: a correção pode ter sido feita na
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
  rejeitada com o motivo da listagem, sem retentativa. A recusa MUST ser lembrada por tenant e ambiente, por um intervalo
  configurável. Sem configuração, ele é de 5 minutos, o mesmo padrão da recusa lembrada da credencial
  (`avalara-tenant-authentication`). Nesse intervalo, as notas do tenant e ambiente são rejeitadas com o mesmo motivo, sem
  nova requisição de listagem.

#### Scenario: A plataforma fora do ar
- **WHEN** a listagem responde HTTP 503
- **THEN** a nota segue o retry nativo
- **AND** a próxima tentativa pede a listagem de novo

#### Scenario: A recusa lembrada
- **WHEN** a listagem do tenant-a no sandbox é recusada com HTTP 403, e mais 20 notas do tenant-a são despachadas dentro
  do intervalo
- **THEN** as 21 notas são rejeitadas com o mesmo motivo
- **AND** a plataforma recebe uma requisição de listagem só

#### Scenario: Salvar esquece a recusa
- **WHEN** a listagem do tenant-a está com a recusa lembrada, e o perfil do tenant-a é salvo
- **THEN** a próxima nota do tenant-a pede a listagem de novo
