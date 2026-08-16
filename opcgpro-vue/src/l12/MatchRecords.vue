<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { l12State } from './net'
import type { ActionEvent, Card, GameState, Phase, PlayerView } from './types'
import GameBoard from './game/GameBoard.vue'

interface MatchSummary {
  matchId: string
  roomCode: string
  player0: string
  player1: string
  deck0: string
  deck1: string
  startedUtc: string
  endedUtc?: string | null
  winner?: number | null
  finalHash?: string | null
  error?: string | null
  commandCount: number
}

interface RecordedCommand {
  sequence: number
  receivedUtc: string
  playerIndex: number
  command: Record<string, unknown>
  accepted: boolean
  error?: string | null
  revision: number
  stateHash: string
  state: Record<string, any>
}

interface MatchDetail { match: MatchSummary; commands: RecordedCommand[] }

const matches = ref<MatchSummary[]>([])
const detail = ref<MatchDetail | null>(null)
const selectedStep = ref(0)
const loading = ref(false)
const error = ref('')
const playing = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
let playbackTimer: ReturnType<typeof setInterval> | null = null

const apiBase = computed(() => {
  try {
    const url = new URL(l12State.endpoint)
    url.protocol = url.protocol === 'wss:' ? 'https:' : 'http:'
    url.pathname = '/api/matches'
    url.search = ''
    url.hash = ''
    return url.toString().replace(/\/$/, '')
  } catch { return 'http://localhost:8080/api/matches' }
})

const currentCommand = computed(() => detail.value?.commands[selectedStep.value] ?? null)
const routineEventTypes = new Set([
  'phase', 'phase-detail', 'draw-skipped', 'prompt', 'prompt-resolved', 'priority-pass',
  'stack-push', 'stack-deferred', 'stack-open', 'stack-resolve', 'match-created',
  'mulligan-start', 'end-turn', 'disaster-removed',
])
const stateEvents = computed<any[]>(() => (currentCommand.value?.state?.Events ?? [])
  .filter((event: any) => !routineEventTypes.has(String(event.Type ?? ''))))
const statePlayers = computed<any[]>(() => currentCommand.value?.state?.Players ?? [])
const phaseNames: Phase[] = ['Initiative', 'DisasterPreparation', 'Mulligan', 'Disaster', 'Reset', 'Draw', 'Morale', 'Main', 'End', 'Defense', 'GameOver']

