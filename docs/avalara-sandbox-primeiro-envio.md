# Primeiro envio ao sandbox da plataforma de compliance

Relatório do teste manual da change `connect-avalara-sandbox` (ADR-0027, design D13). O primeiro envio real voltou
recusado, como o design previa, e por conteúdo: nem a autenticação, nem o caminho, nem a configuração do tenant.

## Ambiente

| | |
|---|---|
| Data | 2026-09-27 (o envio das 5 NF-e às 19:23, horário de Brasília) |
| Plataforma | sandbox da Avalara Brasil, host `api-gateway.sandbox.avalarabrasil.com.br` |
| Hub | host local em Development, compilado do commit `98426f5` (com a inversão do padrão do `Seed:DemoData`, que só mexe no seed, ainda fora de commit; entrou em `d8e9d4f`) |
| Tenant | `tenant-a`, ambiente `sandbox` |
| Origem | Dynamics 365 F&O `fiscosysdev`, empresa `brmf`, pelo feed de mudanças |
| Credencial | a do tenant, digitada na tela e lida do cofre; nenhuma credencial neste documento |

## A verificação da premissa (tarefa 15.3)

| Item | Esperado pelo desenho | Observado |
|---|---|---|
| Autenticação | `client_credentials`, segredo no corpo | `client_credentials` com corpo JSON (`grant_type`, `client_id`, `client_secret`, `disableTokenRefresh: true`), a forma da coleção do cliente |
| Endpoint de token | a URL base + `oauth/token` | `/oauth/token`, o `TokenPath` padrão |
| Resposta de token | `access_token`, `token_type`, `expires_in` | os três, com `token_type` `bearer` e `expires_in` de cerca de 86400 s; mais `refresh_token`, `sessionId`, `userId`, `subId`, `appId`, `login`, `email`, `type`, `create_in` e `expires_in_session` |
| Escopo ou audiência | nenhum | nenhum, nem exigido nem devolvido |
| Recusa de credencial | 400/401 com o código do OAuth | HTTP 400 com `{"error": "<texto livre>"}`, sem `error_description`; um `client_secret` errado volta como "client_id invalid" |
| Caminho de envio | existe (status diferente de 404) | existe: `taxcompliance/v2/fiscal/dfe`, montado pela `baseUrl` do perfil mais o `Avalara:DocumentsPath` |
| Consulta de status | a resposta de status | **não exercitada**: nenhuma nota aceita, nenhum `id` |

A premissa bateu no fluxo e divergiu no formato do corpo (JSON, e não formulário). Pela regra do D13, isso foi ajuste de
forma no provider e no mock, com teste.

## O ponta a ponta

- **Descoberta:** as 14 referências da `brmf` saíram do feed de mudanças.
- **Roteamento:** as 9 NFS-e viraram "ignorado: tipo fora do escopo (ServiceNfse)", sem nenhuma chamada ao F&O.
- **Montagem e envio:** as 5 NF-e 55 foram montadas e enviadas, cada uma com um único POST e zero retentativas,
  com os códigos da empresa traduzidos pelos `establishments` do perfil. A plataforma recusou as cinco com HTTP 400.

### Uma linha por nota

| Nota | HTTP do envio | Status final | Itens com recusa | Omissões declaradas pelo hub | Redações na foto |
|---|---|---|---|---|---|
| `brmf\|BRMF06-110000027` | 400 | `IntegrationError` | 1 | item 1: IcmsDiff não enviado (sem lugar no contrato) | 0 |
| `brmf\|BRMF06-110000031` | 400 | `IntegrationError` | 1 | item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo) | 0 |
| `brmf\|BRMF12-30000001` | 400 | `IntegrationError` | 1 | — | 0 |
| `brmf\|BRMF21-10000026` | 400 | `IntegrationError` | 3 (motivo cortado em 1000 caracteres; a lista inteira está na foto) | — | 0 |
| `brmf\|BRMF21-10000027` | 400 | `IntegrationError` | 2 | — | 0 |

As NFS-e ignoradas: `brmf|BRMF06-110000025`, `brmf|BRMF06-110000026`, `brmf|BRMF06-110000032`, `brmf|BRMF06-110000034`, `brmf|BRMF06-110000035`, `brmf|BRMF21-10000006`, `brmf|BRMF21-10000010`, `brmf|BRMF21-10000019`, `brmf|BRMF21-10000025`.

### Os motivos literais

