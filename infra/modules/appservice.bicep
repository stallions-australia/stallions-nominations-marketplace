param environmentName string
param location string
param tags object
param appInsightsConnectionString string
param keyVaultUri string

var isProduction = environmentName == 'prod'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: 'plan-stallions-noms-${environmentName}'
  location: location
  tags: tags
  sku: {
    name: 'B2'
    tier: 'Basic'
  }
  properties: {}
}

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: 'app-stallions-noms-${environmentName}'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      alwaysOn: isProduction
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      netFrameworkVersion: 'v9.0'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'KeyVaultUri'
          value: keyVaultUri
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: isProduction ? 'Production' : 'Development'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/SqlConnectionString/)'
        }
      ]
    }
  }
}

output principalId string = appService.identity.principalId
output appServiceName string = appService.name
output appServiceHostname string = appService.properties.defaultHostName
