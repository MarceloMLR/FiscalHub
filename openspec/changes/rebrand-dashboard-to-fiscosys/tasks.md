A ordem mantém a tela coerente a cada grupo:

- **Grupo 1:** o teste da paleta, primeiro, falhando pelos motivos certos.
- **Grupo 2:** a paleta e o espelho MUI mudam juntos, e o teste passa.
- **Grupo 3:** os dois tokens novos chegam aos componentes.
- **Grupo 4:** a tipografia.
- **Grupo 5:** a marca e o nome.
- **Grupo 6:** o login.
- **Grupo 7:** os docs.
- **Grupo 8:** a verificação na tela, nos dois temas.

Cada grupo de código termina com o `npm test` e o `npm run build` verdes, no `dashboard/`. O build roda o `tsc --noEmit`:
nenhum erro e nenhum símbolo sem uso. O .NET não muda. O `dotnet build` e o `dotnet test` rodam uma vez, no grupo 8, como
guarda. Uma tarefa só recebe `[x]` com evidência.

## 1. O guarda da paleta, teste primeiro (D1; "Todas as telas usam a mesma paleta da marca", "Texto sobre o acento e acento sobre o tint são legíveis")

- [x] 1.1 Escrever o `dashboard/src/theme/palette.test.ts`:
  - **a leitura:** importa o `tokens.css` por `?raw`, recorta os blocos `:root` e `html[data-theme='dark']` e lê os
    tokens de cada um;
  - **a sincronia, por tema:** `createAppTheme(mode).palette` com `primary.main` = `--accent`,
    `primary.dark` = `--accent-hover`, `primary.light` = `--accent-tint`, `primary.contrastText` = `--accent-ink` e
    `text.primary` = `--ink`. A comparação ignora a caixa do hex;
  - **o contraste, por tema:** a razão do WCAG, calculada no teste, passa de 4,5:1 em `--accent-ink` sobre `--accent`,
    `--accent-ink` sobre `--accent-hover` e `--accent-on-tint` sobre `--accent-tint`;
  - **um token ausente** falha com o nome do token e do tema, e não com `undefined`.
- [x] 1.2 Rodar o `npm test` e anotar as falhas, que são as esperadas: o `--accent-ink` e o `--accent-on-tint` ausentes, e
  o `contrastText` branco no escuro. Nenhuma outra falha.

  **Feito (2026-10-09):** 8 falharam e 58 passaram, de 66.
  - **As 8 falhas** são todas do `palette.test.ts`: o `contrastText` e os três pares de contraste, nos dois temas, com
    "o tema … não define --accent-ink" ou "--accent-on-tint". O `contrastText` branco do escuro ainda não aparece: ele
    só aparece depois que o token existir, e a 2.4 o fecha.
  - **As 8 sincronias de hoje passam** (main, dark, light e text.primary), e as outras 6 suítes também.
  - **Um desvio do D1:** o Vitest troca todo CSS por string vazia, até o `?raw`, e o teste falhava com "o tokens.css
    não tem o bloco :root". O `vite.config.ts` ganhou `test.css.include` só para o `tokens.css`.

## 2. A paleta nos três lugares: tokens e MUI (D1, D3; "Todas as telas usam a mesma paleta da marca")

- [x] 2.1 No `tokens.css`, no `:root`:
  - `--ink` `#001a72`;
  - `--accent` `#0072ce`;
  - `--accent-hover` `#005ba9`;
  - `--accent-tint` `#e6f0fb`;
  - `--accent-ring` `#e6f0fb`;
  - novos: `--accent-ink` `#ffffff` e `--accent-on-tint` `#005ba9`.
- [x] 2.2 No `tokens.css`, no `html[data-theme='dark']`:
  - `--ink` `#fcfcfc`;
  - `--accent` `#3b9ae1`;
  - `--accent-hover` `#6cbaf0`;
  - `--accent-tint` `#10263d`;
  - `--accent-ring` `#10263d`;
  - novos: `--accent-ink` `#071020` e `--accent-on-tint` `#6cbaf0`.
