[CmdletBinding()]
param(
    [string]$Server = "root@legion-12.com",
    [string]$ArtifactManifest = "",
    [string]$CacheRoot = "",
    [switch]$DryRun,
    [switch]$AllowVerifiedWorktree,
    [switch]$ForceVerification
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
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "缺少命令：$Name" }
}

function Invoke-GitFetchWithRetry {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        & git fetch --prune origin main
        if ($LASTEXITCODE -eq 0) { return }
        if ($attempt -eq 3) { throw "GitHub main 连续 3 次同步失败" }
        $delay = 3 * $attempt
        Write-Host "[L12 部署] GitHub 连接失败，${delay} 秒后重试（$attempt/3）..."
        Start-Sleep -Seconds $delay
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$cacheInitializer = Join-Path $PSScriptRoot "Initialize-L12BuildEnvironment.ps1"
$resolvedCacheRoot = & $cacheInitializer -CacheRoot $CacheRoot | Select-Object -Last 1
$serverScript = Join-Path $repoRoot "ops\server\deploy-l12-release.sh"
$verifyScript = Join-Path $repoRoot "ops\windows\verify-l12.ps1"
$originalLocation = Get-Location

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "ssh", "scp", "powershell")) { Require-Command $commandName }

    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or ($branch -ne "main" -and -not $AllowVerifiedWorktree)) {
        throw "只能从 main 分支部署，当前分支：$branch"
    }
    if ($branch -ne "main") {
        Write-Host "[L12 部署] 使用隔离验证工作区：$branch；仍会强制校验 HEAD 与 origin/main 完全一致。"
    }
    if (& git status --porcelain) { throw "工作区存在未提交修改，请先提交或妥善处理后再部署。" }

    Write-Host "[L12 部署] 同步并核对 GitHub main..."
    Invoke-GitFetchWithRetry
    $commit = (& git rev-parse HEAD).Trim()
    $remoteCommit = (& git rev-parse origin/main).Trim()
    if ($commit -ne $remoteCommit) { throw "本地提交与 origin/main 不一致，拒绝部署。" }

    if ([string]::IsNullOrWhiteSpace($ArtifactManifest)) {
        $arguments = @("-ExecutionPolicy", "Bypass", "-File", $verifyScript, "-CacheRoot", $resolvedCacheRoot)
        if ($ForceVerification) { $arguments += "-Force" }
        $verificationOutput = & powershell @arguments
        if ($LASTEXITCODE -ne 0) { throw "本地完整验证或发布包生成失败" }
        $ArtifactManifest = [string]($verificationOutput | Select-Object -Last 1)
    }
    $ArtifactManifest = (Resolve-Path -LiteralPath $ArtifactManifest).Path
    $manifestDirectory = Split-Path -Parent $ArtifactManifest
    $manifest = Get-Content -LiteralPath $ArtifactManifest -Raw | ConvertFrom-Json
    if ($manifest.commit -ne $commit) { throw "发布包提交与当前 main 不一致" }
    $releaseArchive = if ([IO.Path]::IsPathRooted([string]$manifest.releaseArchive)) {
        [string]$manifest.releaseArchive
    } else { Join-Path $manifestDirectory ([string]$manifest.releaseArchive) }
    if (-not (Test-Path -LiteralPath $releaseArchive)) { throw "运行包不存在：$releaseArchive" }
    if ((Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.releaseSha256) {
        throw "运行包校验失败"
    }
    $cardsArchive = ""
    if (-not [string]::IsNullOrWhiteSpace([string]$manifest.cardsArchive)) {
        $cardsArchive = if ([IO.Path]::IsPathRooted([string]$manifest.cardsArchive)) {
            [string]$manifest.cardsArchive
        } else { Join-Path $manifestDirectory ([string]$manifest.cardsArchive) }
        if ((Test-Path -LiteralPath $cardsArchive) -and -not [string]::IsNullOrWhiteSpace([string]$manifest.cardsSha256)) {
            if ((Get-FileHash -LiteralPath $cardsArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.cardsSha256) {
                throw "卡图包校验失败"
            }
        }
    }
    $cardAssetsHashValue = if ($manifest.PSObject.Properties['cardAssetsHash']) { [string]$manifest.cardAssetsHash } else { "" }
    $cardAssetsArchiveValue = if ($manifest.PSObject.Properties['cardAssetsArchive']) { [string]$manifest.cardAssetsArchive } else { "" }
    $cardAssetsSha256Value = if ($manifest.PSObject.Properties['cardAssetsSha256']) { [string]$manifest.cardAssetsSha256 } else { "" }
    $cardAssetsArchive = ""
    $hasCardAssets = -not [string]::IsNullOrWhiteSpace($cardAssetsHashValue) -and
        -not [string]::IsNullOrWhiteSpace($cardAssetsArchiveValue) -and
        -not [string]::IsNullOrWhiteSpace($cardAssetsSha256Value)
    if ($hasCardAssets) {
        if ($cardAssetsHashValue -notmatch '^[0-9a-f]{64}$') { throw "优化卡图版本格式错误" }
        $cardAssetsArchive = if ([IO.Path]::IsPathRooted($cardAssetsArchiveValue)) {
            $cardAssetsArchiveValue
        } else { Join-Path $manifestDirectory $cardAssetsArchiveValue }
        if (-not (Test-Path -LiteralPath $cardAssetsArchive -PathType Leaf)) { throw "优化卡图包不存在：$cardAssetsArchive" }
        if ((Get-FileHash -LiteralPath $cardAssetsArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $cardAssetsSha256Value) {
            throw "优化卡图包校验失败"
        }
    }

    $incoming = "/opt/legion12-deployment/incoming"
    $remoteBootstrap = "/tmp/deploy-l12-release-$commit.sh"
    $remoteRelease = "$incoming/l12-release-$commit.tar.gz"
    $remoteCards = "$incoming/l12-cards-$($manifest.cardsHash).tar.gz"
    $remoteCardAssets = if ($hasCardAssets) { "$incoming/l12-card-assets-$cardAssetsHashValue.tar.gz" } else { "-" }
    Write-Host "[L12 部署] 上传发布工具与预构建运行包..."
    Invoke-External ssh $Server "mkdir -p '$incoming'"
    Invoke-External scp $serverScript "${Server}:$remoteBootstrap"
    Invoke-External scp $releaseArchive "${Server}:$remoteRelease"

    & ssh $Server "test -d '/opt/legion12-static/cards/$($manifest.cardsHash)'"
    $cardsCached = $LASTEXITCODE -eq 0
    if ($cardsCached) {
        Write-Host "[L12 部署] 服务器复用卡图缓存：$($manifest.cardsHash)"
        $cardsSha = "-"
        $cardsPath = "-"
    }
    else {
        Write-Host "[L12 部署] 卡图版本变化，上传一次性缓存包..."
        if ([string]::IsNullOrWhiteSpace($cardsArchive) -or -not (Test-Path -LiteralPath $cardsArchive)) {
            Require-Command "tar"
            $localCardsHash = (& git rev-parse "$commit`:opcgpro-vue/public/cards").Trim()
            if ($localCardsHash -ne $manifest.cardsHash) { throw "当前卡图目录与发布清单不一致" }
            $cardsArchive = Join-Path $manifestDirectory "l12-cards-$($manifest.cardsHash).tar.gz"
            Invoke-External tar -czf $cardsArchive -C ".\opcgpro-vue\public" cards
        }
        $cardsSha = (Get-FileHash -LiteralPath $cardsArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        Invoke-External scp $cardsArchive "${Server}:$remoteCards"
        $cardsPath = $remoteCards
    }

    $cardAssetsSha = "-"
    $cardAssetsPath = "-"
    $cardAssetsHash = "-"
    if ($hasCardAssets) {
        $cardAssetsHash = $cardAssetsHashValue
        & ssh $Server "test -d '/opt/legion12-static/card-assets/$cardAssetsHash'"
        $cardAssetsCached = $LASTEXITCODE -eq 0
        if ($cardAssetsCached) {
            Write-Host "[L12 部署] 服务器复用优化卡图缓存：$cardAssetsHash"
        }
        else {
            Write-Host "[L12 部署] 上传内容寻址优化卡图包（二进制完整后才切换 release manifest）..."
            $cardAssetsSha = $cardAssetsSha256Value
            Invoke-External scp $cardAssetsArchive "${Server}:$remoteCardAssets"
            $cardAssetsPath = $remoteCardAssets
        }
    }
    else {
        Write-Warning "发布清单没有优化卡图包；保留旧 imageUrl 降级链，仅用于旧发布产物兼容。"
    }

    Invoke-External ssh $Server "sed -i 's/\r$//' '$remoteBootstrap' && install -m 0755 '$remoteBootstrap' /usr/local/sbin/deploy-legion12-release && rm -f '$remoteBootstrap'"
    $mode = if ($DryRun) { "dry-run" } else { "deploy" }
    Write-Host "[L12 部署] 服务器执行快速 $mode（不重复构建和全量测试）..."
    Invoke-External ssh $Server "/usr/local/sbin/deploy-legion12-release $mode $commit $($manifest.releaseSha256) $remoteRelease $($manifest.cardsHash) $cardsSha $cardsPath $cardAssetsHash $cardAssetsSha $cardAssetsPath"

    if ($DryRun) { Write-Host "[L12 部署] 干运行成功，线上版本未改变。" }
    else { Write-Host "[L12 部署] 发布成功：https://legion-12.com/" }
}
finally { Set-Location $originalLocation }
