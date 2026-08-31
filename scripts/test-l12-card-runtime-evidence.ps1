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
Write-Host 'Card runtime semantic evidence tests passed.'
