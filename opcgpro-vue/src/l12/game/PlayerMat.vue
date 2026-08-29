<script setup lang="ts">
import { computed, ref } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card, PlayerView } from '../types'
import { factionLogoUrls, godPowerLogoUrl, roundCardUrl } from '../specialAssets'
import CardImage from '../CardImage.vue'

const props = defineProps<{
  player: PlayerView
  side: 'my' | 'opponent'
  controllable?: boolean
  viewerPlayerIndex?: number
  selectedId?: string | null
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

const factionOpen = ref(false)
const factionMinimized = ref(false)
const abilityCardOpen = ref<Card | null>(null)
const abilityCardMinimized = ref(false)
const moraleLimit = computed(() => props.player.morale.length + (props.player.moraleDeck?.length ?? props.player.moraleDeckCount ?? 0))
const currentMoraleLimit = computed(() => props.player.morale.length)
const topGraveyard = computed(() => props.player.graveyard?.at(-1) ?? null)
type MoraleResource = PlayerView['morale'][number]
const displayMoraleSlots = computed<Array<MoraleResource | null>>(() => {
  const olympus = props.player.faction === 'olympus'
  const resources = props.player.morale.map((resource, originalIndex) => ({ resource, originalIndex }))
  resources.sort((left, right) => {
    const rank = ({ isGodPower, tapped }: MoraleResource) => olympus
      ? (isGodPower ? (tapped ? 2 : 0) : (tapped ? 3 : 1))
      : (tapped ? 1 : 0)
    return rank(left.resource) - rank(right.resource) || left.originalIndex - right.originalIndex
  })
  return [...resources.map(({ resource }) => resource), ...Array<null>(Math.max(0, moraleLimit.value - resources.length)).fill(null)]
})
function moraleState(card: MoraleResource | null) {
  if (!card) return 'unused'
  if (card.isGodPower) return card.tapped ? 'rested-god-power' : 'active-god-power'
  return card.tapped ? 'rested-morale' : 'active-morale'
}
function moraleLabel(card: MoraleResource | null) {
  const state = moraleState(card)
  const labels: Record<string, string> = {
    'active-morale': '活跃士气',
    'rested-morale': '休整士气',
    'active-god-power': '活跃神力',
    'rested-god-power': '休整神力',
    unused: '尚未追加的士气',
  }
  return labels[state]
}
const activeMorale = computed(() => props.player.morale.filter(card => !card.tapped).length)
const canopicComplete = computed(() => (props.player.specialZones?.canopicTrack?.filter(card => card.completed).length ?? 0) >= 5)
const currentTrialInstanceId = computed(() => props.player.specialZones?.trials?.find(card => !card.trialCompleted)?.instanceId ?? null)
const spendableMorale = computed(() => activeMorale.value + (props.player.temporaryMorale ?? 0))
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
  <section class="l12-player-mat" :class="[`side-${side}`, `faction-${player.faction}`, { 'active-turn': active }]"
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
            class="canopic-orb" :class="{ completed: card.completed, activatable: controllable && canopicComplete }"
            :title="`${card.name}${card.completed ? '（已置入）' : '（未完成）'}`"
            @mouseenter="emit('focus', card)" @focus="emit('focus', card)"
            @click.stop="emit('focus', card); controllable && canopicComplete && emit('ability', masterCard, 'isisVictory')">
            <img v-if="roundCardUrl(card.cardId, card.imageUrl)" :src="roundCardUrl(card.cardId, card.imageUrl)" :alt="card.name" />
          </button>
        </template>
      </div>
      <div class="master-column" data-l12-zone="master">
        <button class="mini-master" :class="{ targetable: !controllable && attackMode && masterTargetable, tapped: player.master.tapped, 'combat-target': combatTargetMaster }"
          @mouseenter="emit('focus', masterCard)" @focus="emit('focus', masterCard)" @click="emit('master')">
          <CardImage :card-id="player.master.masterId" :legacy-url="player.master.masterImageUrl" :alt="player.master.masterName" intent="board" eager />
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
      <div class="special-lane" :class="{ visible: player.faction === 'otherworld' && player.specialZones?.trials?.length }">
        <div v-if="player.faction === 'otherworld' && player.specialZones?.trials?.length" class="trial-zone" :class="{ opponent: side === 'opponent' }">
          <button v-for="trial in player.specialZones.trials" :key="trial.instanceId" type="button" class="trial-card"
            :class="{ concealed: trial.hidden, 'own-concealed': trial.hidden && side === 'my', inactive: !trial.hidden && !trial.trialCompleted }"
            :title="trial.hidden ? '对方未揭示的试炼' : trial.name"
            @mouseenter="!trial.hidden && emit('focus', trial)" @focus="!trial.hidden && emit('focus', trial)" @click.stop="!trial.hidden && selectZoneCard(trial)">
            <img v-if="trial.hidden" class="trial-card-back" src="/assets/l12/trial-back.png" alt="试炼牌背" />
            <CardImage v-else :card-id="trial.cardId" :legacy-url="trial.imageUrl" :alt="trial.name" intent="board" eager />
            <b v-if="trial.instanceId === currentTrialInstanceId">{{ trial.trialProgress ?? player.specialZones?.trialLevel ?? 0 }}</b>
          </button>
        </div>
      </div>
    </div>

    <div class="battle-zone">
      <div v-if="side === 'opponent'" class="morale-rail">
        <button class="faction-effect-trigger" @click.stop="factionOpen = true; factionMinimized = false">阵营效果</button>
        <span>士气</span>
        <div class="morale-stack">
          <button v-for="(morale, index) in displayMoraleSlots" :key="morale?.instanceId ?? `unused-${index}`" type="button" class="morale-orb"
            :class="[moraleState(morale), { payable: paymentChoiceIds?.includes(morale?.instanceId ?? ''), selected: paymentSelectedIds?.includes(morale?.instanceId ?? '') }]"
            :title="moraleLabel(morale)" :aria-disabled="!paymentChoiceIds?.includes(morale?.instanceId ?? '')"
            @click.stop="morale && selectMoralePayment(morale.instanceId)">
            <img v-if="morale?.isGodPower" class="god-power-logo" :src="godPowerLogoUrl" alt="神力" />
            <img v-else-if="morale && factionLogoUrls[player.faction]" :src="factionLogoUrls[player.faction]" :alt="player.faction" />
          </button>
        </div>
        <b class="morale-count" :title="`当前活跃士气 ${activeMorale} / 当前士气上限 ${currentMoraleLimit}`">{{ activeMorale }}/{{ currentMoraleLimit }}</b>
      </div>

      <div class="formation" data-l12-zone="field">
        <template v-for="row in (side === 'opponent' ? [1, 0] : [0, 1])" :key="row">
          <div v-for="slot in [0,1,2]" :key="slot" class="formation-slot" role="button" tabindex="0"
            :class="{
              targetable: Boolean(player.field[row][slot]) && targetableIds?.includes(player.field[row][slot]!.instanceId) && (selectionMode || (!controllable && attackMode)),
              'prompt-selected': selectedTargetIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              available: (!player.field[row][slot] && promptSlotIds?.includes(`${row}:${slot}`)) || isPlacementDestination(row, player.field[row][slot]) || (controllable && isMoveTarget(row, slot)),
              source: selectedId === player.field[row][slot]?.instanceId,
              'combat-attacker': combatAttackerId === player.field[row][slot]?.instanceId,
              'combat-target': combatTargetId === player.field[row][slot]?.instanceId,
              'payment-resource': paymentChoiceIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              'payment-selected': paymentSelectedIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              'resource-ready': controllable && player.faction === 'taiyangcheng' && player.field[row][slot]?.cardId === 'S01-0212' && !player.field[row][slot]?.tapped,
              [counterState(player.field[row][slot])]: Boolean(counterState(player.field[row][slot]))
            }"
            @click="handleSlot(row, slot, player.field[row][slot])" @keyup.enter="handleSlot(row, slot, player.field[row][slot])">
            <template v-if="player.field[row][slot]">
              <div v-if="canUseAbilities(player.field[row][slot]!) && selectedId === player.field[row][slot]!.instanceId && actionsEnabled && !attackMode && !moveMode && !freeMoveMode && !cavalryMoveMode"
                class="card-context-actions field-actions">
                <button v-if="canAttack(player.field[row][slot]!, row)" :class="{ active: attackMode }"
                  @click.stop="emit('cardAction', 'attack', player.field[row][slot]!)">{{ attackMode ? '选择目标' : '进攻' }}</button>
                <button v-if="canMove(player.field[row][slot]!, row, slot)" :class="{ active: moveMode }"
                  @click.stop="emit('cardAction', 'move', player.field[row][slot]!)">{{ moveMode ? '选择位置' : '移动' }}</button>
                <button v-if="canFreeMove(player.field[row][slot]!, row, slot)" :class="{ active: freeMoveMode }"
                  @click.stop="emit('cardAction', 'freeMove', player.field[row][slot]!)">{{ freeMoveMode ? '选择前后位置' : '免费位移' }}</button>
                <button v-if="canCavalryMove(player.field[row][slot]!)" :class="{ active: cavalryMoveMode }"
                  @click.stop="emit('cardAction', 'cavalryMove', player.field[row][slot]!)">{{ cavalryMoveMode ? '选择任意位置' : '骑兵位移' }}</button>
                <button v-if="canTrial(player.field[row][slot]!)"
                  @click.stop="emit('ability', player.field[row][slot]!, 'trialAdvance')">试炼</button>
                <button v-if="modalAbilities(player.field[row][slot]!).length"
                  @click.stop="beginCardAbility(player.field[row][slot]!)">发动</button>
              </div>
              <CardTile :card="hiddenRevealCard?.instanceId === player.field[row][slot]!.instanceId ? hiddenRevealCard : player.field[row][slot]!"
                :selected="selectedId === player.field[row][slot]!.instanceId"
                @focus-card="emit('focus', $event)"
                @mouseenter="emit('focus', hiddenRevealCard?.instanceId === player.field[row][slot]!.instanceId ? hiddenRevealCard : player.field[row][slot]!)" />
            </template>
            <span v-else>{{ row === 0 ? '前排' : '后排' }} {{ slot + 1 }}</span>
          </div>
        </template>
      </div>

      <div v-if="side === 'my'" class="morale-rail">
        <button class="faction-effect-trigger" @click.stop="factionOpen = true; factionMinimized = false">阵营效果</button>
        <span>士气</span>
        <div class="morale-stack">
          <button v-for="(morale, index) in displayMoraleSlots" :key="morale?.instanceId ?? `unused-${index}`" type="button" class="morale-orb"
            :class="[moraleState(morale), { payable: paymentChoiceIds?.includes(morale?.instanceId ?? ''), selected: paymentSelectedIds?.includes(morale?.instanceId ?? '') }]"
            :title="moraleLabel(morale)" :aria-disabled="!paymentChoiceIds?.includes(morale?.instanceId ?? '')"
            @click.stop="morale && selectMoralePayment(morale.instanceId)">
            <img v-if="morale?.isGodPower" class="god-power-logo" :src="godPowerLogoUrl" alt="神力" />
            <img v-else-if="morale && factionLogoUrls[player.faction]" :src="factionLogoUrls[player.faction]" :alt="player.faction" />
          </button>
        </div>
        <b class="morale-count" :title="`当前活跃士气 ${activeMorale} / 当前士气上限 ${currentMoraleLimit}`">{{ activeMorale }}/{{ currentMoraleLimit }}</b>
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
          <p v-if="!factionActions.length">{{ player.factionEffect?.effectText || '暂无效果文字' }}</p>
          <div v-if="factionActions.length" class="faction-effect-actions">
            <button v-for="entry in factionActions" :key="entry.id"
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
          <p v-if="!activeAbilities(abilityCardOpen).length">{{ abilityCardOpen.effectText || '暂无效果文字' }}</p>
          <div class="faction-effect-actions">
            <button v-for="entry in activeAbilities(abilityCardOpen)" :key="entry.id"
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
.formation-slot.combat-attacker{z-index:8;border-color:#d35a61!important;box-shadow:0 0 0 3px #d35a61,0 0 24px rgba(211,90,97,.72)!important}
.formation-slot.combat-target,.mini-master.combat-target{z-index:8;border-color:#e0b85a!important;box-shadow:0 0 0 3px #e0b85a,0 0 24px rgba(224,184,90,.7)!important}
.formation-slot.combat-target :deep(.card-tile),.mini-master.combat-target{animation:l12-combat-target-cue .3s ease-out both}.card-power{transition:background-color .16s,color .16s,filter .16s}
@keyframes l12-combat-target-cue{0%,100%{filter:none}45%{filter:brightness(1.22)}}
@media(prefers-reduced-motion:reduce){.formation-slot.combat-target :deep(.card-tile),.mini-master.combat-target{animation:none}}
.formation-slot.payment-resource{z-index:9;border-color:#52d58a!important;box-shadow:0 0 0 2px #52d58a,0 0 18px rgba(82,213,138,.55)!important;cursor:pointer}.formation-slot.payment-selected{border-color:#f1c75b!important;box-shadow:0 0 0 3px #f1c75b,0 0 22px rgba(241,199,91,.7)!important}
.formation-slot.resource-ready:not(.payment-resource):not(.combat-attacker):not(.combat-target){border-color:#8cdbad;box-shadow:0 0 0 1px rgba(140,219,173,.72),0 0 10px rgba(82,213,138,.28)}
.formation-slot.prompt-selected{z-index:10;border-color:#f1c75b!important;box-shadow:0 0 0 3px #f1c75b,0 0 22px rgba(241,199,91,.68)!important}.formation-slot.prompt-selected::after{content:'已选择';position:absolute;z-index:12;right:4px;top:4px;padding:3px 6px;background:#f1c75b;color:#15120a;font-size:8px;font-weight:900}
.morale-orb{box-sizing:border-box;width:22px;height:22px;min-width:22px;padding:0;border:1px solid #7d8581;border-radius:50%;display:grid;place-items:center;overflow:hidden;background:#151a1a;transition:filter .16s,box-shadow .16s,border-color .16s}.morale-orb img{width:14px;height:14px;object-fit:contain}.morale-orb img.god-power-logo{filter:sepia(1) saturate(3.2) hue-rotate(352deg) brightness(1.18)}.morale-orb.active-morale{background:var(--faction-morale-active,#b4b2af);border-color:var(--faction-morale-border,#eee);box-shadow:inset 0 0 0 1px rgba(255,255,255,.36),0 0 6px color-mix(in srgb,var(--faction-morale-active,#b4b2af) 76%,transparent);filter:saturate(1.15) brightness(1.1)}.active-turn .morale-orb.active-morale{box-shadow:inset 0 0 0 1px rgba(255,255,255,.52),0 0 11px color-mix(in srgb,var(--faction-morale-active,#b4b2af) 92%,transparent);filter:saturate(1.25) brightness(1.2)}.morale-orb.rested-morale{background:var(--faction-morale-rested,#555);border-color:#4d5350;box-shadow:inset 0 0 0 3px rgba(0,0,0,.38);filter:saturate(.35) brightness(.52)}.morale-orb.active-god-power{background:#0091be;border-color:#f4dda1;box-shadow:inset 0 0 0 1px rgba(255,255,255,.35),0 0 9px rgba(0,145,190,.72);filter:saturate(1.18) brightness(1.12)}.active-turn .morale-orb.active-god-power{box-shadow:inset 0 0 0 1px rgba(255,255,255,.55),0 0 13px rgba(0,174,222,.9);filter:saturate(1.25) brightness(1.2)}.morale-orb.rested-god-power{background:#264c57;border-color:#7e7459;box-shadow:inset 0 0 0 3px rgba(0,0,0,.35);filter:saturate(.48) brightness(.56)}.morale-orb.unused{opacity:.25}.morale-orb.payable{cursor:pointer;border-color:#72e29f;box-shadow:0 0 9px rgba(82,213,138,.75)}.morale-orb.selected{border:3px solid #fff0a0;box-shadow:0 0 12px #f1c75b}.morale-orb:disabled:not(.payable){cursor:default}
.faction-tianting{--faction-morale-active:#dbbc00;--faction-morale-rested:#665a08;--faction-morale-border:#fff0a0}.faction-otherworld{--faction-morale-active:#31873f;--faction-morale-rested:#173e20;--faction-morale-border:#9be5a7}.faction-gaotianyuan{--faction-morale-active:#db0d17;--faction-morale-rested:#681118;--faction-morale-border:#ffacb0}.faction-asgard{--faction-morale-active:#342f2f;--faction-morale-rested:#1c1919;--faction-morale-border:#b9aeae}.faction-taiyangcheng{--faction-morale-active:#74227e;--faction-morale-rested:#38123d;--faction-morale-border:#dfa3e6}.faction-universal{--faction-morale-active:#b4b2af;--faction-morale-rested:#555451;--faction-morale-border:#f0efeb}.faction-olympus{--faction-morale-active:#075b76;--faction-morale-rested:#173844;--faction-morale-border:#86d7ee}
.master-column{position:relative;display:grid;align-content:start;justify-items:center;gap:5px;min-width:88px}.master-column .mini-master{position:relative;inset:auto}.master-marker-track{position:absolute;z-index:9;left:8px;top:-39px;display:flex;width:178px;height:32px;align-items:center;justify-content:flex-start;gap:5px;pointer-events:auto}.side-opponent .master-marker-track{top:auto;bottom:-39px}.master-marker-track.canopic{justify-content:space-between;gap:3px}.special-lane{display:none;position:absolute;z-index:6;left:188px;right:4px;top:0;bottom:0;pointer-events:none}.special-lane.visible{display:grid;align-content:center;justify-items:center}.trial-zone{position:relative;z-index:6;display:grid;gap:6px;width:112px;pointer-events:auto}.trial-card{position:relative;width:112px;height:auto;aspect-ratio:1752/1255;padding:0;border:1px solid #8dc6b2;background:#080b0b;overflow:hidden;box-shadow:0 6px 16px #000}.trial-card .l12-card-image,.trial-card-back{width:100%;height:100%;object-fit:contain;background:#080b0b}.trial-card.inactive .l12-card-image{filter:grayscale(.85) brightness(.52)}.trial-card.concealed .l12-card-image{filter:none}.trial-card b{position:absolute;left:50%;top:50%;display:grid;min-width:30px;height:30px;place-items:center;padding:0 6px;border:2px solid #79c889;border-radius:50%;background:#102e17ed;color:#fff;font-size:16px;box-shadow:0 0 11px rgba(49,135,63,.82);transform:translate(-50%,-50%)}.rune-orb{width:32px;height:32px;min-width:32px;padding:0;border:1px solid #596661;border-radius:50%;overflow:hidden;background:#111;filter:grayscale(1) brightness(.38)}.rune-orb.active{border-color:#80d69c;filter:none;box-shadow:0 0 8px rgba(49,135,63,.7)}.rune-orb.payable{cursor:pointer;box-shadow:0 0 0 2px #75e0a1,0 0 13px rgba(49,135,63,.9)}.rune-orb.selected{border-color:#fff3bd;box-shadow:0 0 0 3px #d8b34d,0 0 15px rgba(216,179,77,.95)}.rune-orb:disabled{cursor:default;opacity:1}.rune-orb img{width:100%;height:100%;object-fit:cover;object-position:center 14%;transform:scale(1.1)}
.canopic-orb{width:32px;height:32px;min-width:32px;padding:0;overflow:hidden;border:1px solid #63555a;border-radius:50%;background:#090a0b;filter:grayscale(1) brightness(.3);cursor:pointer}.canopic-orb img{width:100%;height:100%;object-fit:cover;object-position:center 14%;transform:scale(1.12)}.canopic-orb.completed{border-color:#d0aa52;filter:none;box-shadow:0 0 7px rgba(208,170,82,.65)}.canopic-orb.activatable{border-color:#6ee2a0;box-shadow:0 0 9px rgba(82,213,138,.8);animation:canopic-ready 1.25s ease-in-out infinite alternate}@keyframes canopic-ready{to{transform:translateY(-2px);filter:brightness(1.18)}}
.value-badge{display:grid!important;min-width:25px!important;height:22px!important;place-items:center!important;padding:0 6px!important;border:1px solid #f2f0e6!important;border-radius:2px!important;background:#090b0d!important;color:#fff!important;box-shadow:0 2px 0 #000,0 0 0 1px rgba(0,0,0,.65)!important;font-weight:900!important;line-height:1!important}.value-badge small{margin-left:1px;color:#bfc3c0;font-size:.62em}.pile .pile-count{position:absolute;z-index:8;right:3px;top:3px}.mini-master .master-health{position:absolute;z-index:8;right:3px;bottom:3px;display:inline-flex!important;width:max-content;min-width:44px!important;align-items:center;justify-content:center;white-space:nowrap}.master-protection-icon{position:absolute;z-index:9;left:4px;top:4px;display:grid;width:18px;height:18px;place-items:center;border:1px solid #75c79c;border-radius:2px;background:rgba(8,11,12,.94);color:#a4e7bd;font-size:11px;font-style:normal;line-height:1}.morale-count{display:grid;min-width:42px;height:24px;place-items:center;padding:0 7px;border:1px solid #cbc6b8;background:#080a0b;color:#fff;box-shadow:0 2px 0 #000;font-size:11px;line-height:1;white-space:nowrap}.morale-orb[aria-disabled="true"]{cursor:default}.morale-orb.active-morale[aria-disabled="true"],.morale-orb.active-god-power[aria-disabled="true"]{opacity:1}
</style>

<style scoped>
.extra-relic{position:absolute;z-index:2;inset:0;width:100%;height:100%;padding:0;border:0;background:transparent}.extra-relic :deep(.card-tile){width:100%;height:100%}
.trial-card.own-concealed .l12-card-image{filter:grayscale(.85) brightness(.52)}
.formation-slot.counter-dormant :deep(.card-tile),.formation-slot.hidden-dormant :deep(.card-tile){filter:brightness(.4) saturate(.55)}.formation-slot.counter-ready :deep(.card-tile){filter:brightness(1.08);box-shadow:0 0 0 2px #71e197,0 0 17px rgba(70,220,126,.7)}
.faction-effect-trigger{padding:3px 7px;border:1px solid rgba(238,238,228,.42);border-radius:1px;background:#111718;color:#e8e5dc;font-size:8px;font-weight:900;white-space:nowrap}.faction-effect-trigger:hover{border-color:var(--cyan);color:#fff}
.faction-effect-overlay{position:fixed;z-index:1100;inset:0;display:grid;place-items:center;background:rgba(2,4,5,.78);backdrop-filter:blur(7px)}
.faction-effect-dialog{position:relative;width:min(650px,calc(100vw - 32px));display:grid;grid-template-columns:220px 1fr;gap:24px;padding:22px;border:1px solid rgba(238,238,228,.7);background:linear-gradient(145deg,#171c1d,#07090a);box-shadow:0 24px 70px #000}
.faction-effect-dialog>.l12-card-image{width:220px;height:308px;background:#050708}
.faction-effect-dialog small{color:var(--cyan);font-size:9px;letter-spacing:.14em}.faction-effect-dialog h2{margin:8px 0 14px;color:#f0ede4;font-size:25px}.faction-effect-dialog p{color:#d4d5cf;font-size:13px;font-weight:800;line-height:1.85;white-space:pre-wrap}.faction-close,.faction-minimize{position:absolute;top:9px;width:30px;height:30px;border:1px solid #777;background:#111;color:#eee;font-size:20px}.faction-close{right:9px}.faction-minimize{right:47px}.faction-effect-actions{display:grid;gap:8px;margin-top:20px}.faction-effect-actions button{padding:11px;border:1px solid var(--cyan);background:rgba(40,133,140,.2);color:#fff;font-weight:900;text-align:left}.faction-effect-actions button:disabled{cursor:not-allowed;border-color:#4a504e;background:#202423;color:#737a77;filter:saturate(.25)}.faction-action-hint{display:block;margin-top:18px;color:#777f7c;font-size:10px}.faction-effect-overlay.minimized{z-index:2000;inset:auto 16px 66px auto;display:block;background:transparent;backdrop-filter:none;pointer-events:none}.faction-minimized-bar{display:block;pointer-events:auto}.faction-minimized-bar button{padding:6px 10px;border:1px solid var(--cyan);background:#174e54;color:#fff;box-shadow:0 12px 35px #000}
@media(max-width:650px){.faction-effect-dialog{grid-template-columns:1fr}.faction-effect-dialog>.l12-card-image{width:140px;height:196px;margin:auto}.faction-effect-overlay.minimized{right:10px;bottom:60px}}
</style>
