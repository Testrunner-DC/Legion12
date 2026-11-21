# 文档台账一致性门禁（D5 第一阶段）。
# 只做可机检的结构约束：条目 id 唯一、id 内含可解析日期、同文件条目日期不得倒序。
# 违规数量写入基线文件，只降不升；修复后再下调基线。
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$registryPath = Join-Path $ProjectRoot 'docs/BUGFIX-REGISTRY.md'
$ledgerPath = Join-Path $ProjectRoot 'docs/TASK-LEDGER.md'
$baselinePath = Join-Path $ProjectRoot 'docs/l12/DOC-LEDGER-BASELINE.json'
foreach ($path in @($registryPath, $ledgerPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing ledger document: $path" }
}

$registryText = [IO.File]::ReadAllText($registryPath, [Text.Encoding]::UTF8)
$ledgerText = [IO.File]::ReadAllText($ledgerPath, [Text.Encoding]::UTF8)

# 登记表条目形如：## TYPE-YYYYMMDD-SLUG｜标题
$entryPattern = '(?m)^##\s+(?<id>[A-Z0-9][A-Z0-9-]*\d{8}[A-Z0-9-]*)'
$entries = [regex]::Matches($registryText, $entryPattern)
$entryIds = @($entries | ForEach-Object { $_.Groups['id'].Value })
$duplicateEntryIds = @($entryIds | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })

$dateInId = '(?<date>\d{4})(?<month>\d{2})(?<day>\d{2})'
$undatedEntries = 0
$registryOutOfOrder = 0
$previousDate = $null
foreach ($entry in $entries) {
    $match = [regex]::Match($entry.Groups['id'].Value, $dateInId)
    if (-not $match.Success) { $undatedEntries++; continue }
    $stamp = $match.Groups['date'].Value + '-' + $match.Groups['month'].Value + '-' + $match.Groups['day'].Value
    $parsed = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($stamp, 'yyyy-MM-dd', $null, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        $undatedEntries++
        continue
    }
    if ($null -ne $previousDate -and $parsed -gt $previousDate) { $registryOutOfOrder++ }
    $previousDate = $parsed
}

# 任务台账标题形如：## 2026-11-21 描述
$ledgerOutOfOrder = 0
$previousLedgerDate = $null
foreach ($match in [regex]::Matches($ledgerText, '(?m)^#{2,4}\s*(?<date>20\d{2}-\d{2}-\d{2})')) {
    $parsed = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($match.Groups['date'].Value, 'yyyy-MM-dd', $null, [Globalization.DateTimeStyles]::None, [ref]$parsed)) { continue }
    if ($null -ne $previousLedgerDate -and $parsed -gt $previousLedgerDate) { $ledgerOutOfOrder++ }
    $previousLedgerDate = $parsed
}

$summary = [ordered]@{
    capturedAt = (Get-Date).ToString('yyyy-MM-dd')
    registryEntries = $entries.Count
    duplicateEntryIds = $duplicateEntryIds.Count
    undatedEntries = $undatedEntries
    registryOutOfOrderDates = $registryOutOfOrder
    ledgerOutOfOrderDates = $ledgerOutOfOrder
}

if ($UpdateBaseline) {
    $summary | ConvertTo-Json | Set-Content -LiteralPath $baselinePath -Encoding utf8
    Write-Host "[L12 doc ledger] baseline written: $baselinePath"
    $summary | ConvertTo-Json
    return
}

if (-not (Test-Path -LiteralPath $baselinePath)) {
    throw "Missing doc-ledger baseline: $baselinePath (run once with -UpdateBaseline, review the numbers, then commit)"
}
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json

$failures = @()
foreach ($key in @('duplicateEntryIds', 'undatedEntries', 'registryOutOfOrderDates', 'ledgerOutOfOrderDates')) {
    $current = [int]$summary[$key]
    $allowed = [int]$baseline.$key
    if ($current -gt $allowed) { $failures += "$key grew from $allowed to $current." }
}

$summary | ConvertTo-Json
if ($failures.Count) {
    Write-Error ("Doc ledger check failed:" + [Environment]::NewLine + '- ' + ($failures -join ([Environment]::NewLine + '- ')))
}
Write-Host '[L12 doc ledger] entry ids unique, dates parseable, heading order monotonic.'
