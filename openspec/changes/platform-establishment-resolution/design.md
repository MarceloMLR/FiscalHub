## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código, conferido em 2026-10-02, incluindo onde a leitura
refinou uma premissa do pedido.

- **A tradução vive inteira no `AvalaraOutboundSettings`.**
  - **A leitura:** o `Read` monta o dicionário das chaves de `establishments`, normalizadas pelo `TaxIdentifiers`. Sem a
    tabela, ele guarda o `_establishmentsProblem`, e todo envio do tenant é rejeitado ("Configuração obrigatória", ADR-0026).
  - **Os códigos:** o `CodesFor` devolve o par da entrada ou rejeita: sem entrada, ou com um campo faltando.
  - **A parte nossa:** o `PartiesOf` usa a emissão própria ou de terceiros quando a nota diz (D365). Quando não diz (XML),
    usa a presença na tabela.
- **O dispatcher lê o perfil a cada nota.** O `SubmitAsync` faz `_profiles.GetAsync`, `Read`, `PartiesOf` e `CodesFor`, e
    só depois mapeia, fotografa e envia. Toda rejeição de configuração sai antes do envio, como
    `DispatchRejectedException`.
- **Os precedentes que esta fatia reusa:**
  - **A capacidade declarada:** a porta `IConnectorCredentialTest`, na Application. O adapter registra a implementação com o
    nome dele, e o `ConnectorCredentialTestService` escolhe pelo adapter gravado no perfil. Sem implementação, a resposta é
    "sem teste", e não uma falha.
  - **A busca única e a recusa lembrada:** o `AvalaraTokenProvider`. Ele faz uma busca por chave sob concorrência, lembra a
    recusa por `CredentialRefusalHold` e esquece o tenant pelo `IConnectorProfileObserver`.
  - **A expiração absoluta:** o `D365ReferenceDataCache` (design D13 da montagem do D365), que expira sobre o
    `TimeProvider`.
- **Um dispatcher de saída só.** O `AvalaraComplianceDispatcher` é a única `IComplianceDispatcher<GoodsInvoice>`. O "Mock"
    da tela de conectores é o mesmo adapter apontado para a URL do mock. Hoje, o "destino que não sabe listar" só existe
    numa composição de teste, ou num perfil gravado com outro nome de adapter.
- **O consumo é de uma mensagem por vez por fila, mas há mais de um caminho.** Os `ServiceBusTriggerService` têm
  `MaxConcurrentCalls = 1`. Ainda assim, as duas filas, o `/ingest` e o reprocesso podem despachar notas do mesmo tenant ao
  mesmo tempo. A busca única não é teórica.
- **A premissa refinada: as notas da `brmf`.** As cinco NF-e 55 do fiscosysdev, nas fixtures, são todas da `Matriz`
  (`442782250001-80`). A `SP-01` e a `SAL-01` só têm NFS-e, que são ignoradas e não chegam ao envio, e a `RJ-01` não tem
  nota. Por isso, "os quatro estabelecimentos resolvem e despacham" só se prova com nota real na `Matriz`. Os outros três
  se provam no ponta a ponta automatizado, com o cabeçalho da fixture trocado, como no teste do CNPJ alfanumérico (D12).
- **O seed de hoje** traduz a `Matriz` (`44278225000180`) e o CNPJ dos XMLs de exemplo (`12345678000190`) para
  `20247332000182`, no sandbox do tenant-a.

## Goals / Non-Goals

**Goals:**

- **A regra num lugar só.** O casamento, a recusa por falta e por duplicidade, a janela e a recusa lembrada ficam no
  núcleo, iguais para qualquer destino que saiba listar. O adapter fala HTTP e diz o que a plataforma chama de quê.
- **Recusar em vez de escolher, por construção.** O resultado do casamento não tem a forma "um escolhido entre vários".
- **O caminho de hoje intocado** para quem tem a tabela preenchida: nenhuma chamada nova, nenhum modo de falha novo.
- **A contagem de chamadas provada,** em três níveis (D12).

**Non-Goals:**

- **Uma porta genérica de "códigos do destino" no `DispatchContext`.** A resolução continua sendo feita pelo adapter, que
  sabe qual parte é a nossa e onde está a sobreposição. Só a listagem e a regra sobem.
- **Generalizar o vocabulário.** O registro do núcleo diz "código da empresa" e "código do estabelecimento". O que isso é
  em cada plataforma é do adapter.

## Decisions

### D1. A listagem é uma porta opcional da Application, no desenho do teste de credencial

