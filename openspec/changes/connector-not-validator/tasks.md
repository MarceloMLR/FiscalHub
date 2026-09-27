## 1. A linha: validador só estrutural e grupo da Reforma sem julgamento (D1, D2)

- [x] 1.1 Testes do `GoodsInvoiceValidator`, escritos antes:
  - **Viram "passa":** os testes das regras de conteúdo (CFOP vazio ou malformado, grupo ausente,
    `cClassTrib` vazio);
  - **Casos novos que também passam:** item sem grupo, chave vazia, chave de 43 dígitos, NCM vazio e CFOP de
    3 dígitos;
  - **Continua rejeitada:** a nota sem item, com "A nota não possui itens."
- [x] 1.2 O `GoodsInvoiceValidator` fica só com a regra dos itens. Atualizar o comentário do
  `IDocumentValidator<T>`: a validação é só estrutural, e o conteúdo fiscal é da plataforma (ADR-0026).
- [x] 1.3 Testes do `NfeXmlParser`: o item sem `IBSCBS` é lido com `ReformTaxes == null`, e `IBSCBS` sem
  `gIBSCBS` continua falhando com o elemento citado.
- [x] 1.4 `NfeXmlParser.ParseReformTaxes`: devolver `null` quando o item não tem `IBSCBS`.
- [x] 1.5 `GoodsInvoiceToAvalara`: tirar o guard que lança exceção. Item sem grupo vai sem entradas da
  Reforma, e o teste `Item_without_reform_group_is_a_defect…` passa a afirmar isso.
- [x] 1.6 Testes da `DocumentPipeline`: nota com item sem grupo chama o despachante; nota sem item registra
  a rejeição e não chama o despachante.
- [x] 1.7 `DiscoveryToPipelineTests`: a nota D365 gravada deixa de ser "rejeitada pela Reforma" e chega ao
  despachante falso.
- [x] 1.8 `dotnet build` com 0 warnings e `dotnet test` verde.

## 2. Rejeição vinda do envio, com o motivo da plataforma (D10)

- [x] 2.1 Teste da `DocumentPipeline`, escrito antes: o despachante lança `DispatchRejectedException`. O
  esperado é a rejeição registrada com o `Reason` da exceção, sem exceção propagada e sem
  `RecordSubmissionAsync`.
- [x] 2.2 `DispatchRejectedException` em `Application/Outbound`, e o `try/catch` em volta do `SubmitAsync`
  na `DocumentPipeline`.
- [x] 2.3 Testes da extração do motivo (`PlatformMessage`, interno ao adapter da Avalara):
  - corpo vazio (cita o status HTTP);
  - `{"mensagens":[…]}`;
  - `ProblemDetails` com `errors` por campo;
  - texto puro;
  - JSON sem mensagem reconhecida (vai o JSON compactado);
  - `id` e `status` ignorados;
  - corte em 1.000 caracteres.
- [x] 2.4 Implementar o `PlatformMessage` conforme o D10.
- [x] 2.5 Testes do `AvalaraComplianceDispatcher.SubmitAsync`:
  - **400 e 422:** `DispatchRejectedException` com "Plataforma de compliance recusou: …", uma única
    requisição;
  - **503 e 401:** continuam lançando a exceção de hoje, que vai para o retry nativo.
- [x] 2.6 `SubmitAsync`: tratar 400 e 422 antes do `EnsureSuccessStatusCode`.
- [x] 2.7 Testes do `CheckStatusAsync`:
  - erro com mensagem vira `IntegrationResult.Message`, com o texto da plataforma e sem o status nativo;
  - erro sem mensagem vira "rejeitou sem informar a causa".

  Substituem o teste da frase agnóstica fixa.
- [x] 2.8 `CheckStatusAsync`: ler a resposta como `JsonElement` e extrair o motivo no erro.
- [x] 2.9 Mock:
  - `/admin/result/{carregado|erro|rejeitar}`, com `?motivo=`;
  - o status "erro" devolve `mensagens`, e `rejeitar` devolve 400 no POST;
  - `public partial class Program;` no fim do `Program.cs`.
- [x] 2.10 `dotnet build` com 0 warnings e `dotnet test` verde.

## 3. Omissão visível no registro e no dashboard (D7, D11)

- [x] 3.1 `IntegrationReceipt.Omissions` (`IReadOnlyList<string>`, vazia por padrão). Conferir que os
  despachantes falsos dos testes compilam sem mudança.
