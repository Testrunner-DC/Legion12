[CmdletBinding()]
param([Parameter(Mandatory)][string]$WorkRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repoRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'ops/windows/L12GeneratedStorage.ps1')
$null=Assert-L12GeneratedPath -Path $WorkRoot -Purpose 'native release argument qualification'
$entry=Join-Path $repoRoot 'ops/windows/verify-l12.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($entry,[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'Canonical entry parse failed'}
$paths=[Collections.Generic.List[string]]::new()
foreach($command in $ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true)){
    $elements=$command.CommandElements
    $name=$command.GetCommandName()
    $position=-1
    if($name -eq 'Invoke-External' -and $elements.Count -gt 2 -and $elements[1].Extent.Text -eq 'node'){$position=2}
    if($name -eq 'Invoke-TimedExternal' -and $elements.Count -gt 4 -and
        $elements[1].Extent.Text -in @('"rules"','"platform"','"deployment-drain-consumer"','"server-restore"','"server-publish"')){$position=4}
    if($position -lt 0){continue}
    $argument=$elements[$position]
    if($argument -isnot [Management.Automation.Language.ParenExpressionAst]){throw 'Native file path must use an actual host Join-Path'}
    $pipeline=$argument.Pipeline.PipelineElements
    if($pipeline.Count -ne 1 -or $pipeline[0] -isnot [Management.Automation.Language.CommandAst]){throw 'Ambiguous native argument expression'}
    $join=$pipeline[0].CommandElements
    if($join.Count -ne 3 -or $join[0].Extent.Text -ne 'Join-Path' -or
        $join[1] -isnot [Management.Automation.Language.VariableExpressionAst] -or $join[1].VariablePath.UserPath -ne 'repoRoot' -or
        $join[2] -isnot [Management.Automation.Language.StringConstantExpressionAst]){throw 'Native path expression is not a literal repository binding'}
    $relative=$join[2].Value
    if($relative -match '\\|(^|/)\.\.?(/|$)|^/|:'){throw 'Native path literal is not portable repository relative'}
    $path=Join-Path $repoRoot $relative
    if(-not [IO.Path]::IsPathFullyQualified($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Native file argument is not an actual absolute repository file'}
    $paths.Add($path)
}
if($paths.Count -ne 9){throw "Expected nine actual native file arguments, got $($paths.Count)"}
$actualArguments=@($paths.ToArray())+@('synthetic path with space/中文;percent%literal')
$node=(Get-Command node -CommandType Application -ErrorAction Stop).Source
$received=& $node -e 'process.stdout.write(JSON.stringify(process.argv.slice(1)))' @actualArguments
if($LASTEXITCODE -ne 0){throw 'Actual native argument transport failed'}
$decoded=@($received|ConvertFrom-Json)
if($decoded.Count -ne $actualArguments.Count){throw 'Native arguments split or lost'}
for($i=0;$i -lt $decoded.Count;$i++){if($decoded[$i] -cne $actualArguments[$i]){throw 'Native Unicode/space/semicolon argument changed'}}
$source=Get-Content -LiteralPath $entry -Raw
foreach($binding in @('$pythonExecutable = $pythonCommand.Source',"@('python3','python')",'-CommandType Application',
    '$OutputDirectory = Assert-L12GeneratedPath -Path $OutputDirectory -Purpose ''canonical release output''')){
    if(-not $source.Contains($binding)){throw 'Canonical host/output binding missing'}
}
if($source.IndexOf('$OutputDirectory = Assert-L12GeneratedPath') -gt $source.IndexOf('    foreach ($commandName')){throw 'Output root must be validated before native stages'}
Write-Output 'Canonical native host binding passed: nine actual repository-file arguments plus Unicode/space/semicolon native roundtrip; local interpreter selection and early physical output guard retained. Not whole canonical packaging.'
