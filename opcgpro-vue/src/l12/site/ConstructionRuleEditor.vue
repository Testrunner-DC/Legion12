<script setup lang="ts">
import { computed, ref } from 'vue'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import type { DeckCard } from '@/l12/decks'
import type { OperationsCardRestriction } from '@/l12/platform'

const props = withDefaults(defineProps<{ cards: DeckCard[]; modelValue: OperationsCardRestriction[]; allowMasterRules?: boolean }>(), { allowMasterRules: true })
const emit = defineEmits<{ 'update:modelValue': [value: OperationsCardRestriction[]] }>()
const selectedCardId = ref('')
const pickerOpen = ref(false)
const masterId = ref('')
const maxCopies = ref(3)
const reason = ref('')
const byId = computed(() => new Map(props.cards.map(card => [card.id, card])))
const masters = computed(() => props.cards.filter(card => card.cardType === 'master' && card.id !== 'S01-02M2'))
const pickerItems = computed<SingleCardPickerItem[]>(() => props.cards
  .filter(card => !['destruction', 'disaster'].includes(card.cardType))
  .map(card => ({ id: card.id, cardId: card.id, number: card.number, name: card.nameZh, nameZh: card.nameZh,
    cardType: card.cardType, faction: card.faction, product: card.product, cost: card.cost,
    disasterLevel: card.disasterLevel, effect: card.effect, imageUrl: card.imageUrl, cardImageId: card.id,
    detailCard: card })))
function choose(card: SingleCardPickerItem) { selectedCardId.value = card.cardId; pickerOpen.value = false }
function save() {
  if (!selectedCardId.value) return
  const next: OperationsCardRestriction = { cardId: selectedCardId.value, maxCopies: Math.max(0, Math.min(3, maxCopies.value)), reason: reason.value.trim() || undefined, masterId: masterId.value || undefined }
  emit('update:modelValue', [...props.modelValue.filter(item => !(item.cardId === next.cardId && (item.masterId ?? '') === (next.masterId ?? ''))), next])
  reason.value = ''
}
function remove(rule: OperationsCardRestriction) {
  emit('update:modelValue', props.modelValue.filter(item => !(item.cardId === rule.cardId && (item.masterId ?? '') === (rule.masterId ?? ''))))
}
</script>

<template>
  <div class="rule-editor">
    <div class="rule-builder">
      <div class="rule-fields">
        <label>已选卡牌<button class="single-card-trigger" type="button" @click="pickerOpen = true">{{ byId.get(selectedCardId)?.nameZh ? `${byId.get(selectedCardId)?.number} · ${byId.get(selectedCardId)?.nameZh}` : '打开单卡卡查选择' }}</button></label>
        <label v-if="allowMasterRules">适用范围<select v-model="masterId"><option value="">所有主宰</option><option v-for="master in masters" :key="master.id" :value="master.id">{{ master.nameZh }} · {{ master.id }}</option></select></label>
        <label>构筑上限<select v-model.number="maxCopies"><option v-for="value in [0,1,2,3]" :key="value" :value="value">{{ value }}{{ value === 0 ? '（禁用）' : ' 张' }}</option></select></label>
        <label class="reason">说明<input v-model="reason" maxlength="500" placeholder="可选"/></label>
        <button type="button" :disabled="!selectedCardId" @click="save">添加 / 更新规则</button>
      </div>
    </div>
    <SingleCardPicker v-if="pickerOpen" title="选择要设置构筑规则的卡牌" :items="pickerItems" @select="choose" @close="pickerOpen = false"/>
    <div class="rule-list">
      <article v-for="rule in modelValue" :key="`${rule.masterId || '*'}-${rule.cardId}`"><span><b>{{ byId.get(rule.cardId)?.nameZh || rule.cardId }}</b><small>{{ rule.cardId }} · {{ rule.masterId ? `${byId.get(rule.masterId)?.nameZh || rule.masterId} 专属` : '所有主宰' }}</small></span><strong>上限 {{ rule.maxCopies }}</strong><em>{{ rule.reason || '无备注' }}</em><button type="button" @click="remove(rule)">删除</button></article>
      <p v-if="!modelValue.length">尚未设置构筑规则。</p>
    </div>
  </div>
</template>

<style scoped>
.rule-editor,.rule-builder{display:grid;gap:10px}.rule-fields{display:grid;grid-template-columns:1fr 1fr 130px;gap:8px;align-items:end}.rule-fields label{display:grid;gap:5px}.single-card-trigger{min-height:38px;padding:8px 10px;border:1px solid var(--l12-ui-line-strong,#46545d);background:var(--l12-ui-control,#070d12);color:#fff;text-align:left}.rule-fields .reason{grid-column:1/3}.rule-fields>button{height:36px}.rule-list{display:grid;gap:6px}.rule-list article{display:grid;grid-template-columns:minmax(180px,1fr) auto minmax(120px,1fr) auto;align-items:center;gap:8px;padding:8px;border:1px solid var(--l12-ui-line,#334049);background:var(--l12-ui-panel,#0a1117)}.rule-list span{display:grid}.rule-list small,.rule-list em{color:var(--l12-ui-text-muted,#839097);font-size:14px}.rule-list strong{color:var(--l12-ui-accent,#e0c16b)}.rule-list em{font-style:normal}@media(max-width:760px){.rule-fields,.rule-list article{grid-template-columns:1fr}.rule-fields .reason{grid-column:auto}}
</style>
