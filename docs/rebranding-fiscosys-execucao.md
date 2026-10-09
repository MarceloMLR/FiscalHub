# Rebranding Fiscosys — pacote de execução

> Companheiro de `docs/rebranding-fiscosys.md`, que tem as decisões e o porquê.
> Este aqui tem **o que colar**: o prompt da change, o CSS pronto do login e o checklist.
> Aprovado por Marcelo em 2026-10-08. Assets já no repositório.

---

## 1. O que já está no repositório

Os sete arquivos de marca estão em `dashboard/public/brand/` e estão **untracked** — a change os commita.
(A pasta `dashboard/public/` não existia antes; o Vite funciona sem ela, por isso nunca fez falta.)

```
dashboard/public/brand/
├─ logotipo-horizontal-colorido.svg   664×87    4.109 B   sidebar, tema claro
├─ logotipo-horizontal-branco.svg     664×87    4.112 B   sidebar escuro + painel do login
├─ simbolo-colorido.svg               111×83    1.073 B   sidebar recolhida
├─ simbolo-branco.svg                 111×83      741 B   marca d'água do login
├─ favicon.svg                        128×128     719 B   símbolo negativo sobre quadrado #001A72
├─ favicon-32.png                     32×32       580 B
└─ apple-touch-icon-180.png           180×180   2.725 B
```

### Duas armadilhas de SVG, as duas já resolvidas

**1. Nada de `<style>` dentro do SVG.** Os arquivos originais da agência declaravam as cores em `<style>` com
classes (`.cls-1 { fill: #fcfcfc }`). Pipeline de asset costuma remover `<style>` de SVG, e sem `fill`
declarado o desenho cai para **preto** — foi exatamente o que aconteceu na primeira tentativa do símbolo na
sidebar. Nos arquivos acima as cores são atributos `fill` em cada elemento. **Regra para qualquer SVG novo.**

**2. Conferir o tamanho depois de qualquer transferência.** Ao gravar esses arquivos pela ponte com a
máquina, o pipeline injetou um manifesto **C2PA** (content credentials) em cada um: um bloco
`<metadata><c2pa:manifest>` nos SVGs e um chunk `caBX` nos PNGs. O `favicon.svg` foi de 719 B para 8.493 B —
12× num arquivo que carrega em toda página. Já removido; os tamanhos da tabela acima são os corretos.

Se alguém reentregar assets por esse caminho, limpar assim, dentro da pasta:

```python
import re, struct, pathlib
KEEP = {b'IHDR', b'PLTE', b'IDAT', b'IEND', b'tRNS', b'gAMA', b'cHRM', b'sRGB', b'pHYs', b'bKGD'}
for p in sorted(pathlib.Path('.').iterdir()):
    if p.suffix == '.svg':
        s = p.read_text(encoding='utf-8')
        s = re.sub(r'\s*<metadata>.*?</metadata>', '', s, flags=re.S)
        s = re.sub(r'\s+xmlns:c2pa="[^"]*"', '', s)
        p.write_text(s, encoding='utf-8', newline='\n')
    elif p.suffix == '.png':
        b = p.read_bytes(); out, i = b[:8], 8
        while i < len(b):
            ln = struct.unpack('>I', b[i:i+4])[0]; typ = b[i+4:i+8]; end = i + 12 + ln
            if typ in KEEP: out += b[i:end]
            i = end
        p.write_bytes(out)
```

---

## 2. O prompt da change

Colar numa sessão nova e limpa do Claude Code, na raiz do repositório:

