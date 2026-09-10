[CmdletBinding()]
param([string]$FixtureBase = "")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) { throw $Message }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
    )
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertTo-MsysPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path).Replace('\', '/')
    if ($full -match '^([A-Za-z]):/(.*)$') {
        return "/$($matches[1].ToLowerInvariant())/$($matches[2])"
    }
    return $full
}

function Get-BashPath {
    $bash = Get-Command bash -ErrorAction SilentlyContinue
    if ($bash) { return $bash.Source }
    $git = Get-Command git -ErrorAction Stop
    $gitRoot = Split-Path (Split-Path $git.Source -Parent) -Parent
    $bundledShell = Join-Path $gitRoot "usr\bin\sh.exe"
    if (-not (Test-Path -LiteralPath $bundledShell -PathType Leaf)) {
        throw "找不到可用于服务器发布行为模拟的 Bash。"
    }
    return $bundledShell
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Arguments,
        [AllowEmptyString()][string]$StandardInput = ""
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    if ($StandardInput.Length -gt 0) { $process.StandardInput.Write($StandardInput) }
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; Output = "$stdout$stderr" }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$windowsDeploy = Join-Path $repoRoot "ops\windows\deploy-l12.ps1"
$targetHelper = Join-Path $repoRoot "ops\windows\L12DeployTarget.ps1"
$serverDeploy = Join-Path $repoRoot "ops\server\deploy-l12-release.sh"
$healthVerifier = Join-Path $repoRoot "ops\server\verify-l12-health.mjs"
$bashPath = Get-BashPath
$nodePath = (Get-Command node -ErrorAction Stop).Source
$powerShellPath = (Get-Command pwsh -ErrorAction SilentlyContinue)
if (-not $powerShellPath) { $powerShellPath = Get-Command powershell -ErrorAction Stop }

$fixtureBasePath = if ([string]::IsNullOrWhiteSpace($FixtureBase)) {
    [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
}
else {
    [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $FixtureBase -ErrorAction Stop).Path)
}
$fixtureBasePath = $fixtureBasePath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fixtureRoot = Join-Path $fixtureBasePath "l12-deploy-behavior-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

