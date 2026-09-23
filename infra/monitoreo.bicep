// Vigilancia de disponibilidad de la demo: prueba estándar de /health cada 15 min (también
// revisa el certificado TLS) y alerta por correo si falla. Coste medido 2026-09-23 con la API
// de precios: USD 0.00056 por ejecución (~2 880/mes ≈ USD 1.6) + USD 0.10/mes por la alerta.
// Se despliega en el grupo existente con scripts/azure-monitoreo.ps1.
param tags object
param alertEmail string
param healthUrl string = 'https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com/health'
param location string = resourceGroup().location

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-intec-fuel-dev-b805'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.1') } // Solo guarda resultados de disponibilidad.
  }
}
resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-intec-fuel-dev-b805'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}
var testName = 'wt-intec-fuel-health'
resource health 'Microsoft.Insights/webtests@2022-06-15' = {
  name: testName
  location: location
  tags: union(tags, { 'hidden-link:${insights.id}': 'Resource' })
  kind: 'standard'
  properties: {
    SyntheticMonitorId: testName
    Name: 'Salud de la demo (/health)'
    Kind: 'standard'
    Enabled: true
    Frequency: 900
    Timeout: 30
    RetryEnabled: true
    Locations: [ { Id: 'us-il-ch1-azr' } ] // North Central US, misma región que la VM.
    Request: { RequestUrl: healthUrl, HttpVerb: 'GET', ParseDependentRequests: false }
    ValidationRules: { ExpectedHttpStatusCode: 200, SSLCheck: true, SSLCertRemainingLifetimeCheck: 14 }
  }
}
resource owner 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-intec-fuel-dev-b805'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'combustible'
    enabled: true
    // Renombrado 2026-09-23: re-guardar el mismo receptor no reenvía el OTP de verificación de Azure.
    emailReceivers: [ { name: 'propietario-intec', emailAddress: alertEmail, useCommonAlertSchema: true } ]
    // Push a la app móvil "Microsoft Azure" con la misma cuenta: no depende del OTP de correo.
    azureAppPushReceivers: [ { name: 'propietario-app', emailAddress: alertEmail } ]
  }
}
resource down 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alerta-demo-caida'
  location: 'global'
  tags: tags
  properties: {
    description: 'La demo no responde 200 en /health o el certificado vence en menos de 14 días.'
    severity: 1
    enabled: true
    scopes: [ health.id, insights.id ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    autoMitigate: true
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: health.id
      componentId: insights.id
      failedLocationCount: 1
    }
    actions: [ { actionGroupId: owner.id } ]
  }
}
output actionGroupId string = owner.id
