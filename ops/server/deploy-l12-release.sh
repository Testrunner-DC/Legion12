#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly test_root="${L12_DEPLOY_TEST_ROOT:-}"
if [[ -n "$test_root" ]]; then
  [[ "${L12_DEPLOY_TEST_MODE:-0}" == "1" && "$test_root" == /* && "$test_root" != "/" && "$test_root" == *"/l12-deploy-behavior-"* ]] \
    || { printf '[L12 部署] 错误：测试根目录不满足隔离约束\n' >&2; exit 2; }
fi
log() { printf '[L12 部署] %s\n' "$*"; }
fail() { printf '[L12 部署] 错误：%s\n' "$*" >&2; return 1; }

readonly requested_mode="${1:-}"
if [[ "$requested_mode" == "prepare-storage" ]]; then
  artifact_root_option="${2:-}"
else
  artifact_root_option="${11:-/opt}"
fi
case "$artifact_root_option" in
  /opt|/www/legion12) ;;
  *) fail "服务器制品根只允许 /opt 或 /www/legion12"; exit 2 ;;
esac
readonly artifact_root_option
readonly external_artifact_mode="$(if [[ "$artifact_root_option" == "/www/legion12" ]]; then printf 1; else printf 0; fi)"

readonly active_dir="${test_root}/opt/legion12-test"
readonly runtime_dir="${test_root}/opt/legion12-runtime"
readonly sandbox_fence="${runtime_dir}/.maintenance-sandbox-drain"
readonly deployment_dir="${test_root}/opt/legion12-deployment"
readonly failure_dir="${deployment_dir}/failures"
readonly service_name="legion12-test.service"
readonly public_host="legion-12.com"
readonly lock_file="${test_root}/run/lock/legion12-deploy.lock"
readonly service_override_dir="${test_root}/etc/systemd/system/${service_name}.d"
readonly environment_file="${test_root}/etc/legion12-test.env"
readonly default_releases_dir="${test_root}/opt/legion12-releases"
readonly default_incoming_dir="${deployment_dir}/incoming"
readonly default_runtime_backup_dir="${deployment_dir}/runtime-backups"
readonly default_static_card_assets_dir="${test_root}/opt/legion12-static/card-assets"
readonly external_mount="${test_root}/www"
readonly external_artifact_root="${external_mount}/legion12"
if [[ "$external_artifact_mode" == "1" ]]; then
  readonly artifact_root="$external_artifact_root"
  readonly releases_dir="${artifact_root}/releases"
  readonly incoming_dir="${artifact_root}/incoming"
  readonly runtime_backup_dir="${artifact_root}/runtime-backups"
  readonly static_card_assets_dir="${artifact_root}/card-assets"
  readonly stage_parent="${artifact_root}/staging"
else
  readonly artifact_root="${test_root}/opt"
  readonly releases_dir="$default_releases_dir"
  readonly incoming_dir="$default_incoming_dir"
  readonly runtime_backup_dir="$default_runtime_backup_dir"
  readonly static_card_assets_dir="$default_static_card_assets_dir"
  readonly stage_parent="${test_root}/opt"
fi
readonly external_prepare_min_bytes=$((14 * 1024 * 1024 * 1024))
readonly external_backup_max_bytes=$((4 * 1024 * 1024 * 1024))
readonly external_reserve_bytes=$((8 * 1024 * 1024 * 1024))
readonly external_release_unpacked_max_bytes=$((1 * 1024 * 1024 * 1024))
readonly external_card_unpacked_max_bytes=$((512 * 1024 * 1024))
if [[ -n "$test_root" ]]; then
  readonly public_base="${L12_DEPLOY_PUBLIC_BASE:-https://${public_host}}"
  readonly local_base="${L12_DEPLOY_LOCAL_BASE:-http://127.0.0.1:8083}"
  readonly health_verifier="${L12_DEPLOY_HEALTH_VERIFIER:-${test_root}/usr/local/libexec/verify-legion12-health.mjs}"
  readonly health_attempts="${L12_DEPLOY_HEALTH_ATTEMPTS:-30}"
  readonly health_delay_seconds="${L12_DEPLOY_HEALTH_DELAY_SECONDS:-1}"
else
  readonly public_base="https://${public_host}"
  readonly local_base="http://127.0.0.1:8083"
  readonly health_verifier="/usr/local/libexec/verify-legion12-health.mjs"
  readonly health_attempts="30"
  readonly health_delay_seconds="1"
fi
readonly service_user="legion12"
readonly web_user="www-data"

require_command() { command -v "$1" >/dev/null 2>&1 || fail "服务器缺少命令：$1"; }

assert_plain_directory() {
  local path="$1"
  local resolved
  if [[ ! -d "$path" || -L "$path" ]]; then
    fail "受管目录不是普通目录：${path}"
    return 1
  fi
  if ! resolved="$(readlink -f -- "$path")"; then
    fail "无法解析受管目录：${path}"
    return 1
  fi
  if [[ "$resolved" != "$path" ]]; then
    fail "受管目录包含符号链接或越界：${path}"
    return 1
  fi
  return 0
}

assert_existing_directories_plain() {
  local path
  for path in "$@"; do
    if [[ -e "$path" || -L "$path" ]]; then
      if ! assert_plain_directory "$path"; then return 1; fi
    fi
  done
  return 0
}

external_available_bytes() {
  local available
  if [[ -n "$test_root" ]]; then
    available="${L12_DEPLOY_TEST_EXTERNAL_AVAILABLE_BYTES:-$((20 * 1024 * 1024 * 1024))}"
  else
    if ! require_command df; then return 1; fi
    if ! available="$(df -B1 --output=avail "$external_mount" | awk 'NR == 2 { gsub(/[[:space:]]/, "", $1); print $1 }')"; then
      fail "无法读取外置制品盘可用容量"
      return 1
    fi
  fi
  if [[ ! "$available" =~ ^[0-9]+$ ]]; then
    fail "无法读取外置制品盘可用容量"
    return 1
  fi
  printf '%s\n' "$available"
  return 0
}

validate_external_mount() {
  [[ "$external_artifact_mode" == "1" ]] || return 0
  local mount_target root_source external_source mount_options configured_target
  if ! assert_plain_directory "$external_mount"; then return 1; fi
  if [[ -n "$test_root" ]]; then
    mount_target="${L12_DEPLOY_TEST_EXTERNAL_MOUNT_TARGET:-$external_mount}"
    root_source="test-root-device"
    external_source="${L12_DEPLOY_TEST_EXTERNAL_MOUNT_SOURCE:-test-external-device}"
    mount_options="${L12_DEPLOY_TEST_EXTERNAL_MOUNT_OPTIONS:-rw,relatime}"
    configured_target="${L12_DEPLOY_TEST_EXTERNAL_FSTAB_TARGET:-$external_mount}"
  else
    if ! require_command findmnt; then return 1; fi
    if ! mount_target="$(findmnt -n -T "$external_mount" -o TARGET)"; then return 1; fi
    if ! root_source="$(findmnt -n -T / -o SOURCE)"; then return 1; fi
    if ! external_source="$(findmnt -n -T "$external_mount" -o SOURCE)"; then return 1; fi
    if ! mount_options="$(findmnt -n -T "$external_mount" -o OPTIONS)"; then return 1; fi
    if ! configured_target="$(findmnt --fstab -n -T "$external_mount" -o TARGET)"; then return 1; fi
  fi
  if [[ "$mount_target" != "$external_mount" ]]; then
    fail "外置制品根不在独立精确挂载点"
    return 1
  fi
  if [[ -z "$root_source" || -z "$external_source" || "$root_source" == "$external_source" ]]; then
    fail "外置制品盘与根盘不是独立文件系统"
    return 1
  fi
  if [[ ",$mount_options," != *,rw,* || ",$mount_options," == *,ro,* ]]; then
    fail "外置制品盘不是发布流程所需的 rw 挂载"
    return 1
  fi
  if [[ "$configured_target" != "$external_mount" ]]; then
    fail "外置制品盘缺少持久挂载配置"
    return 1
  fi
  return 0
}

validate_external_prepare_capacity() {
  [[ "$external_artifact_mode" == "1" ]] || return 0
  local available
  if ! available="$(external_available_bytes)"; then return 1; fi
  if (( available < external_prepare_min_bytes )); then
    fail "外置制品盘可用容量不足 14 GiB；停止上传、解包和全量快照"
    return 1
  fi
  return 0
}

prepare_storage_paths() {
  if [[ "$external_artifact_mode" == "1" ]]; then
    if ! validate_external_mount; then return 1; fi
    if ! validate_external_prepare_capacity; then return 1; fi
    if ! assert_existing_directories_plain \
      "$artifact_root" "$incoming_dir" "$runtime_backup_dir" "$static_card_assets_dir" "$stage_parent" "$releases_dir"; then return 1; fi
    if ! mkdir -p "$artifact_root" "$incoming_dir" "$runtime_backup_dir" "$static_card_assets_dir" "$stage_parent" "$releases_dir"; then return 1; fi
    if ! chmod 0755 "$artifact_root" "$static_card_assets_dir" "$stage_parent" "$releases_dir"; then return 1; fi
    if ! chmod 0700 "$incoming_dir" "$runtime_backup_dir"; then return 1; fi
    if ! assert_plain_directory "$artifact_root"; then return 1; fi
    if ! assert_plain_directory "$incoming_dir"; then return 1; fi
    if ! assert_plain_directory "$runtime_backup_dir"; then return 1; fi
    if ! assert_plain_directory "$static_card_assets_dir"; then return 1; fi
    if ! assert_plain_directory "$stage_parent"; then return 1; fi
    if ! assert_plain_directory "$releases_dir"; then return 1; fi
    if [[ -z "$test_root" ]]; then
      if [[ "$(stat -c '%a' "$artifact_root")" != "755" || "$(stat -c '%a' "$stage_parent")" != "755" ||
            "$(stat -c '%a' "$releases_dir")" != "755" || "$(stat -c '%a' "$static_card_assets_dir")" != "755" ||
            "$(stat -c '%a' "$incoming_dir")" != "700" || "$(stat -c '%a' "$runtime_backup_dir")" != "700" ]]; then
        fail "外置制品目录权限不符合发布边界"
        return 1
      fi
    fi
    if ! runuser -u "$service_user" -- test -x "$artifact_root" -a -x "$stage_parent" -a -x "$releases_dir"; then
      fail "服务账号无法穿越外置 release/staging 路径"
      return 1
    fi
    if ! runuser -u "$web_user" -- test -x "$artifact_root" -a -x "$stage_parent" -a -x "$static_card_assets_dir"; then
      fail "Nginx 账号无法穿越外置 release/card-assets 路径"
      return 1
    fi
  else
    if ! mkdir -p "$incoming_dir" "$releases_dir" "$static_card_assets_dir"; then return 1; fi
    if ! chmod 0755 "$(dirname "$static_card_assets_dir")" "$static_card_assets_dir" "$releases_dir"; then return 1; fi
  fi
  if ! mkdir -p "$deployment_dir"; then return 1; fi
  return 0
}

archive_unpacked_bytes() {
  local archive="$1"
  local total
  if ! total="$(tar --numeric-owner -tvzf "$archive" | awk '{ total += $3 } END { printf "%.0f", total }')"; then
    fail "无法计算压缩包解包容量"
    return 1
  fi
  if [[ ! "$total" =~ ^[0-9]+$ ]]; then
    fail "无法计算压缩包解包容量"
    return 1
  fi
  printf '%s\n' "$total"
  return 0
}

validate_external_deploy_capacity() {
  local release_unpacked_bytes="$1"
  local card_unpacked_bytes="$2"
  [[ "$external_artifact_mode" == "1" ]] || return 0
  if (( release_unpacked_bytes > external_release_unpacked_max_bytes )); then
    fail "运行包解包容量超过外置发布上限"
    return 1
  fi
  if (( card_unpacked_bytes > external_card_unpacked_max_bytes )); then
    fail "卡图包解包容量超过外置发布上限"
    return 1
  fi
  local available required
  if ! available="$(external_available_bytes)"; then return 1; fi
  required=$((external_backup_max_bytes + release_unpacked_bytes + card_unpacked_bytes + external_reserve_bytes))
  if (( available < required )); then
    fail "外置制品盘无法同时容纳有界快照、解包内容和 8 GiB 余量；停止发布且不自动删除旧备份"
    return 1
  fi
  return 0
}

assert_new_path() {
  local path="$1"
  if [[ -e "$path" || -L "$path" ]]; then
    fail "拒绝覆盖既有发布制品：${path}"
    return 1
  fi
  return 0
}

create_owned_directory() {
  local path="$1"
  if ! assert_new_path "$path"; then return 1; fi
  if ! mkdir "$path"; then
    fail "无法创建本次发布专属目录：${path}"
    return 1
  fi
  return 0
}

create_owned_file() {
  local path="$1"
  if ! assert_new_path "$path"; then return 1; fi
  if ! ( set -o noclobber; : > "$path" ); then
    fail "无法独占创建本次发布临时文件：${path}"
    return 1
  fi
  return 0
}

assert_incoming_file() {
  local path="$1"
  local expected="$2"
  local resolved_parent
  if [[ "$path" != "$expected" ]]; then
    fail "上传包不在固定 incoming 目录"
    return 1
  fi
  if [[ ! -f "$path" || -L "$path" ]]; then
    fail "上传包不是普通文件"
    return 1
  fi
  if ! resolved_parent="$(readlink -f -- "$(dirname "$path")")"; then
    fail "无法解析上传包父目录"
    return 1
  fi
  if [[ "$resolved_parent" != "$incoming_dir" ]]; then
    fail "上传包路径包含符号链接或越界"
    return 1
  fi
  return 0
}

assert_deployment_unblocked() {
  local blocked_file="${deployment_dir}/deployment-blocked.txt"
  if [[ -e "$blocked_file" || -L "$blocked_file" ]]; then
    fail "检测到未完成人工对账的发布阻断标记：${blocked_file}；拒绝继续。请先完成对账，再由人工移除该标记"
  fi
}

self_test() {
  test "$(id -u)" -eq 0 || fail "必须以 root 身份执行"
  for command_name in id flock sha256sum tar curl systemctl nginx runuser node find readlink ln mv install awk grep tr chmod chown sort timeout date seq head stat dirname basename; do
    require_command "$command_name"
  done
  test -e "$active_dir" || fail "当前部署入口不存在：${active_dir}"
  test -f "$environment_file" || fail "管理员环境配置不存在"
  test -f "$health_verifier" || fail "缺少提交身份校验器：${health_verifier}"
  [[ "$health_attempts" =~ ^[1-9][0-9]*$ ]] || fail "健康检查重试次数无效"
  [[ "$health_delay_seconds" =~ ^[0-9]+([.][0-9]+)?$ ]] || fail "健康检查间隔无效"
  id "$service_user" >/dev/null 2>&1 || fail "找不到服务账号：${service_user}"
  id "$web_user" >/dev/null 2>&1 || fail "找不到 Nginx 账号：${web_user}"
  systemctl cat "$service_name" >/dev/null
  systemctl is-enabled --quiet "$service_name" || fail "正式服务未启用；可能位于已退役节点，拒绝发布"
  nginx -t >/dev/null
  local nginx_dump
  nginx_dump="$(nginx -T 2>&1)"
  grep -Fq 'location = /api/admin/site/media' <<<"$nginx_dump" || fail "Nginx 未为站点素材上传配置精确路由"
  grep -Fq 'client_max_body_size 32m' <<<"$nginx_dump" || fail "Nginx 站点素材上传上限不是 32m"
  grep -Fq 'media_upload_too_large' <<<"$nginx_dump" || fail "Nginx 站点素材 413 未返回可识别 JSON"
  log "服务器快速发布环境检查通过"
}

validate_archive() {
  local archive="$1"
  while IFS= read -r member; do
    [[ "$member" != /* ]] || fail "压缩包包含绝对路径"
    [[ "/${member}/" != *"/../"* ]] || fail "压缩包包含越界路径"
  done < <(tar -tzf "$archive")
}

read_release_commit() {
  local release_root="$1"
  local marker="${release_root}/.deployment-commit"
  local release_commit
  test -f "$marker" || fail "当前版本缺少提交标记：${marker}"
  release_commit="$(tr -d '\r\n' < "$marker")"
  [[ "$release_commit" =~ ^[0-9a-f]{40}$ ]] || fail "当前版本提交标记无效：${marker}"
  printf '%s\n' "$release_commit"
}

verify_health_once() {
  local base_url="$1"
  local expected_commit="$2"
  local response
  response="$(curl -fsS --connect-timeout 5 --max-time 10 -H 'Cache-Control: no-cache' "${base_url}/health")" || return 1
  printf '%s' "$response" | node "$health_verifier" "$expected_commit" --allow-maintenance >/dev/null
}

wait_for_exact_health() {
  local base_url="$1"
  local expected_commit="$2"
  local label="$3"
  local attempt
  for ((attempt=1; attempt<=health_attempts; attempt+=1)); do
    if verify_health_once "$base_url" "$expected_commit"; then
      log "${label}提交身份核验通过：${expected_commit}"
      return 0
    fi
    if (( attempt < health_attempts )); then sleep "$health_delay_seconds"; fi
  done
  fail "${label}健康响应未精确匹配目标提交：${expected_commit}"
}

verify_release_health() {
  local expected_commit="$1"
  local label="$2"
  wait_for_exact_health "$local_base" "$expected_commit" "${label}本机后端" || return 1
  wait_for_exact_health "$public_base" "$expected_commit" "${label}公网" || return 1
}

verify_release_websocket() {
  local label="$1"
  timeout 15s node "${active_dir}/scripts/ws-smoke.mjs" "ws://127.0.0.1:8083/ws" || return 1
  timeout 15s node "${active_dir}/scripts/ws-smoke.mjs" "wss://${public_host}/ws" || return 1
  log "${label}本机与公网 WebSocket 无状态探针通过"
}

validate_card_assets_tree() {
  local root="$1"
  local expected_hash="$2"
  if [[ -n "$test_root" && "${L12_DEPLOY_TEST_SKIP_CARD_ASSET_CONTENT_VALIDATION:-0}" == "1" ]]; then
    assert_plain_directory "$root" || return 1
    test -f "${root}/card-assets.manifest.json" || return 1
    test -f "${root}/card-assets.preload.json" || return 1
    test -d "${root}/cards" || return 1
    return 0
  fi
  node - "$root" "$expected_hash" <<'NODE'
const { createHash } = require('node:crypto')
const { lstatSync, readFileSync } = require('node:fs')
const { join, posix } = require('node:path')

const root = process.argv[2]
const expectedHash = process.argv[3]
const fail = message => { throw new Error(message) }
const rootStat = lstatSync(root)
if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) fail('优化卡图版本根目录必须是普通目录')
const safeFile = relative => {
  let current = root
  const segments = relative.split('/')
  for (let index = 0; index < segments.length; index += 1) {
    current = join(current, segments[index])
    const item = lstatSync(current)
    if (item.isSymbolicLink()) fail(`优化卡图路径不得包含符号链接：${relative}`)
    if (index < segments.length - 1 && !item.isDirectory()) fail(`优化卡图父路径不是目录：${relative}`)
    if (index === segments.length - 1 && !item.isFile()) fail(`优化卡图不是普通文件：${relative}`)
  }
  return current
}
const manifest = JSON.parse(readFileSync(safeFile('card-assets.manifest.json'), 'utf8'))
const preload = JSON.parse(readFileSync(safeFile('card-assets.preload.json'), 'utf8'))
const requiredVariants = {
  originalWebp: 'original.webp',
  thumbWebp: 'thumb-240.webp',
  boardWebp: 'board-480.webp',
  detailWebp: 'detail-960.webp',
  detailAvif: 'detail-960.avif',
}

if (manifest.schemaVersion !== 3 || manifest.complete !== true || manifest.cardCount !== 366 || manifest.playableCardCount !== 324 || manifest.presentationCardCount !== 42) fail('manifest 必须是完整 schema v3 且包含 324 张可玩卡和 42 张展示版本')
if (manifest.assetVersion !== expectedHash || !/^[0-9a-f]{64}$/.test(expectedHash)) fail('manifest 资产版本不匹配')
if (!/^[A-Za-z0-9._-]+$/.test(manifest.catalogVersion)) fail('目录版本包含不安全字符')
if (manifest.basePath !== '/card-assets' || manifest.missing?.length !== 0) fail('manifest 基础路径或缺失列表无效')
const cards = manifest.cards && typeof manifest.cards === 'object' ? manifest.cards : {}
const entries = Object.entries(cards)
if (entries.length !== 366 || new Set(entries.map(([id]) => id)).size !== 366) fail('manifest 卡号不是 366 个唯一值')

let totalBytes = 0
let playableCount = 0
let presentationCount = 0
const versionRows = []
for (const [cardId, card] of entries) {
  if (!/^(?:S\d{2}|ST\d{2}|ST)-[A-Za-z0-9]+$/.test(cardId) || card.cardId !== cardId) fail(`非法卡号：${cardId}`)
  if (!/^[0-9a-f]{64}$/.test(card.contentHash)) fail(`内容哈希无效：${cardId}`)
  if (card.presentationOnly === true) {
    presentationCount += 1
    if (typeof card.baseCardId !== 'string'
        || !/^(?:S\d{2}|ST\d{2}|ST)-[A-Za-z0-9]+$/.test(card.baseCardId)
        || card.baseCardId === cardId) fail(`展示资源基底无效：${cardId}`)
    const baseCard = cards[card.baseCardId]
    if (!baseCard || baseCard.presentationOnly !== false) fail(`展示资源基底不是可玩卡：${cardId}`)
  } else if (card.presentationOnly === false) {
    playableCount += 1
    if (card.baseCardId !== undefined) fail(`可玩卡不得声明展示基底：${cardId}`)
  } else {
    fail(`资源展示身份无效：${cardId}`)
  }
  const prefix = `cards/${manifest.catalogVersion}/${cardId}/${card.contentHash.slice(0, 20)}/`
  for (const [variant, fileName] of Object.entries(requiredVariants)) {
    const relative = card.variants?.[variant]
    if (relative !== `${prefix}${fileName}` || relative.startsWith('/') || relative.includes('\\') || posix.normalize(relative) !== relative) fail(`变体路径无效：${cardId}:${variant}`)
    const file = lstatSync(safeFile(relative))
    if (!Number.isSafeInteger(card.bytes?.[variant]) || card.bytes[variant] !== file.size) fail(`变体大小不匹配：${cardId}:${variant}`)
    totalBytes += file.size
  }
  versionRows.push([
    cardId,
    card.contentHash,
    card.presentationOnly ? 'presentation' : 'playable',
    card.baseCardId || '',
  ].join(':'))
}
if (presentationCount !== manifest.presentationCardCount || playableCount !== manifest.playableCardCount) fail('manifest 可玩卡与展示资源数量不匹配')
const actualVersion = createHash('sha256').update(versionRows.sort().join('\n')).digest('hex')
if (actualVersion !== expectedHash) fail('366 张资源的内容哈希与展示身份聚合版本不匹配')
if (manifest.totalBytes !== totalBytes || totalBytes > 400 * 1024 * 1024) fail('优化卡图总量与 manifest 不匹配或超过 400 MiB')
if (!Array.isArray(preload.entries)) fail('preload 清单格式无效')
for (const entry of preload.entries) {
  if (!cards[entry.cardId] || entry.url !== `/card-assets/${cards[entry.cardId].variants.thumbWebp}`) fail(`preload 条目无效：${entry.cardId}`)
}
NODE
}

if [[ "${1:-}" == "self-test" ]]; then
  self_test
  exit 0
fi

if [[ "$requested_mode" == "prepare-storage" ]]; then
  if [[ "$artifact_root_option" != "/opt" && "$artifact_root_option" != "/www/legion12" ]]; then
    fail "用法：$0 prepare-storage </opt|/www/legion12>"
    exit 2
  fi
  if [[ "${L12_DEPLOY_LOCKED:-0}" != "1" ]]; then
    export L12_DEPLOY_LOCKED=1
    exec flock --close --nonblock "$lock_file" "$0" "$@"
  fi
  assert_deployment_unblocked
  self_test
  prepare_storage_paths
  log "服务器制品目录已通过固定路径、挂载与容量预检：${artifact_root_option}"
  exit 0
fi

mode="$requested_mode"
commit="${2:-}"
release_sha256="${3:-}"
release_archive="${4:-}"
legacy_cards_hash="${5:--}"
legacy_cards_sha256="${6:--}"
legacy_cards_archive="${7:--}"
card_assets_hash="${8:--}"
card_assets_sha256="${9:--}"
card_assets_archive="${10:--}"
[[ "$mode" == "deploy" || "$mode" == "dry-run" ]] || fail "用法：$0 <deploy|dry-run> <提交> <运行包SHA256> <运行包> <- legacy已退役> <- > <- > <优化卡图版本> <优化卡图SHA256|-> <优化卡图包|-> [/opt|/www/legion12]"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "提交哈希格式错误"
[[ "$release_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "运行包 SHA256 格式错误"
assert_incoming_file "$release_archive" "${incoming_dir}/l12-release-${commit}.tar.gz"
[[ "$legacy_cards_hash" == "-" && "$legacy_cards_sha256" == "-" && "$legacy_cards_archive" == "-" ]] || fail "旧版 /cards 卡图链路已退役"

if [[ "$card_assets_hash" == "-" ]]; then
  [[ "$card_assets_sha256" == "-" && "$card_assets_archive" == "-" ]] || fail "优化卡图参数必须全部为 - 或全部提供"
else
  [[ "$card_assets_hash" =~ ^[0-9a-f]{64}$ ]] || fail "优化卡图版本格式错误"
  if [[ "$card_assets_archive" != "-" ]]; then
    [[ "$card_assets_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "优化卡图包 SHA256 格式错误"
    assert_incoming_file "$card_assets_archive" "${incoming_dir}/l12-card-assets-${card_assets_hash}.tar.gz"
  else
    [[ "$card_assets_sha256" == "-" ]] || fail "复用优化卡图缓存时 SHA256 必须为 -"
  fi
fi

if [[ "${L12_DEPLOY_LOCKED:-0}" != "1" ]]; then
  export L12_DEPLOY_LOCKED=1
  exec flock --close --nonblock "$lock_file" "$0" "$@"
fi

assert_deployment_unblocked
self_test
if [[ "$card_assets_hash" != "-" ]]; then
  nginx_dump="$(nginx -T 2>&1)"
  grep -Fq 'location = /card-assets/card-assets.manifest.json' <<<"$nginx_dump" || fail "Nginx 未接入优化卡图 manifest 缓存片段"
  grep -Fq 'max-age=31536000, immutable' <<<"$nginx_dump" || fail "Nginx 未接入内容寻址长缓存策略"
fi
prepare_storage_paths
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "运行包 SHA256 校验失败"
validate_archive "$release_archive"

short_commit="${commit:0:12}"
if [[ -n "$test_root" && -n "${L12_DEPLOY_TEST_TIMESTAMP:-}" ]]; then
  [[ "$L12_DEPLOY_TEST_TIMESTAMP" =~ ^[0-9]{8}T[0-9]{6}Z$ ]] || fail "测试发布时间戳无效"
  timestamp="$L12_DEPLOY_TEST_TIMESTAMP"
else
  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
fi
stage_candidate="${stage_parent}/legion12-staging-${short_commit}-${timestamp}"
stage_dir=""
stage_card_assets_dir=""
release_dir="${releases_dir}/${commit}-${timestamp}"
previous_target=""
previous_commit=""
legacy_dir=""
service_stopped=0
switched=0
active_entry_changed=0
new_service_launch_attempted=0
runtime_backup=""
runtime_backup_partial=""
runtime_backup_checksum=""
runtime_backup_checksum_partial=""
runtime_backup_sha256=""
runtime_restore_dir=""
failed_runtime_dir=""
failure_stage="preflight"

cleanup() {
  if [[ -n "$stage_dir" && -d "$stage_dir" ]]; then rm -rf -- "$stage_dir"; fi
  if [[ -n "$stage_card_assets_dir" && -d "$stage_card_assets_dir" ]]; then rm -rf -- "$stage_card_assets_dir"; fi
  if [[ -n "$runtime_restore_dir" && -d "$runtime_restore_dir" ]]; then rm -rf -- "$runtime_restore_dir"; fi
  if [[ -n "$runtime_backup_partial" && -f "$runtime_backup_partial" && ! -L "$runtime_backup_partial" ]]; then rm -f -- "$runtime_backup_partial"; fi
  if [[ -n "$runtime_backup_checksum_partial" && -f "$runtime_backup_checksum_partial" && ! -L "$runtime_backup_checksum_partial" ]]; then rm -f -- "$runtime_backup_checksum_partial"; fi
  rm -f -- "$release_archive"
  if [[ "$card_assets_archive" != "-" ]]; then rm -f -- "$card_assets_archive"; fi
}

backup_runtime() {
  mkdir -p "$runtime_backup_dir"
  chmod 0700 "$runtime_backup_dir"
  runtime_backup="${runtime_backup_dir}/runtime-before-${short_commit}-${timestamp}.tar.gz"
  runtime_backup_checksum="${runtime_backup}.sha256"
  local backup_partial_candidate="${runtime_backup}.partial"
  local checksum_partial_candidate="${runtime_backup_checksum}.partial"
  assert_plain_directory "$runtime_backup_dir" || return 1
  assert_new_path "$runtime_backup" || return 1
  assert_new_path "$backup_partial_candidate" || return 1
  assert_new_path "$runtime_backup_checksum" || return 1
  assert_new_path "$checksum_partial_candidate" || return 1
  create_owned_file "$backup_partial_candidate" || return 1
  runtime_backup_partial="$backup_partial_candidate"
  create_owned_file "$checksum_partial_candidate" || return 1
  runtime_backup_checksum_partial="$checksum_partial_candidate"

  if [[ "$external_artifact_mode" == "1" ]]; then
    local backup_limit="$external_backup_max_bytes"
    if [[ -n "$test_root" && -n "${L12_DEPLOY_TEST_BACKUP_MAX_BYTES:-}" ]]; then
      [[ "$L12_DEPLOY_TEST_BACKUP_MAX_BYTES" =~ ^[1-9][0-9]*$ ]] || fail "测试备份上限无效"
      backup_limit="$L12_DEPLOY_TEST_BACKUP_MAX_BYTES"
    fi
    if ! ( tar -czf - -C "$runtime_dir" . | head -c "$((backup_limit + 1))" > "$runtime_backup_partial" ); then
      fail "外置 runtime 快照创建失败或超过 4 GiB 硬上限"
      return 1
    fi
    if (( $(stat -c '%s' "$runtime_backup_partial") > backup_limit )); then
      fail "外置 runtime 快照超过 4 GiB 硬上限"
      return 1
    fi
  else
    ( tar -czf - -C "$runtime_dir" . > "$runtime_backup_partial" ) \
      || { fail "runtime 快照创建失败"; return 1; }
  fi

  chmod 0600 "$runtime_backup_partial"
  tar -tzf "$runtime_backup_partial" >/dev/null || { fail "runtime 快照压缩流完整性校验失败"; return 1; }
  runtime_backup_sha256="$(sha256sum "$runtime_backup_partial" | awk '{print $1}')" || return 1
  [[ "$runtime_backup_sha256" =~ ^[0-9a-f]{64}$ ]] || { fail "runtime 快照 SHA256 计算失败"; return 1; }
  printf '%s  %s\n' "$runtime_backup_sha256" "$(basename "$runtime_backup")" > "$runtime_backup_checksum_partial" \
    || { fail "runtime 快照 SHA256 sidecar 写入失败"; return 1; }
  chmod 0600 "$runtime_backup_checksum_partial"
  mv -Tn "$runtime_backup_partial" "$runtime_backup"
  [[ -f "$runtime_backup" && ! -L "$runtime_backup" && ! -e "$runtime_backup_partial" ]] \
    || { fail "runtime 快照最终目标发生竞争或未能原子发布"; return 1; }
  runtime_backup_partial=""
  mv -Tn "$runtime_backup_checksum_partial" "$runtime_backup_checksum"
  [[ -f "$runtime_backup_checksum" && ! -L "$runtime_backup_checksum" && ! -e "$runtime_backup_checksum_partial" ]] \
    || { fail "runtime 快照 SHA256 sidecar 发生竞争或未能原子发布"; return 1; }
  runtime_backup_checksum_partial=""
  chmod 0600 "$runtime_backup"
  log "已创建并校验持久化运行数据快照：${runtime_backup}"
}

restore_runtime_backup() {
  # 仅供具有额外、可证明写入围栏的人工恢复流程复用。常规发布一旦尝试启动
  # 新服务，错误处理器绝不会自动调用本函数覆盖可能包含新事实的 runtime。
  [[ -n "$runtime_backup" && -f "$runtime_backup" ]] || fail "缺少可用于回滚的运行数据快照"
  runtime_restore_dir="${test_root}/opt/legion12-runtime-restore-${timestamp}"
  failed_runtime_dir="${test_root}/opt/legion12-runtime-failed-${timestamp}"
  tar -tzf "$runtime_backup" >/dev/null || return 1
  mkdir -p "$runtime_restore_dir" || return 1
  tar --no-same-owner --no-same-permissions -xzf "$runtime_backup" -C "$runtime_restore_dir" || return 1
  chown -R "${service_user}:${service_user}" "$runtime_restore_dir" || return 1
  chmod 0750 "$runtime_restore_dir" || return 1
  mv "$runtime_dir" "$failed_runtime_dir" || return 1
  mv "$runtime_restore_dir" "$runtime_dir" || return 1
  runtime_restore_dir=""
}

prune_runtime_backups() {
  if [[ "$external_artifact_mode" == "1" ]]; then
    log "外置 runtime 快照不自动删除；长期总数由人工存储治理处理"
    return 0
  fi
  local rows=()
  local index old_backup
  mapfile -t rows < <(find "$runtime_backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.tar.gz' -printf '%T@:%p\n' | sort -rn)
  for ((index=5; index<${#rows[@]}; index+=1)); do
    old_backup="${rows[$index]#*:}"
    rm -f -- "$old_backup" "${old_backup}.sha256"
  done
}

restore_previous() {
  local restore_link="${test_root}/opt/.legion12-restore-${timestamp}"
  ln -s "$previous_target" "$restore_link" || return 1
  mv -Tf "$restore_link" "$active_dir" || return 1
}

stop_service_and_confirm() {
  systemctl stop "$service_name" >/dev/null 2>&1 || true
  if systemctl is-active --quiet "$service_name" >/dev/null 2>&1; then
    return 1
  fi
  return 0
}

write_failure_record() {
  local disposition="$1"
  local blocked="$2"
  local active_target="unresolved"
  local incident_file="${failure_dir}/deploy-${short_commit}-${timestamp}.txt"
  local incident_temp="${incident_file}.tmp"
  mkdir -p "$failure_dir" || return 1
  chmod 0700 "$failure_dir" || return 1
  if [[ -e "$active_dir" || -L "$active_dir" ]]; then
    active_target="$(readlink -f "$active_dir" 2>/dev/null || printf '%s' "$active_dir")"
  fi
  {
    printf 'status=failed\n'
    printf 'failedCommit=%s\n' "$commit"
    printf 'failureStage=%s\n' "$failure_stage"
    printf 'disposition=%s\n' "$disposition"
    printf 'serviceState=%s\n' "$(if [[ "$service_stopped" -eq 1 ]]; then printf stopped; else printf unknown; fi)"
    printf 'activeTarget=%s\n' "$active_target"
    printf 'previousTarget=%s\n' "$previous_target"
    printf 'previousCommit=%s\n' "$previous_commit"
    printf 'runtime=%s\n' "$runtime_dir"
    printf 'runtimeBackup=%s\n' "$runtime_backup"
    printf 'runtimeBackupSha256=%s\n' "$runtime_backup_sha256"
    printf 'failedRuntime=%s\n' "$failed_runtime_dir"
    printf 'recordedAt=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$incident_temp" || return 1
  chmod 0600 "$incident_temp" || return 1
  mv "$incident_temp" "$incident_file" || return 1
  if [[ "$blocked" -eq 1 ]]; then
    install -m 0600 "$incident_file" "${deployment_dir}/deployment-blocked.txt" || return 1
  fi
  log "失败现场说明已保存：${incident_file}"
}

rollback_on_error() {
  local status=$?
  local rollback_ok=1
  if [[ "$status" -eq 0 ]]; then status=1; fi
  trap - ERR INT TERM
  set +e

  if [[ "$new_service_launch_attempted" -eq 1 ]]; then
    # systemctl start 可能在返回失败前已经执行迁移或接受请求，无法证明 runtime
    # 仍与快照相同。宁可中断可用性，也不自动覆盖或删除潜在的新业务事实。
    if stop_service_and_confirm; then service_stopped=1; else service_stopped=0; fi
    write_failure_record "new-service-launch-attempted; runtime preserved; manual reconciliation required" 1 || true
    if [[ "$service_stopped" -eq 1 ]]; then
      log "新服务已尝试启动；拒绝自动恢复旧 runtime。服务保持停止，须人工对账后恢复。"
    else
      log "严重：新服务已尝试启动且无法确认停服；未覆盖 runtime，须立即人工隔离流量并对账。"
    fi
  elif [[ "$service_stopped" -eq 1 ]]; then
    if [[ "$active_entry_changed" -eq 1 || "$switched" -eq 1 ]]; then
      restore_previous || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      systemctl start "$service_name" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      verify_release_health "$previous_commit" "恢复版本" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      verify_release_websocket "恢复版本" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      service_stopped=0
      write_failure_record "failed before new-service launch; previous release restored and verified" 0 || true
      log "新服务启动前失败；上一版本已恢复并通过本机、公网提交身份及 WebSocket 核验。"
    else
      if stop_service_and_confirm; then service_stopped=1; else service_stopped=0; fi
      write_failure_record "pre-launch recovery could not be verified; manual recovery required" 1 || true
      if [[ "$service_stopped" -eq 1 ]]; then
        log "启动新服务前的恢复未能完整验证；服务保持停止且 runtime/备份/版本均保留。"
      else
        log "严重：恢复版本无法验证且无法确认停服；runtime/备份/版本均保留，须立即人工隔离流量。"
      fi
    fi
  fi
  cleanup
  exit "$status"
}
trap rollback_on_error ERR INT TERM

assert_new_path "$stage_candidate"
assert_new_path "$release_dir"
release_unpacked_bytes=0
if [[ "$external_artifact_mode" == "1" ]]; then
  release_unpacked_bytes="$(archive_unpacked_bytes "$release_archive")"
fi
card_unpacked_bytes=0
card_assets_target=""
card_assets_requires_install=0
if [[ "$card_assets_hash" != "-" ]]; then
  preferred_card_assets_target="${static_card_assets_dir}/${card_assets_hash}"
  if [[ "$external_artifact_mode" == "1" && ( -e "$preferred_card_assets_target" || -L "$preferred_card_assets_target" ) ]]; then
    card_assets_target="$preferred_card_assets_target"
  elif [[ "$external_artifact_mode" == "1" && ( -e "${default_static_card_assets_dir}/${card_assets_hash}" || -L "${default_static_card_assets_dir}/${card_assets_hash}" ) ]]; then
    card_assets_target="${default_static_card_assets_dir}/${card_assets_hash}"
  elif [[ "$external_artifact_mode" == "0" && ( -e "$preferred_card_assets_target" || -L "$preferred_card_assets_target" ) ]]; then
    card_assets_target="$preferred_card_assets_target"
  else
    card_assets_target="$preferred_card_assets_target"
    card_assets_requires_install=1
  fi

  if [[ "$card_assets_requires_install" -eq 1 ]]; then
    [[ "$card_assets_archive" != "-" ]] || fail "服务器没有该优化卡图缓存，且未提供优化卡图包"
    [[ "$(sha256sum "$card_assets_archive" | awk '{print $1}')" == "$card_assets_sha256" ]] || fail "优化卡图包 SHA256 校验失败"
    validate_archive "$card_assets_archive"
    if [[ "$external_artifact_mode" == "1" ]]; then
      card_unpacked_bytes="$(archive_unpacked_bytes "$card_assets_archive")"
    fi
    assert_new_path "$card_assets_target"
  else
    validate_card_assets_tree "$card_assets_target" "$card_assets_hash"
  fi
fi
validate_external_deploy_capacity "$release_unpacked_bytes" "$card_unpacked_bytes"

log "展开预构建运行包 ${commit}"
create_owned_directory "$stage_candidate"
stage_dir="$stage_candidate"
tar --no-same-owner --no-same-permissions -xzf "$release_archive" -C "$stage_dir"
test -f "${stage_dir}/.deployment-commit" || fail "运行包缺少提交标记"
[[ "$(tr -d '\r\n' < "${stage_dir}/.deployment-commit")" == "$commit" ]] || fail "运行包提交标记不匹配"
test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "运行包缺少后端入口"
test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "运行包缺少前端首页"
test -r "${stage_dir}/scripts/ws-smoke.mjs" || fail "运行包缺少 WebSocket 冒烟脚本"
test ! -e "${stage_dir}/opcgpro-vue/dist/cards" || fail "运行包不应重复携带卡图缓存"

[[ ! -e "${stage_dir}/opcgpro-vue/dist/card-assets" && ! -L "${stage_dir}/opcgpro-vue/dist/card-assets" ]] || fail "运行包不应重复携带优化卡图缓存"
if [[ "$card_assets_hash" != "-" ]]; then
  if [[ "$card_assets_requires_install" -eq 1 ]]; then
    stage_card_assets_candidate="${stage_parent}/legion12-card-assets-staging-${card_assets_hash}-${timestamp}"
    create_owned_directory "$stage_card_assets_candidate"
    stage_card_assets_dir="$stage_card_assets_candidate"
    tar --no-same-owner --no-same-permissions -xzf "$card_assets_archive" -C "$stage_card_assets_dir"
    test -f "${stage_card_assets_dir}/card-assets.manifest.json" || fail "优化卡图包缺少 manifest"
    test -f "${stage_card_assets_dir}/card-assets.preload.json" || fail "优化卡图包缺少 preload 清单"
    test -d "${stage_card_assets_dir}/cards" || fail "优化卡图包缺少 cards 目录"
    validate_card_assets_tree "$stage_card_assets_dir" "$card_assets_hash"
    chmod 0755 "$stage_card_assets_dir"
    find "$stage_card_assets_dir" -type d -exec chmod 0755 {} +
    find "$stage_card_assets_dir" -type f -exec chmod 0644 {} +
    if [[ "$mode" == "deploy" ]]; then
      mv -Tn "$stage_card_assets_dir" "$card_assets_target"
      [[ -d "$card_assets_target" && ! -L "$card_assets_target" && ! -e "$stage_card_assets_dir" ]] \
        || fail "优化卡图最终目录发生竞争或未能原子发布"
      stage_card_assets_dir=""
    else
      card_assets_target="$stage_card_assets_dir"
    fi
  fi
  runuser -u "$web_user" -- test -r "${card_assets_target}/card-assets.manifest.json" || fail "Nginx 账号无法读取优化卡图 manifest"
  sample_card_asset="$(find "${card_assets_target}/cards" -type f -print -quit)"
  test -n "$sample_card_asset" || fail "优化卡图缓存为空"
  runuser -u "$web_user" -- test -r "$sample_card_asset" || fail "Nginx 账号无法读取优化卡图缓存"
fi

if [[ -n "$card_assets_target" ]]; then
  ln -s "$card_assets_target" "${stage_dir}/opcgpro-vue/dist/card-assets"
fi
chmod 0755 "$stage_dir" "${stage_dir}/opcgpro-vue" "${stage_dir}/opcgpro-vue/dist" "${stage_dir}/publish"
find "${stage_dir}/opcgpro-vue/dist" -type d -exec chmod 0755 {} +
find "${stage_dir}/opcgpro-vue/dist" -type f -exec chmod 0644 {} +
find "${stage_dir}/publish" -type d -exec chmod 0755 {} +
find "${stage_dir}/publish" -type f -exec chmod 0644 {} +
runuser -u "$service_user" -- test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "服务账号无法读取后端入口"
runuser -u "$web_user" -- test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "Nginx 账号无法读取前端首页"

if [[ "$mode" == "dry-run" ]]; then
  log "快速干运行通过：产物、哈希、目录结构及真实账号权限均正常"
  cleanup
  trap - ERR INT TERM
  exit 0
fi

if [[ -L "$active_dir" ]]; then
  previous_target="$(readlink -f "$active_dir")"
else
  previous_target="$active_dir"
fi
previous_commit="$(read_release_commit "$previous_target")"

# This deployment fence is independent of the saved operations maintenance plan.
# Keep it on failure; only a completely verified release may remove it.
block_sandbox_creation() {
  [[ ! -L "$sandbox_fence" && ( ! -e "$sandbox_fence" || -f "$sandbox_fence" ) ]] \
    || fail "沙盒发布围栏不是普通文件，拒绝覆盖"
  printf '%s\n' "$commit" > "$sandbox_fence"
  chmod 0644 "$sandbox_fence"
}
if [[ -d "$runtime_dir" ]]; then block_sandbox_creation; fi

failure_stage="stop-current-service"
log "暂停服务并首次分离持久化运行数据"
service_stopped=1
systemctl stop "$service_name"
if systemctl is-active --quiet "$service_name" >/dev/null 2>&1; then
  fail "服务停止命令返回后仍处于 active，拒绝继续备份或切换"
fi
if [[ ! -d "$runtime_dir" ]]; then
  failure_stage="separate-runtime"
  test -d "${active_dir}/publish/runtime" || fail "当前版本缺少运行数据目录"
  mv "${active_dir}/publish/runtime" "$runtime_dir"
  ln -s "$runtime_dir" "${active_dir}/publish/runtime"
fi
failure_stage="snapshot-runtime"
block_sandbox_creation
chown -R "${service_user}:${service_user}" "$runtime_dir"
chmod 0750 "$runtime_dir"
backup_runtime
ln -s "$runtime_dir" "${stage_dir}/publish/runtime"

failure_stage="configure-runtime-boundary"
mkdir -p "$service_override_dir"
cat > "${service_override_dir}/runtime.conf" <<EOF
[Service]
ReadWritePaths=
ReadWritePaths=${runtime_dir}
EOF
systemctl daemon-reload

failure_stage="install-release"
mv -Tn "$stage_dir" "$release_dir"
[[ -d "$release_dir" && ! -L "$release_dir" && ! -e "$stage_dir" ]] \
  || fail "release 最终目录发生竞争或未能原子发布"
stage_dir=""
if [[ -L "$active_dir" ]]; then
  : # previous_target and previous_commit were captured before stopping the service.
else
  legacy_dir="${test_root}/opt/legion12-legacy-${timestamp}"
  mv "$active_dir" "$legacy_dir"
  previous_target="$legacy_dir"
  active_entry_changed=1
fi

next_link="${test_root}/opt/.legion12-test-next-${timestamp}"
ln -s "$release_dir" "$next_link"
mv -Tf "$next_link" "$active_dir"
switched=1
active_entry_changed=1
failure_stage="start-new-service"
new_service_launch_attempted=1
systemctl start "$service_name"
service_stopped=0

failure_stage="verify-new-release-health"
log "验证本机与公网 HTTP 提交身份"
verify_release_health "$commit" "新版本"
curl -fsS "${public_base}/" >/dev/null
curl -fsS "${public_base}/cards" >/dev/null
failure_stage="verify-new-release-websocket"
log "验证本机与公网 WebSocket 建连及无状态部署协议"
verify_release_websocket "新版本"

if [[ "$card_assets_hash" != "-" ]]; then
  public_asset_version="$(curl -fsS "${public_base}/card-assets/card-assets.manifest.json" | node -e "let body='';process.stdin.on('data',chunk=>body+=chunk);process.stdin.on('end',()=>{const manifest=JSON.parse(body);if(manifest.schemaVersion!==3||manifest.cardCount!==366||manifest.playableCardCount!==324||manifest.presentationCardCount!==42||!manifest.cards?.['ST01-01']||!manifest.cards?.['S01-0101b']||!manifest.cards?.['S01-01M1A']||!manifest.cards?.['S02-01M1A']||!manifest.cards?.['S02-06C1A']||!manifest.cards?.['ST01-C1st']||!manifest.cards?.['S02-05C1B'])process.exit(2);process.stdout.write(manifest.assetVersion||'')})")"
  [[ "$public_asset_version" == "$card_assets_hash" ]] || fail "公网优化卡图 manifest 版本不匹配"
  sample_asset_path="$(node - "${card_assets_target}/card-assets.manifest.json" <<'NODE'
const manifest = require(process.argv[2])
const starter = manifest.cards['ST01-01']
if (!starter?.variants?.thumbWebp) process.exit(2)
process.stdout.write(starter.variants.thumbWebp)
NODE
)"
  manifest_headers="$(curl -fsSI "${public_base}/card-assets/card-assets.manifest.json")"
  grep -Eiq '^cache-control:.*max-age=300.*must-revalidate' <<<"$manifest_headers" || fail "公网优化卡图 manifest 缓存头错误"
  grep -Eiq '^content-type:.*application/json' <<<"$manifest_headers" || fail "公网优化卡图 manifest 类型错误"
  curl -fsS "${public_base}/card-assets/${sample_asset_path}" -o /dev/null
  asset_headers="$(curl -fsSI "${public_base}/card-assets/${sample_asset_path}")"
  grep -Eiq '^cache-control:.*max-age=31536000.*immutable' <<<"$asset_headers" || fail "公网内容寻址卡图缓存头错误"
  grep -Eiq '^content-type:.*image/webp' <<<"$asset_headers" || fail "公网 ST 卡图响应类型错误"
fi

cat > "${deployment_dir}/deployment-info.txt" <<EOF
Legion12 正式服
源仓库：https://github.com/Testrunner-DC/Legion12
源提交：${commit}
活动版本：${release_dir}
上一版本：${previous_target}
服务器制品根：${artifact_root_option}
共享运行数据：${runtime_dir}
部署前运行数据快照：${runtime_backup}
部署前运行数据快照SHA256：${runtime_backup_sha256}
旧版 /cards 卡图：已退役
内容寻址优化卡图版本：${card_assets_hash}
域名：${public_host}
部署日期：$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

failure_stage="prune-runtime-backups"
prune_runtime_backups

# Do not call any maintenance-end/server-start operation or mutate its plan.
rm -f -- "$sandbox_fence"
rm -f -- "$release_archive"
if [[ "$card_assets_archive" != "-" ]]; then rm -f -- "$card_assets_archive"; fi
trap - ERR INT TERM
log "快速部署完成：${commit}"
