## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código, conferido em 2026-10-01, incluindo onde a leitura
refinou uma premissa do pedido.

- **O diretório e a descoberta por período não sabem de tenant nem de ERP.**
  - **O diretório:** o `ICompanyDirectory` não recebe tenant. O `JsonCompanyDirectory` é a única implementação, e o host o
    registra em qualquer ambiente, com o `companies.json`.
  - **A descoberta:** o `IDocumentDiscovery` tem `Origin`, mas ninguém escolhe por ela. O `LocalDocumentDiscovery` é o
    único registrado, também em qualquer ambiente. O `IntegrationRunner` e o `POST /documents/{t}/{k}/reprocess` recebem
    essa instância direto.
  - **A premissa refinada:** o pedido trata o diretório como a única peça que falta para o agendamento funcionar com o
    D365. Não é. Com o diretório certo e a descoberta de hoje, agendar `44278225000260`/`SP-01` acha 0 notas, porque o
    catálogo local só conhece os XMLs de exemplo. A descoberta por período do D365 entra na fatia por isso.
- **O D365 só é lido pelo coletor, pela montagem e pelo teste de credencial.**
  - **O que o coletor faz:** o `D365ChangeFeed` lê a `FSFiscalDocumentBRs` por janela de `SysModifiedDateTime`, com
    keyset em (`SysModifiedDateTime`, `FiscalDocumentRecId`). Ele monta a referência e o grupo no `Map` e no `Group`,
    privados.
  - **O que o adapter já tem:** o `D365ODataClient`, com o throttling, e o `ID365TokenProvider`, singleton e com cache. Em
    Development, o `D365DevelopmentTokenProvider` usa a credencial do tenant quando o `auth` está completo e o segredo
    está no cofre, e senão cai no Azure CLI. Ele loga, uma vez por tenant, qual identidade autenticou.
- **O CNPJ perde as letras em seis lugares.**
  - **No D365:** o `D365HeaderValues.Digits` monta a empresa do grupo no feed (`D365ChangeFeed.cs:180`). A montagem o usa
    para o CNPJ e o CPF das partes, e também para o NCM e o CFOP.
  - **Na Avalara:** o `AvalaraOutboundSettings` tem uma cópia própria do `Digits`, nas chaves do `establishments`, no
    `CodesFor` e no `PartiesOf`. O `GoodsInvoiceToAvalara` tem outra, no parceiro (`cnpj` com 14, `cpf` com 11) e no CEP.
    Com o CNPJ alfanumérico, o parceiro sai sem documento, em silêncio.
  - **No caminho de XML:** o `GoodsInvoiceMetadataExtractor.FromIssuer` filtra com `char.IsDigit`.
  - **No front:** o `formatCompany` só mascara o que casa com `/^\d{14}$/`.
- **Os cards somam no navegador.**
  - **O que a `GroupsPage` faz:** filtra o `/groups` por `referenceDate === hoje` e soma os campos.
  - **O limite:** o `/groups` devolve os 200 grupos mais recentes, ordenados por data. Numa janela de 30 dias, um tenant
    com alguns estabelecimentos passa de 200, e a soma ficaria menor que a real, sem aviso.
- **O grupo não tem modelo, e o modal não filtra pela linha.**
  - **A chave do grupo:** é (empresa, filial, dia, tipo, modo). O `DocumentModel` é gravado em todo registro com grupo,
    pela montagem e pela descoberta.
  - **O modal:** consulta só por (empresa, filial, dia). É o item aberto do STATUS "O modal do grupo não filtra pelo tipo
    e pelo modo".
- **O registro do documento é um por (tenant, chave natural), e não guarda a origem.**
  - **Quando o modo muda:** o `UpsertAsync` de uma linha existente não troca o modo. O `RecordMetadataAsync` só o troca
    quando a referência traz um modo explícito.
  - **O efeito:** uma NFS-e que o coletor registrou e que um agendamento descobre de novo continua na mesma linha, com o
    modo `Automatic`.
