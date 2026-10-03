[CmdletBinding()]
param([string]$FixtureParent='D:\GPT\Legion12\temp')
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib/l12-test-storage.ps1')
$fixture=Join-Path $FixtureParent ('audit-test-'+[Guid]::NewGuid().ToString('N'))
$audit=Join-Path $PSScriptRoot 'audit-l12-storage.ps1'
$passed=$false
$savedCache=$env:L12_WORK_CACHE; $savedDependencies=$env:L12_DEPENDENCY_CACHE
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Run-Audit([string]$Scope='Active',[string]$Candidate="$fixture\app") {
    & pwsh -NoProfile -File $audit -Root $fixture -Strict -Scope $Scope -CandidateRoot $Candidate *> (Join-Path $fixture 'last-audit.log')
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
    $passed=$true
    Write-Host '[L12 storage audit] 11 scoped-budget, dependency, link, compatibility and legacy protection guards passed.'
} finally {
    $env:L12_WORK_CACHE=$savedCache; $env:L12_DEPENDENCY_CACHE=$savedDependencies
    if($passed) {
        [IO.Directory]::Delete("$fixture\workspace")
        if((Split-Path $fixture -Leaf) -notmatch '^audit-test-[0-9a-f]{32}$' -or -not [IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath($FixtureParent).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture root'}
        @(Get-L12PlainTestTree $fixture) | Out-Null
        Remove-Item -LiteralPath $fixture -Recurse -Force
    } else {Write-Warning "Synthetic audit failure retained: $fixture"}
}
