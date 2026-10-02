A ordem vai do dado para a tela, uma fatia vertical por grupo:

- **Grupo 1, as premissas contra o fiscosysdev.** Vem antes do código, porque o filtro de data da descoberta é premissa.
- **Grupo 2, a normalização do CNPJ.** O resto da fatia depende dela.
- **Grupos 3 e 4, o diretório.** Primeiro a porta, a escolha e o fallback, depois o D365.
- **Grupos 5 e 6, a descoberta por período.** Primeiro o D365, depois a escolha, a fila e o reprocesso.
- **Grupo 7, os cards e o modal no servidor.**
- **Grupo 8, o dashboard.**
- **Grupo 9, o lado D365 e a documentação.**
- **Grupo 10, a prova manual do critério de saída.**

Cada grupo de código termina com `dotnet build` com 0 warnings e `dotnet test` verde. O grupo do dashboard termina com o
`npm test` e o `npm run build`.

## 1. As premissas contra o fiscosysdev (D4, D1)

- [x] 1.1 O filtro de data da `FSFiscalDocumentBRs`, pelo Postman, com a credencial de sempre:
  - **a consulta:** `cross-company=true`, o `$select` do feed, `$orderby=FiscalDocumentRecId` e
    `$filter=FiscalDocumentDate ge 2026-08-07T00:00:00Z and FiscalDocumentDate le 2026-08-07T23:59:59Z and dataAreaId eq 'brmf' and FiscalEstablishment eq 'SP-01'`;
  - **o que anotar:** o status HTTP, o número de linhas e o literal de `FiscalDocumentDate` que voltou;
  - **se o F&O recusar:** repetir com o literal de data (`FiscalDocumentDate ge 2026-08-07`);
  - **o registro:** anotar no design (D4) o literal que funcionou. É ele que o teste 5.3 fixa.

  **Feito (2026-10-01), com a sessão do Azure CLI.** HTTP 200, com as duas NFS-e da `SP-01` de 2026-08-07
  (`BRMF06-110000034` e `BRMF06-110000035`), as duas com `FiscalDocumentDate = 2026-08-07T12:00:00Z`. O literal de data
  também funciona, e fica o de `DateTimeOffset`. A data fiscal mais recente da `brmf` é 2026-08-07. Registrado no D4.
- [x] 1.2 A coleção do Postman (`d365/postman`), na convenção das pastas que já existem:
  - a `FiscalEstablishments` com o `$select` do D1;
  - a consulta por período do 1.1.

  **Feito (2026-10-01):** a pasta `FiscalHub — diretório e descoberta por período`, com os dois requests e as variáveis
  `periodoInicio`, `periodoFim` e `estabelecimento`. O README da coleção explica a pasta.
- [x] 1.3 As respostas do 1.1 e da `FiscalEstablishments` como fixtures, se o `tools/d365-fixtures/Record-D365Fixtures.ps1`
  couber. Senão, JSON nos testes, com os valores reais anotados no 1.1. A fixture prova o código, e não o dado
  (`d365/07`).

  **Feito (2026-10-01):** o `Record-D365Fixtures.ps1` ganhou o `-DirectoryOnly` e grava a pasta `directory/`: o cadastro
  da `brmf`, com os quatro estabelecimentos, e a consulta por período da `Matriz`, da `SP-01` e da `SAL-01`. O `RJ-01` não
  tem nota. Registrado no `tools/d365-fixtures/README.md`.

## 2. A normalização do CNPJ e do CPF (D8, `tax-identifier-normalization`)

- [x] 2.1 Teste primeiro, na `FiscalHub.Application.Tests`, que já alcança o Domain (não há projeto de teste do Domain):
  - os cenários da spec: o numérico do F&O, a máscara completa, o alfanumérico, a caixa como veio e o CPF;
  - um caractere fora dos quatro (`_`) é preservado.