- **As duas filas consomem uma mensagem por vez, cada uma por conta própria.**
  - **O consumo:** os dois `ServiceBusTriggerService` têm `MaxConcurrentCalls = 1` e o mesmo processador
    (`QueuedDocumentProcessor` e `DocumentRouter`).
  - **As filas:** o coletor publica na `documents-discovered`, que é o serviço keyed `"discovery"`. O runner e o
    reprocesso publicam na `documents-in`.
  - **O risco:** duas cópias da mesma nota, uma em cada fila, passam juntas pela checagem de idempotência.
- **O período chega com fuso.**
  - **A tela:** manda `T00:00:00-03:00` e `T23:59:59-03:00`.
  - **O agendador:** calcula o dia cheio em BRT (`DayStart`, `DayEnd`).
- **O `establishments` não está na tela.**
  - **Onde ele existe:** no seed (`44278225000180` e `12345678000190` no sandbox do tenant-a) e por SQL.
  - **O que a tela faz:** o `adapterSchemas.ts` declara que ela o preserva ao salvar, sem mostrá-lo.
  - **A premissa refinada:** o pedido fala em "continua à mão na tela". Hoje ele nem está na tela.
- **A role no disco.** O `FSFiscalHubIntegration.xml` referencia 22 privilégios `FS*` e o `FiscalEstablishmentEntityView`
  padrão, sem commit e sem deploy. A nota da receita `06`, sobre role e privilégio não terem o terceiro lugar no
  `XppMetadata`, também está no disco.
- **A conta do "30 dias" da prova.**
  - **A conta do STATUS:** ele diz que, com 30 dias, as NFS-e de 2026-08-07 entram "nos cards de 2026-09-06".
  - **Na regra escolhida (D11):** a janela de 2026-09-06 vai de 2026-08-08 a 2026-09-06.
  - **Em 2026-10-01, data deste planejamento:** 2026-08-07 está a 55 dias, fora de qualquer janela.

## Goals / Non-Goals

**Goals:**

- **Uma regra só de escolha.** O diretório e a descoberta por período são escolhidos pela mesma regra: o adapter de
  entrada do perfil, e o fallback só em Development. É o desenho da resolução do source e do Azure CLI do D365.
- **Uma referência só.** O coletor e a descoberta por período montam a referência pelo mesmo código. Assim, o mesmo
  cabeçalho dá a mesma chave, o mesmo locator e o mesmo grupo por construção, e não por cuidado.
- **Uma normalização só.** Ela fica no Domain, e é usada pelo D365, pela Avalara e pela Application.
- **Os cards contados no servidor,** sobre todas as notas do período.

**Non-Goals:**

- **Generalizar a descoberta por período além do D365.** A porta já é genérica, e nenhum outro ERP tem adapter.
- **Uma trilha de auditoria da execução manual.** Fica o registro de execução que já existe.

## Decisions

### D1. O cadastro de estabelecimentos, numa leitura comum do adapter

Uma leitura interna do adapter do D365 serve o diretório (D2) e a descoberta por período (D4).

- **A consulta:**
  `GET {url}/data/FiscalEstablishments?cross-company=true&$select=dataAreaId,FiscalEstablishmentId,CNPJ,Name`.
- **As empresas do perfil:** com `companies` preenchido, entra o `$filter` por `dataAreaId`, com o mesmo escape do feed.
  Vazio, sem filtro.
- **O que ela usa:** a URL e a credencial das settings do perfil, o `ID365TokenProvider` do coletor, e o
  `D365ODataClient` com as opções de throttling do feed.
- **As páginas:** são seguidas pelo `@odata.nextLink`, pelo `GetAllAsync`. O cadastro é pequeno e não é janela móvel. É o
  mesmo argumento das linhas de um documento lançado, e não o do feed.
- **O `$select`:** os quatro campos. IE, CCM e o grupo do estabelecimento ficam fora, porque nada os usa
  (`d365/07`, "Não exponha campo porque pode ser útil").
