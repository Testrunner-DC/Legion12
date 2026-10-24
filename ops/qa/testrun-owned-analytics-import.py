"""One fixed, first-append-only analytics fixture import. No network or service control.

The CLI accepts only the fixed legacy operation or an exact SHA-bound owned-control terminal
binding; neither accepts arbitrary paths or scopes. The injectable function is for offline SQLite tests.
An intent without a final receipt is UNKNOWN, never permission to repeat this operation.
"""
from __future__ import annotations

import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import socket
import sqlite3
import stat
import subprocess
import sys
import time
import uuid
from dataclasses import dataclass
from urllib.parse import quote

sys.dont_write_bytecode = True
RUN = "b640f54edf5940e48b4f6f54b394a6ff"
SOURCE_ROOT = Path("/tmp/l12-analytics-fixture-" + RUN)
TARGET = Path("/opt/legion12-testrun-runtime/matches.db")
RECEIPTS = Path("/tmp/l12-analytics-import-" + RUN)
ACTIVE_RECEIPTS = RECEIPTS
DB_SHA = "BBBEB2A345F2B46A649F9E4702D53805D58B11C888CC57E57E825B32F7971B61"
PROOF_SHA = "13C99652713A137E9AE107E116FC94CA954750A395E6D9350ED4C55E7AFAEC45"
TEST_COMMIT = "131989a65ef3b26a22e58e5fe5c480c061aae9cb"
TEST_DLL = "BA157FD1CF7870F94F5159EEE2234ED07B0363D59FC639FEFDD01745D1431AE9"
FORMAL_COMMIT = "546187bcc0034580d53fbe814f5b32939249b08a"
FORMAL_DLL = "818441D28D5DFBE95C052117B22AB234C55BBE7459E8C653F71800F6411581ED"
NONMATCH = {"analytics_daily_accounts", "analytics_online_samples", "analytics_page_views",
            "match_recorder_schema", "player_replay_cleanup_schedule", "sandbox_replay_cleanup_schedule",
            "season_identity_migrations"}
COUNTS = {"matches": 16, "match_action_events": 64, "match_action_requests": 16,
          "match_card_fact_compactions": 16, "match_card_fact_summaries": 171,
          "match_card_facts": 192, "match_deck_cards": 543, "match_events": 16,
          "match_participants": 32, "match_state_checkpoints": 32,
          **dict.fromkeys(("match_disaster_candidates", "match_disaster_captures", "match_disaster_decisions",
             "match_public_deck_bindings", "player_replay_payload_expirations", "ranked_match_runtime",
             "ranked_recovery_quarantine", "ranked_settlement_outbox", "response_preference_outbox",
             "sandbox_recordings", "sandbox_replay_expirations", "season_identity_migration_matches",
             "tournament_result_outbox"), 0)}
META_COUNTS = {"match_recorder_schema": 2, "player_replay_cleanup_schedule": 1,
               "sandbox_replay_cleanup_schedule": 1}


def require(value, code):
    if not value:
        raise RuntimeError(code)


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def file_sha(path):
    require(path.stat().st_size <= 64 * 1024 * 1024, "FILE_BOUND")
    return digest(path.read_bytes())


def plain(path):
    path = Path(path)
    require(path.is_absolute() and path.resolve(strict=True) == path, "PHYSICAL_PATH_REQUIRED")
    require(stat.S_ISREG(path.lstat().st_mode) and not path.is_symlink(), "REGULAR_FILE_REQUIRED")
    return (path.stat().st_dev, path.stat().st_ino)


def reject_forbidden_aliases(paths, forbidden_ids):
    for path in paths:
        if os.path.lexists(path):
            require(plain(Path(path)) not in forbidden_ids, "FORMAL_DATABASE_ALIAS_REJECTED")


def selected_family(source, proof, target, backup):
    return [source, proof, target, backup] + [Path(str(path) + suffix)
            for path in (source, target, backup) for suffix in ("-wal", "-shm", "-journal")]


