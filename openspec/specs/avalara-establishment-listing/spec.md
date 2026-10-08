# avalara-establishment-listing Specification

## Purpose

Listar as empresas e os contribuintes da conta do tenant na Avalara, nos formatos verificados, paginando em ordem estável
até a página vazia, para a resolução do estabelecimento pela plataforma. Os códigos são lidos como a plataforma os guarda,
e qualquer falha deixa a listagem inteira sem uso.

## Requirements

### Requirement: As empresas e os contribuintes da conta do tenant

O adapter da Avalara MUST declarar a listagem de estabelecimentos. A listagem MUST ler, na URL base do ambiente ativo do
tenant:

- **as empresas:** em `taxcompliance/v2/empresa`, com `$select=empresaId,codigoCIA,descricao` e `$orderby=empresaId`;
- **os contribuintes de cada empresa:** em `taxcompliance/v2/contribuinte?empresaId=<empresaId>`, com o `empresaId` como
  veio, `$select=contribuinteId,codigo,cnpj` e `$orderby=contribuinteId`.

A resposta de cada página, nos dois endpoints, vem numa das duas formas do requisito "As duas formas de uma lista". Nenhum
endpoint tem uma forma fixa.

O `empresaId` é obrigatório nos contribuintes, e por isso a leitura é por empresa: não há como pedir os contribuintes de um
CNPJ na conta inteira. Uma empresa pode ter vários contribuintes, ou nenhum. Um campo que venha além dos pedidos no
`$select` é ignorado.

A `descricao` da empresa serve só ao motivo da duplicidade (`platform-establishment-resolution`). Ela MUST NOT participar
do casamento nem ir no payload.

A listagem MUST NOT filtrar por tenant: a credencial do tenant já a limita à conta dele. Ela MUST NOT mandar o
`subscriptionId`, que é opcional nos dois endpoints.

#### Scenario: Os dois formatos
- **WHEN** a plataforma devolve `[{"empresaId": 9101, "codigoCIA": "017", "descricao": "EMPRESA EXEMPLO"}]` em empresas,
  e `{"value": [{"contribuinteId": 50001, "codigo": "031", "cnpj": "11222333000181"}]}` nos contribuintes da empresa `9101`
- **THEN** o CNPJ `11222333000181` é listado com o código de empresa `017`, a descrição `EMPRESA EXEMPLO` e o código de
  contribuinte `031`

#### Scenario: O `$select` e o `$orderby` nos dois pedidos
- **WHEN** a listagem pede as empresas e os contribuintes da empresa `9101`
- **THEN** o pedido de empresas leva `$select=empresaId,codigoCIA,descricao` e `$orderby=empresaId`
- **AND** o de contribuintes leva `empresaId=9101`, `$select=contribuinteId,codigo,cnpj` e `$orderby=contribuinteId`

#### Scenario: Campos além do `$select`
- **WHEN** a plataforma ignora o `$select` e devolve também `idPortalCompany` nas empresas e `razao` nos contribuintes
- **THEN** a listagem segue com o mesmo resultado

#### Scenario: Uma empresa com vários contribuintes
- **WHEN** a empresa `017` tem três contribuintes, com três CNPJs
- **THEN** os três são listados, cada um com o código de empresa `017` e o próprio código

#### Scenario: Uma empresa sem contribuinte
- **WHEN** a empresa `LAB` devolve a lista vazia de contribuintes, `{"value": []}` ou `[]`
- **THEN** a listagem segue, sem estabelecimento dessa empresa

### Requirement: Os códigos como texto, como vieram

O `codigoCIA` da empresa MUST ir no `codigoEmpresa`, e o `codigo` do contribuinte no `codigoContribuinte`. Os dois MUST
ser lidos como texto e usados exatamente como vieram: com os zeros à esquerda, os acentos, a caixa e os espaços.

Um código que não vem como texto JSON MUST ser tratado como ausente. Ele MUST NOT ser convertido de número para texto,
porque a conversão perderia os zeros à esquerda que o texto teria.

#### Scenario: Os zeros à esquerda
- **WHEN** o `codigoCIA` é `"017"`
- **THEN** o payload leva `codigoEmpresa = "017"`, e não `"17"`

#### Scenario: O código com acento
- **WHEN** o `codigoCIA` é `"Comércio"`, e é a empresa do único contribuinte com o CNPJ do estabelecimento próprio
- **THEN** o payload leva `codigoEmpresa = "Comércio"`

#### Scenario: O código numérico
- **WHEN** o `codigo` do único contribuinte com o CNPJ vem como o número JSON `1`
- **THEN** o envio é rejeitado porque o contribuinte não tem código

### Requirement: A credencial e o token do envio

