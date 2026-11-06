$ErrorActionPreference = 'Stop'
$trafficScript = Join-Path $PSScriptRoot '../scripts/traffic.ps1'
$global:OpsLabMock = @{ Changes=0; Health='Healthy'; Active=$true; Target='lab--baseline'; Weight=100 }
function global:az {
    $global:LASTEXITCODE = 0
    $command = $args -join ' '
    if ($command -like 'containerapp revision show*') {
        return @{ name = 'lab--baseline'; properties = @{ active = $global:OpsLabMock.Active; healthState = $global:OpsLabMock.Health } } | ConvertTo-Json -Compress
    }
    if ($command -like 'containerapp ingress traffic set*') { $global:OpsLabMock.Changes++; return }
    if ($command -like 'containerapp show*') {
        return ConvertTo-Json -InputObject @(@{ revisionName = $global:OpsLabMock.Target; weight = $global:OpsLabMock.Weight }) -Compress
    }
    throw 'Unexpected Azure CLI call in test'
}
$checks = 0
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw $name }
    $script:checks++; Write-Output "PASS $name"
}
function ExpectFailure([scriptblock]$operation, [string]$name) {
    $failed = $false
    try { & $operation | Out-Null } catch { $failed = $true }
    Check $failed $name
}
try {
    & $trafficScript -ResourceGroup synthetic-rg -AppName lab -RevisionName lab--baseline -WhatIf | Out-Null
    Check ($global:OpsLabMock.Changes -eq 0) 'WhatIf never mutates traffic'
    & $trafficScript -ResourceGroup synthetic-rg -AppName lab -RevisionName lab--baseline -Confirm:$false | Out-Null
    Check ($global:OpsLabMock.Changes -eq 1) 'healthy exact revision traffic verified'
    $global:OpsLabMock.Health = 'Unhealthy'
    ExpectFailure { & $trafficScript -ResourceGroup synthetic-rg -AppName lab -RevisionName lab--baseline -Confirm:$false } 'unhealthy target rejected'
    Check ($global:OpsLabMock.Changes -eq 1) 'unhealthy target cannot change traffic'
    $global:OpsLabMock.Health = 'Healthy'; $global:OpsLabMock.Active = $false
    ExpectFailure { & $trafficScript -ResourceGroup synthetic-rg -AppName lab -RevisionName lab--baseline -Confirm:$false } 'inactive target rejected'
    $global:OpsLabMock.Active = $true; $global:OpsLabMock.Target = 'lab--unexpected'
    ExpectFailure { & $trafficScript -ResourceGroup synthetic-rg -AppName lab -RevisionName lab--baseline -Confirm:$false } 'wrong revision readback rejected'
    $global:OpsLabMock.Target = 'lab--baseline'; $global:OpsLabMock.Weight = 50
    ExpectFailure { & $trafficScript -ResourceGroup synthetic-rg -AppName lab -RevisionName lab--baseline -Confirm:$false } 'partial traffic readback rejected'
    Write-Output "PASS $checks mocked CLI checks; no Azure resource or traffic was used"
}
finally { Remove-Item Function:az -ErrorAction SilentlyContinue; Remove-Variable OpsLabMock -Scope Global -ErrorAction SilentlyContinue }
