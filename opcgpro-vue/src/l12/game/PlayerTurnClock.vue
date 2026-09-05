<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { RankedClockView } from '../types'

const props = defineProps<{
  playerIndex: number
  side: 'my' | 'opponent'
  active: boolean
  rankedClock?: RankedClockView | null
}>()

const nowMs = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => { timer = window.setInterval(() => { nowMs.value = Date.now() }, 250) })
onBeforeUnmount(() => { if (timer !== null) window.clearInterval(timer) })

const clock = computed(() => {
  const snapshot = props.rankedClock
  const player = snapshot?.players.find(entry => entry.playerIndex === props.playerIndex)
  if (!snapshot || !player) return null
  const elapsed = Math.max(0, nowMs.value - snapshot.receivedAtMs)
  const ticking = player.connected && player.acting ? elapsed : 0
  return {
    total: Math.max(0, player.totalRemainingMs - ticking),
    operation: Math.max(0, player.operationRemainingMs - ticking),
    reconnect: player.reconnectRemainingMs == null ? null : Math.max(0, player.reconnectRemainingMs - elapsed),
    connected: player.connected,
  }
})

function formatClock(value: number) {
  const seconds = Math.max(0, Math.ceil(value / 1000))
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
}
</script>

<template>
  <section class="player-turn-clock" :class="[`side-${side}`, { active, disconnected: clock && !clock.connected }]"
    data-ui-contract="persistent-player-turn-clock" :data-player-index="playerIndex">
    <strong>{{ active ? '回合玩家' : '等待回合' }}</strong>
    <template v-if="clock">
      <span><small>总时</small><b>{{ formatClock(clock.total) }}</b></span>
      <span v-if="clock.connected"><small>本次</small><b>{{ formatClock(clock.operation) }}</b></span>
      <span v-else><small>重连</small><b>{{ formatClock(clock.reconnect ?? 0) }}</b></span>
    </template>
    <span v-else class="untimed"><small>计时</small><b>无时限</b></span>
  </section>
</template>

<style scoped>
.player-turn-clock{box-sizing:border-box;display:flex;width:286px;min-height:34px;align-items:center;justify-content:flex-end;gap:9px;padding:5px 9px;border:1px solid #505b5f;background:rgba(5,9,11,.94);box-shadow:0 7px 18px rgba(0,0,0,.72);color:#aeb6b7;pointer-events:none}
.player-turn-clock strong{margin-right:auto;padding:3px 7px;border:1px solid #4c5558;color:#8d9697;font-size:8px;letter-spacing:.12em;white-space:nowrap}
.player-turn-clock span{display:flex;min-width:68px;align-items:baseline;justify-content:flex-end;gap:5px}
.player-turn-clock small{color:#879092;font-size:7px;font-weight:900;white-space:nowrap}
.player-turn-clock b{color:#f2eee2;font:900 11px monospace;letter-spacing:.02em;white-space:nowrap}
.player-turn-clock.active{border-color:#d5b65f;box-shadow:0 0 12px rgba(213,182,95,.3),0 7px 18px rgba(0,0,0,.72)}
.player-turn-clock.active strong,.player-turn-clock.active b{border-color:#d5b65f;color:#f1d77e}
.player-turn-clock.side-opponent.active{border-color:#c9505a}.player-turn-clock.side-opponent.active strong,.player-turn-clock.side-opponent.active b{border-color:#c9505a;color:#f28e96}
.player-turn-clock.side-my.active{border-color:#53bdc5}.player-turn-clock.side-my.active strong,.player-turn-clock.side-my.active b{border-color:#53bdc5;color:#7adce3}
.player-turn-clock.disconnected{border-color:#9b3e49;background:rgba(38,10,14,.95)}.player-turn-clock.disconnected b{color:#f1959e}
.player-turn-clock .untimed{min-width:86px}
</style>
