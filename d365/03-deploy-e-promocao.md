# Fase 3 — Ambiente, deploy e promoção do conector D365 F&O

Descobertas desta fase sobre **como o código X++ sai do VS e chega no ambiente** — do dev até o
cliente. Complementa `02-business-event-status-changed.md`.

---

## Ambiente atual: Unified Developer (PPAC, sem LCS)

Confirmado no **Power Platform Admin Center** (o ambiente `FiscosysDev`):

- **Tipo de implantação: Desenvolvedor Unificado** (Unified Developer, ferramentas de dev habilitadas).
- **Gerenciado pelo PPAC** (Dataverse-linked). **Não há projeto no LCS** (Asset Library / runbook
  clássico **não existe** aqui).
- Deploy de código de dev = **Extensions → Dynamics 365 → Deploy → "Deploy Models to Online
  Environment"** (direto do VS para o AOS online). CoC não mexe em schema → **não precisa DB sync**.
- **Importante:** um Unified **Developer** é ambiente de **dev**. Não tem superfície de "aplicar
  deployable package". Aplicar um package de verdade exige um **Sandbox Standard** (não-developer),
  que este ambiente não é.

> Consequência prática: "gerar o package e aplicar como o cliente" **não é executável neste dev env**
> — falta o alvo (um sandbox). O package pode ser **gerado** (aprende-se o artefato), mas o *apply*
> real fica para quando houver um sandbox.

---

## Os dois mundos: build ≠ deploy

- **Deploy Models to Online Environment** = inner-loop de dev. Rápido, direto do VS. **Cliente nunca
  faz assim.**
- **Deployable package** = o artefato de promoção do cliente. Gera-se com **Create Deployment
  Package** no VS (ou por pipeline). É **imutável e versionado**.

**O pacote do Visual Studio no UDE sai vazio: não mande para cliente.** O **Create Deployment Package** do VS gera o
`AXDeployablePackage_<timestamp>.zip`, mas sem os binários. Conferido em 2026-10-16:

- o `tools/InstallConfig.json` do `AOSService/Packages/dynamicsax-fiscosyshubintegration.7.0.7858.27.nupkg` aponta
  para o `dynamicsax-fiscosyshubintegration.7.0.7858.27.zip`, e esse zip não está no pacote;
- o pacote de abril (`fscentities`) tinha o zip em `AOSService/Packages/files/`, mas com 951 bytes, só o `.xref`;
- o `ModelUtil.exe -convertToUnifiedPackage` falha pelo mesmo motivo (`…\AOSService\Packages\files` não existe).

A Microsoft diz isso no FAQ do UDE: no UDE o VS foi para o formato unificado, e o pacote completo do LCS sai do pipeline
do Azure DevOps. O deploy pelo VS (inner loop) continua valendo para o fiscosysdev.

## Pacote para o cliente: os dois modelos de ambiente

O cliente administra o F&O em um de dois lugares, e cada um pede um formato de pacote:

| | LCS (modelo antigo) | Power Platform admin center (modelo novo, "unificado") |
|---|---|---|
| Como reconhecer | o ambiente está num projeto do LCS; pacotes vão para a Biblioteca de ativos e entram por **Manter › Aplicar atualizações** | não há projeto no LCS; o ambiente é Sandbox ou Produção no PPAC, com o F&O instalado por template |
| Formato | software deployable package (`AXDeployablePackage_*.zip`) | unified package |
| Como aplica | upload na Biblioteca de ativos, aplica no sandbox e depois promove para produção | `pac package deploy` ou a tarefa do Azure DevOps, no sandbox e na produção |
| Cuidado | **all-in-one**: ambiente self-service reconstrói tudo a partir do pacote. Aplicar só o nosso desinstala as outras customizações do cliente. O nosso tem de entrar no pacote all-in-one dele (merge na Biblioteca de ativos ou no pipeline do parceiro) | combinar com o parceiro do cliente quem aplica e com quais outros módulos |

**Um build, dois formatos.** Pipeline no Azure DevOps (agente hospedado `windows-latest`), compilando `d365/model` contra
os NuGet da versão base 10.0.47 (compilador, Platform, Application 1 e 2, Application Suite). Os NuGet saem da Biblioteca
de ativos compartilhada do LCS ou pela extensão Power Platform do Visual Studio. A tarefa **Create Deployable Package**
v3 gera, no mesmo build, o pacote do LCS e o unified package (opção **Create Power Platform Unified Package**). Os
binários são os mesmos, só muda a embalagem.

