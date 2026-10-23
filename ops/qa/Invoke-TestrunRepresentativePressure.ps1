#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BindingPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-F]{64}$')][string]$BindingSha256,
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,47}$')][string]$RunLabel = 'normal-preparation',
    [string]$OutputRoot = 'D:/GPT/Legion12/artifacts/representative-load-current-20261022',
    [string]$CacheRoot = 'D:/GPT/Legion12/cache/primary/representative-load-current-20261022',
    [string]$NodePath = 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This first delivery is an offline preparer, not an executable remote pressure chain.
# Reject before creating output, authenticating, or starting any helper.
if ($Apply) { throw 'NORMAL_PRESSURE_EXECUTION_NOT_IMPLEMENTED: prepared-only; no remote lifecycle is admitted' }
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $taskRepo 'ops/windows/L12GeneratedStorage.ps1')
$taskOutput = Assert-L12GeneratedPath -Path $OutputRoot -Purpose 'normal testrun preparation evidence'
$taskCache = Assert-L12GeneratedPath -Path $CacheRoot -Purpose 'normal testrun preparation cache'
$taskExpectedOutput = Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/artifacts/representative-load-current-20261022' -Purpose 'leased preparation evidence'
$taskExpectedCache = Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/cache/primary/representative-load-current-20261022' -Purpose 'leased preparation cache'
if (-not [string]::Equals($taskOutput,$taskExpectedOutput,[StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($taskCache,$taskExpectedCache,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'NORMAL_PRESSURE_OUTPUT_OUTSIDE_LEASE'
}
if (-not [IO.Path]::IsPathFullyQualified($BindingPath) -or -not [IO.Path]::IsPathFullyQualified($NodePath)) {
    throw 'NORMAL_PRESSURE_ABSOLUTE_INPUT_REQUIRED'
}
$taskRun = Join-Path $taskOutput $RunLabel
$taskRunCache = Join-Path $taskCache $RunLabel
$null = Assert-L12GeneratedPath -Path $taskRun -Purpose 'selected normal preparation run'
$null = Assert-L12GeneratedPath -Path $taskRunCache -Purpose 'selected normal preparation temporary data'
foreach ($taskParent in @($taskOutput,$taskCache)) {
    if (-not (Test-Path -LiteralPath $taskParent)) { $null = New-Item -ItemType Directory -Path $taskParent }
}
if ((Test-Path -LiteralPath $taskRun) -or (Test-Path -LiteralPath $taskRunCache)) {
    throw 'NORMAL_PRESSURE_RUN_EXISTS_INSPECT_ONLY'
}
$null = New-Item -ItemType Directory -Path $taskRun
$null = New-Item -ItemType Directory -Path $taskRunCache
$env:L12_ARTIFACT_ROOT = $taskOutput
. (Join-Path $taskRepo 'ops/windows/Initialize-L12BuildEnvironment.ps1') -CacheRoot $taskRunCache -DependencyCacheRoot $taskRunCache 6>$null | Out-Null
$env:TEMP = $taskRunCache; $env:TMP = $taskRunCache; $env:TMPDIR = $taskRunCache
$taskHead = (& git -C $taskRepo rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'NORMAL_PRESSURE_GIT_BASELINE_FAILED' }
$taskStatus = @(& git -C $taskRepo status --short)
if ($LASTEXITCODE -ne 0) { throw 'NORMAL_PRESSURE_GIT_STATUS_FAILED' }
$taskBaseline = [ordered]@{schema=1;head=$taskHead;status=$taskStatus;capturedBeforePreparation=$true}
$taskBaseline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRun 'source-baseline.json') -Encoding utf8NoBOM -NoNewline
$taskEntry = Join-Path $taskRepo 'opcgpro-vue/scripts/testrun-pressure-protocol.mjs'
& $NodePath $taskEntry --prepare --binding $BindingPath --binding-sha $BindingSha256 --output $taskRun --cache $taskRunCache
if ($LASTEXITCODE -ne 0) { throw 'NORMAL_PRESSURE_OFFLINE_PREPARATION_FAILED' }