A listagem MUST usar a URL base, a credencial e o token do envio no ambiente ativo do tenant (`avalara-tenant-authentication`):

- **o token:** o mesmo cache. Com um token válido guardado, a listagem MUST NOT pedir outro;
- **o endereço:** a mesma regra. A credencial só vai ao endereço do próprio ambiente, por `https`, ou por `http` só em
  loopback;
- **a redação:** a mesma do envio. O corpo das respostas é redigido antes de qualquer motivo ou log. A listagem MUST NOT
  ser gravada como foto do documento.

#### Scenario: Um token para a listagem e para o envio
- **WHEN** a primeira nota do tenant-a é despachada sem token guardado, e a resolução lista a plataforma
- **THEN** o endpoint de token recebe um pedido só, para a listagem e para o envio

### Requirement: As falhas da listagem

Cada resposta da listagem MUST ter um destes desfechos (`platform-establishment-resolution`):

| Resposta | Desfecho |
|---|---|
| 2xx numa das duas formas de uma lista | segue |
| 2xx em outra forma: a raiz não é um array, nem um objeto com o array em `value` | recusa, "Contrato do destino: …", nomeando o endpoint e o que veio (o requisito "A recusa da forma diz o que veio") |
| 2xx com uma página igual à anterior | recusa, "Contrato do destino: …", nomeando o endpoint e o `$skip` (a paginação que não avança) |
| 2xx sem chegar à página vazia dentro do teto de páginas | recusa, "Contrato do destino: …", nomeando o endpoint e o teto |
| 401 com o token do cache | o token é descartado, e a falha é indisponibilidade, como no envio |
| 401 com token recém-emitido, ou 403 | recusa, "Configuração do conector: a plataforma negou a listagem…", com o motivo da plataforma. O motivo diz que, sem a listagem, a tradução vai na tabela `establishments` |
| 404 | recusa, "Configuração do conector: …", que aponta as duas partes da URL: a URL base do perfil e o caminho no appsettings do host |
| outro 4xx | recusa, com o status e o motivo da plataforma |
| 5xx, 429, tempo esgotado, rede | indisponibilidade |

#### Scenario: A plataforma nega a listagem
- **WHEN** a requisição de empresas responde HTTP 403
- **THEN** a nota é rejeitada com motivo de configuração do conector que cita o tenant, o ambiente, o HTTP 403 e o motivo
  da plataforma
- **AND** o motivo aponta a tabela `establishments` como saída

#### Scenario: O token do cache vencido
- **WHEN** a requisição de contribuintes responde HTTP 401 com o token do cache
- **THEN** o token é descartado, e a nota segue o retry nativo

#### Scenario: O caminho errado
- **WHEN** a requisição de empresas responde HTTP 404
- **THEN** a nota é rejeitada com motivo que aponta a URL base do perfil e o caminho de empresas no appsettings

#### Scenario: Empresas em envelope
- **WHEN** a requisição de empresas responde 200 com `{"value": [...]}`
- **THEN** as empresas são lidas como no array, sem recusa: o envelope é uma das duas formas de uma lista

#### Scenario: Um corpo que não é lista
- **WHEN** a requisição de empresas responde 200 com `{"error": "unavailable", "message": "tente mais tarde"}`
- **THEN** a nota é rejeitada com motivo de contrato do destino que nomeia o endpoint de empresas e as propriedades
  `error` e `message`

### Requirement: A listagem é paginada pelo cliente

As duas leituras MUST ser paginadas pelo hub, com `$top` e `$skip`, nos parâmetros que o Swagger da plataforma declara
(conferido em 2026-10-02). A plataforma não devolve marca de página seguinte.

- **A ordem:** todo pedido MUST levar a ordem estável, `$orderby=empresaId` nas empresas e `$orderby=contribuinteId` nos
  contribuintes. Sem ordem estável, paginar com `$skip` é indefinido: o mesmo item pode vir em duas páginas, ou em
  nenhuma, sem erro do servidor.
- **O tamanho da página:** um `$top` fixo durante a listagem, configurável no host. Sem configuração, ele é 100. Um valor
  zero ou negativo MUST impedir o host de subir: com o `$top` 0, a primeira página viria vazia, e a conta pareceria não ter
  empresas.
- **O avanço:** a primeira página tem `$skip=0`, e cada página seguinte soma ao `$skip` o número de itens **recebidos** na
  anterior, e não o `$top`. Um servidor que limite a página abaixo do `$top` faz a leitura dar mais páginas, e não pular
  itens.
- **A parada:** só a página vazia encerra a leitura. Uma página com menos itens que o `$top` MUST NOT encerrar: ela pode
  ser o limite do servidor, e não o fim da lista.
