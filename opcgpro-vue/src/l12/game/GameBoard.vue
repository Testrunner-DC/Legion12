<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ActionEvent, Card, DisasterCardView, GameState, Phase } from '../types'
import { isHorizontalCardType } from '../cardPresentation'
import { gameAction, l12State } from '../net'
import CardTile from '../CardTile.vue'
import GameActions from './GameActions.vue'
import GraveyardOverlay from './GraveyardOverlay.vue'
import HandArea from './HandArea.vue'
import MasterOverlay from './MasterOverlay.vue'
import PhaseTrack from './PhaseTrack.vue'
import PlayerMat from './PlayerMat.vue'
import PhasePlayback from './PhasePlayback.vue'
import PromptOverlay from './PromptOverlay.vue'

const props = withDefaults(defineProps<{ game: GameState; readOnly?: boolean; embedded?: boolean }>(), { readOnly: false, embedded: false })
const scale = ref(1)
const compactViewport = ref(false)
const selectedId = ref<string | null>(null)
const focusCard = ref<Card | null>(null)
const mode = ref<'play' | 'attack' | 'move'>('play')
const mulliganIds = ref<string[]>([])
const defenseIds = ref<string[]>([])
const supportId = ref<string | null>(null)
const graveyardPlayer = ref<number | null>(null)
const playArmed = ref(false)
const masterPlayerIndex = ref<number | null>(null)
const boardTargetIds = ref<string[]>([])
const phasePlaybackPhase = ref<Phase | null>(null)
const me = computed(() => props.game.players[props.game.you])
const enemy = computed(() => props.game.players[1 - props.game.you])
const defenseTargetType = computed(() => props.game.pendingDefense?.target.type ?? null)
const isMyMain = computed(() => props.game.phase === 'Main' && props.game.activePlayer === props.game.you)
const activeMorale = computed(() =>
  me.value.morale.filter(card => !card.tapped).length
  + (me.value.temporaryMorale ?? 0)
  + me.value.field.flat().filter(card => card?.cardId === 'S01-0212' && !card.tapped).length,
)
const counterIds = new Set([
  'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
  'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420',
])
const isCounter = (card?: Card | null) => Boolean(card && (card.cardType === 'counter-tactic' || counterIds.has(card.cardId)))
const playableIds = computed(() => {
  if (!isMyMain.value) return []
  const hasLegionDestination = me.value.field.some((row, rowIndex) => row.some(card => !card || (rowIndex === 1 && isCounter(card))))
  return (me.value.hand ?? [])
    .filter(card => (card.currentCost ?? card.cost) <= activeMorale.value && (card.cardType !== 'legion' || hasLegionDestination))
    .map(card => card.instanceId)
})
const selectedHandCard = computed(() => me.value.hand?.find(card => card.instanceId === selectedId.value) ?? null)
const selectedAttackTargets = computed(() => selectedId.value ? (props.game.legalAttackTargets?.[selectedId.value] ?? []) : [])
const attackableIds = computed(() => Object.keys(props.game.legalAttackTargets ?? {}))
const responsePlayableIds = computed(() => {
  const response = props.game.prompts?.find(prompt => prompt.kind === 'response')
  return response?.validChoices.filter(id => id !== 'pass') ?? []
})
const handPlayableIds = computed(() => {
  if (isMyMain.value) return playableIds.value
  if (responsePlayableIds.value.length) return responsePlayableIds.value
  if (props.game.phase === 'Defense' && props.game.activePlayer !== props.game.you && defenseTargetType.value === 'master')
    return (me.value.hand ?? []).filter(card => card.cardType === 'legion').map(card => card.instanceId)
  return []
})
const boardTargetPrompt = computed(() => {
  const fieldIds = new Set(props.game.players.flatMap(player => player.field.flat().filter(Boolean).map(card => card!.instanceId)))
  return props.game.prompts?.find(prompt => {
    if (!['target', 'targets', 'optional-target', 'optional-targets', 'active-target'].includes(prompt.kind)) return false
    const choices = prompt.validChoices.filter(id => id !== 'skip')
    return choices.length > 0 && choices.every(id => fieldIds.has(id))
  }) ?? null
})
const boardTargetableIds = computed(() => boardTargetPrompt.value?.validChoices.filter(id => id !== 'skip') ?? [])
const boardSlotPrompt = computed(() => props.game.prompts?.find(prompt =>
  prompt.kind === 'slot' && prompt.validChoices.length > 0
  && prompt.validChoices.every(id => /^\d+:\d+$/.test(id)),
) ?? null)
const activeBoardPromptId = computed(() => boardTargetPrompt.value?.promptId ?? boardSlotPrompt.value?.promptId ?? null)
const modalInspectorVisible = computed(() => Boolean(focusCard.value && (
  graveyardPlayer.value !== null || masterPlayerIndex.value !== null || props.game.phase === 'Mulligan'
  || (props.game.prompts?.length ?? 0) > 0 || props.game.waitingPrompt
)))
const sessionDisasters = computed(() => props.game.sessionDisasters ?? [])
function isVisibleDisasterCard(card: DisasterCardView): card is Card {
  return !card.hidden && Boolean(card.cardId && card.name && card.cardType)
}
function focusSessionDisaster(card: DisasterCardView) {
  if (isVisibleDisasterCard(card)) focusCard.value = card
}
const boardSlotPreview = computed<Card | null>(() => {
  const prompt = boardSlotPrompt.value
  const id = prompt?.data?.previewCardId
  if (!prompt || !id) return null
  return {
    instanceId: id,
    cardId: prompt.data?.[`${id}:cardId`] ?? '',
    name: prompt.data?.[id] ?? '展示牌',
    cardType: prompt.data?.[`${id}:cardType`] ?? '',
    faction: prompt.data?.[`${id}:faction`] ?? '',
    imageUrl: prompt.data?.[`${id}:image`],
    effectText: prompt.data?.[`${id}:effect`] ?? '',
    cost: Number(prompt.data?.[`${id}:cost`] ?? 0),
    baseTroops: Number(prompt.data?.[`${id}:baseTroops`] ?? 0),
    troops: Number(prompt.data?.[`${id}:troops`] ?? 0),
    disasterLevel: Number(prompt.data?.[`${id}:disasterLevel`] ?? 0),
    tapped: false,
    summonRound: 0,
  }
})
watch(() => boardTargetPrompt.value?.promptId, () => { boardTargetIds.value = [] })
const hiddenLogTypes = new Set([
  'phase', 'phase-detail', 'draw-skipped', 'prompt', 'prompt-resolved', 'priority-pass',
  'stack-push', 'stack-deferred', 'stack-open', 'stack-resolve', 'match-created',
  'mulligan-start', 'end-turn', 'disaster-removed',
])
const events = computed(() => [...(props.game.recentEvents ?? [])]
  .filter(event => !hiddenLogTypes.has(event.type)).reverse())
