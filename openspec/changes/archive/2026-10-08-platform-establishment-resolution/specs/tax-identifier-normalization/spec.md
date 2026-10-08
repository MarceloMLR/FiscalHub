## MODIFIED Requirements

### Requirement: Uma regra só, em todo lugar que lê CNPJ ou CPF

A mesma normalização MUST valer em todos os lugares que leem CNPJ ou CPF:

- no grupo da nota: na descoberta, na montagem, no D365 e no caminho de XML;
- no diretório de empresas;
- na tradução de estabelecimentos da plataforma;
- na listagem de estabelecimentos da plataforma, no CNPJ de cada contribuinte;
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

#### Scenario: Resolução pela plataforma com CNPJ alfanumérico
- **WHEN** o estabelecimento próprio da nota é `12.ABC.345/01DE-35`, a tabela `establishments` não o tem, e a plataforma
  lista um único contribuinte com `cnpj = 12ABC34501DE35`
- **THEN** os códigos desse contribuinte vão no payload
- **AND** o CNPJ comparado é `12ABC34501DE35` nas duas pontas

#### Scenario: Dois CNPJs alfanuméricos não colidem
- **WHEN** duas notas têm os estabelecimentos `12ABC34501DE35` e `12XYZ34501DE35`
- **THEN** elas ficam em duas empresas diferentes. A regra de "só dígitos" juntaria as duas em `123450135`
