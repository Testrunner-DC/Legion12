[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$NasReceiptPath,
    [Parameter(Mandatory)][string]$RetirementManifestPath,
    [string]$ReceiptDirectory = 'D:\GPT\Legion12\artifacts\nas-storage\retirement',
    [switch]$Apply,
    [switch]$AllowLegacyCodeRetirement
)

$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
$receiptRoot=Assert-L12GeneratedPath -Path $ReceiptDirectory -Purpose 'NAS legacy retirement receipts'
if(-not(Test-Path -LiteralPath $receiptRoot -PathType Container)){New-Item -ItemType Directory -Path $receiptRoot -Force|Out-Null}
$receipt=Get-Content -LiteralPath (Assert-L12GeneratedPath -Path $NasReceiptPath -Purpose 'NAS completion receipt') -Raw|ConvertFrom-Json
if($receipt.status -notin @('verified-source-retained','verified-and-retired')){throw 'L12_LEGACY_RETIREMENT_NAS_PROOF_REQUIRED'}
$manifest=Get-Content -LiteralPath (Assert-L12GeneratedPath -Path $RetirementManifestPath -Purpose 'legacy retirement manifest') -Raw|ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.status -cne 'active' -or $manifest.items -isnot [array]){throw 'L12_LEGACY_RETIREMENT_MANIFEST_INVALID'}
$items=@($manifest.items)
foreach($item in $items){if($item.path -isnot [string] -or $item.action -notin @('disable-task','remove-state','retire-code')){throw 'L12_LEGACY_RETIREMENT_ITEM_INVALID'}}
if(@($items|Where-Object action -eq 'retire-code').Count -gt 0 -and -not $AllowLegacyCodeRetirement){throw 'L12_LEGACY_CODE_RETIREMENT_REQUIRES_SWITCH'}
$results=[Collections.Generic.List[object]]::new()
foreach($item in $items){
  $exists=if($item.action -eq 'disable-task'){ $null -ne (Get-ScheduledTask -TaskName $item.path -ErrorAction SilentlyContinue) } else { Test-Path -LiteralPath $item.path }
  $result=[ordered]@{path=$item.path;action=$item.action;existsBefore=$exists;changed=$false}
  if($Apply -and $exists){
    if($item.action -eq 'disable-task'){Unregister-ScheduledTask -TaskName $item.path -Confirm:$false -ErrorAction Stop;$result.changed=$true}
    elseif($item.action -eq 'remove-state'){Remove-Item -LiteralPath $item.path -Recurse -Force;$result.changed=$true}
    elseif($item.action -eq 'retire-code'){$retired=$item.path+'.retired';Move-Item -LiteralPath $item.path -Destination $retired -Force;$result.changed=$true;$result.retiredPath=$retired}
  }
  $results.Add([pscustomobject]$result)
}
$out=[ordered]@{schema=1;status=if($Apply){'retirement-applied'}else{'retirement-planned'};nasReceipt=$NasReceiptPath;items=@($results);createdUtc=[DateTimeOffset]::UtcNow.ToString('o')}
$outPath=Join-Path $receiptRoot ('legacy-retirement-'+[Guid]::NewGuid().ToString('N')+'.json')
$out|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $outPath -Encoding utf8
[pscustomobject]@{status=$out.status;receiptPath=$outPath;changed=@($results|Where-Object changed).Count}
