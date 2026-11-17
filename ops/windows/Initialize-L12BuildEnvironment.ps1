[CmdletBinding()]
param(
    [string]$CacheRoot = "",
    [string]$DependencyCacheRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

# Node-based acceptance probes must not rediscover a PowerShell executable via
# a shortened npm child PATH. Bind the current host unless explicitly supplied.
function Test-L12PowerShellHostPath {
    param([AllowNull()][string]$Path, [bool]$WindowsHost)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if ($WindowsHost) { return $Path -match '^[A-Za-z]:[\\/]' }
    # Do not reinterpret a drive alias, UNC/device path or relative command on
    # a Unix runner. Existence and file type are checked separately below.
    return $Path.StartsWith('/') -and -not $Path.StartsWith('//') -and -not $Path.Contains('\')
}
$windowsHost = [IO.Path]::DirectorySeparatorChar -eq '\'
$resolvedPowerShellExecutable = $env:L12_PWSH
if ([string]::IsNullOrWhiteSpace($resolvedPowerShellExecutable)) {
    $resolvedPowerShellExecutable = Join-Path $PSHOME $(if ($windowsHost) { 'pwsh.exe' } else { 'pwsh' })
    if ($windowsHost -and -not (Test-Path -LiteralPath $resolvedPowerShellExecutable -PathType Leaf)) {
        $resolvedPowerShellExecutable = Join-Path $PSHOME 'powershell.exe'
    }
}
if (-not (Test-L12PowerShellHostPath $resolvedPowerShellExecutable $windowsHost) -or
    -not (Test-Path -LiteralPath $resolvedPowerShellExecutable -PathType Leaf)) {
    throw 'L12_POWERSHELL_EXECUTABLE_INVALID: require an existing absolute local executable.'
}
$env:L12_PWSH = [IO.Path]::GetFullPath($resolvedPowerShellExecutable)

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
# Check actual junction targets before creating any directories. Never silently fall back to C.
$null=Assert-L12GeneratedPath -Path $resolvedCacheRoot -Purpose 'build cache'
$null=Assert-L12GeneratedPath -Path $resolvedDependencyRoot -Purpose 'dependency cache'
$artifactRoot=if($env:L12_ARTIFACT_ROOT){$env:L12_ARTIFACT_ROOT}elseif(Test-Path -LiteralPath 'D:\GPT\Legion12'){'D:\GPT\Legion12\artifacts'}else{Join-Path $repoRoot 'artifacts'}
$archiveRoot=if($env:L12_ARCHIVE_ROOT){$env:L12_ARCHIVE_ROOT}elseif(Test-Path -LiteralPath 'E:\'){'E:\Legion12-ColdStorage'}else{Join-Path $artifactRoot 'archives'}
$null=Assert-L12GeneratedPath -Path $artifactRoot -Purpose 'acceptance evidence'
$null=Assert-L12GeneratedPath -Path $archiveRoot -Purpose 'long-term archive'
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
$env:TMPDIR = $paths.Temp
$env:DOTNET_CLI_HOME = $paths.DotnetHome
$env:NUGET_PACKAGES = $paths.NugetPackages
$env:NUGET_HTTP_CACHE_PATH = $paths.NugetHttp
$env:npm_config_cache = $paths.Npm
$env:COREPACK_HOME = $paths.Corepack
$env:L12_WORK_CACHE = $resolvedCacheRoot
$env:L12_DEPENDENCY_CACHE = $resolvedDependencyRoot
$env:L12_ARTIFACT_ROOT = [IO.Path]::GetFullPath($artifactRoot)
$env:L12_ARCHIVE_ROOT = [IO.Path]::GetFullPath($archiveRoot)
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_XMLDOC_MODE = "skip"
$env:npm_config_update_notifier = "false"
$env:npm_config_fund = "false"

# Windows CreateProcess carries the complete environment block into every
# child.  This workstation's inherited PATH can accumulate hundreds of old
# .dotnet/tools entries and exceed the practical environment-block limit,
# which surfaces as Win32 error 8 (misleadingly reported as "insufficient
# system resources") when starting git/node/dotnet.  Build/test/deploy
# children only need a small, explicit toolchain path.  Keep optional
# L12_TOOL_PATH entries, but never inherit the unbounded process PATH.
$boundedToolPath = [Collections.Generic.List[string]]::new()
$pythonExecutable = Get-Command python.exe -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { Test-Path -LiteralPath $_.Source -PathType Leaf } | Select-Object -First 1
if (-not $pythonExecutable) {
    $pythonExecutable = Get-Command python3.exe -CommandType Application -ErrorAction SilentlyContinue |
        Where-Object { Test-Path -LiteralPath $_.Source -PathType Leaf } | Select-Object -First 1
}
function Add-L12ToolPath {
    param([AllowNull()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $resolved = try { [IO.Path]::GetFullPath($Path) } catch { return }
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { return }
    if (-not ($boundedToolPath | Where-Object { $_ -ieq $resolved })) {
        [void]$boundedToolPath.Add($resolved)
    }
}
foreach ($path in @(
        (Join-Path $env:SystemRoot 'System32'),
        $env:SystemRoot,
        (Join-Path $env:SystemRoot 'System32\Wbem'),
        (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0'),
        (Join-Path $env:SystemRoot 'System32\OpenSSH'),
        $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'dotnet' } else { $null }),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'dotnet' } else { $null }),
        $(if ($env:L12_NODE_ROOT) { $env:L12_NODE_ROOT } else { $null }),
        $(if ($pythonExecutable) { Split-Path -Parent $pythonExecutable.Source } else { $null }),
        $(if ($pythonExecutable) { Join-Path (Split-Path -Parent $pythonExecutable.Source) 'Scripts' } else { $null }),
        $(if ($pythonExecutable) { Join-Path (Split-Path -Parent $pythonExecutable.Source) 'Library\bin' } else { $null }),
        $(if ($env:CONDA_PREFIX) { $env:CONDA_PREFIX } else { $null }),
        $(if ($env:CONDA_PREFIX) { Join-Path $env:CONDA_PREFIX 'Scripts' } else { $null }),
        $(if ($env:CONDA_PREFIX) { Join-Path $env:CONDA_PREFIX 'Library\bin' } else { $null }),
        (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin'),
        (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd'),
        (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell'),
        (Join-Path ${env:ProgramFiles} 'Git\cmd'),
        (Join-Path ${env:ProgramFiles} 'Git\bin'),
        'C:\nodejs'
    )) { Add-L12ToolPath $path }
if ($env:L12_TOOL_PATH) {
    foreach ($path in $env:L12_TOOL_PATH -split [IO.Path]::PathSeparator) { Add-L12ToolPath $path }
}
if ($boundedToolPath.Count -eq 0) { throw 'L12_BOUNDED_TOOL_PATH_EMPTY' }
$env:Path = $boundedToolPath -join [IO.Path]::PathSeparator
if ($env:Path.Length -gt 4096) { throw "L12_BOUNDED_TOOL_PATH_TOO_LONG: $($env:Path.Length)" }
Write-Host "[L12 构建] 受限工具 PATH 长度：$($env:Path.Length)"

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
