$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$sourcePath=Join-Path $PSScriptRoot '../ops/windows/deploy-l12.ps1'
$tokens=$null;$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($sourcePath,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Deployment script parsing failed'}
$definitions=@($ast.FindAll({param($node)$node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-External'},$true))
if($definitions.Count -ne 1){throw 'Expected the actual unique deployment executor'}
$finalCalls=@($ast.FindAll({param($node)$node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-External' -and $node.Extent.Text.Contains('&& sed -i') -and $node.Extent.Text.Contains('deploy-legion12-release $mode')},$true))
if($finalCalls.Count -ne 1 -or -not @($finalCalls[0].CommandElements | Where-Object {$_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'NoRetry'}).Count){throw 'Final production cutover must explicitly disable automatic retry'}
$oldExitVariable=Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$savedExit=if($oldExitVariable){$oldExitVariable.Value}else{0}
try {
 & {
  param($definition)
  . ([scriptblock]::Create($definition))
  $DeploymentDeadline=$null
  function ssh {param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments)$script:calls++;$global:LASTEXITCODE=if($script:calls -le $script:failCount){$script:failureCode}else{0}}
  function scp {param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments)$script:calls++;$global:LASTEXITCODE=if($script:calls -le $script:failCount){$script:failureCode}else{0}}
  function Start-Sleep {param([int]$Seconds)$script:sleeps++}
  function AssertCalls([bool]$condition,[string]$message){if(-not $condition){throw $message}}
  $script:calls=0;$script:sleeps=0;$script:failCount=3;$script:failureCode=255;$threw=$false
  try {Invoke-External -NoRetry ssh 'synthetic-cutover'}catch{$threw=$true}
  AssertCalls ($threw -and $script:calls -eq 1 -and $script:sleeps -eq 0) 'Uncertain production SSH must fail after one invocation, not redeploy'
  $script:calls=0;$script:sleeps=0;$script:failCount=2;$script:failureCode=255
  Invoke-External -Executable ssh -Arguments @('synthetic-readonly-probe')
  AssertCalls ($script:calls -eq 3 -and $script:sleeps -eq 2) 'Safe SSH probe must retain bounded connection retries'
  $script:calls=0;$script:sleeps=0;$script:failCount=2
  Invoke-External -Executable scp -Arguments @('synthetic-upload')
  AssertCalls ($script:calls -eq 3 -and $script:sleeps -eq 2) 'Artifact upload must retain bounded connection retries'
  $script:calls=0;$script:sleeps=0;$script:failCount=3;$script:failureCode=17;$threw=$false
  try {Invoke-External -Executable ssh -Arguments @('synthetic-rejected-probe')}catch{$threw=$true}
  AssertCalls ($threw -and $script:calls -eq 1 -and $script:sleeps -eq 0) 'Non-transport failure must not retry'
  $DeploymentDeadline=[DateTimeOffset]::UtcNow.AddMinutes(-1)
  foreach($executable in @('ssh','scp')){
   $script:calls=0;$script:sleeps=0;$threw=$false
   try {Invoke-External -Executable $executable -Arguments @('synthetic-after-deadline')}catch{$threw=$true}
   AssertCalls ($threw -and $script:calls -eq 0 -and $script:sleeps -eq 0) 'Deadline must block the actual remote operation after local preflight'
  }
 } $definitions[0].Extent.Text
} finally {$global:LASTEXITCODE=$savedExit}
Write-Output '[L12 deploy retry] 6 actual-executor cases passed; cutover once, safe retries bounded, non-transport/deadline failures closed.'
