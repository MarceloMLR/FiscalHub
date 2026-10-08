# ADR-0035: A credencial do perfil é a única identidade do conector, e a execução grava o estabelecimento que a descoberta resolveu

- **Status:** Aceito
- **Data:** 2026-10-05
- **Revisa:**
  - **ADR-0034, "Piora":** a coluna Empresa da tabela de execuções deixa de mostrar o CNPJ da matriz numa execução de
    filial. A tabela de agendamentos continua mostrando a empresa pedida.
- **Change OpenSpec:** `openspec/changes/explicit-credential-and-execution-cnpj`. As capacidades são `d365-change-feed`,
  `connector-credential-test`, `d365-document-assembly`, `period-discovery` e `company-directory`.

## Contexto

São duas correções pequenas, decididas em 2026-10-05 para entrarem juntas.

**A identidade.** Em Development, o host trocava o token do D365 por um provider que decidia por chamada:

- com o `auth` completo e o segredo no cofre, a credencial do tenant;
- sem `auth`, com o `auth` incompleto ou sem o segredo no cofre, a sessão do Azure CLI de quem rodava o host
  (`az login`).

Ligar a integração automática sem credencial nenhuma funcionava. O host buscava no F&O, e nada na tela dizia que a
identidade não era a do perfil. Era falso positivo: quem rodava concluía que a credencial estava configurada, e ela não
estava. O portão era um `IsDevelopment()` só.

**O CNPJ da execução.** Desde o ADR-0034, a empresa gravada é a que o dropdown oferece, o CNPJ da matriz. Numa execução da
`SP-01`, a coluna Empresa mostrava `44.278.225/0001-80`, e as notas eram de `44.278.225/0002-60`. Quem conferia o CNPJ da
nota contra o da tabela via dois números, e nada acusava erro. A descoberta do D365 já resolvia o escopo antes de ler as
notas, mas a porta `IDocumentDiscovery` devolvia só as referências, e o runner não tinha por onde receber o
estabelecimento.

## Decisão

**A credencial do perfil é a única identidade do conector contra o F&O, em qualquer ambiente do host. A execução grava o
CNPJ do estabelecimento que a descoberta resolveu, e a porta de descoberta passa a devolvê-lo.**

1. **Um provider de token só.** O client credentials, com o tenant do Entra, o client id e a referência do segredo do
   perfil. Não há modo de desenvolvimento, opt-in nem chave de configuração. O adapter não lê o ambiente do host, e o
   host não tem ramo por ambiente para o token. Quem roda local preenche o Tenant do Entra ID, o Client ID e o Client
   Secret pela tela.
2. **Sem credencial, a leitura falha com o motivo, pelo caminho do erro de configuração.** O provider lança a
   `ConnectorSettingsException` antes de pedir token. O motivo distingue três casos:
   - o perfil sem `auth`;
   - o `auth` incompleto, nomeando cada campo que falta pelo nome da tela;
   - o segredo ausente do cofre.

   Ele chega ao quadro Situação da integração automática como último erro, ao dropdown de empresas, à recusa da
   integração manual e ao teste de credencial, pelos caminhos que já existiam para essa exceção.
3. **O log da identidade fica, no provider de produção.** Uma linha por tenant, e de novo quando o par (client id, tenant
   do Entra) muda. O texto é o mesmo de antes para a credencial do tenant. A troca do segredo com o mesmo app não é
   identidade nova.
4. **Os testes contra o F&O real e o gravador de fixtures autenticam como o conector,** com a credencial em três
   variáveis de ambiente: `FISCALHUB_D365_ENTRA_TENANT_ID`, `FISCALHUB_D365_CLIENT_ID` e
   `FISCALHUB_D365_CLIENT_SECRET`. Elas não são as `AZURE_*`, que qualquer processo da máquina leria.
5. **A porta de descoberta devolve o escopo resolvido.** O `DiscoverAsync` devolve um `DiscoveryResult`, com as
   referências e, quando o escopo resolveu exatamente um estabelecimento, o CNPJ dele, do cadastro. O CNPJ é decidido
   antes de ler as notas, e vale também quando nenhuma nota é achada. Com mais de um estabelecimento, nenhum, ou na
   origem que não o conhece, ele vai vazio.
6. **A execução grava o fato; o agendamento, o critério.** O CNPJ vai numa coluna anulável, só na tabela de execuções. O
   agendamento não o grava: ele não executou, e a única fonte seria o formulário. As execuções gravadas antes da coluna
   ficam nulas, e nada as preenche.
7. **A tabela de execuções mostra o CNPJ gravado, e a empresa quando ele não existe,** sem consultar o diretório: o
   histórico não depende do ERP no ar.

## Alternativas consideradas

- **O fallback por opt-in de configuração.** Recriaria, com uma chave a mais, o caminho silencioso que era o defeito.
- **Manter o fallback e mostrar a identidade no painel.** O painel diria a verdade, mas a credencial do perfil continuaria
  sem prova até o primeiro deploy, com dois providers e um ramo por ambiente.
- **Recusar a falta de credencial antes do provider.** A checagem teria de se repetir nos quatro leitores do D365 (o
  feed, o source, o diretório e a descoberta), e o teste de credencial continuaria dependendo da exceção do provider.
- **O runner deduzir o CNPJ das referências achadas** (o `CompanyCode` delas):
  - não funciona com zero notas, e a execução da filial que não achou nada voltaria a mostrar a matriz;
  - depende de uma convenção que difere por origem: o CNPJ completo no D365, a raiz no XML;
  - dá vazio quando as notas da filial trazem mais de um CNPJ da mesma raiz.
- **O runner perguntar ao diretório:**
  - seria uma segunda leitura ao ERP por execução;
  - duplicaria no runner a regra do escopo, que mora no adapter;
  - as duas leituras do cadastro podem discordar;
  - deixaria sem resposta boa o diretório que falha depois de a descoberta enfileirar.
- **Um canal lateral** (um campo do critério preenchido pela descoberta, ou um callback). Mudaria o contrato da porta do
  mesmo jeito, sem o tipo dizer.

## Consequências

**Melhora**

- **A credencial que não está configurada aparece como tal,** em Development também, com o campo que falta.
- **A identidade com que o conector lê o F&O é sempre a do perfil.** O que funciona local é o que funciona no deploy.
- **As fixtures gravadas são o que o conector recebe,** com a role do pacote, e não o que a identidade delegada do
  desenvolvedor enxerga.
- **A coluna Empresa de uma execução de filial mostra o CNPJ das notas dela,** também quando ela não achou nota.

**Piora**

- **Um banco novo de dev não lê o F&O até alguém preencher a tela.** O seed grava o tenant do Entra e o client id vazios,
  e o motivo diz isso.
- **Rodar os testes contra o F&O real pede o segredo do app numa variável de ambiente,** e não mais o `az login`.
- **A porta de descoberta mudou de forma,** e as duas implementações e os testes delas acompanharam.
- **As execuções gravadas antes da coluna continuam mostrando a empresa,** inclusive as de filial.

**Não muda**

- **O ADR-0032 §3:** o mock do diretório e o catálogo local continuam só em Development, por registro explícito do host.
  O "desenho do Azure CLI" que ele cita era o padrão desse registro, e não a identidade do conector.
- **O item D8 do STATUS:** o `CompanyCode` com significados diferentes conforme a origem. O CNPJ da execução vem do
  cadastro, e não das referências.
