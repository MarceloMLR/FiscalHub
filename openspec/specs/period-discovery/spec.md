# period-discovery Specification

## Purpose

Achar as notas de um período e de um estabelecimento na origem do tenant, para a integração manual e a agendada,
com a mesma referência do coletor e na mesma fila dele, e achar pela chave a nota do reprocesso.

## Requirements

### Requirement: A descoberta por período é a do adapter de entrada do perfil

A integração manual e a agendada MUST descobrir as notas pela implementação do adapter de entrada do perfil do tenant,
pela comparação exata do identificador.

- **Sem implementação para o adapter, fora de Development:** a execução MUST falhar com motivo que cita o adapter e o
  tenant, e nada é enfileirado.
- **Sem implementação, em Development, e só lá:** o catálogo local de exemplo responde no lugar dela. Ele MUST NOT
  responder por um tenant cujo adapter tem implementação.

#### Scenario: Tenant do D365
- **WHEN** o tenant-a, com o adapter de entrada `Dynamics365`, dispara uma integração manual
- **THEN** as notas vêm do D365 do tenant-a
- **AND** o catálogo local de exemplo não é consultado

#### Scenario: ERP sem descoberta, fora de Development
- **WHEN** o tenant-b, com o adapter de entrada `iScala`, dispara uma integração manual, e o host roda fora de Development
- **THEN** a resposta é uma recusa que cita `iScala` e o tenant-b
- **AND** nada é enfileirado

#### Scenario: ERP sem descoberta, em Development
- **WHEN** o tenant-b, com o adapter de entrada `iScala`, dispara uma integração manual em Development
- **THEN** o catálogo local de exemplo responde, como hoje

### Requirement: O D365 descobre pelo dia fiscal e pelo estabelecimento

A descoberta por período do D365 MUST trazer as notas cujo grupo cai no recorte pedido:

