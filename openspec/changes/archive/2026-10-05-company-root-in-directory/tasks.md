A ordem vai da regra para a borda:

- **Grupo 1:** a raiz e o prefixo, no Domain.
- **Grupos 2 e 3:** o diretório e a descoberta do D365.
- **Grupo 4:** o par de desenvolvimento.
- **Grupo 5:** a tela.
- **Grupo 6:** os docs.
- **Grupo 7:** a prova contra o fiscosysdev.

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. O grupo da tela termina com o
`npm test` e o `npm run build`. Uma tarefa só recebe `[x]` com evidência.

**Revisão de 2026-10-05, antes do arquivamento.** Os grupos 1 a 7 fizeram a empresa do diretório ser a raiz do CNPJ, com
a comparação por prefixo. A revisão volta a empresa para o CNPJ completo, na tela e no armazenamento, e muda só a
comparação, que passa a ser pela raiz (design, D1 e D2). O grupo 8 é a revisão. As tarefas dos grupos 1 a 7 que ela
substituiu continuam marcadas, porque foram feitas, e cada uma diz por qual tarefa do grupo 8 foi substituída.

## 1. A raiz e o "é desta empresa" (D1)

- [x] 1.1 Teste primeiro, na `FiscalHub.Application.Tests`, que alcança o Domain:
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

  **Substituída pela 8.1:** o `IsOfCompany` deu lugar ao `IsSameCompany`. Os testes do `Root` continuam.
- [x] 1.2 Implementar o `Root` e o `IsOfCompany` no `TaxIdentifiers`. **Substituída pela 8.2.**
- [x] 1.3 O `GoodsInvoiceMetadataExtractor.FromIssuer` usa o `Root`. Os testes de hoje do caminho de XML passam sem
  mudança, e provam que o resultado é o mesmo.
- [x] 1.4 `dotnet build` com 0 warnings e `dotnet test` verde.

## 2. O diretório do D365 pela raiz (D2; `company-directory`)

- [x] 2.1 Teste primeiro, no `D365CompanyDirectoryTests`, com as fixtures gravadas da `brmf`:
  - **uma empresa só:** `44278225`, com o nome da `Matriz` ("Contoso Entertainment System Brazil");
  - **as filiais da raiz:** `Matriz`, `RJ-01`, `SAL-01` e `SP-01`, nessa ordem;
  - **o CNPJ completo:** as filiais de `44278225000260` são só a `SP-01`;
  - **duas raízes:** com um estabelecimento de outra raiz no cadastro, são duas empresas, na ordem do código;
  - **o alfanumérico:** `12.ABC.345/01DE-35` dá a empresa `12ABC345`;
  - **a empresa fora do cadastro:** dá filial nenhuma, sem falha.

  Os testes de hoje que afirmavam "quatro empresas com uma filial cada" e "a filial de cada empresa é o estabelecimento
  dela" mudam para a regra nova, e cada um diz por quê. Os de falha (403, 5xx, throttling, credencial, settings) não mudam.

  **Substituída pela 8.3:** a empresa é o CNPJ da matriz, e não a raiz.
- [x] 2.2 Implementar o agrupamento pela raiz e as filiais por prefixo, e atualizar o comentário do tipo. **Substituída
  pela 8.3.**
- [x] 2.3 `dotnet build` com 0 warnings e `dotnet test` verde.

## 3. A descoberta do D365 por prefixo (D3; `period-discovery`)

