[CmdletBinding()]
param(
    [string]$Root = "D:\GPT\Legion12",
    [int]$IntervalSeconds = 60,
    [double]$WarnDeltaGiB = 2,
    [switch]$Once
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$logRoot = Join-Path ([IO.Path]::GetFullPath($Root).TrimEnd('\')) 'artifacts\network'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$log = Join-Path $logRoot ("network-{0}.csv" -f (Get-Date -Format 'yyyyMMdd'))
$header = 'timestamp,adapter,receivedBytes,sentBytes,receivedDeltaBytes,sentDeltaBytes,totalDeltaBytes,status'
if ((Test-Path -LiteralPath $log) -and (Get-Content -LiteralPath $log -TotalCount 1) -ne $header) {
    $log = Join-Path $logRoot ("network-{0}-v2.csv" -f (Get-Date -Format 'yyyyMMdd'))
}
if (-not (Test-Path -LiteralPath $log)) { $header | Set-Content -LiteralPath $log -Encoding utf8 }

$previousReceived = @{}
$previousSent = @{}
do {
    $timestamp = (Get-Date).ToString('o')
    foreach ($adapter in Get-NetAdapterStatistics | Sort-Object Name) {
        [int64]$receivedDelta = if ($previousReceived.ContainsKey($adapter.Name)) { [int64]$adapter.ReceivedBytes - [int64]$previousReceived[$adapter.Name] } else { 0 }
        [int64]$sentDelta = if ($previousSent.ContainsKey($adapter.Name)) { [int64]$adapter.SentBytes - [int64]$previousSent[$adapter.Name] } else { 0 }
        [int64]$totalDelta = $receivedDelta + $sentDelta
        $status = if ($totalDelta -ge ($WarnDeltaGiB * 1GB)) { 'WARN' } else { 'OK' }
        '"{0}","{1}",{2},{3},{4},{5},{6},{7}' -f $timestamp, $adapter.Name, $adapter.ReceivedBytes, $adapter.SentBytes, $receivedDelta, $sentDelta, $totalDelta, $status |
            Add-Content -LiteralPath $log -Encoding utf8
        if ($status -eq 'WARN') { Write-Warning "$($adapter.Name) transferred $([math]::Round($totalDelta / 1GB, 2)) GiB since the prior sample (received $([math]::Round($receivedDelta / 1GB, 2)), sent $([math]::Round($sentDelta / 1GB, 2)))." }
        $previousReceived[$adapter.Name] = [int64]$adapter.ReceivedBytes
        $previousSent[$adapter.Name] = [int64]$adapter.SentBytes
    }
    if (-not $Once) { Start-Sleep -Seconds $IntervalSeconds }
} while (-not $Once)
