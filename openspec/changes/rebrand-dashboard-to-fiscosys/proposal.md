## Why

O dashboard passa a usar a identidade visual da Fiscosys, e o produto passa a se chamar **FiscosysHub** na tela. As
decisões foram aprovadas em 2026-10-08 e estão em `docs/rebranding-fiscosys.md`. O CSS pronto do login e o checklist
estão em `docs/rebranding-fiscosys-execucao.md`.

A troca não é só de cor:

- **A paleta está em três lugares, não em um.**
  - `tokens.css` pinta os componentes próprios.
  - `theme.ts` repete os mesmos hexes para tudo que é MUI. A tela de Configurações é inteira MUI.
  - O objeto `C` do `LoginPage` é um terceiro conjunto.

  Trocar um só deixa Configurações e o login com o acento petróleo antigo.
- **A paleta nova abre um buraco de contraste.** O texto `--accent` sobre `--accent-tint` cai de 6,47:1 para 4,24:1 no
  tema claro, abaixo do mínimo de 4,5:1. Isso atinge o item ativo da sidebar, o chip Admin e o rádio marcado do
  Agendamento.
- **Um defeito anterior ao rebranding aparece no caminho.** O `.fh-btn` e o avatar da topbar fixam o texto em `#fff`
  sobre `--accent`. No tema escuro de hoje, isso dá 3,08:1, e o azul novo repetiria o problema (3,05:1). Os botões MUI
  `contained` de Configurações têm o mesmo defeito, por outro caminho: o MUI escolhe o branco sozinho, porque aceita a
  partir de 3:1.
- **A fonte de corpo do manual não serve à interface.** O manual pede Raleway Light. A Raleway é face de display, tem
  dígitos old-style por padrão e não tem `tnum`. No campo ERP de Configurações, que mostra "Dynamics365", o 3 e o 5
  cairiam abaixo da linha. O detalhe está na seção 5 do doc de decisões.

## What Changes

- **O nome de exibição muda.** "FiscalHub" vira "FiscosysHub" no título da aba, na sidebar (lockup + "Hub"), no
  painel do login e no cabeçalho do `tokens.css`. O rodapé do login passa a dizer "© 2026 Fiscosys". Os namespaces C#,
  os caminhos de projeto e o nome do repositório continuam `FiscalHub`.
- **A paleta da marca, nos três lugares de uma vez.**
  - **No `tokens.css`, nos dois temas:** mudam `--ink`, `--accent`, `--accent-hover`, `--accent-tint` e `--accent-ring`.
  - **No `theme.ts`:** o espelho MUI muda (`primary.main`, `primary.dark`, `primary.light` e `text.primary`).
  - **No objeto `C` do login:** sete chaves mudam, e a `brandBg` sai, porque o fundo do painel passa a morar no
    `login.css`.
  - **O que não muda:** superfícies, bordas, neutros, os 21 tokens de status, raios, `--control-h` e as três sombras.
- **Dois tokens de cor novos.**
  - **`--accent-ink`** é a tinta que vai sobre o acento. Branco no claro, `#071020` no escuro. Entra no `.fh-btn` e no
    avatar da topbar, e o escuro sobe de 3,08:1 para 6,25:1. No `theme.ts`, o espelho dele é o `primary.contrastText`,
    fixado nos mesmos dois valores para os botões MUI.
  - **`--accent-on-tint`** é o acento usado como texto sobre o tint. `#005ba9` no claro, `#6cbaf0` no escuro. Entra no
    texto e no ícone do item ativo da sidebar, do chip Admin e do rádio marcado. O claro vai a 5,94:1, e o escuro a
    7,26:1.
- **A tipografia.**
  - **Corpo:** Inter 400, no lugar da Manrope. Ela foi desenhada para interface densa, tem dígitos lining por padrão e
    tem `tnum`. Foi comparada com a IBM Plex Sans e a Manrope nas strings reais do produto. O `font-feature-settings:
    'tnum' 1` do `body` fica como está.
  - **Display:** Exo 2, num token novo `--font-display`, só onde já há peso 700 de título ou número. São o título da
    topbar, os números dos KPIs, o contador de agendamentos ativos, o "Perfil de conector" e os títulos do login. O
    "Hub" da sidebar também usa o display, no peso 500.
  - **Mono:** DM Mono continua.
  - **Um lugar só carrega fonte.** O `<link>` do Google Fonts no `index.html` fica, e passa a pedir Exo 2, Inter e DM
    Mono. Hoje ele pede a Inter, que nada usa. O `@import` do `tokens.css` sai.
- **A marca.**
  - **Na sidebar:** o quadrado com a letra "F" dá lugar ao lockup horizontal, colorido no tema claro e branco no escuro.
  - **No login:** o quadrado `#2f7f97` dá lugar ao lockup branco.
  - **Favicon:** SVG, PNG 32 e apple-touch-icon.
  - **Os sete assets de `dashboard/public/brand/`** entram no repositório como estão.
