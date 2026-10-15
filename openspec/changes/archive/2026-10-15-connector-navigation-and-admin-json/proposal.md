## Why

A `platform-account-and-connectors` entregou a **forma de dado** do ADR-0036 e do ADR-0037: o tenant tem uma conta de
plataforma, conectores sob ela, módulos ligados em cada conector e integrações dentro de cada módulo. A barra lateral
não acompanhou — de propósito, porque o reagrupamento é do ADR-0039 e não cabia naquela fatia.

Hoje a barra lateral lista **módulos do tenant** (Fiscal, Contábil, Inventário, Agendamento) sob um bloco
"Integrações", montada a partir de uma lista plana que o `/info` devolve. Com dois conectores isso para de dizer a
verdade: o ADR-0039 §1 mostra que **a tabela não é da nota, é do conector** — a mesma nota física tem ciclo de vida,
ações e colunas diferentes na captura (Tax Compliance) e na emissão (AvaTax). Uma barra lateral por módulo sugere que
existe **um** Fiscal quando passam a existir dois.

E muda quem usa o produto: **o Admin é a Fiscosys, não o cliente** (ADR-0039 §5). O que é diagnóstico de integração —
os JSONs do ERP, do nosso domínio e da plataforma, e os logs — não é do cliente. Isso já é verdade no servidor para o
`/trace` e para o zip, mas:

- **a regra de papel não tem teste.** O `rawTraceRoles` do `Program.cs` é a única coisa que a sustenta, e a suíte não
  tem um único teste de endpoint por papel. O ADR-0039 ("Consequências") registra isso como requisito da fatia que o
  implementar;
- **a tela não segue o servidor: ela o repete.** O `dashboard/src/features/auth/roles.ts` tem uma segunda cópia da
  lista `['Admin']`, com um comentário pedindo que as duas andem juntas. O `platform-response-trace` já exige que a
  lista more **num lugar só no servidor**, e hoje ela mora em dois.

Esta é a **fatia cruzada** do item 2 dos "Próximos passos" do `docs/STATUS.md`: as duas partes do ADR-0039 que não
dependem do Contábil nem do Inventário. A permissão nasce antes de haver dois domínios para proteger, e o eixo novo
nasce antes de haver o segundo conector que o obriga.

## What Changes

### 1. O eixo da navegação passa a ser o conector

A barra lateral agrupa por **conector**, com os **módulos** dele abaixo. **Dois níveis, não três:** a integração
(plano de contas, lançamento, K230) continua coluna e filtro dentro da página do módulo, e nunca item de menu
(ADR-0039 §1).

O `/info` passa a responder a **árvore** que a barra lateral desenha: os conectores do tenant, cada um com o adapter,
o rótulo e os módulos ligados, na ordem. A lista plana `modules` **sai**: ela existia porque era por ela que a barra
lateral era montada, e não é mais.

### 2. O Agendamento desce para dentro do conector

Ele deixa de ser um item fixo do bloco "Integrações" e passa a ser um item **do conector**, junto dos módulos. Não
existe agendamento para o AvaTax: cálculo e emissão são disparados pelo ERP, não por horário (ADR-0039 §2). Quem diz
se um conector agenda é o **adapter**, que já declara módulos e integrações — e não um `if` por nome de adapter.

### 3. A aba Técnico é do Admin, e isso é permissão

O endpoint que serve os JSONs já confere o papel no servidor. Esta fatia fecha as duas pontas que faltam:

- **o teste de papel**, e não só de tenant, no `/trace` e no download do zip — Viewer recebe 403 e o armazenamento das
  fotos não é lido; Admin recebe o conteúdo. Entra antes do código (é a regra que já existe sendo travada por teste);
- **a tela deixa de ter a própria lista.** O `/info` passa a dizer, para o usuário logado, se ele pode ver as fotos
  cruas, derivado da **mesma** lista do servidor. O `roles.ts` do dashboard deixa de existir. Antes de o `/info`
  responder, nenhum botão de JSON ou de download aparece — como o selo de ambiente, que não afirma nada antes da
  resposta.

### 4. O adapter declara o rótulo do conector e se ele agenda

A declaração do adapter (`IAdapterDeclaration`) ganha duas coisas que a barra lateral precisa e que são
características da plataforma, não do cliente: o **rótulo** que aparece no menu (a Avalara declara `Tax Compliance`) e
se o conector **tem agendamento**. Continua valendo o ADR-0037: o adapter declara, o perfil liga, e quem configura não
edita a declaração.

