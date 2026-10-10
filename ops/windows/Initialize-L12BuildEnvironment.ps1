[CmdletBinding()]
param(
    [string]$CacheRoot = "",
    [string]$DependencyCacheRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:L12_WORK_CACHE)) {
        $CacheRoot = $env:L12_WORK_CACHE
    }
    elseif (Test-Path -LiteralPath "D:\GPT\Legion12") {
        $CacheRoot = "D:\GPT\Legion12\cache\primary"
    }
    else {
        $CacheRoot = Join-Path $repoRoot ".l12-cache"
    }
}

$resolvedCacheRoot = [IO.Path]::GetFullPath($CacheRoot)
if ([string]::IsNullOrWhiteSpace($DependencyCacheRoot)) {
    $DependencyCacheRoot = if ($env:L12_DEPENDENCY_CACHE) { $env:L12_DEPENDENCY_CACHE }
        elseif (Test-Path -LiteralPath 'D:\GPT\Legion12') { 'D:\GPT\Legion12\cache\primary' }
        else { $resolvedCacheRoot }
}
$resolvedDependencyRoot = [IO.Path]::GetFullPath($DependencyCacheRoot)
New-Item -ItemType Directory -Path $resolvedCacheRoot -Force | Out-Null

$paths = @{
    Temp = Join-Path $resolvedCacheRoot "temp"
    DotnetHome = Join-Path $resolvedCacheRoot "dotnet-home"
    NugetPackages = Join-Path $resolvedDependencyRoot "nuget\packages"
    NugetHttp = Join-Path $resolvedDependencyRoot "nuget\http"
    Npm = Join-Path $resolvedDependencyRoot "npm"
    Corepack = Join-Path $resolvedDependencyRoot "corepack"
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
$env:L12_DEPENDENCY_CACHE = $resolvedDependencyRoot
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_XMLDOC_MODE = "skip"
$env:npm_config_update_notifier = "false"
$env:npm_config_fund = "false"

foreach ($name in @("TEMP", "TMP", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "npm_config_cache", "COREPACK_HOME")) {
    $resolvedPath = [IO.Path]::GetFullPath([Environment]::GetEnvironmentVariable($name, "Process"))
    $expectedRoot = if ($name -in @('TEMP','TMP','DOTNET_CLI_HOME')) { $resolvedCacheRoot } else { $resolvedDependencyRoot }
    if (-not $resolvedPath.StartsWith($expectedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "构建缓存路径未位于指定根目录：$name=$resolvedPath"
    }
}

Write-Host "[L12 构建] 本次进程缓存根目录：$resolvedCacheRoot"
Write-Host "[L12 构建] 共享依赖缓存：$resolvedDependencyRoot"
Write-Output $resolvedCacheRoot
