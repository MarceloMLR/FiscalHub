## Purpose

Dar ao dashboard a identidade visual da Fiscosys: o nome de exibição FiscosysHub, a paleta e a tipografia da marca, o
logo e o favicon. A capability garante que todas as telas mostrem a mesma marca, nos dois temas, sem perder legibilidade.

## ADDED Requirements

### Requirement: O nome de exibição é FiscosysHub

A interface MUST chamar o produto de "FiscosysHub", e MUST NOT mostrar "FiscalHub" em nenhum texto visível nem no título
da aba.

- **O título da aba:** "FiscosysHub — Painel".
- **A sidebar:** o logo da Fiscosys seguido de "Hub".
- **O painel de marca do login:** o logo da Fiscosys seguido de "Hub". No card do meio do fluxo, o valor é
  "FiscosysHub".
- **O rodapé do login:** "© 2026 Fiscosys · Middleware de integração fiscal". A empresa é a Fiscosys, e não o produto.

#### Scenario: A aba do navegador
- **WHEN** o usuário abre o dashboard
- **THEN** a aba mostra "FiscosysHub — Painel"

#### Scenario: Nenhum FiscalHub na tela
- **WHEN** o usuário percorre o login, o Fiscal, o Agendamento, Configurações, Usuários e um módulo reservado
- **THEN** nenhum texto visível diz "FiscalHub"

#### Scenario: O rodapé do login
- **WHEN** o usuário abre o login numa tela larga
- **THEN** o rodapé do painel de marca diz "© 2026 Fiscosys · Middleware de integração fiscal"

### Requirement: Todas as telas usam a mesma paleta da marca

O acento e a cor de título MUST ser os da marca em todas as telas: nas telas próprias, nas telas montadas com componentes
MUI, como Configurações, e no login.

| Papel | Tema claro | Tema escuro |
|---|---|---|
| acento (botão, aba ativa, link, foco) | `#0072ce` | `#3b9ae1` |
| acento no hover | `#005ba9` | `#6cbaf0` |
| fundo do item selecionado (tint) | `#e6f0fb` | `#10263d` |
| cor de título e de nome | `#001a72` | `#fcfcfc` |

- **O login** é independente de tema, como hoje. O formulário usa os valores do tema claro.
- **Nenhuma tela** MUST mostrar o acento antigo, nem o petróleo `#0b5c7a`, nem o `#3d9dc4`.
- **As cores de status MUST NOT mudar:** verde, vermelho, âmbar, teal e os demais. Elas carregam significado
  (Finalizado, Com pendências, Processando, Sandbox e Produção). Também MUST NOT mudar as superfícies, as bordas, os
  neutros de texto, os raios e as sombras.

#### Scenario: Configurações no tema claro
- **WHEN** o Admin abre Configurações no tema claro
- **THEN** o botão de salvar, a aba ativa e o switch ligado usam o azul `#0072ce`
- **AND** o título "Perfil de conector" usa `#001a72`

#### Scenario: O acento no tema escuro
- **WHEN** o usuário troca para o tema escuro no Agendamento
- **THEN** o botão primário e a aba ativa usam o azul `#3b9ae1`

#### Scenario: Status não muda de cor
- **WHEN** o Fiscal mostra um grupo Finalizado e um Com pendências
- **THEN** os dois badges têm as mesmas cores de antes do rebranding

#### Scenario: O login no tema escuro
- **WHEN** o usuário sai do dashboard com o tema escuro ligado, e o login aparece
- **THEN** o formulário do login é claro, com o botão "Entrar" em `#0072ce`

### Requirement: Texto sobre o acento e acento sobre o tint são legíveis

Nos dois temas, o contraste MUST ser de pelo menos 4,5:1 nestes dois casos:

- **o texto sobre um preenchimento de acento,** no estado normal e no hover. São o botão primário das telas próprias, o
  botão preenchido das telas MUI (o "Salvar" de Configurações, por exemplo) e o avatar da topbar. No tema claro, o texto
  é branco. No escuro, é um quase-preto, `#071020`, porque o branco sobre o azul do escuro dá 3,05:1;
- **o texto na cor do acento sobre o fundo tint do item selecionado.** São o item ativo da sidebar, o chip Admin em
  Usuários e o rádio marcado no Agendamento. Esse texto usa um azul mais escuro que o acento no claro, `#005ba9`, porque
  o `#0072ce` sobre o tint dá 4,24:1. No escuro, usa `#6cbaf0`.

O ícone ou o indicador ao lado desse texto MUST ter a mesma cor do texto: o ícone do item ativo e a bolinha do rádio
marcado.

O botão "Entrar" do login fica fora da regra do escuro: o formulário do login é sempre claro, e o branco sobre o
`#0072ce` dá 4,89:1.

