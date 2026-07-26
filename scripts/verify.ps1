<#
.SYNOPSIS
  The gate. Nothing is "done" in PRAXIS until this passes.

.DESCRIPTION
  Builds and tests all three stacks: the .NET core, the Python AI plane, the Next.js web console.

  Stacks that do not exist yet are reported as SKIPPED, loudly and by name. A skip is not a pass,
  and the verdict says so. A verify script that quietly reports success for work it never ran is
  worse than no verify script at all.

  ASCII only, deliberately: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, and box-drawing
  characters come back as mojibake.

.PARAMETER Quick
  .NET only. For a fast inner loop.
#>
[CmdletBinding()]
param(
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][ValidateSet('PASS', 'FAIL', 'SKIP')][string]$Status,
        [string]$Detail = ''
    )
    $results.Add([pscustomobject]@{ Stage = $Stage; Status = $Status; Detail = $Detail })
}

function Invoke-Stage {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "--> $Stage" -ForegroundColor Cyan

    & $Action

    if ($LASTEXITCODE -ne 0) {
        Add-Result -Stage $Stage -Status 'FAIL' -Detail "exit $LASTEXITCODE"
        return $false
    }

    Add-Result -Stage $Stage -Status 'PASS'
    return $true
}

Write-Host ''
Write-Host 'PRAXIS - verify' -ForegroundColor White

# --- .NET core ---------------------------------------------------------------
# Warnings are errors (Directory.Build.props), so a clean build is itself a test pass.
$built = Invoke-Stage '.NET  build' { dotnet build "$root\Praxis.slnx" --nologo -v quiet }

if ($built[-1] -eq $true) {
    Invoke-Stage '.NET  test' { dotnet test "$root\Praxis.slnx" --no-build --nologo -v quiet } | Out-Null
}
else {
    Add-Result -Stage '.NET  test' -Status 'SKIP' -Detail 'build failed'
}

# --- Python AI plane ---------------------------------------------------------
$aiDir = Join-Path $root 'ai'

if (-not (Test-Path (Join-Path $aiDir 'pyproject.toml'))) {
    Add-Result -Stage 'AI    lint' -Status 'SKIP' -Detail 'ai/ not built yet (M5.1)'
    Add-Result -Stage 'AI    test' -Status 'SKIP' -Detail 'ai/ not built yet (M5.1)'
}
elseif ($Quick) {
    Add-Result -Stage 'AI    lint' -Status 'SKIP' -Detail '-Quick'
    Add-Result -Stage 'AI    test' -Status 'SKIP' -Detail '-Quick'
}
elseif (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    # Named, not silently skipped. A machine without uv cannot verify the AI plane, and it must say
    # so rather than printing a green tick it has not earned.
    Add-Result -Stage 'AI    lint' -Status 'SKIP' -Detail 'uv is not installed (https://astral.sh/uv)'
    Add-Result -Stage 'AI    test' -Status 'SKIP' -Detail 'uv is not installed (https://astral.sh/uv)'
}
else {
    Push-Location $aiDir
    try {
        # `uv run` syncs the venv from the lockfile first, so the gate tests the pinned versions
        # rather than whatever happens to be installed on this machine.
        Invoke-Stage 'AI    lint' { uv run ruff check . } | Out-Null
        Invoke-Stage 'AI    test' { uv run pytest -q } | Out-Null
    }
    finally {
        Pop-Location
    }
}

# --- Web console -------------------------------------------------------------
$webDir = Join-Path $root 'web'

if (-not (Test-Path (Join-Path $webDir 'package.json'))) {
    Add-Result -Stage 'Web   lint' -Status 'SKIP' -Detail 'web/ not built yet (M11.1)'
    Add-Result -Stage 'Web   typecheck' -Status 'SKIP' -Detail 'web/ not built yet (M11.1)'
    Add-Result -Stage 'Web   test' -Status 'SKIP' -Detail 'web/ not built yet (M11.1)'
    Add-Result -Stage 'Web   build' -Status 'SKIP' -Detail 'web/ not built yet (M11.1)'
}
elseif ($Quick) {
    Add-Result -Stage 'Web   lint' -Status 'SKIP' -Detail '-Quick'
    Add-Result -Stage 'Web   typecheck' -Status 'SKIP' -Detail '-Quick'
    Add-Result -Stage 'Web   test' -Status 'SKIP' -Detail '-Quick'
    Add-Result -Stage 'Web   build' -Status 'SKIP' -Detail '-Quick'
}
else {
    Push-Location $webDir
    try {
        # A fresh checkout has no node_modules. Install from the committed lockfile before linting
        # or building, so the web stage is self-contained rather than assuming a prior `npm install`.
        if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
            Invoke-Stage 'Web   deps' { npm ci --silent } | Out-Null
        }

        Invoke-Stage 'Web   lint' { npm run lint } | Out-Null
        # `npm run build` only type-checks source files, never `*.test.ts(x)` - a type error in a
        # test (including a guard that exists purely to fail a *type* check, never a runtime one)
        # passes lint, passes test (vitest transpiles through esbuild and never type-checks), and
        # passes build without this stage. This is the only stage that runs `tsc` over everything.
        Invoke-Stage 'Web   typecheck' { npm run typecheck } | Out-Null
        Invoke-Stage 'Web   test' { npm run test } | Out-Null
        Invoke-Stage 'Web   build' { npm run build } | Out-Null
    }
    finally {
        Pop-Location
    }
}

# --- Verdict -----------------------------------------------------------------
Write-Host ''
Write-Host 'Summary' -ForegroundColor White

foreach ($r in $results) {
    $colour = switch ($r.Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'SKIP' { 'Yellow' }
    }
    $detail = if ($r.Detail) { "  ($($r.Detail))" } else { '' }
    Write-Host ("  {0,-6} {1}{2}" -f $r.Status, $r.Stage, $detail) -ForegroundColor $colour
}

$failedCount  = @($results | Where-Object { $_.Status -eq 'FAIL' }).Count
$skippedCount = @($results | Where-Object { $_.Status -eq 'SKIP' }).Count

Write-Host ''

if ($failedCount -gt 0) {
    Write-Host "FAILED - $failedCount stage(s) did not pass." -ForegroundColor Red
    exit 1
}

if ($skippedCount -gt 0) {
    Write-Host "PASSED - but $skippedCount stage(s) were SKIPPED, not run. A skip is not a pass." -ForegroundColor Yellow
    exit 0
}

Write-Host 'PASSED - every stage ran and passed.' -ForegroundColor Green
exit 0
