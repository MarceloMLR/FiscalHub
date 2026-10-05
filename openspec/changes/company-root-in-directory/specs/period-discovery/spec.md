## MODIFIED Requirements

### Requirement: O D365 descobre pelo dia fiscal e pelo estabelecimento

A descoberta por período do D365 MUST trazer as notas cujo grupo cai no recorte pedido:

- **O período:** o dia do início e o dia do fim, cada um no fuso que ele traz, sem conversão. A nota entra quando o dia
  fiscal dela está entre os dois, inclusive. O dia fiscal é o `FiscalDocumentDate` (`document-grouping`, "O dia da nota é
  a data fiscal, no fuso de quem emitiu").
- **A empresa e a filial:** a nota entra quando o CNPJ normalizado do estabelecimento próprio dela **começa** com a empresa
  pedida e, com filial, o estabelecimento tem o código da filial. Os estabelecimentos são os do diretório do tenant
  (`company-directory`). Uma empresa sem estabelecimento no diretório não traz nenhuma nota, sem falha.
  - **Com a raiz** (8 caracteres): entram as notas de todos os estabelecimentos da raiz, ou só os da filial pedida.
  - **Com um CNPJ completo** (14 caracteres), o que um agendamento antigo guarda: entra exatamente aquele estabelecimento.
    O agendamento gravado continua funcionando, sem conversão.
- **A guarda do documento:** a nota MUST ficar fora quando o CNPJ que ela traz no estabelecimento próprio não começa com a
  empresa pedida, mesmo que o código do estabelecimento esteja no escopo. É o caso do estabelecimento que mudou de CNPJ:
  as notas do CNPJ antigo não são da empresa pedida.
- **O número:** com número, só a nota com esse número entra.
- **O modelo:** todos os modelos entram. O roteamento decide o que é ignorado, como no coletor.
- **A leitura:** vai até o fim do período, página a página, sem pular e sem repetir nota quando o ERP é alterado durante
  a leitura.

A empresa não é gravada no documento: o registro continua com o CNPJ completo do estabelecimento (`document-grouping`). A
empresa é derivada dele por prefixo.

#### Scenario: Agendamento da SP-01
- **WHEN** o tenant-a agenda a empresa `44278225` e a filial `SP-01`, para o período de 2026-08-07 a 2026-08-07
- **THEN** a descoberta traz as notas da `SP-01` com data fiscal 2026-08-07
- **AND** nenhuma nota da `Matriz` entra

#### Scenario: Agendamento antigo com o CNPJ completo
- **WHEN** um agendamento gravado antes desta mudança tem a empresa `44278225000260` e a filial "todas"
- **THEN** a descoberta traz só as notas da `SP-01`
- **AND** nenhuma nota da `Matriz`, da `SAL-01` ou do `RJ-01` entra

#### Scenario: A empresa inteira, todas as filiais
- **WHEN** a integração manual pede a empresa `44278225`, a filial "todas", num período com notas da `Matriz` e da `SP-01`
- **THEN** entram as notas dos dois estabelecimentos

#### Scenario: Todas as filiais
- **WHEN** a filial pedida é "todas"
- **THEN** entram as notas de todos os estabelecimentos do diretório cujo CNPJ começa com a empresa

#### Scenario: O estabelecimento que mudou de CNPJ
- **WHEN** a empresa pedida é `44278225`, e uma nota da `SP-01` traz o CNPJ `112223330001-81` no estabelecimento próprio
- **THEN** a nota fica fora

#### Scenario: Raiz alfanumérica
- **WHEN** a empresa pedida é `12ABC345`, e o cadastro tem um estabelecimento com CNPJ `12.ABC.345/01DE-35`
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
