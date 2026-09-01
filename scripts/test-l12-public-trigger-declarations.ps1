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

$plans = Read-Source 'L12PublicTriggerEffectPlans.cs'
$trialAdvancePlans = Read-Source 'L12TrialAdvanceEffectPlans.cs'
$attackPlans = Read-Source 'L12AttackPublicTriggerPlans.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$counter = Read-Source 'L12S2CounterTactics.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$actions = Read-Source 'L12Actions.cs'
$gm = Read-Source 'L12GmCommands.cs'
$authority = Read-Source 'L12AuthorityEvents.cs'
$zones = Read-Source 'L12AuthoritativeCardZones.cs'
$cardEffects = Read-Source 'L12CardEffects.cs'
$continuations = Read-Source 'L12EffectContinuations.cs'
$runtimeDirectory = (Get-ChildItem -LiteralPath $ProjectRoot -Filter 'L12PublicTriggerEffectPlans.cs' -Recurse -File |
    Select-Object -First 1).Directory.FullName
$allRuntime = (@(Get-ChildItem -LiteralPath $runtimeDirectory -Filter '*.cs' -File | ForEach-Object {
    [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
}) -join "`n")

foreach ($cardId in @(
    'S02-04M1', 'S02-0523', 'S01-02M3', 'S02-02M1', 'S02-01S1',
    'S01-0105', 'S01-0207', 'S01-0208', 'S01-0309', 'S01-0021', 'S01-0213',
    'S01-0223', 'S01-0320', 'S01-0224', 'S02-0202', 'S02-0203', 'S02-0205', 'S01-0206', 'S01-0407',
    'S01-0204', 'S01-0414', 'S01-0417',
    'S02-0304', 'S02-0305', 'S02-05M1', 'S02-06M1', 'S02-0102', 'S02-06S4'
)) {
    Assert-Contains $plans $cardId "Public trigger declaration plan is missing card $cardId."
}

foreach ($cardId in @(
    'S01-0104', 'S01-0106', 'S01-0203', 'S01-0208', 'S01-0301', 'S01-0306', 'S01-0311',
    'S01-0402', 'S01-0405', 'S01-0406', 'S01-0408', 'S01-0413', 'S01-0416', 'S02-0103',
    'S02-0509', 'S02-0511', 'S02-0517', 'S02-0519', 'S02-0605', 'S02-0606', 'S02-0607',
    'S02-0608', 'S02-0612', 'S02-0617'
)) {
    Assert-Contains $attackPlans ('["' + $cardId + '"]') "Attack public trigger plan is missing card $cardId."
}
Assert-Contains $attackPlans 'AttackPublicTriggerPlans' 'Attack declarations need a shared data-driven plan table.'
Assert-Contains $attackPlans 'TryQueueAttackPublicTriggerCandidates' 'Attack triggers must share one candidate entry.'
Assert-Contains $attackPlans 'CreateTriggerCandidate(controller, source, trigger, candidateText, candidateData, source)' 'Attack candidates must retain a last-known source snapshot.'
Assert-Contains $attackPlans 'PayAttackPublicCost(candidate, activation, plan, player, source, costIds)' 'Attack colon costs must commit before stack entry.'
Assert-Contains $attackPlans 'PublicLegions(player).Select(card => card.InstanceId), requiredChoice: required)' 'Menes must be allowed to declare itself as the legion discard cost.'
Assert-Contains $attackPlans '"discard-own-legion" => PublicLegions(player).Any(),' 'Menes must remain activatable when it is the only friendly legion.'
if ($attackPlans.IndexOf('sacrifice.InstanceId == candidate.SourceInstanceId', [StringComparison]::Ordinal) -ge 0) {
    throw 'Menes self-discard cost must not be rejected during declaration commit.'
}
Assert-Contains $attackPlans '["attackPlan"] = "gawain-buff"' 'Gawain rune consumption and buff must remain independent stack segments.'
Assert-Contains $attackPlans 'Candidate("richard-defense"' 'Richard defense tax must be its own same-time candidate.'
Assert-Contains $attackPlans 'Candidate("richard-squires"' 'Richard squire cost/buff must be its own same-time candidate.'
Assert-Contains $attackPlans 'Candidate("robin-rune"' 'Robin rune gain must be its own required candidate.'
Assert-Contains $attackPlans 'Candidate("robin-draw"' 'Robin draw must be its own optional candidate.'

Assert-Contains $plans 'HasPublicTriggerDeclarationPlan' 'Public trigger plans need a shared route predicate.'
Assert-Contains $plans 'QueueOrPushTriggeredEffect' 'Direct trigger entry points must join the TriggerBatch declaration route.'
Assert-Contains $actions 'QueueOrPushTriggeredEffect' 'Normal hand entry and attack triggers must not bypass public declarations.'
Assert-Contains $gm 'QueueOrPushTriggeredEffect' 'GM effect entry must not bypass public declarations.'
Assert-Contains $authority 'QueueOrPushTriggeredEffect' 'Non-hand effect entry must not bypass public declarations.'
Assert-Contains $kernel 'TryBeginPublicTriggerDeclaration(candidate, source)' 'Trigger candidates must enter the public declaration planner.'
Assert-Contains $kernel 'TryCompletePublicTriggerDeclaration(candidate, activation)' 'Trigger declarations need a shared atomic completion hook.'
Assert-Contains $plans 'HasTrialAdvanceTriggerDeclarationPlan(cardId, trigger, data)' 'Trial advances must enter the shared public trigger route.'
Assert-Contains $plans 'TryBeginTrialAdvanceTriggerDeclaration(candidate, source)' 'Trial trigger choices must be declared before stack entry.'
Assert-Contains $plans 'TryCompleteTrialAdvanceTriggerDeclaration(candidate, activation)' 'Trial trigger costs must commit before stack entry.'
foreach ($cardId in @('S02-0602', 'S02-0604', 'S02-0610', 'S02-0614', 'S02-06M2', 'S02-06D1')) {
    Assert-Contains $trialAdvancePlans $cardId "Trial advance event plan is missing card $cardId."
}
foreach ($contract in @(
    'BeginTrialAdvanceActivation', 'TryCommitTrialAdvanceActivation', 'TryResolveTrialAdvanceEffect',
    '["trialAdvanceEvent"] = "true"', 'QueueFinnReadyAfterTrial', 'QueueAvalonTurnStart',
    'player.SpecialZones.Runes--', 'source.Tapped = true', 'FinishStackItem(item)'
)) {
    Assert-Contains $trialAdvancePlans $contract "Trial advance event contract is missing: $contract"
}
if ($trialAdvancePlans.IndexOf('TrialCompleted = true', [StringComparison]::Ordinal) -ge 0 -or
    $trialAdvancePlans.IndexOf('QueueCompletedTrialTriggerBatch', [StringComparison]::Ordinal) -ge 0) {
    throw 'Advancing a trial to 8 must not automatically complete it or publish the Batch 6B completion event.'
}
Assert-Contains $plans 'TryConsumeSelectedResources(player, 1' 'Tsukuyomi must commit its declared resource before stack entry.'
Assert-Contains $plans 'ReturnSelectedMoraleById(player, [costId], 1)' 'Liu Bei must return the declared morale before stack entry.'
Assert-Contains $plans 'DamageMaster(candidate.Controller, 1,' 'Brynhild must pay the known master-damage cost before stack entry.'
Assert-Contains $plans 'candidate.Data["preserveIndependentStack"] = "true"' 'Immortal Gift must preserve its independent draw segment when summon declaration is absent.'
Assert-Contains $plans 'activation.DeclaredValues["entryCard"] = ["mode:none"]' 'Immortal Gift invalid summon segment must cancel independently.'
Assert-Contains $plans 'Batch6GAPublicTriggerPlan' 'Batch 6G-A triggers need one shared data-driven declaration route.'
Assert-Contains $plans 'margaretMasterDamage' 'Margaret damage trigger must predeclare and prepay its rest cost.'
Assert-Contains $allRuntime 'margaret-heal-lock' 'Margaret heal and heal-lock sentences must remain independent stack segments.'
Assert-Contains $plans 'cleanupReservation' 'Optional once-per-turn triggers must reserve pending state before player declaration.'
Assert-Contains $plans 'player.UsedAbilities.Add(onceKey)' 'Committed optional triggers must consume their once before stack entry.'
Assert-Contains $plans 'card.Tapped && !card.IsGodPower' 'Artemis must declare an exact rested ordinary morale target.'
Assert-Contains $kernel 'SourceSnapshot = CaptureLastKnownSourceSnapshot(sourceSnapshot ?? card)' 'Every generated trigger candidate must carry a last-known source snapshot.'
Assert-Contains $kernel 'FindAuthoritativeCard(candidate.SourceInstanceId)' 'Trigger declarations must resolve sources through the internal authoritative lookup.'
Assert-Contains $plans 'owner-unused-slot' 'Tomb Construct must declare owner battlefield slots before stack entry.'
Assert-Contains $plans 'declaredGuardOwners' 'Tomb Construct must retain each attached guard owner with its declared slot.'
Assert-Contains $zones 'TryMoveAuthoritativeCardToOwnerLibraryTop' 'Return-to-library effects need one owner-and-instance zone transaction.'
foreach ($zoneName in @('field', 'relic', 'extra', 'god-power', 'trial', 'canopic', 'resolving', 'hand', 'library', 'graveyard', 'removed')) {
    Assert-Contains $zones ('"' + $zoneName + '"') "Authoritative source lookup is missing zone $zoneName."
}
Assert-Contains $zones 'locations.Count != 1' 'Authority-zone transfer must reject missing or duplicate real instances.'
Assert-Contains $zones 'QueueReturnedToLibraryTopTrigger' 'Returning Katsura must create an independent trigger candidate.'
Assert-Contains $cardEffects 'case "return-library-top"' 'Returned-to-library trigger needs its own stack dispatch.'
if ($prompts.IndexOf('FindAuthoritativeCard', [StringComparison]::Ordinal) -ge 0) {
    throw 'Client prompt lookup must not expose the internal authoritative source query.'
}
if ($zones.IndexOf('Library.Insert(0, sourceSnapshot)', [StringComparison]::Ordinal) -ge 0) {
    throw 'A last-known snapshot must never be inserted as a real zone card.'
}

Assert-Contains $counter 'var revealed = player.Library[0];' 'Cosmos Yin must inspect the hidden library top only during legal resolution.'
Assert-Contains $counter 'CreateDelayedPublicResolutionPrompt(item' 'Cosmos Yin must declare its public target only after the hidden reveal.'
Assert-Contains $plans 'data["declarationTiming"] = "post-hidden-reveal"' 'Delayed public resolution prompts need an explicit post-reveal timing marker.'
Assert-Contains $prompts 'or "S02-0106")' 'Cosmos Yin responses must route to their own effect instead of generic negate handling.'
if ($plans.IndexOf('"S02-0106"', [StringComparison]::Ordinal) -ge 0) {
    throw 'Cosmos Yin must not be placed in the pre-reveal public trigger planner.'
}
foreach ($hiddenCardId in @('S01-0103', 'S02-0401', 'S02-0403')) {
    if ($plans.IndexOf('"' + $hiddenCardId + '"', [StringComparison]::Ordinal) -ge 0) {
        throw "Hidden-information effect $hiddenCardId must remain outside the pre-reveal public trigger planner."
    }
}

foreach ($legacy in @(
    's2-tsukuyomi-pay', 's2-tsukuyomi-target', 's2-tsukuyomi-slot', 's2-tsukuyomi-ready',
    's2-trojan-confirm', 's2-trojan-slot', 'medjed-damage-response',
    's2-nephthys-own-death', 's2-nephthys-scarab-slot',
    's2-xiaotian-morale', 's2-xiaotian-slot', 'liubei-card', 'liubei-summon', 'liubei-slot',
    'brynhild-sigurd', 'regency-card', 'regency-slot', 'kaba-summon', 'kaba-slot',
    'saladin-move', 'saladin-slot', 'ryoma-pick', 'ryoma-slot', 's2-scarab-enter-slot',
    'katsura-return', 'katsura-ready-morale', 'kusanagi-return-top'
)) {
    if ($allRuntime.IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack public trigger continuation returned: $legacy"
    }
}

foreach ($legacyAttackPrompt in @(
    'hanxin-attack', 'guanyu-attack', 'menes-sacrifice', 'ay-pay', 'ay-buff', 'beowulf-buff',
    'olaf-strong', 'gustav-attack-choice', 'gustav-attack-return', 'nobunaga-attack-pay',
    'nobunaga-debuff', 'hijikata-attack-pay', 'hijikata-attack-kill', 'takasugi-attack-pay',
    'hiromasa-disable', 's2-odysseus-show-tactic', 's2-parrot-god-power',
    's2-penthesilea-god-power', 's2-achilles-god-power', 's2-bors-strong',
    's2-percival-attack-discard', 's2-gawain-runes', 's2-richard-attack-squires',
    's2-scathach-rune'
)) {
    if ($allRuntime.IndexOf($legacyAttackPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack attack declaration continuation returned: $legacyAttackPrompt"
    }
}

foreach ($legacyTrialPrompt in @(
    's2-lancelot-entry-charge', 's2-lancelot-kill', 's2-galahad-entry-trial',
    's2-finn-entry-trial', 's2-finn-entry-ready', 's2-constance-entry', 's2-angus-trial'
)) {
    if ($allRuntime.IndexOf($legacyTrialPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack trial declaration continuation returned: $legacyTrialPrompt"
    }
}

foreach ($legacyBatch6GAPrompt in @(
    's2-margaret-entry-mill', 's2-margaret-master-damage', 's2-ring-draw',
    's2-morrigan-enemy-death', 's2-limu-morale', 's2-grail-round-table-rune'
)) {
    if ($allRuntime.IndexOf($legacyBatch6GAPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack Batch 6G-A trigger continuation returned: $legacyBatch6GAPrompt"
    }
}

Write-Host 'Public trigger declaration guard passed.'
