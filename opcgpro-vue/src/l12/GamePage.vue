<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import GmPanel from './game/GmPanel.vue'
import OsirisVictorySequence from './game/OsirisVictorySequence.vue'
import RankedBroadcastTicker from './site/RankedBroadcastTicker.vue'
import L12SettingsModal from './site/L12SettingsModal.vue'
import { gameAction, l12State, leaveRoom } from './net'

const router = useRouter()
const game = computed(() => l12State.game)
const agreedDraw = computed(() => game.value?.phase === 'GameOver' && game.value.winner == null
  && (game.value.matchGovernance?.drawRequest?.status === 'accepted'
    || game.value.recentEvents?.some(event => event.type === 'game-draw')))
const playerFacingWinnerReason = computed(() => {
  const raw = game.value?.winnerReason?.trim() ?? ''
  const filtered = raw.split(/(?<=[。！？；])|\n+/)
    .map(part => part.trim())
    .filter(part => part && !/(双方.*离开.*关闭房间|最长.*30\s*分钟|点击返回.*离开本局|返回后才离开|服务器.*保留|结果将保留在此处)/.test(part))
    .join('')
  return filtered || '对局已结束'
})
const settingsOpen = ref(false)
const gmPanelOpen = ref(l12State.gmEnabled)
const gameOverMinimized = ref(false)
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
watch(() => `${game.value?.matchId ?? ''}:${game.value?.phase ?? ''}`, () => { gameOverMinimized.value = false })
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
  const tournamentCode = l12State.room?.tournamentCode
  if (game.value?.phase === 'GameOver') {
    leaveRoom()
    router.push('/lobby')
    return
  }
  if (l12State.spectating || l12State.room?.sandbox || tournamentCode) leaveRoom()
  router.push(tournamentCode ? `/battle/tournaments?code=${encodeURIComponent(tournamentCode)}` : '/lobby')
}
</script>

