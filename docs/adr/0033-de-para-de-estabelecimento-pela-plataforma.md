# ADR-0033: O de/para do estabelecimento vem da plataforma, e a recusa fica onde haveria escolha

- **Status:** Aceito
- **Data:** 2026-10-05
- **Revisa:**
  - **ADR-0026 §3:** o `codigoEmpresa` e o `codigoContribuinte` passam a vir da listagem da plataforma, casada pelo CNPJ. A
    tabela `establishments` deixa de ser a fonte, e vira sobreposição opcional: quando tem a entrada, ganha.
  - **ADR-0026, a consequência "Configuração obrigatória":** deixa de valer para o destino que sabe listar. A tabela ausente
    é a sobreposição vazia.
- **Change OpenSpec:** `openspec/changes/platform-establishment-resolution`. As capacidades são
  `platform-establishment-resolution` e `avalara-establishment-listing`, e os deltas são de `avalara-document-contract`,
  `tax-identifier-normalization` e `compliance-dispatch-outcome`.
- **Verificado (2026-10-02), pelo Marcelo:**
  - **no sandbox:** `GET /taxcompliance/v2/empresa` devolve um array puro, e `GET /taxcompliance/v2/contribuinte?empresaId=`
    devolve `{"value": [...]}`. O `codigoCIA` é texto livre, com acento (`"Padrão"`, `"QA"`, `"SPL"`);
  - **no Swagger:** os dois aceitam `$top`, `$skip` e `$orderby` ("Define the order by one or more fields (ex.
    LastModified)"), e nenhum devolve `nextLink`. O `empresaId` é obrigatório no `/contribuinte`, e o `subscriptionId` é
    opcional nos dois.

  > **Corrigido pela change `platform-listing-shape` (2026-10-06).** A forma do `/empresa` era condicional à query. O
  > array puro veio da chamada **sem** opções. Com `$top` e `$orderby`, com e sem `$skip`, o sandbox devolve
  > `{"value": [...]}`, e a página vazia vem como `{"value": []}`. O hub sempre chama com a query, e o adapter exigia o
  > array: todo despacho de tenant sem a tabela `establishments` morria em recusa de contrato. Desde a correção, o hub
  > aceita as duas formas nas duas listas, e qualquer outra é recusa que nomeia as propriedades recebidas. As respostas
  > reais estão em `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/Fixtures/sandbox/listagem-empresas-*.json`.
  > A decisão deste ADR não muda: o que mudou foi o fato registrado aqui.

## Contexto

Todo envio à Avalara leva os códigos da empresa e do contribuinte do estabelecimento próprio, e eles nunca vêm do ERP
(ADR-0026 §3). Até aqui, quem os dava era a tabela `establishments` do perfil, mantida à mão por seed e SQL, sem tela.
Três dos quatro estabelecimentos da `brmf` não estavam nela.

A plataforma já sabe os códigos, e sabe o CNPJ de cada contribuinte. A credencial do tenant a lista no escopo dele. O hub
pode casar sozinho.

O risco da troca estava registrado no STATUS ("Mesmo CNPJ em mais de um contribuinte na plataforma"). O sandbox tem
empresas de teste ao lado das reais, e um CNPJ pode aparecer em mais de uma. **Escolher errado não falha:** a nota é aceita
e escriturada no contribuinte errado, sem rejeição e sem log, e só aparece na conferência da apuração.

## Decisão

**O hub casa o CNPJ do estabelecimento próprio com a listagem completa da plataforma, e recusa onde a tradução não existe
ou onde haveria escolha. A tabela `establishments` continua valendo, e ganha.**

### 1. A listagem é capacidade declarada pelo adapter de saída

A porta `IPlatformEstablishmentListing` (Application/Outbound) é opcional, no desenho do `IConnectorCredentialTest`. O
adapter que sabe listar a registra com o nome dele, e o resolvedor a escolhe pelo `OutboundAdapter` do perfil, pela
comparação exata. A `IComplianceDispatcher<T>` não ganha método: listar não é premissa do núcleo. Sem a capacidade, a
tabela é a única fonte, como antes.

### 2. A regra fica no núcleo, e o vocabulário no adapter

