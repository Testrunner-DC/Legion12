[CmdletBinding()]
param(
    [string]$CacheRoot = 'D:\GPT\Legion12\cache\primary'
)

# 前端 UI 契约链（npm run check:ui-contracts）里的 test-tutorial-contract.mjs 需要
# L12_ARTIFACT_ROOT / L12_WORK_CACHE 才能定位生成目录；不初始化就会以
# 「Initialize L12 generated storage, or select explicit tutorial output and cache roots」失败。
# 这个包装脚本让单独执行也能跑通（门禁内部本来就会初始化环境）。
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'ops\windows\Initialize-L12BuildEnvironment.ps1') -CacheRoot $CacheRoot | Out-Null
if (-not $env:L12_ARTIFACT_ROOT -or -not $env:L12_WORK_CACHE) {
    throw 'L12 generated storage roots were not initialized by Initialize-L12BuildEnvironment.ps1'
}
Push-Location (Join-Path $repoRoot 'opcgpro-vue')
try {
    & npm.cmd run check:ui-contracts
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