```
/opsx:propose

Rebranding do dashboard para a identidade visual da Fiscosys. O produto passa a
se chamar FiscosysHub (nome de exibição apenas — os namespaces C# continuam
FiscalHub.*).

Leia primeiro `docs/rebranding-fiscosys.md`, que tem o mapeamento completo e o
raciocínio de cada decisão, e `docs/rebranding-fiscosys-execucao.md`, que tem o
CSS pronto do login e o checklist.

Escopo: cores da marca, logo e tipografia. NÃO é redesenho — nenhuma mudança de
layout, de estrutura de componente ou de comportamento.

A paleta está DUPLICADA em três lugares e os três precisam mudar juntos, senão
a tela de Configurações (que é toda MUI) e o login ficam com o acento antigo:

1. dashboard/src/theme/tokens.css — tokens CSS
2. dashboard/src/theme.ts — paleta MUI, com os mesmos hexes repetidos
3. dashboard/src/features/auth/LoginPage.tsx — objeto C, terceiro conjunto

Mudanças, exatamente estas:

tokens.css, :root
  --ink          #0b1220 -> #001a72
  --accent       #0b5c7a -> #0072ce
  --accent-hover #094a63 -> #005ba9
  --accent-tint  #e6f1f6 -> #e6f0fb
  --accent-ring  #e6f1f6 -> #e6f0fb
  --accent-ink    (novo) -> #ffffff
  --accent-on-tint (novo) -> #005ba9

tokens.css, html[data-theme='dark']
  --ink          #e6ebf2 -> #fcfcfc
  --accent       #3d9dc4 -> #3b9ae1
  --accent-hover #57b0d4 -> #6cbaf0
  --accent-tint  #12303d -> #10263d
  --accent-ring  #12303d -> #10263d
  --accent-ink    (novo) -> #071020
  --accent-on-tint (novo) -> #6cbaf0

tokens.css, tipografia
  --font-display (novo) -> 'Exo 2', system-ui, sans-serif
  --font-sans    'Manrope, …' -> 'Inter', system-ui, -apple-system, sans-serif
  --font-mono    inalterado
  O @import do tokens.css SAI. As tres familias passam a vir de um <link> so,
  no index.html, que ja tem os preconnect.

tokens.css, body
  font-feature-settings: 'tnum' 1  ->  NAO MUDA.
  A Inter tem digitos lining por padrao e a feature tnum, entao o que ja esta la
  continua valendo. Os dois fontVariantNumeric:'tabular-nums' que o GroupsPage
  poe inline nas colunas "Periodo integrado" e "Processadas" tambem continuam
  funcionando. Nada a fazer aqui.

tokens.css, .fh-btn
  color: #fff -> color: var(--accent-ink)

App.tsx, avatar da topbar (linhas 281-283)
  color: '#fff' -> 'var(--accent-ink)'
  Mesmo defeito do .fh-btn: #fff sobre var(--accent) dá 3,05:1 no escuro.
  O PrimaryButton do login NAO entra: lá o fundo é #0072ce e branco dá 4,89:1.

Texto sobre --accent-tint (3 lugares): trocar var(--accent) por
var(--accent-on-tint), no TEXTO E NO ICONE ao lado:
  - item ativo da sidebar (App.tsx, NavItem)
  - chip Admin em Usuários (UsersPage, RoleChip)
  - rádio marcado no Agendamento
O acento novo é mais claro que o petróleo: #0072ce sobre #e6f0fb dá 4,24:1 e
reprova. Não reaproveitar --accent-hover para isso, mesmo com valor igual:
hover é token de estado, e um ajuste nele quebraria o contraste em silêncio.

theme.ts (claro / escuro)
  primary.main  #0b5c7a -> #0072ce   |  #3d9dc4 -> #3b9ae1
  primary.dark  #094a63 -> #005ba9   |  #57b0d4 -> #6cbaf0
  primary.light #e6f1f6 -> #e6f0fb   |  #12303d -> #10263d
  text.primary  #0b1220 -> #001a72   |  #e6ebf2 -> #fcfcfc
  typography.fontFamily 'Manrope, …' -> 'Inter, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif'
  primary.contrastText (novo) -> '#ffffff' no claro, '#071020' no escuro

  O MUI calcula o texto do botao contained sozinho, com contrastThreshold = 3.
  Branco sobre #3b9ae1 da 3,05:1, passa de 3 e reprova os 4,5:1 — e ai o
  "Salvar" de Configuracoes (ConnectorsPage:374) e o botao do RewindMark nascem
  com o mesmo defeito do .fh-btn. Fixar contrastText, nao subir o
  contrastThreshold (isso mexeria em success/warning/error/info tambem).

NÃO MUDAM: superfícies, bordas, todos os neutros de texto, os 21 tokens de
status, raios, --control-h e as três sombras. Verde, vermelho, âmbar e teal
carregam significado (Finalizado, Com pendências, Processando, Sandbox).

--font-display entra só onde já existe peso 700: nome da sidebar e título da
topbar (App.tsx), números dos KPIs (GroupsPage), contador de agendamentos ativos
(IntegrationsPage), "Perfil de conector" (ConnectorsPage) e os títulos do login.
Corpo, rótulos e grade ficam em --font-sans.

Bloco de marca da sidebar (App.tsx): trocar o quadrado 28x28 com a letra "F"
pelo lockup horizontal — colorido no tema claro, branco no escuro, 17px de
altura — seguido de um filete de 1px e "Hub" em var(--font-display), peso 500,
14.5px, cor var(--muted). O tenantId continua embaixo, em mono. A altura de 73px
e a estrutura do bloco não mudam. Nada de símbolo dentro de quadrado.

Login: aplicar as chaves do objeto C e o fundo animado descritos na seção 3 de
docs/rebranding-fiscosys-execucao.md. Criar
dashboard/src/features/auth/login.css com o conteúdo de lá, importado pelo
LoginPage. A chave C.brandBg é REMOVIDA — o gradiente mora no login.css.

No login, o rotulo dos StepCards (10px maiusculas) troca C.brandFaint por uma
chave propria, C.stepLabel = #c3cfec, com um comentario de uma linha: ela existe
porque o rotulo fica sobre o glow-b e o fundo translucido do card, e o paragrafo
nao. Nao consolidar com o brandMuted: o brandMuted reprovou a 760 px de altura
(4,16:1), e o stepLabel da 4,84:1 no pior caso. O brandFaint continua no rodape
e no tracejado. O "Hub" nao muda: medido em 6,09:1 parado e 5,65:1 no pico da
deriva do glow-a.

index.html: <title> para "FiscosysHub — Painel", adicionar os links de favicon
apontando para /brand/, e MANTER o <link> do Google Fonts, trocando a lista de
familias para Exo+2 + Inter + DM+Mono. O Inter ja estava sendo carregado ali sem
uso nenhum; agora ele e a fonte do corpo. Esse <link> passa a ser o unico lugar
que carrega fonte no projeto — o @import do tokens.css sai.

Trocar o texto "FiscalHub" por "FiscosysHub" em: index.html, App.tsx,
components/FhDataGrid.tsx (comentário), features/auth/LoginPage.tsx,
theme/tokens.css (cabeçalho). No rodapé do login, "© 2026 FiscalHub" vira
"© 2026 Fiscosys". O features/auth/roles.ts NAO muda: o unico "FiscalHub" dele
e o caminho src/FiscalHub.Host/Program.cs, que e o projeto C#.

Os assets já estão em dashboard/public/brand/ (untracked) — a change os commita.
Não regenerar nem reprocessar esses arquivos.

Dois achados que a change deve registrar como corrigidos:
- .fh-btn fixava color:#fff. No tema escuro atual isso é branco sobre #3d9dc4,
  3,1:1, abaixo do mínimo de 4,5:1. É bug anterior ao rebranding. Com
  --accent-ink (#071020) o botão vai a 6,25:1.
- index.html carregava Inter sem nenhum uso. O <link> fica e vira o carregador
  das tres familias; a Inter passa a ser o corpo.

Verificação, ao final: npm run build no dashboard, subir o host na 5200, e
conferir na tela, nos dois temas: Fiscal, Agendamento, Configurações, Usuários,
um módulo reservado e o login. O checklist completo está na seção 5 do doc de
execução.
```

