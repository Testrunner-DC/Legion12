[CmdletBinding()]
param(
    [string]$Root = 'D:\GPT\Legion12',
    [switch]$Apply,
    [ValidateRange(2,100)][int]$TestRunsToKeep = 2,
    [ValidateRange(2,100)][int]$DeployDirectoriesToKeep = 2,
    [ValidateRange(24,8760)][int]$MinimumAgeHours = 24,
    [string]$ProductionCommit = '',
    [string]$RollbackCommit = '',
    [string]$TestCommit = '',
    [ValidateRange(2,100)][int]$VerificationRunsToKeep = 2,
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
function Add-Target([string]$Path, [string]$Reason, [bool]$RequireAge = $false, [string]$ExpectedSha256='') {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $tree = @(Get-PlainTree $Path)
    if (@($tree | Where-Object { $_.Name -match '^(PINNED|FAILED|FAILURE|\.keep)(\..*)?$' }).Count) { Write-Host "[skip protected evidence] $Path"; return }
    if ($RequireAge -and @($tree | Where-Object { $_.LastWriteTimeUtc -gt $cutoff }).Count) { return }
    if (Test-InUse $Path $processes) { Write-Host "[skip active] $Path"; return }
    [long]$bytes = 0
    $tree | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $bytes += $_.Length }
    $targets.Add([pscustomobject]@{ Path=$Path; Reason=$Reason; Bytes=[long]$bytes; RequireAge=$RequireAge; ExpectedSha256=$ExpectedSha256 })
}
function Test-CompleteRelease([string]$Path) {
    try {
        $hash=Split-Path $Path -Leaf
        $manifestPath=Join-Path $Path "l12-release-$hash.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { return $false }
        $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.schema -ne 1 -or $manifest.commit -ne $hash -or [IO.Path]::GetFullPath([string]$manifest.releaseArchive) -ne (Join-Path $Path "l12-release-$hash.tar.gz")) { return $false }
        foreach($entry in @(@{Path=$manifest.releaseArchive;Hash=$manifest.releaseSha256},@{Path=$manifest.cardAssetsArchive;Hash=$manifest.cardAssetsSha256})) {
            if ($entry.Hash -notmatch '^[0-9a-fA-F]{64}$') { return $false }
            Assert-PlainPath $entry.Path
            if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Hash) { return $false }
        }
        return $true
    } catch { return $false }
}

# No source tree, .runtime, bin/obj/dist, dependency or hot-cache deletion.
# Other worktrees and raw source libraries are never candidates.
$deployRoot = Join-Path $resolvedRoot 'artifacts\deploy'
$verifiedUnretainedCards=@{}
if ($ProductionCommit -and $RollbackCommit -and $TestCommit) {
    foreach ($commit in @($ProductionCommit,$RollbackCommit,$TestCommit) + $PendingCommits) {
        if ($commit -notmatch '^[0-9a-f]{40}$') { throw 'Full production, rollback and pending hashes required' }
    }
    $pins = @($ProductionCommit,$RollbackCommit,$TestCommit,$headCommit) + $PendingCommits
    foreach ($commit in @($ProductionCommit,$RollbackCommit,$TestCommit)) {
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
            if (-not (Test-CompleteRelease $dir.FullName)) { Write-Host "[skip incomplete/unverified release] $($dir.FullName)"; continue }
            Add-Target $dir.FullName 'unretained deployment artifact' $true
            if (@($targets | ForEach-Object Path) -contains $dir.FullName) {
                $manifest=Get-Content -LiteralPath (Join-Path $dir.FullName "l12-release-$($dir.Name).json") -Raw | ConvertFrom-Json
                $verifiedUnretainedCards[[IO.Path]::GetFullPath([string]$manifest.cardAssetsArchive)]=$manifest.cardAssetsSha256
            }
        }
    }
    $referenced = @()
    foreach ($dir in $directories) {
        if (@($targets | ForEach-Object Path) -contains $dir.FullName) { continue }
        $path = Join-Path $dir.FullName "l12-release-$($dir.Name).json"
        # Unknown/partial staging is retained, not treated as a failed cleanup.
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Assert-PlainPath $path
            try {
                $manifest=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                if ($manifest.cardAssetsArchive) { $referenced += [IO.Path]::GetFullPath([string]$manifest.cardAssetsArchive) }
            } catch { Write-Host "[retain unknown manifest] $path" }
        }
    }
    foreach ($archive in @(Get-ChildItem -LiteralPath $deployRoot -File -Filter 'l12-card-assets-*.tar.gz')) {
        if ($referenced -notcontains $archive.FullName -and $verifiedUnretainedCards.ContainsKey($archive.FullName)) { Add-Target $archive.FullName 'unreferenced verified card archive' $true $verifiedUnretainedCards[$archive.FullName] }
    }
} else { Write-Host 'Deployment cleanup skipped: full production, rollback and test hashes not supplied.' }

