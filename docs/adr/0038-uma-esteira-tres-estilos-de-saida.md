# ADR-0038: Uma esteira, três estilos de saída — e o que a emissão muda

- **Status:** Aceito
- **Data:** 2026-10-13
- **Revisa:**
  - **[ADR-0003](0003-despacho-assincrono-status-integracao.md):** o despacho assíncrono deixa de ser o único estilo. O
    status normalizado continua, e ganha estados nas duas pontas.
  - **[ADR-0026](0026-conector-nao-validador.md):** a postura sobrevive à emissão; o §4 deste ADR nomeia o que muda.
- **Complementa:** [ADR-0036](0036-conta-de-plataforma-e-varios-conectores.md) e
  [ADR-0037](0037-conector-modulo-integracao.md).
- **Change OpenSpec:** a propor.

## Contexto

O AvaTax traz duas coisas que a esteira nunca viu: um **cálculo de imposto**, que a Avalara responde na hora, e uma
**emissão**, em que o hub monta o documento e a Avalara o transmite à SEFAZ ou à prefeitura. E o Tax Compliance traz
as **cargas em lote** do Contábil e do Inventário (ADR-0037).

Um rascunho anterior deste desenho dizia que eram três *formas de fluxo* diferentes e que generalizar a esteira seria
um erro. **Estava errado.** A descrição do fluxo real desmonta isso:

> ERP → motor de cálculo → o cálculo volta → devolvemos ao ERP → o cliente aprova no ERP → uma trigger manda a ordem →
> buscamos a informação no ERP e mandamos para a esteira de emissão → a Avalara transmite.

E o que o usuário quer ver é um documento atravessando **Pendente → Calculado → Enviado → Autorizado ou Rejeitada**.
Um documento, um ciclo de vida. Não são três fluxos: é um, com saídas de formatos diferentes.

**A pergunta que fechou a decisão:** o que de fato varia entre captura, cálculo, emissão e carga? Não é o ciclo de vida
do documento — é **como a saída fala com a plataforma**.

## Decisão

### 1. A esteira é uma só; o que varia é o estilo de saída

| Estilo | Quem usa | Como termina |
|---|---|---|
| **Despacho e confirmação** | Tax Compliance (captura), AvaTax (emissão) | envia e confirma depois, pelo poll |
| **Chamada síncrona** | AvaTax (cálculo) | a resposta vem na própria requisição |
| **Carga em lote** | Contábil, Inventário | N registros por requisição, recusa por registro (ADR-0037 §6) |

`ProcessAsync`, o `IProcessingStore`, a idempotência, o retry, a DLQ e o registro do documento servem os três. O que
muda é a porta de saída, e isso é uma implementação da porta — não uma esteira nova.

### 2. O cálculo tem documento e tem estado, mesmo sendo síncrono

Síncrono diz respeito à **conversa com a Avalara**, não ao ciclo de vida do documento. O documento entra na esteira,
é persistido e aparece no painel.

**E o estado "Calculado" não é a resposta da Avalara — é a devolução ao ERP ter dado certo.** Um cálculo que a
plataforma respondeu mas que não voltou ao ERP **não é** Calculado: para o cliente, aquele cálculo não existe. Esse é
um modo de falha próprio, com dono próprio, e precisa de estado próprio.

### 3. O ciclo de vida cresce nas duas pontas

O status normalizado do ADR-0003 continua sendo o vocabulário do hub. A emissão o estende:

- **antes do envio:** um estado para o documento calculado e à espera da aprovação no ERP;
- **depois da confirmação:** um estado para o documento cancelado.

**A carta de correção é evento, não estado.** Uma nota autorizada com duas cartas continua autorizada; as cartas são um
histórico ordenado preso ao documento. Tratá-las como estado faria "Autorizada" e "Corrigida" competirem por um campo
que só comporta um, e a nota é as duas coisas.

### 4. O ADR-0026 sobrevive à emissão; o que muda é o raio

