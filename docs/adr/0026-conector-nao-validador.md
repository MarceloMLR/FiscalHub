# ADR-0026: Conector, não validador — o conteúdo fiscal é julgado pela plataforma

- **Status:** Aceito
- **Data:** 2026-09-27
- **Revisa:**
  - **ADR-0025 §6:** o item sem o grupo IBS/CBS, ou com o grupo sem classificação, deixa de ser rejeitado.
  - **ADR-0003:** refina a mensagem da rejeição. O texto da plataforma atravessa o adapter, e o status continua
    normalizado.
- **Revisado por:** [ADR-0027](0027-credencial-por-tenant-e-resposta-da-plataforma.md). No §2, o 403 e o 401 com token recém-emitido deixam de ser falha transitória, e o
  aceite sem identificador deixa de ser retentado.
- **Revisado por:** [ADR-0030](0030-grupo-pelo-estabelecimento-e-motivo-pela-foto.md). A omissão visível deixa de fazer
  parte do motivo de falha. Ela é ressalva de nota aceita e fica gravada na foto da resposta do envio.
- **Change OpenSpec:** `openspec/changes/connector-not-validator`
- **Validado no ambiente:** 2026-09-27, contra o `fiscosysdev` e o mock (ver o fim do documento).

## Contexto

O ADR-0025 deixou o hub julgando conteúdo fiscal. O validador rejeitava o item sem o grupo IBS/CBS, e o mapper
da Avalara lançava exceção se um desses chegasse. Com isso, as 5 NF-e 55 do fiscosysdev, que são de 2016 e não têm
IBS/CBS, paravam na nossa validação.

**O argumento contra.** Julgar conteúdo fiscal põe o hub como **segunda fonte de verdade fiscal**:

- teríamos que perseguir as regras da plataforma para sempre, e cada regra dela vira uma regra nossa, com
  atraso;
- **toda divergência entre o nosso julgamento e o dela vira nota que o cliente jura ter mandado e que não
  chegou.** A nota não chega, e o motivo é nosso, não da plataforma que o cliente contratou para julgar.

Quem aceita ou rejeita conteúdo fiscal é a plataforma de compliance. O papel do hub é **entregar** e **mostrar a
resposta dela**.

Na mesma revisão apareceu uma segunda força. O contrato da Avalara no adapter tinha sido modelado contra o mock, e
era um subconjunto do real:

- faltavam `codigoEmpresa` e `codigoContribuinte`;
- o IBS ia como total;
- o parceiro era o destinatário, que numa nota de entrada é o próprio estabelecimento.

Os dois JSONs reais de integração, um de mercadoria e um de serviço, passaram a definir o contrato.

## Decisão

**O hub rejeita só o que impede a requisição de existir. Todo o resto vai para a plataforma, e a resposta dela
aparece no registro do documento, com o motivo dela.**

### 1. A linha: o que o hub se permite julgar

| Onde | Regra | Por que é estrutural |
|---|---|---|
| Validador (Application) | nota sem item | sem item não há o que integrar, para destino nenhum |
| Adapter de saída | tenant sem a tradução do estabelecimento (§3) | não há valor honesto para `codigoEmpresa` e `codigoContribuinte` |
| Adapter de saída | campo inteiro do contrato que não vira número (CFOP, modelo, CST de bloco) | o contrato não consegue representar |
| Adapter de saída | dois tributos do mesmo bloco no mesmo item | o contrato leva um bloco por tributo; somar ou escolher seria julgamento |

**Saem do julgamento do hub:**

- **A chave de acesso com 44 dígitos.** O formato da chave e a obrigação de tê-la são regra fiscal do leiaute:
  UF, ano e mês, CNPJ, modelo, série, número, tipo de emissão, código e dígito verificador. Conferir isso é validar
  a nota, papel de quem a autoriza e de quem a escritura.
- **O CFOP com 4 dígitos.** É formato de conteúdo, e a tabela de CFOP é fiscal.
- **O NCM.**
- **O grupo IBS/CBS ausente.** Sai do validador, do mapper e do parser XML, onde a mesma regra também vivia. Um
  `IBSCBS` presente e incompleto continua sendo falha de leitura, porque é estrutura quebrada, e não conteúdo.
