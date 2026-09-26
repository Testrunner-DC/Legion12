param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "TwelveLegions.Tests/TwelveLegions.Tests.csproj"
$startedAt = Get-Date

dotnet test $project `
    --configuration $Configuration `
    --filter "FullyQualifiedName~WebSocketTransportRecoveryAdversarialTests|FullyQualifiedName~WebSocketConnectionGenerationTests|FullyQualifiedName~LongChainJournalRecoveryTests"

$elapsed = (Get-Date) - $startedAt
if ($elapsed.TotalMinutes -gt 15) {
    throw "LC-03A 真实 WebSocket 专项超过 15 分钟预算：$([Math]::Round($elapsed.TotalMinutes, 2)) 分钟"
}

Write-Host "LC-03A transport recovery gate passed in $([Math]::Round($elapsed.TotalSeconds, 1)) seconds."
