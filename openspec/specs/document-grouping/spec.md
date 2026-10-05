# document-grouping Specification

## Purpose

Dizer de onde saem a empresa, a filial, a data de referência, o modelo e o modo de cada nota, inclusive da ignorada, e
o que os cards e a tabela de grupos do dashboard contam e mostram. A tela agrupa pelo estabelecimento próprio, e não
pelo fornecedor, e conta cada nota no dia da execução que a trouxe, com o período integrado ao lado. A data fiscal
continua gravada, e é o critério da descoberta por período.

## Requirements

### Requirement: Empresa e filial pelo estabelecimento próprio

Quando a origem informa o estabelecimento próprio da nota, o registro do documento MUST agrupar por ele, qualquer que
seja a direção ou a emissão da nota:

- **Empresa:** o CNPJ completo do estabelecimento, normalizado (`tax-identifier-normalization`): sem pontuação, com as
  letras e a caixa como vieram, como texto.
- **Filial:** o código do estabelecimento na origem, como veio.

A origem do D365 MUST informar o estabelecimento em toda nota. Numa nota de entrada emitida por terceiro, a empresa e a
filial MUST ser as do estabelecimento que escritura a nota, e MUST NOT ser as do fornecedor.

Quando a origem não informa o estabelecimento, como no caminho de XML, vale a derivação pelo emitente, sobre o CNPJ
normalizado:

- **Empresa:** os 8 primeiros caracteres.
- **Filial:** os caracteres 9 a 12.

#### Scenario: Nota de entrada de terceiro
- **WHEN** uma nota do D365 tem como emitente o fornecedor `12345678000199`, e o estabelecimento próprio
  `442782250001-80` com o código `Matriz`
- **THEN** o registro do documento tem empresa `44278225000180` e filial `Matriz`

#### Scenario: Nota de saída própria
- **WHEN** uma nota do D365 é de emissão própria do estabelecimento `44278225000180`, com o código `Matriz`
- **THEN** o registro do documento tem empresa `44278225000180` e filial `Matriz`

#### Scenario: Estabelecimento com CNPJ alfanumérico
- **WHEN** uma nota do D365 tem o estabelecimento próprio `12.ABC.345/01DE-35`, com o código `SP-02`
- **THEN** o registro do documento tem empresa `12ABC34501DE35` e filial `SP-02`

#### Scenario: Nota do caminho de XML
- **WHEN** uma NF-e chega pelo caminho de XML, com emitente `12345678000190`
- **THEN** o registro do documento tem empresa `12345678` e filial `0001`

### Requirement: Grupo gravado em toda nota, inclusive na ignorada

Toda nota do D365 MUST ser registrada com o grupo: empresa, filial, data de referência, número e modelo. Vale para
qualquer desfecho, mesmo sem montagem:

- enviada, rejeitada ou confirmada;
- ignorada no roteamento, como a NFS-e;
- ignorada na montagem, por modelo ou status fora do escopo;
- na dead-letter.

A data de referência é o dia da nota, pela regra de "O dia da nota é a data fiscal, no fuso de quem emitiu". No D365,
ela é o `FiscalDocumentDate`, que nunca falta num documento autorizado ou cancelado. O registro MUST NOT usar valor
padrão, nem a data de processamento no lugar dela.

Quem define o grupo:

- **Nota montada:** o grupo sai da montagem.
- **Nota que não chega à montagem:** o grupo é o que a descoberta leu da mesma origem.

O registro MUST gravar também o modo da integração, em qualquer desfecho.

Uma referência sem o grupo, publicada antes desta mudança, MUST seguir registrada como hoje, sem grupo e sem falha. O
mesmo vale para a referência do caminho de XML ignorada antes da montagem. As colunas do grupo continuam aceitando
vazio por causa delas.

#### Scenario: NFS-e ignorada com o grupo
- **WHEN** a descoberta traz a NFS-e `brmf|BRMF21-10000025`, com data fiscal 2026-08-07, estabelecimento
  `442782250002-60` e código `SP-01`, e o roteamento a ignora