"Conector, não validador" diz que quem julga conteúdo fiscal é a plataforma. Na emissão isso continua: o hub monta o
JSON a partir do ERP, **a Avalara emite**, a Avalara recusa, e o motivo dela aparece no registro sem reescrita. O hub
não vira segunda fonte de verdade fiscal.

Mas a emissão muda a **consequência de errar**, e isso precisa estar escrito:

- na captura, um campo errado do nosso lado é uma nota **já emitida** que não se registrou. O erro é nosso, o conserto é
  nosso, e o mundo não viu;
- na emissão, um campo errado é uma **nota emitida errada, no mundo**, e o conserto passa por carta de correção ou
  cancelamento.

A linha do ADR-0026 — "o hub rejeita só o que impede a requisição de existir" — continua certa. O que ela pede, quando
a emissão chegar, é o que **não** é julgar conteúdo: conferir que o ERP mandou o que diz ter mandado antes de montar, e
mostrar o payload antes do envio. Não inventar conteúdo não é a mesma coisa que julgá-lo.

### 5. Os dois conectores se encontram no ERP, e não no hub

Um cliente pode ter Tax Compliance **e** AvaTax, e a mesma nota física passa pelos dois — em momentos diferentes:

```
AvaTax emite  →  a nota autorizada volta ao ERP  →  lá ela fica completa
                                                     →  o feed de mudanças do Tax Compliance a descobre
```

**Nenhum conector entrega para o outro.** O Tax Compliance captura do ERP, como captura qualquer nota já emitida, e não
sabe que o AvaTax existe. Consequências:

- a mesma nota física tem **dois registros** no hub, com ciclos de vida diferentes, e isso é correto — não é
  duplicidade a corrigir;
- nenhum acoplamento novo entre conectores entra no código;
- o ERP continua sendo o sistema de registro, que é o que o ADR-0023 já havia estabelecido para a captura.

## Consequências

- **A `IntegrationStatus` ganha estados**, e toda tela que soma por status precisa ser revista — inclusive o card "Com
  erro" do painel fiscal, que hoje soma três estados num só.
- **A porta de saída ganha operações além do envio:** cancelar, corrigir, reenviar. Elas são **declaradas por
  integração**: o conector de captura não as tem, e pedir por elas ali tem que falhar como configuração, e não como erro
  de plataforma.
- **O poll não serve o cálculo.** Ele continua existindo para os estilos que confirmam depois, e a porta síncrona não o
  usa. Uma implementação que mandasse tudo pelo poll faria o ERP esperar por nada.
- **O defeito D1 fica mais caro.** Com mais estados e mais integrações passando pelo poll, um poll que trava para todos
  os tenants (`docs/STATUS.md`) deixa de ser um problema de um fluxo e vira de vários. A ordem de correção não muda, mas
  a prioridade sobe.
- **Não entra agora:** nenhum código de cálculo, de emissão, de cancelamento ou de carta de correção. Entra o
  reconhecimento de que a esteira é uma, para que a fatia seguinte não construa uma segunda.

## Alternativas consideradas

- **Generalizar a esteira para servir os três com uma abstração só de saída.** Rejeitada: a chamada síncrona e o
  despacho com poll têm contratos de erro diferentes — um devolve o resultado, o outro devolve um protocolo — e a
  abstração que cobrisse os dois esconderia justamente a diferença que o chamador precisa ver.
- **Uma esteira separada para o AvaTax.** Rejeitada no §1: o ciclo de vida é o mesmo, e duas esteiras significam duas
  idempotências, duas DLQs e dois lugares para o mesmo defeito.
- **A carta de correção como estado.** Rejeitada no §3.
- **O Tax Compliance receber a nota direto do AvaTax, dentro do hub.** Rejeitada no §5: criaria acoplamento entre
  conectores, e faria o hub divergir do ERP, que é quem tem o registro.
- **"Calculado" significar que a Avalara respondeu.** Rejeitada no §2: esconderia a falha mais provável do fluxo, que é
  a volta ao ERP.
