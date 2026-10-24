"""Read-only facts for a bounded synthetic restore, not a production backup verifier.

The caller must own the private fixture root. No body, account ID or credential
is returned. The existing stopped DB/WAL proof remains separately mandatory.
"""
import hashlib
from contextlib import closing
from datetime import datetime
import json
import os
from pathlib import Path
import re
import sqlite3
import stat
import time

MAX_FILES, MAX_BYTES, MAX_ROWS = 4096, 128 * 1024**2, 20000
REQUIRED = ("Accounts", "Friends", "BugReports", "Articles", "SiteMedia")


def require(value, code):
    if not value: raise ValueError(code)


def strict_json(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "DUPLICATE_SNAPSHOT_KEY")
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=unique, parse_constant=lambda _: require(False, "NONFINITE_SNAPSHOT_VALUE"))


def hash_value(value):
    raw = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
        allow_nan=False, default=lambda blob: {"blobHex": bytes(blob).hex()}).encode()
    return hashlib.sha256(raw).hexdigest()


def plain(path, directory=False):
    value = path.lstat()
    require(not path.is_symlink() and not (getattr(value, "st_file_attributes", 0) & 0x400)
        and (stat.S_ISDIR(value.st_mode) if directory else stat.S_ISREG(value.st_mode) and value.st_nlink == 1),
        "FIXTURE_LINK_OR_SPECIAL_REJECTED")
    return value