const eventLabels: Record<string, string> = {
  play: '出牌', attack: '进攻', combat: '战斗', defense: '抵挡', support: '支援', move: '位移',
  response: '响应', 'counter-set': '盖伏', effect: '效果', 'faction-effect': '阵营',
  'effect-negated': '无效', 'initiative-choice': '先后攻', mulligan: '调度', cost: '费用',
  disaster: '天灾', 'disaster-active': '天灾', 'disaster-value': '天灾', damage: '伤害',
  heal: '恢复', leave: '离场', put: '登场', search: '检索', reveal: '展示', return: '返回',
  discard: '弃置', reorder: '排序', 'game-over': '胜负', 'extra-turn': '追加回合',
}
function eventLabel(event: ActionEvent) { return eventLabels[event.type] ?? '记录' }
const combat = computed(() => {
  const pending = props.game.pendingDefense
  if (!pending) return null
  const attackerOwner = props.game.players[pending.attackerPlayer]
  const targetOwner = props.game.players[1 - pending.attackerPlayer]
  const attacker = attackerOwner.field.flat().find(card => card?.instanceId === pending.attackerInstanceId)
  if (!attacker) return null
  const target = pending.target.type === 'master'
    ? null : targetOwner.field.flat().find(card => card?.instanceId === pending.target.instanceId)
  const support = supportId.value ? me.value.field.flat().find(card => card?.instanceId === supportId.value) : null
  return {
    attacker, target, attackerOwner, targetOwner, support,
    targetName: target?.name ?? targetOwner.master.masterName,
    targetValue: target ? target.troops + (support?.troops ?? 0) : targetOwner.master.hp,
    targetUnit: target ? '兵力' : '血量',
  }
})
const eligibleSupportId = computed(() => {
  if (defenseTargetType.value !== 'legion') return null
  const targetId = props.game.pendingDefense?.target.instanceId
  for (let slot = 0; slot < 3; slot++) {
    const target = me.value.field[0][slot]
    const support = me.value.field[1][slot]
    const attacker = enemy.value.field.flat().find(card => card?.instanceId === props.game.pendingDefense?.attackerInstanceId)
    if (!target || !support || !attacker || target.instanceId !== targetId) continue
    if (target.troops + support.troops >= attacker.troops) return support.instanceId
  }
  return null
})

