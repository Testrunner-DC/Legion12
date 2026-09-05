<script setup lang="ts">
import { computed } from 'vue'
import type { Card, CardStatusEffect, CardStatusIconKind } from './types'
import { isHorizontalCardType } from './cardPresentation'
import { roundCardUrl } from './specialAssets'
import CardImage from './CardImage.vue'
const props = defineProps<{ card: Card; selected?: boolean; compact?: boolean }>()
defineEmits<{ select: []; focusCard: [card: Card] }>()
const displayCost = computed(() => props.card.playCost ?? props.card.currentCost ?? props.card.cost)
const costState = computed(() => displayCost.value < props.card.cost ? 'discounted' : displayCost.value > props.card.cost ? 'increased' : '')
const isBattlefieldLegion = computed(() => props.card.cardType === 'legion' || props.card.isMasterLegion === true || props.card.cardId === 'S01-0417' && props.card.troops > 0)
const displayBaseTroops = computed(() => props.card.displayBaseTroops ?? props.card.baseTroops)
const attachedGroups = computed(() => {
  const groups = new Map<string, { card: Card; count: number }>()
  for (const card of props.card.attachedCards ?? []) {
    const current = groups.get(card.cardId)
    if (current) current.count += 1
    else groups.set(card.cardId, { card, count: 1 })
  }
  return [...groups.values()]
})
const showFace = computed(() => !props.card.hidden || props.card.identityKnown === true)
type StatusIndicator = { kind: CardStatusIconKind; glyph: string; label: string }
const statusGlyphs: Record<CardStatusIconKind, string> = {
  lock: '🔒',
  'power-up': '🔥',
  'power-down': '↓',
  disabled: '×',
  shield: '🛡',
  'discard-end': '🗑',
  'extra-attack': '✊',
}
const defaultStatusLabels: Record<CardStatusIconKind, string> = {
  lock: '无法重置',
  'power-up': '临时兵力增加',
  'power-down': '临时兵力降低',
  disabled: '当前存在无法进攻、支援或响应发动的限制',
  shield: '免死、致命保护或暂不可被进攻',
  'discard-end': '回合结束时弃置',
  'extra-attack': '获得额外进攻对象权限或下次击杀后转为活跃',
}
const statusAliases: Record<string, CardStatusIconKind> = {
  lock: 'lock', 'cannot-reset': 'lock', 'reset-locked': 'lock',
  'power-up': 'power-up', 'troops-up': 'power-up', 'temporary-power-up': 'power-up',
  'power-down': 'power-down', 'troops-down': 'power-down', 'temporary-power-down': 'power-down',
  disabled: 'disabled', 'cannot-attack': 'disabled', 'cannot-support': 'disabled', 'cannot-respond': 'disabled', 'cannot-activate': 'disabled',
  shield: 'shield', immortal: 'shield', 'lethal-protection': 'shield', 'cannot-be-attacked': 'shield',
  'discard-end': 'discard-end', 'discard-at-turn-end': 'discard-end',
  'extra-attack': 'extra-attack', 'extra-attack-target': 'extra-attack', 'next-kill-ready': 'extra-attack',
}
function normalizedStatusKind(kind: string) { return statusAliases[kind.trim().toLowerCase()] }
function statusLabel(effect: CardStatusEffect, kind: CardStatusIconKind) {
  const label = effect.label?.trim() || defaultStatusLabels[kind]
  return effect.source?.trim() ? `${label}（来源：${effect.source.trim()}）` : label
}
const statusIndicators = computed<StatusIndicator[]>(() => {
  const result: StatusIndicator[] = []
  const seen = new Set<string>()
  const append = (kind: CardStatusIconKind | undefined, label?: string) => {
    if (!kind) return
    const resolvedLabel = label?.trim() || defaultStatusLabels[kind]
    const key = `${kind}\n${resolvedLabel}`
    if (seen.has(key)) return
    seen.add(key)
    result.push({ kind, glyph: statusGlyphs[kind], label: resolvedLabel })
  }
  for (const effect of props.card.statusEffects ?? []) {
    const kind = normalizedStatusKind(effect.kind)
    append(kind, kind ? statusLabel(effect, kind) : undefined)
  }
  for (const icon of props.card.statusIcons ?? []) {
    const kind = normalizedStatusKind(icon)
    if (kind && !result.some(entry => entry.kind === kind)) append(kind)
  }
  if (result.length || props.card.statusEffects?.length || props.card.statusIcons?.length) return result

  // Compatibility for snapshots created before structured statusEffects existed.
  const positiveTroops = (props.card.timedModifiers ?? []).filter(modifier => modifier.troopsDelta > 0)
  const negativeTroops = (props.card.timedModifiers ?? []).filter(modifier => modifier.troopsDelta < 0)
  if (positiveTroops.length) append('power-up', `临时兵力增加：${positiveTroops.map(modifier => modifier.source).filter(Boolean).join('、') || '效果修正'}`)
  if (negativeTroops.length) append('power-down', `临时兵力降低：${negativeTroops.map(modifier => modifier.source).filter(Boolean).join('、') || '效果修正'}`)
  if (props.card.cannotAttack) append('disabled', '当前无法进攻')
  if (props.card.cannotSupport) append('disabled', '当前无法支援')
  if ((props.card.immortalUses ?? 0) > 0 || (props.card.immortalUntilTurn ?? 0) > 0)
    append('shield', '当前具有免死或致命伤害保护')
  if ((props.card.canAttackBackAndMasterUntilTurn ?? 0) > 0) append('extra-attack', '当前获得额外进攻对象权限')
  return result
})
const disabledKeywords = computed(() => (props.card.statusEffects ?? [])
  .filter(effect => effect.kind.trim().toLowerCase() === 'keyword-disabled')
  .map(effect => ({
    name: effect.label?.trim() || '关键词',
    title: `${effect.label?.trim() || '关键词'}已无效${effect.source?.trim() ? `（来源：${effect.source.trim()}）` : ''}`,
  })))
