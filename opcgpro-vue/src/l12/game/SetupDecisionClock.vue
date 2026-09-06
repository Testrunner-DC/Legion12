<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { RankedClockView } from '../types'

const props = defineProps<{
  playerIndex: number | null
  rankedClock?: RankedClockView | null
  roleLabel?: string
}>()

const nowMs = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => { timer = window.setInterval(() => { nowMs.value = Date.now() }, 200) })
onBeforeUnmount(() => { if (timer !== null) window.clearInterval(timer) })

const clock = computed(() => {
  const snapshot = props.rankedClock
  if (!snapshot || snapshot.operationLimitMs !== 60_000 || props.playerIndex == null) return null
  const player = snapshot.players.find(entry => entry.playerIndex === props.playerIndex)
  if (!player?.acting) return null
  const elapsed = Math.max(0, nowMs.value - snapshot.receivedAtMs)
  return {
    remaining: Math.max(0, player.operationRemainingMs - elapsed),
    connected: player.connected,
  }
})

function formatClock(value: number) {
  const seconds = Math.max(0, Math.ceil(value / 1000))
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
}
</script>

<template>
  <div v-if="clock" class="setup-decision-clock" :class="{ disconnected: !clock.connected }"
    data-ui-contract="ranked-setup-sixty-second-clock" role="timer" aria-live="off">
    <span>{{ roleLabel || '准备步骤' }}</span>
    <strong>{{ formatClock(clock.remaining) }}</strong>
    <em v-if="!clock.connected">断线仍继续</em>
  </div>
</template>

<style scoped>
.setup-decision-clock{display:flex;min-height:34px;align-items:center;justify-content:center;gap:10px;margin:8px 0;padding:7px 12px;border:1px solid #d4b85f;background:#17150e;color:#f0df9d;font-size:max(14px,var(--l12-board-readable,14px));font-style:normal;font-weight:900}
.setup-decision-clock span{font-size:max(14px,var(--l12-board-readable,14px))}.setup-decision-clock strong{color:#fff;font:900 max(18px,var(--l12-board-readable,14px)) monospace;letter-spacing:.04em}.setup-decision-clock em{color:#ef9299;font-size:max(14px,var(--l12-board-readable,14px));font-style:normal}.setup-decision-clock.disconnected{border-color:#a8424c;background:#241014}
</style>
