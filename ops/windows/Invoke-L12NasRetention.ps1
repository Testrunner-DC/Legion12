[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PolicyPath,
    [ValidateSet('Plan','Apply')][string]$Mode = 'Plan',
    [string]$ReceiptDirectory = 'D:\GPT\Legion12\artifacts\nas-storage\retention',
    [ValidateRange(2,20)][int]$KeepLatestGroups = 2,
    [ValidateRange(7,3650)][int]$UnpinnedMaxAgeDays = 90
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
$script:ReceiptRoot = Assert-L12GeneratedPath -Path $ReceiptDirectory -Purpose 'NAS retention receipts'
if (-not (Test-Path -LiteralPath $script:ReceiptRoot -PathType Container)) { New-Item -ItemType Directory -Path $script:ReceiptRoot -Force | Out-Null }

function Assert-NasRoot([string]$Path) {
    if ($Path -notmatch '^\\\\[^\\]+\\[^\\]+') { throw 'L12_NAS_RETENTION_DESTINATION_NOT_UNC' }
    if ($Path -match '(^|\\)\.\.($|\\)') { throw 'L12_NAS_RETENTION_TRAVERSAL' }
    return $Path.TrimEnd('\')
}

function Write-Receipt($Value,[string]$Name) {
    $path=Join-Path $script:ReceiptRoot $Name
    $partial=$path+'.partial-'+[Guid]::NewGuid().ToString('N')
    $Value|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $partial -Encoding utf8
    Move-Item -LiteralPath $partial -Destination $path -Force
    return $path
}

$policyPath=Assert-L12GeneratedPath -Path $PolicyPath -Purpose 'NAS retention policy'
$policy=Get-Content -LiteralPath $policyPath -Raw|ConvertFrom-Json
if($policy.schema -ne 1 -or $policy.status -cne 'active' -or $policy.destinationRoot -isnot [string]){throw 'L12_NAS_RETENTION_POLICY_INVALID'}
$root=Assert-NasRoot $policy.destinationRoot
$keepLatest = if($PSBoundParameters.ContainsKey('KeepLatestGroups')) { $KeepLatestGroups } elseif($policy.keepLatestGroups) { [int]$policy.keepLatestGroups } else { 2 }
$maxAge = if($PSBoundParameters.ContainsKey('UnpinnedMaxAgeDays')) { $UnpinnedMaxAgeDays } elseif($policy.unpinnedMaxAgeDays) { [int]$policy.unpinnedMaxAgeDays } else { 90 }
if($keepLatest -lt 2 -or $keepLatest -gt 20 -or $maxAge -lt 7 -or $maxAge -gt 3650){throw 'L12_NAS_RETENTION_LIMITS'}
$protectedPrefixes=@($policy.protectedPrefixes)
if(@($protectedPrefixes).Length -lt 1){$protectedPrefixes=@('current','latest','proof','failure','failed','pinned','private','unresolved')}
$runId=[Guid]::NewGuid().ToString('N')
$lockPath=Join-Path $script:ReceiptRoot 'nas-retention.lock';$lock=$null
try{
  $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  if(-not(Test-Path -LiteralPath $root -PathType Container)){
    $r=[ordered]@{schema=1;runId=$runId;status='paused-nas-unavailable';mode=$Mode;destination=$root;createdUtc=[DateTimeOffset]::UtcNow.ToString('o')}
    $p=Write-Receipt $r ('nas-retention-'+$runId+'.paused.json');return [pscustomobject]@{status=$r.status;receiptPath=$p;deleted=0}
  }
  $groupsRoot=Join-Path $root 'groups'
  $groupDirs=[Collections.Generic.List[object]]::new()
  foreach($d in @(Get-ChildItem -LiteralPath $groupsRoot -Directory -Force -ErrorAction SilentlyContinue)){ $groupDirs.Add($d) }
  # Legacy in-place batches: any directory outside groups\ that carries its own immutable manifest is governed by the same rule.
  foreach($d in @(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue)){
    if($d.Name -ieq 'groups'){continue}
    if(Test-Path -LiteralPath (Join-Path $d.FullName 'nas-group.json') -PathType Leaf){ $groupDirs.Add($d) }
    foreach($s in @(Get-ChildItem -LiteralPath $d.FullName -Directory -Force -ErrorAction SilentlyContinue)){
      if(Test-Path -LiteralPath (Join-Path $s.FullName 'nas-group.json') -PathType Leaf){ $groupDirs.Add($s) }
    }
  }
  $groups=@($groupDirs|ForEach-Object{
    $meta=Join-Path $_.FullName 'nas-group.json'
    if(-not(Test-Path -LiteralPath $meta -PathType Leaf)){return}
    try{$m=Get-Content -LiteralPath $meta -Raw|ConvertFrom-Json}catch{return}
    if($m.status -cne 'verified' -or $m.cleanupEligible -eq $false -or @($m.files).Length -lt 1){return}
    # PowerShell 7.5+ ConvertFrom-Json materialises ISO-8601 strings as [datetime]; accept either shape.
    $completed=$null
    if($m.completedUtc -is [datetime]){ $completed=[DateTimeOffset]$m.completedUtc }
    elseif(($m.completedUtc -is [string]) -and $m.completedUtc.Length -gt 0){ try{ $completed=[DateTimeOffset]::Parse($m.completedUtc) }catch{ return } }
    if($null -eq $completed){ return }
    [pscustomobject]@{name=$_.Name;path=$_.FullName;completedUtc=$completed;pinned=([bool]$m.pinned);manifest=$m}
  })|Sort-Object completedUtc -Descending
  $keep=@($groups|Select-Object -First $keepLatest)
  $cutoff=[DateTimeOffset]::UtcNow.AddDays(-$maxAge)
  $candidates=@($groups|Where-Object{$keep -notcontains $_ -and -not $_.pinned -and $_.completedUtc -lt $cutoff -and ($protectedPrefixes -notcontains $_.name.ToLowerInvariant())})
  $candidate=if(@($candidates).Length -gt 0){$candidates|Select-Object -First 1}else{$null}
  $result=[ordered]@{schema=1;runId=$runId;status=if($candidate){'candidate-found'}else{'nothing-eligible'};mode=$Mode;destination=$root;keepLatestGroups=$keepLatest;unpinnedMaxAgeDays=$maxAge;groupCount=@($groups).Length;kept=@($keep|ForEach-Object Name);candidate=if($candidate){$candidate.name}else{$null};deleted=$false;createdUtc=[DateTimeOffset]::UtcNow.ToString('o')}
  if($Mode -eq 'Apply' -and $candidate){
    # The immutable group manifest must be the deletion proof; no age-only deletion is allowed.
    foreach($file in @($candidate.manifest.files)){
      $path=Join-Path $candidate.path $file.path
      if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'L12_NAS_RETENTION_PROOF_MISSING'}
      $item=Get-Item -LiteralPath $path -Force
      if([long]$item.Length -ne [long]$file.bytes){throw 'L12_NAS_RETENTION_PROOF_BYTES'}
      if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$file.sha256){throw 'L12_NAS_RETENTION_PROOF_SHA'}
    }
    Remove-Item -LiteralPath $candidate.path -Recurse -Force
    if(Test-Path -LiteralPath $candidate.path){throw 'L12_NAS_RETENTION_DELETE_FAILED'}
    $result.status='deleted-one-verified-group';$result.deleted=$true
  }
  $p=Write-Receipt $result ('nas-retention-'+$runId+'.json');[pscustomobject]@{status=$result.status;receiptPath=$p;deleted=[int][bool]$result.deleted}
}catch{
  $r=[ordered]@{schema=1;runId=$runId;status='failed-preserved';mode=$Mode;error=$_.Exception.Message;createdUtc=[DateTimeOffset]::UtcNow.ToString('o')}
  $p=Write-Receipt $r ('nas-retention-'+$runId+'.failure.json');throw ('L12_NAS_RETENTION_FAILED receipt='+$p+' error='+$_.Exception.Message)
}finally{if($lock){$lock.Dispose()}}
