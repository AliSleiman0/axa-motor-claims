# Stop hook: run the test suite so Claude sees failures before ending its turn.
# No-ops (exit 0) until src/Api.Tests exists - the repo is docs-only until week 1.
# NOTE: keep this file ASCII-only; powershell.exe misreads BOM-less UTF-8.
$ErrorActionPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tests = Join-Path $repoRoot 'src\Api.Tests'
if (-not (Test-Path $tests)) { exit 0 }

$out = dotnet test $tests --nologo --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    $tail = ($out | Select-Object -Last 40) -join "`n"
    # exit 2 blocks the stop and feeds the failures back to Claude
    [Console]::Error.WriteLine("dotnet test failed - fix before finishing the turn (never weaken a test to pass it):`n$tail")
    exit 2
}
exit 0
