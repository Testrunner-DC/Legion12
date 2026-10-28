[CmdletBinding()]
param(
    [string]$Server = "root@legion-12.com",
    [string]$KnownHostsFile = "",
    [string]$IdentityFile = "",
    [string]$ArtifactManifest = "",
    [string]$ArtifactDirectory = "",
    [string]$ArtifactManifestSha256 = "",
    [string]$CandidateRepository = "",
    [switch]$DryRun,
    [switch]$ValidateArtifactOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:TestrunHost = "legion-12.com"
$script:TestrunAddress = "154.201.80.91"

function Invoke-External {
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$Executable,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "External command failed ($LASTEXITCODE): $Executable" }
}

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Missing command: $Name" }
}

function Test-ExactJsonInteger {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory = $true)][long]$Expected
    )
    return (($Value -is [int32] -or $Value -is [int64]) -and [long]$Value -eq $Expected)
}

function Test-DeploymentManifestContract {
    param([Parameter(Mandatory = $true)][object]$Manifest)
    foreach ($name in @('schema', 'deploymentDrainProtocol', 'requiresExternalStopPermit', 'runtimeDataCompatibility', 'runtimeSourceCompatibility')) {
        if (-not $Manifest.PSObject.Properties[$name]) { return $false }
    }
    return ((Test-ExactJsonInteger -Value $Manifest.schema -Expected 3) -and
        (Test-ExactJsonInteger -Value $Manifest.deploymentDrainProtocol -Expected 1) -and
        $Manifest.requiresExternalStopPermit -is [bool] -and $Manifest.requiresExternalStopPermit -and
        [string]$Manifest.runtimeDataCompatibility -ceq 'l12-runtime-v1' -and
        [string]$Manifest.runtimeSourceCompatibility -cmatch '^[a-f0-9]{64}$')
}

function Read-StreamExactly {
    param(
        [Parameter(Mandatory = $true)][IO.Stream]$Stream,
        [Parameter(Mandatory = $true)][byte[]]$Buffer,
        [Parameter(Mandatory = $true)][int]$Count
    )
    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.Read($Buffer, $offset, $Count - $offset)
        if ($read -eq 0) { break }
        $offset += $read
    }
    return $offset
}

function Get-TarRegularFileSize {
    param(
        [Parameter(Mandatory = $true)][string]$Archive,
        [Parameter(Mandatory = $true)][string]$Member
    )
    $file = [IO.File]::Open($Archive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress, $true)
    try {
        $header = [byte[]]::new(512)
        $skipBuffer = [byte[]]::new(8192)
        while ($true) {
            if ((Read-StreamExactly -Stream $gzip -Buffer $header -Count $header.Length) -ne $header.Length) {
                throw "Release deployment capability marker metadata is unavailable."
            }
            $nonZero = $false
            foreach ($value in $header) { if ($value -ne 0) { $nonZero = $true; break } }
            if (-not $nonZero) { throw "Release deployment capability marker metadata is unavailable." }

            $nameLength = 0
            while ($nameLength -lt 100 -and $header[$nameLength] -ne 0) { $nameLength++ }
            $name = [Text.Encoding]::ASCII.GetString($header, 0, $nameLength)
            $prefixLength = 0
            while ($prefixLength -lt 155 -and $header[345 + $prefixLength] -ne 0) { $prefixLength++ }
            if ($prefixLength -gt 0) {
                $prefix = [Text.Encoding]::ASCII.GetString($header, 345, $prefixLength)
                $name = "$prefix/$name"
            }
            while ($name.StartsWith('./', [StringComparison]::Ordinal)) { $name = $name.Substring(2) }
            $name = $name.TrimEnd('/')

            $typeFlag = $header[156]
            $isRegular = $typeFlag -eq 0 -or $typeFlag -eq [byte][char]'0'
            $isDirectory = $typeFlag -eq [byte][char]'5'
            if (-not $isRegular -and -not $isDirectory) {
                throw "Release archive uses an unsupported tar extension before deployment capability metadata."
            }

            $sizeText = [Text.Encoding]::ASCII.GetString($header, 124, 12).Trim([char[]]@([char]0, [char]' '))
            if ($sizeText.Length -gt 0 -and $sizeText -cnotmatch '^[0-7]+$') {
                throw "Release archive contains an unsupported tar size encoding."
            }
            [long]$size = 0
            foreach ($character in $sizeText.ToCharArray()) {
                $digit = [int]$character - [int][char]'0'
                if ($size -gt ([long]::MaxValue - $digit) / 8) { throw "Release archive tar size overflows." }
                $size = ($size * 8) + $digit
            }

            if ($name -ceq $Member) {
                if (-not $isRegular) {
                    throw "Release deployment capability marker must be a regular file."
                }
                return $size
            }

            if ($size -gt [long]::MaxValue - 511) { throw "Release archive tar size overflows." }
            [long]$remaining = $size
            $padding = $remaining % 512
            if ($padding -ne 0) { $remaining += 512 - $padding }
            while ($remaining -gt 0) {
                $chunk = [int][Math]::Min([long]$skipBuffer.Length, $remaining)
                if ((Read-StreamExactly -Stream $gzip -Buffer $skipBuffer -Count $chunk) -ne $chunk) {
                    throw "Release archive is truncated while reading member metadata."
                }
                $remaining -= $chunk
            }
        }
    }
    finally {
        $gzip.Dispose()
        $file.Dispose()
    }
}

