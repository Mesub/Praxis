<#
.SYNOPSIS
  Bring PRAXIS up locally.

.DESCRIPTION
  Starts Postgres, waits for it to actually be healthy (not merely "started"), then runs the API.
  The Python AI plane and the web console are started too, once they exist.

.PARAMETER InfraOnly
  Bring up Postgres and stop there.

.PARAMETER Down
  Tear the containers down. Add -Volumes to also delete the data.

.PARAMETER Volumes
  With -Down, delete the Postgres volume as well. This destroys the event log.
#>
[CmdletBinding()]
param(
    [switch]$InfraOnly,
    [switch]$Down,
    [switch]$Volumes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($Down) {
    if ($Volumes) {
        Write-Host 'Tearing down AND deleting the data volume — the event log will be gone.' -ForegroundColor Yellow
        docker compose -f "$root\docker-compose.yml" down -v
    }
    else {
        docker compose -f "$root\docker-compose.yml" down
    }
    exit $LASTEXITCODE
}

Write-Host 'Starting Postgres...' -ForegroundColor Cyan
docker compose -f "$root\docker-compose.yml" up -d

if ($LASTEXITCODE -ne 0) {
    Write-Host 'Could not start Postgres. Is Docker Desktop running?' -ForegroundColor Red
    exit 1
}

# "Started" is not "ready to accept a connection". Wait for the healthcheck.
Write-Host 'Waiting for Postgres to become healthy...' -ForegroundColor Cyan

$deadline = (Get-Date).AddSeconds(90)
$healthy = $false

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
    docker compose -f "$root\docker-compose.yml" logs --tail 30 postgres
    exit 1
}

Write-Host 'Postgres is healthy on localhost:5433.' -ForegroundColor Green

if ($InfraOnly) {
    exit 0
}

Write-Host ''
Write-Host 'Starting the API on http://localhost:5080 ...' -ForegroundColor Cyan
Write-Host 'Ctrl+C to stop. Postgres keeps running — use scripts\dev.ps1 -Down to stop it.' -ForegroundColor DarkGray
Write-Host ''

dotnet run --project "$root\src\Praxis.Api\Praxis.Api.csproj"
