// Log Analytics workspace — required as the log sink for the Container Apps Environment
// (Microsoft.App/managedEnvironments.properties.appLogsConfiguration).
//
// API version note: 2023-09-01 is a stable (non-preview) Microsoft.OperationalInsights/workspaces
// version I'm confident is valid, but this module was never compiled against the live Azure
// resource provider in this environment — verify it is still current before applying.

param namePrefix string
param location string

@description('Log retention in days. 30 is the cheap default; raise for prod compliance needs.')
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

output workspaceId string = workspace.id
output workspaceName string = workspace.name
