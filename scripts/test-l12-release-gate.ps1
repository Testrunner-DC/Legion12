[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$verifyScript = Join-Path $repoRoot "ops\windows\verify-l12.ps1"
$changeGateScript = Join-Path $repoRoot "scripts\verify-l12-change.ps1"
$cacheInitializer = Join-Path $repoRoot "ops\windows\Initialize-L12BuildEnvironment.ps1"
$powerShellHost = Get-Command "pwsh" -ErrorAction SilentlyContinue
if ($null -eq $powerShellHost) { $powerShellHost = Get-Command "powershell" -ErrorAction Stop }

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) { throw $Message }
}

function Invoke-ChildPowerShell {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $rawOutput = @(& $powerShellHost.Source "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" $ScriptPath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorAction }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = (($rawOutput | ForEach-Object { [string]$_ }) -join [Environment]::NewLine)
    }
}

function Invoke-GitChecked {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Push-Location $WorkingDirectory
    try {
        & git @Arguments | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed: git $($Arguments -join ' ')" }
    }
    finally { Pop-Location }
}

function Write-CardManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AssetVersion
    )

    [ordered]@{
        schemaVersion = 3
        complete = $true
        cardCount = 362
        playableCardCount = 324
        presentationCardCount = 38
        assetVersion = $AssetVersion
    } | ConvertTo-Json | Set-Content -LiteralPath $Path -Encoding utf8
}

