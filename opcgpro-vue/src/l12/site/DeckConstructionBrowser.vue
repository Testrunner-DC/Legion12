<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import { cardTypeFilterKey, cardTypeLabel } from '@/l12/cardPresentation'
import { compareDeckCardIds } from '@/l12/deckOrdering'
import type { DeckCard } from '@/l12/decks'
import MobileFilterSheet from './MobileFilterSheet.vue'

export interface ConstructionEntry { cardId: string; quantity: number; section?: string }

const props = withDefaults(defineProps<{
  entries: ConstructionEntry[]
  catalog: DeckCard[]
  title?: string
  masterFaction?: string
  externalDetails?: boolean
  filterTarget?: string
  hideHeader?: boolean
}>(), { title: '构筑快照', filterTarget: '', hideHeader: false })

const query = ref('')
const emit = defineEmits<{ select: [card: DeckCard] }>()
const type = ref('all')
const section = ref('all')
const filtersOpen = ref(false)
const filterTargetReady = ref(false)
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
  return [...totals.values()].sort((a, b) => (a.section || '').localeCompare(b.section || '') || compareDeckCardIds(a.cardId, b.cardId, byId.value, props.masterFaction))
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
const activeFilterCount = computed(() => Number(type.value !== 'all') + Number(section.value !== 'all'))
const sectionLabels: Record<string, string> = {
  main: '主牌库', morale: '士气区', special: '试炼区', extra: '额外区', automatic: '自动额外区',
}
const sectionLabel = (value?: string) => sectionLabels[value || 'main'] || value || '主牌库'
function resetFilters() { type.value = 'all'; section.value = 'all' }
function selectCard(cardId: string) {
  selectedId.value = cardId
  const card = byId.value.get(cardId)
  if (card) emit('select', card)
}
watch(visible, values => {
  if (selectedId.value && !values.some(entry => entry.cardId === selectedId.value)) selectedId.value = ''
}, { immediate: true })
onMounted(() => { filterTargetReady.value = Boolean(props.filterTarget) })
</script>

<template>
  <section class="construction-browser" data-ui-contract="shared-deck-construction-browser">
    <header v-if="!hideHeader"><div><small>构筑卡表</small><h3>{{ title }}</h3></div><b>{{ totalCards }} 张 · {{ normalized.length }} 种</b></header>
    <Teleport v-if="filterTarget && filterTargetReady" :to="filterTarget">
      <nav class="construction-filter-rail" aria-label="构筑筛选">
        <input v-model="query" type="search" placeholder="搜索卡名或编号" aria-label="搜索卡名或编号"/>
        <select v-model="section" aria-label="按区域筛选"><option value="all">全部区域</option><option v-for="value in sections" :key="value" :value="value">{{ sectionLabel(value) }}</option></select>
        <select v-model="type" aria-label="按类型筛选"><option value="all">全部类型</option><option v-for="value in types" :key="value" :value="value">{{ cardTypeLabel(value) }}</option></select>
      </nav>
    </Teleport>
    <nav v-else aria-label="构筑筛选">
      <input v-model="query" type="search" placeholder="搜索卡名或编号"/>
      <MobileFilterSheet v-model="filtersOpen" title="构筑筛选" :active-count="activeFilterCount" @reset="resetFilters">
        <div class="construction-filter-fields">
          <label><span>区域</span><select v-model="section"><option value="all">全部区域</option><option v-for="value in sections" :key="value" :value="value">{{ sectionLabel(value) }}</option></select></label>
          <label><span>类型</span><select v-model="type"><option value="all">全部类型</option><option v-for="value in types" :key="value" :value="value">{{ cardTypeLabel(value) }}</option></select></label>
        </div>
        <template #apply-label>查看 {{ visible.length }} 种卡牌</template>
      </MobileFilterSheet>
      <div class="construction-desktop-filters">
        <select v-model="section" aria-label="按区域筛选"><option value="all">全部区域</option><option v-for="value in sections" :key="value" :value="value">{{ sectionLabel(value) }}</option></select>
        <select v-model="type" aria-label="按类型筛选"><option value="all">全部类型</option><option v-for="value in types" :key="value" :value="value">{{ cardTypeLabel(value) }}</option></select>
      </div>
    </nav>
    <div class="construction-grid">
        <button v-for="entry in visible" :key="`${entry.section}-${entry.cardId}`" :class="{ selected: selectedId === entry.cardId }" @click="selectCard(entry.cardId)">
          <CardImage :card-id="entry.cardId" :legacy-url="byId.get(entry.cardId)?.imageUrl" :alt="byId.get(entry.cardId)?.nameZh || entry.cardId" intent="thumb"/>
          <strong>×{{ entry.quantity }}</strong><span>{{ byId.get(entry.cardId)?.nameZh || entry.cardId }}</span><small>{{ byId.get(entry.cardId)?.number || entry.cardId }} · {{ sectionLabel(entry.section) }}</small>
        </button>
        <p v-if="!visible.length">没有符合筛选条件的卡牌</p>
    </div>
    <CatalogCardDetails v-if="selected && !externalDetails" :card="selected" :show-catalog-only="false" @close="selectedId = ''"/>
  </section>
