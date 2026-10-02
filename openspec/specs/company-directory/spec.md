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

- **Empresa:** o CNPJ do estabelecimento, normalizado (`tax-identifier-normalization`), como texto. O nome da empresa é o
  nome do estabelecimento.
- **Filial:** o código do estabelecimento, como veio, com o nome do estabelecimento.
- **O mesmo CNPJ em dois estabelecimentos:** são duas filiais da mesma empresa.
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
- **THEN** as empresas são `44278225000180`, `44278225000260`, `44278225000341` e `44278225003448`
- **AND** cada uma tem uma filial: `Matriz`, `SP-01`, `SAL-01` e `RJ-01`, respectivamente

#### Scenario: Estabelecimento sem nota
- **WHEN** o `RJ-01` não tem nenhuma nota processada
- **THEN** a empresa `44278225003448` e a filial `RJ-01` aparecem do mesmo jeito

#### Scenario: As filiais de uma empresa
- **WHEN** o usuário pede as filiais da empresa `44278225000260`
- **THEN** a lista tem só a filial `SP-01`, "Filial de serviços"

#### Scenario: Empresa que não está no cadastro
- **WHEN** o usuário pede as filiais da empresa `12345678`
- **THEN** a lista é vazia, sem falha

#### Scenario: As empresas do perfil filtram o cadastro
- **WHEN** o perfil tem `companies = ["brmf"]`, e o cadastro tem estabelecimentos da `brmf` e da `usmf`
- **THEN** só os estabelecimentos da `brmf` aparecem

#### Scenario: CNPJ alfanumérico
- **WHEN** o cadastro tem um estabelecimento com CNPJ `12.ABC.345/01DE-35`
- **THEN** a empresa é `12ABC34501DE35`

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

- **cada empresa:** com o código mascarado (`document-grouping`, "CNPJ formatado na tela") e o nome;
- **cada filial:** com o código e o nome.

O valor enviado à integração e ao agendamento MUST ser o código sem máscara.

Sem diretório, ou com a leitura em falha, a tela MUST mostrar o motivo no lugar da lista. Ela MUST NOT deixar disparar
nem agendar sem empresa.

#### Scenario: Empresa do D365 no dropdown
- **WHEN** o diretório traz a empresa `44278225000260`, "Filial de serviços"
- **THEN** o dropdown mostra "44.278.225/0002-60 — Filial de serviços"
- **AND** a integração é pedida com a empresa `44278225000260`

#### Scenario: ERP sem diretório na tela
- **WHEN** a resposta do diretório é "este ERP não tem diretório"
- **THEN** a tela mostra o motivo no lugar do dropdown
- **AND** os botões de executar e de agendar ficam desabilitados
