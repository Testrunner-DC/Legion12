"""Synthetic private-free SQLite/WAL and snapshot proof regressions."""
import hashlib
from contextlib import closing
import importlib.util
import io
import json
import os
from pathlib import Path
import platform
import sqlite3
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

root = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("runtime_proof", root / "ops/server/verify-l12-runtime-backup.py")
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)

EXPECTED_STAGES = [
    "path_guard",
    "backup_hash_before",
    "database_hash_before",
    "snapshot_stream",
    "sqlite_platform",
    "sqlite_matches",
    "database_hash_after",
    "backup_hash_after",
]
EXPECTED_REASON_CODES = {
    "TIME_BUDGET_EXCEEDED",
    "SNAPSHOT_NOT_LATEST",
    "PERSISTENT_FACTS_CHANGED",
    "BACKUP_CHECKSUM_CHANGED",
    "SQLITE_INTEGRITY_REJECTED",
    "SCHEMA_CONTRACT_REJECTED",
    "UNSAFE_PATH_OR_FILE_SET",
    "SQLITE_READ_UNAVAILABLE",
    "PROOF_REJECTED",
}


class RuntimeProofTests(unittest.TestCase):
    def setUp(self):
        base = Path(os.environ.get("L12_RUNTIME_PROOF_TEST_ROOT", "D:/GPT/Legion12/artifacts/runtime-backup-verifier-tests"))
        base.mkdir(parents=True, exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(prefix="synthetic-", dir=base)
        self.runtime = Path(self.directory.name) / "runtime"
        self.runtime.mkdir()
        self.backup = Path(self.directory.name) / "snapshot.tar.gz"
        for name, table in proof.DATABASES.items():
            with closing(sqlite3.connect(self.runtime / name)) as connection:
                columns = ','.join(column + ' TEXT' for column in sorted(proof.SCHEMA_ANCHORS[name]))
                connection.execute(f"CREATE TABLE {table}(id INTEGER PRIMARY KEY, value TEXT,{columns})")
                connection.execute(f"INSERT INTO {table}(value) VALUES('synthetic-only')")
                connection.commit()
        self.snapshot()

    def tearDown(self):
        self.directory.cleanup()

    def snapshot(self, omit=None, duplicate=False):
        with tarfile.open(self.backup, "w:gz") as archive:
            for path in sorted(self.runtime.iterdir()):
                if path.name != omit:
                    archive.add(path, arcname="./" + path.name)
            if duplicate:
                archive.add(self.runtime / "platform.db", arcname="./platform.db")
        self.sha = hashlib.sha256(self.backup.read_bytes()).hexdigest()

    def verify(self):
        return proof.verify(self.runtime, self.backup, self.sha, test_only=True)

    def assert_rejected(self, reason_code, stage=None):
        with self.assertRaises(proof.ProofError) as caught:
            self.verify()
        error = caught.exception
        self.assertIsInstance(error, ValueError)
        self.assertEqual(reason_code, error.code)
        self.assertIn(error.code, EXPECTED_REASON_CODES)
        self.assertEqual(reason_code, str(error))
        if stage is not None:
            self.assertEqual(stage, error.stage)
        self.assertGreaterEqual(error.elapsed_milliseconds, 0)
        self.assertIsInstance(error.stage_timings, list)
        for timing in error.stage_timings:
            self.assertEqual({"stage", "elapsedMilliseconds"}, set(timing))
            self.assertIn(timing["stage"], EXPECTED_STAGES)
            self.assertGreaterEqual(timing["elapsedMilliseconds"], 0)
        return error

    def create_clean_closed_wal_runtime(self):
        self.runtime = Path(self.directory.name) / "clean-closed-wal-runtime"
        self.runtime.mkdir()
        self.backup = Path(self.directory.name) / "clean-closed-wal-snapshot.tar.gz"
        for name, table in proof.DATABASES.items():
            with closing(sqlite3.connect(self.runtime / name)) as connection:
                self.assertEqual("wal", connection.execute("PRAGMA journal_mode=WAL").fetchone()[0])
                connection.execute("PRAGMA wal_autocheckpoint=0")
                columns = ','.join(column + ' TEXT' for column in sorted(proof.SCHEMA_ANCHORS[name]))
                connection.execute(f"CREATE TABLE {table}(id INTEGER PRIMARY KEY, value TEXT,{columns})")
                connection.execute(f"INSERT INTO {table}(value) VALUES('synthetic-clean-close')")
                connection.commit()
        for name in proof.DATABASES:
            self.assertFalse((self.runtime / (name + "-wal")).exists())
        self.snapshot()

    def leave_stopped_writer_wal(self):
        code = (
            "import os,sqlite3,sys; c=sqlite3.connect(sys.argv[1]); "
            "c.execute('PRAGMA journal_mode=WAL'); c.execute('PRAGMA wal_autocheckpoint=0'); "
            "c.execute(\"INSERT INTO matches(value) VALUES('synthetic-stopped-wal')\"); "
            "c.commit(); os._exit(0)"
        )
        subprocess.run([sys.executable, "-B", "-c", code, str(self.runtime / "matches.db")],
                       check=True, timeout=10)
        wal = self.runtime / "matches.db-wal"
        self.assertTrue(wal.is_file())
        self.assertGreater(wal.stat().st_size, 0)
        return wal

    def write_archive_member(self, archive, name, content=b"synthetic-only", *, kind="file"):
        member = tarfile.TarInfo(name)
        if kind == "symlink":
            member.type = tarfile.SYMTYPE
            member.linkname = "platform.db"
            archive.addfile(member)
            return
        member.size = len(content)
        archive.addfile(member, io.BytesIO(content))

    def rewrite_snapshot_with_member(self, name, *, omit=None, kind="file"):
        with tarfile.open(self.backup, "w:gz") as archive:
            for path in sorted(self.runtime.iterdir()):
                if path.name != omit:
                    archive.add(path, arcname="./" + path.name)
            self.write_archive_member(archive, name, kind=kind)
        self.sha = hashlib.sha256(self.backup.read_bytes()).hexdigest()

    def test_latest_databases_equal_snapshot(self):
        result = self.verify()
        self.assertTrue(result["verified"])
        self.assertEqual(1, result["schema"])
        self.assertEqual(2, result["databases"])
        self.assertEqual(0, result["persistentFactMutations"])
        self.assertFalse(result["checkpointOrRepairPerformed"])
        self.assertEqual(0, result["emptyWalPresenceTransitions"])
        self.assertEqual(EXPECTED_STAGES, [entry["stage"] for entry in result["stageTimings"]])
        for timing in result["stageTimings"]:
            self.assertEqual({"stage", "elapsedMilliseconds"}, set(timing))
            self.assertGreaterEqual(timing["elapsedMilliseconds"], 0)
        self.assertGreaterEqual(result["elapsedMilliseconds"], 0)

    def test_clean_closed_wal_mode_without_wal_is_readonly_verified(self):
        self.create_clean_closed_wal_runtime()
        database_hashes = {
            name: hashlib.sha256((self.runtime / name).read_bytes()).hexdigest()
            for name in proof.DATABASES
        }
        backup_hash = hashlib.sha256(self.backup.read_bytes()).hexdigest()

        result = self.verify()

        self.assertTrue(result["latestDatabaseAndWalEqualBackup"])
        self.assertEqual(2, result["emptyWalPresenceTransitions"])
        self.assertEqual(0, result["persistentFactMutations"])
        self.assertFalse(result["checkpointOrRepairPerformed"])
        self.assertEqual(backup_hash, hashlib.sha256(self.backup.read_bytes()).hexdigest())
        for name, expected_hash in database_hashes.items():
            self.assertEqual(expected_hash, hashlib.sha256((self.runtime / name).read_bytes()).hexdigest())
            wal = self.runtime / (name + "-wal")
            self.assertTrue(wal.is_file())
            self.assertEqual(0, wal.stat().st_size)

    def test_outstanding_wal_is_included_and_not_checkpointed(self):
        connection = sqlite3.connect(self.runtime / "matches.db")
        try:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("PRAGMA wal_autocheckpoint=0")
            connection.execute("INSERT INTO matches(value) VALUES('committed-wal-only')")
            connection.commit()
            self.snapshot()
            wal = (self.runtime / "matches.db-wal").read_bytes()
            self.assertGreater(len(wal), 0)
            database_hash = hashlib.sha256((self.runtime / "matches.db").read_bytes()).hexdigest()
            result = self.verify()
            self.assertTrue(result["latestDatabaseAndWalEqualBackup"])
            self.assertEqual(0, result["emptyWalPresenceTransitions"])
            self.assertEqual(wal, (self.runtime / "matches.db-wal").read_bytes())
            self.assertEqual(database_hash, hashlib.sha256((self.runtime / "matches.db").read_bytes()).hexdigest())
        finally:
            connection.close()

    def test_missing_wal_in_snapshot_rejected(self):
        (self.runtime / "matches.db-wal").write_bytes(b"synthetic-wal")
        self.snapshot(omit="matches.db-wal")
        with self.assertRaises(ValueError): self.verify()

    def test_stopped_writer_wal_survives_readonly_verification(self):
        wal_path = self.leave_stopped_writer_wal()
        wal = wal_path.read_bytes()
        self.snapshot()
        result = self.verify()
        self.assertTrue(result['latestDatabaseAndWalEqualBackup'])
        self.assertEqual(0, result["emptyWalPresenceTransitions"])
        self.assertEqual(wal, wal_path.read_bytes())

    def test_nonempty_wal_appearance_during_proof_is_rejected(self):
        database_hash = hashlib.sha256((self.runtime / "matches.db").read_bytes()).hexdigest()
        original = proof.persistent_facts
        calls = 0

        def facts_with_appearance(runtime, start):
            nonlocal calls
            calls += 1
            if calls == 2:
                (runtime / "matches.db-wal").write_bytes(b"nonempty-wal-appeared")
            return original(runtime, start)

        with patch.object(proof, "persistent_facts", side_effect=facts_with_appearance):
            self.assert_rejected("PERSISTENT_FACTS_CHANGED", "database_hash_after")
        self.assertEqual(2, calls)
        self.assertEqual(database_hash, hashlib.sha256((self.runtime / "matches.db").read_bytes()).hexdigest())

    def test_nonempty_wal_disappearance_during_proof_is_rejected(self):
        wal = self.leave_stopped_writer_wal()
        self.snapshot()
        database_hash = hashlib.sha256((self.runtime / "matches.db").read_bytes()).hexdigest()
        original = proof.persistent_facts
        calls = 0

        def facts_with_disappearance(runtime, start):
            nonlocal calls
            calls += 1
            if calls == 2 and wal.exists():
                wal.unlink()
            return original(runtime, start)

        with patch.object(proof, "persistent_facts", side_effect=facts_with_disappearance):
            self.assert_rejected("PERSISTENT_FACTS_CHANGED", "database_hash_after")
        self.assertEqual(2, calls)
        self.assertEqual(database_hash, hashlib.sha256((self.runtime / "matches.db").read_bytes()).hexdigest())

    def test_nonempty_wal_hash_change_during_proof_is_rejected(self):
        wal = self.leave_stopped_writer_wal()
        self.snapshot()
        original = proof.persistent_facts
        calls = 0

        def facts_with_change(runtime, start):
            nonlocal calls
            calls += 1
            if calls == 2:
                wal.write_bytes(wal.read_bytes() + b"changed")
            return original(runtime, start)

        with patch.object(proof, "persistent_facts", side_effect=facts_with_change):
            self.assert_rejected("PERSISTENT_FACTS_CHANGED", "database_hash_after")
        self.assertEqual(2, calls)

    def test_main_database_hash_change_during_proof_is_rejected(self):
        database = self.runtime / "matches.db"
        original = proof.persistent_facts
        calls = 0

        def facts_with_change(runtime, start):
            nonlocal calls
            calls += 1
            if calls == 2:
                with database.open("ab") as stream:
                    stream.write(b"main-database-changed")
            return original(runtime, start)

        with patch.object(proof, "persistent_facts", side_effect=facts_with_change):
            self.assert_rejected("PERSISTENT_FACTS_CHANGED", "database_hash_after")
        self.assertEqual(2, calls)

    def test_stale_snapshot_rejected(self):
        with closing(sqlite3.connect(self.runtime / "matches.db")) as connection:
            connection.execute("INSERT INTO matches(value) VALUES('later-commit')")
            connection.commit()
        self.assert_rejected("SNAPSHOT_NOT_LATEST", "snapshot_stream")

    def test_changed_archive_checksum_rejected(self):
        self.backup.write_bytes(self.backup.read_bytes() + b"changed")
        self.assert_rejected("BACKUP_CHECKSUM_CHANGED", "backup_hash_before")

    def test_archive_checksum_change_during_proof_is_rejected(self):
        original = proof.persistent_facts
        calls = 0

        def facts_then_change_backup(runtime, start):
            nonlocal calls
            calls += 1
            facts = original(runtime, start)
            if calls == 2:
                self.backup.write_bytes(self.backup.read_bytes() + b"changed-after-snapshot-read")
            return facts

        with patch.object(proof, "persistent_facts", side_effect=facts_then_change_backup):
            self.assert_rejected("BACKUP_CHECKSUM_CHANGED", "backup_hash_after")
        self.assertEqual(2, calls)

    def test_corrupt_database_even_with_matching_snapshot_rejected(self):
        (self.runtime / "matches.db").write_bytes(b"not a SQLite database")
        self.snapshot()
        self.assert_rejected("SQLITE_INTEGRITY_REJECTED", "sqlite_matches")

    def test_duplicate_database_snapshot_entry_rejected(self):
        self.snapshot(duplicate=True)
        self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "snapshot_stream")

    def test_missing_database_rejected(self):
        (self.runtime / "platform.db").unlink()
        self.snapshot()
        self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "database_hash_before")

    def test_required_schema_rejected(self):
        with closing(sqlite3.connect(self.runtime / "matches.db")) as connection:
            connection.execute("DROP TABLE matches")
            connection.commit()
        self.snapshot()
        self.assert_rejected("SCHEMA_CONTRACT_REJECTED", "sqlite_matches")

    def test_incomplete_schema_anchor_rejected(self):
        with closing(sqlite3.connect(self.runtime / 'matches.db')) as connection:
            connection.execute('DROP TABLE matches')
            connection.execute('CREATE TABLE matches(id INTEGER,value TEXT)')
            connection.commit()
        self.snapshot()
        self.assert_rejected("SCHEMA_CONTRACT_REJECTED", "sqlite_matches")

    def test_budget_expiry_rejected(self):
        with patch.object(proof, "LIMIT_SECONDS", -1):
            self.assert_rejected("TIME_BUDGET_EXCEEDED", "path_guard")

    def test_runtime_budget_is_bounded_to_five_minutes(self):
        self.assertEqual(300, proof.LIMIT_SECONDS)

    def test_production_path_whitelist_rejected(self):
        with self.assertRaises(proof.ProofError) as caught:
            proof.verify(self.runtime, self.backup, self.sha)
        self.assertEqual("UNSAFE_PATH_OR_FILE_SET", caught.exception.code)
        self.assertEqual("path_guard", caught.exception.stage)

    def test_unknown_database_rejected(self):
        (self.runtime / 'unknown.db').write_bytes(b'synthetic-only')
        self.snapshot()
        self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "path_guard")

    def test_hot_rollback_journal_rejected(self):
        (self.runtime / 'matches.db-journal').write_bytes(b'synthetic-only')
        self.snapshot()
        self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "path_guard")

    def test_archive_path_traversal_rejected_before_private_entry_read(self):
        self.rewrite_snapshot_with_member("../private-sentinel.db")
        error = self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "snapshot_stream")
        self.assertNotIn("private-sentinel", str(error).lower())

    def test_unknown_root_database_archive_entry_rejected(self):
        self.rewrite_snapshot_with_member("unknown-private.db")
        self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "snapshot_stream")

    def test_persistent_archive_symlink_entry_rejected(self):
        self.rewrite_snapshot_with_member("matches.db", omit="matches.db", kind="symlink")
        self.assert_rejected("UNSAFE_PATH_OR_FILE_SET", "snapshot_stream")

    def test_cli_failure_is_constant_private_free_json(self):
        private_runtime = self.runtime / "private-sentinel-customer-42"
        completed = subprocess.run(
            [sys.executable, "-B", str(root / "ops/server/verify-l12-runtime-backup.py"),
             str(private_runtime), str(self.backup), self.sha],
            capture_output=True, text=True, timeout=10, check=False,
        )
        self.assertEqual(1, completed.returncode)
        self.assertEqual("", completed.stderr)
        payload = json.loads(completed.stdout)
        self.assertEqual(
            {"schema", "verified", "reasonCode", "failedStage", "elapsedMilliseconds", "stageTimings"},
            set(payload),
        )
        self.assertEqual(1, payload["schema"])
        self.assertFalse(payload["verified"])
        self.assertEqual("UNSAFE_PATH_OR_FILE_SET", payload["reasonCode"])
        self.assertEqual("path_guard", payload["failedStage"])
        self.assertGreaterEqual(payload["elapsedMilliseconds"], 0)
        self.assertIsInstance(payload["stageTimings"], list)
        rendered = completed.stdout + completed.stderr
        self.assertNotIn("private-sentinel", rendered.lower())
        self.assertNotIn(str(self.runtime), rendered)

    def test_cli_unexpected_failure_keeps_the_full_private_free_schema(self):
        source_path = root / "ops/server/verify-l12-runtime-backup.py"
        source = source_path.read_text(encoding="utf-8")
        normal_print = '        print(json.dumps(verify(*sys.argv[1:]), separators=(",", ":")))'
        self.assertEqual(1, source.count(normal_print))
        injected = source.replace(normal_print, '        raise RuntimeError("PRIVATE_SENTINEL")', 1)
        injected_path = Path(self.directory.name) / "unexpected-cli-fixture.py"
        injected_path.write_text(injected, encoding="utf-8")
        completed = subprocess.run(
            [sys.executable, "-B", str(injected_path), "runtime", "backup", "checksum"],
            capture_output=True, text=True, timeout=10, check=False,
        )
        self.assertEqual(1, completed.returncode)
        self.assertEqual("", completed.stderr)
        payload = json.loads(completed.stdout)
        self.assertEqual(
            {"schema", "verified", "reasonCode", "failedStage", "elapsedMilliseconds", "stageTimings"},
            set(payload),
        )
        self.assertEqual("PROOF_REJECTED", payload["reasonCode"])
        self.assertEqual("path_guard", payload["failedStage"])
        self.assertEqual(0, payload["elapsedMilliseconds"])
        self.assertEqual([], payload["stageTimings"])
        self.assertNotIn("PRIVATE_SENTINEL", completed.stdout + completed.stderr)

    def test_sqlite_busy_error_maps_to_private_free_read_unavailable(self):
        busy = sqlite3.OperationalError("PRIVATE_SENTINEL database path")
        busy.sqlite_errorcode = sqlite3.SQLITE_BUSY
        with patch.object(proof.sqlite3, "connect", side_effect=busy):
            error = self.assert_rejected("SQLITE_READ_UNAVAILABLE", "sqlite_platform")
        self.assertNotIn("PRIVATE_SENTINEL", str(error))

    def test_unexpected_internal_error_maps_to_private_free_constant(self):
        with patch.object(proof, "persistent_facts", side_effect=RuntimeError("PRIVATE_SENTINEL payload")):
            error = self.assert_rejected("PROOF_REJECTED", "database_hash_before")
        self.assertNotIn("PRIVATE_SENTINEL", str(error))

    def test_only_known_empty_wal_facts_are_canonicalized(self):
        empty = (0, proof.EMPTY_SHA256)
        nonempty = (1, hashlib.sha256(b"x").hexdigest())
        facts = {
            "matches.db": (10, "a" * 64),
            "matches.db-wal": empty,
            "platform.db-wal": nonempty,
            "unknown.db-wal": empty,
        }
        self.assertEqual(
            {
                "matches.db": facts["matches.db"],
                "platform.db-wal": nonempty,
                "unknown.db-wal": empty,
            },
            proof.canonical_facts(facts),
        )


if __name__ == "__main__":
    print(f"[runtime proof versions] Python {platform.python_version()} / SQLite {sqlite3.sqlite_version}", flush=True)
    unittest.main(verbosity=2)
