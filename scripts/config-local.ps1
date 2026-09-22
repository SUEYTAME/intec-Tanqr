$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 convierte cualquier línea de stderr de un programa nativo en error fatal
# (docker y npm escriben su progreso en stderr). Aquí se muestra la salida y se juzga por el código de salida.
function Invoke-Native([scriptblock]$Command, [string]$Failure) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>&1 | ForEach-Object { Write-Output "$_" } } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $projectRoot '.env'
if (-not (Test-Path -LiteralPath $configPath)) {
    Copy-Item -LiteralPath (Join-Path $projectRoot '.env.example') -Destination $configPath
}
$localValues = @{}
foreach ($line in [IO.File]::ReadAllLines($configPath)) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $localValues[$Matches[1]] = $Matches[2] }
}
$changed = $false
foreach ($key in @('POSTGRES_PASSWORD','APP_DB_PASSWORD','JWT_SIGNING_KEY','BOOTSTRAP_PASSWORD','DATA_ENCRYPTION_KEY')) {
    if (-not $localValues[$key]) {
        # Instancia de RNG criptográfico: compatible con Windows PowerShell 5.1 (.NET Framework) y 7.
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $localValues[$key] = [Convert]::ToBase64String($bytes)
        $changed = $true
    }
}
if (-not $localValues['QR_SIGNING_KEY_B64']) {
    # Clave ECDSA P-256 (RS-04) en PKCS#8 PEM. CNG funciona igual en Windows PowerShell 5.1 y 7.
    # [NullString]::Value: con $null PowerShell pasaría "" y CNG crearía una clave persistente con nombre.
    $parameters = [Security.Cryptography.CngKeyCreationParameters]::new()
    $parameters.ExportPolicy = [Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
    $cng = [Security.Cryptography.CngKey]::Create([Security.Cryptography.CngAlgorithm]::ECDsaP256, [NullString]::Value, $parameters)
    try { $der = $cng.Export([Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob) } finally { $cng.Dispose() }
    $pem = "-----BEGIN PRIVATE KEY-----`n" + [Convert]::ToBase64String($der, 'InsertLineBreaks') + "`n-----END PRIVATE KEY-----"
    $localValues['QR_SIGNING_KEY_B64'] = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pem))
    $changed = $true
}
if (-not $localValues['BOOTSTRAP_EMAIL']) { $localValues['BOOTSTRAP_EMAIL'] = 'admin@localhost.test'; $changed = $true }
# Correo local: Mailpit de docker-compose. Para producción se sustituye por el SMTP institucional (B-02).
if (-not $localValues['SMTP_HOST']) {
    $localValues['SMTP_HOST'] = '127.0.0.1'
    $localValues['SMTP_PORT'] = $(if ($localValues['MAILPIT_SMTP_PORT']) { $localValues['MAILPIT_SMTP_PORT'] } else { '11025' })
    $localValues['SMTP_REQUIRE_TLS'] = 'false'
    $localValues['SMTP_FROM'] = 'combustible@localhost.test'
    $changed = $true
}
if ($changed) {
    $lines = [Collections.Generic.List[string]]::new()
    $seen = @{}
    foreach ($line in [IO.File]::ReadAllLines($configPath)) {
        if ($line -match '^([A-Z_]+)=') {
            $key = $Matches[1]; $lines.Add($key + '=' + $localValues[$key]); $seen[$key] = $true
        } else { $lines.Add($line) }
    }
    foreach ($key in $localValues.Keys) { if (-not $seen[$key]) { $lines.Add($key + '=' + $localValues[$key]) } }
    [IO.File]::WriteAllLines($configPath, $lines)
}
foreach ($key in @('JWT_SIGNING_KEY','JWT_ISSUER','JWT_AUDIENCE','APP_DB_PASSWORD','BOOTSTRAP_EMAIL','BOOTSTRAP_PASSWORD',
        'DATA_ENCRYPTION_KEY','QR_SIGNING_KEY_B64','SMTP_HOST','SMTP_PORT','SMTP_USER','SMTP_PASSWORD','SMTP_FROM','SMTP_REQUIRE_TLS','SMS_PROVIDER')) {
    [Environment]::SetEnvironmentVariable($key, $localValues[$key], 'Process')
}
$env:PUBLIC_BASE_URL = $(if ($localValues['PUBLIC_BASE_URL']) { $localValues['PUBLIC_BASE_URL'] } else { 'http://localhost:5173' })
$env:OUTBOX_DIR = Join-Path $projectRoot 'artifacts/outbox'
$dbConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
$dbConnection['Host'] = '127.0.0.1'
$dbConnection['Port'] = $localValues['POSTGRES_PORT']
$dbConnection['Database'] = $localValues['POSTGRES_DB']
$dbConnection['Username'] = $localValues['POSTGRES_USER']
$dbConnection['Password'] = $localValues['POSTGRES_PASSWORD']
$migrationConnection = $dbConnection.ConnectionString
$dbConnection['Username'] = 'combustible_app'
$dbConnection['Password'] = $localValues['APP_DB_PASSWORD']
$applicationConnection = $dbConnection.ConnectionString
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Database = $applicationConnection
