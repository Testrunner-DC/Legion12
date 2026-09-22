<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ActionEvent, Card, DisasterCardView, GameState, Phase } from '../types'
import { isCounterTacticCard, isHorizontalCardType } from '../cardPresentation'
import { blackLotusLogoUrl, destructionRoundBackUrl, disasterRoundUrl, factionLogoUrls, godPowerLogoUrl, roundCardUrl, siteBrandIconUrl } from '../specialAssets'
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
import CardDetailContent from '../CardDetailContent.vue'
import type { DeckCard } from '../decks'
import RankedIdentityBadge from '../RankedIdentityBadge.vue'
import { getFactionPresentation } from '../factionPresentation'
import { landscapeTeleportTarget, viewportRect } from '../mobileViewport'
import { useBattleViewportLayout } from './battleViewportLayout'

type GmPlacementRequest = {
  type: 'placeCard' | 'playHandCard'
  targetPlayer: number
  cardId?: string
  cardInstanceId?: string
  cardName: string
  cardType: string
  triggerEffects: boolean
}
const props = withDefaults(defineProps<{
  game: GameState
  readOnly?: boolean
  replayFocusCard?: Card | null
  replayPlaybackSpeed?: number | null
  gmPlacement?: GmPlacementRequest | null
  gmPanelOpen?: boolean
}>(), { readOnly: false, replayFocusCard: null, replayPlaybackSpeed: null, gmPlacement: null, gmPanelOpen: false })
const emit = defineEmits<{ gmPlacementResolved: []; settings: []; replayPresentationChange: [busy: boolean] }>()
// Preserve the desktop hierarchy while allowing the whole board to become
// genuinely denser on smaller canvases. Full inverse scaling made text remain
// physically constant and therefore grow out of proportion to cards/zones.
const adaptiveBoardToken = (base: number, currentScale: number) => {
  const safeScale = Math.max(.1, Math.min(1, currentScale))
  const minimumScreenSize = base >= 13 ? 8 : base >= 11 ? 7 : 6
  return Math.max(base / Math.sqrt(safeScale), minimumScreenSize / safeScale)
}
const stageSize = computed(() => l12State.gmEnabled
  ? { width: 2304, height: 1296 }
  // The two 350px battlefield halves plus their protected centre seam need 754px
  // after the felt's own border and padding.  Keep that room in the outer stage
  // rather than shrinking the six fixed battlefield cells.
  : { width: 2048, height: 1264 })
const {
  scale,
  compactViewport,
  mobileLandscapeViewport,
} = useBattleViewportLayout({
  stageSize,
  gmPanelOpen: () => props.gmPanelOpen,
  afterUpdate: () => window.requestAnimationFrame(updateInspectorFloatRect),
})
const mobileRecordOpen = ref(false)
const mobileRecordMinimized = ref(false)
const mobileMoralePickerOpen = ref(false)
const mobileMoralePickerMinimized = ref(false)
const mobileMoraleReason = ref('')
// On phones the card face stays intentionally compact. The independent left
// drawer is opened only through its persistent handle, keeping a card tap safe.
const mobileInspectorOpen = ref(false)
const selectedId = ref<string | null>(null)
const focusCard = ref<Card | null>(null)
const focusDetailCard = computed<DeckCard | null>(() => {
  const card = focusCard.value
  if (!card) return null
  const master = card.cardType === 'master'
    ? props.game.players.find(player => player.master.masterId === card.cardId)?.master
    : undefined
  return {
    id: card.cardId,
    number: card.cardId,
    nameZh: card.name,
    cardType: card.cardType,
    isCounterTactic: card.isCounterTactic,
    product: '',
    faction: card.faction,
    imageUrl: card.imageUrl,
    cost: card.hasPrintedCost === false ? undefined : (card.currentCost ?? card.cost),
    hp: master?.hp,
    troops: card.cardType === 'legion' ? card.troops : undefined,
    disasterLevel: card.disasterLevel || undefined,
    trialValue: card.trialValue || undefined,
    traits: card.traits ?? [],
    profession: card.profession,
    effect: card.effectText,
  }
})
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
const boardControlMinimized = ref(false)
const combatDecisionMinimized = ref(false)
const inspectionLayerMinimized = computed(() => promptMinimized.value || boardControlMinimized.value || mobileMoralePickerMinimized.value || combatDecisionMinimized.value)
const phasePlaybackPhase = ref<Phase | null>(null)
const hiddenRevealCard = ref<Card | null>(null)
const publicReveal = ref<{ sequence: number; cards: Card[]; text: string } | null>(null)
const diceReveal = ref<{ sequence: number; values: number[]; animatedValues: number[]; text: string; settled: boolean } | null>(null)
const customDisasterSlot = ref<number | null>(null)
const promptMinimized = ref(false)
const responseTargetIds = ref<string[]>([])
const hasBlockingPrompt = computed(() => Boolean((props.game.prompts?.length ?? 0) > 0 || props.game.waitingPrompt))
const lastHiddenRevealSequence = ref(0)
const lastPublicRevealSequence = ref(0)
const lastDiceSequence = ref(0)
const publicRevealQueue: Array<{ sequence: number; cards: Card[]; text: string }> = []
const diceRevealQueue: Array<{ sequence: number; values: number[]; text: string }> = []
const replayZonePresentationBusy = ref(false)
const replayCombatPresentationBusy = ref(false)
let hiddenRevealTimer: ReturnType<typeof setTimeout> | null = null
let publicRevealTimer: ReturnType<typeof setTimeout> | null = null
let diceRollTimer: ReturnType<typeof setInterval> | null = null
let diceSettleTimer: ReturnType<typeof setTimeout> | null = null
let diceHideTimer: ReturnType<typeof setTimeout> | null = null
const replayCardPresentationBusy = computed(() => Boolean(props.replayPlaybackSpeed && (
  hiddenRevealCard.value || publicReveal.value || replayZonePresentationBusy.value || replayCombatPresentationBusy.value
)))
watch(replayCardPresentationBusy, busy => emit('replayPresentationChange', busy), { immediate: true })

function cardRevealDuration() {
  if (!props.replayPlaybackSpeed) return l12AnimationDuration(3000, 700)
  return Math.max(300, Math.round(l12AnimationDuration(1600, 420) / props.replayPlaybackSpeed))
}
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
const absentIdentityLabels = new Set(['未定级', '暂无段位', '无段位', '未评级', '暂无称号', '无称号', '未获得称号', '暂无'])
function identityLabel(value: string | null | undefined) {
  const label = value?.trim() ?? ''
  return absentIdentityLabels.has(label) ? '' : label
}
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
  me.value.spendableResourceCount ?? (me.value.morale.filter(card => !card.tapped).length
  + (me.value.temporaryMorale ?? 0)
  + (props.game.activePlayer === me.value.playerIndex
    ? me.value.field.flat().filter(card => card?.cardId === 'S01-0212' && !card.tapped && !card.hidden).length : 0)),
)
const isCounter = (card?: Card | null) => isCounterTacticCard(card)
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
  if (props.game.phase === 'Defense' && props.game.pendingDefense?.stage === 'DefenseChoice' && controlledPlayerIndex.value === 1 - props.game.pendingDefense.attackerPlayer && defenseTargetType.value === 'master')
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
  ?.validChoices.filter(id => id !== 'skip' && id !== 'cancel') ?? [])
