## Why

A change `connector-not-validator` deixou o payload montado pelo contrato real e as 5 NF-e 55 do fiscosysdev
sendo enviadas. O destino, porém, era o mock, que aceita tudo, e o envio real nunca aconteceu. Nenhuma
resposta da Avalara passou pelo hub até hoje.

O estado do código, conferido:

- **O envio sai sem autenticação.** O Host registra o `NoOpAvalaraTokenProvider`, que devolve cadeia vazia, e
  o dispatcher manda a requisição sem cabeçalho. O `AvalaraTokenProvider` real existe, mas o
  `AddAvalaraTokenProvider()` nunca é chamado.
- **A credencial é global.** O provider real lê `ClientId` e `ClientSecret` de `AvalaraOptions`, que ninguém
  preenche. O gancho existe (`tenantId` no método), mas o valor não vem do perfil do tenant.
- **O token vai para a URL errada.** O dispatcher já resolve a `baseUrl` por tenant (`BaseOf(settings)`), mas
  o cliente de token usa a `BaseUrl` global. Ligado como está, o segredo de um tenant iria para o endereço
  da config do adapter, e não para o ambiente do tenant.
- **A tela salva credencial que ninguém lê.** O `PUT /connector` grava `clientSecretRef` e `clientTokenRef`
  nas `OutboundSettings`, e nada no adapter as usa. O `PUT` também aceita um segredo em claro sem reclamar.
- **O segredo por referência só existe no D365, e só para leitura.** O `SecretReference` (`kv:<nome>` resolvido
  pelo `IConfiguration`) é interno ao adapter D365, e a tela pede que o Admin digite a referência. Só que o Admin
  do cliente não abre terminal e não provisiona cofre: se a configuração do conector é de tela, o segredo
  também é.
- **A resposta da plataforma não é guardada.** O trace tem três fotos (fonte, domínio, destino). O que a
  plataforma respondeu vira, no máximo, um motivo resumido.
- **O limite de tenant tem furos.** A varredura dos endpoints (design D18) achou quatro:
  - **Vazamento:** o `/trace` e o download não comparam o tenant da rota com o do usuário. Qualquer usuário
    autenticado lê a fonte crua, o domínio e o payload de outro cliente, e é por eles que sai a resposta da
    plataforma.
  - **Injeção:** o `POST /ingest` aceita o tenant do corpo, e o `/drop` de dev grava fixo no tenant-a. Um
    documento fiscal entra em nome de outro cliente e é montado, despachado para a plataforma dele e
    registrado lá.
  - **Leitura alheia pela esteira:** o locator do `/ingest` é um caminho de blob livre, inclusive em
    `traces/`. O hub monta o documento de outro tenant sob o tenant de quem pediu.
  - **Interferência:** o `POST /schedules/{id}/deactivate` não filtra por tenant e desliga o agendamento de
    outro cliente.

Esta fatia liga a Avalara de verdade. O primeiro envio real provavelmente volta rejeitado, e isso é o
**valor** da fatia, não o fracasso dela. É o primeiro retorno honesto sobre o payload, que o mock nunca deu.
O limite de tenant entra junto, porque é o endpoint desta fatia que entrega a resposta da plataforma. E os
outros furos são da mesma regra.

## What Changes

- **Duas partes, e a primeira é mergeada sozinha** (design D19).
  - **Parte 1, grupos 1 a 4:** o limite de tenant. São correções de segurança que valem independente da
    Avalara. Ela é commitada e mergeada antes do portão.
  - **Parte 2, do grupo 5 em diante:** a Avalara. Se o portão não bater, a parte 1 não fica presa atrás de uma
    premissa sobre a autenticação de um terceiro.
- **Portão antes do código da parte Avalara.** A premissa do client credentials sustenta a parte 2 do desenho. Um
  curl contra o sandbox confirma o fluxo, o formato da resposta de token e o `expires_in`, e confere o caminho de
  envio, antes de qualquer código dessa parte. É o grupo 5, e ele também prova o emulador do cofre.
  - **Proteção:** o segredo entra pelo prompt, e o token nunca é impresso.
  - **Se não bater:** `/opsx:update` antes do código da parte 2. A parte 1 já mergeada não volta.
- **Provider real por padrão.** `AddAvalaraComplianceDispatcher` passa a registrar o provider de token real.
  O envio sem autenticação só existe quando pedido explicitamente, com `UseAvalaraWithoutAuthentication()`,
  que o Host não chama.
