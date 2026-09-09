<script setup lang="ts">
defineOptions({ inheritAttrs: false })
import { computed, ref, watch } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card, PlayerView } from '../types'
import { blackLotusLogoUrl, factionLogoUrls, godPowerLogoUrl, roundCardUrl } from '../specialAssets'
import CardImage from '../CardImage.vue'

const props = defineProps<{
  player: PlayerView
  side: 'my' | 'opponent'
  controllable?: boolean
  viewerPlayerIndex?: number
  selectedId?: string | null
  selectedIds?: string[]
  attackMode?: boolean
  moveMode?: boolean
  freeMoveMode?: boolean
  cavalryMoveMode?: boolean
  placementMode?: boolean
  placementRow?: number | null
  placementCanReplaceCounter?: boolean
  actionsEnabled?: boolean
  round?: number
  turnSerial?: number
  active?: boolean
  targetableIds?: string[]
  selectedTargetIds?: string[]
  responseTargetIds?: string[]
  masterTargetable?: boolean
  attackableIds?: string[]
  responsePlayableIds?: string[]
  selectionMode?: boolean
  promptSlotIds?: string[]
  combatAttackerId?: string | null
  combatTargetId?: string | null
  combatTargetMaster?: boolean
  paymentChoiceIds?: string[]
  paymentSelectedIds?: string[]
  hiddenRevealCard?: Card | null
  interactionPromptActive?: boolean
  canActivateOsiris?: boolean
  osirisVictoryDisabledReason?: string
}>()
const emit = defineEmits<{
  slot: [row: number, slot: number, card: Card | null]
  master: []
  focus: [card: Card]
  graveyard: [playerIndex: number]
  cardAction: [action: 'attack' | 'move' | 'freeMove' | 'cavalryMove', card: Card]
  ability: [card: Card, ability: string]
  factionAbility: [ability: string]
  selectCard: [card: Card]
  paymentResource: [instanceId: string]
}>()
const isSelected = (instanceId?: string) => Boolean(instanceId
  && (props.selectedId === instanceId || props.selectedIds?.includes(instanceId)))

const factionOpen = ref(false)
const factionMinimized = ref(false)
const abilityCardOpen = ref<Card | null>(null)
const abilityCardMinimized = ref(false)
watch(() => props.player.specialZones?.trials, trials => {
  const opened = abilityCardOpen.value
  if (opened?.cardType !== 'trial') return
  abilityCardOpen.value = trials?.find(card => card.instanceId === opened.instanceId) ?? null
})
watch(() => props.interactionPromptActive, active => {
  if (!active) return
  factionOpen.value = false
  factionMinimized.value = false
  abilityCardOpen.value = null
  abilityCardMinimized.value = false
})
const currentMoraleLimit = computed(() => props.player.morale.length)
const topGraveyard = computed(() => props.player.graveyard?.at(-1) ?? null)
type MoraleResource = PlayerView['morale'][number]
const visibleMoraleLimit = 12
const visibleTemporaryMoraleCount = computed(() => Math.min(visibleMoraleLimit, Math.max(0, Math.floor(props.player.temporaryMorale ?? 0))))
const displayMoraleSlots = computed<Array<MoraleResource>>(() => {
  const olympus = props.player.faction === 'olympus'
  const resources = props.player.morale.map((resource, originalIndex) => ({ resource, originalIndex }))
  resources.sort((left, right) => {
    const payable = (resource: MoraleResource) => props.paymentChoiceIds?.includes(resource.instanceId) ? 1 : 0
    const rank = ({ isGodPower, tapped }: MoraleResource) => olympus
      ? (isGodPower ? (tapped ? 2 : 0) : (tapped ? 3 : 1))
      : (tapped ? 1 : 0)
    return payable(right.resource) - payable(left.resource)
      || rank(left.resource) - rank(right.resource)
      || left.originalIndex - right.originalIndex
  })
  return resources.map(({ resource }) => resource).slice(0, Math.max(0, visibleMoraleLimit - visibleTemporaryMoraleCount.value))
})
const visibleMoraleCount = computed(() => visibleTemporaryMoraleCount.value + displayMoraleSlots.value.length)
function moraleState(card: MoraleResource) {
  if (card.isGodPower) return card.tapped ? 'rested-god-power' : 'active-god-power'
  return card.tapped ? 'rested-morale' : 'active-morale'
}
function moraleLocked(card: MoraleResource) {
  const lockedUntilRound = card.cannotUntapUntilRound ?? 0
  return lockedUntilRound > 0 && lockedUntilRound >= (props.round ?? 0)
}
function moraleLabel(card: MoraleResource) {
  const state = moraleState(card)
  const labels: Record<string, string> = {
    'active-morale': '活跃士气',
    'rested-morale': '休整士气',
    'active-god-power': '活跃神力',
    'rested-god-power': '休整神力',
  }
  const label = labels[state]
  return moraleLocked(card) ? `${label}；本轮重置阶段无法转为活跃` : label
}
const temporaryMoraleCount = computed(() => Math.max(0, Math.floor(props.player.temporaryMorale ?? 0)))
const activeMorale = computed(() => props.player.morale.filter(card => !card.tapped).length + temporaryMoraleCount.value)
const currentTrialInstanceId = computed(() => props.player.specialZones?.trials?.find(card => !card.trialCompleted)?.instanceId ?? null)
const spendableMorale = computed(() => props.player.spendableResourceCount ?? (activeMorale.value
  + (props.active ? props.player.field.flat().filter(card => card?.cardId === 'S01-0212' && !card.tapped && !card.hidden).length : 0)))
