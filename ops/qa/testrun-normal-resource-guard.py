"""Deadline-bounded read-only supervisor for the normal isolated test service.

The frozen guard remains unchanged. Its runner, deadlines, resource thresholds,
service/commit/DLL checks and row shape are reused by SHA, not reimplemented.
Only its diagnostic-nonce identity hook is replaced by the normal startup profile.
Each round runs in an owned process group; a blocked frozen worker cannot keep
the supervisor alive after its round deadline, total deadline, or SIGTERM.
No SSH, configuration writes, authentication, service starts or stops exist here.
"""
from __future__ import annotations

import concurrent.futures
import contextlib
import hashlib
import json
import math
import os
import pathlib
import re
import signal
import socket
import subprocess
import sys
import threading
import time
import types

sys.dont_write_bytecode = True
LEGACY_PATH = "D:/GPT/Legion12/artifacts/performance-measurement-recovery-20261020/guard-deadline-r4/resource-guard-deadline-r4.py"
LEGACY_SHA = "549BC59A31A3EDF836A045C5F915C9DB2E283B070150ABC7E369A01BBC8A2C1F"
PROFILE = {"baseUrl": "https://legion-12.com/testrun", "matchStorage": "ephemeral",
           "privateDeckObjectPersistence": "true", "diagnostics": "off"}
PROFILE_SHA = hashlib.sha256(json.dumps(PROFILE, separators=(",", ":")).encode()).hexdigest().upper()
MAX_CHILD_STDOUT = 8192
MAX_CHILD_STDERR = 65536
MAX_LINE_BYTES = 4096
MAX_STDOUT_BYTES = 4 * 1024 * 1024
MAX_STDOUT_LINES = 1024
MAX_INPUT_BYTES = 8192
REAP_SECONDS = 0.50
SELF_TEST_SCENARIOS = {"healthy", "memory", "bad-profile", "hang", "noise", "extra", "flood", "stderr", "bad-state"}


class NormalProfileError(RuntimeError, ValueError):
    pass


def load_frozen_guard(selected_path=LEGACY_PATH):
    selected = pathlib.Path(selected_path)
    if not selected.is_absolute() or selected.is_symlink() or not selected.is_file():
        raise ValueError("FROZEN_GUARD_PLAIN_ABSOLUTE_PATH_REQUIRED")
    with selected.open("rb") as stream:
        source = stream.read(65537)
    if len(source) > 65536 or hashlib.sha256(source).hexdigest().upper() != LEGACY_SHA:
        raise ValueError("FROZEN_GUARD_SHA_CHANGED")
    module = types.ModuleType("l12_frozen_deadline_guard")
    exec(compile(source, str(selected), "exec"), module.__dict__)
    return module