</script>

<template>
  <button class="card-tile" :data-card-instance-id="card.instanceId" :class="[{ selected, tapped: card.tapped, compact, 'horizontal-card': isHorizontalCardType(card.cardType), 'has-status-effects': statusIndicators.length }, `type-${card.cardType}`]" @click="$emit('select')">
    <CardImage v-if="showFace" :card-id="card.cardId" :legacy-url="card.imageUrl" :alt="card.name" intent="board" eager />
    <img v-else class="covered-card-back" src="/assets/l12/card-back-official.png" alt="盖伏卡牌" />
    <span v-if="showFace && card.hasPrintedCost !== false" class="card-cost" :class="costState" :title="displayCost === card.cost ? `印刷费用 ${card.cost}` : `当前费用 ${displayCost}；印刷费用 ${card.cost}`">{{ displayCost }}</span>
    <span v-if="showFace && statusIndicators.length" class="card-status-icons" aria-label="当前结构化状态">
      <i v-for="status in statusIndicators" :key="`${status.kind}:${status.label}`" class="card-status-icon" :class="`status-${status.kind}`"
        role="img" :aria-label="status.label" :title="status.label">{{ status.glyph }}</i>
    </span>
    <span v-if="showFace && (card.activeKeywords?.length || disabledKeywords.length)" class="card-keyword-stack" aria-label="当前关键词状态">
      <b v-for="keyword in card.activeKeywords" :key="keyword" class="card-keyword">{{ keyword }}</b>
      <b v-for="keyword in disabledKeywords" :key="`disabled:${keyword.name}`" class="card-keyword disabled-keyword"
        data-ui-contract="disabled-keyword-red-x" :aria-label="keyword.title" :title="keyword.title">
        <span>{{ keyword.name }}</span><i aria-hidden="true">×</i>
      </b>
    </span>
    <span v-if="showFace" class="card-name">{{ card.name }}</span>
    <span v-if="showFace && isBattlefieldLegion" class="card-power"
      :class="{ boosted: card.troops > displayBaseTroops, weakened: card.troops < displayBaseTroops }"
      :title="card.troops === displayBaseTroops ? `当前兵力 ${card.troops}` : `当前兵力 ${card.troops}；比较基准 ${displayBaseTroops}`">{{ card.troops }}</span>
    <span v-if="showFace && card.disasterLevel" class="card-disaster">{{ card.disasterLevel }}</span>
    <span v-if="showFace && attachedGroups.length" class="attached-card-orbs" aria-label="叠放卡牌">
      <span v-for="group in attachedGroups" :key="group.card.cardId" class="attached-card-orb" role="button" tabindex="0"
        :title="`${group.card.name}${group.count > 1 ? ` ×${group.count}` : ''}`"
        @mouseenter.stop="$emit('focusCard', group.card)" @focus.stop="$emit('focusCard', group.card)"
        @click.stop="$emit('focusCard', group.card)" @keyup.enter.stop="$emit('focusCard', group.card)">
        <img v-if="roundCardUrl(group.card.cardId, group.card.imageUrl)" :src="roundCardUrl(group.card.cardId, group.card.imageUrl)" :alt="group.card.name" />
        <i v-else>{{ group.card.name.slice(0, 1) }}</i>
        <b v-if="group.count > 1">{{ group.count }}</b>
      </span>
    </span>
  </button>
