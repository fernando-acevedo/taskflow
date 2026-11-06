// TaskFlow public demo: App Service F1 (API), Static Web Apps Free (frontend), Azure SQL free offer.
// Deploy with infra/deploy.ps1 or infra/deploy.sh. See infra/README.md and docs/adr/0011-azure-hosting-with-bicep.md.
targetScope = 'resourceGroup'

@description('Prefix for resource names. Lowercase letters and digits.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'taskflow'

@description('Region for the web app, plan and SQL. The Azure SQL free offer is expected here (East US).')
param location string = 'eastus'

@description('Region for Static Web Apps. The Free plan is only available in a few regions (westus2, centralus, eastus2, westeurope, eastasia).')
@allowed(['westus2', 'centralus', 'eastus2', 'westeurope', 'eastasia'])
param staticWebAppLocation string = 'eastus2'

@description('SQL Server administrator login (SQL authentication; fine for a demo).')
param sqlAdminLogin string = 'taskflowadmin'

@secure()
@minLength(8)
@description('SQL Server administrator password. Azure SQL complexity (3 of: upper, lower, digit, symbol). Do not use semicolons or single quotes: it is interpolated into the connection string.')
param sqlAdminPassword string

@secure()
@minLength(8)
@description('Password of the demo admin account (Demo:Accounts:0). Policy: 8+ characters with an uppercase letter, a lowercase letter and a digit.')
param demoAdminPassword string

@secure()
@minLength(8)
@description('Password of the demo member account (Demo:Accounts:1). Same policy: 8+ characters with upper, lower and a digit.')
param demoMemberPassword string

@description('Five-field cron expression of the demo reset, evaluated in demoTimeZone.')
param demoResetCron string = '0 3 * * *'

@description('Time zone id used to evaluate demoResetCron.')
param demoTimeZone string = 'UTC'

@description('Email of the real admin account that the demo reset preserves (Seed:Admin:Email).')
param seedAdminEmail string

@secure()
@minLength(8)
@description('Password of the real admin (Seed:Admin:Password). Policy: 8+ characters with upper, lower and a digit.')
param seedAdminPassword string

@description('Display name of the real admin (Seed:Admin:DisplayName).')
param seedAdminDisplayName string = 'Administrator'

@description('Networks (CIDR) trusted for X-Forwarded-For, so the per-IP auth rate limit sees the real client address. Defaults to the private ranges App Service front ends use. Never leave it empty: every client would then share the proxy address. Verify after the first deploy (infra/README.md).')
param forwardedHeadersKnownNetworks array = [
  '10.0.0.0/8'
  '172.16.0.0/12'
  '192.168.0.0/16'
  '169.254.0.0/16'
]

@description('SMTP host. Empty (default) means no email is sent: the demo works, but invitation and password reset emails are not delivered.')
param smtpHost string = ''

@description('SMTP port (Email:Smtp:Port).')
param smtpPort int = 587

@description('SMTP security mode (Email:Smtp:Security).')
param smtpSecurity string = 'StartTls'

@description('SMTP user name (Email:Smtp:Username). Optional.')
param smtpUsername string = ''

@secure()
@description('SMTP password (Email:Smtp:Password). Optional.')
param smtpPassword string = ''

@description('From address of outgoing email (Email:FromAddress). Most SMTP providers require it.')
param emailFromAddress string = ''

@description('Health check path of the web app. Empty disables it (the App Service health check is not offered on the Free plan).')
param healthCheckPath string = ''

var suffix = uniqueString(resourceGroup().id)
var planName = '${namePrefix}-plan-${suffix}'
var webAppName = '${namePrefix}-api-${suffix}'
var sqlServerName = '${namePrefix}-sql-${suffix}'
var sqlDatabaseName = 'taskflow'
var staticWebAppName = '${namePrefix}-web-${suffix}'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true // required for Linux
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Local'
    maxSizeBytes: 34359738368 // 32 GB, the free offer limit
  }
}

// 0.0.0.0 - 0.0.0.0 is the special range that means "allow Azure services".
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: staticWebAppLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// Connect Timeout=60 gives the serverless database time to resume after auto-pause.
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;'

var frontendOrigin = 'https://${staticWebApp.properties.defaultHostname}'

var baseAppSettings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Database__MigrateOnStartup', value: 'true' }
  { name: 'Database__SeedOnStartup', value: 'true' }
  { name: 'Cors__AllowedOrigins__0', value: frontendOrigin }
  // Base URL of the frontend: the emailed invitation and reset links point to it.
  { name: 'Email__FrontendUrl', value: frontendOrigin }
  { name: 'Auth__AllowSelfRegistration', value: 'false' }
  // /home is persistent storage on App Service Linux, so data protection keys (and the tokens they sign) survive restarts.
  { name: 'DataProtection__KeysPath', value: '/home/data/keys' }
  { name: 'Seed__Admin__Email', value: seedAdminEmail }
  { name: 'Seed__Admin__Password', value: seedAdminPassword }
  { name: 'Seed__Admin__DisplayName', value: seedAdminDisplayName }
  { name: 'Demo__Enabled', value: 'true' }
  // Explicit opt-in the API requires for the nightly wipe; Demo__Enabled alone does not wipe.
  { name: 'Demo__AllowDataWipe', value: 'true' }
  { name: 'Demo__ResetCron', value: demoResetCron }
  { name: 'Demo__TimeZone', value: demoTimeZone }
  // Indices follow Demo:Accounts in appsettings.json: 0 = admin, 1 = member.
  { name: 'Demo__Accounts__0__Password', value: demoAdminPassword }
  { name: 'Demo__Accounts__1__Password', value: demoMemberPassword }
]

// X-Forwarded-For is only believed from these networks (the App Service front end uses private addresses).
var forwardedHeadersSettings = [for (network, i) in forwardedHeadersKnownNetworks: {
  name: 'ForwardedHeaders__KnownNetworks__${i}'
  value: network
}]

// Only added when a host is set; without it the API sends no email.
var smtpSettings = empty(smtpHost) ? [] : [
  { name: 'Email__Smtp__Host', value: smtpHost }
  { name: 'Email__Smtp__Port', value: string(smtpPort) }
  { name: 'Email__Smtp__Security', value: smtpSecurity }
  { name: 'Email__Smtp__Username', value: smtpUsername }
  { name: 'Email__Smtp__Password', value: smtpPassword }
  { name: 'Email__FromAddress', value: emailFromAddress }
]

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // not available on F1
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: empty(healthCheckPath) ? null : healthCheckPath
      appSettings: concat(baseAppSettings, forwardedHeadersSettings, smtpSettings)
      connectionStrings: [
        {
          name: 'TaskFlow'
          type: 'SQLAzure'
          connectionString: sqlConnectionString
        }
      ]
    }
  }
}

output apiUrl string = 'https://${webApp.properties.defaultHostName}'
output staticWebAppHostname string = staticWebApp.properties.defaultHostname
output frontendUrl string = 'https://${staticWebApp.properties.defaultHostname}'
output webAppName string = webApp.name
output staticWebAppName string = staticWebApp.name
output sqlServerName string = sqlServer.name
