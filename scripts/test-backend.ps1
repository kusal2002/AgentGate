param([switch]$UnitOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'load-local-env.ps1')
Import-AgentGateEnvironment -ProjectRoot $projectRoot
if (-not $UnitOnly -and -not $env:AGENTGATE_TEST_CONNECTION) {
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    if ($env:ConnectionStrings__AgentGate) { $connection.ConnectionString = $env:ConnectionStrings__AgentGate }
    else {
        $connection['Host'] = if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { 'localhost' }
        $connection['Port'] = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { '5432' }
        $connection['Username'] = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'agentgate' }
        $connection['Password'] = if ($env:POSTGRES_PASSWORD) { $env:POSTGRES_PASSWORD } else { '' }
    }
    $connection['Database'] = 'postgres'
    $env:AGENTGATE_TEST_CONNECTION = $connection.ConnectionString
}
$testProject = Join-Path $projectRoot 'backend/AgentGate.Tests'
# Separate test build outputs from the running API's DLLs, which Windows locks.
$testArtifacts = Join-Path $projectRoot '.local-verification/backend-tests'
if ($UnitOnly) { dotnet test $testProject -m:1 --artifacts-path $testArtifacts --filter 'FullyQualifiedName~RoleRulesTests|FullyQualifiedName~AgentKeyCodecTests|FullyQualifiedName~ActionPayloadTests|FullyQualifiedName~PolicyEvaluatorTests|FullyQualifiedName~ApprovalRulesTests|FullyQualifiedName~SlackSecurityTests|FullyQualifiedName~SlackClientTests' }
else { dotnet test $testProject -m:1 --artifacts-path $testArtifacts }
exit $LASTEXITCODE
