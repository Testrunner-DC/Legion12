[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File | Select-Object -First 1
    if ($null -eq $file) { throw "Missing Batch 6K-B source: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}
function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$composite = Read-Source 'L12CompositeEffectPlans.cs'
$triggers = Read-Source 'L12PublicTriggerEffectPlans.cs'
$faction = Read-Source 'L12S1FactionEffects.cs'
$tests = Read-Source 'AtomicReviewBatch6KBRegressionTests.cs'
$audit = Read-Source 'S01-SUN-CITY-ASGARD-ABILITY-AUDIT.md'

$expectedCards = @(
    'S01-0201','S01-0202','S01-0203','S01-0204','S01-0205','S01-0206','S01-0207',
    'S01-0208','S01-0209','S01-0210','S01-0211','S01-0212','S01-0213','S01-0214',
    'S01-0215','S01-0216','S01-0217','S01-0218','S01-0219','S01-0220','S01-0221',
    'S01-0222','S01-0223','S01-0224','S01-02C1','S01-02D1','S01-02M1','S01-02M2','S01-02M3',
    'S01-0301','S01-0302','S01-0303','S01-0304','S01-0305','S01-0306','S01-0307',
    'S01-0308','S01-0309','S01-0310','S01-0311','S01-0312','S01-0313','S01-0314',
    'S01-0315','S01-0316','S01-0317','S01-0318','S01-0319','S01-0320',
    'S01-03C1','S01-03D1','S01-03M1','S01-03M2'
)
$auditCards = @([regex]::Matches($audit, '(?m)^\| (?<id>S01-[A-Za-z0-9]+) ') |
    ForEach-Object { $_.Groups['id'].Value } | Sort-Object -Unique)
$missing = @($expectedCards | Where-Object { $auditCards -notcontains $_ })
$unexpected = @($auditCards | Where-Object { $expectedCards -notcontains $_ })
if ($expectedCards.Count -ne 53 -or $auditCards.Count -ne 53 -or $missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Batch 6K-B audit inventory drifted (expected=$($expectedCards.Count), actual=$($auditCards.Count), missing=$($missing -join ','), unexpected=$($unexpected -join ','))."
}
Assert-Contains $tests 'Assert.Equal(124, AuditedAbilityCounts.Values.Sum())' 'Batch 6K-B ability inventory must remain frozen at 124.'

foreach ($plan in @(
    'trigger:S01-0201:attack','trigger:S01-0201:death','trigger:S01-0216:enter',
    'trigger:S01-0218:enter','trigger:S01-0219:enter','active:S01-02D1:sunTopThree',
    'active:S01-03D1:valhallaRecover'
)) { Assert-Contains $composite $plan "Batch 6K-B independent plan is missing: $plan" }
Assert-Contains $composite 'data.Remove("wisdomRewards")' 'Independent follow-up segments must not duplicate a Wisdom Codex reward marker.'
foreach ($token in @(
    '("S01-0201", "attack" or "death", _, _) => true',
    '("S01-0315", "enter", _, _) => true',
    '"' + [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5Zu+54m55pGp5pav5LiJ5LiW77ya6aKE5YWI6YCJ5oup6ZqP5ZCO5Ye75p2A55qE5YW15Yqb5LiN6auY5LqOMTAwMOWGm+Wbog==')) + '"',
    '"' + [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5peg6aqo6ICF5LyK55Om5bCU77ya6aKE5YWI5aOw5piO5piv5ZCm5p+l55yL54mM5bqT6aG26YOoM+W8oOeJjA==')) + '"'
)) { Assert-Contains $triggers $token "Batch 6K-B public declaration token is missing: $token" }
foreach ($flow in @(
    'case "thutmose-debuff"','case "thutmose-kill"','case "canopic-box-heal-discard"',
    'case "canopic-two-discard"','case "canopic-three-discard"',
    'AtomicFlowKey(item) == "sun-top-three-recover"','AtomicFlowKey(item) == "valhalla-recover"'
)) { Assert-Contains $faction $flow "Batch 6K-B resolver flow is missing: $flow" }
if ($faction.Contains('HealMaster(item.Controller, 1, "卡诺匹斯箱"); if (source is not null) DiscardRelic')) {
    throw 'Canopic Box search must not heal/discard inside the hidden search continuation.'
}
if ($faction.Contains('case "valhallaRecover": Mill(player, 2, "英灵殿"); RecoverAsgard')) {
    throw 'Valhalla mill and subsequent public recovery must remain separate StackItems.'
}

$fixedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5piO56Gu6ZSZ6K+v4oaS5bey5L+u5aSN'))
$questionStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5pyJ55aR54K5'))
$passedStatus = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6YCa6L+H'))
$fixedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($fixedStatus) + ' \|').Count
$questionCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($questionStatus)).Count
$passedCount = [regex]::Matches($audit, '\| ' + [regex]::Escape($passedStatus) + ' \|').Count
if ($fixedCount -ne 8 -or $questionCount -ne 2 -or $passedCount -ne 43) {
    throw "Batch 6K-B status totals drifted (passed=$passedCount, fixed=$fixedCount, questions=$questionCount)."
}
Write-Host 'S01 Sun City + Asgard per-ability audit guard passed (53 cards / 124 abilities).'
