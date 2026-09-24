<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { projectLog } from './logViewModel'
import type { ActionEvent, Card } from '../types'

const props = defineProps<{ events: ActionEvent[]; you: number; names: string[] }>()
const emit = defineEmits<{ focus: [card: Card] }>()
const list = ref<HTMLElement | null>(null)
const followLatest = ref(true)
const expandedCombats = ref(new Set<number>())
const visible = computed(() => projectLog(props.events, props.you, props.names))
const icons = { attack: '⚔', defense: '🛡', support: '✚', effect: '✦', disaster: '☄', dice: '⚀', 'hand-add': '✋', draw: '◇', play: '▶', info: '·', 'game-over': '★' } as const

function toggleCombat(sequence: number) {
  const next = new Set(expandedCombats.value)
  if (next.has(sequence)) next.delete(sequence)
  else next.add(sequence)
  expandedCombats.value = next
}
function onScroll() {
  const node = list.value
  if (node) followLatest.value = node.scrollHeight - node.scrollTop - node.clientHeight < 48
}
async function latest() {
  followLatest.value = true
  await nextTick()
  if (list.value) list.value.scrollTop = list.value.scrollHeight
}
watch(() => visible.value.at(-1)?.sequence, async () => { if (followLatest.value) await latest() }, { immediate: true })
</script>

<template>
  <div class="battle-event-log" data-ui-contract="perspective-battle-log">
    <div ref="list" class="event-list" @scroll="onScroll">
      <template v-for="row in visible" :key="`${row.kind}:${row.sequence}`">
        <h4 v-if="row.kind === 'turn'" class="turn-divider" :data-event-sequence="row.sequence">
          {{ row.round ? `第 ${row.round} 回合` : '回合开始' }} · {{ row.side }}回合
        </h4>
        <article v-else-if="row.kind === 'combat'" class="combat-summary" :data-event-sequence="row.sequence">
          <div class="combat-main">
            <span class="event-icon" aria-hidden="true">{{ icons.attack }}</span>
            <span class="combat-cards">
              <button class="log-card-link" @click="emit('focus', row.attacker)">〈{{ row.attacker.name }}〉</button>
              <b>{{ row.attackTroops }}</b><span> vs </span>
              <span v-if="row.defender === '主宰'">主宰</span>
              <button v-else class="log-card-link" @click="emit('focus', row.defender)">〈{{ row.defender.name }}〉</button>
              <b v-if="row.defendTroops != null">{{ row.defendTroops }}</b>
              <span> · {{ row.result }}</span><span v-if="row.damage != null" class="event-badge neg">−{{ row.damage }}点</span>
            </span>
            <button class="combat-toggle" :aria-expanded="expandedCombats.has(row.sequence)" @click="toggleCombat(row.sequence)">
              {{ expandedCombats.has(row.sequence) ? '收起' : '明细' }} {{ expandedCombats.has(row.sequence) ? '▴' : '▾' }}
            </button>
          </div>
          <div v-if="expandedCombats.has(row.sequence)" class="combat-detail">
            <p v-for="detail in row.detail" :key="detail.sequence" :class="['battle-event', `event-${detail.icon}`]">
              <span class="event-icon" aria-hidden="true">{{ icons[detail.icon] }}</span>
              <span class="event-message"><strong v-if="detail.actor" class="event-side">{{ detail.actor }} </strong><template v-for="(part, index) in detail.parts" :key="index"><button v-if="part.card" class="log-card-link" @click="emit('focus', part.card)">{{ part.text }}</button><span v-else>{{ part.text }}</span></template><span v-for="item in detail.badges" :key="item.value" :class="['event-badge', item.tone]">{{ item.value }}</span><span v-if="detail.effectText" class="event-effect">{{ detail.effectText }}</span></span>
            </p>
          </div>
        </article>
        <p v-else :class="['battle-event', `event-${row.icon}`]" :data-event-sequence="row.sequence">
          <span class="event-icon" aria-hidden="true">{{ icons[row.icon] }}</span>
          <span class="event-message"><strong v-if="row.actor" class="event-side">{{ row.actor }} </strong><template v-for="(part, index) in row.parts" :key="index"><button v-if="part.card" class="log-card-link" @click="emit('focus', part.card)">{{ part.text }}</button><span v-else>{{ part.text }}</span></template><span v-for="item in row.badges" :key="item.value" :class="['event-badge', item.tone]">{{ item.value }}</span><span v-if="row.effectText" class="event-effect">{{ row.effectText }}</span></span>
        </p>
      </template>
      <p v-if="!visible.length">等待对局开始</p>
    </div>
    <button v-if="!followLatest" class="jump-latest" @click="latest">回到最新记录 ↓</button>
  </div>
</template>

<style scoped>
.battle-event-log{display:flex;min-height:0;flex:1;flex-direction:column;position:relative}.event-list{display:flex;flex:1;min-height:0;flex-direction:column;gap:7px;overflow-y:auto;overflow-x:hidden;overscroll-behavior:contain;padding-right:5px}.battle-event-log .battle-event{display:grid;grid-template-columns:1.6em minmax(0,1fr);gap:6px;margin:0;padding:7px 0;border:0;border-bottom:1px solid #39434a;background:none;font-size:var(--l12-board-copy,13px);line-height:1.55}.event-icon{display:grid;width:1.6em;height:1.6em;place-items:center;color:#8addf3}.event-message{min-width:0;overflow-wrap:anywhere;color:#d7dfdf}.event-side{font-weight:500;color:#9aa8b1}.battle-event-log .log-card-link{display:inline;padding:0;border:0;border-bottom:1px solid currentColor;background:none;color:#87d9ed;font-size:inherit;font-weight:700;line-height:inherit;text-align:left}.event-badge{display:inline-block;margin-left:5px;padding:0 5px;border:1px solid #687882;background:#182328;color:#d9e0e2;line-height:1.45}.event-badge.pos{border-color:#4d8d67;color:#91deb0}.event-badge.neg{border-color:#9d5661;color:#f0a1ad}.event-effect{display:block;margin-top:5px;color:#b9c4c8;white-space:pre-wrap}.turn-divider{margin:9px 0 2px;padding:7px 0;color:#e7c760;text-align:center;font-size:var(--l12-board-copy,13px);border-block:1px solid #665829}.combat-summary{border:1px solid #485963;background:#111a1f}.combat-main{display:grid;grid-template-columns:1.6em minmax(0,1fr) auto;align-items:center;gap:6px;padding:8px;font-size:var(--l12-board-copy,13px);line-height:1.55}.combat-cards{min-width:0;overflow-wrap:anywhere;color:#d7dfdf}.combat-cards b{margin-inline:3px;color:#f0d889}.combat-toggle{align-self:stretch;min-width:52px;border:0;border-left:1px solid #43535c;background:transparent;color:#a9cbd5;font:inherit}.combat-detail{margin:0 8px 5px;padding-left:8px;border-left:2px solid #354750}.combat-detail .battle-event:last-child{border-bottom:0}.jump-latest{min-height:34px;border:1px solid #586873;background:#172630;color:#e5d29b;font-size:var(--l12-board-copy,13px)}
</style>