- [x] 2.2 Domain: `TaxIdentifiers.Normalize`, em `Goods`, pura.
- [x] 2.3 Teste primeiro, no D365:
  - **no `D365ChangeFeedTests`:** o grupo com `FiscalEstablishmentCNPJCPF = 12.ABC.345/01DE-35` dá a empresa
    `12ABC34501DE35`;
  - **no `D365GoodsInvoiceAssemblerTests`:** o estabelecimento próprio e as partes com o CNPJ alfanumérico. O NCM e o CFOP
    continuam só com dígitos (`5.102` → `5102`);
  - **os cenários numéricos de hoje:** passam sem mudança.
- [x] 2.4 D365:
  - o CNPJ e o CPF, no grupo e na montagem, passam pela normalização;
  - o `D365HeaderValues.Digits` fica só para o NCM e o CFOP, e o comentário dele perde o CNPJ.
- [x] 2.5 Teste primeiro, na Avalara:
  - a chave `12.ABC.345/01DE-35` do `establishments` casa com o estabelecimento `12ABC34501DE35`;
  - `12XYZ34501DE35` não usa os códigos de `12ABC34501DE35`, e a rejeição cita `12XYZ34501DE35`;
  - o `PartiesOf` sem a indicação de emissão, com CNPJ alfanumérico;
  - o parceiro com o CNPJ alfanumérico vai em `cnpj`;
  - o CEP continua só com dígitos.
- [x] 2.6 Avalara: as duas cópias do `Digits` dão lugar à normalização no CNPJ e no CPF. O `Digits` fica só para o CEP.
- [x] 2.7 Application:
  - o `GoodsInvoiceMetadataExtractor.FromIssuer` sobre o CNPJ normalizado;
  - o teste: o XML numérico dá `12345678` e `0001`, como hoje, e o alfanumérico dá os 8 primeiros caracteres.
- [x] 2.8 Ponta a ponta em memória (`FiscalHub.Integration.Tests`, `DiscoveryToPipelineTests`), com a nota de saída
  gravada e o CNPJ do estabelecimento trocado por `12.ABC.345/01DE-35`. O mesmo `12ABC34501DE35` aparece:
  - no grupo da referência;
  - no estabelecimento do domínio;
  - nos metadados;
  - na tradução da Avalara.
- [x] 2.9 `dotnet build` com 0 warnings e `dotnet test` verde.

  **Feito (2026-10-02), com uma ressalva de forma.** Um `FiscalHub.Host` da prova anterior (PID 28732, de pé desde
  2026-10-01 11:52) trava o `bin` do host, e o `dotnet build` da solução falha ao copiar as DLLs. Ele não foi derrubado.
  A verificação foi:
  - o host compilado com `OutDir` fora do repositório, com 0 avisos, e a sonda da Avalara do mesmo jeito;
  - cada projeto de teste com o próprio `dotnet test`. Nenhum referencia o host.

  Todos verdes. O D365 tem 171 testes, com 2 pulados que são os opt-in. O 2.8 ficou no `DispatchToMockTests`, que tem o
  despachante real e o mock: o mesmo cabeçalho passa pelo feed real, pela montagem, pelos metadados, pela tradução e pelo
  payload.

## 3. O diretório: a porta, a escolha e o fallback (D2, D3, `company-directory`)

- [x] 3.1 Teste primeiro (Application):
  - **a escolha:**
    - o tenant com `Dynamics365` recebe a implementação dele, e nunca o fallback;
    - o tenant com `iScala`, sem implementação, recebe o fallback quando ele está registrado, e nenhum quando não está;
    - o tenant sem perfil segue a mesma regra;
    - a comparação é ordinal;
  - **a consulta do host:**
    - o tenant vem do `ITenantContext`;
    - os três desfechos: a lista, "sem diretório" com o adapter no texto, e a falha;
    - a `OriginUnavailableException` e a `ConnectorSettingsException` viram falha, com o motivo delas.
- [x] 3.2 Application:
  - a `ICompanyDirectory` com `Origin` e o tenant nas duas operações;
  - a escolha pelo adapter de entrada, e a chave do fallback de desenvolvimento;
  - a `OriginUnavailableException` (`Inbound`), com o motivo seguro;
  - o caso de uso do host, com os três desfechos.
