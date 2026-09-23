// Correo saliente de la demo (RF-06/RF-09): Azure Communication Services Email con dominio
// administrado por Azure y SMTP autenticado con una aplicación Entra. Se despliega en el grupo
// existente con scripts/azure-correo.ps1. B-02 (SMTP institucional) sigue pendiente para operación real.
param tags object
param smtpAppId string
param smtpAppPrincipalId string
param tenantId string = subscription().tenantId

resource email 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'email-intec-fuel-dev-b805'
  location: 'global'
  tags: tags
  properties: { dataLocation: 'United States' }
}
resource domain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: email
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: { domainManagement: 'AzureManaged', userEngagementTracking: 'Disabled' }
}
resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-intec-fuel-dev-b805'
  location: 'global'
  tags: tags
  properties: { dataLocation: 'United States', linkedDomains: [domain.id] }
}
var emailOwner = '09976791-48a7-449e-bb21-39d1a415f350'
resource smtpRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acs.id, smtpAppPrincipalId, emailOwner)
  scope: acs
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', emailOwner), principalId: smtpAppPrincipalId, principalType: 'ServicePrincipal' }
}
resource smtpUser 'Microsoft.Communication/communicationServices/smtpUsernames@2025-09-01' = {
  parent: acs
  name: 'combustible-app'
  properties: { entraApplicationId: smtpAppId, tenantId: tenantId, username: 'combustible-smtp' } // Azure exige que difiera del nombre del recurso.
  dependsOn: [smtpRole]
}
output senderAddress string = 'DoNotReply@${domain.properties.mailFromSenderDomain}'
output smtpUsername string = smtpUser.properties.username