<template>
  <div v-if="game" class="game-page">
    <RankedBroadcastTicker class="battle-ranked-ticker" />
    <div class="battle-route-controls">
      <span :class="{ online: opponent?.connected }"><i/>对方{{ opponent?.connected ? '在线' : '已断开' }}</span>
      <button class="balanced-copy-button" aria-label="返回大厅" @click="returnToLobby"><span class="route-label" aria-hidden="true"><span>返回</span><span>大厅</span></span></button>
      <button v-if="!l12State.spectating && game.phase !== 'GameOver'" class="surrender" @click="surrender">投降</button>
    </div>
    <GameBoard :game="game" :read-only="l12State.spectating" :gm-placement="gmPlacement" :gm-panel-open="gmPanelOpen"
      @gm-placement-resolved="gmPlacement = null" @settings="settingsOpen = true" />
    <GmPanel v-if="l12State.gmEnabled" :game="game" @arm-placement="gmPlacement = $event" @open-change="gmPanelOpen = $event" />
    <OsirisVictorySequence v-if="osirisSequencePlaying" :key="osirisSequenceKey"
      @complete="completeOsirisSequence" />
    <div v-if="settingsOpen" class="battle-settings-mask" @click.self="settingsOpen = false">
      <L12SettingsModal @close="settingsOpen = false"/>
    </div>

    <Transition name="fade">
      <button v-if="l12State.notice" class="toast" @click="l12State.notice = ''">{{ l12State.notice }}</button>
    </Transition>

    <Transition name="fade">
      <button v-if="game.phase === 'GameOver' && !osirisSequencePlaying && gameOverMinimized" class="game-over-restore" type="button" @click="gameOverMinimized = false">恢复对局结果</button>
    </Transition>
    <Transition name="fade">
      <div v-if="game.phase === 'GameOver' && !osirisSequencePlaying && !gameOverMinimized" class="game-over"
        data-ui-contract="manual-game-over-exit" role="dialog" aria-modal="true" aria-label="对局结果">
        <button class="game-over-minimize" type="button" aria-label="最小化对局结果" @click="gameOverMinimized = true">—</button>
        <p>{{ game.winner == null ? (agreedDraw ? '平局' : '对局无效') : (game.winner === game.you ? '胜利' : '败北') }}</p>
        <strong>{{ playerFacingWinnerReason }}</strong>
        <small>对局编号 {{ game.matchId.slice(0, 12) }}</small>
        <section v-if="l12State.rankedSettlement" class="ranked-result">
          <b>{{ l12State.rankedSettlement.faction }} · {{ ['held', 'voided'].includes(l12State.rankedSettlement.rewardStatus || '') ? l12State.rankedSettlement.tierBefore : l12State.rankedSettlement.tierAfter }}</b>
          <strong v-if="l12State.rankedSettlement.rewardStatus === 'held'">本局排位收益待审核，尚未计入七曜值与战绩。请查看处置通知，可提交申诉。</strong>
          <strong v-else-if="l12State.rankedSettlement.rewardStatus === 'voided'">本局排位收益已作废，请查看处置通知及判罚历史。</strong>
          <strong v-else-if="l12State.rankedSettlement.placement && l12State.rankedSettlement.placementPlayed < l12State.rankedSettlement.placementRequired">定级 {{ l12State.rankedSettlement.placementPlayed }}/{{ l12State.rankedSettlement.placementRequired }}</strong>
          <strong v-else>七曜值 {{ l12State.rankedSettlement.before.toLocaleString() }} → {{ l12State.rankedSettlement.after.toLocaleString() }} <i>{{ l12State.rankedSettlement.delta >= 0 ? '+' : '' }}{{ l12State.rankedSettlement.delta.toLocaleString() }}</i></strong>
          <details v-if="l12State.rankedSettlement.components.length && !['held', 'voided'].includes(l12State.rankedSettlement.rewardStatus || '')"><summary>查看结算明细</summary><span v-for="item in l12State.rankedSettlement.components" :key="item.kind">{{ item.label }} {{ item.value >= 0 ? '+' : '' }}{{ item.value.toLocaleString() }}</span></details>
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
.battle-route-controls{position:fixed;z-index:1600;top:12px;right:14px;display:flex;align-items:center;gap:7px;padding:6px;border:1px solid #445057;background:#080d11e8;box-shadow:0 8px 24px #000}.battle-route-controls>span{display:flex;align-items:center;gap:6px;padding:0 7px;color:#b76570;font-size:14px;font-weight:900}.battle-route-controls>span.online{color:#58c99a}.battle-route-controls i{width:7px;height:7px;border-radius:50%;background:currentColor;box-shadow:0 0 7px currentColor}.battle-route-controls button{padding:7px 10px;border:1px solid #57636a;background:#121a20;color:#fff;font-size:14px;font-weight:900;text-align:center;text-wrap:balance;word-break:break-all}.battle-route-controls .surrender{border-color:#7f343e;background:#321219;color:#f2b6bc}.battle-route-controls .route-label,.battle-route-controls .route-label>span{display:inline;padding:0;color:inherit;font:inherit;line-height:inherit}
.battle-settings-button{position:fixed;z-index:1600;left:12px;bottom:12px;display:grid;width:48px;height:48px;place-items:center;border:1px solid #59666b;background:#080d11ed;box-shadow:0 8px 24px #000;color:#e8d183;font-size:19px}.battle-settings-button span{position:absolute;left:100%;bottom:0;padding:4px 7px;border:1px solid #38454b;background:#080d11ed;color:#9da8a8;font-size:14px;letter-spacing:.12em}.battle-settings-mask{position:fixed;z-index:4000;inset:0;display:grid;place-items:center;padding:18px;background:#010407c9;backdrop-filter:blur(8px)}
.battle-ranked-ticker{position:fixed;z-index:1500;top:8px;left:50%;width:min(760px,calc(100vw - 430px));transform:translateX(-50%)}.ranked-result{display:flex;min-width:320px;flex-direction:column;gap:6px;margin:12px 0;padding:12px;border:1px solid #a88c42;background:#17150d}.ranked-result>b{color:#e8cf7e}.ranked-result strong{font-size:14px}.ranked-result i{color:#65d2a1;font-style:normal}.ranked-result details span{display:flex;justify-content:space-between;color:#b5bdbe;font-size:14px}.ranked-result summary{cursor:pointer;color:#e1c978;font-size:14px}@media(max-width:900px){.battle-ranked-ticker{top:52px;width:calc(100vw - 20px)}}
.game-over-minimize,.game-over-restore{display:none}
/* Only the viewport-gated compact battlefield gets this compact route dock.  Desktop
   controls retain their existing layout even in a short browser window. */
.game-page:has(.mobile-landscape-board){inset:0!important;width:100%!important;height:100%!important}
.game-page:has(.mobile-landscape-board) .battle-route-controls{top:calc(var(--l12-viewport-top,0px) + 106px);right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 6px);width:90px;box-sizing:border-box;display:grid;grid-template-columns:1fr 1fr;gap:3px;padding:3px;box-shadow:none}.game-page:has(.mobile-landscape-board) .battle-route-controls>span{display:none}.game-page:has(.mobile-landscape-board) .battle-route-controls button{display:grid;min-width:0;min-height:32px;place-items:center;padding:3px 2px;font-size:10px;line-height:1.05}.game-page:has(.mobile-landscape-board) .battle-route-controls .route-label{display:grid;width:100%;height:100%;place-content:center;justify-items:center;gap:1px;padding:0}.game-page:has(.mobile-landscape-board) .battle-route-controls .route-label>span{display:block;min-width:2em;padding:0;text-align:center;white-space:nowrap}.game-page:has(.mobile-landscape-board) .battle-ranked-ticker{display:none}
.game-page:has(.mobile-landscape-board) :deep(.gm-open){top:calc(var(--l12-viewport-top,0px) + 42px);right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 102px)}
.game-page:has(.mobile-landscape-board) :deep(.gm-panel){top:calc(var(--l12-viewport-top,0px) + 8px);right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 8px);bottom:auto;box-sizing:border-box;width:min(320px,calc(var(--l12-viewport-width,100vw) - 16px));height:calc(var(--l12-viewport-height,100vh) - 16px)}
.game-page:has(.mobile-landscape-board) .game-over{box-sizing:border-box;inset:0;width:100%;height:100%;padding:max(10px,env(safe-area-inset-top)) max(12px,env(safe-area-inset-right)) max(10px,env(safe-area-inset-bottom)) max(12px,env(safe-area-inset-left));place-content:safe center;gap:6px;overflow:auto}
.game-page:has(.mobile-landscape-board) .game-over>p{font-size:clamp(32px,10vh,52px);line-height:1;letter-spacing:.12em}
.game-page:has(.mobile-landscape-board) .game-over>strong{display:block;max-width:min(620px,calc(var(--l12-viewport-width,100vw) - 32px));margin:auto;font-size:12px;line-height:1.35}
.game-page:has(.mobile-landscape-board) .game-over>small{font-size:9px;line-height:1.2}
.game-page:has(.mobile-landscape-board) .game-over>.ranked-result{box-sizing:border-box;width:min(440px,calc(var(--l12-viewport-width,100vw) - 32px));min-width:0;max-height:42vh;margin:2px auto;padding:6px 8px;gap:3px;overflow:auto}
.game-page:has(.mobile-landscape-board) .game-over>.ranked-result>b{font-size:12px}
.game-page:has(.mobile-landscape-board) .game-over>.ranked-result strong,.game-page:has(.mobile-landscape-board) .game-over>.ranked-result summary,.game-page:has(.mobile-landscape-board) .game-over>.ranked-result details span{font-size:10px;line-height:1.3}
.game-page:has(.mobile-landscape-board) .game-over>button{min-height:32px;margin:0 auto;padding:5px 18px;font-size:12px}
.game-page:has(.mobile-landscape-board) .game-over>.game-over-minimize{position:absolute;top:8px;right:8px;display:block;width:34px;min-width:34px;height:30px;margin:0;padding:0}
.game-page:has(.mobile-landscape-board) .game-over-restore{position:fixed;z-index:2147483604;left:96px;bottom:74px;display:block;min-height:32px;padding:4px 9px;border:1px solid #d6bd69;background:#403714;color:#fff;font-size:10px;font-weight:900}
.game-page:has(.mobile-landscape-board):has(.game-over) .battle-route-controls{display:none}
</style>