- [x] 3.3 `Directory.Json`:
  - a assinatura nova e o `Origin` `Local`. O tenant é ignorado, e um comentário diz que é dado de desenvolvimento;
  - o `UseJsonCompanyDirectoryAsDevelopmentFallback(path)`, que registra o diretório como keyed na chave do fallback, e
    não como implementação comum;
  - os testes do adapter e do registro.
- [x] 3.4 Host:
  - o `GET /companies` e o `GET /companies/{code}/branches` pelo caso de uso: 200, 404 com o texto, ou 502 com o motivo;
  - o fallback do JSON só sob `IsDevelopment()`;
  - o `AddJsonCompanyDirectory` incondicional sai.
- [x] 3.5 `dotnet build` com 0 warnings e `dotnet test` verde.

## 4. O diretório do D365 (D1, D2, `company-directory`)

- [x] 4.1 Teste primeiro (`FiscalHub.Adapters.Ingress.D365Poll.Tests`), com o HTTP falso:
  - **a URL:** `cross-company`, o `$select` dos quatro campos, e o `$filter` por `dataAreaId` só quando o perfil tem
    `companies`;
  - **os quatro estabelecimentos da `brmf`:** dão quatro empresas, cada uma com uma filial, com o CNPJ normalizado e os
    nomes;
  - **o mesmo CNPJ em dois estabelecimentos:** dá uma empresa com duas filiais, com o nome do de menor código;
  - **a linha sem CNPJ ou sem código:** fica fora, com o aviso;
  - **o `nextLink`:** é seguido;
  - **as filiais de uma empresa;** a empresa desconhecida dá a lista vazia;
  - **o CNPJ alfanumérico;**
  - **as falhas:**
    - o 403 cita a role e o `FiscalEstablishmentEntityView`;
    - o 500 cita o status, sem o token nem o cabeçalho;
    - o throttling acima do teto;
    - as settings sem `url` não fazem nenhuma requisição;
  - **o token:** vem do provedor do coletor, a mesma instância.
- [x] 4.2 D365:
  - a leitura comum do cadastro (D1), com a tradução das falhas;
  - o `D365CompanyDirectory`;
  - o registro no DI do adapter, ao lado do feed, como scoped.
- [x] 4.3 Host: o registro. O tenant-a, do `Dynamics365`, resolve para o D365.
- [x] 4.4 `dotnet build` com 0 warnings e `dotnet test` verde.

## 5. A descoberta por período do D365 (D4, D6, `period-discovery`)

- [x] 5.1 Teste primeiro: o mesmo cabeçalho, lido pelo feed e pela descoberta, dá referências iguais (a chave natural, o
  locator e o grupo). O `D365ChangeFeedTests` de hoje passa sem mudança.
- [x] 5.2 D365: o `Row`, o `$select`, o `Map` e o `Group` saem do `D365ChangeFeed` para o tipo interno comum (D6).
- [x] 5.3 Teste primeiro, no `D365DocumentDiscovery`:
  - **a URL:**
    - o filtro dos dias com o literal do 1.1;
    - o `or` dos estabelecimentos;
    - o número, quando houver;
    - `$orderby=FiscalDocumentRecId`, o `$top` igual ao `pageSize` do perfil, e o keyset;
  - **os dias:**
    - o início às 00:00 e o fim às 23:59:59, em BRT, dão os mesmos dias, sem conversão;
    - o fim antes do início dá vazio, sem HTTP;
  - **os estabelecimentos:**
    - a empresa sem estabelecimento no cadastro dá vazio, só com o GET do cadastro;
    - sem filial, entram todos os estabelecimentos do CNPJ;
  - **a guarda pelo CNPJ normalizado;**
  - **as páginas:** até a página curta, uma vez cada nota;
  - **a origem:** `Dynamics365` em toda referência;
  - **a busca por chave:**
    - a chave sem `|` dá `null`, sem HTTP;
    - 0 linhas dão `null`, 1 dá a referência, e 2 são falha que nomeia a chave;
  - **as falhas:** as settings inválidas não fazem requisição, e o 403 cita o `FSFiscalDocumentBRView`.
