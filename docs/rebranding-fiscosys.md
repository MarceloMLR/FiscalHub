# Rebranding Fiscosys — decisões, mapeamento de tokens e plano de assets

> Escopo: **cores da marca, logo e tipografia**. Nenhuma mudança de estrutura, de layout ou de componente.
> Aprovado por Marcelo em 2026-10-08. O pacote de execução (prompt, CSS pronto, checklist) está em
> `docs/rebranding-fiscosys-execucao.md`.

---

## 1. O que a marca define

| Cor | Hex | Pantone |
|---|---|---|
| Azul escuro | `#001A72` | 2474 C |
| Azul claro | `#0072CE` | 285 C |

Cinzas do manual: `#FCFCFC` · `#D8D8D8` · `#B2B2B2` · `#7F7F7F` · `#4C4C4C` · `#191919`

Tipografia: Exo 2 Bold (títulos), Exo 2 Medium (subtítulos), Raleway Light (corpo).
**A Raleway não entra no dashboard** — ver a seção 5. Ela continua valendo para peça impressa e marketing.

Gradiente `#0072CE → #001A72` na barra central do símbolo. O conceito do manual é um **grid modular 16:9**,
"sistemas, ordem e conexão", e o gradiente significa **transição entre sistemas/APIs**.

---

## 2. A paleta está em TRÊS lugares, não um

Esse é o achado que mais muda o plano da change.

| Arquivo | O que tem | Como é consumido |
|---|---|---|
| `dashboard/src/theme/tokens.css` | os 57 tokens CSS, claro e escuro | `var(--token)` nos componentes próprios |
| `dashboard/src/theme.ts` | **a mesma paleta repetida**, em hex, como paleta MUI | tudo que é MUI: `ConnectorsPage` inteira, `Alert`, `Tabs`, `Switch`, `TextField`, `DataGrid` |
| `dashboard/src/features/auth/LoginPage.tsx` | **um terceiro conjunto**, no objeto `C` | só o login, que é independente de tema por decisão |

Trocar só o `tokens.css` deixaria a tela de Configurações com o acento petróleo antigo, e o login idem.
Os três precisam mudar juntos.

> Depois do rebranding vale considerar fazer o `theme.ts` ler as variáveis CSS em vez de repetir os hexes —
> mas isso é refactor, não rebranding, e fica fora desta change.

---

## 3. O diff do `tokens.css`

### Tema claro (`:root`)

| Token | Hoje | Novo |
|---|---|---|
| `--ink` | `#0b1220` | `#001a72` |
| `--accent` | `#0b5c7a` | `#0072ce` |
| `--accent-hover` | `#094a63` | `#005ba9` |
| `--accent-tint` | `#e6f1f6` | `#e6f0fb` |
| `--accent-ring` | `#e6f1f6` | `#e6f0fb` |
| `--accent-ink` | *(não existe)* | `#ffffff` |
| `--accent-on-tint` | *(não existe)* | `#005ba9` |

### Tema escuro (`html[data-theme='dark']`)

| Token | Hoje | Novo |
|---|---|---|
| `--ink` | `#e6ebf2` | `#fcfcfc` |
| `--accent` | `#3d9dc4` | `#3b9ae1` |
| `--accent-hover` | `#57b0d4` | `#6cbaf0` |
| `--accent-tint` | `#12303d` | `#10263d` |
| `--accent-ring` | `#12303d` | `#10263d` |
| `--accent-ink` | *(não existe)* | `#071020` |
| `--accent-on-tint` | *(não existe)* | `#6cbaf0` |

### Tipografia

| Token | Hoje | Novo |
|---|---|---|
| `--font-display` | *(não existe)* | `'Exo 2', system-ui, sans-serif` |
| `--font-sans` | `'Manrope', …` | `'Inter', system-ui, -apple-system, sans-serif` |
| `--font-mono` | `'DM Mono', …` | inalterado |

Carregamento: as três famílias passam a vir de **um `<link>` só**, no `index.html`, que já tem os
`preconnect`. O `@import` do `tokens.css` sai — fonte em dois arquivos é a mesma armadilha da paleta em três.

`https://fonts.googleapis.com/css2?family=Exo+2:wght@500;600;700&family=Inter:wght@400;500;600;700&family=DM+Mono:wght@400;500&display=swap`

### Permanece exatamente como está

