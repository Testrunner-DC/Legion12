"""Pure profile/config tests; no Linux service or network acceptance claim."""
import importlib.util
import json
import pathlib
import sys

spec = importlib.util.spec_from_file_location("terminal_guard", pathlib.Path(__file__).with_name("testrun-terminal-resource-guard.py"))
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
root = pathlib.Path(sys.argv[1])
assert root.is_absolute() and (root / "normal-guard.py").is_file()
value = {"schema": 1, "setting": "terminal-recipients-v4:30:" + "2" * 32}
profile, digest = guard.selected_profile(value)
diagnostic, normal, original_profile = guard.prepare_supervisor(root / "terminal-guard.py", value)
assert normal.PROFILE["diagnostics"] == "off" and diagnostic.PROFILE == profile
assert diagnostic.PROFILE_SHA == digest and normal.PROFILE_SHA != digest
assert normal.PROFILE == original_profile
cases = 4

def rejected(callback):
    global cases
    try:
        callback()
    except (ValueError, RuntimeError, SystemExit):
        cases += 1
        return
    raise AssertionError("Expected strict rejection")

for bad in (None, {}, {"schema": True, "setting": value["setting"]}, {**value, "extra": 1},
            {**value, "schema": 2}, {**value, "setting": "terminal-recipients-v4:1:" + "2" * 32},
            {**value, "setting": "terminal-recipients-v4:31:" + "2" * 32},
            {**value, "setting": "terminal-recipients-v4:30:" + "A" * 32},
            {**value, "setting": value["setting"] + ":extra"}):
    rejected(lambda bad=bad: guard.selected_profile(bad))
rejected(lambda: json.loads('{"schema":1,"schema":1}', object_pairs_hook=guard.unique_pairs))
test_off = b"L12_PUBLIC_BASE_URL=https://legion-12.com/testrun/\0L12_TESTRUN_MATCH_STORAGE=ephemeral\0L12_PRIVATE_DECK_OBJECT_PERSISTENCE=true\0"
test = test_off + b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS=" + value["setting"].encode() + b"\0"
formal = b"L12_PUBLIC_BASE_URL=https://legion-12.com\0"
assert normal.validate_normal_profile(test_off, formal, normal.PROFILE_SHA)
assert guard.validate_terminal_profile(normal, test, formal, digest, profile, digest)
cases += 2
rejected(lambda: normal.validate_normal_profile(test, formal, normal.PROFILE_SHA))
for bad_test, bad_formal, sha in (
        (test_off, formal, digest), (test.replace(b"2222", b"3333"), formal, digest),
        (test.replace(b"ephemeral", b"persistent"), formal, digest),
        (test.replace(b"=true", b"=false"), formal, digest),
        (test.replace(b"/testrun/", b"/wrong/"), formal, digest),
        (test + b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS=" + value["setting"].encode() + b"\0", formal, digest),
        (test, formal + b"L12_TESTRUN_MATCH_STORAGE=ephemeral\0", digest),
        (test, formal + b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS=unexpected\0", digest),
        (test, formal.replace(b".com", b".com/testrun"), digest),
        (test, formal, "F" * 64)):
    rejected(lambda a=bad_test, b=bad_formal, s=sha: guard.validate_terminal_profile(normal, a, b, s, profile, digest))
legacy = normal.load_frozen_guard()
values = ["a" * 40, "c" * 40, "101", "102", "B" * 64, "D" * 64, digest, "900"]
assert diagnostic.normal_config(legacy, values, enforce_host=False).env_sha == digest
cases += 1
for index, replacement in ((3, "101"), (6, normal.PROFILE_SHA), (7, "931"), (7, "29"),
                            (7, "900.0"), (2, "9007199254740992")):
    bad = list(values); bad[index] = replacement
    rejected(lambda bad=bad: diagnostic.normal_config(legacy, bad, enforce_host=False))
for name in ("supervise", "round_worker", "validate_row", "validate_state", "parse_worker_output"):
    assert getattr(diagnostic, name).__code__ == getattr(normal, name).__code__
    cases += 1
assert diagnostic.CapturedChild.reap.__code__ == normal.CapturedChild.reap.__code__
cases += 1
print(json.dumps({"status": "passed", "cases": cases, "normalOffStillStrict": True,
                  "exactDiagnosticProfile": True, "supervisorCapsDeadlinesAndReapingCodeUnchanged": True,
                  "networkCalls": 0, "serviceWrites": 0, "linuxRuntimeClaim": False}))
