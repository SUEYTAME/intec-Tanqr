# Vigilancia de la demo en Azure INTEC: infra/monitoreo.bicep (prueba de /health + alerta) e
# infra/presupuesto.bicep (presupuesto de la suscripción). Sin -Deploy solo muestra los what-if.
# El correo de aviso es el de la cuenta con sesión en az; no queda escrito en el repositorio.
param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$subscription = '44f41884-c42a-4162-898f-d83d8d987ff3'
$group = 'rg-intec-fuel-dev-b805'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$email = az account show --subscription $subscription --query user.name -o tsv
if ($LASTEXITCODE -ne 0 -or -not $email) { throw 'No hay sesión de az para la suscripción INTEC.' }
$location = az group show --subscription $subscription -n $group --query location -o tsv
$tags = az group show --subscription $subscription -n $group --query tags -o json | ConvertFrom-Json

function Write-Parameters([string]$Path, [hashtable]$Values) {
    $p = @{}
    foreach ($k in $Values.Keys) { $p[$k] = @{ value = $Values[$k] } }
    $doc = @{ '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'; contentVersion = '1.0.0.0'; parameters = $p }
    [IO.File]::WriteAllText($Path, ($doc | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
}
$monitorParams = Join-Path $root 'artifacts/azure/monitoreo.parameters.json'
$budgetParams = Join-Path $root 'artifacts/azure/presupuesto.parameters.json'
Write-Parameters $monitorParams @{ tags = $tags; alertEmail = $email }
Write-Parameters $budgetParams @{ alertEmail = $email }

$groupArgs = @('--subscription', $subscription, '-g', $group, '--name', 'intec-fuel-monitoreo', '--template-file', 'infra/monitoreo.bicep', '--parameters', "@$monitorParams")
$subArgs = @('--subscription', $subscription, '--location', $location, '--name', 'intec-fuel-presupuesto', '--template-file', 'infra/presupuesto.bicep', '--parameters', "@$budgetParams")
if (-not $Deploy) {
    az deployment group what-if @groupArgs --no-pretty-print -o json
    if ($LASTEXITCODE -ne 0) { throw 'Fallo el what-if del monitoreo.' }
    az deployment sub what-if @subArgs --no-pretty-print -o json
    if ($LASTEXITCODE -ne 0) { throw 'Fallo el what-if del presupuesto.' }
    return
}
az deployment group create @groupArgs -o none
if ($LASTEXITCODE -ne 0) { throw 'Fallo el despliegue del monitoreo.' }
az deployment sub create @subArgs -o none
if ($LASTEXITCODE -ne 0) { throw 'Fallo el despliegue del presupuesto.' }
Write-Output 'Monitoreo y presupuesto desplegados; los avisos llegan al correo de la cuenta.'
