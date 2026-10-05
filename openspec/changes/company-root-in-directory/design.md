## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código, conferido em 2026-10-05.

- **O diretório do D365** (`D365CompanyDirectory`):
  - o `ListCompaniesAsync` agrupa pelo CNPJ completo (`GroupBy(e => e.Cnpj)`), e o nome vem do estabelecimento de menor
    código do grupo;
  - o `ListBranchesAsync` filtra por `e.Cnpj == companyCode`, igualdade exata, com o `DistinctBy` por código.
- **A descoberta por período do D365** (`D365DocumentDiscovery`):
  - **o escopo** (linha 67): os estabelecimentos do cadastro com `e.Cnpj == criteria.Company` e, com filial, o código
    dela. O filtro no F&O é pelos pares (`dataAreaId`, código) do escopo, que são exatos, e não muda;
  - **a guarda** (linha 96): descarta a referência cujo `CompanyCode` (o CNPJ completo do estabelecimento, no cabeçalho
    da nota) é diferente de `criteria.Company`.
- **O par de desenvolvimento já fala em raiz.** O `companies.json` tem `12345678` e `98765432`. O catálogo da
  `LocalDocumentDiscovery` guarda `Company = "12345678"` e compara por igualdade. O caminho de XML deriva a empresa como
  raiz e a filial como a ordem (`GoodsInvoiceMetadataExtractor.FromIssuer`: `cnpj[..8]` e `cnpj.Substring(8, 4)`).
- **Os grupos e os cards do D365** usam o CNPJ completo (`document-grouping`, `d365-change-feed`). Ficam como estão.
- **A tela de integrações** (`IntegrationsPage`) mostra a empresa pelo `formatCompany` dos cards, que só mascara o código
  de 14 caracteres. A raiz apareceria sem máscara.
- **Os testes de hoje** afirmam "quatro empresas com uma filial cada" (`D365CompanyDirectoryTests`), e o escopo e a guarda
  por igualdade (`D365DocumentDiscoveryTests`). As fixtures gravadas da `brmf` ficam em `Fixtures/d365/directory/`: o
  cadastro e os períodos da `Matriz` (2017-01-15), da `SP-01` (2026-08-07) e da `SAL-01` (2016-02-05).

## Goals / Non-Goals

**Goals:**

- **Uma regra só para "o estabelecimento é desta empresa":** num lugar só, usada pelo diretório, pelo escopo e pela
  guarda. É o desenho da normalização do CNPJ, que tirou as cópias espalhadas do "só dígitos".
- **Nenhum agendamento gravado deixa de funcionar,** sem migração.

**Non-Goals:**

- **Unificar a chave do caminho de XML com a do D365.** É uma diferença de antes, nos grupos, e não nesta correção.
- **Validar o tamanho da empresa pedida.**

## Decisions

### D1. A raiz e o "é desta empresa", no Domain, ao lado da normalização

```csharp
// FiscalHub.Domain.Goods.TaxIdentifiers
public static string Root(string normalized);                        // os 8 primeiros caracteres, ou o todo se tiver menos
public static bool IsOfCompany(string normalizedCnpj, string company); // começa com a empresa; empresa vazia não casa
```

- **A raiz é texto:** os 8 primeiros caracteres, sem conversão. O CNPJ alfanumérico tem raiz alfanumérica (`12ABC345`).
- **O "é desta empresa" é prefixo, ordinal:** com a raiz, casa com todos os estabelecimentos dela; com um CNPJ completo,
  casa só com ele. Os dois tamanhos que existem (8, do diretório, e 14, do agendamento antigo) ficam certos sem que
  ninguém precise saber qual dos dois chegou.
- **A empresa vazia não casa com nada.** É o que a igualdade de hoje faz com ela. Sem isso, um `""` viraria "todas as
  empresas" por acidente (`StartsWith("")` é verdadeiro).
- **O `FromIssuer` do caminho de XML passa a usar o `Root`,** com o mesmo resultado de hoje. Fica uma definição só de raiz
  no hub.

**Por que no Domain, e não no adapter do D365.** A regra é do CNPJ, e não do F&O. O diretório e a descoberta de outro ERP
vão precisar dela igual, como precisaram da normalização.

### D2. O diretório agrupa pela raiz, e as filiais vêm por prefixo

- **As empresas:** `GroupBy(e => TaxIdentifiers.Root(e.Cnpj))`, na ordem do código. O nome é o do estabelecimento de menor
  código da raiz, como hoje. Na `brmf`, a `Matriz` é o menor código ordinal (`Matriz` < `RJ-01` < `SAL-01` < `SP-01`).
- **As filiais:** `Where(e => TaxIdentifiers.IsOfCompany(e.Cnpj, companyCode))`, com o `DistinctBy` por código de antes.
  Um código de 14 caracteres, o de um agendamento antigo, traz só a filial dele.

