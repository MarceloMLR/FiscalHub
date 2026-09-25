# Rodando localmente

O FiscalHub roda de ponta a ponta na sua máquina, sem Azure — com equivalentes locais de cada
serviço da nuvem (Azure "sem Azure").

| Azure | Local |
|-------|-------|
| Blob Storage | Azurite (container) |
| Azure SQL | SQL Server (container) |
| Avalara (compliance) | mock em `tools/MockComplianceApi` |

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

No startup o host cria o schema no SQL e sobe um XML de NF-e de exemplo no Blob
(`nfe/nfe-exemplo.xml`). A rota `GET http://localhost:5200/` mostra que está no ar.

## 4. Disparar a esteira

```powershell
$body = '{"tenantId":"tenant-a","naturalKey":"nfe-001","locator":"nfe/nfe-exemplo.xml"}'
Invoke-RestMethod -Method Post -Uri http://localhost:5200/ingest -Body $body -ContentType application/json
```

Isso faz o hub: ler o XML do Blob → validar → mapear e despachar pro mock → gravar o status no SQL.

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

Aí você vê o payload no formato da Avalara — inclusive o IBS/CBS da reforma no array `impostos[]`.

**O status pelo GUID:**

```powershell
Invoke-RestMethod -Uri http://localhost:5100/documents/<GUID>/status
```

## 6. Poll do D365 (fatia 1: descobrir e enfileirar)

O worker de feed de mudanças (ADR-0024) pergunta ao F&O o que mudou na `FSFiscalDocumentBRs`. Ele
consulta por janela de data em `SysModifiedDateTime`, pagina por keyset e usa um lease por tenant, e
publica cada referência na fila **`documents-discovered`**. Essa fila ainda não tem consumidor: a
montagem do documento é a próxima fatia.

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
Feed de mudanças: 1 tenant(s) consultado(s), 14 referência(s) na fila de descoberta.
```

Junto vêm **69 avisos** `Modelo '01' fora do mapa…`. Os 83 cabeçalhos da brmf têm modelo `01` (69),
`SE` (9) e `55` (5), e o mapa padrão cobre `55`, `57` e `SE`. Para o teste, dá para mapear o modelo 1
nas settings, por exemplo com `"modelTypes":{"55":"GoodsInvoice55","57":"Transport57","SE":"ServiceNfse","01":"GoodsInvoice55"}`.
O tratamento definitivo do modelo 1 é decisão do roteamento.

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

> **TTL de 1 hora no emulador.** A fila `documents-discovered` não tem consumidor nesta fatia, e as
> mensagens expiram em 1 hora (limite do emulador). A evidência do teste é o log da passada e o cursor
> no SQL.

**Teste de integração contra o F&O real.** Ele é opt-in e fica pulado sem a variável:

```powershell
$env:FISCALHUB_D365_URL = "https://fiscosysdev.operations.dynamics.com"
$env:FISCALHUB_D365_COMPANY = "brmf"
$env:FISCALHUB_D365_EXPECTED_ROWS = "83"
dotnet test tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests --filter "FullyQualifiedName~Integration"
```

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
