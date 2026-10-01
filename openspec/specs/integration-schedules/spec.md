# integration-schedules Specification

## Purpose

Deixar o usuário do tenant excluir um agendamento de integração, pela API e pela tela, sem perder o histórico das
execuções que ele já disparou.

## Requirements

### Requirement: Exclusão de agendamento

A API MUST aceitar a exclusão de um agendamento do tenant de quem está logado, ativo ou desativado, recorrente ou
único.

- **Depois de excluído:** o agendamento MUST NOT aparecer na lista e MUST NOT disparar de novo.
- **Id de outro tenant:** MUST receber a mesma resposta de "não encontrado" que um id inexistente, e o agendamento
  MUST NOT ser excluído.
- **O histórico:** as execuções que o agendamento já disparou MUST continuar na lista de execuções, com o modo, a
  empresa, a filial, o período e o número de notas que tinham.
- **Na tela:** cada agendamento da lista MUST ter a ação "Excluir", com um pedido de confirmação que diz que a
  exclusão não se desfaz e que o histórico de execuções fica. Sem confirmação, nada é excluído.

A exclusão segue a mesma regra de acesso do desativar.

#### Scenario: Excluir um recorrente ativo
- **WHEN** o usuário do tenant-a exclui o agendamento diário 7, que está ativo, e confirma
- **THEN** a API responde sucesso sem conteúdo, e o agendamento 7 some da lista
- **AND** o agendador não o dispara mais

#### Scenario: O histórico fica
- **WHEN** o agendamento 7 já disparou duas execuções e é excluído
- **THEN** a lista de execuções continua com as duas, com o modo, a empresa, a filial, o período e o número de notas
  de cada uma

#### Scenario: Id de outro tenant
- **WHEN** o usuário do tenant-a pede a exclusão do agendamento 9, que é do tenant-b
- **THEN** a API responde "não encontrado", e o agendamento 9 continua existindo

#### Scenario: Id inexistente
- **WHEN** o usuário pede a exclusão de um agendamento que não existe
- **THEN** a API responde "não encontrado"

#### Scenario: Cancelar a confirmação
- **WHEN** o usuário clica em "Excluir" e cancela a confirmação
- **THEN** nenhum pedido de exclusão é feito, e o agendamento continua na lista
