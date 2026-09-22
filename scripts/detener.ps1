$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$processFile = Join-Path $projectRoot 'artifacts/procesos-locales.json'
if (-not (Test-Path -LiteralPath $processFile)) { Write-Output 'No hay procesos registrados.'; return }
foreach ($record in (Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json)) {
    $process = Get-Process -Id $record.Id -ErrorAction SilentlyContinue
    if (-not $process) { continue }
    $command = (Get-CimInstance Win32_Process -Filter "ProcessId = $($record.Id)").CommandLine
    if ($process.ProcessName -ne $record.Name -or $process.StartTime.ToUniversalTime().Ticks -ne ([DateTimeOffset]$record.Started).UtcTicks -or -not $command.Contains($record.Target)) {
        throw "El proceso $($record.Id) no coincide con el registrado; no se detuvo."
    }
    Stop-Process -Id $record.Id
    Wait-Process -Id $record.Id -Timeout 10 -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $processFile
Write-Output 'API y web detenidas. PostgreSQL y sus datos se conservan.'
