[CmdletBinding()]
param(
    [string]$Server = "root@testrun.legion-12.com",
    [string]$KnownHostsFile = "",
    [string]$IdentityFile = "",
    [string]$ArtifactManifest = "",
    [switch]$DryRun,
    [switch]$ValidateArtifactOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:TestrunHost = "testrun.legion-12.com"
$script:TestrunAddress = "38.76.208.25"

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
        @(".deployment-commit", "publish", "opcgpro-vue", "scripts")
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
        if ($allowedRoots -notcontains $root) { throw "$Kind archive contains an unexpected root: $root" }
        $normalized.Add($member)
    }
    return $normalized.ToArray()
}

function Read-ValidatedArtifact {
    param([Parameter(Mandatory = $true)][string]$ManifestPath)
    if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "A verified artifact manifest is required."
    }
    $manifestPathResolved = (Resolve-Path -LiteralPath $ManifestPath).Path
    $manifestDirectory = Split-Path -Parent $manifestPathResolved
    $manifest = Get-Content -LiteralPath $manifestPathResolved -Raw | ConvertFrom-Json
    $commit = [string]$manifest.commit
    $releaseHash = [string]$manifest.releaseSha256
    $assetHash = [string]$manifest.cardAssetsHash
    $assetArchiveHash = [string]$manifest.cardAssetsSha256
    if ($manifest.schema -ne 3 -or $commit -notmatch '^[0-9a-f]{40}$' -or
        $releaseHash -notmatch '^[0-9a-f]{64}$' -or $assetHash -notmatch '^[0-9a-f]{64}$' -or
        $assetArchiveHash -notmatch '^[0-9a-f]{64}$') { throw "Artifact manifest identity fields are invalid." }
    $releaseArchive = if ([IO.Path]::IsPathRooted([string]$manifest.releaseArchive)) {
        [string]$manifest.releaseArchive
    } else { Join-Path $manifestDirectory ([string]$manifest.releaseArchive) }
    $assetArchive = if ([IO.Path]::IsPathRooted([string]$manifest.cardAssetsArchive)) {
        [string]$manifest.cardAssetsArchive
    } else { Join-Path $manifestDirectory ([string]$manifest.cardAssetsArchive) }
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
    foreach ($required in @(".deployment-commit", "publish/GrandUMIServer.dll", "opcgpro-vue/dist/index.html", "scripts/ws-smoke.mjs")) {
        if ($releaseMembers -notcontains $required) { throw "Release archive is incomplete: $required" }
    }
    foreach ($forbidden in @("publish/runtime", "opcgpro-vue/dist/card-assets", "opcgpro-vue/dist/cards")) {
        if ($releaseMembers | Where-Object { $_ -eq $forbidden -or $_.StartsWith("$forbidden/", [StringComparison]::Ordinal) }) {
            throw "Release archive contains forbidden runtime or cached data: $forbidden"
        }
    }
    $marker = (& tar -xOzf $releaseArchive "./.deployment-commit" 2>$null) -join ''
    if ($LASTEXITCODE -ne 0) { $marker = (& tar -xOzf $releaseArchive ".deployment-commit" 2>$null) -join '' }
    if ($LASTEXITCODE -ne 0 -or $marker.Trim() -ne $commit) { throw "Release commit marker differs from the manifest." }
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
if ($ValidateArtifactOnly) {
    Write-Host "[L12 testrun deploy] Target and artifact validation passed; no remote connection was made."
    exit 0
}

foreach ($command in @("git", "ssh", "scp")) { Require-Command $command }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$originalLocation = Get-Location
try {
    Set-Location $repoRoot
    if (& git status --porcelain) { throw "The worktree is dirty; testrun archives must come from a clean commit." }
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -ne $artifact.Commit) { throw "Artifact commit does not match the clean worktree HEAD." }
    $sshOptions = @(Resolve-TestrunSshOptions -Endpoint $endpoint -KnownHosts $KnownHostsFile -Identity $IdentityFile)
    $serverDeploy = Join-Path $repoRoot "ops\server\deploy-l12-testrun-release.sh"
    $healthVerifier = Join-Path $repoRoot "ops\server\verify-l12-health.mjs"
    $incoming = "/opt/legion12-testrun-deployment/incoming"
    $remoteTool = "/tmp/deploy-l12-testrun-release-$($artifact.Commit).sh"
    $remoteVerifier = "/tmp/verify-l12-testrun-health-$($artifact.Commit).mjs"
    $remoteRelease = "$incoming/l12-testrun-release-$($artifact.Commit).tar.gz"
    $remoteAssets = "$incoming/l12-testrun-card-assets-$($artifact.CardAssetsHash).tar.gz"

    Invoke-External ssh @sshOptions $endpoint.Destination "mkdir -p '$incoming'"
    Invoke-External scp @sshOptions $serverDeploy "$($endpoint.Destination):$remoteTool"
    Invoke-External scp @sshOptions $healthVerifier "$($endpoint.Destination):$remoteVerifier"
    Invoke-External ssh @sshOptions $endpoint.Destination "sed -i 's/\r$//' '$remoteTool' && install -m 0755 '$remoteTool' /usr/local/sbin/deploy-legion12-testrun-release && install -m 0755 '$remoteVerifier' /usr/local/libexec/verify-legion12-testrun-health.mjs && rm -f '$remoteTool' '$remoteVerifier' && /usr/local/sbin/deploy-legion12-testrun-release self-test"
    Invoke-External scp @sshOptions $artifact.ReleaseArchive "$($endpoint.Destination):$remoteRelease"

    & ssh @sshOptions $endpoint.Destination "test -d '/opt/legion12-testrun-static/card-assets/$($artifact.CardAssetsHash)'"
    $assetsCached = $LASTEXITCODE -eq 0
    $assetShaArgument = "-"
    $assetPathArgument = "-"
    if (-not $assetsCached) {
        Invoke-External scp @sshOptions $artifact.CardAssetsArchive "$($endpoint.Destination):$remoteAssets"
        $assetShaArgument = $artifact.CardAssetsSha256
        $assetPathArgument = $remoteAssets
    }
    $mode = if ($DryRun) { "dry-run" } else { "deploy" }
    Invoke-External ssh @sshOptions $endpoint.Destination "/usr/local/sbin/deploy-legion12-testrun-release $mode $($artifact.Commit) $($artifact.ReleaseSha256) $remoteRelease $($artifact.CardAssetsHash) $assetShaArgument $assetPathArgument"
    if ($DryRun) { Write-Host "[L12 testrun deploy] Dry-run passed; the active testrun release was unchanged." }
    else { Write-Host "[L12 testrun deploy] Deployment passed: https://testrun.legion-12.com/" }
}
finally {
    Set-Location $originalLocation
}