```csharp
// Application/Outbound
public interface IPlatformEstablishmentListing
{
    string Adapter { get; }   // o nome do adapter de saída, como o perfil o grava ("Avalara")
    Task<IReadOnlyList<PlatformEstablishment>> ListAsync(TenantConnectorProfile profile, CancellationToken ct = default);
}

public sealed record PlatformEstablishment(
    string TaxId,               // normalizado (TaxIdentifiers)
    string? CompanyCode,        // Avalara: codigoCIA, texto como veio
    string? EstablishmentCode,  // Avalara: codigo, texto como veio
    string? PlatformId,         // Avalara: contribuinteId — a identidade do contribuinte (D4), e o #id do motivo (D9)
    string? CompanyName);       // Avalara: descricao — só para o motivo (D9)
```

- **O que o registro carrega e o que não carrega.** A `descricao` da empresa vem no `$select` (D6) só para o motivo. A
  `razao` do contribuinte e o `empresaId` não são carregados: o casamento não usa nenhum dos dois, e o motivo também não.

- **O contrato da falha:** a recusa permanente é `DispatchRejectedException`. Qualquer outra exceção é indisponibilidade.
  É a mesma divisão que o dispatcher já faz, e a esteira já sabe o que fazer com as duas.
- **O que a porta lista:** os estabelecimentos do ambiente ativo do perfil, completos, ou nada (D7).
- **Os códigos anuláveis:** um candidato sem código continua sendo candidato (D4). Filtrá-lo na leitura esconderia uma
  duplicidade.

**Por que uma porta separada, e não um método na `IComplianceDispatcher<T>`.** O método obrigaria todo destino a listar,
ou a fingir. A porta separada é o que o pedido chama de "capacidade do adapter, não premissa do núcleo": quem não
registra, não lista, e o `establishments` continua sendo a única fonte.

**Alternativa descartada: um adapter da Avalara na `ICompanyDirectory`.** O ADR-0013 previa isso, com o mesmo fluxo de dois
passos (empresas e depois as filiais de cada uma). O ADR-0032 mudou a porta: ela é escolhida pelo adapter de **entrada** e
serve os dropdowns com a chave do ERP (o CNPJ e o `FiscalEstablishmentId`). A listagem é do adapter de **saída**, e os
códigos dela nunca vão para a tela. Na mesma porta, o dropdown passaria a mostrar o cadastro da plataforma, com `Padrão` e
`QA`, no lugar do cadastro do ERP.

**Alternativa descartada: tudo dentro do adapter da Avalara.** Seria menos código hoje. Mas a regra de recusa é do hub, e
não da Avalara: o próximo destino que listasse teria de reimplementar o casamento, a duplicidade e a janela, e um deles
poderia escolher onde o outro recusa.

### D2. O resolvedor: a regra, a janela e a recusa lembrada, no núcleo

O `PlatformEstablishmentResolver` (Application/Outbound) é singleton. Ele escolhe a listagem pelo `OutboundAdapter` do
perfil, pela comparação exata do nome, como o `ConnectorCredentialTestService`, e devolve um índice:

```csharp
Task<PlatformEstablishmentIndex> GetAsync(TenantConnectorProfile profile, CancellationToken ct = default);

// PlatformEstablishmentIndex
bool CanList { get; }                    // false: o adapter não declara a listagem, e nenhuma chamada saiu
bool Knows(string taxId);                // ao menos um contribuinte com o CNPJ (D5)
EstablishmentMatch Match(string taxId);  // Unique(candidato) | None | Ambiguous(candidatos)
```

- **O `Match` não tem como escolher.** Com mais de um candidato, o resultado é `Ambiguous` com a lista, e não há
  sobrecarga, parâmetro ou ordem que devolva um deles. A recusa é a única saída que o tipo oferece.
- **Quem escreve o motivo é o adapter** (D9). Ele sabe o vocabulário ("empresa", "contribuinte", `codigoCIA`) e o caminho da
  sobreposição na tela e nas settings. O núcleo devolve a estrutura.
- **O resolvedor é `IConnectorProfileObserver`** (D8), registrado no Host, como o `CredentialTestBrake`.

### D3. A precedência: a sobreposição primeiro, e a listagem só quando falta

O dispatcher passa a fazer, para os códigos:

1. **A entrada da tabela para o CNPJ.** Se ela existe, vale, e a listagem não é pedida. Uma entrada incompleta é recusada
   como hoje, e não é completada pela plataforma: quem escreveu a entrada disse qual é a tradução.
2. **Sem entrada, o índice.** Com `CanList = false`, a recusa de hoje, de falta de tradução. Com o índice, o `Match` (D4).

