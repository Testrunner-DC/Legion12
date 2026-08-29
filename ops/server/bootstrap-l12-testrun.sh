#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly commit="${1:-}"
readonly archive_sha256="${2:-}"
readonly archive="${3:-}"
readonly cards_hash="${4:-}"
readonly card_assets_hash="${5:-}"
readonly release_root="/opt/legion12-testrun-releases"
readonly release_dir="${release_root}/${commit}"
readonly active_link="/opt/legion12-testrun"
readonly runtime_dir="/opt/legion12-testrun-runtime"
readonly service_name="legion12-testrun.service"
readonly service_template="/tmp/legion12-testrun.service"
readonly nginx_http_template="/tmp/legion12-testrun-http.nginx"
readonly nginx_available="/etc/nginx/sites-available/legion12-testrun"
readonly nginx_enabled="/etc/nginx/sites-enabled/legion12-testrun"
readonly stage_dir="/opt/legion12-testrun-staging-${commit:0:12}-$$"

fail() { printf '[L12 验收站] 错误：%s\n' "$*" >&2; exit 1; }
cleanup() { if [[ -d "$stage_dir" ]]; then rm -rf -- "$stage_dir"; fi; }
trap cleanup EXIT

[[ "$(id -u)" -eq 0 ]] || fail "必须以 root 身份执行"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "提交哈希格式错误"
[[ "$archive_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "发布包哈希格式错误"
[[ "$cards_hash" =~ ^[0-9a-f]{40}$ ]] || fail "卡图版本格式错误"
[[ "$card_assets_hash" =~ ^[0-9a-f]{64}$ ]] || fail "优化卡图版本格式错误"
[[ "$archive" == "/opt/legion12-deployment/incoming/l12-testrun-${commit}.tar.gz" ]] || fail "发布包不在验收站允许路径"
[[ -f "$archive" ]] || fail "发布包不存在"
[[ "$(sha256sum "$archive" | awk '{print $1}')" == "$archive_sha256" ]] || fail "发布包校验失败"
[[ -f "$service_template" && -f "$nginx_http_template" ]] || fail "验收站配置模板不完整"
[[ -d "/opt/legion12-static/cards/${cards_hash}" ]] || fail "卡图缓存不存在"
[[ -d "/opt/legion12-static/card-assets/${card_assets_hash}" ]] || fail "优化卡图缓存不存在"
systemd-analyze verify "$service_template"

while IFS= read -r member; do
  [[ "$member" != /* ]] || fail "发布包包含绝对路径"
  [[ "/${member}/" != *"/../"* ]] || fail "发布包包含越界路径"
done < <(tar -tzf "$archive")

if [[ ! -d "$release_dir" ]]; then
  mkdir -p "$stage_dir"
  tar --no-same-owner --no-same-permissions -xzf "$archive" -C "$stage_dir"
  [[ "$(tr -d '\r\n' < "${stage_dir}/.deployment-commit")" == "$commit" ]] || fail "发布包提交标记不匹配"
  [[ -f "${stage_dir}/publish/GrandUMIServer.dll" ]] || fail "发布包缺少后端入口"
  [[ -f "${stage_dir}/opcgpro-vue/dist/index.html" ]] || fail "发布包缺少前端首页"
  ln -s "/opt/legion12-static/cards/${cards_hash}" "${stage_dir}/opcgpro-vue/dist/cards"
  ln -s "/opt/legion12-static/card-assets/${card_assets_hash}" "${stage_dir}/opcgpro-vue/dist/card-assets"
  chmod 0755 "$stage_dir" "${stage_dir}/publish" "${stage_dir}/opcgpro-vue" "${stage_dir}/opcgpro-vue/dist"
  find "${stage_dir}/publish" "${stage_dir}/opcgpro-vue/dist" -type d -exec chmod 0755 {} +
  find "${stage_dir}/publish" "${stage_dir}/opcgpro-vue/dist" -type f -exec chmod 0644 {} +
  mkdir -p "$release_root"
  chmod 0755 "$release_root"
  mv "$stage_dir" "$release_dir"
fi

chmod 0755 "$release_root" "$release_dir"

mkdir -p "$runtime_dir"
chown -R legion12:legion12 "$runtime_dir"
chmod 0750 "$runtime_dir"
if [[ ! -e "${release_dir}/publish/runtime" ]]; then
  ln -s "$runtime_dir" "${release_dir}/publish/runtime"
fi

if [[ ! -f /etc/legion12-testrun.env ]]; then
  install -o root -g root -m 0600 /etc/legion12-test.env /etc/legion12-testrun.env
fi
install -o root -g root -m 0644 "$service_template" "/etc/systemd/system/${service_name}"
install -o root -g root -m 0644 "$nginx_http_template" "$nginx_available"
ln -sfn "$nginx_available" "$nginx_enabled"
nginx -t

next_link="/opt/.legion12-testrun-next-$$"
ln -s "$release_dir" "$next_link"
mv -Tf "$next_link" "$active_link"
systemctl daemon-reload
systemctl enable --now "$service_name"
systemctl reload nginx

healthy=0
for _ in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8084/health >/dev/null; then healthy=1; break; fi
  sleep 1
done
[[ "$healthy" -eq 1 ]] || fail "验收站后端健康检查超时"
curl -fsS -H 'Host: testrun.legion-12.com' http://127.0.0.1/ >/dev/null
curl -fsS -H 'Host: testrun.legion-12.com' http://127.0.0.1/health >/dev/null

printf '[L12 验收站] 隔离运行单元已部署：%s\n' "$commit"
printf '[L12 验收站] 正式站服务与 /opt/legion12-runtime 未修改。\n'
