param(
    [string]$ArchiveDirectory = 'D:\L12-assets\original',
    [string]$ProjectRoot = '',
    [switch]$Refresh
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
}
if ($ArchiveDirectory.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Card archive must not be stored on drive C.'
}

$catalogRoot = (Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter 'cards.s1.json' -File |
    Where-Object { $_.DirectoryName -like '*TwelveLegions*Data' } |
    Select-Object -First 1).DirectoryName
if ([string]::IsNullOrWhiteSpace($catalogRoot)) { throw 'Unable to locate the L12 card catalog.' }
$publicRoot = Join-Path $ProjectRoot 'opcgpro-vue\public'
$cards = [System.Collections.Generic.List[object]]::new()
foreach ($catalogName in @('cards.s1.json', 'cards.s2.json')) {
    $catalogCards = Get-Content -Raw -Encoding utf8 -LiteralPath (Join-Path $catalogRoot $catalogName) | ConvertFrom-Json
    foreach ($catalogCard in $catalogCards) { $cards.Add($catalogCard) }
}

New-Item -ItemType Directory -Force -Path $ArchiveDirectory | Out-Null
$downloaded = 0
$copied = 0
$retained = 0
$failures = [System.Collections.Generic.List[object]]::new()
$current = 0

foreach ($card in $cards) {
    $current++
    $destination = Join-Path $ArchiveDirectory "$($card.id).png"
    if (!$Refresh -and (Test-Path -LiteralPath $destination -PathType Leaf) -and
        (Get-Item -LiteralPath $destination).Length -gt 0) {
        $retained++
        continue
    }

    $imageUrl = [string]$card.imageUrl
    try {
        if ($imageUrl.StartsWith('/', [System.StringComparison]::Ordinal)) {
            $relative = [System.Uri]::UnescapeDataString($imageUrl.TrimStart('/')).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $source = Join-Path $publicRoot $relative
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Local source not found: $source" }
            Copy-Item -LiteralPath $source -Destination $destination -Force
            $copied++
        }
        elseif ($imageUrl.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase)) {
            $temporary = "$destination.partial"
            try {
                Write-Host "[$current/$($cards.Count)] Downloading $($card.id) $($card.nameZh)"
                & curl.exe --fail --location --silent --show-error --retry 2 --connect-timeout 15 --max-time 90 `
                    --user-agent 'Legion12-CardArchive/1.0' --output $temporary $imageUrl
                if ($LASTEXITCODE -ne 0) { throw "curl failed with exit code $LASTEXITCODE" }
                if (!(Test-Path -LiteralPath $temporary -PathType Leaf) -or (Get-Item -LiteralPath $temporary).Length -eq 0) {
                    throw 'Downloaded file is empty.'
                }
                Move-Item -LiteralPath $temporary -Destination $destination -Force
                $downloaded++
            }
            finally {
                if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
            }
        }
        else { throw "Unsupported image URL: $imageUrl" }
    }
    catch {
        $failures.Add([pscustomobject]@{ CardId = $card.id; Name = $card.nameZh; Error = $_.Exception.Message })
    }
}

Write-Host "Archive ready: retained=$retained copied=$copied downloaded=$downloaded total=$($cards.Count)"
if ($failures.Count -gt 0) {
    $failures | Format-Table -AutoSize | Out-String | Write-Host
    throw "Card archive incomplete: $($failures.Count) source(s) failed."
}
