# Azure Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provision fully isolated dev and prod Azure environments for the Stallions Nominations Marketplace using Bicep + azd, replacing the existing manually-created resource group.

**Architecture:** One `main.bicep` at subscription scope creates a resource group per environment and orchestrates six focused Bicep modules. A separate `keyvault-rbac.bicep` module breaks the circular dependency between Key Vault (which must exist before App Service can reference its URI) and App Service/Functions (which must exist before their managed identity principal IDs are known). RBAC role assignments run last.

**Tech Stack:** Azure Bicep, Azure Developer CLI (`azd`), Azure App Service (.NET 9), Azure SQL (General Purpose), Azure Blob Storage, Azure Key Vault, Azure Functions (Consumption), Azure Monitor / Log Analytics

---

## File Map

| File | Purpose |
|---|---|
| `azure.yaml` | Updated to wire azd to the `infra/` folder |
| `infra/main.bicep` | Subscription-scope entry point; creates RG, orchestrates modules |
| `infra/main.parameters.dev.json` | Dev environment parameter values |
| `infra/main.parameters.prod.json` | Prod environment parameter values |
| `infra/modules/monitoring.bicep` | Log Analytics workspace + Application Insights |
| `infra/modules/storage.bicep` | Storage Account + blob containers (stallion-images, nomination-documents) |
| `infra/modules/sql.bicep` | SQL Server + Database (Serverless for dev, Provisioned for prod) |
| `infra/modules/keyvault.bicep` | Key Vault (vault only — no role assignments) |
| `infra/modules/keyvault-rbac.bicep` | Key Vault Secrets User + Storage role assignments for App Service + Function App managed identities |
| `infra/modules/appservice.bicep` | App Service Plan (B2) + App Service (.NET 9, system-assigned identity) |
| `infra/modules/functions.bicep` | Function App Consumption Plan (Y1) + Function App (.NET 9 isolated, system-assigned identity) |

---

## Task 1: Check existing Key Vault secrets before deletion

**Files:** none — read-only check

- [ ] **Step 1: List secrets in the existing Key Vault**

```bash
az keyvault secret list --vault-name stallions-nominations-kv --output table
```

- [ ] **Step 2: For each secret, retrieve and note its value**

```bash
az keyvault secret show --vault-name stallions-nominations-kv --name <SECRET-NAME> --query value -o tsv
```

Note all values in a secure location (password manager). You will re-enter them later.

- [ ] **Step 3: Confirm you have captured all secrets, then proceed to Task 2**

---

## Task 2: Delete the old resource group and update azure.yaml

**Files:**
- Modify: `azure.yaml`

- [ ] **Step 1: Delete the old resource group**

```bash
az group delete --name stallions-nominations-rg --yes
```

Expected: command runs for ~1-2 minutes, no output on success.

- [ ] **Step 2: Update azure.yaml**

Replace the entire contents of `azure.yaml` with:

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/Azure/azure-dev/main/schemas/v1.0/azure.yaml.json

name: stallions-nominations-marketplace

infra:
  provider: bicep
  path: infra
```

- [ ] **Step 3: Commit**

```bash
git add azure.yaml
git commit -m "chore: wire azd to infra/ bicep folder"
```

---

## Task 3: Scaffold the infra folder structure

**Files:**
- Create: `infra/main.bicep` (stub)
- Create: `infra/modules/` (empty directory placeholder)

- [ ] **Step 1: Create the infra folder and stub main.bicep**

Create `infra/main.bicep` with:

```bicep
targetScope = 'subscription'

@minLength(1)
@maxLength(64)
param environmentName string

param location string = 'australiaeast'

var tags = {
  'azd-env-name': environmentName
  project: 'stallions-nominations-marketplace'
}