def tree_manifest(root):
    root = Path(root)
    require(root.is_absolute() and root.resolve() == root, "PHYSICAL_FIXTURE_ROOT_REQUIRED")
    plain(root, True)
    result, total, entries = {}, 0, 0
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in directories:
            plain(Path(directory) / name, True)
            entries += 1
            require(entries <= MAX_FILES, "FIXTURE_FILE_BUDGET")
        for name in files:
            path = Path(directory) / name
            before = plain(path)
            entries += 1
            total += before.st_size
            require(entries <= MAX_FILES and total <= MAX_BYTES, "FIXTURE_FILE_BUDGET")
            with path.open("rb") as stream:
                opened = os.fstat(stream.fileno())
                require((before.st_dev, before.st_ino) == (opened.st_dev, opened.st_ino), "FIXTURE_FILE_CHANGED")
                digest = hashlib.sha256()
                size = 0
                while chunk := stream.read(1024 * 1024):
                    size += len(chunk)
                    require(size <= before.st_size, "FIXTURE_FILE_CHANGED")
                    digest.update(chunk)
            after = path.lstat()
            require(size == before.st_size and (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
                == (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns), "FIXTURE_FILE_CHANGED")
            result[path.relative_to(root).as_posix()] = {"bytes": size, "sha256": digest.hexdigest()}
    return result


def database_facts(root, deadline):
    root = Path(root)
    require(root.is_absolute() and root.resolve() == root, "PHYSICAL_FIXTURE_ROOT_REQUIRED")
    plain(root, True)
    result = {}
    for name in ("platform.db", "matches.db"):
        file = root / name
        plain(file)
        for suffix in ("-wal", "-shm"):
            if os.path.lexists(str(file) + suffix): plain(Path(str(file) + suffix))
        with closing(sqlite3.connect(file.as_uri() + "?mode=ro", uri=True, timeout=1)) as connection:
            connection.execute("PRAGMA query_only=ON")
            connection.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
            require(connection.execute("PRAGMA integrity_check").fetchall() == [("ok",)], "RESTORED_SQLITE_INTEGRITY")
            tables = connection.execute("SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name").fetchall()
            facts = {}
            for (table,) in tables:
                require(re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,95}", table), "FIXTURE_TABLE_NAME")
                rows = []
                for row in connection.execute('SELECT * FROM "' + table + '"'):
                    require(len(rows) < MAX_ROWS and time.monotonic() < deadline, "FIXTURE_ROW_OR_TIME_BUDGET")
                    rows.append(hash_value(row))
                facts[table] = {"rows": len(rows), "sha256": hash_value(sorted(rows))}
            require("platform_state" in facts if name == "platform.db" else "matches" in facts, "FIXTURE_DATABASE_ANCHOR_MISSING")
            entry = {"integrity": "ok", "tables": facts}
            if name == "matches.db" and "match_recorder_schema" in facts:
                columns = [row[1] for row in connection.execute("PRAGMA table_info('match_recorder_schema')")]
                require(columns == ["component", "version", "migrated_utc"], "SCHEMA_MARKER_COLUMNS_CHANGED")
                entry["schemaMarkers"] = [list(row) for row in connection.execute(
                    "SELECT component,version,migrated_utc FROM match_recorder_schema ORDER BY component")]
            if name == "platform.db":
                rows = connection.execute("SELECT snapshot_json FROM platform_state WHERE singleton_id=1").fetchall()
                require(len(rows) == 1 and isinstance(rows[0][0], str) and len(rows[0][0].encode()) <= 16 * 1024**2, "FIXTURE_SNAPSHOT_BOUND")
                snapshot = strict_json(rows[0][0])
                require(isinstance(snapshot, dict), "FIXTURE_SNAPSHOT_OBJECT")
                for key in REQUIRED:
                    require(isinstance(snapshot.get(key), list), "FIXTURE_REQUIRED_COLLECTION_MISSING")
                entry["snapshot"] = {key: {"count": len(value) if isinstance(value, (list, dict)) else None,
                    "sha256": hash_value(value)} for key, value in snapshot.items()}
            result[name] = entry
    return result


def nonempty_coverage(facts, files):
    snapshot = facts["platform.db"]["snapshot"]
    required = {key: snapshot[key]["count"] for key in REQUIRED}
    require(all(type(count) is int and count > 0 for count in required.values()), "NONEMPTY_BUSINESS_COVERAGE_REQUIRED")
    media = {name: value for name, value in files.items() if name.startswith("site-media/")}
    require(media and all(value["bytes"] > 0 for value in media.values()), "NONEMPTY_MEDIA_FILES_REQUIRED")
    return {"collections": required, "uploadedFiles": len(media), "uploadedBytes": sum(value["bytes"] for value in media.values())}


def require_owned_loopback_listener(text, port, pid):
    require(type(port) is int and 0 < port < 65536 and type(pid) is int and pid > 0, "LOOPBACK_LISTENER_REQUIRED")
    rows = [line.split() for line in text.splitlines() if line.strip()]
    # ss reports a wildcard *peer* for every ordinary listener. Only its local
    # endpoint defines exposure; the original exact owner/PID guard remains.
    require(len(rows) == 1 and len(rows[0]) >= 6 and rows[0][0] == "LISTEN"
        and rows[0][3] == "127.0.0.1:" + str(port)
        and {int(value) for value in re.findall(r"\bpid=([0-9]+),", " ".join(rows[0][5:]))} == {pid},
        "LOOPBACK_LISTENER_REQUIRED")


def require_started_business_preserved(before, after):
    """Only the pinned app's unconditional analytics migration time may refresh.

    Both original full table hashes remain available. No row, version, component,
    unknown column, platform field or other table is erased from comparison.
    """
    if before == after: return {"schemaTimestampRefreshes": 0}
    require(set(before) == set(after) == {"platform.db", "matches.db"}
        and before["platform.db"] == after["platform.db"], "PLATFORM_BUSINESS_CHANGED")
    left, right = before["matches.db"], after["matches.db"]
    require(set(left) == set(right) == {"integrity", "tables", "schemaMarkers"}
        and left["integrity"] == right["integrity"] == "ok", "MATCH_BUSINESS_CHANGED")
    require({key: value for key, value in left["tables"].items() if key != "match_recorder_schema"}
        == {key: value for key, value in right["tables"].items() if key != "match_recorder_schema"}, "MATCH_BUSINESS_CHANGED")
    old, new = left["schemaMarkers"], right["schemaMarkers"]
    require(len(old) == len(new) == left["tables"]["match_recorder_schema"]["rows"]
        == right["tables"]["match_recorder_schema"]["rows"], "SCHEMA_MARKER_ROWS_CHANGED")
    for entry in (left, right):
        require(entry["tables"]["match_recorder_schema"] == {
            "rows": len(entry["schemaMarkers"]),
            "sha256": hash_value(sorted(hash_value(row) for row in entry["schemaMarkers"]))},
            "SCHEMA_MARKER_HASH_MISMATCH")
    refreshed = 0
    for first, second in zip(old, new):
        require(len(first) == len(second) == 3 and first[:2] == second[:2]
            and type(first[1]) is int and type(second[1]) is int, "SCHEMA_MARKER_VERSION_CHANGED")
        if first[2] != second[2]:
            require(first[0] == "match-analytics" and first[1] == 2
                and isinstance(first[2], str) and isinstance(second[2], str), "UNEXPECTED_SCHEMA_MARKER_CHANGED")
            previous, current = datetime.fromisoformat(first[2]), datetime.fromisoformat(second[2])
            require(previous.tzinfo is not None and current.tzinfo is not None and current >= previous, "SCHEMA_MARKER_TIME_REVERSED")
            refreshed += 1
    require(refreshed == 1, "UNEXPLAINED_SCHEMA_TABLE_CHANGE")
    return {"schemaTimestampRefreshes": refreshed, "component": "match-analytics", "version": 2}
