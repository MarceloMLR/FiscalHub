## ADDED Requirements

### Requirement: A tela mostra o segredo configurado sem tê-lo como valor

Em Configurações, o campo de um segredo configurado MUST mostrar uma máscara fixa ao lado de "configurado em <data>".

- **Placeholder, e nunca valor:** a máscara MUST ser o placeholder do campo, e nunca o valor dele. O valor do campo
  fica vazio até o usuário digitar. Se a máscara virasse valor, o próximo salvar gravaria a string da máscara no cofre
  e destruiria o segredo.
- **O tamanho:** a máscara tem tamanho fixo. Ela não conta os caracteres do segredo.
- **Visível fora de foco:** a máscara MUST aparecer com o campo fora de foco, logo que a tela abre. Uma máscara que só
  aparece com o campo em foco faz o segredo gravado parecer ter sumido (prova manual, 2026-10-01).
- **Com cara de preenchido:** a máscara MUST ter a cor e a opacidade do texto do campo, e não as do placeholder. Ela
  diz "este campo está preenchido", e não "digite aqui". Continua sendo placeholder, e não valor.
- **O envio:** o campo continua de escrita pura. A gravação MUST levar o campo só quando o usuário digitou nele. Salvar
  sem digitar nada MUST NOT levar o campo, e o segredo gravado fica como estava.
- **Um segredo não configurado:** o campo aparece vazio, com "não configurado", e sem máscara.

#### Scenario: Segredo configurado
- **WHEN** o Client Secret do `Sandbox` do tenant-a foi gravado em 28/09/2026 14:05, e o Admin abre Configurações
- **THEN** o campo mostra a máscara e "configurado em 28/09/2026 14:05"
- **AND** o valor do campo está vazio

#### Scenario: Salvar sem digitar preserva o segredo
- **WHEN** o Client Secret do `Sandbox` está configurado, e o Admin muda só a URL base e salva, sem digitar no campo do
  segredo
- **THEN** a gravação enviada não tem o campo `clientSecret`, nem nenhum texto de máscara
- **AND** o cofre não é tocado, e o segredo gravado continua o mesmo

#### Scenario: Digitar troca o segredo
- **WHEN** o Admin digita um Client Secret novo e salva
- **THEN** a gravação leva o valor digitado, e só ele

### Requirement: A máscara nunca vira segredo

A gravação do perfil MUST recusar, com HTTP 400, um campo de escrita cujo valor é feito só de caracteres de máscara:
`*`, `•`, `●` e `∗`, com ou sem espaços.

- **O que acontece:** a mensagem nomeia o campo e diz para digitar o segredo ou deixar o campo vazio. Nada é gravado,
  nem no cofre nem no perfil.
- **A mensagem:** não repete o valor.

É a defesa do servidor contra um cliente que mande a máscara como valor. Um segredo real feito só desses caracteres
também é recusado, e o custo é aceito.

#### Scenario: Máscara como valor
- **WHEN** o tenant-a tem o Client Secret do `Sandbox` configurado, e uma gravação chega com
  `"sandbox": {"clientSecret": "••••••••"}`
- **THEN** a gravação responde 400, com uma mensagem que nomeia o `clientSecret` do `Sandbox`
- **AND** o cofre não é tocado, e o perfil gravado mantém a referência de antes

#### Scenario: Asteriscos como valor
- **WHEN** uma gravação chega com `"auth": {"clientSecret": "********"}` nas settings de entrada
- **THEN** a gravação responde 400, e nada é gravado

#### Scenario: Segredo com asterisco no meio
- **WHEN** uma gravação chega com `"clientSecret": "ab*cd"`
- **THEN** o valor é aceito e vai para o cofre
