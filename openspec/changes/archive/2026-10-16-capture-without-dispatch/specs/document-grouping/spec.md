# document-grouping Specification

## MODIFIED Requirements

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

- **Documentos:** toda nota, a ignorada e a salva inclusive.
- **Finalizados:** as confirmadas pela plataforma.
- **Em processamento:** as pendentes e as enviadas.
- **Salvas:** as que pararam com o envio ao conector de saída desligado (`outbound-dispatch-switch`).
- **Com erro:** as rejeitadas, as sem retorno e as da dead-letter.

A nota ignorada MUST NOT contar como erro. A nota salva MUST NOT contar como erro, como finalizada nem como
em processamento: ela conta em "Documentos" e em "Salvas", e em mais nenhum card.

O subtítulo de cada card MUST dizer **o que entra naquela contagem**, e MUST NOT afirmar o estado presente de
nenhuma configuração do tenant. O card conta notas que já pararam; ele não sabe como a configuração está
agora, e um subtítulo que afirme um estado ao vivo passa a mentir assim que alguém o muda.

As mesmas faixas valem para a contagem de cada linha da tabela de grupos, e o selo da linha MUST NOT dizer
"Finalizado" quando o grupo tem nota salva e nenhuma finalizada: um grupo só de notas salvas MUST aparecer
com um selo próprio, de notas salvas.

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

#### Scenario: Salva conta em Documentos e em Salvas
- **WHEN** o tenant tem, com data da execução de hoje, uma NF-e confirmada e três NF-e que pararam com o envio
  desligado, e o filtro é o dia
- **THEN** o card "Documentos" mostra 4, o "Finalizados" mostra 1, o "Salvas" mostra 3, o "Em processamento"
  mostra 0 e o "Com erro" mostra 0

#### Scenario: O subtítulo do card não afirma o estado da chave
- **WHEN** o tenant religa o envio ao conector de saída e volta à tela de grupos, que ainda conta as notas
  salvas de antes
- **THEN** o subtítulo do card "Salvas" continua dizendo o que entra na contagem, e não diz que o envio está
  desligado

#### Scenario: O selo do grupo só de notas salvas
- **WHEN** uma linha da tabela tem 10 notas, todas salvas
- **THEN** o selo da linha diz que o grupo está salvo, e não "Finalizado"

#### Scenario: O erro ainda manda no selo
- **WHEN** uma linha da tabela tem 7 notas salvas e 1 rejeitada
- **THEN** o selo da linha continua dizendo "Com pendências"

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
