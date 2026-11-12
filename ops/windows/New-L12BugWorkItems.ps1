[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IntakeSnapshotPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$ReceiptPath = ""
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
$snapshotPath = Assert-L12GeneratedPath -Path $IntakeSnapshotPath -Purpose 'Bug intake snapshot'
$outputPath = Assert-L12GeneratedPath -Path $OutputPath -Purpose 'Bug work items'
$snapshot = Get-Content -LiteralPath $snapshotPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($snapshot.schema -ne 1 -or [string]::IsNullOrWhiteSpace([string]$snapshot.snapshotTag) -or
    [string]::IsNullOrWhiteSpace([string]$snapshot.snapshotSha256) -or $null -eq $snapshot.items) {
    throw 'Bug intake snapshot schema or immutable identity is invalid.'
}

function Get-SourceFingerprint {
    param($Item)
    $canonical = [ordered]@{
        id = [string]$Item.id; reporterId = $Item.reporterId; reporterName = [string]$Item.reporterName; title = [string]$Item.title
        description = [string]$Item.description; page = [string]$Item.page; roomCode = $Item.roomCode
        matchId = $Item.matchId; version = [string]$Item.version; status = [string]$Item.status
        priority = [string]$Item.priority; assignee = $Item.assignee; adminNotes = $Item.adminNotes
        createdAt = [string]$Item.createdAt; updatedAt = [string]$Item.updatedAt
    }
    $json = ConvertTo-Json $canonical -Depth 8 -Compress
    $sha = [System.Security.Cryptography.SHA256]::Create()
    ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($json))).Replace('-', '').ToLowerInvariant())
}

$workItems = @($snapshot.items | ForEach-Object {
    $terminal = [string]$_.status -in @('closed', 'resolved')
    [ordered]@{
        id = [string]$_.id
        state = if ($terminal) { 'closed' } else { 'ready' }
        sourceSnapshotTag = [string]$snapshot.snapshotTag
        sourcePlatformVersion = [int64]$snapshot.platformVersion
        sourceFingerprint = Get-SourceFingerprint $_
        title = [string]$_.title
        matchId = $_.matchId
        roomCode = $_.roomCode
        requiresUserRuling = $false
        evidence = @(@(
            if ($_.matchId) { [ordered]@{ kind = 'match'; value = [string]$_.matchId } }
            if ($_.roomCode) { [ordered]@{ kind = 'room'; value = [string]$_.roomCode } }
        ))
        proposal = $null
        lastAttempt = $null
        receipt = $null
    }
})

$workflow = [ordered]@{
    schema = 1; kind = 'bug-work-items'; createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    queueSnapshotPath = $snapshotPath; queueSnapshotTag = [string]$snapshot.snapshotTag
    queueSnapshotSha256 = [string]$snapshot.snapshotSha256; platformVersion = [int64]$snapshot.platformVersion
    counts = [ordered]@{ total = $workItems.Count; ready = @($workItems | Where-Object state -eq 'ready').Count; closed = @($workItems | Where-Object state -eq 'closed').Count; needsUserRuling = 0; needsRepro = 0; verifyLive = 0; blocked = 0 }
    items = $workItems
}
$outputDirectory = Split-Path -Parent $outputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null }
[IO.File]::WriteAllText($outputPath, (ConvertTo-Json $workflow -Depth 20), [Text.UTF8Encoding]::new($false))

if ($ReceiptPath) {
    $receiptPath = Assert-L12GeneratedPath -Path $ReceiptPath -Purpose 'Bug work-item receipt'
    $receiptDirectory = Split-Path -Parent $receiptPath
    if ($receiptDirectory) { New-Item -ItemType Directory -Force -Path $receiptDirectory | Out-Null }
    $receipt = [ordered]@{ schema = 1; kind = 'bug-work-items'; status = 'initialized'; queueSnapshotTag = $snapshot.snapshotTag; queueSnapshotSha256 = $snapshot.snapshotSha256; workItemsPath = $outputPath; total = $workItems.Count; ready = $workflow.counts.ready; closed = $workflow.counts.closed; createdAtUtc = $workflow.createdAtUtc }
    [IO.File]::WriteAllText($receiptPath, (ConvertTo-Json $receipt -Depth 10), [Text.UTF8Encoding]::new($false))
}
Write-Host "已生成 $($workItems.Count) 条离线 Bug 工作项；SnapshotTag=$($snapshot.snapshotTag)；未执行任何线上写入。"
