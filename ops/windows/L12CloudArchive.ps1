<#
Offline manual-cloud handoff only. No upload, sync, deletion or key generation.
Prepare input JSON: {schema:1,status:"completed",archive:{sha256,bytes},
 evidence:[{path:<absolute local file>,sha256,bytes}]}. This is an explicit
 caller assertion of a completed archive, not a live-database backup producer.
VerifyReturned uses the ORIGINAL trusted manifest and its independently retained
 SHA. A matching copy proves content only, NOT that it was downloaded from cloud.
Known sync-folder names and caller-supplied CloudSyncRoots are rejected. This is
 not an inventory of every sync client: operators must declare custom sync roots.
Failure leaves isolated partial output and never emits a successful manifest.
#>
[CmdletBinding(DefaultParameterSetName='Prepare')]
param(
    [Parameter(Mandatory)][ValidateSet('Prepare','VerifyReturned')][string]$Action,
    [Parameter(Mandatory)][string]$AgeExecutable,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedAgeSha256,
    [Parameter(Mandatory,ParameterSetName='Prepare')][string]$SourceArchive,
    [Parameter(Mandatory,ParameterSetName='Prepare')][string]$InputManifestPath,
    [Parameter(Mandatory,ParameterSetName='Prepare')][ValidatePattern('^age1[0-9a-z]{58}$')][string]$PublicRecipient,
    [Parameter(Mandatory,ParameterSetName='Prepare')][string]$OutboxDirectory,
    [Parameter(Mandatory,ParameterSetName='VerifyReturned')][string]$ManifestPath,
    [Parameter(Mandatory,ParameterSetName='VerifyReturned')][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedManifestSha256,
    [Parameter(Mandatory,ParameterSetName='VerifyReturned')][string]$ReturnedDirectory,
    [Parameter(Mandatory,ParameterSetName='VerifyReturned')][string]$IdentityPath,
    [Parameter(Mandatory,ParameterSetName='VerifyReturned')][string]$VerificationDirectory,
    [string[]]$CloudSyncRoots=@('F:\BaiduSyncdisk'),
    [ValidateRange(1,3600)][int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
if($Action -cne $PSCmdlet.ParameterSetName){throw 'L12_CLOUD_PARAMETER_SET'}
$handles=[Collections.Generic.List[object]]::new()
$created=$null
function Assert-PlainLocal([string]$Path,[switch]$File,[switch]$Directory,[switch]$NewE) {
    if($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path.Substring(2).Contains(':') -or $Path -match '(^|[\\/])\.{1,2}([\\/]|$)' -or $Path -match '[*?"<>|]'){throw 'L12_CLOUD_ABSOLUTE_LOCAL_PATH_REQUIRED'}
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    if($full -match '(?i)(^|[\\/])(BaiduSyncdisk|BaiduNetdisk|百度网盘|OneDrive(?: - [^\\/]+)?|Dropbox|Google Drive|GoogleDrive|iCloudDrive|Nutstore|坚果云|SynologyDrive)([\\/]|$)'){throw 'L12_CLOUD_SYNC_PATH_REJECTED'}
    foreach($sync in $CloudSyncRoots){
        if($sync -notmatch '^[A-Za-z]:[\\/]' -or $sync.Substring(2).Contains(':') -or $sync -match '(^|[\\/])\.{1,2}([\\/]|$)'){throw 'L12_CLOUD_SYNC_ROOT_INVALID'}
        $syncFull=[IO.Path]::GetFullPath($sync).TrimEnd('\')
        if($full.Equals($syncFull,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($syncFull+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'L12_CLOUD_SYNC_PATH_REJECTED'}
    }
    $cursor=[IO.Path]::GetPathRoot($full)
    foreach($part in $full.Substring($cursor.Length).Split('\',[StringSplitOptions]::RemoveEmptyEntries)){
        $cursor=Join-Path $cursor $part
        $item=$null
        try{$item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop}catch [Management.Automation.ItemNotFoundException]{}
        if($null -ne $item -and (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or -not[string]::IsNullOrEmpty($item.LinkType))){throw 'L12_CLOUD_LINK_REJECTED'}
    }
    if($File -and -not(Test-Path -LiteralPath $full -PathType Leaf)){throw 'L12_CLOUD_INPUT_FILE_REQUIRED'}
    if($Directory -and -not(Test-Path -LiteralPath $full -PathType Container)){throw 'L12_CLOUD_INPUT_DIRECTORY_REQUIRED'}
    if($NewE){
        $physical=Assert-L12GeneratedPath -Path $full -Purpose 'offline cloud archive output'
        if([IO.Path]::GetPathRoot($physical) -ine 'E:\'){throw 'L12_CLOUD_OUTPUT_MUST_BE_E'}
        if(Test-Path -LiteralPath $full){throw 'L12_CLOUD_OUTPUT_EXISTS'}
        $null=Assert-PlainLocal -Path (Split-Path -Parent $full) -Directory
    }
    return $full
}
function In-Tree([string]$Path,[string]$Root){return $Path.Equals($Root,[StringComparison]::OrdinalIgnoreCase) -or $Path.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)}
function Lock-File([string]$Path){
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $record=[pscustomobject]@{path=$Path;stream=$stream;sha256=$null;bytes=$stream.Length}
    $handles.Add($record);$record.sha256=Hash-Stream $stream
    return $record
}
function Hash-Stream($Stream){
    $Stream.Position=0;$algorithm=[Security.Cryptography.SHA256]::Create()
    try{$hash=([Convert]::ToHexString($algorithm.ComputeHash($Stream))).ToLowerInvariant()}finally{$algorithm.Dispose();$Stream.Position=0}
    return $hash
}
function Assert-Binding($Binding){
    if($null -eq $Binding -or $Binding -is [array] -or $Binding.sha256 -isnot [string] -or $Binding.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $Binding.bytes -isnot [long] -and $Binding.bytes -isnot [int] -or $Binding.bytes -lt 0){throw 'L12_CLOUD_MANIFEST_BINDING_INVALID'}
}
function Check-Binding($Record,$Binding){Assert-Binding $Binding;if($Record.sha256 -cne $Binding.sha256 -or $Record.bytes -ne $Binding.bytes){throw 'L12_CLOUD_CONTENT_MISMATCH'}}
function Read-Json($Record){
    if($Record.bytes -gt 1048576){throw 'L12_CLOUD_MANIFEST_TOO_LARGE'}
    $Record.stream.Position=0;$reader=[IO.StreamReader]::new($Record.stream,[Text.UTF8Encoding]::new($false,$true),$true,1024,$true)
    try{$value=$reader.ReadToEnd()|ConvertFrom-Json -Depth 12}catch{throw 'L12_CLOUD_MANIFEST_INVALID'}finally{$reader.Dispose();$Record.stream.Position=0}
    if($null -eq $value -or $value -is [array] -or $value -isnot [pscustomobject]){throw 'L12_CLOUD_MANIFEST_INVALID'}
    return $value
}
function Assert-Stable {
    foreach($record in $handles){$null=Assert-PlainLocal -Path $record.path -File;if($record.stream.Length -ne $record.bytes -or (Hash-Stream $record.stream) -cne $record.sha256){throw 'L12_CLOUD_INPUT_CHANGED'}}
}
function Invoke-Age([string[]]$Arguments){
    $null=Assert-PlainLocal -Path $created -Directory
    $info=[Diagnostics.ProcessStartInfo]::new($AgeExecutable);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    try{
        $null=$process.Start();$process.StandardInput.Close()
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);throw 'L12_CLOUD_AGE_TIMEOUT'}
        $null=$stdout.GetAwaiter().GetResult();$null=$stderr.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0){throw 'L12_CLOUD_AGE_FAILED'}
    }finally{$process.Dispose()}
}
function Write-NewJson([string]$Path,$Value){
    $null=Assert-PlainLocal -Path (Split-Path -Parent $Path) -Directory
    $temp=$Path+'.partial-'+[Guid]::NewGuid().ToString('N')
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 10 -Compress))
    $stream=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    $null=Assert-PlainLocal -Path $temp -File
    [IO.File]::Move($temp,$Path,$false)
}
function New-PrivateDirectory([string]$Path){
    $null=Assert-PlainLocal -Path $Path -NewE
    $null=New-Item -ItemType Directory -Path $Path
    # Only this newly created directory: never modify its existing parent/source.
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')|Select-Object -Unique){
        $rule=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),[Security.AccessControl.FileSystemRights]::FullControl,([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $null=$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
    $actual=Get-Acl -LiteralPath $Path
    $rules=@($actual.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
    $expected=@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')
    if(-not $actual.AreAccessRulesProtected -or $rules.Count -ne 3 -or @($rules|Where-Object {$_.IsInherited -or $_.IdentityReference.Value -notin $expected -or $_.AccessControlType -ne 'Allow' -or $_.FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl -or [int]$_.InheritanceFlags -ne 3 -or [int]$_.PropagationFlags -ne 0}).Count){throw 'L12_CLOUD_PRIVATE_ACL_REQUIRED'}
}
try{
    $AgeExecutable=Assert-PlainLocal -Path $AgeExecutable -File
    $age=Lock-File $AgeExecutable
    if($age.sha256 -ine $ExpectedAgeSha256){throw 'L12_CLOUD_AGE_SHA_MISMATCH'}
    if($Action -eq 'Prepare'){
        $SourceArchive=Assert-PlainLocal -Path $SourceArchive -File
        if($SourceArchive -notmatch '(?i)\.(tar\.gz|tar\.xz|tar\.zst|tar|zip|7z)$'){throw 'L12_CLOUD_COMPLETED_ARCHIVE_REQUIRED'}
        $InputManifestPath=Assert-PlainLocal -Path $InputManifestPath -File
        $out=Assert-PlainLocal -Path $OutboxDirectory -NewE
        $inputRecord=Lock-File $InputManifestPath;$inputManifest=Read-Json $inputRecord
        if($inputManifest.schema -ne 1 -or $inputManifest.status -cne 'completed' -or $inputManifest.evidence -isnot [array] -or $inputManifest.evidence.Count -lt 1 -or $inputManifest.evidence.Count -gt 16){throw 'L12_CLOUD_COMPLETION_MANIFEST_REQUIRED'}
        $source=Lock-File $SourceArchive
        Check-Binding $source $inputManifest.archive
        if($source.bytes -le 0){throw 'L12_CLOUD_EMPTY_ARCHIVE'}
        $records=[Collections.Generic.List[object]]::new()
        $records.Add([pscustomobject]@{record=$source;roles=@('archive')})
        $records.Add([pscustomobject]@{record=$inputRecord;roles=@('completion-manifest')})
        foreach($binding in $inputManifest.evidence){
            $path=Assert-PlainLocal -Path $binding.path -File
            $record=Lock-File $path;Check-Binding $record $binding
            $records.Add([pscustomobject]@{record=$record;roles=@('evidence')})
        }
        $unique=[ordered]@{}
        foreach($entry in $records){
            $key=$entry.record.sha256+':'+$entry.record.bytes
            if($unique.Contains($key)){$unique[$key].roles=@($unique[$key].roles+$entry.roles|Select-Object -Unique)}else{$unique[$key]=$entry}
        }
        New-PrivateDirectory $out;$created=$out
        $items=@()
        foreach($entry in $unique.Values){
            $partial=Join-Path $out ('cipher.partial-'+[Guid]::NewGuid().ToString('N'))
            Invoke-Age -Arguments @('--encrypt','--recipient',$PublicRecipient,'--output',$partial,$entry.record.path)
            $null=Assert-PlainLocal -Path $partial -File
            $cipher=Lock-File $partial
            if($cipher.bytes -le 0){throw 'L12_CLOUD_EMPTY_CIPHER'}
            $cipherHash=$cipher.sha256;$cipherBytes=$cipher.bytes
            $cipher.stream.Dispose();$null=$handles.Remove($cipher)
            $null=Assert-PlainLocal -Path $out -Directory
            $cipherName=$cipherHash+'.age';[IO.File]::Move($partial,(Join-Path $out $cipherName),$false)
            $items+=@{roles=$entry.roles;plaintext=@{sha256=$entry.record.sha256;bytes=$entry.record.bytes};cipher=@{fileName=$cipherName;sha256=$cipherHash;bytes=$cipherBytes}}
        }
        Assert-Stable
        $manifest=[ordered]@{schema=2;format='l12-manual-cloud-archive-v2';status='prepared-offline';createdUtc=[DateTime]::UtcNow.ToString('o');archiveSha256=$source.sha256;recipient=$PublicRecipient;inputManifestSha256=$inputRecord.sha256;items=$items;ageExecutableSha256=$age.sha256;cloudUploadPerformed=$false}
        $manifestPath=Join-Path $out ($source.sha256+'.manifest.json')
        Write-NewJson $manifestPath $manifest
        [pscustomobject]@{status='prepared-offline';directory=$out;itemCount=$items.Count;manifestPath=$manifestPath;manifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant();uploadPerformed=$false}
    }else{
        $ManifestPath=Assert-PlainLocal -Path $ManifestPath -File
        $ReturnedDirectory=Assert-PlainLocal -Path $ReturnedDirectory -Directory
        $IdentityPath=Assert-PlainLocal -Path $IdentityPath -File
        $out=Assert-PlainLocal -Path $VerificationDirectory -NewE
        $originalOutbox=Split-Path -Parent $ManifestPath
        if((In-Tree $ReturnedDirectory $originalOutbox) -or (In-Tree $originalOutbox $ReturnedDirectory) -or (In-Tree $out $ReturnedDirectory) -or (In-Tree $out $originalOutbox)){throw 'L12_CLOUD_INDEPENDENT_RETURN_REQUIRED'}
        $manifestRecord=Lock-File $ManifestPath
        if($manifestRecord.sha256 -ine $ExpectedManifestSha256){throw 'L12_CLOUD_TRUSTED_MANIFEST_SHA_MISMATCH'}
        $manifest=Read-Json $manifestRecord
        if($manifest.schema -ne 2 -or $manifest.format -cne 'l12-manual-cloud-archive-v2' -or $manifest.status -cne 'prepared-offline' -or $manifest.recipient -cnotmatch '^age1[0-9a-z]{58}$' -or $manifest.items -isnot [array] -or $manifest.items.Count -lt 1 -or $manifest.items.Count -gt 18){throw 'L12_CLOUD_PREPARED_MANIFEST_REQUIRED'}
        $returnedItems=@();$archiveRoles=0;$completionRoles=0;$uniquePlain=@{}
        foreach($entry in $manifest.items){
            Assert-Binding $entry.plaintext;Assert-Binding $entry.cipher
            if($entry.cipher.fileName -cnotmatch '^[0-9a-f]{64}\.age$' -or $entry.cipher.fileName -cne ($entry.cipher.sha256+'.age')){throw 'L12_CLOUD_CIPHER_NAME_INVALID'}
            if($entry.roles -isnot [array] -or $entry.roles.Count -lt 1 -or @($entry.roles|Where-Object {$_ -notin @('archive','completion-manifest','evidence')}).Count){throw 'L12_CLOUD_ITEM_ROLES_INVALID'}
            $key=$entry.plaintext.sha256+':'+$entry.plaintext.bytes
            if($uniquePlain.ContainsKey($key)){throw 'L12_CLOUD_DUPLICATE_ITEM'};$uniquePlain[$key]=$true
            if('archive' -in $entry.roles){$archiveRoles++;if($entry.plaintext.sha256 -cne $manifest.archiveSha256){throw 'L12_CLOUD_ARCHIVE_BINDING_INVALID'}}
            if('completion-manifest' -in $entry.roles){$completionRoles++;if($entry.plaintext.sha256 -cne $manifest.inputManifestSha256){throw 'L12_CLOUD_COMPLETION_BINDING_INVALID'}}
            $returnedPath=Assert-PlainLocal -Path (Join-Path $ReturnedDirectory $entry.cipher.fileName) -File
            $returned=Lock-File $returnedPath;Check-Binding $returned $entry.cipher
            $returnedItems+=@{entry=$entry;record=$returned}
        }
        if($archiveRoles -ne 1 -or $completionRoles -ne 1){throw 'L12_CLOUD_REQUIRED_ITEM_MISSING'}
        $identity=Lock-File $IdentityPath
        New-PrivateDirectory $out;$created=$out
        $restored=@()
        foreach($item in $returnedItems){
            $partial=Join-Path $out ('plaintext.partial-'+[Guid]::NewGuid().ToString('N'))
            Invoke-Age -Arguments @('--decrypt','--identity',$IdentityPath,'--output',$partial,$item.record.path)
            $null=Assert-PlainLocal -Path $partial -File
            $plain=Lock-File $partial;Check-Binding $plain $item.entry.plaintext
            $plain.stream.Dispose();$null=$handles.Remove($plain)
            $plainName=$item.entry.plaintext.sha256+'.data'
            $null=Assert-PlainLocal -Path $out -Directory
            [IO.File]::Move($partial,(Join-Path $out $plainName),$false)
            $restored+=@{roles=$item.entry.roles;plaintext=@{fileName=$plainName;sha256=$item.entry.plaintext.sha256;bytes=$item.entry.plaintext.bytes};cipher=@{sha256=$item.record.sha256;bytes=$item.record.bytes}}
        }
        $completion=@($restored|Where-Object {'completion-manifest' -in $_.roles})[0]
        $restoredCompletion=Read-Json (Lock-File (Join-Path $out $completion.plaintext.fileName))
        if($restoredCompletion.schema -ne 1 -or $restoredCompletion.status -cne 'completed' -or $restoredCompletion.evidence -isnot [array] -or $restoredCompletion.evidence.Count -lt 1 -or $restoredCompletion.evidence.Count -gt 16){throw 'L12_CLOUD_RESTORED_COMPLETION_INVALID'}
        $archive=@($restored|Where-Object {'archive' -in $_.roles})[0]
        Check-Binding $archive.plaintext $restoredCompletion.archive
        foreach($binding in $restoredCompletion.evidence){
            Assert-Binding $binding
            if(@($restored|Where-Object {'evidence' -in $_.roles -and $_.plaintext.sha256 -ceq $binding.sha256 -and $_.plaintext.bytes -eq $binding.bytes}).Count -ne 1){throw 'L12_CLOUD_RESTORED_EVIDENCE_MISSING'}
        }
        Assert-Stable
        $receipt=[ordered]@{schema=2;status='content-verified-offline';completedUtc=[DateTime]::UtcNow.ToString('o');manifestSha256=$manifestRecord.sha256;items=$restored;recipient=$manifest.recipient;ageExecutableSha256=$age.sha256;inputsUnchanged=$true;cloudProvenanceVerified=$false;cloudProvenanceLimit='Independent directory and matching bytes do not establish a cloud download; retain separate visible-client transfer evidence.';uploads=0;deletions=0}
        Write-NewJson (Join-Path $out 'verification.receipt.json') $receipt
        [pscustomobject]@{status=$receipt.status;directory=$out;receiptPath=(Join-Path $out 'verification.receipt.json');cloudProvenanceVerified=$false}
    }
}catch{
    if($null -ne $created){
        try{$null=Assert-PlainLocal -Path $created -Directory;Write-NewJson (Join-Path $created 'failure.receipt.json') @{schema=1;status='failed-preserved';success=$false;partialOutputMayExist=$true;uploads=0;deletions=0}}catch{}
    }
    throw
}finally{foreach($record in $handles){$record.stream.Dispose()}}