O `AvalaraOutboundSettings` perde o `_establishmentsProblem`. A tabela ausente vira uma sobreposição vazia. Uma tabela que
não é objeto JSON continua sendo erro de configuração, que nomeia o campo.

**Por que preguiçoso.** Um tenant com a tabela completa e notas do D365, que dizem a emissão, nunca chama a listagem. O
pedido diz que o que está configurado continua funcionando, e a leitura mais forte disso é "sem chamada nova e sem modo
de falha novo".

### D4. O casamento e os candidatos

- **A chave:** o `TaxIdentifiers.Normalize` dos dois lados, e comparação ordinal. A caixa não é convertida, pela regra da
  `tax-identifier-normalization`.
- **O contribuinte sem `cnpj`** fica fora do índice: nunca casaria.
- **A identidade:** o `PlatformId`. Duas linhas com o mesmo `contribuinteId` são um candidato. Uma linha sem identificador
  conta à parte. O erro, se houver, vai para o lado de recusar mais, e nunca para o de escolher.
- **Um candidato:** `Unique`. Se faltar o `CompanyCode` ou o `EstablishmentCode`, o adapter recusa nomeando o campo (D9).
- **Mais de um:** `Ambiguous`, sempre, mesmo que os códigos coincidam. A regra do pedido é "mais de um contribuinte", e
  dois contribuintes com o mesmo par de códigos em empresas diferentes ainda são duas escriturações possíveis.
- **Nenhum desempate.** Ficam fora, de propósito:
  - a ordem do retorno, que é o defeito do STATUS;
  - o código da empresa, que filtraria `Padrão`, `QA` e `SPL`. Seria lógica de cliente, e o código é texto livre;
  - o `codigo` coincidir com a ordem do CNPJ (`0002` e `"002"`), que é convenção de alguns clientes e não regra;
  - o candidato completo contra o incompleto.

### D5. A parte nossa da nota que não diz: a sobreposição ou a plataforma

Quando a nota não diz a emissão, uma parte é estabelecimento do tenant se tem entrada na tabela **ou** se o índice a
`Knows`. A regra de hoje vale sobre essa união: exatamente uma parte é a nossa. Nenhuma ou as duas são recusa citando os
dois CNPJs.

- **O custo:** a nota em XML pede o índice, mesmo com a tabela cheia, porque a outra parte pode ser filial nossa só na
  plataforma. É uma listagem por janela, como qualquer outra (D8).
- **Alternativa descartada: a tabela decide sozinha quando tem uma das partes.** Uma transferência entre a filial A (na
  tabela) e a filial B (só na plataforma) iria como nota da A, com a B de parceiro, sem aviso. Com a união, ela é recusada
  como ambígua, que é o comportamento já registrado no STATUS para as duas na tabela.

### D6. A listagem da Avalara

O `AvalaraEstablishmentListing` (internal, singleton, com um `HttpClient` nomeado e `RedactLoggedHeaders(_ => true)`):

- **As chamadas,** relativas à URL base da seção do ambiente ativo:
  - `GET {baseUrl}{CompaniesPath}?$select=empresaId,codigoCIA,descricao&$orderby=empresaId&$top={n}&$skip={k}`: o
    padrão é `taxcompliance/v2/empresa`. A resposta é um array puro;
  - para cada empresa, em sequência,
    `GET {baseUrl}{TaxpayersPath}?empresaId={id}&$select=contribuinteId,codigo,cnpj&$orderby=contribuinteId&$top={n}&$skip={k}`:
    o padrão é `taxcompliance/v2/contribuinte`. A resposta vem em `{"value": [...]}`.
- **Por que uma leitura por empresa** (Swagger conferido em 2026-10-02): o `empresaId` é obrigatório no `/contribuinte`.
  Não há como pedir um CNPJ na conta inteira, e mesmo que houvesse, a duplicidade exige ver todos.