- **A linha sem CNPJ ou sem código:** fica fora, com aviso no log que cita o `dataAreaId` e o código.
- **O CNPJ:** é normalizado (D8). O valor cru não sai da leitura.

**A falha vira um motivo seguro.** O adapter traduz as falhas numa exceção da Application, a `OriginUnavailableException`,
com o motivo curto da tela. O detalhe vai para o log, como no teste de credencial. A tradução:

| Falha | Motivo |
|---|---|
| HTTP 403 | "O F&O negou a leitura do cadastro de estabelecimentos (HTTP 403). A role FSFiscalHubIntegration precisa do privilégio FiscalEstablishmentEntityView, com deploy no ambiente, e a role precisa estar atribuída ao app." |
| Outro HTTP | o status, sem o corpo |
| Throttling acima do teto | "O F&O pediu espera." |
| A credencial recusada pelo Entra ID | "A credencial do ERP foi recusada", sem o código AADSTS, que vai para o log |
| Settings inválidas (`ConnectorSettingsException`) | a mensagem da configuração, sem rede |

**Por que a entidade da Microsoft, e não uma `FS*`.** As nossas entidades existem porque a Microsoft não publica entidade
sobre a `FiscalDocument_BR` (ADR-0022). Ela publica a `FiscalEstablishmentEntity`: pública, de nome fixo
(`FiscalEstablishments`) e verificada contra o fiscosysdev em 2026-10-01. Uma `FS*` por cima seria um contrato a mais
para manter, sem ganho. O `d365/04` §7 já a previa "para resolver o `DataAreaId` a partir do CNPJ".

### D2. A porta com o tenant, e a escolha pelo adapter de entrada

- **A porta:** o `ICompanyDirectory` ganha `Origin`, e as duas operações recebem o tenant:
  `ListCompaniesAsync(tenantId)` e `ListBranchesAsync(tenantId, companyCode)`. O modelo `Company`/`Branch` não muda.
- **A escolha:** uma resolução na Application pega a implementação cujo `Origin` é igual ao adapter de entrada do perfil,
  com comparação ordinal, como a `InboundSourceResolver`. Sem perfil, ou sem implementação, vale o fallback de
  desenvolvimento (D3), se houver.
- **A mesma regra vale para o `IDocumentDiscovery`,** que já tem `Origin`.
- **A consulta que o host chama:** um caso de uso da Application tira o tenant do `ITenantContext` e devolve um de três
  desfechos.

  | Desfecho | HTTP | Corpo |
  |---|---|---|
  | a lista | 200 | a lista |
  | sem diretório | 404 | "O ERP deste tenant (`iScala`) não tem diretório de empresas no hub." |
  | falha da origem | 502 | o motivo seguro do D1 |

- **Por que o tenant na porta, e não o `ITenantContext` no adapter:** a descoberta é chamada pelo agendador, que não tem
  requisição HTTP. O diretório segue a mesma forma, e o adapter lê o perfil pelo tenant, como o feed. É o que o item do
  STATUS pede: "a porta ganha o tenant".
- **O diretório do D365:** agrupa os estabelecimentos do D1 pelo CNPJ normalizado.
  - **A empresa:** tem o nome do estabelecimento de menor código.
  - **As filiais:** são os códigos, com os nomes.
  - **A ordem:** as empresas pelo código, e as filiais pelo código.
  - **As filiais de uma empresa:** a mesma leitura, filtrada.
- **Alternativa considerada:** uma lista de empresas tirada dos documentos processados. Foi descartada pelo ADR-0013: o
  diretório precisa mostrar estabelecimento sem movimento, como o `RJ-01`.

### D3. O fallback só em Development, no desenho do Azure CLI

O `companies.json` e o catálogo local passam a ser registrados só quando o host chama dois métodos explícitos, só em
`IsDevelopment()`:

- `UseJsonCompanyDirectoryAsDevelopmentFallback(path)`;
- `UseLocalDocumentDiscoveryAsDevelopmentFallback()`.

É o espelho do `UseD365AzureCliFallback`. Eles entram como serviço keyed com a chave de fallback da Application, e não
como implementação comum.