- [x] 3.1 Teste primeiro, no `D365DocumentDiscoveryTests`:
  - **a raiz sem filial:** o escopo tem os quatro estabelecimentos da `brmf`, e o filtro do F&O leva os quatro pares
    (`dataAreaId`, código);
  - **a empresa inteira:** com a raiz, "todas as filiais", e uma resposta com notas da `Matriz` e da `SP-01`, todas entram.
    As notas são as NFS-e gravadas da `SP-01` e uma derivada delas, lançada na `Matriz`: as notas gravadas da `Matriz` e da
    `SAL-01` nos períodos de `directory/` são modelo `01`, fora do mapa, e o mapeamento as tiraria antes da guarda;
  - **a raiz com filial:** `44278225` com `SP-01` leva só o par da `SP-01`;
  - **o agendamento antigo:** `44278225000260`, sem filial, leva só o par da `SP-01`, e a nota da `SP-01` entra;
  - **a guarda:** com a raiz, uma nota da `SP-01` com o CNPJ de outra raiz no cabeçalho fica fora, com o log;
  - **a raiz alfanumérica:** `12ABC345` acha o estabelecimento `12.ABC.345/01DE-35`.

  O teste de hoje `Note_of_the_establishment_with_another_cnpj_stays_out` continua passando, agora pelo prefixo.

  **Substituída pela 8.4:** a empresa pedida é o CNPJ da matriz, e a comparação é pela raiz.
- [x] 3.2 Implementar o escopo e a guarda pelo `IsOfCompany`, e o texto do log da guarda ("não é da empresa").
  **Substituída pela 8.4.**
- [x] 3.3 `dotnet build` com 0 warnings e `dotnet test` verde.

## 4. O par de desenvolvimento, conferido (D5)

- [x] 4.1 Conferir que o `JsonCompanyDirectory` e a `LocalDocumentDiscovery` não mudam: o `companies.json` tem raízes de 8
  caracteres, e o catálogo local guarda a raiz.
- [x] 4.2 Um teste que prova o contrato: a empresa que o diretório de exemplo lista (`12345678`) é a que a descoberta local
  aceita, e traz a nota de exemplo dela. Se um teste de hoje já prova isso, a tarefa é apontá-lo.

## 5. Um formatador só, para a raiz e para o CNPJ completo (D6; `document-grouping`, `company-directory`)

Revisto em 2026-10-05: um `formatCompany` só, mascarando 8 e 14 caracteres, no lugar de um segundo formatador. Continua
valendo depois do grupo 8: o dropdown do D365 passa a mostrar o CNPJ completo, e a máscara de 8 fica para as notas em XML
e o diretório de exemplo.

- [x] 5.1 Teste primeiro, no `companyCode.test.ts`, do `formatCompany`:
  - `44278225` dá `44.278.225`;
  - `12ABC345` dá `12.ABC.345`;
  - `12345678`, a empresa do caminho de XML, dá `12.345.678`. Este teste substitui o de hoje, que esperava `12345678`;
  - `44278225000260` dá `44.278.225/0002-60`, e `12ABC34501DE35` dá `12.ABC.345/01DE-35`, como hoje;
  - outro tamanho (`B01`) fica como veio.
- [x] 5.2 Implementar no `formatCompany`. Ele já é o formatador do dropdown, das duas tabelas da `IntegrationsPage`, dos
  cards e do modal: nenhum chamador muda. Conferir que nenhum teste da tela fixa a empresa de 8 caracteres sem máscara.
- [x] 5.3 `npm test` e `npm run build` verdes.

## 6. Os docs (D7, D8)

Os três itens foram reescritos na 8.7.

- [x] 6.1 O ADR-0034 (`docs/adr/0034-empresa-do-diretorio-e-a-raiz-do-cnpj.md`, pelo `0000-template.md`), com o que o D7
  lista. Também:
  - a linha "Revisado por" no ADR-0032 §1;
  - a linha do índice em `docs/adr/README.md`, e o 0032 marcado como revisado pelo 0034.
- [x] 6.2 O RUNNING, §7, "A integração manual e o agendamento contra o D365":
  - **a tabela da `brmf`:** passa a mostrar uma empresa, `44.278.225`, com as quatro filiais;
  - **"Todas as filiais":** passa a significar a empresa inteira;
  - **o agendamento antigo:** com o CNPJ completo, continua achando só o estabelecimento dele.
