# Correo de la demo en Azure INTEC: app Entra para SMTP + infra/correo.bicep.
# Sin -Deploy solo muestra el what-if. Idempotente: reutiliza la app y el secreto ya guardado.
# El secreto de cliente va directo a Key Vault; nunca se imprime.
param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$subscription = '44f41884-c42a-4162-898f-d83d8d987ff3'
$group = 'rg-intec-fuel-dev-b805'
$vault = 'kv-intec-fuel-dev-b805'
$appName = 'intec-combustible-smtp-b805'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$appId = az ad app list --display-name $appName --query '[0].appId' -o tsv
if (-not $appId) {
    $appId = az ad app create --display-name $appName --sign-in-audience AzureADMyOrg --query appId -o tsv
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la aplicación Entra.' }
}
$spId = az ad sp list --filter "appId eq '$appId'" --query '[0].id' -o tsv
if (-not $spId) {
    $spId = az ad sp create --id $appId --query id -o tsv
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el principal de servicio.' }
}
$tags = az group show --subscription $subscription -n $group --query tags -o json | ConvertFrom-Json
$params = @{
    '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
    contentVersion = '1.0.0.0'
    parameters = @{ tags = @{ value = $tags }; smtpAppId = @{ value = $appId }; smtpAppPrincipalId = @{ value = $spId } }
}
$parameterPath = Join-Path $root 'artifacts/azure/correo.parameters.json'
[IO.File]::WriteAllText($parameterPath, ($params | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
$argsList = @('--subscription', $subscription, '-g', $group, '--name', 'intec-fuel-correo', '--template-file', 'infra/correo.bicep', '--parameters', "@$parameterPath")
if (-not $Deploy) {
    az deployment group what-if @argsList --no-pretty-print -o json
    if ($LASTEXITCODE -ne 0) { throw 'Fallo el what-if del correo.' }
    return
}
$outputs = az deployment group create @argsList --query 'properties.outputs' -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Fallo el despliegue del correo.' }

$existing = az keyvault secret list --subscription $subscription --vault-name $vault --query '[].name' -o tsv
$tmp = Join-Path $root 'artifacts/azure/secreto.tmp'
function Set-VaultValue([string]$Name, [string]$Value) {
    try {
        [IO.File]::WriteAllText($tmp, $Value, (New-Object Text.UTF8Encoding($false)))
        az keyvault secret set --subscription $subscription --vault-name $vault --name $Name --file $tmp --encoding utf-8 -o none
        if ($LASTEXITCODE -ne 0) { throw "No se pudo guardar $Name." }
    } finally { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
}
if ($existing -notcontains 'smtp-password') {
    $secret = az ad app credential reset --id $appId --display-name 'smtp-combustible' --years 1 --append --query password -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $secret) { throw 'No se pudo crear el secreto de cliente.' }
    Set-VaultValue 'smtp-password' $secret
    Remove-Variable secret
    Write-Output 'smtp-password creado (vence en 1 año).'
}
Set-VaultValue 'smtp-host' 'smtp.azurecomm.net'
Set-VaultValue 'smtp-port' '587'
Set-VaultValue 'smtp-user' $outputs.smtpUsername.value
Set-VaultValue 'smtp-from' $outputs.senderAddress.value
Write-Output "Remitente: $($outputs.senderAddress.value)"
