[CmdletBinding()]
param(
    [switch]$LibraryOnly,
    [switch]$Apply,
    [string]$RunId,
    [string]$PipeName,
    [string]$LedgerPath,
    [ValidateRange(1, 60)] [int]$MaxSlots = 60,
    [ValidateRange(1000, 60000)] [int]$ConnectionTimeoutMs = 15000,
    [ValidateRange(1000, 60000)] [int]$FrameTimeoutMs = 10000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:LedgerUtf8 = [Text.UTF8Encoding]::new($false, $true)
$script:LedgerMaxFrameBytes = 16384
$script:LedgerMaxPlaintextBytes = 65536
$script:LedgerSafeErrors = [Collections.Generic.HashSet[string]]::new(
    [string[]]@(
        'APPLY_REQUIRED', 'RUN_ID_REJECTED', 'PIPE_NAME_REJECTED',
        'LEDGER_PATH_REJECTED', 'LEDGER_PARENT_REJECTED', 'LEDGER_ALREADY_EXISTS',
        'PIPE_CONNECT_TIMEOUT', 'PIPE_READ_TIMEOUT', 'PIPE_FRAME_REJECTED',
        'EVENT_SHAPE_REJECTED', 'EVENT_RUN_REJECTED', 'EVENT_SEQUENCE_REJECTED',
        'EVENT_SLOT_REJECTED', 'EVENT_TRANSITION_REJECTED', 'EVENT_VALUE_REJECTED',
        'SNAPSHOT_TOO_LARGE', 'LEDGER_TARGET_CHANGED', 'LEDGER_PERSIST_FAILED'
    ),
    [StringComparer]::Ordinal
)

function Throw-L12LedgerError([string]$Code) {
    throw [InvalidOperationException]::new($Code)
}

function Get-L12LedgerSafeErrorCode([Exception]$Exception) {
    if ($null -ne $Exception -and $script:LedgerSafeErrors.Contains($Exception.Message)) {
        return $Exception.Message
    }
    'LEDGER_PERSIST_FAILED'
}

function Get-L12LedgerSha256Hex([byte[]]$Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Test-L12LedgerHexSha([object]$Value) {
    $Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$'
}

function Assert-L12LedgerExactProperties([object]$Value, [string[]]$Expected) {
    if ($null -eq $Value) { Throw-L12LedgerError 'EVENT_SHAPE_REJECTED' }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    if ($actual.Count -ne $wanted.Count) { Throw-L12LedgerError 'EVENT_SHAPE_REJECTED' }
    for ($index = 0; $index -lt $wanted.Count; $index += 1) {
        if ($actual[$index] -cne $wanted[$index]) { Throw-L12LedgerError 'EVENT_SHAPE_REJECTED' }
    }
}

function Test-L12LedgerTimestamp([object]$Value) {
    if ($Value -isnot [string] -or $Value.Length -lt 20 -or $Value.Length -gt 40) { return $false }
    $parsed = [DateTimeOffset]::MinValue
    [DateTimeOffset]::TryParse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsed
    )
}

function New-L12LedgerState([string]$ExpectedRunId, [int]$ExpectedMaxSlots) {
    if ($ExpectedRunId -notmatch '^[A-Za-z0-9_-]{8,96}$') { Throw-L12LedgerError 'RUN_ID_REJECTED' }
    if ($ExpectedMaxSlots -lt 1 -or $ExpectedMaxSlots -gt 60) { Throw-L12LedgerError 'EVENT_SLOT_REJECTED' }
    [pscustomobject]@{
        RunId = $ExpectedRunId
        MaxSlots = $ExpectedMaxSlots
        Sequence = 0
        Accounts = @()
        CipherSha256 = $null
    }
}

function Copy-L12LedgerAccounts([object[]]$Accounts) {
    @($Accounts | ForEach-Object {
        [ordered]@{
            slot = [int]$_.slot
            stage = [string]$_.stage
            username = [string]$_.username
            password = [string]$_.password
            requestedAtUtc = [string]$_.requestedAtUtc
            preflightAbsent = [bool]$_.preflightAbsent
            requestFingerprint = [string]$_.requestFingerprint
            id = if ($null -eq $_.id) { $null } else { [string]$_.id }
            createdAtUtc = if ($null -eq $_.createdAtUtc) { $null } else { [string]$_.createdAtUtc }
        }
    })
}

function Get-L12LedgerNextSnapshot([object]$State, [object]$Event) {
    if ($null -eq $Event.type -or $Event.type -notin @('intent', 'confirmed')) {
        Throw-L12LedgerError 'EVENT_SHAPE_REJECTED'
    }
    if ($Event.type -ceq 'intent') {
        Assert-L12LedgerExactProperties $Event @(
            'type', 'runId', 'sequence', 'slot', 'username', 'password',
            'requestedAtUtc', 'preflightAbsent', 'requestFingerprint'
        )
    }
    else {
        Assert-L12LedgerExactProperties $Event @(
            'type', 'runId', 'sequence', 'slot', 'id', 'createdAtUtc'
        )
    }

    if ($Event.runId -isnot [string] -or $Event.runId -cne $State.RunId) {
        Throw-L12LedgerError 'EVENT_RUN_REJECTED'
    }
    if ($Event.sequence -isnot [long] -and $Event.sequence -isnot [int]) {
        Throw-L12LedgerError 'EVENT_SEQUENCE_REJECTED'
    }
    $sequence = [long]$Event.sequence
    if ($sequence -ne ([long]$State.Sequence + 1) -or $sequence -gt (2 * $State.MaxSlots)) {
        Throw-L12LedgerError 'EVENT_SEQUENCE_REJECTED'
    }
    if (($Event.slot -isnot [long] -and $Event.slot -isnot [int]) -or
        [long]$Event.slot -lt 0 -or [long]$Event.slot -ge $State.MaxSlots) {
        Throw-L12LedgerError 'EVENT_SLOT_REJECTED'
    }
    $slot = [int]$Event.slot
    $accounts = @(Copy-L12LedgerAccounts $State.Accounts)
    $existingIndex = -1
    for ($index = 0; $index -lt $accounts.Count; $index += 1) {
        if ([int]$accounts[$index].slot -eq $slot) { $existingIndex = $index; break }
    }

    if ($Event.type -ceq 'intent') {
        if ($existingIndex -ge 0 -or $accounts.Count -ge $State.MaxSlots) {
            Throw-L12LedgerError 'EVENT_TRANSITION_REJECTED'
        }
        if ($Event.username -isnot [string] -or $Event.username -notmatch '^[A-Za-z0-9_]{2,11}$' -or
            $Event.password -isnot [string] -or $Event.password.Length -lt 16 -or $Event.password.Length -gt 128 -or
            $Event.preflightAbsent -isnot [bool] -or -not $Event.preflightAbsent -or
            -not (Test-L12LedgerTimestamp $Event.requestedAtUtc) -or
            -not (Test-L12LedgerHexSha $Event.requestFingerprint)) {
            Throw-L12LedgerError 'EVENT_VALUE_REJECTED'
        }
        $accounts += [ordered]@{
            slot = $slot
            stage = 'intent'
            username = $Event.username
            password = $Event.password
            requestedAtUtc = $Event.requestedAtUtc
            preflightAbsent = $true
            requestFingerprint = $Event.requestFingerprint
            id = $null
            createdAtUtc = $null
        }
    }
    else {
        if ($existingIndex -lt 0 -or $accounts[$existingIndex].stage -cne 'intent') {
            Throw-L12LedgerError 'EVENT_TRANSITION_REJECTED'
        }
        if ($Event.id -isnot [string] -or $Event.id.Length -lt 8 -or $Event.id.Length -gt 128 -or
            $Event.id -match '\s' -or -not (Test-L12LedgerTimestamp $Event.createdAtUtc)) {
            Throw-L12LedgerError 'EVENT_VALUE_REJECTED'
        }
        $previous = $accounts[$existingIndex]
        $accounts[$existingIndex] = [ordered]@{
            slot = $slot
            stage = 'confirmed'
            username = $previous.username
            password = $previous.password
            requestedAtUtc = $previous.requestedAtUtc
            preflightAbsent = $true
            requestFingerprint = $previous.requestFingerprint
            id = $Event.id
            createdAtUtc = $Event.createdAtUtc
        }
    }

    $accounts = @($accounts | Sort-Object { [int]$_.slot })
    [ordered]@{
        schema = 1
        runId = $State.RunId
        sequence = $sequence
        count = $accounts.Count
        accounts = $accounts
    }
}

function Invoke-L12LedgerEvent(
    [object]$State,
    [object]$Event,
    [scriptblock]$Protect,
    [scriptblock]$WriteSnapshot
) {
    $snapshot = Get-L12LedgerNextSnapshot $State $Event
    $plain = $script:LedgerUtf8.GetBytes(($snapshot | ConvertTo-Json -Depth 16 -Compress))
    $cipher = $null
    try {
        if ($plain.Length -gt $script:LedgerMaxPlaintextBytes) { Throw-L12LedgerError 'SNAPSHOT_TOO_LARGE' }
        $cipher = [byte[]](& $Protect $plain $State.RunId)
        if ($null -eq $cipher -or $cipher.Length -lt 1) { Throw-L12LedgerError 'LEDGER_PERSIST_FAILED' }
        $cipherSha = [string](& $WriteSnapshot $cipher $State.CipherSha256)
        if (-not (Test-L12LedgerHexSha $cipherSha)) { Throw-L12LedgerError 'LEDGER_PERSIST_FAILED' }

        # State becomes visible, and therefore ACK-able, only after the durable writer returns.
        $State.Sequence = [long]$snapshot.sequence
        $State.Accounts = @($snapshot.accounts)
        $State.CipherSha256 = $cipherSha
        [ordered]@{
            ok = $true
            seq = [long]$State.Sequence
            count = [int]$State.Accounts.Count
            cipherSha256 = $State.CipherSha256
        }
    }
    finally {
        [Array]::Clear($plain, 0, $plain.Length)
        if ($null -ne $cipher) { [Array]::Clear($cipher, 0, $cipher.Length) }
    }
}

function Protect-L12LedgerBytes([byte[]]$Plaintext, [string]$ExpectedRunId) {
    $entropyInput = $script:LedgerUtf8.GetBytes("l12-cohort-ledger-r5|$ExpectedRunId")
    $entropy = $null
    try {
        $entropy = [Security.Cryptography.SHA256]::HashData($entropyInput)
        [Security.Cryptography.ProtectedData]::Protect(
            $Plaintext,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser
        )
    }
    finally {
        [Array]::Clear($entropyInput, 0, $entropyInput.Length)
        if ($null -ne $entropy) { [Array]::Clear($entropy, 0, $entropy.Length) }
    }
}

function Assert-L12LedgerRegularFile([string]$Path, [int64]$MaxBytes) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not ($item -is [IO.FileInfo]) -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        $item.Length -lt 1 -or $item.Length -gt $MaxBytes) {
        Throw-L12LedgerError 'LEDGER_TARGET_CHANGED'
    }
    $item
}

