## Context

A motivação está no `proposal.md`, e os requisitos, na spec `dashboard-brand-identity`. As decisões de marca, com o
porquê de cada uma, estão em `docs/rebranding-fiscosys.md`. O CSS do login, pronto, está na seção 3 de
`docs/rebranding-fiscosys-execucao.md`. Aqui ficam o estado do código que molda o desenho e as escolhas.

**A paleta em três lugares.**

- **`src/theme/tokens.css`:** os tokens, em `:root` e em `html[data-theme='dark']`. O `ThemeModeProvider` grava o
  `data-theme` no `<html>`. O `main.tsx` importa o arquivo uma vez, e ele vale para a página inteira, login incluído.
- **`src/theme.ts`:** os mesmos hexes, como paleta MUI. Hoje não há `contrastText`, e o MUI o calcula com
  `contrastThreshold = 3`. O branco vence sempre que passa de 3:1.
- **`src/features/auth/LoginPage.tsx`:** o objeto `C`, com 21 chaves fixas, porque o login é independente de tema por
  decisão. Ele lê a fonte do `body`, que vem do `tokens.css`, e nenhuma cor de token.

Fora desses três, nenhuma cor de marca aparece fixa no código. A varredura dos 13 hexes antigos fora deles volta vazia.
As únicas cores fixas sobre o acento são os dois `#fff`: o do `.fh-btn` e o do avatar.

**Onde o acento é texto sobre o tint.** São três lugares:

- o `NavItem` (`App.tsx:401-411`), com o texto e o ícone;
- o `RoleChip` (`UsersPage.tsx:393-395`), só o texto, sem ícone;
- o rádio do Agendamento (`IntegrationsPage.tsx:500-518`), com a moldura, a bolinha e o texto.

O ícone do card do tenant em Usuários (`UsersPage.tsx:178-179`) também é acento sobre tint. Mas é gráfico, e o limite
dele é 3:1: dá 4,24:1 e passa.

**As fontes hoje.**

- **Dois carregadores:** o `index.html` tem dois `preconnect` e um `<link>` do Google Fonts que pede a Inter, que nada
  usa. O `tokens.css` abre com um `@import` que pede a Manrope e a DM Mono.
- **Os números:** o `body` tem `font-feature-settings: 'tnum' 1`. Quase todo número das grades está na fonte do corpo:
  o CNPJ da coluna Empresa, as datas, o período, "Notas" e "Quando". Só três lugares usam `fh-mono`. Seis elementos
  pedem `fontVariantNumeric: 'tabular-nums'` inline: o período e o processadas do `GroupsPage`, três colunas do
  `GroupModal` e um campo do `IntegrationsPage`.

**As fontes novas, medidas.** A checagem foi com `fontTools`, nos arquivos servidos pelo Google Fonts: as features
numéricas e o `yMin` dos dígitos.

- **A Exo 2 (v26):** lining e com `tnum`.
- **A Raleway (v37):** old-style por padrão, com o 3, o 4, o 5, o 7 e o 9 cerca de 150 unidades abaixo da linha. Não tem
  `tnum`, só `lnum`. É por isso que ela saiu do corpo (doc de decisões, seção 5).
- **A Inter:** ainda não foi medida. Ela é documentada como lining por padrão e com `tnum`. A tarefa 4.1 mede, antes da
  troca.

**O dashboard e o host.** O host na 5200 é só a API. A tela roda no Vite, na 5173 (`docs/RUNNING.md:86`). O Vite serve
a `public/` na raiz em dev, e a copia para a raiz do `dist` no build. O `vite.config.ts` não define `base`, e por isso
`/brand/...` é o caminho certo nos dois casos.

**Os testes do dashboard.** São do Vitest (`npm test`), em node, ao lado do código (`*.test.ts`). O `tsc --noEmit` do
build também os verifica, e o `vite-env.d.ts` já traz os tipos do `vite/client`, que tipam o import `?raw`. O Vitest, por
padrão, não processa CSS: devolve string vazia para qualquer `.css`, `?raw` incluído.

## Goals / Non-Goals

**Goals:**

- Os três conjuntos de cor mudam na mesma fatia, e um teste impede o `theme.ts` de divergir do `tokens.css` depois dela.
- Nenhum par de texto sobre acento ou de acento sobre tint abaixo de 4,5:1, em nenhum tema, nem nas telas MUI.
- O CSS do login entra como está no doc. As três decisões de motion que custaram iteração (seção 4 do doc de decisões)
  não são desfeitas.

