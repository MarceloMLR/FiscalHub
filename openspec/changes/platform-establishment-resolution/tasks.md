A ordem vai do núcleo para a borda, uma fatia vertical por grupo:

- **Grupo 1, o núcleo:** a porta e o resolvedor. A regra e a janela vêm antes de qualquer HTTP.
- **Grupo 2, a listagem da Avalara:** sozinha, contra HTTP falso.
- **Grupo 3, o dispatcher e o Host:** a sobreposição e a plataforma no envio, e a composição.
- **Grupo 4, o mock e o ponta a ponta.**
- **Grupo 5, o seed e a documentação.**
- **Grupo 6, a prova manual do critério de saída.**

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. Uma tarefa só recebe `[x]` com
evidência. O que for parcial, não exercitado ou movido fica aberto e anotado na própria tarefa.

## 1. A porta e o resolvedor no núcleo (D1, D2, D4, D8; `platform-establishment-resolution`)

- [x] 1.1 Teste primeiro do casamento, na `FiscalHub.Application.Tests`, com uma listagem falsa que conta as chamadas:
  - **um candidato:** o `Match` é `Unique`, e o `Knows` é verdadeiro;
  - **nenhum:** `None`, e o `Knows` é falso;
  - **dois contribuintes em duas empresas:** `Ambiguous`, com os dois, mesmo com os mesmos códigos;
  - **o mesmo `PlatformId` duas vezes:** um candidato só;
  - **duas linhas sem `PlatformId`:** dois candidatos;
  - **o código que coincide com a ordem do CNPJ:** o `"002"` para o `…0002…`, ao lado de um `"007"`, continua `Ambiguous`;
  - **o F&O com traço contra a plataforma só com dígitos:** casa;
  - **o alfanumérico:** casa com o mesmo valor nas duas pontas;
  - **a caixa diferente:** não casa;
  - **o contribuinte sem CNPJ:** fica fora do índice.
- [x] 1.2 Teste primeiro da janela, com um relógio manual:
  - **N resoluções:** 1 chamada;
  - **N resoluções concorrentes:** 1 chamada, e todas recebem o mesmo desfecho, inclusive a falha;
  - **a validade vencida:** 2 chamadas;
  - **dois tenants:** 2 chamadas. **Dois ambientes do mesmo tenant:** também 2;
  - **a falha transitória:** não fica guardada, e a próxima resolução chama de novo;
  - **a recusa (`DispatchRejectedException`):** 1 chamada dentro do `RefusalHold`, relançada com o mesmo motivo. Depois
    do intervalo, uma chamada nova;
  - **o `ProfileSavedAsync`:** esquece o índice, a tarefa em voo e a recusa do tenant, em todos os ambientes, e não os de
    outro tenant;
  - **o adapter sem listagem registrada:** `CanList = false`, e 0 chamadas;
  - **o cancelamento de quem começou a busca:** não cancela as outras resoluções que aguardam.
- [x] 1.3 Teste primeiro das opções: `CacheDuration` de 10 minutos e `RefusalHold` de 5 minutos sem configuração. Zero ou
  negativo é recusado com motivo que nomeia `PlatformEstablishments:CacheDuration` (ou o `RefusalHold`).
- [x] 1.4 Implementar, na `Application/Outbound`:
  - a porta `IPlatformEstablishmentListing` e o `PlatformEstablishment` (D1);
  - o `PlatformEstablishmentIndex` e o `EstablishmentMatch`. O `Match` não tem como devolver um escolhido entre vários (D2);
  - as `PlatformEstablishmentOptions`, com a validação;
  - o `PlatformEstablishmentResolver`, singleton e `IConnectorProfileObserver`. A busca única é feita sem o
    `CancellationToken` de quem a começou, e cada resolução aguarda com o seu.
- [x] 1.5 `dotnet build` com 0 warnings e `dotnet test` verde.

## 2. A listagem da Avalara (D6, D7; `avalara-establishment-listing`)

