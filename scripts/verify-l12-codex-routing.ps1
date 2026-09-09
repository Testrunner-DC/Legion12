[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function ConvertTo-NormalizedText {
    param([Parameter(Mandatory = $true)][string]$Text)
    return $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Read-NormalizedText {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ConvertTo-NormalizedText ([IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8))
}

if ((ConvertTo-NormalizedText "first`r`nsecond`rthird") -ne "first`nsecond`nthird") {
    throw "Line-ending normalization self-check failed"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectConfig = Join-Path $repoRoot ".codex\config.toml"
$agentRoot = Join-Path $repoRoot ".codex\agents"
$expected = [ordered]@{
    "l12-fast.toml" = @{ Model = "gpt-5.6-terra"; Effort = "medium" }
    "l12-standard.toml" = @{ Model = "gpt-5.6-sol"; Effort = "high" }
    "l12-deep.toml" = @{ Model = "gpt-5.6-sol"; Effort = "xhigh" }
    "l12-critical.toml" = @{ Model = "gpt-5.6-sol"; Effort = "max" }
}

if (-not (Test-Path -LiteralPath $projectConfig)) { throw "Missing project config: $projectConfig" }
$projectConfigRaw = Read-NormalizedText $projectConfig
if ($projectConfigRaw -notmatch '(?m)^max_concurrent_threads_per_session\s*=\s*2\s*$' -or
    $projectConfigRaw -match '(?m)^max_threads\s*=') {
    throw "Project config must use the current per-session limit of two optional subagents"
}
foreach ($file in $expected.Keys) {
    $path = Join-Path $agentRoot $file
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing agent config: $path" }
    $agentKey = [IO.Path]::GetFileNameWithoutExtension($file).Replace("-", "_")
    if ($projectConfigRaw -notmatch ('(?m)^\[agents\.' + [regex]::Escape($agentKey) + '\]$')) {
        throw "Project config does not register agent: $agentKey"
    }
    if ($projectConfigRaw -notmatch ('(?m)^config_file\s*=\s*"agents/' + [regex]::Escape($file) + '"$')) {
        throw "Project config does not reference agent file: $file"
    }
    $raw = Read-NormalizedText $path
    foreach ($requiredKey in @("name", "description", "model", "model_reasoning_effort", "developer_instructions")) {
        if ($raw -notmatch "(?m)^$requiredKey\s*=") { throw "$file is missing key: $requiredKey" }
    }
    if ($raw -notmatch ('(?m)^model\s*=\s*"' + [regex]::Escape($expected[$file].Model) + '"')) { throw "$file has an unexpected model" }
    if ($raw -notmatch ('(?m)^model_reasoning_effort\s*=\s*"' + [regex]::Escape($expected[$file].Effort) + '"')) { throw "$file has an unexpected reasoning effort" }
    foreach ($boundary in @("不递归委派", "只读任务不得修改文件", "不得修改共享台账、提交、推送、部署或关闭 Bug", "仅运行专项验证")) {
        if (-not $raw.Contains($boundary)) { throw "$file is missing the execution boundary: $boundary" }
    }
    if ($raw.Contains("自动提交并推送") -or $raw.Contains("完成后运行完整发布门禁")) {
        throw "$file must leave Git and release gates to the primary"
    }
}

$agentsRules = Read-NormalizedText (Join-Path $repoRoot "AGENTS.md")
foreach ($marker in @("Single-primary execution and optional delegation", "l12_fast", "l12_standard", "l12_deep", "l12_critical", "Default to zero subagents", "Subagents must not recursively delegate", "docs/WORKSTREAM-COORDINATION.md")) {
    if (-not $agentsRules.Contains($marker)) { throw "AGENTS.md is missing routing marker: $marker" }
}
if ($agentsRules.Contains("Route the task to exactly one project agent by default")) {
    throw "AGENTS.md must not mandate delegation for every task"
}

$python = Get-Command python.exe -ErrorAction SilentlyContinue
if ($null -ne $python) {
    $tomlFiles = @($projectConfig) + @($expected.Keys | ForEach-Object { Join-Path $agentRoot $_ })
    foreach ($tomlFile in $tomlFiles) {
        & $python.Source -c "import pathlib,tomllib,sys; tomllib.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8'))" $tomlFile
        if ($LASTEXITCODE -ne 0) { throw "TOML parse failed: $tomlFile" }
    }
}

$codexCandidates = @($env:CODEX_CLI_PATH) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$codex = $codexCandidates | Select-Object -First 1
if (-not $codex) {
    $command = Get-Command codex.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { $codex = $command.Source }
}
if (-not $codex) { throw "Codex CLI was not found" }

foreach ($marker in @("Product test isolation", 'must not run the unfiltered `GrandUMIServer.Tests` suite', "PlatformStoreTests|ControlPlane")) {
    if (-not $agentsRules.Contains($marker)) { throw "AGENTS.md is missing test-isolation marker: $marker" }
}

$changeGate = Read-NormalizedText (Join-Path $repoRoot "scripts\verify-l12-change.ps1")
if ($changeGate -notmatch 'GrandUMIServer\.Tests\.csproj[\s\S]*--filter[\s\S]*PlatformStoreTests\|FullyQualifiedName~ControlPlane') {
    throw "L12 change gate must keep GrandUMI shared-project execution filtered to platform/control-plane tests"
}

$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    # Windows PowerShell 5 wraps native stderr warnings as non-terminating ErrorRecord objects.
    # Codex may warn that the sandbox cannot create optional PATH aliases while still returning 0;
    # strict startup is therefore judged by the process exit code, not by the presence of stderr text.
    $helpOutput = & $codex --strict-config --help 2>&1
    $codexExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorAction
}
if ($codexExitCode -ne 0) { throw "Codex strict-config startup failed: $($helpOutput -join [Environment]::NewLine)" }

Write-Host "[L12 Codex] Optional-agent registration, concurrency, execution boundaries, tiers, available TOML parser, and strict-config entry passed."
Write-Host "[L12 Codex] CLI parsing is not proof of live desktop activation. New sessions load trusted project config; the current primary follows the approved operating policy immediately."
