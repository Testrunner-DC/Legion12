[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourceRoot = Get-ChildItem -LiteralPath $repoRoot -Directory | ForEach-Object {
    $candidate = Join-Path $_.FullName 'TwelveLegions'
    if (Test-Path -LiteralPath (Join-Path $candidate 'L12GameEngine.cs') -PathType Leaf) { $candidate }
} | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace([string]$sourceRoot)) { throw 'The TwelveLegions server source root was not found.' }
$engineFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Filter 'L12*.cs' | Where-Object {
    Select-String -LiteralPath $_.FullName -SimpleMatch 'public sealed partial class L12GameEngine' -Quiet
})
if ($engineFiles.Count -lt 1) { throw 'No L12GameEngine partial sources were found.' }

$forbidden = [regex]::new(
    'Microsoft\.AspNetCore|Microsoft\.Data\.Sqlite|System\.Net\.Http|L12WebSocketServer|L12RoomManager|MatchRecorder|\bHttpContext\b|\bHttpRequest\b|\bSqlite[A-Za-z]*\b|\b(?:File|Directory)\.',
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)
$violations = [Collections.Generic.List[string]]::new()
foreach ($file in $engineFiles) {
    $relative = [IO.Path]::GetFileName($file.FullName)
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        $lineNumber += 1
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('//', [StringComparison]::Ordinal)) { continue }
        if ($forbidden.IsMatch($line)) { $violations.Add("$relative`:$lineNumber`:$($line.Trim())") }
    }
}
if ($violations.Count -gt 0) {
    throw "L12GameEngine acquired an HTTP, room, persistence, recorder, or file-system dependency:`n$($violations -join "`n")"
}

$platformReferences = [Collections.Generic.List[string]]::new()
foreach ($file in $engineFiles) {
    $relative = [IO.Path]::GetFileName($file.FullName)
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        $lineNumber += 1
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('//', [StringComparison]::Ordinal)) { continue }
        if ($line.IndexOf('L12PlatformStore.', [StringComparison]::Ordinal) -ge 0) {
            $platformReferences.Add("$relative`:$lineNumber`:$($line.Trim())")
        }
    }
}
$acceptedPlatformReference = @($platformReferences | Where-Object {
    $_ -match '^L12PromptsAndSetup\.cs:\d+:!string\.Equals\(id, L12PlatformStore\.AnnihilationCardId,$'
})
if ($platformReferences.Count -ne 1 -or $acceptedPlatformReference.Count -ne 1) {
    throw "The frozen P1 dependency baseline changed. Review and remove platform dependencies before updating the baseline:`n$($platformReferences -join "`n")"
}

Write-Host "L12 core architecture boundary passed: $($engineFiles.Count) engine sources; no transport, room, persistence, recorder, or file-system dependencies; 1 recorded legacy constant dependency."
