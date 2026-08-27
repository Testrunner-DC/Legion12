[CmdletBinding()]
param(
    [string]$Root = "D:\GPT\Legion12",
    [switch]$Strict
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if (-not (Test-Path -LiteralPath $resolvedRoot)) { throw "L12 root not found: $resolvedRoot" }

function Get-DirectoryBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return [int64]0 }
    [int64]$sum = 0
    Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue |
        ForEach-Object { $sum += [int64]$_.Length }
    return $sum
}

$app = Join-Path $resolvedRoot 'app'
$gitCommon = (& git -C $app rev-parse --git-common-dir 2>$null)
if ($LASTEXITCODE -eq 0 -and $gitCommon) {
    if (-not [IO.Path]::IsPathRooted($gitCommon)) { $gitCommon = Join-Path $app $gitCommon }
    $gitCommon = [IO.Path]::GetFullPath($gitCommon)
}

$budgets = [ordered]@{
    'app' = 2200MB
    'git-common' = 1400MB
    'app\opcgpro-vue\public\cards' = 650MB
    'app\opcgpro-vue\node_modules' = 220MB
    'app\opcgpro-vue\dist' = 5MB
    'artifacts\test-runs' = 500MB
    'artifacts\deploy' = 700MB
    'cache' = 1200MB
    'temp' = 200MB
}

$rows = foreach ($relative in $budgets.Keys) {
    $path = if ($relative -eq 'git-common' -and $gitCommon) { $gitCommon } else { Join-Path $resolvedRoot $relative }
    $bytes = Get-DirectoryBytes $path
    [pscustomobject]@{
        Path = $relative
        MiB = [math]::Round($bytes / 1MB, 1)
        BudgetMiB = [math]::Round([int64]$budgets[$relative] / 1MB, 1)
        Status = if ($bytes -le [int64]$budgets[$relative]) { 'OK' } else { 'OVER' }
    }
}

$required = @('app', 'source-library', 'references', 'tools', 'cache', 'temp', 'artifacts', 'archives')
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $resolvedRoot $_)) })
$workspace = Join-Path $resolvedRoot 'workspace'
$workspaceItem = Get-Item -LiteralPath $workspace -Force -ErrorAction SilentlyContinue
$workspaceTarget = if ($workspaceItem -and ($workspaceItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    $workspaceItem.Target
} else { $null }

Write-Host "[L12 storage audit] root: $resolvedRoot"
$rows | Format-Table -AutoSize
if ($missing.Count) { Write-Warning "Missing required directories: $($missing -join ', ')" }
if (-not $workspaceTarget) { Write-Warning 'workspace is not a compatibility junction.' }
elseif ([IO.Path]::GetFullPath([string]$workspaceTarget).TrimEnd('\') -ne (Join-Path $resolvedRoot 'app')) {
    Write-Warning "workspace points to an unexpected target: $workspaceTarget"
}

$over = @($rows | Where-Object Status -eq 'OVER')
if ($Strict -and ($missing.Count -gt 0 -or -not $workspaceTarget -or $over.Count -gt 0)) { exit 1 }