function value<T>(raw: any, pascal: string, camel: string, fallback: T): T {
  return (raw?.[pascal] ?? raw?.[camel] ?? fallback) as T
}
function replayCard(raw: any): Card | null {
  if (!raw) return null
  return {
    instanceId: value(raw, 'InstanceId', 'instanceId', ''), cardId: value(raw, 'CardId', 'cardId', ''),
    name: value(raw, 'Name', 'name', '未知卡牌'), cardType: value(raw, 'CardType', 'cardType', ''), faction: value(raw, 'Faction', 'faction', ''),
    imageUrl: value(raw, 'ImageUrl', 'imageUrl', undefined), effectText: value(raw, 'EffectText', 'effectText', undefined),
    cost: value(raw, 'Cost', 'cost', 0), currentCost: value(raw, 'CurrentCost', 'currentCost', value(raw, 'Cost', 'cost', 0)),
    baseTroops: value(raw, 'BaseTroops', 'baseTroops', 0), troops: value(raw, 'Troops', 'troops', 0),
    disasterLevel: value(raw, 'DisasterLevel', 'disasterLevel', 0), trialValue: value(raw, 'TrialValue', 'trialValue', 0),
    tapped: value(raw, 'Tapped', 'tapped', false),
    hidden: value(raw, 'Hidden', 'hidden', false), summonRound: value(raw, 'SummonRound', 'summonRound', 0),
    hasCharge: value(raw, 'HasCharge', 'hasCharge', false), hasStrongAttack: value(raw, 'HasStrongAttack', 'hasStrongAttack', false),
    hasSureHit: value(raw, 'HasSureHit', 'hasSureHit', false), cannotAttack: value(raw, 'CannotAttack', 'cannotAttack', false),
    cannotSupport: value(raw, 'CannotSupport', 'cannotSupport', false), immortalUses: value(raw, 'ImmortalUses', 'immortalUses', 0),
  }
}
function replayPlayer(raw: any): PlayerView {
  const zones = value<any>(raw, 'SpecialZones', 'specialZones', {})
  const fieldRaw = value<any[][]>(raw, 'Field', 'field', [[], []])
  return {
    playerIndex: value(raw, 'PlayerIndex', 'playerIndex', 0), name: value(raw, 'Name', 'name', '玩家'),
    deckName: value(raw, 'DeckName', 'deckName', ''), faction: value(raw, 'Faction', 'faction', ''),
    master: {
      masterId: value(raw, 'MasterId', 'masterId', ''), masterName: value(raw, 'MasterName', 'masterName', '主宰'),
      masterImageUrl: value(raw, 'MasterImageUrl', 'masterImageUrl', undefined), hp: value(raw, 'Hp', 'hp', 0),
      maxHp: value(raw, 'MaxHp', 'maxHp', 0), tapped: value(raw, 'MasterTapped', 'masterTapped', false),
    },
    libraryCount: value<any[]>(raw, 'Library', 'library', []).length,
    hand: value<any[]>(raw, 'Hand', 'hand', []).map(replayCard).filter(Boolean) as Card[],
    handCount: value<any[]>(raw, 'Hand', 'hand', []).length,
    moraleDeck: value<any[]>(raw, 'MoraleDeck', 'moraleDeck', []).map(card => ({ instanceId: value(card, 'InstanceId', 'instanceId', ''), cardId: value(card, 'CardId', 'cardId', ''), tapped: value(card, 'Tapped', 'tapped', false) })),
    morale: value<any[]>(raw, 'Morale', 'morale', []).map(card => ({ instanceId: value(card, 'InstanceId', 'instanceId', ''), cardId: value(card, 'CardId', 'cardId', ''), tapped: value(card, 'Tapped', 'tapped', false) })),
    field: [0, 1].map(row => [0, 1, 2].map(slot => replayCard(fieldRaw?.[row]?.[slot]))),
    relic: replayCard(value(raw, 'Relic', 'relic', null)), extraRelics: value<any[]>(raw, 'ExtraRelics', 'extraRelics', []).map(replayCard).filter(Boolean) as Card[],
    graveyard: value<any[]>(raw, 'Graveyard', 'graveyard', []).map(replayCard).filter(Boolean) as Card[],
    resolving: value<any[]>(raw, 'Resolving', 'resolving', []).map(replayCard).filter(Boolean) as Card[],
    specialZones: {
      runes: value(zones, 'Runes', 'runes', 0), trialLevel: value(zones, 'TrialLevel', 'trialLevel', 0),
      godPower: value<any[]>(zones, 'GodPower', 'godPower', []).map(replayCard).filter(Boolean) as Card[],
      trials: value<any[]>(zones, 'Trials', 'trials', []).map(replayCard).filter(Boolean) as Card[],
    },
    temporaryMorale: value(raw, 'TemporaryMorale', 'temporaryMorale', 0), mulliganDone: value(raw, 'MulliganDone', 'mulliganDone', false),
  }
}
const replayGame = computed<GameState | null>(() => {
  const raw = currentCommand.value?.state
  if (!raw) return null
  const rawPhase = value<any>(raw, 'Phase', 'phase', 'Main')
  const defense = value<any>(raw, 'PendingDefense', 'pendingDefense', null)
  const events: ActionEvent[] = value<any[]>(raw, 'Events', 'events', []).map(event => ({
    sequence: value(event, 'Sequence', 'sequence', 0), type: value(event, 'Type', 'type', ''),
    playerIndex: value(event, 'PlayerIndex', 'playerIndex', undefined), text: value(event, 'Text', 'text', ''),
    cards: value<any[]>(event, 'Cards', 'cards', []).map(replayCard).filter(Boolean) as Card[],
  }))
  return {
    matchId: value(raw, 'MatchId', 'matchId', detail.value?.match.matchId ?? ''), roomCode: value(raw, 'RoomCode', 'roomCode', detail.value?.match.roomCode ?? ''),
    you: 0, revision: currentCommand.value?.revision ?? 0, activePlayer: value(raw, 'ActivePlayer', 'activePlayer', 0),
    firstPlayer: value(raw, 'FirstPlayer', 'firstPlayer', 0), diceWinner: value(raw, 'DiceWinner', 'diceWinner', 0),
    initiativeRolls: value(raw, 'InitiativeRolls', 'initiativeRolls', [0, 0]), phase: typeof rawPhase === 'number' ? phaseNames[rawPhase] ?? 'Main' : rawPhase,
    round: value(raw, 'Round', 'round', 1), disasterValue: value(raw, 'DisasterValue', 'disasterValue', 0),
    activeDisaster: replayCard(value(raw, 'ActiveDisaster', 'activeDisaster', null)), players: value<any[]>(raw, 'Players', 'players', []).map(replayPlayer),
    pendingDefense: defense ? {
      attackerPlayer: value(defense, 'AttackerPlayer', 'attackerPlayer', 0),
      attackerInstanceId: value(defense, 'AttackerInstanceId', 'attackerInstanceId', ''),
      target: {
        type: value(value(defense, 'Target', 'target', {}), 'Type', 'type', 'master'),
        instanceId: value(value(defense, 'Target', 'target', {}), 'InstanceId', 'instanceId', undefined),
      },
    } : null, winner: value(raw, 'Winner', 'winner', null),
    prompts: [], effectStack: [], waitingPrompt: null, recentEvents: events, lastAction: events.at(-1) ?? null,
    legalAttackTargets: {}, stateHash: currentCommand.value?.stateHash ?? '',
  }
})

