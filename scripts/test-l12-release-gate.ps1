[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$verifyScript = Join-Path $repoRoot "ops\windows\verify-l12.ps1"
$deployScript = Join-Path $repoRoot "ops\windows\deploy-l12.ps1"
$changeGateScript = Join-Path $repoRoot "scripts\verify-l12-change.ps1"
$cacheInitializer = Join-Path $repoRoot "ops\windows\Initialize-L12BuildEnvironment.ps1"
$performanceExceptions = Join-Path $repoRoot "ops\performance-exceptions.json"
$releaseLedgerScript = Join-Path $repoRoot "scripts\release-ledger.mjs"
$releaseLedgerRoot = Join-Path $repoRoot "release-ledger"
$generatedPlayerRelease = Join-Path $repoRoot "opcgpro-vue\src\l12\site\generatedPlayerRelease.ts"
$platformProject = Join-Path $repoRoot "TwelveLegions.Platform.Tests\TwelveLegions.Platform.Tests.csproj"
$accountPrivacyTests = Join-Path $repoRoot "TwelveLegions.Platform.Tests\AccountPrivacyLifecycleTests.cs"
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
        cardCount = 366
        playableCardCount = 324
        presentationCardCount = 42
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
Assert-True (-not $dryRun.Output.Contains("Low-latency performance architecture lock")) "Release dry-run must leave the complete performance lock to the isolated frontend build."
Assert-True ($dryRun.Output.Contains("Atomic runtime zero-legacy audit")) "Release dry-run dropped the targeted atomic audit."
Assert-True ($dryRun.Output.Contains(".\ops\windows\verify-l12.ps1")) "Release dry-run does not invoke the commit-level verifier."
Assert-True (-not $dryRun.Output.Contains("-ProductionBaseCommit")) "Unbound Release must retain its original default behavior."
$fixtureBaseSha = "d1659fa3" + ("0" * 32)
$boundReleasePlan = Invoke-ChildPowerShell -ScriptPath $changeGateScript -Arguments @(
    "-Level", "Release", "-DryRun", "-ChangedPaths", "opcgpro-vue/src/l12/platform.ts", "-ProductionBaseCommit", $fixtureBaseSha
)
Assert-True ($boundReleasePlan.ExitCode -eq 0 -and $boundReleasePlan.Output.Contains("-ProductionBaseCommit $fixtureBaseSha")) "Release did not pass the exact formal base commit to the isolated verifier."
$shortBasePlan = Invoke-ChildPowerShell -ScriptPath $changeGateScript -Arguments @(
    "-Level", "Release", "-DryRun", "-ChangedPaths", "opcgpro-vue/src/l12/platform.ts", "-ProductionBaseCommit", "d1659fa3"
)
Assert-True ($shortBasePlan.ExitCode -ne 0 -and $shortBasePlan.Output.Contains("40")) "Release accepted a short production base commit."
$blankBasePlan = Invoke-ChildPowerShell -ScriptPath $changeGateScript -Arguments @(
    "-Level", "Release", "-DryRun", "-ChangedPaths", "opcgpro-vue/src/l12/platform.ts", "-ProductionBaseCommit", " "
)
Assert-True ($blankBasePlan.ExitCode -ne 0 -and $blankBasePlan.Output.Contains("40")) "Release silently ignored a whitespace production base commit."
$batchBasePlan = Invoke-ChildPowerShell -ScriptPath $changeGateScript -Arguments @(
    "-Level", "Batch", "-DryRun", "-ChangedPaths", "opcgpro-vue/src/l12/platform.ts", "-ProductionBaseCommit", $fixtureBaseSha
)
Assert-True ($batchBasePlan.ExitCode -ne 0 -and $batchBasePlan.Output.Contains("仅可用于")) "Non-Release gate accepted a production base commit."

# A platform-only source must not pay for the rule engine, and a rule-only
# source must not miss its semantic audits. Unknown shared sources run both.
foreach ($case in @(
    @{ Path = "服务端WebSocket/TwelveLegions/L12PlatformStore.Seasons.cs"; Platform = $true; Rules = $false; Audit = $false },
    @{ Path = "服务端WebSocket/TwelveLegions/L12GameEngine.cs"; Platform = $false; Rules = $true; Audit = $true },
    @{ Path = "服务端WebSocket/TwelveLegions/L12RoomManager.cs"; Platform = $true; Rules = $true; Audit = $true },
    @{ Path = "服务端WebSocket/TwelveLegions/L12RoomManager.ResponsePreferences.cs"; Platform = $true; Rules = $true; Audit = $true },
    @{ Path = "TwelveLegions.Platform.Tests/AccountPrivacyLifecycleTests.cs"; Platform = $true; Rules = $false; Audit = $false },
    @{ Path = "TwelveLegions.Tests/RuleKernelTests.cs"; Platform = $false; Rules = $true; Audit = $true }
)) {
    $plan = Invoke-ChildPowerShell -ScriptPath $changeGateScript -Arguments @("-Level", "Batch", "-DryRun", "-ChangedPaths", $case.Path)
    Assert-True ($plan.ExitCode -eq 0) "Selection dry-run failed for $($case.Path): $($plan.Output)"
    Assert-True ($plan.Output.Contains("Platform persistence release gate") -eq $case.Platform) "Platform selection mismatch for $($case.Path)"
    Assert-True ($plan.Output.Contains("L12 full rule tests") -eq $case.Rules) "Rule selection mismatch for $($case.Path)"
    Assert-True ($plan.Output.Contains("Atomic runtime zero-legacy audit") -eq $case.Audit) "Card-effect audit selection mismatch for $($case.Path)"
}

$verifySource = Get-Content -LiteralPath $verifyScript -Raw
$deploySource = Get-Content -LiteralPath $deployScript -Raw
$changeGateSource = Get-Content -LiteralPath $changeGateScript -Raw
$platformProjectSource = Get-Content -LiteralPath $platformProject -Raw
$accountPrivacySource = Get-Content -LiteralPath $accountPrivacyTests -Raw
Assert-True (-not $verifySource.Contains('FullyQualifiedName!~EmailAuthAndAccountLifecycleTests')) "Release gate must not hide active platform tests with a class-name filter."
Assert-True (-not $changeGateSource.Contains('FullyQualifiedName!~EmailAuthAndAccountLifecycleTests')) "Batch gate must not hide active platform tests with a class-name filter."
Assert-True ($platformProjectSource.Contains('<Compile Remove="EmailAuthAndAccountLifecycleTests.cs" />')) "Retired email scenarios must be explicitly outside the active product suite."
foreach ($activeTest in @("AdminResetAndLogicalDeletionProtectRootAndSelfAndScrubPersonalData", "MatchRecorderAnonymizesNamesDeckLabelsAndRecordedJson")) {
    Assert-True ($accountPrivacySource.Contains($activeTest)) "Active account/privacy regression is missing: $activeTest"
}
Assert-True (([regex]::Matches($verifySource, 'Invoke-TimedExternal "rules" dotnet test "\.\\TwelveLegions\.Tests')).Count -eq 1) "Commit-level verifier must run full rules exactly once."
Assert-True (([regex]::Matches($verifySource, 'Invoke-TimedExternal "platform" dotnet test "\.\\TwelveLegions\.Platform\.Tests')).Count -eq 1) "Commit-level verifier must run the dedicated platform suite exactly once."
Assert-True ($verifySource.Contains('LogFileName=rules.trx') -and $verifySource.Contains('LogFileName=platform.trx') -and
    $verifySource.Contains('"--results-directory", $evidenceDirectory') -and $verifySource.Contains('trxOmittedForBudget = $trxOmittedForBudget')) "Both full suites must write distinct TRX files unless the budget omission is explicitly recorded."
Assert-True (([regex]::Matches($verifySource, 'Invoke-External node "\.\\scripts\\test-release-status\.mjs"')).Count -eq 1) "Commit-level verifier must validate release state source exactly once."
Assert-True (([regex]::Matches($verifySource, 'Invoke-TimedExternal "frontend-npm-ci" \$npmExecutable ci')).Count -eq 1) "Commit-level verifier must install the isolated frontend exactly once."
Assert-True (([regex]::Matches($verifySource, 'Invoke-TimedExternal "frontend-build" \$npmExecutable run build')).Count -eq 1) "Commit-level verifier must build the isolated frontend exactly once."
Assert-True ($deploySource.Contains('$cardAssetsProbe = if ($ServerArtifactRoot -eq "/www/legion12")')) "Deployment must probe the server content-addressed card cache before upload."
Assert-True (([regex]::Matches($deploySource, 'if \(\$cardAssetsCached\)')).Count -eq 1 -and $deploySource.Contains('$cardAssetsHash')) "Deployment must explicitly reuse a matching card asset hash."
Assert-True ($deploySource.IndexOf('Invoke-External scp @sshOptions $cardAssetsArchive') -gt $deploySource.IndexOf('else {', $deploySource.IndexOf('if ($cardAssetsCached)'))) "Card asset upload must remain confined to the remote-cache-miss branch."
Assert-True ($deploySource.Contains('Resolve-L12ProductionBaseCommit')) "Production deployment must read the live production commit before aggregating player notes."
Assert-True ($deploySource.Contains('"-ProductionBaseCommit", $productionBaseCommit')) "Production deployment must bind release verification to the live production commit."
$staleReleaseBaseMessage = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
    '5Y+R5biD5YyF55qE5pu05paw5pel5b+X5Z+657q/5LiN5piv5b2T5YmN5q2j5byP5pyN5o+Q5Lqk'))
