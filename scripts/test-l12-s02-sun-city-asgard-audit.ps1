[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6L-B source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}
function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$game = Read-Source 'L12GameEngine.cs'
$morale = Read-Source 'L12MoralePayments.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$faction = Read-Source 'L12S2FactionEffects.cs'
$remaining = Read-Source 'L12S2RemainingEffects.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$tests = Read-Source 'AtomicReviewBatch6LBRegressionTests.cs'
$audit = Read-Source 'S02-SUN-CITY-ASGARD-ABILITY-AUDIT.md'

$expectedCards = @(
    'S02-0201','S02-0202','S02-0203','S02-0204','S02-0205','S02-0206','S02-0207','S02-02M1',
    'S02-0301','S02-0302','S02-0303','S02-0304','S02-0305','S02-0306','S02-0307','S02-03M1'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S02-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missing = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpected = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 16 -or $auditCards.Count -ne 16 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Batch 6L-B audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missing -join ','), unexpected=$($unexpected -join ','))."
}
Assert-Contains $tests 'Assert.Equal(44, AuditedAbilityCounts.Values.Sum())' 'Batch 6L-B ability inventory must remain frozen at 44.'

Assert-Contains $game 'RecordFieldLegionDeparture(player, card);' 'Both public field-departure transactions must record tomb-named departures.'
if ([regex]::Matches($game, [regex]::Escape('RecordFieldLegionDeparture(player, card);')).Count -ne 2) {
    throw 'Tomb-named departures must be recorded exactly by RemoveFromField and MoveFieldCardToZone.'
}
Assert-Contains $game 'current.TombNamedLegionsLeftThisTurn = 0;' 'Tomb-paladin turn counter must reset at turn end.'
Assert-Contains $game '!card.Name.Contains(' 'Tomb-paladin counter must use the printed name condition.'

Assert-Contains $composite 'source.CardId == "S02-0207"' 'Desert Rule pre-stack cost transaction is missing.'
Assert-Contains $composite 'if (!RemoveFromField(player, discard, true' 'Desert Rule must pay field-legion costs before stacking.'
if ($faction.Contains('RemoveFromField(player, target, true')) {
    throw 'Desert Rule must not return its colon cost to StackItem resolution.'
}
Assert-Contains $faction '!TrySummonFromAnyPrivateZone(player, item.Controller' 'Desert Rule must revalidate its declared slot through the shared summon transaction.'

if ($remaining.Contains('graveyardConfirmed') -or $prompts.Contains('graveyard-active-confirm')) {
    throw 'Thor Hammer active button must not create a duplicate activation-confirmation prompt.'
}
Assert-Contains $remaining 'BeginPendingActivationSequence(playerIndex, source, ability' 'Thor Hammer must start immutable cost/slot declaration from the active button.'

if ($morale.Contains('player.Faction == "taiyangcheng" && State.ActivePlayer')) {
    throw 'A controlled Tomb Guard must not gain an extra unprinted controller-faction restriction.'
}
Assert-Contains $morale '=> State.ActivePlayer == player.PlayerIndex;' 'Tomb Guard resource legality must follow the current controller own-turn condition.'

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
if ($fixedCount -ne 4 -or $passedCount -ne 12) {
    throw "Batch 6L-B status totals drifted (passed=$passedCount, fixed=$fixedCount)."
}
Write-Host 'S02 Sun City + Asgard per-ability audit guard passed (16 cards / 44 abilities).'
