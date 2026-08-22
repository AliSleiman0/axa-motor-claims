#Requires -Version 7
<#
.SYNOPSIS
    Prints the newest thing the fake SMS sender said to one phone (slice 4.3).

.DESCRIPTION
    Login and invite codes go to the console, not to a handset (design.md 6.2, #7/#40 unanswered),
    so during the demo they have to be read out of demo-artifacts\api.log. Doing that by eye costs
    twenty seconds per sign-in and four sign-ins is most of a beat.

    Prints the whole message and, when there is one, the six-digit code on its own line so it can be
    copied without selecting around it.

.PARAMETER Phone
    E.164, as it appears in the log - e.g. +999000003001.

.PARAMETER Wait
    Seconds to wait for a message that has not arrived yet. 0 (the default) reads what is there now.

.EXAMPLE
    .\scripts\demo-otp.ps1 +999000003001
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string]$Phone,
    [int]$Wait = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ApiLog = Join-Path (Split-Path -Parent $PSScriptRoot) 'demo-artifacts\api.log'
if (-not (Test-Path $ApiLog)) { throw "No $ApiLog. Run .\scripts\demo-reset.ps1 first." }

function Get-LatestMessage {
    # Shared read: the API still has this file open (the same reason demo-reset.ps1 does this).
    $stream = [System.IO.File]::Open(
        $ApiLog, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try { $text = (New-Object System.IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }

    $escaped = [regex]::Escape($Phone)
    $matched = [regex]::Matches($text, "FAKE SMS to ${escaped}: (.+)")
    if ($matched.Count -eq 0) { return $null }
    return $matched[$matched.Count - 1].Groups[1].Value.Trim()
}

$deadline = (Get-Date).AddSeconds($Wait)
do {
    $message = Get-LatestMessage
    if ($message) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)

if (-not $message) { throw "Nothing has been sent to $Phone. Did the sign-in request go through?" }

Write-Host $message
if ($message -match '(\d{6})') { Write-Host $Matches[1] -ForegroundColor Green }
