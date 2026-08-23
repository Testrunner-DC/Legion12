[CmdletBinding()]
param(
    [string]$OutputDirectory = $(if ($env:L12_DEPLOY_CACHE) { $env:L12_DEPLOY_CACHE } elseif (Test-Path "D:\GPT") { "D:\GPT\L12-deploy-artifacts" } else { Join-Path ([IO.Path]::GetTempPath()) "l12-deploy-artifacts" }),
    [string]$CacheRoot = "",
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

function Test-CachedArtifact {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)
    if (-not (Test-Path -LiteralPath $ManifestPath)) { return $false }
    try {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
        if ($manifest.commit -ne $commit) { return $false }
        foreach ($entry in @(
            @{ Path = $manifest.releaseArchive; Hash = $manifest.releaseSha256 },
            @{ Path = $manifest.cardsArchive; Hash = $manifest.cardsSha256 }
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
$frontendCardsLink = $null

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "dotnet", "npm", "tar")) { Require-Command $commandName }
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
    $npmExecutable = if ($null -ne $npmCommand) { $npmCommand.Source } else { "npm" }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw "无法读取当前提交" }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $artifactDirectory = Join-Path $OutputDirectory $commit
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $manifestPath = Join-Path $artifactDirectory "l12-release-$commit.json"
    if (-not $Force -and (Test-CachedArtifact $manifestPath)) {
        Write-Host "[L12 验证] 复用已验证提交产物：$commit"
        Write-Output $manifestPath
        exit 0
    }

    Write-Host "[L12 验证] 运行 L12 规则测试..."
    Invoke-External dotnet test ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj" --configuration Release
    Write-Host "[L12 验证] 运行平台持久化测试..."
    Invoke-External dotnet test ".\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj" --configuration Release --filter "FullyQualifiedName~PlatformStoreTests"

    Write-Host "[L12 验证] 在隔离目录安装锁定依赖并构建前端..."
    $frontendSourceRoot = Join-Path $repoRoot "opcgpro-vue"
    $frontendWorkspaceDirectory = Join-Path $artifactDirectory "frontend-work-$([Guid]::NewGuid().ToString('N'))"
    $frontendBuildDirectory = Join-Path $frontendWorkspaceDirectory "opcgpro-vue"
    New-Item -ItemType Directory -Path $frontendBuildDirectory -Force | Out-Null
    $robocopy = Get-Command "robocopy.exe" -ErrorAction SilentlyContinue
    if ($null -ne $robocopy) {
        & $robocopy.Source $frontendSourceRoot $frontendBuildDirectory /E `
            /XD (Join-Path $frontendSourceRoot "node_modules") (Join-Path $frontendSourceRoot "dist") (Join-Path $frontendSourceRoot "public\cards") `
            /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "复制隔离前端构建目录失败（robocopy 退出码 $LASTEXITCODE）" }
    }
    else {
        throw "Windows 发布验证缺少 robocopy，无法安全隔离开发中的 node_modules"
    }

    # 前端契约检查会读取仓库根级的网络冒烟脚本、服务端入口和发布脚本。
    # 隔离工作区必须保留相同的相对目录结构，避免构建依赖开发工作树。
    $contractFiles = @(
        @{ Source = "scripts\ws-smoke.mjs"; Target = "scripts\ws-smoke.mjs" },
        @{ Source = "服务端WebSocket\TwelveLegions\L12WebSocketServer.cs"; Target = "服务端WebSocket\TwelveLegions\L12WebSocketServer.cs" },
        @{ Source = "ops\windows\Initialize-L12BuildEnvironment.ps1"; Target = "ops\windows\Initialize-L12BuildEnvironment.ps1" },
        @{ Source = "ops\windows\verify-l12.ps1"; Target = "ops\windows\verify-l12.ps1" },
        @{ Source = "ops\windows\deploy-l12.ps1"; Target = "ops\windows\deploy-l12.ps1" }
    )
    foreach ($contractFile in $contractFiles) {
        $targetPath = Join-Path $frontendWorkspaceDirectory $contractFile.Target
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $contractFile.Source) -Destination $targetPath -Force
    }
    $frontendCardsLink = Join-Path $frontendBuildDirectory "public\cards"
    New-Item -ItemType Junction -Path $frontendCardsLink -Target (Join-Path $frontendSourceRoot "public\cards") | Out-Null
    Push-Location $frontendBuildDirectory
    try {
        Invoke-External $npmExecutable ci --prefer-offline --no-audit
        Invoke-External $npmExecutable run build
    }
    finally { Pop-Location }

    $stagingDirectory = Join-Path $artifactDirectory "staging-$([Guid]::NewGuid().ToString('N'))"
    $releaseRoot = Join-Path $stagingDirectory "release"
    $webRoot = Join-Path $releaseRoot "opcgpro-vue\dist"
    $publishRoot = Join-Path $releaseRoot "publish"
    $scriptsRoot = Join-Path $releaseRoot "scripts"
    New-Item -ItemType Directory -Path $webRoot, $publishRoot, $scriptsRoot -Force | Out-Null

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
    $robocopy = Get-Command "robocopy.exe" -ErrorAction SilentlyContinue
    if ($null -ne $robocopy) {
        & $robocopy.Source $frontendDistRoot $webRoot /E /XD $frontendCardsRoot /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "复制前端产物失败（robocopy 退出码 $LASTEXITCODE）" }
    }
    else {
        Copy-Item ".\opcgpro-vue\dist\*" $webRoot -Recurse -Force
        Remove-Item (Join-Path $webRoot "cards") -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath (Join-Path $webRoot "cards")) {
        throw "运行包错误包含卡图目录"
    }
    Copy-Item ".\scripts\ws-smoke.mjs" (Join-Path $scriptsRoot "ws-smoke.mjs") -Force
    [IO.File]::WriteAllText((Join-Path $releaseRoot ".deployment-commit"), $commit, [Text.UTF8Encoding]::new($false))

    $releaseArchive = Join-Path $artifactDirectory "l12-release-$commit.tar.gz"
    if (Test-Path -LiteralPath $releaseArchive) { Remove-Item -LiteralPath $releaseArchive -Force }
    Invoke-External tar -czf $releaseArchive -C $releaseRoot .
    if ((Get-Item -LiteralPath $releaseArchive).Length -gt 150MB) {
        throw "不含卡图的运行包异常大于 150MB，请检查发布内容是否混入多平台原生库或运行数据"
    }

    $cardsHash = (& git rev-parse "$commit`:opcgpro-vue/public/cards").Trim()
    if ($LASTEXITCODE -ne 0 -or $cardsHash -notmatch '^[0-9a-f]{40,64}$') { throw "无法读取卡图目录版本" }
    $cardsArchive = Join-Path $OutputDirectory "l12-cards-$cardsHash.tar.gz"
    if (-not (Test-Path -LiteralPath $cardsArchive)) {
        Write-Host "[L12 验证] 首次生成卡图缓存包：$cardsHash"
        Invoke-External tar -czf $cardsArchive -C ".\opcgpro-vue\public" cards
    }

    $releaseSha256 = (Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $cardsSha256 = (Get-FileHash -LiteralPath $cardsArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        schema = 1
        commit = $commit
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        releaseArchive = $releaseArchive
        releaseSha256 = $releaseSha256
        cardsHash = $cardsHash
        cardsArchive = $cardsArchive
        cardsSha256 = $cardsSha256
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-Host "[L12 验证] 完整验证与发布包构建通过：$commit"
    Write-Output $manifestPath
}
finally {
    Set-Location $originalLocation
    if ($null -ne $frontendCardsLink -and (Test-Path -LiteralPath $frontendCardsLink)) {
        [IO.Directory]::Delete($frontendCardsLink)
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
