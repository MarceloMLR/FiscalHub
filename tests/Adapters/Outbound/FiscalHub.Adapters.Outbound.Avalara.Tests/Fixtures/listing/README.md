# Fixtures da listagem de estabelecimentos

Não são respostas gravadas. A pasta `../sandbox/` guarda só o que veio da plataforma; esta guarda a **forma** verificada
pelo Marcelo, com **valores de mentira** (changes `platform-establishment-resolution`, tarefa 2.2, e
`platform-listing-shape`).

**A forma depende de a chamada ter opções de query.** O mesmo endpoint tem duas respostas, e o hub aceita as duas:

| Arquivo | Forma | De qual chamada veio |
|---|---|---|
| `empresas.json` | **array puro** | `GET /taxcompliance/v2/empresa`, **sem** opções de query (2026-10-02) |
| `empresas-envelope.json` | **`{"value": [...]}`** | o mesmo endpoint **com** `$top` e `$orderby`, com e sem `$skip` (2026-10-06). É a forma que o hub recebe, porque sempre chama com a query |
| `empresas-vazio.json` | **`{"value": []}`** | a página vazia com a query, `$skip=999` (2026-10-06). É ela que encerra toda leitura do hub |
| `contribuintes-*.json` | **`{"value": [...]}`** | `GET /taxcompliance/v2/contribuinte?empresaId=`, sempre chamado com query (2026-10-02). A forma dele sem opções nunca foi observada |

As respostas reais das chamadas de 2026-10-06, com o que identifica empresa mascarado, estão em `../sandbox/`
(`listagem-empresas-*.json`). As de 2026-10-02 não foram gravadas.

Os campos são os do `$select` que o hub pede: `empresaId,codigoCIA,descricao` nas empresas e `contribuinteId,codigo,cnpj`
nos contribuintes.

As variantes `*-campos-a-mais.json` trazem também `idPortalCompany`, `empresaId` e `razao`: são o cenário da plataforma
que ignora o `$select`, e o resultado da listagem não pode mudar com elas.

## Os valores são inventados por inteiro

Nenhum `empresaId`, `contribuinteId`, `codigoCIA`, razão social ou `idPortalCompany` daqui vem da conta de sandbox
(change `platform-listing-shape`, D10). As respostas reais, com o que identifica empresa mascarado, ficam em `../sandbox/`. Os
valores inventados preservam três propriedades da plataforma:

- **o `codigoCIA` não acompanha a ordem do `empresaId`:** a `8120` é `"012"`, e a `8122` é `"009"`, como no sandbox;
- **o `codigoCIA` é texto livre, com acento:** `"Comércio"`;
- **os códigos dos contribuintes não seguem a ordem do CNPJ:** a `0001` não é `"001"`. Um código derivado da ordem falharia
  aqui, em vez de passar por coincidência.

Os CNPJs dos contribuintes são os da Contoso no D365 de dev, os mesmos das notas gravadas, de propósito, e o
`11222333000181` de exemplo. Nenhum é da conta de sandbox.

**Um identificador que parece inventado não prova que é.** Quatro identificadores de contribuinte daqui e do mock, na
casa dos dez mil, eram de contribuintes reais da conta (conferido em 2026-10-08; a lista real fica em
`../sandbox/identificadores-da-conta.json`). Por isso:

- **a faixa reservada:** todo identificador inventado novo, de empresa ou de contribuinte, sai de `2.000.000.000` em
  diante: o número antigo ganha dois bilhões na frente, como o `2000010001`. A plataforma numera em sequência, de um em um, e está na casa dos dez mil; a
  faixa fica fora do alcance dela e cabe no `int` do mock e do `PlatformHandler`;
- **a varredura:** os identificadores reais da conta ficam em `../sandbox/identificadores-da-conta.json`, gravados pela
  sonda (`listing --ids`), e o `SandboxFixtureTests` recusa qualquer um deles nas chaves `empresaId` e `contribuinteId`
  daqui e nos registros do mock. Os identificadores escritos dentro do código dos testes ficam para a busca, com a mesma
  lista;
- **fato não é fixture:** uma menção que descreve o sandbox como fato, num ADR, num README de `../sandbox/` ou num
  registro de prova, guarda o valor real, porque é ele a evidência. Uma fixture, o mock, um exemplo de spec ou um valor
  montado num teste é inventado, e não carrega valor real nenhum.
