#Requires -Version 7
<#
.SYNOPSIS
    Provisions the AXA Motor Claims Azure environment (design.md §10, slice 7.6).

.DESCRIPTION
    Idempotent: every resource is created with `az ... create`, which Azure itself treats as an
    upsert for everything provisioned here (resource group, ACR, SQL server/database, storage
    account + container, Container Apps environment/app) — re-running this script against an
    already-provisioned environment is a no-op plus a handful of "already exists" confirmations,
    not a failure. Every AXA-specific name is a parameter with a sensible generated default, so
    #37's eventual answer (which subscription/resource group AXA wants this in) is a parameter
    change here, never a script rewrite — grep this file for a literal you did not pass on the
    command line as your own review step before trusting that claim.

    Requires `az login` first, with a subscription selected (`az account set --subscription ...`).
    This script does not attempt to log in on your behalf — Azure credentials are yours to hold,
    never something a script should coax out of you non-interactively.

    **`-EnvName production` is written but deliberately refuses to run without
    `-IUnderstandThisIsProduction`** (design.md §11: production provisioning is week 8's, not this
    slice's). The `test` path is the one this slice actually ships: scale-to-zero Container Apps,
    the cheapest SQL serverless tier, fakes everywhere except Blob/Push (design.md §10's table).

.PARAMETER EnvName
    'test' (default, and the only one this slice runs) or 'production' (written, gated, unrun).

.PARAMETER ResourceGroupName
    Defaults to 'rg-axamotorclaims-{EnvName}'.

.PARAMETER Location
    An Azure region short name. Defaults to 'uaenorth' — closest region to AXA Middle East's
    likely user base; #22 (data residency) may override this, which is exactly why it is a
    parameter and not a literal in the resource calls below.

.PARAMETER AcrName
    Container registry name (globally unique, alphanumeric only). Defaults to
    'acraxamotorclaims{EnvName}'.

.PARAMETER SqlServerName
    Logical SQL server name (globally unique). Defaults to 'sql-axamotorclaims-{EnvName}'.

.PARAMETER SqlDatabaseName
    Defaults to 'axamotorclaims'.

.PARAMETER SqlAdminUser
    Defaults to 'axaadmin'. The password is never a parameter with a default — see
    -SqlAdminPassword.

.PARAMETER SqlAdminPassword
    Required, a SecureString, prompted for if omitted — this is a real credential and must never
    have a default value or appear in shell history.

.PARAMETER StorageAccountName
    Defaults to 'staxamotorclaims{EnvName}' (must be globally unique, lowercase alphanumeric,
    3-24 chars — Azure's own constraint, not this script's).

.PARAMETER ContainerAppsEnvName
    Defaults to 'cae-axamotorclaims-{EnvName}'.

.PARAMETER ContainerAppName
    Defaults to 'ca-axamotorclaims-{EnvName}'.

.PARAMETER JwtSigningKey
    A real >=32-byte signing key for Auth:Jwt:SigningKey (design.md Appendix A). Required, a
    SecureString, prompted for if omitted — never defaulted, same reasoning as -SqlAdminPassword.

.PARAMETER VapidSubject
    A real routable `mailto:` address (design.md #49 — Apple silently 403s a reserved-domain
    contact). Required; there is no honest default for this one.

.PARAMETER VapidPublicKey
.PARAMETER VapidPrivateKey
    From `npx --yes web-push generate-vapid-keys` (CLAUDE.md). Both required, -VapidPrivateKey a
    SecureString.

.PARAMETER AppBaseUrl
    The public HTTPS URL of this environment, used for invite links (Auth:AppBaseUrl) and web push.
    Required — there is no safe placeholder default the way #24's other config knobs have one,
    because an invite SMS carrying a wrong link reaches a real phone.

.PARAMETER IUnderstandThisIsProduction
    Required alongside `-EnvName production` — the script refuses to provision anything under that
    name without it. Not a safety theater switch: naming it out loud is the point.

.EXAMPLE
    ./scripts/provision-azure.ps1 -SqlAdminPassword (Read-Host -AsSecureString) `
        -JwtSigningKey (Read-Host -AsSecureString) `
        -VapidSubject 'mailto:ops@axa-example.invalid' `
        -VapidPublicKey '...' -VapidPrivateKey (Read-Host -AsSecureString) `
        -AppBaseUrl 'https://ca-axamotorclaims-test.example.azurecontainerapps.io'
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('test', 'production')]
    [string]$EnvName = 'test',

    [string]$ResourceGroupName,
    [string]$Location = 'uaenorth',
    [string]$AcrName,
    [string]$SqlServerName,
    [string]$SqlDatabaseName = 'axamotorclaims',
    [string]$SqlAdminUser = 'axaadmin',
    [Parameter(Mandatory)][SecureString]$SqlAdminPassword,
    [string]$StorageAccountName,
    [string]$ContainerAppsEnvName,
    [string]$ContainerAppName,
    [Parameter(Mandatory)][SecureString]$JwtSigningKey,
    [Parameter(Mandatory)][string]$VapidSubject,
    [Parameter(Mandatory)][string]$VapidPublicKey,
    [Parameter(Mandatory)][SecureString]$VapidPrivateKey,
    [Parameter(Mandatory)][string]$AppBaseUrl,
    [switch]$IUnderstandThisIsProduction
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string]$Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

