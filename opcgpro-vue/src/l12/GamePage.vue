<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import GmPanel from './game/GmPanel.vue'
import OsirisVictorySequence from './game/OsirisVictorySequence.vue'
import { gameAction, l12State, leaveRoom, returnToRoom } from './net'

const router = useRouter()
const game = computed(() => l12State.game)
const opponent = computed(() => l12State.room?.players.find(player => player.playerIndex !== l12State.room?.yourPlayerIndex))
const completedOsirisSequence = ref('')
const osirisSequenceKey = computed(() => {
  const event = [...(game.value?.recentEvents ?? [])].reverse().find(item => item.type === 'special-victory'
    && item.cards?.some(card => card.cardId === 'S01-02M2'))
  return event && game.value ? `${game.value.matchId}:${event.sequence}` : ''
})
const osirisSequencePlaying = computed(() => Boolean(osirisSequenceKey.value
  && completedOsirisSequence.value !== osirisSequenceKey.value))
const gmPlacement = ref<{
  type: 'placeCard' | 'playHandCard'
  targetPlayer: number
  cardId?: string
  cardInstanceId?: string
  cardName: string
  cardType: string
  triggerEffects: boolean
} | null>(null)
function surrender() {
  if (!game.value || game.value.phase === 'GameOver' || !window.confirm('确定要投降并结束本局对战吗？')) return
  gameAction({ type: 'surrender' })
}
function returnToLobby() {
  if (l12State.room?.sandbox) leaveRoom()
  else if (l12State.room && game.value?.phase === 'GameOver') returnToRoom()
  router.push('/lobby')
}
</script>

<template>
  <div v-if="game" class="game-page">
    <div class="battle-route-controls">
      <span :class="{ online: opponent?.connected }"><i/>对手{{ opponent?.connected ? '在线' : '已断开' }}</span>
      <button @click="returnToLobby">返回大厅</button>
      <button v-if="!l12State.spectating && game.phase !== 'GameOver'" class="surrender" @click="surrender">投降</button>
    </div>
    <GameBoard :game="game" :read-only="l12State.spectating" :gm-placement="gmPlacement"
      @gm-placement-resolved="gmPlacement = null" />
    <GmPanel v-if="l12State.gmEnabled" :game="game" @arm-placement="gmPlacement = $event" />
    <OsirisVictorySequence v-if="osirisSequencePlaying" :key="osirisSequenceKey"
      @complete="completedOsirisSequence = osirisSequenceKey" />

    <Transition name="fade">
      <button v-if="l12State.notice" class="toast" @click="l12State.notice = ''">{{ l12State.notice }}</button>
    </Transition>

    <Transition name="fade">
      <div v-if="game.phase === 'GameOver' && !osirisSequencePlaying" class="game-over">
        <p>{{ game.winner === game.you ? '胜利' : '败北' }}</p>
        <strong>{{ game.winnerReason || '对局已结束' }}</strong>
        <small>MATCH {{ game.matchId.slice(0, 12) }} · REV {{ game.revision }}</small>
        <button @click="returnToLobby">返回大厅</button>
      </div>
    </Transition>
  </div>
  <main v-else class="missing-game">
    <h1>对局状态尚未加载</h1>
    <button @click="router.push('/lobby')">返回大厅</button>
  </main>
</template>

<style scoped>
.battle-route-controls{position:fixed;z-index:1600;top:12px;right:14px;display:flex;align-items:center;gap:7px;padding:6px;border:1px solid #445057;background:#080d11e8;box-shadow:0 8px 24px #000}.battle-route-controls span{display:flex;align-items:center;gap:6px;padding:0 7px;color:#b76570;font-size:9px;font-weight:900}.battle-route-controls span.online{color:#58c99a}.battle-route-controls i{width:7px;height:7px;border-radius:50%;background:currentColor;box-shadow:0 0 7px currentColor}.battle-route-controls button{padding:7px 10px;border:1px solid #57636a;background:#121a20;color:#fff;font-size:9px;font-weight:900}.battle-route-controls .surrender{border-color:#7f343e;background:#321219;color:#f2b6bc}
</style>
