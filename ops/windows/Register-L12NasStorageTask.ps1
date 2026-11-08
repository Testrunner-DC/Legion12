[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$PolicyPath,
    [string]$TaskName = 'L12-NAS-StoragePipeline',
    [ValidatePattern('^(?:[01]\d|2[0-3]):[0-5]\d$')][string]$DailyAt = '03:30',
    [switch]$Apply,
    [switch]$AllowSourceRetirement,
    [string]$RunAsUser,
    [string]$PipelineScript = (Join-Path $PSScriptRoot 'Invoke-L12NasStoragePipeline.ps1')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

$policy = Assert-L12GeneratedPath -Path $PolicyPath -Purpose 'NAS task policy'
$pipeline = Assert-L12GeneratedPath -Path $PipelineScript -Purpose 'NAS task script'
$pwsh = (Get-Command pwsh.exe -ErrorAction Stop).Source
if (-not (Test-Path -LiteralPath $policy -PathType Leaf)) { throw 'L12_NAS_TASK_POLICY_MISSING' }
if (-not (Test-Path -LiteralPath $pipeline -PathType Leaf)) { throw 'L12_NAS_TASK_SCRIPT_MISSING' }
$mode = if ($Apply) { 'Apply' } else { 'Plan' }
if ($PSCmdlet.ShouldProcess($TaskName,"register NAS storage pipeline $mode task")) {
    $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"' + $pipeline + '"'),'-Mode',$mode,'-PolicyPath',('"' + $policy + '"'))
    if ($AllowSourceRetirement) { $arguments += '-AllowSourceRetirement' }
    $action = New-ScheduledTaskAction -Execute $pwsh -Argument ($arguments -join ' ')
    $time = [DateTime]::ParseExact($DailyAt,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
    $trigger = New-ScheduledTaskTrigger -Daily -At $time
    $userId = if ([string]::IsNullOrWhiteSpace($RunAsUser)) { [Security.Principal.WindowsIdentity]::GetCurrent().Name } else { $RunAsUser }
    $principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'L12 NAS archive pipeline; uses UNC storage, resumable copy, independent verification and explicit source retirement.' -Force | Out-Null
}
[pscustomobject]@{ taskName = $TaskName; mode = $mode; dailyAt = $DailyAt; runAsUser = $userId; sourceRetirementSwitch = [bool]$AllowSourceRetirement; status = if ($WhatIfPreference) {'what-if'} else {'registered'} }