- **THEN** o registro fica ignorado, com empresa `44278225000260`, filial `SP-01`, data de referência 2026-08-07 e
  modelo `SE`
- **AND** o modo gravado é `Automatic`

#### Scenario: Ignorada na montagem
- **WHEN** a referência foi descoberta com modelo `55`, e a montagem constata status `Cancelled`
- **THEN** o registro fica ignorado, com o grupo que a descoberta trouxe

#### Scenario: A montagem prevalece
- **WHEN** uma NF-e 55 descoberta com o grupo é montada e rejeitada pela plataforma
- **THEN** o registro tem o grupo da montagem, e a gravação da rejeição não o troca pelo da descoberta

#### Scenario: Referência antiga, sem o grupo
- **WHEN** a fila de descoberta entrega uma NFS-e publicada antes desta mudança, sem o grupo
- **THEN** o registro fica ignorado, sem empresa e sem data, e a mensagem é concluída sem erro

### Requirement: O dia da nota é a data fiscal, no fuso de quem emitiu

O dia de uma nota, que é a data de referência gravada no registro e o critério da descoberta por período
(`period-discovery`), MUST ser a data que o próprio documento registra, no fuso de quem o emitiu, sem conversão. O hub
MUST NOT converter essa data para UTC nem para um fuso fixo. Ele não é o dia da tabela nem o dos cards, que é o da execução
(`Os cards e a tabela contam pela data da execução`).

- **D365:** o `FiscalDocumentDate`, um dia sem hora e sem fuso, tomado como veio. Vale na nota montada e na nota que
  não chega à montagem (ignorada, na dead-letter). A data e hora de emissão (`FiscalDocumentDateTime`), que o F&O
  guarda em UTC, MUST NOT definir o dia.
- **XML:** a data do `dhEmi` no fuso que ele próprio traz.

O mesmo documento MUST cair no mesmo dia qualquer que seja o desfecho: processado, rejeitado, ignorado ou na
dead-letter.

#### Scenario: Nota emitida depois das 21h de Brasília
- **WHEN** uma NF-e 55 do D365 tem `FiscalDocumentDateTime = 2026-08-08T01:30:00Z` (22:30 de 2026-08-07 em Brasília)
  e `FiscalDocumentDate = 2026-08-07`, e é montada e rejeitada pela plataforma
- **THEN** o registro tem data de referência 2026-08-07

#### Scenario: Duas notas da mesma noite, uma processada e uma ignorada
- **WHEN** na mesma noite, depois das 21h de Brasília, o estabelecimento emite uma NF-e 55, que é montada, e uma
  NFS-e, que é ignorada, as duas com `FiscalDocumentDate = 2026-08-07`
- **THEN** as duas ficam com data de referência 2026-08-07

#### Scenario: XML no fuso de quem emitiu
- **WHEN** uma NF-e chega pelo caminho de XML com `dhEmi = 2026-06-01T23:30:00-04:00`
- **THEN** o registro tem data de referência 2026-06-01, e não 2026-06-02

### Requirement: O modo da nota se chama Automática

O modo de uma nota que entrou sem ação humana MUST ser gravado e servido como `Automatic`. São as que vieram pelo
coletor, pelo drop ou por evento. Os rótulos da coluna "Tipo" MUST ser:

- `Automatic`: "Automática";
- `Manual`: "Imediata";
- `ScheduledDaily`: "Diária (D-1)";
- `ScheduledOnce`: "Agendada".

Os registros gravados antes desta mudança com `RealTime` MUST passar a `Automatic`. Um registro sem modo MUST ser
servido como `Automatic`. A tela MUST NOT mostrar "Tempo real".

#### Scenario: Nota do coletor
- **WHEN** uma nota entra pelo feed de mudanças do D365
- **THEN** o registro tem modo `Automatic`, e a coluna "Tipo" mostra "Automática"