- **Credencial por tenant e por ambiente, lida do perfil.** A seção do ambiente ativo nas `OutboundSettings`
  passa a trazer `clientId`, `clientSecretRef` (`kv:<nome>`) e, opcionalmente, `tokenUrl`.
  - **O que sai:** `ClientId`, `ClientSecret` e `BaseUrl` saem de `AvalaraOptions`.
  - **O que deixa de ser oferecido:** `clientTokenRef`, que nenhum fluxo usa, sai do seed e da tela.
- **A credencial vai só para o ambiente dela.** O token e o envio usam as URLs da mesma seção do mesmo
  tenant, reaproveitando a leitura por tenant que o dispatcher já faz. Não há mais fallback para uma URL
  global, e a URL tem de ser `https`. A única exceção é o loopback, para o mock.
- **Cache de token por credencial.** A chave junta tenant, ambiente, endpoint, `clientId` e a impressão
  (SHA-256) do segredo:
  - dois tenants nunca compartilham token;
  - trocar de ambiente não reaproveita o token do outro;
  - rotacionar o segredo gera um token novo.

  A renovação usa a margem que já existe (`TokenRenewalMargin`). Um 401 com token do cache invalida a
  entrada.
- **O segredo entra pela tela e vai para o cofre.** O `PUT /connector` aceita o Client Secret (e qualquer campo
  de nome de segredo) como **campo de escrita**. O servidor grava o valor no cofre, sob um nome que ele mesmo
  deriva no prefixo do tenant, e o perfil guarda só a referência.
  - **O que nunca acontece:** o `GET /connector` nunca devolve o valor, nem os últimos 4, nem a referência. Ele
    responde "configurado: sim/não" e a data da última gravação.
  - **A referência é do servidor:** um `*Ref` vindo da requisição é recusado. Assim, um tenant não aponta para
    o segredo de outro.
- **A porta do cofre, com escrita.** O `ISecretStore` (get, set e describe sem o valor) fica em Application. O
  `KeyVaultSecretStore`, sobre o `SecretClient` oficial, fica em Infrastructure e é o **único** adapter, em dev e
  em produção. O adapter D365 migra para a porta, e não sobra um segundo resolvedor.
- **Em dev, o cofre é um emulador da API do Key Vault, em memória** (Lowkey Vault no `docker-compose.yml`, com o
  james-gould de plano B).
  - **O que se mantém:** o caminho da tela é idêntico ao de produção. Mudam só a URI, a credencial e o
    certificado fixado, e os três são recusados fora do loopback. A verificação do desafio de autenticação do
    SDK, que o emulador exige desligar, só é desligada com URI de loopback. Isso é tirado da própria URI, e
    nenhuma configuração a desliga em produção.
  - **O que se garante:** nada fica em claro em disco nem no banco.
  - **O trade-off:** reiniciar o emulador apaga os segredos. A tela mostra "não configurado", e o envio falha
    apontando para ela.
  - **Recusados:** o user-secrets, porque a aplicação não escreve nele; o store cifrado, porque em dev seria
    outro adapter; e o Key Vault de dev real, porque quebra o "Azure sem Azure".

  A justificativa está no design (D3).
- **Garantias do segredo:**
  - a lista de nomes (`clientSecret`, `secret`, `password`, `senha`, `apiKey`, `token`, `accessToken`) vale para o
    que é **gravado**. Recebido pela tela, o valor é o caminho certo. Persistido no perfil, nunca, e o adapter
    recusa na leitura o que tiver chegado por outro caminho;
  - o segredo ausente falha alto, nomeando o campo, o tenant e o ambiente, e aponta para a tela, sem comando
    de terminal;
  - não aparece em log, em mensagem de erro nem nas fotos;
  - nada disso vai para o repositório.
- **Requisito de provisionamento: a escrita do host só alcança segredos de conector.** Em cada ambiente de cliente:
  - um papel só com get, set e readMetadata de segredo;
  - uma condição ABAC que restringe esse papel a `fh-*` (preview no Key Vault);
  - um cofre dedicado aos segredos de conector, como defesa em profundidade e plano B;
  - a verificação em staging: `ForbiddenByRbac` fora do prefixo.

  O `KeyVaultSecretStore` também recusa nome fora de `fh-` antes de chamar o cofre. Registrado no ADR-0027 e no
  checklist do primeiro cliente.
