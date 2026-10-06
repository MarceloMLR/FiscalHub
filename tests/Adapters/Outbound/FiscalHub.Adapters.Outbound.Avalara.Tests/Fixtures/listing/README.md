# Fixtures da listagem de estabelecimentos

Não são respostas gravadas. A pasta `../sandbox/` guarda só o que veio da plataforma; esta guarda a **forma** verificada
pelo Marcelo em 2026-10-02, com **valores de mentira** (change `platform-establishment-resolution`, tarefa 2.2):

- `GET /taxcompliance/v2/empresa` devolve um **array puro**, sem envelope;
- `GET /taxcompliance/v2/contribuinte?empresaId=` devolve **`{"value": [...]}`**;
- os campos são os do `$select` que o hub pede: `empresaId,codigoCIA,descricao` nas empresas e
  `contribuinteId,codigo,cnpj` nos contribuintes.

As variantes `*-campos-a-mais.json` trazem também `idPortalCompany`, `empresaId` e `razao`: são o cenário da plataforma
que ignora o `$select`, e o resultado da listagem não pode mudar com elas.

## Os valores são inventados por inteiro

Nenhum `empresaId`, `codigoCIA`, razão social ou `idPortalCompany` daqui vem da conta de sandbox (change
`platform-listing-shape`, D10). As respostas reais, com o que identifica empresa mascarado, ficam em `../sandbox/`. Os
valores inventados preservam três propriedades da plataforma:

- **o `codigoCIA` não acompanha a ordem do `empresaId`:** a `8120` é `"012"`, e a `8122` é `"009"`, como no sandbox;
- **o `codigoCIA` é texto livre, com acento:** `"Comércio"`;
- **os códigos dos contribuintes não seguem a ordem do CNPJ:** a `0001` não é `"001"`. Um código derivado da ordem falharia
  aqui, em vez de passar por coincidência.

Os CNPJs dos contribuintes são os da Contoso no D365 de dev, os mesmos das notas gravadas, de propósito, e o
`11222333000181` de exemplo. Nenhum é da conta de sandbox.