type AbilityEntry = { id: string; label: string; enabled?: boolean; disabledReason?: string; triggerOnly?: boolean }
const factionActions = computed<AbilityEntry[]>(() => {
  if (props.player.factionEffect?.abilities?.length) return props.player.factionEffect.abilities
  if (props.player.factionEffect?.cardId === 'S01-01C1') return [
    { id: 'factionAddActive', label: '我方 回合1次 可消耗2士气：从士气牌库追加1张活跃的士气。' },
  ]
  if (props.player.factionEffect?.cardId === 'S01-04C1') return [
    { id: 'factionDrawMove', label: '我方 回合1次 可消耗2士气：抽取1张牌。随后可选择我方1张活跃的军团进行1格位移。' },
  ]
  return []
})
const masterCard = computed<Card>(() => ({
  instanceId: `master-${props.player.playerIndex}`,
  cardId: props.player.master.masterId,
  name: props.player.master.masterName,
  cardType: 'master',
  faction: props.player.faction,
  imageUrl: props.player.master.masterImageUrl,
  effectText: props.player.master.effectText,
  cost: 0,
  baseTroops: 0,
  troops: 0,
  disasterLevel: 0,
  tapped: Boolean(props.player.master.tapped),
  summonRound: 0,
  abilities: props.player.master.abilities,
}))
function canAttack(card: Card, row: number) {
  if (!props.controllable) return false
  if (props.attackableIds) return props.attackableIds.includes(card.instanceId)
  return Boolean(props.actionsEnabled && !card.cannotAttack && (row === 0 || card.hasRangeBonus) && !card.tapped && !card.hidden && (card.summonRound < (props.round ?? 0) || card.hasCharge))
}
function isCounterTactic(card: Card | null) {
  return card?.cardType === 'counter-tactic'
    || ['S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021', 'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420'].includes(card?.cardId ?? '')
}
function isBattlefieldLegionCard(card: Card) {
  return card.cardType === 'legion' || card.isMasterLegion === true || card.cardId === 'S01-0417' && card.troops > 0
}
function counterState(card: Card | null) {
  if (card?.hidden && card.identityKnown) return 'hidden-dormant'
  if (props.side !== 'my' || !isCounterTactic(card) || !card?.hidden) return ''
  return props.responsePlayableIds?.includes(card.instanceId) ? 'counter-ready' : 'counter-dormant'
}
function isPlacementDestination(row: number, card: Card | null) {
  if (!props.placementMode || (props.placementRow !== null && props.placementRow !== undefined && props.placementRow !== row)) return false
  return !card || Boolean(props.placementCanReplaceCounter && row === 1 && isCounterTactic(card))
}
function canMove(card: Card, row: number, slot: number) {
  if (!props.controllable || !props.actionsEnabled || card.tapped || card.hidden || spendableMorale.value < 1) return false
  return [[row - 1, slot], [row + 1, slot], [row, slot - 1], [row, slot + 1]]
    .some(([nextRow, nextSlot]) => nextRow >= 0 && nextRow < 2 && nextSlot >= 0 && nextSlot < 3 && !props.player.field[nextRow][nextSlot])
}
function canCavalryMove(card: Card) {
  if (!props.controllable || !props.actionsEnabled || card.tapped || card.hidden || card.profession !== '骑兵') return false
  if (card.lastCavalryMoveTurn === props.turnSerial) return false
  return props.player.field.some(row => row.some(slotCard => !slotCard))
}
function canFreeMove(card: Card, row: number, slot: number) {
  if (!props.controllable || !props.actionsEnabled || card.tapped || card.hidden) return false
  const restedHippolyta = props.player.field.flat().some(unit => unit?.cardId === 'S02-0510' && unit.tapped)
  return restedHippolyta && !props.player.field[1 - row][slot]
}
function isMoveTarget(row: number, slot: number) {
  if ((!props.moveMode && !props.freeMoveMode && !props.cavalryMoveMode) || !props.selectedId || props.player.field[row][slot]) return false
  for (let sourceRow = 0; sourceRow < 2; sourceRow++) {
    for (let sourceSlot = 0; sourceSlot < 3; sourceSlot++) {
      if (props.player.field[sourceRow][sourceSlot]?.instanceId !== props.selectedId) continue
      if (props.freeMoveMode) return sourceSlot === slot && sourceRow !== row
      return Boolean(props.cavalryMoveMode)
        || Math.abs(sourceRow - row) + Math.abs(sourceSlot - slot) === 1
    }
  }
  return false
}
function abilities(card: Card): AbilityEntry[] {
  if (card.abilities?.length) return card.abilities
  const map: Record<string, Array<[string, string]>> = {
    'S01-0105': [['searchBrothers', '检索关羽/张飞']],
    'S01-0109': [['addMorale', '追加士气']],
    'S01-0117': [['artifactDraw', '返还士气·抽牌'], ['artifactSearch', '弃牌·检索']],
    'S01-0417': [['kusanagiDebuff', '对方费用-1'], ['kusanagiStrong', '赋予强攻']],
  }
  return (map[card.cardId] ?? []).map(([id, label]) => ({ id, label }))
}
function activeAbilities(card: Card) {
  return abilities(card).filter(entry => entry.id !== 'freeMove')
}
function trialAbility(card: Card) {
  return activeAbilities(card).find(entry => entry.id === 'trialAdvance')
}
function modalAbilities(card: Card) {
  return activeAbilities(card).filter(entry => entry.id !== 'trialAdvance')
}
function canTrial(card: Card) {
  const entry = trialAbility(card)
  return Boolean(entry && entry.enabled !== false && !entry.triggerOnly && props.actionsEnabled && !card.tapped
    && card.summonRound < (props.round ?? 0) && currentTrialInstanceId.value)
}
function canUseAbilities(card: Card) {
  return Boolean(props.controllable) || (card.cardId === 'S01-0004' && card.ownerIndex === props.viewerPlayerIndex)
}
function selectZoneCard(card: Card) {
  emit('focus', card)
  emit('selectCard', card)
  if (canUseAbilities(card) && abilities(card).length) {
    abilityCardOpen.value = card
    abilityCardMinimized.value = false
  }
}
function handleSlot(row: number, slot: number, card: Card | null) {
  if (props.promptSlotIds?.includes(`${row}:${slot}`)) {
    emit('slot', row, slot, card)
    return
  }
  if (card && props.paymentChoiceIds?.includes(card.instanceId)) {
    emit('focus', card)
    emit('paymentResource', card.instanceId)
    return
  }
  emit('slot', row, slot, card)
}
function selectMoralePayment(instanceId: string) {
  if (props.paymentChoiceIds?.includes(instanceId)) emit('paymentResource', instanceId)
}
function temporaryMoraleChoiceId(index: number) {
  return `temporary-morale:${index}`
}
function temporaryMoralePayable(index: number) {
  return Boolean(props.controllable && props.paymentChoiceIds?.includes(temporaryMoraleChoiceId(index)))
}
function selectRunePayment(index: number) {
  const choiceId = `rune:${index}`
  if (props.paymentChoiceIds?.includes(choiceId)) emit('paymentResource', choiceId)
}
function beginCardAbility(card: Card) {
  const entries = modalAbilities(card)
  if (!entries.length) return
  if (entries.length === 1 && entries[0].enabled !== false && !entries[0].triggerOnly) { emit('ability', card, entries[0].id); return }
  abilityCardOpen.value = card
  abilityCardMinimized.value = false
}
</script>