- [x] 2.3 No `.fh-btn`, `color: #fff` vira `color: var(--accent-ink)`.
- [x] 2.4 No `theme.ts`, nos dois temas: `primary.main`, `primary.dark`, `primary.light` e `text.primary` com os valores
  das tabelas da seção 3 do doc de decisões. Entra também o `primary.contrastText`, `#ffffff` no claro e `#071020` no
  escuro. O comentário do topo diz que o `palette.test.ts` guarda a sincronia com o `tokens.css`.
- [x] 2.5 Conferir que nada mais mudou no `tokens.css`: superfícies, bordas, neutros, os 21 tokens de status, raios,
  `--control-h` e as três sombras. A prova é o `git diff` do arquivo, que só toca as linhas das 2.1 a 2.3.
- [x] 2.6 O `npm test` fica verde, com o `palette.test.ts` passando, e o `npm run build` também.

## 3. Os tokens novos nos componentes (D2; "Texto sobre o acento e acento sobre o tint são legíveis")

- [x] 3.1 O avatar da topbar (`App.tsx:281-283`): `color: '#fff'` vira `'var(--accent-ink)'`.
- [x] 3.2 O `NavItem` (`App.tsx`): no item ativo, o texto e o `span` do ícone passam de `var(--accent)` a
  `var(--accent-on-tint)`. O fundo continua `var(--accent-tint)`.
- [x] 3.3 O `RoleChip` (`UsersPage.tsx`): o texto do Admin passa a `var(--accent-on-tint)`. O fundo e a borda ficam.
- [x] 3.4 O rádio do Agendamento (`IntegrationsPage.tsx`): no marcado, o texto e a borda de 4 px da bolinha passam a
  `var(--accent-on-tint)`. A moldura de 1 px fica em `var(--accent)`.
- [x] 3.5 O ícone do card do tenant em Usuários (`UsersPage.tsx:178-179`) e o `PrimaryButton` do login ficam como estão.
  Conferir que o diff não os toca.
- [x] 3.6 O `npm test` e o `npm run build` ficam verdes.

## 4. A tipografia (D4; "A tipografia da marca")

- [x] 4.1 Medir a Inter antes da troca, com a mesma checagem por `fontTools` que mediu a Raleway e a Exo 2:
  - **o arquivo:** o woff2 do subconjunto latino da Inter 400 que a URL nova do `<link>` serve. Pedir a folha com um
    User-Agent de navegador, senão o Google Fonts serve TTF;
  - **as ferramentas:** o `fonttools` e o `brotli`, instalados numa pasta temporária, fora do repositório;
  - **o que medir:** as features numéricas do GSUB (`lnum`, `onum`, `tnum` e `pnum`) e o `yMin` dos glifos de 0 a 9
    pelo `cmap` padrão;
  - **o esperado:** o `tnum` presente, e o 3, o 4, o 5, o 7 e o 9 sem descida. O `yMin` deles fica perto de 0, como o
    dos demais, só com o overshoot dos dígitos redondos;
  - **a evidência:** a saída da medição fica anotada na tarefa, com a versão da fonte servida.

  Se a Inter não confirmar lining e `tnum`, o grupo para aqui, e a escolha volta ao doc de decisões.

  **Feito (2026-10-09):** a Inter confirma lining e `tnum`.
  - **O arquivo:** `fonts.gstatic.com/s/inter/v20/UcC73FwrK3iLTeHuS_nVMrMxCp50SjIa1ZL7.woff2`, servido pela URL nova
    do `<link>`. É a "Version 4.001", variável, com o eixo `wght` de 100 a 900 e padrão 400, num arquivo só para os
    quatro pesos. São 2048 unidades por em.
  - **As features numéricas:** `tnum` e `pnum`. Não há `lnum` nem `onum`: não existe versão old-style dos dígitos.
  - **O `yMin` de 0 a 9, no padrão:** `[-20, 0, 0, -20, 0, -20, -20, 0, -20, -20]`. O 3, o 4, o 5, o 7 e o 9 dão
    -20, 0, -20, 0 e -20. É só o overshoot das curvas, ~1% do em. Na Raleway era ~-150 em 1000, ~15%.
  - **As larguras:** no padrão, os dígitos são proporcionais, de 833 a 1323. Com o `tnum`, viram `zero.tf` a
    `nine.tf`, todos com 1328. O `'tnum' 1` do `body` é o que faz as colunas alinharem.
