#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ControlPath,
    [Parameter(Mandatory)][ValidatePattern('^[A-F0-9]{64}$')][string]$ExpectedControlSha256,
    [Parameter(Mandatory)][ValidatePattern('^[A-F0-9]{64}$')][string]$ExpectedBindingSha256,
    [ValidateSet('Verify','PrivatePipe')][string]$Mode = 'Verify',
    [switch]$AllowPartial,
    [string]$PrivatePipeName,
    [string]$PrivateConsumerNonceSha256
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Utf8NoBom = [Text.UTF8Encoding]::new($false,$true)
function Fail-NormalLedger([string]$Code) { throw [InvalidOperationException]::new($Code) }
function Assert-NormalKeys($Value,[string[]]$Names) {
    if ($null -eq $Value -or $Value -is [Array] -or
        (@($Value.PSObject.Properties.Name | Sort-Object) -join '|') -cne (@($Names | Sort-Object) -join '|')) {
        Fail-NormalLedger 'NORMAL_LEDGER_EXACT_FIELDS_REQUIRED'
    }
}
function Read-NormalFrame([IO.Stream]$Stream,[int]$Maximum) {
    $prefix=[byte[]]::new(4);$offset=0
    while($offset -lt 4){$task=$Stream.ReadAsync($prefix,$offset,4-$offset);if(-not $task.Wait(10000)){Fail-NormalLedger 'NORMAL_PRIVATE_PIPE_TIMEOUT'};$count=$task.GetAwaiter().GetResult();if($count -lt 1){Fail-NormalLedger 'NORMAL_PRIVATE_PIPE_EOF'};$offset+=$count}
    $length=[BitConverter]::ToInt32($prefix,0)
    if($length -lt 2 -or $length -gt $Maximum){Fail-NormalLedger 'NORMAL_PRIVATE_FRAME_BOUND'}
    $bytes=[byte[]]::new($length);$offset=0
    try {
        while($offset -lt $length){$task=$Stream.ReadAsync($bytes,$offset,$length-$offset);if(-not $task.Wait(10000)){Fail-NormalLedger 'NORMAL_PRIVATE_PIPE_TIMEOUT'};$count=$task.GetAwaiter().GetResult();if($count -lt 1){Fail-NormalLedger 'NORMAL_PRIVATE_PIPE_EOF'};$offset+=$count}
        $script:Utf8NoBom.GetString($bytes) | ConvertFrom-Json -Depth 8 -DateKind String
    } finally {[Array]::Clear($bytes,0,$bytes.Length)}
}
function Write-NormalFrame([IO.Stream]$Stream,$Value) {
    $bytes=$script:Utf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth 20 -Compress))
    try {
        if($bytes.Length -gt 131072){Fail-NormalLedger 'NORMAL_PRIVATE_FRAME_BOUND'}
        $prefix=[BitConverter]::GetBytes([int]$bytes.Length)
        $Stream.Write($prefix,0,4);$Stream.Write($bytes,0,$bytes.Length);$Stream.Flush()
    } finally {[Array]::Clear($bytes,0,$bytes.Length)}
}

