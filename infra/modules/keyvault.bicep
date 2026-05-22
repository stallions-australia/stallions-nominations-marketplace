param environmentName string
param location string
param tags object

@secure()
param sqlConnectionString string = ''

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-stallions-noms-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(sqlConnectionString)) {
  parent: keyVault
  name: 'SqlConnectionString'
  properties: {
    value: sqlConnectionString
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
