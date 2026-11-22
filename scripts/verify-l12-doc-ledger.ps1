# 文档台账一致性门禁（D5）。
# 结构与顺序约束：条目 id 唯一、id 内含可解析日期、同文件条目日期不得倒序。
# 违规数量写入基线文件，只降不升；修复后再下调基线。
# 前向 schema（D5 二期，见 docs/l12/DOC-ENTRY-SCHEMA.md）：id 日期 >= $SchemaEffectiveDate 的条目
# 必须带齐必填标签行；历史条目一律豁免、不检查、不改写。该项为硬失败，不设棘轮。
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$UpdateBaseline
)

# 前向 schema 生效日：id 日期 >= 该日的新条目必须带齐必填标签（见 docs/l12/DOC-ENTRY-SCHEMA.md）。
$SchemaEffectiveDate = '2026-11-23'
$RequiredSchemaFields = [ordered]@{
    '状态'     = @('状态')
    '现象'     = @('现象', '症状', '核对问题', '背景')
    '根因'     = @('根因')
    '同型扫描' = @('同型扫描', '同类扫描')
    '改动'     = @('改动', '修复', '已实现')
    '回归'     = @('回归')
    '验证'     = @('验证')
    '回滚'     = @('回滚')
}

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

# 前向 schema：只看 id 日期 >= $SchemaEffectiveDate 的条目（`#`/`##` 两种登记标题都算）。
# 历史条目完全不进入这段逻辑，因此不可能被本项改写或判红。
$schemaHeadings = [regex]::Matches($registryText, '(?m)^#{1,2}\s+(?<id>[A-Z0-9][A-Z0-9-]*\d{8}[A-Z0-9-]*)')
$schemaEntries = 0
$schemaViolations = @()
for ($index = 0; $index -lt $schemaHeadings.Count; $index++) {
    $heading = $schemaHeadings[$index]
    $idDate = [regex]::Match($heading.Groups['id'].Value, $dateInId)
    if (-not $idDate.Success) { continue }
    $stamp = $idDate.Groups['date'].Value + '-' + $idDate.Groups['month'].Value + '-' + $idDate.Groups['day'].Value
    if ($stamp -lt $SchemaEffectiveDate) { continue }
    $schemaEntries++
    $blockStart = $heading.Index
    $blockEnd = if ($index + 1 -lt $schemaHeadings.Count) { $schemaHeadings[$index + 1].Index } else { $registryText.Length }
    $block = $registryText.Substring($blockStart, $blockEnd - $blockStart)
    $missing = @()
    foreach ($field in $RequiredSchemaFields.Keys) {
        $hit = $false
        foreach ($label in $RequiredSchemaFields[$field]) {
            if ([regex]::IsMatch($block, ('(?m)^\s*[-*]\s*' + [regex]::Escape($label) + '\s*[:：]'))) { $hit = $true; break }
        }
        if (-not $hit) { $missing += $field }
    }
    if ($missing.Count) { $schemaViolations += ($heading.Groups['id'].Value + ' 缺少：' + ($missing -join '/')) }
}

$summary = [ordered]@{
    capturedAt = (Get-Date).ToString('yyyy-MM-dd')
    registryEntries = $entries.Count
    duplicateEntryIds = $duplicateEntryIds.Count
    undatedEntries = $undatedEntries
    registryOutOfOrderDates = $registryOutOfOrder
    ledgerOutOfOrderDates = $ledgerOutOfOrder
    schemaEntries = $schemaEntries
    schemaViolations = $schemaViolations.Count
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
if ($schemaViolations.Count) {
    $detail = $schemaViolations -join ([Environment]::NewLine + '- ')
    $message = '前向 schema 校验失败（自 ' + $SchemaEffectiveDate + ' 生效，见 docs/l12/DOC-ENTRY-SCHEMA.md）：' + [Environment]::NewLine + '- ' + $detail
    Write-Error $message
}
if ($failures.Count) {
    Write-Error ("Doc ledger check failed:" + [Environment]::NewLine + '- ' + ($failures -join ([Environment]::NewLine + '- ')))
}
Write-Host '[L12 doc ledger] entry ids unique, dates parseable, heading order monotonic.'