- [x] 4.2 No `tokens.css`:
  - sai o `@import`;
  - o `--font-sans` vira `'Inter', system-ui, -apple-system, sans-serif`;
  - entra o `--font-display`: `'Exo 2', system-ui, sans-serif`;
  - o `--font-mono` fica.

  O `font-feature-settings: 'tnum' 1` do `body` não muda.
- [x] 4.3 No `theme.ts`, o `typography.fontFamily` passa a
  `'Inter, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif'`.
- [x] 4.4 No `index.html`, o `<link>` do Google Fonts fica, e a URL passa à da seção 3 do doc de decisões:
  `Exo+2:wght@500;600;700`, `Inter:wght@400;500;600;700` e `DM+Mono:wght@400;500`, com `display=swap`. Os dois
  `preconnect` ficam.
- [x] 4.5 O `fontFamily: 'var(--font-display)'` entra nos lugares da tabela do D4, e em nenhum outro:
  - o título da topbar;
  - os dois números de KPI do `GroupsPage`;
  - o contador do `IntegrationsPage`;
  - o `Typography` do "Perfil de conector", por `sx`.

  O "Hub" da sidebar e os títulos do login entram nos grupos 5 e 6.
- [x] 4.6 Os dois greps:
  - o `grep -rnE "Manrope|Raleway|@import" dashboard/src dashboard/index.html` volta vazio;
  - o `grep -rn "fonts.googleapis.com/css2" dashboard/src dashboard/index.html` só acha o `<link>` do `index.html`.
- [x] 4.7 O `npm test` e o `npm run build` ficam verdes.

## 5. A marca e o nome (D5, D6, D8; "O nome de exibição é FiscosysHub", "O logo na sidebar acompanha o tema", "O favicon da Fiscosys")

- [x] 5.1 Os sete arquivos de `dashboard/public/brand/` entram no commit da change, sem reprocessar. Antes, conferir os
  tamanhos contra a tabela da seção 1 do doc de execução (4.112, 4.109, 1.073, 741, 719, 580 e 2.725 B), e que nenhum
  tem `<metadata>`, `<style>` ou `<script>`.
- [x] 5.2 No `index.html`:
  - o `<title>` passa a `FiscosysHub — Painel`;
  - entram os três `<link>` de ícone da seção 4 do doc de execução: o SVG, o PNG 32 e o apple-touch-icon 180.
- [x] 5.3 O bloco de marca da sidebar (`App.tsx`), conforme o D5:
  - sai o quadrado de 28 × 28 com o "F";
  - entram o lockup pelo `mode`, o filete em `--border-strong` e o "Hub" em `--font-display`;
  - o `tenantId` continua embaixo, em mono;
  - a altura fica em 73 px.
- [x] 5.4 O comentário do `FhDataGrid.tsx:4` e o cabeçalho do `tokens.css`, as linhas 1 e 2: "FiscosysHub", e a
  descrição com as fontes e o acento novos.
- [x] 5.5 O `roles.ts` não muda (D6). O `grep -rn FiscalHub dashboard/src dashboard/index.html` só acha o caminho
  `src/FiscalHub.Host/Program.cs` do `roles.ts`. A saída entra como evidência.

  **Feito (2026-10-09), depois do grupo 6:** a saída é uma linha só,
  `dashboard/src/features/auth/roles.ts:2: … (rawTraceRoles, em src/FiscalHub.Host/Program.cs) …`.