# ------------------------------------------------------------------ preconditions

if ($EnvName -eq 'production' -and -not $IUnderstandThisIsProduction) {
    throw "Refusing to provision '-EnvName production' without -IUnderstandThisIsProduction. " +
        "Production provisioning is week 8's (design.md §11) — this switch exists so that stays " +
        "a deliberate decision, not an accidental flag combination."
}

$azAccount = az account show 2>$null | ConvertFrom-Json
if (-not $azAccount) {
    throw "Not logged in to Azure CLI. Run 'az login' (and 'az account set --subscription ...' " +
        "if you have more than one) before running this script — it deliberately does not do " +
        "that on your behalf."
}
Write-Note "Subscription: $($azAccount.name) ($($azAccount.id))"

if (-not $ResourceGroupName) { $ResourceGroupName = "rg-axamotorclaims-$EnvName" }
if (-not $AcrName) { $AcrName = "acraxamotorclaims$EnvName" }
if (-not $SqlServerName) { $SqlServerName = "sql-axamotorclaims-$EnvName" }
if (-not $StorageAccountName) { $StorageAccountName = "staxamotorclaims$EnvName" }
if (-not $ContainerAppsEnvName) { $ContainerAppsEnvName = "cae-axamotorclaims-$EnvName" }
if (-not $ContainerAppName) { $ContainerAppName = "ca-axamotorclaims-$EnvName" }

function ConvertFrom-SecureStringPlain([SecureString]$Value) {
    [System.Net.NetworkCredential]::new('', $Value).Password
}

$sqlAdminPasswordPlain = ConvertFrom-SecureStringPlain $SqlAdminPassword
$jwtSigningKeyPlain = ConvertFrom-SecureStringPlain $JwtSigningKey
$vapidPrivateKeyPlain = ConvertFrom-SecureStringPlain $VapidPrivateKey

# ------------------------------------------------------------------ resource group

Write-Step "Resource group: $ResourceGroupName ($Location)"
az group create --name $ResourceGroupName --location $Location --only-show-errors | Out-Null

# ------------------------------------------------------------------ container registry (Basic SKU — cheapest, adequate for one image)

Write-Step "Container registry: $AcrName"
az acr create --resource-group $ResourceGroupName --name $AcrName --sku Basic `
    --admin-enabled true --only-show-errors | Out-Null

# ------------------------------------------------------------------ Azure SQL — serverless, auto-pause
#
# Auto-pause trades a cold start (the runbook has the readiness-probe interaction this causes) for
# roughly $0 while nobody is using the test environment overnight — the whole reason "test" is
# cheap enough to run alongside "production" per design.md §10.

Write-Step "SQL server: $SqlServerName"
az sql server create --resource-group $ResourceGroupName --name $SqlServerName `
    --admin-user $SqlAdminUser --admin-password $sqlAdminPasswordPlain --only-show-errors | Out-Null

Write-Note "Allowing Azure services through the SQL firewall (needed for Container Apps to reach it)"
az sql server firewall-rule create --resource-group $ResourceGroupName --server $SqlServerName `
    --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 `
    --only-show-errors | Out-Null

$sqlSkuArgs = if ($EnvName -eq 'production') {
    @('--edition', 'GeneralPurpose', '--family', 'Gen5', '--capacity', '2')
} else {
    @('--edition', 'GeneralPurpose', '--family', 'Gen5', '--capacity', '1', '--compute-model', 'Serverless',
      '--auto-pause-delay', '60')
}
Write-Step "SQL database: $SqlDatabaseName ($EnvName tier)"
az sql db create --resource-group $ResourceGroupName --server $SqlServerName --name $SqlDatabaseName `
    @sqlSkuArgs --only-show-errors | Out-Null

$sqlConnectionString = "Server=tcp:$SqlServerName.database.windows.net,1433;Database=$SqlDatabaseName;" +
    "User ID=$SqlAdminUser;Password=$sqlAdminPasswordPlain;Encrypt=true;TrustServerCertificate=false;" +
    "Connection Timeout=30;"

# ------------------------------------------------------------------ storage — the transit buffer (§7.3)

Write-Step "Storage account: $StorageAccountName"
az storage account create --resource-group $ResourceGroupName --name $StorageAccountName `
    --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false `
    --only-show-errors | Out-Null

$storageConnectionString = az storage account show-connection-string `
    --resource-group $ResourceGroupName --name $StorageAccountName --query connectionString -o tsv

