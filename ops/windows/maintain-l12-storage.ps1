[CmdletBinding()]
param(
    [string]$Root = "D:\GPT\Legion12",
    [switch]$Apply,
    [int]$TestRunRetentionDays = 3,
    [int]$TempRetentionHours = 24,
    [int]$DeployArtifactsToKeep = 2,
    [int]$SnapshotSetsToKeep = 2,
    [int]$LogRetentionDays = 14,
    [int]$LogsToKeep = 20
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-DirectoryBytes {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return [int64]0 }
    [int64]$bytes = 0
    Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
        ForEach-Object { $bytes += [int64]$_.Length }
    return $bytes
}

function Test-DirtyGitTree {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath (Join-Path $Path ".git"))) { return $false }
    try {
        $status = & git -c "safe.directory=$($Path.Replace('\', '/'))" -C $Path status --porcelain=v1 2>$null
        return $LASTEXITCODE -ne 0 -or [bool]$status
    }
    catch { return $true }
}

$resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if (-not (Test-Path -LiteralPath $resolvedRoot)) {
    throw "L12 storage root does not exist: $resolvedRoot"
}

$candidates = [Collections.Generic.List[object]]::new()
$skipped = [Collections.Generic.List[object]]::new()

function Add-RemovalCandidate {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Reason
    )
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($fullPath -eq $resolvedRoot -or -not $fullPath.StartsWith("$resolvedRoot\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to process a path outside the L12 storage root: $fullPath"
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    if ((Get-Item -LiteralPath $fullPath -Force).PSIsContainer -and (Test-DirtyGitTree -Path $fullPath)) {
        $skipped.Add([pscustomobject]@{ Path = $fullPath; Reason = "dirty Git worktree" })
        return
    }
    $bytes = if ((Get-Item -LiteralPath $fullPath -Force).PSIsContainer) {
        Get-DirectoryBytes -Path $fullPath
    } else { (Get-Item -LiteralPath $fullPath -Force).Length }
    $candidates.Add([pscustomobject]@{ Path = $fullPath; Reason = $Reason; Bytes = [int64]$bytes })
}

$now = Get-Date
$testRuns = Join-Path $resolvedRoot "artifacts\test-runs"
if (Test-Path -LiteralPath $testRuns) {
    Get-ChildItem -LiteralPath $testRuns -Directory -Force |
        Where-Object { $_.LastWriteTime -lt $now.AddDays(-$TestRunRetentionDays) } |
        ForEach-Object { Add-RemovalCandidate -Path $_.FullName -Reason "test output older than ${TestRunRetentionDays} days" }
}

$tempRoot = Join-Path $resolvedRoot "artifacts\temp"
if (Test-Path -LiteralPath $tempRoot) {
    Get-ChildItem -LiteralPath $tempRoot -Force |
        Where-Object { $_.LastWriteTime -lt $now.AddHours(-$TempRetentionHours) } |
        ForEach-Object { Add-RemovalCandidate -Path $_.FullName -Reason "temporary output older than ${TempRetentionHours} hours" }
}

$deployRoot = Join-Path $resolvedRoot "artifacts\deploy"
if (Test-Path -LiteralPath $deployRoot) {
    $commitArtifacts = @(Get-ChildItem -LiteralPath $deployRoot -Directory -Force |
        Where-Object { $_.Name -match '^[0-9a-f]{40}$' } |
        Sort-Object LastWriteTime -Descending)
    $commitArtifacts | Select-Object -Skip $DeployArtifactsToKeep |
        ForEach-Object { Add-RemovalCandidate -Path $_.FullName -Reason "keep newest ${DeployArtifactsToKeep} deployment artifacts" }
}

$snapshotRoot = Join-Path $resolvedRoot "migration\compact-snapshots"
if (Test-Path -LiteralPath $snapshotRoot) {
    foreach ($prefix in @("active-", "main-")) {
        @(Get-ChildItem -LiteralPath $snapshotRoot -Directory -Force |
            Where-Object { $_.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
            Sort-Object LastWriteTime -Descending) |
            Select-Object -Skip $SnapshotSetsToKeep |
            ForEach-Object { Add-RemovalCandidate -Path $_.FullName -Reason "keep newest ${SnapshotSetsToKeep} migration snapshots" }
    }
}

$logs = @(Get-ChildItem -LiteralPath (Join-Path $resolvedRoot "artifacts") -Recurse -Force -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in @('.log', '.out', '.err') } |
    Sort-Object LastWriteTime -Descending)
$logs | Where-Object { $_.LastWriteTime -lt $now.AddDays(-$LogRetentionDays) } |
    ForEach-Object { Add-RemovalCandidate -Path $_.FullName -Reason "log older than ${LogRetentionDays} days" }
$logs | Select-Object -Skip $LogsToKeep |
    ForEach-Object {
        if (-not ($candidates.Path -contains $_.FullName)) {
            Add-RemovalCandidate -Path $_.FullName -Reason "keep newest ${LogsToKeep} logs"
        }
    }

[int64]$totalBytes = 0
$candidates | ForEach-Object { $totalBytes += [int64]$_.Bytes }
Write-Host "[L12 storage] Mode: $(if ($Apply) { 'apply' } else { 'dry-run' })"
Write-Host "[L12 storage] Root: $resolvedRoot"
Write-Host "[L12 storage] Candidates: $($candidates.Count), about $([math]::Round($totalBytes / 1GB, 2)) GiB"
$candidates | Sort-Object Path | Format-Table Reason, @{ Name = 'MiB'; Expression = { [math]::Round($_.Bytes / 1MB, 1) } }, Path -AutoSize

if ($skipped.Count -gt 0) {
    Write-Warning "Skipped dirty Git worktrees:"
    $skipped | Format-Table Reason, Path -AutoSize
}

if ($Apply) {
    foreach ($candidate in $candidates) {
        $item = Get-Item -LiteralPath $candidate.Path -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) { continue }
        Remove-Item -LiteralPath $candidate.Path -Force -Recurse:$item.PSIsContainer
        Write-Host "[L12 storage] Removed: $($candidate.Path)"
    }
    Write-Host "[L12 storage] Complete. Estimated reclaimed space: $([math]::Round($totalBytes / 1GB, 2)) GiB."
} else {
    Write-Host "[L12 storage] No files were removed. Re-run with -Apply after reviewing the list."
}
