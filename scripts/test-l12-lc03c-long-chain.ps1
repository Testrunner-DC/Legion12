param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ArtifactsPath = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "TwelveLegions.Tests/TwelveLegions.Tests.csproj"
$startedAt = Get-Date
$dotnetArguments = @($project, "--configuration", $Configuration)
if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $dotnetArguments += @("--artifacts-path", [IO.Path]::GetFullPath($ArtifactsPath))
}

dotnet test @dotnetArguments `
    --filter "FullyQualifiedName~LongChainJournalPerformanceBudgetTests" `
    --logger "console;verbosity=detailed"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$correctnessStartedAt = Get-Date
dotnet test @dotnetArguments `
    --no-restore `
    --filter "FullyQualifiedName~LongChainJournalRecoveryTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$correctnessElapsed = (Get-Date) - $correctnessStartedAt
Write-Host "LC-03C-2 seven-risk correctness passed in $([Math]::Round($correctnessElapsed.TotalSeconds, 1)) seconds."
if ($correctnessElapsed.TotalSeconds -gt 90) {
    throw "LC-03C-2 正确性超过 90 秒预算：$([Math]::Round($correctnessElapsed.TotalSeconds, 1)) 秒"
}

dotnet test @dotnetArguments `
    --no-restore `
    --filter "FullyQualifiedName~JournalFailureClosureAdversarialTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test @dotnetArguments `
    --no-restore `
    --filter "FullyQualifiedName~ArchitectureP1CrossLayerGoldenTests|FullyQualifiedName~ArchitectureP2P3BoundaryTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$elapsed = (Get-Date) - $startedAt
Write-Host "LC-03C unified long-chain gate passed in $([Math]::Round($elapsed.TotalSeconds, 1)) seconds."
if ($elapsed.TotalSeconds -gt 180) {
    throw "LC-03C Focused 超过 180 秒预算：$([Math]::Round($elapsed.TotalSeconds, 1)) 秒"
}