- **O que o fallback faz:** responde só quando o adapter de entrada do tenant não tem implementação. Hoje, isso é o
  tenant-b, com o `iScala`.
- **O que ele não faz:** nunca responde pelo tenant-a, que é do `Dynamics365`.
- **O mock e o tenant:** o `JsonCompanyDirectory` recebe o tenant e o ignora, porque é dado de desenvolvimento. Isso fica
  escrito no código.
- **Fora de Development:** nenhum dos dois existe. Hoje os dois sobem em qualquer ambiente, e o dropdown de produção
  mostraria a "Empresa Emitente LTDA" a todo tenant. É o vazamento registrado no ADR-0028, e esta decisão o fecha.
- **Por que não apagar o JSON e o catálogo:**
  - o catálogo serve o reprocesso das notas de exemplo do `Seed:DemoData` (D5);
  - o par é o caminho de desenvolvimento de um tenant sem adapter de ERP.

  Mantê-los registrados em todo ambiente é o defeito; mantê-los no código, só em Development, não é.
- **A regra de não depender de lembrança:** o host registra pelo código, sob `IsDevelopment()`. Nenhum passo manual no
  RUNNING.

### D4. A descoberta por período do D365

O `D365DocumentDiscovery` (origem `Dynamics365`, scoped, porque lê o perfil) entra no DI do adapter, ao lado do feed.

1. **As settings primeiro:** a configuração inválida falha sem rede.
2. **Os estabelecimentos:** vêm da leitura do D1. São os de CNPJ normalizado igual à empresa pedida e, com filial, o de
   código igual a ela. Sem nenhum, o resultado é vazio, com uma linha de log informativa, e sem falha.
3. **Os dias:** o dia do início e o do fim, cada um no próprio fuso (`DateTimeOffset.Date`). A tela e o agendador mandam
   BRT. Fim antes do início dá vazio.
4. **A consulta:** sobre a `FSFiscalDocumentBRs`, com o mesmo `$select` do feed e:
   - `$filter`: `FiscalDocumentDate ge {d0}T00:00:00Z and FiscalDocumentDate le {d1}T23:59:59Z`, e
     `((dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-01') or …)`, e `FiscalDocumentNumber eq '{n}'` quando houver
     número;
   - `$orderby=FiscalDocumentRecId`, `$top` igual ao `pageSize` do perfil, e o keyset `FiscalDocumentRecId gt {último}`;
   - `cross-company=true`.
5. **Cada linha:** passa pelo mapeamento comum (D6) e por uma guarda. A linha cujo CNPJ normalizado não é o da empresa
   pedida fica fora, com log de depuração. A guarda cobre o documento com o CNPJ antigo de um estabelecimento que mudou de
   CNPJ, e mantém o resultado igual a "as notas cujo grupo é (empresa, filial)".
6. **A página curta** encerra a leitura.

**Por que o dia fiscal, e não a emissão:** é o dia do grupo e dos cards (`document-grouping`). O D-1 do agendamento
diário passa a ser "as notas com data fiscal de ontem".

**Por que esse literal de data:** o OData devolve o `FiscalDocumentDate` como `yyyy-MM-ddT12:00:00Z`. Os limites às
00:00:00Z e às 23:59:59Z do dia cobrem essa representação sem converter fuso.

**Verificado contra o fiscosysdev em 2026-10-01 (tarefa 1.1), com a sessão do Azure CLI.**

- **A consulta:**
  `FiscalDocumentDate ge 2026-08-07T00:00:00Z and FiscalDocumentDate le 2026-08-07T23:59:59Z and dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-01'`,
  com `$orderby=FiscalDocumentRecId`.
- **A resposta:** HTTP 200, com as duas NFS-e da `SP-01` daquele dia: `BRMF06-110000034` (RecId 68719477966) e
  `BRMF06-110000035` (RecId 68719478716). As duas vieram com `FiscalDocumentDate = 2026-08-07T12:00:00Z`.
