## Purpose

Dar ao CNPJ e ao CPF uma forma só em todo o hub: sem pontuação, com as letras e a caixa como vieram. Assim, o CNPJ
alfanumérico atravessa a descoberta, a montagem, o diretório, o card e o envio com o mesmo valor.

## ADDED Requirements

### Requirement: O CNPJ e o CPF sem pontuação, com as letras

A normalização de um CNPJ ou de um CPF MUST tirar só os caracteres `.`, `/`, `-` e o espaço.

- **O que ela preserva:** as letras, os dígitos, a caixa e qualquer outro caractere, na ordem em que vieram.
- **O que ela não faz:** MUST NOT conferir o tamanho nem o dígito verificador, e MUST NOT converter a caixa. Isso é
  conteúdo fiscal, e quem o julga é a plataforma (ADR-0026).
- **O documento só com dígitos:** o resultado é o mesmo que a regra de "só dígitos" dava antes desta mudança.

#### Scenario: CNPJ numérico, como o F&O devolve
- **WHEN** o valor é `442782250001-80`
- **THEN** a forma normalizada é `44278225000180`

#### Scenario: CNPJ numérico com a máscara completa
- **WHEN** o valor é `44.278.225/0001-80`
- **THEN** a forma normalizada é `44278225000180`

#### Scenario: CNPJ alfanumérico
- **WHEN** o valor é `12.ABC.345/01DE-35`
- **THEN** a forma normalizada é `12ABC34501DE35`, e não `123450135`

#### Scenario: A caixa como veio
- **WHEN** o valor é `12abc34501de35`
- **THEN** a forma normalizada é `12abc34501de35`

#### Scenario: CPF
- **WHEN** o valor é `123.456.789-09`
- **THEN** a forma normalizada é `12345678909`

### Requirement: Uma regra só, em todo lugar que lê CNPJ ou CPF

A mesma normalização MUST valer em todos os lugares que leem CNPJ ou CPF:

- no grupo da nota: na descoberta, na montagem, no D365 e no caminho de XML;
- no diretório de empresas;
- na tradução de estabelecimentos da plataforma;
- no parceiro do payload.

Nenhum desses lugares MUST tirar letras.

#### Scenario: CNPJ alfanumérico de ponta a ponta
- **WHEN** uma nota do D365 tem `FiscalEstablishmentCNPJCPF = 12.ABC.345/01DE-35`, e o cadastro tem o estabelecimento
  com esse CNPJ
- **THEN** a empresa é `12ABC34501DE35`, com o mesmo valor:
  - na referência do coletor;
  - na referência da descoberta por período;
  - no documento montado;
  - no registro do documento;
  - no grupo e no card;
  - no diretório.

#### Scenario: Tradução com CNPJ alfanumérico
- **WHEN** a tabela `establishments` tem a chave `12.ABC.345/01DE-35`, e o estabelecimento próprio da nota é
  `12ABC34501DE35`
- **THEN** a tradução é achada

#### Scenario: Dois CNPJs alfanuméricos não colidem
- **WHEN** duas notas têm os estabelecimentos `12ABC34501DE35` e `12XYZ34501DE35`
- **THEN** elas ficam em duas empresas diferentes. A regra de "só dígitos" juntaria as duas em `123450135`
