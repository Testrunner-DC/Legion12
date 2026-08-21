[CmdletBinding()]
param(
    [string]$Server = "root@legion12.grand-umi.com",
    [switch]$DryRun,
    [switch]$AllowVerifiedWorktree
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败（退出码 $LASTEXITCODE）：$Command $($Arguments -join ' ')"
    }
}
function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "缺少命令：$Name"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$serverScript = Join-Path $repoRoot "ops\server\deploy-l12-release.sh"
$originalLocation = Get-Location
$workDirectory = $null

try {
    Set-Location $repoRoot
    foreach ($commandName in @("git", "dotnet", "npm", "ssh", "scp")) {
        Require-Command $commandName
    }
    # Windows 的 npm PowerShell shim 会继承本脚本的 StrictMode，并在部分 npm
    # 版本读取不存在的 InvocationInfo.Statement 时失败。优先调用 npm.cmd，
    # 让 npm 在独立的 cmd 进程中运行；非 Windows 环境仍回退到 npm。
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
    $npmExecutable = if ($null -ne $npmCommand) { $npmCommand.Source } else { "npm" }

    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or ($branch -ne "main" -and -not $AllowVerifiedWorktree)) {
        throw "只能从 main 分支部署，当前分支：$branch"
    }
    if ($branch -ne "main") {
        Write-Host "[L12 部署] 使用隔离验证工作区：$branch；稍后仍会强制校验 HEAD 与 origin/main 完全一致。"
    }
    $dirty = (& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $dirty) {
        throw "工作区存在未提交修改，请先提交或妥善处理后再部署。"
    }

    Write-Host "[L12 部署] 同步 GitHub main..."
    Invoke-External git fetch --prune origin main
    $commit = (& git rev-parse HEAD).Trim()
    $remoteCommit = (& git rev-parse origin/main).Trim()
    if ($commit -ne $remoteCommit) {
        throw "本地 main 与 origin/main 不一致。请先执行 git pull --ff-only origin main。"
    }

    Write-Host "[L12 部署] 运行后端测试..."
    Invoke-External dotnet test ".\TwelveLegions.Tests\TwelveLegions.Tests.csproj" --configuration Release
    Invoke-External dotnet test ".\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj" --configuration Release --filter "FullyQualifiedName~PlatformStoreTests"

    Write-Host "[L12 部署] 验证前端生产构建..."
    Push-Location (Join-Path $repoRoot "opcgpro-vue")
    try {
        Invoke-External $npmExecutable ci
        Invoke-External $npmExecutable run build
    }
    finally {
        Pop-Location
    }

    $workDirectory = Join-Path ([IO.Path]::GetTempPath()) "legion12-deploy-$commit-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    $archivePath = Join-Path $workDirectory "legion12-$commit.tar.gz"
    Write-Host "[L12 部署] 打包提交 $commit..."
    Invoke-External git archive --format=tar.gz --output $archivePath HEAD
    $sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

    $remoteBootstrap = "/tmp/deploy-l12-release-$commit.sh"
    $remoteArchive = "/opt/legion12-deployment/incoming/legion12-$commit.tar.gz"
    Write-Host "[L12 部署] 上传发布工具和版本包..."
    Invoke-External ssh $Server "mkdir -p /opt/legion12-deployment/incoming"
    Invoke-External scp $serverScript "${Server}:$remoteBootstrap"
    Invoke-External scp $archivePath "${Server}:$remoteArchive"
    Invoke-External ssh $Server "sed -i 's/\r$//' '$remoteBootstrap' && install -m 0755 '$remoteBootstrap' /usr/local/sbin/deploy-legion12-release && rm -f '$remoteBootstrap'"

    $mode = if ($DryRun) { "dry-run" } else { "deploy" }
    Write-Host "[L12 部署] 服务器开始执行 $mode..."
    Invoke-External ssh $Server "/usr/local/sbin/deploy-legion12-release $mode $commit $sha256 $remoteArchive"

    if ($DryRun) {
        Write-Host "[L12 部署] 干运行成功，线上版本未改变。"
    }
    else {
        Write-Host "[L12 部署] 发布成功：https://legion12.grand-umi.com/"
    }
}
finally {
    Set-Location $originalLocation
    if ($null -ne $workDirectory -and (Test-Path -LiteralPath $workDirectory)) {
        Get-ChildItem -LiteralPath $workDirectory -File | Remove-Item -Force
        Remove-Item -LiteralPath $workDirectory -Force
    }
}
