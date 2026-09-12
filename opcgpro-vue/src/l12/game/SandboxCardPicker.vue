<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from '../cardPresentation'
import { filterableCardCost, loadDeckCatalog, type DeckCard } from '../decks'
import CardImage from '../CardImage.vue'
import CatalogCardDetails from '../CatalogCardDetails.vue'

export type SandboxCatalogCard = DeckCard

const props = withDefaults(defineProps<{ title?: string; allowedTypes?: string[] }>(), { title: '选择卡片', allowedTypes: () => [] })
const emit = defineEmits<{ select: [card: SandboxCatalogCard]; close: [] }>()
const cards = ref<SandboxCatalogCard[]>([])
const loading = ref(true)
const errorText = ref('')
const detailCard = ref<DeckCard | null>(null)
const query = ref('')
const type = ref('all')
const faction = ref('all')
const product = ref('all')
const cost = ref('all')
const disaster = ref('all')

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', bijie: '彼界', otherworld: '彼界', disaster: '天灾',
}
onMounted(async () => {
  try {
    cards.value = await loadDeckCatalog()
  } catch (error) {
    errorText.value = error instanceof Error ? error.message : '卡牌加载失败'
  } finally { loading.value = false }
})

const available = computed(() => props.allowedTypes.length
  ? cards.value.filter(card => props.allowedTypes.includes(cardTypeFilterKey(card.cardType)))
  : cards.value)
const types = computed(() => [...new Set(available.value.map(card => cardTypeFilterKey(card.cardType)))])
const factions = computed(() => [...new Set(available.value.map(card => card.faction))])
const products = computed(() => [...new Set(available.value.map(card => card.product))])
const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return available.value.filter(card => {
    const matchText = !keyword || [card.nameZh, card.number, card.effect].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
    const matchType = type.value === 'all' || cardTypeFilterKey(card.cardType) === type.value
    const matchFaction = faction.value === 'all' || card.faction === faction.value
    const matchProduct = product.value === 'all' || card.product === product.value
    const filterCost = filterableCardCost(card)
    const matchCost = cost.value === 'all' || (filterCost !== null
      && (cost.value === '7+' ? filterCost >= 7 : filterCost === Number(cost.value)))
    const matchDisaster = disaster.value === 'all' || (disaster.value === 'none' ? !card.disasterLevel : card.disasterLevel === Number(disaster.value))
    return matchText && matchType && matchFaction && matchProduct && matchCost && matchDisaster
  }).sort((a, b) => a.number.localeCompare(b.number))
})
</script>

<template>
  <Teleport to="body">
    <div class="picker-mask" @click.self="emit('close')">
      <section class="card-picker" role="dialog" aria-modal="true" :aria-label="title">
        <header><div><small>SANDBOX CARD ARCHIVE</small><h2>{{ title }}</h2></div><button @click="emit('close')">×</button></header>
        <div class="filters">
          <input v-model="query" type="search" placeholder="搜索卡名、编号或效果文字" autofocus/>
          <select v-model="type"><option value="all">全部类型</option><option v-for="key in types" :key="key" :value="key">{{ cardTypeLabel(key) }}</option></select>
          <select v-model="faction"><option value="all">全部阵营</option><option v-for="key in factions" :key="key" :value="key">{{ factionLabels[key] ?? key }}</option></select>
          <select v-model="product"><option value="all">全部卡池</option><option v-for="value in products" :key="value" :value="value">{{ value }}</option></select>
          <select v-model="cost"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value">{{ value }}</option></select>
          <select v-model="disaster"><option value="all">全部天灾等级</option><option value="none">无天灾等级</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select>
        </div>
        <p v-if="loading" class="state">正在读取卡牌图鉴…</p><p v-else-if="errorText" class="state error">{{ errorText }}</p>
        <div v-else class="card-grid">
          <article v-for="card in filtered" :key="card.id" class="picker-card" :class="{ horizontal: isHorizontalCardType(card.cardType) }">
            <button class="picker-image" :aria-label="`查看${card.nameZh}详情`" @click="detailCard = card"><CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" native-orientation/></button>
            <b>{{ card.nameZh }}</b><small>{{ card.number }} · {{ cardTypeLabel(card.cardType) }}</small>
            <div class="picker-card-actions"><button @click="detailCard = card">详情</button><button @click="emit('select', card)">选择</button></div>
          </article>
          <p v-if="!filtered.length" class="state">没有符合条件的卡片</p>
        </div>
      </section>
    </div>
  </Teleport>
  <CatalogCardDetails v-if="detailCard" :card="detailCard" @close="detailCard = null"/>
</template>

<style scoped>
.picker-card{display:flex;min-width:0;flex-direction:column;gap:8px;padding:8px;border:1px solid #39464d;background:#111a21}.picker-card>b,.picker-card>small{white-space:normal;overflow-wrap:anywhere;font-size:var(--l12-board-copy,13px)}.picker-image{display:block!important;width:100%;height:auto;aspect-ratio:5/7;padding:0!important;overflow:hidden}.picker-card.horizontal .picker-image{aspect-ratio:8/5}.picker-image .l12-card-image{height:100%;aspect-ratio:auto!important}.picker-card-actions{display:flex;gap:6px;margin-top:auto}.picker-card-actions button{flex:1;min-height:36px;justify-content:center;font-size:var(--l12-board-copy,13px)}
.picker-mask{position:fixed;z-index:3200;inset:0;display:grid;padding:42px;background:#020507d9;place-items:center;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.card-picker{display:grid;width:min(1180px,96vw);height:min(820px,90vh);grid-template-rows:auto auto minmax(0,1fr);border:1px solid #6a5a2a;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.card-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid #374147}.card-picker h2{margin:4px 0 0;font-size:max(20px,var(--l12-board-copy,13px))}.card-picker small{color:#cfad43;font:900 var(--l12-board-copy,13px) monospace;letter-spacing:.16em}.card-picker header button{border:0;background:transparent;color:#fff;font-size:max(24px,var(--l12-board-copy,13px))}.filters{display:grid;grid-template-columns:minmax(220px,2fr) repeat(5,minmax(92px,1fr));gap:7px;padding:12px;border-bottom:1px solid #303a40}.filters input,.filters select{min-width:0;padding:9px;border:1px solid #445159;background:#070c10;color:#fff;font-weight:800}.card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(116px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.card-grid button{display:grid;min-width:0;gap:4px;padding:6px;border:1px solid #39464d;background:#111a21;color:#fff;text-align:left}.card-grid button:hover{border-color:#e1bd50;box-shadow:0 0 14px #c598383d}.card-grid .l12-card-image,.card-grid button>span{width:100%;aspect-ratio:5/7;background:#050708}.card-grid button.horizontal .l12-card-image{aspect-ratio:8/5}.card-grid b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:var(--l12-board-copy,13px)}.card-grid small{overflow:hidden;color:#87949a;text-overflow:ellipsis;white-space:nowrap;font-size:var(--l12-board-copy,13px)}.state{grid-column:1/-1;margin:auto;color:#829096}.state.error{color:#e28d94}@media(max-width:760px){.picker-mask{padding:8px}.card-picker{width:100%;height:96vh}.filters{grid-template-columns:1fr 1fr}.filters input{grid-column:1/-1}.card-grid{grid-template-columns:repeat(auto-fill,minmax(92px,1fr))}}
@media(max-width:800px){.filters{grid-template-columns:1fr 1fr}.filters input{grid-column:1/-1}}
</style>
