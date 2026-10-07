# Load .env as data, never as executable PowerShell. Invoke from any directory.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$envFile = Join-Path $projectRoot '.env'
if (Test-Path -LiteralPath $envFile) {
    foreach ($line in Get-Content -LiteralPath $envFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') {
            $settingName = $Matches[1]
            $settingValue = $Matches[2].Trim()
            if ($settingValue.Length -ge 2 -and
                (($settingValue.StartsWith('"') -and $settingValue.EndsWith('"')) -or
                 ($settingValue.StartsWith("'") -and $settingValue.EndsWith("'")))) {
                $settingValue = $settingValue.Substring(1, $settingValue.Length - 2)
            }
            if ($settingName -match '^(POSTGRES_|ConnectionStrings__)' -and
                $null -eq [Environment]::GetEnvironmentVariable($settingName, 'Process')) {
                [Environment]::SetEnvironmentVariable($settingName, $settingValue, 'Process')
            }
        }
    }
}
dotnet run --project (Join-Path $projectRoot 'backend/AgentGate.Api') --launch-profile http
exit $LASTEXITCODE