// The compact resource strip is always a readable entry point on a phone.  A
// real resource prompt additionally turns the same large sheet into a selector;
// merely viewing morale never changes game state.
const mobileMoralePickerEnabled = computed(() => mobileLandscapeViewport.value)
const mobileMoraleInteractive = computed(() => Boolean(resourceSelectionPrompt.value))
type MobileMoraleCandidate = {
  id: string
  label: string
  detail: string
  iconUrl: string
  state: 'rune' | 'morale' | 'god-power' | 'black-lotus' | 'temporary'
  selectable: boolean
  disabledReason: string
}
const mobileRuneChoices = computed<MobileMoraleCandidate[]>(() => {
  if (viewMe.value.faction !== 'otherworld') return []
  const runeIds = paymentChoiceIds.value.filter(id => /^rune:\d+$/.test(id))
  const runeCount = Math.max(0, viewMe.value.specialZones?.runes ?? 0)
  const highestPromptRune = Math.max(0, ...runeIds.map(id => Number(id.split(':')[1]) || 0))
  const slotCount = Math.max(runeCount, highestPromptRune)
  return Array.from({ length: slotCount }, (_, offset) => {
    const index = offset + 1
    const id = `rune:${index}`
    const owned = index <= runeCount
    const selectable = mobileMoraleInteractive.value && paymentChoiceIds.value.includes(id)
    return {
      id,
      label: `彼界符文 ${index}`,
      detail: selectable ? '可用于当前选择' : !owned ? '尚未获得这枚符文' : mobileMoraleInteractive.value ? '当前支付不能使用这枚符文' : '当前拥有；需要符文时可选择',
      iconUrl: roundCardUrl('S02-06S1') ?? '',
      state: 'rune' as const,
      selectable,
      disabledReason: selectable ? '' : !owned ? '尚未获得这枚符文' : mobileMoraleInteractive.value ? '当前支付不能使用这枚符文' : '查看状态时无需选择',
    }
  })
})
const mobileMoraleChoices = computed<MobileMoraleCandidate[]>(() => {
  const choiceIds = mobileMoraleInteractive.value
    ? paymentChoiceIds.value
    : viewMe.value.morale.map(resource => resource.instanceId)
  return choiceIds.flatMap<MobileMoraleCandidate>(id => {
  if (id.startsWith('temporary-morale:')) return [{
    id,
    label: '临时士气',
    detail: '休整时消失',
     iconUrl: siteBrandIconUrl,
     state: 'temporary' as const,
     selectable: mobileMoraleInteractive.value && paymentChoiceIds.value.includes(id),
     disabledReason: mobileMoraleInteractive.value && paymentChoiceIds.value.includes(id) ? '' : mobileMoraleInteractive.value ? '当前支付不能使用这枚临时士气' : '查看状态时无需选择',
  }]
  const owner = [viewMe.value, viewEnemy.value].find(player => player.morale.some(resource => resource.instanceId === id))
  const resource = owner?.morale.find(item => item.instanceId === id)
  if (!owner || !resource) return []
  const godPower = Boolean(resource.isGodPower)
  const blackLotus = resource.resourceType === 'black-lotus'
  return [{
    id,
    label: godPower ? '神力' : blackLotus ? '黑色莲花' : '士气',
    detail: `${blackLotus ? '专属士气' : getFactionPresentation(owner.faction).label} · ${resource.tapped ? '休整' : '活跃'}`,
     iconUrl: godPower ? godPowerLogoUrl : blackLotus ? blackLotusLogoUrl : (factionLogoUrls[owner.faction] ?? ''),
     state: godPower ? 'god-power' as const : blackLotus ? 'black-lotus' as const : 'morale' as const,
     selectable: mobileMoraleInteractive.value && paymentChoiceIds.value.includes(id),
     disabledReason: mobileMoraleInteractive.value && paymentChoiceIds.value.includes(id) ? '' : mobileMoraleInteractive.value ? (resource.tapped ? '这枚士气正在休整' : '当前支付不能使用这枚士气') : '查看状态时无需选择',
  }]
  })
})
const activeBoardPromptIds = computed(() => [
  boardTargetPrompt.value?.promptId,
  boardSlotPrompt.value?.promptId,
  resourceSelectionPrompt.value?.promptId,
].filter((promptId): promptId is string => Boolean(promptId)))
const activeBoardPromptId = computed(() => activeBoardPromptIds.value[0] ?? null)
watch(activeBoardPromptId, () => { boardControlMinimized.value = false })
watch(() => [props.game.phase, props.game.pendingDefense?.stage, props.game.turnSerial], () => {
  combatDecisionMinimized.value = false
})
const modalInspectorVisible = computed(() => Boolean(!mobileLandscapeViewport.value && focusCard.value && (
  graveyardPlayer.value !== null || !promptMinimized.value && (
    masterPlayerIndex.value !== null || props.game.phase === 'Mulligan'
    || props.game.phase === 'DisasterPreparation' || props.game.phase === 'Disaster'
    || (props.game.prompts?.length ?? 0) > 0 || props.game.waitingPrompt
  )
)))
const modalPresentationPaused = computed(() => Boolean(
  activeBoardPromptId.value
  // PromptOverlay is a modal while it is expanded. Card presentations that
  // arrive with its prompt stay queued until the player closes or minimizes it.
  || (hasBlockingPrompt.value && !promptMinimized.value)
  || graveyardPlayer.value !== null || masterPlayerIndex.value !== null
  || customDisasterSlot.value !== null || mobileRecordOpen.value || mobileMoralePickerOpen.value
  || modalInspectorVisible.value,
))
const passivePresentationPaused = computed(() => Boolean(
  publicReveal.value || diceReveal.value || hiddenRevealCard.value || modalPresentationPaused.value,
))
function updateInspectorFloatRect() {
  if (!modalInspectorVisible.value || !inspectorAnchor.value) return
  const rect = viewportRect(inspectorAnchor.value)
  const logicalWidth = inspectorAnchor.value.offsetWidth || rect.width
  const logicalHeight = inspectorAnchor.value.offsetHeight || rect.height
  const floatScale = logicalWidth > 0 ? rect.width / logicalWidth : 1
  inspectorFloatStyle.value = {
    left: `${rect.left}px`,
    top: `${rect.top}px`,
    width: `${logicalWidth}px`,
    height: `${logicalHeight}px`,
    transform: `scale(${floatScale})`,
    '--l12-board-copy': `${adaptiveBoardToken(13, floatScale)}px`,
    '--l12-board-meta': `${adaptiveBoardToken(11, floatScale)}px`,
    '--l12-board-micro': `${adaptiveBoardToken(9, floatScale)}px`,
    '--l12-effect-copy': `${adaptiveBoardToken(13, floatScale)}px`,
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
function isCurrentTrial(trials: Array<{ instanceId: string; trialCompleted?: boolean; trialProgress?: number }> | undefined, trial: { instanceId: string }) {
  return trials?.find(candidate => !candidate.trialCompleted
    && (candidate.trialProgress ?? 0) < 8)?.instanceId === trial.instanceId
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
    isCounterTactic: prompt.data?.[`${id}:isCounterTactic`] === 'true',
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
watch(() => resourceSelectionPrompt.value?.promptId, () => {
  paymentResourceIds.value = []
  mobileMoralePickerOpen.value = false
})
watch(mobileMoralePickerEnabled, enabled => {
  if (!enabled) mobileMoralePickerOpen.value = false
})
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
  hiddenRevealTimer = setTimeout(() => { hiddenRevealCard.value = null }, cardRevealDuration())
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
  // A modal owns the screen, and an authoritative zone movement owns the card
  // presentation lane.  Do not start the secondary reveal until both yield.
  if (publicReveal.value || !publicRevealQueue.length || modalPresentationPaused.value || replayZonePresentationBusy.value) return
  publicReveal.value = publicRevealQueue.shift() ?? null
  if (!publicReveal.value) return
  if (publicRevealTimer) clearTimeout(publicRevealTimer)
  publicRevealTimer = setTimeout(() => {
    publicReveal.value = null
    publicRevealTimer = null
    showNextPublicReveal()
  }, cardRevealDuration())
}
function publicRevealText(event: ActionEvent) {
  const override = event.effectText?.trim()
  if (override) {
    if (event.effectResultStatus === 'negated') return `${override}（被无效）`
    if (event.effectResultStatus === 'skipped') return `${override}（无合法处理对象，跳过）`
    if (event.effectResultStatus === 'failed') return `${override}（未能完成结算）`
    return override
  }
  const text = event.text.trim()
  const card = event.cards?.[0]
  if (card && /花魁的馈赠/.test(text)) return `花魁的馈赠将〈${card.name}〉加入手牌`
  return text
}
// Disaster reveals have their own authoritative back-to-face movement.  Keep
// them out of the secondary public-card overlay so the two animations cannot
// cover or cancel each other.
function isDisasterRevealEvent(event: ActionEvent) { return event.type === 'disaster-reveal' }
function diceValuesFromEvent(event: ActionEvent) {
  const result = event.text.match(/结果为\s*([1-6])/)?.[1]
  if (result) return [Number(result)]
  const rollText = event.text.split('掷骰：')[1] ?? ''
  return [...rollText.matchAll(/(?:^|\s)([1-6])(?=，|,|。|$)/g)].map(match => Number(match[1])).slice(-2)
}
function showNextDiceReveal() {
  if (diceReveal.value || !diceRevealQueue.length || modalPresentationPaused.value || replayZonePresentationBusy.value) return
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
    .filter(event => !isDisasterRevealEvent(event) && event.cards?.length && event.sequence > lastPublicRevealSequence.value
      && (event.playerIndex === null || event.playerIndex !== props.game.you)
      && (event.type === 'effect-result'
        || (event.effectResultStatus !== 'declared'
          && (event.type === 'effect-trigger' || event.type === 'effect-response' || event.type === 'effect-activation'))
        || event.type === 'reveal' || event.text.includes('展示')
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
  // Let ZoneMovementPresentationLayer observe the same authoritative batch
  // first.  A disaster-reveal owns the flip animation; its following effect
  // presentation waits for that movement instead of cancelling it.
  void nextTick(showNextPublicReveal)
})
function openMobileMoralePicker() {
  mobileMoralePickerMinimized.value = false
  mobileMoraleReason.value = ''
  mobileMoralePickerOpen.value = true
}
function chooseMobileMorale(choice:MobileMoraleCandidate){
  if(!choice.selectable){mobileMoraleReason.value=choice.disabledReason||'当前不能选择';return}
  mobileMoraleReason.value=''
  togglePaymentResource(choice.id)
}
function inspectDialogCard(card: Card) {
  focusCard.value = card
  if (mobileLandscapeViewport.value) mobileInspectorOpen.value = true
}
function inspectMasterCard(playerIndex: number) {
  const player = props.game.players[playerIndex]
  if (!player) return
  const card:Card={
    instanceId: `master-${playerIndex}`,
    cardId: player.master.masterId,
    name: player.master.masterName,
    cardType: 'master',
    faction: player.faction,
    imageUrl: player.master.masterImageUrl,
    effectText: player.master.effectText,
    cost: 0,
    baseTroops: 0,
    troops: 0,
    disasterLevel: 0,
    tapped: Boolean(player.master.tapped),
    summonRound: 0,
    abilities: player.master.abilities,
  }
  inspectDialogCard(card)
}
function focusMasterCard(playerIndex:number){
  const player=props.game.players[playerIndex]
  if(!player)return
  focusCard.value={instanceId:`master-${playerIndex}`,cardId:player.master.masterId,name:player.master.masterName,cardType:'master',faction:player.faction,imageUrl:player.master.masterImageUrl,effectText:player.master.effectText,cost:0,baseTroops:0,troops:0,disasterLevel:0,tapped:Boolean(player.master.tapped),summonRound:0,abilities:player.master.abilities}
}
watch([modalPresentationPaused, replayZonePresentationBusy], ([modalPaused, zoneBusy], [wasModalPaused]) => {
  if (modalPaused && !wasModalPaused) {
    // A presentation that was already visible yields permanently to a newly
    // opened modal.  It is never pushed back into the queue for replay.
    if (publicRevealTimer) clearTimeout(publicRevealTimer)
    publicRevealTimer = null
    publicReveal.value = null
    if (diceRollTimer) clearInterval(diceRollTimer)
    if (diceSettleTimer) clearTimeout(diceSettleTimer)
    if (diceHideTimer) clearTimeout(diceHideTimer)
    diceRollTimer = null
    diceSettleTimer = null
    diceHideTimer = null
    diceReveal.value = null
  }
  if (!modalPaused && !zoneBusy) {
    showNextPublicReveal()
    showNextDiceReveal()
  }
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
    attackUnit: pending.attackValue > 0 ? '进攻值' : '兵力',
    targetName: target?.name ?? targetOwner.master.masterName,
    targetValue: target ? target.troops + supports.reduce((sum, card) => sum + card.troops, 0) : targetOwner.master.hp,
    targetUnit: target ? '兵力' : '血量',
  }
})
const combatStageLabel = computed(() => {
  const stage = props.game.pendingDefense?.stage
  if (stage === 'AttackerAttackTiming') return '进攻宣告'
  if (stage === 'DefenderAttackTiming') return '进攻响应'
  if (stage === 'DefenseChoice') return '抵挡与支援'
  if (stage === 'CombatDamage') return '伤害结算'
  if (stage === 'KillTriggers' || stage === 'DefenderKillTriggers') return '击杀结算'
  if (stage === 'AttackerDeathTriggers' || stage === 'DefenderDeathTriggers' || stage === 'FinalizeDeaths') return '阵亡结算'
  if (stage === 'AttackerAfterAttack' || stage === 'DefenderAfterAttack') return '进攻后结算'
  if (stage === 'Complete') return '战斗完成'
  return '战斗结算'
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

onMounted(() => {
  lastHiddenRevealSequence.value = Math.max(0, ...(props.game.recentEvents ?? []).map(event => event.sequence))
  lastPublicRevealSequence.value = lastHiddenRevealSequence.value
  lastDiceSequence.value = lastHiddenRevealSequence.value
})
onBeforeUnmount(() => {
  if (hiddenRevealTimer) clearTimeout(hiddenRevealTimer)
  if (publicRevealTimer) clearTimeout(publicRevealTimer)
  if (diceRollTimer) clearInterval(diceRollTimer)
  if (diceSettleTimer) clearTimeout(diceSettleTimer)
  if (diceHideTimer) clearTimeout(diceHideTimer)
  emit('replayPresentationChange', false)
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
  if (card) focusCard.value = card
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
function inspectActiveDisaster() {
  if (!props.game.activeDisaster) return
  focusCard.value = props.game.activeDisaster
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
  playArmed.value = !mobileLandscapeViewport.value && selectedId.value === card.instanceId && (card.cardType === 'legion' || isCounter(card)) && playableIds.value.includes(card.instanceId)
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
function cancelResourcePayment() {
  const prompt = resourceSelectionPrompt.value
  if (!prompt?.validChoices.includes('cancel')) return
  command('resolvePrompt', { promptId: prompt.promptId, cardInstanceIds: ['cancel'] })
}
function confirmMobileMoralePayment(skip = false) {
  confirmResourcePayment(skip)
  mobileMoralePickerOpen.value = false
}
function cancelMobileMoralePayment() {
  cancelResourcePayment()
  mobileMoralePickerOpen.value = false
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
  <div class="board-viewport" :class="{ 'compact-viewport': compactViewport, 'mobile-landscape-board': mobileLandscapeViewport, 'read-only-board': readOnly, 'gm-panel-docked': gmPanelOpen && !compactViewport }" :data-l12-battle-layout="mobileLandscapeViewport ? 'mobile' : 'desktop'" :data-l12-mobile-landscape="mobileLandscapeViewport ? 'true' : undefined">
    <Teleport :to="landscapeTeleportTarget()">
      <button v-if="mobileLandscapeViewport" type="button" class="mobile-card-inspector-handle mobile-card-inspector-handle-global" :class="{ open: mobileInspectorOpen }" :aria-expanded="mobileInspectorOpen" @click="mobileInspectorOpen = !mobileInspectorOpen">{{ mobileInspectorOpen ? '收起详情' : '展开卡牌详情' }}</button>
    </Teleport>
    <div class="board-stage" :style="{ width: `${stageSize.width}px`, height: `${stageSize.height}px`, transform: `scale(${scale})`, '--l12-board-copy': `${adaptiveBoardToken(13, scale)}px`, '--l12-board-meta': `${adaptiveBoardToken(11, scale)}px`, '--l12-board-micro': `${adaptiveBoardToken(9, scale)}px`, '--l12-effect-copy': `${adaptiveBoardToken(13, scale)}px` }">
      <div class="stage-layout">
        <aside class="board-rail left-rail">
          <span v-if="mobileLandscapeViewport" class="mobile-detail-handle-reservation" aria-hidden="true" />
          <section v-if="mobileLandscapeViewport && viewEnemy.specialZones?.trials?.length" class="mobile-extra-zone mobile-extra-zone-opponent" aria-label="对手额外区">
            <small>对手额外区</small>
            <div>
              <button v-for="trial in viewEnemy.specialZones.trials" :key="trial.instanceId" type="button" class="mobile-extra-card"
                :class="{ concealed: trial.hidden, inactive: !trial.hidden && !trial.trialCompleted }"
                :disabled="trial.hidden" :title="trial.hidden ? '对手未揭示的试炼' : trial.name"
                @mouseenter="!trial.hidden && (focusCard = trial)" @click.stop="!trial.hidden && selectPublicCardFor(viewEnemy.playerIndex, trial)">
                <img v-if="trial.hidden" class="trial-card-back" src="/assets/l12/trial-back.png" alt="对手未揭示的试炼" />
                <CardImage v-else :card-id="trial.cardId" :legacy-url="trial.imageUrl" :alt="trial.name" intent="board" eager />
                <b v-if="isCurrentTrial(viewEnemy.specialZones?.trials, trial)" aria-label="当前试炼进度">{{ trial.trialProgress ?? viewEnemy.specialZones?.trialLevel ?? 0 }}</b>
              </button>
            </div>
          </section>
          <div v-if="sessionDisasters.length || game.activeDisaster" class="left-disaster-row">
            <section v-if="sessionDisasters.length" class="grand-panel session-disaster-panel" aria-label="本局天灾">
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
                <button type="button" class="current-disaster-card" data-l12-zone="disaster" :disabled="!game.activeDisaster"
                  @mouseenter="game.activeDisaster && (focusCard = game.activeDisaster)" @click="inspectActiveDisaster">
                  <CardImage v-if="game.activeDisaster" :card-id="game.activeDisaster.cardId" :legacy-url="game.activeDisaster.imageUrl" :alt="game.activeDisaster.name" intent="board" eager />
                  <img v-else src="/assets/l12/card-back-disaster.png" alt="天灾牌背" />
                  <span class="mobile-current-disaster-copy"><b>当前天灾</b><i>{{ game.activeDisaster?.name || '尚未揭示' }}</i></span>
                </button>
            </section>
            <section v-if="mobileLandscapeViewport" class="mobile-current-disaster-value" aria-label="当前天灾值">
              <img src="/assets/l12/disaster-icon-source.png" alt="" />
              <span>天灾值</span><b>{{ game.disasterValue }}</b>
            </section>
          </div>
          <section v-if="mobileLandscapeViewport && viewMe.specialZones?.trials?.length" class="mobile-extra-zone mobile-extra-zone-my" aria-label="我方额外区">
            <small>我方额外区</small>
            <div>
              <button v-for="trial in viewMe.specialZones.trials" :key="trial.instanceId" type="button" class="mobile-extra-card"
                :class="{ concealed: trial.hidden, inactive: !trial.hidden && !trial.trialCompleted }"
                :title="trial.hidden ? '我方未揭示的试炼' : trial.name"
                @mouseenter="focusCard = trial" @click.stop="selectPublicCardFor(viewMe.playerIndex, trial)">
                <CardImage :card-id="trial.cardId" :legacy-url="trial.imageUrl" :alt="trial.name" intent="board" eager />
                <b v-if="isCurrentTrial(viewMe.specialZones?.trials, trial)" aria-label="当前试炼进度">{{ trial.trialProgress ?? viewMe.specialZones?.trialLevel ?? 0 }}</b>
              </button>
            </div>
          </section>
          <div class="left-detail-layout">
            <div class="left-card-column">
              <div ref="inspectorAnchor" class="card-inspector-anchor" data-ui-contract="selected-card-inspector-anchor">
              <Teleport :to="landscapeTeleportTarget()" :disabled="!modalInspectorVisible">
                <div class="board-rail inspector-style-scope">
                <section class="grand-panel card-inspector archive-detail" data-ui-contract="selected-card-inspector" :style="modalInspectorVisible ? inspectorFloatStyle : undefined" :class="{ 'card-inspector-floating': modalInspectorVisible, 'horizontal-inspector': focusCard && isHorizontalCardType(focusCard.cardType) }">
                  <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
                  <template v-if="focusCard && focusDetailCard">
                    <CardDetailContent :card="focusDetailCard" :show-catalog-only="false" />
                    <section v-if="statusTexts(focusCard).length" class="archive-effect battle-card-status"><b>当前状态</b><ul class="inspector-statuses"><li v-for="text in statusTexts(focusCard)" :key="text">{{ text }}</li></ul></section>
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
            :show-play-action="!hasBlockingPrompt && isControlledPlayer(viewEnemy.playerIndex) && isMyMain && !l12State.pendingAction" :confirm-all-playable="mobileLandscapeViewport" :mobile-layout="mobileLandscapeViewport"
            @select="selectHandFor(viewEnemy.playerIndex, $event)" @play="playFromHandFor(viewEnemy.playerIndex, $event)" @focus="focusCard = $event" />
          <HandArea v-else class="opponent-hand" hidden :count="viewEnemy.handCount || 0" :player-index="viewEnemy.playerIndex" />
          <div class="board-status-lane opponent-status-lane" data-ui-contract="opponent-status-safe-lane">
            <PlayerTurnClock class="board-player-clock opponent-player-clock" :player-index="viewEnemy.playerIndex" side="opponent"
              :active="game.activePlayer === viewEnemy.playerIndex" :phase="game.phase" :ranked-clock="l12State.rankedClock" />
          </div>
          <div class="felt-board" data-l12-game-board data-ui-contract="persistent-board-safe-layout">
            <PlayerMat class="battlefield-half opponent-half" :player="viewEnemy" side="opponent" :controllable="isControlledPlayer(viewEnemy.playerIndex)"
              :mobile-layout="mobileLandscapeViewport"
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
              :selected-target-ids="boardTargetIds" :response-target-ids="promptMinimized ? responseTargetIds : []"
              :can-activate-osiris="isControlledPlayer(viewEnemy.playerIndex) && canActivateOsiris"
              :osiris-victory-disabled-reason="osirisVictoryDisabledReason"
              :combat-attacker-id="combat?.attackerOwner.playerIndex === viewEnemy.playerIndex ? combat.attacker.instanceId : null"
              :combat-target-id="combat?.targetOwner.playerIndex === viewEnemy.playerIndex ? combat.target?.instanceId : null"
              :combat-target-master="combat?.targetOwner.playerIndex === viewEnemy.playerIndex && !combat.target"
              :payment-choice-ids="paymentChoiceIds" :payment-selected-ids="paymentResourceIds"
              :mobile-morale-picker="mobileMoralePickerEnabled"
              :master-targetable="!isControlledPlayer(viewEnemy.playerIndex) && !combat && selectedAttackTargets.includes('master')"
              @slot="(row, slot, card) => slotFor(viewEnemy.playerIndex, row, slot, card)" @master="masterFor(viewEnemy.playerIndex)"
              @focus="focusCard = $event" @inspect="inspectDialogCard" @graveyard="(!hasBlockingPrompt || inspectionLayerMinimized) && (graveyardPlayer = $event)"
              @card-action="(action, card) => fieldActionFor(viewEnemy.playerIndex, action, card)"
              @ability="(card, ability) => activateAbilityFor(viewEnemy.playerIndex, card, ability)"
              @faction-ability="ability => activateFactionAbilityFor(viewEnemy.playerIndex, ability)"
              @select-card="card => selectPublicCardFor(viewEnemy.playerIndex, card)" @payment-resource="togglePaymentResource" @open-morale-payment="openMobileMoralePicker" />
            <div class="board-seam" data-ui-contract="phase-safe-track">
              <span class="board-midline-anchor" aria-hidden="true" />
            </div>
            <PhasePlayback :events="game.recentEvents ?? []" @phase-change="phasePlaybackPhase = $event" />
            <ActionPresentationLayer :events="game.recentEvents ?? []" :match-id="game.matchId" :player-names="game.players.map(player => player.name)"
              :paused="passivePresentationPaused" />
            <ZoneMovementPresentationLayer :events="game.recentEvents ?? []" :match-id="game.matchId"
              :viewer-player-index="game.you" :paused="passivePresentationPaused" :playback-speed="replayPlaybackSpeed"
              @busy-change="replayZonePresentationBusy = $event" />
            <CombatMotionPresentationLayer :events="game.recentEvents ?? []" :match-id="game.matchId"
              :playback-speed="replayPlaybackSpeed" @busy-change="replayCombatPresentationBusy = $event" />
            <Teleport :to="landscapeTeleportTarget()">
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
            <div v-if="combat && !activeBoardPromptId" class="combat-presentation" :class="{ 'combat-presentation--passive': game.pendingDefense?.stage !== 'DefenseChoice' }">
              <i class="combat-trace"/>
              <div class="combat-versus">
                <small v-if="mobileLandscapeViewport" class="combat-stage-label">{{ combatStageLabel }}</small>
                <span :class="combat.attackerOwner.playerIndex === game.you ? 'mine' : 'opponent'">{{ combat.attackerOwner.playerIndex === game.you ? '我方' : '对手' }} · {{ combat.attacker.name }}</span>
                <b>{{ combat.attackValue }}<small>{{ combat.attackUnit }}</small></b>
                <em>⚔</em>
                <span :class="combat.targetOwner.playerIndex === game.you ? 'mine' : 'opponent'">{{ combat.targetOwner.playerIndex === game.you ? '我方' : '对手' }} · {{ combat.targetName }}</span>
                <b>{{ combat.targetValue }}<small>{{ combat.targetUnit }}</small></b>
              </div>
              <div v-if="game.phase === 'Defense' && game.pendingDefense?.stage === 'DefenseChoice' && !readOnly && !combatDecisionMinimized" class="combat-resolution-panel">
                <button v-if="mobileLandscapeViewport" class="combat-decision-minimize" type="button" aria-label="最小化支援或抵挡选择" @click="combatDecisionMinimized = true">−</button>
                <GameActions :game="game" :me="me" :mode="mode" :selected-id="selectedId"
                  :mulligan-count="mulliganIds.length" :defense-count="defenseIds.length" :defense-target-type="defenseTargetType"
                  :support-ids="supportIds" :can-support="eligibleSupportIds.length > 0" :support-ready="supportReady" :busy="l12State.pendingAction" @command="command" />
              </div>
            </div>
            <button v-if="mobileLandscapeViewport && combat && game.phase === 'Defense' && game.pendingDefense?.stage === 'DefenseChoice' && combatDecisionMinimized"
              class="combat-decision-restore" type="button" @click="combatDecisionMinimized = false">恢复支援/抵挡</button>
            <PlayerMat class="battlefield-half my-half" :player="viewMe" side="my" :controllable="isControlledPlayer(viewMe.playerIndex)"
              :mobile-layout="mobileLandscapeViewport"
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
              :selected-target-ids="boardTargetIds" :response-target-ids="promptMinimized ? responseTargetIds : []" :payment-choice-ids="paymentChoiceIds" :payment-selected-ids="paymentResourceIds" :mobile-morale-picker="mobileMoralePickerEnabled"
              :can-activate-osiris="isControlledPlayer(viewMe.playerIndex) && canActivateOsiris"
              :osiris-victory-disabled-reason="osirisVictoryDisabledReason"
              :combat-attacker-id="combat?.attackerOwner.playerIndex === viewMe.playerIndex ? combat.attacker.instanceId : null"
              :combat-target-id="combat?.targetOwner.playerIndex === viewMe.playerIndex ? combat.target?.instanceId : null"
              :combat-target-master="combat?.targetOwner.playerIndex === viewMe.playerIndex && !combat.target"
              :master-targetable="!isControlledPlayer(viewMe.playerIndex) && !combat && selectedAttackTargets.includes('master')"
              @slot="(row, slot, card) => slotFor(viewMe.playerIndex, row, slot, card)" @master="masterFor(viewMe.playerIndex)"
              @focus="focusCard = $event" @inspect="inspectDialogCard" @graveyard="(!hasBlockingPrompt || inspectionLayerMinimized) && (graveyardPlayer = $event)"
              @card-action="(action, card) => fieldActionFor(viewMe.playerIndex, action, card)"
              @select-card="card => selectPublicCardFor(viewMe.playerIndex, card)"
              @ability="(card, ability) => activateAbilityFor(viewMe.playerIndex, card, ability)"
              @faction-ability="ability => activateFactionAbilityFor(viewMe.playerIndex, ability)"
              @payment-resource="togglePaymentResource" @open-morale-payment="openMobileMoralePicker" />
          </div>
          <div class="board-status-lane my-status-lane" data-ui-contract="player-status-safe-lane">
            <PlayerTurnClock class="board-player-clock my-player-clock" :player-index="viewMe.playerIndex" side="my"
              :active="game.activePlayer === viewMe.playerIndex" :phase="game.phase" :ranked-clock="l12State.rankedClock" />
          </div>
          <HandArea v-if="l12State.spectating" class="spectator-hand" hidden :count="viewMe.handCount || 0" :player-index="viewMe.playerIndex" />
          <HandArea v-else :cards="viewMe.hand" :player-index="viewMe.playerIndex" :selected-ids="selectedHandIdsFor(viewMe.playerIndex)"
            :playable-ids="playableHandIdsFor(viewMe.playerIndex)" :dim-unplayable="isControlledPlayer(viewMe.playerIndex) && game.phase !== 'Mulligan'"
            :show-play-action="!hasBlockingPrompt && isControlledPlayer(viewMe.playerIndex) && isMyMain && !l12State.pendingAction" :confirm-all-playable="mobileLandscapeViewport" :mobile-layout="mobileLandscapeViewport"
            @select="selectHandFor(viewMe.playerIndex, $event)" @play="playFromHandFor(viewMe.playerIndex, $event)" @focus="focusCard = $event" />
        </main>

        <aside class="board-rail right-rail">
          <!-- Phone status lanes are intentionally not over the hands.  A timed
               match instead receives its own reserved pair of compact clocks in
               this otherwise unused section of the right rail. -->
          <section class="grand-panel player-panel" data-ui-contract="complete-player-summary">
            <button v-if="mobileLandscapeViewport" type="button" class="mobile-record-trigger" @click="mobileRecordOpen = true; mobileRecordMinimized = false">对局记录</button>
            <article class="player-summary opponent-summary">
              <div class="player-summary-primary"><b>对方</b><strong>{{ viewEnemy.name || '未命名玩家' }}</strong></div>
              <div class="player-summary-meta">
                <RankedIdentityBadge v-if="identityLabel(enemyBadge?.rankLabel)" class="rank-badge" :variant="enemyBadge?.rankIsTitle ? 'faction-title' : 'tier'" :faction="enemyBadge?.faction" compact :label="identityLabel(enemyBadge?.rankLabel)" />
                <RankedIdentityBadge v-if="identityLabel(enemyBadge?.masterTitle)" class="title-badge" variant="master-title" :faction="enemyBadge?.faction" compact :label="identityLabel(enemyBadge?.masterTitle)" />
                <span class="connection-state" :class="{ online: playerConnection(viewEnemy.playerIndex) }"><i/>{{ connectionLabel(viewEnemy.playerIndex) }}</span>
              </div>
            </article>
            <hr/>
            <article class="player-summary my-summary">
              <div class="player-summary-primary"><b>我方</b><strong class="mine">{{ viewMe.name || '未命名玩家' }}</strong></div>
              <div class="player-summary-meta">
                <RankedIdentityBadge v-if="identityLabel(myBadge?.rankLabel)" class="rank-badge" :variant="myBadge?.rankIsTitle ? 'faction-title' : 'tier'" :faction="myBadge?.faction" compact :label="identityLabel(myBadge?.rankLabel)" />
                <RankedIdentityBadge v-if="identityLabel(myBadge?.masterTitle)" class="title-badge" variant="master-title" :faction="myBadge?.faction" compact :label="identityLabel(myBadge?.masterTitle)" />
                <span class="connection-state" :class="{ online: playerConnection(viewMe.playerIndex) }"><i/>{{ connectionLabel(viewMe.playerIndex) }}</span>
              </div>
            </article>
          </section>
          <section v-if="mobileLandscapeViewport && l12State.rankedClock" class="mobile-timed-clocks" aria-label="双方对局计时">
            <PlayerTurnClock class="mobile-rail-clock opponent-player-clock" :player-index="viewEnemy.playerIndex" side="opponent"
              :active="game.activePlayer === viewEnemy.playerIndex" :phase="game.phase" :ranked-clock="l12State.rankedClock" />
            <PlayerTurnClock class="mobile-rail-clock my-player-clock" :player-index="viewMe.playerIndex" side="my"
              :active="game.activePlayer === viewMe.playerIndex" :phase="game.phase" :ranked-clock="l12State.rankedClock" />
          </section>
          <section class="grand-panel log-panel record-log"><h3>对局记录</h3>
            <BattleEventLog :events="game.recentEvents ?? []" :you="game.you" :names="game.players.map(player => player.name)" @focus="focusCard = $event" />
          </section>
          <section v-if="!combat && !readOnly" class="grand-panel action-panel" :class="{ 'mobile-context-actions': mobileLandscapeViewport }"><h3>操作</h3><GameActions :game="game" :me="me" :mode="mode" :selected-id="selectedId"
            :mulligan-count="mulliganIds.length" :defense-count="defenseIds.length" :defense-target-type="defenseTargetType"
            :support-ids="supportIds" :can-support="eligibleSupportIds.length > 0" :support-ready="supportReady" :busy="l12State.pendingAction" @command="command" /></section>
        </aside>
      </div>
      <Teleport :to="landscapeTeleportTarget()">
        <section v-if="mobileLandscapeViewport && mobileRecordOpen" class="mobile-record-overlay mobile-safe-overlay" role="dialog" aria-modal="true" aria-label="对局记录">
          <header><h2>对局记录</h2><div class="mobile-record-actions"><button type="button" @click="mobileRecordOpen = false; mobileRecordMinimized = true">最小化</button><button type="button" @click="mobileRecordOpen = false; mobileRecordMinimized = false">关闭</button></div></header>
          <BattleEventLog :events="game.recentEvents ?? []" :you="game.you" :names="game.players.map(player => player.name)" @focus="focusCard = $event" />
        </section>
      </Teleport>
      <button v-if="mobileLandscapeViewport && mobileRecordMinimized" class="mobile-record-restore" type="button" @click="mobileRecordOpen = true; mobileRecordMinimized = false">恢复对局记录</button>
      <Teleport :to="landscapeTeleportTarget()">
        <Transition name="mobile-card-inspector">
          <aside v-if="mobileLandscapeViewport && mobileInspectorOpen" class="mobile-card-inspector mobile-safe-overlay" role="dialog" aria-modal="false" aria-label="卡牌详情">
            <header><div><small>卡牌详情</small><h2>{{ focusCard?.name || '选择一张卡牌' }}</h2></div><button type="button" @click="mobileInspectorOpen = false">收起</button></header>
            <div v-if="focusCard && focusDetailCard" class="archive-detail mobile-card-detail-body">
              <CardDetailContent :card="focusDetailCard" :show-catalog-only="false" />
              <section v-if="statusTexts(focusCard).length" class="archive-effect battle-card-status"><b>当前状态</b><ul class="inspector-statuses"><li v-for="text in statusTexts(focusCard)" :key="text">{{ text }}</li></ul></section>
            </div>
            <p v-else class="mobile-inspector-empty">点击手牌、场上卡牌、圣物、试炼或当前天灾，即可在此查看完整信息。</p>
          </aside>
        </Transition>
      </Teleport>
      <Teleport :to="landscapeTeleportTarget()">
        <section v-if="mobileMoralePickerEnabled && mobileMoralePickerOpen" class="mobile-record-overlay mobile-morale-overlay mobile-safe-overlay" role="dialog" aria-modal="true" aria-label="选择士气">
          <header><div><h2>{{ mobileMoraleInteractive ? '选择士气' : '我方士气' }}</h2><small>{{ mobileMoraleInteractive ? `已选择 ${paymentResourceIds.length}/${resourceSelectionPrompt?.maxChoose ?? 0}` : `活跃 ${viewMe.morale.filter(item => !item.tapped).length} / 共 ${viewMe.morale.length}` }}</small></div><div class="mobile-morale-header-actions"><button type="button" @click="mobileMoralePickerOpen = false; mobileMoralePickerMinimized = true">最小化</button><button type="button" @click="mobileMoralePickerOpen = false; mobileMoralePickerMinimized = false">返回对局</button></div></header>
          <p class="mobile-morale-prompt">{{ resourceSelectionPrompt?.text || '这里展示当前士气状态；需要支付或返还时会自动变为可选择面板。' }}</p>
          <div class="mobile-morale-picker" aria-label="可选择的士气与符文">
            <section v-if="viewMe.faction === 'otherworld'" class="mobile-rune-row" aria-label="彼界阵营符文">
              <div v-if="mobileRuneChoices.length">
                <button v-for="choice in mobileRuneChoices" :key="choice.id" type="button" :class="['mobile-morale-choice', choice.state, { selected: paymentResourceIds.includes(choice.id), unavailable: !choice.selectable }]" :aria-pressed="paymentResourceIds.includes(choice.id)" :aria-disabled="!choice.selectable" :aria-label="`${choice.label}${choice.disabledReason ? `：${choice.disabledReason}` : ''}`" :title="choice.disabledReason || choice.label" @click="chooseMobileMorale(choice)">
                  <img :src="choice.iconUrl" alt="" />
                </button>
              </div>
            </section>
            <section class="mobile-resource-row" aria-label="普通士气与特殊士气">
              <button v-for="choice in mobileMoraleChoices" :key="choice.id" type="button" :class="['mobile-morale-choice', choice.state, { selected: paymentResourceIds.includes(choice.id), unavailable: !choice.selectable }]" :aria-pressed="paymentResourceIds.includes(choice.id)" :aria-disabled="!choice.selectable" :aria-label="`${choice.label}${choice.disabledReason ? `：${choice.disabledReason}` : ''}`" :title="choice.disabledReason || choice.label" @click="chooseMobileMorale(choice)">
                <img :src="choice.iconUrl" alt="" />
              </button>
            </section>
            <p v-if="mobileMoraleReason" class="mobile-morale-reason" role="status">{{ mobileMoraleReason }}</p>
            <p v-if="!mobileMoraleChoices.length">{{ mobileMoraleInteractive ? '当前提示没有可选择的士气。' : '当前没有士气。' }}</p>
          </div>
          <footer v-if="resourceSelectionPrompt" class="mobile-morale-actions">
            <button v-if="resourceSelectionPrompt.validChoices.includes('skip')" type="button" @click="confirmMobileMoralePayment(true)">不发动</button>
            <button v-if="resourceSelectionPrompt.validChoices.includes('cancel')" type="button" @click="cancelMobileMoralePayment">取消打出</button>
            <button class="primary" type="button" :disabled="paymentResourceIds.length < resourceSelectionPrompt.minChoose || paymentResourceIds.length > resourceSelectionPrompt.maxChoose" @click="confirmMobileMoralePayment(false)">{{ resourceSelectionPrompt.kind === 'resource-return' || resourceSelectionPrompt.data?.choiceMode === 'resource-return' ? '确认返还' : resourceSelectionPrompt.kind === 'resource-payment' || resourceSelectionPrompt.data?.choiceMode === 'resource-payment' ? '确认支付' : '确认选择' }}</button>
          </footer>
        </section>
      </Teleport>
      <button v-if="mobileMoralePickerEnabled && mobileMoralePickerMinimized" class="mobile-morale-restore" type="button" @click="openMobileMoralePicker">恢复士气选择</button>
      <GraveyardOverlay v-if="graveyardPlayer !== null" :players="[viewMe, viewEnemy]" :initial-player="graveyardPlayer"
        :own-player-index="game.you" :can-activate-osiris="canActivateOsiris" :inspection-only="hasBlockingPrompt"
        :mobile-layout="mobileLandscapeViewport"
        @close="graveyardPlayer = null" @focus="focusCard = $event" @inspect="inspectDialogCard" @ability="activateAbility" />
      <MasterOverlay v-if="masterPlayerIndex !== null" :player="game.players[masterPlayerIndex]" :mine="masterPlayerIndex === controlledPlayerIndex"
        :can-activate="!readOnly && masterPlayerIndex === controlledPlayerIndex && isMyMain" :busy="l12State.pendingAction" :mobile-layout="mobileLandscapeViewport" @close="masterPlayerIndex = null" @activate="activateMaster" @focus="focusMasterCard(masterPlayerIndex)" @inspect="inspectMasterCard(masterPlayerIndex)" />
      <div v-if="gmPlacement && !readOnly && !boardControlMinimized" class="board-target-controls gm-placement-controls">
        <strong>GM：请选择〈{{ gmPlacement.cardName }}〉的登场位置</strong><span>直接点击目标玩家的绿色高亮空位</span>
        <button v-if="mobileLandscapeViewport" class="board-control-minimize" type="button" @click="boardControlMinimized = true">最小化</button>
        <button @click="emit('gmPlacementResolved')">取消</button>
      </div>
      <div v-if="boardTargetPrompt && !readOnly && !boardControlMinimized" class="board-target-controls">
        <strong>{{ boardTargetPrompt.text }}</strong><span>已选择 {{ boardTargetIds.length }}/{{ boardTargetPrompt.maxChoose }}</span>
        <button v-if="mobileLandscapeViewport" class="board-control-minimize" type="button" @click="boardControlMinimized = true">最小化</button>
        <button v-if="boardTargetPrompt.validChoices.includes('skip')" @click="resolveBoardTarget(true)">不发动</button>
        <button class="primary" :disabled="boardTargetIds.length < boardTargetPrompt.minChoose" @click="resolveBoardTarget(false)">{{ boardTargetPrompt.data?.choiceMode === 'mixed-board-payment' ? '确认费用' : '确认发动' }}</button>
      </div>
      <div v-if="boardSlotPrompt && !readOnly && !boardControlMinimized" class="board-target-controls board-slot-controls">
        <CardImage v-if="boardSlotPreview" :card-id="boardSlotPreview.cardId" :legacy-url="boardSlotPreview.imageUrl" :alt="boardSlotPreview.name" intent="board" eager
          @mouseenter="focusCard = boardSlotPreview" @click="focusCard = boardSlotPreview" />
        <strong>{{ boardSlotPrompt.text }}</strong><span>直接点击绿色高亮空位</span>
        <button v-if="mobileLandscapeViewport" class="board-control-minimize" type="button" @click="boardControlMinimized = true">最小化</button>
        <button v-if="boardSlotPrompt.validChoices.includes('skip')"
          @click="command('resolvePrompt', { promptId: boardSlotPrompt.promptId, cardInstanceIds: ['skip'] })">取消</button>
      </div>
      <div v-if="resourceSelectionPrompt && !readOnly && !boardControlMinimized" class="board-target-controls resource-payment-controls">
        <strong>{{ resourceSelectionPrompt.text }}</strong>
        <span>已选择 {{ paymentResourceIds.length }}/{{ resourceSelectionPrompt.maxChoose }}</span>
        <button v-if="mobileLandscapeViewport" class="board-control-minimize" type="button" @click="boardControlMinimized = true">最小化</button>
        <button v-if="resourceSelectionPrompt.validChoices.includes('skip')" @click="confirmResourcePayment(true)">不发动</button>
        <button v-if="resourceSelectionPrompt.validChoices.includes('cancel')" @click="cancelResourcePayment">{{ resourceSelectionPrompt.data?.cancel ?? '取消打出' }}</button>
        <button class="primary" :disabled="paymentResourceIds.length < resourceSelectionPrompt.minChoose"
          @click="confirmResourcePayment(false)">{{ resourceSelectionPrompt.kind === 'resource-return' || resourceSelectionPrompt.data?.choiceMode === 'resource-return'
            ? '确认返还'
            : resourceSelectionPrompt.kind === 'resource-payment' || resourceSelectionPrompt.data?.choiceMode === 'resource-payment'
              ? '确认支付' : '确认选择' }}</button>
      </div>
      <button v-if="mobileLandscapeViewport && boardControlMinimized && (gmPlacement || boardTargetPrompt || boardSlotPrompt || resourceSelectionPrompt)" class="board-control-restore" type="button" @click="boardControlMinimized = false">恢复当前选择</button>
      <PromptOverlay v-if="!readOnly || game.phase === 'DisasterPreparation'" :game="game" :read-only="readOnly" :suppressed-prompt-id="activeBoardPromptId" :suppressed-prompt-ids="activeBoardPromptIds" :suppress-defense-wait="Boolean(combat)" :mulligan-selected-ids="mulliganIds" :busy="l12State.pendingAction" :inspector-visible="modalInspectorVisible" :mobile-layout="mobileLandscapeViewport"
        @focus-card="focusCard = $event" @mulligan-toggle="toggle(mulliganIds, $event)" @mulligan-confirm="command('mulligan')" @minimized-change="promptMinimized = $event" @response-targets-change="responseTargetIds = $event" />
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
.board-center{--l12-hand-lane-height:160px;display:grid;min-height:0;grid-template-rows:var(--l12-hand-lane-height) 70px minmax(0,1fr) 70px var(--l12-hand-lane-height);align-items:stretch;gap:6px}
.board-center>.l12-hand{position:relative;z-index:40;box-sizing:border-box;width:calc(100% - 400px);height:var(--l12-hand-lane-height)!important;min-height:var(--l12-hand-lane-height);padding-right:0;align-self:stretch;justify-self:center;transform:translateX(-10px)}
.board-center>.opponent-hand{grid-row:1}.opponent-status-lane{grid-row:2}.felt-board{grid-row:3}.my-status-lane{grid-row:4}.board-center>.l12-hand:last-child{grid-row:5}
.board-viewport{top:52px}.board-status-lane{height:70px!important;min-height:70px!important;flex-shrink:0}.player-summary :is(.player-summary-primary,.player-summary-meta,.connection-state){font-size:var(--l12-board-copy,13px)!important}
.right-rail{width:auto}.right-rail .record-log{display:flex;flex:1;flex-direction:column;min-height:150px}.right-rail .action-panel{max-height:300px;overflow:auto}.right-rail .action-panel :deep(.l12-actions>p){display:none}.board-rail .card-inspector{overflow:auto}.session-disaster-strip span{white-space:normal!important;overflow-wrap:anywhere}
.card-inspector.archive-detail{display:block;box-sizing:border-box;border-left:1px solid rgba(240,239,229,.2)}.card-inspector :deep(.card-detail-copy){min-width:0}.card-inspector :deep(.archive-tags){flex-wrap:wrap}.card-inspector :deep(.archive-effect p){white-space:pre-wrap;overflow-wrap:anywhere}.battle-card-status{margin-top:2px}.battle-card-status>ul{margin-top:7px}
.felt-board{
  --l12-board-seam-safe-height:44px;
  --l12-battlefield-half-height:350px;
  box-sizing:border-box;
  width:100%;
  min-height:calc(var(--l12-battlefield-half-height) * 2 + var(--l12-board-seam-safe-height) + 10px);
  display:grid;
  grid-template-rows:minmax(var(--l12-battlefield-half-height),1fr) var(--l12-board-seam-safe-height) minmax(var(--l12-battlefield-half-height),1fr);
  justify-self:center;
  align-items:stretch;
}
.board-status-lane{position:relative;z-index:38;display:flex;box-sizing:border-box;height:70px;min-height:70px;justify-content:flex-end;overflow:visible;pointer-events:none}.board-player-clock{position:relative;right:auto;top:auto;bottom:auto}.opponent-status-lane{order:0;align-items:flex-end}.my-status-lane{order:0;align-items:flex-start}
.player-panel{box-sizing:border-box;height:auto!important;min-height:144px;flex:none;overflow:hidden!important}
.player-summary{display:grid;min-width:0;gap:7px}.player-summary-primary{display:grid;min-width:0;grid-template-columns:max-content minmax(0,1fr);align-items:center;column-gap:6px}.player-summary-primary>b{color:#d2525b;font-size:var(--l12-board-copy,13px);white-space:nowrap}.my-summary .player-summary-primary>b{color:#58bdc5}.player-summary-primary>strong{min-width:0;overflow:hidden!important;font-size:max(15px,var(--l12-board-copy,13px))!important;line-height:1.35!important;text-overflow:ellipsis!important;white-space:nowrap!important}.player-summary-meta{display:flex;min-width:0;align-items:center;column-gap:4px;color:#aeb7b5;font-size:var(--l12-board-copy,13px);line-height:1.35}.player-summary-meta>.rank-badge,.player-summary-meta>.title-badge{min-width:max-content;max-width:none;flex:none}.player-summary-meta>.connection-state{min-width:0;max-width:100%!important;justify-content:flex-end;margin-left:auto!important;font-size:clamp(9px,var(--l12-board-micro,9px),11px)!important;overflow:hidden!important;text-overflow:ellipsis!important}
.connection-state{display:flex!important;width:max-content;max-width:none!important;align-items:center;gap:4px;margin:0!important;color:#b76570!important;font-size:var(--l12-board-copy,13px)!important;font-weight:900;line-height:1!important;overflow:visible!important;white-space:nowrap!important;text-overflow:clip!important}.connection-state.online{color:#58c99a!important}.connection-state i{width:6px;height:6px;border-radius:50%;background:currentColor;box-shadow:0 0 6px currentColor}
.player-panel>hr{margin:9px 0!important}
.right-rail .record-log{min-height:120px;overflow:hidden}.right-rail .record-log>.event-list{min-height:0;overflow-y:auto}
.battlefield-half{position:relative;box-sizing:border-box;width:100%;min-height:0;align-self:stretch;justify-self:center}
.battlefield-half::before{content:'';position:absolute;z-index:1;inset:0;box-sizing:border-box;border:1px solid rgba(238,238,228,.18);pointer-events:none}
.battlefield-half.opponent-half::before{border-color:rgba(196,40,50,.34)}
.battlefield-half.my-half::before{inset:0;border-color:rgba(57,171,181,.4)}
.battlefield-half.opponent-half{grid-row:1}
.board-seam{z-index:12;grid-row:2;box-sizing:border-box;height:var(--l12-board-seam-safe-height);min-height:var(--l12-board-seam-safe-height);isolation:isolate;pointer-events:none}
.board-midline-anchor{position:absolute;left:0;right:0;top:50%;height:1px;background:linear-gradient(90deg,transparent,rgba(238,238,228,.35),transparent)}
.battlefield-half.my-half{grid-row:3}
.felt-board :deep(.formation){width:100%;height:350px;grid-template-columns:repeat(3,173px);grid-template-rows:repeat(2,173px);justify-content:end;gap:4px 8px}
.felt-board :deep(.formation-slot .card-tile),.felt-board :deep(.formation-slot .card-tile.tapped){width:114.4px;height:160.6px;flex-basis:114.4px}
.felt-board :deep(.formation-slot .field-actions){bottom:calc(50% + 86.5px)}
.session-disaster-panel{flex:none;padding:9px 10px}.session-disaster-panel h3{margin:0 0 7px}.session-disaster-strip{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:5px}.session-disaster-strip button{min-width:0;padding:2px;border:1px solid #59625f;background:#070a0b;color:#d9ddd8;cursor:pointer}.session-disaster-strip button.hidden{border-color:#343b39;cursor:default}.session-disaster-strip button.inactive img,.session-disaster-strip button.inactive .l12-card-image{filter:grayscale(.85) brightness(.45)}.session-disaster-strip img,.session-disaster-strip .l12-card-image{display:block;width:100%;height:auto;aspect-ratio:8/5}.session-disaster-strip span{display:block;overflow:hidden;padding:2px 2px 1px;font-size:var(--l12-board-copy,13px);font-weight:900;text-overflow:ellipsis;white-space:nowrap}.session-disaster-strip button:not(.hidden):hover{border-color:#73d4c5;box-shadow:0 0 8px rgba(115,212,197,.3)}
.board-mode-hint{position:absolute;z-index:28;left:50%;top:50%;display:flex;align-items:center;gap:10px;padding:8px 9px 8px 16px;border:1px solid #e0b85a;background:rgba(8,10,11,.95);color:#fff3c2;box-shadow:0 7px 22px #000;transform:translate(-50%,-50%);font-size:var(--l12-board-copy,13px);font-weight:900;pointer-events:auto}.board-mode-hint button{min-width:58px;min-height:44px;padding:6px 12px;border:1px solid #747d7b;background:#171b1c;color:#f1eee4;font-size:var(--l12-board-copy,13px);font-weight:900}.board-mode-hint button:hover{border-color:#e0b85a;background:#292419}
.public-reveal-animation{position:fixed;z-index:2147483000;left:50%;top:50%;display:grid;min-width:190px;max-width:min(760px,80vw);justify-items:center;gap:10px;transform:translate(-50%,-50%);pointer-events:none}.public-reveal-cards{display:flex;max-width:100%;align-items:center;justify-content:center;gap:8px;overflow:hidden}.public-reveal-cards .l12-card-image{width:118px;height:165px;filter:drop-shadow(0 10px 15px #000) drop-shadow(0 0 16px rgba(213,188,112,.38))}.public-reveal-cards .l12-card-image.horizontal{width:190px;height:auto;aspect-ratio:8/5}.public-reveal-animation strong{padding:7px 12px;border:1px solid #d5bc70;background:rgba(7,9,10,.9);box-shadow:0 7px 22px #000;color:#fff2c7;font-size:var(--l12-board-copy,13px);font-weight:900;letter-spacing:.04em;text-align:center;white-space:pre-wrap;overflow-wrap:anywhere}.public-reveal-enter-active,.public-reveal-leave-active{transition:opacity .24s ease,filter .24s ease}.public-reveal-enter-from,.public-reveal-leave-to{opacity:0;filter:blur(5px)}
.combat-presentation{position:absolute;z-index:20;left:50%;top:50%;width:760px;height:1px;transform:translate(-50%,-50%);pointer-events:none}.combat-trace{position:absolute;left:50%;top:-108px;width:4px;height:216px;background:linear-gradient(transparent,#d88a39 20%,#f0ba66 50%,#d88a39 80%,transparent);filter:drop-shadow(0 0 7px #c36b26);transform:rotate(-10deg)}.combat-versus{position:absolute;left:50%;top:0;display:flex;width:max-content;max-width:760px;align-items:center;gap:12px;padding:10px 18px;border:1px solid #8e7650;background:rgba(7,9,10,.95);box-shadow:0 8px 26px #000;transform:translate(-50%,-50%);font-weight:900}.combat-versus span{max-width:190px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.combat-versus span.mine{color:#74d0d3}.combat-versus span.opponent{color:#e6757c}.combat-versus>b{display:flex;align-items:baseline;gap:4px;padding:4px 7px;background:#342a25;color:#fff}.combat-versus b small{color:#c8bba3;font-size:var(--l12-board-copy,13px)}.combat-versus em{color:#e5bd60;font-size:max(18px,var(--l12-board-copy,13px));font-style:normal}.combat-resolution-panel{position:absolute;left:50%;top:34px;width:390px;padding:10px 12px;border:1px solid #8e7650;background:rgba(8,11,12,.96);box-shadow:0 12px 30px #000;transform:translateX(-50%);pointer-events:auto}.combat-resolution-panel :deep(.l12-actions){gap:6px}.combat-resolution-panel :deep(.l12-actions p){margin:0;font-size:var(--l12-board-copy,13px)}.combat-resolution-panel :deep(.l12-actions button){padding:7px 9px}
.record-log .event-list p{display:grid;grid-template-columns:auto minmax(0,1fr);align-items:start;gap:5px;margin:0 0 7px}.record-log .event-list p.event-turn-start{display:block;padding:4px 0;text-align:center}.record-log .event-message{min-width:0;white-space:normal;overflow-wrap:anywhere;word-break:break-word}.turn-divider{color:#e0b641;font-size:var(--l12-board-copy,13px);white-space:nowrap}.event-tag{flex:none;padding:2px 4px;border:1px solid #5c4a86;color:#cbaaff;font-size:var(--l12-board-copy,13px);line-height:1.25}.event-play .event-tag,.event-put .event-tag{border-color:#126f82;color:#5fd5e2}.event-attack .event-tag,.event-combat .event-tag{border-color:#8d2942;color:#ff6687}.event-response .event-tag,.event-defense .event-tag,.event-support .event-tag{border-color:#9a501b;color:#f0a45e}.event-disaster .event-tag,.event-disaster-active .event-tag,.event-disaster-value .event-tag{border-color:#9e722b;color:#efc15b}.event-damage .event-tag,.event-leave .event-tag{border-color:#813c40;color:#dd7c81}.event-move .event-tag{border-color:#26757c;color:#65cbd0}
.board-target-controls{position:fixed;z-index:2147483500;left:50%;top:76px;display:flex;align-items:center;gap:10px;max-width:760px;padding:10px 13px;border:1px solid #70d7df;background:#091011;box-shadow:0 14px 36px #000;transform:translateX(-50%)}.board-target-controls strong{max-width:430px;color:#fff;font-size:var(--l12-board-copy,13px)}.board-target-controls span{color:#8f9894;font-size:var(--l12-board-copy,13px)}.board-target-controls button{padding:7px 12px;border:1px solid #999;background:#1b2020;color:#fff;font-weight:900}.board-target-controls button.primary{border-color:#72e09a;background:#174d2d}.board-target-controls button:disabled{opacity:.38}
.board-slot-controls .l12-card-image{width:52px;height:72px;background:#050708;cursor:pointer}.board-slot-controls span{color:#72e09a;font-weight:900}
.inspector-statuses{display:grid;gap:4px;margin:8px 0 0;padding:0;list-style:none}.inspector-statuses li{padding:4px 6px;border-left:2px solid #70d7df;background:rgba(112,215,223,.08);color:#d9ddd7;font-size:var(--l12-board-copy,13px);font-weight:800;line-height:1.45}
.inspector-card-tags{display:flex;box-sizing:border-box;width:max-content;max-width:100%;align-self:center;justify-content:center;flex-wrap:wrap;gap:5px;margin:0 auto 7px}.inspector-card-tags span{flex:0 0 auto;padding:2px 6px;border:1px solid #4f5e5b;background:#111819;color:#8fdad7;font-size:var(--l12-board-copy,13px);font-weight:900;white-space:nowrap}
.left-disaster-row{display:grid;width:100%;grid-template-columns:132px minmax(0,1fr);gap:8px;flex:none}.left-disaster-row>.grand-panel{box-sizing:border-box;width:100%;min-width:0;min-height:178px}.session-disaster-panel{display:grid;align-content:center;justify-items:center;padding:8px!important}.session-disaster-panel h3{width:100%;margin:0 0 8px}.session-disaster-strip{display:grid;width:max-content;grid-template-columns:repeat(2,51.2px);gap:8px}.session-disaster-strip button{width:51.2px;min-width:51.2px;height:51.2px;padding:0;overflow:hidden;border:2px solid #c8b978;border-radius:50%;background:#070a0b}.session-disaster-strip button.hidden,.session-disaster-strip button.unrevealed{border-color:#49504e;filter:grayscale(1) brightness(.58)}.session-disaster-strip button.revealed{border-color:#69716f;filter:grayscale(.85) brightness(.58)}.session-disaster-strip button.resolved{border-color:#76508f;box-shadow:0 0 8px rgba(133,75,174,.28);filter:grayscale(.35) brightness(.64) saturate(.82)}.session-disaster-strip button.active{border-color:#bc6cff;box-shadow:0 0 13px rgba(187,87,255,.72),inset 0 0 0 1px rgba(231,202,255,.42);filter:none}.session-disaster-strip img,.session-disaster-strip .l12-card-image{width:100%;height:100%;border-radius:50%;transform:scale(1.09)}.session-disaster-strip button:not(.hidden):hover{border-color:#d49aff;box-shadow:0 0 11px rgba(190,102,255,.52)}.left-disaster-row>.current-disaster-panel{display:grid;place-items:center;padding:10px!important}
.session-disaster-panel h3{text-align:center}
.card-inspector-anchor{display:flex;flex:1;min-height:0}.card-inspector-anchor>.card-inspector{width:100%}.selected-card-utility-slot{box-sizing:border-box;width:100%;height:60px;flex:none}.card-inspector :deep(.archive-detail-image){width:207px;max-width:100%}.card-inspector :deep(.archive-detail-image.horizontal){width:250px}.card-inspector-floating{position:fixed!important;z-index:1600!important;box-sizing:border-box;overflow:auto!important;transform-origin:left top;pointer-events:none}
.inspector-style-scope{display:contents!important}
.session-disaster-strip button.replaceable{cursor:pointer}.session-disaster-strip button.replaceable:hover{border-color:#e6bd4a;box-shadow:0 0 12px #d49c3d80}
.dice-reveal-animation{position:fixed;z-index:2147483001;left:50%;top:45%;display:grid;justify-items:center;gap:10px;transform:translate(-50%,-50%);pointer-events:none}.dice-reveal-values{display:flex;gap:14px}.dice-reveal-values b{display:grid;width:76px;height:76px;place-items:center;border:3px solid #e3c36d;border-radius:15px;background:#f1eee2;box-shadow:0 12px 30px #000,0 0 22px rgba(227,195,109,.35);color:#111;font-size:max(44px,var(--l12-board-copy,13px));line-height:1;animation:l12-dice-roll .18s infinite alternate}.dice-reveal-animation.settled .dice-reveal-values b{animation:l12-dice-land .32s ease-out}.dice-reveal-animation strong{max-width:min(720px,82vw);padding:7px 12px;border:1px solid #d5bc70;background:rgba(7,9,10,.92);box-shadow:0 7px 22px #000;color:#fff2c7;font-size:var(--l12-board-copy,13px);font-weight:900;text-align:center}.dice-reveal-enter-active,.dice-reveal-leave-active{transition:opacity .2s ease,filter .2s ease}.dice-reveal-enter-from,.dice-reveal-leave-to{opacity:0;filter:blur(5px)}@keyframes l12-dice-roll{from{transform:rotate(-10deg) scale(.94)}to{transform:rotate(10deg) scale(1.06)}}@keyframes l12-dice-land{0%{transform:scale(1.35) rotate(20deg)}100%{transform:scale(1) rotate(0)}}
.public-reveal-animation{z-index:903}.dice-reveal-animation{z-index:904}.board-target-controls{z-index:3000}.card-inspector-floating{z-index:3100!important}
.battle-title{display:flex;flex-wrap:wrap;gap:5px;margin-top:6px}.battle-title b,.battle-title i{padding:3px 6px;border:1px solid #82663a;border-radius:3px;background:#261b0c;color:#f2d27a;font-size:var(--l12-board-copy,13px);font-style:normal;font-weight:900}.battle-title i{border-color:#75509a;background:#1b1028;color:#dfbdff}

/* The selector is deliberately runtime-gated.  A short desktop window is not a phone
   landscape view, so no desktop grid is affected by these rules. */
.mobile-landscape-board{top:0!important;right:0!important;bottom:0!important;left:0!important;padding:4px!important;overflow:hidden}.mobile-landscape-board .board-stage{width:100%!important;height:100%!important;min-width:0!important;min-height:0!important;aspect-ratio:auto!important;transform:none!important;--l12-board-copy:12px!important;--l12-board-meta:10px!important;--l12-board-micro:9px!important}.mobile-landscape-board .stage-layout{height:100%;grid-template-columns:112px 42px minmax(0,1fr) 112px;gap:4px}.mobile-landscape-board .left-rail{min-height:0;overflow:hidden}.mobile-landscape-board .left-detail-layout{display:none}.mobile-landscape-board .left-disaster-row{grid-template-columns:1fr;gap:4px}.mobile-landscape-board .left-disaster-row>.grand-panel{min-height:0}.mobile-landscape-board .session-disaster-panel{padding:4px!important}.mobile-landscape-board .session-disaster-panel h3{font-size:10px}.mobile-landscape-board .session-disaster-strip{grid-template-columns:repeat(4,minmax(0,1fr));gap:3px}.mobile-landscape-board .session-disaster-strip button{width:auto;min-width:0;height:auto;aspect-ratio:1}.mobile-landscape-board .current-disaster-panel{padding:3px!important}.mobile-landscape-board .phase-column{margin-block:0;padding:3px!important;gap:3px}.mobile-landscape-board .phase-disaster-value{min-height:38px;gap:2px;padding:2px}.mobile-landscape-board .phase-disaster-value img{width:18px;height:20px}.mobile-landscape-board .phase-disaster-value b{font-size:20px}.mobile-landscape-board .board-center{--l12-hand-lane-height:50px;grid-template-rows:var(--l12-hand-lane-height) 34px minmax(0,1fr) 34px var(--l12-hand-lane-height);gap:2px}.mobile-landscape-board .board-center>.l12-hand{width:calc(100% - 112px);height:var(--l12-hand-lane-height)!important;min-height:var(--l12-hand-lane-height);transform:none}.mobile-landscape-board .board-status-lane{height:34px!important;min-height:34px!important}.mobile-landscape-board .felt-board{--l12-board-seam-safe-height:18px;--l12-battlefield-half-height:0px;min-height:0}.mobile-landscape-board .battlefield-half{min-width:0;min-height:0;overflow:clip}.mobile-landscape-board .battlefield-half :deep(.l12-player-mat){width:100%;height:100%;min-width:0;min-height:0;grid-template-rows:minmax(0,1fr)}.mobile-landscape-board .battlefield-half :deep(.battle-zone){width:100%;height:100%;min-width:0;min-height:0;align-self:stretch}.mobile-landscape-board .felt-board :deep(.formation){height:100%;grid-template-columns:repeat(3,minmax(0,1fr));grid-template-rows:repeat(2,minmax(0,1fr));justify-content:stretch;gap:2px}.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile),.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile.tapped){width:min(100%,54px);height:auto;max-height:76px;aspect-ratio:5/7;flex-basis:auto}.mobile-landscape-board .right-rail{grid-template-rows:auto minmax(0,1fr) auto;min-height:0;overflow:hidden}.mobile-landscape-board .player-panel{min-height:0;padding:4px!important}.mobile-landscape-board .player-panel>hr,.mobile-landscape-board .right-rail .record-log{display:none}.mobile-landscape-board .player-summary{gap:2px}.mobile-landscape-board .player-summary-primary>strong{font-size:12px!important}.mobile-landscape-board .player-summary-meta{gap:2px;font-size:9px}.mobile-landscape-board .connection-state{font-size:9px!important}.mobile-landscape-board .right-rail .action-panel{max-height:78px;padding:4px!important}.mobile-landscape-board .right-rail .action-panel h3{display:none}.mobile-landscape-board .board-target-controls{top:4px;max-width:calc(100vw - 16px);padding:6px 8px}.mobile-landscape-board .board-target-controls strong{max-width:46vw}.mobile-landscape-board .resource-payment-controls{bottom:4px;top:auto}.mobile-landscape-board .battlefield-half :deep(.resource-zone){transform:scale(.78);transform-origin:center}.mobile-landscape-board .public-reveal-cards .l12-card-image{width:72px;height:101px}.mobile-landscape-board .dice-reveal-values b{width:46px;height:46px;font-size:28px}
.mobile-record-trigger{width:100%;min-height:34px;border:1px solid #587b7d;background:#10191a;color:#bce8e8;font:inherit;font-weight:900}.mobile-context-actions{position:absolute!important;z-index:70;right:4px;bottom:4px;width:176px;min-height:46px;max-height:none!important;overflow:visible!important;background:rgba(8,12,13,.98)!important;box-shadow:0 0 0 1px #587b7d}.mobile-context-actions :deep(.l12-actions>p){display:none!important}.mobile-context-actions :deep(.l12-actions button){width:100%;min-height:42px;padding:8px 10px;font-size:13px}.mobile-landscape-board .card-context-actions{position:fixed!important;z-index:80!important;right:4px!important;bottom:56px!important;left:auto!important;display:flex!important;width:176px!important;max-width:176px!important;max-height:96px!important;flex-wrap:wrap;overflow:auto;background:rgba(8,12,13,.98);box-shadow:0 0 0 1px #587b7d}.mobile-landscape-board .card-context-actions button{min-height:38px;padding:6px 8px;font-size:12px}.mobile-landscape-board .battlefield-half :deep(.resource-zone){width:96px;max-width:96px;gap:3px;transform:none}.mobile-landscape-board .battlefield-half :deep(.resource-faction-action),.mobile-landscape-board .battlefield-half :deep(.resource-morale-summary),.mobile-landscape-board .battlefield-half :deep(.resource-morale-stack){width:96px;max-width:96px}.mobile-landscape-board .battlefield-half :deep(.resource-morale-summary){grid-template-columns:42px 54px;height:28px}.mobile-landscape-board .battlefield-half :deep(.resource-morale-label),.mobile-landscape-board .battlefield-half :deep(.resource-morale-count){width:auto;min-width:0;height:28px;min-height:28px;padding:0 3px;font-size:11px}.mobile-landscape-board .battlefield-half :deep(.resource-morale-stack){display:grid;grid-template-columns:repeat(auto-fit,minmax(18px,1fr));grid-auto-rows:18px;min-height:0;gap:2px;padding:3px}.mobile-landscape-board .battlefield-half :deep(.resource-morale-stack .morale-orb){width:18px;height:18px;min-width:18px;justify-self:center}.mobile-landscape-board .battlefield-half :deep(.resource-morale-stack .morale-orb img){width:12px;height:12px}.mobile-record-overlay{position:fixed;z-index:2147483600;inset:0;display:flex;min-height:0;flex-direction:column;padding:max(12px,env(safe-area-inset-top)) max(12px,env(safe-area-inset-right)) max(12px,env(safe-area-inset-bottom)) max(12px,env(safe-area-inset-left));background:rgba(5,8,9,.985);color:#edf1ec}.mobile-record-overlay header{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:4px 0 10px;border-bottom:1px solid #46504e}.mobile-record-overlay h2{margin:0;font-size:18px}.mobile-record-overlay button{min-width:72px;min-height:36px;border:1px solid #7f8a86;background:#172021;color:#fff;font-weight:900}.mobile-record-overlay :deep(.event-list){min-height:0;flex:1;overflow:auto;padding:12px 2px}.mobile-morale-overlay header small{display:block;margin-top:3px;color:#b8c5c0;font-weight:800}.mobile-morale-prompt{margin:10px 0 6px;color:#e7ece6;font-size:13px;font-weight:800}.mobile-morale-picker{display:grid;min-height:0;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;overflow:auto;padding:4px 0}.mobile-morale-picker>p{grid-column:1/-1;color:#b7c0bb;text-align:center}.mobile-morale-choice{display:flex;min-width:0;min-height:66px;align-items:center;gap:9px;padding:7px 9px;text-align:left}.mobile-morale-choice img{width:35px;height:35px;flex:none;object-fit:contain}.mobile-morale-choice span{display:grid;min-width:0;gap:3px}.mobile-morale-choice small{overflow:hidden;color:#c1cbc5;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.mobile-morale-choice:disabled{opacity:1}.mobile-morale-choice:disabled small{color:#a8b2ad}.mobile-morale-choice.selected{border-color:#f1c75b;background:#554414;box-shadow:0 0 0 2px rgba(241,199,91,.45)}.mobile-morale-choice.god-power{border-color:#60cde8}.mobile-morale-choice.temporary{border-color:#e9e9dc}.mobile-morale-actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px;padding-top:9px;border-top:1px solid #46504e}.mobile-morale-actions .primary{border-color:#e0bd62;background:#544319}

.mobile-landscape-board :deep(.battlefield-half .master-marker-track){left:1px!important;top:84px!important;bottom:auto!important;width:198px!important;height:36px!important;transform:scale(.31)!important;transform-origin:left top}.mobile-landscape-board :deep(.battlefield-half .special-lane.visible){left:2px!important;top:106px!important;bottom:auto!important;width:62px!important;height:22px!important;align-content:center!important;justify-items:start!important}.mobile-landscape-board :deep(.battlefield-half .trial-zone),.mobile-landscape-board :deep(.battlefield-half.side-opponent .trial-zone){flex-direction:row!important;gap:2px}.mobile-landscape-board :deep(.battlefield-half .trial-card),.mobile-landscape-board :deep(.battlefield-half .side-my .trial-card){width:30px!important}.mobile-landscape-board :deep(.battlefield-half .trial-card b){min-width:12px!important;height:12px!important;padding:0 2px!important;border-width:1px!important;font-size:8px!important}
/*
 * Mobile battle board — reviewed layout.
 * The board keeps a dedicated, unclipped field area.  Edge rails are reserved for
 * controls and status so neither a card nor a card action is ever placed over a
 * formation cell.
 */
.mobile-landscape-board {
  padding: 4px !important;
}
.mobile-landscape-board .board-stage {
  --l12-board-copy: 12px !important;
  --l12-board-meta: 10px !important;
  --l12-board-micro: 9px !important;
  position: relative;
  height: calc(100% - 8px) !important;
}
.mobile-landscape-board .stage-layout {
  inset: 0 !important;
  grid-template-columns: 76px minmax(0, 1fr) 112px !important;
  grid-template-rows: minmax(0, 1fr);
  gap: 4px;
}
.mobile-landscape-board .left-rail { grid-column: 1; grid-row: 1; }
.mobile-landscape-board .phase-column { display: none; }
.mobile-landscape-board .left-disaster-row { height: 100%; grid-template-columns: 1fr; }
.mobile-landscape-board .left-disaster-row > .current-disaster-panel { display: none; }
.mobile-landscape-board .session-disaster-panel { padding: 4px !important; }
.mobile-landscape-board .session-disaster-panel h3 { display: none; }
.mobile-landscape-board .session-disaster-strip {
  grid-template-columns: 1fr !important;
  gap: 5px;
}
.mobile-landscape-board .session-disaster-strip button {
  width: 42px !important;
  min-width: 42px !important;
  height: 42px !important;
  justify-self: center;
}
.mobile-landscape-board .board-center {
  grid-column: 2;
  grid-row: 1;
  grid-template-rows: 0 0 minmax(0, 1fr) 0 68px !important;
  gap: 3px;
  overflow: hidden;
}
.mobile-landscape-board .board-center > .opponent-hand,
.mobile-landscape-board .board-center > .board-status-lane { display: none !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child {
  width: 100% !important;
  height: 68px !important;
  min-height: 68px !important;
  padding: 0 3px;
  align-items: center !important;
  justify-content: flex-start !important;
  transform: none !important;
  overflow-x: auto !important;
  overflow-y: hidden !important;
}
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-back) {
  width: 48px !important;
  height: 67px !important;
  min-width: 48px !important;
  flex: 0 0 48px !important;
  margin-left: 0 !important;
}
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-tile) {
  width: 48px !important;
  height: 67px !important;
  min-height: 67px !important;
  flex-basis: 48px !important;
}
.mobile-landscape-board .felt-board {
  --l12-board-seam-safe-height: 18px;
  min-height: 0;
  width: 100% !important;
  max-width: 100% !important;
}
.mobile-landscape-board .battlefield-half { overflow: hidden; }
.mobile-landscape-board :deep(.battlefield-half.l12-player-mat) {
  display: grid !important;
  width: 100% !important;
  height: 100% !important;
  min-width: 0 !important;
  min-height: 0 !important;
  grid-template-columns: 62px minmax(0, 1fr) 38px 86px !important;
  grid-template-rows: minmax(0, 1fr) !important;
  gap: 3px !important;
  align-items: stretch !important;
}
.mobile-landscape-board :deep(.battlefield-half .commander-zone) {
  min-width: 0 !important;
  min-height: 0 !important;
  grid-template-columns: 1fr !important;
  align-self: stretch !important;
}
.mobile-landscape-board :deep(.battlefield-half .mini-master) {
  width: 54px !important;
  height: 76px !important;
}
.mobile-landscape-board :deep(.battlefield-half .master-column) { min-width: 0 !important; }
.mobile-landscape-board :deep(.battlefield-half .battle-zone) {
  min-width: 0 !important;
  min-height: 0 !important;
  height: 100% !important;
  transform: none !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles) {
  width: 38px !important;
  height: 100% !important;
  min-height: 0 !important;
  grid-template-rows: repeat(2, minmax(0, 1fr)) !important;
  gap: 3px !important;
  transform: none !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile) {
  width: 38px !important;
  height: auto !important;
  min-height: 0 !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile-card) {
  width: 34px !important;
  height: 48px !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile span) { display: none; }
.mobile-landscape-board :deep(.battlefield-half .resource-zone),
.mobile-landscape-board :deep(.battlefield-half .resource-faction-action),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack) {
  width: 86px !important;
  max-width: 86px !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-zone) {
  min-height: 0 !important;
  height: 100% !important;
  justify-content: center !important;
  gap: 3px !important;
  overflow: hidden;
}
.mobile-landscape-board :deep(.battlefield-half .resource-faction-action) {
  min-height: 28px !important;
  padding: 2px 4px !important;
  font-size: 10px !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary) {
  grid-template-columns: 36px 50px !important;
  height: 36px !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label) {
  display: grid !important;
  min-width: 36px !important;
  height: 36px !important;
  padding: 0 !important;
  place-items: center;
  border-radius: 50%;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label img) {
  width: 26px;
  height: 26px;
  object-fit: contain;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label span) { display: none; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-count) {
  width: 50px !important;
  max-width: 50px !important;
  height: 36px !important;
  min-height: 36px !important;
  padding: 0 3px !important;
  font-size: 12px !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack) {
  display: grid !important;
  min-height: 0 !important;
  grid-template-columns: repeat(4, 1fr);
  grid-auto-rows: 17px;
  gap: 2px;
  padding: 3px;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack .morale-orb) {
  width: 17px !important;
  height: 17px !important;
  min-width: 17px !important;
  justify-self: center;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack .morale-orb img) {
  width: 11px !important;
  height: 11px !important;
}
.mobile-landscape-board .felt-board :deep(.formation) {
  width: 100% !important;
  height: 100% !important;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  grid-template-rows: repeat(2, minmax(0, 1fr));
  justify-content: stretch;
  gap: 2px 3px;
}
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile),
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile.tapped) {
  width: 52px !important;
  height: 73px !important;
  max-width: calc(100% - 2px) !important;
  max-height: calc(100% - 2px) !important;
  flex-basis: 52px !important;
}
.mobile-landscape-board .right-rail {
  grid-column: 3;
  grid-row: 1;
  grid-template-rows: auto minmax(0, 1fr) 54px;
  min-height: 0;
  overflow: visible;
}
.mobile-landscape-board .right-rail .player-panel {
  position: static !important;
  min-height: 0;
  padding: 4px !important;
}
.mobile-landscape-board .right-rail .player-panel { grid-row: 1; }
.mobile-landscape-board .right-rail .player-summary,
.mobile-landscape-board .right-rail .record-log { display: none; }
.mobile-landscape-board .mobile-record-trigger {
  position: absolute;
  top: 50%;
  right: 5px;
  width: 102px;
  min-height: 36px;
  padding: 4px 5px;
  font-size: 11px;
  transform: translateY(-50%);
}
.mobile-landscape-board .right-rail .action-panel,
.mobile-landscape-board .mobile-context-actions {
  position: relative !important;
  right: auto !important;
  bottom: auto !important;
  width: 100% !important;
  height: 54px !important;
  min-height: 54px !important;
  max-height: none !important;
  padding: 3px !important;
  overflow: visible !important;
}
.mobile-landscape-board .right-rail .action-panel { grid-row: 3; align-self: end; }
.mobile-landscape-board .right-rail .action-panel h3 { display: none; }
.mobile-landscape-board .card-context-actions {
  position: fixed !important;
  z-index: 81 !important;
  right: 6px !important;
  bottom: 62px !important;
  left: auto !important;
  display: flex !important;
  width: 106px !important;
  max-width: 106px !important;
  max-height: calc(100vh - 170px) !important;
  flex-direction: column;
  flex-wrap: nowrap;
  overflow-y: auto;
  background: rgba(8, 12, 13, .98);
  box-shadow: 0 0 0 1px #587b7d, 0 8px 18px rgba(0, 0, 0, .48);
}
.mobile-landscape-board .card-context-actions button {
  min-height: 32px;
  padding: 5px 7px;
  font-size: 11px;
}
/* Field and hand actions are rendered by child components, so this must cross the
   scoped-style boundary.  The right rail is the only allowed mobile action dock. */
.mobile-landscape-board :deep(.card-context-actions) {
  position: fixed !important;
  z-index: 81 !important;
  right: 6px !important;
  bottom: 62px !important;
  left: auto !important;
  display: flex !important;
  width: 106px !important;
  max-width: 106px !important;
  max-height: calc(100vh - 170px) !important;
  flex-direction: column !important;
  flex-wrap: nowrap !important;
  overflow-y: auto !important;
  background: rgba(8, 12, 13, .98) !important;
  box-shadow: 0 0 0 1px #587b7d, 0 8px 18px rgba(0, 0, 0, .48) !important;
  transform: none !important;
}
.mobile-landscape-board :deep(.card-context-actions button) {
  min-height: 32px !important;
  padding: 5px 7px !important;
  color: #fff !important;
  font-size: 11px !important;
  line-height: 1 !important;
  text-align: center !important;
}
/* The modal is intentionally compact: it keeps the current board visible behind it. */
.mobile-record-overlay {
  position: fixed;
  z-index: 2147483600;
  top: 50%;
  left: 50%;
  display: flex;
  box-sizing: border-box;
  width: min(560px, calc(100vw - 150px));
  max-height: calc(100vh - 28px);
  min-height: 0;
  flex-direction: column;
  padding: 10px 12px;
  border: 1px solid #536663;
  background: rgba(7, 11, 12, .98);
  color: #edf1ec;
  box-shadow: 0 0 0 100vmax rgba(0, 0, 0, .36), 0 14px 40px rgba(0, 0, 0, .76);
  transform: translate(-50%, -50%);
}
.mobile-record-overlay header { padding: 0 0 7px; }
.mobile-record-overlay h2 { font-size: 15px; }
.mobile-record-overlay button { min-width: 62px; min-height: 30px; font-size: 11px; }
.mobile-record-overlay :deep(.event-list) { padding: 7px 1px; }
.mobile-morale-overlay {
  width: min(420px, calc(100vw - 220px));
  max-height: min(290px, calc(100vh - 28px));
}
.mobile-morale-prompt { margin: 7px 0 4px; font-size: 11px; }
.mobile-morale-picker {
  grid-template-columns: repeat(auto-fit, minmax(44px, 1fr));
  gap: 7px;
  overflow-x: hidden;
  padding: 5px 1px;
}
.mobile-morale-choice {
  display: grid;
  width: 44px;
  min-height: 44px;
  padding: 2px;
  place-items: center;
  border-radius: 50%;
}
.mobile-morale-choice img { width: 30px; height: 30px; }
.mobile-morale-choice span { display: none; }
.mobile-morale-actions { padding-top: 7px; }

/*
 * Phone battlefield v2.  This is a separate geometry rather than a scaled-down
 * desktop mat: every zone has an allocated rectangle, the six combat cells are
 * square, and no value marker is allowed to cover a card face.
 */
.mobile-landscape-board .stage-layout {
  grid-template-columns: 108px minmax(0, 1fr) 102px !important;
  gap: 4px !important;
}
.mobile-landscape-board .left-rail { overflow: visible !important; }
.mobile-landscape-board .left-disaster-row {
  display: grid !important;
  grid-template-rows: 58px 28px minmax(0, 1fr);
  gap: 4px !important;
}
.mobile-landscape-board .session-disaster-panel { grid-row: 3 !important; min-height: 0 !important; }
.mobile-landscape-board .session-disaster-strip { grid-template-columns: repeat(2, 42px) !important; }
.mobile-landscape-board .left-disaster-row > .current-disaster-panel {
  grid-row: 1 !important;
  display: block !important;
  min-height: 58px !important;
  padding: 2px !important;
}
.mobile-landscape-board .current-disaster-card {
  display: block !important;
  width: 100% !important;
  height: 52px !important;
  padding: 2px !important;
}
.mobile-landscape-board .current-disaster-card > .l12-card-image,
.mobile-landscape-board .current-disaster-card > img {
  width: 100% !important;
  height: 100% !important;
  border-radius: 0;
  object-fit: contain;
}
.mobile-landscape-board .mobile-current-disaster-copy { display: none; }
.mobile-current-disaster-value { display: none; }
.mobile-landscape-board .mobile-current-disaster-value { grid-row: 2; display: grid; box-sizing: border-box; width: 100%; min-height: 28px; grid-template-columns: 16px minmax(0,1fr) auto; align-items: center; gap: 3px; padding: 3px 5px; border: 1px solid rgba(215,204,163,.72); background: rgba(7,10,11,.94); color: #e8e5d8; box-shadow: 0 3px 8px rgba(0,0,0,.62); }
.mobile-landscape-board .mobile-current-disaster-value img { width: 13px; height: 14px; object-fit: contain; filter: invert(1); }
.mobile-landscape-board .mobile-current-disaster-value span { min-width: 0; overflow: hidden; font-size: 9px; font-weight: 800; text-overflow: ellipsis; white-space: nowrap; }
.mobile-landscape-board .mobile-current-disaster-value b { color: #fff; font-size: 12px; font-variant-numeric: tabular-nums; line-height: 1; }
.mobile-current-disaster-copy { display: grid; min-width: 0; align-content: center; gap: 2px; }
.mobile-current-disaster-copy b { color: #c9b478; font-size: 10px; line-height: 1; }
.mobile-current-disaster-copy i { display: -webkit-box; overflow: hidden; color: #f3f1e9; font-size: 11px; font-style: normal; font-weight: 900; line-height: 1.08; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow-wrap: anywhere; }

.mobile-landscape-board :deep(.battlefield-half.l12-player-mat) {
  grid-template-columns: 132px 216px 40px 82px !important;
  justify-content: center !important;
  gap: 4px !important;
  overflow: visible !important;
}
.mobile-landscape-board :deep(.battlefield-half .commander-zone) {
  display: grid !important;
  grid-template-columns: 62px 62px !important;
  grid-template-rows: minmax(0, 1fr) 22px !important;
  gap: 3px 4px !important;
  align-content: center !important;
  justify-content: center !important;
  overflow: hidden !important;
}
.mobile-landscape-board :deep(.battlefield-half .master-column),
.mobile-landscape-board :deep(.battlefield-half .relic-zone) {
  width: 62px !important;
  min-width: 62px !important;
  height: 80px !important;
  min-height: 80px !important;
  align-self: center !important;
}
.mobile-landscape-board :deep(.battlefield-half .mini-master) {
  width: 62px !important;
  height: 80px !important;
  min-width: 62px !important;
  min-height: 80px !important;
}
.mobile-landscape-board :deep(.battlefield-half .mini-master > span:not(.master-away)) {
  right: 2px !important;
  bottom: 2px !important;
  left: 2px !important;
  overflow: hidden !important;
  font-size: 9px !important;
  line-height: 1.05 !important;
  text-overflow: ellipsis !important;
  white-space: nowrap !important;
}
.mobile-landscape-board :deep(.battlefield-half .master-health) {
  right: 2px !important;
  bottom: 17px !important;
  min-width: 0 !important;
  height: 17px !important;
  padding: 0 3px !important;
  font-size: 10px !important;
  line-height: 17px !important;
}
.mobile-landscape-board :deep(.battlefield-half .master-health small) { font-size: 8px !important; }
.mobile-landscape-board :deep(.battlefield-half .relic-zone .card-tile),
.mobile-landscape-board :deep(.battlefield-half .relic-zone .card-tile.tapped) {
  width: 54px !important;
  height: 76px !important;
  max-width: 54px !important;
  max-height: 76px !important;
  flex-basis: 54px !important;
}
.mobile-landscape-board :deep(.battlefield-half .extra-relic) { max-width: 54px !important; max-height: 76px !important; }
.mobile-landscape-board :deep(.battlefield-half .master-marker-track) {
  position: static !important;
  z-index: auto !important;
  grid-column: 1 / -1 !important;
  grid-row: 2 !important;
  display: flex !important;
  width: 100% !important;
  height: 22px !important;
  min-height: 22px !important;
  justify-content: center !important;
  gap: 4px !important;
  transform: none !important;
}
.mobile-landscape-board :deep(.battlefield-half .master-marker-track .rune-orb),
.mobile-landscape-board :deep(.battlefield-half .master-marker-track .canopic-orb) {
  box-sizing: border-box !important;
  width: 19px !important;
  height: 19px !important;
  min-width: 19px !important;
  min-height: 19px !important;
  aspect-ratio: 1 !important;
  border-radius: 50% !important;
  transform: none !important;
}
.mobile-landscape-board :deep(.battlefield-half .master-marker-track img) { width: 15px !important; height: 15px !important; }
.mobile-landscape-board :deep(.battlefield-half .special-lane.visible) {
  position: static !important;
  grid-column: 1 !important;
  grid-row: 1 !important;
  display: grid !important;
  width: 62px !important;
  height: auto !important;
  min-height: 0 !important;
  align-content: center !important;
  justify-items: start !important;
  transform: none !important;
}
.mobile-landscape-board :deep(.battlefield-half .trial-zone),
.mobile-landscape-board :deep(.battlefield-half.side-opponent .trial-zone) { flex-direction: column !important; gap: 3px !important; }
.mobile-landscape-board :deep(.battlefield-half .trial-card) { width: 42px !important; height: 26px !important; }
.mobile-landscape-board :deep(.battlefield-half .trial-card b) { min-width: 14px !important; height: 14px !important; padding: 0 2px !important; font-size: 8px !important; }

.mobile-landscape-board :deep(.battlefield-half .battle-zone) { width: 216px !important; max-width: 216px !important; justify-self: center !important; }
.mobile-landscape-board .felt-board :deep(.formation) {
  width: 214px !important;
  height: 140px !important;
  grid-template-columns: repeat(3, 68px) !important;
  grid-template-rows: repeat(2, 68px) !important;
  justify-content: center !important;
  align-content: center !important;
  gap: 4px !important;
}
.mobile-landscape-board .felt-board :deep(.formation-slot) {
  box-sizing: border-box !important;
  width: 68px !important;
  height: 68px !important;
  min-width: 68px !important;
  min-height: 68px !important;
  aspect-ratio: 1 !important;
  overflow: visible !important;
}
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile),
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile.tapped) {
  width: 46px !important;
  height: 64px !important;
  min-width: 46px !important;
  min-height: 64px !important;
  max-width: none !important;
  max-height: none !important;
  flex-basis: 46px !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles) {
  width: 40px !important;
  min-width: 40px !important;
  grid-template-rows: repeat(2, 52px) !important;
  align-content: center !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile) { width: 40px !important; height: 52px !important; }
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile-card) { width: 38px !important; height: 50px !important; }
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile .pile-count) {
  right: 1px !important;
  top: 1px !important;
  min-width: 17px !important;
  height: 17px !important;
  padding: 0 2px !important;
  font-size: 9px !important;
  line-height: 17px !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-zone),
.mobile-landscape-board :deep(.battlefield-half .resource-faction-action),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack) { width: 82px !important; max-width: 82px !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary) { grid-template-columns: 32px 50px !important; height: 32px !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label) {
  width: 32px !important;
  min-width: 32px !important;
  height: 32px !important;
  min-height: 32px !important;
  aspect-ratio: 1 !important;
  border-radius: 50% !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label img) { width: 22px !important; height: 22px !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-count) { width: 50px !important; height: 32px !important; min-height: 32px !important; font-size: 10px !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack) { grid-template-columns: repeat(4, 17px) !important; grid-auto-rows: 17px !important; justify-content: center !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack .morale-orb) { width: 17px !important; height: 17px !important; min-width: 17px !important; min-height: 17px !important; aspect-ratio: 1 !important; border-radius: 50% !important; }

/* The right rail is a small dock, not an opaque information panel over the mat. */
.mobile-landscape-board .right-rail { grid-template-rows: 34px minmax(0, 1fr) 48px !important; overflow: visible !important; }
.mobile-landscape-board .right-rail .player-panel { display: contents !important; }
.mobile-landscape-board .mobile-record-trigger { position: static !important; grid-column: 1; grid-row: 1; width: 100% !important; min-height: 30px !important; padding: 3px !important; font-size: 10px !important; transform: none !important; }
.mobile-landscape-board .right-rail .action-panel { height: 48px !important; min-height: 48px !important; }
.mobile-landscape-board .card-context-actions,
.mobile-landscape-board :deep(.card-context-actions) { right: 5px !important; bottom: 54px !important; width: 96px !important; max-width: 96px !important; }

.mobile-card-inspector {
  position: fixed;
  /* Card details are a temporary reading layer above whichever interaction
     opened them. Closing the drawer reveals the untouched source dialog. */
  z-index: 2147483640;
  top: 34px;
  bottom: 8px;
  /* The top disaster-rail tab stays visible while the drawer is open. */
  left: 8px;
  display: flex;
  box-sizing: border-box;
  width: min(250px, 34vw);
  min-width: 210px;
  flex-direction: column;
  gap: 6px;
  padding: 9px;
  overflow: hidden;
  border: 1px solid #668a86;
  background: rgba(7, 12, 13, .985);
  box-shadow: 8px 0 26px rgba(0, 0, 0, .62);
  color: #f1f2ec;
}
.mobile-card-inspector header { display: flex; align-items: center; justify-content: space-between; gap: 7px; border-bottom: 1px solid #47605d; padding-bottom: 5px; }
.mobile-card-inspector header small { display: block; color: #aebfbb; font-size: 9px; line-height: 1; }
.mobile-card-inspector header h2 { max-width: 150px; margin: 3px 0 0; overflow: hidden; font-size: 13px; line-height: 1.1; text-overflow: ellipsis; white-space: nowrap; }
.mobile-card-inspector header button { min-width: 42px; min-height: 26px; padding: 2px 5px; border: 1px solid #607a76; background: #172021; color: #fff; font-size: 10px; font-weight: 900; }
.mobile-card-detail-body { display: block !important; min-height: 0; flex: 1; padding: 6px 2px 8px; border-left: 0; overflow-x: hidden; overflow-y: auto; background: transparent; }
.mobile-card-detail-body :deep(.archive-detail-image) { width: min(150px, 100%); margin-bottom: 8px; }
.mobile-card-detail-body :deep(.archive-detail-image.horizontal) { width: 100%; }
.mobile-card-detail-body :deep(.card-detail-copy) { min-width: 0; }
.mobile-card-detail-body :deep(.archive-tags) { flex-wrap: wrap; gap: 3px; }
.mobile-card-detail-body :deep(.archive-tags span) { padding: 2px 4px; font-size: 9px; }
.mobile-card-detail-body :deep(.archive-number),.mobile-card-detail-body :deep(.archive-effect b),.mobile-card-detail-body :deep(dt),.mobile-card-detail-body :deep(dd) { font-size: 10px; }
.mobile-card-detail-body :deep(.card-detail-copy>h2) { margin: 3px 0 6px; font-size: 14px; white-space: normal; overflow-wrap: anywhere; }
.mobile-card-detail-body :deep(.card-detail-copy>dl) { margin: 7px 0; }
.mobile-card-detail-body :deep(.card-detail-copy>dl>*) { padding: 4px; }
.mobile-card-detail-body :deep(.archive-effect) { padding: 7px 0; }
.mobile-card-detail-body :deep(.archive-effect p) { margin-top: 4px; font-size: 11px; line-height: 1.42; white-space: pre-wrap; overflow-wrap: anywhere; }
.mobile-card-inspector .inspector-statuses { max-height: 70px; margin: 0; overflow: auto; }
.mobile-card-inspector .inspector-statuses li { font-size: 10px; }
.mobile-card-inspector-enter-active,.mobile-card-inspector-leave-active { transition: transform .18s ease, opacity .18s ease; }
.mobile-card-inspector-enter-from,.mobile-card-inspector-leave-to { opacity: 0; }

.mobile-morale-choice { box-sizing: border-box !important; width: 44px !important; min-width: 44px !important; max-width: 44px !important; height: 44px !important; min-height: 44px !important; max-height: 44px !important; aspect-ratio: 1 !important; justify-self: center !important; align-self: center !important; flex: 0 0 44px !important; border-radius: 50% !important; }
.mobile-morale-choice img { width: 28px !important; height: 28px !important; }
.mobile-morale-header-actions { display: flex; gap: 4px; }
.mobile-morale-header-actions button { min-width: 46px !important; }

.mobile-landscape-board .right-rail .player-panel {
  display: block !important;
  min-height: 0 !important;
  padding: 0 !important;
  border: 0 !important;
  background: transparent !important;
  box-shadow: none !important;
}
.mobile-landscape-board .right-rail .player-panel > .player-summary,
.mobile-landscape-board .right-rail .player-panel > hr { display: none !important; }
/* Use the otherwise spare disaster rail as a true vertical extra-zone rail: the
   opponent's trial cards enter from above and ours from below. */
.mobile-landscape-board .left-rail { display: flex !important; flex-direction: column !important; gap: 4px !important; }
.mobile-landscape-board .left-disaster-row { height: auto !important; min-height: 0 !important; flex: 1 1 auto !important; }
.mobile-extra-zone { display: grid; width: 100%; max-height: 112px; flex: 0 1 auto; gap: 2px; box-sizing: border-box; padding: 2px; border: 1px solid rgba(115, 141, 136, .54); background: rgba(7, 11, 12, .82); }
.mobile-extra-zone > small { color: #a9bbb6; font-size: 8px; font-weight: 900; line-height: 1; letter-spacing: .04em; text-align: center; }
.mobile-extra-zone > div { display: flex; max-height: 96px; flex-direction: column; align-items: center; gap: 2px; overflow-y: auto; scrollbar-width: thin; }
.mobile-extra-card { position: relative; width: 76px; min-width: 76px; height: 54px; min-height: 54px; padding: 0; overflow: hidden; border: 1px solid #719288; background: #080b0b; }
.mobile-extra-card > .l12-card-image,.mobile-extra-card > img { display: block; width: 100%; height: 100%; object-fit: contain; }
.mobile-extra-card.inactive { filter: grayscale(.85) brightness(.58); }
.mobile-extra-card b { position: absolute; left: 50%; top: 50%; display: grid; width: 15px; height: 15px; place-items: center; border: 1px solid #79c889; border-radius: 50%; background: #102e17; color: #fff; font-size: 8px; transform: translate(-50%, -50%); }
/* Trial cards have been relocated to the disaster rail.  Override the earlier
   `.special-lane.visible` desktop rule with equal specificity so the old
   commander-adjacent copy can never reappear on a phone. */
.mobile-landscape-board :deep(.battlefield-half .special-lane.visible),
.mobile-landscape-board :deep(.battlefield-half .special-lane.visible .trial-zone) { display: none !important; }

/* The hand lane owns its full height.  No fan, tilt or lift may intrude into the field. */
.mobile-landscape-board .board-center { grid-template-rows: 0 0 minmax(0, 1fr) 0 74px !important; }
.mobile-landscape-board .felt-board { position: relative; z-index: 1 !important; overflow: hidden !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child { position: relative; z-index: 42; height: 74px !important; min-height: 74px !important; padding: 2px 4px 0 !important; align-items: flex-end !important; gap: 4px !important; overflow-x: auto !important; overflow-y: hidden !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-back) { width: 50px !important; min-width: 50px !important; height: 70px !important; min-height: 70px !important; flex: 0 0 50px !important; margin-left: 0 !important; transform: none !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap:nth-child(n + 10)),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-back:nth-child(n + 10)) { margin-left: -10px !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap:hover),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap.selected) { transform: translateY(-2px) !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-tile) { width: 50px !important; height: 70px !important; min-height: 70px !important; flex-basis: 50px !important; }

/* A small, edge-hugging data treatment keeps card art clear; full values live in the drawer. */
.mobile-landscape-board .felt-board :deep(.formation-slot .card-cost),
.mobile-landscape-board .felt-board :deep(.formation-slot .card-power),
.mobile-landscape-board .felt-board :deep(.formation-slot .card-disaster) { padding: var(--l12-card-stat-pad-y, 0) var(--l12-card-stat-pad-x, 1px) !important; border-width: 1px !important; font-size: var(--l12-card-stat-font, 7px) !important; line-height: var(--l12-card-stat-line, 8px) !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-cost) { left: var(--l12-card-edge, 1px) !important; top: var(--l12-card-edge, 1px) !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-disaster) { right: var(--l12-card-edge, 1px) !important; top: var(--l12-card-edge, 1px) !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-power) { bottom: var(--l12-card-edge, 1px) !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-status-icons) { left:var(--l12-card-status-edge,1px) !important; top:var(--l12-card-status-top,12px) !important; gap:var(--l12-card-status-gap,1px) !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-status-icon) { width:var(--l12-card-status-size,8px) !important; min-width:var(--l12-card-status-size,8px) !important; height:var(--l12-card-status-size,8px) !important; flex-basis:var(--l12-card-status-size,8px) !important; font-size:var(--l12-card-status-font,6px) !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-keyword-stack),
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile.has-status-effects .card-keyword-stack) { right:var(--l12-card-status-edge,1px) !important; bottom:var(--l12-card-keyword-bottom,12px) !important; left:auto !important; width:auto !important; max-width:calc(100% - var(--l12-card-status-edge,1px) - var(--l12-card-status-edge,1px)) !important; gap:var(--l12-card-keyword-gap,1px) !important; transform:none !important; }
.mobile-landscape-board .felt-board :deep(.formation-slot .card-keyword-row > .card-keyword) { padding:0 var(--l12-card-keyword-pad,1px) !important; font-size:var(--l12-card-keyword-font,6px) !important; }

.mobile-landscape-board :deep(.battlefield-half .commander-zone) { position: relative !important; overflow: visible !important; }
.mobile-landscape-board :deep(.battlefield-half .mobile-hand-count) { position: absolute !important; z-index: 22; left: -44px !important; top: 5px !important; display: grid !important; width: 48px; height: 28px; place-items: center; border: 1px solid rgba(224, 226, 216, .62); background: rgba(6, 10, 11, .88); box-shadow: 0 3px 7px rgba(0,0,0,.5); color: #e8ebe4; line-height: 1; }
.mobile-landscape-board :deep(.battlefield-half .mobile-hand-count i) { color: #9fb2ae; font-size: 8px; font-style: normal; }
.mobile-landscape-board :deep(.battlefield-half .mobile-hand-count b) { color: #fff; font-size: 13px; font-variant-numeric: tabular-nums; }

.mobile-landscape-board .current-disaster-panel { grid-template-columns: minmax(0, 1fr) !important; column-gap: 0; }
.mobile-landscape-board .current-disaster-panel .mobile-card-inspector-handle { position: relative; z-index: 2147483603; grid-column: 1; grid-row: 1; width: 20px; min-width: 20px; min-height: 52px; padding: 3px 2px; border: 1px solid #668a86; background: #111b1c; box-shadow: 2px 0 6px rgba(0,0,0,.45); color: #e8f1ed; font-size: 8px; font-weight: 900; line-height: 1.1; writing-mode: vertical-rl; }
.mobile-landscape-board .current-disaster-panel .mobile-card-inspector-handle.open { border-color: #d0c480; background: #2d2a16; }
.mobile-landscape-board .current-disaster-panel .current-disaster-card { grid-column: 1; }
.mobile-inspector-empty { margin: auto 0; color: #b7c6c0; font-size: 11px; line-height: 1.55; text-align: center; }

/* Final mobile collision guard: the field and the hand own separate stacking
   contexts, so a battlefield child can never intercept a scrolling hand card. */
.mobile-landscape-board .board-center { isolation: isolate; }
.mobile-landscape-board .felt-board { z-index: 0 !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child { grid-row: 5 !important; margin-top: 3px !important; pointer-events: auto !important; isolation: isolate; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-back) { top: 8px !important; bottom: auto !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-cost),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-power),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-disaster) { min-width: 0 !important; padding: var(--l12-card-stat-pad-y, 0) var(--l12-card-stat-pad-x, 1px) !important; border-width: 1px !important; font-size: var(--l12-card-stat-font, 7px) !important; line-height: var(--l12-card-stat-line, 8px) !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-cost) { left: var(--l12-card-edge, 1px) !important; top: var(--l12-card-edge, 1px) !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-disaster) { right: var(--l12-card-edge, 1px) !important; top: var(--l12-card-edge, 1px) !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-power) { right: var(--l12-card-edge, 1px) !important; bottom: var(--l12-card-edge, 1px) !important; }

/* The morale summary and every visible orb lead to the same phone picker. */
.mobile-landscape-board :deep(.resource-morale-stack) { position: relative; }
.mobile-landscape-board :deep(.mobile-morale-stack-trigger) { position: absolute; z-index: 14; inset: 0; display: block; width: 100%; height: 100%; padding: 0; border: 0; background: transparent; cursor: pointer; }

/* The route dock is read top-to-bottom: return/surrender first, then the log. */
.mobile-landscape-board .mobile-record-trigger { position: fixed !important; z-index: 1600; top:calc(var(--l12-viewport-top,0px) + 98px) !important; right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 6px) !important; width: 90px !important; min-height: 28px !important; padding: 3px 2px !important; transform: none !important; }

/* Timed matches retain both clocks in their own right-rail slot.  This is below
   return/surrender and the match log, above the contextual end-turn action. */
.mobile-landscape-board .mobile-timed-clocks { position: fixed; z-index: 1600; top:calc(var(--l12-viewport-top,0px) + 132px); right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 6px); display: grid; width: 90px; gap: 4px; }
.mobile-landscape-board .mobile-timed-clocks :deep(.player-turn-clock) { display: grid; width: 90px; min-height: 0; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 2px; padding: 3px; box-shadow: 0 3px 8px rgba(0,0,0,.62); }
.mobile-landscape-board .mobile-timed-clocks :deep(.player-turn-clock strong) { grid-column: 1 / -1; padding: 2px 1px; font-size: 8px; line-height: 1; }
.mobile-landscape-board .mobile-timed-clocks :deep(.player-turn-clock span) { gap: 0; }
.mobile-landscape-board .mobile-timed-clocks :deep(.player-turn-clock small) { font-size: 7px; line-height: 1; }
.mobile-landscape-board .mobile-timed-clocks :deep(.player-turn-clock b) { font-size: 10px; line-height: 1.1; }

/* The drawer tab is the first item in the entire disaster rail: above opponent
   extras, session disasters and the current-disaster card. */
.mobile-landscape-board .left-rail > .mobile-card-inspector-handle { position: relative; z-index: 2147483603; display: block; width: 100%; min-height: 22px; flex: 0 0 22px; padding: 3px 4px; overflow: hidden; border: 1px solid #668a86; background: #111b1c; box-shadow: 0 2px 6px rgba(0,0,0,.45); color: #e8f1ed; font-size: 9px; font-weight: 900; line-height: 1; text-align: center; text-overflow: ellipsis; white-space: nowrap; }
.mobile-landscape-board .left-rail > .mobile-card-inspector-handle.open { border-color: #d0c480; background: #2d2a16; }
:global(.mobile-card-inspector-handle-global) {
  position:fixed !important;
  z-index:2147483646 !important;
  left:calc(var(--l12-viewport-left,0px) + 4px) !important;
  top:calc(var(--l12-viewport-top,0px) + 8px) !important;
  box-sizing:border-box;
  width:clamp(88px,calc(var(--l12-viewport-height,100vh) * .22),118px);
  height:clamp(24px,calc(var(--l12-viewport-height,100vh) * .07),30px);
  min-height:0;
  padding:3px 5px;
  overflow:hidden;
  border:1px solid #668a86;
  background:#111b1c;
  color:#e8f1ed;
  font-size:clamp(8px,calc(var(--l12-viewport-height,100vh) * .023),10px);
  font-weight:900;
  line-height:1;
  text-overflow:ellipsis;
  white-space:nowrap;
  box-shadow:0 3px 9px #000;
}
:global(.mobile-card-inspector-handle-global.open) { border-color:#d0c480; background:#2d2a16; }
:global(.mobile-safe-overlay:not(.mobile-card-inspector)) { padding-top:38px !important; }
/* Disaster circles are visual content of this rail, never floating decoration.
   The panel clips active glows and the strip owns the complete two-column grid. */
.mobile-landscape-board .session-disaster-panel { box-sizing: border-box !important; overflow: hidden !important; }
.mobile-landscape-board .session-disaster-strip { box-sizing: border-box !important; width: 100% !important; max-width: 100% !important; justify-content: center !important; overflow: hidden !important; }
.mobile-landscape-board .session-disaster-strip button { box-sizing: border-box !important; max-width: 42px !important; max-height: 42px !important; overflow: hidden !important; }

/* The disaster-rail tab is only a handle.  The actual card drawer is an
   independent, opaque reading surface, so it never inherits the rail width. */
.mobile-card-inspector {
  width: min(clamp(280px, 38vw, 340px), calc(var(--l12-viewport-width, 100vw) - 16px));
  max-width: calc(var(--l12-viewport-width, 100vw) - 16px);
  background: #070c0d;
  opacity: 1 !important;
  backdrop-filter: none;
}

/* On phones the resource summary must say what it opens.  The faction action
   remains a separate control above it; this restores the explicit “士气” label. */
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary) {
  grid-template-columns: 38px 44px !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label) {
  width: 38px !important;
  min-width: 38px !important;
  height: 32px !important;
  min-height: 32px !important;
  padding: 0 !important;
  border-radius: 0 !important;
  aspect-ratio: auto !important;
  font-size: 10px !important;
  letter-spacing: 0 !important;
}
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label img) { display: none !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label span) { display: block !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-count) {
  width: 44px !important;
  max-width: 44px !important;
  padding: 0 !important;
  font-size: 10px !important;
}
.mobile-morale-choice.black-lotus { border-color: #d4ae42; background: #0c0d0d; }
.mobile-morale-choice.temporary img { filter: brightness(0) invert(1); }
:global(.mobile-action-dock) {
  position: fixed !important;
  z-index: 2147483604 !important;
  right: calc(100vw - var(--l12-viewport-left, 0px) - var(--l12-viewport-width, 100vw) + 5px) !important;
  bottom: calc(100vh - var(--l12-viewport-top, 0px) - var(--l12-viewport-height, 100vh) + 64px) !important;
  left: auto !important;
  display: flex !important;
  box-sizing: border-box;
  width: 96px !important;
  max-width: 96px !important;
  max-height: calc(var(--l12-viewport-height, 100vh) - 72px);
  flex-wrap: wrap;
  gap: 3px;
  padding: 3px;
  overflow: auto;
  border: 1px solid #587b7d;
  background: rgba(8, 12, 13, .98);
  box-shadow: 0 5px 18px #000;
  transform: none !important;
  pointer-events: auto !important;
}
:global(.mobile-action-dock button) {
  box-sizing: border-box;
  min-width: 0 !important;
  min-height: 32px !important;
  flex: 1 1 42px;
  padding: 3px 4px !important;
  font-size: 10px !important;
  line-height: 1.1 !important;
  white-space: normal;
}
.mobile-record-overlay.mobile-safe-overlay { top: calc(var(--l12-viewport-top, 0px) + (var(--l12-viewport-height, 100vh) / 2)); left: calc(var(--l12-viewport-left, 0px) + (var(--l12-viewport-width, 100vw) / 2)); width: min(560px, calc(var(--l12-viewport-width, 100vw) - 16px)); max-height: calc(var(--l12-viewport-height, 100vh) - 16px); }
.mobile-morale-overlay.mobile-safe-overlay { width: min(420px, calc(var(--l12-viewport-width, 100vw) - 16px)); max-height: calc(var(--l12-viewport-height, 100vh) - 16px); }
.mobile-card-inspector.mobile-safe-overlay {
  position:fixed !important;
  top:calc(var(--l12-viewport-top,0px) + 8px) !important;
  right:auto !important;
  bottom:auto !important;
  left:calc(var(--l12-viewport-left,0px) + clamp(88px,calc(var(--l12-viewport-height,100vh) * .22),118px) + 12px) !important;
  box-sizing:border-box;
  width:min(clamp(280px,calc(var(--l12-viewport-width,100vw) * .38),340px),calc(var(--l12-viewport-width,100vw) - clamp(88px,calc(var(--l12-viewport-height,100vh) * .22),118px) - 20px)) !important;
  min-width:0;
  max-width:calc(var(--l12-viewport-width,100vw) - clamp(88px,calc(var(--l12-viewport-height,100vh) * .22),118px) - 20px) !important;
  height:calc(var(--l12-viewport-height,100vh) - 16px) !important;
  max-height:calc(var(--l12-viewport-height,100vh) - 16px) !important;
  transform:none !important;
}
.mobile-card-inspector.mobile-safe-overlay>header>div { min-width:0; flex:1; }
.mobile-card-inspector.mobile-safe-overlay>header h2 {
  max-width:none;
  overflow:visible;
  display:-webkit-box;
  -webkit-box-orient:vertical;
  -webkit-line-clamp:2;
  line-clamp:2;
  text-overflow:clip;
  white-space:normal;
  overflow-wrap:anywhere;
}
/* Card actions stay in the end-turn column, directly above that control. They
   only out-rank the right rail on phone landscape; desktop stacking is untouched. */
.mobile-landscape-board :deep(.card-context-actions) { position: fixed !important; z-index: 2147483500 !important; right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 5px) !important; bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + 64px) !important; left: auto !important; width: 96px !important; max-width: 96px !important; pointer-events: auto !important; transform: none !important; }
.mobile-landscape-board .resource-payment-controls { top:auto !important; bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + 4px) !important; max-width:calc(var(--l12-viewport-width,100vw) - 16px) !important; }
/* The rail itself is only a layout reservation.  Let a card-action dock behind
   its empty surface receive the tap, while restoring normal hit testing to the
   rail's real controls. */
.mobile-landscape-board .right-rail { pointer-events: none !important; }
.mobile-landscape-board .right-rail button,.mobile-landscape-board .right-rail a,.mobile-landscape-board .right-rail input,.mobile-landscape-board .right-rail select,.mobile-landscape-board .right-rail textarea { pointer-events: auto !important; }
/* Defense decisions are player choices, not explanatory panels, on phone
   landscape. Keep both outcomes in one compact, reachable horizontal bar. */
.mobile-landscape-board .right-rail .action-panel :deep(.l12-actions) { display: grid !important; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 4px; align-items:stretch; }
.mobile-landscape-board .right-rail .action-panel :deep(.l12-actions>p) { display:none !important; }
.mobile-landscape-board .right-rail .action-panel :deep(.l12-actions button) { min-width:0 !important; min-height:38px !important; height:38px !important; padding:3px 4px !important; font-size:11px !important; line-height:1 !important; white-space:nowrap; }
/* Main phase has one persistent action. Let it occupy the complete dock width
   so “结束回合” remains one readable four-character label on narrow phones. */
.mobile-landscape-board .right-rail .action-panel :deep(.l12-actions button:last-child) { grid-column:1 / -1; }
/* DefenseChoice is rendered inside the combat presentation, not the rail. */
.mobile-landscape-board .combat-resolution-panel { box-sizing:border-box !important; width:min(210px,calc(var(--l12-viewport-width,100vw) - 200px)) !important; max-width:calc(var(--l12-viewport-width,100vw) - 200px) !important; height:42px !important; padding:2px !important; overflow:hidden !important; }
.mobile-landscape-board .combat-resolution-panel :deep(.l12-actions) { display:grid !important; box-sizing:border-box; grid-template-columns:repeat(2,minmax(0,1fr)) !important; height:38px !important; padding-right:30px; gap:4px !important; }
.mobile-landscape-board .combat-resolution-panel :deep(.l12-actions>p) { display:none !important; }
.mobile-landscape-board .combat-resolution-panel :deep(.l12-actions button) { min-width:0 !important; min-height:38px !important; height:38px !important; padding:3px 4px !important; font-size:11px !important; line-height:1 !important; white-space:nowrap !important; }
.mobile-landscape-board .combat-decision-minimize { position:absolute; top:4px; right:3px; z-index:2; width:26px; min-width:26px; height:32px; padding:0; border:1px solid #7a807d; background:#151a1b; color:#fff; font-size:18px; line-height:1; }
.mobile-landscape-board .combat-decision-restore { position:fixed; z-index:2147483610; right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 110px); bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + var(--l12-mobile-hand-h,64px) + 5px); min-height:32px; padding:5px 10px; border:1px solid #d7ad62; background:#4b331d; color:#fff; font-size:11px; font-weight:900; }
/* Defense decisions stay in a compact top bar so the defender can still tap
   hand and battlefield cards. The desktop combat presentation is untouched. */
.mobile-landscape-board .combat-presentation {
  position: fixed !important;
  z-index: 2147483400 !important;
  top: calc(var(--l12-viewport-top, 0px) + 4px) !important;
  left: calc(var(--l12-viewport-left, 0px) + (var(--l12-viewport-width, 100vw) / 2)) !important;
  width: min(420px, calc(var(--l12-viewport-width, 100vw) - 196px)) !important;
  height: auto !important;
  transform: translateX(-50%) !important;
}
.mobile-landscape-board .combat-trace { display: none !important; }
.mobile-landscape-board .combat-versus {
  top: 0 !important;
  box-sizing: border-box;
  max-width: 100% !important;
  gap: 5px !important;
  padding: 4px 7px !important;
  font-size: 10px !important;
  transform: translateX(-50%) !important;
}
.mobile-landscape-board .combat-stage-label { flex:0 0 auto; color:#e7ca73; font-size:9px; font-weight:900; letter-spacing:.08em; white-space:nowrap; }
.mobile-landscape-board .combat-versus span { max-width: 92px !important; }
.mobile-landscape-board .combat-versus > b { gap: 2px !important; padding: 2px 4px !important; }
.mobile-landscape-board .combat-versus em { font-size: 12px !important; }
.mobile-landscape-board .combat-resolution-panel {
  top: 34px !important;
  box-sizing: border-box;
  width: 100% !important;
  padding: 4px !important;
}
.mobile-landscape-board .combat-resolution-panel :deep(.l12-actions) { grid-template-columns: repeat(2,minmax(0,1fr)); padding-right:30px; gap: 4px !important; }
.mobile-landscape-board .combat-resolution-panel :deep(.l12-actions button) { min-height: 32px; padding: 5px 7px !important; font-size: 11px !important; }
/* Non-interactive combat stages reserve the commander-side hand counter lane.
   DefenseChoice keeps the actionable top bar. */
.mobile-landscape-board .combat-presentation--passive {
  top:calc(var(--l12-viewport-top,0px) + 36px) !important;
  left:calc(var(--l12-viewport-left,0px) + (var(--l12-viewport-width,100vw) / 2) + var(--l12-mobile-hand-count-w)) !important;
  width:min(360px,calc(var(--l12-viewport-width,100vw) - 260px)) !important;
  pointer-events:none !important;
}
.mobile-landscape-board .combat-presentation--passive .combat-versus {
  width:100% !important;
  justify-content:center;
  gap:4px !important;
  padding:2px 5px !important;
  min-height:20px;
  box-shadow:0 3px 10px #000b;
}
.mobile-landscape-board .combat-presentation--passive .combat-versus span { max-width:70px !important; }
.mobile-landscape-board .combat-presentation--passive .combat-versus > b { padding:1px 3px !important; }
.mobile-landscape-board .combat-presentation--passive .combat-versus b small { font-size:8px !important; }
/* B3 mobile card system. One logical-viewport scale owns every card-bearing
   zone. It deliberately replaces the former physical-height phone/tablet
   branches so a rotated portrait and a physical landscape of the same logical
   size resolve to identical geometry. */
.mobile-landscape-board {
  top:var(--l12-viewport-top,0px) !important;
  right:auto !important;
  bottom:auto !important;
  left:var(--l12-viewport-left,0px) !important;
  box-sizing:border-box;
  width:var(--l12-viewport-width,100vw) !important;
  height:var(--l12-viewport-height,100vh) !important;
  /* The card scale is solved from both axes. The width reserve contains the
     two outer rails plus hand-count/resource lanes; the height term divides
     the remaining felt between two players and two square formation rows.
     This keeps every card-bearing zone on one scale instead of capping tall
     screens at the old 90px tablet value. */
  --l12-mobile-left-rail-w:clamp(88px,calc(var(--l12-viewport-height,100vh) * .22),118px);
  --l12-mobile-right-rail-w:clamp(90px,calc(var(--l12-viewport-height,100vh) * .22),118px);
  --l12-mobile-outer-reserve:calc(var(--l12-mobile-left-rail-w) + var(--l12-mobile-right-rail-w) + clamp(132px,calc(var(--l12-viewport-height,100vh) * .195),150px));
  /* The width equation also reserves the three inter-group gaps and the
     player's inner frame. Without this fixed reserve, mid-size 4:3 canvases
     centered an over-wide group and leaked a few pixels through both sides. */
  --l12-mobile-card-h:clamp(46px,min(calc((var(--l12-viewport-width,100vw) - var(--l12-mobile-outer-reserve) - 16px) / 5.15),calc((var(--l12-viewport-height,100vh) - var(--l12-mobile-hand-h) - 60px) / 4 - 2px)),166px);
  --l12-mobile-card-w:calc(var(--l12-mobile-card-h) * 5 / 7);
  --l12-mobile-slot:calc(var(--l12-mobile-card-h) + 2px);
  --l12-mobile-formation-w:calc(var(--l12-mobile-slot) * 3 + 4px);
  --l12-mobile-commander-w:calc(var(--l12-mobile-card-w) * 2 + 4px);
  --l12-mobile-resource-w:clamp(74px,calc(var(--l12-viewport-width,100vw) * .09),92px);
  --l12-mobile-marker:min(19px,calc((var(--l12-mobile-commander-w) - 12px) / 5));
  --l12-mobile-marker-gap:clamp(1px,calc(var(--l12-viewport-height,100vh) * .004),4px);
  --l12-mobile-hand-count-w:clamp(36px,calc(var(--l12-mobile-card-w) * .72),48px);
  --l12-mobile-hand-count-h:clamp(14px,calc(var(--l12-viewport-height,100vh) * .033),24px);
  --l12-mobile-hand-h:clamp(64px,calc(var(--l12-viewport-height,100vh) * .16),102px);
  --l12-mobile-hand-card-h:calc(var(--l12-mobile-hand-h) - 6px);
  --l12-mobile-morale-orb:clamp(12px,calc(var(--l12-viewport-height,100vh) * .022),17px);
  --l12-mobile-hand-card-w:calc(var(--l12-mobile-hand-card-h) * 5 / 7);
  --l12-mobile-player-w:calc(var(--l12-viewport-width,100vw) - var(--l12-mobile-left-rail-w) - var(--l12-mobile-right-rail-w) - 38px);
  --l12-mobile-player-track-w:calc(var(--l12-mobile-commander-w) + var(--l12-mobile-formation-w) + var(--l12-mobile-card-w) + var(--l12-mobile-resource-w));
  --l12-mobile-group-gap:clamp(2px,calc((var(--l12-mobile-player-w) - var(--l12-mobile-player-track-w) - 4px) / 3),calc(var(--l12-mobile-card-w) * 1.5));
  --l12-mobile-rail-gap:clamp(3px,calc(var(--l12-viewport-height,100vh) * .012),5px);
  --l12-mobile-detail-handle-h:clamp(24px,calc(var(--l12-viewport-height,100vh) * .07),30px);
  --l12-mobile-current-disaster-h:clamp(48px,calc(var(--l12-viewport-height,100vh) * .14),66px);
  --l12-mobile-disaster-value-h:clamp(24px,calc(var(--l12-viewport-height,100vh) * .07),30px);
  --l12-mobile-disaster-orb:clamp(28px,calc(var(--l12-viewport-height,100vh) * .09),42px);
  --l12-mobile-extra-zone-h:clamp(48px,calc(var(--l12-viewport-height,100vh) * .18),90px);
  --l12-mobile-master-name-font:clamp(7px,calc(var(--l12-mobile-card-w) * .18),10px);
  --l12-mobile-master-health-h:clamp(13px,calc(var(--l12-mobile-card-w) * .34),22px);
  --l12-mobile-master-health-font:clamp(8px,calc(var(--l12-mobile-card-w) * .20),13px);
  --l12-mobile-master-health-small:clamp(6px,calc(var(--l12-mobile-card-w) * .15),10px);
  --l12-mobile-pile-badge-h:clamp(12px,calc(var(--l12-mobile-card-w) * .30),20px);
  --l12-mobile-pile-count-font:clamp(7px,calc(var(--l12-mobile-card-w) * .18),11px);
  --l12-mobile-pile-label-font:clamp(6px,calc(var(--l12-mobile-card-w) * .15),9px);
  --l12-mobile-resource-summary-h:clamp(22px,calc(var(--l12-mobile-card-w) * .52),30px);
  --l12-mobile-resource-font:clamp(7px,calc(var(--l12-mobile-card-w) * .17),10px);
  --l12-mobile-action-w:clamp(84px,calc(var(--l12-mobile-right-rail-w) - 6px),104px);
  --l12-mobile-action-h:clamp(30px,calc(var(--l12-viewport-height,100vh) * .09),38px);
  --l12-mobile-action-font:clamp(9px,calc(var(--l12-viewport-height,100vh) * .025),11px);
}
.mobile-landscape-board .stage-layout { grid-template-columns:var(--l12-mobile-left-rail-w) minmax(0,1fr) var(--l12-mobile-right-rail-w) !important; }
.mobile-landscape-board :deep(.card-tile) { --l12-card-status-edge:clamp(1px,2.2cqw,3px);--l12-card-status-top:clamp(12px,35.7cqw,50px);--l12-card-status-size:clamp(8px,13.9cqw,19.5px);--l12-card-status-gap:clamp(1px,1.5cqw,3px);--l12-card-status-font:clamp(6px,8.5cqw,12px);--l12-card-keyword-bottom:clamp(12px,28.5cqw,40px);--l12-card-keyword-gap:clamp(1px,2.8cqw,4px);--l12-card-keyword-pad:clamp(1px,2.8cqw,4px);--l12-card-keyword-font:clamp(6px,7.2cqw,11.25px);--l12-card-attached-size:clamp(12px,15.7cqw,22px);--l12-card-attached-bottom:clamp(15px,19.3cqw,27px); }
.mobile-landscape-board :deep(.card-tile.compact) { --l12-card-status-top:clamp(11px,34cqw,34px);--l12-card-status-size:clamp(8px,17cqw,17px);--l12-card-keyword-bottom:clamp(11px,34cqw,34px);--l12-card-keyword-gap:clamp(1px,2cqw,2px);--l12-card-keyword-pad:clamp(1px,2cqw,2px);--l12-card-keyword-font:clamp(6px,9cqw,9px); }
.mobile-landscape-board .left-rail,.mobile-landscape-board .left-disaster-row,.mobile-landscape-board .left-disaster-row>.grand-panel { box-sizing:border-box; width:var(--l12-mobile-left-rail-w) !important; min-width:var(--l12-mobile-left-rail-w) !important; }
.mobile-landscape-board :deep(.battlefield-half.l12-player-mat) {
  box-sizing:border-box !important;
  grid-template-columns:var(--l12-mobile-commander-w) var(--l12-mobile-formation-w) var(--l12-mobile-card-w) var(--l12-mobile-resource-w) !important;
  gap:var(--l12-mobile-group-gap) !important;
  justify-content:center !important;
  padding-inline:2px !important;
}
.mobile-landscape-board :deep(.battlefield-half .commander-zone) {
  width:var(--l12-mobile-commander-w) !important;
  min-width:var(--l12-mobile-commander-w) !important;
  grid-template-columns:repeat(2,var(--l12-mobile-card-w)) !important;
  grid-template-rows:minmax(0,1fr) calc(var(--l12-mobile-marker) + 2px) !important;
  gap:2px 4px !important;
}
.mobile-landscape-board :deep(.battlefield-half .mobile-hand-count) {
  position:absolute !important;
  z-index:22;
  left:4px !important;
  top:1px !important;
  display:flex !important;
  box-sizing:border-box;
  width:var(--l12-mobile-hand-count-w) !important;
  min-width:var(--l12-mobile-hand-count-w) !important;
  height:var(--l12-mobile-hand-count-h) !important;
  align-items:center;
  justify-content:center;
  gap:1px;
  padding:0 2px;
  overflow:hidden;
  border:1px solid rgba(224,226,216,.62);
  background:rgba(6,10,11,.92);
  box-shadow:0 2px 5px rgba(0,0,0,.45);
  color:#e8ebe4;
  line-height:1;
  white-space:nowrap;
}
.mobile-landscape-board :deep(.battlefield-half .mobile-hand-count i) { color:#aebdb9; font-size:clamp(7px,calc(var(--l12-viewport-height,100vh) * .0135),9px); font-style:normal; }
.mobile-landscape-board :deep(.battlefield-half .mobile-hand-count b) { color:#fff; font-size:clamp(8px,calc(var(--l12-viewport-height,100vh) * .0155),11px); font-variant-numeric:tabular-nums; }
.mobile-landscape-board :deep(.battlefield-half .master-column),
.mobile-landscape-board :deep(.battlefield-half .relic-zone),
.mobile-landscape-board :deep(.battlefield-half .mini-master),
.mobile-landscape-board :deep(.battlefield-half .relic-zone .card-tile),
.mobile-landscape-board :deep(.battlefield-half .relic-zone .card-tile.tapped) {
  box-sizing:border-box !important;
  width:var(--l12-mobile-card-w) !important;
  min-width:var(--l12-mobile-card-w) !important;
  max-width:var(--l12-mobile-card-w) !important;
  height:var(--l12-mobile-card-h) !important;
  min-height:var(--l12-mobile-card-h) !important;
  max-height:var(--l12-mobile-card-h) !important;
  flex-basis:var(--l12-mobile-card-w) !important;
  aspect-ratio:5/7 !important;
}
.mobile-landscape-board :deep(.battlefield-half .extra-relic) { width:var(--l12-mobile-card-w) !important; height:var(--l12-mobile-card-h) !important; max-width:var(--l12-mobile-card-w) !important; max-height:var(--l12-mobile-card-h) !important; }
.mobile-landscape-board :deep(.battlefield-half .mini-master>span:not(.master-away)) { right:var(--l12-card-edge,1px) !important; bottom:var(--l12-card-edge,1px) !important; left:var(--l12-card-edge,1px) !important; font-size:var(--l12-mobile-master-name-font) !important; }
.mobile-landscape-board :deep(.battlefield-half .master-health) { right:var(--l12-card-edge,1px) !important; bottom:var(--l12-mobile-master-health-h) !important; min-width:0 !important; height:var(--l12-mobile-master-health-h) !important; padding:0 var(--l12-card-stat-pad-x,1px) !important; font-size:var(--l12-mobile-master-health-font) !important; line-height:var(--l12-mobile-master-health-h) !important; }
.mobile-landscape-board :deep(.battlefield-half .master-health small) { font-size:var(--l12-mobile-master-health-small) !important; }
.mobile-landscape-board :deep(.battlefield-half .battle-zone) { width:var(--l12-mobile-formation-w) !important; min-width:var(--l12-mobile-formation-w) !important; max-width:var(--l12-mobile-formation-w) !important; justify-self:center !important; }
.mobile-landscape-board .felt-board :deep(.formation) {
  box-sizing:border-box !important;
  width:var(--l12-mobile-formation-w) !important;
  height:calc(var(--l12-mobile-slot) * 2 + 2px) !important;
  grid-template-columns:repeat(3,var(--l12-mobile-slot)) !important;
  grid-template-rows:repeat(2,var(--l12-mobile-slot)) !important;
  gap:2px !important;
  align-content:center !important;
  justify-content:center !important;
}
.mobile-landscape-board .felt-board :deep(.formation-slot) {
  box-sizing:border-box !important;
  width:var(--l12-mobile-slot) !important;
  min-width:var(--l12-mobile-slot) !important;
  height:var(--l12-mobile-slot) !important;
  min-height:var(--l12-mobile-slot) !important;
  aspect-ratio:1 !important;
}
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile),
.mobile-landscape-board .felt-board :deep(.formation-slot .card-tile.tapped) {
  width:var(--l12-mobile-card-w) !important;
  min-width:var(--l12-mobile-card-w) !important;
  max-width:var(--l12-mobile-card-w) !important;
  height:var(--l12-mobile-card-h) !important;
  min-height:var(--l12-mobile-card-h) !important;
  max-height:var(--l12-mobile-card-h) !important;
  flex-basis:var(--l12-mobile-card-w) !important;
  aspect-ratio:5/7 !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles) {
  width:var(--l12-mobile-card-w) !important;
  min-width:var(--l12-mobile-card-w) !important;
  height:calc(var(--l12-mobile-card-h) * 2 + 2px) !important;
  grid-template-rows:repeat(2,var(--l12-mobile-card-h)) !important;
  gap:2px !important;
  align-content:center !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile),
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile.deck),
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile-card) {
  box-sizing:border-box !important;
  width:var(--l12-mobile-card-w) !important;
  min-width:var(--l12-mobile-card-w) !important;
  height:var(--l12-mobile-card-h) !important;
  min-height:var(--l12-mobile-card-h) !important;
  aspect-ratio:5/7 !important;
}
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile .pile-count) { right:var(--l12-card-edge,1px) !important; top:var(--l12-card-edge,1px) !important; min-width:var(--l12-mobile-pile-badge-h) !important; height:var(--l12-mobile-pile-badge-h) !important; padding:0 var(--l12-card-stat-pad-x,1px) !important; font-size:var(--l12-mobile-pile-count-font) !important; line-height:var(--l12-mobile-pile-badge-h) !important; }
.mobile-landscape-board :deep(.battlefield-half .mat-piles .pile>span) { left:var(--l12-card-edge,1px) !important; bottom:var(--l12-card-edge,1px) !important; padding:var(--l12-card-stat-pad-y,0) var(--l12-card-stat-pad-x,1px) !important; font-size:var(--l12-mobile-pile-label-font) !important; }
.mobile-landscape-board :deep(.battlefield-half .master-marker-track) { box-sizing:border-box !important; width:100% !important; height:calc(var(--l12-mobile-marker) + 2px) !important; min-height:calc(var(--l12-mobile-marker) + 2px) !important; gap:var(--l12-mobile-marker-gap) !important; padding-inline:2px !important; overflow:visible !important; }
.mobile-landscape-board :deep(.battlefield-half .master-marker-track .rune-orb),
.mobile-landscape-board :deep(.battlefield-half .master-marker-track .canopic-orb) { width:var(--l12-mobile-marker) !important; min-width:var(--l12-mobile-marker) !important; height:var(--l12-mobile-marker) !important; min-height:var(--l12-mobile-marker) !important; flex:0 0 var(--l12-mobile-marker) !important; }
.mobile-landscape-board :deep(.battlefield-half .master-marker-track img) { width:calc(var(--l12-mobile-marker) - 4px) !important; height:calc(var(--l12-mobile-marker) - 4px) !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-zone),
.mobile-landscape-board :deep(.battlefield-half .resource-faction-action),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack) { box-sizing:border-box !important; width:var(--l12-mobile-resource-w) !important; max-width:var(--l12-mobile-resource-w) !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-summary) { grid-template-columns:minmax(24px,42%) minmax(34px,58%) !important; height:var(--l12-mobile-resource-summary-h) !important; min-height:var(--l12-mobile-resource-summary-h) !important; overflow:hidden !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label),
.mobile-landscape-board :deep(.battlefield-half .resource-morale-count) { width:auto !important; min-width:0 !important; max-width:none !important; height:var(--l12-mobile-resource-summary-h) !important; min-height:var(--l12-mobile-resource-summary-h) !important; padding:0 var(--l12-card-stat-pad-x,1px) !important; overflow:hidden !important; font-size:var(--l12-mobile-resource-font) !important; white-space:nowrap !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-label img) { width:calc(var(--l12-mobile-resource-summary-h) - 8px) !important; height:calc(var(--l12-mobile-resource-summary-h) - 8px) !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-faction-action) { min-height:calc(var(--l12-mobile-resource-summary-h) - 2px) !important; padding:1px var(--l12-card-stat-pad-x,1px) !important; font-size:var(--l12-mobile-resource-font) !important; line-height:1.1 !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack) { grid-template-columns:repeat(4,minmax(11px,1fr)) !important; grid-auto-rows:var(--l12-mobile-morale-orb) !important; gap:2px !important; padding:2px !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack .morale-orb) { width:var(--l12-mobile-morale-orb) !important; min-width:var(--l12-mobile-morale-orb) !important; height:var(--l12-mobile-morale-orb) !important; min-height:var(--l12-mobile-morale-orb) !important; }
.mobile-landscape-board :deep(.battlefield-half .resource-morale-stack .morale-orb img) { width:calc(var(--l12-mobile-morale-orb) - 4px) !important; height:calc(var(--l12-mobile-morale-orb) - 4px) !important; }
.mobile-landscape-board .board-center { grid-template-rows:0 0 minmax(0,1fr) 0 var(--l12-mobile-hand-h) !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child { box-sizing:border-box; height:var(--l12-mobile-hand-h) !important; min-height:var(--l12-mobile-hand-h) !important; margin-top:0 !important; padding:2px 4px !important; align-items:flex-start !important; overflow-x:auto !important; overflow-y:hidden !important; }
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.hand-card-wrap),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-back),
.mobile-landscape-board .board-center > .l12-hand:last-child :deep(.card-tile) { box-sizing:border-box !important; top:0 !important; bottom:auto !important; width:var(--l12-mobile-hand-card-w) !important; min-width:var(--l12-mobile-hand-card-w) !important; height:var(--l12-mobile-hand-card-h) !important; min-height:var(--l12-mobile-hand-card-h) !important; flex:0 0 var(--l12-mobile-hand-card-w) !important; aspect-ratio:5/7 !important; }

/* The complete left rail participates in one allocation model. The persistent
   detail handle is a real first row, never a fixed overlay. Current disaster,
   value and the four round cards each own a non-overlapping rectangle; optional
   opponent/my extra zones share only the remaining height. */
.mobile-landscape-board .left-rail { box-sizing:border-box !important; gap:var(--l12-mobile-rail-gap) !important; overflow:hidden !important; }
.mobile-landscape-board .mobile-detail-handle-reservation { display:block; box-sizing:border-box; width:100%; height:var(--l12-mobile-detail-handle-h); min-height:var(--l12-mobile-detail-handle-h); flex:0 0 var(--l12-mobile-detail-handle-h); pointer-events:none; }
.mobile-landscape-board .left-disaster-row { display:grid !important; width:100% !important; height:auto !important; min-height:0 !important; flex:0 0 auto !important; grid-template-rows:var(--l12-mobile-current-disaster-h) var(--l12-mobile-disaster-value-h) calc(var(--l12-mobile-disaster-orb) * 2 + var(--l12-mobile-rail-gap) + 8px) !important; gap:var(--l12-mobile-rail-gap) !important; }
.mobile-landscape-board .left-disaster-row>.current-disaster-panel { box-sizing:border-box !important; min-height:var(--l12-mobile-current-disaster-h) !important; height:var(--l12-mobile-current-disaster-h) !important; padding:2px !important; overflow:hidden !important; }
.mobile-landscape-board .current-disaster-card { box-sizing:border-box !important; height:calc(var(--l12-mobile-current-disaster-h) - 4px) !important; min-height:0 !important; padding:1px !important; overflow:hidden !important; }
.mobile-landscape-board .mobile-current-disaster-value { box-sizing:border-box; height:var(--l12-mobile-disaster-value-h) !important; min-height:var(--l12-mobile-disaster-value-h) !important; padding:2px 4px; overflow:hidden; }
.mobile-landscape-board .session-disaster-panel { box-sizing:border-box !important; height:calc(var(--l12-mobile-disaster-orb) * 2 + var(--l12-mobile-rail-gap) + 8px) !important; min-height:0 !important; align-content:center !important; padding:4px !important; overflow:hidden !important; }
.mobile-landscape-board .session-disaster-strip { grid-template-columns:repeat(2,var(--l12-mobile-disaster-orb)) !important; grid-template-rows:repeat(2,var(--l12-mobile-disaster-orb)) !important; gap:var(--l12-mobile-rail-gap) !important; }
.mobile-landscape-board .session-disaster-strip button { width:var(--l12-mobile-disaster-orb) !important; min-width:var(--l12-mobile-disaster-orb) !important; max-width:var(--l12-mobile-disaster-orb) !important; height:var(--l12-mobile-disaster-orb) !important; max-height:var(--l12-mobile-disaster-orb) !important; }
.mobile-landscape-board .mobile-extra-zone { box-sizing:border-box; min-height:0 !important; max-height:var(--l12-mobile-extra-zone-h) !important; flex:1 1 var(--l12-mobile-extra-zone-h) !important; overflow:hidden; }
.mobile-landscape-board .mobile-extra-zone>div { min-height:0; max-height:none; overflow-x:hidden; overflow-y:auto; }

/* R6 phone-only proportional allocation. The complete card-bearing group grows
   from one shared card height; no zone receives an independent stretch. */
.mobile-landscape-board .felt-board { display:grid !important; align-content:center !important; }
.mobile-landscape-board .felt-board :deep(.battlefield-half.l12-player-mat) { align-self:center !important; }
.mobile-landscape-board .board-mode-hint {
  position:fixed !important; z-index:2147483500 !important;
  right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 5px) !important;
  bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + 64px) !important;
  left:auto !important; width:var(--l12-mobile-action-w) !important; max-width:var(--l12-mobile-action-w) !important;
  box-sizing:border-box; display:grid !important; gap:3px; padding:4px !important;
}
:global(.mobile-action-dock) { position:fixed !important; z-index:2147483604 !important; right:calc(100vw - var(--l12-viewport-left,0px) - var(--l12-viewport-width,100vw) + 5px) !important; bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + 64px) !important; left:auto !important; display:flex !important; box-sizing:border-box; width:clamp(84px,calc(var(--l12-viewport-height,100vh) * .22 - 6px),104px) !important; max-width:clamp(84px,calc(var(--l12-viewport-height,100vh) * .22 - 6px),104px) !important; max-height:calc(var(--l12-viewport-height,100vh) - 72px); flex-wrap:wrap; gap:3px; padding:3px; overflow:auto; border:1px solid #587b7d; background:rgba(8,12,13,.98); box-shadow:0 5px 18px #000; transform:none !important; pointer-events:auto !important; }
:global(.mobile-action-dock button) { box-sizing:border-box; min-width:0 !important; min-height:clamp(30px,calc(var(--l12-viewport-height,100vh) * .09),38px) !important; flex:1 1 45%; padding:3px 4px !important; font-size:clamp(9px,calc(var(--l12-viewport-height,100vh) * .025),11px) !important; line-height:1.1 !important; white-space:normal; }
.mobile-landscape-board :deep(.card-context-actions) { width:var(--l12-mobile-action-w) !important; max-width:var(--l12-mobile-action-w) !important; }
.mobile-landscape-board .right-rail .action-panel :deep(.l12-actions button),
.mobile-landscape-board .combat-resolution-panel :deep(.l12-actions button) { min-height:var(--l12-mobile-action-h) !important; height:var(--l12-mobile-action-h) !important; font-size:var(--l12-mobile-action-font) !important; }
.mobile-landscape-board :is(.right-rail,.board-mode-hint,.resource-payment-controls,.combat-resolution-panel,.card-context-actions) button,
.mobile-safe-overlay button,
:global(.mobile-action-dock button) { text-align:center; text-wrap:balance; overflow-wrap:anywhere; }
.mobile-landscape-board .board-mode-hint span { font-size:9px !important; line-height:1.15; white-space:normal; }
.mobile-landscape-board .board-mode-hint button { min-height:32px !important; padding:3px 5px !important; }

/* The detail drawer is a reading surface. The small reference image never
   pushes the name, basic values or rules text below the first screen. */
.mobile-card-detail-body { display:grid !important; box-sizing:border-box; width:100%; min-width:0; grid-template-columns:72px minmax(0,1fr); grid-template-rows:minmax(0,1fr); align-items:start; gap:8px; overflow:hidden !important; }
.mobile-card-detail-body :deep(.archive-detail-image),
.mobile-card-detail-body :deep(.archive-detail-image.horizontal) { box-sizing:border-box; width:72px !important; max-width:72px !important; max-height:102px; margin:0 !important; align-self:start; overflow:hidden; }
.mobile-card-detail-body :deep(.archive-detail-image.horizontal) { height:auto; aspect-ratio:8/5; }
.mobile-card-detail-body :deep(.card-detail-copy) { display:flex; box-sizing:border-box; width:100%; min-width:0; min-height:0; max-height:100%; flex-direction:column; overflow:hidden; padding-right:3px; }
.mobile-card-detail-body :deep(.card-detail-copy *) { max-width:100%; }
.mobile-card-detail-body :deep(.card-detail-copy>h2) { margin-top:0; }
.mobile-card-detail-body :deep(.card-detail-copy>.archive-effect) { display:flex; min-height:52px; flex:1; flex-direction:column; overflow:hidden; }
.mobile-card-detail-body :deep(.card-detail-copy>.archive-effect>.l12-effect-body) { min-height:0; flex:1; overflow-x:hidden; overflow-y:auto; overscroll-behavior:contain; }
.mobile-card-detail-body>.battle-card-status { grid-column:2; max-height:70px; overflow:auto; }

/* Morale/rune picker: Otherworld runes are the first, dedicated row. Field
   circles remain a read-only summary and all exact selection happens here. */
.mobile-morale-picker { display:block !important; overflow-y:auto !important; }
.mobile-rune-row,.mobile-resource-row { box-sizing:border-box; width:100%; }
.mobile-rune-row { padding:4px 0 7px; border-bottom:1px solid #46504e; }
.mobile-rune-row>div { display:flex; min-height:48px; align-items:center; gap:7px; overflow-x:auto; scrollbar-width:none; touch-action:pan-x; }
.mobile-rune-row>div::-webkit-scrollbar { display:none; }
.mobile-resource-row { display:grid; grid-template-columns:repeat(auto-fill,44px); align-items:center; gap:7px; padding-top:7px; }
.mobile-morale-choice { position:relative; display:grid !important; box-sizing:border-box !important; width:44px !important; min-width:44px !important; max-width:44px !important; height:44px !important; min-height:44px !important; max-height:44px !important; flex:0 0 44px !important; place-items:center; padding:5px !important; border:2px solid #7a8882 !important; border-radius:50% !important; background:#101516 !important; aspect-ratio:1 !important; cursor:pointer; }
.mobile-morale-choice img { display:block; width:30px !important; height:30px !important; object-fit:contain; border-radius:50%; }
.mobile-morale-choice.rune { border-color:#5d9f71 !important; }
.mobile-morale-choice.god-power { border-color:#60cde8 !important; }
.mobile-morale-choice.black-lotus { border-color:#d4ae42 !important; }
.mobile-morale-choice.temporary { border-color:#e9e9dc !important; }
.mobile-morale-choice.unavailable { filter:grayscale(.82) brightness(.5); border-style:dashed !important; cursor:help; }
.mobile-morale-choice.selected { filter:none; border-color:#f1c75b !important; background:#443713 !important; box-shadow:0 0 0 2px rgba(241,199,91,.55),0 0 10px rgba(241,199,91,.45) !important; }
.mobile-morale-choice.selected::after { content:'✓'; position:absolute; right:-3px; bottom:-3px; display:grid; width:16px; height:16px; place-items:center; border:1px solid #fff3bb; border-radius:50%; background:#796019; color:#fff; font-size:10px; font-weight:900; }
.mobile-morale-reason { box-sizing:border-box; width:100%; min-height:20px; margin:7px 0 0 !important; padding:3px 6px; overflow:hidden; border-left:2px solid #d4ae42; color:#efe2b2 !important; font-size:10px; line-height:14px; text-align:left !important; text-overflow:ellipsis; white-space:nowrap; }
.mobile-morale-restore { position:fixed; z-index:2147483604; left:calc(var(--l12-viewport-left,0px) + 96px); bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + var(--l12-mobile-hand-h) + 5px); min-height:32px; padding:4px 9px; border:1px solid #70d7df; background:#174e54; color:#fff; font-size:10px; font-weight:900; box-shadow:0 6px 18px #000; }
.mobile-record-actions { display:flex; gap:4px; }
.mobile-record-actions button { min-width:58px; }
.mobile-record-restore,.board-control-restore { position:fixed; z-index:2147483604; left:calc(var(--l12-viewport-left,0px) + 96px); min-height:32px; padding:4px 9px; border:1px solid #70d7df; background:#174e54; color:#fff; font-size:10px; font-weight:900; box-shadow:0 6px 18px #000; }
.mobile-record-restore { top:calc(var(--l12-viewport-top,0px) + 6px); }
.board-control-restore { bottom:calc(100vh - var(--l12-viewport-top,0px) - var(--l12-viewport-height,100vh) + var(--l12-mobile-hand-h) + 5px); }
.mobile-landscape-board .board-control-minimize { min-height:30px !important; padding:3px 6px !important; }
</style>