function updateScale() {
  if (props.embedded) { compactViewport.value = false; scale.value = 1; return }
  compactViewport.value = window.innerWidth < 820
  scale.value = compactViewport.value
    ? Math.max(.78, Math.min(1, window.innerHeight / 900))
    : Math.min(window.innerWidth / 1440, window.innerHeight / 900)
}
onMounted(() => { updateScale(); window.addEventListener('resize', updateScale) })
onBeforeUnmount(() => window.removeEventListener('resize', updateScale))

function command(type: string, extra: Record<string, unknown> = {}) {
  if (props.readOnly) return
  if (l12State.pendingAction) return
  if (type === 'mulligan') extra.cardInstanceIds = mulliganIds.value
  if (type === 'resolveDefense') {
    extra.cardInstanceIds = defenseIds.value
    if (supportId.value && !Object.prototype.hasOwnProperty.call(extra, 'supportInstanceId')) extra.supportInstanceId = supportId.value
  }
  gameAction({ type, ...extra })
  if (type === 'resolveDefense') { defenseIds.value = []; supportId.value = null }
}
function toggle(list: string[], id: string) {
  const index = list.indexOf(id)
  if (index >= 0) list.splice(index, 1); else list.push(id)
}
function selectHand(card: Card) {
  focusCard.value = card
  if (props.game.phase === 'Mulligan') return toggle(mulliganIds.value, card.instanceId)
  if (props.game.phase === 'Defense' && defenseTargetType.value === 'master') return toggle(defenseIds.value, card.instanceId)
  selectedId.value = selectedId.value === card.instanceId ? null : card.instanceId
  mode.value = 'play'
  playArmed.value = selectedId.value === card.instanceId && (card.cardType === 'legion' || isCounter(card)) && playableIds.value.includes(card.instanceId)
}
function ownSlot(row: number, slot: number, card: Card | null) {
  if (boardSlotPrompt.value) {
    const choice = `${row}:${slot}`
    if (!card && boardSlotPrompt.value.validChoices.includes(choice))
      command('resolvePrompt', { promptId: boardSlotPrompt.value.promptId, choice })
    return
  }
  if (boardTargetPrompt.value) { if (card) selectBoardTarget(card); return }
  if (props.game.phase === 'Defense' && defenseTargetType.value === 'legion') {
    if (row === 1 && card?.instanceId === eligibleSupportId.value) {
      supportId.value = supportId.value === card.instanceId ? null : card.instanceId
      focusCard.value = card
    }
    return
  }
  if (card && mode.value === 'play' && playArmed.value && selectedHandCard.value?.cardType === 'legion'
    && row === 1 && isCounter(card)) {
    command('playCard', { cardInstanceId: selectedHandCard.value.instanceId, row, slot })
    selectedId.value = null
    playArmed.value = false
    return
  }
  if (card) {
    selectedId.value = selectedId.value === card.instanceId ? null : card.instanceId
    focusCard.value = card
    mode.value = 'play'
    playArmed.value = false
    return
  }
  if (!selectedId.value) return
  if (mode.value === 'play' && !playArmed.value) return
  command(mode.value === 'move' ? 'move' : 'playCard', { cardInstanceId: selectedId.value, row, slot })
  selectedId.value = null
  playArmed.value = false
}
function enemySlot(_row: number, _slot: number, card: Card | null) {
  if (card) focusCard.value = card
  if (boardTargetPrompt.value) { if (card) selectBoardTarget(card); return }
  if (mode.value !== 'attack' || !selectedId.value || !card || !selectedAttackTargets.value.includes(card.instanceId)) return
  command('attack', { cardInstanceId: selectedId.value, target: { type: 'legion', instanceId: card.instanceId } })
  selectedId.value = null
}
function selectBoardTarget(card: Card) {
  const prompt = boardTargetPrompt.value
  if (!prompt || !boardTargetableIds.value.includes(card.instanceId)) return
  focusCard.value = card
  const index = boardTargetIds.value.indexOf(card.instanceId)
  if (index >= 0) boardTargetIds.value.splice(index, 1)
  else if (prompt.maxChoose === 1) boardTargetIds.value = [card.instanceId]
  else if (boardTargetIds.value.length < prompt.maxChoose) boardTargetIds.value.push(card.instanceId)
}
function resolveBoardTarget(skip = false) {
  const prompt = boardTargetPrompt.value
  if (!prompt) return
  const ids = skip ? ['skip'] : [...boardTargetIds.value]
  if (!skip && (ids.length < prompt.minChoose || ids.length > prompt.maxChoose)) return
  command('resolvePrompt', { promptId: prompt.promptId, cardInstanceIds: ids })
}
function attackMaster() {
  if (mode.value !== 'attack' || !selectedId.value || !selectedAttackTargets.value.includes('master')) return
  command('attack', { cardInstanceId: selectedId.value, target: { type: 'master' } })
  selectedId.value = null
}
function enemyMaster() {
  if (mode.value === 'attack' && selectedId.value) return attackMaster()
  masterPlayerIndex.value = enemy.value.playerIndex
}
function activateMaster(ability: string) {
  command('activateAbility', { cardInstanceId: me.value.master.masterId, ability })
  masterPlayerIndex.value = null
}
type EventPart = { kind: 'text'; text: string } | { kind: 'card'; card: Card }
function eventParts(event: ActionEvent): EventPart[] {
  const cards = [...new Map((event.cards ?? []).map(card => [card.name, card])).values()]
  if (!cards.length) return [{ kind: 'text', text: event.text }]
  const names = cards.map(card => card.name).sort((a, b) => b.length - a.length)
  const escaped = names.map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  const pattern = new RegExp(`(${escaped.join('|')})`, 'g')
  const byName = new Map(cards.map(card => [card.name, card]))
  const parts: EventPart[] = event.text.split(pattern).filter(Boolean).map(text => {
    const card = byName.get(text)
    return card ? { kind: 'card', card } : { kind: 'text', text }
  })
  for (const card of cards) {
    if (event.text.includes(card.name)) continue
    parts.push({ kind: 'text', text: ' · ' }, { kind: 'card', card })
  }
  return parts
}
function playFromHand(card: Card) {
  if (!playableIds.value.includes(card.instanceId)) return
  selectedId.value = card.instanceId
  focusCard.value = card
  mode.value = 'play'
  if (card.cardType === 'legion' || isCounter(card)) {
    playArmed.value = true
    return
  }
  command('playCard', { cardInstanceId: card.instanceId })
  selectedId.value = null
}
function fieldAction(action: 'attack' | 'move', card: Card) {
  selectedId.value = card.instanceId
  focusCard.value = card
  mode.value = action
  playArmed.value = false
}
function selectPublicCard(card: Card) {
  selectedId.value = selectedId.value === card.instanceId ? null : card.instanceId
  focusCard.value = card
  mode.value = 'play'
}
function activateAbility(card: Card, ability: string) {
  command(ability === 'flipHidden' ? 'flipHidden' : 'activateAbility', { cardInstanceId: card.instanceId, ability })
  selectedId.value = null
}
function activateFactionAbility(ability: string) {
  command('activateAbility', { cardInstanceId: `faction-${me.value.playerIndex}`, ability })
}
function statusTexts(card: Card) {
  const statuses: string[] = []
  if (card.hasStrongAttack) statuses.push('强攻：进攻主宰时额外造成 1 点伤害。')
  if (card.hasSureHit) statuses.push('必中：进攻不可被抵挡或支援。')
  if ((card.immortalUses ?? 0) > 0) statuses.push(`免死：剩余 ${card.immortalUses} 次。`)
  if (card.hasCharge) statuses.push('冲锋：登场回合可以进攻。')
  if (card.cannotAttack) statuses.push('当前不能进攻。')
  if (card.cannotSupport) statuses.push('当前不能支援。')
  for (const modifier of card.timedModifiers ?? []) {
    if (modifier.troopsDelta) statuses.push(`${modifier.source}：兵力${modifier.troopsDelta > 0 ? '+' : ''}${modifier.troopsDelta}。`)
    if (modifier.costDelta) statuses.push(`${modifier.source}：费用${modifier.costDelta > 0 ? '+' : ''}${modifier.costDelta}。`)
  }
  return statuses
}
</script>

