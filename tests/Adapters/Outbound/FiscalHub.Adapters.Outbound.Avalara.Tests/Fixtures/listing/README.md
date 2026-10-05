# Fixtures da listagem de estabelecimentos

Não são respostas gravadas. A pasta `../sandbox/` guarda só o que veio da plataforma; esta guarda a **forma** verificada
pelo Marcelo em 2026-10-02, com **valores de mentira** (change `platform-establishment-resolution`, tarefa 2.2):

- `GET /taxcompliance/v2/empresa` devolve um **array puro**, sem envelope;
- `GET /taxcompliance/v2/contribuinte?empresaId=` devolve **`{"value": [...]}`**;
- os campos são os do `$select` que o hub pede: `empresaId,codigoCIA,descricao` nas empresas e
  `contribuinteId,codigo,cnpj` nos contribuintes. O `codigoCIA` é texto livre, com acento (`"Padrão"`).

As variantes `*-campos-a-mais.json` trazem também `idPortalCompany`, `empresaId` e `razao`: são o cenário da plataforma
que ignora o `$select`, e o resultado da listagem não pode mudar com elas.

Os códigos dos contribuintes **não** seguem a ordem do CNPJ (a `0001` não é `"001"`): um código derivado da ordem falharia
aqui, em vez de passar por coincidência.