try {
    . $targetHelper

    foreach ($accepted in @("root@legion-12.com", "root@154.201.80.91")) {
        $endpoint = Resolve-L12ProductionEndpoint -RemoteServer $accepted
        Assert-True ($endpoint.TrustedHostKeyAlias -eq "154.201.80.91") "合法生产目标没有固定到新机主机密钥别名：$accepted"
    }
    foreach ($rejected in @(
        "root@38.76.208.25",
        "root@103.146.230.37",
        "root@example.com",
        "operator@legion-12.com",
        "root@legion-12.com:22",
        "-o@legion-12.com"
    )) {
        $wasRejected = $false
        try { Resolve-L12ProductionEndpoint -RemoteServer $rejected | Out-Null }
        catch { $wasRejected = $true }
        Assert-True $wasRejected "生产目标白名单错误接受：$rejected"
    }

    $invalidEntrypoint = Invoke-NativeCapture -Executable $powerShellPath.Source -Arguments @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $windowsDeploy,
        "-Server", "root@103.146.230.37", "-DryRun"
    )
    Assert-True ($invalidEntrypoint.ExitCode -ne 0) "Windows 正式部署入口接受了退役旧机。"
    Assert-True ($invalidEntrypoint.Output.Contains("拒绝向非当前生产节点部署")) "旧机拒绝没有发生在部署入口的前置目标门禁。"
    Assert-True (-not $invalidEntrypoint.Output.Contains("同步并核对 GitHub main")) "旧机目标在拒绝前已进入同步/远程阶段。"

    $sshFixture = Join-Path $fixtureRoot "ssh"
    $identityFixture = Join-Path $sshFixture "separate-identity"
    New-Item -ItemType Directory -Path $sshFixture, $identityFixture -Force | Out-Null
    $keyPath = Join-Path $identityFixture "host"
    $keygen = Invoke-NativeCapture -Executable (Get-Command ssh-keygen -ErrorAction Stop).Source -Arguments @(
        "-q", "-t", "ed25519", "-N", "", "-f", $keyPath
    )
    Assert-True ($keygen.ExitCode -eq 0) "SSH 主机指纹夹具生成失败：$($keygen.Output)"
    $publicKeyParts = (Get-Content -LiteralPath "$keyPath.pub" -Raw).Trim().Split(' ')
    $knownHosts = Join-Path $sshFixture "known_hosts"
    Write-Utf8NoBom $knownHosts "154.201.80.91 $($publicKeyParts[0]) $($publicKeyParts[1])`n"
    $sshOptions = @(Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer "root@legion-12.com" -KnownHostsFile $knownHosts)
    Assert-True ($sshOptions -contains "StrictHostKeyChecking=yes") "生产 SSH 没有严格主机密钥校验。"
    Assert-True ($sshOptions -contains "HostName=154.201.80.91") "生产 SSH 没有固定连接新机 IP。"
    Assert-True ($sshOptions -contains "HostKeyAlias=154.201.80.91") "生产主域没有固定使用新机 IP 指纹。"
    $explicitSshOptions = @(Resolve-L12SshOptions -RepositoryRoot $repoRoot `
        -RemoteServer "root@legion-12.com" -KnownHostsFile $knownHosts -IdentityFile $keyPath)
    $resolvedExplicitIdentity = (Resolve-Path -LiteralPath $keyPath).Path
    Assert-True ($explicitSshOptions -contains "IdentitiesOnly=yes") "显式 SSH identity 没有限制为唯一身份。"
    Assert-True ($explicitSshOptions -contains $resolvedExplicitIdentity) "显式 SSH identity 未进入独立参数数组。"
    $missingIdentityRejected = $false
    try {
        Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer "root@legion-12.com" `
            -KnownHostsFile $knownHosts -IdentityFile (Join-Path $identityFixture "missing") | Out-Null
    }
    catch { $missingIdentityRejected = $_.Exception.Message.Contains("identity 文件不存在") }
    Assert-True $missingIdentityRejected "不存在的显式 SSH identity 没有失败关闭。"
    $deployCommand = Get-Command -Name $windowsDeploy -ErrorAction Stop
    Assert-True ($deployCommand.Parameters.ContainsKey("KnownHostsFile")) "Windows 部署入口未公开 known_hosts 参数。"
    Assert-True ($deployCommand.Parameters.ContainsKey("IdentityFile")) "Windows 部署入口未公开 identity 参数。"
    Assert-True ($deployCommand.Parameters.ContainsKey("ServerArtifactRoot")) "Windows 部署入口未公开固定服务器制品根参数。"
    $artifactRootParameter = $deployCommand.Parameters["ServerArtifactRoot"]
    $artifactRootValidateSet = @($artifactRootParameter.Attributes |
        Where-Object { $_ -is [Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { $_.ValidValues })
    Assert-True ($artifactRootValidateSet.Count -eq 2) "服务器制品根参数不是精确双值白名单。"
    Assert-True ($artifactRootValidateSet -contains "/opt") "服务器制品根参数缺少默认 /opt。"
    Assert-True ($artifactRootValidateSet -contains "/www/legion12") "服务器制品根参数缺少固定 /www/legion12。"
    $emptyKnownHosts = Join-Path $sshFixture "empty_known_hosts"
    Write-Utf8NoBom $emptyKnownHosts ""
    $missingTrustRejected = $false
    try { Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer "root@legion-12.com" -KnownHostsFile $emptyKnownHosts | Out-Null }
    catch { $missingTrustRejected = $_.Exception.Message.Contains("缺少已人工核验的新生产服务器") }
    Assert-True $missingTrustRejected "缺少新机 IP 指纹时没有失败关闭。"

    $commitA = "a" * 40
    $commitB = "b" * 40
    $validHealth = "{`"status`":`"ok`",`"maintenance`":false,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}"
    $validResult = Invoke-NativeCapture -Executable $nodePath -Arguments @($healthVerifier, $commitA) -StandardInput $validHealth
    Assert-True ($validResult.ExitCode -eq 0) "精确健康身份被错误拒绝：$($validResult.Output)"
    $maintenanceHealth = "{`"status`":`"maintenance`",`"maintenance`":true,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}"
    $strictMaintenance = Invoke-NativeCapture -Executable $nodePath -Arguments @($healthVerifier, $commitA) -StandardInput $maintenanceHealth
    Assert-True ($strictMaintenance.ExitCode -ne 0) "默认严格 ok 校验错误接受了维护状态。"
    $deploymentMaintenance = Invoke-NativeCapture -Executable $nodePath `
        -Arguments @($healthVerifier, $commitA, "--allow-maintenance") -StandardInput $maintenanceHealth
    Assert-True ($deploymentMaintenance.ExitCode -eq 0) "部署维护模式错误拒绝了一致的维护状态：$($deploymentMaintenance.Output)"
    $deploymentOk = Invoke-NativeCapture -Executable $nodePath `
        -Arguments @($healthVerifier, $commitA, "--allow-maintenance") -StandardInput $validHealth
    Assert-True ($deploymentOk.ExitCode -eq 0) "部署维护模式错误拒绝了一致的 ok 状态：$($deploymentOk.Output)"
    $unknownHealthMode = Invoke-NativeCapture -Executable $nodePath `
        -Arguments @($healthVerifier, $commitA, "--unknown-mode") -StandardInput $validHealth
    Assert-True ($unknownHealthMode.ExitCode -eq 2) "未知健康校验模式没有按参数错误失败关闭。"
    foreach ($invalidHealth in @(
        "{`"status`":`"degraded`",`"maintenance`":false,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"unknown`",`"maintenance`":false,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"ok`",`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"ok`",`"maintenance`":false,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitB`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"ok`",`"maintenance`":false,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitB`"}",
        "{`"status`":`"ok`",`"maintenance`":false,`"service`":`"other`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "not-json"
    )) {
        $invalidResult = Invoke-NativeCapture -Executable $nodePath -Arguments @($healthVerifier, $commitA) -StandardInput $invalidHealth
        Assert-True ($invalidResult.ExitCode -ne 0) "健康身份校验器接受了错误响应：$invalidHealth"
    }
    foreach ($invalidDeploymentHealth in @(
        "{`"status`":`"maintenance`",`"maintenance`":false,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"ok`",`"maintenance`":true,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"degraded`",`"maintenance`":true,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"status`":`"maintenance`",`"maintenance`":true,`"service`":`"twelve-legions`",`"serverVersion`":`"$commitB`",`"engineVersion`":`"l12-engine/$commitB`"}"
    )) {
        $invalidDeploymentResult = Invoke-NativeCapture -Executable $nodePath `
            -Arguments @($healthVerifier, $commitA, "--allow-maintenance") -StandardInput $invalidDeploymentHealth
        Assert-True ($invalidDeploymentResult.ExitCode -ne 0) "部署健康校验器接受了错误或不一致响应：$invalidDeploymentHealth"
    }

    function New-FakeCommand {
        param([string]$Directory, [string]$Name, [string]$Body)
        $path = Join-Path $Directory $Name
        Write-Utf8NoBom $path "#!/usr/bin/env sh`n$Body"
        return $path
    }

    $serverScenarioCount = 0

    function Invoke-ServerScenario {
        param(
            [Parameter(Mandatory = $true)][string]$Name,
            [string]$LocalCommitOverride = "",
            [string]$PublicCommitOverride = "",
            [string]$HealthStatus = "ok",
            [string]$HealthMaintenance = "",
            [ValidateSet("deploy", "dry-run")][string]$Mode = "deploy",
            [string]$ArtifactRoot = "/opt",
            [string]$ExternalMountTarget = "",
            [string]$ExternalMountSource = "",
            [string]$ExternalMountOptions = "",
            [string]$ExternalFstabTarget = "",
            [long]$ExternalAvailableBytes = 0,
            [long]$BackupMaxBytes = 0,
            [switch]$FailBackup,
            [switch]$FailBackupValidation,
            [switch]$FailBackupSha,
            [switch]$WriteOnStart,
            [switch]$DisabledService,
            [switch]$PreexistingBlock,
            [ValidateSet("none", "opt-cache", "external-archive")][string]$CardAssetsScenario = "none",
            [switch]$ExternalMountSymlink,
            [ValidateSet("", "final", "partial", "sha", "sha-partial")][string]$ExistingBackupTarget = "",
            [ValidateSet("file", "symlink")][string]$ExistingBackupTargetKind = "file",
            [ValidateSet("", "directory", "symlink")][string]$ExistingStageTarget = "",
            [switch]$ExistingReleaseTarget,
            [switch]$ExternalCardTargetSymlink,
            [switch]$OmitArtifactRootArgument
        )

        $script:serverScenarioCount += 1

        $root = Join-Path $fixtureRoot "l12-deploy-behavior-$Name"
        $active = Join-Path $root "opt\legion12-test"
        $runtime = Join-Path $root "opt\legion12-runtime"
        $externalMount = Join-Path $root "www"
        if ($ExternalMountSymlink) {
            $externalMountBacking = Join-Path $root "www-target"
            New-Item -ItemType Directory -Path $externalMountBacking -Force | Out-Null
            New-Item -ItemType Junction -Path $externalMount -Target $externalMountBacking | Out-Null
        }
        else {
            New-Item -ItemType Directory -Path $externalMount -Force | Out-Null
        }
        $externalArtifactRoot = Join-Path $externalMount "legion12"
        $incoming = if ($ArtifactRoot -eq "/www/legion12") {
            Join-Path $externalArtifactRoot "incoming"
        }
        else {
            Join-Path $root "opt\legion12-deployment\incoming"
        }
        $package = Join-Path $root "package"
        $fakeBin = Join-Path $root "fake-bin"
        New-Item -ItemType Directory -Path `
            (Join-Path $active "publish"), `
            (Join-Path $active "opcgpro-vue\dist"), `
            (Join-Path $active "scripts"), `
            $runtime, $incoming, $package, $fakeBin, `
            (Join-Path $root "etc") -Force | Out-Null

        Write-Utf8NoBom (Join-Path $active ".deployment-commit") "$commitA`n"
        Write-Utf8NoBom (Join-Path $active "publish\GrandUMIServer.dll") "old"
        Write-Utf8NoBom (Join-Path $active "opcgpro-vue\dist\index.html") "old"
        Write-Utf8NoBom (Join-Path $active "scripts\ws-smoke.mjs") "// old"
        Write-Utf8NoBom (Join-Path $runtime "authoritative-before.txt") "preserve"
        Write-Utf8NoBom (Join-Path $root "etc\legion12-test.env") "fixture=1`n"
        if ($PreexistingBlock) {
            Write-Utf8NoBom (Join-Path $root "opt\legion12-deployment\deployment-blocked.txt") "manual-reconciliation-required`n"
        }

        New-Item -ItemType Directory -Path `
            (Join-Path $package "publish"), `
            (Join-Path $package "opcgpro-vue\dist"), `
            (Join-Path $package "scripts") -Force | Out-Null
        Write-Utf8NoBom (Join-Path $package ".deployment-commit") "$commitB`n"
        Write-Utf8NoBom (Join-Path $package "publish\GrandUMIServer.dll") "new"
        Write-Utf8NoBom (Join-Path $package "opcgpro-vue\dist\index.html") "new"
        Write-Utf8NoBom (Join-Path $package "scripts\ws-smoke.mjs") "// probe"

        $archive = Join-Path $incoming "l12-release-$commitB.tar.gz"
        $tarResult = Invoke-NativeCapture -Executable (Get-Command tar -ErrorAction Stop).Source -Arguments @(
            "-czf", $archive, "-C", $package, "."
        )
        Assert-True ($tarResult.ExitCode -eq 0) "无法创建服务器发布夹具：$($tarResult.Output)"
        $archiveSha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()

        $cardAssetsHash = "c" * 64
        $cardAssetsSha = "-"
        $cardAssetsArgument = "-"
        $cardAssetsFixture = $null
        if ($CardAssetsScenario -ne "none") {
            $cardAssetsFixture = if ($CardAssetsScenario -eq "opt-cache") {
                Join-Path $root "opt\legion12-static\card-assets\$cardAssetsHash"
            }
            else {
                Join-Path $root "card-assets-package"
            }
            New-Item -ItemType Directory -Path (Join-Path $cardAssetsFixture "cards") -Force | Out-Null
            Write-Utf8NoBom (Join-Path $cardAssetsFixture "card-assets.manifest.json") `
                "{`"assetVersion`":`"$cardAssetsHash`",`"cards`":{`"ST01-01`":{`"variants`":{`"thumbWebp`":`"cards/sample.webp`"}}}}`n"
            Write-Utf8NoBom (Join-Path $cardAssetsFixture "card-assets.preload.json") "{`"entries`":[]}`n"
            Write-Utf8NoBom (Join-Path $cardAssetsFixture "cards\sample.webp") "fixture-card"
            if ($CardAssetsScenario -eq "external-archive") {
                $cardAssetsArchive = Join-Path $incoming "l12-card-assets-$cardAssetsHash.tar.gz"
                $cardTarResult = Invoke-NativeCapture -Executable (Get-Command tar -ErrorAction Stop).Source -Arguments @(
                    "-czf", $cardAssetsArchive, "-C", $cardAssetsFixture, "."
                )
                Assert-True ($cardTarResult.ExitCode -eq 0) "无法创建卡图发布夹具：$($cardTarResult.Output)"
                $cardAssetsSha = (Get-FileHash -LiteralPath $cardAssetsArchive -Algorithm SHA256).Hash.ToLowerInvariant()
                $cardAssetsArgument = ConvertTo-MsysPath $cardAssetsArchive
            }
        }

        $fixtureTimestamp = "20260909T000000Z"
        $managedRuntimeBackupDirectory = if ($ArtifactRoot -eq "/www/legion12") {
            Join-Path $externalArtifactRoot "runtime-backups"
        }
        else {
            Join-Path $root "opt\legion12-deployment\runtime-backups"
        }
        $managedReleasesDirectory = if ($ArtifactRoot -eq "/www/legion12") {
            Join-Path $externalArtifactRoot "releases"
        }
        else {
            Join-Path $root "opt\legion12-releases"
        }
        $expectedBackup = Join-Path $managedRuntimeBackupDirectory "runtime-before-$($commitB.Substring(0, 12))-$fixtureTimestamp.tar.gz"
        $rootBackupSentinels = @()
        if ($ArtifactRoot -eq "/www/legion12") {
            $rootBackupDirectory = Join-Path $root "opt\legion12-deployment\runtime-backups"
            New-Item -ItemType Directory -Path $rootBackupDirectory -Force | Out-Null
            foreach ($sentinelName in @("runtime-before-root-old-a.tar.gz", "runtime-before-root-old-b.tar.gz")) {
                $sentinelPath = Join-Path $rootBackupDirectory $sentinelName
                Write-Utf8NoBom $sentinelPath "preserve-root-backup"
                $rootBackupSentinels += $sentinelPath
            }
        }
        $existingTargetPath = ""
        $existingTargetMarker = ""
        if (-not [string]::IsNullOrEmpty($ExistingBackupTarget)) {
            New-Item -ItemType Directory -Path $managedRuntimeBackupDirectory -Force | Out-Null
            $existingTargetPath = switch ($ExistingBackupTarget) {
                "final" { $expectedBackup }
                "partial" { "$expectedBackup.partial" }
                "sha" { "$expectedBackup.sha256" }
                "sha-partial" { "$expectedBackup.sha256.partial" }
            }
            if ($ExistingBackupTargetKind -eq "symlink") {
                $existingTargetBacking = Join-Path $root "existing-backup-target-backing"
                New-Item -ItemType Directory -Path $existingTargetBacking -Force | Out-Null
                $existingTargetMarker = Join-Path $existingTargetBacking "marker.txt"
                Write-Utf8NoBom $existingTargetMarker "do-not-overwrite"
                New-Item -ItemType Junction -Path $existingTargetPath -Target $existingTargetBacking | Out-Null
            }
            else {
                Write-Utf8NoBom $existingTargetPath "do-not-overwrite"
                $existingTargetMarker = $existingTargetPath
            }
        }
        if ($ExistingReleaseTarget) {
            New-Item -ItemType Directory -Path $managedReleasesDirectory -Force | Out-Null
            $existingReleasePath = Join-Path $managedReleasesDirectory "$commitB-$fixtureTimestamp"
            New-Item -ItemType Directory -Path $existingReleasePath -Force | Out-Null
            Write-Utf8NoBom (Join-Path $existingReleasePath "marker.txt") "preserve-release"
        }
        $managedStageParent = if ($ArtifactRoot -eq "/www/legion12") {
            Join-Path $externalArtifactRoot "staging"
        }
        else {
            Join-Path $root "opt"
        }
        $existingStagePath = Join-Path $managedStageParent "legion12-staging-$($commitB.Substring(0, 12))-$fixtureTimestamp"
        $existingStageMarker = ""
        if (-not [string]::IsNullOrEmpty($ExistingStageTarget)) {
            New-Item -ItemType Directory -Path $managedStageParent -Force | Out-Null
            if ($ExistingStageTarget -eq "symlink") {
                $existingStageBacking = Join-Path $root "existing-stage-backing"
                New-Item -ItemType Directory -Path $existingStageBacking -Force | Out-Null
                $existingStageMarker = Join-Path $existingStageBacking "marker.txt"
                Write-Utf8NoBom $existingStageMarker "preserve-stage"
                New-Item -ItemType Junction -Path $existingStagePath -Target $existingStageBacking | Out-Null
            }
            else {
                New-Item -ItemType Directory -Path $existingStagePath -Force | Out-Null
                $existingStageMarker = Join-Path $existingStagePath "marker.txt"
                Write-Utf8NoBom $existingStageMarker "preserve-stage"
            }
        }
        $externalCardTarget = Join-Path $externalArtifactRoot "card-assets\$cardAssetsHash"
        $externalCardTargetMarker = ""
        if ($ExternalCardTargetSymlink) {
            $externalCardParent = Split-Path -Parent $externalCardTarget
            $externalCardBacking = Join-Path $root "external-card-target-backing"
            New-Item -ItemType Directory -Path $externalCardParent, $externalCardBacking -Force | Out-Null
            $externalCardTargetMarker = Join-Path $externalCardBacking "marker.txt"
            Write-Utf8NoBom $externalCardTargetMarker "preserve-card-target"
            New-Item -ItemType Junction -Path $externalCardTarget -Target $externalCardBacking | Out-Null
        }

        New-FakeCommand $fakeBin "systemctl" @'
printf 'systemctl %s\n' "$*" >> "$L12_TEST_COMMAND_LOG"
case "${1:-}" in
  cat|daemon-reload) exit 0 ;;
  is-enabled) [ "${L12_TEST_SERVICE_ENABLED:-1}" = "1" ] && exit 0; exit 1 ;;
  stop) printf 'stopped\n' > "$L12_TEST_SERVICE_STATE"; exit 0 ;;
  is-active)
    [ -f "$L12_TEST_SERVICE_STATE" ] && [ "$(tr -d '\r\n' < "$L12_TEST_SERVICE_STATE")" = "running" ] && exit 0
    exit 3 ;;
  start)
    printf 'running\n' > "$L12_TEST_SERVICE_STATE"
    if [ "${L12_TEST_WRITE_ON_START:-0}" = "1" ]; then
      printf 'accepted-after-launch\n' > "$L12_TEST_RUNTIME_DIR/post-launch-write.txt"
    fi
    exit 0 ;;
esac
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "nginx" @'
if [ "${1:-}" = "-T" ]; then
  printf '%s\n' 'location = /api/admin/site/media' 'client_max_body_size 32m' 'media_upload_too_large' 'location = /card-assets/card-assets.manifest.json' 'max-age=31536000, immutable'
fi
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "runuser" @'
printf 'runuser %s\n' "$*" >> "$L12_TEST_COMMAND_LOG"
while [ "$#" -gt 0 ] && [ "$1" != "--" ]; do shift; done
if [ "${1:-}" = "--" ]; then shift; fi
"$@"
'@ | Out-Null
        New-FakeCommand $fakeBin "curl" @'
url=""
for argument in "$@"; do url="$argument"; done
printf 'curl %s\n' "$url" >> "$L12_TEST_COMMAND_LOG"
if [ "$url" = "$L12_DEPLOY_LOCAL_BASE/health" ] || [ "$url" = "$L12_DEPLOY_PUBLIC_BASE/health" ]; then
  served_commit="$(tr -d '\r\n' < "$L12_TEST_ACTIVE_DIR/.deployment-commit")"
  engine_commit="$served_commit"
  if [ "$url" = "$L12_DEPLOY_LOCAL_BASE/health" ] && [ -n "${L12_TEST_LOCAL_COMMIT_OVERRIDE:-}" ]; then
    served_commit="$L12_TEST_LOCAL_COMMIT_OVERRIDE"
  fi
  if [ "$url" = "$L12_DEPLOY_PUBLIC_BASE/health" ] && [ -n "${L12_TEST_PUBLIC_COMMIT_OVERRIDE:-}" ]; then
    served_commit="$L12_TEST_PUBLIC_COMMIT_OVERRIDE"
  fi
  engine_commit="$served_commit"
  health_status="${L12_TEST_HEALTH_STATUS:-ok}"
  health_maintenance="${L12_TEST_HEALTH_MAINTENANCE:-}"
  if [ -z "$health_maintenance" ]; then
    if [ "$health_status" = "maintenance" ]; then health_maintenance=true; else health_maintenance=false; fi
  fi
  printf '{"status":"%s","maintenance":%s,"service":"twelve-legions","serverVersion":"%s","engineVersion":"l12-engine/%s"}\n' "$health_status" "$health_maintenance" "$served_commit" "$engine_commit"
fi
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "timeout" @'
printf 'timeout %s\n' "$*" >> "$L12_TEST_COMMAND_LOG"
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "flock" "exit 0`n" | Out-Null
        New-FakeCommand $fakeBin "id" @'
