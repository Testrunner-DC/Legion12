#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly test_root="${L12_TESTRUN_DEPLOY_TEST_ROOT:-}"
if [[ -n "$test_root" ]]; then
  [[ "${L12_TESTRUN_DEPLOY_TEST_MODE:-0}" == "1" && "$test_root" == /* && "$test_root" != "/" && "$test_root" == *"/l12-testrun-deploy-behavior-"* ]] \
    || { printf '[L12 testrun deploy] ERROR: unsafe fixture root\n' >&2; exit 2; }
fi

readonly public_host="legion-12.com"
readonly public_path="/testrun"
readonly active_dir="${test_root}/opt/legion12-testrun"
readonly releases_dir="${test_root}/opt/legion12-testrun-releases"
readonly runtime_dir="${test_root}/opt/legion12-testrun-runtime"
readonly deployment_dir="${test_root}/opt/legion12-testrun-deployment"
readonly incoming_dir="${deployment_dir}/incoming"
readonly backup_dir="${deployment_dir}/runtime-backups"
readonly failure_dir="${deployment_dir}/failures"
readonly static_card_assets_dir="${test_root}/opt/legion12-testrun-static/card-assets"
readonly static_web_assets_dir="${test_root}/opt/legion12-testrun-static/web-assets"
readonly web_assets_entry="${test_root}/opt/legion12-testrun-web-assets"
readonly environment_file="${test_root}/etc/legion12-testrun.env"
readonly nginx_path_snippet="${test_root}/etc/nginx/snippets/legion12-testrun-path.conf"
readonly service_name="legion12-testrun.service"
readonly service_user="legion12"
readonly web_user="www-data"
readonly lock_file="${test_root}/run/lock/legion12-testrun-deploy.lock"
readonly program_budget_bytes=$((512 * 1024 * 1024))
readonly data_budget_bytes=$((256 * 1024 * 1024))
readonly temporary_budget_bytes=$((128 * 1024 * 1024))
readonly total_budget_bytes=$((1024 * 1024 * 1024))
readonly prune_runtime_backups_enabled="${L12_TESTRUN_PRUNE_RUNTIME_BACKUPS:-0}"
readonly runtime_backup_keep_groups="${L12_TESTRUN_RUNTIME_BACKUP_KEEP_GROUPS:-2}"
[[ "$prune_runtime_backups_enabled" =~ ^[01]$ ]] \
  || { printf '[L12 testrun deploy] ERROR: runtime backup pruning flag must be 0 or 1\n' >&2; exit 2; }
[[ "$runtime_backup_keep_groups" =~ ^[1-9][0-9]*$ ]] \
  || { printf '[L12 testrun deploy] ERROR: runtime backup retention must be a positive integer\n' >&2; exit 2; }
readonly web_asset_retention_minutes="${L12_TESTRUN_WEB_ASSET_RETENTION_MINUTES:-2880}"
[[ "$web_asset_retention_minutes" =~ ^[0-9]+$ ]] \
  && (( web_asset_retention_minutes >= 1440 && web_asset_retention_minutes <= 2880 )) \
  || { printf '[L12 testrun deploy] ERROR: web asset retention must be 1440-2880 minutes\n' >&2; exit 2; }
if [[ -n "$test_root" ]]; then
  readonly local_base="${L12_TESTRUN_DEPLOY_LOCAL_BASE:-http://127.0.0.1:8084}"
  readonly public_base="${L12_TESTRUN_DEPLOY_PUBLIC_BASE:-https://${public_host}${public_path}}"
  readonly health_verifier="${L12_TESTRUN_DEPLOY_HEALTH_VERIFIER:-${test_root}/usr/local/libexec/verify-legion12-testrun-health.mjs}"
  readonly health_attempts="${L12_TESTRUN_DEPLOY_HEALTH_ATTEMPTS:-2}"
  readonly health_delay_seconds="${L12_TESTRUN_DEPLOY_HEALTH_DELAY_SECONDS:-0}"
else
  readonly local_base="http://127.0.0.1:8084"
  readonly public_base="https://${public_host}${public_path}"
  readonly health_verifier="/usr/local/libexec/verify-legion12-testrun-health.mjs"
  readonly health_attempts="30"
  readonly health_delay_seconds="1"
fi

log() { printf '[L12 testrun deploy] %s\n' "$*"; }
fail() { printf '[L12 testrun deploy] ERROR: %s\n' "$*" >&2; return 1; }
require_command() { command -v "$1" >/dev/null 2>&1 || fail "missing command: $1"; }

validate_web_assets_tree() {
  local source_root="$1"
  local invalid_path sample
  [[ -d "$source_root" && ! -L "$source_root" ]] || { fail "web asset root is not a regular directory: ${source_root}"; return 1; }
  invalid_path="$(find "$source_root" -mindepth 1 ! -type d ! -type f -print -quit)"
  [[ -z "$invalid_path" ]] || { fail "web assets contain a link or special file: ${invalid_path}"; return 1; }
  sample="$(find "$source_root" -type f -print -quit)"
  [[ -n "$sample" ]] || { fail "web asset root is empty: ${source_root}"; return 1; }
}

validate_relative_web_asset_path() {
  local relative="$1"
  [[ -n "$relative" && "$relative" != /* && "$relative" != *\\* && "$relative" != *"//"* ]] || return 1
  [[ "$relative" != "." && "$relative" != ".." && "/${relative}/" != *"/./"* && "/${relative}/" != *"/../"* ]] || return 1
  if printf '%s' "$relative" | LC_ALL=C grep -q '[[:cntrl:]]'; then return 1; fi
  return 0
}

install_web_assets_tree() {
  local source_root="$1"
  local public_prefix="$2"
  local source relative target target_parent temporary prefix_path component
  local -a prefix_components
  validate_web_assets_tree "$source_root" || return 1
  [[ "$public_prefix" =~ ^[A-Za-z0-9._/-]+$ && "$public_prefix" != /* && "/${public_prefix}/" != *"/../"* ]] || fail "web asset prefix is invalid"
  prefix_path="$static_web_assets_dir"
  [[ -d "$prefix_path" ]] || mkdir -p "$prefix_path"
  chmod 0755 "$prefix_path" || { fail "cannot normalize shared web asset root permissions"; return 1; }
  IFS='/' read -r -a prefix_components <<< "$public_prefix"
  for component in "${prefix_components[@]}"; do
    prefix_path="${prefix_path}/${component}"
    [[ -d "$prefix_path" ]] || mkdir "$prefix_path"
    chmod 0755 "$prefix_path" || { fail "cannot normalize shared web asset prefix permissions: ${public_prefix}"; return 1; }
  done
  [[ -z "$(find "$static_web_assets_dir" -type l -print -quit)" ]] || fail "shared web asset root contains symlinks"
  while IFS= read -r -d '' source; do
    relative="${source#${source_root}/}"
    validate_relative_web_asset_path "$relative" || { fail "web asset path is invalid: ${relative}"; return 1; }
    target="${static_web_assets_dir}/${public_prefix}/${relative}"
    target_parent="${target%/*}"
    [[ -d "$target_parent" ]] || mkdir -p "$target_parent"
    if [[ -e "$target" || -L "$target" ]]; then
      [[ -f "$target" && ! -L "$target" ]] && cmp -s "$source" "$target" || { fail "hashed web asset content conflict: ${public_prefix}/${relative}"; return 1; }
      continue
    fi
    temporary="${target}.install-${short_commit}-$$"
    [[ ! -e "$temporary" && ! -L "$temporary" ]] || fail "web asset temporary path already exists"
    install -m 0644 "$source" "$temporary"
    mv -Tn "$temporary" "$target"
    if [[ -e "$temporary" ]]; then
      [[ -f "$target" && ! -L "$target" ]] && cmp -s "$source" "$target" || { fail "web asset atomic install conflict"; return 1; }
      rm -f -- "$temporary"
    fi
  done < <(find "$source_root" -type f -print0)
  find "${static_web_assets_dir}/${public_prefix}" -type d -exec chmod 0755 {} + \
    || { fail "cannot normalize shared web asset directory permissions: ${public_prefix}"; return 1; }
}

ensure_web_assets_entry() {
  local current_target next_link
  if [[ -L "$web_assets_entry" ]]; then
    current_target="$(readlink -f "$web_assets_entry")"
    [[ "$current_target" == "$static_web_assets_dir" ]] && return 0
  elif [[ -e "$web_assets_entry" ]]; then
    if [[ -n "$test_root" && -d "$web_assets_entry" ]]; then return 0; fi
    if [[ -n "$test_root" && -f "$web_assets_entry" ]]; then
      current_target="$(readlink -f "$web_assets_entry")"
      [[ "$current_target" == "$static_web_assets_dir" ]] && return 0
    fi
    fail "web asset entry is not a managed symlink"
    return 1
  fi
  next_link="${test_root}/opt/.legion12-testrun-web-assets-next-${timestamp}"
  [[ ! -e "$next_link" && ! -L "$next_link" ]] || fail "web asset entry temporary path exists"
  ln -s "$static_web_assets_dir" "$next_link"
  mv -Tf "$next_link" "$web_assets_entry"
}

append_web_asset_keep_set() {
  local release_root="$1"
  local source_relative="$2"
  local public_prefix="$3"
  local source_root source relative
  source_root="${release_root}/${source_relative}"
  validate_web_assets_tree "$source_root" || return 1
  while IFS= read -r -d '' source; do
    relative="${source#${source_root}/}"
    printf '%s\n' "${public_prefix}/${relative}"
  done < <(find "$source_root" -type f -print0)
}

prune_web_assets() {
  local keep_file candidate relative prefix source_relative keep_release
  keep_file="${deployment_dir}/.web-assets-keep-${short_commit}-${timestamp}-$$"
  [[ ! -e "$keep_file" && ! -L "$keep_file" ]] || fail "web asset keep-list path exists"
  : > "$keep_file"
  for keep_release in "$@"; do
    for prefix in assets testrun/assets; do
      if [[ "$prefix" == "assets" ]]; then source_relative="opcgpro-vue/dist/assets"; else source_relative="opcgpro-vue/dist-testrun/assets"; fi
      append_web_asset_keep_set "$keep_release" "$source_relative" "$prefix" >> "$keep_file" || return 1
    done
  done
  sort -u -o "$keep_file" "$keep_file"
  while IFS= read -r -d '' candidate; do
    relative="${candidate#${static_web_assets_dir}/}"
    if ! grep -Fxq "$relative" "$keep_file"; then
      if ! rm -f -- "$candidate"; then rm -f -- "$keep_file"; return 1; fi
    fi
  done < <(find "$static_web_assets_dir" -type f -mmin +"$web_asset_retention_minutes" -print0)
  find "$static_web_assets_dir" -depth -type d -empty ! -path "$static_web_assets_dir" -delete || { rm -f -- "$keep_file"; return 1; }
  rm -f -- "$keep_file"
}

archive_unpacked_bytes() {
  local archive="$1" total
  total="$(tar --numeric-owner -tvzf "$archive" | awk '{ total += $3 } END { printf "%.0f", total }')" \
    || { fail "cannot calculate unpacked archive bytes"; return 1; }
  [[ "$total" =~ ^[0-9]+$ ]] || { fail "cannot calculate unpacked archive bytes"; return 1; }
  printf '%s\n' "$total"
}

release_layout_bytes() {
  local archive="$1"
  python3 - "$archive" <<'PY'
import tarfile
import sys

archive = sys.argv[1]
files = {}
members = {}
with tarfile.open(archive, "r:gz") as bundle:
    for member in bundle.getmembers():
        name = member.name
        while name.startswith("./"):
            name = name[2:]
        name = name.rstrip("/")
        members[name] = member
        if member.isfile():
            files[name] = member.size
    manifest_name = "opcgpro-vue/testrun-shared-files.txt"
    member = members[manifest_name]
    raw_manifest = bundle.extractfile(member).read().decode("utf-8", "strict")

release_logical = sum(files.values())
production_prefix = "opcgpro-vue/dist/assets/"
testrun_prefix = "opcgpro-vue/dist-testrun/assets/"
production_web = sum(size for name, size in files.items() if name.startswith(production_prefix))
testrun_web = sum(size for name, size in files.items() if name.startswith(testrun_prefix))
hydrate_logical = 0
seen = set()
for raw in raw_manifest.splitlines():
    relative = raw.rstrip("\r")
    if (not relative or relative.startswith("/") or "\\" in relative
            or "//" in relative or relative in (".", "..")
            or "/./" in f"/{relative}/" or "/../" in f"/{relative}/"):
        raise SystemExit("unsafe testrun shared-file path in budget projection")
    if relative in seen:
        raise SystemExit("duplicate testrun shared-file path in budget projection")
    seen.add(relative)
    source = "opcgpro-vue/dist/" + relative
    target = "opcgpro-vue/dist-testrun/" + relative
    if source not in files or target in files:
        raise SystemExit("testrun shared-file layout differs from hydrate contract")
    hydrate_logical += files[source]

# The budget is deliberately logical, not allocated blocks. The hydrate target
# is counted again even though ln creates a hard link, and both compatibility
# prefixes are counted because install_web_assets_tree copies each path.
new_web_cache_logical = production_web + testrun_web + hydrate_logical
print(release_logical + hydrate_logical, hydrate_logical, new_web_cache_logical)
PY
}

missing_web_cache_bytes() {
  local source_root="$1" public_prefix="$2"
  python3 - "$source_root" "$static_web_assets_dir" "$public_prefix" <<'PY'
import filecmp
import os
import stat
import sys

source_root, cache_root, public_prefix = sys.argv[1:]
if not os.path.isdir(source_root) or os.path.islink(source_root):
    raise SystemExit("previous web asset root is unsafe")
prefix_parts = public_prefix.split("/")
if (not prefix_parts or any(not part or part in (".", "..") for part in prefix_parts)):
    raise SystemExit("previous web cache prefix is unsafe")

def fail_walk(error):
    raise error

total = 0
saw_file = False
for directory, dirnames, filenames in os.walk(source_root, topdown=True, followlinks=False, onerror=fail_walk):
    for name in list(dirnames) + list(filenames):
        entry = os.path.join(directory, name)
        mode = os.lstat(entry).st_mode
        if stat.S_ISLNK(mode) or not (stat.S_ISDIR(mode) or stat.S_ISREG(mode)):
            raise SystemExit("previous web assets contain a link or special file")
    for name in filenames:
        source = os.path.join(directory, name)
        relative = os.path.relpath(source, source_root)
        components = relative.split(os.sep)
        if (any(not part or part in (".", "..") or "\\" in part
                or any(ord(character) < 32 or ord(character) == 127 for character in part)
                for part in components)):
            raise SystemExit("previous web asset path is invalid")
        saw_file = True
        target = os.path.join(cache_root, *prefix_parts, *components)
        if os.path.lexists(target):
            if os.path.islink(target) or not os.path.isfile(target):
                raise SystemExit("previous web cache target is unsafe")
            if not filecmp.cmp(source, target, shallow=False):
                raise SystemExit("previous web cache target conflicts")
        else:
            total += os.stat(source, follow_symlinks=False).st_size
if not saw_file:
    raise SystemExit("previous web asset root is empty")
print(total)
PY
}

tree_bytes() {
  local root subtotal total=0
  for root in "$@"; do
    [[ -d "$root" && ! -L "$root" ]] || continue
    subtotal="$(find "$root" -type f -printf '%s\n' | awk '{ total += $1 } END { printf "%.0f", total + 0 }')" \
      || { fail "cannot enumerate logical bytes under ${root}"; return 1; }
    [[ "$subtotal" =~ ^[0-9]+$ ]] || { fail "logical-byte scan returned an invalid size for ${root}"; return 1; }
    total=$((total + subtotal))
  done
  printf '%s' "$total"
}

testrun_program_bytes() {
  if [[ -n "$test_root" && -n "${L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES:-}" ]]; then
    printf '%s' "$L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES"
  else
    tree_bytes "$releases_dir" "$static_card_assets_dir" "$static_web_assets_dir"
  fi
}

testrun_data_bytes() {
  if [[ -n "$test_root" && -n "${L12_TESTRUN_DEPLOY_TEST_DATA_BYTES:-}" ]]; then
    printf '%s' "$L12_TESTRUN_DEPLOY_TEST_DATA_BYTES"
  else
    tree_bytes "$runtime_dir" "$backup_dir" "$failure_dir"
  fi
}

testrun_temporary_bytes() {
  if [[ -n "$test_root" && -n "${L12_TESTRUN_DEPLOY_TEST_TEMP_BYTES:-}" ]]; then
    printf '%s' "$L12_TESTRUN_DEPLOY_TEST_TEMP_BYTES"
    return
  fi
  local -a roots=("$incoming_dir")
  local candidate
  while IFS= read -r -d '' candidate; do roots+=("$candidate"); done \
    < <(find "${test_root}/opt" -mindepth 1 -maxdepth 1 -type d \
      \( -name 'legion12-testrun-staging-*' -o -name 'legion12-testrun-card-assets-staging-*' \) -print0)
  tree_bytes "${roots[@]}"
}

validate_testrun_storage_budget() {
  local release_growth="$1" card_growth="$2" previous_web_growth="$3" new_web_growth="$4" runtime_backup_growth="$5"
  local program data temporary program_after data_after temporary_peak total_peak value
  program="$(testrun_program_bytes)" || return 1
  data="$(testrun_data_bytes)" || return 1
  temporary="$(testrun_temporary_bytes)" || return 1
  for value in "$program" "$data" "$temporary" "$release_growth" "$card_growth" "$previous_web_growth" "$new_web_growth" "$runtime_backup_growth"; do
    [[ "$value" =~ ^[0-9]+$ ]] || { fail "testrun storage report contains a non-numeric size"; return 1; }
  done
  program_after=$((program + release_growth + card_growth + previous_web_growth + new_web_growth))
  data_after=$((data + runtime_backup_growth))
  temporary_peak=$((temporary + release_growth + card_growth))
  total_peak=$((program + data + temporary + release_growth + card_growth + previous_web_growth + new_web_growth + runtime_backup_growth))
  log "fixed-path storage report: program=${releases_dir},${static_card_assets_dir},${static_web_assets_dir}; data=${runtime_dir},${backup_dir},${failure_dir}; temporary=${incoming_dir},managed-staging"
  log "storage projection bytes: releaseLogical=${release_growth}; previousWebMissing=${previous_web_growth}; newWebTwoPrefixes=${new_web_growth}; runtimeBackupAllowance=${runtime_backup_growth}"
  log "storage budget bytes: programAfter=${program_after}/${program_budget_bytes}; dataAfter=${data_after}/${data_budget_bytes}; temporaryPeak=${temporary_peak}/${temporary_budget_bytes}; totalPeak=${total_peak}/${total_budget_bytes}; accounting=logical"
  if (( program_after > program_budget_bytes )); then fail "testrun program budget exceeds 512 MiB; no cleanup was attempted"; return 1; fi
  if (( data_after > data_budget_bytes )); then fail "testrun data budget exceeds 256 MiB; runtime and evidence were retained"; return 1; fi
  if (( temporary_peak > temporary_budget_bytes )); then fail "testrun temporary budget exceeds 128 MiB; incoming and evidence were retained"; return 1; fi
  if (( total_peak > total_budget_bytes )); then fail "testrun total storage budget exceeds 1 GiB; no cleanup was attempted"; return 1; fi
}

assert_cleanup_root() {
  local root="$1" canonical
  [[ -d "$root" && ! -L "$root" ]] || { log "storage cleanup skipped unsafe root: ${root}"; return 1; }
  canonical="$(readlink -f "$root")" || return 1
  [[ "$canonical" == "$root" ]] || { log "storage cleanup skipped non-canonical root: ${root}"; return 1; }
}

assert_cleanup_child() {
  local candidate="$1" root="$2" parent
  assert_cleanup_root "$root" || return 1
  [[ "$candidate" == "${root}/"* && "$candidate" != "$root" ]] || return 1
  [[ ! -L "$candidate" ]] || { log "storage cleanup skipped symlink: ${candidate}"; return 1; }
  parent="$(readlink -f "$(dirname "$candidate")")" || return 1
  [[ "$parent" == "$root" || "$parent" == "${root}/"* ]] || return 1
}

load_failure_records() {
  failure_records=()
  if [[ ! -e "$failure_dir" && ! -L "$failure_dir" ]]; then return 0; fi
  assert_cleanup_root "$failure_dir" || { log "storage cleanup refused: failure receipt root is unsafe or unreadable"; return 1; }
  local listing row kind record name failed_commit
  listing="$(find "$failure_dir" -mindepth 1 -maxdepth 1 -printf '%y:%p\n' | sort)" \
    || { log "storage cleanup refused: failure receipt enumeration failed"; return 1; }
  while IFS= read -r row || [[ -n "$row" ]]; do
    [[ -n "$row" ]] || continue
    kind="${row%%:*}"
    record="${row#*:}"
    name="$(basename "$record")" || return 1
    if [[ "$kind" != "f" || ! "$name" =~ ^deploy-[0-9a-f]{12}-[0-9]{8}T[0-9]{6}Z[.]txt$ || ! -f "$record" || -L "$record" || ! -r "$record" ]]; then
      log "storage cleanup refused: failure receipt is partial, unsafe, or unowned: ${record}"
      return 1
    fi
    failed_commit="$(awk -F= '
      BEGIN { required = "status failedCommit failureStage disposition previousCommit previousTarget runtime runtimeBackup recordedAt" }
      {
        if (NF != 2 || $1 !~ /^(status|failedCommit|failureStage|disposition|previousCommit|previousTarget|runtime|runtimeBackup|recordedAt)$/) bad = 1
        count[$1] += 1
        value[$1] = $2
      }
      END {
        required_count = split(required, keys, " ")
        if (NR != required_count || bad) exit 42
        for (receipt_index = 1; receipt_index <= required_count; receipt_index += 1) if (count[keys[receipt_index]] != 1) exit 42
        if (value["status"] != "failed" || length(value["failedCommit"]) != 40 || value["failedCommit"] !~ /^[0-9a-f]+$/) exit 42
        if (value["failureStage"] == "" || value["disposition"] == "" || length(value["previousCommit"]) != 40 || value["previousCommit"] !~ /^[0-9a-f]+$/) exit 42
        if (value["previousTarget"] == "" || value["runtime"] == "" || value["recordedAt"] == "") exit 42
        print value["failedCommit"]
      }
    ' "$record")" || { log "storage cleanup refused: failure receipt is incomplete or cannot be parsed exactly once: ${record}"; return 1; }
    failure_records+=("$record")
  done <<< "$listing"
}

select_retained_releases() {
  local active_target="$1" row candidate marker commit pin record grep_status failure_protected uncertain=0
  local rollback_count=0
  local -a failure_records
  load_failure_records || return 1
  retained_releases=("$active_target")
  assert_cleanup_child "$active_target" "$releases_dir" || return 1
  [[ -d "$active_target" ]] || return 1
  mapfile -t release_rows < <(find "$releases_dir" -mindepth 1 -maxdepth 1 -type d -printf '%T@:%p\n' | sort -rn)
  for row in "${release_rows[@]}"; do
    candidate="${row#*:}"
    [[ "$candidate" == "$active_target" ]] && continue
    pin="${candidate}/.PINNED"
    if [[ -e "$pin" || -L "$pin" ]]; then
      if [[ -f "$pin" && ! -L "$pin" ]]; then
        retained_releases+=("$candidate")
        log "storage cleanup retained explicitly PINNED release: ${candidate}"
        continue
      fi
      log "storage cleanup refused: PINNED marker is unsafe: ${candidate}"
      uncertain=1
      continue
    fi
    marker="${candidate}/.deployment-commit"
    if ! assert_cleanup_child "$candidate" "$releases_dir" || [[ ! -f "$marker" || -L "$marker" ]]; then
      log "storage cleanup refused: release may be incomplete or ownership is unknown: ${candidate}"
      uncertain=1
      continue
    fi
    commit="$(tr -d '\r\n' < "$marker")"
    if [[ ! "$commit" =~ ^[0-9a-f]{40}$ ]]; then
      log "storage cleanup refused: release identity is invalid: ${candidate}"
      uncertain=1
      continue
    fi
    failure_protected=0
    for record in "${failure_records[@]}"; do
      if grep -Fqx "failedCommit=${commit}" "$record"; then
        failure_protected=1
        break
      else
        grep_status=$?
        if [[ "$grep_status" -ne 1 ]]; then
          log "storage cleanup refused: failure receipt read failed during ownership match: ${record}"
          uncertain=1
          break
        fi
      fi
    done
    (( uncertain == 0 )) || continue
    if (( failure_protected == 1 )); then
      retained_releases+=("$candidate")
      log "storage cleanup retained failed release evidence: ${candidate}"
    elif (( rollback_count < 1 )); then
      retained_releases+=("$candidate")
      rollback_count=$((rollback_count + 1))
    fi
  done
  (( uncertain == 0 )) || return 1
  log "storage cleanup retained releases: ${retained_releases[*]}"
}

prune_release_set() {
  local row candidate keep retained marker commit
  for row in "${release_rows[@]}"; do
    candidate="${row#*:}"
    keep=0
    for retained in "${retained_releases[@]}"; do [[ "$candidate" == "$retained" ]] && keep=1; done
    (( keep == 1 )) && continue
    assert_cleanup_child "$candidate" "$releases_dir" || continue
    marker="${candidate}/.deployment-commit"
    [[ -f "$marker" && ! -L "$marker" ]] \
      || { log "storage cleanup skipped unverified release: ${candidate}"; continue; }
    commit="$(tr -d '\r\n' < "$marker")"
    [[ "$commit" =~ ^[0-9a-f]{40}$ ]] \
      || { log "storage cleanup skipped release with invalid identity: ${candidate}"; continue; }
    if ! rm -rf -- "$candidate"; then log "storage cleanup could not remove release; retained: ${candidate}"; continue; fi
    log "storage cleanup removed release: ${candidate}"
  done
}

prune_runtime_backup_groups() {
  local row backup checksum index=0
  local complete_rows=()
  assert_cleanup_root "$backup_dir" || return 1
  mapfile -t backup_rows < <(find "$backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.tar.gz' -printf '%T@:%p\n' | sort -rn)
  if [[ "$prune_runtime_backups_enabled" != "1" ]]; then
    local paired=0
    for row in "${backup_rows[@]}"; do
      backup="${row#*:}"; checksum="${backup}.sha256"
      [[ -f "$checksum" && ! -L "$checksum" ]] && paired=$((paired + 1))
    done
    log "storage cleanup found ${paired} runtime snapshot groups with checksum sidecars; automatic deletion enabled=0, configured keep=${runtime_backup_keep_groups}"
    log "runtime snapshot deletion skipped by default; explicit authorization is required"
    return 0
  fi
  for row in "${backup_rows[@]}"; do
    backup="${row#*:}"; checksum="${backup}.sha256"
    if [[ ! -f "$checksum" || -L "$checksum" ]] || ! (cd "$backup_dir" && sha256sum -c "$(basename "$checksum")" >/dev/null 2>&1); then
      log "storage cleanup kept incomplete or unverifiable runtime snapshot: ${backup}"
      continue
    fi
    complete_rows+=("$row")
  done
  log "storage cleanup found ${#complete_rows[@]} complete runtime snapshot groups; automatic deletion enabled=${prune_runtime_backups_enabled}, configured keep=${runtime_backup_keep_groups}"
  for row in "${complete_rows[@]}"; do
    backup="${row#*:}"; checksum="${backup}.sha256"
    if (( index < runtime_backup_keep_groups )); then
      log "storage cleanup retained runtime snapshot: ${backup}"
    else
      assert_cleanup_child "$backup" "$backup_dir" && assert_cleanup_child "$checksum" "$backup_dir" || continue
      if ! rm -f -- "$backup" "$checksum"; then log "storage cleanup could not remove runtime snapshot; retained: ${backup}"; continue; fi
      log "storage cleanup removed runtime snapshot: ${backup}"
    fi
    index=$((index + 1))
  done
  while IFS= read -r -d '' backup; do
    assert_cleanup_child "$backup" "$backup_dir" || continue
    if ! rm -f -- "$backup"; then log "storage cleanup could not remove snapshot temporary; retained: ${backup}"; continue; fi
    log "storage cleanup removed abandoned snapshot temporary: ${backup}"
  done < <(find "$backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.partial' -print0)
}

prune_card_asset_versions() {
  local release link target candidate retained keep
  local referenced_assets=()
  assert_cleanup_root "$static_card_assets_dir" || return 1
  for release in "${retained_releases[@]}"; do
    link="${release}/opcgpro-vue/dist/card-assets"
    if [[ ! -L "$link" ]]; then
      log "card asset cleanup skipped: retained release has no trustworthy card-assets link: ${release}"
      return 0
    fi
    target="$(readlink -f "$link")"
    if [[ "$target" != "${static_card_assets_dir}/"* || "$target" == "$static_card_assets_dir" || ! -d "$target" || -L "$target" ]]; then
      log "card asset cleanup skipped: retained release reference cannot be proven safe: ${release}"
      return 0
    fi
    validate_card_assets_tree "$target" "$(basename "$target")" || {
      log "card asset cleanup skipped: retained manifest cannot be verified: ${target}"
      return 0
    }
    referenced_assets+=("$target")
  done
  log "storage cleanup retained card assets: ${referenced_assets[*]}"
  while IFS= read -r -d '' candidate; do
    keep=0
    for retained in "${referenced_assets[@]}"; do [[ "$candidate" == "$retained" ]] && keep=1; done
    (( keep == 1 )) && continue
    if [[ ! "$(basename "$candidate")" =~ ^[0-9a-f]{64}$ ]]; then
      log "card asset cleanup skipped unmanaged version: ${candidate}"
      continue
    fi
    assert_cleanup_child "$candidate" "$static_card_assets_dir" || continue
    if ! rm -rf -- "$candidate"; then log "storage cleanup could not remove card assets; retained: ${candidate}"; continue; fi
    log "storage cleanup removed unreferenced card assets: ${candidate}"
  done < <(find "$static_card_assets_dir" -mindepth 1 -maxdepth 1 -type d -print0)
}

prune_consumed_incoming() {
  local candidate name identity release marker proven
  assert_cleanup_root "$incoming_dir" || return 1
  while IFS= read -r -d '' candidate; do
    name="$(basename "$candidate")"; proven=0
    if [[ "$name" =~ ^l12-testrun-release-([0-9a-f]{40})\.tar\.gz$ ]]; then
      identity="${BASH_REMATCH[1]}"
      while IFS= read -r -d '' release; do
        marker="${release}/.deployment-commit"
        [[ -f "$marker" && ! -L "$marker" && "$(tr -d '\r\n' < "$marker")" == "$identity" ]] && proven=1
      done < <(find "$releases_dir" -mindepth 1 -maxdepth 1 -type d -print0)
    elif [[ "$name" =~ ^l12-testrun-card-assets-([0-9a-f]{64})\.tar\.gz$ ]]; then
      identity="${BASH_REMATCH[1]}"
      [[ -d "${static_card_assets_dir}/${identity}" && ! -L "${static_card_assets_dir}/${identity}" ]] && proven=1
    fi
    if (( proven == 1 )); then
      assert_cleanup_child "$candidate" "$incoming_dir" || continue
      if ! rm -f -- "$candidate"; then log "storage cleanup could not remove consumed incoming artifact; retained: ${candidate}"; continue; fi
      log "storage cleanup removed consumed incoming artifact: ${candidate}"
    else
      log "storage cleanup kept unproven incoming artifact: ${candidate}"
    fi
  done < <(find "$incoming_dir" -mindepth 1 -maxdepth 1 -type f -print0)
}

prune_managed_staging() {
  local candidate
  assert_cleanup_root "${test_root}/opt" || return 1
  while IFS= read -r -d '' candidate; do
    assert_cleanup_child "$candidate" "${test_root}/opt" || continue
    if ! rm -rf -- "$candidate"; then log "storage cleanup could not remove staging directory; retained: ${candidate}"; continue; fi
    log "storage cleanup removed abandoned staging directory: ${candidate}"
  done < <(find "${test_root}/opt" -mindepth 1 -maxdepth 1 -type d \( -name 'legion12-testrun-staging-*' -o -name 'legion12-testrun-card-assets-staging-*' \) -print0)
}

converge_testrun_storage() {
  local before after released
  before="$(tree_bytes "$releases_dir" "$backup_dir" "$incoming_dir" "$static_card_assets_dir" "$static_web_assets_dir")" || before=0
  select_retained_releases "$1" || { log "post-deploy storage cleanup skipped: release retention cannot be proven"; return 0; }
  prune_runtime_backup_groups || log "runtime snapshot cleanup skipped"
  prune_card_asset_versions || log "card asset cleanup skipped"
  prune_consumed_incoming || log "incoming cleanup skipped"
  prune_managed_staging || log "staging cleanup skipped"
  if ! prune_web_assets "${retained_releases[@]}"; then log "web asset cleanup skipped; compatibility assets remain available"; fi
  prune_release_set
  after="$(tree_bytes "$releases_dir" "$backup_dir" "$incoming_dir" "$static_card_assets_dir" "$static_web_assets_dir")" || after="$before"
  released=$((before > after ? before - after : 0))
  log "post-deploy storage cleanup: before=${before} bytes, after=${after} bytes, released=${released} bytes"
}

assert_unblocked() {
  local marker="${deployment_dir}/deployment-blocked.txt"
  [[ ! -e "$marker" && ! -L "$marker" ]] || fail "manual reconciliation marker exists: ${marker}"
}

validate_environment() {
  python3 - "$environment_file" "$(if [[ -z "$test_root" ]]; then printf 1; else printf 0; fi)" <<'PY'
import os
import re
import stat
import sys

path, enforce_metadata = sys.argv[1], sys.argv[2] == "1"
allowed = {
    "L12_ADMIN_PASSWORD", "L12_EMAIL_FEATURE_ENABLED", "L12_PUBLIC_BASE_URL",
    "L12_SMTP_HOST", "L12_SMTP_PORT", "L12_SMTP_USERNAME", "L12_SMTP_PASSWORD",
    "L12_SMTP_FROM_ADDRESS", "L12_SMTP_FROM_NAME", "L12_SMTP_ENABLE_SSL",
    "L12_ENABLE_SECOND_APPROVER_BOOTSTRAP", "L12_SECOND_APPROVER_BOOTSTRAP_TOKEN",
    "L12_TESTRUN_MATCH_STORAGE",
}
values = {}
with open(path, "r", encoding="utf-8") as handle:
    for number, raw in enumerate(handle, 1):
        line = raw.rstrip("\r\n")
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise SystemExit(f"invalid environment line {number}")
        key, value = line.split("=", 1)
        if key not in allowed or key in values:
            raise SystemExit(f"unexpected or duplicate environment key: {key}")
        values[key] = value
if set(values) != allowed:
    raise SystemExit("testrun environment keys are incomplete")
if not re.fullmatch(r"[0-9a-f]{64}", values["L12_ADMIN_PASSWORD"]):
    raise SystemExit("testrun admin password is not an independent 64-character hex secret")
if values["L12_EMAIL_FEATURE_ENABLED"] != "false":
    raise SystemExit("email must remain disabled on testrun")
if values["L12_PUBLIC_BASE_URL"] != "https://legion-12.com/testrun":
    raise SystemExit("testrun public base URL is invalid")
for key in values:
    if key.startswith("L12_SMTP_") and values[key] != "":
        raise SystemExit("SMTP values must remain empty on testrun")
if values["L12_ENABLE_SECOND_APPROVER_BOOTSTRAP"] != "false" or values["L12_SECOND_APPROVER_BOOTSTRAP_TOKEN"] != "":
    raise SystemExit("offline approver bootstrap must remain disabled on testrun")
if values["L12_TESTRUN_MATCH_STORAGE"] != "ephemeral":
    raise SystemExit("testrun match storage must remain ephemeral")
metadata = os.stat(path, follow_symlinks=False)
if not stat.S_ISREG(metadata.st_mode) or stat.S_ISLNK(metadata.st_mode):
    raise SystemExit("testrun environment must be a regular file")
if enforce_metadata and (stat.S_IMODE(metadata.st_mode) != 0o600 or metadata.st_uid != 0 or metadata.st_gid != 0):
    raise SystemExit("testrun environment must be root:root mode 0600")
PY
}

validate_archive() {
  local archive="$1"
  local archive_kind="$2"
  python3 - "$archive" "$archive_kind" <<'PY'
import sys
import tarfile
from pathlib import PurePosixPath

archive, kind = sys.argv[1:]
allowed_roots = {
    "release": (".deployment-commit", "publish", "opcgpro-vue", "scripts"),
    "card-assets": ("card-assets.manifest.json", "card-assets.preload.json", "cards"),
}[kind]
required = {
    "release": {".deployment-commit", "publish/GrandUMIServer.dll", "opcgpro-vue/dist/index.html", "opcgpro-vue/dist-testrun/index.html", "opcgpro-vue/testrun-shared-files.txt", "scripts/ws-smoke.mjs"},
    "card-assets": {"card-assets.manifest.json", "card-assets.preload.json", "cards"},
}[kind]
seen = set()
total = 0
with tarfile.open(archive, "r:gz") as bundle:
    members = bundle.getmembers()
    if not members or len(members) > 50000:
        raise SystemExit("archive member count is invalid")
    for member in members:
        name = member.name
        if "\\" in name or "\x00" in name:
            raise SystemExit("archive contains an unsafe member name")
        while name.startswith("./"):
            name = name[2:]
        name = name.rstrip("/")
        if not name or name == ".":
            continue
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "." in path.parts:
            raise SystemExit(f"archive path escapes its root: {member.name}")
        normalized = path.as_posix()
        if normalized in seen:
            raise SystemExit(f"archive contains duplicate member: {normalized}")
        seen.add(normalized)
        if path.parts[0] not in allowed_roots:
            raise SystemExit(f"archive contains an unexpected root: {path.parts[0]}")
        if not (member.isdir() or member.isfile()):
            raise SystemExit(f"archive links and special files are forbidden: {normalized}")
        if member.isfile():
            total += member.size
            if member.size > 512 * 1024 * 1024:
                raise SystemExit(f"archive member is too large: {normalized}")
    missing = sorted(required - seen)
    if missing:
        raise SystemExit("archive is incomplete: " + ", ".join(missing))
    if total > 1024 * 1024 * 1024:
        raise SystemExit("archive expands beyond 1 GiB")
PY
}

validate_card_assets_tree() {
  local root="$1"
  local expected_hash="$2"
  if [[ -n "$test_root" && "${L12_TESTRUN_DEPLOY_SKIP_CARD_AUDIT:-0}" == "1" ]]; then
    [[ -f "${root}/card-assets.manifest.json" && ! -L "${root}/card-assets.manifest.json" ]] || fail "fixture card asset manifest is missing"
    return
  fi
  python3 - "$root" "$expected_hash" <<'PY'
import hashlib
import json
import os
import stat
import sys

root, expected = sys.argv[1:]
def full_hash(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)
metadata = os.lstat(root)
if not stat.S_ISDIR(metadata.st_mode) or stat.S_ISLNK(metadata.st_mode):
    raise SystemExit("card asset root must be a regular directory")
for current, directories, files in os.walk(root, followlinks=False):
    for name in directories + files:
        if stat.S_ISLNK(os.lstat(os.path.join(current, name)).st_mode):
            raise SystemExit("card asset tree contains a symbolic link")
with open(os.path.join(root, "card-assets.manifest.json"), encoding="utf-8") as handle:
    manifest = json.load(handle)
with open(os.path.join(root, "card-assets.preload.json"), encoding="utf-8") as handle:
    preload = json.load(handle)
if (manifest.get("schemaVersion"), manifest.get("complete"), manifest.get("cardCount"),
        manifest.get("playableCardCount"), manifest.get("presentationCardCount")) != (3, True, 366, 324, 42):
    raise SystemExit("card asset manifest is not a complete schema v3 catalog")
if manifest.get("assetVersion") != expected or not full_hash(expected):
    raise SystemExit("card asset version does not match")
cards = manifest.get("cards") or {}
if len(cards) != 366 or not isinstance(preload.get("entries"), list):
    raise SystemExit("card asset catalog or preload list is incomplete")
variants = ("originalWebp", "thumbWebp", "boardWebp", "detailWebp", "detailAvif")
rows = []
total = 0
for card_id, card in cards.items():
    content_hash = card.get("contentHash", "")
    if not full_hash(content_hash):
        raise SystemExit(f"invalid content hash for {card_id}")
    rows.append(":".join((card_id, content_hash, "presentation" if card.get("presentationOnly") else "playable", card.get("baseCardId", ""))))
    for variant in variants:
        relative = (card.get("variants") or {}).get(variant, "")
        if not relative or relative.startswith("/") or "\\" in relative or ".." in relative.split("/"):
            raise SystemExit(f"unsafe variant path for {card_id}:{variant}")
        absolute = os.path.join(root, *relative.split("/"))
        item = os.lstat(absolute)
        if not stat.S_ISREG(item.st_mode) or stat.S_ISLNK(item.st_mode):
            raise SystemExit(f"variant is not a regular file: {relative}")
        if (card.get("bytes") or {}).get(variant) != item.st_size:
            raise SystemExit(f"variant byte count differs: {relative}")
        total += item.st_size
actual = hashlib.sha256("\n".join(sorted(rows)).encode()).hexdigest()
if actual != expected or manifest.get("totalBytes") != total or total > 400 * 1024 * 1024:
    raise SystemExit("card asset aggregate version or byte count differs")
PY
}

read_release_commit() {
  local release="$1"
  local marker="${release}/.deployment-commit"
  [[ -f "$marker" && ! -L "$marker" ]] || fail "release commit marker is missing or unsafe"
  local value
  value="$(tr -d '\r\n' < "$marker")"
  [[ "$value" =~ ^[0-9a-f]{40}$ ]] || fail "release commit marker is invalid"
  printf '%s\n' "$value"
}

verify_health_once() {
  local base="$1"
  local expected="$2"
  local response
  response="$(curl -fsS --connect-timeout 5 --max-time 10 -H 'Cache-Control: no-cache' "${base}/health")" || return 1
  printf '%s' "$response" | node "$health_verifier" "$expected" --allow-maintenance >/dev/null
}

wait_for_health() {
  local base="$1"
  local expected="$2"
  local label="$3"
  local attempt
  for ((attempt=1; attempt<=health_attempts; attempt+=1)); do
    if verify_health_once "$base" "$expected"; then
      log "${label} health matches ${expected}"
      return 0
    fi
    if (( attempt < health_attempts )); then sleep "$health_delay_seconds"; fi
  done
  fail "${label} health does not match ${expected}"
}

verify_release() {
  local expected="$1"
  local label="$2"
  wait_for_health "$local_base" "$expected" "${label} local" || return 1
  wait_for_health "$public_base" "$expected" "${label} public" || return 1
  curl -fsS --connect-timeout 5 --max-time 10 "${public_base}/" >/dev/null || return 1
  curl -fsS --connect-timeout 5 --max-time 10 "${public_base}/cards" >/dev/null || return 1
  timeout 15s node "${active_dir}/scripts/ws-smoke.mjs" "ws://127.0.0.1:8084/ws" || return 1
  timeout 15s node "${active_dir}/scripts/ws-smoke.mjs" "wss://${public_host}${public_path}/ws" || return 1
  log "${label} HTTP and WebSocket checks passed"
}

self_test() {
  [[ "$(id -u)" -eq 0 ]] || fail "must run as root"
  for command_name in id flock python3 sha256sum tar curl systemctl nginx runuser node find readlink ln mv install cmp awk grep tr chmod chown sort timeout date seq stat dirname basename; do
    require_command "$command_name"
  done
  [[ "$health_attempts" =~ ^[1-9][0-9]*$ ]] || fail "health retry count is invalid"
  [[ "$health_delay_seconds" =~ ^[0-9]+([.][0-9]+)?$ ]] || fail "health retry delay is invalid"
  [[ -f "$health_verifier" && ! -L "$health_verifier" ]] || fail "testrun health verifier is missing"
  [[ -f "$environment_file" && ! -L "$environment_file" ]] || fail "testrun environment is missing or unsafe"
  validate_environment
  id "$service_user" >/dev/null 2>&1 || fail "service account is missing"
  id "$web_user" >/dev/null 2>&1 || fail "web account is missing"
  systemctl cat "$service_name" >/dev/null
  systemctl is-enabled --quiet "$service_name" || fail "testrun service is not enabled"
  systemctl is-active --quiet "$service_name" || fail "testrun service is not active"
  if [[ -z "$test_root" ]]; then
    [[ -L "$active_dir" ]] || fail "testrun active entry must be a managed symbolic link"
  else
    [[ -e "$active_dir" ]] || fail "fixture active entry is missing"
  fi
  local active_target
  active_target="$(readlink -f "$active_dir")"
  [[ "$active_target" == "${releases_dir}/"* && "$active_target" != "$releases_dir" ]] || fail "active release escapes the testrun release root"
  read_release_commit "$active_target" >/dev/null
  [[ -d "$runtime_dir" && ! -L "$runtime_dir" ]] || fail "testrun runtime must be an isolated regular directory"
  if [[ -z "$test_root" ]]; then
    [[ -L "${active_target}/publish/runtime" ]] || fail "active release runtime is not a symbolic link"
  else
    [[ -e "${active_target}/publish/runtime" ]] || fail "fixture runtime link is missing"
  fi
  [[ "$(readlink -f "${active_target}/publish/runtime")" == "$runtime_dir" ]] || fail "active release runtime is not the isolated testrun runtime"
  [[ -f "$nginx_path_snippet" && ! -L "$nginx_path_snippet" ]] || fail "testrun path snippet is missing or unsafe"
  grep -Fq 'location = /testrun/ws' "$nginx_path_snippet" || fail "testrun WebSocket path is missing"
  grep -Fq 'proxy_pass http://127.0.0.1:8084/ws;' "$nginx_path_snippet" || fail "testrun path backend is invalid"
  grep -Fq 'alias /opt/legion12-testrun/opcgpro-vue/dist-testrun/;' "$nginx_path_snippet" || fail "testrun frontend root is invalid"
  grep -Fq 'location ^~ /testrun/assets/' "$nginx_path_snippet" || fail "testrun hashed asset route is missing"
  grep -Fq 'root /opt/legion12-testrun-web-assets;' "$nginx_path_snippet" || fail "testrun hashed asset compatibility root is invalid"
  grep -Fq 'try_files $uri =404;' "$nginx_path_snippet" || fail "testrun missing hashed assets do not return 404"
  if grep -Eq '^[[:space:]]*auth_basic([[:space:]]|;)' "$nginx_path_snippet"; then fail "testrun must remain public without Basic Auth"; fi
  nginx -t >/dev/null
  log "isolated daily deployment preflight passed"
}

if [[ "${1:-}" == "self-test" ]]; then
  assert_unblocked
  self_test
  exit 0
fi

mode="${1:-}"
commit="${2:-}"
release_sha256="${3:-}"
release_archive="${4:-}"
card_assets_hash="${5:-}"
card_assets_sha256="${6:--}"
card_assets_archive="${7:--}"
[[ "$mode" == "deploy" || "$mode" == "dry-run" ]] || fail "usage: $0 <deploy|dry-run> <commit> <release-sha256> <release-archive> <card-assets-hash> <card-assets-sha256|-> <card-assets-archive|->"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "commit format is invalid"
[[ "$release_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "release SHA256 format is invalid"
[[ "$card_assets_hash" =~ ^[0-9a-f]{64}$ ]] || fail "card asset version format is invalid"
[[ "$release_archive" == "${incoming_dir}/l12-testrun-release-${commit}.tar.gz" ]] || fail "release archive path is not allowed"
if [[ "$card_assets_archive" == "-" ]]; then
  [[ "$card_assets_sha256" == "-" ]] || fail "cached card assets require '-' SHA256"
else
  [[ "$card_assets_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "card asset archive SHA256 format is invalid"
  [[ "$card_assets_archive" == "${incoming_dir}/l12-testrun-card-assets-${card_assets_hash}.tar.gz" ]] || fail "card asset archive path is not allowed"
fi

if [[ "${L12_TESTRUN_DEPLOY_LOCKED:-0}" != "1" ]]; then
  export L12_TESTRUN_DEPLOY_LOCKED=1
  exec flock --close --nonblock "$lock_file" "$0" "$@"
fi

assert_unblocked
self_test
mkdir -p "$incoming_dir" "$releases_dir" "$static_card_assets_dir" "$static_web_assets_dir/assets" "$static_web_assets_dir/testrun/assets"
[[ -f "$release_archive" && ! -L "$release_archive" ]] || fail "release archive is missing or unsafe"
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "release archive SHA256 differs"
validate_archive "$release_archive" release
layout="$(release_layout_bytes "$release_archive")" || fail "release layout budget projection failed"
read -r release_unpacked_bytes hydrate_logical_bytes new_web_cache_bytes <<< "$layout"
for value in "$release_unpacked_bytes" "$hydrate_logical_bytes" "$new_web_cache_bytes"; do
  [[ "$value" =~ ^[0-9]+$ ]] || fail "release layout budget projection is invalid"
done
card_unpacked_bytes=0
budget_card_target="${static_card_assets_dir}/${card_assets_hash}"
if [[ ! -d "$budget_card_target" || -L "$budget_card_target" ]]; then
  [[ ! -e "$budget_card_target" && ! -L "$budget_card_target" ]] || fail "card asset target is not a regular directory"
  [[ "$card_assets_archive" != "-" && -f "$card_assets_archive" && ! -L "$card_assets_archive" ]] || fail "card asset cache is absent and no safe archive was supplied"
  [[ "$(sha256sum "$card_assets_archive" | awk '{print $1}')" == "$card_assets_sha256" ]] || fail "card asset archive SHA256 differs"
  validate_archive "$card_assets_archive" card-assets
  card_unpacked_bytes="$(archive_unpacked_bytes "$card_assets_archive")"
fi
budget_previous_target="$(readlink -f "$active_dir")" || fail "cannot resolve previous testrun release for storage projection"
[[ "$budget_previous_target" == "${releases_dir}/"* && "$budget_previous_target" != "$releases_dir" ]] \
  || fail "previous testrun release escapes the managed release root"
previous_web_cache_bytes=0
for projection in "opcgpro-vue/dist/assets:assets" "opcgpro-vue/dist-testrun/assets:testrun/assets"; do
  source_relative="${projection%%:*}"
  public_prefix="${projection#*:}"
  missing_bytes="$(missing_web_cache_bytes "${budget_previous_target}/${source_relative}" "$public_prefix")" \
    || fail "previous compatibility web cache projection failed"
  previous_web_cache_bytes=$((previous_web_cache_bytes + missing_bytes))
done
runtime_logical_bytes="$(tree_bytes "$runtime_dir")" || fail "cannot measure testrun runtime for backup projection"
runtime_backup_allowance=$((runtime_logical_bytes + 16 * 1024 * 1024))
if [[ -n "$test_root" ]]; then
  release_unpacked_bytes="${L12_TESTRUN_DEPLOY_TEST_RELEASE_LOGICAL_BYTES:-$release_unpacked_bytes}"
  previous_web_cache_bytes="${L12_TESTRUN_DEPLOY_TEST_PREVIOUS_WEB_BYTES:-$previous_web_cache_bytes}"
  new_web_cache_bytes="${L12_TESTRUN_DEPLOY_TEST_NEW_WEB_BYTES:-$new_web_cache_bytes}"
  runtime_backup_allowance="${L12_TESTRUN_DEPLOY_TEST_RUNTIME_BACKUP_BYTES:-$runtime_backup_allowance}"
fi
validate_testrun_storage_budget "$release_unpacked_bytes" "$card_unpacked_bytes" \
  "$previous_web_cache_bytes" "$new_web_cache_bytes" "$runtime_backup_allowance"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
short_commit="${commit:0:12}"
stage_dir="${test_root}/opt/legion12-testrun-staging-${short_commit}-${timestamp}"
stage_card_assets_dir=""
release_dir="${releases_dir}/${commit}-${timestamp}"
previous_target=""
previous_commit=""
runtime_backup=""
runtime_backup_checksum=""
runtime_backup_partial=""
runtime_backup_checksum_partial=""
service_stopped=0
switched=0
start_attempted=0
failure_stage="preflight"

cleanup() {
  if [[ -n "$stage_dir" && -d "$stage_dir" ]]; then rm -rf -- "$stage_dir"; fi
  if [[ -n "$stage_card_assets_dir" && -d "$stage_card_assets_dir" ]]; then rm -rf -- "$stage_card_assets_dir"; fi
  if [[ -n "$runtime_backup_partial" && -f "$runtime_backup_partial" && ! -L "$runtime_backup_partial" ]]; then rm -f -- "$runtime_backup_partial"; fi
  if [[ -n "$runtime_backup_checksum_partial" && -f "$runtime_backup_checksum_partial" && ! -L "$runtime_backup_checksum_partial" ]]; then rm -f -- "$runtime_backup_checksum_partial"; fi
  rm -f -- "$release_archive"
  if [[ "$card_assets_archive" != "-" ]]; then rm -f -- "$card_assets_archive"; fi
}

stop_service_and_confirm() {
  systemctl stop "$service_name" >/dev/null 2>&1 || true
  ! systemctl is-active --quiet "$service_name" >/dev/null 2>&1
}

restore_previous_link() {
  local restore_link="${test_root}/opt/.legion12-testrun-restore-${timestamp}"
  ln -s "$previous_target" "$restore_link"
  mv -Tf "$restore_link" "$active_dir"
}

write_failure_record() {
  local disposition="$1"
  local blocked="$2"
  mkdir -p "$failure_dir"
  chmod 0700 "$failure_dir"
  local incident="${failure_dir}/deploy-${short_commit}-${timestamp}.txt"
  local temporary="${incident}.tmp"
  {
    printf 'status=failed\n'
    printf 'failedCommit=%s\n' "$commit"
    printf 'failureStage=%s\n' "$failure_stage"
    printf 'disposition=%s\n' "$disposition"
    printf 'previousCommit=%s\n' "$previous_commit"
    printf 'previousTarget=%s\n' "$previous_target"
    printf 'runtime=%s\n' "$runtime_dir"
    printf 'runtimeBackup=%s\n' "$runtime_backup"
    printf 'recordedAt=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$temporary"
  chmod 0600 "$temporary"
  mv "$temporary" "$incident"
  if [[ "$blocked" -eq 1 ]]; then install -m 0600 "$incident" "${deployment_dir}/deployment-blocked.txt"; fi
  log "failure record saved at ${incident}"
}

rollback_on_error() {
  local status=$?
  if [[ "$status" -eq 0 ]]; then status=1; fi
  trap - ERR INT TERM
  set +e
  local rollback_ok=1
  if [[ "$service_stopped" -eq 1 || "$start_attempted" -eq 1 || "$switched" -eq 1 ]]; then
    stop_service_and_confirm || rollback_ok=0
    service_stopped=1
    if [[ "$switched" -eq 1 ]]; then restore_previous_link || rollback_ok=0; fi
    if [[ "$rollback_ok" -eq 1 ]]; then systemctl start "$service_name" || rollback_ok=0; fi
    if [[ "$rollback_ok" -eq 1 ]]; then verify_release "$previous_commit" "restored release" || rollback_ok=0; fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      service_stopped=0
      write_failure_record "previous testrun release restored; runtime preserved" 0 || true
      log "deployment failed; the previous testrun release is active and verified"
    else
      stop_service_and_confirm
      service_stopped=1
      write_failure_record "previous release could not be verified; service stopped; manual reconciliation required" 1 || true
      log "deployment failed and rollback could not be verified; testrun is stopped"
    fi
  fi
  cleanup
  exit "$status"
}
trap rollback_on_error ERR INT TERM

failure_stage="extract-release"
mkdir -p "$stage_dir"
tar --no-same-owner --no-same-permissions -xzf "$release_archive" -C "$stage_dir"
[[ "$(tr -d '\r\n' < "${stage_dir}/.deployment-commit")" == "$commit" ]] || fail "release commit marker differs"
[[ -f "${stage_dir}/publish/GrandUMIServer.dll" ]] || fail "backend entry is missing"
[[ -f "${stage_dir}/opcgpro-vue/dist/index.html" ]] || fail "frontend entry is missing"
[[ -f "${stage_dir}/opcgpro-vue/dist-testrun/index.html" ]] || fail "testrun frontend entry is missing"
validate_web_assets_tree "${stage_dir}/opcgpro-vue/dist/assets"
[[ -f "${stage_dir}/opcgpro-vue/testrun-shared-files.txt" && ! -L "${stage_dir}/opcgpro-vue/testrun-shared-files.txt" ]] || fail "testrun shared-file manifest is missing or unsafe"
[[ -f "${stage_dir}/scripts/ws-smoke.mjs" ]] || fail "WebSocket probe is missing"
[[ ! -e "${stage_dir}/publish/runtime" && ! -L "${stage_dir}/publish/runtime" ]] || fail "release archive contains runtime data"
[[ ! -e "${stage_dir}/opcgpro-vue/dist/card-assets" && ! -L "${stage_dir}/opcgpro-vue/dist/card-assets" ]] || fail "release archive contains card asset data"
[[ ! -e "${stage_dir}/opcgpro-vue/dist/cards" && ! -L "${stage_dir}/opcgpro-vue/dist/cards" ]] || fail "release archive contains retired card data"

failure_stage="hydrate-shared-frontend"
while IFS= read -r shared_path || [[ -n "$shared_path" ]]; do
  shared_path="${shared_path%$'\r'}"
  [[ -n "$shared_path" && "$shared_path" != /* && "/${shared_path}/" != *"/../"* && "/${shared_path}/" != *"/./"* ]] \
    || fail "testrun shared-file manifest contains an unsafe path"
  shared_source="${stage_dir}/opcgpro-vue/dist/${shared_path}"
  shared_target="${stage_dir}/opcgpro-vue/dist-testrun/${shared_path}"
  [[ -f "$shared_source" && ! -L "$shared_source" ]] || fail "testrun shared source is missing or unsafe: ${shared_path}"
  [[ ! -e "$shared_target" && ! -L "$shared_target" ]] || fail "testrun shared target already exists: ${shared_path}"
  mkdir -p "$(dirname "$shared_target")"
  ln "$shared_source" "$shared_target"
done < "${stage_dir}/opcgpro-vue/testrun-shared-files.txt"
validate_web_assets_tree "${stage_dir}/opcgpro-vue/dist-testrun/assets"

failure_stage="validate-card-assets"
card_assets_target="${static_card_assets_dir}/${card_assets_hash}"
if [[ -d "$card_assets_target" && ! -L "$card_assets_target" ]]; then
  validate_card_assets_tree "$card_assets_target" "$card_assets_hash"
else
  [[ ! -e "$card_assets_target" && ! -L "$card_assets_target" ]] || fail "card asset target is not a regular directory"
  [[ "$card_assets_archive" != "-" && -f "$card_assets_archive" && ! -L "$card_assets_archive" ]] || fail "card asset cache is absent and no safe archive was supplied"
  [[ "$(sha256sum "$card_assets_archive" | awk '{print $1}')" == "$card_assets_sha256" ]] || fail "card asset archive SHA256 differs"
  validate_archive "$card_assets_archive" card-assets
  stage_card_assets_dir="${test_root}/opt/legion12-testrun-card-assets-staging-${card_assets_hash:0:12}-${timestamp}"
  mkdir -p "$stage_card_assets_dir"
  tar --no-same-owner --no-same-permissions -xzf "$card_assets_archive" -C "$stage_card_assets_dir"
  validate_card_assets_tree "$stage_card_assets_dir" "$card_assets_hash"
  chmod 0755 "$stage_card_assets_dir"
  find "$stage_card_assets_dir" -type d -exec chmod 0755 {} +
  find "$stage_card_assets_dir" -type f -exec chmod 0644 {} +
  if [[ "$mode" == "deploy" ]]; then
    mv "$stage_card_assets_dir" "$card_assets_target"
    stage_card_assets_dir=""
  else
    card_assets_target="$stage_card_assets_dir"
  fi
fi

failure_stage="prepare-release"
ln -s "$runtime_dir" "${stage_dir}/publish/runtime"
ln -s "$card_assets_target" "${stage_dir}/opcgpro-vue/dist/card-assets"
chmod 0755 "$stage_dir" "${stage_dir}/publish" "${stage_dir}/opcgpro-vue" "${stage_dir}/opcgpro-vue/dist" "${stage_dir}/opcgpro-vue/dist-testrun"
find "${stage_dir}/publish" "${stage_dir}/opcgpro-vue/dist" "${stage_dir}/opcgpro-vue/dist-testrun" -type d -exec chmod 0755 {} +
find "${stage_dir}/publish" "${stage_dir}/opcgpro-vue/dist" "${stage_dir}/opcgpro-vue/dist-testrun" -type f -exec chmod 0644 {} +
runuser -u "$service_user" -- test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "service account cannot read the backend entry"
runuser -u "$web_user" -- test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "web account cannot read the frontend entry"
runuser -u "$web_user" -- test -r "${stage_dir}/opcgpro-vue/dist-testrun/index.html" || fail "web account cannot read the testrun frontend entry"

if [[ "$mode" == "dry-run" ]]; then
  log "dry-run passed; no service, runtime, active release, or Nginx state was changed"
  cleanup
  trap - ERR INT TERM
  exit 0
fi

previous_target="$(readlink -f "$active_dir")"
previous_commit="$(read_release_commit "$previous_target")"
failure_stage="install-compatible-web-assets"
install_web_assets_tree "${previous_target}/opcgpro-vue/dist/assets" "assets"
install_web_assets_tree "${previous_target}/opcgpro-vue/dist-testrun/assets" "testrun/assets"
install_web_assets_tree "${stage_dir}/opcgpro-vue/dist/assets" "assets"
install_web_assets_tree "${stage_dir}/opcgpro-vue/dist-testrun/assets" "testrun/assets"
ensure_web_assets_entry
sample_web_asset="$(find "$static_web_assets_dir" -type f -print -quit)"
[[ -n "$sample_web_asset" ]] || fail "shared testrun web asset pool is empty"
runuser -u "$web_user" -- test -r "$sample_web_asset" || fail "web account cannot read shared testrun web assets"
failure_stage="stop-current-service"
service_stopped=1
systemctl stop "$service_name"
if systemctl is-active --quiet "$service_name" >/dev/null 2>&1; then fail "service remained active after stop"; fi

failure_stage="snapshot-runtime"
mkdir -p "$backup_dir"
chmod 0700 "$backup_dir"
runtime_backup="${backup_dir}/runtime-before-${short_commit}-${timestamp}.tar.gz"
runtime_backup_checksum="${runtime_backup}.sha256"
runtime_backup_partial="${runtime_backup}.partial"
runtime_backup_checksum_partial="${runtime_backup_checksum}.partial"
[[ ! -e "$runtime_backup" && ! -L "$runtime_backup" && ! -e "$runtime_backup_partial" && ! -L "$runtime_backup_partial" ]] || fail "runtime snapshot target already exists"
[[ ! -e "$runtime_backup_checksum" && ! -L "$runtime_backup_checksum" && ! -e "$runtime_backup_checksum_partial" && ! -L "$runtime_backup_checksum_partial" ]] || fail "runtime snapshot checksum target already exists"
tar -czf "$runtime_backup_partial" -C "$runtime_dir" .
tar -tzf "$runtime_backup_partial" >/dev/null
runtime_backup_sha256="$(sha256sum "$runtime_backup_partial" | awk '{print $1}')"
printf '%s  %s\n' "$runtime_backup_sha256" "$(basename "$runtime_backup")" > "$runtime_backup_checksum_partial"
chmod 0600 "$runtime_backup_partial" "$runtime_backup_checksum_partial"
mv -Tn "$runtime_backup_partial" "$runtime_backup"
runtime_backup_partial=""
mv -Tn "$runtime_backup_checksum_partial" "$runtime_backup_checksum"
runtime_backup_checksum_partial=""

failure_stage="install-release"
mv "$stage_dir" "$release_dir"
stage_dir=""
next_link="${test_root}/opt/.legion12-testrun-next-${timestamp}"
ln -s "$release_dir" "$next_link"
mv -Tf "$next_link" "$active_dir"
switched=1

failure_stage="start-new-service"
start_attempted=1
systemctl start "$service_name"
service_stopped=0
failure_stage="verify-new-release"
verify_release "$commit" "new release"

cat > "${deployment_dir}/deployment-info.txt" <<EOF
Legion12 isolated testrun
commit=${commit}
activeRelease=${release_dir}
previousRelease=${previous_target}
runtime=${runtime_dir}
runtimeBackup=${runtime_backup}
runtimeBackupSha256=${runtime_backup_sha256}
publicBase=${public_base}
deployedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF
chmod 0600 "${deployment_dir}/deployment-info.txt"
failure_stage="post-success-storage-cleanup"
if ! converge_testrun_storage "$release_dir"; then
  log "post-deploy storage cleanup was incomplete; the verified release remains active and retained items will be retried later"
fi
rm -f -- "${deployment_dir}/deployment-blocked.txt"
cleanup
trap - ERR INT TERM
log "daily testrun deployment completed for ${commit}"