<template>
  <section v-bind="$attrs" class="l12-player-mat" :class="[`side-${side}`, `faction-${player.faction}`, { 'active-turn': active }]"
    :data-player-index="player.playerIndex">
    <div class="commander-zone">
      <div v-if="player.faction === 'otherworld' || player.specialZones?.canopicTrack?.length"
        class="master-marker-track" :class="{ runes: player.faction === 'otherworld', canopic: Boolean(player.specialZones?.canopicTrack?.length) }">
        <template v-if="player.faction === 'otherworld'">
          <button v-for="index in 3" :key="index" type="button" class="rune-orb"
            :class="{ active: index <= (player.specialZones?.runes ?? 0), payable: paymentChoiceIds?.includes(`rune:${index}`), selected: paymentSelectedIds?.includes(`rune:${index}`) }"
            :disabled="!paymentChoiceIds?.includes(`rune:${index}`)"
            :title="paymentChoiceIds?.includes(`rune:${index}`) ? '点击选择此符文' : (index <= (player.specialZones?.runes ?? 0) ? '可用符文' : '未获得符文')"
            @click="selectRunePayment(index)">
            <img :src="roundCardUrl('S02-06S1')" alt="符文" />
          </button>
        </template>
        <template v-else>
          <button v-for="card in player.specialZones?.canopicTrack ?? []" :key="card.cardId" type="button"
            class="canopic-orb" :class="{ completed: card.completed, activatable: controllable && canActivateOsiris }"
            :aria-disabled="!controllable || !canActivateOsiris"
            :title="`${card.name}${card.completed ? '（已置入）' : '（未完成）'}${controllable && !canActivateOsiris ? `：${osirisVictoryDisabledReason || '当前不可发动特殊胜利'}` : ''}`"
            @mouseenter="emit('focus', card)" @focus="emit('focus', card)"
            @click.stop="emit('focus', card); controllable && canActivateOsiris && emit('ability', masterCard, 'isisVictory')">
            <img v-if="roundCardUrl(card.cardId, card.imageUrl)" :src="roundCardUrl(card.cardId, card.imageUrl)" :alt="card.name" />
          </button>
        </template>
      </div>
      <div class="master-column" data-l12-zone="master">
        <button class="mini-master" :class="{ targetable: !controllable && attackMode && masterTargetable, tapped: player.master.tapped, 'combat-target': combatTargetMaster }"
          @mouseenter="emit('focus', masterCard)" @focus="emit('focus', masterCard)" @click="emit('master')">
          <CardImage v-if="!player.master.deployedAsLegion" :card-id="player.master.masterId" :legacy-url="player.master.masterImageUrl" :alt="player.master.masterName" intent="board" eager />
          <span v-else class="master-away" aria-label="主宰当前作为军团位于战场">主宰已登场</span>
          <span>{{ player.master.masterName }}</span>
          <i v-if="player.master.statusIcons?.includes('shield')" class="master-protection-icon" role="img"
            :title="player.master.statusEffects?.find(effect => effect.kind === 'shield')?.label || '主宰暂时不可被进攻'" aria-label="主宰暂时不可被进攻">🛡</i>
          <b class="value-badge master-health">{{ player.master.hp }}<small>/{{ player.master.maxHp }}</small></b>
        </button>
      </div>

      <div class="relic-zone" data-l12-zone="relic" @mouseenter="player.relic && emit('focus', player.relic)" @click="player.relic && selectZoneCard(player.relic)">
        <CardTile v-if="player.relic" :card="player.relic" @focus-card="emit('focus', $event)" />
        <div v-else class="zone-placeholder"><i>✦</i><span>圣物区</span></div>
        <button v-for="(relic, index) in player.extraRelics ?? []" :key="relic.instanceId" class="extra-relic"
          :style="{ transform: `translate(${(index + 1) * 12}px, ${(index + 1) * 7}px)` }"
          @mouseenter="emit('focus', relic)" @click.stop="selectZoneCard(relic)">
          <CardTile :card="relic" :selected="selectedId === relic.instanceId" @focus-card="emit('focus', $event)" />
        </button>
      </div>
    </div>
    <div class="special-lane" :class="{ visible: player.specialZones?.trials?.length }" data-ui-contract="mirrored-extra-zone">
        <div v-if="player.specialZones?.trials?.length" class="trial-zone" :class="{ opponent: side === 'opponent' }">
          <button v-for="trial in player.specialZones.trials" :key="trial.instanceId" type="button" class="trial-card"
            :class="{ concealed: trial.hidden, 'own-concealed': trial.hidden && side === 'my', inactive: !trial.hidden && !trial.trialCompleted }"
            :title="trial.hidden && side === 'opponent' ? '对方未揭示的试炼' : trial.name"
            @mouseenter="(!trial.hidden || side === 'my') && emit('focus', trial)" @focus="(!trial.hidden || side === 'my') && emit('focus', trial)" @click.stop="(!trial.hidden || side === 'my') && selectZoneCard(trial)">
            <img v-if="trial.hidden && side === 'opponent'" class="trial-card-back" src="/assets/l12/trial-back.png" alt="试炼牌背" />
            <CardImage v-else :card-id="trial.cardId" :legacy-url="trial.imageUrl" :alt="trial.name" intent="board" eager />
            <b v-if="trial.instanceId === currentTrialInstanceId" class="trial-progress" aria-label="当前试炼进度">{{ trial.trialProgress ?? player.specialZones?.trialLevel ?? 0 }}</b>
          </button>
        </div>
    </div>

    <div class="battle-zone">
      <div class="formation" data-l12-zone="field">
        <template v-for="row in (side === 'opponent' ? [1, 0] : [0, 1])" :key="row">
          <div v-for="slot in [0,1,2]" :key="slot" class="formation-slot" role="button" tabindex="0"
            data-ui-contract="actual-combat-target-only"
            :class="{
              targetable: Boolean(player.field[row][slot]) && targetableIds?.includes(player.field[row][slot]!.instanceId) && (selectionMode || (!controllable && attackMode)),
              'prompt-selected': selectedTargetIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              available: promptSlotIds?.includes(`${row}:${slot}`) || isPlacementDestination(row, player.field[row][slot]) || (controllable && isMoveTarget(row, slot)),
              source: isSelected(player.field[row][slot]?.instanceId),
              'combat-attacker': combatAttackerId === player.field[row][slot]?.instanceId,
              'combat-target': combatTargetId === player.field[row][slot]?.instanceId,
              'response-target': !player.field[row][slot]?.hidden && responseTargetIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              'payment-resource': paymentChoiceIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              'payment-selected': paymentSelectedIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              'resource-ready': controllable && player.faction === 'taiyangcheng' && player.field[row][slot]?.cardId === 'S01-0212' && !player.field[row][slot]?.tapped,
              [counterState(player.field[row][slot])]: Boolean(counterState(player.field[row][slot]))
            }"
            @click="handleSlot(row, slot, player.field[row][slot])" @keyup.enter="handleSlot(row, slot, player.field[row][slot])">
            <template v-if="player.field[row][slot]">
              <div v-if="selectedId === player.field[row][slot]!.instanceId && actionsEnabled && !attackMode && !moveMode && !freeMoveMode && !cavalryMoveMode && (canUseAbilities(player.field[row][slot]!) || canTrial(player.field[row][slot]!))"
                class="card-context-actions field-actions">
                <button v-if="canUseAbilities(player.field[row][slot]!) && canAttack(player.field[row][slot]!, row)" :class="{ active: attackMode }"
                  @click.stop="emit('cardAction', 'attack', player.field[row][slot]!)">{{ attackMode ? '选择目标' : '进攻' }}</button>
                <button v-if="canUseAbilities(player.field[row][slot]!) && canMove(player.field[row][slot]!, row, slot)" :class="{ active: moveMode }"
                  @click.stop="emit('cardAction', 'move', player.field[row][slot]!)">{{ moveMode ? '选择位置' : '移动' }}</button>
                <button v-if="canUseAbilities(player.field[row][slot]!) && canFreeMove(player.field[row][slot]!, row, slot)" :class="{ active: freeMoveMode }"
                  @click.stop="emit('cardAction', 'freeMove', player.field[row][slot]!)">{{ freeMoveMode ? '选择前后位置' : '免费位移' }}</button>
                <button v-if="canUseAbilities(player.field[row][slot]!) && canCavalryMove(player.field[row][slot]!)" :class="{ active: cavalryMoveMode }"
                  @click.stop="emit('cardAction', 'cavalryMove', player.field[row][slot]!)">{{ cavalryMoveMode ? '选择任意位置' : '骑兵位移' }}</button>
                <button v-if="canUseAbilities(player.field[row][slot]!) && modalAbilities(player.field[row][slot]!).length"
                  @click.stop="beginCardAbility(player.field[row][slot]!)">发动</button>
                <button v-if="canTrial(player.field[row][slot]!)" type="button" data-ui-contract="independent-trial-action"
                  @click.stop="emit('ability', player.field[row][slot]!, 'trialAdvance')">试炼</button>
              </div>
              <CardTile :card="hiddenRevealCard?.instanceId === player.field[row][slot]!.instanceId ? hiddenRevealCard : player.field[row][slot]!"
                :class="{ 'battlefield-legion-card': isBattlefieldLegionCard(player.field[row][slot]!) }"
                :selected="isSelected(player.field[row][slot]!.instanceId)"
                @focus-card="emit('focus', $event)"
                @mouseenter="emit('focus', hiddenRevealCard?.instanceId === player.field[row][slot]!.instanceId ? hiddenRevealCard : player.field[row][slot]!)" />
            </template>
            <span v-else>{{ row === 0 ? '前排' : '后排' }} {{ slot + 1 }}</span>
          </div>
        </template>
      </div>
    </div>

    <div class="mat-piles">
      <div class="pile deck" data-l12-zone="library">
        <div class="pile-card" :class="{ 'card-back': !player.libraryTop }"><CardImage v-if="player.libraryTop" :card-id="player.libraryTop.cardId" :legacy-url="player.libraryTop.imageUrl" :alt="player.libraryTop.name" intent="thumb" eager @mouseenter="emit('focus', player.libraryTop)"/><i v-else>XII</i></div>
        <b class="value-badge pile-count">{{ player.libraryCount }}</b><span>牌库</span>
      </div>
      <button class="pile graveyard" data-l12-zone="graveyard" @click="emit('graveyard', player.playerIndex)">
        <div class="pile-card">
          <CardImage v-if="topGraveyard" :card-id="topGraveyard.cardId" :legacy-url="topGraveyard.imageUrl" :alt="topGraveyard.name" intent="thumb" eager />
          <i v-else>墓</i>
        </div>
        <b class="value-badge pile-count">{{ player.graveyard?.length ?? player.graveyardCount ?? 0 }}</b><span>墓地</span>
      </button>
    </div>

    <div class="resource-zone" data-ui-contract="centered-resource-zone">
      <button class="faction-effect-trigger resource-faction-action" data-ui-contract="resource-faction-action"
        @click.stop="factionOpen = true; factionMinimized = false">阵营效果</button>
      <div class="resource-morale-summary" data-ui-contract="resource-morale-summary">
        <span class="resource-morale-label" data-ui-contract="resource-morale-label">士气</span>
        <b class="morale-count resource-morale-count" data-ui-contract="resource-morale-count"
          :title="`当前活跃士气 ${activeMorale} / 当前士气上限 ${currentMoraleLimit}`">{{ activeMorale }}/{{ currentMoraleLimit }}</b>
      </div>
      <div class="morale-stack resource-morale-stack" data-ui-contract="resource-morale-stack"
        :class="{ 'morale-remainder-1': visibleMoraleCount % 3 === 1, 'morale-remainder-2': visibleMoraleCount % 3 === 2 }">
      <button v-for="index in visibleTemporaryMoraleCount" :key="`temporary-${index}`" type="button"
        class="morale-orb temporary-morale" data-ui-contract="temporary-morale-selectable-lotus"
        :class="{ payable: temporaryMoralePayable(index), selected: paymentSelectedIds?.includes(temporaryMoraleChoiceId(index)) }"
        :title="temporaryMoralePayable(index) ? '点击选择此临时士气支付；休整时消失' : '临时士气；休整时消失'"
        :aria-disabled="!temporaryMoralePayable(index)"
        @click.stop="temporaryMoralePayable(index) && selectMoralePayment(temporaryMoraleChoiceId(index))">
        <img :src="blackLotusLogoUrl" alt="黑色莲花临时士气" />
      </button>
      <button v-for="morale in displayMoraleSlots" :key="morale.instanceId" type="button" class="morale-orb"
        :class="[moraleState(morale), { payable: paymentChoiceIds?.includes(morale.instanceId), selected: paymentSelectedIds?.includes(morale.instanceId) }]"
        :title="moraleLabel(morale)" :aria-disabled="!paymentChoiceIds?.includes(morale.instanceId)"
        @click.stop="selectMoralePayment(morale.instanceId)">
        <img v-if="morale.isGodPower" class="god-power-logo" :src="godPowerLogoUrl" alt="神力" />
        <img v-else-if="factionLogoUrls[player.faction]" :src="factionLogoUrls[player.faction]" :alt="player.faction" />
        <span v-if="moraleLocked(morale)" class="morale-lock-icon" data-ui-contract="active-morale-lock"
          role="img" title="本轮重置阶段无法转为活跃" aria-label="本轮重置阶段无法转为活跃"></span>
      </button>
      </div>
    </div>
  </section>

  <Teleport to="body">
    <div v-if="factionOpen" class="faction-effect-overlay" :class="{ minimized: factionMinimized }" @click.self="factionOpen = false">
      <section v-if="factionMinimized" class="faction-minimized-bar">
        <button :aria-label="`展开：${player.factionEffect?.name || '阵营效果'}`" :title="player.factionEffect?.name || '阵营效果'" @click="factionMinimized = false">展开</button>
      </section>
      <section v-else class="faction-effect-dialog" role="dialog" aria-modal="true">
        <button class="faction-minimize" aria-label="最小化弹框" title="最小化以查看场面" @click="factionMinimized = true">—</button>
        <button class="faction-close" aria-label="关闭" @click="factionOpen = false">×</button>
        <CardImage v-if="player.factionEffect" :card-id="player.factionEffect.cardId" :legacy-url="player.factionEffect.imageUrl" :alt="player.factionEffect.name" intent="detail" eager />
        <div>
          <small>{{ side === 'my' ? '我方阵营效果' : '对方阵营效果' }}</small>
          <h2>{{ player.factionEffect?.name || '阵营效果' }}</h2>
          <p v-if="!factionActions.length" class="l12-effect-body">{{ player.factionEffect?.effectText || '暂无效果文字' }}</p>
          <div v-if="factionActions.length" class="faction-effect-actions">
            <button v-for="entry in factionActions" :key="entry.id" class="l12-effect-body l12-effect-body--compact"
              :disabled="!controllable || !actionsEnabled || entry.enabled === false || entry.triggerOnly"
              :title="entry.disabledReason || (entry.triggerOnly ? '仅在触发时点发动' : '')"
              @click="emit('factionAbility', entry.id); factionOpen = false">
              {{ entry.label }}
            </button>
          </div>
          <span v-if="controllable && !actionsEnabled" class="faction-action-hint">仅在我方主要阶段可以发动</span>
        </div>
      </section>
    </div>
  </Teleport>

  <Teleport to="body">
    <div v-if="abilityCardOpen" class="faction-effect-overlay" :class="{ minimized: abilityCardMinimized }" @click.self="abilityCardOpen = null">
      <section v-if="abilityCardMinimized" class="faction-minimized-bar">
        <button :aria-label="`展开：${abilityCardOpen.name}`" :title="abilityCardOpen.name" @click="abilityCardMinimized = false">展开</button>
      </section>
      <section v-else class="faction-effect-dialog" role="dialog" aria-modal="true">
        <button class="faction-minimize" aria-label="最小化弹框" @click="abilityCardMinimized = true">—</button>
        <button class="faction-close" aria-label="关闭" @click="abilityCardOpen = null">×</button>
        <CardImage :card-id="abilityCardOpen.cardId" :legacy-url="abilityCardOpen.imageUrl" :alt="abilityCardOpen.name" intent="detail" eager @mouseenter="emit('focus', abilityCardOpen)" />
        <div>
          <small>卡牌效果</small><h2>{{ abilityCardOpen.name }}</h2>
          <p v-if="!activeAbilities(abilityCardOpen).length" class="l12-effect-body">{{ abilityCardOpen.effectText || '暂无效果文字' }}</p>
          <div class="faction-effect-actions">
            <button v-for="entry in activeAbilities(abilityCardOpen)" :key="entry.id" class="l12-effect-body l12-effect-body--compact"
              :disabled="!actionsEnabled || entry.enabled === false || entry.triggerOnly"
              :title="entry.disabledReason || (entry.triggerOnly ? '仅在触发时点发动' : '')"
              @click="emit('ability', abilityCardOpen!, entry.id); abilityCardOpen = null">{{ entry.label }}</button>
          </div>
          <span v-if="!actionsEnabled" class="faction-action-hint">仅在可发动时点可以发动</span>
        </div>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.l12-player-mat{--commander-area:300px;box-sizing:border-box;width:min(100%,1320px);margin-inline:auto;grid-template-columns:minmax(270px,300px) minmax(500px,1fr) 100px 132px;grid-template-rows:auto auto minmax(30px,1fr);justify-content:center;gap:6px 10px}
