#!/usr/bin/env bash
set -euo pipefail

# Runs the replay-payload pruner while the application is stopped.  The trap is
# intentional: an unexpected pruning failure must never leave the game service
# unavailable.
readonly service_name="legion12-test.service"
readonly prune_script="/tmp/prune-l12-replay-payloads.sh"
readonly batches="${1:-0}"

[[ "$batches" =~ ^[1-9][0-9]*$ ]] || {
  echo "Usage: $0 <maximum-batches>" >&2
  exit 2
}
[[ -x "$prune_script" ]] || {
  echo "Pruning script is missing or not executable: $prune_script" >&2
  exit 2
}

systemctl stop "$service_name"
trap 'systemctl start "$service_name"' EXIT

"$prune_script" "$batches"