function Resolve-TestrunEndpoint {
    param([Parameter(Mandatory = $true)][string]$RemoteServer)
    $match = [regex]::Match($RemoteServer, '\Aroot@(?<host>[^\s@:/]+)\z', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { throw "Testrun target must be root@$script:TestrunHost or root@$script:TestrunAddress." }
    $hostName = $match.Groups['host'].Value.ToLowerInvariant()
    if ($hostName -ne $script:TestrunHost -and $hostName -ne $script:TestrunAddress) {
        throw "Refusing non-testrun target: $hostName"
    }
    [pscustomobject]@{
        Destination = "root@$hostName"
        Host = $hostName
        Address = $script:TestrunAddress
        HostKeyAlias = $script:TestrunAddress
    }
}

function Resolve-TestrunSshOptions {
    param(
        [Parameter(Mandatory = $true)]$Endpoint,
        [Parameter(Mandatory = $true)][string]$KnownHosts,
        [string]$Identity = ""
    )
    if ([string]::IsNullOrWhiteSpace($KnownHosts) -or -not (Test-Path -LiteralPath $KnownHosts -PathType Leaf)) {
        throw "An explicit, pre-verified known_hosts file is required for testrun deployment."
    }
    $resolvedKnownHosts = (Resolve-Path -LiteralPath $KnownHosts).Path
    $trustedEntry = @(& ssh-keygen -F $Endpoint.HostKeyAlias -f $resolvedKnownHosts 2>$null)
    if ($LASTEXITCODE -ne 0 -or $trustedEntry.Count -eq 0) {
        throw "known_hosts does not contain the verified testrun server address fingerprint."
    }
    $options = [Collections.Generic.List[string]]::new()
    foreach ($option in @(
        "-o", "BatchMode=yes",
        "-o", "ConnectTimeout=20",
        "-o", "StrictHostKeyChecking=yes",
        "-o", "UserKnownHostsFile=$resolvedKnownHosts",
        "-o", "HostName=$($Endpoint.Address)",
        "-o", "HostKeyAlias=$($Endpoint.HostKeyAlias)"
    )) { $options.Add($option) }
    if (-not [string]::IsNullOrWhiteSpace($Identity)) {
        if (-not (Test-Path -LiteralPath $Identity -PathType Leaf)) { throw "The explicit SSH identity file does not exist." }
        $options.Add("-o")
        $options.Add("IdentitiesOnly=yes")
        $options.Add("-i")
        $options.Add((Resolve-Path -LiteralPath $Identity).Path)
    }
    return $options.ToArray()
}

function Get-NormalizedTarMembers {
    param(
        [Parameter(Mandatory = $true)][string]$Archive,
        [Parameter(Mandatory = $true)][ValidateSet("release", "card-assets")][string]$Kind
    )
    $rawMembers = @(& tar -tzf $Archive)
    if ($LASTEXITCODE -ne 0 -or $rawMembers.Count -eq 0 -or $rawMembers.Count -gt 50000) { throw "$Kind archive member list is invalid." }
    $verboseMembers = @(& tar -tvzf $Archive)
    if ($LASTEXITCODE -ne 0 -or $verboseMembers.Count -eq 0) { throw "$Kind archive metadata cannot be read." }
    foreach ($line in $verboseMembers) {
        if ([string]::IsNullOrEmpty($line) -or ($line[0] -ne '-' -and $line[0] -ne 'd')) {
            throw "$Kind archive contains a link or special file."
        }
    }
    $allowedRoots = if ($Kind -eq "release") {
        @(".deployment-commit", ".deployment-drain-capability.json", "publish", "opcgpro-vue", "scripts")
    } else { @("card-assets.manifest.json", "card-assets.preload.json", "cards") }
    $seen = @{}
    $normalized = [Collections.Generic.List[string]]::new()
    foreach ($raw in $rawMembers) {
        $member = ([string]$raw).Replace('\', '/')
        while ($member.StartsWith('./', [StringComparison]::Ordinal)) { $member = $member.Substring(2) }
        $member = $member.TrimEnd('/')
        if ([string]::IsNullOrEmpty($member) -or $member -eq '.') { continue }
        if ($member.StartsWith('/', [StringComparison]::Ordinal) -or $member -match '^[A-Za-z]:' -or
            ($member.Split('/') -contains '..') -or ($member.Split('/') -contains '.')) {
            throw "$Kind archive contains an escaping path: $member"
        }
        if ($seen.ContainsKey($member)) { throw "$Kind archive contains a duplicate member: $member" }
        $seen[$member] = $true
        $root = $member.Split('/')[0]
        if ($allowedRoots -cnotcontains $root) { throw "$Kind archive contains an unexpected root: $root" }
        if ($Kind -eq "release" -and $root -ceq ".deployment-drain-capability.json" -and
            $member -cne ".deployment-drain-capability.json") {
            throw "Release deployment capability marker cannot contain child members: $member"
        }
        $normalized.Add($member)
    }
    return $normalized.ToArray()
}

function Resolve-PlainArtifactPath {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$Directory)
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or $Path.StartsWith('\\')) {
        throw "Artifact relocation requires a fully qualified local path."
    }
    $full = [IO.Path]::GetFullPath($Path)
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if ($item.PSIsContainer -ne [bool]$Directory) { throw "Artifact relocation path has the wrong file type: $full" }
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.LinkType) { throw "Artifact relocation path contains a link: $($item.FullName)" }
        $parent = Split-Path -Parent $item.FullName
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $item.FullName) { break }
        $item = Get-Item -LiteralPath $parent -Force -ErrorAction Stop
    }
    return $full
}