- [x] 2.1 Um `HttpMessageHandler` de teste, na `FiscalHub.Adapters.Outbound.Avalara.Tests`:
  - **a resposta:** por caminho e pela query (`empresaId`, `$top` e `$skip`), para servir as páginas;
  - **a contagem:** das requisições por caminho, guardando as queries.

  Pode partir do `StubHttpMessageHandler`, ou do `SequencedHttpMessageHandler` do D365.
- [x] 2.2 As fixtures da listagem, num diretório próprio, `Fixtures/listing/`. O `Fixtures/sandbox/` guarda só resposta
  gravada:
  - **a forma:** a verificada pelo Marcelo em 2026-10-02, que está no pedido desta change, com os campos do `$select`;
  - **os valores:** de mentira;
  - **as empresas:** um array puro, com `"005"`, `"Padrão"` e `"QA"`, cada uma com a `descricao`;
  - **os contribuintes:** em `{"value": [...]}`;
  - **uma variante com `idPortalCompany` e `razao`:** para o cenário da plataforma que ignora o `$select`.

  A origem fica anotada no próprio teste ou num README da pasta.
- [x] 2.3 Teste primeiro da leitura:
  - **os dois formatos:** dão o CNPJ normalizado e os dois códigos;
  - **a empresa com vários contribuintes, e a empresa sem contribuinte:** as duas são lidas;
  - **os códigos:** `"005"` mantém os zeros, e `"Padrão"` mantém o acento. Um `codigo` número JSON vira ausente, sem
    conversão. Nenhum código passa por `Trim`;
  - **o `empresaId` número:** vai na query como veio;
  - **o `$select` e o `$orderby`:** `empresaId,codigoCIA,descricao` e `$orderby=empresaId` nas empresas, e
    `contribuinteId,codigo,cnpj` e `$orderby=contribuinteId` nos contribuintes, em todas as páginas. Os campos a mais são
    ignorados. O `subscriptionId` não vai;
  - **a `descricao`:** chega ao registro da empresa, e não entra no casamento;
  - **3 empresas, cada lista numa página:** 2 requisições de empresas e 6 de contribuintes (a página e a vazia de cada
    lista), em sequência;
  - **o token:** o pedido leva o Bearer do `IAvalaraTokenProvider` com a seção do ambiente ativo. Listagem e envio fazem
    um pedido de token só;
  - **a completude:** a falha na terceira empresa lança, e nada é devolvido.
- [x] 2.4 Teste primeiro da paginação:
  - **com o `$top` 2 e 3 empresas:** `$skip` 0 (2 itens), 2 (1 item) e 3 (vazia), e para na vazia. A página curta não
    encerra;
  - **o servidor que limita a página:** com o `$top` 100 e o handler devolvendo no máximo 10 por página, 25 contribuintes
    saem inteiros, com o `$skip` em 0, 10, 20 e 25. É o teste que falharia com o `$skip` somando o `$top`;
  - **a empresa seguinte:** começa em `$skip=0`;
  - **a falha na segunda página (503):** lança, e nada é devolvido, nem a primeira página;
  - **o `$skip` ignorado:** a página de `$skip=2` igual à de `$skip=0` recusa com "Contrato do destino", nomeando o
    endpoint e o `$skip=2`, e nenhuma página a mais é pedida;
  - **o teto:** com o `ListingMaxPages` 3 e páginas diferentes que não acabam, a recusa nomeia o endpoint e o teto, e a 4ª
    página não é pedida. O padrão é 50;
  - **o `$top` padrão:** é 100 sem configuração, e vai em todas as páginas;
  - **o `ListingPageSize` e o `ListingMaxPages` zero ou negativos:** recusados na composição, com motivo que nomeia a
    configuração.
