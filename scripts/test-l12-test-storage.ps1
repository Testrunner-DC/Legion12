[CmdletBinding()]
param([string]$FixtureParent='D:\GPT\Legion12\temp')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib/l12-test-storage.ps1')
$fixture=Join-Path $FixtureParent ('storage-lifecycle-'+[Guid]::NewGuid().ToString('N'))
$temp=Join-Path $fixture 'temporary'; $evidence=Join-Path $fixture 'evidence'; $outside=Join-Path $fixture 'outside'
New-Item -ItemType Directory -Path $temp,$evidence,$outside -Force | Out-Null
Set-Content -LiteralPath (Join-Path $outside 'keep.txt') -Value 'protected'
$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $oldTmpdir=$env:TMPDIR; $oldRunTemp=$env:L12_TEST_TEMP_ROOT; $passed=$false; $script:lockedFile=$null
function Assert([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Invoke-FakeTestHost {
    param([Parameter(ValueFromRemainingArguments)][string[]]$CommandArguments)
    $loggerIndex=[Array]::IndexOf($CommandArguments,'--logger')
    $resultsIndex=[Array]::IndexOf($CommandArguments,'--results-directory')
    if ($loggerIndex -lt 0 -or $resultsIndex -lt 0) { throw 'Missing TRX arguments' }
    $compilerIndex=[Array]::IndexOf($CommandArguments,'-p:UseSharedCompilation=false')
    $boundaryIndex=[Array]::IndexOf($CommandArguments,'--')
    Assert ($compilerIndex -gt 0 -and ($boundaryIndex -lt 0 -or $compilerIndex -lt $boundaryIndex)) 'Persistent compiler server is not disabled before testhost settings'
    Assert ($env:TEMP -eq $env:TMP -and $env:TEMP -eq $env:TMPDIR -and $env:TEMP -eq $env:L12_TEST_TEMP_ROOT) 'Cross-platform test temp isolation missing'
    $filename=([regex]::Match($CommandArguments[$loggerIndex+1],'LogFileName=([^;]+)')).Groups[1].Value
    $trx=Join-Path $CommandArguments[$resultsIndex+1] $filename
    New-Item -ItemType Directory -Path $CommandArguments[$resultsIndex+1] -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $env:TEMP 'fixture.db') -Value 'synthetic only'
    if ($script:mode -eq 'linked') { New-Item -ItemType Junction -Path (Join-Path $env:TEMP 'linked') -Target $outside | Out-Null }
    if ($script:mode -eq 'locked') { $script:lockedFile=[IO.File]::Open((Join-Path $env:TEMP 'fixture.db'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None) }
    if ($script:mode -ne 'missing') {
        $total=1; $count=1; $executed=1; $failed=0; $skipped=0
        switch ($script:mode) {
            'failed' { $count=0; $failed=1 }
            'skipped' { $count=0; $executed=0; $skipped=1 }
            'empty' { $total=0; $count=0; $executed=0 }
        }
        "<TestRun><ResultSummary><Counters total='$total' passed='$count' executed='$executed' failed='$failed' notExecuted='$skipped'/></ResultSummary></TestRun>" | Set-Content -LiteralPath $trx
    }
    $global:LASTEXITCODE=if ($script:mode -eq 'process-failed') { 1 } else { 0 }
}
try {
    foreach ($script:mode in @('passed','failed','skipped','empty','missing','process-failed','linked','locked')) {
        $failed=$false
        try { Invoke-L12TestRun -Executable 'Invoke-FakeTestHost' -Arguments @('test','fake.csproj','--','xUnit.ParallelizeTestCollections=false') -TemporaryBase $temp -EvidenceBase $evidence -Label $script:mode }
        catch { $failed=$true }
        finally { if ($null -ne $script:lockedFile) { $script:lockedFile.Dispose(); $script:lockedFile=$null } }
        Assert ($failed -eq ($script:mode -ne 'passed')) "Incorrect result for $script:mode"
        Assert ($env:TEMP -eq $oldTemp -and $env:TMP -eq $oldTmp -and $env:TMPDIR -eq $oldTmpdir -and $env:L12_TEST_TEMP_ROOT -eq $oldRunTemp) "Environment leaked for $script:mode"
        $receipt=@(Get-ChildItem -LiteralPath $evidence -Filter receipt.json -File -Recurse | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } | Where-Object label -EQ $script:mode)
        Assert ($receipt.Count -eq 1) "Missing receipt for $script:mode"
        Assert ((Test-Path -LiteralPath $receipt[0].temporaryPath) -eq ($script:mode -ne 'passed')) "Incorrect retention for $script:mode"
        Assert ($receipt[0].status -eq $(if ($script:mode -eq 'passed') { 'success' } else { 'retained' })) "Incorrect status for $script:mode"
        if ($script:mode -eq 'locked') { Assert ($receipt[0].cleanupAttempts -eq 3) 'Cleanup retries not bounded at three' }
        if ($script:mode -eq 'linked') { [IO.Directory]::Delete((Join-Path $receipt[0].temporaryPath 'linked')) }
        Assert ((Get-Content -LiteralPath (Join-Path $outside 'keep.txt')).Trim() -eq 'protected') 'External linked target damaged'
    }
    $bad=$false
    try { Invoke-L12TestRun -Executable 'dotnet' -Arguments @('test') -TemporaryBase $temp -EvidenceBase (Join-Path $temp 'bad') } catch { $bad=$true }
    Assert $bad 'Nested evidence accepted'
    $initializer=Join-Path $PSScriptRoot '../ops/windows/Initialize-L12BuildEnvironment.ps1'
    $names=@('TEMP','TMP','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME','L12_WORK_CACHE','L12_DEPENDENCY_CACHE','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_CLI_TELEMETRY_OPTOUT','NUGET_XMLDOC_MODE','npm_config_update_notifier','npm_config_fund')
    $saved=@{}; foreach ($name in $names) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
    try {
        & $initializer -CacheRoot (Join-Path $fixture 'task-a') -DependencyCacheRoot (Join-Path $fixture 'shared') | Out-Null
        $packages=$env:NUGET_PACKAGES; $npm=$env:npm_config_cache; $firstTemp=$env:TEMP
        & $initializer -CacheRoot (Join-Path $fixture 'task-b') | Out-Null
        Assert ($env:NUGET_PACKAGES -eq $packages -and $env:npm_config_cache -eq $npm) 'Dependencies duplicated per task'
        Assert ($env:TEMP -ne $firstTemp) 'Task output isolation lost'
        Assert (-not (Test-Path -LiteralPath (Join-Path $fixture 'task-b/nuget'))) 'Task-local packages directory created'
    } finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
    $passed=$true
    Write-Host '[L12 test storage] 8 lifecycle scenarios + nested-evidence rejection + shared-cache isolation passed.'
} finally {
    $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:TMPDIR=$oldTmpdir; $env:L12_TEST_TEMP_ROOT=$oldRunTemp
    # Controlled synthetic regression failures, not genuine failed test runs.
    if ($passed) { @(Get-L12PlainTestTree $fixture) | Out-Null; Remove-Item -LiteralPath $fixture -Recurse -Force }
    else { Write-Warning "Storage regression evidence retained: $fixture" }
}
