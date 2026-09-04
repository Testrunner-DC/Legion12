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
}

$agentsRules = Read-NormalizedText (Join-Path $repoRoot "AGENTS.md")
foreach ($marker in @("Automatic task-complexity routing", "l12_fast", "l12_standard", "l12_deep", "l12_critical", "select the highest matching tier", "route one tier higher")) {
    if (-not $agentsRules.Contains($marker)) { throw "AGENTS.md is missing routing marker: $marker" }
}

$python = Get-Command python.exe -ErrorAction SilentlyContinue
if ($null -ne $python) {
    $tomlFiles = @($projectConfig) + @($expected.Keys | ForEach-Object { Join-Path $agentRoot $_ })
    foreach ($tomlFile in $tomlFiles) {
        & $python.Source -c "import pathlib,tomllib,sys; tomllib.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8'))" $tomlFile
        if ($LASTEXITCODE -ne 0) { throw "TOML parse failed: $tomlFile" }
    }
}

$codexCandidates = @(
    $env:CODEX_CLI_PATH,
    "C:\Users\neptu\AppData\Local\OpenAI\Codex\bin\110b3d66a02d864e\codex.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
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

Write-Host "[L12 Codex] Project registration, agent files, tiers, TOML syntax, AGENTS markers, and strict-config entry passed."
Write-Host "[L12 Codex] Runtime activation still requires this repository canonical path to be trusted and the Codex project to be reloaded."
