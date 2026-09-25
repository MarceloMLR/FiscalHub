# ADR-0024: Feed de mudanças por janela de data no D365 (porta, marca d'água, keyset e lease)

- **Status:** Aceito
- **Data:** 2026-09-25
- **Revisa:** ADR-0023 §1 (porta e estado do worker) e §2 (consulta por change tracking). Ajusta o §5
  (NaturalKey). Resolve, para o poll, a pendência de lock/lease anotada nas consequências do ADR-0017.
- **Change OpenSpec:** `openspec/changes/add-d365-change-feed-polling`
- **Validado no ambiente:** 2026-09-25, contra o `fiscosysdev`.
  - **Teste de integração** `D365ChangeFeedIntegrationTests.Keyset_read_from_2015_neither_repeats_nor_skips`:
    aprovado em 4s, com 0 avisos e 0 erros. 83 cabeçalhos em 5 páginas de 20, 83 `FiscalDocumentRecId`
    distintos.
  - **Teste manual (`docs/RUNNING.md` §6):**
    - primeira passada: 5 requisições, 14 referências enfileiradas e 69 avisos de modelo fora do mapa;
    - segunda passada, um minuto depois: 0 referências.

## Contexto

O ADR-0023 fez da descoberta por polling sobre a `FSFiscalDocumentBR` a garantia de captura. O desenho
dele supunha change tracking (`odata.track-changes`) com delta token, e um worker implementando o
`IDocumentDiscovery`. Três fatos mudaram o chão:

1. **Change tracking não está disponível.** Ele exige a entidade no Data management workspace, e o DMF
   está desligado nas 14 entidades (d365/05 §4). O row version change tracking reprova nas regras de
   validação. O que existe é `SysModifiedDateTime`, exposto no cabeçalho e filtrável (d365/05 §5).
2. **O `IDocumentDiscovery` tem outra semântica.** Ele é por período, sem estado, e atende o manual e o
   agendado pelo `IntegrationRunner`. O poll é delta a partir de uma marca, com estado, e falha não
   pode avançar essa marca.
3. **O nextLink do F&O é offset.** Verificado no `fiscosysdev` (d365/05 §10): o `@odata.nextLink` é
   `$skip`/`$top`, sem `$skiptoken` e sem snapshot.

## Decisão

**Descoberta por feed de mudanças com janela de data sobre `SysModifiedDateTime`, com marca d'água
nossa, paginação por keyset composto e lease por tenant em SQL.**

### 1. Porta nova `IDocumentChangeFeed`, separada do `IDocumentDiscovery`

`PullAsync(tenantId, since, ct)` devolve páginas (`IAsyncEnumerable<ChangeFeedPage>`). Cada página traz
as referências e a marca alta que ela garante. O worker (`ChangeFeedPoller`, na Application) enfileira
a página e só então avança a marca. Uma exceção no meio da leitura deixa a marca na última página
confirmada. As regras invioláveis do ADR-0023 ficam no worker, e não no adapter:

- falha não avança;
- lease por tenant;
- sobreposição.

Assim valem para qualquer ERP com consulta por janela. O nome D365 fica só no adapter
(`Ingress.D365Poll`).

### 2. Marca d'água por (tenant, origem), em ticks UTC

Tabela `ChangeFeedCursors`, em ticks UTC pelo mesmo motivo do ADR-0017 (`DateTimeOffset` não traduz no
`WHERE` do SQLite). Guarda também:

- último poll;
- falhas consecutivas;
- último erro;
- "não antes de" de throttling.

A consulta parte de `marca − sobreposição`. A sobreposição padrão é de 300s e precisa ser de pelo menos
1s. O intervalo padrão é de **60s**. Ambos são configuráveis por tenant no `TenantConnectorProfile`
(ADR-0019).

**Pelo relógio do lado F&O.** Na última página de uma leitura completa, a marca alta é
`max(maior SysModifiedDateTime, header Date da primeira resposta)`. Sem isso, num tenant parado, a marca
congelaria no último documento e a janela de sobreposição reenfileiraria os mesmos documentos a cada
passada, para sempre. Com isso, cada documento é relido cerca de ⌈sobreposição/intervalo⌉ + 1 vezes (6
no padrão) e sai da janela.

**Precisão:** o `Date` é o relógio do **web server** do F&O. Ele não é, necessariamente, o mesmo relógio
que carimba o `ModifiedDateTime` na gravação. Os dois ficam na mesma região e sincronizados por NTP, e
a diferença de milissegundos é absorvida pelos 300s de sobreposição. O valor da marca é nosso; o relógio
de onde ele sai é do lado F&O, nunca do FiscalHub.

### 3. Paginação por keyset composto em (`SysModifiedDateTime`, `FiscalDocumentRecId`)

