## Why

Todo despacho do tenant-a contra o sandbox falha desde o `platform-establishment-resolution`. As cinco NF-e 55 da `Matriz`
ficaram em `IntegrationError` com o mesmo motivo:

    Contrato do destino: a listagem de empresas (taxcompliance/v2/empresa) não veio no formato verificado, um array puro
    (veio um objeto).

Não são cinco problemas. A listagem é tudo ou nada, e esse desencontro sozinho derruba a saída inteira do tenant.

**A causa, confirmada contra o sandbox em 2026-10-06.** O `AvalaraEstablishmentListing` trata o 401, o 403, o 404, os
demais 4xx e o `EnsureSuccessStatusCode` antes de parsear. Por isso a mensagem só é alcançável com HTTP 2xx: não é corpo
de erro mal interpretado. O que muda é a URL:

- **a forma registrada** ("array puro, sem envelope", verificada em 2026-10-02) foi colhida do `/empresa` **sem opções de
  query**;
- **o hub chama com opções:** `$select`, `$orderby`, `$top` e `$skip`. Com elas, o mesmo endpoint devolve o envelope;
- **o `/contribuinte` corrobora:** é o que sempre foi chamado com query, e o que já estava registrado como envelope.

É o comportamento normal de uma API OData: o envelope é onde cabem o `@odata.count` e o `@odata.nextLink`. A plataforma
não mudou, e a verificação não estava errada. O endpoint tem duas respostas, e amostramos uma e chamamos a outra.

**O que as chamadas diretas confirmaram no `/empresa`, em 2026-10-06:**

1. `?$top=5&$orderby=empresaId`: o envelope, com 5 itens em ordem de `empresaId`, e o `idPortalCompany` a mais (a chamada
   não tinha `$select`);
2. `?$top=2&$orderby=empresaId&$skip=2`: o envelope, com `7408` e `7409`. O `$skip` é respeitado, e a ordem é estável entre
   requisições;
3. `?$top=5&$orderby=empresaId&$skip=999`: `{"value": []}`.

**A página vazia também vem em envelope** (item 3). Então a parada da leitura depende do mesmo conserto: mesmo com a
primeira página certa, a página vazia seria recusada.

**O `$select` já está provado aceito.** O defeito apareceu como recusa de forma, e não como HTTP 400. Como o adapter trata
todo 4xx antes de parsear, a URL completa do hub, com o `$select`, respondeu 2xx.

## What Changes

- **As duas formas são aceitas, nas duas listas.** O campo `Envelope` do `ListPath` sai. Cada resposta 2xx é lida assim:
  - a raiz é um array: é a lista;
  - a raiz é um objeto com `value` sendo array: o `value` é a lista. As outras propriedades do objeto são ignoradas;
  - qualquer outra coisa: recusa de contrato do destino, como hoje.

  A página vazia vale nas duas formas, e encerra a leitura. As guardas que protegem a completude não mudam: item que não é
  objeto, empresa sem `empresaId`, `$skip` ignorado e teto de páginas.
- **A recusa da forma diz o que veio.** Hoje ela diz "veio um objeto", que é a mesma frase para um corpo de erro e para um
  envelope. Ela passa a nomear as propriedades de primeiro nível do objeto recebido: só os **nomes**, nunca os valores,
  porque é resposta da plataforma e pode ter dado de cliente. "veio um objeto com as propriedades error, message" e "veio
  um objeto com a propriedade value, que não é um array" levam a conclusões opostas.
- **O mock imita as duas respostas do `/empresa`:** o envelope com opções de query, inclusive na página vazia, e o array
  puro sem elas. Hoje ele serve o array puro com qualquer query, e é por isso que o ensaio passou e o envio real falhou. Os
  `DispatchToMockTests` da listagem passam a falhar com o código de hoje, e reproduzem o defeito ponta a ponta.
- **As fixtures e o registro do contrato:**
  - o `Fixtures/listing/` ganha o `empresas-envelope.json`, irmão do `empresas.json`, e o `empresas-vazio.json`, com
    `{"value": []}`, para o teste de que a leitura para nele;
  - o `Fixtures/listing/README.md` corrige a afirmação "devolve um array puro, sem envelope", que é condicional, e diz de
    qual chamada cada forma veio;
  - o `Fixtures/sandbox/` ganha as respostas reais das três chamadas diretas, redigidas e com a query que as produziu. O
    `/empresa` nunca teve resposta real gravada, só a forma reconstruída à mão. A varredura do `SandboxFixtureTests` passa a
    impor a redação: nenhum CNPJ, e todo valor fora da lista do que fica mascarado.
- **A sonda do sandbox ganha o comando `listing`** (`tools/AvalaraSandboxProbe`, ADR-0027 §9). Ele roda a listagem real do
  adapter e o casamento de um CNPJ, e é o meio de provar o de/para de um estabelecimento conhecido contra o sandbox, sem
  mandar documento.
- **O registro do fato errado é corrigido:** no ADR-0033, a linha "Verificado (2026-10-02)" ganha a nota da correção, e o
  comentário do topo do `AvalaraEstablishmentListing` e do mock deixam de afirmar a forma única. Sem ADR novo (design D6).
