## Why

A change `erp-company-directory-and-card-filters` (ADR-0032 §1) fez a empresa do diretório ser o CNPJ completo do
estabelecimento, "a mesma chave dos grupos". Só que empresa e estabelecimento são coisas diferentes. Os quatro
estabelecimentos da `brmf` têm a raiz `44278225`: são filiais da **mesma** empresa, nas ordens `0001`, `0002`, `0003` e
`0034`.

O defeito tem duas faces:

- **Na tela:** o dropdown "Empresa" lista quatro empresas onde existe uma.
- **No funcionamento:** o `ListBranchesAsync` filtra por igualdade do CNPJ, então cada "empresa" tem sempre uma filial, a
  dela mesma. A opção "Todas as filiais" existe, e nunca pode significar mais de uma. A integração manual e a agendada não
  conseguem pedir a empresa inteira.

O que estava errado era a **comparação**, e não o valor. O CNPJ completo continua sendo o que a tela mostra e o que se
grava. Só que "é desta empresa" passa a ser "tem a mesma raiz", e não "tem o mesmo CNPJ".

## What Changes

- **Uma regra só para "é da mesma empresa":** `IsSameCompany(a, b)`, verdadeiro quando os dois CNPJs normalizados têm a
  mesma raiz. A raiz são os 8 primeiros caracteres, como texto: nunca número, e nunca "8 dígitos", porque o CNPJ
  alfanumérico tem raiz alfanumérica (`12ABC345`). A empresa vazia não casa com nada. A regra vale em três lugares:
  - **o diretório:** as filiais de uma empresa;
  - **a descoberta, no escopo:** os estabelecimentos pedidos;
  - **a descoberta, na guarda:** o CNPJ que a nota traz.
- **A empresa do diretório do D365 continua sendo um CNPJ completo:** uma por raiz, com o código e o nome do
  estabelecimento que a representa.
  - O representante é a matriz, de ordem `0001`.
  - Sem a ordem `0001` no cadastro, é o de menor ordem presente.
  - No empate, ganha o de menor código.

  A empresa nunca aparece vazia, nem como a raiz sozinha.
- **As filiais vêm com o CNPJ,** e o dropdown as mostra como "44.278.225/0002-60 — SP-01". O valor da opção continua
  sendo o código do estabelecimento (`FiscalEstablishmentId`).
- **Nenhum rótulo de empresa difere do valor gravado.** A empresa que a tela mostra é a que a integração e o agendamento
  gravam, só mascarada.
- **A guarda da descoberta passa a deixar fora só o CNPJ de outra raiz.** Uma nota com outro CNPJ da mesma raiz entra,
  porque é da empresa pedida. Antes, a igualdade a tirava.
- **Sem migração.** O único agendamento gravado no banco de dev (2026-10-05) é `44278225000180` com a filial `Matriz`, e
  ele continua trazendo só as notas da `Matriz`. Não há agendamento com o CNPJ de uma filial e a filial "todas", que seria
  o único caso a mudar de significado: passaria a ser a empresa inteira. Também não há cliente em produção.
- **O diretório de exemplo (Development) continua em raízes:** o `companies.json` e o catálogo da descoberta local usam
  códigos de 8 caracteres, e a empresa que ele lista é a que a descoberta local aceita. Lá também o rótulo é o valor
  gravado. Os dois só são conferidos, e não mudam.
- **Um formatador só mascara a raiz e o CNPJ completo,** pelo tamanho: 8 caracteres viram `44.278.225`, e 14 continuam
  `44.278.225/0001-80`. A máscara de 8 serve às notas em XML, cuja empresa é a raiz, e ao diretório de exemplo. Ele vale
  no dropdown, nas tabelas da tela de integrações, nos cards e no modal.
- **A divergência do `CompanyCode` entre as origens vai para o STATUS.** No caminho de XML ele é a raiz, e no D365 é o
  CNPJ completo do estabelecimento: mesma coluna, dois conceitos. Esta change não a piora, e não a resolve. A comparação
  pela raiz, porém, já trata os dois tamanhos como a mesma empresa.
