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
docker compose up -d
```

Sobe `azurite` (Blob nas portas 10000/10001), `sql` (SQL Server na 1433), `servicebus` (AMQP na 5672) e `keyvault`
(a API do Key Vault na 8443). Conferir: `docker compose ps` (todos `running`).

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

## 3. Rodar o host

Em **outro** terminal:

```powershell
dotnet run --project src/FiscalHub.Host --urls http://localhost:5200
```

No startup o host cria o schema no SQL e sobe os XMLs de NF-e de exemplo no Blob, no espaço de entrada do
tenant-a (`nfe/tenant-a/nfe-exemplo.xml` e `nfe/tenant-a/nfe-exemplo-2.xml`). A rota `GET http://localhost:5200/` mostra
que está no ar.

### O Client Secret, pela tela

Todo envio leva o token da credencial do tenant, no ambiente ativo (ADR-0027). Não há modo "sem autenticação" no host.
A credencial vem das `OutboundSettings` (`baseUrl`, `tokenUrl` opcional, `clientId`), e o **segredo é digitado na
tela**, até contra o mock:

1. Abra o dashboard, entre como `admin@fiscalhub.local` e vá em **Configurações → Conectores → Saída (compliance)**.
2. Na seção **Sandbox**, digite qualquer valor no **Client Secret** (o mock aceita qualquer um não vazio) e salve.
3. O campo volta vazio, com "configurado em <data>". O valor foi para o cofre, e o perfil guarda só a referência
   `kv:fh-tenant-a--outbound--sandbox--clientsecret`. O `GET /connector` nunca devolve o valor.

Sem isso, o envio é rejeitado com "Configuração do conector: o Client Secret do ambiente 'sandbox' do tenant 'tenant-a'
não está configurado … Configure em Configurações → Conectores → Avalara → Sandbox → Client Secret." Depois de reiniciar
o emulador, digite de novo.

### Tradução dos estabelecimentos (banco já existente)