- **Recusa de credencial não vira retentativa.** Três casos são registrados como impossibilidade do lado
  do conector, com o motivo e sem retentativa:
  - o endpoint de token recusa a credencial;
  - o envio recebe 403;
  - o envio recebe 401 com token recém-emitido.

  Retentar credencial errada não conserta nada, pode bloquear a conta, e na dead-letter o motivo se perde.
  A recusa fica lembrada por credencial durante um intervalo. Nesse intervalo, as notas seguintes do
  tenant falham com o mesmo motivo sem chamar o endpoint de token. Indisponibilidade (5xx, 429, rede)
  continua no retry nativo (ADR-0004).
- **Salvar o perfil esquece a recusa na hora.**
  - **Como:** a gravação do perfil passa a ser um caso de uso da Application (política de segredo, upsert,
    aviso aos adapters). O adapter Avalara esquece a recusa e os tokens do tenant que salvou.
  - **Por quê:** quem corrige o segredo na tela não espera o intervalo achando que não funcionou.
- **Aceite sem identificador não é reenviado.** Um 2xx sem identificador reconhecível deixava de ser
  exceção e retentativa, o que reenviaria à plataforma um documento talvez já aceito. Passa a ser registro
  com motivo explícito e sem retentativa.
- **Quarta foto: a resposta da plataforma.** Ao lado de fonte, domínio e destino, o trace guarda a resposta
  do envio e a última resposta da consulta de status.
  - **Conteúdo:** status HTTP, uma lista fechada de cabeçalhos e o corpo.
  - **Onde aparece:**
    - no `/trace` e no zip de `/documents/{tenant}/{key}/download`;
    - na aba nova "Resposta" do detalhe do documento. O `shape()` do dashboard, que hoje põe todo `.json` na
      aba Destino, passa a reconhecer a resposta e a fonte `source.json` do D365.
  - **Gravação:** depois do POST, a foto é gravada em modo "melhor esforço". Uma falha ao gravá-la não muda
    o desfecho nem provoca reenvio.
- **Regra de segurança.** Nenhum cabeçalho de autorização, token ou credencial aparece na foto, no log, no
  motivo do dashboard ou em mensagem de erro.
  - **Redação:** se a resposta da plataforma ecoar algo sensível, o conteúdo é redigido antes de gravar,
    pelo nome da propriedade e pelo valor conhecido.
  - **Logs:** os clientes HTTP redigem os cabeçalhos nos logs.
  - **Tipos:** os tipos que carregam segredo ou token não o expõem no `ToString`.
  - **Teste:** um teste prova cada canal.
- **Limite de tenant: o tenant vem de quem está logado, nunca da requisição** (design D18).
  - **Varredura:** todos os endpoints que recebem tenant pelo corpo, pela rota ou pela query foram
    conferidos contra o `ITenantContext`, e o resultado está no design.
  - **Fotos:** o `/trace` e o download passam por um caso de uso sobre o `INoteTraceReader`, que já existe.
    Tenant diferente dá o mesmo 404 de documento inexistente, sem ler o Blob do outro.
  - **`/ingest`:** passa a usar o tenant do login, por um caso de uso da Application. O `tenantId` sai do
    corpo.
  - **Locator:** o de XML tem de ficar em `nfe/{tenant}/…`, e a regra fica em quem o lê: o `IInboundSource`
    ganha a conferência do locator, e ela vale na ingestão manual e na busca, para qualquer caminho. O
    `traces/` nunca é origem, nem no próprio tenant, por regra própria. O `..` é recusado.
  - **Seed e catálogo:** os XMLs de exemplo e o catálogo da descoberta passam a `nfe/tenant-a/…`.
  - **Drop:** sem tenant padrão. O arquivo fora de `{tenant}/{chave}.xml` não é ingerido, e o `/drop` de dev
    grava no prefixo do tenant do login.
  - **Agendamento:** o `deactivate` filtra pelo tenant.
  - **Campos mortos:** o `TenantId` sai do `ManualIntegrationRequest` e do `ScheduleRequest`, que o
    ignoravam, junto com o comentário "tenant nulo cai no de dev".
  - **Fluxo legítimo entre tenants:** nenhum foi encontrado. Se um existir, será um papel explícito, com
    autorização própria.
  - **Caminhos de produção:** foram conferidos. O drop, o feed, a fila e a descoberta montam o locator a partir
    do próprio evento ou do sistema. As pré-condições para abrir o drop e a fila a cliente estão
    registradas.
