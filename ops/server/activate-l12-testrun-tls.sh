#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly public_host="testrun.legion-12.com"
readonly active_dir="/opt/legion12-testrun"
readonly service_name="legion12-testrun.service"
readonly http_site="/etc/nginx/sites-available/legion12-testrun-http"
readonly tls_site="/etc/nginx/sites-available/legion12-testrun-tls"
readonly enabled_site="/etc/nginx/sites-enabled/legion12-testrun"
readonly health_verifier="/usr/local/libexec/verify-legion12-testrun-health.mjs"
readonly certificate="/etc/letsencrypt/live/${public_host}/fullchain.pem"
readonly private_key="/etc/letsencrypt/live/${public_host}/privkey.pem"

log() { printf '[L12 testrun TLS] %s\n' "$*"; }
fail() { printf '[L12 testrun TLS] ERROR: %s\n' "$*" >&2; return 1; }

[[ "$(id -u)" -eq 0 ]] || { fail "must run as root"; exit 1; }
for command_name in nginx systemctl curl node timeout readlink ln mv; do
  command -v "$command_name" >/dev/null 2>&1 || { fail "missing command: ${command_name}"; exit 1; }
done
[[ -f "$http_site" && ! -L "$http_site" ]] || { fail "HTTP bootstrap site is missing"; exit 1; }
[[ -f "$tls_site" && ! -L "$tls_site" ]] || { fail "TLS site is missing"; exit 1; }
[[ -f "$certificate" && -f "$private_key" ]] || { fail "ACME certificate is not ready"; exit 1; }
[[ -f "$health_verifier" && ! -L "$health_verifier" ]] || { fail "health verifier is missing"; exit 1; }
[[ -L "$active_dir" ]] || { fail "testrun active release is not initialized"; exit 1; }
systemctl is-enabled --quiet "$service_name" || { fail "testrun service is not enabled"; exit 1; }
systemctl is-active --quiet "$service_name" || { fail "testrun service is not active"; exit 1; }

current_site="$(readlink -f "$enabled_site" 2>/dev/null || true)"
if [[ "$current_site" == "$tls_site" ]]; then
  log "TLS site is already active; no change made"
  exit 0
fi
[[ "$current_site" == "$http_site" ]] || { fail "enabled site is not the managed HTTP bootstrap site"; exit 1; }

expected_commit="$(tr -d '\r\n' < "${active_dir}/.deployment-commit")"
[[ "$expected_commit" =~ ^[0-9a-f]{40}$ ]] || { fail "active release commit marker is invalid"; exit 1; }

restore_http=0
rollback_tls() {
  local status=$?
  trap - ERR INT TERM
  set +e
  if [[ "$restore_http" -eq 1 ]]; then
    rollback_link="/etc/nginx/sites-enabled/.legion12-testrun-http-rollback-$$"
    ln -s "$http_site" "$rollback_link" && mv -Tf "$rollback_link" "$enabled_site"
    nginx -t >/dev/null 2>&1 && systemctl reload nginx >/dev/null 2>&1
    log "TLS activation failed; restored the ACME-only HTTP site"
  fi
  exit "$status"
}
trap rollback_tls ERR INT TERM

next_link="/etc/nginx/sites-enabled/.legion12-testrun-tls-next-$$"
ln -s "$tls_site" "$next_link"
mv -Tf "$next_link" "$enabled_site"
restore_http=1
nginx -t
systemctl reload nginx

health_json="$(curl -fsS --connect-timeout 5 --max-time 10 -H 'Cache-Control: no-cache' "https://${public_host}/health")"
printf '%s' "$health_json" | node "$health_verifier" "$expected_commit" >/dev/null
curl -fsS --connect-timeout 5 --max-time 10 "https://${public_host}/" >/dev/null
timeout 15s node "${active_dir}/scripts/ws-smoke.mjs" "wss://${public_host}/ws"

restore_http=0
trap - ERR INT TERM
log "public HTTPS and WebSocket verification passed for ${expected_commit}"
