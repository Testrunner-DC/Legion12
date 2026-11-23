<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { GameState, RankedClockView } from '../types'

const props = defineProps<{
  playerIndex: number
  phase: GameState['phase']
  rankedClock?: RankedClockView | null
}>()

const nowMs = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => { timer = window.setInterval(() => { nowMs.value = Date.now() }, 1000) })
onBeforeUnmount(() => { if (timer !== null) window.clearInterval(timer) })

function formatClock(value: number) {
  const seconds = Math.max(0, Math.ceil(value / 1000))
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
}

// 只读服务端已下发的计时快照，不新造截止时间。
const notice = computed(() => {
  const snapshot = props.rankedClock
  const player = snapshot?.players.find(entry => entry.playerIndex === props.playerIndex)
  if (!snapshot || !player || !player.acting) return null
  const elapsed = Math.max(0, nowMs.value - snapshot.receivedAtMs)
  const preparation = ['Initiative', 'DisasterPreparation', 'Mulligan'].includes(props.phase)
  const timedPreparation = preparation && snapshot.operationLimitMs > 0
  const operationTicking = player.acting && (timedPreparation || (!preparation && player.connected)) ? elapsed : 0
  const operation = Math.max(0, player.operationRemainingMs - operationTicking)
  const reconnect = player.reconnectRemainingMs == null ? null : Math.max(0, player.reconnectRemainingMs - elapsed)
  if (!player.connected) {
    return reconnect !== null && reconnect > 0
      ? { tone: 'warn', text: `连接中断，本次行动仍在计时（重连剩余 ${formatClock(reconnect)}）` }
      : { tone: 'danger', text: '连接中断且重连时间已用尽，本次行动将由服务器代为处理' }
  }
  if (operation <= 0) return { tone: 'danger', text: '本次行动已超时，服务器将代为处理' }
  return null
})
</script>

<template>
  <p v-if="notice" class="operation-deadline-notice" :class="`tone-${notice.tone}`"
    data-ui-contract="operation-deadline-notice" role="status">{{ notice.text }}</p>
</template>

<style scoped>
.operation-deadline-notice{box-sizing:border-box;margin:0 0 6px;padding:6px 10px;border:1px solid #8a6b25;border-radius:6px;background:rgba(48,34,10,.94);color:#f1d77e;font-size:var(--l12-board-copy,13px);font-weight:900;letter-spacing:.02em;text-align:center}
.operation-deadline-notice.tone-danger{border-color:#9b3e49;background:rgba(38,10,14,.95);color:#f1959e}
</style>