- [x] 3.2 Testes do `SqlProcessingStore` em `FiscalHub.Infrastructure.Tests`, escritos antes:
  - submissão sem omissão deixa o `Reason` nulo;
  - submissão com omissões grava "Enviado sem: a; b";
  - `MarkPolledAsync` confirmado preserva o `Reason`;
  - `MarkPolledAsync` com erro grava "<motivo> | Enviado sem: …";
  - erro sem omissão anterior grava só o motivo.
- [x] 3.3 `SqlProcessingStore`: `RecordSubmissionAsync` e `MarkPolledAsync` conforme o D11.
- [x] 3.4 Dashboard:
  - o `Banner` do `DocumentDetail` ganha o tom `warn`;
  - o motivo usa `error` quando `isFailure(status)`, e `warn` nos outros casos;
  - o `FAILURE_STATUSES` não muda.
- [x] 3.5 `dotnet build` com 0 warnings, `dotnet test` verde, e `npm run build` do dashboard sem erro. O
  dashboard não tem script de lint; o `build` roda `tsc --noEmit`.

## 4. Domínio aditivo e leitura no D365 (D12, delta de `d365-document-assembly`)

- [x] 4.1 Domínio (`Goods`), tudo opcional e com comentário:
  - `Issuance` (`Own`, `ThirdParty`) e, em `GoodsInvoice`, `Issuance`, `EntryExitDate` e `GoodsAmount`;
  - o record `Address` (`Street`, `Number`, `District`, `PostalCode`) e `Party.Address`;
  - em `GoodsInvoiceItem`, `Unit`, `AccountingAmount` e `Origin` (origem da mercadoria, 0 a 8).
- [x] 4.2 `tools/d365-fixtures/Record-D365Fixtures.ps1`:
  - **`$select`:** o cabeçalho ganha `AccountingDate` e `TotalGoodsAmount`; a linha ganha `Unit`,
    `AccountingAmount` e `Origin`; o endereço ganha `Street`, `StreetNumber`, `DistrictName` e `ZipCode`;
  - **Gravação** (opt-in, `az login` no fiscosysdev): regravar `notes/*`, `scope/*`,
    `reference/postaladdress-*` e `snapshot/headers|lines`;
  - **Derivadas:** não existem como arquivo. O `D365Fixtures` monta as derivadas em memória, a partir das
    gravadas, com a edição à vista em cada teste, então regravar já as atualiza. Gravado em 2026-09-26: 23
    arquivos, só os campos novos, sem desvio de dado.
- [x] 4.3 Pelos valores de `Origin` gravados, fixar a tradução para a tabela de origem do leiaute (0 a 8),
  conforme o D12, e registrá-la no d365/04 §3.3.
- [x] 4.4 Testes do `D365GoodsInvoiceAssembler` e do source, escritos antes, sobre as fixtures novas:
  - `Issuance` pelo `FiscalDocumentIssuer`, nas duas direções;
  - `AccountingDate` vira `EntryExitDate`, e `1900-01-01` fica ausente;
  - `TotalGoodsAmount` vira `GoodsAmount`;
  - `Unit`, `AccountingAmount` e `Origin` da linha, com unidade vazia e origem sem tradução ausentes;
  - endereço das duas partes, com FK vazia ou registro não encontrado deixando a parte sem endereço;
  - continuam 4 GETs por nota sem complemento.
- [x] 4.5 Implementar:
  - os `$select` do `D365GoodsInvoiceSource`;
  - o endereço junto com o município no resultado do cache;
  - o mapeamento no assembler, com a tradução da origem;
  - `D365Canonicalizer.Version = 2`.
- [x] 4.6 Teste do canônico: a versão 2 aparece na foto da fonte, e duas montagens da mesma nota continuam
  com a mesma impressão.
- [x] 4.7 Rodar o teste opt-in de montagem contra o fiscosysdev (`FullyQualifiedName~Integration`): as 5
  NF-e 55 montam, com a mesma impressão nas duas montagens.
- [x] 4.8 `dotnet build` com 0 warnings e `dotnet test` verde.

## 5. Contrato real e tradução de códigos (D3 a D9, `avalara-document-contract`)

- [x] 5.1 Testes do `AvalaraOutboundSettings`, escritos antes:
  - **Rejeições com mensagem:** perfil ausente, ambiente ausente, `establishments` ausente, entrada ausente,
    cada um dos dois campos ausente e JSON inválido. Cada uma lança `DispatchRejectedException` com
    mensagem "Configuração do conector: …", que cita o tenant, o ambiente, o CNPJ e o campo;
  - **Chave formatada:** a chave `44.278.225/0001-80` casa com `44278225000180`;
  - **`baseUrl`:** continua com fallback.
