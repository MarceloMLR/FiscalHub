## MODIFIED Requirements

### Requirement: O D365 lista os estabelecimentos do cadastro

O diretório do D365 MUST ler os estabelecimentos fiscais do cadastro do ERP, entre empresas. Ele usa a URL, a credencial
e as empresas (`companies`) do perfil do tenant, as mesmas do coletor.

- **Empresa:** a raiz do CNPJ do estabelecimento, os 8 primeiros caracteres do CNPJ normalizado
  (`tax-identifier-normalization`), como texto. Ela MUST NOT ser tratada como número, nem como "8 dígitos": o CNPJ
  alfanumérico tem raiz alfanumérica. O nome da empresa é o nome do estabelecimento de menor código daquela raiz.
- **Filial:** o código do estabelecimento, como veio, com o nome do estabelecimento.
- **Os estabelecimentos da mesma raiz:** são filiais da mesma empresa.
- **As filiais de uma empresa:** são as dos estabelecimentos cujo CNPJ normalizado começa com o código pedido. Com a raiz,
  vêm todas as da raiz. Com um CNPJ completo, o código que um agendamento antigo guarda, vem exatamente aquele
  estabelecimento. O mesmo código de filial em duas empresas do ERP é uma filial só.
- **As empresas do perfil:** com `companies` preenchido, só os estabelecimentos dessas empresas entram. Vazio, entram
  todos os que a credencial enxerga.
- **Estabelecimento sem CNPJ ou sem código:** MUST ficar fora da lista, com aviso no log que cite a empresa e o código. A
  leitura segue.
- **A fonte:** a lista MUST vir do cadastro, e não dos documentos processados. Um estabelecimento sem nota aparece.
- **A ordem:** as empresas pelo código, e as filiais de cada uma pelo código.

#### Scenario: Os quatro estabelecimentos da brmf
- **WHEN** o cadastro da `brmf` tem os estabelecimentos:
  - `Matriz`, com CNPJ `442782250001-80` e nome "Contoso Entertainment System Brazil";
  - `SP-01`, com CNPJ `442782250002-60` e nome "Filial de serviços";
  - `SAL-01`, com CNPJ `442782250003-41` e nome "Filial Salvador";
  - `RJ-01`, com CNPJ `442782250034-48` e nome "Filial Rio de Janeiro".
- **THEN** há uma empresa só, `44278225`, com o nome "Contoso Entertainment System Brazil", que é o da `Matriz`, o menor
  código da raiz
- **AND** as filiais dela são `Matriz`, `RJ-01`, `SAL-01` e `SP-01`, nessa ordem

#### Scenario: Estabelecimento sem nota
- **WHEN** o `RJ-01` não tem nenhuma nota processada
- **THEN** a filial `RJ-01` aparece na empresa `44278225` do mesmo jeito

#### Scenario: As filiais de uma empresa
- **WHEN** o usuário pede as filiais da empresa `44278225`
- **THEN** a lista tem as quatro: `Matriz`, `RJ-01`, `SAL-01` e `SP-01`

#### Scenario: As filiais pedidas com o CNPJ completo
- **WHEN** o usuário pede as filiais da empresa `44278225000260`, o código que um agendamento antigo guarda
- **THEN** a lista tem só a filial `SP-01`, "Filial de serviços"

#### Scenario: Duas raízes no cadastro
- **WHEN** o cadastro tem os quatro estabelecimentos da raiz `44278225` e um estabelecimento de CNPJ `12345678000190`
- **THEN** as empresas são `12345678` e `44278225`, nessa ordem
- **AND** a empresa `12345678` tem só a filial dela

#### Scenario: Empresa que não está no cadastro
- **WHEN** o usuário pede as filiais da empresa `98765432`
- **THEN** a lista é vazia, sem falha

#### Scenario: As empresas do perfil filtram o cadastro
- **WHEN** o perfil tem `companies = ["brmf"]`, e o cadastro tem estabelecimentos da `brmf` e da `usmf`
- **THEN** só os estabelecimentos da `brmf` aparecem

#### Scenario: CNPJ alfanumérico
- **WHEN** o cadastro tem um estabelecimento com CNPJ `12.ABC.345/01DE-35`
- **THEN** a empresa é `12ABC345`, com as letras

### Requirement: Os dropdowns mostram a empresa mascarada e o nome

A integração manual e o agendamento MUST mostrar:

- **cada empresa:** com o código mascarado e o nome. A raiz, de 8 caracteres, aparece como `NN.NNN.NNN`. Um CNPJ completo,
  de 14 caracteres, aparece com a máscara do CNPJ (`document-grouping`, "CNPJ formatado na tela"). Qualquer outro tamanho
  aparece como veio;
- **cada filial:** com o código e o nome.

As tabelas da mesma tela (as execuções e os agendamentos) MUST mascarar a empresa pela mesma regra: a execução nova traz a
raiz, e um agendamento antigo traz o CNPJ completo.

O valor enviado à integração e ao agendamento MUST ser o código sem máscara.

Sem diretório, ou com a leitura em falha, a tela MUST mostrar o motivo no lugar da lista. Ela MUST NOT deixar disparar
nem agendar sem empresa.

#### Scenario: Empresa do D365 no dropdown
- **WHEN** o diretório traz a empresa `44278225`, "Contoso Entertainment System Brazil"
- **THEN** o dropdown mostra "44.278.225 — Contoso Entertainment System Brazil"
- **AND** a integração é pedida com a empresa `44278225`

#### Scenario: Agendamento antigo na tabela
- **WHEN** a tabela de agendamentos tem um agendamento gravado com a empresa `44278225000260`
- **THEN** a empresa aparece como "44.278.225/0002-60"

#### Scenario: Raiz alfanumérica no dropdown
- **WHEN** o diretório traz a empresa `12ABC345`
- **THEN** o dropdown mostra "12.ABC.345"

#### Scenario: ERP sem diretório na tela
- **WHEN** a resposta do diretório é "este ERP não tem diretório"
- **THEN** a tela mostra o motivo no lugar do dropdown
- **AND** os botões de executar e de agendar ficam desabilitados