Superfícies, bordas, todos os neutros de texto, **os 21 tokens de status**, raios, `--control-h` e as três sombras.

Os neutros de hoje já são azulados e convivem bem com o 285 C. E verde, vermelho, âmbar e teal carregam
significado — `Finalizado`, `Com pendências`, `Processando`, `Sandbox`/`Produção` — então trocar qualquer um
apaga informação.

### O espelho em `theme.ts`

| Chave | Claro hoje → novo | Escuro hoje → novo |
|---|---|---|
| `primary.main` | `#0b5c7a` → `#0072ce` | `#3d9dc4` → `#3b9ae1` |
| `primary.dark` | `#094a63` → `#005ba9` | `#57b0d4` → `#6cbaf0` |
| `primary.light` | `#e6f1f6` → `#e6f0fb` | `#12303d` → `#10263d` |
| `text.primary` | `#0b1220` → `#001a72` | `#e6ebf2` → `#fcfcfc` |
| `typography.fontFamily` | `Manrope, …` → `Inter, …` | idem |
| `primary.contrastText` | *(não existe)* → `#ffffff` | *(não existe)* → `#071020` |

Tudo o mais (success, warning, error, info, background, divider, text.secondary) fica.

#### `primary.contrastText` — o espelho MUI do `--accent-ink`

O MUI calcula sozinho a cor do texto de um botão `contained`, comparando o fundo com branco e preto e
aceitando o que passar do `contrastThreshold`, que **vale 3 por padrão**. Branco sobre `#3b9ae1` dá 3,05:1 —
passa de 3 e reprova os 4,5:1 de texto. Então o "Salvar" de Configurações (`ConnectorsPage:374`) e o botão
do `RewindMark` nascem com exatamente o mesmo defeito do `.fh-btn`, por um caminho diferente.

Fixar `primary.contrastText` é cirúrgico. A alternativa — subir o `contrastThreshold` para 4.5 — mexeria
também no texto de success, warning, error e info, que a change não quer tocar.

### O terceiro conjunto, em `LoginPage.tsx`

| Chave de `C` | Hoje | Novo |
|---|---|---|
| `brandBg` | `#0e2a35` | **removida** — ver nota abaixo |
| `brandText` | `#e8eef1` | `#fcfcfc` |
| `brandMuted` | `#93a8b2` | `#aebfe6` |
| `brandFaint` | `#6f8892` | `#8ea1d2` |
| `ink` | `#0b1220` | `#001a72` |
| `accent` | `#0b5c7a` | `#0072ce` |
| `accentHover` | `#094a63` | `#005ba9` |
| `ring` | `#e6f1f6` | `#e6f0fb` |

> **`brandBg` sai do objeto.** O painel deixa de ter cor chapada: o fundo vira um gradiente com camadas, que
> mora no `login.css`. Manter a chave recriaria a duplicação de paleta que esta change está consertando.

O quadradinho `#2f7f97` com a letra "F" sai e dá lugar ao lockup negativo.

#### Os rótulos dos StepCards ganham chave própria: `stepLabel`

> **Decisão final (2026-10-09): `C.stepLabel = #c3cfec`.** Foi a terceira tentativa. O `brandMuted` abaixo passou em
> 1360 × 900, mas reprovou com a janela a 760 px de altura (4,16 a 4,18:1). O `stepLabel` passa em todas as janelas
> medidas, com 4,84:1 no pior caso, e os três rótulos foram medidos.
>
> **A causa estrutural:** o fundo do card é branco translúcido (`rgba(255,255,255,0.035)` no código; o mockup tinha
> 0,05) e clareia o que estiver atrás. O contraste do rótulo é função de três variáveis: a intensidade do brilho, a
> largura do painel (o centro do `glow-b` fica a 74% dela) e a altura da janela (o centro fica a 64% dela, e o
> conteúdo é centrado). Mudou qualquer uma das três, mede de novo.
>
> **A chave tem nome de trabalho, e não de aparência.** Ela não é um "`brandMuted` mais claro". Ela existe porque o
> rótulo fica sobre o brilho e o parágrafo não. Consolidar `#aebfe6` e `#c3cfec` faria o contraste reprovar de novo.
> As alternativas medidas (rótulo branco, brilho mais baixo, brilho mais fraco) estão no D12 da change
> `rebrand-dashboard-to-fiscosys`.
>
> O texto abaixo é o da segunda tentativa, mantido como registro.

