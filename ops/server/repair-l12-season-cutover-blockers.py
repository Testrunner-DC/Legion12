#!/usr/bin/env python3
"""Audit and repair the narrowly identified 2026-10-01 season cutover blockers.

The default mode is read-only. Mutations require --apply, an explicit settlement
decision, exact row counts/fingerprints, a stopped-service acknowledgement, and
a verified SQLite backup created by this process.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
from pathlib import Path
from typing import Any, Iterable


SERVICE_STOPPED_ACK = "I_HAVE_STOPPED_LEGION12"
LEGACY_ERROR = "season-cutover-repair: abandoned legacy record retired"
TOOL_VERSION = "2.0.0"
PRODUCTION_SERVICE = "legion12-test.service"

EVIDENCE_FIELDS = (
    "role", "matchIdentitySha256", "quarantineReason", "quarantineCreatedUtc",
    "modeId", "startedUtc", "endedUtc", "recordedSeasonId", "runtimeStatus",
    "runtimeRoomMatches", "outboxStatus", "attempts", "lastError", "outboxCreatedUtc",
    "appliedUtc", "payloadHash", "payloadVersion", "payloadSeasonId",
    "seasonIdentityState", "eventCount",
)


class RepairRefused(RuntimeError):
    pass


class ReceiptAfterCommitError(RuntimeError):
    pass


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def utc_timestamp(value: str, label: str) -> str:
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise RepairRefused(f"{label} must be an ISO-8601 timestamp with a timezone.") from error
    if parsed.tzinfo is None:
        raise RepairRefused(f"{label} must include a timezone.")
    return parsed.astimezone(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def fingerprint(match_id: str) -> str:
    return hashlib.sha256(match_id.encode("utf-8")).hexdigest()[:12]


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_fingerprints(values: Iterable[str]) -> list[str]:
    result = sorted({value.strip().lower() for value in values if value.strip()})
    if any(len(value) != 12 or any(char not in "0123456789abcdef" for char in value)
           for value in result):
        raise RepairRefused("Every quarantine fingerprint must be 12 lowercase hexadecimal characters.")
    return result


def is_blank(value: Any) -> bool:
    return value is None or not str(value).strip()


def payload_value(payload: dict[str, Any], name: str, default: Any = None) -> Any:
    wanted = name.lower()
    for key, value in payload.items():
        if key.lower() == wanted:
            return value
    return default


def parse_payload(payload_json: str, payload_hash: str, match_id: str) -> dict[str, Any]:
    if hashlib.sha256(payload_json.encode("utf-8")).hexdigest().lower() != payload_hash.lower():
        raise RepairRefused("Settlement payload hash mismatch.")
    try:
        payload = json.loads(payload_json)
    except json.JSONDecodeError as error:
        raise RepairRefused("Settlement payload JSON is invalid.") from error
    if not isinstance(payload, dict):
        raise RepairRefused("Settlement payload must be a JSON object.")
    version = payload_value(payload, "Version")
    payload_match_id = payload_value(payload, "MatchId")
    first_account = payload_value(payload, "FirstAccountId")
    second_account = payload_value(payload, "SecondAccountId")
    winner = payload_value(payload, "Winner")
    started_at = payload_value(payload, "StartedAt")
    ended_at = payload_value(payload, "EndedAt")
    meaningful = payload_value(payload, "MeaningfulCommandCount")
    conclusion = payload_value(payload, "ConclusionKind")
    first_master = payload_value(payload, "FirstMasterId")
    second_master = payload_value(payload, "SecondMasterId")
    first_network = payload_value(payload, "FirstNetworkFingerprint")
    second_network = payload_value(payload, "SecondNetworkFingerprint")
    final_round = payload_value(payload, "FinalRound", 0)
    first_browser = payload_value(payload, "FirstBrowserFingerprint", "")
    second_browser = payload_value(payload, "SecondBrowserFingerprint", "")
    season_id = payload_value(payload, "SeasonId")
    try:
        start = dt.datetime.fromisoformat(str(started_at).replace("Z", "+00:00"))
        end = dt.datetime.fromisoformat(str(ended_at).replace("Z", "+00:00"))
    except (TypeError, ValueError) as error:
        raise RepairRefused("Settlement payload timestamps are invalid.") from error
    if start.tzinfo is None or end.tzinfo is None:
        raise RepairRefused("Settlement payload timestamps must include a timezone.")
    valid_integer = lambda value: isinstance(value, int) and not isinstance(value, bool)
    if version not in (1, 2) or is_blank(payload_match_id) \
            or str(payload_match_id).lower() != match_id.lower() \
            or is_blank(first_account) or is_blank(second_account) \
            or str(first_account).lower() == str(second_account).lower() \
            or (winner is not None and (not valid_integer(winner) or winner not in (0, 1))) \
            or end < start or not valid_integer(meaningful) or meaningful < 0 \
            or not valid_integer(final_round) or final_round < 0 or is_blank(conclusion) \
            or first_master is None or second_master is None \
            or first_network is None or second_network is None \
            or first_browser is None or second_browser is None \
            or (version >= 2 and is_blank(season_id)):
        raise RepairRefused("Settlement payload fails the production ValidateSettlement rules.")
    return {
        "version": version,
        "seasonId": season_id,
        "matchIdentitySha256": hashlib.sha256(str(payload_match_id).encode("utf-8")).hexdigest(),
    }


def load_evidence(path_value: str) -> tuple[dict[str, dict[str, Any]], str]:
    path = Path(path_value)
    if path.is_symlink() or not path.is_file():
        raise RepairRefused("Evidence must be an existing regular non-symbolic-link file.")
    raw = path.read_bytes()
    try:
        document = json.loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise RepairRefused("Evidence JSON is invalid.") from error
    if not isinstance(document, dict) or document.get("schema") != 1 \
            or not isinstance(document.get("records"), list):
        raise RepairRefused("Evidence JSON schema is unsupported.")
    records: dict[str, dict[str, Any]] = {}
    for record in document["records"]:
        if not isinstance(record, dict) or any(field not in record for field in EVIDENCE_FIELDS):
            raise RepairRefused("Evidence record is incomplete.")
        identity_hash = str(record["matchIdentitySha256"]).lower()
        if not re.fullmatch(r"[0-9a-f]{64}", identity_hash):
            raise RepairRefused("Evidence match identity hash is invalid.")
        fingerprint_value = identity_hash[:12]
        if fingerprint_value in records:
            raise RepairRefused("Evidence contains duplicate match fingerprints.")
        if record["role"] not in ("applied", "decision"):
            raise RepairRefused("Evidence role must be applied or decision.")
        normalized = dict(record)
        normalized["matchIdentitySha256"] = identity_hash
        records[fingerprint_value] = normalized
    return records, hashlib.sha256(raw).hexdigest()


def open_database(path: Path, writable: bool) -> sqlite3.Connection:
    mode = "rw" if writable else "ro"
    connection = sqlite3.connect(
        f"file:{path.as_posix()}?mode={mode}", uri=True, timeout=2 if writable else 30
    )
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys=ON")
    connection.execute(f"PRAGMA query_only={'OFF' if writable else 'ON'}")
    return connection


def required_tables(connection: sqlite3.Connection) -> None:
    expected = {
        "matches", "match_events", "ranked_match_runtime",
        "ranked_settlement_outbox", "ranked_recovery_quarantine",
    }
    actual = {
        row[0] for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type='table'"
        )
    }
    missing = sorted(expected - actual)
    if missing:
        raise RepairRefused(f"Database schema is missing required tables: {', '.join(missing)}")


def scalar(connection: sqlite3.Connection, sql: str, parameters: tuple[Any, ...] = ()) -> int:
    return int(connection.execute(sql, parameters).fetchone()[0])


def readiness(connection: sqlite3.Connection) -> dict[str, Any]:
    active = scalar(connection, """
        SELECT COUNT(*) FROM (
            SELECT m.match_id
            FROM matches m
            WHERE m.ended_utc IS NULL
              AND LOWER(TRIM(COALESCE(m.mode_id,'')))
                  NOT IN ('friendly','casual','tournament','sandbox')
            UNION
            SELECT r.match_id
            FROM ranked_match_runtime r
            LEFT JOIN matches m ON m.match_id=r.match_id
            WHERE r.status='active'
              AND (m.match_id IS NULL
                   OR LOWER(TRIM(COALESCE(m.mode_id,''))) NOT IN ('ranked','tournament')
                   OR m.ended_utc IS NOT NULL
                   OR r.room_code<>m.room_code COLLATE NOCASE)
        )
    """)
    pending = scalar(connection,
        "SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='pending'")
    reconciliation = scalar(connection, """
        SELECT COUNT(*) FROM ranked_settlement_outbox
        WHERE status='applied' AND last_error IS NOT NULL
    """)
    quarantined = scalar(connection, """
        SELECT
            (SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='quarantined')
          + (SELECT COUNT(*) FROM ranked_recovery_quarantine)
    """)
    return {
        "activeMatches": active,
        "pendingSettlements": pending,
        "appliedReconciliationFailures": reconciliation,
        "quarantinedSettlements": quarantined,
        "ready": active == pending == reconciliation == quarantined == 0,
    }


LEGACY_PREDICATE = """
    m.ended_utc IS NULL
    AND LOWER(TRIM(COALESCE(m.mode_id,'')))='legacy'
    AND m.account_0 IS NULL AND m.account_1 IS NULL
    AND (m.season_id IS NULL OR TRIM(m.season_id)='')
    AND (m.initial_state_json IS NULL OR m.initial_state_json='')
    AND julianday(m.started_utc) < julianday(?)
    AND NOT EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=m.match_id)
    AND NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=m.match_id)
    AND NOT EXISTS(SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=m.match_id)
