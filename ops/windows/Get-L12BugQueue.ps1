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
    [ValidateRange(1, 100)]
    [int]$PageSize = 100,
    [string]$SnapshotTag = "",
    [string]$OutputPath = "",
    [string]$ReceiptPath = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "L12GeneratedStorage.ps1")

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
if ([string]::IsNullOrWhiteSpace($login.token)) { throw "管理员登录未返回令牌。" }
$headers = @{ Authorization = "Bearer $($login.token)" }

function Get-BugPage {
    param([int]$Page)
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add("page=$Page")
    $parts.Add("pageSize=$PageSize")
    $parts.Add("includeDetails=false")
    if ($Status) { $parts.Add("status=$([Uri]::EscapeDataString($Status))") }
    if ($Priority) { $parts.Add("priority=$([Uri]::EscapeDataString($Priority))") }
    if ($Search) { $parts.Add("search=$([Uri]::EscapeDataString($Search))") }
    Invoke-RestMethod -Method Get -Uri "$base/api/admin/bugs/query?$($parts -join '&')" -Headers $headers
}

$first = Get-BugPage -Page 1
if ($null -eq $first -or $null -eq $first.items -or $null -eq $first.total -or $null -eq $first.platformVersion) {
    throw "Bug 分页响应缺少不可变快照所需字段。"
}
$platformVersion = [int64]$first.platformVersion
$total = [int]$first.total
$pages = [Math]::Max(1, [int][Math]::Ceiling($total / [double]$PageSize))
$items = [System.Collections.Generic.List[object]]::new()
$items.AddRange(@($first.items))
for ($page = 2; $page -le $pages; $page++) {
    $next = Get-BugPage -Page $page
    if ([int64]$next.platformVersion -ne $platformVersion -or [int]$next.total -ne $total) {
        throw "Bug 队列在分页读取期间发生变化，拒绝生成混合快照；请重新拉取。"
    }
    $items.AddRange(@($next.items))
}
if ($items.Count -ne $total) { throw "Bug 分页快照条数不完整：$($items.Count)/$total。" }

if ([string]::IsNullOrWhiteSpace($SnapshotTag)) {
    $SnapshotTag = "intake-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
}
$snapshot = [ordered]@{
    schema = 1
    snapshotTag = $SnapshotTag
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    apiBase = $base
    sourceEndpoint = "/api/admin/bugs/query"
    filters = [ordered]@{ status = $Status; priority = $Priority; search = $Search }
    pageSize = $PageSize
    pages = $pages
    total = $total
    platformVersion = $platformVersion
    items = @($items)
}
$snapshotJson = ConvertTo-Json -InputObject $snapshot -Depth 20 -Compress
$sha = [System.Security.Cryptography.SHA256]::Create()
$snapshotSha256 = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($snapshotJson))).Replace('-', '').ToLowerInvariant())
$snapshot.snapshotSha256 = $snapshotSha256
$finalJson = ConvertTo-Json -InputObject $snapshot -Depth 20

if ($OutputPath) {
    $resolved = Assert-L12GeneratedPath -Path $OutputPath -Purpose 'Bug intake snapshot'
    $directory = Split-Path -Parent $resolved
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    [System.IO.File]::WriteAllText($resolved, $finalJson, [System.Text.UTF8Encoding]::new($false))
}
if ($ReceiptPath) {
    $receipt = [ordered]@{
        schema = 1; kind = 'bug-intake'; snapshotTag = $SnapshotTag; snapshotPath = if ($OutputPath) { $resolved } else { $null }
        snapshotSha256 = $snapshotSha256; total = $total; pages = $pages; platformVersion = $platformVersion
        capturedAtUtc = $snapshot.capturedAtUtc; status = 'captured'
    }
    $receiptPathResolved = Assert-L12GeneratedPath -Path $ReceiptPath -Purpose 'Bug intake receipt'
    $receiptDirectory = Split-Path -Parent $receiptPathResolved
    if ($receiptDirectory) { New-Item -ItemType Directory -Force -Path $receiptDirectory | Out-Null }
    [System.IO.File]::WriteAllText($receiptPathResolved, (ConvertTo-Json $receipt -Depth 10), [Text.UTF8Encoding]::new($false))
}
if (-not $OutputPath) { $finalJson }
else { Write-Host "已只读导出 $total 条 Bug；SnapshotTag=$SnapshotTag；SHA256=$snapshotSha256；$resolved" }
