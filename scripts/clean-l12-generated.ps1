[CmdletBinding()]
param(
    [string]$Root = "D:\GPT\Legion12",
    [switch]$Apply,
    [int]$TestRunsToKeep = 2,
    [int]$DeployDirectoriesToKeep = 2
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$app = Join-Path $resolvedRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $app '.git'))) {
    $app = Join-Path $resolvedRoot 'workspace'
}
if (-not (Test-Path -LiteralPath (Join-Path $app '.git'))) { throw "Canonical Git checkout not found: $app" }

$targets = [Collections.Generic.List[object]]::new()
function Add-Target([string]$Path, [string]$Reason) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith("$resolvedRoot\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing path outside L12 root: $full"
    }
    if (-not (Test-Path -LiteralPath $full)) { return }
    if ($targets.Count -gt 0 -and @($targets | ForEach-Object { $_.Path }) -contains $full) { return }
    [int64]$bytes = 0
    $item = Get-Item -LiteralPath $full -Force
    if ($item.PSIsContainer) {
        Get-ChildItem -LiteralPath $full -File -Recurse -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $bytes += [int64]$_.Length }
    } else { $bytes = [int64]$item.Length }
    $targets.Add([pscustomobject]@{ Path = $full; Reason = $Reason; Bytes = $bytes })
}

foreach ($relative in @(
    '.runtime',
    'opcgpro-vue\dist',
    'TwelveLegions.Tests\bin', 'TwelveLegions.Tests\obj'
)) { Add-Target (Join-Path $app $relative) 'regenerable build output' }

# Discover .NET project directories instead of embedding non-ASCII path names so
# the script behaves the same in Windows PowerShell 5 and PowerShell 7.
Get-ChildItem -LiteralPath $app -Filter '*.csproj' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } |
    ForEach-Object {
        Add-Target (Join-Path $_.DirectoryName 'bin') 'regenerable build output'
        Add-Target (Join-Path $_.DirectoryName 'obj') 'regenerable build output'
    }

# Historical source snapshots may be dirty and are therefore preserved, but their
# dependency/build directories are still reproducible. Discover only directories
# adjacent to an actual package/project manifest; never delete arbitrary bin/dist names.
foreach ($secondaryRoot in @(
    (Join-Path $resolvedRoot 'repo'),
    (Join-Path $resolvedRoot 'archives'),
    (Join-Path $resolvedRoot 'source-library\legacy-web')
)) {
    if (-not (Test-Path -LiteralPath $secondaryRoot)) { continue }
    Get-ChildItem -LiteralPath $secondaryRoot -Filter 'package.json' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](node_modules|\.git)[\\/]' } |
        ForEach-Object {
            Add-Target (Join-Path $_.DirectoryName 'node_modules') 'archived dependency directory'
            Add-Target (Join-Path $_.DirectoryName 'dist') 'archived frontend build output'
        }
    Get-ChildItem -LiteralPath $secondaryRoot -Filter '*.csproj' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' } |
        ForEach-Object {
            Add-Target (Join-Path $_.DirectoryName 'bin') 'archived .NET build output'
            Add-Target (Join-Path $_.DirectoryName 'obj') 'archived .NET build output'
        }
}

$tempRoot = Join-Path $resolvedRoot 'temp'
if (Test-Path -LiteralPath $tempRoot) {
    Get-ChildItem -LiteralPath $tempRoot -Force |
        ForEach-Object { Add-Target $_.FullName 'temporary working data' }
}

$testRoot = Join-Path $resolvedRoot 'artifacts\test-runs'
if (Test-Path -LiteralPath $testRoot) {
    @(Get-ChildItem -LiteralPath $testRoot -Directory -Force | Sort-Object LastWriteTime -Descending) |
        Select-Object -Skip $TestRunsToKeep |
        ForEach-Object { Add-Target $_.FullName "keep newest $TestRunsToKeep test runs" }
}

$deployRoot = Join-Path $resolvedRoot 'artifacts\deploy'
if (Test-Path -LiteralPath $deployRoot) {
    @(Get-ChildItem -LiteralPath $deployRoot -Directory -Force |
        Where-Object Name -Match '^[0-9a-f]{40}$' | Sort-Object LastWriteTime -Descending) |
        Select-Object -Skip $DeployDirectoriesToKeep |
        ForEach-Object { Add-Target $_.FullName "keep newest $DeployDirectoriesToKeep deployment directories" }
}

[int64]$total = 0
$targets | ForEach-Object { $total += $_.Bytes }
Write-Host "[L12 generated cleanup] mode: $(if ($Apply) { 'apply' } else { 'dry-run' })"
Write-Host "[L12 generated cleanup] reclaimable: $([math]::Round($total / 1GB, 2)) GiB"
$targets | Sort-Object Path | Format-Table Reason, @{n='MiB';e={[math]::Round($_.Bytes / 1MB, 1)}}, Path -AutoSize

if ($Apply) {
    foreach ($target in $targets) {
        $item = Get-Item -LiteralPath $target.Path -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) { Remove-Item -LiteralPath $target.Path -Force -Recurse:$item.PSIsContainer }
    }
} else { Write-Host 'No files removed. Re-run with -Apply after reviewing the exact paths.' }
