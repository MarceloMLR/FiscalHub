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

Gerar o package no VS: **Extensions → Dynamics 365 → Deploy → Create Deployment Package** →
seleciona o model `FiscalHubIntegration` → pasta de saída + nome versionado. (Já foi gerado:
`dynamicsax-fiscalhubintegration.<versão>.nupkg` / `AXDeployablePackage_<timestamp>.zip`.)

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