- **Mock com autenticação.** O mock ganha o endpoint de token e passa a exigir o Bearer que ele emitiu. O
  caminho real de autenticação roda contra o mock no dev e no ponta a ponta em memória.
- **Sonda do sandbox (`tools/AvalaraSandboxProbe`).** Ferramenta de dev que envia variantes de um payload ao
  sandbox, com a mesma credencial, a mesma redação e o mesmo envelope da quarta foto. A saída vai para uma
  pasta ignorada pelo git.
- **Teste manual contra o sandbox, com as respostas gravadas.**
  - **O que roda:** as 5 NF-e 55 passam pelo hub, sem mudar o payload.
  - **O que se grava:** as respostas reais, redigidas, viram fixtures e testes.
  - **O relatório:** separa o que é problema nosso do que é característica do dado. As notas são de 2016 e
    não têm dado da Reforma.
- **Experimento do campo numérico omitido, como tarefa própria.**
  - **Variantes:** a mesma nota vai com `finalidadeNotaFiscal` omitido e com o campo presente, mais um
    controle com `0`.
  - **Leitura:** as respostas são comparadas por uma tabela de interpretação fixada antes do envio.
  - **Checklist:** o item "Campo omitido virando 0" é fechado ou reescrito com a evidência.
- **ADRs.**
  - **ADR-0027:** a credencial por tenant, o segredo em dev, a classificação das falhas de autenticação e a
    quarta foto.
  - **ADR-0028:** o limite de tenant.
  - **Notas de revisão:** os ADRs 0006, 0009, 0018, 0019 e 0026 ganham nota, no formato que o 0025 e o 0024
    receberam. No ADR-0026, a nota fica no §2, no marcador "Falha transitória", que cobria o 401 e o 403 sem
    nomeá-los. A decisão explícita revertida está no D10 do design arquivado da CNV.
- **BREAKING (configuração e dev):**
  - toda seção de ambiente usada precisa de `baseUrl`, `clientId` e `clientSecretRef`. Sem eles, o envio é
    rejeitado com motivo claro;
  - `Avalara:BaseUrl` sai do `appsettings`;
  - no dev, até contra o mock, é preciso digitar o Client Secret na tela, e de novo a cada reinício do
    emulador do cofre. Sem ele, cada nota falha alto apontando para a tela;
  - o `PUT /connector` deixa de aceitar `*Ref` no corpo, e as referências do seed passam ao formato
    `fh-{tenant}-…`;
  - o locator de XML fora de `nfe/{tenant}/` é recusado. O `/ingest` do RUNNING §4 passa a
    `nfe/tenant-a/nfe-exemplo.xml`, sem `tenantId` no corpo;
  - o arquivo na raiz do drop deixa de ir para o tenant-a e não é ingerido.

## Capabilities

### New Capabilities

- `connector-secret-references`: o segredo de qualquer adapter entra pela tela e vai para o cofre. Cobre:
  - o campo de escrita, e a referência derivada pelo servidor no prefixo do tenant;
  - a leitura do perfil sem o valor;
  - o mesmo cofre e o mesmo adapter em dev (emulador em memória) e em produção (Key Vault);
  - a recusa do valor cru persistido;
  - a falha alta que aponta para a tela.
- `avalara-tenant-authentication`: autenticação na plataforma por tenant e ambiente. Cobre:
  - a credencial lida do perfil;
  - o provider real por padrão, e o envio sem autenticação só por pedido explícito;
  - a credencial restrita às URLs do próprio ambiente;
  - o cache por credencial, com margem e invalidação;
  - a classificação das falhas do endpoint de token;
  - a recusa esquecida quando o perfil é salvo.
- `platform-response-trace`: a quarta foto. Cobre:
  - a resposta do envio e da consulta gravada com o documento, presente no `/trace` e no zip;
  - o envelope, com status, cabeçalhos da lista fechada e corpo;
  - a redação de credencial e token em todos os canais;
  - a gravação que não altera o desfecho.
