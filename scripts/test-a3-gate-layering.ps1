[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$gate = Join-Path $PSScriptRoot "verify-l12-change.ps1"
$package = Get-Content -LiteralPath (Join-Path $repoRoot "opcgpro-vue\package.json") -Raw | ConvertFrom-Json

function Get-Plan {
    param([string]$Level, [string]$ChangedPath)
    return (& $gate -Level $Level -ChangedPaths @($ChangedPath) -DryRun 6>&1 | Out-String)
}

function Assert-Count {
    param([string]$Plan, [string]$Label, [int]$Expected)
    $actual = ([regex]::Matches($Plan, [regex]::Escape("[L12 ") + "[^]]+\] " + [regex]::Escape($Label))).Count
    if ($actual -ne $Expected) {
        throw "$Label was scheduled $actual times; expected $Expected."
    }
}

if (-not $package.scripts.build.StartsWith("npm run check:performance-architecture && ")) {
    throw "Direct frontend build must retain the complete performance preflight."
}

$frontend = "opcgpro-vue/src/l12/game/battleViewportLayout.ts"
$rules = "TwelveLegions.Tests/A3Fixture.cs"
$batchFrontend = Get-Plan "Batch" $frontend
Assert-Count $batchFrontend "Low-latency performance architecture lock" 0
Assert-Count $batchFrontend "Frontend production build" 1

$batchRules = Get-Plan "Batch" $rules
Assert-Count $batchRules "Low-latency performance architecture lock" 1
Assert-Count $batchRules "Frontend production build" 0

$focusedFrontend = Get-Plan "Focused" $frontend
Assert-Count $focusedFrontend "Low-latency performance architecture lock" 1
Assert-Count $focusedFrontend "Frontend UI contracts" 1

$releaseFrontend = Get-Plan "Release" $frontend
Assert-Count $releaseFrontend "Low-latency performance architecture lock" 0
Assert-Count $releaseFrontend "Commit-level release verification (no deployment)" 1
Assert-Count $releaseFrontend "Frontend production build" 0

$previousWorkCache = $env:L12_WORK_CACHE
try {
    $env:L12_WORK_CACHE = $null
    $deploymentPlan = Get-Plan "Batch" "ops/windows/deploy-l12.ps1"
    Assert-Count $deploymentPlan "Deployment target, health and failure-preservation behavior" 1
    if ([string]::IsNullOrWhiteSpace($env:L12_WORK_CACHE) -eq $false) {
        throw "DryRun must not initialize a cache or change L12_WORK_CACHE."
    }
}
finally { $env:L12_WORK_CACHE = $previousWorkCache }

Write-Host "A3 gate layering: Batch frontend/other, Focused and Release plans passed."