- [x] 5.4 D365: o `D365DocumentDiscovery` e o registro no DI.
- [x] 5.5 `dotnet build` com 0 warnings e `dotnet test` verde.

## 6. A escolha da descoberta, a fila e o reprocesso (D2, D3, D5, D7, `period-discovery`)

- [x] 6.1 Teste primeiro (Application):
  - **a escolha da descoberta:** a mesma regra do 3.1;
  - **no `IntegrationRunnerTests`:**
    - o runner usa a descoberta escolhida;
    - sem descoberta, a exceção cita o adapter e o tenant, nada é enfileirado e nenhuma execução é registrada;
  - **o reprocesso:**
    - a ordem é a descoberta do adapter primeiro, e depois o fallback;
    - vale a primeira que acha a nota;
    - nenhuma achou dá "não encontrada".
- [x] 6.2 Application:
  - o runner com a escolha;
  - o reprocesso vira um caso de uso da Application. Hoje ele está dentro do endpoint, e assim não tem teste.
- [x] 6.3 `Discovery.Local`: o `UseLocalDocumentDiscoveryAsDevelopmentFallback()`, como keyed na chave do fallback, com o
  teste do registro.
- [x] 6.4 Host:
  - o runner e o reprocesso recebem o `IDocumentQueue` keyed `"discovery"`;
  - o `POST /integrations/manual` responde 409 sem descoberta e 502 com a `OriginUnavailableException`;
  - o catálogo local só sob `IsDevelopment()`;
  - o `AddLocalDocumentDiscovery` incondicional sai.
- [x] 6.5 `dotnet build` com 0 warnings e `dotnet test` verde.

## 7. Os cards e o modal no servidor (D9, D10, `document-grouping`)

- [x] 7.1 Teste primeiro (Infrastructure, SQLite):
  - **o `CountByModelAsync`:**
    - a janela inclusive nas duas pontas, e a data fora dela excluída;
    - uma linha por modelo, com as faixas de status;
    - a ignorada conta no total, e não no erro;
    - o escopo do tenant;
    - o registro sem grupo fica fora;
  - **o `ListGroupsAsync`:**
    - o modelo na chave;
    - dois modelos no mesmo dia dão duas linhas;
  - **o `ListByGroupAsync`:**
    - os filtros de tipo, modelo e modo;
    - o `Automatic` casa o modo nulo;
    - sem filtro, a consulta é a de hoje;
  - **a empresa alfanumérica:** é gravada, agrupada e contada como veio.
- [x] 7.2 Application e Infrastructure:
  - o `DocumentGroup.Model`;
  - o `CountByModelAsync`;
  - os filtros do `ListByGroupAsync`;
  - as faixas de status num lugar só, para os cards e a tabela.

  **Feito (2026-10-02).** As faixas de status são três coleções estáticas do `SqlDocumentQueries`, usadas pela tabela e
  pelos cards. Elas ficam dentro do agregado do agrupamento, e por isso entrou um teste além do pedido:
  `Sql_server_translates_the_card_and_table_queries`. Ele gera o SQL do SQL Server sem conexão (`ToQueryString`), porque os
  testes rodam no SQLite e o host no SQL Server. As duas consultas ficaram expostas como `IQueryable` internos para isso.
- [x] 7.3 Host:
  - o `GET /groups/totals?from&to`, com 400 para data ausente, inválida, ou `from` depois de `to`;
  - a rota do modal com `?type=&model=&trigger=`.
- [x] 7.4 `dotnet build` com 0 warnings e `dotnet test` verde.

## 8. O dashboard (D9, D10, D11, D14)

- [x] 8.1 `types.ts` e `client.ts`:
  - o `model` do grupo e as contagens;
  - o `groupDocuments` com tipo, modelo e modo;
  - os textos do 404 e do 502 do diretório, e do 409 e do 502 da execução.
- [x] 8.2 Teste primeiro (`vitest`):
  - **o `periodWindow`:**
    - o dia, 7, 15 e 30 dias;
    - a virada de mês e de ano;
    - 2026-09-05 com 30 dias começa em 2026-08-07;
  - **o `formatCompany` pelo tamanho:** o numérico, o alfanumérico e o de 8 dígitos sem máscara;
  - **as opções de modelo:** guardam o escolhido quando ele some;
  - **a soma dos cards:** em todos os modelos e num modelo só.
