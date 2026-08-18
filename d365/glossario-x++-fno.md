# Glossário X++ / D365 F&O — termos pra ler o código

Referência rápida dos termos que usamos no FiscalHub (entidades + business event). Cada um com uma definição curta e, quando dá, onde apareceu no nosso código.

## 1. A linguagem X++

- **X++** — linguagem proprietária do F&O, cara de C#/Java, com **SQL embutido**, compilada pra **.NET IL** (roda no CLR).
- **class / method** — igual C#. `static` = método de classe (chama por `Classe::metodo()`), sem `static` = de instância.
- **extends** — herança. Ex.: `class FS_...BusinessEvent extends BusinessEventsBase`.
- **new()** — construtor. `private void new(){}` = ninguém instancia direto; usa-se um método `static newFrom...()` (padrão de fábrica).
- **`::`** — chama membro estático. Ex.: `FS_...Contract::newFromDocument(doc)`.
- **as** — cast/conversão de tipo. Ex.: `sender as FiscalDocument_BR`.
- **parm methods** — o jeito X++ de getter/setter: `public str parmStatus(str _v = status){ status = _v; return status; }`. Chama **com** argumento pra setar, **sem** pra ler.
- **str / int / boolean / real** — tipos primitivos.
- **Atributos `[...]`** — decoradores de metadado (como `[Attribute]` em C#):
  - `[DataContract]` / `[DataMember]` → marca a classe/método como **serializável** (vira JSON no payload).
  - `[DataEventHandler(...)]` → inscreve o método num **evento de tabela**.
  - `[Wrappable(true)] / [Replaceable(true)]` → controla se o método pode ser estendido/substituído (Chain of Command).

## 2. Helpers de enum (usamos no payload)

- **enumNum(MeuEnum)** — devolve o **id** do enum (referência checada em compilação).
- **enum2int(valor)** — converte o valor do enum pra inteiro.
- **enum2Symbol(enumId, valor)** — devolve o **nome do símbolo** (ex.: `"Approved"`) — foi o que usamos pra mandar o status como texto.
- **enum2str(valor)** — devolve o **rótulo traduzido** (label). (Diferente do Symbol, que é o nome técnico.)

## 3. Metadados / AOT (os "objetos")

- **AOT (Application Object Tree)** — a árvore de todos os objetos de metadado (tabelas, classes, enums, entidades…). É o que você navega no **Application Explorer**.
- **Table** — tabela do banco, mas como **objeto fortemente tipado** com métodos (`insert/update/delete/validateWrite`) e metadados. Ex.: `FiscalDocument_BR`.
- **Field** — coluna da tabela. Ex.: `Status`, `FiscalDocumentNumber`.
- **EDT (Extended Data Type)** — um "tipo de domínio" reutilizável em cima de um primitivo (str/int…), com label, tamanho, relações. Ex.: `FiscalDocumentNumber` é um EDT. Foi a fonte daquele erro `ItemIdBase` (faltava referenciar o package do EDT).
- **Base Enum** — enumeração (conjunto fixo de valores nomeados). Ex.: `FiscalDocumentStatus_BR` = `Blank, Approved, Cancelled, ...`.
- **Data Entity** — **view/projeção** sobre uma ou mais tabelas, usada pra **OData** e **DMF**. Não é tabela real. Ex.: `FS_FiscalDocumentBR`.
- **Staging table** — tabela intermediária do **DMF** (import/export). Read-only pro OData **não precisa** dela.
- **View** — projeção read-only (view SQL).
- **State Machine** — máquina de estados que controla um campo de status e suas transições permitidas.
- **Model** — container dos **seus** objetos (ex.: `FiscalHubIntegration`). **Package** — unidade que empacota model(s) e vira o deployable. **Layer** — conceito antigo de camada (`USR` = user layer, onde ficam customizações).

## 4. Funções intrínsecas (referências seguras)

Devolvem nomes checados em **tempo de compilação** (se você renomear o objeto, o compilador avisa — não é string solta):

- **tableStr(FiscalDocument_BR)** → nome da tabela.
- **tableMethodStr(Tabela, metodo)** → nome de um método **que existe na tabela** (por isso deu erro com `update`, que a tabela não declara).
- **enumNum(FiscalDocumentStatus_BR)** → id do enum.
- **classStr / methodStr / fieldStr** → mesma ideia pra classe/método/campo.

## 5. Extensibilidade (como estender sem tocar no padrão)

- **Event handler** — método `static` inscrito num evento; forma desacoplada de estender.
- **DataEventHandler + DataEventType** — eventos de **tabela**: `Inserting/Inserted/Updating/Updated/Deleting/Deleted`. Usamos `Updating` no gatilho.
- **PreHandlerFor / PostHandlerFor** — roda **antes/depois** de um método específico.
- **Chain of Command (CoC)** — estende um método "embrulhando" ele e chamando `next`. Precisa que o método seja `Wrappable`.
- **Delegate** — um "gancho" que uma classe publica pra outros assinarem.
- **orig()** — devolve os valores do registro **como foram lidos** (antes de mudar). É como a gente detecta "o status mudou": `doc.Status != doc.orig().Status`.
- **xRecord / Common** — tipos base que toda tabela herda. `Common` = buffer genérico de tabela (o `sender` do handler vem como `Common`).

## 6. Business events (o que criamos)

- **BusinessEventsContract** — classe base do **payload** (o JSON que sai). A nossa: `FS_FiscalDocStatusChangedContract`.
- **BusinessEventsBase** — classe base do **evento**. A nossa: `FS_FiscalDocStatusChangedBusinessEvent`.
- **buildContract()** — método que devolve o payload preenchido; o framework chama na hora de enviar.
- **send()** — dispara o evento. É **transaction-safe** (só sai no commit) e o envio é **drenado por batch**.
- **Endpoint** — pra onde o evento vai (ex.: Azure Service Bus Queue "test"). **Catalog** — lista dos eventos. **Activate** — liga o evento a um endpoint + empresa.

## 7. Runtime & transação

- **AOS (Application Object Server)** — o processo servidor que roda a lógica do F&O (é quem serve o OData e gera o `$metadata`).
- **Batch framework** — motor de jobs em segundo plano (agendados/assíncronos); é ele que **drena a fila de business events**. Se não estiver rodando, a mensagem não sai.
- **ttsbegin / ttscommit** — marcam um bloco de **transação** (tudo-ou-nada).
- **CIL / MSIL / .NET IL** — o bytecode que o X++ compila (por isso o stacktrace do erro vinha como `Microsoft.Dynamics.Ax.MSIL...`).

## 8. Integração / ALM

- **OData** — protocolo REST pra consultar/gravar **data entities** por HTTP. **$metadata** = o schema. **cross-company=true** = todas as empresas. **dataAreaId** = a empresa (ex.: `brmf`).
- **DMF (Data Management Framework)** — import/export **em massa** via entidades + staging (arquivos, recurring integrations, data packages).
- **Deployable package** — zip que instala **código** num ambiente via servicing (o jeito de entregar pro cliente).
- **Managed solution** — solution do Dataverse; é o **transporte** que o UDE usa no "Deploy Models to Online Environment" (aditivo — não apaga o que você removeu).
