. "$PSScriptRoot/config-local.ps1"
Push-Location $projectRoot
try {
    docker compose config --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Configuración Docker inválida.' }
    docker compose up -d --wait --wait-timeout 60
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL no está saludable.' }
    $env:ConnectionStrings__Database = $migrationConnection
    dotnet run --project backend/src/Combustible.Api --no-launch-profile -- --initialize
    if ($LASTEXITCODE -ne 0) { throw 'Falló la inicialización de la API.' }
    $accessDir = Join-Path $projectRoot 'artifacts'
    New-Item -ItemType Directory -Path $accessDir -Force | Out-Null
    $access = "Acceso de desarrollo local (no producción)`n`nURL: http://localhost:5173`nUsuario: $($localValues['BOOTSTRAP_EMAIL'])`nContraseña inicial: $($localValues['BOOTSTRAP_PASSWORD'])`n`nSi la contraseña fue cambiada desde la aplicación, este valor inicial ya no aplica."
    [IO.File]::WriteAllText((Join-Path $accessDir 'acceso-local.txt'), $access)
    Write-Output 'Preparado. Credenciales en artifacts/acceso-local.txt (ignorado por Git).'
} finally {
    $env:ConnectionStrings__Database = $applicationConnection
    Pop-Location
}