- **O teto:** no máximo 50 páginas por lista, configurável no host. Um valor zero ou negativo MUST impedir o host de subir.
  A lista que não chega à página vazia dentro do teto MUST encerrar a listagem com recusa de contrato do destino, nomeando
  o endpoint e o teto, e nenhuma página além do teto MUST ser pedida. É a guarda geral contra a leitura que não termina.
- **Os dois endpoints:** valem para as empresas e para os contribuintes. Os contribuintes de cada empresa têm a própria
  paginação, que começa em `$skip=0`, e o próprio teto.
- **A paginação que não avança:** uma página não vazia igual à anterior, item a item, MUST encerrar a listagem com recusa
  de contrato do destino, nomeando o endpoint e o `$skip`. Com a ordem estável, é assim que aparece uma plataforma que
  ignora o `$skip`, sem gastar o teto.
- **A completude:** a falha em qualquer página é a falha da listagem inteira (`platform-establishment-resolution`).

#### Scenario: Três páginas de empresas
- **WHEN** o `$top` é 2 e a conta tem 3 empresas
- **THEN** a listagem pede as empresas com `$skip=0` (2 itens), `$skip=2` (1 item) e `$skip=3` (vazia), e para na página
  vazia
- **AND** as 3 empresas são listadas

#### Scenario: A página curta não encerra
- **WHEN** o `$top` é 100, o servidor devolve no máximo 10 itens por página, e a empresa `9101` tem 25 contribuintes
- **THEN** a listagem pede os contribuintes da `9101` com `$skip=0`, `10`, `20` e `25`, e para na página vazia
- **AND** os 25 contribuintes são listados, sem nenhum pulado

#### Scenario: Cada empresa começa do zero
- **WHEN** a listagem passa da empresa `9101` para a `9102`
- **THEN** o primeiro pedido de contribuintes da `9102` tem `$skip=0`

#### Scenario: A falha na segunda página
- **WHEN** a segunda página dos contribuintes de uma empresa responde HTTP 503
- **THEN** nenhum estabelecimento é resolvido pela listagem, nem os da primeira página

#### Scenario: Página de tamanho zero, ou teto zero
- **WHEN** a configuração do host traz o tamanho da página, ou o teto de páginas, da listagem igual a zero
- **THEN** o host não sobe, com motivo que nomeia a configuração

#### Scenario: O `$skip` ignorado
- **WHEN** a página de empresas com `$skip=2` volta igual à de `$skip=0`
- **THEN** a nota é rejeitada com motivo de contrato do destino que nomeia o endpoint de empresas e o `$skip=2`
- **AND** nenhuma página a mais é pedida

#### Scenario: O teto de páginas
- **WHEN** os contribuintes de uma empresa voltam com itens em todas as 50 páginas
- **THEN** a nota é rejeitada com motivo de contrato do destino que nomeia o endpoint de contribuintes, a empresa e o teto
  de 50 páginas
- **AND** a 51ª página não é pedida

### Requirement: As duas formas de uma lista

Cada resposta 2xx de uma página da listagem, das empresas ou dos contribuintes, MUST ser lida numa destas duas formas,
as duas observadas no sandbox:

- **o array:** a raiz é um array JSON, e ele é a página;
- **o envelope:** a raiz é um objeto JSON com a propriedade `value` sendo um array, e o `value` é a página. As outras
  propriedades do objeto, como `@odata.context`, `@odata.count` e `@odata.nextLink`, MUST ser ignoradas.

Qualquer outra forma é recusa de contrato do destino (o requisito "As falhas da listagem").

- **A forma é de cada resposta.** Ela MUST NOT depender do endpoint, da query pedida nem da forma da página anterior. Os
  mesmos itens MUST dar o mesmo resultado nas duas formas.
- **O que vale para os itens não muda com a forma.** Todo item MUST ser um objeto, e a empresa sem `empresaId` continua
  sendo recusa. A ordem, o `$skip` pelos itens recebidos, a parada só na página vazia, a página repetida e o teto de
  páginas valem igual nas duas formas.
- **A página vazia vale nas duas formas.** O `[]` e o `{"value": []}` são a página vazia, e MUST encerrar a leitura da
  lista, sem recusa. Com a query do hub, o sandbox devolve a página vazia em envelope (verificado em 2026-10-06).
- **A página repetida se compara pelos itens,** e não pelo corpo: a mesma página numa forma e na outra é a mesma página.
- **A paginação continua sendo do cliente.** Um `@odata.nextLink` no envelope MUST NOT ser seguido, MUST NOT substituir
  o `$skip` e MUST NOT encerrar a leitura.

