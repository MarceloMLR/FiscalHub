## Context

A motivação está no proposal.md (Why). Abaixo, o estado do código antes da change, conferido em 2026-10-05.

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
- **Um formatador só já serve a tela toda.** O `formatCompany` é usado pelos cards, pelo modal, pelo dropdown e pelas duas
  tabelas da tela de integrações. Ele só mascara o código de 14 caracteres: a empresa dos cards das notas em XML, que é a
  raiz, aparece sem máscara.
- **Os agendamentos gravados.** No banco de dev, em 2026-10-05, o único é `44278225000180` com a filial `Matriz`. Não há
  nenhum com o CNPJ completo e a filial nula, e não há cliente em produção.
- **Os testes de hoje** afirmam "quatro empresas com uma filial cada" (`D365CompanyDirectoryTests`), e o escopo e a guarda
  por igualdade (`D365DocumentDiscoveryTests`). As fixtures gravadas da `brmf` ficam em `Fixtures/d365/directory/`: o
  cadastro e os períodos da `Matriz` (2017-01-15), da `SP-01` (2026-08-07) e da `SAL-01` (2016-02-05).

## Goals / Non-Goals

**Goals:**

- **Uma regra só para "é da mesma empresa":** num lugar só, usada pelo diretório, pelo escopo e pela guarda. É o desenho
  da normalização do CNPJ, que tirou as cópias espalhadas do "só dígitos".
- **O valor não muda, a comparação muda:** a empresa continua sendo um CNPJ completo na tela e no armazenamento.
- **Nenhum rótulo de empresa difere do valor gravado.**
- **O agendamento gravado continua igual,** sem migração.

**Non-Goals:**

- **Unificar a chave do caminho de XML com a do D365.** É uma diferença de antes, nos grupos, e não nesta correção. Ela
  fica registrada no STATUS (D8), e não só aqui: o design de uma change arquivada não é relido.
- **Validar o tamanho da empresa pedida.**

## Decisions

### D1. A raiz e o "é da mesma empresa", no Domain, ao lado da normalização

```csharp
// FiscalHub.Domain.Goods.TaxIdentifiers
public static string Root(string normalized);         // os 8 primeiros caracteres, ou o todo se tiver menos
public static bool IsSameCompany(string a, string b); // a mesma raiz, ordinal; um lado vazio não casa
```

- **A raiz é texto:** os 8 primeiros caracteres, sem conversão. O CNPJ alfanumérico tem raiz alfanumérica (`12ABC345`), e
  a caixa não é convertida.
- **O "é da mesma empresa" é a igualdade das raízes,** ordinal e simétrica. Qualquer CNPJ da empresa casa com qualquer
  outro dela: o da matriz, o de uma filial, ou a raiz sozinha, a empresa do caminho de XML. Ninguém precisa saber qual
  forma chegou.
- **A empresa vazia não casa com nada.** É o que a igualdade de antes fazia com ela. Sem isso, dois vazios teriam a mesma
  raiz, e um `""` viraria uma empresa.
- **O `FromIssuer` do caminho de XML passa a usar o `Root`,** com o mesmo resultado de hoje. Fica uma definição só de raiz
  no hub.

**Por que no Domain, e não no adapter do D365.** A regra é do CNPJ, e não do F&O. O diretório e a descoberta de outro ERP
vão precisar dela igual, como precisaram da normalização.

**Por que a igualdade das raízes, e não o prefixo.** A primeira versão desta change tinha um `IsOfCompany(cnpj, company)`,
que era "o CNPJ começa com a empresa". Ele só funcionava porque a empresa era a raiz, de 8 caracteres. Com a empresa
gravada como CNPJ completo (D2), o prefixo voltaria a ser a igualdade de antes, e "Todas as filiais" voltaria a significar
uma. A igualdade das raízes não depende do tamanho do que chega.

### D2. O diretório: uma empresa por raiz, com o CNPJ do representante

- **As empresas:** `GroupBy(e => TaxIdentifiers.Root(e.Cnpj))`, na ordem da raiz. Cada grupo dá uma empresa com o código
  e o nome do estabelecimento que o representa:
  - o de ordem `0001`, a matriz;
  - sem a ordem `0001` no cadastro, o de menor ordem presente, com as ordens comparadas como texto;
  - no empate (o mesmo CNPJ em dois estabelecimentos), o de menor código.

  A ordem são os 4 caracteres depois da raiz. Na `brmf`, a empresa é `44278225000180`, "Contoso Entertainment System
  Brazil".