- `tenant-boundary`: o tenant vem de quem está logado. Cobre:
  - as fotos só para o tenant dono;
  - a ingestão manual no tenant do login;
  - o locator dentro do espaço do tenant, com `traces` nunca como origem;
  - o recurso por identificador só do próprio tenant;
  - a zona de drop sem tenant padrão.

### Modified Capabilities

- `compliance-dispatch-outcome`:
  - em "Rejeição síncrona da plataforma", o 401 e o 403 saem da lista do retry nativo. O 403 e o 401 com
    token recém-emitido passam a ser impossibilidade do conector. O 401 com token do cache invalida o token
    e segue o retry. O motivo passa a sair do corpo já redigido;
  - "Impossibilidade do lado do conector" passa a incluir a credencial, o segredo e a URL do ambiente, a
    recusa da credencial pelo endpoint de token e a credencial negada pela plataforma depois do envio;
  - entra o requisito "Aceite sem identificador não é reenviado".

## Non-goals

- **Corrigir o payload a partir das rejeições.** É a próxima fatia. Esta registra e classifica as rejeições,
  sem mudar o mapper.
- **Ajustar a leitura da resposta real** (onde vem o identificador, os valores de status) além de não
  reenviar e de mostrar a resposta. Também vai com a próxima fatia.
- **App registration e client credentials do D365.** O D365 continua na identidade delegada do Azure CLI. A
  migração do resolvedor de segredo do adapter D365 é mecânica e não liga esse modo.
- **Emitir nota nova no fiscosysdev.** É tarefa manual, fora da change.
- **Cancelamento e consulta de status em regime.**
- **Provider de configuração do Key Vault no Host de produção.** A porta e o nome da referência já servem a
  ele, e o wiring vem com o deploy.
- **Visualização dedicada da resposta.** A aba "Resposta" mostra o envelope como JSON, na mesma visualização
  das outras abas.
- **Abrir o drop ou a fila a cliente.** As pré-condições (a credencial de escrita presa ao tenant, e o tenant
  tirado dela) ficam registradas no ADR-0028 e no STATUS.md, para a fatia que fizer isso.
- **Decidir se o `/ingest` existe em produção.** A pergunta fica no STATUS.md. A correção do locator vale de
  qualquer forma.
- **Escopo por tenant do diretório de empresas.** A porta não recebe tenant, e o adapter JSON é de dev.
  Registrado no STATUS.md.
- **Estado compartilhado da recusa entre instâncias do host.** Hoje há uma instância só (ver os riscos do
  design).
- **Checagem de credenciais na subida do host.** A falha aparece por documento, com o motivo.

## Impact

- **Application:**
  - `Connectors` ganha:
    - `ISecretStore`, a porta do cofre com escrita;
    - o parser da referência `kv:` e a derivação do nome (`SecretNames`), puros;
    - o caso de uso `ConnectorProfileService`, que grava o perfil (campo de escrita → cofre → referência), lê
      sem o valor e avisa os `IConnectorProfileObserver`;
  - `Tracing` ganha:
    - `SaveResponseAsync` no `IProcessingTrace`, e o `NoOpProcessingTrace` acompanha;
    - o caso de uso `DocumentTraceQuery`, sobre o `INoteTraceReader` que já existe e o `ITenantContext`;
  - `Support` ganha o `TraceArchive.Zip`, extraído do `SupportTicketService`, que passa a usá-lo;
  - `Inbound`:
    - o `IInboundSource<T>` ganha `CheckLocator`;
    - entra o caso de uso `ManualIngestion`, com o tenant do contexto.
- **Infrastructure:**
  - `KeyVaultSecretStore`, sobre o `SecretClient`, com cache curto do valor lido;
  - `BlobProcessingTrace` grava a quarta foto no mesmo prefixo das outras, pelo `TracePaths`;
  - `SqlScheduleStore.DeactivateAsync` filtra pelo tenant;
  - o seed traz `clientId` e as referências `fh-{tenant}-…`, sem `clientTokenRef` e sem nenhum valor.
- **Adapters:**
  - `Outbound.Avalara`: credencial e endpoints por seção; provider por credencial, com cache, margem e
    invalidação; classificação das falhas; redação; envelope da resposta; `AvalaraOptions` sem campos
    globais de credencial e URL; clientes HTTP com cabeçalhos redigidos no log;
  - `Inbound.Xml`: `CheckLocator`, com o prefixo do tenant, sem `traces` e sem travessia, aplicado também no
    `FetchAsync`;
  - `Ingress.D365Poll`:
    - `CheckLocator` pelo `D365DocumentLocator.Parse`;
    - `ClientCredentialsD365TokenProvider` sobre o `ISecretStore`, com o mesmo comportamento;
  - `Ingress.BlobDrop`: sem `DefaultTenant`, e exigindo `{tenant}/{chave}.xml`;
  - `Discovery.Local`: o catálogo em `nfe/tenant-a/…`.
