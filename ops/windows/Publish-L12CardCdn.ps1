param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$CatalogVersion,
    [string]$ProjectRoot = '',
    [string]$BaseUrl = 'https://cards.legion12.grand-umi.com',
    [string[]]$CardIds = @(),
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
}
$frontend = Join-Path $ProjectRoot 'opcgpro-vue'
$catalogRoot = (Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter 'cards.s1.json' -File |
    Where-Object { $_.DirectoryName -like '*TwelveLegions*Data' } |
    Select-Object -First 1).DirectoryName
if ([string]::IsNullOrWhiteSpace($catalogRoot)) { throw 'Unable to locate the L12 card catalog.' }
$catalogFiles = (@((Join-Path $catalogRoot 'cards.s1.json'), (Join-Path $catalogRoot 'cards.s2.json')) -join ';')

if ($SourceDirectory.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase) -or
    $OutputDirectory.StartsWith('C:\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Card asset source and output directories must not be on drive C.'
}

Push-Location $frontend
try {
    $arguments = @(
        '.\scripts\build-l12-card-cdn.mjs', '--source', $SourceDirectory, '--output', $OutputDirectory,
        '--catalog-version', $CatalogVersion, '--base-url', $BaseUrl, '--catalog-files', $catalogFiles
    )
    if ($CardIds.Count -gt 0) { $arguments += @('--card-ids', ($CardIds -join ';')) }
    & node @arguments
    if ($LASTEXITCODE -ne 0) { throw "Card asset build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }

if (!$Upload) {
    Write-Host 'Card assets and manifests generated. Upload was not requested.'
    exit 0
}

foreach ($name in @('CLOUDFLARE_ACCOUNT_ID', 'R2_BUCKET', 'AWS_ACCESS_KEY_ID', 'AWS_SECRET_ACCESS_KEY')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Environment variable $name is required before upload."
    }
}
if ($null -eq (Get-Command aws -ErrorAction SilentlyContinue)) { throw 'AWS CLI is required for upload.' }
$endpoint = "https://$env:CLOUDFLARE_ACCOUNT_ID.r2.cloudflarestorage.com"
& aws s3 sync $OutputDirectory "s3://$env:R2_BUCKET" --endpoint-url $endpoint `
    --exclude 'card-assets.manifest.json' --exclude 'card-assets.preload.json' `
    --cache-control 'public,max-age=31536000,immutable'
if ($LASTEXITCODE -ne 0) { throw 'Immutable R2 asset upload failed.' }
foreach ($manifest in @('card-assets.manifest.json', 'card-assets.preload.json')) {
    & aws s3 cp (Join-Path $OutputDirectory $manifest) "s3://$env:R2_BUCKET/$manifest" `
        --endpoint-url $endpoint --content-type 'application/json; charset=utf-8' --cache-control 'public,max-age=300'
    if ($LASTEXITCODE -ne 0) { throw "$manifest upload failed." }
}
Write-Host "R2 upload completed: $BaseUrl/card-assets.manifest.json"
