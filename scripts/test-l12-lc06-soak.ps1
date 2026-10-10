param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ArtifactsPath = "",
    [string]$EvidenceDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "TwelveLegions.Tests/TwelveLegions.Tests.csproj"
$startedAt = Get-Date
$soakStartedAt = Get-Date
$temporaryEvidence = [string]::IsNullOrWhiteSpace($EvidenceDirectory)

if ($temporaryEvidence) {
    $evidenceRoot = Join-Path ([IO.Path]::GetTempPath()) ("l12-lc06-" + [Guid]::NewGuid().ToString("N"))
} else {
    $evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
}
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

$dotnetArguments = @($project, "--configuration", $Configuration)
if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $dotnetArguments += @("--artifacts-path", [IO.Path]::GetFullPath($ArtifactsPath))
}

function Read-Evidence([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "LC-06 测试没有生成证据文件：$Path"
    }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
}

function Assert-StableRuns($First, $Second) {
    if ($First.Cases.Count -ne 9 -or $Second.Cases.Count -ne 9) {
        throw "LC-06 两轮均必须恰好包含 9 个 case：first=$($First.Cases.Count), second=$($Second.Cases.Count)"
    }
    for ($index = 0; $index -lt $First.Cases.Count; $index++) {
        $left = $First.Cases[$index]
        $right = $Second.Cases[$index]
        $identity = "$($left.ScenarioId)/$($left.Seed)/$($left.TargetCommandCount)"
        if ($left.ScenarioId -ne $right.ScenarioId `
            -or $left.Seed -ne $right.Seed `
            -or $left.TargetCommandCount -ne $right.TargetCommandCount) {
            throw "LC-06 两轮 case 顺序或身份不一致：index=$index, first=$identity"
        }
        if ($left.StableFingerprint -ne $right.StableFingerprint) {
            throw "LC-06 两轮确定性指纹不一致：$identity；first=$($left.StableFingerprint), second=$($right.StableFingerprint)"
        }
        Write-Host ("LC-06 stable case {0}: state={1}B projection={2}B journal={3}B checkpoint={4}B database={5}B wall={6}s" -f `
            $identity, $right.StateBytes, $right.MaximumProjectionBytes, $right.JournalBytes, `
            $right.CheckpointBytes, $right.DatabaseBytes, [Math]::Round($right.TotalMilliseconds / 1000, 3))
    }
}

try {
    $reports = @()
    for ($run = 1; $run -le 2; $run++) {
        $evidencePath = Join-Path $evidenceRoot "lc06-soak-run-$run.json"
        $env:L12_LC06_RUN_ORDINAL = "run-$run"
        $env:L12_LC06_EVIDENCE_PATH = $evidencePath
        $runArguments = @($dotnetArguments)
        if ($run -eq 2) { $runArguments += "--no-restore" }
        dotnet test @runArguments `
            --filter "FullyQualifiedName~LongChainSoakClosureTests" `
            --logger "console;verbosity=normal"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $report = Read-Evidence $evidencePath
        $reports += $report
        Write-Host "LC-06 deep soak run $run passed: 9/9 cases, evidence=$evidencePath"
    }

    Assert-StableRuns $reports[0] $reports[1]
    $soakElapsed = (Get-Date) - $soakStartedAt
    if ($soakElapsed.TotalMinutes -gt 20) {
        throw "LC-06 18 次专项总墙钟超过 20 分钟：$([Math]::Round($soakElapsed.TotalMinutes, 2)) 分钟"
    }
    foreach ($scale in $reports[1].Scales) {
        Write-Host ("LC-06 scale {0}: journal={1}B ({2}B/cmd), checkpoint={3}B ({4}B/cmd), database={5}B ({6}B/cmd)" -f `
            $scale.TargetCommandCount, $scale.JournalBytes, [Math]::Round($scale.JournalBytesPerCommand, 3), `
            $scale.CheckpointBytes, [Math]::Round($scale.CheckpointBytesPerCommand, 3), `
            $scale.DatabaseBytes, [Math]::Round($scale.DatabaseBytesPerCommand, 3))
    }

    $adjacentFilter = @(
        "FullyQualifiedName~LongChainJournalRecoveryTests",
        "FullyQualifiedName~LongChainLifecycleContractTests",
        "FullyQualifiedName~LongChainJournalPerformanceBudgetTests",
        "FullyQualifiedName~RecipientPrivacyMatrixAdversarialTests",
        "FullyQualifiedName~JournalFailureClosureAdversarialTests",
        "FullyQualifiedName~ArchitectureP1CrossLayerGoldenTests",
        "FullyQualifiedName~ArchitectureP2P3BoundaryTests"
    ) -join "|"
    dotnet test @dotnetArguments `
        --no-restore `
        --filter $adjacentFilter `
        --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    node (Join-Path $repositoryRoot "scripts/check-l12-architecture-lock.mjs")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $elapsed = (Get-Date) - $startedAt
    Write-Host "LC-06 unified soak gate passed in $([Math]::Round($elapsed.TotalMinutes, 2)) minutes; deep soak=$([Math]::Round($soakElapsed.TotalMinutes, 2)) minutes."
}
finally {
    Remove-Item Env:L12_LC06_RUN_ORDINAL -ErrorAction SilentlyContinue
    Remove-Item Env:L12_LC06_EVIDENCE_PATH -ErrorAction SilentlyContinue
    if ($temporaryEvidence -and (Test-Path -LiteralPath $evidenceRoot)) {
        $resolvedEvidenceRoot = [IO.Path]::GetFullPath($evidenceRoot)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedEvidenceRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) `
            -or -not ([IO.Path]::GetFileName($resolvedEvidenceRoot)).StartsWith("l12-lc06-", [StringComparison]::Ordinal)) {
            throw "拒绝清理未验证的 LC-06 临时目录：$resolvedEvidenceRoot"
        }
        Remove-Item -LiteralPath $resolvedEvidenceRoot -Recurse -Force
    }
}
