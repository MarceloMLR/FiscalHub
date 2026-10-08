# Rodando localmente

O FiscalHub roda de ponta a ponta na sua máquina, sem Azure — com equivalentes locais de cada
serviço da nuvem (Azure "sem Azure").

| Azure | Local |
|-------|-------|
| Blob Storage | Azurite (container) |
| Azure SQL | SQL Server (container) |
| Service Bus | emulador do Service Bus (container) |
| Key Vault (segredos de conector) | emulador da API do Key Vault, Lowkey Vault (container, **só em memória**) |
| Avalara (compliance) | mock em `tools/MockComplianceApi`, que exige token como a plataforma; o sandbox real na seção 8 |

## Pré-requisitos

- SDK do **.NET 10**
- **Docker Desktop** rodando

## 1. Subir a infra (Blob, SQL, fila e cofre)

Na raiz do repositório:

```powershell
.\scripts\up.ps1
```

Sobe `azurite` (Blob nas portas 10000/10001), `sql` (SQL Server na 1433), `servicebus` (AMQP na 5672) e `keyvault`
(a API do Key Vault na 8443), e confere no fim que os quatro ficaram de pé.

**Use o script, e não o `docker compose up -d` cru.** O emulador do Service Bus não tem armazenamento próprio: ele cria
as bases dele dentro do container de SQL e, na subida, derruba e recria essas bases. Quando os dois containers morrem
juntos — um reinício da máquina, por exemplo — o SQL pode parar no meio de um `DROP`: a base sai do catálogo e os
arquivos ficam no disco. Na subida seguinte o emulador não encontra a base, pula o drop, tenta criar e morre com
`Cannot create file ... because it already exists`, saindo com código 139. Ele não se recupera sozinho, porque o estado
órfão não é "existe" nem "não existe".

O script sobe o SQL primeiro, remove as bases do emulador (catálogo e arquivos) e só então sobe o Service Bus. Limpar
não perde nada: o emulador recria essas bases em toda subida, por conta própria.

Sem a fila no ar, o poll do D365 até roda, mas nada é enfileirado nem montado — e o sintoma é "descobriu e não fez
nada". Isso já custou dois diagnósticos.

**O cofre de dev é em memória, de propósito** (ADR-0027). O `keyvault` é o emulador da mesma API do Key Vault, e o host
fala com ele pelo mesmo adapter de produção; o `appsettings.Development.json` só troca o endereço, a credencial e o
certificado do emulador. Ele sobe **sem volume, sem import e sem export**, porque a persistência do emulador grava os
segredos em claro no disco. O preço: `docker compose restart keyvault` (ou `down`) apaga os segredos. A tela de
conectores passa a mostrar "não configurado", o envio falha apontando para ela, e é só digitar de novo.

## 2. Rodar o mock de compliance

Em um terminal:

```powershell
dotnet run --project tools/MockComplianceApi --urls http://localhost:5100
```

O mock imita a plataforma no que já foi verificado no sandbox: responde no caminho de envio do sandbox
(`taxcompliance/v2/fiscal/dfe`, o do `appsettings.Development.json`) e em `/documents`, exige o token que ele emite, e
recusa a credencial com HTTP 400 e `{"error": "<texto livre>"}`.

## 3. Rodar o host

Em **outro** terminal:

```powershell
dotnet run --project src/FiscalHub.Host --urls http://localhost:5200
```

No startup o host cria o schema no SQL e sobe os XMLs de NF-e de exemplo no Blob, no espaço de entrada do
tenant-a (`nfe/tenant-a/nfe-exemplo.xml` e `nfe/tenant-a/nfe-exemplo-2.xml`). A rota `GET http://localhost:5200/` mostra
que está no ar.

As migrações rodam na subida (`Migrate`), e o log mostra cada uma aplicada. Duas delas mexem em dado já gravado:

- **`WidenBranchCode`:** alarga o `BranchCode` para 20, porque a filial do D365 é o código do estabelecimento.
- **`RenameRealTimeTrigger`:** troca o modo `RealTime` por `Automatic` nos documentos já processados.

### O dashboard

Em **outro** terminal:

```powershell
cd dashboard; npm install; npm run dev
```

O Vite serve em `http://localhost:5173` e consome a API do host em `http://localhost:5200` (`VITE_API_BASE_URL`).

**Depois de uma mudança no front, recarregue a página com Ctrl+Shift+R antes de testar.** Uma aba aberta desde antes
da mudança continua rodando o pacote antigo.

- **O sintoma:** o interruptor salva e volta desligado. O `PUT /connector` vai, responde sucesso e grava o campo antigo:
  o pacote antigo manda o `realtime`, que o servidor ignora, e não mexe no `poll.enabled`.
- **O custo:** custou uma rodada de diagnóstico na prova manual da `automatic-integration-switch`.

O que a tela mostra, e que parece defeito mas não é:

- **Os cards e a tabela contam pelo dia da execução, na janela e no modelo escolhidos** (ADR-0032 §8).
  - **Qual data:** o dia em que a integração rodou, em Brasília: o da imediata, da diária ou da agendada, ou o da busca do
    coletor. A data fiscal continua gravada, mas não é a data da linha.
  - **O período integrado:** a coluna "Período integrado" mostra o período da imediata, da diária e da agendada. A
    automática mostra "—".
  - **A janela:** o dia (o padrão), os últimos 7, 15 ou 30 dias (hoje e os N−1 anteriores, pelo dia do navegador), ou o
    personalizado, de uma data a outra.
  - **O modelo:** todos, ou um modelo. A NFS-e ignorada conta em "Documentos", e não em "Com erro".
  - **A contagem é do servidor** (`GET /groups/totals`), sobre todas as notas da janela, e a tabela (`GET /groups`) segue o
    mesmo filtro. O modal de uma linha lista só as notas dela.
  - **A nota fica na linha da última entrada:** outra integração, ou o coletor de novo, a move. O reprocesso não a move, e
    só soma na coluna "Reprocessos" do modal. A integração agendada que a idempotência pula também não.
  - **O efeito no fiscosysdev:** as notas de 2016 que a integração imediata de hoje trouxe aparecem hoje, com o período de
    2016 ao lado. As que o banco já tinha ficam no dia em que foram gravadas pela primeira vez.
- **A empresa é o CNPJ do estabelecimento próprio.**
  - **Nas notas do D365:** o CNPJ sem a pontuação e com as letras, mascarado na tela pelo tamanho (14 caracteres, o
    alfanumérico também), e a filial é o código do estabelecimento (`Matriz`, `SP-01`, `SAL-01` na `brmf`).
  - **No caminho de XML de dev:** continuam os 8 primeiros caracteres do CNPJ do emitente.
