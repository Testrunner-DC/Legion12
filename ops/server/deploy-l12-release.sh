#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly active_dir="/opt/legion12-test"
readonly deployment_dir="/opt/legion12-deployment"
readonly incoming_dir="${deployment_dir}/incoming"
readonly deployment_patch="${deployment_dir}/hong-kong-test.patch"
readonly service_name="legion12-test.service"
readonly public_base="https://legion12.grand-umi.com"
readonly lock_file="/run/lock/legion12-deploy.lock"

log() { printf '[L12 部署] %s\n' "$*"; }
fail() { printf '[L12 部署] 错误：%s\n' "$*" >&2; return 1; }
require_command() { command -v "$1" >/dev/null 2>&1 || fail "服务器缺少命令：$1"; }

self_test() {
  test "$(id -u)" -eq 0 || fail "必须以 root 身份执行"
  for command_name in flock sha256sum tar patch npm node dotnet curl systemctl nginx; do
    require_command "$command_name"
  done
  test -d "$active_dir" || fail "当前部署目录不存在：${active_dir}"
  test -f "$deployment_patch" || fail "香港部署补丁不存在：${deployment_patch}"
  test -f "/etc/legion12-test.env" || fail "管理员环境配置不存在"
  systemctl cat "$service_name" >/dev/null
  nginx -t >/dev/null
  log "服务器部署环境检查通过"
}

if [[ "${1:-}" == "self-test" ]]; then
  self_test
  exit 0
fi

mode="${1:-}"
commit="${2:-}"
expected_sha256="${3:-}"
archive_path="${4:-}"
[[ "$mode" == "deploy" || "$mode" == "dry-run" ]] || fail "用法：$0 <deploy|dry-run> <提交> <SHA256> <压缩包>"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "提交哈希格式错误"
[[ "$expected_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "压缩包 SHA256 格式错误"
[[ "$archive_path" == "${incoming_dir}/legion12-${commit}.tar.gz" ]] || fail "压缩包不在允许的上传位置"

if [[ "${L12_DEPLOY_LOCKED:-0}" != "1" ]]; then
  export L12_DEPLOY_LOCKED=1
  exec flock --close --nonblock "$lock_file" "$0" "$@"
fi

self_test
mkdir -p "$incoming_dir"
test -f "$archive_path" || fail "找不到上传的压缩包"
actual_sha256="$(sha256sum "$archive_path" | awk '{print $1}')"
[[ "$actual_sha256" == "$expected_sha256" ]] || fail "压缩包 SHA256 校验失败"

while IFS= read -r member; do
  [[ "$member" != /* ]] || fail "压缩包包含绝对路径"
  [[ "/${member}/" != *"/../"* ]] || fail "压缩包包含越界路径"
done < <(tar -tzf "$archive_path")

short_commit="${commit:0:12}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
stage_dir="/opt/legion12-staging-${short_commit}-${timestamp}"
rollback_dir=""
failed_dir=""
service_stopped=0
switched=0

cleanup() {
  if [[ -n "$stage_dir" && -d "$stage_dir" ]]; then rm -rf -- "$stage_dir"; fi
  rm -f -- "$archive_path"
}

rollback_on_error() {
  status=$?
  trap - ERR INT TERM
  if [[ "$switched" -eq 1 && -n "$rollback_dir" && -d "$rollback_dir" ]]; then
    log "新版本验证失败，正在恢复上一版本"
    systemctl stop "$service_name" || true
    failed_dir="/opt/legion12-failed-${short_commit}-${timestamp}"
    if [[ -d "$active_dir" ]]; then mv "$active_dir" "$failed_dir"; fi
    mv "$rollback_dir" "$active_dir"
    systemctl start "$service_name" || true
  elif [[ "$service_stopped" -eq 1 ]]; then
    systemctl start "$service_name" || true
  fi
  cleanup
  exit "$status"
}
trap rollback_on_error ERR INT TERM

log "展开提交 ${commit}"
mkdir -p "$stage_dir"
tar --no-same-owner --no-same-permissions -xzf "$archive_path" -C "$stage_dir"
printf '%s\n' "$commit" > "${stage_dir}/.deployment-commit"

log "应用香港服务器配置补丁"
sed -i 's/\r$//' \
  "${stage_dir}/opcgpro-vue/src/l12/net.ts" \
  "${stage_dir}/服务端WebSocket/TwelveLegions/L12PlatformStore.cs"
patch --batch --forward -p1 -d "$stage_dir" < "$deployment_patch"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
log "安装前端依赖并构建"
(
  cd "${stage_dir}/opcgpro-vue"
  npm ci
  npm run build
)

log "运行后端测试"
dotnet test "${stage_dir}/TwelveLegions.Tests/TwelveLegions.Tests.csproj" --configuration Release
dotnet test "${stage_dir}/服务端WebSocket.Tests/GrandUMIServer.Tests.csproj" --configuration Release \
  --filter "FullyQualifiedName~PlatformStoreTests"

log "发布后端"
dotnet publish "${stage_dir}/服务端WebSocket/GrandUMIServer.csproj" \
  --configuration Release --output "${stage_dir}/publish"

if [[ "$mode" == "dry-run" ]]; then
  log "干运行通过：构建、测试和部署补丁均正常，未切换线上版本"
  cleanup
  trap - ERR INT TERM
  exit 0
fi

rollback_dir="/opt/legion12-rollback-${short_commit}-${timestamp}"
log "暂停服务并复制账号、Bug、官网内容和对局记录"
systemctl stop "$service_name"
service_stopped=1
mkdir -p "${stage_dir}/publish/runtime"
if [[ -d "${active_dir}/publish/runtime" ]]; then
  cp -a "${active_dir}/publish/runtime/." "${stage_dir}/publish/runtime/"
fi
chown -R legion12:legion12 "${stage_dir}/publish"
chmod 0750 "${stage_dir}/publish/runtime"

log "切换到新版本，旧版本保存在 ${rollback_dir}"
mv "$active_dir" "$rollback_dir"
switched=1
mv "$stage_dir" "$active_dir"
stage_dir=""
systemctl start "$service_name"
service_stopped=0

log "执行外网健康检查"
healthy=0
for _ in $(seq 1 30); do
  if curl -fsS "${public_base}/health" >/dev/null; then healthy=1; break; fi
  sleep 1
done
[[ "$healthy" -eq 1 ]] || fail "后端健康检查超时"
curl -fsS "${public_base}/" >/dev/null
curl -fsS "${public_base}/cards" >/dev/null
node "${active_dir}/scripts/ws-smoke.mjs" "wss://legion12.grand-umi.com/ws"

cat > "${deployment_dir}/deployment-info.txt" <<EOF
Legion12 香港测试服
源仓库：https://github.com/Testrunner-DC/Legion12
源提交：${commit}
域名：legion12.grand-umi.com
后端端口：127.0.0.1:8083
网页 Basic Auth：已移除
管理员账号：Admin
部署日期：$(date -u +%Y-%m-%dT%H:%M:%SZ)
回滚目录：${rollback_dir}
EOF

rm -f -- "$archive_path"
trap - ERR INT TERM
log "部署完成：${commit}"
