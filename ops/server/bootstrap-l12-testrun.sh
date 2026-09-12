#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly commit="${1:-}"
readonly release_sha256="${2:-}"
readonly release_archive="${3:-}"
readonly card_assets_hash="${4:-}"
readonly card_assets_sha256="${5:--}"
readonly card_assets_archive="${6:--}"
readonly public_host="testrun.legion-12.com"
readonly public_base="https://${public_host}"
readonly local_base="http://127.0.0.1:8084"
readonly service_name="legion12-testrun.service"
readonly service_user="legion12"
readonly web_user="www-data"
readonly release_root="/opt/legion12-testrun-releases"
readonly release_dir="${release_root}/${commit}-$(date -u +%Y%m%dT%H%M%SZ)"
readonly active_link="/opt/legion12-testrun"
readonly runtime_dir="/opt/legion12-testrun-runtime"
readonly deployment_dir="/opt/legion12-testrun-deployment"
readonly incoming_dir="${deployment_dir}/incoming"
readonly static_card_assets_dir="/opt/legion12-testrun-static/card-assets"
readonly service_template="/tmp/legion12-testrun.service"
readonly nginx_http_template="/tmp/legion12-testrun-http.nginx"
readonly nginx_tls_template="/tmp/legion12-testrun.nginx"
readonly activate_tls_template="/tmp/activate-l12-testrun-tls.sh"
readonly daily_deploy_template="/tmp/deploy-l12-testrun-release.sh"
readonly health_verifier_template="/tmp/verify-l12-testrun-health.mjs"
readonly environment_file="/etc/legion12-testrun.env"
readonly service_unit="/etc/systemd/system/${service_name}"
readonly nginx_http_site="/etc/nginx/sites-available/legion12-testrun-http"
readonly nginx_tls_site="/etc/nginx/sites-available/legion12-testrun-tls"
readonly nginx_enabled="/etc/nginx/sites-enabled/legion12-testrun"
readonly stage_dir="/opt/legion12-testrun-staging-${commit:0:12}-$$"
readonly stage_card_assets_dir="/opt/legion12-testrun-card-assets-staging-${card_assets_hash:0:12}-$$"
readonly environment_temp="/etc/.legion12-testrun.env.$$"

infrastructure_changed=0
service_start_attempted=0
release_installed=0

log() { printf '[L12 testrun bootstrap] %s\n' "$*"; }
fail() { printf '[L12 testrun bootstrap] ERROR: %s\n' "$*" >&2; return 1; }
require_command() { command -v "$1" >/dev/null 2>&1 || fail "missing command: $1"; }

cleanup() {
  [[ ! -d "$stage_dir" ]] || rm -rf -- "$stage_dir"
  [[ ! -d "$stage_card_assets_dir" ]] || rm -rf -- "$stage_card_assets_dir"
  rm -f -- "$environment_temp"
}

