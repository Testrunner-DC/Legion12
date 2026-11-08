[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$PolicyPath,
    [string]$TaskName = 'L12-NAS-Retention',
    [ValidatePattern('^(?:[01]\d|2[0-3]):[0-5]\d$')][string]$DailyAt = '05:00',
    [switch]$Apply,
    [string]$RetentionScript = (Join-Path $PSScriptRoot 'Invoke-L12NasRetention.ps1')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

$policy = Assert-L12GeneratedPath -Path $PolicyPath -Purpose 'NAS retention task policy'
$script = Assert-L12GeneratedPath -Path $RetentionScript -Purpose 'NAS retention task script'
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
if (-not (Test-Path -LiteralPath $policy -PathType Leaf)) { throw 'L12_NAS_RETENTION_TASK_POLICY_MISSING' }
if (-not (Test-Path -LiteralPath $script -PathType Leaf)) { throw 'L12_NAS_RETENTION_TASK_SCRIPT_MISSING' }

$mode = if ($Apply) { 'Apply' } else { 'Plan' }
if ($PSCmdlet.ShouldProcess($TaskName,"register NAS retention $mode task")) {
    $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"' + $script + '"'),'-Mode',$mode,'-PolicyPath',('"' + $policy + '"'))
    $action = New-ScheduledTaskAction -Execute $pwsh -Argument ($arguments -join ' ')
    $time = [DateTime]::ParseExact($DailyAt,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
    $trigger = New-ScheduledTaskTrigger -Daily -At $time
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'L12 NAS retention; keeps recent verified groups and protected evidence, deleting at most one verified old group per run.' -Force | Out-Null
}
[pscustomobject]@{ taskName = $TaskName; mode = $mode; dailyAt = $DailyAt; status = if ($WhatIfPreference) {'what-if'} else {'registered'} }
