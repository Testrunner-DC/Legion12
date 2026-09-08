<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ActionEvent, Card, DisasterCardView, GameState, Phase } from '../types'
import { isHorizontalCardType } from '../cardPresentation'
import { destructionRoundBackUrl, disasterRoundUrl } from '../specialAssets'
import { gameAction, gmAction, l12State, sandboxAction } from '../net'
import GameActions from './GameActions.vue'
import BattleEventLog from './BattleEventLog.vue'
import BattleUtilityDock from './BattleUtilityDock.vue'
import ActionPresentationLayer from './ActionPresentationLayer.vue'
import ZoneMovementPresentationLayer from './ZoneMovementPresentationLayer.vue'
import CombatMotionPresentationLayer from './CombatMotionPresentationLayer.vue'
import GraveyardOverlay from './GraveyardOverlay.vue'
import HandArea from './HandArea.vue'
import { l12AnimationDuration } from '../audioPreferences'
import MasterOverlay from './MasterOverlay.vue'
import PhaseTrack from './PhaseTrack.vue'
import PlayerMat from './PlayerMat.vue'
import PlayerTurnClock from './PlayerTurnClock.vue'
import PhasePlayback from './PhasePlayback.vue'
import PromptOverlay from './PromptOverlay.vue'
import SandboxCardPicker, { type SandboxCatalogCard } from './SandboxCardPicker.vue'
import CardImage from '../CardImage.vue'
import RankedIdentityBadge from '../RankedIdentityBadge.vue'
import { getFactionPresentation } from '../factionPresentation'

type GmPlacementRequest = {
  type: 'placeCard' | 'playHandCard'
  targetPlayer: number
  cardId?: string
  cardInstanceId?: string
  cardName: string
  cardType: string
  triggerEffects: boolean
}
const props = withDefaults(defineProps<{ game: GameState; readOnly?: boolean; replayFocusCard?: Card | null; gmPlacement?: GmPlacementRequest | null; gmPanelOpen?: boolean }>(), { readOnly: false, replayFocusCard: null, gmPlacement: null, gmPanelOpen: false })
const emit = defineEmits<{ gmPlacementResolved: []; settings: [] }>()
const scale = ref(1)
const stageSize = computed(() => l12State.gmEnabled
  ? { width: 2304, height: 1296 }
  : { width: 2048, height: 1152 })
const compactViewport = ref(false)
const selectedId = ref<string | null>(null)
const focusCard = ref<Card | null>(null)
const inspectorAnchor = ref<HTMLElement | null>(null)
const inspectorFloatStyle = ref<Record<string, string>>({})
watch(() => props.replayFocusCard, card => {
  if (props.readOnly) focusCard.value = card ?? null
}, { immediate: true })
type BoardMode = 'play' | 'attack' | 'move' | 'freeMove' | 'cavalryMove'
const mode = ref<BoardMode>('play')
const mulliganIds = ref<string[]>([])
const defenseIds = ref<string[]>([])
const supportIds = ref<string[]>([])
const graveyardPlayer = ref<number | null>(null)
const playArmed = ref(false)
const masterPlayerIndex = ref<number | null>(null)
const boardTargetIds = ref<string[]>([])
const paymentResourceIds = ref<string[]>([])
const phasePlaybackPhase = ref<Phase | null>(null)
const hiddenRevealCard = ref<Card | null>(null)
const publicReveal = ref<{ sequence: number; cards: Card[]; text: string } | null>(null)
const diceReveal = ref<{ sequence: number; values: number[]; animatedValues: number[]; text: string; settled: boolean } | null>(null)
const customDisasterSlot = ref<number | null>(null)
const promptMinimized = ref(false)
const hasBlockingPrompt = computed(() => Boolean((props.game.prompts?.length ?? 0) > 0 || props.game.waitingPrompt))
const lastHiddenRevealSequence = ref(0)
const lastPublicRevealSequence = ref(0)
const lastDiceSequence = ref(0)
const publicRevealQueue: Array<{ sequence: number; cards: Card[]; text: string }> = []
const diceRevealQueue: Array<{ sequence: number; values: number[]; text: string }> = []
let hiddenRevealTimer: ReturnType<typeof setTimeout> | null = null
let publicRevealTimer: ReturnType<typeof setTimeout> | null = null
let diceRollTimer: ReturnType<typeof setInterval> | null = null
let diceSettleTimer: ReturnType<typeof setTimeout> | null = null
let diceHideTimer: ReturnType<typeof setTimeout> | null = null
const controlledPlayerIndex = computed(() => {
  if (props.readOnly || !l12State.gmEnabled) return props.game.you
  const pendingPrompt = props.game.prompts?.[0]
  if (pendingPrompt) return pendingPrompt.playerIndex
  if (props.game.phase === 'Mulligan')
    return props.game.players.find(player => !player.mulliganDone)?.playerIndex ?? props.game.activePlayer
  if (props.game.phase === 'Defense' && props.game.pendingDefense?.stage === 'DefenseChoice')
    return 1 - props.game.pendingDefense.attackerPlayer
  return props.game.activePlayer
})
const me = computed(() => props.game.players[controlledPlayerIndex.value])
const enemy = computed(() => props.game.players[1 - controlledPlayerIndex.value])
// The sandbox actor may change for prompts, but the observing player's board orientation never changes.
const viewMe = computed(() => props.game.players[props.game.you])
const viewEnemy = computed(() => props.game.players[1 - props.game.you])
const myBadge = computed(() => props.game.playerBadges?.find(item => item.playerIndex === viewMe.value.playerIndex))
const enemyBadge = computed(() => props.game.playerBadges?.find(item => item.playerIndex === viewEnemy.value.playerIndex))
const playerConnection = (playerIndex: number) => {
  const timed = l12State.rankedClock?.players.find(player => player.playerIndex === playerIndex)
  if (timed) return timed.connected
  return l12State.room?.players.find(player => player.playerIndex === playerIndex)?.connected ?? null
}
const connectionLabel = (playerIndex: number) => {
  const connected = playerConnection(playerIndex)
  return connected === null ? (props.readOnly ? '记录快照' : '状态同步中') : connected ? '在线' : '已断开'
}
const factionLabel = (faction: string) => getFactionPresentation(faction).label
function isControlledPlayer(playerIndex: number) { return playerIndex === controlledPlayerIndex.value }
const defenseTargetType = computed(() => props.game.pendingDefense?.stage === 'DefenseChoice'
  ? props.game.pendingDefense.target.type : null)
const isMyMain = computed(() => props.game.phase === 'Main' && props.game.activePlayer === controlledPlayerIndex.value)
const activeMorale = computed(() =>
  me.value.morale.filter(card => !card.tapped).length
  + (me.value.temporaryMorale ?? 0)
  + (me.value.faction === 'taiyangcheng' ? me.value.field.flat().filter(card => card?.cardId === 'S01-0212' && !card.tapped).length : 0),
)
const counterIds = new Set([
  'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
  'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420',
  'S02-0015', 'S02-0016', 'S02-0017', 'S02-0018',
  'S02-0523',
])
const isCounter = (card?: Card | null) => Boolean(card && (card.cardType === 'counter-tactic' || counterIds.has(card.cardId)))
const isInfiltrator = (card?: Card | null) => card?.cardId === 'S01-0004'
const promotionFoundationIdsFor = (card: Card) => me.value.promotionOptions?.[card.instanceId] ?? []
const canPromote = (card: Card) => promotionFoundationIdsFor(card).length > 0
const playableIds = computed(() => {
  if (!isMyMain.value || hasBlockingPrompt.value) return []
  const hasLegionDestination = me.value.field.some((row, rowIndex) => row.some(card => !card || (rowIndex === 1 && isCounter(card))))
  const hasInfiltratorDestination = hasLegionDestination || enemy.value.field.some(row => row.some(card => !card))
  return (me.value.hand ?? [])
    .filter(card => !card.playBlockedReason)
    .filter(card => !(props.game.activeDisaster?.cardId === 'S02-DS01' && card.cardType === 'legion'
      && card.profession && card.profession === me.value.libraryTop?.profession))
    .filter(card => canPromote(card) || ((card.minimumPlayCost ?? card.playCost ?? card.currentCost ?? card.cost) <= activeMorale.value
      && (card.cardType !== 'legion' || (isInfiltrator(card) ? hasInfiltratorDestination : hasLegionDestination))))
    .map(card => card.instanceId)
})
const selectedHandCard = computed(() => me.value.hand?.find(card => card.instanceId === selectedId.value) ?? null)
const promotionFoundationTargetIds = computed(() => {
  const card = selectedHandCard.value
  return card ? promotionFoundationIdsFor(card) : []
})
const selectedAttackTargets = computed(() => selectedId.value ? (props.game.legalAttackTargets?.[selectedId.value] ?? []) : [])
const attackableIds = computed(() => Object.keys(props.game.legalAttackTargets ?? {}))
const responsePlayableIds = computed(() => {
  const response = props.game.prompts?.find(prompt => prompt.kind === 'response')
  return response?.validChoices.filter(id => id !== 'pass') ?? []
})
const handPlayableIds = computed(() => {
  if (isMyMain.value) return playableIds.value
  if (responsePlayableIds.value.length) return responsePlayableIds.value
  if (props.game.phase === 'Defense' && props.game.activePlayer !== controlledPlayerIndex.value && defenseTargetType.value === 'master')
    return (me.value.hand ?? []).filter(card => card.cardType === 'legion').map(card => card.instanceId)
  return []
})
const boardTargetPrompt = computed(() => {
  const fieldIds = new Set(props.game.players.flatMap(player => player.field.flat().filter(Boolean).map(card => card!.instanceId)))
  return props.game.prompts?.find(prompt => {
    if (prompt.data?.choiceMode === 'mixed-board-payment') return true
    if (prompt.data?.choiceMode === 'board-target') return prompt.validChoices.filter(id => id !== 'skip').every(id => fieldIds.has(id))
    if (!['target', 'targets', 'optional-target', 'optional-targets', 'active-target'].includes(prompt.kind)) return false
    const choices = prompt.validChoices.filter(id => id !== 'skip')
    return choices.length > 0 && choices.every(id => fieldIds.has(id))
  }) ?? null
})
const boardTargetableIds = computed(() => boardTargetPrompt.value?.validChoices.filter(id => id !== 'skip') ?? [])
const boardSlotPrompt = computed(() => props.game.prompts?.find(prompt =>
  (prompt.kind === 'slot' || prompt.data?.choiceMode === 'board-slot')
  && prompt.validChoices.some(id => id !== 'skip')
  && prompt.validChoices.filter(id => id !== 'skip').every(id => /^\d+:\d+$/.test(id)),
) ?? null)
const boardSlotTargetPlayerIndex = computed(() => {
  const raw = boardSlotPrompt.value?.data?.targetPlayerIndex
  const parsed = raw === undefined ? Number.NaN : Number(raw)
  return Number.isInteger(parsed) ? parsed : controlledPlayerIndex.value
})
const resourceSelectionPrompt = computed(() => props.game.prompts?.find(prompt =>
  prompt.kind === 'resource-payment' || prompt.data?.choiceMode === 'resource-payment'
  || prompt.kind === 'resource-return' || prompt.data?.choiceMode === 'resource-return'
  || prompt.data?.choiceMode === 'resource-selection' || prompt.data?.choiceMode === 'board-selection'
  || prompt.kind === 'target-morale',
) ?? null)
const paymentChoiceIds = computed(() => (resourceSelectionPrompt.value
  ?? (boardTargetPrompt.value?.data?.choiceMode === 'mixed-board-payment' ? boardTargetPrompt.value : null))
  ?.validChoices.filter(id => id !== 'skip') ?? [])
