[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6K-C source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}
function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$composite = Read-Source 'L12CompositeEffectPlans.cs'
$attackPlans = Read-Source 'L12AttackPublicTriggerPlans.cs'
$extended = Read-Source 'L12S1ExtendedEffects.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$tests = Read-Source 'AtomicReviewBatch6KCRegressionTests.cs'
$audit = Read-Source 'S01-TAKAMAGAHARA-ABILITY-AUDIT.md'

$expectedCards = @(
    'S01-0401','S01-0402','S01-0403','S01-0404','S01-0405','S01-0406','S01-0407',
    'S01-0408','S01-0409','S01-0410','S01-0411','S01-0412','S01-0413','S01-0414',
    'S01-0415','S01-0416','S01-0417','S01-0418','S01-0419','S01-0420',
    'S01-04C1','S01-04D1','S01-04M1','S01-04M2'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S01-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missing = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpected = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 24 -or $auditCards.Count -ne 24 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Batch 6K-C audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missing -join ','), unexpected=$($unexpected -join ','))."
}
Assert-Contains $tests 'Assert.Equal(55, AuditedAbilityCounts.Values.Sum())' 'Batch 6K-C ability inventory must remain frozen at 55.'

foreach ($plan in @(
    'trigger:S01-0401:attack',
    'active:S01-04M1:amaterasuKill',
    'active:S01-04M1:amaterasuReady'
)) { Assert-Contains $composite $plan "Batch 6K-C independent plan is missing: $plan" }
Assert-Contains $composite 'divine-punishment-effect' 'Divine Punishment must consume its prestack target declaration.'
Assert-Contains $attackPlans '["S01-0401"] = new("honda", TargetKind: "enemy-after-cost-debuff"' 'Honda must remain in the attack public-trigger plan.'
Assert-Contains $extended 'private static IEnumerable<L12CardInstance> PublicFactionLegions' 'Public faction selectors must share the public-legion filter.'
Assert-Contains $extended 'L12StructuredCardRules.HasFaction(player, card, faction)' 'Faction selectors must honor the World Ring in every zone.'
Assert-Contains $prompts 'var next = State.DeferredEffectStack[^1];' 'Independent deferred segments must be exposed one response item at a time.'
if ($prompts.Contains('EffectStack.AddRange(State.DeferredEffectStack)')) {
    throw 'Deferred independent segments must not be bulk-pushed under a single response window.'
}

$production = [string]::Join("`n", @(Get-ChildItem -LiteralPath $ProjectRoot -Filter '*.cs' -Recurse -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|TwelveLegions.Tests)[\\/]'
    } | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8) }))
foreach ($legacy in @('honda-kill-zero','divine-punishment-kill')) {
    if ($production.Contains($legacy)) { throw "Removed Batch 6K-C resolution prompt token returned: $legacy" }
}
foreach ($rawFaction in @('.Faction == "gaotianyuan"','.Faction != "gaotianyuan"')) {
    if ($production.Contains($rawFaction)) { throw "A naked Takamagahara faction comparison bypasses the shared World Ring rule: $rawFaction" }
}

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$questionStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5pyJ55aR54K5'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$questionCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($questionStatus)).Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
if ($fixedCount -ne 10 -or $questionCount -ne 0 -or $passedCount -ne 14) {
    throw "Batch 6K-C status totals drifted (passed=$passedCount, fixed=$fixedCount, questions=$questionCount)."
}
Write-Host 'S01 Takamagahara per-ability audit guard passed (24 cards / 55 abilities).'
