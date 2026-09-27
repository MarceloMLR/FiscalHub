## Context

A motivação está no proposal.md. Os requisitos estão nas specs:

- `integration-validation`;
- `avalara-document-contract`;
- `compliance-dispatch-outcome`;
- o delta de `d365-document-assembly`.

Aqui fica o estado atual que molda o desenho.

**Código hoje.**

- **Validador.** O `GoodsInvoiceValidator` rejeita por seis motivos:
  - chave de acesso com número de dígitos diferente de 44;
  - nota sem item;
  - CFOP com número de dígitos diferente de 4;
  - NCM ausente;
  - grupo IBS/CBS ausente;
  - CST ou `cClassTrib` vazios no grupo.
- **Parser XML.** O `NfeXmlParser` exige o elemento `IBSCBS` em todo item. Uma NF-e anterior à Reforma
  falha na leitura, vai para retentativa e acaba na dead-letter.
- **Mapper.** O `GoodsInvoiceToAvalara` tem estes problemas:
  - lança exceção se chegar item sem grupo;
  - faz `int.Parse` do CFOP;
  - manda o IBS como uma entrada de total mais uma de CBS;
  - não tem `codigoEmpresa` nem `codigoContribuinte`;
  - manda o destinatário como parceiro. Na nota de entrada `BRMF06-110000027`, o destinatário é a Contoso,
    o próprio estabelecimento.
- **Dispatcher.** O `AvalaraComplianceDispatcher` tem estes pontos:
  - **Envio:** faz `EnsureSuccessStatusCode`. Um 400 da plataforma vira exceção, é reenviado 5 vezes e cai
    na dead-letter. O motivo registrado é `MaxDeliveryCountExceeded`, e o texto da Avalara se perde.
  - **Consulta:** o status "erro" vira sempre a frase "Integração rejeitada pela plataforma de
    compliance.", porque a resposta nativa só é lida em `id` e `status`.
  - **Settings malformadas:** são toleradas, e a URL cai no padrão.
- **Registro.** O `SqlProcessingStore` trata o `Reason` assim:
  - a rejeição grava `IntegrationError` com o motivo;
  - a submissão grava o `Reason` nulo;
  - a consulta grava o `Reason` recebido, que é nulo na confirmação.

  O `Reason` é `nvarchar(max)`.
- **Dashboard.** O `DocumentDetail` mostra o `Reason` num banner de erro, qualquer que seja o status.
  `FAILURE_STATUSES` = `IntegrationError`, `Unconfirmed` e `DeadLettered`.
- **Autenticação da Avalara.** O host não liga o `AvalaraTokenProvider`. O envio vai sem token, e só o mock
  responde. Ligar o sandbox real é a próxima fatia (D18).
- **Mock.** O POST devolve um GUID. O status é "carregado" ou "erro" por comando, sem motivo.
- **Perfil de dev.** As `OutboundSettings` do tenant-a trazem, por ambiente, `baseUrl` e as referências
  de segredo. O seed só roda com a tabela vazia.

**As 5 NF-e 55 do fiscosysdev** (fixtures gravadas):

| Chave natural | Emissão / sentido | Chave de acesso | Impostos | Encargo |
|---|---|---|---|---|
| `brmf\|BRMF06-110000027` | terceiros / entrada (Proseware) | vazia | ICMS 90, ICMSDiff 90, PIS 98, COFINS 98, todos com valor 0 e base em "outras" | — |
| `brmf\|BRMF21-10000026` | própria / saída | ok | ICMS 00, IPI 51, PIS 01, COFINS 01 | — |
| `brmf\|BRMF21-10000027` | própria / saída | ok | ICMS, IPI, PIS, COFINS | — |
| `brmf\|BRMF12-30000001` | própria / saída | ok | ICMS, PIS, COFINS | — |
| `brmf\|BRMF06-110000031` | própria / entrada, importação (Wide World Importers, **sem CNPJ**) | ok | ICMS, IPI 01, PIS 56, COFINS 56, `ImportTax` complementado (4.500 / 30 / 1.350) | Outras, 416,25 |

- **Nenhuma delas tem IBS/CBS nem retenção.** O estabelecimento próprio é sempre a Contoso (CNPJ
  `44278225000180`).
- **Na base inteira (547 impostos):** nenhum item tem dois impostos do mesmo tipo no mesmo destino, e todo
  CST é numérico ou vazio. IRRF, ISS e `ImportTax` vêm sem CST.

**Evidência sobre o contrato**, em ordem de autoridade:

1. **Os dois JSONs reais que o usuário forneceu,** um de mercadoria e um de serviço. Eles dizem o que a
   Avalara exige para integrar, e onde.
2. **O schema completo** em `docs/contracts/avalara-document.example.json`, no formato de exemplo do
   Swagger. Ele dá os nomes e os tipos dos campos do contrato da Avalara, mas não diz o que é obrigatório
   nem a semântica dos códigos.

Nenhuma dessas fontes documenta as tabelas de códigos numéricos da Avalara.

A instrução anterior do usuário, de mandar os impostos clássicos no array genérico, veio da leitura do
contrato derivado do mock. Os JSONs reais a contradizem, e a evidência ganha (D6).

## Goals / Non-Goals

**Goals:**

- **A lista do que o hub julga é curta e auditável (D1).** Cada item tem um porquê estrutural, derivado do
  princípio, e não do resultado de algum teste.
- **Todo campo do JSON mínimo de mercadoria tem destino no desenho (D3):** vai, com a fonte citada, ou
  não vai, com o motivo.
- **Onde evidência real contradiz uma decisão anterior, vale a evidência (D6).**
- **Nada é inventado.** Valor ausente não é escrito: nem zero, nem nulo explícito, nem texto de
  preenchimento.
- **Tudo o que o domínio tem e não vai é dito no registro do documento (D7).**
- **O motivo da plataforma aparece no dashboard com a mesma clareza da rejeição nossa (D10, D11).**
- **`dotnet test` fica verde e sem rede.** A integração ponta a ponta roda contra o mock em memória.

O desfecho esperado do teste manual, com as 5 NF-e 55 enviadas, é consequência da linha, e não critério
para escolher regra.

**Non-Goals:**

- **Descobrir as tabelas de código da Avalara.** Cada código entra depois, com evidência, como uma linha
  numa tabela.
- **Mandar campo sem evidência.** Todo campo que vai além do JSON mínimo usa um nome do schema completo da
  Avalara. Esses campos estão marcados como "schema" no D3 e no D6.
- **Mexer no retry do transporte.** A rejeição vinda do envio usa o mesmo caminho da rejeição na validação.
- **Ligar o sandbox real da Avalara.** É a próxima fatia (D18).
- **Implementar o hash de transição do canônico.** A regra fica registrada para a primeira mudança de
  versão com tenant em produção (D17).

## Decisions

### D1. A linha: o que o hub julga (questão a do pedido)

O hub rejeita antes da plataforma só o que **impede a requisição de existir**. Essa lista é o contrato do
que o hub se permite julgar:

| Onde | Regra | Por que é estrutural |
|---|---|---|
| Validador (Application) | **nota sem item** | Sem item não há `itens`, e não há o que integrar. Vale para qualquer destino. |
| Adapter de saída | **tenant sem tradução do estabelecimento** (D4) | Sem `codigoEmpresa` e `codigoContribuinte`, a plataforma não sabe de quem é a nota. Não há valor honesto para pôr no lugar. |
| Adapter de saída | **campo inteiro do contrato que não vira número:** CFOP, modelo, CST de bloco (D6) | O contrato leva esses campos como inteiros. Um CFOP vazio ou um CST `XX` não podem ser representados. |
| Adapter de saída | **dois tributos do mesmo bloco no mesmo item** (D6) | O contrato leva um bloco de cada tributo por item. Somar ou escolher um deles seria julgamento. |

**O que sai do validador, e por quê:**

| Regra de hoje | Veredito | Motivo |
|---|---|---|
| Chave com número de dígitos diferente de 44 | sai | O formato da chave e a obrigação de tê-la são regra fiscal do leiaute. A chave é composta por UF, ano e mês, CNPJ, modelo, série, número, tipo de emissão, código e dígito verificador. Conferir isso é validar a nota, que é papel de quem autoriza e de quem escritura, e não do conector. Chave vazia vai sem `chaveNFe` (D3). |
| CFOP com número de dígitos diferente de 4 | sai | É formato de conteúdo, e a tabela de CFOP é fiscal. Fica no adapter só a representabilidade como número. |
| NCM ausente | sai | O NCM nem está no contrato mínimo, e a ausência dele é conteúdo. |
| Grupo IBS/CBS ausente | sai | É conteúdo fiscal. É a decisão que está sendo invertida (D2). |
| CST ou `cClassTrib` vazios no grupo | sai | É conteúdo fiscal. |

**Por que a conferência do contrato fica no adapter, e não no validador.** O `IDocumentValidator<T>` é
agnóstico de destino. Que o CFOP e o CST sejam inteiros e que existam dois códigos da empresa é exigência
do contrato da Avalara. Pôr essas regras no validador levaria o contrato de um destino para dentro da
Application. O adapter confere tudo de uma vez e rejeita com a lista completa, e não na primeira falha.

**Alternativa.** Manter as regras de formato no validador, como "higiene". Foi descartada: cada regra de
formato é um julgamento que pode divergir do da plataforma, e é exatamente essa divergência que se quer
eliminar.

### D2. Grupo da Reforma ausente não é julgado em lugar nenhum

A regra vivia em três lugares, e sai dos três:

- **Validador:** sai (D1).
- **Mapper:** o guard sai, e item sem grupo vai sem entradas da Reforma.
- **Parser XML:** `ParseReformTaxes` devolve `null` quando o item não tem `IBSCBS`. Um `IBSCBS` sem
  `gIBSCBS` continua sendo falha de leitura, porque é estrutura quebrada do documento de origem, e não
  conteúdo fiscal. É a mesma regra do D365: nenhum dos três tipos deixa o grupo ausente, e presença parcial
  falha.

Tirar a regra do validador e do mapper e deixá-la no parser faria a NF-e em XML anterior à Reforma
continuar parando, só que na dead-letter, com motivo pior.

### D3. O contrato a partir do JSON real de mercadoria

O `AvalaraContract` é reescrito com os campos do JSON mínimo de mercadoria, mais os campos do schema
completo que o D6 e o D8 justificam. As regras do DTO:

- **Campo opcional é anulável** (`int?`, `decimal?`, `string?`, objeto).
- **Serialização:** o `JsonSerializerOptions` do adapter usa `DefaultIgnoreCondition = WhenWritingNull`,
  para que ausente seja ausente no JSON.
- **Nomes:** em camelCase. O `TipoItem` com T maiúsculo do exemplo real é tratado como grafia do exemplo;
  vale o `tipoItem` do schema completo. Nesta fatia ele nem vai.

Na tabela abaixo, "vai" significa que o campo sai no payload quando o documento tem o valor. As fontes do
D365 citam a entidade e o campo, e, entre parênteses, o campo do domínio.

**Topo**

| Campo | Fonte | Nesta fatia |
|---|---|---|
| `codigoEmpresa` | `OutboundSettings.<ambiente>.establishments[<CNPJ próprio>].codigoEmpresa` | vai (D4) |
| `codigoContribuinte` | `…establishments[<CNPJ próprio>].codigoContribuinte` | vai (D4) |
| `finalidadeNotaFiscal` | `FSFiscalDocumentBR.Purpose` (enum) | **não disponível.** Os valores do enum no F&O não foram gravados, e a tabela da Avalara não está documentada. O `1` do exemplo bate com o `finNFe` "normal", mas é um exemplo só. |
| `operacao` | — | **não disponível.** A semântica não está documentada. Os dois exemplos, um de fornecedor e um de cliente, trazem `0`, então não é o sentido da nota. |
| `serie` | `FiscalDocumentSeries` (`Series`) | vai |
| `modelo` | `Model` (`Model`), como número | vai. Se não for número, rejeita (D1). |
| `numeroDocumento` | `FiscalDocumentNumber` (`Number`) | vai como veio (`000001`) |
| `chaveNFe` | `AccessKey` (`AccessKey`) | vai quando não vazia |
| `dataEmissao` | `FiscalDocumentDateTime`, ou `FiscalDocumentDate` se aquele vier vazio (`IssueDate`) | vai |
| `dataEntradaSaida` | `AccountingDate` (`EntryExitDate`, **novo**) | vai. d365/04 §2.4 diz: data contábil = data de entrada. Para a saída, ver Open Questions. |
| `periodoEscrituracao` | derivado de `dataEntradaSaida`: 1º dia do mês, 00:00Z | vai. Os dois exemplos batem com "mês da data de entrada/saída". O formato segue o exemplo de mercadoria. |
| `situacao` | derivado: só nota `Approved` é despachada, como emitida | vai `0`. Os dois exemplos trazem `0`, e o legado usa `cod_sit 00` para `Approved` (d365/04 §2.6). |
| `tipoPagamento` | `FSFiscalDocumentBR.PaymentMethod` existe | **não disponível.** Não há tradução com evidência. |
| `codigoReferenciaIntegracao` | chave natural do hub (`DispatchContext.NaturalKey`) | vai. É a referência nossa, e não do ERP. |
| `origemSistema` | — | **não disponível.** Falta no exemplo de serviço, então não é obrigatório, e a semântica não está documentada. |

**`parceiro`** (a contraparte, D5)

| Campo | Fonte | Nesta fatia |
|---|---|---|
| `codigo` | provável `FSFiscalDocumentBR.FiscalDocumentAccountNum` | **fora de escopo** (enriquecimento do código do parceiro) |
| `nome` | `ThirdPartyName` ou `FiscalEstablishmentName` da contraparte (`Party.Name`) | vai |
| `cnpj` / `cpf` | `*CNPJCPF` da contraparte (`Party.TaxId`) | vai pelo tamanho: 14 dígitos em `cnpj`, 11 em `cpf`, e nenhum nos outros casos. O `cpf` vem do schema. |
| `ativo` | cadastro (`FSVendorBR`/`FSCustomerBR.Blocked`) | **fora de escopo** (é cadastro do parceiro, e não dado da nota) |
| `endereco` | `FSPostalAddressBR.Street`, pelo `*PostalAddress` (`Party.Address.Street`, **novo**) | vai |
| `numero` | `FSPostalAddressBR.StreetNumber` | vai |
| `bairro` | `FSPostalAddressBR.DistrictName` | vai |
| `cep` | `FSPostalAddressBR.ZipCode`, só dígitos | vai |