- [x] 8.3 `GroupsPage`:
  - os seletores de período e de modelo, com o dia como padrão;
  - os cards pelo `/groups/totals`;
  - a nota de cada card;
  - a coluna "Modelo";
  - o `rowId` com o modelo.
- [x] 8.4 `GroupModal`:
  - manda o tipo, o modelo e o modo da linha;
  - o título conta o mesmo que a lista.
- [x] 8.5 `IntegrationsPage`:
  - os dropdowns com a máscara e o nome;
  - o motivo do 404 e do 502 no lugar do dropdown;
  - os botões desabilitados sem empresa;
  - o banner do 409 e do 502 da execução;
  - a coluna "Empresa" mascarada nas execuções e nos agendamentos.

  **Feito (2026-10-02), com três notas.**
  - **O 409 e o 502 da execução:** aparecem na caixa de erro do próprio modal ("Falha: <motivo>"), que já existia, e não no
    banner da página. O client passou a propagar a mensagem do servidor nessa chamada.
  - **Os controles:** o seletor segmentado e o select saíram da tela para `components/Controls.tsx`, e são os mesmos dos
    filtros dos cards.
  - **Além da tarefa, no mesmo espírito:** o erro do reprocesso, no detalhe do documento, passou a mostrar o motivo do
    servidor, porque o reprocesso da nota do D365 pode responder 502 com a falha da origem (D5).
- [x] 8.6 `npm test` e `npm run build` verdes.

## 9. O lado D365 e a documentação

- [x] 9.1 D365, sem código novo:
  - **o commit:** o `d365/model/AxSecurityRole/FSFiscalHubIntegration.xml` e a nota da receita `06`, que já estão no
    disco;
  - **o `d365/README.md`:**
    - a role referencia também o privilégio padrão `FiscalEstablishmentEntityView`;
    - o hub lê a `FiscalEstablishments` para o diretório e a descoberta por período, e o 403 dela nomeia o privilégio;
    - a contagem do pacote: 22 entidades, 22 privilégios próprios, uma role e um privilégio padrão referenciado;
  - **o `d365/04` §7:** a `FiscalEstablishments` deixa o "pode sair". O hub a lê. Registrar:
    - por que é a entidade da Microsoft, e não uma `FS*`;
    - os quatro campos;
    - o que ficou fora de propósito: IE, CCM e o grupo (`d365/07`, passo 5).
- [x] 9.2 `docs/adr/0032-*.md`, pelo `0000-template.md`, com o que o proposal lista. Registrar no `docs/adr/README.md`.
  Pôr as linhas de revisão:
  - **no 0013:** a porta recebe o tenant, e a primeira fonte real é o ERP, e não a Avalara;
  - **no 0014:** a descoberta do D365, o catálogo local só em Development, e a fila de descoberta;
  - **no 0028:** o diretório por tenant, fechado;
  - **no 0030:** a empresa é o CNPJ normalizado, e não "14 dígitos".
- [x] 9.3 `docs/RUNNING.md`:
  - **a integração manual e a agendada contra o D365:** o que leem, o dia fiscal como critério e a fila de descoberta;
  - **o deploy da role:** antes de usar o diretório, e o texto do 403 quando falta;
  - **o fallback de Development:** o tenant-b vê o mock, e o tenant-a nunca. Sem passo manual;
  - **o reprocesso da nota do D365.**
