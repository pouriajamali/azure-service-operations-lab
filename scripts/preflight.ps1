[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is not installed. No cloud checks or changes performed.'
}
$accountJson = & az account show --query '{id:id,state:state}' -o json --only-show-errors 2>$null
if ($LASTEXITCODE -ne 0) { throw 'No authenticated Azure subscription. No cloud changes performed.' }
$account = $accountJson | ConvertFrom-Json
if ($account.state -ne 'Enabled') { throw 'Selected subscription is not enabled.' }
Write-Output 'Azure login and enabled subscription found; this does not validate resource permissions, cost or deployment.'
& az bicep build --file (Join-Path $PSScriptRoot '../infra/foundation.bicep') --stdout | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Foundation compilation failed.' }
& az bicep build --file (Join-Path $PSScriptRoot '../infra/application.bicep') --stdout | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