var resourceGroupName = 'rg-stallions-noms-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/main.bicep
```

Expected: no output (success). Any errors indicate syntax problems to fix before continuing.

- [ ] **Step 3: Commit**

```bash
git add infra/main.bicep
git commit -m "chore: scaffold infra folder with stub main.bicep"
```

---

## Task 4: Write monitoring.bicep

**Files:**
- Create: `infra/modules/monitoring.bicep`

- [ ] **Step 1: Create the module**

Create `infra/modules/monitoring.bicep` with:

```bicep
param environmentName string
param location string
param tags object

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-stallions-noms-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-stallions-noms-${environmentName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

output logAnalyticsId string = logAnalytics.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/monitoring.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/monitoring.bicep
git commit -m "feat(infra): add monitoring module (Log Analytics + App Insights)"
```

---

## Task 5: Write storage.bicep

**Files:**
- Create: `infra/modules/storage.bicep`

- [ ] **Step 1: Create the module**

Create `infra/modules/storage.bicep` with:

```bicep
param environmentName string
param location string
param tags object

var storageAccountName = 'ststallionsnoms${environmentName}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource stallionImagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'stallion-images'
  properties: {
    publicAccess: 'None'
  }
}

resource nominationDocumentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'nomination-documents'
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/storage.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/storage.bicep
git commit -m "feat(infra): add storage module with stallion-images and nomination-documents containers"
```

---

## Task 6: Write sql.bicep

**Files:**
- Create: `infra/modules/sql.bicep`

- [ ] **Step 1: Create the module**

Create `infra/modules/sql.bicep` with:

```bicep
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
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/sql.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/sql.bicep
git commit -m "feat(infra): add SQL module (Serverless dev / Provisioned prod)"
```

---

## Task 7: Write keyvault.bicep

**Files:**
- Create: `infra/modules/keyvault.bicep`

Note: This module creates the vault only. Role assignments are in `keyvault-rbac.bicep` (Task 9) to avoid a circular dependency with App Service and Function App managed identity principal IDs.

- [ ] **Step 1: Create the module**

Create `infra/modules/keyvault.bicep` with:

```bicep
param environmentName string
param location string
param tags object

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

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/keyvault.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/keyvault.bicep
git commit -m "feat(infra): add Key Vault module (RBAC-enabled, soft delete)"
```

---

## Task 8: Write appservice.bicep

**Files:**
- Create: `infra/modules/appservice.bicep`

- [ ] **Step 1: Create the module**

Create `infra/modules/appservice.bicep` with:

```bicep
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
      ]
    }
  }
}

output principalId string = appService.identity.principalId
output appServiceName string = appService.name
output appServiceHostname string = appService.properties.defaultHostName
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/appservice.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/appservice.bicep
git commit -m "feat(infra): add App Service module (B2, system-assigned identity, HTTPS-only)"
```

---

## Task 9: Write functions.bicep

**Files:**
- Create: `infra/modules/functions.bicep`

- [ ] **Step 1: Create the module**

Create `infra/modules/functions.bicep` with:

```bicep
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
```

Note: `AzureWebJobsStorage__accountName` uses the managed identity connection pattern (double underscore). This replaces the traditional connection string and requires Storage role assignments in `keyvault-rbac.bicep`.

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/functions.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/functions.bicep
git commit -m "feat(infra): add Function App module (Consumption Y1, managed identity storage)"
```

---

## Task 10: Write keyvault-rbac.bicep

**Files:**
- Create: `infra/modules/keyvault-rbac.bicep`

This module assigns roles to the App Service and Function App managed identities on both Key Vault and Storage. It runs last in `main.bicep` after all other modules have emitted their `principalId` outputs.

- [ ] **Step 1: Create the module**

Create `infra/modules/keyvault-rbac.bicep` with:

