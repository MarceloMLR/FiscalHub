A ordem vai da regra para a borda:

- **Grupo 1:** a raiz e o prefixo, no Domain.
- **Grupos 2 e 3:** o diretório e a descoberta do D365.
- **Grupo 4:** o par de desenvolvimento.
- **Grupo 5:** a tela.
- **Grupo 6:** os docs.
- **Grupo 7:** a prova contra o fiscosysdev.

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. O grupo da tela termina com o
`npm test` e o `npm run build`. Uma tarefa só recebe `[x]` com evidência.

## 1. A raiz e o "é desta empresa" (D1)

- [ ] 1.1 Teste primeiro, na `FiscalHub.Application.Tests`, que alcança o Domain:
  - **`Root`:**
    - `44278225000260` dá `44278225`;
    - `12ABC34501DE35` dá `12ABC345`, com as letras;
    - um valor com menos de 8 caracteres volta inteiro.
  - **`IsOfCompany`:**
    - a raiz `44278225` casa com os quatro CNPJs da `brmf`;
    - o CNPJ completo `44278225000260` casa só com ele, e não com `44278225000180`;
    - `12345678` não casa com `44278225000180`;
    - `12ABC345` casa com `12ABC34501DE35`, e `12abc345` não casa (a caixa não é convertida);
    - a empresa vazia não casa com nada.
- [ ] 1.2 Implementar o `Root` e o `IsOfCompany` no `TaxIdentifiers`.
- [ ] 1.3 O `GoodsInvoiceMetadataExtractor.FromIssuer` usa o `Root`. Os testes de hoje do caminho de XML passam sem
  mudança, e provam que o resultado é o mesmo.
- [ ] 1.4 `dotnet build` com 0 warnings e `dotnet test` verde.

## 2. O diretório do D365 pela raiz (D2; `company-directory`)

- [ ] 2.1 Teste primeiro, no `D365CompanyDirectoryTests`, com as fixtures gravadas da `brmf`:
  - **uma empresa só:** `44278225`, com o nome da `Matriz` ("Contoso Entertainment System Brazil");
  - **as filiais da raiz:** `Matriz`, `RJ-01`, `SAL-01` e `SP-01`, nessa ordem;
  - **o CNPJ completo:** as filiais de `44278225000260` são só a `SP-01`;
  - **duas raízes:** com um estabelecimento de outra raiz no cadastro, são duas empresas, na ordem do código;
  - **o alfanumérico:** `12.ABC.345/01DE-35` dá a empresa `12ABC345`;
  - **a empresa fora do cadastro:** dá filial nenhuma, sem falha.

  Os testes de hoje que afirmavam "quatro empresas com uma filial cada" e "a filial de cada empresa é o estabelecimento
  dela" mudam para a regra nova, e cada um diz por quê. Os de falha (403, 5xx, throttling, credencial, settings) não mudam.
- [ ] 2.2 Implementar o agrupamento pela raiz e as filiais por prefixo, e atualizar o comentário do tipo.
- [ ] 2.3 `dotnet build` com 0 warnings e `dotnet test` verde.

## 3. A descoberta do D365 por prefixo (D3; `period-discovery`)

- [ ] 3.1 Teste primeiro, no `D365DocumentDiscoveryTests`:
  - **a raiz sem filial:** o escopo tem os quatro estabelecimentos da `brmf`, e o filtro do F&O leva os quatro pares
    (`dataAreaId`, código);
  - **a empresa inteira:** com a raiz, "todas as filiais", e uma resposta com notas da `Matriz` e da `SP-01` (das fixtures
    de período gravadas), as duas notas entram;
  - **a raiz com filial:** `44278225` com `SP-01` leva só o par da `SP-01`;
  - **o agendamento antigo:** `44278225000260`, sem filial, leva só o par da `SP-01`, e a nota da `SP-01` entra;
  - **a guarda:** com a raiz, uma nota da `SP-01` com o CNPJ de outra raiz no cabeçalho fica fora, com o log;
  - **a raiz alfanumérica:** `12ABC345` acha o estabelecimento `12.ABC.345/01DE-35`.

  O teste de hoje `Note_of_the_establishment_with_another_cnpj_stays_out` continua passando, agora pelo prefixo.
