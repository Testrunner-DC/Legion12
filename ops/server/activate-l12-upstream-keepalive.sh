#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly source_snippet="${1:-/tmp/nginx-l12-upstream-keepalive.conf}"
readonly target_snippet="/etc/nginx/snippets/legion12-upstream-keepalive.conf"
readonly nginx_conf="/etc/nginx/nginx.conf"
readonly production_site="/etc/nginx/sites-available/legion12"
readonly production_enabled="/etc/nginx/sites-enabled/legion12"
readonly upstream_name="l12_app_main"
readonly include_line="    include /etc/nginx/snippets/legion12-upstream-keepalive.conf;"

fail() { printf '[L12 upstream keepalive] ERROR: %s\n' "$*" >&2; exit 1; }
[[ "$(id -u)" -eq 0 ]] || fail "must run as root"
for command_name in cp date grep id install nginx python3 readlink systemctl; do
  command -v "$command_name" >/dev/null 2>&1 || fail "missing command: $command_name"
done
[[ -f "$source_snippet" && ! -L "$source_snippet" ]] || fail "source snippet is missing or unsafe"
[[ -f "$nginx_conf" && ! -L "$nginx_conf" ]] || fail "Nginx main configuration is missing or unsafe"
[[ -f "$production_site" && ! -L "$production_site" ]] || fail "production Nginx site is missing or unsafe"
[[ -L "$production_enabled" && "$(readlink -f "$production_enabled")" == "$production_site" ]] \
  || fail "production Nginx site is not the active managed site"
grep -Fq 'server_name legion-12.com;' "$production_site" || fail "production host marker is missing"
grep -Fq "upstream ${upstream_name} {" "$source_snippet" || fail "upstream block is missing"
grep -Fq 'server 127.0.0.1:8083;' "$source_snippet" || fail "upstream backend is invalid"
grep -Fq 'keepalive 32;' "$source_snippet" || fail "upstream keepalive pool is missing"

readonly suffix="$(date -u +%Y%m%dT%H%M%SZ)-$$"
readonly conf_backup="/etc/nginx/.nginx.conf-before-upstream-keepalive-${suffix}"
readonly site_backup="/etc/nginx/sites-available/.legion12-before-upstream-keepalive-${suffix}"
readonly snippet_backup="/etc/nginx/snippets/.legion12-upstream-keepalive-before-${suffix}.conf"
snippet_existed=0
completed=0
cp --preserve=mode,ownership,timestamps "$nginx_conf" "$conf_backup"
cp --preserve=mode,ownership,timestamps "$production_site" "$site_backup"
if [[ -f "$target_snippet" && ! -L "$target_snippet" ]]; then
  cp --preserve=mode,ownership,timestamps "$target_snippet" "$snippet_backup"
  snippet_existed=1
elif [[ -e "$target_snippet" || -L "$target_snippet" ]]; then
  fail "installed keepalive snippet is unsafe"
fi

rollback() {
  local status=$?
  trap - EXIT ERR INT TERM
  if [[ "$completed" -eq 0 ]]; then
    cp --preserve=mode,ownership,timestamps "$conf_backup" "$nginx_conf" || true
    cp --preserve=mode,ownership,timestamps "$site_backup" "$production_site" || true
    if [[ "$snippet_existed" -eq 1 ]]; then
      cp --preserve=mode,ownership,timestamps "$snippet_backup" "$target_snippet" || true
    else
      rm -f "$target_snippet"
    fi
    nginx -t >/dev/null 2>&1 && systemctl reload nginx >/dev/null 2>&1 || true
  fi
  rm -f "$conf_backup" "$site_backup" "$snippet_backup" "$source_snippet"
  exit "$status"
}
trap rollback EXIT ERR INT TERM

install -o root -g root -m 0644 "$source_snippet" "$target_snippet"

# upstream 属于 http 上下文：include 行只能插在 nginx.conf 的 http 块内。
python3 - "$nginx_conf" "$include_line" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
include = sys.argv[2]
text = path.read_text(encoding="utf-8")
if include in text:
    raise SystemExit(0)
marker = "http {\n"
positions = [index for index in range(len(text)) if text.startswith(marker, index)]
if len(positions) != 1:
    raise SystemExit("http block marker is missing or ambiguous")
