. (Join-Path $PSScriptRoot 'L12GeneratedStorage.ps1')

function Copy-L12PlainDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$DestinationRoot,
        [string[]]$ExcludedRelativeDirectories=@()
    )
    $sourceInput=[IO.Path]::GetFullPath($SourceRoot)
    $sourceItem=Get-Item -LiteralPath $sourceInput -Force -ErrorAction Stop
    if(-not $sourceItem.PSIsContainer -or ($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){
        throw 'L12_COPY_SOURCE_NOT_PLAIN_DIRECTORY'
    }
    $source=Resolve-L12PhysicalPath -Path $sourceInput
    $destinationInput=[IO.Path]::GetFullPath($DestinationRoot)
    $destination=Assert-L12GeneratedPath -Path $destinationInput -Purpose 'isolated ordinary-file copy'
    if((Test-Path -LiteralPath $destinationInput) -and
        ((Get-Item -LiteralPath $destinationInput -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){
        throw 'L12_COPY_DESTINATION_NOT_EMPTY_PLAIN_DIRECTORY'
    }
    $separator=[IO.Path]::DirectorySeparatorChar
    $comparison=if($separator -eq '\'){[StringComparison]::OrdinalIgnoreCase}else{[StringComparison]::Ordinal}
    if($source.Equals($destination,$comparison) -or
        $destination.StartsWith($source.TrimEnd($separator)+$separator,$comparison) -or
        $source.StartsWith($destination.TrimEnd($separator)+$separator,$comparison)){
        throw 'L12_COPY_ROOTS_OVERLAP'
    }
    $comparer=if($separator -eq '\'){[StringComparer]::OrdinalIgnoreCase}else{[StringComparer]::Ordinal}
    $excluded=[Collections.Generic.HashSet[string]]::new($comparer)
    foreach($relative in $ExcludedRelativeDirectories){
        if([string]::IsNullOrWhiteSpace($relative) -or $relative -match '(^|/)\.\.?(/|$)|[\\:*?"<>|]|^/|/$|//'){
            throw 'L12_COPY_EXCLUSION_NOT_CANONICAL_RELATIVE_DIRECTORY'
        }
        if(-not $excluded.Add($relative)){throw 'L12_COPY_DUPLICATE_EXCLUSION'}
    }
    if(Test-Path -LiteralPath $destination){
        $target=Get-Item -LiteralPath $destination -Force
        if(-not $target.PSIsContainer -or ($target.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            @(Get-ChildItem -LiteralPath $destination -Force).Count){throw 'L12_COPY_DESTINATION_NOT_EMPTY_PLAIN_DIRECTORY'}
    }
    # Validate the complete source inventory before the first destination write.
    # Excluded dependency/card directories are pruned, never traversed, even if
    # they are junctions. Every other linked item fails closed on every host.
    $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($source)
    $directories=[Collections.Generic.List[string]]::new()
    $files=[Collections.Generic.List[object]]::new()
    while($pending.Count){
        foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop){
            $relativeNative=[IO.Path]::GetRelativePath($source,$entry.FullName)
            if($separator -ne '\' -and $relativeNative.Contains('\')){throw 'L12_COPY_SOURCE_PATH_NOT_PORTABLE'}
            $relative=$relativeNative.Replace('\','/')
            if($excluded.Contains($relative)){
                if(-not $entry.PSIsContainer){throw 'L12_COPY_EXCLUSION_IS_NOT_DIRECTORY'}
                continue
            }
            if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'L12_COPY_LINKED_SOURCE_REFUSED'}
            if($relative -match '(^|/)\.\.?(/|$)|[\\:*?"<>|]|^/' -or [IO.Path]::IsPathRooted($relative)){
                throw 'L12_COPY_SOURCE_PATH_NOT_PORTABLE'
            }
            if($entry.PSIsContainer){$directories.Add($relative);$pending.Push($entry.FullName)}
            else{$files.Add([pscustomobject]@{source=$entry.FullName;relative=$relative;bytes=$entry.Length})}
        }
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach($relative in $directories){New-Item -ItemType Directory -Path (Join-Path $destination $relative) -Force | Out-Null}
    $bytes=0L
    foreach($file in $files){
        [IO.File]::Copy($file.source,(Join-Path $destination $file.relative),$false)
        $bytes+=$file.bytes
    }
    return [pscustomobject]@{files=$files.Count;bytes=$bytes;directories=$directories.Count}
}