- **O selo da barra lateral:**
  - **Quando aparece:** só para o adapter de entrada que varre (hoje, o `Dynamics365`);
  - **As cores:** verde é "ligada", e vermelho é "desligada".
  - **Adapter que não varre:** o selo não aparece.
- **O detalhe da nota** mostra primeiro o que se lê:
  - **na recusa:** a lista de campos, com as mensagens da plataforma;
  - **na nota aceita com omissão:** a marca "Enviado com ressalvas".

  O JSON cru abre num modal próprio, pelo "Visualizar JSON", com uma aba por foto. Fechar ou apertar Esc volta ao
  detalhe.
  - **Só para Admin, de fato:** o "Visualizar JSON" e o "Baixar arquivos" só aparecem para o Admin, e o `/trace` e o zip
    dão 403 para os demais papéis.
  - **O Viewer:** vê a primeira vista pela leitura do desfecho (`/documents/{tenant}/{chave}/reading`), que traz só a
    lista de campos e as omissões.
- **Agendamentos:** a aba "Agendamentos" de Integrações tem "Excluir", com confirmação. A exclusão não se desfaz, e as
  execuções que o agendamento disparou continuam na aba "Execuções".

### O Client Secret, pela tela

Todo envio leva o token da credencial do tenant, no ambiente ativo (ADR-0027). Não há modo "sem autenticação" no host.
A credencial vem das `OutboundSettings` (`baseUrl`, `clientId` e, fora da tela, o `tokenUrl` opcional), e o **segredo é
digitado na tela**, até contra o mock:

1. Abra o dashboard, entre como `admin@fiscalhub.local` e vá em **Configurações → Conectores → Saída (compliance)**.
2. Na seção **Sandbox**, digite qualquer valor no **Client Secret** (o mock aceita qualquer um não vazio) e salve.
3. O campo volta vazio, com a máscara (uma fileira de bolinhas) e "configurado em <data>". O valor foi para o cofre, e o perfil guarda
   só a referência `kv:fh-tenant-a--outbound--sandbox--clientsecret`. O `GET /connector` nunca devolve o valor.
   - **A máscara é placeholder, e não valor.** Salvar sem digitar no campo não manda o segredo, e o do cofre fica.
   - **A defesa do servidor:** um valor feito só de `*`, `•`, `●` ou `∗` é recusado com 400, sem tocar o cofre.

Sem isso, o envio é rejeitado com "Configuração do conector: o Client Secret do ambiente 'sandbox' do tenant 'tenant-a'
não está configurado … Configure em Configurações → Conectores → Avalara → Sandbox → Client Secret." Depois de reiniciar
o emulador, digite de novo.

### Os estabelecimentos: a plataforma lista, a tabela sobrepõe

Todo envio precisa de `codigoEmpresa` e `codigoContribuinte`. Eles **não vêm do ERP** (ADR-0026 §3). Vêm da **listagem da
plataforma**, casada pelo CNPJ do estabelecimento próprio, sem pontuação e com as letras (ADR-0033):

- **a listagem:** `GET /taxcompliance/v2/empresa` e, para cada empresa, `GET /taxcompliance/v2/contribuinte?empresaId=`,
  paginados até a página vazia;
- **o payload:** leva o `codigoCIA` da empresa e o `codigo` do único contribuinte com o CNPJ.

**A tabela `establishments`** das `OutboundSettings`, por ambiente, virou sobreposição opcional:

- **com a entrada:** quando ela tem o CNPJ, ganha, e a listagem nem é chamada;
- **sem a tabela:** ela é a sobreposição vazia, e não um erro.

**As recusas,** todas "Configuração do conector: …", sem retentativa:

- **nenhum contribuinte com o CNPJ:** "o estabelecimento 44278225000180 não tem contribuinte cadastrado na plataforma …";
- **mais de um:** o hub não escolhe, e nomeia os candidatos: "empresa '012' (METALURGICA …), contribuinte '010' (#2000010001); …".

**A janela.** A listagem fica guardada por tenant e ambiente, e um lote de notas usa uma só. As opções:

| Opção | Padrão | O que é |
|---|---|---|
| `PlatformEstablishments:CacheDuration` | 10 min | A validade da listagem guardada |
| `PlatformEstablishments:RefusalHold` | 5 min | Quanto tempo uma recusa da listagem fica lembrada |
| `Avalara:ListingPageSize` | 100 | O `$top` de cada página |
| `Avalara:ListingMaxPages` | 50 | O teto de páginas por lista |

Zero ou negativo em qualquer uma impede o host de subir.

**Para reler a plataforma** depois de cadastrar um contribuinte, salve o perfil do conector na tela, mesmo sem mudar nada, e
reprocesse a nota. Salvar esquece a listagem guardada do tenant, e a próxima nota lista de novo.

**O mock lista como a plataforma,** com uma conta de mentira. Os códigos dos contribuintes não seguem a ordem do CNPJ:

| Empresa | Contribuinte | CNPJ | Código |
|---|---|---|---|
| `012` (METALURGICA EXEMPLO) | Matriz | `44278225000180` | `010` |
| | SP-01 | `44278225000260` | `007` |
| | SAL-01 | `44278225000341` | `021` |
| | RJ-01 | `44278225003448` | `003` |
| | Os XMLs de exemplo | `12345678000190` | `015` |
| `Comércio` | | `11222333000181` | `001` |
| `009` | | `99888777000166` | `001` |

Os modos do mock ficam em `/admin`, abertos como os outros toggles:

```powershell
# A duplicidade: o CNPJ da Matriz também na empresa 009 (empresa= é o codigoCIA; sem ele, 009)
Invoke-RestMethod -Method Post "http://localhost:5100/admin/contribuintes/adicionar?cnpj=44278225000180&empresa=009"
# O CNPJ sem cadastro
Invoke-RestMethod -Method Post "http://localhost:5100/admin/contribuintes/remover?cnpj=44278225000180"
# O servidor que limita a página abaixo do $top (sem itens, ou 0, tira o limite)
Invoke-RestMethod -Method Post "http://localhost:5100/admin/listagem/limite?itens=2"
# Volta ao inicial: a conta, sem limite, e os contadores zerados
Invoke-RestMethod -Method Post "http://localhost:5100/admin/contribuintes/restaurar"
# A conta atual e as requisições de listagem recebidas (cada página conta uma)
Invoke-RestMethod "http://localhost:5100/admin/contribuintes" | ConvertTo-Json -Depth 5
```