Todo envio precisa de `codigoEmpresa` e `codigoContribuinte`. Eles **não vêm do ERP**: vêm da tabela
`establishments` das `OutboundSettings` do perfil do tenant, por ambiente, com o CNPJ do estabelecimento próprio
como chave (ADR-0026). Sem ela, o envio é rejeitado com um motivo que diz exatamente o que falta ("Configuração do
conector: …").

O seed de um banco novo já traz, no `sandbox` do tenant-a:

- `44278225000180`, a Contoso do D365 (`brmf`);
- `12345678000190`, o tenant-a dos XMLs de exemplo.

Os dois apontam para `20247332000182`, a empresa do JSON real do ambiente Avalara de teste.

**Num banco criado antes desta mudança,** o seed não roda de novo. Aplique à mão, pelo mesmo padrão de SQL por
arquivo das `InboundSettings` (seção 6). As referências seguem o nome que o servidor deriva (`fh-{tenant}--…`, ADR-0027),
e o `clientTokenRef` não existe mais. O SQL grava só a referência: o valor continua sendo digitado na tela.

```powershell
$out = '{"sandbox":{"baseUrl":"http://localhost:5100/","clientId":"mock-client","clientSecretRef":"kv:fh-tenant-a--outbound--sandbox--clientsecret","establishments":{"44278225000180":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"},"12345678000190":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"}}},"production":{"baseUrl":"https://api.avalara.com/","clientId":"","clientSecretRef":"kv:fh-tenant-a--outbound--production--clientsecret","establishments":{}}}'
"UPDATE ConnectorProfiles SET OutboundSettings = N'$out' WHERE TenantId = 'tenant-a';" | Set-Content -Encoding ascii set-outbound.sql
docker cp set-outbound.sql fiscalhub-sql-1:/tmp/set-outbound.sql
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub -i /tmp/set-outbound.sql
```

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

O XML também depende da tradução dos estabelecimentos. O XML não diz qual parte é a do tenant, e quem diz é a
tabela: o emitente `12345678000190` está nela, então é o estabelecimento próprio, e o destinatário é o parceiro.

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
- `az login` com um usuário que tenha acesso ao `fiscosysdev`. Em Development, o host usa o token da
  sessão do Azure CLI, porque a app registration ainda não existe.
- Host rodando em Development. É o padrão do `dotnet run`, pelo `launchSettings.json`.

**1. Ligar o poll do tenant-a** com página de 20, para exercitar 5 páginas por keyset, e marca inicial
em 2015:

```powershell
$settings = '{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"],"pageSize":20,"auth":{"tenantId":"","clientId":"","clientSecretRef":"kv:fh-tenant-a--inbound--auth--clientsecret"},"poll":{"enabled":true,"intervalSeconds":60,"overlapSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}}'
"UPDATE ConnectorProfiles SET InboundSettings = N'$settings' WHERE TenantId = 'tenant-a';" | Set-Content -Encoding ascii enable-poll.sql
docker cp enable-poll.sql fiscalhub-sql-1:/tmp/enable-poll.sql
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub -i /tmp/enable-poll.sql
```

O SQL vai por arquivo porque o PowerShell 5.1 quebra as aspas do JSON quando ele é passado direto no `-Q`.

Dá para usar o `PUT /connector` no lugar do SQL. As settings enviadas substituem as gravadas, com duas exceções: o
segredo ausente mantém a referência que já estava, e os chamados ausentes do corpo ficam como estavam. Um `*Ref` no
corpo é recusado com 400, porque a referência é do servidor.

**2. Rodar o host** (seção 3). Em até 15 segundos aparece no log:

```
Feed de mudanças: 1 tenant(s) consultado(s), 14 referência(s) na fila de descoberta, 0 suprimida(s) por já publicadas.
```

Junto vêm **69 avisos** `Modelo '01' fora do mapa…`. Isso é esperado. Os 83 cabeçalhos da brmf têm
modelo `01` (69), `SE` (9) e `55` (5), e o mapa padrão cobre `55`, `57` e `SE`. As notas modelo `01`
(Nota Fiscal 1/1A, formulário em papel) são dado de demonstração antigo, e a recomendação é mantê-las
fora do mapa (ADR-0024). Para exercitar mais referências no teste, dá para incluir o `01` no
`modelTypes` das settings.

**3. Conferir o cursor.** A marca deve estar perto de agora: é o relógio do F&O no fim da leitura.

```powershell
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "SELECT TenantId, Origin, DATEADD(SECOND, (WatermarkTicks - 621355968000000000) / 10000000, '1970-01-01') AS Marca, DATEADD(SECOND, (LastPolledTicks - 621355968000000000) / 10000000, '1970-01-01') AS UltimoPoll, ConsecutiveFailures, LastError FROM ChangeFeedCursors;"
```

**4. Segunda passada.** Um minuto depois, a próxima passada lê só a janela de sobreposição (5 minutos
antes da marca) e não reenfileira nada antigo.

**Rebobinar**, para repetir o teste:

```powershell
# apaga o cursor: o startFrom volta a valer na próxima passada
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub `
  -Q "DELETE FROM ChangeFeedCursors WHERE TenantId = 'tenant-a';"
# ou aponta a marca direto para 2015-01-01T00:00:00Z (em ticks)
#   UPDATE ChangeFeedCursors SET WatermarkTicks = 635556672000000000 WHERE TenantId = 'tenant-a';
```

**Desligar:** `poll.enabled = false` nas settings; vale na próxima passada.

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

Se a nota tinha observação de omissão, ela vem depois do motivo, separada por ` | `.

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
Invoke-RestMethod -Method Post -Uri http://localhost:5100/admin/token/recusar   # 401 invalid_client
Invoke-RestMethod -Method Post -Uri http://localhost:5100/admin/token/aceitar
```

Com a recusa, a nota é rejeitada com "Configuração do conector: a plataforma recusou a credencial do tenant 'tenant-a'
no ambiente 'sandbox' (HTTP 401: invalid_client — …)", e nenhum documento é enviado. A recusa fica lembrada por 5
minutos, para não martelar o login: as notas seguintes falham com o mesmo motivo sem pedir token. **Salvar o perfil na
tela** (mesmo sem mudar nada) esquece a recusa na hora, e a próxima nota pede token de novo.

**A resposta da plataforma** é a quarta foto (ADR-0027): `avalara.response.submit.json` e
`avalara.response.status.json`, ao lado das outras três, no `/trace`, no zip e na aba **Resposta** do detalhe do
documento. É um envelope com o status, a URL sem query, alguns cabeçalhos e o corpo, já redigido: sem token, sem
segredo e sem `Bearer` com valor. O campo `redactions` conta o que foi redigido.

## 8. Sandbox da plataforma (ADR-0027)

**Expectativa honesta.** O primeiro envio real provavelmente volta rejeitado, e é isso que se quer medir. As 5 notas são
da Contoso de demonstração, de 2016, sem IBS/CBS. Corrigir o payload a partir das respostas é a próxima fatia.

**A credencial do sandbox** é distribuída fora do repositório e fora do chat, pelo canal de segredos da equipe. Ela é
digitada só na tela. Nunca vai para arquivo do repositório, terminal compartilhado, print ou mensagem.

**1. Configurar pela tela, com a guarda antes.** A tela mostra só alguns campos, e salvar regrava as settings inteiras.
Antes de confiar nela, prove que ela não apaga o que não mostra: os `establishments`, as `companies` e o `poll`. Sem os
`establishments`, toda nota é rejeitada por falta de tradução; sem o `poll`, o feed do D365 para de descobrir notas.

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
   sandbox, a URL do token (se a documentação do sandbox der uma diferente de URL base + `oauth/token`), o Client ID e o
   Client Secret.

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

Os quatro têm de sair `ok`. **Se algum sumir ou mudar, o teste para aqui:** o problema é da tela de conectores (ou do
`PUT /connector`), e não da Avalara. Não siga para a premissa de autenticação; restaure o perfil pelo arquivo de antes (ou
pelo SQL da seção 3) e registre o achado.

d) **Confira o caminho do segredo:**

