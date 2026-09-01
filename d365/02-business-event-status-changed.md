# Fase 2 — Custom Business Event: mudança de status da nota fiscal

Objetivo: quando o **status** de uma `FiscalDocument_BR` mudar, disparar um **business event** que manda o **ID da nota** (empresa + número + status novo) pro **Azure Service Bus**. O adapter do FiscalHub consome a fila e busca a nota completa via OData.

Descobertas do ambiente (Application Explorer → `FiscalDocument_BR` [FiscalBooks]):
- Campo do número: **`FiscalDocumentNumber`**
- Campo do status: **`Status`** — enum **`FiscalDocumentStatus_BR`**
  - Valores: `Blank, Approved, Cancelled, Created, Denied, Discarded, Rejected, RejectedNoFix, Reversed, CancelledBySubstitution`
- **Não há** método dedicado de transição de status → o hook robusto é um **post-event handler no `update()`** da tabela (pega a mudança independente de quem escreveu).

Os 3 objetos ficam no model **FiscalHubIntegration** (assim vão no package do cliente). Sugestão de pasta no projeto: `Fiscal/BusinessEvents`.

---

## 1. Contrato (payload) — `FS_FiscalDocStatusChangedContract`

```xpp
[DataContract]
class FS_FiscalDocStatusChangedContract extends BusinessEventsContract
{
    private str company;
    private str fiscalDocumentNumber;
    private str status;

    private void initialize(FiscalDocument_BR _doc)
    {
        company              = _doc.dataAreaId;
        fiscalDocumentNumber = _doc.FiscalDocumentNumber;
        status               = enum2Symbol(enumNum(FiscalDocumentStatus_BR), enum2int(_doc.Status));
    }

    public static FS_FiscalDocStatusChangedContract newFromDocument(FiscalDocument_BR _doc)
    {
        FS_FiscalDocStatusChangedContract contract = new FS_FiscalDocStatusChangedContract();
        contract.initialize(_doc);
        return contract;
    }

    [DataMember('Company')]
    public str parmCompany(str _company = company)
    {
        company = _company;
        return company;
    }

    [DataMember('FiscalDocumentNumber')]
    public str parmFiscalDocumentNumber(str _number = fiscalDocumentNumber)
    {
        fiscalDocumentNumber = _number;
        return fiscalDocumentNumber;
    }

    [DataMember('Status')]
    public str parmStatus(str _status = status)
    {
        status = _status;
        return status;
    }
}
```

O JSON que sai fica assim:
```json
{ "Company": "brmf", "FiscalDocumentNumber": "000001", "Status": "Approved" }
```

---

## 2. Evento — `FS_FiscalDocStatusChangedBusinessEvent`

```xpp
[BusinessEvents(
    classStr(FS_FiscalDocStatusChangedContract),
    'FS_FiscalDocStatusChangedBusinessEvent',
    'Disparado quando o status da nota fiscal (FiscalDocument_BR) muda',
    ModuleAxapta::Ledger)]
public final class FS_FiscalDocStatusChangedBusinessEvent extends BusinessEventsBase
{
    private FiscalDocument_BR fiscalDocument;

    private void new()
    {
    }

    public static FS_FiscalDocStatusChangedBusinessEvent newFromDocument(FiscalDocument_BR _doc)
    {
        FS_FiscalDocStatusChangedBusinessEvent businessEvent = new FS_FiscalDocStatusChangedBusinessEvent();
        businessEvent.fiscalDocument = _doc;
        return businessEvent;
    }

    [Wrappable(true), Replaceable(true)]
    public BusinessEventsContract buildContract()
    {
        return FS_FiscalDocStatusChangedContract::newFromDocument(fiscalDocument);
    }
}
```

> **CRÍTICO:** o atributo **`[BusinessEvents(contrato, 'nome', 'descrição', ModuleAxapta::Módulo)]`** é o que **registra o evento no catálogo**. Só estender `BusinessEventsBase` **não basta** — sem esse atributo, o *Rebuild business events catalog* **não descobre** a classe (o evento compila e deploya, mas nunca aparece no catálogo). Parâmetros: (1) `classStr` do contrato, (2) o **Business event ID** que aparece na grid, (3) a descrição, (4) o módulo (`ModuleAxapta::Ledger`, `::Tax`, etc.).

---