- **Por que o `0001` explícito.** No CNPJ numérico, a menor ordem já é a `0001`, e o "menor" bastaria. A regra escrita diz
  a intenção, que é "a matriz", e não depende de como as letras de uma ordem alfanumérica se comparam aos dígitos.
- **Nunca vazia.** O grupo tem ao menos um estabelecimento, e todo estabelecimento lido tem CNPJ: o sem CNPJ fica fora da
  leitura, com aviso, desde o ADR-0032. A empresa de um cliente sem a matriz no D365 aparece com o CNPJ da filial de
  menor ordem, e o nome dela.
- **As filiais:** `Where(e => TaxIdentifiers.IsSameCompany(e.Cnpj, companyCode))`, com o `DistinctBy` por código de antes.
  Cada filial leva o CNPJ normalizado (`Branch.TaxId`, opcional, porque o diretório de exemplo não o tem).

**A primeira versão desta change gravava a raiz.** O dropdown oferecia `44278225`, o agendamento novo gravava a raiz, e o
antigo, o CNPJ completo. Eram duas formas de empresa convivendo, mascaradas de dois jeitos. A revisão de 2026-10-05 a
desfez:

- **A tela e o armazenamento ficam com uma forma só,** o CNPJ completo, que já era a dos grupos do D365.
- **Nenhum rótulo difere do valor gravado:** o que a tela mostra é o que o agendamento grava.
- **O defeito era a comparação, e não o valor:** consertar só a comparação é a mudança menor.

### D3. A descoberta: o escopo e a guarda pela mesma regra

- **O escopo:** `Where(e => criteria.Company is null || TaxIdentifiers.IsSameCompany(e.Cnpj, criteria.Company))`. Sem
  filial, o escopo tem todos os estabelecimentos da raiz, e o filtro do F&O ganha um par por estabelecimento.
- **A guarda:** `!TaxIdentifiers.IsSameCompany(reference.Metadata.CompanyCode, criteria.Company)`. A nota fica fora quando
  o CNPJ dela é de outra raiz: um estabelecimento que mudou de CNPJ para o de outra empresa. A nota com outro CNPJ da mesma
  raiz entra, porque é da empresa pedida. Antes, a igualdade a tirava.
- **Os logs:** o da guarda passa a dizer que o CNPJ da nota "não é da empresa", e não que é "diferente" dela.

### D4. Sem migração

- **O agendamento gravado** (`44278225000180` com a `Matriz`) tem filial, e o escopo dele continua sendo um par só. Ele
  traz as mesmas notas de antes.
- **O único caso que mudaria de significado** é um agendamento com o CNPJ de uma filial e a filial "todas": antes, só
  aquele estabelecimento; agora, a empresa inteira. Ele não existe no banco de dev (conferido em 2026-10-05), e não há
  cliente em produção.
- **Os novos** gravam o CNPJ que o dropdown oferece, o da matriz.

### D5. O par de desenvolvimento só é conferido

- **O `JsonCompanyDirectory`** devolve as empresas do `companies.json`, que são raízes de 8 caracteres, e as filiais da
  empresa pedida por igualdade do código. As filiais não têm CNPJ, e aparecem com o código e o nome.
- **A `LocalDocumentDiscovery`** compara por igualdade com o catálogo, que guarda a raiz. A empresa que o diretório de
  exemplo lista é a que a descoberta local aceita.
- **Também lá, o rótulo é o valor gravado:** a raiz, mascarada como raiz.
- **Nenhum dos dois muda.** Um teste prova que a empresa do diretório de exemplo é a que a descoberta local aceita.

### D6. Um formatador só, que mascara a raiz e o CNPJ completo

O `formatCompany` mascara pelo tamanho:

- **8 caracteres:** viram `NN.NNN.NNN`. É a empresa das notas em XML e a do diretório de exemplo;
- **14 caracteres:** continuam com a máscara do CNPJ. É a empresa do D365, no dropdown e nos grupos;
- **qualquer outro tamanho:** fica como veio.

Ele continua sendo o formatador de toda a tela: o dropdown, as tabelas da tela de integrações, os cards e o modal. O
`formatBranch` monta o rótulo da filial sobre ele: com o CNPJ, "44.278.225/0002-60 — SP-01"; sem, "0001 — Matriz".

**Um formatador só, e não dois.** Duas funções que formatam a mesma coisa, e diferem só no tamanho que mascaram, são
convite para alguém chamar a errada daqui a seis meses. O efeito nos cards é melhoria: o card de XML passa a mostrar
`12.345.678` em vez de `12345678`.

