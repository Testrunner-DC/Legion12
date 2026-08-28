[CmdletBinding()]
param(
    [ValidateSet("Focused", "Batch", "Release")]
    [string]$Level = "Focused",
    [string[]]$ChangedPaths = @(),
    [string]$CacheRoot = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$script:paths = @()

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$originalLocation = Get-Location

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
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE" }
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
    & git '-c' 'core.quotepath=false' 'status' '--porcelain=v1' '--untracked-files=all'
    if ($LASTEXITCODE -ne 0) { throw "Unable to read changed paths from Git" }
}

try {
    Set-Location $repoRoot
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
        foreach ($statusLine in @(Get-GitChangedPathStatus)) {
            if (-not $statusLine -or $statusLine.Length -le 3) { continue }
            $path = $statusLine.Substring(3)
            if ($path.Contains(" -> ")) { $path = $path.Substring($path.LastIndexOf(" -> ") + 4) }
            $script:paths += $path.Replace("\", "/")
        }
        $script:paths = @($script:paths | Sort-Object -Unique)
    }
    if ($LASTEXITCODE -ne 0) { throw "Unable to read changed paths from Git" }

    Write-Host "[L12 $Level] Changed files: $($script:paths.Count)"
    $script:paths | ForEach-Object { Write-Host "  $_" }

    $configChanged = Test-AnyPath @('^\.codex/', '(^|/)AGENTS\.md$', '^scripts/verify-l12-change\.ps1$', '^scripts/verify-l12-codex-routing\.ps1$', '^docs/(TASK-LEDGER|CHANGE-BATCH-WORKFLOW|REGRESSION-FIXTURES)\.md$')
    $backendChanged = Test-AnyPath @('^service-backend-never-match$', '^TwelveLegions\.Tests/', '^scripts/(audit-l12-atomic-effects|export-l12-legacy-effect-inventory|migrate-l12-card-cases-to-atomic-routes)')
    $platformChanged = Test-AnyPath @('^service-tests-never-match$')
    $frontendChanged = Test-AnyPath @('^opcgpro-vue/', '^scripts/(ws-smoke|ws-ui-peer)')
    $cardEffectChanged = Test-AnyPath @('^TwelveLegions\.Tests/')
    $workflowChanged = Test-AnyPath @('^\.github/workflows/verify-release\.yml$', '^scripts/verify-l12-github-workflow\.ps1$')
    $storageChanged = Test-AnyPath @('^scripts/(audit-l12-storage|clean-l12-generated)\.ps1$', '^ops/windows/(watch-l12-network|finalize-l12-codex-session-move)\.ps1$', '^docs/STORAGE-(GOVERNANCE|MAINTENANCE)\.md$')

    # Add non-ASCII service paths without embedding them in this Windows PowerShell 5 compatible source file.
    foreach ($path in $script:paths) {
        if ($path.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase) -and -not $path.StartsWith("TwelveLegions.Tests/", [StringComparison]::OrdinalIgnoreCase)) {
            $backendChanged = $true
        }
        if ($path.EndsWith("GrandUMIServer.Tests.csproj", [StringComparison]::OrdinalIgnoreCase) -or $path.Contains("WebSocket.Tests/") -or $path.Contains("WebSocketBridge")) {
            $platformChanged = $true
        }
        if ($path.Contains("/TwelveLegions/") -and $path.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase)) {
            $cardEffectChanged = $true
        }
    }

    Invoke-Checked "Git whitespace and conflict-marker check" "git" @("diff", "--check")

    if ($configChanged) {
        Invoke-Checked "Codex TOML and routing validation" "powershell" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            (Join-Path $repoRoot "scripts\verify-l12-codex-routing.ps1")
        )
    }

    if ($workflowChanged) {
        Invoke-Checked "GitHub verification/release workflow contract" "powershell" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            (Join-Path $repoRoot "scripts\verify-l12-github-workflow.ps1")
        )
    }

    if ($storageChanged) {
        Invoke-Checked "D-drive storage budget and layout" "powershell" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            (Join-Path $repoRoot "scripts\audit-l12-storage.ps1"), "-Strict"
        )
    }

    if ($Level -eq "Focused") {
        if ($backendChanged) {
            Invoke-Checked "L12 focused rule tests" "dotnet" @("test", ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj", "--no-restore")
        }
        if ($platformChanged) {
            $platformProject = Get-ChildItem -LiteralPath $repoRoot -Filter "GrandUMIServer.Tests.csproj" -Recurse | Select-Object -First 1 -ExpandProperty FullName
            Invoke-Checked "Platform persistence focused tests" "dotnet" @("test", $platformProject, "--no-restore", "--filter", "FullyQualifiedName~PlatformStoreTests|FullyQualifiedName~ControlPlane")
        }
        if ($frontendChanged) {
            Invoke-Checked "Frontend UI contracts" "npm.cmd" @("run", "check:ui-contracts") (Join-Path $repoRoot "opcgpro-vue")
        }
        return
    }

    if ($backendChanged) {
        Invoke-Checked "L12 full rule tests" "dotnet" @("test", ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj", "--configuration", "Release")
    }
    if ($platformChanged -or $Level -eq "Release") {
        $platformProject = Get-ChildItem -LiteralPath $repoRoot -Filter "GrandUMIServer.Tests.csproj" -Recurse | Select-Object -First 1 -ExpandProperty FullName
        Invoke-Checked "Platform persistence release gate" "dotnet" @("test", $platformProject, "--configuration", "Release", "--filter", "FullyQualifiedName~PlatformStoreTests|FullyQualifiedName~ControlPlane")
    }
    if ($cardEffectChanged) {
        Invoke-Checked "Atomic runtime zero-legacy audit" "powershell" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\audit-l12-atomic-effects.ps1", "-RequireZero")
    }
    if ($frontendChanged) {
        Invoke-Checked "Frontend production build" "npm.cmd" @("run", "build") (Join-Path $repoRoot "opcgpro-vue")
    }

    if ($Level -eq "Release") {
        Invoke-Checked "Commit-level release verification (no deployment)" "powershell" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\ops\windows\verify-l12.ps1", "-CacheRoot", $env:L12_WORK_CACHE)
    }
}
finally { Set-Location $originalLocation }
