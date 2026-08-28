#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly active_dir="/opt/legion12-test"
readonly releases_dir="/opt/legion12-releases"
readonly runtime_dir="/opt/legion12-runtime"
readonly static_cards_dir="/opt/legion12-static/cards"
readonly static_card_assets_dir="/opt/legion12-static/card-assets"
readonly deployment_dir="/opt/legion12-deployment"
readonly incoming_dir="${deployment_dir}/incoming"
readonly runtime_backup_dir="${deployment_dir}/runtime-backups"
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
  for command_name in flock sha256sum tar curl systemctl nginx runuser node find readlink ln mv install awk grep tr chmod chown sort; do
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

validate_card_assets_tree() {
  local root="$1"
  local expected_hash="$2"
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

if (manifest.schemaVersion !== 2 || manifest.complete !== true || manifest.cardCount !== 248) fail('manifest 必须是完整 schema v2 且包含 248 张卡')
if (manifest.assetVersion !== expectedHash || !/^[0-9a-f]{64}$/.test(expectedHash)) fail('manifest 资产版本不匹配')
if (!/^[A-Za-z0-9._-]+$/.test(manifest.catalogVersion)) fail('目录版本包含不安全字符')
if (manifest.basePath !== '/card-assets' || manifest.missing?.length !== 0) fail('manifest 基础路径或缺失列表无效')
const cards = manifest.cards && typeof manifest.cards === 'object' ? manifest.cards : {}
const entries = Object.entries(cards)
if (entries.length !== 248 || new Set(entries.map(([id]) => id)).size !== 248) fail('manifest 卡号不是 248 个唯一值')

let totalBytes = 0
const versionRows = []
for (const [cardId, card] of entries) {
  if (!/^S\d{2}-[A-Z0-9]+$/.test(cardId) || card.cardId !== cardId) fail(`非法卡号：${cardId}`)
  if (!/^[0-9a-f]{64}$/.test(card.contentHash)) fail(`内容哈希无效：${cardId}`)
  const prefix = `cards/${manifest.catalogVersion}/${cardId}/${card.contentHash.slice(0, 20)}/`
  for (const [variant, fileName] of Object.entries(requiredVariants)) {
    const relative = card.variants?.[variant]
    if (relative !== `${prefix}${fileName}` || relative.startsWith('/') || relative.includes('\\') || posix.normalize(relative) !== relative) fail(`变体路径无效：${cardId}:${variant}`)
    const file = lstatSync(safeFile(relative))
    if (!Number.isSafeInteger(card.bytes?.[variant]) || card.bytes[variant] !== file.size) fail(`变体大小不匹配：${cardId}:${variant}`)
    totalBytes += file.size
  }
  versionRows.push(`${cardId}:${card.contentHash}`)
}
const actualVersion = createHash('sha256').update(versionRows.sort().join('\n')).digest('hex')
if (actualVersion !== expectedHash) fail('248 张卡的内容哈希聚合版本不匹配')
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

mode="${1:-}"
commit="${2:-}"
release_sha256="${3:-}"
release_archive="${4:-}"
cards_hash="${5:-}"
cards_sha256="${6:--}"
cards_archive="${7:--}"
card_assets_hash="${8:--}"
card_assets_sha256="${9:--}"
card_assets_archive="${10:--}"
[[ "$mode" == "deploy" || "$mode" == "dry-run" ]] || fail "用法：$0 <deploy|dry-run> <提交> <运行包SHA256> <运行包> <卡图版本> <卡图SHA256|-> <卡图包|-> <优化卡图版本|-> <优化卡图SHA256|-> <优化卡图包|->"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "提交哈希格式错误"
[[ "$release_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "运行包 SHA256 格式错误"
[[ "$cards_hash" =~ ^[0-9a-f]{40,64}$ ]] || fail "卡图版本格式错误"
[[ "$release_archive" == "${incoming_dir}/l12-release-${commit}.tar.gz" ]] || fail "运行包不在允许目录"
if [[ "$cards_archive" != "-" ]]; then
  [[ "$cards_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "卡图包 SHA256 格式错误"
  [[ "$cards_archive" == "${incoming_dir}/l12-cards-${cards_hash}.tar.gz" ]] || fail "卡图包不在允许目录"
fi

if [[ "$card_assets_hash" == "-" ]]; then
  [[ "$card_assets_sha256" == "-" && "$card_assets_archive" == "-" ]] || fail "优化卡图参数必须全部为 - 或全部提供"
else
  [[ "$card_assets_hash" =~ ^[0-9a-f]{64}$ ]] || fail "优化卡图版本格式错误"
  if [[ "$card_assets_archive" != "-" ]]; then
    [[ "$card_assets_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "优化卡图包 SHA256 格式错误"
    [[ "$card_assets_archive" == "${incoming_dir}/l12-card-assets-${card_assets_hash}.tar.gz" ]] || fail "优化卡图包不在允许目录"
  else
    [[ "$card_assets_sha256" == "-" ]] || fail "复用优化卡图缓存时 SHA256 必须为 -"
  fi
fi

if [[ "${L12_DEPLOY_LOCKED:-0}" != "1" ]]; then
  export L12_DEPLOY_LOCKED=1
  exec flock --close --nonblock "$lock_file" "$0" "$@"
fi

self_test
if [[ "$card_assets_hash" != "-" ]]; then
  nginx_dump="$(nginx -T 2>&1)"
  grep -Fq 'location = /card-assets/card-assets.manifest.json' <<<"$nginx_dump" || fail "Nginx 未接入优化卡图 manifest 缓存片段"
  grep -Fq 'max-age=31536000, immutable' <<<"$nginx_dump" || fail "Nginx 未接入内容寻址长缓存策略"
fi
mkdir -p "$incoming_dir" "$releases_dir" "$static_cards_dir" "$static_card_assets_dir"
chmod 0755 "$(dirname "$static_cards_dir")" "$static_cards_dir" "$static_card_assets_dir" "$releases_dir"
test -f "$release_archive" || fail "找不到运行包"
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "运行包 SHA256 校验失败"
validate_archive "$release_archive"

short_commit="${commit:0:12}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
stage_dir="/opt/legion12-staging-${short_commit}-${timestamp}"
stage_cards_dir=""
stage_card_assets_dir=""
release_dir="${releases_dir}/${commit}-${timestamp}"
previous_target=""
legacy_dir=""
service_stopped=0
switched=0
runtime_backup=""
runtime_restore_dir=""
failed_runtime_dir=""

cleanup() {
  if [[ -n "$stage_dir" && -d "$stage_dir" ]]; then rm -rf -- "$stage_dir"; fi
  if [[ -n "$stage_cards_dir" && -d "$stage_cards_dir" ]]; then rm -rf -- "$stage_cards_dir"; fi
  if [[ -n "$stage_card_assets_dir" && -d "$stage_card_assets_dir" ]]; then rm -rf -- "$stage_card_assets_dir"; fi
  if [[ -n "$runtime_restore_dir" && -d "$runtime_restore_dir" ]]; then rm -rf -- "$runtime_restore_dir"; fi
  rm -f -- "$release_archive"
  if [[ "$cards_archive" != "-" ]]; then rm -f -- "$cards_archive"; fi
  if [[ "$card_assets_archive" != "-" ]]; then rm -f -- "$card_assets_archive"; fi
}

backup_runtime() {
  mkdir -p "$runtime_backup_dir"
  chmod 0700 "$runtime_backup_dir"
  runtime_backup="${runtime_backup_dir}/runtime-before-${short_commit}-${timestamp}.tar.gz"
  tar -czf "$runtime_backup" -C "$runtime_dir" .
  chmod 0600 "$runtime_backup"
  log "已创建持久化运行数据快照：${runtime_backup}"
}

restore_runtime_backup() {
  [[ -n "$runtime_backup" && -f "$runtime_backup" ]] || fail "缺少可用于回滚的运行数据快照"
  runtime_restore_dir="/opt/legion12-runtime-restore-${timestamp}"
  failed_runtime_dir="/opt/legion12-runtime-failed-${timestamp}"
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
  local rows=()
  local index
  mapfile -t rows < <(find "$runtime_backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.tar.gz' -printf '%T@:%p\n' | sort -rn)
  for ((index=5; index<${#rows[@]}; index+=1)); do
    rm -f -- "${rows[$index]#*:}"
  done
}

restore_previous() {
  local restore_link="/opt/.legion12-restore-${timestamp}"
  ln -s "$previous_target" "$restore_link" || return 1
  mv -Tf "$restore_link" "$active_dir" || return 1
}

rollback_on_error() {
  status=$?
  trap - ERR INT TERM
  if [[ "$switched" -eq 1 && -n "$previous_target" && -e "$previous_target" ]]; then
    log "新版本验证失败，正在恢复上一版本及对应运行数据"
    systemctl stop "$service_name" || true
    if restore_runtime_backup && restore_previous && systemctl start "$service_name"; then
      rm -rf -- "$failed_runtime_dir"
      failed_runtime_dir=""
      log "上一版本及运行数据已恢复"
    else
      log "自动回滚未完整成功；为保护数据，服务保持停止并保留现场目录"
    fi
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

test ! -e "${stage_dir}/opcgpro-vue/dist/card-assets" || fail "运行包不应重复携带优化卡图缓存"
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

card_assets_target=""
if [[ "$card_assets_hash" != "-" ]]; then
  card_assets_target="${static_card_assets_dir}/${card_assets_hash}"
  if [[ ! -d "$card_assets_target" ]]; then
    [[ "$card_assets_archive" != "-" ]] || fail "服务器没有该优化卡图缓存，且未提供优化卡图包"
    test -f "$card_assets_archive" || fail "找不到优化卡图包"
    [[ "$(sha256sum "$card_assets_archive" | awk '{print $1}')" == "$card_assets_sha256" ]] || fail "优化卡图包 SHA256 校验失败"
    validate_archive "$card_assets_archive"
    stage_card_assets_dir="/opt/legion12-card-assets-staging-${card_assets_hash}-${timestamp}"
    mkdir -p "$stage_card_assets_dir"
    tar --no-same-owner --no-same-permissions -xzf "$card_assets_archive" -C "$stage_card_assets_dir"
    test -f "${stage_card_assets_dir}/card-assets.manifest.json" || fail "优化卡图包缺少 manifest"
    test -f "${stage_card_assets_dir}/card-assets.preload.json" || fail "优化卡图包缺少 preload 清单"
    test -d "${stage_card_assets_dir}/cards" || fail "优化卡图包缺少 cards 目录"
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
  else
    validate_card_assets_tree "$card_assets_target" "$card_assets_hash"
  fi
  runuser -u "$web_user" -- test -r "${card_assets_target}/card-assets.manifest.json" || fail "Nginx 账号无法读取优化卡图 manifest"
  sample_card_asset="$(find "${card_assets_target}/cards" -type f -print -quit)"
  test -n "$sample_card_asset" || fail "优化卡图缓存为空"
  runuser -u "$web_user" -- test -r "$sample_card_asset" || fail "Nginx 账号无法读取优化卡图缓存"
fi

ln -s "$cards_target" "${stage_dir}/opcgpro-vue/dist/cards"
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
backup_runtime
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

if [[ "$card_assets_hash" != "-" ]]; then
  public_asset_version="$(curl -fsS "${public_base}/card-assets/card-assets.manifest.json" | node -e "let body='';process.stdin.on('data',chunk=>body+=chunk);process.stdin.on('end',()=>process.stdout.write(JSON.parse(body).assetVersion || ''))")"
  [[ "$public_asset_version" == "$card_assets_hash" ]] || fail "公网优化卡图 manifest 版本不匹配"
  sample_asset_path="$(node - "${card_assets_target}/card-assets.manifest.json" <<'NODE'
const manifest = require(process.argv[2])
const first = Object.values(manifest.cards)[0]
process.stdout.write(first.variants.thumbWebp)
NODE
)"
  manifest_headers="$(curl -fsSI "${public_base}/card-assets/card-assets.manifest.json")"
  grep -Eiq '^cache-control:.*max-age=300.*must-revalidate' <<<"$manifest_headers" || fail "公网优化卡图 manifest 缓存头错误"
  asset_headers="$(curl -fsSI "${public_base}/card-assets/${sample_asset_path}")"
  grep -Eiq '^cache-control:.*max-age=31536000.*immutable' <<<"$asset_headers" || fail "公网内容寻址卡图缓存头错误"
fi

cat > "${deployment_dir}/deployment-info.txt" <<EOF
Legion12 香港测试服
源仓库：https://github.com/Testrunner-DC/Legion12
源提交：${commit}
活动版本：${release_dir}
上一版本：${previous_target}
共享运行数据：${runtime_dir}
部署前运行数据快照：${runtime_backup}
共享卡图版本：${cards_hash}
内容寻址优化卡图版本：${card_assets_hash}
域名：${public_host}
部署日期：$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

prune_runtime_backups

rm -f -- "$release_archive"
if [[ "$cards_archive" != "-" ]]; then rm -f -- "$cards_archive"; fi
if [[ "$card_assets_archive" != "-" ]]; then rm -f -- "$card_assets_archive"; fi
trap - ERR INT TERM
log "快速部署完成：${commit}"