**Non-Goals:**

- O teste não cobre o objeto `C` do login. O `C` é privado ao módulo, e exportá-lo só para o teste alarga a superfície.
  A conferência do login é na tela.
- Nenhum teste de render nem de screenshot. O projeto não tem infraestrutura para isso, e o checklist na tela cobre o
  visual.

## Decisions

### D1. O `theme.ts` continua com hexes, e um teste o mantém igual ao `tokens.css`

O `createPalette` do MUI decompõe as cores: `augmentColor`, `alpha` e `getContrastText`. Uma string `var(--accent)`
quebra esse cálculo. O modo `cssVariables` do MUI v6 resolveria, mas é refactor, e o doc o deixa fora.

O guarda é um teste novo, o `src/theme/palette.test.ts`.

- **A leitura:** o teste lê o `tokens.css` por `import tokens from './tokens.css?raw'`, recorta os blocos `:root` e
  `html[data-theme='dark']` e lê os tokens de cada um. O Vitest troca todo CSS por string vazia, até o `?raw`. Por isso
  o `vite.config.ts` ganha `test.css.include` só para o `tokens.css`. Ler com `node:fs` pediria o `@types/node`, que o
  projeto não tem.
- **A sincronia:** ele compara o `createAppTheme(mode).palette` de cada tema com os tokens:

  | Paleta MUI | Token |
  |---|---|
  | `primary.main` | `--accent` |
  | `primary.dark` | `--accent-hover` |
  | `primary.light` | `--accent-tint` |
  | `primary.contrastText` | `--accent-ink` |
  | `text.primary` | `--ink` |

- **O contraste:** a razão do WCAG, calculada no próprio teste, sem dependência nova, MUST passar de 4,5:1 em três
  pares, nos dois temas:
  - `--accent-ink` sobre `--accent`;
  - `--accent-ink` sobre `--accent-hover`;
  - `--accent-on-tint` sobre `--accent-tint`.
- **Teste primeiro:** escrito antes da troca, ele falha hoje por dois motivos. O `--accent-ink` não existe. E o
  `contrastText` calculado é branco, enquanto o token do escuro será `#071020`.

**Alternativa descartada:** o `theme.ts` ler as variáveis por `getComputedStyle` na criação do tema. O tema seria criado
antes do CSS carregar, e o teste em node não teria DOM.

### D2. Os dois tokens de cor novos

| Token | Claro | Escuro | Trabalho |
|---|---|---|---|
| `--accent-ink` | `#ffffff` | `#071020` | a tinta sobre um preenchimento de acento |
| `--accent-on-tint` | `#005ba9` | `#6cbaf0` | o acento como texto ou ícone sobre o tint |

- **O `--accent-ink`** entra no `.fh-btn` (`tokens.css`) e no avatar da topbar (`App.tsx:281-283`). O `PrimaryButton`
  do login fica com o `#fff`: o formulário é sempre claro, e o branco sobre `#0072ce` dá 4,89:1.
- **O `--accent-on-tint`** entra nos três lugares da seção Context, no texto e no que o acompanha:
  - **no `NavItem`:** o texto e o ícone;
  - **no `RoleChip`:** o texto;
  - **no rádio:** o texto e a bolinha, que é a borda de 4 px do indicador.

  A moldura de 1 px do rádio marcado fica em `--accent`. Ela é a borda da seleção, como a borda de foco do `.fh-input`
  e o sublinhado das abas, e o limite dela é 3:1.
- **Por que não reaproveitar o `--accent-hover`,** que tem o mesmo valor hoje: o hover é token de estado. No dia em que
  o hover mudar, três textos mudariam junto, e o contraste voltaria a reprovar sem ninguém ver. Isso está no doc de
  decisões, seção 5.

### D3. O `primary.contrastText` do MUI é o espelho do `--accent-ink`

O `theme.ts` fixa o `primary.contrastText` em `#ffffff` no claro e `#071020` no escuro. Sem isso, o "Salvar" de
Configurações (`ConnectorsPage.tsx:374`) e o botão do `RewindMark` (`:97`) ficam com branco a 3,05:1 no escuro.

**Alternativa descartada:** subir o `contrastThreshold` para 4,5. Ele vale para todas as cores da paleta. Os botões e
chips preenchidos de success, warning, error e info trocariam de texto junto, e as cores de status ficam fora da change.

### D4. A tipografia

- **O `tokens.css`:**
  - o `@import` sai;
  - o `--font-sans` passa a `'Inter', system-ui, -apple-system, sans-serif`;
  - entra o `--font-display` novo.
