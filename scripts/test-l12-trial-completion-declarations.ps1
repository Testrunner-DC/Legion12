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

function Assert-NotContains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -ge 0) { throw $Message }
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
    'fenianTarget',
    'fenianRunePaid'
)) {
    Assert-Contains $plans $contract "Batch 6B trial-completion contract is missing: $contract"
}

Assert-Contains $s2 'CompleteTrialRuleAction(playerIndex, source)' 'completeTrial must execute directly as a rule action.'
Assert-Contains $plans 'QueueCompletedTrialTriggerBatch(controller, trial)' 'The rule flip must publish the separate printed completion trigger.'
$completeTrialText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5a6M5oiQ6K+V54K8'))
$completeTrialPattern = 'PushEffect(playerIndex, source, "active", "' + $completeTrialText + '",'
Assert-NotContains $s2 $completeTrialPattern 'The rule flip must not be a negatable active effect.'
Assert-Contains $models 'MinimumReferenceNumericValue' 'Variable rune declarations must drive a generic number of public target steps.'
Assert-Contains $models 'L12ActivationCancellationPolicy' 'A declaration must carry an explicit whole-flow cancellation policy.'
Assert-Contains $kernel 'DeclaredNumericValueAtLeast' 'Pending activation must honor numeric conditional declaration steps.'
Assert-Contains $kernel 'ShouldAddActivationCancellationChoice(step)' 'Pending activation must apply the explicit cancellation policy before exposing a whole-flow skip control.'
Assert-Contains $kernel 'L12ActivationCancellationPolicy.NotAllowed => false' 'Mandatory later segments must not expose a whole-flow skip control.'
Assert-Contains $prompts 'QueueNextTrialCompletionSegment(item)' 'Independent trial-completion segments must continue after resolution or negation.'
Assert-Contains $prompts '["mode:grave"]' 'The public graveyard mode needs a player-facing label.'
Assert-Contains $prompts '["mode:library"]' 'The delayed library mode needs a player-facing label.'
Assert-Contains $plans '!L12S2ZoneOps.SpendRunes(player, 1)' 'Each Fenian declaration must pay exactly one rune before its response stack opens.'
Assert-Contains $plans 'var target = DeclaredEnemyTarget(item.Controller, targetId)' 'Each Fenian stack must revalidate its single frozen target at resolution.'
$fenianPaidRuneNotRefundedText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5bey5pSv5LuY56ym5paH5LiN6L+U6L+Y'))
Assert-Contains $plans $fenianPaidRuneNotRefundedText 'Fenian target invalidation must report that its paid rune remains spent.'
Assert-Contains $plans 'if (plan == "fenian-legend")' 'Fenian repeat availability must be evaluated only after the current stack item finishes.'
Assert-Contains $plans 'player.SpecialZones.Runes <= 0 || !PublicLegions' 'Fenian repeat must stop when either runes or legal enemy targets are absent.'
Assert-NotContains $plans 'fenianTargets' 'The retired batch target list must not return.'
Assert-NotContains $plans 'fenianRuneCount' 'The retired variable rune-count declaration must not return.'
Assert-Contains $tests 'FenianTrialPaysOneRuneForOneTargetAndOffersRepeatOnlyAfterThatStackEnds' 'Fenian negation must keep the one paid rune and offer the next independent use afterward.'
Assert-Contains $tests 'FenianTargetLossFailsOnlyThatAlreadyPaidUseAndMayThenDeclineTheRepeat' 'Fenian target invalidation must fail only the paid current use.'
Assert-Contains $tests 'FenianRepeatMayChooseTheSameStillLegalTargetInASecondIndependentStack' 'Fenian must allow the same still-legal target in a later independent stack.'
$remaining = Read-Source 'L12S2RemainingEffects.cs'
$advancePlans = Read-Source 'L12TrialAdvanceEffectPlans.cs'
$angusTests = Read-Source 'EffectBatch294RegressionTests.cs'
Assert-Contains $s2 'if (advanced && queueAngusTrigger) QueueS2AngusTrialAdvanceRune(playerIndex, source ?? trial)' 'Ordinary trial progress must queue Angus only after actual progress increases.'
Assert-Contains $advancePlans 'if (AdvanceTrialWithoutAngusTrigger(playerIndex, source.TrialValue, source))' 'Finn trial progress must defer Angus until the shared same-timing follow-up batch is built.'
Assert-Contains $advancePlans 'QueueFinnTrialAdvanceFollowups(playerIndex, source, source)' 'Finn and Angus must enter one shared same-timing trigger batch.'
Assert-Contains $remaining 'CreateTriggerCandidate(playerIndex, master, "trial-advance"' 'Angus must be a separate optional trial-progress candidate.'
Assert-Contains $remaining 'State.ActivePlayer != playerIndex' 'Angus rune trigger must remain own-turn only.'
Assert-Contains $angusTests 'AngusTrialAdvanceDeclineDoesNotConsumeButNegatedActivationDoes' 'Angus optional once-per-turn declaration needs a runtime regression.'
if ($plans.Contains('angus-rune') -or $plans.Contains('TrialAngusMaster')) {
    throw 'The retired Angus completion-rune effect must not return.'
}
Assert-Contains $plans 'candidate.Data["trialSegment"] = skipOptionalSearch ? "1" : "0"' 'Declining Lake Lady search must begin at the first mandatory segment.'
Assert-Contains $kernel 'var stackText = candidate.Data.GetValueOrDefault("stackText");' 'A declared candidate must read its explicit first segment before any generic text.'
Assert-Contains $kernel 'if (string.IsNullOrWhiteSpace(stackText))' 'Only an absent explicit stack segment may use a fallback.'
Assert-Contains $kernel 'UsesGenericStackEffectText(candidate.Trigger, candidate.Text)' 'Full trigger text must not replace a specialized follow-up segment.'
Assert-Contains $kernel 'Text = stackText,' 'A declared candidate must project its actual first segment text onto the stack.'
$promptTests = Read-Source 'Bq20260907_263RegressionTests.cs'
Assert-Contains $promptTests 'ExplicitStackTextWinsAndEmptyResolvedTextFallsBackWithoutReadingHiddenCardText' 'Explicit, empty and specialized segment fallbacks require a runtime regression.'

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

Write-Host 'Trial completion TriggerBatch, hidden-information delay, and repeatable one-rune/one-target Fenian stack guard passed.'