function Resolve-ValidatedCandidateRepository {
    param([Parameter(Mandatory = $true)][string]$Repository, [Parameter(Mandatory = $true)][string]$Commit)
    Require-Command git
    $root = Resolve-PlainArtifactPath -Path $Repository -Directory
    $top = @(& git -C $root rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or $top.Count -ne 1 -or [IO.Path]::GetFullPath($top[0]) -ne $root) {
        throw "CandidateRepository must be the actual Git worktree root."
    }
    $dirty = @(& git -C $root status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw "The worktree is dirty; testrun archives must come from a clean commit." }
    $head = @(& git -C $root rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1 -or $head[0] -cne $Commit) { throw "Artifact commit does not match the clean worktree HEAD." }
    foreach ($relative in @('ops/server/deploy-l12-testrun-release.sh', 'ops/server/verify-l12-health.mjs',
        'ops/server/activate-l12-testrun-path.sh', 'ops/server/legion12-testrun-path.nginx', 'ops/server/legion12-testrun.service')) {
        $null = Resolve-PlainArtifactPath -Path (Join-Path $root $relative)
        $null = & git -C $root ls-files --error-unmatch -- $relative 2>$null
        if ($LASTEXITCODE -ne 0) { throw "CandidateRepository deployment tool is not tracked: $relative" }
    }
    return $root
}

