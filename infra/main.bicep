// Crux AI hosted demo. South Africa North, lean SKUs.
// App Service Linux (.NET 8) + Azure SQL Basic + Blob. Optional Key Vault and App Insights.
// Blazor Interactive Server needs WebSockets and ARR affinity, which App Service provides
// without a container registry.

@description('Azure region. South Africa North is the default.')
param location string = 'southafricanorth'

@description('Short name prefix. Lowercase letters and digits. Used in globally unique resource names.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'cruxai'

@description('Azure SQL admin login. SQL authentication is used for the app connection.')
param sqlAdminLogin string = 'cruxadmin'

@description('Azure SQL admin password. At least 12 characters with upper, lower, digit, and symbol. Do not use a semicolon; it breaks the connection string. Do not commit this value.')
@secure()
@minLength(12)
param sqlAdminPassword string

@description('Azure SQL database name. Must be empty on first boot; the app creates tables with EF EnsureCreated.')
param sqlDatabaseName string = 'cruxai'

@description('App Service plan SKU. B1 is the lean Linux size with Always On. Staging slots force at least S1.')
@allowed([
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
])
param appServiceSku string = 'B1'

@description('Create a staging slot. Basic SKUs are raised to S1 because slots require Standard.')
param enableStagingSlot bool = false

@description('Store SQL, storage, and Entra secrets in Key Vault and point App Service at them with references. Requires keyVaultAdminObjectId.')
param enableKeyVault bool = false

@description('Object id of the principal running this deployment (az ad signed-in-user show --query id -o tsv). Required when enableKeyVault is true so the template can write secrets.')
param keyVaultAdminObjectId string = ''

@description('Create a Log Analytics workspace and Application Insights. Off by default to keep the demo lean.')
param enableAppInsights bool = false

@description('Demo, or EntraExternalId for the five-minute hosted path. Entra still needs the tenant settings below.')
@allowed([
  'Demo'
  'EntraExternalId'
])
param authProvider string = 'Demo'

@description('External ID authority host, including the trailing slash. Example: https://harbour.ciamlogin.com/')
param entraInstance string = ''

@description('External ID domain. Example: harbour.onmicrosoft.com')
param entraDomain string = ''

@description('External ID tenant id (directory id).')
param entraTenantId string = ''

@description('App registration client id.')
param entraClientId string = ''

@description('App registration client secret. Do not commit this value.')
@secure()
param entraClientSecret string = ''

@description('Organisation created for the first Entra sign-in.')
param defaultOrganizationName string = 'Harbour Street Studio'

var suffix = uniqueString(resourceGroup().id)
var compactPrefix = toLower(replace(namePrefix, '-', ''))
var storageAccountName = take('${compactPrefix}${suffix}', 24)
var webAppName = '${namePrefix}-${suffix}'
var planName = 'plan-${namePrefix}-${suffix}'
var sqlServerName = 'sql-${namePrefix}-${suffix}'
var keyVaultName = take('kv${compactPrefix}${suffix}', 24)
var workspaceName = take('log-${namePrefix}-${suffix}', 63)
var appInsightsName = take('appi-${namePrefix}-${suffix}', 63)
var requestedTier = startsWith(appServiceSku, 'S') ? 'Standard' : 'Basic'
var effectiveSku = enableStagingSlot && requestedTier == 'Basic' ? 'S1' : appServiceSku
var effectiveTier = enableStagingSlot && requestedTier == 'Basic' ? 'Standard' : requestedTier
var tags = {
  project: 'crux-ai'
  environment: 'demo'
}
var tenantId = subscription().tenantId
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var keyVaultHost = enableKeyVault ? 'https://${keyVault!.name}${environment().suffixes.keyvaultDns}' : ''
var sqlAppSetting = enableKeyVault ? '@Microsoft.KeyVault(SecretUri=${keyVaultHost}/secrets/sql-connection/)' : sqlConnectionString
var blobAppSetting = enableKeyVault ? '@Microsoft.KeyVault(SecretUri=${keyVaultHost}/secrets/blob-connection/)' : storageConnectionString
var entraSecretAppSetting = enableKeyVault && !empty(entraClientSecret) ? '@Microsoft.KeyVault(SecretUri=${keyVaultHost}/secrets/entra-client-secret/)' : entraClientSecret
var baseSettings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'true'
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
  ENABLE_ORYX_BUILD: 'false'
  WEBSITE_RUN_FROM_PACKAGE: '1'
  Database__Provider: 'AzureSql'
  ConnectionStrings__AzureSql: sqlAppSetting
  Storage__Provider: 'AzureBlob'
  Storage__AzureBlob__ConnectionString: blobAppSetting
  Storage__AzureBlob__ContainerName: 'csv-uploads'
  Storage__DataProtection__ContainerName: 'crux-keys'
  Auth__Provider: authProvider
  Auth__DefaultOrganizationName: defaultOrganizationName
  Analytics__Sink: 'File'
  Analytics__FilePath: '/home/crux/partner-events.jsonl'
}
var entraSettings = authProvider == 'EntraExternalId' ? {
  Auth__EntraExternalId__Instance: entraInstance
  Auth__EntraExternalId__Domain: entraDomain
  Auth__EntraExternalId__TenantId: entraTenantId
  Auth__EntraExternalId__ClientId: entraClientId
  Auth__EntraExternalId__ClientSecret: entraSecretAppSetting
  Auth__EntraExternalId__CallbackPath: '/signin-oidc'
  Auth__EntraExternalId__SignedOutCallbackPath: '/signout-callback-oidc'
} : {}
var insightsConnection = enableAppInsights ? appInsights!.properties.ConnectionString : ''
var insightsSettings = enableAppInsights ? {
  APPLICATIONINSIGHTS_CONNECTION_STRING: insightsConnection
  ApplicationInsights__ConnectionString: insightsConnection
} : {}
var appSettings = union(union(baseSettings, entraSettings), insightsSettings)

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: {
    name: effectiveSku
    tier: effectiveTier
    capacity: 1
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      webSocketsEnabled: true
      http20Enabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/healthz'
      use32BitWorkerProcess: false
    }
  }
}

