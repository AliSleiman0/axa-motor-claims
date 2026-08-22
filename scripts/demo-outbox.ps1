#Requires -Version 7
<#
.SYNOPSIS
    Prints the outbox, the documents and the notification log (slice 4.3). Reads only.

.DESCRIPTION
    A thin wrapper over scripts\demo-outbox.cs so the demo card lists three commands of the same
    shape. The work is in the .cs file; see its header for why it exists (A2 is slice 6.2).

.PARAMETER WarmUp
    Restore and build the file-based app, print nothing. demo-reset.ps1 calls this so the first
    real invocation during the demo is instant instead of a ten-second restore.
#>
[CmdletBinding()]
param([switch]$WarmUp)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$app = Join-Path $PSScriptRoot 'demo-outbox.cs'

if ($WarmUp) {
    # The database is deliberately not required to be present yet: a build is all this is after, and
    # the app exits 1 with its own message when it cannot connect.
    $output = & dotnet run $app 2>&1
    # A build error here would otherwise surface for the first time mid-demo, which is exactly what
    # a warm-up exists to prevent. A *connection* failure is fine - the caller may run this before
    # the database exists - and the app reports that on stderr with its own exit code 1.
    if ($LASTEXITCODE -ne 0 -and $output -notmatch 'Could not open the demo database') {
        throw "demo-outbox.cs did not build:`n$($output -join "`n")"
    }
    return
}

& dotnet run $app