function Write-L12LedgerAtomicSnapshot(
    [string]$TargetPath,
    [byte[]]$Ciphertext,
    [AllowNull()] [string]$KnownCipherSha
) {
    $fullTarget = [IO.Path]::GetFullPath($TargetPath)
    $parent = Split-Path -Parent $fullTarget
    if ([string]::IsNullOrWhiteSpace($parent) -or -not (Test-Path -LiteralPath $parent -PathType Container)) {
        Throw-L12LedgerError 'LEDGER_PARENT_REJECTED'
    }
    $parentItem = Get-Item -LiteralPath $parent -Force
    if ($parentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Throw-L12LedgerError 'LEDGER_PARENT_REJECTED'
    }
    if ($KnownCipherSha) {
        $existing = Assert-L12LedgerRegularFile $fullTarget 1048576
        $existingBytes = [IO.File]::ReadAllBytes($existing.FullName)
        try {
            if ((Get-L12LedgerSha256Hex $existingBytes) -cne $KnownCipherSha) {
                Throw-L12LedgerError 'LEDGER_TARGET_CHANGED'
            }
        }
        finally { [Array]::Clear($existingBytes, 0, $existingBytes.Length) }
    }
    elseif (Test-Path -LiteralPath $fullTarget) {
        Throw-L12LedgerError 'LEDGER_ALREADY_EXISTS'
    }

    $newSha = Get-L12LedgerSha256Hex $Ciphertext
    $tempPath = Join-Path $parent ((Split-Path -Leaf $fullTarget) + '.' + [Guid]::NewGuid().ToString('N') + '.cipher.tmp')
    try {
        $stream = [IO.FileStream]::new(
            $tempPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::Read,
            4096,
            [IO.FileOptions]::WriteThrough
        )
        try {
            $stream.Write($Ciphertext, 0, $Ciphertext.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }

        if ($KnownCipherSha) {
            # Recheck immediately before replacement; this sink never knowingly replaces a
            # target other than the exact ciphertext version it last committed.
            $current = Assert-L12LedgerRegularFile $fullTarget 1048576
            $currentBytes = [IO.File]::ReadAllBytes($current.FullName)
            try {
                if ((Get-L12LedgerSha256Hex $currentBytes) -cne $KnownCipherSha) {
                    Throw-L12LedgerError 'LEDGER_TARGET_CHANGED'
                }
            }
            finally { [Array]::Clear($currentBytes, 0, $currentBytes.Length) }
            [IO.File]::Replace($tempPath, $fullTarget, [NullString]::Value, $true)
        }
        else {
            [IO.File]::Move($tempPath, $fullTarget)
        }
        $written = Assert-L12LedgerRegularFile $fullTarget 1048576
        $writtenBytes = [IO.File]::ReadAllBytes($written.FullName)
        try {
            if ((Get-L12LedgerSha256Hex $writtenBytes) -cne $newSha) {
                Throw-L12LedgerError 'LEDGER_PERSIST_FAILED'
            }
        }
        finally { [Array]::Clear($writtenBytes, 0, $writtenBytes.Length) }
        $newSha
    }
    finally {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) { [IO.File]::Delete($tempPath) }
    }
}

