[CmdletBinding()]
param(
    [string]$Root = 'D:\GPT\Legion12',
    [switch]$Apply,
    [ValidateRange(2,100)][int]$TestRunsToKeep = 2,
    [ValidateRange(2,100)][int]$DeployDirectoriesToKeep = 2,
    [ValidateRange(24,8760)][int]$MinimumAgeHours = 24,
    [string]$ProductionCommit = '',
    [string]$RollbackCommit = '',
    [string[]]$PendingCommits = @(),
    [string[]]$ObsoleteVerificationDirectory = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$app = Join-Path $resolvedRoot 'app'
$targets = [Collections.Generic.List[object]]::new()
$cutoff = [DateTime]::UtcNow.AddHours(-$MinimumAgeHours)

# Validate every ancestor; never traverse reparse points during enumeration.
function Assert-PlainPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $full.StartsWith("$resolvedRoot\", [StringComparison]::OrdinalIgnoreCase)) { throw "Outside cleanup root: $full" }
    $cursor = $full
    while ($cursor) {
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked path refused: $cursor" }
        $cursor = Split-Path -Parent $cursor
    }
}
function Get-PlainTree([string]$Path) {
    Assert-PlainPath $Path
    $item = Get-Item -LiteralPath $Path -Force
    $item
    if ($item.PSIsContainer) {
        foreach ($child in @(Get-ChildItem -LiteralPath $Path -Force)) {
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked descendant refused: $($child.FullName)" }
            Get-PlainTree $child.FullName
        }
    }
}
function Get-ProcessSnapshot {
    # Fail closed if inspection is unavailable; never print process commands.
    @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $_.Name -match '^(node|dotnet|GrandUMIServer|MSBuild|testhost|tar|7z|robocopy)(\.exe)?$'
    })
}
function Test-InUse([string]$Path, $Processes) {
    foreach ($process in $Processes) {
        if ([string]::IsNullOrWhiteSpace([string]$process.CommandLine)) { return $true }
        $command = ([string]$process.CommandLine).Replace('/', '\')
        if ($command.IndexOf($Path, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
        # Relative build/archive paths cannot be reliably excluded.
        if ($process.Name -match '^(dotnet|GrandUMIServer|MSBuild|testhost|tar|7z|robocopy)') { return $true }
    }
    return $false
}
if (-not (Test-Path -LiteralPath (Join-Path $app '.git'))) { throw 'Canonical app checkout missing' }
Assert-PlainPath $app
$headCommit = & git -C $app rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve app HEAD' }
$processes = Get-ProcessSnapshot
function Add-Target([string]$Path, [string]$Reason, [bool]$RequireAge = $false) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $tree = @(Get-PlainTree $Path)
    if ($RequireAge -and @($tree | Where-Object { $_.LastWriteTimeUtc -gt $cutoff }).Count) { return }
    if (Test-InUse $Path $processes) { Write-Host "[skip active] $Path"; return }
    [long]$bytes = 0
    $tree | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $bytes += $_.Length }
    $targets.Add([pscustomobject]@{ Path=$Path; Reason=$Reason; Bytes=[long]$bytes; RequireAge=$RequireAge })
}

# No source tree, .runtime, bin/obj/dist, dependency or hot-cache deletion.
# Other worktrees and raw source libraries are never candidates.
$deployRoot = Join-Path $resolvedRoot 'artifacts\deploy'
if ($ProductionCommit -or $RollbackCommit) {
    foreach ($commit in @($ProductionCommit,$RollbackCommit) + $PendingCommits) {
        if ($commit -notmatch '^[0-9a-f]{40}$') { throw 'Full production, rollback and pending hashes required' }
    }
    $pins = @($ProductionCommit,$RollbackCommit,$headCommit) + $PendingCommits
    foreach ($commit in @($ProductionCommit,$RollbackCommit)) {
        $path = Join-Path $deployRoot "$commit\l12-release-$commit.json"
        Assert-PlainPath $path
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($manifest.commit -ne $commit) { throw "Pinned manifest mismatch: $commit" }
        foreach ($entry in @(@{Path=$manifest.releaseArchive;Hash=$manifest.releaseSha256},@{Path=$manifest.cardAssetsArchive;Hash=$manifest.cardAssetsSha256})) {
            Assert-PlainPath $entry.Path
            if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Hash) { throw "Pinned archive checksum mismatch: $commit" }
        }
    }
    Assert-PlainPath $deployRoot
    $directories = @(Get-ChildItem -LiteralPath $deployRoot -Directory -Force | Where-Object Name -Match '^[0-9a-f]{40}$' | Sort-Object LastWriteTimeUtc -Descending)
    $retained = @($directories | Select-Object -First $DeployDirectoriesToKeep | ForEach-Object Name) + $pins
    foreach ($dir in $directories) {
        if ($retained -notcontains $dir.Name) {
            # Incomplete staging/source directories require separate review.
            $allowed = @("l12-release-$($dir.Name).json", "l12-release-$($dir.Name).tar.gz")
            $entries = @(Get-PlainTree $dir.FullName | Where-Object { $_.FullName -ne $dir.FullName })
            if (@($entries | Where-Object { $_.PSIsContainer -or $allowed -notcontains $_.Name }).Count) {
                Write-Host "[skip unexpected content] $($dir.FullName)"
                continue
            }
            Add-Target $dir.FullName 'unretained deployment artifact'
        }
    }
    $referenced = @()
    foreach ($dir in $directories) {
        if (@($targets | ForEach-Object Path) -contains $dir.FullName) { continue }
        $path = Join-Path $dir.FullName "l12-release-$($dir.Name).json"
        Assert-PlainPath $path
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($manifest.commit -ne $dir.Name -or -not $manifest.cardAssetsArchive) { throw "Invalid retained manifest: $path" }
        $referenced += [IO.Path]::GetFullPath([string]$manifest.cardAssetsArchive)
    }
    foreach ($archive in @(Get-ChildItem -LiteralPath $deployRoot -File -Filter 'l12-card-assets-*.tar.gz')) {
        if ($referenced -notcontains $archive.FullName) { Add-Target $archive.FullName 'unreferenced card archive' }
    }
} else { Write-Host 'Deployment cleanup skipped: production and rollback hashes not supplied.' }