index = positions[0] + len(marker)
path.write_text(text[:index] + "\n" + include + "\n" + text[index:], encoding="utf-8")
PY

# 按 location 块逐个判定，绝不整文件替换：
#  - 只转换块内 proxy_pass 是不带路径的裸 127.0.0.1:8083 的块；
#  - 任何带 WebSocket upgrade 语义的块（$http_upgrade 或 Connection "upgrade"）一律跳过。
#    若在那类块里注入 proxy_set_header Connection ""，会覆盖 Connection "upgrade"，
#    握手直接失败——整站实时对战全部不可用。
#  - 带路径的 8083 反代（/ws、/_l12/share-page、站点素材上传）本来就匹配不上裸目标。
converted_total="$(python3 - "$production_site" "$upstream_name" <<'PY'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
upstream = sys.argv[2]
text = path.read_text(encoding="utf-8")
if "proxy_pass http://%s;" % upstream in text:
    print(0)
    raise SystemExit(0)

BLOCK = re.compile(r"(?m)^([ \t]*)location\b[^{]*\{")
BARE = re.compile(r"(?m)^([ \t]*)proxy_pass http://127\.0\.0\.1:8083;[ \t]*$")
UPGRADE_MARKERS = ('$http_upgrade', 'Connection "upgrade"')


def block_spans(source):
    spans = []
    for match in BLOCK.finditer(source):
        depth = 0
        index = match.end() - 1
        while index < len(source):
            character = source[index]
            if character == "{":
                depth += 1
            elif character == "}":
                depth -= 1
                if depth == 0:
                    spans.append((match.start(), match.end(), index))
                    break
            index += 1
        else:
            raise SystemExit("location block is not terminated")
    return spans


def convert(match):
    indent = match.group(1)
    return (
        indent + "proxy_http_version 1.1;\n"
        + indent + 'proxy_set_header Connection "";\n'
        + indent + "proxy_pass http://" + upstream + ";"
    )


converted = 0
skipped = []
for start, body_start, end in reversed(block_spans(text)):
    body = text[body_start:end]
    header = text[start:body_start].split("\n", 1)[0].strip()
    if any(marker in body for marker in UPGRADE_MARKERS):
        if BARE.search(body):
            skipped.append(header)
        continue
    updated, count = BARE.subn(convert, body)
    if count < 1:
        continue
    text = text[:body_start] + updated + text[end:]
    converted += count

if converted < 1:
    raise SystemExit("no non-upgrade bare 8083 proxy_pass target found")
for start, body_start, end in block_spans(text):
    body = text[body_start:end]
    if "$http_upgrade" in body and 'Connection ""' in body:
        raise SystemExit("an upgrade location received the keepalive Connection header")
path.write_text(text, encoding="utf-8")
for header in skipped:
    print("skipped upgrade route: %s" % header, file=sys.stderr)
print(converted)
PY
)"
[[ "$converted_total" =~ ^[0-9]+$ ]] || fail "conversion report is invalid"

nginx -t
if ! grep -Fq "proxy_pass http://${upstream_name};" "$production_site"; then
  fail "API proxy target was not switched to the keepalive upstream"
fi
if [[ "$converted_total" -gt 0 ]]; then
  connection_free_count="$(grep -Fc 'proxy_set_header Connection "";' "$production_site" || true)"
  [[ "$connection_free_count" -eq "$converted_total" ]] \
    || fail "keepalive Connection header count does not match converted location count"
fi
nginx_dump="$(nginx -T 2>&1)"
grep -Fq "upstream ${upstream_name} {" <<<"$nginx_dump" || fail "Nginx did not load the keepalive upstream"
systemctl reload nginx
completed=1
rm -f "$conf_backup" "$site_backup" "$snippet_backup" "$source_snippet"
trap - EXIT ERR INT TERM
if [[ "$converted_total" -gt 0 ]]; then
  printf '[L12 upstream keepalive] %s non-upgrade location(s) now reuse upstream connections\n' "$converted_total"
else
  printf '[L12 upstream keepalive] include refreshed; /api reverse proxy already used the keepalive upstream\n'
fi
