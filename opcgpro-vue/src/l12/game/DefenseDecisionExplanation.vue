<script setup lang="ts">
import { computed } from 'vue'
import type { GameState, PlayerView } from '../types'

const props = defineProps<{
  game: GameState; me: PlayerView; defenseIds: string[]; supportIds: string[]; defenseTargetType: string | null; compact?: boolean
}>()
const selectedBlockers = computed(() => (props.me.hand ?? []).filter(card => props.defenseIds.includes(card.instanceId)))
const selectedBlockTroops = computed(() => selectedBlockers.value.reduce((total, card) => total + card.troops, 0))
const selectedSupporters = computed(() => props.me.field[1].flatMap((card, slot) => card && props.supportIds.includes(card.instanceId)
  ? [{ card, slot }] : []))
const selectedSupportTroops = computed(() => selectedSupporters.value.reduce((total, item) => total + item.card.troops, 0))
const targetTroops = computed(() => props.me.field.flat().find(card => card?.instanceId === props.game.pendingDefense?.target.instanceId)?.troops ?? 0)
const attackValue = computed(() => props.game.pendingDefense?.attackValue ?? 0)
const attackValueLabel = computed(() => attackValue.value > 0 ? String(attackValue.value) : '待服务器确认')
const slotNames = ['左格', '中格', '右格']
</script>

<template>
  <div class="defense-decision-explanation">
    <p v-if="compact && defenseTargetType === 'master'" class="defense-explanation">已选 {{ selectedBlockers.length }} 张手牌军团，兵力 {{ selectedBlockTroops }}／需达到进攻值 {{ attackValueLabel }}；成功抵挡时弃置所选军团，不抵挡立即提交。<span v-if="game.pendingDefense?.richardDefenseTaxActive">另需按后续提示弃置 1 张手牌，否则抵挡无效。</span></p>
    <p v-else-if="compact && defenseTargetType === 'legion'" class="defense-explanation">已选 {{ selectedSupporters.length }} 张后排军团，合计兵力 {{ targetTroops + selectedSupportTroops }}／需达到进攻值 {{ attackValueLabel }}；成功支援时所选军团阵亡，不支援立即提交。<span v-if="game.pendingDefense?.richardDefenseTaxActive">另需按后续提示弃置 1 张手牌，否则支援无效。</span></p>
    <template v-else-if="defenseTargetType === 'master'">
      <p class="defense-explanation">从我方手牌选择军团抵挡主宰受到的进攻。所选军团总兵力须达到本次进攻值 {{ attackValueLabel }}。</p>
      <p class="defense-selection">已选 {{ selectedBlockers.length }} 张手牌军团，合计 {{ selectedBlockTroops }} 兵力<span v-if="selectedBlockers.length">：{{ selectedBlockers.map(card => `〈${card.name}〉`).join('、') }}</span>。若抵挡结算成功，将弃置这些军团。</p>
      <p v-if="game.pendingDefense?.richardDefenseTaxActive" class="defense-extra-cost">确认抵挡后还会要求额外弃置 1 张手牌；不支付则本次抵挡无效。</p>
      <p class="defense-consequence">确认抵挡会提交所选军团；不抵挡会立即提交放弃抵挡，继续结算本次进攻。调整选择只需再次点击手牌。</p>
    </template>
    <template v-else-if="defenseTargetType === 'legion'">
      <p class="defense-explanation">选择我方后排军团支援被进攻军团。防守军团 {{ targetTroops }} 兵力＋已选支援 {{ selectedSupportTroops }} 兵力，须达到本次进攻值 {{ attackValueLabel }}。</p>
      <p class="defense-selection">已选 {{ selectedSupporters.length }} 张支援军团<span v-if="selectedSupporters.length">：{{ selectedSupporters.map(({ card, slot }) => `〈${card.name}〉（我方后排${slotNames[slot]}）`).join('、') }}</span>。若支援结算成功，所选支援军团阵亡；交战双方不损失兵力。</p>
      <p v-if="game.pendingDefense?.richardDefenseTaxActive" class="defense-extra-cost">确认支援后还会要求额外弃置 1 张手牌；不支付则本次支援无效。</p>
      <p class="defense-consequence">确认支援会提交所选军团；不支援会立即提交放弃支援，继续结算本次进攻。调整选择只需再次点击军团。</p>
    </template>
  </div>
</template>

<style scoped>
.defense-decision-explanation{display:grid;gap:6px;max-width:100%}
.defense-decision-explanation p{margin:0;max-width:100%;overflow-wrap:anywhere;font-size:var(--l12-board-copy,13px);line-height:1.4}
.defense-explanation{color:#f5e7c5;font-weight:800}
.defense-selection{color:#d7e9e6}
.defense-extra-cost{color:#f4c983}
.defense-consequence{color:#b8c4c2}
</style>
