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
