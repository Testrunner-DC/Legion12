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

$transportStartedAt = Get-Date
dotnet test @dotnetArguments `
    --filter "FullyQualifiedName~WebSocketTransportRecoveryAdversarialTests|FullyQualifiedName~WebSocketConnectionGenerationTests" `
    --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$transportElapsed = (Get-Date) - $transportStartedAt
Write-Host "LC-04A real WebSocket matrix passed in $([Math]::Round($transportElapsed.TotalSeconds, 1)) seconds."
if ($transportElapsed.TotalSeconds -gt 60) {
    throw "LC-04A 专项超过 60 秒预算：$([Math]::Round($transportElapsed.TotalSeconds, 1)) 秒"
}

dotnet test @dotnetArguments `
    --no-restore `
    --filter "FullyQualifiedName~ArchitectureP1CrossLayerGoldenTests|FullyQualifiedName~ArchitectureP2P3BoundaryTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

node (Join-Path $repositoryRoot "scripts/check-l12-architecture-lock.mjs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$elapsed = (Get-Date) - $startedAt
Write-Host "LC-04A unified gate passed in $([Math]::Round($elapsed.TotalSeconds, 1)) seconds."
if ($elapsed.TotalSeconds -gt 120) {
    throw "LC-04A 统一门禁超过 120 秒预算：$([Math]::Round($elapsed.TotalSeconds, 1)) 秒"
}