**`itens[]`**

| Campo | Fonte | Nesta fatia |
|---|---|---|
| `numeroSequencia` | `LineNum` (`Number`) | vai |
| `item.codigo` | `ItemId` (`ProductCode`) | vai |
| `item.descricao` | `Description` | vai |
| `item.unidadeMedida.codigo` | `FSFiscalDocumentLineBR.Unit` (`Unit`, **novo**) | vai. É a unidade da nota. A unidade de estoque do item (`FSItemBR.BOMUnitId`) é outra coisa. |
| `item.unidadeMedida.descricao` | `FSUnitOfMeasureBR.Description` | **não disponível.** A entidade tem `LanguageId`, e a escolha do idioma não foi feita. |
| `item.tipoItem` | `FSFiscalDocumentLineBR.ItemType` ou `FSItemBR.InventProductType` | **não disponível.** Os exemplos (`7` e `"9"`) batem com o TIPO_ITEM do SPED (registro 0200), mas não se gravou qual campo do F&O corresponde a ele, nem com que valores. |
| `cfop` | `CFOP`, só dígitos, como número | vai. Se não for número, rejeita (D1). |
| `unidadeMedida.codigo` | `Unit` | vai |
| `unidadeMedida.descricao` | — | **não disponível** (mesmo motivo da descrição do item) |
| `quantidade` | `Quantity` | vai |
| `valorTotal` | `LineAmount` (`TotalAmount`) | vai |
| `valorContabil` | `FSFiscalDocumentLineBR.AccountingAmount` (`AccountingAmount`, **novo**) | vai |
| `origemCredito` | candidato `FSFiscalDocumentLineBR.CreditSourceCode`; o legado derivava do 1º dígito do CFOP | **não disponível.** O formato não foi gravado, e derivar do CFOP seria regra fiscal nossa. |
| `imposto` | tributos clássicos do item (`Taxes`, `Withholdings`) | vai, bloco a bloco (D6, D8) |
| `impostos[]` | grupo IBS/CBS (`ReformTaxes`) | vai só quando o item tem o grupo (D6) |

**Entrada de `impostos[]`** (só o grupo IBS/CBS)

| Campo | Fonte | Nesta fatia |
|---|---|---|
| `imposto.codigo` | `CBS`, `IBS ESTADUAL` ou `IBS MUNICIPAL`, pela parcela | vai |
| `situacaoTributariaImposto.imposto.codigo` | o mesmo código | vai |
| `situacaoTributariaImposto.codigo` | `ReformTaxes.Cst` | vai quando houver |
| `classificacaoTributariaImposto` | `ReformTaxes.ClassTrib`; no D365 vem sempre vazio | vai quando não vazia |
| `valorBaseTributo` | `ReformTaxes.TaxBase` | vai |
| `aliquotaTributo` | `Rate` da parcela | vai |
| `valorTributoBruto` | `Amount` da parcela | vai |
| `valorTributoLiquido` | — | **não disponível.** O domínio não distingue bruto de líquido. O exemplo repete o bruto, e repetir seria afirmar que não há dedução. |
| `embasamentoLegal` | — | **não disponível.** O "Base Legal não informada" do exemplo é texto de preenchimento. |
| `origemInformacao` | — | **não disponível.** A semântica não está documentada. |

**`totais`**

| Campo | Fonte | Nesta fatia |
|---|---|---|
| `valorMercadorias` | `TotalGoodsAmount` (`GoodsAmount`, **novo**) | vai |
| `valorDocumento` | `TotalAmount` | vai |
| `valorTotalComIBSCBSeIS` | — | **não disponível.** O F&O não tem o campo. |
| `icms.valorBaseICMS`, `icms.valorICMS`, `pis.valorPIS`, `cofins.valorCOFINS` | — | **não disponível.** O cabeçalho do F&O não traz total por imposto, e somar os itens seria o hub produzindo valor fiscal. Se a plataforma exigir, o caminho é o F&O expor o total, e não o hub somar. |

### D4. Tradução dos códigos da empresa: tabela por estabelecimento, por ambiente

```json
{
  "sandbox": {
    "baseUrl": "http://localhost:5100/",
    "clientSecretRef": "kv:avalara-a-sandbox-secret",
    "clientTokenRef": "kv:avalara-a-sandbox-token",
    "establishments": {
      "44278225000180": { "codigoEmpresa": "20247332000182", "codigoContribuinte": "20247332000182" }
    }
  },
  "production": { "baseUrl": "https://api.avalara.com/", "establishments": { } }
}
```

- **Leitura.** Um `AvalaraOutboundSettings` interno ao adapter lê o perfil. A seção vem de
  `profile.Environment.ToLowerInvariant()`, como já acontece com a `baseUrl`. As chaves de `establishments`
  são normalizadas para dígitos, e as duas formas são aceitas: `44.278.225/0001-80` e `44278225000180`.
- **O que falha.** Faltar o perfil, a seção do ambiente, o `establishments`, a entrada do estabelecimento
  ou um dos dois campos lança `DispatchRejectedException` (D10), sem nenhuma requisição à plataforma. O
  mesmo vale para settings que não são JSON válido.
  - **Mensagem:** a regra é "Configuração do conector: …", citando o tenant, o ambiente e o CNPJ e
    nomeando os campos que faltam.
  - **Exemplo:** "Configuração do conector: o tenant 'tenant-a', no ambiente 'sandbox', não tem tradução
    para o estabelecimento 44278225000180 (faltam codigoEmpresa e codigoContribuinte em
    OutboundSettings.sandbox.establishments)."
- **A `baseUrl` continua com fallback** para a config do adapter quando está ausente, como hoje. Os códigos
  não têm fallback, porque não há valor honesto padrão.
- **Por que a chave é o CNPJ.** É a identidade do estabelecimento em toda NF-e, qualquer que seja o ERP, e
  está no domínio. O `dataAreaId` é do D365 e não chega ao domínio. Com o CNPJ, a mesma tabela serve XML e
  D365.
- **Por que tabela, e não um par por ambiente** (decisão do usuário):
  - cliente real tem várias filiais e empresas, e o perfil D365 já aceita várias `companies`;
  - um par único mandaria a nota de toda filial para o mesmo contribuinte na plataforma;
  - a tabela também responde qual lado da nota é o nosso (D5).

### D5. Estabelecimento próprio e parceiro

**Domínio.** Entra `GoodsInvoice.Issuance`, anulável, com os valores `Own` e `ThirdParty`. É o IND_EMIT
do SPED: emissão própria ou de terceiros, do ponto de vista de quem escritura a nota.

- **O D365 preenche** pelo `FiscalDocumentIssuer`, que a montagem já lê para decidir emitente e
  destinatário.
- **O XML deixa nulo:** o XML não diz de qual lado está o tenant.

**Regra do adapter.**

1. Com `Issuance`, o próprio é o emitente (`Own`) ou o destinatário (`ThirdParty`).
2. Sem `Issuance`, o próprio é a única parte cujo CNPJ está em `establishments` do ambiente. Se nenhuma
   parte ou as duas estiverem lá, lança `DispatchRejectedException` citando os dois CNPJs.
3. O parceiro é a outra parte.