$testRoot = Join-Path $resolvedRoot 'artifacts\test-runs'
if (Test-Path -LiteralPath $testRoot) {
    Assert-PlainPath $testRoot
    @(Get-ChildItem -LiteralPath $testRoot -Directory | Sort-Object LastWriteTimeUtc -Descending) | Select-Object -Skip $TestRunsToKeep | ForEach-Object { Add-Target $_.FullName 'old test run' $true }
}
$tempRoot = Join-Path $resolvedRoot 'temp'
if (Test-Path -LiteralPath $tempRoot) {
    Assert-PlainPath $tempRoot
    Get-ChildItem -LiteralPath $tempRoot -Force | ForEach-Object { Add-Target $_.FullName 'expired temporary data' $true }
}
foreach ($name in $ObsoleteVerificationDirectory) {
    if ($name -notmatch '^verify-[a-zA-Z0-9-]+$') { throw 'Only explicitly reviewed artifacts/verify-* names accepted' }
    $path = Join-Path $resolvedRoot "artifacts\$name"
    if (Test-Path -LiteralPath $path) {
        $tree = @(Get-PlainTree $path)
        if (@($tree | Where-Object { -not $_.PSIsContainer -and $_.Name -notmatch '(\.tar\.gz|\.json)$' }).Count) { throw "Unexpected source/evidence files: $path" }
        Add-Target $path 'reviewed obsolete verification archives' $true
    }
}
[long]$total = 0
$targets | ForEach-Object { $total += $_.Bytes }
Write-Host "[cleanup] reclaimable $([math]::Round($total / 1GB, 2)) GiB; apply=$Apply"
$targets | Select-Object Reason,@{n='MiB';e={[math]::Round($_.Bytes/1MB,1)}},Path | Format-Table -AutoSize
if (-not $Apply) { Write-Host 'No files removed.'; return }
if ($targets.Count -eq 0) { Write-Host 'Nothing to remove.'; return }

# Validate the entire plan before deletion, then recheck each candidate.
$processes = Get-ProcessSnapshot
foreach ($target in $targets) {
    $tree = @(Get-PlainTree $target.Path)
    if (Test-InUse $target.Path $processes) { throw "Target became active: $($target.Path)" }
    if ($target.RequireAge -and @($tree | Where-Object LastWriteTimeUtc -GT $cutoff).Count) { throw "Target changed: $($target.Path)" }
}
$reportRoot = Join-Path $resolvedRoot 'artifacts\cleanup'
Assert-PlainPath (Split-Path -Parent $reportRoot)
if (-not (Test-Path -LiteralPath $reportRoot)) { New-Item -ItemType Directory -Path $reportRoot | Out-Null }
Assert-PlainPath $reportRoot
$report = Join-Path $reportRoot ("cleanup-{0}.json" -f [Guid]::NewGuid().ToString('N'))
$records = [Collections.Generic.List[object]]::new()
foreach ($target in $targets) {
    $tree = @(Get-PlainTree $target.Path)
    if (Test-InUse $target.Path (Get-ProcessSnapshot)) { throw "Target became active: $($target.Path)" }
    $files = @($tree | Where-Object { -not $_.PSIsContainer } | ForEach-Object { [pscustomobject]@{Path=$_.FullName;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
    $record = [pscustomobject]@{Path=$target.Path;Reason=$target.Reason;Bytes=$target.Bytes;Status='planned';Files=$files}
    $records.Add($record)
    ConvertTo-Json -InputObject @($records.ToArray()) -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
    Remove-Item -LiteralPath $target.Path -Recurse -Force
    $record.Status = 'removed'
    ConvertTo-Json -InputObject @($records.ToArray()) -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
Write-Host "Cleanup receipt: $report"
