// Key Vault — replaces the root .env file's secrets for a real Azure deployment. RBAC
// authorization is used (enableRbacAuthorization: true) rather than vault access policies, which
// is the modern/recommended Key Vault authorization model.
//
// Secret naming convention: Key Vault secret names allow only alphanumerics and hyphens (no `__`
// or `:`, which is how ASP.NET Core's env-var config binder separates section/key). This module
// mirrors each `.env.example` variable name with `__` replaced by `--` (the standard convention
// Azure App Service / Container Apps' own Key-Vault-reference tooling documents for this exact
// translation), e.g. `JwtSettings__Secret` -> `JwtSettings--Secret`.
//
// Deliberately NOT done here: wiring these secrets into the Container Apps' `secretRef`/
// `keyVaultUrl` configuration (which additionally needs an RBAC role assignment granting each
// Container App's managed identity "Key Vault Secrets User" on this vault). That wiring is left
// as an explicit open question for Dan (see infra/README.md "Known gaps") rather than assumed,
// since it also requires deciding whether Container Apps should read secrets via Key Vault
// references or via its own native `secrets` block populated at deploy time — a design choice,
// not just plumbing.
//
// API version note: 2023-07-01 is a stable (non-preview) Microsoft.KeyVault/vaults version I'm
// confident is valid. Not compiled in this environment — verify against current docs before
// applying.

param namePrefix string
param location string

@description('Name of the Service Bus namespace provisioned by serviceBus.bicep — used only to look up its connection string via listKeys(), never to create/modify it.')
param serviceBusNamespaceName string

@secure()
param jwtSecret string

@secure()
param encryptionBankFieldKey string

@secure()
param smtpUsername string = ''

@secure()
param smtpPassword string = ''

@secure()
param cloudinaryCloudName string

@secure()
param cloudinaryApiKey string

@secure()
param cloudinaryApiSecret string

@secure()
param geminiApiKey string

@secure()
@description('Full ConnectionStrings:DefaultConnection value, built by main.bicep from the SQL module outputs + admin password.')
param apiDbConnectionString string

@secure()
@description('Full ConnectionStrings:TryOnConnection value, built by main.bicep from the SQL module outputs + admin password.')
param tryOnDbConnectionString string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${namePrefix}-kv'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Looked up rather than declared: every Service Bus namespace gets an implicit
// 'RootManageSharedAccessKey' authorization rule created by the platform; listKeys() on its
// resourceId is the standard ARM/Bicep pattern for retrieving a namespace-level connection
// string without a separate module output crossing a secure/non-secure boundary.
var serviceBusAuthRuleId = resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', serviceBusNamespaceName, 'RootManageSharedAccessKey')
var serviceBusConnectionString = listKeys(serviceBusAuthRuleId, '2021-11-01').primaryConnectionString

resource jwtSecretResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'JwtSettings--Secret'
  properties: {
    value: jwtSecret
  }
}

resource encryptionBankFieldKeyResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'EncryptionSettings--BankFieldKey'
  properties: {
    value: encryptionBankFieldKey
  }
}

resource smtpUsernameResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SmtpSettings--Username'
  properties: {
    value: smtpUsername
  }
}

resource smtpPasswordResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SmtpSettings--Password'
  properties: {
    value: smtpPassword
  }
}

resource cloudinaryCloudNameResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Cloudinary--CloudName'
  properties: {
    value: cloudinaryCloudName
  }
}

resource cloudinaryApiKeyResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Cloudinary--ApiKey'
  properties: {
    value: cloudinaryApiKey
  }
}

resource cloudinaryApiSecretResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Cloudinary--ApiSecret'
  properties: {
    value: cloudinaryApiSecret
  }
}

resource geminiApiKeyResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'GeminiSettings--ApiKey'
  properties: {
    value: geminiApiKey
  }
}

resource apiDbConnectionStringResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConnectionStrings--DefaultConnection'
  properties: {
    value: apiDbConnectionString
  }
}

resource tryOnDbConnectionStringResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConnectionStrings--TryOnConnection'
  properties: {
    value: tryOnDbConnectionString
  }
}

resource serviceBusConnectionStringResource 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ServiceBusSettings--ConnectionString'
  properties: {
    value: serviceBusConnectionString
  }
}

output vaultUri string = keyVault.properties.vaultUri
output vaultName string = keyVault.name