Depois de mudar a conta no mock, salve o perfil na tela para o hub reler, como faria depois de cadastrar na plataforma.

**O seed de um banco novo** traz a tabela vazia no `sandbox` do tenant-a: o mock lista, e o caminho automático é o padrão.

**Num banco criado antes desta mudança,** o seed não roda de novo. A tabela antiga traduz a Matriz (`44278225000180`) e o
CNPJ dos XMLs (`12345678000190`) para `20247332000182`, e continua valendo como sobreposição. Para exercitar o caminho
automático, esvazie a tabela pelo mesmo padrão de SQL por arquivo das `InboundSettings` (seção 6). O SQL grava só a
referência do segredo, cujo valor continua sendo digitado na tela.

```powershell
$out = '{"sandbox":{"baseUrl":"http://localhost:5100/","clientId":"mock-client","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret","establishments":{}},"production":{"baseUrl":"https://api.avalara.com/","clientId":"","clientSecretRef":"kv:fh-tenant-a--outbound--production--clientsecret","establishments":{}}}'
"UPDATE ConnectorProfiles SET OutboundSettings = N'$out' WHERE TenantId = 'tenant-a';" | Set-Content -Encoding ascii set-outbound.sql
docker cp set-outbound.sql fiscalhub-sql-1:/tmp/set-outbound.sql
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub -i /tmp/set-outbound.sql
```

Depois do SQL, salve o perfil na tela, ou espere a validade, para o hub esquecer a listagem guardada.

## 4. Disparar a esteira

O `/ingest` exige login, e a nota entra no tenant de quem está logado. O corpo não leva tenant (ADR-0028):

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5200/auth/login -ContentType application/json `
  -Body '{"email":"admin@fiscalhub.local","password":"Fiscal@123"}'
$auth = @{ Authorization = "Bearer $($login.token)" }

$body = '{"naturalKey":"nfe-001","locator":"nfe/tenant-a/nfe-exemplo.xml"}'
Invoke-RestMethod -Method Post -Uri http://localhost:5200/ingest -Headers $auth -Body $body -ContentType application/json
```

Isso faz o hub: ler o XML do Blob → validar → mapear e despachar pro mock → gravar o status no SQL.

O locator tem de estar no espaço de entrada do tenant do login, `nfe/{tenant}/<arquivo>`. Fora dele, o `/ingest`
responde 400 com a regra. O armazenamento de fotos (`traces/…`) nunca é origem, nem no próprio tenant.

Pelo drop, o arquivo precisa estar em `drop/{tenant}/{chave}.xml`. Um arquivo na raiz do drop não é ingerido: fica
lá, e o host avisa no log uma vez.

O XML também depende dos estabelecimentos. O XML não diz qual parte é a do tenant: a nossa é a única que tem entrada na
tabela `establishments` ou contribuinte na plataforma (ADR-0033). O emitente `12345678000190` está na listagem do mock,
então é o estabelecimento próprio, e o destinatário é o parceiro. As duas partes do tenant, ou nenhuma, é recusa citando os
dois CNPJs.

## 5. Ver o resultado

**No SQL** (o que a esteira gravou — inclui o `ExternalId`, que é o GUID do mock):

```powershell
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "SELECT NaturalKey, Status, ExternalId FROM ProcessedDocuments;"
```

**O JSON que o hub enviou** (use o `ExternalId`/GUID do passo acima):

```powershell
Invoke-RestMethod -Uri http://localhost:5100/documents/<GUID> | ConvertTo-Json -Depth 10
```

Aí você vê o payload no formato da Avalara, que segue os JSONs reais de integração:

- os códigos da empresa, vindos da configuração;
- o parceiro;
- os tributos clássicos no bloco `imposto` de cada item;
- o IBS/CBS da Reforma no array `impostos[]`, em `CBS`, `IBS ESTADUAL` e `IBS MUNICIPAL`.

Campo que o documento não tem não aparece.

**O status pelo GUID:**

```powershell
Invoke-RestMethod -Uri http://localhost:5100/documents/<GUID>/status
```

## 6. Poll do D365 (fatia 1: descobrir e enfileirar)

O worker de feed de mudanças (ADR-0024) pergunta ao F&O o que mudou na `FSFiscalDocumentBRs`. Ele
consulta por janela de data em `SysModifiedDateTime`, pagina por keyset e usa um lease por tenant, e
publica cada referência na fila **`documents-discovered`**. Quem consome essa fila é a montagem
(seção 7).

Para simular "chegou nota nova" sem inserir nada no ERP, o roteiro rebobina a marca para 2015. A
primeira passada então descobre os 83 cabeçalhos do `fiscosysdev` (empresa `brmf`).

**Pré-requisitos**

- `docker compose up -d`, que sobe SQL, Azurite e o emulador do Service Bus com a fila
  `documents-discovered`.
- **A identidade no F&O.** O conector autentica só com a credencial do perfil do tenant, em Development como em
  produção (change `explicit-credential-and-execution-cnpj`). A sessão do `az login` não entra no lugar.
  - **Onde preencher:** em **Configurações → Conectores → Entrada (ERP)**, o **Tenant do Entra ID**, o **Client ID** e
    o **Client Secret** do app do conector. Salve. O segredo vai para o cofre, e o perfil guarda só a referência.
  - **O log:** uma linha por tenant, e de novo quando o app muda, diz com que identidade ele autentica:

    ```
    D365: o tenant tenant-a autentica no F&O com a credencial do próprio tenant (client credentials: app <client id>, tenant do Entra <tenant do Entra>).
    ```

  - **Sem a credencial, a integração não lê o F&O.** O quadro **Situação** mostra o motivo como último erro, e o
    dropdown de empresas mostra o mesmo motivo no lugar da lista. Um banco novo de dev vem com o tenant do Entra e o
    Client ID vazios, e o motivo é:

    ```
    A credencial do ERP está incompleta: falta Tenant do Entra ID e Client ID. Configure em Configurações → Conectores → Entrada.
    ```
- Host rodando em Development. É o padrão do `dotnet run`, pelo `launchSettings.json`.

**1. Preparar o poll do tenant-a**, ainda desligado, com página de 20, para exercitar 5 páginas por keyset, e marca
inicial em 2015. O `pageSize` e o `startFrom` não têm tela, e por isso vão por SQL. O `enabled` vai `false`, porque quem
liga é a tela, no passo 2. O SQL muda só esses três campos, com `JSON_MODIFY`: a URL, as empresas e a credencial gravada
pela tela ficam como estão, e a ordem entre este passo e a tela não importa.

