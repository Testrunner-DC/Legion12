# Official API transport for an existing L12CloudArchive encrypted group.
# No source retirement, key generation, automatic scheduling or implicit token retry.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$script:L12BaiduEntropy=[Text.Encoding]::UTF8.GetBytes('L12-Baidu-124583157-v1')
$script:L12BaiduPartBytes=4MB

function Assert-L12BaiduLocal([string]$Path,[switch]$Exists,[switch]$Private){
 if($Path -notmatch '^[DE]:[\\/]' -or $Path.Substring(2).Contains(':') -or $Path -match '(^|[\\/])\.{1,2}([\\/]|$)|[*?"<>|]' -or $Path -match '(?i)(^|[\\/])(BaiduSyncdisk|BaiduNetdisk|OneDrive[^\\/]*|Dropbox)([\\/]|$)'){throw 'L12_BAIDU_LOCAL_PATH'}
 $full=[IO.Path]::GetFullPath($Path);$cursor=[IO.Path]::GetPathRoot($full)
 foreach($part in $full.Substring($cursor.Length).Split('\',[StringSplitOptions]::RemoveEmptyEntries)){
  $cursor=Join-Path $cursor $part
  if(Test-Path -LiteralPath $cursor){$item=Get-Item -LiteralPath $cursor -Force;if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'L12_BAIDU_LINK'}}
 }
 $physical=Assert-L12GeneratedPath -Path $full -Purpose 'private Baidu transport'
 if($Private -and [IO.Path]::GetPathRoot($physical) -ine 'E:\'){throw 'L12_BAIDU_PRIVATE_MUST_BE_E'}
 if($Exists -and !(Test-Path -LiteralPath $full)){throw 'L12_BAIDU_PATH_MISSING'}
 return $full
}
function New-L12BaiduPrivateDirectory([string]$Path){
 $full=Assert-L12BaiduLocal $Path -Private
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
 $allowed=@($sid.Value,'S-1-5-18','S-1-5-32-544')
 if(!(Test-Path -LiteralPath $full)){
  if(!(Test-Path -LiteralPath (Split-Path -Parent $full))){throw 'L12_BAIDU_PARENT_MISSING'}
  [IO.Directory]::CreateDirectory($full)|Out-Null
  $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
  foreach($identity in $allowed){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($identity),[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))}
  Set-Acl -LiteralPath $full -AclObject $acl
 }
 $acl=Get-Acl -LiteralPath $full
 if(!$acl.AreAccessRulesProtected -or @($acl.Access|Where-Object{$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed}).Count){throw 'L12_BAIDU_PRIVATE_ACL'}
 return $full
}
function Get-L12BaiduSha([byte[]]$Bytes){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()}
function Read-L12BaiduProtected([string]$Path){
 $null=Assert-L12BaiduLocal $Path -Exists -Private
 if((Get-Item -LiteralPath $Path).Length -gt 4MB){throw 'L12_BAIDU_PROTECTED_LIMIT'}
 $allowed=@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')
 if(@((Get-Acl -LiteralPath $Path).Access|Where-Object{$_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed}).Count){throw 'L12_BAIDU_PROTECTED_ACL'}
 [byte[]]$plain=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path),$script:L12BaiduEntropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
 try{try{[Text.UTF8Encoding]::new($false,$true).GetString($plain)|ConvertFrom-Json}catch{throw 'L12_BAIDU_PROTECTED_FORMAT'}}finally{[Array]::Clear($plain,0,$plain.Length)}
}
function Write-L12BaiduProtected([string]$Path,$Value){
 $null=Assert-L12BaiduLocal $Path -Private
 [byte[]]$plain=[Text.Encoding]::UTF8.GetBytes(($Value|ConvertTo-Json -Depth 32 -Compress));$temp=$Path+'.tmp-'+[Guid]::NewGuid().ToString('N')
 try{
  [byte[]]$cipher=[Security.Cryptography.ProtectedData]::Protect($plain,$script:L12BaiduEntropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  $stream=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($cipher,0,$cipher.Length);$stream.Flush($true)}finally{$stream.Dispose()}
  if(Test-Path -LiteralPath $Path){[IO.File]::Replace($temp,$Path,[NullString]::Value)}else{[IO.File]::Move($temp,$Path)}
 }finally{[Array]::Clear($plain,0,$plain.Length)}
}
function Assert-L12BaiduUri([string]$Url,[switch]$UploadOrigin,[switch]$OAuth){
 $uri=$null
 if(![Uri]::TryCreate($Url,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -cne 'https' -or !$uri.IsDefaultPort -or $uri.UserInfo -or $uri.Fragment){throw 'L12_BAIDU_HTTPS_REQUIRED'}
 $hostName=$uri.DnsSafeHost.ToLowerInvariant()
 $trusted=($hostName -ceq 'pan.baidu.com' -or $hostName -ceq 'd.pcs.baidu.com' -or $hostName.EndsWith('.pcs.baidu.com') -or $hostName.EndsWith('.baidupcs.com'))
 if($OAuth){$trusted=$hostName -ceq 'openapi.baidu.com'}
 if(!$trusted){throw 'L12_BAIDU_UNTRUSTED_HOST'}
 if($UploadOrigin -and ($uri.AbsolutePath -cne '/' -or $uri.Query)){throw 'L12_BAIDU_UPLOAD_ORIGIN'}
 return $uri
}
function New-L12BaiduUrl([string]$Base,[hashtable]$Query){$Base+'?'+(($Query.GetEnumerator()|ForEach-Object{[Uri]::EscapeDataString($_.Key)+'='+[Uri]::EscapeDataString([string]$_.Value)}) -join '&')}
function New-L12BaiduForm([hashtable]$Fields){$map=[Collections.Generic.Dictionary[string,string]]::new();foreach($pair in $Fields.GetEnumerator()){$map[$pair.Key]=[string]$pair.Value};[Net.Http.FormUrlEncodedContent]::new($map)}
function Get-L12BaiduRemaining($Context){$seconds=[Math]::Ceiling(($Context.deadline-[DateTime]::UtcNow).TotalSeconds);if($seconds -le 0){throw 'L12_BAIDU_DEADLINE'};[int][Math]::Min($Context.requestTimeoutSeconds,$seconds)}
function Invoke-L12BaiduHttp($Context,[string]$Method,[string]$Url,[Net.Http.HttpContent]$Content=$null,[switch]$OAuth,[Nullable[long]]$RangeStart=$null){
 $null=Assert-L12BaiduUri $Url -OAuth:$OAuth
 $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),$Url);$request.Content=$Content
 if($null -ne $RangeStart){$request.Headers.Range=[Net.Http.Headers.RangeHeaderValue]::new($RangeStart,$null)}
 $cancel=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds((Get-L12BaiduRemaining $Context)))
 try{
  try{$response=$Context.http.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead,$cancel.Token).GetAwaiter().GetResult()}catch{throw 'L12_BAIDU_TRANSPORT_UNCERTAIN'}
  return [pscustomobject]@{response=$response;request=$request;cancel=$cancel}
 }catch{$request.Dispose();$cancel.Dispose();throw}
}
function Close-L12BaiduHttp($Reply){$Reply.response.Dispose();$Reply.request.Dispose();$Reply.cancel.Dispose()}
function Read-L12BaiduBounded($Stream,[long]$Limit,$CancellationToken){
 $buffer=[byte[]]::new(8192);$memory=[IO.MemoryStream]::new()
 try{while(($count=$Stream.ReadAsync($buffer,0,$buffer.Length,$CancellationToken).GetAwaiter().GetResult()) -gt 0){if($memory.Length+$count -gt $Limit){throw 'L12_BAIDU_RESPONSE_LIMIT'};$memory.Write($buffer,0,$count)};return ,$memory.ToArray()}finally{$memory.Dispose()}
}
function Invoke-L12BaiduJson($Context,[string]$Method,[string]$Url,[Net.Http.HttpContent]$Content=$null,[switch]$OAuth){
 $reply=Invoke-L12BaiduHttp $Context $Method $Url $Content -OAuth:$OAuth
 try{
  $response=$reply.response
  if(!$response.IsSuccessStatusCode){throw ('L12_BAIDU_HTTP_'+[int]$response.StatusCode)}
  $stream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
  [byte[]]$bytes=Read-L12BaiduBounded $stream 131072 $reply.cancel.Token
  # The official upload service declares charset=utf8; decode its JSON explicitly.
  try{$data=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json}catch{throw 'L12_BAIDU_INVALID_JSON'}
  foreach($property in @('errno','error_code')){if($data.PSObject.Properties[$property] -and [long]$data.$property -ne 0){throw ('L12_BAIDU_API_'+[long]$data.$property)}}
  return $data
 }finally{Close-L12BaiduHttp $reply}
}
function New-L12BaiduContext([string]$CredentialPath,[int]$Seconds,[int]$RequestTimeoutSeconds=120){
 $credentialFile=Assert-L12BaiduLocal $CredentialPath -Exists -Private
 $null=New-L12BaiduPrivateDirectory (Split-Path -Parent $credentialFile)
 $null=Assert-L12BaiduLocal ($credentialFile+'.lock') -Private
 $lease=[IO.File]::Open($credentialFile+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{
  $credential=Read-L12BaiduProtected $credentialFile
  if($credential.version -ne 1 -or $credential.appId -cne '124583157' -or $credential.appName -cne '十二军团私有备份' -or !$credential.accessToken -or !$credential.refreshToken -or $credential.scope -cne 'basic netdisk'){throw 'L12_BAIDU_CREDENTIAL_IDENTITY'}
  $handler=[Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false
  $http=[Net.Http.HttpClient]::new($handler);$http.Timeout=[Threading.Timeout]::InfiniteTimeSpan;$http.DefaultRequestHeaders.UserAgent.ParseAdd('pan.baidu.com')
  return [pscustomobject]@{credential=$credential;credentialPath=$credentialFile;credentialLease=$lease;http=$http;deadline=[DateTime]::UtcNow.AddSeconds($Seconds);requestTimeoutSeconds=$RequestTimeoutSeconds}
 }catch{$lease.Dispose();throw}
}
function Close-L12BaiduContext($Context){$Context.http.Dispose();$Context.credential=$null;$Context.credentialLease.Dispose()}
function Assert-L12BaiduCredentialUsable($Context){
 foreach($suffix in @('.refresh-attempt.dpapi','.refresh-pending.dpapi')){if(Test-Path -LiteralPath ($Context.credentialPath+$suffix)){throw 'L12_BAIDU_REFRESH_RECOVERY_REQUIRED'}}
 if([DateTime]::Parse($Context.credential.expiresUtc).ToUniversalTime() -lt $Context.deadline.AddMinutes(1)){throw 'L12_BAIDU_REFRESH_REQUIRED'}
}
function Get-L12BaiduCredentialStatus {
 param([string]$CredentialPath='E:/Legion12-ColdStorage/private-baidu-api-124583157/oauth.dpapi')
 $context=New-L12BaiduContext $CredentialPath 30
 try{[pscustomobject]@{appId=$context.credential.appId;appName=$context.credential.appName;scope=$context.credential.scope;expiresUtc=$context.credential.expiresUtc;refreshRecoveryRequired=((Test-Path -LiteralPath ($CredentialPath+'.refresh-attempt.dpapi')) -or (Test-Path -LiteralPath ($CredentialPath+'.refresh-pending.dpapi')));protection='Windows DPAPI CurrentUser'}}finally{Close-L12BaiduContext $context}
}
function Remove-L12BaiduRefreshMarker([string]$Path){
 $null=Assert-L12BaiduLocal $Path -Exists -Private
 if(!$Path.EndsWith('.refresh-attempt.dpapi') -and !$Path.EndsWith('.refresh-pending.dpapi')){throw 'L12_BAIDU_REFRESH_MARKER_PATH'}
 Remove-Item -LiteralPath $Path
}
function Update-L12BaiduAuthorization {
 param([string]$CredentialPath='E:/Legion12-ColdStorage/private-baidu-api-124583157/oauth.dpapi',[ValidateRange(5,300)][int]$TimeBudgetSeconds=60)
 $context=New-L12BaiduContext $CredentialPath $TimeBudgetSeconds
 try{
  $attempt=$context.credentialPath+'.refresh-attempt.dpapi';$pending=$context.credentialPath+'.refresh-pending.dpapi'
  if(Test-Path -LiteralPath $pending){
   $staged=Read-L12BaiduProtected $pending
   $currentSha=(Get-FileHash -LiteralPath $context.credentialPath -Algorithm SHA256).Hash
   if($currentSha -cne $staged.previousCipherSha256 -and $context.credential.refreshToken -cne $staged.credential.refreshToken){throw 'L12_BAIDU_REFRESH_CONFLICT'}
   if($staged.credential.version -ne 1 -or $staged.credential.appId -cne $context.credential.appId -or $staged.credential.appName -cne $context.credential.appName -or $staged.credential.clientId -cne $context.credential.clientId -or $staged.credential.clientSecret -cne $context.credential.clientSecret -or $staged.credential.scope -cne 'basic netdisk' -or !$staged.credential.refreshToken -or !$staged.credential.accessToken -or [DateTime]::Parse($staged.credential.expiresUtc).ToUniversalTime() -le [DateTime]::UtcNow){throw 'L12_BAIDU_REFRESH_PENDING_INVALID'}
   Write-L12BaiduProtected $context.credentialPath $staged.credential
   $readback=Read-L12BaiduProtected $context.credentialPath
   if($readback.refreshToken -cne $staged.credential.refreshToken){throw 'L12_BAIDU_REFRESH_READBACK'}
   # Keep pending until last: a crash during cleanup can still promote without another refresh.
   if(Test-Path -LiteralPath $attempt){Remove-L12BaiduRefreshMarker $attempt}
   Remove-L12BaiduRefreshMarker $pending
   return [pscustomobject]@{status='refresh-recovered';expiresUtc=$readback.expiresUtc}
  }
  if(Test-Path -LiteralPath $attempt){throw 'L12_BAIDU_REFRESH_UNCERTAIN_REAUTHORIZE_NO_RETRY'}
  $oldSha=(Get-FileHash -LiteralPath $context.credentialPath -Algorithm SHA256).Hash
  Write-L12BaiduProtected $attempt @{previousCipherSha256=$oldSha;startedUtc=[DateTime]::UtcNow.ToString('o')}
  # A refresh token is single use. Never automatically retry this request.
  $url=New-L12BaiduUrl 'https://openapi.baidu.com/oauth/2.0/token' @{grant_type='refresh_token';refresh_token=$context.credential.refreshToken;client_id=$context.credential.clientId;client_secret=$context.credential.clientSecret}
  $result=Invoke-L12BaiduJson $context 'GET' $url -OAuth
  if(!$result.PSObject.Properties['access_token'] -or !$result.PSObject.Properties['refresh_token'] -or !$result.access_token -or !$result.refresh_token -or $result.scope -cne 'basic netdisk' -or [long]$result.expires_in -le 0){throw 'L12_BAIDU_REFRESH_RESPONSE_INVALID'}
  $new=$context.credential.PSObject.Copy();$new.accessToken=$result.access_token;$new.refreshToken=$result.refresh_token;$new.expiresUtc=[DateTime]::UtcNow.AddSeconds([long]$result.expires_in).ToString('o')
  Write-L12BaiduProtected $pending @{previousCipherSha256=$oldSha;credential=$new}
  # Re-entry promotes this durable pending record, without consuming the old token twice.
 }finally{Close-L12BaiduContext $context}
 Update-L12BaiduAuthorization -CredentialPath $CredentialPath -TimeBudgetSeconds $TimeBudgetSeconds
}

function Read-L12BaiduManifest([string]$ManifestPath,[string]$ExpectedManifestSha256){
 $manifestStream=$null
 try{
 $manifestFile=Assert-L12BaiduLocal $ManifestPath -Exists -Private
 $manifestStream=[IO.File]::Open($manifestFile,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 if($ExpectedManifestSha256 -cnotmatch '^[a-f0-9]{64}$' -or $manifestStream.Length -gt 65536){throw 'L12_BAIDU_MANIFEST_SHA'}
 [byte[]]$manifestBytes=[byte[]]::new([int]$manifestStream.Length);$manifestStream.ReadExactly($manifestBytes);$manifestStream.Position=0
 if((Get-L12BaiduSha $manifestBytes) -cne $ExpectedManifestSha256){throw 'L12_BAIDU_MANIFEST_SHA'}
 try{$manifest=[Text.UTF8Encoding]::new($false,$true).GetString($manifestBytes)|ConvertFrom-Json}catch{throw 'L12_BAIDU_MANIFEST_FORMAT'}
 if($manifest.schema -ne 2 -or $manifest.format -cne 'l12-manual-cloud-archive-v2' -or $manifest.status -cne 'prepared-offline' -or $manifest.archiveSha256 -cnotmatch '^[a-f0-9]{64}$' -or [IO.Path]::GetFileName($manifestFile) -cne ($manifest.archiveSha256+'.manifest.json')){throw 'L12_BAIDU_MANIFEST_FORMAT'}
 $items=@($manifest.items);if($items.Count -lt 2 -or $items.Count -gt 18){throw 'L12_BAIDU_GROUP_COUNT'}
 if(@($items|Where-Object{$_.roles -contains 'archive'}).Count -ne 1 -or @($items|Where-Object{$_.roles -contains 'completion-manifest'}).Count -ne 1){throw 'L12_BAIDU_GROUP_ROLES'}
 $definitions=@();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($item in $items){
  if($item.cipher.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $item.cipher.fileName -cne ($item.cipher.sha256+'.age') -or [long]$item.cipher.bytes -lt 200 -or [long]$item.cipher.bytes -gt 4GB -or !$seen.Add($item.cipher.fileName)){throw 'L12_BAIDU_CIPHER_DEFINITION'}
  $definitions+=@{name=$item.cipher.fileName;sha256=$item.cipher.sha256;bytes=[long]$item.cipher.bytes;age=$true}
 }
 $definitions+=@{name=[IO.Path]::GetFileName($manifestFile);sha256=$ExpectedManifestSha256;bytes=$manifestStream.Length;age=$false}
 return [pscustomobject]@{manifestFile=$manifestFile;stream=$manifestStream;definitions=$definitions;manifestSha256=$ExpectedManifestSha256;remoteRoot=('/apps/十二军团私有备份/l12-'+$ExpectedManifestSha256)}
 }catch{if($manifestStream){$manifestStream.Dispose()};throw}
}
function Get-L12BaiduGroupPlan([string]$ManifestPath,[string]$ExpectedManifestSha256,[DateTime]$Deadline=[DateTime]::MaxValue){
 $files=[Collections.Generic.List[object]]::new();$trusted=$null
 try{
  $trusted=Read-L12BaiduManifest $ManifestPath $ExpectedManifestSha256
  $manifestFile=$trusted.manifestFile;$manifestStream=$trusted.stream
  $definitions=$trusted.definitions
  foreach($definition in $definitions){
   $path=Assert-L12BaiduLocal (Join-Path (Split-Path -Parent $manifestFile) $definition.name) -Exists -Private
   $stream=if($definition.age){[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)}else{$manifestStream}
   $file=[pscustomobject]@{name=$definition.name;sha256=$definition.sha256;bytes=$definition.bytes;parts=@();stream=$stream};$files.Add($file)
   if($stream.Length -ne $definition.bytes){throw 'L12_BAIDU_SOURCE_LENGTH'}
   if($definition.age){$header=[byte[]]::new(22);$stream.ReadExactly($header);if([Text.Encoding]::ASCII.GetString($header) -cne "age-encryption.org/v1`n"){throw 'L12_BAIDU_CIPHER_HEADER'};$stream.Position=0}
   $sha=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256);$md5=[Security.Cryptography.MD5]::Create();$buffer=[byte[]]::new($script:L12BaiduPartBytes)
   try{
    while($stream.Position -lt $stream.Length){if([DateTime]::UtcNow -gt $Deadline){throw 'L12_BAIDU_DEADLINE'};$length=[int][Math]::Min($buffer.Length,$stream.Length-$stream.Position);$stream.ReadExactly($buffer,0,$length);$sha.AppendData($buffer,0,$length);$file.parts+=([Convert]::ToHexString($md5.ComputeHash($buffer,0,$length)).ToLowerInvariant())}
    if([Convert]::ToHexString($sha.GetHashAndReset()).ToLowerInvariant() -cne $definition.sha256){throw 'L12_BAIDU_SOURCE_SHA'}
   }finally{$sha.Dispose();$md5.Dispose();[Array]::Clear($buffer,0,$buffer.Length)}
   $stream.Position=0
  }
  return [pscustomobject]@{manifestSha256=$ExpectedManifestSha256;remoteRoot=('/apps/十二军团私有备份/l12-'+$ExpectedManifestSha256);files=$files}
 }catch{foreach($file in $files){$file.stream.Dispose()};if($trusted){$trusted.stream.Dispose()};throw}
}
function Close-L12BaiduPlan($Plan){foreach($file in $Plan.files){$file.stream.Dispose()}}
function Assert-L12BaiduRemotePath([string]$Path,[string]$Root,[string]$Name){if($Path -cne ($Root+'/'+$Name) -or $Name -cnotmatch '^[a-f0-9]{64}\.(age|manifest\.json)$'){throw 'L12_BAIDU_REMOTE_PATH'}}
function Get-L12BaiduMetadata($Context,[string]$Path,[switch]$AllowMissing){
 $body=New-L12BaiduForm @{target=('['+($Path|ConvertTo-Json -Compress)+']');blocks=1;dlink=1}
 $url=New-L12BaiduUrl 'https://pan.baidu.com/rest/2.0/xpan/file' @{method='filemetas';access_token=$Context.credential.accessToken}
 try{$reply=Invoke-L12BaiduJson $Context 'POST' $url $body}catch{if($AllowMissing -and $_.Exception.Message -cin @('L12_BAIDU_API_-3','L12_BAIDU_API_-9','L12_BAIDU_API_31066')){return $null};throw}
 if(!$reply.PSObject.Properties['info'] -or @($reply.info).Count -ne 1){throw 'L12_BAIDU_META_CARDINALITY'}
 $item=$reply.info[0]
 if($item.PSObject.Properties['errno'] -and [long]$item.errno -ne 0){if($AllowMissing -and [long]$item.errno -in -3,-9,31066){return $null};throw ('L12_BAIDU_META_'+[long]$item.errno)}
 if($item.path -cne $Path -or !$item.fs_id){throw 'L12_BAIDU_META_IDENTITY'}
 return $item
}
function Assert-L12BaiduMetadataIdentity($Item,$File,[string]$Path){
 if($Item.path -cne $Path -or [long]$Item.size -ne $File.bytes -or [int]$Item.isdir -ne 0 -or [string]$Item.fs_id -cnotmatch '^[1-9][0-9]*$'){throw 'L12_BAIDU_META_MISMATCH'}
}
function Assert-L12BaiduMetadata($Item,$File,[string]$Path){
 Assert-L12BaiduMetadataIdentity $Item $File $Path
 if(!$Item.PSObject.Properties['block_list'] -or (@($Item.block_list) -join ',') -cne ($File.parts -join ',')){throw 'L12_BAIDU_META_BLOCKS'}
}
function New-L12BaiduTransferState($Plan){
 [pscustomobject]@{schema=1;appId='124583157';manifestSha256=$Plan.manifestSha256;remoteRoot=$Plan.remoteRoot;files=@($Plan.files|ForEach-Object{[pscustomobject]@{name=$_.name;bytes=$_.bytes;sha256=$_.sha256;parts=$_.parts;phase='planned';uploadid=$null;doneParts=@();fsId=$null;download=[pscustomobject]@{started=$false;bytes=0;prefixSha256=(Get-L12BaiduSha ([byte[]]::new(0)));fsId=$null;complete=$false}}});uploadCompleted=$false;downloadCompleted=$false}
}
function Read-L12BaiduTransferState($Plan,[string]$Path){
 if(!(Test-Path -LiteralPath $Path)){return New-L12BaiduTransferState $Plan}
 $state=Read-L12BaiduProtected $Path
 if($state.schema -ne 1 -or $state.appId -cne '124583157' -or $state.manifestSha256 -cne $Plan.manifestSha256 -or $state.remoteRoot -cne $Plan.remoteRoot -or @($state.files).Count -ne $Plan.files.Count){throw 'L12_BAIDU_STATE_IDENTITY'}
 for($i=0;$i -lt $Plan.files.Count;$i++){
  $a=$state.files[$i];$b=$Plan.files[$i]
  if($a.name -cne $b.name -or [long]$a.bytes -ne $b.bytes -or $a.sha256 -cne $b.sha256 -or (@($a.parts) -join ',') -cne ($b.parts -join ',') -or $a.phase -cnotin @('planned','parts','create-uncertain','created')){throw 'L12_BAIDU_STATE_SOURCE'}
  $partsSeen=[Collections.Generic.HashSet[int]]::new();foreach($part in @($a.doneParts)){if($part -isnot [long] -and $part -isnot [int]){throw 'L12_BAIDU_STATE_PART'};if($part -lt 0 -or $part -ge $b.parts.Count -or !$partsSeen.Add([int]$part)){throw 'L12_BAIDU_STATE_PART'}}
 }
 return $state
}
function Invoke-L12BaiduArchiveUpload {
 param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$ExpectedManifestSha256,[Parameter(Mandatory)][string]$TransferDirectory,[string]$CredentialPath='E:/Legion12-ColdStorage/private-baidu-api-124583157/oauth.dpapi',[ValidateRange(5,3600)][int]$TimeBudgetSeconds=300,[ValidateRange(5,300)][int]$RequestTimeoutSeconds=120)
 $directory=New-L12BaiduPrivateDirectory $TransferDirectory;$leasePath=Assert-L12BaiduLocal (Join-Path $directory 'transfer.lock') -Private
 $lease=[IO.File]::Open($leasePath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$context=$null;$plan=$null
 try{
  $context=New-L12BaiduContext $CredentialPath $TimeBudgetSeconds $RequestTimeoutSeconds;Assert-L12BaiduCredentialUsable $context
  $plan=Get-L12BaiduGroupPlan $ManifestPath $ExpectedManifestSha256 $context.deadline;$statePath=Join-Path $directory 'transfer.dpapi';$state=Read-L12BaiduTransferState $plan $statePath
  Write-L12BaiduProtected $statePath $state
  for($index=0;$index -lt $plan.files.Count;$index++){
   $file=$plan.files[$index];$entry=$state.files[$index];$path=$plan.remoteRoot+'/'+$file.name;Assert-L12BaiduRemotePath $path $plan.remoteRoot $file.name
   if($entry.phase -cin @('created','create-uncertain')){
    $metadata=Get-L12BaiduMetadata $context $path -AllowMissing:($entry.phase -ceq 'create-uncertain')
    if($null -eq $metadata){$entry.phase='parts';Write-L12BaiduProtected $statePath $state}else{
     Assert-L12BaiduMetadata $metadata $file $path
     if($entry.fsId -and [string]$metadata.fs_id -cne $entry.fsId){throw 'L12_BAIDU_REMOTE_REPLACED'}
     $entry.phase='created';$entry.fsId=[string]$metadata.fs_id;Write-L12BaiduProtected $statePath $state;continue
    }
   }
   if($entry.phase -ceq 'planned'){
    $body=New-L12BaiduForm @{path=$path;size=$file.bytes;isdir=0;block_list=(ConvertTo-Json -InputObject @($file.parts) -Compress);autoinit=1;rtype=0}
    $pre=Invoke-L12BaiduJson $context 'POST' (New-L12BaiduUrl 'https://pan.baidu.com/rest/2.0/xpan/file' @{method='precreate';access_token=$context.credential.accessToken}) $body
    # The live precreate response may omit the optional path echo. The task stays
    # bound to this exact path/size/parts; reject an echo if it is present but wrong.
    if(!$pre.uploadid -or ($pre.PSObject.Properties['path'] -and $pre.path -cne $path)){throw 'L12_BAIDU_PRECREATE_IDENTITY'}
    $required=@($pre.block_list);if($required.Count -eq 0){$required=@(0)}
    $requiredSet=[Collections.Generic.HashSet[int]]::new()
    foreach($part in $required){if(($part -isnot [long] -and $part -isnot [int]) -or $part -lt 0 -or $part -ge $file.parts.Count -or !$requiredSet.Add([int]$part)){throw 'L12_BAIDU_PRECREATE_PARTS'}}
    $entry.uploadid=$pre.uploadid;$entry.phase='parts';$entry.doneParts=@(0..($file.parts.Count-1)|Where-Object{!$requiredSet.Contains($_)})
    Write-L12BaiduProtected $statePath $state
   }
   if(@(0..($file.parts.Count-1)|Where-Object{$entry.doneParts -notcontains $_}).Count){
    $locate=Invoke-L12BaiduJson $context 'GET' (New-L12BaiduUrl 'https://d.pcs.baidu.com/rest/2.0/pcs/file' @{method='locateupload';appid=250528;access_token=$context.credential.accessToken;path=$path;uploadid=$entry.uploadid;upload_version='2.0'})
    $servers=@($locate.servers|Where-Object{$_.server -clike 'https://*'});if(!$servers.Count){throw 'L12_BAIDU_UPLOAD_SERVER'}
    $origin=$servers[0].server;$null=Assert-L12BaiduUri $origin -UploadOrigin
   }
   for($part=0;$part -lt $file.parts.Count;$part++){
    if($entry.doneParts -contains $part){continue}
    $null=Get-L12BaiduRemaining $context
    $file.stream.Position=[long]$part*$script:L12BaiduPartBytes;$length=[int][Math]::Min($script:L12BaiduPartBytes,$file.bytes-$file.stream.Position);$buffer=[byte[]]::new($length);$file.stream.ReadExactly($buffer)
    try{
     $uploadUrl=New-L12BaiduUrl ($origin.TrimEnd('/')+'/rest/2.0/pcs/superfile2') @{method='upload';access_token=$context.credential.accessToken;type='tmpfile';path=$path;uploadid=$entry.uploadid;partseq=$part}
     $reply=$null
     for($attempt=0;$attempt -lt 2;$attempt++){
      $multipart=[Net.Http.MultipartFormDataContent]::new();$multipart.Add([Net.Http.ByteArrayContent]::new($buffer),'file',$file.name)
      try{$reply=Invoke-L12BaiduJson $context 'POST' $uploadUrl $multipart;break}catch{if($_.Exception.Message -cne 'L12_BAIDU_TRANSPORT_UNCERTAIN' -or $attempt -eq 1){throw};Start-Sleep -Milliseconds 250}
     }
     if($reply.md5 -cne $file.parts[$part]){throw 'L12_BAIDU_PART_MD5'}
     $entry.doneParts+=@($part);Write-L12BaiduProtected $statePath $state
    }finally{[Array]::Clear($buffer,0,$buffer.Length)}
   }
   if(@($entry.doneParts|Select-Object -Unique).Count -ne $file.parts.Count){throw 'L12_BAIDU_PARTS_INCOMPLETE'}
   $entry.phase='create-uncertain';Write-L12BaiduProtected $statePath $state
   $body=New-L12BaiduForm @{path=$path;size=$file.bytes;isdir=0;block_list=(ConvertTo-Json -InputObject @($file.parts) -Compress);uploadid=$entry.uploadid;rtype=0;mode=1}
   $created=Invoke-L12BaiduJson $context 'POST' (New-L12BaiduUrl 'https://pan.baidu.com/rest/2.0/xpan/file' @{method='create';access_token=$context.credential.accessToken}) $body
   if($created.path -cne $path -or [long]$created.size -ne $file.bytes -or !$created.fs_id){throw 'L12_BAIDU_CREATED_IDENTITY'}
   $entry.fsId=[string]$created.fs_id;$entry.phase='created';Write-L12BaiduProtected $statePath $state
  }
  $state.uploadCompleted=$true;Write-L12BaiduProtected $statePath $state
  [pscustomobject]@{status='uploaded-awaiting-independent-download';manifestSha256=$plan.manifestSha256;remoteRoot=$plan.remoteRoot;fileCount=$plan.files.Count;sourceDeleted=$false}
 }finally{if($plan){Close-L12BaiduPlan $plan};if($context){Close-L12BaiduContext $context};$lease.Dispose()}
}

function Get-L12BaiduPrefixSha([string]$Path,[long]$Bytes){
 $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 $sha=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
 try{if($stream.Length -lt $Bytes){throw 'L12_BAIDU_PARTIAL_CHANGED'};$buffer=[byte[]]::new(65536);$remaining=$Bytes;while($remaining -gt 0){$count=$stream.Read($buffer,0,[int][Math]::Min($remaining,$buffer.Length));if($count -eq 0){throw 'L12_BAIDU_PARTIAL_CHANGED'};$sha.AppendData($buffer,0,$count);$remaining-=$count};[Convert]::ToHexString($sha.GetHashAndReset()).ToLowerInvariant()}finally{$sha.Dispose();$stream.Dispose()}
}
function Copy-L12BaiduDownloadBody($Stream,[string]$PartialPath,[long]$ExpectedBytes,[long]$Start,$CancellationToken,[scriptblock]$Checkpoint){
 $file=[IO.File]::Open($PartialPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::None)
 $sha=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
 $lastCheckpoint=$Start
 try{
  if($file.Length -ne $Start){throw 'L12_BAIDU_PARTIAL_CHANGED'}
  if($Start -gt 0){$file.Dispose();$read=[IO.File]::OpenRead($PartialPath);try{$buffer=[byte[]]::new(65536);while(($count=$read.Read($buffer,0,$buffer.Length)) -gt 0){$sha.AppendData($buffer,0,$count)}}finally{$read.Dispose()};$file=[IO.File]::Open($PartialPath,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::None);if($file.Length -ne $Start){throw 'L12_BAIDU_PARTIAL_CHANGED'}}
  $file.Position=$Start;$buffer=[byte[]]::new(65536);$lastCheckpoint=$Start
  while(($count=$Stream.ReadAsync($buffer,0,$buffer.Length,$CancellationToken).GetAwaiter().GetResult()) -gt 0){
   if($file.Position+$count -gt $ExpectedBytes){throw 'L12_BAIDU_DOWNLOAD_TOO_LARGE'};$file.Write($buffer,0,$count);$sha.AppendData($buffer,0,$count)
   if($file.Position-$lastCheckpoint -ge 4MB -or $file.Position -eq $ExpectedBytes){$file.Flush($true);if($Checkpoint){& $Checkpoint $file.Position ([Convert]::ToHexString($sha.GetCurrentHash()).ToLowerInvariant())};$lastCheckpoint=$file.Position}
  }
  $file.Flush($true);if($file.Length -ne $ExpectedBytes){throw 'L12_BAIDU_DOWNLOAD_INCOMPLETE'}
 }finally{
  try{
   if($file.CanWrite -and $file.Position -gt $lastCheckpoint){
    $file.Flush($true)
    if($Checkpoint){& $Checkpoint $file.Position ([Convert]::ToHexString($sha.GetCurrentHash()).ToLowerInvariant())}
   }
  }finally{$file.Dispose();$sha.Dispose()}
 }
}
function Receive-L12BaiduFile($Context,[string]$Dlink,[string]$PartialPath,[long]$Bytes,[scriptblock]$Checkpoint){
 $start=if(Test-Path -LiteralPath $PartialPath){(Get-Item -LiteralPath $PartialPath).Length}else{0}
 if($start -gt $Bytes){throw 'L12_BAIDU_PARTIAL_TOO_LARGE'};if($start -eq $Bytes){return}
 if($Dlink -match '(?i)[?&]access_token='){throw 'L12_BAIDU_DLINK_TOKEN'}
 $url=$Dlink+'&access_token='+[Uri]::EscapeDataString($Context.credential.accessToken)
 for($redirect=0;$redirect -le 5;$redirect++){
  $reply=Invoke-L12BaiduHttp $Context 'GET' $url -RangeStart $start
  try{
   $response=$reply.response
   if([int]$response.StatusCode -in 301,302,303,307,308){if(!$response.Headers.Location){throw 'L12_BAIDU_REDIRECT_MISSING'};$next=[Uri]::new([Uri]$url,$response.Headers.Location);$null=Assert-L12BaiduUri $next.AbsoluteUri;$url=$next.AbsoluteUri;continue}
   $status=[int]$response.StatusCode
   if(($start -gt 0 -and $status -ne 206) -or ($start -eq 0 -and $status -notin 200,206)){throw 'L12_BAIDU_RANGE_STATUS'}
   if($status -eq 206){$range=$response.Content.Headers.ContentRange;if(!$range -or $range.Unit -cne 'bytes' -or $range.From -ne $start -or $range.To -ne ($Bytes-1) -or $range.Length -ne $Bytes){throw 'L12_BAIDU_RANGE_IDENTITY'}}
   if($null -ne $response.Content.Headers.ContentLength -and $response.Content.Headers.ContentLength -ne ($Bytes-$start)){throw 'L12_BAIDU_DOWNLOAD_LENGTH'}
   $stream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
   try{Copy-L12BaiduDownloadBody $stream $PartialPath $Bytes $start $reply.cancel.Token $Checkpoint}catch{if($_.Exception.Message -clike 'L12_BAIDU_*'){throw};throw 'L12_BAIDU_DOWNLOAD_INTERRUPTED'}
   return
  }finally{Close-L12BaiduHttp $reply}
 }
 throw 'L12_BAIDU_REDIRECT_LIMIT'
}
function Receive-L12BaiduTrackedFile($Context,$File,$Entry,[string]$Path,[string]$Returned,[string]$StatePath,$State,[switch]$ManifestOnly){
 $output=Assert-L12BaiduLocal (Join-Path $Returned $File.name) -Private;$partial=Assert-L12BaiduLocal ($output+'.partial') -Private
 $progress=$Entry.download
 if((Test-Path -LiteralPath $output) -and (!$progress.started -or $progress.bytes -ne $File.bytes -or $progress.prefixSha256 -cne $File.sha256 -or $progress.fsId -cne $Entry.fsId)){throw 'L12_BAIDU_UNTRACKED_RETURNED'}
 if(!(Test-Path -LiteralPath $output)){
  $metadata=Get-L12BaiduMetadata $Context $Path
  if($ManifestOnly){Assert-L12BaiduMetadataIdentity $metadata $File $Path}else{Assert-L12BaiduMetadata $metadata $File $Path}
  if(($Entry.fsId -and [string]$metadata.fs_id -cne $Entry.fsId) -or !$metadata.dlink){throw 'L12_BAIDU_REMOTE_REPLACED'}
  if(!$Entry.fsId){if(!$ManifestOnly){throw 'L12_BAIDU_REMOTE_REPLACED'};$Entry.fsId=[string]$metadata.fs_id;Write-L12BaiduProtected $StatePath $State}
  if(!$progress.started){
   if(Test-Path -LiteralPath $partial){throw 'L12_BAIDU_UNTRACKED_PARTIAL'}
   $progress.started=$true;$progress.fsId=$Entry.fsId;Write-L12BaiduProtected $StatePath $State
  }
  if($progress.fsId -cne $Entry.fsId -or $progress.bytes -lt 0 -or $progress.bytes -gt $File.bytes){throw 'L12_BAIDU_DOWNLOAD_STATE'}
  if(Test-Path -LiteralPath $partial){
   if((Get-L12BaiduPrefixSha $partial $progress.bytes) -cne $progress.prefixSha256){throw 'L12_BAIDU_PARTIAL_CHANGED'}
   $trim=[IO.File]::Open($partial,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$trim.SetLength($progress.bytes);$trim.Flush($true)}finally{$trim.Dispose()}
  }elseif($progress.bytes -ne 0){throw 'L12_BAIDU_PARTIAL_MISSING'}
  $checkpoint={param($length,$prefixSha);$progress.bytes=$length;$progress.prefixSha256=$prefixSha;Write-L12BaiduProtected $StatePath $State}
  Receive-L12BaiduFile $Context $metadata.dlink $partial $File.bytes $checkpoint
  if((Get-Item -LiteralPath $partial).Length -ne $File.bytes -or (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ine $File.sha256){throw 'L12_BAIDU_RETURNED_SHA'}
  [IO.File]::Move($partial,$output)
 }
 if((Get-Item -LiteralPath $output).Length -ne $File.bytes -or (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash -ine $File.sha256){throw 'L12_BAIDU_RETURNED_SHA'}
 $progress.complete=$true;Write-L12BaiduProtected $StatePath $State
}
function Invoke-L12BaiduArchiveDownload {
 param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$ExpectedManifestSha256,[Parameter(Mandatory)][string]$TransferDirectory,[string]$CredentialPath='E:/Legion12-ColdStorage/private-baidu-api-124583157/oauth.dpapi',[ValidateRange(5,3600)][int]$TimeBudgetSeconds=300,[ValidateRange(5,300)][int]$RequestTimeoutSeconds=120)
 $directory=New-L12BaiduPrivateDirectory $TransferDirectory;$lease=[IO.File]::Open((Assert-L12BaiduLocal (Join-Path $directory 'transfer.lock') -Private),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$context=$null;$plan=$null
 try{
  $context=New-L12BaiduContext $CredentialPath $TimeBudgetSeconds $RequestTimeoutSeconds;Assert-L12BaiduCredentialUsable $context
  $plan=Get-L12BaiduGroupPlan $ManifestPath $ExpectedManifestSha256 $context.deadline;$statePath=Join-Path $directory 'transfer.dpapi';$state=Read-L12BaiduTransferState $plan $statePath
  if(!$state.uploadCompleted -or @($state.files|Where-Object{$_.phase -cne 'created' -or !$_.fsId}).Count){throw 'L12_BAIDU_UPLOAD_NOT_COMPLETE'}
  $returned=New-L12BaiduPrivateDirectory (Join-Path $directory 'returned')
  for($i=0;$i -lt $plan.files.Count;$i++){
   $file=$plan.files[$i];$entry=$state.files[$i];$path=$plan.remoteRoot+'/'+$file.name
   Receive-L12BaiduTrackedFile $context $file $entry $path $returned $statePath $state
  }
  $state.downloadCompleted=$true;Write-L12BaiduProtected $statePath $state
  $receipt=[ordered]@{schema=1;status='cloud-cipher-roundtrip-verified';completedUtc=[DateTime]::UtcNow.ToString('o');manifestSha256=$plan.manifestSha256;remoteRoot=$plan.remoteRoot;returnedDirectory=$returned;files=@($state.files|Select-Object name,bytes,sha256,fsId);cloudProvenanceVerified=$true;plaintextRecoveryVerified=$false;sourceDeleted=$false}
  $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $directory 'cloud-receipt.json') -Encoding utf8
  [pscustomobject]$receipt
 }finally{if($plan){Close-L12BaiduPlan $plan};if($context){Close-L12BaiduContext $context};$lease.Dispose()}
}
function Read-L12BaiduRestoreState($Plan,[string]$Path){
 if(!(Test-Path -LiteralPath $Path)){
  return [pscustomobject]@{schema=1;kind='manifest-only-restore';appId='124583157';manifestSha256=$Plan.manifestSha256;remoteRoot=$Plan.remoteRoot;files=@($Plan.definitions|ForEach-Object{[pscustomobject]@{name=$_.name;bytes=$_.bytes;sha256=$_.sha256;fsId=$null;download=[pscustomobject]@{started=$false;bytes=0;prefixSha256=(Get-L12BaiduSha ([byte[]]::new(0)));fsId=$null;complete=$false}}});downloadCompleted=$false}
 }
 $state=Read-L12BaiduProtected $Path
 if($state.schema -ne 1 -or $state.kind -cne 'manifest-only-restore' -or $state.appId -cne '124583157' -or $state.manifestSha256 -cne $Plan.manifestSha256 -or $state.remoteRoot -cne $Plan.remoteRoot -or @($state.files).Count -ne $Plan.definitions.Count){throw 'L12_BAIDU_RESTORE_STATE_IDENTITY'}
 for($i=0;$i -lt $Plan.definitions.Count;$i++){
  $entry=$state.files[$i];$file=$Plan.definitions[$i];$progress=$entry.download
  if($entry.name -cne $file.name -or $entry.sha256 -cne $file.sha256 -or [long]$entry.bytes -ne $file.bytes){throw 'L12_BAIDU_RESTORE_STATE_SOURCE'}
  if(($entry.fsId -and [string]$entry.fsId -cnotmatch '^[1-9][0-9]*$') -or $progress.started -isnot [bool] -or $progress.complete -isnot [bool] -or ($progress.bytes -isnot [long] -and $progress.bytes -isnot [int]) -or $progress.bytes -lt 0 -or $progress.bytes -gt $file.bytes -or $progress.prefixSha256 -cnotmatch '^[a-f0-9]{64}$' -or ($progress.started -and (!$entry.fsId -or $progress.fsId -cne $entry.fsId)) -or (!$progress.started -and ($progress.bytes -ne 0 -or $progress.complete))){throw 'L12_BAIDU_DOWNLOAD_STATE'}
 }
 return $state
}
function Invoke-L12BaiduArchiveRestore {
 param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$ExpectedManifestSha256,[Parameter(Mandatory)][string]$TransferDirectory,[string]$CredentialPath='E:/Legion12-ColdStorage/private-baidu-api-124583157/oauth.dpapi',[ValidateRange(5,3600)][int]$TimeBudgetSeconds=300,[ValidateRange(5,300)][int]$RequestTimeoutSeconds=120)
 $trusted=$null;$lease=$null;$context=$null
 try{
  # Only this trusted, locked manifest is needed; neither source ciphers nor old-machine state is read.
  $trusted=Read-L12BaiduManifest $ManifestPath $ExpectedManifestSha256
  $directory=New-L12BaiduPrivateDirectory $TransferDirectory
  $lease=[IO.File]::Open((Assert-L12BaiduLocal (Join-Path $directory 'restore.lock') -Private),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  $statePath=Join-Path $directory 'restore.dpapi'
  if(!(Test-Path -LiteralPath $statePath) -and @(Get-ChildItem -LiteralPath $directory -Force|Where-Object{$_.Name -cne 'restore.lock'}).Count){throw 'L12_BAIDU_RESTORE_DIRECTORY_NOT_EMPTY'}
  $state=Read-L12BaiduRestoreState $trusted $statePath;Write-L12BaiduProtected $statePath $state
  $context=New-L12BaiduContext $CredentialPath $TimeBudgetSeconds $RequestTimeoutSeconds;Assert-L12BaiduCredentialUsable $context
  $returned=New-L12BaiduPrivateDirectory (Join-Path $directory 'returned')
  for($i=0;$i -lt $trusted.definitions.Count;$i++){
   $file=$trusted.definitions[$i];$entry=$state.files[$i];$path=$trusted.remoteRoot+'/'+$file.name
   Assert-L12BaiduRemotePath $path $trusted.remoteRoot $file.name
   Receive-L12BaiduTrackedFile $context $file $entry $path $returned $statePath $state -ManifestOnly
  }
  $state.downloadCompleted=$true;Write-L12BaiduProtected $statePath $state
  $receipt=[ordered]@{schema=1;status='cloud-cipher-restore-verified';completedUtc=[DateTime]::UtcNow.ToString('o');manifestSha256=$trusted.manifestSha256;remoteRoot=$trusted.remoteRoot;returnedDirectory=$returned;files=@($state.files|Select-Object name,bytes,sha256,fsId);cloudProvenanceVerified=$true;plaintextRecoveryVerified=$false;sourceCipherRequired=$false;oldTransferStateRequired=$false;sourceDeleted=$false}
  $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $directory 'restore-receipt.json') -Encoding utf8
  [pscustomobject]$receipt
 }finally{if($context){Close-L12BaiduContext $context};if($lease){$lease.Dispose()};if($trusted){$trusted.stream.Dispose()}}
}
Export-ModuleMember -Function Get-L12BaiduCredentialStatus,Update-L12BaiduAuthorization,Invoke-L12BaiduArchiveUpload,Invoke-L12BaiduArchiveDownload,Invoke-L12BaiduArchiveRestore