</template>

<style scoped>
.covered-card-back{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
.card-status-icons{position:absolute;z-index:9;left:3px;right:3px;top:25px;display:flex;height:17px;align-items:center;gap:2px;pointer-events:auto}.card-status-icon{display:grid;width:15px;min-width:11px;height:15px;flex:0 1 15px;place-items:center;overflow:hidden;border:1px solid rgba(255,255,255,.82);border-radius:2px;background:rgba(8,11,12,.94);box-shadow:0 1px 4px rgba(0,0,0,.78);color:#fff;font-family:"Segoe UI Symbol","Microsoft YaHei",sans-serif;font-size:9px;font-style:normal;font-weight:900;line-height:1}.card-status-icon.status-power-up{border-color:#ef9a48;color:#ffbd70}.card-status-icon.status-power-down{border-color:#749fcc;color:#a9d3ff}.card-status-icon.status-disabled{border-color:#da6269;color:#ff8d93}.card-status-icon.status-shield{border-color:#75c79c;color:#a4e7bd}.card-status-icon.status-discard-end{border-color:#a184bd;color:#d9b9ee}.card-status-icon.status-extra-attack{border-color:#e1b759;color:#ffe094}.card-keyword-stack{position:absolute;z-index:8;left:3px;top:25px;display:flex;max-width:calc(100% - 6px);flex-direction:column;align-items:flex-start;gap:2px;pointer-events:none}.card-tile.has-status-effects .card-keyword-stack{top:45px}.card-keyword{display:block;max-width:100%;padding:2px 5px;overflow:hidden;border:1px solid rgba(255,255,255,.86);border-radius:2px;background:rgba(17,24,24,.94);box-shadow:0 1px 4px rgba(0,0,0,.72);color:#fff;font-family:"Microsoft YaHei",sans-serif;font-size:8px;font-weight:900;line-height:1.15;text-overflow:ellipsis;white-space:nowrap}.card-tile.compact .card-status-icons{top:21px;height:15px}.card-tile.compact .card-status-icon{height:13px;font-size:8px}.card-tile.compact .card-keyword-stack{top:21px;gap:1px}.card-tile.compact.has-status-effects .card-keyword-stack{top:38px}.card-tile.compact .card-keyword{padding:1px 3px;font-size:7px}
.card-cost.discounted{background:#174b31!important;color:#fff!important;border-color:#50b47d!important}.card-cost.increased{background:#651d28!important;color:#fff!important;border-color:#c85a67!important}
.disabled-keyword{position:relative;border-color:#a6323a;background:rgba(38,12,15,.95);color:#c6b8b8}.disabled-keyword span{opacity:.72}.disabled-keyword i{position:absolute;inset:50% auto auto 50%;color:#ff4b58;font-size:22px;font-style:normal;font-weight:1000;line-height:1;text-shadow:0 0 5px #250005;transform:translate(-50%,-52%)}
.attached-card-orbs{position:absolute;z-index:9;right:3px;bottom:27px;display:flex;max-width:calc(100% - 6px);flex-direction:row-reverse;align-items:center;gap:1px;pointer-events:auto}.attached-card-orb{position:relative;display:grid;width:22px;height:22px;min-width:22px;place-items:center;padding:0;overflow:visible;border:1px solid #d8d2bd;border-radius:50%;background:#090b0c;box-shadow:0 2px 7px #000;cursor:pointer}.attached-card-orb img{position:static!important;inset:auto!important;width:100%!important;height:100%!important;border-radius:50%;background:#090b0c;object-fit:cover;object-position:center 14%;transform:scale(1.08)}.attached-card-orb i{color:#eee;font-size:8px;font-style:normal;font-weight:900}.attached-card-orb b{position:absolute;z-index:2;left:50%;top:-9px;display:grid;min-width:15px;height:15px;place-items:center;padding:0 3px;border:1px solid #f0e7c8;border-radius:8px;background:#111;color:#fff;font-size:8px;line-height:1;transform:translateX(-50%)}.attached-card-orb:hover,.attached-card-orb:focus-visible{border-color:#72d9df;box-shadow:0 0 9px rgba(112,217,223,.8);outline:none}
.attached-card-orb{border-color:#f0f2ef;box-shadow:0 0 0 2px rgba(194,199,196,.72),0 2px 7px #000}.attached-card-orbs{gap:3px}
</style>