- **O `theme.ts`:** o `typography.fontFamily` passa a usar a Inter. Configurações, que é MUI, passa a usar a Inter.
- **O carregamento: um `<link>` só, no `index.html`.** O `<link>` que hoje pede a Inter fica, e passa a pedir Exo 2,
  Inter e DM Mono. É o único lugar do projeto que carrega fonte.
  - **Por que um lugar só:** fonte em dois arquivos é a mesma armadilha da paleta em três (doc de decisões, seção 3).
  - **Por que o `<link>`, e não o `@import`:** o `<link>` está no HTML. O navegador o acha na primeira leitura da
    página, junto com os `preconnect` que já estão ali. O `@import` só é descoberto depois que o CSS do bundle chega, e
    isso acrescenta uma volta antes de a folha de fontes começar a baixar.
  - **Os dois `preconnect`** ficam.
- **O `--font-display`, aplicado por `fontFamily: 'var(--font-display)'`, só nestes lugares:**

  | Onde | Arquivo |
  |---|---|
  | o "Hub" da sidebar, peso 500 | `App.tsx` (bloco de marca) |
  | o título da topbar | `App.tsx:233` |
  | os números dos KPIs | `GroupsPage.tsx:97` (`Kpi`) e `:178` (Finalizados) |
  | o contador de agendamentos ativos | `IntegrationsPage.tsx:275` |
  | "Perfil de conector" | `ConnectorsPage.tsx:227`, por `sx`, só nesse `Typography` |
  | os títulos do login | `LoginPage.tsx:103` (headline) e `:218` (`Head`) |
  | o "Hub" do login, peso 500 | `LoginPage.tsx` (lockup) |

  O `h6` do tema MUI não muda. Hoje ele só aparece no "Perfil de conector", e o `sx` deixa o lugar explícito, como os
  outros da tabela, que são estilos inline. Um `h6` futuro decide a fonte dele.
- **O login também lê o token,** e não a pilha literal que o snippet do doc de execução traz para o "Hub". A fonte não
  varia por tema. Ler o `--font-display` não quebra a independência de tema do login, e a pilha literal seria um quarto
  lugar da tipografia.
- **Os números:** o `font-feature-settings: 'tnum' 1` do `body` não muda, nem os seis `tabular-nums` inline. A Inter,
  a Exo 2 e a DM Mono têm `tnum`. A Inter é lining por padrão, e a tarefa 4.1 confirma isso antes da troca. Se a medida
  não confirmar, a troca para, e a decisão volta ao doc.

### D5. O logo da sidebar escolhe o arquivo pelo `mode`

O `App.tsx` já tem o `mode` do `useThemeMode()` (`:64`). O `<img>` usa
`src={mode === 'dark' ? '/brand/logotipo-horizontal-branco.svg' : '/brand/logotipo-horizontal-colorido.svg'}`.

- **O desenho:**
  - o lockup com `height: 17`, `width` e `height` como atributos (130 × 17) para reservar o espaço, e `alt="Fiscosys"`;
  - o filete de 1 × 14 px em `var(--border-strong)`, o token dos divisores;
  - o "Hub" em `var(--font-display)`, 500, 14,5 px, `var(--muted)`, `lineHeight: 1`. O nome lido pelo leitor de tela
    fica "Fiscosys Hub";
  - a linha do lockup com `gap: 9`.
- **A estrutura:** o bloco continua com 73 px, a marca em cima e o `tenantId` embaixo, em mono. Sai o quadrado de
  28 × 28 e o `gap` que o separava do texto.
- **Os contrastes:** o "Hub" em `--muted` dá 4,55:1 no claro e 5,00:1 no escuro.

**Alternativas descartadas:**

- **Dois `<img>` alternados por CSS no `html[data-theme]`:** duplica a marcação, e o `mode` já está no escopo;
- **Um SVG inline com `currentColor`:** o lockup colorido tem dois azuis e um gradiente. Não é de uma cor só.

### D6. O nome de exibição muda só onde é exibição

| Arquivo | O que muda |
|---|---|
| `index.html` | `<title>FiscosysHub — Painel</title>` |
| `App.tsx` | o texto "FiscalHub" da sidebar dá lugar ao lockup + "Hub" (D5) |
| `LoginPage.tsx` | o lockup, o valor do card "Conector" ("FiscosysHub") e o rodapé ("© 2026 Fiscosys · Middleware de integração fiscal") |
| `FhDataGrid.tsx:4` | o comentário diz "DataGrid padrão do FiscosysHub" |
| `tokens.css:1-2` | o cabeçalho diz "Design System FiscosysHub", e a linha 2 descreve as fontes e o acento novos. Hoje ela diz "Manrope + DM Mono, acento petróleo", e ficaria falsa |