#### Scenario: Botão primário no escuro
- **WHEN** o usuário abre o Agendamento no tema escuro
- **THEN** o texto do botão primário é `#071020` sobre `#3b9ae1`, a 6,25:1
- **AND** no hover, é `#071020` sobre `#6cbaf0`, a 8,98:1

#### Scenario: Botão MUI no escuro
- **WHEN** o Admin abre Configurações no tema escuro
- **THEN** o texto do botão de salvar é `#071020` sobre `#3b9ae1`

#### Scenario: Avatar no escuro
- **WHEN** o usuário está no tema escuro
- **THEN** as iniciais do avatar da topbar são `#071020` sobre `#3b9ae1`

#### Scenario: Item ativo da sidebar no claro
- **WHEN** o Fiscal está aberto no tema claro
- **THEN** o rótulo "Fiscal" da sidebar é `#005ba9` sobre `#e6f0fb`, a 5,94:1
- **AND** o ícone ao lado do rótulo é `#005ba9`

#### Scenario: Rádio marcado no escuro
- **WHEN** o usuário marca uma opção de rádio no Agendamento, no tema escuro
- **THEN** o rótulo e a bolinha da opção marcada são `#6cbaf0` sobre `#10263d`

### Requirement: A tipografia da marca

O dashboard MUST usar estas três famílias:

- **Inter:** corpo, rótulos, formulários e a grade, no peso 400. Os pesos de destaque que já existem ficam como estão.
- **Exo 2:** só nos títulos e números que já são de peso 700, e no "Hub" do logo. São estes:
  - o título da topbar;
  - os números dos KPIs do Fiscal;
  - o contador de agendamentos ativos;
  - o título "Perfil de conector";
  - os títulos do painel de marca e do formulário do login;
  - o "Hub" da sidebar e do login, no peso 500.

  Nenhum outro texto muda de família.
- **DM Mono:** o que já é monoespaçado, como o tenant da sidebar e o JSON cru.

Os números de corpo MUST continuar como hoje: lining, alinhados à linha de base, e tabulares, com a mesma largura por
dígito. Isso vale para o CNPJ, as datas, os períodos, as contagens das grades e os códigos com dígito, como o do ERP.

As três famílias MUST vir de um carregamento só. A página MUST NOT baixar a Manrope nem a Raleway.

#### Scenario: KPI em Exo 2
- **WHEN** o Fiscal mostra os KPIs
- **THEN** os números dos KPIs estão em Exo 2, e os rótulos acima deles em Inter

#### Scenario: Grade em Inter
- **WHEN** o Fiscal mostra a grade de grupos
- **THEN** as células estão em Inter 400

#### Scenario: Números lining e tabulares na grade
- **WHEN** o Agendamento mostra uma execução com 39 notas, de 08/10/2026, da empresa 44.278.225/0001-80
- **THEN** os dígitos 3, 4, 5, 7 e 9 ficam na mesma linha de base dos demais, sem descer
- **AND** as colunas "Notas" e "Quando", alinhadas à direita, alinham dígito com dígito entre as linhas

#### Scenario: Código do ERP em Configurações
- **WHEN** o Admin abre Configurações, e o campo ERP mostra "Dynamics365"
- **THEN** o 3 e o 5 ficam na linha de base, como as letras vizinhas

#### Scenario: Fontes baixadas
- **WHEN** o dashboard carrega
- **THEN** a página faz uma única requisição de folha ao Google Fonts, com a Exo 2, a Inter e a DM Mono
- **AND** nenhuma requisição pede a Manrope ou a Raleway

### Requirement: O logo na sidebar acompanha o tema

O bloco de marca da sidebar MUST mostrar o logo horizontal da Fiscosys, e não um símbolo dentro de um quadrado.

- **O logo:** colorido no tema claro e branco no tema escuro, com 17 px de altura.
- **O "Hub":** um filete vertical de 1 px e o "Hub", na cor neutra apagada do tema.
- **O tenant:** fica embaixo, em mono, como hoje.
- **O bloco:** a altura de 73 px e a posição do bloco não mudam.

A troca de tema MUST trocar o logo na hora, sem recarregar a página.

#### Scenario: Tema claro
- **WHEN** um usuário do tenant-a entra no tema claro
- **THEN** a sidebar mostra o logo colorido, o filete, "Hub" e, embaixo, "tenant-a"

#### Scenario: Troca para o escuro
- **WHEN** o usuário troca para o tema escuro
- **THEN** a sidebar passa a mostrar o logo branco, sem recarregar a página

### Requirement: O painel de marca do login

O painel de marca do login MUST ter o fundo da marca: um gradiente do azul escuro da Fiscosys, com brilhos, um grid de
colunas, a marca d'água do símbolo, trilhas de fluxo e nós. O logo é o branco, com 21 px de altura.

- **O movimento:** os brilhos derivam, um pulso de luz corre sobre cada trilha, os nós pulsam e o tracejado entre os
  cards do fluxo anda. A marca d'água MUST ficar parada.
