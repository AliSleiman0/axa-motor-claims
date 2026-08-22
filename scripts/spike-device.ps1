#Requires -Version 7
<#
.SYNOPSIS
    Puts the two handsets on the running app (slice 6.3a, the early device spike).

.DESCRIPTION
    `demo-reset.ps1` already builds the whole environment - Azurite, the database, the API on 5180
    with Push__Mode=webpush, and a Vite dev server on 5173 over plain http. **This script does not
    replace it and does not edit it.** Run that first; this adds the two things the phones need.

    THE SAMSUNG reaches Vite on 5173 through `adb reverse tcp:5173 tcp:5173`, which maps the handset's
    own localhost onto this machine's port. That is deliberate and it is the whole trick: Chromium
    treats http://localhost as a potentially trustworthy origin, so the WebView gets the secure
    context that getUserMedia, geolocation and service workers all require - with no certificate and
    no CA installed on the phone. `capacitor.config.json` therefore holds no machine-specific address.

    THE IPHONE cannot do that, so it gets a second Vite on 5174 over HTTPS on the LAN, with an mkcert
    leaf whose root the phone trusts as an installed profile. Safari gives a page no camera, no
    location and no web push without a secure context, and it will not trust the ASP.NET dev cert.

    Both Vite instances proxy /api and /auth to the API on localhost:5180, so the API is never bound
    to the LAN and never needs a certificate of its own.

    Nothing here is a secret in the repo: the key, the leaf and the copy of the root CA all live under
    demo-artifacts/, which is already gitignored.

.PARAMETER Stop
    Stop the HTTPS Vite and the CA server this script starts, drop the adb reverse, and exit.

.PARAMETER ServeCa
    Also serve the root CA over http on 8081 so the iPhone can download and install it. Needed once
    per phone. It serves a directory containing the CA and nothing else - never the private key.

.PARAMETER LanIp
    Override the detected LAN address (useful when the machine has several).
#>
[CmdletBinding()]
param(
    [switch]$Stop,
    [switch]$ServeCa,
    [string]$LanIp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $RepoRoot 'demo-artifacts'
$CertDir = Join-Path $Artifacts 'certs'
$CaShareDir = Join-Path $Artifacts 'ca-share'
$WebDir = Join-Path $RepoRoot 'src\Web'
$PidFile = Join-Path $Artifacts 'spike.pids'
$WebLog = Join-Path $Artifacts 'spike-web.log'
$WebErrLog = Join-Path $Artifacts 'spike-web.err.log'
$HttpsDescriptor = Join-Path $CertDir 'spike-https.json'

$HttpPort = 5173      # started by demo-reset.ps1; the Samsung's target via adb reverse
$HttpsPort = 5174     # started here; the iPhone's target over the LAN
$CaPort = 8081
$ApiPort = 5180

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
    # /T for the same reason demo-reset.ps1 gives: `npm run dev` starts the real server as a child.
    & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
}