- **O literal de data** (`FiscalDocumentDate ge 2026-08-07`) também funciona, com as mesmas duas linhas. Fica o de
  `DateTimeOffset`, que é o que o teste 5.3 fixa.
- **A data mais recente da `brmf`:** é 2026-08-07, das duas linhas acima. Não há nota nos últimos 30 dias, o que confirma
  o limite das provas 10.4 e 10.6 (D11).

**Por que o código do estabelecimento no servidor, e o CNPJ só na guarda:** o documento guarda o CNPJ como o F&O o
formata (`442782250002-60`), e o hub só conhece a forma normalizada. Filtrar o CNPJ no servidor exigiria adivinhar o
formato. O código (`FiscalEstablishment`) é exato, e o diretório dá o par `dataAreaId` e código.

**Por que keyset por RecId, e não o `nextLink`:** é o motivo do ADR-0024. O `nextLink` do F&O é offset, e pula linha
quando uma nota do período é alterada durante a leitura. O RecId é a chave primária, estável.

**O custo:** um GET no cadastro, mais um GET por página. A montagem vem depois, na fila, só para a NF-e 55, como no
coletor.

**A busca por chave (`FindByKeyAsync`), usada no reprocesso:**

- **A chave:** `dataAreaId|Voucher`, cortada no primeiro `|`, com as duas partes não vazias. Outra forma devolve `null`,
  sem rede.
- **A consulta:** `$filter=dataAreaId eq '{a}' and Voucher eq '{v}'`, com `$top=2` e o mesmo `$select`.
- **O resultado:**
  - nenhuma linha devolve `null`;
  - uma linha devolve a referência;
  - duas são falha que nomeia a chave, porque a chave natural deixou de ser única.
- **O custo:** o `Voucher` não lidera índice (ADR-0025). É uma varredura por clique, aceitável para um reprocesso
  manual.

### D5. O reprocesso pela ordem das descobertas

A resolução do D2 também dá a lista ordenada de candidatas de um tenant: a do adapter de entrada, e depois o fallback de
desenvolvimento, quando existe e é outro. O reprocesso pergunta a cada uma, nessa ordem, e vale a primeira que acha a
nota. Nenhuma achou: "Nota não encontrada na origem para reprocessar", como hoje.

- **Por que dá certo sem guardar a origem:** as formas de chave são disjuntas (`empresa|voucher` contra a chave de
  acesso de 44 dígitos), e o D365 recusa a outra forma sem rede.
- **O efeito:**
  - o reprocesso da nota do D365 passa a funcionar. Hoje ele responde "não encontrada", porque só o catálogo era
    perguntado;
  - a nota de exemplo, em Development, continua.
- **Alternativa considerada:** gravar a origem no registro do documento. Seria uma migração e uma escrita em todo
  desfecho, para um ganho que a ordem já dá. Fica para quando houver um ERP cuja chave se confunda com a de outro.

### D6. O mapeamento comum da referência do D365

O `Row`, o `$select`, o `Map` e o `Group` saem do `D365ChangeFeed` para um tipo interno do adapter, usado pelo feed e
pela descoberta.

- **Por que:** um mapeamento só é o que faz "a mesma referência" valer por construção. O teste lê o mesmo cabeçalho pelos
  dois caminhos e compara as referências.
- **A origem:**
  - **no feed:** o poller põe a origem ao publicar;
  - **na descoberta:** a própria descoberta põe `Dynamics365` na referência. O runner não pode pôr pela origem da
    descoberta, porque a do catálogo local é `Local`, e a do documento dele é `Xml`.

### D7. O runner e o reprocesso publicam na fila de descoberta

- **No host:** o `IntegrationRunner` e o endpoint de reprocesso recebem o `IDocumentQueue` keyed `"discovery"`.
- **O que continua na fila de entrada:** o drop e o `/ingest`.
- **Por que:** a regra "uma por vez, para duas cópias não passarem juntas pela idempotência"
  (`discovery-queue-consumer`) vale por fila. O coletor já publica as notas do D365 na fila de descoberta. Uma execução
  agendada que publicasse a mesma nota na fila de entrada correria em paralelo com a cópia do coletor, e as duas
  passariam pela checagem antes de qualquer uma gravar o envio.