- **O painel de marca do login ganha o fundo animado em CSS.** Seis camadas, num arquivo novo `login.css`: o
  gradiente, dois brilhos, o grid de colunas, a marca d'água, seis trilhas de fluxo e dois nós. O tracejado entre os cards
  anda. Com "reduzir movimento" ligado no sistema, nada anima. Os rótulos dos cards ganham um tom próprio, o
  `stepLabel` (`#c3cfec`). Eles ficam sobre o segundo brilho e o fundo translúcido do card, onde o `brandFaint` e depois
  o `brandMuted` reprovaram em janela baixa.
- **Três ajustes no login, depois do apply.**
  - **O autofill do Chrome:** o campo preenchido pelo navegador mantém as cores do tema. Uma regra global vale para o
    app. O MUI ganha o espelho dela no `theme.ts`, e o login tem a sua, sempre clara.
  - **As colunas:** o painel fica com 55% e o formulário com 45%, no mínimo 420 px. Antes, o painel travava em 620 px,
    e em tela larga o formulário ficava maior que ele.
  - **As trilhas:** as de baixo descem, e as de cima sobem em espelho. Em janela baixa, elas cortavam os cards e a
    headline.
- **Um teste guarda a paleta duplicada.** Ele afirma que o espelho MUI do `theme.ts` tem os mesmos valores do `tokens.css`
  nos dois temas, `contrastText` incluído. Afirma também que os pares de contraste dos tokens novos passam de 4,5:1.
- **Os dois docs do rebranding entram no repositório, com duas correções.**
  - O contraste do `--accent-ink`: os docs dizem 12,1:1, e o valor medido é 6,25:1.
  - As linhas que ficaram do plano anterior: as que tiram o `<link>` da Inter, a que mantém o `@import` e a do
    `theme.ts` com a Raleway.

## Capabilities

### New Capabilities

- `dashboard-brand-identity`: a identidade visual do dashboard. Cobre o nome de exibição, a paleta da marca e a
  coerência entre os três conjuntos, a tipografia, a marca na sidebar, o login e o favicon. Cobre também os contrastes
  mínimos do texto sobre o acento e do acento sobre o tint, e o movimento do login sob "reduzir movimento".

### Modified Capabilities

Nenhuma. A `module-navigation` descreve o que a sidebar lista, e não como ela se pinta. Os requisitos dela não mudam.

## Non-goals

- **Redesenho.** Nenhuma mudança de layout, de estrutura de componente ou de comportamento. A altura de 73 px e a ordem
  do bloco de marca da sidebar ficam.
- **Renomear o código.** Namespaces C#, projetos, o pacote npm `fiscalhub-dashboard`, o `dashboard/README.md`, o
  `CLAUDE.md`, as ADRs e o `openspec/` continuam com "FiscalHub". O comentário do `roles.ts` também fica: o único
  "FiscalHub" dele é o caminho `src/FiscalHub.Host/Program.cs`, que continua existindo (design, D6).
- **Fazer o `theme.ts` ler as variáveis CSS.** É refactor, e não rebranding. Nesta change, o teste de sincronia é o que
  impede os dois de divergirem.
- **As cores de status, os neutros, as superfícies, as bordas, os raios e as sombras.**
- **O `PrimaryButton` do login.** O formulário do login é sempre claro, e o branco sobre `#0072ce` dá 4,89:1, que passa.
- **A Raleway.** Ela sai do dashboard, e continua valendo para peça impressa e marketing.
- **Hospedar as fontes no repositório.** Continua o Google Fonts. Os TTFs da agência ficam fora do repo.
- **A sidebar recolhida.** Ela não existe. O `simbolo-colorido.svg` entra com os assets, mas sem uso.
- **Backend.** Nada muda no .NET: nem Domain, nem Application, nem Infrastructure, nem Adapters, nem Host.
- **ADR.** Não há decisão de arquitetura. O registro das decisões de marca é o `docs/rebranding-fiscosys.md`, que entra
  com a change.

## Impact

- **Dashboard, arquivos alterados:**
  - `index.html`: título, favicons e as três famílias no `<link>` de fontes;
  - `src/theme/tokens.css`: tokens, a saída do `@import`, `.fh-btn` e cabeçalho;
  - `src/theme.ts`: paleta, `contrastText` e fonte;
  - `src/App.tsx`: bloco de marca, título da topbar, avatar, `NavItem`;
  - `src/features/auth/LoginPage.tsx`;
  - `src/features/groups/GroupsPage.tsx`;
  - `src/features/integrations/IntegrationsPage.tsx`: contador e rádio;
  - `src/features/connectors/ConnectorsPage.tsx`;
  - `src/features/users/UsersPage.tsx`: `RoleChip`;
  - `src/components/FhDataGrid.tsx`: comentário;
  - `vite.config.ts`: o `test.css.include` do `tokens.css`, para o teste da paleta ler o arquivo.
- **Dashboard, arquivos novos:**
  - `src/features/auth/login.css`;
  - `src/theme/palette.test.ts`;
  - os sete arquivos de `public/brand/`.
- **Docs:** `docs/rebranding-fiscosys.md` e `docs/rebranding-fiscosys-execucao.md` entram no repositório.
- **Rede:** hoje são duas folhas do Google Fonts, a Inter pelo `<link>` e a Manrope com a DM Mono pelo `@import`. Passa
  a ser uma só, pelo `<link>`, com Exo 2, Inter e DM Mono.
- **Sem impacto:** API, banco, mensageria e o lado D365.
