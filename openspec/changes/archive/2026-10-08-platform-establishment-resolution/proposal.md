## Why

Todo envio à Avalara leva o `codigoEmpresa` e o `codigoContribuinte` do estabelecimento próprio, e eles nunca vêm do ERP
(ADR-0026 §3). Hoje quem os dá é o mapa `establishments` das `OutboundSettings`, mantido à mão, por seed e por SQL, sem
tela. Três dos quatro estabelecimentos da `brmf` não estão nele, e as notas deles são rejeitadas por falta de tradução.

A plataforma já sabe os códigos e o CNPJ de cada contribuinte, e a credencial do tenant já a lista no escopo dele. O hub
pode casar sozinho, sem cadastro manual no caminho feliz.

O risco desta troca está registrado no `docs/STATUS.md` ("Mesmo CNPJ em mais de um contribuinte na plataforma"). Se a
resolução automática escolher errado, nada falha: a nota é aceita e escriturada no contribuinte errado, sem rejeição e
sem log. Por isso a fatia recusa onde haveria escolha.

## What Changes

- **A listagem é uma capacidade que o adapter de saída declara.**
  - **A porta:** uma porta nova na Application, opcional, no desenho do `IConnectorCredentialTest`. O adapter que sabe
    listar os estabelecimentos da plataforma a registra com o nome dele.
  - **Quem sabe listar:** a Avalara sabe; outro destino pode não saber. Sem a capacidade, o `establishments` manual
    continua sendo a única fonte, com o comportamento de hoje.
  - **O que não muda:** a `IComplianceDispatcher<T>` não ganha método. Listar não vira premissa do núcleo.
- **A listagem da Avalara.**
  - **As chamadas:** `GET /taxcompliance/v2/empresa` devolve um array puro. Para cada empresa, `GET
    /taxcompliance/v2/contribuinte?empresaId=` devolve `{"value": [...]}`. O `empresaId` é obrigatório, então não há como
    filtrar por CNPJ na conta inteira. As duas usam a credencial e o token do envio.
  - **A paginação, pelo cliente** (Swagger conferido em 2026-10-02), nos dois endpoints. Não há `nextLink`:
    - **a ordem:** `$orderby=empresaId` nas empresas e `$orderby=contribuinteId` nos contribuintes. Sem ordem estável,
      paginar com `$skip` é indefinido;
    - **o avanço:** `$top` fixo, e o `$skip` somando os itens recebidos em cada página;
    - **a parada:** só na página vazia. Um limite de página do servidor abaixo do `$top` custa páginas, e não itens;
    - **o teto:** 50 páginas por lista, como guarda geral.

    Assim, uma lista longa não é truncada em silêncio.
  - **O `$select`:** `empresaId,codigoCIA,descricao` nas empresas, e `contribuinteId,codigo,cnpj` nos contribuintes. A
    `descricao` é o que torna legível o motivo da duplicidade.
  - **Os códigos:** o payload leva o `codigoCIA` da empresa e o `codigo` do contribuinte, lidos como texto, como vieram
    (`"005"`, `"Padrão"`, `"QA"`).
  - **A completude:** a listagem é tudo ou nada. Se uma chamada falhar, nenhum estabelecimento é resolvido por ela.
- **O casamento é pelo CNPJ normalizado dos dois lados** (`tax-identifier-normalization`). O CNPJ alfanumérico casa com
  o mesmo valor nas duas pontas.
- **A recusa, onde haveria escolha ou invenção.**
  - **Nenhum contribuinte com o CNPJ:** a recusa nomeia o CNPJ que não tem cadastro na plataforma. Sem o código não há
    payload, então isso é tradução que não existe, e não validação fiscal (ADR-0026).
  - **Mais de um contribuinte com o CNPJ:** a recusa nomeia os candidatos, e nada é despachado. Cada um é nomeado pela
    empresa (código e descrição), pelo código do contribuinte e pelo `contribuinteId`. Não há critério de desempate: nem a
    ordem do retorno, nem a empresa, nem o `codigo` coincidir com a ordem do CNPJ.
- **O `establishments` vira sobreposição opcional.**
  - **A precedência:** uma entrada para o CNPJ ganha da resolução automática, e nesse caso a listagem nem é chamada.
  - **A migração:** não há. O que está configurado continua funcionando.
  - **A tabela ausente:** deixa de ser erro quando o adapter sabe listar.
- **A parte nossa da nota que não diz** (o XML) passa a ser a que tem tradução manual ou contribuinte na plataforma.
- **Uma listagem por janela de despacho, e não por nota.**
  - **O custo:** montar o mapa custa 2 chamadas em `/empresa` e 2 em `/contribuinte` por empresa, quando cada lista cabe
    numa página: a segunda, vazia, confirma o fim. Cada página a mais é uma chamada a mais.
  - **O cache:** por tenant e ambiente, com validade configurável. Uma busca por vez por chave, sob concorrência.
  - **A invalidação:** salvar o perfil esquece a listagem do tenant (`IConnectorProfileObserver`).
  - **A recusa lembrada:** uma recusa permanente da listagem fica lembrada pelo mesmo intervalo da recusa da credencial
    (ADR-0027 §7). Assim, N notas não viram N tentativas.
