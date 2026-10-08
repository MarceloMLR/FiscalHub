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
casa dos dez mil, eram de contribuintes reais da conta, e outros cinco eram os próximos que a plataforma ia emitir
(conferido em 2026-10-08; a lista real fica em `../sandbox/identificadores-da-conta.json`). Duas defesas cobrem falhas
diferentes, e uma não substitui a outra:

- **a varredura pega o que é real hoje.** Os identificadores reais da conta ficam em
  `../sandbox/identificadores-da-conta.json`, gravados pela sonda (`listing --ids`). O `SandboxFixtureTests` recusa
  qualquer um deles nas chaves `empresaId` e `contribuinteId` daqui e nos registros do mock, e a falha diz em que data a
  lista foi gravada (`recordedAt`). Os identificadores escritos dentro do código dos testes e nas specs ficam para a busca,
  com a mesma lista. O limite é a gravação: ninguém é obrigado a regravá-la, e uma lista velha não vê o que a conta
  cadastrou depois dela;
- **a faixa reservada é o que torna um valor novo seguro para sempre,** justamente porque a gravação envelhece. Todo
  identificador inventado novo, de empresa ou de contribuinte, sai de `2.000.000.000` em diante: o número antigo ganha
  dois bilhões na frente, como o `2000010001`. A plataforma numera em sequência, de um em um, e todo número abaixo da
  faixa está no caminho dela: um dia a conta chega lá, e o valor inventado vira real sozinho, sem que a varredura acuse,
  porque ela lê uma gravação. Um valor da faixa não depende de a gravação estar em dia. Ela cabe no `int` do mock e do
  `PlatformHandler`.

**Os inventados de antes, fora da faixa, ficam como estão:** os `empresaId` `8120` a `8122`, `8201` a `8207`, `9101` e
`9102`, e os `contribuinteId` `20001`, `30001`, `50001`, `50002`, `60001` e `90001` em diante. Eles não são seguros: os
`empresaId` da conta estão em `7413`, e os inventados começam em `8120`, no caminho de crescimento da sequência, só mais
longe; o mesmo vale para os de contribuinte. São aceitáveis por serem **detectáveis**, e só enquanto a gravação for
regravada: os das fixtures e dos registros do mock, pelo teste; os do código dos testes e das specs, pela busca; o `90001`
em diante, que o mock emite ao adicionar um contribuinte, pela busca nos testes que o afirmam. Eles migram para a faixa de
forma oportunista, quando alguém mexer naquelas fixtures ou naqueles testes por outro motivo.

**As cópias em `openspec/changes/archive/`** são anteriores à regra e ficam como estão. São o registro do que cada change
propôs na época, e não material vivo: quem governa é a spec canônica em `openspec/specs/`, que segue a regra. Um documento
arquivado só recebe uma correção datada quando afirma uma regra falsa, e o registro histórico dele não muda.
- **fato não é fixture:** uma menção que descreve o sandbox como fato, num ADR, num README de `../sandbox/` ou num
  registro de prova, guarda o valor real, porque é ele a evidência. Uma fixture, o mock, um exemplo de spec ou um valor
  montado num teste é inventado, e não carrega valor real nenhum.