- [x] 5.6 O `npm test` e o `npm run build` ficam verdes. O `dist/brand/` tem os sete arquivos.

## 6. O login (D7; "O painel de marca do login", "O nome de exibição é FiscosysHub")

- [x] 6.1 Criar o `dashboard/src/features/auth/login.css` com o bloco CSS da seção 3 do doc de execução, sem edição, e
  importá-lo no topo do `LoginPage.tsx`.
- [x] 6.2 O objeto `C`:
  - mudam `brandText`, `brandMuted`, `brandFaint`, `ink`, `accent`, `accentHover` e `ring`, com os valores da tabela da
    seção 3 do doc de decisões;
  - a `brandBg` sai;
  - as outras chaves ficam.
- [x] 6.3 Os quatro pontos de JSX da seção 3 do doc de execução:
  - o painel sem o `background` inline, com as camadas;
  - o `fh-login-content` nos três blocos;
  - o lockup branco, com o filete e o "Hub", no lugar do "F";
  - o `fh-login-flow` no lugar do `Dash()`, que sai.

  O "Hub" usa `var(--font-display)` (D4), e não a pilha literal do snippet.
- [x] 6.4 Os três pontos a mais do D7:
  - o rótulo do `StepCard` passa de `C.brandFaint` a `C.brandMuted`. Depois, a 9.9 o passou para `C.stepLabel`;
  - a headline do painel e o título do `Head` ganham `var(--font-display)`;
  - o valor do card "Conector" passa a "FiscosysHub", e o rodapé a "© 2026 Fiscosys · Middleware de integração fiscal".
- [x] 6.5 O `grep -rnE "0e2a35|2f7f97|brandBg" dashboard/src` volta vazio.
- [x] 6.6 O `npm test` e o `npm run build` ficam verdes.

## 7. Os docs (D8)

- [x] 7.1 Em `docs/rebranding-fiscosys.md`:
  - **na seção 5:** o contraste do `--accent-ink` no escuro passa de 12,1:1 a 6,25:1;
  - **na seção 6:** o item "O `index.html` carrega Inter… O link sai" passa a dizer que o `<link>` fica, com as três
    famílias, porque a Inter é o corpo;
  - **na seção 7:** a decisão "manter o `@import` do Google Fonts, como o `tokens.css` já faz" passa a "o `<link>` do
    `index.html`, que é o único carregador";
  - **na seção 8:** o `roles.ts` sai da lista, com uma linha sobre o porquê (D6).
- [x] 7.2 Em `docs/rebranding-fiscosys-execucao.md`:
  - **no prompt:**
    - a linha do `typography.fontFamily` passa da Raleway à Inter;
    - o item "Dois achados" passa a 6,25:1;
    - a linha "index.html carregava Inter sem nenhum uso" passa a dizer que esse `<link>` virou o carregador das três
      famílias;
    - o `roles.ts` sai da lista de nomes;
  - **na seção 4:** o bloco "Sai:" com o `<link>` da Inter passa a "Muda:", com o `<link>` das três famílias.
- [x] 7.3 Nenhuma linha dos dois docs manda remover a Inter, manter o `@import` ou usar a Raleway no dashboard. A
  prova é um grep por "Raleway", "@import" e "Inter" nos dois, com cada ocorrência que sobra coerente com a seção 5.
- [x] 7.4 Os dois docs entram no commit da change.

## 8. Verificação (o checklist da seção 5 do doc de execução)

