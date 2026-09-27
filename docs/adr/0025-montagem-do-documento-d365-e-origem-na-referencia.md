# ADR-0025: Montagem do documento D365, origem na referência e supressão de republicação

- **Status:** Aceito
- **Data:** 2026-09-26
- **Revisa:** ADR-0024. Muda o Locator (§ mapeamento) e liga a fila de descoberta, que não tinha
  consumidor. Adota a alternativa "filtrar os repetidos da sobreposição", que o ADR-0024 tinha rejeitado.
- **Change OpenSpec:** `openspec/changes/add-d365-document-assembly`
- **Validado no ambiente:** 2026-09-26, contra o `fiscosysdev` (`docs/RUNNING.md` §7).
  - **Teste de integração** `D365GoodsInvoiceSourceIntegrationTests`: as 5 NF-e 55 da `brmf` montadas duas vezes
    cada, com a mesma impressão nas duas.
  - **Teste manual (host + emulador):**
    - primeira passada: 14 referências na fila de descoberta, 0 suprimidas, 69 avisos de modelo `01`;
    - consumo: 5 NF-e montadas uma vez cada e rejeitadas ("tributos da Reforma (IBS/CBS) ausentes"; uma
      também pela chave de acesso vazia), 9 NFS-e `Ignored`, 0 enviadas ao mock;
    - GETs: 4 por montagem (cabeçalho, linhas, impostos, encargos), mais 1 contábil só na nota de importação.
      Cadastros: 5 endereços para 10 consultas (o do estabelecimento veio do cache) e 2 cidades;
    - a impressão gravada é o SHA-256 da foto `source.json` no Blob;
    - segunda passada, um minuto depois: 0 referências, 0 suprimidas.
  - **Supressão ao vivo não exercitada.** Numa base parada, depois da primeira passada nada fica na janela de
    sobreposição. A regra é provada pelos testes do poller.

## Contexto

O ADR-0024 deixou a fila `documents-discovered` com uma referência por documento que mudou no F&O, e
sem consumidor. A `DocumentPipeline<TDocument>` já faz idempotência por hash, foto do domínio,
validação, envio e registro, e busca o documento por `IInboundSource<TDocument>`. Montar a nota é
escrever esse `FetchAsync` para o D365, e consumir a fila é ligar um segundo gatilho à mesma esteira.

Quatro forças moldaram as decisões:

