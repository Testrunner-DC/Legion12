[CmdletBinding()]
param(
    [string]$OutputDirectory = $(if ($env:L12_DEPLOY_CACHE) { $env:L12_DEPLOY_CACHE } elseif (Test-Path "D:\GPT\Legion12") { "D:\GPT\Legion12\artifacts\deploy" } else { Join-Path ([IO.Path]::GetTempPath()) "l12-deploy-artifacts" }),
    [string]$CacheRoot = "",
    [string]$CardAssetDirectory = $(if ($env:L12_CARD_ASSET_ROOT) { $env:L12_CARD_ASSET_ROOT } else { "D:\L12-assets\published\current" }),
    [switch]$Force
)

$ErrorActionPreference = "Stop"
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

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "缺少命令：$Name"
    }
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
        if ($manifest.schema -ne 3) { return $false }
        if ($manifest.commit -ne $commit) { return $false }
        if ($manifest.cardAssetsHash -ne $cardAssetsHash) { return $false }
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

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "dotnet", "npm", "tar")) { Require-Command $commandName }
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
    $npmExecutable = if ($null -ne $npmCommand) { $npmCommand.Source } else { "npm" }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw "无法读取当前提交" }
    Assert-CleanCommit -ExpectedCommit $commit -Operation "验证或复用发布包"
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
    $manifestPath = Join-Path $artifactDirectory "l12-release-$commit.json"
    if (-not $Force -and (Test-CachedArtifact $manifestPath)) {
        Assert-CleanCommit -ExpectedCommit $commit -Operation "复用发布包"
        Write-Host "[L12 验证] 复用已验证提交产物：$commit"
        Write-Output $manifestPath
        exit 0
    }

    Write-Host "[L12 验证] 运行 L12 规则测试..."
    # Several collections intentionally exercise independent SQLite lifecycles and
    # Windows junctions. Keep the release gate serial so their process-wide pools
    # and temporary paths cannot race during teardown.
    Invoke-External dotnet test ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj" --configuration Release '--' 'xUnit.ParallelizeTestCollections=false'
    Write-Host "[L12 验证] 运行平台持久化测试..."
    # 邮箱能力已经退出产品范围；旧测试保留用于历史追溯，不再阻塞当前产品发布。
    Invoke-External dotnet test ".\TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj" --configuration Release --filter 'FullyQualifiedName!~EmailAuthAndAccountLifecycleTests' '--' 'xUnit.ParallelizeTestCollections=false'

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
        @{ Source = "服务端WebSocket\TwelveLegions\Data\morale-identities.json"; Target = "服务端WebSocket\TwelveLegions\Data\morale-identities.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\card-product-inclusions.json"; Target = "服务端WebSocket\TwelveLegions\Data\card-product-inclusions.json" },
        @{ Source = "服务端WebSocket\TwelveLegions\Data\card-archive-assets.json"; Target = "服务端WebSocket\TwelveLegions\Data\card-archive-assets.json" },
        @{ Source = "TwelveLegions.Platform.Tests\PublicDeckMatchBindingTests.cs"; Target = "TwelveLegions.Platform.Tests\PublicDeckMatchBindingTests.cs" },
        @{ Source = "ops\windows\Get-L12BugQueue.ps1"; Target = "ops\windows\Get-L12BugQueue.ps1" }
    )
    foreach ($contractFile in $contractFiles) {
        $targetPath = Join-Path $frontendWorkspaceDirectory $contractFile.Target
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $contractFile.Source) -Destination $targetPath -Force
    }
    # UI 的玩家文案门禁会全量扫描服务端 Prompt 协议值；隔离构建必须复制全部 C# 定义，
    # 否则门禁会因为缺文件失败，或只扫描局部文件而产生“缺失标签为 0”的假阳性。
    $isolatedServerSourceRoot = Join-Path $frontendWorkspaceDirectory "服务端WebSocket\TwelveLegions"
    New-Item -ItemType Directory -Path $isolatedServerSourceRoot -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "服务端WebSocket\TwelveLegions") -File -Filter "*.cs" |
        Copy-Item -Destination $isolatedServerSourceRoot -Force
    Push-Location $frontendBuildDirectory
    try {
        Invoke-External $npmExecutable ci --prefer-offline --no-audit
        $previousClientRelease = $env:VITE_APP_VERSION
        try {
            $env:VITE_APP_VERSION = $commit
            Invoke-External $npmExecutable run build
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
    Invoke-External dotnet restore ".\服务端WebSocket\GrandUMIServer.csproj" --ignore-failed-sources
    Invoke-External dotnet publish ".\服务端WebSocket\GrandUMIServer.csproj" --configuration Release --self-contained false --output $publishRoot --no-restore
    $nativeRuntimesRoot = Join-Path $publishRoot "runtimes"
    if (-not (Test-Path -LiteralPath (Join-Path $nativeRuntimesRoot "linux-x64"))) {
        throw "发布产物缺少 linux-x64 原生依赖"
    }
    Get-ChildItem -LiteralPath $nativeRuntimesRoot -Directory |
        Where-Object Name -ne "linux-x64" |
        Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath (Join-Path $nativeRuntimesRoot "linux-x64") -Recurse -Filter "*.a" |
        Remove-Item -Force

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

    $releaseArchive = Join-Path $artifactDirectory "l12-release-$commit.tar.gz"
    if (Test-Path -LiteralPath $releaseArchive) { Remove-Item -LiteralPath $releaseArchive -Force }
    Invoke-External tar -czf $releaseArchive -C $releaseRoot .
    if ((Get-Item -LiteralPath $releaseArchive).Length -gt 150MB) {
        throw "不含卡图的运行包异常大于 150MB，请检查发布内容是否混入多平台原生库或运行数据"
    }

    $releaseSha256 = (Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $cardAssetsArchive = Join-Path $OutputDirectory "l12-card-assets-$cardAssetsHash.tar.gz"
    if (-not (Test-Path -LiteralPath $cardAssetsArchive)) {
        Write-Host "[L12 验证] 首次生成内容寻址优化卡图包：$cardAssetsHash"
        Invoke-External tar -czf $cardAssetsArchive -C $CardAssetDirectory .
    }
    $cardAssetsSha256 = (Get-FileHash -LiteralPath $cardAssetsArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-CleanCommit -ExpectedCommit $commit -Operation "写入发布包清单"
    [ordered]@{
        schema = 3
        commit = $commit
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        releaseArchive = $releaseArchive
        releaseSha256 = $releaseSha256
        cardAssetsHash = $cardAssetsHash
        cardAssetsArchive = $cardAssetsArchive
        cardAssetsSha256 = $cardAssetsSha256
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-Host "[L12 验证] 完整验证与发布包构建通过：$commit"
    Write-Output $manifestPath
}
finally {
    Set-Location $originalLocation
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