- [x] 5.2 Implementar o `AvalaraOutboundSettings` (D4) e trocar o `ExtractBaseUrl` do dispatcher por ele.
- [x] 5.3 Testes da resolução do estabelecimento próprio e do parceiro:
  - `Issuance` `Own` e `ThirdParty`;
  - sem `Issuance`: casamento pelo emitente e pelo destinatário (os dois XMLs do `LocalSeed`), ambíguo e
    nenhum;
  - documento do parceiro: 14 dígitos em `cnpj`, 11 em `cpf`, e vazio sem nenhum dos dois.
- [x] 5.4 Implementar a resolução (D5).
- [x] 5.5 Reescrever o `AvalaraContract` a partir do JSON mínimo de mercadoria e dos blocos do schema (D3,
  D6):
  - no item, os blocos `imposto.icms`, `ipi`, `pis`, `cofins`, `ii`, `icmsst` e `issqn`, com os campos do
    D6;
  - o array `impostos` só com as entradas do grupo IBS/CBS;
  - campos opcionais anuláveis, `WhenWritingNull` no `JsonSerializerOptions` do adapter e `parceiro.cpf`.

  Teste: o payload serializado não tem nenhum `null` nem nenhum campo da lista sem fonte.
- [x] 5.6 Testes do mapper, escritos antes:
  - **Cabeçalho:** série, `modelo` como número, `numeroDocumento` como veio, chave vazia omitida,
    `dataEmissao`, `dataEntradaSaida`, `periodoEscrituracao` pelo mês, `situacao = 0`,
    `codigoReferenciaIntegracao` = chave natural, `valorDocumento` e `valorMercadorias`.
  - **Itens:** unidade nos dois lugares, `cfop` numérico, `valorContabil`, CFOP `12345` enviado.
  - **Bloco `imposto`,** a partir da `BRMF21-10000026`:
    - `icms`, `ipi`, `pis` e `cofins` com CST como número, base, alíquota e valor;
    - `TabA` pela origem e sem `TabA` quando o item não tem origem;
    - array `impostos` vazio e sem `classificacaoTributariaImposto`.
  - **Bases:** o ICMS CST 90 da `BRMF06-110000027` com base 0 e `valorBaseOutrosICMS` 1.000; base isenta ou
    em "outras" igual a zero não é escrita.
  - **II:** `imposto.ii` da `BRMF06-110000031` com `baseCalculoII` 4.500, alíquota 30 e valor 1.350, e sem a
    base em "outras".
  - **ICMS-ST e ISS:** `icmsst`; `issqn` com o ISS retido nos campos `*ISSRetido`.
  - **Reforma:** três entradas no array, sem total do IBS; item sem grupo sem entradas.
  - **Problemas de contrato,** todos listados de uma vez: CFOP vazio, modelo não numérico, CST não numérico e
    dois tributos do mesmo bloco no item.
  - **Omissões:** `IcmsDiff`, encargo com valor, retenção de IRRF e IS; nota sem omissão tem lista vazia.
- [x] 5.7 Implementar o mapper (D6 a D9): o mapeamento campo a campo, os blocos por tipo com as regras do D6
  e o retorno com payload, problemas de contrato e omissões.
- [x] 5.8 Testes do dispatcher com o contrato novo:
  - `codigoEmpresa` e `codigoContribuinte` iguais a `20247332000182` para o estabelecimento
    `44278225000180`, e nunca o CNPJ;
  - tenant sem códigos: zero requisições;
  - problema de contrato: zero requisições, motivo "Contrato do destino: …";
  - rejeição síncrona com omissões: o motivo traz as duas partes;
  - recibo com `Omissions`;
  - foto do destino salva antes do POST.
- [x] 5.9 Ligar a ordem do D10 no `SubmitAsync` e ajustar os testes existentes do dispatcher ao perfil com
  `establishments`.
- [x] 5.10 `dotnet build` com 0 warnings e `dotnet test` verde.

## 6. Ponta a ponta contra o mock (D13, D15)

- [x] 6.1 `FiscalHub.Integration.Tests`:
  - o pacote `Microsoft.AspNetCore.Mvc.Testing` e as referências ao `tools/MockComplianceApi` e ao
    `Outbound.Avalara`;
  - um harness com o mock em memória (`WebApplicationFactory<Program>`), o dispatcher real ligado pelo
    `AddAvalaraComplianceDispatcher` com o handler do `TestServer`, o `StatusPoller` e um perfil com
    `establishments`.