#### Scenario: Registro antigo
- **WHEN** a base tem uma nota gravada antes desta mudança com modo `RealTime`
- **THEN** depois da migração, o grupo dela é servido com modo `Automatic`, e a tela mostra "Automática"

#### Scenario: Ignorada de uma integração manual
- **WHEN** uma integração manual enfileira uma nota que o roteamento ignora
- **THEN** o registro tem modo `Manual`, e não `Automatic`

### Requirement: CNPJ formatado na tela

A empresa MUST aparecer mascarada pelo tamanho, sejam os caracteres dígitos ou letras, por uma regra só:

- **14 caracteres, o CNPJ completo:** a máscara de CNPJ (`44.278.225/0001-80`, `12.ABC.345/01DE-35`). É a empresa dos
  grupos do D365 e a do diretório do D365 (`company-directory`);
- **8 caracteres, a raiz do CNPJ:** a máscara da raiz (`44.278.225`, `12.ABC.345`). É a empresa dos grupos do caminho de
  XML e a do diretório de exemplo;
- **qualquer outro tamanho:** aparece como está.

A regra vale:

- na tabela de grupos e no título do grupo;
- nos dropdowns da integração manual e do agendamento, inclusive no CNPJ da filial;
- nas listas de execuções e de agendamentos.

A máscara é só de apresentação. O código servido, o da URL do grupo, o do filtro e o enviado à integração MUST continuar
sem máscara.

#### Scenario: Empresa do D365
- **WHEN** a tabela de grupos lista a empresa `44278225000180`
- **THEN** a coluna "Empresa" mostra `44.278.225/0001-80`
- **AND** abrir o grupo consulta as notas da empresa `44278225000180`

#### Scenario: Empresa alfanumérica
- **WHEN** a tabela de grupos lista a empresa `12ABC34501DE35`
- **THEN** a coluna "Empresa" mostra `12.ABC.345/01DE-35`
- **AND** abrir o grupo consulta as notas da empresa `12ABC34501DE35`

#### Scenario: Empresa do XML
- **WHEN** a tabela de grupos lista a empresa `12345678`, a raiz que o caminho de XML grava
- **THEN** a coluna "Empresa" mostra `12.345.678`, e não mais `12345678`
- **AND** abrir o grupo consulta as notas da empresa `12345678`

#### Scenario: Raiz alfanumérica
- **WHEN** a empresa é `12ABC345`
- **THEN** ela aparece como `12.ABC.345`

#### Scenario: Outro tamanho
- **WHEN** a empresa é `B01`
- **THEN** ela aparece como `B01`

### Requirement: A execução que trouxe a nota

O registro do documento MUST guardar a execução que trouxe a nota por último (pedido da conferência na tela,
2026-10-02):

- **O dia da execução:** o dia, em Brasília, em que a integração rodou. Na imediata, na diária (D-1) e na agendada, é o
  dia da execução. Na automática, é o dia em que o coletor buscou a nota.
- **O período integrado:** o período da integração imediata, da diária e da agendada, de uma data a outra. A automática
  MUST NOT ter período.
- **O modo:** o da mesma execução.

A nota é registrada uma vez só, na linha da última entrada:

- **Outra integração, ou o coletor de novo:** a nota passa para o dia, o modo e o período dessa entrada.
- **O reprocesso:** MUST NOT mover a nota. Ele só soma na contagem de reprocessos.
- **A integração agendada que pula a nota pela idempotência,** porque ela já foi integrada com o mesmo conteúdo, MUST NOT
  movê-la: nada foi integrado.
- **A nota gravada antes desta mudança:** fica no dia em que foi gravada pela primeira vez, sem período.

#### Scenario: Integração imediata de um período antigo
- **WHEN** em 2026-10-02 o usuário dispara uma integração imediata da Matriz para 2016-09-01 a 2016-09-03, e a nota número
  1, de data fiscal 2016-09-02, é integrada