## 3. Gatilho (handler) — `FS_FiscalDocument_BR_EventHandler`

```xpp
class FS_FiscalDocument_BR_EventHandler
{
    // Dispara quando a nota fica "pronta pra integrar" (chega no status alvo),
    // cobrindo os DOIS jeitos de finalizar:
    //  - INSERT: nota que já nasce no status final (típico de ENTRADA/fornecedor, NF-e já autorizada)
    //  - UPDATE: nota que transita pro status final (típico de SAÍDA)

    [DataEventHandler(tableStr(FiscalDocument_BR), DataEventType::Inserting)]
    public static void FiscalDocument_BR_onInserting(Common sender, DataEventArgs e)
    {
        FiscalDocument_BR doc = sender as FiscalDocument_BR;
        if (FS_FiscalDocument_BR_EventHandler::isReadyToIntegrate(doc.Status))
        {
            FS_FiscalDocStatusChangedBusinessEvent::newFromDocument(doc).send();
        }
    }

    [DataEventHandler(tableStr(FiscalDocument_BR), DataEventType::Updating)]
    public static void FiscalDocument_BR_onUpdating(Common sender, DataEventArgs e)
    {
        FiscalDocument_BR doc = sender as FiscalDocument_BR;
        if (doc.Status != doc.orig().Status
            && FS_FiscalDocument_BR_EventHandler::isReadyToIntegrate(doc.Status))
        {
            FS_FiscalDocStatusChangedBusinessEvent::newFromDocument(doc).send();
        }
    }

    // Decisão de NEGÓCIO: quais status significam "nota finalizada, pode integrar".
    // Approved (autorizada) é o natural. Dá pra incluir Cancelled/Reversed p/ avisar o
    // conector de cancelamento, etc.
    private static boolean isReadyToIntegrate(FiscalDocumentStatus_BR _status)
    {
        switch (_status)
        {
            case FiscalDocumentStatus_BR::Approved:
                return true;
            default:
                return false;
        }
    }
}
```

Por que assim (não dá double-fire): nota que **nasce** Approved dispara **1x no insert**; nota que nasce `Created` e depois **vira** Approved dispara **1x no update** (e não de novo, porque já está Approved). Cada nota integra uma vez, no momento em que fica pronta.

> Usamos **`DataEventHandler` (table event)** e NÃO `PostHandlerFor(... tableMethodStr(..., update))`, porque a `FiscalDocument_BR` não declara um `update()` próprio (herda de `Common`) — e o `tableMethodStr` só aceita método existente na tabela, o que dá o erro *"The instance method designated by argument 'update' does not exist"*. O table event pega a mudança sem depender disso.

Variações:
- **Disparar só num status alvo** (ex.: aprovado):
  ```xpp
  if (doc.Status != doc.orig().Status && doc.Status == FiscalDocumentStatus_BR::Approved)
  ```
- **Notas que já nascem num status** (opcional): adicione outro handler com `DataEventType::Inserting`.

Notas técnicas:
- No evento `Updating`, `doc` tem o valor NOVO e `doc.orig()` tem o valor ATUAL no banco → a comparação detecta a mudança real.
- `send()` é **transaction-safe**: o framework só envia no commit (se der rollback, não sai).
- Precisa do **batch rodando** no ambiente (o envio é feito por threads de batch → há uma pequena latência).

---

## 4. Onde criar no projeto

Projeto FiscalHubIntegration → botão direito na pasta (ex.: `Fiscal/BusinessEvents`) → **Add → New Item → Dynamics 365 Items → Code → Class**. Crie 3 classes com os nomes acima e cole o corpo.

## 5. Build + publicar + ativar

1. **Build** do projeto (corrige erros se houver).
2. **Deploy** pro ambiente (o fluxo que já dominamos: Deploy Models to Online Environment + Sync — ou, no cliente, o deployable package).
3. No F&O: **System administration → Setup → Business events → Business events catalog** → menu **Manage → Rebuild business events catalog** → o `FS_FiscalDocStatusChanged...` aparece.
4. Selecione o evento → **Activate** → escolha o **endpoint** (o `test` do Service Bus, ou uma fila dedicada) + a **empresa** (`brmf`).

## 6. Testar

