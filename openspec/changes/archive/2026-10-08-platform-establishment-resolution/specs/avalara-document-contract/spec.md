## MODIFIED Requirements

### Requirement: Códigos da empresa por estabelecimento e ambiente

`codigoEmpresa` e `codigoContribuinte` MUST vir da configuração do tenant ou da plataforma, no ambiente ativo, para o
CNPJ do estabelecimento próprio do documento (`platform-establishment-resolution`):

- **A sobreposição:** a entrada da tabela `establishments` das settings de saída, cuja chave é o CNPJ do estabelecimento
  próprio. Quando há entrada, ela vale.
- **A plataforma:** sem entrada, o `codigoCIA` da empresa e o `codigo` do único contribuinte da plataforma com esse CNPJ
  (`avalara-establishment-listing`).
- **O que não vale:** eles MUST NOT vir do ERP, mesmo quando o documento traz um valor parecido, como o
  próprio CNPJ do estabelecimento.
- **Comparação da chave:** é feita pelo CNPJ normalizado dos dois lados (`tax-identifier-normalization`): sem
  pontuação, com as letras e a caixa como vieram.

Se faltar o perfil ou a seção do ambiente, ou se a entrada da tabela não tiver um dos dois campos, o envio MUST ser
rejeitado antes de qualquer requisição à plataforma. O motivo MUST citar o tenant, o ambiente, o CNPJ normalizado e cada
campo que falta.

A tabela ausente MUST NOT ser, por si só, motivo de rejeição. As recusas da resolução pela plataforma (sem contribuinte,
mais de um, sem código, falha da listagem) são as de `platform-establishment-resolution`.

#### Scenario: Os códigos saem da configuração, e não do ERP
- **WHEN** o estabelecimento próprio da nota tem CNPJ `44278225000180`, e a tabela do ambiente ativo
  traduz esse CNPJ para `codigoEmpresa = 20247332000182` e `codigoContribuinte = 20247332000182`
- **THEN** o payload leva `codigoEmpresa` e `codigoContribuinte` iguais a `20247332000182`
- **AND** nenhum dos dois é `44278225000180`

#### Scenario: Os códigos saem da plataforma
- **WHEN** a tabela do ambiente ativo não tem o CNPJ `44278225000180`, e o único contribuinte da plataforma com esse CNPJ
  tem `codigo = "010"`, na empresa de `codigoCIA = "017"`
- **THEN** o payload leva `codigoEmpresa = "017"` e `codigoContribuinte = "010"`
- **AND** nenhum dos dois é `44278225000180`

#### Scenario: O ambiente ativo escolhe a tabela
- **WHEN** o perfil está em `Production`, e as tabelas de `sandbox` e `production` traduzem o mesmo CNPJ
  para códigos diferentes
- **THEN** o payload leva os códigos da tabela de `production`

#### Scenario: Sem a tabela, com a plataforma
- **WHEN** a seção do ambiente ativo não tem `establishments`, e a plataforma tem um único contribuinte com o CNPJ do
  estabelecimento próprio
- **THEN** o envio segue com os códigos desse contribuinte

#### Scenario: Estabelecimento sem tradução
- **WHEN** o CNPJ do estabelecimento próprio não está na tabela do ambiente ativo, e nenhum contribuinte da plataforma tem
  esse CNPJ
- **THEN** o envio é rejeitado com motivo que cita o tenant, o ambiente e o CNPJ
- **AND** nenhuma requisição de envio é feita à plataforma

#### Scenario: Um dos dois códigos falta
- **WHEN** a entrada do estabelecimento tem `codigoEmpresa` e não tem `codigoContribuinte`
- **THEN** o envio é rejeitado com motivo que nomeia `codigoContribuinte`

#### Scenario: Chave com CNPJ alfanumérico
- **WHEN** a tabela do ambiente ativo tem a chave `12.ABC.345/01DE-35`, e o estabelecimento próprio da nota é
  `12ABC34501DE35`
- **THEN** os códigos dessa entrada vão no payload

