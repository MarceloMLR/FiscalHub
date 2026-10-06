A ordem vai dos valores para a evidência, uma fatia vertical por grupo:

- **Grupo 1, os valores inventados:** as fixtures que se dizem de mentira passam a ser. Só valores, num commit separado.
- **Grupo 2, a reprodução:** os dois dublês passam a imitar o sandbox, e os testes caem com o motivo visto em dev.
- **Grupo 3, a correção no adapter:** as duas formas, a parada na página vazia em envelope e a recusa que diz o que veio.
  A suíte volta ao verde.
- **Grupo 4, a resposta real gravada:** a varredura que impõe a curadoria, as três respostas das chamadas diretas e o
  comando `listing` da sonda.
- **Grupo 5, o registro do fato:** o ADR-0033, as anotações na outra change e o item de Operação do STATUS.
- **Grupo 6, as provas pelo caminho do hub,** contra o sandbox e contra o mock. O contrato do `/empresa` já está
  confirmado por chamada direta (proposal, Why), e não se prova de novo.

O trabalho sai de uma branch nova a partir do `main` (design, Migration Plan). O grupo 1 é um commit só, de valores, e o
conserto da forma vem nos commits seguintes. O grupo 2 termina com os testes vermelhos de propósito, porque é a reprodução,
e o verde é o fim do grupo 3. Fora isso, cada grupo de código termina com `dotnet build -warnaserror` limpo e `dotnet test`
verde. Uma tarefa só recebe `[x]` com evidência. O que for parcial, não exercitado ou movido fica aberto e anotado na
própria tarefa.

## 1. Os valores inventados, num commit separado (D10)

- [x] 1.1 Inventar e trocar os valores que vieram do sandbox, sem mudar nenhuma lógica:
  - **o `Fixtures/listing/`:** o `empresaId`, o `codigoCIA`, a `descricao` e o `idPortalCompany` de todos os arquivos,
    inclusive as variantes `*-campos-a-mais.json`. Os arquivos `contribuintes-<empresaId>.json` mudam de nome com o
    `empresaId`;
  - **o `PlatformHandler`:** os identificadores do `FromFixtures`;
  - **o mock:** as empresas do `PlatformDirectory.Initial()`, com o `empresaId`, o `codigoCIA`, a `descricao` e o
    `idPortalCompany`;
  - **o que fica:** os CNPJs dos contribuintes, que são os da Contoso e os de exemplo, os `contribuinteId` e os códigos de
    contribuinte.

  Nenhum valor novo coincide com um do sandbox, pela lista do D10: os `empresaId` 7330 e 7407 a 7412, os `codigoCIA` de
  `"001"` a `"005"`, `"Padrão"`, `"QA"` e `"SPL"`, as razões sociais e os `idPortalCompany`. As três propriedades do D10
  ficam: o `codigoCIA` fora da ordem do `empresaId`, os códigos de contribuinte fora da ordem do CNPJ e um `codigoCIA` com
  acento.
- [x] 1.2 O `Fixtures/listing/README.md`: a afirmação "valores de mentira" passa a ser verdade. Ele registra as três
  propriedades, e diz que os CNPJs são os da Contoso de propósito, para as notas gravadas resolverem no mock.
- [x] 1.3 Os testes que afirmam esses valores, ou que montam a mesma empresa real inline, por substituição mecânica: o
  `AvalaraEstablishmentListingTests`, o `AvalaraDispatcherPlatformCodesTests`, o `AvalaraOutboundSettingsTests` e o
  `DispatchToMockTests`, e os outros que a suíte apontar. Só o valor esperado muda, e nenhuma asserção muda de natureza.
  Um código genérico sozinho, como os do `PlatformEstablishmentResolverTests`, fica.
- [x] 1.4 O RUNNING: as linhas que mostram os códigos e a descrição do mock, como o exemplo da duplicidade, passam aos
  valores novos.
