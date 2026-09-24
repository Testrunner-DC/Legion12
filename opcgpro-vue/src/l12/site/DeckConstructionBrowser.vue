<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import CardDetailContent from '@/l12/CardDetailContent.vue'
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
}>(), { title: '构筑快照' })

const query = ref('')
const type = ref('all')
const section = ref('all')
const filtersOpen = ref(false)
const selectedId = ref('')
const mobileDetailOpen = ref(false)
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
  if (window.matchMedia('(max-width:700px)').matches) mobileDetailOpen.value = true
}
watch(visible, values => {
  if (!values.some(entry => entry.cardId === selectedId.value)) selectedId.value = values[0]?.cardId || ''
}, { immediate: true })
</script>

<template>
  <section class="construction-browser" data-ui-contract="shared-deck-construction-browser">
    <header><div><small>DECK SNAPSHOT</small><h3>{{ title }}</h3></div><b>{{ totalCards }} 张 · {{ normalized.length }} 种</b></header>
    <nav aria-label="构筑筛选">
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
    <div class="construction-workspace">
      <div class="construction-grid">
        <button v-for="entry in visible" :key="`${entry.section}-${entry.cardId}`" :class="{ selected: selectedId === entry.cardId }" @click="selectCard(entry.cardId)">
          <CardImage :card-id="entry.cardId" :legacy-url="byId.get(entry.cardId)?.imageUrl" :alt="byId.get(entry.cardId)?.nameZh || entry.cardId" intent="thumb"/>
          <strong>×{{ entry.quantity }}</strong><span>{{ byId.get(entry.cardId)?.nameZh || entry.cardId }}</span><small>{{ byId.get(entry.cardId)?.number || entry.cardId }} · {{ sectionLabel(entry.section) }}</small>
        </button>
        <p v-if="!visible.length">没有符合筛选条件的卡牌</p>
      </div>
      <aside v-if="selected" data-ui-contract="shared-card-detail"><CardDetailContent :card="selected" :show-catalog-only="false"/></aside>
    </div>
    <Teleport to="body"><div v-if="selected && mobileDetailOpen" class="construction-detail-mask" @click.self="mobileDetailOpen = false"><section role="dialog" aria-modal="true" :aria-label="`${selected.nameZh}卡牌详情`"><button class="construction-detail-close" @click="mobileDetailOpen = false">×</button><CardDetailContent :card="selected" layout="modal" :show-catalog-only="false"/></section></div></Teleport>
  </section>
</template>

<style scoped>
.construction-browser{display:grid;min-height:0;gap:10px;color:#eee}.construction-browser>header{display:flex;align-items:end;justify-content:space-between;gap:12px}.construction-browser h3{margin:3px 0 0}.construction-browser header small{color:#d4b65d;font:900 14px monospace;letter-spacing:.14em}.construction-browser header>b{color:#92a0a4;font-size:14px}.construction-browser>nav{display:grid;grid-template-columns:minmax(150px,1fr) auto;gap:7px}.construction-desktop-filters{display:grid;grid-template-columns:110px 120px;gap:7px}.construction-browser input,.construction-browser select{box-sizing:border-box;min-width:0;width:100%;padding:8px;border:1px solid #47545b;background:#080e13;color:#fff;font-size:14px}.construction-filter-fields{display:grid;gap:12px}.construction-filter-fields label{display:grid;gap:6px;color:#aeb8ba;font-size:12px;font-weight:900}.construction-workspace{display:grid;grid-template-columns:minmax(0,1fr) 190px;gap:10px;min-height:0}.construction-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(92px,1fr));align-content:start;gap:8px;max-height:58vh;overflow:auto}.construction-grid>button{position:relative;display:grid;min-width:0;gap:3px;padding:5px;border:1px solid #334149;background:#0b1217;color:#fff;text-align:left}.construction-grid>button:hover,.construction-grid>button.selected{border-color:#d4b65d}.construction-grid .l12-card-image{width:100%;height:auto;aspect-ratio:5/7}.construction-grid strong{position:absolute;right:7px;top:7px;padding:3px 5px;background:#080b0de8;color:#f1d376}.construction-grid span,.construction-grid small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.construction-grid span{font-size:14px;font-weight:900}.construction-grid small{color:#6f7e83;font-size:14px}.construction-grid>p{grid-column:1/-1;color:#748087;text-align:center}.construction-workspace>aside{min-width:0;padding:9px;border-left:1px solid #35424a}.construction-workspace>aside .l12-card-image{width:150px;height:210px;margin:auto}.construction-workspace>aside small{display:block;margin-top:8px;color:#6d9da2}.construction-workspace>aside h4{margin:4px 0}.construction-workspace>aside p,.construction-workspace>aside div{color:#8e9a9d;font-size:14px;line-height:1.6}.construction-workspace>aside div{padding-top:8px;border-top:1px solid #35424a;color:#d4d9d7}
.construction-grid>button{grid-template-rows:auto minmax(2.8em,auto) auto;align-content:start}.construction-grid>button:hover,.construction-grid>button.selected{box-shadow:inset 0 0 0 1px rgba(212,182,93,.34)}.construction-grid .l12-card-image{box-sizing:border-box;border:1px solid rgba(224,214,184,.18);background:#070b0f}.construction-grid span,.construction-grid small{min-width:0;overflow-wrap:anywhere}.construction-grid span{overflow:visible;line-height:1.4;text-overflow:clip;white-space:normal}.construction-grid small{align-self:end}.construction-workspace>aside h4{overflow-wrap:anywhere}
@media(max-width:700px){.construction-browser{overflow-x:clip}.construction-browser>header{align-items:start}.construction-browser>header>b{max-width:40%;text-align:right}.construction-browser>nav{grid-template-columns:minmax(0,1fr) auto}.construction-desktop-filters{display:none}.construction-workspace{grid-template-columns:1fr}.construction-workspace>aside{display:none}.construction-grid{grid-template-columns:repeat(auto-fill,minmax(min(82px,28vw),1fr));gap:6px}.construction-grid>button{padding:4px}.construction-grid strong{right:4px;top:4px;padding:2px 4px}.construction-grid span{font-size:12px}.construction-grid small{font-size:11px}}
</style>

<style>
.construction-detail-mask{position:fixed;z-index:190;inset:0;display:grid;place-items:center;padding:max(10px,env(safe-area-inset-top)) max(10px,env(safe-area-inset-right)) max(10px,env(safe-area-inset-bottom)) max(10px,env(safe-area-inset-left));background:rgba(1,4,6,.8);backdrop-filter:blur(6px)}.construction-detail-mask>section{position:relative;display:grid;grid-template-columns:minmax(120px,32%) minmax(0,1fr);gap:13px;width:min(760px,94vw);max-height:86dvh;overflow:auto;padding:14px;border:1px solid #52606a;background:#111923;color:#eee}.construction-detail-close{position:absolute;z-index:2;right:8px;top:8px;width:34px;height:34px;border:1px solid #59666e;background:#0a1117;color:#fff;font-size:22px}.construction-detail-mask .archive-modal-image{align-self:start}.construction-detail-mask .archive-modal-detail{padding-right:36px}@media(max-width:520px){.construction-detail-mask>section{grid-template-columns:minmax(96px,29%) minmax(0,1fr);gap:9px;padding:10px}.construction-detail-mask .card-detail-copy h2{font-size:17px}.construction-detail-mask .card-detail-copy{font-size:12px}}
</style>
