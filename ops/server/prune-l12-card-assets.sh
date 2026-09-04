#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

readonly active_dir="/opt/legion12-test"
readonly legacy_root="/opt/legion12-static/cards"
readonly asset_root="/opt/legion12-static/card-assets"
readonly incoming_root="/opt/legion12-deployment/incoming"
readonly public_base="https://legion-12.com"

fail() { printf '[L12 卡图清理] 错误：%s\n' "$*" >&2; exit 1; }
log() { printf '[L12 卡图清理] %s\n' "$*"; }

[[ "$(id -u)" -eq 0 ]] || fail "必须以 root 身份执行"
[[ "${1:-}" == "dry-run" || "${1:-}" == "apply" ]] || fail "用法：$0 <dry-run|apply>"
[[ -L "${active_dir}/opcgpro-vue/dist/card-assets" ]] || fail "当前版本未链接内容寻址卡图"
[[ ! -e "${active_dir}/opcgpro-vue/dist/cards" ]] || fail "当前版本仍依赖旧版 /cards 卡图"

active_target="$(readlink -f "${active_dir}/opcgpro-vue/dist/card-assets")"
[[ "$active_target" == "${asset_root}/"* ]] || fail "当前卡图目标越界：${active_target}"
active_hash="${active_target#${asset_root}/}"
[[ "$active_hash" =~ ^[0-9a-f]{64}$ && "$active_target" == "${asset_root}/${active_hash}" ]] || fail "当前卡图版本目录无效"
[[ -f "${active_target}/card-assets.manifest.json" ]] || fail "当前卡图缺少 manifest"

node - "$active_target" "$active_hash" <<'NODE'
const { readFileSync } = require('node:fs')
const { join } = require('node:path')
const [root, expected] = process.argv.slice(2)
const manifest = JSON.parse(readFileSync(join(root, 'card-assets.manifest.json'), 'utf8'))
if (manifest.schemaVersion !== 3 || manifest.complete !== true || manifest.assetVersion !== expected ||
    manifest.cardCount !== 361 || manifest.playableCardCount !== 324 || manifest.presentationCardCount !== 37 ||
    !manifest.cards?.['ST01-01'] || !manifest.cards?.['S01-0101b'] || !manifest.cards?.['S01-01C1A'] ||
    !manifest.cards?.['S02-06C1A'] || !manifest.cards?.['ST01-C1st']) process.exit(2)
NODE

public_hash="$(curl -fsS "${public_base}/card-assets/card-assets.manifest.json" | node -e "let b='';process.stdin.on('data',c=>b+=c);process.stdin.on('end',()=>process.stdout.write(JSON.parse(b).assetVersion||''))")"
[[ "$public_hash" == "$active_hash" ]] || fail "公网与活动卡图版本不一致"

mapfile -t obsolete_assets < <(find "$asset_root" -mindepth 1 -maxdepth 1 -type d ! -name "$active_hash" -print | sort)
mapfile -t legacy_archives < <(find "$incoming_root" -maxdepth 1 -type f -name 'l12-cards-*.tar.gz' -print 2>/dev/null | sort)
legacy_exists=0
[[ -d "$legacy_root" ]] && legacy_exists=1

log "保留内容寻址卡图版本：${active_hash}"
log "待删除旧优化版本：${#obsolete_assets[@]} 个"
log "待删除旧 /cards 目录：${legacy_exists} 个"
log "待删除旧卡图上传包：${#legacy_archives[@]} 个"
printf '%s\n' "${obsolete_assets[@]}" "${legacy_archives[@]}" | sed '/^$/d'

[[ "$1" == "apply" ]] || exit 0
for target in "${obsolete_assets[@]}"; do
  [[ "$target" == "${asset_root}/"* && "$target" != "$active_target" ]] || fail "拒绝删除非预期优化目录：${target}"
  rm -rf -- "$target"
done
if [[ -d "$legacy_root" ]]; then
  [[ "$legacy_root" == "/opt/legion12-static/cards" ]] || fail "旧卡图根目录不匹配"
  rm -rf -- "$legacy_root"
fi
for target in "${legacy_archives[@]}"; do
  [[ "$target" == "${incoming_root}/l12-cards-"*.tar.gz ]] || fail "拒绝删除非预期上传包：${target}"
  rm -f -- "$target"
done
log "旧卡图清理完成；活动内容寻址版本保持不变"