- **A paginação, pelo cliente** (Swagger conferido em 2026-10-02). Os dois endpoints aceitam `$top` e `$skip`, e nenhum
  devolve `nextLink`:
  - **a ordem:** `$orderby=empresaId` nas empresas e `$orderby=contribuinteId` nos contribuintes. Sem ordem estável,
    paginar com `$skip` é indefinido: o mesmo item pode vir em duas páginas ou em nenhuma, sem erro do servidor. Com a
    ordem, um item repetido entre páginas não acontece, e a deduplicação pelo `contribuinteId` (D4) deixa de ser o que
    esconde o problema;
  - **o `$top`:** é fixo durante a listagem, e vem de `Avalara:ListingPageSize`, com 100 de padrão. O ponta a ponta e a
    prova no sandbox usam uma página pequena, para atravessar páginas de verdade. Zero ou negativo impede o host de subir,
    como a validade da janela (D8): com o `$top` 0, a primeira página viria vazia, e a conta pareceria não ter empresas;
  - **o `$skip`:** começa em 0 e soma, a cada página, os itens **recebidos** nela, e não o `$top`. Ele volta a 0 em cada
    empresa. Somar o `$top` perderia itens em silêncio diante de um servidor que limita a página: pedidos 100 e recebidos
    10, o `$skip` iria a 100 e os itens de 10 a 99 nunca seriam lidos. A parada na página vazia não salvaria nada, porque
    a leitura chegaria ao fim, só que com buracos;
  - **a parada:** só a página vazia. Uma página curta não encerra, porque pode ser o limite do servidor, e não o fim da
    lista. O custo é uma página vazia a mais por lista, e em troca a leitura não depende de premissa sobre o servidor;
  - **o teto:** 50 páginas por lista, em `Avalara:ListingMaxPages`, como guarda geral. Zero ou negativo impede o host de
    subir. A lista que não chega à página vazia dentro do teto recusa a listagem com "Contrato do destino", nomeando o
    endpoint e o teto, e a página seguinte não é pedida. **O teto é em páginas, e não em itens.** Ele cobre 49 páginas
    com itens mais a vazia que confirma o fim. O teto efetivo em itens é, então, 49 vezes o tamanho real da página, que é
    o menor entre o `$top` e o limite do servidor. Com páginas de 100, são 4.900 itens por lista. Se o servidor limitar
    a 10, são 490. A falha é alta nos dois casos, e por isso é segura;
  - **a paginação que não avança:** uma página não vazia igual à anterior, item a item, recusa a listagem com
    "Contrato do destino", nomeando o endpoint e o `$skip`. Com a ordem estável, uma plataforma que ignora o `$skip`
    devolve sempre a mesma página, e é isso que esta guarda pega na segunda página, sem gastar o teto. O teto pega o
    resto: uma plataforma que ignorasse o `$skip` e não ordenasse pelo campo pedido devolveria páginas diferentes e sem fim. Comparar item a item
    não depende de identificador, que pode faltar.
- **O `$select`:** só os campos que o hub usa, e a `descricao` da empresa, que deixa o motivo da duplicidade legível
  (D9). O `/empresa` é uma lista só por listagem, e o que ela economizaria sem a descrição não paga a legibilidade. No
  `/contribuinte`, que se repete por empresa, só `contribuinteId,codigo,cnpj`. Um campo que venha além do pedido é
  ignorado.
- **O `subscriptionId`:** opcional nos dois, e não é mandado (Open Questions).
- **Os caminhos, o tamanho da página e o teto nas `AvalaraOptions`,** ao lado do `DocumentsPath`. O padrão é o
  verificado, e o mock atende o mesmo caminho e os mesmos parâmetros. Assim nenhum passo do RUNNING precisa ser lembrado.
- **Em sequência, e não em paralelo.** Oito empresas são dezesseis requisições curtas (a página e a vazia de cada uma),
  uma vez por janela, mais uma por página a mais. Em paralelo seria mais rápido, e trocaria isso por pico de requisições e
  risco de 429, sem ganho que alguém veja.
- **O `empresaId`:** é usado como veio, número ou texto JSON, escapado na query.
- **Os códigos:** só texto JSON, e sem `Trim`. Um número JSON vira ausente (`avalara-establishment-listing`).
- **A credencial e o token:** a mesma `AvalaraOutboundSettings` e o mesmo `IAvalaraTokenProvider` do envio. A chave do
  cache de token é a mesma, então a listagem não pede token a mais. O 401 com o token do cache chama o `Invalidate`, como o
  dispatcher.
- **A redação:** o corpo passa pelo `SensitiveText` antes de virar motivo. A listagem não é fotografada (proposal,
  Non-goals). O log diz quantas empresas, quantos contribuintes e quantas páginas vieram, sem o conteúdo.
- **O nome do adapter:** a mesma constante que o `AvalaraCredentialTest` usa.

### D7. As falhas da listagem, e a completude

A tabela de desfechos está na spec `avalara-establishment-listing`. As decisões por trás dela:

- **Tudo ou nada.** A listagem só devolve depois de ler todas as páginas de todas as empresas. Qualquer falha no meio
  lança, inclusive numa página, e nada parcial chega ao índice. Uma listagem parcial é o mesmo defeito do STATUS por outro
  caminho: o segundo contribuinte do CNPJ estaria na empresa que falhou, e o primeiro pareceria único.
