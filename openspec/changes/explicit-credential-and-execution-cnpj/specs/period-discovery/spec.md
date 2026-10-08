## ADDED Requirements

### Requirement: A execução grava o estabelecimento que a descoberta resolveu

Junto com as notas, a descoberta por período MUST dizer o CNPJ do estabelecimento quando o escopo pedido resolveu
exatamente um estabelecimento do diretório. A execução manual e a agendada MUST gravar esse CNPJ no registro da
execução. Execuções registram fato.

- **De onde vem:** do estabelecimento do diretório (`company-directory`), normalizado como a empresa: sem a pontuação e
  com as letras do CNPJ alfanumérico. Ele é decidido pelo escopo, antes de ler as notas, e não pelas notas achadas.
- **Com zero notas:** a descoberta MUST dizer o CNPJ mesmo quando não acha nenhuma nota. A execução de uma filial que não
  achou nada é tão desta filial quanto a que achou.
- **Sem CNPJ:** a execução grava o campo vazio quando:
  - o escopo tem mais de um estabelecimento;
  - o escopo é vazio, porque a empresa ou a filial não está no diretório;
  - a origem não conhece o CNPJ do estabelecimento, como o catálogo local de exemplo.
- **O agendamento não grava:** o agendamento registra o critério pedido, e não o estabelecimento. Ele não executou, e a
  única fonte seria o formulário.
- **As execuções antigas:** as gravadas antes deste campo ficam com ele vazio. Nada as preenche depois.

#### Scenario: A execução da SP-01
- **WHEN** a integração manual do tenant-a pede a empresa `44278225000180` e a filial `SP-01`
- **THEN** a descoberta diz o CNPJ `44278225000260`
- **AND** a execução é gravada com a empresa `44278225000180`, a filial `SP-01` e o CNPJ `44278225000260`

#### Scenario: A filial que não achou nota
- **WHEN** um agendamento do tenant-a da empresa `44278225000180` e da filial `SP-01` roda num período sem nota da `SP-01`
- **THEN** nenhuma nota é enfileirada
- **AND** a execução é gravada com o CNPJ `44278225000260`

#### Scenario: Todas as filiais
- **WHEN** a integração manual pede a empresa `44278225000180` e a filial "todas", e a raiz `44278225` tem quatro
  estabelecimentos no diretório
- **THEN** a execução é gravada sem CNPJ

#### Scenario: A raiz com um estabelecimento só
- **WHEN** a integração manual pede a filial "todas" de uma empresa cuja raiz tem um estabelecimento só no diretório
- **THEN** a execução é gravada com o CNPJ desse estabelecimento

#### Scenario: A filial fora do diretório
- **WHEN** a integração manual pede a empresa `44278225000180` e uma filial que o diretório não tem
- **THEN** nenhuma nota é enfileirada, e a execução é gravada sem CNPJ

#### Scenario: A nota com outro CNPJ da mesma raiz
- **WHEN** a integração manual pede a filial `SP-01`, e a única nota achada traz o CNPJ `442782250099-99` no
  estabelecimento próprio
- **THEN** a execução é gravada com o CNPJ `44278225000260`, o do diretório, e não com o da nota

#### Scenario: O catálogo local de exemplo
- **WHEN** o catálogo local responde a uma integração manual em Development, com a filial `0001`
- **THEN** a execução é gravada sem CNPJ

#### Scenario: O agendamento continua sem o CNPJ
- **WHEN** o Admin agenda a empresa `44278225000180` e a filial `SP-01`
- **THEN** o agendamento é gravado com a empresa e a filial, e sem CNPJ de estabelecimento
