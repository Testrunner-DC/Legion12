<#
.SYNOPSIS
正式服部署唯一推荐入口：登录取部署控制令牌，再以 SecureString 经标准输入交给 deploy-l12.ps1。

.DESCRIPTION
薄包装，不含部署逻辑。只做三件事：
  1. 取得正式服管理员会话令牌（64 位小写十六进制）；凭据来源顺序：
     env L12_DEPLOY_ADMIN_USERNAME / L12_DEPLOY_ADMIN_PASSWORD
     -> -CredentialFile（JSON，含 username+password 或已铸造的 token）
     仓库内不得出现任何凭据；令牌从不回显。
  2. 以 SecureString 传入 deploy-l12.ps1 的 -DeploymentControlSessionToken。
  3. 其余参数原样透传。

失败即止：缺凭据 / 令牌格式不符 / 登录失败 / deploy-l12.ps1 非零退出，都原样传播退出码，
不重试、不吞错；取令牌失败时不会调用任何外部命令（不会碰到 ssh）。

令牌语义（仓库内事实）：它是普通管理员会话令牌，服务端仅以 Authorization: Bearer 使用
（L12WebSocketServer.DeploymentDrainControl.cs:61），要求 ReleasesRead/ReleasesExecute，
role=admin 即具备（L12AdminControlPlane.cs:137）。登录端点 POST /api/auth/login
（L12WebSocketServer.cs:1175）返回 { message, account, token }。不需要第二批准：
--bootstrap-second-approver 是「离线引导第二审批人账号」的既有高风险命令特性
（L12PlatformStore.SecurityGovernance.cs:158），与取得部署令牌无关；MFA 双次校验尚未实现
（L12PlatformStore.SecurityGovernance.cs:95-99，EnrollmentEnabled=false）。

.NOTES
-DryRun 不需要令牌（deploy-l12.ps1:30 对 -DryRun 放行），因此该模式下不登录、不接触正式服。
#>
[CmdletBinding()]
param(
    [string]$Server = "root@legion-12.com",
    [string]$LoginBaseUrl = "https://legion-12.com",
    [string]$CredentialFile = "D:\GPT\Legion12\artifacts\deploy-credentials.json",
    [string]$ArtifactManifest = "",
    [string]$KnownHostsFile = "",
    [string]$IdentityFile = "",
    [Nullable[DateTimeOffset]]$DeploymentDeadline = $null,
    [ValidateSet("/opt", "/www/legion12")]
    [string]$ServerArtifactRoot = "/opt",
    [switch]$Rollback,
    [switch]$DryRun,
    [switch]$PruneRuntimeBackups,
    [string]$DeployScriptPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Fail {
    param([string]$Code, [string]$Message, [int]$ExitCode)
    [Console]::Error.WriteLine("" + $Code + ": " + $Message)
    exit $ExitCode
}

function Get-CredentialRecord {
    $username = $env:L12_DEPLOY_ADMIN_USERNAME
    $password = $env:L12_DEPLOY_ADMIN_PASSWORD
    if (-not [string]::IsNullOrWhiteSpace($username) -and -not [string]::IsNullOrWhiteSpace($password)) {
        return [pscustomobject]@{ Username = $username.Trim(); Password = $password; Token = "" }
    }
    if (-not (Test-Path -LiteralPath $CredentialFile -PathType Leaf)) { return $null }
    $raw = Get-Content -LiteralPath $CredentialFile -Raw -Encoding utf8
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    try { $record = $raw | ConvertFrom-Json } catch { return $null }
    if ($null -eq $record) { return $null }
    $names = @($record.PSObject.Properties.Name)
    $fileToken = if ($names -contains "token") { [string]$record.token } else { "" }
    $fileUser = if ($names -contains "username") { [string]$record.username } else { "" }
    $filePassword = if ($names -contains "password") { [string]$record.password } else { "" }
    return [pscustomobject]@{ Username = $fileUser.Trim(); Password = $filePassword; Token = $fileToken.Trim() }
}

function Assert-ControlToken {
    param([string]$Token)
    if ($Token -cnotmatch "^[0-9a-f]{64}$") {
        Fail -Code "deploy_control_token_invalid" -Message "部署控制令牌格式不符（要求 64 位小写十六进制），未调用任何外部命令。" -ExitCode 3
    }
    return $Token
}

$deployScript = if ([string]::IsNullOrWhiteSpace($DeployScriptPath)) {
    Join-Path $PSScriptRoot "deploy-l12.ps1"
} else { $DeployScriptPath }
if (-not (Test-Path -LiteralPath $deployScript -PathType Leaf)) {
    Fail -Code "deploy_script_missing" -Message ("找不到 deploy-l12.ps1：" + $deployScript) -ExitCode 5
}

$controlToken = $null
if (-not $DryRun) {
    $record = Get-CredentialRecord
    if ($null -eq $record) {
        Fail -Code "deploy_credentials_missing" -Message "缺少部署凭据：请设置 L12_DEPLOY_ADMIN_USERNAME/L12_DEPLOY_ADMIN_PASSWORD，或提供 -CredentialFile。" -ExitCode 2
    }
    if (-not [string]::IsNullOrWhiteSpace($record.Token)) {
        $controlToken = Assert-ControlToken -Token $record.Token
    }
    else {
        if ([string]::IsNullOrWhiteSpace($record.Username) -or [string]::IsNullOrWhiteSpace($record.Password)) {
            Fail -Code "deploy_credentials_missing" -Message "凭据不完整：需要 username+password，或已铸造的 token。" -ExitCode 2
        }
        $loginUrl = $LoginBaseUrl.TrimEnd("/") + "/api/auth/login"
        $body = @{ username = $record.Username; password = $record.Password } | ConvertTo-Json -Compress
        try {
            $response = Invoke-RestMethod -Method Post -Uri $loginUrl -ContentType "application/json" -Body $body -TimeoutSec 30
        }
        catch {
            Fail -Code "deploy_login_failed" -Message ("登录正式服失败，未取得部署控制令牌：" + $_.Exception.Message) -ExitCode 4
        }
        if ($null -eq $response -or -not (@($response.PSObject.Properties.Name) -contains "token")) {
            Fail -Code "deploy_login_failed" -Message "登录响应没有返回 token。" -ExitCode 4
        }
        $controlToken = Assert-ControlToken -Token ([string]$response.token)
    }
    $response = $null
    $record = $null
}

$secureToken = $null
if (-not $DryRun) { $secureToken = ConvertTo-SecureString -String $controlToken -AsPlainText -Force }
$controlToken = $null

$forward = @{
    Server = $Server
    ArtifactManifest = $ArtifactManifest
    KnownHostsFile = $KnownHostsFile
    IdentityFile = $IdentityFile
    ServerArtifactRoot = $ServerArtifactRoot
}
if ($null -ne $DeploymentDeadline) { $forward.DeploymentDeadline = $DeploymentDeadline }
if ($Rollback) { $forward.Rollback = $true }
if ($DryRun) { $forward.DryRun = $true }
if ($PruneRuntimeBackups) { $forward.PruneRuntimeBackups = $true }
if (-not $DryRun) { $forward.DeploymentControlSessionToken = $secureToken }

$exitCode = 0
try {
    & $deployScript @forward
    if ($null -ne $LASTEXITCODE) { $exitCode = $LASTEXITCODE }
}
catch {
    [Console]::Error.WriteLine("deploy_wrapper_failed: " + $_.Exception.Message)
    $exitCode = 1
}
finally {
    $secureToken = $null
    $forward = $null
}
exit $exitCode