- **O 403 e o 401 com token novo são recusa,** como no envio (ADR-0027 §7). Pode haver credencial que envia e não lista.
  Por isso o motivo aponta a tabela `establishments` como saída.
- **O 404 é configuração,** como no envio. O motivo aponta as duas partes da URL: a URL base do perfil e o
  `Avalara:CompaniesPath` ou o `Avalara:TaxpayersPath` do appsettings. O texto do `SubmitPathNotFound` vira um formatador
  comum, parametrizado pelo método, pelo caminho e pelo nome da opção.
- **O formato fora do verificado é recusa de contrato.** Retentar devolveria o mesmo 200. O mesmo vale para a paginação
  que não avança e para o teto (D6).
- **A paginação é caso tratado, e não risco.** A versão anterior deste design recusava a listagem diante de um
  `nextLink`, e deixava o corte do `/empresa` como risco para o STATUS. O Swagger mostrou a paginação pelo cliente. Com a
  ordem estável, o `$skip` pelos itens recebidos e a parada só na página vazia, o truncamento silencioso deixa de existir,
  e não sobra premissa sobre o limite de página do servidor.

### D8. A janela: o cache, a busca única, o vencimento e a invalidação

- **A chave:** tenant e ambiente (o `Environment` do perfil, em minúsculas), como no pedido.
- **A janela é a validade.** A primeira resolução que precisa do índice o monta, e toda resolução do tenant e ambiente
  dentro da validade usa o mesmo. O vencimento é absoluto, sobre o `TimeProvider`, como no `D365ReferenceDataCache`. Não
  há varredura: são poucas entradas, uma por tenant e ambiente.
- **As opções:** `PlatformEstablishmentOptions`, lidas da seção `PlatformEstablishments` do appsettings do Host:
  - `CacheDuration` vale 10 minutos sem configuração. Um cadastro novo na plataforma aparece em minutos, e um lote de
    milhares de notas usa uma listagem;
  - `RefusalHold` vale 5 minutos sem configuração, o mesmo padrão da recusa da credencial;
  - **valor zero ou negativo impede o host de subir.** Uma validade zero seria a listagem por nota, que o pedido proíbe. É
    melhor falhar na subida que descobrir pela conta de chamadas.
- **A busca única:** uma tarefa em voo por chave. Quem chega durante ela aguarda a mesma tarefa e recebe o mesmo desfecho,
  sucesso ou falha. A tarefa não leva o `CancellationToken` de quem a começou, e cada um aguarda com o seu. Se a primeira
  nota fosse cancelada, as outras não deveriam herdar o cancelamento. O limite de tempo é o do `HttpClient`.
- **A falha não fica guardada,** com uma exceção: a `DispatchRejectedException` fica lembrada por chave pelo
  `RefusalHold`. Dentro dele, o resolvedor relança uma `DispatchRejectedException` com o mesmo motivo, sem chamada. Sem
  isso, uma credencial que não lista viraria uma tentativa por nota, que é o padrão que bloqueia conta (ADR-0027 §7).
- **A invalidação:** o `ProfileSavedAsync(tenant)` esquece o índice, a tarefa em voo e a recusa lembrada do tenant, em todos
  os ambientes. A ordem e o "salvar sem mudar também vale" são os do `ConnectorProfileService`, que já avisa os
  observadores.
- **Por processo,** como o token. Com duas réplicas, salvar o perfil esquece só na que atendeu o `PUT`, e a outra espera a
  validade. O item do STATUS das réplicas ganha esta linha.

**Alternativa descartada: reler a plataforma quando o CNPJ não é achado.** Com um teto (uma releitura por minuto), ela
corrigiria sozinha o cadastro recente. Mas um CNPJ que nunca vai existir na plataforma faria uma listagem por minuto
enquanto houvesse nota dele, e a conta de chamadas deixaria de ser "uma por janela". O motivo da recusa diz como forçar a
releitura (D9).

### D9. Os motivos

Todos começam pelo prefixo de sempre e citam o tenant e o ambiente (ADR-0026 §2). Nenhum repete valor de segredo.