- [x] 1.5 A conferência:
  - **a busca:** nenhuma razão social, nenhum `idPortalCompany` e nenhum `empresaId` do sandbox em `tests/`, `tools/` e
    `docs/RUNNING.md`, fora do `Fixtures/sandbox/`. Anotar a busca usada;
  - `dotnet build -warnaserror` limpo e `dotnet test` verde, com só valores trocados no diff;
  - o commit, sozinho, antes do grupo 2.

  **Feito (2026-10-06).** Os valores: a `8120` (`"012"`, METALURGICA EXEMPLO), a `8121` (`"Comércio"`) e a `8122`
  (`"009"`, LABORATORIO), com os `idPortalCompany` `00000000-0000-4000-8000-0000000081NN`. A busca, sem nenhuma ocorrência
  fora do `Fixtures/sandbox/`: `grep -rnE "RESULTA|TMSA|IMS - |ELTER|BULKTECH|7e93b784|92acdd6d|9efa51d4|aef7d5be|81c69c9c|(7330|740[7-9]|741[0-2])" tests tools docs/RUNNING.md`.
  Ela achou uma sobra que a troca mecânica não cobria, o `"7412"` do teste da falha na terceira empresa, e o
  `7400 + i` do teste dos sete candidatos, que gerava o `7407`, também mudou (para `8200 + i`). O build e a suíte rodaram em
  `Release` (`dotnet build -c Release -warnaserror`: 0 warnings; `dotnet test -c Release`: 1140 aprovados, 3 ignorados, os
  opt-in de sempre), porque um `FiscalHub.Host` em execução travava o `bin/Debug` do host.

## 2. A reprodução, nos dois níveis (D4, D5)

- [x] 2.1 O `PlatformHandler` imita o sandbox com a query:
  - **o padrão:** o envelope `{"value": [...]}` nas duas listas, inclusive na página vazia;
  - **uma opção por lista:** força o array puro;
  - **o `FromFixtures`:** lê os itens das fixtures nas duas formas, e o handler decide o embrulho;
  - **o resumo da classe:** diz de onde vem cada forma.
- [x] 2.2 As fixtures da listagem (D5), já com os valores do grupo 1:
  - **o `Fixtures/listing/empresas-envelope.json`:** os mesmos itens do `empresas.json`, em `{"value": [...]}`, sem
    `@odata.*`;
  - **o `Fixtures/listing/empresas-vazio.json`:** `{"value": []}`, a página vazia como o sandbox a devolve com a query;
  - **o `Fixtures/listing/README.md`:** a forma deixa de ser única. O array puro veio do `/empresa` sem opções de query
    (2026-10-02). O envelope veio do `/empresa` com `$top` e `$orderby`, com e sem `$skip`, inclusive a página vazia
    (2026-10-06), e do `/contribuinte`, sempre chamado com query. O README aponta o `../sandbox/` para as respostas reais.
- [x] 2.3 O `MockComplianceApi` (D4):
  - **o `/empresa`:** o envelope quando a query tem um parâmetro que começa com `$`, inclusive na página vazia, e o array
    puro sem nenhum;
  - **o `/contribuinte`:** continua sempre em envelope. O comentário diz que a forma dele sem opções nunca foi observada;
  - **os comentários do topo e da listagem:** dizem de qual chamada veio cada forma, e deixam de afirmar a forma única.

  Um teste novo no `DispatchToMockTests`, como o `The_mock_answers_on_the_sandbox_submit_path_like_the_platform`:
  - o `/empresa?$top=5&$orderby=empresaId` do mock responde um objeto com o array em `value`;
  - o `/empresa?$top=5&$orderby=empresaId&$skip=999` responde `{"value": []}`;
  - o `/empresa` sem query responde um array.
