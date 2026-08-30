. "$PSScriptRoot/config-local.ps1"
Push-Location $projectRoot
try {
    Invoke-Native { docker compose config --quiet } 'Configuración Docker inválida.'
    Invoke-Native { docker compose up -d --wait --wait-timeout 60 } 'PostgreSQL o Mailpit no están saludables.'
    $env:ConnectionStrings__Database = $migrationConnection
    Invoke-Native { dotnet run --project backend/src/Combustible.Api --no-launch-profile -- --initialize } 'Falló la inicialización de la API.'
    $accessDir = Join-Path $projectRoot 'artifacts'
    New-Item -ItemType Directory -Path $accessDir -Force | Out-Null
    $access = "Acceso de desarrollo local (no producción)`n`nURL: http://localhost:5173`nUsuario: $($localValues['BOOTSTRAP_EMAIL'])`nContraseña inicial: $($localValues['BOOTSTRAP_PASSWORD'])`n`nSi la contraseña fue cambiada desde la aplicación, este valor inicial ya no aplica."
    [IO.File]::WriteAllText((Join-Path $accessDir 'acceso-local.txt'), $access)
    Write-Output 'Preparado. Credenciales en artifacts/acceso-local.txt (ignorado por Git).'
} finally {
    $env:ConnectionStrings__Database = $applicationConnection
    Pop-Location
}
