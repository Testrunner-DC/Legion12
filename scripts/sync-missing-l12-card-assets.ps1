param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [string]$ProjectRoot = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$publicRoot = Join-Path $ProjectRoot 'opcgpro-vue/public'
$sources = @('cards.s1.json', 'cards.s2.json', 'cards.st.json') | ForEach-Object {
    Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter $_ |
        Where-Object { $_.FullName -match 'TwelveLegions[\\/]Data' } |
        Select-Object -First 1 -ExpandProperty FullName
}
$sourceFiles = Get-ChildItem -LiteralPath $SourceDirectory -File
$copied = 0
$unresolved = [System.Collections.Generic.List[object]]::new()

foreach ($source in $sources) {
    $cards = Get-Content -Raw -Encoding utf8 -LiteralPath $source | ConvertFrom-Json
    foreach ($card in $cards) {
        $imageUrl = [string]$card.imageUrl
        if ([string]::IsNullOrWhiteSpace($imageUrl) -or !$imageUrl.StartsWith('/')) { continue }
        $decoded = [System.Uri]::UnescapeDataString($imageUrl.TrimStart('/')).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $destination = Join-Path $publicRoot $decoded
        if (Test-Path -LiteralPath $destination -PathType Leaf) { continue }
        $prefix = "$($card.id)-"
        $candidate = $sourceFiles | Where-Object { $_.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($null -eq $candidate) {
            $unresolved.Add([pscustomobject]@{ CardId = $card.id; Name = $card.nameZh; Destination = $destination })
            continue
        }
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $candidate.FullName -Destination $destination
        $copied++
    }
}

Write-Host "Copied $copied missing card asset(s)."
if ($unresolved.Count -gt 0) {
    $unresolved | Sort-Object CardId | Format-Table -AutoSize | Out-String | Write-Host
    throw "Asset sync incomplete: $($unresolved.Count) card source(s) unresolved."
}
