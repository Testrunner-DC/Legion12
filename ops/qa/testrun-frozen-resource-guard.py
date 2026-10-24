"""Deadline-bounded read-only shared-host guard.

This is a successor to resource-guard-port-r3.py.  It preserves the eight
positional arguments and the schema-1 sample/stop rows.  The only additional
row fields are ``phase`` and ``roundElapsedMs``.  It emits no heartbeat rows.
"""

from __future__ import annotations

import concurrent.futures
import contextlib
import hashlib
import json
import os
import pathlib
import re
import signal
import socket
import subprocess
import sys
import time
import urllib.request


EXPECTED_HOST = "ser550468033481"
ROUND_INTERVAL_SECONDS = 2.0
ROUND_BUDGET_SECONDS = 3.75
PROBE_BUDGET_SECONDS = 3.20
SERVICE_TIMEOUT_SECONDS = 3.0
HEALTH_TIMEOUT_SECONDS = 2.0
IDENTITY_TIMEOUT_SECONDS = 3.0
MAX_PARALLEL_PROBES = 6
MAX_TEXT_CHARS = 8192
MAX_ENVIRONMENT_BYTES = 65536

TEST_UNIT = "legion12-testrun.service"
FORMAL_UNIT = "legion12-test.service"
TEST_COMMIT_PATH = "/opt/legion12-testrun/.deployment-commit"
FORMAL_COMMIT_PATH = "/opt/legion12-test/.deployment-commit"
TEST_DLL_PATH = "/opt/legion12-testrun/publish/GrandUMIServer.dll"
FORMAL_DLL_PATH = "/opt/legion12-test/publish/GrandUMIServer.dll"


class GuardFailure(RuntimeError):
    def __init__(self, reason: str, phase: str):
        super().__init__(reason)
        self.reason = reason
        self.phase = phase


class Config:
    def __init__(self, values: list[str]):
        (
            self.expected_test,
            self.expected_formal,
            self.test_pid,
            self.formal_pid,
            self.test_dll,
            self.formal_dll,
            self.env_sha,
            raw_seconds,
        ) = values
        self.seconds = min(930, max(30, int(raw_seconds)))


def parse_config(argv: list[str], *, enforce_host: bool = True) -> Config:
    if len(argv) != 9:
        raise SystemExit("EXACT_GUARD_ARGUMENTS_AND_HOST_REQUIRED")
    if enforce_host and (
        socket.gethostname() != EXPECTED_HOST
        or not hasattr(os, "geteuid")
        or os.geteuid() != 0
    ):
        raise SystemExit("EXACT_GUARD_ARGUMENTS_AND_HOST_REQUIRED")
    expected_test, expected_formal, test_pid, formal_pid, test_dll, formal_dll, env_sha, _ = argv[1:]
    if not all(re.fullmatch(r"[0-9a-f]{40}", value) for value in (expected_test, expected_formal)):
        raise SystemExit("COMMIT_FORMAT")
    if not all(re.fullmatch(r"[1-9][0-9]*", value) for value in (test_pid, formal_pid)):
        raise SystemExit("PID_FORMAT")
    if not all(re.fullmatch(r"[0-9A-F]{64}", value) for value in (test_dll, formal_dll, env_sha)):
        raise SystemExit("HASH_FORMAT")
    try:
        return Config(argv[1:])
    except ValueError as error:
        raise SystemExit("SECONDS_FORMAT") from error


@contextlib.contextmanager
def posix_deadline(seconds: float):
    """Interrupt the main sampling thread if a complete round exceeds budget."""
    if os.name != "posix" or not hasattr(signal, "setitimer"):
        raise GuardFailure("POSIX_ROUND_DEADLINE_UNAVAILABLE", "budget")
    prior_handler = signal.getsignal(signal.SIGALRM)
    prior_delay, prior_interval = signal.setitimer(signal.ITIMER_REAL, 0)
    if prior_delay or prior_interval:
        signal.setitimer(signal.ITIMER_REAL, prior_delay, prior_interval)
        raise GuardFailure("PREEXISTING_PROCESS_TIMER", "budget")

    def expired(_signum, _frame):
        raise GuardFailure("ROUND_BUDGET_3750MS", "budget")

    signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, seconds)
    try:
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, prior_handler)