- **Por que as duas fontes.** Só a tabela não resolve a transferência entre filiais, que é comum, porque
  as duas partes estão na tabela. Só a `Issuance` deixa o XML sem resposta. O D365 sabe o lado, e o XML
  depende da configuração.
- **Correção de comportamento.** Hoje o parceiro é sempre o destinatário. Nas duas notas de entrada da
  base, o parceiro mandado era a própria Contoso.

### D6. Impostos: bloco estruturado para os clássicos, array só para IBS/CBS

**A evidência ganhou da instrução anterior.**

- **O que a primeira versão fazia:** mandava os clássicos no array genérico `impostos`, por instrução do
  usuário. A instrução veio da leitura do contrato derivado do mock.
- **O que os dois JSONs reais mandam:** ICMS, PIS e COFINS no bloco `imposto` do item (e ISSQN, no de
  serviço), e o array só para IBS/CBS.
- **A regra:** onde evidência real contradiz uma decisão nossa, vale a evidência. É o princípio desta
  change, a plataforma como autoridade sobre o próprio contrato, aplicado ao nosso desenho.

**Níveis de evidência:**

| Nível | Fonte | O que prova |
|---|---|---|
| JSON real | os dois JSONs aceitos | que o campo é exigido e onde ele fica |
| schema | `docs/contracts/avalara-document.example.json` | nome e tipo do campo no contrato da Avalara; não prova que é exigido |

Um tributo vai no payload quando tem evidência de JSON real, ou quando tem evidência de schema com bloco
próprio do mesmo tributo e campos de base, alíquota e valor sem ambiguidade. É a decisão do usuário. O resto
é omissão (D7).

**Mapeamento por tipo:**

| Domínio | Bloco do item | Campos | Evidência |
|---|---|---|---|
| `Icms` | `imposto.icms` | `situacaoTributariaICMSTabA` ← origem do item (D12); `valorBaseICMS` ← base; `aliquotaICMS`; `valorICMS` | JSON real |
| | | `situacaoTributariaICMSTabB` ← CST; `valorBaseIsentoICMS` ← base isenta; `valorBaseOutrosICMS` ← base em "outras" | schema |
| `Pis` | `imposto.pis` | `situacaoTributariaPIS` ← CST; `baseCalculoPIS`; `aliquotaPIS`; `valorPIS` | JSON real |
| `Cofins` | `imposto.cofins` | `situacaoTributariaCOFINS` ← CST; `baseCalculoCOFINS`; `aliquotaCOFINS`; `valorCOFINS` | JSON real |
| `Iss` | `imposto.issqn` | `baseCalculoISSQN`; `aliquotaISSQN`; `valorISSQN` | JSON real (serviço) |
| `Ipi` | `imposto.ipi` | `situacaoTributariaIPI` ← CST; `baseCalculoIPI`; `aliquotaIPI`; `valorIPI`; `valorBaseIsentoIPI`; `valorBaseOutrosIPI` | schema |
| `ImportTax` | `imposto.ii` | `baseCalculoII`; `aliquotaII`; `valorII` | schema (D9) |
| `IcmsSt` | `imposto.icmsst` | `valorBaseICMSST`; `aliquotaICMSST`; `valorICMSST` | schema |
| grupo IBS/CBS | array `impostos` | uma entrada por parcela: `CBS`, `IBS ESTADUAL`, `IBS MUNICIPAL` (D3) | JSON real |

**Regras:**

- **CST como número.** Os campos de CST do bloco são inteiros (`70`, `1` e `0` nos exemplos), então o CST
  vira número (`01` → 1). CST vazio deixa o campo ausente. CST que não é número é problema de contrato
  (D1), com o item e o tipo citados. Na base gravada, todo CST é numérico ou vazio.
- **`TabA` e `TabB`.** O CST do ICMS tem duas partes no leiaute: a origem (Tabela A) e a tributação
  (Tabela B). O JSON real manda só a `TabA` (origem `0`). O `TaxationCode` do F&O é a tributação (`00`,
  `90`), e vai na `TabB`, que é campo do schema. A origem vem do item (D12). Sem origem traduzível, a
  `TabA` fica ausente.
- **Base isenta e base em "outras".**
  - Vão só nos blocos que têm campo para elas, que são o ICMS e o IPI.
  - Vão só quando não são zero. O `TaxLine` guarda as duas como número com zero padrão, e um source que
    não as lê produziria zero sem ter afirmado nada. Zero nessas parcelas é a ausência da parcela, e não se
    escreve.
- **Um bloco por tributo por item.** O contrato leva um `icms` por item. Dois tributos do mesmo bloco no
  mesmo item teriam de ser somados ou escolhidos, e as duas coisas são julgamento. Por isso é problema de
  contrato (D1). A base gravada não tem esse caso.
- **Tributo sobre encargo:** não vai, mesmo que tenha bloco (D7).
- **Grupo IBS/CBS:**
  - vira três entradas, e não "IBS" (total) mais "CBS" como hoje;
  - cada entrada leva o CST do grupo, a classificação do grupo quando não vazia, a base do grupo e a
    alíquota e o valor da parcela;
  - o `IbsTotalAmount` do domínio não vai, porque o contrato não tem total do IBS;
  - tributo clássico nunca leva `classificacaoTributariaImposto`, porque não entra no array.

**Por que o `ICMSDiff` não vai no `icms.difal`.**

- O bloco `icms.difal` do schema é a partilha da EC 87/2015, na venda interestadual a não contribuinte:
  alíquota de destino, percentual de partilha, valores de origem e de destino e FCP.
- O `ICMSDiff` do F&O, na nota da base, é o diferencial de alíquota da compra para uso e consumo.
- Pôr um no lugar do outro seria decidir que são o mesmo tributo, o que é julgamento fiscal. Por isso
  fica como omissão até a plataforma dizer onde vai.

**Alternativas.**

- **Manter o array genérico** (instrução anterior): contrariado pelos dois JSONs reais.
- **Só o JSON mínimo:** o IPI e o II virariam omissão, embora o contrato da Avalara tenha bloco próprio
  para eles. O usuário escolheu usar o schema.

### D7. O que o documento tem e o contrato não leva: omissão visível

É a decisão do usuário, entre três opções: omitir e mostrar, mandar num lugar aproximado ou rejeitar a
nota.

**O que vira omissão:**

- **Tributo sem lugar no contrato.** São o `IcmsDiff` (D6), o `Irrf`, o `Inss`, o `InssCprb` e a `Csll`
  não retidos, e o `Other`.
- **Retenção que não é de ISS** (D8).
- **Encargo**, com os tributos e retenções dele.
- **Imposto Seletivo.** O array dos JSONs reais só tem IBS e CBS.

**Mecanismo.**

- **Coleta.** O mapper devolve o payload e a lista de omissões, em texto, na ordem de item. O formato é
  `item <n>: <o quê> (<por quê>)`. Exemplos:
  - `item 1: IcmsDiff não enviado (sem lugar no contrato)`;
  - `item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)`;
  - `item 2: retenção Irrf não enviada (impostosRetidos sem tradução de tipoImposto)`.
- **Recibo.** O `IntegrationReceipt` ganha `IReadOnlyList<string> Omissions`, vazia por padrão. Assim a
  mudança não quebra nenhum outro despachante.
- **Registro:** D11.

**Por que omitir, e não as outras duas.**

