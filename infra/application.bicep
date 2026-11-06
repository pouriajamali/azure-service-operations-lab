targetScope = 'resourceGroup'
param location string = resourceGroup().location
param appName string = 'ops-lab-api'
@description('Public image pinned by sha256, from the verified source commit.')
@minLength(80)
param image string
param environmentId string
param identityId string
param secretUri string
param sourceCommit string
param revisionSuffix string

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Multiple'
      secrets: [{ name: 'dependency-token', keyVaultUrl: secretUri, identity: identityId }]
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
        traffic: [{ latestRevision: true, weight: 100 }]
      }
    }
    template: {
      revisionSuffix: revisionSuffix
      containers: [{
        name: 'api'
        image: image
        env: [
          { name: 'LAB_DEPENDENCY_TOKEN', secretRef: 'dependency-token' }
          { name: 'SOURCE_COMMIT', value: sourceCommit }
        ]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        probes: [
          { type: 'Startup', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 5, failureThreshold: 24 }
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 10, failureThreshold: 3 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 5, failureThreshold: 3 }
        ]
      }]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '4' } } }]
      }
    }
  }
}
output baseUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
