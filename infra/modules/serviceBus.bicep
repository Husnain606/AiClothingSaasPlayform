// Azure Service Bus namespace + tryon-events topic — the real-Azure counterpart to the local
// servicebus-emulator container. Topic properties mirror
// services/fashionsaas-tryon/servicebus-emulator-config.json exactly:
//   - DefaultMessageTimeToLive: PT1H
//   - DuplicateDetectionHistoryTimeWindow: PT20S
//   - RequiresDuplicateDetection: false
//   - main-api-tryon-results subscription — the main API's TryOnResultConsumer. D10's original
//     "publish-only, no subscriptions" decision anticipated this ("consumers, if any are added
//     later, own their own subscription"); the free-Hugging-Face async try-on flow added that
//     consumer. This is NOT optional: an Azure Service Bus topic with no subscription DISCARDS
//     every message, so without it every TryOnResultEvent would vanish in production and the
//     consumer could never attach.
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

// Mirrors the local emulator's subscription (servicebus-emulator-config.json) so dev and Azure
// agree on the name the main API binds to (ServiceBusSettings.SubscriptionName).
resource mainApiSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2021-11-01' = {
  parent: topic
  name: 'main-api-tryon-results'
  properties: {
    defaultMessageTimeToLive: 'PT1H'
    lockDuration: 'PT30S'
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
  }
}

output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
output topicName string = topic.name