- **O CST e o `cClassTrib` vazios no grupo.**

**Por que a conferência do contrato fica no adapter.** O `IDocumentValidator<T>` é agnóstico de destino. O que é
exigência do contrato de um destino é conferido pelo adapter dele. Pôr essas regras no validador levaria o contrato
da Avalara para dentro da Application.

### 2. Rejeição com o motivo de quem rejeitou

- **Recusa permanente no envio.** O adapter lança a `DispatchRejectedException` (Application/Outbound), e a esteira
  registra a rejeição sem retentativa. É o mesmo desfecho da rejeição na validação, e não um esquema novo de retry.
  Há três casos:
  - **HTTP 400 ou 422 no envio:** o texto é "Plataforma de compliance recusou: …";
  - **configuração que falta:** "Configuração do conector: …";
  - **dado que o contrato não representa:** "Contrato do destino: …".
- **Recusa na consulta de status.** O "erro" leva a mensagem da resposta da plataforma. Sem mensagem, diz que a
  plataforma não informou a causa.
- **Falha transitória.** Continua como exceção, com retry nativo e dead-letter (ADR-0004).

  > **Revisado pelo ADR-0027 (2026-09-27).** Esta frase cobria o 401 e o 403 sem nomeá-los, porque o token ainda nem
  > estava ligado. Agora o 403, e o 401 com token recém-emitido, são impossibilidade do lado do conector: rejeição com o
  > motivo da plataforma, sem retentativa. O 401 com token do cache invalida o token e segue o retry nativo, e a
  > tentativa seguinte pede outro. O 2xx sem identificador reconhecível também vira rejeição, sem retentativa, porque o
  > documento pode ter sido aceito. O porquê: na dead-letter o motivo se perde (fica `MaxDeliveryCountExceeded`), e
  > retentar credencial pode bloquear a conta. A decisão explícita revertida é o D10 do design arquivado da
  > `connector-not-validator` ("tratar 401 e 403 como rejeição" estava nas alternativas descartadas). O 5xx, o 429 e
  > a rede continuam no retry nativo. O 404 do envio também virou rejeição de configuração (o caminho de envio não
  > existe na URL), e o da consulta de status continua pendente.
- **Refinamento do ADR-0003.** O status continua normalizado, e o nativo "erro" não sai do adapter. O que atravessa
  é a **mensagem humana** da plataforma. A frase agnóstica de antes escondia justamente o que o usuário precisa ver.
- **O formato real do erro da Avalara ainda não foi gravado.** A extração do motivo é tolerante: lista de mensagens,
  `ProblemDetails` ou, sem formato reconhecido, o corpo como veio.

### 3. Códigos da empresa: tradução por estabelecimento, por ambiente

`codigoEmpresa` e `codigoContribuinte` vêm das `OutboundSettings` do perfil do tenant, na seção do ambiente ativo,
pela tabela `establishments`. A chave é o CNPJ do estabelecimento próprio, e o valor é o par de códigos da
plataforma. **Nunca vêm do ERP.**

É um requisito permanente, e não um ajuste de demonstração: todo cliente tem código diferente entre o ERP e a
plataforma, e cliente com várias filiais precisa de uma tradução por filial.

A mesma tabela diz qual parte da nota é a nossa quando a origem não diz. O XML não diz; o D365 diz, pela emissão
própria ou de terceiros (`GoodsInvoice.Issuance`, o IND_EMIT do SPED). O parceiro é a outra parte.

### 4. O contrato vem dos JSONs reais, e a evidência ganha da instrução

- **Clássicos no bloco estruturado `imposto`.** ICMS, PIS e COFINS vão em `imposto` (ISSQN, no JSON de serviço).
- **Array `impostos` só para o grupo da Reforma:** `CBS`, `IBS ESTADUAL` e `IBS MUNICIPAL`, sem total do IBS.
- **A evidência ganhou da instrução anterior.** A primeira versão do desenho mandava os clássicos no array genérico,
  por instrução do usuário, que veio da leitura do contrato derivado do mock. Os dois JSONs reais mandam outra coisa.
  **Onde evidência real contradiz decisão nossa, vale a evidência.** É o princípio deste ADR, a plataforma como
  autoridade sobre o próprio contrato, aplicado ao nosso desenho.
