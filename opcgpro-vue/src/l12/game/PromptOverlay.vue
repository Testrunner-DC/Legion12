<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { Card, DisasterCardView, GameState } from '../types'
import { isHorizontalCardType } from '../cardPresentation'
import { gameAction, l12State, sandboxAction } from '../net'
import { masterProfileUrl } from '../specialAssets'

const props = withDefaults(defineProps<{
  game: GameState
  mulliganSelectedIds?: string[]
  busy?: boolean
  suppressedPromptId?: string | null
  suppressDefenseWait?: boolean
  readOnly?: boolean
}>(), { mulliganSelectedIds: () => [], busy: false, suppressDefenseWait: false, readOnly: false })
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

const choiceLabels: Record<string, string> = {
  first: '选择先攻', second: '选择后攻', yes: '是', no: '否', agree: '同意', refuse: '不同意',
  pass: '不响应', skip: '不发动', top: '牌库顶部', bottom: '牌库底部', recruit: '活跃登场',
  confirm: '确认信息',
  'free-tactic': '主动战术无需消耗费用', 'back-master': '后排远程军团可进攻主宰',
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
function label(id: string) {
  const base = choiceLabels[id] ?? prompt.value?.data?.[id] ?? cardFor(id)?.name ?? id.replace(':', ' 排第 ')
  const zone = prompt.value?.data?.[`${id}:zone`]
  return zone ? `${base} · ${zone}` : base
}
function imageFor(id: string) { return prompt.value?.data?.[`${id}:image`] ?? cardFor(id)?.imageUrl }
function numberData(id: string, key: string) {
  const value = prompt.value?.data?.[`${id}:${key}`]
  return value === undefined || value === '' ? undefined : Number(value)
}
function detailFor(id: string | null) {
  if (!id) return null
  const card = cardFor(id)
  const imageUrl = imageFor(id)
  if (!card && !imageUrl) return null
  return {
    id,
    name: label(id),
    imageUrl,
    effectText: card?.effectText ?? prompt.value?.data?.[`${id}:effect`] ?? '',
    cardType: card?.cardType ?? prompt.value?.data?.[`${id}:cardType`] ?? '',
    traits: card?.traits ?? prompt.value?.data?.[`${id}:traits`]?.split('|').filter(Boolean) ?? [],
    profession: (card?.profession ?? prompt.value?.data?.[`${id}:profession`]) || undefined,
    cost: card?.playCost ?? card?.currentCost ?? card?.cost ?? numberData(id, 'cost'),
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
    cardId: prompt.value?.data?.[`${id}:cardId`] ?? '',
    name: detail.name,
    cardType: detail.cardType,
    faction: prompt.value?.data?.[`${id}:faction`] ?? '',
    traits: detail.traits,
    profession: detail.profession,
    imageUrl: detail.imageUrl,
    effectText: detail.effectText,
    cost: detail.cost ?? 0,
    baseTroops: detail.baseTroops ?? detail.troops ?? 0,
    troops: detail.troops ?? detail.baseTroops ?? 0,
    disasterLevel: detail.disasterLevel ?? 0,
    tapped: false,
    summonRound: 0,
  }
}
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
const displayedChoices = computed(() => {
  const listed = prompt.value?.data?.displayCardIds?.split('|').filter(Boolean)
  if (listed?.length) return listed
  if (previewCardId.value && !currentChoices.value.length) return [previewCardId.value]
  return currentChoices.value
})
const displayedCardsAreChoices = computed(() => displayedChoices.value.some(id => prompt.value?.validChoices.includes(id)))
const placementMode = computed(() => prompt.value?.data?.placementMode ?? '')
const currentSelected = computed(() => isMulligan.value ? props.mulliganSelectedIds : ['split-top-bottom', 'all-top-bottom', 'all-bottom'].includes(placementMode.value) ? (placementSelected.value ? [placementSelected.value] : []) : selected.value)
const previewCardId = computed(() => prompt.value?.data?.previewCardId ?? null)
const hasCardChoices = computed(() => Boolean(previewCardId.value) || displayedChoices.value.some(id => Boolean(detailFor(id))))
const isEffectOptionList = computed(() => prompt.value?.kind === 'option' && !hasCardChoices.value && !isInitiative.value)
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
  if (prompt.value) return prompt.value.text
  if (isMulligan.value) return '选择需要调度的起始手牌'
  if (isMulliganPhase.value) return '等待对手完成调度'
  return waitingText()
})

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
      :class="{ preparation: isPreparation, initiative: isInitiative, 'disaster-choice': isDisasterChoice, 'information-confirm': isInfoConfirm, waiting: waitingPrompt || (isMulliganPhase && !isMulligan), minimized }">
      <section v-if="minimized" class="prompt-minimized-bar" role="status">
        <strong>{{ overlayTitle }}</strong>
        <button @click="minimized = false">展开</button>
      </section>

      <section v-else-if="prompt" class="prompt-panel" :class="{ 'has-card-choices': hasCardChoices, 'single-card-row': isSingleCardRow }" role="dialog" aria-modal="true" :aria-label="prompt.text">
        <header>
          <small>{{ kindLabel() }}</small><h2>{{ prompt.text }}</h2>
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
              <img :src="entry.card.imageUrl || '/assets/l12/card-back-disaster.png'" :alt="entry.card.name || '未揭示天灾'"/><span>{{ entry.card.name || '未揭示天灾' }}</span><small>{{ entry.note }}</small>
            </button><p v-if="!group.entries.length">等待本阶段结果</p></div>
          </section>
        </div>
        <button v-if="previewCardId && !displayedChoices.includes(previewCardId)" class="prompt-featured-card"
          :class="{ disaster: isHorizontalCardType(detailFor(previewCardId)?.cardType) }"
          @mouseenter="focusChoice(previewCardId)" @focus="focusChoice(previewCardId)" @click="focusChoice(previewCardId)">
          <img v-if="imageFor(previewCardId)" :src="imageFor(previewCardId)" :alt="label(previewCardId)" /><span>{{ label(previewCardId) }}</span>
        </button>
        <div v-if="placementMode === 'all-top-bottom' || placementMode === 'all-bottom'" class="all-placement-workspace">
          <strong class="placement-edge top-edge">{{ placementMode === 'all-bottom' ? '先放到底' : '靠顶' }}</strong>
          <div class="all-placement-row">
            <button v-for="choice in placementOrder" :key="choice" class="placement-mini-card"
              :class="{ selected: placementSelected === choice }"
              @click="selectSwapChoice(choice)" @mouseenter="focusChoice(choice)" @focus="focusChoice(choice)">
              <img v-if="imageFor(choice)" :src="imageFor(choice)" :alt="label(choice)" /><span>{{ label(choice) }}</span>
            </button>
          </div>
          <strong class="placement-edge bottom-edge">{{ placementMode === 'all-bottom' ? '后放到底' : '靠底' }}</strong>
        </div>
        <div v-else-if="placementMode === 'split-top-bottom'" class="placement-workspace">
          <section class="placement-destination top" @dragover.prevent @drop="dropPlacement('top')">
            <header><strong>靠顶</strong><small>从左到右，最左侧最靠牌库顶</small></header>
            <div class="placement-row">
              <button v-for="choice in placementTop" :key="choice" class="placement-mini-card"
                :class="{ selected: placementSelected === choice }" draggable="true"
                @dragstart="draggedChoice = choice" @dragover.prevent @drop.stop.prevent="dropPlacement('top', choice)"
                @mouseenter="focusChoice(choice)" @click="selectSplitSwap(choice)">
                <img v-if="imageFor(choice)" :src="imageFor(choice)" :alt="label(choice)" /><span>{{ label(choice) }}</span>
                <i @click.stop="returnToUnassigned(choice)">撤回</i>
              </button>
              <p v-if="!placementTop.length">拖到这里，或选中后点击“放回顶部”</p>
            </div>
          </section>

          <section class="placement-candidates">
            <header><strong>待安排</strong><small>拖动卡牌决定归属和顺序</small></header>
            <div class="placement-row">
              <button v-for="choice in unassignedChoices" :key="choice" class="placement-mini-card"
                :class="{ selected: placementSelected === choice }" draggable="true"
                @dragstart="draggedChoice = choice" @mouseenter="focusChoice(choice)" @click="placementSelected = choice; focusChoice(choice)">
                <img v-if="imageFor(choice)" :src="imageFor(choice)" :alt="label(choice)" /><span>{{ label(choice) }}</span>
              </button>
              <p v-if="!unassignedChoices.length">全部卡牌均已安排</p>
            </div>
            <div class="placement-buttons">
              <button :disabled="!placementSelected" @click="assignPlacement('top')">放回顶部</button>
              <button :disabled="!placementSelected" @click="assignPlacement('bottom')">放回底部</button>
            </div>
          </section>

          <section class="placement-destination bottom" @dragover.prevent @drop="dropPlacement('bottom')">
            <header><strong>靠底</strong><small>从左到右，最左侧最先到达牌库底</small></header>
            <div class="placement-row">
              <button v-for="choice in placementBottom" :key="choice" class="placement-mini-card"
                :class="{ selected: placementSelected === choice }" draggable="true"
                @dragstart="draggedChoice = choice" @dragover.prevent @drop.stop.prevent="dropPlacement('bottom', choice)"
                @mouseenter="focusChoice(choice)" @click="selectSplitSwap(choice)">
                <img v-if="imageFor(choice)" :src="imageFor(choice)" :alt="label(choice)" /><span>{{ label(choice) }}</span>
                <i @click.stop="returnToUnassigned(choice)">撤回</i>
              </button>
              <p v-if="!placementBottom.length">拖到这里，或选中后点击“放回底部”</p>
            </div>
          </section>
        </div>
        <div v-else class="prompt-choices" :class="{ 'card-grid': hasCardChoices, 'effect-option-list': isEffectOptionList }">
          <button v-for="choice in displayedChoices" :key="choice"
            :class="{ selected: selected.includes(choice), unavailable: displayedCardsAreChoices && !prompt.validChoices.includes(choice), 'card-choice': detailFor(choice), 'horizontal-card': isHorizontalCardType(detailFor(choice)?.cardType) }"
            @mouseenter="focusChoice(choice)" @focus="focusChoice(choice)" @click="focusChoice(choice); toggle(choice)">
            <img v-if="imageFor(choice)" :src="imageFor(choice)" :alt="label(choice)" />
            <span>{{ label(choice) }}</span>
            <b v-if="selected.includes(choice) && prompt.maxChoose > 1">{{ selected.indexOf(choice) + 1 }}</b>
          </button>
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
            <span>{{ isInfoConfirm ? '双方均确认后继续' : `选择 ${prompt.minChoose}–${prompt.maxChoose} 项` }}</span>
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
        <div class="prompt-choices card-grid">
          <button v-for="card in me.hand" :key="card.instanceId" class="card-choice"
            :class="{ selected: mulliganSelectedIds.includes(card.instanceId) }"
            @mouseenter="focusChoice(card.instanceId)" @focus="focusChoice(card.instanceId)" @click="focusChoice(card.instanceId); emit('mulliganToggle', card.instanceId)">
            <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.name" />
            <span>{{ card.name }}</span>
          </button>
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
              <img :src="entry.card.imageUrl || '/assets/l12/card-back-disaster.png'" :alt="entry.card.name || '未揭示天灾'"/><span>{{ entry.card.name || '未揭示天灾' }}</span><small>{{ entry.note }}</small>
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
.l12-prompt-overlay{position:fixed!important;z-index:1000!important;inset:0;box-sizing:border-box;display:flex!important;width:100vw;height:100vh;align-items:center!important;justify-content:center!important;padding:18px;background:rgba(2,4,5,.48)!important;backdrop-filter:blur(3px)}
.prompt-panel{position:relative;width:min(760px,calc(100vw - 36px));max-height:calc(100vh - 36px);margin:auto;padding:16px;overflow:hidden}
.prompt-panel header{position:relative;padding-right:44px}.prompt-minimize{position:absolute;right:0;top:0;width:32px;height:27px;border:1px solid #8b918d;background:#111718;color:#fff;font-size:18px;line-height:18px}.prompt-minimize:hover{border-color:#70d7df;background:#174e54}
.l12-prompt-overlay.initiative .prompt-panel{width:min(480px,calc(100vw - 32px));padding:24px}.l12-prompt-overlay.initiative .prompt-choices{display:grid;grid-template-columns:1fr 1fr;min-height:112px;align-items:stretch}.l12-prompt-overlay.initiative .prompt-choices>button{width:100%;max-width:none;min-height:92px;border:2px solid #eeeadf;background:#121718;color:#fff;font-size:18px}.l12-prompt-overlay.initiative .prompt-choices>button:hover,.l12-prompt-overlay.initiative .prompt-choices>button.selected{border-color:#7de1e7;background:#1b6f77;color:#fff}
.prompt-panel.has-card-choices{width:min(820px,calc(100vw - 36px))}.prompt-choices.card-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;min-height:0;max-height:292px;padding:10px 3px;overflow:auto}.prompt-choices.card-grid>button{display:flex;width:100%;min-width:0;max-width:none;min-height:0;flex-direction:column;align-items:center;justify-content:center;padding:5px;border:2px solid #d9d8cf;background:#101516;color:#fff}.prompt-choices.card-grid>button:hover,.prompt-choices.card-grid>button.selected{border-color:#70d7df;background:#174e54;color:#fff}.prompt-choices.card-grid img{width:72px;max-width:100%;height:100px;margin:0 auto 5px;object-fit:contain}.prompt-choices.card-grid span{display:block;width:100%;overflow:hidden;color:#fff;font-size:11px;line-height:17px;text-align:center;text-overflow:ellipsis;white-space:nowrap}
.prompt-choices.effect-option-list{display:flex;max-width:640px;flex-direction:column;align-items:stretch;gap:8px;margin:12px auto}.prompt-choices.effect-option-list>button{width:100%;max-width:none;min-height:54px;padding:10px 16px;border:2px solid #d9d8cf;background:#101516;color:#fff;font-size:11px;font-weight:900;line-height:1.55;text-align:left;white-space:normal}.prompt-choices.effect-option-list>button:hover,.prompt-choices.effect-option-list>button.selected{border-color:#70d7df;background:#174e54;color:#fff}
.prompt-choices.card-grid>button.horizontal-card{grid-column:span 2}.prompt-choices.card-grid>button.horizontal-card img{width:180px;height:112px}.prompt-panel.single-card-row{width:min(900px,calc(100vw - 36px))}.prompt-panel.single-card-row .prompt-choices.card-grid{display:flex;max-height:none;flex-wrap:nowrap;gap:6px;overflow-x:auto;overflow-y:hidden}.prompt-panel.single-card-row .prompt-choices.card-grid>button{flex:0 0 112px;width:112px;padding:3px}.prompt-panel.single-card-row .prompt-choices.card-grid img{width:92px;height:128px}
.prompt-featured-card{display:flex;width:150px;min-height:0;flex-direction:column;align-items:center;gap:5px;margin:10px auto 2px;padding:5px;border:2px solid #ded9cc;background:#0a0e0f;color:#fff}.prompt-featured-card img{width:126px;height:176px;object-fit:contain}.prompt-featured-card.disaster{width:230px}.prompt-featured-card.disaster img{width:210px;height:132px}.prompt-featured-card span{font-size:11px;font-weight:900}
.l12-prompt-overlay.information-confirm .prompt-panel{width:min(850px,calc(100vw - 36px));overflow-y:auto}.l12-prompt-overlay.information-confirm .prompt-choices.card-grid{display:flex;justify-content:center;max-height:none}.l12-prompt-overlay.information-confirm .prompt-choices.card-grid>button{flex:0 0 min(616px,calc(100vw - 100px));width:min(616px,calc(100vw - 100px));max-width:616px}.l12-prompt-overlay.information-confirm .prompt-choices.card-grid>button img{width:min(588px,calc(100vw - 140px));height:min(368px,48vh)}.l12-prompt-overlay.information-confirm .prompt-featured-card{width:min(630px,calc(100vw - 90px));margin-inline:auto}.l12-prompt-overlay.information-confirm .prompt-featured-card img{width:min(602px,calc(100vw - 130px));height:min(376px,48vh)}.l12-prompt-overlay.information-confirm .prompt-featured-card span{font-size:14px}
.prompt-choices.card-grid>button.unavailable{border-color:#3f4442;filter:brightness(.42);cursor:not-allowed}.prompt-choices.card-grid>button.unavailable:hover{background:#101516;box-shadow:none}
.mulligan-panel{width:min(900px,calc(100vw - 36px))!important}.mulligan-panel .prompt-choices.card-grid{display:flex;max-height:none;flex-wrap:nowrap;gap:6px;overflow-x:auto}.mulligan-panel .prompt-choices.card-grid>button{flex:1 0 142px;max-width:168px;padding:3px}.mulligan-panel .prompt-choices.card-grid img{width:84px;height:117px}
.l12-prompt-overlay.disaster-choice .prompt-panel{width:min(820px,calc(100vw - 36px))}.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid{display:flex;max-height:none;justify-content:flex-start;overflow-x:auto;overflow-y:hidden;padding-bottom:8px;scrollbar-color:#65706d #111516;scrollbar-width:thin}.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid>button{flex:0 0 128px}.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid img{width:106px;height:74px}.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid span{font-size:11px;line-height:18px}
.placement-workspace{display:grid;grid-template-columns:1fr 1.1fr 1fr;gap:8px;min-height:166px;margin:9px 3px;padding:8px;border:1px solid rgba(238,238,228,.28);background:#090d0e}.placement-workspace>section{min-width:0;padding:7px;border:1px solid #39413f;background:#101516}.placement-workspace>section>header{display:block;min-height:32px;padding:0 0 5px;border-bottom:1px solid #323a38}.placement-workspace>section>header strong{display:block;color:#fff;font-size:11px}.placement-workspace>section>header small{display:block;margin-top:2px;color:#7f8884;font-size:7px;line-height:1.35}.placement-destination.top{border-color:#3b9da5}.placement-destination.bottom{border-color:#9c3f46}.placement-row{display:flex;min-height:92px;align-items:center;gap:4px;overflow-x:auto;padding:5px 1px}.placement-row>p{margin:auto;color:#626b68;font-size:8px;line-height:1.5;text-align:center}.placement-mini-card{position:relative;display:flex!important;width:58px!important;min-width:58px!important;height:82px!important;min-height:82px!important;flex:0 0 58px!important;flex-direction:column;align-items:center;padding:2px!important;border:1px solid #6c7470!important;background:#080b0c!important;color:#fff!important}.placement-mini-card.selected{border-color:#70d7df!important;box-shadow:0 0 8px rgba(74,193,202,.46)}.placement-mini-card img{width:52px!important;height:66px!important;margin:0!important;object-fit:contain}.placement-mini-card span{display:block;width:100%;overflow:hidden;font-size:7px;line-height:11px;text-overflow:ellipsis;white-space:nowrap}.placement-mini-card i{position:absolute;right:1px;top:1px;padding:1px 2px;background:#8c2931;color:#fff;font-size:6px;font-style:normal}.placement-buttons{display:grid;grid-template-columns:1fr 1fr;gap:5px}.placement-buttons button{padding:5px 3px;border:1px solid #dcd8cc;background:#1a2020;color:#fff;font-size:8px;font-weight:900}.placement-buttons button:first-child{border-color:#5cbac1}.placement-buttons button:last-child{border-color:#ba555c}.placement-buttons button:disabled{opacity:.38}
.all-placement-workspace{display:grid;grid-template-columns:62px 1fr 62px;align-items:center;gap:8px;margin:10px 3px;padding:10px;border:1px solid rgba(238,238,228,.28);background:#090d0e}.all-placement-row{display:flex;min-width:0;justify-content:center;gap:7px;overflow-x:auto;padding:5px}.all-placement-row .placement-mini-card{width:84px!important;min-width:84px!important;height:118px!important;flex-basis:84px!important}.all-placement-row .placement-mini-card img{width:78px!important;height:99px!important}.placement-edge{color:#fff;font-size:18px;font-weight:900;letter-spacing:.28em;text-align:center;writing-mode:vertical-rl}.top-edge{color:#70d7df}.bottom-edge{color:#d76069}
.prompt-card-detail{display:grid;min-height:112px;grid-template-columns:130px 1fr;gap:14px;margin:0 3px 10px;padding:10px;border:1px solid rgba(238,238,228,.32);background:#090d0e}.prompt-card-detail>img{width:130px;height:108px;object-fit:contain;background:#050708}.prompt-card-detail small{color:#70d7df;font-size:9px;letter-spacing:.12em}.prompt-card-detail h3{margin:3px 0 5px;color:#fff;font-size:16px}.prompt-card-detail dl{display:flex;gap:12px;margin:0}.prompt-card-detail dl div{display:flex;gap:4px}.prompt-card-detail dt{color:#777f7c;font-size:9px}.prompt-card-detail dd{margin:0;color:#fff;font-size:10px;font-weight:900}.prompt-card-detail p{max-height:48px;margin:5px 0 0;overflow:auto;color:#c9cdc7;font-size:10px;font-weight:800;line-height:1.55;white-space:pre-wrap}
.prompt-panel footer button.primary{border:2px solid #fff!important;background:#f2eee3!important;color:#090c0d!important}.prompt-panel footer button.primary:disabled{border-color:#646966!important;background:#2a2e2d!important;color:#929792!important}.l12-prompt-overlay.waiting{background:rgba(2,4,5,.3)!important;backdrop-filter:blur(2px)}.waiting-panel{position:relative;width:min(430px,calc(100vw - 32px));padding:25px;text-align:center}.waiting-panel>.prompt-minimize{right:10px;top:10px}.waiting-panel small{color:#73d7de;font-size:9px;letter-spacing:.16em}.waiting-panel h2{margin:10px 0;color:#fff;font-size:21px}.waiting-panel p{color:#c0c5bf;font-size:11px}.waiting-panel i{display:inline-block;width:7px;height:7px;margin:10px 4px 0;border-radius:50%;background:#70d7df;animation:waiting-pulse 1.2s infinite}.waiting-panel i:nth-of-type(2){animation-delay:.2s}.waiting-panel i:nth-of-type(3){animation-delay:.4s}@keyframes waiting-pulse{0%,70%,100%{opacity:.25;transform:translateY(0)}35%{opacity:1;transform:translateY(-4px)}}
.l12-prompt-overlay.minimized{inset:auto 16px 16px auto;width:auto;height:auto;padding:0;background:transparent!important;backdrop-filter:none;pointer-events:none}.prompt-minimized-bar{display:flex;max-width:430px;align-items:center;gap:14px;padding:10px 12px;border:1px solid #e4e0d5;background:#0c1112;box-shadow:0 12px 35px #000;pointer-events:auto}.prompt-minimized-bar strong{overflow:hidden;color:#fff;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.prompt-minimized-bar button{flex:none;padding:7px 12px;border:1px solid #70d7df;background:#174e54;color:#fff;font-weight:900}
/* 天灾卡是横版；所有天灾准备、公开、触发与详情场景共用同一比例。 */
.prompt-choices.card-grid>button.horizontal-card{grid-column:span 2;min-width:0}
.prompt-choices.card-grid>button.horizontal-card img{width:min(220px,100%);height:auto;aspect-ratio:8/5;object-fit:contain}
.prompt-featured-card.disaster{width:min(420px,calc(100vw - 90px))}
.prompt-featured-card.disaster img{width:100%;height:auto;aspect-ratio:8/5;object-fit:contain}
.l12-prompt-overlay.information-confirm .prompt-featured-card.disaster{width:min(630px,calc(100vw - 90px))}
.l12-prompt-overlay.information-confirm .prompt-featured-card.disaster img{width:100%;height:auto;max-height:48vh;aspect-ratio:8/5;object-fit:contain}
.l12-prompt-overlay.disaster-choice .prompt-panel{width:min(980px,calc(100vw - 36px))}
.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid>button{flex:0 0 154px;width:154px}
.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid img{width:143px;height:auto;aspect-ratio:8/5;object-fit:contain}
.prompt-card-detail.disaster{grid-template-columns:minmax(220px,320px) 1fr}
.prompt-card-detail.disaster>img{width:100%;height:auto;aspect-ratio:8/5;object-fit:contain}
@media(max-width:700px){.prompt-choices.card-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.mulligan-panel .prompt-choices.card-grid,.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid{display:flex}.l12-prompt-overlay.disaster-choice .prompt-choices.card-grid>button{flex-basis:min(220px,72vw);width:min(220px,72vw)}.placement-workspace{grid-template-columns:1fr;max-height:310px;overflow:auto}.all-placement-workspace{grid-template-columns:44px 1fr 44px}.placement-edge{font-size:14px}}
.l12-prompt-overlay.preparation .prompt-panel{width:min(1100px,calc(100vw - 36px))}
.disaster-preparation-history{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin:10px 0 12px;padding:9px;border:1px solid #3c4646;background:#080c0d;text-align:left}.disaster-preparation-history>section{min-width:0;border:1px solid #394240;background:#101516}.disaster-preparation-history>section>header{display:flex;align-items:center;justify-content:space-between;padding:6px 8px;border-bottom:1px solid #333c3a}.disaster-preparation-history>section>header b{font-size:10px}.disaster-preparation-history>section>header span{display:grid;width:19px;height:19px;place-items:center;background:#232a28;color:#fff;font:900 9px monospace}.disaster-preparation-history>section>div{display:flex;min-height:78px;align-items:center;gap:5px;overflow-x:auto;padding:6px}.disaster-preparation-history button{display:flex;width:112px;min-width:112px;flex-direction:column;gap:3px;padding:3px;border:2px solid #68706d;background:#080b0c;color:#fff}.disaster-preparation-history img{width:102px;height:auto;aspect-ratio:8/5;object-fit:contain}.disaster-preparation-history button span{overflow:hidden;font-size:8px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}.disaster-preparation-history p{margin:auto;color:#626b68;font-size:8px}.disaster-preparation-history .banned button{border-color:#a83c46;box-shadow:0 0 8px rgba(168,60,70,.22)}.disaster-preparation-history .banned>header b{color:#ef7780}.disaster-preparation-history .revealed button{border-color:#d1b76c;box-shadow:0 0 8px rgba(209,183,108,.2)}.disaster-preparation-history .revealed>header b{color:#e8cf83}.disaster-preparation-history .chosen button{border-color:#3f9d73;box-shadow:0 0 8px rgba(63,157,115,.24)}.disaster-preparation-history .chosen>header b{color:#79d2a7}.l12-prompt-overlay.preparation .waiting-panel{width:min(1100px,calc(100vw - 36px));padding:16px 20px 22px}.l12-prompt-overlay.preparation .waiting-panel>small{display:block;margin-top:12px}
.disaster-preparation-history{grid-template-columns:minmax(260px,.8fr) minmax(420px,1.2fr)}.disaster-preparation-history button small{display:block;width:100%;overflow:hidden;color:#9ca6a1;font-size:7px;font-weight:900;text-align:center;text-overflow:ellipsis;white-space:nowrap}.disaster-preparation-history .chosen button small{color:#78d1a6}
@media(max-width:700px){.disaster-preparation-history{grid-template-columns:1fr;max-height:260px;overflow:auto}.disaster-preparation-history>section>div{min-height:68px}.disaster-preparation-history button{width:96px;min-width:96px}.disaster-preparation-history img{width:86px}}
.initiative-race img{width:58px;height:58px;object-fit:cover;border:2px solid #666;border-radius:2px}
</style>