Assert-True ($deploySource.Contains($staleReleaseBaseMessage)) "A stale or prebuilt manifest must not bypass the production changelog range gate."

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
    New-Item -ItemType Directory -Path (Join-Path $fixtureRepo "ops\windows"), (Join-Path $fixtureRepo "scripts"), (Join-Path $fixtureRepo "TwelveLegions.Tests"), (Join-Path $fixtureRepo "opcgpro-vue\src\l12\site"), $fixtureOutput, $fixtureCardAssets, $fakeBin -Force | Out-Null
    Copy-Item -LiteralPath $verifyScript -Destination (Join-Path $fixtureRepo "ops\windows\verify-l12.ps1") -Force
    New-Item -ItemType Directory -Path (Join-Path $fixtureRepo 'scripts/lib') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/l12-test-storage.ps1') -Destination (Join-Path $fixtureRepo 'scripts/lib/l12-test-storage.ps1')
    Copy-Item -LiteralPath $cacheInitializer -Destination (Join-Path $fixtureRepo "ops\windows\Initialize-L12BuildEnvironment.ps1") -Force
    Copy-Item -LiteralPath $performanceExceptions -Destination (Join-Path $fixtureRepo "ops\performance-exceptions.json") -Force
    Copy-Item -LiteralPath $changeGateScript -Destination (Join-Path $fixtureRepo "scripts\verify-l12-change.ps1") -Force
    Copy-Item -LiteralPath $releaseLedgerScript -Destination (Join-Path $fixtureRepo "scripts\release-ledger.mjs") -Force
    Copy-Item -LiteralPath $releaseLedgerRoot -Destination (Join-Path $fixtureRepo "release-ledger") -Recurse -Force
    Copy-Item -LiteralPath $generatedPlayerRelease -Destination (Join-Path $fixtureRepo "opcgpro-vue\src\l12\site\generatedPlayerRelease.ts") -Force
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
    Assert-True (-not $cleanReleasePlan.Output.Contains("Low-latency performance architecture lock")) "Clean Release must not repeat the isolated build performance lock."
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
    $fixtureNotesHash = (Get-FileHash -LiteralPath (Join-Path $fixtureRepo "opcgpro-vue\src\l12\site\generatedPlayerRelease.ts") -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        schema = 3
        commit = $fixtureCommit
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        releaseArchive = $releaseArchive
        releaseSha256 = $releaseSha
        cardAssetsHash = $assetVersionA
        cardAssetsArchive = $cardArchive
        cardAssetsSha256 = $cardSha
        releaseBaseCommit = ""
        playerReleaseNotesSha256 = $fixtureNotesHash
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
    Assert-True ($staleCommands.Contains("LogFileName=rules.trx") -and $staleCommands.Contains("--results-directory")) "Full rules did not request an isolated TRX result."
    $failureEvidenceRoot = Join-Path (Join-Path $fixtureOutput "verification-evidence") $fixtureCommit
    $failureRuns = @(Get-ChildItem -LiteralPath $failureEvidenceRoot -Directory)
    Assert-True ($failureRuns.Count -eq 1) "Failed full verification did not retain one unique D-drive evidence run."
    $failureTiming = Get-Content -LiteralPath (Join-Path $failureRuns[0].FullName "timings.json") -Raw | ConvertFrom-Json
    Assert-True ($failureTiming.status -eq "failure" -and $failureTiming.stages.Count -eq 1 -and
        $failureTiming.stages[0].stage -eq "rules" -and -not $failureTiming.stages[0].passed -and
        -not $failureTiming.trxOmittedForBudget) "Failed rule stage did not retain its elapsed time, failure status and TRX intent."

    # A full evidence directory may omit optional TRX, but must still execute
    # the complete rule command and mark the omission in durable timing output.
    Remove-Item -LiteralPath $commandLog -Force -ErrorAction SilentlyContinue
    $budgetRun = Invoke-ChildPowerShell -ScriptPath $fixtureVerify -Arguments ($verifyArguments + @("-EvidenceBudgetBytes", "1"))
    Assert-True ($budgetRun.ExitCode -ne 0 -and $budgetRun.Output.Contains("不新增可选逐用例 TRX")) "Budgeted verification did not warn while continuing to the full rule suite."
    $budgetCommands = if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" }
    Assert-True ($budgetCommands.Contains("dotnet test") -and $budgetCommands.Contains("LogFileName=pass-proof.trx") -and -not $budgetCommands.Contains("LogFileName=rules.trx")) "Evidence budget changed coverage or dropped the mandatory cleanup pass proof. Commands: $budgetCommands Output: $($budgetRun.Output)"
    $budgetRuns = @(Get-ChildItem -LiteralPath $failureEvidenceRoot -Directory)
    Assert-True ($budgetRuns.Count -eq 2) "Budgeted verification did not retain a separate stage record."
    $budgetTimings = @($budgetRuns | ForEach-Object { Get-Content -LiteralPath (Join-Path $_.FullName "timings.json") -Raw | ConvertFrom-Json })
    Assert-True (@($budgetTimings | Where-Object { $_.trxOmittedForBudget -and $_.status -eq "failure" }).Count -eq 1) "Budgeted failure did not record that its TRX was omitted."

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

    # An expired exception must reject even an otherwise valid cached archive,
    # before the test/build commands run and without deleting prior failure TRX.
    Remove-Item -LiteralPath (Join-Path $fixtureRepo "dirty-untracked.txt") -Force
    $fixtureExceptions = Join-Path $fixtureRepo "ops\performance-exceptions.json"
    $expiredDocument = Get-Content -LiteralPath $fixtureExceptions -Raw | ConvertFrom-Json
    $expiredDocument.exceptions[0].expiresAt = "2020-01-01"
    $expiredDocument | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixtureExceptions -Encoding utf8
    Invoke-GitChecked $fixtureRepo @("add", "ops/performance-exceptions.json")
    Invoke-GitChecked $fixtureRepo @("commit", "--quiet", "-m", "expired performance exception fixture")
    Push-Location $fixtureRepo
    try {
        $expiredCommit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to read expired fixture commit." }
    }
    finally { Pop-Location }
    $expiredArtifactDirectory = Join-Path $fixtureOutput $expiredCommit
    New-Item -ItemType Directory -Path $expiredArtifactDirectory -Force | Out-Null
    $expiredManifest = Join-Path $expiredArtifactDirectory "l12-release-$expiredCommit.json"
    [ordered]@{
        schema = 3
        commit = $expiredCommit
        generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
        releaseArchive = $releaseArchive
        releaseSha256 = $releaseSha
        cardAssetsHash = $assetVersionA
        cardAssetsArchive = $cardArchive
        cardAssetsSha256 = $cardSha
        releaseBaseCommit = ""
        playerReleaseNotesSha256 = $fixtureNotesHash
    } | ConvertTo-Json | Set-Content -LiteralPath $expiredManifest -Encoding utf8
    Remove-Item -LiteralPath $commandLog -Force -ErrorAction SilentlyContinue
    $expiredCache = Invoke-ChildPowerShell -ScriptPath $fixtureVerify -Arguments $verifyArguments
    Assert-True ($expiredCache.ExitCode -ne 0 -and $expiredCache.Output.Contains("请修正例外")) "Expired performance exception did not reject cached release reuse."
    Assert-True (-not $expiredCache.Output.Contains($expiredManifest)) "Expired performance exception returned a cached release manifest."
    $expiredCommands = if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" }
    Assert-True (-not $expiredCommands.Contains("dotnet test")) "Expired cached release started the full test run before explaining the exception."
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
