[CmdletBinding()]
param([string]$EvidenceRoot='D:/GPT/Legion12/artifacts/storage-output-routing-20261016/audit-20261022')
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
. "$repo/ops/windows/L12GeneratedStorage.ps1"
$evidence=Assert-L12GeneratedPath -Path $EvidenceRoot -Purpose 'retired storage entry negative tests'
$receipt=Join-Path $evidence 'legacy-entry-retirement-tests.json'
if(Test-Path -LiteralPath $receipt){throw 'Preserve existing test receipt; use a new explicit evidence directory'}
$script=Join-Path $repo 'ops/windows/maintain-l12-storage.ps1'
$cases=@(
 @{name='default preview stops';args=@{}},
 @{name='apply stops';args=@{Apply=$true}},
 @{name='nonexistent root stops before path access';args=@{Root='D:/l12-retired-entry-must-not-create-20261022';Apply=$true}},
 @{name='C root apply stops';args=@{Root='C:/';Apply=$true}},
 @{name='D root apply stops';args=@{Root='D:/';Apply=$true}},
 @{name='E root apply stops';args=@{Root='E:/';Apply=$true}},
 @{name='zero age does not reactivate';args=@{Apply=$true;TestRunRetentionDays=0;TempRetentionHours=0;LogRetentionDays=0}},
 @{name='zero keep counts do not reactivate';args=@{Apply=$true;DeployArtifactsToKeep=0;SnapshotSetsToKeep=0;LogsToKeep=0}}
)
$results=[Collections.Generic.List[object]]::new()
foreach($case in $cases){
 $rejected=$false;$arguments=$case.args
 try{& $script @arguments|Out-Null}catch{if(-not $_.Exception.Message.StartsWith('L12_STORAGE_LEGACY_ENTRY_RETIRED:')){throw};$rejected=$true}
 if(-not $rejected){throw ('Retired entry executed: '+$case.name)}
 $results.Add([pscustomobject]@{name=$case.name;passed=$true})
}
if($results.Count -ne 8 -or (Test-Path -LiteralPath 'D:/l12-retired-entry-must-not-create-20261022')){throw 'Retired entry contract failed'}
$body=@{status='passed';cases=@($results);sourceSha256=(Get-FileHash -LiteralPath $script).Hash;mutationsByRetiredEntry=0;completedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Depth 5
$file=[IO.File]::Open($receipt,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($body);$file.Write($bytes);$file.Flush($true)}finally{$file.Dispose()}
Write-Output "Retired storage entry passed: $($results.Count) negative cases; no scan or deletion."