---

## 3. `dashboard/src/features/auth/login.css` — arquivo novo, conteúdo final

A classe `.fh-login-brand` **já existe** no `LoginPage` e já é usada pelo `tokens.css` para esconder o painel
abaixo de 900 px. Este arquivo pendura tudo nela.

```css
/* Painel de marca do login — FiscosysHub.
   Seis camadas tiradas do manual: gradiente 2474 C escurecendo na diagonal,
   dois brilhos 285 C que derivam, o grid modular (só colunas), a marca d'água
   do símbolo, seis trilhas de fluxo e dois nós.
   As trilhas SÃO as réguas horizontais do grid: por isso o grid não desenha
   horizontais, senão apareceria uma linha parada a poucos pixels de uma que anda. */

.fh-login-brand {
  position: relative;
  overflow: hidden;
  isolation: isolate;
  background: linear-gradient(162deg, #001a72 0%, #001a72 40%, #00103f 100%);
}

.fh-login-brand > .fh-login-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 0;
}

.fh-login-brand > .fh-login-content {
  position: relative;
  z-index: 1;
}

/* ── Brilhos ── */
.fh-login-glow-a {
  background: radial-gradient(640px 560px at 10% 4%, rgba(0, 114, 206, 0.55) 0%, rgba(0, 114, 206, 0) 62%);
  animation: fhLoginDriftA 26s ease-in-out infinite alternate;
}
.fh-login-glow-b {
  background: radial-gradient(560px 520px at 74% 64%, rgba(0, 114, 206, 0.24) 0%, rgba(0, 114, 206, 0) 70%);
  animation: fhLoginDriftB 34s ease-in-out infinite alternate;
}
@keyframes fhLoginDriftA {
  from { transform: translate3d(0, 0, 0) scale(1); }
  to   { transform: translate3d(34px, 26px, 0) scale(1.1); }
}
@keyframes fhLoginDriftB {
  from { transform: translate3d(0, 0, 0) scale(1.06); }
  to   { transform: translate3d(-28px, -34px, 0) scale(1); }
}

/* ── Grid modular: só as colunas ── */
.fh-login-grid {
  background-image: repeating-linear-gradient(to right, rgba(255, 255, 255, 0.055) 0 1px, transparent 1px 48px);
  -webkit-mask-image: radial-gradient(130% 95% at 18% 12%, #000 0%, rgba(0, 0, 0, 0.35) 45%, transparent 76%);
  mask-image: radial-gradient(130% 95% at 18% 12%, #000 0%, rgba(0, 0, 0, 0.35) 45%, transparent 76%);
}

/* ── Marca d'água: parada, de propósito ── */
.fh-login-watermark {
  position: absolute;
  right: -112px;
  bottom: -64px;
  width: 520px;
  opacity: 0.06;
  pointer-events: none;
  z-index: 0;
}

/* ── Trilhas de fluxo ──
   O pulso é o background da própria linha, não um elemento por cima:
   é o que garante que a luz corra exatamente sobre a linha fixa.
   As posições são percentuais, mas o bloco de conteúdo é centrado na vertical: quanto mais baixa a janela, mais os
   cards descem em porcentagem, e a headline sobe. Os dois grupos são espelho um do outro (10/18/27 e 73/82/90) e
   deixam livre a faixa de 27% a 73%, que cobre o conteúdo de 760 a 1080 px de altura. */
.fh-login-track {
  position: absolute;
  left: 0;
  right: 0;
  height: 1px;
  background-color: rgba(255, 255, 255, 0.055);
  background-image: linear-gradient(90deg,
    rgba(90, 170, 255, 0) 0%,
    rgba(90, 170, 255, 0.85) 60%,
    rgba(226, 242, 255, 1) 86%,
    rgba(226, 242, 255, 0) 100%);
  background-repeat: no-repeat;
  background-size: 170px 100%;
  background-position: -190px 0;
  animation: fhLoginRun linear infinite;
}
@keyframes fhLoginRun {
  from { background-position: -190px 0; }
  to   { background-position: calc(100% + 190px) 0; }
}
.fh-login-t1 { top: 10%; animation-duration: 9s;   animation-delay: 0s; }
.fh-login-t2 { top: 18%; animation-duration: 13s;  animation-delay: -4s; }
.fh-login-t3 { top: 27%; animation-duration: 7.5s; animation-delay: -2s; }
.fh-login-t4 { top: 73%; animation-duration: 11s;  animation-delay: -6s; }
.fh-login-t5 { top: 82%; animation-duration: 8.5s; animation-delay: -1s; }
.fh-login-t6 { top: 90%; animation-duration: 14s;  animation-delay: -9s; }

/* ── Nós: onde o hub toca a nota ── */
.fh-login-node {
  position: absolute;
  width: 5px;
  height: 5px;
  margin: -2px 0 0 -2px;
  border-radius: 999px;
  background: rgba(174, 191, 230, 0.55);
  box-shadow: 0 0 0 3px rgba(0, 114, 206, 0.18);
  animation: fhLoginBlink 6s ease-in-out infinite;
}
.fh-login-n1 { top: 27%; left: 28%; } /* acompanha a t3 */
.fh-login-n2 { top: 73%; left: 72%; animation-delay: -3s; } /* acompanha a t4 */
@keyframes fhLoginBlink {
  0%, 100% { opacity: 0.35; box-shadow: 0 0 0 3px rgba(0, 114, 206, 0.12); }
  50%      { opacity: 0.9;  box-shadow: 0 0 0 7px rgba(0, 114, 206, 0.05); }
}

/* ── Tracejado entre os cards: a nota indo do ERP ao compliance ── */
.fh-login-flow {
  align-self: center;
  width: 16px;
  height: 1px;
  flex-shrink: 0;
  background-image: repeating-linear-gradient(to right, #8ea1d2 0 3px, rgba(142, 161, 210, 0) 3px 7px);
  background-size: 7px 1px;
  animation: fhLoginMarch 2.4s linear infinite;
}
.fh-login-flow-2 { animation-delay: -1.2s; }
@keyframes fhLoginMarch {
  to { background-position: 7px 0; }
}

/* ── Quem pediu menos movimento, recebe menos movimento.
      Gradiente, grid e marca d'água ficam: a tela continua bonita parada. ── */
@media (prefers-reduced-motion: reduce) {
  .fh-login-glow-a,
  .fh-login-glow-b,
  .fh-login-flow,
  .fh-login-track,
  .fh-login-node {
    animation: none;
  }
  .fh-login-track { background-image: none; }
}

/* ── Autofill do Chrome no formulário, que é sempre claro: a regra global do tokens.css usaria var(--surface), escura
      quando o app ficou no tema escuro. O !important é preciso porque o IconInput põe o box-shadow inline (o anel de
      foco, ou none), e inline vence a folha. No foco, o anel vai junto com o fundo. ── */
.fh-login-input:-webkit-autofill,
.fh-login-input:-webkit-autofill:hover,
.fh-login-input:-webkit-autofill:focus,
.fh-login-input:-webkit-autofill:active {
  -webkit-box-shadow: 0 0 0 1000px #fff inset !important;
  box-shadow: 0 0 0 1000px #fff inset !important;
  -webkit-text-fill-color: #001a72;
  caret-color: #001a72;
}
.fh-login-input:-webkit-autofill:focus {
  -webkit-box-shadow: 0 0 0 1000px #fff inset, 0 0 0 3px #e6f0fb !important;
  box-shadow: 0 0 0 1000px #fff inset, 0 0 0 3px #e6f0fb !important;
}
```

