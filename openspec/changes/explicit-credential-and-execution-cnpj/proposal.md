## Why

Duas correções pequenas, decididas em 2026-10-05 para entrarem juntas (`docs/STATUS.md`, item "A coluna Empresa das
tabelas de agendamento e de execução mostra o CNPJ da matriz").

- **O fallback do Azure CLI dá falso positivo.** Em Development, ligar a integração automática sem credencial nenhuma
  funciona: o host busca no F&O com a sessão do `az login`, e nada na tela diz que a identidade não é a do perfil. Quem
  roda conclui que a credencial está configurada, e ela não está. O portão é um `IsDevelopment()` só
  (`Program.cs:186-191`), e dentro dele o `D365DevelopmentTokenProvider` cai no Azure CLI em três situações: o perfil sem
  `auth`, o `auth` incompleto e o segredo ausente do cofre.
- **A coluna Empresa da execução mostra o CNPJ da matriz.** Desde a `company-root-in-directory`, a empresa gravada é a
  que o dropdown oferece, o CNPJ da matriz. Numa execução da `SP-01`, a coluna mostra `44.278.225/0001-80`, e as notas
  são de `44.278.225/0002-60`. Quem confere o CNPJ da nota contra o da tabela vê dois números, e nada acusa erro. No dev,
  a execução 16 (2026-10-05) é um exemplo.

## What Changes

### O fallback do Azure CLI sai

- **A credencial do perfil é a única identidade do conector contra o F&O,** em Development e em produção, igual. Não é
  opt-in nem configurável. Quem roda local preenche o Tenant do Entra ID, o Client ID e o Client Secret pela tela, o
  fluxo já provado no Postman.
- **Saem:** o `AzureCliD365TokenProvider`, o `D365DevelopmentTokenProvider`, o `UseD365AzureCliFallback()` e a chamada
  dele no `Program.cs`, com o bloco `if (builder.Environment.IsDevelopment())` que a envolve.
- **Sem credencial, a integração falha com motivo legível,** pelo caminho que já existe para erro de credencial: o mesmo
  que mostra o `AADSTS` no painel da integração automática, o motivo no lugar do dropdown e a recusa da integração
  manual. Os três motivos continuam distintos, como o `D365DevelopmentTokenProvider` já os distinguia:
  - o perfil não tem `auth`;
  - o `auth` está incompleto, e a mensagem diz qual campo falta;
  - o segredo não está no cofre.
- **O log da identidade fica.** Uma linha por tenant, e de novo quando a identidade muda, diz com que app e tenant do
  Entra o tenant autentica. Ela é a prova de tarefas já fechadas (`company-root-in-directory` 7.1,
  `platform-establishment-resolution` 6.x, `erp-company-directory-and-card-filters`). Muda de casa, para o
  `ClientCredentialsD365TokenProvider`, sem a parte do "porque", que não existe mais.
- **Os testes contra o F&O real e o gravador de fixtures** deixam de pedir `az login`. Passam a pedir o tenant do Entra,
  o client id e o client secret por variável de ambiente, e a mensagem de skip diz exatamente qual falta.
- **O roteiro do `RUNNING.md`** ensina a credencial pela tela. O passo de SQL que preparava o poll gravava o `auth` vazio
  por cima do que a tela tinha gravado. Ele passa a mexer só no que precisa, e a ordem dos passos deixa de importar.

### A execução grava o CNPJ do estabelecimento

- **Uma coluna anulável, só na execução:** o CNPJ do estabelecimento que a descoberta resolveu, quando o escopo resolveu
  um só. No agendamento, não: ele não executou, e a única fonte seria o formulário. Execuções registram fato;
  agendamentos registram critério.
- **A descoberta devolve o escopo que resolveu,** junto com as referências. A porta `IDocumentDiscovery` muda de forma,
  e o dado vale também quando a descoberta não acha nenhuma nota (a escolha está no design, D5).
- **A tabela de execuções mostra esse CNPJ quando ele existe, e a empresa quando não,** sem consultar o diretório: o
  histórico não pode depender do ERP no ar. A execução gravada antes da coluna fica com ela nula, e continua mostrando a
  empresa.
- **A tabela de agendamentos não muda.**
- **ADR-0035** registra as duas decisões: a identidade única e a forma nova da porta.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `d365-change-feed`:
  - **a autenticação no F&O:** client credentials sem exceção. Saem o modo Azure CLI e os dois cenários dele. A falta de
    credencial falha com um dos três motivos, e a identidade é logada uma vez por tenant;
  - **o teste contra o ambiente real:** pede a credencial por variável de ambiente, e não o `az login`.
- `connector-credential-test`: no teste do D365, a regra da identidade fica sem a exceção de desenvolvimento. O cenário
  "Em desenvolvimento, sem o segredo" é reescrito: o teste responde sobre a credencial gravada, e não sobre quem roda o
  host.
- `d365-document-assembly`: o teste contra o ambiente real pede a credencial por variável de ambiente.
- `period-discovery`: a descoberta devolve, junto com as notas, o CNPJ do estabelecimento quando o escopo resolveu um só.
- `company-directory`: a tabela de execuções mostra o CNPJ do estabelecimento gravado na execução, e a empresa quando ele
  não existe. A tabela de agendamentos continua com a empresa.

## Non-goals

- **A pendência do app registration.**
- **O item D8 do STATUS,** o `CompanyCode` com significados diferentes conforme a origem. O caminho escolhido não lê o
  `CompanyCode` das referências, e por isso não o piora (design, D5).
- **Rotacionar as credenciais vazadas do conector legado.**
- **O CNPJ do estabelecimento no agendamento.** A tabela de agendamentos continua mostrando a empresa pedida, que é o
  critério.
- **Preencher a coluna nas execuções antigas.** Elas ficam nulas, e mostram a empresa como hoje.
- **O Postman e a receita de entidade com a sessão do az** (`d365/postman/README.md:167-170` e
  `d365/06-receita-criar-entidade-na-mao.md:167`). Testam as entidades direto, sem passar pelo conector, e continuam
  valendo.
- **Reescrever o histórico:** `docs/STATUS.md:1150`, o ADR-0032 §3 e tudo em `openspec/changes/archive/` registram o que
  foi feito na data, e não são instrução.
- **O teste de credencial ler a `FiscalEstablishments`** (o item do STATUS continua aberto).

## Impact

- **Application:**
  - a porta `IDocumentDiscovery` passa a devolver um `DiscoveryResult`;
  - o `IntegrationRunner` grava o CNPJ na execução;
  - `IntegrationExecution` e `ExecutionSummary` ganham o campo;
  - o comentário do `InboundAdapterChoice` deixa de citar o Azure CLI.
- **Adapters:**
  - **D365Poll:** saem dois providers e um método público. O `ClientCredentialsD365TokenProvider` ganha o log e o motivo
    que nomeia o campo que falta. O `D365DocumentDiscovery` devolve o escopo;
  - **Discovery.Local:** devolve o resultado sem CNPJ.
- **Infrastructure:** a coluna `EstablishmentTaxId` em `IntegrationExecutions`, com uma migração EF Core; o store e a
  leitura passam a gravar e ler a coluna.
- **Host:** sai o bloco do fallback; o `GET /executions` traz o campo novo.
- **Dashboard:** a coluna Empresa da tabela de execuções.
- **Testes:**
  - saem o `D365DevelopmentTokenProviderTests` e o teste de cache do Azure CLI;
  - os dois testes de integração com o F&O real passam a usar o client credentials;
  - o `D365PollRegistrationTests` passa a provar que nenhum provider de Azure CLI existe;
  - entram testes dos três motivos, do log e da coluna, com mutação nas quatro provas do STATUS.
- **Ferramentas e docs:**
  - `tools/d365-fixtures/` (o script e o README);
  - `docs/RUNNING.md`;
  - `docs/adr/0035-*.md`;
  - `docs/STATUS.md`, só para fechar o item da coluna, com as provas.
- **Comportamento em Development:** um banco novo de dev, cujo seed grava o `auth` com o tenant e o client id vazios,
  passa a falhar no F&O com "falta Tenant do Entra ID, Client ID" até alguém preencher a tela. É a mudança pedida.