- **Mecanismo.**
  - Toda página leva `$top=<pageSize>` e `$orderby=SysModifiedDateTime,FiscalDocumentRecId`.
  - A primeira página filtra `SysModifiedDateTime gt <since>`.
  - A partir da segunda, o filtro ancora na última linha lida:
    `(SysModifiedDateTime gt T) or (SysModifiedDateTime eq T and FiscalDocumentRecId gt R)`. `T` é o
    literal exatamente como veio na resposta.
  - A leitura termina na primeira página curta.
  - O `pageSize` fica entre 1 e 10.000, o teto de página do servidor, para uma página truncada nunca ser
    confundida com a última.
- **Por que não o nextLink.** O nextLink é offset. Uma nota já varrida que sofre update migra para o fim
  da ordenação, tudo depois dela desloca uma casa, e uma linha na fronteira da página se perde. Em regime
  a sobreposição recupera; em backfill (marca rebobinada para 2015), a perda é permanente.
- **Por que o keyset.** Ele ancora em valor, não em posição, e nenhum insert ou update desloca o que
  ainda não foi lido. Como o `FiscalDocumentRecId` é único na tabela, a âncora é estritamente
  crescente: nunca devolve a mesma página duas vezes, mesmo com milhares de linhas no mesmo segundo.
  Exercitado no `fiscosysdev`: 83 cabeçalhos em 5 páginas de 20, com 83 RecIds distintos.

### 4. Lease genérico em SQL, com fencing do cursor

`ILeaseStore` (Application/Coordination), genérico por recurso (`changefeed:{origin}:{tenant}`), sobre a
tabela `Leases`:

- **Aquisição.** `UPDATE` condicional atômico (dono atual ou lease expirado), mais `INSERT` tratando a
  violação de PK.
- **Renovação.** O lease é renovado a cada página, para seguir vivo numa leitura longa.
- **Fencing do cursor.** O avanço da marca é **uma** instrução condicionada ao lease:

  ```sql
  UPDATE ChangeFeedCursors SET WatermarkTicks = @w ...
   WHERE TenantId = @t AND Origin = @o AND WatermarkTicks < @w
     AND EXISTS (SELECT 1 FROM Leases WHERE Resource = @r AND Owner = @me AND ExpiresTicks > @now)
  ```

  Uma réplica que perdeu o lease (pausa de GC, rede) não avança o cursor. No máximo, ela enfileirou uma
  página a mais: o modo de falha é duplicação, nunca perda.

O agendador (ADR-0017) pode adotar o mesmo `ILeaseStore` depois.

### 5. Fila própria de descoberta

As referências vão para `documents-discovered`, pela mesma porta `IDocumentQueue` (segunda instância,
registrada por chave). Nesta fase a fila **não tem consumidor**. Enfileirar em `documents-in` levaria as
referências D365 ao `XmlGoodsInvoiceSource`, que não sabe buscá-las, e daí para a DLQ. A fatia de
roteamento/montagem (ADR-0023, passos 2 e 3) liga o consumidor.

### 6. NaturalKey = `empresa|Voucher`

Ajusta o ADR-0023 §5. Empresa|número|série colide em nota de entrada: dois fornecedores podem mandar a
mesma NF e série para a mesma empresa. O `Voucher` é o caminho de busca já validado
(`$filter=Voucher eq`) e é o `cod_referencia_integracao` do legado.

**Confirmada (2026-09-25)** por duas evidências independentes:

- **Levantamento:** 83 cabeçalhos no `fiscosysdev`, zero vouchers duplicados (d365/05 §8).
- **Domínio:** o voucher do documento fiscal é **único e imutável**: a sequência não repete, e o valor
  não muda depois de gravado. Resposta de Marcelo Lima, por conhecimento de domínio, não por leitura da
  configuração do ERP.

**Consequência da imutabilidade.** Como o voucher não muda, a transição `Approved → Cancelled` mantém a
**mesma** `NaturalKey`. É o mesmo documento com uma nova tentativa/versão, não um documento novo. Isso
confirma o modelo "documento 1:N tentativas" do ADR-0023 §5 e o item 10 das notas de implementação
(soltar o 1:1 no store). O mesmo vale para a correção de nota de entrada: mesma chave, conteúdo novo,
nova tentativa (ADR-0016).

**Risco residual: a sequência numérica é configuração por cliente.** O número sequencial do voucher é
configurado em cada F&O. Um cliente com sequência que **reinicia por exercício fiscal** faria o voucher
repetir entre anos, e a idempotência suprimiria uma nota legítima.

- **Mitigação sem custo:** o `FiscalDocumentRecId` já vem no `$select` por causa da paginação keyset.
  O desempate seria `empresa|ano|voucher` ou `empresa|RecId`, sem consulta extra ao ERP.
- **Verificação:** uma vez, no **onboarding de cada cliente**, conferindo a sequência numérica do
  voucher. Não a cada nota.

## Alternativas consideradas