if [ "${1:-}" = "-u" ]; then printf '0\n'; fi
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "chown" "exit 0`n" | Out-Null
        New-FakeCommand $fakeBin "chmod" @'
printf 'chmod %s\n' "$*" >> "$L12_TEST_COMMAND_LOG"
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "seq" "exit 0`n" | Out-Null
        New-FakeCommand $fakeBin "sha256sum" @'
case "${1:-}" in
  *runtime-before-*.partial)
    if [ "${L12_TEST_FAIL_BACKUP_SHA:-0}" = "1" ]; then exit 92; fi ;;
esac
node -e "const fs=require('node:fs'),crypto=require('node:crypto');const p=process.argv[1];process.stdout.write(crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')+'  '+p+'\n')" "$1"
'@ | Out-Null
        New-FakeCommand $fakeBin "stat" @'
if [ "${1:-}" = "-c" ] && [ "${2:-}" = "%s" ]; then
  node -e "process.stdout.write(String(require('node:fs').statSync(process.argv[1]).size)+'\n')" "$3"
  exit 0
fi
exit 64
'@ | Out-Null
        New-FakeCommand $fakeBin "ln" @'
printf 'ln %s\n' "$*" >> "$L12_TEST_COMMAND_LOG"
if [ "${1:-}" != "-s" ]; then exit 64; fi
node -e "require('node:fs').symlinkSync(process.argv[1],process.argv[2],'junction')" "$2" "$3"
'@ | Out-Null
        New-FakeCommand $fakeBin "install" @'