- **O período:** o dia do início e o dia do fim, cada um no fuso que ele traz, sem conversão. A nota entra quando o dia
  fiscal dela está entre os dois, inclusive. O dia fiscal é o `FiscalDocumentDate` (`document-grouping`, "O dia da nota é
  a data fiscal, no fuso de quem emitiu").
- **A empresa e a filial:** a nota entra quando o estabelecimento próprio dela tem o CNPJ da empresa (normalizado) e,
  com filial, o código da filial. Os estabelecimentos são os do diretório do tenant (`company-directory`). Uma empresa sem
  estabelecimento no diretório não traz nenhuma nota, sem falha.
- **O número:** com número, só a nota com esse número entra.
- **O modelo:** todos os modelos entram. O roteamento decide o que é ignorado, como no coletor.
- **A leitura:** vai até o fim do período, página a página, sem pular e sem repetir nota quando o ERP é alterado durante
  a leitura.

#### Scenario: Agendamento da SP-01
- **WHEN** o tenant-a agenda a empresa `44278225000260` e a filial `SP-01`, para o período de 2026-08-07 a 2026-08-07
- **THEN** a descoberta traz as notas da `SP-01` com data fiscal 2026-08-07
- **AND** nenhuma nota da `Matriz` entra

#### Scenario: Nota fora do período
- **WHEN** a nota tem data fiscal 2016-03-05, e o período é agosto de 2026
- **THEN** ela não entra

#### Scenario: Nota emitida à noite
- **WHEN** a nota tem `FiscalDocumentDate = 2026-08-07` e `FiscalDocumentDateTime = 2026-08-08T01:30:00Z`, e o período é
  de 2026-08-07 a 2026-08-07, com o fuso de Brasília
- **THEN** ela entra

#### Scenario: Todas as filiais
- **WHEN** a filial pedida é "todas"
- **THEN** entram as notas de todos os estabelecimentos do diretório com o CNPJ da empresa

#### Scenario: Nota específica
- **WHEN** o pedido traz o número `000123`
- **THEN** só a nota com o número `000123`, no período e no estabelecimento, entra

#### Scenario: Mais notas que uma página
- **WHEN** o período tem mais notas que o tamanho de página do perfil
- **THEN** todas entram, uma vez cada

### Requirement: A mesma referência do coletor

Cada nota descoberta por período no D365 MUST virar a mesma referência que o coletor publica para ela:

- a mesma chave natural;
- o mesmo locator;
- a mesma origem;
- o mesmo grupo: empresa, filial, data de referência, número e modelo.

Com isso, a nota cai no mesmo registro e no mesmo grupo. O modo da referência é o da execução (manual, diária ou
agendada), e a idempotência segue as regras de hoje: o manual recarrega, e o agendado dedupa pelo conteúdo.

#### Scenario: O coletor e a descoberta leem o mesmo cabeçalho
- **WHEN** o coletor e a descoberta por período leem o cabeçalho da NFS-e `brmf|BRMF21-10000025`
- **THEN** as duas referências têm a chave natural `brmf|BRMF21-10000025`, o locator `d365/brmf/<RecId>`, a origem
  `Dynamics365` e o mesmo grupo

#### Scenario: Nota que o coletor já trouxe
- **WHEN** a NFS-e `brmf|BRMF21-10000025` foi registrada pelo coletor, e um agendamento da `SP-01` a descobre de novo
- **THEN** o registro do documento continua um só, com a mesma empresa, a mesma filial e a mesma data
- **AND** a nota aparece numa linha só da tabela de grupos

### Requirement: A execução e o reprocesso publicam na fila do coletor

As referências da integração manual, da agendada e do reprocesso MUST ser publicadas na fila de descoberta, a mesma em
que o coletor publica. Assim, duas cópias da mesma nota são processadas uma depois da outra (`discovery-queue-consumer`,
"Repetição absorvida pela idempotência existente").

A ingestão pela zona de drop e pelo endpoint de ingestão continua na fila de entrada.

#### Scenario: O coletor e um agendamento ao mesmo tempo
- **WHEN** o coletor e um agendamento publicam a mesma NF-e ao mesmo tempo, e o conteúdo na origem não mudou
- **THEN** as duas cópias passam pela mesma fila, e a segunda só começa depois que a primeira terminou
- **AND** a nota é enviada ao destino uma vez só

#### Scenario: Nota de exemplo pela integração manual, em Development
- **WHEN** o catálogo local responde a uma integração manual em Development
- **THEN** a referência vai para a fila de descoberta, com a origem `Xml`, e é buscada pelo adapter de XML

### Requirement: O reprocesso acha a nota na origem dela

O reprocesso MUST procurar a nota primeiro na descoberta do adapter de entrada do perfil e, em Development, no catálogo
local de exemplo. Vale a primeira que acha a nota.

- **A nota do D365:** MUST ser achada pela chave natural, que é a empresa e o voucher.
- **Uma chave sem a forma da origem:** MUST NOT gerar consulta a essa origem.
- **A nota não achada:** a resposta é "não encontrada na origem", como hoje.

#### Scenario: Nota do D365
- **WHEN** o usuário do tenant-a pede o reprocesso da nota `brmf|BRMF06-110000027`
- **THEN** a referência é reenfileirada com o locator `d365/brmf/<RecId>`, a origem `Dynamics365` e o gatilho manual

#### Scenario: Nota de exemplo, em Development
- **WHEN** o usuário do tenant-a pede, em Development, o reprocesso da nota 123 do catálogo, cuja chave é a chave de acesso
  de 44 dígitos
- **THEN** nenhuma consulta é feita ao D365
- **AND** o catálogo local a reenfileira com a origem `Xml` e o gatilho manual

#### Scenario: Nota que não existe na origem
- **WHEN** o usuário pede o reprocesso de `brmf|BRMF99-0`, que o D365 não tem
- **THEN** a resposta é "não encontrada na origem", e nada é enfileirado

### Requirement: O reprocesso é contado, e a contagem aparece no modal

Cada reprocesso aceito, com a nota achada na origem e reenfileirada, MUST somar um na contagem de reprocessos do registro do
documento. O pedido de outro tenant e a nota que não está na origem MUST NOT contar.

O modal do grupo MUST mostrar, por nota, a contagem de reprocessos ao lado da de consultas de status. A contagem não zera
quando a nota é reenviada, ao contrário da de consultas. A nota gravada antes desta mudança começa em zero.

(Pedido da conferência na tela, 2026-10-02.)

#### Scenario: Dois reprocessos
- **WHEN** o usuário reprocessa duas vezes a NF-e `brmf|BRMF06-110000027`, e a nota é achada nas duas
- **THEN** o modal do grupo mostra 2 em "Reprocessos" para ela

#### Scenario: Reprocesso recusado não conta
- **WHEN** o reprocesso responde "não encontrada na origem"
- **THEN** a contagem de reprocessos da nota não muda
