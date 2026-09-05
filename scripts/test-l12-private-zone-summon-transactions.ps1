[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.runtime)[\\/]' } |
        Select-Object -First 1
    if ($null -eq $file) { throw "Missing source file: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$s1 = Read-Source 'L12S1FactionEffects.cs'
$s2 = Read-Source 'L12S2FactionEffects.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$tests = Read-Source 'AtomicReviewBatch6ERegressionTests.cs'

$tryStart = $s1.IndexOf('private bool TrySummonFromAnyPrivateZone', [StringComparison]::Ordinal)
$adapterStart = $s1.IndexOf('private void SummonFromAnyPrivateZone', [StringComparison]::Ordinal)
if ($tryStart -lt 0 -or $adapterStart -le $tryStart) {
    throw 'Private-zone summons must use one shared Try transaction before the compatibility adapter.'
}
$tryBody = $s1.Substring($tryStart, $adapterStart - $tryStart)

foreach ($contract in @(
    'if (!EmptySlots(destination).Contains(slotChoice, StringComparer.OrdinalIgnoreCase))',
    'matches.Length != 1',
    '!EffectEntryBattlefieldChoices(sourceOwner.PlayerIndex, card).Contains(destinationPlayerIndex)',
    'if (destination.Field[row][slot] is not null)',
    'sourceOwner.Hand.Remove(card)',
    'sourceOwner.Graveyard.Remove(card)',
    'sourceOwner.Library.Remove(card)',
    'destination.Field[row][slot] = card',
    'QueueNonHandEntry(destinationPlayerIndex, card'
)) {
    Assert-Contains $tryBody $contract "Private-zone summon transaction is missing: $contract"
}

$slotValidation = $tryBody.IndexOf('if (!EmptySlots(destination).Contains', [StringComparison]::Ordinal)
$uniqueValidation = $tryBody.IndexOf('matches.Length != 1', [StringComparison]::Ordinal)
$firstRemoval = $tryBody.IndexOf('sourceOwner.Hand.Remove(card)', [StringComparison]::Ordinal)
$assignment = $tryBody.IndexOf('destination.Field[row][slot] = card', [StringComparison]::Ordinal)
if ($slotValidation -lt 0 -or $uniqueValidation -lt 0 -or $firstRemoval -lt 0 -or $assignment -lt 0 -or
    $slotValidation -gt $firstRemoval -or $uniqueValidation -gt $firstRemoval -or $firstRemoval -gt $assignment) {
    throw 'Private-zone summon validation must finish before any source removal or battlefield assignment.'
}

Assert-Contains $s1 '=> _ = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, instanceId, slotChoice, tapped);' `
    'Every legacy private-zone summon caller must inherit the shared Try transaction.'
if ($s1.IndexOf('player.Field[row][slot] = card;', [StringComparison]::Ordinal) -ge 0) {
    throw 'The old unchecked private-zone battlefield assignment returned.'
}

foreach ($cardId in @(
    'S01-0208', 'S01-0210', 'S01-0305', 'S01-0308', 'S01-0309',
    'S02-0202', 'S02-0203', 'S02-0205', 'S02-0601', 'S01-0204'
)) {
    Assert-Contains $tests $cardId "Batch 6E regression coverage is missing card $cardId."
}

Assert-Contains $kernel 'var graveCards = player.Graveyard.Where(card => card.InstanceId != candidate.SourceInstanceId' `
    'Bjorn declarations must offer only cards that can pay the library-bottom cost.'
Assert-Contains $kernel 'MoveGraveToLibraryBottom(player, costs);' `
    'Bjorn must pay its four-card library-bottom cost before stack entry.'
Assert-Contains $kernel 'candidate.Data["bjornCostsPrepaid"] = "true"' `
    'Bjorn needs an immutable marker preventing duplicate cost payment at resolution.'
Assert-Contains $s1 'var costsPaid = item.Data.GetValueOrDefault("bjornCostsPrepaid") == "true"' `
    'Bjorn resolution must consume the prepaid marker instead of paying again.'
Assert-Contains $s2 'TrySummonFromAnyPrivateZone(player, player.PlayerIndex, declaredScarab' `
    'Scarab entry triggers must use the shared resolution transaction.'
Assert-Contains $s2 'TrySummonFromAnyPrivateZone(player, player.PlayerIndex, scarab.InstanceId' `
    'Golden Scarab active entry must use the shared resolution transaction.'

Write-Host 'Private-zone summon slot revalidation and prepaid-cost guard passed.'
