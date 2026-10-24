"""Pure synthetic restore-evidence boundaries. No application or network starts."""
import argparse
import ast
from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import time
import unittest

spec = importlib.util.spec_from_file_location("restore_facts", Path(__file__).with_name("restore-business-facts.py"))
facts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(facts)


class RestoreFacts(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(dir=WORK)
        self.root = Path(self.directory.name).resolve()
        self.snapshot = {key: [{"synthetic": key, "text": "中文\n引号\""}] for key in facts.REQUIRED}
        self.snapshot["OtherBusiness"] = [{"mustAlsoSurvive": True}]
        for name in ("platform.db", "matches.db"):
            with closing(sqlite3.connect(self.root / name)) as db, db:
                if name == "platform.db":
                    db.execute("CREATE TABLE platform_state(singleton_id INTEGER PRIMARY KEY,snapshot_json TEXT)")
                    db.execute("INSERT INTO platform_state VALUES(1,?)", (json.dumps(self.snapshot),))
                    db.execute("CREATE TABLE audit_entries(id INTEGER,payload BLOB)")
                    db.execute("INSERT INTO audit_entries VALUES(1,?)", (b"synthetic-private-audit",))
                else: db.execute("CREATE TABLE matches(id INTEGER)")
        (self.root / "site-media").mkdir()
        (self.root / "site-media" / "synthetic.webp").write_bytes(b"synthetic-test-bytes")

    def tearDown(self): self.directory.cleanup()

    def read(self): return facts.database_facts(self.root, time.monotonic() + 5)

    def update_snapshot(self, raw):
        with closing(sqlite3.connect(self.root / "platform.db")) as db, db: db.execute("UPDATE platform_state SET snapshot_json=?", (raw,))

    def test_all_snapshot_fields_and_tables_included_private_body_absent(self):
        value = self.read()
        self.assertIn("OtherBusiness", value["platform.db"]["snapshot"])
        self.assertIn("audit_entries", value["platform.db"]["tables"])
        encoded = json.dumps(value)
        self.assertNotIn("synthetic-private-audit", encoded)
        self.assertNotIn("mustAlsoSurvive", encoded)
        self.assertNotIn("中文", encoded)

    def test_read_only_database_bytes(self):
        before = facts.tree_manifest(self.root)
        self.read()
        self.assertEqual(facts.tree_manifest(self.root), before)

    def test_nonempty_coverage(self):
        result = facts.nonempty_coverage(self.read(), facts.tree_manifest(self.root))
        self.assertEqual(result["collections"], {key: 1 for key in facts.REQUIRED})
        self.assertEqual(result["uploadedFiles"], 1)

    def test_each_empty_collection_refused(self):
        original = self.read()
        for key in facts.REQUIRED:
            value = json.loads(json.dumps(original))
            value["platform.db"]["snapshot"][key]["count"] = 0
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "NONEMPTY_BUSINESS"): facts.nonempty_coverage(value, facts.tree_manifest(self.root))

    def test_missing_media_refused(self):
        with self.assertRaisesRegex(ValueError, "NONEMPTY_MEDIA"): facts.nonempty_coverage(self.read(), {})

    def test_zero_byte_media_refused(self):
        (self.root / "site-media" / "synthetic.webp").write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "NONEMPTY_MEDIA"): facts.nonempty_coverage(self.read(), facts.tree_manifest(self.root))

    def test_body_change_is_visible_as_hash_only(self):
        before = self.read()
        self.snapshot["Articles"][0]["text"] = "changed body"
        self.update_snapshot(json.dumps(self.snapshot))
        after = self.read()
        self.assertNotEqual(before["platform.db"]["snapshot"]["Articles"], after["platform.db"]["snapshot"]["Articles"])
        self.assertNotIn("changed body", json.dumps(after))

    def test_other_table_change_is_not_silently_ignored(self):
        before = self.read()
        with closing(sqlite3.connect(self.root / "platform.db")) as db, db: db.execute("UPDATE audit_entries SET payload='changed'")
        self.assertNotEqual(before, self.read())

    def test_row_order_is_not_a_false_difference(self):
        with closing(sqlite3.connect(self.root / "matches.db")) as db, db: db.executemany("INSERT INTO matches VALUES(?)", [(1,), (2,)])
        before = self.read()
        with closing(sqlite3.connect(self.root / "matches.db")) as db, db:
            db.execute("DELETE FROM matches")
            db.executemany("INSERT INTO matches VALUES(?)", [(2,), (1,)])
        self.assertEqual(before, self.read())

    def test_media_byte_change_detected(self):
        before = facts.tree_manifest(self.root)
        (self.root / "site-media" / "synthetic.webp").write_bytes(b"changed")
        self.assertNotEqual(before, facts.tree_manifest(self.root))

    def test_duplicate_snapshot_keys_rejected(self):
        self.update_snapshot('{"Articles":[],"Articles":[]}')
        with self.assertRaisesRegex(ValueError, "DUPLICATE"): self.read()

    def test_required_collection_cannot_be_a_scalar(self):
        self.snapshot["Articles"] = 4
        self.update_snapshot(json.dumps(self.snapshot))
        with self.assertRaisesRegex(ValueError, "COLLECTION_MISSING"): self.read()

    def test_missing_database_not_created(self):
        (self.root / "matches.db").unlink()
        with self.assertRaises(FileNotFoundError): self.read()
        self.assertFalse((self.root / "matches.db").exists())

    def test_relative_root_refused(self):
        with self.assertRaisesRegex(ValueError, "PHYSICAL"): facts.tree_manifest(Path("relative"))

    def test_file_budget(self):
        original = facts.MAX_FILES
        try:
            facts.MAX_FILES = 1
            with self.assertRaisesRegex(ValueError, "BUDGET"): facts.tree_manifest(self.root)
        finally: facts.MAX_FILES = original

    def test_byte_budget(self):
        original = facts.MAX_BYTES
        try:
            facts.MAX_BYTES = 1
            with self.assertRaisesRegex(ValueError, "BUDGET"): facts.tree_manifest(self.root)
        finally: facts.MAX_BYTES = original

    def test_hard_link_refused(self):
        os.link(self.root / "platform.db", self.root / "copy.db")
        with self.assertRaisesRegex(ValueError, "LINK_OR_SPECIAL"): self.read()

    def test_expired_budget_refused(self):
        with self.assertRaisesRegex(ValueError, "TIME_BUDGET"): facts.database_facts(self.root, time.monotonic() - 1)

    def test_isolated_application_does_not_inherit_shared_gallery_or_ephemeral_marker(self):
        source = Path(__file__).with_name("restore-business-drill.py").read_text(encoding="utf8")
        tree = ast.parse(source)
        environments = [node.value for node in ast.walk(tree) if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "environment" for target in node.targets)]
        self.assertEqual(len(environments), 1)
        keys = [ast.literal_eval(key) for key in environments[0].keys]
        self.assertNotIn("L12_TESTRUN_SHARED_GALLERY", keys)
        self.assertNotIn("L12_TESTRUN_MATCH_STORAGE", keys)

    def test_real_function_proof_is_persisted_before_actual_application_start(self):
        source = Path(__file__).with_name("restore-business-drill.py").read_text(encoding="utf8")
        tree = ast.parse(source)
        writes = [node for node in ast.walk(tree) if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "durable_proof"]
        starts = [node for node in ast.walk(tree) if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "Popen"]
        self.assertEqual(len(writes), 2)
        self.assertEqual(len(starts), 1)
        self.assertLess(min(node.lineno for node in writes), starts[0].lineno)
        self.assertIn('os.fsync(stream.fileno())', source)
        self.assertIn('os.fsync(descriptor)', source)

    def test_local_exposure_not_wildcard_peer_and_exact_process_owner(self):
        valid = 'LISTEN 0 512 127.0.0.1:51234 0.0.0.0:* users:(("dotnet",pid=1234,fd=11))'
        facts.require_owned_loopback_listener(valid, 51234, 1234)
        for value in [valid.replace('127.0.0.1:', '0.0.0.0:'), valid.replace('127.0.0.1:51234', '[::]:51234'),
            valid.replace('pid=1234,', 'pid=12345,'), valid.replace(':51234', ':51235'),
            valid + '\n' + valid, valid.replace('LISTEN', 'ESTAB'), valid.replace('pid=1234,fd=11', 'pid=1234,fd=11),("other",pid=99,fd=10')]:
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, 'LOOPBACK_LISTENER_REQUIRED'):
                facts.require_owned_loopback_listener(value, 51234, 1234)

    def schema_markers(self):
        with closing(sqlite3.connect(self.root / "matches.db")) as db, db:
            db.execute("CREATE TABLE match_recorder_schema(component TEXT PRIMARY KEY,version INTEGER,migrated_utc TEXT)")
            db.executemany("INSERT INTO match_recorder_schema VALUES(?,?,?)", [
                ("match-analytics", 2, "2026-10-24T04:00:00+00:00"),
                ("disaster-setup-analytics", 1, "2026-10-23T04:00:00+00:00")])

    def mutate_marker(self, sql, args=()):
        with closing(sqlite3.connect(self.root / "matches.db")) as db, db: db.execute(sql, args)

    def test_known_startup_timestamp_refresh_preserves_all_business(self):
        self.schema_markers()
        before = self.read()
        self.mutate_marker("UPDATE match_recorder_schema SET migrated_utc=? WHERE component='match-analytics'",
            ("2026-10-24T05:00:00+00:00",))
        after = self.read()
        original = json.dumps([before, after], sort_keys=True)
        self.assertEqual(facts.require_started_business_preserved(before, after), {
            "schemaTimestampRefreshes": 1, "component": "match-analytics", "version": 2})
        self.assertEqual(original, json.dumps([before, after], sort_keys=True))
        self.assertEqual(facts.require_started_business_preserved(before, before), {"schemaTimestampRefreshes": 0})

    def test_schema_marker_changes_are_not_a_table_wide_exception(self):
        self.schema_markers()
        before = self.read()
        for sql in ["UPDATE match_recorder_schema SET version=3 WHERE component='match-analytics'",
            "UPDATE match_recorder_schema SET component='unknown' WHERE component='match-analytics'",
            "UPDATE match_recorder_schema SET migrated_utc='2026-10-24T05:00:00+00:00' WHERE component='disaster-setup-analytics'",
            "UPDATE match_recorder_schema SET migrated_utc='2026-10-24T03:00:00+00:00' WHERE component='match-analytics'",
            "UPDATE match_recorder_schema SET migrated_utc='2026-10-24T05:00:00' WHERE component='match-analytics'",
            "INSERT INTO match_recorder_schema VALUES('new',1,'2026-10-24T05:00:00+00:00')"]:
            with self.subTest(sql=sql):
                self.mutate_marker(sql)
                with self.assertRaises(ValueError): facts.require_started_business_preserved(before, self.read())
                self.mutate_marker("DELETE FROM match_recorder_schema")
                with closing(sqlite3.connect(self.root / "matches.db")) as db, db:
                    db.executemany("INSERT INTO match_recorder_schema VALUES(?,?,?)", before["matches.db"]["schemaMarkers"])

    def test_unknown_schema_marker_columns_refused(self):
        self.schema_markers()
        self.mutate_marker("ALTER TABLE match_recorder_schema ADD COLUMN unknown TEXT")
        with self.assertRaisesRegex(ValueError, "COLUMNS_CHANGED"): self.read()

    def test_real_business_changes_still_refused_with_valid_metadata_refresh(self):
        self.schema_markers()
        before = self.read()
        self.mutate_marker("UPDATE match_recorder_schema SET migrated_utc='2026-10-24T05:00:00+00:00' WHERE component='match-analytics'")
        after = self.read()
        for database, key in [("platform.db", "tables"), ("platform.db", "snapshot"), ("matches.db", "tables")]:
            changed = json.loads(json.dumps(after))
            changed[database][key]["newBusinessFact"] = {"rows": 1, "sha256": "0" * 64}
            with self.subTest(database=database, key=key), self.assertRaises(ValueError):
                facts.require_started_business_preserved(before, changed)

    def test_schema_hash_must_bind_exact_public_metadata_rows(self):
        self.schema_markers()
        before = self.read()
        self.mutate_marker("UPDATE match_recorder_schema SET migrated_utc='2026-10-24T05:00:00+00:00' WHERE component='match-analytics'")
        after = self.read()
        after["matches.db"]["tables"]["match_recorder_schema"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "HASH_MISMATCH"): facts.require_started_business_preserved(before, after)

    def test_schema_table_hash_only_change_cannot_pass(self):
        self.schema_markers()
        before = self.read()
        after = json.loads(json.dumps(before))
        after["matches.db"]["tables"]["match_recorder_schema"]["sha256"] = "0" * 64
        with self.assertRaises(ValueError): facts.require_started_business_preserved(before, after)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--work-root", required=True)
    args = parser.parse_args()
    WORK = Path(args.work_root)
    if not WORK.is_absolute(): raise ValueError("ABSOLUTE_WORK_ROOT_REQUIRED")
    script = Path(__file__).resolve().parents[1] / "windows" / "L12GeneratedStorage.ps1"
    env = dict(os.environ, L12_RESTORE_GUARD=str(script), L12_RESTORE_OUTPUT=str(WORK))
    subprocess.run([env["L12_PWSH"], "-NoProfile", "-NonInteractive", "-Command",
        "$ErrorActionPreference='Stop'; . $env:L12_RESTORE_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_RESTORE_OUTPUT -Purpose 'synthetic restore facts'"],
        env=env, check=True, capture_output=True, timeout=15000)
    WORK.mkdir(parents=True, exist_ok=True)
    unittest.main(argv=[__file__])
