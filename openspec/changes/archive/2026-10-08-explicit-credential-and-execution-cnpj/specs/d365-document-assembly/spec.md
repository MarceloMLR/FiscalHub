## MODIFIED Requirements

### Requirement: Teste contra o ambiente real opt-in

Um teste de integração MUST montar as NF-e modelo 55 do ambiente real. Ele MUST ser pulado a menos que a
variável de ambiente que aponta o ambiente e as três da credencial estejam definidas, no mesmo padrão do teste do feed de
mudanças: a autenticação é a do conector, client credentials, e o motivo do skip nomeia cada variável que falta.

#### Scenario: Contra o fiscosysdev
- **WHEN** a variável aponta o fiscosysdev, a empresa é `brmf` e as variáveis da credencial trazem o app do conector
- **THEN** cada NF-e modelo 55 da base é montada sem erro, e duas montagens seguidas da mesma nota dão a
  mesma impressão

#### Scenario: O ambiente sem a credencial
- **WHEN** a variável do ambiente está definida, e as três da credencial não
- **THEN** o teste aparece como pulado, o motivo nomeia as três variáveis, e nenhuma chamada de rede é feita
