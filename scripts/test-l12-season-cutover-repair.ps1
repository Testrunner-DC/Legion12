[CmdletBinding()]
param([string]$FixtureBase = "")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Fingerprint([string]$Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($bytes) }
    finally { $sha.Dispose() }
    return (-join ($hash | ForEach-Object { $_.ToString("x2") })).Substring(0, 12)
}

function Invoke-Repair {
    param([string[]]$Arguments, [bool]$ExpectSuccess = $true)
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = (& $python.Source $repair @Arguments 2>&1 | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($ExpectSuccess -and $exitCode -ne 0) { throw "Repair tool failed: $output" }
    if (-not $ExpectSuccess -and $exitCode -eq 0) { throw "Repair tool unexpectedly succeeded: $output" }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$repair = Join-Path $repoRoot "ops\server\repair-l12-season-cutover-blockers.py"
$python = Get-Command python -ErrorAction Stop
$base = if ([string]::IsNullOrWhiteSpace($FixtureBase)) {
    [IO.Path]::GetTempPath()
} else {
    (Resolve-Path -LiteralPath $FixtureBase -ErrorAction Stop).Path
}
$root = Join-Path $base "l12-season-cutover-repair-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root -Force | Out-Null

$seedScript = @'
import hashlib,json,sqlite3,sys
p,evidence_path=sys.argv[1],sys.argv[2]
c=sqlite3.connect(p)
c.executescript("""
CREATE TABLE matches(
 match_id TEXT PRIMARY KEY,room_code TEXT NOT NULL,mode_id TEXT NOT NULL,
 started_utc TEXT NOT NULL,ended_utc TEXT,account_0 TEXT,account_1 TEXT,
 season_id TEXT,initial_state_json TEXT,error TEXT);
CREATE TABLE match_events(id INTEGER PRIMARY KEY,match_id TEXT);
CREATE TABLE ranked_match_runtime(match_id TEXT PRIMARY KEY,room_code TEXT,status TEXT);
CREATE TABLE ranked_settlement_outbox(
 match_id TEXT PRIMARY KEY,payload_json TEXT NOT NULL,payload_hash TEXT NOT NULL,
 status TEXT NOT NULL,attempts INTEGER NOT NULL,last_error TEXT,
 created_utc TEXT NOT NULL,applied_utc TEXT);
CREATE TABLE ranked_recovery_quarantine(
 match_id TEXT PRIMARY KEY,reason TEXT NOT NULL,created_utc TEXT NOT NULL);
""")
for i in range(3):
 c.execute("INSERT INTO matches VALUES(?,?,?,?,?,?,?,?,?,?)",
  (f"legacy-{i}",f"L{i}","legacy",f"2026-08-2{i}T00:00:00Z",None,None,None,None,None,None))
c.execute("INSERT INTO match_events(match_id) VALUES('legacy-0')")
def envelope(name,started,ended,meaningful,first_master,second_master,final_round):
 return {"Version":1,"MatchId":name,"FirstAccountId":f"{name}-one",
  "SecondAccountId":f"{name}-two","FirstMasterId":first_master,
  "SecondMasterId":second_master,"Winner":None,"StartedAt":started,"EndedAt":ended,
  "MeaningfulCommandCount":meaningful,"ConclusionKind":"restore-incompatible",
  "FirstNetworkFingerprint":"","SecondNetworkFingerprint":"","FinalRound":final_round,
  "FirstBrowserFingerprint":"","SecondBrowserFingerprint":""}
for i,name in enumerate(("applied-a","applied-b","applied-c")):
 started=f"2026-09-0{i+1}T00:00:00Z"; ended=f"2026-09-0{i+1}T01:00:00Z"
 c.execute("INSERT INTO matches VALUES(?,?,?,?,?,?,?,?,?,?)",
  (name,f"A{i}","ranked",started,ended,f"{name}-one",f"{name}-two","S01","{}",None))
 payload=json.dumps(envelope(name,started,ended,20+i,f"master-{i}",f"master-{i+1}",6+i),separators=(",",":"))
 digest=hashlib.sha256(payload.encode()).hexdigest()
 c.execute("INSERT INTO ranked_match_runtime VALUES(?,?,?)",(name,f"A{i}","completed"))
 c.execute("INSERT INTO ranked_settlement_outbox VALUES(?,?,?,?,?,?,?,?)",
  (name,payload,digest,"applied",0,None,ended,f"2026-09-0{i+1}T01:00:01Z"))
 c.execute("INSERT INTO ranked_recovery_quarantine VALUES(?,?,?)",
  (name,f"replay-failure-{i}",ended))
 for event in range(i+2): c.execute("INSERT INTO match_events(match_id) VALUES(?)",(name,))
name="decision"
c.execute("INSERT INTO matches VALUES(?,?,?,?,?,?,?,?,?,?)",
 (name,"D1","ranked","2026-09-02T00:00:00Z","2026-09-02T01:00:00Z",
  "one","two",None,"{}",None))
payload=json.dumps(envelope(name,"2026-09-02T00:00:00Z","2026-09-02T01:00:00Z",0,"","",13),separators=(",",":"))
digest=hashlib.sha256(payload.encode()).hexdigest()
c.execute("INSERT INTO ranked_settlement_outbox VALUES(?,?,?,?,?,?,?,?)",
 (name,payload,digest,"quarantined",1,"recorded season identity missing",
  "2026-09-02T01:00:00Z","2026-09-02T01:00:01Z"))
c.execute("INSERT INTO ranked_recovery_quarantine VALUES(?,?,?)",
 (name,"missing authoritative initial state","2026-09-02T01:00:00Z"))
for event in range(5): c.execute("INSERT INTO match_events(match_id) VALUES('decision')")
c.execute("INSERT INTO matches VALUES(?,?,?,?,?,?,?,?,?,?)",
 ("friendly-live","F1","friendly","2026-09-02T00:00:00Z",None,"one","two","S01","{}",None))
c.commit()
records=[]
for row in c.execute("""SELECT q.match_id,q.reason,q.created_utc,m.mode_id,m.started_utc,m.ended_utc,
 m.season_id,m.room_code,r.status,r.room_code,o.status,o.attempts,o.last_error,o.created_utc,
 o.applied_utc,o.payload_json,o.payload_hash,
 (SELECT COUNT(*) FROM match_events e WHERE e.match_id=q.match_id)
 FROM ranked_recovery_quarantine q JOIN matches m ON m.match_id=q.match_id
 LEFT JOIN ranked_match_runtime r ON r.match_id=q.match_id
 LEFT JOIN ranked_settlement_outbox o ON o.match_id=q.match_id ORDER BY q.match_id"""):
 payload=json.loads(row[15]); identity=hashlib.sha256(row[0].encode()).hexdigest()
 records.append({"role":"decision" if row[0]=="decision" else "applied",
  "matchIdentitySha256":identity,"quarantineReason":row[1],"quarantineCreatedUtc":row[2],
  "modeId":row[3],"startedUtc":row[4],"endedUtc":row[5],"recordedSeasonId":row[6],
  "runtimeStatus":row[8],"runtimeRoomMatches":row[9] is None or row[9].lower()==row[7].lower(),
  "outboxStatus":row[10],"attempts":row[11],"lastError":row[12],"outboxCreatedUtc":row[13],
  "appliedUtc":row[14],"payloadHash":row[16],"payloadVersion":payload["Version"],
  "payloadSeasonId":payload.get("SeasonId"),
  "seasonIdentityState":"v1-missing-recorded" if row[6] is None else "v1-recorded-backfill",
  "eventCount":row[17]})
with open(evidence_path,"w",encoding="utf-8") as f: json.dump({"schema":1,"records":records},f)
'@

$inspectScript = @'
import json,sqlite3,sys
c=sqlite3.connect(sys.argv[1])
result={
 "legacy":c.execute("SELECT COUNT(*) FROM matches WHERE mode_id='legacy' AND ended_utc IS NULL").fetchone()[0],
 "quarantine":c.execute("SELECT COUNT(*) FROM ranked_recovery_quarantine").fetchone()[0],
 "pending":c.execute("SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='pending'").fetchone()[0],
 "waived":c.execute("SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='waived'").fetchone()[0],
 "decisionSeason":c.execute("SELECT season_id FROM matches WHERE match_id='decision'").fetchone()[0],
 "decisionStatus":c.execute("SELECT status FROM ranked_settlement_outbox WHERE match_id='decision'").fetchone()[0],
 "audit":c.execute("SELECT COUNT(*) FROM season_cutover_blocker_resolutions").fetchone()[0],
 "completeAudit":c.execute("""SELECT COUNT(*) FROM season_cutover_blocker_resolutions
  WHERE operator='fixture-operator' AND human_reason='fixture season cutover recovery'
    AND tool_version='2.0.0' AND source_commit=? AND length(snapshot_sha256)=64
    AND length(evidence_sha256)=64 AND length(match_identity_sha256)=64
    AND event_count>=0""",("a"*40,)).fetchone()[0]
}
print(json.dumps(result))
'@

try {
    $seed = Join-Path $root "seed.py"
    $evidence = Join-Path $root "evidence.json"
    $inspect = Join-Path $root "inspect.py"
    [IO.File]::WriteAllText($seed, $seedScript, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($inspect, $inspectScript, [Text.UTF8Encoding]::new($false))
    $appliedIds = @("applied-a", "applied-b", "applied-c")
    $appliedFingerprints = @($appliedIds | ForEach-Object { Fingerprint $_ })
    $decisionFingerprint = Fingerprint "decision"

    function Seed-Database([string]$Path) {
        & $python.Source $seed $Path $evidence
        if ($LASTEXITCODE -ne 0) { throw "Fixture creation failed." }
    }
    function Base-Arguments([string]$Database) {
        $arguments = @(
            "--database", $Database,
            "--evidence-file", $evidence,
            "--legacy-before", "2026-09-03T00:00:00Z",
            "--expected-legacy-count", "3",
            "--decision-fingerprint", $decisionFingerprint
        )
        foreach ($item in $appliedFingerprints) {
            $arguments += @("--expected-applied-quarantine-fingerprint", $item)
        }
        return $arguments
    }
    function Apply-Arguments([string]$Receipt) {
        return @(
            "--receipt", $Receipt,
            "--operator", "fixture-operator",
            "--reason", "fixture season cutover recovery",
            "--source-commit", ("a" * 40),
            "--service-name", "L12SeasonCutoverFixture-$([Guid]::NewGuid().ToString('N'))"
        )
    }
    function Inspect-Database([string]$Database) {
        $json = (& $python.Source $inspect $Database) -join [Environment]::NewLine
        if ($LASTEXITCODE -ne 0) { throw "Fixture inspection failed." }
        return $json | ConvertFrom-Json
    }

    $dryDatabase = Join-Path $root "dry.db"
    Seed-Database $dryDatabase
    $beforeHash = (Get-FileHash -LiteralPath $dryDatabase -Algorithm SHA256).Hash
    $dry = Invoke-Repair -Arguments (Base-Arguments $dryDatabase)
    $dryReceipt = $dry.Output | ConvertFrom-Json
    Assert-True ($dryReceipt.mode -eq "dry-run") "Default invocation was not dry-run."
    Assert-True ($dryReceipt.before.readiness.activeMatches -eq 3) "Dry-run did not identify legacy blockers."
    Assert-True ((Get-FileHash -LiteralPath $dryDatabase -Algorithm SHA256).Hash -eq $beforeHash) "Dry-run changed the database."

    $refused = Invoke-Repair -ExpectSuccess $false -Arguments ((Base-Arguments $dryDatabase) + @(
        "--apply", "--snapshot-directory", $root,
        "--service-stopped-ack", "I_HAVE_STOPPED_LEGION12"
    ))
    Assert-True ($refused.Output.Contains("explicit --decision")) "Apply without decision did not fail closed."
    Assert-True ((Get-FileHash -LiteralPath $dryDatabase -Algorithm SHA256).Hash -eq $beforeHash) "Refused apply changed the database."

    $mismatchArguments = @(Base-Arguments $dryDatabase)
    $countIndex = [Array]::IndexOf($mismatchArguments, "--expected-legacy-count")
    $mismatchArguments[$countIndex + 1] = "4"
    $mismatch = Invoke-Repair -ExpectSuccess $false -Arguments $mismatchArguments
    Assert-True ((Get-FileHash -LiteralPath $dryDatabase -Algorithm SHA256).Hash -eq $beforeHash) "Count mismatch changed the database."

    $changedEvidence = Join-Path $root "changed-evidence.json"
    $changed = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json
    ($changed.records | Where-Object role -eq "decision").payloadHash = "0" * 64
    $changed | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $changedEvidence -Encoding utf8
    $evidenceMismatchArguments = @(Base-Arguments $dryDatabase)
    $evidenceIndex = [Array]::IndexOf($evidenceMismatchArguments, "--evidence-file")
    $evidenceMismatchArguments[$evidenceIndex + 1] = $changedEvidence
    $evidenceMismatch = Invoke-Repair -ExpectSuccess $false -Arguments $evidenceMismatchArguments
    Assert-True ($evidenceMismatch.Output.Contains("field payloadHash")) "Changed evidence did not fail closed."
    Assert-True ((Get-FileHash -LiteralPath $dryDatabase -Algorithm SHA256).Hash -eq $beforeHash) "Evidence mismatch changed the database."

    $raceDatabase = Join-Path $root "race.db"
    Seed-Database $raceDatabase
    $raceBeforeHash = (Get-FileHash -LiteralPath $raceDatabase -Algorithm SHA256).Hash
    $raceSnapshots = Join-Path $root "race-snapshots"
    New-Item -ItemType Directory -Path $raceSnapshots | Out-Null
    $raceReceiptPath = Join-Path $root "race-receipt.json"
    $lockerScript = Join-Path $root "hold-write-lock.py"
    $lockerReady = Join-Path $root "locker-ready"
    [IO.File]::WriteAllText($lockerScript, @'
import pathlib,sqlite3,sys,time
c=sqlite3.connect(sys.argv[1])
c.execute("BEGIN IMMEDIATE")
pathlib.Path(sys.argv[2]).write_text("ready",encoding="utf-8")
time.sleep(20)
c.rollback()
'@, [Text.UTF8Encoding]::new($false))
    $locker = Start-Process -FilePath $python.Source -ArgumentList @($lockerScript, $raceDatabase, $lockerReady) -PassThru -WindowStyle Hidden
    try {
        for ($attempt = 0; $attempt -lt 100 -and -not (Test-Path -LiteralPath $lockerReady); $attempt++) {
            Start-Sleep -Milliseconds 50
        }
        Assert-True (Test-Path -LiteralPath $lockerReady) "Race fixture failed to acquire its write lock."
        $race = Invoke-Repair -ExpectSuccess $false -Arguments ((Base-Arguments $raceDatabase) + @(
            "--apply", "--decision", "waive",
            "--snapshot-directory", $raceSnapshots,
            "--service-stopped-ack", "I_HAVE_STOPPED_LEGION12",
            "--now", "2026-10-01T00:29:00Z"
        ) + (Apply-Arguments $raceReceiptPath))
        Assert-True ($race.Output.Contains("locked")) "Concurrent writer did not fail closed before snapshot."
        Assert-True (@(Get-ChildItem -LiteralPath $raceSnapshots -File).Count -eq 0) "Race refusal created a pre-lock snapshot."
    }
    finally {
        if (-not $locker.HasExited) { Stop-Process -Id $locker.Id -Force }
        $locker.WaitForExit()
    }
    Assert-True ((Get-FileHash -LiteralPath $raceDatabase -Algorithm SHA256).Hash -eq $raceBeforeHash) "Race refusal changed the database."

    $settleDatabase = Join-Path $root "settle.db"
    Seed-Database $settleDatabase
    $settleSnapshots = Join-Path $root "settle-snapshots"
    New-Item -ItemType Directory -Path $settleSnapshots | Out-Null
    $settleReceiptPath = Join-Path $root "settle-receipt.json"
    $settle = Invoke-Repair -Arguments ((Base-Arguments $settleDatabase) + @(
        "--apply", "--decision", "settle", "--season-id", "S01",
        "--snapshot-directory", $settleSnapshots,
        "--service-stopped-ack", "I_HAVE_STOPPED_LEGION12",
        "--now", "2026-10-01T00:30:00Z"
    ) + (Apply-Arguments $settleReceiptPath))
    $settleReceipt = $settle.Output | ConvertFrom-Json
    Assert-True ($settleReceipt.after.readiness.pendingSettlements -eq 1) "Settlement repair did not leave one idempotent pending item."
    Assert-True ($settleReceipt.after.readiness.quarantinedSettlements -eq 0) "Settlement repair left quarantine blockers."
    Assert-True ((Test-Path -LiteralPath $settleReceipt.snapshot)) "Settlement repair did not create a snapshot."
    Assert-True ((Get-FileHash -LiteralPath $settleReceipt.snapshot -Algorithm SHA256).Hash.ToLowerInvariant() -eq $settleReceipt.snapshotSha256) "Snapshot hash mismatch."
    $settleState = Inspect-Database $settleDatabase
    Assert-True ($settleState.legacy -eq 0 -and $settleState.quarantine -eq 0) "Settlement repair did not retire legacy/quarantine rows."
    Assert-True ($settleState.decisionSeason -eq "S01" -and $settleState.decisionStatus -eq "pending") "Settlement branch did not stage the explicit season."
    Assert-True ($settleState.audit -eq 7) "Settlement branch did not preserve all resolution evidence."
    Assert-True ($settleState.completeAudit -eq 7) "Settlement branch audit metadata is incomplete."

    $restored = Join-Path $root "restored.db"
    Copy-Item -LiteralPath $settleReceipt.snapshot -Destination $restored
    $restoredStateQuery = @'
import json,sqlite3,sys
c=sqlite3.connect(sys.argv[1])
print(json.dumps({
"legacy":c.execute("SELECT COUNT(*) FROM matches WHERE mode_id='legacy' AND ended_utc IS NULL").fetchone()[0],
"quarantine":c.execute("SELECT COUNT(*) FROM ranked_recovery_quarantine").fetchone()[0],
"outbox":c.execute("SELECT status FROM ranked_settlement_outbox WHERE match_id='decision'").fetchone()[0]
}))
'@
    $restoreInspector = Join-Path $root "inspect-restored.py"
    [IO.File]::WriteAllText($restoreInspector, $restoredStateQuery, [Text.UTF8Encoding]::new($false))
    $restoredState = ((& $python.Source $restoreInspector $restored) -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-True ($restoredState.legacy -eq 3 -and $restoredState.quarantine -eq 4 -and $restoredState.outbox -eq "quarantined") "Snapshot rollback rehearsal did not restore the pre-repair state."

    $waiveDatabase = Join-Path $root "waive.db"
    Seed-Database $waiveDatabase
    $waiveSnapshots = Join-Path $root "waive-snapshots"
    New-Item -ItemType Directory -Path $waiveSnapshots | Out-Null
    $waiveReceiptPath = Join-Path $root "waive-receipt.json"
    $waive = Invoke-Repair -Arguments ((Base-Arguments $waiveDatabase) + @(
        "--apply", "--decision", "waive",
        "--snapshot-directory", $waiveSnapshots,
        "--service-stopped-ack", "I_HAVE_STOPPED_LEGION12",
        "--now", "2026-10-01T00:31:00Z"
    ) + (Apply-Arguments $waiveReceiptPath))
    $waiveReceipt = $waive.Output | ConvertFrom-Json
    Assert-True ([bool]$waiveReceipt.after.readiness.ready) "Waive branch did not fully drain the cutover gate."
    $waiveState = Inspect-Database $waiveDatabase
    Assert-True ($waiveState.decisionStatus -eq "waived" -and $waiveState.waived -eq 1) "Waive branch did not retain a terminal outbox record."
    Assert-True ($waiveState.audit -eq 7) "Waive branch did not preserve all resolution evidence."
    Assert-True ($waiveState.completeAudit -eq 7) "Waive branch audit metadata is incomplete."
    $verifyWaive = Invoke-Repair -Arguments ((Base-Arguments $waiveDatabase) + @(
        "--verify-waive-run", $waiveReceipt.runId,
        "--now", "2026-10-01T00:32:00Z"
    ))
    $verifyWaiveReceipt = $verifyWaive.Output | ConvertFrom-Json
    Assert-True ($verifyWaiveReceipt.mode -eq "verify-waive") "Waive verification did not use the read-only post-check mode."
    Assert-True ([bool]$verifyWaiveReceipt.verification.readiness.ready) "Waive post-check did not confirm a ready gate."
    Assert-True ([bool]$verifyWaiveReceipt.verification.payloadHashPreserved) "Waive post-check did not preserve the payload hash."

    Write-Host "Season cutover repair behavior: PASS"
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
