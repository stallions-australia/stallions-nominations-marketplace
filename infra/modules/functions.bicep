param environmentName string
param location string
param tags object
param storageAccountName string
param appInsightsConnectionString string
param keyVaultUri string

resource consumptionPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: 'plan-stallions-noms-func-${environmentName}'
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: 'func-stallions-noms-${environmentName}'
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: consumptionPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'KeyVaultUri'
          value: keyVaultUri
        }
      ]
    }
  }
}

output principalId string = functionApp.identity.principalId
output functionAppName string = functionApp.name
