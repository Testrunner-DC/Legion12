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
$production = 'a' * 40; $rollback = 'b' * 40; $old = 'c' * 40
foreach ($hash in @($production,$rollback,$old)) {
    $archive = "$deploy\$hash\l12-release-$hash.tar.gz"
    $card = "$deploy\l12-card-assets-$hash.tar.gz"
    Write-Fixture $archive; Write-Fixture $card
    @{commit=$hash;releaseArchive=$archive;releaseSha256=(Get-FileHash $archive).Hash;cardAssetsArchive=$card;cardAssetsSha256=(Get-FileHash $card).Hash} | ConvertTo-Json | Set-Content "$deploy\$hash\l12-release-$hash.json"
}
# The actual production pair must survive even when mtimes are older.
(Get-Item "$deploy\$production").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-10)
(Get-Item "$deploy\$rollback").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-9)
(Get-Item "$deploy\$old").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-11)
Write-Fixture "$fixture\temp\old.txt"
(Get-Item "$fixture\temp\old.txt").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2)
Write-Fixture "$fixture\temp\fresh.txt"
Write-Fixture "$fixture\app\bin\keep.txt"
Write-Fixture "$fixture\repo\node_modules\keep.txt"
Write-Fixture "$fixture\artifacts\verify-expired\sample.tar.gz"
Get-ChildItem "$fixture\artifacts\verify-expired" -Recurse -Force | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2) }
(Get-Item "$fixture\artifacts\verify-expired").LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2)
$argsForCleanup = @{Root=$fixture;ProductionCommit=$production;RollbackCommit=$rollback;ObsoleteVerificationDirectory=@('verify-expired')}
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
$fakeProcesses = @([pscustomobject]@{Name='node.exe';CommandLine="node $fixture\temp\old.txt"})
& $cleaner @argsForCleanup -Apply | Out-Null
Assert (Test-Path "$fixture\temp\old.txt") 'Active file deleted'
Assert (-not (Test-Path "$deploy\$old")) 'Old unretained release not removed'
Assert (-not (Test-Path "$deploy\l12-card-assets-$old.tar.gz")) 'Unreferenced archive not removed'
Assert (Test-Path "$deploy\$production") 'Production release removed'
Assert (Test-Path "$deploy\$rollback") 'Rollback release removed'
Assert (Test-Path "$deploy\l12-card-assets-$rollback.tar.gz") 'Rollback shared dependency removed'
Assert (Test-Path "$fixture\temp\fresh.txt") 'Fresh temp removed'
Assert (Test-Path "$fixture\app\bin\keep.txt") 'Hot build output removed'
Assert (Test-Path "$fixture\repo\node_modules\keep.txt") 'Other worktree modified'
Assert (-not (Test-Path "$fixture\artifacts\verify-expired")) 'Reviewed expired archives not removed'
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
Assert (-not (Test-Path "$fixture\temp\old.txt")) 'Expired inactive temp retained'
& $cleaner @argsForCleanup -Apply | Out-Null
Assert (Test-Path "$fixture\temp\fresh.txt") 'Repeated cleanup changed fresh data'
$receipts = @(Get-ChildItem "$fixture\artifacts\cleanup" -Filter '*.json')
Assert ($receipts.Count -ge 2) 'Cleanup receipts missing'
Write-Host "Cleanup behavioral guards passed. Small fixture retained: $fixture"
