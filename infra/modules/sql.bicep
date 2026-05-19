param environmentName string
param location string
param tags object

@secure()
param sqlAdminPassword string
param sqlAdminLogin string = 'sqladmin'

// Dev only: set to your local IP to allow direct SQL connections for development
param developerIpAddress string = ''

var isDev = environmentName == 'dev'

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: 'sql-stallions-noms-${environmentName}'
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Dev only — allows a developer's machine to connect directly to SQL
resource allowDeveloperIp 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = if (isDev && !empty(developerIpAddress)) {
  parent: sqlServer
  name: 'DeveloperIp'
  properties: {
    startIpAddress: developerIpAddress
    endIpAddress: developerIpAddress
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: 'sqldb-stallions-noms-${environmentName}'
  location: location
  tags: tags
  sku: isDev ? {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  } : {
    name: 'GP_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: isDev ? {
    autoPauseDelay: 60
    minCapacity: '0.5'
    requestedBackupStorageRedundancy: 'Local'
  } : {
    requestedBackupStorageRedundancy: 'Geo'
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerName string = sqlServer.name
output sqlDatabaseName string = sqlDatabase.name