- [x] 6.2 Teste: a nota D365 gravada `BRMF21-10000026` é enviada. O JSON guardado no mock tem:
  - os códigos da configuração;
  - o parceiro Southridge;
  - os blocos `imposto.icms`, `ipi`, `pis` e `cofins` em cada item, e nenhum array `impostos`.

  O poll a confirma.
- [x] 6.3 Teste: a nota de importação `BRMF06-110000031` é enviada com o II em `imposto.ii` e com "Enviado
  sem" do encargo, e a confirmação preserva a observação.

  O store do ponta a ponta é falso. O teste prova a cadeia: as omissões chegam no recibo, e a confirmação volta
  com motivo nulo. A preservação em si é do `SqlProcessingStore`, provada na 3.2.
- [x] 6.4 Teste: com o mock em `erro`, o poll registra `IntegrationError` com o motivo do mock. Com o mock em
  `rejeitar`, o envio registra `IntegrationError` com o motivo, depois de um único POST.
- [x] 6.5 `dotnet build` com 0 warnings e `dotnet test` verde.

## 7. Configuração de dev, ADR e documentação (D14, D16 a D18)

- [x] 7.1 Seed dos perfis:
  - `OutboundSettings.sandbox.establishments` do tenant-a, com `44278225000180` e `12345678000190`, os dois
    apontando para `20247332000182`;
  - `production` sem estabelecimentos;
  - o tenant-b no mesmo formato, sem estabelecimentos.
- [x] 7.2 `docs/adr/0026-conector-nao-validador.md`, pelo template `0000`, com o conteúdo do D14; entrada no
  `docs/adr/README.md`. O ADR inclui:
  - a instrução do array genérico revertida pela evidência dos JSONs reais;
  - a justificativa de cada regra do D1 pelo princípio;
  - a regra da versão do canônico em produção (D17).
- [x] 7.3 Notas de revisão:
  - **ADR-0025:** no §6 e no primeiro item de "Piora", apontando o ADR-0026. No §7, apontando a regra de
    versão do D17;
  - **ADR-0003:** o refinamento da mensagem da plataforma.
- [x] 7.4 `docs/RUNNING.md`:
  - passo de configuração de `establishments` em banco existente, por SQL em arquivo;
  - §7 com o desfecho novo: 5 enviadas e confirmadas pelo mock, 9 ignoradas, e as omissões (`IcmsDiff` na
    `BRMF06-110000027`, encargo na `BRMF06-110000031`);
  - payload com os blocos `imposto`, conferível no mock;
  - roteiro de rejeição com `erro` e `rejeitar`;
  - nota de que o mock aceita tudo e de que o envio ao sandbox real é a próxima fatia;
  - §4 e §5: o XML também depende de `establishments`.
- [x] 7.5 `d365/04`:
  - campos lidos agora (§2.4 e §3.2), com a tabela de tradução da origem (§3.3);
  - pendências no §11: valores de `Purpose`, `PaymentMethod`, `ItemType`/`InventProductType` e
    `CreditSourceCode`; `FiscalDocumentAccountNum` como fonte de `parceiro.codigo`; idioma do
    `FSUnitOfMeasureBR`; `AccountingDate` na saída.
- [x] 7.6 `docs/STATUS.md`:
  - **Próxima fatia, explícita:** ligação com o sandbox real da Avalara, com o conteúdo do D18 (credenciais
    por tenant, token por tenant ligado no host, `BaseUrl` do sandbox, teste manual contra o sandbox,
    respostas reais gravadas e as Open Questions que o envio real responde);
  - **Depois dela:** tabelas de código com a Avalara, e fatia de serviço;
  - a entidade de `CClassTribTable_BR` deixa de ser pré-requisito da demonstração;
  - **checklist do primeiro cliente** (já consolidado):
    - acrescentar o que esta fatia abrir;
    - acrescentar a tradução do `Origin` gravada;
    - não marcar como provado o que só passou por teste com fixture derivada.

  O hash de transição já está lá, como pré-requisito da primeira mudança de versão com tenant em produção.
- [x] 7.7 Teste manual pelo RUNNING.md:
  - **Passada normal** (host, emulador, mock, poll do tenant-a com `startFrom` 2015): 5 NF-e 55 enviadas e
    confirmadas, 9 ignoradas, as omissões como aviso no dashboard e o payload conferido no mock;
  - **Rejeição:** repetir com o mock em `erro` e conferir o motivo no dashboard;
  - **Registro:** o resultado vai em "Validado no ambiente" no ADR-0026.
- [x] 7.8 `dotnet build` final com 0 warnings e `dotnet test` verde.