- **"Reduzir movimento":** com a preferência de reduzir movimento ligada no sistema, nada no painel MUST animar. O
  gradiente, o grid e a marca d'água continuam.
- **A legibilidade:** o texto do painel MUST ter pelo menos 4,5:1 sobre o fundo em que está, com os brilhos e o fundo
  dos cards contados, e não só sobre o azul escuro `#001a72`, em janelas de 760 a 1080 px de altura. Por isso, os
  rótulos dos cards do fluxo têm um tom próprio, mais claro que o do parágrafo: eles ficam sobre o brilho e o fundo
  translúcido do card, e o parágrafo não.
- **As telas estreitas:** abaixo de 900 px de largura o painel continua escondido, como hoje, e o formulário ocupa a
  largura toda, centrado.
- **A proporção:** acima de 900 px, o painel MUST ser ligeiramente maior que o formulário, e não muito maior. O
  formulário fica com 45% da largura, no mínimo 420 px, e o painel com o resto, 55%.
- **As trilhas e o texto:** nenhuma trilha nem nó MUST cortar o lockup, a headline, o parágrafo, os cards ou o rodapé,
  em janelas de 760 a 1080 px de altura.

#### Scenario: Painel ligeiramente maior que o formulário
- **WHEN** o usuário abre o login numa janela de 1920 × 1080
- **THEN** o painel tem 1056 px (55%), e o formulário, 864 px (45%)

#### Scenario: Janela no limite das duas colunas
- **WHEN** o usuário abre o login numa janela de 920 px de largura
- **THEN** o formulário fica com 420 px, e o painel com 500 px

#### Scenario: Janela baixa
- **WHEN** o usuário abre o login numa janela de 1360 × 760
- **THEN** nenhuma trilha passa por dentro dos cards, da headline ou do parágrafo

#### Scenario: Login numa tela larga
- **WHEN** o usuário abre o login numa janela de 1360 px
- **THEN** o painel de marca mostra o logo branco, o fundo em gradiente e os pulsos correndo sobre as trilhas

#### Scenario: Rótulo do card sob o brilho
- **WHEN** o rótulo "Saída" fica sob o ponto mais forte do segundo brilho
- **THEN** o rótulo continua com pelo menos 4,5:1 sobre o fundo do card

#### Scenario: Rótulo do card em janela baixa
- **WHEN** o usuário abre o login numa janela de 1000 × 760, e os cards descem para a faixa do segundo brilho
- **THEN** os três rótulos (ENTRADA, CONECTOR e SAÍDA) continuam com pelo menos 4,5:1

#### Scenario: Reduzir movimento
- **WHEN** a preferência "reduzir movimento" está ligada no sistema, e o usuário abre o login
- **THEN** nenhum elemento do painel anima
- **AND** o gradiente, o grid e a marca d'água continuam visíveis

#### Scenario: Tela estreita
- **WHEN** o usuário abre o login numa janela de 800 px
- **THEN** só o formulário aparece, centrado na janela, como antes

### Requirement: O campo preenchido pelo navegador segue o tema

Quando o navegador preenche um campo com um dado salvo, como o e-mail, o campo MUST manter o fundo e a cor de texto do
campo naquele tema, e não o fundo próprio do navegador. Isso vale para todo campo do dashboard: os das telas próprias e
os das telas MUI, nos dois temas.

- **O login:** como o formulário é sempre claro, o campo preenchido fica branco com o texto `#001a72`, mesmo que o app
  tenha ficado no tema escuro.
- **O foco:** o campo preenchido e com foco MUST continuar mostrando o anel de foco que tinha.

#### Scenario: E-mail salvo no login, com o tema escuro guardado
- **WHEN** o tema escuro ficou ligado, e o Chrome preenche o e-mail salvo no login
- **THEN** o campo do e-mail fica branco, com o texto `#001a72`

#### Scenario: Campo MUI preenchido no escuro
- **WHEN** o Chrome preenche um campo de Configurações no tema escuro
- **THEN** o campo fica no fundo do cartão, `#161b22`, com o texto `#fcfcfc`, e não no azul do autofill do MUI

#### Scenario: Foco num campo preenchido
- **WHEN** o usuário põe o foco no e-mail que o Chrome preencheu no login
- **THEN** o campo continua branco, e o anel de foco aparece

### Requirement: O favicon da Fiscosys

A aba MUST mostrar o símbolo da Fiscosys como favicon: o símbolo branco sobre um quadrado `#001a72`. A página MUST
oferecer o SVG, um PNG de 32 px para navegadores sem SVG e um ícone de 180 px para a tela inicial do iOS.

#### Scenario: Favicon na aba
- **WHEN** o usuário abre o dashboard
- **THEN** a aba mostra o símbolo da Fiscosys, legível tanto em aba clara como em aba escura
