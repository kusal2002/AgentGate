param([switch]$MigrateOnly)
# Load .env as data, never as executable PowerShell. Invoke from any directory.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'load-local-env.ps1')
Import-AgentGateEnvironment -ProjectRoot $projectRoot
if ($MigrateOnly) {
    Push-Location $projectRoot
    try {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet ef database update --project backend/AgentGate.Infrastructure --startup-project backend/AgentGate.Api
    } finally { Pop-Location }
} else {
    dotnet run --project (Join-Path $projectRoot 'backend/AgentGate.Api') --launch-profile http
}
exit $LASTEXITCODE