$privateRecords=[Collections.Generic.List[object]]::new()
try {
    if(-not $IsWindows){Fail-NormalLedger 'NORMAL_DPAPI_WINDOWS_REQUIRED'}
    . (Join-Path $PSScriptRoot '../windows/L12GeneratedStorage.ps1')
    $evidenceRoot=Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/artifacts/representative-load-current-20261022' -Purpose 'normal ledger evidence lease'
    $cacheRoot=Assert-L12GeneratedPath -Path 'D:/GPT/Legion12/cache/primary/representative-load-current-20261022' -Purpose 'normal ledger cache lease'
    $controlFull=[IO.Path]::GetFullPath($ControlPath)
    $physicalControl=Assert-L12GeneratedPath -Path $controlFull -Purpose 'normal ledger public control'
    if(-not $physicalControl.StartsWith($evidenceRoot.TrimEnd([char]92)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        Fail-NormalLedger 'NORMAL_LEDGER_CONTROL_OUTSIDE_LEASE'
    }
    $controlItem=Get-Item -LiteralPath $controlFull -Force
    if(-not ($controlItem -is [IO.FileInfo]) -or ($controlItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $controlItem.Length -gt 16384 -or $controlItem.Length -lt 1){Fail-NormalLedger 'NORMAL_LEDGER_CONTROL_REJECTED'}
    if((Get-FileHash -LiteralPath $controlFull -Algorithm SHA256).Hash -cne $ExpectedControlSha256){Fail-NormalLedger 'NORMAL_LEDGER_CONTROL_SHA_CHANGED'}
    $control=Get-Content -LiteralPath $controlFull -Raw | ConvertFrom-Json -Depth 12 -DateKind String
    Assert-NormalKeys $control @('schema','kind','runId','baseUrl','test','formal','bindingSha256','registrationWindow','groups')
    if($control.schema -ne 1 -or $control.kind -cne 'normal-testrun-account-ledgers-v1' -or
        $control.runId -cnotmatch '^[a-f0-9]{32}$' -or $control.baseUrl -cne 'https://legion-12.com/testrun/' -or
        $control.bindingSha256 -cne $ExpectedBindingSha256 -or @($control.groups).Count -ne 2){Fail-NormalLedger 'NORMAL_LEDGER_BINDING_REJECTED'}
    foreach($identity in @($control.test,$control.formal)){
        Assert-NormalKeys $identity @('pid','commit','dllSha256')
        if(($identity.pid -isnot [long] -and $identity.pid -isnot [int]) -or $identity.pid -le 0 -or $identity.pid -gt 9007199254740991 -or
            $identity.commit -cnotmatch '^[a-f0-9]{40}$' -or $identity.dllSha256 -cnotmatch '^[A-F0-9]{64}$'){Fail-NormalLedger 'NORMAL_LEDGER_IDENTITY_REJECTED'}
    }
    if($control.test.pid -eq $control.formal.pid){Fail-NormalLedger 'NORMAL_LEDGER_DISTINCT_PID_REQUIRED'}
    Assert-NormalKeys $control.registrationWindow @('openedUtc','notAfterUtc')
    $opened=[DateTimeOffset]::Parse($control.registrationWindow.openedUtc,[Globalization.CultureInfo]::InvariantCulture)
    $notAfter=[DateTimeOffset]::Parse($control.registrationWindow.notAfterUtc,[Globalization.CultureInfo]::InvariantCulture)
    if($notAfter -le $opened -or ($notAfter-$opened).TotalSeconds -gt 900){Fail-NormalLedger 'NORMAL_LEDGER_WINDOW_REJECTED'}

    # Read only four immutable functions, never dot-source the historical runner.
    $source='C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/performance-causal-isolation-20261016/terminal-bounds-v2/cohort30-terminal-v2/ledger-r5/test-account-ledger-real-r4.ps1'
    $readerSha='C991435C5E18FC8A26D9F57264E25BC38F4D1B4D4CC79DAF395D1C0EFCC6BECE'
    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne $readerSha){Fail-NormalLedger 'NORMAL_FROZEN_READER_CHANGED'}
    $tokens=$null;$parseErrors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$parseErrors)
    if(@($parseErrors).Count){Fail-NormalLedger 'NORMAL_FROZEN_READER_PARSE_FAILED'}
    foreach($name in @('Throw-L12RealTest','Assert-L12RegularFile','Test-L12ContainsBytes','Read-L12ProtectedSnapshot')){
        $functions=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$true) | Where-Object Name -CEQ $name)
        if($functions.Count -ne 1){Fail-NormalLedger 'NORMAL_FROZEN_READER_FUNCTION_CHANGED'}
        . ([scriptblock]::Create($functions[0].Extent.Text))
    }
    $groups=[Collections.Generic.List[object]]::new();$parentSeen=$null;$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($index in 0..1){
        $group=$control.groups[$index]
        Assert-NormalKeys $group @('index','runId','maxSlots','ledgerPath')
        $expectedRun='normal-'+$control.runId+'-'+$index
        $maximum=@(60,9)[$index]
        $leaf=@('players60.dpapi','outer9.dpapi')[$index]
        $full=[IO.Path]::GetFullPath([string]$group.ledgerPath)
        $parent=Split-Path -Parent $full
        $physical=Assert-L12GeneratedPath -Path $full -Purpose 'normal owned ledger ciphertext'
        if($group.index -ne $index -or $group.runId -cne $expectedRun -or $group.maxSlots -ne $maximum -or
            [IO.Path]::GetFileName($full) -cne $leaf -or [IO.Path]::GetFileName($parent) -cne $control.runId -or
            -not $physical.StartsWith($cacheRoot.TrimEnd([char]92)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            ($null -ne $parentSeen -and -not [string]::Equals($parent,$parentSeen,[StringComparison]::OrdinalIgnoreCase))){Fail-NormalLedger 'NORMAL_LEDGER_GROUP_REJECTED'}
        $parentSeen=$parent
        if(-not (Test-Path -LiteralPath $full -PathType Leaf)){
            if(-not $AllowPartial){Fail-NormalLedger 'NORMAL_LEDGER_MISSING'}
            $groups.Add([ordered]@{index=$index;runId=$expectedRun;missing=$true;count=0;sequence=0;confirmed=0;intent=0;cipherSha256=$null;wrongRunRejected=$false})
            continue
        }
        $read=Read-L12ProtectedSnapshot $full $expectedRun @()
        $snapshot=$read.Snapshot
        Assert-NormalKeys $snapshot @('schema','runId','sequence','count','accounts')
        if($snapshot.schema -ne 1 -or $snapshot.runId -cne $expectedRun -or $snapshot.count -ne @($snapshot.accounts).Count -or
            $snapshot.count -lt 1 -or $snapshot.count -gt $maximum){Fail-NormalLedger 'NORMAL_LEDGER_CONTENT_REJECTED'}
        $slots=[Collections.Generic.HashSet[int]]::new();$confirmed=0;$intent=0
        foreach($account in @($snapshot.accounts)){
            Assert-NormalKeys $account @('slot','stage','username','password','requestedAtUtc','preflightAbsent','requestFingerprint','id','createdAtUtc')
            if(($account.slot -isnot [int] -and $account.slot -isnot [long]) -or $account.slot -lt 0 -or $account.slot -ge $maximum -or
                -not $slots.Add([int]$account.slot) -or -not $names.Add([string]$account.username) -or
                $account.username -cnotmatch '^[A-Za-z0-9_]{2,11}$' -or $account.password -isnot [string] -or
                $account.password.Length -lt 16 -or $account.password.Length -gt 128 -or
                $account.preflightAbsent -isnot [bool] -or $account.preflightAbsent -ne $true -or $account.requestFingerprint -cnotmatch '^[a-f0-9]{64}$'){
                Fail-NormalLedger 'NORMAL_LEDGER_ACCOUNT_REJECTED'
            }
            $requested=[DateTimeOffset]::Parse($account.requestedAtUtc,[Globalization.CultureInfo]::InvariantCulture)
            if($requested -lt $opened -or $requested -gt $notAfter){Fail-NormalLedger 'NORMAL_LEDGER_REQUEST_WINDOW_REJECTED'}
            if($account.stage -ceq 'intent'){
                if($null -ne $account.id -or $null -ne $account.createdAtUtc){Fail-NormalLedger 'NORMAL_LEDGER_INTENT_REJECTED'};$intent++
            }elseif($account.stage -ceq 'confirmed'){
                if($account.id -isnot [string] -or $account.id.Length -lt 8 -or $account.id.Length -gt 128 -or $account.id -match '\s' -or $account.createdAtUtc -isnot [string]){Fail-NormalLedger 'NORMAL_LEDGER_CONFIRMATION_REJECTED'}
                $created=[DateTimeOffset]::Parse($account.createdAtUtc,[Globalization.CultureInfo]::InvariantCulture)
                if($created -lt $opened -or $created -gt $notAfter){Fail-NormalLedger 'NORMAL_LEDGER_CREATION_WINDOW_REJECTED'};$confirmed++
            }else{Fail-NormalLedger 'NORMAL_LEDGER_STAGE_REJECTED'}
            $privateRecords.Add([ordered]@{ledgerIndex=$index;slot=[int]$account.slot;stage=[string]$account.stage;username=[string]$account.username;
                password=[string]$account.password;requestedAtUtc=[string]$account.requestedAtUtc;preflightAbsent=$true;requestFingerprint=[string]$account.requestFingerprint;
                id=$account.id;createdAt=$account.createdAtUtc})
        }
        if($snapshot.sequence -ne ($snapshot.count+$confirmed)){Fail-NormalLedger 'NORMAL_LEDGER_SEQUENCE_REJECTED'}
        $wrongRejected=$false
        try{$null=Read-L12ProtectedSnapshot $full ($expectedRun+'-wrong') @()}catch{$wrongRejected=$true}
        if(-not $wrongRejected){Fail-NormalLedger 'NORMAL_LEDGER_WRONG_RUN_ACCEPTED'}
        if(-not $AllowPartial -and ($snapshot.count -ne $maximum -or $intent -ne 0)){Fail-NormalLedger 'NORMAL_LEDGER_INCOMPLETE'}
        $groups.Add([ordered]@{index=$index;runId=$expectedRun;missing=$false;count=[int]$snapshot.count;sequence=[long]$snapshot.sequence;
            confirmed=$confirmed;intent=$intent;cipherSha256=[string]$read.CipherSha256;wrongRunRejected=$true})
        $snapshot=$null;$read=$null
    }
    $complete=@($groups | Where-Object {$_.missing -or $_.intent -ne 0 -or $_.count -ne @(60,9)[$_.index]}).Count -eq 0
    if($Mode -ceq 'PrivatePipe'){
        if($PrivatePipeName -cnotmatch '^[A-Za-z0-9_-]{16,96}$' -or $PrivateConsumerNonceSha256 -cnotmatch '^[A-F0-9]{64}$'){Fail-NormalLedger 'NORMAL_PRIVATE_PIPE_ARGUMENT_REJECTED'}
        $options=[IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
        $pipe=[IO.Pipes.NamedPipeServerStream]::new($PrivatePipeName,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,$options,4096,4096)
        try {
            $connecting=$pipe.WaitForConnectionAsync();if(-not $connecting.Wait(10000)){Fail-NormalLedger 'NORMAL_PRIVATE_PIPE_TIMEOUT'};$connecting.GetAwaiter().GetResult()
            $hello=Read-NormalFrame $pipe 1024
            Assert-NormalKeys $hello @('schema','kind','runId','controlSha256','nonce')
            if($hello.schema -ne 1 -or $hello.kind -cne 'normal-ledger-private-consumer' -or $hello.runId -cne $control.runId -or
                $hello.controlSha256 -cne $ExpectedControlSha256 -or $hello.nonce -cnotmatch '^[a-f0-9]{64}$' -or
                [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($script:Utf8NoBom.GetBytes($hello.nonce))) -cne $PrivateConsumerNonceSha256){Fail-NormalLedger 'NORMAL_PRIVATE_CONSUMER_REJECTED'}
            Write-NormalFrame $pipe ([ordered]@{schema=1;kind='normal-ledger-private-envelope';runId=$control.runId;controlSha256=$ExpectedControlSha256;
                bindingSha256=$ExpectedBindingSha256;registrationWindow=$control.registrationWindow;accounts=$privateRecords.ToArray();ledgerBindings=$groups.ToArray()})
            $ack=Read-NormalFrame $pipe 1024
            Assert-NormalKeys $ack @('schema','consumed','controlSha256')
            if($ack.schema -ne 1 -or $ack.consumed -isnot [bool] -or $ack.consumed -ne $true -or $ack.controlSha256 -cne $ExpectedControlSha256){Fail-NormalLedger 'NORMAL_PRIVATE_CONSUMER_ACK_REJECTED'}
        } finally {$pipe.Dispose()}
    }
    [ordered]@{schema=1;kind='normal-testrun-ledger-cold-read';runId=$control.runId;controlSha256=$ExpectedControlSha256;
        bindingSha256=$ExpectedBindingSha256;complete=$complete;allowPartial=[bool]$AllowPartial;groups=$groups.ToArray();readerSourceSha256=$readerSha;
        credentialsEmittedToStdout=$false;privatePipeUsed=($Mode -ceq 'PrivatePipe');privateConsumerCompleted=($Mode -ceq 'PrivatePipe');networkCalls=0;formalWrites=0} | ConvertTo-Json -Depth 8 -Compress
} catch {
    $code=$_.Exception.Message
    if($code -cnotmatch '^(?:NORMAL_[A-Z0-9_]+|SNAPSHOT_REJECTED)$'){$code='NORMAL_LEDGER_READ_FAILED'}
    [Console]::Error.WriteLine(([ordered]@{schema=1;status='failed';code=$code;credentialsEmittedToStdout=$false;networkCalls=0}|ConvertTo-Json -Compress))
    exit 2
} finally {
    foreach($record in $privateRecords){$record.username='';$record.password='';$record.id='';$record.createdAt=''}
    $privateRecords.Clear();$control=$null
}
