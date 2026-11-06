[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$AppName,
    [Parameter(Mandatory)][string]$RevisionName
)
$ErrorActionPreference = 'Stop'
$raw = & az containerapp revision show --resource-group $ResourceGroup --name $AppName --revision $RevisionName -o json --only-show-errors
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect selected revision.' }
$revision = $raw | ConvertFrom-Json
if ($revision.name -ne $RevisionName -or -not $revision.properties.active -or $revision.properties.healthState -ne 'Healthy') {
    throw 'Target must be the named active, healthy revision. No traffic changed.'
}
if ($PSCmdlet.ShouldProcess("$ResourceGroup/$AppName", "Route 100% traffic to $RevisionName")) {
    & az containerapp ingress traffic set --resource-group $ResourceGroup --name $AppName --revision-weight "${RevisionName}=100" --only-show-errors -o none
    if ($LASTEXITCODE -ne 0) { throw 'Traffic change failed; inspect cloud state.' }
    $trafficJson = & az containerapp show --resource-group $ResourceGroup --name $AppName --query properties.configuration.ingress.traffic -o json --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw 'Traffic readback failed; rollback is not verified.' }
    $traffic = @($trafficJson | ConvertFrom-Json)
    $positive = @($traffic | Where-Object { $_.weight -gt 0 })
    if ($positive.Count -ne 1 -or $positive[0].revisionName -ne $RevisionName -or $positive[0].weight -ne 100) {
        throw 'Traffic readback differs from the selected revision at 100%; inspect cloud state.'
    }
    Write-Output $trafficJson
}
