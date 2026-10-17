<script setup lang="ts">
import { ref } from 'vue'
import GmPanel from './GmPanel.vue'
import { l12State } from '../net'
import { tournamentApi } from '../platform'
import type { GameState } from '../types'

defineProps<{ game: GameState }>()
defineEmits<{ openChange: [open: boolean] }>()
const reason = ref('')
const minutes = ref(5)
const busy = ref(false)
const notice = ref('')
async function control(kind: 'pause' | 'extend') {
  const room = l12State.room
  if (busy.value || !l12State.refereeControlsEnabled || !room?.tournamentId || !room.tournamentMatchId) return
  const explanation = reason.value.trim()
  if (!explanation || explanation.length > 500) { notice.value = '请填写 1–500 字的裁判理由'; return }
  if (kind === 'extend' && (!Number.isInteger(minutes.value) || minutes.value < 1 || minutes.value > 60)) {
    notice.value = '单次补时为 1–60 整分钟'; return
  }
  busy.value = true
  try {
    const tournament = await tournamentApi.get(room.tournamentId)
    // Scope changes while awaiting a read must not operate a new table.
    if (!l12State.refereeControlsEnabled || l12State.room?.tournamentId !== room.tournamentId
      || l12State.room?.tournamentMatchId !== room.tournamentMatchId) return
    if (kind === 'pause') await tournamentApi.pauseMatch(room.tournamentId, room.tournamentMatchId,
      tournament.version, !l12State.rankedClock?.paused, explanation)
    else await tournamentApi.extendMatch(room.tournamentId, room.tournamentMatchId,
      tournament.version, minutes.value, explanation)
    notice.value = kind === 'extend' ? '补时已提交，沿本桌原计时口径生效' : '暂停状态已提交，等待本桌同步'
  } catch (error) { notice.value = error instanceof Error ? error.message : '裁判操作未完成，请重试' }
  finally { busy.value = false }
}
</script>

<template>
  <GmPanel :game="game" :initially-open="false" referee-mode :referee-reason="reason" @open-change="$emit('openChange', $event)">
    <template #referee-controls><section class="referee-controls">
      <label>裁判理由<textarea v-model="reason" maxlength="500" rows="2" /></label>
      <button :disabled="busy || !reason.trim()" @click="control('pause')">{{ l12State.rankedClock?.paused ? '恢复本桌' : '暂停本桌' }}</button>
      <label>补时（分钟）<input v-model.number="minutes" type="number" min="1" max="60" step="1" /></label>
      <button :disabled="busy || !reason.trim()" @click="control('extend')">为本桌补时</button>
      <p v-if="notice" role="status">{{ notice }}</p>
    </section></template>
  </GmPanel>
</template>

<style scoped>
.referee-controls{display:grid;gap:8px;padding:12px;color:var(--l12-ui-text);font-family:var(--l12-font-sans,'Microsoft YaHei',sans-serif)}
label{display:grid;gap:5px}textarea,input,button{box-sizing:border-box;width:100%;min-height:40px;border:1px solid var(--l12-ui-line-strong);background:var(--l12-ui-control);color:var(--l12-ui-text);font:inherit}textarea{resize:vertical}button:disabled{opacity:.45}p{margin:0;font-size:12px;line-height:1.5}
</style>