- [x] 2.4 Rodar e anotar a reprodução, com o adapter de hoje:
  - `dotnet build -warnaserror` limpo;
  - os testes da `FiscalHub.Adapters.Outbound.Avalara.Tests` que listam empresas, e os `DispatchToMockTests` da resolução
    pela plataforma, caem com "não veio no formato verificado, um array puro (veio um objeto)".

  Anotar aqui os testes que caíram. Se algum teste da resolução **não** cair, ele não atravessa o `/empresa`: anotar qual,
  e por quê.

  **Feito (2026-10-06).** `dotnet build -c Release -warnaserror` limpo. Caíram 49 testes, todos com o motivo de dev:
  - **43 na `FiscalHub.Adapters.Outbound.Avalara.Tests`,** do `AvalaraEstablishmentListingTests` e do
    `AvalaraDispatcherPlatformCodesTests`. Em 24, a falha traz o motivo inteiro, e nos outros 19 o xUnit o corta em
    "Contrato do destino: a listagem de empres…";
  - **6 no `DispatchToMockTests`:** `The_four_brmf_establishments_resolve_and_dispatch_with_an_empty_table`,
    `A_server_page_limit_below_the_top_still_resolves_the_four_establishments`, `A_batch_of_notes_lists_the_platform_once`,
    `A_duplicate_on_the_platform_is_refused_naming_both_and_nothing_is_posted`,
    `A_cnpj_without_taxpayer_is_refused_naming_it_and_goes_out_after_restore_save_and_reprocess` e
    `An_alphanumeric_cnpj_on_the_platform_resolves_with_the_same_value`. Quatro deles só afirmam o status
    (`IntegrationError`), e o motivo gravado foi conferido com um `Assert.Fail` temporário, revertido em seguida.

  Da resolução pela plataforma, só o `The_table_wins_over_the_platform_and_lists_nothing` não caiu: a sobreposição ganha, e
  ele não lista nada. O teste novo do mock passou, porque prova o mock, e não o adapter.

## 3. As duas formas e a recusa que diz o que veio (D1, D2, D3, D8; `avalara-establishment-listing`)

- [x] 3.1 Teste primeiro das duas formas, no `AvalaraEstablishmentListingTests`:
  - **as empresas:** o `empresas.json` e o `empresas-envelope.json`, servidos crus, dão a mesma listagem, com os mesmos
    códigos e na mesma ordem;
  - **a parada na página vazia em envelope:** com o `empresas-vazio.json` servido cru depois da última página cheia, a
    leitura das empresas para nele, sem recusa, e nenhuma página de empresas a mais é pedida;
  - **os contribuintes:** em array, inline, dão o mesmo que em envelope. O comentário diz que essa forma não foi observada
    no `/contribuinte`, e que o teste prova que a regra é a mesma nas duas listas;
  - **a empresa sem contribuinte:** `[]` e `{"value": []}` dão o mesmo resultado;
  - **o envelope com `@odata.context`, `@odata.count` e `@odata.nextLink`:** é aceito. Com o `$top` 2, a próxima página
    pedida tem `$skip=2`, pela URL do hub, e nenhuma requisição vai ao endereço do `nextLink`. A leitura para só na página
    vazia;
  - **a mesma página na outra forma:** a de `$skip=2` em array, igual à de `$skip=0` em envelope, é recusa por `$skip`
    ignorado, nomeando o endpoint e o `$skip=2`.
- [x] 3.2 Teste primeiro da recusa da forma, pela tabela da spec. Todas começam com "Contrato do destino: " e nomeiam o
  endpoint:
  - **o objeto sem `value`:** `{"error": …, "message": …}` diz "um objeto com as propriedades error, message";
  - **uma propriedade só:** diz "com a propriedade";
  - **o objeto vazio:** diz "um objeto vazio";
  - **o `value` que não é array:** `{"value": {}}` nomeia o `value` e diz que ele é um objeto, e não um array;
  - **a raiz que não é objeto nem array:** o número, o texto, o booleano e o `null`, cada um pelo tipo;
  - **doze propriedades:** as dez primeiras, na ordem em que vieram, e "e mais 2";
  - **o corpo que não é JSON:** a mensagem de hoje.
- [x] 3.3 Teste primeiro de que a recusa nunca traz os valores: com
  `{"mensagem": "CNPJ 11222333000181 sem acesso", "codigo": 9101}`, o motivo tem `mensagem` e `codigo`, e não tem
  `11222333000181`, `sem acesso` nem `9101`. O mesmo vale para o número e o texto na raiz.