function Test-SuccessfulEvidence([string]$Path) {
    $tree=@(Get-PlainTree $Path)
    if (@($tree | Where-Object { $_.Name -match '^(PINNED|FAILED|FAILURE|\.keep)(\..*)?$' }).Count) { return $false }
    $timings=Join-Path $Path 'timings.json'
    if (-not (Test-Path -LiteralPath $timings -PathType Leaf)) { return $false }
    try {
        $state=Get-Content -LiteralPath $timings -Raw | ConvertFrom-Json
        if ($state.schema -ne 1 -or $state.status -ne 'success' -or $null -eq $state.stages -or @($state.stages).Count -eq 0 -or @($state.stages | Where-Object { $_.passed -isnot [bool] -or -not $_.passed }).Count) { return $false }
        foreach ($receiptFile in @($tree | Where-Object { -not $_.PSIsContainer -and $_.Name -eq 'receipt.json' })) {
            $receipt=Get-Content -LiteralPath $receiptFile.FullName -Raw | ConvertFrom-Json
            if ($receipt.schema -ne 1 -or $receipt.status -ne 'success') { return $false }
        }
        return $true
    } catch { return $false }
}

$testRoot = Join-Path $resolvedRoot 'artifacts\test-runs'
if (Test-Path -LiteralPath $testRoot) {
    Assert-PlainPath $testRoot
    @(Get-ChildItem -LiteralPath $testRoot -Directory | Where-Object { Test-SuccessfulEvidence $_.FullName } | Sort-Object LastWriteTimeUtc -Descending) | Select-Object -Skip $TestRunsToKeep | ForEach-Object { Add-Target $_.FullName 'old proven-success test run' $true }
}
$tempRoot = Join-Path $resolvedRoot 'temp'
if (Test-Path -LiteralPath $tempRoot) {
    Assert-PlainPath $tempRoot
    # Age is not proof of closure. Unowned legacy fixtures/logs stay protected.
    Get-ChildItem -LiteralPath $tempRoot -Directory -Force | Where-Object { Test-SuccessfulEvidence $_.FullName } | ForEach-Object { Add-Target $_.FullName 'expired proven-success temporary data' $true }
}
$verificationRoot=Join-Path $deployRoot 'verification-evidence'
if (Test-Path -LiteralPath $verificationRoot) {
    Assert-PlainPath $verificationRoot
    foreach ($commitDirectory in @(Get-ChildItem -LiteralPath $verificationRoot -Directory | Where-Object Name -Match '^[0-9a-f]{40}$')) {
        @(Get-ChildItem -LiteralPath $commitDirectory.FullName -Directory | Where-Object { Test-SuccessfulEvidence $_.FullName } | Sort-Object LastWriteTimeUtc -Descending) |
            Select-Object -Skip $VerificationRunsToKeep | ForEach-Object { Add-Target $_.FullName 'old proven-success verification evidence' $true }
    }
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
function Assert-TargetStillEligible($Target, $Processes) {
    $tree = @(Get-PlainTree $Target.Path)
    if (@($tree | Where-Object { $_.Name -match '^(PINNED|FAILED|FAILURE|\.keep)(\..*)?$' }).Count) { throw "Target became protected: $($Target.Path)" }
    if ($Target.Reason -match 'proven-success' -and -not (Test-SuccessfulEvidence $Target.Path)) { throw "Target no longer proves success: $($Target.Path)" }
    if ($Target.Reason -eq 'unretained deployment artifact' -and -not (Test-CompleteRelease $Target.Path)) { throw "Deployment artifact changed: $($Target.Path)" }
    if ($Target.ExpectedSha256 -and (Get-FileHash -LiteralPath $Target.Path -Algorithm SHA256).Hash -ne $Target.ExpectedSha256) { throw "Verified archive changed: $($Target.Path)" }
    if (Test-InUse $Target.Path $Processes) { throw "Target became active: $($Target.Path)" }
    if ($Target.RequireAge -and @($tree | Where-Object LastWriteTimeUtc -GT $cutoff).Count) { throw "Target changed: $($Target.Path)" }
    return $tree
}
$processes = Get-ProcessSnapshot
foreach ($target in $targets) {
    @(Assert-TargetStillEligible $target $processes) | Out-Null
}
$reportRoot = Join-Path $resolvedRoot 'artifacts\cleanup'
Assert-PlainPath (Split-Path -Parent $reportRoot)
if (-not (Test-Path -LiteralPath $reportRoot)) { New-Item -ItemType Directory -Path $reportRoot | Out-Null }
Assert-PlainPath $reportRoot
$report = Join-Path $reportRoot ("cleanup-{0}.json" -f [Guid]::NewGuid().ToString('N'))
$records = [Collections.Generic.List[object]]::new()
foreach ($target in $targets) {
    $tree = @(Assert-TargetStillEligible $target (Get-ProcessSnapshot))
    $files = @($tree | Where-Object { -not $_.PSIsContainer } | ForEach-Object { [pscustomobject]@{Path=$_.FullName;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
    $record = [pscustomobject]@{Path=$target.Path;Reason=$target.Reason;Bytes=$target.Bytes;Status='planned';Files=$files}
    $records.Add($record)
    ConvertTo-Json -InputObject @($records.ToArray()) -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
    @(Assert-TargetStillEligible $target (Get-ProcessSnapshot)) | Out-Null
    Remove-Item -LiteralPath $target.Path -Recurse -Force
    $record.Status = 'removed'
    ConvertTo-Json -InputObject @($records.ToArray()) -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
}
Write-Host "Cleanup receipt: $report"
