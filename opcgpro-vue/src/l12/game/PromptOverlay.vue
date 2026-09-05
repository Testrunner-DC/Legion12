<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { Card, DisasterCardView, GameState } from '../types'
import { isHorizontalCardType } from '../cardPresentation'
import { gameAction, l12State, sandboxAction } from '../net'
import { masterProfileUrl } from '../specialAssets'
import CardImage from '../CardImage.vue'
import PromptCardCandidate from './PromptCardCandidate.vue'

const props = withDefaults(defineProps<{
  game: GameState
  mulliganSelectedIds?: string[]
  busy?: boolean
  suppressedPromptId?: string | null
  suppressDefenseWait?: boolean
  readOnly?: boolean
  inspectorVisible?: boolean
}>(), { mulliganSelectedIds: () => [], busy: false, suppressDefenseWait: false, readOnly: false, inspectorVisible: false })
const emit = defineEmits<{
  mulliganToggle: [id: string]
  mulliganConfirm: []
  focusCard: [card: Card]
  minimizedChange: [minimized: boolean]
}>()

const prompt = computed(() => props.game.prompts?.find(item => item.promptId !== props.suppressedPromptId) ?? null)
const waitingPrompt = computed(() => prompt.value ? null : props.game.waitingPrompt ?? null)
const sandboxActorIndex = computed(() => {
  if (!l12State.gmEnabled) return props.game.you
  if (prompt.value) return prompt.value.playerIndex
  if (props.game.phase === 'Mulligan')
    return props.game.players.find(player => !player.mulliganDone)?.playerIndex ?? props.game.activePlayer
  if (props.game.phase === 'Defense' && props.game.pendingDefense)
    return 1 - props.game.pendingDefense.attackerPlayer
  return props.game.activePlayer
})
const me = computed(() => props.game.players[sandboxActorIndex.value] ?? props.game.players[props.game.you] ?? props.game.players[0])
const initiativePlayers = computed(() => [me.value, ...props.game.players.filter(player => player.playerIndex !== me.value.playerIndex)])
const isMulliganPhase = computed(() => props.game.phase === 'Mulligan')
const isMulligan = computed(() => !props.readOnly && isMulliganPhase.value && !me.value.mulliganDone)
const waitingDefense = computed(() => !l12State.gmEnabled && !props.suppressDefenseWait && props.game.phase === 'Defense' && props.game.pendingDefense?.attackerPlayer === props.game.you)
const displayKind = computed(() => prompt.value?.kind ?? waitingPrompt.value?.kind ?? (waitingDefense.value ? 'defense-wait' : isMulliganPhase.value ? 'mulligan' : ''))
const isDisasterPreparation = computed(() => props.game.phase === 'DisasterPreparation')
const isPreparation = computed(() => isDisasterPreparation.value || ['initiative', 'disaster-ban', 'disaster-pick', 'disaster-reveal', 'mulligan'].includes(displayKind.value))
const isInitiative = computed(() => displayKind.value === 'initiative')
const isDisasterChoice = computed(() => ['disaster-ban', 'disaster-pick'].includes(prompt.value?.kind ?? ''))
const visible = computed(() => Boolean(prompt.value || waitingPrompt.value || waitingDefense.value || isMulliganPhase.value || isDisasterPreparation.value))
const selected = ref<string[]>([])
const hoveredChoice = ref<string | null>(null)
const minimized = ref(false)
const placementTop = ref<string[]>([])
const placementBottom = ref<string[]>([])
const placementSelected = ref<string | null>(null)
const draggedChoice = ref<string | null>(null)
const placementOrder = ref<string[]>([])
const animatedRolls = ref([1, 1])
const diceSettled = ref(false)
let diceTimer: ReturnType<typeof setInterval> | null = null
let diceSettleTimer: ReturnType<typeof setTimeout> | null = null
const dieFaces = ['⚀', '⚁', '⚂', '⚃', '⚄', '⚅']
function dieFace(value: number) { return dieFaces[Math.max(1, Math.min(6, value)) - 1] }
function startInitiativeDice() {
  if (diceTimer) clearInterval(diceTimer)
  if (diceSettleTimer) clearTimeout(diceSettleTimer)
  diceSettled.value = false
  diceTimer = setInterval(() => { animatedRolls.value = [1 + Math.floor(Math.random() * 6), 1 + Math.floor(Math.random() * 6)] }, 90)
  diceSettleTimer = setTimeout(() => {
    if (diceTimer) clearInterval(diceTimer)
    diceTimer = null
    animatedRolls.value = [...props.game.initiativeRolls]
    diceSettled.value = true
  }, 1250)
}

watch(() => `${isInitiative.value}:${props.game.matchId}:${props.game.initiativeRolls.join(',')}`, () => {
  if (isInitiative.value) startInitiativeDice()
}, { immediate: true })
onBeforeUnmount(() => { if (diceTimer) clearInterval(diceTimer); if (diceSettleTimer) clearTimeout(diceSettleTimer) })

watch(() => `${prompt.value?.promptId ?? ''}:${props.game.phase}:${me.value.mulliganDone}`, () => {
  selected.value = []
  hoveredChoice.value = null
  minimized.value = false
  placementTop.value = []
  placementBottom.value = []
  placementSelected.value = null
  draggedChoice.value = null
  placementOrder.value = (prompt.value?.validChoices ?? []).filter(id => id !== 'skip')
})
watch(minimized, value => emit('minimizedChange', value), { immediate: true })

function sendAction(command: Record<string, unknown>, actingPlayerIndex = prompt.value?.playerIndex ?? sandboxActorIndex.value) {
  if (l12State.gmEnabled) sandboxAction(actingPlayerIndex, command)
  else gameAction(command)
}

