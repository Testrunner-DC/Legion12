[CmdletBinding()]
param(
    [ValidateSet('Plan','Apply','Verify')][string]$Mode = 'Plan',
    [Parameter(Mandatory)][string]$PolicyPath,
    [string]$ReceiptDirectory = 'D:\GPT\Legion12\artifacts\nas-storage\pipeline',
    [string]$ManifestDirectory = 'D:\GPT\Legion12\artifacts\nas-storage\manifests',
    [string]$GroupName,
    [ValidateRange(3,30)][int]$NasProbeTimeoutSeconds = 8,
    [switch]$AllowSourceRetirement
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')
$script:ReceiptRoot = Assert-L12GeneratedPath -Path $ReceiptDirectory -Purpose 'NAS pipeline receipts'
if (-not (Test-Path -LiteralPath $script:ReceiptRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $script:ReceiptRoot -Force | Out-Null
}
$script:ManifestRoot = Assert-L12GeneratedPath -Path $ManifestDirectory -Purpose 'NAS source manifests'
if (-not (Test-Path -LiteralPath $script:ManifestRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $script:ManifestRoot -Force | Out-Null
}

function Assert-NasRoot {
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -notmatch '^\\\\[^\\]+\\[^\\]+') { throw 'L12_NAS_DESTINATION_NOT_UNC' }
    if ($Path -match '(^|\\)\.\.($|\\)') { throw 'L12_NAS_DESTINATION_TRAVERSAL' }
    return $Path.TrimEnd('\')
}

function Get-NasShareRoot {
    param([Parameter(Mandatory)][string]$Path)
    $match = [regex]::Match($Path,'^\\\\[^\\]+\\[^\\]+')
    if (-not $match.Success) { return $null }
    return $match.Value
}

function Ensure-NasSession {
    param([Parameter(Mandatory)][string]$ShareRoot)
    $net = Join-Path $env:SystemRoot 'System32\net.exe'
    if (-not (Test-Path -LiteralPath $net -PathType Leaf)) { return $false }
    $proc = $null
    try {
        $proc = Start-Process -FilePath $net -ArgumentList @('use',$ShareRoot,'/persistent:no') -WindowStyle Hidden -PassThru
        if (-not $proc.WaitForExit($NasProbeTimeoutSeconds * 1000)) { $proc.Kill(); return $false }
        return $proc.ExitCode -eq 0
    } catch { return $false } finally { if ($proc) { $proc.Dispose() } }
}

$script:LastNasProbe = [ordered]@{}
function Test-NasRootAvailable {
    param([Parameter(Mandatory)][string]$Path)
    $share = Get-NasShareRoot $Path
    $server = if ($share) { ([regex]::Match($share,'^\\\\([^\\]+)\\').Groups[1].Value) } else { $null }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $script:LastNasProbe = [ordered]@{ server = $server; share = $share; identity = $identity; sessionName = $env:SESSIONNAME; mappingCount = 0; mappingStatus = @(); tcp445 = $false; error = $null; checkedUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    if ([string]::IsNullOrWhiteSpace($server)) { $script:LastNasProbe.error = 'L12_NAS_SHARE_INVALID'; return $false }
    $sessionReady = Ensure-NasSession $share
    $mapped = @()
    try { $mapped = @(Get-SmbMapping -ErrorAction SilentlyContinue | Where-Object { $_.RemotePath -like ('\\\\' + $server + '\\*') }) } catch { $script:LastNasProbe.error = $_.Exception.Message }
    $script:LastNasProbe.mappingCount = $mapped.Count
    $script:LastNasProbe.mappingStatus = @($mapped | ForEach-Object { [string]$_.Status })
    if ($mapped.Count -gt 0 -and @($mapped | Where-Object { [string]$_.Status -in @('0','OK') }).Count -gt 0) { $script:LastNasProbe.tcp445 = $true; return $true }
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $async = $client.BeginConnect($server,445,$null,$null)
        if (-not $async.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($NasProbeTimeoutSeconds))) { $script:LastNasProbe.error = 'L12_NAS_TCP445_TIMEOUT'; return $false }
        $client.EndConnect($async)
        $script:LastNasProbe.tcp445 = $client.Connected
        if (-not $sessionReady -and -not $client.Connected) { $script:LastNasProbe.error = 'L12_NAS_SESSION_UNAVAILABLE' }
        return $client.Connected
    } catch { $script:LastNasProbe.error = $_.Exception.Message; return $false } finally { $client.Dispose() }
}

function Assert-LocalSource {
    param([Parameter(Mandatory)][string]$Path)
    $physical = Assert-L12GeneratedPath -Path $Path -Purpose 'NAS pipeline source'
    if ([IO.Path]::GetPathRoot($physical) -eq 'C:\') { throw 'L12_NAS_SOURCE_ON_C' }
    return $physical.TrimEnd('\')
}

function Write-NasReceipt {
    param([Parameter(Mandatory)]$Value,[Parameter(Mandatory)][string]$Name)
    $path = Join-Path $script:ReceiptRoot $Name
    $partial = $path + '.partial-' + [Guid]::NewGuid().ToString('N')
    $Value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $partial -Encoding utf8
    Move-Item -LiteralPath $partial -Destination $path -Force
    return $path
}

function Read-NasPolicy {
    $path = Assert-L12GeneratedPath -Path $PolicyPath -Purpose 'NAS pipeline policy'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'L12_NAS_POLICY_MISSING' }
    $policy = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($policy.schema -ne 1 -or $policy.status -cne 'active' -or $policy.destinationRoot -isnot [string] -or $policy.entries -isnot [array]) {
        throw 'L12_NAS_POLICY_INVALID'
    }
    $destination = Assert-NasRoot $policy.destinationRoot
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $entries = @($policy.entries | Where-Object enabled)
    if ($entries.Count -lt 1 -or $entries.Count -gt 16) { throw 'L12_NAS_POLICY_ENTRY_COUNT' }
    foreach ($entry in $entries) {
        if ($entry.id -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.id) -or -not $seen.Add($entry.id)) { throw 'L12_NAS_POLICY_ENTRY_ID' }
        if ($entry.sourceRoot -isnot [string] -or $entry.destinationSubdirectory -isnot [string]) { throw 'L12_NAS_POLICY_ENTRY_PATH' }
        if ($entry.retireSourceAfterVerification -isnot [bool]) { throw 'L12_NAS_POLICY_RETIRE_FLAG' }
        $source = Assert-LocalSource $entry.sourceRoot
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw ('L12_NAS_SOURCE_MISSING_' + $entry.id) }
        $sub = [string]$entry.destinationSubdirectory
        if ($sub -match '(^|[\\/])\.\.($|[\\/])' -or [IO.Path]::IsPathRooted($sub)) { throw 'L12_NAS_DESTINATION_SUBPATH' }
        $entry | Add-Member -NotePropertyName resolvedSource -NotePropertyValue $source -Force
        $entry | Add-Member -NotePropertyName resolvedDestination -NotePropertyValue (Join-Path $destination $sub.Replace('/','\')) -Force
    }
    $layout = if ($policy.layout -ceq 'root') { 'root' } else { 'grouped' }
    if ($layout -ceq 'root' -and $policy.allowExistingRootMerge -ne $true) { throw 'L12_NAS_ROOT_MERGE_NOT_APPROVED' }
    [pscustomobject]@{ raw = $policy; destination = $destination; entries = $entries; layout = $layout }
}

function Get-NasManifest {
    param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$EntryId)
    $files = [Collections.Generic.List[object]]::new()
    $totalBytes = [long]0
    $items = Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force -ErrorAction Stop | Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }
    foreach ($item in $items) {
        $relative = $item.FullName.Substring($SourceRoot.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $files.Add([pscustomobject][ordered]@{ path = $relative; bytes = [long]$item.Length; sha256 = $hash })
        $totalBytes += [long]$item.Length
    }
    [pscustomobject][ordered]@{ schema = 1; entryId = $EntryId; sourceRoot = $SourceRoot; fileCount = $files.Count; totalBytes = $totalBytes; files = @($files) }
}

function Get-NasInventory {
    param([Parameter(Mandatory)][string]$SourceRoot)
    $count = 0
    $bytes = [long]0
    $items = Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force -ErrorAction Stop | Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }
    foreach ($item in $items) { $count++; $bytes += [long]$item.Length }
    [pscustomobject][ordered]@{ fileCount = $count; totalBytes = $bytes }
}

function Verify-NasManifest {
    param([Parameter(Mandatory)]$Manifest,[Parameter(Mandatory)][string]$DestinationRoot)
    $checked = 0
    foreach ($file in @($Manifest.files)) {
        $path = Join-Path $DestinationRoot $file.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('L12_NAS_VERIFY_MISSING_FILE:' + [string]$file.path) }
        $item = Get-Item -LiteralPath $path -Force
        if ([long]$item.Length -ne [long]$file.bytes) { throw ('L12_NAS_VERIFY_BYTES:' + [string]$file.path + ':expected=' + [string]$file.bytes + ':actual=' + [string]$item.Length) }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -cne [string]$file.sha256) { throw ('L12_NAS_VERIFY_SHA:' + [string]$file.path + ':expected=' + [string]$file.sha256 + ':actual=' + $hash) }
        $checked++
    }
    return $checked
}

