#!/bin/bash
# One-time backfill: adjudicate ranked recovery quarantine rows that predate the
# auto-adjudication policy so they stop blocking season cutover and deployment readiness.
# Idempotent; reversible by deleting the adjudication rows it writes.
set -uo pipefail
DB=${DB:-/opt/legion12-runtime/matches.db}
OUT=${OUT:-/opt/legion12-deployment}
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
LOG=/var/log/l12-recovery-adjudication-backfill-$STAMP.log
RECEIPT=$OUT/recovery-adjudication-backfill-$STAMP.json
exec >>"$LOG" 2>&1
echo "=== backfill start $(date -u +%FT%TZ) db=$DB"
case "$(readlink -f "$DB")" in
  /opt/legion12-runtime/matches.db) ;;
  *) echo "ABORT unexpected db path"; exit 2 ;;
esac
if ! systemctl is-active --quiet legion12-test.service; then echo 'ABORT service not active'; exit 3; fi
sqlite3 "$DB" "CREATE TABLE IF NOT EXISTS ranked_recovery_adjudications (
  match_id TEXT PRIMARY KEY, decision TEXT NOT NULL, reason TEXT NOT NULL,
  source TEXT NOT NULL, policy_version INTEGER NOT NULL, decided_utc TEXT NOT NULL);"
BEFORE=$(sqlite3 -readonly "$DB" "SELECT COUNT(*) FROM ranked_recovery_quarantine q WHERE NOT EXISTS(SELECT 1 FROM ranked_recovery_adjudications a WHERE a.match_id=q.match_id);")
echo "pending_quarantine_rows=$BEFORE"
sqlite3 "$DB" "INSERT INTO ranked_recovery_adjudications(match_id,decision,reason,source,policy_version,decided_utc)
  SELECT q.match_id,'voided',substr(q.reason,1,240),'auto-backfill',1,strftime('%Y-%m-%dT%H:%M:%f+00:00','now')
  FROM ranked_recovery_quarantine q
  WHERE NOT EXISTS(SELECT 1 FROM ranked_recovery_adjudications a WHERE a.match_id=q.match_id)
  ON CONFLICT(match_id) DO NOTHING;"
AFTER=$(sqlite3 -readonly "$DB" "SELECT COUNT(*) FROM ranked_recovery_quarantine q WHERE NOT EXISTS(SELECT 1 FROM ranked_recovery_adjudications a WHERE a.match_id=q.match_id);")
ADJ=$(sqlite3 -readonly "$DB" "SELECT COUNT(*) FROM ranked_recovery_adjudications;")
INTEG=$(sqlite3 -readonly "$DB" 'PRAGMA integrity_check;' | head -1)
echo "after_pending=$AFTER adjudications=$ADJ integrity=$INTEG"
cat > "$RECEIPT" <<JSON
{
  "schema": 1,
  "ranUtc": "$STAMP",
  "database": "$DB",
  "policy": "incompatible ranked recovery is voided on arrival; adjudication rows are the audit and the readiness exclusion key",
  "pendingQuarantineBefore": $BEFORE,
  "pendingQuarantineAfter": $AFTER,
  "adjudicationRows": $ADJ,
  "source": "auto-backfill",
  "integrityCheck": "$INTEG",
  "reversibleBy": "DELETE FROM ranked_recovery_adjudications WHERE source='auto-backfill';",
  "log": "$LOG"
}
JSON
python3 -m json.tool "$RECEIPT" > /dev/null && echo receipt-valid
echo "=== backfill done $(date -u +%FT%TZ)"