<template>
  <div class="board-viewport" :class="{ 'compact-viewport': compactViewport, 'embedded-replay': embedded, 'read-only-board': readOnly }">
    <div class="board-stage" :style="{ transform: `scale(${scale})` }">
      <div class="stage-layout">
        <aside class="board-rail left-rail">
          <section v-if="sessionDisasters.length" class="grand-panel session-disaster-panel" aria-label="本局天灾">
            <h3>本局天灾</h3>
            <div class="session-disaster-strip">
              <button v-for="card in sessionDisasters" :key="card.instanceId" :class="{ hidden: card.hidden }"
                :disabled="card.hidden" @click="focusSessionDisaster(card)" @mouseenter="focusSessionDisaster(card)">
                <img :src="card.imageUrl || '/assets/l12/disaster-back.png'" :alt="card.name || '未揭示天灾'"/>
                <span>{{ card.name || '未揭示' }}</span>
              </button>
            </div>
          </section>
          <section class="grand-panel card-inspector">
            <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
            <h3>选中卡牌</h3>
            <template v-if="focusCard">
              <CardTile :card="focusCard" />
              <h2>{{ focusCard.name }}</h2>
              <dl><div><dt>费用</dt><dd>{{ focusCard.cost }}</dd></div><div><dt>兵力</dt><dd>{{ focusCard.troops || '—' }}</dd></div><div><dt>天灾等级</dt><dd>{{ focusCard.disasterLevel || '—' }}</dd></div></dl>
              <p class="inspector-effect">{{ focusCard.effectText || '无效果文字' }}</p>
              <ul v-if="statusTexts(focusCard).length" class="inspector-statuses"><li v-for="text in statusTexts(focusCard)" :key="text">{{ text }}</li></ul>
            </template>
            <div v-else class="empty-inspector">悬停或选择卡牌<br/>查看数值</div>
          </section>
        </aside>

        <main class="board-center">
          <HandArea hidden :count="enemy.handCount || 0" />
          <div class="felt-board">
            <PlayerMat :player="enemy" side="opponent" :active="game.activePlayer === enemy.playerIndex && !combat"
              :attack-mode="mode === 'attack' && Boolean(selectedId)" :selection-mode="Boolean(boardTargetPrompt)"
              :targetable-ids="boardTargetPrompt ? boardTargetableIds : selectedAttackTargets"
              :combat-attacker-id="combat?.attackerOwner.playerIndex === enemy.playerIndex ? combat.attacker.instanceId : null"
              :combat-target-id="combat?.targetOwner.playerIndex === enemy.playerIndex ? combat.target?.instanceId : null"
              :combat-target-master="combat?.targetOwner.playerIndex === enemy.playerIndex && !combat.target"
              :master-targetable="selectedAttackTargets.includes('master')" @slot="enemySlot" @master="enemyMaster"
              @focus="focusCard = $event" @graveyard="graveyardPlayer = $event" />
            <div class="board-seam">
              <div class="disaster-zone" @mouseenter="game.activeDisaster && (focusCard = game.activeDisaster)" @click="game.activeDisaster && (focusCard = game.activeDisaster)">
                <img class="disaster-card-image"
                  :src="game.activeDisaster?.imageUrl || '/assets/l12/card-back-disaster.png'"
                  :alt="game.activeDisaster?.name || '天灾牌背'" />
                <span class="disaster-value"><img src="/assets/l12/disaster-icon-source.png" alt="天灾"/><b>{{ game.disasterValue }}</b></span>
              </div>
              <PhaseTrack :phase="phasePlaybackPhase ?? game.phase" :round="game.round" :active-side="game.activePlayer === game.you ? 'my' : 'opponent'" />
            </div>
            <PhasePlayback :events="game.recentEvents ?? []" @phase-change="phasePlaybackPhase = $event" />
            <div v-if="combat" class="combat-presentation">
              <i class="combat-trace"/>
              <div class="combat-versus">
                <span :class="combat.attackerOwner.playerIndex === game.you ? 'mine' : 'opponent'">{{ combat.attackerOwner.playerIndex === game.you ? '我方' : '对手' }} · {{ combat.attacker.name }}</span>
                <b>{{ combat.attacker.troops }}<small>兵力</small></b>
                <em>⚔</em>
                <span :class="combat.targetOwner.playerIndex === game.you ? 'mine' : 'opponent'">{{ combat.targetOwner.playerIndex === game.you ? '我方' : '对手' }} · {{ combat.targetName }}</span>
                <b>{{ combat.targetValue }}<small>{{ combat.targetUnit }}</small></b>
              </div>
              <div v-if="game.phase === 'Defense' && !readOnly" class="combat-resolution-panel">
                <GameActions :game="game" :me="me" :mode="mode" :selected-id="selectedId"
                  :mulligan-count="mulliganIds.length" :defense-count="defenseIds.length" :defense-target-type="defenseTargetType"
                  :support-id="supportId" :can-support="Boolean(eligibleSupportId)" :busy="l12State.pendingAction" @command="command" />
              </div>
            </div>
            <PlayerMat :player="me" side="my" :active="game.activePlayer === me.playerIndex && !combat"
              :selected-id="supportId || selectedId" :move-mode="mode === 'move'"
              :placement-mode="mode === 'play' && playArmed && (selectedHandCard?.cardType === 'legion' || isCounter(selectedHandCard))"
              :placement-can-replace-counter="selectedHandCard?.cardType === 'legion'"
              :placement-row="isCounter(selectedHandCard) ? 1 : null" :actions-enabled="!readOnly && isMyMain && !l12State.pendingAction" :round="game.round"
              :attackable-ids="attackableIds" :response-playable-ids="responsePlayableIds"
              :selection-mode="Boolean(boardTargetPrompt || boardSlotPrompt)" :targetable-ids="boardTargetableIds" :prompt-slot-ids="boardSlotPrompt?.validChoices ?? []"
              :combat-attacker-id="combat?.attackerOwner.playerIndex === me.playerIndex ? combat.attacker.instanceId : null"
              :combat-target-id="combat?.targetOwner.playerIndex === me.playerIndex ? combat.target?.instanceId : null"
              :combat-target-master="combat?.targetOwner.playerIndex === me.playerIndex && !combat.target"
              @slot="ownSlot" @master="masterPlayerIndex = me.playerIndex" @focus="focusCard = $event" @graveyard="graveyardPlayer = $event" @card-action="fieldAction"
              @select-card="selectPublicCard" @ability="activateAbility" @faction-ability="activateFactionAbility" />
          </div>
          <HandArea :cards="me.hand" :selected-ids="game.phase === 'Mulligan' ? mulliganIds : game.phase === 'Defense' ? defenseIds : selectedId ? [selectedId] : []"
            :playable-ids="l12State.pendingAction ? [] : handPlayableIds" :dim-unplayable="game.phase !== 'Mulligan'"
            :show-play-action="isMyMain && !l12State.pendingAction" @select="selectHand" @play="playFromHand" @focus="focusCard = $event" />
        </main>

        <aside class="board-rail right-rail">
          <section class="grand-panel player-panel">
            <h3>对手</h3><strong>{{ enemy.name }}</strong><span>{{ enemy.master.masterName }} · 血量 {{ enemy.master.hp }}</span>
            <hr/><h3>我方</h3><strong class="mine">{{ me.name }}</strong><span>{{ me.master.masterName }} · 血量 {{ me.master.hp }}</span>
          </section>
          <section class="grand-panel log-panel record-log"><h3>对局记录</h3>
            <div class="event-list">
              <p v-for="event in events" :key="event.sequence"
                :class="[`event-${event.type}`, { mine: event.playerIndex === game.you, opponent: event.playerIndex !== null && event.playerIndex !== undefined && event.playerIndex !== game.you }]">
                <template v-if="event.type === 'turn-start'"><strong class="turn-divider">—— {{ event.text }} ——</strong></template>
                <template v-else>
                <b class="event-tag">{{ eventLabel(event) }}</b>
                <template v-for="(part, index) in eventParts(event)" :key="index">
                  <span v-if="part.kind === 'text'">{{ part.text }}</span>
                  <button v-else class="log-card-link" @click="focusCard = part.card">{{ part.card.name }}</button>
                </template>
                </template>
              </p>
              <p v-if="!events.length" class="empty-log">等待对局开始</p>
            </div>
            <small>房间 {{ game.roomCode }} · REV {{ game.revision }}<br/>MATCH {{ game.matchId.slice(0,10) }}</small>
          </section>
          <section v-if="!combat && !readOnly" class="grand-panel action-panel"><h3>操作</h3><GameActions :game="game" :me="me" :mode="mode" :selected-id="selectedId"
            :mulligan-count="mulliganIds.length" :defense-count="defenseIds.length" :defense-target-type="defenseTargetType"
            :support-id="supportId" :can-support="Boolean(eligibleSupportId)" :busy="l12State.pendingAction" @command="command" /></section>
        </aside>
      </div>
      <GraveyardOverlay v-if="graveyardPlayer !== null" :players="[me, enemy]" :initial-player="graveyardPlayer"
        @close="graveyardPlayer = null" @focus="focusCard = $event" />
      <MasterOverlay v-if="masterPlayerIndex !== null" :player="game.players[masterPlayerIndex]" :mine="masterPlayerIndex === game.you"
        :can-activate="!readOnly && masterPlayerIndex === game.you && isMyMain" :busy="l12State.pendingAction" @close="masterPlayerIndex = null" @activate="activateMaster" />
      <div v-if="boardTargetPrompt && !readOnly" class="board-target-controls">
        <strong>{{ boardTargetPrompt.text }}</strong><span>已选择 {{ boardTargetIds.length }}/{{ boardTargetPrompt.maxChoose }}</span>
        <button v-if="boardTargetPrompt.validChoices.includes('skip')" @click="resolveBoardTarget(true)">不发动</button>
        <button class="primary" :disabled="boardTargetIds.length < boardTargetPrompt.minChoose" @click="resolveBoardTarget(false)">确认发动</button>
      </div>
      <div v-if="boardSlotPrompt && !readOnly" class="board-target-controls board-slot-controls">
        <img v-if="boardSlotPreview?.imageUrl" :src="boardSlotPreview.imageUrl" :alt="boardSlotPreview.name"
          @mouseenter="focusCard = boardSlotPreview" @click="focusCard = boardSlotPreview" />
        <strong>{{ boardSlotPrompt.text }}</strong><span>直接点击绿色高亮空位</span>
      </div>
      <PromptOverlay v-if="!readOnly || game.phase === 'DisasterPreparation'" :game="game" :read-only="readOnly" :suppressed-prompt-id="activeBoardPromptId" :suppress-defense-wait="Boolean(combat)" :mulligan-selected-ids="mulliganIds" :busy="l12State.pendingAction"
        @focus-card="focusCard = $event" @mulligan-toggle="toggle(mulliganIds, $event)" @mulligan-confirm="command('mulligan')" />
    </div>
    <Teleport to="body">
      <aside v-if="modalInspectorVisible && focusCard" class="modal-card-inspector" :class="{ disaster: isHorizontalCardType(focusCard.cardType) }">
        <img v-if="focusCard.imageUrl" :src="focusCard.imageUrl" :alt="focusCard.name" />
        <div><small>选中卡牌</small><h2>{{ focusCard.name }}</h2>
          <dl><span v-if="focusCard.cost !== undefined">费用 <b>{{ focusCard.currentCost ?? focusCard.cost }}</b></span><span v-if="focusCard.troops">兵力 <b>{{ focusCard.troops }}</b></span><span v-if="focusCard.disasterLevel">天灾等级 <b>{{ focusCard.disasterLevel }}</b></span></dl>
          <p>{{ focusCard.effectText || '无效果文字' }}</p>
          <ul v-if="statusTexts(focusCard).length"><li v-for="text in statusTexts(focusCard)" :key="text">{{ text }}</li></ul>
        </div>
      </aside>
    </Teleport>
  </div>
