## Why

A fatia anterior deixou o hub julgando conteúdo fiscal. O validador rejeita item sem o grupo IBS/CBS, e o
mapper da Avalara lança exceção se um desses chegar. Isso faz do hub uma segunda fonte de verdade fiscal:

- teríamos que acompanhar as regras da Avalara para sempre;
- cada divergência entre o nosso julgamento e o dela vira uma nota que o cliente jura ter mandado e que
  não chegou.

Quem aceita ou rejeita conteúdo fiscal é a plataforma de compliance. O papel do hub é entregar a nota e
mostrar a resposta dela.

Isso é o que falta para a demonstração ponta a ponta. Hoje as 5 NF-e 55 do fiscosysdev param na nossa
validação.

O contrato da Avalara no adapter também não está pronto. Ele foi modelado contra o mock e é só um
subconjunto do real:

- não tem `codigoEmpresa` nem `codigoContribuinte`;
- manda o IBS como total, quando a Avalara espera a parcela estadual e a municipal separadas;
- manda o destinatário como parceiro, e numa nota de entrada o destinatário é o próprio estabelecimento.

## What Changes

- **A linha do que o hub julga.** O hub rejeita o que **impede a requisição de existir**, e nunca o conteúdo
  fiscal.
  - **O validador fica só com uma regra:** "a nota não possui itens".
  - **Saem do validador:** a chave com 44 dígitos, o CFOP com 4 dígitos, o NCM, o grupo da Reforma, o CST e
    o `cClassTrib`. O formato e a obrigatoriedade desses campos são regra fiscal.
  - **A mesma regra no XML:** o parser deixa de falhar quando a nota não tem o grupo IBS/CBS.
  - **O que o contrato não consegue representar** passa a ser conferido no adapter de saída, porque é ele
    que conhece o contrato. Isso inclui o campo inteiro que não é número (CFOP, modelo, CST), o tributo
    repetido onde o contrato leva um só e os códigos do tenant.
- **O mapper para de lançar e emite o que tem.** Um campo ausente não vai no payload. Ele nunca vira zero
  nem texto de preenchimento.
- **O contrato vem do JSON real de mercadoria.** O design traz uma tabela com cada campo, dizendo de onde ele
  vem (entidade e campo do D365) ou marcando-o como não disponível.
- **`codigoEmpresa` e `codigoContribuinte` vêm das `OutboundSettings` do tenant.**
  - **Formato:** uma tabela por ambiente, com o CNPJ do estabelecimento próprio como chave e o par de
    códigos da plataforma como valor. Nada disso vem do ERP. A tradução é um requisito permanente, e não um
    ajuste de demonstração.
  - **Qual lado é o nosso:** quando a origem não diz qual parte da nota é o estabelecimento próprio, a
    tabela diz, e o parceiro passa a ser a contraparte.
  - **Sem tradução:** falha clara, que cita o ambiente, o CNPJ e o campo que falta.
- **Impostos como os JSONs reais mandam.**
  - **Clássicos:** vão no bloco estruturado `imposto` do item. ICMS, PIS e COFINS seguem os JSONs reais; a
    `TabA` do ICMS é a origem da mercadoria, e cada CST vai como número.
  - **Onde os JSONs mínimos são silenciosos:** vale o schema completo da Avalara. O IPI vai em `ipi`, o
    imposto de importação em `ii`, o ICMS-ST em `icmsst` e o ISS em `issqn`, este com o ISS retido.
  - **Reforma:** o array `impostos` fica só com o grupo IBS/CBS, em `CBS`, `IBS ESTADUAL` e
    `IBS MUNICIPAL`.
  - **A instrução anterior foi revertida:** ela mandava os clássicos no array genérico e veio do contrato
    derivado do mock. A evidência real ganha, e o design registra isso.
- **Omissão visível.** Há dados que o domínio tem e que o contrato não leva nesta fatia:
  - tributo sem lugar no contrato (nas notas da base, o `ICMSDiff`);
  - retenção que não é de ISS;
  - encargo;
  - Imposto Seletivo.

  Esses dados não vão, e o registro do documento diz isso ("enviado sem: …"). O dashboard mostra a
  omissão como aviso, e não como falha.
- **Retenção em campo separado, nunca no array `impostos`.**
  - **ISS retido:** vai nos campos próprios do `issqn`.
  - **As demais:** o lugar delas é `impostosRetidos`, cujo `tipoImposto` é um código numérico sem tabela
    confirmada. Por isso, nesta fatia, elas são omissão visível.
