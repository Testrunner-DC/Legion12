[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ServerDll,
    [string] $ArtifactRoot = 'D:\GPT\Legion12\artifacts\process-metric-program',
    [string] $Commit = 'abcdef0123456789',
    [string] $DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullDPath([string] $Path, [string] $Name) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith('D:\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must be an absolute D-drive path"
    }
    return $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally { $listener.Stop() }
}

function Test-ExactMarker([string] $Path) {
    if (-not [System.IO.File]::Exists($Path)) { return $false }
    $expected = [System.Text.Encoding]::UTF8.GetBytes("l12-process-metrics-owned-v1`n")
    $actual = [System.IO.File]::ReadAllBytes($Path)
    return [System.Convert]::ToHexString($actual) -ceq [System.Convert]::ToHexString($expected)
}

function Read-SharedCompleteLines([string] $Path) {
    $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        $reader = [System.IO.StreamReader]::new($stream, $encoding, $false, 4096, $true)
        try { $text = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if (-not $text.EndsWith("`n", [System.StringComparison]::Ordinal)) { return @() }
    return @($text.Split([char] 10, [System.StringSplitOptions]::RemoveEmptyEntries))
}

if (-not ('L12ProcessMetricProgramNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class L12ProcessMetricProgramNative
{
    public sealed class OwnedProcessStart
    {
        public int ProcessId { get; }
        public long StartTimeUtcTicks { get; }

        internal OwnedProcessStart(int processId, long startTimeUtcTicks)
        {
            ProcessId = processId;
            StartTimeUtcTicks = startTimeUtcTicks;
        }
    }

    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;
    private const uint CtrlBreakEvent = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string currentDirectory, ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out FileTime creation,
        out FileTime exit, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    public static OwnedProcessStart StartHiddenGroup(string dotnetPath, string serverDll,
        int port, string workingDirectory)
    {
        var startup = new StartupInfo
        {
            cb = Marshal.SizeOf<StartupInfo>(),
            dwFlags = StartfUseShowWindow,
            wShowWindow = SwHide,
        };
        var command = new StringBuilder(Quote(dotnetPath) + " " + Quote(serverDll) + " " + port);
        if (!CreateProcessW(dotnetPath, command, IntPtr.Zero, IntPtr.Zero, false,
                CreateNewProcessGroup | CreateUnicodeEnvironment, IntPtr.Zero, workingDirectory,
                ref startup, out var created))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "owned process launch failed");
        try
        {
            if (!GetProcessTimes(created.hProcess, out var creation, out _, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "owned process identity capture failed");
            var fileTime = ((long) creation.High << 32) | creation.Low;
            return new OwnedProcessStart(checked((int) created.dwProcessId),
                DateTime.FromFileTimeUtc(fileTime).Ticks);
        }
        catch
        {
            TerminateProcess(created.hProcess, 1);
            throw;
        }
        finally
        {
            CloseHandle(created.hThread);
            CloseHandle(created.hProcess);
        }
    }

    public static void SendTargetedBreak(int processGroupId)
    {
        if (processGroupId <= 0) throw new ArgumentOutOfRangeException(nameof(processGroupId));
        if (!SetConsoleCtrlHandler(IntPtr.Zero, true))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "control handler isolation failed");
        try
        {
            if (!GenerateConsoleCtrlEvent(CtrlBreakEvent, checked((uint) processGroupId)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "targeted control event failed");
        }
        finally { SetConsoleCtrlHandler(IntPtr.Zero, false); }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
'@
}

function Wait-Healthy([int] $Port, [System.Diagnostics.Process] $Process,
    [datetime] $DeadlineUtc) {
    $uri = "http://127.0.0.1:$Port/health"
    while ([datetime]::UtcNow -lt $DeadlineUtc) {
        $Process.Refresh()
        if ($Process.HasExited) { throw 'owned server exited before health became available' }
        try {
            $response = Invoke-WebRequest -Uri $uri -Method Get -TimeoutSec 2
            if ([int] $response.StatusCode -eq 200) { return }
        }
        catch { }
        Start-Sleep -Milliseconds 200
    }
    throw 'owned server health deadline exceeded'
}

function Stop-OwnedServer([System.Diagnostics.Process] $Process, [long] $StartTicks) {
    $Process.Refresh()
    if ($Process.HasExited) { throw 'owned server exited before targeted CancelKeyPress' }
    if ($Process.StartTime.ToUniversalTime().Ticks -ne $StartTicks) {
        throw 'owned server identity changed before shutdown'
    }
    [L12ProcessMetricProgramNative]::SendTargetedBreak($Process.Id)
    if (-not $Process.WaitForExit(10000)) { throw 'owned server did not honor targeted CancelKeyPress' }
    $exitCode = [int] $Process.ExitCode
    $exitCodeHex = '0x' + [System.BitConverter]::ToUInt32(
        [System.BitConverter]::GetBytes($exitCode), 0).ToString('X8')
    return [pscustomobject]@{ ExitCode = $exitCode; ExitCodeHex = $exitCodeHex }
}

function Stop-OwnedServerAfterFailure([System.Diagnostics.Process] $Process, [long] $StartTicks) {
    $current = $null
    try {
        $current = [System.Diagnostics.Process]::GetProcessById($Process.Id)
        $current.Refresh()
        if (-not $current.HasExited -and $current.StartTime.ToUniversalTime().Ticks -eq $StartTicks) {
            $current.Kill($true)
            $null = $current.WaitForExit(10000)
        }
    }
    catch { }
    finally { if ($null -ne $current) { $current.Dispose() } }
}

function Stop-OwnedProcessAfterLaunchFailure([int] $MetricProcessId, [long] $StartTicks) {
    $current = $null
    try {
        $current = [System.Diagnostics.Process]::GetProcessById($MetricProcessId)
        $current.Refresh()
        if (-not $current.HasExited -and $current.StartTime.ToUniversalTime().Ticks -eq $StartTicks) {
            $current.Kill($true)
            $null = $current.WaitForExit(10000)
        }
    }
    catch { }
    finally { if ($null -ne $current) { $current.Dispose() } }
}

function Copy-IsolatedServer([string] $SourceDirectory, [string] $Destination) {
    if (Test-Path -LiteralPath (Join-Path $SourceDirectory 'runtime')) {
        throw 'source server directory must not contain a runtime tree'
    }
    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $SourceDirectory -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination $Destination -Recurse -Force
    }
}

function Start-IsolatedServer([string] $PublishDirectory, [string] $DllName,
    [string] $PhysicalRuntime, [int] $Port) {
    [System.IO.Directory]::CreateDirectory($PhysicalRuntime) | Out-Null
    $runtimeLink = Join-Path $PublishDirectory 'runtime'
    if (Test-Path -LiteralPath $runtimeLink) { throw 'isolated publish runtime leaf already exists' }
    New-Item -ItemType Junction -Path $runtimeLink -Value $PhysicalRuntime | Out-Null
    $resolvedDotnet = (Get-Command $DotnetPath -ErrorAction Stop).Source
    $nativeStart = [L12ProcessMetricProgramNative]::StartHiddenGroup($resolvedDotnet,
        (Join-Path $PublishDirectory $DllName), $Port, $PublishDirectory)
    $metricProcessId = [int] $nativeStart.ProcessId
    $metricStartTicks = [long] $nativeStart.StartTimeUtcTicks
    $metricProcess = $null
    try {
        $metricProcess = [System.Diagnostics.Process]::GetProcessById($metricProcessId)
        $metricProcess.Refresh()
        if ($metricProcess.StartTime.ToUniversalTime().Ticks -ne $metricStartTicks) {
            throw 'owned server identity changed during launch handoff'
        }
        return [pscustomobject]@{
            Process = $metricProcess
            StartTicks = $metricStartTicks
        }
    }
    catch {
        if ($null -ne $metricProcess) { $metricProcess.Dispose() }
        Stop-OwnedProcessAfterLaunchFailure $metricProcessId $metricStartTicks
        throw
    }
}

if (-not $IsWindows) { throw 'this lifecycle harness requires native Windows process groups' }
if ($Commit -notmatch '^[0-9a-fA-F]{7,64}$') { throw 'Commit has an invalid fixed identity' }
$serverDllPath = Get-FullDPath $ServerDll 'ServerDll'
$artifactBase = Get-FullDPath $ArtifactRoot 'ArtifactRoot'
if (-not [System.IO.File]::Exists($serverDllPath)) { throw 'ServerDll does not exist' }
$sourceDirectory = [System.IO.Path]::GetDirectoryName($serverDllPath)
$dllName = [System.IO.Path]::GetFileName($serverDllPath)
$runRoot = Join-Path $artifactBase ("run-" + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($runRoot) | Out-Null

$previousHost = $env:L12_LISTEN_HOST
$previousRelease = $env:L12_RELEASE_VERSION
$env:L12_LISTEN_HOST = '127.0.0.1'
$env:L12_RELEASE_VERSION = $Commit
$receipt = [ordered]@{
    schema = 1
    success = $false
    canonicalLines = 0
    canonicalSpacingMilliseconds = 0
    cancelKeyPress = $false
    healthyShutdownStage = 'not-started'
    healthyExitCode = $null
    healthyExitCodeHex = $null
    degradedServerStayedHealthy = $false
    unknownEvidencePreserved = $false
    blockedShutdownStage = 'not-started'
    blockedExitCode = $null
    blockedExitCodeHex = $null
}

try {
    $healthyRoot = Join-Path $runRoot 'healthy'
    $healthyPublish = Join-Path $healthyRoot 'publish'
    $healthyRuntime = Join-Path $healthyRoot 'physical-runtime'
    Copy-IsolatedServer $sourceDirectory $healthyPublish
    $healthyPort = Get-FreeLoopbackPort
    $owned = Start-IsolatedServer $healthyPublish $dllName $healthyRuntime $healthyPort
    try {
        Wait-Healthy $healthyPort $owned.Process ([datetime]::UtcNow.AddSeconds(20))
        $deadline = [datetime]::UtcNow.AddSeconds(35)
        $lines = @()
        $segment = $null
        $healthyMetricRoot = Join-Path $healthyRuntime 'metrics\process'
        while ([datetime]::UtcNow -lt $deadline) {
            $segments = @(if (Test-Path -LiteralPath $healthyMetricRoot -PathType Container) {
                Get-ChildItem -LiteralPath $healthyMetricRoot -Filter '*.jsonl' -File -Recurse
            })
            if ($segments.Count -eq 1) {
                try { $candidate = @(Read-SharedCompleteLines $segments[0].FullName) }
                catch [System.IO.IOException] { $candidate = @() }
                if ($candidate.Count -ge 2) {
                    $segment = $segments[0]
                    $lines = $candidate
                    break
                }
            }
            Start-Sleep -Milliseconds 250
        }
        if ($lines.Count -lt 2 -or $null -eq $segment) { throw 'two canonical metric lines were not produced' }
        $first = $lines[0] | ConvertFrom-Json
        $second = $lines[1] | ConvertFrom-Json
        if ($first.v -ne 1 -or $second.v -ne 1 -or
            $first.commit -cne $Commit -or $second.commit -cne $Commit -or
            $first.instance -cne $second.instance -or
            $segment.BaseName -cne $first.instance) { throw 'canonical metric identity validation failed' }
        $spacing = [long] $second.archiveUtcMs - [long] $first.archiveUtcMs
        if ($spacing -lt 8000 -or $spacing -gt 20000) { throw 'canonical metric interval was outside the bounded range' }
        $dayDirectory = $segment.Directory.FullName
        $processRoot = [System.IO.Directory]::GetParent($dayDirectory).FullName
        if (-not (Test-ExactMarker (Join-Path $processRoot '.l12-process-metrics-owned-v1')) -or
            -not (Test-ExactMarker (Join-Path $dayDirectory '.l12-process-metrics-owned-v1'))) {
            throw 'canonical owned-v1 marker validation failed'
        }
        $receipt.canonicalLines = $lines.Count
        $receipt.canonicalSpacingMilliseconds = $spacing
        $receipt.healthyShutdownStage = 'signal-dispatch'
        $shutdown = Stop-OwnedServer $owned.Process $owned.StartTicks
        $receipt.healthyExitCode = $shutdown.ExitCode
        $receipt.healthyExitCodeHex = $shutdown.ExitCodeHex
        $receipt.healthyShutdownStage = if ($shutdown.ExitCode -eq 0) { 'complete' } else { 'nonzero-exit' }
        if ($shutdown.ExitCode -ne 0) { throw 'owned server returned a nonzero exit code' }
        $receipt.cancelKeyPress = $true
    }
    finally {
        Stop-OwnedServerAfterFailure $owned.Process $owned.StartTicks
        $owned.Process.Dispose()
    }

    $blockedRoot = Join-Path $runRoot 'blocked'
    $blockedPublish = Join-Path $blockedRoot 'publish'
    $blockedRuntime = Join-Path $blockedRoot 'physical-runtime'
    Copy-IsolatedServer $sourceDirectory $blockedPublish
    $unknownRoot = Join-Path $blockedRuntime 'metrics\process'
    [System.IO.Directory]::CreateDirectory($unknownRoot) | Out-Null
    $sentinel = Join-Path $unknownRoot 'incident-evidence.txt'
    $sentinelBytes = [System.Text.Encoding]::UTF8.GetBytes('synthetic-incident-evidence')
    [System.IO.File]::WriteAllBytes($sentinel, $sentinelBytes)
    $blockedPort = Get-FreeLoopbackPort
    $blocked = Start-IsolatedServer $blockedPublish $dllName $blockedRuntime $blockedPort
    try {
        Wait-Healthy $blockedPort $blocked.Process ([datetime]::UtcNow.AddSeconds(20))
        Start-Sleep -Seconds 12
        Wait-Healthy $blockedPort $blocked.Process ([datetime]::UtcNow.AddSeconds(5))
        $after = [System.IO.File]::ReadAllBytes($sentinel)
        if ([System.Convert]::ToHexString($after) -cne [System.Convert]::ToHexString($sentinelBytes)) {
            throw 'unknown incident evidence changed during degraded metrics startup'
        }
        if (Test-Path -LiteralPath (Join-Path $unknownRoot '.l12-process-metrics-owned-v1')) {
            throw 'degraded metrics startup claimed an unknown root'
        }
        $receipt.blockedShutdownStage = 'signal-dispatch'
        $shutdown = Stop-OwnedServer $blocked.Process $blocked.StartTicks
        $receipt.blockedExitCode = $shutdown.ExitCode
        $receipt.blockedExitCodeHex = $shutdown.ExitCodeHex
        $receipt.blockedShutdownStage = if ($shutdown.ExitCode -eq 0) { 'complete' } else { 'nonzero-exit' }
        if ($shutdown.ExitCode -ne 0) { throw 'owned server returned a nonzero exit code' }
        $receipt.degradedServerStayedHealthy = $true
        $receipt.unknownEvidencePreserved = $true
    }
    finally {
        Stop-OwnedServerAfterFailure $blocked.Process $blocked.StartTicks
        $blocked.Process.Dispose()
    }

    $receipt.success = $true
}
finally {
    $env:L12_LISTEN_HOST = $previousHost
    $env:L12_RELEASE_VERSION = $previousRelease
    $receiptPath = Join-Path $runRoot 'receipt.json'
    $receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
    Write-Output $receiptPath
}

if (-not $receipt.success) { exit 1 }