- **As referências XML do catálogo também vão para lá.** Os dois consumidores são o mesmo processador e o mesmo
  roteador, e nada muda para elas além do nome da fila.
- **Alternativas consideradas:**
  - **a fila pela origem** (D365 na de descoberta, XML na de entrada): é uma regra a mais, sem ganho;
  - **uma fila só para tudo:** é uma mudança maior, fora desta fatia.

### D8. A normalização do CNPJ e do CPF

- **A função:** `TaxIdentifiers.Normalize(string)`, pura, no Domain (`Goods`, ao lado da `Party`). Ela tira `.`, `/`,
  `-` e espaço, e preserva o resto.
- **Onde ela entra:**
  - no grupo do feed e da descoberta (D6);
  - no CNPJ e no CPF das partes e do estabelecimento, na montagem;
  - no diretório e na guarda da descoberta;
  - nas chaves do `establishments`, no `CodesFor` e no `PartiesOf`;
  - no parceiro do payload, com `cnpj` para 14 caracteres e `cpf` para 11;
  - no `FromIssuer` do caminho de XML.
- **O que fica só com dígitos:** o NCM e o CFOP, no `D365HeaderValues.Digits`, que perde o comentário do CNPJ. Também o
  CEP, na Avalara.
- **A comparação:** é ordinal, depois de normalizar os dois lados. A caixa é preservada, como o pedido diz (Risks).
- **Nada a migrar:**
  - **o resultado:** para documento numérico, é idêntico ao de hoje, e a base não tem CNPJ alfanumérico (o fiscosysdev
    só tem numérico);
  - **a impressão de conteúdo:** o canônico do D365 é feito das respostas cruas, e não do domínio. O
    `D365Canonicalizer.Version` não sobe.
- **No front:** o `formatCompany` mascara quando o código tem 14 caracteres, pelo tamanho, e não pelo tipo.

### D9. Os cards: a contagem por modelo, no servidor

- **A consulta:** o `IDocumentQueries` ganha `CountByModelAsync(from, to)`, escopado pelo tenant. Ele conta as notas com
  grupo cuja data de referência está entre `from` e `to`, inclusive. A data é texto `yyyy-MM-dd`, cuja ordem é a da data.
  - **As faixas de status:** são as mesmas do `DocumentGroup`, escritas num lugar só, para os cards e a tabela não
    divergirem.
  - **O resultado:** uma linha por modelo, com o total, finalizadas, em processamento e com erro.
- **O endpoint:** `GET /groups/totals?from=yyyy-MM-dd&to=yyyy-MM-dd` devolve 200 com a lista. Data ausente, inválida, ou
  `from` depois de `to`, dá 400.
- **Por que por modelo numa resposta só, e não `?model=`:**
  - trocar o modelo na tela é imediato;
  - os modelos do período, que entram nas opções do filtro, saem da mesma resposta.
- **Por que não somar no navegador:** é o truncamento dos 200 grupos (Context).
- **Na tela:**
  - **a janela:** sai de uma função pura `periodWindow(days, today)`, testada no `vitest`;
  - **os seletores:** o de período (Dia, 7, 15 e 30 dias, com o Dia como padrão, e o personalizado, D11) e o de modelo;
  - **as opções de modelo:** os que o hub conhece, que são os do mapa padrão do ERP (`55` NF-e, `57` CT-e e `SE` NFS-e),
    mais os da resposta, mais o escolhido, se ele sumir. Na primeira versão, eram só os da resposta, e a conferência na tela
    (2026-10-02) achou o dropdown vazio: num banco sem nota, ou num dia sem nota, não sobrava nenhum;
  - **a nota de cada card:** "no dia de hoje", "nos últimos N dias", ou "no período escolhido";
  - **sem texto da janela ao lado dos filtros:** a primeira versão repetia a janela ("2026-10-02, pela data fiscal"), e a
    conferência na tela pediu para tirar;
  - **a atualização:** a cada 5 segundos, como os grupos.