- [x] 6.3 O STATUS, um item novo: a divergência do `CompanyCode` entre as origens (D8). No XML ele é a raiz, com a filial
  como a ordem (`0001`); no D365, o CNPJ completo do estabelecimento, com a filial como o código (`Matriz`). O item tem
  Falta, Prova e Sintoma, como os outros, e cita esta change.

## 7. A prova contra o fiscosysdev

A prova da primeira versão, com a empresa como a raiz. A da revisão é a 8.10.

- [x] 7.1 Com o host local e o perfil do tenant-a (a credencial do D365 do próprio tenant, e não o Azure CLI):
  - **o dropdown "Empresa":** lista uma empresa, `44.278.225`;
  - **o dropdown "Filial":** com ela escolhida, lista `Matriz`, `RJ-01`, `SAL-01` e `SP-01`;
  - **"Todas as filiais":** uma integração manual de 2015-01-01 a 2026-08-31, que pega as notas da `brmf` de modelo do
    mapa, descobre notas de mais de um estabelecimento: as NF-e 55 da `Matriz` e as NFS-e da `SP-01` (e da `SAL-01`, se
    houver). Anotar quantas de cada um. As notas modelo `01` (como as de 2016-02-05 e 2017-01-15) ficam fora do mapa, e não
    entram em nenhum período;
  - **o agendamento antigo:** pela API da integração manual com a empresa `44278225000260`, a descoberta traz só notas
    da `SP-01`. É o mesmo caminho do agendamento gravado, com o mesmo critério;
  - **o card de uma nota em XML:** mostra a empresa como `12.345.678`, e não mais `12345678`. Abrir o grupo continua
    listando as notas dele. Sem nota em XML na base, anotar que a prova do card é o teste do 5.1.

  A guarda do `CompanyCode` não se provoca no fiscosysdev, porque nenhum estabelecimento mudou de CNPJ. A prova dela é a do
  3.1, e isso fica anotado aqui.

  **A saída durante a prova.** A integração manual publica na fila de descoberta, e as notas descobertas seguem para o
  destino ativo do tenant-a, que hoje é o sandbox real da Avalara. Para a prova não mandar nada à plataforma real, a saída
  vai para o mock enquanto ela dura, pelo mesmo caminho da 6.1 da `platform-establishment-resolution`: o `PUT /connector`
  com as settings inteiras, o mock na seção `production`, e o perfil restaurado e comparado no fim.

  **Feito (2026-10-05), com o host desta branch e o mock locais.**
  - **A identidade no F&O:** o log diz "D365: o tenant tenant-a autentica no F&O com a credencial do próprio tenant (client
    credentials: app 88e5c98e-…)", e não o Azure CLI.
  - **A saída no mock:** o `PUT /connector` levou as settings inteiras. A seção `production` apontou para o mock, com a
    tabela `establishments` copiada do sandbox (o código desta branch, o do `main`, a exige), e sem segredo no corpo. No fim,
    o perfil foi restaurado, e o `GET` comparado com o de antes não teve nenhuma diferença. O log do host tem 0 requisições
    ao sandbox real e nenhuma falha.
  - **O dropdown "Empresa":** `GET /companies` devolveu `[{"code":"44278225","name":"Contoso Entertainment System
    Brazil"}]`, uma empresa só. A tela a mostra pelo `formatCompany` como `44.278.225` (o teste do 5.1).
  - **O dropdown "Filial":** `GET /companies/44278225/branches` devolveu `Matriz`, `RJ-01`, `SAL-01` e `SP-01`. O código antigo,
    `GET /companies/44278225000260/branches`, devolveu só a `SP-01`.
  - **"Todas as filiais":** a integração manual da empresa `44278225`, de 2015-01-01 a 2026-08-31, descobriu 14 notas de dois
    estabelecimentos:
    - a `Matriz`, com 7: as 5 NF-e 55 (`Confirmed`) e 2 NFS-e (`Ignored`). **Corrigido na 8.10:** as 5 NF-e foram
      reenviadas ao mock, e não deixadas pela idempotência. A integração imediata usa o gatilho manual, que fura a
      idempotência de propósito (ADR-0015, `DocumentPipeline`), e o log do mock da 7.1 tem 5 `POST` de nota. Nada foi ao
      sandbox real nesta prova.

      **A rodada das 13:04 (2026-10-05), no host local do usuário.** Foi uma integração imediata da raiz `44278225`, de
      2016-01-01 a 2018-12-01, com o perfil no sandbox. Pelo gatilho manual, ela provavelmente reenviou ao sandbox real
      as NF-e de 2016 do período. Segundo o usuário, essas notas já tinham ido antes e voltado com `IntegrationError` de
      validação, e o reenvio gerou mais rejeições de validação no sandbox. Não precisa de ação. A execução dela, a 14,
      gravada com a raiz, foi apagada do banco de dev, porque nada no código de hoje produz esse valor;
    - a `SP-01`, com 7 NFS-e (`Ignored`).

    A `SAL-01` não entrou: as notas dela são modelo `01`, fora do mapa.
  - **O agendamento antigo:** a integração manual com a empresa `44278225000260`, sem filial, descobriu 7 notas, todas da
    `SP-01`.
  - **O card de XML:** não há nota em XML no banco de dev (nenhum `CompanyCode` de 8 caracteres). Criar uma pelo `/ingest`
    misturaria as duas origens no tenant-a, que é o caso do item novo do STATUS. A prova do card é o teste do 5.1
    (`formatCompany('12345678')` dá `12.345.678`); os cards e o modal usam o `formatCompany` (`GroupsPage.tsx:50` e
    `GroupModal.tsx:42`).
  - **No fim,** o host e o mock foram parados, e a solução inteira compilou com 0 warnings e passou nos testes.

