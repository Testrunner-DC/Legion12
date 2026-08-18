<script setup lang="ts">
import { computed, ref } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card, PlayerView } from '../types'

const props = defineProps<{
  player: PlayerView
  side: 'my' | 'opponent'
  selectedId?: string | null
  attackMode?: boolean
  moveMode?: boolean
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
  cardAction: [action: 'attack' | 'move' | 'cavalryMove', card: Card]
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
function moraleState(index: number) {
  const card = props.player.morale[index]
  if (!card) return 'unused'
  if (card.isGodPower) return card.tapped ? 'rested-god-power' : 'active-god-power'
  return card.tapped ? 'rested-morale' : 'active-morale'
}
function moraleLabel(index: number) {
  const state = moraleState(index)
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
const activeGodPower = computed(() => props.player.morale.filter(card => card.isGodPower && !card.tapped).length)
const canopicComplete = computed(() => (props.player.specialZones?.canopicTrack?.filter(card => card.completed).length ?? 0) >= 5)
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
  if (props.attackableIds) return props.attackableIds.includes(card.instanceId)
  return Boolean(props.actionsEnabled && !card.cannotAttack && (row === 0 || card.hasRangeBonus) && !card.tapped && !card.hidden && (card.summonRound < (props.round ?? 0) || card.hasCharge))
}
function isCounterTactic(card: Card | null) {
  return card?.cardType === 'counter-tactic'
    || ['S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021', 'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420'].includes(card?.cardId ?? '')
}
function counterState(card: Card | null) {
  if (props.side === 'my' && card?.hidden && card.cardId === 'S01-0415') return 'hidden-dormant'
  if (props.side !== 'my' || !isCounterTactic(card) || !card?.hidden) return ''
  return props.responsePlayableIds?.includes(card.instanceId) ? 'counter-ready' : 'counter-dormant'
}
function isPlacementDestination(row: number, card: Card | null) {
  if (props.side !== 'my' || !props.placementMode || (props.placementRow !== null && props.placementRow !== undefined && props.placementRow !== row)) return false
  return !card || Boolean(props.placementCanReplaceCounter && row === 1 && isCounterTactic(card))
}
function canMove(card: Card, row: number, slot: number) {
  if (!props.actionsEnabled || card.tapped || card.hidden || spendableMorale.value < 1) return false
  return [[row - 1, slot], [row + 1, slot], [row, slot - 1], [row, slot + 1]]
    .some(([nextRow, nextSlot]) => nextRow >= 0 && nextRow < 2 && nextSlot >= 0 && nextSlot < 3 && !props.player.field[nextRow][nextSlot])
}
function canCavalryMove(card: Card) {
  if (!props.actionsEnabled || card.tapped || card.hidden || card.profession !== '骑兵') return false
  if (card.lastCavalryMoveTurn === props.turnSerial) return false
  return props.player.field.some(row => row.some(slotCard => !slotCard))
}
function isMoveTarget(row: number, slot: number) {
  if ((!props.moveMode && !props.cavalryMoveMode) || !props.selectedId || props.player.field[row][slot]) return false
  for (let sourceRow = 0; sourceRow < 2; sourceRow++) {
    for (let sourceSlot = 0; sourceSlot < 3; sourceSlot++) {
      if (props.player.field[sourceRow][sourceSlot]?.instanceId !== props.selectedId) continue
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
function selectZoneCard(card: Card) {
  emit('focus', card)
  emit('selectCard', card)
  if (props.side === 'my' && abilities(card).length) {
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
function beginCardAbility(card: Card) {
  const entries = activeAbilities(card)
  if (!entries.length) return
  if (entries.length === 1 && entries[0].enabled !== false && !entries[0].triggerOnly) { emit('ability', card, entries[0].id); return }
  abilityCardOpen.value = card
  abilityCardMinimized.value = false
}
</script>

<template>
  <section class="l12-player-mat" :class="[`side-${side}`, { 'active-turn': active }]">
    <div class="commander-zone">
      <div class="master-column">
        <div v-if="player.specialZones?.canopicTrack?.length" class="canopic-track" aria-label="卡诺匹斯特殊胜利进度">
          <button v-for="card in player.specialZones.canopicTrack" :key="card.cardId" type="button"
            class="canopic-orb" :class="{ completed: card.completed, activatable: side === 'my' && canopicComplete }"
            :title="`${card.name}${card.completed ? '（已置入）' : '（未完成）'}`"
            @mouseenter="emit('focus', card)" @focus="emit('focus', card)"
            @click.stop="emit('focus', card); side === 'my' && canopicComplete && emit('ability', masterCard, 'isisVictory')">
            <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.name" />
          </button>
        </div>
        <button class="mini-master" :class="{ targetable: side === 'opponent' && attackMode && masterTargetable, tapped: player.master.tapped, 'combat-target': combatTargetMaster }"
          @mouseenter="emit('focus', masterCard)" @focus="emit('focus', masterCard)" @click="emit('master')">
          <img v-if="player.master.masterImageUrl" :src="player.master.masterImageUrl" />
          <span>{{ player.master.masterName }}</span>
          <b class="value-badge master-health">{{ player.master.hp }}<small>/{{ player.master.maxHp }}</small></b>
        </button>
        <div v-if="player.faction === 'otherworld'" class="rune-zone" aria-label="符文区">
          <span v-for="index in 3" :key="index" class="rune-orb" :class="{ active: index <= (player.specialZones?.runes ?? 0) }" :title="index <= (player.specialZones?.runes ?? 0) ? '可用符文' : '未获得符文'">
            <img src="/cards/faces/彼界/符文.png" alt="符文" />
          </span>
        </div>
      </div>

      <div class="relic-zone" @mouseenter="player.relic && emit('focus', player.relic)" @click="player.relic && selectZoneCard(player.relic)">
        <CardTile v-if="player.relic" :card="player.relic" />
        <div v-else class="zone-placeholder"><i>✦</i><span>圣物区</span></div>
        <button v-for="(relic, index) in player.extraRelics ?? []" :key="relic.instanceId" class="extra-relic"
          :style="{ transform: `translate(${(index + 1) * 12}px, ${(index + 1) * 7}px)` }"
          @mouseenter="emit('focus', relic)" @click.stop="selectZoneCard(relic)">
          <CardTile :card="relic" :selected="selectedId === relic.instanceId" />
        </button>
      </div>
      <div class="special-lane" :class="{ visible: player.specialZones && (player.faction === 'olympus' || player.faction === 'otherworld') }">
        <div class="faction-side-channel" :class="{ visible: player.specialZones && (player.faction === 'olympus' || player.faction === 'otherworld') }">
          <template v-if="player.faction === 'olympus'">
            <span>活跃神力</span><b>{{ activeGodPower }}</b>
          </template>
          <template v-else-if="player.faction === 'otherworld'">
            <span>试炼值</span><b>{{ player.specialZones?.trialLevel ?? 0 }}/8</b>
          </template>
        </div>
        <div v-if="player.faction === 'otherworld' && player.specialZones?.trials?.length" class="trial-zone" :class="{ opponent: side === 'opponent' }">
          <button v-for="(trial, index) in player.specialZones.trials" :key="trial.instanceId" type="button" class="trial-card"
            :class="{ concealed: trial.hidden, inactive: !trial.hidden && !trial.trialCompleted }"
            :style="{ transform: `translateY(${index * 14}px)` }" :title="trial.hidden ? '对方未揭示的试炼' : trial.name"
            @mouseenter="!trial.hidden && emit('focus', trial)" @focus="!trial.hidden && emit('focus', trial)" @click.stop="!trial.hidden && emit('focus', trial)">
            <img :src="trial.imageUrl || '/assets/l12/trial-back.png'" :alt="trial.hidden ? '试炼牌背' : trial.name" />
            <b v-if="!trial.hidden">{{ trial.trialProgress ?? player.specialZones?.trialLevel ?? 0 }}/8</b>
          </button>
        </div>
      </div>
    </div>

    <div class="battle-zone">
      <div v-if="side === 'opponent'" class="morale-rail">
        <button class="faction-effect-trigger" @click.stop="factionOpen = true; factionMinimized = false">阵营效果</button>
        <span>士气</span>
        <div class="morale-stack">
          <button v-for="index in moraleLimit" :key="index" type="button" class="morale-orb"
            :class="[moraleState(index - 1), { payable: paymentChoiceIds?.includes(player.morale[index - 1]?.instanceId ?? ''), selected: paymentSelectedIds?.includes(player.morale[index - 1]?.instanceId ?? '') }]"
            :title="moraleLabel(index - 1)" :disabled="!paymentChoiceIds?.includes(player.morale[index - 1]?.instanceId ?? '')"
            @click.stop="player.morale[index - 1] && selectMoralePayment(player.morale[index - 1].instanceId)">
            <img v-if="player.morale[index - 1]?.isGodPower" src="/brand/faction-olympus.png" alt="神力" />
          </button>
        </div>
        <b :title="`当前活跃士气 ${activeMorale} / 当前士气上限 ${currentMoraleLimit}`">{{ activeMorale }}/{{ currentMoraleLimit }}</b>
      </div>

      <div class="formation">
        <template v-for="row in (side === 'opponent' ? [1, 0] : [0, 1])" :key="row">
          <div v-for="slot in [0,1,2]" :key="slot" class="formation-slot" role="button" tabindex="0"
            :class="{
              targetable: Boolean(player.field[row][slot]) && targetableIds?.includes(player.field[row][slot]!.instanceId) && (selectionMode || (side === 'opponent' && attackMode)),
              'prompt-selected': selectedTargetIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              available: side === 'my' && ((!player.field[row][slot] && promptSlotIds?.includes(`${row}:${slot}`)) || isPlacementDestination(row, player.field[row][slot]) || isMoveTarget(row, slot)),
              source: selectedId === player.field[row][slot]?.instanceId,
              'combat-attacker': combatAttackerId === player.field[row][slot]?.instanceId,
              'combat-target': combatTargetId === player.field[row][slot]?.instanceId,
              'payment-resource': paymentChoiceIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              'payment-selected': paymentSelectedIds?.includes(player.field[row][slot]?.instanceId ?? ''),
              [counterState(player.field[row][slot])]: Boolean(counterState(player.field[row][slot]))
            }"
            @click="handleSlot(row, slot, player.field[row][slot])" @keyup.enter="handleSlot(row, slot, player.field[row][slot])">
            <template v-if="player.field[row][slot]">
              <div v-if="side === 'my' && selectedId === player.field[row][slot]!.instanceId && actionsEnabled"
                class="card-context-actions field-actions">
                <button v-if="canAttack(player.field[row][slot]!, row)" :class="{ active: attackMode }"
                  @click.stop="emit('cardAction', 'attack', player.field[row][slot]!)">{{ attackMode ? '选择目标' : '进攻' }}</button>
                <button v-if="canMove(player.field[row][slot]!, row, slot)" :class="{ active: moveMode }"
                  @click.stop="emit('cardAction', 'move', player.field[row][slot]!)">{{ moveMode ? '选择位置' : '移动' }}</button>
                <button v-if="canCavalryMove(player.field[row][slot]!)" :class="{ active: cavalryMoveMode }"
                  @click.stop="emit('cardAction', 'cavalryMove', player.field[row][slot]!)">{{ cavalryMoveMode ? '选择任意位置' : '骑兵位移' }}</button>
                <button v-if="activeAbilities(player.field[row][slot]!).length"
                  @click.stop="beginCardAbility(player.field[row][slot]!)">发动</button>
              </div>
              <CardTile :card="hiddenRevealCard?.instanceId === player.field[row][slot]!.instanceId ? hiddenRevealCard : player.field[row][slot]!"
                :selected="selectedId === player.field[row][slot]!.instanceId"
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
          <button v-for="index in moraleLimit" :key="index" type="button" class="morale-orb"
            :class="[moraleState(index - 1), { payable: paymentChoiceIds?.includes(player.morale[index - 1]?.instanceId ?? ''), selected: paymentSelectedIds?.includes(player.morale[index - 1]?.instanceId ?? '') }]"
            :title="moraleLabel(index - 1)" :disabled="!paymentChoiceIds?.includes(player.morale[index - 1]?.instanceId ?? '')"
            @click.stop="player.morale[index - 1] && selectMoralePayment(player.morale[index - 1].instanceId)">
            <img v-if="player.morale[index - 1]?.isGodPower" src="/brand/faction-olympus.png" alt="神力" />
          </button>
        </div>
        <b :title="`当前活跃士气 ${activeMorale} / 当前士气上限 ${currentMoraleLimit}`">{{ activeMorale }}/{{ currentMoraleLimit }}</b>
      </div>
    </div>

    <div class="mat-piles">
      <div class="pile deck">
        <div class="pile-card card-back"><i>XII</i></div>
        <b class="value-badge pile-count">{{ player.libraryCount }}</b><span>牌库</span>
      </div>
      <button class="pile graveyard" @click="emit('graveyard', player.playerIndex)">
        <div class="pile-card">
          <img v-if="topGraveyard?.imageUrl" :src="topGraveyard.imageUrl" :alt="topGraveyard.name" />
          <i v-else>墓</i>
        </div>
        <b class="value-badge pile-count">{{ player.graveyard?.length ?? player.graveyardCount ?? 0 }}</b><span>墓地</span>
      </button>
    </div>
  </section>

  <Teleport to="body">
    <div v-if="factionOpen" class="faction-effect-overlay" :class="{ minimized: factionMinimized }" @click.self="factionOpen = false">
      <section v-if="factionMinimized" class="faction-minimized-bar">
        <strong>{{ player.factionEffect?.name || '阵营效果' }}</strong>
        <button @click="factionMinimized = false">展开</button>
        <button aria-label="关闭" @click="factionOpen = false">×</button>
      </section>
      <section v-else class="faction-effect-dialog" role="dialog" aria-modal="true">
        <button class="faction-minimize" aria-label="最小化弹框" title="最小化以查看场面" @click="factionMinimized = true">—</button>
        <button class="faction-close" aria-label="关闭" @click="factionOpen = false">×</button>
        <img v-if="player.factionEffect?.imageUrl" :src="player.factionEffect.imageUrl" :alt="player.factionEffect.name" />
        <div>
          <small>{{ side === 'my' ? '我方阵营效果' : '对方阵营效果' }}</small>
          <h2>{{ player.factionEffect?.name || '阵营效果' }}</h2>
          <p v-if="!factionActions.length">{{ player.factionEffect?.effectText || '暂无效果文字' }}</p>
          <div v-if="factionActions.length" class="faction-effect-actions">
            <button v-for="entry in factionActions" :key="entry.id"
              :disabled="side !== 'my' || !actionsEnabled || entry.enabled === false || entry.triggerOnly"
              :title="entry.disabledReason || (entry.triggerOnly ? '仅在触发时点发动' : '')"
              @click="emit('factionAbility', entry.id); factionOpen = false">
              {{ entry.label }}
            </button>
          </div>
          <span v-if="side === 'my' && !actionsEnabled" class="faction-action-hint">仅在我方主要阶段可以发动</span>
        </div>
      </section>
    </div>
  </Teleport>

  <Teleport to="body">
    <div v-if="abilityCardOpen" class="faction-effect-overlay" :class="{ minimized: abilityCardMinimized }" @click.self="abilityCardOpen = null">
      <section v-if="abilityCardMinimized" class="faction-minimized-bar">
        <strong>{{ abilityCardOpen.name }}</strong>
        <button @click="abilityCardMinimized = false">展开</button>
        <button aria-label="关闭" @click="abilityCardOpen = null">×</button>
      </section>
      <section v-else class="faction-effect-dialog" role="dialog" aria-modal="true">
        <button class="faction-minimize" aria-label="最小化弹框" @click="abilityCardMinimized = true">—</button>
        <button class="faction-close" aria-label="关闭" @click="abilityCardOpen = null">×</button>
        <img v-if="abilityCardOpen.imageUrl" :src="abilityCardOpen.imageUrl" :alt="abilityCardOpen.name" @mouseenter="emit('focus', abilityCardOpen)" />
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
.formation-slot.payment-resource{z-index:9;border-color:#52d58a!important;box-shadow:0 0 0 2px #52d58a,0 0 18px rgba(82,213,138,.55)!important;cursor:pointer}.formation-slot.payment-selected{border-color:#f1c75b!important;box-shadow:0 0 0 3px #f1c75b,0 0 22px rgba(241,199,91,.7)!important}
.formation-slot.prompt-selected{z-index:10;border-color:#f1c75b!important;box-shadow:0 0 0 3px #f1c75b,0 0 22px rgba(241,199,91,.68)!important}.formation-slot.prompt-selected::after{content:'已选择';position:absolute;z-index:12;right:4px;top:4px;padding:3px 6px;background:#f1c75b;color:#15120a;font-size:8px;font-weight:900}
.morale-orb{box-sizing:border-box;width:16px;height:16px;min-width:16px;padding:0;border:1px solid #7d8581;border-radius:50%;display:grid;place-items:center;overflow:hidden;background:#151a1a}.morale-orb img{width:11px;height:11px;object-fit:contain;filter:brightness(0) invert(1)}.morale-orb.active-morale{background:#dbbc00;border-color:#fff0a0}.morale-orb.rested-morale{background:#554f42;border-color:#777;filter:saturate(.45)}.morale-orb.active-god-power{background:#0091be;border-color:#baf4ff;box-shadow:0 0 8px rgba(0,145,190,.55)}.morale-orb.rested-god-power{background:#264c57;border-color:#617c83;filter:saturate(.5) brightness(.7)}.morale-orb.unused{opacity:.25}.morale-orb.payable{cursor:pointer;border-color:#72e29f;box-shadow:0 0 9px rgba(82,213,138,.75)}.morale-orb.selected{border:3px solid #fff0a0;box-shadow:0 0 12px #f1c75b}.morale-orb:disabled:not(.payable){cursor:default}
.master-column{display:grid;align-content:start;justify-items:center;gap:5px;min-width:88px}.master-column .mini-master{position:relative;inset:auto}.special-lane{display:none;position:relative;align-self:stretch;min-width:102px}.special-lane.visible{display:grid;align-content:center;justify-items:center;gap:6px}.special-lane .faction-side-channel{position:static;inset:auto;width:94px;min-height:24px}.trial-zone{position:relative;z-index:6;width:98px;height:75px}.trial-card{position:absolute;inset:0;width:98px;height:61px;padding:0;border:1px solid #8dc6b2;background:#080b0b;overflow:hidden;box-shadow:0 6px 16px #000}.trial-card img{width:100%;height:100%;object-fit:cover}.trial-card.inactive img{filter:grayscale(.85) brightness(.52)}.trial-card.concealed img{filter:none}.trial-card b{position:absolute;right:3px;bottom:3px;padding:2px 5px;background:#090b0be6;color:#fff;font-size:8px}.rune-zone{position:relative;z-index:7;display:flex;justify-content:center;gap:4px;width:100%}.rune-orb{width:22px;height:22px;border:1px solid #596661;border-radius:50%;overflow:hidden;background:#111;filter:grayscale(1) brightness(.38)}.rune-orb.active{border-color:#80d69c;filter:none;box-shadow:0 0 8px rgba(49,135,63,.7)}.rune-orb img{width:100%;height:100%;object-fit:cover}
.canopic-track{position:relative;z-index:8;display:flex;width:100%;justify-content:center;gap:2px}.canopic-orb{width:18px;height:18px;min-width:18px;padding:0;overflow:hidden;border:1px solid #63555a;border-radius:50%;background:#090a0b;filter:grayscale(1) brightness(.3);cursor:pointer}.canopic-orb img{width:100%;height:100%;object-fit:cover;object-position:center 30%}.canopic-orb.completed{border-color:#d0aa52;filter:none;box-shadow:0 0 7px rgba(208,170,82,.65)}.canopic-orb.activatable{border-color:#6ee2a0;box-shadow:0 0 9px rgba(82,213,138,.8);animation:canopic-ready 1.25s ease-in-out infinite alternate}@keyframes canopic-ready{to{transform:translateY(-2px);filter:brightness(1.18)}}
.value-badge{display:grid!important;min-width:25px!important;height:22px!important;place-items:center!important;padding:0 6px!important;border:1px solid #f2f0e6!important;border-radius:2px!important;background:#090b0d!important;color:#fff!important;box-shadow:0 2px 0 #000,0 0 0 1px rgba(0,0,0,.65)!important;font-weight:900!important;line-height:1!important}.value-badge small{margin-left:1px;color:#bfc3c0;font-size:.62em}.pile .pile-count{position:absolute;z-index:8;right:3px;top:3px}.mini-master .master-health{position:absolute;z-index:8;right:3px;bottom:3px}
</style>

<style scoped>
.extra-relic{position:absolute;z-index:2;inset:0;width:100%;height:100%;padding:0;border:0;background:transparent}.extra-relic :deep(.card-tile){width:100%;height:100%}
.formation-slot.counter-dormant :deep(.card-tile),.formation-slot.hidden-dormant :deep(.card-tile){filter:brightness(.4) saturate(.55)}.formation-slot.counter-ready :deep(.card-tile){filter:brightness(1.08);box-shadow:0 0 0 2px #71e197,0 0 17px rgba(70,220,126,.7)}
.faction-effect-trigger{padding:3px 7px;border:1px solid rgba(238,238,228,.42);border-radius:1px;background:#111718;color:#e8e5dc;font-size:8px;font-weight:900;white-space:nowrap}.faction-effect-trigger:hover{border-color:var(--cyan);color:#fff}
.faction-effect-overlay{position:fixed;z-index:1100;inset:0;display:grid;place-items:center;background:rgba(2,4,5,.78);backdrop-filter:blur(7px)}
.faction-effect-dialog{position:relative;width:min(650px,calc(100vw - 32px));display:grid;grid-template-columns:220px 1fr;gap:24px;padding:22px;border:1px solid rgba(238,238,228,.7);background:linear-gradient(145deg,#171c1d,#07090a);box-shadow:0 24px 70px #000}
.faction-effect-dialog>img{width:220px;height:308px;object-fit:contain;background:#050708}
.faction-effect-dialog small{color:var(--cyan);font-size:9px;letter-spacing:.14em}.faction-effect-dialog h2{margin:8px 0 14px;color:#f0ede4;font-size:25px}.faction-effect-dialog p{color:#d4d5cf;font-size:13px;font-weight:800;line-height:1.85;white-space:pre-wrap}.faction-close,.faction-minimize{position:absolute;top:9px;width:30px;height:30px;border:1px solid #777;background:#111;color:#eee;font-size:20px}.faction-close{right:9px}.faction-minimize{right:47px}.faction-effect-actions{display:grid;gap:8px;margin-top:20px}.faction-effect-actions button{padding:11px;border:1px solid var(--cyan);background:rgba(40,133,140,.2);color:#fff;font-weight:900;text-align:left}.faction-effect-actions button:disabled{cursor:not-allowed;border-color:#4a504e;background:#202423;color:#737a77;filter:saturate(.25)}.faction-action-hint{display:block;margin-top:18px;color:#777f7c;font-size:10px}.faction-effect-overlay.minimized{inset:auto 16px 16px auto;display:block;background:transparent;backdrop-filter:none;pointer-events:none}.faction-minimized-bar{display:flex;align-items:center;gap:8px;padding:9px 10px;border:1px solid #ded9cc;background:#0c1112;box-shadow:0 12px 35px #000;pointer-events:auto}.faction-minimized-bar strong{max-width:270px;overflow:hidden;color:#fff;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.faction-minimized-bar button{padding:6px 10px;border:1px solid var(--cyan);background:#174e54;color:#fff}
@media(max-width:650px){.faction-effect-dialog{grid-template-columns:1fr}.faction-effect-dialog>img{width:140px;height:196px;margin:auto}}
</style>