- [x] 3.4 Os testes de hoje que mudam, cada um com um comentário que diz por quê:
  - **o `Companies_in_an_envelope_are_a_contract_refusal`:** passa a provar a aceitação, com o nome trocado;
  - **o `Taxpayers_without_the_value_array_are_a_contract_refusal`:** o `[{"contribuinteId":1}]` sai da teoria, porque o
    array é aceito. Os outros casos ficam, e ganham a conferência da mensagem;
  - **o resumo da classe:** deixa de dizer "as empresas num array puro".
- [x] 3.5 Implementar, no `AvalaraEstablishmentListing`:
  - o `ListPath` sem o `Envelope`;
  - o `Parse` com as duas formas, decididas a cada resposta (D1);
  - a mensagem nova, com a descrição do que veio no lugar do `Kind` (D3);
  - o comentário do topo da classe, que deixa de afirmar uma forma por lista.
- [x] 3.6 As mutações (D8). Cada uma é aplicada, rodada e revertida, e o teste que caiu é anotado aqui:
  - **aceitar qualquer objeto sem olhar o `value`** (o objeto sem `value` como lista vazia): tem de derrubar um teste do
    3.2;
  - **pôr o valor da propriedade na mensagem:** tem de derrubar o 3.3.

  Se uma mutação passar, falta um teste: escrevê-lo antes de marcar.

  **Feito (2026-10-06).** As duas caíram:
  - **a primeira** derrubou 9 testes: as seis teorias de objeto do `A_form_that_is_not_a_list_is_refused_saying_what_came`,
    as duas do `Taxpayers_without_the_value_array_are_a_contract_refusal`, o
    `More_than_ten_properties_name_the_first_ten_in_the_order_they_came` e o
    `The_refusal_names_and_never_carries_a_value` do objeto;
  - **a segunda** derrubou 8, entre eles o `The_refusal_names_and_never_carries_a_value` do objeto (3.3).

  O código foi restaurado de um backup depois de cada uma, e o diff dele bate com o da 3.5.
- [x] 3.7 `dotnet build -warnaserror` limpo e `dotnet test` verde. Os testes que caíram no 2.4 passam sem mudança neles,
  inclusive os `DispatchToMockTests`.

  **Feito (2026-10-06),** em `Release`, pelo `FiscalHub.Host` em execução (ver 1.5): 0 warnings, e a suíte inteira verde.
  A `FiscalHub.Adapters.Outbound.Avalara.Tests` foi de 311 a 330 testes, e a `FiscalHub.Integration.Tests` de 26 a 27. Os
  seis `DispatchToMockTests` do 2.4 passam sem nenhuma mudança neles. Dos 43 testes do adapter que caíram no 2.4, só os
  dois que a 3.4 manda mudar foram tocados.

## 4. A resposta real gravada, e a sonda (D5, D7)

- [x] 4.1 Teste primeiro da varredura, no `SandboxFixtureTests`, para os arquivos `listagem-*`:
  - **nenhum CNPJ:** nem com 14 dígitos seguidos, nem formatado;
  - **a lista do que fica:** fora do `empresaId`, do `codigoCIA`, do `contribuinteId` e do `codigo`, todo valor de item
    é `[mascarado]`. Os nomes das propriedades ficam;
  - **a teoria de amostras da varredura:** ganha os dois casos, o pego e o que passa.
