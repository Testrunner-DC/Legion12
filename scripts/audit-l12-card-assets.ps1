param(
    [string]$ProjectRoot = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$publicRoot = Join-Path $ProjectRoot 'opcgpro-vue/public'
$sources = @('cards.s1.json', 'cards.s2.json') | ForEach-Object {
    Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter $_ |
        Where-Object { $_.FullName -match 'TwelveLegions[\\/]Data' } |
        Select-Object -First 1 -ExpandProperty FullName
}

$missing = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($source in $sources) {
    $cards = Get-Content -Raw -Encoding utf8 -LiteralPath $source | ConvertFrom-Json
    foreach ($card in $cards) {
        $imageUrl = [string]$card.imageUrl
        if ([string]::IsNullOrWhiteSpace($imageUrl) -or !$imageUrl.StartsWith('/')) { continue }
        $decoded = [System.Uri]::UnescapeDataString($imageUrl.TrimStart('/')).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $assetPath = Join-Path $publicRoot $decoded
        if (!$seen.Add($assetPath)) { continue }
        if (!(Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            $missing.Add([pscustomobject]@{
                CardId = $card.id
                Name = $card.nameZh
                ImageUrl = $imageUrl
                ExpectedPath = $assetPath
            })
        }
    }
}

if ($missing.Count -gt 0) {
    $missing | Sort-Object CardId | Format-Table -AutoSize | Out-String | Write-Host
    throw "Card asset audit failed: $($missing.Count) missing file(s)."
}

Write-Host 'Card asset audit passed: every card imageUrl resolves to a file.'
