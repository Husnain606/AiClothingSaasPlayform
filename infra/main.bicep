// Phase 8 (Groups F-G) — FashionSaaS Azure infrastructure orchestrator.
//
// TEMPLATES ONLY. This has NOT been compiled (`az bicep build`) or deployed (`az deployment ...
// what-if` / `create`) in this environment — az/Bicep CLI are not installed here. Manually
// reviewed for Bicep syntax correctness only. Dan must validate before applying — see
// infra/README.md.
//
// Deployed at subscription scope because this template also creates the resource group itself
// (so a single `az deployment sub create` stands up the whole environment from nothing) — see
// infra/README.md for the full rationale.

targetScope = 'subscription'

@description('Short environment name, e.g. dev, staging, prod. Used to derive all resource names.')
@minLength(2)
@maxLength(10)
param environmentName string

@description('Azure region for all resources.')
param location string = 'eastus'

@description('SKU for the Azure SQL Database (main + try-on). Cheap default for dev; use a higher tier for staging/prod.')
param sqlSkuName string = 'S0'

@description('SKU for the Azure Container Registry.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param acrSkuName string = 'Basic'

@description('SKU for the Azure Service Bus namespace. Topics require at least Standard.')
@allowed([
  'Standard'
  'Premium'
])
param serviceBusSkuName string = 'Standard'

@description('SQL administrator login name.')
param sqlAdminLogin string = 'fashionsaasadmin'

@secure()
@description('SQL administrator login password. Never checked in — pass at deploy time (parameter file, prompt, or Key Vault reference in a pipeline).')
param sqlAdminPassword string

@secure()
@description('Maps to JwtSettings:Secret (HS256 signing key, >= 32 chars). Shared by api and tryon-api.')
param jwtSecret string

@secure()
@description('Maps to EncryptionSettings:BankFieldKey (AES key for bank-field column encryption).')
param encryptionBankFieldKey string

@secure()
@description('Maps to SmtpSettings:Username. Optional — leave empty if real email sending is not needed yet.')
param smtpUsername string = ''

@secure()
@description('Maps to SmtpSettings:Password. Optional — leave empty if real email sending is not needed yet.')
param smtpPassword string = ''

@secure()
@description('Maps to Cloudinary:CloudName.')
param cloudinaryCloudName string

@secure()
@description('Maps to Cloudinary:ApiKey.')
param cloudinaryApiKey string

@secure()
@description('Maps to Cloudinary:ApiSecret.')
param cloudinaryApiSecret string

@secure()
@description('Maps to GeminiSettings:ApiKey (Google Gemini API key).')
param geminiApiKey string

var resourceGroupName = 'rg-fashionsaas-${environmentName}'
var namePrefix = 'fsaas-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
}

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    skuName: acrSkuName
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    skuName: sqlSkuName
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
  }
}

module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBus'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    skuName: serviceBusSkuName
  }
}

// Connection strings are built here (not returned as module *outputs*, since Bicep cannot mark an
// output @secure() — only params — and secret-bearing outputs land in plaintext in the deployment
// history). They are consumed immediately below as secure module *parameters* into keyVault,
// which is the supported pattern for passing a computed secret between modules.
var apiDbConnectionString = 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sql.outputs.mainDbName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var tryOnDbConnectionString = 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sql.outputs.tryOnDbName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    jwtSecret: jwtSecret
    encryptionBankFieldKey: encryptionBankFieldKey
    smtpUsername: smtpUsername
    smtpPassword: smtpPassword
    cloudinaryCloudName: cloudinaryCloudName
    cloudinaryApiKey: cloudinaryApiKey
    cloudinaryApiSecret: cloudinaryApiSecret
    geminiApiKey: geminiApiKey
    apiDbConnectionString: apiDbConnectionString
    tryOnDbConnectionString: tryOnDbConnectionString
  }
}

module containerApps 'modules/containerApps.bicep' = {
  name: 'containerApps'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    acrLoginServer: acr.outputs.loginServer
    keyVaultUri: keyVault.outputs.vaultUri
  }
}

output resourceGroupName string = rg.name
output acrLoginServer string = acr.outputs.loginServer
output sqlServerFqdn string = sql.outputs.serverFqdn
output serviceBusNamespaceFqdn string = serviceBus.outputs.namespaceFqdn
output keyVaultName string = keyVault.outputs.vaultName
output apiFqdn string = containerApps.outputs.apiFqdn
output tryOnApiFqdn string = containerApps.outputs.tryOnApiFqdn
output storefrontFqdn string = containerApps.outputs.storefrontFqdn
