# ADR-0034: A empresa continua sendo um CNPJ completo, e se compara pela raiz

- **Status:** Aceito
- **Data:** 2026-10-05
- **Revisa:**
  - **ADR-0032 §1, "A chave":** a empresa do diretório continua sendo um CNPJ completo, mas deixa de haver uma por
    estabelecimento: é uma por raiz, com o CNPJ da matriz. "Ser da empresa" passa a ser ter a mesma raiz, e não o mesmo
    CNPJ.
  - **ADR-0032, a máscara pelo tamanho:** passa a valer também para a raiz, de 8 caracteres, que é a empresa das notas em
    XML.
- **Change OpenSpec:** `openspec/changes/company-root-in-directory`. As capacidades são `company-directory`,
  `period-discovery` e `document-grouping`.
- **Revisado por:** [ADR-0035](0035-credencial-do-perfil-e-o-cnpj-da-execucao.md). Na "Piora", a coluna Empresa da tabela
  de execuções passa a mostrar o CNPJ do estabelecimento que a descoberta resolveu, gravado na execução.

## Contexto

O ADR-0032 fez a empresa do diretório ser o CNPJ completo do estabelecimento, para ter a mesma chave dos grupos do
dashboard. Só que empresa e estabelecimento são coisas diferentes. Os quatro estabelecimentos da `brmf` têm a raiz
`44278225`: são filiais da mesma empresa, nas ordens `0001`, `0002`, `0003` e `0034`.

O defeito tinha duas faces:

- **Na tela:** o dropdown "Empresa" listava quatro empresas onde existe uma.
- **No funcionamento:** as filiais de uma empresa eram as de CNPJ igual, então cada "empresa" tinha uma filial, a dela
  mesma. A opção "Todas as filiais" nunca podia significar mais de uma, e a integração manual e a agendada não conseguiam
  pedir a empresa inteira.

O que estava errado era a comparação. O valor, um CNPJ completo, é o que a tela mostra, o que o agendamento grava e o que
os grupos do D365 usam.

## Decisão

**A empresa continua sendo um CNPJ completo, na tela e no armazenamento. Dois CNPJs são da mesma empresa quando têm a
mesma raiz. A identidade fiscal da nota continua sendo o estabelecimento.**

1. **A raiz é texto.** São os 8 primeiros caracteres do CNPJ normalizado (`TaxIdentifiers.Root`). Nunca número, e nunca "8
   dígitos": o CNPJ alfanumérico tem raiz alfanumérica (`12ABC345`), e a caixa não é convertida.
2. **"É da mesma empresa" é a igualdade das raízes, num lugar só** (`TaxIdentifiers.IsSameCompany`), ao lado da
   normalização. O diretório, o escopo da descoberta e a guarda da descoberta usam a mesma função. Um lado vazio não casa
   com nada, como a igualdade de antes.
3. **O diretório do D365 dá uma empresa por raiz,** com o CNPJ e o nome do estabelecimento que a representa:
   - o de ordem `0001`, a matriz;
   - sem a matriz no cadastro, o de menor ordem presente;
   - no empate, o de menor código.

   A empresa nunca aparece vazia, nem como a raiz sozinha. As filiais são os estabelecimentos da mesma raiz, cada uma com
   o código e o CNPJ.
4. **O rótulo é o valor gravado.** A empresa aparece como o CNPJ que a integração e o agendamento gravam, só mascarado. A
   filial aparece pelo CNPJ dela, com o código ao lado: "44.278.225/0002-60 — SP-01". O valor da filial continua sendo o
   código.
5. **A descoberta por período compara pela raiz, no escopo e na guarda.** Sem filial, entram todos os estabelecimentos da
   raiz. A guarda deixa fora a nota cujo CNPJ é de outra raiz. Uma nota com outro CNPJ da mesma raiz entra, porque é da
   empresa pedida.
6. **Sem migração.** O único agendamento gravado no banco de dev (2026-10-05) é `44278225000180` com a `Matriz`, e ele
   traz as mesmas notas de antes. Um agendamento com o CNPJ de uma filial e "todas" passaria a ser a empresa inteira, mas
   não existe nenhum, e não há cliente em produção.
