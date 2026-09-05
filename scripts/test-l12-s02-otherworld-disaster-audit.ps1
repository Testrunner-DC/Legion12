[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6L-D source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}
function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$active = Read-Source 'L12ActiveAbilities.cs'
$structured = Read-Source 'L12StructuredCardRules.cs'
$otherworldStructured = Read-Source 'L12StructuredCardRules.S02OtherworldHumanAssisted.cs'
$faction = Read-Source 'L12S2FactionEffects.cs'
$trialCompletion = Read-Source 'L12TrialCompletionTriggerPlans.cs'
$disasters = Read-Source 'L12Disasters.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$atomic = Read-Source 'AtomicEffects.cs'
$tests = Read-Source 'AtomicReviewBatch6LDRegressionTests.cs'
$audit = Read-Source 'S02-OTHERWORLD-DISASTER-ABILITY-AUDIT.md'
$matrix = Read-Source 'CARD-EFFECT-REVIEW-MATRIX.md'
$openQuestions = Read-Source 'OPEN-QUESTIONS.md'

$expectedCards = @(
    'S02-0601','S02-0602','S02-0603','S02-0604','S02-0605','S02-0606','S02-0607','S02-0608',
    'S02-0609','S02-0610','S02-0611','S02-0612','S02-0613','S02-0614','S02-0615','S02-0616',
    'S02-0617','S02-0618','S02-0619','S02-0620','S02-0621','S02-0622','S02-06C1','S02-06D1',
    'S02-06M1','S02-06M2','S02-06S1','S02-06S2','S02-06S3','S02-06S4','S02-06S5','S02-06S6',
    'S02-DS01','S02-DS02','S02-DS03','S02-DS04','S02-DS05','S02-DS06'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S02-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missing = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpected = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 38 -or $auditCards.Count -ne 38 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Batch 6L-D audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missing -join ','), unexpected=$($unexpected -join ','))."
}
Assert-Contains $tests 'Assert.Equal(108, AuditedAbilityCounts.Values.Sum())' 'Batch 6L-D ability inventory must remain frozen at 108.'

Assert-Contains $faction 'DeclarationKey = "mode", Text = "梅林：选择效果"' 'Merlin public mode declaration is missing.'
Assert-Contains $faction 'RequiredDeclaredChoice = "mode:debuff"' 'Merlin public enemy target must only be declared for debuff mode.'
Assert-Contains $faction '["action"] = "s2-merlin-search"' 'Merlin legal-resolution private search prompt is missing.'
if ([regex]::IsMatch($faction, 'var tactics\s*=\s*player\.Library\.Where')) {
    throw 'Merlin hidden library identities returned to pre-stack declaration.'
}
Assert-Contains $trialCompletion 'var canUse = player.Library.Count > 0;' 'Grail declaration must not inspect hidden match existence.'
if ($trialCompletion.Contains('player.Library.Any(card => card.Faction == "otherworld"')) {
    throw 'Grail hidden match existence leaked into declaration again.'
}

Assert-Contains $structured 'entry-cost-minus-per-friendly-faction-legion' 'Bors dynamic discount must use the shared structured cost query.'
Assert-Contains $structured 'HasFaction(controller, target, faction)' 'Bors discount must use effective Otherworld faction.'
Assert-Contains $otherworldStructured '("faction", "otherworld")' 'Bors confirmed Otherworld faction parameter is missing.'
Assert-Contains $faction 'L12StructuredCardRules.HasFaction(player, declaredTarget, "otherworld")' 'Morrigan commit validation must use effective Otherworld faction.'
Assert-Contains $faction 'L12StructuredCardRules.HasFaction(player, chosen, "otherworld")' 'Fenian commit validation must use effective Otherworld faction.'
Assert-Contains $faction 'L12StructuredCardRules.HasFaction(player, card, "otherworld")' 'Otherworld hidden search must use effective faction.'
Assert-Contains $trialCompletion 'L12StructuredCardRules.HasFaction(player, card, "otherworld")' 'Grail resolution must use effective Otherworld faction.'
Assert-Contains $faction 'player.Graveyard.Where(card => card.Faction == "otherworld")' 'Crusade only-Otherworld printed-faction boundary must remain explicit.'
Assert-Contains $faction 'top.Faction == "otherworld" ? new[] { "hand", "top", "bottom" }' 'Amakine only-Otherworld printed-faction boundary must remain explicit.'

