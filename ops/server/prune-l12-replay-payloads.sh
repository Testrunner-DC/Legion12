#!/usr/bin/env bash
set -euo pipefail

# One-off operator tool for retired Bug-reference replay payloads. It deliberately
# keeps match summaries, deck/card facts, command journals and ranked settlement data.
# Stop the service before invoking it; batches stay small to avoid a large SQLite WAL.
readonly database="/opt/legion12-runtime/matches.db"
readonly batch_size=10
readonly maximum_batches="${1:-0}"

[[ "$maximum_batches" =~ ^[1-9][0-9]*$ ]] || {
  echo "Usage: $0 <maximum-batches>" >&2
  exit 2
}
[[ -f "$database" && ! -L "$database" ]] || {
  echo "Refusing to use a missing or linked database: $database" >&2
  exit 2
}

total=0
for ((batch = 1; batch <= maximum_batches; batch++)); do
  output="$(sqlite3 -batch -noheader "$database" <<SQL
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS player_replay_payload_expirations (
  match_id TEXT PRIMARY KEY,
  expired_utc TEXT NOT NULL,
  retained_command_count INTEGER NOT NULL,
  cleared_payload_bytes INTEGER NOT NULL,
  FOREIGN KEY(match_id) REFERENCES matches(match_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_player_replay_payload_expirations_utc
  ON player_replay_payload_expirations(expired_utc,match_id);
BEGIN IMMEDIATE;
CREATE TEMP TABLE l12_replay_payload_prune(match_id TEXT PRIMARY KEY);
INSERT INTO l12_replay_payload_prune(match_id)
SELECT m.match_id
FROM matches m
WHERE m.ended_utc IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM player_replay_payload_expirations x WHERE x.match_id=m.match_id)
  AND NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=m.match_id)
  AND NOT EXISTS(SELECT 1 FROM ranked_match_runtime r WHERE r.match_id=m.match_id)
  AND NOT EXISTS(SELECT 1 FROM ranked_recovery_quarantine q WHERE q.match_id=m.match_id)
ORDER BY julianday(m.ended_utc),m.ended_utc,m.match_id
LIMIT $batch_size;
SELECT count(*) FROM l12_replay_payload_prune;
UPDATE matches SET initial_state_json=NULL
WHERE match_id IN (SELECT match_id FROM l12_replay_payload_prune);
UPDATE match_events SET state_json='{}'
WHERE match_id IN (SELECT match_id FROM l12_replay_payload_prune) AND state_json<>'{}';
DELETE FROM match_action_requests
WHERE match_id IN (SELECT match_id FROM l12_replay_payload_prune);
DELETE FROM match_action_events
WHERE match_id IN (SELECT match_id FROM l12_replay_payload_prune);
DELETE FROM match_state_checkpoints
WHERE match_id IN (SELECT match_id FROM l12_replay_payload_prune);
INSERT INTO player_replay_payload_expirations(match_id,expired_utc,retained_command_count,cleared_payload_bytes)
SELECT c.match_id,strftime('%Y-%m-%dT%H:%M:%fZ','now'),
       (SELECT count(*) FROM match_events e WHERE e.match_id=c.match_id),0
FROM l12_replay_payload_prune c;
COMMIT;
PRAGMA wal_checkpoint(TRUNCATE);
SQL
)"
  count="$(printf '%s\n' "$output" | head -n 1)"
  [[ "$count" =~ ^[0-9]+$ ]] || { echo "Unexpected SQLite output: $output" >&2; exit 1; }
  printf 'batch=%s;matches=%s\n' "$batch" "$count"
  (( total += count ))
  (( count == 0 )) && break
done
printf 'completed_matches=%s\n' "$total"
