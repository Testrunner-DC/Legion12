[CmdletBinding()]
param(
    [string]$CacheRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:L12_WORK_CACHE)) {
        $CacheRoot = $env:L12_WORK_CACHE
    }
    elseif (Test-Path -LiteralPath "D:\GPT") {
        $CacheRoot = "D:\GPT\L12-cache"
    }
    else {
        $CacheRoot = Join-Path $repoRoot ".l12-cache"
    }
}

$resolvedCacheRoot = [IO.Path]::GetFullPath($CacheRoot)
New-Item -ItemType Directory -Path $resolvedCacheRoot -Force | Out-Null

$paths = @{
    Temp = Join-Path $resolvedCacheRoot "temp"
    DotnetHome = Join-Path $resolvedCacheRoot "dotnet-home"
    NugetPackages = Join-Path $resolvedCacheRoot "nuget\packages"
    NugetHttp = Join-Path $resolvedCacheRoot "nuget\http"
    Npm = Join-Path $resolvedCacheRoot "npm"
    Corepack = Join-Path $resolvedCacheRoot "corepack"
}
foreach ($path in $paths.Values) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

$env:TEMP = $paths.Temp
$env:TMP = $paths.Temp
$env:DOTNET_CLI_HOME = $paths.DotnetHome
$env:NUGET_PACKAGES = $paths.NugetPackages
$env:NUGET_HTTP_CACHE_PATH = $paths.NugetHttp
$env:npm_config_cache = $paths.Npm
$env:COREPACK_HOME = $paths.Corepack
$env:L12_WORK_CACHE = $resolvedCacheRoot
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_XMLDOC_MODE = "skip"
$env:npm_config_update_notifier = "false"
$env:npm_config_fund = "false"

foreach ($name in @("TEMP", "TMP", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "npm_config_cache", "COREPACK_HOME")) {
    $resolvedPath = [IO.Path]::GetFullPath([Environment]::GetEnvironmentVariable($name, "Process"))
    if (-not $resolvedPath.StartsWith($resolvedCacheRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "构建缓存路径未位于指定根目录：$name=$resolvedPath"
    }
}

Write-Host "[L12 构建] 本次进程缓存根目录：$resolvedCacheRoot"
Write-Output $resolvedCacheRoot
