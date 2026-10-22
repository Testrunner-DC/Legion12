[CmdletBinding()]
param([string]$FixtureParent='D:\GPT\Legion12\temp')
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib/l12-test-storage.ps1')
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'ops/windows/L12GeneratedStorage.ps1')
$FixtureParent=Assert-L12GeneratedPath -Path $FixtureParent -Purpose 'storage-audit synthetic fixtures'
$fixture=Join-Path $FixtureParent ('audit-test-'+[Guid]::NewGuid().ToString('N'))
$null=Assert-L12GeneratedPath -Path $fixture -Purpose 'storage-audit isolated fixture'
$audit=Join-Path $PSScriptRoot 'audit-l12-storage.ps1'
$passed=$false
$savedCache=$env:L12_WORK_CACHE; $savedDependencies=$env:L12_DEPENDENCY_CACHE
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Run-Audit([string]$Scope='Active',[string]$Candidate="$fixture\app",[string]$Script=$audit) {
    & pwsh -NoProfile -File $Script -Root $fixture -Strict -Scope $Scope -CandidateRoot $Candidate *> (Join-Path $fixture 'last-audit.log')
    return $LASTEXITCODE
}
function Sized-Fixture([string]$Path,[long]$Bytes) {
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew)
    try {$stream.SetLength($Bytes)}finally{$stream.Dispose()}
}
try {
    foreach($name in @('app','source-library','references','tools','cache','temp','artifacts','archives','outside')) { New-Item -ItemType Directory -Path (Join-Path $fixture $name) -Force | Out-Null }
    & git -C "$fixture\app" init -q
    if($LASTEXITCODE -ne 0){throw 'Fixture Git initialization failed'}
    New-Item -ItemType Junction -Path "$fixture\workspace" -Target "$fixture\app" | Out-Null
    $env:L12_WORK_CACHE="$fixture\cache\task"; $env:L12_DEPENDENCY_CACHE="$fixture\cache\shared"
    Assert ((Run-Audit) -eq 0) 'Empty governed fixture rejected'
    Sized-Fixture "$fixture\app\opcgpro-vue\node_modules\synthetic.bin" (221MB)
    Assert ((Run-Audit) -ne 0) 'Candidate frontend dependency overage silently passed'
    Remove-Item -LiteralPath "$fixture\app\opcgpro-vue\node_modules\synthetic.bin"
    [IO.Directory]::Delete("$fixture\app\opcgpro-vue\node_modules")
    Sized-Fixture "$fixture\outside\shared-node\synthetic.bin" (221MB)
    New-Item -ItemType Junction -Path "$fixture\app\opcgpro-vue\node_modules" -Target "$fixture\outside\shared-node" | Out-Null
    Assert ((Run-Audit) -ne 0) 'Shared frontend dependency junction counted as zero'
    Remove-Item -LiteralPath "$fixture\outside\shared-node\synthetic.bin"
    Assert ((Run-Audit) -eq 0) 'Ordinary shared dependency target rejected'
    [IO.Directory]::Delete("$fixture\app\opcgpro-vue\node_modules")
    Sized-Fixture "$fixture\cache\task\temp\synthetic.bin" (201MB)
    Assert ((Run-Audit) -ne 0) 'Task general temporary overage silently passed'
    Remove-Item -LiteralPath "$fixture\cache\task\temp\synthetic.bin"
    Sized-Fixture "$fixture\app\TwelveLegions.Tests\bin\synthetic.bin" (601MB)
    Assert ((Run-Audit) -ne 0) 'Backend output overage silently passed'
    Remove-Item -LiteralPath "$fixture\app\TwelveLegions.Tests\bin\synthetic.bin"
    New-Item -ItemType Junction -Path "$fixture\app\unexpected-linked-source" -Target "$fixture\outside" | Out-Null
    Assert ((Run-Audit) -ne 0) 'Active linked descendant silently counted as zero'
    [IO.Directory]::Delete("$fixture\app\unexpected-linked-source")
    New-Item -ItemType Junction -Path "$fixture\linked-candidate" -Target "$fixture\app" | Out-Null
    Assert ((Run-Audit -Candidate "$fixture\linked-candidate") -ne 0) 'Linked candidate ancestor accepted'
    [IO.Directory]::Delete("$fixture\linked-candidate")
    [IO.Directory]::Delete("$fixture\workspace")
    New-Item -ItemType Junction -Path "$fixture\workspace" -Target "$fixture\outside" | Out-Null
    Assert ((Run-Audit) -ne 0) 'Wrong compatibility target accepted'
    [IO.Directory]::Delete("$fixture\workspace")
    New-Item -ItemType Junction -Path "$fixture\workspace" -Target "$fixture\app" | Out-Null
    Sized-Fixture "$fixture\cache\unknown-protected-legacy.bin" (1201MB)
    Assert ((Run-Audit) -eq 0) 'Explicit Active scope confused legacy inventory with candidate budget'
    Assert ((Run-Audit -Scope 'Inventory') -ne 0) 'Original inventory aggregate threshold weakened'
    Assert (Test-Path -LiteralPath "$fixture\cache\unknown-protected-legacy.bin") 'Read-only audit removed unknown data'
    # Instrument only this disposable test copy. No production audit switch or
    # environment bypass is introduced. A distinct candidate makes app itself
    # unrelated legacy data, in addition to aggregate cache/artifact roots.
    $candidate="$fixture\candidate"
    New-Item -ItemType Directory -Path $candidate | Out-Null
    & git -C $candidate init -q
    if($LASTEXITCODE -ne 0){throw 'Scope fixture Git initialization failed'}
    $trace=Join-Path $fixture 'scope-scan-paths.log'
    $probe=Join-Path $fixture 'audit-scope-probe.ps1'
    $header='function Get-DirectoryBytes([string]$Path, [string[]]$ExcludedDirectories=@(), [bool]$RejectLinks=$false) {'
    $injection=@'

    [IO.File]::AppendAllText('__TRACE__',([IO.Path]::GetFullPath($Path)+[Environment]::NewLine))
    $legacyOnly=@((Join-Path $resolvedRoot 'app'),(Join-Path $resolvedRoot 'cache'),(Join-Path $resolvedRoot 'artifacts/test-runs'),(Join-Path $resolvedRoot 'artifacts/deploy'),(Join-Path $resolvedRoot 'temp'))
    if($Scope -eq 'Active' -and [IO.Path]::GetFullPath($Path) -in $legacyOnly){throw 'TEST_UNRELATED_LEGACY_SCAN'}
'@
    $original=[IO.File]::ReadAllText($audit)
    Assert (($original.Split($header).Count-1) -eq 1) 'Audit test instrumentation header changed'
    [IO.File]::WriteAllText($probe,$original.Replace($header,$header+$injection.Replace('__TRACE__',$trace.Replace("'","''"))),[Text.UTF8Encoding]::new($false))
    Assert ((Run-Audit -Candidate $candidate -Script $probe) -eq 0) 'Active scanned unrelated legacy app/cache/artifacts'
    $activeScans=@([IO.File]::ReadAllLines($trace))
    Assert ($activeScans.Count -eq 18) 'Active did not measure exactly the existing eighteen active rows'
    Assert ($activeScans -contains $candidate) 'Active candidate source was not actually measured'
    Assert ($activeScans -contains "$fixture\cache\shared\nuget\packages") 'Active shared dependency measurement was bypassed'
    Assert ($activeScans -contains "$candidate\服务端WebSocket\bin") 'Active backend budget measurement was bypassed'
    Remove-Item -LiteralPath $trace
    Assert ((Run-Audit -Scope 'Inventory' -Candidate $candidate -Script $probe) -ne 0) 'Inventory stopped enforcing the legacy aggregate budget'
    $inventoryScans=@([IO.File]::ReadAllLines($trace))
    Assert ($inventoryScans.Count -eq 9) 'Inventory did not measure exactly the original nine rows'
    foreach($legacy in @("$fixture\app","$fixture\cache","$fixture\artifacts\test-runs","$fixture\artifacts\deploy","$fixture\temp")) {
        Assert ($inventoryScans -contains $legacy) 'Inventory silently skipped an original aggregate root'
    }
    Assert (Test-Path -LiteralPath "$fixture\cache\unknown-protected-legacy.bin") 'Instrumented audit removed protected legacy data'
    # Negative control: restoring the former unconditional legacy calculation
    # in another test copy must trip the real Active interception immediately.
    $inventoryBranch='if ($Scope -eq ''Inventory'') {'
    Assert (($original.Split($inventoryBranch).Count-1) -eq 1) 'Inventory branch instrumentation changed'
    $regressedProbe=Join-Path $fixture 'audit-unbounded-negative-control.ps1'
    $regressed=$original.Replace($inventoryBranch,'if ($Scope -in @(''Inventory'',''Active'')) {')
    [IO.File]::WriteAllText($regressedProbe,$regressed.Replace($header,$header+$injection.Replace('__TRACE__',$trace.Replace("'","''"))),[Text.UTF8Encoding]::new($false))
    Assert ((Run-Audit -Candidate $candidate -Script $regressedProbe) -ne 0) 'Unbounded Active negative control did not fail'
    Assert (([IO.File]::ReadAllText((Join-Path $fixture 'last-audit.log'))) -like '*TEST_UNRELATED_LEGACY_SCAN*') 'Negative control failed for an unrelated reason'
    $passed=$true
    Write-Host '[L12 storage audit] 11 scoped-budget, dependency, link, compatibility and legacy protection guards passed.'
    Write-Host '[L12 storage audit] Actual instrumented subprocesses: Active measured only 18 active rows; Inventory measured all original 9 legacy rows and retained its over-budget failure.'
    Write-Host '[L12 storage audit] Negative control: a regressed unbounded Active copy was rejected by the actual legacy-scan interception.'
} finally {
    $env:L12_WORK_CACHE=$savedCache; $env:L12_DEPENDENCY_CACHE=$savedDependencies
    if($passed) {
        [IO.Directory]::Delete("$fixture\workspace")
        if((Split-Path $fixture -Leaf) -notmatch '^audit-test-[0-9a-f]{32}$' -or -not [IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath($FixtureParent).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture root'}
        @(Get-L12PlainTestTree $fixture) | Out-Null
        Remove-Item -LiteralPath $fixture -Recurse -Force
    } else {Write-Warning "Synthetic audit failure retained: $fixture"}
}
