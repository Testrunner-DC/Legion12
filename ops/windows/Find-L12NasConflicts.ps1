[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$DestinationRoot,
    [string]$OutputPath = 'D:\GPT\Legion12\artifacts\nas-storage\pipeline\nas-conflicts-latest.json',
    [switch]$SuspectOnly,
    [ValidateRange(1,16)][int]$ThrottleLimit = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

$manifestPath = Assert-L12GeneratedPath -Path $ManifestPath -Purpose 'NAS conflict manifest'
$outputPath = Assert-L12GeneratedPath -Path $OutputPath -Purpose 'NAS conflict receipt'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'L12_NAS_CONFLICT_MANIFEST_MISSING' }
if ($DestinationRoot -notmatch '^\\\\[^\\]+\\[^\\]+') { throw 'L12_NAS_DESTINATION_NOT_UNC' }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$sourceRoot = [string]$manifest.sourceRoot
if ($SuspectOnly -and [string]::IsNullOrWhiteSpace($sourceRoot)) { throw 'L12_NAS_CONFLICT_SOURCE_ROOT_MISSING' }
$items = @($manifest.files)
$conflicts = @(
    $items | ForEach-Object -Parallel {
        $item = $_
        try {
            $relative = ([string]$item.path).Replace('/','\')
            $path = Join-Path $using:DestinationRoot $relative
            if ($using:SuspectOnly) {
                $sourcePath = Join-Path $using:sourceRoot $relative
                $sourceInfo = Get-Item -LiteralPath $sourcePath -Force
                $destInfo = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
                if ($null -eq $destInfo -or $destInfo.LastWriteTimeUtc -lt $sourceInfo.LastWriteTimeUtc) { return }
            }
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                [pscustomobject]@{ path = $relative; reason = 'missing'; expectedBytes = [long]$item.bytes; actualBytes = $null; expectedSha256 = [string]$item.sha256; actualSha256 = $null }
                return
            }
            $file = Get-Item -LiteralPath $path -Force
            if ([long]$file.Length -ne [long]$item.bytes) {
                [pscustomobject]@{ path = $relative; reason = 'bytes'; expectedBytes = [long]$item.bytes; actualBytes = [long]$file.Length; expectedSha256 = [string]$item.sha256; actualSha256 = $null }
                return
            }
            $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -cne [string]$item.sha256) {
                [pscustomobject]@{ path = $relative; reason = 'sha'; expectedBytes = [long]$item.bytes; actualBytes = [long]$file.Length; expectedSha256 = [string]$item.sha256; actualSha256 = $actual }
            }
        } catch {
            [pscustomobject]@{ path = [string]$item.path; reason = 'error'; expectedBytes = [long]$item.bytes; actualBytes = $null; expectedSha256 = [string]$item.sha256; actualSha256 = $_.Exception.Message }
        }
    } -ThrottleLimit $ThrottleLimit
)

$receipt = [ordered]@{
    schema = 1
    status = if ($conflicts.Count -eq 0) { 'clean' } else { 'conflicts-found' }
    manifest = $manifestPath
    destination = $DestinationRoot
    suspectOnly = [bool]$SuspectOnly
    checkedFiles = $items.Count
    conflictCount = $conflicts.Count
    conflicts = @($conflicts)
    checkedUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$partial = $outputPath + '.partial-' + [Guid]::NewGuid().ToString('N')
$receipt | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $partial -Encoding utf8
Move-Item -LiteralPath $partial -Destination $outputPath -Force
$receipt | ConvertTo-Json -Depth 8