| Caso | Motivo |
|---|---|
| Sem contribuinte | "Configuração do conector: o estabelecimento {cnpj} não tem contribuinte cadastrado na plataforma (tenant '{t}', ambiente '{e}'). Cadastre-o na plataforma, ou traduza-o em OutboundSettings.{e}.establishments. Se o cadastro acabou de ser feito, salve o perfil do conector (Configurações → Conectores) para o hub reler a plataforma, e reprocesse a nota." |
| Mais de um | "Configuração do conector: o estabelecimento {cnpj} tem {n} contribuintes na plataforma (tenant '{t}', ambiente '{e}'), e o hub não escolhe entre eles: empresa '{codigoCIA}' ({descricao}), contribuinte '{codigo}' (#{contribuinteId}); …. Remova a duplicidade na plataforma, ou traduza o estabelecimento em OutboundSettings.{e}.establishments." Por exemplo: "empresa '005' (RESULTA IND E COM...), contribuinte '001' (#10001)". Até cinco candidatos, e "e mais {k}". Código ausente aparece como "sem código", e descrição ou `#id` ausentes ficam de fora. |
| Contribuinte sem código | "Configuração do conector: o único contribuinte do estabelecimento {cnpj} na plataforma (empresa '{codigoCIA}') está sem o código do contribuinte (tenant…)." O mesmo para a empresa sem `codigoCIA`. |
| Destino sem listagem | o motivo de hoje, de falta de tradução em `establishments`. |
| A parte nossa | "nenhuma parte da nota (emitente {a}, destinatário {b}) tem tradução em OutboundSettings.{e}.establishments nem contribuinte na plataforma…", ou "as duas partes … são estabelecimentos do tenant (pela tradução ou pela plataforma), e a nota não diz qual é o próprio." |

- **A empresa pelo código e pela descrição, sem o `empresaId`.** O código e a descrição já a identificam para um humano.
  O `empresaId` não acrescenta nada legível.
- **O contribuinte pelo código e pelo `#contribuinteId`.** Dois contribuintes podem ter o mesmo código na mesma empresa, e
  é o caso que a identidade pelo `contribuinteId` (D4) existe para contar. Sem o `#id`, o motivo mostraria dois candidatos
  idênticos justamente quando a detecção mais importa. O `#id` também é endereço: a plataforma tem `GET
  /v2/contribuinte/{id}`, e quem lê a recusa vai direto ao registro, em vez de procurar pelo CNPJ.
- **O teto de cinco** mantém o motivo legível, e longe do corte de 1000 caracteres do registro.

### D10. O mock imita a listagem verificada

O `MockComplianceApi` ganha, com o Bearer exigido como nos caminhos de envio:

- **`GET /taxcompliance/v2/empresa`:** array puro;
- **`GET /taxcompliance/v2/contribuinte?empresaId=`:** `{"value": [...]}`. O `empresaId` é obrigatório, e sem ele o mock
  responde 400. Uma empresa desconhecida devolve `{"value": []}`;
- **o `$top` e o `$skip` nos dois:** o mock pagina de verdade, então uma lista maior que o `$top` só sai inteira se o hub
  pedir as páginas;
- **o `$orderby` nos dois:** o mock ordena pelo campo pedido. Sem `$orderby`, ele devolve numa ordem que muda a cada
  pedido, para que um hub que esquecesse a ordem falhasse no ensaio, e não por sorte;
- **um limite de página próprio, opcional** (`POST /admin/listagem/limite?itens=`): o mock devolve no máximo esse número
  por página, mesmo com um `$top` maior. É o ensaio do servidor que limita a página;
- **o `$select` nos dois:** o mock devolve só os campos pedidos. Um campo de que o hub dependesse sem pedir sumiria no
  ensaio, em vez de passar por sobra.

O que ele lista:

- **Uma empresa real** (`codigoCIA = "005"`, com descrição). Ela tem os quatro estabelecimentos da `brmf` e o CNPJ dos XMLs
  de exemplo, com códigos que **não** seguem a ordem do CNPJ: a `Matriz` não é `"001"`, e a `SP-01` não é `"002"`. Um
  código derivado da ordem falharia no ensaio, em vez de passar por coincidência.
- **As empresas de teste ao lado**, como no sandbox: `Padrão` e `QA`, com contribuintes de outros CNPJs.

Os modos, pela convenção do `/admin/*`, abertos porque são ferramenta de dev:

- **`POST /admin/contribuintes/adicionar?cnpj=&empresa=`:** põe um contribuinte com o CNPJ na empresa pedida (sem ela, a
  `QA`), com um identificador novo. Com um CNPJ que já está na listagem, é a duplicidade. Com um CNPJ alfanumérico, é o
  cenário da plataforma alfanumérica;
- **`POST /admin/contribuintes/remover?cnpj=`:** tira o CNPJ da listagem;
- **`POST /admin/contribuintes/restaurar`:** volta ao inicial, sem limite de página próprio;
- **`GET /admin/contribuintes`:** a listagem atual e os contadores de requisições de empresas e de contribuintes, que
  contam cada página. É o que a prova manual usa para a contagem.

