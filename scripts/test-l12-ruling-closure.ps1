[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Select-Object -First 1
    if ($null -eq $file) { throw "Missing ruling-closure source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

function Decode-Utf8Base64([string]$Value) {
    return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($Value))
}

$actions = Read-Source 'L12Actions.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$disasters = Read-Source 'L12Disasters.cs'
$lethal = Read-Source 'L12LethalReplacements.cs'
$postResolution = Read-Source 'L12PostResolutionGeneratedEffects.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$tests = Read-Source 'RulingClosureRegressionTests.cs'
$faq = Read-Source 'FAQ-RULINGS.md'
$open = Read-Source 'OPEN-QUESTIONS.md'

foreach ($token in @(
    'TryOfferCardLethalSubstitution',
    'leaveKind: L12FieldLeaveKind.Defeat',
    'bypassLethalReplacement: true',
    'pending.LethalReplacementSubstitutes[protectedCard.InstanceId] = choice',
    'ResolveAttachedCardLethalKillSources(prompt, defeatedInstanceId)',
    'AttachCardLethalKillSource(protectedCard, pending.Event)',
    'owner.Graveyard.Add(substitute)',
    'NotifyCardDiscarded(controller, substitute, "hand", causedByEffect: true)'
)) {
    Assert-Contains $lethal $token "Shared lethal-replacement boundary is missing: $token"
}
if ($actions.Contains('TryPreventS1FactionDeath')) {
    throw 'Legacy Horemheb card-only lethal prevention returned.'
}

foreach ($token in @(
    'BeginPtolemyRepeatedTacticEffect',
    'BeginFaithZealotMasterChoice(completed)',
    '"faith-zealot-post-resolution"',
    '["repeatedEffectOnly"] = "true"'
)) {
    Assert-Contains $postResolution $token "Post-resolution generated-effect boundary is missing: $token"
}
Assert-Contains $composite 'effectOnlyRepeat: true' 'Repeated composite effects must use the effect-only declaration path.'
Assert-Contains $composite '.Where(segment => segment.CostKey is not null)' 'Repeated effects must identify and filter original composite costs.'
Assert-Contains $prompts 'item.Data.ContainsKey("postResolutionGenerated")' 'Completed parent effects must dispatch generated interactions after leaving the stack.'

foreach ($token in @(
    'item.Data["thunderLosers"] = $"{State.ActivePlayer}|{other}"',
    'ContinueThunderWrathReturnSequence(item)',
    'MoveFieldCardToZone(player, card, "hand"'
)) {
    Assert-Contains $disasters $token "Thunder Wrath tie/return boundary is missing: $token"
}

$facts = [regex]::Matches($tests, '(?m)^\s*\[Fact\]\s*$').Count
if ($facts -ne 13) { throw "Ruling closure regression inventory drifted (expected=13, actual=$facts)." }
foreach ($testName in @(
    'HoremhebSubstituteReceivesTheActualLethalDestinationWithOwnerAndDeathEvents',
    'HelenUsesARealEffectDiscardRatherThanAFieldDeathTransaction',
    'PtolemyRepeatsColonEffectWithoutTheOriginalCostOrVirtualCardMovement',
    'FaithZealotMasterChoiceAppearsOnlyAfterZealotLeavesTheStack',
    'NegatedLiJingRevealDoesNotReadOrProcessTheHiddenTopCard',
    'LiJingRevealAndDependentChoiceStayInsideOneUninterruptedStackItem',
    'ThunderWrathTieProcessesActivePlayerThenOtherPlayerAndRecordsTheSequence',
    'ThunderWrathTieSkipsAPlayerWithoutLegionsAndStillProcessesTheOther'
)) {
    Assert-Contains $tests $testName "Ruling regression is missing: $testName"
}

foreach ($encodedToken in @(
    '5oiY5Zy65pu/5Luj54mM5om/5o6l5Y+X5L+d5oqk5Yab5Zui5Y6f5pys55qE6Ie05ZG957uT5p6c',
    '5LuO5omL54mM6L+b5YWl5omA5pyJ6ICF5aKT5Zyw77yM5Lqn55Sf5pWI5p6c5byD54mM5LqL5Lu2',
    '5LiN5YaN5qyh5pSv5LuY',
    '5YWI5a6M5oiQ6Ieq6Lqr5pWI5p6c57uT566X5bm256a75byA5aCG5Y+g',
    '5bGe5LqO5ZCM5LiA5qyh6ZqQ6JeP5L+h5oGv5LqL5Yqh',
    '5omA5pyJ5bm25YiX6ICF6YO95piv6L6T5a62'
)) {
    $token = Decode-Utf8Base64 $encodedToken
    Assert-Contains $faq $token "FAQ ruling is missing: $token"
}
Assert-Contains $open (Decode-Utf8Base64 '5b2T5YmN5peg5b6F6KOB5a6a6aG5') 'Resolved rulings must leave no current open question.'
if ([regex]::IsMatch($open, '(?m)^### [1-5]\. ')) {
    throw 'Resolved numbered ruling questions returned to OPEN-QUESTIONS.'
}

Write-Host 'Five-ruling closure guard passed (13 regressions; OPEN queue empty).'