- [x] 8.1 No `dashboard/`, o `npm test` e o `npm run build` ficam verdes. Na raiz, o `dotnet build` termina com 0
  warnings, e o `dotnet test` fica verde, como guarda: o .NET não muda.

  **Feito (2026-10-09):**
  - **O dashboard:** 66 testes passaram, de 66, e o build passou. O build segue avisando que um chunk passa de 500 kB,
    como já avisava antes da change.
  - **O .NET:** o `dotnet build` termina com 0 warnings e 0 erros. O `dotnet test` dá 1.212 aprovados, 3 pulados e 0
    falhas, de 1.215. Os pulados são os dois contra o F&O real e o do emulador do cofre.
- [x] 8.2 Subir a infra, o host na 5200 e o dashboard (`npm run dev`, na 5173), como no `docs/RUNNING.md`.

  **Feito (2026-10-09):** os quatro containers já estavam de pé. O host subiu na 5200, e o `GET /` respondeu 200. O
  dashboard já rodava na 5173: era um `vite` aberto antes, desta mesma pasta, que serviu as mudanças por HMR. A
  conferência das 8.3 a 8.7 foi feita com o Chrome headless, pelo `playwright-core` numa pasta temporária fora do
  repositório. O roteiro só lê: não salvou nada.
- [x] 8.3 Conferir na tela, nos dois temas, com uma captura de cada tela por tema como evidência:
  - **a sidebar:** o lockup colorido no claro e o branco no escuro. A troca de tema troca o lockup sem recarregar. O
    "Hub" ao lado, o tenant embaixo e o bloco com 73 px;
  - **a topbar:** o título em Exo 2 e o avatar legível;
  - **o item ativo, o chip Admin e o rádio marcado:** o texto e o ícone ou a bolinha na mesma cor, com o conta-gotas do
    DevTools batendo com o `--accent-on-tint`;
  - **o Fiscal:**
    - os KPIs em Exo 2;
    - os badges de status com as cores de antes;
    - a grade em Inter 400;
    - o 3, o 4, o 5, o 7 e o 9 na linha de base, no CNPJ, na Data e no processadas (`11/12`);
  - **o Agendamento:**
    - o texto do botão primário legível no escuro, `#071020` sobre `#3b9ae1`;
    - as colunas "Notas" e "Quando" alinhadas dígito com dígito;
  - **Configurações:**
    - o acento azul, sem petróleo;
    - o "Salvar" com texto `#071020` no escuro;
    - o campo ERP com "Dynamics365", o 3 e o 5 na linha de base;
  - **Usuários:** o card do tenant e o chip Admin;
  - **o módulo reservado:** o card tracejado.

  **Feito (2026-10-09),** com 12 capturas, as seis telas nos dois temas, mais o Fiscal em 30 dias e as execuções. Os
  valores são os computados pelo navegador, claro / escuro:
  - **a sidebar:** o `logotipo-horizontal-colorido.svg` / `-branco.svg`, com 17 × 130 px, e a troca de tema troca o
    arquivo sem recarregar. O "Hub" em Exo 2 500, `#6b7788` / `#7f8b9c`, e o filete em `--border-strong`. O bloco tem
    73 px;
  - **a topbar:** o título em Exo 2. O avatar com `#ffffff` sobre `#0072ce` / `#071020` sobre `#3b9ae1`;
  - **o item ativo:** o texto e o ícone em `#005ba9` sobre `#e6f0fb` / `#6cbaf0` sobre `#10263d`. O chip Admin é igual. O
    rádio marcado tem o texto e a bolinha de 4 px na mesma cor, e a moldura em `#0072ce` / `#3b9ae1`. O ícone do card do
    tenant fica em `--accent`, como previsto;
  - **o Fiscal, em 30 dias:**
    - os KPIs (14, 0, 0 e 5) em Exo 2;
    - as células em Inter 400;
    - Finalizado verde e Com pendências laranja, como antes;
    - o CNPJ, a data e o processadas (`0/2`) com os dígitos na linha;
  - **os dígitos medidos no navegador, a 200 px:** o 3, o 5 e o 9 da Inter descem 2 px, que é o overshoot. O 4 e o 7
    descem 0, e um old-style desceria ~30 px. Com o `tnum` do `body`, `1111111111` e `8888888888` têm a mesma largura
    (87,55 px);
  - **o Agendamento:**
    - o "Nova integração" em `#071020` sobre `#3b9ae1`, e no hover sobre `#6cbaf0`;
    - o contador em Exo 2;
    - nas execuções, "Notas" e "Quando" terminam todas no mesmo x (1117 e 1287), com `tnum`;
  - **Configurações:**
    - o "Perfil de conector" em Exo 2, `#001a72` / `#fcfcfc`;
    - a aba ativa no acento azul;
    - o "Salvar" com `#ffffff` sobre `#0072ce` / `#071020` sobre `#3b9ae1`;
    - o campo ERP mostra "Dynamics365", em Inter, com o 3 e o 5 na linha.

    O "D365FnO" do plano não existe no produto: o campo mostra o nome do adapter. A spec foi corrigida para
    "Dynamics365";
  - **um achado que fica registrado, sem mudar o layout:** a célula Empresa tem 130 px úteis. O CNPJ
    `44.278.225/0002-60` ocupava 134,9 px na Manrope e ocupa 143,4 px na Inter. Já truncava antes, e agora trunca
    8,5 px a mais.
