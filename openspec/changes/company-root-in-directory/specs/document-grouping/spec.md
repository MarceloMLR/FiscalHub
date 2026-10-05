## MODIFIED Requirements

### Requirement: CNPJ formatado na tela

A empresa MUST aparecer mascarada pelo tamanho, sejam os caracteres dígitos ou letras, por uma regra só:

- **14 caracteres, o CNPJ completo:** a máscara de CNPJ (`44.278.225/0001-80`, `12.ABC.345/01DE-35`). É a empresa dos
  grupos do D365 e a do diretório do D365 (`company-directory`);
- **8 caracteres, a raiz do CNPJ:** a máscara da raiz (`44.278.225`, `12.ABC.345`). É a empresa dos grupos do caminho de
  XML e a do diretório de exemplo;
- **qualquer outro tamanho:** aparece como está.

A regra vale:

- na tabela de grupos e no título do grupo;
- nos dropdowns da integração manual e do agendamento, inclusive no CNPJ da filial;
- nas listas de execuções e de agendamentos.

A máscara é só de apresentação. O código servido, o da URL do grupo, o do filtro e o enviado à integração MUST continuar
sem máscara.

#### Scenario: Empresa do D365
- **WHEN** a tabela de grupos lista a empresa `44278225000180`
- **THEN** a coluna "Empresa" mostra `44.278.225/0001-80`
- **AND** abrir o grupo consulta as notas da empresa `44278225000180`

#### Scenario: Empresa alfanumérica
- **WHEN** a tabela de grupos lista a empresa `12ABC34501DE35`
- **THEN** a coluna "Empresa" mostra `12.ABC.345/01DE-35`
- **AND** abrir o grupo consulta as notas da empresa `12ABC34501DE35`

#### Scenario: Empresa do XML
- **WHEN** a tabela de grupos lista a empresa `12345678`, a raiz que o caminho de XML grava
- **THEN** a coluna "Empresa" mostra `12.345.678`, e não mais `12345678`
- **AND** abrir o grupo consulta as notas da empresa `12345678`

#### Scenario: Raiz alfanumérica
- **WHEN** a empresa é `12ABC345`
- **THEN** ela aparece como `12.ABC.345`

#### Scenario: Outro tamanho
- **WHEN** a empresa é `B01`
- **THEN** ela aparece como `B01`