Write-Note "Container 'media-transit' (design.md §4's BlobOptions.ContainerName default)"
az storage container create --name media-transit --connection-string $storageConnectionString `
    --public-access off --only-show-errors | Out-Null

# ------------------------------------------------------------------ Container Apps environment + app

Write-Step "Container Apps environment: $ContainerAppsEnvName"
az containerapp env create --resource-group $ResourceGroupName --name $ContainerAppsEnvName `
    --location $Location --only-show-errors | Out-Null

$acrLoginServer = az acr show --name $AcrName --query loginServer -o tsv
$acrCredentials = az acr credential show --name $AcrName | ConvertFrom-Json

# A placeholder image tag on first create — the CI `package` job pushes the real one on every
# merge to main and `deploy-test` updates the app to point at it (design.md §10's CI/CD order).
$bootstrapImage = "mcr.microsoft.com/dotnet/aspnet:10.0"

$scaleArgs = if ($EnvName -eq 'production') {
    @('--min-replicas', '1', '--max-replicas', '3')
} else {
    @('--min-replicas', '0', '--max-replicas', '2')
}

Write-Step "Container App: $ContainerAppName ($EnvName scaling)"
$appExists = az containerapp show --resource-group $ResourceGroupName --name $ContainerAppName `
    --only-show-errors 2>$null
if ($appExists) {
    Write-Note "Already exists — updating scale/registry settings only, secrets/env vars are set below regardless."
    az containerapp update --resource-group $ResourceGroupName --name $ContainerAppName `
        @scaleArgs --only-show-errors | Out-Null
} else {
    az containerapp create --resource-group $ResourceGroupName --name $ContainerAppName `
        --environment $ContainerAppsEnvName --image $bootstrapImage `
        --registry-server $acrLoginServer --registry-username $acrCredentials.username `
        --registry-password $acrCredentials.passwords[0].value `
        --target-port 8080 --ingress external `
        @scaleArgs --only-show-errors | Out-Null
}

# ------------------------------------------------------------------ secrets + env vars
#
# Pure env vars, no appsettings.Test.json — CLAUDE.md and this slice's card are explicit that a
# Test json would resurrect slice 3.4's override-ordering bug (placeholders silently beating real
# values). ASPNETCORE_ENVIRONMENT stays Production in the container; only user-secrets loading
# branches on environment, and there are no user secrets in a container.

Write-Step "Setting Container Apps secrets"
az containerapp secret set --resource-group $ResourceGroupName --name $ContainerAppName --secrets `
    "connectionstrings-default=$sqlConnectionString" `
    "auth-jwt-signingkey=$jwtSigningKeyPlain" `
    "blob-connectionstring=$storageConnectionString" `
    "push-vapid-publickey=$VapidPublicKey" `
    "push-vapid-privatekey=$vapidPrivateKeyPlain" `
    --only-show-errors | Out-Null

Write-Step "Setting environment variables (plain config + secret references)"
az containerapp update --resource-group $ResourceGroupName --name $ContainerAppName --set-env-vars `
    "ASPNETCORE_ENVIRONMENT=Production" `
    "ASPNETCORE_URLS=http://+:8080" `
    "ConnectionStrings__Default=secretref:connectionstrings-default" `
    "Auth__Jwt__SigningKey=secretref:auth-jwt-signingkey" `
    "Auth__AppBaseUrl=$AppBaseUrl" `
    "Blob__Mode=azure" `
    "Blob__ConnectionString=secretref:blob-connectionstring" `
    "Push__Mode=webpush" `
    "Push__Vapid__Subject=$VapidSubject" `
    "Push__Vapid__PublicKey=secretref:push-vapid-publickey" `
    "Push__Vapid__PrivateKey=secretref:push-vapid-privatekey" `
    "Next3__Mode=fake" `
    "Next3__AssignmentSource=fake" `
    --only-show-errors | Out-Null

Write-Step "Done."
Write-Note "Resource group:      $ResourceGroupName"
Write-Note "ACR:                 $acrLoginServer"
Write-Note "SQL server:          $SqlServerName.database.windows.net / $SqlDatabaseName"
Write-Note "Storage account:     $StorageAccountName (container: media-transit)"
Write-Note "Container App:       $ContainerAppName"
Write-Note ""
Write-Note "Next: push .github/workflows/ci.yml (reviewed, not yet pushed by design — see HANDOFF.md)"
Write-Note "and let its 'package'/'deploy-test' jobs put a real image behind this app. Until then it"
Write-Note "is running the bootstrap aspnet base image and will not answer /health/ready meaningfully."