bootstrap_failed() {
  local status=$?
  if [[ "$status" -eq 0 ]]; then status=1; fi
  trap - ERR INT TERM
  set +e
  if [[ "$service_start_attempted" -eq 1 ]]; then
    systemctl stop "$service_name" >/dev/null 2>&1
    systemctl disable "$service_name" >/dev/null 2>&1
  fi
  if [[ "$infrastructure_changed" -eq 1 ]]; then
    install -d -o root -g root -m 0700 "$deployment_dir"
    {
      printf 'status=bootstrap-failed\n'
      printf 'commit=%s\n' "$commit"
      printf 'releaseInstalled=%s\n' "$release_installed"
      printf 'runtimePreserved=%s\n' "$runtime_dir"
      printf 'recordedAt=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    } > "${deployment_dir}/deployment-blocked.txt"
    chmod 0600 "${deployment_dir}/deployment-blocked.txt"
    log "bootstrap failed; testrun service is stopped and a reconciliation marker was kept"
  fi
  cleanup
  exit "$status"
}
trap bootstrap_failed ERR INT TERM
trap cleanup EXIT

validate_archive() {
  local archive="$1"
  local archive_kind="$2"
  python3 - "$archive" "$archive_kind" <<'PY'
import os
import sys
import tarfile
from pathlib import PurePosixPath

archive, kind = sys.argv[1:]
allowed_roots = {
    "release": (".deployment-commit", "publish", "opcgpro-vue", "scripts"),
    "card-assets": ("card-assets.manifest.json", "card-assets.preload.json", "cards"),
}[kind]
required = {
    "release": {".deployment-commit", "publish/GrandUMIServer.dll", "opcgpro-vue/dist/index.html", "scripts/ws-smoke.mjs"},
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
        root = path.parts[0]
        if root not in allowed_roots:
            raise SystemExit(f"archive contains an unexpected root: {root}")
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

validate_environment() {
  local path="$1"
  python3 - "$path" <<'PY'
import os
import re
import stat
import sys

path = sys.argv[1]
allowed = {
    "L12_ADMIN_PASSWORD", "L12_EMAIL_FEATURE_ENABLED", "L12_PUBLIC_BASE_URL",
    "L12_SMTP_HOST", "L12_SMTP_PORT", "L12_SMTP_USERNAME", "L12_SMTP_PASSWORD",
    "L12_SMTP_FROM_ADDRESS", "L12_SMTP_FROM_NAME", "L12_SMTP_ENABLE_SSL",
    "L12_ENABLE_SECOND_APPROVER_BOOTSTRAP", "L12_SECOND_APPROVER_BOOTSTRAP_TOKEN",
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
    raise SystemExit("testrun admin password must be an independent 64-character hex secret")
if values["L12_EMAIL_FEATURE_ENABLED"] != "false":
    raise SystemExit("email must remain disabled on testrun")
if values["L12_PUBLIC_BASE_URL"] != "https://testrun.legion-12.com":
    raise SystemExit("testrun public base URL is invalid")
for key in values:
    if key.startswith("L12_SMTP_") and values[key] != "":
        raise SystemExit("SMTP values must remain empty on testrun")
if values["L12_ENABLE_SECOND_APPROVER_BOOTSTRAP"] != "false" or values["L12_SECOND_APPROVER_BOOTSTRAP_TOKEN"] != "":
    raise SystemExit("offline approver bootstrap must remain disabled on testrun")
metadata = os.stat(path, follow_symlinks=False)
if stat.S_IMODE(metadata.st_mode) != 0o600 or metadata.st_uid != 0 or metadata.st_gid != 0:
    raise SystemExit("testrun environment must be root:root mode 0600")
PY
}

validate_card_assets_tree() {
  local root="$1"
  local expected_hash="$2"
  python3 - "$root" "$expected_hash" <<'PY'
import hashlib
import json
import os
import stat
import sys

root, expected = sys.argv[1:]
def re_full_hash(value):
    return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)

root_stat = os.lstat(root)
if not stat.S_ISDIR(root_stat.st_mode) or stat.S_ISLNK(root_stat.st_mode):
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
if manifest.get("assetVersion") != expected or not re_full_hash(expected):
    raise SystemExit("card asset version does not match")
cards = manifest.get("cards") or {}
if len(cards) != 366 or not isinstance(preload.get("entries"), list):
    raise SystemExit("card asset catalog or preload list is incomplete")
variants = ("originalWebp", "thumbWebp", "boardWebp", "detailWebp", "detailAvif")
rows = []
total = 0
for card_id, card in cards.items():
    content_hash = card.get("contentHash", "")
    if not re_full_hash(content_hash):
        raise SystemExit(f"invalid content hash for {card_id}")
    rows.append(":".join((card_id, content_hash, "presentation" if card.get("presentationOnly") else "playable", card.get("baseCardId", ""))))
    for variant in variants:
        relative = (card.get("variants") or {}).get(variant, "")
        if not relative or relative.startswith("/") or "\\" in relative or ".." in relative.split("/"):
            raise SystemExit(f"unsafe variant path for {card_id}:{variant}")
        absolute = os.path.join(root, *relative.split("/"))
        metadata = os.lstat(absolute)
        if not stat.S_ISREG(metadata.st_mode) or stat.S_ISLNK(metadata.st_mode):
            raise SystemExit(f"variant is not a regular file: {relative}")
        if (card.get("bytes") or {}).get(variant) != metadata.st_size:
            raise SystemExit(f"variant byte count differs: {relative}")
        total += metadata.st_size
actual = hashlib.sha256("\n".join(sorted(rows)).encode()).hexdigest()
if actual != expected or manifest.get("totalBytes") != total or total > 400 * 1024 * 1024:
    raise SystemExit("card asset aggregate version or byte count differs")
PY
}

[[ "$(id -u)" -eq 0 ]] || fail "must run as root"
for command_name in id python3 sha256sum tar openssl systemctl systemd-analyze nginx runuser node find readlink ln mv install chmod chown curl date stat; do
  require_command "$command_name"
done
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

for template in "$service_template" "$nginx_http_template" "$nginx_tls_template" "$activate_tls_template" "$daily_deploy_template" "$health_verifier_template"; do
  [[ -f "$template" && ! -L "$template" ]] || fail "bootstrap template is missing or unsafe: ${template}"
done
id "$service_user" >/dev/null 2>&1 || fail "service account is missing"
id "$web_user" >/dev/null 2>&1 || fail "web account is missing"
[[ -f "$release_archive" && ! -L "$release_archive" ]] || fail "release archive is missing or unsafe"
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "release archive SHA256 differs"
validate_archive "$release_archive" release

for forbidden in "$active_link" "$runtime_dir" "$release_root" "$environment_file" "$service_unit" "$nginx_http_site" "$nginx_tls_site" "$nginx_enabled"; do
  [[ ! -e "$forbidden" && ! -L "$forbidden" ]] || fail "bootstrap is first-run only; managed path already exists: ${forbidden}"
done
if systemctl is-enabled --quiet "$service_name" >/dev/null 2>&1 || systemctl is-active --quiet "$service_name" >/dev/null 2>&1; then
  fail "bootstrap refuses an existing or active testrun service"
fi

mkdir -p "$stage_dir"
tar --no-same-owner --no-same-permissions -xzf "$release_archive" -C "$stage_dir"
[[ "$(tr -d '\r\n' < "${stage_dir}/.deployment-commit")" == "$commit" ]] || fail "release commit marker differs"
[[ -f "${stage_dir}/publish/GrandUMIServer.dll" ]] || fail "backend entry is missing"
[[ -f "${stage_dir}/opcgpro-vue/dist/index.html" ]] || fail "frontend entry is missing"
[[ -f "${stage_dir}/scripts/ws-smoke.mjs" ]] || fail "WebSocket probe is missing"
[[ ! -e "${stage_dir}/publish/runtime" && ! -L "${stage_dir}/publish/runtime" ]] || fail "release archive contains runtime data"
[[ ! -e "${stage_dir}/opcgpro-vue/dist/card-assets" && ! -L "${stage_dir}/opcgpro-vue/dist/card-assets" ]] || fail "release archive contains card asset data"
[[ ! -e "${stage_dir}/opcgpro-vue/dist/cards" && ! -L "${stage_dir}/opcgpro-vue/dist/cards" ]] || fail "release archive contains retired card data"

mkdir -p "$static_card_assets_dir"
card_assets_target="${static_card_assets_dir}/${card_assets_hash}"
if [[ -d "$card_assets_target" && ! -L "$card_assets_target" ]]; then
  [[ "$card_assets_archive" == "-" ]] || log "validated cached card assets; uploaded duplicate will be discarded"
  validate_card_assets_tree "$card_assets_target" "$card_assets_hash"
else
  [[ ! -e "$card_assets_target" && ! -L "$card_assets_target" ]] || fail "card asset target is not a regular directory"
  [[ "$card_assets_archive" != "-" && -f "$card_assets_archive" && ! -L "$card_assets_archive" ]] || fail "card asset cache is absent and no safe archive was supplied"
  [[ "$(sha256sum "$card_assets_archive" | awk '{print $1}')" == "$card_assets_sha256" ]] || fail "card asset archive SHA256 differs"
  validate_archive "$card_assets_archive" card-assets
  mkdir -p "$stage_card_assets_dir"
  tar --no-same-owner --no-same-permissions -xzf "$card_assets_archive" -C "$stage_card_assets_dir"
  validate_card_assets_tree "$stage_card_assets_dir" "$card_assets_hash"
  chmod 0755 "$stage_card_assets_dir"
  find "$stage_card_assets_dir" -type d -exec chmod 0755 {} +
  find "$stage_card_assets_dir" -type f -exec chmod 0644 {} +
  mv "$stage_card_assets_dir" "$card_assets_target"
fi

admin_password="$(openssl rand -hex 32)"
[[ "$admin_password" =~ ^[0-9a-f]{64}$ ]] || fail "failed to generate an independent admin secret"
cat > "$environment_temp" <<EOF
L12_ADMIN_PASSWORD=${admin_password}
L12_EMAIL_FEATURE_ENABLED=false
L12_PUBLIC_BASE_URL=${public_base}
L12_SMTP_HOST=
L12_SMTP_PORT=
L12_SMTP_USERNAME=
L12_SMTP_PASSWORD=
L12_SMTP_FROM_ADDRESS=
L12_SMTP_FROM_NAME=
L12_SMTP_ENABLE_SSL=
L12_ENABLE_SECOND_APPROVER_BOOTSTRAP=false
L12_SECOND_APPROVER_BOOTSTRAP_TOKEN=
EOF
chown root:root "$environment_temp"
chmod 0600 "$environment_temp"
validate_environment "$environment_temp"

chmod 0755 "$stage_dir" "${stage_dir}/publish" "${stage_dir}/opcgpro-vue" "${stage_dir}/opcgpro-vue/dist"
find "${stage_dir}/publish" "${stage_dir}/opcgpro-vue/dist" -type d -exec chmod 0755 {} +
find "${stage_dir}/publish" "${stage_dir}/opcgpro-vue/dist" -type f -exec chmod 0644 {} +
ln -s "$runtime_dir" "${stage_dir}/publish/runtime"
ln -s "$card_assets_target" "${stage_dir}/opcgpro-vue/dist/card-assets"
runuser -u "$service_user" -- test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "service account cannot read the backend entry"
runuser -u "$web_user" -- test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "web account cannot read the frontend entry"

install -d -o root -g root -m 0755 "$release_root"
install -d -o "$service_user" -g "$service_user" -m 0750 "$runtime_dir"
install -d -o root -g root -m 0700 "$deployment_dir"
install -d -o root -g root -m 0755 /usr/local/libexec
install -o root -g root -m 0600 "$environment_temp" "$environment_file"
install -o root -g root -m 0644 "$service_template" "$service_unit"
install -o root -g root -m 0644 "$nginx_http_template" "$nginx_http_site"
install -o root -g root -m 0644 "$nginx_tls_template" "$nginx_tls_site"
install -o root -g root -m 0755 "$activate_tls_template" /usr/local/sbin/activate-legion12-testrun-tls
install -o root -g root -m 0755 "$daily_deploy_template" /usr/local/sbin/deploy-legion12-testrun-release
install -o root -g root -m 0755 "$health_verifier_template" /usr/local/libexec/verify-legion12-testrun-health.mjs
infrastructure_changed=1
validate_environment "$environment_file"
systemd-analyze verify "$service_unit"
ln -s "$nginx_http_site" "$nginx_enabled"
nginx -t

mv "$stage_dir" "$release_dir"
release_installed=1
next_link="/opt/.legion12-testrun-next-$$"
ln -s "$release_dir" "$next_link"
mv -Tf "$next_link" "$active_link"
systemctl daemon-reload
systemctl reload nginx
service_start_attempted=1
systemctl enable --now "$service_name"

healthy=0
for _ in $(seq 1 30); do
  if response="$(curl -fsS --connect-timeout 3 --max-time 8 "${local_base}/health")" \
      && printf '%s' "$response" | node /usr/local/libexec/verify-legion12-testrun-health.mjs "$commit" >/dev/null; then
    healthy=1
    break
  fi
  sleep 1
done
[[ "$healthy" -eq 1 ]] || fail "local testrun health did not match the release commit"
timeout 15s node "${active_link}/scripts/ws-smoke.mjs" "ws://127.0.0.1:8084/ws"

cat > "${deployment_dir}/deployment-info.txt" <<EOF
Legion12 isolated testrun
commit=${commit}
activeRelease=${release_dir}
runtime=${runtime_dir}
publicHost=${public_host}
tlsState=awaiting-activation
deployedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF
chmod 0600 "${deployment_dir}/deployment-info.txt"
rm -f -- "$release_archive"
if [[ "$card_assets_archive" != "-" ]]; then rm -f -- "$card_assets_archive"; fi
rm -f -- "${deployment_dir}/deployment-blocked.txt"
service_start_attempted=0
trap - ERR INT TERM
log "isolated service is healthy; issue the certificate, then activate TLS explicitly"
log "email is disabled and the generated admin secret remains only in ${environment_file}"
