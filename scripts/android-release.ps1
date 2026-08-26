#Requires -Version 7
<#
.SYNOPSIS
    Builds the signed Android release APK (slice 6.3).

.DESCRIPTION
    Four preconditions, then three commands. The preconditions are the point: each one is a way the
    release build silently produces something that installs, runs, and is wrong.

      1. CAP_SERVER_URL must be UNSET. Set, `capacitor.config.ts` adds `server.url`, `cleartext` and
         `webContentsDebuggingEnabled` - an APK that loads its web layer from a laptop, permits plain
         http application-wide, and lets anyone with adb attach DevTools to a signed build and read
         an expert's claims. It runs perfectly on the developer's desk, which is the problem.

      2. google-services.json must exist and must not carry PLACEHOLDER values. Without it the app
         compiles and installs and simply never receives a push - the BRD's primary trigger, absent,
         with no error anywhere. Gradle warns; nobody reads a warning in a 300-line build log.

      3. keystore.properties must exist. Without it Gradle produces app-release-UNSIGNED.apk, which
         cannot be installed on a handset, and the failure appears at `adb install` rather than here.

      4. A JDK 21 must be findable. The Capacitor plugins pin a Java 21 toolchain; this machine's
         PATH java is 17, and Gradle's auto-detection does NOT find the Microsoft OpenJDK install on
         its own - the failure is `Cannot find a Java installation ... matching {languageVersion=21}`
         four minutes into a build. JAVA_HOME is set here rather than left to the environment.

    Nothing here is a secret in the repository: the keystore, its properties file and the real
    google-services.json are all gitignored (android/.gitignore), and the service-account JSON the
    *server* signs FCM with never comes near this script - it is a mounted secret, like the VAPID
    private key (design.md §10).

.PARAMETER SkipWebBuild
    Reuse the existing src/Web/dist instead of running `npm run build`. For a second attempt after a
    Gradle-only failure; never for a release you intend to ship.

.PARAMETER JdkHome
    Override the JDK 21 used for the Gradle toolchain. Detected from the usual install locations
    when omitted.
#>
[CmdletBinding()]
param(
    [switch]$SkipWebBuild,
    [string]$JdkHome
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WebDir = Join-Path $RepoRoot 'src\Web'
$AndroidDir = Join-Path $WebDir 'android'
$KeystoreProperties = Join-Path $AndroidDir 'keystore.properties'
$GoogleServices = Join-Path $AndroidDir 'app\google-services.json'
$Placeholder = Join-Path $AndroidDir 'app\google-services.json.placeholder'
$ApkDir = Join-Path $AndroidDir 'app\build\outputs\apk\release'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Note([string]$Message) { Write-Host "    $Message" -ForegroundColor DarkGray }

# ------------------------------------------------------------------ preconditions

Write-Step 'Checking the release preconditions'

if ($env:CAP_SERVER_URL) {
    throw @"
CAP_SERVER_URL is set ('$($env:CAP_SERVER_URL)').

A release build must not carry a dev server. With it set, capacitor.config.ts adds server.url,
cleartext traffic and WebView debugging - the APK would load its web layer from that address,
permit plain http, and allow anyone with adb to attach DevTools to a signed build.

Clear it and run again:  `$env:CAP_SERVER_URL = `$null
"@
}
Write-Note 'CAP_SERVER_URL is unset - the config will build in its release shape.'

if (-not (Test-Path $GoogleServices)) {
    throw @"
No google-services.json at $GoogleServices

Without it the app builds and installs and can never receive a push notification, which is the
BRD's primary requirement. Download it from the Firebase console (Project settings -> Your apps ->
Android) - the registered package name must be com.axa.motorclaims, because FCM binds to the
applicationId compiled into the APK.

$Placeholder shows the shape. It is gitignored on purpose; it is environment-specific, not secret.
"@
}

