# PostToolUse hook (Edit|Write): format-check edited C# files.
# No-ops (exit 0) until AxaMotorClaims.sln exists - the repo is docs-only until week 1.
# NOTE: keep this file ASCII-only; powershell.exe misreads BOM-less UTF-8.
$ErrorActionPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)  # .claude/hooks -> repo root
$sln = Join-Path $repoRoot 'AxaMotorClaims.sln'
if (-not (Test-Path $sln)) { exit 0 }

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$file = $payload.tool_input.file_path
if (-not $file -or $file -notmatch '\.cs$') { exit 0 }

$out = dotnet format $sln --include $file --verify-no-changes --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    # exit 2 feeds the output back to Claude so it fixes formatting before moving on
    [Console]::Error.WriteLine("dotnet format reports issues in ${file}:`n$out`nRun 'dotnet format --include $file' or fix manually.")
    exit 2
}
exit 0