</template>

<style scoped>
.construction-browser{display:grid;min-height:0;gap:10px;color:#eee}.construction-browser>header{display:flex;align-items:end;justify-content:space-between;gap:12px}.construction-browser h3{margin:3px 0 0}.construction-browser header small{color:#d4b65d;font-size:14px;font-weight:900;letter-spacing:.14em}.construction-browser header>b{color:#92a0a4;font-size:14px}.construction-browser>nav{display:grid;grid-template-columns:minmax(150px,1fr) auto;gap:7px}.construction-filter-rail{display:grid;gap:8px}.construction-desktop-filters{display:grid;grid-template-columns:110px 120px;gap:7px}.construction-browser input,.construction-browser select,.construction-filter-rail input,.construction-filter-rail select{box-sizing:border-box;min-width:0;width:100%;padding:8px;border:1px solid #47545b;background:#080e13;color:#fff;font-size:14px}.construction-filter-fields{display:grid;gap:12px}.construction-filter-fields label{display:grid;gap:6px;color:#aeb8ba;font-size:12px;font-weight:900}.construction-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(112px,1fr));align-content:start;gap:8px;overflow:visible}.construction-grid>button{position:relative;display:grid;min-width:0;gap:3px;padding:5px;border:1px solid #334149;background:#0b1217;color:#fff;text-align:left}.construction-grid>button:hover,.construction-grid>button.selected{border-color:#d4b65d}.construction-grid .l12-card-image{width:100%;height:auto;aspect-ratio:5/7}.construction-grid strong{position:absolute;right:7px;top:7px;padding:3px 5px;background:#080b0de8;color:#f1d376}.construction-grid span,.construction-grid small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.construction-grid span{font-size:14px;font-weight:900}.construction-grid small{color:#6f7e83;font-size:14px}.construction-grid>p{grid-column:1/-1;color:#748087;text-align:center}
.construction-grid>button{grid-template-rows:auto minmax(2.8em,auto) auto;align-content:start}.construction-grid>button:hover,.construction-grid>button.selected{box-shadow:inset 0 0 0 1px rgba(212,182,93,.34)}.construction-grid .l12-card-image{box-sizing:border-box;border:1px solid rgba(224,214,184,.18);background:#070b0f}.construction-grid span,.construction-grid small{min-width:0;overflow-wrap:anywhere}.construction-grid span{overflow:visible;line-height:1.4;text-overflow:clip;white-space:normal}.construction-grid small{align-self:end}.construction-workspace>aside h4{overflow-wrap:anywhere}
@media(max-width:700px){.construction-browser{overflow-x:clip}.construction-browser>header{align-items:start}.construction-browser>header>b{max-width:40%;text-align:right}.construction-browser>nav{grid-template-columns:minmax(0,1fr) auto}.construction-desktop-filters{display:none}.construction-grid{grid-template-columns:repeat(auto-fill,minmax(min(82px,28vw),1fr));gap:6px}.construction-grid>button{padding:4px}.construction-grid strong{right:4px;top:4px;padding:2px 4px}.construction-grid span{font-size:12px}.construction-grid small{font-size:11px}}
.construction-grid small{display:block;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
@media(max-width:700px){.construction-grid{grid-template-columns:repeat(auto-fill,minmax(min(94px,30vw),1fr))}}
</style>