- Mude o status de uma nota (`FiscalDocument_BR`) na empresa `brmf`.
- A mensagem deve cair na **fila do Service Bus** (dá pra ver no Service Bus Explorer / Azure).
- Depois: plugar no **consumidor do FiscalHub** (que enfileira `DocumentReference (Trigger=Event)` → esteira → adapter busca por ID no OData).

---

## Código vs. Config (o que vai no package)

- **Vai no package (código):** as 3 classes.
- **NÃO vai (config do ambiente):** a **ativação + binding** (evento → endpoint → empresa) e o **endpoint do Service Bus** (connection string/Key Vault). O cliente configura isso, ou entregamos como **data package (DMF)** pra automatizar o binding.

---

## Resultado — Fase 2 validada end-to-end (07/08/2026)

O custom business event **`FS_FiscalDocStatusChangedBusinessEvent`** disparou de ponta a ponta. Mudando o `Status` de uma `FiscalDocument_BR` para `Approved` (via runnable com `update()` **record-based**), a mensagem caiu no Service Bus com o payload correto:

```json
{ "BusinessEventId": "FS_FiscalDocStatusChangedBusinessEvent",
  "Company": "brmf", "FiscalDocumentNumber": "1", "Status": "Approved" }
```

Runnable de teste `FS_TestFireStatusChange` (roda no AOS online):
`https://<ambiente>.operations.dynamics.com/?cmp=brmf&mi=SysClassRunner&cls=FS_TestFireStatusChange`

### ⚠️ Limitação crítica: table event × posting (set-based)

Eventos de tabela (`DataEventHandler` Inserting/Updating) **NÃO disparam** quando o registro é gravado por **`doInsert()`/`doUpdate()`** ou por **operações set-based** (`insert_recordset`/`update_recordset`) — porque vão direto no banco, pulando a lógica X++ da tabela. E o **posting** do F&O grava a `FiscalDocument_BR` justamente assim.

Consequência prática (comprovada): **postar** uma nota (fluxo real) **não** aciona o business event de tabela; uma transição via **`update()` record-based** (cancelamento manual, ou o runnable) **aciona**.

### Decisão pra produção (gatilho real-time confiável)

O hook tem que casar com COMO o dado é gravado no posting (set-based). Opções, em ordem de recomendação:

1. **Data event** (aba *Data event catalog*) — baseado em **change tracking** (nível de banco), então **pega gravações set-based/doInsert**. Registrar Create/Update na entidade `FS_FiscalDocumentBR` e o consumidor filtra `Status == Approved`. ← recomendado pra tempo-real.
2. **Polling por status** via OData (caminho `ScheduledDaily`/`Manual` do FiscalHub, ADR-0022). ← mais simples e à prova de bala.
3. **CoC no método de negócio** que autoriza a nota (classe de processamento, não a tabela) — mais cirúrgico, exige achar o método.

O **custom business event de tabela** (este) fica validado e útil para transições que passam por `update()` real (ex.: cancelamento manual) — e serviu para **aprender** todo o mecanismo (criar contrato/evento/handler, o atributo `[BusinessEvents]`, catálogo, ativação, Service Bus).

---

## Atualização (2026-08/09) — CoC reforçada em update() + doUpdate() (adotado)

A recomendação acima (data event / polling como caminho principal) foi **revista**. O caminho adotado é
a **CoC na tabela cobrindo `update()` E `doUpdate()`**, disparando na transição para **Approved** e
**Cancelled**. Motivo: `doUpdate()` também é interceptado, e `update_recordset` geralmente **degrada
para linha-a-linha** quando `update()` está estendido — cobrindo o grosso dos caminhos de gravação.

Payload enriquecido (para buscar a nota depois): `Company, FiscalDocumentRecId (RefRecId),
FiscalDocumentNumber, FiscalDocumentSeries, FiscalDocumentDate (ISO), Status`. O evento passou a receber
o **buffer** (`newFromDoc(FiscalDocument_BR)`) e montar o contract no `buildContract()`.

Validado no **table browser** (two-step `Created → Approved`, que chama `update()` record-based) tanto na
**entrada** quanto na **saída**; o evento caiu no endpoint `test`. **Furo residual** (SQL cru /
`skipDataMethods`) fica coberto por um **poll de reconciliação** na `FiscalDocument_BR` — mantido no
**backlog** como rede de segurança (ativar se aparecer nota Approved sem disparo).

Envio de business event é por **batch** (job do outbox → endpoint); em produção depende do job rodar.
