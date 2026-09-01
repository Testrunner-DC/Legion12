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

$plans = Read-Source 'L12PublicResponseEffectPlans.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$s1 = Read-Source 'L12S1ExtendedEffects.cs'
$s2 = Read-Source 'L12S2CounterTactics.cs'
$triggers = Read-Source 'L12PublicTriggerEffectPlans.cs'

foreach ($cardId in @('S01-0020', 'S01-0120', 'S01-0224', 'S01-0320', 'S02-0016', 'S02-0017', 'S02-0018')) {
    Assert-Contains ($plans + $composite + $triggers) $cardId `
        "Fifth-batch response/trigger plan is missing card $cardId."
}

Assert-Contains $prompts 'TryBeginPublicResponseDeclaration' `
    'Direct responses with public declarations must enter the shared pre-stack planner.'
Assert-Contains $kernel 'activation.Ability == "public-response-declaration"' `
    'Pending response declarations need a shared atomic completion hook.'
Assert-Contains $prompts '["mode:discard"]' `
    'Ruined Ritual discard mode must have a natural-language label at the shared prompt boundary.'
Assert-Contains $prompts '["mode:suppress"]' `
    'Ruined Ritual suppress mode must have a natural-language label at the shared prompt boundary.'
Assert-Contains $kernel 'step.Kind == "opponent-hand-anonymous"' `
    'Pending activation steps must support anonymous opponent-hand choices.'
Assert-Contains $kernel 'ResolveHiddenPromptChoice(prompt, choice)' `
    'Anonymous slots must resolve only on the server after player selection.'
Assert-Contains $plans 'ReturnSelectedMoraleById(player, [cost], 1)' `
    'Empty City must commit its declared morale return before response stack entry.'
Assert-Contains $plans 'ResumeResponseAfterCancelledDeclaration(activation)' `
    'Cancelled or invalid response declarations must restore the same priority window.'

foreach ($flow in @(
    'battle-until-dawn-buff', 'battle-until-dawn-draw', 'empty-city-block', 'empty-city-draw',
    'wisdom-draw', 'wisdom-recover', 'blood-eagle-debuff', 'blood-eagle-recover',
    'ruined-ritual', 'supply-plunder-return', 'supply-plunder-draw', 'poison-negate', 'poison-discard'
)) {
    Assert-Contains $composite ('"' + $flow + '"') "Independent response segment is missing: $flow"
}

Assert-Contains $plans 'player.Graveyard.Count < 5' `
    'Battle Until Dawn declaration must check graveyard count, not library/base count.'
Assert-Contains $s1 'player.Graveyard.Count >= 5' `
    'Battle Until Dawn independent draw segment must revalidate graveyard count.'
Assert-Contains $s1 'case "empty-city-draw"' `
    'Empty City must defer its front-row check and optional draw to its own segment.'
Assert-Contains $prompts 'CreateTriggerCandidate(controller, wisdom' `
    'Wisdom Codex reward must be created only after the target finishes successfully.'
Assert-Contains $triggers '["S01-0224|wisdom-reward"]' `
    'Wisdom Codex reward must predeclare its optional recovery segment.'
Assert-Contains $triggers '["S01-0320|reaction"]' `
    'Blood Eagle must predeclare its ordered graveyard targets.'
Assert-Contains $s2 'CompositeDeclared(item, "handTarget")' `
    'Ruined Ritual and Supply Plunder must consume the anonymous predeclared hand slot.'

foreach ($legacy in @('s2-ruin-mode', 's2-ruin-discard', 's2-plunder-return', 'blood-eagle-pick')) {
    if (($s1 + $s2).IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack response prompt returned: $legacy"
    }
}

Write-Host 'Public response declaration and independent-segment guard passed.'