Como gravados no registro do documento (o texto que o dashboard mostra), já redigidos. A foto de cada resposta está no
zip da nota, e a da `BRMF12-30000001` é a fixture `tests/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara.Tests/Fixtures/sandbox/recusa-no-envio.json`.

**`brmf|BRMF06-110000027`**

```text
Plataforma de compliance recusou: operacao: 'Operacao' não pode ser nulo.; tipoPagamento: 'Tipo Pagamento' não pode ser nulo.; parceiro.Codigo: 'Codigo' não pode ser nulo.; parceiro.Codigo: 'Codigo' deve ser informado.; itens[0].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' deve ser informado.; One or more validation errors occurred. | Enviado sem: item 1: IcmsDiff não enviado (sem lugar no contrato)
```

**`brmf|BRMF06-110000031`**

```text
Plataforma de compliance recusou: operacao: 'Operacao' não pode ser nulo.; tipoPagamento: 'Tipo Pagamento' não pode ser nulo.; parceiro.Codigo: 'Codigo' não pode ser nulo.; parceiro.Codigo: 'Codigo' deve ser informado.; itens[0].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' deve ser informado.; One or more validation errors occurred. | Enviado sem: item 1: encargo Other de 416,25 não enviado (o contrato mínimo não tem campo de encargo)
```

**`brmf|BRMF12-30000001`**

```text
Plataforma de compliance recusou: operacao: 'Operacao' não pode ser nulo.; tipoPagamento: 'Tipo Pagamento' não pode ser nulo.; parceiro.Codigo: 'Codigo' não pode ser nulo.; parceiro.Codigo: 'Codigo' deve ser informado.; itens[0].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' deve ser informado.; One or more validation errors occurred.
```

**`brmf|BRMF21-10000026`**

```text
Plataforma de compliance recusou: operacao: 'Operacao' não pode ser nulo.; tipoPagamento: 'Tipo Pagamento' não pode ser nulo.; parceiro.Codigo: 'Codigo' não pode ser nulo.; parceiro.Codigo: 'Codigo' deve ser informado.; itens[0].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[1].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[1].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[1].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[2].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[2].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[2].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[1].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[1].Item.UnidadeMedida.Desc…
```

**`brmf|BRMF21-10000027`**

```text
Plataforma de compliance recusou: operacao: 'Operacao' não pode ser nulo.; tipoPagamento: 'Tipo Pagamento' não pode ser nulo.; parceiro.Codigo: 'Codigo' não pode ser nulo.; parceiro.Codigo: 'Codigo' deve ser informado.; itens[0].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[1].Item.TipoItem: 'Tipo Item' não pode ser nulo.; itens[1].UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[1].UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[0].Item.UnidadeMedida.Descricao: 'Descricao' deve ser informado.; itens[1].Item.UnidadeMedida.Descricao: 'Descricao' não pode ser nulo.; itens[1].Item.UnidadeMedida.Descricao: 'Descricao' deve ser informado.; One or more validation errors occurred.
```

## A classificação dos motivos

Pela regra do D13, fixada antes de ler. As cinco notas têm os mesmos seis motivos, repetidos por item:

| Motivo (caminho da plataforma) | O que o hub mandou | Classe | Por quê | Pergunta à Avalara |
|---|---|---|---|---|
| `operacao` | ausente | nosso: contrato ou mapeamento | o domínio tem a emissão (`issuance`) e a origem tem a `Direction`; falta mapear para o código da plataforma | quais códigos de `operacao` e a regra para entrada e saída |
| `tipoPagamento` | ausente | nosso: contrato ou mapeamento | o domínio não tem pagamento; exige ler da origem (o grupo `pag` do XML; no D365, a entidade que o traga) | quais códigos e se há padrão para nota sem pagamento |
| `parceiro.Codigo` | ausente (vão `nome`, `cnpj`, `endereco`, `numero`, `bairro`, `cep`) | nosso: contrato ou mapeamento | o domínio da parte não tem código | se é o código do parceiro no ERP ou um cadastro da plataforma |
| `itens[].Item.TipoItem` | ausente (vão `codigo`, `descricao`, `unidadeMedida`) | nosso: contrato ou mapeamento | o domínio do item não tem tipo; exige o cadastro de produto da origem | a tabela de tipos de item aceita |
| `itens[].UnidadeMedida.Descricao` | `unidadeMedida: { codigo: "pcs" }`, sem descrição | nosso: contrato ou mapeamento | o domínio tem a unidade (`unit`); outro payload, do mesmo domínio, resolve | se a descrição pode repetir o código |
| `itens[].Item.UnidadeMedida.Descricao` | idem, no bloco `item` | nosso: contrato ou mapeamento | idem | idem |