## 8. A revisão: a empresa é o CNPJ completo, e a comparação é pela raiz (D1 a D8)

Pedida em 2026-10-05. Antes dela, foi verificado no banco de dev:

- o único agendamento gravado é `44278225000180` com a filial `Matriz`;
- nenhum tem o CNPJ completo e a filial nula;
- não há cliente em produção.

- [x] 8.1 Teste primeiro, no `TaxIdentifiersTests`, do `IsSameCompany`:
  - os quatro CNPJs da `brmf` são da mesma empresa que a matriz, nos dois sentidos;
  - duas filiais da mesma raiz são da mesma empresa;
  - a raiz sozinha, a empresa do caminho de XML, é da mesma empresa que os estabelecimentos dela;
  - outra raiz é outra empresa;
  - `12ABC34501DE35` e `12ABC34500XY12` são da mesma empresa, e `12abc34501de35` não (a caixa não é convertida);
  - um lado vazio não casa com nada.
- [x] 8.2 Trocar o `IsOfCompany` pelo `IsSameCompany` no `TaxIdentifiers`. Nenhuma chamada ao `IsOfCompany` sobra no
  código.
- [x] 8.3 O diretório (D2). Teste primeiro, no `D365CompanyDirectoryTests`:
  - **a `brmf`:** uma empresa, `44278225000180`, com o nome da `Matriz`;
  - **as filiais:** as quatro, cada uma com o CNPJ dela (`Branch.TaxId`);
  - **qualquer CNPJ da raiz:** os quatro trazem as quatro filiais;
  - **sem a ordem `0001`:** a empresa é a de menor ordem, `44278225000260`, "Filial de serviços", e não vazia;
  - **duas raízes:** `12345678000190` e `44278225000180`, nessa ordem;
  - **o mesmo CNPJ em dois estabelecimentos:** o desempate é o menor código;
  - **a empresa vazia:** filial nenhuma;
  - **o alfanumérico:** `12ABC34501DE35`.

  Depois, o representante e as filiais pela raiz, com o CNPJ, no `D365CompanyDirectory`.
