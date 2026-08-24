<#
.SYNOPSIS
    Walks slice 5.2's Broker Option 1 end to end against the live demo rig.

.DESCRIPTION
    The DoD's manual Chrome pass, driven over HTTP instead — the Claude browser extension is not
    connected. Run `.\scripts\demo-reset.ps1` first.

    It proves the things a screen pass would: the routed recipient, the attachments *by name* in the
    fake email, the state-then-send ordering when the sender is down, Resend, the kill-switch
    changing `/api/config/media` live without a restart, and the `Never` timing all the way through
    (no doc type, no outbox row).
#>

param(
    [string]$ApiBase = 'http://localhost:5180',
    [string]$Phone = '+999000003004'
)

$ErrorActionPreference = 'Stop'
$Placeholders = Join-Path $PSScriptRoot '..\src\Api\appsettings.Placeholders.json'
$MediaDir = Join-Path $PSScriptRoot '..\demo-artifacts\media'

function Write-Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Note([string]$m) { Write-Host "    $m" -ForegroundColor DarkGray }

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body, [string]$Token, [int[]]$Accept = @(200, 201))

    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $arguments = @{
        Method = $Method; Uri = "$ApiBase$Path"; Headers = $headers
        SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'
    }
    if ($null -ne $Body) {
        $arguments.Body = ($Body | ConvertTo-Json -Depth 6)
        $arguments.ContentType = 'application/json'
    }

    $response = Invoke-RestMethod @arguments
    if ($Accept -notcontains $status) {
        throw "$Method $Path -> $status $($response | ConvertTo-Json -Compress -Depth 6)"
    }
    return [pscustomobject]@{ Status = $status; Body = $response }
}

$ApiLog = Join-Path $PSScriptRoot '../demo-artifacts/api.log'

# Read out of the API log, exactly as demo-reset.ps1 does — the fake sender writes there and there is
# no dev endpoint for it.
function Get-Sms([string]$Number) {
    $escaped = [regex]::Escape($Number)
    $stream = [IO.File]::Open($ApiLog, 'Open', 'Read', 'ReadWrite')
    try { $text = ([IO.StreamReader]::new($stream)).ReadToEnd() } finally { $stream.Dispose() }
    $matched = [regex]::Matches($text, "FAKE SMS to ${escaped}: (.+)")
    return @($matched | ForEach-Object { $_.Groups[1].Value.Trim() })
}

function Wait-NewSms([string]$Number, [int]$Baseline) {
    for ($i = 0; $i -lt 120; $i++) {
        $all = @(Get-Sms $Number)
        if ($all.Count -gt $Baseline) { return $all[-1] }
        Start-Sleep -Milliseconds 250
    }
    throw "No new SMS for $Number."
}

# --------------------------------------------------------------------------- the broker

Write-Step 'Creating and activating a broker'
$adminOtpBase = @(Get-Sms '+999000000001').Count
Invoke-Api POST '/auth/otp/request' @{ phone = '+999000000001' } | Out-Null
$adminSms = Wait-NewSms '+999000000001' $adminOtpBase
if ($adminSms -notmatch '(\d{6})') { throw 'No admin OTP.' }
$adminToken = (Invoke-Api POST '/auth/otp/verify' @{ phone = '+999000000001'; code = $Matches[1] }).Body.accessToken

$baseline = @(Get-Sms $Phone).Count
Invoke-Api POST '/api/admin/brokers/' @{
    phone = $Phone; displayName = 'DEMO Broker One'
    irisCode = 'PLACEHOLDER-IRIS-01'; email = 'demo-broker@example.invalid'
} -Token $adminToken | Out-Null

$inviteSms = Wait-NewSms $Phone $baseline
if ($inviteSms -notmatch 'invite token is ([A-Za-z0-9_-]+)') { throw "No invite token in '$inviteSms'." }
$inviteToken = $Matches[1]

$baseline = @(Get-Sms $Phone).Count
Invoke-Api POST '/auth/invite/accept' @{ token = $inviteToken } | Out-Null
$codeSms = Wait-NewSms $Phone $baseline
if ($codeSms -notmatch '(\d{6})') { throw "No OTP in '$codeSms'." }
$broker = (Invoke-Api POST '/auth/invite/verify' @{ token = $inviteToken; code = $Matches[1] }).Body.accessToken
Write-Note "broker $Phone active"

# --------------------------------------------------------------------------- B2

Write-Step 'The insurance types the browser would be offered'
$types = (Invoke-Api GET '/api/broker/config' -Token $broker).Body.insuranceTypes
Write-Note ($types -join ' | ')

Write-Step 'B2: the six fields'
$request = (Invoke-Api POST '/api/broker/requests' @{
    insuredName = 'DEMO Insured Three'; insuranceType = $types[0]
    insuredAddress = 'PLACEHOLDER Address 1'; carValue = 25000
    estimatedPremium = 1200; effectiveDate = '2026-09-01'
} -Token $broker).Body
Write-Note "request $($request.id) state=$($request.state)"

$bad = Invoke-Api POST '/api/broker/requests' @{
    insuredName = 'DEMO'; insuranceType = 'NOT-A-TYPE'; insuredAddress = 'x'
    carValue = 1; estimatedPremium = 1; effectiveDate = '2026-09-01'
} -Token $broker -Accept @(400)
Write-Note "an unknown type: $($bad.Status) $($bad.Body.error)"

