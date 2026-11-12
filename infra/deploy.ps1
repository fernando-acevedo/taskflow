<#
.SYNOPSIS
  Creates the resource group and deploys infra/main.bicep (App Service, Static Web App, Azure SQL).
.DESCRIPTION
  Requires Azure CLI and `az login`. Required values come from environment variables
  (TASKFLOW_SQL_ADMIN_PASSWORD, TASKFLOW_DEMO_ADMIN_PASSWORD, TASKFLOW_DEMO_MEMBER_PASSWORD,
  TASKFLOW_SEED_ADMIN_EMAIL, TASKFLOW_SEED_ADMIN_PASSWORD) and are prompted for when missing.
  Optional: TASKFLOW_SEED_ADMIN_DISPLAY_NAME, TASKFLOW_SMTP_HOST, TASKFLOW_SMTP_USERNAME,
  TASKFLOW_SMTP_PASSWORD, TASKFLOW_EMAIL_FROM (without an SMTP host no email is sent).
  App passwords need 8+ characters with an uppercase letter, a lowercase letter and a digit.
  Nothing is written to disk.
#>
param(
  [string]$ResourceGroup = 'rg-taskflow-demo',
  [string]$Location = 'eastus',
  [string]$SubscriptionId
)

$ErrorActionPreference = 'Stop'

function Read-SecretIfMissing([string]$Name, [string]$Prompt) {
  if (-not [Environment]::GetEnvironmentVariable($Name)) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $plain = [System.Net.NetworkCredential]::new('', $secure).Password
    [Environment]::SetEnvironmentVariable($Name, $plain, 'Process')
  }
}

Read-SecretIfMissing 'TASKFLOW_SQL_ADMIN_PASSWORD' 'SQL admin password (min 8 chars, upper/lower/digit/symbol)'
Read-SecretIfMissing 'TASKFLOW_DEMO_ADMIN_PASSWORD' 'Demo admin password (8+ chars, upper/lower/digit)'
Read-SecretIfMissing 'TASKFLOW_DEMO_MEMBER_PASSWORD' 'Demo member password (8+ chars, upper/lower/digit)'
if (-not $env:TASKFLOW_SEED_ADMIN_EMAIL) { $env:TASKFLOW_SEED_ADMIN_EMAIL = Read-Host -Prompt 'Email of the real admin account' }
Read-SecretIfMissing 'TASKFLOW_SEED_ADMIN_PASSWORD' 'Real admin password (8+ chars, upper/lower/digit)'
if (-not $env:TASKFLOW_CHATBOT_SUPPORT_CONTACT) { $env:TASKFLOW_CHATBOT_SUPPORT_CONTACT = Read-Host -Prompt 'Chatbot support contact (email or URL shown to users)' }
if (-not $env:TASKFLOW_CHATBOT_HELP_URL) { $env:TASKFLOW_CHATBOT_HELP_URL = Read-Host -Prompt 'Chatbot help page URL' }

function Assert-AppPassword([string]$Name) {
  $v = [Environment]::GetEnvironmentVariable($Name)
  if ($v.Length -lt 8 -or $v -cnotmatch '[A-Z]' -or $v -cnotmatch '[a-z]' -or $v -notmatch '[0-9]') {
    throw "$Name must have 8+ characters with an uppercase letter, a lowercase letter and a digit."
  }
}
Assert-AppPassword 'TASKFLOW_DEMO_ADMIN_PASSWORD'
Assert-AppPassword 'TASKFLOW_DEMO_MEMBER_PASSWORD'
Assert-AppPassword 'TASKFLOW_SEED_ADMIN_PASSWORD'
$sql = $env:TASKFLOW_SQL_ADMIN_PASSWORD
$classes = @(($sql -cmatch '[A-Z]'), ($sql -cmatch '[a-z]'), ($sql -match '[0-9]'), ($sql -match '[^A-Za-z0-9]')) | Where-Object { $_ }
if ($sql.Length -lt 8 -or $classes.Count -lt 3) {
  throw 'TASKFLOW_SQL_ADMIN_PASSWORD must have 8+ characters and 3 of: uppercase, lowercase, digit, symbol.'
}
if ($sql.Contains(';') -or $sql.Contains("'")) {
  throw "TASKFLOW_SQL_ADMIN_PASSWORD must not contain ';' or a single quote (it goes into the connection string)."
}

if ($SubscriptionId) { az account set --subscription $SubscriptionId | Out-Null }

$template = Join-Path $PSScriptRoot 'main.bicep'
$params = Join-Path $PSScriptRoot 'main.bicepparam'

Write-Host "Creating resource group $ResourceGroup in $Location..."
az group create --name $ResourceGroup --location $Location --output none
if ($LASTEXITCODE -ne 0) { throw 'az group create failed' }

Write-Host 'Deploying main.bicep...'
az deployment group create `
  --resource-group $ResourceGroup `
  --name taskflow-main `
  --template-file $template `
  --parameters $params `
  --output none
if ($LASTEXITCODE -ne 0) { throw 'az deployment group create failed' }

$out = az deployment group show --resource-group $ResourceGroup --name taskflow-main --query properties.outputs --output json | ConvertFrom-Json
$apiUrl = $out.apiUrl.value
$webAppName = $out.webAppName.value
$swaName = $out.staticWebAppName.value

Write-Host ''
Write-Host "API URL:          $apiUrl"
Write-Host "Frontend URL:     $($out.frontendUrl.value)"
Write-Host "Web app name:     $webAppName"
Write-Host "Static Web App:   $swaName"
Write-Host ''
Write-Host 'Next steps (details in infra/README.md):'
Write-Host '  1. Create the Entra app registration with a federated credential for GitHub (environment production) and give it'
Write-Host "     the custom deployer role (infra/roles/webapp-deployer.json) on web app $webAppName."
Write-Host '  2. Get the Static Web Apps deployment token:'
Write-Host "       az staticwebapp secrets list --name $swaName --resource-group $ResourceGroup --query properties.apiKey -o tsv"
Write-Host '  3. GitHub repository secrets: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID, AZURE_STATIC_WEB_APPS_API_TOKEN'
Write-Host "  4. GitHub repository variables: AZURE_WEBAPP_NAME=$webAppName  API_URL=$apiUrl"
Write-Host '  5. Run the "Deploy API" workflow, then "Deploy frontend".'