Sobre o navy puro, `brandFaint` dá 5,9:1 e passa. Mas o rótulo do StepCard (10 px, maiúsculas) não está
sobre navy puro: está sobre **três camadas somadas** — a base do gradiente, o `glow-b` por cima e o fundo
`rgba(255,255,255,0.05)` do próprio card, que clareia mais ainda. O card "Saída" cai quase em cima do centro
do `glow-b`.

| Fundo no ponto do rótulo | `brandFaint` | `brandMuted` |
|---|---|---|
| navy puro | 5,88:1 | 8,19:1 |
| base + glow-b | 5,02:1 | 6,99:1 |
| base + glow-b + card 5% | **4,40:1 — reprova** | 6,13:1 |
| idem, com margem no alpha | **4,09:1 — reprova** | 5,70:1 |

**Decisão da segunda tentativa, substituída pela de cima: o rótulo do StepCard usa `brandMuted`.** Nenhuma cor nova, e o `login.css` não muda. A hierarquia
se mantém pelo tamanho e peso (10 px/700 maiúsculas contra 13,5 px/600 do valor em branco logo abaixo), não
pela cor.

`brandFaint` continua no rodapé (`© 2026 Fiscosys`, 7,14:1 — o canto inferior esquerdo é a parte mais escura
do painel) e no tracejado entre os cards.

> O **"Hub"** do lockup foi medido junto e **não precisa mudar**: modelando a queda do radial, o alpha do
> `glow-a` naquele ponto é 0,29 parado e 0,36 no pico da deriva, o que dá 6,09:1 e 5,65:1. Estimativas mais
> grosseiras sugeriram ~4,5:1 ali; a conta com a geometria do gradiente dá folga.

---

## 4. O painel de marca do login: motion graphic em CSS

O painel era um azul chapado com texto. Virou um motion graphic contínuo, todo em CSS, com o vocabulário do
próprio manual — nada de brilho genérico. O código final está na seção 3 do doc de execução.

| Camada | O que é | Movimento |
|---|---|---|
| Base | `linear-gradient(162deg, #001a72 0%, #001a72 40%, #00103f 100%)` | estática. `#00103f` é escurecimento do próprio navy, não cor nova |
| Dois brilhos | radiais de `#0072CE` a 55% e 24%, um atrás do logo, outro à direita | derivam (`translate3d` + `scale`) em 26 s e 34 s, `ease-in-out alternate` |
| Grid modular | colunas de 1 px a `rgba(255,255,255,0.055)` a cada 48 px, com `mask-image` radial | estático — é a âncora de ordem da composição |
| Marca d'água | `simbolo-branco.svg` a 520 px sangrando no canto inferior direito | **parada**, a 6% de opacidade |
| Trilhas de fluxo | 6 linhas de 1 px com um pulso claro atravessando cada uma | 7,5 s a 14 s, lineares, defasadas: cada pulso é uma nota indo do ERP ao compliance |
| Nós | 2 pontos de 5 px com halo de `#0072CE` | pulsam em 6 s, defasados — onde o hub toca a nota |

Três decisões que custaram iteração e não devem ser desfeitas:

1. **O grid não desenha linhas horizontais.** Desenhava, a cada 48 px, e as trilhas ficavam em porcentagem da
   altura — então aterrissavam a poucos pixels de uma régua do grid, nunca em cima. Resultado: uma linha
   parada ao lado de uma que anda. As trilhas **são** as réguas horizontais agora.
2. **O pulso é o `background-image` da própria trilha**, animado por `background-position` — não um elemento
   por cima. É o que garante que a luz corra exatamente sobre a linha, em qualquer renderizador.
3. **A marca d'água não anima.** Tinha uma animação de escala e opacidade que, somada ao brilho centrado em
   cima dela, lia como uma mancha pulsando. Um logo respirando parece defeito.

As trilhas deixam livre a faixa de 27% a 73% da altura, que é onde fica o conteúdo: movimento em cima e embaixo,
calma no meio. A primeira versão evitava 40%–62%, mas as trilhas são percentuais e o conteúdo é centrado na vertical:
em janela baixa ele ocupa uma fatia maior da altura. Em 760 px de altura, a trilha de 66% cortava os cards por dentro,
e a de 34% cortava a headline. Os dois grupos hoje são espelho um do outro (10/18/27 e 73/82/90), e os nós acompanham a
terceira e a quarta trilha. Medido de 760 a 1080 px de altura, nenhuma trilha corta texto ou card (change
`rebrand-dashboard-to-fiscosys`, tarefa 9.7).