```powershell
@'
UPDATE ConnectorProfiles
SET InboundSettings = JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(InboundSettings,
    '$.poll', JSON_QUERY(ISNULL(JSON_QUERY(InboundSettings, '$.poll'), '{}'))),
    '$.pageSize', 20),
    '$.poll.enabled', CAST(0 AS bit)),
    '$.poll.startFrom', '2015-01-01T00:00:00Z')
WHERE TenantId = 'tenant-a';
'@ | Set-Content -Encoding ascii enable-poll.sql
docker cp enable-poll.sql fiscalhub-sql-1:/tmp/enable-poll.sql
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub -i /tmp/enable-poll.sql
```

O SQL vai por arquivo porque o PowerShell 5.1 quebra as aspas quando elas são passadas direto no `-Q`. O here-string
com aspas simples (`@'…'@`) não expande o `$` do caminho JSON. A primeira troca cria a seção `poll` quando o perfil não a
tem: sem ela, o `JSON_MODIFY` deixaria o `enabled` e o `startFrom` de fora, sem erro.

Dá para usar o `PUT /connector` no lugar do SQL. As settings enviadas substituem as gravadas, com duas exceções: o
segredo ausente mantém a referência que já estava, e os chamados ausentes do corpo ficam como estavam. Um `*Ref` no
corpo é recusado com 400, porque a referência é do servidor.

A gravação também recusa, com 400, um valor da seção `poll` que ela está escrevendo e que o coletor não leria, como um
`enabled` em texto ou um `overlapSeconds` zero. A mensagem nomeia o campo. Um valor inválido que já estava gravado e
volta igual passa, para não trancar a tela: ele continua aparecendo como falha do tenant no log e no cursor.

**2. Rodar o host** (seção 3) **e ligar pela tela.** Em **Configurações → Conectores → Entrada (ERP)**, com o ERP
`Dynamics365`, ligue **Integração automática** e salve. O interruptor grava o `poll.enabled` e só aparece para adapter
que varre. Em até 15 segundos aparece no log:

```
Feed de mudanças: 1 tenant(s) consultado(s), 14 referência(s) na fila de descoberta, 0 suprimida(s) por já publicadas.
```

Junto vêm **69 avisos** `Modelo '01' fora do mapa…`. Isso é esperado. Os 83 cabeçalhos da brmf têm
modelo `01` (69), `SE` (9) e `55` (5), e o mapa padrão cobre `55`, `57` e `SE`. As notas modelo `01`
(Nota Fiscal 1/1A, formulário em papel) são dado de demonstração antigo, e a recomendação é mantê-las
fora do mapa (ADR-0024). Para exercitar mais referências no teste, dá para incluir o `01` no
`modelTypes` das settings.

A barra lateral passa a mostrar **Integração automática ligada**. O selo vem do `/info`, que o deriva do mesmo
`poll.enabled` que o coletor lê.

**3. Conferir o cursor.** A marca deve estar perto de agora: é o relógio do F&O no fim da leitura.

**Pela tela:** em **Configurações → Entrada (ERP)**, sob o interruptor, fica o quadro **Situação**. Ele é só para Admin, e
aparece assim que o interruptor é ligado na tela, antes de salvar. Ele se atualiza a cada 15 segundos e mostra:

- a **última busca**;
- as **falhas consecutivas**;
- o **último erro**, quando há falha;
- **sincronizado até**, que é a marca;
- **aguardando o ERP até**, quando a origem pediu para esperar (throttling);
- antes da primeira busca, de onde ela vai começar: do `startFrom`, ou do momento em que rodar, com o histórico de fora.
  Com marca, o `startFrom` não aparece.

**Pelo SQL,** a mesma leitura:

```powershell
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "SELECT TenantId, Origin, DATEADD(SECOND, (WatermarkTicks - 621355968000000000) / 10000000, '1970-01-01') AS Marca, DATEADD(SECOND, (LastPolledTicks - 621355968000000000) / 10000000, '1970-01-01') AS UltimoPoll, ConsecutiveFailures, LastError FROM ChangeFeedCursors;"
```

**4. Segunda passada.** Um minuto depois, a próxima passada lê só a janela de sobreposição (5 minutos
antes da marca) e não reenfileira nada antigo.

**Rebobinar**, para repetir o teste.

**Pela tela** (change `module-navigation-and-integration-panel`, ADR-0031):

1. No quadro **Situação**, escolha em **Buscar novamente desde** a data e a hora, no fuso do navegador, e confirme.
2. A confirmação diz o que acontece:
   - as notas **alteradas** no ERP desde essa data serão lidas de novo (e não as emitidas);
   - as já enviadas e sem alteração não serão reenviadas;
   - as recusadas, com erro ou ignoradas serão processadas de novo;
   - cada NF-e lida gera pelo menos 4 consultas ao F&O.
3. O log registra `Rebobinamento: <usuário> levou a marca do tenant tenant-a de … para …`. A passada seguinte vem no
   intervalo normal, até 60 segundos, e diz `0 suprimida(s)`.

**As regras:**

- **O lease:** o rebobinamento toma o mesmo lease do coletor. Com o coletor lendo, a tela responde "A integração
  automática está buscando notas agora…", e é só tentar de novo.
- **Só para trás:** uma data depois da marca, ou no futuro, é recusada.
- **Só com marca:** sem cursor, ou com cursor sem marca, não há o que voltar. A primeira passada parte do `startFrom`, e
  para mudar o ponto de partida vale o SQL abaixo.

**Pelo SQL,** a alternativa. Apagar o cursor reprocessa com o processo de pé: a passada seguinte parte do `startFrom` e
põe tudo de volta na fila de descoberta, com `0 suprimida(s)`. Quando o cursor não existe, ou existe sem marca, o poller
esquece o registro de publicações do tenant antes da leitura (change `automatic-integration-switch`, design D5).

```powershell
# apaga o cursor: o startFrom volta a valer na próxima passada
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "DELETE FROM ChangeFeedCursors WHERE TenantId = 'tenant-a';"
# ou aponta a marca direto para 2015-01-01T00:00:00Z (em ticks)
#   UPDATE ChangeFeedCursors SET WatermarkTicks = 635556672000000000 WHERE TenantId = 'tenant-a';
```

Se o `DELETE` cair no meio de uma passada, o log diz `lease do tenant tenant-a perdido no meio do poll`, e isso é
esperado. O avanço da marca não acha a linha, e o aviso culpa o lease. Não conta falha, e a passada seguinte parte do
`startFrom`.

