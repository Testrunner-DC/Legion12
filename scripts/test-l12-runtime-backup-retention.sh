#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

source_file="${1:?actual deployment source required}"
fixture_base="${2:?explicit synthetic output root required}"
[[ "$fixture_base" == /* && "$fixture_base" != / && -d "$fixture_base" && ! -L "$fixture_base" ]] || exit 2
fixture_base="$(readlink -f "$fixture_base")"
owner_root="$(mktemp -d "${fixture_base}/l12-backup-retention-XXXXXX")"
printf 'l12-backup-retention-owned-v1\n' > "$owner_root/OWNED"
passed=0
cleanup() {
  if (( passed == 0 )); then printf 'FAILED synthetic root retained: %s\n' "$owner_root" >&2; return; fi
  [[ "$owner_root" == "$fixture_base/"l12-backup-retention-* && "$(readlink -f "$owner_root")" == "$owner_root" \
    && "$(cat "$owner_root/OWNED")" == l12-backup-retention-owned-v1 ]] || return 1
  rm -rf -- "$owner_root"
}
trap cleanup EXIT
helpers="$owner_root/actual-functions.sh"
for name in assert_cleanup_root assert_cleanup_child load_failure_records runtime_backup_pair_verified prune_runtime_backup_groups; do
  awk -v start="$name() {" '
    { sub(/\r$/, "") }
    $0 == start { copying=1; found=1 }
    copying { print }
    copying && $0 == "}" { done=1; exit }
    END { if (!found || !done) exit 42 }
  ' "$source_file" >> "$helpers"
done
source "$helpers"
log() { printf '%s\n' "$*" >> "$owner_root/operations.log"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
assert_exists() { [[ -f "$1" ]] || fail "expected synthetic file $1"; }
assert_count() {
  local count
  count="$(find "$backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.tar.gz' | wc -l)"
  (( count == $1 )) || fail "expected $1 synthetic snapshots, observed $count"
}
seed_case() {
  local name="$1" index commit stamp archive
  case_root="$owner_root/$name"
  mkdir -p "$case_root/backups" "$case_root/failures" "$case_root/payload"
  backup_dir="$case_root/backups"; failure_dir="$case_root/failures"
  prune_runtime_backups_enabled=1; runtime_backup_keep_groups=2; runtime_backup=""
  printf 'synthetic backup payload, no player data\n' > "$case_root/payload/synthetic.txt"
  snapshots=()
  for index in 1 2 3 4 5; do
    printf -v commit '%012x' "$index"
    printf -v stamp '20261001T00000%sZ' "$index"
    archive="$backup_dir/runtime-before-$commit-$stamp.tar.gz"
    tar -czf "$archive" -C "$case_root/payload" .
    (cd "$backup_dir" && sha256sum "$(basename "$archive")" > "$archive.sha256")
    snapshots+=("$archive")
  done
}
ok() { prune_runtime_backup_groups || fail "expected accepted synthetic cleanup in $case_root"; }
refused() { if prune_runtime_backup_groups; then fail "expected refusal in $case_root"; fi; assert_count 5; }
failure_record() {
  printf 'status=failed\nfailedCommit=%040d\nfailureStage=synthetic\ndisposition=preserve\npreviousCommit=%040d\npreviousTarget=/synthetic\nruntime=/synthetic-runtime\nruntimeBackup=%s\nrecordedAt=2026-10-21T00:00:00Z\n' \
    1 2 "$1" > "$failure_dir/deploy-000000000001-20261001T000001Z.txt"
}
case_passed() { printf 'PASS %s\n' "$1"; }

seed_case default-off; prune_runtime_backups_enabled=0
printf partial > "$backup_dir/runtime-before-synthetic.partial"
ok; assert_count 5; assert_exists "$backup_dir/runtime-before-synthetic.partial"; case_passed default-off

seed_case bounded-one; ok; assert_count 4
[[ ! -e "${snapshots[0]}" && ! -e "${snapshots[0]}.sha256" ]] || fail 'oldest pair not removed'
assert_exists "${snapshots[3]}"; assert_exists "${snapshots[4]}"; case_passed bounded-one-and-newest-two

seed_case pinned; touch "${snapshots[0]}.PINNED"; ok; assert_count 4
assert_exists "${snapshots[0]}"; [[ ! -e "${snapshots[1]}" ]] || fail 'pinned candidate was not skipped'; case_passed per-file-pin

seed_case global-pin; touch "$backup_dir/PINNED"; ok; assert_count 5; case_passed global-pin
seed_case linked-pin; ln -s "$case_root/missing" "$backup_dir/PINNED"; ok; assert_count 5; case_passed dangling-global-pin

seed_case failure-bound; failure_record "${snapshots[0]}"; ok; assert_count 4
assert_exists "${snapshots[0]}"; case_passed exact-failure-binding
seed_case malformed-failure; printf partial > "$failure_dir/unknown.txt"; refused; case_passed unknown-failure-preserves-all
seed_case escaped-failure; failure_record "$case_root/elsewhere.tar.gz"; refused; case_passed escaped-failure-binding

seed_case bad-newest-hash; printf bad >> "${snapshots[4]}"; refused; case_passed bad-newest-preserves-all
seed_case bad-oldest-hash; printf bad >> "${snapshots[0]}"; refused; case_passed bad-oldest-preserves-all
seed_case wrong-sidecar-target; cp "${snapshots[1]}.sha256" "${snapshots[0]}.sha256"; refused; case_passed sidecar-must-name-exact-archive
seed_case multiline-sidecar; cat "${snapshots[1]}.sha256" >> "${snapshots[0]}.sha256"; refused; case_passed multiline-sidecar
seed_case oversized-sidecar; printf '%0600d\n' 1 > "${snapshots[0]}.sha256"; refused; case_passed oversized-sidecar
seed_case invalid-gzip; printf 'not an archive' > "${snapshots[0]}"
(cd "$backup_dir" && sha256sum "$(basename "${snapshots[0]}")" > "${snapshots[0]}.sha256")
refused; case_passed hash-alone-not-archive-proof

seed_case linked-sidecar; mv "${snapshots[0]}.sha256" "$case_root/real-sidecar"
ln -s "$case_root/real-sidecar" "${snapshots[0]}.sha256"; refused; case_passed linked-sidecar
seed_case linked-archive; mv "${snapshots[0]}" "$case_root/outside-archive"
ln -s "$case_root/outside-archive" "${snapshots[0]}"; ok
[[ -L "${snapshots[0]}" ]] || fail 'archive link changed'; assert_exists "$case_root/outside-archive"; case_passed linked-archive-untouched

seed_case unknown-and-partial; printf unknown > "$backup_dir/runtime-before-unknown.tar.gz"
printf partial > "$backup_dir/runtime-before-unknown.partial"; ok
assert_exists "$backup_dir/runtime-before-unknown.tar.gz"; assert_exists "$backup_dir/runtime-before-unknown.partial"; case_passed unknown-and-partial-preserved
seed_case current-backup; runtime_backup="${snapshots[0]}"; ok; assert_exists "${snapshots[0]}"; case_passed current-snapshot-protected
seed_case invalid-keep; runtime_backup_keep_groups=1; refused; case_passed fewer-than-two-refused
seed_case huge-keep; runtime_backup_keep_groups=999999999999999999999; refused; case_passed overflow-retention-refused

seed_case find-failure; find() { return 77; }
if prune_runtime_backup_groups; then fail 'failed enumeration accepted'; fi
unset -f find; assert_count 5; case_passed enumeration-fails-closed
seed_case timeout-failure; timeout() { return 124; }; refused; unset -f timeout; case_passed timeout-fails-closed
seed_case expired-budget
if runtime_backup_pair_verified "${snapshots[0]}" "$SECONDS"; then fail 'expired budget accepted'; fi
assert_count 5; case_passed exhausted-budget
seed_case timestamp-order; touch -d '2030-01-01' "${snapshots[0]}"; ok
[[ ! -e "${snapshots[0]}" ]] || fail 'mtime overrode embedded backup timestamp'; case_passed timestamp-not-mtime

native_timeout=0
timeout 0.05s sh -c 'sleep 1' || native_timeout=$?
(( native_timeout == 124 )) || fail 'native timeout did not enforce a real child deadline'
case_passed native-timeout-deadline

seed_case plan-default-off; prune_runtime_backups_enabled=0
prune_runtime_backup_groups plan || fail 'read-only plan failed while deletion disabled'
assert_count 5
for snapshot in "${snapshots[@]}"; do assert_exists "$snapshot.sha256"; done
grep -Fq "eligible one pair=${snapshots[0]}; deletion=0" "$owner_root/operations.log" || fail 'plan did not identify the oldest verified pair'
case_passed plan-is-read-only-and-keeps-default-off
seed_case plan-failure-bound; failure_record "${snapshots[0]}"
prune_runtime_backup_groups plan || fail 'protected plan failed'
assert_count 5
grep -Fq "eligible one pair=${snapshots[1]}; deletion=0" "$owner_root/operations.log" || fail 'plan did not preserve exact failure binding'
case_passed plan-preserves-failure-binding
seed_case plan-corrupt-retained; printf bad >> "${snapshots[4]}"
if prune_runtime_backup_groups plan; then fail 'plan accepted corrupt retained snapshot'; fi
assert_count 5; case_passed plan-refuses-corrupt-retained-group
seed_case plan-invalid-action
if prune_runtime_backup_groups unknown; then fail 'unknown action accepted'; fi
assert_count 5; case_passed plan-refuses-unknown-action

seed_case plan-cli
cli_root="$owner_root/l12-testrun-deploy-behavior-plan"
mkdir -p "$cli_root/opt/legion12-testrun-deployment" "$cli_root/run/lock"
cp -a "$backup_dir" "$cli_root/opt/legion12-testrun-deployment/runtime-backups"
mkdir "$cli_root/opt/legion12-testrun-deployment/failures"
cli_backup="$cli_root/opt/legion12-testrun-deployment/runtime-backups"
cli_run() {
  L12_TESTRUN_DEPLOY_TEST_MODE=1 L12_TESTRUN_DEPLOY_TEST_ROOT="$cli_root" \
    L12_TESTRUN_PRUNE_RUNTIME_BACKUPS=0 bash "$source_file" runtime-backup-plan "$@"
}
before="$(find "$cli_backup" -type f -exec sha256sum {} \; | LC_ALL=C sort)"
cli_run > "$case_root/plan.log"
[[ "$(find "$cli_backup" -type f -exec sha256sum {} \; | LC_ALL=C sort)" == "$before" ]] || fail 'CLI plan modified a backup or sidecar'
grep -Fq 'deletion=0' "$case_root/plan.log" || fail 'CLI plan did not disclose no deletion'
case_passed cli-plan-preserves-all-backup-bytes
if cli_run extra > "$case_root/extra.log" 2>&1; then fail 'CLI accepted extra arguments'; fi
case_passed cli-plan-refuses-extra-arguments
exec 8>>"$cli_root/run/lock/legion12-testrun-deploy.lock"
flock --nonblock 8 || fail 'cannot own synthetic deployment lock'
if cli_run > "$case_root/busy.log" 2>&1; then fail 'CLI plan ran during deployment lock'; fi
grep -Fq 'deployment is active' "$case_root/busy.log" || fail 'CLI lock refusal not explicit'
exec 8>&-
case_passed cli-plan-does-not-race-a-deployment
printf bad >> "$cli_backup/$(basename "${snapshots[4]}")"
if cli_run > "$case_root/corrupt.log" 2>&1; then fail 'CLI accepted a corrupt retained group'; fi
[[ "$(find "$cli_backup" -type f -name '*.tar.gz' | wc -l)" == 5 ]] || fail 'CLI removed a backup after failed proof'
case_passed cli-plan-refuses-corrupt-proof-without-deletion

passed=1
printf 'PASS all 33 synthetic retention cases; no service, real backup, database or player data accessed\n'
