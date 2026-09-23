param location string
param tags object
param deployerObjectId string
param sshPublicKey string
param adminSourceCidr string
param adminUsername string = 'azureadmin'

resource nsg 'Microsoft.Network/networkSecurityGroups@2025-01-01' = {
  name: 'nsg-intec-fuel-dev-b805'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'HTTPS'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'ACME-HTTP'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '80'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'SSH-admin'
        properties: {
          priority: 120
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '22'
          sourceAddressPrefix: adminSourceCidr
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}
resource vnet 'Microsoft.Network/virtualNetworks@2025-01-01' = {
  name: 'vnet-intec-fuel-dev-b805'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.72.0.0/16'] }
    subnets: [
      {
        name: 'snet-intec-fuel-dev-b805'
        properties: {
          addressPrefix: '10.72.1.0/24'
          networkSecurityGroup: { id: nsg.id }
        }
      }
    ]
  }
}
resource pip 'Microsoft.Network/publicIPAddresses@2025-01-01' = {
  name: 'pip-intec-fuel-dev-b805'
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
    dnsSettings: { domainNameLabel: 'intec-fuel-dev-b805' }
  }
}
resource nic 'Microsoft.Network/networkInterfaces@2025-01-01' = {
  name: 'nic-intec-fuel-dev-b805'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'primary'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: { id: vnet.properties.subnets[0].id }
          publicIPAddress: { id: pip.id }
        }
      }
    ]
  }
}
resource vm 'Microsoft.Compute/virtualMachines@2026-04-01' = {
  name: 'vm-intec-fuel-dev-b805'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    hardwareProfile: { vmSize: 'Standard_B2als_v2' }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: '24.04.202609040'
      }
      osDisk: {
        name: 'os-intec-fuel-dev-b805'
        createOption: 'FromImage'
        diskSizeGB: 64
        managedDisk: { storageAccountType: 'StandardSSD_LRS' }
        deleteOption: 'Detach'
      }
    }
    osProfile: {
      computerName: 'intec-fuel'
      adminUsername: adminUsername
      customData: base64(loadTextContent('../cloud-init.yaml'))
      linuxConfiguration: {
        disablePasswordAuthentication: true
        provisionVMAgent: true
        ssh: {
          publicKeys: [{ path: '/home/${adminUsername}/.ssh/authorized_keys', keyData: sshPublicKey }]
        }
        patchSettings: { patchMode: 'AutomaticByPlatform' }
      }
    }
    securityProfile: {
      securityType: 'TrustedLaunch'
      uefiSettings: { secureBootEnabled: true, vTpmEnabled: true }
    }
    networkProfile: { networkInterfaces: [{ id: nic.id }] }
    diagnosticsProfile: { bootDiagnostics: { enabled: true } }
  }
}
resource kv 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: 'kv-intec-fuel-dev-b805'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    networkAcls: { defaultAction: 'Allow', bypass: 'AzureServices' }
  }
}
resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: 'stintecfueldevb805'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}
resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}
resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = [for name in ['backups', 'deployments']: {
  parent: blobs
  name: name
  properties: { publicAccess: 'None' }
}]
var kvOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var kvReader = '4633458b-17de-408a-b874-0445c86b69e6'
var blobContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
resource kvDeployRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, deployerObjectId, kvOfficer)
  scope: kv
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvOfficer), principalId: deployerObjectId, principalType: 'User' }
}
resource kvVmRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, vm.id, kvReader)
  scope: kv
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvReader), principalId: vm.identity.principalId, principalType: 'ServicePrincipal' }
}
resource storageDeployRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, deployerObjectId, blobContributor)
  scope: storage
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobContributor), principalId: deployerObjectId, principalType: 'User' }
}
resource storageVmRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, vm.id, blobContributor)
  scope: storage
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobContributor), principalId: vm.identity.principalId, principalType: 'ServicePrincipal' }
}
output publicIp string = pip.properties.ipAddress
output fqdn string = pip.properties.dnsSettings.fqdn
output vmName string = vm.name
output keyVaultName string = kv.name
output storageAccountName string = storage.name
output vmPrincipalId string = vm.identity.principalId