- **Host:**
  - a seção `SecretStore`, com o emulador em loopback no `appsettings.Development.json` e a recusa, na subida,
    da configuração de emulador fora do loopback;
  - o `PUT /connector` passa pelo caso de uso, e o `*Ref` no corpo dá 400;
  - o `GET /connector` responde "configurado" e a data, sem o valor;
  - o `ConnectorProfileRequest` não imprime as settings no `ToString`;
  - o `/trace` e o download passam pelo `DocumentTraceQuery`, e o zip pelo `TraceArchive.Zip`;
  - o `/ingest` passa pela `ManualIngestion`, com 400 para locator fora da regra;
  - o `/drop` grava no tenant do login, e o `LocalSeed` grava em `nfe/tenant-a/`;
  - o `deactivate` responde 404 fora do tenant;
  - os campos `TenantId` mortos saem dos corpos;
  - `Avalara:BaseUrl` sai do `appsettings`.
- **Dashboard:**
  - a tela de conectores troca `clientTokenRef` por `clientId` e `tokenUrl`, e mostra a recusa do `PUT`;
  - os campos "de referência" viram campos de senha de escrita, com "configurado em <data>" ou "não
    configurado", sem nunca ler o valor de volta;
  - o `shape()` reconhece a resposta e a `source.json`, e entra a aba "Resposta";
  - os tipos perdem o `tenantId` dos corpos.
- **Infra local:** o `docker-compose.yml` ganha o emulador do cofre, com a versão fixada e sem persistência.
- **tools:**
  - `MockComplianceApi` ganha o endpoint de token e passa a exigir o Bearer;
  - `AvalaraSandboxProbe` é novo;
  - o `.gitignore` ganha a pasta de saída da sonda.
- **Testes:**
  - provider: por tenant, cache, margem, isolamento, invalidação, falhas do token;
  - DI: o real por padrão, o stub só explícito;
  - o segredo pela tela: vai só para o cofre, e o perfil guarda só a referência; o `*Ref` na requisição é
    recusado; o campo ausente mantém o segredo;
  - o segredo não volta no `GET`, nem aparece em log, em mensagem ou nas fotos;
  - gravar pela tela esquece o token e a recusa, também quando o perfil falha depois do cofre;
  - o `KeyVaultSecretStore` sobre um `SecretClient` escrito à mão, e um teste opt-in contra o emulador;
  - segredo ausente e malformado, e referência fora do prefixo do tenant;
  - o seed sem valor e no prefixo;
  - redação em foto, log, motivo e exceção;
  - quarta foto no trace e no zip;
  - a recusa esquecida ao salvar o perfil, só para o tenant que salvou;
  - as fotos de outro tenant: não encontradas, sem leitura do armazenamento;
  - o `/ingest` no tenant do login;
  - o locator: o `traces` do próprio tenant e o de outro, o prefixo alheio, o `..`;
  - o drop sem tenant padrão;
  - o `deactivate` de outro tenant;
  - aceite sem identificador;
  - ponta a ponta com autenticação contra o mock em memória;
  - respostas reais gravadas rodadas de novo pelo dispatcher.

  O `dotnet test` continua sem rede.
- **Docs:**
  - `docs/adr/0027-*.md` e `docs/adr/0028-*.md`, o índice e as notas nos ADRs 0006, 0009, 0018, 0019 e 0026;
  - `docs/RUNNING.md`:
    - o emulador do cofre, o segredo pela tela e o reinício que o apaga;
    - a troca do tenant-a para o sandbox;
    - o roteiro da sonda;
    - o §4 com o locator novo e sem `tenantId`;
  - `docs/STATUS.md`: checklist com evidência, as pré-condições do drop e da fila, a pergunta do `/ingest` em
    produção e o diretório sem tenant;
  - relatório do primeiro envio real.
- **Sistemas externos:** o sandbox da Avalara passa a receber o token e os documentos do tenant de dev.
