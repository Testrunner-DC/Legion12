[CmdletBinding()]
param(
    [string]$Root = "D:\GPT\Legion12",
    [switch]$Strict,
    [ValidateSet('Inventory','Active')][string]$Scope='Inventory',
    [string]$CandidateRoot=''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if (-not (Test-Path -LiteralPath $resolvedRoot)) { throw "L12 root not found: $resolvedRoot" }
function Assert-OrdinaryAncestors([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked audit input refused: $cursor" }
        }
        $cursor=Split-Path -Parent $cursor
    }
}
Assert-OrdinaryAncestors $resolvedRoot

function Get-DirectoryBytes([string]$Path, [string[]]$ExcludedDirectories=@(), [bool]$RejectLinks=$false) {
    if (-not (Test-Path -LiteralPath $Path)) { return [int64]0 }
    [int64]$sum = 0
    $pending=[Collections.Generic.Stack[string]]::new(); $pending.Push($Path)
    while ($pending.Count) {
        $current=Get-Item -LiteralPath $pending.Pop() -Force -ErrorAction Stop
        if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { if ($RejectLinks) { throw "Linked active storage refused: $($current.FullName)" }; continue }
        foreach ($entry in @(Get-ChildItem -LiteralPath $current.FullName -Force -ErrorAction Stop)) {
            if ($entry.PSIsContainer -and $ExcludedDirectories -contains $entry.Name) { continue }
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { if ($RejectLinks) { throw "Linked active storage refused: $($entry.FullName)" }; continue }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            else { $sum += [int64]$entry.Length }
        }
    }
    return $sum
}

$app = Join-Path $resolvedRoot 'app'
$gitCommon = (& git -C $app rev-parse --git-common-dir 2>$null)
if ($LASTEXITCODE -eq 0 -and $gitCommon) {
    if (-not [IO.Path]::IsPathRooted($gitCommon)) { $gitCommon = Join-Path $app $gitCommon }
    $gitCommon = [IO.Path]::GetFullPath($gitCommon)
}

$budgets = [ordered]@{
    'app' = 2200MB
    'git-common' = 1400MB
    'app\opcgpro-vue\public\cards' = 650MB
    'app\opcgpro-vue\node_modules' = 220MB
    # One warm production build includes ~60 MiB official playmat/card-back
    # assets copied from public. The old 5 MiB budget counted JS/CSS only.
    'app\opcgpro-vue\dist' = 100MB
    'artifacts\test-runs' = 500MB
    'artifacts\deploy' = 700MB
    'cache' = 1200MB
    'temp' = 200MB
}

$rows = foreach ($relative in $budgets.Keys) {
    $path = if ($relative -eq 'git-common' -and $gitCommon) { $gitCommon } else { Join-Path $resolvedRoot $relative }
    $bytes = Get-DirectoryBytes $path
    [pscustomobject]@{
        Path = $relative
        MiB = [math]::Round($bytes / 1MB, 1)
        BudgetMiB = [math]::Round([int64]$budgets[$relative] / 1MB, 1)
        Status = if ($bytes -le [int64]$budgets[$relative]) { 'OK' } else { 'OVER' }
    }
}