function allCards(): Card[] {
  const zoneCards = props.game.players.flatMap(player => [
    ...(player.hand ?? []), ...player.field.flat().filter(Boolean) as Card[], ...(player.graveyard ?? []),
    ...(player.relic ? [player.relic] : []), ...(player.resolving ?? []),
  ])
  return [
    ...zoneCards,
    ...(props.game.activeDisaster ? [props.game.activeDisaster] : []),
    ...(props.game.bannedDisasters ?? []),
    ...(props.game.removedDisasters ?? []),
    ...(props.game.revealedDisasters ?? []),
    ...(props.game.chosenDisasters ?? []).filter(isVisibleDisasterCard),
  ]
}
function disasterEventOwner(card: Card, type: string, fallback: string) {
  const event = props.game.recentEvents?.find(item => item.type === type && item.cards?.some(entry => entry.instanceId === card.instanceId))
  return event?.playerIndex === undefined || event.playerIndex === null
    ? fallback
    : `${props.game.players[event.playerIndex]?.name ?? `玩家${event.playerIndex + 1}`}选用`
}
function isVisibleDisasterCard(card: DisasterCardView): card is Card {
  return !card.hidden && Boolean(card.cardId && card.name && card.cardType)
}
function disasterOwnerLabel(card: DisasterCardView) {
  if (card.ownerIndex === undefined || card.ownerIndex < 0) return '玩家已选用'
  return `${props.game.players[card.ownerIndex]?.name ?? `玩家${card.ownerIndex + 1}`}选用`
}
const disasterHistory = computed(() => [
  {
    key: 'banned', label: '已禁用', entries: (props.game.bannedDisasters ?? []).map(card => ({
      card, note: disasterEventOwner(card, 'disaster-banned', '已禁用').replace('选用', '禁用'),
    })),
  },
  {
    key: 'chosen', label: '已选用', entries: [
      ...(props.game.revealedDisasters ?? []).map(card => ({ card, note: '随机公开' })),
      ...(props.game.chosenDisasters ?? []).map(card => ({
        card,
        note: card.hidden ? disasterOwnerLabel(card) : disasterEventOwner(card as Card, 'disaster-selected', disasterOwnerLabel(card)),
      })),
    ],
  },
])
function cardFor(id: string) { return allCards().find(card => card.instanceId === id) }
function isInternalChoiceValue(value: string) {
  const normalized = value.trim().toLowerCase()
  return /^(mode|continuation|action|prompt|activation|stack|effect)(:|[-_])/.test(normalized)
    || /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(normalized)
}
function safeChoiceFallback(id: string) {
  const position = prompt.value?.validChoices.indexOf(id) ?? -1
  return position >= 0 ? `效果选项 ${position + 1}` : '效果选项'
}
function naturalChoiceLabel(value: string | undefined, id: string) {
  const normalized = value?.trim()
  return normalized && normalized !== id && !isInternalChoiceValue(normalized) ? normalized : null
}
function label(id: string) {
  if (isEffectDecision.value) {
    if (['yes', 'mode:use'].includes(id.toLowerCase())) return '发动'
    if (['no', 'mode:none'].includes(id.toLowerCase())) return '不发动'
  }
  const base = naturalChoiceLabel(prompt.value?.choiceLabels?.[id], id)
    ?? cardFor(id)?.name
    ?? naturalChoiceLabel(prompt.value?.data?.[`${id}:name`], id)
    ?? naturalChoiceLabel(prompt.value?.data?.[id], id)
    ?? safeChoiceFallback(id)
  const zone = naturalChoiceLabel(prompt.value?.data?.[`${id}:zone`], id)
  return zone ? `${base} · ${zone}` : base
}
function imageFor(id: string) { return prompt.value?.data?.[`${id}:image`] ?? cardFor(id)?.imageUrl }
function cardIdFor(id: string) {
  return cardFor(id)?.cardId ?? prompt.value?.data?.[`${id}:cardId`] ?? (/^(?:S\d{2}|ST\d{2}|ST)-/.test(id) ? id : '')
}
function cardName(id: string) {
  return naturalChoiceLabel(prompt.value?.data?.[`${id}:name`], id)
    ?? cardFor(id)?.name
    ?? label(id)
}
function selectionHint(id: string) {
  const value = naturalChoiceLabel(prompt.value?.choiceLabels?.[id], id)
  return value?.startsWith('当前') ? value : ''
}
function cardMeta(id: string) {
  return naturalChoiceLabel(prompt.value?.data?.[`${id}:zone`], id)
    ?? (prompt.value?.kind === 'opponent-hand-card' && !cardIdFor(id) ? '匿名手牌' : '')
}
function numberData(id: string, key: string) {
  const value = prompt.value?.data?.[`${id}:${key}`]
  return value === undefined || value === '' ? undefined : Number(value)
}
function booleanData(id: string, key: string) {
  const value = prompt.value?.data?.[`${id}:${key}`]
  return value === undefined ? undefined : value === 'true'
}
function detailFor(id: string | null) {
  if (!id) return null
  const card = cardFor(id)
  const imageUrl = imageFor(id)
  const cardId = cardIdFor(id)
  if (!card && !imageUrl && !cardId) return null
  return {
    id,
    cardId,
    name: cardName(id),
    imageUrl,
    effectText: card?.effectText ?? prompt.value?.data?.[`${id}:effect`] ?? '',
    cardType: card?.cardType ?? prompt.value?.data?.[`${id}:cardType`] ?? '',
    traits: card?.traits ?? prompt.value?.data?.[`${id}:traits`]?.split('|').filter(Boolean) ?? [],
    profession: (card?.profession ?? prompt.value?.data?.[`${id}:profession`]) || undefined,
    hasPrintedCost: card?.hasPrintedCost ?? booleanData(id, 'hasPrintedCost'),
    cost: (card?.hasPrintedCost ?? booleanData(id, 'hasPrintedCost')) === false
      ? undefined
      : card?.playCost ?? card?.currentCost ?? card?.cost ?? numberData(id, 'cost'),
    troops: card?.troops ?? numberData(id, 'troops'),
    baseTroops: card?.baseTroops ?? numberData(id, 'baseTroops'),
    disasterLevel: card?.disasterLevel ?? numberData(id, 'disasterLevel'),
  }
}
function cardObjectFor(id: string): Card | null {
  const existing = cardFor(id)
  if (existing) return existing
  const detail = detailFor(id)
  if (!detail) return null
  return {
    instanceId: id,
    cardId: detail.cardId,
    name: detail.name,
    cardType: detail.cardType,
    faction: prompt.value?.data?.[`${id}:faction`] ?? '',
    traits: detail.traits,
    profession: detail.profession,
    imageUrl: detail.imageUrl,
    effectText: detail.effectText,
    cost: detail.cost ?? 0,
    hasPrintedCost: detail.hasPrintedCost ?? detail.cost !== undefined,
    baseTroops: detail.baseTroops ?? detail.troops ?? 0,
    troops: detail.troops ?? detail.baseTroops ?? 0,
    disasterLevel: detail.disasterLevel ?? 0,
    tapped: false,
    summonRound: 0,
  }
}
watch(() => `${prompt.value?.promptId ?? ''}:${prompt.value?.data?.sourceInstanceId ?? ''}`, () => {
  const sourceInstanceId = prompt.value?.data?.sourceInstanceId
  if (!sourceInstanceId) return
  const source = cardObjectFor(sourceInstanceId)
  if (source) emit('focusCard', source)
}, { immediate: true })
function focusChoice(id: string) {
  hoveredChoice.value = id
  const card = cardObjectFor(id)
  if (card) emit('focusCard', card)
}
function focusHistoryCard(card: DisasterCardView) {
  if (!isVisibleDisasterCard(card)) return
  focusChoice(card.instanceId)
}

