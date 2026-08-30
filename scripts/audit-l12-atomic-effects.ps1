param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$RequireZero,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/l12-card-runtime-evidence.ps1')
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
$runtimeFiles = @(Get-ChildItem -LiteralPath $sourcePath -Filter 'L12*.cs' -File | Where-Object {
    ($_.Name -notin @('AtomicEffects.cs', 'L12RuntimeEffectRoutes.cs', 'L12CompositeEffectPlans.cs')) -and
        ($_.Name -notmatch '^L12StructuredCardRules(?:\.|$)')
})
$source = ($runtimeFiles | Get-Content -Raw) -join "`n"
$matches = [regex]::Matches($source, 'case\s+"(?<id>S\d{2}-[A-Za-z0-9]+)"')
$uniqueIds = @($matches | ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$effectCards = @($cards | Where-Object { -not [string]::IsNullOrWhiteSpace($_.effect) })

$branchPatterns = [ordered]@{
    cardCase = 'case\s+"S\d{2}-[A-Za-z0-9]+"'
    cardConditional = '(?m)^\s*(?:if|else\s+if)\s*\([^\r\n]*(?:CardId|SourceCardId)[^\r\n]*"S\d{2}-[A-Za-z0-9]+"'
    cardSwitchArm = '(?m)^\s*"S\d{2}-[A-Za-z0-9]+"\s*=>'
    effectTextInference = '(?:EffectText|\.Effect)\??\.Contains\s*\('
}
$atomicSource = [System.IO.File]::ReadAllText((Join-Path $sourcePath 'AtomicEffects.cs'), [System.Text.Encoding]::UTF8)
$routeSource = [System.IO.File]::ReadAllText((Join-Path $sourcePath 'L12RuntimeEffectRoutes.cs'), [System.Text.Encoding]::UTF8)
$fineProgramMatches = [regex]::Matches($atomicSource,
    'Program\("(?<id>S\d{2}-[A-Za-z0-9]+)"\s*,\s*"(?<trigger>[^"]+)"')
$compositeRouteMatches = [regex]::Matches($routeSource,
    'new\("(?<id>S\d{2}-[A-Za-z0-9]+)"\s*,\s*"(?<trigger>[^"]+)"')
$fineCardIds = @($fineProgramMatches | ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$compositeCardIds = @($compositeRouteMatches | ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$catalogOnlyCardIds = @($cards | Where-Object {
    $fineCardIds -notcontains $_.id -and $compositeCardIds -notcontains $_.id
} | ForEach-Object { $_.id } | Sort-Object -Unique)
$runtimeEvidence = Get-L12CardRuntimeEvidence -ProjectRoot $ProjectRoot -Cards $cards
$unroutedRuntimeCardIds = @($catalogOnlyCardIds | Where-Object {
    $runtimeEvidence[$_].Sources.Count -gt 0
} | Sort-Object -Unique)
$noRuntimeEntranceCardIds = @($catalogOnlyCardIds | Where-Object {
    $runtimeEvidence[$_].Sources.Count -eq 0
} | Sort-Object -Unique)
$unroutedWithTestEvidenceCardIds = @($catalogOnlyCardIds | Where-Object {
    $runtimeEvidence[$_].Tests.Count -gt 0
} | Sort-Object -Unique)
$branchCounts = [ordered]@{}
$details = New-Object System.Collections.Generic.List[object]
foreach ($file in $runtimeFiles) {
    $fileText = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    foreach ($entry in $branchPatterns.GetEnumerator()) {
        $patternMatches = [regex]::Matches($fileText, $entry.Value)
        $existingCount = 0
        if ($branchCounts.Contains($entry.Key)) {
            $existingCount = [int]$branchCounts[$entry.Key]
        }
        $branchCounts[$entry.Key] = $existingCount + $patternMatches.Count
        foreach ($match in $patternMatches) {
            $line = ([regex]::Matches($fileText.Substring(0, $match.Index), "`n")).Count + 1
            $details.Add([ordered]@{
                category = $entry.Key
                file = $file.FullName.Substring($ProjectRoot.TrimEnd('\').Length + 1).Replace('\', '/')
                line = $line
                excerpt = ($match.Value -replace '\s+', ' ').Trim()
            })
        }
    }
}
$legacyDispatchTotal = ($branchCounts.Values | Measure-Object -Sum).Sum

$failures = @()
if ($cards.Count -ne [int]$baseline.catalogCards) {
    $failures += "Catalog size changed from $($baseline.catalogCards) to $($cards.Count); update the atomic baseline explicitly."
}
if ($matches.Count -gt [int]$baseline.legacyCardCaseOccurrences) {
    $failures += "Legacy card case count grew from $($baseline.legacyCardCaseOccurrences) to $($matches.Count)."
}
foreach ($entry in $branchCounts.GetEnumerator()) {
    $baselineName = "legacy$([char]::ToUpperInvariant($entry.Key[0]))$($entry.Key.Substring(1))Occurrences"
    if ($baseline.PSObject.Properties.Name -contains $baselineName -and [int]$entry.Value -gt [int]$baseline.$baselineName) {
        $failures += "$($entry.Key) count grew from $($baseline.$baselineName) to $($entry.Value)."
    }
}
if (($baseline.PSObject.Properties.Name -contains 'fineGrainedVerifiedCards') -and
    ($fineCardIds.Count -lt [int]$baseline.fineGrainedVerifiedCards)) {
    $failures += "Fine-grained verified card coverage fell from $($baseline.fineGrainedVerifiedCards) to $($fineCardIds.Count)."
}
if (($baseline.PSObject.Properties.Name -contains 'compositeTransitionRoutes') -and
    ($compositeRouteMatches.Count -gt [int]$baseline.compositeTransitionRoutes)) {
    $failures += "Composite transition routes grew from $($baseline.compositeTransitionRoutes) to $($compositeRouteMatches.Count)."
}
if (($baseline.PSObject.Properties.Name -contains 'catalogOnlyPendingCards') -and
    ($catalogOnlyCardIds.Count -gt [int]$baseline.catalogOnlyPendingCards)) {
    $failures += "Catalog-only pending card count grew from $($baseline.catalogOnlyPendingCards) to $($catalogOnlyCardIds.Count)."
}
if (($baseline.PSObject.Properties.Name -contains 'noRuntimeEntranceCards') -and
    ($noRuntimeEntranceCardIds.Count -gt [int]$baseline.noRuntimeEntranceCards)) {
    $failures += "Cards without an authoritative runtime entrance grew from $($baseline.noRuntimeEntranceCards) to $($noRuntimeEntranceCardIds.Count)."
}
if ($RequireZero -and $matches.Count -ne 0) {
    $failures += "Runtime card-id case dispatch is not zero: $($matches.Count) occurrence(s)."
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
    identityReferenceAndInferenceTotal = $legacyDispatchTotal
    branchCounts = $branchCounts
    fineGrainedVerifiedPrograms = $fineProgramMatches.Count
    fineGrainedVerifiedCards = $fineCardIds.Count
    compositeTransitionRoutes = $compositeRouteMatches.Count
    compositeTransitionCards = $compositeCardIds.Count
    catalogOnlyPendingCards = $catalogOnlyCardIds.Count
    unroutedWithAuthoritativeRuntimeCards = $unroutedRuntimeCardIds.Count
    noRuntimeEntranceCards = $noRuntimeEntranceCardIds.Count
    unroutedWithTestEvidenceCards = $unroutedWithTestEvidenceCardIds.Count
    noRuntimeEntranceCardIds = $noRuntimeEntranceCardIds
}
$summary | ConvertTo-Json
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $report = [ordered]@{ generatedAt = (Get-Date).ToString('o'); summary = $summary; occurrences = $details }
    $target = if ([System.IO.Path]::IsPathRooted($ReportPath)) { $ReportPath } else { Join-Path $ProjectRoot $ReportPath }
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
    [System.IO.File]::WriteAllText($target, ($report | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
}
if ($failures.Count) {
    Write-Error ("Atomic effect audit failed:`n- " + ($failures -join "`n- "))
}
Write-Host 'Atomic effect audit passed.'
