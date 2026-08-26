#Requires -Version 7
<#
.SYNOPSIS
    Puts the machine into the week-4 demo's starting state (slice 4.3). Idempotent; under two minutes.

.DESCRIPTION
    Drops and recreates the AxaMotorClaims LocalDB, migrates it, starts Azurite and the API with
    `Blob__Mode=azure` and `Push__Mode=webpush`, seeds the four demo users and two assignments
    **through the admin API**, generates the demo media, and starts the web dev server.

    Nothing here writes to the database directly. Every row it creates goes through the same
    endpoints a person would use, which is the point: if the seed works, the demo's onboarding path
    works, and a seed that broke would be a bug worth knowing about before the client is in the room.

    The one thing it cannot do for you is grant Chrome permission to show notifications. That is
    browser UI; see docs/demo-week6.md's pre-flight.

.PARAMETER Stop
    Tear down everything this script starts (API, web, Azurite) and exit.

.PARAMETER SkipAzurite
    Leave blob storage alone. Only useful if you are running Azurite yourself.

.PARAMETER KeepAssets
    Do not regenerate demo-artifacts/media. Use it if you have swapped in your own photographs.
#>
[CmdletBinding()]
param(
    [switch]$Stop,
    [switch]$SkipAzurite,
    [switch]$KeepAssets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $RepoRoot 'demo-artifacts'
$ApiLog = Join-Path $Artifacts 'api.log'
$ApiErrLog = Join-Path $Artifacts 'api.err.log'
$WebLog = Join-Path $Artifacts 'web.log'
$WebErrLog = Join-Path $Artifacts 'web.err.log'
$AzuriteLog = Join-Path $Artifacts 'azurite.log'
$AzuriteErrLog = Join-Path $Artifacts 'azurite.err.log'
$AzuriteData = Join-Path $Artifacts 'azurite'
$MediaDir = Join-Path $Artifacts 'media'
$PidFile = Join-Path $Artifacts 'demo.pids'
$AdminTokenFile = Join-Path $Artifacts 'admin-tokens.json'
$Placeholders = Join-Path $RepoRoot 'src\Api\appsettings.Placeholders.json'

$ApiPort = 5180
$WebPort = 5173
$AzuritePort = 10000
$ApiBase = "http://localhost:$ApiPort"
$WebBase = "http://localhost:$WebPort"

# The fake NEXT3's own fixtures (src/Api/Integrations/Next3/FakeNext3Client.cs). Not client data and
# not placeholder config: they are the simulator's seed rows, and the demo has to name them.
$ExpertNext3Id = 'PLACEHOLDER-EXP-01'
$SeededAssignments = @(
    @{ VisaNo = 'PLACEHOLDER-VISA-0001'; Ref = 'DEMO-REF-0001' }
    @{ VisaNo = 'PLACEHOLDER-VISA-0002'; Ref = 'DEMO-REF-0002' }
)

# The demo cast. +999 is an unassigned country code, so no real handset can ever be dialled by
# accident - the same reason Appendix A's seeded admin uses it.
$DemoUsers = @(
    @{
        Kind = 'expert'; Slug = 'experts'; Phone = '+999000003001'
        Body = @{
            phone = '+999000003001'; displayName = 'DEMO Expert'
            email = 'demo-expert@example.invalid'; next3Id = $ExpertNext3Id
        }
    }
    @{
        Kind = 'garage'; Slug = 'garages'; Phone = '+999000003002'
        Body = @{
            phone = '+999000003002'; displayName = 'DEMO Garage'; contactName = 'DEMO Contact'
            contactPhone = '+999000003002'; mobile = '+999000003002'
            email = 'demo-garage@example.invalid'; next3Id = 'PLACEHOLDER-GAR-01'
            address = 'PLACEHOLDER address'; openingHours = 'PLACEHOLDER hours'
        }
    }
    @{
        Kind = 'claim officer'; Slug = 'claim-officers'; Phone = '+999000003003'
        Body = @{
            phone = '+999000003003'; displayName = 'DEMO Claim Officer'
            next3User = 'PLACEHOLDER-OFF-01'; email = 'demo-officer@example.invalid'
        }
    }
    # Added 2026-08-26. Weeks 5 and 6 built the whole broker module - Option 1, the Option 2 public
    # page, the five car shots and B4's review - and this rig still seeded only the three roles week 4
    # needed, so any broker beat dead-ended at sign-in. `displayName` is what the public page shows a
    # member of the public as the broker who sent them the link (design.md 9.1), so it has to read
    # like a name rather than a slug.
    @{
        Kind = 'broker'; Slug = 'brokers'; Phone = '+999000003004'
        Body = @{
            phone = '+999000003004'; displayName = 'DEMO Broker'
            irisCode = 'PLACEHOLDER-IRIS-01'; email = 'demo-broker@example.invalid'
        }
    }
)

# ---------------------------------------------------------------------------- helpers

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string]$Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

