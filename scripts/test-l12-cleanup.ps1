[CmdletBinding()]
param([string]$FixtureParent = 'D:\GPT\Legion12\temp')
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $FixtureParent ('cleanup-test-' + [Guid]::NewGuid().ToString('N'))
$cleaner = Join-Path $PSScriptRoot 'clean-l12-generated.ps1'
New-Item -ItemType Directory -Path "$fixture\app", "$fixture\artifacts\deploy", "$fixture\temp" -Force | Out-Null
git -C "$fixture\app" init -q
git -C "$fixture\app" -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty -qm fixture
if ($LASTEXITCODE -ne 0) { throw 'Fixture Git initialization failed' }
$fakeProcesses = @()
# Controlled process snapshots make active/unknown states deterministic.
function Get-CimInstance { param($ClassName, $ErrorAction) $fakeProcesses }
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Write-Fixture([string]$Path, [string]$Content='fixture') {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    Set-Content -LiteralPath $Path -Value $Content
}
$deploy = "$fixture\artifacts\deploy"
$production = 'a' * 40; $rollback = 'b' * 40; $old = 'c' * 40; $testCommit = 'd' * 40
foreach ($hash in @($production,$rollback,$old,$testCommit)) {
    $archive = "$deploy\$hash\l12-release-$hash.tar.gz"
    $card = "$deploy\l12-card-assets-$hash.tar.gz"
    Write-Fixture $archive; Write-Fixture $card
    @{schema=1;commit=$hash;releaseArchive=$archive;releaseSha256=(Get-FileHash $archive).Hash;cardAssetsArchive=$card;cardAssetsSha256=(Get-FileHash $card).Hash} | ConvertTo-Json | Set-Content "$deploy\$hash\l12-release-$hash.json"
    Get-ChildItem -LiteralPath "$deploy\$hash" -Force | ForEach-Object { $_.LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-13) }
    (Get-Item -LiteralPath $card).LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-13)
}
New-Item -ItemType Directory -Path "$deploy\$('e'*40)" | Out-Null
Write-Fixture "$deploy\$('f'*40)\l12-release-$('f'*40).json" '{}'
foreach($hash in @(('e'*40),('f'*40))){(Get-Item "$deploy\$hash").LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-15)}
# The actual production pair must survive even when mtimes are older.
(Get-Item "$deploy\$production").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-10)
(Get-Item "$deploy\$rollback").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-9)
(Get-Item "$deploy\$old").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-11)
(Get-Item "$deploy\$testCommit").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-12)
Write-Fixture "$fixture\temp\old.txt"
(Get-Item "$fixture\temp\old.txt").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2)
Write-Fixture "$fixture\temp\fresh.txt"
Write-Fixture "$fixture\app\bin\keep.txt"
Write-Fixture "$fixture\repo\node_modules\keep.txt"
Write-Fixture "$fixture\artifacts\verify-expired\sample.tar.gz"
Get-ChildItem "$fixture\artifacts\verify-expired" -Recurse -Force | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2) }
(Get-Item "$fixture\artifacts\verify-expired").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2)
function Write-Evidence([string]$Path, [string]$Status='success', [bool]$Passed=$true, [bool]$Empty=$false) {
    $stages = if ($Empty) { @() } else { @(@{name='synthetic';passed=$Passed}) }
    Write-Fixture "$Path\timings.json" (@{schema=1;status=$Status;stages=$stages} | ConvertTo-Json -Depth 4)
    Get-ChildItem -LiteralPath $Path -Recurse -Force | ForEach-Object { $_.LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-3) }
    (Get-Item -LiteralPath $Path).LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-3)
}
foreach ($base in @("$fixture\artifacts\test-runs", "$deploy\verification-evidence\$production")) {
    foreach ($name in @('old-success','newer-success','newest-success')) { Write-Evidence "$base\$name" }
    (Get-Item "$base\newer-success").LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-2.5)
    (Get-Item "$base\newest-success").LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-2)
    Write-Evidence "$base\failed" 'failed' $false
    Write-Evidence "$base\false-success" 'success' $false
    Write-Evidence "$base\empty-stages" 'success' $true $true
    Write-Fixture "$base\unknown\fixture.db"
    Write-Evidence "$base\pinned"
    Write-Fixture "$base\pinned\PINNED"; (Get-Item "$base\pinned\PINNED").LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-3)
}
Write-Evidence "$fixture\temp\closed-success"
Write-Evidence "$fixture\temp\failed" 'failed' $false
$argsForCleanup = @{Root=$fixture;ProductionCommit=$production;RollbackCommit=$rollback;TestCommit=$testCommit;ObsoleteVerificationDirectory=@('verify-expired')}
& $cleaner -Root $fixture -ProductionCommit $production -RollbackCommit $rollback -Apply | Out-Null
Assert (Test-Path "$deploy\$old") 'Missing test version must protect deployment artifacts'
& $cleaner @argsForCleanup | Out-Null
Assert (Test-Path "$fixture\temp\old.txt") 'Dry run deleted a file'
# Missing/corrupt pinned artifacts must fail before any cleanup.
$savedHash = Get-Content "$deploy\$production\l12-release-$production.json" -Raw
Write-Fixture "$deploy\$production\l12-release-$production.tar.gz" 'changed'
$failed = $false
try { & $cleaner @argsForCleanup -Apply | Out-Null } catch { $failed = $true }
Assert $failed 'Corrupt pinned archive was accepted'
Assert (Test-Path "$fixture\temp\old.txt") 'Failure partially cleaned data'
Write-Fixture "$deploy\$production\l12-release-$production.tar.gz"
(Get-Item "$deploy\$production\l12-release-$production.tar.gz").LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-13)
$fakeProcesses = @([pscustomobject]@{Name='node.exe';CommandLine="node $fixture\temp\old.txt"})
& $cleaner @argsForCleanup -Apply | Out-Null
Assert (Test-Path "$fixture\temp\old.txt") 'Active file deleted'
Assert (-not (Test-Path "$deploy\$old")) 'Old unretained release not removed'
Assert (-not (Test-Path "$deploy\l12-card-assets-$old.tar.gz")) 'Unreferenced archive not removed'
Assert (Test-Path "$deploy\$production") 'Production release removed'
Assert (Test-Path "$deploy\$rollback") 'Rollback release removed'
Assert (Test-Path "$deploy\$testCommit") 'Test release removed'
Assert (Test-Path "$deploy\$('e'*40)") 'Empty/unknown staging removed'
Assert (Test-Path "$deploy\$('f'*40)") 'Invalid manifest staging removed'
Assert (Test-Path "$deploy\l12-card-assets-$rollback.tar.gz") 'Rollback shared dependency removed'
Assert (Test-Path "$fixture\temp\fresh.txt") 'Fresh temp removed'
Assert (Test-Path "$fixture\app\bin\keep.txt") 'Hot build output removed'
Assert (Test-Path "$fixture\repo\node_modules\keep.txt") 'Other worktree modified'
Assert (-not (Test-Path "$fixture\artifacts\verify-expired")) 'Reviewed expired archives not removed'
foreach ($base in @("$fixture\artifacts\test-runs", "$deploy\verification-evidence\$production")) {
    Assert (-not (Test-Path "$base\old-success")) 'Old success was not reclaimed'
    foreach ($name in @('newer-success','newest-success','failed','false-success','empty-stages','unknown','pinned')) {
        Assert (Test-Path "$base\$name") "Protected evidence lost: $name"
    }
}
Assert (-not (Test-Path "$fixture\temp\closed-success")) 'Closed temp not reclaimed'
Assert (Test-Path "$fixture\temp\failed") 'Failed temp lost'
$fakeProcesses = @()
# A junction must fail before enumeration/deletion, preserving its target.
New-Item -ItemType Directory -Path "$fixture\outside" | Out-Null
Write-Fixture "$fixture\outside\keep.txt"
New-Item -ItemType Junction -Path "$fixture\temp\linked" -Target "$fixture\outside" | Out-Null
$failed = $false
try { & $cleaner @argsForCleanup -Apply | Out-Null } catch { $failed = $true }
Assert $failed 'Junction accepted'
Assert (Test-Path "$fixture\outside\keep.txt") 'Junction target damaged'
Assert (Test-Path "$fixture\temp\old.txt") 'Link failure caused partial deletion'
# Unlink ONLY the fixture junction; never recursively delete its target.
[IO.Directory]::Delete("$fixture\temp\linked")
& $cleaner @argsForCleanup -Apply | Out-Null
Assert (Test-Path "$fixture\temp\old.txt") 'Unowned old temp must be preserved'
& $cleaner @argsForCleanup -Apply | Out-Null
Assert (Test-Path "$fixture\temp\fresh.txt") 'Repeated cleanup changed fresh data'
$receipts = @(Get-ChildItem "$fixture\artifacts\cleanup" -Filter '*.json')
Assert ($receipts.Count -ge 1) 'Cleanup receipts missing'
# Remove only this owned synthetic fixture after all assertions passed.
$resolvedFixture=[IO.Path]::GetFullPath($fixture)
if (-not $resolvedFixture.StartsWith([IO.Path]::GetFullPath($FixtureParent).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $fixture -Leaf) -notmatch '^cleanup-test-[0-9a-f]{32}$') { throw 'Fixture cleanup boundary invalid' }
if (@(Get-ChildItem -LiteralPath $fixture -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Fixture link remains' }
Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
Write-Host 'Cleanup behavioral guards passed; owned synthetic fixture removed.'
