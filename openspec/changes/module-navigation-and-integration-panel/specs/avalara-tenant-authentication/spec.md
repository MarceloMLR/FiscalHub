## ADDED Requirements

### Requirement: O teste de credencial conversa com a recusa lembrada

O teste de credencial da Avalara (capability `connector-credential-test`) troca o token sem passar pela recusa lembrada
do envio. Quem freia o botão é o endpoint de teste. O resultado do teste MUST valer para o envio:

- **Um teste que dá certo:** MUST esquecer, na hora, a recusa lembrada do tenant e os tokens dele em cache, como o
  salvar do perfil já faz. O token novo pode ser guardado no cache.
- **Um teste recusado:** a recusa MUST passar a valer como recusa lembrada do envio daquela credencial, pelo mesmo
  intervalo. Assim, as notas em voo não viram, cada uma, uma tentativa de login com a credencial errada.
- **Os outros tenants:** MUST NOT ser afetados.

#### Scenario: Liberação do lado da plataforma, provada pelo teste
- **WHEN** o endpoint de token recusou a credencial do tenant-a no envio, a plataforma liberou o cliente, e o Admin
  testa a credencial dentro do intervalo, sem mudar nada
- **THEN** o teste vai ao endpoint de token e funciona
- **AND** o envio seguinte do tenant-a pede token na hora, sem esperar o intervalo

#### Scenario: Recusa no teste vale para o envio
- **WHEN** o teste da credencial do tenant-a no `Sandbox` é recusado, e uma nota do tenant-a vai ser enviada logo
  depois no `Sandbox`
- **THEN** o envio falha com a recusa lembrada, sem pedir token

#### Scenario: Outro tenant não é afetado pelo teste
- **WHEN** o tenant-a e o tenant-b têm credenciais recusadas, e o teste do tenant-a dá certo
- **THEN** o envio seguinte do tenant-b continua falhando com a recusa lembrada, sem pedir token
