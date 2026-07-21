// Container Apps Environment + 3 Container Apps (api, tryon-api, storefront), mirroring the root
// docker-compose.yml topology. Chosen over App Service for Containers per D5/design spec §5:
// per-revision traffic splitting + scale-to-zero on the Consumption plan fits three
// independently-scaled services better than one App Service plan's shared sizing model.
//
// Images are referenced by tag but NOT pushed by this phase (D1) — the `*Image` params default to
// placeholders in the ACR this template also provisions; Dan (or a future CD pipeline) must build
// and push real images to that registry before these Container Apps can start successfully.
//
// Secret wiring: env blocks below carry only non-secret settings (ASPNETCORE_ENVIRONMENT /
// ASPNETCORE_HTTP_PORTS). The full secret matrix (JwtSettings__Secret, ConnectionStrings__*,
// Cloudinary__*, GeminiSettings__ApiKey, ServiceBusSettings__ConnectionString) is provisioned as
// Key Vault secrets by keyVault.bicep but deliberately NOT wired into these Container Apps'
// `secrets`/`secretRef` configuration here — see infra/README.md "Known gaps" for why this is left
// as an open decision rather than assumed.
//
// API version note: Microsoft.App/managedEnvironments and Microsoft.App/containerApps move fast;
// 2024-03-01 is a version I'm reasonably confident is a valid, stable GA API version, but Container
// Apps' schema has changed release-to-release (e.g. workload profiles) more than most resource
// providers, and this stack post-dates some of my training data. Verify this is still current
// against Microsoft Learn before applying — do not trust this version blindly.

param namePrefix string
param location string

@description('Container Apps Environment workload profile type. Consumption is the cheap, scale-to-zero-capable default.')
@allowed([
  'Consumption'
])
param skuName string = 'Consumption'

param logAnalyticsWorkspaceId string
param acrLoginServer string
param keyVaultUri string

@description('Fully-qualified image references; left as placeholders until an image is pushed to the ACR provisioned by acr.bicep.')
param apiImage string = '${acrLoginServer}/fashionsaas-api:latest'
param tryOnApiImage string = '${acrLoginServer}/fashionsaas-tryon:latest'
param storefrontImage string = '${acrLoginServer}/fashionsaas-storefront:latest'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-cae'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: skuName
        workloadProfileType: skuName
      }
    ]
  }
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    workloadProfileName: skuName
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

resource tryOnApiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-tryon-api'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    workloadProfileName: skuName
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'tryon-api'
          image: tryOnApiImage
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

resource storefrontApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-storefront'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    workloadProfileName: skuName
    configuration: {
      ingress: {
        external: true
        targetPort: 80
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'storefront'
          image: storefrontImage
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

// Note: keyVaultUri is accepted as a param but not yet consumed by a `secrets`/`secretRef` block
// (see the header comment) — kept on the module's signature so the day that wiring is implemented
// it's a body-only change, not a signature change. An unused param is a Bicep linter warning, not
// a build error.

output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
output tryOnApiFqdn string = tryOnApiApp.properties.configuration.ingress.fqdn
output storefrontFqdn string = storefrontApp.properties.configuration.ingress.fqdn
