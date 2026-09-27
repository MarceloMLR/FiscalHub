# Rodando localmente

O FiscalHub roda de ponta a ponta na sua máquina, sem Azure — com equivalentes locais de cada
serviço da nuvem (Azure "sem Azure").

| Azure | Local |
|-------|-------|
| Blob Storage | Azurite (container) |
| Azure SQL | SQL Server (container) |
| Avalara (compliance) | mock em `tools/MockComplianceApi` (o sandbox real é a próxima fatia) |

## Pré-requisitos

- SDK do **.NET 10**
- **Docker Desktop** rodando

## 1. Subir a infra (Blob + SQL)

Na raiz do repositório:

```powershell
docker compose up -d
```

Sobe dois containers: `azurite` (Blob nas portas 10000/10001) e `sql` (SQL Server na 1433).
Conferir: `docker compose ps` (ambos `running`).

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
arquivo das `InboundSettings` (seção 6):

```powershell
$out = '{"sandbox":{"baseUrl":"http://localhost:5100/","clientSecretRef":"kv:avalara-a-sandbox-secret","clientTokenRef":"kv:avalara-a-sandbox-token","establishments":{"44278225000180":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"},"12345678000190":{"codigoEmpresa":"20247332000182","codigoContribuinte":"20247332000182"}}},"production":{"baseUrl":"https://api.avalara.com/","clientSecretRef":"kv:avalara-a-prod-secret","clientTokenRef":"kv:avalara-a-prod-token","establishments":{}}}'
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
$settings = '{"url":"https://fiscosysdev.operations.dynamics.com","companies":["brmf"],"pageSize":20,"auth":{"tenantId":"","clientId":"","clientSecretRef":"kv:d365-a-secret"},"poll":{"enabled":true,"intervalSeconds":60,"overlapSeconds":300,"startFrom":"2015-01-01T00:00:00Z"}}'
"UPDATE ConnectorProfiles SET InboundSettings = N'$settings' WHERE TenantId = 'tenant-a';" | Set-Content -Encoding ascii enable-poll.sql
docker cp enable-poll.sql fiscalhub-sql-1:/tmp/enable-poll.sql
docker exec fiscalhub-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Local_Dev_123!" -C -d FiscalHub -i /tmp/enable-poll.sql
```

O SQL vai por arquivo porque o PowerShell 5.1 quebra as aspas do JSON quando ele é passado direto no `-Q`.

Dá para usar o `PUT /connector` no lugar do SQL, mas ele regrava o perfil inteiro e não carrega o
`SupportAdapter`: a configuração de chamados do tenant se perde.

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

> **O mock aceita tudo.** "5 enviadas" contra o mock prova o caminho e o contrato montado, e não a aceitação da
> Avalara. O envio ao sandbox real (credenciais, token por tenant, `BaseUrl`) é a próxima fatia. Até lá, os
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

## Notas

- **Idempotência:** repetir o `POST /ingest` com o mesmo `naturalKey` não duplica nem reenvia
  (a esteira vê que já foi enviado). Use um `naturalKey` novo para processar de novo.
- As connection strings em `appsettings.json` são **de dev local** (mesma senha do `docker-compose`).
  Em produção, os segredos vêm de Key Vault.

## Derrubar

```powershell
docker compose down     # para os containers (–v também apaga os volumes)
```

E feche os dois `dotnet run`.