O `PlatformEstablishmentResolver` (Application/Outbound) guarda a regra, igual para qualquer destino que liste: o
casamento, a duplicidade, a janela e a recusa lembrada. O resultado do casamento é `None`, `Unique` ou `Ambiguous`, e não
tem a forma "um escolhido entre vários": a recusa é a única saída que o tipo oferece. O adapter escreve o motivo, porque
sabe o que a plataforma chama de quê (`codigoCIA`, contribuinte) e onde fica a sobreposição.

### 3. A precedência e a preguiça

A entrada da tabela para o CNPJ ganha, e nesse caso a listagem nem é pedida. A entrada incompleta é recusada nomeando o
campo, e nunca completada pela plataforma: quem a escreveu disse qual é a tradução. Um tenant com a tabela completa e
notas do D365, que dizem a emissão, não faz chamada nova e não ganha modo de falha novo.

### 4. O casamento, sem desempate

- **A chave:** o CNPJ normalizado dos dois lados (ADR-0032), comparado exatamente. A caixa não é convertida.
- **Os candidatos:** os contribuintes distintos pelo `contribuinteId`. O mesmo listado duas vezes conta um. A linha sem
  identificador conta à parte: o erro, se houver, vai para o lado de recusar mais.
- **Nenhum:** recusa nomeando o CNPJ, e não erro genérico. É tradução que não existe, e não validação fiscal: sem o código
  não há o que pôr no campo.
- **Mais de um:** recusa nomeando os candidatos, e nada é despachado, mesmo que os códigos coincidam. Cada candidato é
  nomeado pela empresa (código e descrição) e pelo contribuinte (código e `#contribuinteId`). O `#id` separa dois
  contribuintes de mesmo código na mesma empresa, e é o endereço do registro na plataforma. O `empresaId` fica de fora:
  depois do código e da descrição, ele não acrescenta nada legível.
- **Nenhum desempate:** nem a ordem do retorno, nem o código da empresa (que filtraria `Padrão`, `QA` e `SPL`, e seria
  lógica de cliente), nem o código do contribuinte coincidir com a ordem do CNPJ, que é convenção de alguns clientes e não
  regra.

### 5. A parte nossa da nota que não diz

Quando a nota não diz a emissão (o XML), uma parte é estabelecimento do tenant se tem entrada na tabela **ou** contribuinte
na plataforma. Exatamente uma tem de ser. Uma transferência entre uma filial da tabela e outra só da plataforma é recusada
como ambígua, e não enviada como nota de uma delas.

### 6. A listagem da Avalara: paginada pelo cliente, até a página vazia

- **As chamadas:** as empresas, e depois os contribuintes de cada empresa, em sequência. O `empresaId` é obrigatório, então
  não há como pedir um CNPJ na conta inteira.
- **O `$select`:** `empresaId,codigoCIA,descricao` nas empresas (a descrição deixa o motivo legível, e o `/empresa` é uma
  lista só), e `contribuinteId,codigo,cnpj` nos contribuintes, que se repetem por empresa.
- **A ordem estável:** `$orderby=empresaId` e `$orderby=contribuinteId`. Sem ela, paginar com `$skip` é indefinido.
- **O avanço:** `$top` fixo (`Avalara:ListingPageSize`, 100), e o `$skip` somando os itens **recebidos**. Somar o `$top`
  perderia itens em silêncio diante de um servidor que limita a página.
- **A parada:** só a página vazia. A página curta pode ser o limite do servidor, e não o fim. O custo é uma requisição a
  mais por lista, e em troca a leitura não depende de premissa sobre o servidor.
- **As guardas:** a página igual à anterior (o `$skip` ignorado) e o teto de páginas (`Avalara:ListingMaxPages`, 50) são
  recusa de contrato. O teto é em páginas, e não em itens: com páginas de 100 são 4.900 itens por lista, e com um servidor
  que limite a 10, 490.
- **Os códigos:** como texto, como vieram, sem `Trim`. Um número JSON é ausente, porque convertê-lo perderia os zeros à
  esquerda.
- **A credencial e o token:** os do envio, pela mesma seção do ambiente ativo.

### 7. Tudo ou nada

A listagem só é usada inteira: todas as páginas de todas as empresas. Qualquer falha lança, e nada parcial chega ao índice.
Uma listagem parcial é o defeito do STATUS por outro caminho: o segundo contribuinte do CNPJ estaria justamente na empresa
que falhou, e o primeiro pareceria único.

### 8. Uma listagem por janela de despacho

