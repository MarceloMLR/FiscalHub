## MODIFIED Requirements

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

### Requirement: Os cards e a tabela contam pela data fiscal

Os cards do dashboard MUST contar as notas do tenant cuja data de referência está no período escolhido, e que são do
modelo escolhido.

- **O período:** o dia de hoje, que é o padrão, os últimos 7, 15 ou 30 dias, ou um período personalizado.
  - **Os últimos N dias:** são hoje e os N−1 dias anteriores, inclusive. "Hoje" é o dia no relógio de quem está vendo o
    dashboard.
  - **O personalizado:** vai de uma data a outra, inclusive, escolhidas por quem vê. Ele começa preenchido com a janela que
    estava escolhida. Com a data inicial depois da final, ou com uma das duas vazia, a tela MUST dizer o problema e MUST NOT
    contar.
- **O modelo:** todos, que é o padrão, ou um modelo só.
  - **As opções:** MUST trazer sempre os modelos que o hub conhece, que são os do mapa padrão do ERP (`55` NF-e, `57` CT-e e
    `SE` NFS-e), mesmo sem nenhuma nota. Os modelos que as notas do período trazem também entram.
  - **O modelo escolhido:** continua na lista quando o período muda e não o tem, e os cards mostram 0.

A tela MUST NOT repetir a janela em texto ao lado dos filtros: os filtros mostram a escolha.
- **A contagem:** MUST ser feita sobre todas as notas do período, e não sobre uma parte dos grupos.

Os cards contam:

- **Documentos:** toda nota, a ignorada inclusive.
- **Finalizados:** as confirmadas pela plataforma.
- **Em processamento:** as pendentes e as enviadas.
- **Com erro:** as rejeitadas, as sem retorno e as da dead-letter.

A nota ignorada MUST NOT contar como erro.

O critério é a data fiscal, com o recorte do período, e é intencional:

- a nota processada hoje com data fiscal fora do período MUST NOT entrar nos cards;
- ela aparece na tabela de grupos, no dia da data fiscal dela.

A tabela de grupos MUST listar os grupos de todas as datas e de todos os modelos, a nota ignorada inclusive, sem os
filtros dos cards. A tabela MUST mostrar o modelo de cada grupo. Grupos da mesma empresa, filial e data com tipo, modelo
ou modo diferentes MUST aparecer como linhas distintas, sem erro na tela.

#### Scenario: Ignorada conta no dia dela
- **WHEN** o tenant tem, com data fiscal de hoje, uma NF-e confirmada e uma NFS-e ignorada, e o filtro é o dia
- **THEN** o card "Documentos" mostra 2, o "Finalizados" mostra 1, e o "Com erro" mostra 0

#### Scenario: Notas de 2016 ficam fora dos cards
- **WHEN** a passada contra o fiscosysdev processa hoje as 14 notas da `brmf`, todas com data fiscal entre 2015 e
  2026-08-07, e o filtro é o dia
- **THEN** os cards mostram 0
- **AND** a tabela de grupos mostra as 14 notas, as 9 ignoradas incluídas, nas datas fiscais delas

#### Scenario: Os últimos 30 dias
- **WHEN** hoje é 2026-09-05, o filtro é de 30 dias, e o tenant tem NFS-e da `brmf` com data fiscal 2026-08-07 e notas
  com data fiscal de 2016
- **THEN** as NFS-e de 2026-08-07 entram nos cards, porque a janela vai de 2026-08-07 a 2026-09-05
- **AND** as notas de 2016 não entram

#### Scenario: O dia exclui as duas
- **WHEN** hoje é 2026-09-05, o filtro é o dia, e o tenant tem as mesmas notas
- **THEN** nem as NFS-e de 2026-08-07 nem as notas de 2016 entram nos cards

#### Scenario: O filtro por modelo
- **WHEN** o período tem 5 NF-e de modelo `55` rejeitadas e 9 NFS-e de modelo `SE` ignoradas
- **THEN** com o filtro `SE`, o card "Documentos" mostra 9 e o "Com erro" mostra 0
- **AND** com o filtro `55`, o card "Documentos" mostra 5 e o "Com erro" mostra 5
- **AND** com todos os modelos, o card "Documentos" mostra 14

#### Scenario: Período personalizado alcança as notas antigas
- **WHEN** o filtro é personalizado, de 2015-01-01 a 2026-10-02, e o tenant tem as notas da `brmf` de 2015 a 2026-08-07
- **THEN** todas elas entram nos cards
- **AND** com o personalizado de 2026-08-07 a 2026-08-07, entram só as notas daquele dia

#### Scenario: Período personalizado invertido
- **WHEN** o filtro é personalizado, com a data inicial 2026-08-08 e a final 2026-08-07
- **THEN** a tela diz que a data inicial é depois da final, e nenhuma contagem é pedida

#### Scenario: Os modelos sem nenhuma nota
- **WHEN** o tenant não tem nenhuma nota na janela escolhida
- **THEN** o filtro de modelo oferece todos, `55`, `57` e `SE`

#### Scenario: O modelo escolhido sai do período
- **WHEN** o filtro é `SE`, e o usuário troca para um período que não tem nenhuma NFS-e
- **THEN** os cards mostram 0, e `SE` continua escolhido e na lista

#### Scenario: Mesmo dia, tipos diferentes
- **WHEN** o estabelecimento tem, na mesma data, uma NF-e 55 e uma NFS-e ignorada
- **THEN** a tabela mostra duas linhas para essa data, uma com o modelo `55` e outra com o modelo `SE`, e a tela não
  acusa linha repetida

### Requirement: CNPJ formatado na tela

Uma empresa com 14 caracteres MUST aparecer com a máscara de CNPJ (`44.278.225/0001-80`, `12.ABC.345/01DE-35`), sejam os
caracteres dígitos ou letras. A regra vale:

- na tabela de grupos e no título do grupo;
- nos dropdowns da integração manual e do agendamento;
- nas listas de execuções e de agendamentos.

Uma empresa com outro tamanho, como a de 8 dígitos do caminho de XML, aparece como está.

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
- **WHEN** a tabela de grupos lista a empresa `12345678`
- **THEN** a coluna "Empresa" mostra `12345678`

## ADDED Requirements

### Requirement: O modal lista as notas da linha

O modal de um grupo MUST listar exatamente as notas da linha da tabela: a mesma empresa, filial, data, tipo, modelo e
modo. O número de notas do título MUST ser o total da linha, e o mesmo número de notas da lista. A nota ignorada da linha
entra.

#### Scenario: NF-e e NFS-e no mesmo dia
- **WHEN** o estabelecimento tem, no mesmo dia, uma NF-e 55 rejeitada e uma NFS-e ignorada
- **THEN** o modal da linha da NF-e lista só a NF-e, com "1 nota"
- **AND** o modal da linha da NFS-e lista só a NFS-e, com "1 nota"

#### Scenario: Mesmo dia, modos diferentes
- **WHEN** o estabelecimento tem, no mesmo dia e no mesmo modelo, uma nota de modo `Automatic` e uma de modo `Manual`
- **THEN** o modal de cada linha lista só a nota daquele modo

#### Scenario: O modal bate com o card
- **WHEN** o período do card tem uma linha só do modelo `SE`, com 3 notas, uma delas ignorada, e o filtro do card é `SE`
- **THEN** o card "Documentos" mostra 3, e o modal dessa linha lista as 3
