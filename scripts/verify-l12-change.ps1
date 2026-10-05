[CmdletBinding()]
param(
    [ValidateSet("Focused", "Batch", "Release")]
    [string]$Level = "Focused",
    [string[]]$ChangedPaths = @(),
    [string]$CacheRoot = "",
    [string]$ProductionBaseCommit = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$script:paths = @()

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$originalLocation = Get-Location
. (Join-Path $PSScriptRoot 'lib/l12-test-storage.ps1')

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )
    Write-Host "[L12 $Level] $Label"
    Write-Host "  $Executable $($Arguments -join ' ')"
    if ($DryRun) { return }
    Push-Location $WorkingDirectory
    try {
        if ($Executable -eq 'dotnet' -and $Arguments[0] -eq 'test') {
            Invoke-L12TestRun -Executable $Executable -Arguments $Arguments -Label $Label `
                -TemporaryBase (Join-Path $env:L12_WORK_CACHE 'test-temp') -EvidenceBase (Join-Path $env:L12_WORK_CACHE 'test-evidence')
            return
        }
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

function Invoke-CheckedPowerShellScript {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [hashtable]$NamedArguments = @{},
        [string]$WorkingDirectory = $repoRoot
    )
    $renderedArguments = @($NamedArguments.GetEnumerator() | Sort-Object Key | ForEach-Object {
        "-$($_.Key) $($_.Value)"
    }) -join ' '
    Write-Host "[L12 $Level] $Label"
    Write-Host "  & $ScriptPath $renderedArguments"
    if ($DryRun) { return }
    Push-Location $WorkingDirectory
    try {
        & $ScriptPath @NamedArguments
        if (-not $?) { throw "$Label failed" }
    }
    finally { Pop-Location }
}

function Test-AnyPath {
    param([string[]]$Patterns)
    foreach ($path in $script:paths) {
        foreach ($pattern in $Patterns) {
            if ($path -match $pattern) { return $true }
        }
    }
    return $false
}

function Get-GitChangedPathStatus {
    $statusLines = @(& git '-c' 'core.quotepath=false' 'status' '--porcelain=v1' '--untracked-files=all')
    if ($LASTEXITCODE -ne 0) { throw "Unable to read changed paths from Git" }
    return $statusLines
}

function Get-HeadChangedPaths {
    $headPaths = @(& git '-c' 'core.quotepath=false' 'diff-tree' '--root' '--no-commit-id' '--name-only' '-r' '-m' '--first-parent' 'HEAD')
    if ($LASTEXITCODE -ne 0) { throw "Unable to read changed paths from HEAD" }
    return @($headPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

try {
    Set-Location $repoRoot
    if ($ProductionBaseCommit.Length -gt 0) {
        if ($Level -ne "Release") { throw "ProductionBaseCommit 仅可用于提交级 Release 验证。" }
        if ($ProductionBaseCommit -notmatch '^[0-9a-fA-F]{40}$') { throw "ProductionBaseCommit 必须为完整 40 位提交 SHA。" }
    }
    if ($Level -eq "Release" -and -not $DryRun) {
        $releaseDirtyPaths = @(Get-GitChangedPathStatus)
        if ($releaseDirtyPaths.Count -gt 0) {
            throw "Release verification requires a clean committed tree; commit or otherwise preserve local changes first."
        }
    }
    $cacheInitializer = Join-Path $repoRoot "ops\windows\Initialize-L12BuildEnvironment.ps1"
    if (-not $DryRun) {
        & $cacheInitializer -CacheRoot $CacheRoot | Out-Null
        if (-not $?) { throw "Build cache initialization failed" }
    }

    if ($ChangedPaths.Count -gt 0) {
        $script:paths = @($ChangedPaths | ForEach-Object { $_.Replace("\", "/") } | Sort-Object -Unique)
    }
    else {
        # 中文路径若沿用 Git 默认 quotepath，会被转义成八进制字符串，导致后端与
        # 平台变更无法命中门禁规则。统一从 porcelain 状态读取已暂存、未暂存及
        # 未跟踪文件，避免多次 Git 调用在 Windows PowerShell 5 下丢失前两次输出。
        $script:paths = @()
        $statusLines = @(Get-GitChangedPathStatus)
        foreach ($statusLine in $statusLines) {
            if (-not $statusLine -or $statusLine.Length -le 3) { continue }
            $path = $statusLine.Substring(3)
            if ($path.Contains(" -> ")) { $path = $path.Substring($path.LastIndexOf(" -> ") + 4) }
            $script:paths += $path.Replace("\", "/")
        }
        # Release is intentionally restricted to a clean commit. In that state the
        # porcelain list is empty, so classify HEAD itself to retain every targeted
        # declaration, atomic, workflow, storage, and configuration audit.
        if ($Level -eq "Release" -and $statusLines.Count -eq 0) {
            $script:paths = @(Get-HeadChangedPaths | ForEach-Object { $_.Replace("\", "/") })
        }
        $script:paths = @($script:paths | Sort-Object -Unique)
    }

    Write-Host "[L12 $Level] Changed files: $($script:paths.Count)"
    $script:paths | ForEach-Object { Write-Host "  $_" }

    Invoke-Checked "P0-P4 architecture exit lock" "node" @((Join-Path $repoRoot "scripts\check-l12-architecture-lock.mjs"))
    Invoke-CheckedPowerShellScript "P1 kernel dependency boundary" `
        (Join-Path $repoRoot "scripts\test-l12-core-architecture-boundaries.ps1")

    $configChanged = Test-AnyPath @('^\.codex/', '(^|/)AGENTS\.md$', '^scripts/verify-l12-change\.ps1$', '^scripts/verify-l12-codex-routing\.ps1$', '^docs/(TASK-LEDGER|CHANGE-BATCH-WORKFLOW|REGRESSION-FIXTURES)\.md$')
    $runtimeEvidenceChanged = Test-AnyPath @('^scripts/lib/l12-card-runtime-evidence\.ps1$', '^scripts/test-l12-card-runtime-evidence\.ps1$', '^scripts/export-l12-card-effect-review-matrix\.ps1$')
    $publicActiveChanged = Test-AnyPath @(
        '^scripts/test-l12-public-active-declarations\.ps1$',
        '(^|/)L12PublicActiveEffectPlans\.cs$',
        '(^|/)L12CompositeEffectPlans\.cs$',
        '(^|/)L12RuleKernelIntegration\.cs$',
        '(^|/)L12S1FactionEffects\.cs$',
        '(^|/)L12S2RemainingEffects\.cs$'
    )
    $publicTriggerChanged = Test-AnyPath @(
        '^scripts/test-l12-public-trigger-declarations\.ps1$',
        '^scripts/test-l12-private-zone-summon-transactions\.ps1$',
        '(^|/)L12PublicTriggerEffectPlans\.cs$',
        '(^|/)L12TrialAdvanceEffectPlans\.cs$',
        '(^|/)L12RuleKernelIntegration\.cs$',
        '(^|/)L12S1FactionEffects\.cs$',
        '(^|/)L12S1ExtendedEffects\.cs$',
        '(^|/)L12S2CounterTactics\.cs$',
        '(^|/)L12S2FactionEffects\.cs$',
        '(^|/)L12S2RemainingEffects\.cs$'
    )
    $publicResponseChanged = Test-AnyPath @(
        '^scripts/test-l12-public-response-declarations\.ps1$',
        '(^|/)L12PublicResponseEffectPlans\.cs$',
        '(^|/)L12CompositeEffectPlans\.cs$',
        '(^|/)L12PromptsAndSetup\.cs$',
        '(^|/)L12PublicTriggerEffectPlans\.cs$',
        '(^|/)L12RuleKernelIntegration\.cs$',
        '(^|/)L12S1ExtendedEffects\.cs$',
        '(^|/)L12S2CounterTactics\.cs$'
    )
    $publicHandPlayChanged = Test-AnyPath @(
        '^scripts/test-l12-hand-play-declarations\.ps1$',
        '^scripts/test-l12-effect-generated-play-transactions\.ps1$',
        '(^|/)L12Actions\.cs$',
        '(^|/)L12CompositeEffectPlans\.cs$',
        '(^|/)L12EffectGeneratedPlay\.cs$',
        '(^|/)L12PromptsAndSetup\.cs$',
        '(^|/)L12RuleKernelIntegration\.cs$',
        '(^|/)L12S1ExtendedEffects\.cs$',
        '(^|/)L12S1FactionEffects\.cs$',
        '(^|/)L12S2UniversalEffects\.cs$'
    )
    $backendChanged = $runtimeEvidenceChanged -or $publicActiveChanged -or $publicTriggerChanged -or $publicResponseChanged -or $publicHandPlayChanged -or (Test-AnyPath @('^TwelveLegions\.Tests/', '^scripts/(audit-l12-atomic-effects|export-l12-legacy-effect-inventory|migrate-l12-card-cases-to-atomic-routes|test-l12-st-effect-audit)'))
    $platformChanged = Test-AnyPath @('^TwelveLegions\.Platform\.Tests/')
    $frontendChanged = Test-AnyPath @('^opcgpro-vue/', '^scripts/(ws-smoke|ws-ui-peer)')
    $cardEffectChanged = $runtimeEvidenceChanged -or $publicActiveChanged -or $publicTriggerChanged -or $publicResponseChanged -or $publicHandPlayChanged -or (Test-AnyPath @('^TwelveLegions\.Tests/'))
    $testStorageChanged = Test-AnyPath @('^scripts/(lib/l12-test-storage|test-l12-test-storage|invoke-l12-tests)\.ps1$', '^scripts/lib/TestStorageIsolationTests\.cs$', '^ops/windows/Initialize-L12BuildEnvironment\.ps1$')
    $workflowChanged = $testStorageChanged -or (Test-AnyPath @('^\.github/workflows/verify-release\.yml$', '^scripts/(check-l12-architecture-lock\.mjs|verify-l12-github-workflow\.ps1)$'))
    if ($testStorageChanged) { $backendChanged=$true; $platformChanged=$true }
    $storageChanged = Test-AnyPath @('^scripts/(audit-l12-storage|clean-l12-generated|test-l12-cleanup|test-l12-storage-audit)\.ps1$', '^ops/windows/(watch-l12-network|finalize-l12-codex-session-move)\.ps1$', '^docs/STORAGE-(GOVERNANCE|MAINTENANCE)\.md$')
    $releaseGateChanged = Test-AnyPath @('^ops/windows/verify-l12\.ps1$', '^ops/windows/deploy-l12\.ps1$', '^scripts/verify-l12-change\.ps1$', '^scripts/test-l12-release-gate\.ps1$', '^scripts/(release-ledger|test-release-ledger|release-status|test-release-status)\.mjs$', '^release-ledger/')
    $deploymentBehaviorChanged = Test-AnyPath @('^ops/windows/(deploy-l12|L12DeployTarget)\.ps1$', '^ops/server/(deploy-l12-release\.sh|verify-l12-health\.mjs|verify-l12-runtime-backup\.py)$', '^scripts/(test-l12-deploy-behavior|verify-l12-change)\.ps1$', '^scripts/test-l12-runtime-backup\.py$')
    $testrunDeploymentChanged = Test-AnyPath @('^ops/server/deploy-l12-testrun-release\.sh$', '^scripts/(test-l12-testrun-deploy-behavior|verify-l12-change)\.ps1$')

    # Non-ASCII service paths are classified by their filename. Unknown shared
    # server sources intentionally exercise both suites rather than silently
    # skipping the dedicated platform gate.
    foreach ($path in $script:paths) {
        if (-not $path.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($path -eq 'scripts/lib/TestStorageIsolationTests.cs') { continue } # Both full suites selected above; no card semantics changed.
        if ($path -match '^TwelveLegions\.Tests/') { continue }
        if ($path -match '^TwelveLegions\.Platform\.Tests/') { continue }
        $filename = [IO.Path]::GetFileName($path)
        $platformOnly = $path -match '/TwelveLegions/' -and $filename -match '^(L12PlatformStore(?:\..*)?|MatchRecorder(?:\..*)?|L12AdminControlPlane|L12ServerStorageMonitor|L12UsernamePolicy|L12TrustedClientAddress|MatchAnalyticsModels)\.cs$'
        $ruleOnly = $path -match '/TwelveLegions/' -and $filename -match '^(L12GameEngine(?:\..*)?|L12RuleKernelIntegration|L12CardEffects|L12S[12].*Effects|L12Public(?:Active|Response|Trigger)EffectPlans|L12CompositeEffectPlans|RuleKernel|AtomicEffects)\.cs$'
        if (-not $platformOnly) {
            $backendChanged = $true
            $cardEffectChanged = $true
        }
        if ($platformOnly -or -not $ruleOnly) { $platformChanged = $true }
    }

    # A frontend Batch build and the isolated Release build run the complete
    # performance lock as their first npm build step. Focused and non-frontend
    # Batch paths still run it here because they do not build the frontend.
    if ($Level -eq "Focused" -or ($Level -eq "Batch" -and -not $frontendChanged)) {
        Invoke-Checked "Low-latency performance architecture lock" "npm.cmd" @("run", "check:performance-architecture") (Join-Path $repoRoot "opcgpro-vue")
    }

    Invoke-Checked "Git whitespace and conflict-marker check" "git" @("diff", "--check")

    if ($cardEffectChanged) {
        Invoke-CheckedPowerShellScript "Card runtime semantic evidence mapping" `
            (Join-Path $repoRoot "scripts\test-l12-card-runtime-evidence.ps1")
        Invoke-CheckedPowerShellScript "Public active predeclaration guard" `
            (Join-Path $repoRoot "scripts\test-l12-public-active-declarations.ps1")
        Invoke-CheckedPowerShellScript "Public trigger predeclaration guard" `
            (Join-Path $repoRoot "scripts\test-l12-public-trigger-declarations.ps1")
        Invoke-CheckedPowerShellScript "Public response predeclaration and independent-segment guard" `
            (Join-Path $repoRoot "scripts\test-l12-public-response-declarations.ps1")
        Invoke-CheckedPowerShellScript "Public hand-play predeclaration and independent-segment guard" `
            (Join-Path $repoRoot "scripts\test-l12-hand-play-declarations.ps1")
        Invoke-CheckedPowerShellScript "Effect-generated play and private-zone transaction guard" `
            (Join-Path $repoRoot "scripts\test-l12-effect-generated-play-transactions.ps1")
        Invoke-CheckedPowerShellScript "S01 universal and Heaven per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s01-universal-heaven-audit.ps1")
        Invoke-CheckedPowerShellScript "S01 Sun City and Asgard per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s01-sun-city-asgard-audit.ps1")
        Invoke-CheckedPowerShellScript "S01 Takamagahara per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s01-takamagahara-audit.ps1")
        Invoke-CheckedPowerShellScript "S02 universal and Heaven per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s02-universal-heaven-audit.ps1")
        Invoke-CheckedPowerShellScript "S02 Sun City and Asgard per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s02-sun-city-asgard-audit.ps1")
        Invoke-CheckedPowerShellScript "S02 Takamagahara and Olympus per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s02-takamagahara-olympus-audit.ps1")
        Invoke-CheckedPowerShellScript "S02 Otherworld and disaster per-ability audit guard" `
            (Join-Path $repoRoot "scripts\test-l12-s02-otherworld-disaster-audit.ps1")
        Invoke-CheckedPowerShellScript "Trial completion TriggerBatch and declaration guard" `
            (Join-Path $repoRoot "scripts\test-l12-trial-completion-declarations.ps1")
        Invoke-CheckedPowerShellScript "Private-zone summon resolution transaction guard" `
            (Join-Path $repoRoot "scripts\test-l12-private-zone-summon-transactions.ps1")
        Invoke-CheckedPowerShellScript "ST full catalog effect coverage guard" `
            (Join-Path $repoRoot "scripts\test-l12-st-effect-audit.ps1")
    }

    if ($configChanged) {
        Invoke-CheckedPowerShellScript "Codex TOML and routing validation" `
            (Join-Path $repoRoot "scripts\verify-l12-codex-routing.ps1")
    }

    if ($testStorageChanged) {
        Invoke-CheckedPowerShellScript 'Test temporary lifecycle and shared dependency regression' (Join-Path $repoRoot 'scripts/test-l12-test-storage.ps1')
    }
    if ($workflowChanged) {
        Invoke-CheckedPowerShellScript "GitHub verification/release workflow contract" `
            (Join-Path $repoRoot "scripts\verify-l12-github-workflow.ps1")
    }

    if ($storageChanged) {
        Invoke-CheckedPowerShellScript 'Storage scoped-budget and link behavior regression' (Join-Path $repoRoot 'scripts/test-l12-storage-audit.ps1')
        Invoke-CheckedPowerShellScript "D-drive storage budget and layout" `
            (Join-Path $repoRoot "scripts\audit-l12-storage.ps1") @{ Strict = $true; Scope = 'Active'; CandidateRoot = $repoRoot }
        Invoke-CheckedPowerShellScript "Generated-output cleanup behavior regression" `
            (Join-Path $repoRoot "scripts\test-l12-cleanup.ps1")
    }

    if ($releaseGateChanged) {
        Invoke-Checked "Player release ledger regression" "node" @(".\scripts\test-release-ledger.mjs")
        Invoke-Checked "Release status source regression" "node" @(".\scripts\test-release-status.mjs")
        Invoke-CheckedPowerShellScript "Release verification gate regression" `
            (Join-Path $repoRoot "scripts\test-l12-release-gate.ps1")
    }

    if ($deploymentBehaviorChanged) {
        $deploymentCacheRoot = $env:L12_WORK_CACHE
        if ([string]::IsNullOrWhiteSpace($deploymentCacheRoot)) {
            $deploymentCacheRoot = if ($CacheRoot) { $CacheRoot } elseif (Test-Path "D:\GPT\Legion12") { "D:\GPT\Legion12\cache\primary" } else { Join-Path $repoRoot ".l12-cache" }
        }
        $deploymentFixtureBase = if ([string]::IsNullOrWhiteSpace($env:L12_WORK_CACHE)) {
            Join-Path $deploymentCacheRoot "temp"
        } else {
            Join-Path $env:L12_WORK_CACHE "temp"
        }
        Invoke-Checked "Deployment target, health and failure-preservation behavior" "pwsh" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            (Join-Path $repoRoot "scripts\test-l12-deploy-behavior.ps1"),
            "-FixtureBase", $deploymentFixtureBase
        )
        Invoke-Checked "Stopped SQLite/WAL snapshot proof regressions" "python" @("-B", (Join-Path $repoRoot "scripts/test-l12-runtime-backup.py"))
    }

    if ($testrunDeploymentChanged) {
        $testrunFixtureBase = if ($env:L12_WORK_CACHE) { Join-Path $env:L12_WORK_CACHE "temp" } elseif ($CacheRoot) { Join-Path $CacheRoot "temp" } else { "D:/GPT/Legion12/cache/primary/temp" }
        Invoke-Checked "Test release storage budget and protected evidence behavior" "pwsh" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            (Join-Path $repoRoot "scripts/test-l12-testrun-deploy-behavior.ps1"), "-FixtureBase", $testrunFixtureBase
        )
    }

    if ($Level -eq "Focused") {
        if ($backendChanged) {
            Invoke-Checked "L12 focused rule tests" "dotnet" @("test", ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj", "--no-restore", "--", "xUnit.ParallelizeTestCollections=false")
        }
        if ($platformChanged) {
            $platformProject = Join-Path $repoRoot "TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj"
            Invoke-Checked "Platform persistence focused tests" "dotnet" @("test", $platformProject, "--no-restore", "--", "xUnit.ParallelizeTestCollections=false")
        }
        if ($frontendChanged) {
            Invoke-Checked "Frontend UI contracts" "npm.cmd" @("run", "check:ui-contracts") (Join-Path $repoRoot "opcgpro-vue")
        }
        return
    }

    if ($cardEffectChanged) {
        Invoke-CheckedPowerShellScript "Atomic runtime zero-legacy audit" `
            (Join-Path $repoRoot "scripts\audit-l12-atomic-effects.ps1") @{ RequireZero = $true }
    }

    if ($Level -eq "Release") {
        $releaseArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\ops\windows\verify-l12.ps1")
        if (-not [string]::IsNullOrWhiteSpace($env:L12_WORK_CACHE)) {
            $releaseArguments += @("-CacheRoot", $env:L12_WORK_CACHE)
        }
        if ($ProductionBaseCommit.Length -gt 0) {
            $releaseArguments += @("-ProductionBaseCommit", $ProductionBaseCommit)
        }
        Invoke-Checked "Commit-level release verification (no deployment)" "pwsh" $releaseArguments
        return
    }

    if ($backendChanged) {
        Invoke-Checked "L12 full rule tests" "dotnet" @("test", ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj", "--configuration", "Release", "--", "xUnit.ParallelizeTestCollections=false")
    }
    if ($platformChanged) {
        $platformProject = Join-Path $repoRoot "TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj"
        Invoke-Checked "Platform persistence release gate" "dotnet" @("test", $platformProject, "--configuration", "Release", "--", "xUnit.ParallelizeTestCollections=false")
    }
    if ($frontendChanged) {
        $clientRelease = (& git -C $repoRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $clientRelease -notmatch '^[0-9a-f]{40}$') { throw "无法读取前端构建提交版本" }
        $previousClientRelease = $env:VITE_APP_VERSION
        try {
            $env:VITE_APP_VERSION = $clientRelease
            Invoke-Checked "Frontend production build" "npm.cmd" @("run", "build") (Join-Path $repoRoot "opcgpro-vue")
        }
        finally { $env:VITE_APP_VERSION = $previousClientRelease }
    }
}
finally { Set-Location $originalLocation }
