[CmdletBinding()]
param(
    [string]$Server = "root@legion-12.com",
    [string]$KnownHostsFile = "",
    [string]$IdentityFile = "",
    [string]$ArtifactManifest = "",
    [string]$CacheRoot = "",
    [ValidateSet("/opt", "/www/legion12")]
    [string]$ServerArtifactRoot = "/www/legion12",
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
    $maxAttempts = if ($Executable -in @("ssh", "scp")) { 3 } else { 1 }
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        & $Executable @Arguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) { return }
        if ($exitCode -ne 255 -or $attempt -eq $maxAttempts) {
            throw "命令执行失败（退出码 $exitCode）：$Executable $($Arguments -join ' ')"
        }
        $delay = 5 * $attempt
        Write-Host "[L12 部署] SSH 连接暂时不可用，${delay} 秒后重试（$attempt/$maxAttempts）..."
        Start-Sleep -Seconds $delay
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

function Resolve-L12ProductionBaseCommit {
    try {
        $health = Invoke-RestMethod -Uri "https://legion-12.com/health" -Method Get -TimeoutSec 15
    }
    catch {
        throw "无法读取当前正式服版本，不能确定玩家更新日志区间：$($_.Exception.Message)"
    }
    $baseCommit = [string]$health.serverVersion
    if ($baseCommit -notmatch '^[0-9a-f]{40}$') {
        throw "正式服 /health 未返回完整 serverVersion，拒绝靠人工回忆生成更新日志。"
    }
    return $baseCommit.ToLowerInvariant()
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
$sharePageActivator = Join-Path $repoRoot "ops\server\activate-l12-share-pages.sh"
$sharePageSnippet = Join-Path $repoRoot "ops\server\nginx-l12-share-pages.conf"
$webAssetsActivator = Join-Path $repoRoot "ops\server\activate-l12-web-assets.sh"
$webAssetsSnippet = Join-Path $repoRoot "ops\server\nginx-l12-web-assets.conf"
$verifyScript = Join-Path $repoRoot "ops\windows\verify-l12.ps1"
$originalLocation = Get-Location
$toolBundle = ""

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "ssh", "scp", "ssh-keygen", "powershell", "tar")) { Require-Command $commandName }
    $sshOptions = @(Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer $Server `
        -KnownHostsFile $KnownHostsFile -IdentityFile $IdentityFile)
    $remoteHost = $productionEndpoint.Host
    $trustedProductionAlias = $productionEndpoint.TrustedHostKeyAlias
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
    $productionBaseCommit = Resolve-L12ProductionBaseCommit
    & git merge-base --is-ancestor $productionBaseCommit $commit
    if ($LASTEXITCODE -ne 0) {
        throw "当前正式服提交 $productionBaseCommit 不是待部署提交 $commit 的祖先；拒绝错误聚合更新日志。"
    }
    Write-Host "[L12 部署] 玩家更新日志区间：$productionBaseCommit -> $commit"

    if ([string]::IsNullOrWhiteSpace($ArtifactManifest)) {
        $arguments = @("-ExecutionPolicy", "Bypass", "-File", $verifyScript, "-CacheRoot", $resolvedCacheRoot,
            "-ProductionBaseCommit", $productionBaseCommit)
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
    $manifestReleaseBase = if ($manifest.PSObject.Properties['releaseBaseCommit']) { [string]$manifest.releaseBaseCommit } else { "" }
    $manifestNotesHash = if ($manifest.PSObject.Properties['playerReleaseNotesSha256']) { [string]$manifest.playerReleaseNotesSha256 } else { "" }
    if ($manifestReleaseBase -ne $productionBaseCommit) {
        throw "发布包的更新日志基线不是当前正式服提交；请重新生成发布包。"
    }
    if ($manifestNotesHash -notmatch '^[0-9a-f]{64}$') {
        throw "发布包缺少经过门禁生成的玩家更新日志摘要。"
    }
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
    $remoteToolDir = "/tmp/l12-deploy-tools-$commit"
    $remoteToolBundle = "/tmp/l12-deploy-tools-$commit.tar"
    $remoteBootstrap = "$remoteToolDir/ops/server/deploy-l12-release.sh"
    $remoteHealthVerifier = "$remoteToolDir/ops/server/verify-l12-health.mjs"
    $remoteSharePageActivator = "$remoteToolDir/ops/server/activate-l12-share-pages.sh"
    $remoteSharePageSnippet = "$remoteToolDir/ops/server/nginx-l12-share-pages.conf"
    $remoteWebAssetsActivator = "$remoteToolDir/ops/server/activate-l12-web-assets.sh"
    $remoteWebAssetsSnippet = "$remoteToolDir/ops/server/nginx-l12-web-assets.conf"
    $remoteRelease = "$incoming/l12-release-$commit.tar.gz"
    $remoteCardAssets = if ($hasCardAssets) { "$incoming/l12-card-assets-$cardAssetsHashValue.tar.gz" } else { "-" }
    Write-Host "[L12 部署] 上传并安装经过本地验证的发布工具..."
    $toolBundle = Join-Path $resolvedCacheRoot "temp\l12-deploy-tools-$commit-$([Guid]::NewGuid().ToString('N')).tar"
    Invoke-External tar -cf $toolBundle `
        "ops/server/deploy-l12-release.sh" `
        "ops/server/verify-l12-health.mjs" `
        "ops/server/activate-l12-share-pages.sh" `
        "ops/server/nginx-l12-share-pages.conf" `
        "ops/server/activate-l12-web-assets.sh" `
        "ops/server/nginx-l12-web-assets.conf"
    Invoke-External scp @sshOptions $toolBundle "${Server}:$remoteToolBundle"
    Invoke-External ssh @sshOptions $Server "install -d -m 0700 '$remoteToolDir' && tar -xf '$remoteToolBundle' -C '$remoteToolDir' && rm -f '$remoteToolBundle' && sed -i 's/\r$//' '$remoteBootstrap' '$remoteWebAssetsActivator' && install -m 0755 '$remoteBootstrap' /usr/local/sbin/deploy-legion12-release && install -d -m 0755 /usr/local/libexec && install -m 0755 '$remoteHealthVerifier' /usr/local/libexec/verify-legion12-health.mjs && chmod 0755 '$remoteWebAssetsActivator' && '$remoteWebAssetsActivator' '$remoteWebAssetsSnippet' && rm -f '$remoteBootstrap' '$remoteHealthVerifier' '$remoteWebAssetsActivator' && /usr/local/sbin/deploy-legion12-release prepare-storage '$ServerArtifactRoot'"

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
        $cardAssetsProbeExitCode = 255
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            & ssh @sshOptions $Server $cardAssetsProbe
            $cardAssetsProbeExitCode = $LASTEXITCODE
            if ($cardAssetsProbeExitCode -ne 255) { break }
            if ($attempt -lt 3) {
                $delay = 5 * $attempt
                Write-Host "[L12 部署] 卡图缓存探测连接暂时不可用，${delay} 秒后重试（$attempt/3）..."
                Start-Sleep -Seconds $delay
            }
        }
        if ($cardAssetsProbeExitCode -ne 0 -and $cardAssetsProbeExitCode -ne 1) {
            throw "卡图缓存探测连接失败（退出码 $cardAssetsProbeExitCode），拒绝把连接故障误判为缓存缺失并重复上传卡图。"
        }
        $cardAssetsCached = $cardAssetsProbeExitCode -eq 0
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
    if ($DryRun) {
        Invoke-External ssh @sshOptions $Server "/usr/local/sbin/deploy-legion12-release $mode $commit $($manifest.releaseSha256) $remoteRelease - - - $cardAssetsHash $cardAssetsSha $cardAssetsPath '$ServerArtifactRoot' && rm -f '$remoteSharePageActivator' '$remoteSharePageSnippet' '$remoteWebAssetsActivator' '$remoteWebAssetsSnippet' && rmdir '$remoteToolDir/ops/server' '$remoteToolDir/ops' '$remoteToolDir'"
        Write-Host "[L12 部署] 干运行成功，线上版本未改变。"
    }
    else {
        Write-Host "[L12 部署] 启用主页与资讯分享信息路由..."
        Invoke-External ssh @sshOptions $Server "/usr/local/sbin/deploy-legion12-release $mode $commit $($manifest.releaseSha256) $remoteRelease - - - $cardAssetsHash $cardAssetsSha $cardAssetsPath '$ServerArtifactRoot' && sed -i 's/\r$//' '$remoteSharePageActivator' && chmod 0755 '$remoteSharePageActivator' && '$remoteSharePageActivator' '$remoteSharePageSnippet' && rm -f '$remoteSharePageActivator' && rmdir '$remoteToolDir/ops/server' '$remoteToolDir/ops' '$remoteToolDir' && curl -fsS --connect-timeout 5 --max-time 10 https://legion-12.com/ | grep -Fq 'og:title'"
        Write-Host "[L12 部署] 发布成功：https://legion-12.com/"
    }
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($toolBundle) -and (Test-Path -LiteralPath $toolBundle -PathType Leaf)) {
        Remove-Item -LiteralPath $toolBundle -Force
    }
    Set-Location $originalLocation
}