**Desligar:** pela tela, desligando **Integração automática** e salvando. Vale na próxima passada, em até 15 segundos:
o log do feed fica em silêncio para o tenant, e o selo some. É pausa, e não reset: o cursor fica, e religar retoma da
marca. Uma leitura que já estava em curso termina normalmente.

> **TTL de 1 hora no emulador.** As mensagens das duas filas expiram em 1 hora (limite do emulador). Com o
> host no ar, a `documents-discovered` é consumida na hora (seção 7).

**Teste de integração contra o F&O real.** Ele é opt-in e fica pulado sem a variável:

```powershell
$env:FISCALHUB_D365_URL = "https://fiscosysdev.operations.dynamics.com"
$env:FISCALHUB_D365_COMPANY = "brmf"
$env:FISCALHUB_D365_EXPECTED_ROWS = "83"
dotnet test tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests --filter "FullyQualifiedName~Integration"
```

## 7. Montagem do D365 (fatia 2: consumir a descoberta e montar a nota)

O consumidor da `documents-discovered` entrega cada referência ao roteador (ADR-0025):

- **NF-e (modelo 55):** o source do D365 monta a nota. São 4 GETs (cabeçalho, linhas, impostos, encargos),
  mais a contábil do voucher quando há `ImportTax` zerado, e endereço e cidade pelo cache. Depois a nota
  segue a mesma esteira do XML: idempotência, foto do domínio, validação, envio.
- **NFS-e e CT-e:** saem como **"ignorado: tipo fora do escopo"**, gravado, sem nenhuma chamada ao F&O.

**Roteiro.** O mesmo da seção 6: ligar o poll do tenant-a com `startFrom` em 2015 e rodar o host (e o mock).
As 14 referências da primeira passada são consumidas na sequência, uma por vez. Antes, confira a tradução dos
estabelecimentos (seção 3).

**Desfecho esperado no fiscosysdev: 5 enviadas e confirmadas, 9 ignoradas.** O hub não julga conteúdo fiscal
(ADR-0026). As notas de 2016, sem IBS/CBS e uma delas sem chave de acesso, seguem para a plataforma, e quem responde
é ela.

```powershell
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "SELECT NaturalKey, Type, Status, Reason FROM ProcessedDocuments WHERE TenantId = 'tenant-a' AND NaturalKey LIKE 'brmf|%' ORDER BY Status, NaturalKey;"
```

- **5 NF-e 55 com `Confirmed`**, depois do poll de status. Duas delas têm uma observação no motivo, que aparece
  como **aviso** no dashboard, e não como falha:
  - `BRMF06-110000027`: "Enviado sem: item 1: IcmsDiff não enviado (sem lugar no contrato)";
  - `BRMF06-110000031`, a nota de importação: "Enviado sem" do encargo. O imposto de importação vai no bloco
    `imposto.ii`.
- **9 NFS-e com `Ignored`** ("Ignorado" no dashboard, fora do filtro de falhas) e motivo "ignorado: tipo
  fora do escopo (ServiceNfse)".
- **O payload** de cada nota pode ser conferido em `GET http://localhost:5100/documents/<ExternalId>`. Ele tem:
  - os códigos da configuração;
  - o parceiro (a contraparte);
  - os blocos `imposto` por item;
  - nenhum array `impostos`, porque nenhuma nota da base tem IBS/CBS.

> **O mock aceita todo conteúdo.** Ele exige o token que ele mesmo emitiu, mas "5 enviadas" contra o mock prova o
> caminho, a autenticação e o contrato montado, e não a aceitação da Avalara. O envio ao sandbox real é a seção 8. Os
> caminhos que a demonstração não prova estão no checklist do primeiro cliente, em `docs/STATUS.md`.

**Roteiro de rejeição.** Com o mock no ar, force o resultado dos próximos documentos antes da passada.

Se as notas já passaram uma vez, a idempotência não as reenvia: nota confirmada com a mesma impressão não volta.
Nesse caso, apague os registros delas e rebobine a marca (seção 6):

```powershell
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "DELETE FROM ProcessedDocuments WHERE TenantId = 'tenant-a' AND NaturalKey LIKE 'brmf|%'; DELETE FROM ChangeFeedCursors WHERE TenantId = 'tenant-a';"
```

Uma nota **rejeitada** não bloqueia o reprocessamento, então dá para voltar ao normal e repetir só com a
rebobinada.

```powershell
# recusa na consulta de status: as 5 viram IntegrationError com o motivo do mock no dashboard
Invoke-RestMethod -Method Post -Uri "http://localhost:5100/admin/result/erro?motivo=CFOP%20incompativel%20com%20a%20operacao"
# recusa no envio (HTTP 400): mesmo desfecho, na hora do envio e sem retentativa
Invoke-RestMethod -Method Post -Uri "http://localhost:5100/admin/result/rejeitar?motivo=codigoEmpresa%20nao%20cadastrado"
# volta ao normal
Invoke-RestMethod -Method Post -Uri http://localhost:5100/admin/result/carregado
```

O motivo gravado diz quem recusou:

- "Plataforma de compliance rejeitou: …", na consulta;
- "Plataforma de compliance recusou: …", no envio.

No envio, o mock recusa como o sandbox: HTTP 400 com o ProblemDetails, e o motivo do `?motivo=` num campo do mapa
`errors`.

- **O motivo gravado:** é o resumo ("1 campo com erro: documento").
- **Onde fica o texto do `?motivo=`:** na foto da resposta, e o detalhe da nota o mostra na lista.

A omissão não entra no motivo da falha. Ela fica na foto da resposta do envio (`request.omissions`). Numa nota aceita,
continua no registro e aparece na tela como "Enviado com ressalvas".

**Foto da fonte.** O JSON canônico que a montagem hasheia fica no Blob, em
`traces/tenant-a/<período>/brmf|<voucher>/source.json`. A impressão gravada em `ProcessedDocuments.ContentHash`
é o SHA-256 desse arquivo. Diante de um "por que reenviou?", basta comparar duas fotos.

**Supressão de republicação.** O log da passada conta as referências suprimidas: pares (documento,
carimbo) já publicados, com o carimbo assentado (ADR-0025 §9). No roteiro acima ela não aparece, porque
nenhuma nota muda durante o teste e, depois da primeira passada, as notas de 2016 ficam fora da janela de
sobreposição. Ela aparece com nota sendo alterada no ERP. A repetição volta, por uma janela de
sobreposição, depois de reiniciar o host, trocar de réplica ou rebobinar a marca. O hash por conteúdo
continua impedindo o reenvio.