Write-Step 'B2: one captured document and one uploaded'
function Send-Document([string]$RequestId, [string]$Origin, [string]$File, [string]$ContentType) {
    $path = Join-Path $MediaDir $File
    $content = [System.Net.Http.MultipartFormDataContent]::new()
    $content.Add([System.Net.Http.StringContent]::new('broker_document'), 'bucket')
    $content.Add([System.Net.Http.StringContent]::new($Origin), 'origin')
    $bytes = [System.Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($path))
    $bytes.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
    $content.Add($bytes, 'file', $File)

    $client = [System.Net.Http.HttpClient]::new()
    $client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $broker)
    $response = $client.PostAsync("$ApiBase/api/broker/requests/$RequestId/documents", $content).Result
    $body = $response.Content.ReadAsStringAsync().Result
    $client.Dispose()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
}

$captured = Send-Document $request.id 'captured' 'sharp-car-photo.png' 'image/png'
$uploaded = Send-Document $request.id 'uploaded' 'garage-invoice.pdf' 'application/pdf'
Write-Note "captured: $($captured.Status)   uploaded: $($uploaded.Status)"

$documents = (Invoke-Api GET "/api/broker/requests/$($request.id)/documents" -Token $broker).Body
foreach ($d in $documents) {
    Write-Note ("{0,-28} origin={1,-9} pushStatus={2,-4} docType={3}" -f
        $d.fileName, $d.origin, $d.pushStatus, ($(if ($null -eq $d.docType) { '(null)' } else { $d.docType })))
}

# --------------------------------------------------------------------------- the failed send

Write-Step 'Submitting with the sender down (§5.3 ordering)'
$json = Get-Content $Placeholders -Raw
Set-Content $Placeholders ($json -replace '"FailureRate": 0\.0', '"FailureRate": 1.0') -NoNewline
Start-Sleep -Seconds 2

$down = (Invoke-Api POST "/api/broker/requests/$($request.id)/submit" -Token $broker).Body
Write-Note "submit: state=$($down.state) emailFailed=$($down.emailFailed)"

Set-Content $Placeholders $json -NoNewline
Start-Sleep -Seconds 2

$listed = (Invoke-Api GET '/api/broker/requests' -Token $broker).Body |
    Where-Object { $_.id -eq $request.id }
Write-Note "B1 shows: state=$($listed.state) emailedAt=$(if ($null -eq $listed.emailedAt) { '(null)' } else { $listed.emailedAt })"

$late = Send-Document $request.id 'uploaded' 'garage-invoice.pdf' 'application/pdf'
Write-Note "a document after the submit: $($late.Status) $($late.Body)"

Write-Step 'Resend'
$resent = (Invoke-Api POST "/api/broker/requests/$($request.id)/resend" -Token $broker).Body
Write-Note "resend: emailFailed=$($resent.emailFailed) recipient=$($resent.recipient)"

$again = Invoke-Api POST "/api/broker/requests/$($request.id)/resend" -Token $broker -Accept @(409)
Write-Note "a second resend: $($again.Status) $($again.Body.error)"

# --------------------------------------------------------------------------- the kill-switch

Write-Step 'The kill-switch, live'
function Get-BrokerBucket {
    ((Invoke-Api GET '/api/config/media').Body.buckets | Where-Object { $_.bucket -eq 'broker_document' })
}
Write-Note "allowUpload before: $((Get-BrokerBucket).allowUpload)"

Set-Content $Placeholders ($json -replace '"AllowUpload": true', '"AllowUpload": false') -NoNewline
Start-Sleep -Seconds 2
Write-Note "allowUpload with the switch off: $((Get-BrokerBucket).allowUpload)"

$second = (Invoke-Api POST '/api/broker/requests' @{
    insuredName = 'DEMO Insured Four'; insuranceType = $types[1]
    insuredAddress = 'PLACEHOLDER Address 2'; carValue = 18000
    estimatedPremium = 900; effectiveDate = '2026-10-01'
} -Token $broker).Body

$refused = Send-Document $second.id 'uploaded' 'garage-invoice.pdf' 'application/pdf'
Write-Note "a picked file with the switch off: $($refused.Status) $($refused.Body)"
$stillOk = Send-Document $second.id 'captured' 'sharp-car-photo.png' 'image/png'
Write-Note "a captured one: $($stillOk.Status)"

Set-Content $Placeholders $json -NoNewline
Start-Sleep -Seconds 2
Write-Note "allowUpload restored: $((Get-BrokerBucket).allowUpload)"

# --------------------------------------------------------------------------- B3

Write-Step 'B3: a customer link'
$link = (Invoke-Api POST '/api/broker/link-requests' @{
    customerMobile = '+999000007001'; insuranceType = $types[0]
} -Token $broker).Body
Write-Note "url=$($link.url) expires=$($link.expiresAt)"

$rows = (Invoke-Api GET '/api/broker/requests' -Token $broker).Body
Write-Step 'B1'
foreach ($r in $rows) {
    Write-Note ("{0,-22} opt={1} {2,-22} {3}" -f
        ($(if ($r.insuredName) { $r.insuredName } else { '—' })), $r.option, $r.state,
        ($(if ($r.emailRecipient) { $r.emailRecipient } else { '' })))
}

Write-Step 'What the fake email carried (from the API log)'
$log = Join-Path $PSScriptRoot '..\demo-artifacts\api.log'
Get-Content $log | Select-String 'FAKE EMAIL' | Select-Object -Last 6 |
    ForEach-Object { Write-Note ($_.Line.Trim() -replace '\s+', ' ') }
