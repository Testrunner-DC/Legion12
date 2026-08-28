#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly active_dir="/opt/legion12-test"
readonly releases_dir="/opt/legion12-releases"
readonly runtime_dir="/opt/legion12-runtime"
readonly static_cards_dir="/opt/legion12-static/cards"
readonly deployment_dir="/opt/legion12-deployment"
readonly incoming_dir="${deployment_dir}/incoming"
readonly service_name="legion12-test.service"
readonly public_host="legion-12.com"
readonly public_base="https://${public_host}"
readonly lock_file="/run/lock/legion12-deploy.lock"
readonly service_user="legion12"
readonly web_user="www-data"

log() { printf '[L12 部署] %s\n' "$*"; }
fail() { printf '[L12 部署] 错误：%s\n' "$*" >&2; return 1; }
require_command() { command -v "$1" >/dev/null 2>&1 || fail "服务器缺少命令：$1"; }

self_test() {
  test "$(id -u)" -eq 0 || fail "必须以 root 身份执行"
  for command_name in flock sha256sum tar curl systemctl nginx runuser node find readlink ln mv install awk tr chmod chown; do
    require_command "$command_name"
  done
  test -e "$active_dir" || fail "当前部署入口不存在：${active_dir}"
  test -f "/etc/legion12-test.env" || fail "管理员环境配置不存在"
  id "$service_user" >/dev/null 2>&1 || fail "找不到服务账号：${service_user}"
  id "$web_user" >/dev/null 2>&1 || fail "找不到 Nginx 账号：${web_user}"
  systemctl cat "$service_name" >/dev/null
  nginx -t >/dev/null
  log "服务器快速发布环境检查通过"
}