function Invoke-NasCopy {
    param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Destination,[Parameter(Mandatory)][string]$LogPath,[switch]$NoOverwriteExisting)
    $args = @($Source,$Destination,'/E','/COPY:DAT','/DCOPY:DAT','/Z','/J','/MT:16','/R:3','/W:5','/XJ','/FFT','/NP',('/LOG:' + $LogPath))
    if ($NoOverwriteExisting) { $args += @('/XN','/XO') }
    & robocopy @args
    $code = $LASTEXITCODE
    if ($code -ge 8) { throw ('L12_NAS_ROBOCOPY_' + $code) }
    return $code
}

$runId = [Guid]::NewGuid().ToString('N')
$lockPath = Join-Path $script:ReceiptRoot 'nas-pipeline.lock'
$lock = $null
$results = [Collections.Generic.List[object]]::new()
try {
    $lock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $plan = Read-NasPolicy
    $group = if ($GroupName) { $GroupName } elseif ($plan.raw.groupName) { [string]$plan.raw.groupName } else { 'group-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + $runId.Substring(0,8) }
    if ($group -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,96}$') { throw 'L12_NAS_GROUP_NAME' }
    if ($Mode -in @('Apply','Verify') -and -not (Test-NasRootAvailable $plan.destination)) {
        $paused = [ordered]@{ schema = 1; runId = $runId; status = 'paused-nas-unavailable'; mode = $Mode; destination = $plan.destination; probeTimeoutSeconds = $NasProbeTimeoutSeconds; probe = $script:LastNasProbe; sourceRetirement = $false; createdUtc = [DateTimeOffset]::UtcNow.ToString('o') }
        $path = Write-NasReceipt $paused ('nas-' + $runId + '.paused.json')
        return [pscustomobject]@{ status = $paused.status; receiptPath = $path; entries = 0 }
    }
    if ($Mode -eq 'Verify' -and -not $GroupName) {
        $groupsRoot = Join-Path $plan.destination 'groups'
        $latestGroup = Get-ChildItem -LiteralPath $groupsRoot -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'nas-group.json') -PathType Leaf } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $latestGroup) { throw 'L12_NAS_VERIFY_GROUP_MISSING' }
        $group = $latestGroup.Name
    }
    $entryRoot = if ($plan.layout -ceq 'root') { $plan.destination } else { Join-Path $plan.destination ('groups\' + $group) }
    $groupManifestRoot = Join-Path $plan.destination ('groups\' + $group)
    foreach ($entry in $plan.entries) {
        $log = Join-Path $script:ReceiptRoot ($entry.id + '-' + $runId + '.robocopy.log')
        $subdirectory = [string]$entry.destinationSubdirectory
        $destinationPath = if ([string]::IsNullOrWhiteSpace($subdirectory)) { $entryRoot } else { Join-Path $entryRoot $subdirectory.Replace('/','\') }
        $result = [pscustomobject][ordered]@{ id = $entry.id; source = $entry.resolvedSource; destination = $destinationPath; layout = $plan.layout; destinationSubdirectory = $subdirectory; destinationGroup = $group; mode = $Mode; status = 'planned'; preflightFileCount = $null; preflightBytes = $null; fileCount = $null; totalBytes = $null; verifiedFiles = $null; sourceRetired = $false; log = $log; manifest = $null }
        $results.Add($result)
        if ($Mode -eq 'Plan') { continue }
        if ($Mode -eq 'Apply') {
            $inventory = Get-NasInventory $entry.resolvedSource
            $result.preflightFileCount = $inventory.fileCount
            $result.preflightBytes = $inventory.totalBytes
            $result.status = 'copying'
            $null = Invoke-NasCopy $entry.resolvedSource $result.destination $log -NoOverwriteExisting:($plan.layout -ceq 'root')
            $manifest = Get-NasManifest $entry.resolvedSource $entry.id
            $manifestPath = Join-Path $script:ManifestRoot ($entry.id + '-' + $runId + '.manifest.json')
            $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
            $result.manifest = $manifestPath
        } else {
            $manifestPath = Get-ChildItem -LiteralPath $script:ManifestRoot -Filter ($entry.id + '-*.manifest.json') -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
            if (-not $manifestPath -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'L12_NAS_VERIFY_MANIFEST_MISSING' }
            $result.manifest = $manifestPath
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        }
        $result.fileCount = [int]$manifest.fileCount
        $result.totalBytes = [long]$manifest.totalBytes
        $result.verifiedFiles = Verify-NasManifest $manifest $result.destination
        $result.status = 'verified'
        if ($entry.retireSourceAfterVerification -and $AllowSourceRetirement) {
            Remove-Item -LiteralPath $entry.resolvedSource -Recurse -Force
            if (Test-Path -LiteralPath $entry.resolvedSource) { throw 'L12_NAS_SOURCE_RETIREMENT_FAILED' }
            $result.sourceRetired = $true
            $result.status = 'verified-source-retired'
        }
    }
    if ($Mode -ne 'Plan') {
        $groupFiles = [Collections.Generic.List[object]]::new()
        foreach ($result in @($results)) {
            $manifest = Get-Content -LiteralPath $result.manifest -Raw | ConvertFrom-Json
            $relativeEntry = if ($plan.layout -ceq 'root') { 'root-' + $result.id } else { $result.destination.Substring($entryRoot.Length).TrimStart('\\') }
            foreach ($file in @($manifest.files)) {
                $groupFiles.Add([pscustomobject][ordered]@{ path = (Join-Path $relativeEntry $file.path).Replace('\\','/'); bytes = [long]$file.bytes; sha256 = [string]$file.sha256 })
            }
        }
        if (-not (Test-Path -LiteralPath $groupManifestRoot -PathType Container)) { New-Item -ItemType Directory -Path $groupManifestRoot -Force | Out-Null }
        $groupManifest = [ordered]@{ schema = 1; status = 'verified'; cleanupEligible = ($plan.layout -cne 'root'); layout = $plan.layout; group = $group; pinned = $false; completedUtc = [DateTimeOffset]::UtcNow.ToString('o'); fileCount = $groupFiles.Count; files = @($groupFiles) }
        $groupManifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $groupManifestRoot 'nas-group.json') -Encoding utf8
    }
    $status = if ($Mode -eq 'Plan') { 'plan-created' } elseif (@($results | Where-Object {$_.status -eq 'verified-source-retired'}).Count -gt 0) { 'verified-and-retired' } else { 'verified-source-retained' }
    $receipt = [ordered]@{ schema = 1; runId = $runId; status = $status; mode = $Mode; layout = $plan.layout; policyPath = (Assert-L12GeneratedPath -Path $PolicyPath -Purpose 'NAS pipeline policy'); destination = $plan.destination; group = $group; groupRoot = if ($Mode -eq 'Plan') { $null } else { $groupManifestRoot }; entries = @($results); createdUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    $receiptPath = Write-NasReceipt $receipt ('nas-' + $runId + '.json')
    return [pscustomobject]@{ status = $status; receiptPath = $receiptPath; entries = $results.Count; sourceRetired = @($results | Where-Object sourceRetired).Count }
} catch {
    $failure = [ordered]@{ schema = 1; runId = $runId; status = 'failed-preserved'; mode = $Mode; error = $_.Exception.Message; probe = $script:LastNasProbe; partialResults = @($results); createdUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    $path = Write-NasReceipt $failure ('nas-' + $runId + '.failure.json')
    throw ('L12_NAS_PIPELINE_FAILED receipt=' + $path + ' error=' + $_.Exception.Message)
} finally { if ($lock) { $lock.Dispose() } }