onMounted(loadMatches)
onBeforeUnmount(stopPlayback)
watch(selectedStep, () => {
  if (detail.value && selectedStep.value >= detail.value.commands.length - 1) stopPlayback()
})

async function loadMatches() {
  loading.value = true
  error.value = ''
  try {
    const response = await fetch(`${apiBase.value}?limit=100`)
    if (!response.ok) throw new Error(`服务器返回 ${response.status}`)
    matches.value = await response.json()
    if (matches.value[0]) await selectMatch(matches.value[0])
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取对局记录失败'
  } finally { loading.value = false }
}

async function selectMatch(match: MatchSummary) {
  stopPlayback()
  loading.value = true
  error.value = ''
  try {
    const response = await fetch(`${apiBase.value}/${encodeURIComponent(match.matchId)}`)
    if (!response.ok) throw new Error(`服务器返回 ${response.status}`)
    detail.value = await response.json()
    selectedStep.value = Math.max(0, detail.value!.commands.length - 1)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取对局详情失败'
  } finally { loading.value = false }
}

function exportReplay() {
  if (!detail.value) return
  const payload = { format: 'legion12-replay', version: 1, exportedAt: new Date().toISOString(), detail: detail.value }
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
  const anchor = document.createElement('a')
  anchor.href = URL.createObjectURL(blob)
  anchor.download = `${detail.value.match.matchId}.l12-replay.json`
  anchor.click()
  setTimeout(() => URL.revokeObjectURL(anchor.href), 1000)
}

async function importReplay(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  stopPlayback()
  error.value = ''
  try {
    const raw = JSON.parse(await file.text())
    const imported = (raw?.format === 'legion12-replay' ? raw.detail : raw) as MatchDetail
    if (!imported?.match?.matchId || !Array.isArray(imported.commands)) throw new Error('文件不是有效的十二军团回放')
    detail.value = imported
    selectedStep.value = Math.max(0, imported.commands.length - 1)
  } catch (reason) { error.value = reason instanceof Error ? reason.message : '回放导入失败' }
  finally { input.value = '' }
}

function dateLabel(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value))
}

function stopPlayback() {
  playing.value = false
  if (playbackTimer) clearInterval(playbackTimer)
  playbackTimer = null
}

function togglePlayback() {
  if (!detail.value?.commands.length) return
  if (playing.value) return stopPlayback()
  if (selectedStep.value >= detail.value.commands.length - 1) selectedStep.value = 0
  playing.value = true
  playbackTimer = setInterval(() => {
    if (!detail.value || selectedStep.value >= detail.value.commands.length - 1) return stopPlayback()
    selectedStep.value++
  }, 900)
}

