# ADR-0030: O grupo da nota vem do estabelecimento próprio no ERP, e a tela lê o motivo pela foto

- **Status:** Aceito
- **Data:** 2026-09-28
- **Revisa:**
  - **ADR-0026:** a omissão visível deixa de fazer parte do motivo de falha. Ela é ressalva de nota aceita e fica
    gravada na foto da resposta do envio.
  - **ADR-0027 §8:** o corpo da quarta foto perde o ruído do `ProblemDetails` quando traz o mapa de erros por campo, e o
    envelope do envio passa a levar as omissões do pedido.
- **Revisado por:** [ADR-0032](0032-diretorio-do-erp-descoberta-por-periodo-e-filtros-dos-cards.md). A empresa do grupo é o CNPJ do estabelecimento sem a pontuação e com as letras, como
  texto, e não o "CNPJ de 14 dígitos": o CNPJ alfanumérico perdia as letras em silêncio.
- **Change OpenSpec:** `openspec/changes/establishment-and-readable-dashboard`. As capacidades são `document-grouping`,
  `integration-schedules`, `compliance-dispatch-outcome`, `platform-response-trace`, `automatic-integration`,
  `d365-change-feed`, `d365-document-assembly` e `discovery-queue-consumer`.

## Contexto

O primeiro envio real ao sandbox da Avalara (2026-09-27, `docs/avalara-sandbox-primeiro-envio.md`) mostrou uma tela que
não é apresentável nem acionável.

- **O grupo saía do emitente.** A empresa eram os 8 primeiros dígitos do CNPJ do emitente, e a filial, os 4 seguintes.
  Numa nota de entrada de terceiro, isso é o fornecedor. O cabeçalho do D365 já traz o estabelecimento próprio: o
  `FiscalEstablishmentCNPJCPF` e o `FiscalEstablishment`.
- **A nota ignorada não tinha grupo.** O registro dela não guardava empresa, filial, data nem modelo. Como os cards
  somam os grupos do dia, e o grupo exige empresa e data, as 9 NFS-e do fiscosysdev não entravam em nenhum card.
- **O dia da nota montada saía em UTC.** O extrator usava a data da emissão, que o F&O guarda em UTC. Num fuso UTC-3,
  toda nota emitida depois das 21h caía no dia seguinte, enquanto a ignorada, pela data fiscal, ficava no dia certo.
- **O motivo da recusa não se lia.** Era uma linha com o mapa `errors` achatado, o `title` "One or more validation
  errors occurred." no fim e a omissão do hub depois de um ` | `, cortada em 1000 caracteres.
- **A omissão não estava em foto nenhuma.** Ela vivia só no motivo do registro. Tirá-la do motivo da nota recusada
  sem gravá-la antes numa foto a apagaria.

## Decisão

**A nota é agrupada pelo estabelecimento próprio e pelo dia fiscal que a origem registra. A nota que não chega à
montagem é agrupada pelo que a descoberta leu, e a tela lê o motivo da recusa pela foto.**

1. **Empresa e filial do estabelecimento próprio.** A `GoodsInvoice` ganha o `Establishment` (CNPJ só com dígitos e
   código), opcional, e o D365 o preenche. Com ele, a empresa é o CNPJ de 14 dígitos, e a filial, o código. Sem ele
   (caminho de XML, andaime de dev), vale a derivação pelo emitente.
2. **O dia é a data fiscal, no fuso de quem emitiu, sem conversão.** Não há fuso do hub, nem UTC, nem Brasília fixo:
   - **no D365:** o `FiscalDocumentDate`, na nota montada (`GoodsInvoice.FiscalDate`) e na ignorada;
   - **no XML:** a data do `dhEmi` no fuso que ele traz.

   Os cards contam pelo dia fiscal, com recorte do dia, e isso é de propósito: as notas de 2016 do fiscosysdev ficarem
   fora é o correto.
3. **A descoberta leva o grupo.** O `$select` do feed ganha o `FiscalDocumentDate`, o `FiscalEstablishmentCNPJCPF` e o
   `FiscalEstablishment`, e a `DocumentReference` ganha o `Metadata`, opcional.
   - **Quem grava:** o store, na nota ignorada, na dead-letter e em qualquer desfecho sem montagem, junto com o modo.
   - **Quem prevalece:** o grupo da montagem, que nunca é trocado pelo da descoberta.
   - **A leitura:** a descoberta e a montagem leem o dia e o CNPJ pela mesma função do adapter.
4. **O canônico sobe para a v3.** Ler o `FiscalEstablishment` na montagem muda a impressão. O `$select` da descoberta
   não entra no canônico. A subida acontece sem tenant em produção. O hash de transição (ADR-0026 D17) continua
   pré-requisito da primeira subida com cliente.
