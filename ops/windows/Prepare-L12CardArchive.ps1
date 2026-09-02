param(
    [string]$ArchiveDirectory = 'D:\L12-assets\original',
    [string]$ProjectRoot = '',
    [switch]$Refresh
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
}
if ($ArchiveDirectory.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '卡图原图归档不得存放在 C 盘。'
}

$catalogRoot = (Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter 'cards.s1.json' -File |
    Where-Object { $_.DirectoryName -like '*TwelveLegions*Data' } |
    Select-Object -First 1).DirectoryName
if ([string]::IsNullOrWhiteSpace($catalogRoot)) { throw '找不到 L12 权威卡牌目录。' }
$publicRoot = Join-Path $ProjectRoot 'opcgpro-vue\public'
$cards = [System.Collections.Generic.List[object]]::new()
foreach ($catalogName in @('cards.s1.json', 'cards.s2.json', 'cards.st.json')) {
    $catalogCards = Get-Content -Raw -Encoding utf8 -LiteralPath (Join-Path $catalogRoot $catalogName) | ConvertFrom-Json
    foreach ($catalogCard in $catalogCards) { $cards.Add($catalogCard) }
}
$uniqueCardIds = @($cards | ForEach-Object { [string]$_.id } | Sort-Object -Unique)
if ($cards.Count -ne 324 -or $uniqueCardIds.Count -ne 324) {
    throw "权威卡牌目录必须为 324 张唯一卡号，当前 total=$($cards.Count) unique=$($uniqueCardIds.Count)"
}

New-Item -ItemType Directory -Force -Path $ArchiveDirectory | Out-Null
$downloaded = 0
$copied = 0
$retained = 0
$failures = [System.Collections.Generic.List[object]]::new()
$current = 0

foreach ($card in $cards) {
    $current++
    $destination = Join-Path $ArchiveDirectory "$($card.id).png"
    if (!$Refresh -and (Test-Path -LiteralPath $destination -PathType Leaf) -and
        (Get-Item -LiteralPath $destination).Length -gt 0) {
        $retained++
        continue
    }

    $imageUrl = [string]$card.imageUrl
    try {
        if ($imageUrl.StartsWith('/', [System.StringComparison]::Ordinal)) {
            $relative = [System.Uri]::UnescapeDataString($imageUrl.TrimStart('/')).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $source = Join-Path $publicRoot $relative
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "找不到本地原图：$source" }
            Copy-Item -LiteralPath $source -Destination $destination -Force
            $copied++
        }
        elseif ($imageUrl.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase)) {
            $temporary = "$destination.partial"
            try {
                Write-Host "[$current/$($cards.Count)] 正在下载 $($card.id) $($card.nameZh)"
                & curl.exe --fail --location --silent --show-error --retry 2 --connect-timeout 15 --max-time 90 `
                    --user-agent 'Legion12-CardArchive/1.0' --output $temporary $imageUrl
                if ($LASTEXITCODE -ne 0) { throw "curl 下载失败，退出码：$LASTEXITCODE" }
                if (!(Test-Path -LiteralPath $temporary -PathType Leaf) -or (Get-Item -LiteralPath $temporary).Length -eq 0) {
                    throw '下载结果为空文件。'
                }
                Move-Item -LiteralPath $temporary -Destination $destination -Force
                $downloaded++
            }
            finally {
                if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
            }
        }
        else { throw "不支持的图片地址：$imageUrl" }
    }
    catch {
        $failures.Add([pscustomobject]@{ CardId = $card.id; Name = $card.nameZh; Error = $_.Exception.Message })
    }
}

Write-Host "原图归档完成：复用=$retained 本地复制=$copied 下载=$downloaded 总数=$($cards.Count)"
if ($failures.Count -gt 0) {
    $failures | Format-Table -AutoSize | Out-String | Write-Host
    throw "卡图原图归档不完整：$($failures.Count) 个来源失败。"
}
$archivedIds = @(Get-ChildItem -LiteralPath $ArchiveDirectory -File |
    Where-Object Extension -In @('.png', '.jpg', '.jpeg', '.webp', '.avif') |
    ForEach-Object BaseName)
$missingIds = @($uniqueCardIds | Where-Object { $_ -notin $archivedIds })
if ($missingIds.Count -gt 0) { throw "D 盘原图归档缺少 $($missingIds.Count) 张：$($missingIds -join '、')" }
Write-Host "D 盘原图归档完整性通过：324/324（$ArchiveDirectory）"
