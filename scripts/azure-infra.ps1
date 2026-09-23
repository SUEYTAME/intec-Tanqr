param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$subscription = '44f41884-c42a-4162-898f-d83d8d987ff3'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$actual = az account show --subscription $subscription --query id -o tsv
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $subscription) { throw 'Cuenta INTEC no verificada.' }
$publicKeyPath = Join-Path $root 'artifacts/azure/id_ed25519.pub'
if (-not (Test-Path $publicKeyPath)) { throw 'Falta la clave SSH de despliegue.' }
$adminIp = (Invoke-RestMethod https://api.ipify.org).Trim()
if ($adminIp -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { throw 'IP de administracion invalida.' }
$params = @{
  '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
  contentVersion = '1.0.0.0'
  parameters = @{
    createdAt = @{ value = '2026-09-23T03:40:30Z' }
    deployerObjectId = @{ value = 'd9f97e72-8622-4e69-b76e-7755e44d49d9' }
    sshPublicKey = @{ value = (Get-Content $publicKeyPath -Raw).Trim() }
    adminSourceCidr = @{ value = "$adminIp/32" }
  }
}
$parameterPath = Join-Path $root 'artifacts/azure/parameters.json'
[IO.File]::WriteAllText($parameterPath, ($params | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
$argsList = @('--subscription', $subscription, '--location', 'northcentralus', '--name', 'intec-fuel-b805', '--template-file', 'infra/main.bicep', '--parameters', "@$parameterPath")
if ($Deploy) {
  az deployment sub create @argsList --query 'properties.outputs' -o json
} else {
  az deployment sub what-if @argsList --no-pretty-print -o json
}
if ($LASTEXITCODE -ne 0) { throw 'Fallo de validacion/despliegue de infraestructura.' }
