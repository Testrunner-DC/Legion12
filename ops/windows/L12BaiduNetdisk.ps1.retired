[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('Status','Refresh','Upload','Download','Restore','PrepareVolumes')][string]$Action,
 [string]$ManifestPath,
 [string]$ExpectedManifestSha256,
 [string]$ExpectedOriginalManifestSha256,
 [string]$TransferDirectory,
 [string]$CredentialPath='E:/Legion12-ColdStorage/private-baidu-api-124583157/oauth.dpapi',
 [ValidateRange(5,3600)][int]$TimeBudgetSeconds=300,
 [ValidateRange(5,300)][int]$RequestTimeoutSeconds=120,
 [ValidateRange(65536,1073741824)][long]$VolumeBytes=1GB
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'L12_BAIDU_POWERSHELL7_REQUIRED'}
Import-Module (Join-Path $PSScriptRoot 'L12BaiduNetdisk.psm1') -Force
try{switch($Action){
 'Status'{Get-L12BaiduCredentialStatus -CredentialPath $CredentialPath}
 'Refresh'{Update-L12BaiduAuthorization -CredentialPath $CredentialPath -TimeBudgetSeconds $TimeBudgetSeconds}
 'PrepareVolumes'{New-L12BaiduTransportVolumes -ManifestPath $ManifestPath -ExpectedManifestSha256 $ExpectedManifestSha256 -TransferDirectory $TransferDirectory -VolumeBytes $VolumeBytes -TimeBudgetSeconds $TimeBudgetSeconds}
 'Upload'{Invoke-L12BaiduArchiveUpload -ManifestPath $ManifestPath -ExpectedManifestSha256 $ExpectedManifestSha256 -TransferDirectory $TransferDirectory -CredentialPath $CredentialPath -TimeBudgetSeconds $TimeBudgetSeconds -RequestTimeoutSeconds $RequestTimeoutSeconds -ExpectedOriginalManifestSha256 $ExpectedOriginalManifestSha256}
 'Download'{Invoke-L12BaiduArchiveDownload -ManifestPath $ManifestPath -ExpectedManifestSha256 $ExpectedManifestSha256 -TransferDirectory $TransferDirectory -CredentialPath $CredentialPath -TimeBudgetSeconds $TimeBudgetSeconds -RequestTimeoutSeconds $RequestTimeoutSeconds -ExpectedOriginalManifestSha256 $ExpectedOriginalManifestSha256}
 'Restore'{Invoke-L12BaiduArchiveRestore -ManifestPath $ManifestPath -ExpectedManifestSha256 $ExpectedManifestSha256 -TransferDirectory $TransferDirectory -CredentialPath $CredentialPath -TimeBudgetSeconds $TimeBudgetSeconds -RequestTimeoutSeconds $RequestTimeoutSeconds -ExpectedOriginalManifestSha256 $ExpectedOriginalManifestSha256}
}}catch{if($_.Exception.Message -cmatch '^L12_BAIDU_[A-Z0-9_-]+$'){throw $_.Exception.Message};throw 'L12_BAIDU_LOCAL_FAILURE'}