- **Estender o `IDocumentDiscovery`.** A semântica é outra (período e sem estado, contra delta, estado e
  falha que não avança). Misturar faria o manual e o agendado herdarem um cursor que não é deles.
- **Paginação por nextLink (`Prefer: odata.maxpagesize`).** Funciona e foi verificada (`maxpagesize`
  honrado; o `Preference-Applied` volta vazio). Mas é offset e perde linha em backfill sob escrita
  concorrente.
- **`$top` refazendo a consulta a partir da marca, sem âncora.** Trava quando há `pageSize` ou mais
  linhas dentro da janela de sobreposição, por exemplo no job de export/import de NF-e em lote: devolve
  sempre as mesmas N linhas.
- **Keyset em (`SysModifiedDateTime`, `dataAreaId`, `Voucher`).** A ordenação funciona, mas
  `Voucher gt 'X'` é recusado (string não compara com `gt`). Não dá para ancorar.
- **Marca d'água só pelo maior `SysModifiedDateTime`.** Congela num tenant parado e reenfileira a cauda
  da janela a cada passada, para sempre.
- **Filtrar os repetidos da sobreposição**, com o conjunto de (chave, timestamp) emitido ou com detecção
  de duplicata do Service Bus. Zera a repetição, mas é um quarto esquema de idempotência. O dedupe por
  conteúdo (ADR-0016) continua sendo a garantia, e a fatia de roteamento pode absorver os repetidos
  barato.
- **Blob lease ou `sp_getapplock`.** O blob lease exige Azurite nos testes e tem TTL de 15 a 60s ou
  infinito. O `sp_getapplock` não existe em SQLite. O lease em SQL testa em SQLite e já está no
  processo.

## Consequências

**Melhora**

- **Captura sem nada no ERP além do pacote de leitura.** Nenhuma configuração de runtime no cliente.
- **Auto-recuperável.** Falha, restart ou réplica que cai deixam a marca na última página confirmada; a
  próxima passada repete.
- **Backfill seguro.** Rebobinar a marca, por exemplo apagando o cursor e usando `poll.startFrom`, relê
  tudo sem pular linha.
- **Motor genérico.** Outro ERP com consulta por janela entra escrevendo só um adapter da porta.

**Piora**

- **Repetição limitada.** Cada documento chega à fila de descoberta cerca de 6 vezes no padrão. O custo é
  zero enquanto a fila não tem consumidor; depois, o roteamento precisa absorver barato.
- **Volume real ainda não medido** (d365/05 §10). Sem índice que cubra `ModifiedDateTime` na
  `FiscalDocument_BR`, cada página pode virar varredura mais ordenação. Medir antes do primeiro cliente.
- **Gravação tardia além da sobreposição** escapa do poll. A sobreposição é generosa e configurável, e
  a rede de segurança é a descoberta D-1 agendada, que ainda vai ser escrita para o D365.
- **Risco residual na `NaturalKey`: a sequência de voucher é configuração por cliente.** Uma sequência
  que reinicia por exercício fiscal repetiria o voucher entre anos (§6). O desempate
  (`empresa|ano|voucher` ou `empresa|RecId`) já está disponível sem consulta extra. A verificação é
  feita no onboarding de cada cliente.

**Pendências**

- **Onboarding de cada cliente:** conferir que a sequência numérica do voucher não reinicia por
  exercício fiscal (§6).
- **Volume real:** medir a latência da página keyset e confirmar se há índice em `ModifiedDateTime`
  antes do primeiro cliente (d365/05 §10).
- **Modelo `01`:** tratamento é do roteamento; ver a evidência abaixo.

## Evidência para a fatia de roteamento: modelo `01`

Não é decisão desta fatia. O dado fica registrado para ninguém decidir depois pela premissa errada de
que "modelo 01 é a maioria".

| Modelo | Notas no `fiscosysdev` | Anos |
|---|---|---|
| `01` | 69 | 2015, 2016, 2017 |
| `SE` | 9 | 2015, 2016, 2026 |
| `55` | 5 | 2016 |
| **No mapa padrão (`55`/`57`/`SE`)** | **14 de 83** | |

- **O que é o modelo `01`.** É a Nota Fiscal modelo 1/1A, um formulário em papel substituído pela NF-e
  (modelo 55).
- **Por que ele aparece tanto aqui.** As 69 notas modelo `01` do `fiscosysdev` são **inteiramente dado
  de demonstração antigo**. Em cliente real, a incidência é próxima de zero. As únicas notas recentes do
  ambiente são as `SE` de 2026.
- **Recomendação:** manter o modelo `01` **fora** do mapa. A fatia de roteamento grava "ignorado:
  modelo fora do escopo" como desfecho explícito. Se algum cliente tiver modelo `01` de verdade, basta
  uma linha no `modelTypes` das settings do tenant, sem código de domínio.
