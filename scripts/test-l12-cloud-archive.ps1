[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AgeExecutable,
    [Parameter(Mandatory)][string]$AgeKeygenExecutable,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedAgeSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedKeygenSha256,
    [string]$EvidenceRoot='D:\GPT\Legion12\artifacts\server-health-20261029\cloud-storage\pipeline',
    [string]$EFixtureParent='E:\Legion12-ColdStorage'
)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'ops/windows/L12GeneratedStorage.ps1')
$EvidenceRoot=Assert-L12GeneratedPath -Path $EvidenceRoot -Purpose 'synthetic offline archive tests'
$EFixtureParent=Assert-L12GeneratedPath -Path $EFixtureParent -Purpose 'synthetic archive isolation'
if([IO.Path]::GetPathRoot($EvidenceRoot) -ine 'D:\' -or [IO.Path]::GetPathRoot($EFixtureParent) -ine 'E:\'){throw 'L12_TEST_EXPLICIT_D_E_REQUIRED'}
& (Join-Path $repo 'ops/windows/Initialize-L12BuildEnvironment.ps1') -CacheRoot 'D:\GPT\Legion12\cache\primary' -DependencyCacheRoot 'D:\GPT\Legion12\cache\primary' | Out-Null
foreach($binding in @(@($AgeExecutable,$ExpectedAgeSha256),@($AgeKeygenExecutable,$ExpectedKeygenSha256))){if((Get-FileHash -LiteralPath $binding[0] -Algorithm SHA256).Hash -ine $binding[1]){throw 'L12_TEST_TOOL_SHA_MISMATCH'}}
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
$id=[Guid]::NewGuid().ToString('N')
$fixture=Join-Path $EvidenceRoot ('synthetic-'+$id)
$isolated=Join-Path $EFixtureParent ('l12-cloud-synthetic-'+$id)
$null=New-Item -ItemType Directory -Path $fixture
$null=New-Item -ItemType Directory -Path $isolated
$script=Join-Path $repo 'ops/windows/L12CloudArchive.ps1'
$results=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[scriptblock]$Test){& $Test;$results.Add([pscustomobject]@{name=$Name;passed=$true})}
function Reject([scriptblock]$Test,[string]$Code){$seen=$false;try{& $Test|Out-Null}catch{if($_.Exception.Message -notlike "*$Code*"){throw};$seen=$true};if(-not $seen){throw 'Expected rejection absent'}}
function Write-Json($Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))}
function Invoke-Keygen([string[]]$Arguments){
    $info=[Diagnostics.ProcessStartInfo]::new($AgeKeygenExecutable);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    try{
        $null=$process.Start();$process.StandardInput.Close()
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(10000)){$process.Kill($true);throw 'SYNTHETIC_KEYGEN_TIMEOUT'}
        $text=$stdout.GetAwaiter().GetResult();$null=$stderr.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0){throw 'SYNTHETIC_KEYGEN_FAILED'}
        return $text
    }finally{$process.Dispose()}
}
function New-SyntheticKey([string]$Name){
    $path=Join-Path $fixture ($Name+'.identity')
    # Synthetic test identities only, never real account/backup keys. Tool output
    # is suppressed and neither private text nor identity path enters the report.
    $null=Invoke-Keygen -Arguments @('-o',$path)
    $public=(Invoke-Keygen -Arguments @('-y',$path)).Trim()
    if($public -cnotmatch '^age1[0-9a-z]{58}$'){throw 'SYNTHETIC_PUBLIC_KEY_INVALID'}
    return @{identity=$path;recipient=$public}
}
$status='failed';$errorType=$null
try{
    $first=New-SyntheticKey 'first';$wrong=New-SyntheticKey 'wrong'
    $source=Join-Path $fixture 'synthetic.tar'
    # This suite exercises archive byte transport, not tar/database restoration.
    [IO.File]::WriteAllBytes($source,[Text.Encoding]::UTF8.GetBytes(('SYNTHETIC-ARCHIVE-NO-PLAYER-DATA-'*80)))
    $proof=Join-Path $fixture 'completed-proof.json';Write-Json $proof @{status='completed';synthetic=$true}
    $sourceHash=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant();$sourceBytes=(Get-Item -LiteralPath $source).Length
    $sourceAcl=(Get-Acl -LiteralPath $source).Sddl;$proofAcl=(Get-Acl -LiteralPath $proof).Sddl
    $completionManifestPath=Join-Path $fixture 'input.json'
    Write-Json $completionManifestPath @{schema=1;status='completed';archive=@{sha256=$sourceHash;bytes=$sourceBytes};evidence=@(@{path=$proof;sha256=(Get-FileHash -LiteralPath $proof).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $proof).Length})}
    $base=@{AgeExecutable=$AgeExecutable;ExpectedAgeSha256=$ExpectedAgeSha256}
    $prepare=@{Action='Prepare';SourceArchive=$source;InputManifestPath=$completionManifestPath;PublicRecipient=$first.recipient;OutboxDirectory=(Join-Path $isolated 'outbox')}
    Check 'prepare real age, original source stable, no filename disclosure' {
        $script:prepared=& $script @base @prepare
        if($script:prepared.status-ne 'prepared-offline' -or (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()-cne $sourceHash){throw 'Prepare or original preservation failed'}
        $script:manifest=Get-Content -LiteralPath $script:prepared.manifestPath -Raw|ConvertFrom-Json
        $script:archiveItem=@($script:manifest.items|Where-Object {'archive' -in $_.roles})[0]
        $text=Get-Content -LiteralPath $script:prepared.manifestPath -Raw
        if($text.Contains($source) -or $text.Contains($proof) -or $text.Contains('identity') -or $script:manifest.cloudUploadPerformed){throw 'Manifest disclosure/upload claim'}
        if($script:manifest.items.Count -ne 3 -or @($script:manifest.items|Where-Object {$_.cipher.fileName -cnotmatch '^[0-9a-f]{64}\.age$'}).Count){throw 'Complete group/content-addressed names absent'}
    }
    $returned=Join-Path $fixture 'independent-return';$null=New-Item -ItemType Directory -Path $returned
    foreach($entry in $manifest.items){Copy-Item -LiteralPath (Join-Path $prepare.OutboxDirectory $entry.cipher.fileName) -Destination (Join-Path $returned $entry.cipher.fileName)}
    $verify=@{Action='VerifyReturned';ManifestPath=$prepared.manifestPath;ExpectedManifestSha256=$prepared.manifestSha256;ReturnedDirectory=$returned;IdentityPath=$first.identity;VerificationDirectory=(Join-Path $isolated 'verified')}
    Check 'independent synthetic roundtrip, plaintext strict SHA, no cloud provenance claim' {
        $verified=& $script @base @verify
        $receipt=Get-Content -LiteralPath $verified.receiptPath -Raw|ConvertFrom-Json
        if($receipt.status-ne 'content-verified-offline' -or $receipt.cloudProvenanceVerified -or $receipt.deletions-ne 0 -or -not $receipt.inputsUnchanged){throw 'Incorrect verification receipt'}
        $script:verifiedDirectory=$verified.directory;$script:restoredItems=$receipt.items
        $plainArchive=@($receipt.items|Where-Object {'archive' -in $_.roles})[0]
        if($receipt.items.Count-ne 3 -or (Get-FileHash -LiteralPath (Join-Path $verified.directory $plainArchive.plaintext.fileName)).Hash.ToLowerInvariant()-cne $sourceHash){throw 'Roundtrip plaintext differs'}
    }
    Check 'prepare refuses existing output' {Reject {& $script @base @prepare} 'L12_CLOUD_OUTPUT_EXISTS'}
    Check 'verify refuses existing output' {Reject {& $script @base @verify} 'L12_CLOUD_OUTPUT_EXISTS'}
    Check 'wrong key produces no success receipt; source preserved' {
        $v=$verify.Clone();$v.IdentityPath=$wrong.identity;$v.VerificationDirectory=Join-Path $isolated 'wrong-key'
        Reject {& $script @base @v} 'L12_CLOUD_AGE_FAILED'
        if(Test-Path -LiteralPath (Join-Path $v.VerificationDirectory 'verification.receipt.json')){throw 'Wrong key success receipt'}
        if((Get-Content -LiteralPath (Join-Path $v.VerificationDirectory 'failure.receipt.json') -Raw|ConvertFrom-Json).success){throw 'Failed attempt marked successful'}
    }
    Check 'tampered cipher rejected before decrypt/output' {
        $tampered=Join-Path $fixture 'tampered-return';$null=New-Item -ItemType Directory -Path $tampered
        $path=Join-Path $tampered $archiveItem.cipher.fileName
        $bytes=[IO.File]::ReadAllBytes((Join-Path $returned $archiveItem.cipher.fileName));$bytes[100]=$bytes[100] -bxor 1;[IO.File]::WriteAllBytes($path,$bytes)
        $v=$verify.Clone();$v.ReturnedDirectory=$tampered;$v.VerificationDirectory=Join-Path $isolated 'tampered'
        Reject {& $script @base @v} 'L12_CLOUD_CONTENT_MISMATCH'
        if(Test-Path -LiteralPath $v.VerificationDirectory){throw 'Tampered input created output'}
    }
    Check 'wrong trusted manifest hash rejected' {
        $v=$verify.Clone();$v.ExpectedManifestSha256=('0'*64);$v.VerificationDirectory=Join-Path $isolated 'bad-manifest'
        Reject {& $script @base @v} 'L12_CLOUD_TRUSTED_MANIFEST_SHA_MISMATCH'
    }
    Check 'outbox is not an independent download directory' {
        $v=$verify.Clone();$v.ReturnedDirectory=$prepare.OutboxDirectory;$v.VerificationDirectory=Join-Path $isolated 'same-origin'
        Reject {& $script @base @v} 'L12_CLOUD_INDEPENDENT_RETURN_REQUIRED'
    }
    Check 'raw completion manifest and proof restored byte-for-byte, not hashes alone' {
        $completion=@($restoredItems|Where-Object {'completion-manifest' -in $_.roles})[0]
        $proofEntry=@($restoredItems|Where-Object {'evidence' -in $_.roles})[0]
        if((Get-FileHash -LiteralPath (Join-Path $verifiedDirectory $completion.plaintext.fileName)).Hash -ine (Get-FileHash -LiteralPath $completionManifestPath).Hash -or (Get-FileHash -LiteralPath (Join-Path $verifiedDirectory $proofEntry.plaintext.fileName)).Hash -ine (Get-FileHash -LiteralPath $proof).Hash){throw 'Raw metadata bytes not recoverable'}
    }
    Check 'missing any proof cipher refuses entire group before output' {
        $missing=Join-Path $fixture 'missing-proof';$null=New-Item -ItemType Directory -Path $missing
        foreach($entry in @($manifest.items|Where-Object {'evidence' -notin $_.roles})){Copy-Item -LiteralPath (Join-Path $returned $entry.cipher.fileName) -Destination (Join-Path $missing $entry.cipher.fileName)}
        $v=$verify.Clone();$v.ReturnedDirectory=$missing;$v.VerificationDirectory=Join-Path $isolated 'missing-proof'
        Reject {& $script @base @v} 'L12_CLOUD_INPUT_FILE_REQUIRED'
        if(Test-Path -LiteralPath $v.VerificationDirectory){throw 'Incomplete group created output'}
    }
    Check 'new outbox and restored plaintext directories have only three private principals' {
        $expected=@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')
        foreach($path in @($prepare.OutboxDirectory,$verifiedDirectory)){
            $acl=Get-Acl -LiteralPath $path;$rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
            if(-not $acl.AreAccessRulesProtected -or $rules.Count-ne 3 -or @($rules|Where-Object {$_.IdentityReference.Value -notin $expected -or $_.IsInherited -or $_.AccessControlType-ne 'Allow' -or $_.FileSystemRights-ne [Security.AccessControl.FileSystemRights]::FullControl}).Count){throw 'Output ACL is not private'}
            foreach($file in @(Get-ChildItem -LiteralPath $path -File)){
                $fileRules=@((Get-Acl -LiteralPath $file.FullName).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
                if($fileRules.Count-ne 3 -or @($fileRules|Where-Object {$_.IdentityReference.Value -notin $expected}).Count){throw 'Output file expanded permissions'}
            }
        }
        if((Get-Acl -LiteralPath $source).Sddl -cne $sourceAcl -or (Get-Acl -LiteralPath $proof).Sddl -cne $proofAcl){throw 'Original source ACL changed'}
    }
    Check 'duplicate evidence bytes are encrypted only once' {
        $duplicate=Join-Path $fixture 'duplicate-input.json';$i=Get-Content -LiteralPath $completionManifestPath -Raw|ConvertFrom-Json;$i.evidence=@($i.evidence[0],$i.evidence[0]);Write-Json $duplicate $i
        $p=$prepare.Clone();$p.InputManifestPath=$duplicate;$p.OutboxDirectory=Join-Path $isolated 'dedup'
        $dedup=& $script @base @p
        if($dedup.itemCount-ne 3){throw 'Evidence not deduplicated'}
    }
    Check 'source SHA mismatch rejected before output' {
        $bad=Join-Path $fixture 'bad-input.json';$i=Get-Content -LiteralPath $completionManifestPath -Raw|ConvertFrom-Json;$i.archive.sha256='0'*64;Write-Json $bad $i
        $p=$prepare.Clone();$p.InputManifestPath=$bad;$p.OutboxDirectory=Join-Path $isolated 'bad-source'
        Reject {& $script @base @p} 'L12_CLOUD_CONTENT_MISMATCH'
        if(Test-Path -LiteralPath $p.OutboxDirectory){throw 'Source mismatch created output'}
    }
    Check 'incomplete source manifest rejected' {
        $bad=Join-Path $fixture 'incomplete-input.json';$i=Get-Content -LiteralPath $completionManifestPath -Raw|ConvertFrom-Json;$i.status='partial';Write-Json $bad $i
        $p=$prepare.Clone();$p.InputManifestPath=$bad;$p.OutboxDirectory=Join-Path $isolated 'incomplete'
        Reject {& $script @base @p} 'L12_CLOUD_COMPLETION_MANIFEST_REQUIRED'
    }
    Check 'relative and traversal output paths rejected' {
        foreach($path in @('relative-output',(Join-Path $isolated '..\escape'))){$p=$prepare.Clone();$p.OutboxDirectory=$path;Reject {& $script @base @p} 'L12_CLOUD_ABSOLUTE_LOCAL_PATH_REQUIRED'}
    }
    Check 'physical C output rejected without C writes' {
        $p=$prepare.Clone();$p.OutboxDirectory='C:\l12-cloud-must-not-create';Reject {& $script @base @p} 'L12_OUTPUT_ON_C'
    }
    Check 'D output rejected; encrypted and plaintext outputs require E' {
        $p=$prepare.Clone();$p.OutboxDirectory=Join-Path $fixture 'not-E';Reject {& $script @base @p} 'L12_CLOUD_OUTPUT_MUST_BE_E'
    }
    Check 'known and custom sync directories rejected, including identity' {
        $p=$prepare.Clone();$p.SourceArchive='F:\BaiduSyncdisk\never-read.tar';$p.OutboxDirectory=Join-Path $isolated 'sync-source';Reject {& $script @base @p} 'L12_CLOUD_SYNC_PATH_REJECTED'
        $p=$prepare.Clone();$p.OutboxDirectory=Join-Path $isolated 'OneDrive\never';Reject {& $script @base @p} 'L12_CLOUD_SYNC_PATH_REJECTED'
        $v=$verify.Clone();$v.IdentityPath='F:\BaiduSyncdisk\never-read.key';$v.VerificationDirectory=Join-Path $isolated 'sync-key';Reject {& $script @base @v} 'L12_CLOUD_SYNC_PATH_REJECTED'
        $p=$prepare.Clone();$p.OutboxDirectory=Join-Path $isolated 'custom-sync';Reject {& $script @base @p -CloudSyncRoots @($isolated)} 'L12_CLOUD_SYNC_PATH_REJECTED'
    }
    Check 'input and output ancestor junctions rejected' {
        $alias=Join-Path $fixture 'source-alias';$null=New-Item -ItemType Junction -Path $alias -Target $returned
        $v=$verify.Clone();$v.ReturnedDirectory=$alias;$v.VerificationDirectory=Join-Path $isolated 'input-link';Reject {& $script @base @v} 'L12_CLOUD_LINK_REJECTED'
        $eTarget=Join-Path $isolated 'plain-link-target';$null=New-Item -ItemType Directory -Path $eTarget
        $eAlias=Join-Path $isolated 'E-alias';$null=New-Item -ItemType Junction -Path $eAlias -Target $eTarget
        $p=$prepare.Clone();$p.OutboxDirectory=Join-Path $eAlias 'output-link';Reject {& $script @base @p} 'L12_CLOUD_LINK_REJECTED'
    }
    Check 'archive and proof bytes unchanged after all attempts' {
        if((Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()-cne $sourceHash -or (Get-Item -LiteralPath $source).Length-ne $sourceBytes){throw 'Source changed'}
        $original=Get-Content -LiteralPath $completionManifestPath -Raw|ConvertFrom-Json
        if((Get-FileHash -LiteralPath $proof).Hash.ToLowerInvariant()-cne $original.evidence[0].sha256){throw 'Proof changed'}
    }
    $status='completed'
}catch{$errorType=$_.Exception.GetType().Name;throw}
finally{
    $report=[ordered]@{schema=1;status=$status;errorType=$errorType;syntheticOnly=$true;cases=@($results.ToArray());caseCount=$results.Count;cloudUploadOrDownloadPerformed=$false;cloudProvenanceVerified=$false;realKeysOrDataUsed=$false;sourceSha256=(Get-FileHash -LiteralPath $script).Hash;testSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;ageSha256=$ExpectedAgeSha256.ToLowerInvariant();keygenSha256=$ExpectedKeygenSha256.ToLowerInvariant();syntheticFixture=$fixture;isolatedEFixture=$isolated;completedUtc=[DateTime]::UtcNow.ToString('o')}
    Write-Json (Join-Path $EvidenceRoot ($id+'.result.json')) $report
}
Write-Output "Offline synthetic archive checks passed: $($results.Count); no real cloud transfer or cleanup performed."
