[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6L-A source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}
function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$composite = Read-Source 'L12CompositeEffectPlans.cs'
$remaining = Read-Source 'L12S2RemainingEffects.cs'
$faction = Read-Source 'L12S2FactionEffects.cs'
$game = Read-Source 'L12GameEngine.cs'
$active = Read-Source 'L12ActiveAbilities.cs'
$publicTrigger = Read-Source 'L12PublicTriggerEffectPlans.cs'
$enterPlans = Read-Source 'L12EnterPublicTriggerPlans.cs'
$attackPlans = Read-Source 'L12AttackPublicTriggerPlans.cs'
$postResolution = Read-Source 'L12PostResolutionGeneratedEffects.cs'
$rulingTests = Read-Source 'RulingClosureRegressionTests.cs'
$tests = Read-Source 'AtomicReviewBatch6LARegressionTests.cs'
$audit = Read-Source 'S02-UNIVERSAL-HEAVEN-ABILITY-AUDIT.md'
$open = Read-Source 'OPEN-QUESTIONS.md'

$expectedCards = @(
    'S02-0001','S02-0002','S02-0003','S02-0004','S02-0005','S02-0006','S02-0007',
    'S02-0008','S02-0009','S02-0010','S02-0011','S02-0012','S02-0013','S02-0014',
    'S02-0015','S02-0016','S02-0017','S02-0018','S02-0101','S02-0102','S02-0103',
    'S02-0104','S02-0105','S02-0106','S02-01M1','S02-01S1'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S02-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missing = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpected = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 26 -or $auditCards.Count -ne 26 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Batch 6L-A audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missing -join ','), unexpected=$($unexpected -join ','))."
}
Assert-Contains $tests 'Assert.Equal(51, AuditedAbilityCounts.Values.Sum())' 'Batch 6L-A ability inventory must remain frozen at 51.'

Assert-Contains $composite '["trigger:S02-0101:enter"]' 'Yingzheng must retain its independent composite plan.'
Assert-Contains $composite 'new("yingzheng-kill"' 'Yingzheng kill segment is missing.'
Assert-Contains $composite 'new("yingzheng-return"' 'Yingzheng subsequent morale segment is missing.'
Assert-Contains $faction 'CompositeFirstSegmentData("trigger:S02-0101:enter"' 'Yingzheng paid entry must start the composite plan before stacking.'
Assert-Contains $faction 'case "yingzheng-kill"' 'Yingzheng kill resolver is missing.'
Assert-Contains $faction 'case "yingzheng-return"' 'Yingzheng subsequent resolver is missing.'
if ($faction.Contains('ResolveYingzhengPaidEnterEffect')) {
    throw 'Yingzheng kill and subsequent morale return must not return to one serial resolver.'
}

Assert-Contains $remaining 'DeclarationKey = "entrySlot"' 'Wukong must declare its public front slot before stacking.'
Assert-Contains $remaining 'player.Field[row][slot] is not null' 'Wukong must revalidate its declared slot at resolution.'
Assert-Contains $remaining 'AddEvent("effect-cancelled", item.Controller' 'Wukong invalid-slot cancellation event is missing.'
if ($remaining -like '*var slot = EmptySlots(player).FirstOrDefault(choice => choice.StartsWith*') {
    throw 'Wukong must not auto-select the first front slot during activation commit.'
}

Assert-Contains $game 'bool fromFactionEffect = false' 'Shared morale-add restriction provenance is missing.'
Assert-Contains $game 'player.FactionMoraleAdditionForbiddenUntilTurn == State.TurnSerial' 'Yingzheng morale-add restriction is not consumed by the shared entry point.'
Assert-Contains $active 'fromFactionEffect: true' 'Tianting faction morale effects must explicitly identify their allowed provenance.'
Assert-Contains $open '当前无待裁定项' 'Resolved ruling questions must leave no current open item.'
Assert-Contains $postResolution 'BeginFaithZealotMasterChoice(completed)' 'Faith Zealot master choice must begin only after its parent effect completes.'
Assert-Contains $postResolution '"faith-zealot-post-resolution"' 'Faith Zealot must create a post-resolution interaction.'
Assert-Contains $rulingTests 'FaithZealotMasterChoiceAppearsOnlyAfterZealotLeavesTheStack' 'Faith Zealot post-resolution regression is missing.'

$rawFactionFilter = '\.Faction\s*(?:==|!=)\s*"(?:tianting|taiyangcheng|asgard|gaotianyuan|olympus|otherworld)"'
foreach ($source in @($composite, $publicTrigger, $enterPlans, $attackPlans)) {
    if ([regex]::IsMatch($source, $rawFactionFilter)) {
        throw 'Public declaration/composite plans must use EffectiveFaction/HasFaction while Ring of Dominion is active.'
    }
}

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$questionStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5pyJ55aR54K5'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$questionCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($questionStatus) + ' \|').Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
if ($fixedCount -ne 3 -or $questionCount -ne 0 -or $passedCount -ne 23) {
    throw "Batch 6L-A status totals drifted (passed=$passedCount, fixed=$fixedCount, questions=$questionCount)."
}
Write-Host 'S02 universal + Heaven per-ability audit guard passed (26 cards / 51 abilities).'
