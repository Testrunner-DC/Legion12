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
    --filter "FullyQualifiedName~JournalFailureClosureAdversarialTests|FullyQualifiedName~LatencyAndPersistenceRegressionTests|FullyQualifiedName~RankedPersistenceRecoveryTests"
$exitCode = $LASTEXITCODE

$elapsed = (Get-Date) - $startedAt
Write-Host "LC-03B journal failure matrix finished in $([Math]::Round($elapsed.TotalSeconds, 1)) seconds with exit code $exitCode."
if ($elapsed.TotalSeconds -gt 120) {
    throw "LC-03B Focused 超过 120 秒预算：$([Math]::Round($elapsed.TotalSeconds, 1)) 秒"
}
if ($exitCode -ne 0) { exit $exitCode }