1. **As entidades por FK (PR #53).** A `FSFiscalDocumentTaxTransBR` e a `FSFiscalDocumentMiscChargeBR`
   ligam imposto e encargo à linha por chave estrangeira declarada (d365/04 §9). A montagem cabe em 4
   consultas, sem par polimórfico.
2. **A fiscal não é superconjunto da contábil.**
   - Casadas pelo `TaxTransRecId`, as 547 linhas batem 1:1 nos dois sentidos.
   - A contábil grava imposto de saída com sinal negativo, e o CST de saída vem com a família de entrada.
   - 26 linhas vêm zeradas na fiscal com valor na contábil. O `ImportTax` só tem valor do lado contábil.
3. **Um mesmo tenant recebe de mais de uma origem.** O perfil do tenant-a diz `Dynamics365`, e o mesmo
   tenant roda o fluxo XML (drop, `/ingest`, reprocesso).
4. **A sobreposição custa caro fora da base de demonstração.**
   - Com sobreposição de 300s e intervalo de 60s, cada nota é redescoberta cerca de 6 vezes, e cada vez
     vira uma montagem de 4 a 5 GETs.
   - O hash por conteúdo não evita isso: a esteira busca antes de checar a idempotência, e tem que ser
     nessa ordem.
   - A premissa do ADR-0024 para rejeitar o filtro ("o roteamento absorve os repetidos barato") não se
     sustentou.

## Decisão

**Montar a NF-e de mercadoria no `FetchAsync` do D365 a partir da entidade fiscal, com a origem viajando
na referência e a republicação da sobreposição suprimida no poller.**

### 1. Origem na referência

`DocumentReference.Origin` é opcional, no mesmo desenho do `Trigger`: ausente, vale o `InboundAdapter`
do perfil do tenant. Quem publica preenche:

- o `ChangeFeedPoller` põe a origem do feed;
- o drop, o `/ingest` e a descoberta local põem `Xml`.

Um resolver na Application (`IInboundSourceResolver<T>`) escolhe o `IInboundSource` por documento, em
todos os caminhos da esteira. Origem sem adapter falha com erro que nomeia a origem e o tenant. O
perfil continua respondendo "o que varrer"; a referência responde "de onde veio".

### 2. Roteamento por tipo e desfecho `Ignored`

Um roteador na Application manda só a NF-e 55 para a esteira. NFS-e e CT-e saem como "ignorado: tipo
fora do escopo", gravado no `IntegrationStatus.Ignored`, visível no dashboard e fora dos KPIs de falha.
Um source que constate, na montagem, que o documento saiu do escopo lança
`DocumentOutOfScopeException`, e o roteador grava o mesmo desfecho. Os casos são modelo diferente de 55
e status diferente de autorizado.

### 3. Locator com RecId

O Locator passa a `d365/<dataAreaId>/<FiscalDocumentRecId>`. O cabeçalho é lido pela chave primária; o
`Voucher` não lidera nenhum índice da `FiscalDocument_BR`. O source confere `dataAreaId|Voucher` contra
a `NaturalKey`.

### 4. Só nota autorizada; vazio é falha

- **Só `Status = Approved` é montada.** A esteira só conhece a operação "emitida", e o adapter da Avalara
  não despacha cancelamento. Montar uma cancelada seria mandá-la como emitida.
- **Cabeçalho vazio é falha**, que segue para retentativa e dead-letter, e não "ignorado". Com
  `cross-company=true`, perda de acesso à empresa também devolve vazio.

### 5. A entidade fiscal é a fonte; o complemento é por tipo conhecido

Tipo, CST, base, alíquota, valor e lugar no documento vêm da `FSFiscalDocumentTaxTransBR`, sem regra de
sinal nem de CST no adapter. Cada imposto vai para um único lugar:

- imposto do item;
- imposto do encargo;
- retenção, pelo `RetainedTax`;
- grupo IBS/CBS.

**Complemento contábil só para o `ImportTax`,** o tipo conhecido em que a fiscal vem zerada.

- **Consulta:** a linha zerada com ponte (`TaxTransRecId`) dispara uma consulta a mais na
  `FSTaxTransBRs`, pelo voucher e pela empresa.
- **Merge campo a campo:** vale a fiscal quando ela é diferente de zero, e a contábil quando a fiscal é
  zero.
- **Os outros zerados** (IPI 05, PIS/COFINS 98/99, ICMS 90) ficam zerados, como a fiscal os apresenta.
  Um complemento genérico levaria, por exemplo, valor de IPI suspenso para a nota.

**Divergência não prevista para o documento**, sem escolher um lado: ponte sem par, tipo diferente,
contábil negativa ou dois valores diferentes de zero e diferentes entre si.

### 6. Domínio aditivo; IBS/CBS ausente não vira zero

O `GoodsInvoiceItem` ganha `Taxes`, `Withholdings` e `Charges` (com `TaxLine` e `ItemCharge`). O
`ReformTaxes` passa a opcional: nota sem IBS/CBS é montada sem o grupo, e o validador a rejeita com
motivo próprio.

O `cClassTrib` não está na entidade fiscal. Na contábil, ele é um RecId de `CClassTribTable_BR`, sem
entidade que o resolva. Ele fica vazio, e a nota com IBS/CBS é rejeitada por "cClassTrib ausente" até
existir essa entidade.

### 7. Hash de conteúdo canônico

O D365 não tem um "cru" único, e sim N respostas JSON. A impressão (ADR-0016) é o SHA-256 de um JSON
canônico:

- versão no topo (`"v":1`);
- entidades em ordem fixa;
- coleções ordenadas pelo RecId;
- propriedades em ordem ordinal;
- números como vieram;
- sem `@odata.*` e sem `SysModifiedDateTime`.

Cadastros não entram: mudança de cadastro não altera a nota emitida. O canônico é também a foto da
fonte (ADR-0006), salva antes do mapeamento. Mudar o `$select` ou o formato muda a impressão, e por isso
a versão sobe junto.

### 8. Cadastros em cache por tenant

Endereço e cidade (código IBGE do município das partes) ficam em cache em memória por (tenant, entidade,
RecId), com expiração absoluta de 1 hora por padrão. Item, unidade, país e modelo não entram enquanto
nenhum campo do domínio os consumir.

### 9. Supressão de republicação no poller

A página do feed passa a trazer, por referência, o carimbo de alteração e, por leitura, o **horizonte
estável**: carimbos até ali são definitivos. No D365, o horizonte é o `Date` da primeira resposta da
leitura menos uma margem de 10s. O `ChangeFeedPoller` não republica o par (tenant, origem, chave natural,
carimbo) que ele mesmo já publicou com o carimbo assentado.

| | sem supressão | com supressão |
|---|---|---|
| publicações por alteração | ~6 | ~1,15 |
| GETs por nota | ~27 | ~5 |
| teto pelo limite do F&O (6.000 a cada 5 min) | ~45 notas/min | ~230 notas/min |
| teto do consumidor serial (~50 montagens/min) | ~8 notas/min | ~43 notas/min |

- **O carimbo é parte da chave.** Nota alterada dentro da janela tem carimbo novo e é republicada.
- **Só o par assentado é registrado.** O `SysModifiedDateTime` tem resolução de segundo (medido na
  base), e duas gravações no mesmo segundo ficariam com o mesmo carimbo. O par quente é publicado de novo
  na passada seguinte, e só então passa a ser suprimido.
- **O estado fica em memória, por réplica, num singleton.** É podado pela janela e zerado quando a marca
  regride. Reinício e troca de réplica voltam, por uma janela, ao custo de hoje.

**Não é um quarto esquema de idempotência.** Não decide se um documento é processado, e nada depende
dele para correção: perder o estado só devolve a repetição. A garantia contra reenvio continua sendo o
hash por conteúdo (ADR-0016). É por isso que a alternativa rejeitada no ADR-0024 é adotada aqui, como
filtro de tráfego.

## Alternativas consideradas

- **Resolver só pelo perfil do tenant.** Quebra o fluxo XML de um tenant cujo ERP é o D365, ou obriga a
  mudar o seed de dev.
- **Ler o cabeçalho por `dataAreaId + Voucher`.** Sem mudar o Locator, mas é varredura por documento, e
  fica ambíguo se o voucher reiniciar por exercício.
- **Buscar a `FSTaxTransBR` sempre.** Permitiria comparar tudo, mas é uma chamada a mais em toda nota
  para detectar o que não temos como tratar além de parar.
- **Complemento genérico por zero.** Levaria valor de imposto suspenso ou não tributado para a nota.
- **Zerar o grupo IBS/CBS quando ausente.** Inventa alíquota e valor zero na foto do domínio.
- **Supressão dentro do adapter do D365.** Não mexe no contrato, mas cada feed novo teria que
  reimplementar; a sobreposição é conceito do poller.
- **Supressão só pelo documento, ou por duplicate detection do Service Bus** (`MessageId =
  tenant:chave`). Perde alteração real dentro da janela.
- **Encurtar a sobreposição.** Troca custo por risco de perder nota.
- **Registro persistido em tabela.** Sobrevive a reinício, ao custo de uma tabela e de uma escrita por
  referência. Fica para quando o reinício pesar.

## Consequências

**Melhora**

- **A fila de descoberta fecha o caminho do D365 até a esteira**, sem salto nem armazenamento
  intermediário.
- **Nenhum valor fiscal é escolhido em silêncio.** Onde fiscal e contábil divergem fora do padrão
  conhecido, o documento para, de forma visível.
- **Uma alteração vira cerca de uma montagem em regime**, sem encolher a sobreposição.
- **Motor genérico.** Outro ERP entra implementando o feed e o source; a supressão e o resolver valem
  para ele.

**Piora**

- **Nenhuma nota do D365 chega à Avalara nesta fatia.** As notas 55 da base são de 2016, sem IBS/CBS, e
  o `cClassTrib` não é resolvível. O desfecho esperado no fiscosysdev é: 5 rejeitadas, 9 `SE` ignoradas,
  0 enviadas.
- **Nota confirmada e depois cancelada passa a `Ignored`.** O `ExternalId` é preservado, mas o
  cancelamento não é despachado.
- **Dead-letter com motivo genérico** (`MaxDeliveryCountExceeded`). A causa fica no log e na foto da
  fonte.
- **Consumidor serial.** Concorrência maior exige serializar por documento antes.

**Pendências (próximas fatias)**

- Nota de serviço, com domínio de serviço: CCM, município de prestação, ISS e item da lista.
- Entidade de `CClassTribTable_BR` no pacote D365.
- Despacho de cancelamento, com os status configuráveis do ADR-0023.
- Impostos, retenções e encargos no parser do XML.
- Persistir o registro de publicações, se reinício ou troca de réplica pesarem.
