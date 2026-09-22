param([switch]$Reiniciar)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($Reiniciar) { & "$PSScriptRoot/detener.ps1" }
$processFile = Join-Path $projectRoot 'artifacts/procesos-locales.json'
if (Test-Path -LiteralPath $processFile) {
    $saved = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
    $alive = @($saved | Where-Object {
        $process = Get-Process -Id $_.Id -ErrorAction SilentlyContinue
        $process -and $process.StartTime.ToUniversalTime().Ticks -eq ([DateTimeOffset]$_.Started).UtcTicks
    })
    if ($alive.Count -gt 0) {
        Write-Output 'Hay procesos de esta aplicación activos. URL: http://localhost:5173. Usa -Reiniciar para actualizar.'
        return
    }
}
foreach ($port in @(5080,5173)) {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
    try { $probe.Start() } finally { $probe.Stop() }
}
& "$PSScriptRoot/preparar.ps1"
. "$PSScriptRoot/config-local.ps1"
Push-Location (Join-Path $projectRoot 'frontend')
try { Invoke-Native { npm ci } 'Falló npm ci.' } finally { Pop-Location }
$outputDir = Join-Path $projectRoot 'artifacts'
$dll = Join-Path $projectRoot 'backend/src/Combustible.Api/bin/Debug/net10.0/Combustible.Api.dll'
$vite = Join-Path $projectRoot 'frontend/node_modules/vite/bin/vite.js'
# Los servidores reciben solo la conexión de aplicación; no la contraseña bootstrap.
Remove-Item Env:BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue
Remove-Item Env:APP_DB_PASSWORD -ErrorAction SilentlyContinue
$apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('exec', ('"' + $dll + '"'), '--urls', 'http://127.0.0.1:5080') -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputDir 'api.log') -RedirectStandardError (Join-Path $outputDir 'api-error.log')
$records = @(@{ Id = $apiProcess.Id; Started = $apiProcess.StartTime.ToUniversalTime().ToString('o'); Name = 'dotnet'; Target = $dll })
ConvertTo-Json -InputObject @($records) | Set-Content -LiteralPath $processFile
$webProcess = Start-Process -FilePath 'node' -ArgumentList @(('"' + $vite + '"')) -WorkingDirectory (Join-Path $projectRoot 'frontend') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputDir 'web.log') -RedirectStandardError (Join-Path $outputDir 'web-error.log')
$records += @{ Id = $webProcess.Id; Started = $webProcess.StartTime.ToUniversalTime().ToString('o'); Name = 'node'; Target = $vite }
ConvertTo-Json -InputObject @($records) | Set-Content -LiteralPath $processFile
$ready = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        $apiResponse = Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:5080/health/ready' -TimeoutSec 2
        $webResponse = Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:5173' -TimeoutSec 2
        if ($apiResponse.StatusCode -eq 200 -and $webResponse.StatusCode -eq 200) { $ready = $true; break }
    } catch {
        # Servidor aún sin escuchar: WebException en PowerShell 5.1, HttpRequestException en 7. Otro error se propaga.
        $kind = $_.Exception.GetType().FullName
        if ($kind -ne 'System.Net.WebException' -and $kind -ne 'System.Net.Http.HttpRequestException') { throw }
        Start-Sleep -Milliseconds 500
    }
}
if (-not $ready) { throw 'Los servidores no respondieron. Revisa artifacts/api-error.log y web-error.log.' }
Write-Output 'Aplicación disponible: http://localhost:5173'
Write-Output 'Credenciales locales: artifacts/acceso-local.txt'
