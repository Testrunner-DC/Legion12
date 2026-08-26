[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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
$projectConfigRaw = Get-Content -LiteralPath $projectConfig -Raw
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
    $raw = Get-Content -LiteralPath $path -Raw
    foreach ($requiredKey in @("name", "description", "model", "model_reasoning_effort", "developer_instructions")) {
        if ($raw -notmatch "(?m)^$requiredKey\s*=") { throw "$file is missing key: $requiredKey" }
    }
    if ($raw -notmatch ('(?m)^model\s*=\s*"' + [regex]::Escape($expected[$file].Model) + '"')) { throw "$file has an unexpected model" }
    if ($raw -notmatch ('(?m)^model_reasoning_effort\s*=\s*"' + [regex]::Escape($expected[$file].Effort) + '"')) { throw "$file has an unexpected reasoning effort" }
}

$agentsRules = Get-Content -LiteralPath (Join-Path $repoRoot "AGENTS.md") -Raw
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

$helpOutput = & $codex --strict-config --help 2>&1
if ($LASTEXITCODE -ne 0) { throw "Codex strict-config startup failed: $($helpOutput -join [Environment]::NewLine)" }

Write-Host "[L12 Codex] Project registration, agent files, tiers, TOML syntax, AGENTS markers, and strict-config entry passed."
Write-Host "[L12 Codex] Runtime activation still requires this repository canonical path to be trusted and the Codex project to be reloaded."
