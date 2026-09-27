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

    As respostas são salvas como vieram (Invoke-WebRequest -OutFile), sem reformatar. O $select de
    cada consulta de nota é o do design D5: o mesmo que o source do D365 pede, e o que entra no hash.

.EXAMPLE
    az login
    ./tools/d365-fixtures/Record-D365Fixtures.ps1 -EnvironmentUrl https://fiscosysdev.operations.dynamics.com -Company brmf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [Parameter(Mandatory = $true)][string]$Company,
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\..\tests\Adapters\Ingress\FiscalHub.Adapters.Ingress.D365Poll.Tests\Fixtures\d365')
)

$ErrorActionPreference = 'Stop'

# $select do design D5 — precisa bater com o D365GoodsInvoiceSource.
$HeaderSelect = 'FiscalDocumentRecId,dataAreaId,Voucher,Model,Status,Direction,FiscalDocumentIssuer,AccessKey,FiscalDocumentSeries,FiscalDocumentNumber,FiscalDocumentDate,FiscalDocumentDateTime,FiscalEstablishmentCNPJCPF,FiscalEstablishmentName,FiscalEstablishmentIE,FiscalEstablishmentPostalAddress,ThirdPartyCNPJCPF,ThirdPartyName,ThirdPartyIE,ThirdPartyPostalAddress,TotalAmount'
$LineSelect = 'FiscalDocumentLineRecId,FiscalDocumentRecId,LineNum,ItemId,Description,FiscalClassification,CFOP,Quantity,UnitPrice,LineAmount'
$TaxSelect = 'FiscalDocumentTaxTransRecId,FiscalDocumentLineRecId,FiscalDocumentMiscChargeRecId,TaxTransRecId,FiscalTaxType,TaxationCode,TaxBaseAmount,TaxBaseAmountExempt,TaxBaseAmountOther,TaxValue,TaxAmount,RetainedTax'
$ChargeSelect = 'FiscalDocumentMiscChargeRecId,FiscalDocumentLineRecId,ChargeNum,MiscChargeType,Amount,Txt'
$TaxTransSelect = 'TaxTransRecId,Voucher,TaxType,TaxBaseAmount,TaxValue,TaxAmount'
$PostalAddressSelect = 'PostalAddressRecId,CityRecId'
$CitySelect = 'AddressCityRecId,IBGECode'

$base = $EnvironmentUrl.TrimEnd('/')
$token = az account get-access-token --resource $base --query accessToken -o tsv
if (-not $token) { throw 'Sem token do Azure CLI: rode az login.' }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json' }

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

New-Item -ItemType Directory -Force $OutputDir | Out-Null
Push-Location $OutputDir
try {
    $companyFilter = "dataAreaId eq '$(Escape-OData $Company)'"

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
}
finally {
    Pop-Location
}

Write-Host "Pronto: $OutputDir"