**Teste de integração da montagem contra o F&O real.** É o mesmo opt-in da seção 6: com as variáveis
definidas, o filtro `Integration` também monta as 5 notas 55 da `brmf`, duas vezes cada, e confere a
impressão.

**Recusa da credencial, ao vivo.** O mock também simula a recusa do endpoint de token:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5100/admin/token/recusar   # 400 {"error":"client_id invalid"}
Invoke-RestMethod -Method Post -Uri http://localhost:5100/admin/token/aceitar
```

Com a recusa, a nota é rejeitada com "Configuração do conector: a plataforma recusou a credencial do tenant 'tenant-a'
no ambiente 'sandbox' (HTTP 400: client_id invalid). Confira o Client ID e o Client Secret na tela de conectores.", e
nenhum documento é enviado. É a forma do sandbox: HTTP 400 com `{"error": "<texto livre>"}`, e o texto não aponta o campo
certo (um `client_secret` errado volta como "client_id invalid"). Por isso a mensagem manda conferir o Client ID **e** o
Client Secret (ADR-0027 §3). A recusa fica lembrada por 5
minutos, para não martelar o login: as notas seguintes falham com o mesmo motivo sem pedir token. **Salvar o perfil na
tela** (mesmo sem mudar nada) esquece a recusa na hora, e a próxima nota pede token de novo.

**A resposta da plataforma** é a quarta foto (ADR-0027): `avalara.response.submit.json` e
`avalara.response.status.json`, ao lado das outras três, no `/trace`, no zip e na aba **Resposta** do detalhe do
documento. É um envelope com o status, a URL sem query, alguns cabeçalhos e o corpo, já redigido: sem token, sem
segredo e sem `Bearer` com valor. O campo `redactions` conta o que foi redigido.

### A integração manual e o agendamento contra o D365 (ADR-0032, ADR-0034)

A tela **Agendamento** (a integração manual e os agendamentos) lê o ERP do tenant, e não mais o `companies.json`.

- **O dropdown de empresas** vem do cadastro de estabelecimentos do F&O (`FiscalEstablishments`, a entidade padrão da
  Microsoft), filtrado pelas `companies` do perfil. Os estabelecimentos com a mesma **raiz** do CNPJ (os 8 primeiros
  caracteres) são filiais da mesma empresa (ADR-0034). A empresa aparece pelo CNPJ completo da matriz, o de ordem `0001`
  (sem ela, o de menor ordem), e cada filial pelo CNPJ dela, com o código ao lado. Na `brmf`, é uma empresa só,
  "44.278.225/0001-80 — Contoso Entertainment System Brazil", com quatro filiais:

  | Filial, como aparece | Código gravado |
  |---|---|
  | `44.278.225/0001-80 — Matriz` | `Matriz` |
  | `44.278.225/0034-48 — RJ-01`, que não tem nota | `RJ-01` |
  | `44.278.225/0003-41 — SAL-01` | `SAL-01` |
  | `44.278.225/0002-60 — SP-01` | `SP-01` |

- **O que se grava é o que se vê:** a empresa é gravada com o CNPJ da matriz (`44278225000180`), sem máscara, e a
  filial, pelo código.
- **"Todas as filiais"** é a empresa inteira: a descoberta traz as notas de todos os estabelecimentos da raiz.
- **A integração imediata reenvia.** Ela usa o gatilho manual, que fura a idempotência de propósito (ADR-0015): as NF-e
  já confirmadas vão de novo ao destino ativo do tenant. A agendada, única ou diária, não reenvia o que já foi enviado sem
  alteração. Para testar a imediata sem mandar nada à plataforma real, aponte a saída para o mock antes.

- **O deploy da role vem antes.** A role `FSFiscalHubIntegration` precisa do privilégio padrão
  `FiscalEstablishmentEntityView`, com build e deploy do modelo (sem sync). Sem ele, o dropdown mostra o motivo do 403: "O
  F&O negou a leitura do cadastro de estabelecimentos (HTTP 403). A role FSFiscalHubIntegration precisa do privilégio
  FiscalEstablishmentEntityView…".
- **A descoberta** lê a `FSFiscalDocumentBRs` pelo **dia fiscal** do período e pelos estabelecimentos da empresa, ou só
  pelo da filial escolhida. O agendamento diário (D-1) traz as notas com data fiscal de ontem. Todos os modelos entram, e
  a NFS-e e o CT-e viram "ignorado" no roteamento, como no coletor.
- **A fila é a do coletor** (`documents-discovered`). A nota descoberta pelo agendamento cai no mesmo registro e no mesmo
  grupo que o coletor produz, sem linha duplicada. A NFS-e ignorada mantém o modo `Automatic` de quando o coletor a
  registrou.
- **O reprocesso** de uma nota do D365 com falha (o botão "Reprocessar" do detalhe) acha a nota pela chave
  (`brmf|<voucher>`) e a reenfileira com o gatilho manual. É envio real à plataforma.
- **Em Development, o tenant cujo ERP não tem diretório** (o tenant-b, do `iScala`) continua vendo o `companies.json` e o
  catálogo dos XMLs de exemplo, e a nota de exemplo continua reprocessável. Isso entra pelo código, sob `IsDevelopment()`,
  sem passo manual. O tenant-a, do `Dynamics365`, nunca vê o mock. Fora de Development, o mock não existe: o ERP sem
  diretório responde "não tem diretório de empresas no hub".

## 8. Sandbox da plataforma (ADR-0027)

**Expectativa honesta.** O primeiro envio real provavelmente volta rejeitado, e é isso que se quer medir. As 5 notas são
da Contoso de demonstração, de 2016, sem IBS/CBS. Corrigir o payload a partir das respostas é a próxima fatia.

**A credencial do sandbox** é distribuída fora do repositório e fora do chat, pelo canal de segredos da equipe. Ela é
digitada só na tela. Nunca vai para arquivo do repositório, terminal compartilhado, print ou mensagem.

**1. Configurar pela tela, com a guarda antes.** A tela mostra só alguns campos, e salvar regrava as settings inteiras.
Antes de confiar nela, prove que ela não apaga o que não mostra: os `establishments`, as `companies` e o `poll`. Sem os
`establishments`, as entradas que sobrepõem a plataforma somem sem aviso, e a nota passa a ir com os códigos da listagem
(ADR-0033); sem o `poll`, o feed do D365 para de descobrir notas.

a) **Antes de salvar pela tela,** guarde a resposta do `GET /connector`. Ela nunca traz segredo nem referência, então
   pode ir para um arquivo, mas fora do repositório:

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5200/auth/login -ContentType application/json `
  -Body '{"email":"admin@fiscalhub.local","password":"Fiscal@123"}'
