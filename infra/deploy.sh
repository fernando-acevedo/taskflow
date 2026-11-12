#!/usr/bin/env bash
# Creates the resource group and deploys infra/main.bicep. Requires Azure CLI and `az login`.
# Required: TASKFLOW_SQL_ADMIN_PASSWORD, TASKFLOW_DEMO_ADMIN_PASSWORD, TASKFLOW_DEMO_MEMBER_PASSWORD,
# TASKFLOW_SEED_ADMIN_EMAIL, TASKFLOW_SEED_ADMIN_PASSWORD (prompted for when missing; app passwords need
# 8+ chars with upper, lower and a digit). Optional: TASKFLOW_SEED_ADMIN_DISPLAY_NAME, TASKFLOW_SMTP_HOST,
# TASKFLOW_SMTP_USERNAME, TASKFLOW_SMTP_PASSWORD, TASKFLOW_EMAIL_FROM (no SMTP host means no email). Usage: ./deploy.sh [resource-group] [location]
set -euo pipefail

RESOURCE_GROUP="${1:-rg-taskflow-demo}"
LOCATION="${2:-eastus}"
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ask() { # name prompt [secret: 1 hides the input, 0 echoes it]
  if [ -z "${!1:-}" ]; then
    if [ "${3:-1}" = "1" ]; then read -r -s -p "$2: " value; else read -r -p "$2: " value; fi
    echo
    export "$1=$value"
  fi
}
ask TASKFLOW_SQL_ADMIN_PASSWORD "SQL admin password (min 8 chars, upper/lower/digit/symbol)"
ask TASKFLOW_DEMO_ADMIN_PASSWORD "Demo admin password (8+ chars, upper/lower/digit)"
ask TASKFLOW_DEMO_MEMBER_PASSWORD "Demo member password (8+ chars, upper/lower/digit)"
ask TASKFLOW_SEED_ADMIN_EMAIL "Email of the real admin account" 0
ask TASKFLOW_SEED_ADMIN_PASSWORD "Real admin password (8+ chars, upper/lower/digit)"
ask TASKFLOW_CHATBOT_SUPPORT_CONTACT "Chatbot support contact (email or URL shown to users)" 0
ask TASKFLOW_CHATBOT_HELP_URL "Chatbot help page URL" 0

check_app_password() {
  local v="${!1}"
  if [ "${#v}" -lt 8 ] || [[ ! "$v" =~ [A-Z] ]] || [[ ! "$v" =~ [a-z] ]] || [[ ! "$v" =~ [0-9] ]]; then
    echo "$1 must have 8+ characters with an uppercase letter, a lowercase letter and a digit." >&2; exit 1
  fi
}
check_app_password TASKFLOW_DEMO_ADMIN_PASSWORD
check_app_password TASKFLOW_DEMO_MEMBER_PASSWORD
check_app_password TASKFLOW_SEED_ADMIN_PASSWORD
sql="$TASKFLOW_SQL_ADMIN_PASSWORD"; classes=0
[[ "$sql" =~ [A-Z] ]] && classes=$((classes+1))
[[ "$sql" =~ [a-z] ]] && classes=$((classes+1))
[[ "$sql" =~ [0-9] ]] && classes=$((classes+1))
[[ "$sql" =~ [^A-Za-z0-9] ]] && classes=$((classes+1))
if [ "${#sql}" -lt 8 ] || [ "$classes" -lt 3 ]; then
  echo "TASKFLOW_SQL_ADMIN_PASSWORD must have 8+ characters and 3 of: uppercase, lowercase, digit, symbol." >&2; exit 1
fi
if [[ "$sql" == *";"* || "$sql" == *"'"* ]]; then
  echo "TASKFLOW_SQL_ADMIN_PASSWORD must not contain ';' or a single quote (it goes into the connection string)." >&2; exit 1
fi

echo "Creating resource group $RESOURCE_GROUP in $LOCATION..."
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "Deploying main.bicep..."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --name taskflow-main \
  --template-file "$DIR/main.bicep" \
  --parameters "$DIR/main.bicepparam" \
  --output none

out() { az deployment group show --resource-group "$RESOURCE_GROUP" --name taskflow-main --query "properties.outputs.$1.value" -o tsv; }
API_URL="$(out apiUrl)"; WEBAPP="$(out webAppName)"; SWA="$(out staticWebAppName)"

cat <<TXT

API URL:        $API_URL
Frontend URL:   $(out frontendUrl)
Web app name:   $WEBAPP
Static Web App: $SWA

Next steps (details in infra/README.md):
  1. Create the Entra app registration with a federated credential for GitHub (environment production) and give it
     the custom deployer role (infra/roles/webapp-deployer.json) on web app $WEBAPP.
  2. az staticwebapp secrets list --name $SWA --resource-group $RESOURCE_GROUP --query properties.apiKey -o tsv
  3. GitHub secrets: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID, AZURE_STATIC_WEB_APPS_API_TOKEN
  4. GitHub variables: AZURE_WEBAPP_NAME=$WEBAPP  API_URL=$API_URL
  5. Run the "Deploy API" workflow, then "Deploy frontend".
TXT
