[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$PolicyPath,
    [string]$TaskName = 'L12-CloudStorage-Plan',
    [ValidatePattern('^(?:[01]\d|2[0-3]):[0-5]\d$')][string]$DailyAt = '03:30',
    [switch]$Apply,
    [switch]$AllowSourceRetirement,
    [string]$PipelineScript = (Join-Path $PSScriptRoot 'Invoke-L12CloudStoragePipeline.ps1')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

function Assert-TaskInputPath {
    param([Parameter(Mandatory)][string]$Path, [switch]$RequireE)
    $physical = Assert-L12GeneratedPath -Path $Path -Purpose 'cloud storage task input'
    if ($RequireE -and [IO.Path]::GetPathRoot($physical) -ne 'E:\') {
        throw 'L12_CLOUD_TASK_E_REQUIRED'
    }
    return $physical
}

$policy = Assert-TaskInputPath $PolicyPath
$pipeline = Assert-TaskInputPath $PipelineScript
if (-not (Test-Path -LiteralPath $policy -PathType Leaf)) { throw 'L12_CLOUD_TASK_POLICY_MISSING' }
if (-not (Test-Path -LiteralPath $pipeline -PathType Leaf)) { throw 'L12_CLOUD_TASK_SCRIPT_MISSING' }
$policyDocument = Get-Content -LiteralPath $policy -Raw | ConvertFrom-Json
$entriesProperty = $policyDocument.PSObject.Properties['entries']
if ($policyDocument.schema -ne 1 -or $policyDocument.status -cne 'active' -or $null -eq $entriesProperty -or @($entriesProperty.Value | Where-Object enabled).Count -lt 1) {
    throw 'L12_CLOUD_TASK_POLICY_NOT_ACTIVE'
}
if ($Apply -and -not $AllowSourceRetirement) {
    throw 'L12_CLOUD_TASK_APPLY_REQUIRES_RETIREMENT_SWITCH'
}

$mode = if ($Apply) { 'Apply' } else { 'Plan' }
$shouldRegister = $PSCmdlet.ShouldProcess($TaskName, "register L12 cloud storage $mode task")
if ($shouldRegister) {
    $arguments = @(
        '-NoLogo'
        '-NoProfile'
        '-NonInteractive'
        '-ExecutionPolicy', 'Bypass'
        '-File', ('"' + $pipeline + '"')
        '-Mode', $mode
        '-PolicyPath', ('"' + $policy + '"')
    )
    if ($AllowSourceRetirement) { $arguments += '-AllowSourceRetirement' }
    $action = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument ($arguments -join ' ')
    $time = [DateTime]::ParseExact($DailyAt, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    $trigger = New-ScheduledTaskTrigger -Daily -At $time
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'L12 cloud archive plan/upload/readback pipeline; source retirement is explicit.' -Force | Out-Null
}

[pscustomobject]@{
    taskName = $TaskName
    mode = $mode
    dailyAt = $DailyAt
    sourceRetirementSwitch = [bool]$AllowSourceRetirement
    status = if ($WhatIfPreference) { 'what-if' } else { 'registered' }
}