**O `roles.ts` não muda,** ao contrário da lista do pedido. O único "FiscalHub" dele é o caminho
`src/FiscalHub.Host/Program.cs`, que é o projeto C# e continua existindo. Trocar o texto faria o comentário apontar para
um arquivo que não existe, e contraria a própria regra do pedido: "namespaces C# continuam `FiscalHub.*`".

Ao final, `grep -rn FiscalHub dashboard/src dashboard/index.html` só acha essa linha.

### D7. O login: o CSS do doc e quatro pontos de JSX, mais três

- **O `login.css`:** é criado com o conteúdo da seção 3 do doc de execução, sem edição, e importado no topo do
  `LoginPage.tsx` (`import './login.css'`). Depois do apply, os ajustes do D9 e do D11 o mudaram, e o doc acompanha. É o primeiro CSS de feature do dashboard. O Vite o injeta globalmente, e
  todas as classes têm o prefixo `fh-login-`, sem colisão. Abaixo de 900 px, a regra do `tokens.css` (`display: none
  !important`) continua escondendo o painel, porque o `login.css` não declara `display`.
- **Os quatro pontos do doc** entram como estão:
  1. as camadas, e o painel sem o `background` inline;
  2. o `fh-login-content` nos três blocos;
  3. o lockup branco no lugar do "F";
  4. o `fh-login-flow` no lugar do `Dash()`.

  O `Dash()` sai, porque fica sem uso: o `noUnusedLocals` quebraria o build.
- **Os três pontos a mais:**
  - **O objeto `C`:** sete valores mudam, e a `brandBg` sai, conforme a tabela da seção 3 do doc de decisões;
  - **O rótulo do `StepCard`:** sai do `C.brandFaint` e vai para uma chave própria, a `C.stepLabel` (`#c3cfec`). Foi a
    terceira tentativa: o `brandMuted` também reprovou em janela baixa. A causa está no D12. O `brandFaint` continua no
    rodapé, onde dá 7,0:1;
  - **Os títulos:** o `--font-display` (D4).
- **Sem mudança no comportamento:** os três modos (login, esqueci e nova senha), os banners e o foco ficam como estão.

### D8. Os assets e os docs entram como estão, com as correções dos docs

- **Os sete arquivos de `public/brand/`** são commitados sem reprocessar. Os tamanhos conferem com a tabela da seção 1
  do doc de execução. Nenhum tem `<script>`, `<style>`, `mix-blend-mode` nem `<metadata>`.
- **Os dois docs** entram com a change, porque a change os cita como fonte. Eles já trazem a Inter, o `contrastText` e o
  rótulo do `StepCard`. Faltam três correções:
  - **o contraste do `--accent-ink` no escuro é 6,25:1, e não 12,1:1.** O valor errado aparece na seção 5 do doc de
    decisões e no item "Dois achados" do prompt, no doc de execução;
  - **as linhas que ficaram do plano anterior.** Elas contradizem a seção 5 do próprio doc:

    | Doc | Linha | O que diz |
    |---|---|---|
    | decisões | 297 | o `<link>` da Inter sai |
    | decisões | 306 | o `@import` do `tokens.css` fica |
    | execução | 140 | o `theme.ts` com a Raleway |
    | execução | 193 | a Inter sem uso como achado corrigido |
    | execução | 409-411 | o `<link>` da Inter sai |

  - **o `roles.ts` que fica (D6):** os dois docs o listam entre os arquivos que trocam o nome (decisões, linha 352;
    execução, linha 183).

### D9. O autofill do Chrome: a regra global, o espelho MUI e o login

O Chrome pinta o campo que ele preenche com um fundo próprio, com `!important` interno. Nem `background-color` nem
`color` o sobrescrevem. O jeito é uma sombra inset que cobre o campo e o `-webkit-text-fill-color`.

- **A regra global, no `tokens.css`:** `input:-webkit-autofill` (e `:hover`, `:focus`, `:active`) com a sombra em
  `var(--surface)` e o texto em `var(--ink)`. Ela cobre o `.fh-input` e os inputs inline do `UsersPage`.
