"""Read-only stopped-runtime proof. No checkpoints, repairs or private output."""
import hashlib
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
import json
import os
from pathlib import Path, PurePosixPath
import re
import sqlite3
import stat
import sys
import tarfile
import time

DATABASES = {"platform.db": "platform_state", "matches.db": "matches"}
SCHEMA_ANCHORS = {
    "platform.db": {"singleton_id", "schema_version", "storage_revision", "business_version", "snapshot_json", "snapshot_sha256", "updated_utc"},
    "matches.db": {"match_id", "room_code", "mode_id", "started_utc", "ended_utc", "storage_version", "hash_version"},
}
PERSISTENT_NAMES = tuple(name + suffix for name in DATABASES for suffix in ("", "-wal"))
LIMIT_SECONDS = 300
EMPTY_SHA256 = hashlib.sha256(b"").hexdigest()


class ProofError(ValueError):
    """Only fixed reason/stage identifiers are ever returned by the CLI."""
    def __init__(self, code):
        super().__init__(code)
        self.code = code
        self.stage = "path_guard"
        self.elapsed_milliseconds = 0
        self.stage_timings = []


def canonical_facts(facts):
    # A read-only WAL connection may create an empty bookkeeping file. Remove
    # only known, demonstrably empty WALs from equality; never remove files,
    # checkpoint, ignore nonempty WAL bytes, or relax either main DB hash.
    return {name: value for name, value in facts.items()
            if not (name in PERSISTENT_NAMES and name.endswith("-wal")
                    and value == (0, EMPTY_SHA256))}


def empty_wal_transitions(before, after):
    return sum(name.endswith("-wal") and (name in before) != (name in after)
               for name in PERSISTENT_NAMES)


def timed_call(name, callback):
    started = time.monotonic()
    try:
        value = callback()
        error = None
    except Exception as exc:  # preserve the original fixed proof error
        value = None
        error = exc
    return value, error, {
        "stage": name,
        "elapsedMilliseconds": round((time.monotonic() - started) * 1000, 3),
    }


def snapshot_facts(backup, start):
    archived = {}
    with tarfile.open(backup, mode="r|gz") as snapshot:
        for member in snapshot:
            if time.monotonic() - start > LIMIT_SECONDS:
                raise ProofError("TIME_BUDGET_EXCEEDED")
            name = PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts:
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            normalized = str(name)
            if len(name.parts) == 1 and (normalized.endswith('.db') or normalized.endswith('.db-wal')) and normalized not in PERSISTENT_NAMES:
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            if normalized not in PERSISTENT_NAMES:
                continue
            if normalized in archived or not member.isfile():
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            stream = snapshot.extractfile(member)
            if stream is None:
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            with stream:
                archived[normalized] = digest(stream, start)
    return archived


def sqlite_contract(runtime, name, required_table, start):
    # Read the real WAL view. Never use immutable, perform checkpoints,
    # delete bookkeeping files or repair a damaged database.
    uri = (runtime / name).resolve().as_uri() + "?mode=ro"
    connection = sqlite3.connect(uri, uri=True, timeout=1)
    try:
        connection.execute("PRAGMA query_only=ON")
        connection.set_progress_handler(lambda: int(time.monotonic() - start > LIMIT_SECONDS), 1000)
        connection.execute("BEGIN")
        if connection.execute("PRAGMA quick_check(1)").fetchall() != [("ok",)]:
            raise ProofError("SQLITE_INTEGRITY_REJECTED")
        if connection.execute("SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name=?", (required_table,)).fetchone() != (1,):
            raise ProofError("SCHEMA_CONTRACT_REJECTED")
        columns = {row[1] for row in connection.execute("PRAGMA table_info('" + required_table + "')")}
        if not SCHEMA_ANCHORS[name].issubset(columns):
            raise ProofError("SCHEMA_CONTRACT_REJECTED")
    finally:
        connection.close()


