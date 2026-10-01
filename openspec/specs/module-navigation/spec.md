# module-navigation Specification

## Purpose

Organizar a barra lateral do dashboard por módulo de integração (Fiscal, Contábil, Inventário e Agendamento), montada a
partir dos módulos que cada tenant tem, marcados pelo Admin em Configurações. É apresentação, e não permissão.

## Requirements

### Requirement: O bloco Integrações da barra lateral

A barra lateral MUST ter um bloco "Integrações" com os sub-blocos Fiscal, Contábil, Inventário e Agendamento, nessa
ordem.

- **Fiscal:** abre a tela de documentos fiscais que existe hoje, com os cards, os grupos e o detalhe.
- **Agendamento:** abre a tela de integração manual que existe hoje, com a integração manual, os agendamentos e as
  execuções. O Agendamento MUST aparecer sempre, qualquer que seja o conjunto de módulos do tenant.
- **Contábil e Inventário:** abrem um painel vazio, que diz que o módulo ainda não está disponível. O
  painel MUST NOT mostrar documentos fiscais, nem filtrados, nem contados: Contábil e Inventário são outro domínio, e
  não um recorte do Fiscal.

O bloco "Administração" (Configurações e Usuários) continua como está, só para Admin. A tela aberta ao entrar é o Fiscal,
quando o tenant o tem. Sem ele, abre o primeiro módulo que o tenant tem, na ordem do bloco, ou o Agendamento.

#### Scenario: Tenant só com o Fiscal
- **WHEN** um usuário de um tenant que só tem o módulo Fiscal entra no dashboard
- **THEN** o bloco "Integrações" mostra Fiscal e Agendamento, e a tela aberta é o Fiscal

#### Scenario: Os quatro sub-blocos
- **WHEN** o tenant tem os módulos Fiscal, Contábil e Inventário
- **THEN** o bloco "Integrações" mostra Fiscal, Contábil, Inventário e Agendamento, nessa ordem

#### Scenario: O painel reservado
- **WHEN** o usuário abre o Contábil
- **THEN** a tela diz que o módulo ainda não está disponível
- **AND** nenhum documento fiscal, card ou contagem aparece nela

#### Scenario: Sem o Fiscal
- **WHEN** o tenant tem só o módulo Inventário
- **THEN** o bloco mostra Inventário e Agendamento, e a tela aberta ao entrar é o Inventário

### Requirement: Os módulos do tenant ficam no perfil

Os módulos que um tenant tem MUST ficar gravados no perfil de conector dele, como dado, e nunca em código.

- **Os valores aceitos:** `Fiscal`, `Contabil` e `Inventario`.
- **O padrão:** um perfil sem módulos gravados, ou um tenant sem perfil, tem só o `Fiscal`. É o comportamento de antes
  desta mudança.
- **A leitura:** o `/info` MUST responder `modules` com a lista do tenant logado, para qualquer papel, porque é por ela
  que a barra lateral é montada. A leitura do perfil (`GET /connector`) também devolve os `modules`.
- **A gravação:** a gravação do perfil (`PUT /connector`) aceita `modules`. Uma gravação sem o campo MUST manter os
  módulos gravados.
- **Uma lista inválida:** a gravação MUST recusar com HTTP 400, e nada é gravado, nem no cofre nem no perfil, nestes
  casos:
  - a lista tem um valor fora dos aceitos. A mensagem nomeia o valor e os aceitos;
  - a lista está vazia. A mensagem diz para marcar pelo menos um módulo.
- **A lista válida:** um valor repetido conta uma vez só, e a lista é gravada na ordem Fiscal, Contábil, Inventário.
- **O tenant:** a gravação vale só para o tenant do Admin logado.

#### Scenario: Perfil sem módulos
- **WHEN** o perfil do tenant-a foi gravado antes desta mudança, e um Viewer do tenant-a chama o `/info`
- **THEN** a resposta traz `modules = ["Fiscal"]`

#### Scenario: Admin marca o Inventário
- **WHEN** o Admin do tenant-a marca Fiscal e Inventário e salva
- **THEN** o `/info` do tenant-a passa a responder `modules = ["Fiscal", "Inventario"]`
- **AND** a barra lateral mostra Inventário, sem recarregar a página

#### Scenario: Cliente antigo sem o campo
- **WHEN** o tenant-a tem `modules = ["Fiscal", "Contabil"]`, e uma gravação do perfil chega sem o campo `modules`
- **THEN** o perfil continua com `modules = ["Fiscal", "Contabil"]`

#### Scenario: Valor desconhecido
- **WHEN** a gravação traz `modules = ["Fiscal", "Folha"]`
- **THEN** a gravação responde 400, com uma mensagem que nomeia `Folha` e os valores aceitos
- **AND** nada é gravado

#### Scenario: Lista vazia
- **WHEN** a gravação traz `modules = []`
- **THEN** a gravação responde 400, pedindo para marcar pelo menos um módulo
- **AND** nada é gravado

#### Scenario: Só o tenant do Admin
- **WHEN** o Admin do tenant-a tira o Contábil e salva, e o tenant-c tem o Contábil
- **THEN** o `/info` do tenant-c continua trazendo `Contabil`

### Requirement: Esconder um módulo é apresentação, e não permissão

Os módulos decidem só o que a barra lateral mostra. A API MUST continuar respondendo para um módulo escondido, com as
mesmas regras de papel e de tenant de antes. Restringir o acesso por módulo é outra fatia.

#### Scenario: Fiscal escondido
- **WHEN** o tenant-a tem só o módulo Inventário, e um usuário do tenant-a chama o `/groups` direto
- **THEN** a resposta é a mesma de um tenant com o Fiscal, com os grupos do tenant-a
- **AND** a barra lateral do tenant-a não mostra o Fiscal

### Requirement: A marcação em Configurações

Em Configurações, o Admin MUST ver os três módulos com uma caixa de marcar cada, marcadas conforme o perfil gravado, e
gravá-los com o resto do perfil, pelo mesmo botão de salvar. Os módulos marcados só valem depois de salvos.

#### Scenario: Marcar e salvar
- **WHEN** o Admin abre Configurações, marca o Contábil e salva
- **THEN** o perfil passa a ter o Contábil, e a barra lateral o mostra

#### Scenario: Marcar sem salvar
- **WHEN** o Admin marca o Contábil e sai de Configurações sem salvar
- **THEN** o perfil e a barra lateral ficam como estavam