- **THEN** a linha da nota tem a data 2026-10-02, o modo "Imediata" e o período integrado de 2016-09-01 a 2016-09-03

#### Scenario: Nota da integração automática
- **WHEN** o coletor busca em 2026-10-02 uma NFS-e de data fiscal 2026-08-07
- **THEN** a linha da nota tem a data 2026-10-02, o modo "Automática", e nenhum período integrado

#### Scenario: A última entrada move a nota
- **WHEN** o coletor registrou a NFS-e `brmf|BRMF06-110000034` em 2026-10-01, e um agendamento da `SP-01` a ignora de novo
  em 2026-10-02
- **THEN** o registro continua um só, agora na data 2026-10-02, com o modo "Agendada" e o período do agendamento

#### Scenario: O reprocesso não move a nota
- **WHEN** a NF-e integrada em 2026-10-01 por uma integração imediata é reprocessada em 2026-10-02
- **THEN** a linha continua com a data 2026-10-01, o modo "Imediata" e o mesmo período, e a contagem de reprocessos sobe

### Requirement: Os cards e a tabela contam pela data da execução

Os cards e a tabela de grupos do dashboard MUST mostrar as notas do tenant cuja data da execução
(`A execução que trouxe a nota`) está no período escolhido, e que são do modelo escolhido. O mesmo filtro vale para os
dois (pedido da conferência na tela, 2026-10-02).

- **O período:** o dia de hoje, que é o padrão, os últimos 7, 15 ou 30 dias, ou um período personalizado.
  - **Os últimos N dias:** são hoje e os N−1 dias anteriores, inclusive. "Hoje" é o dia no relógio de quem está vendo o
    dashboard.
  - **O personalizado:** vai de uma data a outra, inclusive, escolhidas por quem vê. Ele começa preenchido com a janela que
    estava escolhida. Com a data inicial depois da final, ou com uma das duas vazia, a tela MUST dizer o problema e MUST NOT
    contar nem listar.
- **O modelo:** todos, que é o padrão, ou um modelo só.
  - **As opções:** MUST trazer sempre os modelos que o hub conhece, que são os do mapa padrão do ERP (`55` NF-e, `57` CT-e e
    `SE` NFS-e), mesmo sem nenhuma nota. Os modelos que as notas do período trazem também entram.
  - **O modelo escolhido:** continua na lista quando o período muda e não o tem, e os cards mostram 0.
- **A contagem dos cards:** MUST ser feita sobre todas as notas do período, e não sobre uma parte dos grupos.

A tela MUST NOT repetir a janela em texto ao lado dos filtros: os filtros mostram a escolha.

Os cards contam:

- **Documentos:** toda nota, a ignorada inclusive.
- **Finalizados:** as confirmadas pela plataforma.
- **Em processamento:** as pendentes e as enviadas.
- **Com erro:** as rejeitadas, as sem retorno e as da dead-letter.

A nota ignorada MUST NOT contar como erro.

A tabela de grupos MUST mostrar, por linha, a data da execução, o modelo, o modo e o período integrado ("—" na
automática). As datas aparecem como aaaa-mm-dd, e o período como as duas datas separadas por um traço
(`2016-09-01 – 2016-10-02`), ou uma só quando o período é de um dia. Grupos da mesma empresa e filial com data da execução, período, tipo, modelo ou modo diferentes MUST aparecer
como linhas distintas, sem erro na tela.

#### Scenario: Notas de 2016 integradas hoje
- **WHEN** o coletor processa hoje as notas da `brmf`, todas com data fiscal entre 2015 e 2026-08-07, e o filtro é o dia
- **THEN** os cards contam essas notas, e a tabela as mostra com a data de hoje

#### Scenario: A integração imediata de hoje entra no dia
- **WHEN** a integração imediata da Matriz para 2016-09-01 a 2016-09-03 roda hoje, e o filtro é o dia
- **THEN** a nota número 1 entra nos cards e na tabela, com a data de hoje e o período integrado `2016-09-01 – 2016-09-03`

