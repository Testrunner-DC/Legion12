# 前向 schema 行为回归（D5 二期）：夹具驱动，证明历史条目豁免、新条目缺字段必红。
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$verifyScript = Join-Path $PSScriptRoot "verify-l12-doc-ledger.ps1"
if (-not (Test-Path -LiteralPath $verifyScript)) { throw "Missing $verifyScript" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("l12-doc-ledger-" + [guid]::NewGuid().ToString("N").Substring(0, 12))
$docsDir = Join-Path $tempRoot "docs"
$l12Dir = Join-Path $docsDir "l12"
New-Item -ItemType Directory -Force -Path $l12Dir | Out-Null
$registryPath = Join-Path $docsDir "BUGFIX-REGISTRY.md"
Set-Content -LiteralPath (Join-Path $docsDir "TASK-LEDGER.md") -Value "# 任务台账" -Encoding utf8
@{ duplicateEntryIds = 0; undatedEntries = 0; registryOutOfOrderDates = 0; ledgerOutOfOrderDates = 0 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $l12Dir "DOC-LEDGER-BASELINE.json") -Encoding utf8

function Invoke-Fixture {
    param([Parameter(Mandatory = $true)][string]$Registry)
    Set-Content -LiteralPath $registryPath -Value $Registry -Encoding utf8
    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $verifyScript -ProjectRoot $tempRoot 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

$legacyEntry = @(
    "## LEGACY-20260101-OLD-ENTRY｜历史条目（无 schema 字段）",
    "",
    "- 现象：历史写法，没有标签行。",
    "- 修复：远古提交里已经改过。"
) -join "`n"

$newEntry = @(
    "## PLATFORM-20261123-NEW-ENTRY｜新条目（字段齐全）",
    "",
    "- 状态：local_verified_not_deployed",
    "- 现象：可观察到的事实。",
    "- 根因：机制原因。",
    "- 同型扫描：全池检索零命中。",
    "- 改动：改了 store 的一处分支。",
    "- 回归：新增一条守卫用例。",
    "- 验证：门禁 Batch 通过。",
    "- 回滚：恢复旧分支即可。"
) -join "`n"

$incompleteEntry = @(
    "# CHECK-20261124-MISSING-ROOTCAUSE｜缺根因的新条目",
    "",
    "- 状态：local_fixed_not_deployed",
    "- 现象：有现象。",
    "- 同型扫描：无。",
    "- 改动：无。",
    "- 回归：无。",
    "- 验证：无。",
    "- 回滚：无。"
) -join "`n"

try {
    $legacy = Invoke-Fixture -Registry $legacyEntry
    if ($legacy.ExitCode -ne 0) {
        throw ("历史条目必须豁免，但门禁失败（exit {0}）：{1}" -f $legacy.ExitCode, $legacy.Output)
    }

    $complete = Invoke-Fixture -Registry $newEntry
    if ($complete.ExitCode -ne 0) {
        throw ("字段齐全的新条目必须通过，但门禁失败（exit {0}）：{1}" -f $complete.ExitCode, $complete.Output)
    }

    $incomplete = Invoke-Fixture -Registry $incompleteEntry
    if ($incomplete.ExitCode -eq 0) {
        throw "缺必填字段的新条目必须让门禁变红，但它通过了。"
    }
    if ($incomplete.Output -notmatch "CHECK-20261124-MISSING-ROOTCAUSE") {
        throw ("门禁失败信息必须点名条目 id，实际输出：{0}" -f $incomplete.Output)
    }
    if ($incomplete.Output -notmatch "根因") {
        throw ("门禁失败信息必须点名缺失字段，实际输出：{0}" -f $incomplete.Output)
    }

    Write-Host "[L12 doc ledger schema] historical exempt, complete entry passes, missing 根因 rejected with entry id."
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}