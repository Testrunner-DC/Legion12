[CmdletBinding()]
param(
    [string]$Workflow = ".github\workflows\verify-release.yml"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$path = if ([IO.Path]::IsPathRooted($Workflow)) { $Workflow } else { Join-Path $repoRoot $Workflow }
if (-not (Test-Path -LiteralPath $path)) { throw "Workflow not found: $path" }

$text = Get-Content -LiteralPath $path -Raw
$releaseCondition = "if: github.event_name == 'workflow_dispatch' || startsWith(github.ref, 'refs/tags/v')"
$conditionCount = ([regex]::Matches($text, [regex]::Escape($releaseCondition))).Count

if ($conditionCount -ne 2) { throw "Expected the package and upload steps to share two release-only conditions; found $conditionCount." }
if ($text -notmatch "tags:\s*\['v\*'\]") { throw 'Release tag trigger v* is missing.' }
if ($text -notmatch 'if \[\[ -d "\$\{root\}/publish/runtimes" \]\]; then') { throw 'Optional runtimes directory guard is missing.' }
if ($text -notmatch 'archive_bytes > 157286400') { throw '150 MiB release archive budget is missing.' }
if ($text -notmatch '::error::release archive is larger') { throw 'Release archive diagnostic is missing.' }
if (([regex]::Matches($text, 'npm run check:performance-architecture')).Count -ne 1) {
    throw 'GitHub main verification must run the low-latency performance architecture lock exactly once.'
}
if (([regex]::Matches($text, 'node scripts/check-l12-architecture-lock\.mjs')).Count -ne 1) {
    throw 'GitHub main verification must run the P0-P4 architecture exit lock exactly once.'
}

Write-Host '[L12 workflow] ordinary pushes run architecture, performance and release verification; manual/tag runs package and upload.'
