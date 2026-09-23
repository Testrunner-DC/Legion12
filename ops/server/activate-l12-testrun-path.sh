#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly source_snippet="${1:-/tmp/legion12-testrun-path.nginx}"
readonly source_service="${2:-/tmp/legion12-testrun.service}"
readonly target_snippet="/etc/nginx/snippets/legion12-testrun-path.conf"
readonly target_service="/etc/systemd/system/legion12-testrun.service"
readonly environment_file="/etc/legion12-testrun.env"
readonly production_site="/etc/nginx/sites-available/legion12"
readonly production_enabled="/etc/nginx/sites-enabled/legion12"
readonly include_line="    include /etc/nginx/snippets/legion12-testrun-path.conf;"

fail() { printf '[L12 testrun path] ERROR: %s\n' "$*" >&2; exit 1; }
[[ "$(id -u)" -eq 0 ]] || fail "must run as root"
[[ -f "$source_snippet" && ! -L "$source_snippet" ]] || fail "source snippet is missing or unsafe"
[[ -f "$source_service" && ! -L "$source_service" ]] || fail "source service is missing or unsafe"
[[ -f "$target_service" && ! -L "$target_service" ]] || fail "installed service is missing or unsafe"
[[ -f "$environment_file" && ! -L "$environment_file" ]] || fail "testrun environment is missing or unsafe"
[[ -f "$production_site" && ! -L "$production_site" ]] || fail "production Nginx site is missing or unsafe"
[[ -L "$production_enabled" && "$(readlink -f "$production_enabled")" == "$production_site" ]] \
  || fail "production Nginx site is not the active managed site"
grep -Fq 'server_name legion-12.com;' "$production_site" || fail "production host marker is missing"
grep -Fq 'proxy_pass http://127.0.0.1:8084/ws;' "$source_snippet" || fail "testrun WebSocket backend is invalid"
grep -Fq 'alias /opt/legion12-testrun/opcgpro-vue/dist-testrun/;' "$source_snippet" || fail "testrun frontend root is invalid"

readonly suffix="$(date -u +%Y%m%dT%H%M%SZ)-$$"
readonly site_backup="/etc/nginx/sites-available/.legion12-before-testrun-path-${suffix}"
readonly service_backup="/etc/systemd/system/.legion12-testrun-before-path-${suffix}.service"
readonly environment_backup="/etc/.legion12-testrun-before-path-${suffix}.env"
readonly snippet_backup="/etc/nginx/snippets/.legion12-testrun-path-before-${suffix}.conf"
snippet_existed=0
completed=0
cp --preserve=mode,ownership,timestamps "$production_site" "$site_backup"
cp --preserve=mode,ownership,timestamps "$target_service" "$service_backup"
cp --preserve=mode,ownership,timestamps "$environment_file" "$environment_backup"
if [[ -f "$target_snippet" && ! -L "$target_snippet" ]]; then
  cp --preserve=mode,ownership,timestamps "$target_snippet" "$snippet_backup"
  snippet_existed=1
elif [[ -e "$target_snippet" || -L "$target_snippet" ]]; then
  fail "installed path snippet is unsafe"
fi

rollback() {
  local status=$?
  trap - EXIT ERR INT TERM
  if [[ "$completed" -eq 0 ]]; then
    cp --preserve=mode,ownership,timestamps "$site_backup" "$production_site" || true
    cp --preserve=mode,ownership,timestamps "$service_backup" "$target_service" || true
    cp --preserve=mode,ownership,timestamps "$environment_backup" "$environment_file" || true
    if [[ "$snippet_existed" -eq 1 ]]; then
      cp --preserve=mode,ownership,timestamps "$snippet_backup" "$target_snippet" || true
    else
      rm -f "$target_snippet"
    fi
    systemctl daemon-reload >/dev/null 2>&1 || true
    nginx -t >/dev/null 2>&1 && systemctl reload nginx >/dev/null 2>&1 || true
  fi
  rm -f "$site_backup" "$service_backup" "$environment_backup" "$snippet_backup"
  exit "$status"
}
trap rollback EXIT ERR INT TERM

install -o root -g root -m 0644 "$source_snippet" "$target_snippet"
install -o root -g root -m 0644 "$source_service" "$target_service"

python3 - "$environment_file" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
old = "L12_PUBLIC_BASE_URL=https://testrun.legion-12.com"
new = "L12_PUBLIC_BASE_URL=https://legion-12.com/testrun"
if new not in text:
    if text.count(old) != 1:
        raise SystemExit("testrun public base marker is missing or ambiguous")
    text = text.replace(old, new)
path.write_text(text, encoding="utf-8")
PY
chmod 0600 "$environment_file"

python3 - "$production_site" "$include_line" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
include = sys.argv[2]
text = path.read_text(encoding="utf-8")
if include in text:
    raise SystemExit(0)
marker = "    server_name legion-12.com;\n"
positions = [index for index in range(len(text)) if text.startswith(marker, index)]
if len(positions) != 1:
    raise SystemExit("production HTTPS host marker is not unique")
index = positions[0] + len(marker)
path.write_text(text[:index] + "\n" + include + "\n" + text[index:], encoding="utf-8")
PY

systemctl daemon-reload
nginx -t
systemctl reload nginx
completed=1
rm -f "$site_backup" "$service_backup" "$environment_backup" "$snippet_backup"
trap - EXIT ERR INT TERM
printf '[L12 testrun path] mounted https://legion-12.com/testrun/\n'
