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
readonly runtime_verifier="${test_root}/usr/local/libexec/verify-legion12-runtime-backup.py"
readonly drain_consumer="${test_root}/usr/local/libexec/l12-deployment-drain-consumer.py"
readonly service_name="legion12-test.service"
readonly public_host="legion-12.com"
readonly lock_file="${test_root}/run/lock/legion12-deploy.lock"
readonly service_override_dir="${test_root}/etc/systemd/system/${service_name}.d"
readonly environment_file="${test_root}/etc/legion12-test.env"
readonly default_releases_dir="${test_root}/opt/legion12-releases"
readonly default_incoming_dir="${deployment_dir}/incoming"
readonly default_runtime_backup_dir="${deployment_dir}/runtime-backups"
readonly default_static_card_assets_dir="${test_root}/opt/legion12-static/card-assets"
readonly default_static_web_assets_dir="${test_root}/opt/legion12-static/web-assets"
readonly web_assets_entry="${test_root}/opt/legion12-web-assets"
readonly external_mount="${test_root}/www"
readonly external_artifact_root="${external_mount}/legion12"
if [[ "$external_artifact_mode" == "1" ]]; then
  readonly artifact_root="$external_artifact_root"
  readonly releases_dir="${artifact_root}/releases"
  readonly incoming_dir="${artifact_root}/incoming"
  readonly runtime_backup_dir="${artifact_root}/runtime-backups"
  readonly static_card_assets_dir="${artifact_root}/card-assets"
  readonly static_web_assets_dir="${artifact_root}/web-assets"
  readonly stage_parent="${artifact_root}/staging"
else
  readonly artifact_root="${test_root}/opt"
  readonly releases_dir="$default_releases_dir"
  readonly incoming_dir="$default_incoming_dir"
  readonly runtime_backup_dir="$default_runtime_backup_dir"
  readonly static_card_assets_dir="$default_static_card_assets_dir"
  readonly static_web_assets_dir="$default_static_web_assets_dir"
  readonly stage_parent="${test_root}/opt"
fi
readonly system_min_available_bytes=$((10 * 1024 * 1024 * 1024))
readonly external_policy_floor_bytes=$((4 * 1024 * 1024 * 1024))
readonly external_prepare_min_bytes=$((14 * 1024 * 1024 * 1024))
readonly external_backup_max_bytes=$((4 * 1024 * 1024 * 1024))
readonly external_reserve_bytes=$((8 * 1024 * 1024 * 1024))
readonly external_release_unpacked_max_bytes=$((1 * 1024 * 1024 * 1024))
readonly external_card_unpacked_max_bytes=$((512 * 1024 * 1024))
readonly prune_runtime_backups_enabled="${L12_PRUNE_RUNTIME_BACKUPS:-0}"
readonly runtime_backup_keep_groups="${L12_RUNTIME_BACKUP_KEEP_GROUPS:-2}"
[[ "$prune_runtime_backups_enabled" =~ ^[01]$ ]] \
  || { fail "runtime 快照自动删除开关只允许 0 或 1"; exit 2; }
[[ "$runtime_backup_keep_groups" =~ ^[1-9][0-9]*$ ]] \
  || { fail "runtime 快照保留组数必须为正整数"; exit 2; }
readonly web_asset_retention_minutes="${L12_WEB_ASSET_RETENTION_MINUTES:-2880}"
# Includes the unchanged 310-second runtime proof, release switch, exact health
# checks, and a bounded old-process restart/cancel attempt before the outer cutoff.
readonly deployment_recovery_reserve_seconds=900
[[ "$web_asset_retention_minutes" =~ ^[0-9]+$ ]] \
  && (( web_asset_retention_minutes >= 1440 && web_asset_retention_minutes <= 2880 )) \
  || { fail "前端哈希资源保留窗口只允许 1440—2880 分钟"; exit 2; }
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

system_available_bytes() {
  local available
  if [[ -n "$test_root" ]]; then
    available="${L12_DEPLOY_TEST_SYSTEM_AVAILABLE_BYTES:-$((20 * 1024 * 1024 * 1024))}"
  else
    require_command df || return 1
    available="$(df -B1 --output=avail / | awk 'NR == 2 { gsub(/[[:space:]]/, "", $1); print $1 }')" \
      || { fail "无法读取系统盘可用容量"; return 1; }
  fi
  [[ "$available" =~ ^[0-9]+$ ]] || { fail "无法读取系统盘可用容量"; return 1; }
  printf '%s\n' "$available"
}

validate_system_capacity() {
  local available
  available="$(system_available_bytes)" || return 1
  log "固定路径容量报告：release=${releases_dir}; runtime=${runtime_dir}; backup=${runtime_backup_dir}; systemAvailableBytes=${available}; systemMinimumBytes=${system_min_available_bytes}"
  if (( available < system_min_available_bytes )); then
    fail "系统盘可用容量不足 10 GiB；停止发布，不清理 runtime、快照或事故证据"
    return 1
  fi
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
  log "数据盘容量报告：root=${artifact_root}; availableBytes=${available}; policyFloorBytes=${external_policy_floor_bytes}; uploadPrepareBytes=${external_prepare_min_bytes}; postPlanReserveBytes=${external_reserve_bytes}" >&2
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
  validate_system_capacity || return 1
  if [[ "$external_artifact_mode" == "1" ]]; then
    if ! validate_external_mount; then return 1; fi
    # 14 GiB and the later 8 GiB residual plan remain stronger than the named 4 GiB policy floor.
    if ! validate_external_prepare_capacity; then return 1; fi
    if ! assert_existing_directories_plain \
      "$artifact_root" "$incoming_dir" "$runtime_backup_dir" "$static_card_assets_dir" "$static_web_assets_dir" "$stage_parent" "$releases_dir"; then return 1; fi
    if ! mkdir -p "$artifact_root" "$incoming_dir" "$runtime_backup_dir" "$static_card_assets_dir" "$static_web_assets_dir/assets" "$stage_parent" "$releases_dir"; then return 1; fi
    if ! chmod 0755 "$artifact_root" "$static_card_assets_dir" "$static_web_assets_dir" "$static_web_assets_dir/assets" "$stage_parent" "$releases_dir"; then return 1; fi
    if ! chmod 0700 "$incoming_dir" "$runtime_backup_dir"; then return 1; fi
    if ! assert_plain_directory "$artifact_root"; then return 1; fi
    if ! assert_plain_directory "$incoming_dir"; then return 1; fi
    if ! assert_plain_directory "$runtime_backup_dir"; then return 1; fi
    if ! assert_plain_directory "$static_card_assets_dir"; then return 1; fi
    if ! assert_plain_directory "$static_web_assets_dir"; then return 1; fi
    if ! assert_plain_directory "$stage_parent"; then return 1; fi
    if ! assert_plain_directory "$releases_dir"; then return 1; fi
    if [[ -z "$test_root" ]]; then
      if [[ "$(stat -c '%a' "$artifact_root")" != "755" || "$(stat -c '%a' "$stage_parent")" != "755" ||
            "$(stat -c '%a' "$releases_dir")" != "755" || "$(stat -c '%a' "$static_card_assets_dir")" != "755" || "$(stat -c '%a' "$static_web_assets_dir")" != "755" ||
            "$(stat -c '%a' "$incoming_dir")" != "700" || "$(stat -c '%a' "$runtime_backup_dir")" != "700" ]]; then
        fail "外置制品目录权限不符合发布边界"
        return 1
      fi
    fi
    if ! runuser -u "$service_user" -- test -x "$artifact_root" -a -x "$stage_parent" -a -x "$releases_dir"; then
      fail "服务账号无法穿越外置 release/staging 路径"
      return 1
    fi
    if ! runuser -u "$web_user" -- test -x "$artifact_root" -a -x "$stage_parent" -a -x "$static_card_assets_dir" -a -x "$static_web_assets_dir"; then
      fail "Nginx 账号无法穿越外置 release/card-assets/web-assets 路径"
      return 1
    fi
  else
    if ! mkdir -p "$incoming_dir" "$releases_dir" "$static_card_assets_dir" "$static_web_assets_dir/assets"; then return 1; fi
    if ! chmod 0755 "$(dirname "$static_card_assets_dir")" "$static_card_assets_dir" "$static_web_assets_dir" "$static_web_assets_dir/assets" "$releases_dir"; then return 1; fi
  fi
  if ! mkdir -p "$deployment_dir"; then return 1; fi
  return 0
}

