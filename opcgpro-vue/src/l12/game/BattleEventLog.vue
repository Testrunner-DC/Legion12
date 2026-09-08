<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import type { ActionEvent, Card } from '../types'
const props = defineProps<{ events: ActionEvent[]; you: number; names: string[] }>()
const emit = defineEmits<{ focus: [card: Card] }>()
const list = ref<HTMLElement | null>(null)
const followLatest = ref(true)
const omitted = new Set(['phase', 'phase-detail', 'draw-skipped', 'prompt', 'priority-pass', 'stack-push', 'stack-deferred', 'stack-open', 'stack-resolve', 'end-turn', 'disaster-removed'])
const labels: Record<string, string> = { play: '出牌', attack: '进攻', combat: '战斗', defense: '抵挡', support: '支援', move: '位移', response: '响应', 'counter-set': '盖伏', effect: '效果', 'faction-effect': '阵营效果', 'effect-trigger': '触发', 'effect-response': '响应', 'effect-activation': '发动', 'effect-negated': '无效', 'prompt-resolved': '效果选择', 'initiative-choice': '先后手', mulligan: '调度', cost: '支付', damage: '伤害', heal: '恢复', leave: '离场', put: '登场', search: '检索', reveal: '公开', 'disaster-reveal': '公开', return: '返回', discard: '弃置', reorder: '排序', shuffle: '洗牌', dice: '掷骰', 'game-over': '胜负', 'attack-aborted': '进攻结束', 'effect-cancelled': '取消', 'effect-failed': '未生效' }
const visible = computed(() => [...props.events].filter(event => !omitted.has(event.type)).sort((a, b) => a.sequence - b.sequence))
function side(index?: number) { return index == null ? '' : index === props.you ? '我方' : '对方' }
function message(event: ActionEvent) {
  let text = event.text
  const name = event.playerIndex == null ? '' : props.names[event.playerIndex]
  if (name && text.startsWith(name)) text = text.slice(name.length).replace(/^\s*[：:]?\s*/, '')
  return text.replace(/对手/g, '对方')
}
function parts(event: ActionEvent): Array<{ text: string; card?: Card }> {
  const cards = [...new Map((event.cards ?? []).filter(card => !card.hidden).map(card => [card.name, card])).values()]
  let text = message(event)
  if (!cards.length) return [{ text }]
  const names = cards.map(card => card.name).filter(Boolean).sort((a, b) => b.length - a.length)
  if (!names.length) return [{ text }]
  const expression = new RegExp(`(${names.map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`, 'g')
  const byName = new Map(cards.map(card => [card.name, card]))
  const result: Array<{ text: string; card?: Card }> = text.split(expression).filter(Boolean).map(value => ({ text: value, card: byName.get(value) }))
  for (const card of cards) if (!text.includes(card.name)) result.push({ text: ' · ' }, { text: card.name, card })
  return result
}
function heading(event: ActionEvent) {
  const round = event.text.match(/第\s*(\d+)\s*回合/)?.[1]
  return `${round ? `第 ${round} 回合` : '回合开始'} · ${side(event.playerIndex)}回合`
}
function onScroll() { const node = list.value; if (node) followLatest.value = node.scrollHeight - node.scrollTop - node.clientHeight < 48 }
async function latest() { followLatest.value = true; await nextTick(); if (list.value) list.value.scrollTop = list.value.scrollHeight }
watch(() => visible.value.at(-1)?.sequence, async () => { if (followLatest.value) await latest() }, { immediate: true })
</script>
<template>
  <div class="battle-event-log" data-ui-contract="perspective-battle-log">
    <div ref="list" class="event-list" @scroll="onScroll">
      <template v-for="event in visible" :key="event.sequence">
        <h4 v-if="event.type === 'turn-start'" class="turn-divider">{{ heading(event) }}</h4>
        <p v-else :class="['battle-event', `event-${event.type}`]">
          <b class="event-tag">{{ labels[event.type] ?? '记录' }}</b>
          <span class="event-message"><strong v-if="side(event.playerIndex)" class="event-side">{{ side(event.playerIndex) }} </strong><template v-for="(part, index) in parts(event)" :key="index"><button v-if="part.card" class="log-card-link" @click="emit('focus', part.card)">{{ part.card.cardId }} {{ part.text }}</button><span v-else>{{ part.text }}</span></template><span v-if="event.effectText && !event.text.includes(event.effectText)" class="event-effect">{{ event.effectText }}</span></span>
        </p>
      </template>
      <p v-if="!visible.length">等待对局开始</p>
    </div>
    <button v-if="!followLatest" class="jump-latest" @click="latest">回到最新记录 ↓</button>
  </div>
</template>
<style scoped>
.battle-event-log{display:flex;min-height:0;flex:1;flex-direction:column;position:relative}.event-list{display:flex;flex:1;min-height:0;flex-direction:column;gap:10px;overflow-y:auto;overscroll-behavior:contain;padding-right:5px}.battle-event-log .battle-event{display:grid;grid-template-columns:auto minmax(0,1fr);gap:8px;margin:0;padding:8px 0;border:0;border-bottom:1px solid #39434a;background:none;font-size:var(--l12-board-copy,13px);line-height:1.65}.event-tag{align-self:start;white-space:nowrap;padding:0 4px;border:1px solid #598f9e;color:#8addf3;line-height:1.45;font-size:inherit}.event-message{overflow-wrap:anywhere;color:#d7dfdf}.event-side{font-weight:500;color:#9aa8b1}.battle-event-log .log-card-link{display:inline;padding:0;border:0;border-bottom:1px solid currentColor;background:none;color:#87d9ed;font-size:inherit;font-weight:700;line-height:inherit;text-align:left}.event-effect{display:block;margin-top:5px;color:#b9c4c8;white-space:pre-wrap}.turn-divider{margin:12px 0 4px;padding:8px 0;color:#e7c760;text-align:center;font-size:var(--l12-board-copy,13px);border-block:1px solid #665829}.event-cost .event-tag,.event-reveal .event-tag{border-color:#b39e4e;color:#e9cb60}.event-prompt-resolved .event-tag{color:#dba4ed;border-color:#9562a6}.event-attack .event-tag,.event-damage .event-tag{color:#ed93a3;border-color:#ad5265}.jump-latest{min-height:34px;border:1px solid #586873;background:#172630;color:#e5d29b;font-size:var(--l12-board-copy,13px)}
</style>
