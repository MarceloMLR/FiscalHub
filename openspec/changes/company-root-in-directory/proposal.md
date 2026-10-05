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

## What Changes

- **A empresa do diretório do D365 passa a ser a raiz do CNPJ:** os 8 primeiros caracteres do CNPJ normalizado, como
  texto. Nunca número, e nunca "8 dígitos": o CNPJ alfanumérico tem raiz alfanumérica (`12ABC345`).
  - **O nome da empresa:** continua sendo o do estabelecimento de menor código daquela raiz.
  - **A filial:** continua sendo o código do estabelecimento (`FiscalEstablishmentId`).
- **As filiais de uma empresa** passam a ser as dos estabelecimentos cujo CNPJ **começa** com o código pedido, e não os de
  CNPJ igual. O `DistinctBy` por código continua.
- **A descoberta por período do D365 casa a empresa por prefixo,** nos dois lugares:
  - **o escopo dos estabelecimentos;**
  - **a guarda do documento:** a intenção continua, que é deixar fora a nota de um estabelecimento que mudou de CNPJ. O
    que muda é que a nota de outra filial da mesma empresa passa a entrar, que é o certo quando se pede a empresa inteira.
- **Sem migração.** Um agendamento já gravado com o CNPJ de 14 caracteres continua casando, por prefixo, com exatamente
  aquele estabelecimento. Um valor novo, de 8 caracteres, casa com todos os da raiz.
- **O diretório de exemplo (Development) já é raiz:** o `companies.json` e o catálogo da descoberta local usam códigos de
  8 caracteres. Os dois só são conferidos, e não mudam.
- **A tela de integrações mostra a raiz mascarada** (`44.278.225`) no dropdown e nas duas tabelas dela. O formatador dos
  cards e do modal não muda.
- **ADR-0034.** Registra a correção, e o ADR-0032 §1 ganha a linha "Revisado por".

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `company-directory`: dois requisitos mudam:
  - **o D365 lista os estabelecimentos:** a empresa é a raiz do CNPJ, e as filiais são as da raiz, por prefixo;
  - **os dropdowns:** a empresa aparece com a raiz mascarada.
- `period-discovery`: no requisito do dia fiscal e do estabelecimento, a empresa casa por prefixo do CNPJ, no escopo e na
  guarda.

## Non-goals

- **Os cards e o modal.** Eles agrupam por estabelecimento, com o CNPJ completo, e isso está certo: a identidade fiscal de
  uma nota é o estabelecimento que a emitiu. O dropdown e os cards são caminhos separados.
- **O `CompanyCode` gravado no documento.** Continua sendo o CNPJ completo do estabelecimento. A empresa é derivada por
  prefixo, e não armazenada.
- **Converter os agendamentos gravados.** O prefixo os mantém funcionando como estão.
- **A empresa do caminho de XML.** Ele já deriva a empresa como raiz e a filial como a ordem
  (`GoodsInvoiceMetadataExtractor.FromIssuer`), uma diferença de antes em relação aos grupos do D365. Ela fica como está.
- **Mascarar a raiz nos cards.** Os cards das notas em XML já mostram a raiz sem máscara, e mexer no formatador deles é
  mexer nos cards.
- **Conferir o tamanho da empresa pedida.** O que chega é o que o diretório deu (8) ou o que já estava gravado (14).

## Impact

- **Domain:** a raiz e o "o estabelecimento é desta empresa" (prefixo), no `TaxIdentifiers`, ao lado da normalização.
- **Application:** o `GoodsInvoiceMetadataExtractor.FromIssuer` passa a usar a mesma raiz, com o mesmo resultado de hoje.
- **Adapters (`Ingress.D365Poll`):**
  - o `D365CompanyDirectory`: o agrupamento pela raiz, e as filiais por prefixo;
  - o `D365DocumentDiscovery`: o escopo e a guarda por prefixo, e o texto do log da guarda.
- **Adapters (`Directory.Json`, `Discovery.Local`):** só conferidos. Um teste prova que a empresa do diretório de exemplo
  é aceita pela descoberta local.
- **Dashboard:** a máscara da raiz na tela de integrações (`IntegrationsPage`), com teste.
- **Testes:**
  - o diretório e a descoberta do D365, com as fixtures gravadas da `brmf` (`directory/`);
  - o formatador da tela de integrações, no `vitest`.
- **Docs:** o ADR-0034, a linha de revisão no ADR-0032 e no índice, e o RUNNING (§7, a tabela dos estabelecimentos da
  `brmf`).
- **Sem migração, sem mudança de contrato da API e sem mudança no D365.**
