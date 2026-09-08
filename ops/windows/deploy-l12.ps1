[CmdletBinding()]
param(
    [string]$Server = "root@legion-12.com",
    [string]$KnownHostsFile = "",
    [string]$IdentityFile = "",
    [string]$ArtifactManifest = "",
    [string]$CacheRoot = "",
    [ValidateSet("/opt", "/www/legion12")]
    [string]$ServerArtifactRoot = "/opt",
    [switch]$DryRun,
    # 兼容旧调用；隔离工作树现在会自动通过 HEAD == origin/main 的强校验，
    # 不再需要调用者手动追加此参数。
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
$targetHelper = Join-Path $PSScriptRoot "L12DeployTarget.ps1"
. $targetHelper
# 在初始化缓存、Git 同步或任何 SSH/SCP 写入之前拒绝旧机及任意目标。
$productionEndpoint = Resolve-L12ProductionEndpoint -RemoteServer $Server
$Server = $productionEndpoint.Destination
$cacheInitializer = Join-Path $PSScriptRoot "Initialize-L12BuildEnvironment.ps1"
$resolvedCacheRoot = & $cacheInitializer -CacheRoot $CacheRoot | Select-Object -Last 1
$serverScript = Join-Path $repoRoot "ops\server\deploy-l12-release.sh"
$serverHealthVerifier = Join-Path $repoRoot "ops\server\verify-l12-health.mjs"
$verifyScript = Join-Path $repoRoot "ops\windows\verify-l12.ps1"
$originalLocation = Get-Location

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "ssh", "scp", "ssh-keygen", "powershell")) { Require-Command $commandName }
    $sshOptions = @(Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer $Server `
        -KnownHostsFile $KnownHostsFile -IdentityFile $IdentityFile)
    $remoteHost = $productionEndpoint.Host
    $trustedProductionAlias = "38.76.208.25"
    $knownHostsOption = $sshOptions | Where-Object { $_ -like "UserKnownHostsFile=*" } | Select-Object -First 1
    $knownHosts = if ($knownHostsOption) { $knownHostsOption.Substring("UserKnownHostsFile=".Length) } else { "" }
    $trustedAliasEntry = if ($knownHosts) { @(& ssh-keygen -F $trustedProductionAlias -f $knownHosts 2>$null) } else { @() }
    if ($sshOptions -notcontains "StrictHostKeyChecking=yes") {
        throw "生产连接没有启用严格主机密钥校验，拒绝部署。"
    }
    if ($sshOptions -notcontains "HostName=$trustedProductionAlias") {
        throw "生产连接没有固定到新服务器 IP，拒绝部署。"
    }
    if ($remoteHost -eq "legion-12.com" -and $trustedAliasEntry.Count -gt 0) {
        if ($sshOptions -notcontains "HostKeyAlias=$trustedProductionAlias") {
            throw "主域连接没有固定到新生产服务器主机指纹，拒绝部署。"
        }
    }
    elseif ($remoteHost -ne $trustedProductionAlias -or $trustedAliasEntry.Count -eq 0) {
        throw "生产目标或新服务器主机指纹在调用前发生变化，拒绝部署。"
    }

    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
        throw "无法确定当前 Git 分支，拒绝部署。"
    }
    if ($branch -ne "main") {
        Write-Host "[L12 部署] 自动使用隔离验证工作区：$branch；将强制校验 HEAD 与 origin/main 完全一致。"
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
        $verificationHost = Get-Command "pwsh" -ErrorAction SilentlyContinue
        if (-not $verificationHost) { $verificationHost = Get-Command "powershell" -ErrorAction Stop }
        Write-Host "[L12 部署] 使用验证宿主：$($verificationHost.Source)"
        $verificationOutput = & $verificationHost.Source @arguments
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

    $incoming = if ($ServerArtifactRoot -eq "/www/legion12") {
        "/www/legion12/incoming"
    }
    else {
        "/opt/legion12-deployment/incoming"
    }
    $remoteBootstrap = "/tmp/deploy-l12-release-$commit.sh"
    $remoteHealthVerifier = "/tmp/verify-l12-health-$commit.mjs"
    $remoteRelease = "$incoming/l12-release-$commit.tar.gz"
    $remoteCardAssets = if ($hasCardAssets) { "$incoming/l12-card-assets-$cardAssetsHashValue.tar.gz" } else { "-" }
    Write-Host "[L12 部署] 上传并安装经过本地验证的发布工具..."
    Invoke-External scp @sshOptions $serverScript "${Server}:$remoteBootstrap"
    Invoke-External scp @sshOptions $serverHealthVerifier "${Server}:$remoteHealthVerifier"
    Invoke-External ssh @sshOptions $Server "sed -i 's/\r$//' '$remoteBootstrap' && install -m 0755 '$remoteBootstrap' /usr/local/sbin/deploy-legion12-release && install -d -m 0755 /usr/local/libexec && install -m 0755 '$remoteHealthVerifier' /usr/local/libexec/verify-legion12-health.mjs && rm -f '$remoteBootstrap' '$remoteHealthVerifier'"
    Invoke-External ssh @sshOptions $Server "/usr/local/sbin/deploy-legion12-release prepare-storage '$ServerArtifactRoot'"

    Write-Host "[L12 部署] 上传预构建运行包..."
    Invoke-External scp @sshOptions $releaseArchive "${Server}:$remoteRelease"

    $cardAssetsSha = "-"
    $cardAssetsPath = "-"
    $cardAssetsHash = "-"
    if ($hasCardAssets) {
        $cardAssetsHash = $cardAssetsHashValue
        $cardAssetsProbe = if ($ServerArtifactRoot -eq "/www/legion12") {
            "if test -d '/www/legion12/card-assets/$cardAssetsHash' && test ! -L '/www/legion12/card-assets/$cardAssetsHash'; then exit 0; fi; test -d '/opt/legion12-static/card-assets/$cardAssetsHash' && test ! -L '/opt/legion12-static/card-assets/$cardAssetsHash'"
        }
        else {
            "test -d '/opt/legion12-static/card-assets/$cardAssetsHash' && test ! -L '/opt/legion12-static/card-assets/$cardAssetsHash'"
        }
        & ssh @sshOptions $Server $cardAssetsProbe
        $cardAssetsCached = $LASTEXITCODE -eq 0
        if ($cardAssetsCached) {
            Write-Host "[L12 部署] 服务器复用优化卡图缓存：$cardAssetsHash"
        }
        else {
            Write-Host "[L12 部署] 上传内容寻址优化卡图包（二进制完整后才切换 release manifest）..."
            $cardAssetsSha = $cardAssetsSha256Value
            Invoke-External scp @sshOptions $cardAssetsArchive "${Server}:$remoteCardAssets"
            $cardAssetsPath = $remoteCardAssets
        }
    }
    else { throw "发布清单缺少完整优化卡图包，拒绝退回旧卡图链路。" }

    $mode = if ($DryRun) { "dry-run" } else { "deploy" }
    Write-Host "[L12 部署] 服务器执行快速 $mode（不重复构建和全量测试）..."
    Invoke-External ssh @sshOptions $Server "/usr/local/sbin/deploy-legion12-release $mode $commit $($manifest.releaseSha256) $remoteRelease - - - $cardAssetsHash $cardAssetsSha $cardAssetsPath '$ServerArtifactRoot'"

    if ($DryRun) { Write-Host "[L12 部署] 干运行成功，线上版本未改变。" }
    else { Write-Host "[L12 部署] 发布成功：https://legion-12.com/" }
}
finally { Set-Location $originalLocation }