$auth = @{ Authorization = "Bearer $($login.token)" }
$antes = Invoke-RestMethod -Uri http://localhost:5200/connector -Headers $auth
$antes | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 "$env:TEMP\connector-antes.json"
```

b) **Salve pela tela.** Em **Configurações → Conectores → Saída**, na seção **Sandbox** do tenant-a: a URL base do
   sandbox, o Client ID e o Client Secret. A URL do token não está na tela: o hub a monta pela URL base mais
   `oauth/token`. Se o sandbox exigir outra, ela vai no `tokenUrl` da seção, por SQL.

c) **Faça o `GET` de novo e compare** o que tem de sobreviver:

```powershell
$depois = Invoke-RestMethod -Uri http://localhost:5200/connector -Headers $auth
$depois | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 "$env:TEMP\connector-depois.json"
$in0 = $antes.inboundSettings | ConvertFrom-Json;   $in1 = $depois.inboundSettings | ConvertFrom-Json
$out0 = $antes.outboundSettings | ConvertFrom-Json; $out1 = $depois.outboundSettings | ConvertFrom-Json
$itens = @(
  @('inbound.companies', $in0.companies, $in1.companies),
  @('inbound.poll', $in0.poll, $in1.poll),
  @('outbound.sandbox.establishments', $out0.sandbox.establishments, $out1.sandbox.establishments),
  @('outbound.production.establishments', $out0.production.establishments, $out1.production.establishments)
)
foreach ($i in $itens) {
  $a = ConvertTo-Json -InputObject $i[1] -Depth 10 -Compress
  $b = ConvertTo-Json -InputObject $i[2] -Depth 10 -Compress
  if ($a -eq $b) { "ok     $($i[0])" } else { "MUDOU  $($i[0])`n  antes:  $a`n  depois: $b" }
}
```

Os quatro têm de sair `ok`. A única diferença admitida é no `inbound.poll`, e só no `enabled`, se o interruptor
**Integração automática** foi mexido nesse mesmo salvar. **Se algum sumir ou mudar, o teste para aqui:** o problema é da tela de conectores (ou do
`PUT /connector`), e não da Avalara. Não siga para a premissa de autenticação; restaure o perfil pelo arquivo de antes (ou
pelo SQL da seção 3) e registre o achado.

d) **Confira o caminho do segredo:**

- a tela mostra "configurado em <data>", e o `GET /connector` não traz o valor, nem parte dele, nem a referência (no
  `$depois`, o `secrets."outbound.sandbox.clientSecret"` tem `configured: true` e a data);
- a linha do perfil no SQL tem só o `clientSecretRef` `kv:fh-tenant-a--outbound--sandbox--clientsecret`;
- um `PUT /connector` feito à mão com `clientSecretRef` no corpo dá 400.

e) **Teste a credencial pela tela.** Em **Configurações**, a seção **Sandbox** e a **Produção** da Avalara, e a aba do
   ERP (`Dynamics365`), têm o botão **Testar credencial**.
   - **O que ele testa:** a credencial **gravada**, lida do cofre no servidor. Com uma edição pendente, ele pede para
     salvar antes.
   - **O token é sempre novo,** nunca o do cache. Um teste que reusasse o cache passaria com um segredo já revogado.
   - **Na Avalara:** para no token. A permissão só aparece no primeiro envio.
   - **No D365:** pega um token do Entra ID e lê a `FSFiscalDocumentBRs` com `$top=1`, porque o token prova a
     credencial, e não a permissão. A leitura vazia não prova o acesso às empresas.
   - **A tela:** mostra só "Credenciais e conexão válidas" ou "Credenciais ou ambiente inválidos". Com a plataforma fora
     do ar, mostra "Não foi possível conectar agora…". Nunca token nem segredo.
   - **O motivo detalhado:** fica no log do host, na linha `Teste de credencial do tenant … (Dynamics365 entrada): Refused.
     O Entra ID recusou a credencial (AADSTS7000215)…`. Ela traz o código AADSTS, o status HTTP ou o campo que falta.
   - **O freio:** um teste recusado fica lembrado por 5 minutos, por tenant e por adapter, e na Avalara por ambiente.
     Nesse intervalo, o clique devolve o motivo lembrado sem ir à plataforma. Salvar o perfil libera. Um teste da
     Avalara que dá certo também libera o envio, sem esperar a recusa lembrada.

**2. Verificar a premissa de autenticação.** O hub manda OAuth `client_credentials` com corpo JSON, na forma da coleção
do Postman do cliente (ADR-0027 §3). O resto do contrato de token (os nomes da resposta, a validade, o caminho do endpoint)
ainda é suposto. Antes das notas, rode a sonda (seção 9):

```powershell
dotnet run --project tools/AvalaraSandboxProbe -- token --tenant tenant-a
```

Ela diz se obteve o token, os campos da resposta e o `expires_in`, e nunca imprime o token. No primeiro envio, confira
que o caminho de envio existe. Se ele não existir, a nota é rejeitada na hora, sem retentativa, com "o caminho de envio
não existe nessa URL", e o motivo aponta as duas partes da URL: a URL base do sandbox, na tela, e o
`Avalara:DocumentsPath`, no `appsettings`. O caminho certo entra ali, sem código, e a nota é reprocessada. Um fluxo
estruturalmente outro (escopo ou audiência obrigatórios, token por empresa, mTLS) para o teste.

**3. Corrigir pela tela.** Com um Client Secret errado de propósito, a primeira nota é rejeitada com o motivo da
plataforma. Salve o certo na tela e reprocesse: o token é pedido na hora, sem esperar os 5 minutos da recusa lembrada.

**4. As 5 NF-e 55,** como na seção 7, com o sandbox no lugar do mock. Confira no dashboard o desfecho e o motivo de cada
uma, e a aba Resposta.

**5. Conferir o zip** de cada nota: `source.json`, `domain.json`, `avalara.json`, `avalara.response.submit.json` e
`avalara.response.status.json`. Anote toda foto com `redactions > 0`: é sinal de que a plataforma ecoou algo sensível.
Confira também que o log do host não tem token, segredo nem valor de cabeçalho.

> **Espere o poll fechar antes de trocar de ambiente.** A consulta de status usa a seção do ambiente **ativo**. Um
> documento enviado ao sandbox e ainda em consulta, depois da troca para Production, seria consultado no endereço e
> com a credencial de produção.

## 9. Sonda do sandbox (`tools/AvalaraSandboxProbe`)

É a ferramenta do teste manual e do experimento do campo omitido, e não a esteira (ADR-0027 §9). Usa a configuração do
Host (o banco de dev, o cofre e a seção `Avalara`), o perfil do tenant e o segredo gravado pela tela. Só **lê**: nunca
grava no cofre nem no banco. Tudo o que grava em `tools/AvalaraSandboxProbe/out/` sai redigido, e a pasta está no
`.gitignore`.

```powershell
# o token: se obteve, os campos e o expires_in (o token nunca é impresso)
dotnet run --project tools/AvalaraSandboxProbe -- token --tenant tenant-a