# `-cmatch`, not `-match`. PowerShell's default is case-INSENSITIVE while the Gradle guard uses
# Groovy's case-sensitive `contains('PLACEHOLDER')`, so the two would disagree about the same file —
# the template's own prose says "a placeholder" in lower case, which this would have rejected and
# Gradle would have accepted. Two layers checking one rule have to check it the same way.
if ((Get-Content $GoogleServices -Raw) -cmatch 'PLACEHOLDER') {
    throw @"
google-services.json still carries PLACEHOLDER values.

That is the template copied rather than the real file. Gradle skips the google-services plugin on a
placeholder (deliberately - a hard failure on a clean clone would be worse), so this would produce a
release APK that cannot receive push, quietly.
"@
}
Write-Note 'google-services.json is present and carries no placeholders.'

if (-not (Test-Path $KeystoreProperties)) {
    throw @"
No keystore.properties at $KeystoreProperties

Without it Gradle emits app-release-unsigned.apk, which no handset will install.

Create the keystore once (keep both the file and the passwords somewhere durable - losing them means
this app can never be updated again, because Android identifies an app by its signing key):

  keytool -genkeypair -v -keystore $AndroidDir\release.jks ``
      -keyalg RSA -keysize 2048 -validity 10000 -alias axa-motor-claims

Then write $KeystoreProperties (gitignored):

  storeFile=release.jks
  storePassword=...
  keyAlias=axa-motor-claims
  keyPassword=...
"@
}
Write-Note 'keystore.properties is present - the release build will be signed.'

# The Capacitor plugins pin a Java 21 toolchain and Gradle does not auto-detect this machine's
# install, so the failure is four minutes in and reads as a Gradle problem rather than a PATH one.
if (-not $JdkHome) {
    $candidates = @(
        'C:\Program Files\Microsoft\jdk-21*',
        'C:\Program Files\Eclipse Adoptium\jdk-21*',
        'C:\Program Files\Java\jdk-21*',
        'C:\dev\tools\jdk21'
    )
    $JdkHome = $candidates |
        ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $JdkHome -or -not (Test-Path (Join-Path $JdkHome 'bin\java.exe'))) {
    throw @"
No JDK 21 found. The Capacitor plugins require a Java 21 toolchain and Gradle will not download one.

Install it (winget install Microsoft.OpenJDK.21) or pass -JdkHome <path>.
"@
}

$env:JAVA_HOME = $JdkHome
Write-Note "JAVA_HOME = $JdkHome"

# ------------------------------------------------------------------ build

if ($SkipWebBuild) {
    Write-Warning 'Skipping the web build; the APK will package whatever is already in src/Web/dist.'
} else {
    Write-Step 'Building the web app (tsc + eslint + vite)'
    Push-Location $WebDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}

Write-Step 'Syncing the native project'
Push-Location $WebDir
try {
    npx cap sync android
    if ($LASTEXITCODE -ne 0) { throw "npx cap sync android failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}

Write-Step 'Assembling the signed release APK'
Push-Location $AndroidDir
try {
    & .\gradlew.bat assembleRelease --no-daemon
    if ($LASTEXITCODE -ne 0) { throw "gradlew assembleRelease failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}

# ------------------------------------------------------------------ result

$unsigned = Join-Path $ApkDir 'app-release-unsigned.apk'
if (Test-Path $unsigned) {
    # Belt and braces over precondition 3: if Gradle ever emits an unsigned artifact despite the
    # properties file being present, saying so here beats discovering it at `adb install`.
    throw "Gradle produced an UNSIGNED apk at $unsigned - check $KeystoreProperties."
}

$apk = Get-ChildItem $ApkDir -Filter '*.apk' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $apk) { throw "No APK was produced in $ApkDir." }

Write-Host ''
Write-Host 'Release APK built.' -ForegroundColor Green
Write-Host ''
Write-Host "  APK:      $($apk.FullName)"
Write-Host "  Size:     $([math]::Round($apk.Length / 1MB, 1)) MB"
Write-Host ''
Write-Host '  Install:  adb install -r "' -NoNewline; Write-Host "$($apk.FullName)`""
Write-Host ''
Write-Host '  Before shipping, confirm on the APK itself (the week-6 checklist):'
Write-Note 'no server.url and no cleartext:  unzip -p <apk> assets/capacitor.config.json'
Write-Note 'debugging off:                   aapt dump badging <apk> | findstr debuggable'
Write-Note 'signed:                          apksigner verify --verbose <apk>'
