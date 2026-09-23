# Genera UNA vez los secretos de producción y los guarda en Key Vault (cuenta Azure INTEC).
# Reintentos reutilizan los valores existentes: cambiar DATA_ENCRYPTION_KEY o QR_SIGNING_KEY_B64
# haría ilegibles los datos cifrados y los tickets emitidos. Nunca imprime valores.
$ErrorActionPreference = 'Stop'
$subscription = '44f41884-c42a-4162-898f-d83d8d987ff3'
$vault = 'kv-intec-fuel-dev-b805'
$root = Split-Path $PSScriptRoot -Parent

function New-RandomBase64 {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [Convert]::ToBase64String($bytes)
}
# Sin '+', '/', '=' para que viaje sin escapes por cadenas de conexión y archivos env.
function New-RandomPassword { (New-RandomBase64) -replace '[+/=]', '' }
# Igual que scripts/config-local.ps1: ECDSA P-256 PKCS#8 PEM en base64.
function New-EcdsaPem {
    $parameters = [Security.Cryptography.CngKeyCreationParameters]::new()
    $parameters.ExportPolicy = [Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
    $cng = [Security.Cryptography.CngKey]::Create([Security.Cryptography.CngAlgorithm]::ECDsaP256, [NullString]::Value, $parameters)
    try { $der = $cng.Export([Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob) } finally { $cng.Dispose() }
    $pem = "-----BEGIN PRIVATE KEY-----`n" + [Convert]::ToBase64String($der, 'InsertLineBreaks') + "`n-----END PRIVATE KEY-----"
    [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pem))
}

$generators = [ordered]@{
    'postgres-password'     = { New-RandomPassword }
    'app-db-password'       = { New-RandomPassword }
    'bootstrap-password'    = { New-RandomPassword }
    'tls-pfx-password'      = { New-RandomPassword }
    'jwt-signing-key'       = { New-RandomBase64 }
    'data-encryption-key'   = { New-RandomBase64 }
    'qr-signing-key-b64'    = { New-EcdsaPem }
    'oauth-signing-key-b64' = { New-EcdsaPem }
}
$existing = az keyvault secret list --subscription $subscription --vault-name $vault --query '[].name' -o tsv
if ($LASTEXITCODE -ne 0) { throw 'No se pudo listar Key Vault.' }
$tmp = Join-Path $root 'artifacts/azure/secreto.tmp'
foreach ($name in $generators.Keys) {
    if ($existing -contains $name) { Write-Output "$name ya existe; se conserva."; continue }
    try {
        [IO.File]::WriteAllText($tmp, (& $generators[$name]), (New-Object Text.UTF8Encoding($false)))
        az keyvault secret set --subscription $subscription --vault-name $vault --name $name --file $tmp --encoding utf-8 -o none
        if ($LASTEXITCODE -ne 0) { throw "No se pudo guardar $name." }
        Write-Output "$name creado."
    } finally { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
}