- [x] 8.4 A descoberta (D3). Teste primeiro, no `D365DocumentDiscoveryTests`, com a empresa `44278225000180`:
  - **sem filial:** o escopo tem os quatro pares, e cinco com mais um estabelecimento no CNPJ da `SP-01`;
  - **a empresa inteira:** as notas da `Matriz` e da `SP-01` entram;
  - **com a `SP-01`:** só o par dela;
  - **o agendamento gravado** (`44278225000180` com a `Matriz`): só o par da `Matriz`;
  - **o CNPJ de uma filial, sem filial:** a empresa inteira;
  - **a guarda:** a nota com o CNPJ de outra raiz fica fora, com o log "não é da empresa 44278225000180";
  - **outro CNPJ da mesma raiz:** a nota entra. Era o teste `Note_of_the_establishment_with_another_cnpj_stays_out`, que
    mudou de sentido;
  - **o alfanumérico:** `12ABC34501DE35` acha o estabelecimento dele.

  Depois, o escopo e a guarda pelo `IsSameCompany`.
- [x] 8.5 O par de desenvolvimento continua em raízes (D5), e não muda. Os testes do 4.2 passam sem mudança (os 11 da
  `Discovery.Local.Tests` e os 3 da `Directory.Json.Tests`).
- [x] 8.6 A tela (D6):
  - o `Branch` ganha o `taxId`, opcional (no `ICompanyDirectory` e no `types.ts`);
  - o `formatBranch`, com teste: "44.278.225/0002-60 — SP-01", o alfanumérico, e "0001 — Matriz" sem o CNPJ;
  - o dropdown "Filial" usa o `formatBranch`, e o `formatCompany` não muda;
  - `npm test` (45 testes) e `npm run build` verdes.
- [x] 8.7 Os docs:
  - o ADR-0034, reescrito e renomeado para `docs/adr/0034-a-empresa-se-compara-pela-raiz-do-cnpj.md`;
  - a linha "Revisado por" e a nota do §1 no ADR-0032, e a linha do índice;
  - o RUNNING, §7: a empresa e as filiais da `brmf` como aparecem, e o que se grava;
  - o item do STATUS: por que a divergência é tolerável, com a comparação pela raiz;
  - um item novo no STATUS: a coluna Empresa das tabelas de agendamento e de execução mostra o CNPJ da matriz, com Falta,
    Prova e Sintoma. A tratativa é o CNPJ do estabelecimento só na execução, numa coluna anulável preenchida na
    descoberta, e fica para a próxima correção pequena. A filial nas tabelas continua pelo código. As duas decisões são
    de 2026-10-05.
- [x] 8.8 Os artefatos: o proposal, as três specs e o design revistos, e o `openspec validate --strict` sem erro.
- [x] 8.9 `dotnet clean` e `dotnet build` da solução inteira com 0 warnings e 0 erros, e o `dotnet test` da solução sobre
  esse build: os 10 projetos de teste passam. Foi feito depois de parar o host local do usuário, que travava o `bin` do
  Host. Antes disso, o Host tinha sido compilado numa saída separada, que não vale como a evidência.