### O que muda no JSX do `LoginPage`

Quatro pontos, nenhum deles estrutural.

**1.** O painel perde o `background` do estilo inline (passa a vir do CSS) e ganha as camadas logo no início:

```jsx
<div
  className="fh-login-brand"
  style={{ flex: '1 1 auto', minWidth: 0, color: C.brandText, display: 'flex',
           flexDirection: 'column', padding: '40px 48px', boxSizing: 'border-box' }}
>
  <div className="fh-login-layer fh-login-glow-a" />
  <div className="fh-login-layer fh-login-glow-b" />
  <div className="fh-login-layer fh-login-grid" />
  <img className="fh-login-watermark" src="/brand/simbolo-branco.svg" alt="" />
  <div className="fh-login-layer">
    <div className="fh-login-track fh-login-t1" />
    <div className="fh-login-track fh-login-t2" />
    <div className="fh-login-track fh-login-t3" />
    <div className="fh-login-track fh-login-t4" />
    <div className="fh-login-track fh-login-t5" />
    <div className="fh-login-track fh-login-t6" />
    <div className="fh-login-node fh-login-n1" />
    <div className="fh-login-node fh-login-n2" />
  </div>
  {/* …os três blocos de conteúdo que já existem, cada um com className="fh-login-content" */}
</div>
```

