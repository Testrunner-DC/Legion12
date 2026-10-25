"""Real local SQLite positive/negative tests. No subprocess, service or network access."""
import dataclasses
import ast
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import sys
import uuid

sys.dont_write_bytecode = True
MODULE = Path(__file__).with_name("testrun-owned-analytics-import.py")
spec = importlib.util.spec_from_file_location("owned_import", MODULE)
tool = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = tool
spec.loader.exec_module(tool)


def fixture(root):
    root.mkdir()
    source, target, proof = root / "source.sqlite", root / "target.sqlite", root / "fixture-proof.json"
    run = uuid.uuid4().hex
    ids = [f"qa-history-{run}-{i:02}" for i in range(16)]
    sql = """
    PRAGMA foreign_keys=ON;
    CREATE TABLE matches(match_id TEXT PRIMARY KEY, payload BLOB NOT NULL);
    CREATE TABLE facts(id INTEGER PRIMARY KEY AUTOINCREMENT, match_id TEXT NOT NULL REFERENCES matches(match_id), value TEXT);
    CREATE INDEX facts_match ON facts(match_id);
    CREATE TABLE match_recorder_schema(component TEXT PRIMARY KEY, version INTEGER NOT NULL, migrated_utc TEXT NOT NULL);
    CREATE TABLE schedule(name TEXT PRIMARY KEY, next TEXT);
    CREATE TABLE other_data(id INTEGER PRIMARY KEY, value TEXT);
    """
    for name in (source, target):
        con = sqlite3.connect(name)
        con.executescript(sql)
        con.execute("INSERT INTO match_recorder_schema VALUES('journal',2,?)",
                    ("2026-10-22T13:00:00Z" if name == source else "2026-10-22T10:00:00Z",))
        con.execute("INSERT INTO schedule VALUES('next',?)", ("source" if name == source else "preserve-target",))
        if name == source:
            con.executemany("INSERT INTO matches VALUES(?,?)", [(key, b"\x00\xff" + key.encode()) for key in ids])
            con.executemany("INSERT INTO facts(match_id,value) VALUES(?,?)", [(key, "合成<script>") for key in ids for _ in range(2)])
        con.commit()
        con.close()
    proof.write_text(json.dumps({"runId": str(uuid.UUID(run)), "matchIds": ids, "matches": 16,
         "minimumReturnedSamples": 5, "defaultQueryTotal": 76, "serviceDatabaseWrites": 0,
         "assemblySha256": tool.TEST_DLL, "database": str(source)}), encoding="utf-8")
    policy = tool.Policy(run, tool.file_sha(source), tool.file_sha(proof), {"matches": 16, "facts": 32},
                         frozenset({"match_recorder_schema", "schedule", "other_data"}),
                         {"match_recorder_schema": 1, "schedule": 1})
    return source, proof, target, root / "receipts", policy


def change(path, sql, args=()):
    with sqlite3.connect(path) as con:
        con.execute(sql, args)


def counts(path):
    with sqlite3.connect(path) as con:
        return tuple(con.execute("SELECT COUNT(*) FROM " + table).fetchone()[0] for table in ("matches", "facts"))