Assert-Contains $active '$"active:{sourceInstanceId}:crusade-choice"' 'Crusade three modes must share the printed once-per-turn key.'
$galahadStart = $faction.IndexOf('if (ability == "galahadGrailReward" && source.CardId == "S02-0604")', [StringComparison]::Ordinal)
$galahadCommit = $faction.IndexOf('if (ability == "galahadGrailReward" && source.CardId == "S02-0604")', $galahadStart + 1, [StringComparison]::Ordinal)
$galahadEnd = $faction.IndexOf('if (ability == "runeUse"', $galahadCommit, [StringComparison]::Ordinal)
if ($galahadCommit -lt 0 -or $galahadEnd -lt 0) { throw 'Galahad commit branch is missing.' }
$galahad = $faction.Substring($galahadCommit, $galahadEnd - $galahadCommit)
$removeIndex = $galahad.IndexOf('RemoveFromField(', [StringComparison]::Ordinal)
$pushIndex = $galahad.IndexOf('PushEffect(', [StringComparison]::Ordinal)
if ($removeIndex -lt 0 -or $pushIndex -lt 0 -or $removeIndex -gt $pushIndex) {
    throw 'Galahad self-discard cost must complete before PushEffect.'
}
Assert-Contains $galahad '["healMode"] = target' 'Galahad immutable optional-heal declaration is missing.'

Assert-Contains $prompts 'if (trigger == "disaster") item.Data["unrespondable"] = "true";' 'Disaster effects must remain unrespondable.'
Assert-Contains $disasters 'SetLibrariesReversedByDisaster(true);' 'Heaven Earth Change must reverse the real library order.'
Assert-Contains $disasters 'SetLibrariesReversedByDisaster(false);' 'Heaven Earth Change must restore library order on leaving.'
Assert-Contains $atomic 'Program("S02-DS05", "disaster"' 'Wrath neutral nonlethal master damage verified atomic program is missing.'
Assert-Contains $atomic '("neutralSource", "true")' 'Disaster master damage must remain a neutral source.'

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
if ($fixedCount -ne 8 -or $passedCount -ne 30) {
    throw "Batch 6L-D status totals drifted (passed=$passedCount, fixed=$fixedCount)."
}

$auditFiles = @(
    'S01-UNIVERSAL-HEAVEN-ABILITY-AUDIT.md', 'S01-SUN-CITY-ASGARD-ABILITY-AUDIT.md',
    'S01-TAKAMAGAHARA-ABILITY-AUDIT.md', 'S02-UNIVERSAL-HEAVEN-ABILITY-AUDIT.md',
    'S02-SUN-CITY-ASGARD-ABILITY-AUDIT.md', 'S02-TAKAMAGAHARA-OLYMPUS-ABILITY-AUDIT.md',
    'S02-OTHERWORLD-DISASTER-ABILITY-AUDIT.md'
)
$allAuditRows = @($auditFiles | ForEach-Object {
    [regex]::Matches((Read-Source $_), '(?m)^\| (?<id>S\d{2}-[A-Za-z0-9]+) [^|\r\n]+ \| (?<abilities>\d+) \|.*?\| (?<status>[^|\r\n]+) \|$') |
        ForEach-Object {
            [pscustomobject]@{
                Id = $_.Groups['id'].Value
                Abilities = [int]$_.Groups['abilities'].Value
                Status = $_.Groups['status'].Value.Trim()
            }
        }
})
$allAudited = @($allAuditRows.Id | Sort-Object -Unique)
$duplicateAuditIds = @($allAuditRows | Group-Object Id | Where-Object Count -ne 1)
if ($allAuditRows.Count -ne 248 -or $allAudited.Count -ne 248 -or $duplicateAuditIds.Count -gt 0) {
    throw "Full-pool per-card audit coverage drifted (rows=$($allAuditRows.Count), unique=$($allAudited.Count), duplicateGroups=$($duplicateAuditIds.Count))."
}
$allAbilityCount = ($allAuditRows | Measure-Object Abilities -Sum).Sum
if ($allAbilityCount -ne 577) {
    throw "Full-pool per-ability audit total drifted (expected=577, actual=$allAbilityCount)."
}

$questionStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5pyJ55aR54K5'))
$matrixFixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v5bey5L+u5aSN'))
$fullPassedCount = @($allAuditRows | Where-Object Status -eq $passedStatus).Count
$fullFixedCount = @($allAuditRows | Where-Object Status -eq $fixedStatus).Count
$fullQuestionRows = @($allAuditRows | Where-Object { $_.Status.StartsWith($questionStatus, [StringComparison]::Ordinal) })
if ($fullPassedCount -ne 189 -or $fullFixedCount -ne 59 -or $fullQuestionRows.Count -ne 0) {
    throw "Full-pool audit status totals drifted (passed=$fullPassedCount, fixed=$fullFixedCount, questionCards=$($fullQuestionRows.Count))."
}
$expectedQuestionCards = @()
$actualQuestionCards = @($fullQuestionRows | ForEach-Object { $_.Id } | Sort-Object)
if (($actualQuestionCards -join ',') -ne (($expectedQuestionCards | Sort-Object) -join ',')) {
    throw "Full-pool audit question-card set drifted (actual=$($actualQuestionCards -join ','))."
}

$matrixRows = @([regex]::Matches($matrix, '(?m)^\| (?<id>S\d{2}-[A-Za-z0-9]+) \|.*\| (?<conclusion>[^|\r\n]+) \|\r?$'))
if ($matrixRows.Count -ne 248 -or @($matrixRows | ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique).Count -ne 248) {
    throw "Card-effect matrix must contain exactly one row for every audited card (actual=$($matrixRows.Count))."
}
$matrixById = @{}
foreach ($row in $matrixRows) { $matrixById[$row.Groups['id'].Value] = $row.Groups['conclusion'].Value }
foreach ($row in $allAuditRows) {
    $conclusion = $matrixById[$row.Id]
    if ([string]::IsNullOrWhiteSpace($conclusion)) { throw "Matrix row missing for $($row.Id)." }
    if ($row.Status -eq $fixedStatus -and -not $conclusion.Contains($matrixFixedStatus)) {
        throw "Matrix fixed status drifted for $($row.Id)."
    }
    if ($row.Status.StartsWith($questionStatus, [StringComparison]::Ordinal) -and -not $conclusion.Contains($questionStatus)) {
        throw "Matrix question status drifted for $($row.Id)."
    }
    if ($row.Status -eq $passedStatus -and
        ($conclusion.Contains($matrixFixedStatus) -or $conclusion.Contains($questionStatus))) {
        throw "Matrix passed status drifted for $($row.Id)."
    }
}

$noOpenQuestionText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5b2T5YmN5peg5b6F6KOB5a6a6aG5'))
Assert-Contains $openQuestions $noOpenQuestionText 'OPEN-QUESTIONS must record that the current queue is empty.'
$openHeadings = [regex]::Matches($openQuestions, '(?m)^### [1-5]\. ').Count
if ($openHeadings -ne 0) { throw "OPEN-QUESTIONS must not retain resolved numbered ruling items (actual=$openHeadings)." }

Write-Host 'S02 Otherworld + disaster per-ability audit guard passed (38 cards / 108 abilities; full pool 248 cards / 577 abilities; 189 passed / 59 fixed / 0 question cards).'
