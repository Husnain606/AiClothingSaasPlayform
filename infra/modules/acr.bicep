// Azure Container Registry — target for the three application images (fashionsaas-api,
// fashionsaas-tryon, fashionsaas-storefront). No CI job pushes to it yet (Group E's CI workflow
// builds locally in the runner and discards the images, per D1/D4) — pushing here, and pointing
// Container Apps at real tags, is a follow-on CD step for Dan.
//
// API version note: 2023-07-01 is a stable (non-preview) Microsoft.ContainerRegistry/registries
// version I'm confident is valid as of my training data. Not compiled in this environment —
// verify against current docs before applying.

param namePrefix string
param location string

@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Basic'

// ACR names must be globally unique, 5-50 alphanumeric characters, no hyphens.
var registryName = replace('${namePrefix}acr', '-', '')

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: skuName
  }
  properties: {
    adminUserEnabled: false
  }
}

output loginServer string = acr.properties.loginServer
output registryName string = acr.name
output registryId string = acr.id