- **Onde os JSONs mínimos são silenciosos,** vale o schema completo da Avalara (`docs/contracts`), com os nomes de
  campo dela:
  - IPI em `imposto.ipi`;
  - imposto de importação em `imposto.ii`, com a base tributável: 4.500 × 30% = 1.350. A base em "outras" não tem
    campo no bloco;
  - ICMS-ST em `imposto.icmsst`;
  - ISS retido nos campos `*ISSRetido` do `issqn`;
  - CST do ICMS na `TabB`, com a origem da mercadoria na `TabA`;
  - bases isenta e em "outras" do ICMS e do IPI, só quando não são zero.

  O desenho marca cada campo como "JSON real" ou "schema".

### 5. Nada é inventado; o que fica de fora é dito

- **Campo sem fonte não vai.** Nunca vira zero, nulo explícito ou texto de preenchimento. Isso vale para
  `finalidadeNotaFiscal`, `operacao`, `tipoPagamento`, `origemSistema`, `tipoItem`, `origemCredito`,
  `embasamentoLegal`, `origemInformacao`, `valorTributoLiquido`, `parceiro.codigo`, `parceiro.ativo` e os totais
  de imposto. O hub não soma valor fiscal.
- **O que o documento tem e o contrato não leva é omissão visível.** São eles:
  - tributo sem lugar no contrato (diferencial de alíquota, IRRF, INSS e CSLL não retidos, "outros");
  - retenção que não é de ISS. O lugar dela é `impostosRetidos`, cujo `tipoImposto` não tem tabela confirmada;
  - encargo;
  - Imposto Seletivo.

  A omissão sai no recibo do envio e fica no registro do documento ("Enviado sem: …"). A confirmação da plataforma a
  preserva, e numa rejeição posterior ela vem depois do motivo da plataforma. O dashboard a mostra como **aviso**,
  e não como falha.

  > **Revisado pelo ADR-0030 (2026-09-28).** A omissão sai do motivo de falha: a recusa do envio, a recusa da consulta
  > e o "sem retorno" trazem só o motivo da falha. Ela fica gravada na foto da resposta do envio (`request.omissions`),
  > em todo envio que recebeu resposta. Na nota aceita, continua no registro ("Enviado sem: …"), e o dashboard a mostra
  > como a marca "Enviado com ressalvas", que abre a lista. O porquê: o motivo de falha virou resumo, e a ressalva
  > misturada a ele fazia a omissão do hub parecer erro da plataforma.
- **Retenção nunca entra no array `impostos`:** seria lida como imposto do item e somada a ele.

### 6. Versão da impressão do D365: regra para base grande

Ler campos novos no D365 muda o canônico, que passou à versão 2, e com ele a impressão. **A reintegração só acontece
quando a nota é relida:**

- por toque no ERP;
- por rebobinamento da marca ou backfill;
- por reprocesso.

Em 14 notas, isso é irrelevante. Em cem mil, um rebobinamento logo depois da mudança vira uma enxurrada de
reenvios. **Regra:** mudança de versão com tenant em produção exige antes o **hash de transição**. O source calcula
também a impressão da versão anterior, e a idempotência a aceita para a nota já integrada, regravando a impressão
nova sem reenviar. Sem o hash de transição, não se rebobina. Reenvio desejado é reprocesso deliberado, em lotes.

### 7. `cClassTrib` fora do caminho crítico

Como nenhum grupo IBS/CBS é exigido pelo hub, a entidade sobre `CClassTribTable_BR` deixa de ser pré-requisito da
demonstração. Ela volta a importar quando houver nota com IBS/CBS e a plataforma exigir a classificação.

## Alternativas consideradas

- **Manter as regras fiscais no validador, "por higiene".** Cada regra é um julgamento que pode divergir do da
  plataforma, e é exatamente essa divergência que faz a nota não chegar.
- **Clássicos no array genérico** (a instrução anterior). É uniforme, mas os dois JSONs reais o contradizem.
- **Um par de códigos por ambiente.** É mais simples, mas manda a nota de toda filial para o mesmo contribuinte e
  não responde qual parte da nota é a nossa.
