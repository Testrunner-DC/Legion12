[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SnapshotPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$ReceiptPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$snapshot = (Resolve-Path -LiteralPath $SnapshotPath).Path
$receiptParent = Split-Path -Parent ([IO.Path]::GetFullPath($ReceiptPath))
if (-not (Test-Path -LiteralPath $receiptParent -PathType Container)) {
    New-Item -ItemType Directory -Path $receiptParent | Out-Null
}
if (Test-Path -LiteralPath $ReceiptPath) {
    throw "Refusing to overwrite an existing performance receipt: $ReceiptPath"
}

$verifier = Join-Path $repositoryRoot 'opcgpro-vue\scripts\verify-performance-snapshot.mjs'
Get-Content -LiteralPath $snapshot -Raw |
    & node $verifier --expected-commit $ExpectedCommit --receipt $ReceiptPath
if ($LASTEXITCODE -ne 0) { throw "Testrun performance acceptance failed." }

Write-Host "Testrun performance acceptance passed; receipt: $ReceiptPath"