> **`C.brandBg` sai do objeto `C`.** O gradiente passa a ser a fonte única e mora no `login.css`.

**2.** Os três blocos filhos (lockup, miolo, rodapé) ganham `className="fh-login-content"` — é o que os põe
acima das camadas.

**3.** O lockup substitui o quadrado com a letra "F":

```jsx
<div className="fh-login-content" style={{ display: 'flex', alignItems: 'center', gap: 11 }}>
  <img src="/brand/logotipo-horizontal-branco.svg" alt="Fiscosys" style={{ height: 21, display: 'block' }} />
  <span style={{ width: 1, height: 17, background: 'rgba(255,255,255,0.3)' }} />
  <span style={{ fontFamily: "'Exo 2', system-ui, sans-serif", fontSize: 17, fontWeight: 500, color: C.brandMuted, lineHeight: 1 }}>Hub</span>
</div>
```

**4.** O componente `Dash()` vira `<div className="fh-login-flow" />`, e o segundo
`<div className="fh-login-flow fh-login-flow-2" />`.

### Os ajustes depois do apply (2026-10-09)

**5. As colunas.** O painel tinha `width: '44%', maxWidth: 620`, e o formulário, `flex: 1`. Em tela larga o painel
travava em 620 px, e o formulário ficava com o resto. Agora o formulário tem coluna própria, e o painel fica com o resto
(o `flex` do painel está no snippet do ponto 1):