- **A janela:** a listagem fica guardada por tenant e ambiente pela validade (`PlatformEstablishments:CacheDuration`, 10
  minutos), com vencimento absoluto. Dentro dela, toda nota do tenant e ambiente usa a mesma listagem.
- **A busca única:** uma listagem em voo por chave, e quem chega durante ela recebe o mesmo desfecho. Ela não leva o
  cancelamento de quem a começou.
- **A falha:** a indisponibilidade segue o retry nativo e a dead-letter, sem ficar guardada. A recusa permanente fica
  lembrada (`PlatformEstablishments:RefusalHold`, 5 minutos): sem isso, uma credencial que não lista viraria uma tentativa
  por nota, o padrão que bloqueia conta (ADR-0027 §7).
- **A invalidação:** salvar o perfil do tenant, mesmo sem mudar nada, esquece a listagem, a busca em voo e a recusa, em
  todos os ambientes. É a saída documentada para o contribuinte recém-cadastrado na plataforma.
- **Zero ou negativo** em qualquer uma das quatro opções impede o host de subir: a validade zero seria a listagem por nota.

## Alternativas consideradas

- **Tudo dentro do adapter da Avalara.** Seria menos código, mas a regra de recusa é do hub, e não da Avalara. O próximo
  destino que listasse teria de reimplementar o casamento, a duplicidade e a janela, e um deles poderia escolher onde o
  outro recusa.
- **Um adapter da Avalara na `ICompanyDirectory`,** como o ADR-0013 previa, com o mesmo fluxo de dois passos. O ADR-0032
  mudou a porta: ela é do adapter de **entrada** e serve os dropdowns com a chave do ERP. A listagem é do adapter de
  **saída**, e os códigos dela nunca vão para a tela. Na mesma porta, o dropdown mostraria o cadastro da plataforma, com
  `Padrão` e `QA`, no lugar do cadastro do ERP.
- **Escolher entre os candidatos por algum critério** (o primeiro, o "real", o código que bate com a ordem do CNPJ). É o
  defeito que não falha.
- **A tabela decide sozinha quando tem uma das partes.** A transferência entre uma filial da tabela e outra só da
  plataforma iria como nota de uma delas, sem aviso.
- **Recusar a listagem diante de um `nextLink`, e deixar o corte como risco.** Era o desenho antes da conferência do
  Swagger. A paginação pelo cliente o torna caso tratado.
- **Parar na página curta.** Supõe que o servidor nunca limita a página abaixo do `$top`, e com essa suposição errada o
  truncamento volta, em silêncio.
- **Reler a plataforma quando o CNPJ não é achado.** Corrigiria sozinha o cadastro recente, mas um CNPJ que nunca vai
  existir faria uma listagem por minuto enquanto houvesse nota dele.
- **Uma tela de de/para.** No caminho feliz não há cadastro. Ela entra só se a duplicidade se mostrar comum.

## Consequências

**Melhora**

- **Os estabelecimentos resolvem sem cadastro manual.** Com a tabela vazia, as notas da `brmf` vão com os códigos da
  plataforma.
- **A duplicidade aparece como recusa,** com os candidatos nomeados e endereçáveis, em vez de virar escrituração errada.
- **O que estava configurado continua valendo,** sem migração e sem chamada nova.
- **Um destino novo que liste herda a regra,** sem reimplementá-la.

**Piora**

- **A janela atrasa o cadastro novo:** até o vencimento, ou até o salvar do perfil, a nota do contribuinte recém-cadastrado
  é recusada, de forma visível e reprocessável.
- **A janela atrasa a duplicidade nova:** criada depois da listagem, só é vista no vencimento. Até lá, as notas vão para o
  contribuinte que já era o único.
- **A listagem fora do ar bloqueia quem não tem tabela,** com retry nativo e dead-letter. A tabela é a saída.
- **Toda lista custa uma página vazia a mais,** uma vez por janela.
- **O cache é por processo,** como o token: com mais de uma réplica, o salvar esquece só na que atendeu.

**Pendências** (registradas em `docs/STATUS.md`)

- **O mesmo CNPJ numa conta real:** a recusa por duplicidade provada fora do mock.
- **Os campos ordenáveis:** se o `empresaId` e o `contribuinteId` são ordenáveis. A prova é a mesma listagem com o `$top`
  padrão e com o `$top` 2, contra o sandbox.
- **O `subscriptionId`:** o que ele escopa, e se uma credencial enxerga mais de uma subscription.