O mock não separa a listagem por credencial: o tenant-a e o tenant-b veem a mesma. O isolamento entre tenants se prova no
teste do resolvedor (D12).

### D11. O seed, o RUNNING e o STATUS

- **O seed:** o `establishments` do sandbox do tenant-a passa a `{}`, porque o mock lista. Um banco novo exercita o
  caminho automático por padrão. A produção já estava vazia, e o tenant-b também.
- **Um banco de dev existente** guarda a entrada da `Matriz`, que continua valendo como sobreposição. O RUNNING diz isso,
  e mostra o SQL para esvaziar a tabela quando se quer provar o caminho automático.
- **O RUNNING:** a seção "Tradução dos estabelecimentos" passa a descrever a resolução pela plataforma, a sobreposição
  opcional, o "salvar para reler" e os modos do mock. Sai a frase "sem os `establishments`, toda nota é rejeitada".
- **O STATUS:**
  - **o item do mesmo CNPJ:** ganha a tratativa implementada, e a evidência do mock e do sandbox. Ele só fecha com a prova
    pedida no item, a recusa contra uma conta real com a duplicidade;
  - **o item dos estabelecimentos do cliente:** a rejeição em massa na virada deixa de depender da tabela;
  - **o item das réplicas:** ganha a listagem.

  A paginação não entra no STATUS: o truncamento silencioso é caso tratado (D6, D7).

### D12. As provas da contagem, em três níveis

- **O resolvedor** (Application.Tests), com uma listagem falsa que conta as chamadas e um relógio manual:
  - N resoluções dão 1 chamada;
  - N resoluções concorrentes dão 1 chamada;
  - a validade vencida dá 2;
  - o salvar do perfil dá 2;
  - dois tenants dão 2, e dois ambientes também;
  - a falha transitória não fica guardada;
  - a recusa lembrada dá 1 dentro do intervalo, e o salvar a esquece;
  - o adapter sem listagem dá `CanList = false` e 0 chamadas.
- **O adapter** (Avalara.Tests), com um `HttpMessageHandler` que responde por caminho e pela query, e conta por caminho:
  - a listagem de 3 empresas, cada lista numa página, faz 2 requisições de empresas e 6 de contribuintes (a página e a
    vazia de cada lista);
  - com o `$top` 2: 3 empresas são 3 páginas (`$skip` 0, 2 e 3); o `$skip` volta a 0 na empresa seguinte; a página igual
    à anterior recusa sem pedir outra;
  - com o `$top` 100 e um servidor que devolve no máximo 10: 25 contribuintes saem inteiros, com o `$skip` em 0, 10, 20 e
    25;
  - com o teto 3 e páginas que não acabam: a recusa, sem a 4ª página;
  - N envios do mesmo estabelecimento, pelo dispatcher, fazem 2 + 6 requisições de listagem e N de envio;
  - com a sobreposição, 0 de listagem.
- **O ponta a ponta** (`DispatchToMockTests`, com o mock em memória e o `$top` pequeno):
  - as notas dos quatro estabelecimentos da `brmf`, com o `establishments` vazio, são despachadas com os códigos do mock.
    Com o `$top` menor que a lista da empresa `005`, isso só passa se o hub pedir todas as páginas;
  - o mesmo com o limite de página do mock abaixo do `$top`, que só passa com o `$skip` pelos itens recebidos;
  - o contador do mock mostra as páginas de uma listagem só para o lote inteiro.

### D13. O ADR-0033

`docs/adr/0033-de-para-de-estabelecimento-pela-plataforma.md` registra:

- a capacidade declarada (D1, D2);
- a precedência (D3);
- a regra de recusa sem desempate (D4);
- a parte nossa pela união (D5);
- a paginação pelo cliente: a ordem estável, o `$skip` pelos itens recebidos, a parada na página vazia e o teto, e o
  `$select` (D6);
- a completude (D7);
- a janela (D8).

Ele revisa o ADR-0026:

- **o §3:** os códigos passam a vir da plataforma, e a tabela vira sobreposição;
- **a consequência "Configuração obrigatória":** deixa de valer para o destino que lista.

O ADR-0026 ganha a linha "Revisado por". O ADR-0033 cita também o ADR-0013, que previa a Avalara como diretório, para
dizer por que esta listagem não é aquele diretório (D1).

## Risks / Trade-offs