const activeBoardPromptId = computed(() => boardTargetPrompt.value?.promptId
  ?? boardSlotPrompt.value?.promptId ?? resourceSelectionPrompt.value?.promptId ?? null)
const passivePresentationPaused = computed(() => Boolean(
  publicReveal.value || diceReveal.value || hiddenRevealCard.value || activeBoardPromptId.value,
))
const modalInspectorVisible = computed(() => Boolean(!promptMinimized.value && focusCard.value && (
  graveyardPlayer.value !== null || masterPlayerIndex.value !== null || props.game.phase === 'Mulligan'
  || props.game.phase === 'DisasterPreparation' || props.game.phase === 'Disaster'
  || (props.game.prompts?.length ?? 0) > 0 || props.game.waitingPrompt
)))
function updateInspectorFloatRect() {
  if (!modalInspectorVisible.value || !inspectorAnchor.value) return
  const rect = inspectorAnchor.value.getBoundingClientRect()
  const logicalWidth = inspectorAnchor.value.offsetWidth || rect.width
  const logicalHeight = inspectorAnchor.value.offsetHeight || rect.height
  const floatScale = logicalWidth > 0 ? rect.width / logicalWidth : 1
  inspectorFloatStyle.value = {
    left: `${rect.left}px`,
    top: `${rect.top}px`,
    width: `${logicalWidth}px`,
    height: `${logicalHeight}px`,
    transform: `scale(${floatScale})`,
    '--l12-board-copy': `${13 / Math.min(1, floatScale)}px`,
    '--l12-board-meta': `${11 / Math.min(1, floatScale)}px`,
    '--l12-board-micro': `${9 / Math.min(1, floatScale)}px`,
    '--l12-effect-copy': `${13 / Math.min(1, floatScale)}px`,
  }
}
watch(modalInspectorVisible, visible => {
  if (visible) updateInspectorFloatRect()
}, { flush: 'sync' })
const sessionDisasters = computed(() => props.game.sessionDisasters ?? [])
const osirisVictoryCard = computed(() => me.value.graveyard?.find(card => card.cardId === 'S01-02M2') ?? null)
const osirisVictoryAbility = computed(() => me.value.master.abilities?.find(entry => entry.id === 'isisVictory')
  ?? osirisVictoryCard.value?.abilities?.find(entry => entry.id === 'isisVictory'))
const canActivateOsiris = computed(() => Boolean(!props.readOnly && isMyMain.value && !l12State.pendingAction
  && osirisVictoryCard.value && osirisVictoryAbility.value
  && osirisVictoryAbility.value.enabled !== false && !osirisVictoryAbility.value.triggerOnly))
const osirisVictoryDisabledReason = computed(() => osirisVictoryAbility.value?.disabledReason
  ?? '需要伊西斯、墓地的复苏的奥西里斯与5种已完成的卡诺匹斯圣物')
