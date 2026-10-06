## MODIFIED Requirements

### Requirement: Os dropdowns mostram a empresa mascarada e o nome

A integração manual e o agendamento MUST mostrar:

- **cada empresa:** o código, mascarado pela regra única de `document-grouping` ("CNPJ formatado na tela"), e o nome. A
  empresa do D365 é o CNPJ completo da matriz, e aparece com a máscara do CNPJ. O rótulo MUST ser o código gravado, só
  mascarado: nenhum rótulo de empresa difere do valor que a integração e o agendamento gravam;
- **cada filial:** com o CNPJ do estabelecimento, se o diretório o tiver, mascarado pela mesma regra e com o código ao
  lado. Sem o CNPJ, como no diretório de exemplo, ela aparece com o código e o nome.

As tabelas da mesma tela mascaram pela mesma regra, e mostram a filial pelo código gravado. A coluna Empresa difere entre
as duas:

- **a tabela de agendamentos** MUST mostrar a empresa gravada, que é o critério pedido;
- **a tabela de execuções** MUST mostrar o CNPJ do estabelecimento que a execução gravou (`period-discovery`, "A execução
  grava o estabelecimento que a descoberta resolveu"), e a empresa gravada quando a execução não tem esse CNPJ. É o caso
  da execução de várias filiais, e o da execução gravada antes do campo.

As duas tabelas MUST NOT consultar o diretório para montar a coluna: o histórico não depende do ERP no ar.

O valor enviado à integração e ao agendamento MUST ser o código sem máscara: o da empresa, que é o CNPJ completo, e o da
filial.

Sem diretório, ou com a leitura em falha, a tela MUST mostrar o motivo no lugar da lista. Ela MUST NOT deixar disparar
nem agendar sem empresa.

#### Scenario: Empresa do D365 no dropdown
- **WHEN** o diretório traz a empresa `44278225000180`, "Contoso Entertainment System Brazil"
- **THEN** o dropdown mostra "44.278.225/0001-80 — Contoso Entertainment System Brazil"
- **AND** a integração é pedida com a empresa `44278225000180`

#### Scenario: Filial do D365 no dropdown
- **WHEN** o diretório traz a filial `SP-01`, com o CNPJ `44278225000260`
- **THEN** o dropdown mostra "44.278.225/0002-60 — SP-01"
- **AND** a integração é pedida com a filial `SP-01`

#### Scenario: Filial sem CNPJ no dropdown
- **WHEN** o diretório de exemplo traz a filial `0001`, "Matriz", sem CNPJ
- **THEN** o dropdown mostra "0001 — Matriz"

#### Scenario: O agendamento na tabela
- **WHEN** a tabela de agendamentos tem um agendamento gravado com a empresa `44278225000180` e a filial `Matriz`
- **THEN** a empresa aparece como "44.278.225/0001-80", o mesmo texto do dropdown, e a filial como "Matriz"

#### Scenario: O agendamento de uma filial na tabela
- **WHEN** a tabela de agendamentos tem um agendamento gravado com a empresa `44278225000180` e a filial `SP-01`
- **THEN** a empresa aparece como "44.278.225/0001-80", e a filial como "SP-01"

#### Scenario: A execução da SP-01 na tabela
- **WHEN** a tabela de execuções tem uma execução gravada com a empresa `44278225000180`, a filial `SP-01` e o CNPJ
  `44278225000260`
- **THEN** a empresa aparece como "44.278.225/0002-60", e a filial como "SP-01"

#### Scenario: A execução de todas as filiais na tabela
- **WHEN** a tabela de execuções tem uma execução gravada com a empresa `44278225000180`, a filial "todas" e sem CNPJ
- **THEN** a empresa aparece como "44.278.225/0001-80"

#### Scenario: A execução gravada antes do CNPJ
- **WHEN** a tabela de execuções tem uma execução gravada antes do campo, com a empresa `44278225000180`, a filial `SP-01`
  e o CNPJ vazio
- **THEN** a empresa aparece como "44.278.225/0001-80", como antes

#### Scenario: A tabela de execuções com o ERP fora do ar
- **WHEN** a leitura do diretório do tenant-a falha, e a tabela de execuções tem a execução da `SP-01` com o CNPJ
  `44278225000260`
- **THEN** a empresa da execução aparece como "44.278.225/0002-60"
- **AND** montar a tabela não faz nenhuma leitura ao ERP

#### Scenario: ERP sem diretório na tela
- **WHEN** a resposta do diretório é "este ERP não tem diretório"
- **THEN** a tela mostra o motivo no lugar do dropdown
- **AND** os botões de executar e de agendar ficam desabilitados