function Read-L12LedgerExactBytes(
    [IO.Stream]$Stream,
    [int]$Count,
    [int]$TimeoutMs,
    [bool]$AllowCleanEof
) {
    $buffer = [byte[]]::new($Count)
    $offset = 0
    while ($offset -lt $Count) {
        $task = $Stream.ReadAsync($buffer, $offset, $Count - $offset)
        if (-not $task.Wait($TimeoutMs)) { Throw-L12LedgerError 'PIPE_READ_TIMEOUT' }
        $read = $task.GetAwaiter().GetResult()
        if ($read -eq 0) {
            if ($AllowCleanEof -and $offset -eq 0) { return $null }
            Throw-L12LedgerError 'PIPE_FRAME_REJECTED'
        }
        $offset += $read
    }
    $buffer
}

function Read-L12LedgerFrame([IO.Stream]$Stream, [int]$TimeoutMs) {
    $prefix = Read-L12LedgerExactBytes $Stream 4 $TimeoutMs $true
    if ($null -eq $prefix) { return $null }
    $length = [BitConverter]::ToInt32($prefix, 0)
    if ($length -lt 2 -or $length -gt $script:LedgerMaxFrameBytes) {
        Throw-L12LedgerError 'PIPE_FRAME_REJECTED'
    }
    $payload = Read-L12LedgerExactBytes $Stream $length $TimeoutMs $false
    try {
        $script:LedgerUtf8.GetString($payload) | ConvertFrom-Json -Depth 16 -DateKind String
    }
    catch { Throw-L12LedgerError 'PIPE_FRAME_REJECTED' }
    finally { [Array]::Clear($payload, 0, $payload.Length) }
}