const sessionDisasterSlots = computed<(DisasterCardView | null)[]>(() =>
  Array.from({ length: 4 }, (_, index) => sessionDisasters.value[index] ?? null),
)
const resolvedDisasterIds = computed(() => new Set((props.game.removedDisasters ?? []).map(card => card.instanceId)))
function sessionDisasterState(card: DisasterCardView | null) {
  if (!card || card.hidden) return 'unrevealed'
  if (card.instanceId === props.game.activeDisaster?.instanceId) return 'active'
  if (resolvedDisasterIds.value.has(card.instanceId)) return 'resolved'
  return 'revealed'
}
function isVisibleDisasterCard(card: DisasterCardView): card is Card {
  return !card.hidden && Boolean(card.cardId && card.name && card.cardType)
}
function focusSessionDisaster(card: DisasterCardView, index?: number) {
  if (isVisibleDisasterCard(card)) focusCard.value = card
  if (index !== undefined && props.game.disasterMode === 'custom' && l12State.gmEnabled && !props.readOnly && index < 3)
    customDisasterSlot.value = index
}
function replaceCustomDisaster(card: SandboxCatalogCard) {
  if (customDisasterSlot.value === null) return
  gmAction({ type: 'replaceDisaster', targetPlayer: controlledPlayerIndex.value, slot: customDisasterSlot.value, cardId: card.id })
  customDisasterSlot.value = null
}
const boardSlotPreview = computed<Card | null>(() => {
  const prompt = boardSlotPrompt.value
  const id = prompt?.data?.previewCardId
  if (!prompt || !id || prompt.data?.previewPresentation !== 'handled-card') return null
  return {
    instanceId: id,
    cardId: prompt.data?.[`${id}:cardId`] ?? '',
    name: prompt.data?.[id] ?? '展示牌',
    cardType: prompt.data?.[`${id}:cardType`] ?? '',
    faction: prompt.data?.[`${id}:faction`] ?? '',
    traits: prompt.data?.[`${id}:traits`]?.split('|').filter(Boolean) ?? [],
    profession: prompt.data?.[`${id}:profession`] || undefined,
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
watch(() => boardTargetPrompt.value?.promptId, () => {
  boardTargetIds.value = boardTargetPrompt.value?.data?.lockedChoices?.split('|').filter(Boolean) ?? []
})
function clearOrdinaryInteractionState() {
  selectedId.value = null
  mode.value = 'play'
  playArmed.value = false
  defenseIds.value = []
  supportIds.value = []
  graveyardPlayer.value = null
  masterPlayerIndex.value = null
  focusCard.value = null
  customDisasterSlot.value = null
}
watch(hasBlockingPrompt, blocking => {
  if (blocking) clearOrdinaryInteractionState()
}, { immediate: true, flush: 'sync' })
watch(activeBoardPromptId, promptId => {
  if (!promptId) return
  graveyardPlayer.value = null
  masterPlayerIndex.value = null
  focusCard.value = null
  customDisasterSlot.value = null
})
watch(() => resourceSelectionPrompt.value?.promptId, () => { paymentResourceIds.value = [] })
watch(controlledPlayerIndex, () => {
  selectedId.value = null
  focusCard.value = null
  mode.value = 'play'
  playArmed.value = false
  mulliganIds.value = []
  defenseIds.value = []
  supportIds.value = []
  boardTargetIds.value = []
  paymentResourceIds.value = []
  graveyardPlayer.value = null
  masterPlayerIndex.value = null
  promptMinimized.value = false
})
watch(() => props.game.recentEvents?.map(event => event.sequence).join(',') ?? '', () => {
  const event = [...(props.game.recentEvents ?? [])].reverse().find(item => item.type === 'hidden-reveal' && item.cards?.length)
  if (!event?.cards?.[0] || event.sequence <= lastHiddenRevealSequence.value) return
  lastHiddenRevealSequence.value = event.sequence
  hiddenRevealCard.value = event.cards[0]
  if (hiddenRevealTimer) clearTimeout(hiddenRevealTimer)
  hiddenRevealTimer = setTimeout(() => { hiddenRevealCard.value = null }, l12AnimationDuration(3000, 700))
})
watch(() => props.game.recentEvents?.map(event => event.sequence).join(',') ?? '', () => {
  const specialVictory = [...(props.game.recentEvents ?? [])].reverse().find(item => item.type === 'special-victory'
    && item.cards?.some(card => card.cardId === 'S01-02M2'))
  if (!specialVictory) return
  graveyardPlayer.value = null
  masterPlayerIndex.value = null
  focusCard.value = null
  promptMinimized.value = false
})
function showNextPublicReveal() {
  if (publicReveal.value || !publicRevealQueue.length) return
  publicReveal.value = publicRevealQueue.shift() ?? null
  if (!publicReveal.value) return
  if (publicRevealTimer) clearTimeout(publicRevealTimer)
  publicRevealTimer = setTimeout(() => {
    publicReveal.value = null
    publicRevealTimer = null
    showNextPublicReveal()
  }, l12AnimationDuration(3000, 700))
}
function publicRevealText(event: ActionEvent) {
  const text = event.effectText?.trim() || event.text.trim()
  const card = event.cards?.[0]
  if (card && /花魁的馈赠/.test(text)) return `花魁的馈赠将〈${card.name}〉加入手牌`
  return text
}
function diceValuesFromEvent(event: ActionEvent) {
  const result = event.text.match(/结果为\s*([1-6])/)?.[1]
  if (result) return [Number(result)]
  const rollText = event.text.split('掷骰：')[1] ?? ''
  return [...rollText.matchAll(/(?:^|\s)([1-6])(?=，|,|。|$)/g)].map(match => Number(match[1])).slice(-2)
}
function showNextDiceReveal() {
  if (diceReveal.value || !diceRevealQueue.length) return
  const next = diceRevealQueue.shift()
  if (!next) return
  const values = next.values.length ? next.values : [1]
  diceReveal.value = { ...next, values, animatedValues: values.map(() => 1 + Math.floor(Math.random() * 6)), settled: false }
  diceRollTimer = setInterval(() => {
    if (diceReveal.value) diceReveal.value.animatedValues = values.map(() => 1 + Math.floor(Math.random() * 6))
  }, l12AnimationDuration(90, 50))
  diceSettleTimer = setTimeout(() => {
    if (diceRollTimer) clearInterval(diceRollTimer)
    diceRollTimer = null
    if (diceReveal.value) {
      diceReveal.value.animatedValues = [...values]
      diceReveal.value.settled = true
    }
  }, l12AnimationDuration(900, 180))
  diceHideTimer = setTimeout(() => {
    diceReveal.value = null
    diceHideTimer = null
    showNextDiceReveal()
  }, l12AnimationDuration(2200, 700))
}
watch(() => props.game.recentEvents?.map(event => event.sequence).join(',') ?? '', () => {
  const fresh = (props.game.recentEvents ?? [])
    .filter(event => event.cards?.length && event.sequence > lastPublicRevealSequence.value
      && (event.type === 'disaster-reveal' || event.playerIndex === null || event.playerIndex !== props.game.you)
      && (event.type === 'effect-trigger' || event.type === 'effect-response' || event.type === 'effect-activation'
        || event.type === 'reveal' || event.type === 'disaster-reveal' || event.text.includes('展示')
        || (event.type === 'search' && /展示|加入手牌/.test(event.effectText || event.text)))
      && !(event.type === 'effect-trigger' && /展示|公开/.test(event.text)))
    .sort((left, right) => left.sequence - right.sequence)
  for (const event of fresh) {
    publicRevealQueue.push({
      sequence: event.sequence,
      cards: event.cards ?? [],
      text: publicRevealText(event),
    })
    lastPublicRevealSequence.value = Math.max(lastPublicRevealSequence.value, event.sequence)
  }
  showNextPublicReveal()
})
watch(() => props.game.recentEvents?.map(event => event.sequence).join(',') ?? '', () => {
  const fresh = (props.game.recentEvents ?? [])
    .filter(event => event.type === 'dice' && event.sequence > lastDiceSequence.value)
    .sort((left, right) => left.sequence - right.sequence)
  for (const event of fresh) {
    diceRevealQueue.push({ sequence: event.sequence, values: diceValuesFromEvent(event), text: event.text })
    lastDiceSequence.value = Math.max(lastDiceSequence.value, event.sequence)
  }
  showNextDiceReveal()
})
const combat = computed(() => {
  const pending = props.game.pendingDefense
  if (!pending) return null
  const attackerOwner = props.game.players[pending.attackerPlayer]
  const targetOwner = props.game.players[1 - pending.attackerPlayer]
  const attacker = [...attackerOwner.field.flat(), ...(attackerOwner.resolving ?? [])]
    .find(card => card?.instanceId === pending.attackerInstanceId)
  if (!attacker) return null
  const target = pending.target.type === 'master'
    ? null : [...targetOwner.field.flat(), ...(targetOwner.resolving ?? [])]
      .find(card => card?.instanceId === pending.target.instanceId)
  const supports = me.value.field.flat().filter(card => card && supportIds.value.includes(card.instanceId)) as Card[]
  return {
    attacker, target, attackerOwner, targetOwner, supports, stage: pending.stage,
    attackValue: pending.attackValue > 0 ? pending.attackValue : attacker.troops,
    attackUnit: pending.attackValue > 0 ? '冻结进攻值' : '兵力',
    targetName: target?.name ?? targetOwner.master.masterName,
    targetValue: target ? target.troops + supports.reduce((sum, card) => sum + card.troops, 0) : targetOwner.master.hp,
    targetUnit: target ? '兵力' : '血量',
  }
})
const eligibleSupportIds = computed(() => {
  if (defenseTargetType.value !== 'legion') return []
  const targetId = props.game.pendingDefense?.target.instanceId
  const result: string[] = []
  for (let slot = 0; slot < 3; slot++) {
    const target = me.value.field[0][slot]
    if (!target || target.instanceId !== targetId) continue
    me.value.field[1].forEach((support, supportSlot) => {
      if (!support || support.cannotSupport) return
      const hasCooperativeSupport = support.activeKeywords?.includes('协防')
      if (supportSlot === slot || hasCooperativeSupport) result.push(support.instanceId)
    })
  }
  return result
})
const supportReady = computed(() => {
  if (!combat.value || defenseTargetType.value !== 'legion' || supportIds.value.length === 0) return false
  return combat.value.targetValue >= combat.value.attackValue
})

function updateScale() {
  compactViewport.value = window.innerWidth < 820
  // The hand fan and left utility dock paint about 42 logical pixels beyond the stage's
  // nominal 16:9 box. Because the stage is vertically centered below the 52px site bar,
  // reserve that overflow on both edges so every control stays visible at exact 16:9.
  const availableHeight = window.innerHeight - 124
  const availableWidth = window.innerWidth - (props.gmPanelOpen && !compactViewport.value ? 344 : 0)
  scale.value = compactViewport.value
    ? Math.max(.7, Math.min(1, availableHeight / stageSize.value.height))
    : Math.min(availableWidth / stageSize.value.width, availableHeight / stageSize.value.height)
  window.requestAnimationFrame(updateInspectorFloatRect)
}
watch(stageSize, updateScale)
watch(() => props.gmPanelOpen, updateScale)
onMounted(() => {
  lastHiddenRevealSequence.value = Math.max(0, ...(props.game.recentEvents ?? []).map(event => event.sequence))
  lastPublicRevealSequence.value = lastHiddenRevealSequence.value
  lastDiceSequence.value = lastHiddenRevealSequence.value
  updateScale()
  window.addEventListener('resize', updateScale)
})
onBeforeUnmount(() => {
  window.removeEventListener('resize', updateScale)
  if (hiddenRevealTimer) clearTimeout(hiddenRevealTimer)
  if (publicRevealTimer) clearTimeout(publicRevealTimer)
  if (diceRollTimer) clearInterval(diceRollTimer)
  if (diceSettleTimer) clearTimeout(diceSettleTimer)
  if (diceHideTimer) clearTimeout(diceHideTimer)
})

function withPromptBinding(extra: Record<string, unknown>) {
  const promptId = typeof extra.promptId === 'string' ? extra.promptId : ''
  const prompt = props.game.prompts?.find(candidate => candidate.promptId === promptId)
  if (!prompt) return extra
  return {
    ...extra,
    activationId: prompt.activationId,
    sourceInstanceId: prompt.sourceInstanceId,
    sourceCardId: prompt.sourceCardId,
    step: prompt.step,
    createdRevision: prompt.createdRevision,
    controller: prompt.controller,
  }
}
function command(type: string, extra: Record<string, unknown> = {}) {
  if (props.readOnly) return
  if (l12State.pendingAction) return
  if (hasBlockingPrompt.value && type !== 'resolvePrompt') return
  if (type === 'resolvePrompt') extra = withPromptBinding(extra)
  if (type === 'mulligan') extra.cardInstanceIds = mulliganIds.value
  if (type === 'resolveDefense') {
    extra.cardInstanceIds = defenseIds.value
    if (defenseTargetType.value === 'legion') extra.cardInstanceIds = [...supportIds.value]
  }
  if (l12State.gmEnabled) sandboxAction(controlledPlayerIndex.value, { type, ...extra })
  else gameAction({ type, ...extra })
  if (type === 'resolveDefense') { defenseIds.value = []; supportIds.value = [] }
}
function toggle(list: string[], id: string) {
  const index = list.indexOf(id)
  if (index >= 0) list.splice(index, 1); else list.push(id)
}
function selectedHandIdsFor(playerIndex: number) {
  if (!isControlledPlayer(playerIndex)) return []
  if (props.game.phase === 'Mulligan') return mulliganIds.value
  if (props.game.phase === 'Defense' && props.game.pendingDefense?.stage === 'DefenseChoice') return defenseIds.value
  return selectedId.value ? [selectedId.value] : []
}
function playableHandIdsFor(playerIndex: number) {
  return isControlledPlayer(playerIndex) && !l12State.pendingAction ? handPlayableIds.value : []
}
function selectHandFor(playerIndex: number, card: Card) {
  if (isControlledPlayer(playerIndex)) selectHand(card)
  else focusCard.value = card
}
function playFromHandFor(playerIndex: number, card: Card) {
  if (isControlledPlayer(playerIndex)) playFromHand(card)
}
function slotFor(playerIndex: number, row: number, slot: number, card: Card | null) {
  if (props.gmPlacement && props.gmPlacement.targetPlayer === playerIndex) {
    if (card) { focusCard.value = card; return }
    gmAction({
      type: props.gmPlacement.type,
      targetPlayer: playerIndex,
      row,
      slot,
      triggerEffects: props.gmPlacement.triggerEffects,
      ...(props.gmPlacement.cardId ? { cardId: props.gmPlacement.cardId } : {}),
      ...(props.gmPlacement.cardInstanceId ? { cardInstanceId: props.gmPlacement.cardInstanceId } : {}),
    })
    emit('gmPlacementResolved')
    return
  }
  if (isControlledPlayer(playerIndex)) ownSlot(row, slot, card)
  else enemySlot(row, slot, card)
}
function masterFor(playerIndex: number) {
  if (isControlledPlayer(playerIndex)) masterPlayerIndex.value = playerIndex
  else enemyMaster()
}
function fieldActionFor(playerIndex: number, action: 'attack' | 'move' | 'freeMove' | 'cavalryMove', card: Card) {
  if (isControlledPlayer(playerIndex)) fieldAction(action, card)
}
function activateAbilityFor(playerIndex: number, card: Card, ability: string) {
  if (isControlledPlayer(playerIndex)) activateAbility(card, ability)
}
function activateFactionAbilityFor(playerIndex: number, ability: string) {
  if (isControlledPlayer(playerIndex)) activateFactionAbility(ability)
}
function selectPublicCardFor(playerIndex: number, card: Card) {
  focusCard.value = card
  if (isControlledPlayer(playerIndex)) selectPublicCard(card)
}
function targetableIdsFor(playerIndex: number) {
  if (boardTargetPrompt.value) return boardTargetableIds.value
  if (isControlledPlayer(playerIndex) && props.game.phase === 'Defense' && defenseTargetType.value === 'legion')
    return eligibleSupportIds.value
  return isControlledPlayer(playerIndex) ? promotionFoundationTargetIds.value : selectedAttackTargets.value
}
function selectionModeFor(playerIndex: number) {
  return Boolean(boardTargetPrompt.value || (isControlledPlayer(playerIndex)
    && props.game.phase === 'Defense' && defenseTargetType.value === 'legion') || (isControlledPlayer(playerIndex)
    && (boardSlotPrompt.value || promotionFoundationTargetIds.value.length)))
}
function selectHand(card: Card) {
  focusCard.value = card
  if (hasBlockingPrompt.value) return
  if (props.game.phase === 'Mulligan') return toggle(mulliganIds.value, card.instanceId)
  if (props.game.phase === 'Defense' && defenseTargetType.value === 'master') return toggle(defenseIds.value, card.instanceId)
  selectedId.value = selectedId.value === card.instanceId ? null : card.instanceId
  mode.value = 'play'
  playArmed.value = selectedId.value === card.instanceId && (card.cardType === 'legion' || isCounter(card)) && playableIds.value.includes(card.instanceId)
}
function resolveBoardSlotPrompt(playerIndex: number, row: number, slot: number) {
  const prompt = boardSlotPrompt.value
  if (!prompt || boardSlotTargetPlayerIndex.value !== playerIndex) return false
  const choice = `${row}:${slot}`
  if (prompt.validChoices.includes(choice)) command('resolvePrompt', { promptId: prompt.promptId, choice })
  return true
}
function ownSlot(row: number, slot: number, card: Card | null) {
  if (resolveBoardSlotPrompt(me.value.playerIndex, row, slot)) return
  if (resourceSelectionPrompt.value) {
    if (card && paymentChoiceIds.value.includes(card.instanceId)) togglePaymentResource(card.instanceId)
    return
  }
  if (boardTargetPrompt.value) { if (card) selectBoardTarget(card); return }
  if (hasBlockingPrompt.value) { if (card) focusCard.value = card; return }
  if (props.game.phase === 'Defense' && defenseTargetType.value === 'legion') {
    if (row === 1 && card && eligibleSupportIds.value.includes(card.instanceId)) {
      toggle(supportIds.value, card.instanceId)
      focusCard.value = card
    }
    return
  }
  if (card && mode.value === 'play' && playArmed.value && selectedHandCard.value
    && promotionFoundationIdsFor(selectedHandCard.value).includes(card.instanceId)) {
    command('playCard', { cardInstanceId: selectedHandCard.value.instanceId, choice: `promotion:${card.instanceId}` })
    selectedId.value = null
    playArmed.value = false
    return
  }
  if (card && mode.value === 'play' && playArmed.value && selectedHandCard.value?.cardType === 'legion'
    && row === 1 && isCounter(card)) {
    command('playCard', { cardInstanceId: selectedHandCard.value.instanceId, row, slot, targetPlayerIndex: me.value.playerIndex })
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
  const commandType = mode.value === 'freeMove' ? 'move'
    : mode.value === 'move' || mode.value === 'cavalryMove' ? mode.value : 'playCard'
  command(commandType, {
    cardInstanceId: selectedId.value, row, slot,
    ...(mode.value === 'play' ? { targetPlayerIndex: me.value.playerIndex } : {}),
  })
  selectedId.value = null
  mode.value = 'play'
  playArmed.value = false
}
function togglePaymentResource(instanceId: string) {
  const prompt = resourceSelectionPrompt.value
    ?? (boardTargetPrompt.value?.data?.choiceMode === 'mixed-board-payment' ? boardTargetPrompt.value : null)
  if (!prompt || !paymentChoiceIds.value.includes(instanceId)) return
  const selected = prompt.data?.choiceMode === 'mixed-board-payment' ? boardTargetIds.value : paymentResourceIds.value
  if (prompt.data?.lockedChoices?.split('|').includes(instanceId)) return
  const index = selected.indexOf(instanceId)
  if (index >= 0) selected.splice(index, 1)
  else if (selected.length < prompt.maxChoose) selected.push(instanceId)
}
function confirmResourcePayment(skip = false) {
  const prompt = resourceSelectionPrompt.value
  if (!prompt) return
  if (skip) {
    if (!prompt.validChoices.includes('skip')) return
    command('resolvePrompt', { promptId: prompt.promptId, cardInstanceIds: ['skip'] })
    return
  }
  if (paymentResourceIds.value.length < prompt.minChoose || paymentResourceIds.value.length > prompt.maxChoose) return
  command('resolvePrompt', { promptId: prompt.promptId, cardInstanceIds: [...paymentResourceIds.value] })
}
function enemySlot(row: number, slot: number, card: Card | null) {
  if (card) focusCard.value = card
  if (resolveBoardSlotPrompt(enemy.value.playerIndex, row, slot)) return
  if (resourceSelectionPrompt.value) {
    if (card && paymentChoiceIds.value.includes(card.instanceId)) togglePaymentResource(card.instanceId)
    return
  }
  if (boardTargetPrompt.value) { if (card) selectBoardTarget(card); return }
  if (hasBlockingPrompt.value) return
  if (mode.value === 'play' && playArmed.value && selectedHandCard.value && isInfiltrator(selectedHandCard.value) && !card) {
    command('playCard', { cardInstanceId: selectedHandCard.value.instanceId, row, slot, targetPlayerIndex: enemy.value.playerIndex })
    selectedId.value = null
    playArmed.value = false
    return
  }
  if (mode.value === 'play' && card && isInfiltrator(card) && card.ownerIndex === controlledPlayerIndex.value) {
    selectedId.value = selectedId.value === card.instanceId ? null : card.instanceId
    focusCard.value = card
    playArmed.value = false
    return
  }
  if (mode.value !== 'attack' || !selectedId.value || !card || !selectedAttackTargets.value.includes(card.instanceId)) return
  command('attack', { cardInstanceId: selectedId.value, target: { type: 'legion', instanceId: card.instanceId } })
  selectedId.value = null
  mode.value = 'play'
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
  if (hasBlockingPrompt.value) return
  if (mode.value !== 'attack' || !selectedId.value || !selectedAttackTargets.value.includes('master')) return
  command('attack', { cardInstanceId: selectedId.value, target: { type: 'master' } })
  selectedId.value = null
  mode.value = 'play'
}
function enemyMaster() {
  if (hasBlockingPrompt.value) return
  if (mode.value === 'attack' && selectedId.value) return attackMaster()
  masterPlayerIndex.value = enemy.value.playerIndex
}
function activateMaster(ability: string) {
  if (hasBlockingPrompt.value) return
  if (ability === 'isisVictory') return activateOsirisVictory()
  command('activateAbility', { cardInstanceId: me.value.master.masterId, ability })
  masterPlayerIndex.value = null
}
function playFromHand(card: Card) {
  if (hasBlockingPrompt.value) return
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
function fieldAction(action: Exclude<BoardMode, 'play'>, card: Card) {
  if (hasBlockingPrompt.value) return
  selectedId.value = card.instanceId
  focusCard.value = card
  mode.value = action
  playArmed.value = false
}
function cancelLocalAttackSelection() {
  if (mode.value !== 'attack' || combat.value || hasBlockingPrompt.value) return
  selectedId.value = null
  mode.value = 'play'
  playArmed.value = false
}
function selectPublicCard(card: Card) {
  if (hasBlockingPrompt.value) return
  selectedId.value = selectedId.value === card.instanceId ? null : card.instanceId
  focusCard.value = card
  mode.value = 'play'
}
function activateAbility(card: Card, ability: string) {
  if (hasBlockingPrompt.value) return
  if (ability === 'isisVictory') { activateOsirisVictory(); return }
  command('activateAbility', { cardInstanceId: card.instanceId, ability })
  selectedId.value = null
}
function activateOsirisVictory() {
  const osiris = osirisVictoryCard.value
  if (!osiris || !canActivateOsiris.value) return
  graveyardPlayer.value = null
  masterPlayerIndex.value = null
  focusCard.value = null
  promptMinimized.value = false
  selectedId.value = null
  command('activateAbility', { cardInstanceId: osiris.instanceId, ability: 'isisVictory' })
}
function activateFactionAbility(ability: string) {
  if (hasBlockingPrompt.value) return
  command('activateAbility', { cardInstanceId: `faction-${me.value.playerIndex}`, ability })
}
function statusTexts(card: Card) {
  const statuses: string[] = []
  if (card.hasStrongAttack) statuses.push('强攻：进攻主宰时额外造成 1 点伤害。')
  if (card.hasSureHit) statuses.push('必中：进攻不可被抵挡或支援。')
  if (card.hasShock) statuses.push('震击：进攻目标左右相邻的军团本回合兵力-2000。')
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
  <div class="board-viewport" :class="{ 'compact-viewport': compactViewport, 'read-only-board': readOnly, 'gm-panel-docked': gmPanelOpen && !compactViewport }">
    <div class="board-stage" :style="{ width: `${stageSize.width}px`, height: `${stageSize.height}px`, transform: `scale(${scale})`, '--l12-board-copy': `${13 / Math.min(1, scale)}px`, '--l12-board-meta': `${11 / Math.min(1, scale)}px`, '--l12-board-micro': `${9 / Math.min(1, scale)}px`, '--l12-effect-copy': `${13 / Math.min(1, scale)}px` }">
      <div class="stage-layout">
        <aside class="board-rail left-rail">
          <div v-if="sessionDisasters.length" class="left-disaster-row">
            <section class="grand-panel session-disaster-panel" aria-label="本局天灾">
              <h3>本局天灾</h3>
              <div class="session-disaster-strip">
                <button v-for="(card, index) in sessionDisasterSlots" :key="card?.instanceId ?? `hidden-disaster-${index}`"
                  :class="[sessionDisasterState(card), { hidden: !card || card.hidden, replaceable: game.disasterMode === 'custom' && l12State.gmEnabled && index < 3 }]" :disabled="!card || card.hidden" :title="card && !card.hidden ? (game.disasterMode === 'custom' && l12State.gmEnabled && index < 3 ? `${card.name} · 点击更换` : card.name) : '未揭示天灾'"
                  @click="card && focusSessionDisaster(card, index)" @mouseenter="card && isVisibleDisasterCard(card) && (focusCard = card)">
                  <img v-if="!card || card.hidden" :src="destructionRoundBackUrl" alt="未揭示天灾"/>
                  <img v-else :src="disasterRoundUrl(card.cardId, card.imageUrl)" :alt="card.name || '天灾'"/>
                </button>
              </div>
            </section>
            <section class="grand-panel current-disaster-panel" data-ui-contract="left-current-disaster">
                <button type="button" class="current-disaster-card" :disabled="!game.activeDisaster"
                  @mouseenter="game.activeDisaster && (focusCard = game.activeDisaster)" @click="game.activeDisaster && (focusCard = game.activeDisaster)">
                  <CardImage v-if="game.activeDisaster" :card-id="game.activeDisaster.cardId" :legacy-url="game.activeDisaster.imageUrl" :alt="game.activeDisaster.name" intent="board" eager />
                  <img v-else src="/assets/l12/card-back-disaster.png" alt="天灾牌背" />
                </button>
            </section>
          </div>
          <div class="left-detail-layout">
            <div class="left-card-column">
              <div ref="inspectorAnchor" class="card-inspector-anchor" data-ui-contract="selected-card-inspector-anchor">
              <Teleport to="body" :disabled="!modalInspectorVisible">
                <div class="board-rail inspector-style-scope">
                <section class="grand-panel card-inspector" data-ui-contract="selected-card-inspector" :style="modalInspectorVisible ? inspectorFloatStyle : undefined" :class="{ 'card-inspector-floating': modalInspectorVisible, 'horizontal-inspector': focusCard && isHorizontalCardType(focusCard.cardType) }">
                  <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
                  <h3>选中卡牌</h3>
                  <template v-if="focusCard">
                    <CardImage class="inspector-card-image" :card-id="focusCard.cardId" :legacy-url="focusCard.imageUrl" :alt="focusCard.name" intent="detail" eager />
                    <h2>{{ focusCard.name }}</h2>
                    <div v-if="focusCard.traits?.length || focusCard.profession" class="inspector-card-tags">
                      <span v-for="trait in focusCard.traits" :key="trait">{{ trait }}</span><span v-if="focusCard.profession">{{ focusCard.profession }}</span>
                    </div>
                    <div v-if="focusCard.trialValue" class="inspector-card-tags"><span>试炼值 {{ focusCard.trialValue }}</span></div>
                    <p class="inspector-effect l12-effect-body l12-effect-body--compact">{{ focusCard.effectText || '无效果文字' }}</p>
                    <ul v-if="statusTexts(focusCard).length" class="inspector-statuses"><li v-for="text in statusTexts(focusCard)" :key="text">{{ text }}</li></ul>
                  </template>
                  <div v-else class="empty-inspector">悬停或选择卡牌<br/>查看数值</div>
                </section>
                </div>
              </Teleport>
              </div>
              <div class="selected-card-utility-slot" data-ui-contract="selected-card-utility-dock">
                <BattleUtilityDock @settings="emit('settings')" />
              </div>
            </div>
          </div>
        </aside>

        <section class="grand-panel phase-column" aria-label="回合阶段">
          <span class="phase-disaster-value" data-ui-contract="phase-disaster-value"><img src="/assets/l12/disaster-icon-source.png" alt="天灾值"/><b>{{ game.disasterValue }}</b></span>
          <PhaseTrack vertical :phase="phasePlaybackPhase ?? game.phase" :round="game.round" :active-side="game.activePlayer === game.you ? 'my' : 'opponent'" />
        </section>

        <main class="board-center" :class="{ 'timed-board': Boolean(l12State.rankedClock) }" data-l12-game-stage>
          <HandArea v-if="l12State.gmEnabled" class="opponent-hand" :cards="viewEnemy.hand" :player-index="viewEnemy.playerIndex"
            :selected-ids="selectedHandIdsFor(viewEnemy.playerIndex)"
            :playable-ids="playableHandIdsFor(viewEnemy.playerIndex)" :dim-unplayable="isControlledPlayer(viewEnemy.playerIndex) && game.phase !== 'Mulligan'"
            :show-play-action="!hasBlockingPrompt && isControlledPlayer(viewEnemy.playerIndex) && isMyMain && !l12State.pendingAction"
            @select="selectHandFor(viewEnemy.playerIndex, $event)" @play="playFromHandFor(viewEnemy.playerIndex, $event)" @focus="focusCard = $event" />
          <HandArea v-else class="opponent-hand" hidden :count="viewEnemy.handCount || 0" :player-index="viewEnemy.playerIndex" />
          <div class="board-status-lane opponent-status-lane" data-ui-contract="opponent-status-safe-lane">
            <PlayerTurnClock class="board-player-clock opponent-player-clock" :player-index="viewEnemy.playerIndex" side="opponent"
              :active="game.activePlayer === viewEnemy.playerIndex" :phase="game.phase" :ranked-clock="l12State.rankedClock" />
          </div>
          <div class="felt-board" data-l12-game-board data-ui-contract="persistent-board-safe-layout">
            <PlayerMat class="battlefield-half opponent-half" :player="viewEnemy" side="opponent" :controllable="isControlledPlayer(viewEnemy.playerIndex)"
              :active="game.activePlayer === viewEnemy.playerIndex && !combat && !(mode === 'attack' && selectedId)" :viewer-player-index="game.you"
              :selected-id="selectedId" :selected-ids="supportIds" :actions-enabled="!hasBlockingPrompt && !readOnly && isControlledPlayer(viewEnemy.playerIndex) && isMyMain && !l12State.pendingAction"
              :placement-mode="!hasBlockingPrompt && (Boolean(gmPlacement && gmPlacement.targetPlayer === viewEnemy.playerIndex) || (isControlledPlayer(viewEnemy.playerIndex) && mode === 'play' && playArmed && (isInfiltrator(selectedHandCard) || selectedHandCard?.cardType === 'legion' || isCounter(selectedHandCard))))"
              :placement-can-replace-counter="selectedHandCard?.cardType === 'legion'" :placement-row="isCounter(selectedHandCard) ? 1 : null"
              :turn-serial="game.turnSerial" :round="game.round" :hidden-reveal-card="hiddenRevealCard" :interaction-prompt-active="Boolean(hasBlockingPrompt)"
              :attack-mode="!combat && mode === 'attack' && Boolean(selectedId)"
              :move-mode="isControlledPlayer(viewEnemy.playerIndex) && mode === 'move'" :free-move-mode="isControlledPlayer(viewEnemy.playerIndex) && mode === 'freeMove'" :cavalry-move-mode="isControlledPlayer(viewEnemy.playerIndex) && mode === 'cavalryMove'"
              :selection-mode="selectionModeFor(viewEnemy.playerIndex)" :targetable-ids="targetableIdsFor(viewEnemy.playerIndex)"
              :prompt-slot-ids="boardSlotTargetPlayerIndex === viewEnemy.playerIndex ? (boardSlotPrompt?.validChoices ?? []) : []"
              :attackable-ids="isControlledPlayer(viewEnemy.playerIndex) ? attackableIds : []" :response-playable-ids="isControlledPlayer(viewEnemy.playerIndex) ? responsePlayableIds : []"
              :selected-target-ids="boardTargetIds"
              :can-activate-osiris="isControlledPlayer(viewEnemy.playerIndex) && canActivateOsiris"
              :osiris-victory-disabled-reason="osirisVictoryDisabledReason"
              :combat-attacker-id="combat?.attackerOwner.playerIndex === viewEnemy.playerIndex ? combat.attacker.instanceId : null"
              :combat-target-id="combat?.targetOwner.playerIndex === viewEnemy.playerIndex ? combat.target?.instanceId : null"
              :combat-target-master="combat?.targetOwner.playerIndex === viewEnemy.playerIndex && !combat.target"
              :payment-choice-ids="paymentChoiceIds" :payment-selected-ids="paymentResourceIds"
              :master-targetable="!isControlledPlayer(viewEnemy.playerIndex) && !combat && selectedAttackTargets.includes('master')"
              @slot="(row, slot, card) => slotFor(viewEnemy.playerIndex, row, slot, card)" @master="masterFor(viewEnemy.playerIndex)"
              @focus="focusCard = $event" @graveyard="!hasBlockingPrompt && (graveyardPlayer = $event)"
              @card-action="(action, card) => fieldActionFor(viewEnemy.playerIndex, action, card)"
              @ability="(card, ability) => activateAbilityFor(viewEnemy.playerIndex, card, ability)"
              @faction-ability="ability => activateFactionAbilityFor(viewEnemy.playerIndex, ability)"
              @select-card="card => selectPublicCardFor(viewEnemy.playerIndex, card)" @payment-resource="togglePaymentResource" />
            <div class="board-seam" data-ui-contract="phase-safe-track">
              <span class="board-midline-anchor" aria-hidden="true" />
            </div>
            <PhasePlayback :events="game.recentEvents ?? []" @phase-change="phasePlaybackPhase = $event" />
            <ActionPresentationLayer :events="game.recentEvents ?? []" :match-id="game.matchId" :player-names="game.players.map(player => player.name)"
              :paused="passivePresentationPaused" />
            <ZoneMovementPresentationLayer :events="game.recentEvents ?? []" :match-id="game.matchId"
              :viewer-player-index="game.you" :paused="passivePresentationPaused" />
            <CombatMotionPresentationLayer :events="game.recentEvents ?? []" :match-id="game.matchId" />
            <Teleport to="body">
              <Transition name="public-reveal">
                <div v-if="publicReveal && !activeBoardPromptId" :key="publicReveal.sequence" class="public-reveal-animation" data-ui-contract="public-card-reveal-animation">
                  <div class="public-reveal-cards">
                    <CardImage v-for="card in publicReveal.cards" :key="card.instanceId" :card-id="card.cardId" :legacy-url="card.imageUrl" :alt="card.name" intent="detail" eager
                      :class="{ horizontal: isHorizontalCardType(card.cardType) }" />
                  </div>
                  <strong class="l12-effect-body l12-effect-body--prominent">{{ publicReveal.text }}</strong>
                </div>
              </Transition>
              <Transition name="dice-reveal">
                <div v-if="diceReveal && !activeBoardPromptId" :key="diceReveal.sequence" class="dice-reveal-animation" :class="{ settled: diceReveal.settled }" data-ui-contract="dice-event-animation">
                  <div class="dice-reveal-values">
                    <b v-for="(value, index) in diceReveal.animatedValues" :key="index">{{ value }}</b>
                  </div>
                  <strong>{{ diceReveal.text }}</strong>
                </div>
              </Transition>
            </Teleport>
            <div v-if="mode === 'attack' && selectedId && !combat && !hasBlockingPrompt" class="board-mode-hint" data-ui-contract="cancel-local-attack-selection">
              <span>请选择进攻对象</span><button type="button" @click="cancelLocalAttackSelection">取消</button>
            </div>
            <div v-if="combat && !activeBoardPromptId" class="combat-presentation">
              <i class="combat-trace"/>
              <div class="combat-versus">
                <span :class="combat.attackerOwner.playerIndex === game.you ? 'mine' : 'opponent'">{{ combat.attackerOwner.playerIndex === game.you ? '我方' : '对手' }} · {{ combat.attacker.name }}</span>
                <b>{{ combat.attackValue }}<small>{{ combat.attackUnit }}</small></b>
                <em>⚔</em>
                <span :class="combat.targetOwner.playerIndex === game.you ? 'mine' : 'opponent'">{{ combat.targetOwner.playerIndex === game.you ? '我方' : '对手' }} · {{ combat.targetName }}</span>
                <b>{{ combat.targetValue }}<small>{{ combat.targetUnit }}</small></b>
              </div>
              <div v-if="game.phase === 'Defense' && game.pendingDefense?.stage === 'DefenseChoice' && !readOnly" class="combat-resolution-panel">
                <GameActions :game="game" :me="me" :mode="mode" :selected-id="selectedId"
                  :mulligan-count="mulliganIds.length" :defense-count="defenseIds.length" :defense-target-type="defenseTargetType"
                  :support-ids="supportIds" :can-support="eligibleSupportIds.length > 0" :support-ready="supportReady" :busy="l12State.pendingAction" @command="command" />
              </div>
            </div>
            <PlayerMat class="battlefield-half my-half" :player="viewMe" side="my" :controllable="isControlledPlayer(viewMe.playerIndex)"
              :active="game.activePlayer === viewMe.playerIndex && !combat && !(mode === 'attack' && selectedId)" :viewer-player-index="game.you"
              :turn-serial="game.turnSerial" :round="game.round" :hidden-reveal-card="hiddenRevealCard" :interaction-prompt-active="Boolean(hasBlockingPrompt)"
              :selected-id="selectedId" :selected-ids="supportIds" :actions-enabled="!hasBlockingPrompt && !readOnly && isControlledPlayer(viewMe.playerIndex) && isMyMain && !l12State.pendingAction"
              :move-mode="isControlledPlayer(viewMe.playerIndex) && mode === 'move'" :free-move-mode="isControlledPlayer(viewMe.playerIndex) && mode === 'freeMove'" :cavalry-move-mode="isControlledPlayer(viewMe.playerIndex) && mode === 'cavalryMove'"
              :placement-mode="!hasBlockingPrompt && (Boolean(gmPlacement && gmPlacement.targetPlayer === viewMe.playerIndex) || (isControlledPlayer(viewMe.playerIndex) && mode === 'play' && playArmed && (selectedHandCard?.cardType === 'legion' || isCounter(selectedHandCard))))"
              :placement-can-replace-counter="selectedHandCard?.cardType === 'legion'" :placement-row="isCounter(selectedHandCard) ? 1 : null"
              :attack-mode="!combat && mode === 'attack' && Boolean(selectedId)"
              :selection-mode="selectionModeFor(viewMe.playerIndex)" :targetable-ids="targetableIdsFor(viewMe.playerIndex)"
              :prompt-slot-ids="boardSlotTargetPlayerIndex === viewMe.playerIndex ? (boardSlotPrompt?.validChoices ?? []) : []"
              :attackable-ids="isControlledPlayer(viewMe.playerIndex) ? attackableIds : []" :response-playable-ids="isControlledPlayer(viewMe.playerIndex) ? responsePlayableIds : []"
              :selected-target-ids="boardTargetIds" :payment-choice-ids="paymentChoiceIds" :payment-selected-ids="paymentResourceIds"
              :can-activate-osiris="isControlledPlayer(viewMe.playerIndex) && canActivateOsiris"
              :osiris-victory-disabled-reason="osirisVictoryDisabledReason"
              :combat-attacker-id="combat?.attackerOwner.playerIndex === viewMe.playerIndex ? combat.attacker.instanceId : null"
              :combat-target-id="combat?.targetOwner.playerIndex === viewMe.playerIndex ? combat.target?.instanceId : null"
              :combat-target-master="combat?.targetOwner.playerIndex === viewMe.playerIndex && !combat.target"
              :master-targetable="!isControlledPlayer(viewMe.playerIndex) && !combat && selectedAttackTargets.includes('master')"
              @slot="(row, slot, card) => slotFor(viewMe.playerIndex, row, slot, card)" @master="masterFor(viewMe.playerIndex)"
              @focus="focusCard = $event" @graveyard="!hasBlockingPrompt && (graveyardPlayer = $event)"
              @card-action="(action, card) => fieldActionFor(viewMe.playerIndex, action, card)"
              @select-card="card => selectPublicCardFor(viewMe.playerIndex, card)"
              @ability="(card, ability) => activateAbilityFor(viewMe.playerIndex, card, ability)"
              @faction-ability="ability => activateFactionAbilityFor(viewMe.playerIndex, ability)"
              @payment-resource="togglePaymentResource" />
          </div>
          <div class="board-status-lane my-status-lane" data-ui-contract="player-status-safe-lane">
            <PlayerTurnClock class="board-player-clock my-player-clock" :player-index="viewMe.playerIndex" side="my"
              :active="game.activePlayer === viewMe.playerIndex" :phase="game.phase" :ranked-clock="l12State.rankedClock" />
          </div>
          <HandArea :cards="viewMe.hand" :player-index="viewMe.playerIndex" :selected-ids="selectedHandIdsFor(viewMe.playerIndex)"
            :playable-ids="playableHandIdsFor(viewMe.playerIndex)" :dim-unplayable="isControlledPlayer(viewMe.playerIndex) && game.phase !== 'Mulligan'"
            :show-play-action="!hasBlockingPrompt && isControlledPlayer(viewMe.playerIndex) && isMyMain && !l12State.pendingAction"
            @select="selectHandFor(viewMe.playerIndex, $event)" @play="playFromHandFor(viewMe.playerIndex, $event)" @focus="focusCard = $event" />
        </main>

        <aside class="board-rail right-rail">
          <section class="grand-panel player-panel" data-ui-contract="complete-player-summary">
            <article class="player-summary opponent-summary">
              <div class="player-summary-primary"><b>对方</b><strong>{{ viewEnemy.name || '未命名玩家' }}</strong></div>
              <div class="player-summary-meta">
                <RankedIdentityBadge class="rank-badge" variant="tier" compact :label="enemyBadge?.rankLabel || '未定级'" />
                <RankedIdentityBadge class="title-badge" variant="title" compact :label="enemyBadge?.masterTitle || '暂无称号'" />
                <span class="connection-state" :class="{ online: playerConnection(viewEnemy.playerIndex) }"><i/>{{ connectionLabel(viewEnemy.playerIndex) }}</span>
              </div>
            </article>
            <hr/>
            <article class="player-summary my-summary">
              <div class="player-summary-primary"><b>我方</b><strong class="mine">{{ viewMe.name || '未命名玩家' }}</strong></div>
              <div class="player-summary-meta">
                <RankedIdentityBadge class="rank-badge" variant="tier" compact :label="myBadge?.rankLabel || '未定级'" />
                <RankedIdentityBadge class="title-badge" variant="title" compact :label="myBadge?.masterTitle || '暂无称号'" />
                <span class="connection-state" :class="{ online: playerConnection(viewMe.playerIndex) }"><i/>{{ connectionLabel(viewMe.playerIndex) }}</span>
              </div>
            </article>
          </section>
          <section class="grand-panel log-panel record-log"><h3>对局记录</h3>
            <BattleEventLog :events="game.recentEvents ?? []" :you="game.you" :names="game.players.map(player => player.name)" @focus="focusCard = $event" />
          </section>
          <section v-if="!combat && !readOnly" class="grand-panel action-panel"><h3>操作</h3><GameActions :game="game" :me="me" :mode="mode" :selected-id="selectedId"
            :mulligan-count="mulliganIds.length" :defense-count="defenseIds.length" :defense-target-type="defenseTargetType"
            :support-ids="supportIds" :can-support="eligibleSupportIds.length > 0" :support-ready="supportReady" :busy="l12State.pendingAction" @command="command" /></section>
        </aside>
      </div>
      <GraveyardOverlay v-if="graveyardPlayer !== null" :players="[viewMe, viewEnemy]" :initial-player="graveyardPlayer"
        :own-player-index="game.you" :can-activate-osiris="canActivateOsiris"
        @close="graveyardPlayer = null" @focus="focusCard = $event" @ability="activateAbility" />
      <MasterOverlay v-if="masterPlayerIndex !== null" :player="game.players[masterPlayerIndex]" :mine="masterPlayerIndex === controlledPlayerIndex"
        :can-activate="!readOnly && masterPlayerIndex === controlledPlayerIndex && isMyMain" :busy="l12State.pendingAction" @close="masterPlayerIndex = null" @activate="activateMaster" />
      <div v-if="gmPlacement && !readOnly" class="board-target-controls gm-placement-controls">
        <strong>GM：请选择〈{{ gmPlacement.cardName }}〉的登场位置</strong><span>直接点击目标玩家的绿色高亮空位</span>
        <button @click="emit('gmPlacementResolved')">取消</button>
      </div>
      <div v-if="boardTargetPrompt && !readOnly" class="board-target-controls">
        <strong>{{ boardTargetPrompt.text }}</strong><span>已选择 {{ boardTargetIds.length }}/{{ boardTargetPrompt.maxChoose }}</span>
        <button v-if="boardTargetPrompt.validChoices.includes('skip')" @click="resolveBoardTarget(true)">不发动</button>
        <button class="primary" :disabled="boardTargetIds.length < boardTargetPrompt.minChoose" @click="resolveBoardTarget(false)">{{ boardTargetPrompt.data?.choiceMode === 'mixed-board-payment' ? '确认费用' : '确认发动' }}</button>
      </div>
      <div v-if="boardSlotPrompt && !readOnly" class="board-target-controls board-slot-controls">
        <CardImage v-if="boardSlotPreview" :card-id="boardSlotPreview.cardId" :legacy-url="boardSlotPreview.imageUrl" :alt="boardSlotPreview.name" intent="board" eager
          @mouseenter="focusCard = boardSlotPreview" @click="focusCard = boardSlotPreview" />
        <strong>{{ boardSlotPrompt.text }}</strong><span>直接点击绿色高亮空位</span>
        <button v-if="boardSlotPrompt.validChoices.includes('skip')"
          @click="command('resolvePrompt', { promptId: boardSlotPrompt.promptId, cardInstanceIds: ['skip'] })">取消</button>
      </div>
      <div v-if="resourceSelectionPrompt && !readOnly" class="board-target-controls resource-payment-controls">
        <strong>{{ resourceSelectionPrompt.text }}</strong>
        <span>已选择 {{ paymentResourceIds.length }}/{{ resourceSelectionPrompt.maxChoose }}</span>
        <button v-if="resourceSelectionPrompt.validChoices.includes('skip')" @click="confirmResourcePayment(true)">不发动</button>
        <button class="primary" :disabled="paymentResourceIds.length < resourceSelectionPrompt.minChoose"
          @click="confirmResourcePayment(false)">{{ resourceSelectionPrompt.kind === 'resource-return' || resourceSelectionPrompt.data?.choiceMode === 'resource-return'
            ? '确认返还'
            : resourceSelectionPrompt.kind === 'resource-payment' || resourceSelectionPrompt.data?.choiceMode === 'resource-payment'
              ? '确认支付' : '确认选择' }}</button>
      </div>
      <PromptOverlay v-if="!readOnly || game.phase === 'DisasterPreparation'" :game="game" :read-only="readOnly" :suppressed-prompt-id="activeBoardPromptId" :suppress-defense-wait="Boolean(combat)" :mulligan-selected-ids="mulliganIds" :busy="l12State.pendingAction" :inspector-visible="modalInspectorVisible"
        @focus-card="focusCard = $event" @mulligan-toggle="toggle(mulliganIds, $event)" @mulligan-confirm="command('mulligan')" @minimized-change="promptMinimized = $event" />
    </div>
  </div>
  <SandboxCardPicker v-if="customDisasterSlot !== null" title="更换自定天灾（第四槽堙灭固定）" :allowed-types="['destruction']" @select="replaceCustomDisaster" @close="customDisasterSlot = null"/>
</template>

<style scoped>
.board-stage{aspect-ratio:16/9}
.board-viewport.gm-panel-docked{right:344px}
.stage-layout{display:grid;grid-template-columns:340px 92px minmax(0,1fr) 320px;align-items:stretch;gap:8px}
.stage-layout>.board-rail{width:auto;min-width:0}
.left-rail{display:flex}.left-detail-layout{display:flex;min-height:0;flex:1}.left-card-column{display:flex;width:100%;min-width:0;min-height:0;flex-direction:column;gap:10px}.left-rail>.grand-panel,.left-card-column>.grand-panel,.left-card-column>.card-inspector-anchor{box-sizing:border-box;width:100%}.right-rail{display:grid;grid-template-rows:auto minmax(0,1fr) auto;align-items:stretch}
.current-disaster-panel{display:grid;flex:none;grid-template-columns:minmax(0,1fr);align-items:center;padding:10px!important}.current-disaster-card{width:100%;padding:0;overflow:hidden;border:1px solid rgba(240,239,229,.72);background:#080a0b}.current-disaster-card:disabled{cursor:default}.current-disaster-card img,.current-disaster-card :deep(.l12-card-image){display:block;width:100%;height:auto;aspect-ratio:8/5;object-fit:contain}.phase-column{display:flex;min-width:0;min-height:0;margin-block:242px;flex-direction:column;gap:8px;padding:8px 5px!important;overflow:hidden}.phase-disaster-value{display:flex;min-height:64px;align-items:center;justify-content:center;gap:6px;padding:5px 3px;border:1px solid rgba(238,238,228,.34);background:rgba(7,10,11,.68);color:#fff}.phase-disaster-value img{width:30px;height:32px;object-fit:contain;filter:invert(1)}.phase-disaster-value b{font-size:max(30px,var(--l12-board-copy,13px));line-height:1}.phase-column :deep(.l12-phase-track.vertical){flex:1;min-height:0}
.board-center{--l12-hand-lane-height:160px;display:grid;grid-template-rows:var(--l12-hand-lane-height) 70px minmax(0,1fr) 70px var(--l12-hand-lane-height);align-items:stretch;gap:6px}
.board-center>.l12-hand{position:relative;z-index:40;box-sizing:border-box;width:100%;height:var(--l12-hand-lane-height)!important;min-height:var(--l12-hand-lane-height);align-self:stretch;transform:translateX(-10px)}
.board-center.timed-board>.l12-hand{width:calc(100% - 400px);padding-right:0;justify-self:center}
.board-center>.opponent-hand{grid-row:1}.opponent-status-lane{grid-row:2}.felt-board{grid-row:3}.my-status-lane{grid-row:4}.board-center>.l12-hand:last-child{grid-row:5}
.board-viewport{top:52px}.board-status-lane{height:70px!important;min-height:70px!important;flex-shrink:0}.player-summary :is(.player-summary-primary,.player-summary-meta,.connection-state){font-size:var(--l12-board-copy,13px)!important}
.right-rail{width:auto}.right-rail .record-log{display:flex;flex:1;flex-direction:column;min-height:150px}.right-rail .action-panel{max-height:300px;overflow:auto}.right-rail .action-panel :deep(.l12-actions>p){display:none}.board-rail .card-inspector{overflow:auto}.session-disaster-strip span{white-space:normal!important;overflow-wrap:anywhere}
.felt-board{
  --l12-board-seam-safe-height:44px;
  box-sizing:border-box;
  width:100%;
  display:grid;
  grid-template-rows:minmax(0,1fr) var(--l12-board-seam-safe-height) minmax(0,1fr);
  justify-self:center;
  align-items:stretch;
}
.board-status-lane{position:relative;z-index:38;display:flex;box-sizing:border-box;height:70px;min-height:70px;justify-content:flex-end;overflow:visible;pointer-events:none}.board-player-clock{position:relative;right:auto;top:auto;bottom:auto}.opponent-status-lane{order:0;align-items:flex-end}.my-status-lane{order:0;align-items:flex-start}
.player-panel{box-sizing:border-box;height:auto!important;min-height:144px;flex:none;overflow:hidden!important}
.player-summary{display:grid;min-width:0;gap:7px}.player-summary-primary{display:grid;min-width:0;grid-template-columns:max-content minmax(0,1fr);align-items:center;column-gap:6px}.player-summary-primary>b{color:#d2525b;font-size:var(--l12-board-copy,13px);white-space:nowrap}.my-summary .player-summary-primary>b{color:#58bdc5}.player-summary-primary>strong{min-width:0;overflow:hidden!important;font-size:max(15px,var(--l12-board-copy,13px))!important;line-height:1.35!important;text-overflow:ellipsis!important;white-space:nowrap!important}.player-summary-meta{display:grid;min-width:0;grid-template-columns:max-content max-content minmax(0,1fr);align-items:center;column-gap:4px;color:#aeb7b5;font-size:var(--l12-board-copy,13px);line-height:1.35}.player-summary-meta>.rank-badge,.player-summary-meta>.title-badge{min-width:max-content;max-width:none;flex:none}.player-summary-meta>.connection-state{min-width:0;max-width:100%!important;justify-self:end;justify-content:flex-end;margin-left:0!important;font-size:clamp(9px,var(--l12-board-micro,9px),11px)!important;overflow:hidden!important;text-overflow:ellipsis!important}
.connection-state{display:flex!important;width:max-content;max-width:none!important;align-items:center;gap:4px;margin:0!important;color:#b76570!important;font-size:var(--l12-board-copy,13px)!important;font-weight:900;line-height:1!important;overflow:visible!important;white-space:nowrap!important;text-overflow:clip!important}.connection-state.online{color:#58c99a!important}.connection-state i{width:6px;height:6px;border-radius:50%;background:currentColor;box-shadow:0 0 6px currentColor}
.player-panel>hr{margin:9px 0!important}
.right-rail .record-log{min-height:120px;overflow:hidden}.right-rail .record-log>.event-list{min-height:0;overflow-y:auto}
.battlefield-half{position:relative;box-sizing:border-box;width:100%;min-height:0;align-self:stretch;justify-self:center}
.battlefield-half::before{content:'';position:absolute;z-index:1;inset:0;box-sizing:border-box;border:1px solid rgba(238,238,228,.18);pointer-events:none}
.battlefield-half.opponent-half::before{border-color:rgba(196,40,50,.34)}
.battlefield-half.my-half::before{inset:0;border-color:rgba(57,171,181,.4)}
.battlefield-half.opponent-half{grid-row:1}
.board-seam{z-index:12;grid-row:2;box-sizing:border-box;height:var(--l12-board-seam-safe-height);min-height:var(--l12-board-seam-safe-height);isolation:isolate}
.board-midline-anchor{position:absolute;left:0;right:0;top:50%;height:1px;background:linear-gradient(90deg,transparent,rgba(238,238,228,.35),transparent)}
.battlefield-half.my-half{grid-row:3}
.felt-board :deep(.formation){width:100%;height:350px;grid-template-columns:repeat(3,173px);grid-template-rows:repeat(2,173px);justify-content:end;gap:4px 8px}
.felt-board :deep(.formation-slot .card-tile),.felt-board :deep(.formation-slot .card-tile.tapped){width:114.4px;height:160.6px;flex-basis:114.4px}
.felt-board :deep(.formation-slot .field-actions){bottom:calc(50% + 86.5px)}
.session-disaster-panel{flex:none;padding:9px 10px}.session-disaster-panel h3{margin:0 0 7px}.session-disaster-strip{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:5px}.session-disaster-strip button{min-width:0;padding:2px;border:1px solid #59625f;background:#070a0b;color:#d9ddd8;cursor:pointer}.session-disaster-strip button.hidden{border-color:#343b39;cursor:default}.session-disaster-strip button.inactive img,.session-disaster-strip button.inactive .l12-card-image{filter:grayscale(.85) brightness(.45)}.session-disaster-strip img,.session-disaster-strip .l12-card-image{display:block;width:100%;height:auto;aspect-ratio:8/5}.session-disaster-strip span{display:block;overflow:hidden;padding:2px 2px 1px;font-size:var(--l12-board-copy,13px);font-weight:900;text-overflow:ellipsis;white-space:nowrap}.session-disaster-strip button:not(.hidden):hover{border-color:#73d4c5;box-shadow:0 0 8px rgba(115,212,197,.3)}
.board-mode-hint{position:absolute;z-index:28;left:50%;top:50%;display:flex;align-items:center;gap:10px;padding:8px 9px 8px 16px;border:1px solid #e0b85a;background:rgba(8,10,11,.95);color:#fff3c2;box-shadow:0 7px 22px #000;transform:translate(-50%,-50%);font-size:var(--l12-board-copy,13px);font-weight:900;pointer-events:auto}.board-mode-hint button{min-width:58px;min-height:44px;padding:6px 12px;border:1px solid #747d7b;background:#171b1c;color:#f1eee4;font-size:var(--l12-board-copy,13px);font-weight:900}.board-mode-hint button:hover{border-color:#e0b85a;background:#292419}
.public-reveal-animation{position:fixed;z-index:2147483000;left:50%;top:50%;display:grid;min-width:190px;max-width:min(760px,80vw);justify-items:center;gap:10px;transform:translate(-50%,-50%);pointer-events:none}.public-reveal-cards{display:flex;max-width:100%;align-items:center;justify-content:center;gap:8px;overflow:hidden}.public-reveal-cards .l12-card-image{width:118px;height:165px;filter:drop-shadow(0 10px 15px #000) drop-shadow(0 0 16px rgba(213,188,112,.38))}.public-reveal-cards .l12-card-image.horizontal{width:190px;height:auto;aspect-ratio:8/5}.public-reveal-animation strong{padding:7px 12px;border:1px solid #d5bc70;background:rgba(7,9,10,.9);box-shadow:0 7px 22px #000;color:#fff2c7;font-size:var(--l12-board-copy,13px);font-weight:900;letter-spacing:.04em;text-align:center}.public-reveal-enter-active,.public-reveal-leave-active{transition:opacity .24s ease,filter .24s ease}.public-reveal-enter-from,.public-reveal-leave-to{opacity:0;filter:blur(5px)}
.combat-presentation{position:absolute;z-index:20;left:50%;top:50%;width:760px;height:1px;transform:translate(-50%,-50%);pointer-events:none}.combat-trace{position:absolute;left:50%;top:-108px;width:4px;height:216px;background:linear-gradient(transparent,#d88a39 20%,#f0ba66 50%,#d88a39 80%,transparent);filter:drop-shadow(0 0 7px #c36b26);transform:rotate(-10deg)}.combat-versus{position:absolute;left:50%;top:0;display:flex;width:max-content;max-width:760px;align-items:center;gap:12px;padding:10px 18px;border:1px solid #8e7650;background:rgba(7,9,10,.95);box-shadow:0 8px 26px #000;transform:translate(-50%,-50%);font-weight:900}.combat-versus span{max-width:190px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.combat-versus span.mine{color:#74d0d3}.combat-versus span.opponent{color:#e6757c}.combat-versus>b{display:flex;align-items:baseline;gap:4px;padding:4px 7px;background:#342a25;color:#fff}.combat-versus b small{color:#c8bba3;font-size:var(--l12-board-copy,13px)}.combat-versus em{color:#e5bd60;font-size:max(18px,var(--l12-board-copy,13px));font-style:normal}.combat-resolution-panel{position:absolute;left:50%;top:34px;width:390px;padding:10px 12px;border:1px solid #8e7650;background:rgba(8,11,12,.96);box-shadow:0 12px 30px #000;transform:translateX(-50%);pointer-events:auto}.combat-resolution-panel :deep(.l12-actions){gap:6px}.combat-resolution-panel :deep(.l12-actions p){margin:0;font-size:var(--l12-board-copy,13px)}.combat-resolution-panel :deep(.l12-actions button){padding:7px 9px}
.record-log .event-list p{display:grid;grid-template-columns:auto minmax(0,1fr);align-items:start;gap:5px;margin:0 0 7px}.record-log .event-list p.event-turn-start{display:block;padding:4px 0;text-align:center}.record-log .event-message{min-width:0;white-space:normal;overflow-wrap:anywhere;word-break:break-word}.turn-divider{color:#e0b641;font-size:var(--l12-board-copy,13px);white-space:nowrap}.event-tag{flex:none;padding:2px 4px;border:1px solid #5c4a86;color:#cbaaff;font-size:var(--l12-board-copy,13px);line-height:1.25}.event-play .event-tag,.event-put .event-tag{border-color:#126f82;color:#5fd5e2}.event-attack .event-tag,.event-combat .event-tag{border-color:#8d2942;color:#ff6687}.event-response .event-tag,.event-defense .event-tag,.event-support .event-tag{border-color:#9a501b;color:#f0a45e}.event-disaster .event-tag,.event-disaster-active .event-tag,.event-disaster-value .event-tag{border-color:#9e722b;color:#efc15b}.event-damage .event-tag,.event-leave .event-tag{border-color:#813c40;color:#dd7c81}.event-move .event-tag{border-color:#26757c;color:#65cbd0}
.board-target-controls{position:fixed;z-index:2147483500;left:50%;top:76px;display:flex;align-items:center;gap:10px;max-width:760px;padding:10px 13px;border:1px solid #70d7df;background:#091011;box-shadow:0 14px 36px #000;transform:translateX(-50%)}.board-target-controls strong{max-width:430px;color:#fff;font-size:var(--l12-board-copy,13px)}.board-target-controls span{color:#8f9894;font-size:var(--l12-board-copy,13px)}.board-target-controls button{padding:7px 12px;border:1px solid #999;background:#1b2020;color:#fff;font-weight:900}.board-target-controls button.primary{border-color:#72e09a;background:#174d2d}.board-target-controls button:disabled{opacity:.38}
.board-slot-controls .l12-card-image{width:52px;height:72px;background:#050708;cursor:pointer}.board-slot-controls span{color:#72e09a;font-weight:900}
.inspector-statuses{display:grid;gap:4px;margin:8px 0 0;padding:0;list-style:none}.inspector-statuses li{padding:4px 6px;border-left:2px solid #70d7df;background:rgba(112,215,223,.08);color:#d9ddd7;font-size:var(--l12-board-copy,13px);font-weight:800;line-height:1.45}
.inspector-card-tags{display:flex;box-sizing:border-box;width:max-content;max-width:100%;align-self:center;justify-content:center;flex-wrap:wrap;gap:5px;margin:0 auto 7px}.inspector-card-tags span{flex:0 0 auto;padding:2px 6px;border:1px solid #4f5e5b;background:#111819;color:#8fdad7;font-size:var(--l12-board-copy,13px);font-weight:900;white-space:nowrap}
.left-disaster-row{display:grid;width:100%;grid-template-columns:132px minmax(0,1fr);gap:8px;flex:none}.left-disaster-row>.grand-panel{box-sizing:border-box;width:100%;min-width:0;min-height:178px}.session-disaster-panel{display:grid;align-content:center;justify-items:center;padding:8px!important}.session-disaster-panel h3{width:100%;margin:0 0 8px}.session-disaster-strip{display:grid;width:max-content;grid-template-columns:repeat(2,51.2px);gap:8px}.session-disaster-strip button{width:51.2px;min-width:51.2px;height:51.2px;padding:0;overflow:hidden;border:2px solid #c8b978;border-radius:50%;background:#070a0b}.session-disaster-strip button.hidden,.session-disaster-strip button.unrevealed{border-color:#49504e;filter:grayscale(1) brightness(.58)}.session-disaster-strip button.revealed{border-color:#69716f;filter:grayscale(.85) brightness(.58)}.session-disaster-strip button.resolved{border-color:#76508f;box-shadow:0 0 8px rgba(133,75,174,.28);filter:grayscale(.35) brightness(.64) saturate(.82)}.session-disaster-strip button.active{border-color:#bc6cff;box-shadow:0 0 13px rgba(187,87,255,.72),inset 0 0 0 1px rgba(231,202,255,.42);filter:none}.session-disaster-strip img,.session-disaster-strip .l12-card-image{width:100%;height:100%;border-radius:50%;transform:scale(1.09)}.session-disaster-strip button:not(.hidden):hover{border-color:#d49aff;box-shadow:0 0 11px rgba(190,102,255,.52)}.left-disaster-row>.current-disaster-panel{display:grid;place-items:center;padding:10px!important}
.session-disaster-panel h3{text-align:center}
.card-inspector-anchor{display:flex;flex:1;min-height:0}.card-inspector-anchor>.card-inspector{width:100%}.selected-card-utility-slot{box-sizing:border-box;width:100%;height:60px;flex:none}.inspector-card-image{display:block;width:168px;height:235px;max-width:100%;flex:0 0 235px;margin:4px auto 10px;object-fit:contain;background:#050708}.card-inspector.horizontal-inspector .inspector-card-image{width:100%;max-width:239px;height:auto;flex-basis:auto;aspect-ratio:8/5}.card-inspector-floating{position:fixed!important;z-index:1600!important;box-sizing:border-box;overflow:auto!important;transform-origin:left top;pointer-events:none}.card-inspector-floating .inspector-card-image{width:min(168px,100%);max-width:100%;height:auto;aspect-ratio:5/7}.card-inspector-floating.horizontal-inspector .inspector-card-image{aspect-ratio:8/5}
.inspector-style-scope{display:contents!important}
.session-disaster-strip button.replaceable{cursor:pointer}.session-disaster-strip button.replaceable:hover{border-color:#e6bd4a;box-shadow:0 0 12px #d49c3d80}
.dice-reveal-animation{position:fixed;z-index:2147483001;left:50%;top:45%;display:grid;justify-items:center;gap:10px;transform:translate(-50%,-50%);pointer-events:none}.dice-reveal-values{display:flex;gap:14px}.dice-reveal-values b{display:grid;width:76px;height:76px;place-items:center;border:3px solid #e3c36d;border-radius:15px;background:#f1eee2;box-shadow:0 12px 30px #000,0 0 22px rgba(227,195,109,.35);color:#111;font-size:max(44px,var(--l12-board-copy,13px));line-height:1;animation:l12-dice-roll .18s infinite alternate}.dice-reveal-animation.settled .dice-reveal-values b{animation:l12-dice-land .32s ease-out}.dice-reveal-animation strong{max-width:min(720px,82vw);padding:7px 12px;border:1px solid #d5bc70;background:rgba(7,9,10,.92);box-shadow:0 7px 22px #000;color:#fff2c7;font-size:var(--l12-board-copy,13px);font-weight:900;text-align:center}.dice-reveal-enter-active,.dice-reveal-leave-active{transition:opacity .2s ease,filter .2s ease}.dice-reveal-enter-from,.dice-reveal-leave-to{opacity:0;filter:blur(5px)}@keyframes l12-dice-roll{from{transform:rotate(-10deg) scale(.94)}to{transform:rotate(10deg) scale(1.06)}}@keyframes l12-dice-land{0%{transform:scale(1.35) rotate(20deg)}100%{transform:scale(1) rotate(0)}}
.public-reveal-animation{z-index:903}.dice-reveal-animation{z-index:904}.board-target-controls{z-index:3000}.card-inspector-floating{z-index:3100!important}
.battle-title{display:flex;flex-wrap:wrap;gap:5px;margin-top:6px}.battle-title b,.battle-title i{padding:3px 6px;border:1px solid #82663a;border-radius:3px;background:#261b0c;color:#f2d27a;font-size:var(--l12-board-copy,13px);font-style:normal;font-weight:900}.battle-title i{border-color:#75509a;background:#1b1028;color:#dfbdff}
</style>
