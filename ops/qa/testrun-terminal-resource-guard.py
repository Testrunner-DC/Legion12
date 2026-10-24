"""Explicit diagnostic profile over the unchanged bounded resource supervisor.

This is not the normal capacity guard. Every outer row says diagnosticOnly and
the ready/completion kinds differ. The normal OFF validator remains untouched.
The supervisor's process isolation, caps, deadlines and reaping are reused by SHA.
"""
from __future__ import annotations
import hashlib
import json
import pathlib
import re
import stat
import sys
import types

sys.dont_write_bytecode = True
NORMAL_SHA = "8CE69E70F27B23F7FD2651764CD1ACFA14C8BCF84161E923DB482975219F8C07"


def selected_profile(value):
    if (not isinstance(value, dict) or set(value) != {"schema", "setting"}
            or type(value["schema"]) is not int or value["schema"] != 1
            or not isinstance(value["setting"], str)
            or not re.fullmatch(r"terminal-recipients-v4:30:[0-9a-f]{32}", value["setting"])):
        raise ValueError("EXACT_TERMINAL_RECIPIENT_PROFILE_REQUIRED")
    profile = {"baseUrl": "https://legion-12.com/testrun", "matchStorage": "ephemeral",
               "privateDeckObjectPersistence": "true", "diagnosticSetting": value["setting"]}
    digest = hashlib.sha256(json.dumps(profile, separators=(",", ":")).encode()).hexdigest().upper()
    return profile, digest


def unique_pairs(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("DUPLICATE_PROFILE_FIELD")
        value[key] = item
    return value


def validate_terminal_profile(normal, test_raw, formal_raw, expected_sha, profile, digest):
    if expected_sha != digest:
        raise ValueError("TERMINAL_PROFILE_BINDING_CHANGED")
    # Reuse the normal validator for every other test/formal startup field. Only
    # remove the already-exact checked diagnostic entry from this temporary byte
    # view; the actual process environment is never changed or called OFF.
    test, _ = normal.selected_environment(test_raw), normal.selected_environment(formal_raw)
    setting = test.get(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS")
    if setting != profile["diagnosticSetting"].encode():
        raise ValueError("TERMINAL_DIAGNOSTIC_SETTING_CHANGED")
    remaining = b"\0".join(entry for entry in test_raw.split(b"\0")
                            if not entry.startswith(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS="))
    # The imported module keeps its original normal PROFILE_SHA; don't mutate it.
    return normal.validate_normal_profile(remaining, formal_raw, normal.PROFILE_SHA)


def load_supervisor(entry):
    selected = entry.parent / "normal-guard.py"
    if not selected.is_file() or selected.is_symlink():
        raise ValueError("BOUND_NORMAL_SUPERVISOR_REQUIRED")
    source = selected.read_bytes()
    if len(source) > 131072 or hashlib.sha256(source).hexdigest().upper() != NORMAL_SHA:
        raise ValueError("BOUND_NORMAL_SUPERVISOR_CHANGED")
    module = types.ModuleType("l12_terminal_supervisor")
    module.__file__ = str(selected)
    exec(compile(source, str(selected), "exec"), module.__dict__)
    return module


def prepare_supervisor(entry, profile_value):
    profile, digest = selected_profile(profile_value)
    normal = load_supervisor(entry)
    normal_validator = normal.validate_normal_profile
    normal_sha = normal.PROFILE_SHA
    normal_profile = dict(normal.PROFILE)
    # Keep a separate immutable normal validation namespace. The process worker
    # profile hash must be diagnostic, but the normal validation contract is OFF.
    off = types.SimpleNamespace(selected_environment=normal.selected_environment,
                                validate_normal_profile=normal_validator, PROFILE_SHA=normal_sha)
    # That function reads its defining module globals. Preserve its OFF hash by
    # executing the same pinned source once in the dedicated normal namespace.
    off_module = load_supervisor(entry)
    off.validate_normal_profile = off_module.validate_normal_profile
    def config(legacy, values, *, enforce_host=True):
        if enforce_host:
            normal.assert_live_runtime(legacy)
        value = legacy.parse_config(["terminal-guard", *values], enforce_host=enforce_host)
        if value.test_pid == value.formal_pid or value.env_sha != digest:
            raise ValueError("TERMINAL_DISTINCT_ENVIRONMENT_BINDING_REQUIRED")
        if any(int(pid) > 9007199254740991 for pid in (value.test_pid, value.formal_pid)):
            raise ValueError("TERMINAL_PID_SAFE_INTEGER_REQUIRED")
        if not re.fullmatch(r"[0-9]+", values[-1]) or not 30 <= int(values[-1]) <= 930:
            raise ValueError("TERMINAL_SECONDS_30_TO_930_REQUIRED")
        return value
    def operations(legacy):
        class DiagnosticOperations(legacy.RealOperations):
            def nonce_identity(self, value):
                return validate_terminal_profile(off,
                    self.bounded_bytes(f"/proc/{value.test_pid}/environ"),
                    self.bounded_bytes(f"/proc/{value.formal_pid}/environ"), value.env_sha, profile, digest)
            def identity(self, value):
                super().identity(value)
                self.nonce_identity(value)
        return DiagnosticOperations
    emitter = normal.BoundedEmitter
    class DiagnosticEmitter(emitter):
        def emit(self, value):
            row = dict(value, diagnosticOnly=True, onlineCapacityAccepted=False)
            if row.get("kind") == "normal-guard-ready":
                row["kind"] = "terminal-recipient-guard-ready"
            elif row.get("kind") == "normal-guard-completion":
                row["kind"] = "terminal-recipient-guard-completion"
                row["diagnosticProfileSha256"] = row.pop("normalProfileSha256")
            return super().emit(row)
    normal.__file__ = str(entry)
    normal.PROFILE = profile
    normal.PROFILE_SHA = digest
    normal.normal_config = config
    normal.operations_type = operations
    normal.BoundedEmitter = DiagnosticEmitter
    return normal, off_module, normal_profile


def main():
    entry = pathlib.Path(__file__).resolve(strict=True)
    if sys.platform != "linux" or entry != pathlib.Path(__file__) or entry.is_symlink():
        raise ValueError("TERMINAL_GUARD_LINUX_PLAIN_ENTRY_REQUIRED")
    selected = entry.parent / "diagnostic-profile.json"
    info = selected.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size > 1024:
        raise ValueError("TERMINAL_PRIVATE_PROFILE_REQUIRED")
    value = json.loads(selected.read_bytes(), object_pairs_hook=unique_pairs)
    normal, _, _ = prepare_supervisor(entry, value)
    arguments = sys.argv[1:]
    if len(arguments) == 2 and arguments[0] == "--_round-worker":
        return normal.round_worker(arguments[1])
    if not arguments or arguments[0] != "--observe":
        raise ValueError("EXACT_TERMINAL_GUARD_MODE_REQUIRED")
    options, scenario, seconds = normal.parse_cli(arguments)
    if scenario is not None:
        raise ValueError("TERMINAL_GUARD_SELF_TEST_MODE_NOT_LIVE")
    legacy = normal.load_frozen_guard(options["--frozen-guard"])
    values = [options["--test-commit"], options["--formal-commit"], options["--test-pid"], options["--formal-pid"],
              options["--test-dll"], options["--formal-dll"], normal.PROFILE_SHA, "900"]
    config = normal.normal_config(legacy, values)
    return normal.supervise(legacy, config, options["--frozen-guard"], seconds=seconds)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception:
        print("TERMINAL_GUARD_REJECTED", file=sys.stderr)
        raise SystemExit(1)
