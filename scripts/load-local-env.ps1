function Import-AgentGateEnvironment {
    param([string]$ProjectRoot)
    $envFile = Join-Path $ProjectRoot '.env'
    if (-not (Test-Path -LiteralPath $envFile)) { return }
    foreach ($line in Get-Content -LiteralPath $envFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') {
            $settingName = $Matches[1]
            $settingValue = $Matches[2].Trim()
            if ($settingValue.Length -ge 2 -and
                (($settingValue.StartsWith('"') -and $settingValue.EndsWith('"')) -or
                 ($settingValue.StartsWith("'") -and $settingValue.EndsWith("'")))) {
                $settingValue = $settingValue.Substring(1, $settingValue.Length - 2)
            }
            if ($settingName -match '^(POSTGRES_|JWT_|ConnectionStrings__|PolicyDefaults__|Approvals__)' -and
                $null -eq [Environment]::GetEnvironmentVariable($settingName, 'Process')) {
                [Environment]::SetEnvironmentVariable($settingName, $settingValue, 'Process')
            }
        }
    }
}
