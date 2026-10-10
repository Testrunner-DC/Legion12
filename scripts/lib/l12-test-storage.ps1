Set-StrictMode -Version Latest

# A test host owns its SQLite pools. Reclaim only after that process exits and
# its TRX proves every executed test passed. Never clear production pools.
function Assert-L12PlainTestPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Test storage linked path refused: $cursor"
            }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Get-L12PlainTestTree([string]$Path) {
    Assert-L12PlainTestPath $Path
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($Path)
    while ($queue.Count) {
        $directory = $queue.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Test storage linked descendant refused: $($item.FullName)" }
            $item
            if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) }
        }
    }
}

function Invoke-L12TestRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][AllowEmptyString()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$TemporaryBase,
        [Parameter(Mandatory)][string]$EvidenceBase,
        [string]$Label = 'tests'
    )
    $Arguments=@($Arguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($Arguments.Count -eq 0 -or $Arguments[0] -ne 'test') { throw 'Only dotnet test is supervised.' }
    $temporaryRoot = [IO.Path]::GetFullPath($TemporaryBase)
    $evidenceRoot = [IO.Path]::GetFullPath($EvidenceBase)
    $separator = [IO.Path]::DirectorySeparatorChar
    if ($evidenceRoot -eq $temporaryRoot -or $evidenceRoot.StartsWith($temporaryRoot.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Evidence must be outside the temporary tree.'
    }
    Assert-L12PlainTestPath $temporaryRoot
    Assert-L12PlainTestPath $evidenceRoot
    $runId = 'test-' + [Guid]::NewGuid().ToString('N')
    $run = Join-Path $temporaryRoot $runId
    $evidence = Join-Path $evidenceRoot $runId
    New-Item -ItemType Directory -Path $run, $evidence -ErrorAction Stop | Out-Null
    $receiptPath = Join-Path $evidence 'receipt.json'
    $record = [ordered]@{ schema=1; runId=$runId; label=$Label; ownerPid=$PID; startedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); temporaryPath=$run; status='started'; exitCode=$null; counters=$null; cleanupAttempts=0; error=$null }
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    $oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldTmpdir = $env:TMPDIR; $oldRunTemp = $env:L12_TEST_TEMP_ROOT
    # The compiler server outlives dotnet test and keeps analyzer assemblies
    # in TEMP open. This run must not start a persistent shared compiler in
    # its owned temporary tree; do not kill unrelated compiler processes.
    $testArguments = @($Arguments)
    $optionBoundary=[array]::IndexOf($testArguments,'--')
    if ($optionBoundary -lt 0) { $optionBoundary=$testArguments.Count }
    $testArguments=@($testArguments | Select-Object -First $optionBoundary) + @('-p:UseSharedCompilation=false') + @($testArguments | Select-Object -Skip $optionBoundary)
    # Keep existing Release TRX paths and optional evidence budgets. If none was
    # requested, the small mandatory pass proof lives in the run's evidence dir.
    $logger = ''; $results = ''; $separatorIndex = $testArguments.Count
    for ($i=0; $i -lt $testArguments.Count; $i++) {
        if ($testArguments[$i] -eq '--') { $separatorIndex=$i; break }
        if ($testArguments[$i] -eq '--logger' -and $i+1 -lt $testArguments.Count -and $testArguments[$i+1] -match '^trx(?:;|$)') { $logger=$testArguments[$i+1] }
        if ($testArguments[$i] -eq '--results-directory' -and $i+1 -lt $testArguments.Count) { $results=$testArguments[$i+1] }
    }
    $filenameMatch=[regex]::Match($logger,'LogFileName=([^;]+)')
    if ($logger -and (-not $results -or -not $filenameMatch.Success -or $filenameMatch.Groups[1].Value -match '[/\\\\]' -or $filenameMatch.Groups[1].Value -in @('.','..'))) { throw 'Supervised TRX needs an explicit result directory and basename.' }
    $privateProof = -not $logger
    if ($privateProof) {
        $trx = Join-Path $evidence 'pass-proof.trx'
        $extra = @('--logger', 'trx;LogFileName=pass-proof.trx', '--results-directory', $evidence)
        $testArguments = @($testArguments | Select-Object -First $separatorIndex) + $extra + @($testArguments | Select-Object -Skip $separatorIndex)
    } else {
        $trx = Join-Path $results $filenameMatch.Groups[1].Value
    }
    try {
        if (Test-Path -LiteralPath $trx) { throw 'Existing TRX cannot prove this run; data retained.' }
        $env:TEMP=$run; $env:TMP=$run; $env:TMPDIR=$run; $env:L12_TEST_TEMP_ROOT=$run
        & $Executable @testArguments | Out-Host
        $record.exitCode=$LASTEXITCODE
        if ($record.exitCode -ne 0) { throw "Test process failed: $($record.exitCode)" }
        if (-not (Test-Path -LiteralPath $trx -PathType Leaf)) { throw 'Missing test pass proof; temporary data retained.' }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver=$null
        $reader=[Xml.XmlReader]::Create($trx,$settings)
        try { $xml=[Xml.XmlDocument]::new(); $xml.XmlResolver=$null; $xml.Load($reader) } finally { $reader.Dispose() }
        $counterNodes=@($xml.SelectNodes("//*[local-name()='ResultSummary']/*[local-name()='Counters']"))
        if ($counterNodes.Count -ne 1) { throw 'Ambiguous test counters; data retained.' }
        $counter=$counterNodes[0]
        $total=[int]$counter.GetAttribute('total'); $passed=[int]$counter.GetAttribute('passed')
        $executed=[int]$counter.GetAttribute('executed'); $failed=[int]$counter.GetAttribute('failed'); $skipped=[int]$counter.GetAttribute('notExecuted')
        $record.counters=@{ total=$total; passed=$passed; executed=$executed; failed=$failed; skipped=$skipped }
        if ($total -lt 1 -or $passed -ne $total -or $executed -ne $total -or $failed -ne 0 -or $skipped -ne 0) { throw 'Tests failed, skipped or empty; data retained.' }
        $expected=Join-Path $temporaryRoot $runId
        if ($run -ne $expected -or (Split-Path -Leaf $run) -notmatch '^test-[0-9a-f]{32}$') { throw 'Temporary ownership mismatch.' }
        # Preflight the entire tree before the first destructive operation.
        $tree=@(Get-L12PlainTestTree $run)
        [long]$bytes=0
        foreach ($file in $tree) { if (-not $file.PSIsContainer) { $bytes += $file.Length } }
        $record['temporaryLogicalBytes']=$bytes
        $record.status='passed-cleanup-pending'
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
        for ($attempt=1; $attempt -le 3; $attempt++) {
            $record.cleanupAttempts=$attempt
            try {
                if (Test-Path -LiteralPath $run) {
                    @(Get-L12PlainTestTree $run) | Out-Null
                    Remove-Item -LiteralPath $run -Recurse -Force -ErrorAction Stop
                }
                break
            } catch {
                if ($attempt -eq 3) { throw }
                Start-Sleep -Milliseconds 250
            }
        }
        $record.status='success'
        # Counters remain durable. Full optional Release TRX is not removed.
        if ($privateProof) { Remove-Item -LiteralPath $trx -Force -ErrorAction Stop }
        Write-Host "[L12 test storage] $Label passed; owned temporary tree removed. Receipt: $receiptPath"
    } catch {
        $record.status='retained'
        $record.error=$_.Exception.Message
        Write-Warning "[L12 test storage] $Label retained at $run; evidence: $receiptPath"
        throw
    } finally {
        $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:TMPDIR=$oldTmpdir; $env:L12_TEST_TEMP_ROOT=$oldRunTemp
        $record['completedAtUtc']=[DateTimeOffset]::UtcNow.ToString('O')
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    }
}
