<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import type { ActionEvent, Card } from '../types'
const props = defineProps<{ events: ActionEvent[]; you: number; names: string[] }>()
const emit = defineEmits<{ focus: [card: Card] }>()
const list = ref<HTMLElement | null>(null)
const followLatest = ref(true)
const omitted = new Set([
  'phase', 'phase-detail', 'draw-skipped', 'prompt', 'priority-pass', 'stack-push', 'stack-deferred', 'stack-open', 'stack-resolve', 'end-turn',
  'disaster-removed', 'match-created', 'initiative', 'disaster-selected', 'disaster-deck-ready', 'mulligan-start', 'shuffle',
  'combat-stage', 'combat-resume', 'attack-ended', 'effect-order', 'effect-skip', 'effect-noop', 'ability-rejected', 'effect-rejected',
  'effect-announced', 'defense-invalid', 'authority-event', 'promotion-stack-leave', 'replacement-transaction', 'derived-vanished',
])
const labels: Record<string, string> = { play: '出牌', attack: '进攻', combat: '战斗', defense: '抵挡', support: '支援', move: '位移', response: '响应', 'counter-set': '盖伏', effect: '效果', 'faction-effect': '阵营效果', 'effect-trigger': '触发', 'effect-response': '响应', 'effect-activation': '发动', 'effect-result': '结算', 'effect-negated': '无效', 'prompt-resolved': '效果选择', 'initiative-choice': '先后手', mulligan: '调度', 'disaster-banned': '天灾禁选', disaster: '本局天灾', 'disaster-active': '天灾效果', 'disaster-value': '天灾数值', cost: '支付', draw: '抽牌', damage: '伤害', heal: '恢复', leave: '离场', grave: '墓地', put: '登场', search: '检索', reveal: '公开', 'disaster-reveal': '本局天灾', return: '返回', discard: '弃置', reorder: '排序', dice: '掷骰', 'game-over': '胜负', 'attack-aborted': '进攻结束', 'effect-cancelled': '取消', 'effect-failed': '未生效' }
const outcomeChange = /(?:增加|减少|追加|抽取|弃置|失去|恢复|位移)\s*(-?\d+)\s*(?:张|点|格|士气|神力)?/g
const meaningfulFailure = /(?:未|没有|失败|无效|取消|中止|但|并|随后|然后|，|；)/
const compoundOutcome = /(?:未|并|随后|然后|先|再|但|转为|伤害|恢复|增加|减少|弃置|失去|位移|登场|离场|返回|公开|检索|获得|支付|消耗|重置|横置|活跃|休整|[，；。！？])/
function onlyZeroChange(event: ActionEvent) {
  if (['effect-failed', 'effect-cancelled', 'attack-aborted'].includes(event.type) || meaningfulFailure.test(event.text)) return false
  const changes = [...event.text.matchAll(outcomeChange)]
  return changes.length > 0 && changes.every(change => Number(change[1]) === 0)
}
const visible = computed(() => [...props.events]
  .filter(event => !omitted.has(event.type) && !onlyZeroChange(event))
  .sort((a, b) => a.sequence - b.sequence))