#### Scenario: Dois CNPJs alfanuméricos com os mesmos dígitos
- **WHEN** a tabela tem só a chave `12ABC34501DE35`, a plataforma não tem contribuinte com `12XYZ34501DE35`, e o
  estabelecimento próprio da nota é `12XYZ34501DE35`
- **THEN** o envio é rejeitado por falta de tradução, com motivo que cita `12XYZ34501DE35`
- **AND** os códigos de `12ABC34501DE35` não são usados

### Requirement: Estabelecimento próprio e parceiro

O estabelecimento próprio do documento MUST ser determinado assim:

- **A nota diz se é de emissão própria ou de terceiros:** o estabelecimento próprio é o emitente na
  emissão própria, e o destinatário na emissão de terceiros.
- **A nota não diz:** o estabelecimento próprio é a única parte que é estabelecimento do tenant. Uma parte é estabelecimento
  do tenant quando o CNPJ normalizado dela tem entrada na tabela do ambiente ativo, ou tem ao menos um contribuinte na
  plataforma (`platform-establishment-resolution`). Se nenhuma parte ou as duas forem, o envio MUST ser rejeitado antes de
  qualquer requisição de envio, com motivo que cita os CNPJs das duas partes.

O `parceiro` do payload MUST ser a outra parte. Ele leva:

- **`nome`;**
- **documento:** o documento normalizado (`tax-identifier-normalization`). Ele vai em `cnpj` quando tem 14 caracteres,
  sejam dígitos ou letras, e em `cpf` quando tem 11. Em qualquer outro caso, não vai nenhum dos dois;
- **endereço:** `endereco`, `numero`, `bairro` e `cep`, só os que o documento tiver. O `cep` vai só com
  dígitos.

#### Scenario: Nota de entrada de terceiro
- **WHEN** a nota é de emissão de terceiros, com o fornecedor Proseware como emitente e o estabelecimento
  Contoso como destinatário
- **THEN** o parceiro do payload é a Proseware

#### Scenario: Nota de saída própria
- **WHEN** a nota é de emissão própria, com o estabelecimento como emitente e o cliente como destinatário
- **THEN** o parceiro do payload é o cliente

#### Scenario: A nota não diz qual lado é o nosso
- **WHEN** uma NF-e em XML chega sem a indicação de emissão própria ou de terceiros, só o CNPJ do emitente está na tabela
  do ambiente ativo, e o destinatário não tem contribuinte na plataforma
- **THEN** o emitente é o estabelecimento próprio e o destinatário é o parceiro

#### Scenario: Só a plataforma conhece o emitente
- **WHEN** uma NF-e em XML chega sem a indicação, a tabela do ambiente ativo está vazia, o emitente tem um contribuinte na
  plataforma e o destinatário não tem
- **THEN** o emitente é o estabelecimento próprio e o destinatário é o parceiro

#### Scenario: As duas partes estão na tabela e a nota não diz qual é a nossa
- **WHEN** a nota não traz a indicação e os CNPJs do emitente e do destinatário estão ambos na tabela
- **THEN** o envio é rejeitado com motivo que cita os dois CNPJs

#### Scenario: Uma parte na tabela, a outra na plataforma
- **WHEN** a nota não traz a indicação, o emitente está na tabela, e o destinatário tem um contribuinte na plataforma
- **THEN** o envio é rejeitado com motivo que cita os dois CNPJs

#### Scenario: Nenhuma parte é do tenant
- **WHEN** a nota não traz a indicação, nenhuma das duas partes está na tabela, e nenhuma tem contribuinte na plataforma
- **THEN** o envio é rejeitado com motivo que cita os dois CNPJs

#### Scenario: Parceiro estrangeiro sem documento brasileiro
- **WHEN** a contraparte da nota de importação não tem CNPJ nem CPF
- **THEN** o parceiro vai sem `cnpj` e sem `cpf`, com o nome e o endereço que houver

#### Scenario: Parceiro com CNPJ alfanumérico
- **WHEN** o parceiro da nota tem o CNPJ `12.ABC.345/01DE-35`
- **THEN** o parceiro do payload leva `cnpj = 12ABC34501DE35`, e não vai sem documento