if [ "${1:-}" = "-m" ]; then shift 2; fi
if [ "$#" -ne 2 ]; then exit 64; fi
cp "$1" "$2"
'@ | Out-Null
        New-FakeCommand $fakeBin "tar" @'
if [ "${L12_TEST_FAIL_BACKUP:-0}" = "1" ]; then
  is_runtime_backup=0
  for argument in "$@"; do
    if [ "$argument" = "$L12_TEST_RUNTIME_DIR" ]; then is_runtime_backup=1; fi
  done
  if [ "$is_runtime_backup" = "1" ]; then exit 91; fi
fi
if [ "${L12_TEST_FAIL_BACKUP_VALIDATION:-0}" = "1" ]; then
  for argument in "$@"; do
    case "$argument" in *runtime-before-*.partial) exit 93 ;; esac
  done
fi
exec "$L12_TEST_REAL_TAR" "$@"
'@ | Out-Null

        $rootPosix = ConvertTo-MsysPath $root
        $fakeBinPosix = ConvertTo-MsysPath $fakeBin
        $serverDeployPosix = ConvertTo-MsysPath $serverDeploy
        $healthVerifierPosix = ConvertTo-MsysPath $healthVerifier
        $nodeDirectoryPosix = ConvertTo-MsysPath (Split-Path $nodePath -Parent)
        $archivePosix = ConvertTo-MsysPath $archive
        $externalMountPosix = ConvertTo-MsysPath $externalMount
        $realTarPosix = ConvertTo-MsysPath (Get-Command tar -ErrorAction Stop).Source
        $commandLog = Join-Path $root "commands.log"
        $serviceState = Join-Path $root "service.state"

        $environment = @{
            L12_DEPLOY_TEST_MODE = "1"
            L12_DEPLOY_TEST_ROOT = $rootPosix
            L12_DEPLOY_HEALTH_VERIFIER = $healthVerifierPosix
            L12_DEPLOY_HEALTH_ATTEMPTS = "1"
            L12_DEPLOY_HEALTH_DELAY_SECONDS = "0"
            L12_DEPLOY_LOCKED = "1"
            L12_DEPLOY_LOCAL_BASE = "http://127.0.0.1:8083"
            L12_DEPLOY_PUBLIC_BASE = "https://legion-12.com"
            L12_TEST_FAKE_BIN = $fakeBinPosix
            L12_TEST_NODE_DIR = $nodeDirectoryPosix
            L12_TEST_SERVER_SCRIPT = $serverDeployPosix
            L12_TEST_REAL_TAR = $realTarPosix
            L12_TEST_COMMAND_LOG = (ConvertTo-MsysPath $commandLog)
            L12_TEST_SERVICE_STATE = (ConvertTo-MsysPath $serviceState)
            L12_TEST_ACTIVE_DIR = "$rootPosix/opt/legion12-test"
            L12_TEST_RUNTIME_DIR = "$rootPosix/opt/legion12-runtime"
            L12_TEST_LOCAL_COMMIT_OVERRIDE = $LocalCommitOverride
            L12_TEST_PUBLIC_COMMIT_OVERRIDE = $PublicCommitOverride
            L12_TEST_HEALTH_STATUS = $HealthStatus
            L12_TEST_HEALTH_MAINTENANCE = $HealthMaintenance
            L12_TEST_FAIL_BACKUP = $(if ($FailBackup) { "1" } else { "0" })
            L12_TEST_FAIL_BACKUP_VALIDATION = $(if ($FailBackupValidation) { "1" } else { "0" })
            L12_TEST_FAIL_BACKUP_SHA = $(if ($FailBackupSha) { "1" } else { "0" })
            L12_TEST_WRITE_ON_START = $(if ($WriteOnStart) { "1" } else { "0" })
            L12_TEST_SERVICE_ENABLED = $(if ($DisabledService) { "0" } else { "1" })
            L12_DEPLOY_TEST_EXTERNAL_MOUNT_TARGET = $(if ([string]::IsNullOrEmpty($ExternalMountTarget)) { $externalMountPosix } else { $ExternalMountTarget })
            L12_DEPLOY_TEST_EXTERNAL_MOUNT_SOURCE = $(if ([string]::IsNullOrEmpty($ExternalMountSource)) { "test-external-device" } else { $ExternalMountSource })
            L12_DEPLOY_TEST_EXTERNAL_MOUNT_OPTIONS = $(if ([string]::IsNullOrEmpty($ExternalMountOptions)) { "rw,relatime" } else { $ExternalMountOptions })
            L12_DEPLOY_TEST_EXTERNAL_FSTAB_TARGET = $(if ([string]::IsNullOrEmpty($ExternalFstabTarget)) { $externalMountPosix } else { $ExternalFstabTarget })
            L12_DEPLOY_TEST_EXTERNAL_AVAILABLE_BYTES = $(if ($ExternalAvailableBytes -gt 0) { [string]$ExternalAvailableBytes } else { "" })
            L12_DEPLOY_TEST_BACKUP_MAX_BYTES = $(if ($BackupMaxBytes -gt 0) { [string]$BackupMaxBytes } else { "" })
            L12_DEPLOY_TEST_TIMESTAMP = $fixtureTimestamp
            L12_DEPLOY_TEST_SKIP_CARD_ASSET_CONTENT_VALIDATION = $(if ($CardAssetsScenario -eq "none") { "0" } else { "1" })
        }
        $savedEnvironment = @{}
        foreach ($entry in $environment.GetEnumerator()) {
            $savedEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
            [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
        }
        try {
            $launcher = 'export PATH="$L12_TEST_FAKE_BIN:/usr/bin:/bin:$L12_TEST_NODE_DIR"; /usr/bin/sh "$L12_TEST_SERVER_SCRIPT" "$@"'
            $serverArguments = @(
                "-c", $launcher, "l12-test", $Mode, $commitB, $archiveSha, $archivePosix,
                "-", "-", "-", $(if ($CardAssetsScenario -eq "none") { "-" } else { $cardAssetsHash }),
                $cardAssetsSha, $cardAssetsArgument
            )
            if (-not $OmitArtifactRootArgument) { $serverArguments += $ArtifactRoot }
            $result = Invoke-NativeCapture -Executable $bashPath -Arguments $serverArguments
        }
        finally {
            foreach ($entry in $savedEnvironment.GetEnumerator()) {
                [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
            }
        }
        [pscustomobject]@{
            Name = $Name
            Root = $root
            ExitCode = $result.ExitCode
            Output = $result.Output
            Commands = $(if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" })
            ExpectedBackup = $expectedBackup
            ExistingTargetPath = $existingTargetPath
            ExistingTargetMarker = $existingTargetMarker
            ExistingStagePath = $existingStagePath
            ExistingStageMarker = $existingStageMarker
            ExistingReleasePath = $(if ($ExistingReleaseTarget) { $existingReleasePath } else { "" })
            ExternalCardTarget = $externalCardTarget
            ExternalCardTargetMarker = $externalCardTargetMarker
            ExternalArtifactRoot = $externalArtifactRoot
            ManagedReleasesDirectory = $managedReleasesDirectory
            ExpectedRelease = (Join-Path $managedReleasesDirectory "$commitB-$fixtureTimestamp")
            CardAssetsHash = $cardAssetsHash
            CardAssetsFixture = $cardAssetsFixture
            RootBackupSentinels = $rootBackupSentinels
        }
    }

    function Assert-BaseStatePreserved {
        param(
            [Parameter(Mandatory = $true)]$Scenario,
            [switch]$AllowNewRuntimeFact
        )
        $activePath = Join-Path $Scenario.Root "opt\legion12-test"
        $runtimePath = Join-Path $Scenario.Root "opt\legion12-runtime"
        Assert-True ((Get-Content -LiteralPath (Join-Path $activePath ".deployment-commit") -Raw).Trim() -eq $commitA) `
            "$($Scenario.Name)：拒绝/恢复后活动版本发生变化。"
        Assert-True ((Get-Content -LiteralPath (Join-Path $runtimePath "authoritative-before.txt") -Raw) -eq "preserve") `
            "$($Scenario.Name)：拒绝/恢复后原有 runtime 事实发生变化。"
        if (-not $AllowNewRuntimeFact) {
            Assert-True (-not (Test-Path -LiteralPath (Join-Path $runtimePath "post-launch-write.txt"))) `
                "$($Scenario.Name)：新服务启动前错误产生了 runtime 事实。"
        }
    }

    function Assert-RootBackupsPreserved {
        param([Parameter(Mandatory = $true)]$Scenario)
        foreach ($sentinel in $Scenario.RootBackupSentinels) {
            Assert-True (Test-Path -LiteralPath $sentinel -PathType Leaf) "$($Scenario.Name)：根盘旧备份被删除。"
            Assert-True ((Get-Content -LiteralPath $sentinel -Raw) -eq "preserve-root-backup") `
                "$($Scenario.Name)：根盘旧备份被改写。"
        }
    }

    function Assert-BackupReceipt {
        param([Parameter(Mandatory = $true)]$Scenario)
        $backup = $Scenario.ExpectedBackup
        $sidecar = "$backup.sha256"
        Assert-True (Test-Path -LiteralPath $backup -PathType Leaf) "$($Scenario.Name)：缺少最终 runtime 备份。"
        Assert-True (-not ((Get-Item -LiteralPath $backup).Attributes -band [IO.FileAttributes]::ReparsePoint)) `
            "$($Scenario.Name)：最终 runtime 备份不是普通文件。"
        Assert-True (Test-Path -LiteralPath $sidecar -PathType Leaf) "$($Scenario.Name)：缺少 runtime 备份 SHA256 sidecar。"
        Assert-True (-not (Test-Path -LiteralPath "$backup.partial")) "$($Scenario.Name)：遗留 runtime 备份 partial。"
        Assert-True (-not (Test-Path -LiteralPath "$sidecar.partial")) "$($Scenario.Name)：遗留 SHA256 sidecar partial。"
        $actualSha = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant()
        $expectedSidecar = "$actualSha  $([IO.Path]::GetFileName($backup))`n"
        Assert-True ((Get-Content -LiteralPath $sidecar -Raw).Replace("`r`n", "`n") -eq $expectedSidecar) `
            "$($Scenario.Name)：runtime 备份 SHA256 sidecar 与最终文件不一致。"
        return $actualSha
    }

    function Assert-NoOwnedBackupResidue {
        param([Parameter(Mandatory = $true)]$Scenario)
        foreach ($path in @(
            $Scenario.ExpectedBackup,
            "$($Scenario.ExpectedBackup).partial",
            "$($Scenario.ExpectedBackup).sha256",
            "$($Scenario.ExpectedBackup).sha256.partial"
        )) {
            Assert-True (-not (Test-Path -LiteralPath $path)) "$($Scenario.Name)：失败后遗留本次未完成备份：$path"
        }
    }

    function Assert-EarlyExternalRejection {
        param([Parameter(Mandatory = $true)]$Scenario)
        Assert-True ($Scenario.ExitCode -ne 0) "$($Scenario.Name)：外置前置门禁错误放行。"
        Assert-True (-not $Scenario.Commands.Contains("systemctl stop")) "$($Scenario.Name)：前置拒绝后仍停止服务。"
        Assert-True (-not $Scenario.Commands.Contains("systemctl start")) "$($Scenario.Name)：前置拒绝后仍启动服务。"
        Assert-BaseStatePreserved $Scenario
        Assert-RootBackupsPreserved $Scenario
    }

    function Assert-CollisionTargetPreserved {
        param(
            [Parameter(Mandatory = $true)]$Scenario,
            [Parameter(Mandatory = $true)][string]$Path,
            [Parameter(Mandatory = $true)][string]$Marker,
            [Parameter(Mandatory = $true)][string]$ExpectedMarker
        )
        Assert-True ($Scenario.ExitCode -ne 0) "$($Scenario.Name)：既有目标碰撞错误放行。"
        Assert-True (Test-Path -LiteralPath $Path) "$($Scenario.Name)：既有目标被清理。"
        Assert-True (Test-Path -LiteralPath $Marker -PathType Leaf) "$($Scenario.Name)：既有目标标记被清理。"
        Assert-True ((Get-Content -LiteralPath $Marker -Raw) -eq $ExpectedMarker) "$($Scenario.Name)：既有目标字节被改写。"
    }

    $windowsDeployText = Get-Content -LiteralPath $windowsDeploy -Raw
    $prepareStorageIndex = $windowsDeployText.IndexOf('/usr/local/sbin/deploy-legion12-release prepare-storage ''$ServerArtifactRoot''', [StringComparison]::Ordinal)
    $releaseUploadIndex = $windowsDeployText.IndexOf('Invoke-External scp @sshOptions $releaseArchive', [StringComparison]::Ordinal)
    Assert-True ($prepareStorageIndex -ge 0 -and $prepareStorageIndex -lt $releaseUploadIndex) `
        "Windows 发布入口没有在大运行包上传前完成外置挂载/容量预检。"
    Assert-True ($windowsDeployText.Contains('''$ServerArtifactRoot''')) `
        "Windows 发布入口没有把固定制品根传给最终服务器发布命令。"
    $artifactRootAst = $deployCommand.ScriptBlock.Ast.ParamBlock.Parameters |
        Where-Object { $_.Name.VariablePath.UserPath -eq "ServerArtifactRoot" }
    Assert-True ($artifactRootAst.DefaultValue.Extent.Text -eq '"/opt"') "Windows 发布入口默认制品根不再是 /opt。"
    $invalidArtifactRootEntrypoint = Invoke-NativeCapture -Executable $powerShellPath.Source -Arguments @(
        "-NoProfile", "-File", $windowsDeploy, "-ServerArtifactRoot", "/tmp", "-DryRun"
    )
    Assert-True ($invalidArtifactRootEntrypoint.ExitCode -ne 0) "Windows 发布入口接受了任意服务器制品根。"
    Assert-True (-not $invalidArtifactRootEntrypoint.Output.Contains("同步并核对 GitHub main")) `
        "非法服务器制品根在参数绑定拒绝前已进入发布流程。"

    $success = Invoke-ServerScenario -Name "success"
    Assert-True ($success.ExitCode -eq 0) "精确版本部署行为夹具失败：$($success.Output)"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $success.Root "opt\legion12-runtime\.maintenance-sandbox-drain"))) "成功发布后没有仅解除临时沙盒围栏。"
    Assert-True ($success.Commands.Contains("curl http://127.0.0.1:8083/health")) "成功路径没有核验目标机本地健康身份。"
    Assert-True ($success.Commands.Contains("curl https://legion-12.com/health")) "成功路径没有核验公网健康身份。"
    Assert-True ($success.Commands.Contains("ws://127.0.0.1:8083/ws")) "成功路径没有执行本机 WebSocket 探针。"
    Assert-True ($success.Commands.Contains("wss://legion-12.com/ws")) "成功路径没有执行公网 WebSocket 探针。"
    $successInfo = Get-Content -LiteralPath (Join-Path $success.Root "opt\legion12-deployment\deployment-info.txt") -Raw
    Assert-True ($successInfo.StartsWith("Legion12 正式服`n") -or $successInfo.StartsWith("Legion12 正式服`r`n")) `
        "成功元数据标题未标识正式服。"
    Assert-True ($successInfo.Contains($commitB)) "成功元数据未绑定目标提交。"
    Assert-True ($successInfo.Contains("服务器制品根：/opt")) "默认发布元数据未记录兼容的 /opt 制品根。"
    Assert-True (Test-Path -LiteralPath $success.ExpectedRelease -PathType Container) "默认发布没有落入既有 /opt release 布局。"
    $successBackupSha = Assert-BackupReceipt $success
    Assert-True ($successInfo.Contains("部署前运行数据快照SHA256：$successBackupSha")) "默认发布元数据与最终备份 SHA256 不一致。"

    $legacyDefaultRoot = Invoke-ServerScenario -Name "legacy-default-root" -Mode "dry-run" -OmitArtifactRootArgument
    Assert-True ($legacyDefaultRoot.ExitCode -eq 0) "省略新增位置参数的旧服务器调用不再默认使用 /opt：$($legacyDefaultRoot.Output)"
    Assert-True (-not $legacyDefaultRoot.Commands.Contains("systemctl stop")) "兼容调用的 dry-run 错误停止服务。"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $legacyDefaultRoot.Root "www\legion12"))) "省略位置参数时错误启用了外置布局。"

    $serverInvalidRoot = Invoke-ServerScenario -Name "server-invalid-root" -ArtifactRoot "/tmp"
    Assert-True ($serverInvalidRoot.ExitCode -ne 0) "服务器入口接受了任意制品根。"
    Assert-True ($serverInvalidRoot.Output.Contains("只允许 /opt 或 /www/legion12")) "服务器制品根拒绝没有明确固定白名单。"
    Assert-True ([string]::IsNullOrEmpty($serverInvalidRoot.Commands)) "非法服务器制品根在拒绝前执行了系统命令。"

    $externalSuccess = Invoke-ServerScenario -Name "external-success" -ArtifactRoot "/www/legion12"
    Assert-True ($externalSuccess.ExitCode -eq 0) "外置制品路径发布失败：$($externalSuccess.Output)"
    Assert-True (Test-Path -LiteralPath $externalSuccess.ExpectedRelease -PathType Container) "外置 release 未落到固定独立盘目录。"
    Assert-True ((Get-Content -LiteralPath (Join-Path $externalSuccess.ExpectedRelease ".deployment-commit") -Raw).Trim() -eq $commitB) `
        "外置 release 提交标记错误。"
    $externalInfo = Get-Content -LiteralPath (Join-Path $externalSuccess.Root "opt\legion12-deployment\deployment-info.txt") -Raw
    Assert-True ($externalInfo.Contains("服务器制品根：/www/legion12")) "外置发布元数据没有固定制品根。"
    Assert-True ($externalInfo.Contains("活动版本：$(ConvertTo-MsysPath $externalSuccess.ExpectedRelease)")) `
        "外置发布元数据没有绑定外盘 release。"
    $externalBackupSha = Assert-BackupReceipt $externalSuccess
    Assert-True ($externalInfo.Contains("部署前运行数据快照SHA256：$externalBackupSha")) "外置发布元数据与最终备份 SHA256 不一致。"
    Assert-RootBackupsPreserved $externalSuccess
    $externalRootPosix = ConvertTo-MsysPath $externalSuccess.ExternalArtifactRoot
    $externalStageParentPosix = "$externalRootPosix/staging"
    $externalReleaseParentPosix = "$externalRootPosix/releases"
    $externalCardParentPosix = "$externalRootPosix/card-assets"
    Assert-True ($externalSuccess.Commands.Contains("chmod 0755 $externalRootPosix $externalCardParentPosix $externalStageParentPosix $externalReleaseParentPosix")) `
        "外置 staging/release/card 根没有以 0755 建立真实账号穿越边界。"
    Assert-True ($externalSuccess.Commands.Contains("chmod 0700 $externalRootPosix/incoming $externalRootPosix/runtime-backups")) `
        "外置 incoming/runtime-backups 没有保持 0700。"
    Assert-True ($externalSuccess.Commands.Contains("runuser -u legion12 -- test -x $externalRootPosix -a -x $externalStageParentPosix -a -x $externalReleaseParentPosix")) `
        "发布前没有以服务账号验证外置 staging/release 穿越权限。"
    Assert-True ($externalSuccess.Commands.Contains("runuser -u www-data -- test -x $externalRootPosix -a -x $externalStageParentPosix -a -x $externalCardParentPosix")) `
        "发布前没有以 Nginx 账号验证外置 staging/card 权限。"
    Assert-True ($externalSuccess.Commands.Contains("runuser -u legion12 -- test -r $externalStageParentPosix/legion12-staging-")) `
        "解包后没有以服务账号验证外置 staging 文件读取。"
    Assert-True ($externalSuccess.Commands.Contains("runuser -u www-data -- test -r $externalStageParentPosix/legion12-staging-")) `
        "解包后没有以 Nginx 账号验证外置 staging 文件读取。"

    $maintenanceSuccess = Invoke-ServerScenario -Name "maintenance-success" -HealthStatus "maintenance"
    Assert-True ($maintenanceSuccess.ExitCode -eq 0) "维护门禁下精确版本部署被错误判定失败：$($maintenanceSuccess.Output)"

    $wrongMountTarget = Invoke-ServerScenario -Name "external-wrong-mount-target" -ArtifactRoot "/www/legion12" `
        -ExternalMountTarget "/unexpected-mount"
    Assert-EarlyExternalRejection $wrongMountTarget
    Assert-True ($wrongMountTarget.Output.Contains("不在独立精确挂载点")) "外置挂载点拒绝原因不明确。"

    $sameMountSource = Invoke-ServerScenario -Name "external-same-root-source" -ArtifactRoot "/www/legion12" `
        -ExternalMountSource "test-root-device"
    Assert-EarlyExternalRejection $sameMountSource
    Assert-True ($sameMountSource.Output.Contains("不是独立文件系统")) "外置盘与根盘同源没有明确拒绝。"

    $readOnlyMount = Invoke-ServerScenario -Name "external-readonly-mount" -ArtifactRoot "/www/legion12" `
        -ExternalMountOptions "ro,relatime"
    Assert-EarlyExternalRejection $readOnlyMount
    Assert-True ($readOnlyMount.Output.Contains("不是发布流程所需的 rw 挂载")) "只读外置挂载没有明确拒绝。"

    $missingPersistentMount = Invoke-ServerScenario -Name "external-missing-fstab" -ArtifactRoot "/www/legion12" `
        -ExternalFstabTarget "/unexpected-mount"
    Assert-EarlyExternalRejection $missingPersistentMount
    Assert-True ($missingPersistentMount.Output.Contains("缺少持久挂载配置")) "缺少持久挂载配置没有明确拒绝。"

    $symlinkMount = Invoke-ServerScenario -Name "external-symlink-mount" -ArtifactRoot "/www/legion12" -ExternalMountSymlink
    Assert-EarlyExternalRejection $symlinkMount
    Assert-True ($symlinkMount.Output.Contains("符号链接或越界") -or $symlinkMount.Output.Contains("受管目录")) `
        "外置挂载软链/越界没有明确拒绝：$($symlinkMount.Output)"

    $lowExternalCapacity = Invoke-ServerScenario -Name "external-low-capacity" -ArtifactRoot "/www/legion12" `
        -ExternalAvailableBytes ([long](13GB))
    Assert-EarlyExternalRejection $lowExternalCapacity
    Assert-True ($lowExternalCapacity.Output.Contains("不足 14 GiB")) "外置盘 14 GiB 前置容量门槛没有明确拒绝。"
    Assert-True (-not $lowExternalCapacity.Output.Contains("自动删除")) "外置容量不足路径错误尝试自动清理。"

    $externalDryRun = Invoke-ServerScenario -Name "external-dry-run" -ArtifactRoot "/www/legion12" -Mode "dry-run"
    Assert-True ($externalDryRun.ExitCode -eq 0) "外置 dry-run 失败：$($externalDryRun.Output)"
    Assert-True (-not $externalDryRun.Commands.Contains("systemctl stop")) "外置 dry-run 错误停止服务。"
    Assert-True (-not (Test-Path -LiteralPath $externalDryRun.ExpectedRelease)) "外置 dry-run 错误发布了最终 release。"
    Assert-True (-not (Test-Path -LiteralPath $externalDryRun.ExpectedBackup)) "外置 dry-run 错误创建了 runtime 备份。"
    Assert-True (-not (Test-Path -LiteralPath $externalDryRun.ExistingStagePath)) "外置 dry-run 遗留 staging。"
    Assert-RootBackupsPreserved $externalDryRun

    $externalOptCardReuse = Invoke-ServerScenario -Name "external-opt-card-reuse" -ArtifactRoot "/www/legion12" `
        -Mode "dry-run" -CardAssetsScenario "opt-cache"
    Assert-True ($externalOptCardReuse.ExitCode -eq 0) "外置发布无法复用现有 /opt 卡图缓存：$($externalOptCardReuse.Output)"
    $optCardManifestPosix = "$(ConvertTo-MsysPath $externalOptCardReuse.CardAssetsFixture)/card-assets.manifest.json"
    Assert-True ($externalOptCardReuse.Commands.Contains("runuser -u www-data -- test -r $optCardManifestPosix")) `
        "外置发布没有以 Nginx 账号核验复用的 /opt 卡图。"
    Assert-True (-not (Test-Path -LiteralPath $externalOptCardReuse.ExternalCardTarget)) `
        "复用 /opt 卡图时错误复制到外置盘。"
    Assert-RootBackupsPreserved $externalOptCardReuse

    $externalNewCard = Invoke-ServerScenario -Name "external-new-card" -ArtifactRoot "/www/legion12" `
        -Mode "dry-run" -CardAssetsScenario "external-archive"
    Assert-True ($externalNewCard.ExitCode -eq 0) "新增卡图包无法在外置 staging 完整验证：$($externalNewCard.Output)"
    $externalCardStage = Join-Path (Join-Path $externalNewCard.ExternalArtifactRoot "staging") `
        "legion12-card-assets-staging-$($externalNewCard.CardAssetsHash)-20260909T000000Z"
    Assert-True (-not (Test-Path -LiteralPath $externalCardStage)) "外置新增卡图 dry-run 后遗留 staging。"
    Assert-True (-not (Test-Path -LiteralPath $externalNewCard.ExternalCardTarget)) "外置新增卡图 dry-run 错误发布最终缓存。"
    Assert-True ($externalNewCard.Commands.Contains("runuser -u www-data -- test -r $(ConvertTo-MsysPath $externalCardStage)/card-assets.manifest.json")) `
        "外置新增卡图没有在 staging 以 Nginx 账号验证读取权限。"
    Assert-RootBackupsPreserved $externalNewCard

    $retiredNode = Invoke-ServerScenario -Name "retired-node" -DisabledService
    Assert-True ($retiredNode.ExitCode -ne 0) "已禁用服务的退役节点通过了服务器发布自检。"
    Assert-True (-not $retiredNode.Commands.Contains("systemctl stop")) "退役节点拒绝前已停止或改动服务。"

    foreach ($blockedMode in @("deploy", "dry-run")) {
        $blockedRetry = Invoke-ServerScenario -Name "preexisting-block-$blockedMode" -Mode $blockedMode -PreexistingBlock
        $blockedMarker = Join-Path $blockedRetry.Root "opt\legion12-deployment\deployment-blocked.txt"
        $blockedActive = Join-Path $blockedRetry.Root "opt\legion12-test"
        $blockedRuntime = Join-Path $blockedRetry.Root "opt\legion12-runtime"
        Assert-True ($blockedRetry.ExitCode -ne 0) "已有人工对账阻断时仍允许 $blockedMode。"
        Assert-True ($blockedRetry.Output.Contains("人工对账")) "已有阻断的拒绝没有给出人工对账指引。"
        Assert-True (-not $blockedRetry.Commands.Contains("systemctl stop")) "已有阻断时 $blockedMode 仍执行了停服。"
        Assert-True (-not $blockedRetry.Commands.Contains("systemctl start")) "已有阻断时 $blockedMode 仍执行了启动。"
        Assert-True ((Get-Content -LiteralPath (Join-Path $blockedActive ".deployment-commit") -Raw).Trim() -eq $commitA) "已有阻断时活动版本发生变化。"
        Assert-True (-not ((Get-Item -LiteralPath $blockedActive).Attributes -band [IO.FileAttributes]::ReparsePoint)) "已有阻断时活动入口被替换。"
        Assert-True ((Get-Content -LiteralPath (Join-Path $blockedRuntime "authoritative-before.txt") -Raw) -eq "preserve") "已有阻断时 runtime 发生变化。"
        Assert-True ((Get-Content -LiteralPath $blockedMarker -Raw) -eq "manual-reconciliation-required`n") "已有阻断标记被改写或删除。"
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $blockedRetry.Root "opt\legion12-releases"))) "已有阻断时仍创建了 release staging 边界。"
    }

    $localStale = Invoke-ServerScenario -Name "local-stale" -LocalCommitOverride $commitA -WriteOnStart
    Assert-True ($localStale.ExitCode -ne 0) "本机仍运行旧提交时部署被错误判定成功。"
    Assert-True (Test-Path -LiteralPath (Join-Path $localStale.Root "opt\legion12-runtime\.maintenance-sandbox-drain")) "验证失败后错误解除沙盒发布围栏。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $localStale.Root "opt\legion12-runtime\post-launch-write.txt"))) "失败处理覆盖了新服务启动后产生的 runtime 事实。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $localStale.Root "opt\legion12-deployment\deployment-blocked.txt"))) "新服务启动后失败没有写 fail-closed 说明。"
    Assert-True (([regex]::Matches($localStale.Commands, 'systemctl start')).Count -eq 1) "新服务启动后失败错误重启了旧版本。"

    $publicStale = Invoke-ServerScenario -Name "public-stale" -PublicCommitOverride $commitA -WriteOnStart
    Assert-True ($publicStale.ExitCode -ne 0) "公网仍返回旧提交时部署被错误判定成功。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $publicStale.Root "opt\legion12-runtime\post-launch-write.txt"))) "公网版本失败后丢失新 runtime 事实。"
    Assert-True ($publicStale.Output.Contains("拒绝自动恢复旧 runtime")) "公网版本失败没有明确进入人工对账边界。"

    $degradedHealth = Invoke-ServerScenario -Name "degraded-health" -HealthStatus "degraded" -WriteOnStart
    Assert-True ($degradedHealth.ExitCode -ne 0) "版本正确但 status 非 ok 时部署被错误判定成功。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $degradedHealth.Root "opt\legion12-runtime\post-launch-write.txt"))) "非 ok 健康响应后的失败处理覆盖了新 runtime 事实。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $degradedHealth.Root "opt\legion12-deployment\deployment-blocked.txt"))) "非 ok 健康响应没有进入 fail-closed 边界。"

    $inconsistentMaintenance = Invoke-ServerScenario -Name "inconsistent-maintenance" `
        -HealthStatus "maintenance" -HealthMaintenance "false" -WriteOnStart
    Assert-True ($inconsistentMaintenance.ExitCode -ne 0) "maintenance=false 与 status=maintenance 不一致时部署被错误判定成功。"

    foreach ($stageKind in @("directory", "symlink")) {
        $stageCollision = Invoke-ServerScenario -Name "external-stage-collision-$stageKind" `
            -ArtifactRoot "/www/legion12" -ExistingStageTarget $stageKind
        Assert-CollisionTargetPreserved $stageCollision $stageCollision.ExistingStagePath `
            $stageCollision.ExistingStageMarker "preserve-stage"
        Assert-True (-not $stageCollision.Commands.Contains("systemctl stop")) `
            "$($stageCollision.Name)：stage 碰撞拒绝后仍停止服务。"
        if ($stageKind -eq "symlink") {
            Assert-True (((Get-Item -LiteralPath $stageCollision.ExistingStagePath).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) `
                "$($stageCollision.Name)：既有 stage 软链被替换。"
        }
        Assert-BaseStatePreserved $stageCollision
        Assert-RootBackupsPreserved $stageCollision
    }

    $releaseCollision = Invoke-ServerScenario -Name "external-release-collision" -ArtifactRoot "/www/legion12" `
        -ExistingReleaseTarget
    $releaseCollisionMarker = Join-Path $releaseCollision.ExistingReleasePath "marker.txt"
    Assert-CollisionTargetPreserved $releaseCollision $releaseCollision.ExistingReleasePath $releaseCollisionMarker "preserve-release"
    Assert-True (-not $releaseCollision.Commands.Contains("systemctl stop")) "既有 release 拒绝后仍停止服务。"
    Assert-BaseStatePreserved $releaseCollision
    Assert-RootBackupsPreserved $releaseCollision

    $cardTargetCollision = Invoke-ServerScenario -Name "external-card-target-symlink" -ArtifactRoot "/www/legion12" `
        -CardAssetsScenario "external-archive" -ExternalCardTargetSymlink
    Assert-CollisionTargetPreserved $cardTargetCollision $cardTargetCollision.ExternalCardTarget `
        $cardTargetCollision.ExternalCardTargetMarker "preserve-card-target"
    Assert-True (((Get-Item -LiteralPath $cardTargetCollision.ExternalCardTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) `
        "既有外置卡图目标软链被替换。"
    Assert-True (-not $cardTargetCollision.Commands.Contains("systemctl stop")) "外置卡图目标软链拒绝后仍停止服务。"
    Assert-BaseStatePreserved $cardTargetCollision
    Assert-RootBackupsPreserved $cardTargetCollision

    foreach ($backupTarget in @("final", "partial", "sha", "sha-partial")) {
        foreach ($targetKind in @("file", "symlink")) {
            $backupCollision = Invoke-ServerScenario -Name "external-backup-$backupTarget-$targetKind" `
                -ArtifactRoot "/www/legion12" -ExistingBackupTarget $backupTarget -ExistingBackupTargetKind $targetKind
            Assert-CollisionTargetPreserved $backupCollision $backupCollision.ExistingTargetPath `
                $backupCollision.ExistingTargetMarker "do-not-overwrite"
            if ($targetKind -eq "symlink") {
                Assert-True (((Get-Item -LiteralPath $backupCollision.ExistingTargetPath).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) `
                    "$($backupCollision.Name)：既有备份目标软链被替换。"
            }
            foreach ($managedPath in @(
                $backupCollision.ExpectedBackup,
                "$($backupCollision.ExpectedBackup).partial",
                "$($backupCollision.ExpectedBackup).sha256",
                "$($backupCollision.ExpectedBackup).sha256.partial"
            )) {
                if ($managedPath -ne $backupCollision.ExistingTargetPath) {
                    Assert-True (-not (Test-Path -LiteralPath $managedPath)) `
                        "$($backupCollision.Name)：碰撞拒绝前创建了非自有备份目标：$managedPath"
                }
            }
            Assert-True ($backupCollision.Commands.Contains("systemctl stop")) `
                "$($backupCollision.Name)：备份碰撞夹具没有到达快照边界。"
            Assert-True ($backupCollision.Output.Contains("上一版本已恢复并通过")) `
                "$($backupCollision.Name)：备份碰撞后没有验证父服务恢复。"
            Assert-BaseStatePreserved $backupCollision
            Assert-RootBackupsPreserved $backupCollision
        }
    }

    $backupFailures = @(
        (Invoke-ServerScenario -Name "external-backup-create-failure" -ArtifactRoot "/www/legion12" -FailBackup),
        (Invoke-ServerScenario -Name "external-backup-validation-failure" -ArtifactRoot "/www/legion12" -FailBackupValidation),
        (Invoke-ServerScenario -Name "external-backup-sha-failure" -ArtifactRoot "/www/legion12" -FailBackupSha),
        (Invoke-ServerScenario -Name "external-backup-size-limit" -ArtifactRoot "/www/legion12" -BackupMaxBytes 32)
    )
    foreach ($backupFailure in $backupFailures) {
        Assert-True ($backupFailure.ExitCode -ne 0) "$($backupFailure.Name)：备份失败夹具意外成功。"
        Assert-True ($backupFailure.Commands.Contains("systemctl stop")) "$($backupFailure.Name)：未到达快照边界。"
        Assert-True ($backupFailure.Output.Contains("上一版本已恢复并通过")) `
            "$($backupFailure.Name)：新服务启动前备份失败没有验证父服务恢复。"
        Assert-NoOwnedBackupResidue $backupFailure
        Assert-BaseStatePreserved $backupFailure
        Assert-RootBackupsPreserved $backupFailure
    }
    Assert-True ($backupFailures[3].Output.Contains("超过 4 GiB 硬上限")) "外置备份超上限没有明确 fail-closed。"

    $externalPostLaunchFailure = Invoke-ServerScenario -Name "external-post-launch-failure" `
        -ArtifactRoot "/www/legion12" -LocalCommitOverride $commitA -WriteOnStart
    Assert-True ($externalPostLaunchFailure.ExitCode -ne 0) "外置发布后身份错误被判定成功。"
    Assert-True (Test-Path -LiteralPath (Join-Path $externalPostLaunchFailure.Root "opt\legion12-runtime\post-launch-write.txt") -PathType Leaf) `
        "外置发布后失败丢失新服务写入的 runtime 事实。"
    Assert-True ((Get-Content -LiteralPath (Join-Path $externalPostLaunchFailure.Root "opt\legion12-runtime\authoritative-before.txt") -Raw) -eq "preserve") `
        "外置发布后失败覆盖了原 runtime 事实。"
    Assert-True ((Get-Content -LiteralPath (Join-Path $externalPostLaunchFailure.Root "opt\legion12-test\.deployment-commit") -Raw).Trim() -eq $commitB) `
        "外置发布后失败错误切回旧程序并可能触发 runtime 恢复。"
    Assert-True (([regex]::Matches($externalPostLaunchFailure.Commands, 'systemctl start')).Count -eq 1) `
        "外置发布后失败错误重启了旧服务。"
    Assert-True ($externalPostLaunchFailure.Output.Contains("拒绝自动恢复旧 runtime")) `
        "外置发布后失败没有进入禁止自动恢复边界。"
    $postLaunchBackupSha = Assert-BackupReceipt $externalPostLaunchFailure
    $postLaunchFailureRecord = Get-ChildItem -LiteralPath (Join-Path $externalPostLaunchFailure.Root "opt\legion12-deployment\failures") `
        -Filter "deploy-*.txt" | Select-Object -First 1
    Assert-True ($null -ne $postLaunchFailureRecord) "外置发布后失败没有保存失败现场。"
    $postLaunchFailureText = Get-Content -LiteralPath $postLaunchFailureRecord.FullName -Raw
    Assert-True ($postLaunchFailureText.Contains("runtimeBackup=$(ConvertTo-MsysPath $externalPostLaunchFailure.ExpectedBackup)")) `
        "外置发布后失败现场没有指向最终备份。"
    Assert-True ($postLaunchFailureText.Contains("runtimeBackupSha256=$postLaunchBackupSha")) `
        "外置发布后失败现场 SHA256 与最终备份不一致。"
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $externalPostLaunchFailure.Root "opt") `
        -Directory -Filter "legion12-runtime-restore-*").Count -eq 0) "外置发布后失败仍创建了 runtime 恢复目录。"
    Assert-RootBackupsPreserved $externalPostLaunchFailure

    $preLaunchFailure = Invoke-ServerScenario -Name "prelaunch-recovery" -FailBackup
    Assert-True ($preLaunchFailure.ExitCode -ne 0) "备份失败夹具意外成功。"
    Assert-True ($preLaunchFailure.Output.Contains("上一版本已恢复并通过本机、公网提交身份及 WebSocket 核验")) "新服务启动前失败没有验证旧版本恢复。"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $preLaunchFailure.Root "opt\legion12-deployment\deployment-blocked.txt"))) "已验证的启动前恢复被错误标记为阻断。"

    $unverifiedRecovery = Invoke-ServerScenario -Name "unverified-recovery" -FailBackup -PublicCommitOverride $commitB
    Assert-True ($unverifiedRecovery.ExitCode -ne 0) "旧版本公网身份错误时恢复被错误判定成功。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $unverifiedRecovery.Root "opt\legion12-deployment\deployment-blocked.txt"))) "无法核验的旧版本恢复没有保持停服并记录。"
    Assert-True (-not $unverifiedRecovery.Output.Contains("上一版本已恢复并通过")) "仅凭 systemctl start 误报了回滚成功。"
    Assert-True (([regex]::Matches($unverifiedRecovery.Commands, 'systemctl stop')).Count -ge 2) "旧版本身份无法核验后没有再次停服。"

    $unverifiedLocalRecovery = Invoke-ServerScenario -Name "unverified-local-recovery" -FailBackup -LocalCommitOverride $commitB
    Assert-True ($unverifiedLocalRecovery.ExitCode -ne 0) "旧版本本机身份错误时恢复被错误判定成功。"
    Assert-True ((Test-Path -LiteralPath (Join-Path $unverifiedLocalRecovery.Root "opt\legion12-deployment\deployment-blocked.txt"))) "本机身份错误被公网成功响应掩盖。"
    Assert-True (-not $unverifiedLocalRecovery.Output.Contains("上一版本已恢复并通过")) "本机身份失败后仍误报回滚成功。"

    Write-Host "[L12 deploy behavior] $serverScenarioCount isolated server scenarios passed: default/external paths, mount and capacity gates, atomic backup ownership, exact identity, and fail-closed recovery."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixture = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fixtureRoot).Path)
        if (-not $resolvedFixture.StartsWith($fixtureBasePath, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path $resolvedFixture -Leaf).StartsWith("l12-deploy-behavior-", [StringComparison]::Ordinal)) {
            throw "拒绝清理受管临时目录以外的发布夹具：$resolvedFixture"
        }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
