#Requires -Version 7
<#
.SYNOPSIS
    Seeds a deployed AXA Motor Claims environment with the four demo profiles, through the real
    admin API (design.md §10, slice 7.6).

.DESCRIPTION
    Derived from scripts/demo-reset.ps1's seeding sequence, not a copy of the whole script: this
    one does no process, LocalDB, or Azurite management — it assumes the environment named by
    -ApiBase is already up (provisioned by scripts/provision-azure.ps1 and deployed by CI). Every
    row it creates goes through the same admin endpoints a person clicking through the (future)
    admin UI would use, which is the point demo-reset.ps1's own doc comment already makes: if the
    seed works, onboarding works.

    **OTPs are read by you, not scraped.** demo-reset.ps1 can tail a local api.log because it
    started that process itself; this script has no such file to read. Every OTP-consuming step
    prompts you to check the container's logs (`az containerapp logs show --name <app> --resource-
    group <rg> --follow`, or the runbook's exact command) for a line reading
    "FAKE SMS to <phone>: ..." and paste in the code. Push:Mode=fake / a fake SMS sender is assumed
    — this script has no path for a real SMS provider, because a real provider means the code goes
    to an actual phone, not a log line this script could read either way.

.PARAMETER ApiBase
    The deployed environment's base URL, e.g. https://ca-axamotorclaims-test.example.azurecontainerapps.io

.PARAMETER AdminPhone
    The seeded admin's phone number (Auth:SeedAdmin:Phone, design.md Appendix A). No default here
    on purpose — pulling it from a local appsettings.Placeholders.json would silently assume the
    deployed environment's placeholder file matches this machine's, which is exactly the kind of
    two-copies-that-can-disagree problem this project's own conventions warn about elsewhere.

.EXAMPLE
    ./scripts/uat-seed.ps1 -ApiBase https://ca-axamotorclaims-test.example.azurecontainerapps.io `
        -AdminPhone '+999000000001'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApiBase,
    [Parameter(Mandatory)][string]$AdminPhone
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string]$Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        $Body,
        [string]$Token,
        [int[]]$Accept = @(200)
    )
    $arguments = @{
        Method             = $Method
        Uri                = "$ApiBase$Path"
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'status'
        TimeoutSec         = 30
    }
    if ($Token) { $arguments.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) {
        $arguments.Body = ($Body | ConvertTo-Json -Depth 6)
        $arguments.ContentType = 'application/json'
    }

    $response = Invoke-RestMethod @arguments
    if ($Accept -notcontains $status) {
        throw "$Method $Path answered $status - $($response | ConvertTo-Json -Depth 4 -Compress)"
    }
    return $response
}

function Read-SixDigitCode([string]$Prompt) {
    while ($true) {
        $raw = Read-Host $Prompt
        if ($raw -match '(\d{6})') { return $Matches[1] }
        Write-Host "    That did not contain a 6-digit code - paste the whole SMS line if unsure." -ForegroundColor Yellow
    }
}

function Read-InviteToken([string]$Prompt) {
    while ($true) {
        $raw = Read-Host $Prompt
        if ($raw -match 'invite token is ([A-Za-z0-9_-]+)') { return $Matches[1] }
        # Also accept the bare token, in case the operator already extracted it.
        if ($raw -match '^[A-Za-z0-9_-]+$') { return $raw }
        Write-Host "    Could not find an invite token in that - paste the whole SMS line." -ForegroundColor Yellow
    }
}

# The same seed cast demo-reset.ps1 uses, so the two scripts describe the same known-good demo
# data set rather than two subtly different casts. +999 is an unassigned country code (Appendix A).
$UatUsers = @(
    @{
        Kind = 'expert'; Slug = 'experts'; Phone = '+999000004001'
        Body = @{
            phone = '+999000004001'; displayName = 'UAT Expert'
            email = 'uat-expert@example.invalid'; next3Id = 'PLACEHOLDER-EXP-01'
        }
    }
    @{
        Kind = 'garage'; Slug = 'garages'; Phone = '+999000004002'
        Body = @{
            phone = '+999000004002'; displayName = 'UAT Garage'; contactName = 'UAT Contact'
            contactPhone = '+999000004002'; mobile = '+999000004002'
            email = 'uat-garage@example.invalid'; next3Id = 'PLACEHOLDER-GAR-01'
        }
    }
    @{
        Kind = 'claim officer'; Slug = 'claim-officers'; Phone = '+999000004003'
        Body = @{
            phone = '+999000004003'; displayName = 'UAT Claim Officer'
            next3User = 'PLACEHOLDER-CO-01'; email = 'uat-officer@example.invalid'
        }
    }
    @{
        Kind = 'broker'; Slug = 'brokers'; Phone = '+999000004004'
        Body = @{
            phone = '+999000004004'; displayName = 'UAT Broker'
            irisCode = 'PLACEHOLDER-IRIS'; email = 'uat-broker@example.invalid'
        }
    }
)

Write-Step "Checking $ApiBase is up"
$null = Invoke-WebRequest -Uri "$ApiBase/health" -TimeoutSec 10
Write-Note "Reachable."

Write-Step 'Signing in as the seeded admin'
Invoke-Api POST '/auth/otp/request' @{ phone = $AdminPhone } | Out-Null
$adminCode = Read-SixDigitCode "Check the container logs for 'FAKE SMS to $AdminPhone' and paste the 6-digit code"
$tokens = Invoke-Api POST '/auth/otp/verify' @{ phone = $AdminPhone; code = $adminCode }
$adminToken = $tokens.accessToken
Write-Note 'Signed in.'

Write-Step 'Creating and activating the UAT profiles'
foreach ($user in $UatUsers) {
    $phone = $user.Phone
    Write-Note "$($user.Kind) ($phone)"

    Invoke-Api POST "/api/admin/$($user.Slug)/" $user.Body -Token $adminToken -Accept @(200, 201) | Out-Null
    $inviteToken = Read-InviteToken "  Check the logs for 'FAKE SMS to $phone' (the invite) and paste the SMS line or the token"

    Invoke-Api POST '/auth/invite/accept' @{ token = $inviteToken } | Out-Null
    $otpCode = Read-SixDigitCode "  Check the logs for the next 'FAKE SMS to $phone' (the OTP) and paste the 6-digit code"

    Invoke-Api POST '/auth/invite/verify' @{ token = $inviteToken; code = $otpCode } | Out-Null
    Write-Note "  active."
}

Write-Step 'Done.'
Write-Note 'Every profile above went through the real admin API — if this script succeeded, the'
Write-Note 'onboarding path a real admin will use against this environment also works.'