- **Tributo sem lugar mandado num lugar aproximado** (o diferencial de alíquota no `difal` da EC 87, o IS com código
  deduzido). Seria decidir o que a plataforma deve entender, e uma plataforma que aceitasse escrituraria o tributo
  errado.
- **Rejeitar a nota que tem dado sem lugar no contrato.** Trataria como estrutural algo que não impede a requisição
  de existir.
- **Devolver um recibo com status de erro, em vez da exceção tipada.** Obrigaria o `ExternalId` a ficar opcional.
  A exceção repete o desenho da `DocumentOutOfScopeException`.

## Consequências

**Melhora**

- **Nota com conteúdo fiscal discutível chega à plataforma,** e quem responde é ela, com o motivo à vista no
  dashboard.
- **O contrato é o real:** códigos da empresa, parceiro certo, blocos por tributo e IBS/CBS em estadual e municipal.
- **Tudo o que não foi é dito.** A omissão aparece no registro, e não em silêncio.
- **O julgamento do hub cabe numa tabela de quatro linhas.**

**Piora**

- **A nota pode chegar incompleta** (diferencial de alíquota, encargo, retenções que não são de ISS, IS). Fica à vista
  como omissão, e cada lugar entra com evidência.
- **Os blocos tirados do schema podem não ser o que a plataforma espera.** A recusa, se vier, aparece com o motivo.
- **Campo omitido pode ser lido como `0` pela plataforma,** se a API tratar enum ausente como zero. Isso só se
  confirma no envio real.
- **O `Reason` do registro deixou de ser só motivo de falha,** e passou a ser também observação de envio. A tela e os
  KPIs separam pelo status.
- **Configuração obrigatória:** sem `establishments`, todo envio do tenant é rejeitado, com motivo claro.

**Pendências** (próximas fatias; o registro de risco está no checklist do primeiro cliente, em `docs/STATUS.md`)

- **Ligação com o sandbox real da Avalara:** credenciais e token por tenant, `BaseUrl`, teste manual contra o
  sandbox e gravação das respostas reais. É a próxima fatia.
- **Tabelas de código da Avalara**, e os lugares do diferencial de alíquota, do IS e das retenções.
- **Hash de transição** antes da primeira mudança de versão do canônico com tenant em produção.

## Validado no ambiente

2026-09-27, pelo roteiro de `docs/RUNNING.md` §7: host, emulador do Service Bus, mock de compliance e poll do
tenant-a a partir de 2015, contra o `fiscosysdev`.

- **Fixtures regravadas** com o `$select` ampliado. Mudaram 23 arquivos, só nos campos novos, sem desvio de dado.
  O teste opt-in montou as 5 NF-e 55 duas vezes cada, com a mesma impressão (canônico v2).
- **Primeira passada:** 14 referências na fila de descoberta e 0 suprimidas.
- **Passada normal:** 5 NF-e 55 `Confirmed` e 9 NFS-e `Ignored`.
  - `BRMF06-110000027` com "Enviado sem: item 1: IcmsDiff não enviado (sem lugar no contrato)";
  - `BRMF06-110000031` com "Enviado sem" do encargo de 416,25.

  As duas observações sobreviveram à confirmação.
- **Payload no mock:**
  - `BRMF06-110000027`:
    - vai sem `chaveNFe`;
    - o parceiro é a Proseware, o fornecedor, e não o próprio estabelecimento;
    - o ICMS vai com `TabA 0` e `TabB 90`, base 0 e `valorBaseOutrosICMS` 1.000.
  - `BRMF06-110000031`:
    - o parceiro é estrangeiro, sem CNPJ;
    - o II vai em `imposto.ii` (4.500 / 30 / 1.350);
    - a `TabA` do ICMS é `1` (`DirectImport`).
  - Em nenhuma nota há array `impostos` nem `null`.
- **Mock em `erro`:** as 5 ficaram `IntegrationError` com "Plataforma de compliance rejeitou: <motivo do mock>". Nas
  duas notas com omissão, a observação veio depois do ` | `.
- **Mock em `rejeitar`:** as 5 ficaram `IntegrationError` com "Plataforma de compliance recusou: <motivo>", sem
  `ExternalId` e sem dead-letter.
- **Não exercitado:** o sandbox real da Avalara, que é a próxima fatia. O mock aceita qualquer payload.