#### Scenario: Ignorada conta no dia dela
- **WHEN** o tenant tem, com data da execução de hoje, uma NF-e confirmada e uma NFS-e ignorada, e o filtro é o dia
- **THEN** o card "Documentos" mostra 2, o "Finalizados" mostra 1, e o "Com erro" mostra 0

#### Scenario: O filtro vale para a tabela
- **WHEN** o tenant tem notas executadas hoje e em 2026-09-20, e o filtro é o dia
- **THEN** a tabela mostra só as linhas de hoje
- **AND** com o personalizado de 2026-09-20 a 2026-09-20, a tabela mostra só as de 2026-09-20

#### Scenario: O filtro por modelo
- **WHEN** o período tem 5 NF-e de modelo `55` rejeitadas e 9 NFS-e de modelo `SE` ignoradas
- **THEN** com o filtro `SE`, o card "Documentos" mostra 9, o "Com erro" mostra 0, e a tabela mostra só as linhas `SE`
- **AND** com o filtro `55`, o card "Documentos" mostra 5 e o "Com erro" mostra 5
- **AND** com todos os modelos, o card "Documentos" mostra 14

#### Scenario: Período personalizado invertido
- **WHEN** o filtro é personalizado, com a data inicial 2026-08-08 e a final 2026-08-07
- **THEN** a tela diz que a data inicial é depois da final, e nenhuma contagem nem lista é pedida

#### Scenario: Os modelos sem nenhuma nota
- **WHEN** o tenant não tem nenhuma nota na janela escolhida
- **THEN** o filtro de modelo oferece todos, `55`, `57` e `SE`

#### Scenario: O modelo escolhido sai do período
- **WHEN** o filtro é `SE`, e o usuário troca para um período que não tem nenhuma NFS-e
- **THEN** os cards mostram 0, e `SE` continua escolhido e na lista

#### Scenario: Mesmo dia, tipos diferentes
- **WHEN** o estabelecimento tem, na mesma data da execução, uma NF-e 55 e uma NFS-e ignorada
- **THEN** a tabela mostra duas linhas para essa data, uma com o modelo `55` e outra com o modelo `SE`, e a tela não
  acusa linha repetida

### Requirement: O modal lista as notas da linha

O modal de um grupo MUST listar exatamente as notas da linha da tabela: a mesma empresa, filial, data da execução, período
integrado, tipo, modelo e modo. O número de notas do título MUST ser o total da linha, e o mesmo número de notas da lista. A
nota ignorada da linha entra.

#### Scenario: NF-e e NFS-e no mesmo dia
- **WHEN** o estabelecimento tem, no mesmo dia da execução, uma NF-e 55 rejeitada e uma NFS-e ignorada
- **THEN** o modal da linha da NF-e lista só a NF-e, com "1 nota"
- **AND** o modal da linha da NFS-e lista só a NFS-e, com "1 nota"

#### Scenario: Mesmo dia, modos diferentes
- **WHEN** o estabelecimento tem, no mesmo dia e no mesmo modelo, uma nota de modo `Automatic` e uma de modo `Manual`
- **THEN** o modal de cada linha lista só a nota daquele modo

#### Scenario: Mesmo dia, períodos diferentes
- **WHEN** duas integrações imediatas rodam hoje para o mesmo estabelecimento, uma de 2016-09-01 a 2016-09-03 e outra de
  2016-03-01 a 2016-03-01
- **THEN** a tabela mostra uma linha para cada período, e o modal de cada uma lista só as notas dela

#### Scenario: O modal bate com o card
- **WHEN** o período do card tem uma linha só do modelo `SE`, com 3 notas, uma delas ignorada, e o filtro do card é `SE`
- **THEN** o card "Documentos" mostra 3, e o modal dessa linha lista as 3
