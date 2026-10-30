# Explicit transport envelope only; the original trusted V2/age bytes never change.
$script:L12BaiduVolumeMaximum=1GB
$script:L12BaiduVolumeGroupMaximum=64GB
$script:L12BaiduVolumeReferenceMaximum=128

function ConvertFrom-L12BaiduVolumeJson([byte[]]$Bytes){
 try{
  $text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes)
  $document=[Text.Json.JsonDocument]::Parse($text)
  try{
   function Check-VolumeJsonElement($Element){
    if($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object){
     $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
     foreach($property in $Element.EnumerateObject()){if(!$seen.Add($property.Name)){throw 'duplicate JSON key'};Check-VolumeJsonElement $property.Value}
    }elseif($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array){foreach($element in $Element.EnumerateArray()){Check-VolumeJsonElement $element}}
   }
   Check-VolumeJsonElement $document.RootElement
  }finally{$document.Dispose()}
  return ConvertFrom-Json -InputObject $text -Depth 32
 }catch{throw 'L12_BAIDU_VOLUME_FORMAT'}
}
function Assert-L12BaiduVolumeKeys($Value,[string[]]$Keys){
 $actual=@($Value.PSObject.Properties.Name)
 $set=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($name in $actual){$null=$set.Add($name)}
 if($actual.Count -ne $Keys.Count -or !$set.SetEquals($Keys)){throw 'L12_BAIDU_VOLUME_FORMAT'}
}
function Test-L12BaiduVolumeInteger($Value){($Value -is [long]) -or ($Value -is [int])}
function Read-L12BaiduVolumeDocument([string]$Path,[string]$Expected,[long]$Maximum){
 $stream=$null
 try{
  $path=Assert-L12BaiduLocal $Path -Exists -Private
  if($Expected -cnotmatch '^[a-f0-9]{64}$'){throw 'L12_BAIDU_MANIFEST_SHA'}
  $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  if($stream.Length -le 0 -or $stream.Length -gt $Maximum){throw 'L12_BAIDU_VOLUME_LIMIT'}
  $raw=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($raw);$stream.Position=0
  if((Get-L12BaiduSha $raw) -cne $Expected){throw 'L12_BAIDU_MANIFEST_SHA'}
  [pscustomobject]@{path=$path;stream=$stream;raw=$raw;value=(ConvertFrom-L12BaiduVolumeJson $raw)}
 }catch{if($stream){$stream.Dispose()};throw}
}
function Get-L12BaiduVolumeOriginalModel($Manifest,[string]$FileName){
 if(!(Test-L12BaiduVolumeInteger $Manifest.schema) -or $Manifest.schema -ne 2 -or $Manifest.format -isnot [string] -or $Manifest.format -cne 'l12-manual-cloud-archive-v2' -or
    $Manifest.status -isnot [string] -or $Manifest.status -cne 'prepared-offline' -or $Manifest.items -isnot [array] -or $Manifest.archiveSha256 -isnot [string] -or
    $Manifest.archiveSha256 -cnotmatch '^[a-f0-9]{64}$' -or $FileName -cne ($Manifest.archiveSha256+'.manifest.json')){throw 'L12_BAIDU_VOLUME_ORIGINAL'}
 $items=@($Manifest.items)
 if($items.Count -lt 2 -or $items.Count -gt 18 -or @($items|Where-Object{$_.roles -contains 'archive'}).Count -ne 1 -or
    @($items|Where-Object{$_.roles -contains 'completion-manifest'}).Count -ne 1){throw 'L12_BAIDU_VOLUME_ORIGINAL'}
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$ciphers=@();[long]$total=0
 foreach($item in $items){
  $cipher=$item.cipher
  if(!(Test-L12BaiduVolumeInteger $cipher.bytes) -or $cipher.bytes -lt 200 -or $cipher.bytes -gt $script:L12BaiduVolumeGroupMaximum -or $cipher.sha256 -isnot [string] -or
     $cipher.fileName -isnot [string] -or $cipher.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $cipher.fileName -cne ($cipher.sha256+'.age') -or !$seen.Add($cipher.fileName)){throw 'L12_BAIDU_VOLUME_ORIGINAL'}
  $total+=[long]$cipher.bytes;if($total -gt $script:L12BaiduVolumeGroupMaximum){throw 'L12_BAIDU_VOLUME_LIMIT'}
  $ciphers+=@{name=$cipher.fileName;sha256=$cipher.sha256;bytes=[long]$cipher.bytes}
 }
 [pscustomobject]@{ciphers=$ciphers;total=$total}
}
function Read-L12BaiduVolumeEnvelope([string]$Path,[string]$Expected,[string]$OriginalExpected){
 $document=$null
 try{
  if($OriginalExpected -cnotmatch '^[a-f0-9]{64}$' -or [IO.Path]::GetFileName($Path) -cne ($Expected+'.volumes.json')){throw 'L12_BAIDU_VOLUME_TRUST'}
  $document=Read-L12BaiduVolumeDocument $Path $Expected 256KB;$envelope=$document.value
  Assert-L12BaiduVolumeKeys $envelope @('schema','format','status','volumeBytes','originalManifest','ciphers')
  if(!(Test-L12BaiduVolumeInteger $envelope.schema) -or $envelope.schema -ne 1 -or $envelope.format -isnot [string] -or $envelope.format -cne 'l12-baidu-transport-volumes-v1' -or
     $envelope.status -isnot [string] -or $envelope.status -cne 'prepared-offline' -or $envelope.ciphers -isnot [array] -or
     !(Test-L12BaiduVolumeInteger $envelope.volumeBytes) -or $envelope.volumeBytes -lt 64KB -or $envelope.volumeBytes -gt $script:L12BaiduVolumeMaximum){throw 'L12_BAIDU_VOLUME_FORMAT'}
  $original=$envelope.originalManifest;Assert-L12BaiduVolumeKeys $original @('fileName','sha256','bytes','contentBase64')
  if($original.sha256 -isnot [string] -or $original.sha256 -cne $OriginalExpected -or $original.fileName -isnot [string] -or !(Test-L12BaiduVolumeInteger $original.bytes) -or $original.bytes -le 0 -or $original.bytes -gt 65536 -or
     $original.contentBase64 -isnot [string] -or $original.contentBase64.Length -gt 87384){throw 'L12_BAIDU_VOLUME_TRUST'}
  try{$originalRaw=[Convert]::FromBase64String($original.contentBase64)}catch{throw 'L12_BAIDU_VOLUME_ORIGINAL'}
  if($originalRaw.Length -ne $original.bytes -or [Convert]::ToBase64String($originalRaw) -cne $original.contentBase64 -or
     (Get-L12BaiduSha $originalRaw) -cne $OriginalExpected){throw 'L12_BAIDU_VOLUME_TRUST'}
  $model=Get-L12BaiduVolumeOriginalModel (ConvertFrom-L12BaiduVolumeJson $originalRaw) $original.fileName
  if(@($envelope.ciphers).Count -ne $model.ciphers.Count){throw 'L12_BAIDU_VOLUME_ORDER'}
  $definitions=[Collections.Generic.List[object]]::new();$byName=@{};[int]$references=0
  for($i=0;$i -lt $model.ciphers.Count;$i++){
   $cipher=$envelope.ciphers[$i];$expectedCipher=$model.ciphers[$i]
   Assert-L12BaiduVolumeKeys $cipher @('name','sha256','bytes','volumes')
   if($cipher.name -isnot [string] -or $cipher.name -cne $expectedCipher.name -or $cipher.sha256 -isnot [string] -or $cipher.sha256 -cne $expectedCipher.sha256 -or
      !(Test-L12BaiduVolumeInteger $cipher.bytes) -or $cipher.bytes -ne $expectedCipher.bytes -or $cipher.volumes -isnot [array] -or @($cipher.volumes).Count -lt 1){throw 'L12_BAIDU_VOLUME_ORDER'}
   [long]$offset=0;[int]$index=0
   foreach($volume in @($cipher.volumes)){
    $references++;if($references -gt $script:L12BaiduVolumeReferenceMaximum){throw 'L12_BAIDU_VOLUME_LIMIT'}
    Assert-L12BaiduVolumeKeys $volume @('index','offset','bytes','sha256','name')
    $length=[long][Math]::Min([long]$envelope.volumeBytes,[long]$cipher.bytes-$offset)
    if(!(Test-L12BaiduVolumeInteger $volume.index) -or $volume.index -ne $index -or !(Test-L12BaiduVolumeInteger $volume.offset) -or $volume.offset -ne $offset -or
       !(Test-L12BaiduVolumeInteger $volume.bytes) -or $length -le 0 -or $volume.bytes -ne $length -or $volume.sha256 -isnot [string] -or $volume.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
       $volume.name -isnot [string] -or $volume.name -cne ($volume.sha256+'.volume')){throw 'L12_BAIDU_VOLUME_ORDER'}
    # Repeated content is legitimate at distinct ordered offsets. Only the cloud
    # object is deduplicated; every occurrence remains in the reconstruction list.
    if($byName.ContainsKey($volume.name)){
     if($byName[$volume.name].bytes -ne $volume.bytes){throw 'L12_BAIDU_VOLUME_ORDER'}
    }else{$definition=@{name=$volume.name;sha256=$volume.sha256;bytes=[long]$volume.bytes;age=$false};$byName[$volume.name]=$definition;$definitions.Add($definition)}
    $offset+=[long]$volume.bytes;$index++
   }
   if($offset -ne $cipher.bytes){throw 'L12_BAIDU_VOLUME_ORDER'}
  }
  $definitions.Add(@{name=$original.fileName;sha256=$OriginalExpected;bytes=[long]$original.bytes;age=$false})
  $definitions.Add(@{name=[IO.Path]::GetFileName($document.path);sha256=$Expected;bytes=$document.stream.Length;age=$false})
  [pscustomobject]@{manifestFile=$document.path;stream=$document.stream;manifestSha256=$Expected;definitions=$definitions.ToArray();
   remoteRoot=('/apps/十二军团私有备份/l12-volumes-v1-'+$Expected);volumeCiphers=@($envelope.ciphers);originalManifestName=$original.fileName;
   originalManifestSha256=$OriginalExpected;originalManifestBytes=$originalRaw;transportFormat=$envelope.format}
 }catch{if($document){$document.stream.Dispose()};if($_.Exception.Message -cmatch '^L12_BAIDU_[A-Z0-9_-]+$'){throw};throw 'L12_BAIDU_VOLUME_FORMAT'}
}
function Write-L12BaiduNewBytes([string]$Path,[byte[]]$Bytes){
 $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try{$stream.Write($Bytes);$stream.Flush($true)}finally{$stream.Dispose()}
}
function New-L12BaiduTransportVolumes {
 param([Parameter(Mandatory)][string]$ManifestPath,[Parameter(Mandatory)][string]$ExpectedManifestSha256,
       [Parameter(Mandatory)][string]$TransferDirectory,[ValidateRange(65536,1073741824)][long]$VolumeBytes=1GB,
       [ValidateRange(5,3600)][int]$TimeBudgetSeconds=300)
 $source=$null;$lock=$null;$deadline=[DateTime]::UtcNow.AddSeconds($TimeBudgetSeconds)
 try{
  $source=Read-L12BaiduVolumeDocument $ManifestPath $ExpectedManifestSha256 65536
  $model=Get-L12BaiduVolumeOriginalModel $source.value ([IO.Path]::GetFileName($source.path))
  [long]$count=0
  foreach($cipher in $model.ciphers){[long]$remainder=0;$count+=[Math]::DivRem([long]$cipher.bytes,$VolumeBytes,[ref]$remainder);if($remainder){$count++}}
  if($count -gt $script:L12BaiduVolumeReferenceMaximum){throw 'L12_BAIDU_VOLUME_LIMIT'}
  $directory=New-L12BaiduPrivateDirectory $TransferDirectory
  if(@(Get-ChildItem -LiteralPath $directory -Force).Count){throw 'L12_BAIDU_VOLUME_DIRECTORY_NOT_EMPTY'}
  if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($directory))).AvailableFreeSpace -lt $model.total+64MB){throw 'L12_BAIDU_VOLUME_CAPACITY'}
  $lock=[IO.File]::Open((Join-Path $directory 'prepare-volumes.lock'),[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  $ciphers=@();$written=@{};$buffer=[byte[]]::new(1MB)
  try{
   foreach($cipher in $model.ciphers){
    $inputPath=Assert-L12BaiduLocal (Join-Path (Split-Path -Parent $source.path) $cipher.name) -Exists -Private
    $cipherStream=[IO.File]::Open($inputPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $whole=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try{
     if($cipherStream.Length -ne $cipher.bytes){throw 'L12_BAIDU_SOURCE_LENGTH'}
     $header=[byte[]]::new(22);$cipherStream.ReadExactly($header);$cipherStream.Position=0
     if([Text.Encoding]::ASCII.GetString($header) -cne "age-encryption.org/v1`n"){throw 'L12_BAIDU_CIPHER_HEADER'}
     $volumes=@();[long]$offset=0
     while($offset -lt $cipher.bytes){
      $length=[long][Math]::Min($VolumeBytes,[long]$cipher.bytes-$offset);$temporary=Join-Path $directory ('volume.partial-'+[Guid]::NewGuid().ToString('N'))
      $output=[IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
      $piece=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
      try{
       [long]$remaining=$length
       while($remaining -gt 0){if([DateTime]::UtcNow -ge $deadline){throw 'L12_BAIDU_DEADLINE'};$n=Get-L12BaiduChunkLength $remaining $buffer.Length;$cipherStream.ReadExactly($buffer,0,$n);$output.Write($buffer,0,$n);$piece.AppendData($buffer,0,$n);$whole.AppendData($buffer,0,$n);$remaining-=$n}
       $output.Flush($true);$digest=[Convert]::ToHexString($piece.GetHashAndReset()).ToLowerInvariant()
      }finally{$piece.Dispose();$output.Dispose()}
      $name=$digest+'.volume';$destination=Join-Path $directory $name
      if($written.ContainsKey($name)){if($written[$name] -ne $length){throw 'L12_BAIDU_VOLUME_ORDER'};[IO.File]::Delete($temporary)}
      else{[IO.File]::Move($temporary,$destination);$written[$name]=$length}
      $volumes+=@{index=$volumes.Count;offset=$offset;bytes=$length;sha256=$digest;name=$name};$offset+=$length
     }
     if([Convert]::ToHexString($whole.GetHashAndReset()).ToLowerInvariant() -cne $cipher.sha256){throw 'L12_BAIDU_SOURCE_SHA'}
     $ciphers+=@{name=$cipher.name;sha256=$cipher.sha256;bytes=$cipher.bytes;volumes=$volumes}
    }finally{$whole.Dispose();$cipherStream.Dispose()}
   }
  }finally{[Array]::Clear($buffer,0,$buffer.Length)}
  $envelope=[ordered]@{schema=1;format='l12-baidu-transport-volumes-v1';status='prepared-offline';volumeBytes=$VolumeBytes;
   originalManifest=@{fileName=[IO.Path]::GetFileName($source.path);sha256=$ExpectedManifestSha256;bytes=$source.raw.Length;contentBase64=[Convert]::ToBase64String($source.raw)};ciphers=$ciphers}
  $bytes=[Text.Encoding]::UTF8.GetBytes(($envelope|ConvertTo-Json -Depth 16 -Compress))
  if($bytes.Length -gt 256KB){throw 'L12_BAIDU_VOLUME_LIMIT'}
  Write-L12BaiduNewBytes (Join-Path $directory ([IO.Path]::GetFileName($source.path))) $source.raw
  $hash=Get-L12BaiduSha $bytes;$path=Join-Path $directory ($hash+'.volumes.json');$partial=$path+'.partial-'+[Guid]::NewGuid().ToString('N')
  Write-L12BaiduNewBytes $partial $bytes;[IO.File]::Move($partial,$path)
  [pscustomobject]@{status='transport-volumes-prepared-offline';manifestPath=$path;manifestSha256=$hash;originalManifestSha256=$ExpectedManifestSha256;
   volumeReferences=$count;uniqueVolumes=$written.Count;sourceDeleted=$false;uploadPerformed=$false;
   recoveryNotes='Retain this envelope AND its SHA256 AND the original V2 manifest SHA256 outside cloud. Restore with both trusted SHAs; use returned originalManifestPath and original SHA with unchanged L12CloudArchive VerifyReturned. Offline preparation requires a fresh directory; interrupted preparation is retained and never overwritten.'}
 }finally{if($lock){$lock.Dispose()};if($source){$source.stream.Dispose()}}
}
function Copy-L12BaiduVolumeCipher($Cipher,[string]$Directory,$Output,[DateTime]$Deadline){
 $whole=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256);$buffer=[byte[]]::new(1MB);[long]$total=0
 try{
  foreach($volume in $Cipher.volumes){
   $path=Assert-L12BaiduLocal (Join-Path $Directory $volume.name) -Exists -Private
   $volumeStream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
   $piece=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
   try{
    if($volumeStream.Length -ne $volume.bytes){throw 'L12_BAIDU_RETURNED_SHA'}
    if($total -eq 0){$header=[byte[]]::new(22);$volumeStream.ReadExactly($header);$volumeStream.Position=0;if([Text.Encoding]::ASCII.GetString($header) -cne "age-encryption.org/v1`n"){throw 'L12_BAIDU_CIPHER_HEADER'}}
    while($volumeStream.Position -lt $volumeStream.Length){if([DateTime]::UtcNow -ge $Deadline){throw 'L12_BAIDU_DEADLINE'};$n=Get-L12BaiduChunkLength ($volumeStream.Length-$volumeStream.Position) $buffer.Length;$volumeStream.ReadExactly($buffer,0,$n);$piece.AppendData($buffer,0,$n);$whole.AppendData($buffer,0,$n);if($Output){$Output.Write($buffer,0,$n)};$total+=$n}
    if([Convert]::ToHexString($piece.GetHashAndReset()).ToLowerInvariant() -cne $volume.sha256){throw 'L12_BAIDU_RETURNED_SHA'}
   }finally{$piece.Dispose();$volumeStream.Dispose()}
  }
  if($total -ne $Cipher.bytes -or [Convert]::ToHexString($whole.GetHashAndReset()).ToLowerInvariant() -cne $Cipher.sha256){throw 'L12_BAIDU_VOLUME_CIPHER_SHA'}
 }finally{$whole.Dispose();[Array]::Clear($buffer,0,$buffer.Length)}
}
function Complete-L12BaiduVolumeReturn($Trusted,[string]$Returned,[string]$Directory,[DateTime]$Deadline){
 if(!$Trusted.PSObject.Properties['volumeCiphers']){return $null}
 $statePath=Assert-L12BaiduLocal (Join-Path $Directory 'reassembly.dpapi') -Private
 if(Test-Path -LiteralPath $statePath){$state=Read-L12BaiduProtected $statePath}
 else{$state=[pscustomobject]@{schema=1;kind='transport-volume-reassembly';manifestSha256=$Trusted.manifestSha256;originalManifestSha256=$Trusted.originalManifestSha256;manifestPhase='planned';files=@($Trusted.volumeCiphers|ForEach-Object{[pscustomobject]@{name=$_.name;bytes=$_.bytes;sha256=$_.sha256;phase='planned';temporary=$null}})};Write-L12BaiduProtected $statePath $state}
 if(!(Test-L12BaiduVolumeInteger $state.schema) -or $state.schema -ne 1 -or $state.kind -cne 'transport-volume-reassembly' -or $state.manifestSha256 -cne $Trusted.manifestSha256 -or
    $state.originalManifestSha256 -cne $Trusted.originalManifestSha256 -or $state.files -isnot [array] -or @($state.files).Count -ne $Trusted.volumeCiphers.Count -or
    $state.manifestPhase -isnot [string] -or $state.manifestPhase -cnotin @('planned','writing','complete')){throw 'L12_BAIDU_VOLUME_STATE'}
 for($i=0;$i -lt $Trusted.volumeCiphers.Count;$i++){
  $cipher=$Trusted.volumeCiphers[$i];$entry=$state.files[$i]
  if($entry.name -cne $cipher.name -or !(Test-L12BaiduVolumeInteger $entry.bytes) -or $entry.bytes -ne $cipher.bytes -or $entry.sha256 -cne $cipher.sha256 -or
     $entry.phase -isnot [string] -or $entry.phase -cnotin @('planned','writing','complete')){throw 'L12_BAIDU_VOLUME_STATE'}
  $path=Assert-L12BaiduLocal (Join-Path $Returned $cipher.name) -Private
  if(Test-Path -LiteralPath $path){
   if($entry.phase -ceq 'planned'){throw 'L12_BAIDU_UNTRACKED_RETURNED'}
   if((Get-Item -LiteralPath $path).Length -ne $cipher.bytes -or (Get-FileHash -LiteralPath $path).Hash -ine $cipher.sha256){throw 'L12_BAIDU_RETURNED_SHA'}
   $entry.phase='complete';Write-L12BaiduProtected $statePath $state;continue
  }
  if($entry.phase -ceq 'complete'){throw 'L12_BAIDU_VOLUME_RESULT_MISSING'}
  $newPartial=$entry.phase -ceq 'planned'
  if($newPartial){$entry.phase='writing';$entry.temporary=$cipher.name+'.assembling-'+[Guid]::NewGuid().ToString('N');Write-L12BaiduProtected $statePath $state}
  if($entry.temporary -isnot [string] -or $entry.temporary -cnotmatch ('^'+[regex]::Escape($cipher.name)+'\.assembling-[a-f0-9]{32}$')){throw 'L12_BAIDU_VOLUME_STATE'}
  $temporary=Assert-L12BaiduLocal (Join-Path $Returned $entry.temporary) -Private
  $mode=if($newPartial){[IO.FileMode]::CreateNew}else{[IO.FileMode]::Create}
  $output=[IO.File]::Open($temporary,$mode,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{Copy-L12BaiduVolumeCipher $cipher $Returned $output $Deadline;$output.Flush($true)}finally{$output.Dispose()}
  [IO.File]::Move($temporary,$path);$entry.phase='complete';Write-L12BaiduProtected $statePath $state
 }
 $originalDirectory=New-L12BaiduPrivateDirectory (Join-Path $Directory 'trusted-original')
 $originalPath=Assert-L12BaiduLocal (Join-Path $originalDirectory $Trusted.originalManifestName) -Private
 if(@(Get-ChildItem -LiteralPath $originalDirectory -Force|Where-Object{$_.Name -cne $Trusted.originalManifestName}).Count){throw 'L12_BAIDU_UNTRACKED_RETURNED'}
 if(Test-Path -LiteralPath $originalPath){
  if($state.manifestPhase -ceq 'planned'){throw 'L12_BAIDU_UNTRACKED_RETURNED'}
  if((Get-Item -LiteralPath $originalPath).Length -ne $Trusted.originalManifestBytes.Length -or (Get-FileHash -LiteralPath $originalPath).Hash -ine $Trusted.originalManifestSha256){throw 'L12_BAIDU_MANIFEST_SHA'}
 }else{
  if($state.manifestPhase -ceq 'complete'){throw 'L12_BAIDU_VOLUME_RESULT_MISSING'}
  $state.manifestPhase='writing';Write-L12BaiduProtected $statePath $state
  Write-L12BaiduNewBytes $originalPath $Trusted.originalManifestBytes
 }
 $state.manifestPhase='complete';Write-L12BaiduProtected $statePath $state
 [pscustomobject]@{originalManifestPath=$originalPath;originalManifestSha256=$Trusted.originalManifestSha256;transportEnvelopeSha256=$Trusted.manifestSha256;transportFormat=$Trusted.transportFormat;
  reconstructedCiphers=@($Trusted.volumeCiphers|Select-Object name,bytes,sha256);recoveryNotes='Use originalManifestPath + originalManifestSha256 with unchanged L12CloudArchive VerifyReturned, and returnedDirectory as its independent returned ciphertext directory. Keep both original and envelope SHAs outside cloud. Transport verification is not plaintext/decryption verification.'}
}
function Assert-L12BaiduVolumeReturnCapacity($Trusted,[string]$Returned){
 if(!$Trusted.PSObject.Properties['volumeCiphers']){return}
 [long]$needed=64MB
 foreach($file in @($Trusted.definitions)+@($Trusted.volumeCiphers)){
  $path=Assert-L12BaiduLocal (Join-Path $Returned $file.name) -Private
  [long]$present=0
  if(Test-Path -LiteralPath $path){$item=Get-Item -LiteralPath $path;if($item.PSIsContainer){throw 'L12_BAIDU_UNTRACKED_RETURNED'};$present=[Math]::Min([long]$item.Length,[long]$file.bytes)}
  # This is only conservative capacity accounting, never proof authorizing an
  # existing result. Tracked download/reassembly checks still validate it later.
  $needed+=[long]$file.bytes-$present
 }
 if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($Returned))).AvailableFreeSpace -lt $needed){throw 'L12_BAIDU_VOLUME_CAPACITY'}
}
function Add-L12BaiduVolumeReceipt($Receipt,$VolumeReturn){
 if($null -eq $VolumeReturn){return}
 $Receipt['status']=if($Receipt.status -ceq 'cloud-cipher-restore-verified'){'cloud-volume-cipher-restore-verified'}else{'cloud-volume-cipher-roundtrip-verified'}
 foreach($property in $VolumeReturn.PSObject.Properties){$Receipt[$property.Name]=$property.Value}
}
function Write-L12BaiduVolumeReceipt([string]$Path,$Receipt){
 $path=Assert-L12BaiduLocal $Path -Private
 if(Test-Path -LiteralPath $path){
  $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  try{
   if($stream.Length -gt 256KB){throw 'L12_BAIDU_VOLUME_RECEIPT'}
   $bytes=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($bytes)
   $stored=ConvertFrom-L12BaiduVolumeJson $bytes
   Assert-L12BaiduVolumeKeys $stored @($Receipt.Keys)
   foreach($key in $Receipt.Keys){
    if($key -ceq 'completedUtc'){continue}
    if((ConvertTo-Json -InputObject $stored.$key -Depth 16 -Compress) -cne (ConvertTo-Json -InputObject $Receipt[$key] -Depth 16 -Compress)){throw 'L12_BAIDU_VOLUME_RECEIPT'}
   }
   $null=[DateTimeOffset]::Parse($stored.completedUtc)
   return $stored # A bound repeat preserves the first receipt byte-for-byte.
  }catch{throw 'L12_BAIDU_VOLUME_RECEIPT'}finally{$stream.Dispose()}
 }
 $bytes=[Text.Encoding]::UTF8.GetBytes(($Receipt|ConvertTo-Json -Depth 16))
 Write-L12BaiduNewBytes $path $bytes
 return [pscustomobject]$Receipt
}
