## MODIFIED Requirements

### Requirement: Impossibilidade do lado do conector

Quando o conector não consegue fazer a requisição do documento, o documento MUST ser registrado como
rejeitado.

Antes de qualquer requisição à plataforma, nos casos a seguir:

- falta configuração do tenant, incluindo a credencial, a referência do segredo, o segredo resolvido e a
  URL do ambiente, ou a entrada da tabela de estabelecimentos tem um código faltando;
- um campo exigido pelo contrato não pode ser representado;
- a URL do ambiente não é aceita;
- o endpoint de token recusa a credencial.

Depois da listagem de estabelecimentos da plataforma, e antes do envio (`platform-establishment-resolution`):

- o estabelecimento próprio não tem tradução: não tem entrada na tabela, e não tem contribuinte na plataforma, ou o destino
  não sabe listar;
- o CNPJ do estabelecimento próprio casa com mais de um contribuinte na plataforma;
- o único contribuinte que casa não tem um dos códigos;
- a listagem é recusada: a credencial negada, o caminho que não existe, o formato fora do verificado, a paginação que
  não avança ou o teto de páginas (`avalara-establishment-listing`).

Depois da requisição de envio, como na rejeição síncrona:

- a plataforma nega a credencial (403, ou 401 com token recém-emitido);
- o caminho de envio não existe na URL montada (404 no envio). O motivo aponta a URL base do perfil e o
  `Avalara:DocumentsPath` do host, que são as duas partes da URL.

O motivo MUST identificar o problema como do conector (configuração ou contrato do destino). A mensagem da
fila MUST ser concluída sem retentativa.

Depois de corrigida a causa, o reprocessamento manual do documento MUST enviá-lo normalmente.

#### Scenario: Tenant sem os códigos da empresa
- **WHEN** a esteira envia uma nota de um tenant cujo destino não sabe listar, e cujas settings de saída não têm a tabela
  de estabelecimentos no ambiente ativo
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o
  que falta
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Estabelecimento sem contribuinte na plataforma
- **WHEN** a esteira envia uma nota cujo estabelecimento próprio não está na tabela, e a listagem completa da plataforma não
  tem contribuinte com esse CNPJ
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que cita o CNPJ
- **AND** nenhuma requisição de envio é feita
- **AND** a mensagem da fila é concluída sem retentativa

#### Scenario: Estabelecimento com mais de um contribuinte
- **WHEN** a esteira envia uma nota cujo estabelecimento próprio não está na tabela, e a plataforma tem dois contribuintes
  com esse CNPJ
- **THEN** o documento é registrado como rejeitado, com motivo que nomeia os dois candidatos
- **AND** nenhuma requisição de envio é feita

#### Scenario: Tenant sem o segredo
- **WHEN** a esteira envia uma nota de um tenant cujo segredo não está configurado, ou cujo cofre não tem o
  valor
- **THEN** o documento é registrado como rejeitado, com motivo de configuração do conector que nomeia o campo
  e aponta a tela de conectores onde configurá-lo
- **AND** nenhuma requisição é feita ao endpoint de token nem à plataforma

#### Scenario: Reprocessar depois de corrigir a configuração
- **WHEN** a configuração que faltava é incluída e o documento rejeitado é reprocessado manualmente
- **THEN** o documento é enviado à plataforma e registrado como enviado

#### Scenario: Reprocessar depois de cadastrar o contribuinte
- **WHEN** o documento foi rejeitado porque o CNPJ não tinha contribuinte, o contribuinte é cadastrado na plataforma, o
  perfil do tenant é salvo e o documento é reprocessado manualmente
- **THEN** o documento é enviado com os códigos do contribuinte novo e registrado como enviado
