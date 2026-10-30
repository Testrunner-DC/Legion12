[CmdletBinding()]
param([string]$OutputRoot='D:/GPT/Legion12/artifacts/server-health-20261029/cloud-storage/baidu-resume-20261030')
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'ops/windows/Initialize-L12BuildEnvironment.ps1')
Assert-L12GeneratedPath -Path $OutputRoot|Out-Null
New-Item -ItemType Directory -Path $OutputRoot -Force|Out-Null
$modulePath=Join-Path $repo 'ops/windows/L12BaiduNetdisk.psm1'
Import-Module $modulePath -Force;$module=Get-Module L12BaiduNetdisk
$root='E:/Legion12-ColdStorage/l12-baidu-offline-tests-'+[Guid]::NewGuid().ToString('N')
Assert-L12GeneratedPath -Path $root|Out-Null
$root=& $module {param($p) New-L12BaiduPrivateDirectory $p} $root
$checks=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[scriptblock]$Body){try{& $Body;$checks.Add(@{name=$Name;passed=$true})}catch{$checks.Add(@{name=$Name;passed=$false;error=$_.Exception.Message});throw}}
function Assert([bool]$Condition,[string]$Message='assertion failed'){if(!$Condition){throw $Message}}
function Reject([scriptblock]$Body,[string]$Code=''){try{& $Body|Out-Null}catch{if($Code -and $_.Exception.Message -cnotlike ($Code+'*')){throw ('wrong rejection: '+$_.Exception.Message)};return};throw 'expected rejection'}
function In-Module([scriptblock]$Code,$Arg=$null){& $module $Code $Arg}
$credential=Join-Path $root 'test-oauth.dpapi'
$fake=[pscustomobject]@{version=1;appId='124583157';appName='十二军团私有备份';clientId='synthetic-client';clientSecret='synthetic-secret';accessToken='synthetic-access';refreshToken='synthetic-refresh';scope='basic netdisk';expiresUtc=[DateTime]::UtcNow.AddDays(2).ToString('o')}
In-Module {param($a) Write-L12BaiduProtected $a.path $a.value} @{path=$credential;value=$fake}
$source=Join-Path $root 'source';$source=In-Module {param($p) New-L12BaiduPrivateDirectory $p} $source
$random=[Random]::new(20261030);$definitions=@();$sourceBytes=@{}
foreach($size in @((8MB+123),512,300)){
 [byte[]]$bytes=[byte[]]::new($size);$random.NextBytes($bytes);[Text.Encoding]::ASCII.GetBytes("age-encryption.org/v1`n").CopyTo($bytes,0)
 $hash=In-Module {param($b) Get-L12BaiduSha $b} $bytes;$name=$hash+'.age';[IO.File]::WriteAllBytes((Join-Path $source $name),$bytes);$sourceBytes[$name]=$bytes
 $role=if(!$definitions.Count){'archive'}elseif($definitions.Count -eq 1){'completion-manifest'}else{'evidence'}
 $definitions+=@{roles=@($role);plaintext=@{bytes=$size-200;sha256=('1'*64)};cipher=@{fileName=$name;sha256=$hash;bytes=$size}}
}
$archiveSha='2'*64;$manifestPath=Join-Path $source ($archiveSha+'.manifest.json')
$manifest=@{schema=2;format='l12-manual-cloud-archive-v2';status='prepared-offline';archiveSha256=$archiveSha;items=$definitions}
[IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 10 -Compress),[Text.UTF8Encoding]::new($false))
$manifestSha=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant();$sourceBytes[[IO.Path]::GetFileName($manifestPath)]=[IO.File]::ReadAllBytes($manifestPath)
$mock=[pscustomobject]@{calls=[Collections.Generic.List[object]]::new();downloadStarts=[Collections.Generic.List[long]]::new();files=@{};tasks=@{};pieces=@{};nextId=1000;failPart=-1;failPartTimes=0;badMd5=$false;badPathEcho=$false;locateLifetime=60;uploadPauseOnceMs=0;loseCreate=$false;skipCreate=$false;downloadShort=0;rangeMode='normal';refreshCount=0;loseRefresh=$false;failWrite='';failWriteTimes=0;failDelete='';failDeleteTimes=0;wire=$false;wireBytes=[byte[]]::new(0);wireRedirect=$null;wireStatus=200;sources=$sourceBytes}
In-Module {
 param($m)
 $script:Mock=$m;$script:OriginalJson=(Get-Command Invoke-L12BaiduJson).ScriptBlock;$script:OriginalHttp=(Get-Command Invoke-L12BaiduHttp).ScriptBlock;$script:OriginalWrite=(Get-Command Write-L12BaiduProtected).ScriptBlock;$script:OriginalRemoveMarker=(Get-Command Remove-L12BaiduRefreshMarker).ScriptBlock
 function script:Query([string]$Text){$map=@{};foreach($piece in $Text.TrimStart('?').Split('&')){$pair=$piece.Split('=',2);$map[[Net.WebUtility]::UrlDecode($pair[0])]=if($pair.Length -eq 2){[Net.WebUtility]::UrlDecode($pair[1])}else{''}};return $map}
 function script:Write-L12BaiduProtected([string]$Path,$Value){if($script:Mock.failWriteTimes -gt 0 -and $Path.EndsWith($script:Mock.failWrite)){$script:Mock.failWriteTimes--;throw 'L12_TEST_DURABLE_WRITE_FAILURE'};& $script:OriginalWrite $Path $Value}
 function script:Remove-L12BaiduRefreshMarker([string]$Path){if($script:Mock.failDeleteTimes -gt 0 -and $Path.EndsWith($script:Mock.failDelete)){$script:Mock.failDeleteTimes--;throw 'L12_TEST_MARKER_CLEANUP_FAILURE'};& $script:OriginalRemoveMarker $Path}
 function script:Invoke-L12BaiduJson($Context,[string]$Method,[string]$Url,[Net.Http.HttpContent]$Content=$null,[switch]$OAuth){
  if($script:Mock.wire){return & $script:OriginalJson $Context $Method $Url $Content -OAuth:$OAuth}
  $q=Query ([Uri]$Url).Query;$form=@{};if($Content -is [Net.Http.FormUrlEncodedContent]){$form=Query ($Content.ReadAsStringAsync().GetAwaiter().GetResult())}
  $operation=if($q.ContainsKey('grant_type')){$q.grant_type}else{$q.method}
  $script:Mock.calls.Add(@{operation=$operation;part=if($q.ContainsKey('partseq')){[int]$q.partseq}else{-1};path=if($form.ContainsKey('path')){$form.path}elseif($q.ContainsKey('path')){$q.path}else{''}})
  switch($operation){
   'precreate'{$id='task-'+[Guid]::NewGuid().ToString('N');$parts=@($form.block_list|ConvertFrom-Json);$script:Mock.tasks[$id]=@{path=$form.path;parts=$parts;bytes=[long]$form.size};$script:Mock.pieces[$id]=@{};$reply=[pscustomobject]@{errno=0;uploadid=$id;block_list=@(0..($parts.Count-1))};if($script:Mock.badPathEcho){$reply|Add-Member -NotePropertyName path -NotePropertyValue '/apps/wrong/file.age'};return $reply}
   'locateupload'{return [pscustomobject]@{error_code=0;servers=@(@{server='https://c.pcs.baidu.com'});expire=$script:Mock.locateLifetime}}
   'upload'{
    $task=$script:Mock.tasks[$q.uploadid];$part=[int]$q.partseq
    if($script:Mock.uploadPauseOnceMs -gt 0){$delay=$script:Mock.uploadPauseOnceMs;$script:Mock.uploadPauseOnceMs=0;Start-Sleep -Milliseconds $delay}
    if($part -eq $script:Mock.failPart -and $script:Mock.failPartTimes -gt 0){$script:Mock.failPartTimes--;throw 'L12_BAIDU_TRANSPORT_UNCERTAIN'}
    $enumerator=$Content.GetEnumerator();$null=$enumerator.MoveNext();[byte[]]$data=$enumerator.Current.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $digest=[Convert]::ToHexString([Security.Cryptography.MD5]::HashData($data)).ToLowerInvariant()
    if($digest -cne $task.parts[$part]){throw 'mock received wrong part bytes'};$script:Mock.pieces[$q.uploadid][$part]=$data
    return [pscustomobject]@{md5=$(if($script:Mock.badMd5){'0'*32}else{$digest})}
   }
   'create'{
    if($script:Mock.skipCreate){$script:Mock.skipCreate=$false;throw 'L12_BAIDU_TRANSPORT_UNCERTAIN'}
    if($script:Mock.files.ContainsKey($form.path)){throw 'L12_BAIDU_API_-8'}
    $task=$script:Mock.tasks[$form.uploadid];$stream=[IO.MemoryStream]::new();try{for($i=0;$i -lt $task.parts.Count;$i++){$data=$script:Mock.pieces[$form.uploadid][$i];$stream.Write($data,0,$data.Length)};$data=$stream.ToArray()}finally{$stream.Dispose()}
    $script:Mock.nextId++;$record=[pscustomobject]@{path=$form.path;size=$data.Length;isdir=0;fs_id=$script:Mock.nextId;block_list=$task.parts;dlink=('https://d.pcs.baidu.com/file/mock?id='+$script:Mock.nextId);bytes=$data};$script:Mock.files[$form.path]=$record
    if($script:Mock.loseCreate){$script:Mock.loseCreate=$false;throw 'L12_BAIDU_TRANSPORT_UNCERTAIN'}
    return [pscustomobject]@{errno=0;path=$form.path;size=$data.Length;fs_id=$record.fs_id}
   }
   'filemetas'{$paths=@($form.target|ConvertFrom-Json);$path=$paths[0];if(!$script:Mock.files.ContainsKey($path)){throw 'L12_BAIDU_API_-9'};return [pscustomobject]@{errno=0;info=@($script:Mock.files[$path])}}
   'refresh_token'{$script:Mock.refreshCount++;if($script:Mock.loseRefresh){throw 'L12_BAIDU_TRANSPORT_UNCERTAIN'};return [pscustomobject]@{access_token='synthetic-access-new';refresh_token='synthetic-refresh-new';scope='basic netdisk';expires_in=2592000}}
   default{throw ('Unexpected mock operation '+$operation)}
  }
 }
 function script:Invoke-L12BaiduHttp($Context,[string]$Method,[string]$Url,[Net.Http.HttpContent]$Content=$null,[switch]$OAuth,[Nullable[long]]$RangeStart=$null){
  $null=Assert-L12BaiduUri $Url -OAuth:$OAuth
  $response=[Net.Http.HttpResponseMessage]::new()
  if($script:Mock.wire){
   $response.StatusCode=$script:Mock.wireStatus;$response.Content=[Net.Http.ByteArrayContent]::new($script:Mock.wireBytes);$response.Content.Headers.TryAddWithoutValidation('Content-Type','application/json; charset=utf8')|Out-Null
   if($script:Mock.wireRedirect){$response.Headers.Location=[Uri]$script:Mock.wireRedirect}
  }else{
   $id=(Query ([Uri]$Url).Query).id;$entry=@($script:Mock.files.Values|Where-Object{[string]$_.fs_id -ceq $id})[0];$start=[long]$RangeStart
   $script:Mock.downloadStarts.Add($start)
   $remaining=$entry.bytes.Length-$start;$copyLength=$remaining
   if($script:Mock.downloadShort -gt 0){$copyLength=[Math]::Min($copyLength,$script:Mock.downloadShort);$script:Mock.downloadShort=0}
   [byte[]]$data=[byte[]]::new($copyLength);[Array]::Copy($entry.bytes,$start,$data,0,$copyLength)
   $response.StatusCode=if($start -gt 0 -and $script:Mock.rangeMode -ne 'ignore'){206}else{200}
   $response.Content=[Net.Http.ByteArrayContent]::new($data);$response.Content.Headers.ContentLength=$remaining
   if([int]$response.StatusCode -eq 206){$offset=if($script:Mock.rangeMode -eq 'wrong'){$start+1}else{$start};$response.Content.Headers.ContentRange=[Net.Http.Headers.ContentRangeHeaderValue]::new($offset,$entry.bytes.Length-1,$entry.bytes.Length)}
  }
  return [pscustomobject]@{response=$response;request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,$Url);cancel=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(30))}
 }
} $mock
$baseArgs=@{ManifestPath=$manifestPath;ExpectedManifestSha256=$manifestSha;CredentialPath=$credential;TimeBudgetSeconds=120}
$transfer=Join-Path $root 'transfer';$baseArgs.TransferDirectory=$transfer
try{
 Check 'uri-allowed-https' {In-Module {Assert-L12BaiduUri 'https://c.pcs.baidu.com';Assert-L12BaiduUri 'https://d7.baidupcs.com/file?q=x'}|Out-Null}
 foreach($url in @('http://c.pcs.baidu.com','https://pcs.baidu.com.evil.test/','https://evilbaidupcs.com/','https://u:p@c.pcs.baidu.com/','https://c.pcs.baidu.com:444/','https://c.pcs.baidu.com/#x')){Check ('uri-reject-'+$url){Reject {In-Module {param($u) Assert-L12BaiduUri $u} $url} 'L12_BAIDU_'}}
 Check 'upload-origin-reject-path-query' {Reject {In-Module {Assert-L12BaiduUri 'https://c.pcs.baidu.com/file?a=b' -UploadOrigin}} 'L12_BAIDU_UPLOAD_ORIGIN'}
 Check 'local-source-C-rejected' {Reject {In-Module {Assert-L12BaiduLocal 'C:/temp/probe.age' -Private}} 'L12_BAIDU_LOCAL_PATH'}
 Check 'local-source-ADS-rejected' {Reject {In-Module {Assert-L12BaiduLocal 'E:/file:stream' -Private}} 'L12_BAIDU_LOCAL_PATH'}
 Check 'local-source-traversal-rejected' {Reject {In-Module {Assert-L12BaiduLocal 'E:/safe/../file' -Private}} 'L12_BAIDU_LOCAL_PATH'}
 Check 'credential-status-no-secrets' {$status=Get-L12BaiduCredentialStatus -CredentialPath $credential;$text=$status|ConvertTo-Json;Assert ($text -notmatch 'synthetic-access|synthetic-refresh|synthetic-secret');Assert ($status.appId -ceq '124583157')}
 Check 'plan-multi-part-and-immutable-lock' {
  In-Module {param($a) $plan=Get-L12BaiduGroupPlan $a.path $a.sha;try{if($plan.files[0].parts.Count -ne 3){throw 'part count'};try{$write=[IO.File]::Open($a.path,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite);$write.Dispose();throw 'manifest not locked'}catch [IO.IOException]{};try{$write=[IO.File]::Open($plan.files[0].stream.Name,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite);$write.Dispose();throw 'cipher not locked'}catch [IO.IOException]{}}finally{Close-L12BaiduPlan $plan}} @{path=$manifestPath;sha=$manifestSha}
 }
 Check 'manifest-wrong-sha-rejected' {Reject {In-Module {param($p) Get-L12BaiduGroupPlan $p ('f'*64)} $manifestPath} 'L12_BAIDU_MANIFEST_SHA'}
 Check 'plan-deadline-rejected' {Reject {In-Module {param($a) Get-L12BaiduGroupPlan $a.path $a.sha ([DateTime]::UtcNow.AddSeconds(-1))} @{path=$manifestPath;sha=$manifestSha}} 'L12_BAIDU_DEADLINE'}
 $echoArgs=$baseArgs.Clone();$echoArgs.TransferDirectory=Join-Path $root 'wrong-echo'
 Check 'wrong-precreate-path-echo-rejected' {$mock.badPathEcho=$true;try{Reject {Invoke-L12BaiduArchiveUpload @echoArgs} 'L12_BAIDU_PRECREATE_IDENTITY'}finally{$mock.badPathEcho=$false}}
 Check 'upload-interruption-preserves-first-part' {$mock.failPart=1;$mock.failPartTimes=2;Reject {Invoke-L12BaiduArchiveUpload @baseArgs} 'L12_BAIDU_TRANSPORT_UNCERTAIN';$state=In-Module {param($p) Read-L12BaiduProtected $p} (Join-Path $transfer 'transfer.dpapi');Assert ($state.files[0].doneParts.Count -eq 1);Assert ($state.files[0].doneParts[0] -eq 0)}
 Check 'upload-resume-skips-confirmed-part' {$before=@($mock.calls|Where-Object{$_.operation -eq 'upload' -and $_.part -eq 0}).Count;$result=Invoke-L12BaiduArchiveUpload @baseArgs;Assert ($result.fileCount -eq 4);$after=@($mock.calls|Where-Object{$_.operation -eq 'upload' -and $_.part -eq 0}).Count;Assert (($after-$before) -eq 3) 'first archive part was resent'}
 Check 'upload-completed-rerun-only-metadata' {$before=@($mock.calls|Where-Object{$_.operation -in 'upload','create','precreate'}).Count;Invoke-L12BaiduArchiveUpload @baseArgs|Out-Null;$after=@($mock.calls|Where-Object{$_.operation -in 'upload','create','precreate'}).Count;Assert ($before -eq $after)}
 Check 'state-bytes-not-plaintext-json' {$raw=[IO.File]::ReadAllBytes((Join-Path $transfer 'transfer.dpapi'));Assert ([Text.Encoding]::UTF8.GetString($raw) -notmatch 'synthetic-access|uploadid|manifestSha256')}
 Check 'download-interruption-saves-sub-checkpoint-prefix' {$mock.downloadShort=2MB;Reject {Invoke-L12BaiduArchiveDownload @baseArgs} 'L12_BAIDU_DOWNLOAD_INCOMPLETE';$state=In-Module {param($p) Read-L12BaiduProtected $p} (Join-Path $transfer 'transfer.dpapi');Assert ($state.files[0].download.bytes -eq 2MB) 'small progress lost'}
 Check 'download-range-ignored-rejected' {$mock.rangeMode='ignore';Reject {Invoke-L12BaiduArchiveDownload @baseArgs} 'L12_BAIDU_RANGE_STATUS'}
 Check 'download-range-wrong-offset-rejected' {$mock.rangeMode='wrong';Reject {Invoke-L12BaiduArchiveDownload @baseArgs} 'L12_BAIDU_RANGE_IDENTITY';$mock.rangeMode='normal'}
 Check 'download-range-resume-and-full-sha' {$result=Invoke-L12BaiduArchiveDownload @baseArgs;Assert $result.cloudProvenanceVerified;Assert (!$result.plaintextRecoveryVerified);foreach($file in $result.files){Assert ((Get-FileHash -LiteralPath (Join-Path $result.returnedDirectory $file.name) -Algorithm SHA256).Hash -ieq $file.sha256)}}
 Check 'download-completed-repeat-hashes-no-network' {$before=$mock.calls.Count;Invoke-L12BaiduArchiveDownload @baseArgs|Out-Null;Assert ($mock.calls.Count -eq $before)}
 Check 'exclusive-transfer-lease' {$lease=[IO.File]::Open((Join-Path $transfer 'transfer.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{Reject {Invoke-L12BaiduArchiveUpload @baseArgs}}finally{$lease.Dispose()}}
 $lost=Join-Path $root 'lost-create';$lostArgs=$baseArgs.Clone();$lostArgs.TransferDirectory=$lost
 Check 'create-response-lost-does-not-repeat-create' {$mock.files.Clear();$mock.loseCreate=$true;Reject {Invoke-L12BaiduArchiveUpload @lostArgs} 'L12_BAIDU_TRANSPORT_UNCERTAIN';$before=@($mock.calls|Where-Object{$_.operation -eq 'create'}).Count;Invoke-L12BaiduArchiveUpload @lostArgs|Out-Null;$after=@($mock.calls|Where-Object{$_.operation -eq 'create'}).Count;Assert (($after-$before) -eq 3)}
 $missing=Join-Path $root 'before-create';$missingArgs=$baseArgs.Clone();$missingArgs.TransferDirectory=$missing
 Check 'create-before-send-interruption-recovers-when-absent' {$mock.skipCreate=$true;$mock.files.Clear();Reject {Invoke-L12BaiduArchiveUpload @missingArgs} 'L12_BAIDU_TRANSPORT_UNCERTAIN';Invoke-L12BaiduArchiveUpload @missingArgs|Out-Null;Assert ($mock.files.Count -eq 4)}
 $untracked=Join-Path $missing 'returned';$first=$definitions[0].cipher.fileName
 Check 'untracked-returned-file-not-cloud-proof' {In-Module {param($p) New-L12BaiduPrivateDirectory $p} $untracked|Out-Null;[IO.File]::WriteAllBytes((Join-Path $untracked $first),$sourceBytes[$first]);Reject {Invoke-L12BaiduArchiveDownload @missingArgs} 'L12_BAIDU_UNTRACKED_RETURNED'}
 Check 'untracked-partial-not-cloud-proof' {$local=Join-Path $untracked $first;Remove-Item -LiteralPath $local;[IO.File]::WriteAllBytes(($local+'.partial'),[byte[]]@(1,2,3));Reject {Invoke-L12BaiduArchiveDownload @missingArgs} 'L12_BAIDU_UNTRACKED_PARTIAL'}
 $statePath=Join-Path $transfer 'transfer.dpapi'
 Check 'duplicate-state-parts-rejected' {$original=[IO.File]::ReadAllBytes($statePath);try{In-Module {param($p) $s=Read-L12BaiduProtected $p;$s.files[0].doneParts=@(0,0);Write-L12BaiduProtected $p $s} $statePath;Reject {Invoke-L12BaiduArchiveUpload @baseArgs} 'L12_BAIDU_STATE_PART'}finally{[IO.File]::WriteAllBytes($statePath,$original)}}
 Check 'changed-state-manifest-rejected' {$original=[IO.File]::ReadAllBytes($statePath);try{In-Module {param($p) $s=Read-L12BaiduProtected $p;$s.manifestSha256='0'*64;Write-L12BaiduProtected $p $s} $statePath;Reject {Invoke-L12BaiduArchiveUpload @baseArgs} 'L12_BAIDU_STATE_IDENTITY'}finally{[IO.File]::WriteAllBytes($statePath,$original)}}
 $partialFile=Join-Path $untracked ($first+'.partial');Remove-Item -LiteralPath $partialFile
 $mock.downloadShort=2MB;Reject {Invoke-L12BaiduArchiveDownload @missingArgs} 'L12_BAIDU_DOWNLOAD_INCOMPLETE'
 Check 'tampered-download-prefix-rejected' {$stream=[IO.File]::Open($partialFile,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{$original=$stream.ReadByte();$stream.Position=0;$stream.WriteByte($original -bxor 1)}finally{$stream.Dispose()};try{Reject {Invoke-L12BaiduArchiveDownload @missingArgs} 'L12_BAIDU_PARTIAL_CHANGED'}finally{$stream=[IO.File]::OpenWrite($partialFile);try{$stream.WriteByte($original)}finally{$stream.Dispose()}}}
 Check 'crashed-uncommitted-download-tail-is-trimmed' {$stream=[IO.File]::Open($partialFile,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$stream.Write([byte[]]::new(1024),0,1024)}finally{$stream.Dispose()};$result=Invoke-L12BaiduArchiveDownload @missingArgs;Assert $result.cloudProvenanceVerified;Assert ((Get-FileHash -LiteralPath (Join-Path $untracked $first) -Algorithm SHA256).Hash -ieq $definitions[0].cipher.sha256)}
 Check 'changed-source-cipher-rejected' {$cipherPath=Join-Path $source $first;$stream=[IO.File]::Open($cipherPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{$stream.Position=40;$original=$stream.ReadByte();$stream.Position=40;$stream.WriteByte($original -bxor 1)}finally{$stream.Dispose()};try{Reject {In-Module {param($a) Get-L12BaiduGroupPlan $a.path $a.sha} @{path=$manifestPath;sha=$manifestSha}} 'L12_BAIDU_SOURCE_SHA'}finally{$stream=[IO.File]::OpenWrite($cipherPath);try{$stream.Position=40;$stream.WriteByte($original)}finally{$stream.Dispose()}}}
 Check 'over-broad-credential-file-acl-rejected' {$originalAcl=Get-Acl -LiteralPath $credential;$wide=Get-Acl -LiteralPath $credential;$wide.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-1-0'),[Security.AccessControl.FileSystemRights]::Read,[Security.AccessControl.AccessControlType]::Allow));Set-Acl -LiteralPath $credential -AclObject $wide;try{Reject {Get-L12BaiduCredentialStatus -CredentialPath $credential} 'L12_BAIDU_PROTECTED_ACL'}finally{Set-Acl -LiteralPath $credential -AclObject $originalAcl}}
 $corrupt=Join-Path $root 'bad-return';$corruptArgs=$baseArgs.Clone();$corruptArgs.TransferDirectory=$corrupt;$mock.files.Clear()
 Check 'wrong-part-md5-is-not-checkpointed' {$mock.badMd5=$true;try{Reject {Invoke-L12BaiduArchiveUpload @corruptArgs} 'L12_BAIDU_PART_MD5'}finally{$mock.badMd5=$false};$state=In-Module {param($p) Read-L12BaiduProtected $p} (Join-Path $corrupt 'transfer.dpapi');Assert ($state.files[0].doneParts.Count -eq 0);Assert ($mock.files.Count -eq 0)}
 Check 'unconfirmed-part-resends-and-then-creates' {Invoke-L12BaiduArchiveUpload @corruptArgs|Out-Null;Assert ($mock.files.Count -eq 4)}
 Check 'wrong-cloud-content-fails-full-sha' {$record=@($mock.files.Values|Where-Object{$_.path.EndsWith('/'+$first)})[0];$record.bytes[200]=$record.bytes[200] -bxor 1;Reject {Invoke-L12BaiduArchiveDownload @corruptArgs} 'L12_BAIDU_RETURNED_SHA';Assert (!(Test-Path -LiteralPath (Join-Path $corrupt 'cloud-receipt.json')))}
 $locationArgs=$baseArgs.Clone();$locationArgs.TransferDirectory=Join-Path $root 'location-refresh'
 Check 'large-transfer-refreshes-expired-upload-location' {$mock.files.Clear();$mock.locateLifetime=1;$mock.uploadPauseOnceMs=1100;$before=@($mock.calls|Where-Object{$_.operation -eq 'locateupload'}).Count;try{Invoke-L12BaiduArchiveUpload @locationArgs|Out-Null}finally{$mock.locateLifetime=60;$mock.uploadPauseOnceMs=0};$after=@($mock.calls|Where-Object{$_.operation -eq 'locateupload'}).Count;Assert (($after-$before) -ge 5) 'first multi-part file reused an expired location'}
 Check 'multi-gib-chunk-length-remains-bounded' {In-Module {foreach($remaining in @([long]2147483647,[long]2147483648,[long]3201470767,[long]4294967296)){if((Get-L12BaiduChunkLength $remaining 4MB) -ne 4MB -or (Get-L12BaiduChunkLength $remaining 65536) -ne 65536){throw 'wrong multi-GiB chunk'}};if((Get-L12BaiduChunkLength 123 4MB) -ne 123 -or (Get-L12BaiduChunkLength 0 4MB) -ne 0){throw 'wrong final chunk'}};Reject {In-Module {Get-L12BaiduChunkLength -1 4MB}} 'L12_BAIDU_CHUNK_LENGTH'}
 Check 'already-expired-location-is-rejected' {$mock.locateLifetime=0;try{Reject {In-Module {Get-L12BaiduUploadLocation ([pscustomobject]@{credential=@{accessToken='synthetic-access'}}) '/apps/十二军团私有备份/test' 'synthetic-task'}} 'L12_BAIDU_UPLOAD_LOCATION_EXPIRED'}finally{$mock.locateLifetime=60}}
 $refresh=Join-Path $root 'refresh-oauth.dpapi';In-Module {param($a) Write-L12BaiduProtected $a.path $a.value} @{path=$refresh;value=$fake}
 Check 'refresh-single-use-response-lost-blocks-retry' {$mock.loseRefresh=$true;$before=$mock.refreshCount;Reject {Update-L12BaiduAuthorization -CredentialPath $refresh} 'L12_BAIDU_TRANSPORT_UNCERTAIN';Reject {Update-L12BaiduAuthorization -CredentialPath $refresh} 'L12_BAIDU_REFRESH_UNCERTAIN';Assert ($mock.refreshCount -eq $before+1);$mock.loseRefresh=$false}
 $staged=Join-Path $root 'staged-oauth.dpapi';In-Module {param($a) Write-L12BaiduProtected $a.path $a.value} @{path=$staged;value=$fake}
 Check 'refresh-pending-atomic-promotion-recovery' {$mock.failWrite='staged-oauth.dpapi';$mock.failWriteTimes=1;Reject {Update-L12BaiduAuthorization -CredentialPath $staged} 'L12_TEST_DURABLE_WRITE_FAILURE';Assert (Test-Path -LiteralPath ($staged+'.refresh-pending.dpapi'));$before=$mock.refreshCount;$result=Update-L12BaiduAuthorization -CredentialPath $staged;Assert ($result.status -eq 'refresh-recovered');Assert ($mock.refreshCount -eq $before);Assert (!(Test-Path -LiteralPath ($staged+'.refresh-attempt.dpapi')))}
 $cleanup=Join-Path $root 'cleanup-oauth.dpapi';In-Module {param($a) Write-L12BaiduProtected $a.path $a.value} @{path=$cleanup;value=$fake}
 Check 'refresh-cleanup-crash-keeps-recoverable-pending' {$mock.failDelete='cleanup-oauth.dpapi.refresh-pending.dpapi';$mock.failDeleteTimes=1;Reject {Update-L12BaiduAuthorization -CredentialPath $cleanup} 'L12_TEST_MARKER_CLEANUP_FAILURE';Assert (Test-Path -LiteralPath ($cleanup+'.refresh-pending.dpapi'));Assert (!(Test-Path -LiteralPath ($cleanup+'.refresh-attempt.dpapi')));$before=$mock.refreshCount;Update-L12BaiduAuthorization -CredentialPath $cleanup|Out-Null;Assert ($mock.refreshCount -eq $before);Assert (!(Test-Path -LiteralPath ($cleanup+'.refresh-pending.dpapi')))}
 Check 'credential-refresh-and-transfer-lock-shared' {$lease=[IO.File]::Open(($credential+'.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{Reject {Update-L12BaiduAuthorization -CredentialPath $credential}}finally{$lease.Dispose()}}
 # A disaster-recovery caller has only the independently trusted original manifest.
 # Keep the original 49 guards; no source cipher or original DPAPI state is copied here.
 $manifestOnly=Join-Path $root 'manifest-only';$manifestOnly=In-Module {param($p) New-L12BaiduPrivateDirectory $p} $manifestOnly
 $recoveryManifest=Join-Path $manifestOnly ([IO.Path]::GetFileName($manifestPath));[IO.File]::Copy($manifestPath,$recoveryManifest)
 foreach($cloudFile in $mock.files.Values){$cloudFile.bytes=[byte[]]$sourceBytes[[IO.Path]::GetFileName($cloudFile.path)].Clone()}
 $restoreArgs=$baseArgs.Clone();$restoreArgs.ManifestPath=$recoveryManifest;$restoreArgs.TransferDirectory=Join-Path $root 'fresh-restore'
 $cloudFirst=@($mock.files.Values|Where-Object{$_.path.EndsWith('/'+$first)})[0]
 Check 'restore-manifest-only-plan-does-not-require-cipher' {Assert (@(Get-ChildItem -LiteralPath $manifestOnly -Filter '*.age').Count -eq 0);Assert (!(Test-Path -LiteralPath (Join-Path $manifestOnly 'transfer.dpapi')));$trusted=In-Module {param($a) Read-L12BaiduManifest $a.path $a.sha} @{path=$recoveryManifest;sha=$manifestSha};try{Assert ($trusted.definitions.Count -eq 4)}finally{$trusted.stream.Dispose()};Reject {In-Module {param($a) Get-L12BaiduGroupPlan $a.path $a.sha} @{path=$recoveryManifest;sha=$manifestSha}} 'L12_BAIDU_PATH_MISSING'}
 Check 'restore-wrong-trusted-manifest-sha-rejected' {$bad=$restoreArgs.Clone();$bad.ExpectedManifestSha256='f'*64;$before=$mock.calls.Count;Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_MANIFEST_SHA';Assert ($mock.calls.Count -eq $before);Assert (!(Test-Path -LiteralPath $bad.TransferDirectory))}
 Check 'restore-invalid-manifest-name-rejected' {$badPath=Join-Path $manifestOnly ('f'*64+'.manifest.json');[IO.File]::Copy($recoveryManifest,$badPath);$bad=$restoreArgs.Clone();$bad.ManifestPath=$badPath;Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_MANIFEST_FORMAT'}
 Check 'restore-rejects-existing-foreign-directory-without-overwrite' {$bad=$restoreArgs.Clone();$bad.TransferDirectory=Join-Path $root 'foreign-restore';In-Module {param($p) New-L12BaiduPrivateDirectory $p} $bad.TransferDirectory|Out-Null;$marker=Join-Path $bad.TransferDirectory 'other-output.bin';[IO.File]::WriteAllBytes($marker,[byte[]]@(1,2,3));Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_RESTORE_DIRECTORY_NOT_EMPTY';Assert ([Convert]::ToHexString([IO.File]::ReadAllBytes($marker)) -ceq '010203')}
 Check 'restore-complete-without-source-state-or-md5-parts' {$blocks=$cloudFirst.block_list;$cloudFirst.PSObject.Properties.Remove('block_list');try{$result=Invoke-L12BaiduArchiveRestore @restoreArgs;Assert ($result.status -ceq 'cloud-cipher-restore-verified');Assert $result.cloudProvenanceVerified;Assert (!$result.plaintextRecoveryVerified -and !$result.oldTransferStateRequired -and !$result.sourceCipherRequired);Assert (!(Test-Path -LiteralPath (Join-Path $restoreArgs.TransferDirectory 'transfer.dpapi')));foreach($file in $result.files){Assert ((Get-FileHash -LiteralPath (Join-Path $result.returnedDirectory $file.name)).Hash -ieq $file.sha256)}}finally{$cloudFirst|Add-Member -NotePropertyName block_list -NotePropertyValue $blocks}}
 Check 'restore-completed-reentry-uses-own-state-and-rehashes' {$before=$mock.downloadStarts.Count;Invoke-L12BaiduArchiveRestore @restoreArgs|Out-Null;Assert ($mock.downloadStarts.Count -eq $before)}
 $resumeRestore=$restoreArgs.Clone();$resumeRestore.TransferDirectory=Join-Path $root 'resume-restore'
 Check 'restore-interruption-persists-new-machine-prefix' {$mock.downloadShort=2MB;Reject {Invoke-L12BaiduArchiveRestore @resumeRestore} 'L12_BAIDU_DOWNLOAD_INCOMPLETE';$state=In-Module {param($p) Read-L12BaiduProtected $p} (Join-Path $resumeRestore.TransferDirectory 'restore.dpapi');Assert ($state.files[0].download.bytes -eq 2MB);Assert ($state.files[0].fsId -ceq [string]$cloudFirst.fs_id);Assert (!(Test-Path -LiteralPath (Join-Path $resumeRestore.TransferDirectory 'transfer.dpapi')))}
 Check 'restore-metadata-wrong-path-rejected' {$saved=$cloudFirst.path;$cloudFirst.path='/apps/wrong/'+$first;try{Reject {Invoke-L12BaiduArchiveRestore @resumeRestore} 'L12_BAIDU_META_IDENTITY'}finally{$cloudFirst.path=$saved}}
 Check 'restore-metadata-wrong-size-rejected' {$saved=$cloudFirst.size;$cloudFirst.size++;try{Reject {Invoke-L12BaiduArchiveRestore @resumeRestore} 'L12_BAIDU_META_MISMATCH'}finally{$cloudFirst.size=$saved}}
 Check 'restore-metadata-missing-fsid-rejected' {$saved=$cloudFirst.fs_id;$cloudFirst.fs_id=0;try{Reject {Invoke-L12BaiduArchiveRestore @resumeRestore} 'L12_BAIDU_META_IDENTITY'}finally{$cloudFirst.fs_id=$saved}}
 Check 'restore-metadata-changed-fsid-rejected' {$saved=$cloudFirst.fs_id;$cloudFirst.fs_id++;try{Reject {Invoke-L12BaiduArchiveRestore @resumeRestore} 'L12_BAIDU_REMOTE_REPLACED'}finally{$cloudFirst.fs_id=$saved}}
 Check 'restore-range-resume-uses-persisted-prefix' {$before=$mock.downloadStarts.Count;$result=Invoke-L12BaiduArchiveRestore @resumeRestore;Assert ($mock.downloadStarts[$before] -eq 2MB);Assert $result.cloudProvenanceVerified}
 Check 'restore-untracked-returned-file-is-not-cloud-proof' {$bad=$restoreArgs.Clone();$bad.TransferDirectory=Join-Path $root 'restore-untracked-file';$mock.downloadShort=2MB;Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_DOWNLOAD_INCOMPLETE';$other=$definitions[1].cipher.fileName;[IO.File]::WriteAllBytes((Join-Path $bad.TransferDirectory ('returned/'+$other)),$sourceBytes[$other]);Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_UNTRACKED_RETURNED';Assert (!(Test-Path -LiteralPath (Join-Path $bad.TransferDirectory 'restore-receipt.json')))}
 Check 'restore-untracked-partial-file-is-not-cloud-proof' {$bad=$restoreArgs.Clone();$bad.TransferDirectory=Join-Path $root 'restore-untracked-partial';$mock.downloadShort=2MB;Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_DOWNLOAD_INCOMPLETE';$other=$definitions[1].cipher.fileName;[IO.File]::WriteAllBytes((Join-Path $bad.TransferDirectory ('returned/'+$other+'.partial')),[byte[]]@(1,2,3));Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_UNTRACKED_PARTIAL'}
 Check 'restore-cloud-content-full-sha-required' {$bad=$restoreArgs.Clone();$bad.TransferDirectory=Join-Path $root 'restore-corrupt';$cloudFirst.bytes[200]=$cloudFirst.bytes[200] -bxor 1;try{Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_RETURNED_SHA';Assert (!(Test-Path -LiteralPath (Join-Path $bad.TransferDirectory 'restore-receipt.json')))}finally{$cloudFirst.bytes[200]=$cloudFirst.bytes[200] -bxor 1}}
 Check 'restore-wrong-state-remote-root-rejected' {$stateFile=Join-Path $resumeRestore.TransferDirectory 'restore.dpapi';$saved=[IO.File]::ReadAllBytes($stateFile);try{In-Module {param($p) $state=Read-L12BaiduProtected $p;$state.remoteRoot='/apps/wrong';Write-L12BaiduProtected $p $state} $stateFile;Reject {Invoke-L12BaiduArchiveRestore @resumeRestore} 'L12_BAIDU_RESTORE_STATE_IDENTITY'}finally{[IO.File]::WriteAllBytes($stateFile,$saved)}}
 Check 'restore-exclusive-local-lease' {$lease=[IO.File]::Open((Join-Path $resumeRestore.TransferDirectory 'restore.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{Reject {Invoke-L12BaiduArchiveRestore @resumeRestore}}finally{$lease.Dispose()}}
 Check 'invalid-utf8-json-charset-is-handled' {$mock.wire=$true;$mock.wireBytes=[Text.Encoding]::UTF8.GetBytes('{"errno":0,"ok":true}');$result=In-Module {$context=[pscustomobject]@{http=$null;deadline=[DateTime]::UtcNow.AddSeconds(30);credential=@{accessToken='synthetic-access'}};Invoke-L12BaiduJson $context 'GET' 'https://pan.baidu.com/rest/test'};Assert $result.ok}
 Check 'json-body-bounded' {$mock.wireBytes=[byte[]]::new(131073);Reject {In-Module {Invoke-L12BaiduJson ([pscustomobject]@{}) 'GET' 'https://pan.baidu.com/rest/test'}} 'L12_BAIDU_RESPONSE_LIMIT'}
 Check 'metadata-redirect-not-followed' {$mock.wireStatus=302;$mock.wireRedirect='https://evil.test/token';Reject {In-Module {Invoke-L12BaiduJson ([pscustomobject]@{}) 'GET' 'https://pan.baidu.com/rest/test'}} 'L12_BAIDU_HTTP_302'}
 Check 'download-cross-host-redirect-rejected' {Reject {In-Module {Receive-L12BaiduFile ([pscustomobject]@{credential=@{accessToken='synthetic-access'}}) 'https://d.pcs.baidu.com/file?a=b' (Join-Path $args[0] 'redirect.partial') 1024 $null} $root} 'L12_BAIDU_UNTRUSTED_HOST'}
 Check 'dlink-with-token-rejected' {Reject {In-Module {param($r) Receive-L12BaiduFile ([pscustomobject]@{credential=@{accessToken='synthetic-access'}}) 'https://d.pcs.baidu.com/file?access_token=other' (Join-Path $r 'token.partial') 1024 $null} $root} 'L12_BAIDU_DLINK_TOKEN'}
 Check 'original-sources-unchanged' {foreach($pair in $sourceBytes.GetEnumerator()){Assert ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes((Join-Path $source $pair.Key)))).ToLowerInvariant() -ceq (In-Module {param($b) Get-L12BaiduSha $b} $pair.Value))}}
 # All original 69 checks above remain independent and unchanged.
 $volumeStart=$checks.Count;Assert ($volumeStart -eq 69)
 $mock.wire=$false;$mock.wireStatus=200;$mock.wireRedirect=$null;$mock.downloadShort=0;$mock.rangeMode='normal'
 $volumeDirectory=Join-Path $root 'volume-source'
 Check 'volumes-actual-streaming-preparation' {
  $script:preparedVolumes=New-L12BaiduTransportVolumes -ManifestPath $manifestPath -ExpectedManifestSha256 $manifestSha -TransferDirectory $volumeDirectory -VolumeBytes 1MB
  Assert ($preparedVolumes.volumeReferences -eq 11);Assert (!$preparedVolumes.sourceDeleted -and !$preparedVolumes.uploadPerformed)
  $script:volumeEnvelope=Get-Content -LiteralPath $preparedVolumes.manifestPath -Raw|ConvertFrom-Json -Depth 20
  Assert ($volumeEnvelope.originalManifest.sha256 -ceq $manifestSha)
  Assert (@(Get-ChildItem -LiteralPath $volumeDirectory -Filter '*.volume').Count -eq 11)
 }
 $volumeArgs=@{ManifestPath=$preparedVolumes.manifestPath;ExpectedManifestSha256=$preparedVolumes.manifestSha256;ExpectedOriginalManifestSha256=$manifestSha;CredentialPath=$credential;TimeBudgetSeconds=120;TransferDirectory=(Join-Path $root 'volume-transfer')}
 function Read-VolumeTest($Arguments){In-Module {param($a) $p=Read-L12BaiduManifest $a.ManifestPath $a.ExpectedManifestSha256 $a.ExpectedOriginalManifestSha256;try{return $p.definitions.Count}finally{$p.stream.Dispose()}} $Arguments}
 function New-VolumeVariant([scriptblock]$Change){
  $copy=Get-Content -LiteralPath $preparedVolumes.manifestPath -Raw|ConvertFrom-Json -Depth 20
  & $Change $copy
  $bytes=[Text.Encoding]::UTF8.GetBytes(($copy|ConvertTo-Json -Depth 20 -Compress));$hash=In-Module {param($b) Get-L12BaiduSha $b} $bytes
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root ('variant-'+[Guid]::NewGuid().ToString('N')))
  $path=Join-Path $directory ($hash+'.volumes.json');[IO.File]::WriteAllBytes($path,$bytes)
  return @{ManifestPath=$path;ExpectedManifestSha256=$hash;ExpectedOriginalManifestSha256=$manifestSha}
 }
 Check 'volumes-require-both-independent-trusted-shas' {$bad=$volumeArgs.Clone();$bad.ExpectedOriginalManifestSha256='';Reject {Read-VolumeTest $bad} 'L12_BAIDU_VOLUME_TRUST';$bad.ExpectedOriginalManifestSha256='f'*64;Reject {Read-VolumeTest $bad} 'L12_BAIDU_VOLUME_TRUST';$bad=$volumeArgs.Clone();$bad.ExpectedManifestSha256='f'*64;Reject {Read-VolumeTest $bad}}
 Check 'volumes-original-v2-four-gib-guard-unchanged' {
  $copy=$manifest|ConvertTo-Json -Depth 16|ConvertFrom-Json;$copy.items[0].cipher.bytes=[long]4GB+1
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'large-v2-metadata')
  $path=Join-Path $directory ($archiveSha+'.manifest.json');[IO.File]::WriteAllText($path,($copy|ConvertTo-Json -Depth 16 -Compress),[Text.UTF8Encoding]::new($false));$hash=(Get-FileHash $path).Hash.ToLowerInvariant()
  Reject {In-Module {param($a) Read-L12BaiduManifest $a.path $a.sha} @{path=$path;sha=$hash}} 'L12_BAIDU_CIPHER_DEFINITION'
 }
 Check 'volumes-int64-above-four-gib-metadata-is-explicit-not-a-large-file-run' {
  $copy=Get-Content $preparedVolumes.manifestPath -Raw|ConvertFrom-Json -Depth 20
  $original=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($copy.originalManifest.contentBase64))|ConvertFrom-Json -Depth 20
  $original.items[0].cipher.bytes=[long]4GB+1;$raw=[Text.Encoding]::UTF8.GetBytes(($original|ConvertTo-Json -Depth 20 -Compress))
  $hash=In-Module {param($b) Get-L12BaiduSha $b} $raw;$copy.originalManifest.sha256=$hash;$copy.originalManifest.bytes=$raw.Length;$copy.originalManifest.contentBase64=[Convert]::ToBase64String($raw)
  $copy.volumeBytes=1GB;$copy.ciphers[0].bytes=[long]4GB+1;$copy.ciphers[0].volumes=@()
  for($i=0;$i -lt 5;$i++){$digest=('8'*63)+$i;$copy.ciphers[0].volumes+=@{index=$i;offset=[long]$i*1GB;bytes=$(if($i -eq 4){1}else{1GB});sha256=$digest;name=($digest+'.volume')}}
  $bytes=[Text.Encoding]::UTF8.GetBytes(($copy|ConvertTo-Json -Depth 20 -Compress));$envelopeHash=In-Module {param($b) Get-L12BaiduSha $b} $bytes
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'large-envelope-metadata')
  $path=Join-Path $directory ($envelopeHash+'.volumes.json');[IO.File]::WriteAllBytes($path,$bytes)
  Assert ((Read-VolumeTest @{ManifestPath=$path;ExpectedManifestSha256=$envelopeHash;ExpectedOriginalManifestSha256=$hash}) -eq 9)
 }
 Check 'volumes-original-source-and-plan-fully-bound' {In-Module {param($a) $p=Get-L12BaiduGroupPlan $a.ManifestPath $a.ExpectedManifestSha256 ([DateTime]::MaxValue) $a.ExpectedOriginalManifestSha256;try{if($p.files.Count -ne 13 -or @($p.files|Where-Object{$_.bytes -gt 1GB}).Count -or $p.remoteRoot -cnotlike '/apps/十二军团私有备份/l12-volumes-v1-*'){throw 'volume plan not bounded'}}finally{Close-L12BaiduPlan $p}} $volumeArgs}
 Check 'volumes-reference-count-and-group-capacity-are-bounded-before-output' {
  $directory=Join-Path $root 'too-many-volumes'
  Reject {New-L12BaiduTransportVolumes -ManifestPath $manifestPath -ExpectedManifestSha256 $manifestSha -TransferDirectory $directory -VolumeBytes 64KB} 'L12_BAIDU_VOLUME_LIMIT';Assert (!(Test-Path $directory))
  $copy=$manifest|ConvertTo-Json -Depth 16|ConvertFrom-Json;$copy.items[0].cipher.bytes=[long]64GB
  Reject {In-Module {param($a) Get-L12BaiduVolumeOriginalModel $a.value $a.name} @{value=$copy;name=($archiveSha+'.manifest.json')}} 'L12_BAIDU_VOLUME_LIMIT'
 }
 Check 'volumes-preparation-never-overwrites-a-completed-or-unknown-directory' {
  Reject {New-L12BaiduTransportVolumes -ManifestPath $manifestPath -ExpectedManifestSha256 $manifestSha -TransferDirectory $volumeDirectory -VolumeBytes 1MB} 'L12_BAIDU_VOLUME_DIRECTORY_NOT_EMPTY'
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'prepare-interrupted');$partial=Join-Path $directory 'volume.partial-unknown';[IO.File]::WriteAllText($partial,'keep')
  Reject {New-L12BaiduTransportVolumes -ManifestPath $manifestPath -ExpectedManifestSha256 $manifestSha -TransferDirectory $directory -VolumeBytes 1MB} 'L12_BAIDU_VOLUME_DIRECTORY_NOT_EMPTY';Assert ([IO.File]::ReadAllText($partial) -ceq 'keep')
 }
 foreach($damage in @('reordered','duplicate-index','missing','overlap','truncated-length','extra-field','boolean-schema','string-length','scalar-volumes','boolean-index')){
  Check ('volumes-reject-'+$damage) {
   $variant=New-VolumeVariant {
    param($value)
    switch($damage){
     'reordered'{$first=$value.ciphers[0].volumes[0];$value.ciphers[0].volumes[0]=$value.ciphers[0].volumes[1];$value.ciphers[0].volumes[1]=$first}
     'duplicate-index'{$value.ciphers[0].volumes[1].index=0}
     'missing'{$value.ciphers[0].volumes=@($value.ciphers[0].volumes|Select-Object -SkipLast 1)}
     'overlap'{$value.ciphers[0].volumes[1].offset=0}
     'truncated-length'{$value.ciphers[0].volumes[0].bytes--}
     'extra-field'{$value|Add-Member -NotePropertyName skipHash -NotePropertyValue $true}
     'boolean-schema'{$value.schema=$true}
     'string-length'{$value.ciphers[0].bytes=[string]$value.ciphers[0].bytes}
     'scalar-volumes'{$value.ciphers[1].volumes=$value.ciphers[1].volumes[0]}
     'boolean-index'{$value.ciphers[0].volumes[0].index=$false}
    }
   }
   Reject {Read-VolumeTest $variant} 'L12_BAIDU_VOLUME_'
  }
 }
 Check 'volumes-reject-duplicate-json-key' {
  $text=Get-Content $preparedVolumes.manifestPath -Raw;$text=$text.Replace('"schema":1,','"schema":1,"schema":1,');$bytes=[Text.Encoding]::UTF8.GetBytes($text);$hash=In-Module {param($b) Get-L12BaiduSha $b} $bytes
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'duplicate-json');$path=Join-Path $directory ($hash+'.volumes.json');[IO.File]::WriteAllBytes($path,$bytes)
  Reject {Read-VolumeTest @{ManifestPath=$path;ExpectedManifestSha256=$hash;ExpectedOriginalManifestSha256=$manifestSha}} 'L12_BAIDU_VOLUME_FORMAT'
 }
 Check 'volumes-full-original-cipher-sha-required' {
  $cipher=$volumeEnvelope.ciphers[0]|ConvertTo-Json -Depth 12|ConvertFrom-Json;$cipher.sha256='f'*64
  Reject {In-Module {param($a) Copy-L12BaiduVolumeCipher $a.cipher $a.directory $null ([DateTime]::MaxValue)} @{cipher=$cipher;directory=$volumeDirectory}} 'L12_BAIDU_VOLUME_CIPHER_SHA'
 }
 $mock.files.Clear();$mock.tasks.Clear();$mock.pieces.Clear()
 Check 'volumes-real-original-upload-path' {$result=Invoke-L12BaiduArchiveUpload @volumeArgs;Assert ($result.fileCount -eq 13);Assert ($result.remoteRoot -clike '/apps/十二军团私有备份/l12-volumes-v1-*')}
 Check 'volumes-real-original-download-reassembles-exact-ciphertext' {
  $script:volumeDownload=Invoke-L12BaiduArchiveDownload @volumeArgs
  Assert ($volumeDownload.status -ceq 'cloud-volume-cipher-roundtrip-verified' -and !$volumeDownload.plaintextRecoveryVerified)
  foreach($item in $definitions){Assert ((Get-FileHash (Join-Path $volumeDownload.returnedDirectory $item.cipher.fileName)).Hash.ToLowerInvariant() -ceq $item.cipher.sha256)}
  Assert ((Get-FileHash $volumeDownload.originalManifestPath).Hash.ToLowerInvariant() -ceq $manifestSha)
  Assert (!(Split-Path -Parent $volumeDownload.originalManifestPath).StartsWith($volumeDownload.returnedDirectory,[StringComparison]::OrdinalIgnoreCase))
 }
 $volumeRecovery=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'only-volume-envelope')
 $envelopeOnly=Join-Path $volumeRecovery ([IO.Path]::GetFileName($preparedVolumes.manifestPath));[IO.File]::Copy($preparedVolumes.manifestPath,$envelopeOnly)
 $volumeRestore=$volumeArgs.Clone();$volumeRestore.ManifestPath=$envelopeOnly;$volumeRestore.TransferDirectory=Join-Path $root 'volume-fresh-restore'
 Check 'volumes-independent-envelope-only-restore' {
  Assert (@(Get-ChildItem $volumeRecovery -Force).Count -eq 1)
  $script:volumeRestored=Invoke-L12BaiduArchiveRestore @volumeRestore
  Assert ($volumeRestored.status -ceq 'cloud-volume-cipher-restore-verified' -and $volumeRestored.cloudProvenanceVerified -and !$volumeRestored.plaintextRecoveryVerified)
  Assert ($volumeRestored.originalManifestSha256 -ceq $manifestSha -and $volumeRestored.transportEnvelopeSha256 -ceq $preparedVolumes.manifestSha256)
  foreach($item in $definitions){Assert ((Get-FileHash (Join-Path $volumeRestored.returnedDirectory $item.cipher.fileName)).Hash.ToLowerInvariant() -ceq $item.cipher.sha256)}
 }
 Check 'volumes-restore-repeat-preserves-exact-completed-outputs' {$receipt=Join-Path $volumeRestore.TransferDirectory 'restore-receipt.json';$before=(Get-FileHash $receipt).Hash;$again=Invoke-L12BaiduArchiveRestore @volumeRestore;Assert ($again.originalManifestSha256 -ceq $manifestSha);Assert ((Get-FileHash $receipt).Hash -ceq $before)}
 Check 'volumes-interrupted-remote-prefix-requires-real-range-resume' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-range-resume';$mock.downloadShort=123
  Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_DOWNLOAD_INCOMPLETE';$before=$mock.downloadStarts.Count
  $result=Invoke-L12BaiduArchiveRestore @bad;Assert (@($mock.downloadStarts|Select-Object -Skip $before|Where-Object{$_ -eq 123}).Count -eq 1);Assert $result.cloudProvenanceVerified
 }
 Check 'volumes-damaged-cloud-volume-rejected-without-success-receipt' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-corrupt-cloud';$cloud=$mock.files[$volumeRestored.remoteRoot+'/'+$volumeEnvelope.ciphers[0].volumes[0].name];$cloud.bytes[50]=$cloud.bytes[50] -bxor 1
  try{Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_RETURNED_SHA';Assert (!(Test-Path (Join-Path $bad.TransferDirectory 'restore-receipt.json')))}finally{$cloud.bytes[50]=$cloud.bytes[50] -bxor 1}
 }
 Check 'volumes-missing-cloud-volume-rejected' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-missing-cloud';$path=$volumeRestored.remoteRoot+'/'+$volumeEnvelope.ciphers[0].volumes[0].name;$saved=$mock.files[$path];$mock.files.Remove($path)
  try{Reject {Invoke-L12BaiduArchiveRestore @bad};Assert (!(Test-Path (Join-Path $bad.TransferDirectory 'restore-receipt.json')))}finally{$mock.files[$path]=$saved}
 }
 Check 'volumes-truncated-cloud-volume-rejected' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-truncated-cloud';$cloud=$mock.files[$volumeRestored.remoteRoot+'/'+$volumeEnvelope.ciphers[0].volumes[0].name];$saved=$cloud.bytes;$cloud.bytes=[byte[]]$saved[0..($saved.Length-2)]
  try{Reject {Invoke-L12BaiduArchiveRestore @bad};Assert (!(Test-Path (Join-Path $bad.TransferDirectory 'restore-receipt.json')))}finally{$cloud.bytes=$saved}
 }
 In-Module {
  $script:OriginalVolumeCopy=(Get-Command Copy-L12BaiduVolumeCipher).ScriptBlock;$script:VolumeInterrupt=$false
  $script:VolumeWrite=(Get-Command Write-L12BaiduProtected).ScriptBlock;$script:VolumeAckInterrupt=$false
  function script:Copy-L12BaiduVolumeCipher($Cipher,[string]$Directory,$Output,[DateTime]$Deadline){if($script:VolumeInterrupt -and $Output){$script:VolumeInterrupt=$false;$Output.Write([byte[]]@(1,2,3),0,3);throw 'L12_TEST_VOLUME_INTERRUPTED'};& $script:OriginalVolumeCopy $Cipher $Directory $Output $Deadline}
  function script:Write-L12BaiduProtected([string]$Path,$Value){if($script:VolumeAckInterrupt -and $Path.EndsWith('reassembly.dpapi') -and $Value.files[0].phase -ceq 'complete'){$script:VolumeAckInterrupt=$false;throw 'L12_TEST_VOLUME_ACK_INTERRUPTED'};& $script:VolumeWrite $Path $Value}
 }
 Check 'volumes-owned-partial-reassembly-resumes-without-overwriting-unknown' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-reassembly-resume';In-Module {$script:VolumeInterrupt=$true}
  Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_TEST_VOLUME_INTERRUPTED';Assert (!(Test-Path (Join-Path $bad.TransferDirectory 'restore-receipt.json')))
  Assert (@(Get-ChildItem (Join-Path $bad.TransferDirectory 'returned') -Filter '*.assembling-*').Count -eq 1)
  $result=Invoke-L12BaiduArchiveRestore @bad;Assert $result.cloudProvenanceVerified
 }
 Check 'volumes-move-before-state-ack-reconciles-owned-output' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-ack-resume';In-Module {$script:VolumeAckInterrupt=$true}
  Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_TEST_VOLUME_ACK_INTERRUPTED'
  Assert (Test-Path (Join-Path $bad.TransferDirectory ('returned/'+$definitions[0].cipher.fileName)))
  $result=Invoke-L12BaiduArchiveRestore @bad;Assert $result.cloudProvenanceVerified
 }
 Check 'volumes-untracked-full-cipher-is-not-adopted' {
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'volume-untracked-reassembly');$returned=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $directory 'returned')
  [IO.File]::WriteAllBytes((Join-Path $returned $definitions[0].cipher.fileName),$sourceBytes[$definitions[0].cipher.fileName])
  Reject {In-Module {param($a) $p=Read-L12BaiduManifest $a.path $a.sha $a.original;try{Complete-L12BaiduVolumeReturn $p $a.returned $a.directory ([DateTime]::MaxValue)}finally{$p.stream.Dispose()}} @{path=$envelopeOnly;sha=$preparedVolumes.manifestSha256;original=$manifestSha;returned=$returned;directory=$directory}} 'L12_BAIDU_UNTRACKED_RETURNED'
 }
 Check 'volumes-untracked-receipt-is-never-overwritten' {
  $bad=$volumeRestore.Clone();$bad.TransferDirectory=Join-Path $root 'volume-untracked-receipt';$mock.downloadShort=123
  Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_DOWNLOAD_INCOMPLETE'
  $receipt=Join-Path $bad.TransferDirectory 'restore-receipt.json';[IO.File]::WriteAllText($receipt,'unknown receipt')
  Reject {Invoke-L12BaiduArchiveRestore @bad} 'L12_BAIDU_VOLUME_RECEIPT';Assert ([IO.File]::ReadAllText($receipt) -ceq 'unknown receipt')
 }
 Check 'volumes-duplicate-content-retains-each-reference-occurrence' {
  $first=[byte[]]::new(65536);$random.NextBytes($first);[Text.Encoding]::ASCII.GetBytes("age-encryption.org/v1`n").CopyTo($first,0)
  $directory=In-Module {param($p) New-L12BaiduPrivateDirectory $p} (Join-Path $root 'volume-identical-content')
  $digest=In-Module {param($b) Get-L12BaiduSha $b} $first;[IO.File]::WriteAllBytes((Join-Path $directory ($digest+'.volume')),$first)
  $all=[byte[]]::new(131072);$first.CopyTo($all,0);$first.CopyTo($all,65536);$whole=In-Module {param($b) Get-L12BaiduSha $b} $all
  $cipher=[pscustomobject]@{bytes=131072;sha256=$whole;volumes=@(@{name=($digest+'.volume');sha256=$digest;bytes=65536;index=0;offset=0},@{name=($digest+'.volume');sha256=$digest;bytes=65536;index=1;offset=65536})}
  In-Module {param($a) Copy-L12BaiduVolumeCipher $a.cipher $a.directory $null ([DateTime]::MaxValue)} @{cipher=$cipher;directory=$directory}
 }
 $volumeCheckCount=$checks.Count-$volumeStart;Assert ($volumeCheckCount -eq 32) 'volume check count changed'
}catch{
 $failure=$_.Exception.Message
}finally{
 $result=[ordered]@{schema=1;syntheticOnly=$true;networkRequests=0;realCredentialAccess=$false;fixtureRoot=$root;checks=@($checks.ToArray());passed=@($checks|Where-Object{$_.passed}).Count;failed=@($checks|Where-Object{!$_.passed}).Count;moduleSha256=(Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash;completedUtc=[DateTime]::UtcNow.ToString('o')}
 if(Get-Variable failure -ErrorAction SilentlyContinue){$result.failure=$failure}
 $result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $OutputRoot 'result.json') -Encoding utf8
 [pscustomobject]$result|Select-Object passed,failed,fixtureRoot|ConvertTo-Json -Compress
}
if($result.failed -gt 0 -or $result.Contains('failure') -or $result.passed -ne 101){throw ('L12_BAIDU_TEST_FAILURE: '+$(if($result.Contains('failure')){$result.failure}else{'incomplete check count'}))}
$physical=Assert-L12GeneratedPath -Path $root
if($physical -cnotmatch '^E:\\Legion12-ColdStorage\\l12-baidu-offline-tests-[0-9a-f]{32}$'){throw 'L12_BAIDU_FIXTURE_CLEANUP_SCOPE'}
$children=@(Get-ChildItem -LiteralPath $physical -Recurse -Force)
if(@($children|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or !$_.FullName.StartsWith($physical+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){throw 'L12_BAIDU_FIXTURE_CLEANUP_LINK'}
$removedBytes=($children|Where-Object{!$_.PSIsContainer}|Measure-Object Length -Sum).Sum
Remove-Item -LiteralPath $physical -Recurse -Force
$result.fixtureRemoved=$true;$result.regenerableLogicalBytesRemoved=$removedBytes
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $OutputRoot 'result.json') -Encoding utf8
