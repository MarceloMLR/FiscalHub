## MODIFIED Requirements

### Requirement: Fotos só para o tenant do usuário

A inspeção do trace e o download das fotos de um documento MUST comparar o tenant da rota com o tenant do
usuário logado:

- **Tenants diferentes:** a resposta MUST ser a mesma de um documento sem fotos (HTTP 404, com o mesmo
  corpo), e o armazenamento das fotos do outro tenant MUST NOT ser lido.
- **Mesmo tenant:** o comportamento é o de hoje, para quem tem o papel que vê as fotos cruas (hoje, o Admin). Quem não
  tem o papel recebe 403 antes de qualquer leitura (`platform-response-trace`, "As fotos cruas só para quem pode ver
  o JSON").

A mesma regra de tenant vale para a leitura do desfecho do documento, que é aberta a qualquer papel do tenant.

#### Scenario: Download de documento de outro tenant
- **WHEN** um Admin do tenant-b pede o download das fotos de `tenant-a/<chave>`, que existe
- **THEN** a resposta é 404, com o mesmo corpo de um documento inexistente
- **AND** o armazenamento das fotos do tenant-a não é lido

#### Scenario: Inspeção de documento de outro tenant
- **WHEN** um Admin do tenant-b pede a inspeção do trace de `tenant-a/<chave>`
- **THEN** a resposta é 404, com o mesmo corpo de um documento inexistente
- **AND** o armazenamento das fotos do tenant-a não é lido

#### Scenario: Download do próprio tenant
- **WHEN** um Admin do tenant-a pede o download das fotos de `tenant-a/<chave>`
- **THEN** a resposta é o zip com as fotos do documento, incluindo as respostas da plataforma

#### Scenario: Documento inexistente do próprio tenant
- **WHEN** um Admin do tenant-a pede o download de `tenant-a/<chave>`, que não tem fotos
- **THEN** a resposta é 404, com o mesmo corpo do caso de outro tenant

#### Scenario: Viewer do próprio tenant
- **WHEN** um Viewer do tenant-a pede a inspeção do trace ou o download de `tenant-a/<chave>`
- **THEN** a resposta é 403, e o armazenamento das fotos não é lido