- a tela mostra "configurado em <data>", e o `GET /connector` não traz o valor, nem parte dele, nem a referência (no
  `$depois`, o `secrets."outbound.sandbox.clientSecret"` tem `configured: true` e a data);
- a linha do perfil no SQL tem só o `clientSecretRef` `kv:fh-tenant-a--outbound--sandbox--clientsecret`;
- um `PUT /connector` feito à mão com `clientSecretRef` no corpo dá 400.

**2. Verificar a premissa de autenticação.** O hub assume OAuth `client_credentials`, com o segredo no corpo do
formulário (ADR-0027 §3). Antes das notas, rode a sonda (seção 9):

```powershell
dotnet run --project tools/AvalaraSandboxProbe -- token --tenant tenant-a
```

Ela diz se obteve o token, os campos da resposta e o `expires_in`, e nunca imprime o token. No primeiro envio, confira
que o caminho de envio existe (qualquer status diferente de 404). Um ajuste de forma entra por configuração ou com teste:
o caminho certo vai em `Avalara:DocumentsPath` (ou `Avalara:TokenPath`) no `appsettings`, sem código. Um fluxo
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
```

- `--omit` e `--set` mexem só no topo do payload, e podem repetir. O valor do `--set` é JSON quando dá (`1`, `true`,
  `null`), e texto nos outros casos.
- `--ref-suffix` acrescenta `#<sufixo>` ao `codigoReferenciaIntegracao`, para as variantes não colidirem entre si.
- Cada comando grava o envelope redigido: `out/token.json`, `out/<label>.payload.json`, `out/<label>.submit.json`,
  `out/<label>.status.json` e `out/<label>.readback.json`. Só o que for curado para evidência sai de lá.
- Contra o mock, a sonda funciona igual: é o jeito de ensaiar o roteiro sem o sandbox.

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
