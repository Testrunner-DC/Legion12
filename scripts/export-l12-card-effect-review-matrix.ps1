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
$batch6BReviewedCardIds = @('S02-06M2', 'S02-06S3', 'S02-06S4', 'S02-06S5')
$batch6CReviewedCardIds = @('S01-0014', 'S01-0015', 'S01-0119', 'S01-0419', 'S02-0306')
$batch6DReviewedCardIds = @('S01-0204', 'S01-0414', 'S01-0417')
$batch6EReviewedCardIds = @(
    'S01-0208', 'S01-0210', 'S01-0305', 'S01-0308', 'S01-0309',
    'S02-0202', 'S02-0203', 'S02-0205', 'S02-0601'
)
$batch6FReviewedCardIds = @(
    'S02-0602', 'S02-0604', 'S02-0606', 'S02-0609', 'S02-0610',
    'S02-0613', 'S02-0614', 'S02-0617', 'S02-0618', 'S02-06M2', 'S02-06D1'
)
$batch6GAReviewedCardIds = @(
    'S02-0102', 'S02-0304', 'S02-0305', 'S02-05M1', 'S02-06M1', 'S02-06S4'
)
$batch6GBReviewedCardIds = @('S02-0102')
$batch6IAReviewedCardIds = @(
    'S01-0115', 'S01-0301', 'S01-0304', 'S01-0309', 'S01-0405', 'S01-0409', 'S01-0413',
    'S02-0104', 'S02-0203', 'S02-0402', 'S02-0507', 'S02-0512', 'S02-0616'
)
$batch6IBReviewedCardIds = @(
    'S01-0001', 'S01-0112', 'S01-0115', 'S01-0207', 'S01-0210', 'S01-0303',
    'S01-0304', 'S01-0306', 'S01-0313', 'S01-0403', 'S01-0407', 'S02-0002',
    'S02-01S1', 'S02-0301', 'S02-0508', 'S02-0518', 'S02-0601', 'S02-0615'
)
$batch6JAReviewedCardIds = @(
    'S01-0101', 'S01-0102', 'S01-0103', 'S01-0108', 'S01-0110', 'S01-0111', 'S01-0112',
    'S01-0201', 'S01-0202', 'S01-0205', 'S01-0210', 'S01-0215', 'S01-0217', 'S01-0220',
    'S01-0313', 'S01-0316', 'S01-0317', 'S01-0402', 'S01-0403', 'S01-0406', 'S01-0408',
    'S01-0411', 'S01-0412', 'S01-0416', 'S01-0417', 'S02-0003', 'S02-0008', 'S02-0204',
    'S02-0303', 'S02-0401', 'S02-0402', 'S02-0404', 'S02-0501', 'S02-0502', 'S02-0505',
    'S02-0506', 'S02-0513', 'S02-0518', 'S02-0520', 'S02-0601', 'S02-0608', 'S02-0613',
    'S02-0617', 'S02-0619'
)
$batch6JBReviewedCardIds = @(
    'S01-0007', 'S01-0013', 'S01-0101', 'S01-0108', 'S01-0311',
    'S02-0001', 'S02-0012', 'S02-01M1', 'S01-01C1'
)
$batch6JCReviewedCardIds = @('S02-0102', 'S02-0403')
$batch6KAReviewedCardIds = @(
    'S01-0001', 'S01-0002', 'S01-0003', 'S01-0004', 'S01-0005', 'S01-0006', 'S01-0007',
    'S01-0008', 'S01-0009', 'S01-0010', 'S01-0011', 'S01-0012', 'S01-0013', 'S01-0014',
    'S01-0015', 'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
    'S01-00C1', 'S01-0101', 'S01-0102', 'S01-0103', 'S01-0104', 'S01-0105', 'S01-0106',
    'S01-0107', 'S01-0108', 'S01-0109', 'S01-0110', 'S01-0111', 'S01-0112', 'S01-0113',
    'S01-0114', 'S01-0115', 'S01-0116', 'S01-0117', 'S01-0118', 'S01-0119', 'S01-0120',
    'S01-01C1', 'S01-01D1', 'S01-01M1', 'S01-01M2', 'S01-DS01', 'S01-DS02', 'S01-DS03',
    'S01-DS04', 'S01-DS05', 'S01-DS06', 'S01-DS07', 'S01-DS08', 'S01-DS09', 'S01-DS10'
)
$batch6KAFixedCardIds = @(
    'S01-0001', 'S01-0020', 'S01-0021', 'S01-0105', 'S01-0116', 'S01-0120', 'S01-01D1', 'S01-01M1'
)
$batch6KAQuestionCardIds = @()
$batch6KBReviewedCardIds = @(
    'S01-0201','S01-0202','S01-0203','S01-0204','S01-0205','S01-0206','S01-0207',
    'S01-0208','S01-0209','S01-0210','S01-0211','S01-0212','S01-0213','S01-0214',
    'S01-0215','S01-0216','S01-0217','S01-0218','S01-0219','S01-0220','S01-0221',
    'S01-0222','S01-0223','S01-0224','S01-02C1','S01-02D1','S01-02M1','S01-02M2','S01-02M3',
    'S01-0301','S01-0302','S01-0303','S01-0304','S01-0305','S01-0306','S01-0307',
    'S01-0308','S01-0309','S01-0310','S01-0311','S01-0312','S01-0313','S01-0314',
    'S01-0315','S01-0316','S01-0317','S01-0318','S01-0319','S01-0320',
    'S01-03C1','S01-03D1','S01-03M1','S01-03M2'
)
$batch6KBFixedCardIds = @('S01-0201','S01-0216','S01-0218','S01-0219','S01-0224','S01-02D1','S01-0315','S01-03D1')
$batch6KBQuestionCardIds = @()
$batch6KCReviewedCardIds = @(
    'S01-0401','S01-0402','S01-0403','S01-0404','S01-0405','S01-0406','S01-0407',
    'S01-0408','S01-0409','S01-0410','S01-0411','S01-0412','S01-0413','S01-0414',
    'S01-0415','S01-0416','S01-0417','S01-0418','S01-0419','S01-0420',
    'S01-04C1','S01-04D1','S01-04M1','S01-04M2'
)
$batch6KCFixedCardIds = @('S01-0401','S01-0407','S01-0416','S01-0417','S01-0418','S01-0419','S01-04D1','S01-04M1','S01-04M2')
$batch6LAReviewedCardIds = @(
    'S02-0001','S02-0002','S02-0003','S02-0004','S02-0005','S02-0006','S02-0007',
    'S02-0008','S02-0009','S02-0010','S02-0011','S02-0012','S02-0013','S02-0014',
    'S02-0015','S02-0016','S02-0017','S02-0018','S02-0101','S02-0102','S02-0103',
    'S02-0104','S02-0105','S02-0106','S02-01M1','S02-01S1'
)
$batch6LAFixedCardIds = @('S02-0101','S02-01M1')
$batch6LAQuestionCardIds = @()
$batch6LBReviewedCardIds = @(
    'S02-0201','S02-0202','S02-0203','S02-0204','S02-0205','S02-0206','S02-0207','S02-02M1',
    'S02-0301','S02-0302','S02-0303','S02-0304','S02-0305','S02-0306','S02-0307','S02-03M1'
)
$batch6LBFixedCardIds = @('S02-0202','S02-0207','S02-0301','S02-03M1')
$batch6LCReviewedCardIds = @(
    'S02-0401','S02-0402','S02-0403','S02-0404','S02-0405','S02-0406','S02-04M1',
    'S02-0501','S02-0502','S02-0503','S02-0504','S02-0505','S02-0506','S02-0507',
    'S02-0508','S02-0509','S02-0510','S02-0511','S02-0512','S02-0513','S02-0514',
    'S02-0515','S02-0516','S02-0517','S02-0518','S02-0519','S02-0520','S02-0521',
    'S02-0522','S02-0523','S02-05M1','S02-05M2','S02-05C1','S02-05C1A','S02-05D1'
)
$batch6LCFixedCardIds = @(
    'S02-0405','S02-04M1','S02-0501','S02-0503','S02-0505','S02-0507','S02-0510',
    'S02-0513','S02-0514','S02-0520','S02-0521','S02-05M1','S02-05M2','S02-05D1'
)
$batch6LCQuestionCardIds = @()
$batch6LDReviewedCardIds = @(
    'S02-0601','S02-0602','S02-0603','S02-0604','S02-0605','S02-0606','S02-0607','S02-0608',
    'S02-0609','S02-0610','S02-0611','S02-0612','S02-0613','S02-0614','S02-0615','S02-0616',
    'S02-0617','S02-0618','S02-0619','S02-0620','S02-0621','S02-0622','S02-06C1','S02-06D1',
    'S02-06M1','S02-06M2','S02-06S1','S02-06S2','S02-06S3','S02-06S4','S02-06S5','S02-06S6',
    'S02-DS01','S02-DS02','S02-DS03','S02-DS04','S02-DS05','S02-DS06'
)
$batch6LDFixedCardIds = @(
    'S02-0603','S02-0604','S02-0605','S02-0620','S02-06M1','S02-06S4','S02-06S5','S02-06S6'
)
$batch6MFixedCardIds = @('S02-0008')

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
    if ($batch6BReviewedCardIds -contains $card.id) {
        $review += '；6B试炼完成触发链已验收'
    }
    if ($batch6CReviewedCardIds -contains $card.id) {
        $review += '；6C复合手牌战术独立段已验收'
    }
    if ($batch6DReviewedCardIds -contains $card.id) {
        $review += '；6D最后已知来源与权威区域事务已验收'
    }
    if ($batch6EReviewedCardIds -contains $card.id) {
        $review += '；6E公开位置结算重验与原子登场事务已验收'
    }
    if ($batch6FReviewedCardIds -contains $card.id) {
        $review += '；6F试炼推进公开事件与费用预付已验收'
    }
    if ($batch6GAReviewedCardIds -contains $card.id) {
        $review += '；6G-A可选触发声明、次数预留与独立段已验收'
    }
    if ($batch6GBReviewedCardIds -contains $card.id) {
        $review += '；6G-B隐藏展示延迟与独立抽牌段已验收'
    }
    if ($batch6IAReviewedCardIds -contains $card.id) {
        $review += '；6I-A原子可选触发公开声明与条件快照已验收'
    }
    if ($batch6IBReviewedCardIds -contains $card.id) {
        $review += '；6I-B阵亡/击杀后公开声明、费用预付与来源快照已验收'
    }
    if ($batch6JAReviewedCardIds -contains $card.id) {
        $review += '；6J-A登场/晋升登场公开声明、隐藏信息延迟与独立段已验收'
    }
    if ($batch6JBReviewedCardIds -contains $card.id) {
        $review += '；6J-B主动/响应/后续触发公开声明与合法结算Prompt分类已验收'
    }
    if ($batch6JCReviewedCardIds -contains $card.id) {
        $review += '；6J-C效果生成免费打出与私密区域事务已验收'
    }
    if ($batch6KAReviewedCardIds -contains $card.id) {
        $review += '；6K-A逐卡逐能力独立语义已验收'
    }
    if ($batch6KAFixedCardIds -contains $card.id) {
        $review += '（6K-A明确错误已修复）'
    }
    if ($batch6KAQuestionCardIds -contains $card.id) {
        $review += '（6K-A有疑点，见OPEN-QUESTIONS）'
    }
    if ($batch6KBReviewedCardIds -contains $card.id) {
        $review += '；6K-B逐卡逐能力独立语义已验收'
    }
    if ($batch6KBFixedCardIds -contains $card.id) {
        $review += '（6K-B明确错误已修复）'
    }
    if ($batch6KBQuestionCardIds -contains $card.id) {
        $review += '（6K-B有疑点，见OPEN-QUESTIONS）'
    }
    if ($batch6KCReviewedCardIds -contains $card.id) {
        $review += '；6K-C逐卡逐能力独立语义已验收'
    }
    if ($batch6KCFixedCardIds -contains $card.id) {
        $review += '（6K-C明确错误已修复）'
    }
    if ($batch6LAReviewedCardIds -contains $card.id) {
        $review += '；6L-A逐卡逐能力独立语义已验收'
    }
    if ($batch6LAFixedCardIds -contains $card.id) {
        $review += '（6L-A明确错误已修复）'
    }
    if ($batch6LAQuestionCardIds -contains $card.id) {
        $review += '（6L-A有疑点，见OPEN-QUESTIONS）'
    }
    if ($batch6LBReviewedCardIds -contains $card.id) {
        $review += '；6L-B逐卡逐能力独立语义已验收'
    }
    if ($batch6LBFixedCardIds -contains $card.id) {
        $review += '（6L-B明确错误已修复）'
    }
    if ($batch6LCReviewedCardIds -contains $card.id) {
        $review += '；6L-C逐卡逐能力独立语义已验收'
    }
    if ($batch6LCFixedCardIds -contains $card.id) {
        $review += '（6L-C明确错误已修复）'
    }
    if ($batch6LCQuestionCardIds -contains $card.id) {
        $review += '（6L-C有疑点，见OPEN-QUESTIONS）'
    }
    if ($batch6LDReviewedCardIds -contains $card.id) {
        $review += '；6L-D逐卡逐能力独立语义已验收'
    }
    if ($batch6LDFixedCardIds -contains $card.id) {
        $review += '（6L-D明确错误已修复）'
    }
    if ($batch6MFixedCardIds -contains $card.id) {
        $review += '（6M最终交叉审查明确错误已修复）'
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
$unroutedRuntime = @($unrouted | Where-Object { $_.Review.StartsWith('未进入原子路由（有实战入口）', [StringComparison]::Ordinal) })
$noRuntime = @($unrouted | Where-Object { $_.Review.StartsWith('无实战入口', [StringComparison]::Ordinal) })
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
