param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = 'docs/l12/CARD-EFFECT-REVIEW-MATRIX.md'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/l12-card-runtime-evidence.ps1')
$sourcePath = Join-Path $ProjectRoot '服务端WebSocket/TwelveLegions'
$dataPath = Join-Path $sourcePath 'Data'
$atomicSource = [System.IO.File]::ReadAllText((Join-Path $sourcePath 'AtomicEffects.cs'), [System.Text.Encoding]::UTF8)
$routeSource = [System.IO.File]::ReadAllText((Join-Path $sourcePath 'L12RuntimeEffectRoutes.cs'), [System.Text.Encoding]::UTF8)
$programMatches = [regex]::Matches($atomicSource,
    'Program\("(?<id>S\d{2}-[A-Za-z0-9]+)"\s*,\s*"(?<trigger>[^"]+)"')
$routeMatches = [regex]::Matches($routeSource,
    'new\("(?<id>S\d{2}-[A-Za-z0-9]+)"\s*,\s*"(?<trigger>[^"]+)"')

function Group-TriggersByCard([System.Text.RegularExpressions.MatchCollection]$matches) {
    $grouped = @{}
    foreach ($match in $matches) {
        $id = $match.Groups['id'].Value
        if (-not $grouped.ContainsKey($id)) { $grouped[$id] = New-Object System.Collections.Generic.List[string] }
        $grouped[$id].Add($match.Groups['trigger'].Value)
    }
    return $grouped
}
$fineByCard = Group-TriggersByCard $programMatches
$compositeByCard = Group-TriggersByCard $routeMatches

$cards = New-Object System.Collections.Generic.List[object]
foreach ($fileName in @('cards.s1.json', 'cards.s2.json')) {
    $decoded = [System.IO.File]::ReadAllText((Join-Path $dataPath $fileName), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($card in $decoded) { $cards.Add($card) }
}
$runtimeEvidence = Get-L12CardRuntimeEvidence -ProjectRoot $ProjectRoot -Cards $cards
$batch6HReviewedCardIds = @(
    'S01-0104', 'S01-0106', 'S01-0203', 'S01-0208', 'S01-0301', 'S01-0306', 'S01-0311',
    'S01-0402', 'S01-0405', 'S01-0406', 'S01-0408', 'S01-0413', 'S01-0416', 'S02-0103',
    'S02-0509', 'S02-0511', 'S02-0517', 'S02-0519', 'S02-0605', 'S02-0606', 'S02-0607',
    'S02-0608', 'S02-0612', 'S02-0617'
)

$rows = foreach ($card in ($cards | Sort-Object id)) {
    $fine = if ($fineByCard.ContainsKey($card.id)) { @($fineByCard[$card.id] | Sort-Object -Unique) } else { @() }
    $composite = if ($compositeByCard.ContainsKey($card.id)) { @($compositeByCard[$card.id] | Sort-Object -Unique) } else { @() }
    $evidence = $runtimeEvidence[$card.id]
    $route = if ($fine.Count -gt 0 -and $composite.Count -gt 0) {
        '混合：细原子 + 复合过渡'
    } elseif ($fine.Count -gt 0) {
        '细原子已验证'
    } elseif ($composite.Count -gt 0) {
        '复合过渡'
    } else {
        '未进入原子路由'
    }
    $review = if ($fine.Count -gt 0 -or $composite.Count -gt 0) {
        '已入原子路由；逐能力待验收'
    } elseif ($evidence.Sources.Count -gt 0) {
        '未进入原子路由（有实战入口）'
    } else {
        '无实战入口'
    }
    if ($batch6HReviewedCardIds -contains $card.id) {
        $review += '；6H进攻公开声明已验收'
    }
    [pscustomobject]@{
        Id = $card.id
        Name = ($card.nameZh -replace '\|', '\|')
        Product = $card.product
        Faction = $card.faction
        Type = $card.cardType
        Route = $route
        Fine = if ($fine.Count) { $fine -join '、' } else { '—' }
        Composite = if ($composite.Count) { $composite -join '、' } else { '—' }
        Runtime = if ($evidence.Categories.Count) { $evidence.Categories -join '、' } else { '—' }
        RuntimeSources = if ($evidence.Sources.Count) { $evidence.Sources -join '、' } else { '—' }
        Tests = if ($evidence.Tests.Count) { $evidence.Tests -join '、' } else { '—' }
        Review = $review
    }
}

$fineOnly = @($rows | Where-Object Route -eq '细原子已验证').Count
$mixed = @($rows | Where-Object Route -eq '混合：细原子 + 复合过渡').Count
$compositeOnly = @($rows | Where-Object Route -eq '复合过渡').Count
$unrouted = @($rows | Where-Object Route -eq '未进入原子路由')
$unroutedRuntime = @($unrouted | Where-Object Review -eq '未进入原子路由（有实战入口）')
$noRuntime = @($unrouted | Where-Object Review -eq '无实战入口')
$unroutedWithTests = @($unrouted | Where-Object Tests -ne '—')
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# 248 张卡效独立审查矩阵')
$lines.Add('')
$lines.Add("生成日期：$(Get-Date -Format 'yyyy-MM-dd')")
$lines.Add('')
$lines.Add('本表把“是否进入原子路由”与“是否存在权威实战入口”分开。主动注册、时机集合、响应池、静态/派生规则、主宰/阵营与试炼/Token/特殊区都是真实运行入口；测试文件只是独立证据，不会单独把卡升级为“已实装”。`细原子已验证`仍只证明至少1项能力被接管。')
$lines.Add('')
$lines.Add("- 卡池：$($rows.Count) 张")
$lines.Add("- 仅细原子已验证：$fineOnly 张")
$lines.Add("- 细原子与复合过渡混合：$mixed 张")
$lines.Add("- 仅复合过渡：$compositeOnly 张")
$lines.Add("- 未进入原子路由：$($unrouted.Count) 张")
$lines.Add("  - 有权威实战入口：$($unroutedRuntime.Count) 张")
$lines.Add("  - 无实战入口：$($noRuntime.Count) 张")
$lines.Add("  - 已有至少1个自动测试文件证据：$($unroutedWithTests.Count) 张")
$lines.Add("- 细原子程序：$($programMatches.Count) 条；复合过渡路由：$($routeMatches.Count) 条")
$lines.Add('')
$lines.Add('| 卡号 | 卡名 | 赛季 | 阵营 | 类型 | 原子路由层级 | 细原子时机 | 复合过渡时机 | 其他权威实战入口 | 源码证据 | 测试证据 | 审查结论 |')
$lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($row in $rows) {
    $lines.Add("| $($row.Id) | $($row.Name) | $($row.Product) | $($row.Faction) | $($row.Type) | $($row.Route) | $($row.Fine) | $($row.Composite) | $($row.Runtime) | $($row.RuntimeSources) | $($row.Tests) | $($row.Review) |")
}

$target = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $ProjectRoot $OutputPath }
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.File]::WriteAllLines($target, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Card-effect review matrix exported: $target"
