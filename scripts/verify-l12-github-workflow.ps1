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
$testEntry = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/invoke-l12-tests.ps1') -Raw
foreach ($project in @('TwelveLegions.Tests','TwelveLegions.Platform.Tests')) {
    if ($text -notmatch [regex]::Escape("./scripts/invoke-l12-tests.ps1 -Project ./$project/$project.csproj")) { throw "Full supervised project missing: $project" }
}
if ($testEntry -match '--filter' -or -not $testEntry.Contains("@('test',`$Project,'--configuration'")) { throw 'CI test entry must execute both full projects without filters.' }
$standardEntry = Get-Content -LiteralPath (Join-Path $repoRoot 'ops/windows/verify-l12.ps1') -Raw
if ($text -match 'actions/upload-artifact|dotnet publish|releaseSha256') { throw 'CI cannot publish a second legacy deployment artifact.' }
if ($text -notmatch "tags:\s*\['v\*'\]") { throw 'Release tag trigger v* is missing.' }
if (-not $standardEntry.Contains('发布产物缺少 linux-x64 原生依赖')) { throw 'Canonical Linux runtime guard is missing.' }
if (-not $standardEntry.Contains(').Length -gt 150MB')) { throw 'Canonical 150 MiB release archive budget is missing.' }
if (-not $standardEntry.Contains('运行包异常大于 150MB')) { throw 'Canonical release archive diagnostic is missing.' }
if (([regex]::Matches($text, 'npm run check:performance-architecture')).Count -ne 0) {
    throw 'GitHub verification must not run a standalone performance lock before the frontend build.'
}
if (([regex]::Matches($text, '(?m)^\s+npm run build 2>&1 \| Tee-Object -FilePath \$log\s*$')).Count -ne 1) {
    throw 'GitHub main verification must run the frontend build exactly once; its first step owns the performance lock.'
}
if (-not $text.Contains('exit $buildExit') -or -not $text.Contains('if ($buildExit -ne 0)')) {
    throw 'CI must propagate the actual frontend native failure, not just annotate it.'
}
if (([regex]::Matches($text, 'node scripts/check-l12-architecture-lock\.mjs')).Count -ne 1) {
    throw 'GitHub main verification must run the P0-P4 architecture exit lock exactly once.'
}
if ($text -match 'FullyQualifiedName!~EmailAuthAndAccountLifecycleTests') {
    throw 'GitHub main verification must run the entire active platform test project without a class-name filter.'
}
if (([regex]::Matches($text, 'node scripts/test-release-status\.mjs')).Count -ne 1) {
    throw 'GitHub main verification must validate the release state source exactly once.'
}

& node (Join-Path $repoRoot 'scripts/test-ci-release-entry.mjs') $path
if ($LASTEXITCODE -ne 0) { throw 'Canonical release entry contract failed.' }
Write-Host '[L12 workflow] push/PR/manual/tag verify the full code; canonical asset-bound packages remain owned by verify-l12.ps1.'