function Stop-Recorded {
    if (-not (Test-Path $PidFile)) { return }
    foreach ($line in Get-Content $PidFile) {
        $parsed = 0
        if ([int]::TryParse($line.Trim(), [ref]$parsed)) { Stop-Tree $parsed }
    }
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

<#
    winget installs mkcert as a package alias that a shell started before the install cannot see -
    which is exactly the shell this usually runs in. Look it up rather than trust PATH.
#>
function Resolve-Mkcert {
    $onPath = Get-Command mkcert -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $packaged = Get-ChildItem -Path "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter 'mkcert.exe' `
        -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($packaged) { return $packaged.FullName }
    throw 'mkcert not found. Install it with: winget install FiloSottile.mkcert'
}

function Resolve-Adb {
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" }
    $candidate = Join-Path $sdk 'platform-tools\adb.exe'
    if (Test-Path $candidate) { return $candidate }
    return $null
}

function Get-LanIp {
    if ($LanIp) { return $LanIp }
    # Tailscale and other virtual adapters answer this query too, and a phone on the house Wi-Fi
    # cannot reach a 100.x CGNAT address - so prefer RFC1918 and say out loud which was picked.
    $candidates = @(Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object {
            $_.PrefixOrigin -in 'Dhcp', 'Manual' -and
            $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*'
        } |
        Sort-Object -Property @{ Expression = { $_.IPAddress -like '192.168.*' -or $_.IPAddress -like '10.*' } } -Descending)
    if ($candidates.Count -eq 0) { throw 'No LAN IPv4 address found. Pass -LanIp explicitly.' }
    if ($candidates.Count -gt 1) {
        Write-Note ("several addresses; using {0} ({1}). Others: {2}" -f `
            $candidates[0].IPAddress, $candidates[0].InterfaceAlias,
            (($candidates | Select-Object -Skip 1).IPAddress -join ', '))
    }
    return $candidates[0].IPAddress
}

# ---------------------------------------------------------------------------- teardown

New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null

Write-Step 'Stopping anything a previous run started'
Stop-Recorded
foreach ($port in @($HttpsPort, $CaPort)) {
    $owner = Get-PortOwner $port
    if ($owner -ne 0) { Stop-Tree $owner }
}

$adb = Resolve-Adb
if ($Stop) {
    if ($adb) { & $adb reverse --remove-all 2>&1 | Out-Null }
    Write-Host 'Spike servers stopped.' -ForegroundColor Green
    return
}

# ---------------------------------------------------------------------------- preconditions

if ((Get-PortOwner $ApiPort) -eq 0) {
    throw "Nothing is listening on $ApiPort. Run .\scripts\demo-reset.ps1 first - it starts the API, Azurite and the plain-http Vite on $HttpPort."
}
if ((Get-PortOwner $HttpPort) -eq 0) {
    Write-Warning "Nothing is listening on $HttpPort. The Samsung reaches the app through that port; run .\scripts\demo-reset.ps1."
}

$ip = Get-LanIp

# ---------------------------------------------------------------------------- certificate

$mkcert = Resolve-Mkcert
New-Item -ItemType Directory -Path $CertDir -Force | Out-Null
$certFile = Join-Path $CertDir "$ip.pem"
$keyFile = Join-Path $CertDir "$ip-key.pem"

if (-not (Test-Path $certFile) -or -not (Test-Path $keyFile)) {
    Write-Step "Minting a leaf certificate for $ip"
    # -install puts the root in this machine's own trust store. Harmless if already there, and it is
    # what makes the same certificate work in desktop Chrome as well as on the phone.
    & $mkcert -install 2>&1 | ForEach-Object { Write-Note $_ }
    # localhost and 127.0.0.1 ride on the same leaf, so one certificate serves the LAN and loopback.
    & $mkcert -cert-file $certFile -key-file $keyFile $ip 'localhost' '127.0.0.1' 2>&1 |
        ForEach-Object { Write-Note $_ }
    if (-not (Test-Path $certFile)) { throw 'mkcert did not produce a certificate.' }
} else {
    Write-Note "reusing $certFile"
}

$caRoot = (& $mkcert -CAROOT).Trim()
# Copied as .crt into a directory of its own. The extension is what makes iOS offer to install a
# profile rather than render the PEM as text, and the separate directory is what keeps the private
# key in $CertDir from being served alongside it when -ServeCa is on.
New-Item -ItemType Directory -Path $CaShareDir -Force | Out-Null
$caCopy = Join-Path $CaShareDir 'rootCA.crt'
Copy-Item (Join-Path $caRoot 'rootCA.pem') $caCopy -Force

# ---------------------------------------------------------------------------- https vite

Write-Step "Starting the HTTPS dev server on $HttpsPort"
Remove-Item $WebLog, $WebErrLog -Force -ErrorAction SilentlyContinue

<#
    The certificate reaches Vite through a file on disk, NOT through the environment, and that is a
    bug fix rather than a preference. The first version exported SPIKE_HTTPS_CERT/KEY into the child
    process; it worked until `git checkout` touched vite.config.ts, Vite restarted the server
    in-process, re-evaluated its config without those variables, and silently came back on plain http
    with no LAN binding. Safari then loses the secure context and the camera, the voice note and web
    push all fail at once - looking exactly like the platform limits this spike exists to measure.
    A file survives a restart; an env var set by a script that has already exited does not.
#>
@{ cert = $certFile; key = $keyFile } | ConvertTo-Json | Set-Content -LiteralPath $HttpsDescriptor -Encoding utf8

$web = Start-Process -PassThru -WindowStyle Hidden -FilePath 'npm.cmd' `
    -ArgumentList 'run', 'dev', '--', '--config', 'vite.config.spike.ts', '--port', $HttpsPort, '--strictPort' `
    -WorkingDirectory $WebDir -RedirectStandardOutput $WebLog -RedirectStandardError $WebErrLog
Add-Content -LiteralPath $PidFile -Value $web.Id

$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline -and (Get-PortOwner $HttpsPort) -eq 0) { Start-Sleep -Milliseconds 300 }
if ((Get-PortOwner $HttpsPort) -eq 0) { throw "The HTTPS dev server did not start; see $WebErrLog." }

# ---------------------------------------------------------------------------- ca download

if ($ServeCa) {
    Write-Step "Serving the root CA on $CaPort"
    $ca = Start-Process -PassThru -WindowStyle Hidden -FilePath 'npx.cmd' `
        -ArgumentList '--yes', 'serve', '--listen', "tcp://0.0.0.0:$CaPort", $CaShareDir
    Add-Content -LiteralPath $PidFile -Value $ca.Id
}

# ---------------------------------------------------------------------------- adb

$reverseDone = $false
if ($adb) {
    $devices = @((& $adb devices) -split "`n" | Where-Object { $_ -match "\sdevice\s*$" })
    if ($devices.Count -gt 0) {
        & $adb reverse "tcp:$HttpPort" "tcp:$HttpPort" 2>&1 | Out-Null
        $reverseDone = $true
        Write-Note "adb reverse tcp:$HttpPort -> this machine ($($devices.Count) device(s))"
    } else {
        Write-Warning 'No authorised Android device. Plug the Samsung in, enable USB debugging, accept the RSA prompt on the handset, then re-run this script.'
    }
} else {
    Write-Warning 'adb not found; the Android half needs it.'
}

# ---------------------------------------------------------------------------- the card

Write-Host ''
Write-Host 'Device spike ready.' -ForegroundColor Green
Write-Host ''
Write-Host '  SAMSUNG (Capacitor shell)'
if ($reverseDone) {
    Write-Host "    reaches http://localhost:$HttpPort through adb reverse - already set up."
} else {
    Write-Host "    once the handset is authorised:  adb reverse tcp:$HttpPort tcp:$HttpPort"
}
Write-Host '    install the APK:  adb install -r src\Web\android\app\build\outputs\apk\debug\app-debug.apk'
Write-Host ''
Write-Host '  IPHONE (Safari / PWA)'
Write-Host '    open  ' -NoNewline
Write-Host "https://${ip}:$HttpsPort" -ForegroundColor Yellow
if ($ServeCa) {
    Write-Host '    install the root CA first from  ' -NoNewline
    Write-Host "http://${ip}:$CaPort/rootCA.crt" -ForegroundColor Yellow
} else {
    Write-Host "    root CA:  $caCopy   (-ServeCa serves it to the phone over http)"
}
Write-Host '    Settings > General > VPN & Device Management > install the profile,'
Write-Host '    THEN Settings > General > About > Certificate Trust Settings > turn it on.'
Write-Host '    The second step is separate, and is the one people miss.'
Write-Host ''
Write-Host "  Logs: $WebLog"
Write-Host '  Stop: .\scripts\spike-device.ps1 -Stop'
