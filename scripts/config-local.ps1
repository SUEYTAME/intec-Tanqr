$ErrorActionPreference = 'Stop'
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
foreach ($key in @('POSTGRES_PASSWORD','APP_DB_PASSWORD','JWT_SIGNING_KEY','BOOTSTRAP_PASSWORD')) {
    if (-not $localValues[$key]) {
        $localValues[$key] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $changed = $true
    }
}
if (-not $localValues['BOOTSTRAP_EMAIL']) { $localValues['BOOTSTRAP_EMAIL'] = 'admin@localhost.test'; $changed = $true }
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
foreach ($key in @('JWT_SIGNING_KEY','JWT_ISSUER','JWT_AUDIENCE','APP_DB_PASSWORD','BOOTSTRAP_EMAIL','BOOTSTRAP_PASSWORD')) {
    [Environment]::SetEnvironmentVariable($key, $localValues[$key], 'Process')
}
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