function Read-ValidatedArtifact {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)
    if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "A verified artifact manifest is required."
    }
    $relocate = -not [string]::IsNullOrWhiteSpace($ArtifactDirectory)
    if ($relocate -ne (-not [string]::IsNullOrWhiteSpace($ArtifactManifestSha256))) {
        throw "ArtifactDirectory and ArtifactManifestSha256 must be supplied together."
    }
    $manifestPathResolved = (Resolve-Path -LiteralPath $ManifestPath).Path
    if ($relocate) {
        $manifestPathResolved = Resolve-PlainArtifactPath -Path ([IO.Path]::GetFullPath($ManifestPath))
        if ($ArtifactManifestSha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or
            (Get-FileHash -LiteralPath $manifestPathResolved -Algorithm SHA256).Hash -ine $ArtifactManifestSha256) {
            throw "Original artifact manifest SHA256 differs."
        }
    }
    $manifestDirectory = Split-Path -Parent $manifestPathResolved
    $manifest = Get-Content -LiteralPath $manifestPathResolved -Raw | ConvertFrom-Json
    $commit = [string]$manifest.commit
    $releaseHash = [string]$manifest.releaseSha256
    $assetHash = [string]$manifest.cardAssetsHash
    $assetArchiveHash = [string]$manifest.cardAssetsSha256
    if (-not (Test-DeploymentManifestContract -Manifest $manifest) -or $commit -cnotmatch '^[0-9a-f]{40}$' -or
        $releaseHash -notmatch '^[0-9a-f]{64}$' -or $assetHash -notmatch '^[0-9a-f]{64}$' -or
        $assetArchiveHash -notmatch '^[0-9a-f]{64}$') { throw "Artifact manifest identity fields are invalid." }
    if ($relocate) {
        $archiveRoot = Resolve-PlainArtifactPath -Path $ArtifactDirectory -Directory
        $releaseArchive = Resolve-PlainArtifactPath -Path (Join-Path $archiveRoot "l12-release-$commit.tar.gz")
        $assetArchive = Resolve-PlainArtifactPath -Path (Join-Path $archiveRoot "l12-card-assets-$assetHash.tar.gz")
    }
    else {
    $releaseArchive = if ([IO.Path]::IsPathRooted([string]$manifest.releaseArchive)) {
        [string]$manifest.releaseArchive
    } else { Join-Path $manifestDirectory ([string]$manifest.releaseArchive) }
    $assetArchive = if ([IO.Path]::IsPathRooted([string]$manifest.cardAssetsArchive)) {
        [string]$manifest.cardAssetsArchive
    } else { Join-Path $manifestDirectory ([string]$manifest.cardAssetsArchive) }
    }
    foreach ($path in @($releaseArchive, $assetArchive)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Artifact archive is missing: $path" }
    }
    $releaseArchive = (Resolve-Path -LiteralPath $releaseArchive).Path
    $assetArchive = (Resolve-Path -LiteralPath $assetArchive).Path
    if ((Split-Path $releaseArchive -Leaf) -ne "l12-release-$commit.tar.gz") { throw "Release archive name is not bound to the manifest commit." }
    if ((Split-Path $assetArchive -Leaf) -ne "l12-card-assets-$assetHash.tar.gz") { throw "Card asset archive name is not bound to its content version." }
    if ((Get-Item -LiteralPath $releaseArchive).Length -gt 150MB) { throw "Release archive exceeds 150 MiB." }
    if ((Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $releaseHash) { throw "Release archive SHA256 differs." }
    if ((Get-FileHash -LiteralPath $assetArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $assetArchiveHash) { throw "Card asset archive SHA256 differs." }
    $releaseMembers = @(Get-NormalizedTarMembers -Archive $releaseArchive -Kind release)
    foreach ($required in @(".deployment-commit", ".deployment-drain-capability.json", "publish/GrandUMIServer.dll", "opcgpro-vue/dist/index.html", "opcgpro-vue/dist-testrun/index.html", "opcgpro-vue/testrun-shared-files.txt", "scripts/ws-smoke.mjs")) {
        if ($releaseMembers -cnotcontains $required) { throw "Release archive is incomplete: $required" }
    }
    foreach ($forbidden in @("publish/runtime", "opcgpro-vue/dist/card-assets", "opcgpro-vue/dist/cards")) {
        if ($releaseMembers | Where-Object { $_ -eq $forbidden -or $_.StartsWith("$forbidden/", [StringComparison]::Ordinal) }) {
            throw "Release archive contains forbidden runtime or cached data: $forbidden"
        }
    }
    $marker = (& tar -xOzf $releaseArchive "./.deployment-commit" 2>$null) -join ''
    if ($LASTEXITCODE -ne 0) { $marker = (& tar -xOzf $releaseArchive ".deployment-commit" 2>$null) -join '' }
    if ($LASTEXITCODE -ne 0 -or $marker.Trim() -ne $commit) { throw "Release commit marker differs from the manifest." }
    $capabilityMarkerSize = Get-TarRegularFileSize -Archive $releaseArchive -Member ".deployment-drain-capability.json"
    if ($capabilityMarkerSize -gt 4096) { throw "Release deployment capability marker is too large." }
    $capabilityMarkerLines = @(& tar -xOzf $releaseArchive "./.deployment-drain-capability.json" 2>$null)
    $capabilityMarkerExitCode = $LASTEXITCODE
    if ($capabilityMarkerExitCode -ne 0) {
        $capabilityMarkerLines = @(& tar -xOzf $releaseArchive ".deployment-drain-capability.json" 2>$null)
        $capabilityMarkerExitCode = $LASTEXITCODE
    }
    if ($capabilityMarkerExitCode -ne 0 -or $capabilityMarkerLines.Count -ne 1) {
        throw "Release deployment capability marker must contain exactly one canonical line."
    }
    $capabilityMarker = [string]$capabilityMarkerLines[0]
    $expectedCapabilityMarker = [ordered]@{
        schema = 1
        protocolVersion = 1
        requiresExternalStopPermit = $true
        runtimeDataCompatibility = "l12-runtime-v1"
        runtimeSourceCompatibility = [string]$manifest.runtimeSourceCompatibility
        commit = $commit
    } | ConvertTo-Json -Compress
    if (-not [string]::Equals($capabilityMarker, $expectedCapabilityMarker, [StringComparison]::Ordinal)) {
        throw "Release deployment capability marker differs from the manifest contract."
    }
    $assetMembers = @(Get-NormalizedTarMembers -Archive $assetArchive -Kind card-assets)
    foreach ($required in @("card-assets.manifest.json", "card-assets.preload.json", "cards")) {
        if (-not ($assetMembers | Where-Object { $_ -eq $required -or $_.StartsWith("$required/", [StringComparison]::Ordinal) })) {
            throw "Card asset archive is incomplete: $required"
        }
    }
    [pscustomobject]@{
        Commit = $commit
        ReleaseArchive = $releaseArchive
        ReleaseSha256 = $releaseHash
        CardAssetsHash = $assetHash
        CardAssetsArchive = $assetArchive
        CardAssetsSha256 = $assetArchiveHash
    }
}

$endpoint = Resolve-TestrunEndpoint -RemoteServer $Server
foreach ($command in @("tar", "ssh-keygen")) { Require-Command $command }
$artifact = Read-ValidatedArtifact -ManifestPath $ArtifactManifest
$candidateRoot = if (-not [string]::IsNullOrWhiteSpace($CandidateRepository)) {
    Resolve-ValidatedCandidateRepository -Repository $CandidateRepository -Commit $artifact.Commit
} else { "" }
if ($ValidateArtifactOnly) {
    Write-Host "[L12 testrun deploy] Target and artifact validation passed; no remote connection was made."
    exit 0
}

foreach ($command in @("git", "ssh", "scp")) { Require-Command $command }
$repoRoot = if ($candidateRoot) { $candidateRoot } else { (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path }
$originalLocation = Get-Location
try {
    Set-Location $repoRoot
    $dirty = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw "The worktree is dirty; testrun archives must come from a clean commit." }
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -ne $artifact.Commit) { throw "Artifact commit does not match the clean worktree HEAD." }
    $sshOptions = @(Resolve-TestrunSshOptions -Endpoint $endpoint -KnownHosts $KnownHostsFile -Identity $IdentityFile)
    $serverDeploy = Join-Path $repoRoot "ops\server\deploy-l12-testrun-release.sh"
    $healthVerifier = Join-Path $repoRoot "ops\server\verify-l12-health.mjs"
    $pathActivator = Join-Path $repoRoot "ops\server\activate-l12-testrun-path.sh"
    $pathSnippet = Join-Path $repoRoot "ops\server\legion12-testrun-path.nginx"
    $serviceTemplate = Join-Path $repoRoot "ops\server\legion12-testrun.service"
    $incoming = "/opt/legion12-testrun-deployment/incoming"
    $remoteTransfer = "$incoming/transfer-$($artifact.Commit)"
    $remoteTool = "$remoteTransfer/$(Split-Path $serverDeploy -Leaf)"
    $remoteVerifier = "$remoteTransfer/$(Split-Path $healthVerifier -Leaf)"
    $remoteActivator = "$remoteTransfer/$(Split-Path $pathActivator -Leaf)"
    $remoteSnippet = "$remoteTransfer/$(Split-Path $pathSnippet -Leaf)"
    $remoteService = "$remoteTransfer/$(Split-Path $serviceTemplate -Leaf)"
    $remoteRelease = "$incoming/l12-testrun-release-$($artifact.Commit).tar.gz"
    $remoteAssets = "$incoming/l12-testrun-card-assets-$($artifact.CardAssetsHash).tar.gz"

    $assetProbe = @(& ssh @sshOptions $endpoint.Destination "mkdir -p '$remoteTransfer' && if test -d '/opt/legion12-testrun-static/card-assets/$($artifact.CardAssetsHash)'; then printf cached; else printf missing; fi")
    if ($LASTEXITCODE -ne 0) { throw "Card asset cache probe failed; refusing to treat a connection failure as a cache miss." }
    $assetProbeResult = ($assetProbe -join '').Trim()
    if ($assetProbeResult -ne 'cached' -and $assetProbeResult -ne 'missing') { throw "Card asset cache probe returned an invalid result." }
    $assetsCached = $assetProbeResult -eq 'cached'
    $assetShaArgument = "-"
    $assetPathArgument = "-"
    $uploadSources = [Collections.Generic.List[string]]::new()
    foreach ($source in @($serverDeploy, $healthVerifier, $pathActivator, $pathSnippet, $serviceTemplate, $artifact.ReleaseArchive)) {
        $uploadSources.Add($source)
    }
    if (-not $assetsCached) {
        $uploadSources.Add($artifact.CardAssetsArchive)
        $assetShaArgument = $artifact.CardAssetsSha256
        $assetPathArgument = $remoteAssets
    }
    else {
        Write-Host "[L12 testrun deploy] Reusing content-addressed card asset cache: $($artifact.CardAssetsHash)"
    }
    $uploadArguments = $uploadSources.ToArray()
    & scp @sshOptions @uploadArguments "$($endpoint.Destination):$remoteTransfer/"
    if ($LASTEXITCODE -ne 0) { throw "Batched testrun upload failed ($LASTEXITCODE)." }

    $mode = if ($DryRun) { "dry-run" } else { "deploy" }
    $remoteReleaseSource = "$remoteTransfer/$(Split-Path $artifact.ReleaseArchive -Leaf)"
    $remoteAssetMove = if ($assetsCached) { ":" } else {
        "mv -f '$remoteTransfer/$(Split-Path $artifact.CardAssetsArchive -Leaf)' '$remoteAssets'"
    }
    $remoteCommand = @(
        "set -eu",
        "sed -i 's/\r$//' '$remoteActivator' '$remoteSnippet' '$remoteService' '$remoteTool'",
        "chmod 0755 '$remoteActivator'",
        "'$remoteActivator' '$remoteSnippet' '$remoteService'",
        "install -m 0755 '$remoteTool' /usr/local/sbin/deploy-legion12-testrun-release",
        "install -m 0755 '$remoteVerifier' /usr/local/libexec/verify-legion12-testrun-health.mjs",
        "mv -f '$remoteReleaseSource' '$remoteRelease'",
        $remoteAssetMove,
        "rm -f '$remoteActivator' '$remoteSnippet' '$remoteService' '$remoteTool' '$remoteVerifier'",
        "/usr/local/sbin/deploy-legion12-testrun-release self-test",
        "/usr/local/sbin/deploy-legion12-testrun-release $mode $($artifact.Commit) $($artifact.ReleaseSha256) $remoteRelease $($artifact.CardAssetsHash) $assetShaArgument $assetPathArgument",
        "(rmdir '$remoteTransfer' 2>/dev/null || true)"
    ) -join " && "
    Invoke-External ssh @sshOptions $endpoint.Destination $remoteCommand
    if ($DryRun) { Write-Host "[L12 testrun deploy] Dry-run passed; the active testrun release was unchanged." }
    else { Write-Host "[L12 testrun deploy] Deployment passed: https://legion-12.com/testrun/" }
}
finally {
    Set-Location $originalLocation
}
