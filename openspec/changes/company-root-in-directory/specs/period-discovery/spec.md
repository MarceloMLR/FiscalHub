## MODIFIED Requirements

### Requirement: O D365 descobre pelo dia fiscal e pelo estabelecimento

A descoberta por período do D365 MUST trazer as notas cujo grupo cai no recorte pedido:

- **O período:** o dia do início e o dia do fim, cada um no fuso que ele traz, sem conversão. A nota entra quando o dia
  fiscal dela está entre os dois, inclusive. O dia fiscal é o `FiscalDocumentDate` (`document-grouping`, "O dia da nota é
  a data fiscal, no fuso de quem emitiu").
- **A empresa e a filial:** a empresa chega pelo CNPJ completo, como o diretório a dá e o agendamento a grava. A nota
  entra quando duas condições valem:
  - o estabelecimento próprio dela é da empresa pedida: a mesma raiz de CNPJ, que são os 8 primeiros caracteres do CNPJ
    normalizado, comparados como texto;
  - com filial, o estabelecimento tem o código da filial.

  Os estabelecimentos são os do diretório do tenant (`company-directory`). Uma empresa sem estabelecimento no diretório
  não traz nenhuma nota, sem falha. A empresa vazia não casa com nada.
  - **Sem filial:** entram as notas de todos os estabelecimentos da raiz. Vale para qualquer CNPJ da empresa, inclusive o
    de uma filial.
  - **Com filial:** entram só as notas do estabelecimento da filial.
- **A guarda do documento:** a nota MUST ficar fora quando o CNPJ que ela traz no estabelecimento próprio é de outra raiz,
  mesmo que o código do estabelecimento esteja no escopo. É o caso de um estabelecimento que mudou de CNPJ para o de outra
  empresa: as notas do CNPJ antigo não são da empresa pedida. Uma nota com outro CNPJ da mesma raiz entra, porque é da
  empresa pedida.
- **O número:** com número, só a nota com esse número entra.
- **O modelo:** todos os modelos entram. O roteamento decide o que é ignorado, como no coletor.
- **A leitura:** vai até o fim do período, página a página, sem pular e sem repetir nota quando o ERP é alterado durante
  a leitura.

A empresa não é gravada no documento: o registro continua com o CNPJ completo do estabelecimento (`document-grouping`). A
comparação com a empresa pedida é pela raiz.

#### Scenario: Agendamento da SP-01
- **WHEN** o tenant-a agenda a empresa `44278225000180` e a filial `SP-01`, para o período de 2026-08-07 a 2026-08-07
- **THEN** a descoberta traz as notas da `SP-01` com data fiscal 2026-08-07
- **AND** nenhuma nota da `Matriz` entra

#### Scenario: O agendamento gravado da Matriz
- **WHEN** um agendamento tem a empresa `44278225000180` e a filial `Matriz`, o único gravado no banco de dev em
  2026-10-05
- **THEN** a descoberta traz só as notas da `Matriz`, as mesmas de antes desta mudança

#### Scenario: A empresa inteira, todas as filiais
- **WHEN** a integração manual pede a empresa `44278225000180`, a filial "todas", num período com notas da `Matriz` e da
  `SP-01`
- **THEN** entram as notas dos dois estabelecimentos

#### Scenario: Todas as filiais
- **WHEN** a filial pedida é "todas"
- **THEN** entram as notas de todos os estabelecimentos do diretório com a raiz da empresa pedida

#### Scenario: O CNPJ de uma filial como empresa
- **WHEN** o pedido tem a empresa `44278225000260`, o CNPJ da `SP-01`, e a filial "todas"
- **THEN** a descoberta procura nos quatro estabelecimentos da raiz `44278225`, e não só na `SP-01`

#### Scenario: O estabelecimento que mudou de CNPJ
- **WHEN** a empresa pedida é `44278225000180`, e uma nota da `SP-01` traz o CNPJ `112223330001-81` no estabelecimento
  próprio
- **THEN** a nota fica fora

#### Scenario: Outro CNPJ da mesma raiz
- **WHEN** a empresa pedida é `44278225000180`, e uma nota da `SP-01` traz o CNPJ `442782250099-99` no estabelecimento
  próprio
- **THEN** a nota entra

#### Scenario: CNPJ alfanumérico
- **WHEN** a empresa pedida é `12ABC34501DE35`, e o cadastro tem um estabelecimento com CNPJ `12.ABC.345/01DE-35`
- **THEN** as notas desse estabelecimento entram

#### Scenario: Nota fora do período
- **WHEN** a nota tem data fiscal 2016-03-05, e o período é agosto de 2026
- **THEN** ela não entra

#### Scenario: Nota emitida à noite
- **WHEN** a nota tem `FiscalDocumentDate = 2026-08-07` e `FiscalDocumentDateTime = 2026-08-08T01:30:00Z`, e o período é
  de 2026-08-07 a 2026-08-07, com o fuso de Brasília
- **THEN** ela entra

#### Scenario: Nota específica
- **WHEN** o pedido traz o número `000123`
- **THEN** só a nota com o número `000123`, no período e no estabelecimento, entra

#### Scenario: Mais notas que uma página
- **WHEN** o período tem mais notas que o tamanho de página do perfil
- **THEN** todas entram, uma vez cada
