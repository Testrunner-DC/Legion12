[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$CardAssetRoot,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repo 'ops/windows/L12GeneratedStorage.ps1')
& (Join-Path $repo 'ops/windows/Initialize-L12BuildEnvironment.ps1') | Out-Null
if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Canonical CI commit is invalid' }
# Preserve the lexical leaf: the read-only input verifier must still be able to
# reject a restored root link, rather than receiving its already-resolved target.
$CardAssetRoot = [IO.Path]::GetFullPath($CardAssetRoot)
$null = Assert-L12GeneratedPath -Path $CardAssetRoot -Purpose 'canonical CI trusted input'
$OutputRoot = Assert-L12GeneratedPath -Path $OutputRoot -Purpose 'canonical CI package output'
if (Test-Path -LiteralPath $OutputRoot) { throw 'Canonical CI needs a fresh package output; preserve previous failure evidence' }
$lock = Join-Path $repo 'ops/ci/l12-card-assets-input.lock.json'
$assetTool = Join-Path $repo 'ops/ci/l12-card-assets-input.py'
$pythonCommand = Get-Command -Name @('python3','python') -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $pythonCommand) { throw 'Canonical CI requires Python' }
$python = $pythonCommand.Source
$log = Join-Path $env:L12_ARTIFACT_ROOT 'canonical-native.log'
$null = Assert-L12GeneratedPath -Path $log -Purpose 'canonical CI native log'
New-Item -ItemType Directory -Path $env:L12_ARTIFACT_ROOT -Force | Out-Null
try {
    if ((& git -C $repo rev-parse HEAD).Trim() -cne $Commit -or $LASTEXITCODE -ne 0) { throw 'Canonical CI checkout mismatch' }
    if (Test-Path -LiteralPath $CardAssetRoot) {
        & $python -I -B $assetTool verify --lock $lock --input-root $CardAssetRoot 2>&1 | Tee-Object -FilePath $log
    } else {
        New-Item -ItemType Directory -Path (Split-Path -Parent $CardAssetRoot) -Force | Out-Null
        & $python -I -B $assetTool prepare --lock $lock --output-root $CardAssetRoot --from-web 2>&1 | Tee-Object -FilePath $log
    }
    if ($LASTEXITCODE -ne 0) { throw 'Canonical CI trusted input rejected; no cache repair or fallback' }
    # A fresh, actual initialized host captures all native output (including
    # Out-Host inside the one owner). No hand tar/publish, test filter or skip flag.
    & $env:L12_PWSH -NoProfile -NonInteractive -File (Join-Path $repo 'ops/windows/verify-l12.ps1') `
        -OutputDirectory $OutputRoot -CacheRoot $env:L12_WORK_CACHE -CardAssetDirectory $CardAssetRoot 2>&1 |
        Tee-Object -FilePath $log -Append
    $canonicalExit = $LASTEXITCODE
    if ($canonicalExit -ne 0) { throw "Canonical CI verification failed with exit $canonicalExit" }
    $qualified = & $python -I -B (Join-Path $repo 'ops/ci/l12-canonical-artifacts.py') `
        --output-root $OutputRoot --commit $Commit --lock $lock
    if ($LASTEXITCODE -ne 0) { throw 'Canonical CI output manifest or archives rejected' }
    $qualified = $qualified | ConvertFrom-Json
    foreach ($name in @('manifest','releaseArchive','cardAssetsArchive')) {
        $value = [string]$qualified.$name
        if ([string]::IsNullOrWhiteSpace($value) -or $value.Contains("`n") -or $value.Contains("`r")) { throw 'Invalid canonical output binding' }
        if ($env:GITHUB_OUTPUT) { "$name=$value" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append }
    }
} catch {
    $tail = ($_ | Out-String)
    if (Test-Path -LiteralPath $log) { $tail += (Get-Content -LiteralPath $log -Tail 24) -join "`n" }
    $tailBytes = [Text.Encoding]::UTF8.GetBytes($tail)
    if ($tailBytes.Length -gt 3000) {
        $start = $tailBytes.Length - 3000
        while (($tailBytes[$start] -band 0xC0) -eq 0x80) { $start++ }
        $tail = [Text.Encoding]::UTF8.GetString($tailBytes, $start, $tailBytes.Length - $start)
    }
    $tail = $tail.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
    Write-Host "::error title=L12 canonical actual failure::$tail"
    throw
}
