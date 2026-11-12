using './main.bicep'

// No secrets in this file: they are read from environment variables at deployment time
// (deploy.ps1 / deploy.sh set them, prompting when they are missing).
param namePrefix = 'taskflow'
param location = 'eastus'
param staticWebAppLocation = 'eastus2'
param sqlAdminLogin = 'taskflowadmin'
param sqlAdminPassword = readEnvironmentVariable('TASKFLOW_SQL_ADMIN_PASSWORD')
param demoAdminPassword = readEnvironmentVariable('TASKFLOW_DEMO_ADMIN_PASSWORD')
param demoMemberPassword = readEnvironmentVariable('TASKFLOW_DEMO_MEMBER_PASSWORD')
param demoResetCron = '0 3 * * *'
param demoTimeZone = 'UTC'
param seedAdminEmail = readEnvironmentVariable('TASKFLOW_SEED_ADMIN_EMAIL')
param seedAdminPassword = readEnvironmentVariable('TASKFLOW_SEED_ADMIN_PASSWORD')
param seedAdminDisplayName = readEnvironmentVariable('TASKFLOW_SEED_ADMIN_DISPLAY_NAME', 'Administrator')
// Required by the chatbot (ADR 0026); no defaults, so a deployment without them fails before reaching Azure.
param chatbotSupportContact = readEnvironmentVariable('TASKFLOW_CHATBOT_SUPPORT_CONTACT')
param chatbotHelpUrl = readEnvironmentVariable('TASKFLOW_CHATBOT_HELP_URL')
param forwardedHeadersKnownNetworks = [
  '10.0.0.0/8'
  '172.16.0.0/12'
  '192.168.0.0/16'
  '169.254.0.0/16'
]
// Email is optional: with an empty TASKFLOW_SMTP_HOST the API sends no mail.
param smtpHost = readEnvironmentVariable('TASKFLOW_SMTP_HOST', '')
param smtpPort = 587
param smtpSecurity = 'StartTls'
param smtpUsername = readEnvironmentVariable('TASKFLOW_SMTP_USERNAME', '')
param smtpPassword = readEnvironmentVariable('TASKFLOW_SMTP_PASSWORD', '')
param emailFromAddress = readEnvironmentVariable('TASKFLOW_EMAIL_FROM', '')
param healthCheckPath = ''