### D10. O grupo ganha o modelo, e o modal filtra pela linha

- **O grupo:** o `ListGroupsAsync` agrupa também pelo `DocumentModel`, e o `DocumentGroup` ganha o `Model` (anulável).
  - **A tabela:** ganha a coluna "Modelo", depois da "Data".
  - **O `rowId`:** inclui o modelo.
- **O modal:** o `ListByGroupAsync` ganha filtros opcionais de tipo, modelo e modo.
  - **A rota:** é a mesma, com `?type=&model=&trigger=`.
  - **O modo `Automatic`:** casa também o modo nulo, como o `/groups` serve.
  - **Sem os filtros:** a consulta é a de hoje.
  - **O que a tela manda:** sempre os três, da linha.
- **O efeito:** o título do modal (`group.total`) e a lista contam as mesmas notas. Isso fecha o item do STATUS.
- **A troca da chave:** só separa linhas quando dois modelos caem no mesmo tipo, por um `modelTypes` próprio do tenant.
  Nada persiste o `rowId` no front.

### D11. A janela do período

Os últimos N dias são hoje e os N−1 anteriores, inclusive. O "Dia" é N = 1.

- **Por que não [hoje − N, hoje]:** o "Dia" viraria dois dias.
- **O personalizado** (pedido da conferência na tela, 2026-10-02):
  - **o que é:** duas datas, de e até, inclusive, mandadas como estão ao `GET /groups/totals`, que já aceita qualquer
    janela;
  - **como começa:** preenchido com a janela que estava escolhida;
  - **o que a tela recusa:** com uma das datas vazia, ou a inicial depois da final, a tela diz o problema e não pede a
    contagem;
  - **o que ele resolve na prova:** alcança as notas de 2015 a 2026-08-07, o que nenhuma janela fixa alcança em
    2026-10-02, e com isso o filtro por modelo contra o modal (10.6) fica provável na tela.
- **Na prova:** a conta do STATUS (Context) erra por um dia nesta regra. O cenário da spec usa 2026-09-05, em que a
  janela de 30 dias começa em 2026-08-07.
- **Em 2026-10-01:** nenhuma janela alcança 2026-08-07. A prova manual do "30 dias entra" precisa de uma nota com data
  fiscal nos últimos 30 dias, lançada no fiscosysdev, ou fica só no teste com relógio fixo (tarefa 10.4). A tarefa
  registra qual foi. A prova do filtro por modelo contra o modal (10.6) tem o mesmo limite.

### D12. O `establishments` continua configuração digitada

**O mapa não é semeado pelo diretório.**

- **Os códigos:** `codigoEmpresa` e `codigoContribuinte` são da plataforma, e nunca vêm do ERP (ADR-0026 §3). Semear
  pelo diretório daria só a chave.
- **Uma entrada sem os códigos:** é rejeitada do mesmo jeito. Muda só o texto: "sem codigoEmpresa e codigoContribuinte",
  no lugar de "não tem tradução".
- **Escrita sem autor:** semear faria o hub escrever configuração que o Admin não digitou, e que mudaria sozinha quando o
  ERP ganhasse uma filial.

**O que o diretório muda.** Uma execução manual ou agendada da `SAL-01` ou do `RJ-01` que traga NF-e 55 é rejeitada com
"não tem tradução para o estabelecimento …". É o desfecho certo, visível e com motivo. As notas da `SP-01` no fiscosysdev
são NFS-e, que são ignoradas e não chegam à plataforma.

**O seed.** O `12345678000190` fica no `establishments` do tenant-a, para o caminho de XML (`/ingest` e `/drop`).