def selected_environment(raw: bytes) -> dict[bytes, bytes]:
    if len(raw) > 65536:
        raise NormalProfileError("NORMAL_PROFILE_ENV_BOUND")
    allowed = {b"L12_PUBLIC_BASE_URL", b"L12_TESTRUN_MATCH_STORAGE",
               b"L12_PRIVATE_DECK_OBJECT_PERSISTENCE", b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS"}
    selected = {}
    for entry in raw.split(b"\0"):
        name, separator, value = entry.partition(b"=")
        if separator and name in allowed:
            if name in selected:
                raise NormalProfileError("NORMAL_PROFILE_DUPLICATE_ENV")
            selected[name] = value
    return selected


def validate_normal_profile(test_raw: bytes, formal_raw: bytes, expected_sha: str):
    if expected_sha != PROFILE_SHA:
        raise NormalProfileError("NORMAL_PROFILE_BINDING_CHANGED")
    test, formal = selected_environment(test_raw), selected_environment(formal_raw)
    if test.get(b"L12_PUBLIC_BASE_URL", b"").rstrip(b"/") != b"https://legion-12.com/testrun":
        raise NormalProfileError("NORMAL_TEST_BASE_CHANGED")
    if test.get(b"L12_TESTRUN_MATCH_STORAGE") != b"ephemeral":
        raise NormalProfileError("NORMAL_TEST_EPHEMERAL_REQUIRED")
    if test.get(b"L12_PRIVATE_DECK_OBJECT_PERSISTENCE") != b"true":
        raise NormalProfileError("NORMAL_TEST_F2_OBJECT_REQUIRED")
    if test.get(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS", b""):
        raise NormalProfileError("NORMAL_TEST_DIAGNOSTIC_MUST_BE_OFF")
    # Normal formal deployments may omit the optional public-base override.
    # A configured override is still bound to the exact production HTTPS root.
    formal_base = formal.get(b"L12_PUBLIC_BASE_URL", b"")
    if formal_base not in (b"", b"https://legion-12.com", b"https://legion-12.com/"):
        raise NormalProfileError("NORMAL_FORMAL_BASE_CHANGED")
    if formal.get(b"L12_TESTRUN_MATCH_STORAGE", b"") or formal.get(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS", b""):
        raise NormalProfileError("NORMAL_FORMAL_PROFILE_CHANGED")
    return True


def assert_live_runtime(legacy, *, platform=None, hostname=None, uid=None):
    if (platform if platform is not None else sys.platform) != "linux":
        raise ValueError("NORMAL_GUARD_LINUX_REQUIRED")
    actual_uid = uid if uid is not None else os.geteuid() if hasattr(os, "geteuid") else -1
    if actual_uid != 0:
        raise ValueError("NORMAL_GUARD_ROOT_READ_REQUIRED")
    if (hostname if hostname is not None else socket.gethostname()) != legacy.EXPECTED_HOST:
        raise ValueError("NORMAL_GUARD_HOST_REJECTED")


def normal_config(legacy, values, *, enforce_host=True):
    if enforce_host:
        assert_live_runtime(legacy)
    config = legacy.parse_config(["normal-guard", *values], enforce_host=enforce_host)
    if config.test_pid == config.formal_pid or config.env_sha != PROFILE_SHA:
        raise ValueError("NORMAL_DISTINCT_ENVIRONMENT_BINDING_REQUIRED")
    if any(int(value) > 9007199254740991 for value in (config.test_pid, config.formal_pid)):
        raise ValueError("NORMAL_PID_SAFE_INTEGER_REQUIRED")
    if not re.fullmatch(r"[0-9]+", values[-1]) or not 30 <= int(values[-1]) <= 930:
        raise ValueError("NORMAL_SECONDS_30_TO_930_REQUIRED")
    return config


def operations_type(legacy):
    class NormalOperations(legacy.RealOperations):
        def nonce_identity(self, config):
            # Method name is the frozen runner's narrow injection point; no nonce
            # or owned diagnostic environment is installed or pretended here.
            return validate_normal_profile(
                self.bounded_bytes(f"/proc/{config.test_pid}/environ"),
                self.bounded_bytes(f"/proc/{config.formal_pid}/environ"), config.env_sha)

        def identity(self, config):
            super().identity(config)
            self.nonce_identity(config)

    return NormalOperations


def unique_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("DUPLICATE_JSON_FIELD")
        value[key] = item
    return value


def read_json(raw):
    def invalid_constant(_value):
        raise ValueError("NONFINITE_JSON_NUMBER")
    return json.loads(raw, object_pairs_hook=unique_object, parse_constant=invalid_constant)


def safe_reason(error, fallback="NORMAL_GUARD_FAILURE"):
    reason = str(error)
    return reason if re.fullmatch(r"[A-Za-z][A-Za-z0-9_]{0,99}", reason) else fallback


def stop_row(reason, phase="budget", *, elapsed_ms=0):
    return {"schema": 1, "observedUtcMs": int(time.time() * 1000), "readOnly": True,
            "stop": True, "reason": reason, "phase": phase, "roundElapsedMs": elapsed_ms}


class BoundedEmitter:
    def __init__(self, memory_only=False):
        self.bytes = 0
        self.lines = 0
        self.memory_only = memory_only
        self.descriptor = sys.stdout.fileno()
        if sys.platform == "linux":
            os.set_blocking(self.descriptor, False)

    def emit(self, value):
        raw = (json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n").encode()
        if len(raw) > MAX_LINE_BYTES or self.bytes + len(raw) > MAX_STDOUT_BYTES or self.lines >= MAX_STDOUT_LINES:
            raise RuntimeError("NORMAL_GUARD_OUTPUT_BOUND")
        deadline = time.monotonic() + 0.20
        offset = 0
        while offset < len(raw):
            try:
                written = os.write(self.descriptor, raw[offset:])
                if written <= 0:
                    raise RuntimeError("NORMAL_GUARD_OUTPUT_CLOSED")
                offset += written
            except BlockingIOError:
                if time.monotonic() >= deadline:
                    raise RuntimeError("NORMAL_GUARD_OUTPUT_BACKPRESSURE")
                time.sleep(0.005)
            except (BrokenPipeError, OSError) as error:
                raise RuntimeError("NORMAL_GUARD_OUTPUT_CLOSED") from error
        self.bytes += len(raw)
        self.lines += 1


def validate_state(value):
    if not isinstance(value, dict) or set(value) != {"base_oom", "base_restart", "previous_cpu", "formal_failures"}:
        raise ValueError("WORKER_STATE_INVALID")
    for name in ("base_oom", "base_restart"):
        if value[name] is not None and (type(value[name]) is not int or value[name] < 0):
            raise ValueError("WORKER_STATE_INVALID")
    previous = value["previous_cpu"]
    if previous is not None and (not isinstance(previous, list) or len(previous) != 2
            or type(previous[0]) is not int or previous[0] < 0
            or type(previous[1]) not in (int, float) or not math.isfinite(previous[1]) or previous[1] < 0):
        raise ValueError("WORKER_STATE_INVALID")
    if type(value["formal_failures"]) is not int or not 0 <= value["formal_failures"] <= 2:
        raise ValueError("WORKER_STATE_INVALID")
    return value


def validate_row(row, config):
    allowed = {"schema", "observedUtcMs", "readOnly", "stop", "testMemoryBytes", "hostAvailableBytes", "oomDelta",
               "restartDelta", "testPid", "formalPid", "testCpuUsageUsec", "testCpuPercentOfOneCore", "testHealthMs",
               "formalSafetyHealthMs", "reason", "phase", "roundElapsedMs"}
    if not isinstance(row, dict) or set(row) - allowed:
        raise ValueError("WORKER_ROW_INVALID")
    for name in ("schema", "observedUtcMs", "readOnly", "stop", "phase", "roundElapsedMs"):
        if name not in row:
            raise ValueError("WORKER_ROW_INVALID")
    if row["schema"] != 1 or type(row["readOnly"]) is not bool or not row["readOnly"] or type(row["stop"]) is not bool:
        raise ValueError("WORKER_ROW_INVALID")
    if type(row["observedUtcMs"]) is not int or row["observedUtcMs"] < 0 or row["phase"] not in {
            "complete", "service", "resource", "health", "identity", "budget"}:
        raise ValueError("WORKER_ROW_INVALID")
    elapsed = row["roundElapsedMs"]
    if type(elapsed) not in (int, float) or not math.isfinite(elapsed) or elapsed < 0:
        raise ValueError("WORKER_ROW_INVALID")
    if row["stop"]:
        if row["phase"] == "complete" or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]{0,99}", row.get("reason", "")):
            raise ValueError("WORKER_ROW_INVALID")
    else:
        if row["phase"] != "complete" or elapsed >= 3750 or "reason" in row:
            raise ValueError("WORKER_ROW_INVALID")
        for name in ("testMemoryBytes", "hostAvailableBytes", "oomDelta", "restartDelta", "testPid", "formalPid", "testCpuUsageUsec"):
            if type(row.get(name)) is not int or row[name] < 0:
                raise ValueError("WORKER_ROW_INVALID")
        if row["testMemoryBytes"] >= 850 * 1024 * 1024 or row["hostAvailableBytes"] < 512 * 1024 * 1024:
            raise ValueError("WORKER_RESOURCE_UNSAFE")
        if row["oomDelta"] != 0 or row["restartDelta"] != 0:
            raise ValueError("WORKER_RESOURCE_UNSAFE")
    if row.get("testPid", int(config.test_pid)) != int(config.test_pid) or row.get("formalPid", int(config.formal_pid)) != int(config.formal_pid):
        raise ValueError("WORKER_PID_CHANGED")
    return row


def initial_state():
    return {"base_oom": None, "base_restart": None, "previous_cpu": None, "formal_failures": 0}


def parse_process_start_ticks(raw, expected_pid):
    if len(raw) > 4096 or not raw.startswith(str(expected_pid) + " (") or ")" not in raw:
        raise ValueError("SUPERVISOR_STAT_IDENTITY_REJECTED")
    fields = raw.rsplit(")", 1)[1].split()
    if len(fields) < 20 or not re.fullmatch(r"[0-9]+", fields[19]) or int(fields[19]) < 1:
        raise ValueError("SUPERVISOR_START_TICKS_REJECTED")
    return fields[19]


def supervisor_identity(config, memory_only):
    selected_pid = os.getpid()
    start_ticks = None
    boot_id = None
    if sys.platform == "linux":
        with pathlib.Path("/proc/self/stat").open(encoding="utf-8") as stream:
            start_ticks = parse_process_start_ticks(stream.read(4097), selected_pid)
        with pathlib.Path("/proc/sys/kernel/random/boot_id").open(encoding="ascii") as stream:
            boot_id = stream.read(65).strip()
        if not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", boot_id):
            raise ValueError("SUPERVISOR_BOOT_ID_REJECTED")
    elif not memory_only:
        raise ValueError("NORMAL_GUARD_LINUX_REQUIRED")
    return {"supervisorPid": selected_pid, "processStartTicks": start_ticks, "bootId": boot_id,
            "hostname": socket.gethostname(), "sourceSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest().upper(),
            "testPid": int(config.test_pid), "testCommit": config.expected_test, "testDllSha256": config.test_dll,
            "formalPid": int(config.formal_pid), "formalCommit": config.expected_formal, "formalDllSha256": config.formal_dll,
            "profileSha256": PROFILE_SHA}


def validate_completion_identity(ready, completion):
    names = {"supervisorPid", "processStartTicks", "bootId", "hostname", "sourceSha256", "testPid", "testCommit",
             "testDllSha256", "formalPid", "formalCommit", "formalDllSha256", "profileSha256"}
    if any(ready.get(name) != completion.get(name) for name in names):
        raise ValueError("SUPERVISOR_COMPLETION_IDENTITY_CHANGED")
    if ready.get("selfTestOnly") != completion.get("selfTestOnly"):
        raise ValueError("SUPERVISOR_COMPLETION_MODE_CHANGED")
    return True


def memory_operations(legacy, scenario):
    class MemoryOperations(operations_type(legacy)):
        def bounded_bytes(self, name):
            if "/101/" not in name:
                return b"L12_PUBLIC_BASE_URL=https://legion-12.com\0"
            setting = b"false" if scenario == "bad-profile" else b"true"
            return (b"L12_PUBLIC_BASE_URL=https://legion-12.com/testrun\0L12_TESTRUN_MATCH_STORAGE=ephemeral\0"
                    b"L12_PRIVATE_DECK_OBJECT_PERSISTENCE=" + setting + b"\0")
        def service(self, name):
            return {"MainPID": "101" if name == legacy.TEST_UNIT else "102", "NRestarts": "0",
                    "ActiveState": "active", "ControlGroup": "/system.slice/memory-fixture"}
        def health(self, _port):
            return {"status": "ok", "serverVersion": "a" * 40, "maintenance": False}, 1.0
        def identity(self, selected):
            self.nonce_identity(selected)
        def resources(self, _group):
            return {"memory": (850 if scenario == "memory" else 200) * 1024 * 1024,
                    "available": 1024 * 1024 * 1024, "oom": 0, "usage": 100}
    return MemoryOperations()


def read_worker_input():
    captured = bytearray()
    finished = threading.Event()
    failed = []
    descriptor = sys.stdin.fileno()
    def receive():
        try:
            while len(captured) <= MAX_INPUT_BYTES:
                block = os.read(descriptor, min(1024, MAX_INPUT_BYTES + 1 - len(captured)))
                if not block:
                    break
                captured.extend(block)
        except OSError:
            failed.append("WORKER_INPUT_READ_FAILED")
        finally:
            finished.set()
    # os.read, not a buffered sys.stdin read, avoids interpreter buffered-lock
    # shutdown hangs if a direct/malformed internal invocation never sends EOF.
    threading.Thread(target=receive, daemon=True).start()
    if not finished.wait(1.0):
        raise ValueError("WORKER_INPUT_DEADLINE_1S")
    if failed:
        raise ValueError(failed[0])
    if len(captured) > MAX_INPUT_BYTES:
        raise ValueError("WORKER_INPUT_BOUND")
    return bytes(captured)


def round_worker(selected_path, memory_scenario=None):
    # Internal protocol is bounded, has no credentials, and never changes real mode
    # into a self-test: a live supervisor rejects any selfTestOnly=true envelope.
    try:
        raw = read_worker_input()
        payload = read_json(raw)
        if set(payload) != {"values", "round", "state"} or type(payload["round"]) is not int or not 0 <= payload["round"] <= 500:
            raise ValueError("WORKER_INPUT_INVALID")
        legacy = load_frozen_guard(selected_path)
        config = normal_config(legacy, payload["values"], enforce_host=memory_scenario is None)
        state = validate_state(payload["state"])
        if memory_scenario is not None and payload["values"][:6] != ["a" * 40, "a" * 40, "101", "102", "B" * 64, "B" * 64]:
            raise ValueError("SELF_TEST_FIXED_IDENTITIES_REQUIRED")
        if memory_scenario == "hang":
            time.sleep(6)
        if memory_scenario == "noise":
            os.write(sys.stdout.fileno(), b"not-json\n"); return 0
        if memory_scenario == "flood":
            for _ in range(1024):
                os.write(sys.stdout.fileno(), b"x" * 8192)
            return 0
        if memory_scenario == "stderr":
            os.write(sys.stderr.fileno(), b"private-fixture-must-not-be-forwarded\n")
        operations = operations_type(legacy)() if memory_scenario is None else memory_operations(legacy, memory_scenario)
        # Validate the effective exec environment every round, not just the first
        # diagnostic hook. Protected F2 API writable/object readback stays with caller.
        operations.nonce_identity(config)
        kwargs = {} if memory_scenario is None else {"deadline_factory": lambda _seconds: contextlib.nullcontext()}
        runner = legacy.GuardRunner(config, operations, **kwargs)
        for name, value in state.items():
            setattr(runner, name, tuple(value) if name == "previous_cpu" and value is not None else value)
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=legacy.MAX_PARALLEL_PROBES)
        try:
            row = runner.collect(payload["round"], executor)
        finally:
            executor.shutdown(wait=False, cancel_futures=True)
        state = {name: getattr(runner, name) for name in initial_state()}
        if memory_scenario == "bad-state":
            state["formal_failures"] = -1
        envelope = {"schema": 1, "selfTestOnly": memory_scenario is not None, "row": row, "state": state,
                    "frozenGuardSha256": LEGACY_SHA, "normalProfileSha256": PROFILE_SHA}
        encoded = (json.dumps(envelope, separators=(",", ":"), allow_nan=False) + "\n").encode()
        os.write(sys.stdout.fileno(), encoded)
        if memory_scenario == "extra":
            os.write(sys.stdout.fileno(), b"{}\n")
        return 1 if row["stop"] else 0
    except Exception as error:
        envelope = {"schema": 1, "selfTestOnly": memory_scenario is not None,
                    "row": stop_row(safe_reason(error, "WORKER_INPUT_INVALID"), "identity"), "state": initial_state(),
                    "frozenGuardSha256": LEGACY_SHA, "normalProfileSha256": PROFILE_SHA}
        os.write(sys.stdout.fileno(), (json.dumps(envelope, separators=(",", ":")) + "\n").encode())
        return 1


class CapturedChild:
    def __init__(self, process):
        self.process = process
        self.stdout = bytearray()
        self.stderr = bytearray()
        self.bound = threading.Event()
        self.bound_reason = None
        self.threads = []
        for stream, target, cap, name in [(process.stdout, self.stdout, MAX_CHILD_STDOUT, "WORKER_STDOUT_BOUND"),
                                          (process.stderr, self.stderr, MAX_CHILD_STDERR, "WORKER_STDERR_BOUND")]:
            def consume(selected=stream, destination=target, maximum=cap, reason=name):
                try:
                    while True:
                        block = selected.read(1024)
                        if not block:
                            break
                        remaining = max(0, maximum - len(destination))
                        destination.extend(block[:remaining])
                        if len(block) > remaining:
                            self.bound_reason = reason
                            self.bound.set()
                except (OSError, ValueError):
                    self.bound_reason = "WORKER_PIPE_READ_FAILED"
                    self.bound.set()
            thread = threading.Thread(target=consume, daemon=True)
            thread.start(); self.threads.append(thread)

    def reap(self):
        process = self.process
        finish_by = time.monotonic() + REAP_SECONDS
        if process.poll() is None:
            if sys.platform == "linux":
                try:
                    if os.getpgid(process.pid) != process.pid:
                        raise RuntimeError("OWNED_CHILD_PROCESS_GROUP_CHANGED")
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
            else:
                process.terminate()
            try:
                process.wait(timeout=0.15)
            except subprocess.TimeoutExpired:
                if sys.platform == "linux":
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                else:
                    process.kill()
                process.wait(timeout=max(0.001, finish_by - time.monotonic()))
        # Also retire subprocesses that inherited the worker's owned group even
        # when its leader exited after an exceptional frozen thread shutdown.
        if sys.platform == "linux":
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        for thread in self.threads:
            thread.join(timeout=max(0, finish_by - time.monotonic()))
        if not any(thread.is_alive() for thread in self.threads):
            for stream in (process.stdout, process.stderr):
                stream.close()
        return process.poll() is not None and not any(thread.is_alive() for thread in self.threads)


def parse_worker_output(raw, memory_only, config):
    if len(raw.splitlines()) != 1 or len(raw) > MAX_LINE_BYTES:
        raise ValueError("WORKER_OUTPUT_INVALID")
    value = read_json(raw)
    if not isinstance(value, dict) or set(value) != {"schema", "selfTestOnly", "row", "state", "frozenGuardSha256", "normalProfileSha256"}:
        raise ValueError("WORKER_OUTPUT_INVALID")
    if value["schema"] != 1 or type(value["selfTestOnly"]) is not bool or value["selfTestOnly"] != memory_only:
        raise ValueError("WORKER_MODE_CHANGED")
    if value["frozenGuardSha256"] != LEGACY_SHA or value["normalProfileSha256"] != PROFILE_SHA:
        raise ValueError("WORKER_SOURCE_CHANGED")
    return validate_row(value["row"], config), validate_state(value["state"])


def supervise(legacy, config, selected_path, *, seconds, memory_scenario=None, signal_after_ms=None):
    memory_only = memory_scenario is not None
    try:
        emitter = BoundedEmitter(memory_only)
    except (OSError, ValueError):
        return 74
    stopped = threading.Event()
    observed_signal = {"reason": None}
    prior_handlers = {}
    for name in ("SIGTERM", "SIGINT"):
        number = getattr(signal, name)
        prior_handlers[number] = signal.getsignal(number)
        def requested(_number, _frame, reason=name + "_REQUESTED"):
            observed_signal["reason"] = reason; stopped.set()
        signal.signal(number, requested)
    started = time.monotonic(); deadline = started + seconds
    state = initial_state(); rounds = 0; all_reaped = True; active = None
    reason = None; exit_code = 1; stderr_bytes = 0
    identity = {}
    values = [config.expected_test, config.expected_formal, config.test_pid, config.formal_pid,
              config.test_dll, config.formal_dll, PROFILE_SHA, "900"]
    try:
        identity = supervisor_identity(config, memory_only)
        emitter.emit({"schema": 2, "kind": "normal-guard-memory-ready" if memory_only else "normal-guard-ready",
                      "selfTestOnly": memory_only, "readOnly": True, "frozenGuardSha256": LEGACY_SHA, **identity})
        while reason is None:
            round_started = time.monotonic()
            if stopped.is_set():
                reason = observed_signal["reason"]; exit_code = 143 if reason == "SIGTERM_REQUESTED" else 130; break
            if round_started >= deadline:
                reason = "TOTAL_DEADLINE_REACHED"; exit_code = 2; break
            arguments = [sys.executable, "-I", "-B", str(pathlib.Path(__file__).resolve()), "--_round-worker", selected_path]
            if memory_only:
                arguments += ["--_memory-scenario", memory_scenario]
            process = subprocess.Popen(arguments, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                       start_new_session=sys.platform == "linux")
            active = CapturedChild(process)
            payload = json.dumps({"values": values, "round": rounds, "state": state}, separators=(",", ":")).encode()
            process.stdin.write(payload); process.stdin.close()
            round_deadline = min(deadline, round_started + legacy.ROUND_BUDGET_SECONDS)
            while process.poll() is None:
                now = time.monotonic()
                if memory_only and signal_after_ms is not None and (now - started) * 1000 >= signal_after_ms:
                    signal_after_ms = None
                    signal.raise_signal(signal.SIGTERM)
                if stopped.is_set():
                    reason = observed_signal["reason"]; exit_code = 143 if reason == "SIGTERM_REQUESTED" else 130; break
                if active.bound.is_set():
                    reason = active.bound_reason; break
                if now >= round_deadline:
                    reason = "TOTAL_DEADLINE_REACHED" if now >= deadline else "ROUND_PROCESS_DEADLINE_3750MS"
                    exit_code = 2 if reason == "TOTAL_DEADLINE_REACHED" else 1
                    break
                stopped.wait(min(0.01, max(0, round_deadline - now)))
            reaped = active.reap(); all_reaped = all_reaped and reaped
            stderr_bytes += len(active.stderr)
            round_elapsed_ms = (time.monotonic() - round_started) * 1000
            if reason is None and time.monotonic() >= deadline:
                reason = "TOTAL_DEADLINE_REACHED"; exit_code = 2
            if reason is None and round_elapsed_ms >= 3750:
                reason = "ROUND_PROCESS_DEADLINE_3750MS"
            if reason is None and not reaped:
                reason = "WORKER_REAP_INCOMPLETE"
            if reason is None and active.bound.is_set():
                reason = active.bound_reason
            if reason is None and active.stderr:
                reason = "WORKER_STDERR_NOT_EMPTY"
            if reason is not None:
                active = None; break
            try:
                row, state = parse_worker_output(active.stdout, memory_only, config)
                if process.returncode != (1 if row["stop"] else 0):
                    raise ValueError("WORKER_EXIT_MISMATCH")
            except Exception as error:
                reason = safe_reason(error, "WORKER_OUTPUT_INVALID"); active = None; break
            active = None
            # Include subprocess start, normal-profile checks and owned-pipe reap
            # in the observed round time; do not report only frozen collect time.
            emitted = dict(row, roundElapsedMs=round(round_elapsed_ms, 2), selfTestOnly=memory_only, sampleIndex=rounds)
            emitter.emit(emitted); rounds += 1
            if row["stop"]:
                reason = row["reason"]; break
            next_round = round_started + (0.05 if memory_only else legacy.ROUND_INTERVAL_SECONDS)
            while time.monotonic() < min(next_round, deadline) and not stopped.is_set():
                if memory_only and signal_after_ms is not None and (time.monotonic() - started) * 1000 >= signal_after_ms:
                    signal_after_ms = None; signal.raise_signal(signal.SIGTERM)
                stopped.wait(min(0.01, max(0, min(next_round, deadline) - time.monotonic())))
    except Exception as error:
        reason = safe_reason(error, "SUPERVISOR_EXCEPTION")
    finally:
        if active is not None:
            try:
                all_reaped = active.reap() and all_reaped
            except Exception:
                all_reaped = False
        for number, handler in prior_handlers.items():
            signal.signal(number, handler)
    if not all_reaped:
        reason = "WORKER_REAP_INCOMPLETE"; exit_code = 1
    summary = {"schema": 2, "kind": "normal-guard-completion", "status": "stopped", "reason": reason,
               "selfTestOnly": memory_only, "readOnly": True, "samples": rounds, "stopRequested": True,
               "allOwnedChildrenReaped": all_reaped, "elapsedMs": round((time.monotonic() - started) * 1000, 2),
               "totalDeadlineMs": seconds * 1000, "roundDeadlineMs": 3750, "stdoutBytesBeforeCompletion": emitter.bytes,
               "childReapBoundMs": REAP_SECONDS * 1000,
               "stderrBytesObserved": stderr_bytes, "formalWrites": 0, "serviceWrites": 0,
               "frozenGuardSha256": LEGACY_SHA, "normalProfileSha256": PROFILE_SHA,
               "effectiveF2ApiReadbackRequiredFromCaller": True, **identity}
    try:
        emitter.emit(summary)
    except Exception:
        return 74
    return exit_code


def parse_cli(arguments):
    if not arguments or arguments[0] not in ("--observe", "--self-test-scenario"):
        raise ValueError("EXACT_NORMAL_GUARD_MODE_REQUIRED")
    memory = arguments[0] == "--self-test-scenario"
    selected = list(arguments[1:]); scenario = selected.pop(0) if memory and selected else None
    if memory and scenario not in SELF_TEST_SCENARIOS:
        raise ValueError("EXACT_SELF_TEST_SCENARIO_REQUIRED")
    allowed = {"--frozen-guard", "--test-commit", "--formal-commit", "--test-pid", "--formal-pid",
               "--test-dll", "--formal-dll", "--seconds"}
    if memory:
        allowed.add("--self-test-signal-after-ms")
    options = {}
    while selected:
        key = selected.pop(0)
        if key not in allowed or key in options or not selected:
            raise ValueError("NORMAL_GUARD_ARGUMENT_REJECTED")
        options[key] = selected.pop(0)
    required = allowed - {"--self-test-signal-after-ms"} if memory else allowed
    if set(options) - allowed or required - set(options):
        raise ValueError("NORMAL_GUARD_ARGUMENT_REQUIRED")
    if not pathlib.Path(options["--frozen-guard"]).is_absolute():
        raise ValueError("FROZEN_GUARD_ABSOLUTE_PATH_REQUIRED")
    seconds_raw = options["--seconds"]
    if memory:
        if not re.fullmatch(r"(?:[0-9]+)(?:\.[0-9]+)?", seconds_raw) or not 0.10 <= float(seconds_raw) <= 30:
            raise ValueError("SELF_TEST_SECONDS_BOUND")
        seconds = float(seconds_raw)
    else:
        if not re.fullmatch(r"[0-9]+", seconds_raw) or not 30 <= int(seconds_raw) <= 930:
            raise ValueError("NORMAL_SECONDS_30_TO_930_REQUIRED")
        seconds = int(seconds_raw)
    if memory and "--self-test-signal-after-ms" in options:
        delay = options["--self-test-signal-after-ms"]
        if not re.fullmatch(r"[0-9]+", delay) or not 1 <= int(delay) <= 5000:
            raise ValueError("SELF_TEST_SIGNAL_BOUND")
    return options, scenario, seconds


def cli(arguments):
    try:
        options, scenario, seconds = parse_cli(arguments)
        legacy = load_frozen_guard(options["--frozen-guard"])
        values = [options["--test-commit"], options["--formal-commit"], options["--test-pid"], options["--formal-pid"],
                  options["--test-dll"], options["--formal-dll"], PROFILE_SHA, "900"]
        config = normal_config(legacy, values, enforce_host=scenario is None)
        if scenario is not None and values[:6] != ["a" * 40, "a" * 40, "101", "102", "B" * 64, "B" * 64]:
            raise ValueError("SELF_TEST_FIXED_IDENTITIES_REQUIRED")
    except (Exception, SystemExit) as error:
        print(json.dumps({"schema": 2, "kind": "normal-guard-admission", "status": "rejected",
                          "reason": safe_reason(error, "NORMAL_GUARD_ARGUMENT_REJECTED"), "childProcessesStarted": 0,
                          "serviceWrites": 0, "formalWrites": 0}, separators=(",", ":")))
        return 64
    return supervise(legacy, config, options["--frozen-guard"], seconds=seconds, memory_scenario=scenario,
                     signal_after_ms=int(options["--self-test-signal-after-ms"]) if "--self-test-signal-after-ms" in options else None)


def self_test(selected_path=LEGACY_PATH):
    legacy = load_frozen_guard(selected_path)
    assert (legacy.ROUND_INTERVAL_SECONDS, legacy.ROUND_BUDGET_SECONDS, legacy.PROBE_BUDGET_SECONDS,
            legacy.HEALTH_TIMEOUT_SECONDS, legacy.MAX_PARALLEL_PROBES) == (2.0, 3.75, 3.2, 2.0, 6)
    good_test = (b"L12_PUBLIC_BASE_URL=https://legion-12.com/testrun\0L12_TESTRUN_MATCH_STORAGE=ephemeral\0"
                 b"L12_PRIVATE_DECK_OBJECT_PERSISTENCE=true\0")
    good_formal = b"L12_PUBLIC_BASE_URL=https://legion-12.com\0"
    cases = 1
    assert validate_normal_profile(good_test, good_formal, PROFILE_SHA)
    cases += 1
    for formal_raw in (b"", b"L12_PUBLIC_BASE_URL=\0", b"L12_PUBLIC_BASE_URL=https://legion-12.com/\0"):
        assert validate_normal_profile(good_test, formal_raw, PROFILE_SHA)
        cases += 1
    for test_raw, formal_raw, sha in [
        (good_test.replace(b"/testrun", b""), good_formal, PROFILE_SHA),
        (good_test.replace(b"ephemeral", b"persistent"), good_formal, PROFILE_SHA),
        (good_test.replace(b"=true", b"=false"), good_formal, PROFILE_SHA),
        (good_test + b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS=terminal-recorder-v3:6:bad\0", good_formal, PROFILE_SHA),
        (good_test + b"L12_PUBLIC_BASE_URL=duplicate\0", good_formal, PROFILE_SHA),
        (good_test, good_formal + b"L12_TESTRUN_MATCH_STORAGE=ephemeral\0", PROFILE_SHA),
        (good_test, good_formal.replace(b".com", b".com/testrun"), PROFILE_SHA),
        *[(good_test, b"L12_PUBLIC_BASE_URL=" + base + b"\0", PROFILE_SHA) for base in (
            b"https://other.example", b"http://legion-12.com", b"https://user@legion-12.com",
            b"https://legion-12.com?x=1", b"https://legion-12.com/#fragment", b"https://legion-12.com//")],
        (good_test, good_formal, "0" * 64),
    ]:
        try:
            validate_normal_profile(test_raw, formal_raw, sha)
            raise AssertionError("BAD_PROFILE_ACCEPTED")
        except ValueError:
            cases += 1
    values = ["a" * 40, "a" * 40, "101", "102", "B" * 64, "B" * 64, PROFILE_SHA, "900"]
    config = normal_config(legacy, values, enforce_host=False)
    assert config.expected_test == config.expected_formal
    cases += 1
    try:
        normal_config(legacy, values[:3] + ["101"] + values[4:], enforce_host=False)
        raise AssertionError("SAME_PROCESS_ACCEPTED")
    except ValueError:
        cases += 1

    class MemoryOperations(operations_type(legacy)):
        memory, available, oom, restart = 200 * 1024 * 1024, 1024 * 1024 * 1024, 0, 0
        test_pid, formal_pid = "101", "102"
        def bounded_bytes(self, name):
            return good_test if "/101/" in name else good_formal
        def service(self, name):
            return {"MainPID": self.test_pid if name == legacy.TEST_UNIT else self.formal_pid,
                    "NRestarts": str(self.restart), "ActiveState": "active", "ControlGroup": "/system.slice/example"}
        def health(self, _port):
            return {"serverVersion": "a" * 40, "status": "ok", "maintenance": False}, 1.0
        def identity(self, selected):
            self.nonce_identity(selected)
        def resources(self, _group):
            return {"memory": self.memory, "available": self.available, "oom": self.oom, "usage": 100}

    def runner(ops):
        clock = iter(index / 1000 for index in range(1000))
        def deadline(seconds):
            assert seconds == 3.75
            return contextlib.nullcontext()
        return legacy.GuardRunner(config, ops, monotonic=lambda: next(clock), deadline_factory=deadline)

    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        ops = MemoryOperations()
        selected = runner(ops)
        assert selected.collect(0, executor)["stop"] is False
        cases += 1
        for attribute, value, reason in [
            ("memory", 850 * 1024 * 1024, "TEST_MEMORY_850M"),
            ("available", 512 * 1024 * 1024 - 1, "HOST_AVAILABLE_512M"),
            ("oom", 1, "TEST_OOM_OR_RESTART"),
            ("restart", 1, "TEST_OOM_OR_RESTART"),
            ("test_pid", "999", "TEST_PROCESS_CHANGED"),
            ("formal_pid", "999", "FORMAL_PROCESS_CHANGED"),
        ]:
            ops = MemoryOperations(); selected = runner(ops)
            assert selected.collect(0, executor)["stop"] is False
            setattr(ops, attribute, value)
            row = selected.collect(1, executor)
            assert row["stop"] and row["reason"] == reason
            cases += 1
    child_cases = []
    script = str(pathlib.Path(__file__).resolve())
    common = ["--frozen-guard", str(pathlib.Path(selected_path).resolve()), "--test-commit", "a" * 40,
              "--formal-commit", "a" * 40, "--test-pid", "101", "--formal-pid", "102",
              "--test-dll", "B" * 64, "--formal-dll", "B" * 64]

    def run_child(name, arguments, expected_code, expected_reason, maximum_seconds=8):
        begun = time.monotonic()
        result = subprocess.run([sys.executable, "-I", "-B", script, *arguments], capture_output=True,
                                timeout=maximum_seconds, check=False)
        elapsed = time.monotonic() - begun
        assert result.returncode == expected_code, (name, result.returncode, result.stderr)
        assert not result.stderr, (name, "UNEXPECTED_STDERR")
        assert len(result.stdout) <= MAX_STDOUT_BYTES
        rows = [read_json(line) for line in result.stdout.splitlines()]
        assert rows and rows[-1]["reason"] == expected_reason, (name, rows)
        assert b"private-fixture-must-not-be-forwarded" not in result.stdout
        if rows[-1].get("kind") == "normal-guard-completion":
            assert rows[-1]["selfTestOnly"] is True
            assert rows[-1]["allOwnedChildrenReaped"] is True
            assert rows[-1]["serviceWrites"] == rows[-1]["formalWrites"] == 0
            ready = next(row for row in rows if row.get("kind") == "normal-guard-memory-ready")
            assert not any(row.get("kind") == "normal-guard-ready" for row in rows)
            assert validate_completion_identity(ready, rows[-1])
        else:
            assert rows[-1]["childProcessesStarted"] == 0
        child_cases.append({"name": name, "exitCode": result.returncode, "reason": rows[-1]["reason"],
                            "elapsedMs": round(elapsed * 1000, 2), "stdoutBytes": len(result.stdout),
                            "stderrBytes": len(result.stderr), "selfTestOnly": True})
        return rows

    assert_live_runtime(legacy, platform="linux", hostname=legacy.EXPECTED_HOST, uid=0)
    cases += 1
    for name, replacement in [("platform", {"platform": "win32"}), ("host", {"hostname": "wrong-host"}), ("uid", {"uid": 1000})]:
        parameters = {"platform": "linux", "hostname": legacy.EXPECTED_HOST, "uid": 0, **replacement}
        try:
            assert_live_runtime(legacy, **parameters)
            raise AssertionError("UNSAFE_RUNTIME_ACCEPTED")
        except ValueError:
            cases += 1
    valid_memory = ["--self-test-scenario", "healthy", *common, "--seconds", "0.25"]
    for name, change, expected_reason in [
        ("missing-mode", [], "EXACT_NORMAL_GUARD_MODE_REQUIRED"),
        ("unknown-option", [*valid_memory, "--remote", "true"], "NORMAL_GUARD_ARGUMENT_REJECTED"),
        ("duplicate-option", [*valid_memory, "--seconds", "1"], "NORMAL_GUARD_ARGUMENT_REJECTED"),
        ("missing-option-value", [*valid_memory, "--self-test-signal-after-ms"], "NORMAL_GUARD_ARGUMENT_REJECTED"),
        ("missing-required", ["--self-test-scenario", "healthy", "--seconds", "1"], "NORMAL_GUARD_ARGUMENT_REQUIRED"),
        ("nonfinite-duration", [*valid_memory[:-1], "nan"], "SELF_TEST_SECONDS_BOUND"),
        ("selftest-too-long", [*valid_memory[:-1], "31"], "SELF_TEST_SECONDS_BOUND"),
        ("live-too-long", ["--observe", *common, "--seconds", "931"], "NORMAL_SECONDS_30_TO_930_REQUIRED"),
        ("live-selftest-flag-rejected", ["--observe", *common, "--seconds", "30", "--self-test-signal-after-ms", "1"], "NORMAL_GUARD_ARGUMENT_REJECTED"),
    ]:
        run_child(name, change, 64, expected_reason)
    if sys.platform != "linux":
        run_child("actual-live-CLI-rejects-non-linux", ["--observe", *common, "--seconds", "30"], 64, "NORMAL_GUARD_LINUX_REQUIRED")
    for name, key, replacement, reason in [
        ("bad-commit", "--test-commit", "bad", "COMMIT_FORMAT"),
        ("bad-pid", "--test-pid", "0", "PID_FORMAT"),
        ("same-pid", "--formal-pid", "101", "NORMAL_DISTINCT_ENVIRONMENT_BINDING_REQUIRED"),
        ("unsafe-pid", "--test-pid", "9007199254740992", "NORMAL_PID_SAFE_INTEGER_REQUIRED"),
        ("bad-dll", "--test-dll", "b" * 64, "HASH_FORMAT"),
        ("relative-source", "--frozen-guard", "guard.py", "FROZEN_GUARD_ABSOLUTE_PATH_REQUIRED"),
    ]:
        changed = list(valid_memory); changed[changed.index(key) + 1] = replacement
        run_child(name, changed, 64, reason)
    healthy = run_child("total-deadline-real-child", valid_memory, 2, "TOTAL_DEADLINE_REACHED")
    assert len(healthy) >= 2 and healthy[-1]["elapsedMs"] < 1250
    ready = healthy[0]
    assert ready["kind"] == "normal-guard-memory-ready" and ready["supervisorPid"] > 0
    assert ready["testPid"] != ready["formalPid"]
    assert ready["sourceSha256"] == hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest().upper()
    cases += 1
    fake_stat = "101 (space and ) name) " + " ".join(["S"] + ["0"] * 18 + ["12345"])
    assert parse_process_start_ticks(fake_stat, 101) == "12345"
    cases += 1
    for field, value in [("supervisorPid", 999), ("processStartTicks", "999"), ("bootId", "changed"),
                         ("sourceSha256", "0" * 64), ("profileSha256", "0" * 64), ("selfTestOnly", False)]:
        bad = dict(healthy[-1], **{field: value})
        try:
            validate_completion_identity(ready, bad)
            raise AssertionError("WRONG_COMPLETION_IDENTITY_ACCEPTED")
        except ValueError:
            cases += 1
    try:
        parse_process_start_ticks(fake_stat, 102)
        raise AssertionError("WRONG_STAT_PID_ACCEPTED")
    except ValueError:
        cases += 1
    for scenario, reason in [("memory", "TEST_MEMORY_850M"), ("bad-profile", "NORMAL_TEST_F2_OBJECT_REQUIRED"),
                             ("noise", "WORKER_OUTPUT_INVALID"), ("extra", "WORKER_OUTPUT_INVALID"),
                             ("flood", "WORKER_STDOUT_BOUND"), ("stderr", "WORKER_STDERR_NOT_EMPTY"),
                             ("bad-state", "WORKER_STATE_INVALID")]:
        run_child("worker-" + scenario, ["--self-test-scenario", scenario, *common, "--seconds", "6"], 1, reason)
    blocked = run_child("round-deadline-real-blocked-child", ["--self-test-scenario", "hang", *common, "--seconds", "6"],
                        1, "ROUND_PROCESS_DEADLINE_3750MS")
    assert 3700 <= blocked[-1]["elapsedMs"] < 4750
    total = run_child("total-deadline-reaps-blocked-child", ["--self-test-scenario", "hang", *common, "--seconds", "0.20"],
                      2, "TOTAL_DEADLINE_REACHED")
    assert total[-1]["elapsedMs"] < 1200
    terminated = run_child("actual-subprocess-internal-SIGTERM-delivery", ["--self-test-scenario", "hang", *common,
                           "--seconds", "6", "--self-test-signal-after-ms", "150"], 143, "SIGTERM_REQUESTED")
    assert terminated[-1]["elapsedMs"] < 1200
    closed_started = time.monotonic()
    closed = subprocess.Popen([sys.executable, "-I", "-B", script, *valid_memory], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    closed.stdout.close(); closed.stdout = None
    _unused, closed_errors = closed.communicate(timeout=2)
    assert closed.returncode == 74 and not closed_errors
    child_cases.append({"name": "closed-supervisor-output-pipe", "exitCode": 74,
                        "elapsedMs": round((time.monotonic() - closed_started) * 1000, 2), "selfTestOnly": True})
    input_started = time.monotonic()
    unfinished = subprocess.Popen([sys.executable, "-I", "-B", script, "--_round-worker", str(pathlib.Path(selected_path).resolve()),
                                   "--_memory-scenario", "healthy"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    unfinished.stdin.write(b"{"); unfinished.stdin.flush()
    unfinished.wait(timeout=2)
    unfinished.stdin.close()
    input_output = unfinished.stdout.read(); input_errors = unfinished.stderr.read()
    unfinished.stdout.close(); unfinished.stderr.close()
    input_receipt = read_json(input_output)
    assert unfinished.returncode == 1 and not input_errors
    assert input_receipt["row"]["reason"] == "WORKER_INPUT_DEADLINE_1S" and input_receipt["selfTestOnly"] is True
    child_cases.append({"name": "internal-worker-unclosed-stdin-deadline", "exitCode": 1,
                        "elapsedMs": round((time.monotonic() - input_started) * 1000, 2), "selfTestOnly": True})
    external_signal_verified = False
    if sys.platform == "linux":
        process = subprocess.Popen([sys.executable, "-I", "-B", script, "--self-test-scenario", "healthy", *common, "--seconds", "6"],
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        first = process.stdout.readline()
        assert read_json(first)["selfTestOnly"] is True
        process.send_signal(signal.SIGTERM)
        remaining, errors = process.communicate(timeout=2)
        completion = read_json(remaining.splitlines()[-1])
        assert process.returncode == 143 and not errors and completion["reason"] == "SIGTERM_REQUESTED"
        assert completion["allOwnedChildrenReaped"] is True
        child_cases.append({"name": "external-POSIX-SIGTERM", "exitCode": 143, "selfTestOnly": True})
        external_signal_verified = True
    return {"schema": 1, "status": "offline-tests-passed", "cases": cases + len(child_cases), "networkCalls": 0,
            "serviceWrites": 0, "originalGuardSha256": LEGACY_SHA, "profileSha256": PROFILE_SHA,
            "guardSourceSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest().upper(),
            "remoteExecutionEnabled": False, "observeCliImplemented": True, "externalPosixSigtermVerified": external_signal_verified,
            "actualChildProcessCases": child_cases,
            "scope": "injected memory operations and actual CLI subprocesses; no real systemd, cgroup, HTTP or F2 API was read"}


if __name__ == "__main__":
    arguments = sys.argv[1:]
    if arguments == ["--self-test"]:
        print(json.dumps(self_test(), separators=(",", ":")))
    elif len(arguments) == 3 and arguments[:2] == ["--self-test", "--frozen-guard"]:
        print(json.dumps(self_test(arguments[2]), separators=(",", ":")))
    elif len(arguments) in (2, 4) and arguments[0] == "--_round-worker":
        scenario = None
        if len(arguments) == 4:
            if arguments[2] != "--_memory-scenario" or arguments[3] not in SELF_TEST_SCENARIOS:
                raise SystemExit(64)
            scenario = arguments[3]
        raise SystemExit(round_worker(arguments[1], scenario))
    else:
        raise SystemExit(cli(arguments))