**Por que CSS e não vídeo.** Um `.mp4` de fundo pesa centenas de KB numa tela que todo mundo carrega antes de
fazer qualquer coisa, não acompanha o painel quando ele muda de largura (o painel fica com o que sobra da coluna do
formulário),
não tem como respeitar `prefers-reduced-motion` e perde nitidez em tela retina. O CSS são 8 elementos
absolutos animando `transform`, `opacity` e `background-position`.

O guard de `prefers-reduced-motion` não é opcional: sem ele, quem marcou "reduzir movimento" no sistema
continua recebendo animação. Gradiente, grid e marca d'água ficam — a tela continua bonita parada.

---

## 5. Os dois tokens novos

### `--font-display`
Entra onde já existe peso 700: nome na sidebar e título da topbar (`App.tsx`), números dos KPIs
(`GroupsPage`), contador de agendamentos ativos (`IntegrationsPage`), "Perfil de conector"
(`ConnectorsPage`) e os títulos do login. Corpo, rótulos e grade continuam em `--font-sans`.

### O corpo é Inter, não Raleway — e por quê

O manual pede Raleway Light para corpo. Testada contra o conteúdo real do dashboard, ela não serve, e isso
apareceu por três caminhos independentes:

1. **Medição.** Raleway 4.026 traz dígitos **old-style** por padrão — 3, 4, 5, 7 e 9 descem até 15,4% do em —
   e **não tem a feature `tnum`**. O `'tnum' 1` que já existe no `body` do `tokens.css` viraria letra morta,
   e junto com ele os dois `fontVariantNumeric: 'tabular-nums'` que o `GroupsPage` põe inline nas colunas
   *Período integrado* e *Processadas*.
2. **Olho.** Marcelo apontou o campo ERP de Configurações: `D365FnO` é o pior caso possível — maiúsculas com
   `3` e `5` caindo abaixo da linha no meio da string.
3. **Natureza da fonte.** A Raleway é face de **display**: hastes finas, aberturas fechadas, pensada para
   30 px. A célula do `FhDataGrid` é 13,5 px e o `.fh-label` é 11 px.

O manual foi escrito para impressão, e ali a Raleway está certa. Numa grade fiscal, não.

**Decisão: `--font-sans` = Inter.** Desenhada para interface densa — altura-x alta, aberturas largas, dígitos
lining por padrão e `tnum`. Comparada lado a lado com IBM Plex Sans e Manrope nas strings reais do produto
(prancheta "Tipografia — opções para o corpo" do canvas), foi a escolhida.

Ironia útil: **a Inter já está sendo carregada hoje** pelo `index.html`, sem nada usar. O plano anterior
mandava remover esse `<link>`; agora ele fica, ampliado para as três famílias.

> **Alternativas descartadas.** *IBM Plex Sans*: mais caráter e uma mono irmã que unificaria com a DM Mono,
> mas é escolha de identidade e a identidade aqui já é a Exo 2. *Manrope*: é a atual e não tem defeito algum
> — mas aí a tipografia simplesmente não muda. *Exo 2 no corpo também*: cansa em 13,5 px numa tabela densa.

**A Exo 2 não muda de papel**: título, subtítulo e número de KPI, via `--font-display`. É lining, tem `tnum`
e é a voz da marca.

#### O que esta escolha CANCELA do plano anterior

| Item | Plano com Raleway | Com Inter |
|---|---|---|
| `font-feature-settings` do `body` | passaria a `'tnum' 1, 'lnum' 1` | **fica como está**, `'tnum' 1` |
| `fontVariantNumeric` do `GroupsPage` | virava no-op, perda aceita | **volta a funcionar**, nada a fazer |
| `<link>` do Inter no `index.html` | removido por não ter uso | **mantido**, agora com as três famílias |
| Alinhamento das colunas numéricas | pior que hoje, custo aceito | **igual ao de hoje** |

A troca de fonte diminuiu a change em vez de aumentá-la: três alterações a menos no `tokens.css` e no
`GroupsPage`, e um custo de qualidade que deixa de existir.