- **O que sai da tela:** com o diretório do D365, a "Empresa Emitente LTDA" sai do dropdown do tenant-a.
- **O que isso fecha:** a ressalva do STATUS ("uma integração manual para a Empresa Emitente LTDA manda a nota de exemplo
  ao sandbox"), pelo caminho da tela.

**O próximo passo, no STATUS:** a tela da tradução, com uma linha por estabelecimento do diretório e os dois códigos
digitados pelo Admin.

### D13. A role, o deploy e a prova com a credencial do conector

- **O privilégio:** o `FiscalEstablishmentEntityView` (Read: Allow) é o privilégio padrão da Microsoft para a entidade.
  Ele entra na `FSFiscalHubIntegration` pelo repositório e pelo modelo no UDE.
  - **Os lugares:** role e privilégio não têm o terceiro lugar no `XppMetadata` (a nota da receita `06`).
  - **O que o deploy exige:** build e deploy do modelo, sem sync de banco, porque nenhuma tabela nem entidade mudou.
- **O pacote:** continua metadado puro. São 22 entidades, 22 privilégios próprios, uma role e um privilégio padrão
  referenciado.
- **A prova "funciona com a credencial do conector":** em Development, a linha do `D365DevelopmentTokenProvider` diz qual
  identidade autenticou o tenant. A prova exige que seja o app do perfil (client credentials), e não o Azure CLI. Antes
  do deploy, a mesma leitura dá o 403 do D1.

### D14. A tela de integrações

- **O dropdown de empresa:** mostra "44.278.225/0002-60 — Filial de serviços", e o valor é o código sem máscara.
- **O dropdown de filial:** mostra "SP-01 — Filial de serviços".
- **As tabelas:** a coluna "Empresa" das execuções e dos agendamentos usa a mesma máscara.
- **O 404 e o 502:** o texto do servidor aparece no lugar do dropdown, e os botões de executar e de agendar ficam
  desabilitados.
- **O 409 da execução sem descoberta,** e o 502 de uma falha da origem durante a execução, aparecem no banner da tela,
  como a mensagem de sucesso aparece hoje.

## Risks / Trade-offs

- **[O filtro de data do OData é premissa]** → A tarefa 1.1 o confere no fiscosysdev antes do código. Se o literal for
  outro, o teste fixa o que funcionou.
- **[Estabelecimento removido do cadastro]** → As notas dele no período não são achadas pela descoberta por período,
  porque o código vem do cadastro. O coletor continua achando as alterações. Vai para o STATUS como limite conhecido.
- **[A caixa preservada]** → `12abc…` não casa com uma chave `12ABC…` no `establishments`. O desfecho é uma rejeição
  visível, e não uma junção silenciosa. As letras do CNPJ são maiúsculas pela regra da Receita, e o hub não as inventa.
- **[O cadastro lido a cada abertura do dropdown e a cada execução]** → A entidade é pequena, o token tem cache, e o front
  guarda a lista por 5 minutos. Um cache no servidor fica para quando houver medida.
- **[O agendamento de um tenant sem descoberta]** → Ele retenta a cada passada, como qualquer falha de agendamento hoje.
  A tela não deixa criar um, porque sem diretório não há empresa. Um agendamento antigo do tenant-b, fora de
  Development, cairia nisso. Vai para o STATUS.
- **[O reprocesso da nota do D365 é envio real]** → O botão só aparece na nota com falha, e o gatilho manual fura a
  idempotência, como no XML. É o comportamento que o reprocesso já prometia.
- **[O 403 tem mais de uma causa]** → O motivo cita as duas: o privilégio sem deploy, e a role não atribuída ao app.
- **[O fallback lista o mock para o tenant-b em Development]** → É o comportamento de hoje, agora restrito a
  Development. O mock ignora o tenant, e isso está escrito no código.
- **[Duas leituras do cadastro por execução manual]** → Uma é do dropdown, e a outra da descoberta. É aceitável, e
  evita passar a lista do front para o servidor.

## Migration Plan

1. **No D365 (o Marcelo):** build e deploy do modelo com a role, sem sync. A ordem com o hub não importa. Sem o
   privilégio, o dropdown mostra o motivo do 403, e o resto do hub não muda.
2. **No hub:** não há migração de banco. Front e back sobem juntos.
3. **Rollback:** reverter o commit. O privilégio na role pode ficar, porque é só leitura.