- **Mandar num lugar aproximado** decide o que a plataforma deve entender por aquele dado. Exemplos: o
  `ICMSDiff` no `difal`, ou o IS no array com um código deduzido. Uma plataforma que aceitasse
  escrituraria o tributo errado, o que é pior que ausente.
- **Rejeitar a nota** trataria como estrutural algo que não impede a requisição de existir. É o oposto da
  linha do D1.
- **Omitir e dizer** entrega a nota, que é o papel do conector, e põe a lacuna do nosso lado, à vista.

### D8. Retenção (questão do pedido)

Os JSONs mínimos não têm retenção. O schema completo mostra dois lugares para ela:

- **ISS retido:** campos próprios no bloco `imposto.issqn` do item. São eles `valorBaseISSRetido`,
  `aliquotaISSRetido` e `valorISSRetido`.
- **As demais retenções:** `impostosRetidos[]`, no nível do documento. Os campos são `tipoImposto`
  (inteiro), `valorBaseRetencao`, `aliquotaRetencao`, `valorRetido` e `sequenciaItem`, que liga a retenção
  ao item.

**Decisão:** campo separado, e nunca entrada no array `impostos` nem flag.

- **ISS retido:** vai nos campos `*ISSRetido` do `issqn`. O nome é inequívoco e está no schema, que é o
  mesmo nível de evidência do IPI e do II (D6).
- **As demais:** o lugar é `impostosRetidos`, mas o `tipoImposto` é um código numérico sem tabela
  confirmada. Mandar sem ele, ou com um chute, seria inventar. Cada uma vira omissão (D7), e o DTO de
  `impostosRetidos` entra junto com a tabela.
- **Nunca no array `impostos`:** uma entrada ali seria lida como imposto do item e somada a ele. É o erro
  que o domínio já evita, ao guardar as retenções numa coleção separada.

**Efeito nas NF-e 55 da base:** nenhum. As 12 retenções da base estão em notas `01` e `SE` (11 IRRF e 1
ISS).

### D9. Base duplicada do `ImportTax` (questão c do pedido)

Na nota de importação, depois do complemento contábil, o imposto de importação fica com três valores:

- `TaxBase` 4.500 (vindo da contábil);
- `OtherBase` 4.500 (vindo da fiscal, `TaxBaseAmountOther`);
- valor 1.350.

**Decisão:** o II vai no bloco `imposto.ii` com `baseCalculoII` = `TaxBase` (4.500), `aliquotaII` = 30 e
`valorII` = 1.350.

- **Por que a `TaxBase`:**
  - é a base sobre a qual o imposto foi calculado: 4.500 × 30% = 1.350. Base, alíquota e valor fecham
    entre si;
  - a `OtherBase` é a classificação da base no livro fiscal ("outras"), e o bloco `ii` não tem campo para
    ela, então não vai;
  - não há escolha entre os dois números para o mesmo campo.
- **A regra geral é a do D6:** base isenta e base em "outras" só vão nos blocos que têm campo para elas
  (ICMS e IPI). Qual das duas bases vale no livro fiscal do II continua como pergunta para o fiscal, mas não
  afeta o payload.

**Alternativa descartada:** bloquear o envio do II como pendência do fiscal. O bloco `ii` só tem um campo de
base, e a `TaxBase` fecha com o valor. Bloquear deixaria a plataforma sem um imposto que o contrato dela sabe
receber.

### D10. Rejeição vinda do envio (questão b do pedido)

Antes, o desfecho "rejeitado" era do hub. Agora ele vem da plataforma, ou do conector quando a requisição
não pode existir.

```csharp
// Application/Outbound
public sealed class DispatchRejectedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

// DocumentPipeline.ProcessAsync, no lugar do envio de hoje
IntegrationReceipt receipt;
try
{
    receipt = await _dispatcher.SubmitAsync(document, context, ct);
}
catch (DispatchRejectedException rejected)
{
    await _store.RecordRejectionAsync(reference, rejected.Reason, ct);
    return;
}
await _store.RecordSubmissionAsync(reference, receipt, ct);
```

**Ordem do `SubmitAsync` na Avalara:**

1. Perfil e `AvalaraOutboundSettings` (D4).
2. Estabelecimento próprio, parceiro e códigos (D4, D5).
3. Mapeamento: payload, problemas de contrato e omissões. Havendo problema de contrato, lança
   `DispatchRejectedException` com todos os problemas, abertos por "Contrato do destino: …".
4. Foto do destino (ADR-0006), como hoje, antes do POST.
5. POST.
   - **400 ou 422:** lê o corpo, extrai o motivo e lança `DispatchRejectedException` com o texto
     "Plataforma de compliance recusou: <motivo>". Havendo omissões, acrescenta " | Enviado sem: …".
   - **Outro status sem sucesso:** exceção, como hoje, e retry nativo.
6. Recibo com `ExternalId` e `Omissions`.

**Consulta de status.** O `AvalaraStatusResponse` passa a ser lido como `JsonElement`. O `status` é
traduzido como hoje. No erro, o motivo é extraído da mesma resposta, sem a propriedade `status`, e sem
mensagem vira "Plataforma de compliance rejeitou sem informar a causa." O `IntegrationResult.Message`
passa a levar "Plataforma de compliance rejeitou: <motivo>".

**Extração do motivo** (`PlatformMessage`, interno ao adapter; o formato real não é conhecido):

- **Corpo vazio:** "HTTP <status> sem corpo".
- **JSON:** junta, em ordem, os textos encontrados nas propriedades de nome `mensagem`, `mensagens`,
  `message`, `messages`, `erro`, `erros`, `error`, `errors`, `detail`, `title` e `descricao`, sem distinguir
  maiúscula.
  - A busca é recursiva, em arrays de texto e em objetos.
  - Num objeto de erros por campo (`ProblemDetails.errors`), cada texto vira `<campo>: <mensagem>`.
  - `id`, `status`, `type` e `traceId` ficam de fora.
  - Se não sobrar nada, vale o JSON compactado.
- **Não JSON:** o texto, com os espaços colapsados.
- **Tamanho:** corte em 1.000 caracteres, com reticências.

**Por que não é um esquema novo de retry.**

- A rejeição de conteúdo já era registrada sem exceção e sem retentativa: é a rejeição na validação.
- Isto leva o mesmo desfecho para as rejeições que só se descobrem na hora do envio.
- O erro transitório continua no retry nativo (ADR-0004).
- A idempotência não muda: `IntegrationError` não bloqueia o reprocessamento (`AlreadyProcessedAsync` só
  olha `Submitted`/`Confirmed`).

**Relação com o ADR-0003.** O status continua normalizado, e o texto nativo `erro` não sai do adapter. O
que atravessa é a mensagem humana da plataforma. É um refinamento, registrado no ADR-0026: a frase
agnóstica de hoje escondia justamente o que o usuário precisa ver.

**Alternativas.**

- **Devolver um recibo com `Status = IntegrationError` em vez de exceção.** Obrigaria o `ExternalId`
  (hoje obrigatório) a ficar opcional, e mudaria o contrato do `RecordSubmissionAsync`. A exceção tipada
  repete o desenho da `DocumentOutOfScopeException`.
- **Tratar 401 e 403 como rejeição.** Auth quebrada é do tenant inteiro, e não do documento, por isso
  segue retentativa e dead-letter visível. Ligar o token é a próxima fatia (D18).