- [x] 2.5 Teste primeiro das falhas, pela tabela da spec:
  - **o 2xx fora do formato** (empresas em envelope, contribuintes sem `value`): recusa "Contrato do destino", nomeando o
    endpoint;
  - **o 401 com token do cache:** chama o `Invalidate`, e a exceção é transitória;
  - **o 401 com token novo, e o 403:** recusa "Configuração do conector", com o motivo da plataforma e a tabela
    `establishments` como saída;
  - **o 404:** recusa que aponta a URL base e o `Avalara:CompaniesPath` ou o `Avalara:TaxpayersPath`;
  - **outro 4xx:** recusa, com o status;
  - **o 5xx, o 429 e a rede:** exceção transitória;
  - **o corpo:** é redigido antes do motivo, e um token devolvido no corpo não aparece.
- [x] 2.6 Implementar:
  - o `CompaniesPath`, o `TaxpayersPath`, o `ListingPageSize` (100) e o `ListingMaxPages` (50) nas `AvalaraOptions`, com
    os caminhos verificados como padrão;
  - o `AvalaraEstablishmentListing`, com a ordem, o `$skip` pelos itens recebidos, a parada na página vazia, o teto e o
    `$select` do D6, e o log de itens e de páginas por endpoint;
  - o formatador comum do 404, também usado pelo `SubmitPathNotFound`;
  - o registro no `AddAvalaraComplianceDispatcher`: o `HttpClient` nomeado com `RedactLoggedHeaders(_ => true)`, e a
    `IPlatformEstablishmentListing` singleton, com o mesmo nome de adapter do `AvalaraCredentialTest`.
- [x] 2.7 O `AvalaraRegistrationTests` prova que a listagem é registrada com o nome `Avalara`.
- [x] 2.8 `dotnet build` com 0 warnings e `dotnet test` verde.

## 3. O dispatcher pela sobreposição e pela plataforma (D3, D5, D9; `avalara-document-contract`, `compliance-dispatch-outcome`)

- [x] 3.1 Teste primeiro, no `AvalaraOutboundSettingsTests`:
  - **a seção sem `establishments`:** é uma sobreposição vazia, e não um problema;
  - **o `establishments` que não é objeto:** é erro de configuração, nomeando o campo;
  - **a entrada incompleta:** continua recusada, nomeando o campo;
  - **a chave alfanumérica:** acha a entrada.

  Os testes de hoje que esperavam a rejeição "não tem establishments" mudam para o novo comportamento, e cada um diz por
  quê.
- [x] 3.2 Teste primeiro, no `AvalaraComplianceDispatcherTests`, com o resolvedor real sobre a listagem e o handler do 2.1:
  - **a entrada ganha:** códigos manuais e 0 requisições de listagem;
  - **a entrada incompleta:** não cai na plataforma;
  - **sem entrada:** os códigos da plataforma (`"005"`, `"010"`), e nenhum é o CNPJ;
  - **sem contribuinte:** o motivo do D9, sem `POST`;
  - **duplicidade:** sem `POST`, e o motivo no formato do D9: `empresa '005' (RESULTA IND E COM...), contribuinte '001'
    (#10001)`. Além disso:
    - o `empresaId` não aparece;
    - dois contribuintes de código `001` na mesma empresa saem distintos pelo `#id`;
    - sem `descricao` ou sem `contribuinteId`, a parte que falta fica de fora;
    - com sete candidatos, cinco são nomeados, e o motivo diz "e mais 2";
  - **sem código:** o contribuinte sem `codigo` e a empresa sem `codigoCIA` são recusados nomeando o campo;
  - **o resolvedor sem a listagem registrada:** o motivo de hoje, de falta de tradução;
  - **a parte nossa da nota que não diz:**
    - só a plataforma conhece o emitente: o emitente é o próprio;
    - o emitente na tabela e o destinatário na plataforma: recusa citando os dois;
    - nenhuma das duas: recusa citando os dois;
  - **a nota do D365 (emissão dita) com a tabela:** 0 requisições de listagem;
  - **N envios do mesmo estabelecimento:** 2 + 6 requisições de listagem e N de envio;
  - **a listagem recusada (403):** `DispatchRejectedException`. **A transitória (503):** exceção que não é rejeição.
