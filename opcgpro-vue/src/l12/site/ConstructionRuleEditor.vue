<script setup lang="ts">
import { computed, ref } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import type { DeckCard } from '@/l12/decks'
import type { OperationsCardRestriction } from '@/l12/platform'

const props = withDefaults(defineProps<{ cards: DeckCard[]; modelValue: OperationsCardRestriction[]; allowMasterRules?: boolean }>(), { allowMasterRules: true })
const emit = defineEmits<{ 'update:modelValue': [value: OperationsCardRestriction[]] }>()
const query = ref('')
const selectedCardId = ref('')
const masterId = ref('')
const maxCopies = ref(3)
const reason = ref('')
const byId = computed(() => new Map(props.cards.map(card => [card.id, card])))
const masters = computed(() => props.cards.filter(card => card.cardType === 'master' && card.id !== 'S01-02M2'))
const candidates = computed(() => {
  const value = query.value.trim().toLocaleLowerCase('zh-CN')
  return props.cards.filter(card => !['destruction', 'disaster'].includes(card.cardType)
    && (!value || `${card.nameZh} ${card.id}`.toLocaleLowerCase('zh-CN').includes(value))).slice(0, 80)
})
function choose(card: DeckCard) { selectedCardId.value = card.id }
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
      <label>筛选卡牌<input v-model="query" placeholder="卡名或卡号"/></label>
      <div class="candidate-grid">
        <button v-for="card in candidates" :key="card.id" type="button" :class="{ selected: selectedCardId === card.id }" @click="choose(card)">
          <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb"/><span>{{ card.nameZh }}</span><small>{{ card.id }}</small>
        </button>
      </div>
      <div class="rule-fields">
        <label>已选卡牌<input :value="byId.get(selectedCardId)?.nameZh || selectedCardId" readonly placeholder="请从上方选择"/></label>
        <label v-if="allowMasterRules">适用范围<select v-model="masterId"><option value="">所有主宰</option><option v-for="master in masters" :key="master.id" :value="master.id">{{ master.nameZh }} · {{ master.id }}</option></select></label>
        <label>构筑上限<select v-model.number="maxCopies"><option v-for="value in [0,1,2,3]" :key="value" :value="value">{{ value }}{{ value === 0 ? '（禁用）' : ' 张' }}</option></select></label>
        <label class="reason">说明<input v-model="reason" maxlength="500" placeholder="可选"/></label>
        <button type="button" :disabled="!selectedCardId" @click="save">添加 / 更新规则</button>
      </div>
    </div>
    <div class="rule-list">
      <article v-for="rule in modelValue" :key="`${rule.masterId || '*'}-${rule.cardId}`"><span><b>{{ byId.get(rule.cardId)?.nameZh || rule.cardId }}</b><small>{{ rule.cardId }} · {{ rule.masterId ? `${byId.get(rule.masterId)?.nameZh || rule.masterId} 专属` : '所有主宰' }}</small></span><strong>上限 {{ rule.maxCopies }}</strong><em>{{ rule.reason || '无备注' }}</em><button type="button" @click="remove(rule)">删除</button></article>
      <p v-if="!modelValue.length">尚未设置构筑规则。</p>
    </div>
  </div>
</template>

<style scoped>
.rule-editor,.rule-builder{display:grid;gap:10px}.candidate-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(100px,1fr));gap:6px;max-height:300px;overflow:auto}.candidate-grid button{display:grid;gap:4px;min-width:0;padding:6px;border:1px solid #344149;background:#091016;color:#d8dfe1;text-align:left}.candidate-grid button.selected{border-color:#d0aa4a;background:#2a220f}.candidate-grid .l12-card-image{aspect-ratio:5/7}.candidate-grid span,.candidate-grid small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.candidate-grid small{color:#849096;font-size:8px}.rule-fields{display:grid;grid-template-columns:1fr 1fr 130px;gap:8px;align-items:end}.rule-fields label{display:grid;gap:5px}.rule-fields .reason{grid-column:1/3}.rule-fields>button{height:36px}.rule-list{display:grid;gap:6px}.rule-list article{display:grid;grid-template-columns:minmax(180px,1fr) auto minmax(120px,1fr) auto;align-items:center;gap:8px;padding:8px;border:1px solid #334049;background:#0a1117}.rule-list span{display:grid}.rule-list small,.rule-list em{color:#839097;font-size:9px}.rule-list strong{color:#e0c16b}.rule-list em{font-style:normal}@media(max-width:760px){.rule-fields,.rule-list article{grid-template-columns:1fr}.rule-fields .reason{grid-column:auto}}
</style>
