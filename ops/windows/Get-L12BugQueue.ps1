[CmdletBinding()]
param(
    [string]$ApiBase = "https://legion-12.com",
    [string]$Username = "Admin",
    [string]$Password = $env:L12_ADMIN_PASSWORD,
    [ValidateSet("", "new", "confirmed", "in-progress", "resolved", "closed")]
    [string]$Status = "",
    [ValidateSet("", "low", "normal", "high", "critical")]
    [string]$Priority = "",
    [string]$Search = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Password)) {
    $securePassword = Read-Host "L12 管理员密码" -AsSecureString
    $credential = [System.Net.NetworkCredential]::new("", $securePassword)
    $Password = $credential.Password
}

$base = $ApiBase.TrimEnd("/")
$login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType "application/json" -Body (@{
    username = $Username
    password = $Password
} | ConvertTo-Json)

if ([string]::IsNullOrWhiteSpace($login.token)) {
    throw "管理员登录未返回令牌。"
}

$query = [System.Collections.Generic.List[string]]::new()
if ($Status) { $query.Add("status=$([Uri]::EscapeDataString($Status))") }
if ($Priority) { $query.Add("priority=$([Uri]::EscapeDataString($Priority))") }
if ($Search) { $query.Add("search=$([Uri]::EscapeDataString($Search))") }
$uri = "$base/api/admin/bugs"
if ($query.Count -gt 0) { $uri += "?" + ($query -join "&") }

$reports = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ Authorization = "Bearer $($login.token)" }
$json = ConvertTo-Json -InputObject @($reports) -Depth 12

if ($OutputPath) {
    $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    $directory = Split-Path -Parent $resolved
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    [System.IO.File]::WriteAllText($resolved, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "已只读导出 $(@($reports).Count) 条 Bug：$resolved"
} else {
    $json
}
