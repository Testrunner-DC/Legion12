param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/l12-card-runtime-evidence.ps1')
$serverPath = Get-ChildItem -LiteralPath $ProjectRoot -Directory | ForEach-Object {
    $candidate = Join-Path $_.FullName 'TwelveLegions/Data/cards.st.json'
    if (Test-Path -LiteralPath $candidate) { $candidate }
} | Select-Object -First 1
$webPath = Join-Path $ProjectRoot 'opcgpro-vue/public/data/l12/cards.st.json'
$serverPath = [string]$serverPath
if ([string]::IsNullOrWhiteSpace($serverPath)) { throw 'Cannot locate the authoritative ST catalog.' }
$serverRaw = [System.IO.File]::ReadAllText($serverPath, [System.Text.Encoding]::UTF8)
$webRaw = [System.IO.File]::ReadAllText($webPath, [System.Text.Encoding]::UTF8)
if ($serverRaw -cne $webRaw) { throw 'Server and web ST catalogs differ.' }

$decodedCards = $serverRaw | ConvertFrom-Json
$cards = @($decodedCards.GetEnumerator())
function Get-AtomicReference([object]$Card) {
    $property = $Card.PSObject.Properties['atomicReference']
    if ($null -eq $property) { return '' }
    return [string]$property.Value
}

$noEffect = @($cards | Where-Object { [string]::IsNullOrWhiteSpace((Get-AtomicReference $_)) })
$effectCards = @($cards | Where-Object { -not [string]::IsNullOrWhiteSpace((Get-AtomicReference $_)) })
$abilityCount = ($cards | ForEach-Object {
    [regex]::Matches((Get-AtomicReference $_), '(?im)^\s*Ability\s+\d+').Count
} | Measure-Object -Sum).Sum
if ($cards.Count -ne 76 -or $effectCards.Count -ne 69 -or $noEffect.Count -ne 7) {
    throw "Unexpected ST catalog counts: total=$($cards.Count) effect=$($effectCards.Count) noEffect=$($noEffect.Count)"
}
if ($abilityCount -ne 121) { throw "Expected 121 ST ability boundaries, found $abilityCount." }
if (@($effectCards | Where-Object { [string]::IsNullOrWhiteSpace((Get-AtomicReference $_)) }).Count -gt 0) {
    throw 'An effect-bearing ST card has no atomic reference.'
}

$evidence = Get-L12CardRuntimeEvidence -ProjectRoot $ProjectRoot -Cards $cards
$missingRuntime = @($effectCards | Where-Object {
    $item = $evidence[$_.id]
    @($item.Sources).Count -eq 0 -and @($item.Categories).Count -eq 0
})
$missingTests = @($effectCards | Where-Object { @($evidence[$_.id].Tests).Count -eq 0 })
if ($missingRuntime.Count -gt 0) { throw "ST cards without runtime evidence: $($missingRuntime.id -join ', ')" }
if ($missingTests.Count -gt 0) { throw "ST cards without test evidence: $($missingTests.id -join ', ')" }

Write-Host 'ST effect audit passed: 76 cards, 69 effect cards, 7 vanilla cards, 121 human-authored ability boundaries, with runtime and test evidence.'