function Get-PortOwner([int]$Port) {
    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($connection) { return [int]$connection.OwningProcess }
    return 0
}

function Stop-Tree([int]$ProcessId) {
    if ($ProcessId -le 0) { return }
    # /T because `dotnet run` and `npm run dev` both start the real server as a child: killing the
    # wrapper alone leaves the port held, and killing the child alone leaves the wrapper holding the
    # handle on our log file.
    & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
}

function Stop-Listener([int]$Port, [string]$Name) {
    $owner = Get-PortOwner $Port
    if ($owner -eq 0) { return }
    Write-Note "stopping $Name on port $Port (pid $owner)"
    Stop-Tree $owner
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and (Get-PortOwner $Port) -ne 0) { Start-Sleep -Milliseconds 200 }
    if ((Get-PortOwner $Port) -ne 0) { throw "Port $Port is still held after 15 s; stop $Name by hand." }
}

function Stop-RecordedProcesses {
    if (-not (Test-Path $PidFile)) { return }
    foreach ($line in Get-Content $PidFile) {
        $parsed = 0
        if ([int]::TryParse($line.Trim(), [ref]$parsed)) { Stop-Tree $parsed }
    }
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

function Add-RecordedProcess([int]$ProcessId) {
    Add-Content -LiteralPath $PidFile -Value $ProcessId
}

<#
    Reads a log another process is still writing to. Get-Content -Raw opens the file without sharing
    writes and fails with "used by another process" against a live redirect, which is the whole
    reason this exists.
#>
function Read-LogText([string]$Path) {
    if (-not (Test-Path $Path)) { return '' }
    $stream = [System.IO.File]::Open(
        $Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $reader = New-Object System.IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } finally { $stream.Dispose() }
}

<# Every message the fake SMS sender has logged for one phone, oldest first (design.md 6.2). #>
function Get-SmsMessages([string]$Phone) {
    $escaped = [regex]::Escape($Phone)
    $matched = [regex]::Matches((Read-LogText $ApiLog), "FAKE SMS to ${escaped}: (.+)")
    return @($matched | ForEach-Object { $_.Groups[1].Value.Trim() })
}

function Wait-NewSms([string]$Phone, [int]$Baseline, [int]$TimeoutSeconds = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $messages = @(Get-SmsMessages $Phone)
        if ($messages.Count -gt $Baseline) { return $messages[-1] }
        Start-Sleep -Milliseconds 250
    }
    throw "No new SMS for $Phone within $TimeoutSeconds s. See $ApiLog."
}

function Read-Jsonc([string]$Path) {
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Skip
    $options.AllowTrailingCommas = $true
    return [System.Text.Json.JsonDocument]::Parse((Get-Content -Raw -LiteralPath $Path), $options)
}

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

function Wait-Http([string]$Url, [int]$TimeoutSeconds, [string]$What) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $null = Invoke-WebRequest -Uri $Url -TimeoutSec 3 -SkipHttpErrorCheck
            return
        } catch { Start-Sleep -Milliseconds 400 }
    }
    throw "$What did not answer at $Url within $TimeoutSeconds s."
}

# ---------------------------------------------------------------------------- teardown

New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null

Write-Step 'Stopping anything a previous run started'
Stop-RecordedProcesses
Stop-Listener $ApiPort 'API'
Stop-Listener $WebPort 'web dev server'
if ($Stop) {
    if (-not $SkipAzurite) { Stop-Listener $AzuritePort 'Azurite' }
    Write-Host 'Demo environment stopped.' -ForegroundColor Green
    return
}

$started = Get-Date

# ---------------------------------------------------------------------------- preconditions

Write-Step 'Checking preconditions'

$config = Read-Jsonc $Placeholders
try {
    $adminPhone = $config.RootElement.GetProperty('Auth').GetProperty('SeedAdmin').GetProperty('Phone').GetString()
    $failureRate = $config.RootElement.GetProperty('Fake').GetProperty('FailureRate').GetDouble()
    $next3Mode = $config.RootElement.GetProperty('Next3').GetProperty('Mode').GetString()
} finally { $config.Dispose() }

if ($failureRate -ne 0) {
    Write-Warning "Fake:FailureRate is $failureRate in appsettings.Placeholders.json. Beat 3 leaves it at 1.0; set it back to 0.0 before demoing."
}
if ($next3Mode -ne 'fake') { throw "Next3:Mode is '$next3Mode'. The demo runs on the simulator; set it back to 'fake'." }