function commandLabel(type: unknown) {
  const labels: Record<string, string> = {
    resolvePrompt: '完成选择', mulligan: '调度', playCard: '打出卡牌', attack: '进攻',
    resolveDefense: '抵挡／支援', move: '位移', activateAbility: '发动效果',
    flipHidden: '翻回正面', endTurn: '结束回合', surrender: '投降',
  }
  return labels[String(type)] ?? String(type ?? '未知操作')
}

</script>

<template>
  <section class="match-records grand-panel">
    <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
    <header class="records-header"><div><p class="kicker">MATCH RECORDS · SQLITE / JSON</p><h1>对局记录</h1></div><div class="record-file-actions"><input ref="fileInput" type="file" accept="application/json,.json" @change="importReplay"/><button @click="fileInput?.click()">打开 JSON 回放</button><button :disabled="!detail" @click="exportReplay">保存 JSON</button><button @click="loadMatches">刷新记录</button></div></header>
    <p v-if="error" class="records-error">{{ error }}。请确认服务端正在运行。</p>
    <div class="records-workspace">
      <aside class="records-list">
        <button v-for="match in matches" :key="match.matchId" :class="{ selected: detail?.match.matchId === match.matchId }" @click="selectMatch(match)">
          <span><b>{{ match.player0 }}</b><em>VS</em><b>{{ match.player1 }}</b></span>
          <small>{{ dateLabel(match.startedUtc) }} · {{ match.commandCount }} 次操作</small>
          <i>{{ match.endedUtc ? (match.winner === null ? '已结束' : `${match.winner === 0 ? match.player0 : match.player1} 胜`) : '进行中' }}</i>
        </button>
        <p v-if="!loading && !matches.length">尚无已记录对局。</p>
      </aside>

      <main v-if="detail" class="record-detail">
        <header><div><small>ROOM {{ detail.match.roomCode }}</small><h2>{{ detail.match.player0 }} <em>VS</em> {{ detail.match.player1 }}</h2><p>{{ detail.match.deck0 }} · {{ detail.match.deck1 }}</p></div><code>{{ detail.match.matchId.slice(0, 12) }}</code></header>
        <div v-if="detail.commands.length" class="playback-bar">
          <button :disabled="selectedStep <= 0" @click="selectedStep--">‹</button>
          <button class="playback-toggle" @click="togglePlayback">{{ playing ? '暂停' : '播放' }}</button>
          <input v-model.number="selectedStep" type="range" min="0" :max="detail.commands.length - 1"/>
          <button :disabled="selectedStep >= detail.commands.length - 1" @click="selectedStep++">›</button>
          <b>步骤 {{ selectedStep + 1 }} / {{ detail.commands.length }}</b>
        </div>
        <section v-if="currentCommand" class="record-state">
          <div class="record-command" :class="{ rejected: !currentCommand.accepted }">
            <span>玩家 {{ currentCommand.playerIndex + 1 }}</span><b>{{ commandLabel(currentCommand.command.type) }}</b><small>REV {{ currentCommand.revision }}</small>
            <p v-if="currentCommand.error">{{ currentCommand.error }}</p>
          </div>
          <div class="record-score"><article v-for="player in statePlayers" :key="player.PlayerIndex"><span>{{ player.Name }}</span><b>{{ player.Hp }} / {{ player.MaxHp }}</b><small>手牌 {{ player.Hand?.length ?? 0 }} · 牌库 {{ player.Library?.length ?? 0 }} · 墓地 {{ player.Graveyard?.length ?? 0 }}</small></article><strong>天灾 {{ currentCommand.state.DisasterValue ?? 0 }}</strong></div>
          <div v-if="replayGame" class="record-live-board" aria-label="只读实战棋盘回放"><GameBoard :game="replayGame" read-only embedded /></div>
          <div class="record-timeline"><h3>截至此步的完整记录</h3><ol><li v-for="event in [...stateEvents].reverse()" :key="event.Sequence"><b>#{{ event.Sequence }}</b><span>{{ event.Text }}</span></li></ol></div>
        </section>
      </main>
      <div v-else class="records-placeholder">{{ loading ? '正在读取对局记录…' : '选择一场对局查看详细记录' }}</div>
    </div>
  </section>
</template>
