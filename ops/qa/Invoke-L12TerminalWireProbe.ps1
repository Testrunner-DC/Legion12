#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AcceptedServerRoot,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-F]{64}$')][string]$ServerSha256,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$CacheRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. "$repo/ops/windows/L12GeneratedStorage.ps1"
$output=Assert-L12GeneratedPath -Path $OutputRoot -Purpose 'offline terminal wire attribution evidence'
$cache=Assert-L12GeneratedPath -Path $CacheRoot -Purpose 'small offline terminal wire probe'
if(Test-Path -LiteralPath "$output/wire-attribution.json"){throw 'Preserve the existing completed probe; do not overwrite'}
$server=[IO.Path]::GetFullPath($AcceptedServerRoot)
if((Get-FileHash -LiteralPath "$server/GrandUMIServer.dll").Hash -cne $ServerSha256){throw 'Accepted server drift'}
$paths=@('node','dotnet','git','rg')|ForEach-Object{Split-Path (Get-Command $_ -CommandType Application|Select-Object -First 1).Source}
$env:PATH=(@($paths)+@($PSHOME,'C:/Windows/System32','C:/Windows/System32/WindowsPowerShell/v1.0')|Select-Object -Unique)-join ';'
& "$repo/ops/windows/Initialize-L12BuildEnvironment.ps1" -CacheRoot $cache|Out-Null
New-Item -ItemType Directory -Path $output -Force|Out-Null
& dotnet build "$repo/ops/qa/TerminalWireProbe/TerminalWireProbe.csproj" -c Release --artifacts-path "$cache/build" "-p:L12AcceptedServerRoot=$server" -p:UseSharedCompilation=false -v minimal 2>&1|Tee-Object -FilePath "$output/build.log"
if($LASTEXITCODE -ne 0){throw 'Offline probe build failed'}
$probe="$cache/build/bin/TerminalWireProbe/release/TerminalWireProbe.dll"
if((Get-FileHash -LiteralPath "$cache/build/bin/TerminalWireProbe/release/GrandUMIServer.dll").Hash -cne $ServerSha256){throw 'Actual probe reference differs'}
& node "$repo/opcgpro-vue/scripts/measure-terminal-wire-fields.mjs" $output $probe $server $ServerSha256
if($LASTEXITCODE -ne 0){throw 'Offline terminal attribution failed; preserve failure and investigate'}