function Write-L12LedgerFrame([IO.Stream]$Stream, [object]$Value) {
    $payload = $script:LedgerUtf8.GetBytes(($Value | ConvertTo-Json -Depth 8 -Compress))
    try {
        if ($payload.Length -gt 1024) { Throw-L12LedgerError 'PIPE_FRAME_REJECTED' }
        $prefix = [BitConverter]::GetBytes([int]$payload.Length)
        $Stream.Write($prefix, 0, $prefix.Length)
        $Stream.Write($payload, 0, $payload.Length)
        $Stream.Flush()
    }
    finally { [Array]::Clear($payload, 0, $payload.Length) }
}

function Start-L12AccountLedgerSink(
    [string]$ExpectedRunId,
    [string]$ExpectedPipeName,
    [string]$OutputPath,
    [int]$ExpectedMaxSlots,
    [int]$ConnectTimeoutMs,
    [int]$ReadTimeoutMs
) {
    if ($ExpectedPipeName -notmatch '^[A-Za-z0-9_-]{16,96}$') { Throw-L12LedgerError 'PIPE_NAME_REJECTED' }
    $fullOutput = [IO.Path]::GetFullPath($OutputPath)
    if ([string]::IsNullOrWhiteSpace($OutputPath) -or [IO.Path]::GetExtension($fullOutput) -cne '.dpapi') {
        Throw-L12LedgerError 'LEDGER_PATH_REJECTED'
    }
    if (Test-Path -LiteralPath $fullOutput) { Throw-L12LedgerError 'LEDGER_ALREADY_EXISTS' }

    $state = New-L12LedgerState $ExpectedRunId $ExpectedMaxSlots
    $pipeOptions = [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
    $server = [IO.Pipes.NamedPipeServerStream]::new(
        $ExpectedPipeName,
        [IO.Pipes.PipeDirection]::InOut,
        1,
        [IO.Pipes.PipeTransmissionMode]::Byte,
        $pipeOptions,
        4096,
        4096
    )
    try {
        $wait = $server.WaitForConnectionAsync()
        if (-not $wait.Wait($ConnectTimeoutMs)) { Throw-L12LedgerError 'PIPE_CONNECT_TIMEOUT' }
        $wait.GetAwaiter().GetResult()
        while ($true) {
            $event = Read-L12LedgerFrame $server $ReadTimeoutMs
            if ($null -eq $event) { break }
            try {
                $ack = Invoke-L12LedgerEvent $state $event {
                    param([byte[]]$plain, [string]$run)
                    Protect-L12LedgerBytes $plain $run
                } {
                    param([byte[]]$cipher, [AllowNull()] [string]$knownSha)
                    Write-L12LedgerAtomicSnapshot $fullOutput $cipher $knownSha
                }
                Write-L12LedgerFrame $server $ack
            }
            catch {
                $code = Get-L12LedgerSafeErrorCode $_.Exception
                try { Write-L12LedgerFrame $server ([ordered]@{ ok = $false; code = $code }) } catch {}
                Throw-L12LedgerError $code
            }
        }
        [ordered]@{
            seq = [long]$state.Sequence
            count = [int]$state.Accounts.Count
            cipherSha256 = $state.CipherSha256
        }
    }
    finally { $server.Dispose() }
}

if (-not $LibraryOnly) {
    try {
        if (-not $Apply) { Throw-L12LedgerError 'APPLY_REQUIRED' }
        $summary = Start-L12AccountLedgerSink $RunId $PipeName $LedgerPath $MaxSlots $ConnectionTimeoutMs $FrameTimeoutMs
        [Console]::Out.WriteLine(($summary | ConvertTo-Json -Compress))
    }
    catch {
        $code = Get-L12LedgerSafeErrorCode $_.Exception
        [Console]::Error.WriteLine(([ordered]@{ status = 'failed'; code = $code } | ConvertTo-Json -Compress))
        exit 1
    }
}
