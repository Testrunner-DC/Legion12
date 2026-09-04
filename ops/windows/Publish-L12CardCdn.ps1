param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$CatalogVersion,
    [string]$ProjectRoot = '',
    [string]$BaseUrl = '/card-assets',
    [string[]]$CardIds = @(),
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'
if ($Upload -and $CardIds.Count -gt 0) { throw '指定部分卡牌仅用于本地诊断，不得上传。' }
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
}
$frontend = Join-Path $ProjectRoot 'opcgpro-vue'
$catalogRoot = Join-Path $ProjectRoot '服务端WebSocket\TwelveLegions\Data'
if (-not (Test-Path -LiteralPath (Join-Path $catalogRoot 'cards.s1.json') -PathType Leaf)) {
    throw "找不到 L12 权威卡牌目录：$catalogRoot"
}
$catalogFiles = (@((Join-Path $catalogRoot 'cards.s1.json'), (Join-Path $catalogRoot 'cards.s2.json'), (Join-Path $catalogRoot 'cards.st.json')) -join ';')
$presentationCatalog = Join-Path $catalogRoot 'card-archive-assets.json'

if ($SourceDirectory.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase) -or
    $OutputDirectory.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '卡图源目录和输出目录不得位于 C 盘。'
}

Push-Location $frontend
try {
    $arguments = @(
        '.\scripts\build-l12-card-cdn.mjs', '--source', $SourceDirectory, '--output', $OutputDirectory,
        '--catalog-version', $CatalogVersion, '--base-url', $BaseUrl, '--catalog-files', $catalogFiles,
        '--presentation-catalog', $presentationCatalog
    )
    if ($CardIds.Count -gt 0) { $arguments += @('--card-ids', ($CardIds -join ';')) }
    & node @arguments
    if ($LASTEXITCODE -ne 0) { throw "卡图资源生成失败，退出码：$LASTEXITCODE。" }
}
finally { Pop-Location }

if (!$Upload) {
    Write-Host '卡图资源与清单已生成；本次未请求上传。'
    exit 0
}

foreach ($name in @('CLOUDFLARE_ACCOUNT_ID', 'R2_BUCKET', 'AWS_ACCESS_KEY_ID', 'AWS_SECRET_ACCESS_KEY')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "上传前必须配置环境变量：$name。"
    }
}
if ($null -eq (Get-Command aws -ErrorAction SilentlyContinue)) { throw '上传前必须安装 AWS CLI。' }
$endpoint = "https://$env:CLOUDFLARE_ACCOUNT_ID.r2.cloudflarestorage.com"
& aws s3 sync $OutputDirectory "s3://$env:R2_BUCKET" --endpoint-url $endpoint `
    --exclude 'card-assets.manifest.json' --exclude 'card-assets.preload.json' `
    --cache-control 'public,max-age=31536000,immutable'
if ($LASTEXITCODE -ne 0) { throw 'R2 不可变资源上传失败。' }
foreach ($manifest in @('card-assets.manifest.json', 'card-assets.preload.json')) {
    & aws s3 cp (Join-Path $OutputDirectory $manifest) "s3://$env:R2_BUCKET/$manifest" `
        --endpoint-url $endpoint --content-type 'application/json; charset=utf-8' --cache-control 'public,max-age=300'
    if ($LASTEXITCODE -ne 0) { throw "$manifest 上传失败。" }
}
Write-Host "R2 上传完成：$BaseUrl/card-assets.manifest.json"
