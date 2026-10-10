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

$matrixFilter = "FullyQualifiedName~RecipientPrivacyMatrixAdversarialTests"
for ($run = 1; $run -le 2; $run++) {
    $matrixStartedAt = Get-Date
    dotnet test @dotnetArguments `
        --filter $matrixFilter `
        --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $matrixElapsed = (Get-Date) - $matrixStartedAt
    Write-Host "LC-05A recipient matrix run $run passed in $([Math]::Round($matrixElapsed.TotalSeconds, 1)) seconds."
    if ($matrixElapsed.TotalSeconds -gt 30) {
        throw "LC-05A 单轮矩阵超过 30 秒预算：$([Math]::Round($matrixElapsed.TotalSeconds, 1)) 秒"
    }
}

$focusedStartedAt = Get-Date
$focusedFilter = @(
    "FullyQualifiedName~RecipientPrivacyMatrixAdversarialTests",
    "FullyQualifiedName~PromptPresentationContractTests",
    "FullyQualifiedName~TrialProgressPrivacyTests",
    "FullyQualifiedName~PromptAuditLogTests",
    "FullyQualifiedName~MatchRecorderTests.PlayerReplayRedactsOpponentPrivateZonesAndUsesCoveredCardOwnershipKnowledge",
    "FullyQualifiedName~WebSocketTransportRecoveryAdversarialTests",
    "FullyQualifiedName~WebSocketConnectionGenerationTests"
) -join "|"
dotnet test @dotnetArguments `
    --no-restore `
    --filter $focusedFilter `
    --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$focusedElapsed = (Get-Date) - $focusedStartedAt
Write-Host "LC-05A focused privacy suite passed in $([Math]::Round($focusedElapsed.TotalSeconds, 1)) seconds."
if ($focusedElapsed.TotalSeconds -gt 90) {
    throw "LC-05A 聚焦套件超过 90 秒预算：$([Math]::Round($focusedElapsed.TotalSeconds, 1)) 秒"
}

dotnet test @dotnetArguments `
    --no-restore `
    --filter "FullyQualifiedName~ArchitectureP1CrossLayerGoldenTests|FullyQualifiedName~ArchitectureP2P3BoundaryTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

node (Join-Path $repositoryRoot "scripts/check-l12-architecture-lock.mjs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$elapsed = (Get-Date) - $startedAt
Write-Host "LC-05A unified gate passed in $([Math]::Round($elapsed.TotalSeconds, 1)) seconds."
if ($elapsed.TotalSeconds -gt 180) {
    throw "LC-05A 统一门禁超过 180 秒预算：$([Math]::Round($elapsed.TotalSeconds, 1)) 秒"
}
