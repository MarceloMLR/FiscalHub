## Purpose

Montar o documento enviado à Avalara a partir do contrato real (o JSON mínimo de mercadoria, e o schema
completo onde ele é silencioso), traduzindo o domínio sem inventar valor. O que o documento tem vai no
payload. O que ele não tem fica fora. O que ele tem e o contrato não leva é declarado como omissão.

## ADDED Requirements

### Requirement: Códigos da empresa por estabelecimento e ambiente

`codigoEmpresa` e `codigoContribuinte` MUST vir das settings de saída do perfil do tenant:

- **Onde:** na tabela do ambiente ativo, na entrada cuja chave é o CNPJ do estabelecimento próprio do
  documento.
- **O que não vale:** eles MUST NOT vir do ERP, mesmo quando o documento traz um valor parecido, como o
  próprio CNPJ do estabelecimento.
- **Comparação da chave:** é feita só pelos dígitos do CNPJ.

Se faltar o perfil, a seção do ambiente, a entrada do estabelecimento ou qualquer um dos dois campos, o
envio MUST ser rejeitado antes de qualquer requisição à plataforma. O motivo MUST citar o tenant, o
ambiente, o CNPJ e cada campo que falta.

#### Scenario: Os códigos saem da configuração, e não do ERP
- **WHEN** o estabelecimento próprio da nota tem CNPJ `44278225000180`, e a tabela do ambiente ativo
  traduz esse CNPJ para `codigoEmpresa = 20247332000182` e `codigoContribuinte = 20247332000182`
- **THEN** o payload leva `codigoEmpresa` e `codigoContribuinte` iguais a `20247332000182`
- **AND** nenhum dos dois é `44278225000180`

#### Scenario: O ambiente ativo escolhe a tabela
- **WHEN** o perfil está em `Production`, e as tabelas de `sandbox` e `production` traduzem o mesmo CNPJ
  para códigos diferentes
- **THEN** o payload leva os códigos da tabela de `production`

#### Scenario: Estabelecimento sem tradução
- **WHEN** o CNPJ do estabelecimento próprio não está na tabela do ambiente ativo
- **THEN** o envio é rejeitado com motivo que cita o tenant, o ambiente e o CNPJ
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Um dos dois códigos falta
- **WHEN** a entrada do estabelecimento tem `codigoEmpresa` e não tem `codigoContribuinte`
- **THEN** o envio é rejeitado com motivo que nomeia `codigoContribuinte`

### Requirement: Estabelecimento próprio e parceiro

O estabelecimento próprio do documento MUST ser determinado assim:

- **A nota diz se é de emissão própria ou de terceiros:** o estabelecimento próprio é o emitente na
  emissão própria, e o destinatário na emissão de terceiros.
- **A nota não diz:** o estabelecimento próprio é a única parte cujo CNPJ está na tabela do ambiente
  ativo. Se nenhuma parte ou as duas estiverem na tabela, o envio MUST ser rejeitado antes de qualquer
  requisição à plataforma, com motivo que cita os CNPJs das duas partes.

O `parceiro` do payload MUST ser a outra parte. Ele leva:

- **`nome`;**
- **documento:** o CNPJ em `cnpj` quando tem 14 dígitos, o CPF em `cpf` quando tem 11, e nenhum dos dois
  em qualquer outro caso;
- **endereço:** `endereco`, `numero`, `bairro` e `cep`, só os que o documento tiver. O `cep` vai só com
  dígitos.

#### Scenario: Nota de entrada de terceiro
- **WHEN** a nota é de emissão de terceiros, com o fornecedor Proseware como emitente e o estabelecimento
  Contoso como destinatário
- **THEN** o parceiro do payload é a Proseware

#### Scenario: Nota de saída própria
- **WHEN** a nota é de emissão própria, com o estabelecimento como emitente e o cliente como destinatário
- **THEN** o parceiro do payload é o cliente

#### Scenario: A nota não diz qual lado é o nosso
- **WHEN** uma NF-e em XML chega sem a indicação de emissão própria ou de terceiros, e só o CNPJ do
  emitente está na tabela do ambiente ativo
- **THEN** o emitente é o estabelecimento próprio e o destinatário é o parceiro

#### Scenario: As duas partes estão na tabela e a nota não diz qual é a nossa
- **WHEN** a nota não traz a indicação e os CNPJs do emitente e do destinatário estão ambos na tabela
- **THEN** o envio é rejeitado com motivo que cita os dois CNPJs

