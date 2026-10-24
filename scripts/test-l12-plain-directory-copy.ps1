[CmdletBinding()]
param([Parameter(Mandatory)][string]$WorkRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'ops/windows/L12PlainDirectoryCopy.ps1')
$canonical=Get-Content -LiteralPath (Join-Path $repo 'ops/windows/verify-l12.ps1') -Raw
if($canonical -match 'robocopy' -or ([regex]::Matches($canonical,'\$null = Copy-L12PlainDirectory -SourceRoot')).Count -ne 3){throw 'All three canonical copies must use the same plain-copy component'}
foreach($binding in @("-ExcludedRelativeDirectories @('node_modules','dist','dist-testrun','public/cards')",'L12PlainDirectoryCopy.ps1','schema = 3','runtimeSourceCompatibility','cardAssetsSha256','dist-testrun','testrun-shared-files.txt')){
    if(-not $canonical.Contains($binding)){throw "Canonical package binding missing: $binding"}
}
$root=Assert-L12GeneratedPath -Path (Join-Path $WorkRoot ('copy-test-'+[Guid]::NewGuid().ToString('N'))) -Purpose 'plain copy synthetic fixtures'
if(Test-Path -LiteralPath $root){throw 'New fixture required'}
New-Item -ItemType Directory -Path $root | Out-Null
$links=[Collections.Generic.List[string]]::new();$cases=0
function Reject([scriptblock]$Action,[string]$Expected){
    $caught=$false
    try{& $Action | Out-Null}catch{if($_.Exception.Message -notlike "*$Expected*"){throw};$caught=$true}
    if(-not $caught){throw "Expected refusal: $Expected"}
}
function Link([string]$Path,[string]$Target){
    $kind=if([IO.Path]::DirectorySeparatorChar -eq '\'){'Junction'}else{'SymbolicLink'}
    New-Item -ItemType $kind -Path $Path -Target $Target | Out-Null
    $links.Add($Path)
}
try{
    $source=Join-Path $root 'source';$target=Join-Path $root 'target';$outside=Join-Path $root 'outside'
    foreach($p in @($source,$outside,(Join-Path $source 'nested/empty'),(Join-Path $source 'public'))){New-Item -ItemType Directory -Path $p -Force | Out-Null}
    [IO.File]::WriteAllBytes((Join-Path $source 'ordinary.bin'),[byte[]]@(0,255,13,10,0,42))
    [IO.File]::WriteAllText((Join-Path $source 'nested/中文.txt'),'synthetic UTF8 only',[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $source '.hidden-file'),'synthetic hidden leaf')
    [IO.File]::WriteAllText((Join-Path $outside 'must-not-copy.txt'),'synthetic dependency bytes')
    Link (Join-Path $source 'node_modules') $outside
    Link (Join-Path $source 'public/cards') $outside
    $result=Copy-L12PlainDirectory -SourceRoot $source -DestinationRoot $target -ExcludedRelativeDirectories @('node_modules','public/cards')
    if($result.files -ne 3 -or -not (Test-Path -LiteralPath (Join-Path $target 'nested/empty'))){throw 'Plain inventory not preserved'}
    foreach($leaf in @('ordinary.bin','nested/中文.txt','.hidden-file')){
        if((Get-FileHash -LiteralPath (Join-Path $source $leaf)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $leaf)).Hash){throw 'Copied bytes differ'}
    }
    if((Test-Path -LiteralPath (Join-Path $target 'node_modules')) -or (Test-Path -LiteralPath (Join-Path $target 'public/cards'))){throw 'Excluded link traversed'};$cases++
    Reject {Copy-L12PlainDirectory $source $target @('node_modules','public/cards')} 'L12_COPY_DESTINATION_NOT_EMPTY';$cases++
    Reject {Copy-L12PlainDirectory $source (Join-Path $source 'nested-output')} 'L12_COPY_ROOTS_OVERLAP';$cases++
    Reject {Copy-L12PlainDirectory $source $root} 'L12_COPY_ROOTS_OVERLAP';$cases++
    foreach($relative in @('../outside','/absolute','nested\escape','nested//empty','./nested','D:/escape','nested/','node_modules','ordinary.bin')){
        $exclusions=@('node_modules','public/cards',$relative)
        $expected=if($relative -eq 'node_modules'){'L12_COPY_DUPLICATE_EXCLUSION'}elseif($relative -eq 'ordinary.bin'){'L12_COPY_EXCLUSION_IS_NOT_DIRECTORY'}else{'L12_COPY_EXCLUSION_NOT_CANONICAL'}
        $bad=Join-Path $root ('bad-'+$cases)
        Reject {Copy-L12PlainDirectory $source $bad $exclusions} $expected
        if(Test-Path -LiteralPath $bad){throw 'Invalid exclusion wrote destination'};$cases++
    }
    $newTarget=Join-Path $root 'link-rejected'
    Reject {Copy-L12PlainDirectory $source $newTarget @('node_modules')} 'L12_COPY_LINKED_SOURCE_REFUSED'
    if(Test-Path -LiteralPath $newTarget){throw 'Linked input caused partial output'};$cases++
    $linkedDestination=Join-Path $root 'destination-link';Link $linkedDestination $outside
    Reject {Copy-L12PlainDirectory $source $linkedDestination @('node_modules','public/cards')} 'L12_COPY_DESTINATION_NOT_EMPTY';$cases++
    $emptyOutside=Join-Path $root 'empty-outside';New-Item -ItemType Directory -Path $emptyOutside | Out-Null
    $emptyLinkedDestination=Join-Path $root 'empty-destination-link';Link $emptyLinkedDestination $emptyOutside
    Reject {Copy-L12PlainDirectory $source $emptyLinkedDestination @('node_modules','public/cards')} 'L12_COPY_DESTINATION_NOT_EMPTY';$cases++
    $parentAlias=Join-Path $root 'parent-alias';Link $parentAlias $root
    Reject {Copy-L12PlainDirectory (Join-Path $parentAlias 'source') (Join-Path $source 'nested-output') @('node_modules','public/cards')} 'L12_COPY_ROOTS_OVERLAP';$cases++
    Reject {Copy-L12PlainDirectory (Join-Path $source 'node_modules') (Join-Path $root 'linked-source')} 'L12_COPY_SOURCE_NOT_PLAIN';$cases++
    $dist=Join-Path $root 'dist';$distTarget=Join-Path $root 'dist-target'
    New-Item -ItemType Directory -Path (Join-Path $dist 'assets') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $dist 'index.html'),'<html>synthetic</html>')
    [IO.File]::WriteAllText((Join-Path $dist 'assets/app.js'),'synthetic only')
    Link (Join-Path $dist 'cards') $outside
    $result=Copy-L12PlainDirectory $dist $distTarget @('cards')
    if($result.files -ne 2 -or (Test-Path -LiteralPath (Join-Path $distTarget 'cards'))){throw 'Distribution exclusion failed'};$cases++
    Write-Output "Plain directory copy passed: $cases actual filesystem cases; exclusions pruned, other links/overlap/nonempty targets refused; no package or product data."
}finally{
    foreach($link in $links){if(Test-Path -LiteralPath $link){[IO.DirectoryInfo]::new($link).Delete()}}
    if((Assert-L12GeneratedPath $root) -cne $root){throw 'Fixture target changed'}
    if(@(Get-ChildItem -LiteralPath $root -Force -Recurse | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Unexpected fixture link'}
    Remove-Item -LiteralPath $root -Recurse -Force
}