**O que perguntar ao cliente antes de gerar:**

1. Os ambientes (sandbox e produção) são administrados pelo LCS (projeto, Biblioteca de ativos, Aplicar atualizações) ou
   pelo Power Platform admin center, sem projeto no LCS?
2. Quem aplica pacotes hoje (TI interna ou parceiro) e como: pela Biblioteca de ativos ou por pipeline?
3. Há outras customizações ou soluções de ISV instaladas? Se sim, quem monta o pacote all-in-one?
4. A versão de cada ambiente (Ajuda e suporte › Sobre): 10.0.47 ou superior.

**Acesso:** se o cliente já tem um app registrado para integração (o client ID do conector legado), basta atribuir a role
`FiscosysHubIntegration` ao usuário ligado a esse client ID em **Aplicativos do Microsoft Entra ID**, em cada ambiente.
Se esse usuário já tem roles amplas (o legado costuma usar System Administrator), o acesso funciona sem a nossa role e o
teste não prova nada: quando o legado sair, deixe o usuário só com a `FiscosysHubIntegration`. O segredo novo vai para o
hub por canal seguro, nunca por e-mail.

Referências: [FAQ do UDE](https://learn.microsoft.com/power-platform/developer/unified-experience/finance-operations-faq),
[all-in-one deployable packages](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/aio-deployable-packages),
[pipeline com agente hospedado](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/hosted-build-automation),
[pacote nos dois formatos](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/pipeline-create-deployable-package),
[LCS × PPAC para parceiros](https://learn.microsoft.com/power-platform/admin/unified-experience/partners-systems-implementers).

---

## Modelo ISV: 1 código → 1 build → N clientes

O lado X++ do FiscalHub é um **hook genérico** (business event + CoC + data entity). **Não tem lógica
de cliente.** Logo:

- **Um único build** produz **um único package**, aplicado em **N clientes**. Não há pipeline por
  cliente.
- A variação por cliente é **configuração**, não código: o **endpoint do Service Bus** (ativação do
  business event apontando pro nosso SB) e a conta Avalara vivem em config, no ambiente do cliente.
- Só viraria múltiplos packages se um cliente exigisse **X++ bespoke divergente** — anti-padrão de
  produto; evita-se empurrando a variação pra config.

---

## Como o package chega no ambiente do cliente (build vs apply)

- **Build (seu):** NÃO toca o ambiente do cliente. Só compila e publica o `.zip` como artefato (ex.:
  Azure DevOps Artifacts). Ambiente-agnóstico — o mesmo artefato serve todos.
- **Apply (toca um ambiente):** exige **credencial autorizada** naquele ambiente. Dois modelos:
  1. **Entrega o arquivo** — você manda o `.zip`, o cliente aplica pelo processo dele (mais comum no ISV).
  2. **Você aplica** — o cliente registra um **service principal** (app registration) com direito de
     deploy e te passa os dados; você cria uma **service connection** no seu Azure DevOps e um
     **release pipeline** sobe+aplica via a **API de deployment** do ambiente. Sem credencial, o
     pipeline não alcança o ambiente (é a fronteira de segurança).

### Pipeline de build (moderno, recomendado)

Agente **MS-hosted** (windows-latest) + os pacotes do compilador FnO e da aplicação vindos como
**NuGet** (feed do LCS/shared asset library). Etapas: restore (compilador + `*.DevALM.BuildXpp`) →
MSBuild compila só o model → (opcional BP/testes) → **Create Deployable Package** → publish artifact.
Sem Build VM dedicada.

### Produção

Prod **pode** estar no pipeline, mas com **gate de aprovação** e/ou apply manual pelo admin. O comum
no ISV: você automatiza até o **sandbox** do cliente; **produção** o cliente aplica/aprova (mesmo
package que passou no sandbox; há **downtime** — o runbook para o AOS; deploy **agendado**).

---

## Service Bus: credencial por cliente

O Service Bus é **nosso**, não do cliente. Não entregar a mesma chave raiz pra todo mundo: cada
cliente recebe uma **SAS send-only** própria (e, de preferência, fila/tópico por cliente), pra
**isolar** e **revogar** por cliente. Casa com o multi-tenant do FiscalHub.

---

## Business events: entrega por batch

O `.send()` do business event enfileira num outbox que um **job de business events** empurra pro
endpoint. Se o batch não estiver rodando, a mensagem fica no outbox. Nos testes, o endpoint usado foi
o **`test`** (ativado por empresa/legal entity no catálogo).