"""


def inspect(connection: sqlite3.Connection, legacy_before: str) -> dict[str, Any]:
    unfinished_legacy = scalar(connection, """
        SELECT COUNT(*) FROM matches
        WHERE ended_utc IS NULL AND LOWER(TRIM(COALESCE(mode_id,'')))='legacy'
    """)
    legacy_rows = connection.execute(
        f"""SELECT m.match_id,m.started_utc,
                   EXISTS(SELECT 1 FROM match_events e WHERE e.match_id=m.match_id) AS has_events
            FROM matches m WHERE {LEGACY_PREDICATE} ORDER BY m.started_utc,m.match_id""",
        (legacy_before,),
    ).fetchall()
    quarantine_rows = connection.execute("""
        SELECT q.match_id,q.reason,q.created_utc AS quarantine_created_utc,
               m.room_code AS match_room_code,m.mode_id,m.started_utc,m.ended_utc,m.season_id,
               r.status AS runtime_status,r.room_code AS runtime_room_code,
               o.status AS outbox_status,o.attempts,o.last_error,o.created_utc AS outbox_created_utc,
               o.payload_json,o.payload_hash,o.applied_utc,
               (SELECT COUNT(*) FROM match_events e WHERE e.match_id=q.match_id) AS event_count
        FROM ranked_recovery_quarantine q
        JOIN matches m ON m.match_id=q.match_id
        LEFT JOIN ranked_match_runtime r ON r.match_id=q.match_id
        LEFT JOIN ranked_settlement_outbox o ON o.match_id=q.match_id
        ORDER BY q.created_utc,q.match_id
    """).fetchall()
    quarantines: list[dict[str, Any]] = []
    for row in quarantine_rows:
        if row["payload_json"] is None or row["payload_hash"] is None:
            raise RepairRefused("A quarantined settlement is missing its payload or hash.")
        payload = parse_payload(row["payload_json"], row["payload_hash"], row["match_id"])
        recorded_season = row["season_id"]
        if payload["version"] == 1 and is_blank(recorded_season):
            season_identity_state = "v1-missing-recorded"
        elif payload["version"] == 1:
            season_identity_state = "v1-recorded-backfill"
        elif str(payload["seasonId"]).lower() == str(recorded_season or "").lower():
            season_identity_state = "v2-matched"
        else:
            season_identity_state = "invalid-mismatch"
        quarantines.append({
            "fingerprint": fingerprint(row["match_id"]),
            "matchIdentitySha256": hashlib.sha256(row["match_id"].encode("utf-8")).hexdigest(),
            "quarantineReason": row["reason"],
            "quarantineCreatedUtc": row["quarantine_created_utc"],
            "modeId": row["mode_id"],
            "startedUtc": row["started_utc"],
            "endedUtc": row["ended_utc"],
            "recordedSeasonId": row["season_id"],
            "runtimeStatus": row["runtime_status"],
            "runtimeRoomMatches": row["runtime_room_code"] is None or str(
                row["runtime_room_code"]
            ).lower() == str(row["match_room_code"]).lower(),
            "outboxStatus": row["outbox_status"],
            "attempts": row["attempts"],
            "lastError": row["last_error"],
            "outboxCreatedUtc": row["outbox_created_utc"],
            "appliedUtc": row["applied_utc"],
            "eventCount": row["event_count"],
            "payloadHash": str(row["payload_hash"]).lower(),
            "payloadVersion": payload["version"],
            "payloadSeasonId": payload["seasonId"],
            "seasonIdentityState": season_identity_state,
        })
    return {
        "readiness": readiness(connection),
        "legacy": {
            "unfinishedTotal": unfinished_legacy,
            "candidateCount": len(legacy_rows),
            "withEvents": sum(int(row["has_events"]) for row in legacy_rows),
            "oldestStartedUtc": legacy_rows[0]["started_utc"] if legacy_rows else None,
            "newestStartedUtc": legacy_rows[-1]["started_utc"] if legacy_rows else None,
        },
        "quarantines": quarantines,
    }


def validate_plan(
    state: dict[str, Any],
    expected_legacy_count: int,
    applied_fingerprints: list[str],
    decision_fingerprint: str,
    evidence: dict[str, dict[str, Any]],
    decision: str | None,
    season_id: str | None,
) -> dict[str, Any]:
    if state["legacy"]["unfinishedTotal"] != expected_legacy_count:
        raise RepairRefused(
            f"Expected {expected_legacy_count} unfinished legacy rows, "
            f"found {state['legacy']['unfinishedTotal']}."
        )
    if state["legacy"]["candidateCount"] != expected_legacy_count:
        raise RepairRefused(
            "The unfinished legacy set does not exactly match the safe archival predicate."
        )
    rows = {row["fingerprint"]: row for row in state["quarantines"]}
    expected = sorted(applied_fingerprints + [decision_fingerprint])
    if sorted(rows) != expected:
        raise RepairRefused(
            f"Quarantine fingerprints changed; expected {expected}, found {sorted(rows)}."
        )
    if sorted(evidence) != expected:
        raise RepairRefused("Evidence identities do not match the explicitly selected quarantines.")
    if evidence[decision_fingerprint]["role"] != "decision" \
            or any(evidence[item]["role"] != "applied" for item in applied_fingerprints):
        raise RepairRefused("Evidence roles do not match the adjudication plan.")
    for item, row in rows.items():
        expected_row = evidence[item]
        for field in EVIDENCE_FIELDS:
            if field == "role":
                continue
            if row.get(field) != expected_row[field]:
                raise RepairRefused(
                    f"Quarantine evidence changed for {item}: field {field} no longer matches."
                )
    for item in applied_fingerprints:
        row = rows[item]
        if row["endedUtc"] is None or row["runtimeStatus"] != "completed" \
                or row["outboxStatus"] != "applied" or row["lastError"] is not None \
                or row["seasonIdentityState"] not in ("v1-recorded-backfill", "v2-matched"):
            raise RepairRefused(f"Applied quarantine {item} no longer has the verified terminal state.")
    target = rows[decision_fingerprint]
    if target["endedUtc"] is None or target["outboxStatus"] != "quarantined" \
            or target["runtimeStatus"] is not None \
            or target["seasonIdentityState"] != "v1-missing-recorded":
        raise RepairRefused("The adjudication target no longer has the verified isolated state.")
    if target["recordedSeasonId"] not in (None, ""):
        raise RepairRefused("The adjudication target unexpectedly already has a recorded season.")
    if decision == "settle":
        if not season_id or target["payloadVersion"] != 1 or not is_blank(target["payloadSeasonId"]):
            raise RepairRefused(
                "Settlement repair requires an explicit season id and a valid version-1 payload "
                "without an embedded season."
            )
    return rows


def verify_service_stopped(database: Path, service_name: str) -> dict[str, Any]:
    if os.name == "posix":
        if service_name != PRODUCTION_SERVICE:
            raise RepairRefused(f"Linux repair only accepts service {PRODUCTION_SERVICE}.")
        systemctl = shutil.which("systemctl")
        if not systemctl:
            raise RepairRefused("systemctl is required to verify the stopped service.")
        result = subprocess.run(
            [systemctl, "show", service_name, "--property=ActiveState", "--value"],
            check=False, capture_output=True, text=True, timeout=15,
        )
        active_state = result.stdout.strip().lower()
        if result.returncode != 0 or active_state not in ("inactive", "failed"):
            raise RepairRefused(
                f"Service stop verification failed: {service_name} ActiveState={active_state or 'unknown'}."
            )
        targets = {
            str(database.resolve()),
            str(Path(str(database) + "-wal").resolve()),
            str(Path(str(database) + "-shm").resolve()),
        }
        holders: list[int] = []
        proc = Path("/proc")
        for process in proc.iterdir():
            if not process.name.isdigit() or int(process.name) == os.getpid():
                continue
            fd_dir = process / "fd"
            try:
                for descriptor in fd_dir.iterdir():
                    try:
                        if str(descriptor.resolve()) in targets:
                            holders.append(int(process.name))
                            break
                    except (FileNotFoundError, PermissionError, OSError):
                        continue
            except (FileNotFoundError, PermissionError, OSError):
                continue
        if holders:
            raise RepairRefused(
                "Database, WAL, or SHM is still open by another process: "
                + ",".join(str(pid) for pid in sorted(set(holders)))
            )
        return {"service": service_name, "activeState": active_state, "openDatabaseProcesses": []}
    fixture_marker = "l12-season-cutover-repair-"
    if not service_name.startswith("L12SeasonCutoverFixture-") \
            or fixture_marker not in str(database.parent):
        raise RepairRefused("The repair tool is only executable on Linux production or an isolated test fixture.")
    return {"service": service_name, "activeState": "fixture-not-installed", "openDatabaseProcesses": []}


def claim_receipt(path_value: str, run_id: str, database: Path) -> Path:
    path_input = Path(path_value)
    parent_input = path_input.parent
    if parent_input.is_symlink() or not parent_input.is_dir():
        raise RepairRefused("Receipt parent must be an existing regular non-symbolic-link directory.")
    if path_input.exists() or path_input.is_symlink():
        raise RepairRefused("Receipt path already exists; refusing to overwrite it.")
    path = path_input.resolve()
    prepared = {
        "schema": 1,
        "status": "prepared",
        "databaseCommitted": False,
        "runId": run_id,
        "database": str(database),
        "warning": "If this remains prepared, inspect the database and snapshot before retrying.",
    }
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    descriptor = os.open(path, flags, 0o600)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(prepared, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        try:
            path.unlink()
        except OSError:
            pass
        raise
    return path


def write_claimed_receipt(path: Path, receipt: dict[str, Any]) -> None:
    with path.open("w", encoding="utf-8") as stream:
        json.dump(receipt, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())


def create_snapshot(database: Path, snapshot_directory: Path, run_id: str) -> tuple[Path, str]:
    if not snapshot_directory.exists() or not snapshot_directory.is_dir() \
            or snapshot_directory.is_symlink():
        raise RepairRefused("Snapshot directory must be an existing regular directory.")
    snapshot = snapshot_directory / f"matches-before-season-cutover-repair-{run_id}.db"
    sidecar = snapshot.with_suffix(snapshot.suffix + ".sha256")
    if snapshot.exists() or snapshot.is_symlink() or sidecar.exists() or sidecar.is_symlink():
        raise RepairRefused("Refusing to overwrite an existing snapshot.")
    source = open_database(database, writable=False)
    try:
        destination = sqlite3.connect(snapshot)
        try:
            source.backup(destination)
            if destination.execute("PRAGMA quick_check").fetchone()[0] != "ok":
                raise RepairRefused("The generated SQLite snapshot failed quick_check.")
        finally:
            destination.close()
    finally:
        source.close()
    digest = sha256_file(snapshot)
    sidecar.write_text(f"{digest}  {snapshot.name}\n", encoding="utf-8")
    return snapshot, digest


def create_resolution_table(connection: sqlite3.Connection) -> None:
    connection.execute("""
        CREATE TABLE IF NOT EXISTS season_cutover_blocker_resolutions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            run_id TEXT NOT NULL,
            match_id TEXT NOT NULL,
            match_fingerprint TEXT NOT NULL,
            match_identity_sha256 TEXT NOT NULL,
            category TEXT NOT NULL,
            decision TEXT NOT NULL,
            previous_reason TEXT,
            previous_quarantine_created_utc TEXT,
            previous_outbox_status TEXT,
            previous_outbox_hash TEXT,
            previous_outbox_error TEXT,
            previous_outbox_attempts INTEGER,
            previous_outbox_applied_utc TEXT,
            payload_version INTEGER,
            payload_season_id TEXT,
            event_count INTEGER NOT NULL,
            runtime_status TEXT,
            expected_season_id TEXT,
            operator TEXT NOT NULL,
            human_reason TEXT NOT NULL,
            tool_version TEXT NOT NULL,
            source_commit TEXT NOT NULL,
            snapshot_sha256 TEXT NOT NULL,
            evidence_sha256 TEXT NOT NULL,
            resolved_utc TEXT NOT NULL,
            UNIQUE(run_id,match_id,category)
        )
    """)


def insert_resolution(
    connection: sqlite3.Connection,
    run_id: str,
    match_id: str,
    category: str,
    decision: str,
    resolved_utc: str,
    operator: str,
    human_reason: str,
    source_commit: str,
    snapshot_sha256: str,
    evidence_sha256: str,
    season_id: str | None = None,
) -> None:
    source = connection.execute("""
        SELECT q.reason,q.created_utc,o.status AS outbox_status,o.payload_hash,o.last_error,
               o.attempts,o.applied_utc,o.payload_json,r.status AS runtime_status,
               (SELECT COUNT(*) FROM match_events e WHERE e.match_id=m.match_id) AS event_count
        FROM matches m
        LEFT JOIN ranked_recovery_quarantine q ON q.match_id=m.match_id
        LEFT JOIN ranked_settlement_outbox o ON o.match_id=m.match_id
        LEFT JOIN ranked_match_runtime r ON r.match_id=m.match_id
        WHERE m.match_id=?
    """, (match_id,)).fetchone()
    payload_version = None
    payload_season_id = None
    if source["payload_json"] is not None:
        payload = json.loads(source["payload_json"])
        payload_version = payload_value(payload, "Version")
        payload_season_id = payload_value(payload, "SeasonId")
    connection.execute("""
        INSERT INTO season_cutover_blocker_resolutions(
            run_id,match_id,match_fingerprint,match_identity_sha256,category,decision,
            previous_reason,previous_quarantine_created_utc,previous_outbox_status,
            previous_outbox_hash,previous_outbox_error,previous_outbox_attempts,
            previous_outbox_applied_utc,payload_version,payload_season_id,event_count,
            runtime_status,expected_season_id,operator,human_reason,tool_version,
            source_commit,snapshot_sha256,evidence_sha256,resolved_utc)
        VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
    """, (
        run_id, match_id, fingerprint(match_id), hashlib.sha256(match_id.encode("utf-8")).hexdigest(),
        category, decision,
        source["reason"], source["created_utc"], source["outbox_status"],
        source["payload_hash"], source["last_error"], source["attempts"],
        source["applied_utc"], payload_version, payload_season_id, source["event_count"],
        source["runtime_status"], season_id, operator, human_reason, TOOL_VERSION,
        source_commit, snapshot_sha256, evidence_sha256, resolved_utc,
    ))


def apply_repairs(
    connection: sqlite3.Connection,
    legacy_before: str,
    expected_legacy_count: int,
    applied_fingerprints: list[str],
    decision_fingerprint: str,
    decision: str,
    season_id: str | None,
    run_id: str,
    resolved_utc: str,
    operator: str,
    human_reason: str,
    source_commit: str,
    snapshot_sha256: str,
    evidence_sha256: str,
) -> None:
    create_resolution_table(connection)
    legacy_ids = [
        row[0] for row in connection.execute(
            f"SELECT m.match_id FROM matches m WHERE {LEGACY_PREDICATE} ORDER BY m.match_id",
            (legacy_before,),
        )
    ]
    if len(legacy_ids) != expected_legacy_count:
        raise RepairRefused("Legacy candidates changed after the transaction lock was acquired.")
    for match_id in legacy_ids:
        insert_resolution(connection, run_id, match_id, "legacy-unfinished",
                          "retire-as-abandoned", resolved_utc, operator, human_reason,
                          source_commit, snapshot_sha256, evidence_sha256)
    placeholders = ",".join("?" for _ in legacy_ids)
    update = connection.execute(f"""
        UPDATE matches
        SET ended_utc=?,
            error=CASE WHEN error IS NULL OR error='' THEN ?
                       ELSE error || '; ' || ? END
        WHERE match_id IN ({placeholders}) AND ended_utc IS NULL
    """, (resolved_utc, LEGACY_ERROR, LEGACY_ERROR, *legacy_ids))
    if update.rowcount != expected_legacy_count:
        raise RepairRefused("Legacy update count changed inside the transaction.")

    quarantine_rows = connection.execute(
        "SELECT match_id FROM ranked_recovery_quarantine ORDER BY match_id"
    ).fetchall()
    by_fingerprint = {fingerprint(row["match_id"]): row["match_id"] for row in quarantine_rows}
    for item in applied_fingerprints:
        match_id = by_fingerprint[item]
        insert_resolution(connection, run_id, match_id, "recovery-quarantine",
                          "retire-applied-settlement", resolved_utc, operator, human_reason,
                          source_commit, snapshot_sha256, evidence_sha256)
        if connection.execute(
            "DELETE FROM ranked_recovery_quarantine WHERE match_id=?", (match_id,)
        ).rowcount != 1:
            raise RepairRefused(f"Applied quarantine {item} changed inside the transaction.")

    target_id = by_fingerprint[decision_fingerprint]
    insert_resolution(connection, run_id, target_id, "recovery-quarantine",
                      f"adjudication-{decision}", resolved_utc, operator, human_reason,
                      source_commit, snapshot_sha256, evidence_sha256, season_id)
    if connection.execute(
        "DELETE FROM ranked_recovery_quarantine WHERE match_id=?", (target_id,)
    ).rowcount != 1:
        raise RepairRefused("The adjudication quarantine changed inside the transaction.")
    if decision == "settle":
        if connection.execute("""
            UPDATE matches SET season_id=?
            WHERE match_id=? AND (season_id IS NULL OR TRIM(season_id)='')
        """, (season_id, target_id)).rowcount != 1:
            raise RepairRefused("The adjudication target season identity changed.")
        if connection.execute("""
            UPDATE ranked_settlement_outbox
            SET status='pending',attempts=0,last_error=NULL,applied_utc=NULL
            WHERE match_id=? AND status='quarantined'
        """, (target_id,)).rowcount != 1:
            raise RepairRefused("The adjudication target outbox state changed.")
    else:
        if connection.execute("""
            UPDATE ranked_settlement_outbox
            SET status='waived',
                last_error='season-cutover-waived: explicit operator adjudication'
            WHERE match_id=? AND status='quarantined'
        """, (target_id,)).rowcount != 1:
            raise RepairRefused("The adjudication target outbox state changed.")


def verify_waive_run(
    connection: sqlite3.Connection,
    run_id: str,
    legacy_before: str,
    expected_legacy_count: int,
    applied_fingerprints: list[str],
    decision_fingerprint: str,
    evidence: dict[str, dict[str, Any]],
    evidence_sha256: str,
) -> dict[str, Any]:
    audit_table = connection.execute("""
        SELECT COUNT(*) FROM sqlite_master
        WHERE type='table' AND name='season_cutover_blocker_resolutions'
    """).fetchone()[0]
    if audit_table != 1:
        raise RepairRefused("Resolution audit table is missing.")
    state = inspect(connection, legacy_before)
    expected_readiness = {
        "activeMatches": 0,
        "pendingSettlements": 0,
        "appliedReconciliationFailures": 0,
        "quarantinedSettlements": 0,
        "ready": True,
    }
    if state["readiness"] != expected_readiness:
        raise RepairRefused(f"Post-waive readiness was unexpected: {state['readiness']}")
    if state["legacy"]["unfinishedTotal"] != 0 or state["quarantines"]:
        raise RepairRefused("Post-waive blockers are not fully drained.")

    audit_rows = connection.execute("""
        SELECT match_id,match_fingerprint,match_identity_sha256,category,decision,
               previous_reason,previous_quarantine_created_utc,previous_outbox_status,
               previous_outbox_hash,previous_outbox_error,previous_outbox_attempts,
               previous_outbox_applied_utc,payload_version,payload_season_id,event_count,
               runtime_status,operator,human_reason,tool_version,source_commit,
               snapshot_sha256,evidence_sha256
        FROM season_cutover_blocker_resolutions WHERE run_id=?
        ORDER BY category,match_fingerprint
    """, (run_id,)).fetchall()
    expected_audit_count = expected_legacy_count + len(applied_fingerprints) + 1
    if len(audit_rows) != expected_audit_count:
        raise RepairRefused(
            f"Expected {expected_audit_count} audit rows for the run, found {len(audit_rows)}."
        )
    common_metadata = {
        (row["operator"], row["human_reason"], row["tool_version"], row["source_commit"],
         row["snapshot_sha256"], row["evidence_sha256"])
        for row in audit_rows
    }
    metadata = next(iter(common_metadata)) if len(common_metadata) == 1 else None
    if metadata is None or any(is_blank(value) for value in metadata) \
            or metadata[2] != TOOL_VERSION \
            or not re.fullmatch(r"[0-9a-f]{40}", metadata[3]) \
            or not re.fullmatch(r"[0-9a-f]{64}", metadata[4]) \
            or metadata[5] != evidence_sha256:
        raise RepairRefused("Resolution audit operator, reason, tool, commit, or hashes are incomplete.")
    legacy_rows = [row for row in audit_rows if row["category"] == "legacy-unfinished"]
    if len(legacy_rows) != expected_legacy_count \
            or any(row["decision"] != "retire-as-abandoned" for row in legacy_rows):
        raise RepairRefused("Legacy resolution audit evidence is incomplete.")
    quarantine_rows = {
        row["match_fingerprint"]: row for row in audit_rows
        if row["category"] == "recovery-quarantine"
    }
    expected_quarantines = sorted(applied_fingerprints + [decision_fingerprint])
    if sorted(quarantine_rows) != expected_quarantines:
        raise RepairRefused("Quarantine resolution audit fingerprints are incomplete.")
    for item in applied_fingerprints:
        row = quarantine_rows[item]
        expected = evidence[item]
        if row["decision"] != "retire-applied-settlement" \
                or row["match_identity_sha256"] != expected["matchIdentitySha256"] \
                or row["previous_outbox_hash"] != expected["payloadHash"] \
                or row["previous_outbox_status"] != expected["outboxStatus"] \
                or row["previous_outbox_attempts"] != expected["attempts"] \
                or row["previous_outbox_applied_utc"] != expected["appliedUtc"] \
                or row["event_count"] != expected["eventCount"] \
                or row["runtime_status"] != expected["runtimeStatus"]:
            raise RepairRefused(f"Applied quarantine audit decision changed for {item}.")
    target_audit = quarantine_rows[decision_fingerprint]
    target_expected = evidence[decision_fingerprint]
    if target_audit["decision"] != "adjudication-waive" \
            or target_audit["match_identity_sha256"] != target_expected["matchIdentitySha256"] \
            or target_audit["previous_reason"] != target_expected["quarantineReason"] \
            or target_audit["previous_quarantine_created_utc"] != target_expected["quarantineCreatedUtc"] \
            or target_audit["previous_outbox_hash"] != target_expected["payloadHash"] \
            or target_audit["previous_outbox_status"] != target_expected["outboxStatus"] \
            or target_audit["previous_outbox_error"] != target_expected["lastError"] \
            or target_audit["previous_outbox_attempts"] != target_expected["attempts"] \
            or target_audit["previous_outbox_applied_utc"] != target_expected["appliedUtc"] \
            or target_audit["payload_version"] != target_expected["payloadVersion"] \
            or target_audit["payload_season_id"] != target_expected["payloadSeasonId"] \
            or target_audit["event_count"] != target_expected["eventCount"] \
            or target_audit["runtime_status"] != target_expected["runtimeStatus"]:
        raise RepairRefused("Waive adjudication audit evidence is incomplete.")

    target = connection.execute("""
        SELECT m.season_id,o.status,o.last_error,o.payload_hash,
               EXISTS(SELECT 1 FROM ranked_recovery_quarantine q
                      WHERE q.match_id=m.match_id) AS still_quarantined
        FROM season_cutover_blocker_resolutions a
        JOIN matches m ON m.match_id=a.match_id
        JOIN ranked_settlement_outbox o ON o.match_id=a.match_id
        WHERE a.run_id=? AND a.match_fingerprint=?
          AND a.category='recovery-quarantine'
    """, (run_id, decision_fingerprint)).fetchone()
    if target is None or target["status"] != "waived" \
            or target["season_id"] not in (None, "") or target["still_quarantined"] != 0 \
            or target["payload_hash"] != target_audit["previous_outbox_hash"] \
            or target["last_error"] != "season-cutover-waived: explicit operator adjudication":
        raise RepairRefused("Waived outbox evidence or retained payload hash changed.")
    applied_rows = connection.execute("""
        SELECT a.match_fingerprint,o.status,o.last_error,o.payload_hash,r.status AS runtime_status
        FROM season_cutover_blocker_resolutions a
        JOIN ranked_settlement_outbox o ON o.match_id=a.match_id
        LEFT JOIN ranked_match_runtime r ON r.match_id=a.match_id
        WHERE a.run_id=? AND a.decision='retire-applied-settlement'
    """, (run_id,)).fetchall()
    if len(applied_rows) != len(applied_fingerprints) or any(
        row["status"] != "applied" or row["last_error"] is not None
        or row["runtime_status"] != "completed"
        or row["payload_hash"] != evidence[row["match_fingerprint"]]["payloadHash"]
        for row in applied_rows
    ):
        raise RepairRefused("Previously applied settlement evidence changed after waive.")
    return {
        "readiness": state["readiness"],
        "unfinishedLegacy": state["legacy"]["unfinishedTotal"],
        "activeQuarantines": len(state["quarantines"]),
        "auditRows": len(audit_rows),
        "waivedFingerprint": decision_fingerprint,
        "payloadHashPreserved": True,
    }


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", required=True)
    parser.add_argument("--evidence-file", required=True)
    parser.add_argument("--legacy-before", required=True)
    parser.add_argument("--expected-legacy-count", required=True, type=int)
    parser.add_argument("--expected-applied-quarantine-fingerprint", action="append", default=[])
    parser.add_argument("--decision-fingerprint", required=True)
    parser.add_argument("--decision", choices=("settle", "waive"))
    parser.add_argument("--season-id")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--verify-waive-run")
    parser.add_argument("--service-stopped-ack")
    parser.add_argument("--service-name", default=PRODUCTION_SERVICE)
    parser.add_argument("--snapshot-directory")
    parser.add_argument("--receipt")
    parser.add_argument("--operator")
    parser.add_argument("--reason")
    parser.add_argument("--source-commit")
    parser.add_argument("--now", help="Explicit UTC timestamp for deterministic rehearsal.")
    return parser.parse_args()


def main() -> int:
    args = parse_arguments()
    database_input = Path(args.database)
    if database_input.is_symlink():
        raise RepairRefused("Refusing to mutate or inspect a symbolic-link database path.")
    if not database_input.is_file():
        raise RepairRefused("Database must be an existing regular file.")
    database = database_input.resolve()
    if args.expected_legacy_count < 0:
        raise RepairRefused("Expected legacy count cannot be negative.")
    applied = canonical_fingerprints(args.expected_applied_quarantine_fingerprint)
    decision_fingerprint = canonical_fingerprints([args.decision_fingerprint])[0]
    evidence, evidence_sha256 = load_evidence(args.evidence_file)
    if decision_fingerprint in applied:
        raise RepairRefused("The adjudication target cannot also be an applied quarantine.")
    selected = sorted(applied + [decision_fingerprint])
    if sorted(evidence) != selected \
            or evidence[decision_fingerprint]["role"] != "decision" \
            or any(evidence[item]["role"] != "applied" for item in applied):
        raise RepairRefused("Evidence identities and roles do not match the selected quarantines.")
    if args.verify_waive_run:
        if args.apply or args.decision or args.season_id or args.service_stopped_ack \
                or args.snapshot_directory or args.operator or args.reason or args.source_commit:
            raise RepairRefused("--verify-waive-run is a separate read-only mode.")
    elif args.apply:
        if args.decision is None:
            raise RepairRefused("--apply requires an explicit --decision settle or waive.")
        if args.decision == "settle" and not args.season_id:
            raise RepairRefused("--decision settle requires --season-id.")
        if args.decision == "waive" and args.season_id:
            raise RepairRefused("--season-id is only accepted with --decision settle.")
        if args.service_stopped_ack != SERVICE_STOPPED_ACK:
            raise RepairRefused(f"--apply requires --service-stopped-ack {SERVICE_STOPPED_ACK}.")
        if not args.snapshot_directory:
            raise RepairRefused("--apply requires --snapshot-directory.")
        if not args.receipt:
            raise RepairRefused("--apply requires an exclusive --receipt path.")
        if is_blank(args.operator) or len(args.operator.strip()) > 128:
            raise RepairRefused("--apply requires --operator of at most 128 characters.")
        if is_blank(args.reason) or len(args.reason.strip()) > 500:
            raise RepairRefused("--apply requires --reason of at most 500 characters.")
        if not re.fullmatch(r"[0-9a-f]{40}", (args.source_commit or "").lower()):
            raise RepairRefused("--apply requires a 40-character lowercase --source-commit.")
    elif args.service_stopped_ack or args.snapshot_directory \
            or args.operator or args.reason or args.source_commit:
        raise RepairRefused("Apply-only metadata is not accepted in a read-only mode.")

    legacy_before = utc_timestamp(args.legacy_before, "--legacy-before")
    resolved_utc = utc_timestamp(args.now, "--now") if args.now else utc_now()
    run_id = hashlib.sha256(
        (f"{database}|{resolved_utc}|{args.decision or args.verify_waive_run or 'inspect'}|"
         f"{evidence_sha256}").encode("utf-8")
    ).hexdigest()[:20]
    receipt_path = claim_receipt(args.receipt, run_id, database) if args.receipt else None
    database_main_file_sha256_before = sha256_file(database)
    connection = open_database(database, writable=False)
    try:
        required_tables(connection)
        if args.verify_waive_run:
            verified = verify_waive_run(
                connection, args.verify_waive_run, legacy_before,
                args.expected_legacy_count, applied, decision_fingerprint, evidence,
                evidence_sha256,
            )
            receipt = {
                "schema": 1,
                "mode": "verify-waive",
                "verifiedRunId": args.verify_waive_run,
                "recordedAt": resolved_utc,
                "database": str(database),
                "databaseMainFileSha256": database_main_file_sha256_before,
                "evidenceFileSha256": evidence_sha256,
                "verification": verified,
            }
            if receipt_path:
                write_claimed_receipt(receipt_path, receipt)
            print(json.dumps(receipt, ensure_ascii=False, indent=2))
            return 0
        before = inspect(connection, legacy_before)
        validate_plan(before, args.expected_legacy_count, applied, decision_fingerprint, evidence,
                      args.decision, args.season_id)
    finally:
        connection.close()

    snapshot_path = None
    snapshot_sha256 = None
    service_stop_verification = None
    database_committed = False
    after = before
    if args.apply:
        snapshot_directory_input = Path(args.snapshot_directory)
        if snapshot_directory_input.is_symlink():
            raise RepairRefused("Snapshot directory cannot be a symbolic link.")
        service_stop_verification = verify_service_stopped(database, args.service_name)
        connection = open_database(database, writable=True)
        try:
            connection.execute("BEGIN IMMEDIATE")
            service_stop_verification = verify_service_stopped(database, args.service_name)
            locked = inspect(connection, legacy_before)
            validate_plan(locked, args.expected_legacy_count, applied, decision_fingerprint, evidence,
                          args.decision, args.season_id)
            before = locked
            snapshot_path, snapshot_sha256 = create_snapshot(
                database, snapshot_directory_input.resolve(), run_id
            )
            apply_repairs(connection, legacy_before, args.expected_legacy_count, applied,
                          decision_fingerprint, args.decision, args.season_id, run_id, resolved_utc,
                          args.operator.strip(), args.reason.strip(), args.source_commit.lower(),
                          snapshot_sha256, evidence_sha256)
            after = inspect(connection, legacy_before)
            expected_pending = 1 if args.decision == "settle" else 0
            if after["readiness"] != {
                "activeMatches": 0,
                "pendingSettlements": expected_pending,
                "appliedReconciliationFailures": 0,
                "quarantinedSettlements": 0,
                "ready": expected_pending == 0,
            }:
                raise RepairRefused(f"Post-repair readiness was unexpected: {after['readiness']}")
            if after["legacy"]["unfinishedTotal"] != 0 or after["quarantines"]:
                raise RepairRefused("Post-repair blockers did not drain exactly.")
            connection.commit()
            database_committed = True
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    try:
        database_main_file_sha256_after = sha256_file(database)
    except OSError as error:
        if database_committed:
            raise ReceiptAfterCommitError(
                f"DATABASE COMMITTED but post-commit database hash failed: {error}"
            ) from error
        raise
    receipt = {
        "schema": 1,
        "mode": "apply" if args.apply else "dry-run",
        "decision": args.decision,
        "runId": run_id,
        "status": "committed" if args.apply else "observed",
        "databaseCommitted": database_committed,
        "recordedAt": resolved_utc,
        "database": str(database),
        "databaseMainFileSha256Before": database_main_file_sha256_before,
        "databaseMainFileSha256After": database_main_file_sha256_after,
        "logicalPreRepairSnapshotSha256": snapshot_sha256,
        "snapshot": str(snapshot_path) if snapshot_path else None,
        "snapshotSha256": snapshot_sha256,
        "evidenceFileSha256": evidence_sha256,
        "toolVersion": TOOL_VERSION,
        "sourceCommit": args.source_commit.lower() if args.source_commit else None,
        "operator": args.operator.strip() if args.operator else None,
        "reason": args.reason.strip() if args.reason else None,
        "serviceStopVerification": service_stop_verification,
        "before": before,
        "after": after,
        "followUpRequired": (
            "Restart the service, let the repaired pending settlement apply exactly once, then "
            "run a settlement-specific read-only reconciliation check."
            if args.apply and args.decision == "settle" else None
        ),
    }
    if receipt_path:
        try:
            write_claimed_receipt(receipt_path, receipt)
        except OSError as error:
            if database_committed:
                raise ReceiptAfterCommitError(
                    f"DATABASE COMMITTED but final receipt write failed at {receipt_path}: {error}"
                ) from error
            raise
    print(json.dumps(receipt, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ReceiptAfterCommitError as error:
        print(json.dumps({"ok": False, "databaseCommitted": True, "error": str(error)},
                         ensure_ascii=False), file=sys.stderr)
        raise SystemExit(3)
    except (RepairRefused, sqlite3.Error, OSError, ValueError, subprocess.SubprocessError) as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False), file=sys.stderr)
        raise SystemExit(2)