</template>

<style scoped>
.session-disaster-panel{flex:none;padding:9px 10px}.session-disaster-panel h3{margin:0 0 7px}.session-disaster-strip{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:5px}.session-disaster-strip button{min-width:0;padding:2px;border:1px solid #59625f;background:#070a0b;color:#d9ddd8;cursor:pointer}.session-disaster-strip button.hidden{border-color:#343b39;cursor:default}.session-disaster-strip img{display:block;width:100%;height:auto;aspect-ratio:8/5;object-fit:contain}.session-disaster-strip span{display:block;overflow:hidden;padding:2px 2px 1px;font-size:7px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}.session-disaster-strip button:not(.hidden):hover{border-color:#73d4c5;box-shadow:0 0 8px rgba(115,212,197,.3)}
.combat-presentation{position:absolute;z-index:20;left:50%;top:50%;width:760px;height:1px;transform:translate(-50%,-50%);pointer-events:none}.combat-trace{position:absolute;left:50%;top:-108px;width:4px;height:216px;background:linear-gradient(transparent,#d88a39 20%,#f0ba66 50%,#d88a39 80%,transparent);filter:drop-shadow(0 0 7px #c36b26);transform:rotate(-10deg)}.combat-versus{position:absolute;left:50%;top:0;display:flex;width:max-content;max-width:760px;align-items:center;gap:12px;padding:10px 18px;border:1px solid #8e7650;background:rgba(7,9,10,.95);box-shadow:0 8px 26px #000;transform:translate(-50%,-50%);font-weight:900}.combat-versus span{max-width:190px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.combat-versus span.mine{color:#74d0d3}.combat-versus span.opponent{color:#e6757c}.combat-versus>b{display:flex;align-items:baseline;gap:4px;padding:4px 7px;background:#342a25;color:#fff}.combat-versus b small{color:#c8bba3;font-size:7px}.combat-versus em{color:#e5bd60;font-size:18px;font-style:normal}.combat-resolution-panel{position:absolute;left:50%;top:34px;width:390px;padding:10px 12px;border:1px solid #8e7650;background:rgba(8,11,12,.96);box-shadow:0 12px 30px #000;transform:translateX(-50%);pointer-events:auto}.combat-resolution-panel :deep(.l12-actions){gap:6px}.combat-resolution-panel :deep(.l12-actions p){margin:0;font-size:10px}.combat-resolution-panel :deep(.l12-actions button){padding:7px 9px}
.record-log .event-list p{display:flex;align-items:flex-start;gap:5px;margin:0 0 7px}.record-log .event-list p.event-turn-start{display:block;padding:4px 0;text-align:center}.turn-divider{color:#e0b641;font-size:10px;white-space:nowrap}.event-tag{flex:none;padding:2px 4px;border:1px solid #5c4a86;color:#cbaaff;font-size:8px;line-height:1.25}.event-play .event-tag,.event-put .event-tag{border-color:#126f82;color:#5fd5e2}.event-attack .event-tag,.event-combat .event-tag{border-color:#8d2942;color:#ff6687}.event-response .event-tag,.event-defense .event-tag,.event-support .event-tag{border-color:#9a501b;color:#f0a45e}.event-disaster .event-tag,.event-disaster-active .event-tag,.event-disaster-value .event-tag{border-color:#9e722b;color:#efc15b}.event-damage .event-tag,.event-leave .event-tag{border-color:#813c40;color:#dd7c81}.event-move .event-tag{border-color:#26757c;color:#65cbd0}
.disaster-zone {
  left: 53px;
  width: 112px;
  height: auto;
  aspect-ratio: 8 / 5;
  border-width: 2px;
  box-shadow: 0 8px 22px #000, 0 0 0 1px rgba(238,238,228,.2);
  transition: transform .16s, box-shadow .16s;
}
.disaster-zone:hover {
  z-index: 30;
  transform: translate(-50%,-50%);
  box-shadow: 0 12px 30px #000, 0 0 15px rgba(214,66,77,.4);
}
.disaster-card-image { object-fit: contain; }
.disaster-value {
  left: 154px;
  right: auto;
  transform: translate(-50%,-50%);
}
.disaster-value b { font-size: 17px; }
.board-target-controls{position:fixed;z-index:1050;left:50%;top:76px;display:flex;align-items:center;gap:10px;max-width:760px;padding:10px 13px;border:1px solid #70d7df;background:#091011;box-shadow:0 14px 36px #000;transform:translateX(-50%)}.board-target-controls strong{max-width:430px;color:#fff;font-size:11px}.board-target-controls span{color:#8f9894;font-size:9px}.board-target-controls button{padding:7px 12px;border:1px solid #999;background:#1b2020;color:#fff;font-weight:900}.board-target-controls button.primary{border-color:#72e09a;background:#174d2d}.board-target-controls button:disabled{opacity:.38}
.board-slot-controls img{width:52px;height:72px;object-fit:contain;background:#050708;cursor:pointer}.board-slot-controls span{color:#72e09a;font-weight:900}
.inspector-statuses{display:grid;gap:4px;margin:8px 0 0;padding:0;list-style:none}.inspector-statuses li{padding:4px 6px;border-left:2px solid #70d7df;background:rgba(112,215,223,.08);color:#d9ddd7;font-size:8px;font-weight:800;line-height:1.45}
.modal-card-inspector{position:fixed;z-index:1300;left:18px;top:50%;display:grid;width:270px;max-height:calc(100vh - 36px);grid-template-rows:auto 1fr;gap:10px;box-sizing:border-box;padding:12px;border:2px solid #ded9cc;background:#080d0e;box-shadow:0 20px 55px #000;transform:translateY(-50%);pointer-events:none}.modal-card-inspector>img{width:138px;height:193px;margin:auto;object-fit:contain;background:#030506}.modal-card-inspector.disaster{width:430px}.modal-card-inspector.disaster>img{width:400px;height:auto;aspect-ratio:8/5;object-fit:contain}.modal-card-inspector small{color:#70d7df;font-size:9px;font-weight:900;letter-spacing:.16em}.modal-card-inspector h2{margin:5px 0 8px;color:#fff;font:900 19px/1.25 'Microsoft YaHei',sans-serif}.modal-card-inspector dl{display:flex;flex-wrap:wrap;gap:5px;margin:0 0 8px}.modal-card-inspector dl span{padding:4px 6px;background:#252b29;color:#d8ddd8;font-size:9px}.modal-card-inspector dl b{color:#fff}.modal-card-inspector p{max-height:160px;margin:0;overflow:auto;color:#f0eee7;font-size:11px;font-weight:800;line-height:1.7;white-space:pre-wrap}.modal-card-inspector ul{display:grid;gap:4px;margin:8px 0 0;padding:0;list-style:none}.modal-card-inspector li{padding:4px 6px;border-left:2px solid #70d7df;background:#132326;color:#fff;font-size:9px;font-weight:800}@media(max-width:760px){.modal-card-inspector{left:8px;top:auto;bottom:8px;width:min(250px,calc(100vw - 16px));max-height:42vh;grid-template-columns:72px 1fr;grid-template-rows:1fr;transform:none}.modal-card-inspector>img{width:68px;height:96px}.modal-card-inspector.disaster{width:calc(100vw - 16px);grid-template-columns:minmax(120px,38vw) 1fr}.modal-card-inspector.disaster>img{width:100%;height:auto;aspect-ratio:8/5}.modal-card-inspector p{max-height:80px}}
</style>
