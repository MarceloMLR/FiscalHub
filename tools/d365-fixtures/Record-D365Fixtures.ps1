<#
.SYNOPSIS
    Grava, byte a byte, as respostas OData do D365 F&O que os testes da montagem usam como fixture.

.DESCRIPTION
    Design D15 da change add-d365-document-assembly. Só para o fiscosysdev (dado de demonstração):
    NUNCA aponte para ambiente de cliente — as respostas vão para o repositório.

    O que grava, em <OutputDir>:
      notes/<RecId>/          por nota modelo 55 da empresa: as 4 consultas exatas da montagem
                              (header, lines, taxes, charges) e a contábil do voucher (taxtrans).
      reference/              endereços e cidades referenciados pelos cabeçalhos das notas 55.
      scope/<RecId>/          cabeçalho (e linhas) das notas fora do escopo: canceladas e uma SE.
      snapshot/               base inteira da empresa: cabeçalhos, linhas, impostos, encargos e a
                              contábil dos vouchers fiscais (em blocos — a FSTaxTransBRs inteira tem
                              centenas de milhares de linhas de todas as empresas).
      directory/              o cadastro de estabelecimentos da empresa (FiscalEstablishments, a entidade
                              padrão da Microsoft) e, por estabelecimento com nota, a consulta da descoberta
                              por período no dia fiscal mais recente dele (change
                              erp-company-directory-and-card-filters, D1 e D4).

    Com -DirectoryOnly, grava só o directory/, sem regravar o resto.

    As respostas são salvas como vieram (Invoke-WebRequest -OutFile), sem reformatar. O $select de
    cada consulta de nota é o do design D5: o mesmo que o source do D365 pede, e o que entra no hash.

    A identidade é a do conector (change explicit-credential-and-execution-cnpj, D8): client credentials, com o app do
    conector, e nunca a sessão do Azure CLI. A resposta gravada é a que o conector recebe. A credencial vem de três
    variáveis de ambiente, as mesmas dos testes contra o F&O real, e fica na sessão: nunca em arquivo versionado, nem em
    parâmetro, que ficaria no histórico do shell. O segredo e o token nunca são impressos.

.EXAMPLE
    $env:FISCALHUB_D365_ENTRA_TENANT_ID = '<tenant do Entra do app>'
    $env:FISCALHUB_D365_CLIENT_ID = '<client id do app>'
    $env:FISCALHUB_D365_CLIENT_SECRET = '<client secret do app>'
    ./tools/d365-fixtures/Record-D365Fixtures.ps1 -EnvironmentUrl https://fiscosysdev.operations.dynamics.com -Company brmf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [Parameter(Mandatory = $true)][string]$Company,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\..\tests\Adapters\Ingress\FiscalHub.Adapters.Ingress.D365Poll.Tests\Fixtures\d365'),
    [switch]$DirectoryOnly
)

$ErrorActionPreference = 'Stop'

# $select do design D5 (add-d365-document-assembly), ampliado pelo D12 (connector-not-validator) e pelo D3
# (establishment-and-readable-dashboard, o FiscalEstablishment) — precisa bater com o D365GoodsInvoiceSource. O snapshot
# dos cabeçalhos também cobre os campos da descoberta (D365ChangeFeed.Select): data fiscal e estabelecimento.
$HeaderSelect = 'FiscalDocumentRecId,dataAreaId,Voucher,Model,Status,Direction,FiscalDocumentIssuer,AccessKey,FiscalDocumentSeries,FiscalDocumentNumber,FiscalDocumentDate,FiscalDocumentDateTime,FiscalEstablishment,FiscalEstablishmentCNPJCPF,FiscalEstablishmentName,FiscalEstablishmentIE,FiscalEstablishmentPostalAddress,ThirdPartyCNPJCPF,ThirdPartyName,ThirdPartyIE,ThirdPartyPostalAddress,TotalAmount,TotalGoodsAmount,AccountingDate'
$LineSelect = 'FiscalDocumentLineRecId,FiscalDocumentRecId,LineNum,ItemId,Description,FiscalClassification,CFOP,Quantity,UnitPrice,LineAmount,Unit,AccountingAmount,Origin'
$TaxSelect = 'FiscalDocumentTaxTransRecId,FiscalDocumentLineRecId,FiscalDocumentMiscChargeRecId,TaxTransRecId,FiscalTaxType,TaxationCode,TaxBaseAmount,TaxBaseAmountExempt,TaxBaseAmountOther,TaxValue,TaxAmount,RetainedTax'
$ChargeSelect = 'FiscalDocumentMiscChargeRecId,FiscalDocumentLineRecId,ChargeNum,MiscChargeType,Amount,Txt'
$TaxTransSelect = 'TaxTransRecId,Voucher,TaxType,TaxBaseAmount,TaxValue,TaxAmount'
$PostalAddressSelect = 'PostalAddressRecId,CityRecId,Street,StreetNumber,DistrictName,ZipCode'
$CitySelect = 'AddressCityRecId,IBGECode'
# O $select da descoberta (D365ChangeFeed.Select), que a descoberta por período reusa, e o do cadastro de estabelecimentos
# (change erp-company-directory-and-card-filters, D1). Precisam bater com o adapter.
$FeedSelect = 'dataAreaId,Voucher,Model,Direction,Status,FiscalDocumentNumber,FiscalDocumentSeries,FiscalDocumentDate,FiscalEstablishmentCNPJCPF,FiscalEstablishment,SysModifiedDateTime,FiscalDocumentRecId'
$EstablishmentSelect = 'dataAreaId,FiscalEstablishmentId,CNPJ,Name'

