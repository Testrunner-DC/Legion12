param(
    [Parameter(Mandatory)][string]$ControlPath,
    [Parameter(Mandatory)][ValidatePattern('^[A-F0-9]{64}$')][string]$ExpectedServerSha256
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../windows/L12GeneratedStorage.ps1')
$null = Assert-L12GeneratedPath -Path (Split-Path -Parent $ControlPath) -Purpose 'local pressure cold ledger verification'
$control = Get-Content -LiteralPath $ControlPath -Raw | ConvertFrom-Json -Depth 16
if ($control.kind -cne 'local-pressure-account-ledgers' -or $control.runId -notmatch '^[a-f0-9]{32}$' -or
    $control.serverSha256 -cne $ExpectedServerSha256 -or @($control.groups).Count -ne 2) { throw 'LOCAL_LEDGER_BINDING_REJECTED' }
$base = [uri]$control.baseUrl
if ($base.Scheme -cne 'http' -or $base.Host -cne '127.0.0.1' -or $base.Port -le 0 -or
    $base.UserInfo -or $base.Query -or $base.Fragment -or $base.AbsolutePath -cne '/') { throw 'LOCAL_LEDGER_BINDING_REJECTED' }
# Execute only the existing immutable repository helpers, not a test runner.
$source = Join-Path $PSScriptRoot 'ledger/AccountLedgerReader.ps1'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne
    '7ABD52F25FFB11B9DEDBD11B6C6E4FF592F72DA0900D46133BC41E34AE1EBE25') { throw 'LOCAL_READER_SOURCE_REJECTED' }
$tokens=$null; $parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$parseErrors)
if (@($parseErrors).Count -ne 0) { throw 'LOCAL_READER_SOURCE_REJECTED' }
foreach ($name in @('Throw-L12RealTest','Assert-L12RegularFile','Test-L12ContainsBytes','Read-L12ProtectedSnapshot')) {
    $functions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true) | Where-Object Name -CEQ $name)
    if ($functions.Count -ne 1) { throw 'LOCAL_READER_SOURCE_REJECTED' }
    . ([scriptblock]::Create($functions[0].Extent.Text))
}
$script:Utf8NoBom=[Text.UTF8Encoding]::new($false,$true)
$results=@()
foreach ($index in 0..1) {
    $group=$control.groups[$index]
    $expectedRun='local-'+$control.runId+'-'+$index
    if ($group.runId -cne $expectedRun -or $group.maxSlots -ne @(60,9)[$index] -or
        [IO.Path]::GetFileName($group.ledgerPath) -cne ('ledger-'+$index+'.dpapi') -or
        [IO.Path]::GetFileName((Split-Path -Parent $group.ledgerPath)) -cne $control.runId) { throw 'LOCAL_LEDGER_BINDING_REJECTED' }
    $null=Assert-L12GeneratedPath -Path (Split-Path -Parent $group.ledgerPath) -Purpose 'owned synthetic ledger'
    $owner=Get-Content -LiteralPath (Join-Path (Split-Path -Parent $group.ledgerPath) 'owner.json') -Raw | ConvertFrom-Json
    if ($owner.kind -cne 'l12-local-real-pressure-v1' -or $owner.runId -cne $control.runId -or
        $owner.serverSha256 -cne $ExpectedServerSha256) { throw 'LOCAL_LEDGER_BINDING_REJECTED' }
    if (-not (Test-Path -LiteralPath $group.ledgerPath -PathType Leaf)) { continue }
    $read=Read-L12ProtectedSnapshot $group.ledgerPath $expectedRun @()
    $snapshot=$read.Snapshot
    if ($snapshot.schema -ne 1 -or $snapshot.runId -cne $expectedRun -or
        $snapshot.count -ne @($snapshot.accounts).Count -or $snapshot.count -gt $group.maxSlots -or
        $snapshot.sequence -lt $snapshot.count -or $snapshot.sequence -gt (2*$snapshot.count)) { throw 'LOCAL_LEDGER_CONTENT_REJECTED' }
    $wrongRunRejected=$false
    try { $null=Read-L12ProtectedSnapshot $group.ledgerPath ($expectedRun+'-wrong') @() } catch { $wrongRunRejected=$true }
    if (-not $wrongRunRejected) { throw 'LOCAL_LEDGER_WRONG_RUN_ACCEPTED' }
    $results += [ordered]@{group=$index; count=$snapshot.count; sequence=$snapshot.sequence;
        confirmed=@($snapshot.accounts | Where-Object stage -CEQ 'confirmed').Count;
        cipherSha256=$read.CipherSha256; wrongRunRejected=$true}
    $snapshot=$null; $read=$null
}
[ordered]@{schema=1; kind='local-pressure-ledger-cold-read'; groups=$results;
    secretsEmitted=$false; remoteCalls=0} | ConvertTo-Json -Depth 5 -Compress
