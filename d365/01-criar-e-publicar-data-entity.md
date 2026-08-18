# Fase 1 — Criar e publicar a data entity `FS_FISCALDOCUMENT_BR`

Objetivo: criar uma **data entity de nome fixo** que projeta a `FiscalDocument_BR` padrão, marcá-la como
pública e **ver ela respondendo no OData**. Sem gatilho e sem pacote ainda — só provar que a entidade
existe e é consultável.

> Guia para quem não desenvolve F&O no dia a dia. Onde depende do seu ambiente, está marcado com
> **⚠️ CONFIRMAR**. Faça no ambiente de **DEV** (UDE), nunca direto em produção.

---

## 0. Pré-requisitos

- Ambiente **de desenvolvimento** F&O no modelo novo (UDE), com **Visual Studio** aberto e conectado a
  ele (as *Finance and Operations developer tools* instaladas). No UDE, o VS local aponta pro ambiente
  de dev na nuvem.
- Acesso de **administrador** nesse ambiente de dev (pra sincronizar banco e testar OData).
- A URL do ambiente, algo como `https://{seu-ambiente}.operations.dynamics.com` (**⚠️ CONFIRMAR** a sua).

---

## 1. Descobrir o nome exato da tabela de origem

Antes de tudo, confirme como a tabela fiscal se chama **no seu ambiente**. A localização BR normalmente
usa `FiscalDocument_BR`, mas pode variar por versão/customização.

- No Visual Studio: **Dynamics 365 → Application Explorer** (ou View → Application Explorer).
- Na busca, digite `FiscalDocument` e veja as tabelas (`Data Model → Tables`). Anote o **nome exato** da
  tabela do cabeçalho da nota (**⚠️ CONFIRMAR**, provavelmente `FiscalDocument_BR`) e, se for buscar
  linhas/itens depois, a de linhas.
- Clicando na tabela, o **Application Explorer** mostra em qual **package/model** ela está (ex.:
  `ApplicationSuite`). Anote — o nosso model vai precisar **referenciar** esse package.

---

## 2. Criar um Model próprio (`FiscalHubIntegration`)

Um *model* é o container do nosso código; criar o nosso separa a customização do padrão e permite
exportar o pacote depois.

1. **Dynamics 365 → Model Management → Create model**.
2. Nome do model: `FiscalHubIntegration`.
3. Marque **Create new package** (pacote próprio — importante pra exportar limpo depois).
4. Em **Select referenced packages**, marque o package que contém a `FiscalDocument_BR` (do passo 1, ex.:
   `ApplicationSuite`) e `ApplicationPlatform`/`ApplicationFoundation` se pedir. Sem essa referência, a
   entidade não “enxerga” a tabela de origem.
5. Finish. Ele oferece criar um **projeto** associado — aceite (ou crie no passo 3 abaixo).

---

## 3. Criar o projeto do Visual Studio

Se não criou junto: **File → New → Project → Finance Operations → Finance Operations Project**. Associe ao
model `FiscalHubIntegration`. Salve.

---

## 4. Criar a Data Entity pelo assistente

1. No projeto: **Add → New Item → Data Model → Data Entity**.
2. Nome (AOT): `FS_FiscalDocumentBR`. **Add** → abre o *Data Entity Wizard*.
3. **Primary datasource**: selecione a tabela do passo 1 (ex.: `FiscalDocument_BR`).
4. **Entity category**: `Transaction` (nota fiscal é transacional).
5. **Public collection name**: `FS_FiscalDocumentBR` — **é o nome que vira a URL do OData** (`/data/FS_FiscalDocumentBR`). **Public entity name**: `FS_FiscalDocumentBR`.
6. **Staging table / Enable data management**: para leitura via OData **não é obrigatório**. Se aparecer a
   opção de gerar staging, pode **desmarcar** (só vamos ler). Se o assistente criar mesmo assim, tudo bem.
