[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6L-C source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}
function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$actions = Read-Source 'L12Actions.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$remaining = Read-Source 'L12S2RemainingEffects.cs'
$faction = Read-Source 'L12S2FactionEffects.cs'
$publicActive = Read-Source 'L12PublicActiveEffectPlans.cs'
$kernel = Read-Source 'RuleKernel.cs'
$lethal = Read-Source 'L12LethalReplacements.cs'
$rulingTests = Read-Source 'RulingClosureRegressionTests.cs'
$tests = Read-Source 'AtomicReviewBatch6LCRegressionTests.cs'
$audit = Read-Source 'S02-TAKAMAGAHARA-OLYMPUS-ABILITY-AUDIT.md'

$expectedCards = @(
    'S02-0401','S02-0402','S02-0403','S02-0404','S02-0405','S02-0406','S02-04M1',
    'S02-0501','S02-0502','S02-0503','S02-0504','S02-0505','S02-0506','S02-0507',
    'S02-0508','S02-0509','S02-0510','S02-0511','S02-0512','S02-0513','S02-0514',
    'S02-0515','S02-0516','S02-0517','S02-0518','S02-0519','S02-0520','S02-0521',
    'S02-0522','S02-0523','S02-05M1','S02-05M2','S02-05C1','S02-05C1A','S02-05D1'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S02-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missing = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpected = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 35 -or $auditCards.Count -ne 35 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Batch 6L-C audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missing -join ','), unexpected=$($unexpected -join ','))."
}
Assert-Contains $tests 'Assert.Equal(104, AuditedAbilityCounts.Values.Sum())' 'The current S02 Takamagahara and Olympus ability inventory must remain frozen at 104.'

Assert-Contains $remaining '["ability"] = "tsukuyomiFrontAttackBuff"' 'Tsukuyomi back-to-front movement must create a TriggerCandidate.'
Assert-Contains $remaining 'case "tsukuyomiFrontAttackBuff"' 'Tsukuyomi back-to-front trigger resolver is missing.'
if ($remaining.Contains('moved.TsukuyomiFrontMoveBonusCount++;')) {
    throw 'Tsukuyomi movement recording must not mutate the attack bonus synchronously.'
}
Assert-Contains $composite '["S02-0405"]' 'Fortune hidden search/next-Uesugi composite plan is missing.'
Assert-Contains $composite 'new("fortune-next-uesugi"' 'Fortune next-Uesugi segment must remain independent.'
Assert-Contains $composite '["active:S02-05D1:divinityRecover"]' 'Divinity recovery/entry active composite plan is missing.'
Assert-Contains $remaining 'AtomicFlowKey(item, source) == "divinity-entry"' 'Divinity independent entry resolver is missing.'

Assert-Contains $kernel 'public static void InheritPromotionState' 'Promotion shared-state transaction is missing.'
foreach ($token in @('promoted.HasShock |= foundation.HasShock;', 'promoted.TimedModifiers.Add(',
        'promoted.CannotReadyByEffectUntilTurn', 'promoted.TauntExpiresAtPlayerTurnStart',
        'promoted.LastCavalryMoveTurn')) {
    Assert-Contains $kernel $token "Promotion shared-state boundary is missing: $token"
}

foreach ($source in @($remaining, $faction, $publicActive)) {
    if ([regex]::IsMatch($source, 'card\.Faction\s*[!=]=\s*"olympus"|legion\.Faction\s*[!=]=\s*"olympus"|declaredTarget\.Faction\s*[!=]=\s*"olympus"')) {
        throw 'Printed Olympus faction comparison returned to an effect selector or declaration revalidation.'
    }
}
Assert-Contains $actions 'L12StructuredCardRules.HasFaction(player, card, "olympus")' 'Olympus next-legion discount must use effective faction.'
Assert-Contains $faction 'L12StructuredCardRules.HasFaction(player, card, "olympus")' 'Olympus hidden searches must use effective faction.'
Assert-Contains $lethal 'NotifyCardDiscarded(controller, substitute, "hand", causedByEffect: true)' 'Helen lethal replacement must use the real effect-discard transaction.'
Assert-Contains $rulingTests 'HelenUsesARealEffectDiscardRatherThanAFieldDeathTransaction' 'Helen discard/event ruling regression is missing.'
Assert-Contains $rulingTests 'HelenSubstituteIsRevalidatedAndInvalidSelectionDoesNotProtectHer' 'Helen invalid-destination revalidation regression is missing.'

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$questionStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5pyJ55aR54K5'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
$questionCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($questionStatus) + ' \|').Count
if ($fixedCount -ne 17 -or $passedCount -ne 18 -or $questionCount -ne 0) {
    throw "Batch 6L-C status totals drifted (passed=$passedCount, fixed=$fixedCount, question=$questionCount)."
}
Write-Host 'S02 Takamagahara + Olympus per-ability audit guard passed (35 cards / 104 abilities).'