- [x] 8.4 O login numa janela de 1360 px:
  - o lockup branco, as seis camadas e os pulsos correndo sobre as linhas;
  - a marca d'água parada;
  - o rótulo "Saída" com pelo menos 4,5:1, medido no conta-gotas sobre o fundo do card.

  Depois, numa janela de 800 px, só o formulário aparece.

  **Feito (2026-10-09):**
  - **O painel:** o lockup `logotipo-horizontal-branco.svg` com 21 px. O fundo é o gradiente de 162°.
  - **As animações:** `fhLoginDriftA`, `fhLoginDriftB`, `fhLoginRun`, `fhLoginBlink` e `fhLoginMarch`. A marca d'água
    não anima (`none`), com opacidade 0,06.
  - **O rótulo "Saída":** a medida foi o pixel mais claro do fundo do card, em quatro momentos da deriva, ao longo de
    12 s. O pior fundo foi `rgb(34, 70, 139)`, e o rótulo em `#aebfe6` ficou em 4,92:1 ou mais.
  - **O "Hub":** 5,70:1 no pior pixel.
  - **Os textos:** "FiscosysHub" no card do meio e "© 2026 Fiscosys · Middleware de integração fiscal" no rodapé. Os
    títulos estão em Exo 2.
  - **A janela de 800 px:** o painel fica com `display: none`, e só o formulário aparece.
- [x] 8.5 O login com "reduzir movimento" ligado no Windows (Configurações › Acessibilidade › Efeitos visuais ›
  Efeitos de animação, desligado): nenhuma animação. O gradiente, o grid e a marca d'água continuam.

  **Feito (2026-10-09), por emulação:** o `prefers-reduced-motion: reduce` foi emulado no navegador, que é a mesma
  media query que a opção do Windows liga. A opção do sistema não foi trocada.
  - **As animações:** as cinco ficam `none`, e o pulso das trilhas some (`background-image: none`).
  - **O que continua:** o gradiente, o grid e a marca d'água (opacidade 0,06).
- [x] 8.6 Na aba Network, há uma única requisição de folha ao `fonts.googleapis.com`, com a Exo 2, a Inter e a DM Mono,
  e nenhuma da Manrope ou da Raleway. O favicon aparece na aba.

  **Feito (2026-10-09):**
  - **As fontes:** uma requisição `stylesheet`, para a URL das três famílias, e três `font` do `fonts.gstatic.com`: Exo 2
    v26, Inter v20 e DM Mono v16. Não há Manrope nem Raleway, e a Inter, a Exo 2 e a DM Mono estão carregadas.
  - **O favicon:** o `index.html` traz os três `<link>` de ícone, e os três arquivos respondem 200. A aba do Chrome
    headless não foi vista, porque o modo headless não mostra a aba.