const currentChoices = computed(() => isMulligan.value ? (me.value.hand ?? []).map(card => card.instanceId) : (prompt.value?.validChoices ?? []))
const isEffectDecision = computed(() => prompt.value?.data?.uiPattern === 'effect-decision')
const previewCardId = computed(() => prompt.value?.data?.previewCardId ?? null)
const previewPresentation = computed(() => prompt.value?.data?.previewPresentation ?? '')
const showPreviewCard = computed(() => Boolean(previewCardId.value)
  && ['handled-card', 'information-card'].includes(previewPresentation.value))
const declineChoices = new Set(['no', 'mode:none', 'skip'])
const orderedEffectChoices = computed(() => [
  ...currentChoices.value.filter(choice => !declineChoices.has(choice.toLowerCase())),
  ...currentChoices.value.filter(choice => declineChoices.has(choice.toLowerCase())),
])
const displayedChoices = computed(() => {
  if (prompt.value?.kind === 'option') return orderedEffectChoices.value
  const listed = prompt.value?.data?.displayCardIds?.split('|').filter(Boolean)
  if (listed?.length) return listed
  if (showPreviewCard.value && previewCardId.value && !currentChoices.value.length) return [previewCardId.value]
  return currentChoices.value
})
const supplementalChoices = computed(() => currentChoices.value
  .filter(id => !displayedChoices.value.includes(id)))
const isCardSelectionPrompt = computed(() => prompt.value?.data?.cardSelection === 'true')
const placementMode = computed(() => prompt.value?.data?.placementMode ?? '')
const currentSelected = computed(() => isMulligan.value ? props.mulliganSelectedIds : ['split-top-bottom', 'all-top-bottom', 'all-bottom'].includes(placementMode.value) ? (placementSelected.value ? [placementSelected.value] : []) : selected.value)
const hasCardChoices = computed(() => !isEffectDecision.value
  && (showPreviewCard.value || displayedChoices.value.some(id => Boolean(detailFor(id)))))
const isEffectOptionList = computed(() => (isEffectDecision.value || prompt.value?.kind === 'option')
  && !hasCardChoices.value && !isInitiative.value)
const displayedCardsAreAllFromHand = computed(() => {
  const handIds = new Set(props.game.players.flatMap(player => (player.hand ?? []).map(card => card.instanceId)))
  const cards = displayedChoices.value.filter(id => Boolean(detailFor(id)))
  return cards.length > 0 && cards.every(id => handIds.has(id))
})
const isSingleCardRow = computed(() => prompt.value?.data?.layout === 'single-row'
  || prompt.value?.data?.sourceZone === 'hand'
  || displayedCardsAreAllFromHand.value
  || placementMode.value === 'single-top-bottom')
const unassignedChoices = computed(() => currentChoices.value.filter(id => !placementTop.value.includes(id) && !placementBottom.value.includes(id)))
const overlayTitle = computed(() => {
  if (prompt.value) return isEffectDecision.value ? (prompt.value.data?.sourceName || prompt.value.text) : prompt.value.text
  if (isMulligan.value) return '选择需要调度的起始手牌'
  if (isMulliganPhase.value) return '等待对手完成调度'
  return waitingText()
})
const decisionEffectText = computed(() => prompt.value?.data?.effectText?.trim() || prompt.value?.text || '')

