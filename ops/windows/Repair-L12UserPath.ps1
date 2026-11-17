[CmdletBinding()]
param(
    [string]$ArtifactRoot = $(if ($env:L12_ARTIFACT_ROOT) { $env:L12_ARTIFACT_ROOT } else { 'D:\GPT\Legion12\artifacts' }),
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

$resolvedRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$null = Assert-L12GeneratedPath -Path $resolvedRoot -Purpose 'PATH repair evidence'
$repairRoot = Join-Path $resolvedRoot 'tooling-path-repair'
New-Item -ItemType Directory -Path $repairRoot -Force | Out-Null

$before = [Environment]::GetEnvironmentVariable('Path', 'User')
if ([string]::IsNullOrWhiteSpace($before)) { $before = '' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssZ')
$backupPath = Join-Path $repairRoot "user-path-before-$stamp.txt"
[IO.File]::WriteAllText($backupPath, $before, [Text.UTF8Encoding]::new($false))

$entries = [Collections.Generic.List[string]]::new()
foreach ($entry in ($before -split ';')) {
    $trimmed = $entry.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -match '\.dotnet\\tools$') { continue }
    if (-not ($entries | Where-Object { $_ -ieq $trimmed })) { [void]$entries.Add($trimmed) }
}
foreach ($extra in @(
        'C:\Users\neptu\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd',
        'C:\Users\neptu\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell',
        'C:\Users\neptu\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin')) {
    if ((Test-Path -LiteralPath $extra -PathType Container) -and -not ($entries | Where-Object { $_ -ieq $extra })) {
        [void]$entries.Add($extra)
    }
}
$after = $entries -join ';'
if ($after.Length -gt 4096) { throw "L12_USER_PATH_TOO_LONG: $($after.Length)" }

if (-not $WhatIf) {
    [Environment]::SetEnvironmentVariable('Path', $after, 'User')
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class L12EnvironmentBroadcast {
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
'@
    [IntPtr]$result = [IntPtr]::Zero
    [void][L12EnvironmentBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x001A, [IntPtr]::Zero,
        'Environment', 0x0002, 5000, [ref]$result)
}

[ordered]@{
    schema = 1
    beforeLength = $before.Length
    afterLength = $after.Length
    removedDotnetToolEntries = @($before -split ';' | Where-Object { $_ -match '\.dotnet\\tools$' }).Count
    backupPath = $backupPath
    userPath = $after
} | ConvertTo-Json -Depth 4