- [x] 8.7 Nenhum "FiscalHub" visível em nenhuma das telas da 8.3 e 8.4.

  **Feito (2026-10-09):** o `innerText` do `body` não contém "FiscalHub" em nenhuma das telas, nos dois temas, nem no
  login. O título da aba é "FiscosysHub — Painel".
- [x] 8.8 O `openspec validate rebrand-dashboard-to-fiscosys --strict` passa.

## 9. Três ajustes no login, depois do apply (D9, D10, D11; "O painel de marca do login", "O campo preenchido pelo navegador segue o tema")

- [x] 9.1 O autofill no `tokens.css`: a regra global `input:-webkit-autofill` (e `:hover`, `:focus`, `:active`), com a
  sombra inset em `var(--surface)` e o texto em `var(--ink)`. Junto, uma `.fh-input:-webkit-autofill:focus` que soma o
  anel de foco à sombra.
- [x] 9.2 O espelho do MUI no `theme.ts`: o `styleOverrides.input` do `MuiOutlinedInput`, com o `&:-webkit-autofill` em
  `background.paper` e `text.primary`.
- [x] 9.3 O autofill do login: a `className="fh-login-input"` no `<input>` do `IconInput`, e no `login.css` a regra em
  `#fff` e `#001a72`. O `box-shadow` leva `!important`, para vencer o inline do `IconInput`, e no foco soma o anel
  `#e6f0fb`.
- [x] 9.4 As colunas:
  - o painel com `flex: '1 1 auto', minWidth: 0`;
  - o formulário com `className="fh-login-form"` e `flex: '0 0 max(420px, 45%)'`. A primeira versão,
    `clamp(420px, 38%, 560px)`, deixou o painel grande demais (62% a 71%) e foi trocada na 9.11;
  - no `@media` de 900 px do `tokens.css`, `.fh-login-form { flex: 1 1 auto !important; }`.
- [x] 9.5 As trilhas e os nós, no `login.css`:
  - `t2` 22%→18%, `t3` 34%→27% e `n1` 34%→27%;
  - `t4` 66%→73%, `t5` 78%→82% e `n2` 66%→73%;
  - a `t1` e a `t6` ficam;
  - o comentário explica a faixa livre de 27% a 73%.
- [x] 9.6 O `npm test` e o `npm run build` ficam verdes.

  **Feito (2026-10-09):** 66 testes passaram, de 66, e o build passou.
- [x] 9.7 Verificar no navegador, com o Chrome headless:
  - a geometria em 1080 e em 760 de altura, e entre elas;
  - a proporção das colunas;
  - a tela estreita;
  - o autofill.

  **Feito (2026-10-09):**
  - **As trilhas:** em 1360 × 1080, 1360 × 900, 1360 × 800, 1360 × 760, 1440 × 900, 1920 × 1080 e 1000 × 760, nenhuma
    trilha nem nó corta o lockup, a headline, o parágrafo, os cards ou o rodapé. A menor folga é de 15 px (a `t1` até o
    lockup, em 760). Em 760, a `t3` passa 24 px acima da headline, e a `t4`, 21 px abaixo dos cards. Antes do espelho,
    a `t3` cortava a headline: −7 px em 900, −23 px em 800 e −29 px em 760.
  - **As colunas:** em 1360 e em 1440, o painel fica com 62% (843 px e 893 px), e o formulário com 517 px e 547 px. Em
    1920, o painel fica com 70,8% (1360 px), e o formulário com 560 px. Em 1000, o painel fica com 58%, e o formulário
    com 420 px.
  - **A tela estreita:** em 800 px, a coluna do formulário vai de 0 a 800, e o formulário fica centrado (x = 400). Em
    390 px, o `scrollWidth` é 390, sem rolagem horizontal.
  - **O autofill:** é simulado, porque o headless não preenche sozinho. Cada regra `:-webkit-autofill` foi duplicada
    com uma classe no lugar da pseudo-classe, o que mantém a especificidade e a ordem. Os valores computados:
    - **o login, com o tema claro ou escuro no `<html>`:** a sombra `#fff` e o texto `#001a72`. No foco, a sombra soma
      o anel `#e6f0fb`;
    - **no app, claro / escuro:** o `TextField` do MUI, o input inline de Usuários e o `.fh-input` ficam com
      `#ffffff`/`#001a72` e `#161b22`/`#fcfcfc`. O `.fh-input` com foco mantém o anel (`#e6f0fb` / `#10263d`);
    - **as regras do MUI:** o campo de Configurações tem a regra do MUI, com `rgb(38, 103, 152)`, e logo depois a do
      espelho, com `rgb(22, 27, 34)`, que vence. Sem o espelho, o campo ficaria azul no escuro.
  - **As capturas** de 1360 × 1080, 1360 × 760 e 1920 × 1080 foram conferidas a olho.
