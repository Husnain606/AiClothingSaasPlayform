// Azure SQL logical server + 2 databases (AiClothing, TryOnDb), mirroring the single
// docker-compose `sqlserver` container hosting both databases locally.
//
// API version note: 2021-11-01 is a long-stable, non-preview Microsoft.Sql API version I'm
// confident is valid and widely used in Microsoft's own quickstart templates. Not compiled in
// this environment — verify against current docs before applying; a newer stable version may
// exist by the time this is applied.

param namePrefix string
param location string

@description('Azure SQL Database SKU (DTU tier name). S0 is a cheap default for dev.')
param skuName string = 'S0'

param adminLogin string = 'fashionsaasadmin'

@secure()
param adminPassword string

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: '${namePrefix}-sql'
  location: location
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
  }
}

resource mainDb 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: 'AiClothing'
  location: location
  sku: {
    name: skuName
  }
}

resource tryOnDb 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: 'TryOnDb'
  location: location
  sku: {
    name: skuName
  }
}

// Least-restrictive Azure-documented special case meaning "allow Azure-hosted resources (e.g.
// Container Apps) to reach this server" — NOT "allow the public internet". See
// infra/README.md's "Known gaps" section: tighten to specific outbound IPs before production use.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output serverName string = sqlServer.name
output mainDbName string = mainDb.name
output tryOnDbName string = tryOnDb.name
