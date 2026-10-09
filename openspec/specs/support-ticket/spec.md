# support-ticket Specification

## Purpose

Abrir um chamado de suporte a partir de uma ou mais notas, levando junto os arquivos de rastreabilidade (origem,
domínio, destino) e um resumo do estado de cada nota. O provider do chamado é escolhido no perfil do tenant
(ADR-0021). Tudo escopado ao tenant.

## Requirements

### Requirement: O chamado sai de notas selecionadas, dentro do tenant

Abrir um chamado MUST exigir ao menos uma nota, um título e uma descrição. O tenant MUST ser o do usuário logado, e
não um campo do pedido. As notas MUST ser buscadas com o escopo desse tenant: chave de nota de outro tenant
simplesmente não entra.

Se nenhuma das notas selecionadas existir no tenant, a abertura MUST falhar dizendo isso.

Toda falha de regra da abertura MUST responder HTTP 400, com a mensagem.

#### Scenario: Seleção vazia
- **WHEN** o usuário pede o chamado sem nenhuma nota
- **THEN** a abertura falha com "Selecione ao menos uma nota para abrir o chamado."

#### Scenario: Título ou descrição em branco
- **WHEN** o título ou a descrição vêm vazios, ou só com espaços
- **THEN** a abertura falha dizendo qual dos dois é obrigatório

#### Scenario: Chave de outro tenant
- **WHEN** a seleção inclui a chave de uma nota de outro tenant
- **THEN** essa nota não entra no chamado
- **AND** se nenhuma nota sobrar, a abertura falha com "Nenhuma das notas selecionadas foi encontrada neste tenant."

### Requirement: O provider vem do perfil do tenant

O adapter de chamados MUST sair do perfil de conector do tenant, e nunca de código. O nome do perfil MUST ser comparado
com o dos adapters registrados sem distinguir maiúscula.

- **Perfil sem adapter de chamados:** a abertura MUST falhar dizendo que não está configurada para o tenant.
- **Adapter nomeado no perfil, mas não registrado:** a abertura MUST falhar dizendo o nome que não existe.

As configurações do provider MUST vir do perfil, junto com o nome do adapter.

#### Scenario: Tenant sem chamados configurados
- **WHEN** o perfil do tenant não tem adapter de chamados
- **THEN** a abertura falha com "Abertura de chamado não está configurada para este tenant."

#### Scenario: Adapter que não existe
- **WHEN** o perfil nomeia o adapter `Freshdesk`, e só o `Local` está registrado
- **THEN** a abertura falha com uma mensagem que cita `Freshdesk`

### Requirement: Um zip de rastreabilidade por nota

Para cada nota do chamado, os arquivos de rastreabilidade MUST ser empacotados num zip próprio, um por nota, e não um
zip único, e anexados ao chamado. O nome do arquivo MUST ser derivado da chave da nota, trocando por `_` tudo que não
for letra, dígito, `-` ou `_`.

Nota sem arquivo de rastreabilidade MUST seguir no chamado, só sem anexo. Ela não bloqueia a abertura.

#### Scenario: Notas com rastreabilidade
- **WHEN** o chamado leva três notas, todas com arquivos
- **THEN** o chamado vai com três zips, um por nota

#### Scenario: O nome do zip
- **WHEN** a chave da nota é `brmf|BRMF21-10000026`
- **THEN** o anexo dela se chama `brmf_BRMF21-10000026.zip`

#### Scenario: Nota sem arquivos
- **WHEN** uma das notas não tem arquivo de rastreabilidade
- **THEN** o chamado abre assim mesmo, sem o anexo dessa nota

### Requirement: O teto de anexos é do chamado inteiro

A soma dos zips das notas e dos anexos extras do usuário MUST respeitar um teto único de 20 MB (20 × 1024 × 1024
bytes), qualquer que seja o provider. Estourar o teto MUST falhar com uma mensagem que diz o que fazer: selecionar
menos notas, ou remover arquivos.

Anexo extra vazio MUST ser ignorado, e não contado.

#### Scenario: Logs passam do teto
- **WHEN** os zips das notas somam mais de 20 MB
- **THEN** a abertura falha pedindo para selecionar menos notas

#### Scenario: Anexos do usuário passam do teto
- **WHEN** os zips cabem, mas os anexos extras estouram a soma
- **THEN** a abertura falha pedindo para remover arquivos ou selecionar menos notas

#### Scenario: Anexo extra vazio
- **WHEN** o usuário manda um anexo extra de zero bytes
- **THEN** o chamado abre sem esse anexo

### Requirement: A descrição leva o estado de cada nota

A descrição enviada ao provider MUST ser o texto do usuário seguido de um bloco por nota com:

- o número, ou a chave, quando não houver número;
- a chave;
- o status;
- o motivo, quando houver;
- o id externo, quando houver;
- as tentativas;
- a data de atualização, em UTC.

A descrição MUST terminar dizendo que os logs seguem anexados, zipados por nota.

#### Scenario: Duas notas no chamado
- **WHEN** o chamado leva duas notas
- **THEN** a descrição tem o texto do usuário e, depois, um bloco para cada uma, com status e tentativas

#### Scenario: Nota sem número
- **WHEN** uma nota do chamado não tem número
- **THEN** o bloco dela é identificado pela chave

### Requirement: O tamanho dos logs pode ser estimado antes

A tela MUST conseguir perguntar quantos bytes os zips das notas selecionadas ocupam, antes de abrir o chamado, para
avisar o usuário cedo. A estimativa MUST usar o mesmo escopo de tenant da abertura. A resposta MUST trazer também o
teto, em bytes.

Seleção vazia MUST estimar zero, sem erro.

#### Scenario: Estimativa antes de abrir
- **WHEN** a tela pede a estimativa de três notas
- **THEN** a resposta é a soma dos bytes dos zips dessas notas, e o teto

#### Scenario: Estimativa sem seleção
- **WHEN** a tela pede a estimativa sem notas
- **THEN** a resposta é zero, e não um erro
