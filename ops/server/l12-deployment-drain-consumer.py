#!/usr/bin/env python3
"""Strict loopback consumer for the Legion12 deployment-drain protocol.

The bearer session is read once from standard input.  It is never accepted in an
argument, environment variable, file, log message, exception, or JSON receipt.
This helper does not stop or start systemd; it only consumes and releases the
application-level stop permit.  The enclosing deployment script owns the single
flock transaction and binds these receipts to the verified release archive.
"""

from __future__ import annotations

import argparse
import hashlib
import http.client
import json
import re
import sys
import time
from dataclasses import dataclass
from typing import Any, Callable


PROTOCOL_VERSION = 1
LOOPBACK_HOST = "127.0.0.1"
LOOPBACK_PORT = 8083
CONTROL_ROOT = "/api/admin/deployment-drain"
MAX_RESPONSE_BYTES = 64 * 1024
MAX_WAIT_SECONDS = 30 * 60
MAX_POLL_MILLISECONDS = 10_000
RECOVERY_RESERVE_SECONDS = 5 * 60

GUID_RE = re.compile(r"^[0-9a-f]{32}$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
TOKEN_RE = re.compile(r"^[0-9a-f]{64}$")

CONTROL_KEYS = {
    "protocolVersion", "code", "phase", "epoch", "processInstance", "activeCommit",
    "owner", "admissionLeases", "activityLeases", "fenceSynchronized", "fenceUnknown",
    "stopConsumed", "sealReady", "stopPermitted", "permit", "readiness",
}
OWNER_KEYS = {"operationId", "targetCommit", "processInstance"}
PERMIT_KEYS = {
    "protocolVersion", "operationId", "targetCommit", "processInstance", "activeCommit",
    "epoch", "sealId",
}
READINESS_KEYS = {"rooms", "durability", "platform"}
ROOM_READINESS_KEYS = {"verified", "blockers"}
DURABILITY_READINESS_KEYS = {"verified", "blockers", "failureCode", "queries"}
PLATFORM_READINESS_KEYS = {"verified", "revision", "pendingRoomCommands", "failureCode"}
ERROR_KEYS = {"code", "message", "correlationId"}

ROOM_BLOCKERS = {
    "room_lock_busy", "pregame_rooms", "unfinished_rooms", "unrecorded_completion",
    "unreported_ranked", "unreported_tournament", "pending_response_sync",
    "queued_matchmaking", "pending_invitations", "unknown_rooms", "invalid_memberships",
}
DURABILITY_BLOCKERS = {
    "unfinished_matches", "invalid_metadata", "unknown_modes", "unsupported_versions",
    "active_or_unknown_runtime", "runtime_inconsistent", "ranked_settlement_unresolved",
    "quarantined_recovery", "tournament_settlement_unresolved", "response_outbox_unresolved",
    "sandbox_unresolved", "journal_orphans", "missing_v2_checkpoint",
    "expiration_inconsistent",
}
CONTROL_CODES = {
    "status", "Applied", "Idempotent", "TransitionInProgress", "WrongPhase",
    "OwnerMismatch", "OutstandingLeases", "StaleEpoch", "ExternalReadinessBlocked",
    "ExternalReadinessUnknown", "FenceFailure", "UnknownFence", "probe_busy",
    "probe_unknown", "probe_cancelled", "drain_not_ready", "work_remaining",
    "durability_not_clear",
}
WAITABLE_SEAL_CODES = {
    "TransitionInProgress", "OutstandingLeases", "ExternalReadinessBlocked",
    "probe_busy", "drain_not_ready", "work_remaining", "durability_not_clear",
}
ERROR_CODE_MAP = {
    "authentication_required": "AUTHENTICATION_REJECTED",
    "permission_denied": "AUTHORIZATION_REJECTED",
    "audit_unavailable": "AUDIT_UNAVAILABLE",
    "deployment_protocol_unavailable": "PROTOCOL_UNAVAILABLE",
    "invalid_deployment_owner": "REQUEST_REJECTED",
    "invalid_deployment_permit": "REQUEST_REJECTED",
}


class ConsumerFailure(Exception):
    def __init__(self, code: str, stage: str, disposition: str = "unchanged",
                 retry_safe: bool = False):
        super().__init__(code)
        self.code = code
        self.stage = stage
        self.disposition = disposition
        self.retry_safe = retry_safe

    def with_disposition(self, disposition: str, retry_safe: bool | None = None) -> "ConsumerFailure":
        return ConsumerFailure(self.code, self.stage, disposition,
                               self.retry_safe if retry_safe is None else retry_safe)


class TransportFailure(Exception):
    pass


class SafeArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        raise ConsumerFailure("ARGUMENT_REJECTED", "arguments")


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _exact_object(value: Any, keys: set[str], stage: str) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != keys:
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    return value


def _nonnegative_int(value: Any, stage: str) -> int:
    if not _is_int(value) or value < 0 or value >= 2**63 - 1:
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    return value


def _strict_json(raw: bytes, stage: str) -> dict[str, Any]:
    def pairs(items: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in items:
            if key in result:
                raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
            result[key] = value
        return result

    try:
        decoded = raw.decode("utf-8", errors="strict")
        value = json.loads(decoded, object_pairs_hook=pairs)
    except ConsumerFailure:
        raise
    except (UnicodeDecodeError, json.JSONDecodeError):
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown") from None
    if not isinstance(value, dict):
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    return value


class LoopbackTransport:
    def request(self, method: str, path: str, token: str,
                body: dict[str, Any] | None = None) -> tuple[int, dict[str, Any]]:
        payload = None if body is None else json.dumps(
            body, ensure_ascii=True, sort_keys=True, separators=(",", ":")
        ).encode("ascii")
        headers = {
            "Accept": "application/json",
            "Authorization": f"Bearer {token}",
            "Cache-Control": "no-store",
            "Connection": "close",
        }
        if payload is not None:
            headers["Content-Type"] = "application/json"
            headers["Content-Length"] = str(len(payload))
        connection = http.client.HTTPConnection(LOOPBACK_HOST, LOOPBACK_PORT, timeout=5)
        try:
            connection.request(method, path, body=payload, headers=headers)
            response = connection.getresponse()
            content_type = response.getheader("Content-Type", "").split(";", 1)[0].strip().lower()
            content_encoding = response.getheader("Content-Encoding", "identity").strip().lower()
            raw = response.read(MAX_RESPONSE_BYTES + 1)
            if len(raw) > MAX_RESPONSE_BYTES or content_type != "application/json" \
                    or content_encoding not in ("", "identity"):
                raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", "http-response", "unknown")
            return response.status, _strict_json(raw, "http-response")
        except ConsumerFailure:
            raise
        except (OSError, TimeoutError, http.client.HTTPException):
            raise TransportFailure() from None
        finally:
            connection.close()


@dataclass(frozen=True)
class Binding:
    action: str
    package_sha256: str
    operation_id: str
    target_commit: str

    def transaction_sha256(self) -> str:
        raw = json.dumps({
            "action": self.action,
            "operationId": self.operation_id,
            "packageSha256": self.package_sha256,
            "schema": 1,
            "targetCommit": self.target_commit,
        }, ensure_ascii=True, sort_keys=True, separators=(",", ":")).encode("ascii")
        return hashlib.sha256(raw).hexdigest()


def _state_sha256(binding: Binding, active_commit: str, process_instance: str,
                  epoch: int, seal_id: str | None) -> str:
    raw = json.dumps({
        "activeCommit": active_commit,
        "epoch": epoch,
        "processInstance": process_instance,
        "sealId": seal_id,
        "transactionBindingSha256": binding.transaction_sha256(),
    }, ensure_ascii=True, sort_keys=True, separators=(",", ":")).encode("ascii")
    return hashlib.sha256(raw).hexdigest()


def _validate_blockers(value: Any, expected: set[str], stage: str) -> dict[str, int]:
    if not isinstance(value, dict) or set(value) != expected:
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    for count in value.values():
        _nonnegative_int(count, stage)
    return value


def _validate_readiness(value: Any, stage: str) -> dict[str, Any]:
    readiness = _exact_object(value, READINESS_KEYS, stage)
    rooms = readiness["rooms"]
    if rooms is not None:
        room = _exact_object(rooms, ROOM_READINESS_KEYS, stage)
        if not isinstance(room["verified"], bool):
            raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
        _validate_blockers(room["blockers"], ROOM_BLOCKERS, stage)
    durability = readiness["durability"]
    if durability is not None:
        durable = _exact_object(durability, DURABILITY_READINESS_KEYS, stage)
        if not isinstance(durable["verified"], bool) or not isinstance(durable["failureCode"], str):
            raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
        _validate_blockers(durable["blockers"], DURABILITY_BLOCKERS, stage)
        _nonnegative_int(durable["queries"], stage)
    platform = readiness["platform"]
    if platform is not None:
        ready = _exact_object(platform, PLATFORM_READINESS_KEYS, stage)
        if not isinstance(ready["verified"], bool) or (
            ready["failureCode"] is not None and not isinstance(ready["failureCode"], str)
        ):
            raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
        if not _is_int(ready["revision"]) or not _is_int(ready["pendingRoomCommands"]):
            raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    return readiness


def _validate_owner(value: Any, stage: str) -> dict[str, Any] | None:
    if value is None:
        return None
    owner = _exact_object(value, OWNER_KEYS, stage)
    if not GUID_RE.fullmatch(owner["operationId"] if isinstance(owner["operationId"], str) else "") \
            or not COMMIT_RE.fullmatch(owner["targetCommit"] if isinstance(owner["targetCommit"], str) else "") \
            or not GUID_RE.fullmatch(owner["processInstance"] if isinstance(owner["processInstance"], str) else ""):
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    return owner


def _validate_permit(value: Any, stage: str) -> dict[str, Any] | None:
    if value is None:
        return None
    permit = _exact_object(value, PERMIT_KEYS, stage)
    if not _is_int(permit["protocolVersion"]) or permit["protocolVersion"] != PROTOCOL_VERSION \
            or not GUID_RE.fullmatch(permit["operationId"] if isinstance(permit["operationId"], str) else "") \
            or not COMMIT_RE.fullmatch(permit["targetCommit"] if isinstance(permit["targetCommit"], str) else "") \
            or not GUID_RE.fullmatch(permit["processInstance"] if isinstance(permit["processInstance"], str) else "") \
            or not COMMIT_RE.fullmatch(permit["activeCommit"] if isinstance(permit["activeCommit"], str) else "") \
            or not GUID_RE.fullmatch(permit["sealId"] if isinstance(permit["sealId"], str) else ""):
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    _nonnegative_int(permit["epoch"], stage)
    return permit


def _validate_control(value: Any, stage: str) -> dict[str, Any]:
    response = _exact_object(value, CONTROL_KEYS, stage)
    if not _is_int(response["protocolVersion"]) or response["protocolVersion"] != PROTOCOL_VERSION \
            or not isinstance(response["code"], str) or response["code"] not in CONTROL_CODES \
            or not isinstance(response["phase"], str) \
            or response["phase"] not in ("open", "draining", "sealing", "sealed") \
            or not GUID_RE.fullmatch(response["processInstance"] if isinstance(response["processInstance"], str) else "") \
            or not COMMIT_RE.fullmatch(response["activeCommit"] if isinstance(response["activeCommit"], str) else ""):
        raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    _nonnegative_int(response["epoch"], stage)
    _nonnegative_int(response["admissionLeases"], stage)
    _nonnegative_int(response["activityLeases"], stage)
    for name in ("fenceSynchronized", "fenceUnknown", "stopConsumed", "sealReady", "stopPermitted"):
        if not isinstance(response[name], bool):
            raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
    _validate_owner(response["owner"], stage)
    _validate_permit(response["permit"], stage)
    _validate_readiness(response["readiness"], stage)
    return response


def _all_readiness_clear(readiness: dict[str, Any]) -> bool:
    rooms = readiness["rooms"]
    durability = readiness["durability"]
    platform = readiness["platform"]
    return rooms is not None and durability is not None and platform is not None \
        and rooms["verified"] is True and all(value == 0 for value in rooms["blockers"].values()) \
        and durability["verified"] is True and durability["failureCode"] == "none" \
        and durability["queries"] > 0 \
        and all(value == 0 for value in durability["blockers"].values()) \
        and platform["verified"] is True and platform["revision"] >= 0 \
        and platform["pendingRoomCommands"] == 0 and platform["failureCode"] is None


class DeploymentDrainConsumer:
    def __init__(self, transport: Any, token: str,
                 monotonic: Callable[[], float] = time.monotonic,
                 sleep: Callable[[float], None] = time.sleep,
                 wall_clock: Callable[[], float] = time.time):
        self._transport = transport
        self._token = token
        self._monotonic = monotonic
        self._sleep = sleep
        self._wall_clock = wall_clock

    def _request(self, method: str, endpoint: str, stage: str,
                 body: dict[str, Any] | None = None) -> tuple[int, dict[str, Any]]:
        status, value = self._transport.request(method, f"{CONTROL_ROOT}/{endpoint}", self._token, body)
        if not _is_int(status) or status < 100 or status > 599:
            raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
        if set(value) == ERROR_KEYS:
            if not all(isinstance(value[name], str) for name in ERROR_KEYS):
                raise ConsumerFailure("RESPONSE_CONTRACT_REJECTED", stage, "unknown")
            raise ConsumerFailure(ERROR_CODE_MAP.get(value["code"], "HTTP_REJECTED"), stage)
        return status, _validate_control(value, stage)

    def _status(self, stage: str) -> dict[str, Any]:
        status, response = self._request("GET", "status", stage)
        if status != 200 or response["code"] != "status":
            raise ConsumerFailure("STATUS_REJECTED", stage, "unknown")
        return response

    @staticmethod
    def _owner(binding: Binding, process_instance: str) -> dict[str, str]:
        return {
            "operationId": binding.operation_id,
            "targetCommit": binding.target_commit,
            "processInstance": process_instance,
        }

    @staticmethod
    def _matches_owner(state: dict[str, Any], owner: dict[str, str]) -> bool:
        return state["owner"] == owner and state["processInstance"] == owner["processInstance"]

    @staticmethod
    def _require_open(state: dict[str, Any], expected_active_commit: str, stage: str) -> None:
        if state["phase"] != "open" or state["owner"] is not None \
                or state["activeCommit"] != expected_active_commit \
                or not state["fenceSynchronized"] or state["fenceUnknown"] \
                or state["stopConsumed"] or state["sealReady"] or state["stopPermitted"] \
                or state["permit"] is not None:
            raise ConsumerFailure("PREEXISTING_OR_UNKNOWN_FENCE", stage, "unknown")

    @staticmethod
    def _require_same_closed(state: dict[str, Any], owner: dict[str, str],
                             expected_active_commit: str, stage: str) -> None:
        if state["phase"] not in ("draining", "sealing", "sealed") \
                or not DeploymentDrainConsumer._matches_owner(state, owner) \
                or state["activeCommit"] != expected_active_commit \
                or not state["fenceSynchronized"] or state["fenceUnknown"]:
            raise ConsumerFailure("OWNER_OR_STATE_MISMATCH", stage, "unknown")

    def _cancel_before_stop(self, owner: dict[str, str], expected_active_commit: str) -> bool:
        try:
            state = self._status("cancel-reconcile")
            if state["phase"] == "open":
                self._require_open(state, expected_active_commit, "cancel-reconcile")
                return True
            self._require_same_closed(state, owner, expected_active_commit, "cancel-reconcile")
            if state["stopConsumed"]:
                return False
            try:
                status, result = self._request("POST", "cancel", "cancel-before-stop", owner)
            except TransportFailure:
                result = self._status("cancel-reconcile")
                status = 200 if result["phase"] == "open" else 0
            if status == 200 and result["phase"] == "open":
                self._require_open(result, expected_active_commit, "cancel-before-stop")
                return True
            return False
        except (ConsumerFailure, TransportFailure):
            return False

    def acquire_stop(self, binding: Binding, expected_active_commit: str,
                     wait_seconds: int, poll_milliseconds: int,
                     deadline_epoch: int) -> dict[str, Any]:
        stop_cutoff = deadline_epoch - RECOVERY_RESERVE_SECONDS
        if self._wall_clock() >= stop_cutoff:
            raise ConsumerFailure("DEPLOYMENT_DEADLINE_EXCEEDED", "initial-deadline")
        initial = self._status("initial-status")
        self._require_open(initial, expected_active_commit, "initial-status")
        owner = self._owner(binding, initial["processInstance"])
        begun = False
        consumed = False
        try:
            for attempt in range(2):
                try:
                    status, begin = self._request("POST", "begin", "begin", owner)
                except TransportFailure:
                    observed = self._status("begin-reconcile")
                    if observed["phase"] == "open":
                        self._require_open(observed, expected_active_commit, "begin-reconcile")
                        if attempt == 0:
                            continue
                    else:
                        self._require_same_closed(observed, owner, expected_active_commit, "begin-reconcile")
                        if not observed["stopConsumed"]:
                            begun = True
                            break
                    raise ConsumerFailure("BEGIN_STATE_UNKNOWN", "begin", "unknown")
                except ConsumerFailure as begin_failure:
                    # Even a malformed/error response to a mutating request is
                    # ambiguous until a separate strict status read proves Open.
                    try:
                        observed = self._status("begin-reconcile")
                    except (ConsumerFailure, TransportFailure):
                        raise begin_failure.with_disposition("unknown") from None
                    if observed["phase"] == "open":
                        self._require_open(observed, expected_active_commit, "begin-reconcile")
                        raise begin_failure
                    self._require_same_closed(observed, owner, expected_active_commit, "begin-reconcile")
                    begun = True
                    consumed = observed["stopConsumed"]
                    raise begin_failure.with_disposition("closed") from None
                if status != 200 or begin["code"] not in ("Applied", "Idempotent"):
                    try:
                        observed = self._status("begin-reconcile")
                    except (ConsumerFailure, TransportFailure):
                        raise ConsumerFailure("BEGIN_STATE_UNKNOWN", "begin", "unknown") from None
                    if observed["phase"] == "open":
                        self._require_open(observed, expected_active_commit, "begin-reconcile")
                        raise ConsumerFailure("BEGIN_REJECTED", "begin")
                    self._require_same_closed(observed, owner, expected_active_commit,
                                              "begin-reconcile")
                    begun = True
                    consumed = observed["stopConsumed"]
                    raise ConsumerFailure("BEGIN_REJECTED", "begin", "closed")
                self._require_same_closed(begin, owner, expected_active_commit, "begin")
                if begin["phase"] != "draining" or begin["stopConsumed"]:
                    raise ConsumerFailure("BEGIN_REJECTED", "begin", "unknown")
                begun = True
                break
            if not begun:
                raise ConsumerFailure("BEGIN_STATE_UNKNOWN", "begin", "unknown")

            remaining_before_stop = stop_cutoff - self._wall_clock()
            if remaining_before_stop <= 0:
                raise ConsumerFailure("DEPLOYMENT_DEADLINE_EXCEEDED", "seal-deadline", "closed")
            deadline = self._monotonic() + min(wait_seconds, remaining_before_stop)
            permit: dict[str, Any] | None = None
            while self._monotonic() <= deadline:
                try:
                    status, seal = self._request("POST", "seal", "seal", owner)
                except TransportFailure:
                    observed = self._status("seal-reconcile")
                    self._require_same_closed(observed, owner, expected_active_commit, "seal-reconcile")
                    if observed["stopConsumed"]:
                        raise ConsumerFailure("SEAL_STATE_UNKNOWN", "seal", "closed")
                    if self._monotonic() < deadline:
                        self._sleep(poll_milliseconds / 1000)
                        continue
                    break
                self._require_same_closed(seal, owner, expected_active_commit, "seal")
                if status == 200 and seal["code"] in ("Applied", "Idempotent") \
                        and seal["phase"] == "sealed" and seal["permit"] is not None \
                        and seal["sealReady"] and not seal["stopPermitted"] \
                        and not seal["stopConsumed"] and seal["admissionLeases"] == 0 \
                        and seal["activityLeases"] == 0:
                    if seal["code"] == "Applied" and not _all_readiness_clear(seal["readiness"]):
                        raise ConsumerFailure("READINESS_CONTRACT_REJECTED", "seal", "closed")
                    permit = seal["permit"]
                    if permit != {
                        "protocolVersion": PROTOCOL_VERSION,
                        "operationId": binding.operation_id,
                        "targetCommit": binding.target_commit,
                        "processInstance": owner["processInstance"],
                        "activeCommit": expected_active_commit,
                        "epoch": seal["epoch"],
                        "sealId": permit["sealId"],
                    }:
                        raise ConsumerFailure("PERMIT_MISMATCH", "seal", "closed")
                    break
                if seal["code"] not in WAITABLE_SEAL_CODES or seal["stopConsumed"]:
                    raise ConsumerFailure("SEAL_REJECTED", "seal", "closed")
                if self._monotonic() < deadline:
                    self._sleep(poll_milliseconds / 1000)
                else:
                    break
            if permit is None:
                raise ConsumerFailure("DRAIN_TIMEOUT", "seal", "closed", retry_safe=True)

            if self._wall_clock() >= stop_cutoff:
                raise ConsumerFailure("DEPLOYMENT_DEADLINE_EXCEEDED", "consume-deadline", "closed")
            for attempt in range(2):
                try:
                    status, result = self._request("POST", "consume", "consume", permit)
                except TransportFailure:
                    observed = self._status("consume-reconcile")
                    self._require_same_closed(observed, owner, expected_active_commit, "consume-reconcile")
                    # Status v1 deliberately does not echo the permit.  The
                    # strongest available response-loss proof is the exact
                    # owner/process/commit/epoch tuple plus StopConsumed.
                    if observed["phase"] == "sealed" and observed["epoch"] == permit["epoch"] \
                            and observed["stopConsumed"]:
                        consumed = True
                        result = observed
                        status = 200
                        break
                    if observed["phase"] == "sealed" and not observed["stopConsumed"] and attempt == 0:
                        continue
                    raise ConsumerFailure("CONSUME_STATE_UNKNOWN", "consume", "closed")
                if status != 200 or result["code"] not in ("Applied", "Idempotent") \
                        or result["phase"] != "sealed" or result["permit"] != permit \
                        or not result["stopConsumed"] or not result["stopPermitted"] \
                        or not result["sealReady"] or result["epoch"] != permit["epoch"]:
                    raise ConsumerFailure("CONSUME_REJECTED", "consume", "closed")
                self._require_same_closed(result, owner, expected_active_commit, "consume")
                consumed = True
                break
            if not consumed:
                raise ConsumerFailure("CONSUME_STATE_UNKNOWN", "consume", "closed")

            return _receipt("acquire-stop", binding, expected_active_commit,
                            owner["processInstance"], permit["epoch"], permit["sealId"], "sealed")
        except ConsumerFailure as failure:
            if begun and not consumed:
                if self._cancel_before_stop(owner, expected_active_commit):
                    raise failure.with_disposition("open", retry_safe=True) from None
                raise ConsumerFailure("CANCEL_FAILED", "cancel-before-stop", "closed") from None
            raise
        except TransportFailure:
            if begun and not consumed and self._cancel_before_stop(owner, expected_active_commit):
                raise ConsumerFailure("TRANSPORT_REJECTED", "protocol", "open", True) from None
            raise ConsumerFailure("TRANSPORT_STATE_UNKNOWN", "protocol", "closed" if begun else "unknown") from None

    def observe_restart(self, binding: Binding, expected_active_commit: str,
                        previous_process_instance: str, previous_epoch: int) -> dict[str, Any]:
        state = self._status("restart-status")
        owner = self._owner(binding, state["processInstance"])
        self._require_same_closed(state, owner, expected_active_commit, "restart-status")
        if state["phase"] != "draining" or state["processInstance"] == previous_process_instance \
                or state["epoch"] <= previous_epoch or state["stopConsumed"] \
                or state["sealReady"] or state["stopPermitted"] or state["permit"] is not None \
                or state["admissionLeases"] != 0 or state["activityLeases"] != 0:
            raise ConsumerFailure("RESTART_STATE_REJECTED", "restart-status", "closed")
        return _receipt("observe-restart", binding, expected_active_commit,
                        state["processInstance"], state["epoch"], None, "draining")

    def cancel_open(self, binding: Binding, expected_active_commit: str,
                    process_instance: str, epoch: int) -> dict[str, Any]:
        owner = self._owner(binding, process_instance)
        state = self._status("pre-open-status")
        self._require_same_closed(state, owner, expected_active_commit, "pre-open-status")
        if state["phase"] != "draining" or state["epoch"] != epoch or state["stopConsumed"] \
                or state["admissionLeases"] != 0 or state["activityLeases"] != 0:
            raise ConsumerFailure("OPEN_PRECONDITION_REJECTED", "pre-open-status", "closed")
        try:
            status, result = self._request("POST", "cancel", "open", owner)
            if status == 200 and result["code"] in ("Applied", "Idempotent"):
                self._require_open(result, expected_active_commit, "open")
                if result["processInstance"] != process_instance or result["epoch"] <= epoch:
                    raise ConsumerFailure("OPEN_RESPONSE_REJECTED", "open", "unknown")
                return _receipt("cancel-open", binding, expected_active_commit,
                                process_instance, result["epoch"], None, "open")
        except (TransportFailure, ConsumerFailure):
            pass
        try:
            observed = self._status("open-reconcile")
            self._require_open(observed, expected_active_commit, "open-reconcile")
            if observed["processInstance"] != process_instance or observed["epoch"] <= epoch:
                raise ConsumerFailure("OPEN_STATE_UNKNOWN", "open-reconcile", "unknown")
            return _receipt("cancel-open", binding, expected_active_commit,
                            process_instance, observed["epoch"], None, "open")
        except (ConsumerFailure, TransportFailure):
            raise ConsumerFailure("CANCEL_FAILED", "open", "closed") from None


def _receipt(command: str, binding: Binding, active_commit: str, process_instance: str,
             epoch: int, seal_id: str | None, phase: str) -> dict[str, Any]:
    return {
        "schema": 1,
        "ok": True,
        "command": command,
        "protocolVersion": PROTOCOL_VERSION,
        "action": binding.action,
        "packageSha256": binding.package_sha256,
        "operationId": binding.operation_id,
        "targetCommit": binding.target_commit,
        "activeCommit": active_commit,
        "processInstance": process_instance,
        "epoch": epoch,
        "sealId": seal_id,
        "phase": phase,
        "transactionBindingSha256": binding.transaction_sha256(),
        "stateBindingSha256": _state_sha256(
            binding, active_commit, process_instance, epoch, seal_id),
    }


def _read_token() -> str:
    raw = sys.stdin.buffer.read(67)
    if len(raw) > 66:
        raise ConsumerFailure("TOKEN_REJECTED", "token")
    if raw.endswith(b"\r\n"):
        raw = raw[:-2]
    elif raw.endswith(b"\n"):
        raw = raw[:-1]
    try:
        token = raw.decode("ascii", errors="strict")
    except UnicodeDecodeError:
        raise ConsumerFailure("TOKEN_REJECTED", "token") from None
    if not TOKEN_RE.fullmatch(token):
        raise ConsumerFailure("TOKEN_REJECTED", "token")
    return token


def _binding(args: argparse.Namespace) -> Binding:
    action = args.action
    package_sha256 = args.package_sha256
    operation_id = args.operation_id
    target_commit = args.target_commit
    if action not in ("deploy", "rollback") or not SHA256_RE.fullmatch(package_sha256) \
            or not GUID_RE.fullmatch(operation_id) or not COMMIT_RE.fullmatch(target_commit):
        raise ConsumerFailure("ARGUMENT_REJECTED", "arguments")
    return Binding(action, package_sha256, operation_id, target_commit)


def _commit(value: str) -> str:
    if not COMMIT_RE.fullmatch(value):
        raise ConsumerFailure("ARGUMENT_REJECTED", "arguments")
    return value


def _guid(value: str) -> str:
    if not GUID_RE.fullmatch(value):
        raise ConsumerFailure("ARGUMENT_REJECTED", "arguments")
    return value


def _parser() -> argparse.ArgumentParser:
    parser = SafeArgumentParser(add_help=True)
    subparsers = parser.add_subparsers(dest="command", required=True, parser_class=SafeArgumentParser)

    def common(command: str) -> argparse.ArgumentParser:
        item = subparsers.add_parser(command)
        item.add_argument("--action", required=True, choices=("deploy", "rollback"))
        item.add_argument("--package-sha256", required=True)
        item.add_argument("--operation-id", required=True)
        item.add_argument("--target-commit", required=True)
        item.add_argument("--expected-active-commit", required=True)
        return item

    acquire = common("acquire-stop")
    acquire.add_argument("--wait-seconds", required=True, type=int)
    acquire.add_argument("--poll-milliseconds", type=int, default=1000)
    acquire.add_argument("--deadline-epoch", required=True, type=int)

    observe = common("observe-restart")
    observe.add_argument("--previous-process-instance", required=True)
    observe.add_argument("--previous-epoch", required=True, type=int)

    cancel = common("cancel-open")
    cancel.add_argument("--process-instance", required=True)
    cancel.add_argument("--epoch", required=True, type=int)
    return parser


def _failure_json(failure: ConsumerFailure) -> dict[str, Any]:
    return {
        "schema": 1,
        "ok": False,
        "reasonCode": failure.code,
        "failedStage": failure.stage,
        "stateDisposition": failure.disposition,
        "retrySafe": failure.retry_safe,
    }


def main() -> int:
    try:
        args = _parser().parse_args()
        binding = _binding(args)
        expected_active_commit = _commit(args.expected_active_commit)
        if args.command == "acquire-stop":
            if not _is_int(args.wait_seconds) or args.wait_seconds < 1 \
                    or args.wait_seconds > MAX_WAIT_SECONDS \
                    or not _is_int(args.poll_milliseconds) or args.poll_milliseconds < 100 \
                    or args.poll_milliseconds > MAX_POLL_MILLISECONDS \
                    or not _is_int(args.deadline_epoch) or args.deadline_epoch < 1:
                raise ConsumerFailure("ARGUMENT_REJECTED", "arguments")
        elif args.command == "observe-restart":
            _guid(args.previous_process_instance)
            _nonnegative_int(args.previous_epoch, "arguments")
        elif args.command == "cancel-open":
            _guid(args.process_instance)
            _nonnegative_int(args.epoch, "arguments")
        token = _read_token()
        consumer = DeploymentDrainConsumer(LoopbackTransport(), token)
        if args.command == "acquire-stop":
            result = consumer.acquire_stop(binding, expected_active_commit,
                                           args.wait_seconds, args.poll_milliseconds,
                                           args.deadline_epoch)
        elif args.command == "observe-restart":
            result = consumer.observe_restart(binding, expected_active_commit,
                                              args.previous_process_instance, args.previous_epoch)
        else:
            result = consumer.cancel_open(binding, expected_active_commit,
                                          args.process_instance, args.epoch)
        print(json.dumps(result, ensure_ascii=True, sort_keys=True, separators=(",", ":")))
        return 0
    except ConsumerFailure as failure:
        print(json.dumps(_failure_json(failure), ensure_ascii=True,
                         sort_keys=True, separators=(",", ":")))
        return 2
    except (TransportFailure, BrokenPipeError):
        failure = ConsumerFailure("TRANSPORT_STATE_UNKNOWN", "protocol", "unknown")
        print(json.dumps(_failure_json(failure), ensure_ascii=True,
                         sort_keys=True, separators=(",", ":")))
        return 2
    except Exception:
        failure = ConsumerFailure("CONSUMER_REJECTED", "internal", "unknown")
        print(json.dumps(_failure_json(failure), ensure_ascii=True,
                         sort_keys=True, separators=(",", ":")))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