7. **O documento não muda.** O `CompanyCode` do registro continua sendo o CNPJ completo do estabelecimento, no D365. Os
   cards e o modal continuam agrupando pelo estabelecimento.
8. **Um formatador só mascara a raiz e o CNPJ completo,** pelo tamanho: `44.278.225` e `44.278.225/0001-80`. A máscara de 8
   serve às notas em XML, cuja empresa é a raiz, e ao diretório de exemplo. O card das notas em XML passa a mostrar
   `12.345.678` em vez de `12345678`.

## Alternativas consideradas

- **A raiz como valor:** o dropdown oferecia `44278225`, e a comparação era "o CNPJ começa com a empresa". Foi a primeira
  versão desta decisão, e foi desfeita antes do arquivamento. Ela criava duas formas de empresa convivendo, a raiz nos
  agendamentos novos e o CNPJ nos antigos, e a raiz que a tela mostrava não era o CNPJ de ninguém. O defeito era a
  comparação, e mudar o valor era mudar mais do que o necessário.
- **O prefixo com o CNPJ completo.** Com a empresa gravada como CNPJ de 14 caracteres, "começa com" volta a ser a
  igualdade, e "Todas as filiais" volta a significar uma. A igualdade das raízes não depende do tamanho do que chega.
- **O menor CNPJ da raiz, sem a regra do `0001`.** No CNPJ numérico, dá o mesmo resultado. A regra escrita diz a intenção,
  que é "a matriz", e não depende de como as letras de uma ordem alfanumérica se comparam aos dígitos.
- **Gravar a raiz no documento, numa coluna nova.** A raiz se deriva do CNPJ completo sem perda, e uma coluna a mais é um
  lugar a mais para divergir.
- **Dois formatadores,** um para a tela de integrações e o dos cards, para não tocar nos cards. Duas funções que formatam
  a mesma coisa e diferem só no tamanho que mascaram são convite para alguém chamar a errada, e o efeito nos cards (a raiz
  do XML mascarada) é melhoria.

## Consequências

**Melhora**

- **O dropdown mostra a empresa uma vez,** com as filiais dela.
- **"Todas as filiais" significa a empresa inteira,** na integração manual e na agendada.
- **A tela e o armazenamento têm uma forma só de empresa do D365,** o CNPJ completo, que já era a dos grupos.
- **O agendamento gravado continua igual.**

**Piora**

- **O dropdown e os cards mostram CNPJs diferentes da mesma empresa:** o da matriz no dropdown, e o do estabelecimento que
  emitiu no card. É o certo, e a ponte é o `IsSameCompany`.
- **A guarda deixa entrar a nota com outro CNPJ da mesma raiz.** Pela igualdade, ela ficava fora.
- **A coluna Empresa das tabelas de agendamento e de execução mostra o CNPJ da matriz.** Numa linha cuja filial não é a
  matriz, o CNPJ exibido não é o do estabelecimento daquelas notas, e antes era. A coluna Filial desambigua, pelo
  código.

  > **Revisado pelo ADR-0035 (2026-10-05).** Na tabela de execuções, a coluna mostra o CNPJ do estabelecimento gravado na
  > execução, quando a descoberta resolveu um só. A tabela de agendamentos continua com a empresa pedida.
- **Um código de 8 caracteres que não seja raiz de CNPJ** seria mascarado como raiz. O de 14 caracteres já tinha o mesmo
  risco desde o ADR-0032, aceito pela convenção do hub: a empresa vem do CNPJ.

**Fica registrado no STATUS**

- **O `CompanyCode` significa coisas diferentes conforme a origem:** a raiz no caminho de XML, e o CNPJ completo no D365.
  É uma divergência de antes, que esta decisão não piora e não resolve. A descoberta já compara os dois pela raiz; os
  grupos e os filtros dos cards ainda comparam por igualdade.
- **A coluna Empresa das tabelas mostra o CNPJ da matriz.** A tratativa é gravar o CNPJ do estabelecimento só na
  execução, numa coluna anulável preenchida na descoberta, quando o escopo resolveu um estabelecimento só. No
  agendamento, não: ele não executou, e um CNPJ que envelhece é pior que nenhum. Execuções registram fato; agendamentos
  registram critério. A tabela não consulta o diretório: o histórico não pode depender do ERP no ar.
