<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { GameState, RankedClockView } from '../types'

const props = defineProps<{
  playerIndex: number | null
  phase: GameState['phase']
  rankedClock?: RankedClockView | null
  roleLabel?: string
  showAllActing?: boolean
  playerLabels?: Record<number, string>
}>()

const nowMs = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => { timer = window.setInterval(() => { nowMs.value = Date.now() }, 200) })
onBeforeUnmount(() => { if (timer !== null) window.clearInterval(timer) })

const clock = computed(() => {
  const snapshot = props.rankedClock
  const preparation = props.phase === 'Initiative' || ['DisasterPreparation', 'Mulligan'].includes(props.phase)
  if (!snapshot || !preparation || snapshot.operationLimitMs <= 0) return null
  const player = snapshot.players.find(entry => entry.playerIndex === props.playerIndex)
  const actors = props.showAllActing ? snapshot.players.filter(entry => entry.acting) : player?.acting ? [player] : []
  if (!actors.length) return null
  const elapsed = snapshot.paused ? 0 : Math.max(0, nowMs.value - snapshot.receivedAtMs)
  return {
    paused: Boolean(snapshot.paused),
    entries: actors.map(player => ({ playerIndex: player.playerIndex,
      remaining: Math.max(0, player.operationRemainingMs - elapsed), connected: player.connected })),
  }
})

function formatClock(value: number) {
  const seconds = Math.max(0, Math.ceil(value / 1000))
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
}
</script>

<template>
  <div v-if="clock" class="setup-decision-clock" :class="{ disconnected: clock.entries.some(entry => !entry.connected), paused: clock.paused }"
    data-ui-contract="ranked-setup-decision-clock" role="timer" aria-live="off">
    <div v-for="entry in clock.entries" :key="entry.playerIndex" class="setup-clock-entry"
      :class="{ urgent: !clock.paused && entry.remaining > 0 && entry.remaining <= 10000 }" :data-clock-player="entry.playerIndex">
      <span>{{ playerLabels?.[entry.playerIndex] || roleLabel || '准备步骤' }} · 本次剩余</span>
      <strong>{{ formatClock(entry.remaining) }}</strong>
      <em v-if="!clock.paused && !entry.connected">断线时继续计时</em>
      <em v-else-if="!clock.paused && entry.remaining === 0">等待服务器处理</em>
    </div>
    <em v-if="clock.paused">计时已暂停</em>
  </div>
</template>

<style scoped>
.setup-decision-clock{display:flex;min-height:34px;align-items:center;justify-content:center;gap:10px;margin:8px 0;padding:7px 12px;border:1px solid #d4b85f;background:#17150e;color:#f0df9d;font-size:var(--l12-board-copy,13px);font-style:normal;font-weight:900}
.setup-decision-clock span{font-size:var(--l12-board-copy,13px)}.setup-decision-clock strong{color:#fff;font:900 max(18px,var(--l12-board-copy,13px)) monospace;letter-spacing:.04em}.setup-decision-clock em{color:#ef9299;font-size:var(--l12-board-copy,13px);font-style:normal}.setup-decision-clock.disconnected{border-color:#a8424c;background:#241014}
</style>
<style scoped>
.setup-decision-clock{flex-wrap:wrap;gap:5px 16px;max-width:100%;box-sizing:border-box}
.setup-clock-entry{display:flex;align-items:center;justify-content:center;flex-wrap:wrap;gap:6px 10px;min-width:0}
.setup-clock-entry span{overflow-wrap:anywhere}.setup-clock-entry.urgent strong{color:#ffb1a7}.setup-decision-clock.paused{border-color:#71848c;background:#121e24}.setup-decision-clock.paused>em{color:#b9c7ce}
</style>