- [x] 3.3 Implementar:
  - o `AvalaraOutboundSettings` sem o `_establishmentsProblem`, com a consulta da sobreposição e os motivos do D9;
  - o dispatcher lendo o perfil uma vez e pedindo o índice ao resolvedor só quando precisa: a nota que não diz a emissão
    (D5), ou o CNPJ sem entrada (D3).
- [x] 3.4 Host:
  - as `PlatformEstablishmentOptions` lidas da seção `PlatformEstablishments`, com a validação do 1.3 na subida;
  - o resolvedor singleton, registrado também como `IConnectorProfileObserver`, ao lado do `CredentialTestBrake`.
- [x] 3.5 `ConnectorProfileServiceTests`: salvar o perfil avisa o resolvedor, e salvar sem mudar também avisa. Se o teste de
  hoje já cobre "todos os observadores", isto é uma linha nele.
- [x] 3.6 `dotnet build` com 0 warnings e `dotnet test` verde.

## 4. O mock e o ponta a ponta (D10, D12)

- [x] 4.1 O `MockComplianceApi`:
  - **as rotas:** `GET /taxcompliance/v2/empresa` (array puro) e `GET /taxcompliance/v2/contribuinte?empresaId=`
    (`{"value": [...]}`), com o Bearer exigido. Sem o `empresaId`, os contribuintes respondem 400;
  - **os parâmetros,** nos dois endpoints (D10):
    - o `$top` e o `$skip` paginam de verdade;
    - o `$orderby` ordena pelo campo pedido, e sem ele a ordem muda a cada pedido;
    - o `$select` devolve só os campos pedidos;
  - **os dados:**
    - a empresa `"005"`, com descrição, os quatro estabelecimentos da `brmf` (`44278225000180`, `44278225000260`,
      `44278225000341` e `44278225003448`) e o `12345678000190` dos XMLs. Os códigos não seguem a ordem do CNPJ;
    - as empresas `Padrão` e `QA`, com descrição e com contribuintes de outros CNPJs;
  - **a administração:**
    - `POST /admin/contribuintes/adicionar?cnpj=&empresa=`, `remover?cnpj=` e `restaurar`;
    - `POST /admin/listagem/limite?itens=`, o limite de página do próprio mock;
    - `GET /admin/contribuintes`, com a listagem e os contadores, que contam cada página;
  - **o comentário do topo:** passa a citar a listagem verificada.
- [x] 4.2 `DispatchToMockTests`, com o resolvedor na composição do `Harness`, o `establishments` vazio, o mock em memória e o
  `ListingPageSize` 2, menor que a lista de contribuintes da empresa `"005"`:
  - **os quatro estabelecimentos:** uma nota de cada um, com o cabeçalho da fixture trocado como no teste alfanumérico, é
    despachada com os códigos do mock. Isso só passa se o hub pedir todas as páginas;
  - **o limite de página do mock:** com o `ListingPageSize` 100 e o limite do mock em 2, as mesmas quatro notas são
    despachadas. Isso só passa com o `$skip` pelos itens recebidos e a parada na página vazia;
  - **um lote de N notas:** o contador do mock mostra as páginas de uma listagem só, e não N listagens;
  - **o `adicionar` do CNPJ da `Matriz` na `QA`:** recusa nomeando os dois, e nenhum documento novo no mock;
  - **o `remover` da `Matriz`:** recusa nomeando o CNPJ. Depois, o `restaurar`, o salvar do perfil (o observador) e o
    reprocesso: a nota é enviada;
  - **o `adicionar` de um CNPJ alfanumérico:** uma nota com `12.ABC.345/01DE-35` é despachada com os códigos dele;
  - **os testes de hoje com a sobreposição:** continuam passando, e provam que ela ganha.
- [x] 4.3 `dotnet build` com 0 warnings e `dotnet test` verde.

## 5. O seed e a documentação (D11, D13)