- **O anel de foco do `.fh-input`:** ele também é `box-shadow`. A regra global de `:focus` tem especificidade (0,2,1),
  maior que a do `.fh-input:focus`, (0,2,0), e apagaria o anel. Uma regra `.fh-input:-webkit-autofill:focus` põe as
  duas sombras juntas.
- **O MUI tem regra própria,** no `OutlinedInput`: no escuro, `#266798` com texto branco, num seletor
  `.css-…:-webkit-autofill` de especificidade (0,2,0), que vence a global. O espelho vai no `theme.ts`, como o
  `contrastText` (D3): o `styleOverrides.input` do `MuiOutlinedInput` usa `background.paper` e `text.primary`, que são
  os mesmos valores do `--surface` e do `--ink`.
- **O login tem a dele, no `login.css`:** `.fh-login-input:-webkit-autofill`, em `#fff` e `#001a72`, porque o formulário
  é sempre claro, e a regra global daria um campo escuro se o app tivesse ficado no tema escuro. A classe entra no
  `<input>` do `IconInput`, que é o componente dos campos do login.
  - **O `!important`:** o `IconInput` põe o `box-shadow` inline, `none` sem foco e o anel com foco. Inline vence
    qualquer regra de folha sem `!important`. Sem ele, a regra do login não teria efeito nenhum.
  - **O foco:** a regra de foco do login soma o anel de 3 px (`#e6f0fb`) à sombra que cobre o campo.

**Alternativa descartada:** tirar o anel de foco do inline do `IconInput` e passá-lo para o `login.css`. Evitaria o
`!important`, mas mexe na estrutura do componente, o estado `focused` e a borda, numa change que não muda componente.

### D10. As colunas do login: 55% para o painel e 45% para o formulário

Antes, o painel tinha `width: 44%` e `maxWidth: 620`, e o formulário, `flex: 1`. Em tela larga, o painel parava em
620 px, e o formulário ficava com o resto, invertendo a proporção.

- **O painel:** `flex: '1 1 auto'` e `minWidth: 0`. Fica com o que sobra.
- **O formulário:** `flex: '0 0 max(420px, 45%)'`. O painel fica com 55% em qualquer largura, e o formulário nunca fica
  abaixo de 420 px. Em 920 px de largura, o mínimo vale, e o painel fica com 54,3%.
- **A primeira versão** usava `clamp(420px, 38%, 560px)`. O teto de 560 px fazia o painel crescer com a tela: 62% em
  1440 e 71% em 1920. O Marcelo achou grande demais e pediu "ligeiramente maior que a metade". A proporção fixa, sem
  teto, é o que mantém a relação igual em qualquer tela.
- **Abaixo de 900 px:** o painel some, como antes. Mas o formulário, com a coluna fixa, ficaria com 420 px encostados à
  esquerda, e a faixa vazia à direita ficaria na cor do `--page`, escura se o tema guardado fosse o escuro. Num celular
  de 390 px, a coluna estouraria a largura. Por isso, a coluna ganha a classe `fh-login-form`, e o mesmo `@media` do
  `tokens.css` que esconde o painel lhe devolve `flex: 1 1 auto !important`. O `!important` vence o `flex` inline.

### D11. As trilhas abrem a faixa do conteúdo, em espelho, e os nós acompanham

As trilhas têm posição percentual, e o bloco de conteúdo é centrado na vertical. Quanto mais baixa a janela, mais o
conteúdo ocupa em porcentagem: os cards descem, e a headline sobe. Em 760 px de altura, o conteúdo vai de 30,1% a 70,3%.

- **O grupo de baixo, como pedido:** a `t4` passa de 66% a 73%, e a `t5`, de 78% a 82%. A `t6` fica em 90%.
- **O grupo de cima, em espelho:** a medida mostrou o mesmo problema em cima. A `t3` e o `n1`, a 34%, cortavam a
  headline por dentro: −7 px em 900 de altura, −23 px em 800 e −29 px em 760. A `t2` passa de 22% a 18%, e a `t3`, de
  34% a 27%. A `t1` fica em 10%. Os dois grupos ficam simétricos: 10/18/27 e 73/82/90.
- **Os nós:** o `n1` acompanha a `t3`, e o `n2`, a `t4`. Sem isso, um ponto ficaria solto no meio do painel.
- **O comentário** do `login.css` deixa de dizer "evitam a faixa de 40%–62%" e explica o motivo.

