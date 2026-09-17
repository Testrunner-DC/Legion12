[CmdletBinding()]
param([string]$CacheRoot = "")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$previousUpdate = $env:L12_UPDATE_EFFECT_INVENTORY
$previousOutput = $env:L12_EFFECT_INVENTORY_JSON
Push-Location $repoRoot
try {
    $cache = & (Join-Path $repoRoot "ops/windows/Initialize-L12BuildEnvironment.ps1") -CacheRoot $CacheRoot
    $auditDirectory = Join-Path ([string]$cache) "effect-lifecycle-inventory"
    New-Item -ItemType Directory -Path $auditDirectory -Force | Out-Null
    $env:L12_UPDATE_EFFECT_INVENTORY = "1"
    $env:L12_EFFECT_INVENTORY_JSON = Join-Path $auditDirectory "effect-ability-inventory.json"
    & dotnet test (Join-Path $repoRoot "TwelveLegions.Tests/TwelveLegions.Tests.csproj") `
        --no-restore --filter "FullyQualifiedName~EffectLifecycleInventoryTests" `
        -- "xUnit.ParallelizeTestCollections=false"
    if ($LASTEXITCODE -ne 0) { throw "Ability inventory generation or regression checks failed ($LASTEXITCODE)." }
    Write-Host "Ability inventory: $(Join-Path $repoRoot 'docs/l12/EFFECT-ABILITY-INVENTORY.md')"
    Write-Host "Structured evidence: $env:L12_EFFECT_INVENTORY_JSON"
}
finally {
    $env:L12_UPDATE_EFFECT_INVENTORY = $previousUpdate
    $env:L12_EFFECT_INVENTORY_JSON = $previousOutput
    Pop-Location
}