- [x] 4.2 As três respostas reais, das chamadas diretas de 2026-10-06. Os corpos crus foram colados pelo Marcelo na
  conversa da proposta, e o resultado da curadoria, que não tem dado sensível, fica registrado aqui. Cada item tem também
  `"descricao": "[mascarado]"` e `"idPortalCompany": "[mascarado]"`, nessa ordem de propriedades:

  | Arquivo | A query | Os itens em `value` (`empresaId`, `codigoCIA`) |
  |---|---|---|
  | `listagem-empresas-top5.json` | `$top=5&$orderby=empresaId` | 7330 `"001"`, 7407 `"002"`, 7408 `"004"`, 7409 `"003"`, 7410 `"005"` |
  | `listagem-empresas-skip2.json` | `$top=2&$orderby=empresaId&$skip=2` | 7408 `"004"`, 7409 `"003"` |
  | `listagem-empresas-vazia.json` | `$top=5&$orderby=empresaId&$skip=999` | nenhum: `{"value": []}` |

  Nenhuma resposta é fabricada. Para cada uma:
  - **o arquivo:** `Fixtures/sandbox/listagem-empresas-top5.json`, `listagem-empresas-skip2.json` e
    `listagem-empresas-vazia.json`, no formato do `recusa-no-envio.json`, com o `exchange` `listing`;
  - **o host e o status, como fato** (D5): o `request.url` é
    `https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/empresa`, do `sandbox.baseUrl` do perfil do
    tenant-a no banco de dev. O `response.status` é 200, e o campo `note` diz numa linha por quê: o adapter trata todo 4xx
    e o `EnsureSuccessStatusCode` antes de parsear, e o defeito foi recusa de forma, então a resposta foi 2xx, e num `GET`
    de listagem isso é 200. É a mesma inferência que prova o `$select` aceito;
  - **a query:** em `request.query`. Os cabeçalhos e o horário ficam de fora;
  - **a curadoria:** o `empresaId` e o `codigoCIA` ficam reais, e a `descricao` e o `idPortalCompany` saem `[mascarado]`.
    Conferir a olho antes de gravar;
  - **o README da pasta:** uma linha por arquivo, com a forma e a origem: a data, o host, a query e que o corpo veio do
    Postman. O README diz também que o array sem opções e o envelope do `/contribuinte` não têm resposta gravada.

  A varredura do 4.1 tem de passar com os três arquivos.
- [x] 4.3 A reprodução, no `SandboxFixtureTests`:
  - **a página real e a vazia real:** com o `ListingPageSize` 5, o `top5` como primeira página de empresas e a `vazia`
    como segunda, a leitura termina com as 5 empresas, em 2 páginas, sem recusa;
  - **a evidência do `$skip`:** os itens do `skip2` são o terceiro e o quarto do `top5`, pelo `empresaId`, lido dos
    arquivos e não escrito no teste;
  - **o resumo da classe:** passa a citar a listagem.
- [x] 4.4 O comando `listing` da sonda (D7): roda o `AvalaraEstablishmentListing` real com as `AvalaraOptions` do host, e
  imprime as contagens e as páginas por endpoint e, para cada `--cnpj`, o casamento pelo `PlatformEstablishmentIndex`: os
  códigos e o `#id`, nenhum, ou os candidatos. Nenhum outro conteúdo da listagem é impresso. O uso da sonda e o RUNNING §9
  ganham o comando.
- [x] 4.5 `dotnet build -warnaserror` limpo e `dotnet test` verde. A sonda compila sem warning.

  **Feito (2026-10-06),** em `Release` (ver 1.5): 0 warnings na solução e na sonda, e a suíte inteira verde. A
  `FiscalHub.Adapters.Outbound.Avalara.Tests` foi de 330 a 340 testes. A varredura do 4.1 ficou vermelha antes dos
  arquivos, pela falta deles, e verde com os três. A busca do 1.5 continua sem ocorrência fora do `Fixtures/sandbox/`: a
  reprodução lê os `empresaId` dos arquivos. O `listing` da sonda compila, mas ainda não rodou: ele é exercitado no 6.1.

## 5. O registro do fato (D6, D9, D11)

- [ ] 5.1 O ADR-0033: a linha "Verificado (2026-10-02)" ganha a nota em citação. A forma do `/empresa` era condicional à
  query: o array sem opções, e o envelope com elas, inclusive na página vazia. O hub aceita as duas desde esta change, e as
  respostas reais estão no `Fixtures/sandbox/`.
- [ ] 5.2 Na change `platform-establishment-resolution`, anotar sem marcar nem desmarcar:
  - **na 6.1:** o mock servia o `/empresa` em array com a query, ao contrário da plataforma. A evidência vale para o
    casamento, a duplicidade, a paginação e a sobreposição, e não para a forma. A divergência foi corrigida nesta change;
  - **na 6.2:** o caminho da listagem foi destravado aqui. Nas empresas, o `$orderby`, o `$skip` e a página vazia já estão
    provados por chamada direta (as fixtures do `Fixtures/sandbox/`), e a listagem completa e o de/para, pelo grupo 6. O
    `$orderby` e o `$skip` dos contribuintes continuam com ela.
