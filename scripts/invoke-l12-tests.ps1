[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Project,
    [string]$Configuration='Release'
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'lib/l12-test-storage.ps1')
if (Test-Path -LiteralPath 'D:\GPT\Legion12') {
    & (Join-Path $PSScriptRoot '../ops/windows/Initialize-L12BuildEnvironment.ps1') | Out-Null
    $storageBase=$env:L12_WORK_CACHE
} else {
    $storageBase=if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'l12-tests' } else { Join-Path ([IO.Path]::GetTempPath()) 'l12-tests' }
}
Invoke-L12TestRun -Executable 'dotnet' -Arguments @('test',$Project,'--configuration',$Configuration,'--','xUnit.ParallelizeTestCollections=false') `
    -TemporaryBase (Join-Path $storageBase 'test-temp') -EvidenceBase (Join-Path $storageBase 'test-evidence') -Label $Project