# uma variante do payload de destino (o avalara.json do zip), com a referência marcada
dotnet run --project tools/AvalaraSandboxProbe -- send --tenant tenant-a --payload .\avalara.json --label exp-a `
  --omit finalidadeNotaFiscal --ref-suffix exp-a --poll
dotnet run --project tools/AvalaraSandboxProbe -- send --tenant tenant-a --payload .\avalara.json --label exp-b `
  --set finalidadeNotaFiscal=1 --ref-suffix exp-b --poll

# a leitura de volta, se a plataforma a oferecer
dotnet run --project tools/AvalaraSandboxProbe -- get --tenant tenant-a --id <id> --label exp-a

# a listagem de estabelecimentos inteira, e o casamento de um CNPJ, sem mandar documento
dotnet run --project tools/AvalaraSandboxProbe -- listing --tenant tenant-a --cnpj <cnpj de um contribuinte da conta>
```

- `listing` roda a listagem real do adapter pelo resolvedor, como o despacho a pede, com as `AvalaraOptions` do host
  (inclusive o `Avalara:ListingPageSize`). Imprime a linha de log da listagem, com as empresas, os contribuintes e as
  páginas de cada endpoint, e, para cada `--cnpj`, o casamento: os códigos e o `#id` do contribuinte único, nenhum, ou os
  candidatos da duplicidade. Nenhum outro conteúdo da listagem é impresso, e nada é gravado em `out/`. A sobreposição
  `establishments` do perfil não entra: ela só vale no envio. Uma recusa da listagem sai com o motivo, como no envio.
- `listing --ids` imprime só os identificadores da conta: os `empresaId` e os `contribuinteId`, sem CNPJ, código ou
  descrição. É a lista de `Fixtures/sandbox/identificadores-da-conta.json`, que o `SandboxFixtureTests` usa para recusar
  valor real nas fixtures inventadas e no mock. A varredura só vê o que a conta tinha na data da gravação, e a falha dela
  mostra essa data (`recordedAt`): com a lista velha, regrave o arquivo com a saída deste comando. Um identificador
  inventado novo sai da faixa reservada, que não depende da gravação (`Fixtures/listing/README.md`).

- `--omit` e `--set` mexem só no topo do payload, e podem repetir. O valor do `--set` é JSON quando dá (`1`, `true`,
  `null`), e texto nos outros casos.
- `--ref-suffix` acrescenta `#<sufixo>` ao `codigoReferenciaIntegracao`, para as variantes não colidirem entre si.
- Cada comando grava o envelope redigido: `out/token.json`, `out/<label>.payload.json`, `out/<label>.submit.json`,
  `out/<label>.status.json` e `out/<label>.readback.json`. Só o que for curado para evidência sai de lá.
- No `out/token.json`, o `access_token` e o `refresh_token` saem como `[redigido]`, e a sessão, a conta, o login e o
  e-mail (`sessionId`, `userId`, `subId`, `appId`, `login`, `email`) como `[mascarado]`. O `token_type` e o `expires_in` ficam: é o que a
  evidência precisa mostrar.
- Contra o mock, a sonda funciona igual: é o jeito de ensaiar o roteiro sem o sandbox.

## 10. Limpar a base de demonstração

O seed de dev tem duas partes:

- **usuários, tenants e perfis de conector:** semeados sempre, porque sem eles não há login nem credencial;
- **a demonstração** (notas com fotos, execuções e agendamentos, para a paginação e os KPIs): **opt-in**, só com
  `Seed:DemoData = true`. Sem a chave, vale `false`: um ambiente não ganha documentos, execuções e agendamentos falsos
  por padrão. O `appsettings.Development.json` traz a chave explícita, em `false`.

Cada parte só semeia a tabela vazia. Com a demonstração ligada, limpar a base e subir o host trazia tudo de volta, e
parecia que a limpeza tinha falhado. Desligada, a base limpa continua limpa.

Para apagar os dados de demonstração (e o que a esteira gravou) de uma base que já os tem:

```powershell
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "DELETE FROM ProcessedDocuments; DELETE FROM IntegrationExecutions; DELETE FROM ScheduledIntegrations;"
```

**`ConnectorProfiles`, `Users` e `Tenants` NÃO entram nessa limpeza.** Apagar `ConnectorProfiles` perde a configuração dos
conectores (as URLs, os `establishments`, o `poll` e as referências dos segredos gravados na tela); apagar `Users` ou
`Tenants` tira o login. O seed os recria na próxima subida, mas com os valores de fábrica, e os segredos do cofre
deixam de ter quem os referencie.

- **As fotos de demonstração** no Blob (container `traces`) ficam. Sem a linha no SQL, elas não aparecem no dashboard.
- **Os XMLs de exemplo** (`nfe/tenant-a/…`) continuam sendo enviados ao Blob na subida: são a entrada do `/ingest`, e não
  dado de demonstração.
- **Para ter a demonstração de volta,** com as três tabelas vazias, suba o host uma vez com `$env:Seed__DemoData = "true"`.

## Notas

- **Idempotência:** repetir o `POST /ingest` com o mesmo `naturalKey` não duplica nem reenvia
  (a esteira vê que já foi enviado). Use um `naturalKey` novo para processar de novo.
- As connection strings em `appsettings.json` são **de dev local** (mesma senha do `docker-compose`).
  Em produção, os segredos vêm de Key Vault.
- **Os segredos de conector** nunca estão em arquivo: o valor vai da tela para o cofre (em dev, o emulador em memória),
  e o banco guarda só a referência. Reiniciar o `keyvault` apaga os valores; a tela mostra "não configurado".

## Derrubar

```powershell
docker compose down     # para os containers (–v também apaga os volumes)
```

E feche os dois `dotnet run`.
