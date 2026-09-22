. "$PSScriptRoot/config-local.ps1"
Push-Location (Join-Path $projectRoot 'frontend')
try {
    npx playwright test
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas de navegador fallaron.' }
} finally { Pop-Location }