- **O mock imita a listagem verificada:** os dois formatos, o `$top`, o `$skip`, o `$orderby` e o `$select`, os quatro
  estabelecimentos da `brmf` com códigos que não seguem a ordem do CNPJ, as empresas de teste ao lado das reais, os modos para provocar a
  duplicidade e o CNPJ sem cadastro, e a contagem de chamadas.
- **O seed do sandbox do tenant-a** passa a ter o `establishments` vazio, porque o mock lista. O RUNNING deixa de tratar a
  tabela como obrigatória.
- **ADR-0033.** Registra a capacidade declarada, a regra de recusa, a precedência da sobreposição, a janela de cache, a
  completude e a paginação pelo cliente. Revisa o ADR-0026 §3 e a consequência "Configuração obrigatória".

## Capabilities

### New Capabilities

- `platform-establishment-resolution`: o de/para do estabelecimento pela plataforma, independente do destino:
  - a capacidade declarada pelo adapter;
  - a precedência da sobreposição manual;
  - o casamento pelo CNPJ;
  - as recusas por falta e por duplicidade;
  - a completude da listagem;
  - a janela de cache, a invalidação ao salvar e a recusa lembrada.
- `avalara-establishment-listing`: a listagem da Avalara:
  - os dois endpoints e os dois formatos;
  - os códigos como texto;
  - a credencial e o token do envio;
  - a tradução das falhas;
  - a paginação pelo cliente, com ordem estável, parada na página vazia e teto, e o `$select`.

### Modified Capabilities

- `avalara-document-contract`: dois requisitos mudam:
  - **os códigos da empresa:** vêm da sobreposição ou da listagem da plataforma, e não só da tabela;
  - **o estabelecimento próprio da nota que não diz:** é a parte com tradução manual ou contribuinte na plataforma.
- `tax-identifier-normalization`: a listagem da plataforma entra na lista de lugares onde a regra vale.
- `compliance-dispatch-outcome`: a impossibilidade do lado do conector muda em dois pontos:
  - a tabela ausente deixa de ser rejeição quando o adapter lista;
  - a falta de contribuinte, a duplicidade e a recusa da listagem passam a ser rejeição.

## Non-goals

- **Tela de de/para.** No caminho feliz não há cadastro. A tela entra só se a duplicidade se mostrar comum (STATUS).
- **Espelho da capacidade no dashboard,** como o `CREDENTIAL_TEST_ADAPTERS`. Nada na tela depende dela.
- **Desempate de qualquer tipo,** e filtrar as empresas de teste (`Padrão`, `QA`, `SPL`) pelo código. Seria escolher, e é
  lógica de cliente.
- **O `subscriptionId`.** É opcional nos dois endpoints, e a listagem não o manda. Fica como pergunta aberta no design.
- **Reler a plataforma quando um CNPJ não é achado.** A releitura é pelo vencimento da janela ou pelo salvar do perfil.
- **Foto da listagem no trace.** A foto é por documento, e a listagem é do tenant. O motivo da recusa traz os candidatos.
- **O teste de credencial e a sonda do sandbox listando.** O botão continua trocando só o token.
- **Cruzar o diretório do ERP com a listagem da plataforma.** É o material da tela, se ela vier.
- **Cache compartilhado entre réplicas.** É por processo, como o token. O item do STATUS cresce com isto.
- **Emitir, validar ou calcular** qualquer coisa. A fatia só traduz o estabelecimento para os códigos que a plataforma
  já tem.

## Impact

- **Application (`Outbound`):**
  - a porta da listagem e o registro do estabelecimento da plataforma;
  - o resolvedor: o casamento, a janela de cache, a busca única, a recusa lembrada e o observador do perfil;
  - as opções da janela.
- **Adapters (`Outbound.Avalara`):**
  - a listagem HTTP;
  - os dois caminhos nas `AvalaraOptions`;
  - a sobreposição no `AvalaraOutboundSettings`, que deixa de exigir a tabela;
  - o dispatcher resolvendo os códigos e a parte nossa pelo resolvedor;
  - o registro da capacidade no DI.
- **Host:** o resolvedor e o observador no DI, com as opções lidas da configuração.
- **Infrastructure:** o seed do tenant-a com o `establishments` vazio no sandbox. Sem migração.
- **Tools:** o `MockComplianceApi`, com a listagem, os modos e o contador.
- **Testes:**
  - o resolvedor, com uma listagem falsa que conta as chamadas;
  - a listagem e o dispatcher, com HTTP falso que conta as requisições;
  - o ponta a ponta contra o mock em memória (`DispatchToMockTests`).
- **Docs:**
  - o ADR-0033, e a linha de revisão no ADR-0026;
  - o RUNNING;
  - o STATUS: o item do mesmo CNPJ, o item dos estabelecimentos do cliente e o item das réplicas.
- **Dashboard e D365:** nada muda.