```bicep
param keyVaultName string
param storageAccountName string
param appServicePrincipalId string
param functionAppPrincipalId string

// Role definition IDs (built-in, immutable across all Azure tenants)
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageQueueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// App Service — Key Vault Secrets User
resource appServiceKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appServicePrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// App Service — Storage Blob Data Contributor
resource appServiceStorageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, appServicePrincipalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Key Vault Secrets User
resource functionAppKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionAppPrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Storage Blob Data Contributor (for app blobs)
resource functionAppStorageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionAppPrincipalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Storage Queue Data Contributor (Functions runtime)
resource functionAppStorageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionAppPrincipalId, storageQueueDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Function App — Storage Table Data Contributor (Functions runtime)
resource functionAppStorageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionAppPrincipalId, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}
```

- [ ] **Step 2: Validate it compiles**

```bash
az bicep build --file infra/modules/keyvault-rbac.bicep
```

Expected: no output.

- [ ] **Step 3: Commit**

```bash
git add infra/modules/keyvault-rbac.bicep
git commit -m "feat(infra): add RBAC role assignments for App Service + Function App managed identities"
```

---

## Task 11: Complete main.bicep

**Files:**
- Modify: `infra/main.bicep`

Replace the stub from Task 3 with the full orchestration.

- [ ] **Step 1: Replace infra/main.bicep with the complete version**

```bicep
targetScope = 'subscription'

@minLength(1)
@maxLength(64)
param environmentName string

param location string = 'australiaeast'

@secure()
param sqlAdminPassword string

param sqlAdminLogin string = 'sqladmin'

param developerIpAddress string = ''

var tags = {
  'azd-env-name': environmentName
  project: 'stallions-nominations-marketplace'
}

var resourceGroupName = 'rg-stallions-noms-${environmentName}'

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
```

- [ ] **Step 2: Validate the full template compiles**

```bash
az bicep build --file infra/main.bicep
```

Expected: no output. If errors appear they will reference a specific module file and line.

- [ ] **Step 3: Commit**

```bash
git add infra/main.bicep
git commit -m "feat(infra): complete main.bicep orchestration with all modules"
```

---

## Task 12: Write parameter files

**Files:**
- Create: `infra/main.parameters.dev.json`
- Create: `infra/main.parameters.prod.json`

- [ ] **Step 1: Create infra/main.parameters.dev.json**

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environmentName": {
      "value": "${AZURE_ENV_NAME}"
    },
    "location": {
      "value": "${AZURE_LOCATION}"
    },
    "sqlAdminPassword": {
      "value": "${SQL_ADMIN_PASSWORD}"
    },
    "developerIpAddress": {
      "value": "${DEVELOPER_IP_ADDRESS}"
    }
  }
}
```

- [ ] **Step 2: Create infra/main.parameters.prod.json**

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environmentName": {
      "value": "${AZURE_ENV_NAME}"
    },
    "location": {
      "value": "${AZURE_LOCATION}"
    },
    "sqlAdminPassword": {
      "value": "${SQL_ADMIN_PASSWORD}"
    }
  }
}
```

- [ ] **Step 3: Commit**

```bash
git add infra/main.parameters.dev.json infra/main.parameters.prod.json
git commit -m "feat(infra): add azd parameter files for dev and prod environments"
```

---

## Task 13: Configure and provision the dev environment

**Files:** none — azd commands only

- [ ] **Step 1: Create the azd dev environment**

```bash
azd env new dev
```

Expected: `New environment 'dev' created`

- [ ] **Step 2: Set the location**

```bash
azd env set AZURE_LOCATION australiaeast --environment dev
```

- [ ] **Step 3: Set the SQL admin password**

Choose a strong password (16+ characters, uppercase, lowercase, number, symbol).

```bash
azd env set SQL_ADMIN_PASSWORD "<your-strong-password>" --environment dev
```

- [ ] **Step 4: Preview changes using Azure CLI what-if**

```bash
az deployment sub what-if \
  --location australiaeast \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.dev.json \
  --parameters environmentName=dev sqlAdminPassword="$SQL_ADMIN_PASSWORD"
```