#### Scenario: As empresas em envelope, como o sandbox devolve com a query
- **WHEN** a requisição de empresas responde 200 com
  `{"value": [{"empresaId": 9101, "codigoCIA": "017", "descricao": "EMPRESA EXEMPLO", "idPortalCompany": "0000…"}]}`,
  e os contribuintes da empresa `9101` trazem `{"contribuinteId": 50001, "codigo": "031", "cnpj": "11222333000181"}`
- **THEN** o CNPJ `11222333000181` é listado com o código de empresa `017` e o código de contribuinte `031`

#### Scenario: A página vazia em envelope encerra a leitura
- **WHEN** o `$top` é 5, a página de empresas com `$skip=0` traz 5 empresas em envelope, e a de `$skip=5` responde
  `{"value": []}`
- **THEN** a leitura das empresas para nessa página, sem recusa, e as 5 empresas são listadas
- **AND** nenhuma página de empresas além dela é pedida

#### Scenario: As duas formas dão o mesmo resultado, nas duas listas
- **WHEN** a mesma conta é listada duas vezes, uma com as empresas e os contribuintes em array, e outra com os dois em
  envelope
- **THEN** as duas listagens têm os mesmos estabelecimentos, com os mesmos códigos, na mesma ordem

#### Scenario: Os contribuintes num array
- **WHEN** a requisição de contribuintes da empresa `9101` responde 200 com
  `[{"contribuinteId": 50001, "codigo": "031", "cnpj": "11222333000181"}]`
- **THEN** o CNPJ `11222333000181` é listado com o código de contribuinte `031`

#### Scenario: As propriedades do OData no envelope
- **WHEN** o `$top` é 2, e a página de empresas com `$skip=0` vem com `@odata.context`, `@odata.count` igual a 3,
  `@odata.nextLink` e duas empresas em `value`
- **THEN** a próxima página pedida é a do hub, com `$skip=2`, e não a do `@odata.nextLink`
- **AND** a leitura só para na página vazia

#### Scenario: A mesma página na outra forma
- **WHEN** a página de empresas com `$skip=2` traz os mesmos itens da página com `$skip=0`, uma em array e a outra em
  envelope
- **THEN** a nota é rejeitada com motivo de contrato do destino que nomeia o endpoint de empresas e o `$skip=2`

### Requirement: A recusa da forma diz o que veio

A recusa de contrato do destino por forma MUST nomear o endpoint e dizer o que veio:

| O que veio | O motivo diz |
|---|---|
| um objeto sem `value` | "um objeto com as propriedades `<nomes>`", ou "com a propriedade `<nome>`" quando é uma só, ou "um objeto vazio" |
| um objeto com `value` que não é array | os nomes, como acima, e que o `value` não é um array, com o tipo dele |
| um número, um texto, um booleano ou `null` na raiz | o tipo: "um número", "um texto", "um booleano" ou "null" |
| um corpo que não é JSON | que a lista não respondeu com JSON |

- **Os nomes:** as propriedades de primeiro nível do objeto recebido, na ordem em que vieram, até dez, e "e mais `<n>`"
  quando passam disso.
- **Nunca os valores.** O motivo MUST NOT trazer o valor de nenhuma propriedade, nem o número ou o texto que veio na raiz.
  É resposta da plataforma, pode ter dado de cliente, e o motivo vai para o registro do documento e para a tela.
- **A redação de sempre:** o corpo continua sendo redigido antes de qualquer motivo ou log.

#### Scenario: Os nomes, e não os valores
- **WHEN** a requisição de empresas responde 200 com `{"mensagem": "CNPJ 11222333000181 sem acesso", "codigo": 9101}`
- **THEN** a nota é rejeitada com motivo de contrato do destino que diz "um objeto com as propriedades mensagem, codigo"
- **AND** o motivo não traz `11222333000181`, `sem acesso` nem `9101`

#### Scenario: O `value` que não é array
- **WHEN** a requisição de contribuintes responde 200 com `{"value": {"contribuinteId": 50001}}`
- **THEN** a nota é rejeitada com motivo de contrato do destino que nomeia o endpoint de contribuintes, a propriedade
  `value` e diz que ela não é um array, mas um objeto
- **AND** o motivo não traz `50001`

#### Scenario: A raiz que não é objeto nem array
- **WHEN** a requisição de empresas responde 200 com o número `42`, ou com o texto `"ok"`
- **THEN** a nota é rejeitada com motivo de contrato do destino que diz "um número", ou "um texto"
- **AND** o motivo não traz `42`, nem `ok`

#### Scenario: Mais de dez propriedades
- **WHEN** a requisição de empresas responde 200 com um objeto de doze propriedades, sem `value`
- **THEN** o motivo nomeia as dez primeiras, na ordem em que vieram, e diz "e mais 2"

#### Scenario: O objeto vazio
- **WHEN** a requisição de empresas responde 200 com `{}`
- **THEN** o motivo diz "um objeto vazio"