- [ ] 3.2 Implementar o escopo e a guarda pelo `IsOfCompany`, e o texto do log da guarda ("não é da empresa").
- [ ] 3.3 `dotnet build` com 0 warnings e `dotnet test` verde.

## 4. O par de desenvolvimento, conferido (D5)

- [ ] 4.1 Conferir que o `JsonCompanyDirectory` e a `LocalDocumentDiscovery` não mudam: o `companies.json` tem raízes de 8
  caracteres, e o catálogo local guarda a raiz.
- [ ] 4.2 Um teste que prova o contrato: a empresa que o diretório de exemplo lista (`12345678`) é a que a descoberta local
  aceita, e traz a nota de exemplo dela. Se um teste de hoje já prova isso, a tarefa é apontá-lo.

## 5. A tela de integrações (D6; `company-directory`)

- [ ] 5.1 Teste primeiro, no `companyCode.test.ts`, do `formatCompanyKey`:
  - `44278225` dá `44.278.225`;
  - `12ABC345` dá `12.ABC.345`;
  - `44278225000260` dá `44.278.225/0002-60`;
  - outro tamanho fica como veio.

  O teste de hoje do `formatCompany` não muda.
- [ ] 5.2 Implementar o `formatCompanyKey`, e usá-lo no dropdown de empresas e nas duas tabelas da `IntegrationsPage`.
  Os cards e o modal continuam com o `formatCompany`.
- [ ] 5.3 `npm test` e `npm run build` verdes.

## 6. Os docs (D7)

- [ ] 6.1 O ADR-0034 (`docs/adr/0034-empresa-do-diretorio-e-a-raiz-do-cnpj.md`, pelo `0000-template.md`), com o que o D7
  lista. Também:
  - a linha "Revisado por" no ADR-0032 §1;
  - a linha do índice em `docs/adr/README.md`, e o 0032 marcado como revisado pelo 0034.
- [ ] 6.2 O RUNNING, §7, "A integração manual e o agendamento contra o D365":
  - **a tabela da `brmf`:** passa a mostrar uma empresa, `44.278.225`, com as quatro filiais;
  - **"Todas as filiais":** passa a significar a empresa inteira;
  - **o agendamento antigo:** com o CNPJ completo, continua achando só o estabelecimento dele.

## 7. A prova contra o fiscosysdev

- [ ] 7.1 Com o host local e o perfil do tenant-a (a credencial do D365 do próprio tenant, e não o Azure CLI):
  - **o dropdown "Empresa":** lista uma empresa, `44.278.225`;
  - **o dropdown "Filial":** com ela escolhida, lista `Matriz`, `RJ-01`, `SAL-01` e `SP-01`;
  - **"Todas as filiais":** uma integração manual de 2016-02-05 a 2017-01-15 descobre notas de mais de um estabelecimento
    (a `SAL-01` em 2016-02-05, a `Matriz` em 2017-01-15, pelas fixtures gravadas). Anotar quantas de cada um;
  - **o agendamento antigo:** pela API da integração manual com a empresa `44278225000260`, a descoberta traz só notas
    da `SP-01`. É o mesmo caminho do agendamento gravado, com o mesmo critério.

  A guarda do `CompanyCode` não se provoca no fiscosysdev, porque nenhum estabelecimento mudou de CNPJ. A prova dela é a do
  3.1, e isso fica anotado aqui.

  **A saída durante a prova.** A integração manual publica na fila de descoberta, e as notas descobertas seguem para o
  destino ativo do tenant-a, que hoje é o sandbox real da Avalara. Para a prova não mandar nada à plataforma real, a saída
  vai para o mock enquanto ela dura, pelo mesmo caminho da 6.1 da `platform-establishment-resolution`: o `PUT /connector`
  com as settings inteiras, o mock na seção `production`, e o perfil restaurado e comparado no fim.
