[CmdletBinding()]
param(
    [string]$OutputDirectory = $(if ($env:L12_DEPLOY_CACHE) { $env:L12_DEPLOY_CACHE } elseif (Test-Path "D:\GPT\Legion12") { "D:\GPT\Legion12\artifacts\deploy" } else { Join-Path ([IO.Path]::GetTempPath()) "l12-deploy-artifacts" }),
    [string]$CacheRoot = "",
    [string]$CardAssetDirectory = $(if ($env:L12_CARD_ASSET_ROOT) { $env:L12_CARD_ASSET_ROOT } else { "D:\L12-assets\published\current" }),
    [string]$ProductionBaseCommit = "",
    [ValidateRange(1L, 1099511627776L)][long]$EvidenceBudgetBytes = 480MB,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot '../../scripts/lib/l12-test-storage.ps1')
Set-StrictMode -Version Latest

function Invoke-External {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Executable,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败（退出码 $LASTEXITCODE）：$Executable $($Arguments -join ' ')"
    }
}

function Invoke-TimedExternal {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Stage,
        [Parameter(Mandatory = $true, Position = 1)][string]$Executable,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $passed = $false
    try {
        if ($Executable -eq 'dotnet' -and $Arguments[0] -eq 'test') {
            Invoke-L12TestRun -Executable $Executable -Arguments $Arguments -Label $Stage `
                -TemporaryBase (Join-Path $env:L12_WORK_CACHE 'test-temp') -EvidenceBase (Join-Path $evidenceDirectory 'test-storage')
        } else { Invoke-External $Executable @Arguments }
        $passed = $true
    }
    finally {
        $watch.Stop()
        $script:verificationTimings.Add([ordered]@{
            stage = $Stage
            elapsedMilliseconds = [math]::Round($watch.Elapsed.TotalMilliseconds, 1)
            passed = $passed
        })
        Write-Host "[L12 验证] 阶段 $Stage：$([math]::Round($watch.Elapsed.TotalSeconds, 2)) 秒；通过=$passed"
    }
}

function Assert-PerformanceExceptionsCurrent {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)
    $path = Join-Path $RepositoryRoot "ops\performance-exceptions.json"
    $document = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
    if ($document.schema -ne 1) { throw "性能例外清单格式错误，拒绝复用已验证发布包：$path" }
    $today = [DateTime]::UtcNow.Date
    foreach ($exception in $document.exceptions) {
        $expiryText = [string]$exception.expiresAt
        $expiry = [DateTime]::MinValue
        $parsed = [DateTime]::TryParseExact($expiryText, "yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref]$expiry)
        if (-not $parsed -or $expiry.Date -lt $today) {
            throw "性能例外 $($exception.id) 已过期或日期无效（$expiryText）；请修正例外并从干净提交重新完整验证，拒绝复用发布包。"
        }
    }
}

function Get-VerificationEvidenceBytes {
    param([Parameter(Mandatory = $true)][string]$EvidenceRoot)
    if (-not (Test-Path -LiteralPath $EvidenceRoot)) { return 0L }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push([IO.Path]::GetFullPath($EvidenceRoot))
    $bytes = 0L
    while ($pending.Count -gt 0) {
        $directory = Get-Item -LiteralPath $pending.Pop() -Force
        if (-not $directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "验证证据目录不是普通目录，拒绝越界扫描：$($directory.FullName)"
        }
        foreach ($entry in Get-ChildItem -LiteralPath $directory.FullName -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "验证证据目录包含 junction/symlink，拒绝越界扫描：$($entry.FullName)"
            }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            else { $bytes += $entry.Length }
        }
    }
    return $bytes
}

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "缺少命令：$Name"
    }
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string]$Text)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))
        return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally { $algorithm.Dispose() }
}

function Test-ExactJsonInteger {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory = $true)][long]$Expected
    )
    return (($Value -is [int32] -or $Value -is [int64]) -and [long]$Value -eq $Expected)
}

function Assert-CleanCommit {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedCommit,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    $currentCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $currentCommit -ne $ExpectedCommit) {
        throw "验证期间 HEAD 已变化，拒绝$Operation。"
    }
    $dirtyPaths = @(& git '-c' 'core.quotepath=false' 'status' '--porcelain=v1' '--untracked-files=all')
    if ($LASTEXITCODE -ne 0) { throw "无法读取 Git 工作区状态，拒绝$Operation。" }
    if ($dirtyPaths.Count -gt 0) {
        throw "工作区存在未提交修改，拒绝$Operation；发布包只能绑定到干净提交。"
    }
}

function Remove-DuplicateTestrunFiles {
    param(
        [Parameter(Mandatory = $true)][string]$ProductionRoot,
        [Parameter(Mandatory = $true)][string]$TestrunRoot,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $sharedPaths = [Collections.Generic.List[string]]::new()
    $testrunRootPrefix = $TestrunRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($testrunFile in Get-ChildItem -LiteralPath $TestrunRoot -Recurse -File) {
        if (-not $testrunFile.FullName.StartsWith($testrunRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "测试服产物不在预期根目录内：$($testrunFile.FullName)"
        }
        $relativePath = $testrunFile.FullName.Substring($testrunRootPrefix.Length).Replace('\', '/')
        $productionFile = Join-Path $ProductionRoot $relativePath
        if (-not (Test-Path -LiteralPath $productionFile -PathType Leaf)) { continue }
        if ((Get-Item -LiteralPath $productionFile).Length -ne $testrunFile.Length) { continue }
        $productionHash = (Get-FileHash -LiteralPath $productionFile -Algorithm SHA256).Hash
        $testrunHash = (Get-FileHash -LiteralPath $testrunFile.FullName -Algorithm SHA256).Hash
        if ($productionHash -ne $testrunHash) { continue }
        Remove-Item -LiteralPath $testrunFile.FullName -Force
        $sharedPaths.Add($relativePath)
    }
    $sharedPaths.Sort([StringComparer]::Ordinal)
    $manifestText = if ($sharedPaths.Count -eq 0) { "" } else { ($sharedPaths -join "`n") + "`n" }
    [IO.File]::WriteAllText($ManifestPath, $manifestText, [Text.UTF8Encoding]::new($false))
    Write-Host "[L12 验证] 测试前端复用正式前端静态文件：$($sharedPaths.Count) 个"
}

function Test-CachedArtifact {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)
    if (-not (Test-Path -LiteralPath $ManifestPath)) { return $false }
    try {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
        if (-not (Test-ExactJsonInteger -Value $manifest.schema -Expected 3)) { return $false }
        if ($manifest.commit -ne $commit) { return $false }
        if (-not (Test-ExactJsonInteger -Value $manifest.deploymentDrainProtocol -Expected 1) -or
            $manifest.requiresExternalStopPermit -isnot [bool] -or -not $manifest.requiresExternalStopPermit -or
            $manifest.runtimeDataCompatibility -ne "l12-runtime-v1") { return $false }
        if ([string]$manifest.runtimeSourceCompatibility -notmatch '^[a-f0-9]{64}$') { return $false }
        if ($manifest.cardAssetsHash -ne $cardAssetsHash) { return $false }
        $cachedReleaseBase = if ($manifest.PSObject.Properties['releaseBaseCommit']) { [string]$manifest.releaseBaseCommit } else { "" }
        $cachedNotesHash = if ($manifest.PSObject.Properties['playerReleaseNotesSha256']) { [string]$manifest.playerReleaseNotesSha256 } else { "" }
        if ($cachedReleaseBase -ne $effectiveReleaseBaseCommit) { return $false }
        if ($cachedNotesHash -ne $playerReleaseNotesSha256) { return $false }
        foreach ($entry in @(
            @{ Path = $manifest.releaseArchive; Hash = $manifest.releaseSha256 },
            @{ Path = $manifest.cardAssetsArchive; Hash = $manifest.cardAssetsSha256 }
        )) {
            if (-not (Test-Path -LiteralPath $entry.Path)) { return $false }
            if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Hash) { return $false }
        }
        return $true
    }
    catch { return $false }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$cacheInitializer = Join-Path $PSScriptRoot "Initialize-L12BuildEnvironment.ps1"
$resolvedCacheRoot = & $cacheInitializer -CacheRoot $CacheRoot | Select-Object -Last 1
$originalLocation = Get-Location
$stagingDirectory = $null
$frontendWorkspaceDirectory = $null
$frontendBuildDirectory = $null
$evidenceDirectory = $null
$trxOmittedForBudget = $false
$verificationSucceeded = $false
$script:verificationTimings = [Collections.Generic.List[object]]::new()

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "dotnet", "npm", "tar", "python")) { Require-Command $commandName }
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
    $npmExecutable = if ($null -ne $npmCommand) { $npmCommand.Source } else { "npm" }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw "无法读取当前提交" }
    Assert-CleanCommit -ExpectedCommit $commit -Operation "验证或复用发布包"
    Invoke-External node ".\scripts\release-ledger.mjs" validate --repo $repoRoot
    Invoke-External node ".\scripts\test-release-status.mjs"
    $CardAssetDirectory = (Resolve-Path -LiteralPath $CardAssetDirectory).Path
    $cardAssetManifestPath = Join-Path $CardAssetDirectory "card-assets.manifest.json"
    if (-not (Test-Path -LiteralPath $cardAssetManifestPath -PathType Leaf)) { throw "优化卡图目录缺少发布清单：$cardAssetManifestPath" }
    $cardAssetManifest = Get-Content -LiteralPath $cardAssetManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($cardAssetManifest.schemaVersion -ne 3 -or -not $cardAssetManifest.complete -or $cardAssetManifest.cardCount -ne 366 -or
        $cardAssetManifest.playableCardCount -ne 324 -or $cardAssetManifest.presentationCardCount -ne 42 -or
        [string]$cardAssetManifest.assetVersion -notmatch '^[0-9a-f]{64}$') {
        throw "优化卡图发布清单必须为完整 schema v3（324 张可玩卡 + 42 张展示版本）内容寻址版本"
    }
    $cardAssetsHash = [string]$cardAssetManifest.assetVersion
    $catalogRoot = Join-Path $repoRoot "服务端WebSocket\TwelveLegions\Data"
    Invoke-External node ".\opcgpro-vue\scripts\audit-l12-card-cdn.mjs" --root $CardAssetDirectory --catalog-files "$catalogRoot\cards.s1.json;$catalogRoot\cards.s2.json;$catalogRoot\cards.st.json" --presentation-catalog "$catalogRoot\card-archive-assets.json"

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $artifactDirectory = Join-Path $OutputDirectory $commit
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $effectiveReleaseBaseCommit = ""
    $generatedPlayerRelease = Join-Path $artifactDirectory "generatedPlayerRelease.ts"
    if (-not [string]::IsNullOrWhiteSpace($ProductionBaseCommit)) {
        if ($ProductionBaseCommit -notmatch '^[0-9a-f]{40}$') { throw "正式服基线提交格式错误：$ProductionBaseCommit" }
        $effectiveReleaseBaseCommit = $ProductionBaseCommit.ToLowerInvariant()
        $releaseSummary = Join-Path $artifactDirectory "player-release-$effectiveReleaseBaseCommit-$commit.json"
        Invoke-External node ".\scripts\release-ledger.mjs" release --repo $repoRoot `
            --from $effectiveReleaseBaseCommit --to $commit --output $generatedPlayerRelease --summary $releaseSummary
    }
    else {
        Copy-Item -LiteralPath ".\opcgpro-vue\src\l12\site\generatedPlayerRelease.ts" -Destination $generatedPlayerRelease -Force
    }
    $playerReleaseNotesSha256 = (Get-FileHash -LiteralPath $generatedPlayerRelease -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestPath = Join-Path $artifactDirectory "l12-release-$commit.json"
    # A cached archive was fully checked at creation, but exception expiry is
    # time-dependent and must never be bypassed by an unchanged commit hash.
    Assert-PerformanceExceptionsCurrent -RepositoryRoot $repoRoot
    if (-not $Force -and (Test-CachedArtifact $manifestPath)) {
        Assert-CleanCommit -ExpectedCommit $commit -Operation "复用发布包"
        Write-Host "[L12 验证] 复用已验证提交产物：$commit"
        Write-Output $manifestPath
        exit 0
    }

    $evidenceRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) "verification-evidence"
    $existingEvidenceBytes = Get-VerificationEvidenceBytes -EvidenceRoot $evidenceRoot
    $trxOmittedForBudget = $existingEvidenceBytes -ge $EvidenceBudgetBytes
    if ($trxOmittedForBudget) {
        Write-Warning "验证证据已有 $existingEvidenceBytes 字节，达到 $EvidenceBudgetBytes 字节预算；本轮继续完整门禁，但不新增可选逐用例 TRX。请人工治理 $evidenceRoot；失败和 PINNED 证据不会自动删除。"
    }
    $runId = "{0}-{1}" -f [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ"), [Guid]::NewGuid().ToString('N')
    $evidenceDirectory = Join-Path (Join-Path $evidenceRoot $commit) $runId
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    Write-Host "[L12 验证] 本次阶段计时与 TRX 证据：$evidenceDirectory"

    Write-Host "[L12 验证] 运行 L12 规则测试..."
    # Several collections intentionally exercise independent SQLite lifecycles and
    # Windows junctions. Keep the release gate serial so their process-wide pools
    # and temporary paths cannot race during teardown.
    $ruleTrxArguments = if ($trxOmittedForBudget) { @() } else { @("--logger", "trx;LogFileName=rules.trx", "--results-directory", $evidenceDirectory) }
    Invoke-TimedExternal "rules" dotnet test ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj" --configuration Release `
        @ruleTrxArguments '--' 'xUnit.ParallelizeTestCollections=false'
    Write-Host "[L12 验证] 运行平台持久化测试..."
    # 退役邮箱测试由项目文件显式排除；有效账号与隐私测试仍在平台套件中全量运行。
    $platformTrxArguments = if ($trxOmittedForBudget) { @() } else { @("--logger", "trx;LogFileName=platform.trx", "--results-directory", $evidenceDirectory) }
    Invoke-TimedExternal "platform" dotnet test ".\TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj" --configuration Release `
        @platformTrxArguments '--' 'xUnit.ParallelizeTestCollections=false'
    Invoke-TimedExternal "deployment-drain-consumer" python -B ".\scripts\test-l12-deployment-drain-consumer.py"

    Write-Host "[L12 验证] 在隔离目录安装锁定依赖并构建前端..."
    $frontendSourceRoot = Join-Path $repoRoot "opcgpro-vue"
    $frontendWorkspaceDirectory = Join-Path $artifactDirectory "frontend-work-$([Guid]::NewGuid().ToString('N'))"
    $frontendBuildDirectory = Join-Path $frontendWorkspaceDirectory "opcgpro-vue"
    New-Item -ItemType Directory -Path $frontendBuildDirectory -Force | Out-Null
    $robocopy = Get-Command "robocopy.exe" -ErrorAction SilentlyContinue
    if ($null -ne $robocopy) {
        & $robocopy.Source $frontendSourceRoot $frontendBuildDirectory /E `
            /XD (Join-Path $frontendSourceRoot "node_modules") (Join-Path $frontendSourceRoot "dist") (Join-Path $frontendSourceRoot "dist-testrun") (Join-Path $frontendSourceRoot "public\cards") `
            /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "复制隔离前端构建目录失败（robocopy 退出码 $LASTEXITCODE）" }
    }
    else {
        throw "Windows 发布验证缺少 robocopy，无法安全隔离开发中的 node_modules"
    }

    # 前端契约检查会读取仓库根级的网络冒烟脚本、服务端入口和发布脚本。
    # 隔离工作区必须保留相同的相对目录结构，避免构建依赖开发工作树。
    $contractFiles = @(
        @{ Source = ".github\workflows\verify-release.yml"; Target = ".github\workflows\verify-release.yml" },
        @{ Source = "ops\performance-budgets.json"; Target = "ops\performance-budgets.json" },
        @{ Source = "ops\performance-exceptions.json"; Target = "ops\performance-exceptions.json" },
        @{ Source = "scripts\verify-l12-change.ps1"; Target = "scripts\verify-l12-change.ps1" },
        @{ Source = "scripts\release-ledger.mjs"; Target = "scripts\release-ledger.mjs" },
        @{ Source = "scripts\ws-smoke.mjs"; Target = "scripts\ws-smoke.mjs" },
        @{ Source = "服务端WebSocket\TwelveLegions\L12WebSocketServer.cs"; Target = "服务端WebSocket\TwelveLegions\L12WebSocketServer.cs" },
        @{ Source = "ops\windows\Initialize-L12BuildEnvironment.ps1"; Target = "ops\windows\Initialize-L12BuildEnvironment.ps1" },
        @{ Source = "ops\windows\verify-l12.ps1"; Target = "ops\windows\verify-l12.ps1" },
        @{ Source = "ops\windows\verify-l12-testrun-performance.ps1"; Target = "ops\windows\verify-l12-testrun-performance.ps1" },
        @{ Source = "ops\windows\deploy-l12.ps1"; Target = "ops\windows\deploy-l12.ps1" },
        @{ Source = "ops\windows\L12DeployTarget.ps1"; Target = "ops\windows\L12DeployTarget.ps1" },
        @{ Source = "ops\server\deploy-l12-release.sh"; Target = "ops\server\deploy-l12-release.sh" },
        @{ Source = "ops\server\legion12-testrun.nginx"; Target = "ops\server\legion12-testrun.nginx" },
        @{ Source = "ops\server\legion12-testrun-http.nginx"; Target = "ops\server\legion12-testrun-http.nginx" },
        @{ Source = "ops\server\legion12-testrun-path.nginx"; Target = "ops\server\legion12-testrun-path.nginx" },
        @{ Source = "ops\server\nginx-l12-share-pages.conf"; Target = "ops\server\nginx-l12-share-pages.conf" },
        @{ Source = "ops\server\activate-l12-share-pages.sh"; Target = "ops\server\activate-l12-share-pages.sh" },
        @{ Source = "ops\server\activate-l12-web-assets.sh"; Target = "ops\server\activate-l12-web-assets.sh" },
        @{ Source = "ops\server\nginx-l12-web-assets.conf"; Target = "ops\server\nginx-l12-web-assets.conf" },
        @{ Source = "ops\server\nginx-l12-card-assets.conf"; Target = "ops\server\nginx-l12-card-assets.conf" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\cards.s1.json"; Target = "服务端WebSocket\TwelveLegions\Data\cards.s1.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\cards.s2.json"; Target = "服务端WebSocket\TwelveLegions\Data\cards.s2.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\cards.st.json"; Target = "服务端WebSocket\TwelveLegions\Data\cards.st.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\preset-decks.s1.json"; Target = "服务端WebSocket\TwelveLegions\Data\preset-decks.s1.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\preset-decks.s2.json"; Target = "服务端WebSocket\TwelveLegions\Data\preset-decks.s2.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\morale-identities.json"; Target = "服务端WebSocket\TwelveLegions\Data\morale-identities.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\card-product-inclusions.json"; Target = "服务端WebSocket\TwelveLegions\Data\card-product-inclusions.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\card-archive-assets.json"; Target = "服务端WebSocket\TwelveLegions\Data\card-archive-assets.json" },
        @{ Source = "TwelveLegions.Platform.Tests\PublicDeckMatchBindingTests.cs"; Target = "TwelveLegions.Platform.Tests\PublicDeckMatchBindingTests.cs" },
        @{ Source = "TwelveLegions.Platform.Tests\RankedAnalyticsRangeTests.cs"; Target = "TwelveLegions.Platform.Tests\RankedAnalyticsRangeTests.cs" },
        @{ Source = "ops\windows\Get-L12BugQueue.ps1"; Target = "ops\windows\Get-L12BugQueue.ps1" }
    )
    foreach ($contractFile in $contractFiles) {
        $targetPath = Join-Path $frontendWorkspaceDirectory $contractFile.Target
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $contractFile.Source) -Destination $targetPath -Force
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot "release-ledger") `
        -Destination (Join-Path $frontendWorkspaceDirectory "release-ledger") -Recurse -Force
    Copy-Item -LiteralPath $generatedPlayerRelease `
        -Destination (Join-Path $frontendBuildDirectory "src\l12\site\generatedPlayerRelease.ts") -Force
    # UI 的玩家文案门禁会全量扫描服务端 Prompt 协议值；隔离构建必须复制全部 C# 定义，
    # 否则门禁会因为缺文件失败，或只扫描局部文件而产生“缺失标签为 0”的假阳性。
    $isolatedServerSourceRoot = Join-Path $frontendWorkspaceDirectory "服务端WebSocket\TwelveLegions"
    New-Item -ItemType Directory -Path $isolatedServerSourceRoot -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "服务端WebSocket\TwelveLegions") -File -Filter "*.cs" |
        Copy-Item -Destination $isolatedServerSourceRoot -Force
    Push-Location $frontendBuildDirectory
    try {
        Invoke-TimedExternal "frontend-npm-ci" $npmExecutable ci --prefer-offline --no-audit
        $previousClientRelease = $env:VITE_APP_VERSION
        try {
            $env:VITE_APP_VERSION = $commit
            Invoke-TimedExternal "frontend-build" $npmExecutable run build
        }
        finally { $env:VITE_APP_VERSION = $previousClientRelease }
    }
    finally { Pop-Location }

    $stagingDirectory = Join-Path $artifactDirectory "staging-$([Guid]::NewGuid().ToString('N'))"
    $releaseRoot = Join-Path $stagingDirectory "release"
    $webRoot = Join-Path $releaseRoot "opcgpro-vue\dist"
    $testrunWebRoot = Join-Path $releaseRoot "opcgpro-vue\dist-testrun"
    $publishRoot = Join-Path $releaseRoot "publish"
    $scriptsRoot = Join-Path $releaseRoot "scripts"
    New-Item -ItemType Directory -Path $webRoot, $testrunWebRoot, $publishRoot, $scriptsRoot -Force | Out-Null

    Write-Host "[L12 验证] 生成服务器兼容的框架依赖发布产物..."
    Invoke-TimedExternal "server-restore" dotnet restore ".\服务端WebSocket\GrandUMIServer.csproj" --ignore-failed-sources
    Invoke-TimedExternal "server-publish" dotnet publish ".\服务端WebSocket\GrandUMIServer.csproj" `
        --configuration Release --self-contained false --output $publishRoot --no-restore
    $nativeRuntimesRoot = Join-Path $publishRoot "runtimes"
    if (-not (Test-Path -LiteralPath (Join-Path $nativeRuntimesRoot "linux-x64"))) {
        throw "发布产物缺少 linux-x64 原生依赖"
    }
    Get-ChildItem -LiteralPath $nativeRuntimesRoot -Directory |
        Where-Object Name -ne "linux-x64" |
        Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath (Join-Path $nativeRuntimesRoot "linux-x64") -Recurse -Filter "*.a" |
        Remove-Item -Force

    $backendGitTree = (& git rev-parse "${commit}:服务端WebSocket").Trim()
    if ($LASTEXITCODE -ne 0 -or $backendGitTree -notmatch '^[a-f0-9]{40}$') {
        throw "无法把 runtime 兼容证明绑定到已验证提交的后台 Git subtree。"
    }
    $rootBuildConfiguration = [Collections.Generic.List[object]]::new()
    foreach ($relativePath in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.Config')) {
        $configurationPath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) { continue }
        $configurationItem = Get-Item -LiteralPath $configurationPath -Force
        if ($configurationItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "根级构建/SDK 配置不是普通文件：$relativePath"
        }
        $rootBuildConfiguration.Add([ordered]@{
            path = $relativePath.Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    $publishDepsPath = Join-Path $publishRoot 'GrandUMIServer.deps.json'
    if (-not (Test-Path -LiteralPath $publishDepsPath -PathType Leaf) -or
        ((Get-Item -LiteralPath $publishDepsPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "实际 publish 缺少普通 GrandUMIServer.deps.json，无法生成 runtime 兼容证明。"
    }
    $runtimeCompatibilityEvidence = [ordered]@{
        schema = 1
        backendGitTree = $backendGitTree
        rootBuildConfiguration = @($rootBuildConfiguration)
        publishDepsSha256 = (Get-FileHash -LiteralPath $publishDepsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $runtimeSourceCompatibility = Get-TextSha256 `
        ($runtimeCompatibilityEvidence | ConvertTo-Json -Depth 6 -Compress)

    Write-Host "[L12 验证] 汇总前端运行产物（卡图单独缓存）..."
    $frontendDistRoot = (Resolve-Path (Join-Path $frontendBuildDirectory "dist")).Path
    $frontendCardsRoot = Join-Path $frontendDistRoot "cards"
    $frontendTestrunDistRoot = (Resolve-Path (Join-Path $frontendBuildDirectory "dist-testrun")).Path
    $frontendTestrunCardsRoot = Join-Path $frontendTestrunDistRoot "cards"
    $robocopy = Get-Command "robocopy.exe" -ErrorAction SilentlyContinue
    if ($null -ne $robocopy) {
        & $robocopy.Source $frontendDistRoot $webRoot /E /XD $frontendCardsRoot /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "复制前端产物失败（robocopy 退出码 $LASTEXITCODE）" }
        & $robocopy.Source $frontendTestrunDistRoot $testrunWebRoot /E /XD $frontendTestrunCardsRoot /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "复制测试服前端产物失败（robocopy 退出码 $LASTEXITCODE）" }
    }
    else {
        Copy-Item ".\opcgpro-vue\dist\*" $webRoot -Recurse -Force
        Remove-Item (Join-Path $webRoot "cards") -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item ".\opcgpro-vue\dist-testrun\*" $testrunWebRoot -Recurse -Force
        Remove-Item (Join-Path $testrunWebRoot "cards") -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath (Join-Path $webRoot "cards")) {
        throw "运行包错误包含卡图目录"
    }
    if (Test-Path -LiteralPath (Join-Path $webRoot "card-assets")) {
        throw "运行包错误包含优化卡图目录"
    }
    if (Test-Path -LiteralPath (Join-Path $testrunWebRoot "cards")) {
        throw "测试服运行包错误包含卡图目录"
    }
    if (Test-Path -LiteralPath (Join-Path $testrunWebRoot "card-assets")) {
        throw "测试服运行包错误包含优化卡图目录"
    }
    Remove-DuplicateTestrunFiles `
        -ProductionRoot $webRoot `
        -TestrunRoot $testrunWebRoot `
        -ManifestPath (Join-Path $releaseRoot "opcgpro-vue\testrun-shared-files.txt")
    Copy-Item ".\scripts\ws-smoke.mjs" (Join-Path $scriptsRoot "ws-smoke.mjs") -Force
    [IO.File]::WriteAllText((Join-Path $releaseRoot ".deployment-commit"), $commit, [Text.UTF8Encoding]::new($false))
    $deploymentCapability = [ordered]@{
        schema = 1
        protocolVersion = 1
        requiresExternalStopPermit = $true
        runtimeDataCompatibility = "l12-runtime-v1"
        runtimeSourceCompatibility = $runtimeSourceCompatibility
        commit = $commit
    } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText(
        (Join-Path $releaseRoot ".deployment-drain-capability.json"),
        $deploymentCapability + "`n",
        [Text.UTF8Encoding]::new($false))

    $releaseArchive = Join-Path $artifactDirectory "l12-release-$commit.tar.gz"
    if (Test-Path -LiteralPath $releaseArchive) { Remove-Item -LiteralPath $releaseArchive -Force }
    Invoke-TimedExternal "release-archive" tar -czf $releaseArchive -C $releaseRoot .
    if ((Get-Item -LiteralPath $releaseArchive).Length -gt 150MB) {
        throw "不含卡图的运行包异常大于 150MB，请检查发布内容是否混入多平台原生库或运行数据"
    }

    $releaseSha256 = (Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $cardAssetsArchive = Join-Path $OutputDirectory "l12-card-assets-$cardAssetsHash.tar.gz"
    if (-not (Test-Path -LiteralPath $cardAssetsArchive)) {
        Write-Host "[L12 验证] 首次生成内容寻址优化卡图包：$cardAssetsHash"
        Invoke-TimedExternal "card-assets-archive" tar -czf $cardAssetsArchive -C $CardAssetDirectory .
    }
    $cardAssetsSha256 = (Get-FileHash -LiteralPath $cardAssetsArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-CleanCommit -ExpectedCommit $commit -Operation "写入发布包清单"
    [ordered]@{
        schema = 3
        commit = $commit
        deploymentDrainProtocol = 1
        requiresExternalStopPermit = $true
        runtimeDataCompatibility = "l12-runtime-v1"
        runtimeSourceCompatibility = $runtimeSourceCompatibility
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        releaseArchive = $releaseArchive
        releaseSha256 = $releaseSha256
        cardAssetsHash = $cardAssetsHash
        cardAssetsArchive = $cardAssetsArchive
        cardAssetsSha256 = $cardAssetsSha256
        releaseBaseCommit = $effectiveReleaseBaseCommit
        playerReleaseNotesSha256 = $playerReleaseNotesSha256
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $verificationSucceeded = $true
    Write-Host "[L12 验证] 完整验证与发布包构建通过：$commit"
    Write-Output $manifestPath
}
finally {
    Set-Location $originalLocation
    if ($null -ne $evidenceDirectory -and (Test-Path -LiteralPath $evidenceDirectory -PathType Container)) {
        try {
            [ordered]@{
                schema = 1
                commit = $commit
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
                status = if ($verificationSucceeded) { "success" } else { "failure" }
                trxOmittedForBudget = $trxOmittedForBudget
                stages = @($script:verificationTimings)
            } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidenceDirectory "timings.json") -Encoding utf8
        }
        catch { Write-Warning "无法写入验证阶段计时证据：$($_.Exception.Message)" }
    }
    if ($null -ne $frontendWorkspaceDirectory -and (Test-Path -LiteralPath $frontendWorkspaceDirectory)) {
        $resolvedFrontendBuild = (Resolve-Path -LiteralPath $frontendWorkspaceDirectory).Path
        $resolvedOutputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
        if (-not $resolvedFrontendBuild.StartsWith($resolvedOutputRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝清理输出目录以外的前端构建目录：$resolvedFrontendBuild"
        }
        Remove-Item -LiteralPath $resolvedFrontendBuild -Recurse -Force
    }
    if ($null -ne $stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
