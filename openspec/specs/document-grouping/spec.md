# document-grouping Specification

## Purpose

Dizer de onde saem a empresa, a filial, a data de referência, o modelo e o modo de cada nota, inclusive da ignorada, e
o que os cards e a tabela de grupos do dashboard contam e mostram. A tela agrupa pelo estabelecimento próprio, e não
pelo fornecedor, e conta toda nota no dia da data fiscal dela.

## Requirements

### Requirement: Empresa e filial pelo estabelecimento próprio

Quando a origem informa o estabelecimento próprio da nota, o registro do documento MUST agrupar por ele, qualquer que
seja a direção ou a emissão da nota:

- **Empresa:** o CNPJ completo do estabelecimento, com 14 dígitos e só dígitos.
- **Filial:** o código do estabelecimento na origem, como veio.

A origem do D365 MUST informar o estabelecimento em toda nota. Numa nota de entrada emitida por terceiro, a empresa e a
filial MUST ser as do estabelecimento que escritura a nota, e MUST NOT ser as do fornecedor.

Quando a origem não informa o estabelecimento, como no caminho de XML, vale a derivação pelo emitente:

- **Empresa:** os 8 primeiros dígitos do CNPJ.
- **Filial:** os dígitos 9 a 12.

#### Scenario: Nota de entrada de terceiro
- **WHEN** uma nota do D365 tem como emitente o fornecedor `12345678000199`, e o estabelecimento próprio
  `442782250001-80` com o código `Matriz`
- **THEN** o registro do documento tem empresa `44278225000180` e filial `Matriz`

#### Scenario: Nota de saída própria
- **WHEN** uma nota do D365 é de emissão própria do estabelecimento `44278225000180`, com o código `Matriz`
- **THEN** o registro do documento tem empresa `44278225000180` e filial `Matriz`

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

O dia de uma nota, que é a data de referência do grupo e o que os cards comparam com hoje, MUST ser a data que o
próprio documento registra, no fuso de quem o emitiu, sem conversão. O hub MUST NOT converter essa data para UTC nem
para um fuso fixo.

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
- **THEN** as duas ficam com data de referência 2026-08-07, e contam no mesmo dia

#### Scenario: XML no fuso de quem emitiu
- **WHEN** uma NF-e chega pelo caminho de XML com `dhEmi = 2026-06-01T23:30:00-04:00`
- **THEN** o registro tem data de referência 2026-06-01, e não 2026-06-02

### Requirement: Os cards e a tabela contam pela data fiscal

Os cards do dashboard MUST contar as notas do tenant cuja data de referência é o dia de hoje. "Hoje" é o dia no relógio
de quem está vendo o dashboard. Os cards contam:

- **Documentos:** toda nota, a ignorada inclusive.
- **Finalizados:** as confirmadas pela plataforma.
- **Em processamento:** as pendentes e as enviadas.
- **Com erro:** as rejeitadas, as sem retorno e as da dead-letter.

A nota ignorada MUST NOT contar como erro.

O critério é a data fiscal, com recorte do dia, e é intencional:

- a nota processada hoje com data fiscal de outro dia MUST NOT entrar nos cards de hoje;
- ela aparece na tabela de grupos, no dia da data fiscal dela.

A tabela de grupos MUST listar os grupos de todas as datas, a nota ignorada inclusive. Grupos da mesma empresa, filial e
data com tipo ou modo diferentes MUST aparecer como linhas distintas, sem erro na tela.

#### Scenario: Ignorada conta no dia dela
- **WHEN** o tenant tem, com data fiscal de hoje, uma NF-e confirmada e uma NFS-e ignorada
- **THEN** o card "Documentos" mostra 2, o "Finalizados" mostra 1, e o "Com erro" mostra 0

#### Scenario: Notas de 2016 ficam fora dos cards
- **WHEN** a passada contra o fiscosysdev processa hoje as 14 notas da `brmf`, todas com data fiscal entre 2015 e
  2026-08-07
- **THEN** os cards de hoje mostram 0
- **AND** a tabela de grupos mostra as 14 notas, as 9 ignoradas incluídas, nas datas fiscais delas

#### Scenario: Mesmo dia, tipos diferentes
- **WHEN** o estabelecimento tem, na mesma data, uma NF-e 55 e uma NFS-e ignorada
- **THEN** a tabela mostra duas linhas para essa data, e a tela não acusa linha repetida

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

Na tabela de grupos e no título do grupo, uma empresa de 14 dígitos MUST aparecer com a máscara de CNPJ
(`44.278.225/0001-80`). Uma empresa com outro formato, como a de 8 dígitos do caminho de XML, aparece como está.

A máscara é só de apresentação. O código servido, o da URL do grupo e o do filtro MUST continuar só com dígitos.

#### Scenario: Empresa do D365
- **WHEN** a tabela de grupos lista a empresa `44278225000180`
- **THEN** a coluna "Empresa" mostra `44.278.225/0001-80`
- **AND** abrir o grupo consulta as notas da empresa `44278225000180`

#### Scenario: Empresa do XML
- **WHEN** a tabela de grupos lista a empresa `12345678`
- **THEN** a coluna "Empresa" mostra `12345678`
