// ============================================================================
// PEMP — core Azure infrastructure (UK region only, per SRS §9 / CON-01).
// Provisions: Log Analytics + App Insights, Key Vault, Azure SQL (Entra-auth),
// private Storage (evidence/reports), and a Linux Web App for the Blazor app with
// a system-assigned managed identity wired to Key Vault + SQL + Blob (no secrets
// in config — SEC-CRD/SEC-DAT). Entra app registration is done outside Bicep
// (see docs/azure-entra-setup.md); its IDs are passed in as parameters.
// ============================================================================

@description('UK region only — no data egress outside the approved boundary (CON-01).')
@allowed([ 'uksouth', 'ukwest' ])
param location string = 'uksouth'

@description('Short prefix for resource names, e.g. "pemp" or "pemp-prod".')
@minLength(3)
@maxLength(11)
param namePrefix string = 'pemp'

@description('Entra object id of the SQL Entra administrator (a user or group).')
param sqlAdminObjectId string

@description('Display name of the SQL Entra administrator.')
param sqlAdminLogin string

@description('Entra tenant id (Acme tenant) for app auth.')
param entraTenantId string = subscription().tenantId

@description('Entra app (client) id of the registered PEMP web app — see setup guide.')
param entraClientId string

var suffix = uniqueString(resourceGroup().id)
var tags = { app: 'PEMP', env: namePrefix, dataResidency: 'UK' }
// KV + Storage names: lowercase alphanumeric only, max 24 chars.
var alphaPrefix = toLower(replace(namePrefix, '-', ''))

// ---- Observability (NFR-MNT / SEC-INS-03) ---------------------------------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs-${suffix}'
  location: location
  tags: tags
  properties: { retentionInDays: 90, sku: { name: 'PerGB2018' } }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

// ---- Key Vault (SEC-CRD-01) — RBAC, soft-delete, no public secrets ---------
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('${alphaPrefix}kv${suffix}', 24)
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: entraTenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
  }
}

// ---- Private storage for evidence/reports (SEC-EVD-01) ---------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('${alphaPrefix}st${suffix}', 24)
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Disabled'
  }
}

// ---- Azure SQL (Entra-only auth — passwordless, SEC-DAT-02) ----------------
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${namePrefix}-sql-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled' // tighten to Private Endpoint in production
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: entraTenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'pemp'
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose' } // serverless general purpose
  properties: { zoneRedundant: false }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// ---- App hosting (Linux, .NET 10) -----------------------------------------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-plan-${suffix}'
  location: location
  tags: tags
  sku: { name: 'B1', tier: 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: '${namePrefix}-web-${suffix}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'UseSqlite', value: 'false' }
        { name: 'ConnectionStrings__Pemp', value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=pemp;Authentication=Active Directory Default;Encrypt=True;' }
        { name: 'AzureAd__Instance', value: environment().authentication.loginEndpoint }
        { name: 'AzureAd__TenantId', value: entraTenantId }
        { name: 'AzureAd__ClientId', value: entraClientId }
        { name: 'KeyVault__Uri', value: vault.properties.vaultUri }
        { name: 'Storage__BlobEndpoint', value: storage.properties.primaryEndpoints.blob }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
    }
  }
}

// ---- Role assignments for the Web App managed identity ----------------------
// Key Vault Secrets User
resource kvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, web.id, 'kv-secrets-user')
  scope: vault
  properties: {
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

// Storage Blob Data Contributor
resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, web.id, 'blob-data-contributor')
  scope: storage
  properties: {
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

output webAppName string = web.name
output webAppUrl string = 'https://${web.properties.defaultHostName}'
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output keyVaultUri string = vault.properties.vaultUri
output webAppPrincipalId string = web.identity.principalId
