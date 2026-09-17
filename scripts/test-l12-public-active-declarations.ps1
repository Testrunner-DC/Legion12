[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File | Select-Object -First 1
    if ($null -eq $file) { throw "Missing source file: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$plans = Read-Source 'L12PublicActiveEffectPlans.cs'
$s1 = Read-Source 'L12S1FactionEffects.cs'
$s2 = Read-Source 'L12S2RemainingEffects.cs'
$s2Faction = Read-Source 'L12S2FactionEffects.cs'
$active = Read-Source 'L12ActiveAbilities.cs'
$actions = Read-Source 'L12Actions.cs'
$continuations = Read-Source 'L12EffectContinuations.cs'
$payments = Read-Source 'L12MoralePayments.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$allRuntime = $plans + "`n" + $s1 + "`n" + $s2 + "`n" + $s2Faction + "`n" +
    $active + "`n" + $actions + "`n" + $continuations + "`n" + $payments

foreach ($ability in @(
    'cleopatraGuard', 'sunGuard', 'alvidaSummon', 'lokiHeal', 'palaceExchange',
    'yomiSweep', 'yomiRecover', 'amaterasuKill', 'divinityPower',
    'asgardDraw', 'factionDrawMove', 'sunTopThree', 'isisCanopic', 'mengpoMorale', 'amaterasuReady'
)) {
    Assert-Contains $plans ('"' + $ability + '"') "Public declaration plan is missing ability $ability."
}

Assert-Contains $s2 'new L12ActivationSelectionStep { Kind = "slot"' 'Thor Hammer slot must be declared before its graveyard cost is committed.'
Assert-Contains $s2 '["ability"] = ability, ["slot"] = slot' 'Thor Hammer committed stack data must retain the declared slot.'
Assert-Contains $composite 'for (var nextIndex = current + 1; nextIndex < segments.Count; nextIndex++)' 'Composite segment queue must continue after an invalid independent segment.'
Assert-Contains $composite 'AddEvent("effect-cancelled"' 'Composite invalid-segment cancellation must preserve later independent segments.'
foreach ($cardId in @('S02-0307', 'S02-0206', 'S02-0406')) {
    Assert-Contains $composite ('["' + $cardId + '"]') "Composite hand-play declaration plan is missing card $cardId."
}
Assert-Contains $composite 'TryCommitCompositePreStackCosts' 'Composite hand-play costs must have a shared pre-stack commit hook.'
Assert-Contains $composite 'L12LibraryOps.Mill(player, 1)' 'Hela must discard the library top as a pre-stack colon cost.'
Assert-Contains $actions 'TryCommitCompositePreStackCosts(playerIndex, card, compositeDeclaration)' 'Normal hand play must commit colon costs before entering the stack.'

foreach ($legacy in @(
    'case "palace-kill":', 'case "palace-revive":',
    'case "yomi-kill3": if', 'case "yomi-kill1": if',
    's2-thor-hammer-slot', 's2-divinity-damage', 's2-divinity-recover',
    's2-divinity-hand', 'loki-heal-return',
    'asgard-draw-heal', 'asgard-heal', 'mengpo-discard', 'isis-canopic-reward',
    'amaterasu-discard', 'faction-move-card', 'faction-move-slot',
    's2-asgard-curse', 's2-fearless-assassination', 's2-tenka-mode', 's2-tenka-row'
)) {
    if ($allRuntime.IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-payment continuation returned: $legacy"
    }
}

$availability = Read-Source 'L12ActionAvailability.cs'
$usage = Read-Source 'L12ActiveUsageRules.cs'
Assert-Contains $availability 'L12ActiveUsageRules.Find(canonical, ability) is not null' 'Usage gates must opt in to explicit limits.'
Assert-Contains $active 'L12ActiveUsageRules.UsageKey(sourceInstanceId, sourceCardId, ability)' 'Shared usage keys must come from the reviewed registry.'
foreach ($source in @($active, $s1, $s2, $s2Faction)) {
    Assert-Contains $source 'RecordLimitedActiveAbilityUse(player, source, ability)' 'Active costs must use the shared usage writer.'
    if ($source.Contains('player.UsedAbilities.Add(onceKey);')) {
        throw 'A raw active once-key write bypasses explicit usage rules.'
    }
}
if ($usage -match 'Regex\.|\.Effect|EffectText') { throw 'Usage policy must not infer live rules from prose.' }
$universal = Read-Source 'L12S2UniversalEffects.cs'
Assert-Contains $universal 'UsedLimitedMasterAbilityViews(player)' 'Reset candidates must use explicit shared limits.'
Assert-Contains $universal 'ActiveAbilityUsageKey(' 'Reset settlement must use the same grouped key.'
Write-Host 'Public active declaration guard passed.'