$base = $EnvironmentUrl.TrimEnd('/')

# A credencial do app do conector, por variável de ambiente: falta qualquer uma, a gravação nem começa.
$credentialVariables = 'FISCALHUB_D365_ENTRA_TENANT_ID', 'FISCALHUB_D365_CLIENT_ID', 'FISCALHUB_D365_CLIENT_SECRET'
$missing = @($credentialVariables | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    throw "Falta a credencial do app do conector: $($missing -join ', '). Defina $($credentialVariables -join ', ') (`$env:) antes de gravar."
}

# Client credentials no Entra ID, com o escopo do ambiente: o mesmo token que o conector pede.
$tokenResponse = Invoke-RestMethod -Method Post `
    -Uri "https://login.microsoftonline.com/$($env:FISCALHUB_D365_ENTRA_TENANT_ID)/oauth2/v2.0/token" `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body @{
        grant_type    = 'client_credentials'
        client_id     = $env:FISCALHUB_D365_CLIENT_ID
        client_secret = $env:FISCALHUB_D365_CLIENT_SECRET
        scope         = "$base/.default"
    }
if (-not $tokenResponse.access_token) { throw 'O Entra ID respondeu sem access_token para o app do conector.' }
$headers = @{ Authorization = "Bearer $($tokenResponse.access_token)"; Accept = 'application/json' }

function Get-Query([string]$Select, [string]$Filter) {
    "cross-company=true&`$select=$([Uri]::EscapeDataString($Select))&`$filter=$([Uri]::EscapeDataString($Filter))"
}

function Read-Json([string]$Path) {
    Get-Content -Raw -Encoding UTF8 $Path | ConvertFrom-Json
}

# Grava a resposta crua em $Path. Se houver @odata.nextLink, grava as páginas seguintes em <nome>.p2.json…
function Save-OData([string]$EntitySet, [string]$Query, [string]$Path) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $url = "$base/data/$($EntitySet)?$Query"
    $page = 1
    $target = $Path
    while ($true) {
        Invoke-WebRequest -UseBasicParsing -Uri $url -Headers $headers -OutFile $target
        $next = (Read-Json $target).'@odata.nextLink'
        if (-not $next) { break }
        $page++
        $url = $next
        $target = $Path -replace '\.json$', ".p$page.json"
    }
    Write-Host "  $EntitySet -> $(Resolve-Path -Relative $Path)"
}

function Get-Rows([string]$Path) { @((Read-Json $Path).value) }

function Escape-OData([string]$Value) { $Value.Replace("'", "''") }

# O dia (aaaa-mm-dd) de um campo de data do OData, como veio (12:00Z). O PowerShell 7 converte o texto em DateTime.
function Get-Day($Value) {
    if ($Value -is [datetime]) { $Value.ToUniversalTime().ToString('yyyy-MM-dd') } else { ([string]$Value).Substring(0, 10) }
}

# O cadastro de estabelecimentos e a consulta da descoberta por período (change erp-company-directory-and-card-filters,
# D1 e D4). O dia de cada estabelecimento é o dia fiscal mais recente dele nos cabeçalhos do snapshot/, já gravado.
function Save-Directory([string]$CompanyFilter) {
    Write-Host 'Diretorio e descoberta por periodo'
    Save-OData 'FiscalEstablishments' (Get-Query $EstablishmentSelect $CompanyFilter) 'directory/establishments.json'
    # Nao chamar de $headers: o Save-OData le os cabecalhos HTTP por esse nome.
    $documents = Get-Rows 'snapshot/headers.json'
    foreach ($establishment in Get-Rows 'directory/establishments.json') {
        $id = $establishment.FiscalEstablishmentId
        $mine = @($documents | Where-Object { $_.FiscalEstablishment -eq $id })
        if ($mine.Count -eq 0) {
            Write-Host "  $id sem nota: sem consulta por periodo"
            continue
        }

        $day = $mine | ForEach-Object { Get-Day $_.FiscalDocumentDate } | Sort-Object -Descending | Select-Object -First 1
        $filter = "FiscalDocumentDate ge $($day)T00:00:00Z and FiscalDocumentDate le $($day)T23:59:59Z and $CompanyFilter and FiscalEstablishment eq '$(Escape-OData $id)'"
        $query = "cross-company=true&`$select=$([Uri]::EscapeDataString($FeedSelect))&`$orderby=FiscalDocumentRecId&`$top=500&`$filter=$([Uri]::EscapeDataString($filter))"
        Save-OData 'FSFiscalDocumentBRs' $query "directory/period-$id-$day.json"
    }
}

