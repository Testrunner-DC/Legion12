[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6K-A source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$composite = Read-Source 'L12CompositeEffectPlans.cs'
$responses = Read-Source 'L12PublicResponseEffectPlans.cs'
$triggers = Read-Source 'L12PublicTriggerEffectPlans.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$extended = Read-Source 'L12S1ExtendedEffects.cs'
$tests = Read-Source 'AtomicReviewBatch6KARegressionTests.cs'
$audit = Read-Source 'S01-UNIVERSAL-HEAVEN-ABILITY-AUDIT.md'

$expectedCards = @(
    'S01-0001', 'S01-0002', 'S01-0003', 'S01-0004', 'S01-0005', 'S01-0006', 'S01-0007',
    'S01-0008', 'S01-0009', 'S01-0010', 'S01-0011', 'S01-0012', 'S01-0013', 'S01-0014',
    'S01-0015', 'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
    'S01-00C1', 'S01-0101', 'S01-0102', 'S01-0103', 'S01-0104', 'S01-0105', 'S01-0106',
    'S01-0107', 'S01-0108', 'S01-0109', 'S01-0110', 'S01-0111', 'S01-0112', 'S01-0113',
    'S01-0114', 'S01-0115', 'S01-0116', 'S01-0117', 'S01-0118', 'S01-0119', 'S01-0120',
    'S01-01C1', 'S01-01D1', 'S01-01M1', 'S01-01M2', 'S01-DS01', 'S01-DS02', 'S01-DS03',
    'S01-DS04', 'S01-DS05', 'S01-DS06', 'S01-DS07', 'S01-DS08', 'S01-DS09', 'S01-DS10'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S01-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missingCards = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpectedCards = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 56 -or $auditCards.Count -ne 56 -or $missingCards.Count -gt 0 -or $unexpectedCards.Count -gt 0) {
    throw "Batch 6K-A audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missingCards -join ','), unexpected=$($unexpectedCards -join ','))."
}
Assert-Contains $tests 'Assert.Equal(94, AuditedAbilityCounts.Values.Sum())' 'Batch 6K-A ability inventory must remain frozen at 94.'

foreach ($plan in @(
    'trigger:S01-0001:enter', 'active:S01-0105:searchBrothers', 'active:S01-0116:xishiExchange',
    'active:S01-01M1:drawCycle', 'active:S01-01D1:palaceReward', 'active:S01-01D1:palaceExchange'
)) {
    Assert-Contains $composite $plan "Batch 6K-A independent segment plan is missing: $plan"
}
foreach ($responseCard in @('S01-0020', 'S01-0120')) {
    Assert-Contains $responses ('["' + $responseCard + '"]') "Batch 6K-A response declaration plan is missing: $responseCard"
}
Assert-Contains $kernel 'RevealSetReactionSourceWhenStacked(candidate)' 'A set reaction source must reveal and move before the response window opens.'

$regencyStart = $triggers.IndexOf('case ("S01-0021", "reaction", _):', [StringComparison]::Ordinal)
$regencyEnd = $triggers.IndexOf('case ("S01-0213", "reaction", _):', [StringComparison]::Ordinal)
if ($regencyStart -lt 0 -or $regencyEnd -le $regencyStart) { throw 'Regency declaration plan could not be isolated.' }
$regencyPlan = $triggers.Substring($regencyStart, $regencyEnd - $regencyStart)
Assert-Contains $regencyPlan 'PublicTriggerStep("hand-card", "entryCard"' 'Regency must privately declare exactly one legal hand legion.'
Assert-Contains $regencyPlan 'allowCancel: false' 'Mandatory Regency choices must not expose cancellation.'
if ($regencyPlan.IndexOf('mode:none', [StringComparison]::Ordinal) -ge 0 -or
    $regencyPlan.IndexOf('optional-card', [StringComparison]::Ordinal) -ge 0) {
    throw 'Regency is mandatory when a legal hand legion and position exist; mode:none/optional-card must not return.'
}

foreach ($flow in @('battle-until-dawn-draw', 'empty-city-draw')) {
    $caseCount = [regex]::Matches($extended, 'case "' + [regex]::Escape($flow) + '":').Count
    if ($caseCount -ne 1) { throw "Legacy post-stack continuation returned for $flow (case count $caseCount; expected 1 resolver case)." }
}

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$questionStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5pyJ55aR54K5'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$questionCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($questionStatus)).Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
if ($fixedCount -ne 8 -or $questionCount -ne 2 -or $passedCount -ne 46) {
    throw "Batch 6K-A status totals drifted (passed=$passedCount, fixed=$fixedCount, questions=$questionCount)."
}
Write-Host 'S01 universal + Heaven per-ability audit guard passed (56 cards / 94 abilities).'