```jsx
<div className="fh-login-form" style={{ flex: '0 0 max(420px, 45%)', background: C.page, /* … */ }}>
```

O painel fica com 55% em qualquer largura, ligeiramente maior que a metade. Uma primeira versão usava
`clamp(420px, 38%, 560px)`, e o teto de 560 px fazia o painel crescer com a tela (71% em 1920): grande demais. Abaixo
de 900 px, o `@media` do `tokens.css` esconde
o painel e devolve `flex: 1 1 auto !important` à `.fh-login-form`. Sem isso, a coluna de 420 px ficaria encostada à
esquerda, e num celular de 390 px estouraria a largura.

**6. O autofill do Chrome.** O `<input>` do `IconInput` ganha `className="fh-login-input"`, e o `login.css` tem a regra
dele (no bloco acima). A regra global, para o resto do app, está no `tokens.css`, e o espelho do MUI, no `theme.ts`.

**7. As trilhas.** As de baixo desceram para 73% e 82%, e as de cima subiram para 18% e 27%, espelhadas. Os nós
acompanham a `t3` e a `t4`. Elas deixam livre a faixa de 27% a 73% (bloco acima).

---

## 4. `index.html`

Entra:
```html
<link rel="icon" type="image/svg+xml" href="/brand/favicon.svg" />
<link rel="icon" type="image/png" sizes="32x32" href="/brand/favicon-32.png" />
<link rel="apple-touch-icon" sizes="180x180" href="/brand/apple-touch-icon-180.png" />
<title>FiscosysHub — Painel</title>
```

Muda — o `<link>` do Google Fonts fica e passa a pedir as três famílias. É o único lugar que carrega fonte:
```html
<link href="https://fonts.googleapis.com/css2?family=Exo+2:wght@500;600;700&family=Inter:wght@400;500;600;700&family=DM+Mono:wght@400;500&display=swap" rel="stylesheet" />
```

---

## 5. Checklist de conferência

Depois do `npm run build` e com o host na 5200, nos **dois temas**:

- [ ] Sidebar: lockup colorido no claro, branco no escuro, "Hub" ao lado, tenant embaixo
- [ ] Topbar: título em Exo 2, avatar legível (`--accent-ink`), nos dois temas
- [ ] Item ativo da sidebar, chip Admin e rádio marcado: texto **e ícone** em `--accent-on-tint`
- [ ] Fiscal: KPIs em Exo 2, badges de status **sem mudança de cor**, grade em Inter 400
- [ ] Colunas numéricas alinhadas e nenhum dígito abaixo da linha — conferir CNPJ, Data e `11/12`
- [ ] Configurações: o campo ERP mostrando `D365FnO` — era o pior caso da fonte antiga
- [ ] Agendamento: botão "Nova integração" com texto legível no tema escuro — era o bug de 3,1:1
- [ ] Configurações: a tela é MUI; se o acento ali ainda estiver petróleo, o `theme.ts` não foi alterado
- [ ] Usuários: card do tenant com o ícone em `--accent-tint` / `--accent`
- [ ] Módulo reservado: card tracejado
- [ ] Login: lockup branco, fundo com as seis camadas, pulsos correndo **sobre** as linhas
- [ ] Login: rótulos ENTRADA/CONECTOR/SAÍDA em `stepLabel` (`#c3cfec`), legíveis por cima do brilho, também com a janela a 760 px de altura
- [ ] Configurações: botão "Salvar" (MUI contained) com texto legível no tema escuro
- [ ] Login com "reduzir movimento" ligado no SO: fundo parado, nenhuma animação
- [ ] Favicon na aba
- [ ] Nenhuma ocorrência de "FiscalHub" visível na interface
- [ ] `grep -rn "Manrope\|Raleway\|0b5c7a\|3d9dc4\|0e2a35" dashboard/src dashboard/index.html` não retorna nada
- [ ] Os sete arquivos de `public/brand/` com os tamanhos da seção 1 — nenhum inchado por metadado