New-Item -ItemType Directory -Force $OutputDir | Out-Null
Push-Location $OutputDir
try {
    $companyFilter = "dataAreaId eq '$(Escape-OData $Company)'"

    if ($DirectoryOnly) {
        Save-Directory $companyFilter
        return
    }

    Write-Host "Snapshot da empresa $Company"
    Save-OData 'FSFiscalDocumentBRs' (Get-Query "$HeaderSelect,SysModifiedDateTime" $companyFilter) 'snapshot/headers.json'
    Save-OData 'FSFiscalDocumentLineBRs' (Get-Query $LineSelect $companyFilter) 'snapshot/lines.json'
    Save-OData 'FSFiscalDocumentTaxTransBRs' (Get-Query "$TaxSelect,FiscalDocumentRecId,MiscChargeFiscalDocumentRecId" $companyFilter) 'snapshot/taxes.json'
    Save-OData 'FSFiscalDocumentMiscChargeBRs' (Get-Query "$ChargeSelect,FiscalDocumentRecId" $companyFilter) 'snapshot/charges.json'

    $allHeaders = Get-Rows 'snapshot/headers.json'
    $vouchers = @($allHeaders | ForEach-Object { $_.Voucher } | Sort-Object -Unique)
    $block = 0
    for ($i = 0; $i -lt $vouchers.Count; $i += 20) {
        $block++
        $slice = $vouchers[$i..([Math]::Min($i + 19, $vouchers.Count - 1))]
        $terms = ($slice | ForEach-Object { "Voucher eq '$(Escape-OData $_)'" }) -join ' or '
        Save-OData 'FSTaxTransBRs' (Get-Query $TaxTransSelect "($terms) and $companyFilter") ("snapshot/taxtrans-{0:d2}.json" -f $block)
    }

    $addresses = @{}
    foreach ($header in @($allHeaders | Where-Object { $_.Model -eq '55' } | Sort-Object FiscalDocumentRecId)) {
        $rec = $header.FiscalDocumentRecId
        Write-Host "Nota 55 $rec ($($header.Voucher))"
        Save-OData 'FSFiscalDocumentBRs' (Get-Query $HeaderSelect "FiscalDocumentRecId eq $rec") "notes/$rec/header.json"
        Save-OData 'FSFiscalDocumentLineBRs' (Get-Query $LineSelect "FiscalDocumentRecId eq $rec") "notes/$rec/lines.json"
        Save-OData 'FSFiscalDocumentTaxTransBRs' (Get-Query $TaxSelect "FiscalDocumentRecId eq $rec or MiscChargeFiscalDocumentRecId eq $rec") "notes/$rec/taxes.json"
        Save-OData 'FSFiscalDocumentMiscChargeBRs' (Get-Query $ChargeSelect "FiscalDocumentRecId eq $rec") "notes/$rec/charges.json"
        Save-OData 'FSTaxTransBRs' (Get-Query $TaxTransSelect "Voucher eq '$(Escape-OData $header.Voucher)' and $companyFilter") "notes/$rec/taxtrans.json"

        foreach ($address in @($header.FiscalEstablishmentPostalAddress, $header.ThirdPartyPostalAddress)) {
            if ($address -and $address -ne 0) { $addresses[[string]$address] = $true }
        }
    }

    Write-Host 'Cadastros referenciados'
    $cities = @{}
    foreach ($address in $addresses.Keys) {
        $path = "reference/postaladdress-$address.json"
        Save-OData 'FSPostalAddressBRs' (Get-Query $PostalAddressSelect "PostalAddressRecId eq $address") $path
        foreach ($row in Get-Rows $path) {
            if ($row.CityRecId -and $row.CityRecId -ne 0) { $cities[[string]$row.CityRecId] = $true }
        }
    }
    foreach ($city in $cities.Keys) {
        Save-OData 'FSAddressCityBRs' (Get-Query $CitySelect "AddressCityRecId eq $city") "reference/city-$city.json"
    }

    Write-Host 'Fora do escopo: canceladas e uma SE'
    $outOfScope = @($allHeaders | Where-Object { $_.Status -ne 'Approved' }) +
                  @($allHeaders | Where-Object { $_.Model -eq 'SE' -and $_.Status -eq 'Approved' } | Sort-Object FiscalDocumentRecId | Select-Object -First 1)
    foreach ($header in $outOfScope) {
        $rec = $header.FiscalDocumentRecId
        Save-OData 'FSFiscalDocumentBRs' (Get-Query $HeaderSelect "FiscalDocumentRecId eq $rec") "scope/$rec/header.json"
        Save-OData 'FSFiscalDocumentLineBRs' (Get-Query $LineSelect "FiscalDocumentRecId eq $rec") "scope/$rec/lines.json"
    }

    Save-Directory $companyFilter
}
finally {
    Pop-Location
}

Write-Host "Pronto: $OutputDir"