5. **O motivo é um resumo, e a lista mora na foto.**
   - **O `Reason`:** com o mapa `errors`, o motivo é "N campos com erro: a, b, c e mais K", sem o `title`.
   - **A tela:** lê o mapa da foto e o mostra campo a campo, com as mensagens da plataforma. A leitura é pelo formato
     padrão do `ProblemDetails`, sem dicionário de um destino.
6. **A foto perde o ruído.**
   - **O que sai:** com o mapa `errors`, o `type`, o `title` e o `status` que repete o HTTP.
   - **O que fica:** o método, a URL (que prova o ambiente) e o `traceId` (que abre chamado na plataforma).
   - **Sem o mapa:** o corpo fica como veio.
   - **A sonda do sandbox:** continua crua, porque existe para registrar a forma da plataforma.
7. **A omissão é ressalva de nota aceita.**
   - **Na foto:** o envelope do envio leva o `request.omissions`, e a omissão fica gravada em todo envio que recebeu
     resposta.
   - **No motivo de falha:** a recusa do envio, a recusa da consulta e o "sem retorno" não a trazem.
   - **Na nota aceita:** continua no `Reason`, e a tela a mostra como a marca "Enviado com ressalvas".
8. **As fotos cruas são só para Admin, de fato.**
   - **No servidor:** o `/trace` e o zip exigem um dos papéis de uma lista só (`rawTraceRoles`, hoje o Admin). Quem não
     tem o papel recebe 403 antes de qualquer leitura. A regra de tenant do ADR-0028 continua valendo para o Admin.
   - **A primeira vista:** vem de uma leitura do desfecho (`/documents/{tenant}/{chave}/reading`), aberta a qualquer
     papel do tenant. Ela devolve só a lista de campos da recusa e as omissões, tiradas das fotos no servidor.
   - **Na tela:** o "Visualizar JSON" abre um modal próprio, com uma aba por foto. Ele e o "Baixar arquivos" aparecem só
     para os papéis da lista (`RAW_JSON_ROLES`).
   - **O gancho do Suporte:** o papel entra nas duas listas e no `UserRole`. Ele não é criado agora.
   - **O chamado de suporte:** continua anexando os zips no servidor, para qualquer papel. O que ele anexa quando quem
     o abre não pode ver as fotos cruas fica em aberto (STATUS).

   Uma primeira versão fazia o botão só esconder a tela, com o `/trace` e o zip abertos. O pedido passou a ser que o
   usuário comum não tenha acesso, e a restrição virou de autorização.
9. **O modo "Automática".** O modo gravado da nota que entrou sem ação humana passa de `RealTime` a `Automatic`, com a
   migração `RenameRealTimeTrigger`, e a tela o chama "Automática".

## Alternativas consideradas

- **Derivar o estabelecimento da `Issuance` e das partes.** Duplicaria no extrator a regra do adapter de saída sobre
  qual parte é a própria.
- **Brasília fixo (`America/Sao_Paulo`) para o dia.** Erra os estabelecimentos dos outros fusos do Brasil e ainda pode
  divergir do `FiscalDocumentDate` que o ERP gravou.
- **Ler o `FiscalEstablishment` na descoberta, para não subir o canônico.** A nota montada teria uma parte do cabeçalho
  vinda de outra leitura, e a foto da fonte deixaria de ser a nota inteira.
- **Tirar só o `title` e manter a lista inteira no `Reason`.** O corte em 1000 continuaria, com duas cópias da lista.
- **Uma coluna para a ressalva, ou uma foto só das omissões.** Seria contrato e arquivo novos para um valor que o estado
  já separa e que pertence à requisição que o envelope descreve.
- **Só esconder os botões, ou bloquear só o zip.** O `/trace` entrega as mesmas fotos do zip, e o Viewer as leria pela
  API. A proteção seria só aparente.
- **A tela ler a lista direto da foto.** Exige o `/trace` aberto ao Viewer. A leitura do desfecho no servidor entrega só
  o que a primeira vista usa.

## Consequências

- **A tela agrupa pelo estabelecimento que escritura.** Toda nota do D365, ignorada inclusive, cai no dia fiscal dela,
  e a mesma noite não se divide pelo desfecho.
- **Contratos mudam juntos:**
  - o `DocumentGroup.trigger` (`Automatic`);
  - o `/info` (`inboundScans`);
  - a forma do `Reason` da recusa;
  - a forma da foto da resposta;
  - o `/trace` e o zip, que dão 403 para quem não é Admin, e a leitura do desfecho, nova.

  Não há cliente em produção, e front e back sobem juntos.
- **Uma nota já aceita e relida depois do deploy é reenviada uma vez,** pela v3. Hoje nenhuma nota foi aceita.
- **O `BranchCode` passa a 20 caracteres** (migração `WidenBranchCode`), porque o tamanho do código do estabelecimento
  no F&O não é publicado pelo OData.
- **O "hoje" dos cards é o do navegador,** comparado com o dia fiscal da nota. Os filtros por período e por modelo são
  o próximo passo (STATUS).