> **Verificar no apply:** rodar na Inter servida pelo Google Fonts a mesma checagem com `fontTools` que foi
> feita na Raleway e na Exo 2 — features numéricas e `yMin` dos dígitos. Raleway e Exo 2 eu medi nos TTFs;
> a Inter está documentada como lining + `tnum`, mas confirmar custa um comando.
>
> **Medido em 2026-10-09** (tarefa 4.1 da change `rebrand-dashboard-to-fiscosys`): Inter v20, "Version 4.001",
> variável (`wght` 100–900). Features numéricas `tnum` e `pnum`, sem `onum`. `yMin` do 3, 4, 5, 7 e 9 entre 0 e
> −20 em 2048 (só o overshoot das curvas, ~1% do em). Com `tnum`, os dez dígitos têm a mesma largura (1328).

### `--accent-ink` — e o bug que ele conserta
`.fh-btn` fixa `color: #fff`. No **tema escuro de hoje** isso é branco sobre `#3d9dc4`: **3,1:1**, abaixo do
mínimo de 4,5:1. É anterior ao rebranding, e o azul novo repetiria (3,0:1). Com `#071020` o mesmo botão vai
a 6,25:1, e a 8,98:1 no hover. Uma versão anterior deste doc dizia 12,1:1; o valor medido é 6,25:1.

Troca `#fff` por `var(--accent-ink)` em **dois** lugares: `.fh-btn` (`tokens.css`) e o avatar da topbar
(`App.tsx:281-283`), que tem exatamente o mesmo defeito — `#fff` sobre `var(--accent)`, 3,05:1 no escuro.

> O `PrimaryButton` do login **não entra**. O formulário do login é sempre claro, e lá o fundo é `#0072ce`:
> branco dá 4,89:1, passa. Além disso o login não lê tokens CSS. Uma versão anterior deste doc mandava mudar
> os três lugares; eram dois.

### `--accent-on-tint` — o segundo buraco que a paleta nova abre

O `--accent` fazia dois trabalhos: cor de preenchimento (botão, avatar) e cor de **texto sobre
`--accent-tint`**. Com o petróleo antigo os dois funcionavam. O azul da marca é mais claro, e o segundo
trabalho quebra:

| | Contraste |
|---|---|
| `#0b5c7a` sobre `#e6f1f6` (antigo) | 6,47:1 |
| `#0072ce` sobre `#e6f0fb` (novo, claro) | **4,24:1 — reprova** |
| `#005ba9` sobre `#e6f0fb` | 5,94:1 |
| `#3b9ae1` sobre `#10263d` (novo, escuro) | 5,05:1 — passa |
| `#6cbaf0` sobre `#10263d` | 7,26:1 |

Atinge três lugares: o item ativo da sidebar (13,5 px/600), o chip Admin em Usuários (12 px/600) e o rádio
marcado no Agendamento (13 px/600).

**Decisão: token próprio, não reaproveitar `--accent-hover`.** Os valores coincidem hoje, mas `hover` é um
token de **estado**: no dia em que alguém ajustar o hover — que é o tipo de ajuste mais provável de todos —
três cores de texto mudam junto e o contraste volta a reprovar em silêncio. O token nomeia o trabalho real,
e faz par com o `--accent-ink`: um é a tinta que vai **sobre** o acento, o outro é o acento **sobre** o tint.

Nos três lugares, trocar `var(--accent)` por `var(--accent-on-tint)` **no texto e no ícone ao lado** — se só
o texto mudar, ícone e rótulo ficam em azuis diferentes lado a lado.

> O escuro já passaria com `--accent` (5,05:1). Define-se o token nos dois temas assim mesmo: uma regra só
> é mais fácil de seguir que "no claro usa um, no escuro usa outro".

---

## 6. Verificações feitas no código

- **`--ink` em `#001a72` é seguro.** É usado em títulos, nomes, `.fh-input`, `Segmented` ativo, KPI
  "Documentos", coluna Nome de Usuários. **As células do `FhDataGrid` usam `var(--text)`** — a grade não
  fica azul. Era a dúvida que a primeira versão da proposta deixou em aberto.
- **O `StatusChip` monta a cor por interpolação** (`var(--${tone}-bg)`). Como nenhum status muda, nada a fazer.
- **O `index.html` carregava Inter** sem nada usar — o `body` usava `var(--font-sans)`, que era Manrope. O
  `<link>` fica: a Inter vira o corpo, e ele passa a pedir as três famílias (seção 5).
- **O `ReservedModulePage` e o `GroupsPage` não têm cor própria** — herdam tudo por token.
- Fora o `#fff` já citado, nenhuma cor de marca aparece hardcoded nos componentes.