function Write-FakeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$ExitCode
    )

    $body = "@echo off`r`necho $Name %*>>`"%L12_TEST_COMMAND_LOG%`"`r`nexit /b $ExitCode`r`n"
    [IO.File]::WriteAllText((Join-Path $Directory "$Name.cmd"), $body, [Text.Encoding]::ASCII)
}

# Release planning may include targeted semantic audits, but the full rules, platform,
# and frontend build must be owned by verify-l12.ps1 exactly once.
$dryRun = Invoke-ChildPowerShell -ScriptPath $changeGateScript -Arguments @(
    "-Level", "Release",
    "-DryRun",
    "-ChangedPaths", "opcgpro-vue/WebSocket.Tests/TwelveLegions/ReleaseGateProbe.cs"
)
Assert-True ($dryRun.ExitCode -eq 0) "Release dry-run failed: $($dryRun.Output)"
$releaseInvocationCount = ([regex]::Matches($dryRun.Output, "Commit-level release verification \(no deployment\)")).Count
Assert-True ($releaseInvocationCount -eq 1) "Release dry-run must schedule the commit-level verifier exactly once."
foreach ($duplicateLabel in @("L12 full rule tests", "Platform persistence release gate", "Frontend production build")) {
    Assert-True (-not $dryRun.Output.Contains($duplicateLabel)) "Release dry-run still schedules duplicate work: $duplicateLabel"
}
Assert-True ($dryRun.Output.Contains("Atomic runtime zero-legacy audit")) "Release dry-run dropped the targeted atomic audit."
Assert-True ($dryRun.Output.Contains(".\ops\windows\verify-l12.ps1")) "Release dry-run does not invoke the commit-level verifier."

$verifySource = Get-Content -LiteralPath $verifyScript -Raw
Assert-True (([regex]::Matches($verifySource, 'Invoke-External dotnet test "\.\\TwelveLegions\.Tests')).Count -eq 1) "Commit-level verifier must run full rules exactly once."
Assert-True (([regex]::Matches($verifySource, 'Invoke-External dotnet test [^\r\n]+PlatformStoreTests')).Count -eq 1) "Commit-level verifier must run filtered platform tests exactly once."
Assert-True (([regex]::Matches($verifySource, 'Invoke-External \$npmExecutable ci')).Count -eq 1) "Commit-level verifier must install the isolated frontend exactly once."
Assert-True (([regex]::Matches($verifySource, 'Invoke-External \$npmExecutable run build')).Count -eq 1) "Commit-level verifier must build the isolated frontend exactly once."

$fixtureBase = if (Test-Path -LiteralPath "D:\GPT\Legion12") { "D:\GPT\Legion12\temp" } else { [IO.Path]::GetTempPath() }
New-Item -ItemType Directory -Path $fixtureBase -Force | Out-Null
$fixtureRoot = Join-Path $fixtureBase "l12-release-gate-$([Guid]::NewGuid().ToString('N'))"
$fixtureRepo = Join-Path $fixtureRoot "repo"
$fixtureOutput = Join-Path $fixtureRoot "artifacts"
$fixtureCache = Join-Path $fixtureRoot "cache"
$fixtureCardAssets = Join-Path $fixtureRoot "card-assets"
$fakeBin = Join-Path $fixtureRoot "fake-bin"
$commandLog = Join-Path $fixtureRoot "commands.log"
$originalPath = $env:PATH
$originalCommandLog = $env:L12_TEST_COMMAND_LOG

try {
    New-Item -ItemType Directory -Path (Join-Path $fixtureRepo "ops\windows"), (Join-Path $fixtureRepo "scripts"), (Join-Path $fixtureRepo "TwelveLegions.Tests"), $fixtureOutput, $fixtureCardAssets, $fakeBin -Force | Out-Null
    Copy-Item -LiteralPath $verifyScript -Destination (Join-Path $fixtureRepo "ops\windows\verify-l12.ps1") -Force
    Copy-Item -LiteralPath $cacheInitializer -Destination (Join-Path $fixtureRepo "ops\windows\Initialize-L12BuildEnvironment.ps1") -Force
    Copy-Item -LiteralPath $changeGateScript -Destination (Join-Path $fixtureRepo "scripts\verify-l12-change.ps1") -Force
    [IO.File]::WriteAllText((Join-Path $fixtureRepo "tracked.txt"), "clean fixture`n", [Text.Encoding]::ASCII)

    Invoke-GitChecked $fixtureRepo @("init", "--quiet")
    Invoke-GitChecked $fixtureRepo @("config", "user.email", "release-gate@example.invalid")
    Invoke-GitChecked $fixtureRepo @("config", "user.name", "L12 Release Gate")
    Invoke-GitChecked $fixtureRepo @("add", ".")
    Invoke-GitChecked $fixtureRepo @("commit", "--quiet", "-m", "release gate fixture")
    Push-Location $fixtureRepo
    try {
        $primaryBranch = (& git branch --show-current).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($primaryBranch)) { throw "Unable to read fixture branch." }
    }
    finally { Pop-Location }

    Invoke-GitChecked $fixtureRepo @("switch", "--quiet", "-c", "release-probe")
    [IO.File]::WriteAllText((Join-Path $fixtureRepo "TwelveLegions.Tests\ReleaseGateProbe.cs"), "// committed card-effect probe`n", [Text.Encoding]::ASCII)
    Invoke-GitChecked $fixtureRepo @("add", "TwelveLegions.Tests/ReleaseGateProbe.cs")
    Invoke-GitChecked $fixtureRepo @("commit", "--quiet", "-m", "add card-effect probe")
    Invoke-GitChecked $fixtureRepo @("switch", "--quiet", $primaryBranch)
    [IO.File]::WriteAllText((Join-Path $fixtureRepo "first-parent.txt"), "first parent`n", [Text.Encoding]::ASCII)
    Invoke-GitChecked $fixtureRepo @("add", "first-parent.txt")
    Invoke-GitChecked $fixtureRepo @("commit", "--quiet", "-m", "advance first parent")
    Invoke-GitChecked $fixtureRepo @("merge", "--quiet", "--no-ff", "release-probe", "-m", "merge release probe")
    Push-Location $fixtureRepo
    try {
        $fixtureCommit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to read fixture merge commit." }
    }
    finally { Pop-Location }

    # With an empty porcelain status and no explicit ChangedPaths, Release must
    # classify the committed HEAD diff so card-effect declaration/atomic audits
    # remain active while the full runtime tests are still delegated only once.
    $fixtureChangeGate = Join-Path $fixtureRepo "scripts\verify-l12-change.ps1"
    $cleanReleasePlan = Invoke-ChildPowerShell -ScriptPath $fixtureChangeGate -Arguments @("-Level", "Release", "-DryRun")
    Assert-True ($cleanReleasePlan.ExitCode -eq 0) "Clean committed Release dry-run failed: $($cleanReleasePlan.Output)"
    Assert-True ($cleanReleasePlan.Output.Contains("TwelveLegions.Tests/ReleaseGateProbe.cs")) "Clean Release did not classify the committed HEAD diff."
    Assert-True ($cleanReleasePlan.Output.Contains("Public active predeclaration guard")) "Clean committed card-effect change dropped declaration audits."
    Assert-True ($cleanReleasePlan.Output.Contains("Atomic runtime zero-legacy audit")) "Clean committed card-effect change dropped the atomic audit."
    Assert-True (([regex]::Matches($cleanReleasePlan.Output, "Commit-level release verification \(no deployment\)")).Count -eq 1) "Clean Release must schedule the commit-level verifier exactly once."
    foreach ($duplicateLabel in @("L12 full rule tests", "Platform persistence release gate", "Frontend production build")) {
        Assert-True (-not $cleanReleasePlan.Output.Contains($duplicateLabel)) "Clean Release still schedules duplicate work: $duplicateLabel"
    }

    foreach ($fakeCommand in @(
        @{ Name = "node"; ExitCode = 0 },
        @{ Name = "dotnet"; ExitCode = 91 },
        @{ Name = "npm"; ExitCode = 92 },
        @{ Name = "tar"; ExitCode = 93 }
    )) {
        Write-FakeCommand -Directory $fakeBin -Name $fakeCommand.Name -ExitCode $fakeCommand.ExitCode
    }
    $env:PATH = "$fakeBin$([IO.Path]::PathSeparator)$originalPath"
    $env:L12_TEST_COMMAND_LOG = $commandLog

    $assetVersionA = "a" * 64
    $assetVersionB = "b" * 64
    $cardManifestPath = Join-Path $fixtureCardAssets "card-assets.manifest.json"
    Write-CardManifest -Path $cardManifestPath -AssetVersion $assetVersionA

    $artifactDirectory = Join-Path $fixtureOutput $fixtureCommit
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $releaseArchive = Join-Path $artifactDirectory "l12-release-$fixtureCommit.tar.gz"
    $cardArchive = Join-Path $fixtureOutput "l12-card-assets-$assetVersionA.tar.gz"
    [IO.File]::WriteAllText($releaseArchive, "verified release", [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($cardArchive, "verified cards", [Text.Encoding]::ASCII)
    $releaseSha = (Get-FileHash -LiteralPath $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $cardSha = (Get-FileHash -LiteralPath $cardArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $cachedManifest = Join-Path $artifactDirectory "l12-release-$fixtureCommit.json"
    [ordered]@{
        schema = 3
        commit = $fixtureCommit
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        releaseArchive = $releaseArchive
        releaseSha256 = $releaseSha
        cardAssetsHash = $assetVersionA
        cardAssetsArchive = $cardArchive
        cardAssetsSha256 = $cardSha
    } | ConvertTo-Json | Set-Content -LiteralPath $cachedManifest -Encoding utf8

    Remove-Item -LiteralPath $commandLog -Force -ErrorAction SilentlyContinue
    $fixtureVerify = Join-Path $fixtureRepo "ops\windows\verify-l12.ps1"
    $verifyArguments = @(
        "-OutputDirectory", $fixtureOutput,
        "-CacheRoot", $fixtureCache,
        "-CardAssetDirectory", $fixtureCardAssets
    )
    $cacheHit = Invoke-ChildPowerShell -ScriptPath $fixtureVerify -Arguments $verifyArguments
    Assert-True ($cacheHit.ExitCode -eq 0) "Valid commit/card cache was not reused: $($cacheHit.Output)"
    Assert-True ($cacheHit.Output.Contains($cachedManifest)) "Valid cache did not return its manifest path."
    $cacheHitCommands = if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" }
    Assert-True (-not $cacheHitCommands.Contains("dotnet ")) "Valid cache unexpectedly reran full rule tests."

    # A still-valid archive hash must not make a cache entry reusable after the
    # current card asset version changes.
    Write-CardManifest -Path $cardManifestPath -AssetVersion $assetVersionB
    Remove-Item -LiteralPath $commandLog -Force -ErrorAction SilentlyContinue
    $staleCardCache = Invoke-ChildPowerShell -ScriptPath $fixtureVerify -Arguments $verifyArguments
    Assert-True ($staleCardCache.ExitCode -ne 0) "Stale cardAssetsHash cache was incorrectly reused."
    Assert-True (-not $staleCardCache.Output.Contains($cachedManifest)) "Stale cardAssetsHash cache returned the old manifest."
    $staleCommands = if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" }
    Assert-True ($staleCommands.Contains("dotnet test")) "Card asset version mismatch did not fall through to fresh full verification."

    # Both entry points must fail before audit/build/cache reuse when any tracked
    # or untracked content makes the commit identity ambiguous.
    Write-CardManifest -Path $cardManifestPath -AssetVersion $assetVersionA
    [IO.File]::WriteAllText((Join-Path $fixtureRepo "dirty-untracked.txt"), "dirty`n", [Text.Encoding]::ASCII)
    Remove-Item -LiteralPath $commandLog -Force -ErrorAction SilentlyContinue
    $dirtyVerify = Invoke-ChildPowerShell -ScriptPath $fixtureVerify -Arguments $verifyArguments
    Assert-True ($dirtyVerify.ExitCode -ne 0) "Direct verifier accepted a dirty worktree."
    Assert-True (-not $dirtyVerify.Output.Contains($cachedManifest)) "Direct verifier reused an old HEAD cache from a dirty worktree."
    $dirtyCommands = if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" }
    Assert-True ([string]::IsNullOrWhiteSpace($dirtyCommands)) "Dirty direct verification executed audit or build commands before failing."

    $dirtyRelease = Invoke-ChildPowerShell -ScriptPath $fixtureChangeGate -Arguments @(
        "-Level", "Release",
        "-CacheRoot", $fixtureCache
    )
    Assert-True ($dirtyRelease.ExitCode -ne 0) "Release change gate accepted a dirty worktree."
    Assert-True ($dirtyRelease.Output.Contains("clean committed tree")) "Dirty Release failure did not explain the clean-commit requirement."
}
finally {
    $env:PATH = $originalPath
    $env:L12_TEST_COMMAND_LOG = $originalCommandLog
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixtureRoot = (Resolve-Path -LiteralPath $fixtureRoot).Path
        $resolvedFixtureBase = [IO.Path]::GetFullPath($fixtureBase)
        if (-not $resolvedFixtureRoot.StartsWith($resolvedFixtureBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean fixture outside the governed temporary root: $resolvedFixtureRoot"
        }
        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse -Force
    }
}

Write-Host "[L12 release gate] dry-run single-pass, dirty-tree rejection, and commit/card cache binding passed."
