// Azure Service Bus namespace + tryon-events topic — the real-Azure counterpart to the local
// servicebus-emulator container. Topic properties mirror
// services/fashionsaas-tryon/servicebus-emulator-config.json exactly:
//   - DefaultMessageTimeToLive: PT1H
//   - DuplicateDetectionHistoryTimeWindow: PT20S
//   - RequiresDuplicateDetection: false
//   - No subscriptions — publish-only (D10 locked decision: the try-on service only ever
//     publishes to this topic; consumers, if any are added later, own their own subscription).
//
// API version note: 2021-11-01 is a stable (non-preview) Microsoft.ServiceBus version I'm
// confident is valid. Not compiled in this environment — verify against current docs before
// applying.

param namePrefix string
param location string

@description('Standard is the cheap default and the minimum tier that supports topics (Basic does not).')
@allowed([
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

resource namespace 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: '${namePrefix}-sb'
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2021-11-01' = {
  parent: namespace
  name: 'tryon-events'
  properties: {
    defaultMessageTimeToLive: 'PT1H'
    duplicateDetectionHistoryTimeWindow: 'PT20S'
    requiresDuplicateDetection: false
  }
}

output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
output topicName string = topic.name