- [ ] 9.4 `docs/STATUS.md`:
  - **fechar, com a evidência do grupo 10:**
    - "Filtros dos cards";
    - "O diretório de empresas com o CNPJ de 14 dígitos";
    - "Diretório de empresas por tenant";
    - "O modal do grupo não filtra pelo tipo e pelo modo";
    - a ressalva da "Empresa Emitente LTDA", pelo caminho da tela;
  - **itens novos:**
    - a tela da tradução `establishments`, com as linhas do diretório (D12);
    - o estabelecimento removido do cadastro, que não é achado pela descoberta por período;
    - o agendamento de um tenant sem descoberta, que retenta a cada passada;
    - o CNPJ alfanumérico em minúsculas, que não casa com a chave em maiúsculas;
    - o teste de credencial, que não lê a `FiscalEstablishments`.

  **Parcial (2026-10-02).** Os itens novos entraram. Os quatro itens a fechar ganharam a nota "Implementado …; falta a
  prova manual (grupo 10)" e continuam abertos: a evidência é o grupo 10. A sessão da fatia também entrou no fim do STATUS,
  com o que foi verificado contra o fiscosysdev e os achados.
- [x] 9.5 O `CLAUDE.md` §7 e o contexto do `openspec/config.yaml`:
  - a role referencia também o `FiscalEstablishmentEntityView` padrão;
  - o hub lê a `FiscalEstablishments` da Microsoft.

## 10. Prova manual do critério de saída

- [ ] 10.1 Preparar:
  - `scripts/up.ps1`;
  - o deploy da role pelo Marcelo, com build e deploy e sem sync, com a data registrada;
  - o host com `| Tee-Object`;
  - o perfil do tenant-a com o `auth` completo e o segredo no cofre, para o client credentials, e não o Azure CLI;
  - o dashboard de pé.
- [ ] 10.2 A role com a credencial do conector:
  - a linha do `D365DevelopmentTokenProvider` mostra que o tenant-a autenticou pelo app do perfil;
  - o dropdown carrega;
  - se a ordem permitir, antes do deploy: o texto do 403 no lugar do dropdown.
- [ ] 10.3 O dropdown lista os quatro estabelecimentos, com o `RJ-01`:
  - a conferência visual do usuário;
  - o banco com 0 registros de documento para a filial `RJ-01`, que é a prova de que a lista vem do cadastro.
- [ ] 10.4 O filtro de 30 dias. **A prova como foi pedida não vale em 2026-10-01:** 2026-08-07 está a 55 dias, fora de
  qualquer janela (D11).
  - **Se houver uma nota com data fiscal nos últimos 30 dias no fiscosysdev, lançada no ERP:** ela entra com 30 dias, e
    não entra com o dia.
  - **Sem ela:** a tarefa fica aberta, anotada como não exercitada no ambiente, com os testes 7.1 e 8.2 como prova.
  - **Com o filtro no dia:** nem as notas de 2026-08-07 nem as de 2016 entram, por conferência visual.
- [ ] 10.5 Agendar `44278225000260`/`SP-01`, único, com o período cobrindo 2026-08-07, e rodar:
  - **no log:** as referências descobertas, publicadas na fila de descoberta;
  - **no banco:** um registro por chave natural, sem linha nova;
  - **na tabela:** as notas na mesma linha que o coletor produziu, com o modo `Automatic` mantido nas ignoradas.
- [ ] 10.6 O filtro por modelo bate com o modal, com as ignoradas. Depende da nota recente do 10.4, porque as notas de
  2026-08-07 não cabem em nenhuma janela. Sem ela, a tarefa fica aberta, anotada, com os testes 7.1 e 8.2 como prova.
  Em qualquer caso, conferir na tela que o modal de cada linha lista só as notas dela, e com o mesmo número do título.
- [ ] 10.7 O CNPJ alfanumérico. A prova é pelos testes 2.8 e 7.1. Manualmente, só se o fiscosysdev aceitar um
  estabelecimento com CNPJ alfanumérico. Senão, a tarefa fica anotada como não exercitada no ambiente.
- [ ] 10.8 O reprocesso de uma NF-e 55 do D365 com falha:
  - a referência vai para a fila de descoberta, com o locator `d365/brmf/…`, a origem `Dynamics365` e o gatilho manual;
  - é um envio real ao sandbox da Avalara, que recusa por validação como antes.
- [ ] 10.9 O fallback: em Development, o tenant-b vê o mock, e o tenant-a não o vê.
- [ ] 10.10 Registrar a prova no STATUS (9.4), com as linhas do log, e o que foi só conferência visual do usuário, sem
  linha de log.