- **ADR-0034.** Registra a correção, e o ADR-0032 §1 ganha a linha "Revisado por".

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `company-directory`: dois requisitos mudam:
  - **o D365 lista os estabelecimentos:** uma empresa por raiz, com o CNPJ da matriz (ou o de menor ordem), e as filiais
    da raiz, com o CNPJ delas;
  - **os dropdowns:** a filial aparece pelo CNPJ com o código ao lado, e o rótulo da empresa é o valor gravado.
- `period-discovery`: no requisito do dia fiscal e do estabelecimento, a empresa casa pela raiz, no escopo e na guarda.
- `document-grouping`: o "CNPJ formatado na tela" passa a mascarar também a raiz, de 8 caracteres. A empresa do XML deixa
  de aparecer como está.

## Non-goals

- **O agrupamento dos cards e do modal.** Eles agrupam por estabelecimento, com o CNPJ completo, e isso está certo: a
  identidade fiscal de uma nota é o estabelecimento que a emitiu. Neles, só a máscara muda, e só para a empresa de 8
  caracteres.
- **O `CompanyCode` gravado no documento.** Continua sendo o CNPJ completo do estabelecimento. A empresa é comparada pela
  raiz, e não armazenada.
- **Converter os agendamentos gravados.** O único que existe continua certo.
- **Unificar o `CompanyCode` das origens.** O caminho de XML deriva a empresa como raiz e a filial como a ordem
  (`GoodsInvoiceMetadataExtractor.FromIssuer`), e o D365 grava o CNPJ completo e o código do estabelecimento. A divergência
  é de antes, e fica registrada no STATUS com Falta, Prova e Sintoma.
- **O CNPJ da filial nas tabelas.** As tabelas de agendamentos e de execuções mostram a filial pelo código gravado. O CNPJ
  dela não é gravado, e mostrá-lo exigiria consultar o diretório por linha, o que faria o histórico depender do ERP no
  ar. A consequência é uma regressão de leitura: a coluna Empresa de uma linha que não é da matriz mostra o CNPJ da
  matriz, e não o do estabelecimento daquelas notas. Ela fica no STATUS, com a tratativa, para a próxima correção
  pequena: gravar o CNPJ do estabelecimento só na execução, numa coluna anulável, quando o escopo da descoberta resolveu
  um estabelecimento só. No agendamento, não: execuções registram fato, e agendamentos, critério.
- **Conferir o tamanho da empresa pedida.**

## Impact

- **Domain:** a raiz e o "é da mesma empresa" (`Root` e `IsSameCompany`), no `TaxIdentifiers`, ao lado da normalização.
- **Application:**
  - o `GoodsInvoiceMetadataExtractor.FromIssuer` passa a usar a mesma raiz, com o mesmo resultado de hoje;
  - o `Branch` do diretório ganha o `TaxId`, opcional.
- **Adapters (`Ingress.D365Poll`):**
  - o `D365CompanyDirectory`: o agrupamento pela raiz, o representante, e as filiais pela raiz, com o CNPJ;
  - o `D365DocumentDiscovery`: o escopo e a guarda pela raiz, e o texto do log da guarda.
- **Adapters (`Directory.Json`, `Discovery.Local`):** só conferidos. Um teste prova que a empresa do diretório de exemplo
  é aceita pela descoberta local.
- **Dashboard:**
  - o `formatCompany` passa a mascarar também a raiz, com teste;
  - um `formatBranch` monta o rótulo da filial, com teste;
  - o `Branch` ganha o `taxId`.
- **Testes:**
  - o diretório e a descoberta do D365, com as fixtures gravadas da `brmf` (`directory/`);
  - os formatadores, no `vitest`.
- **Docs:**
  - o ADR-0034, e a linha de revisão no ADR-0032 e no índice;
  - o RUNNING (§7, a tabela dos estabelecimentos da `brmf`);
  - o STATUS: a divergência do `CompanyCode` entre as origens, e a coluna Empresa das tabelas com o CNPJ da matriz.
- **Sem migração e sem mudança no D365.** O contrato da API só ganha o campo `taxId`, opcional, nas filiais.