### 5. Toda tela ganha a barra lateral nova

No código de hoje a barra lateral é um componente só, no `App.tsx`, e por isso "toda tela" é automático. O descuido que
o ADR-0039 avisa — **um item apontando para uma tela que não é a dele** — é travado por um teste de tabela sobre o
modelo de navegação: todo item declara a tela que abre, e o teste percorre a árvore inteira.

## Capabilities

### Modified Capabilities

- `module-navigation`: o eixo passa a ser o conector; o Agendamento desce para dentro dele; o `/info` responde a árvore
  de conectores e perde a lista plana; o tenant sem conector ganha um grupo que nomeia a ausência; a frase "é
  apresentação, e não permissão" passa a valer **só para o módulo**, e deixa de valer para a visão técnica.
- `platform-response-trace`: a lista de papéis que veem as fotos cruas passa a ser publicada pelo `/info` para o
  usuário logado, e a tela deixa de ter cópia; a regra de papel do `/trace` e do zip ganha teste de endpoint; antes do
  `/info`, os botões não aparecem.
- `platform-account-connectors`: a declaração do adapter ganha o rótulo do conector e se ele agenda; as duas menções à
  lista plana de módulos do `/info` acompanham a forma nova.

## Non-goals

- **As filas por integração.** São o ADR-0037 §4 e vão junto do Contábil, que é quem as justifica.
- **A carga como linha e o erro agrupado por motivo** (ADR-0039 §3 e §4). Não existe carga; sem ela não há o que
  especificar.
- **Qualquer coisa de AvaTax** (ADR-0038): cálculo, emissão, estados novos, cancelamento, carta de correção. O segundo
  conector é o que torna o eixo novo necessário, e não o que esta fatia entrega.
- **O wizard de provisionamento** (ADR-0039). A configuração continua pelo painel do conector.
- **As URLs base e de token declaradas pelo adapter.** É a change própria do item 5 do `STATUS.md`. Esta fatia
  acrescenta à declaração o rótulo e o agendamento, e nada de endereço.
- **A integração como item de menu.** O ADR-0039 §1 a rejeita; ela continua coluna e filtro, e a página do Fiscal não
  ganha coluna de integração nesta fatia (há uma integração só).
- **Restringir o acesso por módulo.** Continua sendo outra fatia (item 9 do `STATUS.md`): o que deixa de ser
  apresentação aqui é a **visão técnica**, e não o módulo.
- **Um papel de Suporte.** A lista de papéis continua sendo `['Admin']`; o que muda é ela morar num lugar só e a tela
  perguntar ao servidor.
- **O painel de conectores em Configurações.** Ele continua nomeando o conector pelo adapter; alinhar o rótulo
  declarado com aquela tela é cosmético e não entra aqui (design D3).
- **Os defeitos D1, D2 e D3.** Cada um tem a sua change (item 1 do `STATUS.md`).

## Impact

- **Application:**
  - `IAdapterDeclaration` ganha `Label` e `Scheduling`;
  - `InstanceInfo` troca a lista plana `Modules` pela árvore `Connectors` (adapter, rótulo, módulos, agendamento) e
    ganha `CanViewRawJson`, derivado da lista de papéis do servidor;
  - `TenantModules.Of` deixa de ser o que o `/info` devolve e passa a servir a montagem da árvore (a ordem dos módulos
    dentro de um conector continua sendo a dele).
- **Adapters:** `Outbound.Avalara` declara o rótulo `Tax Compliance` e que o conector agenda.
- **Host:** os endpoints das fotos cruas (`/trace`, `/documents/{…}/download`) e a lista de papéis saem do corpo do
  `Program.cs` para um lugar só, que o teste e o `/info` usam; o `/info` muda de forma.
- **Dashboard:** `features/modules/modules.ts` vira o modelo de navegação por conector, com a tela de cada item,
  preservando a tabela de rótulos de módulo que o `features/connectors/connectorPanelRows.ts` também importa; o
  `App.tsx` desenha grupos e itens; `features/auth/roles.ts` deixa de existir, e quem decide os botões do detalhe é o
  `/info`.
- **Testes:** entra um harness de teste de endpoint por papel (TestServer, sem SQL nem Blob — design D8), com os testes
  do `/trace` e do zip; os testes do `/info`; e os testes do modelo de navegação no dashboard.
- **Docs:** ADR-0039 (o campo "Change OpenSpec"), `docs/STATUS.md`.
