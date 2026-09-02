<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import GmPanel from './game/GmPanel.vue'
import OsirisVictorySequence from './game/OsirisVictorySequence.vue'
import RankedBroadcastTicker from './site/RankedBroadcastTicker.vue'
import { gameAction, l12State, leaveRoom, returnToRoom } from './net'

const router = useRouter()
const game = computed(() => l12State.game)
const opponent = computed(() => l12State.room?.players.find(player => player.playerIndex !== l12State.room?.yourPlayerIndex))
const completedOsirisSequence = ref('')
const osirisSequenceKey = ref('')
let osirisSequenceMatchId = ''
let lastOsirisVictorySequence = 0
watch(() => [game.value?.matchId ?? '', game.value?.recentEvents?.map(event => event.sequence).join(',') ?? ''], () => {
  const matchId = game.value?.matchId ?? ''
  const events = (game.value?.recentEvents ?? [])
    .filter(item => item.type === 'special-victory'
      && item.cards?.some(card => card.cardId === 'S01-02M2'))
    .sort((left, right) => left.sequence - right.sequence)
  const highest = Math.max(0, ...events.map(event => event.sequence))
  if (matchId !== osirisSequenceMatchId) {
    osirisSequenceMatchId = matchId
    lastOsirisVictorySequence = highest
    osirisSequenceKey.value = ''
    completedOsirisSequence.value = ''
    return
  }
  const event = events.find(event => event.sequence > lastOsirisVictorySequence)
  lastOsirisVictorySequence = Math.max(lastOsirisVictorySequence, highest)
  if (!event || !matchId) return
  const key = `${matchId}:${event.sequence}`
  if (key === completedOsirisSequence.value || key === osirisSequenceKey.value) return
  osirisSequenceKey.value = key
}, { immediate: true })
const osirisSequencePlaying = computed(() => Boolean(osirisSequenceKey.value
  && completedOsirisSequence.value !== osirisSequenceKey.value))
function completeOsirisSequence() {
  completedOsirisSequence.value = osirisSequenceKey.value
  osirisSequenceKey.value = ''
}
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
  const tournamentCode = l12State.room?.tournamentCode ?? game.value?.tournamentCode
  if (l12State.spectating || l12State.room?.sandbox || l12State.room?.tournamentId || game.value?.tournamentId) leaveRoom()
  else if (l12State.room && game.value?.phase === 'GameOver') returnToRoom()
  router.push(tournamentCode ? { path: '/battle/tournaments', query: { code: tournamentCode } } : '/lobby')
}
</script>

<template>
  <div v-if="game" class="game-page">
    <RankedBroadcastTicker class="battle-ranked-ticker" />
    <div class="battle-route-controls">
      <span :class="{ online: opponent?.connected }"><i/>对手{{ opponent?.connected ? '在线' : '已断开' }}</span>
      <button @click="returnToLobby">返回大厅</button>
      <button v-if="!l12State.spectating && game.phase !== 'GameOver'" class="surrender" @click="surrender">投降</button>
    </div>
    <GameBoard :game="game" :read-only="l12State.spectating" :gm-placement="gmPlacement"
      @gm-placement-resolved="gmPlacement = null" />
    <GmPanel v-if="l12State.gmEnabled" :game="game" @arm-placement="gmPlacement = $event" />
    <OsirisVictorySequence v-if="osirisSequencePlaying" :key="osirisSequenceKey"
      @complete="completeOsirisSequence" />

    <Transition name="fade">
      <button v-if="l12State.notice" class="toast" @click="l12State.notice = ''">{{ l12State.notice }}</button>
    </Transition>

    <Transition name="fade">
      <div v-if="game.phase === 'GameOver' && !osirisSequencePlaying" class="game-over">
        <p>{{ game.winner === game.you ? '胜利' : '败北' }}</p>
        <strong>{{ game.winnerReason || '对局已结束' }}</strong>
        <small>MATCH {{ game.matchId.slice(0, 12) }} · REV {{ game.revision }}</small>
        <section v-if="l12State.rankedSettlement" class="ranked-result">
          <b>{{ l12State.rankedSettlement.faction }} · {{ l12State.rankedSettlement.tierAfter }}</b>
          <strong v-if="l12State.rankedSettlement.placement && l12State.rankedSettlement.placementPlayed < l12State.rankedSettlement.placementRequired">定级 {{ l12State.rankedSettlement.placementPlayed }}/{{ l12State.rankedSettlement.placementRequired }}</strong>
          <strong v-else>七曜值 {{ l12State.rankedSettlement.before.toLocaleString() }} → {{ l12State.rankedSettlement.after.toLocaleString() }} <i>{{ l12State.rankedSettlement.delta >= 0 ? '+' : '' }}{{ l12State.rankedSettlement.delta.toLocaleString() }}</i></strong>
          <details v-if="l12State.rankedSettlement.components.length"><summary>查看结算明细</summary><span v-for="item in l12State.rankedSettlement.components" :key="item.kind">{{ item.label }} {{ item.value >= 0 ? '+' : '' }}{{ item.value.toLocaleString() }}</span></details>
        </section>
        <button @click="returnToLobby">返回大厅</button>
      </div>
    </Transition>
  </div>
  <main v-else class="missing-game">
    <h1>对局状态尚未加载</h1>
    <button @click="returnToLobby">返回赛事/大厅</button>
  </main>
</template>

<style scoped>
.battle-route-controls{position:fixed;z-index:1600;top:12px;right:14px;display:flex;align-items:center;gap:7px;padding:6px;border:1px solid #445057;background:#080d11e8;box-shadow:0 8px 24px #000}.battle-route-controls span{display:flex;align-items:center;gap:6px;padding:0 7px;color:#b76570;font-size:9px;font-weight:900}.battle-route-controls span.online{color:#58c99a}.battle-route-controls i{width:7px;height:7px;border-radius:50%;background:currentColor;box-shadow:0 0 7px currentColor}.battle-route-controls button{padding:7px 10px;border:1px solid #57636a;background:#121a20;color:#fff;font-size:9px;font-weight:900}.battle-route-controls .surrender{border-color:#7f343e;background:#321219;color:#f2b6bc}
.battle-ranked-ticker{position:fixed;z-index:1500;top:8px;left:50%;width:min(760px,calc(100vw - 430px));transform:translateX(-50%)}.ranked-result{display:flex;min-width:320px;flex-direction:column;gap:6px;margin:12px 0;padding:12px;border:1px solid #a88c42;background:#17150d}.ranked-result>b{color:#e8cf7e}.ranked-result strong{font-size:13px}.ranked-result i{color:#65d2a1;font-style:normal}.ranked-result details span{display:flex;justify-content:space-between;color:#b5bdbe;font-size:10px}.ranked-result summary{cursor:pointer;color:#e1c978;font-size:10px}@media(max-width:900px){.battle-ranked-ticker{top:52px;width:calc(100vw - 20px)}}
</style>
