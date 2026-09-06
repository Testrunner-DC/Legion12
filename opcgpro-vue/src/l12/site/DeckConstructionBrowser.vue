<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from '@/l12/cardPresentation'
import type { DeckCard } from '@/l12/decks'

export interface ConstructionEntry { cardId: string; quantity: number; section?: string }

const props = withDefaults(defineProps<{
  entries: ConstructionEntry[]
  catalog: DeckCard[]
  title?: string
}>(), { title: '构筑快照' })

const query = ref('')
const type = ref('all')
const section = ref('all')
const selectedId = ref('')
const byId = computed(() => new Map(props.catalog.map(card => [card.id, card])))
const normalized = computed(() => {
  const totals = new Map<string, ConstructionEntry>()
  for (const entry of props.entries) {
    const zone = entry.section || 'main'
    const key = `${zone}:${entry.cardId}`
    const current = totals.get(key)
    if (current) current.quantity += entry.quantity
    else totals.set(key, { cardId: entry.cardId, quantity: entry.quantity, section: zone })
  }
  return [...totals.values()].sort((a, b) => (a.section || '').localeCompare(b.section || '') || a.cardId.localeCompare(b.cardId))
})
const types = computed(() => [...new Set(normalized.value.map(entry => cardTypeFilterKey(byId.value.get(entry.cardId)?.cardType || 'unknown')))])
const sections = computed(() => [...new Set(normalized.value.map(entry => entry.section || 'main'))])
const visible = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return normalized.value.filter(entry => {
    const card = byId.value.get(entry.cardId)
    return (type.value === 'all' || cardTypeFilterKey(card?.cardType || 'unknown') === type.value)
      && (section.value === 'all' || entry.section === section.value)
      && (!keyword || [entry.cardId, card?.number, card?.nameZh].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword)))
  })
})
const selected = computed(() => byId.value.get(selectedId.value))
const totalCards = computed(() => normalized.value.reduce((sum, entry) => sum + entry.quantity, 0))
watch(visible, values => {
  if (!values.some(entry => entry.cardId === selectedId.value)) selectedId.value = values[0]?.cardId || ''
}, { immediate: true })
</script>

<template>
  <section class="construction-browser" data-ui-contract="shared-deck-construction-browser">
    <header><div><small>DECK SNAPSHOT</small><h3>{{ title }}</h3></div><b>{{ totalCards }} 张 · {{ normalized.length }} 种</b></header>
    <nav aria-label="构筑筛选"><input v-model="query" placeholder="搜索卡名或编号"/><select v-model="section"><option value="all">全部区域</option><option v-for="value in sections" :key="value" :value="value">{{ value }}</option></select><select v-model="type"><option value="all">全部类型</option><option v-for="value in types" :key="value" :value="value">{{ cardTypeLabel(value) }}</option></select></nav>
    <div class="construction-workspace">
      <div class="construction-grid">
        <button v-for="entry in visible" :key="`${entry.section}-${entry.cardId}`" :class="{ selected: selectedId === entry.cardId, landscape: isHorizontalCardType(byId.get(entry.cardId)?.cardType) }" @click="selectedId = entry.cardId">
          <CardImage :card-id="entry.cardId" :legacy-url="byId.get(entry.cardId)?.imageUrl" :alt="byId.get(entry.cardId)?.nameZh || entry.cardId" intent="thumb"/>
          <strong>×{{ entry.quantity }}</strong><span>{{ byId.get(entry.cardId)?.nameZh || entry.cardId }}</span><small>{{ byId.get(entry.cardId)?.number || entry.cardId }} · {{ entry.section }}</small>
        </button>
        <p v-if="!visible.length">没有符合筛选条件的卡牌</p>
      </div>
      <aside v-if="selected"><CardImage :card-id="selected.id" :legacy-url="selected.imageUrl" :alt="selected.nameZh" intent="detail" eager/><small>{{ selected.number }}</small><h4>{{ selected.nameZh }}</h4><p>{{ cardTypeLabel(cardTypeFilterKey(selected.cardType)) }} · {{ selected.faction }}</p><div>{{ selected.effect || '无效果文字' }}</div></aside>
    </div>
  </section>
</template>

<style scoped>
.construction-browser{display:grid;min-height:0;gap:10px;color:#eee}.construction-browser>header{display:flex;align-items:end;justify-content:space-between;gap:12px}.construction-browser h3{margin:3px 0 0}.construction-browser header small{color:#d4b65d;font:900 14px monospace;letter-spacing:.14em}.construction-browser header>b{color:#92a0a4;font-size:14px}.construction-browser>nav{display:grid;grid-template-columns:minmax(150px,1fr) 110px 120px;gap:7px}.construction-browser input,.construction-browser select{box-sizing:border-box;min-width:0;width:100%;padding:8px;border:1px solid #47545b;background:#080e13;color:#fff;font-size:14px}.construction-workspace{display:grid;grid-template-columns:minmax(0,1fr) 190px;gap:10px;min-height:0}.construction-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(92px,1fr));align-content:start;gap:8px;max-height:58vh;overflow:auto}.construction-grid>button{position:relative;display:grid;min-width:0;gap:3px;padding:5px;border:1px solid #334149;background:#0b1217;color:#fff;text-align:left}.construction-grid>button:hover,.construction-grid>button.selected{border-color:#d4b65d}.construction-grid .l12-card-image{width:100%;height:auto;aspect-ratio:5/7}.construction-grid button.landscape .l12-card-image{aspect-ratio:8/5}.construction-grid strong{position:absolute;right:7px;top:7px;padding:3px 5px;background:#080b0de8;color:#f1d376}.construction-grid span,.construction-grid small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.construction-grid span{font-size:14px;font-weight:900}.construction-grid small{color:#6f7e83;font-size:14px}.construction-grid>p{grid-column:1/-1;color:#748087;text-align:center}.construction-workspace>aside{min-width:0;padding:9px;border-left:1px solid #35424a}.construction-workspace>aside .l12-card-image{width:150px;height:210px;margin:auto}.construction-workspace>aside small{display:block;margin-top:8px;color:#6d9da2}.construction-workspace>aside h4{margin:4px 0}.construction-workspace>aside p,.construction-workspace>aside div{color:#8e9a9d;font-size:14px;line-height:1.6}.construction-workspace>aside div{padding-top:8px;border-top:1px solid #35424a;color:#d4d9d7}
@media(max-width:700px){.construction-browser>nav{grid-template-columns:1fr 1fr}.construction-browser>nav input{grid-column:1/-1}.construction-workspace{grid-template-columns:1fr}.construction-workspace>aside{display:none}}
</style>