# Push:Mode=webpush validates on start, so a missing VAPID pair kills the API at boot with a message
# nobody reads at 9am. Catch it here instead.
$secrets = (& dotnet user-secrets list --project (Join-Path $RepoRoot 'src\Api') 2>&1) -join "`n"
if ($secrets -notmatch 'Push:Vapid:PrivateKey\s*=\s*\S' -or $secrets -match 'Push:Vapid:PrivateKey\s*=\s*PLACEHOLDER') {
    throw "No VAPID key pair in user-secrets. Generate one and store it - the three commands are in CLAUDE.md."
}
Write-Note "admin phone $adminPhone; VAPID pair present; Next3:Mode=fake"

# ---------------------------------------------------------------------------- storage

if (-not $SkipAzurite) {
    if ((Get-PortOwner $AzuritePort) -ne 0) {
        Write-Step 'Azurite already listening on 10000 - left alone'
    } else {
        Write-Step 'Starting Azurite'
        # Wiped, not reused: blobs from a previous run have no rows behind them after the database is
        # recreated, so they would sit in the container until the orphan sweep (design.md 7.3).
        Remove-Item $AzuriteData -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $AzuriteData -Force | Out-Null
        $azurite = Start-Process -PassThru -WindowStyle Hidden -FilePath 'npx.cmd' `
            -ArgumentList '--yes', '-p', 'azurite', 'azurite-blob', '--silent', '--skipApiVersionCheck',
                          '--location', $AzuriteData `
            -RedirectStandardOutput $AzuriteLog -RedirectStandardError $AzuriteErrLog
        Add-RecordedProcess $azurite.Id
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline -and (Get-PortOwner $AzuritePort) -eq 0) { Start-Sleep -Milliseconds 300 }
        if ((Get-PortOwner $AzuritePort) -eq 0) { throw "Azurite did not start; see $AzuriteLog / $AzuriteErrLog." }
    }
}

# ---------------------------------------------------------------------------- database

Write-Step 'Recreating the database'
Push-Location $RepoRoot
try {
    & dotnet tool restore | Out-Null
    & dotnet build (Join-Path $RepoRoot 'src\Api') --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    # A drop against a database that is not there is a success for our purposes.
    & dotnet ef database drop --force --no-build --project src\Api 2>&1 | Out-Null
    & dotnet ef database update --no-build --project src\Api 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed. Run it by hand to see why.' }
} finally { Pop-Location }

# ---------------------------------------------------------------------------- api

Write-Step 'Starting the API'
Remove-Item $ApiLog, $ApiErrLog -Force -ErrorAction SilentlyContinue
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $ApiBase
$env:Blob__Mode = 'azure'
$env:Push__Mode = 'webpush'
# The invitation link in the onboarding SMS (S1). Set here for the same reason as the two above:
# --no-launch-profile means launchSettings.json is not read, so its Auth__AppBaseUrl never applies.
# Without this the demo's invitations point at https://PLACEHOLDER-app.example, which is correct for
# a placeholder and useless for anyone who taps one.
$env:Auth__AppBaseUrl = $WebBase
try {
    # `dotnet run`, not the built exe: it puts the content root at src/Api, which is what makes beat
    # 3's live edit of appsettings.Placeholders.json reload. Run the exe out of bin and you would be
    # editing a copy nothing reads.
    $api = Start-Process -PassThru -WindowStyle Hidden -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', (Join-Path $RepoRoot 'src\Api'), '--no-launch-profile', '--no-build' `
        -WorkingDirectory $RepoRoot -RedirectStandardOutput $ApiLog -RedirectStandardError $ApiErrLog
    Add-RecordedProcess $api.Id
} finally {
    Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:ASPNETCORE_URLS, Env:Blob__Mode, Env:Push__Mode,
        Env:Auth__AppBaseUrl -ErrorAction SilentlyContinue
}
Wait-Http "$ApiBase/health" 90 'API'

# ---------------------------------------------------------------------------- seed

Write-Step 'Signing in as the seeded admin'
$baseline = @(Get-SmsMessages $adminPhone).Count
Invoke-Api POST '/auth/otp/request' @{ phone = $adminPhone } | Out-Null
$adminSms = Wait-NewSms $adminPhone $baseline
if ($adminSms -notmatch '(\d{6})') { throw "Could not read an OTP out of '$adminSms'." }
$tokens = Invoke-Api POST '/auth/otp/verify' @{ phone = $adminPhone; code = $Matches[1] }
$adminToken = $tokens.accessToken
# Kept so demo-assign.ps1 can inject beat 2's assignment without signing in again - a second sign-in
# inside the OtpResendSeconds window is throttled, and the throttle would land mid-demo.
$tokens | ConvertTo-Json | Set-Content -LiteralPath $AdminTokenFile