- **[O `empresaId` ou o `contribuinteId` não ser campo ordenável]** → O `$orderby` está declarado no Swagger nos dois
  endpoints ("Define the order by one or more fields (ex. LastModified)"), então o parâmetro existe. O que não se sabe é se
  esses dois campos são ordenáveis, porque o exemplo da documentação é o `LastModified`. Há dois desfechos possíveis:
  - **a recusa do campo (4xx):** a listagem é recusada, alto, com o motivo da plataforma;
  - **o campo ignorado em silêncio:** a ordem fica instável, e um item pode cair em duas páginas ou em nenhuma. O repetido
    some na deduplicação pelo `contribuinteId`. O que falta é truncamento silencioso, e nem o teto nem a guarda da página
    repetida o veem.

  Mitigação: a prova 6.2 compara a listagem com o `$top` padrão e com o `$top` 2. Com a ordem instável, as duas divergem.
- **[A janela atrasa o cadastro novo]** → Um contribuinte cadastrado depois da listagem só aparece no vencimento. Até lá, as
  notas dele são recusadas, de forma visível e reprocessável. Mitigação: a validade curta, o salvar que força a releitura e
  o motivo que diz isso.
- **[A janela atrasa a duplicidade nova]** → Um contribuinte duplicado criado depois da listagem só é visto no vencimento.
  Até lá, as notas vão para o contribuinte que já era o único. Mitigação: a janela de 10 minutos limita o atraso. É o mesmo
  contribuinte que estava certo antes da duplicidade.
- **[A listagem fora do ar bloqueia quem não tem tabela]** → O envio pode estar de pé e a listagem não. Mitigação: o
  desfecho é o retry nativo e a dead-letter, como qualquer indisponibilidade. A tabela é a saída para um estabelecimento
  crítico. Quem tem a tabela completa e notas do D365 não é afetado (D3).
- **[Por réplica]** → Vale o mesmo do token (D8), e o item do STATUS cresce.
- **[Toda lista custa uma página vazia a mais]** → É uma requisição por lista, uma vez por janela: com 8 empresas, 18
  requisições em vez de 9. É o preço de não depender de premissa sobre o limite de página do servidor, nem de contagem
  total, que o contrato não oferece.
- **[O teto recusa uma conta muito grande]** → O teto efetivo em itens depende do limite de página do servidor (D6). Com
  páginas de 100, uma lista passa do teto com mais de 4.900 itens. Se o servidor limitar a 10, passa com mais de 490. A
  recusa é alta e nomeia o teto. A saída é subir o `Avalara:ListingMaxPages`. Subir o `Avalara:ListingPageSize` só
  adianta enquanto ele estiver abaixo do limite do servidor: acima dele, a página continua do tamanho do limite.

## Migration Plan

- **Banco:** sem migração. As `OutboundSettings` gravadas continuam válidas, e a tabela, quando existe, ganha.
- **Seed:** só um banco novo é afetado (D11).
- **Configuração do Host:** a seção `PlatformEstablishments` é opcional, e os padrões estão no código.
- **Rollback:** reverter o código. Um tenant que passou a depender da resolução automática, com a tabela vazia, volta a ter
  as notas rejeitadas por falta de tradução. A volta é preencher a tabela e reprocessar.

## Open Questions

- ~~**Os quatro CNPJs da `brmf` estão cadastrados como contribuintes no sandbox da Avalara?**~~ **Respondida (Marcelo,
  2026-10-05): não.** O fiscosysdev e o sandbox da Avalara são ambientes sem relação, e nenhum CNPJ da `brmf` existe lá.
  Na prova 6.2, a recusa nomeando o CNPJ é o desfecho esperado. O que ela prova é o `$orderby` e o `$skip` contra a
  plataforma real, e o caminho feliz fica provado no mock.
- **O que o `subscriptionId` escopa?** É opcional nos dois endpoints (Swagger, 2026-10-02), e a listagem não o manda, porque
  a credencial já limita à conta. A pergunta não bloqueia, e a resposta não muda a spec nem as tarefas:
  - **se a credencial enxerga mais de uma subscription:** um CNPJ repetido entre elas vira recusa por duplicidade, que é o
    lado seguro;
  - **se for preciso escolher uma:** ela entra como mais um campo da seção do ambiente, sem mudar a regra.
- **O `empresaId` e o `contribuinteId` são campos ordenáveis?** O `$orderby` está declarado no Swagger nos dois endpoints,
  com o `LastModified` de exemplo (Risks). A resposta não muda a spec nem as tarefas: o hub ordena por esses dois campos de
  todo jeito, e a prova 6.2 diz se a ordem é estável.
