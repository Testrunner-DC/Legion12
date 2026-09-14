[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}
. (Join-Path $PSScriptRoot 'lib/l12-card-runtime-evidence.ps1')

function Assert-ContainsEvidence($Evidence, [string]$CardId, [string]$FileName, [string]$Kind) {
    if ($Evidence[$CardId].Tests -notcontains $FileName) {
        throw "$Kind semantic evidence did not map $FileName to $CardId."
    }
}

$cards = @(
    [pscustomobject]@{ id = 'S01-0113'; cardType = 'legion' },
    [pscustomobject]@{ id = 'S02-06S1'; cardType = 'token' },
    [pscustomobject]@{ id = 'TEST-RUNE'; cardType = 'rune' },
    [pscustomobject]@{ id = 'TEST-TRIAL'; cardType = 'trial' }
)
$evidence = Get-L12CardRuntimeEvidence -ProjectRoot $ProjectRoot -Cards $cards
Assert-ContainsEvidence $evidence 'S01-0113' 'LatestBugRegressionTests.cs' 'ability'
Assert-ContainsEvidence $evidence 'S02-06S1' 'RuleKernelTests.cs' 'token type'
Assert-ContainsEvidence $evidence 'TEST-RUNE' 'ExtendedCardEffectsTests.cs' 'type'
Assert-ContainsEvidence $evidence 'TEST-TRIAL' 'Bq20260830_02RegressionTests.cs' 'shared-entry'
$finePrograms = @(Get-L12FineAtomicProgramMatches -SourcePath (Join-Path $ProjectRoot '服务端WebSocket/TwelveLegions'))
if ($finePrograms.Count -ne 128) {
    throw "Fine atomic evidence expected 128 explicit and generated programs, got $($finePrograms.Count)."
}
foreach ($expected in @(
    @{ Id = 'S01-0210'; Trigger = 'enter' },
    @{ Id = 'S01-0313'; Trigger = 'death' },
    @{ Id = 'S02-0002'; Trigger = 'after-kill' },
    @{ Id = 'ST05-07'; Trigger = 'enter' },
    @{ Id = 'S01-0301'; Trigger = 'attack' },
    @{ Id = 'S01-0311'; Trigger = 'attack' },
    @{ Id = 'S02-0509'; Trigger = 'attack' },
    @{ Id = 'S02-0517'; Trigger = 'attack' },
    @{ Id = 'S02-0519'; Trigger = 'attack' },
    @{ Id = 'S02-0606'; Trigger = 'attack' }
)) {
    if (-not ($finePrograms | Where-Object {
        $_.Groups['id'].Value -eq $expected.Id -and $_.Groups['trigger'].Value -eq $expected.Trigger
    })) {
        throw "Generated fine atomic evidence did not include $($expected.Id)/$($expected.Trigger)."
    }
}
Write-Host 'Card runtime semantic evidence tests passed.'
