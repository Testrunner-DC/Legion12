[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Apply')][string]$Mode = 'Plan',
    [Parameter(Mandatory)][string]$PolicyPath,
    [string]$ReceiptDirectory = 'D:\GPT\Legion12\artifacts\cloud-storage\pipeline',
    [string]$CredentialPath = 'E:\Legion12-ColdStorage\private-baidu-api-124583157\oauth.dpapi',
    [switch]$AllowSourceRetirement,
    [ValidateRange(5, 3600)][int]$TimeBudgetSeconds = 300,
    [ValidateRange(5, 300)][int]$RequestTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
$script:ReceiptRoot = Assert-L12GeneratedPath -Path $ReceiptDirectory -Purpose 'cloud retention receipts'
if (-not (Test-Path -LiteralPath $script:ReceiptRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $script:ReceiptRoot -Force | Out-Null
}

function Assert-PipelinePath {
    param([Parameter(Mandatory)][string]$Path, [switch]$RequireE)
    $physical = Assert-L12GeneratedPath -Path $Path -Purpose 'cloud retention input'
    if ([IO.Path]::GetPathRoot($physical) -eq 'C:\') { throw 'L12_CLOUD_PIPELINE_C_PATH' }
    if ($RequireE -and [IO.Path]::GetPathRoot($physical) -ne 'E:\') { throw 'L12_CLOUD_PIPELINE_E_REQUIRED' }
    return [IO.Path]::GetFullPath($physical).TrimEnd('\')
}

function Read-PipelinePolicy {
    $path = Assert-PipelinePath $PolicyPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'L12_CLOUD_PIPELINE_POLICY_MISSING' }
    $policy = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($policy.schema -ne 1 -or $policy.status -cne 'active' -or $policy.entries -isnot [array] -or @($policy.entries).Count -lt 1 -or @($policy.entries).Count -gt 32) {
        throw 'L12_CLOUD_PIPELINE_POLICY_INVALID'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in @($policy.entries)) {
        if ($entry.id -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.id) -or -not $seen.Add($entry.id)) { throw 'L12_CLOUD_PIPELINE_ENTRY_ID' }
        if ($entry.enabled -isnot [bool]) { throw 'L12_CLOUD_PIPELINE_ENTRY_ENABLED' }
        if (-not $entry.enabled) { continue }
        foreach ($name in @('manifestPath', 'transferDirectory', 'sourceArchivePath')) {
            if ($entry.$name -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.$name)) { throw ('L12_CLOUD_PIPELINE_' + $name + '_REQUIRED') }
        }
        if ($entry.manifestSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $entry.sourceArchiveSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $entry.sourceArchiveBytes -isnot [long] -and $entry.sourceArchiveBytes -isnot [int] -or [long]$entry.sourceArchiveBytes -le 0) { throw 'L12_CLOUD_PIPELINE_BINDING_INVALID' }
        if ($entry.retireSourceAfterCloudProof -isnot [bool]) { throw 'L12_CLOUD_PIPELINE_RETIRE_FLAG' }
        $originalManifestProperty = $entry.PSObject.Properties['expectedOriginalManifestSha256']
        if ($null -ne $originalManifestProperty -and $originalManifestProperty.Value -and [string]$originalManifestProperty.Value -notmatch '^[0-9a-fA-F]{64}$') { throw 'L12_CLOUD_PIPELINE_ORIGINAL_MANIFEST_SHA' }
        $manifest = Assert-PipelinePath $entry.manifestPath -RequireE
        $transfer = Assert-PipelinePath $entry.transferDirectory -RequireE
        $source = Assert-PipelinePath $entry.sourceArchivePath
        $transfer = $transfer.TrimEnd('\')
        if ($source.Equals($manifest, [StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($transfer + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'L12_CLOUD_PIPELINE_SOURCE_OVERLAP'
        }
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf) -or -not (Test-Path -LiteralPath $transfer -PathType Container) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw ('L12_CLOUD_PIPELINE_INPUT_MISSING_' + $entry.id)
        }
    }
    if (@($policy.entries | Where-Object { $_.enabled -and $_.retireSourceAfterCloudProof }).Count -and -not $AllowSourceRetirement -and $Mode -eq 'Apply') {
        throw 'L12_CLOUD_PIPELINE_RETIREMENT_REQUIRES_EXPLICIT_SWITCH'
    }
    return $policy
}

function Get-BoundFile([string]$Path, [string]$ExpectedSha256, [long]$ExpectedBytes) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Length -ne $ExpectedBytes) { throw 'L12_CLOUD_PIPELINE_SOURCE_BYTES' }
    $sha = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sha -cne $ExpectedSha256.ToLowerInvariant()) { throw 'L12_CLOUD_PIPELINE_SOURCE_SHA' }
    return [pscustomobject]@{ path = $item.FullName; bytes = $item.Length; sha256 = $sha }
}

function Write-PipelineReceipt($Value, [string]$Name) {
    $path = Join-Path $script:ReceiptRoot $Name
    $temp = $path + '.partial-' + [Guid]::NewGuid().ToString('N')
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temp -Encoding utf8
    Move-Item -LiteralPath $temp -Destination $path -Force
    return $path
}

$runId = [Guid]::NewGuid().ToString('N')
$lockPath = Join-Path $script:ReceiptRoot 'pipeline.lock'
$lock = $null
$results = [Collections.Generic.List[object]]::new()
try {
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $policy = Read-PipelinePolicy
    foreach ($entry in @($policy.entries | Where-Object enabled)) {
        $sourcePath = Assert-PipelinePath $entry.sourceArchivePath
        $source = Get-BoundFile $sourcePath ([string]$entry.sourceArchiveSha256) ([long]$entry.sourceArchiveBytes)
        $manifestPath = Assert-PipelinePath $entry.manifestPath -RequireE
        $transferDirectory = Assert-PipelinePath $entry.transferDirectory -RequireE
        $result = [pscustomobject][ordered]@{ id = $entry.id; source = $source; manifestPath = $manifestPath; transferDirectory = $transferDirectory; mode = $Mode; status = 'source-bound'; cloudVerified = $false; cloudReceipt = $null; uploadStatus = $null; downloadStatus = $null; sourceRetired = $false; sourceRetirementAllowed = [bool]$AllowSourceRetirement; retiredSha256 = $null }
        $results.Add($result)
        if ($Mode -eq 'Plan') {
            $result.status = 'planned-cloud-upload-verify'
            continue
        }

        Import-Module (Join-Path $PSScriptRoot 'L12BaiduNetdisk.psm1') -Force
        $common = @{ ManifestPath = $manifestPath; ExpectedManifestSha256 = ([string]$entry.manifestSha256).ToLowerInvariant(); TransferDirectory = $transferDirectory; CredentialPath = (Assert-PipelinePath $CredentialPath -RequireE); TimeBudgetSeconds = $TimeBudgetSeconds; RequestTimeoutSeconds = $RequestTimeoutSeconds }
        $originalManifestProperty = $entry.PSObject.Properties['expectedOriginalManifestSha256']
        if ($null -ne $originalManifestProperty -and $originalManifestProperty.Value) { $common.ExpectedOriginalManifestSha256 = ([string]$originalManifestProperty.Value).ToLowerInvariant() }
        $upload = Invoke-L12BaiduArchiveUpload @common
        $download = Invoke-L12BaiduArchiveDownload @common
        $cloudReceiptPath = Join-Path $transferDirectory 'cloud-receipt.json'
        if (-not (Test-Path -LiteralPath $cloudReceiptPath -PathType Leaf)) { throw 'L12_CLOUD_PIPELINE_RECEIPT_MISSING' }
        $cloudReceipt = Get-Content -LiteralPath $cloudReceiptPath -Raw | ConvertFrom-Json
        if ($cloudReceipt.cloudProvenanceVerified -ne $true -or $cloudReceipt.status -notin @('cloud-cipher-roundtrip-verified', 'cloud-volume-cipher-roundtrip-verified')) { throw 'L12_CLOUD_PIPELINE_CLOUD_PROOF_REQUIRED' }
        $result.cloudVerified = $true
        $result.cloudReceipt = $cloudReceiptPath
        $result.uploadStatus = $upload.status
        $result.downloadStatus = $download.status
        if ($entry.retireSourceAfterCloudProof -and $AllowSourceRetirement) {
            $beforeRetirement = Get-BoundFile $sourcePath ([string]$entry.sourceArchiveSha256) ([long]$entry.sourceArchiveBytes)
            Remove-Item -LiteralPath $sourcePath -Force
            if (Test-Path -LiteralPath $sourcePath) { throw 'L12_CLOUD_PIPELINE_SOURCE_RETIREMENT_FAILED' }
            $result.sourceRetired = $true
            $result.status = 'cloud-verified-source-retired'
            $result.retiredSha256 = $beforeRetirement.sha256
        } else { $result.status = 'cloud-verified-source-retained' }
    }
    $status = if ($Mode -eq 'Plan') { 'plan-created' } elseif (@($results | Where-Object { $_.status -like '*retired' }).Count -gt 0) { 'cloud-verified-and-retired' } else { 'cloud-verified-source-retained' }
    $receipt = [ordered]@{ schema = 1; runId = $runId; status = $status; mode = $Mode; policyPath = (Assert-PipelinePath $PolicyPath); entries = @($results); sourceRetirementRequiresExplicitSwitch = $true; createdUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    $receiptPath = Write-PipelineReceipt $receipt ("pipeline-$runId.json")
    [pscustomobject]@{ status = $status; receiptPath = $receiptPath; entries = $results.Count; sourceRetired = @($results | Where-Object sourceRetired).Count }
} catch {
    $failure = [ordered]@{ schema = 1; runId = $runId; status = 'failed-preserved'; mode = $Mode; error = $_.Exception.Message; partialResults = @($results); sourceRetirementRequiresExplicitSwitch = $true; createdUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    $path = Write-PipelineReceipt $failure ("pipeline-$runId.failure.json")
    throw ("L12_CLOUD_PIPELINE_FAILED receipt=" + $path + ' error=' + $_.Exception.Message)
} finally { if ($lock) { $lock.Dispose() } }
