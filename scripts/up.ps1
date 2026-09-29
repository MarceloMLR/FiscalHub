#Requires -Version 5.1
<#
.SYNOPSIS
    Sobe a infra local do FiscalHub (Blob, SQL, fila e cofre).

.DESCRIPTION
    Substitui o `docker compose up -d` cru, por um motivo concreto.

    O emulador do Service Bus nao tem armazenamento proprio: ele cria as bases dele
    (SbGatewayDatabase e SbMessageContainerDatabase00001) DENTRO do container de SQL,
    e na subida derruba e recria essas bases. Quando os dois containers morrem juntos
    -- reinicio da maquina, por exemplo -- o SQL pode parar no meio de um DROP: a base
    sai do catalogo e os arquivos .mdf/.ldf ficam no disco.

    Na subida seguinte o emulador pergunta se a base existe, ouve que nao, pula o drop,
    tenta criar e bate em "Cannot create file ... because it already exists". Sai com
    codigo 139 e nunca se recupera sozinho, porque o estado orfao nao e "existe" nem
    "nao existe".

    Este script sobe o SQL primeiro, remove as bases do emulador (catalogo E arquivos)
    e so entao sobe o Service Bus. Limpar nao perde nada: o emulador recria essas bases
    em toda subida, por conta propria. O que ele nao sabe fazer e apagar arquivo de uma
    base que, para o SQL, nao existe mais.

    Nota de implementacao: o docker escreve progresso no stderr, e no PowerShell 5.1
    isso vira erro terminante se ErrorActionPreference for Stop. Por isso os erros sao
    conferidos por $LASTEXITCODE, e nao por excecao.

.EXAMPLE
    .\scripts\up.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
$sqlc = "/opt/mssql-tools18/bin/sqlcmd"
$conn = @("-S","localhost","-U","sa","-P","Local_Dev_123!","-C")

function Etapa($t) { Write-Host "`n== $t" -ForegroundColor Cyan }
function Parar($t) { Write-Host "`n$t" -ForegroundColor Red; Pop-Location; exit 1 }

Push-Location $repo

Etapa "Subindo Blob, SQL e cofre"
docker compose up -d azurite sql keyvault 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Parar "falhou o 'docker compose up' do Blob/SQL/cofre. O Docker Desktop esta rodando?" }

Etapa "Esperando o SQL aceitar conexao"
$pronto = $false
foreach ($i in 1..40) {
    docker exec fiscalhub-sql-1 $sqlc @conn -Q "SELECT 1" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $pronto = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $pronto) { Parar "o SQL nao respondeu em 80s. Veja: docker logs fiscalhub-sql-1 --tail 40" }
Write-Host "   SQL pronto"

Etapa "Limpando as bases do emulador do Service Bus"
$limpeza = @"
IF DB_ID('SbGatewayDatabase') IS NOT NULL
BEGIN ALTER DATABASE SbGatewayDatabase SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE SbGatewayDatabase; END
IF DB_ID('SbMessageContainerDatabase00001') IS NOT NULL
BEGIN ALTER DATABASE SbMessageContainerDatabase00001 SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE SbMessageContainerDatabase00001; END
"@
$tmp = Join-Path $env:TEMP "fh-limpa-sb.sql"
Set-Content -Path $tmp -Value $limpeza -Encoding ascii
docker cp $tmp fiscalhub-sql-1:/tmp/fh-limpa-sb.sql 2>&1 | Out-Null
docker exec fiscalhub-sql-1 $sqlc @conn -i /tmp/fh-limpa-sb.sql 2>&1 | Out-Null
docker exec -u 0 fiscalhub-sql-1 bash -lc "rm -f /var/opt/mssql/data/Sb*.mdf /var/opt/mssql/data/Sb*.ldf" 2>&1 | Out-Null
Write-Host "   bases e arquivos orfaos removidos (o emulador os recria na subida)"

Etapa "Subindo o Service Bus"
docker compose up -d servicebus 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Parar "falhou o 'docker compose up' do Service Bus." }
Write-Host "   esperando o emulador terminar de criar as bases (ate 90s)"
$fila = $false
foreach ($i in 1..18) {
    Start-Sleep -Seconds 5
    $st = (docker inspect -f "{{.State.Status}}" fiscalhub-servicebus-1 2>&1)
    if ($st -ne "running") { break }
    docker exec fiscalhub-sql-1 $sqlc @conn -Q "IF DB_ID('SbMessageContainerDatabase00001') IS NULL RAISERROR('ainda nao',16,1)" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $fila = $true; break }
}

Etapa "Conferindo"
$todosOk = $true
foreach ($n in @("azurite","sql","keyvault","servicebus")) {
    $c = "fiscalhub-$n-1"
    $st = (docker inspect -f "{{.State.Status}}" $c 2>&1)
    Write-Host ("   {0,-12} {1}" -f $n, $st)
    if ($st -ne "running") { $todosOk = $false }
}
if (-not $todosOk) {
    Write-Host "`nAlgum container nao subiu." -ForegroundColor Red
    Write-Host "Se foi o servicebus, veja o motivo com:  docker logs fiscalhub-servicebus-1 --tail 30"
    Pop-Location; exit 1
}
if (-not $fila) { Write-Host "   aviso: o emulador esta de pe mas as bases dele ainda nao apareceram; confira em alguns segundos" -ForegroundColor Yellow }

Write-Host "`nInfra no ar." -ForegroundColor Green
Write-Host "O cofre e em memoria: os segredos de conector (Avalara e D365) precisam ser digitados de novo na tela."
Pop-Location