### D3. A descoberta: o escopo e a guarda pelo mesmo prefixo

- **O escopo:** `Where(e => criteria.Company is null || TaxIdentifiers.IsOfCompany(e.Cnpj, criteria.Company))`. Com a raiz
  e sem filial, o escopo tem todos os estabelecimentos da raiz, e o filtro do F&O ganha um par por estabelecimento.
- **A guarda:** `!TaxIdentifiers.IsOfCompany(reference.Metadata.CompanyCode, criteria.Company)`. A intenção é a de hoje:
  a nota de um estabelecimento que mudou de CNPJ fica fora. O que muda é que a nota de outra filial da mesma empresa
  passa a entrar, que é o certo quando se pede a empresa inteira.
- **Os logs:** o da guarda passa a dizer que o CNPJ da nota "não é da empresa", e não que é "diferente" dela.

### D4. Sem migração

- **Os agendamentos gravados** guardam o CNPJ completo. Pelo prefixo, eles continuam casando com exatamente aquele
  estabelecimento, como antes.
- **Os novos** guardam a raiz, porque é o que o dropdown passa a oferecer.
- **As duas formas convivem,** e a tela as mascara pelo tamanho (D6).

### D5. O par de desenvolvimento só é conferido

- **O `JsonCompanyDirectory`** devolve as empresas do `companies.json`, que já são raízes de 8 caracteres, e as filiais da
  empresa pedida por igualdade do código. A empresa que ele dá é a que a `LocalDocumentDiscovery` procura.
- **A `LocalDocumentDiscovery`** compara por igualdade com o catálogo, que guarda a raiz. Com a raiz pedida, o resultado é
  o mesmo do prefixo. O único caso diferente seria um CNPJ completo pedido ao catálogo local, que não acontece: a descoberta
  local só atende o tenant cujo ERP não tem descoberta, e a empresa dele vem do diretório de exemplo.
- **Nenhum dos dois muda.** Um teste prova que a empresa do diretório de exemplo é a que a descoberta local aceita.

### D6. A tela de integrações mascara a raiz, sem mexer no formatador dos cards

- **Um formatador novo,** `formatCompanyKey`, no mesmo módulo do `formatCompany`:
  - 8 caracteres viram `NN.NNN.NNN`;
  - 14 caracteres usam o `formatCompany`;
  - qualquer outro tamanho fica como veio.
- **Onde vale:** o dropdown de empresas e as duas tabelas da `IntegrationsPage` (as execuções e os agendamentos).
- **O `formatCompany` não muda.** Os cards e o modal continuam com ele. Mascarar 8 caracteres nele mudaria os cards das
  notas em XML, cuja empresa já é a raiz: seria mexer nos cards.

### D7. O ADR-0034

`docs/adr/0034-empresa-do-diretorio-e-a-raiz-do-cnpj.md` registra:

- a empresa do diretório como a raiz;
- a regra de prefixo, num lugar só (D1);
- o escopo e a guarda da descoberta (D3);
- a convivência sem migração (D4);
- a separação entre o dropdown (a empresa, pela raiz) e os cards (o estabelecimento, pelo CNPJ completo).

Ele revisa o ADR-0032 §1, "A chave: a empresa é o CNPJ do estabelecimento … É a mesma chave dos grupos, sem tradução". A
chave dos grupos continua a mesma, e a da empresa passa a ser a raiz. A ponte entre as duas é o prefixo, e não a
igualdade.

## Risks / Trade-offs

- **[O dropdown e os cards têm chaves diferentes]** → A empresa do dropdown (`44278225`) e a empresa do card
  (`44278225000260`) não são o mesmo texto. Mitigação: nenhum código as compara por igualdade. A única ponte é a descoberta,
  pelo `IsOfCompany`. Os testes do D1 cobrem os dois tamanhos.
- **[Empresa de 9 a 13 caracteres]** → O prefixo casaria com um subconjunto de estabelecimentos. Nada a produz: o diretório
  dá 8, e o agendamento antigo tem 14. Pela API, ela só alcança estabelecimentos do próprio tenant (ADR-0028). Fica sem
  validação (Non-Goals).
- **[O nome da empresa é o de uma filial]** → O nome é o do estabelecimento de menor código, como antes. Num cliente cujo
  menor código não seja o da matriz, o dropdown mostra o nome de uma filial. O CNPJ mascarado continua certo.
- **[Rollback]** → Um agendamento novo, gravado com a raiz, deixaria de achar notas com o código de antes, que compara por
  igualdade com o CNPJ completo. A volta seria regravar a empresa dele pela tela.

## Migration Plan

- **Banco:** sem migração. Os agendamentos e as execuções gravados ficam como estão.
- **Deploy:** o front e o back sobem juntos, como sempre. Um front antigo contra o back novo mostraria a raiz sem máscara,
  e só isso.
- **Rollback:** reverter o código (Risks).