### D11. Omissão no registro e no dashboard

- **`RecordSubmissionAsync`:** `Reason` = `null` sem omissões; com omissões, `"Enviado sem: " +
  string.Join("; ", omissions)`.
- **`MarkPolledAsync`:** enquanto o documento está `Submitted`, o `Reason` só pode ser a observação do
  envio. Por isso a consulta faz o seguinte:
  - **motivo novo nulo** (confirmação, ou ainda em processamento): mantém o `Reason`;
  - **motivo novo presente** (erro, sem confirmação), com `Reason` anterior: grava
    `"<motivo novo> | <Reason anterior>"`.
- **Dashboard:**
  - o `Banner` ganha o tom `warn`, e o `DocumentDetail` usa `error` para `isFailure(status)` e `warn` nos
    outros casos;
  - com isso, o motivo de um `Ignored` também deixa de aparecer como erro;
  - o `FAILURE_STATUSES` não muda, e a omissão não entra nos KPIs de falha;
  - não há mudança de contrato da API: o `reason` já é exposto.
- **Sem coluna nova e sem migration:** o `Reason` é `nvarchar(max)`.

**Alternativa.** Uma coluna própria de observações. Separa melhor, mas pede migration e mudança na API e na
tela, para um texto que o `Reason` já carrega e que o dashboard já mostra.

### D12. Domínio aditivo e leitura no D365

| Domínio (novo, opcional) | D365 | XML |
|---|---|---|
| `GoodsInvoice.Issuance` | `FiscalDocumentIssuer` (já lido) | nulo |
| `GoodsInvoice.EntryExitDate` | `AccountingDate`; `1900-01-01` = ausente | nulo (non-goal) |
| `GoodsInvoice.GoodsAmount` | `TotalGoodsAmount` | nulo (non-goal) |
| `Party.Address` (`Street`, `Number`, `District`, `PostalCode`) | `FSPostalAddressBR` pelo cache | nulo (non-goal) |
| `GoodsInvoiceItem.Unit` | `Unit` (vazio = ausente) | nulo (non-goal) |
| `GoodsInvoiceItem.AccountingAmount` | `AccountingAmount` | nulo (non-goal) |
| `GoodsInvoiceItem.Origin` (origem da mercadoria, 0 a 8) | `Origin` da linha, traduzido | nulo (non-goal) |

- **`$select`:**
  - o cabeçalho ganha `AccountingDate` e `TotalGoodsAmount`;
  - a linha ganha `Unit`, `AccountingAmount` e `Origin`;
  - o endereço do cache ganha `Street`, `StreetNumber`, `DistrictName` e `ZipCode`.

  O número de GETs por montagem não muda.
- **Origem.** A linha fiscal carrega a origem do item no momento da emissão (d365/04 §3.3). Ela é a `TabA`
  do CST do ICMS (D6). O formato do valor ainda não foi gravado, e a gravação da fatia 4 é que define a
  tradução:
  - número de 0 a 8 vai como está;
  - nome de enum é traduzido pela tabela de origem do leiaute (0 a 8), pelos rótulos do enum, e a tabela
    fica registrada no d365/04;
  - valor sem tradução deixa a origem ausente, e nunca vira `0`.
- **Endereço.** O `D365PartyMunicipalities` passa a carregar o endereço junto com o município, e segue a
  mesma regra: FK vazia ou registro não encontrado deixa a parte sem endereço. Cadastro continua fora do
  canônico.
- **Canônico v2.** O cabeçalho e a linha entram no canônico pelo `$select`. Mudar o `$select` muda a
  impressão, e por isso `D365Canonicalizer.Version` passa de 1 para 2, como manda o D11 da fatia anterior.
  A consequência operacional está no D17.
- **Fixtures.** O `tools/d365-fixtures/Record-D365Fixtures.ps1` ganha os `$select` novos e regrava:
  - `notes/*`, `scope/*`, `reference/postaladdress-*` e `snapshot/headers|lines`;
  - as derivadas (`*.derived.json`), refeitas sobre as novas gravações, com a mesma edição descrita no
    `README.md`.

  A gravação é opt-in e precisa de `az login` no fiscosysdev.

### D13. O mock

- **Resultado padrão por comando:** `carregado`, `erro` ou `rejeitar`, no mesmo `/admin/result/{value}`,
  com `?motivo=` opcional.
- **`erro`:** o status devolve `{ "id", "status": "erro", "mensagens": ["<motivo>"] }`.
- **`rejeitar`:** o POST devolve 400 com `{ "mensagens": ["<motivo>"] }` e não guarda o documento.
- **Motivo padrão:** diz que é simulação do mock.
- **Formatos presumidos:** até haver resposta real gravada, o que acontece na fatia do sandbox (D18). A
  extração do D10 não depende deles.
- **`public partial class Program;`** no fim do `Program.cs`, para o teste ponta a ponta subir o mock em
  memória (`WebApplicationFactory<Program>`). O `FiscalHub.Integration.Tests` ganha, só no teste:
  - o pacote `Microsoft.AspNetCore.Mvc.Testing`;
  - a referência ao mock e ao adapter da Avalara.

### D14. ADR-0026 e as revisões

`docs/adr/0026-conector-nao-validador.md` registra:

- **O argumento da inversão:** segunda fonte de verdade fiscal, perseguir as regras da plataforma, e toda
  divergência vira nota que não chegou.
- **A linha e a lista do D1,** com a justificativa de cada regra pelo princípio.
- **A evidência acima da instrução (D6):** a instrução do array genérico foi revertida pelos JSONs reais,
  e a regra fica registrada.
- **A tradução dos códigos por estabelecimento (D4) e o estabelecimento próprio (D5).**
- **Bloco estruturado com níveis de evidência (D6) e a omissão visível (D7), com a retenção (D8).**
- **A rejeição da plataforma com o motivo dela (D10)**, como refinamento do ADR-0003.
- **A regra da versão do canônico em produção (D17).**
- **O `cClassTrib` fora do caminho crítico:** a entidade sobre `CClassTribTable_BR` deixa de ser
  pré-requisito da demonstração, porque nenhum grupo IBS/CBS é exigido.

**Revisões nos ADRs existentes:**

