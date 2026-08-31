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
$attackPlans = Read-Source 'L12AttackPublicTriggerPlans.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$counter = Read-Source 'L12S2CounterTactics.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$actions = Read-Source 'L12Actions.cs'
$gm = Read-Source 'L12GmCommands.cs'
$authority = Read-Source 'L12AuthorityEvents.cs'
$runtimeDirectory = (Get-ChildItem -LiteralPath $ProjectRoot -Filter 'L12PublicTriggerEffectPlans.cs' -Recurse -File |
    Select-Object -First 1).Directory.FullName
$allRuntime = (@(Get-ChildItem -LiteralPath $runtimeDirectory -Filter '*.cs' -File | ForEach-Object {
    [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
}) -join "`n")

foreach ($cardId in @(
    'S02-04M1', 'S02-0523', 'S01-02M3', 'S02-02M1', 'S02-01S1',
    'S01-0105', 'S01-0207', 'S01-0208', 'S01-0309', 'S01-0021', 'S01-0213',
    'S01-0223', 'S01-0320', 'S01-0224', 'S02-0202', 'S02-0203', 'S02-0205', 'S01-0206', 'S01-0407'
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
Assert-Contains $plans 'TryConsumeSelectedResources(player, 1' 'Tsukuyomi must commit its declared resource before stack entry.'
Assert-Contains $plans 'ReturnSelectedMoraleById(player, [costId], 1)' 'Liu Bei must return the declared morale before stack entry.'
Assert-Contains $plans 'DamageMaster(candidate.Controller, 1,' 'Brynhild must pay the known master-damage cost before stack entry.'
Assert-Contains $plans 'candidate.Data["preserveIndependentStack"] = "true"' 'Immortal Gift must preserve its independent draw segment when summon declaration is absent.'
Assert-Contains $plans 'activation.DeclaredValues["entryCard"] = ["mode:none"]' 'Immortal Gift invalid summon segment must cancel independently.'

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
    'saladin-move', 'saladin-slot', 'ryoma-pick', 'ryoma-slot', 's2-scarab-enter-slot'
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

Write-Host 'Public trigger declaration guard passed.'
