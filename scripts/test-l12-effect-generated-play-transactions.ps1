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

$runtimeRoot = Get-ChildItem -LiteralPath $ProjectRoot -Directory -Recurse |
    Where-Object { $_.Name -eq 'TwelveLegions' -and (Test-Path (Join-Path $_.FullName 'L12GameEngine.cs')) } |
    Select-Object -First 1
if ($null -eq $runtimeRoot) { throw 'Missing TwelveLegions runtime directory.' }
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtimeRoot.FullName -Filter '*.cs' -File)
$effectGenerated = Read-Source 'L12EffectGeneratedPlay.cs'
$s2Faction = Read-Source 'L12S2FactionEffects.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'

foreach ($required in @(
    'EffectGeneratedFreePlaySlots',
    'BeginEffectGeneratedFreePlay',
    'CompleteEffectGeneratedFreePlay',
    'TrySummonFromAnyPrivateZone',
    'RemoveAuthoritativeLocation',
    '["effectGeneratedPlay"] = "free"',
    'ResolveOnPlayContinuousEffects',
    'QueueOrPushTriggeredEffect'
)) {
    Assert-Contains $effectGenerated $required "Effect-generated free-play authority transaction is missing: $required"
}
Assert-Contains $kernel 'activation.Ability == EffectGeneratedFreePlayAbility' 'PendingActivation must dispatch effect-generated free plays through the shared transaction.'
Assert-Contains $s2Faction 'BeginEffectGeneratedFreePlay(item.Controller, card, item, "library"' 'Li Mu and Okita must use the shared effect-generated free-play entry.'
if ($s2Faction.IndexOf('s2-okita-slot', [StringComparison]::Ordinal) -ge 0) {
    throw 'Legacy Okita resolution-time slot prompt returned.'
}
if ($s2Faction.IndexOf('PushEffect(item.Controller, card, "play"', [StringComparison]::Ordinal) -ge 0) {
    throw 'Legacy faction resolver direct play PushEffect returned.'
}

$directPlayPushes = @($runtimeFiles | Select-String -SimpleMatch 'PushEffect(' |
    Where-Object { $_.Line.IndexOf('"play"', [StringComparison]::Ordinal) -ge 0 })
if ($directPlayPushes.Count -gt 3) {
    throw "Effect-generated play direct-PushEffect ratchet increased: $($directPlayPushes.Count) > 3."
}
$allowedPlayPushFiles = @('L12CompositeEffectPlans.cs', 'L12EffectGeneratedPlay.cs', 'L12S1FactionEffects.cs')
foreach ($match in $directPlayPushes) {
    if ($allowedPlayPushFiles -notcontains $match.Path.Substring($match.Path.LastIndexOf([IO.Path]::DirectorySeparatorChar) + 1)) {
        throw "Unclassified direct play PushEffect: $($match.Path):$($match.LineNumber)"
    }
}

$directHandAdds = @($runtimeFiles | Select-String -SimpleMatch '.Hand.Add(')
if ($directHandAdds.Count -gt 7) {
    throw "Direct Hand.Add ratchet increased: $($directHandAdds.Count) > 7. Use AddCardToHandByEffect for effect movement."
}
$allowedHandAddFiles = @('L12AuthorityEvents.cs', 'L12GameEngine.cs', 'L12GmCommands.cs', 'L12PromptsAndSetup.cs')
foreach ($match in $directHandAdds) {
    if ($allowedHandAddFiles -notcontains $match.Path.Substring($match.Path.LastIndexOf([IO.Path]::DirectorySeparatorChar) + 1)) {
        throw "Unclassified direct Hand.Add: $($match.Path):$($match.LineNumber)"
    }
}

$directLibraryRemoves = @($runtimeFiles | Select-String -SimpleMatch 'Library.Remove(')
if ($directLibraryRemoves.Count -gt 41) {
    throw "Direct Library.Remove ratchet increased: $($directLibraryRemoves.Count) > 41. Classify or route new zone transactions."
}

Write-Host "Effect-generated play and private-zone transaction guard passed: playPush=$($directPlayPushes.Count), handAdd=$($directHandAdds.Count), libraryRemove=$($directLibraryRemoves.Count)."