7. Finish. Ele gera a entidade e os artefatos (privilégios/roles automáticos).

### 4.1. Escolher os campos (comece mínimo)

Abra a entidade gerada, expanda **Fields** e deixe, pra este primeiro teste, só o essencial:

- a **chave** da nota (o RecId / número do documento — **⚠️ CONFIRMAR** o campo-chave no seu ambiente),
- o **status** (o campo que usaremos no gatilho depois),
- 1–2 campos pra “ver dado” (ex.: número, data).

Depois a gente amplia a projeção com tudo que monta o JSON de saída. Menos é mais neste teste.

---

## 5. Marcar como pública (exposição no OData)

Clique na **data entity** (nó raiz) → **Properties** e confirme:

- **Is Public = Yes** ← sem isto, o OData não expõe.
- **Public Collection Name = FS_FiscalDocumentBR** e **Public Entity Name = FS_FiscalDocumentBR**.
- **Data Management Enabled**: pode deixar `No` para o teste de leitura.

---

## 6. Build + sincronizar o banco

1. **Build → Build Solution** (ou botão direito no projeto → Build). Corrija erros se houver.
2. **Dynamics 365 → Synchronize database** (sincroniza os artefatos da entidade no banco do dev). Pode
   levar alguns minutos.

---

## 7. Liberar acesso pra você testar

Pra este primeiro teste, use o **seu próprio usuário** (que já é admin) — assim você isola “a entidade
funciona?” de “a permissão está certa?”. A app registration/role de integração a gente configura na
Fase 2. Se o seu usuário é System Administrator, ele já lê a entidade.

---

## 8. Testar no OData ✅

Três formas, da mais simples à mais completa:

**(a) Ver se a entidade está exposta** — abra no navegador (logado no F&O):

```
https://{seu-ambiente}.operations.dynamics.com/data/$metadata
```

Procure por `FS_FiscalDocumentBR` no XML. Se aparecer, ela está pública.

**(b) Consultar dados** — no navegador:

```
https://{seu-ambiente}.operations.dynamics.com/data/FS_FiscalDocumentBR?$top=1&cross-company=true
```

Deve voltar um JSON com 1 registro. (`cross-company=true` traz de todas as empresas; sem isso, só a
empresa default.)

**(c) Com token (como o conector fará)** — no Postman/cURL, pegue um token Entra pro recurso do F&O e
chame a mesma URL com `Authorization: Bearer …`. É assim que o adapter vai ler. Detalhamos a app
registration na Fase 2.

### Filtro por status (prévia do que o adapter usará)

```
/data/FS_FiscalDocumentBR?$filter=FiscalDocumentStatus eq 'Aprovada'&cross-company=true
```

(**⚠️ CONFIRMAR** o nome do campo/valor de status.)

---

## Deu certo? / Problemas comuns

- **Entidade não aparece no `$metadata`**: faltou `Is Public = Yes`, ou não fez o *Synchronize database*,
  ou o build falhou.
- **403/401**: permissão — teste primeiro com usuário admin (passo 7).
- **Vazio mas sem erro**: não há registro na empresa default → use `cross-company=true`, ou a tabela
  origem está vazia nesse ambiente de dev (crie uma nota de teste).
- **Erro de referência no build**: o model não referencia o package da `FiscalDocument_BR` (passo 2.4).

---

## Quando funcionar

Me avise que:
1. Confirmamos o **nome exato** da tabela origem, do **campo-chave** e do **campo/valor de status**
   (vou precisar deles pro adapter e pro business event).
2. Passamos pra **Fase 2** (Business Event no status → Azure Service Bus).

---

### Referências

- OData e `Is Public` / Public Collection Name — Microsoft Learn: <https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/odata>
- Visão geral de data entities — Microsoft Learn: <https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/data-entities/data-entities>
- Unified Developer Experience (F&O) — Microsoft Learn: <https://learn.microsoft.com/en-us/power-platform/developer/unified-experience/finance-operations-dev-overview>