- [x] 5.1 O seed: o `establishments` do sandbox do tenant-a passa a `{}`, e o comentário diz que o mock lista.
- [x] 5.2 O ADR-0033 (`docs/adr/0033-de-para-de-estabelecimento-pela-plataforma.md`, pelo `0000-template.md`), com o que o
  D13 lista. Também:
  - a linha "Revisado por" no ADR-0026;
  - a linha do índice em `docs/adr/README.md`, e o 0026 marcado como revisado pelo 0033.
- [x] 5.3 O RUNNING:
  - **a seção "Tradução dos estabelecimentos":** reescrita para a resolução pela plataforma e a sobreposição opcional;
  - **o SQL:** mostra a tabela vazia, e diz que um banco antigo mantém a entrada da `Matriz` como sobreposição;
  - **os modos e o contador do mock:** documentados;
  - **o "salvar para reler":** documentado;
  - **a guarda do §1 do teste manual:** continua conferindo que a tela preserva o `establishments`. Sai a frase "sem os
    `establishments`, toda nota é rejeitada".
- [x] 5.4 O STATUS, pelos itens do D11:
  - o item do mesmo CNPJ, com a tratativa e a evidência, aberto até a prova numa conta real;
  - o item dos estabelecimentos do cliente;
  - o item das réplicas.

  A paginação não entra no STATUS: o truncamento silencioso é caso tratado (D6, D7).

## 6. A prova manual do critério de saída

