targetScope = 'subscription'

@minLength(1)
@maxLength(64)
param environmentName string

param location string = 'australiaeast'

@secure()
param sqlAdminPassword string

param sqlAdminLogin string = 'sqladmin'

param developerIpAddress string = ''

// Microsoft Entra External ID — one external tenant serves both environments.
// Client IDs are not secrets; they are embedded in the public MSAL config.
// Fill these in after completing the Azure Portal setup (external tenant creation + app registrations).
var entraTenantName  = 'YOUR_EXTERNAL_TENANT_NAME'   // subdomain only, e.g. 'stallionsnoms'
var entraTenantId    = 'YOUR_EXTERNAL_TENANT_ID'      // GUID from external tenant overview
var entraApiClientId = environmentName == 'prod'
  ? 'YOUR_PROD_API_CLIENT_ID'
  : 'YOUR_DEV_API_CLIENT_ID'

var tags = {
  'azd-env-name': environmentName
  project: 'stallions-nominations-marketplace'
}

var resourceGroupName = 'rg-stallions-noms-${environmentName}'

var sqlConnectionString = 'Server=tcp:sql-stallions-noms-${environmentName}.database.windows.net,1433;Database=sqldb-stallions-noms-${environmentName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module storage './modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module sql './modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    developerIpAddress: developerIpAddress
  }
}

module keyvault './modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    sqlConnectionString: sqlConnectionString
  }
}

module appservice './modules/appservice.bicep' = {
  name: 'appservice'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyvault.outputs.keyVaultUri
    entraTenantId: entraTenantId
    entraTenantName: entraTenantName
    entraApiClientId: entraApiClientId
    storageAccountName: storage.outputs.storageAccountName
  }
}

module functions './modules/functions.bicep' = {
  name: 'functions'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    storageAccountName: storage.outputs.storageAccountName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyvault.outputs.keyVaultUri
  }
}

module rbac './modules/keyvault-rbac.bicep' = {
  name: 'rbac'
  scope: rg
  params: {
    keyVaultName: keyvault.outputs.keyVaultName
    storageAccountName: storage.outputs.storageAccountName
    appServicePrincipalId: appservice.outputs.principalId
    functionAppPrincipalId: functions.outputs.principalId
  }
}

output AZURE_RESOURCE_GROUP string = resourceGroupName
output AZURE_APP_SERVICE_NAME string = appservice.outputs.appServiceName
output AZURE_FUNCTION_APP_NAME string = functions.outputs.functionAppName
output AZURE_KEY_VAULT_NAME string = keyvault.outputs.keyVaultName
output AZURE_KEY_VAULT_URI string = keyvault.outputs.keyVaultUri
output AZURE_SQL_SERVER_FQDN string = sql.outputs.sqlServerFqdn
output AZURE_SQL_DATABASE_NAME string = sql.outputs.sqlDatabaseName
output AZURE_STORAGE_ACCOUNT_NAME string = storage.outputs.storageAccountName
output AZURE_APP_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.appInsightsConnectionString
