#Requires -Version 7
<#
.SYNOPSIS
    Injects a synthetic NEXT3 assignment - beat 2 of docs\demo-week4.md (slice 4.3).

.DESCRIPTION
    Wraps POST /api/admin/dev/assignments, which is admin-gated and refuses unless the fake
    assignment source is the configured one (design.md 6.2). It reuses the admin session
    demo-reset.ps1 established, refreshing it when the access token has aged out, so the demo does
    not have to sign in again mid-beat - and so it never trips the OTP resend throttle.

.PARAMETER VisaNo
    One of the simulator's seeded claims. Defaults to PLACEHOLDER-VISA-0003, which demo-reset.ps1
    deliberately leaves un-injected so beat 2 has a live one to show.

.EXAMPLE
    .\scripts\demo-assign.ps1
    .\scripts\demo-assign.ps1 -VisaNo PLACEHOLDER-VISA-0005 -Reference DEMO-LIVE-02
#>
[CmdletBinding()]
param(
    [string]$VisaNo = 'PLACEHOLDER-VISA-0003',
    [string]$ExpertNext3Id = 'PLACEHOLDER-EXP-01',
    [string]$Reference = 'DEMO-LIVE-01'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ApiBase = 'http://localhost:5180'
$TokenFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'demo-artifacts\admin-tokens.json'

if (-not (Test-Path $TokenFile)) { throw "No admin session at $TokenFile. Run .\scripts\demo-reset.ps1 first." }
$tokens = Get-Content -Raw $TokenFile | ConvertFrom-Json

# Returns the status alongside the body: -StatusCodeVariable writes into the *calling* scope, which
# inside a function is the function's own, so a bare $status would be unset by the time it is read.
function Invoke-Assign([string]$AccessToken) {
    $body = Invoke-RestMethod -Method Post -Uri "$ApiBase/api/admin/dev/assignments" `
        -ContentType 'application/json' -Headers @{ Authorization = "Bearer $AccessToken" } `
        -SkipHttpErrorCheck -StatusCodeVariable 'status' -Body (@{
            visaNo = $VisaNo; expertNext3Id = $ExpertNext3Id; next3AssignmentRef = $Reference
        } | ConvertTo-Json)
    return [pscustomobject]@{ Status = $status; Body = $body }
}

$result = Invoke-Assign $tokens.accessToken

if ($result.Status -eq 401) {
    # Access tokens live 15 minutes (Appendix A) and a demo runs longer than that. Rotating here is
    # what keeps this one command reliable at minute 20 as well as minute 2.
    $tokens = Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/refresh" -ContentType 'application/json' `
        -Body (@{ refreshToken = $tokens.refreshToken } | ConvertTo-Json)
    $tokens | ConvertTo-Json | Set-Content -LiteralPath $TokenFile
    $result = Invoke-Assign $tokens.accessToken
}

if ($result.Status -ne 200) {
    throw "Injection answered $($result.Status) - $($result.Body | ConvertTo-Json -Depth 4 -Compress)"
}

if ($result.Body.created) {
    Write-Host "Assigned $VisaNo to $ExpertNext3Id. The expert's device should be ringing." -ForegroundColor Green
} else {
    # The handler dedupes on next3_assignment_ref (design.md 6.2), so a repeated reference is a
    # deliberate no-op rather than a second assignment - which is the replay-safety the demo relies on.
    Write-Warning "$VisaNo was already assigned under reference '$Reference' - nothing was created, and no notification will fire. Use a different -Reference."
}
