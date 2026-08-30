param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = 'docs/l12/CARD-EFFECT-REVIEW-MATRIX.md'
)

$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $ProjectRoot '服务端WebSocket/TwelveLegions'
$dataPath = Join-Path $sourcePath 'Data'
$atomicSource = [System.IO.File]::ReadAllText((Join-Path $sourcePath 'AtomicEffects.cs'), [System.Text.Encoding]::UTF8)
$routeSource = [System.IO.File]::ReadAllText((Join-Path $sourcePath 'L12RuntimeEffectRoutes.cs'), [System.Text.Encoding]::UTF8)
$programMatches = [regex]::Matches($atomicSource,
    'Program\("(?<id>S\d{2}-[A-Za-z0-9]+)"\s*,\s*"(?<trigger>[^"]+)"')
$routeMatches = [regex]::Matches($routeSource,
    'new\("(?<id>S\d{2}-[A-Za-z0-9]+)"\s*,\s*"(?<trigger>[^"]+)"')

$fineByCard = @{}
foreach ($match in $programMatches) {
    $id = $match.Groups['id'].Value
    if (-not $fineByCard.ContainsKey($id)) { $fineByCard[$id] = New-Object System.Collections.Generic.List[string] }
    $fineByCard[$id].Add($match.Groups['trigger'].Value)
}
$compositeByCard = @{}
foreach ($match in $routeMatches) {
    $id = $match.Groups['id'].Value
    if (-not $compositeByCard.ContainsKey($id)) { $compositeByCard[$id] = New-Object System.Collections.Generic.List[string] }
    $compositeByCard[$id].Add($match.Groups['trigger'].Value)
}

$cards = New-Object System.Collections.Generic.List[object]
foreach ($fileName in @('cards.s1.json', 'cards.s2.json')) {
    $decoded = [System.IO.File]::ReadAllText((Join-Path $dataPath $fileName), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($card in $decoded) { $cards.Add($card) }
}

$rows = foreach ($card in ($cards | Sort-Object id)) {
    $fine = if ($fineByCard.ContainsKey($card.id)) {
        @($fineByCard[$card.id] | Sort-Object -Unique)
    } else { @() }
    $composite = if ($compositeByCard.ContainsKey($card.id)) {
        @($compositeByCard[$card.id] | Sort-Object -Unique)
    } else { @() }
    $runtime = if ($fine.Count -gt 0 -and $composite.Count -gt 0) {
        '混合：细原子已验证 + 复合过渡'
    } elseif ($fine.Count -gt 0) {
        '细原子已验证'
    } elseif ($composite.Count -gt 0) {
        '复合过渡'
    } else {
        '仅入清单 / 待迁移'
    }
    [pscustomobject]@{
        Id = $card.id
        Name = ($card.nameZh -replace '\|', '\|')
        Product = $card.product
        Faction = $card.faction
        Type = $card.cardType
        Runtime = $runtime
        Fine = if ($fine.Count) { $fine -join '、' } else { '—' }
        Composite = if ($composite.Count) { $composite -join '、' } else { '—' }
        Review = '待独立验收'
    }
}

$fineOnly = @($rows | Where-Object Runtime -eq '细原子已验证').Count
$mixed = @($rows | Where-Object Runtime -eq '混合：细原子已验证 + 复合过渡').Count
$compositeOnly = @($rows | Where-Object Runtime -eq '复合过渡').Count
$catalogOnly = @($rows | Where-Object Runtime -eq '仅入清单 / 待迁移').Count
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# 248 张卡效独立审查矩阵')
$lines.Add('')
$lines.Add("生成日期：$(Get-Date -Format 'yyyy-MM-dd')")
$lines.Add('')
$lines.Add('本表只记录可由源码和测试证据证明的运行时迁移层级；“细原子已验证”表示该卡至少有一个能力由细原子程序接管，并不自动证明同卡全部能力均已通过独立验收。“复合过渡”仍由 `operation.composite-flow` 承接，不能视为完成细原子化。人工/独立验收必须逐能力补证。')
$lines.Add('')
$lines.Add("- 卡池：$($rows.Count) 张")
$lines.Add("- 仅细原子已验证：$fineOnly 张")
$lines.Add("- 细原子与复合过渡混合：$mixed 张")
$lines.Add("- 仅复合过渡：$compositeOnly 张")
$lines.Add("- 仅入清单 / 待迁移：$catalogOnly 张")
$lines.Add("- 细原子程序：$($programMatches.Count) 条；复合过渡路由：$($routeMatches.Count) 条")
$lines.Add('')
$lines.Add('| 卡号 | 卡名 | 赛季 | 阵营 | 类型 | 运行时层级 | 细原子时机 | 复合过渡时机 | 独立验收 |')
$lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($row in $rows) {
    $lines.Add("| $($row.Id) | $($row.Name) | $($row.Product) | $($row.Faction) | $($row.Type) | $($row.Runtime) | $($row.Fine) | $($row.Composite) | $($row.Review) |")
}

$target = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $ProjectRoot $OutputPath }
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.File]::WriteAllLines($target, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Card-effect review matrix exported: $target"