resource staging 'Microsoft.Web/sites/slots@2023-12-01' = if (enableStagingSlot) {
  parent: web
  name: 'staging'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      webSocketsEnabled: true
      http20Enabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/healthz'
      use32BitWorkerProcess: false
    }
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    version: '12.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource azureServicesFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource csvContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'csv-uploads'
  properties: {
    publicAccess: 'None'
  }
}

resource keysContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'crux-keys'
  properties: {
    publicAccess: 'None'
  }
}

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (enableAppInsights) {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = if (enableAppInsights) {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
    IngestionMode: 'LogAnalytics'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = if (enableKeyVault) {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: false
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: false
    publicNetworkAccess: 'Enabled'
    accessPolicies: concat(
      [
        {
          tenantId: tenantId
          objectId: web.identity.principalId
          permissions: {
            secrets: [
              'get'
              'list'
            ]
          }
        }
      ],
      enableStagingSlot
        ? [
            {
              tenantId: tenantId
              objectId: staging!.identity.principalId
              permissions: {
                secrets: [
                  'get'
                  'list'
                ]
              }
            }
          ]
        : [],
      empty(keyVaultAdminObjectId)
        ? []
        : [
            {
              tenantId: tenantId
              objectId: keyVaultAdminObjectId
              permissions: {
                secrets: [
                  'get'
                  'list'
                  'set'
                  'delete'
                ]
              }
            }
          ]
    )
  }
}

resource sqlSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVault) {
  parent: keyVault
  name: 'sql-connection'
  properties: {
    value: sqlConnectionString
  }
}

resource blobSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVault) {
  parent: keyVault
  name: 'blob-connection'
  properties: {
    value: storageConnectionString
  }
}

resource entraSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVault && !empty(entraClientSecret)) {
  parent: keyVault
  name: 'entra-client-secret'
  properties: {
    value: entraClientSecret
  }
}

resource webSettings 'Microsoft.Web/sites/config@2023-12-01' = if (!enableKeyVault) {
  parent: web
  name: 'appsettings'
  properties: appSettings
  dependsOn: [
    csvContainer
    keysContainer
    sqlDatabase
  ]
}

resource webSettingsKv 'Microsoft.Web/sites/config@2023-12-01' = if (enableKeyVault && empty(entraClientSecret)) {
  parent: web
  name: 'appsettings'
  properties: appSettings
  dependsOn: [
    csvContainer
    keysContainer
    sqlDatabase
    sqlSecret
    blobSecret
  ]
}

resource webSettingsKvEntra 'Microsoft.Web/sites/config@2023-12-01' = if (enableKeyVault && !empty(entraClientSecret)) {
  parent: web
  name: 'appsettings'
  properties: appSettings
  dependsOn: [
    csvContainer
    keysContainer
    sqlDatabase
    sqlSecret
    blobSecret
    entraSecret
  ]
}

resource stagingSettings 'Microsoft.Web/sites/slots/config@2023-12-01' = if (enableStagingSlot && !enableKeyVault) {
  parent: staging
  name: 'appsettings'
  properties: appSettings
  dependsOn: [
    csvContainer
    keysContainer
    sqlDatabase
  ]
}

resource stagingSettingsKv 'Microsoft.Web/sites/slots/config@2023-12-01' = if (enableStagingSlot && enableKeyVault && empty(entraClientSecret)) {
  parent: staging
  name: 'appsettings'
  properties: appSettings
  dependsOn: [
    csvContainer
    keysContainer
    sqlDatabase
    sqlSecret
    blobSecret
  ]
}

resource stagingSettingsKvEntra 'Microsoft.Web/sites/slots/config@2023-12-01' = if (enableStagingSlot && enableKeyVault && !empty(entraClientSecret)) {
  parent: staging
  name: 'appsettings'
  properties: appSettings
  dependsOn: [
    csvContainer
    keysContainer
    sqlDatabase
    sqlSecret
    blobSecret
    entraSecret
  ]
}

output webAppName string = web.name
output webAppUrl string = 'https://${web.properties.defaultHostName}'
output healthUrl string = 'https://${web.properties.defaultHostName}/healthz'
output stagingSlot string = enableStagingSlot ? 'staging' : ''
output stagingUrl string = enableStagingSlot ? 'https://${webAppName}-staging.azurewebsites.net' : ''
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output storageAccountName string = storage.name
output blobContainerName string = csvContainer.name
output keyVaultName string = enableKeyVault ? keyVault.name : ''
output appServiceSku string = effectiveSku
output authProvider string = authProvider
output skuNote string = effectiveSku == appServiceSku ? '' : 'Staging slots need Standard. The plan was raised from ${appServiceSku} to ${effectiveSku}.'
output keyVaultNote string = enableKeyVault && empty(keyVaultAdminObjectId) ? 'enableKeyVault is true but keyVaultAdminObjectId is empty. Secret creation will fail until that object id is supplied.' : ''
