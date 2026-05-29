param environmentName string
param location string
param tags object
param appInsightsConnectionString string
param keyVaultUri string
param b2cTenantId string
param b2cDomain string
param b2cClientId string
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
          // Always run as Production on Azure — dev/prod distinction is handled
          // by separate resource groups and Entra app registrations, not the runtime env.
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/SqlConnectionString/)'
        }
        {
          name: 'AzureAdB2C__Instance'
          value: 'https://stallionsaustralia.b2clogin.com'
        }
        {
          name: 'AzureAdB2C__Domain'
          value: b2cDomain
        }
        {
          name: 'AzureAdB2C__TenantId'
          value: b2cTenantId
        }
        {
          name: 'AzureAdB2C__ClientId'
          value: b2cClientId
        }
        {
          name: 'AzureAdB2C__SignUpSignInPolicyId'
          value: 'B2C_1_susi'
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
