[CmdletBinding()]
param(
    [string]$SourceRoot = "$env:USERPROFILE\.codex\sessions",
    [string]$TargetRoot = "D:\GPT\CodexData\sessions",
    [string]$LegacyTargetRoot = "D:\GPT\Legion12\codex-session\sessions",
    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-NormalizedPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-PathWithin([string]$Candidate, [string]$Root) {
    $candidatePath = Get-NormalizedPath $Candidate
    $rootPath = Get-NormalizedPath $Root
    return $candidatePath.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Get-TreeEntries([string]$Root) {
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue) {
        if ($item.PSIsContainer) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                [pscustomobject]@{ Kind = 'Link'; Item = $item }
            } else {
                Get-TreeEntries -Root $item.FullName
                [pscustomobject]@{ Kind = 'Directory'; Item = $item }
            }
        } else {
            [pscustomobject]@{ Kind = 'File'; Item = $item }
        }
    }
}

function Get-RelativePath([string]$Root, [string]$Path) {
    $normalizedRoot = Get-NormalizedPath $Root
    $normalizedPath = Get-NormalizedPath $Path
    if (-not (Test-PathWithin -Candidate $normalizedPath -Root $normalizedRoot)) {
        throw "Path is outside root: $normalizedPath"
    }
    return $normalizedPath.Substring($normalizedRoot.Length).TrimStart('\')
}

function Move-TreeFiles([string]$FromRoot, [string]$ToRoot) {
    if (-not (Test-Path -LiteralPath $FromRoot)) { return }
    $entries = @(Get-TreeEntries -Root $FromRoot)
    foreach ($entry in $entries | Where-Object Kind -eq 'File') {
        $sourceFile = $entry.Item
        $relative = Get-RelativePath -Root $FromRoot -Path $sourceFile.FullName
        $destination = Join-Path $ToRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        if (Test-Path -LiteralPath $destination) {
            $destinationFile = Get-Item -LiteralPath $destination
            $same = $sourceFile.Length -eq $destinationFile.Length -and
                (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            if (-not $same) { throw "Conflicting session file: $relative" }
            Remove-Item -LiteralPath $sourceFile.FullName -Force
        } else {
            Move-Item -LiteralPath $sourceFile.FullName -Destination $destination
        }
    }
    foreach ($entry in $entries | Where-Object Kind -eq 'Directory' | Sort-Object { $_.Item.FullName.Length } -Descending) {
        if (-not (Get-ChildItem -LiteralPath $entry.Item.FullName -Force -ErrorAction SilentlyContinue)) {
            Remove-Item -LiteralPath $entry.Item.FullName -Force
        }
    }
}

$source = Get-NormalizedPath $SourceRoot
$target = Get-NormalizedPath $TargetRoot
$legacyTarget = Get-NormalizedPath $LegacyTargetRoot

if (-not $source.EndsWith('\.codex\sessions', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected Codex session source: $source"
}
if (-not (Test-PathWithin -Candidate $target -Root 'D:\GPT\CodexData')) {
    throw "Unexpected neutral Codex data target: $target"
}
if (-not (Test-PathWithin -Candidate $legacyTarget -Root 'D:\GPT\Legion12\codex-session')) {
    throw "Unexpected legacy target: $legacyTarget"
}

$sourceItem = Get-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
if ($sourceItem -and $sourceItem.LinkType -eq 'Junction') {
    if ([string]$sourceItem.Target -eq $target) {
        Write-Host "Codex sessions already use: $target"
        return
    }
    throw "Source root is already linked elsewhere: $($sourceItem.Target)"
}

$legacyItem = Get-Item -LiteralPath $legacyTarget -Force -ErrorAction SilentlyContinue
$legacyAlreadyRedirected = $legacyItem -and
    $legacyItem.LinkType -eq 'Junction' -and
    [string]$legacyItem.Target -eq $target
if ($legacyItem -and $legacyItem.LinkType -eq 'Junction' -and -not $legacyAlreadyRedirected) {
    throw "Legacy target is linked to an unexpected location: $($legacyItem.Target)"
}

$sourceEntries = if (Test-Path -LiteralPath $source) { @(Get-TreeEntries -Root $source) } else { @() }
$legacyFiles = @(if ((Test-Path -LiteralPath $legacyTarget) -and -not $legacyAlreadyRedirected) {
    Get-TreeEntries -Root $legacyTarget | Where-Object Kind -eq 'File'
})
$sourceFiles = @($sourceEntries | Where-Object Kind -eq 'File')
$sourceLinks = @($sourceEntries | Where-Object Kind -eq 'Link')

Write-Host "Source physical files: $($sourceFiles.Count)"
Write-Host "Source child links: $($sourceLinks.Count)"
Write-Host "Legacy D-drive files: $($legacyFiles.Count)"
Write-Host "Legacy compatibility link already redirected: $legacyAlreadyRedirected"
Write-Host "Final neutral target: $target"

if ($PlanOnly) {
    foreach ($link in $sourceLinks) {
        $linkTarget = [string]$link.Item.Target
        $action = if (Test-PathWithin -Candidate $linkTarget -Root $legacyTarget) {
            'Replace legacy link with migrated data'
        } else {
            'Preserve external link'
        }
        Write-Host "${action}: $($link.Item.FullName) -> $linkTarget"
    }
    return
}

if (Get-Process -Name 'Codex' -ErrorAction SilentlyContinue) {
    throw 'Close the Codex desktop app before running this script. Use -PlanOnly while Codex is open.'
}

New-Item -ItemType Directory -Path $target -Force | Out-Null

# Preserve links owned by other projects (for example HeroRush) inside the neutral
# global data root. Links into the old Legion12 session folder are replaced by the
# real files migrated below, so unrelated sessions never remain under Legion12.
foreach ($entry in $sourceLinks) {
    $link = $entry.Item
    $relative = Get-RelativePath -Root $source -Path $link.FullName
    $linkTarget = [string]$link.Target
    $destination = Join-Path $target $relative
    if (-not (Test-PathWithin -Candidate $linkTarget -Root $legacyTarget)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        if (Test-Path -LiteralPath $destination) {
            $existing = Get-Item -LiteralPath $destination -Force
            if ($existing.LinkType -ne 'Junction' -or [string]$existing.Target -ne $linkTarget) {
                throw "Conflicting preserved link: $destination"
            }
        } else {
            New-Item -ItemType Junction -Path $destination -Target $linkTarget | Out-Null
        }
    }
    Remove-Item -LiteralPath $link.FullName -Force
}

if (-not $legacyAlreadyRedirected) {
    Move-TreeFiles -FromRoot $legacyTarget -ToRoot $target
}
Move-TreeFiles -FromRoot $source -ToRoot $target

if ((Test-Path -LiteralPath $legacyTarget) -and -not $legacyAlreadyRedirected) {
    $remainingLegacy = @(Get-ChildItem -LiteralPath $legacyTarget -Force -ErrorAction SilentlyContinue)
    if ($remainingLegacy.Count -eq 0) { Remove-Item -LiteralPath $legacyTarget -Force }
}

$remainingSource = @(Get-ChildItem -LiteralPath $source -Force -ErrorAction SilentlyContinue)
if ($remainingSource.Count -ne 0) {
    throw "Source still contains entries: $source"
}
Remove-Item -LiteralPath $source -Force
New-Item -ItemType Junction -Path $source -Target $target | Out-Null
Write-Host "Codex sessions now use the neutral D-drive store: $target"