# Inventory retains the original aggregate thresholds. Active verification is
# a separate, explicit scope: protected historical databases/worktrees cannot
# be deleted to make a source change pass. It never authorizes their cleanup.
if ($Scope -eq 'Active') {
    if ([string]::IsNullOrWhiteSpace($CandidateRoot)) { throw 'Active audit requires the precise candidate checkout.' }
    $candidate=[IO.Path]::GetFullPath($CandidateRoot)
    if (-not $candidate.StartsWith($resolvedRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $candidate '.git'))) { throw 'Candidate must be a Git checkout inside the governed root.' }
    Assert-OrdinaryAncestors $candidate
    $cacheRoot=if ($env:L12_WORK_CACHE) { $env:L12_WORK_CACHE } else { Join-Path $resolvedRoot 'cache/primary' }
    $dependencyRoot=if ($env:L12_DEPENDENCY_CACHE) { $env:L12_DEPENDENCY_CACHE } else { Join-Path $resolvedRoot 'cache/primary' }
    foreach($inputRoot in @($cacheRoot,$dependencyRoot)) {
        if (-not [IO.Path]::GetFullPath($inputRoot).StartsWith($resolvedRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Active cache must be inside the governed root.' }
    }
    # node_modules may be an existing shared dependency junction. Measure its
    # single ordinary target explicitly; never treat a link as zero bytes or
    # grant cleanup authority over that target.
    $frontendDependencies=Join-Path $candidate 'opcgpro-vue/node_modules'
    if (Test-Path -LiteralPath $frontendDependencies) {
        $dependencyItem=Get-Item -LiteralPath $frontendDependencies -Force
        if ($dependencyItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            if ($dependencyItem.LinkType -ne 'Junction' -or @($dependencyItem.Target).Count -ne 1) { throw 'Ambiguous frontend dependency link refused.' }
            $frontendDependencies=[IO.Path]::GetFullPath([string]$dependencyItem.Target)
            if (-not (Test-Path -LiteralPath $frontendDependencies -PathType Container)) { throw 'Frontend dependency target missing.' }
        }
        Assert-OrdinaryAncestors $frontendDependencies
    }
    $activeRows=@(
        @{ Name='candidate source'; Path=$candidate; Budget=2200MB; Exclude=@('.git','.tmp','.runtime','.l12-cache','node_modules','bin','obj','dist','dist-testrun') },
        @{ Name='candidate frontend dependencies'; Path=$frontendDependencies; Budget=220MB; Exclude=@() },
        @{ Name='shared NuGet packages'; Path=(Join-Path $dependencyRoot 'nuget/packages'); Budget=1200MB; Exclude=@() },
        @{ Name='shared NuGet HTTP'; Path=(Join-Path $dependencyRoot 'nuget/http'); Budget=200MB; Exclude=@() },
        @{ Name='shared npm'; Path=(Join-Path $dependencyRoot 'npm'); Budget=220MB; Exclude=@() },
        @{ Name='shared corepack'; Path=(Join-Path $dependencyRoot 'corepack'); Budget=200MB; Exclude=@() },
        @{ Name='task general temporary'; Path=(Join-Path $cacheRoot 'temp'); Budget=200MB; Exclude=@() },
        @{ Name='task dotnet home'; Path=(Join-Path $cacheRoot 'dotnet-home'); Budget=200MB; Exclude=@() },
        # A single hot configuration includes ~530 MiB native libraries.
        # 600 MiB admits that observed build but rejects Debug+Release copies.
        @{ Name='server bin'; Path=(Join-Path $candidate '服务端WebSocket/bin'); Budget=600MB; Exclude=@() },
        @{ Name='server obj'; Path=(Join-Path $candidate '服务端WebSocket/obj'); Budget=150MB; Exclude=@() },
        @{ Name='rules bin'; Path=(Join-Path $candidate 'TwelveLegions.Tests/bin'); Budget=600MB; Exclude=@() },
        @{ Name='rules obj'; Path=(Join-Path $candidate 'TwelveLegions.Tests/obj'); Budget=150MB; Exclude=@() },
        @{ Name='platform bin'; Path=(Join-Path $candidate 'TwelveLegions.Platform.Tests/bin'); Budget=600MB; Exclude=@() },
        @{ Name='platform obj'; Path=(Join-Path $candidate 'TwelveLegions.Platform.Tests/obj'); Budget=150MB; Exclude=@() },
        @{ Name='candidate dist'; Path=(Join-Path $candidate 'opcgpro-vue/dist'); Budget=100MB; Exclude=@() },
        @{ Name='candidate test dist'; Path=(Join-Path $candidate 'opcgpro-vue/dist-testrun'); Budget=100MB; Exclude=@() },
        @{ Name='owned test temporary'; Path=(Join-Path $cacheRoot 'test-temp'); Budget=200MB; Exclude=@() },
        @{ Name='test receipts'; Path=(Join-Path $cacheRoot 'test-evidence'); Budget=500MB; Exclude=@() }
    ) | ForEach-Object {
        Assert-OrdinaryAncestors $_.Path
        $bytes=Get-DirectoryBytes $_.Path $_.Exclude $true
        [pscustomobject]@{Path=$_.Name;MiB=[math]::Round($bytes/1MB,1);BudgetMiB=[math]::Round($_.Budget/1MB,1);Status=if($bytes -le $_.Budget){'OK'}else{'OVER'}}
    }
    Write-Host '[L12 storage audit] Legacy inventory (not deletion authority):'
    $rows | Format-Table -AutoSize
    if (@($rows | Where-Object Status -eq 'OVER').Count) { Write-Warning 'Legacy aggregate budgets exceeded. Preserve protected/unknown data and continue per-directory governance; do not erase it to pass a gate.' }
    $rows=$activeRows
}

$required = @('app', 'source-library', 'references', 'tools', 'cache', 'temp', 'artifacts', 'archives')
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $resolvedRoot $_)) })
$workspace = Join-Path $resolvedRoot 'workspace'
$workspaceItem = Get-Item -LiteralPath $workspace -Force -ErrorAction SilentlyContinue
$workspaceTarget = if ($workspaceItem -and ($workspaceItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    $workspaceItem.Target
} else { $null }

Write-Host "[L12 storage audit] root: $resolvedRoot"
$rows | Format-Table -AutoSize
if ($missing.Count) { Write-Warning "Missing required directories: $($missing -join ', ')" }
$workspaceValid=($workspaceTarget -and @($workspaceTarget).Count -eq 1 -and [IO.Path]::GetFullPath([string]$workspaceTarget).TrimEnd('\') -eq (Join-Path $resolvedRoot 'app'))
if (-not $workspaceTarget) { Write-Warning 'workspace is not a compatibility junction.' }
elseif (-not $workspaceValid) {
    Write-Warning "workspace points to an unexpected target: $workspaceTarget"
}

$over = @($rows | Where-Object Status -eq 'OVER')
if ($Strict -and ($missing.Count -gt 0 -or -not $workspaceValid -or $over.Count -gt 0)) { exit 1 }
