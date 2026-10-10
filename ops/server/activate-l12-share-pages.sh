#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly source_snippet="${1:-/tmp/nginx-l12-share-pages.conf}"
readonly target_snippet="/etc/nginx/snippets/legion12-share-pages.conf"
readonly production_site="/etc/nginx/sites-available/legion12"
readonly production_enabled="/etc/nginx/sites-enabled/legion12"
readonly include_line="    include /etc/nginx/snippets/legion12-share-pages.conf;"

fail() { printf '[L12 share pages] ERROR: %s\n' "$*" >&2; exit 1; }
[[ "$(id -u)" -eq 0 ]] || fail "must run as root"
for command_name in cp date grep id install nginx python3 readlink systemctl; do
  command -v "$command_name" >/dev/null 2>&1 || fail "missing command: $command_name"
done
[[ -f "$source_snippet" && ! -L "$source_snippet" ]] || fail "source snippet is missing or unsafe"
[[ -f "$production_site" && ! -L "$production_site" ]] || fail "production Nginx site is missing or unsafe"
[[ -L "$production_enabled" && "$(readlink -f "$production_enabled")" == "$production_site" ]] \
  || fail "production Nginx site is not the active managed site"
grep -Fq 'server_name legion-12.com;' "$production_site" || fail "production host marker is missing"
grep -Fq 'proxy_pass http://127.0.0.1:8083/_l12/share-page;' "$source_snippet" \
  || fail "share page backend is invalid"
grep -Fq 'location = /news' "$source_snippet" || fail "news share route is missing"

readonly suffix="$(date -u +%Y%m%dT%H%M%SZ)-$$"
readonly site_backup="/etc/nginx/sites-available/.legion12-before-share-pages-${suffix}"
readonly snippet_backup="/etc/nginx/snippets/.legion12-share-pages-before-${suffix}.conf"
snippet_existed=0
completed=0
cp --preserve=mode,ownership,timestamps "$production_site" "$site_backup"
if [[ -f "$target_snippet" && ! -L "$target_snippet" ]]; then
  cp --preserve=mode,ownership,timestamps "$target_snippet" "$snippet_backup"
  snippet_existed=1
elif [[ -e "$target_snippet" || -L "$target_snippet" ]]; then
  fail "installed share snippet is unsafe"
fi

rollback() {
  local status=$?
  trap - EXIT ERR INT TERM
  if [[ "$completed" -eq 0 ]]; then
    cp --preserve=mode,ownership,timestamps "$site_backup" "$production_site" || true
    if [[ "$snippet_existed" -eq 1 ]]; then
      cp --preserve=mode,ownership,timestamps "$snippet_backup" "$target_snippet" || true
    else
      rm -f "$target_snippet"
    fi
    nginx -t >/dev/null 2>&1 && systemctl reload nginx >/dev/null 2>&1 || true
  fi
  rm -f "$site_backup" "$snippet_backup" "$source_snippet"
  exit "$status"
}
trap rollback EXIT ERR INT TERM

install -o root -g root -m 0644 "$source_snippet" "$target_snippet"
python3 - "$production_site" "$include_line" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
include = sys.argv[2]
text = path.read_text(encoding="utf-8")
if include not in text:
    marker = "    server_name legion-12.com;\n"
    positions = [index for index in range(len(text)) if text.startswith(marker, index)]
    if len(positions) != 1:
        raise SystemExit("production HTTPS host marker is not unique")
    index = positions[0] + len(marker)
    path.write_text(text[:index] + "\n" + include + "\n" + text[index:], encoding="utf-8")
PY

nginx -t
systemctl reload nginx
completed=1
rm -f "$site_backup" "$snippet_backup" "$source_snippet"
trap - EXIT ERR INT TERM
printf '[L12 share pages] dynamic home/news metadata routes are active\n'
