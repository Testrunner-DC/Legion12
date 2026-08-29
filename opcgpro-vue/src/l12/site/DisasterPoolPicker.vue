<script setup lang="ts">
import { computed, ref } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import type { DeckCard } from '@/l12/decks'

const props = withDefaults(defineProps<{ cards: DeckCard[]; modelValue: string[]; lockedId?: string; minimum?: number }>(), { minimum: 9 })
const emit = defineEmits<{ 'update:modelValue': [value: string[]] }>()
const query = ref('')
const disasters = computed(() => props.cards.filter(card => ['destruction', 'disaster'].includes(card.cardType)))
const filtered = computed(() => {
  const value = query.value.trim().toLocaleLowerCase('zh-CN')
  return disasters.value.filter(card => !value || `${card.nameZh} ${card.id}`.toLocaleLowerCase('zh-CN').includes(value))
})
const selected = (id: string) => props.modelValue.includes(id)
function toggle(id: string) {
  if (id === props.lockedId) return
  const next = selected(id) ? props.modelValue.filter(item => item !== id) : [...props.modelValue, id]
  const locked = props.lockedId && (props.modelValue.includes(props.lockedId) || id === props.lockedId) ? props.lockedId : undefined
  emit('update:modelValue', [...new Set(next.filter(item => item !== locked)), ...(locked ? [locked] : [])])
}
</script>

<template>
  <div class="pool-picker">
    <header><input v-model="query" placeholder="按天灾名称或卡号筛选"/><span :class="{ invalid: modelValue.length < minimum }">已选 {{ modelValue.length }} 张 · 至少 {{ minimum }} 张（含堙灭）</span></header>
    <div class="pool-grid">
      <button v-for="card in filtered" :key="card.id" type="button" :class="{ selected: selected(card.id), locked: card.id === lockedId }" @click="toggle(card.id)">
        <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb"/>
        <span>{{ card.nameZh }}</span><small>{{ card.id }}{{ card.id === lockedId ? ' · 固定末位' : '' }}</small>
      </button>
    </div>
  </div>
</template>

<style scoped>
.pool-picker{display:grid;gap:10px}.pool-picker>header{display:flex;align-items:center;gap:9px}.pool-picker input{flex:1}.pool-picker header span{white-space:nowrap}.pool-picker header span.invalid{color:#ff9aa1}.pool-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(128px,1fr));gap:8px;max-height:520px;overflow:auto;padding:2px}.pool-grid button{display:grid;gap:5px;padding:7px;border:1px solid #354249;background:#091016;color:#d5dcde;text-align:left}.pool-grid button.selected{border-color:#d2ad4e;background:#29210f;box-shadow:inset 0 0 0 1px #8e742f}.pool-grid button.locked{border-style:double}.pool-grid .l12-card-image{aspect-ratio:8/5}.pool-grid span{font-weight:900}.pool-grid small{color:#8b979c;font-size:8px}
</style>