def backup_digest(backup, start):
    with backup.open("rb") as source:
        return digest(source, start)


def digest(stream, start):
    value = hashlib.sha256()
    length = 0
    while True:
        if time.monotonic() - start > LIMIT_SECONDS:
            raise ProofError("TIME_BUDGET_EXCEEDED")
        chunk = stream.read(1024 * 1024)
        if not chunk:
            return (length, value.hexdigest())
        value.update(chunk)
        length += len(chunk)


def persistent_facts(runtime, start):
    facts = {}
    for name in PERSISTENT_NAMES:
        path = runtime / name
        if path.is_symlink():
            raise ProofError("UNSAFE_PATH_OR_FILE_SET")
        if not path.exists():
            if name in DATABASES:
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            continue
        if not stat.S_ISREG(path.stat().st_mode):
            raise ProofError("UNSAFE_PATH_OR_FILE_SET")
        with path.open("rb") as source:
            facts[name] = digest(source, start)
    return facts


def verify(runtime, backup, expected_sha, *, test_only=False):
    start = time.monotonic()
    current_stage = "path_guard"
    timings = []

    @contextmanager
    def phase(name):
        nonlocal current_stage
        current_stage = name
        phase_start = time.monotonic()
        try:
            if phase_start - start > LIMIT_SECONDS:
                raise ProofError("TIME_BUDGET_EXCEEDED")
            yield
        finally:
            timings.append({"stage": name, "elapsedMilliseconds": round((time.monotonic() - phase_start) * 1000, 3)})

    try:
        with phase("path_guard"):
            runtime, backup = Path(runtime), Path(backup)
            if not test_only:
                if runtime != Path("/opt/legion12-runtime") or backup.parent not in (
                    Path("/www/legion12/runtime-backups"), Path("/opt/legion12-deployment/runtime-backups")
                ):
                    raise ProofError("UNSAFE_PATH_OR_FILE_SET")
                if not re.fullmatch(r"runtime-before-[0-9a-f]{12}-[0-9]{8}T[0-9]{6}Z\.tar\.gz", backup.name):
                    raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            if runtime.is_symlink() or not runtime.is_dir() or backup.parent.is_symlink() or backup.is_symlink() or not backup.is_file():
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            if not re.fullmatch(r"[0-9a-f]{64}", expected_sha):
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            if any(path.name not in DATABASES for path in runtime.glob('*.db')):
                raise ProofError("UNSAFE_PATH_OR_FILE_SET")
            for name in DATABASES:
                journal = runtime / (name + '-journal')
                if journal.is_symlink() or journal.exists() and journal.stat().st_size:
                    raise ProofError("UNSAFE_PATH_OR_FILE_SET")
        # These checks are independent read-only observations of the stopped
        # runtime. Run them concurrently so a large valid snapshot does not
        # spend the entire bounded proof budget serially rereading the same
        # bytes. The individual checks and their exact contracts are unchanged.
        with ThreadPoolExecutor(max_workers=2) as pool:
            before_future = pool.submit(timed_call, "database_hash_before", lambda: persistent_facts(runtime, start))
            backup_future = pool.submit(timed_call, "backup_hash_before", lambda: backup_digest(backup, start))
            before, before_error, before_timing = before_future.result()
            backup_result, backup_error, backup_timing = backup_future.result()
        timings.extend([backup_timing, before_timing])
        if backup_error is not None:
            current_stage = "backup_hash_before"
            raise backup_error
        if before_error is not None:
            current_stage = "database_hash_before"
            raise before_error
        if backup_result[1] != expected_sha:
            current_stage = "backup_hash_before"
            raise ProofError("BACKUP_CHECKSUM_CHANGED")
        with ThreadPoolExecutor(max_workers=3) as pool:
            snapshot_future = pool.submit(timed_call, "snapshot_stream", lambda: snapshot_facts(backup, start))
            platform_future = pool.submit(timed_call, "sqlite_platform", lambda: sqlite_contract(runtime, "platform.db", "platform_state", start))
            matches_future = pool.submit(timed_call, "sqlite_matches", lambda: sqlite_contract(runtime, "matches.db", "matches", start))
            archived, snapshot_error, snapshot_timing = snapshot_future.result()
            _, platform_error, platform_timing = platform_future.result()
            _, matches_error, matches_timing = matches_future.result()
        timings.extend([snapshot_timing, platform_timing, matches_timing])
        if snapshot_error is not None:
            current_stage = "snapshot_stream"
            raise snapshot_error
        if platform_error is not None:
            current_stage = "sqlite_platform"
            raise platform_error
        if matches_error is not None:
            current_stage = "sqlite_matches"
            raise matches_error
        if canonical_facts(before) != canonical_facts(archived):
            current_stage = "snapshot_stream"
            raise ProofError("SNAPSHOT_NOT_LATEST")
        with phase("database_hash_after"):
            after = persistent_facts(runtime, start)
            if canonical_facts(before) != canonical_facts(after):
                raise ProofError("PERSISTENT_FACTS_CHANGED")
        with phase("backup_hash_after"):
            with backup.open('rb') as source:
                if digest(source, start)[1] != expected_sha:
                    raise ProofError("BACKUP_CHECKSUM_CHANGED")
        if time.monotonic() - start > LIMIT_SECONDS:
            raise ProofError("TIME_BUDGET_EXCEEDED")
        canonical = canonical_facts(before)
        return {
            "schema": 1, "verified": True, "databases": len(DATABASES),
            "persistentFiles": len(before), "persistentBytes": sum(value[0] for value in before.values()),
            "backupSha256": expected_sha, "databaseWalFingerprintSha256": hashlib.sha256(
                json.dumps(canonical, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
            "sqliteQuickCheck": "ok", "schemaAnchorsVerified": True,
            "latestDatabaseAndWalEqualBackup": True,
            "persistentFactMutations": 0, "checkpointOrRepairPerformed": False,
            "emptyWalPresenceTransitions": empty_wal_transitions(before, after),
            "stageTimings": timings,
            "elapsedMilliseconds": round((time.monotonic() - start) * 1000, 3),
        }
    except Exception as error:
        if isinstance(error, ProofError):
            failure = error
        elif time.monotonic() - start > LIMIT_SECONDS:
            failure = ProofError("TIME_BUDGET_EXCEEDED")
        elif isinstance(error, sqlite3.DatabaseError):
            code = getattr(error, "sqlite_errorcode", -1) & 255
            failure = ProofError("SQLITE_READ_UNAVAILABLE" if code in (5, 6) else "SQLITE_INTEGRITY_REJECTED")
        elif isinstance(error, (OSError, tarfile.TarError)):
            failure = ProofError("UNSAFE_PATH_OR_FILE_SET")
        else:
            failure = ProofError("PROOF_REJECTED")
        failure.stage = current_stage
        failure.elapsed_milliseconds = round((time.monotonic() - start) * 1000, 3)
        failure.stage_timings = timings
        raise failure from None


if __name__ == "__main__":
    try:
        if len(sys.argv) != 4:
            raise ProofError("UNSAFE_PATH_OR_FILE_SET")
        print(json.dumps(verify(*sys.argv[1:]), separators=(",", ":")))
    except ProofError as error:
        # Only bounded, fixed identifiers and durations; never raw errors/paths/data.
        print(json.dumps({"schema": 1, "verified": False, "reasonCode": error.code,
                          "failedStage": error.stage, "elapsedMilliseconds": error.elapsed_milliseconds,
                          "stageTimings": error.stage_timings}, separators=(",", ":")))
        sys.exit(1)
    except Exception:
        print('{"schema":1,"verified":false,"reasonCode":"PROOF_REJECTED","failedStage":"path_guard","elapsedMilliseconds":0,"stageTimings":[]}')
        sys.exit(1)