#### Scenario: Parceiro estrangeiro sem documento brasileiro
- **WHEN** a contraparte da nota de importação não tem CNPJ nem CPF
- **THEN** o parceiro vai sem `cnpj` e sem `cpf`, com o nome e o endereço que houver

### Requirement: Cabeçalho do documento

O payload MUST levar, do documento:

- **Identificação:** `serie`; `modelo` como número; `numeroDocumento` como veio, sem tirar zeros à
  esquerda; `chaveNFe`, só quando a chave não é vazia.
- **Datas:**
  - `dataEmissao`;
  - `dataEntradaSaida`, quando o documento a tiver;
  - `periodoEscrituracao`, quando houver data de entrada/saída: é o primeiro dia do mês dessa data, à
    meia-noite UTC.
- **`situacao = 0` (documento regular).** Só nota autorizada é despachada, e sempre como emitida.
- **`codigoReferenciaIntegracao`:** a chave natural do documento no hub.
- **Totais:** `totais.valorDocumento`, pelo valor total, e `totais.valorMercadorias`, quando o documento
  tiver o valor das mercadorias.

#### Scenario: Chave de acesso vazia
- **WHEN** a nota chega com a chave de acesso vazia
- **THEN** o payload não tem `chaveNFe`

#### Scenario: Período de escrituração pela data de entrada/saída
- **WHEN** a data de entrada/saída da nota é 2016-09-02
- **THEN** `periodoEscrituracao` é `2016-09-01T00:00:00Z`

#### Scenario: Referência da integração
- **WHEN** a nota de chave natural `brmf|BRMF06-110000027` é enviada
- **THEN** `codigoReferenciaIntegracao` é `brmf|BRMF06-110000027`

#### Scenario: Número com zeros à esquerda
- **WHEN** o número da nota é `000001`
- **THEN** `numeroDocumento` é `000001`

### Requirement: Itens do documento

Cada item do documento MUST virar uma entrada em `itens`, com:

- **Identificação:** `numeroSequencia`, `item.codigo` e `item.descricao`.
- **Unidade:** `item.unidadeMedida.codigo` e `unidadeMedida.codigo`, pela unidade do item, quando houver.
- **Operação:** `cfop` como número, `quantidade` e `valorTotal`.
- **`valorContabil`,** quando o item tiver valor contábil.

Um CFOP que não pode ser representado como número, inclusive vazio, MUST rejeitar o envio antes de
qualquer requisição à plataforma, com motivo que cita o item, o valor e a exigência do contrato. O mesmo
vale para um modelo que não é número. O número de dígitos do CFOP não é conferido.

#### Scenario: CFOP numérico
- **WHEN** o item tem CFOP `5102`
- **THEN** a entrada do item tem `cfop = 5102`

#### Scenario: CFOP vazio
- **WHEN** o item 2 chega com CFOP vazio
- **THEN** o envio é rejeitado com motivo que cita o item 2 e o CFOP exigido como número pelo contrato
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: CFOP numérico fora do formato
- **WHEN** o item tem CFOP `12345`
- **THEN** a entrada do item tem `cfop = 12345`, e o julgamento fica com a plataforma

### Requirement: Tributos clássicos no bloco estruturado do item

Os tributos do item que não pertencem ao grupo da Reforma MUST ir no bloco `imposto` do item, como mandam
os JSONs reais da Avalara, e MUST NOT ir no array `impostos`. Ficam de fora os tributos que estão num
encargo.

Cada tipo vai num bloco:

| Tipo | Bloco | Campos |
|---|---|---|
| ICMS | `icms` | `situacaoTributariaICMSTabA` (origem da mercadoria), `situacaoTributariaICMSTabB` (CST), `valorBaseICMS`, `aliquotaICMS`, `valorICMS`, `valorBaseIsentoICMS`, `valorBaseOutrosICMS` |
| IPI | `ipi` | `situacaoTributariaIPI` (CST), `baseCalculoIPI`, `aliquotaIPI`, `valorIPI`, `valorBaseIsentoIPI`, `valorBaseOutrosIPI` |
| PIS | `pis` | `situacaoTributariaPIS` (CST), `baseCalculoPIS`, `aliquotaPIS`, `valorPIS` |
| COFINS | `cofins` | `situacaoTributariaCOFINS` (CST), `baseCalculoCOFINS`, `aliquotaCOFINS`, `valorCOFINS` |
| Imposto de importação | `ii` | `baseCalculoII`, `aliquotaII`, `valorII` |
| ICMS-ST | `icmsst` | `valorBaseICMSST`, `aliquotaICMSST`, `valorICMSST` |
| ISS | `issqn` | `baseCalculoISSQN`, `aliquotaISSQN`, `valorISSQN`; se retido, `valorBaseISSRetido`, `aliquotaISSRetido`, `valorISSRetido` |

