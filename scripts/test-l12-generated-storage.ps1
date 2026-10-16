[CmdletBinding()]
param([string]$EvidenceRoot='D:\GPT\Legion12\artifacts\storage-output-routing-20261016',[string]$ReportName=('guard-tests-'+[Guid]::NewGuid().ToString('N')+'.json'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repoRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'ops/windows/L12GeneratedStorage.ps1')
$null=Assert-L12GeneratedPath $EvidenceRoot
if([IO.Path]::GetFileName($ReportName) -ne $ReportName){throw 'Receipt must be a leaf filename'}
if(Test-Path -LiteralPath (Join-Path $EvidenceRoot $ReportName)){throw 'Existing test evidence preserved'}
New-Item -ItemType Directory -Path $EvidenceRoot -Force|Out-Null
$fixture=Join-Path $EvidenceRoot ('guard-fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture|Out-Null
$results=[Collections.Generic.List[object]]::new()
$variables=@('TEMP','TMP','TMPDIR','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME','L12_WORK_CACHE','L12_DEPENDENCY_CACHE','L12_ARTIFACT_ROOT','L12_ARCHIVE_ROOT','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_CLI_TELEMETRY_OPTOUT','NUGET_XMLDOC_MODE','npm_config_update_notifier','npm_config_fund')
$saved=@{};foreach($name in $variables){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
function Check([string]$Name,[scriptblock]$Action){&$Action;$results.Add([pscustomobject]@{name=$Name;passed=$true})}
function Reject-C([scriptblock]$Action){$rejected=$false;try{&$Action|Out-Null}catch{if($_.Exception.Message -notlike '*L12_OUTPUT_ON_C*' -and $_.Exception.Message -notlike '*L12_OUTPUT_UNSUPPORTED_PATH*'){throw};$rejected=$true};if(-not $rejected){throw 'Expected unsafe output rejection'}}
try{
    Check 'plain C output rejected, not created' { $candidate='C:\l12-output-guard-must-not-create-20261016';Reject-C {Assert-L12GeneratedPath $candidate};if(Test-Path -LiteralPath $candidate){throw 'C output unexpectedly exists'} }
    Check 'extended DOS C alias rejected' {Reject-C {Assert-L12GeneratedPath '\\?\C:\l12-output-guard-must-not-create-20261016'}}
    Check 'device C alias rejected' {Reject-C {Assert-L12GeneratedPath '\\.\C:\l12-output-guard-must-not-create-20261016'}}
    Check 'UNC alias rejected without network access' {Reject-C {Assert-L12GeneratedPath '\\localhost\C$\l12-output-guard-must-not-create-20261016'}}
    Check 'existing C source remains readable' {if(-not(Test-Path -LiteralPath 'C:\Users\neptu\Documents\ChatGPT\Legion12')){throw 'Source unavailable'}}
    Check 'D output accepted' {if((Assert-L12GeneratedPath (Join-Path $fixture 'new-d')) -notlike 'D:*'){throw 'Unexpected physical drive'}}
    Check 'extended DOS D output accepted' {if((Assert-L12GeneratedPath ('\\?\'+(Join-Path $fixture 'new-d'))) -ne (Join-Path $fixture 'new-d')){throw 'Extended non-C path normalization failed'}}
    Check 'C compatibility alias to D accepted without new writes' {if((Assert-L12GeneratedPath 'C:\Users\neptu\Documents\ChatGPT\Legion12\.cache\l12-guard-no-write') -notlike 'D:*'){throw 'Real non-C compatibility alias rejected'}}
    $actual=Join-Path $fixture 'actual';New-Item -ItemType Directory -Path $actual|Out-Null
    $alias=Join-Path $fixture 'good-alias';New-Item -ItemType Junction -Path $alias -Target $actual|Out-Null
    Check 'junction and missing suffix resolve physically' {if((Assert-L12GeneratedPath (Join-Path $alias 'not-yet-created')) -ne (Join-Path $actual 'not-yet-created')){throw 'Incorrect junction resolution'}}
    $bad=Join-Path $fixture 'looks-like-d';New-Item -ItemType Junction -Path $bad -Target 'C:\Users\neptu\Documents\ChatGPT\Legion12'|Out-Null
    Check 'D alias back to C rejected' {Reject-C {Assert-L12GeneratedPath (Join-Path $bad 'would-grow-on-c')}}
    $nestedBad=Join-Path $actual 'back-to-c';New-Item -ItemType Junction -Path $nestedBad -Target 'C:\Users\neptu\Documents\ChatGPT\Legion12'|Out-Null
    Check 'nested alias back to C rejected' {Reject-C {Assert-L12GeneratedPath (Join-Path $alias 'back-to-c/never-create')}}
    $initializer=Join-Path $repoRoot 'ops/windows/Initialize-L12BuildEnvironment.ps1'
    Check 'initializer rejects explicit C before directory creation' {Reject-C {&$initializer -CacheRoot 'C:\l12-output-guard-must-not-create-20261016' -DependencyCacheRoot (Join-Path $fixture 'dependencies')}}
    Check 'initializer rejects dependency C before creating valid cache' {$cache=Join-Path $fixture 'must-not-create';Reject-C {&$initializer -CacheRoot $cache -DependencyCacheRoot 'C:\l12-output-guard-must-not-create-20261016'};if(Test-Path -LiteralPath $cache){throw 'Partial cache was created'}}
    $env:L12_WORK_CACHE='C:\l12-output-guard-must-not-create-20261016'
    Check 'initializer rejects inherited C cache' {Reject-C {&$initializer -DependencyCacheRoot (Join-Path $fixture 'dependencies')}}
    $env:L12_WORK_CACHE=Join-Path $fixture 'work'
    $env:L12_ARTIFACT_ROOT='C:\l12-output-guard-must-not-create-20261016'
    Check 'initializer rejects inherited C evidence' {Reject-C {&$initializer -DependencyCacheRoot (Join-Path $fixture 'dependencies')}}
    $env:L12_ARTIFACT_ROOT=Join-Path $fixture 'evidence';$env:L12_ARCHIVE_ROOT=Join-Path $fixture 'archives'
    Check 'initializer rejects C archive before writing' {$env:L12_ARCHIVE_ROOT='C:\l12-output-guard-must-not-create-20261016';Reject-C {&$initializer -DependencyCacheRoot (Join-Path $fixture 'dependencies')};$env:L12_ARCHIVE_ROOT=Join-Path $fixture 'archives'}
    Check 'normal initializer keeps every generated path off C' {&$initializer -DependencyCacheRoot (Join-Path $fixture 'dependencies')|Out-Null;foreach($name in @('TEMP','TMP','TMPDIR','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME','L12_ARTIFACT_ROOT','L12_ARCHIVE_ROOT')){$null=Assert-L12GeneratedPath ([Environment]::GetEnvironmentVariable($name,'Process'))}}
    $receipt=[ordered]@{schema=1;status='completed';cases=@($results.ToArray());fixture=$fixture;sourceSha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot 'ops/windows/L12GeneratedStorage.ps1')).Hash;initializerSha256=(Get-FileHash -LiteralPath $initializer).Hash;completedUtc=[DateTime]::UtcNow.ToString('o');productOrOnlineWrites=0}
    $receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $EvidenceRoot $ReportName)
    foreach($link in @($nestedBad,$alias,$bad)){[IO.DirectoryInfo]::new($link).Delete()}
    if(-not $fixture.StartsWith($EvidenceRoot.TrimEnd('\')+'\guard-fixture-',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected fixture target'}
    if(@(Get-ChildItem -LiteralPath $fixture -Recurse -Force|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Unexpected fixture link remains'}
    Remove-Item -LiteralPath $fixture -Recurse -Force
    Write-Output "Generated storage guards passed: $($results.Count) cases; owned fixture removed"
}finally{foreach($name in $variables){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
