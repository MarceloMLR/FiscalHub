# company-directory Specification

## Purpose

Dar à integração manual e ao agendamento as empresas e as filiais do tenant logado, lidas do ERP dele pela
implementação do adapter de entrada do perfil, sem o mock fora de Development, e dizer o que a tela mostra quando
o ERP não tem diretório ou a leitura falha.

## Requirements

### Requirement: O diretório é o do tenant logado, pelo adapter de entrada do perfil

A lista de empresas e a de filiais MUST ser do tenant do usuário logado. Um tenant na requisição MUST NOT decidir de
quem é a lista (`tenant-boundary`).

A implementação do diretório MUST ser a do adapter de entrada do perfil do tenant, pela comparação exata do
identificador, como na resolução do adapter de entrada (`inbound-source-resolution`).

- **Sem implementação para o adapter, ou sem perfil:** a resposta MUST dizer que o ERP do tenant não tem diretório, e
  citar o adapter. Fora de Development, ela MUST NOT ser uma lista de outra fonte.
- **Em Development, e só lá:** o diretório de exemplo responde no lugar da implementação que falta. Ele MUST NOT
  responder por um tenant cujo adapter tem implementação.

#### Scenario: Tenant do D365
- **WHEN** o perfil do tenant-a tem o adapter de entrada `Dynamics365`, e o usuário do tenant-a abre a integração manual
- **THEN** as empresas vêm do cadastro do D365 do tenant-a
- **AND** a empresa `12345678` do diretório de exemplo não aparece

#### Scenario: ERP sem diretório, fora de Development
- **WHEN** o perfil do tenant-b tem o adapter de entrada `iScala`, e o host roda fora de Development
- **THEN** a resposta diz que o ERP `iScala` do tenant-b não tem diretório
- **AND** nenhuma empresa é listada

#### Scenario: ERP sem diretório, em Development
- **WHEN** o perfil do tenant-b tem o adapter de entrada `iScala`, e o host roda em Development
- **THEN** a lista é a do diretório de exemplo, como hoje

### Requirement: O D365 lista os estabelecimentos do cadastro

O diretório do D365 MUST ler os estabelecimentos fiscais do cadastro do ERP, entre empresas. Ele usa a URL, a credencial
e as empresas (`companies`) do perfil do tenant, as mesmas do coletor.

- **A empresa:** uma por raiz do CNPJ. A raiz é formada pelos 8 primeiros caracteres do CNPJ normalizado
  (`tax-identifier-normalization`), como texto. Ela MUST NOT ser tratada como número, nem como "8 dígitos": o CNPJ
  alfanumérico tem raiz alfanumérica. Os estabelecimentos da mesma raiz são filiais da mesma empresa.
  - **O código:** o CNPJ completo, normalizado, do estabelecimento que representa a raiz. A ordem do CNPJ são os 4
    caracteres depois da raiz.
    - O representante é o de ordem `0001`, a matriz.
    - Sem a ordem `0001` no cadastro, é o de menor ordem presente, com as ordens comparadas como texto.
    - No empate, ganha o de menor código.

    O código MUST NOT ser vazio, nem a raiz sozinha: é o mesmo texto que a tela mostra e que um agendamento grava.
  - **O nome:** o do mesmo estabelecimento.
- **A filial:** o código do estabelecimento, como veio, com o nome e o CNPJ normalizado dele.
- **As filiais de uma empresa:** as dos estabelecimentos com a mesma raiz do código pedido. Qualquer CNPJ da empresa traz
  todas elas: o da matriz, que é o que o dropdown oferece, ou o de uma filial. A empresa vazia não traz nenhuma. O mesmo
  código de filial em duas empresas do ERP é uma filial só.
- **As empresas do perfil:** com `companies` preenchido, só os estabelecimentos dessas empresas entram. Vazio, entram
  todos os que a credencial enxerga.
- **Estabelecimento sem CNPJ ou sem código:** MUST ficar fora da lista, com aviso no log que cite a empresa e o código. A
  leitura segue.
- **A fonte:** a lista MUST vir do cadastro, e não dos documentos processados. Um estabelecimento sem nota aparece.
- **A ordem:** as empresas pela raiz, e as filiais de cada uma pelo código.

