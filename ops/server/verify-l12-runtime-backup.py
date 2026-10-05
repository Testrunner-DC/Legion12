"""Read-only stopped-runtime proof. No checkpoints, repairs or private output."""
import hashlib
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
LIMIT_SECONDS = 110


def digest(stream, start):
    value = hashlib.sha256()
    length = 0
    while True:
        if time.monotonic() - start > LIMIT_SECONDS:
            raise ValueError("proof time budget exceeded")
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
            raise ValueError("persistent database path is not plain")
        if not path.exists():
            if name in DATABASES:
                raise ValueError("required database missing")
            continue
        if not stat.S_ISREG(path.stat().st_mode):
            raise ValueError("persistent database path is not regular")
        with path.open("rb") as source:
            facts[name] = digest(source, start)
    return facts


def verify(runtime, backup, expected_sha, *, test_only=False):
    start = time.monotonic()
    runtime, backup = Path(runtime), Path(backup)
    if not test_only:
        if runtime != Path("/opt/legion12-runtime") or backup.parent not in (
            Path("/www/legion12/runtime-backups"), Path("/opt/legion12-deployment/runtime-backups")
        ):
            raise ValueError("fixed production paths required")
        if not re.fullmatch(r"runtime-before-[0-9a-f]{12}-[0-9]{8}T[0-9]{6}Z\.tar\.gz", backup.name):
            raise ValueError("owned backup name required")
    if runtime.is_symlink() or not runtime.is_dir() or backup.parent.is_symlink() or backup.is_symlink() or not backup.is_file():
        raise ValueError("plain stopped runtime and owned backup required")
    if not re.fullmatch(r"[0-9a-f]{64}", expected_sha):
        raise ValueError("backup checksum required")
    if any(path.name not in DATABASES for path in runtime.glob('*.db')):
        raise ValueError("unknown required database needs explicit verification")
    for name in DATABASES:
        journal = runtime / (name + '-journal')
        if journal.is_symlink() or journal.exists() and journal.stat().st_size:
            raise ValueError("hot rollback journal requires recovery, not a release")
    with backup.open("rb") as source:
        _, actual_sha = digest(source, start)
    if actual_sha != expected_sha:
        raise ValueError("backup checksum changed")
    before = persistent_facts(runtime, start)
    archived = {}
    with tarfile.open(backup, mode="r|gz") as snapshot:
        for member in snapshot:
            if time.monotonic() - start > LIMIT_SECONDS:
                raise ValueError("proof time budget exceeded")
            name = PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts:
                raise ValueError("unsafe snapshot entry")
            normalized = str(name)
            if normalized not in PERSISTENT_NAMES:
                continue
            if normalized in archived or not member.isfile():
                raise ValueError("duplicate or unsafe persistent snapshot entry")
            stream = snapshot.extractfile(member)
            if stream is None:
                raise ValueError("persistent snapshot entry unreadable")
            with stream:
                archived[normalized] = digest(stream, start)
    if before != archived:
        raise ValueError("snapshot differs from latest stopped database/WAL facts")
    for name, required_table in DATABASES.items():
        # mode=ro preserves WAL visibility. immutable=1 would ignore an outstanding
        # WAL and is deliberately not used. SQLite may update its ephemeral SHM
        # reader bookkeeping; persistent DB/WAL facts are checked before/after.
        uri = (runtime / name).resolve().as_uri() + "?mode=ro"
        connection = sqlite3.connect(uri, uri=True, timeout=1)
        try:
            connection.execute("PRAGMA query_only=ON")
            connection.set_progress_handler(lambda: int(time.monotonic() - start > LIMIT_SECONDS), 1000)
            connection.execute("BEGIN")
            if connection.execute("PRAGMA quick_check(1)").fetchall() != [("ok",)]:
                raise ValueError("SQLite integrity rejected")
            if connection.execute("SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name=?", (required_table,)).fetchone() != (1,):
                raise ValueError("required database metadata missing")
            columns = {row[1] for row in connection.execute("PRAGMA table_info('" + required_table + "')")}
            if not SCHEMA_ANCHORS[name].issubset(columns):
                raise ValueError("required database metadata columns missing")
        finally:
            connection.close()
    after = persistent_facts(runtime, start)
    if before != after:
        raise ValueError("persistent facts changed during read-only proof")
    with backup.open('rb') as source:
        if digest(source, start)[1] != expected_sha:
            raise ValueError("backup changed during proof")
    if time.monotonic() - start > LIMIT_SECONDS:
        raise ValueError("proof time budget exceeded before completion")
    return {
        "schema": 1, "verified": True, "databases": len(DATABASES),
        "persistentFiles": len(before), "persistentBytes": sum(value[0] for value in before.values()),
        "backupSha256": expected_sha, "databaseWalFingerprintSha256": hashlib.sha256(
            json.dumps(before, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        "sqliteQuickCheck": "ok", "schemaAnchorsVerified": True,
        "latestDatabaseAndWalEqualBackup": True,
        "persistentFactMutations": 0, "checkpointOrRepairPerformed": False,
        "elapsedMilliseconds": round((time.monotonic() - start) * 1000, 3),
    }


if __name__ == "__main__":
    try:
        if len(sys.argv) != 4:
            raise ValueError("fixed runtime, owned backup and SHA required")
        print(json.dumps(verify(*sys.argv[1:]), separators=(",", ":")))
    except Exception:
        # Never print SQLite error text, paths supplied by users or private bytes.
        print("Stopped runtime/backup proof rejected; no repair or checkpoint performed", file=sys.stderr)
        sys.exit(1)
