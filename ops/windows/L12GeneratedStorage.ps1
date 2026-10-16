# Generated data only. This does not restrict reading source files or application runtime data.
function Resolve-L12PhysicalPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[int]$LinkDepth=0)
    if($LinkDepth -gt 24){throw 'L12_OUTPUT_LINK_DEPTH: too many or cyclic links'}
    if([IO.Path]::DirectorySeparatorChar -eq '\'){
        if($Path.StartsWith('\\?\',[StringComparison]::OrdinalIgnoreCase)){
            if($Path.Substring(4) -notmatch '^[A-Za-z]:\\'){throw 'L12_OUTPUT_UNSUPPORTED_PATH: generated outputs need a local DOS-drive path'}
            $Path=$Path.Substring(4)
        }
        if($Path.StartsWith('\\')){throw 'L12_OUTPUT_UNSUPPORTED_PATH: device and UNC aliases are not generated-output roots'}
    }
    $full=[IO.Path]::GetFullPath($Path)
    $root=[IO.Path]::GetPathRoot($full)
    $current=$root
    $parts=$full.Substring($root.Length).Split([char[]]@([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar),[StringSplitOptions]::RemoveEmptyEntries)
    foreach($part in $parts){
        $next=Join-Path $current $part
        $item=$null
        try{$item=Get-Item -LiteralPath $next -Force -ErrorAction Stop}
        catch [System.Management.Automation.ItemNotFoundException] { }
        # Permission/drive/provider failures are not proof of a plain non-C path: fail closed.
        if($null -ne $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){
            $target=$item.ResolveLinkTarget($true)
            if($null -eq $target){throw 'L12_OUTPUT_UNKNOWN_LINK: cannot establish physical target'}
            $current=Resolve-L12PhysicalPath -Path $target.FullName -LinkDepth ($LinkDepth+1)
        }else{$current=$next}
    }
    return [IO.Path]::GetFullPath($current)
}
function Assert-L12GeneratedPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[string]$Purpose='generated output')
    $physical=Resolve-L12PhysicalPath -Path $Path
    if([IO.Path]::GetPathRoot($physical).Equals('C:\',[StringComparison]::OrdinalIgnoreCase)){
        throw "L12_OUTPUT_ON_C: $Purpose must use a physical D/E output directory, not $physical"
    }
    return $physical
}