def durable(path, value):
    data = json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
    finally:
        if os.name == "posix":
            directory = os.open(Path(path).parent, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(directory)
            finally:
                os.close(directory)


def connect(path, *, readonly=False, immutable=False):
    uri = "file:" + quote(Path(path).as_posix(), safe="/:") + ("?mode=ro" if readonly else "?mode=rw")
    if immutable:
        uri += "&immutable=1"
    con = sqlite3.connect(uri, uri=True, timeout=3, isolation_level=None)
    con.execute("PRAGMA foreign_keys=ON")
    con.execute("PRAGMA busy_timeout=3000")
    if readonly:
        con.execute("PRAGMA query_only=ON")
    return con


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


def schema(con):
    return con.execute("SELECT type,name,tbl_name,sql FROM sqlite_master "
                       "WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name").fetchall()


def columns(con, table):
    info = con.execute("PRAGMA table_xinfo(" + identifier(table) + ")").fetchall()
    require(info and all(row[6] == 0 for row in info), "UNSUPPORTED_GENERATED_COLUMN")
    return [row[1] for row in info]


def checked_rows(con, table):
    rows = con.execute("SELECT * FROM " + identifier(table)).fetchall()
    require(len(rows) <= 200000, "ROW_BOUND")
    return rows


def encoded_rows(rows):
    def cell(value):
        if isinstance(value, bytes):
            return ["blob", value.hex()]
        return [type(value).__name__, value]
    encoded = sorted(json.dumps([cell(value) for value in row], ensure_ascii=True,
                     separators=(",", ":"), allow_nan=False).encode() for row in rows)
    require(sum(map(len, encoded)) <= 64 * 1024 * 1024, "DATA_BOUND")
    return digest(b"\n".join(encoded))


def snapshot(con, tables):
    return {name: {"count": len(rows), "sha256": encoded_rows(rows)}
            for name in sorted(tables) for rows in [checked_rows(con, name)]}


def healthy(con):
    require(con.execute("PRAGMA integrity_check").fetchall() == [("ok",)], "INTEGRITY_FAILED")
    require(not con.execute("PRAGMA foreign_key_check").fetchall(), "FOREIGN_KEY_FAILED")


def version_semantics(con, table):
    # migrated_utc is this database's migration history, not a schema version.
    # The full target rows are separately hashed and preserved throughout import.
    require(columns(con, table) == ["component", "version", "migrated_utc"], "VERSION_COLUMNS_CHANGED")
    rows = con.execute("SELECT component,version FROM " + identifier(table) + " ORDER BY component").fetchall()
    require(all(isinstance(component, str) and component and type(version) is int and version >= 0
                for component, version in rows), "VERSION_VALUES_REJECTED")
    return rows


@dataclass(frozen=True)
class Policy:
    run_id: str
    database_sha: str
    proof_sha: str
    counts: dict
    nonmatch: frozenset
    metadata_counts: dict
    version_table: str = "match_recorder_schema"
    assembly_sha: str = TEST_DLL


@dataclass(frozen=True)
class TerminalBinding:
    operation_run_id: str
    fixture_run_id: str
    database_sha: str
    proof_sha: str
    test_pid: int
    test_commit: str
    test_dll: str
    formal_pid: int
    formal_commit: str
    formal_dll: str
    diagnostic_setting: str
    template_source_sha: str

    @property
    def source_root(self):
        return Path("/tmp/l12-analytics-fixture-" + self.fixture_run_id)

    @property
    def receipts(self):
        return Path("/tmp/l12-analytics-import-" + self.fixture_run_id + "-terminal-" + self.operation_run_id)


def parse_terminal_binding(raw, expected_sha):
    require(type(raw) is bytes and 0 < len(raw) <= 8192, "TERMINAL_BINDING_SIZE_REJECTED")
    require(type(expected_sha) is str and re.fullmatch(r"[A-F0-9]{64}", expected_sha)
            and digest(raw) == expected_sha, "TERMINAL_BINDING_SHA_CHANGED")
    def exact_object(pairs):
        result = {}
        for key, selected in pairs:
            require(key not in result, "TERMINAL_BINDING_DUPLICATE_FIELD_REJECTED")
            result[key] = selected
        return result
    value = json.loads(raw.decode("utf-8"), object_pairs_hook=exact_object)
    require(type(value) is dict and set(value) == {"schema", "kind", "operationRunId", "fixtureRunId",
            "databaseSha256", "proofSha256", "test", "formal", "diagnosticSetting", "templateSourceSha256"},
            "TERMINAL_BINDING_SHAPE_REJECTED")
    require(type(value["schema"]) is int and value["schema"] == 1
            and value["kind"] == "terminal-owned-analytics-import-v1",
            "TERMINAL_BINDING_KIND_REJECTED")
    require(all(type(value[name]) is str and re.fullmatch(r"[a-f0-9]{32}", value[name])
                for name in ("operationRunId", "fixtureRunId")), "TERMINAL_BINDING_RUN_REJECTED")
    require(all(type(value[name]) is str and re.fullmatch(r"[A-F0-9]{64}", value[name])
                for name in ("databaseSha256", "proofSha256")), "TERMINAL_BINDING_FIXTURE_REJECTED")
    identities = []
    for name in ("test", "formal"):
        item = value[name]
        require(type(item) is dict and set(item) == {"pid", "commit", "dllSha256"},
                "TERMINAL_BINDING_IDENTITY_SHAPE_REJECTED")
        require(type(item["pid"]) is int and item["pid"] > 0
                and type(item["commit"]) is str and re.fullmatch(r"[a-f0-9]{40}", item["commit"])
                and type(item["dllSha256"]) is str and re.fullmatch(r"[A-F0-9]{64}", item["dllSha256"]),
                "TERMINAL_BINDING_IDENTITY_REJECTED")
        identities.append(item)
    require(identities[0]["pid"] != identities[1]["pid"], "TERMINAL_BINDING_DISTINCT_PID_REQUIRED")
    require(type(value["diagnosticSetting"]) is str
            and re.fullmatch(r"terminal-recipients-v4:30:[a-f0-9]{32}", value["diagnosticSetting"]),
            "TERMINAL_BINDING_DIAGNOSTIC_REJECTED")
    require(type(value["templateSourceSha256"]) is str
            and re.fullmatch(r"[A-F0-9]{64}", value["templateSourceSha256"]),
            "TERMINAL_BINDING_SOURCE_REJECTED")
    return TerminalBinding(value["operationRunId"], value["fixtureRunId"], value["databaseSha256"],
        value["proofSha256"], identities[0]["pid"], identities[0]["commit"], identities[0]["dllSha256"],
        identities[1]["pid"], identities[1]["commit"], identities[1]["dllSha256"], value["diagnosticSetting"],
        value["templateSourceSha256"])


def append_fixture(source, proof_path, target, receipt_root, policy, *, guard=lambda: None,
                   hook=lambda *_: None, forbidden_ids=frozenset()):
    """Only offline callers inject paths/hooks. CLI below fixes every authority input."""
    source, proof_path, target, receipt_root = map(Path, (source, proof_path, target, receipt_root))
    source_identity, target_identity = plain(source), plain(target)
    plain(proof_path)
    selected = selected_family(source, proof_path, target, receipt_root / "before.sqlite")
    # Check before hashing/opening databases, before receipt creation or backup writes.
    reject_forbidden_aliases(selected, forbidden_ids)
    require(source_identity != target_identity, "DISTINCT_DATABASES_REQUIRED")
    require(file_sha(source) == policy.database_sha and file_sha(proof_path) == policy.proof_sha, "FIXTURE_SHA_CHANGED")
    for suffix in ("-wal", "-shm", "-journal"):
        sidecar = Path(str(source) + suffix)
        require(not os.path.lexists(sidecar), "SOURCE_SIDECAR_REJECTED")
    proof = json.loads(proof_path.read_text(encoding="utf-8"))
    ids = [f"qa-history-{policy.run_id}-{index:02}" for index in range(16)]
    require(uuid.UUID(proof["runId"]).hex == policy.run_id and proof["matchIds"] == ids
            and proof["matches"] == 16 and proof["minimumReturnedSamples"] >= 5
            and proof["defaultQueryTotal"] > 0 and proof["serviceDatabaseWrites"] == 0
            and proof["assemblySha256"] == policy.assembly_sha and proof["database"] == str(source), "FIXTURE_PROOF_REJECTED")
    guard()
    # Atomic mkdir is also a durable one-attempt fence: never remove/reuse this root.
    receipt_root.mkdir(mode=0o700)
    require(receipt_root.resolve() == receipt_root and not receipt_root.is_symlink(), "RECEIPT_ROOT_REJECTED")
    committed = False
    transaction_started = False
    con = None
    outcome = {"schema": 1, "runId": policy.run_id, "committed": False, "status": "PRECOMMIT_REJECTED"}
    try:
        with contextlib.closing(connect(source, readonly=True, immutable=True)) as src:
            healthy(src)
            expected_schema = schema(src)
            tables = {row[1] for row in expected_schema if row[0] == "table"}
            require(tables == set(policy.counts) | set(policy.nonmatch), "UNKNOWN_TABLE_OR_CLOSURE")
            require(all(row[0] in {"table", "index", "trigger"} for row in expected_schema), "UNKNOWN_SCHEMA_OBJECT")
            for table in tables:
                require(("match_id" in columns(src, table)) == (table in policy.counts), "MATCH_SCOPE_CHANGED")
            source_data = snapshot(src, tables)
            for table, count in policy.counts.items():
                require(source_data[table]["count"] == count, "FIXTURE_COUNT_CHANGED")
                names = columns(src, table)
                require(all(row[names.index("match_id")] in ids for row in checked_rows(src, table)), "FOREIGN_MATCH_ID")
            require({row[columns(src, "matches").index("match_id")] for row in checked_rows(src, "matches")} == set(ids), "MATCH_IDS_CHANGED")
            for table in policy.nonmatch:
                require(source_data[table]["count"] == policy.metadata_counts.get(table, 0), "UNKNOWN_NONMATCH_SOURCE_DATA")
            require(file_sha(source) == policy.database_sha and plain(source) == source_identity, "SOURCE_CHANGED")
            con = connect(target)
            healthy(con)
            require(schema(con) == expected_schema, "SCHEMA_CHANGED")
            before = snapshot(con, tables)
            require(all(before[name]["count"] == 0 for name in policy.counts), "TARGET_MATCH_TABLES_NOT_EMPTY")
            require(version_semantics(con, policy.version_table) == version_semantics(src, policy.version_table), "VERSION_METADATA_CHANGED")
            for name in policy.nonmatch:
                require(before[name]["count"] == policy.metadata_counts.get(name, 0), "TARGET_NONMATCH_COUNT_CHANGED")
            versions = {name: con.execute("PRAGMA " + name).fetchone()[0] for name in ("user_version", "application_id")}
            require(all(src.execute("PRAGMA " + name).fetchone()[0] == value for name, value in versions.items()), "PRAGMA_VERSION_CHANGED")
            backup = receipt_root / "before.sqlite"
            guard()
            reject_forbidden_aliases(selected, forbidden_ids)
            backup.touch(mode=0o600, exist_ok=False)
            reject_forbidden_aliases(selected, forbidden_ids)
            until = time.monotonic() + 10
            def progress(*_):
                require(time.monotonic() < until, "BACKUP_DEADLINE")
            with contextlib.closing(sqlite3.connect(backup)) as saved:
                con.backup(saved, pages=64, progress=progress, sleep=0.025)
                healthy(saved)
                require(schema(saved) == expected_schema and snapshot(saved, tables) == before, "BACKUP_SNAPSHOT_CHANGED")
            durable(receipt_root / "backup-proof.json", {"schema": 1, "sqliteBackup": True, "sha256": file_sha(backup),
                    "tables": before, "sourceSha256": policy.database_sha, "targetIdentity": target_identity})
            guard()
            hook("before_begin", con)
            reject_forbidden_aliases(selected, forbidden_ids)
            con.execute("BEGIN IMMEDIATE")
            transaction_started = True
            require(plain(target) == target_identity, "TARGET_IDENTITY_CHANGED")
            reject_forbidden_aliases(selected, forbidden_ids)
            require(schema(con) == expected_schema and snapshot(con, tables) == before, "TARGET_CHANGED_AFTER_BACKUP")
            require(all(con.execute("PRAGMA " + name).fetchone()[0] == value for name, value in versions.items()), "TARGET_VERSION_CHANGED")
            con.execute("PRAGMA defer_foreign_keys=ON")
            for table in sorted(policy.counts):
                names = columns(src, table)
                sql = "INSERT INTO " + identifier(table) + "(" + ",".join(map(identifier, names)) + ") VALUES(" + ",".join("?" for _ in names) + ")"
                con.executemany(sql, checked_rows(src, table))
                hook("after_table:" + table, con)
            healthy(con)
            after = snapshot(con, tables)
            require(all(after[name] == source_data[name] for name in policy.counts), "IMPORTED_HASH_MISMATCH")
            require(all(after[name] == before[name] for name in policy.nonmatch), "METADATA_CHANGED")
            require(schema(con) == expected_schema, "SCHEMA_CHANGED_IN_TRANSACTION")
            guard()
            durable(receipt_root / "commit-intent.json", {"schema": 1, "status": "COMMIT_OUTCOME_UNKNOWN_UNTIL_READBACK",
                    "runId": policy.run_id, "matchIds": ids, "tables": after, "targetIdentity": target_identity,
                    "sourceSha256": policy.database_sha, "rollbackScope": {"matchIdsOnly": ids, "automaticRollback": False}})
            hook("before_commit", con)
            # From this point an exception is UNKNOWN/committed-pending, never a rollback claim.
            outcome["status"] = "COMMIT_OUTCOME_UNKNOWN_RECONCILIATION_REQUIRED"
            committed = None
            con.execute("COMMIT")
            transaction_started = False
            committed = True
            outcome.update(committed=True, status="COMMITTED_READBACK_PENDING")
            durable(receipt_root / "commit-marker.json", {**outcome, "matchIds": ids})
            hook("after_commit", con)
            with contextlib.closing(connect(target, readonly=True)) as reread:
                healthy(reread)
                require(plain(target) == target_identity and schema(reread) == expected_schema
                        and snapshot(reread, tables) == after, "POSTCOMMIT_READBACK_FAILED")
            guard()
            outcome.update(status="COMMITTED_VERIFIED", tables=after, metadataPreserved=True,
                           matches=16, formalWrites=0, sourceSha256=policy.database_sha,
                           backupSha256=file_sha(backup), matchIds=ids, onlineCapacityAccepted=False)
            durable(receipt_root / "result.json", outcome)
            return outcome
    except Exception as error:
        if transaction_started and con is not None and con.in_transaction:
            con.execute("ROLLBACK")
            committed = False
            outcome["status"] = "ROLLED_BACK"  # SQLite confirms the transaction was still open.
        outcome["committed"] = committed
        outcome["errorClass"] = type(error).__name__
        with contextlib.suppress(Exception):
            durable(receipt_root / "failure.json", outcome)
        raise
    finally:
        if con is not None:
            con.close()


def live_storage_binding(source_root=SOURCE_ROOT, receipts=RECEIPTS):
    test = Path("/opt/legion12-testrun/publish/runtime").resolve(strict=True)
    formal = Path("/opt/legion12-test/publish/runtime").resolve(strict=True)
    require(test.is_dir() and formal.is_dir() and test == TARGET.parent and formal != test,
            "ACTUAL_RUNTIME_BINDING_REJECTED")
    forbidden = set()
    for entry in formal.iterdir():
        if entry.name.endswith((".db", ".db-wal", ".db-shm", ".db-journal")):
            # Stat only: never open a formal database as SQLite or read its contents.
            info = entry.stat()
            require(stat.S_ISREG(info.st_mode), "FORMAL_DATABASE_FAMILY_REJECTED")
            forbidden.add((info.st_dev, info.st_ino))
    formal_matches = (formal / "matches.db").stat()
    require(stat.S_ISREG(formal_matches.st_mode), "FORMAL_MATCH_DATABASE_REQUIRED")
    match_identity = (formal_matches.st_dev, formal_matches.st_ino)
    require(match_identity in forbidden, "FORMAL_MATCH_IDENTITY_CHANGED")
    reject_forbidden_aliases(selected_family(source_root / "fixture-matches.db", source_root / "fixture-proof.json",
                             TARGET, receipts / "before.sqlite"), forbidden)
    return {"testRuntime": str(test), "formalRuntime": str(formal),
            "formalMatchesIdentity": match_identity}, forbidden


def live_guard(binding=None):
    require(sys.platform == "linux" and os.geteuid() == 0 and socket.gethostname() == "ser550468033481", "FIXED_ROOT_HOST_REQUIRED")
    result = {}
    identities = ((542637, TEST_COMMIT, TEST_DLL), (534086, FORMAL_COMMIT, FORMAL_DLL)) if binding is None else (
        (binding.test_pid, binding.test_commit, binding.test_dll),
        (binding.formal_pid, binding.formal_commit, binding.formal_dll))
    for (name, unit, root), (pid, commit, sha) in zip((
        ("test", "legion12-testrun.service", Path("/opt/legion12-testrun")),
        ("formal", "legion12-test.service", Path("/opt/legion12-test"))), identities):
        info = dict(line.split("=", 1) for line in subprocess.check_output(
            ["systemctl", "show", unit, "-p", "MainPID", "-p", "ActiveState", "-p", "NRestarts"], text=True, timeout=3).splitlines())
        require(info["ActiveState"] == "active" and int(info["MainPID"]) == pid, "SERVICE_PID_CHANGED")
        require((root / ".deployment-commit").read_text().strip() == commit and file_sha(root / "publish/GrandUMIServer.dll") == sha, "DEPLOYMENT_CHANGED")
        environ = {}
        for entry in Path(f"/proc/{pid}/environ").read_bytes().split(b"\0"):
            key, _, value = entry.partition(b"=")
            if key in {b"L12_PUBLIC_BASE_URL", b"L12_TESTRUN_MATCH_STORAGE", b"L12_PRIVATE_DECK_OBJECT_PERSISTENCE", b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS"}:
                require(key not in environ, "DUPLICATE_ENVIRONMENT")
                environ[key] = value
        expected_diagnostic = binding.diagnostic_setting.encode() if binding is not None and name == "test" else b""
        require(environ.get(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS", b"") == expected_diagnostic, "DIAGNOSTICS_CHANGED")
        if name == "test":
            require(environ.get(b"L12_PUBLIC_BASE_URL", b"").rstrip(b"/") == b"https://legion-12.com/testrun" and environ.get(b"L12_TESTRUN_MATCH_STORAGE") == b"ephemeral" and environ.get(b"L12_PRIVATE_DECK_OBJECT_PERSISTENCE") == b"true", "TEST_PROFILE_CHANGED")
        else:
            require(environ.get(b"L12_PUBLIC_BASE_URL", b"") in {b"", b"https://legion-12.com", b"https://legion-12.com/"} and not environ.get(b"L12_TESTRUN_MATCH_STORAGE"), "FORMAL_PROFILE_CHANGED")
        result[name] = (pid, Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[19], info["NRestarts"])
    source_root = SOURCE_ROOT if binding is None else binding.source_root
    receipts = RECEIPTS if binding is None else binding.receipts
    result["storage"], _ = live_storage_binding(source_root, receipts)
    return result


def main():
    global ACTIVE_RECEIPTS
    if sys.argv[1:] == ["--apply"]:
        binding = None
    else:
        require(len(sys.argv) == 4 and sys.argv[1:3] == ["--terminal-apply", "--binding-sha"],
                "ONLY_FIXED_APPLY_MODE_ALLOWED")
        raw = sys.stdin.buffer.read(8193)
        binding = parse_terminal_binding(raw, sys.argv[3])
        require(file_sha(Path(__file__)) == binding.template_source_sha, "TERMINAL_TEMPLATE_SOURCE_CHANGED")
        ACTIVE_RECEIPTS = binding.receipts
    def expired(*_):
        raise TimeoutError("IMPORT_TOTAL_DEADLINE")
    signal.signal(signal.SIGALRM, expired)
    signal.alarm(35)
    try:
        apply_fixed(binding)
    finally:
        signal.alarm(0)


def apply_fixed(binding=None):
    source_root = SOURCE_ROOT if binding is None else binding.source_root
    receipts = RECEIPTS if binding is None else binding.receipts
    run_id = RUN if binding is None else binding.fixture_run_id
    database_sha = DB_SHA if binding is None else binding.database_sha
    proof_sha = PROOF_SHA if binding is None else binding.proof_sha
    assembly_sha = TEST_DLL if binding is None else binding.test_dll
    initial = live_guard(binding)
    _, forbidden_ids = live_storage_binding(source_root, receipts)
    for path in (source_root, source_root.parent):
        require(path.resolve(strict=True) == path and not path.is_symlink(), "FIXTURE_ROOT_REJECTED")
    require(source_root.stat().st_uid == 0 and stat.S_IMODE(source_root.stat().st_mode) == 0o700, "PRIVATE_FIXTURE_ROOT_REQUIRED")
    require(not os.path.lexists(receipts), "REENTRY_RECEIPT_EXISTS")
    import fcntl
    lock = Path("/run/lock/legion12-normal-pressure.lock")
    with lock.open("a+") as stream:
        require(not lock.is_symlink() and stat.S_ISREG(os.fstat(stream.fileno()).st_mode)
                and os.fstat(stream.fileno()).st_uid == 0, "LOCK_PATH_REJECTED")
        fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        def guard():
            require(live_guard(binding) == initial, "SERVICE_IDENTITY_CHANGED")
            _, current_forbidden = live_storage_binding(source_root, receipts)
            forbidden_ids.update(current_forbidden)
        result = append_fixture(source_root / "fixture-matches.db", source_root / "fixture-proof.json", TARGET, receipts,
                 Policy(run_id, database_sha, proof_sha, COUNTS, frozenset(NONMATCH), META_COUNTS,
                        assembly_sha=assembly_sha),
                 guard=guard, forbidden_ids=forbidden_ids)
        print(json.dumps(result, separators=(",", ":")))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"schema": 1, "status": "FAILED_OR_RECONCILIATION_REQUIRED",
                          "errorClass": type(error).__name__, "receiptRoot": str(ACTIVE_RECEIPTS),
                          "retryAutomatically": False}), file=sys.stderr)
        raise SystemExit(2)