Regras:

- **Base de cálculo:** é a base tributável do tributo.
- **CST:** vai como número, sem zeros à esquerda (`01` vira 1). CST vazio deixa o campo ausente.
- **Origem da mercadoria:** vai só quando o item a tiver.
- **Base isenta e base em "outras":** vão só nos campos próprios do ICMS e do IPI, e só quando não são
  zero. Os outros blocos não levam essas parcelas.
- **Não pode ser representado,** e MUST rejeitar o envio antes de qualquer requisição à plataforma, com
  motivo que cita o item e o tipo:
  - CST que não é número;
  - dois tributos do mesmo bloco no mesmo item.

#### Scenario: Tributos clássicos com CST, base e valor
- **WHEN** o item 1 da nota `brmf|BRMF21-10000026` tem ICMS CST `00` com base 3.500,00, alíquota 12 e valor
  420,00, e PIS CST `01` com base 3.500,00, alíquota 1,65 e valor 57,75
- **THEN** o bloco `imposto.icms` do item leva `situacaoTributariaICMSTabB = 0`, base 3.500,00, alíquota 12
  e valor 420,00
- **AND** o bloco `imposto.pis` leva `situacaoTributariaPIS = 1`, base 3.500,00, alíquota 1,65 e valor 57,75
- **AND** o array `impostos` do item não tem entrada para ICMS nem para PIS

#### Scenario: Origem da mercadoria na Tabela A
- **WHEN** o item tem origem `0` e ICMS
- **THEN** o bloco `imposto.icms` leva `situacaoTributariaICMSTabA = 0`

#### Scenario: Base só em "outras"
- **WHEN** o item tem ICMS CST `90` com base tributável 0, base em "outras" 1.000,00, alíquota 12 e valor 0
- **THEN** o bloco `imposto.icms` leva base 0, alíquota 12, valor 0 e `valorBaseOutrosICMS` 1.000,00
- **AND** não leva `valorBaseIsentoICMS`

#### Scenario: Imposto de importação com base tributável
- **WHEN** o item 1 da nota de importação `brmf|BRMF06-110000031` tem imposto de importação com base
  tributável 4.500,00, base em "outras" 4.500,00, alíquota 30 e valor 1.350,00
- **THEN** o bloco `imposto.ii` do item leva `baseCalculoII` 4.500,00, `aliquotaII` 30 e `valorII` 1.350,00
- **AND** a base em "outras" não vai, porque o bloco não tem campo para ela

#### Scenario: CST que não é número
- **WHEN** o item 1 tem PIS com CST `XX`
- **THEN** o envio é rejeitado com motivo de contrato que cita o item 1 e o PIS
- **AND** nenhuma requisição é feita à plataforma

#### Scenario: Dois tributos do mesmo bloco no item
- **WHEN** o item 1 tem duas linhas de ICMS
- **THEN** o envio é rejeitado com motivo de contrato que cita o item 1 e o ICMS repetido

### Requirement: Grupo da Reforma no array genérico

O grupo IBS/CBS do item MUST ir no array `impostos`, em três entradas: `CBS`, `IBS ESTADUAL` e
`IBS MUNICIPAL`. Cada entrada leva:

- `imposto.codigo`;
- `situacaoTributariaImposto`, com o mesmo `imposto.codigo` e com o CST do grupo como `codigo`, omitido
  quando vazio;
- `classificacaoTributariaImposto`, com a classificação do grupo, omitida quando vazia;
- `valorBaseTributo`, com a base do grupo;
- `aliquotaTributo` e `valorTributoBruto`, com a alíquota e o valor da parcela.

Não há entrada de total do IBS. O item sem o grupo IBS/CBS MUST ir sem entradas no array, e o mapeamento
MUST NOT falhar.