function escapeRegExp(value: string) { return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') }
function normalizeTypography(value: string) {
  return value.replace(/\s+/g, ' ').trim()
    .replace(/\s*([，。；：！？])\s*/g, '$1')
    .replace(/([〈《（])\s+/g, '$1').replace(/\s+([〉》）])/g, '$1')
    .replace(/\s*(-?\d+)\s*(张|点|格|士气|神力)/g, '$1$2')
}
function publicCards(event: ActionEvent) {
  return [...new Map((event.cards ?? []).filter(card => !card.hidden && Boolean(card.name)).map(card => [card.name, card])).values()]
}
function redactHiddenCardNames(event: ActionEvent, value: string) {
  const hiddenNames = [...new Set((event.cards ?? []).filter(card => card.hidden && Boolean(card.name)).map(card => card.name))]
    .sort((a, b) => b.length - a.length)
  return hiddenNames.reduce((text, name) => text.replace(new RegExp(`〈?${escapeRegExp(name)}〉?`, 'g'), '隐藏卡牌'), value)
}
function sourceNameFor(text: string, event: ActionEvent, verbIndex: number) {
  const bracketed = text.slice(0, verbIndex).match(/^〈([^〉]+)〉/i)?.[1]
  if (bracketed) return bracketed.replace(/的效果$/, '')
  const colonSource = text.slice(0, verbIndex).match(/^([^：:]{1,32})[：:]$/)?.[1]
  if (colonSource) return colonSource.replace(/[〈〉]/g, '').trim()
  const source = publicCards(event).find(card => text.startsWith(card.name) && text.indexOf(card.name) < verbIndex)
  if (source) return source.name
  const beforeActor = text.slice(0, verbIndex).match(/^(.{1,32}?)(?:的效果)?(?:使|让)/)?.[1]
  return beforeActor?.replace(/[〈〉]/g, '').trim() ?? ''
}
function compactDraw(event: ActionEvent, text: string) {
  const match = /抽取\s*(\d+)\s*张牌\s*[。.]?$/.exec(text)
  if (!match || match.index === undefined || compoundOutcome.test(text.slice(0, match.index))) return text
  const source = sourceNameFor(text, event, match.index)
  return `${source ? `${source}：` : ''}抽取${match[1]}张牌。`
}
function compactMove(event: ActionEvent, text: string) {
  const match = /位移\s*(\d+)\s*格\s*[。.]?$/.exec(text)
  if (!match || match.index === undefined || compoundOutcome.test(text.slice(0, match.index))) return text
  const candidates = publicCards(event)
    .map(card => ({ card, index: text.lastIndexOf(card.name, match.index) }))
    .filter(candidate => candidate.index >= 0)
    .sort((a, b) => b.index - a.index)
  const target = candidates[0]
  if (!target) return text
  const source = sourceNameFor(text, event, target.index)
  return `${source ? `${source}：` : ''}〈${target.card.name}〉位移${match[1]}格。`
}
function side(index?: number) { return index == null ? '' : index === props.you ? '我方' : '对方' }
function message(event: ActionEvent) {
  let text = redactHiddenCardNames(event, normalizeTypography(event.text))
  const name = event.playerIndex == null ? '' : props.names[event.playerIndex]
  if (name && text.startsWith(name)) text = text.slice(name.length).replace(/^\s*[：:]?\s*/, '')
  text = text.replace(/对手/g, '对方')
  if (event.type === 'draw') return compactDraw(event, text)
  if (event.type === 'move' || event.type === 'faction-effect') return compactMove(event, text)
  return text
}
function parts(event: ActionEvent): Array<{ text: string; card?: Card }> {
  const cards = publicCards(event)
  let text = message(event)
  if (!cards.length) return [{ text }]
  const names = cards.map(card => card.name).filter(Boolean).sort((a, b) => b.length - a.length)
  if (!names.length) return [{ text }]
  const expression = new RegExp(`(${names.map(escapeRegExp).join('|')})`, 'g')
  const byName = new Map(cards.map(card => [card.name, card]))
  return text.split(expression).filter(Boolean).map(value => ({ text: value, card: byName.get(value) }))
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
        <p v-else :class="['battle-event', `event-${event.type}`]" :data-event-sequence="event.sequence">
          <b class="event-tag">{{ labels[event.type] ?? '记录' }}</b>
          <span class="event-message"><strong v-if="side(event.playerIndex)" class="event-side">{{ side(event.playerIndex) }} </strong><template v-for="(part, index) in parts(event)" :key="index"><button v-if="part.card" class="log-card-link" @click="emit('focus', part.card)">{{ part.text }}</button><span v-else>{{ part.text }}</span></template><span v-if="event.effectText && !event.text.includes(event.effectText)" class="event-effect">{{ event.effectText }}</span></span>
        </p>
      </template>
      <p v-if="!visible.length">等待对局开始</p>
    </div>
    <button v-if="!followLatest" class="jump-latest" @click="latest">回到最新记录 ↓</button>
  </div>
</template>
<style scoped>
.battle-event-log{display:flex;min-height:0;flex:1;flex-direction:column;position:relative}.event-list{display:flex;flex:1;min-height:0;flex-direction:column;gap:10px;overflow-y:auto;overscroll-behavior:contain;padding-right:5px}.battle-event-log .battle-event{display:grid;grid-template-columns:auto minmax(0,1fr);gap:8px;margin:0;padding:8px 0;border:0;border-bottom:1px solid #39434a;background:none;font-size:var(--l12-board-copy,13px);line-height:1.65}.event-tag{align-self:start;white-space:nowrap;padding:0 4px;border:1px solid #598f9e;color:#8addf3;line-height:1.45;font-size:inherit}.event-message{overflow-wrap:anywhere;color:#d7dfdf}.event-side{font-weight:500;color:#9aa8b1}.battle-event-log .log-card-link{display:inline;padding:0;border:0;border-bottom:1px solid currentColor;background:none;color:#87d9ed;font-size:inherit;font-weight:700;line-height:inherit;text-align:left}.event-effect{display:block;margin-top:5px;color:#b9c4c8;white-space:pre-wrap}.turn-divider{margin:12px 0 4px;padding:8px 0;color:#e7c760;text-align:center;font-size:var(--l12-board-copy,13px);border-block:1px solid #665829}.event-cost .event-tag,.event-reveal .event-tag{border-color:#b39e4e;color:#e9cb60}.event-prompt-resolved .event-tag{color:#dba4ed;border-color:#9562a6}.event-attack .event-tag,.event-damage .event-tag{color:#ed93a3;border-color:#ad5265}.jump-latest{min-height:34px;border:1px solid #586873;background:#172630;color:#e5d29b;font-size:var(--l12-board-copy,13px)}
.event-tag{box-sizing:border-box;width:2.8em;padding-inline:3px;line-height:1.35;text-align:center;white-space:normal;overflow-wrap:anywhere}
</style>
