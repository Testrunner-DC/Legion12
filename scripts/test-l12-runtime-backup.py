"""Synthetic private-free SQLite/WAL and snapshot proof regressions."""
import hashlib
from contextlib import closing
import importlib.util
import io
import os
from pathlib import Path
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
            for path in self.runtime.iterdir():
                if path.name != omit:
                    archive.add(path, arcname="./" + path.name)
            if duplicate:
                archive.add(self.runtime / "platform.db", arcname="./platform.db")
        self.sha = hashlib.sha256(self.backup.read_bytes()).hexdigest()

    def verify(self):
        return proof.verify(self.runtime, self.backup, self.sha, test_only=True)

    def test_latest_databases_equal_snapshot(self):
        result = self.verify()
        self.assertTrue(result["verified"])
        self.assertEqual(2, result["databases"])
        self.assertEqual(0, result["persistentFactMutations"])

    def test_outstanding_wal_is_included_and_not_checkpointed(self):
        connection = sqlite3.connect(self.runtime / "matches.db")
        try:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("PRAGMA wal_autocheckpoint=0")
            connection.execute("INSERT INTO matches(value) VALUES('committed-wal-only')")
            connection.commit()
            self.snapshot()
            wal = (self.runtime / "matches.db-wal").read_bytes()
            self.assertTrue(self.verify()["latestDatabaseAndWalEqualBackup"])
            self.assertEqual(wal, (self.runtime / "matches.db-wal").read_bytes())
        finally:
            connection.close()

    def test_missing_wal_in_snapshot_rejected(self):
        (self.runtime / "matches.db-wal").write_bytes(b"synthetic-wal")
        self.snapshot(omit="matches.db-wal")
        with self.assertRaises(ValueError): self.verify()

    def test_stopped_writer_wal_survives_readonly_verification(self):
        code = "import os,sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('PRAGMA journal_mode=WAL'); c.execute('PRAGMA wal_autocheckpoint=0'); c.execute(\"INSERT INTO matches(value) VALUES('synthetic-stopped-wal')\"); c.commit(); os._exit(0)"
        subprocess.run([sys.executable, '-c', code, str(self.runtime / 'matches.db')], check=True, timeout=10)
        wal = (self.runtime / 'matches.db-wal').read_bytes()
        self.snapshot()
        self.assertTrue(self.verify()['latestDatabaseAndWalEqualBackup'])
        self.assertEqual(wal, (self.runtime / 'matches.db-wal').read_bytes())

    def test_stale_snapshot_rejected(self):
        with closing(sqlite3.connect(self.runtime / "matches.db")) as connection:
            connection.execute("INSERT INTO matches(value) VALUES('later-commit')")
            connection.commit()
        with self.assertRaises(ValueError): self.verify()

    def test_changed_archive_checksum_rejected(self):
        self.backup.write_bytes(self.backup.read_bytes() + b"changed")
        with self.assertRaises(ValueError): self.verify()

    def test_corrupt_database_even_with_matching_snapshot_rejected(self):
        (self.runtime / "matches.db").write_bytes(b"not a SQLite database")
        self.snapshot()
        with self.assertRaises(sqlite3.DatabaseError): self.verify()

    def test_duplicate_database_snapshot_entry_rejected(self):
        self.snapshot(duplicate=True)
        with self.assertRaises(ValueError): self.verify()

    def test_missing_database_rejected(self):
        (self.runtime / "platform.db").unlink()
        self.snapshot()
        with self.assertRaises(ValueError): self.verify()

    def test_required_schema_rejected(self):
        with closing(sqlite3.connect(self.runtime / "matches.db")) as connection:
            connection.execute("DROP TABLE matches")
            connection.commit()
        self.snapshot()
        with self.assertRaises(ValueError): self.verify()

    def test_incomplete_schema_anchor_rejected(self):
        with closing(sqlite3.connect(self.runtime / 'matches.db')) as connection:
            connection.execute('DROP TABLE matches')
            connection.execute('CREATE TABLE matches(id INTEGER,value TEXT)')
            connection.commit()
        self.snapshot()
        with self.assertRaises(ValueError): self.verify()

    def test_budget_expiry_rejected(self):
        with patch.object(proof, "LIMIT_SECONDS", -1):
            with self.assertRaises(ValueError): self.verify()

    def test_production_path_whitelist_rejected(self):
        with self.assertRaises(ValueError): proof.verify(self.runtime, self.backup, self.sha)

    def test_unknown_database_rejected(self):
        (self.runtime / 'unknown.db').write_bytes(b'synthetic-only')
        self.snapshot()
        with self.assertRaises(ValueError): self.verify()

    def test_hot_rollback_journal_rejected(self):
        (self.runtime / 'matches.db-journal').write_bytes(b'synthetic-only')
        self.snapshot()
        with self.assertRaises(ValueError): self.verify()


if __name__ == "__main__": unittest.main(verbosity=2)
