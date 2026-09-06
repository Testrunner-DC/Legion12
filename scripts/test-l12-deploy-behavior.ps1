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

    foreach ($accepted in @("root@legion-12.com", "root@38.76.208.25")) {
        $endpoint = Resolve-L12ProductionEndpoint -RemoteServer $accepted
        Assert-True ($endpoint.TrustedHostKeyAlias -eq "38.76.208.25") "合法生产目标没有固定到新机主机密钥别名：$accepted"
    }
    foreach ($rejected in @(
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
    Write-Utf8NoBom $knownHosts "38.76.208.25 $($publicKeyParts[0]) $($publicKeyParts[1])`n"
    $sshOptions = @(Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer "root@legion-12.com" -KnownHostsFile $knownHosts)
    Assert-True ($sshOptions -contains "StrictHostKeyChecking=yes") "生产 SSH 没有严格主机密钥校验。"
    Assert-True ($sshOptions -contains "HostName=38.76.208.25") "生产 SSH 没有固定连接新机 IP。"
    Assert-True ($sshOptions -contains "HostKeyAlias=38.76.208.25") "生产主域没有固定使用新机 IP 指纹。"
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
    $emptyKnownHosts = Join-Path $sshFixture "empty_known_hosts"
    Write-Utf8NoBom $emptyKnownHosts ""
    $missingTrustRejected = $false
    try { Resolve-L12SshOptions -RepositoryRoot $repoRoot -RemoteServer "root@legion-12.com" -KnownHostsFile $emptyKnownHosts | Out-Null }
    catch { $missingTrustRejected = $_.Exception.Message.Contains("缺少已人工核验的新生产服务器") }
    Assert-True $missingTrustRejected "缺少新机 IP 指纹时没有失败关闭。"

    $commitA = "a" * 40
    $commitB = "b" * 40
    $validHealth = "{`"status`":`"ok`",`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}"
    $validResult = Invoke-NativeCapture -Executable $nodePath -Arguments @($healthVerifier, $commitA) -StandardInput $validHealth
    Assert-True ($validResult.ExitCode -eq 0) "精确健康身份被错误拒绝：$($validResult.Output)"
    foreach ($invalidHealth in @(
        "{`"status`":`"degraded`",`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"service`":`"twelve-legions`",`"serverVersion`":`"$commitB`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "{`"service`":`"twelve-legions`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitB`"}",
        "{`"service`":`"other`",`"serverVersion`":`"$commitA`",`"engineVersion`":`"l12-engine/$commitA`"}",
        "not-json"
    )) {
        $invalidResult = Invoke-NativeCapture -Executable $nodePath -Arguments @($healthVerifier, $commitA) -StandardInput $invalidHealth
        Assert-True ($invalidResult.ExitCode -ne 0) "健康身份校验器接受了错误响应：$invalidHealth"
    }

    function New-FakeCommand {
        param([string]$Directory, [string]$Name, [string]$Body)
        $path = Join-Path $Directory $Name
        Write-Utf8NoBom $path "#!/usr/bin/env sh`n$Body"
        return $path
    }

    function Invoke-ServerScenario {
        param(
            [Parameter(Mandatory = $true)][string]$Name,
            [string]$LocalCommitOverride = "",
            [string]$PublicCommitOverride = "",
            [string]$HealthStatus = "ok",
            [ValidateSet("deploy", "dry-run")][string]$Mode = "deploy",
            [switch]$FailBackup,
            [switch]$WriteOnStart,
            [switch]$DisabledService,
            [switch]$PreexistingBlock
        )

        $root = Join-Path $fixtureRoot "l12-deploy-behavior-$Name"
        $active = Join-Path $root "opt\legion12-test"
        $runtime = Join-Path $root "opt\legion12-runtime"
        $incoming = Join-Path $root "opt\legion12-deployment\incoming"
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
  printf '%s\n' 'location = /api/admin/site/media' 'client_max_body_size 32m' 'media_upload_too_large'
fi
exit 0
'@ | Out-Null
        New-FakeCommand $fakeBin "runuser" @'
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
  printf '{"status":"%s","service":"twelve-legions","serverVersion":"%s","engineVersion":"l12-engine/%s"}\n' "${L12_TEST_HEALTH_STATUS:-ok}" "$served_commit" "$engine_commit"
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
        New-FakeCommand $fakeBin "chmod" "exit 0`n" | Out-Null
        New-FakeCommand $fakeBin "seq" "exit 0`n" | Out-Null
        New-FakeCommand $fakeBin "sha256sum" @'
node -e "const fs=require('node:fs'),crypto=require('node:crypto');const p=process.argv[1];process.stdout.write(crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')+'  '+p+'\n')" "$1"
'@ | Out-Null
        New-FakeCommand $fakeBin "ln" @'
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
  for argument in "$@"; do
    case "$argument" in *runtime-before-*) exit 91 ;; esac
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
            L12_TEST_FAIL_BACKUP = $(if ($FailBackup) { "1" } else { "0" })
            L12_TEST_WRITE_ON_START = $(if ($WriteOnStart) { "1" } else { "0" })
            L12_TEST_SERVICE_ENABLED = $(if ($DisabledService) { "0" } else { "1" })
        }
        $savedEnvironment = @{}
        foreach ($entry in $environment.GetEnumerator()) {
            $savedEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
            [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
        }
        try {
            $launcher = 'export PATH="$L12_TEST_FAKE_BIN:/usr/bin:/bin:$L12_TEST_NODE_DIR"; /usr/bin/sh "$L12_TEST_SERVER_SCRIPT" "$@"'
            $result = Invoke-NativeCapture -Executable $bashPath -Arguments @(
                "-c", $launcher, "l12-test", $Mode, $commitB, $archiveSha, $archivePosix,
                "-", "-", "-", "-", "-", "-"
            )
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
        }
    }

    $success = Invoke-ServerScenario -Name "success"
    Assert-True ($success.ExitCode -eq 0) "精确版本部署行为夹具失败：$($success.Output)"
    Assert-True ($success.Commands.Contains("curl http://127.0.0.1:8083/health")) "成功路径没有核验目标机本地健康身份。"
    Assert-True ($success.Commands.Contains("curl https://legion-12.com/health")) "成功路径没有核验公网健康身份。"
    Assert-True ($success.Commands.Contains("ws://127.0.0.1:8083/ws")) "成功路径没有执行本机 WebSocket 探针。"
    Assert-True ($success.Commands.Contains("wss://legion-12.com/ws")) "成功路径没有执行公网 WebSocket 探针。"
    Assert-True ((Get-Content -LiteralPath (Join-Path $success.Root "opt\legion12-deployment\deployment-info.txt") -Raw).Contains($commitB)) "成功元数据未绑定目标提交。"

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

    Write-Host "[L12 deploy behavior] target allowlist, pinned trust, exact local/public identity, and fail-closed recovery passed."
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
