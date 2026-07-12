<#
.SYNOPSIS
  Bring PRAXIS up locally.

.DESCRIPTION
  Starts Postgres, waits for it to be genuinely healthy (not merely "started"), then runs the API.
  The Python AI plane and the web console join in once they exist.

  ASCII only, and $ErrorActionPreference stays at Continue around native calls: Windows PowerShell
  5.1 reads a BOM-less .ps1 as ANSI, and it treats anything docker writes to stderr -- including
  ordinary progress lines like "Container praxis-postgres Stopping" -- as a terminating error.

.PARAMETER InfraOnly
  Bring up Postgres and stop there.

.PARAMETER Down
  Tear the containers down. Add -Volumes to delete the data as well.

.PARAMETER Volumes
  With -Down, delete the Postgres volume. This destroys the event log. There is no undo.
#>
[CmdletBinding()]
param(
    [switch]$InfraOnly,
    [switch]$Down,
    [switch]$Volumes
)

$root    = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $root 'docker-compose.yml'

function Invoke-Native {
    param([Parameter(Mandatory)][scriptblock]$Command)

    # Native stderr is not an exception. Only the exit code is.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command 2>&1 | ForEach-Object { Write-Host $_ }
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

if ($Down) {
    if ($Volumes) {
        Write-Host 'Tearing down AND deleting the data volume. The event log will be gone.' -ForegroundColor Yellow
        $code = Invoke-Native { docker compose -f $compose down -v }
    }
    else {
        $code = Invoke-Native { docker compose -f $compose down }
    }

    exit $code
}

Write-Host 'Starting Postgres...' -ForegroundColor Cyan

$code = Invoke-Native { docker compose -f $compose up -d }

if ($code -ne 0) {
    Write-Host 'Could not start Postgres. Is Docker Desktop running?' -ForegroundColor Red
    exit 1
}

# "Started" is not "ready to accept a connection". Wait for the healthcheck to actually pass.
Write-Host 'Waiting for Postgres to become healthy...' -ForegroundColor Cyan

$deadline = (Get-Date).AddSeconds(90)
$healthy  = $false

while ((Get-Date) -lt $deadline) {
    $state = docker inspect --format '{{.State.Health.Status}}' praxis-postgres 2>$null

    if ($state -eq 'healthy') {
        $healthy = $true
        break
    }

    Start-Sleep -Seconds 2
}

if (-not $healthy) {
    Write-Host 'Postgres did not become healthy within 90s.' -ForegroundColor Red
    Invoke-Native { docker compose -f $compose logs --tail 30 postgres } | Out-Null
    exit 1
}

Write-Host 'Postgres is healthy on localhost:5455.' -ForegroundColor Green

if ($InfraOnly) {
    exit 0
}

Write-Host ''
Write-Host 'Starting the API. Ctrl+C to stop.' -ForegroundColor Cyan
Write-Host 'Postgres keeps running afterwards - use scripts\dev.ps1 -Down to stop it.' -ForegroundColor DarkGray
Write-Host ''

dotnet run --project (Join-Path $root 'src\Praxis.Api\Praxis.Api.csproj')

exit $LASTEXITCODE