**O risco conhecido é de antes.** Um código de 8 caracteres que não seja raiz de CNPJ, como o de um ERP que use outra
chave, seria mascarado errado. O de 14 caracteres já tem o mesmo risco desde o ADR-0032, e foi aceito pela convenção do
hub: a empresa vem do CNPJ.

### D7. O ADR-0034

`docs/adr/0034-a-empresa-se-compara-pela-raiz-do-cnpj.md` registra:

- a empresa do diretório como uma por raiz, com o CNPJ completo do representante (D2);
- a regra da mesma raiz, num lugar só (D1);
- o escopo e a guarda da descoberta (D3);
- a ausência de migração (D4);
- o formatador único (D6);
- por que a raiz não virou o valor gravado.

Ele revisa o ADR-0032 §1, "A chave: a empresa é o CNPJ do estabelecimento … É a mesma chave dos grupos, sem tradução". A
chave continua sendo um CNPJ completo. O que muda é a comparação: a mesma raiz, e não o mesmo CNPJ.

### D8. A divergência do `CompanyCode` entre as origens, no STATUS

O `CompanyCode` do registro significa coisas diferentes conforme a origem:

| Origem | Empresa (`CompanyCode`) | Filial (`BranchCode`) |
|---|---|---|
| XML (`GoodsInvoiceMetadataExtractor.FromIssuer`) | a raiz, `cnpj[..8]` | a ordem, `cnpj[8..12]` (`0001`) |
| D365 (`D365HeaderReference`) | o CNPJ completo do estabelecimento | o código do estabelecimento (`Matriz`) |

É a mesma coluna com dois conceitos. Hoje é tolerável, porque um tenant usa uma origem ou outra. Esta change não piora,
e a comparação pela raiz já trata os dois tamanhos como a mesma empresa. O que morde é o que compara por igualdade: os
grupos e os filtros dos cards. Uma consulta que atravesse as duas origens mostra a mesma empresa como duas linhas, e o
filtro de uma não acha as notas da outra.

Por isso, a divergência vai para o `docs/STATUS.md`, com Falta, Prova e Sintoma, como os outros itens.

## Risks / Trade-offs

- **[O dropdown e os cards mostram CNPJs diferentes da mesma empresa]** → O dropdown mostra o da matriz
  (`44278225000180`), e o card de uma nota da `SP-01`, o da `SP-01` (`44278225000260`). É o certo: o card é do
  estabelecimento que emitiu. A ponte entre os dois é a descoberta, pelo `IsSameCompany`.
- **[A coluna Empresa das tabelas mostra o CNPJ da matriz]** → Numa linha de agendamento ou de execução cuja filial não
  é a matriz, o CNPJ exibido não é o do estabelecimento daquelas notas. A coluna Filial desambigua, mas é regressão de
  leitura: antes, a linha mostrava o CNPJ do próprio estabelecimento. A filial continua nas tabelas pelo código, porque
  consultar o diretório por linha faria o histórico depender do ERP no ar. A tratativa fica no `docs/STATUS.md`, para a
  próxima correção pequena: gravar o CNPJ do estabelecimento só na execução, numa coluna anulável, quando o escopo da
  descoberta resolveu um estabelecimento só. No agendamento, não: execuções registram fato, e agendamentos, critério.
- **[Um agendamento com o CNPJ de uma filial e "todas" muda de significado]** → Passa a trazer a empresa inteira. Não há
  nenhum no banco de dev, nem cliente em produção (D4).
- **[A nota com outro CNPJ da mesma raiz passa a entrar]** → Pela igualdade, ela ficava fora. Pela raiz, é da empresa
  pedida, que é o certo. A guarda continua tirando o CNPJ de outra raiz.
- **[O nome da empresa é o de uma filial]** → Só sem a matriz no cadastro: o nome é o da filial de menor ordem, o mesmo
  estabelecimento do código. O CNPJ mostrado e o nome são coerentes.
- **[Um código de 8 caracteres que não é raiz de CNPJ]** → O formatador único o mascararia como raiz (D6). Nenhuma origem
  de hoje o produz. O risco é o mesmo que o de 14 caracteres já tem, aceito no ADR-0032.
- **[Rollback]** → Reverter o código volta a igualdade: o agendamento gravado continua certo, porque tem filial.

## Migration Plan

- **Banco:** sem migração. Os agendamentos e as execuções gravados ficam como estão.
- **Deploy:** o front e o back sobem juntos, como sempre. Um front antigo contra o back novo ignora o `taxId` das filiais
  e mostra o código e o nome, como antes.
- **Rollback:** reverter o código (Risks).
