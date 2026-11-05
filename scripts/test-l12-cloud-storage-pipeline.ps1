[CmdletBinding()]
param(
    [string]$EvidenceRoot = 'D:\GPT\Legion12\artifacts\cloud-storage\pipeline-tests',
    [string]$ReferenceManifestPath = 'E:\Legion12-ColdStorage\codex-conversation-archive-20261102\manifest.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'ops\windows\L12GeneratedStorage.ps1')
$root = Assert-L12GeneratedPath -Path ($EvidenceRoot + '-' + [Guid]::NewGuid().ToString('N')) -Purpose 'cloud pipeline test evidence'
$manifest = Assert-L12GeneratedPath -Path $ReferenceManifestPath -Purpose 'cloud pipeline test manifest'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'L12_TEST_REFERENCE_MANIFEST_MISSING' }
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
New-Item -ItemType Directory -Path $root -Force | Out-Null

try {
    $source = Join-Path $root 'source.age'
    [IO.File]::WriteAllBytes($source, [Text.Encoding]::UTF8.GetBytes('synthetic source bound to this test'))
    $sourceBytes = (Get-Item -LiteralPath $source).Length
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $policy = [ordered]@{
        schema = 1
        status = 'active'
        entries = @([ordered]@{
            id = 'synthetic-plan'
            enabled = $true
            manifestPath = $manifest
            manifestSha256 = $manifestHash
            transferDirectory = (Split-Path -Parent $manifest)
            sourceArchivePath = $source
            sourceArchiveSha256 = $sourceHash
            sourceArchiveBytes = [long]$sourceBytes
            retireSourceAfterCloudProof = $true
        })
    }
    $policyPath = Join-Path $root 'policy.json'
    $policy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyPath -Encoding utf8
    $pipeline = Join-Path $repo 'ops\windows\Invoke-L12CloudStoragePipeline.ps1'
    $receiptRoot = Join-Path $root 'receipts'

    $plan = & $pipeline -Mode Plan -PolicyPath $policyPath -ReceiptDirectory $receiptRoot
    if ($plan.status -ne 'plan-created') { throw 'L12_TEST_PLAN_STATUS' }
    $planReceipt = Get-Content -LiteralPath $plan.receiptPath -Raw | ConvertFrom-Json
    if ($planReceipt.entries.Count -ne 1 -or $planReceipt.entries[0].status -ne 'planned-cloud-upload-verify') {
        throw 'L12_TEST_PLAN_RECEIPT'
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'L12_TEST_PLAN_DELETED_SOURCE' }

    try {
        & $pipeline -Mode Apply -PolicyPath $policyPath -ReceiptDirectory $receiptRoot | Out-Null
        throw 'L12_TEST_APPLY_RETIREMENT_GUARD_MISSING'
    } catch {
        if ($_.Exception.Message -notmatch 'RETIREMENT_REQUIRES_EXPLICIT_SWITCH') {
            throw
        }
    }
    $failure = @(Get-ChildItem -LiteralPath $receiptRoot -Filter '*.failure.json' -File)
    if ($failure.Count -ne 1) { throw 'L12_TEST_FAILURE_RECEIPT_MISSING' }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'L12_TEST_APPLY_DELETED_SOURCE' }
    [pscustomobject]@{
        status = 'passed'
        planReceipt = $plan.receiptPath
        failureReceipt = $failure[0].FullName
        sourceRetained = $true
        onlineTransfer = $false
    }
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