validate_web_assets_tree() {
  local source_root="$1"
  local invalid_path sample
  [[ -d "$source_root" && ! -L "$source_root" ]] || { fail "前端哈希资源根不是普通目录：${source_root}"; return 1; }
  invalid_path="$(find "$source_root" -mindepth 1 ! -type d ! -type f -print -quit)"
  [[ -z "$invalid_path" ]] || { fail "前端哈希资源包含链接或特殊文件：${invalid_path}"; return 1; }
  sample="$(find "$source_root" -type f -print -quit)"
  [[ -n "$sample" ]] || { fail "前端哈希资源目录为空：${source_root}"; return 1; }
}

validate_relative_web_asset_path() {
  local relative="$1"
  [[ -n "$relative" && "$relative" != /* && "$relative" != *\\* && "$relative" != *"//"* ]] || return 1
  [[ "$relative" != "." && "$relative" != ".." && "/${relative}/" != *"/./"* && "/${relative}/" != *"/../"* ]] || return 1
  if printf '%s' "$relative" | LC_ALL=C grep -q '[[:cntrl:]]'; then return 1; fi
  return 0
}

install_web_assets_tree() {
  local source_root="$1"
  local public_prefix="$2"
  local source relative target target_parent temporary prefix_path component
  local -a prefix_components
  validate_web_assets_tree "$source_root" || return 1
  log "安装前端兼容资源：${source_root} -> ${public_prefix}"
  [[ "$public_prefix" =~ ^[A-Za-z0-9._/-]+$ && "$public_prefix" != /* && "/${public_prefix}/" != *"/../"* ]] \
    || { fail "前端哈希资源公开前缀无效"; return 1; }
  prefix_path="$static_web_assets_dir"
  [[ -d "$prefix_path" ]] || mkdir -p "$prefix_path"
  chmod 0755 "$prefix_path" || { fail "无法规范共享前端哈希资源根目录权限"; return 1; }
  IFS='/' read -r -a prefix_components <<< "$public_prefix"
  for component in "${prefix_components[@]}"; do
    prefix_path="${prefix_path}/${component}"
    [[ -d "$prefix_path" ]] || mkdir "$prefix_path"
    chmod 0755 "$prefix_path" || { fail "无法规范共享前端哈希资源公开前缀权限：${public_prefix}"; return 1; }
  done
  if [[ -n "$(find "$static_web_assets_dir" -type l -print -quit)" ]]; then
    fail "共享前端哈希资源目录包含符号链接"
    return 1
  fi
  while IFS= read -r -d '' source; do
    relative="${source#${source_root}/}"
    validate_relative_web_asset_path "$relative" \
      || { fail "前端哈希资源路径无效：${relative}"; return 1; }
    target="${static_web_assets_dir}/${public_prefix}/${relative}"
    target_parent="${target%/*}"
    if [[ ! -d "$target_parent" ]]; then log "创建前端兼容资源目录：${target_parent}"; mkdir -p "$target_parent"; fi
    if [[ -e "$target" || -L "$target" ]]; then
      [[ -f "$target" && ! -L "$target" ]] || { fail "共享前端哈希资源目标不是普通文件：${target}"; return 1; }
      cmp -s "$source" "$target" || { fail "相同哈希资源路径出现不同内容：${public_prefix}/${relative}"; return 1; }
      continue
    fi
    temporary="${target}.install-${short_commit}-$$"
    [[ ! -e "$temporary" && ! -L "$temporary" ]] || { fail "前端哈希资源临时目标已存在：${temporary}"; return 1; }
    install -m 0644 "$source" "$temporary"
    mv -Tn "$temporary" "$target"
    if [[ -e "$temporary" ]]; then
      [[ -f "$target" && ! -L "$target" ]] && cmp -s "$source" "$target" \
        || { fail "前端哈希资源原子安装发生内容冲突：${public_prefix}/${relative}"; return 1; }
      rm -f -- "$temporary"
    fi
  done < <(find "$source_root" -type f -print0)
  find "${static_web_assets_dir}/${public_prefix}" -type d -exec chmod 0755 {} + \
    || { fail "无法规范共享前端哈希资源目录权限：${public_prefix}"; return 1; }
}

ensure_web_assets_entry() {
  local current_target next_link
  if [[ -L "$web_assets_entry" ]]; then
    current_target="$(readlink -f "$web_assets_entry")"
    [[ "$current_target" == "$static_web_assets_dir" ]] && return 0
  elif [[ -e "$web_assets_entry" ]]; then
    if [[ -n "$test_root" && -d "$web_assets_entry" ]]; then return 0; fi
    fail "前端哈希资源入口不是受管符号链接：${web_assets_entry}"
    return 1
  fi
  next_link="${test_root}/opt/.legion12-web-assets-next-${timestamp}"
  [[ ! -e "$next_link" && ! -L "$next_link" ]] || { fail "前端哈希资源入口临时路径已存在"; return 1; }
  ln -s "$static_web_assets_dir" "$next_link"
  mv -Tf "$next_link" "$web_assets_entry"
}

append_web_asset_keep_set() {
  local release_root="$1"
  local source_relative="$2"
  local public_prefix="$3"
  local source_root source relative
  source_root="${release_root}/${source_relative}"
  validate_web_assets_tree "$source_root" || return 1
  while IFS= read -r -d '' source; do
    relative="${source#${source_root}/}"
    printf '%s\n' "${public_prefix}/${relative}"
  done < <(find "$source_root" -type f -print0)
}

prune_web_assets() {
  local keep_file candidate relative keep_release
  keep_file="${deployment_dir}/.web-assets-keep-${short_commit}-${timestamp}-$$"
  [[ ! -e "$keep_file" && ! -L "$keep_file" ]] || { fail "前端哈希资源保留清单临时路径已存在"; return 1; }
  : > "$keep_file"
  for keep_release in "$@"; do
    append_web_asset_keep_set "$keep_release" "opcgpro-vue/dist/assets" "assets" >> "$keep_file" || return 1
  done
  sort -u -o "$keep_file" "$keep_file"
  while IFS= read -r -d '' candidate; do
    relative="${candidate#${static_web_assets_dir}/}"
    if ! grep -Fxq "$relative" "$keep_file"; then
      if ! rm -f -- "$candidate"; then rm -f -- "$keep_file"; return 1; fi
    fi
  done < <(find "${static_web_assets_dir}/assets" -type f -mmin +"$web_asset_retention_minutes" -print0)
  find "${static_web_assets_dir}/assets" -depth -type d -empty ! -path "${static_web_assets_dir}/assets" -delete || { rm -f -- "$keep_file"; return 1; }
  rm -f -- "$keep_file"
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
  for command_name in id flock python3 sha256sum tar curl systemctl nginx runuser node find readlink ln mv install cmp awk grep tr chmod chown sort timeout date seq head stat dirname basename; do
    require_command "$command_name"
  done
  test -e "$active_dir" || fail "当前部署入口不存在：${active_dir}"
  test -f "$environment_file" || fail "管理员环境配置不存在"
  test -f "$health_verifier" || fail "缺少提交身份校验器：${health_verifier}"
  [[ -f "$runtime_verifier" && ! -L "$runtime_verifier" && -r "$runtime_verifier" ]] || fail "缺少普通只读停服数据库校验器"
  [[ -f "$drain_consumer" && ! -L "$drain_consumer" && -x "$drain_consumer" ]] || fail "缺少普通可执行部署排空消费者"
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
  grep -Fq 'location ^~ /assets/' <<<"$nginx_dump" || fail "Nginx 未为前端哈希资源配置严格静态路由"
  grep -Fq 'root /opt/legion12-web-assets;' <<<"$nginx_dump" || fail "Nginx 前端哈希资源未使用兼容资源入口"
  grep -Fq 'try_files $uri =404;' <<<"$nginx_dump" || fail "Nginx 前端哈希资源缺失时未严格返回 404"
  log "服务器快速发布环境检查通过"
}

validate_archive() {
  local archive="$1"
  while IFS= read -r member; do
    [[ "$member" != /* ]] || fail "压缩包包含绝对路径"
    [[ "/${member}/" != *"/../"* ]] || fail "压缩包包含越界路径"
  done < <(tar -tzf "$archive")
}

read_deployment_capability_fingerprint() {
  local release_root="$1"
  local expected_commit="$2"
  local marker="${release_root}/.deployment-drain-capability.json"
  node - "$marker" "$expected_commit" <<'NODE'
const { lstatSync, readFileSync } = require('node:fs')
const marker = process.argv[2]
const expectedCommit = process.argv[3]
const stat = lstatSync(marker)
if (!stat.isFile() || stat.isSymbolicLink()) process.exit(2)
const value = JSON.parse(readFileSync(marker, 'utf8'))
const keys = Object.keys(value).sort()
const expectedKeys = ['commit', 'protocolVersion', 'requiresExternalStopPermit', 'runtimeDataCompatibility', 'runtimeSourceCompatibility', 'schema']
if (JSON.stringify(keys) !== JSON.stringify(expectedKeys)
    || value.schema !== 1
    || value.protocolVersion !== 1
    || value.requiresExternalStopPermit !== true
    || value.runtimeDataCompatibility !== 'l12-runtime-v1'
    || !/^[a-f0-9]{64}$/.test(value.runtimeSourceCompatibility)
    || value.commit !== expectedCommit) process.exit(2)
process.stdout.write(value.runtimeSourceCompatibility)
NODE
}

validate_deployment_capability() {
  local actual
  actual="$(read_deployment_capability_fingerprint "$1" "$2")" || return 1
  [[ -z "${3:-}" || "$actual" == "$3" ]]
}

assert_before_stop_deadline() {
  local now
  now="$(date -u +%s)"
  [[ "$now" =~ ^[0-9]+$ ]] || { fail "无法读取部署截止时间"; return 1; }
  (( now < deployment_deadline_epoch - deployment_recovery_reserve_seconds )) \
    || { fail "部署截止时间不足以保留取消与恢复预算，拒绝消费停服许可或停止服务"; return 1; }
}

assert_before_recovery_deadline() {
  local now
  now="$(date -u +%s)"
  [[ "$now" =~ ^[0-9]+$ ]] || return 1
  (( now < deployment_deadline_epoch ))
}

parse_consumer_failure() {
  node -e '
let text="";process.stdin.setEncoding("utf8");process.stdin.on("data",chunk=>text+=chunk);
process.stdin.on("end",()=>{try{const value=JSON.parse(text);const keys=Object.keys(value).sort();
const expected=["failedStage","ok","reasonCode","retrySafe","schema","stateDisposition"];
const known=new Set(["ARGUMENT_REJECTED","TOKEN_REJECTED","RESPONSE_CONTRACT_REJECTED","AUTHENTICATION_REJECTED","AUTHORIZATION_REJECTED","AUDIT_UNAVAILABLE","PROTOCOL_UNAVAILABLE","REQUEST_REJECTED","HTTP_REJECTED","STATUS_REJECTED","PREEXISTING_OR_UNKNOWN_FENCE","OWNER_OR_STATE_MISMATCH","BEGIN_STATE_UNKNOWN","BEGIN_REJECTED","READINESS_CONTRACT_REJECTED","PERMIT_MISMATCH","SEAL_STATE_UNKNOWN","SEAL_REJECTED","DRAIN_TIMEOUT","CONSUME_STATE_UNKNOWN","CONSUME_REJECTED","CANCEL_FAILED","TRANSPORT_REJECTED","TRANSPORT_STATE_UNKNOWN","RESTART_STATE_REJECTED","OPEN_PRECONDITION_REJECTED","OPEN_RESPONSE_REJECTED","OPEN_STATE_UNKNOWN","DEPLOYMENT_DEADLINE_EXCEEDED","CONSUMER_REJECTED"]);
if(JSON.stringify(keys)!==JSON.stringify(expected)||value.schema!==1||value.ok!==false||!known.has(value.reasonCode)||typeof value.failedStage!=="string"||!new Set(["unchanged","open","closed","unknown"]).has(value.stateDisposition)||typeof value.retrySafe!=="boolean")process.exit(2);process.stdout.write(value.reasonCode+"\t"+value.stateDisposition)}catch{process.exit(2)}});'
}

validate_consumer_receipt() {
  local expected_command="$1" expected_active_commit="$2" expected_phase="$3"
  node -e "$(cat <<'NODE'
const { createHash } = require('node:crypto')
const [command, action, packageSha256, operationId, targetCommit, activeCommit, phase] = process.argv.slice(1)
let text='';process.stdin.setEncoding('utf8');process.stdin.on('data',chunk=>text+=chunk)
process.stdin.on('end',()=>{try{
  const value=JSON.parse(text)
  const keys=Object.keys(value).sort()
  const expectedKeys=['action','activeCommit','command','epoch','ok','operationId','packageSha256','phase','processInstance','protocolVersion','schema','sealId','stateBindingSha256','targetCommit','transactionBindingSha256']
  const guid=/^[0-9a-f]{32}$/; const sha=/^[0-9a-f]{64}$/; const commit=/^[0-9a-f]{40}$/
  if(JSON.stringify(keys)!==JSON.stringify(expectedKeys)||value.schema!==1||value.ok!==true||value.protocolVersion!==1
    ||value.command!==command||value.action!==action||value.packageSha256!==packageSha256
    ||value.operationId!==operationId||value.targetCommit!==targetCommit||value.activeCommit!==activeCommit
    ||value.phase!==phase||!guid.test(value.processInstance)||!Number.isSafeInteger(value.epoch)||value.epoch<0
    ||!sha.test(value.transactionBindingSha256)||!sha.test(value.stateBindingSha256)||!commit.test(value.activeCommit))process.exit(2)
  if(command==='acquire-stop'){if(value.phase!=='sealed'||!guid.test(value.sealId))process.exit(2)}
  else if(value.sealId!==null)process.exit(2)
  const transaction=JSON.stringify({action,operationId,packageSha256,schema:1,targetCommit})
  const transactionHash=createHash('sha256').update(transaction).digest('hex')
  const state=JSON.stringify({activeCommit,epoch:value.epoch,processInstance:value.processInstance,sealId:value.sealId,transactionBindingSha256:transactionHash})
  const stateHash=createHash('sha256').update(state).digest('hex')
  if(value.transactionBindingSha256!==transactionHash||value.stateBindingSha256!==stateHash)process.exit(2)
  process.stdout.write([value.processInstance,String(value.epoch),value.sealId===null?'-':value.sealId,transactionHash,stateHash].join('\t'))
}catch{process.exit(2)}})
NODE
)" "$expected_command" "$mode" "$release_sha256" "$deployment_operation_id" "$commit" "$expected_active_commit" "$expected_phase"
}

run_consumer() {
  local expected_command="$1" expected_active_commit="$2" expected_phase="$3"
  shift 3
  local status parsed
  consumer_output=""
  if consumer_output="$(printf '%s\n' "$deployment_control_token" | "$drain_consumer" "$@")"; then
    :
  else
    status=$?
    if parsed="$(printf '%s' "$consumer_output" | parse_consumer_failure 2>/dev/null)"; then
      IFS=$'\t' read -r drain_failure_code drain_failure_disposition <<<"$parsed"
    else
      drain_failure_code="CONSUMER_REJECTED"
      drain_failure_disposition="unknown"
    fi
    consumer_output=""
    return "$status"
  fi
  if ! parsed="$(printf '%s' "$consumer_output" | validate_consumer_receipt "$expected_command" "$expected_active_commit" "$expected_phase")"; then
    drain_failure_code="CONSUMER_RECEIPT_REJECTED"
    drain_failure_disposition="unknown"
    consumer_output=""
    return 2
  fi
  consumer_output=""
  consumer_fields="$parsed"
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
deployment_deadline_epoch="${12:-}"
[[ -z "${13:-}" ]] || fail "发布参数数量错误"
[[ "$mode" == "deploy" || "$mode" == "rollback" || "$mode" == "dry-run" ]] || fail "用法：$0 <deploy|rollback|dry-run> <提交> <运行包SHA256> <运行包> <- legacy已退役> <- > <- > <优化卡图版本> <优化卡图SHA256|-> <优化卡图包|-> [/opt|/www/legion12] [截止Unix秒]"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "提交哈希格式错误"
[[ "$release_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "运行包 SHA256 格式错误"
if [[ "$mode" == "deploy" || "$mode" == "rollback" ]]; then
  [[ "$deployment_deadline_epoch" =~ ^[0-9]{10,}$ ]] || fail "正式发布必须携带有界部署截止时间"
fi
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
runtime_proof_failure_code=""
drain_failure_code=""
drain_failure_disposition=""
deployment_control_token=""
consumer_output=""
consumer_fields=""
deployment_operation_id=""
drain_process_instance=""
drain_epoch=""
drain_seal_id=""
drain_transaction_sha256=""
drain_state_sha256=""
target_runtime_source_compatibility=""
current_runtime_source_compatibility=""
restart_process_instance=""
restart_epoch=""
old_main_pid=""
new_main_pid=""
drain_stop_consumed=0
drain_opened=0
stop_attempted=0
sandbox_fence_created=0
runtime_restore_dir=""
failed_runtime_dir=""
failure_stage="preflight"

cleanup() {
  deployment_control_token=""
  consumer_output=""
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
  local row backup checksum index=0
  local rows=() complete_rows=()
  assert_cleanup_root "$runtime_backup_dir" || return 1
  mapfile -t rows < <(find "$runtime_backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.tar.gz' -printf '%T@:%p\n' | sort -rn)
  if [[ "$prune_runtime_backups_enabled" != "1" ]]; then
    local paired=0
    for row in "${rows[@]}"; do
      backup="${row#*:}"; checksum="${backup}.sha256"
      [[ -f "$checksum" && ! -L "$checksum" ]] && paired=$((paired + 1))
    done
    log "部署后收口统计到 ${paired} 组带校验文件的 runtime 快照；自动删除开关=0，配置保留=${runtime_backup_keep_groups} 组"
    log "runtime 快照默认仅统计不删除；需 Main 获得明确授权后启用"
    return 0
  fi
  for row in "${rows[@]}"; do
    backup="${row#*:}"; checksum="${backup}.sha256"
    if [[ ! -f "$checksum" || -L "$checksum" ]] || ! (cd "$runtime_backup_dir" && sha256sum -c "$(basename "$checksum")" >/dev/null 2>&1); then
      log "部署后收口保留不完整或无法校验的 runtime 快照：${backup}"
      continue
    fi
    complete_rows+=("$row")
  done
  log "部署后收口统计到 ${#complete_rows[@]} 组完整 runtime 快照；自动删除开关=${prune_runtime_backups_enabled}，配置保留=${runtime_backup_keep_groups} 组"
  for row in "${complete_rows[@]}"; do
    backup="${row#*:}"; checksum="${backup}.sha256"
    if (( index < runtime_backup_keep_groups )); then
      log "部署后收口保留 runtime 快照：${backup}"
    else
      assert_cleanup_child "$backup" "$runtime_backup_dir" && assert_cleanup_child "$checksum" "$runtime_backup_dir" || continue
      if ! rm -f -- "$backup" "$checksum"; then log "部署后收口无法删除 runtime 快照，保守保留：${backup}"; continue; fi
      log "部署后收口删除旧 runtime 快照：${backup}"
    fi
    index=$((index + 1))
  done
  while IFS= read -r -d '' backup; do
    assert_cleanup_child "$backup" "$runtime_backup_dir" || continue
    if ! rm -f -- "$backup"; then log "部署后收口无法删除快照临时文件，保守保留：${backup}"; continue; fi
    log "部署后收口删除废弃快照临时文件：${backup}"
  done < <(find "$runtime_backup_dir" -maxdepth 1 -type f -name 'runtime-before-*.partial' -print0)
}

tree_bytes() {
  local root file total=0 size
  for root in "$@"; do
    [[ -d "$root" && ! -L "$root" ]] || continue
    while IFS= read -r -d '' file; do
      size="$(stat -c '%s' "$file")" || return 1
      total=$((total + size))
    done < <(find "$root" -type f -print0)
  done
  printf '%s' "$total"
}

assert_cleanup_root() {
  local root="$1" canonical
  [[ -d "$root" && ! -L "$root" ]] || { log "部署后收口跳过不安全根目录：${root}"; return 1; }
  canonical="$(readlink -f "$root")" || return 1
  [[ "$canonical" == "$root" ]] || { log "部署后收口跳过非规范根目录：${root}"; return 1; }
}

assert_cleanup_child() {
  local candidate="$1" root="$2" parent
  assert_cleanup_root "$root" || return 1
  [[ "$candidate" == "${root}/"* && "$candidate" != "$root" ]] || return 1
  [[ ! -L "$candidate" ]] || { log "部署后收口跳过符号链接：${candidate}"; return 1; }
  parent="$(readlink -f "$(dirname "$candidate")")" || return 1
  [[ "$parent" == "$root" || "$parent" == "${root}/"* ]] || return 1
}

select_retained_releases() {
  local active_target="$1" previous_release="${2:-}" row candidate marker release_commit rollback_count=0
  retained_releases=("$active_target")
  assert_cleanup_child "$active_target" "$releases_dir" || return 1
  [[ -d "$active_target" ]] || return 1
  if [[ -n "$previous_release" && "$previous_release" != "$active_target" && "$previous_release" == "${test_root}/opt/legion12-legacy-"* ]]; then
    marker="${previous_release}/.deployment-commit"
    if assert_cleanup_child "$previous_release" "${test_root}/opt" && [[ -f "$marker" && ! -L "$marker" ]]; then
      release_commit="$(tr -d '\r\n' < "$marker")"
      if [[ "$release_commit" =~ ^[0-9a-f]{40}$ ]]; then
        retained_releases+=("$previous_release")
        rollback_count=1
      else
        log "部署后收口保留但不清理身份无效的 legacy release：${previous_release}"
      fi
    else
      log "部署后收口无法验证 legacy 回滚版本，保守保留：${previous_release}"
    fi
  fi
  mapfile -t release_rows < <(find "$releases_dir" -mindepth 1 -maxdepth 1 -type d -printf '%T@:%p\n' | sort -rn)
  for row in "${release_rows[@]}"; do
    candidate="${row#*:}"
    [[ "$candidate" == "$active_target" ]] && continue
    marker="${candidate}/.deployment-commit"
    if ! assert_cleanup_child "$candidate" "$releases_dir" || [[ ! -f "$marker" || -L "$marker" ]]; then
      log "部署后收口保留无法验证的 release：${candidate}"
      continue
    fi
    release_commit="$(tr -d '\r\n' < "$marker")"
    if [[ ! "$release_commit" =~ ^[0-9a-f]{40}$ ]]; then
      log "部署后收口保留身份无效的 release：${candidate}"
      continue
    fi
    if (( rollback_count < 2 )); then
      retained_releases+=("$candidate")
      rollback_count=$((rollback_count + 1))
    fi
  done
  log "部署后收口保留 release：${retained_releases[*]}"
}

prune_legacy_releases() {
  local candidate keep retained marker release_commit
  assert_cleanup_root "${test_root}/opt" || return 1
  while IFS= read -r -d '' candidate; do
    keep=0
    for retained in "${retained_releases[@]}"; do [[ "$candidate" == "$retained" ]] && keep=1; done
    (( keep == 1 )) && continue
    assert_cleanup_child "$candidate" "${test_root}/opt" || continue
    marker="${candidate}/.deployment-commit"
    [[ -f "$marker" && ! -L "$marker" ]] || { log "部署后收口跳过无法验证的 legacy release：${candidate}"; continue; }
    release_commit="$(tr -d '\r\n' < "$marker")"
    [[ "$release_commit" =~ ^[0-9a-f]{40}$ ]] || { log "部署后收口跳过身份无效的 legacy release：${candidate}"; continue; }
    if ! rm -rf -- "$candidate"; then log "部署后收口无法删除旧 legacy release，保守保留：${candidate}"; continue; fi
    log "部署后收口删除旧 legacy release：${candidate}"
  done < <(find "${test_root}/opt" -mindepth 1 -maxdepth 1 -type d -name 'legion12-legacy-*' -print0)
}

prune_release_set() {
  local row candidate keep retained marker release_commit
  for row in "${release_rows[@]}"; do
    candidate="${row#*:}"; keep=0
    for retained in "${retained_releases[@]}"; do [[ "$candidate" == "$retained" ]] && keep=1; done
    (( keep == 1 )) && continue
    assert_cleanup_child "$candidate" "$releases_dir" || continue
    marker="${candidate}/.deployment-commit"
    [[ -f "$marker" && ! -L "$marker" ]] || { log "部署后收口跳过无法验证的 release：${candidate}"; continue; }
    release_commit="$(tr -d '\r\n' < "$marker")"
    [[ "$release_commit" =~ ^[0-9a-f]{40}$ ]] || { log "部署后收口跳过身份无效的 release：${candidate}"; continue; }
    if ! rm -rf -- "$candidate"; then log "部署后收口无法删除旧 release，保守保留：${candidate}"; continue; fi
    log "部署后收口删除旧 release：${candidate}"
  done
}

prune_card_asset_versions() {
  local release link target candidate retained keep expected_hash
  local referenced_assets=()
  assert_cleanup_root "$static_card_assets_dir" || return 1
  for release in "${retained_releases[@]}"; do
    link="${release}/opcgpro-vue/dist/card-assets"
    if [[ ! -e "$link" && ! -L "$link" ]]; then continue; fi
    if [[ ! -L "$link" ]]; then
      log "优化卡图清理跳过：保留 release 的 card-assets 不是可信链接：${release}"
      return 0
    fi
    target="$(readlink -f "$link")"
    if [[ "$target" != "${static_card_assets_dir}/"* && "$target" != "${default_static_card_assets_dir}/"* ]]; then
      log "优化卡图清理跳过：保留 release 引用越界：${release}"
      return 0
    fi
    [[ -d "$target" && ! -L "$target" ]] || { log "优化卡图清理跳过：引用目标不可验证：${target}"; return 0; }
    expected_hash="$(basename "$target")"
    validate_card_assets_tree "$target" "$expected_hash" || { log "优化卡图清理跳过：manifest 无法验证：${target}"; return 0; }
    referenced_assets+=("$target")
  done
  log "部署后收口保留优化卡图：${referenced_assets[*]:-(无)}"
  while IFS= read -r -d '' candidate; do
    keep=0
    for retained in "${referenced_assets[@]}"; do [[ "$candidate" == "$retained" ]] && keep=1; done
    (( keep == 1 )) && continue
    if [[ ! "$(basename "$candidate")" =~ ^[0-9a-f]{64}$ ]]; then log "优化卡图清理跳过非受管版本：${candidate}"; continue; fi
    assert_cleanup_child "$candidate" "$static_card_assets_dir" || continue
    if ! rm -rf -- "$candidate"; then log "部署后收口无法删除无引用优化卡图，保守保留：${candidate}"; continue; fi
    log "部署后收口删除无引用优化卡图：${candidate}"
  done < <(find "$static_card_assets_dir" -mindepth 1 -maxdepth 1 -type d -print0)
}

prune_consumed_incoming() {
  local candidate name identity release marker proven
  assert_cleanup_root "$incoming_dir" || return 1
  while IFS= read -r -d '' candidate; do
    name="$(basename "$candidate")"; proven=0
    if [[ "$name" =~ ^l12-release-([0-9a-f]{40})\.tar\.gz$ ]]; then
      identity="${BASH_REMATCH[1]}"
      while IFS= read -r -d '' release; do
        marker="${release}/.deployment-commit"
        [[ -f "$marker" && ! -L "$marker" && "$(tr -d '\r\n' < "$marker")" == "$identity" ]] && proven=1
      done < <(find "$releases_dir" -mindepth 1 -maxdepth 1 -type d -print0)
    elif [[ "$name" =~ ^l12-card-assets-([0-9a-f]{64})\.tar\.gz$ ]]; then
      identity="${BASH_REMATCH[1]}"
      [[ -d "${static_card_assets_dir}/${identity}" && ! -L "${static_card_assets_dir}/${identity}" ]] && proven=1
    fi
    if (( proven == 1 )); then
      assert_cleanup_child "$candidate" "$incoming_dir" || continue
      if ! rm -f -- "$candidate"; then log "部署后收口无法删除已消费 incoming，保守保留：${candidate}"; continue; fi
      log "部署后收口删除已消费 incoming：${candidate}"
    else
      log "部署后收口保留无法证明已消费的 incoming：${candidate}"
    fi
  done < <(find "$incoming_dir" -mindepth 1 -maxdepth 1 -type f -print0)
}

prune_managed_staging() {
  local candidate
  assert_cleanup_root "$stage_parent" || return 1
  while IFS= read -r -d '' candidate; do
    assert_cleanup_child "$candidate" "$stage_parent" || continue
    if ! rm -rf -- "$candidate"; then log "部署后收口无法删除废弃 staging，保守保留：${candidate}"; continue; fi
    log "部署后收口删除废弃 staging：${candidate}"
  done < <(find "$stage_parent" -mindepth 1 -maxdepth 1 -type d \( -name 'legion12-staging-*' -o -name 'legion12-card-assets-staging-*' \) -print0)
}

converge_deployment_storage() {
  local before after released
  before="$(tree_bytes "$releases_dir" "$runtime_backup_dir" "$incoming_dir" "$static_card_assets_dir" "$static_web_assets_dir")" || before=0
  select_retained_releases "$1" "${2:-}" || { log "部署后存储收口跳过：无法证明 release 保留边界"; return 0; }
  prune_runtime_backups || log "runtime 快照统计/清理跳过"
  prune_card_asset_versions || log "优化卡图清理跳过"
  prune_consumed_incoming || log "incoming 清理跳过"
  prune_managed_staging || log "staging 清理跳过"
  if ! prune_web_assets "${retained_releases[@]}"; then log "共享前端哈希资源清理跳过，保留全部兼容资源"; fi
  prune_release_set
  prune_legacy_releases || log "legacy release 清理跳过"
  after="$(tree_bytes "$releases_dir" "$runtime_backup_dir" "$incoming_dir" "$static_card_assets_dir" "$static_web_assets_dir")" || after="$before"
  released=$((before > after ? before - after : 0))
  log "部署后存储收口：清理前=${before} 字节，清理后=${after} 字节，释放=${released} 字节"
}

restore_previous() {
  local restore_link="${test_root}/opt/.legion12-restore-${timestamp}"
  ln -s "$previous_target" "$restore_link" || return 1
  mv -Tf "$restore_link" "$active_dir" || return 1
}

read_service_main_pid() {
  local pid
  pid="$(systemctl show --property MainPID --value "$service_name")" || return 1
  [[ "$pid" =~ ^[0-9]+$ ]] || return 1
  printf '%s\n' "$pid"
}

stop_service_and_confirm() {
  systemctl stop "$service_name" >/dev/null 2>&1 || true
  if systemctl is-active --quiet "$service_name" >/dev/null 2>&1; then
    return 1
  fi
  [[ "$(read_service_main_pid)" == "0" ]] || return 1
  return 0
}

write_sandbox_fence_payload() {
  printf 'schema=1\naction=%s\npackageSha256=%s\ncommit=%s\noperationId=%s\n' \
    "$mode" "$release_sha256" "$commit" "$deployment_operation_id"
}

assert_sandbox_fence_absent() {
  [[ ! -e "$sandbox_fence" && ! -L "$sandbox_fence" ]]
}

assert_owned_sandbox_fence() {
  local expected_sha256 actual_sha256
  [[ "$sandbox_fence_created" -eq 1 && -f "$sandbox_fence" && ! -L "$sandbox_fence" ]] || return 1
  expected_sha256="$(write_sandbox_fence_payload | sha256sum | awk '{print $1}')" || return 1
  actual_sha256="$(sha256sum -- "$sandbox_fence" | awk '{print $1}')" || return 1
  [[ "$expected_sha256" =~ ^[0-9a-f]{64}$ && "$actual_sha256" == "$expected_sha256" ]]
}

create_owned_sandbox_fence() {
  assert_sandbox_fence_absent || return 1
  # noclobber turns the final open into an atomic create. A file, directory, or
  # symlink appearing after the preflight is therefore preserved, never replaced.
  ( umask 022; set -o noclobber; write_sandbox_fence_payload > "$sandbox_fence" ) || return 1
  sandbox_fence_created=1
  assert_owned_sandbox_fence
}

remove_sandbox_fence_and_confirm() {
  log "最终开放前清除并核验临时沙盒围栏"
  assert_owned_sandbox_fence || return 1
  rm -f -- "$sandbox_fence" || return 1
  [[ ! -e "$sandbox_fence" && ! -L "$sandbox_fence" ]] || return 1
  sandbox_fence_created=0
  log "临时沙盒围栏已确认不存在；下一步 exact cancel 是最终业务开放点"
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
    printf 'runtimeProofFailureCode=%s\n' "$runtime_proof_failure_code"
    printf 'deploymentAction=%s\n' "$mode"
    printf 'drainFailureCode=%s\n' "$drain_failure_code"
    printf 'drainFailureDisposition=%s\n' "$drain_failure_disposition"
    printf 'drainStopConsumed=%s\n' "$drain_stop_consumed"
    printf 'drainOpened=%s\n' "$drain_opened"
    printf 'drainTransactionBindingSha256=%s\n' "$drain_transaction_sha256"
    printf 'drainStateBindingSha256=%s\n' "$drain_state_sha256"
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

observe_restarted_drain() {
  local expected_active_commit="$1"
  assert_before_recovery_deadline || { drain_failure_code="DEPLOYMENT_DEADLINE_EXCEEDED"; return 1; }
  run_consumer "observe-restart" "$expected_active_commit" "draining" \
    observe-restart --action "$mode" --package-sha256 "$release_sha256" \
    --operation-id "$deployment_operation_id" --target-commit "$commit" \
    --expected-active-commit "$expected_active_commit" \
    --previous-process-instance "$drain_process_instance" --previous-epoch "$drain_epoch" || return 1
  IFS=$'\t' read -r restart_process_instance restart_epoch _ _ _ <<<"$consumer_fields"
  [[ "$restart_process_instance" =~ ^[0-9a-f]{32}$ && "$restart_epoch" =~ ^[0-9]+$ \
    && "$restart_process_instance" != "$drain_process_instance" \
    && "$restart_epoch" -gt "$drain_epoch" ]] || { drain_failure_code="CONSUMER_RECEIPT_REJECTED"; return 1; }
}

cancel_restarted_drain() {
  local expected_active_commit="$1"
  local opened_process opened_epoch opened_seal opened_transaction opened_state
  assert_before_recovery_deadline || { drain_failure_code="DEPLOYMENT_DEADLINE_EXCEEDED"; return 1; }
  run_consumer "cancel-open" "$expected_active_commit" "open" \
    cancel-open --action "$mode" --package-sha256 "$release_sha256" \
    --operation-id "$deployment_operation_id" --target-commit "$commit" \
    --expected-active-commit "$expected_active_commit" \
    --process-instance "$restart_process_instance" --epoch "$restart_epoch" || return 1
  IFS=$'\t' read -r opened_process opened_epoch opened_seal opened_transaction opened_state <<<"$consumer_fields"
  [[ "$opened_process" == "$restart_process_instance" && "$opened_epoch" =~ ^[0-9]+$ \
    && "$opened_epoch" -gt "$restart_epoch" && "$opened_seal" == "-" \
    && "$opened_transaction" == "$drain_transaction_sha256" ]] \
    || { drain_failure_code="CONSUMER_RECEIPT_REJECTED"; return 1; }
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
  else
    if [[ "$drain_stop_consumed" -eq 0 \
      && ( "$drain_failure_disposition" == "closed" || "$drain_failure_disposition" == "unknown" ) ]]; then
      write_failure_record "deployment drain state is not proven open; service and latest runtime preserved; manual reconciliation required" 1 || true
      log "部署排空消费者未能证明控制面保持开放；不停止服务、不恢复旧数据，保留现场等待人工对账。"
      cleanup
      exit "$status"
    fi
    if [[ "$drain_stop_consumed" -eq 1 && "$service_stopped" -eq 0 ]]; then
      if [[ "$stop_attempted" -eq 0 ]] && assert_before_recovery_deadline && stop_service_and_confirm; then
        stop_attempted=1
        service_stopped=1
      else
        write_failure_record "stop permit consumed but service stop was not confirmed; runtime preserved; manual reconciliation required" 1 || true
        log "停服许可已消费但无法确认服务停止；保留最新 runtime 与排空围栏，须人工对账。"
        cleanup
        exit "$status"
      fi
    fi
  fi

  if [[ "$new_service_launch_attempted" -eq 0 && "$service_stopped" -eq 1 ]]; then
    if [[ "$active_entry_changed" -eq 1 || "$switched" -eq 1 ]]; then
      restore_previous || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]] && assert_before_recovery_deadline; then
      systemctl start "$service_name" || rollback_ok=0
    else
      rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      systemctl is-active --quiet "$service_name" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      new_main_pid="$(read_service_main_pid)" || rollback_ok=0
      [[ "$new_main_pid" =~ ^[1-9][0-9]+$ && "$new_main_pid" != "$old_main_pid" ]] || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 && "$drain_stop_consumed" -eq 1 ]]; then
      observe_restarted_drain "$previous_commit" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      verify_release_health "$previous_commit" "恢复版本" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      verify_release_websocket "恢复版本" || rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 ]] && ! assert_before_recovery_deadline; then
      rollback_ok=0
    fi
    if [[ "$rollback_ok" -eq 1 && "$drain_stop_consumed" -eq 1 ]]; then
      remove_sandbox_fence_and_confirm || rollback_ok=0
      if [[ "$rollback_ok" -eq 1 ]]; then cancel_restarted_drain "$previous_commit" || rollback_ok=0; fi
    fi
    if [[ "$rollback_ok" -eq 1 ]]; then
      service_stopped=0
      drain_opened=1
      drain_failure_code=""
      drain_failure_disposition="open"
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
target_runtime_source_compatibility="$(read_deployment_capability_fingerprint "$stage_dir" "$commit")" \
  || fail "运行包缺少精确部署排空与 runtime 源兼容能力标记"
test -r "${stage_dir}/publish/GrandUMIServer.dll" || fail "运行包缺少后端入口"
test -r "${stage_dir}/opcgpro-vue/dist/index.html" || fail "运行包缺少前端首页"
validate_web_assets_tree "${stage_dir}/opcgpro-vue/dist/assets"
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
    if [[ "$mode" != "dry-run" ]]; then
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
if [[ "$mode" == "rollback" ]]; then
  current_runtime_source_compatibility="$(read_deployment_capability_fingerprint "$previous_target" "$previous_commit")" \
    || fail "当前活动包缺少可验证的 runtime 源兼容指纹，拒绝 rollback"
  [[ "$target_runtime_source_compatibility" == "$current_runtime_source_compatibility" ]] \
    || fail "rollback 目标与当前活动包的 runtime 源兼容指纹不同，拒绝读取或覆盖最新 runtime"
fi

failure_stage="install-compatible-web-assets"
install_web_assets_tree "${previous_target}/opcgpro-vue/dist/assets" "assets"
install_web_assets_tree "${stage_dir}/opcgpro-vue/dist/assets" "assets"
ensure_web_assets_entry
sample_web_asset="$(find "${static_web_assets_dir}/assets" -type f -print -quit)"
test -n "$sample_web_asset" || fail "共享前端哈希资源池为空"
runuser -u "$web_user" -- test -r "$sample_web_asset" || fail "Nginx 账号无法读取共享前端哈希资源"

failure_stage="verify-current-service-before-drain"
assert_plain_directory "$runtime_dir" || fail "常规部署消费者要求既有共享 runtime；首次分离须走独立人工核验流程"
assert_sandbox_fence_absent || fail "常规部署开始前检测到未知沙盒围栏；拒绝覆盖或复用"
systemctl is-active --quiet "$service_name" || fail "当前正式服务不是 active，拒绝消费停服许可"
old_main_pid="$(read_service_main_pid)"
[[ "$old_main_pid" =~ ^[1-9][0-9]+$ ]] || fail "当前正式服务 MainPID 无效，拒绝消费停服许可"
verify_release_health "$previous_commit" "停服前当前版本"
verify_release_websocket "停服前当前版本"

failure_stage="consume-deployment-stop-permit"
assert_before_stop_deadline
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "消费停服许可前运行包 SHA256 已变化"
validate_deployment_capability "$stage_dir" "$commit" "$target_runtime_source_compatibility" || fail "消费停服许可前部署能力标记已变化"
if [[ "$mode" == "rollback" ]]; then
  validate_deployment_capability "$previous_target" "$previous_commit" "$current_runtime_source_compatibility" \
    || fail "消费停服许可前当前活动包 runtime 源兼容指纹已变化"
fi
if [[ -n "$test_root" ]]; then
  deployment_operation_id="${L12_DEPLOY_TEST_OPERATION_ID:-}"
else
  [[ -r /proc/sys/kernel/random/uuid ]] || fail "服务器无法生成部署操作标识"
  deployment_operation_id="$(tr -d '-' < /proc/sys/kernel/random/uuid)"
fi
[[ "$deployment_operation_id" =~ ^[0-9a-f]{32}$ ]] || fail "部署操作标识生成失败"
if ! IFS= read -r deployment_control_token; then
  fail "缺少标准输入中的部署排空会话"
fi
[[ "$deployment_control_token" =~ ^[0-9a-f]{64}$ ]] || { deployment_control_token=""; fail "部署排空会话格式无效"; }
drain_wait_seconds=$((deployment_deadline_epoch - $(date -u +%s) - deployment_recovery_reserve_seconds))
(( drain_wait_seconds > 0 )) || fail "部署截止时间不足以等待排空"
if (( drain_wait_seconds > 1800 )); then drain_wait_seconds=1800; fi
drain_failure_code="CONSUMER_INTERRUPTED"
drain_failure_disposition="unknown"
if run_consumer "acquire-stop" "$previous_commit" "sealed" \
  acquire-stop --action "$mode" --package-sha256 "$release_sha256" \
  --operation-id "$deployment_operation_id" --target-commit "$commit" \
  --expected-active-commit "$previous_commit" --wait-seconds "$drain_wait_seconds" \
  --poll-milliseconds 1000 --deadline-epoch "$deployment_deadline_epoch"; then
  :
else
  fail "部署排空消费者拒绝停服许可：${drain_failure_code}"
fi
IFS=$'\t' read -r drain_process_instance drain_epoch drain_seal_id drain_transaction_sha256 drain_state_sha256 <<<"$consumer_fields"
[[ "$drain_process_instance" =~ ^[0-9a-f]{32}$ && "$drain_epoch" =~ ^[0-9]+$ \
  && "$drain_seal_id" =~ ^[0-9a-f]{32}$ \
  && "$drain_transaction_sha256" =~ ^[0-9a-f]{64}$ \
  && "$drain_state_sha256" =~ ^[0-9a-f]{64}$ ]] || fail "停服许可回执字段无效"
drain_stop_consumed=1
drain_failure_code=""
drain_failure_disposition="closed"

failure_stage="stop-current-service"
[[ "$(sha256sum "$release_archive" | awk '{print $1}')" == "$release_sha256" ]] || fail "停服许可消费后运行包 SHA256 已变化"
validate_deployment_capability "$stage_dir" "$commit" "$target_runtime_source_compatibility" || fail "停服许可消费后部署能力标记已变化"
if [[ "$mode" == "rollback" ]]; then
  validate_deployment_capability "$previous_target" "$previous_commit" "$current_runtime_source_compatibility" \
    || fail "停服许可消费后当前活动包 runtime 源兼容指纹已变化"
fi
assert_before_stop_deadline
create_owned_sandbox_fence || fail "停服许可消费后无法原子创建本事务沙盒围栏；保留未知对象并保持排空关闭"
log "已消费同 owner/epoch 停服许可并创建本事务沙盒围栏；暂停服务"
stop_attempted=1
stop_service_and_confirm || fail "服务停止命令返回后无法确认 inactive 且 MainPID=0，拒绝继续备份或切换"
service_stopped=1
failure_stage="snapshot-runtime"
assert_owned_sandbox_fence || fail "停止服务后本事务沙盒围栏归属发生变化；保留现场并保持排空关闭"
chown -R "${service_user}:${service_user}" "$runtime_dir"
chmod 0750 "$runtime_dir"
backup_runtime
failure_stage="verify-stopped-runtime-backup"
if runtime_proof="$(timeout 310 python3 -B "$runtime_verifier" "$runtime_dir" "$runtime_backup" "$runtime_backup_sha256")"; then
  : # Conditional assignment prevents inherited ERR from recovering twice in a subshell.
else
  proof_status=$?
  if [[ "$proof_status" -eq 124 ]]; then
    runtime_proof_failure_code="EXTERNAL_TIMEOUT"
  else
    runtime_proof_failure_code="$(printf '%s' "$runtime_proof" | node -e '
let text="";process.stdin.setEncoding("utf8");process.stdin.on("data",chunk=>text+=chunk);
process.stdin.on("end",()=>{try{const proof=JSON.parse(text);const known=new Set(["TIME_BUDGET_EXCEEDED","SNAPSHOT_NOT_LATEST","PERSISTENT_FACTS_CHANGED","BACKUP_CHECKSUM_CHANGED","SQLITE_INTEGRITY_REJECTED","SCHEMA_CONTRACT_REJECTED","UNSAFE_PATH_OR_FILE_SET","SQLITE_READ_UNAVAILABLE","PROOF_REJECTED"]);process.stdout.write(proof.verified===false&&known.has(proof.reasonCode)?proof.reasonCode:"PROOF_REJECTED");}catch{process.stdout.write("PROOF_REJECTED");}});')"
  fi
  log "停服校验拒绝原因：${runtime_proof_failure_code}"
  fail "停服数据库与备份证明失败或超时，拒绝切换版本"
fi
runtime_proof_failure_code="PROOF_REJECTED"
if printf '%s' "$runtime_proof" | node -e '
let body="";process.stdin.on("data",chunk=>body+=chunk);process.stdin.on("end",()=>{
  try { const proof=JSON.parse(body);
    const stages=["path_guard","backup_hash_before","database_hash_before","snapshot_stream","sqlite_platform","sqlite_matches","database_hash_after","backup_hash_after"];
    if(proof.schema!==1||proof.verified!==true||proof.databases!==2||proof.sqliteQuickCheck!=="ok"
      ||proof.schemaAnchorsVerified!==true||proof.latestDatabaseAndWalEqualBackup!==true
      ||proof.persistentFactMutations!==0||proof.checkpointOrRepairPerformed!==false
      ||proof.backupSha256!==process.argv[1]
      ||!/^[a-f0-9]{64}$/.test(proof.databaseWalFingerprintSha256||"")
      ||!Number.isInteger(proof.emptyWalPresenceTransitions)||proof.emptyWalPresenceTransitions<0||proof.emptyWalPresenceTransitions>2
      ||!Number.isFinite(proof.elapsedMilliseconds)||proof.elapsedMilliseconds<0||proof.elapsedMilliseconds>300000
      ||!Array.isArray(proof.stageTimings)||proof.stageTimings.length!==stages.length
      ||proof.stageTimings.some((item,index)=>item.stage!==stages[index]
        ||!Number.isFinite(item.elapsedMilliseconds)||item.elapsedMilliseconds<0||item.elapsedMilliseconds>300000)) process.exit(2);
  } catch { process.exit(2); }
});' "$runtime_backup_sha256"; then
  runtime_proof_failure_code=""
else
  fail "停服校验成功合同无效，拒绝切换版本"
fi
log "停服最新数据库、WAL与备份只读校验通过；未执行修复或数据回退"
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
systemctl is-active --quiet "$service_name" || fail "目标服务启动后不是 active"
new_main_pid="$(read_service_main_pid)"
[[ "$new_main_pid" =~ ^[1-9][0-9]+$ && "$new_main_pid" != "$old_main_pid" ]] \
  || fail "目标服务启动后 MainPID 未精确更换"

failure_stage="verify-restarted-deployment-drain"
if observe_restarted_drain "$commit"; then
  :
else
  fail "目标服务未以相同部署 owner、递增 epoch 与精确提交继承排空围栏：${drain_failure_code}"
fi

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
停服数据库与快照验证：${runtime_proof}
旧版 /cards 卡图：已退役
内容寻址优化卡图版本：${card_assets_hash}
部署动作：${mode}
部署排空协议：v1 consumer-level binding（非服务端原子 package binding）
目标包runtime源兼容指纹：${target_runtime_source_compatibility}
当前包runtime源兼容指纹：${current_runtime_source_compatibility:--}
部署事务绑定SHA256：${drain_transaction_sha256}
停服许可状态绑定SHA256：${drain_state_sha256}
停服前进程实例：${drain_process_instance}
停服前epoch：${drain_epoch}
重启后进程实例：${restart_process_instance}
重启后epoch：${restart_epoch}
域名：${public_host}
部署日期：$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

failure_stage="clear-sandbox-fence-before-open"
remove_sandbox_fence_and_confirm || fail "无法在最终开放前精确清除临时沙盒围栏；保持部署排空关闭"
failure_stage="open-new-release"
assert_before_recovery_deadline || { drain_failure_code="DEPLOYMENT_DEADLINE_EXCEEDED"; fail "部署截止时间已到，拒绝重新开放业务"; }
if cancel_restarted_drain "$commit"; then
  drain_opened=1
  drain_failure_code=""
  drain_failure_disposition="open"
else
  fail "无法精确取消目标进程的部署排空围栏：${drain_failure_code}"
fi
deployment_control_token=""
trap - ERR INT TERM

# The application drain is now exactly open, and this cancel is the final
# business-success linearization point. Remaining cleanup is non-mutating to
# service/runtime/admission state.
if ! converge_deployment_storage "$release_dir" "$previous_target"; then
  log "部署后存储收口未全部完成；已验证的新版本保持运行，未删除项将在下次安全收口重试"
fi
if ! rm -f -- "$release_archive"; then log "发布已完成，但 incoming 运行包清理失败，保守保留"; fi
if [[ "$card_assets_archive" != "-" ]] && ! rm -f -- "$card_assets_archive"; then
  log "发布已完成，但 incoming 卡图包清理失败，保守保留"
fi
log "快速${mode}完成：${commit}"
