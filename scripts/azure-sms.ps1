# Configura Twilio desde .env en el Key Vault INTEC sin imprimir valores.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$values = @{}
foreach ($line in [IO.File]::ReadAllLines((Join-Path $root '.env'))) {
    if ($line -match '^([A-Z_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2] }
}
if ($values['SMS_PROVIDER'] -ne 'twilio') { throw 'Se requiere SMS_PROVIDER=twilio en .env.' }
foreach ($key in @('TWILIO_ACCOUNT_SID','TWILIO_AUTH_TOKEN')) {
    if (-not $values[$key]) { throw "Falta $key en .env." }
}
if ([string]::IsNullOrWhiteSpace($values['TWILIO_FROM']) -eq [string]::IsNullOrWhiteSpace($values['TWILIO_MESSAGING_SERVICE_SID'])) {
    throw 'Defina exactamente TWILIO_FROM o TWILIO_MESSAGING_SERVICE_SID.'
}
$mapping = [ordered]@{
    'sms-provider' = 'SMS_PROVIDER'
    'twilio-account-sid' = 'TWILIO_ACCOUNT_SID'
    'twilio-auth-token' = 'TWILIO_AUTH_TOKEN'
    'twilio-from' = 'TWILIO_FROM'
    'twilio-messaging-service-sid' = 'TWILIO_MESSAGING_SERVICE_SID'
    'twilio-trial-template' = 'TWILIO_TRIAL_TEMPLATE'
    'twilio-trial-until' = 'TWILIO_TRIAL_UNTIL'
}
$temporary = Join-Path $root 'artifacts/azure/twilio-secret.tmp'
foreach ($name in $mapping.Keys) {
    try {
        # Vacío explícito elimina un remitente anterior sin borrar versiones de Key Vault.
        [IO.File]::WriteAllText($temporary, [string]$values[$mapping[$name]], [Text.UTF8Encoding]::new($false))
        az keyvault secret set --subscription 44f41884-c42a-4162-898f-d83d8d987ff3 --vault-name kv-intec-fuel-dev-b805 --name $name --file $temporary --encoding utf-8 -o none
        if ($LASTEXITCODE -ne 0) { throw "No se pudo guardar $name." }
        Write-Output "$name configurado."
    } finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}
