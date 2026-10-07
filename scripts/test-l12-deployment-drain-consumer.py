#!/usr/bin/env python3
"""Isolated fault matrix for the production deployment-drain consumer."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ops" / "server" / "l12-deployment-drain-consumer.py"
SPEC = importlib.util.spec_from_file_location("l12_deployment_drain_consumer", SOURCE)
assert SPEC is not None and SPEC.loader is not None
consumer = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = consumer
SPEC.loader.exec_module(consumer)


TOKEN = "1" * 64
OPERATION = "2" * 32
PROCESS_A = "3" * 32
PROCESS_B = "4" * 32
SEAL = "5" * 32
OLD_COMMIT = "6" * 40
NEW_COMMIT = "7" * 40
PACKAGE_SHA = "8" * 64
WALL_EPOCH = 2_000_000_000
DEADLINE_EPOCH = WALL_EPOCH + 900


def zeroes(keys: set[str]) -> dict[str, int]:
    return {key: 0 for key in sorted(keys)}


def clear_readiness() -> dict[str, Any]:
    return {
        "rooms": {"verified": True, "blockers": zeroes(consumer.ROOM_BLOCKERS)},
        "durability": {
            "verified": True,
            "blockers": zeroes(consumer.DURABILITY_BLOCKERS),
            "failureCode": "none",
            "queries": len(consumer.DURABILITY_BLOCKERS) + 1,
        },
        "platform": {
            "verified": True,
            "revision": 12,
            "pendingRoomCommands": 0,
            "failureCode": None,
        },
    }


def empty_readiness() -> dict[str, Any]:
    return {"rooms": None, "durability": None, "platform": None}


def owner(process: str = PROCESS_A) -> dict[str, str]:
    return {
        "operationId": OPERATION,
        "targetCommit": NEW_COMMIT,
        "processInstance": process,
    }


def permit(epoch: int = 3) -> dict[str, Any]:
    return {
        "protocolVersion": 1,
        "operationId": OPERATION,
        "targetCommit": NEW_COMMIT,
        "processInstance": PROCESS_A,
        "activeCommit": OLD_COMMIT,
        "epoch": epoch,
        "sealId": SEAL,
    }


def state(*, phase: str, epoch: int, process: str = PROCESS_A,
          active: str = OLD_COMMIT, current_owner: dict[str, str] | None = None,
          code: str = "status", admission: int = 0, activity: int = 0,
          synchronized: bool = True, unknown: bool = False, consumed: bool = False,
          current_permit: dict[str, Any] | None = None,
          readiness: dict[str, Any] | None = None) -> dict[str, Any]:
    return {
        "protocolVersion": 1,
        "code": code,
        "phase": phase,
        "epoch": epoch,
        "processInstance": process,
        "activeCommit": active,
        "owner": current_owner,
        "admissionLeases": admission,
        "activityLeases": activity,
        "fenceSynchronized": synchronized,
        "fenceUnknown": unknown,
        "stopConsumed": consumed,
        "sealReady": current_permit is not None,
        "stopPermitted": current_permit is not None and consumed,
        "permit": current_permit,
        "readiness": empty_readiness() if readiness is None else readiness,
    }


def open_state(*, process: str = PROCESS_A, active: str = OLD_COMMIT,
               epoch: int = 0, code: str = "status") -> dict[str, Any]:
    return state(phase="open", epoch=epoch, process=process, active=active,
                 current_owner=None, code=code)


def draining(*, process: str = PROCESS_A, active: str = OLD_COMMIT,
             epoch: int = 1, code: str = "status", activity: int = 0) -> dict[str, Any]:
    return state(phase="draining", epoch=epoch, process=process, active=active,
                 current_owner=owner(process), code=code, activity=activity)


def sealed(*, epoch: int = 3, code: str = "Applied", consumed: bool = False,
           readiness: dict[str, Any] | None = None, include_permit: bool = True) -> dict[str, Any]:
    return state(phase="sealed", epoch=epoch, current_owner=owner(), code=code,
                 consumed=consumed, current_permit=permit(epoch) if include_permit else None,
                 readiness=readiness)


class FakeTransport:
    def __init__(self, steps: list[Any]):
        self.steps = list(steps)
        self.calls: list[tuple[str, str, str, Any]] = []

    def request(self, method: str, path: str, token: str,
                body: dict[str, Any] | None = None) -> tuple[int, dict[str, Any]]:
        self.calls.append((method, path, token, body))
        if not self.steps:
            raise AssertionError(f"unexpected request {method} {path}")
        step = self.steps.pop(0)
        if isinstance(step, BaseException):
            raise step
        expected_method, expected_endpoint, status, response = step
        if method != expected_method or path != f"{consumer.CONTROL_ROOT}/{expected_endpoint}":
            raise AssertionError(f"expected {expected_method} {expected_endpoint}, got {method} {path}")
        if token != TOKEN:
            raise AssertionError("consumer changed the bearer token")
        return status, response


class Clock:
    def __init__(self) -> None:
        self.value = 0.0

    def monotonic(self) -> float:
        return self.value

    def sleep(self, seconds: float) -> None:
        self.value += seconds

    def wall(self) -> float:
        return WALL_EPOCH + self.value


def binding(action: str = "deploy", package_sha: str = PACKAGE_SHA) -> Any:
    return consumer.Binding(action, package_sha, OPERATION, NEW_COMMIT)


def client(steps: list[Any], clock: Clock | None = None) -> tuple[Any, FakeTransport]:
    fake = FakeTransport(steps)
    if clock is None:
        clock = Clock()
    return consumer.DeploymentDrainConsumer(
        fake, TOKEN, clock.monotonic, clock.sleep, clock.wall), fake


class ConsumerTests(unittest.TestCase):
    def assert_done(self, fake: FakeTransport) -> None:
        self.assertEqual([], fake.steps)
        self.assertTrue(all(call[2] == TOKEN for call in fake.calls))

    def test_acquire_stop_binds_exact_permit_and_package(self) -> None:
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 200, sealed(readiness=clear_readiness())),
            ("POST", "consume", 200, sealed(consumed=True)),
        ]
        subject, fake = client(steps)
        receipt = subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("acquire-stop", receipt["command"])
        self.assertEqual("sealed", receipt["phase"])
        self.assertEqual(PROCESS_A, receipt["processInstance"])
        self.assertEqual(3, receipt["epoch"])
        self.assertEqual(SEAL, receipt["sealId"])
        self.assertEqual(PACKAGE_SHA, receipt["packageSha256"])
        self.assertRegex(receipt["transactionBindingSha256"], r"^[0-9a-f]{64}$")
        self.assertRegex(receipt["stateBindingSha256"], r"^[0-9a-f]{64}$")
        self.assert_done(fake)

    def test_action_and_package_are_part_of_transaction_binding(self) -> None:
        baseline = binding().transaction_sha256()
        self.assertNotEqual(baseline, binding("rollback").transaction_sha256())
        self.assertNotEqual(baseline, binding(package_sha="9" * 64).transaction_sha256())

    def test_protocol_unavailable_never_begins(self) -> None:
        error = {
            "code": "deployment_protocol_unavailable",
            "message": "not enabled",
            "correlationId": "constant-test-correlation",
        }
        subject, fake = client([("GET", "status", 503, error)])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("PROTOCOL_UNAVAILABLE", caught.exception.code)
        self.assertEqual("unchanged", caught.exception.disposition)
        self.assert_done(fake)

    def test_authentication_rejection_is_constant_and_does_not_mutate(self) -> None:
        error = {"code": "authentication_required", "message": "secret detail", "correlationId": "id"}
        subject, fake = client([("GET", "status", 401, error)])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("AUTHENTICATION_REJECTED", caught.exception.code)
        self.assertNotIn("secret", caught.exception.code)
        self.assert_done(fake)

    def test_bool_and_wrong_owner_numeric_types_are_rejected(self) -> None:
        cases: list[tuple[str, dict[str, Any]]] = []

        bad = open_state()
        bad["protocolVersion"] = True
        cases.append(("control protocol bool", bad))

        bad = open_state()
        bad["epoch"] = True
        cases.append(("control epoch bool", bad))

        bad = draining()
        bad["owner"]["operationId"] = True
        cases.append(("owner identifier bool", bad))

        bad = sealed(readiness=clear_readiness())
        bad["permit"]["protocolVersion"] = True
        cases.append(("permit protocol bool", bad))

        bad = sealed(readiness=clear_readiness())
        bad["permit"]["epoch"] = True
        cases.append(("permit epoch bool", bad))

        bad = sealed(readiness=clear_readiness())
        bad["readiness"]["rooms"]["blockers"]["unfinished_rooms"] = True
        cases.append(("room count bool", bad))

        bad = sealed(readiness=clear_readiness())
        bad["readiness"]["durability"]["queries"] = True
        cases.append(("query count bool", bad))

        bad = sealed(readiness=clear_readiness())
        bad["readiness"]["platform"]["revision"] = True
        cases.append(("platform revision bool", bad))

        bad = sealed(readiness=clear_readiness())
        bad["readiness"]["platform"]["pendingRoomCommands"] = True
        cases.append(("pending command count bool", bad))

        for label, response in cases:
            with self.subTest(label=label):
                with self.assertRaises(consumer.ConsumerFailure) as caught:
                    consumer._validate_control(response, "strict-type")
                self.assertEqual("RESPONSE_CONTRACT_REJECTED", caught.exception.code)

    def test_bool_http_status_is_not_treated_as_200(self) -> None:
        subject, fake = client([("GET", "status", True, open_state())])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("RESPONSE_CONTRACT_REJECTED", caught.exception.code)
        self.assertEqual("unknown", caught.exception.disposition)
        self.assert_done(fake)

    def test_non_200_begin_with_own_drain_is_reconciled_and_cancelled(self) -> None:
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 409, draining(code="TransitionInProgress")),
            ("GET", "status", 200, draining()),
            ("GET", "status", 200, draining()),
            ("POST", "cancel", 200, open_state(epoch=2, code="Applied")),
        ]
        subject, fake = client(steps)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("BEGIN_REJECTED", caught.exception.code)
        self.assertEqual("open", caught.exception.disposition)
        self.assertTrue(caught.exception.retry_safe)
        self.assert_done(fake)

    def test_non_200_begin_is_unchanged_only_after_exact_open_reread(self) -> None:
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 409, draining(code="TransitionInProgress")),
            ("GET", "status", 200, open_state()),
        ]
        subject, fake = client(steps)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("BEGIN_REJECTED", caught.exception.code)
        self.assertEqual("unchanged", caught.exception.disposition)
        self.assert_done(fake)

    def test_non_200_begin_foreign_reread_stays_unknown_and_is_not_cancelled(self) -> None:
        foreign = owner()
        foreign["operationId"] = "a" * 32
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 409, draining(code="TransitionInProgress")),
            ("GET", "status", 200,
             state(phase="draining", epoch=1, current_owner=foreign)),
        ]
        subject, fake = client(steps)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("OWNER_OR_STATE_MISMATCH", caught.exception.code)
        self.assertEqual("unknown", caught.exception.disposition)
        self.assert_done(fake)

    def test_deadline_reserves_recovery_budget_before_any_request(self) -> None:
        subject, fake = client([])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(
                binding(), OLD_COMMIT, 30, 100,
                WALL_EPOCH + consumer.RECOVERY_RESERVE_SECONDS)
        self.assertEqual("DEPLOYMENT_DEADLINE_EXCEEDED", caught.exception.code)
        self.assertEqual("initial-deadline", caught.exception.stage)
        self.assert_done(fake)

    def test_deadline_is_rechecked_before_consume_and_own_drain_is_cancelled(self) -> None:
        clock = Clock()
        waiting = draining(code="OutstandingLeases", activity=1)
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 409, waiting),
            ("POST", "seal", 200, sealed(readiness=clear_readiness())),
            ("GET", "status", 200, draining()),
            ("POST", "cancel", 200, open_state(epoch=2, code="Applied")),
        ]
        subject, fake = client(steps, clock)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(
                binding(), OLD_COMMIT, 30, 1000,
                WALL_EPOCH + consumer.RECOVERY_RESERVE_SECONDS + 1)
        self.assertEqual("DEPLOYMENT_DEADLINE_EXCEEDED", caught.exception.code)
        self.assertEqual("open", caught.exception.disposition)
        self.assertTrue(caught.exception.retry_safe)
        self.assert_done(fake)

    def test_preexisting_foreign_drain_is_not_cancelled(self) -> None:
        foreign = dict(owner())
        foreign["operationId"] = "a" * 32
        subject, fake = client([("GET", "status", 200,
                                 state(phase="draining", epoch=4, current_owner=foreign))])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("PREEXISTING_OR_UNKNOWN_FENCE", caught.exception.code)
        self.assert_done(fake)

    def test_unknown_response_field_cancels_own_unconsumed_drain(self) -> None:
        bad = sealed(readiness=clear_readiness())
        bad["futureField"] = True
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 200, bad),
            ("GET", "status", 200, draining()),
            ("POST", "cancel", 200, open_state(epoch=2, code="Applied")),
        ]
        subject, fake = client(steps)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("RESPONSE_CONTRACT_REJECTED", caught.exception.code)
        self.assertEqual("open", caught.exception.disposition)
        self.assert_done(fake)

    def test_readiness_blocker_times_out_then_exactly_cancels(self) -> None:
        blocked = clear_readiness()
        blocked["durability"]["blockers"]["unfinished_matches"] = 9
        first = draining(code="durability_not_clear")
        first["readiness"] = blocked
        clock = Clock()
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 409, first),
            ("POST", "seal", 409, first),
            ("GET", "status", 200, draining()),
            ("POST", "cancel", 200, open_state(epoch=2, code="Applied")),
        ]
        subject, fake = client(steps, clock)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 1, 1000, DEADLINE_EPOCH)
        self.assertEqual("DRAIN_TIMEOUT", caught.exception.code)
        self.assertEqual("open", caught.exception.disposition)
        self.assertTrue(caught.exception.retry_safe)
        self.assert_done(fake)

    def test_waived_settlement_is_not_treated_as_clear(self) -> None:
        blocked = clear_readiness()
        blocked["durability"]["blockers"]["ranked_settlement_unresolved"] = 1
        self.assertFalse(consumer._all_readiness_clear(blocked))

    def test_clear_readiness_requires_every_fixed_blocker(self) -> None:
        missing = clear_readiness()
        del missing["rooms"]["blockers"]["unfinished_rooms"]
        with self.assertRaises(consumer.ConsumerFailure):
            consumer._validate_readiness(missing, "test")

    def test_consume_transport_loss_accepts_real_status_dto_without_permit_echo(self) -> None:
        real_status_shape = sealed(code="status", consumed=True, include_permit=False)
        self.assertIsNone(real_status_shape["permit"])
        self.assertFalse(real_status_shape["sealReady"])
        self.assertFalse(real_status_shape["stopPermitted"])
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 200, sealed(readiness=clear_readiness())),
            consumer.TransportFailure(),
            ("GET", "status", 200, real_status_shape),
        ]
        subject, fake = client(steps)
        receipt = subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("sealed", receipt["phase"])
        self.assert_done(fake)

    def test_consume_ambiguity_with_consumed_mismatch_stays_closed(self) -> None:
        mismatched = sealed(code="status", consumed=True, include_permit=False)
        mismatched["epoch"] = 99
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 200, sealed(readiness=clear_readiness())),
            consumer.TransportFailure(),
            ("GET", "status", 200, mismatched),
            ("GET", "status", 200, mismatched),
        ]
        subject, fake = client(steps)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 30, 100, DEADLINE_EPOCH)
        self.assertEqual("CANCEL_FAILED", caught.exception.code)
        self.assertEqual("closed", caught.exception.disposition)
        self.assert_done(fake)

    def test_cancel_failure_after_timeout_stays_closed(self) -> None:
        clock = Clock()
        waiting = draining(code="OutstandingLeases", activity=1)
        steps = [
            ("GET", "status", 200, open_state()),
            ("POST", "begin", 200, draining(code="Applied")),
            ("POST", "seal", 409, waiting),
            ("POST", "seal", 409, waiting),
            ("GET", "status", 200, draining()),
            ("POST", "cancel", 503, draining(code="FenceFailure")),
        ]
        subject, fake = client(steps, clock)
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.acquire_stop(binding(), OLD_COMMIT, 1, 1000, DEADLINE_EPOCH)
        self.assertEqual("CANCEL_FAILED", caught.exception.code)
        self.assertEqual("closed", caught.exception.disposition)
        self.assert_done(fake)

    def test_restart_observation_binds_new_process_epoch_and_target(self) -> None:
        restarted = draining(process=PROCESS_B, active=NEW_COMMIT, epoch=4)
        subject, fake = client([("GET", "status", 200, restarted)])
        receipt = subject.observe_restart(binding(), NEW_COMMIT, PROCESS_A, 3)
        self.assertEqual(PROCESS_B, receipt["processInstance"])
        self.assertEqual(4, receipt["epoch"])
        self.assertEqual("draining", receipt["phase"])
        self.assertEqual(binding().transaction_sha256(), receipt["transactionBindingSha256"])
        self.assert_done(fake)

    def test_restart_same_process_is_rejected_closed(self) -> None:
        subject, fake = client([("GET", "status", 200,
                                 draining(process=PROCESS_A, active=NEW_COMMIT, epoch=4))])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.observe_restart(binding(), NEW_COMMIT, PROCESS_A, 3)
        self.assertEqual("RESTART_STATE_REJECTED", caught.exception.code)
        self.assertEqual("closed", caught.exception.disposition)
        self.assert_done(fake)

    def test_wrong_commit_after_restart_is_rejected_closed(self) -> None:
        subject, fake = client([("GET", "status", 200,
                                 draining(process=PROCESS_B, active=OLD_COMMIT, epoch=4))])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.observe_restart(binding(), NEW_COMMIT, PROCESS_A, 3)
        self.assertEqual("OWNER_OR_STATE_MISMATCH", caught.exception.code)
        self.assertEqual("unknown", caught.exception.disposition)
        self.assert_done(fake)

    def test_verified_previous_process_can_recover_without_restoring_database(self) -> None:
        recovered = draining(process=PROCESS_B, active=OLD_COMMIT, epoch=4)
        subject, fake = client([("GET", "status", 200, recovered)])
        receipt = subject.observe_restart(binding(), OLD_COMMIT, PROCESS_A, 3)
        self.assertEqual(OLD_COMMIT, receipt["activeCommit"])
        self.assertEqual(PROCESS_B, receipt["processInstance"])
        self.assert_done(fake)

    def test_cancel_open_uses_rebound_owner_and_exact_epoch(self) -> None:
        preopen = draining(process=PROCESS_B, active=NEW_COMMIT, epoch=4)
        opened = open_state(process=PROCESS_B, active=NEW_COMMIT, epoch=5, code="Applied")
        subject, fake = client([
            ("GET", "status", 200, preopen),
            ("POST", "cancel", 200, opened),
        ])
        receipt = subject.cancel_open(binding(), NEW_COMMIT, PROCESS_B, 4)
        self.assertEqual("open", receipt["phase"])
        self.assertEqual(5, receipt["epoch"])
        self.assert_done(fake)

    def test_cancel_response_loss_accepts_only_exact_open_reread(self) -> None:
        preopen = draining(process=PROCESS_B, active=NEW_COMMIT, epoch=4)
        opened = open_state(process=PROCESS_B, active=NEW_COMMIT, epoch=5)
        subject, fake = client([
            ("GET", "status", 200, preopen),
            consumer.TransportFailure(),
            ("GET", "status", 200, opened),
        ])
        receipt = subject.cancel_open(binding(), NEW_COMMIT, PROCESS_B, 4)
        self.assertEqual("open", receipt["phase"])
        self.assert_done(fake)

    def test_cancel_ambiguous_reread_stays_closed(self) -> None:
        preopen = draining(process=PROCESS_B, active=NEW_COMMIT, epoch=4)
        subject, fake = client([
            ("GET", "status", 200, preopen),
            consumer.TransportFailure(),
            ("GET", "status", 200, preopen),
        ])
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            subject.cancel_open(binding(), NEW_COMMIT, PROCESS_B, 4)
        self.assertEqual("CANCEL_FAILED", caught.exception.code)
        self.assertEqual("closed", caught.exception.disposition)
        self.assert_done(fake)

    def test_duplicate_json_key_is_rejected(self) -> None:
        with self.assertRaises(consumer.ConsumerFailure) as caught:
            consumer._strict_json(b'{"schema":1,"schema":2}', "duplicate")
        self.assertEqual("RESPONSE_CONTRACT_REJECTED", caught.exception.code)

    def test_invalid_token_cli_never_echoes_secret_or_contacts_http(self) -> None:
        marker = "not-a-token-secret-marker"
        command = [
            sys.executable, "-B", str(SOURCE), "acquire-stop",
            "--action", "deploy", "--package-sha256", PACKAGE_SHA,
            "--operation-id", OPERATION, "--target-commit", NEW_COMMIT,
            "--expected-active-commit", OLD_COMMIT, "--wait-seconds", "1",
            "--deadline-epoch", str(DEADLINE_EPOCH),
        ]
        result = subprocess.run(command, input=marker + "\n", text=True,
                                capture_output=True, timeout=10, check=False)
        self.assertEqual(2, result.returncode)
        self.assertNotIn(marker, result.stdout)
        self.assertNotIn(marker, result.stderr)
        body = json.loads(result.stdout)
        self.assertEqual({"failedStage", "ok", "reasonCode", "retrySafe", "schema", "stateDisposition"},
                         set(body))
        self.assertEqual("TOKEN_REJECTED", body["reasonCode"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
