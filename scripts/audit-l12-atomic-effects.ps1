param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$baselinePath = Join-Path $ProjectRoot 'docs/l12/ATOMIC-EFFECT-BASELINE.json'
$serverProject = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter 'GrandUMIServer.csproj' -File | Select-Object -First 1
if ($null -eq $serverProject) { throw 'GrandUMIServer.csproj not found.' }
$sourcePath = Join-Path $serverProject.DirectoryName 'TwelveLegions'
$dataPath = Join-Path $sourcePath 'Data'
$baseline = [System.IO.File]::ReadAllText($baselinePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$cards = New-Object System.Collections.Generic.List[object]
foreach ($fileName in @('cards.s1.json', 'cards.s2.json')) {
    $decoded = [System.IO.File]::ReadAllText((Join-Path $dataPath $fileName), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($card in $decoded) { $cards.Add($card) }
}
$source = (Get-ChildItem -LiteralPath $sourcePath -Filter 'L12*.cs' -File | Get-Content -Raw) -join "`n"
$matches = [regex]::Matches($source, 'case\s+"(?<id>S\d{2}-[A-Za-z0-9]+)"')
$uniqueIds = @($matches | ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$effectCards = @($cards | Where-Object { -not [string]::IsNullOrWhiteSpace($_.effect) })

$failures = @()
if ($cards.Count -ne [int]$baseline.catalogCards) {
    $failures += "Catalog size changed from $($baseline.catalogCards) to $($cards.Count); update the atomic baseline explicitly."
}
if ($matches.Count -gt [int]$baseline.legacyCardCaseOccurrences) {
    $failures += "Legacy card case count grew from $($baseline.legacyCardCaseOccurrences) to $($matches.Count)."
}
if (-not (Test-Path -LiteralPath (Join-Path $dataPath 'effects/atoms.schema.json'))) {
    $failures += 'Missing Data/effects/atoms.schema.json.'
}
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'AtomicEffects.cs'))) {
    $failures += 'Missing server atom registry.'
}

$summary = [ordered]@{
    catalogCards = $cards.Count
    cardsWithEffectText = $effectCards.Count
    legacyCardCaseOccurrences = $matches.Count
    legacyUniqueCardIds = $uniqueIds.Count
    baselineLegacyCaseOccurrences = [int]$baseline.legacyCardCaseOccurrences
}
$summary | ConvertTo-Json
if ($failures.Count) {
    Write-Error ("Atomic effect audit failed:`n- " + ($failures -join "`n- "))
}
Write-Host 'Atomic effect audit passed.'
