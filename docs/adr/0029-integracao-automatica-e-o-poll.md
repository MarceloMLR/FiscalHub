# ADR-0029: Integração automática é o `poll.enabled` — o perfil perde o flag de tempo real

- **Status:** Aceito
- **Data:** 2026-09-27
- **Revisa:**
  - **ADR-0019:** o `realtime` sai dos campos comuns tipados do perfil. Se o tenant integra sozinho passa a ser o
    `poll.enabled` das settings de entrada, e só ele.
- **Change OpenSpec:** `openspec/changes/automatic-integration-switch`, capacidades `automatic-integration` e
  `change-feed-polling`.

## Contexto

O ADR-0019 tipou o `realtime` como campo comum do perfil, para a "capacidade de tempo real" que varia por cliente. O que
o coletor lê para rodar, porém, é outro dado: o `poll.enabled` das `InboundSettings` (ADR-0024). O flag era gravado,
servido pelo `/info` e só desenhava um selo na barra lateral.

- **Os dois divergiam já no seed.** O tenant-a nascia com `Realtime = true` e `poll.enabled = false`, e a tela dizia
  "Tempo real ligado" com o coletor parado.
- **O interruptor mexia no campo errado.** A tela tinha interruptor para o flag e nenhum para o poll. Ligar ou
  desligar o coletor de um tenant exigia SQL.
- **O nome prometia o que ninguém faz.** "Tempo real" soa como integração por evento, que nenhum ERP nosso faz hoje. A
  captura é por polling (ADR-0023), e a pergunta do usuário é se o conector roda sozinho.

## Decisão

**A integração automática do tenant é o `poll.enabled` das settings de entrada, e o perfil não guarda esse estado em
nenhum outro lugar.**

1. **Fonte única.** O `realtime` sai do `TenantConnectorProfile`, dos contratos do `/connector` e da tabela (migração
   `RemoveConnectorProfileRealtime`).
2. **A tela grava o `poll.enabled`.** O interruptor "Integração automática" é uma vista do `poll.enabled` dentro das
   settings de entrada, gravada pelo mesmo `PUT /connector`, sem campo próprio no pedido.
   - Ligar grava `true` e preserva o resto da seção.
   - Desligar grava `false` e mantém a seção. Sem ela, o poller avisaria que o poll "não está configurado".
3. **A leitura é derivada.** O `/info` responde `automaticIntegration`, verdadeiro só quando o adapter de entrada varre
   e o `poll.enabled` está ligado. Settings ilegíveis contam como desligada: o selo não afirma o que não se lê.
4. **Varrer é ter feed registrado.** Um adapter de entrada varre quando o host tem um `IDocumentChangeFeed` com a origem
   dele, e o `/info` usa as mesmas origens que o poller consome. Na tela, a marca é provisória
   (`SCANNING_INBOUND_ADAPTERS`, em `adapterSchemas.ts`), ao lado dos campos de cada adapter, até os schemas virem do
   backend.
5. **Desligar é pausa.** Desligar não mexe no cursor, e religar retoma da marca. Rebobinar continua sendo ato de
   operação, por SQL.
6. **A gravação guarda a seção `poll`.** Ela recusa o valor que está escrevendo e que o coletor não leria, e deixa
   passar o valor inválido já gravado que volta igual, para não trancar a tela por um campo que ela não edita.

## Alternativas consideradas

- **Manter o `realtime` como espelho, sincronizado na gravação pela tela.** São dois lugares, e a primeira edição por
  SQL os separa.
- **Deixar a coluna sem uso.** Coluna morta convida a reuso.
- **Um campo `automaticIntegration` no `PUT`, aplicado pelo servidor no JSON.** Seriam dois jeitos de gravar o mesmo
  valor no mesmo pedido, com regra de precedência.
- **O backend devolver a lista de adapters que varrem.** É o destino certo, mas junto com a mudança dos schemas dos
  adapters para o backend. Fazer só esta parte deixaria metade do schema de cada lado.

## Consequências

- **A tela diz o que o coletor faz.** O selo "Integração automática ligada" aparece só quando o poller consultaria o
  tenant. Não é saúde: ligado com falha continua ligado, e a falha está no log e no cursor.
- **O contrato do front muda.** O `/info` troca `realtime` por `automaticIntegration`, e o `/connector` perde o
  `realtime`. Não há cliente em produção. Com cliente, e com o frontend único (ADR-0020), uma troca assim pediria
  compatibilidade por uma versão.
- **Duas declarações de "varrer"** (o feed no backend, a marca na tela), até os schemas irem para o backend.
- **O "Tempo real" do modo de integração continua.** O `RealTime` do modo de integração diz como o documento entrou, e
  não se o conector roda sozinho. É a mesma palavra para outro conceito. Renomear mexe em contrato e fica para outra
  fatia (STATUS).
- **Próximo passo natural:** ligar o interruptor abre as opções do coletor (intervalo, sobreposição, `startFrom`). É
  por ali que um campo da seção `poll` quebrado por SQL passa a ser corrigível na tela.
