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
    --filter "FullyQualifiedName~LongChainJournalPerformanceBudgetTests" `
    --logger "console;verbosity=detailed"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test $project `
    --configuration $Configuration `
    --no-restore `
    --filter "FullyQualifiedName~LongChainJournalRecoveryTests|FullyQualifiedName~JournalFailureClosureAdversarialTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$elapsed = (Get-Date) - $startedAt
Write-Host "LC-03C long-chain baseline gate passed in $([Math]::Round($elapsed.TotalSeconds, 1)) seconds."
if ($elapsed.TotalSeconds -gt 180) {
    throw "LC-03C Focused 超过 180 秒预算：$([Math]::Round($elapsed.TotalSeconds, 1)) 秒"
}
