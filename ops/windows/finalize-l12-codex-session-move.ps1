[CmdletBinding()]
param(
    [string]$SourceRoot = "$env:USERPROFILE\.codex\sessions",
    [string]$TargetRoot = "D:\GPT\Legion12\codex-session\sessions"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (Get-Process -Name 'Codex' -ErrorAction SilentlyContinue) {
    throw 'Close the Codex desktop app before running this script.'
}

$source = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
$target = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\')
if (-not $source.EndsWith('\.codex\sessions', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected Codex session source: $source"
}
if (-not $target.StartsWith('D:\GPT\Legion12\codex-session\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected Codex session target: $target"
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
function Get-RealSessionItems([string]$Path) {
    foreach ($item in Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue) {
        if ($item.PSIsContainer) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                [pscustomobject]@{ Kind = 'Junction'; Item = $item }
            } else {
                Get-RealSessionItems -Path $item.FullName
                [pscustomobject]@{ Kind = 'Directory'; Item = $item }
            }
        } else {
            [pscustomobject]@{ Kind = 'File'; Item = $item }
        }
    }
}

$items = @(Get-RealSessionItems -Path $source)
$items | Where-Object Kind -eq 'File' | ForEach-Object {
    $file = $_.Item
    $relative = $file.FullName.Substring($source.Length + 1)
    $destination = Join-Path $target $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if (Test-Path -LiteralPath $destination) {
        $same = $file.Length -eq (Get-Item -LiteralPath $destination).Length -and
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if (-not $same) { throw "Conflicting session file: $relative" }
        Remove-Item -LiteralPath $file.FullName -Force
    } else {
        Move-Item -LiteralPath $file.FullName -Destination $destination
    }
}

# Existing date junctions are removed as links only; their D-drive targets are not traversed.
$items | Where-Object Kind -eq 'Junction' | ForEach-Object { Remove-Item -LiteralPath $_.Item.FullName -Force }
$items | Where-Object Kind -eq 'Directory' | Sort-Object { $_.Item.FullName.Length } -Descending | ForEach-Object {
    if (-not (Get-ChildItem -LiteralPath $_.Item.FullName -Force -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $_.Item.FullName -Force
    }
}
if (Get-ChildItem -LiteralPath $source -Force -ErrorAction SilentlyContinue) {
    throw "Source still contains files: $source"
}
Remove-Item -LiteralPath $source -Force
New-Item -ItemType Junction -Path $source -Target $target | Out-Null
Write-Host "Codex sessions now use: $target"