- [x] 6.1 Contra o mock, com o host, o SQL e o cofre locais, e o `establishments` vazio (o SQL do RUNNING):
  - **a passada do coletor do tenant-a:** as 5 NF-e 55 da `Matriz` são enviadas com os códigos do mock. O `GET
    /admin/contribuintes` mostra as páginas de uma listagem só para o lote;
  - **a duplicidade:** `adicionar` o CNPJ da `Matriz` na `QA`, salvar o perfil e reprocessar uma nota. Ela é recusada
    nomeando os dois candidatos no formato do D9, com a descrição e o `#id`, e o mock não recebe documento;
  - **o limite de página do mock:** `POST /admin/listagem/limite?itens=2`, salvar e reprocessar uma nota. Ela é enviada, e
    o contador mostra mais páginas. Depois, `restaurar`;
  - **o CNPJ sem cadastro:** `restaurar`, `remover` a `Matriz`, salvar e reprocessar. A nota é recusada nomeando o CNPJ;
  - **a volta:** `restaurar`, salvar e reprocessar. A nota é enviada;
  - **a sobreposição:** uma entrada manual da `Matriz` com códigos `MANUAL-*`. O payload no mock leva os `MANUAL-*`, e o
    contador não sobe.

  **Feito (2026-10-05), com o host e o mock locais.**
  - **A identidade no F&O:** o coletor autenticou com a credencial do próprio tenant, e não com o Azure CLI. O log diz
    "D365: o tenant tenant-a autentica no F&O com a credencial do próprio tenant (client credentials: app 88e5c98e-…)".
    O `auth` do perfil já tinha o tenantId, o clientId e o segredo no cofre (gravado pela tela em 2026-10-01).
  - **O perfil, sem perder nada:** o `PUT /connector` levou as settings inteiras lidas do `GET`. A seção `sandbox` (a
    Avalara real) voltou como estava, e o Client Secret dela ficou no cofre pela referência. O mock entrou na seção
    `production`, com a tabela vazia e um segredo de teste, e o ambiente ativo foi `Production` durante a prova. No fim, o
    perfil foi restaurado, e o `GET` comparado com o de antes deu igual em tudo, menos num ponto: o
    `outbound.production.clientSecret` passou a "configurado" com o valor de teste. O cofre não tem apagar, e um Client
    Secret de produção de verdade o sobrescreve pela tela.
  - **A passada do coletor:** rebobinada a marca para 2015, a passada achou 14 referências. As 5 NF-e 55 da `Matriz`,
    que estavam em `IntegrationError` pela recusa do sandbox, foram enviadas com `codigoEmpresa 005` e `codigoContribuinte
    010` e confirmadas. O lote inteiro custou uma listagem: o log diz "3 empresas em 2 páginas e 7 contribuintes em 6
    páginas", e o contador do mock deu 2 e 6.
  - **A duplicidade:** a `BRMF21-10000026` foi recusada com "o estabelecimento 44278225000180 tem 2 contribuintes na
    plataforma (…): empresa '005' (RESULTA IND E COM MAQUINAS (mock)), contribuinte '010' (#10001); empresa 'QA' (QA
    (mock)), contribuinte '001' (#90001). Remova a duplicidade…". O `ExternalId` continuou o da passada, porque nenhum
    envio saiu.
  - **O limite de página do mock (2 itens):** a nota foi enviada com `005` e `010`, e a listagem deu 3 páginas de
    empresas e 8 de contribuintes, sem perder nenhum dos 7.
  - **O CNPJ sem cadastro:** a nota foi recusada com "o estabelecimento 44278225000180 não tem contribuinte cadastrado na
    plataforma (…)".
  - **A volta:** a nota foi enviada com `005` e `010`, e confirmada.
  - **A sobreposição:** o payload levou `MANUAL-E` e `MANUAL-C`, e o contador continuou em 2 e 6.
  - **No fim,** o poll de status confirmou todas as notas contra o mock antes da restauração, e nenhuma ficou pendente
    para ser consultada no sandbox real. O host e o mock foram parados, e o log do host não teve nenhuma falha.
- [ ] 6.2 Contra o sandbox, com o `establishments` vazio, nas notas da `Matriz`.
  - **O desfecho esperado é a recusa nomeando o CNPJ** ("o estabelecimento 44278225000180 não tem contribuinte cadastrado
    na plataforma …"). O fiscosysdev e o sandbox da Avalara são ambientes sem relação, e nenhum CNPJ da `brmf` existe lá
    (Marcelo, 2026-10-05). A recusa é o resultado certo, e não uma falha da prova. O caminho feliz fica provado no mock
    (6.1 e 4.2).
  - **O que a 6.2 prova** é o `$orderby` e o `$skip` contra a plataforma real, comparando o `$top` 2 com o padrão. Isso
    vale haja casamento ou não: a listagem é feita inteira antes do casamento.

  **A paginação contra a plataforma real:** rodar a mesma resolução duas vezes. Uma vez com o `ListingPageSize` padrão, e
  outra com `Avalara:ListingPageSize` 2, salvando o perfil entre as duas para reler. As duas devem dar as mesmas empresas
  e os mesmos contribuintes, com mais páginas na segunda. Anotar os itens e as páginas por endpoint que o log mostrou.
  - **o que a comparação prova:** que o `$skip` é respeitado, e também que a ordem pelo `$orderby` é estável. Com a ordem
    instável, as duas rodadas divergiriam (design, Risks). Se divergirem, a prova falhou: registrar a diferença, e não
    marcar;
  - **o teto:** com o `$top` 2, o teto de 50 páginas cobre listas de até 98 itens. Se ele recusar, repetir com um `$top`
    que caiba, e anotar qual;
  - **sem exercício:** se nenhuma lista do sandbox passar de 2 itens, anotar que o `$skip` não foi exercitado lá.

  Se a recusa por duplicidade aparecer com as empresas de teste do sandbox, ela é a prova do item do STATUS: registrar os
  candidatos.
- [x] 6.3 Os outros três estabelecimentos (`SP-01`, `SAL-01` e `RJ-01`) não têm NF-e 55 no fiscosysdev. A prova deles é a
  do 4.2. Anotar isso aqui e no STATUS, e não marcar a prova real deles como feita.

  **Anotado (2026-10-05).** A prova deles é o
  `DispatchToMockTests.The_four_brmf_establishments_resolve_and_dispatch_with_an_empty_table`, com o cabeçalho da nota da
  `Matriz` trocado para cada CNPJ: os quatro saem com os códigos do mock (`010`, `007`, `021` e `003`), numa listagem só. O
  STATUS registra isso no item "Estabelecimentos do cliente e transferência entre filiais por XML". **A prova real dos três
  não foi feita**, e só será quando houver NF-e 55 deles numa base.