- **Rejeição da plataforma com o motivo dela.** Nos dois casos a seguir, o documento é registrado como
  rejeitado e não há retentativa:
  - **Síncrona:** HTTP 400 ou 422 no envio.
  - **Assíncrona:** status "erro" na consulta. O motivo sai da resposta da plataforma, e não de uma frase
    genérica.
  - **Impossibilidade do lado do conector** (configuração, dado que o contrato não representa): segue o
    mesmo caminho, e o motivo deixa claro que o problema é nosso.

  O erro transitório continua no retry nativo (ADR-0004).
- **O domínio cresce de forma aditiva, e todos os campos novos são opcionais:**
  - na nota: emissão própria ou de terceiros, data de entrada/saída e valor das mercadorias;
  - no participante: endereço;
  - no item: unidade, valor contábil e origem da mercadoria.
- **D365 lê os campos novos.**
  - **Cabeçalho:** `AccountingDate` e `TotalGoodsAmount`.
  - **Linha:** `Unit`, `AccountingAmount` e `Origin`.
  - **Endereço das partes,** pelo cache de cadastros.
  - **Impressão:** o canônico sobe para a versão 2, e cada nota D365 relida reintegra uma vez.
  - **Fixtures:** são regravadas.
- **Versão da impressão em base grande.** Mudar o canônico reintegra cada nota já integrada que for
  relida. Um rebobinamento ou backfill logo depois de mudar a versão vira uma enxurrada. O design registra o
  procedimento: hash de transição, que aceita a impressão da versão anterior e regrava a nova sem reenviar,
  e nada de rebobinar sem ele. É regra para a primeira mudança de versão com tenant em produção, e não é
  implementado agora.
- **Base duplicada do `ImportTax`.** O imposto de importação vai em `imposto.ii` com a `TaxBase`, que fecha
  com base × alíquota = valor. A `OtherBase` não tem campo no bloco.
- **`cClassTrib` fora do caminho crítico.** A entidade sobre `CClassTribTable_BR` deixa de ser pré-requisito
  da demonstração.
- **O mock ganha dois comportamentos:** motivo na consulta com status "erro" e rejeição síncrona sob
  comando.
- **ADR-0026** registra a inversão e o argumento dela, a evidência acima da instrução anterior e a regra da
  versão da impressão. O ADR-0025 ganha nota de revisão no §6. O ADR-0003 é refinado: o motivo da
  plataforma chega ao registro como texto, e o status continua normalizado.
- **O desfecho esperado do teste manual muda** (RUNNING.md): as 5 NF-e 55 passam a ser **enviadas** ao mock.
  O que a plataforma responder é resultado válido, desde que o motivo apareça no dashboard.
- **BREAKING (configuração):**
  - **Sem `establishments`:** o perfil de saída de cada tenant passa a precisar do campo por ambiente. Sem
    ele, todo envio do tenant é rejeitado com motivo claro.
  - **Banco novo:** o seed de dev já traz o campo.
  - **Banco de dev existente:** precisa do passo de configuração do RUNNING.md.

## Capabilities

### New Capabilities

- `integration-validation`: o que o hub se permite julgar antes do envio. Cobre o validador só estrutural e
  o grupo da Reforma ausente sem bloqueio em nenhum caminho de entrada, inclusive no XML.
- `avalara-document-contract`: o payload montado a partir do JSON real da Avalara. Cobre:
  - a tradução dos códigos de empresa por ambiente e por estabelecimento;
  - a identificação do estabelecimento próprio e do parceiro;
  - o mapeamento campo a campo;
  - os tributos clássicos no bloco estruturado e o grupo da Reforma no array;
  - o lugar da retenção;
  - o campo ausente omitido, e nunca zerado;
  - a lista do que ficou de fora.
- `compliance-dispatch-outcome`: o desfecho do envio. Cobre:
  - a rejeição da plataforma, síncrona ou assíncrona, registrada com o motivo dela e sem retentativa;
  - a impossibilidade do lado do conector registrada da mesma forma, com motivo que a identifica;
  - a omissão visível no registro e no dashboard, preservada na confirmação.

### Modified Capabilities

- `d365-document-assembly`:
  - "Mapeamento do cabeçalho e das linhas" passa a incluir emissão própria ou de terceiros, data de
    entrada/saída, valor das mercadorias, unidade, valor contábil e origem do item;
  - em "Grupo IBS/CBS do item", o item sem o grupo deixa de ser rejeitado e segue para o envio;
  - "Cadastros de referência em cache" passa a trazer também logradouro, número, bairro e CEP das partes.

