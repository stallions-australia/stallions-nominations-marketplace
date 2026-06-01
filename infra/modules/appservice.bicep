param environmentName string
param location string
param tags object
param appInsightsConnectionString string
param keyVaultUri string
param entraTenantId string
param entraTenantName string
param entraApiClientId string
param storageAccountName string

var isProduction = environmentName == 'prod'

// Merge the azd service-name tag so azd deploy can locate this App Service
var appServiceTags = union(tags, { 'azd-service-name': 'api' })

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
  tags: appServiceTags
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
          // Dev runs as Development so the Blazor WASM client loads appsettings.json (dev client ID).
          // Prod runs as Production so it loads appsettings.Production.json (prod client ID).
          // The dev/prod distinction maps directly to Entra External ID app registrations.
          name: 'ASPNETCORE_ENVIRONMENT'
          value: isProduction ? 'Production' : 'Development'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/SqlConnectionString/)'
        }
        {
          name: 'AzureAd__Instance'
          value: 'https://${entraTenantName}.ciamlogin.com/'
        }
        {
          name: 'AzureAd__TenantId'
          value: entraTenantId
        }
        {
          name: 'AzureAd__ClientId'
          value: entraApiClientId
        }
        {
          name: 'AZURE_STORAGE_ACCOUNT_NAME'
          value: storageAccountName
        }
      ]
    }
  }
}

output principalId string = appService.identity.principalId
output appServiceName string = appService.name
output appServiceHostname string = appService.properties.defaultHostName