- **ADR-0025:** nota de revisão no §6 ("revisado pelo ADR-0026: o item sem grupo e o grupo sem
  classificação seguem para o envio"). Em "Piora", o primeiro item ganha a mesma nota. O §7 (canônico)
  aponta para a regra do D17.
- **ADR-0003:** nota apontando o refinamento da mensagem.

**Por que um ADR novo, e não reescrever o 0025.** É a convenção do repositório: o 0025 revisou o 0024 com
um ADR novo e uma nota no antigo. A decisão invertida continua legível com o argumento da época, e o
argumento novo fica ao lado. Pelo pedido, o 0025 fica "revisado" pela nota, que aponta para o 0026.

### D15. Testes

| Pedido | Onde |
|---|---|
| Item sem grupo da Reforma não é rejeitado e chega ao mapper | validador; esteira (o despachante é chamado); mapper (não lança, vai só com o bloco `imposto`) |
| Documento sem item continua rejeitado | validador; esteira (registra a rejeição, não chama o despachante) |
| Tenant sem `codigoEmpresa` ou `codigoContribuinte`: falha clara, nomeando o que falta | dispatcher: zero requisições, motivo com tenant, ambiente, CNPJ e campo. Esteira: `DispatchRejectedException` vira rejeição registrada. |
| Os dois códigos saem do `OutboundSettings`, e não do ERP | dispatcher: CNPJ `44278225000180` traduzido para `20247332000182`, e o payload não tem o CNPJ nos códigos |
| Clássicos com CST, base e valor, e classificação nula | mapper sobre o domínio da `BRMF21-10000026`: `imposto.icms`, `ipi`, `pis` e `cofins` com CST como número, base, alíquota e valor. O array `impostos` fica vazio. O JSON serializado não tem `classificacaoTributariaImposto` nem nenhum `null`. |
| Rejeição da Avalara registrada com o motivo dela | dispatcher (400, 422, 503, corpo vazio, texto puro, status erro com e sem mensagem); `SqlProcessingStore` (junção do motivo com a omissão); ponta a ponta |
| Ponta a ponta contra o mock, com o contrato novo | `FiscalHub.Integration.Tests`: nota D365 gravada, esteira, dispatcher real, mock em memória e poll. Casos: confirmada, erro e rejeitar. |

Outros testes:

- parser XML com e sem `IBSCBS`;
- montagem D365 com os campos novos (`Issuance`, datas, unidade, valor contábil, origem, endereço,
  `1900-01-01`);
- bloco `imposto`:
  - `TabA` pela origem e `TabB` pelo CST;
  - base isenta e em "outras" só quando não são zero (o ICMS CST 90 da `BRMF06-110000027`);
  - o II em `imposto.ii` com a `TaxBase`;
  - ICMS-ST e ISS, com o ISS retido;
  - CST não numérico e bloco repetido como problema de contrato;
- omissões (`IcmsDiff`, encargo, retenção que não é de ISS, IS);
- estabelecimento próprio (com `Issuance`, pela tabela, ambíguo, nenhum);
- `store`: omissão preservada na confirmação;
- dashboard: tom do banner, se a suíte de front tiver teste de componente.

### D16. RUNNING.md

**§7, desfecho esperado no fiscosysdev:** 5 enviadas e confirmadas pelo mock, e 9 ignoradas.

- `BRMF06-110000027` com "Enviado sem: item 1: IcmsDiff…".
- `BRMF06-110000031` com "Enviado sem" do encargo. O II vai no bloco `imposto.ii`.
- Payload conferível em `GET /documents/<GUID>` no mock: blocos `imposto` por item, sem array `impostos`
  (nenhuma nota tem IBS/CBS).
- Uma nota deixa explícito que o mock aceita tudo, e que o envio ao sandbox real é a próxima fatia (D18).

**Roteiro de rejeição:**

- `POST /admin/result/erro?motivo=...` antes da passada: as 5 ficam `IntegrationError`, com o motivo do mock
  no dashboard;
- `rejeitar`: o mesmo, na hora do envio.

**Passo novo de configuração:** incluir `establishments` nas `OutboundSettings` do tenant-a em banco já
existente. É o mesmo padrão de SQL por arquivo das `InboundSettings`. O seed passa a trazer, no `sandbox`:

- `44278225000180` (Contoso, `brmf`);
- `12345678000190` (o tenant-a nos XMLs de exemplo do `LocalSeed`: emitente do `nfe-exemplo.xml` e
  destinatário do `nfe-exemplo-2.xml`, o que exercita, nos dois sentidos, a identificação pela tabela do
  D5).

Os dois apontam para `20247332000182`, a empresa do JSON real do ambiente Avalara de teste. O `production`
fica sem estabelecimentos, e nesse ambiente o envio é rejeitado com motivo claro.

### D17. Versão da impressão: consequência operacional (pedido 4)

**Quando a reintegração acontece.** A impressão só é calculada quando a nota é **buscada**. Uma mudança de
versão não reintegra a base sozinha. Ela reintegra, uma vez, cada nota já integrada que for **relida**:

- **tocada no ERP:** entra na janela de sobreposição pelo `SysModifiedDateTime`. Com a v1, um toque sem
  mudança fiscal seria descartado pelo hash; com a v2, o primeiro toque depois do deploy reenvia;
- **rebobinamento da marca** ou backfill por período: relê tudo o que cair na janela;
- **reprocesso manual:** já fura a idempotência de propósito (ADR-0015), e não muda nada.

**Por escala:**

- **Nesta fatia:** irrelevante. São 14 notas, e nenhuma das 5 NF-e 55 foi enviada.
- **Numa base de cem mil notas:**
  - **Toques:** os toques do dia a dia viram um reenvio a conta-gotas.
  - **Rebobinar ou fazer backfill logo depois do deploy:** vira enxurrada.
    - **Leitura:** 4 a 5 GETs por nota, sob o teto do F&O de cerca de 1.200 por minuto.
    - **Vazão:** o consumidor serial faz cerca de 50 montagens por minuto, ou seja, mais de 30 horas.
    - **Destino:** cem mil documentos reenviados à plataforma, com comportamento não confirmado para
      reenvio (atualiza ou duplica pelo `codigoReferenciaIntegracao`?).

**O que fazer em base grande.** É regra para qualquer mudança de versão com tenant em produção:

1. **Hash de transição.** Por uma janela de transição, o source calcula duas impressões:
   - a da versão nova;
   - a da versão anterior, sobre o `$select` anterior, recortando os campos novos das mesmas respostas.

   A idempotência aceita a impressão anterior para nota já integrada (`Submitted`/`Confirmed`). Quando ela
   casa, o registro passa a guardar a impressão nova, sem reenvio. A base migra sozinha, nota a nota, à
   medida que é relida, sem enxurrada.

   Consequência: uma nota já integrada não é reenviada só porque o hub passou a ler mais campos. Se os
   campos novos importam para a plataforma, o reenvio é deliberado (item 3).
2. **Sem o hash de transição, não rebobinar a marca** nem rodar backfill depois do deploy.
3. **Reenvio desejado:** reprocesso em lotes, por período, depois de confirmar com a plataforma o que
   acontece com um documento reenviado.

**Por que não implementar agora.** Não há tenant em produção, e as notas desta base nunca foram enviadas. A
regra fica no ADR-0026 e no §7 do ADR-0025. O hash de transição vira tarefa da primeira mudança de versão
que encontrar um tenant real.

Isso não cria um esquema novo de idempotência. É o mesmo hash por conteúdo (ADR-0016), aceitando por um
período a impressão da versão anterior do mesmo conteúdo.

### D18. Ligação com a Avalara real: próxima fatia, explícita (pedido 3)

**Por que não nesta fatia.** Ela depende de três insumos que o repositório não tem:

- o fluxo de autenticação real da Avalara Brasil: endpoint, tipo de concessão e campos;
- as credenciais do sandbox;
- a `BaseUrl` do sandbox.

Hoje o `AvalaraTokenProvider` é global (`AvalaraOptions.ClientId`/`ClientSecret`), e não por tenant,
embora as `OutboundSettings` já guardem `clientSecretRef` e `clientTokenRef` por ambiente. Esta change fecha
com build, testes e o mock. A próxima começa pela autenticação.

**Conteúdo da próxima fatia** (registrado no STATUS.md e no RUNNING.md):

1. **Credenciais por tenant e por ambiente,** lidas das referências das `OutboundSettings` (`kv:`), pelo
   mesmo padrão de segredo por referência do adapter D365. Nunca em claro.
2. **Token por tenant,** com o provedor refeito para o fluxo real e ligado no host.
3. **`BaseUrl` do sandbox** no perfil do tenant de dev.
4. **Teste manual apontando para o sandbox,** e não para o mock: as 5 NF-e 55, com o desfecho que a Avalara
   der.
5. **Respostas reais gravadas** (aceite, rejeição síncrona e consulta com erro), que viram casos de teste da
   extração do motivo (D10).
6. **As Open Questions que só o envio real responde:**
   - formato do erro;
   - se um campo omitido vira `0`;
   - se os blocos pelo schema são aceitos (D6);
   - o que acontece com um documento reenviado (D17).

**O que esta fatia deixa pronto para ela:**

- o contrato pelo JSON real;
- os códigos da empresa por estabelecimento;
- a rejeição com o motivo da plataforma;
- a omissão visível.

A próxima fatia só troca o destino e a autenticação.

## Risks / Trade-offs

Os caminhos que a base de demonstração não exercita, desta change e das anteriores, estão consolidados no
**checklist do primeiro cliente** em `docs/STATUS.md`, com como se prova e qual o sintoma. Os riscos abaixo
são os desta change. Os que dependem de dado real ou do sandbox também estão no checklist.

- **[Blocos pelo schema podem não ser o que a plataforma espera]** O IPI, o II, o ICMS-ST e o ISS retido,
  a `TabB` e as bases isenta e em "outras" vêm do schema, e não de JSON aceito. → O que torna isso seguro:
  - se a plataforma recusar, o motivo aparece no dashboard (D10);
  - ajustar é trabalho só do mapper;
  - o primeiro envio ao sandbox (D18) decide.
- **[Origem sem tradução deixa a `TabA` ausente]** O JSON real manda a `TabA`, e ela pode ser exigida. Os
  valores do `Origin` no F&O ainda não foram gravados. → A gravação é da fatia 4. Valor sem tradução fica
  ausente, e a rejeição, se vier, mostra o motivo.
- **[Omitir campo inteiro pode virar 0 no destino]** O schema tem cara de .NET (`int32`), e um enum omitido
  pode ser lido como `0`, que é um código válido (`operacao`, `finalidadeNotaFiscal`, `tipoPagamento`). →
  Não há como mandar "ausente" melhor que ausente. A tabela do D3 lista cada campo, a foto do destino mostra
  a ausência, e a confirmação fica para a fatia do sandbox (D18).
- **[O mock aceita tudo]** "5 enviadas" contra o mock prova o caminho e o contrato montado, e não a
  aceitação da Avalara. → O RUNNING.md diz isso, e o envio ao sandbox é a próxima fatia (D18).
- **[Nota chega incompleta na plataforma]** Sem `ICMSDiff`, encargo, IS e retenções que não são de ISS, a
  escrituração do lado da Avalara fica incompleta. → A omissão fica visível no registro e no dashboard, e
  cada lugar entra com evidência. É a troca escolhida: a nota chega e a lacuna fica à vista, em vez de a nota
  parar.
- **[Heurística do motivo capta ruído]** Sem o formato real, a extração pode pegar texto demais ou de menos.
  → O caso sem formato reconhecido cai no corpo cru, e há corte de tamanho. O formato real, quando gravado,
  vira caso de teste.
- **[O `Reason` muda de sentido]** Ele deixa de ser só motivo de falha e passa a ser também observação de
  envio. → A tela separa por status (tom), e os KPIs olham status, não `Reason`. Quem lê o banco direto
  precisa saber disso, e o ADR-0026 registra.
- **[Mudança de versão do canônico em base grande]** Rebobinar ou fazer backfill logo depois de mudar a
  versão reenvia toda nota relida. → A regra e o procedimento estão no D17. Nesta fatia, o efeito é nulo.
- **[Configuração quebra o envio]** Sem `establishments`, todo envio do tenant é rejeitado. → O motivo diz
  exatamente o que falta, e o documento é reprocessável depois de corrigido. O seed e o RUNNING.md cobrem o
  dev. Em tenant real, a configuração precisa vir antes do deploy.
- **[Data de entrada/saída na saída]** `AccountingDate` é "data de entrada" pela d365/04. Na nota de saída
  ela é a data de lançamento, que normalmente é a de emissão. → Open Question para o fiscal. Se estiver
  errado, trocar a fonte é mudança só no D365.
- **[Totais de imposto ausentes]** Se a plataforma exigir `totais.icms` e afins, a nota será rejeitada. → O
  motivo aparece. O caminho é o F&O expor os totais, e não o hub somar.

## Migration Plan

1. **Banco:** sem migration.
2. **Configuração, antes do deploy:**
   - incluir `establishments` nas `OutboundSettings` de cada tenant, em cada ambiente usado;
   - em dev, o seed (banco novo) ou o passo do RUNNING.md (banco existente).
3. **D365:** depois do deploy, só as notas relidas reintegram, uma vez cada, pela impressão v2. Em base
   grande, não rebobinar a marca nem rodar backfill sem o hash de transição (D17).
4. **Mensagens em voo:** não mudam de formato, e nada a migrar.
5. **Rollback:** reverter o deploy.
   - O código antigo ignora `establishments` (só lê `baseUrl`) e ignora o `Omissions` do recibo.
   - Registros `Submitted`/`Confirmed` com "Enviado sem" no `Reason` ficam como texto inofensivo, que o
     código antigo mostra num banner de erro.
   - Notas rejeitadas pela plataforma ficam `IntegrationError`, reprocessáveis.
   - As impressões v2 gravadas fazem as notas relidas reintegrarem uma vez sob o código antigo, que volta a
     rejeitá-las na validação.

## Open Questions

Nada disto muda spec, abordagem ou tarefas. Cada resposta vira uma linha de tabela, uma fonte ou um caso de
teste. As que dependem do envio real são respondidas na fatia do sandbox (D18).

- **Formato real das respostas de erro da Avalara,** tanto a síncrona quanto a consulta com erro. A extração
  do D10 é tolerante, e o formato real vira teste.
- **Tabelas de código da Avalara:**
  - `finalidadeNotaFiscal`, `operacao`, `tipoPagamento`, `origemSistema`, `tipoItem`, `origemCredito`,
    `origemInformacao` e `tipoImposto` (retenção);
  - se um enum omitido é lido como `0`;
  - onde vão o diferencial de alíquota (`ICMSDiff`) e o Imposto Seletivo.
- **Valores dos enums do F&O a gravar:** `Purpose`, `PaymentMethod`, `ItemType` da linha contra
  `InventProductType` do item, e `CreditSourceCode`. A origem (`Origin`) é gravada na fatia 4.
- **`dataEntradaSaida` na saída:** é mesmo `AccountingDate`? (fiscal)
- **Formato de `periodoEscrituracao`:** o exemplo de mercadoria usa data e hora, e o de serviço usa `MM/yyyy`.
  Vale o de mercadoria até a plataforma dizer outra coisa.
- **Descrição da unidade:** qual idioma do `FSUnitOfMeasureBR` vale.
- **Livro fiscal do II:** qual base vale no livro, a `TaxBase` ou a `OtherBase`. Não afeta o payload (D9).
