## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código, conferido em 2026-10-06.

- **O `AvalaraEstablishmentListing` fixa uma forma por lista.** O `ListPath` carrega `Envelope: false` nas empresas e
  `Envelope: true` nos contribuintes. O `Parse` exige o array na raiz no primeiro caso, e o objeto com `value` no segundo.
  A forma trocada é recusa de contrato, e a mensagem só diz o tipo da raiz (`Kind`: "um objeto", "um array" ou "outro
  tipo").
- **O `Parse` só vê 2xx.** O `PageAsync` trata o 401, o 403, o 404, os demais 4xx e o `EnsureSuccessStatusCode` antes. O
  corpo chega ao `Parse` já redigido pelo `SensitiveText`.
- **O contrato do `/empresa` está confirmado por chamada direta** (2026-10-06, proposal, Why): o envelope com
  `$top` e `$orderby`, o `$skip` respeitado com a ordem estável, e a página vazia em envelope. O `$select` aceito está
  provado pelo próprio defeito: a URL completa do hub respondeu 2xx. A página vazia em envelope é recusada pelo adapter de
  hoje, então a parada da leitura também depende do conserto. As respostas viram fixture (D5), e a verificação contra o
  sandbox desta change é sobre o caminho do hub (D9).
- **A guarda da página repetida já independe da forma.** A assinatura de cada página é o texto cru dos **itens**, e não o
  corpo.
- **Os dois dublês da plataforma servem a forma errada.**
  - **O `MockComplianceApi`** devolve o `/empresa` num array puro com qualquer query.
  - **O `PlatformHandler` dos testes** faz o mesmo.

  Por isso o ensaio, os testes e a prova 6.1 do `platform-establishment-resolution` (contra o mock, 2026-10-05) passaram,
  e o envio real falhou. É o caso que a regra "o mock imita a plataforma" existe para evitar: cada diferença entre o mock e
  a plataforma é um ensaio que passa e um envio real que falha.
- **As fixtures:** o `Fixtures/listing/` guarda a forma reconstruída à mão, com valores de mentira. O `Fixtures/sandbox/`
  guarda só resposta real, e tem um arquivo só, o `recusa-no-envio.json`. A listagem nunca teve resposta real gravada. O
  `SandboxFixtureTests` varre a pasta atrás de credencial e token, e reexercita o dispatcher contra o que foi gravado.
- **A sonda do sandbox** (`tools/AvalaraSandboxProbe`, ADR-0027 §9) só lê, grava em `out/` com a redação do hub, e não tem
  comando de listagem. O `PlatformResponseEnvelope` que ela usa grava a URL **sem a query**, por regra da foto.
- **A spec da listagem ainda não está em `openspec/specs/`.** A `avalara-establishment-listing` foi introduzida pelo
  `platform-establishment-resolution`, que está no `main` e não foi arquivado: 28 de 29 tarefas, com a 6.2 aberta.

## Goals / Non-Goals

**Goals:**

- **O contrato continua fechado,** sobre as duas formas observadas da mesma API, e não aberto a qualquer coisa.
- **O defeito reproduzido antes de corrigido,** em dois níveis: o teste do adapter e o ponta a ponta contra o mock.
- **A recusa que sobra se explica sozinha,** sem expor o que a plataforma devolveu.
- **A evidência real gravada,** para que o próximo contrato da listagem seja conferido contra JSON de verdade, como pede o
  ADR-0026 §4.

**Non-Goals:**

- **Mudar a paginação, o tudo ou nada ou o casamento.** Só a leitura de cada página muda.
- **Tocar o envio, a consulta de status ou o token.**

## Decisions

### D1. As duas formas, decididas a cada resposta

O `ListPath` perde o `Envelope`, e o `Parse` vira uma regra só para as duas listas:

- a raiz é um array: é a página;
- a raiz é um objeto e o `value` é um array: o `value` é a página, e as outras propriedades são ignoradas;
- qualquer outra coisa: recusa de contrato do destino (D3).

**Por que não virar só o `/empresa` para `Envelope: true`.** Seria trocar um palpite sobre a forma por outro. A forma
depende de a chamada ter ou não opções de query, que é detalhe de implementação do adapter. Ela pode mudar na próxima vez
que alguém mexer na paginação: um `$select` a menos, ou uma primeira página sem `$skip`. Aceitar as duas remove um campo,
em vez de acrescentar um caso.

**Alternativas descartadas:**

- **Derivar a forma da query** (com opções, envelope; sem, array). Codificaria como contrato uma correlação observada,
  e não documentada. Qual opção sozinha liga o envelope não foi isolado.
- **Chamar sem opções, para receber sempre o array.** Sem `$top`, `$skip` e `$orderby`, não há paginação pelo cliente,
  e a leitura volta a depender do limite de página do servidor (`platform-establishment-resolution`, D6).
- **Aceitar qualquer objeto, procurando o primeiro array dentro dele.** Abriria o contrato: um corpo de erro com uma
  lista de mensagens viraria uma lista de empresas.

**A forma é de cada resposta,** e não da lista nem da página anterior. Como o hub manda as mesmas opções em todas as
páginas, uma lista que troque de forma no meio não deve acontecer. Travar a forma na primeira página acrescentaria estado
e uma recusa para um caso que não ameaça a completude: os itens continuam conferidos um a um.

**O `@odata.nextLink` é ignorado.** O envelope é onde ele caberia, mas a paginação é do cliente. O `$skip` pelos itens
recebidos e a parada só na página vazia já cobrem o servidor que limita a página. Seguir o `nextLink` seria um segundo
mecanismo de paginação, com mais uma premissa sobre o servidor.

### D2. O peso contra o ADR-0026, e o que continua rígido

A rigidez foi deliberada. O D7 do `platform-establishment-resolution` diz que o formato fora do verificado é recusa de
contrato, porque retentar devolveria o mesmo 200. A recusa alta, e não o palpite, é o que o ADR-0026 §2 pede para o que o
contrato não representa. Esta change mexe nisso, e a justificativa precisa ficar escrita.

**O que a rigidez protegia** era a leitura errada de uma resposta que não é lista. O pior caso é um corpo de erro lido
como lista vazia:

- **nas empresas:** a conta pareceria não ter nenhuma, e toda nota seria recusada por falta de contribuinte. A falha é
  alta, mas tem o motivo errado;
- **nos contribuintes de uma empresa:** a falha seria silenciosa. O segundo contribuinte de um CNPJ estaria justamente
  ali, e o primeiro pareceria único. É a escolha errada que o ADR-0033 existe para impedir.

**Essa proteção não muda.** O objeto sem `value`, ou com `value` que não é array, continua sendo recusa. Um corpo de erro
nunca vira lista vazia, e a mutação "aceitar qualquer objeto sem olhar o `value`" tem de derrubar um teste (tarefa 3.6).
As guardas da completude também não mudam: o item que não é objeto, a empresa sem `empresaId`, o `$skip` ignorado e o teto
de páginas.

**O que muda é o conjunto de formas aceitas,** de uma por lista para duas por lista. As duas são formas observadas da
mesma API. O envelope, além disso, é o padrão do OData para coleção. O ADR-0026 §4 dá a regra: o contrato vem dos JSONs
reais, e onde a evidência contradiz uma decisão nossa, vale a evidência. Aqui, a evidência mostrou duas respostas para o
mesmo endpoint, e o contrato passa a ser as duas.

### D3. A recusa diz o que veio, com os nomes e nunca os valores

A mensagem de hoje, "não veio no formato verificado, um array puro (veio um objeto)", tem dois defeitos:

- **é a mesma frase para um corpo de erro e para um envelope,** que levam a conclusões opostas;
- **manda o leitor procurar mudança de contrato,** quando o que mudou pode ter sido a nossa chamada.

**A nova:** "{lista} ({caminho}) não veio em nenhuma das duas formas de lista, um array ou um objeto com o array em
\"value\" (veio {o que veio})." O "o que veio" está na tabela da spec: os nomes das propriedades de primeiro nível do
objeto, na ordem em que vieram, ou o tipo da raiz. O `Kind` dá lugar a uma descrição que conhece os tipos do JSON.

- **Só os nomes,** porque o valor é resposta da plataforma, pode ter dado de cliente, e o motivo vai para o registro do
  documento e para a tela. Os nomes já bastam para separar os casos: `error, message` é corpo de erro, `value` com outro
  tipo é envelope quebrado, e `Value` com maiúscula é outro contrato.
- **Até dez nomes,** e "e mais N", pelo mesmo motivo do teto de candidatos do D9 do `platform-establishment-resolution`: o
  motivo fica legível, e longe do corte de 1000 caracteres do registro.
- **O corpo que não é JSON** continua com a mensagem de hoje.
- **O prefixo e a categoria não mudam:** "Contrato do destino: …", `DispatchRejectedException`, sem retentativa.

### D4. O mock e o dublê dos testes imitam o sandbox

**No `MockComplianceApi`:**

- **o `/empresa`:** o envelope quando a query tem qualquer opção OData (um parâmetro que começa com `$`), e o array puro
  sem nenhuma. A página vazia com opções também vem em envelope, `{"value": []}`, como na chamada com `$skip=999`. É a
  regra mais simples que bate com as amostras: sem opções, array, e com as opções das chamadas diretas ou com a query do
  hub, envelope;
- **o `/contribuinte`:** continua sempre em envelope. O hub sempre o chama com opções, e a forma dele sem opções nunca foi
  observada. Não se fabrica forma;
- **o comentário do topo e o da listagem:** passam a dizer de qual chamada veio cada forma.

**No `PlatformHandler`:** o padrão passa a ser o do sandbox com a query, o envelope nas duas listas. Uma opção por lista
força o array. As fixtures continuam sendo a fonte dos itens, e o handler decide o embrulho.

**A ordem é teste primeiro, nos dois níveis.** Com o mock e o handler trocados e o adapter de hoje, os testes da listagem
e os `DispatchToMockTests` da resolução pela plataforma falham com o motivo visto em dev. É a reprodução do defeito. Só
então o `Parse` muda.

### D5. As fixtures: as formas trocadas, e a resposta real

**As duas pastas têm propósitos opostos.** O `Fixtures/listing/` é inventado, e nele tudo é inventado (D10). O
`Fixtures/sandbox/` é gravação real, e nele só se mascara o que identifica empresa ou pessoa.

**O `Fixtures/listing/`,** com valores de mentira:

- **o `empresas-envelope.json`:** irmão do `empresas.json`, com os mesmos itens em `{"value": [...]}`, que é a forma
  observada em 2026-10-06. Sem as propriedades `@odata.*`, porque as amostras não as trouxeram;
- **o `empresas-vazio.json`:** `{"value": []}`, a página vazia como o sandbox a devolve com a query (a chamada com
  `$skip=999`). É a fixture do teste de que a leitura para nela (D8);
- **o README:** a afirmação "devolve um array puro, sem envelope" vira condicional. O README diz de qual chamada veio cada
  forma, e aponta o `../sandbox/` para as respostas reais.

Os contribuintes num array e o envelope com `@odata.*` não viram fixture, porque nenhum dos dois foi observado. Eles ficam
inline nos testes, como prova de que a regra é a mesma nas duas listas e de que as propriedades a mais são ignoradas, e o
comentário diz isso.

**O `Fixtures/sandbox/`,** com as respostas reais das três chamadas diretas de 2026-10-06. Os corpos crus vieram do
Postman do Marcelo, colados na conversa da proposta, e o resultado da curadoria fica registrado na tarefa 4.2:

| Arquivo | A query | O que prova |
|---|---|---|
| `listagem-empresas-top5.json` | `$top=5&$orderby=empresaId` | o envelope, com 5 itens em ordem de `empresaId`, e o `idPortalCompany` fora do que o hub pede |
| `listagem-empresas-skip2.json` | `$top=2&$orderby=empresaId&$skip=2` | o `$skip` respeitado e a ordem estável: `7408` e `7409` |
| `listagem-empresas-vazia.json` | `$top=5&$orderby=empresaId&$skip=999` | a página vazia em envelope, `{"value": []}` |

- **O formato é o do `recusa-no-envio.json`,** com o `exchange` `listing`. Os cabeçalhos e o horário, que o Postman não
  mostrou, ficam de fora, e não são inventados.
- **O host e o status são fatos derivados, e não suposições:**
  - **o host:** `https://api-gateway.sandbox.avalarabrasil.com.br/`, o `sandbox.baseUrl` do perfil do tenant-a no banco
    de dev, conferido direto. O `request.url` é `https://api-gateway.sandbox.avalarabrasil.com.br/taxcompliance/v2/empresa`;
  - **o status:** 200. O `AvalaraEstablishmentListing` trata o 401, o 403, o 404, os demais 4xx e o
    `EnsureSuccessStatusCode` antes de parsear, e o defeito apareceu como recusa de forma. Então a resposta foi 2xx, e num
    `GET` de listagem isso é 200. É a mesma inferência que prova o `$select` aceito. O motivo fica numa linha no próprio
    arquivo, num campo `note`, para quem abrir o arquivo saber que o status foi derivado, e não lido.
- **A query vai num campo próprio, `request.query`.** A forma depende dela, e sem ela o arquivo não diz o que prova. O
  `request.url` continua sem query, como na foto.
- **Não há arquivo das outras formas.** O array puro sem opções e o envelope do `/contribuinte` vêm da verificação de
  2026-10-02, sem resposta gravada, e o README diz isso. A query exata do hub, com o `$select`, também não foi gravada: o
  que ela prova, o 2xx, já está provado pelo defeito.
- **A curadoria, por uma lista do que fica.** Ficam os nomes de todas as propriedades, o `empresaId`, o `codigoCIA`, o
  `contribuinteId` e o `codigo`, que são a forma e o que o hub lê. Eles ficam reais, porque a reprodução depende deles:
  a chamada 2 traz o terceiro e o quarto itens da chamada 1. Todo outro valor sai como `[mascarado]`, como `descricao`,
  `idPortalCompany`, `cnpj` e `razao`. Nas três respostas de hoje, isso é a `descricao` e o `idPortalCompany`. Com a
  lista do que fica, um campo novo da plataforma sai mascarado, sem depender de alguém lembrar dele.
- **A varredura impõe a curadoria.** Como a curadoria é à mão, a regra fica no `SandboxFixtureTests`, e não em quem curou.
  Além de credencial e token, nenhum arquivo `listagem-*` pode ter um CNPJ, nem com 14 dígitos seguidos, nem formatado.
  Também não pode ter valor fora da lista do que fica que não seja `[mascarado]`.
- **A reprodução no `SandboxFixtureTests`:**
  - **a página real e a vazia real:** com o `ListingPageSize` 5, o `top5` como a primeira página e a `vazia` como a
    segunda, a leitura das empresas termina com as 5, em 2 páginas, sem recusa. O adapter de hoje recusa já a primeira;
  - **a evidência do `$skip`:** os itens do `skip2` são o terceiro e o quarto do `top5`, pelo `empresaId`. A prova da
    plataforma fica conferível no repositório, e não só no relato. O teste lê os `empresaId` dos arquivos e não os
    escreve: um identificador real só existe no `Fixtures/sandbox/`.

### D6. Sem ADR novo: a correção vai no ADR-0033 e nos READMEs

Nenhuma decisão de arquitetura muda. A listagem pela plataforma, a paginação pelo cliente, o tudo ou nada e a recusa do
desconhecido continuam como o ADR-0033 decidiu. O que muda é um fato registrado, a forma do `/empresa`, e a tolerância do
adapter dentro do contrato.

- **O ADR-0033:** a linha "Verificado (2026-10-02)" ganha uma nota em citação, no padrão das revisões do repositório: a
  forma era condicional à query, e a correção está nesta change.
- **Os READMEs das fixtures e os comentários do código e do mock** deixam de afirmar a forma única.

A alternativa era um ADR-0036 para a correção. Seria um registro de decisão para um fato corrigido, e o número ainda
depende de outra change em voo, a que traz o ADR-0035.

### D7. A sonda ganha o comando `listing`

É o meio de provar o de/para de um estabelecimento conhecido contra o sandbox (D9). Só lê, e usa o perfil, o cofre, as
`AvalaraOptions` do host e o token como os outros comandos. Ele roda o `AvalaraEstablishmentListing` real e imprime:

- a linha de log da listagem: as empresas e os contribuintes, e as páginas de cada endpoint;
- para cada `--cnpj`, o casamento pelo `PlatformEstablishmentIndex`: os códigos e o `#id` no único, nada no nenhum, e os
  candidatos na duplicidade.

Nenhum outro conteúdo da listagem é impresso.

**Alternativas descartadas:**

- **Provar pelo host.** O host lista inteiro e loga as contagens, mas o casamento de um estabelecimento conhecido
  exigiria uma nota com o CNPJ de um contribuinte do sandbox. Nenhum CNPJ da `brmf` existe lá (resposta de 2026-10-05, no
  design do `platform-establishment-resolution`), e mandar um XML adulterado ao sandbox seria enviar documento para provar
  uma leitura. A sonda prova a leitura sem enviar nada. O host continua provando o resto do caminho (D9).
- **Um comando que grava a página crua** (`list-page`), e uma impressão da listagem para comparar tamanhos de página. As
  chamadas diretas já foram feitas, e já provaram o `$skip` e a ordem nas empresas. A curadoria das respostas é à mão, e
  a varredura a impõe (D5).

### D8. Os testes, e as mutações

- **As duas formas, nas duas listas:** com as fixtures servidas cruas, o `empresas.json` e o `empresas-envelope.json` dão a
  mesma listagem. Os contribuintes em array, inline, dão o mesmo que em envelope.
- **A parada na página vazia em envelope:** com o `empresas-vazio.json` servido cru depois da última página cheia, a
  leitura das empresas para nele, sem recusa, e nenhuma página a mais é pedida. Com o adapter de hoje, ele é recusa. Com o
  `PlatformHandler` no padrão novo (D4), toda página vazia dos outros testes também passa a vir em envelope.
- **A terceira forma:** o objeto sem `value`, o `value` que não é array, o número, o texto, o booleano, o `null` e o
  objeto vazio são recusa de contrato, e a mensagem diz o que veio, pela tabela da spec.
- **Nunca os valores:** um corpo com CNPJ, texto e número nos valores dá um motivo com os nomes e sem nenhum dos valores.
- **Os testes de hoje que mudam,** e cada um diz por quê:
  - `Companies_in_an_envelope_are_a_contract_refusal` passa a provar a aceitação;
  - o `[{"contribuinteId":1}]` sai da teoria `Taxpayers_without_the_value_array_are_a_contract_refusal`, porque o array
    agora é aceito. Os outros casos ficam, e ganham a conferência da mensagem;
  - o resumo da classe deixa de dizer "as empresas num array puro".
- **As mutações,** aplicadas, rodadas e revertidas, cada uma anotada na tarefa com o teste que caiu:
  - aceitar qualquer objeto sem olhar o `value`;
  - pôr o valor da propriedade na mensagem.

### D9. O que esta correção dá às provas do `platform-establishment-resolution`

O estado das duas provas: a 6.1, contra o mock, está marcada (2026-10-05), e a 6.2, contra o sandbox, está aberta.

- **A 6.1** foi feita contra um mock que servia o `/empresa` em array com a query, ao contrário da plataforma. A
  evidência dela continua valendo para o casamento, a duplicidade, a paginação e a sobreposição, que não dependem da
  forma. Para a forma, não vale. Esta change não desmarca a 6.1, e anota nela a divergência e a correção.
- **A 6.2** estava bloqueada por este defeito: a listagem morria na primeira página. Quem a registra e a marca é aquela
  change. O que ela pede fica assim:
  - **já provado por chamada direta, nas empresas:** o `$orderby` e o `$skip` (a chamada com `$skip=2`), a página vazia
    (a chamada com `$skip=999`) e o `$select` aceito (o próprio defeito). As fixtures do sandbox guardam a evidência
    (D5);
  - **provado por esta change, pelo caminho do hub** (grupo 6 das tarefas): a listagem completa das empresas e dos
    contribuintes, sem recusa de contrato, pela `listing` da sonda e pela recusa da esteira; e o de/para de um
    estabelecimento conhecido, pela `listing --cnpj`;
  - **o que continua com a 6.2:** o `$orderby` e o `$skip` dos contribuintes, que não foram chamados direto. Com o `$top`
    padrão de 100, eles só se exercitam numa empresa com mais de 100 contribuintes. A comparação com o `ListingPageSize` 2,
    da própria 6.2, continua sendo o caminho.

  O item do STATUS dos campos ordenáveis fica meio respondido: o `empresaId` ordena, e o `contribuinteId` ainda não se
  sabe. A anotação vai na 6.2 (tarefa 5.2), e quem atualiza o STATUS é aquela change.

  **Pela esteira, contra o sandbox, o desfecho esperado das cinco NF-e 55 da `Matriz` continua sendo a recusa** "o
  estabelecimento 44278225000180 não tem contribuinte cadastrado na plataforma …". É a prova de que a listagem chegou ao
  fim, e não uma falha desta correção. O caminho feliz, até a nota confirmada, se prova com a saída no mock.

**O contorno em uso no banco de dev muda as provas** (D11). A sobreposição do tenant-a aponta os CNPJs da Contoso para um
contribuinte da TMSA. Com ela, a sobreposição ganha, e a listagem nem é chamada para essas notas:

- **a recusa pela esteira:** só prova a listagem com a sobreposição tirada durante a prova, e reposta no fim;
- **o `listing --cnpj` da sonda:** não é afetado, porque lê a listagem direto, sem consultar a sobreposição;
- **a nota no mock:** a seção do mock não tem a sobreposição, e o perfil volta ao que era no fim, com o contorno.

### D10. As fixtures inventadas são inventadas por inteiro

> **Corrigido em 2026-10-08, depois do arquivamento, a pedido do Marcelo.** Este D10 afirmava que os `contribuinteId` das
> fixtures e do mock já eram inventados, e a conferência por busca não os olhava. A conta de sandbox tem os contribuintes
> `9709` e `9992` a `10004`: o `10001` a `10004` eram reais. As correções estão nos parágrafos "Nenhum valor novo",
> "O que fica", "A faixa reservada", "Fato não é fixture" e "A conferência", e o commit dos valores é o `8200965`.

O `Fixtures/listing/README.md` afirma que os valores são de mentira. Não são: o `Fixtures/listing/` e o mock carregam a
empresa real do sandbox, com o `empresaId` 7410, o `codigoCIA` `"005"`, a razão social e o começo do `idPortalCompany`
(`7e93b784-…`). Ou se consertam os valores, ou se conserta o README, e esta change escreve a regra de mascaramento:
publicá-la violada uma pasta ao lado tira a credibilidade dela.

**O que muda,** no `Fixtures/listing/`, no `PlatformHandler` e no mock:

- **tudo o que veio do sandbox:** o `empresaId`, o `codigoCIA`, a `descricao` e o `idPortalCompany`. Um `empresaId` real
  numa fixture que se declara de mentira é o mesmo problema da razão social, só menos visível;
- **os nomes dos arquivos:** `contribuintes-7410.json` e os outros levam o `empresaId` no nome, e mudam com ele.

**Nenhum valor novo pode coincidir com um do sandbox:**

- os `empresaId` 7330 e 7407 a 7413, todos da conta: os pares com o `codigoCIA` foram conferidos em 2026-10-08 (tarefa
  1.5);
- os `codigoCIA` de `"001"` a `"005"`, o `"Padrão"`, o `"SPL"` e o `"QA"`, todos da conta;
- os `contribuinteId` `9709` e `9992` a `10004`, todos da conta (conferidos em 2026-10-08, pela sonda);
- as razões sociais e os `idPortalCompany`.

**As propriedades que os valores inventados preservam:**

- **o `codigoCIA` não acompanha a ordem do `empresaId`,** como no sandbox (a 7408 é `"004"`, e a 7409 é `"003"`);
- **os códigos de contribuinte não seguem a ordem do CNPJ,** de propósito: um código derivado da ordem falha, em vez de
  passar por coincidência. O README já registra isso;
- **o `codigoCIA` é texto livre,** e pelo menos um tem acento, como o README registra.

**O que fica:** os CNPJs dos contribuintes. No mock e nas fixtures, eles são os da Contoso no D365 de dev, de propósito,
para as notas gravadas resolverem, e os de exemplo (`11222333000181`, `12345678000190`, `99888777000166`). Nenhum é da
conta de sandbox. Os códigos de contribuinte (`codigo`) são códigos curtos e genéricos, e caem na regra do código
genérico, abaixo.

**Os `contribuinteId` não eram inventados.** A versão anterior deste parágrafo dizia que eram, sem ter conferido: o
`/contribuinte` nunca foi chamado direto, e nenhum identificador de contribuinte da conta tinha sido visto. O `10001`, o
`10002`, o `10003` e o `10004` do mock, das fixtures e dos testes eram reais, e o `10005` a `10009` eram os próximos que a
sequência da plataforma emite. Todos passaram à faixa reservada. Um identificador que parece inventado não é prova de
que é: foi exatamente assim que o `10001` passou.

**A faixa reservada dos identificadores inventados:** `2.000.000.000` mais o número antigo (o `10001` virou
`2000010001`). Por quê:

- **os identificadores da plataforma são sequenciais e positivos.** A conta mostra empresas de `7330` a `7413` e
  contribuintes de `9709` e de `9992` a `10004`, e a sequência cresce de um em um. Para chegar a dois bilhões, ela teria de
  avançar dois bilhões de cadastros: a faixa fica fora do alcance dela;
- **cabe no `int` de 32 bits,** o tipo do mock e do `PlatformHandler`, que vai até `2.147.483.647`;
- **o número antigo continua legível** no fim, e a busca por palavra inteira não confunde um com o outro;
- **negativos colidiriam menos ainda, mas não servem:** o mock ordena o `$orderby` pelo texto do número (`D12`), e um
  identificador negativo mudaria a ordem da listagem. Seria mexer em comportamento numa troca de valores.

Todo identificador inventado novo sai desta faixa. Os inventados de antes que ficaram fora dela (os `empresaId` `8120` a
`8122`, `8201` a `8207`, `9101` e `9102`, e os `contribuinteId` `20001`, `30001`, `50001`, `50002`, `60001` e `90001` em
diante) foram conferidos contra a conta em 2026-10-08 e não coincidem com nenhum real.

**Fato não é fixture.** As duas coisas seguem regras opostas:

- **uma menção que descreve o sandbox como fato,** num ADR, num README do `Fixtures/sandbox/`, num registro de prova ou
  num relato de tarefa, é evidência. O valor real fica, porque é ele que se está registrando. É o caso do ADR-0033, que
  cita os `codigoCIA` `"Padrão"`, `"QA"` e `"SPL"` da conta, e das respostas gravadas no `Fixtures/sandbox/`;
- **uma fixture, o mock, um exemplo de spec ou um valor montado num teste é inventado,** e não pode carregar nenhum valor
  real: nem razão social, nem `idPortalCompany`, nem identificador de empresa ou de contribuinte. Se um valor real for
  preciso, ele é gravado no `Fixtures/sandbox/`, e o teste o lê de lá.

**O escopo é só valores:** as fixtures, o `PlatformHandler`, o mock, os testes que afirmam esses valores, o README e as
linhas do RUNNING que mostram os códigos e a descrição do mock. Nenhuma mudança de comportamento. Que vários testes
confiram o texto não é risco: é substituição mecânica, e o teste que quebra é justamente o que deve quebrar.

Vários testes do adapter não leem as fixtures, mas montam a mesma empresa real inline: o
`AvalaraDispatcherPlatformCodesTests`, o `AvalaraOutboundSettingsTests` e o `AvalaraEstablishmentListingTests` usam
`WithCompany(7410, "005", "RESULTA", …)`. Eles também mudam, porque a razão social e o `empresaId` do sandbox não ficam em
lugar nenhum fora do `Fixtures/sandbox/`, e o `"005"` muda junto com eles. Um código genérico que aparece sozinho, sem a
empresa do sandbox, fica como está, como os do `PlatformEstablishmentResolverTests`.

**A ordem: num commit separado, antes do conserto da forma.** É muita linha trocada e nenhuma lógica, e misturada ao
conserto esconderia a mudança que importa. Vindo antes, os arquivos que o conserto cria, como o `empresas-envelope.json`,
já nascem inventados. A suíte verde antes e depois, com só valores trocados, prova que o comportamento não mudou.

**A conferência.** A versão anterior era uma busca por uma lista de valores reais montada com o que tinha aparecido nas
chamadas diretas: razões sociais, `idPortalCompany` e `empresaId`. O buraco estava aí: os `contribuinteId` não entraram
na lista porque este D10 os tinha posto em "o que fica", e a busca só achava o que já se sabia ser real. Agora:

- **a lista é a da conta inteira, e não a do que foi visto.** Os identificadores reais (os `empresaId` e os
  `contribuinteId`) ficam em `Fixtures/sandbox/identificadores-da-conta.json`, gravados pelo `listing --ids` da sonda, que
  imprime só números;
- **um teste recusa qualquer um deles** nas fixtures inventadas (as chaves `empresaId` e `contribuinteId` do
  `Fixtures/listing/`) e nos registros do mock (`SandboxFixtureTests`);
- **o resto continua por busca, com a lista inteira:** os identificadores escritos dentro do código dos testes, o RUNNING
  e as specs, com as razões sociais e os `idPortalCompany`, fora do `Fixtures/sandbox/`. Ficam fora as coincidências que
  não são identificador da plataforma, como a porta 10001 do Azurite e os `RecId` do D365;
- **a lista envelhece com a conta.** Antes de pôr um valor inventado que não sai da faixa, regravar o arquivo pela sonda.

### D11. O item de Operação do STATUS

O ambiente de dev tem uma limitação que nenhuma prova desta change resolve, e o contorno em uso precisa de registro com
data de validade. O item vai na seção de Operação do `docs/STATUS.md`, com três partes:

- **o ambiente:** o D365 de dev (o fiscosysdev, Contoso, raiz `44278225`) e a conta Avalara de sandbox (TMSA, IMS, ELTER,
  BULKTECH e RESULTA) descrevem empresas diferentes, sem nenhum CNPJ em comum. O de/para casa por CNPJ, então nenhum
  estabelecimento do ERP de dev resolve na plataforma de dev, e o fim da esteira só fecha contra o mock;
- **o contorno em uso, temporário:** a sobreposição `OutboundSettings.sandbox.establishments` do tenant-a aponta os quatro
  CNPJs da Contoso para um mesmo contribuinte da TMSA, para as notas chegarem à validação da Avalara e os erros seguintes
  aparecerem. Ela vive só no banco de dev, não está no repositório, e some num `docker compose down -v`. O item diz o
  gatilho de remoção, o cuidado enquanto ela existir e o sintoma se ficar esquecida (tarefa 5.3);
- **a alternativa recusada, para não ser reaberta:** despachar sem o código do contribuinte quando o CNPJ não resolve.

**Por que a alternativa foi recusada:**

- **o `AvalaraJson` não escreve campo nulo.** Não sairia um campo em branco, e sim um payload sem o campo;
- **a premissa de que a plataforma recusaria do mesmo jeito não está testada.** O padrão provável para identificador
  ausente é o contribuinte principal da conta. O pior caso é aceitar e registrar a nota debaixo de outro contribuinte;
- **a recusa de hoje diz o quê e o que fazer.** O erro remoto viria diluído entre campos sem relação.

## Risks / Trade-offs

- **[A forma aceita esconde uma mudança real de contrato]** → A terceira forma continua sendo recusa, e agora diz o que
  veio. As guardas dos itens e da paginação não mudam. Um envelope com itens de outro formato cai na empresa sem
  `empresaId`, ou no item que não é objeto.
- **[A página vazia em envelope]** → Confirmada no sandbox (a chamada com `$skip=999`). Com a query do hub, é ela que
  encerra toda leitura. O `{"value": []}` encerra como o `[]` já encerrava, e o teste da `empresas-vazio.json` guarda
  isso. Um corpo de erro na forma de envelope vazio seria indistinguível do fim da lista, como o array vazio já era: não
  há caso novo.
- **[Um nome de propriedade que carrega dado]** → Um objeto cujas chaves fossem CNPJs vazaria pelos nomes. Numa resposta
  de lista OData, é improvável. O corpo já passou pela redação de segredos antes do motivo, e o teto de dez nomes limita
  o alcance.
- **[A regra do mock generaliza duas amostras]** → "Qualquer opção `$` liga o envelope" pode não ser a regra exata da
  plataforma. Para o hub, que aceita as duas formas, não importa. O mock só precisa reproduzir as duas chamadas
  observadas, e reproduz.
- **[A resposta real gravada expõe dado de cliente]** → A curadoria é à mão, a partir dos corpos do Postman, e mascara
  por uma lista do que fica, e não do que sai. A regra não depende de quem curou: a varredura do `SandboxFixtureTests`
  recusa CNPJ nos arquivos `listagem-*`, e também todo valor fora da lista do que fica que não esteja `[mascarado]`.
- **[As respostas gravadas são de um dia]** → A reprodução confere a forma e os itens gravados, e não a contagem atual da
  conta, que muda.

## Migration Plan

- **Sem migração de banco, sem configuração nova** e sem mudança de API do host. As `AvalaraOptions` e o perfil continuam
  os mesmos.
- **A branch:** a correção sai de uma branch nova a partir do `main`, onde o defeito está. A pasta desta change foi criada
  na árvore da `feat/explicit-credential-and-execution-cnpj`, que tem trabalho em andamento: ela vai para a branch nova
  sem levar o resto.
- **Os commits:** primeiro o dos valores inventados (D10), só com valores trocados e a suíte verde. Depois o conserto da
  forma, com a reprodução e a correção.
- **O banco de dev:** a change não toca o contorno da sobreposição. As provas que precisam dela fora a tiram e a repõem
  (D9), e o item do STATUS registra quando ela sai de vez (D11).
- **A ordem de arquivamento:** o `platform-establishment-resolution` primeiro, porque é ele que leva a
  `avalara-establishment-listing` para `openspec/specs/`. O `openspec validate` desta change passa antes disso, mas o
  arquivamento só acha os requisitos modificados depois.
- **O rollback:** reverter o código. O tenant sem a tabela `establishments` volta a ter todo envio recusado pela forma do
  `/empresa`. A saída, nesse caso, é preencher a tabela, que ganha da listagem e nem a chama.
