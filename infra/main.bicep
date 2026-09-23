targetScope = 'subscription'

param location string = 'northcentralus'
param environmentName string = 'intec-fuel-dev-b805'
param sessionId string = 'b805f7aa-7186-4692-98e7-df81256974cc'
param deployedBy string = '1128305@est.intec.edu.do'
param createdAt string
param deployerObjectId string
param sshPublicKey string
param adminSourceCidr string

var tags = {
  'app-onboard-skill': 'true'
  'app-onboard-session-id': sessionId
  'created-at': createdAt
  environment: environmentName
  'deployed-by': deployedBy
  project: 'intec-combustible'
}

resource rg 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: 'rg-intec-fuel-dev-b805'
  location: location
  tags: tags
}

module resources './modules/resources.bicep' = {
  name: 'combustible-resources'
  scope: rg
  params: {
    location: location
    tags: tags
    deployerObjectId: deployerObjectId
    sshPublicKey: sshPublicKey
    adminSourceCidr: adminSourceCidr
  }
}

output resourceGroupName string = rg.name
output publicIp string = resources.outputs.publicIp
output fqdn string = resources.outputs.fqdn
output vmName string = resources.outputs.vmName
output keyVaultName string = resources.outputs.keyVaultName
output storageAccountName string = resources.outputs.storageAccountName
output vmPrincipalId string = resources.outputs.vmPrincipalId