function toggle(id: string) {
  const p = prompt.value
  if (!p || !p.validChoices.includes(id)) return
  if (p.data?.choiceMode === 'instant') { resolveChoice(id); return }
  const index = selected.value.indexOf(id)
  if (index >= 0) { selected.value.splice(index, 1); return }
  if (p.maxChoose === 1) selected.value = [id]
  else if (selected.value.length < p.maxChoose) selected.value.push(id)
}
function resolveChoice(choice: string) {
  const p = prompt.value
  if (!p || !p.validChoices.includes(choice)) return
  sendAction({ type: 'resolvePrompt', promptId: p.promptId, cardInstanceIds: [choice] }, p.playerIndex)
}
function confirm() {
  const p = prompt.value
  if (!p || selected.value.length < p.minChoose || selected.value.length > p.maxChoose) return
  sendAction({ type: 'resolvePrompt', promptId: p.promptId, cardInstanceIds: [...selected.value] }, p.playerIndex)
}
function resolveSinglePlacement(destination: 'top' | 'bottom') {
  const p = prompt.value
  if (!p || selected.value.length !== 1) return
  sendAction({ type: 'resolvePrompt', promptId: p.promptId, cardInstanceIds: [...selected.value], destination }, p.playerIndex)
}
function removePlacement(id: string) {
  placementTop.value = placementTop.value.filter(choice => choice !== id)
  placementBottom.value = placementBottom.value.filter(choice => choice !== id)
}
function returnToUnassigned(id: string) {
  removePlacement(id)
  placementSelected.value = id
}
function assignPlacement(destination: 'top' | 'bottom', id = placementSelected.value) {
  if (!id) return
  removePlacement(id)
  const target = destination === 'top' ? placementTop.value : placementBottom.value
  target.push(id)
  placementSelected.value = id
}
function selectSplitSwap(id: string) {
  focusChoice(id)
  if (!placementSelected.value) { placementSelected.value = id; return }
  if (placementSelected.value === id) { placementSelected.value = null; return }
  const locate = (choice: string) => placementTop.value.includes(choice) ? placementTop.value : placementBottom.value.includes(choice) ? placementBottom.value : null
  const firstList = locate(placementSelected.value)
  const secondList = locate(id)
  if (firstList && secondList) {
    const first = firstList.indexOf(placementSelected.value)
    const second = secondList.indexOf(id)
    ;[firstList[first], secondList[second]] = [secondList[second], firstList[first]]
  }
  placementSelected.value = null
}
function dropPlacement(destination: 'top' | 'bottom', beforeId?: string) {
  const id = draggedChoice.value
  if (!id) return
  removePlacement(id)
  const target = destination === 'top' ? placementTop.value : placementBottom.value
  const index = beforeId ? target.indexOf(beforeId) : -1
  if (index >= 0) target.splice(index, 0, id); else target.push(id)
  placementSelected.value = id
  draggedChoice.value = null
}
function confirmSplitPlacement() {
  const p = prompt.value
  if (!p || unassignedChoices.value.length) return
  sendAction({
    type: 'resolvePrompt', promptId: p.promptId,
    topCardInstanceIds: [...placementTop.value], bottomCardInstanceIds: [...placementBottom.value],
  }, p.playerIndex)
}
function reorderAll(beforeId: string) {
  const id = draggedChoice.value
  if (!id || id === beforeId) return
  placementOrder.value = placementOrder.value.filter(choice => choice !== id)
  const index = placementOrder.value.indexOf(beforeId)
  placementOrder.value.splice(index < 0 ? placementOrder.value.length : index, 0, id)
  draggedChoice.value = null
}
function selectSwapChoice(id: string) {
  focusChoice(id)
  if (!placementSelected.value) { placementSelected.value = id; return }
  if (placementSelected.value === id) { placementSelected.value = null; return }
  const first = placementOrder.value.indexOf(placementSelected.value)
  const second = placementOrder.value.indexOf(id)
  if (first >= 0 && second >= 0) {
    const next = [...placementOrder.value]
    ;[next[first], next[second]] = [next[second], next[first]]
    placementOrder.value = next
  }
  placementSelected.value = null
}
function confirmAllPlacement(destination: 'top' | 'bottom') {
  const p = prompt.value
  if (!p || placementOrder.value.length !== p.validChoices.length) return
  sendAction({
    type: 'resolvePrompt', promptId: p.promptId,
    topCardInstanceIds: destination === 'top' ? [...placementOrder.value] : [],
    bottomCardInstanceIds: destination === 'bottom' ? [...placementOrder.value] : [],
  }, p.playerIndex)
}
const isInfoConfirm = computed(() => ['disaster-reveal', 'disaster-trigger'].includes(prompt.value?.kind ?? ''))
const usesDetailCardImages = computed(() => isDisasterChoice.value || isInfoConfirm.value)
function waitingText() {
  if (waitingDefense.value) return `${props.game.players[1 - props.game.you].name} 正在选择是否支援或抵挡`
  const waiting = waitingPrompt.value
  if (!waiting) return ''
  const action: Record<string, string> = {
    initiative: '正在选择先攻或后攻',
    'disaster-ban': '正在禁用天灾',
    'disaster-pick': '正在选择天灾',
    'disaster-reveal': '正在确认随机公开天灾',
    'disaster-trigger': '正在确认触发的天灾',
    response: '正在选择是否响应',
    'discard-cost': '正在支付响应费用',
    target: '正在选择效果目标', targets: '正在选择效果目标',
    'optional-target': '正在决定是否发动并选择目标', 'optional-targets': '正在决定是否发动并选择目标',
    'active-target': '正在选择主动效果目标',
    option: '正在选择效果', optional: '正在决定是否发动效果',
    card: '正在选择卡牌', cards: '正在选择卡牌', search: '正在查看并选择卡牌',
    discard: '正在选择弃置卡牌', order: '正在排列卡牌', slot: '正在选择战场位置',
  }
  return `${waiting.playerName} ${action[waiting.kind] ?? '正在处理选择'}`
}
function kindLabel() {
  if (isEffectDecision.value) return 'OPTION'
  if (isInitiative.value) return '先后攻决定'
  if (isDisasterChoice.value) return prompt.value?.kind === 'disaster-ban' ? '天灾禁用' : '天灾选择'
  if (prompt.value?.kind === 'disaster-reveal') return '随机公开天灾'
  if (prompt.value?.kind === 'disaster-trigger') return '天灾触发'
  return prompt.value?.kind ?? ''
}
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="l12-prompt-overlay"
      :class="{ preparation: isPreparation, initiative: isInitiative, 'disaster-choice': isDisasterChoice, 'information-confirm': isInfoConfirm, waiting: waitingPrompt || (isMulliganPhase && !isMulligan), minimized, 'inspector-active': inspectorVisible }">
      <section v-if="minimized" class="prompt-minimized-bar" role="status">
        <button :aria-label="`展开：${overlayTitle}`" :title="overlayTitle" @click="minimized = false">展开</button>
      </section>

      <section v-else-if="prompt" class="prompt-panel" :class="{ 'has-card-choices': hasCardChoices, 'single-card-row': isSingleCardRow, 'effect-decision': isEffectDecision }" role="dialog" aria-modal="true" :aria-label="prompt.text">
        <header :class="{ 'effect-decision-header': isEffectDecision }">
          <small>{{ kindLabel() }}</small><h2>{{ isEffectDecision ? (prompt.data?.sourceName || prompt.text) : prompt.text }}</h2>
          <p v-if="isEffectDecision" class="effect-decision-text l12-effect-body">{{ decisionEffectText }}</p>
          <button v-if="!isDisasterPreparation" class="prompt-minimize" aria-label="最小化弹框" title="最小化" @click="minimized = true">—</button>
        </header>
        <div v-if="isInitiative" class="initiative-race" :class="{ settled: diceSettled }">
          <article v-for="player in initiativePlayers" :key="player.playerIndex" :class="{ winner: diceSettled && game.diceWinner === player.playerIndex }">
            <img :src="masterProfileUrl(player.master.masterId, player.master.masterImageUrl)" :alt="player.master.masterName" />
            <div><strong>{{ player.name }}</strong><span>{{ player.master.masterName }}</span></div>
            <b>{{ dieFace(animatedRolls[player.playerIndex] ?? 1) }}</b><em>{{ animatedRolls[player.playerIndex] ?? 1 }} 点</em>
          </article>
        </div>
        <div v-if="isDisasterPreparation" class="disaster-preparation-history" aria-label="天灾准备进度">
          <section v-for="group in disasterHistory" :key="group.key" :class="group.key">
            <header><b>{{ group.label }}</b><span>{{ group.entries.length }}</span></header>
            <div><button v-for="entry in group.entries" :key="entry.card.instanceId" :class="{ hidden: entry.card.hidden }"
              @click="focusHistoryCard(entry.card)" @mouseenter="focusHistoryCard(entry.card)">
              <img v-if="entry.card.hidden" src="/assets/l12/card-back-disaster.png" alt="未揭示天灾"/>
               <CardImage v-else :card-id="entry.card.cardId || ''" :legacy-url="entry.card.imageUrl" :alt="entry.card.name || '天灾'" intent="detail" eager/><span>{{ entry.card.name || '未揭示天灾' }}</span><small>{{ entry.note }}</small>
            </button><p v-if="!group.entries.length">等待本阶段结果</p></div>
          </section>
        </div>
        <div v-if="showPreviewCard && previewCardId && !displayedChoices.includes(previewCardId)" class="prompt-card-strip featured-card-strip">
          <PromptCardCandidate :card-id="cardIdFor(previewCardId)" :legacy-url="imageFor(previewCardId)"
            :name="cardName(previewCardId)" :meta="cardMeta(previewCardId)" :horizontal="isHorizontalCardType(detailFor(previewCardId)?.cardType)"
            intent="detail" size="featured" @focus="focusChoice(previewCardId)" @select="focusChoice(previewCardId)"/>
        </div>
        <div v-if="placementMode === 'all-top-bottom' || placementMode === 'all-bottom'" class="all-placement-workspace">
          <strong class="placement-edge top-edge">{{ placementMode === 'all-bottom' ? '先放到底' : '靠顶' }}</strong>
          <div class="prompt-card-strip all-placement-row">
            <PromptCardCandidate v-for="choice in placementOrder" :key="choice"
              :card-id="cardIdFor(choice)" :legacy-url="imageFor(choice)" :name="cardName(choice)" :meta="cardMeta(choice)"
              :horizontal="isHorizontalCardType(detailFor(choice)?.cardType)" :selected="placementSelected === choice"
              draggable="true" @dragstart="draggedChoice = choice" @dragover.prevent @drop.stop.prevent="reorderAll(choice)"
              @focus="focusChoice(choice)" @select="selectSwapChoice(choice)"/>
          </div>
          <strong class="placement-edge bottom-edge">{{ placementMode === 'all-bottom' ? '后放到底' : '靠底' }}</strong>
        </div>
        <div v-else-if="placementMode === 'split-top-bottom'" class="placement-workspace">
          <section class="placement-destination top" @dragover.prevent @drop="dropPlacement('top')">
            <header><strong>靠顶</strong><small>从左到右，最左侧最靠牌库顶</small></header>
            <div class="prompt-card-strip placement-row">
              <PromptCardCandidate v-for="choice in placementTop" :key="choice" size="compact"
                :card-id="cardIdFor(choice)" :legacy-url="imageFor(choice)" :name="cardName(choice)" :meta="cardMeta(choice)"
                :horizontal="isHorizontalCardType(detailFor(choice)?.cardType)" :selected="placementSelected === choice"
                removable draggable="true"
                @dragstart="draggedChoice = choice" @dragover.prevent @drop.stop.prevent="dropPlacement('top', choice)"
                @focus="focusChoice(choice)" @select="selectSplitSwap(choice)" @remove="returnToUnassigned(choice)"/>
              <p v-if="!placementTop.length">拖到这里，或选中后点击“放回顶部”</p>
            </div>
          </section>

          <section class="placement-candidates">
            <header><strong>待安排</strong><small>拖动卡牌决定归属和顺序</small></header>
            <div class="prompt-card-strip placement-row">
              <PromptCardCandidate v-for="choice in unassignedChoices" :key="choice" size="compact"
                :card-id="cardIdFor(choice)" :legacy-url="imageFor(choice)" :name="cardName(choice)" :meta="cardMeta(choice)"
                :horizontal="isHorizontalCardType(detailFor(choice)?.cardType)" :selected="placementSelected === choice"
                draggable="true" @dragstart="draggedChoice = choice" @focus="focusChoice(choice)"
                @select="placementSelected = choice; focusChoice(choice)"/>
              <p v-if="!unassignedChoices.length">全部卡牌均已安排</p>
            </div>
            <div class="placement-buttons">
              <button :disabled="!placementSelected" @click="assignPlacement('top')">放回顶部</button>
              <button :disabled="!placementSelected" @click="assignPlacement('bottom')">放回底部</button>
            </div>
          </section>

          <section class="placement-destination bottom" @dragover.prevent @drop="dropPlacement('bottom')">
            <header><strong>靠底</strong><small>从左到右，最左侧最先到达牌库底</small></header>
            <div class="prompt-card-strip placement-row">
              <PromptCardCandidate v-for="choice in placementBottom" :key="choice" size="compact"
                :card-id="cardIdFor(choice)" :legacy-url="imageFor(choice)" :name="cardName(choice)" :meta="cardMeta(choice)"
                :horizontal="isHorizontalCardType(detailFor(choice)?.cardType)" :selected="placementSelected === choice"
                removable draggable="true"
                @dragstart="draggedChoice = choice" @dragover.prevent @drop.stop.prevent="dropPlacement('bottom', choice)"
                @focus="focusChoice(choice)" @select="selectSplitSwap(choice)" @remove="returnToUnassigned(choice)"/>
              <p v-if="!placementBottom.length">拖到这里，或选中后点击“放回底部”</p>
            </div>
          </section>
        </div>
        <div v-else class="prompt-choices" :class="{ 'prompt-card-strip': hasCardChoices, 'effect-option-list': isEffectOptionList }">
          <template v-for="choice in displayedChoices" :key="choice">
            <PromptCardCandidate v-if="detailFor(choice)"
              :card-id="cardIdFor(choice)" :legacy-url="imageFor(choice)" :name="cardName(choice)" :meta="cardMeta(choice)"
              :badge="selectionHint(choice)"
              :horizontal="isHorizontalCardType(detailFor(choice)?.cardType)" :selected="selected.includes(choice)"
              :unavailable="isCardSelectionPrompt && !prompt.validChoices.includes(choice)"
              :intent="usesDetailCardImages ? 'detail' : 'thumb'" :size="isInfoConfirm ? 'featured' : 'standard'"
              :selection-order="selected.includes(choice) && prompt.maxChoose > 1 ? selected.indexOf(choice) + 1 : undefined"
              @focus="focusChoice(choice)" @select="toggle(choice)"/>
            <button v-else :class="{ selected: selected.includes(choice) }" @click="toggle(choice)">
              <span :class="{ 'l12-effect-body': isEffectOptionList, 'l12-effect-body--compact': isEffectOptionList }">{{ label(choice) }}</span>
            </button>
          </template>
        </div>
        <div v-if="supplementalChoices.length && prompt.data?.choiceMode !== 'optional-add'" class="prompt-supplemental-choices">
          <button v-for="choice in supplementalChoices" :key="choice" :class="{ selected: selected.includes(choice) }"
            @click="toggle(choice)">{{ label(choice) }}</button>
        </div>
        <footer>
          <template v-if="placementMode === 'single-top-bottom'">
            <span>先选择 1 张手牌，再决定放回位置</span>
            <button :disabled="l12State.pendingAction || selected.length !== 1" @click="resolveSinglePlacement('top')">放回顶部</button>
            <button class="primary" :disabled="l12State.pendingAction || selected.length !== 1" @click="resolveSinglePlacement('bottom')">放回底部</button>
          </template>
          <template v-else-if="placementMode === 'split-top-bottom'">
            <span>靠顶 {{ placementTop.length }} / 靠底 {{ placementBottom.length }} / 待安排 {{ unassignedChoices.length }}；已安排的牌可依次点击两张交换位置</span>
            <button class="primary" :disabled="l12State.pendingAction || unassignedChoices.length > 0" @click="confirmSplitPlacement">
              {{ l12State.pendingAction ? '处理中…' : '确认排列' }}
            </button>
          </template>
          <template v-else-if="placementMode === 'all-top-bottom'">
            <span>依次点击两张牌交换位置，然后将全部卡牌放回同一端</span>
            <button :disabled="l12State.pendingAction" @click="confirmAllPlacement('top')">全部放回顶部</button>
            <button class="primary" :disabled="l12State.pendingAction" @click="confirmAllPlacement('bottom')">全部放回底部</button>
          </template>
          <template v-else-if="placementMode === 'all-bottom'">
            <span>依次点击两张牌交换位置；左侧卡牌先放回牌库底部</span>
            <button class="primary" :disabled="l12State.pendingAction" @click="confirmAllPlacement('bottom')">确认顺序并全部放回底部</button>
          </template>
          <template v-else-if="prompt.data?.choiceMode === 'optional-add'">
            <span>选择后，将在下一步排列其余展示牌返回牌库底部的顺序</span>
            <button :disabled="l12State.pendingAction" @click="resolveChoice('skip')">不加入手牌</button>
            <button class="primary" :disabled="l12State.pendingAction || selected.length !== 1" @click="resolveChoice(selected[0])">加入手牌</button>
          </template>
          <template v-else-if="prompt.data?.choiceMode === 'instant'">
            <span>点击选项后立即结算</span>
          </template>
          <template v-else>
            <span>{{ isEffectDecision ? '请选择是否发动本次效果' : isInfoConfirm ? '双方均确认后继续' : `选择 ${prompt.minChoose}–${prompt.maxChoose} 项` }}</span>
            <button v-if="prompt.minChoose === 0 && !isInfoConfirm" :disabled="l12State.pendingAction" @click="selected = []; confirm()">不选择</button>
            <button class="primary" :disabled="l12State.pendingAction || selected.length < prompt.minChoose || selected.length > prompt.maxChoose" @click="confirm">
              {{ l12State.pendingAction ? '处理中…' : (isInfoConfirm ? '确认信息' : '确认选择') }}
            </button>
          </template>
        </footer>
      </section>

      <section v-else-if="isMulligan" class="prompt-panel mulligan-panel has-card-choices" role="dialog" aria-modal="true" aria-label="起始手牌调度">
        <header>
          <small>调度</small><h2>选择需要调度的起始手牌</h2>
          <button class="prompt-minimize" aria-label="最小化弹框" title="最小化以查看场面" @click="minimized = true">—</button>
        </header>
        <div class="prompt-choices prompt-card-strip">
          <PromptCardCandidate v-for="card in me.hand" :key="card.instanceId"
            :card-id="card.cardId" :legacy-url="card.imageUrl" :name="card.name" meta="手牌"
            :horizontal="isHorizontalCardType(card.cardType)" :selected="mulliganSelectedIds.includes(card.instanceId)"
            @focus="focusChoice(card.instanceId)" @select="emit('mulliganToggle', card.instanceId)"/>
        </div>
        <footer><span>已选择 {{ mulliganSelectedIds.length }} 张</span><button class="primary" :disabled="busy" @click="emit('mulliganConfirm')">{{ busy ? '处理中…' : '确认调度' }}</button></footer>
      </section>

      <section v-else class="prompt-panel waiting-panel" role="status">
        <button v-if="!isDisasterPreparation" class="prompt-minimize" aria-label="最小化弹框" title="最小化" @click="minimized = true">—</button>
        <div v-if="isDisasterPreparation" class="disaster-preparation-history" aria-label="天灾准备进度">
          <section v-for="group in disasterHistory" :key="group.key" :class="group.key">
            <header><b>{{ group.label }}</b><span>{{ group.entries.length }}</span></header>
            <div><button v-for="entry in group.entries" :key="entry.card.instanceId" :class="{ hidden: entry.card.hidden }"
              @click="focusHistoryCard(entry.card)" @mouseenter="focusHistoryCard(entry.card)">
              <img v-if="entry.card.hidden" src="/assets/l12/card-back-disaster.png" alt="未揭示天灾"/>
               <CardImage v-else :card-id="entry.card.cardId || ''" :legacy-url="entry.card.imageUrl" :alt="entry.card.name || '天灾'" intent="detail" eager/><span>{{ entry.card.name || '未揭示天灾' }}</span><small>{{ entry.note }}</small>
            </button><p v-if="!group.entries.length">等待本阶段结果</p></div>
          </section>
        </div>
        <div v-if="isInitiative" class="initiative-race" :class="{ settled: diceSettled }">
          <article v-for="player in initiativePlayers" :key="player.playerIndex" :class="{ winner: diceSettled && game.diceWinner === player.playerIndex }">
            <img :src="masterProfileUrl(player.master.masterId, player.master.masterImageUrl)" :alt="player.master.masterName" />
            <div><strong>{{ player.name }}</strong><span>{{ player.master.masterName }}</span></div>
            <b>{{ dieFace(animatedRolls[player.playerIndex] ?? 1) }}</b><em>{{ animatedRolls[player.playerIndex] ?? 1 }} 点</em>
          </article>
        </div>
        <small>{{ isMulliganPhase ? '调度' : waitingDefense ? '进攻结算' : '对手操作' }}</small>
        <h2>{{ isMulliganPhase ? '等待对手完成调度' : waitingText() }}</h2>
        <i/><i/><i/>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.initiative-race{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:16px 0}.initiative-race article{display:grid;grid-template-columns:52px 1fr 58px;grid-template-rows:auto auto;align-items:center;gap:3px 9px;padding:10px;border:2px solid #4c5553;background:#0c1112}.initiative-race article.winner{border-color:#e4bd58;box-shadow:0 0 18px rgba(228,189,88,.35)}.initiative-race img{grid-row:1/3;width:52px;height:73px;object-fit:contain}.initiative-race div{display:grid}.initiative-race strong{color:#fff;font-size:12px}.initiative-race span{color:#89928e;font-size:9px}.initiative-race b{grid-column:3;grid-row:1/3;color:#fff;font-size:52px;line-height:1;animation:dice-shake .18s infinite alternate}.initiative-race.settled b{animation:dice-land .32s ease-out}.initiative-race em{grid-column:3;grid-row:2;color:#e6c15e;font-size:9px;font-style:normal;text-align:center;transform:translateY(14px)}@keyframes dice-shake{from{transform:rotate(-9deg) scale(.94)}to{transform:rotate(9deg) scale(1.05)}}@keyframes dice-land{0%{transform:scale(1.35) rotate(18deg)}100%{transform:scale(1) rotate(0)}}
.l12-prompt-overlay{position:fixed!important;z-index:2147483600!important;inset:0;box-sizing:border-box;display:flex!important;width:100vw;height:100vh;align-items:center!important;justify-content:center!important;padding:18px;background:rgba(2,4,5,.48)!important;backdrop-filter:blur(3px)}
.l12-prompt-overlay.inspector-active:not(.minimized){--inspector-safe-lane:clamp(118px,19vw,258px);padding-left:var(--inspector-safe-lane)}.l12-prompt-overlay.inspector-active:not(.minimized) .prompt-panel{max-width:calc(100vw - var(--inspector-safe-lane) - 18px)}
.prompt-panel{position:relative;width:min(760px,calc(100vw - 36px));max-height:calc(100vh - 36px);margin:auto;padding:16px;overflow:hidden}
.prompt-panel header{position:relative;padding-right:44px}.prompt-minimize{position:absolute;right:0;top:0;width:32px;height:27px;border:1px solid #8b918d;background:#111718;color:#fff;font-size:18px;line-height:18px}.prompt-minimize:hover{border-color:#70d7df;background:#174e54}
.l12-prompt-overlay.initiative .prompt-panel{width:min(480px,calc(100vw - 32px));padding:24px}.l12-prompt-overlay.initiative .prompt-choices{display:grid;grid-template-columns:1fr 1fr;min-height:112px;align-items:stretch}.l12-prompt-overlay.initiative .prompt-choices>button{width:100%;max-width:none;min-height:92px;border:2px solid #eeeadf;background:#121718;color:#fff;font-size:18px}.l12-prompt-overlay.initiative .prompt-choices>button:hover,.l12-prompt-overlay.initiative .prompt-choices>button.selected{border-color:#7de1e7;background:#1b6f77;color:#fff}
.prompt-panel.has-card-choices{width:min(920px,calc(100vw - 36px))}.prompt-card-strip{display:flex;min-width:0;max-width:100%;flex-wrap:nowrap;align-items:flex-start;justify-content:flex-start;gap:8px;padding:10px 3px;overflow-x:auto;overflow-y:hidden;scrollbar-color:#65706d #111516;scrollbar-width:thin}.prompt-choices.prompt-card-strip{max-height:none}.featured-card-strip{justify-content:center;margin:2px auto}
.prompt-choices.effect-option-list{display:grid;max-width:100%;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));align-items:stretch;gap:8px;margin:12px auto;padding:2px 1px 8px;overflow:visible}.prompt-choices.effect-option-list>button{width:100%;min-width:0;max-width:none;min-height:54px;padding:10px 16px;border:2px solid #d9d8cf;background:#101516;color:#fff;font-size:11px;font-weight:900;line-height:1.55;text-align:left;white-space:normal}.prompt-choices.effect-option-list>button:hover,.prompt-choices.effect-option-list>button.selected{border-color:#70d7df;background:#174e54;color:#fff}
.prompt-supplemental-choices{display:flex;justify-content:flex-end;gap:7px;margin:4px 3px}.prompt-supplemental-choices button{padding:7px 12px;border:1px solid #8b918d;background:#111718;color:#fff;font-size:9px;font-weight:900}.prompt-supplemental-choices button:hover,.prompt-supplemental-choices button.selected{border-color:#70d7df;background:#174e54}
.effect-decision-header h2{margin-bottom:8px}.effect-decision-text{margin:0;padding:11px 13px;border:1px solid #3b4542;background:#0b1011;color:#eef0eb;font-size:13px;line-height:1.75;white-space:pre-wrap}.prompt-panel.effect-decision .prompt-choices.effect-option-list{max-width:520px}.prompt-panel.effect-decision .prompt-choices.effect-option-list>button{text-align:center;font-size:14px}
.prompt-panel.single-card-row{width:min(920px,calc(100vw - 36px))}.l12-prompt-overlay.information-confirm .prompt-panel{width:min(850px,calc(100vw - 36px));overflow-y:auto}.l12-prompt-overlay.information-confirm .prompt-card-strip{justify-content:center}.mulligan-panel{width:min(920px,calc(100vw - 36px))!important}.l12-prompt-overlay.disaster-choice .prompt-panel{width:min(980px,calc(100vw - 36px))}
.placement-workspace{display:grid;grid-template-columns:1fr 1.1fr 1fr;gap:8px;min-height:166px;margin:9px 3px;padding:8px;border:1px solid rgba(238,238,228,.28);background:#090d0e}.placement-workspace>section{min-width:0;padding:7px;border:1px solid #39413f;background:#101516}.placement-workspace>section>header{display:block;min-height:32px;padding:0 0 5px;border-bottom:1px solid #323a38}.placement-workspace>section>header strong{display:block;color:#fff;font-size:11px}.placement-workspace>section>header small{display:block;margin-top:2px;color:#7f8884;font-size:7px;line-height:1.35}.placement-destination.top{border-color:#3b9da5}.placement-destination.bottom{border-color:#9c3f46}.placement-row{min-height:124px;align-items:center;gap:4px;padding:5px 1px}.placement-row>p{margin:auto;color:#626b68;font-size:8px;line-height:1.5;text-align:center}.placement-buttons{display:grid;grid-template-columns:1fr 1fr;gap:5px}.placement-buttons button{padding:5px 3px;border:1px solid #dcd8cc;background:#1a2020;color:#fff;font-size:8px;font-weight:900}.placement-buttons button:first-child{border-color:#5cbac1}.placement-buttons button:last-child{border-color:#ba555c}.placement-buttons button:disabled{opacity:.38}
.all-placement-workspace{display:grid;grid-template-columns:62px minmax(0,1fr) 62px;align-items:center;gap:8px;margin:10px 3px;padding:10px;border:1px solid rgba(238,238,228,.28);background:#090d0e}.all-placement-row{min-width:0;padding:5px}.placement-edge{color:#fff;font-size:18px;font-weight:900;letter-spacing:.28em;text-align:center;writing-mode:vertical-rl}.top-edge{color:#70d7df}.bottom-edge{color:#d76069}
.prompt-card-detail{display:grid;min-height:112px;grid-template-columns:130px 1fr;gap:14px;margin:0 3px 10px;padding:10px;border:1px solid rgba(238,238,228,.32);background:#090d0e}.prompt-card-detail>.l12-card-image{width:130px;height:108px;background:#050708}.prompt-card-detail small{color:#70d7df;font-size:9px;letter-spacing:.12em}.prompt-card-detail h3{margin:3px 0 5px;color:#fff;font-size:16px}.prompt-card-detail dl{display:flex;gap:12px;margin:0}.prompt-card-detail dl div{display:flex;gap:4px}.prompt-card-detail dt{color:#777f7c;font-size:9px}.prompt-card-detail dd{margin:0;color:#fff;font-size:10px;font-weight:900}.prompt-card-detail p{max-height:48px;margin:5px 0 0;overflow:auto;color:#c9cdc7;font-size:10px;font-weight:800;line-height:1.55;white-space:pre-wrap}
.prompt-panel footer button.primary{border:2px solid #fff!important;background:#f2eee3!important;color:#090c0d!important}.prompt-panel footer button.primary:disabled{border-color:#646966!important;background:#2a2e2d!important;color:#929792!important}.l12-prompt-overlay.waiting{background:rgba(2,4,5,.3)!important;backdrop-filter:blur(2px)}.waiting-panel{position:relative;width:min(430px,calc(100vw - 32px));padding:25px;text-align:center}.waiting-panel>.prompt-minimize{right:10px;top:10px}.waiting-panel small{color:#73d7de;font-size:9px;letter-spacing:.16em}.waiting-panel h2{margin:10px 0;color:#fff;font-size:21px}.waiting-panel p{color:#c0c5bf;font-size:11px}.waiting-panel i{display:inline-block;width:7px;height:7px;margin:10px 4px 0;border-radius:50%;background:#70d7df;animation:waiting-pulse 1.2s infinite}.waiting-panel i:nth-of-type(2){animation-delay:.2s}.waiting-panel i:nth-of-type(3){animation-delay:.4s}@keyframes waiting-pulse{0%,70%,100%{opacity:.25;transform:translateY(0)}35%{opacity:1;transform:translateY(-4px)}}
.l12-prompt-overlay.minimized{z-index:2147483600;inset:auto 16px 66px auto;width:auto;height:auto;padding:0;background:transparent!important;backdrop-filter:none;pointer-events:none}.prompt-minimized-bar{display:block;pointer-events:auto}.prompt-minimized-bar button{padding:7px 12px;border:1px solid #70d7df;background:#174e54;color:#fff;font-weight:900;box-shadow:0 12px 35px #000}
@media(max-width:760px){.l12-prompt-overlay.minimized{right:10px;bottom:60px}}
@media(max-width:520px){.l12-prompt-overlay.inspector-active:not(.minimized){--inspector-safe-lane:92px;padding:8px 8px 8px var(--inspector-safe-lane)}.l12-prompt-overlay.inspector-active:not(.minimized) .prompt-panel{max-width:calc(100vw - var(--inspector-safe-lane) - 8px);padding:10px}}
/* 天灾卡是横版；所有天灾准备、公开、触发与详情场景共用同一比例。 */
.prompt-card-detail.disaster{grid-template-columns:minmax(220px,320px) 1fr}
.prompt-card-detail.disaster>.l12-card-image{width:100%;height:auto;aspect-ratio:8/5}
@media(max-width:700px){.placement-workspace{grid-template-columns:1fr;max-height:430px;overflow:auto}.all-placement-workspace{grid-template-columns:44px minmax(0,1fr) 44px}.placement-edge{font-size:14px}}
.l12-prompt-overlay.preparation .prompt-panel{width:min(1100px,calc(100vw - 36px))}
.disaster-preparation-history{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin:10px 0 12px;padding:9px;border:1px solid #3c4646;background:#080c0d;text-align:left}.disaster-preparation-history>section{min-width:0;border:1px solid #394240;background:#101516}.disaster-preparation-history>section>header{display:flex;align-items:center;justify-content:space-between;padding:6px 8px;border-bottom:1px solid #333c3a}.disaster-preparation-history>section>header b{font-size:10px}.disaster-preparation-history>section>header span{display:grid;width:19px;height:19px;place-items:center;background:#232a28;color:#fff;font:900 9px monospace}.disaster-preparation-history>section>div{display:flex;min-height:78px;align-items:center;gap:5px;overflow-x:auto;padding:6px}.disaster-preparation-history button{display:flex;width:112px;min-width:112px;flex-direction:column;gap:3px;padding:3px;border:2px solid #68706d;background:#080b0c;color:#fff}.disaster-preparation-history img,.disaster-preparation-history .l12-card-image{width:102px;height:auto;aspect-ratio:8/5}.disaster-preparation-history button span{overflow:hidden;font-size:8px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}.disaster-preparation-history p{margin:auto;color:#626b68;font-size:8px}.disaster-preparation-history .banned button{border-color:#a83c46;box-shadow:0 0 8px rgba(168,60,70,.22)}.disaster-preparation-history .banned>header b{color:#ef7780}.disaster-preparation-history .revealed button{border-color:#d1b76c;box-shadow:0 0 8px rgba(209,183,108,.2)}.disaster-preparation-history .revealed>header b{color:#e8cf83}.disaster-preparation-history .chosen button{border-color:#3f9d73;box-shadow:0 0 8px rgba(63,157,115,.24)}.disaster-preparation-history .chosen>header b{color:#79d2a7}.l12-prompt-overlay.preparation .waiting-panel{width:min(1100px,calc(100vw - 36px));padding:16px 20px 22px}.l12-prompt-overlay.preparation .waiting-panel>small{display:block;margin-top:12px}
.disaster-preparation-history{grid-template-columns:minmax(260px,.8fr) minmax(420px,1.2fr)}.disaster-preparation-history button small{display:block;width:100%;overflow:hidden;color:#9ca6a1;font-size:7px;font-weight:900;text-align:center;text-overflow:ellipsis;white-space:nowrap}.disaster-preparation-history .chosen button small{color:#78d1a6}
@media(max-width:700px){.disaster-preparation-history{grid-template-columns:1fr;max-height:260px;overflow:auto}.disaster-preparation-history>section>div{min-height:68px}.disaster-preparation-history button{width:96px;min-width:96px}.disaster-preparation-history img,.disaster-preparation-history .l12-card-image{width:86px}}
.initiative-race img{width:58px;height:58px;object-fit:cover;border:2px solid #666;border-radius:2px}
.l12-prompt-overlay,.l12-prompt-overlay.minimized{z-index:3000!important}
</style>
