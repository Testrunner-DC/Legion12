# D5 P2-a：登记表结构可解析性评估（只读，不改任何文件）
# 目的：在把 Markdown 变成 JSONL 之前，先量出"能解析多少、缺哪些字段、多少行无法归类"。
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$registryPath = Join-Path $ProjectRoot 'docs/BUGFIX-REGISTRY.md'
if (-not (Test-Path -LiteralPath $registryPath)) { throw "Missing registry: $registryPath" }
$text = [IO.File]::ReadAllText($registryPath, [Text.Encoding]::UTF8)
$lines = $text -split "`n" | ForEach-Object { $_.TrimEnd("`r") }

# 标签 -> 规范字段。同义写法必须显式列出来，不能让解析器自己猜。
$fieldMap = [ordered]@{
    '现象' = 'symptom'; '现象与根因' = 'symptom'
    '根因' = 'rootCause'
    '同类扫描' = 'sameTypeScan'; '同类型扫描' = 'sameTypeScan'; '同型扫描' = 'sameTypeScan'
    '改动' = 'change'; '修复' = 'change'; '公共修复' = 'change'; '已实现' = 'change'
    '修改' = 'change'; '公共修改' = 'change'; '共享修复' = 'change'; '共享修正' = 'change'
    '修正' = 'change'; '明确修复' = 'change'; '最小修复' = 'change'; '统一修复' = 'change'
    '修改文件' = 'change'; '实施' = 'change'; '实现' = 'change'
    '回归' = 'regression'; '回归守卫' = 'regression'
    '验证' = 'verification'
    '防回滚' = 'rollbackGuard'; '防回归' = 'rollbackGuard'; '回滚守卫' = 'rollbackGuard'; '防回滚与验证' = 'rollbackGuard'
    '回滚' = 'rollback'
    '历史记录匹配' = 'historyMatch'
    '边界' = 'boundary'
    '状态' = 'status'
}

$canonical = @('symptom','rootCause','sameTypeScan','change','regression','verification','rollbackGuard','rollback','historyMatch','boundary','status')
$entries = @()
$current = $null
$unrecognizedLabels = @{}
$unrecognizedLines = 0
$labelHits = @{}

foreach ($line in $lines) {
    if ($line -match '^##\s+(?<id>[A-Za-z0-9][A-Za-z0-9-]*)\s*(?:｜(?<title>.*))?$') {
        if ($null -ne $current) { $entries += $current }
        $id = $matches['id']
        $title = $matches['title']
        $dateMatch = [regex]::Match($id, '(?<y>\d{4})(?<m>\d{2})(?<d>\d{2})')
        $status = if ($title -match '（(?<s>[^）]*)）') { $matches['s'] } else { '' }
        $current = [ordered]@{
            id = $id
            prefix = ($id -split '-')[0]
            date = if ($dateMatch.Success) { $dateMatch.Groups['y'].Value + '-' + $dateMatch.Groups['m'].Value + '-' + $dateMatch.Groups['d'].Value } else { '' }
            title = if ($title) { ($title -replace '（[^）]*）$','').Trim() } else { '' }
            status = $status
            fields = @{}
            extraLines = 0
        }
        continue
    }
    if ($null -eq $current) { continue }
    if ($line -match '^-\s*(?<label>[^：:]{1,12})[：:]') {
        # 加粗标记与全角空格会破坏标签匹配，先归一化再查表。
        $label = ($matches['label'] -replace '\*', '').Trim()
        $field = if ($fieldMap.Contains($label)) { $fieldMap[$label] } else { $null }
        if ($null -eq $field) {
            if (-not $unrecognizedLabels.ContainsKey($label)) { $unrecognizedLabels[$label] = 0 }
            $unrecognizedLabels[$label]++
            $current.extraLines++
        }
        else {
            if (-not $current.fields.ContainsKey($field)) { $current.fields[$field] = 0 }
            $current.fields[$field]++
            if (-not $labelHits.ContainsKey($label)) { $labelHits[$label] = 0 }
            $labelHits[$label]++
        }
        continue
    }
    if ($line.Trim().Length -gt 0) { $unrecognizedLines++ }
}
if ($null -ne $current) { $entries += $current }

$missing = [ordered]@{}
foreach ($field in $canonical) {
    $missing[$field] = @($entries | Where-Object { -not $_.fields.ContainsKey($field) }).Count
}
$duplicateIds = @($entries | Group-Object { $_.id } | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
$prefixes = @($entries | Group-Object { $_.prefix } | Sort-Object Count -Descending)

$report = [ordered]@{
    capturedAt = (Get-Date).ToString('yyyy-MM-dd')
    registryPath = 'docs/BUGFIX-REGISTRY.md'
    entries = $entries.Count
    lines = $lines.Count
    duplicateIds = $duplicateIds
    distinctPrefixes = $prefixes.Count
    topPrefixes = @($prefixes | Select-Object -First 10 | ForEach-Object { [ordered]@{ prefix = $_.Name; count = $_.Count } })
    fieldCoverage = [ordered]@{
        present = [ordered]@{}
        missing = $missing
    }
    labelVocabulary = @($labelHits.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { [ordered]@{ label = $_.Key; count = $_.Value } })
    unrecognizedLabels = @($unrecognizedLabels.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { [ordered]@{ label = $_.Key; count = $_.Value } })
    unrecognizedBodyLines = $unrecognizedLines
    entriesWithExtraLines = @($entries | Where-Object { $_.extraLines -gt 0 }).Count
}
foreach ($field in $canonical) { $report.fieldCoverage.present[$field] = $entries.Count - $missing[$field] }

$report | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $target = if ([IO.Path]::IsPathRooted($ReportPath)) { $ReportPath } else { Join-Path $ProjectRoot $ReportPath }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::WriteAllText($target, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Write-Host "[L12 registry structure] report written: $target"
}
