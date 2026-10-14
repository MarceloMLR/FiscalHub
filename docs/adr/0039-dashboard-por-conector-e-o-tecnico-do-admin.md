# ADR-0039: O dashboard é por conector, a linha é o que a ação opera, e o técnico é do Admin

- **Status:** Aceito
- **Data:** 2026-10-13
- **Revisa:**
  - **`module-navigation`:** a barra lateral deixa de listar módulos do tenant e passa a listar conectores com os
    módulos deles. A frase "é apresentação, e não permissão" deixa de valer para a visão técnica (§5).
  - **[ADR-0018](0018-autenticacao-jwt-e-escopo-por-tenant.md):** o papel passa a decidir **o que a tela mostra**, e não
    só o que o endpoint aceita.
- **Complementa:** [ADR-0037](0037-conector-modulo-integracao.md) e
  [ADR-0038](0038-uma-esteira-tres-estilos-de-saida.md).
- **Desenho:** canvas "FiscosysHub — as telas do produto".
- **Change OpenSpec:** a propor.

## Contexto

Com dois conectores, três módulos e integrações de formatos diferentes, a barra lateral de hoje — Fiscal, Contábil,
Inventário, tudo sob "Integrações" — para de dizer a verdade: ela sugere que existe um Fiscal, quando passam a existir
dois, com ciclos de vida distintos.

E muda quem usa o produto. **O cliente não tem Admin: o Admin é a Fiscosys.** O cliente tem quem olha nota e resolve
pendência fiscal. Isso tem consequência em duas direções: as telas de provisionamento são internas, e o que é
diagnóstico de integração não é do cliente.

**A pergunta que fechou a decisão:** quando o cliente tem Tax Compliance e AvaTax, a nota aparece duas vezes na mesma
tabela? Não — porque a tabela não é da nota, é do conector.

## Decisão

### 1. O eixo da navegação é o conector

A barra lateral agrupa por **conector**, e os **módulos** dele vêm abaixo. Tax Compliance e AvaTax têm dashboards
separados: a mesma nota física nunca divide tabela, porque na captura e na emissão ela tem ciclo de vida, ações e
colunas diferentes.

**Dois níveis, não três.** A integração (plano de contas, lançamento, K230) é **coluna e filtro** dentro da página do
módulo, e não item de menu. Com três integrações no Contábil e outras tantas no Inventário, a árvore completa daria uma
dúzia de linhas numa barra de 232 px.

### 2. O agendamento é do Tax Compliance

Ele desce para dentro do conector, junto com os módulos. Não existe agendamento para o AvaTax: o cálculo e a emissão
são disparados pelo ERP, não por horário. Um agendamento global ofereceria tipos de integração que não se agendam.

### 3. A linha é o que a ação opera

| Onde | A linha é | Porque |
|---|---|---|
| Tax Compliance · Fiscal | o **grupo** (estabelecimento + dia + modelo) | já era, e a ação é sobre o grupo |
| Tax Compliance · Contábil e Inventário | a **carga** | ninguém age sobre uma conta isolada; age sobre a carga |
| AvaTax · Notas Fiscais | a **nota** | reprocessar, cancelar e corrigir são por nota |

A tabela do Fiscal já era por grupo, com contagem `5/5` e status — a carga entra na mesma forma, trocando só as colunas
do domínio.

### 4. O erro de uma carga se lê por motivo, não por registro

Abrir uma carga com 1.324 recusas **não** abre 1.324 linhas. Abre os **motivos**, agrupados, com a contagem de cada um:
em trinta mil itens as recusas se repetem, e três causas costumam explicar tudo. A lista plana faria o analista
descobrir isso sozinho, rolando.

Disso saem duas coisas que o painel fiscal não precisa ter:

- **dois reprocessares, com custos diferentes.** "Só os recusados" relê esses registros no ERP; "a carga inteira" refaz
  as trinta mil, e serve quando a correção foi no cadastro e vale para todos. Num módulo de lote, reprocessar tudo por
  reflexo ocupa a fila a noite inteira;
- **baixar os recusados em arquivo.** Quem conserta mil e trezentos itens é quem mantém o cadastro no ERP, e essa
  pessoa precisa da lista fora do navegador.

### 5. A visão técnica é do Admin — e isso é permissão, não apresentação

O detalhe de um documento tem duas abas:

- **Resumo** — andamento, dados do documento, e baixar o XML e o PDF. É do cliente.
- **Técnico** — os JSONs do ERP, do nosso domínio e da plataforma, e os logs. **É do Admin.**

**Esta é a primeira permissão de verdade do produto.** O `module-navigation` registra que módulo é apresentação e não
permissão; a visão técnica não é. Portanto:

- **a decisão é do servidor.** O endpoint que serve os JSONs confere o papel. Esconder o botão não protege nada: a
  resposta tem credencial do ERP, endereço de plataforma e payload de documento;
- **a tela segue o servidor**, e não o contrário.

### 6. O que a tela mostra é função do estado, sempre

Ações, downloads e seções do corpo são todos derivados do estado do documento, e nenhum aparece desabilitado sem motivo
nem some sem explicação:

- não há o que **cancelar** nem o que **corrigir** numa nota que nunca foi emitida;
- **XML e PDF** só existem depois de autorizada ou cancelada — antes disso o item aparece apagado, dizendo por quê, em
  vez de sumir;
- uma nota **cancelada** ganha o XML do próprio cancelamento, que é outro documento;
- o **histórico de cartas de correção** só existe quando há alguma;
- as fontes de JSON seguem o que **o cliente contratou** e o que **já aconteceu com aquele documento**.

## Consequências

- **O `module-navigation` precisa de delta grande:** eixo, origem do módulo e o fim da frase "apresentação, e não
  permissão" para a visão técnica.
- **Toda tela ganha a barra lateral nova**, inclusive as que não mudam de conteúdo. É mecânico, e é onde um descuido
  passa: o desenho já revelou um item apontando para uma tela que não era a dele.
- **Um cliente com um conector e um módulo ganha um nível de menu que não usa** — "Tax Compliance → Fiscal", duas linhas
  para uma página. Colapsar quando há um conector só faria a interface ter duas formas conforme o cliente; preferimos
  uma estrutura só, e o custo é esse.
- **O endpoint dos JSONs precisa de teste de papel**, e não só de tenant. É requisito da fatia que o implementar.
- **Nada disso entra antes da forma de dado** dos ADRs 0036 e 0037: sem conectores e integrações no modelo, não há o que
  a barra lateral agrupe.

## Alternativas consideradas

- **Navegação por módulo, com o conector como coluna.** Rejeitada no §1: juntaria na mesma tabela uma nota capturada e
  uma nota emitida, que têm ações e estados diferentes.
- **Três níveis de menu, até a integração.** Rejeitada no §1 pelo tamanho da árvore.
- **Agendamento global.** Rejeitada no §2.
- **Uma lista plana de registros recusados.** Rejeitada no §4.
- **Esconder a aba Técnico só na tela.** Rejeitada no §5 — é a diferença entre uma interface e um controle de acesso.