validate_archive() {
  local archive="$1"
  while IFS= read -r member; do
    [[ "$member" != /* ]] || fail "压缩包包含绝对路径"
    [[ "/${member}/" != *"/../"* ]] || fail "压缩包包含越界路径"
  done < <(tar -tzf "$archive")
}

if [[ "${1:-}" == "self-test" ]]; then
  self_test
  exit 0
fi

mode="${1:-}"
commit="${2:-}"
release_sha256="${3:-}"
release_archive="${4:-}"
cards_hash="${5:-}"
cards_sha256="${6:--}"
cards_archive="${7:--}"
[[ "$mode" == "deploy" || "$mode" == "dry-run" ]] || fail "用法：$0 <deploy|dry-run> <提交> <运行包SHA256> <运行包> <卡图版本> <卡图SHA256|-> <卡图包|->"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "提交哈希格式错误"
[[ "$release_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "运行包 SHA256 格式错误"
[[ "$cards_hash" =~ ^[0-9a-f]{40,64}$ ]] || fail "卡图版本格式错误"
[[ "$release_archive" == "${incoming_dir}/l12-release-${commit}.tar.gz" ]] || fail "运行包不在允许目录"
if [[ "$cards_archive" != "-" ]]; then
  [[ "$cards_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "卡图包 SHA256 格式错误"
  [[ "$cards_archive" == "${incoming_dir}/l12-cards-${cards_hash}.tar.gz" ]] || fail "卡图包不在允许目录"
fi

if [[ "${L12_DEPLOY_LOCKED:-0}" != "1" ]]; then
  export L12_DEPLOY_LOCKED=1
  exec flock --close --nonblock "$lock_file" "$0" "$@"
fi

self_test
mkdir -p "$incoming_dir" "$releases_dir" "$static_cards_dir"
chmod 0755 "$(dirname "$static_cards_dir")" "$static_cards_dir" "$releases_dir"
test -f "$release_archive" || fail "找不到运行包"
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "运行包 SHA256 校验失败"
validate_archive "$release_archive"

short_commit="${commit:0:12}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
stage_dir="/opt/legion12-staging-${short_commit}-${timestamp}"
stage_cards_dir=""
release_dir="${releases_dir}/${commit}-${timestamp}"
previous_target=""
legacy_dir=""
service_stopped=0
switched=0

cleanup() {
  if [[ -n "$stage_dir" && -d "$stage_dir" ]]; then rm -rf -- "$stage_dir"; fi
  if [[ -n "$stage_cards_dir" && -d "$stage_cards_dir" ]]; then rm -rf -- "$stage_cards_dir"; fi
  rm -f -- "$release_archive"
  if [[ "$cards_archive" != "-" ]]; then rm -f -- "$cards_archive"; fi
}

restore_previous() {
  local restore_link="/opt/.legion12-restore-${timestamp}"
  ln -s "$previous_target" "$restore_link"
  mv -Tf "$restore_link" "$active_dir"
}

rollback_on_error() {
  status=$?
  trap - ERR INT TERM
  if [[ "$switched" -eq 1 && -n "$previous_target" && -e "$previous_target" ]]; then
    log "新版本验证失败，正在原子恢复上一版本"
    systemctl stop "$service_name" || true
    restore_previous || true
    systemctl start "$service_name" || true
  elif [[ "$service_stopped" -eq 1 ]]; then
    systemctl start "$service_name" || true
  fi
  cleanup
  exit "$status"
}
trap rollback_on_error ERR INT TERM

log "展开预构建运行包 ${commit}"
mkdir -p "$stage_dir"
tar --no-same-owner --no-same-permissions -xzf "$release_archive" -C "$stage_dir"
test -f "${stage_dir}/.deployment-commit" || fail "运行包缺少提交标记"
[[ "$(tr -d '\r\n' < "${stage_dir}/.deployment-commit")" == "$commit" ]] || fail "运行包提交标记不匹配"
test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "运行包缺少后端入口"
test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "运行包缺少前端首页"
test -r "${stage_dir}/scripts/ws-smoke.mjs" || fail "运行包缺少 WebSocket 冒烟脚本"
test ! -e "${stage_dir}/opcgpro-vue/dist/cards" || fail "运行包不应重复携带卡图缓存"

cards_target="${static_cards_dir}/${cards_hash}"
if [[ ! -d "$cards_target" ]]; then
  [[ "$cards_archive" != "-" ]] || fail "服务器没有该卡图缓存，且未提供卡图包"
  test -f "$cards_archive" || fail "找不到卡图包"
  [[ "$(sha256sum "$cards_archive" | awk '{print $1}')" == "$cards_sha256" ]] || fail "卡图包 SHA256 校验失败"
  validate_archive "$cards_archive"
  stage_cards_dir="/opt/legion12-cards-staging-${cards_hash}-${timestamp}"
  mkdir -p "$stage_cards_dir"
  tar --no-same-owner --no-same-permissions -xzf "$cards_archive" -C "$stage_cards_dir"
  test -d "${stage_cards_dir}/cards" || fail "卡图包目录结构错误"
  chmod 0755 "$stage_cards_dir"
  find "${stage_cards_dir}/cards" -type d -exec chmod 0755 {} +
  find "${stage_cards_dir}/cards" -type f -exec chmod 0644 {} +
  if [[ "$mode" == "deploy" ]]; then
    mv "${stage_cards_dir}/cards" "$cards_target"
    rmdir "$stage_cards_dir"
    stage_cards_dir=""
  else
    cards_target="${stage_cards_dir}/cards"
  fi
fi

ln -s "$cards_target" "${stage_dir}/opcgpro-vue/dist/cards"
chmod 0755 "$stage_dir" "${stage_dir}/opcgpro-vue" "${stage_dir}/opcgpro-vue/dist" "${stage_dir}/publish"
find "${stage_dir}/opcgpro-vue/dist" -type d -exec chmod 0755 {} +
find "${stage_dir}/opcgpro-vue/dist" -type f -exec chmod 0644 {} +
find "${stage_dir}/publish" -type d -exec chmod 0755 {} +
find "${stage_dir}/publish" -type f -exec chmod 0644 {} +
runuser -u "$service_user" -- test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "服务账号无法读取后端入口"
runuser -u "$web_user" -- test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "Nginx 账号无法读取前端首页"
sample_card="$(find "$cards_target" -type f -print -quit)"
test -n "$sample_card" || fail "卡图缓存为空"
runuser -u "$web_user" -- test -r "$sample_card" || fail "Nginx 账号无法读取卡图缓存"

if [[ "$mode" == "dry-run" ]]; then
  log "快速干运行通过：产物、哈希、目录结构及真实账号权限均正常"
  cleanup
  trap - ERR INT TERM
  exit 0
fi

log "暂停服务并首次分离持久化运行数据"
systemctl stop "$service_name"
service_stopped=1
if [[ ! -d "$runtime_dir" ]]; then
  test -d "${active_dir}/publish/runtime" || fail "当前版本缺少运行数据目录"
  mv "${active_dir}/publish/runtime" "$runtime_dir"
  ln -s "$runtime_dir" "${active_dir}/publish/runtime"
fi
chown -R "${service_user}:${service_user}" "$runtime_dir"
chmod 0750 "$runtime_dir"
ln -s "$runtime_dir" "${stage_dir}/publish/runtime"

mkdir -p "/etc/systemd/system/${service_name}.d"
cat > "/etc/systemd/system/${service_name}.d/runtime.conf" <<EOF
[Service]
ReadWritePaths=
ReadWritePaths=${runtime_dir}
EOF
systemctl daemon-reload

mv "$stage_dir" "$release_dir"
stage_dir=""
if [[ -L "$active_dir" ]]; then
  previous_target="$(readlink -f "$active_dir")"
else
  legacy_dir="/opt/legion12-legacy-${timestamp}"
  mv "$active_dir" "$legacy_dir"
  previous_target="$legacy_dir"
fi

next_link="/opt/.legion12-test-next-${timestamp}"
ln -s "$release_dir" "$next_link"
switched=1
mv -Tf "$next_link" "$active_dir"
systemctl start "$service_name"
service_stopped=0

log "验证公网 HTTP 健康状态"
healthy=0
for _ in $(seq 1 30); do
  if curl -fsS "${public_base}/health" >/dev/null; then healthy=1; break; fi
  sleep 1
done
[[ "$healthy" -eq 1 ]] || fail "后端健康检查超时"
curl -fsS "${public_base}/" >/dev/null
curl -fsS "${public_base}/cards" >/dev/null
log "验证公网 WebSocket 建连与无状态部署协议"
timeout 15s node "${active_dir}/scripts/ws-smoke.mjs" "wss://${public_host}/ws"

cat > "${deployment_dir}/deployment-info.txt" <<EOF
Legion12 香港测试服
源仓库：https://github.com/Testrunner-DC/Legion12
源提交：${commit}
活动版本：${release_dir}
上一版本：${previous_target}
共享运行数据：${runtime_dir}
共享卡图版本：${cards_hash}
域名：${public_host}
部署日期：$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

rm -f -- "$release_archive"
if [[ "$cards_archive" != "-" ]]; then rm -f -- "$cards_archive"; fi
trap - ERR INT TERM
log "快速部署完成：${commit}"
