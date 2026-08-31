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

$plans = Read-Source 'L12CompositeEffectPlans.cs'
$actions = Read-Source 'L12Actions.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$s1 = Read-Source 'L12S1ExtendedEffects.cs'
$s1Faction = Read-Source 'L12S1FactionEffects.cs'
$cards = Read-Source 'L12CardEffects.cs'
$s2 = Read-Source 'L12S2UniversalEffects.cs'
$s2Faction = Read-Source 'L12S2FactionEffects.cs'
$continuations = Read-Source 'L12EffectContinuations.cs'
$allRuntime = $s1 + "`n" + $s1Faction + "`n" + $cards + "`n" + $s2 + "`n" + $s2Faction + "`n" + $continuations

foreach ($cardId in @(
    'S01-0005', 'S01-0006', 'S01-0009', 'S01-0010', 'S01-0011', 'S01-0118', 'S01-0221',
    'S01-0318', 'S01-0319', 'S02-0009', 'S02-0010', 'S02-0011', 'S02-0013',
    'S01-0014', 'S01-0015', 'S01-0119', 'S01-0419', 'S02-0306'
)) {
    Assert-Contains $plans ('["' + $cardId + '"]') "Sixth-batch hand-play declaration plan is missing card $cardId."
}

foreach ($flow in @(
    'volley-effect', 'evil-ritual-effect', 'strategic-transfer-effect', 'forged-orders-effect',
    'plague-effect', 'march-buff-effect', 'march-kill-effect', 'duat-effect',
    'valkyrie-summon-effect', 'hunt-kill-effect', 'defense-deployment-set',
    'defense-deployment-draw', 'black-lotus-disaster', 'black-lotus-morale',
    'chaotic-arrows-effect', 'holy-lock-effect',
    'ritual-draw', 'ritual-disaster', 'peace-draw', 'peace-negotiation',
    'observing-stars-reorder', 'observing-stars-morale', 'oiran-search',
    'oiran-ready-morale', 'mimir-recover-draw', 'mimir-mill'
)) {
    Assert-Contains ($plans + $allRuntime) ('"' + $flow + '"') "Sixth-batch atomic hand-play flow is missing: $flow"
}

Assert-Contains $plans 'PreStackCost: true' 'Known sixth-batch colon costs must be marked for pre-stack payment.'
Assert-Contains $plans 'case "discard-hand"' 'Composite hand play must support a shared discard-from-hand colon cost.'
Assert-Contains $plans 'case "conditional-master-damage"' 'Composite hand play must support conditional master-damage costs.'
Assert-Contains $plans 'case "grave-bottom"' 'Composite hand play must support ordered grave-to-library-bottom costs.'
Assert-Contains $plans '!next.PreStackCost && !TryPayCompositeSegmentCost' 'Prepaid costs must not be charged again between independent segments.'
Assert-Contains $actions 'TryCommitCompositePreStackCosts(playerIndex, card, compositeDeclaration)' 'Ordinary hand play must atomically commit declared colon costs before stack entry.'
Assert-Contains $actions 'HasHandPlayPlan(card.CardId)' 'Ordinary hand play must route every composite plan through the shared declaration entry.'
Assert-Contains $s2Faction 'BeginCommittedCompositeEffectDeclaration(item.Controller, card, item, "s2-limu-draw")' 'Free tactic play must enter the same composite declaration planner.'
Assert-Contains $s2Faction 'HasHandPlayPlan(card.CardId)' 'Free tactic play must recognize every shared composite plan.'
Assert-Contains $kernel 'step.Kind == "composite-defense-slot"' 'Defense Deployment must publicly declare each destination slot.'
Assert-Contains $kernel 'step.Kind == "composite-opposite-slot"' 'Forged Orders must retain its public destination in declaration data.'
Assert-Contains $plans 'CompositeFirstSegmentTargets' 'Public targets and locations must be projected onto the first stack item.'
Assert-Contains $prompts '!queuedCompositeContinuation && !IsPendingCombatDeath' 'A tactic source must remain in resolving across deferred independent segments.'
Assert-Contains $plans 'new[] { handCard, bottomCard }.Any' 'Blood Eagle must preserve a still-legal declared grave target when its peer fails.'
Assert-Contains $plans 'HandPlayPlansWithoutControllerDeclaration' 'Affected-player-only composite plans must not create an empty controller declaration prompt.'
Assert-Contains $plans '"moraleTarget"' 'Oiran Gift must retain the exact public morale target in immutable declaration data.'
Assert-Contains $plans '"disasterValue"' 'Sacrifice to Heaven must retain its public disaster delta in immutable declaration data.'
Assert-Contains $plans 'player.UsedAbilities.Add("s2-mimir-used")' 'Mimir once usage must be reserved when its declaration commits, before stack response.'

foreach ($modeLabel in @('mode:front', 'mode:back', 'mode:single', 'mode:kill', 'mode:morale', 'mode:mill')) {
    Assert-Contains $prompts ('["' + $modeLabel + '"]') "Sixth-batch player-facing label is missing: $modeLabel"
}

foreach ($legacy in @(
    'volley-mode', 'volley-single', 'evil-ritual-discard', 'strategic-return', 'strategic-buff',
    'orders-pick', 'orders-row', 'plague-lock', 'march-buff"', 'march-kill"',
    'duat-mode', 'duat-kill', 'valkyrie-card', 'hunt-return', 'hunt-kill"',
    's2-defense-deployment', 's2-black-lotus-disaster', 's2-black-lotus-morale',
    's2-chaotic-arrows', 's2-holy-lock-attach',
    '["action"] = "ritual-disaster"', '["action"] = "s2-mimir-mill"',
    '["action"] = "observing-stars-morale"', 'case "oiran-morale":',
    '是否从士气牌库追加 1 张活跃士气？', '是否将 1 张士气转为活跃？'
)) {
    if ($allRuntime.IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack hand-play prompt returned: $legacy"
    }
}

Write-Host 'Public hand-play declaration, pre-stack cost, and independent-segment guard passed.'
