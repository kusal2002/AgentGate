param([switch]$UnitOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdkRoot = Join-Path $projectRoot 'sdk/typescript'
function Invoke-Check([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Verification command failed with exit code $LASTEXITCODE." } }
if (-not (Test-Path (Join-Path $sdkRoot 'node_modules/typescript'))) { Invoke-Check { npm --prefix $sdkRoot ci } }
Invoke-Check { npm --prefix $sdkRoot test }
Invoke-Check { npm --prefix $sdkRoot run typecheck }
Invoke-Check { npm --prefix $sdkRoot run lint }
Invoke-Check { npm --prefix $sdkRoot run test:package }
if ($UnitOnly) { return }

# Restore the caller's environment, including values imported from .env.
$savedEnvironment = @{}
Get-ChildItem Env: | ForEach-Object { $savedEnvironment[$_.Name] = $_.Value }
$runId = [Guid]::NewGuid().ToString('N')
$database = "agentgate_sdk_tests_$runId"
$runRoot = Join-Path $projectRoot ".local-verification/sdk-live-$runId"
$apiProcess = $null
$databaseCreated = $false
$psql = $null
Push-Location $projectRoot
try {
    . (Join-Path $PSScriptRoot 'load-local-env.ps1')
    Import-AgentGateEnvironment -ProjectRoot $projectRoot
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    if ($env:ConnectionStrings__AgentGate) { $connection.ConnectionString = $env:ConnectionStrings__AgentGate }
    else {
        $connection['Host'] = if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { 'localhost' }
        $connection['Port'] = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { '5432' }
        $connection['Username'] = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'postgres' }
        $connection['Password'] = if ($env:POSTGRES_PASSWORD) { $env:POSTGRES_PASSWORD } else { '' }
    }
    $env:PGHOST = [string]$connection['Host']; $env:PGPORT = [string]$connection['Port']
    $env:PGUSER = [string]$connection['Username']; $env:PGPASSWORD = [string]$connection['Password']
    $env:PGDATABASE = 'postgres'
    $psqlCommand = Get-Command psql -ErrorAction SilentlyContinue
    if ($psqlCommand) { $psql = $psqlCommand.Source }
    elseif (Test-Path 'D:/Applications/POSTGRESQL/bin/psql.exe') { $psql = 'D:/Applications/POSTGRESQL/bin/psql.exe' }
    else { throw 'Add your installed PostgreSQL bin directory to PATH so psql can create the isolated test database.' }
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    Invoke-Check { & $psql -X -v ON_ERROR_STOP=1 -c "CREATE DATABASE $database" }
    $databaseCreated = $true
    $connection['Database'] = $database
    $env:ConnectionStrings__AgentGate = $connection.ConnectionString
    $env:POSTGRES_DB = $database
    $secretBytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($secretBytes)
    $env:JWT_SECRET = [Convert]::ToBase64String($secretBytes)
    # Disable every live Slack credential/configuration in the child API.
    foreach ($name in @('SLACK_BOT_TOKEN','SLACK_SIGNING_SECRET','SLACK_APP_ID','SLACK_TEAM_ID','SLACK_ORGANIZATION_ID')) { [Environment]::SetEnvironmentVariable($name, '', 'Process') }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ArtifactsPath = Join-Path $projectRoot '.local-verification/phase10-build'
    $env:UseArtifactsOutput = 'true'
    Invoke-Check { dotnet build backend/AgentGate.Api -m:1 --artifacts-path $env:ArtifactsPath }
    Invoke-Check { dotnet tool restore }
    Invoke-Check { dotnet ef database update --no-build --project backend/AgentGate.Infrastructure --startup-project backend/AgentGate.Api }
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
    $apiExecutable = Join-Path $env:ArtifactsPath 'bin/AgentGate.Api/debug/AgentGate.Api.exe'
    $apiProcess = Start-Process -FilePath $apiExecutable -WorkingDirectory (Join-Path $projectRoot 'backend/AgentGate.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'api.log') -RedirectStandardError (Join-Path $runRoot 'api-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($apiProcess.HasExited) { throw "Isolated API exited. Inspect $runRoot/api-error.log." }
        try { $null = Invoke-RestMethod "$env:ASPNETCORE_URLS/health/ready" -TimeoutSec 1; $ready = $true; break } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw "Isolated API did not become ready. Inspect $runRoot/api.log." }
    $env:AGENTGATE_SDK_TEST_URL = $env:ASPNETCORE_URLS
    $env:AGENTGATE_SDK_TEST_MODE = 'isolated'
    Invoke-Check { npm --prefix $sdkRoot run test:integration }
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        try { Stop-Process -Id $apiProcess.Id; $apiProcess.WaitForExit() }
        catch { Write-Warning 'Could not stop the isolated SDK API process.' }
    }
    if ($databaseCreated -and $database -match '^agentgate_sdk_tests_[0-9a-f]{32}$') {
        & $psql -X -v ON_ERROR_STOP=1 -c "DROP DATABASE $database WITH (FORCE)"
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove isolated database $database." }
    }
    foreach ($item in @(Get-ChildItem Env:)) {
        if (-not $savedEnvironment.ContainsKey($item.Name)) { [Environment]::SetEnvironmentVariable($item.Name, $null, 'Process') }
    }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    Pop-Location
}