.commander-zone{grid-column:1;grid-row:1/-1;align-self:center;grid-template-columns:140px 100px;min-height:200px}.battle-zone{grid-column:2;grid-row:1/-1;align-self:center;transform:translateX(-40px)}.mat-piles{grid-column:3;grid-row:1/-1;align-self:center;width:100px;height:286px;grid-template-rows:repeat(2,140px);gap:6px;transform:translateX(-22px)}.mat-piles .pile,.mat-piles .pile.deck{box-sizing:border-box;width:100px;height:140px;min-height:140px}.mat-piles .pile-card{width:96px;height:134px}.mat-piles .pile span{left:5px;bottom:5px;padding:3px 6px;border:1px solid rgba(238,238,228,.38);background:rgba(5,7,8,.78);line-height:1;white-space:nowrap}.mat-piles .pile .pile-count{right:5px;top:5px;min-width:34px!important;height:28px!important;padding:0 8px!important;font-size:max(17px,var(--l12-board-copy,13px))!important}.master-column .mini-master{width:140px;height:196px}.master-column .mini-master>span{left:8px;right:8px;bottom:40px;overflow:visible;white-space:nowrap;text-overflow:clip;line-height:1.35}.relic-zone{width:100px;height:140px}
.resource-zone{grid-column:4;grid-row:1/-1;display:flex;box-sizing:border-box;width:132px;max-width:132px;align-self:center;justify-self:start;flex-direction:column;gap:7px}.resource-faction-action,.resource-morale-summary,.resource-morale-stack{box-sizing:border-box;width:132px;max-width:132px;flex:none}.resource-faction-action{order:1;justify-self:start}.resource-morale-summary{order:2;display:grid;grid-template-columns:56px 76px;height:34px;align-items:stretch}.resource-morale-stack{order:3}.side-opponent .resource-morale-stack{order:1;flex-wrap:wrap-reverse;align-content:flex-end}.side-opponent .resource-morale-summary{order:2}.side-opponent .resource-faction-action{order:3}.resource-morale-label{display:grid;box-sizing:border-box;min-width:56px;height:34px;place-items:center;padding:0 5px;border:1px solid color-mix(in srgb,var(--resource-accent,#d2c8a5) 48%,#5b625f);border-right:0;background:rgba(7,10,11,.64);color:#d2d4cf;font-size:var(--l12-board-copy,13px);font-weight:900;letter-spacing:.08em;line-height:1;white-space:nowrap}.resource-morale-count{box-sizing:border-box;width:76px;max-width:76px;justify-self:stretch;margin:0}.resource-morale-stack{display:flex;min-height:46px;flex-flow:row wrap;align-content:flex-start;align-items:center;justify-content:flex-start;gap:6px;padding:5px 5px;border:1px solid color-mix(in srgb,var(--resource-accent,#d2c8a5) 38%,#454c49);background:linear-gradient(145deg,rgba(19,23,23,.78),rgba(5,8,9,.62));box-shadow:inset 3px 0 color-mix(in srgb,var(--resource-accent,#d2c8a5) 65%,transparent)}
.formation-slot.combat-attacker{box-shadow:none!important}
.formation-slot.combat-target,.mini-master.combat-target{z-index:8;border-color:#e0b85a!important;box-shadow:0 0 0 3px #e0b85a,0 0 24px rgba(224,184,90,.7)!important}
.formation-slot.response-target{z-index:8;border-color:#e0b85a!important;box-shadow:0 0 0 3px #e0b85a,0 0 24px rgba(224,184,90,.7)!important}
.formation-slot.combat-target :deep(.card-tile),.mini-master.combat-target{animation:l12-combat-target-cue .3s ease-out both}.card-power{transition:background-color .16s,color .16s,filter .16s}
@keyframes l12-combat-target-cue{0%,100%{filter:none}45%{filter:brightness(1.22)}}
@media(prefers-reduced-motion:reduce){.formation-slot.combat-target :deep(.card-tile),.mini-master.combat-target{animation:none}}
.formation-slot.payment-resource{z-index:9;border-color:#52d58a!important;box-shadow:0 0 0 2px #52d58a,0 0 18px rgba(82,213,138,.55)!important;cursor:pointer}.formation-slot.payment-selected{border-color:#f1c75b!important;box-shadow:0 0 0 3px #f1c75b,0 0 22px rgba(241,199,91,.7)!important}
.formation-slot.resource-ready:not(.payment-resource):not(.combat-attacker):not(.combat-target){border-color:#8cdbad;box-shadow:0 0 0 1px rgba(140,219,173,.72),0 0 10px rgba(82,213,138,.28)}
.formation-slot.prompt-selected{z-index:10;border-color:#f1c75b!important;box-shadow:0 0 0 3px #f1c75b,0 0 22px rgba(241,199,91,.68)!important}.formation-slot.prompt-selected::after{content:'已选择';position:absolute;z-index:12;right:4px;top:4px;padding:3px 6px;background:#f1c75b;color:#15120a;font-size:var(--l12-board-copy,13px);font-weight:900}
.morale-orb{position:relative;box-sizing:border-box;width:22px;height:22px;min-width:22px;padding:0;border:1px solid #7d8581;border-radius:50%;display:grid;place-items:center;overflow:visible;background:#151a1a;transition:filter .16s,box-shadow .16s,border-color .16s}.morale-orb img{width:14px;height:14px;object-fit:contain}.morale-orb img.god-power-logo{filter:sepia(1) saturate(3.2) hue-rotate(352deg) brightness(1.18)}.morale-orb.active-morale{background:var(--faction-morale-active,#b4b2af);border-color:var(--faction-morale-border,#eee);box-shadow:inset 0 0 0 1px rgba(255,255,255,.36),0 0 6px color-mix(in srgb,var(--faction-morale-active,#b4b2af) 76%,transparent);filter:saturate(1.15) brightness(1.1)}.active-turn .morale-orb.active-morale{box-shadow:inset 0 0 0 1px rgba(255,255,255,.52),0 0 11px color-mix(in srgb,var(--faction-morale-active,#b4b2af) 92%,transparent);filter:saturate(1.25) brightness(1.2)}.morale-orb.rested-morale{background:var(--faction-morale-rested,#555);border-color:#4d5350;box-shadow:inset 0 0 0 3px rgba(0,0,0,.38);filter:saturate(.35) brightness(.52)}.morale-orb.active-god-power{background:#0091be;border-color:#f4dda1;box-shadow:inset 0 0 0 1px rgba(255,255,255,.35),0 0 9px rgba(0,145,190,.72);filter:saturate(1.18) brightness(1.12)}.active-turn .morale-orb.active-god-power{box-shadow:inset 0 0 0 1px rgba(255,255,255,.55),0 0 13px rgba(0,174,222,.9);filter:saturate(1.25) brightness(1.2)}.morale-orb.rested-god-power{background:#264c57;border-color:#7e7459;box-shadow:inset 0 0 0 3px rgba(0,0,0,.35);filter:saturate(.48) brightness(.56)}.morale-orb.unused{opacity:.25}.morale-orb.payable{cursor:pointer;border-color:#72e29f;box-shadow:0 0 9px rgba(82,213,138,.75)}.morale-orb.selected{border:3px solid #fff0a0;box-shadow:0 0 12px #f1c75b}.morale-orb:disabled:not(.payable){cursor:default}
.morale-lock-icon{position:absolute;z-index:3;right:-10px;top:-3px;box-sizing:border-box;width:12px;height:10px;border:1px solid rgba(218,221,214,.76);border-radius:2px;background:rgba(7,9,10,.82);box-shadow:0 1px 3px rgba(0,0,0,.72);opacity:.78;pointer-events:none;transition:opacity .16s,transform .16s}.morale-lock-icon::before{content:'';position:absolute;left:2px;top:-6px;box-sizing:border-box;width:6px;height:7px;border:2px solid rgba(7,9,10,.92);border-bottom:0;border-radius:5px 5px 0 0}.morale-lock-icon::after{content:'';position:absolute;left:4px;top:3px;width:2px;height:4px;border-radius:1px;background:rgba(225,227,219,.82)}.morale-orb.selected .morale-lock-icon,.morale-orb.payable .morale-lock-icon{opacity:.32;transform:translate(2px,-2px) scale(.84)}
.morale-orb.temporary-morale{position:relative;border-color:#f2f2ed;background:#050607;box-shadow:0 0 0 2px #121416,0 0 10px rgba(255,255,255,.35);cursor:default;opacity:1!important}.morale-orb.temporary-morale img{width:16px;height:16px;object-fit:contain}.morale-orb.temporary-morale.payable{cursor:pointer;border-color:#72e29f;box-shadow:0 0 0 2px #121416,0 0 12px rgba(82,213,138,.78)}
.faction-tianting{--faction-morale-active:#dbbc00;--faction-morale-rested:#665a08;--faction-morale-border:#fff0a0}.faction-otherworld{--faction-morale-active:#31873f;--faction-morale-rested:#173e20;--faction-morale-border:#9be5a7}.faction-gaotianyuan{--faction-morale-active:#db0d17;--faction-morale-rested:#681118;--faction-morale-border:#ffacb0}.faction-asgard{--faction-morale-active:#342f2f;--faction-morale-rested:#1c1919;--faction-morale-border:#b9aeae}.faction-taiyangcheng{--faction-morale-active:#74227e;--faction-morale-rested:#38123d;--faction-morale-border:#dfa3e6}.faction-universal{--faction-morale-active:#b4b2af;--faction-morale-rested:#555451;--faction-morale-border:#f0efeb}.faction-olympus{--faction-morale-active:#075b76;--faction-morale-rested:#173844;--faction-morale-border:#86d7ee}
.master-column{position:relative;display:grid;align-content:start;justify-items:center;gap:5px;min-width:88px}.master-column .mini-master{position:relative;inset:auto}.master-marker-track{position:absolute;z-index:9;left:8px;top:-43px;display:flex;width:198px;height:36px;align-items:center;justify-content:flex-start;gap:5px;pointer-events:auto}.side-opponent .master-marker-track{top:auto;bottom:-43px}.master-marker-track.canopic{justify-content:space-between;gap:3px}.special-lane{display:none;position:absolute;z-index:18;left:8px;right:auto;width:296px;height:168px;pointer-events:none}.special-lane.visible{display:grid;align-content:center;justify-items:center}.side-opponent .special-lane{top:auto;bottom:calc(100% + 8px)}.side-my .special-lane{left:12px;top:calc(100% + 8px);bottom:auto;width:140px}.trial-zone{position:relative;z-index:6;display:flex;width:max-content;max-width:100%;align-items:center;justify-content:center;gap:8px;pointer-events:auto}.trial-card{position:relative;width:112px;height:auto;aspect-ratio:1752/1255;padding:0;border:1px solid #8dc6b2;background:#080b0b;overflow:hidden;box-shadow:0 6px 16px #000}.side-my .trial-card{width:123.2px}.trial-card .l12-card-image,.trial-card-back{width:100%;height:100%;object-fit:contain;background:#080b0b}.trial-card.inactive .l12-card-image{filter:grayscale(.85) brightness(.52)}.trial-card.concealed .l12-card-image{filter:none}.trial-card b{position:absolute;left:50%;top:50%;display:grid;min-width:30px;height:30px;place-items:center;padding:0 6px;border:2px solid #79c889;border-radius:50%;background:#102e17ed;color:#fff;font-size:max(16px,var(--l12-board-copy,13px));box-shadow:0 0 11px rgba(49,135,63,.82);transform:translate(-50%,-50%)}.rune-orb{width:36px;height:36px;min-width:36px;padding:0;border:1px solid #596661;border-radius:50%;overflow:hidden;background:#111;filter:grayscale(1) brightness(.38)}.rune-orb.active{border-color:#80d69c;filter:none;box-shadow:0 0 8px rgba(49,135,63,.7)}.rune-orb.payable{cursor:pointer;box-shadow:0 0 0 2px #75e0a1,0 0 13px rgba(49,135,63,.9)}.rune-orb.selected{border-color:#fff0a0;box-shadow:0 0 0 3px #d8b34d,0 0 15px rgba(216,179,77,.95)}.rune-orb:disabled{cursor:default;opacity:1}.rune-orb img{width:100%;height:100%;object-fit:cover;object-position:center 14%;transform:scale(1.1)}
.canopic-orb{width:36px;height:36px;min-width:36px;padding:0;overflow:hidden;border:1px solid #63555a;border-radius:50%;background:#090a0b;filter:grayscale(1) brightness(.3);cursor:pointer}.canopic-orb img{width:100%;height:100%;object-fit:cover;object-position:center 14%;transform:scale(1.12)}.canopic-orb.completed{border-color:#d0aa52;filter:none;box-shadow:0 0 7px rgba(208,170,82,.65)}.canopic-orb.activatable{border-color:#6ee2a0;box-shadow:0 0 9px rgba(82,213,138,.8);animation:canopic-ready 1.25s ease-in-out infinite alternate}@keyframes canopic-ready{to{transform:translateY(-2px);filter:brightness(1.18)}}
.master-marker-track{top:-70px}.side-opponent .master-marker-track{top:auto;bottom:-70px}
.value-badge{display:grid!important;min-width:25px!important;height:22px!important;place-items:center!important;padding:0 6px!important;border:1px solid #f2f0e6!important;border-radius:2px!important;background:#090b0d!important;color:#fff!important;box-shadow:0 2px 0 #000,0 0 0 1px rgba(0,0,0,.65)!important;font-weight:900!important;line-height:1!important}.value-badge small{margin-left:1px;color:#bfc3c0;font-size:.62em}.pile .pile-count{position:absolute;z-index:8;right:3px;top:3px}.mini-master .master-health{position:absolute;z-index:8;right:3px;bottom:3px;display:inline-flex!important;width:max-content;min-width:44px!important;align-items:center;justify-content:center;white-space:nowrap}.master-protection-icon{position:absolute;z-index:9;left:4px;top:4px;display:grid;width:18px;height:18px;place-items:center;border:1px solid #75c79c;border-radius:2px;background:rgba(8,11,12,.94);color:#a4e7bd;font-size:var(--l12-board-copy,13px);font-style:normal;line-height:1}.morale-count{display:grid;min-width:42px;height:24px;place-items:center;padding:0 7px;border:1px solid #cbc6b8;background:#080a0b;color:#fff;box-shadow:0 2px 0 #000;font-size:var(--l12-board-copy,13px);line-height:1;white-space:nowrap}.morale-orb[aria-disabled="true"]{cursor:default}.morale-orb.active-morale[aria-disabled="true"],.morale-orb.active-god-power[aria-disabled="true"]{opacity:1}
.master-column .mini-master .master-health{right:6px;bottom:5px;min-width:58px!important;height:32px!important;padding:0 8px!important;font-size:max(21px,var(--l12-board-copy,13px))!important}.master-column .mini-master .master-health small{margin-left:2px;font-size:var(--l12-board-copy,13px)!important}
.resource-morale-stack .morale-orb{width:32px;height:32px;min-width:32px}.resource-morale-stack .morale-orb img{width:21px;height:21px}.resource-morale-stack .morale-orb.temporary-morale img{width:23px;height:23px}
.side-my{--resource-accent:#53bdc5}.side-opponent{--resource-accent:#c9505a}
.resource-morale-count{height:34px;min-height:34px;padding:0 5px;border-color:color-mix(in srgb,var(--resource-accent,#d2c8a5) 48%,#5b625f);background:rgba(7,10,11,.72);box-shadow:none;color:#f0eee6;font-size:max(16px,var(--l12-board-copy,13px))}
.l12-player-mat{grid-template-columns:minmax(270px,300px) minmax(500px,1fr) 100px 156px}.mat-piles{transform:translateX(-30px)}
.resource-zone,.resource-faction-action,.resource-morale-summary,.resource-morale-stack{width:156px;max-width:156px}.resource-zone{gap:8px}.resource-morale-summary{grid-template-columns:68px 88px;height:38px}.resource-morale-label{min-width:68px;height:38px;padding:0 10px}.resource-morale-count{width:88px;max-width:88px;height:38px;min-height:38px;padding:0 12px}.resource-morale-stack{min-height:54px;justify-content:center;gap:8px 10px;padding:10px}
.resource-morale-stack.morale-remainder-1::after,.resource-morale-stack.morale-remainder-2::after{content:'';display:block;flex:none;height:32px;pointer-events:none;visibility:hidden}.resource-morale-stack.morale-remainder-1::after{width:74px}.resource-morale-stack.morale-remainder-2::after{width:32px}
.commander-zone{grid-template-columns:140px 132.25px}.battle-zone{transform:translateX(-74px)}.mat-piles{transform:translateX(-24px)}
.relic-zone{box-sizing:border-box;width:132.25px;height:185.15px;aspect-ratio:5/7}.relic-zone :deep(.card-tile){width:127.65px;height:178.71px;flex-basis:127.65px;aspect-ratio:5/7}
.special-lane{left:6px;width:152px;height:286px}.special-lane.visible{align-content:start}.side-opponent .special-lane{top:auto;bottom:calc(100% + 11px);align-content:end}.side-my .special-lane{left:6px;top:calc(100% + 11px);bottom:auto;width:152px;align-content:start}
.trial-zone{flex-direction:column}.side-opponent .trial-zone{flex-direction:column-reverse}.trial-card,.side-my .trial-card{width:135.52px}
</style>

<style scoped>
.extra-relic{position:absolute;z-index:2;inset:0;width:100%;height:100%;padding:0;border:0;background:transparent}.extra-relic :deep(.card-tile){width:100%;height:100%}
.trial-card.own-concealed .l12-card-image{filter:grayscale(.85) brightness(.52)}
.trial-card .trial-progress{box-sizing:border-box;width:46px;min-width:46px;height:46px;min-height:46px;padding:0 7px;background:#102e17f2;font-size:max(20px,var(--l12-board-copy,13px));font-variant-numeric:tabular-nums;line-height:1;white-space:nowrap;box-shadow:0 0 0 2px rgba(4,22,10,.7),0 0 14px rgba(49,135,63,.88)}
.formation-slot.counter-dormant :deep(.card-tile),.formation-slot.hidden-dormant :deep(.card-tile){filter:brightness(.4) saturate(.55)}.formation-slot.counter-ready :deep(.card-tile){filter:brightness(1.08);box-shadow:0 0 0 2px #71e197,0 0 17px rgba(70,220,126,.7)}
.formation-slot :deep(.battlefield-legion-card){border:1px solid transparent;border-radius:0}.formation-slot:not(.counter-ready) :deep(.battlefield-legion-card),.formation-slot:not(.counter-ready) :deep(.battlefield-legion-card:hover),.formation-slot:not(.counter-ready) :deep(.battlefield-legion-card.selected),.formation-slot:not(.counter-ready) :deep(.battlefield-legion-card.tapped.selected){border-color:transparent;box-shadow:none}.formation-slot.source:not(.combat-target):not(.payment-resource):not(.payment-selected):not(.prompt-selected){border-color:#62c5cc;box-shadow:inset 0 0 0 2px rgba(70,185,195,.5)}.formation-slot:focus-visible{outline:2px solid #f4f0df;outline-offset:2px}
.faction-effect-trigger{padding:3px 7px;border:1px solid rgba(238,238,228,.42);border-radius:1px;background:#111718;color:#e8e5dc;font-size:var(--l12-board-copy,13px);font-weight:900;white-space:nowrap}.faction-effect-trigger:hover{border-color:var(--cyan);color:#fff}
.faction-effect-trigger.resource-faction-action{min-height:38px;padding:7px 10px;border-color:color-mix(in srgb,var(--resource-accent,#d2c8a5) 58%,#737b77);background:linear-gradient(120deg,color-mix(in srgb,var(--resource-accent,#d2c8a5) 22%,#101415),rgba(8,11,12,.82));box-shadow:inset 3px 0 var(--resource-accent,#d2c8a5);color:#f0eee7;letter-spacing:.06em;text-align:center}.faction-effect-trigger.resource-faction-action:hover{border-color:var(--resource-accent,#d2c8a5);background:linear-gradient(120deg,color-mix(in srgb,var(--resource-accent,#d2c8a5) 34%,#101415),#111718)}
.faction-effect-overlay{position:fixed;z-index:1100;inset:0;display:grid;place-items:center;background:rgba(2,4,5,.78);backdrop-filter:blur(7px)}
.faction-effect-dialog{position:relative;width:min(650px,calc(100vw - 32px));display:grid;grid-template-columns:220px 1fr;gap:24px;padding:22px;border:1px solid rgba(238,238,228,.7);background:linear-gradient(145deg,#171c1d,#07090a);box-shadow:0 24px 70px #000}
.faction-effect-dialog>.l12-card-image{width:220px;height:308px;background:#050708}
.faction-effect-dialog small{color:var(--cyan);font-size:var(--l12-board-copy,13px);letter-spacing:.14em}.faction-effect-dialog h2{margin:8px 0 14px;color:#f0ede4;font-size:max(25px,var(--l12-board-copy,13px))}.faction-effect-dialog p{color:#d4d5cf;font-size:var(--l12-board-copy,13px);font-weight:800;line-height:1.85;white-space:pre-wrap}.faction-close,.faction-minimize{position:absolute;top:9px;width:30px;height:30px;border:1px solid #777;background:#111;color:#eee;font-size:max(20px,var(--l12-board-copy,13px))}.faction-close{right:9px}.faction-minimize{right:47px}.faction-effect-actions{display:grid;gap:8px;margin-top:20px}.faction-effect-actions button{padding:11px;border:1px solid var(--cyan);background:rgba(40,133,140,.2);color:#fff;font-weight:900;text-align:left}.faction-effect-actions button:disabled{cursor:not-allowed;border-color:#4a504e;background:#202423;color:#737a77;filter:saturate(.25)}.faction-action-hint{display:block;margin-top:18px;color:#777f7c;font-size:var(--l12-board-copy,13px)}.faction-effect-overlay.minimized{z-index:2000;inset:auto 16px 66px auto;display:block;background:transparent;backdrop-filter:none;pointer-events:none}.faction-minimized-bar{display:block;pointer-events:auto}.faction-minimized-bar button{padding:6px 10px;border:1px solid var(--cyan);background:#174e54;color:#fff;box-shadow:0 12px 35px #000}
@media(max-width:650px){.faction-effect-dialog{grid-template-columns:1fr}.faction-effect-dialog>.l12-card-image{width:140px;height:196px;margin:auto}.faction-effect-overlay.minimized{right:10px;bottom:60px}}
</style>
