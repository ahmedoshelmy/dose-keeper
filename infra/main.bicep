// Dose Keeper on Azure Functions Flex Consumption. Compile-only in CI (`az bicep build`); deploying is optional and manual:
//   az group create -n rg-dose-keeper -l <region>
//   az deployment group create -g rg-dose-keeper -f infra/main.bicep
// No keys: the app reaches storage with its system-assigned identity (AzureWebJobsStorage__accountName + RBAC).

@description('Short name used to derive resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'dosekeeper'

param location string = resourceGroup().location

@description('Instance memory for Flex Consumption (MB).')
@allowed([512, 2048, 4096])
param instanceMemoryMB int = 2048

@description('Upper bound on scale-out, keeps a runaway bill impossible.')
@minValue(1)
@maxValue(100)
param maximumInstanceCount int = 10

var suffix = uniqueString(resourceGroup().id, appName)
var storageName = take(toLower('st${appName}${suffix}'), 24)
var deploymentContainer = 'app-package'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: true
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }

  resource blobs 'blobServices' = {
    name: 'default'
    resource packages 'containers' = {
      name: deploymentContainer
    }
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${appName}-${suffix}'
  location: location
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${appName}-${suffix}'
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING', value: 'Authorization=AAD' }
        { name: 'RefillCheckSchedule', value: '0 0 2 * * *' }
        { name: 'WeeklyReportSchedule', value: '0 0 6 * * Sat' }
        { name: 'ReminderRepairSchedule', value: '0 */15 * * * *' }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer}'
          authentication: { type: 'SystemAssignedIdentity' }
        }
      }
      scaleAndConcurrency: {
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
}

// Data-plane roles the Functions host, Durable Functions (blobs, queues, tables) and the app's own adapters need.
var storageRoles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
]

resource storageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in storageRoles: {
    name: guid(storage.id, app.id, roleId)
    scope: storage
    properties: {
      principalId: app.identity.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    }
  }
]

resource metricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(insights.id, app.id, 'metrics')
  scope: insights
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb') // Monitoring Metrics Publisher
  }
}

output functionAppName string = app.name
output apiBaseUrl string = 'https://${app.properties.defaultHostName}/api'