Nenhum motivo é **nosso: configuração** (os códigos da empresa, a credencial e o caminho passaram), nenhum é
**característica do dado** (a recusa não citou a data de 2016, a falta de IBS/CBS, a chave de acesso nem o participante
estrangeiro) e nenhum ficou **indeterminado**. O que resta indeterminado é o valor que cada campo deve levar, e isso é
pergunta à Avalara, e não palpite. Os seis são a entrada da próxima fatia (a correção do payload).

## O experimento do campo omitido

**Não rodou.** O `finalidadeNotaFiscal` não apareceu na recusa, e o experimento (as variantes omitido, `1` e `0`) fica mais
informativo depois que uma nota for aceita, quando a leitura de volta e a consulta mostram o que a plataforma gravou.
Foi movido para a próxima fatia.

## As perguntas do CNV D18

Só o que a evidência sustenta:

- **Formato do erro: respondido.** ProblemDetails (RFC 9110): `type`, `title` ("One or more validation errors occurred."),
  `status`, `traceId` e `errors`, um mapa do caminho do campo para a lista de mensagens, em português. Os caminhos usam o
  nome das propriedades do contrato da plataforma (`itens[0].Item.UnidadeMedida.Descricao`), e não o do nosso JSON em
  camelCase. A `PlatformMessage` tira dele um texto legível ("campo: mensagem; …; title"), provado por teste sobre a
  resposta gravada. A foto capturou só o cabeçalho `Date` da lista fechada: a resposta do endpoint de envio do sandbox não
  traz `Content-Type`. Não é defeito da foto: o envelope lê os cabeçalhos da resposta e os de conteúdo
  (`response.Content.Headers`), e o mesmo código capturou o `Content-Type` na resposta de token do mesmo sandbox e nas do
  mock. As duas respostas do endpoint de envio (pelo hub e pela sonda) vieram sem ele.
- **Um campo omitido vira `0`?** Respondido para dois: `operacao` e `tipoPagamento` omitidos voltam como "não pode ser nulo",
  isto é, falham alto. **Não respondido** para o `finalidadeNotaFiscal`, que não apareceu na recusa (o experimento acima).
- **Os blocos pelo schema são aceitos?** **Não respondido.** A validação parou nos campos obrigatórios, e nenhum bloco
  (IPI, II, ICMS-ST) foi citado, o que não prova que sejam aceitos.
- **Os totais são exigidos?** **Não respondido**, pelo mesmo motivo.
- **Reenvio: atualiza ou duplica?** **Não respondido**: nenhuma nota foi aceita.

## Não exercitado

- **O aceite** e a leitura do `id`, **a consulta de status** (o caminho `{DocumentsPath}/{id}/status`, convenção do mock)
  e **a recusa de credencial em produção**. Não viraram fixture: não se fabrica resposta.
- **Os logs do host** (tarefa 16.4): o host rodou numa console interativa, sem arquivo de log, e a saída não pôde ser
  varrida.
- **As abas "Resposta" e "Destino"** na tela (16.2): não conferidas. O motivo legível das 5 notas foi conferido no registro.
- **Os zips e as cinco fotos** (16.3), a correção pela tela com segredo errado (15.4) e duas conferências do caminho do
  segredo (15.2: o `PUT` à mão com `clientSecretRef` dando 400, e o reinício do emulador levando a "não configurado"):
  movidos para a próxima fatia.

## Achados

- **O motivo é cortado em 1000 caracteres** numa nota com muitos itens (`BRMF21-10000026`). A lista inteira fica na foto.
  - **Resolvido em 2026-09-28** (change `establishment-and-readable-dashboard`, ADR-0030).
    - **O motivo:** com o mapa `errors`, é um resumo ("6 campos com erro: operacao, tipoPagamento, parceiro.Codigo e
      mais 3"), sem o `title` "One or more validation errors occurred." e sem a omissão.
    - **O detalhe da nota:** lê a lista inteira da foto, campo a campo.
    - **A foto:** perdeu o `type`, o `title` e o `status` repetido, e guarda o `traceId` e a URL.
    - **Os motivos literais acima:** são os de antes da mudança, como foram gravados.
- **A resposta de token traz um `email`** (vazio neste tenant) que a máscara da troca de token não cobria. Passou a cobrir
  no mesmo dia, depois do envio, junto com os outros identificadores.