- [ ] 5.3 O item na seção de Operação do `docs/STATUS.md` (D11), no estilo dos outros itens:
  - **o ambiente:** o D365 de dev (o fiscosysdev, Contoso, raiz `44278225`) e a conta Avalara de sandbox (TMSA, IMS,
    ELTER, BULKTECH e RESULTA) descrevem empresas diferentes, sem nenhum CNPJ em comum. O de/para casa por CNPJ, então
    nenhum estabelecimento do ERP de dev resolve na plataforma de dev, e o fim da esteira só fecha contra o mock;
  - **o contorno em uso, temporário:** os quatro CNPJs da Contoso apontados, pela sobreposição
    `OutboundSettings.sandbox.establishments` do tenant-a, para um mesmo contribuinte da TMSA, para as notas chegarem à
    validação da Avalara e os erros seguintes aparecerem. Vive só no banco de dev, não está no repositório, e some num
    `docker compose down -v`;
  - **o gatilho de remoção:** o cadastro dos CNPJs da Contoso como contribuintes na conta de sandbox. Ao remover, conferir
    que a resolução volta a vir da listagem;
  - **enquanto existir:** documentos da Contoso aparecem na conta da TMSA. Conferir o status das notas depois de cada
    rodada, e apagar o que tiver sido aceito;
  - **o sintoma se ficar esquecida:** as notas integram com o código de outra empresa, e nada acusa. O mesmo arranjo em
    produção mandaria documentos de um cliente para a conta de outro;
  - **a alternativa recusada, para não ser reaberta:** despachar sem o código do contribuinte quando o CNPJ não resolve,
    pelos três motivos do D11: o `AvalaraJson` não escreve campo nulo, a premissa de que a plataforma recusaria não está
    testada (o pior caso é aceitar debaixo do contribuinte principal), e a recusa de hoje diz o quê e o que fazer. A
    recusa fica como está.
- [ ] 5.4 `openspec validate --all --strict` verde.

## 6. As provas pelo caminho do hub (D9)

- [ ] 6.1 A listagem completa contra o sandbox, pela sonda: `listing --tenant tenant-a`. As empresas e os contribuintes
  de todas elas são lidos até a página vazia, sem recusa de contrato. Anotar as empresas, os contribuintes e as páginas
  por endpoint. A sonda lê a listagem direto, e a sobreposição do contorno não interfere.
- [ ] 6.2 O de/para de um estabelecimento conhecido: `listing --tenant tenant-a --cnpj <CNPJ de um contribuinte do
  sandbox>`, com o CNPJ conferido pelo Marcelo no sandbox. O casamento é único, e os códigos e o `#id` são os que o
  sandbox mostra para esse contribuinte. Anotar a empresa e o contribuinte pelo código, sem o CNPJ.
- [ ] 6.3 Pela esteira, contra o sandbox:
  - **antes:** tirar a sobreposição do contorno do tenant-a, guardando-a, e salvar o perfil, para a listagem ser chamada;
  - **a prova:** reprocessar uma das cinco NF-e 55 da `Matriz`. Ela é recusada com "o estabelecimento 44278225000180 não
    tem contribuinte cadastrado na plataforma …", e não com "Contrato do destino". O log do host mostra as contagens da
    listagem. A recusa é o desfecho certo, porque nenhum CNPJ da `brmf` existe no sandbox;
  - **depois:** repor a sobreposição do contorno, salvar o perfil e conferir que ela voltou.
- [ ] 6.4 Com a saída no mock, pelo RUNNING: o mock desta change, que serve o envelope com a query, e a tabela
  `establishments` vazia na seção do mock. Reprocessar uma NF-e 55 da `Matriz`: ela é enviada com o `codigoEmpresa` e o
  `codigoContribuinte` que o mock inventado dá à `Matriz` (grupo 1), e o poll a confirma (`Confirmed`) em vez de
  `IntegrationError`. O `GET /admin/contribuintes` mostra as páginas de uma listagem. No fim, o perfil volta ao que era,
  com o contorno.
