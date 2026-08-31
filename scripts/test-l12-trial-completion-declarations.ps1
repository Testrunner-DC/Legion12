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

$plans = Read-Source 'L12TrialCompletionTriggerPlans.cs'
$s2 = Read-Source 'L12S2FactionEffects.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$models = Read-Source 'Models.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$tests = Read-Source 'AtomicReviewBatch6BRegressionTests.cs'

foreach ($cardId in @('S02-06S3', 'S02-06S4', 'S02-06S5', 'S02-06M2')) {
    Assert-Contains ($plans + "`n" + $tests) $cardId "Batch 6B trial-completion coverage is missing $cardId."
}

foreach ($contract in @(
    'QueueCompletedTrialTriggerBatch',
    'TryBeginTrialCompletionTriggerDeclaration',
    'TryCompleteTrialCompletionTriggerDeclaration',
    'ResolveTrialCompletionTriggerEffect',
    'QueueNextTrialCompletionSegment',
    'trial-completion-library-arthur',
    'trial-completion-library-search',
    'fenianTargets'
)) {
    Assert-Contains $plans $contract "Batch 6B trial-completion contract is missing: $contract"
}

Assert-Contains $s2 'QueueCompletedTrialTriggerBatch(item.Controller, source)' 'completeTrial must publish a shared completion event instead of resolving printed effects inline.'
Assert-Contains $models 'MinimumReferenceNumericValue' 'Variable rune declarations must drive a generic number of public target steps.'
Assert-Contains $models 'AllowCancel' 'A declaration with mandatory later segments must be able to reject whole-flow cancellation.'
Assert-Contains $kernel 'DeclaredNumericValueAtLeast' 'Pending activation must honor numeric conditional declaration steps.'
Assert-Contains $kernel 'step.AllowCancel ? step.ValidChoices.Append("skip") : step.ValidChoices' 'Mandatory later segments must not expose a whole-flow skip control.'
Assert-Contains $prompts 'QueueNextTrialCompletionSegment(item)' 'Independent trial-completion segments must continue after resolution or negation.'
Assert-Contains $prompts '["mode:grave"]' 'The public graveyard mode needs a player-facing label.'
Assert-Contains $prompts '["mode:library"]' 'The delayed library mode needs a player-facing label.'
Assert-Contains $plans 'L12S2ZoneOps.SpendRunes(player, count)' 'Fenian Legend must atomically prepay X runes before stack entry.'
Assert-Contains $plans 'CreateTriggerCandidate(controller, angus, "trial-complete"' 'Angus must be a separate same-time completion candidate.'
Assert-Contains $plans 'candidate.Data["trialSegment"] = skipOptionalSearch ? "1" : "0"' 'Declining Lake Lady search must begin at the first mandatory segment.'
Assert-Contains $kernel 'candidate.Data.GetValueOrDefault("stackText", candidate.Text)' 'A declared candidate must project its actual first segment text onto the stack.'

foreach ($legacy in @(
    'ResolveCompletedTrialTrigger',
    'QueueFirstFenianDebuff',
    'QueueNextFenianDebuff',
    'fenianSingleDebuff',
    's2-lake-lady-arthur',
    's2-grail-search',
    's2-fenian-trial-debuff'
)) {
    if ($s2.IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack trial-completion path returned: $legacy"
    }
}

Write-Host 'Trial completion TriggerBatch, hidden-information delay, prepaid-cost, and independent-segment guard passed.'
