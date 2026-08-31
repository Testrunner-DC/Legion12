[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File | Select-Object -First 1
    if ($null -eq $file) { throw "Missing source file: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$plans = Read-Source 'L12PublicActiveEffectPlans.cs'
$s1 = Read-Source 'L12S1FactionEffects.cs'
$s2 = Read-Source 'L12S2RemainingEffects.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$allRuntime = $plans + "`n" + $s1 + "`n" + $s2

foreach ($ability in @(
    'cleopatraGuard', 'sunGuard', 'alvidaSummon', 'lokiHeal', 'palaceExchange',
    'yomiSweep', 'yomiRecover', 'amaterasuKill', 'divinityPower'
)) {
    Assert-Contains $plans ('"' + $ability + '"') "Public declaration plan is missing ability $ability."
}

Assert-Contains $s2 'new L12ActivationSelectionStep { Kind = "slot"' 'Thor Hammer slot must be declared before its graveyard cost is committed.'
Assert-Contains $s2 '["ability"] = ability, ["slot"] = declared[3]' 'Thor Hammer committed stack data must retain the declared slot.'
Assert-Contains $composite 'for (var nextIndex = current + 1; nextIndex < segments.Count; nextIndex++)' 'Composite segment queue must continue after an invalid independent segment.'
Assert-Contains $composite 'AddEvent("effect-cancelled"' 'Composite invalid-segment cancellation must preserve later independent segments.'

foreach ($legacy in @(
    'case "palace-kill":', 'case "palace-revive":',
    'case "yomi-kill3": if', 'case "yomi-kill1": if',
    's2-thor-hammer-slot', 's2-divinity-damage', 's2-divinity-recover',
    's2-divinity-hand', 'loki-heal-return'
)) {
    if ($allRuntime.IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-payment continuation returned: $legacy"
    }
}

Write-Host 'Public active declaration guard passed.'