Write-Step 'Creating and activating the demo users'
foreach ($user in $DemoUsers) {
    $phone = $user.Phone

    $baseline = @(Get-SmsMessages $phone).Count
    Invoke-Api POST "/api/admin/$($user.Slug)/" $user.Body -Token $adminToken -Accept @(200, 201) | Out-Null
    $inviteSms = Wait-NewSms $phone $baseline
    # The same character class as Api.Tests' CapturingSmsSender, and for the same reason: the SMS now
    # carries a link after the token, so a greedy \S+ would swallow the full stop into the token.
    if ($inviteSms -notmatch 'invite token is ([A-Za-z0-9_-]+)') { throw "Could not read an invite token out of '$inviteSms'." }
    $inviteToken = $Matches[1]

    $baseline = @(Get-SmsMessages $phone).Count
    Invoke-Api POST '/auth/invite/accept' @{ token = $inviteToken } | Out-Null
    $codeSms = Wait-NewSms $phone $baseline
    if ($codeSms -notmatch '(\d{6})') { throw "Could not read an OTP out of '$codeSms'." }

    Invoke-Api POST '/auth/invite/verify' @{ token = $inviteToken; code = $Matches[1] } | Out-Null
    Write-Note "$($user.Kind) $phone active"
}

Write-Step 'Injecting two assignments from the simulator'
foreach ($assignment in $SeededAssignments) {
    $result = Invoke-Api POST '/api/admin/dev/assignments' @{
        visaNo = $assignment.VisaNo; expertNext3Id = $ExpertNext3Id; next3AssignmentRef = $assignment.Ref
    } -Token $adminToken
    Write-Note "$($assignment.VisaNo) created=$($result.created)"
}

# ---------------------------------------------------------------------------- media + web

Write-Step 'Warming up the queue viewer'
& (Join-Path $PSScriptRoot 'demo-outbox.ps1') -WarmUp

if (-not $KeepAssets) {
    Write-Step 'Generating the demo media'
    Remove-Item $MediaDir -Recurse -Force -ErrorAction SilentlyContinue
    & node (Join-Path $PSScriptRoot 'demo-media.mjs') $MediaDir | ForEach-Object { Write-Note $_ }
    if ($LASTEXITCODE -ne 0) { throw 'demo-media.mjs failed.' }
}

Write-Step 'Starting the web dev server'
Remove-Item $WebLog, $WebErrLog -Force -ErrorAction SilentlyContinue
$web = Start-Process -PassThru -WindowStyle Hidden -FilePath 'npm.cmd' -ArgumentList 'run', 'dev' `
    -WorkingDirectory (Join-Path $RepoRoot 'src\Web') -RedirectStandardOutput $WebLog -RedirectStandardError $WebErrLog
Add-RecordedProcess $web.Id
Wait-Http $WebBase 60 'web dev server'

# ---------------------------------------------------------------------------- the card

$elapsed = [int]((Get-Date) - $started).TotalSeconds
Write-Host ''
Write-Host "Demo environment ready in ${elapsed}s." -ForegroundColor Green
Write-Host ''
Write-Host "  App          $WebBase"
Write-Host "  API          $ApiBase   (log: demo-artifacts\api.log)"
Write-Host "  Blob         Azurite on $AzuritePort, container media-transit"
Write-Host "  Media        $MediaDir"
Write-Host ''
Write-Host '  Sign in with these, one Chrome profile each:'
Write-Host "    admin          $adminPhone"
foreach ($user in $DemoUsers) { Write-Host ("    {0,-14} {1}" -f $user.Kind, $user.Phone) }
Write-Host ''
Write-Host '  OTP for any of them:   .\scripts\demo-otp.ps1 <phone>'
# Activation just sent each of them a code, and Auth:OtpResendSeconds is 60. A presenter who signs in
# the second this script finishes meets a 429 and no new code; a presenter who sets up their Chrome
# profiles first never notices. Say so rather than let it be discovered.
Write-Host '  (sign-in is throttled for ~60s from now - the activation codes were just sent)' -ForegroundColor DarkYellow
Write-Host '  Assign a claim:        .\scripts\demo-assign.ps1'
Write-Host '  The queue:             .\scripts\demo-outbox.ps1'
Write-Host '  Kill NEXT3 (beat 3):   set Fake:FailureRate to 1.0 in src\Api\appsettings.Placeholders.json'
Write-Host ''
Write-Host '  Chrome will not show a notification until you allow it for' -NoNewline
Write-Host " $WebBase" -ForegroundColor Yellow -NoNewline
Write-Host ' - see the pre-flight in docs\demo-week6.md.'