def main():
    if len(sys.argv) != 3:
        raise RuntimeError("Explicit governed cache and evidence required")
    cache, output = map(Path, sys.argv[1:])
    assert cache.is_absolute() and output.is_absolute()
    assert str(cache).replace("\\", "/").lower().startswith("d:/gpt/legion12/cache/primary/")
    assert str(output).replace("\\", "/").lower().startswith("d:/gpt/legion12/artifacts/")
    cache.mkdir(parents=True, exist_ok=False)
    output.mkdir(parents=True, exist_ok=False)
    results = []

    terminal_value = {"schema": 1, "kind": "terminal-owned-analytics-import-v1",
        "operationRunId": "1" * 32, "fixtureRunId": "2" * 32,
        "databaseSha256": "A" * 64, "proofSha256": "B" * 64,
        "test": {"pid": 101, "commit": "a" * 40, "dllSha256": "C" * 64},
        "formal": {"pid": 102, "commit": "b" * 40, "dllSha256": "D" * 64},
        "diagnosticSetting": "terminal-recipients-v4:30:" + "3" * 32,
        "templateSourceSha256": "E" * 64}
    raw = json.dumps(terminal_value, sort_keys=True, separators=(",", ":")).encode()
    selected = tool.parse_terminal_binding(raw, tool.digest(raw))
    assert selected.source_root == Path("/tmp/l12-analytics-fixture-" + "2" * 32)
    assert selected.receipts == Path("/tmp/l12-analytics-import-" + "2" * 32 + "-terminal-" + "1" * 32)
    results.append({"name": "strict-terminal-binding-derives-only-fixed-roots", "passed": True})
    for name, mutate, expected_sha in (
        ("binding-extra-field", lambda value: value.__setitem__("extra", True), None),
        ("binding-bool-schema", lambda value: value.__setitem__("schema", True), None),
        ("binding-bool-pid", lambda value: value["test"].__setitem__("pid", True), None),
        ("binding-same-pid", lambda value: value["formal"].__setitem__("pid", value["test"]["pid"]), None),
        ("binding-diagnostic-scope", lambda value: value.__setitem__("diagnosticSetting", "terminal-recipients-v4:31:" + "3" * 32), None),
        ("binding-template-source", lambda value: value.__setitem__("templateSourceSha256", "wrong"), None),
        ("binding-sha", lambda _value: None, "0" * 64)):
        value = json.loads(json.dumps(terminal_value)); mutate(value)
        changed = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
        try:
            tool.parse_terminal_binding(changed, expected_sha or tool.digest(changed))
            raise AssertionError(name + " accepted")
        except RuntimeError:
            pass
        results.append({"name": "reject-" + name, "passed": True})
    duplicate = raw[:-1] + b',"schema":1}'
    try:
        tool.parse_terminal_binding(duplicate, tool.digest(duplicate))
        raise AssertionError("duplicate binding field accepted")
    except RuntimeError:
        pass
    results.append({"name": "reject-binding-duplicate-field", "passed": True})

    normal_value = {"schema": 1, "kind": "normal-owned-analytics-import-v1",
        "operationRunId": "4" * 32, "fixtureRunId": "5" * 32,
        "qualificationBindingSha256": "F945C94BBC8558DB58E2164348973A57984F94CBDA71BA60B793D119C687A89C",
        "databaseSha256": "6" * 64, "proofSha256": "7" * 64,
        "test": {"pid": 201, "commit": "c8750df92e2c9f477c88496c91bd63b105af95cd",
                 "dllSha256": "8" * 64},
        "formal": {"pid": 202, "commit": "9" * 40, "dllSha256": "A" * 64},
        "diagnosticsOff": True, "templateSourceSha256": "B" * 64}
    normal_raw = json.dumps(normal_value, sort_keys=True, separators=(",", ":")).encode()
    normal = tool.parse_normal_binding(normal_raw, tool.digest(normal_raw))
    assert normal.source_root == Path("/tmp/l12-analytics-fixture-" + "5" * 32)
    assert normal.receipts == Path("/tmp/l12-analytics-import-" + "5" * 32 + "-normal-" + "4" * 32)
    assert normal.diagnostic_setting == ""
    assert normal.qualification_binding_sha == normal_value["qualificationBindingSha256"]
    results.append({"name": "strict-normal-binding-derives-only-fixed-roots-and-diagnostics-off", "passed": True})
    for name, mutate, expected_sha in (
        ("normal-extra-field", lambda value: value.__setitem__("extra", True), None),
        ("normal-bool-schema", lambda value: value.__setitem__("schema", True), None),
        ("normal-bool-pid", lambda value: value["test"].__setitem__("pid", True), None),
        ("normal-same-pid", lambda value: value["formal"].__setitem__("pid", value["test"]["pid"]), None),
        ("normal-diagnostics-false", lambda value: value.__setitem__("diagnosticsOff", False), None),
        ("normal-diagnostics-int", lambda value: value.__setitem__("diagnosticsOff", 1), None),
        ("normal-diagnostics-string", lambda value: value.__setitem__("diagnosticsOff", "true"), None),
        ("normal-missing-field", lambda value: value.pop("proofSha256"), None),
        ("normal-qualification-binding", lambda value: value.__setitem__("qualificationBindingSha256", "wrong"), None),
        ("normal-template-source", lambda value: value.__setitem__("templateSourceSha256", "wrong"), None),
        ("normal-binding-sha", lambda _value: None, "0" * 64)):
        value = json.loads(json.dumps(normal_value)); mutate(value)
        changed = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
        try:
            tool.parse_normal_binding(changed, expected_sha or tool.digest(changed))
            raise AssertionError(name + " accepted")
        except RuntimeError:
            pass
        results.append({"name": "reject-" + name, "passed": True})
    normal_duplicate = normal_raw[:-1] + b',"diagnosticsOff":true}'
    try:
        tool.parse_normal_binding(normal_duplicate, tool.digest(normal_duplicate))
        raise AssertionError("normal duplicate binding field accepted")
    except RuntimeError:
        pass
    results.append({"name": "reject-normal-binding-duplicate-field", "passed": True})
    for name, parser, selected in (
        ("terminal-as-normal", tool.parse_normal_binding, raw),
        ("normal-as-terminal", tool.parse_terminal_binding, normal_raw)):
        try:
            parser(selected, tool.digest(selected))
            raise AssertionError(name + " accepted")
        except RuntimeError:
            pass
        results.append({"name": "reject-" + name, "passed": True})

    def case(name, mutate=None, hook=None, success=False, committed=False, target_count=0):
        args = list(fixture(cache / name))
        if mutate:
            mutate(args)
        failure = None
        try:
            proof = tool.append_fixture(*args, hook=hook or (lambda *_: None))
            assert success and proof["status"] == "COMMITTED_VERIFIED"
        except (RuntimeError, sqlite3.Error, FileExistsError) as error:
            failure = type(error).__name__
            assert not success
        assert counts(args[2]) == ((16, 32) if success or committed else (target_count, 0))
        with sqlite3.connect(args[2]) as con:
            assert con.execute("SELECT next FROM schedule").fetchone()[0] == "preserve-target"
            assert con.execute("SELECT migrated_utc FROM match_recorder_schema").fetchone()[0] == "2026-10-22T10:00:00Z"
        if success:
            assert counts(args[3] / "before.sqlite") == (0, 0)
            try:
                tool.append_fixture(*args)
                raise AssertionError("reentry accepted")
            except FileExistsError:
                pass
        if committed:
            receipt = json.loads((args[3] / "failure.json").read_text())
            assert receipt["committed"] is True and receipt["status"] == "COMMITTED_READBACK_PENDING"
        if (args[3] / "failure.json").exists() and not success and not committed:
            receipt = json.loads((args[3] / "failure.json").read_text())
            assert receipt["committed"] is False
        results.append({"name": name, "passed": True, "expectedRejection": failure})

    def source_change(args, sql):
        change(args[0], sql)
        args[4] = dataclasses.replace(args[4], database_sha=tool.file_sha(args[0]))

    case("positive-full-closure-different-migration-times-preserved-backup-reentry", success=True)
    case("wrong-database-SHA", lambda a: a.__setitem__(4, dataclasses.replace(a[4], database_sha="0" * 64)))
    case("wrong-proof-SHA", lambda a: a.__setitem__(4, dataclasses.replace(a[4], proof_sha="0" * 64)))
    case("wrong-bound-assembly-SHA", lambda a: a.__setitem__(4, dataclasses.replace(a[4], assembly_sha="0" * 64)))
    case("old-nonempty-target", lambda a: change(a[2], "INSERT INTO matches VALUES('old',X'01')"), target_count=1)
    case("unknown-source-table", lambda a: source_change(a, "CREATE TABLE unknown_table(value TEXT)"))
    case("unknown-nonmatch-source-data", lambda a: source_change(a, "INSERT INTO other_data VALUES(1,'unexpected')"))
    case("schema-index-mismatch", lambda a: change(a[2], "CREATE INDEX foreign_index ON matches(payload)"))
    case("version-metadata-mismatch", lambda a: change(a[2], "UPDATE match_recorder_schema SET version=3"))
    case("component-metadata-mismatch", lambda a: change(a[2], "UPDATE match_recorder_schema SET component='unknown'"))
    for suffix in ("-wal", "-shm"):
        case("reject-existing-source" + suffix, lambda a, suffix=suffix: Path(str(a[0]) + suffix).touch())
    def foreign_match(args):
        with sqlite3.connect(args[0]) as con:
            old = con.execute("SELECT match_id FROM matches ORDER BY match_id LIMIT 1").fetchone()[0]
            con.execute("UPDATE facts SET match_id='foreign' WHERE match_id=?", (old,))
            con.execute("UPDATE matches SET match_id='foreign' WHERE match_id=?", (old,))
        args[4] = dataclasses.replace(args[4], database_sha=tool.file_sha(args[0]))
    case("foreign-match-id", foreign_match)
    case("orphan-FK", lambda a: source_change(a, "INSERT INTO facts(match_id,value) VALUES('missing','orphan')"))
    def fail_transaction(stage, _):
        if stage == "after_table:matches":
            raise RuntimeError("INJECTED_PRECOMMIT_FAILURE")
    case("injected-transaction-failure-zero-append", hook=fail_transaction)
    def change_metadata(stage, con):
        if stage == "after_table:matches":
            con.execute("UPDATE schedule SET next='must-rollback'")
    case("metadata-mutation-rollback", hook=change_metadata)
    def fail_after_commit(stage, _):
        if stage == "after_commit":
            raise RuntimeError("INJECTED_POSTCOMMIT_FAILURE")
    case("postcommit-failure-never-claims-rollback", hook=fail_after_commit, committed=True)
    def race(stage, con):
        if stage == "before_begin":
            con.execute("INSERT INTO matches VALUES('concurrent',X'01')")
    case("transaction-readback-detects-concurrent-append", hook=race, target_count=1)
    def receipt_exists(args):
        args[3].mkdir()
    case("existing-owned-receipt-rejected", mutate=receipt_exists)
    # Real same-volume hard links: a plain, nonempty-path check is not isolation.
    for family in ("target", "source", "target-wal"):
        args = fixture(cache / ("forbidden-hardlink-" + family))
        formal_alias = args[2].with_name("forbidden-formal.sqlite")
        linked = args[2] if family.startswith("target") else args[0]
        os.link(linked, formal_alias)
        if family == "target-wal":
            # Keep the main target distinct; only its WAL aliases the forbidden DB.
            formal_alias.unlink()
            formal_alias.touch()
            os.link(formal_alias, Path(str(args[2]) + "-wal"))
        identity = (formal_alias.stat().st_dev, formal_alias.stat().st_ino)
        try:
            tool.append_fixture(*args, forbidden_ids={identity})
            raise AssertionError("formal hardlink accepted")
        except RuntimeError as error:
            assert str(error) == "FORMAL_DATABASE_ALIAS_REJECTED"
        assert not args[3].exists()  # Refused before receipt creation or backup writes.
        if family != "target-wal":
            assert counts(args[2]) == (0, 0)
        results.append({"name": "reject-real-os-link-" + family + "-before-backup", "passed": True})
    tree = ast.parse(MODULE.read_text(encoding="utf-8"))
    assert MODULE.read_text(encoding="utf-8").count('sys.argv[1:3] == ["--normal-apply", "--binding-sha"]') == 1
    results.append({"name": "normal-cli-is-one-explicit-parallel-mode", "passed": True})
    entry = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "main")
    alarms = sorted((node for node in ast.walk(entry) if isinstance(node, ast.Call)
                     and isinstance(node.func, ast.Attribute) and node.func.attr == "alarm"
                     and node.args and isinstance(node.args[0], ast.Constant) and node.args[0].value == 35),
                    key=lambda node: node.lineno)
    apply_calls = sorted((node for node in ast.walk(entry) if isinstance(node, ast.Call)
                          and isinstance(node.func, ast.Name) and node.func.id == "apply_fixed"),
                         key=lambda node: node.lineno)
    assert len(alarms) == len(apply_calls) == 2
    assert all(alarm.lineno < apply_call.lineno for alarm, apply_call in zip(alarms, apply_calls))
    results.append({"name": "35-second-CLI-deadline-covers-normal-and-existing-imports", "passed": True})
    result = {"schema": 1, "status": "passed", "cases": len(results), "results": results,
              "realSqlite": True, "networkCalls": 0, "liveDatabaseWrites": 0,
              "sources": {name: tool.file_sha(Path(__file__).with_name(name)) for name in
                          ("testrun-owned-analytics-import.py", "test-testrun-owned-analytics-import.py")},
              "boundary": "Offline injected SQLite dependency fixtures only; no Linux identity or live import performed."}
    tool.durable(output / "offline-tests.json", result)
    print(json.dumps({"status": "passed", "cases": len(results), "evidence": str(output), "networkCalls": 0}))


if __name__ == "__main__":
    main()