- [x] 8.10 A prova contra o fiscosysdev, com o host desta branch, o perfil do tenant-a e a saída no mock, pelo mesmo
  caminho da 7.1:
  - **o dropdown "Empresa":** uma empresa, `44.278.225/0001-80`;
  - **o dropdown "Filial":** as quatro, com o CNPJ e o código ("44.278.225/0002-60 — SP-01");
  - **"Todas as filiais":** a empresa `44278225000180` traz notas de mais de um estabelecimento. No fiscosysdev, só a
    `Matriz` e a `SP-01` têm notas de modelo do mapa: a `SAL-01` só tem modelo `01`, e o `RJ-01` não tem nota. A prova
    das quatro é o escopo, com os quatro pares;
  - **com a filial `SP-01`:** só notas da `SP-01`;
  - **o agendamento gravado** (`44278225000180` com a `Matriz`): as mesmas 7 notas da 7.1;
  - **a guarda:** não se provoca no fiscosysdev, e a prova é a da 8.4;
  - **sem a ordem `0001`:** não se provoca no fiscosysdev, e a prova é a da 8.3.

  **Feito (2026-10-05), depois do build limpo da 8.9, com o host desta branch (`--no-build`, sobre aquele build) e o mock
  locais.** O host local do usuário foi parado antes, a pedido dele.
  - **A identidade no F&O:** o log diz "D365: o tenant tenant-a autentica no F&O com a credencial do próprio tenant (client
    credentials: app 88e5c98e-…)".
  - **A saída no mock:** o `PUT /connector` levou as settings inteiras do `GET`, com a seção `production` no mock e a tabela
    `establishments` do sandbox, sem segredo no corpo. No fim, as notas reenviadas já estavam `Confirmed` pelo mock (nada
    `Submitted`, para o poll de status não consultar o sandbox real com ids do mock). Só então o perfil foi restaurado, e
    o `GET` comparado com o de antes não teve nenhuma diferença. O log do host tem 0 requisições ao sandbox real e nenhuma
    falha.
  - **O dropdown "Empresa":** `GET /companies` devolveu `[{"code":"44278225000180","name":"Contoso Entertainment System
    Brazil"}]`, uma empresa só. A tela a mostra como `44.278.225/0001-80` pelo `formatCompany` (o teste do 5.1).
  - **O dropdown "Filial":** `GET /companies/44278225000180/branches` devolveu `Matriz`, `RJ-01`, `SAL-01` e `SP-01`, cada
    uma com o `taxId` (`44278225000180`, `44278225003448`, `44278225000341` e `44278225000260`). A tela as mostra pelo
    `formatBranch`, como "44.278.225/0002-60 — SP-01" (o teste da 8.6). Com o CNPJ da `SP-01`,
    `GET /companies/44278225000260/branches` devolveu as mesmas quatro.
  - **"Todas as filiais"** (execução 15): a empresa `44278225000180`, de 2015-01-01 a 2026-08-31, descobriu 14 notas, o
    mesmo total da 7.1:
    - a `Matriz`, com 7: 5 NF-e 55 (`Confirmed`) e 2 NFS-e (`Ignored`);
    - a `SP-01`, com 7 NFS-e (`Ignored`).

    O log do host esconde a query string do OData (`?*`), então o filtro com os quatro pares não aparece. A prova dos
    quatro pares é a da 8.4.
  - **Com a filial `SP-01`** (execução 16): 7 notas, todas da `SP-01`. A execução ficou gravada como `44278225000180` com
    a `SP-01`, que é o caso do item novo do STATUS (a coluna Empresa mostra o CNPJ da matriz).
  - **O agendamento gravado:** ele é o `ScheduledOnce` 2, de 2016-09-01 a 2016-10-02, inativo. As três execuções dele no
    banco (11, 12 e 13, de 2026-10-05) descobriram 1 nota cada. A integração imediata com o mesmo critério (execução 17)
    também descobriu 1: a `brmf|BRMF06-110000027`, NF-e 55 da `Matriz` de 2016-09-02. As "7 notas" são as da `Matriz` no
    período da 7.1: com a `Matriz`, de 2015-01-01 a 2026-08-31 (execução 18), vieram as mesmas 7, 5 NF-e 55 e 2 NFS-e.
    A descoberta é a mesma do agendamento. O envio não: a agendada não reenviaria a nota já confirmada.
  - **Os reenvios:** a integração imediata usa o gatilho manual, que fura a idempotência (ADR-0015). As NF-e 55 foram
    reenviadas ao mock: 5 na execução 15, 1 na 17 e 5 na 18, que são os 11 `POST` do log do mock. As NFS-e não, porque o
    roteamento as ignora.
  - **No fim,** o host e o mock foram parados, e as portas 5100 e 5200 ficaram livres.