- **As fixtures que se dizem de mentira passam a ser de mentira.** O `Fixtures/listing/README.md` afirma que os valores são
  inventados, mas o `Fixtures/listing/` e o mock carregam a empresa real do sandbox: o `empresaId` 7410, o `codigoCIA`
  `"005"`, a razão social e o começo do `idPortalCompany`. Esta change escreve a regra de mascaramento, e publicá-la
  violada uma pasta ao lado tira a credibilidade dela. Ali, tudo passa a ser inventado. São só valores, os testes que os
  afirmam e o README, sem mudança de comportamento, num commit separado do conserto da forma (design D10).
- **O STATUS ganha um item de Operação** (design D11):
  - **o ambiente:** o D365 de dev e a conta de sandbox não têm nenhum CNPJ em comum, e o fim da esteira só fecha contra o
    mock;
  - **o contorno temporário:** a sobreposição que aponta os CNPJs da Contoso para um contribuinte da TMSA, com o gatilho de
    remoção, o cuidado enquanto existir e o sintoma se ficar esquecida;
  - **a alternativa recusada:** despachar sem o código do contribuinte.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `avalara-establishment-listing`: introduzida pela change `platform-establishment-resolution`, que já está no `main` e
  ainda não foi arquivada. Esta change é arquivada depois dela (design, Migration Plan).
  - **as empresas e os contribuintes da conta:** o requisito deixa de fixar uma forma por endpoint;
  - **as duas formas de uma lista:** requisito novo, com o que é lista, a página vazia nas duas formas, e o que é recusa;
  - **a recusa da forma diz o que veio:** requisito novo, com os nomes das propriedades e nunca os valores;
  - **as falhas da listagem:** a linha "2xx fora do formato" e o cenário "Empresas em envelope" mudam.

## Non-goals

- **Provar de novo o contrato do `/empresa`.** O envelope, o `$skip`, a ordem, a página vazia e o `$select` aceito já
  foram confirmados (Why). A verificação contra o sandbox desta change é sobre o caminho do hub.
- **Fechar as provas do `platform-establishment-resolution`.** Esta change cobre o caminho da listagem, e quem fecha a
  6.2 é aquela change. O design (D9) diz o que desta correção a atende.
- **O `$select` ignorado pela plataforma.** O cenário `campos-a-mais` já o cobre, e a chamada de hoje (com o
  `idPortalCompany`, fora do que o hub pede) confirma que campo a mais não quebra.
- **Seguir o `@odata.nextLink`.** A paginação continua sendo do cliente, pelo `$skip` com os itens recebidos e a parada
  na página vazia. O `nextLink`, se vier no envelope, é ignorado como as outras propriedades.
- **Isolar qual opção de query liga o envelope.** Aceitar as duas formas torna a pergunta irrelevante para o hub.
- **Afrouxar outros contratos da Avalara.** O envio, a consulta de status e o token continuam como estão.
- **Um esquema novo de recusa ou de retry.** A forma desconhecida continua sendo `DispatchRejectedException` com o prefixo
  "Contrato do destino: " (ADR-0026 §2), sem retentativa.
- **Despachar sem o código do contribuinte quando o CNPJ não resolve.** Foi considerado e recusado, e a recusa de hoje fica
  como está. Os motivos ficam no item de Operação do STATUS, para não ser reaberto.
- **Trocar os exemplos de valores reais nos artefatos do `platform-establishment-resolution`.** O spec e o design daquela
  change trazem a razão social e o `"005"` em exemplos. A decisão é daquela change, antes do arquivamento.

## Impact

- **Código:**
  - `src/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara/AvalaraEstablishmentListing.cs`: o `Parse`, a mensagem de
    recusa e o `ListPath` sem o `Envelope`;
  - `tools/MockComplianceApi/Program.cs`: a forma do `/empresa` pela presença de opções de query;
  - `tools/AvalaraSandboxProbe/Program.cs`: o comando `listing`.
- **Testes:**
  - `AvalaraEstablishmentListingTests`: as duas formas por lista, a parada na página vazia em envelope, a terceira forma
    recusada e a mensagem sem valores. Os testes de hoje que exigiam a recusa do envelope nas empresas, e do array nos
    contribuintes, mudam, e cada um diz por quê;
  - `SandboxFixtureTests`: a reprodução das respostas reais do `/empresa`, e a varredura com a regra da redação;
  - `DispatchToMockTests`: continuam iguais, e passam a atravessar o envelope do mock.
- **Fixtures:** `Fixtures/listing/` (os valores inventados, os nomes de arquivo, o envelope, a página vazia e o README) e
  `Fixtures/sandbox/` (as três respostas reais e o README).
- **Os valores inventados:** o `PlatformHandler`, o mock e os testes que afirmam os valores das fixtures e do mock, ou que
  montam a mesma empresa real inline: o `AvalaraEstablishmentListingTests`, o `AvalaraDispatcherPlatformCodesTests`, o
  `AvalaraOutboundSettingsTests` e o `DispatchToMockTests`.
- **Docs:**
  - o ADR-0033: a nota na linha "Verificado";
  - o RUNNING: o comando novo da sonda no §9, e os códigos e a descrição do mock onde aparecem;
  - o STATUS: o item de Operação.
- **Sem mudança de dependência entre camadas:** tudo fica no adapter da Avalara, no mock e na sonda. Sem migração, sem
  configuração nova e sem mudança de API do host.