class RealOperations:
    @staticmethod
    def bounded_text(path: str) -> str:
        value = pathlib.Path(path).read_text(encoding="utf-8")
        if len(value) > MAX_TEXT_CHARS:
            raise RuntimeError("TEXT_BOUND")
        return value.strip()

    @staticmethod
    def bounded_bytes(path: str) -> bytes:
        with pathlib.Path(path).open("rb") as stream:
            value = stream.read(MAX_ENVIRONMENT_BYTES + 1)
        if len(value) > MAX_ENVIRONMENT_BYTES:
            raise RuntimeError("PROCESS_ENVIRONMENT_BOUND")
        return value

    def service(self, name: str) -> dict[str, str]:
        try:
            result = subprocess.run(
                [
                    "systemctl",
                    "show",
                    name,
                    "-p",
                    "MainPID",
                    "-p",
                    "NRestarts",
                    "-p",
                    "ActiveState",
                    "-p",
                    "ControlGroup",
                ],
                capture_output=True,
                text=True,
                timeout=SERVICE_TIMEOUT_SECONDS,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("SERVICE_READ_TIMEOUT_3S") from error
        if result.returncode:
            raise RuntimeError("SERVICE_READ")
        return dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)

    def health(self, port: int):
        started = time.monotonic()
        try:
            with urllib.request.urlopen(
                f"http://127.0.0.1:{port}/health", timeout=HEALTH_TIMEOUT_SECONDS
            ) as response:
                body = response.read(4097)
        except TimeoutError as error:
            reason = "TEST_HEALTH_TIMEOUT_2S" if port == 8084 else "FORMAL_HEALTH_TIMEOUT_2S"
            raise RuntimeError(reason) from error
        if len(body) > 4096:
            raise RuntimeError("HEALTH_BODY_BOUND")
        return json.loads(body), (time.monotonic() - started) * 1000

    def identity(self, config: Config) -> None:
        if self.bounded_text(TEST_COMMIT_PATH) != config.expected_test:
            raise RuntimeError("TEST_COMMIT_CHANGED")
        if self.bounded_text(FORMAL_COMMIT_PATH) != config.expected_formal:
            raise RuntimeError("FORMAL_COMMIT_CHANGED")
        try:
            result = subprocess.run(
                ["sha256sum", "--", TEST_DLL_PATH, FORMAL_DLL_PATH],
                capture_output=True,
                text=True,
                timeout=IDENTITY_TIMEOUT_SECONDS,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("IDENTITY_HASH_TIMEOUT_3S") from error
        if result.returncode or result.stderr:
            raise RuntimeError("IDENTITY_HASH_READ")
        found: dict[str, str] = {}
        for line in result.stdout.splitlines():
            parts = line.split(maxsplit=1)
            if len(parts) != 2 or not re.fullmatch(r"[0-9a-fA-F]{64}", parts[0]):
                raise RuntimeError("IDENTITY_HASH_OUTPUT")
            found[parts[1].lstrip("*")] = parts[0].upper()
        if found != {TEST_DLL_PATH: config.test_dll, FORMAL_DLL_PATH: config.formal_dll}:
            if found.get(TEST_DLL_PATH) != config.test_dll:
                raise RuntimeError("TEST_DLL_CHANGED")
            if found.get(FORMAL_DLL_PATH) != config.formal_dll:
                raise RuntimeError("FORMAL_DLL_CHANGED")
            raise RuntimeError("IDENTITY_HASH_OUTPUT")

    def nonce_identity(self, config: Config) -> None:
        diagnostics = [
            item.split(b"=", 1)[1]
            for item in self.bounded_bytes(f"/proc/{config.test_pid}/environ").split(b"\0")
            if item.startswith(b"L12_TESTRUN_TRANSPORT_DIAGNOSTICS=")
        ]
        if len(diagnostics) != 1 or hashlib.sha256(diagnostics[0]).hexdigest().upper() != config.env_sha:
            raise RuntimeError("DIAGNOSTIC_PROCESS_SETTING_CHANGED")

    def resources(self, control_group: str) -> dict[str, int]:
        if not control_group.startswith("/system.slice/") or ".." in control_group:
            raise RuntimeError("CGROUP_PATH")
        memory = int(self.bounded_text("/sys/fs/cgroup" + control_group + "/memory.current"))
        events = dict(
            line.split()
            for line in self.bounded_text("/sys/fs/cgroup" + control_group + "/memory.events").splitlines()
        )
        cpu = dict(
            line.split()
            for line in self.bounded_text("/sys/fs/cgroup" + control_group + "/cpu.stat").splitlines()
        )
        available_kib = int(
            next(
                line
                for line in pathlib.Path("/proc/meminfo").read_text().splitlines()
                if line.startswith("MemAvailable:")
            ).split()[1]
        )
        return {
            "memory": memory,
            "oom": int(events.get("oom_kill", "0")) + int(events.get("oom", "0")),
            "usage": int(cpu["usage_usec"]),
            "available": available_kib * 1024,
        }


def phase_call(phase: str, function, *arguments):
    try:
        return function(*arguments)
    except GuardFailure:
        raise
    except Exception as error:
        reason = str(error) if isinstance(error, RuntimeError) and str(error) else type(error).__name__
        raise GuardFailure(reason, phase) from error


class GuardRunner:
    def __init__(
        self,
        config: Config,
        operations,
        *,
        monotonic=time.monotonic,
        wall_time=time.time,
        round_budget: float = ROUND_BUDGET_SECONDS,
        probe_budget: float = PROBE_BUDGET_SECONDS,
        deadline_factory=posix_deadline,
    ):
        self.config = config
        self.operations = operations
        self.monotonic = monotonic
        self.wall_time = wall_time
        self.round_budget = round_budget
        self.probe_budget = probe_budget
        self.deadline_factory = deadline_factory
        self.base_oom = None
        self.base_restart = None
        self.previous_cpu = None
        self.formal_failures = 0

    def collect(self, rounds: int, executor: concurrent.futures.ThreadPoolExecutor) -> dict:
        started = self.monotonic()
        row = {
            "schema": 1,
            "observedUtcMs": int(self.wall_time() * 1000),
            "readOnly": True,
            "stop": False,
        }
        futures: dict[concurrent.futures.Future, str] = {}
        try:
            with self.deadline_factory(self.round_budget):
                futures[executor.submit(phase_call, "service", self.operations.service, TEST_UNIT)] = "service"
                futures[executor.submit(phase_call, "service", self.operations.service, FORMAL_UNIT)] = "service"
                if rounds % 5 == 0:
                    futures[executor.submit(phase_call, "health", self.operations.health, 8084)] = "health"
                    futures[executor.submit(phase_call, "health", self.operations.health, 8083)] = "health"
                if rounds % 30 == 0:
                    futures[executor.submit(phase_call, "identity", self.operations.identity, self.config)] = "identity"
                if rounds == 0:
                    futures[executor.submit(phase_call, "identity", self.operations.nonce_identity, self.config)] = "identity"

                done, pending = concurrent.futures.wait(futures, timeout=self.probe_budget)
                if pending:
                    for future in pending:
                        future.cancel()
                    phases = {futures[future] for future in pending}
                    phase = next(iter(phases)) if len(phases) == 1 else "budget"
                    raise GuardFailure("PROBE_BUDGET_3200MS", phase)
                results = {future: future.result() for future in done}

                service_futures = [future for future, phase in futures.items() if phase == "service"]
                test = results[service_futures[0]]
                formal = results[service_futures[1]]
                if test.get("MainPID") != self.config.test_pid or test.get("ActiveState") != "active":
                    raise GuardFailure("TEST_PROCESS_CHANGED", "service")
                if formal.get("MainPID") != self.config.formal_pid or formal.get("ActiveState") != "active":
                    raise GuardFailure("FORMAL_PROCESS_CHANGED", "service")

                resource = phase_call("resource", self.operations.resources, test.get("ControlGroup", ""))
                sample_time = self.monotonic()
                restart = int(test["NRestarts"])
                if self.base_oom is None:
                    self.base_oom, self.base_restart = resource["oom"], restart
                row.update(
                    testMemoryBytes=resource["memory"],
                    hostAvailableBytes=resource["available"],
                    oomDelta=resource["oom"] - self.base_oom,
                    restartDelta=restart - self.base_restart,
                    testPid=int(self.config.test_pid),
                    formalPid=int(self.config.formal_pid),
                    testCpuUsageUsec=resource["usage"],
                )
                if self.previous_cpu:
                    row["testCpuPercentOfOneCore"] = round(
                        (resource["usage"] - self.previous_cpu[0])
                        / (sample_time - self.previous_cpu[1])
                        / 10000,
                        2,
                    )
                self.previous_cpu = (resource["usage"], sample_time)
                if resource["memory"] >= 850 * 1024 * 1024:
                    raise GuardFailure("TEST_MEMORY_850M", "resource")
                if resource["available"] < 512 * 1024 * 1024:
                    raise GuardFailure("HOST_AVAILABLE_512M", "resource")
                if resource["oom"] != self.base_oom or restart != self.base_restart:
                    raise GuardFailure("TEST_OOM_OR_RESTART", "resource")

                health_futures = [future for future, phase in futures.items() if phase == "health"]
                if health_futures:
                    by_port = {
                        8084: results[health_futures[0]],
                        8083: results[health_futures[1]],
                    }
                    test_health, test_ms = by_port[8084]
                    formal_health, formal_ms = by_port[8083]
                    if (
                        test_health.get("serverVersion") != self.config.expected_test
                        or test_health.get("status") != "ok"
                        or test_health.get("maintenance") is not False
                    ):
                        raise GuardFailure("TEST_HTTP_IDENTITY", "health")
                    if (
                        formal_health.get("serverVersion") != self.config.expected_formal
                        or formal_health.get("status") != "ok"
                        or formal_health.get("maintenance") is not False
                    ):
                        self.formal_failures += 1
                        if self.formal_failures >= 2:
                            raise GuardFailure("FORMAL_HEALTH_UNAVAILABLE", "health")
                    else:
                        self.formal_failures = 0
                    row["testHealthMs"] = round(test_ms, 2)
                    row["formalSafetyHealthMs"] = round(formal_ms, 2)

                if self.monotonic() - started >= self.round_budget:
                    raise GuardFailure("ROUND_BUDGET_3750MS", "budget")
        except GuardFailure as error:
            return self._stop_row(started, error, row)
        except Exception as error:
            return self._stop_row(started, GuardFailure(type(error).__name__, "budget"), row)

        row["phase"] = "complete"
        row["roundElapsedMs"] = round((self.monotonic() - started) * 1000, 2)
        return row

    def _stop_row(self, started: float, error: GuardFailure, row: dict | None = None) -> dict:
        value = row or {
            "schema": 1,
            "observedUtcMs": int(self.wall_time() * 1000),
            "readOnly": True,
        }
        value["stop"] = True
        value["reason"] = error.reason
        value["phase"] = error.phase
        value["roundElapsedMs"] = round((self.monotonic() - started) * 1000, 2)
        return value


def emit(row: dict) -> None:
    print(json.dumps(row, separators=(",", ":")), flush=True)


def interval_delay(round_started: float, now: float) -> float:
    return max(0.0, round_started + ROUND_INTERVAL_SECONDS - now)


def main(argv: list[str] | None = None) -> int:
    config = parse_config(argv or sys.argv)
    operations = RealOperations()
    runner = GuardRunner(config, operations)

    started = time.monotonic()
    rounds = 0
    executor = concurrent.futures.ThreadPoolExecutor(
        max_workers=MAX_PARALLEL_PROBES, thread_name_prefix="l12-guard"
    )
    try:
        while time.monotonic() - started < config.seconds:
            round_started = time.monotonic()
            row = runner.collect(rounds, executor)
            emit(row)
            if row["stop"]:
                return 1
            rounds += 1
            time.sleep(interval_delay(round_started, time.monotonic()))
    finally:
        executor.shutdown(wait=False, cancel_futures=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