## Non-goals

- **Ligação com o sandbox real da Avalara.** É a **próxima fatia**, e explícita: credenciais por tenant pelas
  referências das `OutboundSettings`, token por tenant ligado no host, `BaseUrl` do sandbox e teste manual
  apontando para lá, e não para o mock. Esta fatia fecha com o mock, que aceita tudo.
- **Nota de serviço no domínio.** Continua como "ignorado: tipo fora do escopo", e a fatia própria segue no
  roadmap. O JSON de serviço serviu só de referência de forma.
- **Entidade sobre `CClassTribTable_BR`.**
- **App registration e client credentials do D365.** Todo teste real segue com o Azure CLI.
- **Enriquecimento do código do parceiro.** A fonte provável (`FSFiscalDocumentBR.FiscalDocumentAccountNum`)
  fica registrada para essa fatia.
- **Códigos do contrato sem tradução com evidência.** São `finalidadeNotaFiscal`, `operacao`,
  `tipoPagamento`, `origemSistema`, `tipoItem`, `origemCredito`, `origemInformacao` e `parceiro.ativo`.
  Ficam omitidos, e cada um entra quando houver evidência.
- **Totais de imposto em `totais` e `valorTotalComIBSCBSeIS`.** O cabeçalho do F&O não os traz, e o hub não
  soma valor fiscal.
- **Encargo, retenção que não é de ISS, diferencial de alíquota e Imposto Seletivo no payload.** Nesta
  fatia, são omissão visível.
- **Hash de transição do canônico.** A regra fica registrada, e a implementação vem com a primeira mudança
  de versão que encontrar um tenant em produção.
- **O parser XML preencher os campos novos do domínio.** Muda só a regra do grupo IBS/CBS ausente.
- **Dead-letter imediata com motivo para erro transitório.** O retry nativo continua (ADR-0004).
- **Agrupamento do dashboard pela emissão própria.** O extrator de metadados continua usando o emitente.
- **Despacho de cancelamento.**

## Impact

- **Domain** (`Goods`):
  - `GoodsInvoice` ganha `Issuance`, `EntryExitDate` e `GoodsAmount`;
  - `Party` ganha `Address`;
  - `GoodsInvoiceItem` ganha `Unit`, `AccountingAmount` e `Origin`.

  Tudo opcional. Nenhuma dependência nova.
- **Application:**
  - o `GoodsInvoiceValidator` fica só com a regra dos itens;
  - `Outbound` ganha a `DispatchRejectedException` e a lista `Omissions` no `IntegrationReceipt`;
  - a `DocumentPipeline` registra a rejeição vinda do envio.
- **Infrastructure:**
  - a submissão grava as omissões no `Reason`;
  - a consulta de status preserva o `Reason` na confirmação e o põe depois do motivo da plataforma na
    rejeição;
  - o seed de dev traz `establishments`.

  Sem migration (`Reason` já é `nvarchar(max)`).
- **Adapters:**
  - `Outbound.Avalara`: contrato novo, com o bloco `imposto` por item e o array só para a Reforma; settings
    de saída com a tabela de estabelecimentos; mapper sem exceção e com omissões; rejeição síncrona e motivo
    na consulta;
  - `Ingress.D365Poll`: `$select` do cabeçalho, da linha e do endereço, montagem dos campos novos e canônico
    na versão 2;
  - `Inbound.Xml`: grupo IBS/CBS opcional.
- **tools/MockComplianceApi:** motivo no status "erro" e rejeição síncrona sob comando.
- **Dashboard:** o `Reason` de um documento que não falhou aparece como aviso.
- **Testes:**
  - validador, parser, mapper, dispatcher, esteira, store e montagem D365, esta com fixtures regravadas;
  - integração ponta a ponta contra o mock em memória, com o contrato novo.
- **Docs:**
  - `docs/adr/0026-*.md` e o índice;
  - notas de revisão no ADR-0025 e no ADR-0003;
  - `docs/RUNNING.md` (configuração de estabelecimentos, desfecho novo e a próxima fatia);
  - `d365/04` (campos lidos, tabela de origem e pendências de tradução);
  - `docs/STATUS.md` (a próxima fatia, com o conteúdo dela).
- **Sistemas externos:** o D365 passa a ser lido com mais campos no mesmo número de GETs. O mock recebe o
  contrato novo, e a Avalara real só na próxima fatia.
