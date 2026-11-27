[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$GroupPath,
    [ValidateSet('Plan','Apply')][string]$Mode = 'Plan',
    [switch]$Pinned,
    [switch]$SkipExisting,
    [string]$ReceiptDirectory = 'D:\GPT\Legion12\artifacts\nas-storage\legacy-governance'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$groupFull = [IO.Path]::GetFullPath($GroupPath).TrimEnd('\')
if (-not $rootFull.StartsWith('\\')) { throw 'L12_NAS_LEGACY_ROOT_NOT_UNC' }
if (-not $groupFull.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'L12_NAS_LEGACY_GROUP_OUTSIDE_ROOT' }
if ($groupFull -eq (Join-Path $rootFull 'groups')) { throw 'L12_NAS_LEGACY_GROUP_IS_GROUPS_ROOT' }

$manifestPath = Join-Path $groupFull 'nas-group.json'
if ($SkipExisting -and (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    return [pscustomobject]@{ status = 'skipped-existing'; group = $groupFull }
}

$files = @(Get-ChildItem -LiteralPath $groupFull -Recurse -File -Force | Where-Object { $_.FullName -ne $manifestPath })
if ($files.Count -lt 1) { throw 'L12_NAS_LEGACY_GROUP_EMPTY' }

$sw = [Diagnostics.Stopwatch]::StartNew()
$list = [Collections.Generic.List[object]]::new()
$newest = [datetime]::MinValue
$total = [long]0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($groupFull.Length + 1).Replace('\', '/')
    $list.Add([pscustomobject][ordered]@{ path = $rel; bytes = [long]$f.Length; sha256 = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
    $total += [long]$f.Length
    if ($f.LastWriteTimeUtc -gt $newest) { $newest = $f.LastWriteTimeUtc }
}
$sw.Stop()

$manifest = [ordered]@{
    schema = 1
    status = 'verified'
    cleanupEligible = $true
    layout = 'legacy-in-place'
    group = (Split-Path -Leaf $groupFull)
    pinned = [bool]$Pinned
    completedUtc = $newest.ToString('o')
    fileCount = $list.Count
    files = @($list)
    governedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    governedSource = $groupFull
    governedBy = 'New-L12NasLegacyGroupManifest.ps1'
}
if ($Mode -eq 'Apply') {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

if (-not (Test-Path -LiteralPath $ReceiptDirectory -PathType Container)) { New-Item -ItemType Directory -Path $ReceiptDirectory -Force | Out-Null }
$receipt = [ordered]@{
    schema = 1; status = 'manifest-written'; mode = $Mode; group = $manifest.group; groupPath = $groupFull
    pinned = $manifest.pinned; fileCount = $list.Count; bytes = $total; completedUtc = $manifest.completedUtc
    hashSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); manifestPath = if ($Mode -eq 'Apply') { $manifestPath } else { $null }
    createdUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$receiptPath = Join-Path $ReceiptDirectory ('legacy-manifest-' + [Guid]::NewGuid().ToString('N') + '.json')
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8
return [pscustomobject]@{ status = $receipt.status; group = $manifest.group; files = $list.Count; bytes = $total; seconds = $receipt.hashSeconds; receiptPath = $receiptPath }