---

## 7. Assets

### O que veio nas pastas da agência
Fontes: Exo 2 e Raleway completas em TTF (18 cortes cada). **Decisão:** carregar do Google Fonts pelo
`<link>` do `index.html`, o único lugar do projeto que carrega fonte (seção 3) — nenhum binário no repo. Os
TTFs ficam guardados para o caso de rede fechada.

Logotipo: `logotipo-colorido.svg` (529×214, empilhado), `logotipo-branca.svg` (664×87, horizontal),
`Simbolo-colorido.svg` e `Ativo 7.svg` (111×83), `Ativo 3/4.svg` (cópias), um `.eps` e ~18 PNGs.

**Mapa de cor do wordmark**, confirmado comparando os dois lockups (mesmo desenho, deslocado +134 em x no
texto e +1,69 em y no símbolo): `F I S C O` = `#0072CE`, `S Y S` = `#001A72`; no símbolo, bloco superior
direito e varredura inferior em `#001A72`, bloco inferior esquerdo e varredura superior em `#0072CE`, barra
central em gradiente.

### Lacunas, resolvidas
1. **Não havia lockup horizontal colorido** — o colorido só vinha empilhado, o horizontal só vinha branco.
   Gerado a partir da geometria do branco com as cores do empilhado.
2. **Não havia favicon** — gerado `favicon.svg`: símbolo negativo sobre quadrado `#001A72` arredondado.
   Fundo sólido de propósito: o símbolo transparente perde as partes navy numa aba escura.
3. **`mix-blend-mode: multiply`** numa das letras S dos lockups originais — o `isolation: isolate` do grupo
   pai neutraliza na teoria, mas é comportamento que já teve bug em Safari. Removido nas cópias web.
4. **Todos os `<style>` internos viraram atributos `fill`.** Pipeline de asset costuma remover `<style>` de
   SVG, e sem `fill` declarado o desenho cai para preto. Regra para qualquer SVG novo no projeto.

### Onde cada um entra

| Lugar | Arquivo |
|---|---|
| Bloco de marca da sidebar, tema claro | `logotipo-horizontal-colorido.svg` a 17 px |
| Bloco de marca da sidebar, tema escuro | `logotipo-horizontal-branco.svg` a 17 px |
| Painel de marca do login | `logotipo-horizontal-branco.svg` a 21 px |
| Marca d'água do login | `simbolo-branco.svg` a 520 px, opacidade 6% |
| Sidebar recolhida, se houver | `simbolo-colorido.svg` / `simbolo-branco.svg` |
| Favicon | `favicon.svg` + `favicon-32.png` + `apple-touch-icon-180.png` |

Todos já gravados em `dashboard/public/brand/`.

> **Bloco de marca da sidebar:** lockup + filete + "Hub" em Exo 2 Medium na cor `muted`, com o `tenantId`
> abaixo. **Nada de símbolo dentro de um quadrado** — logo colorido em fundo claro, logo branco em fundo
> escuro, que é o que o manual prevê. A 17 px de altura o lockup ocupa 130 px dos 204 px úteis; o conjunto
> fica em ~180 px. A estrutura do bloco (marca em cima, tenant embaixo, 73 px de altura) não muda.

---

## 8. Naming

**FiscosysHub**, uma palavra, substituindo "FiscalHub" em `dashboard/index.html`, `src/App.tsx`,
`src/components/FhDataGrid.tsx` (comentário), `src/features/auth/LoginPage.tsx` (lockup, StepCard "Conector"
e o rodapé, que vira `© 2026 Fiscosys`), `src/theme/tokens.css` (cabeçalho).

O `src/features/auth/roles.ts` **não** muda: o único "FiscalHub" dele é o caminho `src/FiscalHub.Host/Program.cs`,
que é o projeto C# e continua existindo. Trocar o texto faria o comentário apontar para um arquivo que não existe.

"Hub" é a palavra certa: a topologia é hub-and-spoke (vários ERPs de um lado, várias plataformas de
compliance do outro, um ponto no meio), e é o único candidato que não promete inspeção — o que a ADR-0026
proíbe.

> **Namespaces C# continuam `FiscalHub.*`.** Nome de exibição e namespace são coisas diferentes; renomear
> namespaces é refactor grande, de risco alto e benefício zero para o usuário.