- [x] 9.8 Os docs acompanham:
  - **o bloco do `login.css`,** na seção 3 do doc de execução, é igual ao arquivo (conferido com `diff`);
  - **o mesmo doc** ganha os pontos 5 a 7, com as colunas, o autofill e as trilhas, e o snippet do ponto 1 com o `flex`
    novo;
  - **no doc de decisões,** a seção 4 troca a faixa de 40%–62% pela de 27% a 73%, com o porquê.
- [x] 9.9 Decidir o rótulo "Saída" em janela baixa (design, D12).
  - **O problema:** com 760 px de altura, em qualquer largura, o rótulo em `brandMuted` fica em 4,16 a 4,18:1 sobre o
    `glow-b` e o fundo do card. Com o layout original, ficava em 4,11 a 4,23:1.
  - **Com 900 px de altura ou mais,** passa: 4,94 a 5,62.

  **Feito (2026-10-09):** quatro correções foram medidas, injetadas só no teste (D12). O Marcelo escolheu um tom
  próprio para o rótulo, numa chave com nome de trabalho, `C.stepLabel = '#c3cfec'`, com um comentário de uma linha.
  A causa estrutural foi registrada no D12. Medido com o código, os três rótulos no pior momento da deriva:

  | Janela | ENTRADA | CONECTOR | SAÍDA |
  |---|---|---|---|
  | 920 × 760 | 6,61 | 4,98 | 4,84 |
  | 1000 × 760 | 5,38 | 5,31 | 4,84 |
  | 1360 × 760 | 6,87 | 6,78 | 4,98 |
  | 1360 × 900 | 6,87 | 6,78 | 5,84 |
  | 1920 × 1080 | 6,83 | 6,94 | 6,64 |

  66 testes passaram, de 66, e o build passou. Os docs registram a decisão e a causa.
- [x] 9.10 O `openspec validate rebrand-dashboard-to-fiscosys --strict` passa.
- [x] 9.11 As colunas em 55/45. O Marcelo achou o painel grande demais com o `clamp(420px, 38%, 560px)` e pediu
  "ligeiramente maior que a metade". O formulário passa a `flex: '0 0 max(420px, 45%)'`.

  **Feito (2026-10-09):** 66 testes passaram, de 66, e o build passou. Medido no navegador:
  - **as colunas:** o painel fica com 55% em 1000, 1360, 1440 e 1920 px (550, 748, 792 e 1056 px), e com 54,3% em 920 px,
    onde o formulário fica nos 420 px do mínimo;
  - **as trilhas:** de 760 a 1080 de altura, nenhuma corta texto ou card, e a menor folga segue em 15 px;
  - **a captura** de 1360 × 900 foi conferida a olho;
  - **o rótulo "Saída":** 4,94 em 1360 × 900, 5,08 em 1440 × 900 e 5,62 em 1920 × 1080. Em 760 de altura, 4,16 a 4,18,
    como na 9.9.