#### Scenario: Os quatro estabelecimentos da brmf
- **WHEN** o cadastro da `brmf` tem os estabelecimentos:
  - `Matriz`, com CNPJ `442782250001-80` e nome "Contoso Entertainment System Brazil";
  - `SP-01`, com CNPJ `442782250002-60` e nome "Filial de serviços";
  - `SAL-01`, com CNPJ `442782250003-41` e nome "Filial Salvador";
  - `RJ-01`, com CNPJ `442782250034-48` e nome "Filial Rio de Janeiro".
- **THEN** há uma empresa só, `44278225000180`, "Contoso Entertainment System Brazil": o CNPJ e o nome da `Matriz`, a de
  ordem `0001`
- **AND** as filiais dela são `Matriz`, `RJ-01`, `SAL-01` e `SP-01`, nessa ordem, cada uma com o CNPJ dela

#### Scenario: Estabelecimento sem nota
- **WHEN** o `RJ-01` não tem nenhuma nota processada
- **THEN** a filial `RJ-01` aparece na empresa `44278225000180` do mesmo jeito

#### Scenario: As filiais de uma empresa
- **WHEN** o usuário pede as filiais da empresa `44278225000180`
- **THEN** a lista tem as quatro: `Matriz`, `RJ-01`, `SAL-01` e `SP-01`

#### Scenario: As filiais pedidas com o CNPJ de uma filial
- **WHEN** o usuário pede as filiais da empresa `44278225000260`, o CNPJ da `SP-01`
- **THEN** a lista tem as mesmas quatro, porque a comparação é pela raiz

#### Scenario: Cadastro sem a matriz
- **WHEN** o cadastro tem só a `SP-01` (ordem `0002`) e a `SAL-01` (ordem `0003`) da raiz `44278225`
- **THEN** a empresa é `44278225000260`, "Filial de serviços", o CNPJ e o nome da de menor ordem
- **AND** ela não aparece vazia, nem como a raiz sozinha

#### Scenario: Duas raízes no cadastro
- **WHEN** o cadastro tem os quatro estabelecimentos da raiz `44278225` e um estabelecimento de CNPJ `12345678000190`
- **THEN** as empresas são `12345678000190` e `44278225000180`, nessa ordem
- **AND** a empresa `12345678000190` tem só a filial dela

#### Scenario: Empresa que não está no cadastro
- **WHEN** o usuário pede as filiais da empresa `98765432000188`
- **THEN** a lista é vazia, sem falha

#### Scenario: Empresa vazia
- **WHEN** o usuário pede as filiais da empresa vazia
- **THEN** a lista é vazia, e não a de todas as empresas

#### Scenario: As empresas do perfil filtram o cadastro
- **WHEN** o perfil tem `companies = ["brmf"]`, e o cadastro tem estabelecimentos da `brmf` e da `usmf`
- **THEN** só os estabelecimentos da `brmf` aparecem

#### Scenario: CNPJ alfanumérico
- **WHEN** o cadastro tem um só estabelecimento, com CNPJ `12.ABC.345/01DE-35`
- **THEN** a empresa é `12ABC34501DE35`, com as letras, e a raiz dela é `12ABC345`

### Requirement: A falha da leitura é dita, sem segredo

Quando a leitura do diretório no ERP falha, a resposta MUST ser uma falha com motivo, e MUST NOT ser uma lista vazia.

- **Settings do perfil inválidas:** o motivo é o da configuração, e nenhuma chamada é feita ao ERP.
- **O ERP nega a leitura (HTTP 403):** o motivo MUST citar a role do conector e o privilégio da entidade do cadastro.
- **Outra resposta de erro:** o motivo cita o status HTTP.
- **O ERP pede espera (throttling):** o motivo diz que o ERP pediu espera.

O motivo MUST NOT conter o token, o segredo, nem um cabeçalho com valor.

#### Scenario: A role sem o privilégio
- **WHEN** o F&O responde 403 à leitura do cadastro de estabelecimentos
- **THEN** a falha diz que a role `FSFiscalHubIntegration` precisa do privilégio `FiscalEstablishmentEntityView`
- **AND** nenhuma empresa é listada

#### Scenario: O perfil sem a URL do ERP
- **WHEN** o perfil do tenant-a não tem a `url` do F&O
- **THEN** a falha cita a `url`, e nenhuma chamada é feita ao F&O

#### Scenario: O motivo sem o token
- **WHEN** a leitura falha com HTTP 500
- **THEN** o motivo cita o 500, e não contém o token nem o cabeçalho `Authorization`

### Requirement: Os dropdowns mostram a empresa mascarada e o nome

A integração manual e o agendamento MUST mostrar:

- **cada empresa:** o código, mascarado pela regra única de `document-grouping` ("CNPJ formatado na tela"), e o nome. A
  empresa do D365 é o CNPJ completo da matriz, e aparece com a máscara do CNPJ. O rótulo MUST ser o código gravado, só
  mascarado: nenhum rótulo de empresa difere do valor que a integração e o agendamento gravam;
- **cada filial:** com o CNPJ do estabelecimento, se o diretório o tiver, mascarado pela mesma regra e com o código ao
  lado. Sem o CNPJ, como no diretório de exemplo, ela aparece com o código e o nome.

As tabelas da mesma tela mascaram pela mesma regra, e mostram a filial pelo código gravado. A coluna Empresa difere entre
as duas:

- **a tabela de agendamentos** MUST mostrar a empresa gravada, que é o critério pedido;
- **a tabela de execuções** MUST mostrar o CNPJ do estabelecimento que a execução gravou (`period-discovery`, "A execução
  grava o estabelecimento que a descoberta resolveu"), e a empresa gravada quando a execução não tem esse CNPJ. É o caso
  da execução de várias filiais, e o da execução gravada antes do campo.

As duas tabelas MUST NOT consultar o diretório para montar a coluna: o histórico não depende do ERP no ar.

O valor enviado à integração e ao agendamento MUST ser o código sem máscara: o da empresa, que é o CNPJ completo, e o da
filial.

Sem diretório, ou com a leitura em falha, a tela MUST mostrar o motivo no lugar da lista. Ela MUST NOT deixar disparar
nem agendar sem empresa.

#### Scenario: Empresa do D365 no dropdown
- **WHEN** o diretório traz a empresa `44278225000180`, "Contoso Entertainment System Brazil"
- **THEN** o dropdown mostra "44.278.225/0001-80 — Contoso Entertainment System Brazil"
- **AND** a integração é pedida com a empresa `44278225000180`

#### Scenario: Filial do D365 no dropdown
- **WHEN** o diretório traz a filial `SP-01`, com o CNPJ `44278225000260`
- **THEN** o dropdown mostra "44.278.225/0002-60 — SP-01"
- **AND** a integração é pedida com a filial `SP-01`

#### Scenario: Filial sem CNPJ no dropdown
- **WHEN** o diretório de exemplo traz a filial `0001`, "Matriz", sem CNPJ
- **THEN** o dropdown mostra "0001 — Matriz"

#### Scenario: O agendamento na tabela
- **WHEN** a tabela de agendamentos tem um agendamento gravado com a empresa `44278225000180` e a filial `Matriz`
- **THEN** a empresa aparece como "44.278.225/0001-80", o mesmo texto do dropdown, e a filial como "Matriz"

#### Scenario: O agendamento de uma filial na tabela
- **WHEN** a tabela de agendamentos tem um agendamento gravado com a empresa `44278225000180` e a filial `SP-01`
- **THEN** a empresa aparece como "44.278.225/0001-80", e a filial como "SP-01"

#### Scenario: A execução da SP-01 na tabela
- **WHEN** a tabela de execuções tem uma execução gravada com a empresa `44278225000180`, a filial `SP-01` e o CNPJ
  `44278225000260`
- **THEN** a empresa aparece como "44.278.225/0002-60", e a filial como "SP-01"

#### Scenario: A execução de todas as filiais na tabela
- **WHEN** a tabela de execuções tem uma execução gravada com a empresa `44278225000180`, a filial "todas" e sem CNPJ
- **THEN** a empresa aparece como "44.278.225/0001-80"

#### Scenario: A execução gravada antes do CNPJ
- **WHEN** a tabela de execuções tem uma execução gravada antes do campo, com a empresa `44278225000180`, a filial `SP-01`
  e o CNPJ vazio
- **THEN** a empresa aparece como "44.278.225/0001-80", como antes

#### Scenario: A tabela de execuções com o ERP fora do ar
- **WHEN** a leitura do diretório do tenant-a falha, e a tabela de execuções tem a execução da `SP-01` com o CNPJ
  `44278225000260`
- **THEN** a empresa da execução aparece como "44.278.225/0002-60"
- **AND** montar a tabela não faz nenhuma leitura ao ERP

#### Scenario: ERP sem diretório na tela
- **WHEN** a resposta do diretório é "este ERP não tem diretório"
- **THEN** a tela mostra o motivo no lugar do dropdown
- **AND** os botões de executar e de agendar ficam desabilitados
