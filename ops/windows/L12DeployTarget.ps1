Set-StrictMode -Version Latest

$script:L12ProductionHost = "legion-12.com"
$script:L12ProductionAddress = "154.201.80.91"

function Resolve-L12ProductionEndpoint {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RemoteServer)

    $match = [regex]::Match($RemoteServer, '\Aroot@(?<host>[^\s@:/]+)\z', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        throw "生产部署目标格式无效；只允许 root@$script:L12ProductionHost 或 root@$script:L12ProductionAddress。"
    }

    $remoteHost = $match.Groups['host'].Value.ToLowerInvariant()
    if ($remoteHost -ne $script:L12ProductionHost -and $remoteHost -ne $script:L12ProductionAddress) {
        throw "拒绝向非当前生产节点部署：$remoteHost；只允许 $script:L12ProductionHost / $script:L12ProductionAddress。"
    }

    [pscustomobject]@{
        Destination = "root@$remoteHost"
        Host = $remoteHost
        TrustedHostKeyAlias = $script:L12ProductionAddress
    }
}

function Resolve-L12SshOptions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$RemoteServer,
        [string]$KnownHostsFile = "",
        [string]$IdentityFile = ""
    )

    $endpoint = Resolve-L12ProductionEndpoint -RemoteServer $RemoteServer
    if (-not (Get-Command ssh-keygen -ErrorAction SilentlyContinue)) {
        throw "缺少命令：ssh-keygen"
    }

    $candidateKnownHosts = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($KnownHostsFile)) {
        $candidateKnownHosts.Add($KnownHostsFile)
    }
    else {
        $candidateProfiles = [Collections.Generic.List[string]]::new()
        if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
            $candidateProfiles.Add($env:USERPROFILE)
        }
        try {
            $repositoryOwner = (Get-Acl -LiteralPath $RepositoryRoot).Owner
            $repositoryOwnerName = ($repositoryOwner -split '\\')[-1]
            if (-not [string]::IsNullOrWhiteSpace($repositoryOwnerName) -and -not [string]::IsNullOrWhiteSpace($env:SystemDrive)) {
                $candidateProfiles.Add((Join-Path "$($env:SystemDrive)\Users" $repositoryOwnerName))
            }
        }
        catch {
            Write-Verbose "无法从仓库所有者推导 SSH 配置目录：$($_.Exception.Message)"
        }

        foreach ($profile in $candidateProfiles | Select-Object -Unique) {
            $candidateKnownHosts.Add((Join-Path $profile ".ssh\known_hosts"))
        }
    }

    $trustedKnownHosts = $null
    foreach ($candidate in $candidateKnownHosts | Select-Object -Unique) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $trustedEntry = @(& ssh-keygen -F $endpoint.TrustedHostKeyAlias -f $candidate 2>$null)
        if ($LASTEXITCODE -eq 0 -and $trustedEntry.Count -gt 0) {
            $trustedKnownHosts = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($trustedKnownHosts)) {
        throw "缺少已人工核验的新生产服务器 $($endpoint.TrustedHostKeyAlias) 主机指纹；拒绝读取域名或旧服务器 known_hosts 条目。"
    }

    $options = [Collections.Generic.List[string]]::new()
    foreach ($option in @(
        "-o", "BatchMode=yes",
        "-o", "ConnectTimeout=20",
        "-o", "StrictHostKeyChecking=yes",
        "-o", "UserKnownHostsFile=$trustedKnownHosts",
        "-o", "HostName=$($endpoint.TrustedHostKeyAlias)",
        "-o", "HostKeyAlias=$($endpoint.TrustedHostKeyAlias)"
    )) {
        $options.Add($option)
    }

    $sshDirectory = Split-Path -Parent $trustedKnownHosts
    $identity = if (-not [string]::IsNullOrWhiteSpace($IdentityFile)) {
        if (-not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) {
            throw "显式 SSH identity 文件不存在或不是普通文件。"
        }
        (Resolve-Path -LiteralPath $IdentityFile).Path
    }
    else {
        @("id_ed25519", "id_rsa") |
            ForEach-Object { Join-Path $sshDirectory $_ } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            ForEach-Object { (Resolve-Path -LiteralPath $_).Path } |
            Select-Object -First 1
    }
    if (-not [string]::IsNullOrWhiteSpace($identity)) {
        foreach ($option in @("-o", "IdentitiesOnly=yes", "-i", $identity)) { $options.Add($option) }
    }

    Write-Host "[L12 部署] 固定使用已验证的新生产服务器 IP 主机指纹。"
    return $options.ToArray()
}
