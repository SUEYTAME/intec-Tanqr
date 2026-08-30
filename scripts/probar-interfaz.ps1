. "$PSScriptRoot/config-local.ps1"
Push-Location (Join-Path $projectRoot 'frontend')
try {
    Invoke-Native { npx playwright test } 'Las pruebas de navegador fallaron.'
} finally { Pop-Location }