#### Scenario: Grupo da Reforma em três entradas
- **WHEN** o item tem o grupo IBS/CBS com CST `000`, `cClassTrib` `000001`, base 2.324,95, CBS 0,9% de
  20,92, IBS estadual 0,1% de 2,32 e IBS municipal 0% de 0,00
- **THEN** o array tem as entradas `CBS`, `IBS ESTADUAL` e `IBS MUNICIPAL`, cada uma com CST `000`,
  classificação `000001`, base 2.324,95 e a alíquota e o valor da própria parcela
- **AND** não há entrada `IBS` com o total

#### Scenario: Item sem o grupo da Reforma
- **WHEN** o item não tem o grupo IBS/CBS e tem ICMS, PIS e COFINS
- **THEN** o mapeamento não falha
- **AND** o item leva os blocos `icms`, `pis` e `cofins` e nenhuma entrada no array `impostos`

### Requirement: Campo ausente fica fora do payload

Um valor que o documento não tem MUST NOT ser escrito no payload. Ele não vira zero, nem nulo explícito,
nem texto de preenchimento.

Os campos do contrato que não têm fonte nesta fatia MUST NOT aparecer no payload:

- `finalidadeNotaFiscal`, `operacao`, `tipoPagamento` e `origemSistema`;
- `item.tipoItem`, `item.unidadeMedida.descricao`, `unidadeMedida.descricao` e `origemCredito`;
- `embasamentoLegal`, `origemInformacao` e `valorTributoLiquido`;
- `parceiro.codigo` e `parceiro.ativo`;
- `totais.valorTotalComIBSCBSeIS` e os totais de imposto em `totais`.

#### Scenario: Nota sem data de entrada/saída
- **WHEN** a nota chega sem data de entrada/saída
- **THEN** o payload não tem `dataEntradaSaida` nem `periodoEscrituracao`

#### Scenario: Item sem origem
- **WHEN** o item tem ICMS e não tem origem da mercadoria
- **THEN** o bloco `imposto.icms` não tem `situacaoTributariaICMSTabA`

#### Scenario: Campos sem fonte não aparecem
- **WHEN** qualquer nota é mapeada
- **THEN** o payload não tem nenhum dos campos sem fonte listados neste requisito

### Requirement: O que o documento tem e o contrato não leva

Estes dados do documento MUST NOT ir no payload, e cada um MUST ser declarado como uma omissão que
acompanha o resultado do envio:

- **Tributo sem lugar no contrato:** diferencial de alíquota do ICMS, IRRF, INSS, INSS sobre a receita
  bruta e CSLL não retidos, e tributo classificado como "outros".
- **Retenção que não é de ISS.** MUST NOT entrar no array `impostos` nem em bloco de tributo. O lugar dela
  no contrato é `impostosRetidos`, e nesta fatia ela não é enviada.
- **Encargo do item e os tributos dele.**
- **Imposto Seletivo.**

A omissão cita o item, o tipo e, no caso do encargo, o valor.

#### Scenario: Diferencial de alíquota
- **WHEN** o item 1 da nota `brmf|BRMF06-110000027` tem diferencial de alíquota do ICMS
- **THEN** o payload não leva esse tributo, nem no bloco `icms` nem no array `impostos`
- **AND** o resultado do envio declara a omissão do diferencial de alíquota do item 1

#### Scenario: Retenção de IRRF
- **WHEN** o item tem uma retenção de IRRF
- **THEN** o payload não leva a retenção em lugar nenhum
- **AND** o resultado do envio declara a omissão da retenção de IRRF do item

#### Scenario: Retenção de ISS vai no bloco próprio
- **WHEN** o item tem ISS retido com base 100,00, alíquota 5 e valor 5,00
- **THEN** o bloco `imposto.issqn` leva `valorBaseISSRetido` 100,00, `aliquotaISSRetido` 5 e
  `valorISSRetido` 5,00
- **AND** o resultado do envio não declara omissão por ela

#### Scenario: Encargo do item
- **WHEN** o item 1 tem um encargo "outras despesas" de 416,25
- **THEN** o payload não leva o encargo
- **AND** o resultado do envio declara a omissão do encargo do item 1, com o valor 416,25

#### Scenario: Nada omitido
- **WHEN** todos os tributos da nota têm lugar no contrato, e ela não tem retenção fora do ISS, encargo nem
  Imposto Seletivo
- **THEN** o resultado do envio não declara omissão