**O resultado medido**, de 760 a 1080 px de altura e de 1000 a 1920 px de largura: nenhuma trilha nem nó corta o
lockup, a headline, o parágrafo, os cards ou o rodapé. A menor folga é de 15 px, a da `t1` até o lockup em 760 px de
altura, que não mudou.

### D12. O rótulo do card: a causa estrutural e a chave `stepLabel`

O rótulo dos cards do fluxo (ENTRADA, CONECTOR e SAÍDA) precisou de três tentativas. A razão foi a mesma nas três.

- **O fundo do card é branco translúcido:** o `C.cardBg` é `rgba(255,255,255,0.035)`. O mockup do canvas tinha 0,05,
  e as medições são do 0,035, que é o que roda. Ele clareia o que estiver atrás dele.
- **O que está atrás depende de três variáveis:**
  - **a intensidade do brilho:** o `glow-b` (0,24 no centro);
  - **a largura do painel:** o centro do brilho fica a 74% da largura, e os cards começam na margem esquerda, com no
    máximo 460 px. Quanto mais estreito o painel, mais o brilho cai sobre o card "Saída";
  - **a altura da janela:** o centro do brilho fica a 64% da altura, e o conteúdo é centrado na vertical. Em janela
    baixa, os cards descem para essa faixa.

  Por isso uma medida num tamanho de janela só não basta.
- **As três tentativas:**
  1. **`brandFaint` (`#8ea1d2`):** passava sobre o navy puro, com 5,9:1, e reprovava sob o brilho, com ~4,1:1;
  2. **`brandMuted` (`#aebfe6`):** passou em 1360 × 900, com 4,92, e reprovou com 760 px de altura, com 4,16 a 4,18;
  3. **`stepLabel` (`#c3cfec`):** passa em todas as janelas medidas, com 4,84 no pior caso.
- **A chave tem nome de trabalho, e não de aparência.** Ela se chama `stepLabel`, e não "um `brandMuted` mais claro",
  e tem um comentário de uma linha: ela existe porque o rótulo fica sobre o brilho e o parágrafo não. Sem isso, alguém
  veria `#aebfe6` e `#c3cfec` lado a lado, acharia que é duplicação e consolidaria, e o contraste voltaria a reprovar.
- **As alternativas medidas** (pior caso em 1000 × 760, 1360 × 760 e 1360 × 900):

  | Opção | Resultado | Por que não |
  |---|---|---|
  | rótulo em `#fcfcfc` | 7,48 / 7,51 / 8,88 | fica tão claro quanto o valor abaixo dele, e a hierarquia some |
  | `glow-b` mais baixo, a 80% | 4,53 / 4,53 / 5,45 | passa por pouco, e muda a composição do painel |
  | `glow-b` mais fraco, a 0,16 | 4,47 / 4,53 / 5,35 | ainda reprova em 1000 × 760 |
  | **`stepLabel` `#c3cfec`** | **4,92 / 4,94 / 5,87** | escolhida: o brilho não muda, e a hierarquia fica |

## Risks / Trade-offs

- **[A Inter tem métricas diferentes das da Manrope]** → Rótulos e células podem mudar de largura na grade e na sidebar.
  A conferência na tela olha a grade do Fiscal e a sidebar nos dois temas. Se algo truncar, o achado é registrado, e o
  layout não muda nesta change.
- **[A Inter não confirmar lining + `tnum` na medida]** → A tarefa 4.1 mede antes da troca. Se não confirmar, a troca
  para, e a escolha volta ao doc de decisões.
- **[O Google Fonts fora do ar ou bloqueado]** → As fontes caem para `system-ui`, como hoje acontece com a Manrope. Nada
  quebra.
- **[Mexer no brilho, na largura do painel ou no fundo do card sem medir o rótulo]** → O contraste do rótulo depende
  dessas três coisas (D12). Hoje a folga é pequena: 4,84:1 no pior caso. Qualquer mudança no `glow-b`, nas colunas ou no
  `cardBg` pede a medição de novo, com o roteiro do pior momento da deriva, em 760 px de altura.
- **[A animação do login e o desempenho]** → São 8 elementos absolutos, animando `transform`, `opacity` e
  `background-position`, só na tela de login. O "reduzir movimento" desliga tudo.
- **[O `theme.ts` e o `tokens.css` divergirem no futuro]** → O teste do D1 quebra na hora.

## Migration Plan

Não há migração de dado nem de API. A mudança é do bundle do dashboard. O rollback é reverter o commit. Os assets em
`public/brand/` e o `login.css` são arquivos novos, e não sobrescrevem nada.