Review the output — confirm the resource group `rg-stallions-noms-dev` and all expected resources appear as `+ Create`. No resources should show as modified or deleted.

- [ ] **Step 5: Provision dev**

```bash
azd provision --environment dev
```

Expected: All resources created successfully in `rg-stallions-noms-dev`. This takes approximately 5-10 minutes. Watch for any errors and resolve before continuing.

- [ ] **Step 6: Verify in Azure portal or CLI**

```bash
az resource list --resource-group rg-stallions-noms-dev --output table
```

Expected: All 9+ resources listed (App Service Plan, App Service, SQL Server, SQL Database, Storage Account, Key Vault, Function App Consumption Plan, Function App, Log Analytics, App Insights).

---

## Task 14: Create Entra ID app registrations (manual)

This task is performed in the Azure portal — Bicep cannot create app registrations.

- [ ] **Step 1: Create the dev app registration**

1. Go to [portal.azure.com](https://portal.azure.com) → Microsoft Entra ID → App registrations → New registration
2. Name: `stallions-noms-dev`
3. Supported account types: `Accounts in this organizational directory only`
4. Redirect URI: `Single-page application (SPA)` → `https://app-stallions-noms-dev.azurewebsites.net/authentication/login-callback`
5. Click Register

- [ ] **Step 2: Enable access and ID tokens**

Under the `stallions-noms-dev` registration → Authentication → check both `Access tokens` and `ID tokens` → Save.

- [ ] **Step 3: Add app roles**

Under `stallions-noms-dev` → App roles → Create app role:

| Display name | Value | Allowed member types |
|---|---|---|
| Buyer | `buyer` | Users/Groups |
| Stud Farm Admin | `stud-farm-admin` | Users/Groups |
| Stallions Staff | `stallions-staff` | Users/Groups |

- [ ] **Step 4: Store client ID and tenant ID in Key Vault**

```bash
az keyvault secret set --vault-name kv-stallions-noms-dev --name EntraClientId --value "<application-client-id-from-portal>"
az keyvault secret set --vault-name kv-stallions-noms-dev --name EntraTenantId --value "<directory-tenant-id-from-portal>"
```

- [ ] **Step 5: Repeat Steps 1–4 for prod**

Registration name: `stallions-noms-prod`
Redirect URI: `https://app-stallions-noms-prod.azurewebsites.net/authentication/login-callback`
Key Vault: `kv-stallions-noms-prod`

---

## Task 15: Provision the prod environment

**Files:** none — azd commands only

- [ ] **Step 1: Create the azd prod environment**

```bash
azd env new prod
```

- [ ] **Step 2: Set location and SQL password**

```bash
azd env set AZURE_LOCATION australiaeast --environment prod
azd env set SQL_ADMIN_PASSWORD "<different-strong-password-for-prod>" --environment prod
```

Use a different password from dev.

- [ ] **Step 3: Preview the prod deployment**

```bash
az deployment sub what-if \
  --location australiaeast \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.prod.json \
  --parameters environmentName=prod sqlAdminPassword="$SQL_ADMIN_PASSWORD"
```

Confirm `rg-stallions-noms-prod` and all resources appear as `+ Create` with correct prod configuration (provisioned SQL, always-on App Service).

- [ ] **Step 4: Provision prod**

```bash
azd provision --environment prod
```

Expected: All resources created in `rg-stallions-noms-prod`.

- [ ] **Step 5: Verify**

```bash
az resource list --resource-group rg-stallions-noms-prod --output table
```

Expected: Same resource set as dev, confirmed in Australia East.

- [ ] **Step 6: Final commit — add .gitignore for azd env files**

azd stores environment state in `.azure/` — the `.env` files inside contain secrets and must not be committed.

Create `.gitignore` (or add to existing) with:

```
.azure/**/.env
```

```bash
git add .gitignore
git commit -m "chore: ignore azd .env files containing secrets"
```
