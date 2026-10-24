[CmdletBinding()]
param([Parameter(Mandatory)][string]$WorkRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'ops/windows/L12GeneratedStorage.ps1')
$root=Assert-L12GeneratedPath -Path $WorkRoot -Purpose 'build host boundary fixtures'
$initializer=Join-Path $repo 'ops/windows/Initialize-L12BuildEnvironment.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($initializer,[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'Initializer parse failed'}
$declaration=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-L12PowerShellHostPath'},$false)
if(@($declaration).Count -ne 1){throw 'Actual host path predicate must be unique'}
. ([scriptblock]::Create($declaration[0].Extent.Text))
$cases=0
foreach($row in @(
    @('D:/tools/pwsh.exe',$true,$true), @('C:\tools\pwsh.exe',$true,$true),
    @('pwsh.exe',$true,$false), @('/tools/pwsh',$true,$false), @('\tools\pwsh.exe',$true,$false),
    @('\\server\share\pwsh.exe',$true,$false), @('\\?\D:\pwsh.exe',$true,$false),
    @('/opt/microsoft/powershell/7/pwsh',$false,$true), @('/usr/bin/pwsh',$false,$true),
    @('pwsh',$false,$false), @('relative/pwsh',$false,$false), @('D:/tools/pwsh.exe',$false,$false),
    @('//server/share/pwsh',$false,$false), @('/tmp\pwsh',$false,$false),
    @('',$true,$false), @('',$false,$false)
)){
    if((Test-L12PowerShellHostPath $row[0] $row[1]) -ne $row[2]){throw "Host path boundary failed: $($row[0])"};$cases++
}
$names=@('TEMP','TMP','TMPDIR','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME','L12_WORK_CACHE','L12_DEPENDENCY_CACHE','L12_ARTIFACT_ROOT','L12_ARCHIVE_ROOT','L12_PWSH','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_CLI_TELEMETRY_OPTOUT','NUGET_XMLDOC_MODE','npm_config_update_notifier','npm_config_fund')
$saved=@{};foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$fixture=Join-Path $root ('host-test-'+[Guid]::NewGuid().ToString('N'))
if(Test-Path -LiteralPath $fixture){throw 'Fixture must be new'}
try{
    $env:L12_ARTIFACT_ROOT=Join-Path $fixture 'evidence'
    $env:L12_ARCHIVE_ROOT=Join-Path $fixture 'archives'
    foreach($invalid in @('pwsh',(Join-Path $root 'missing-host'),$PSHOME)){
        $env:L12_PWSH=$invalid;$rejected=$false
        try{& $initializer -CacheRoot $fixture -DependencyCacheRoot (Join-Path $fixture 'dependencies') | Out-Null}
        catch{if($_.Exception.Message -notlike '*L12_POWERSHELL_EXECUTABLE_INVALID*'){throw};$rejected=$true}
        if(-not $rejected -or (Test-Path -LiteralPath $fixture)){throw 'Invalid host accepted or cache created early'};$cases++
    }
    $env:L12_PWSH=$null
    & $initializer -CacheRoot $fixture -DependencyCacheRoot (Join-Path $fixture 'dependencies') | Out-Null
    $windowsHost=[IO.Path]::DirectorySeparatorChar -eq '\'
    $expected=Join-Path $PSHOME $(if($windowsHost){'pwsh.exe'}else{'pwsh'})
    if($env:L12_PWSH -cne $expected){throw 'Default host is not the actual current executable'};$cases++
    & $initializer -CacheRoot $fixture -DependencyCacheRoot (Join-Path $fixture 'dependencies') | Out-Null
    if($env:L12_PWSH -cne $expected){throw 'Explicit existing host changed'};$cases++
    $node=(Get-Command node -ErrorAction Stop).Source;$oldPath=$env:PATH
    try{
        $env:PATH=Split-Path -Parent $node
        $actual=& $node -e 'const {execFileSync}=require("node:child_process");process.stdout.write(execFileSync(process.env.L12_PWSH,["-NoProfile","-Command","Write-Output L12_HOST_OK"],{encoding:"utf8"}));'
        if($LASTEXITCODE -ne 0 -or $actual.Trim() -cne 'L12_HOST_OK'){throw 'Actual shortened-PATH Node child failed'};$cases++
    }finally{$env:PATH=$oldPath}
    foreach($name in @('TEMP','TMP','TMPDIR','DOTNET_CLI_HOME','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','npm_config_cache','COREPACK_HOME','L12_ARTIFACT_ROOT','L12_ARCHIVE_ROOT')){
        $physical=Assert-L12GeneratedPath -Path ([Environment]::GetEnvironmentVariable($name,'Process')) -Purpose 'initialized fixture path'
        if(-not $physical.StartsWith($fixture+[IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal)){throw 'Initialized path escaped fixture'}
    };$cases++
    Write-Output "Build host boundaries passed: $cases cases; actual host=$([Runtime.InteropServices.RuntimeInformation]::OSDescription). Other-OS path cases are pure inputs, not execution."
}finally{
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
    if(Test-Path -LiteralPath $fixture){
        if((Assert-L12GeneratedPath -Path $fixture) -cne $fixture){throw 'Fixture target changed'}
        if(@(Get-ChildItem -LiteralPath $fixture -Force -Recurse | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Unexpected fixture link'}
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
