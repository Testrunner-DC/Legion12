<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { filterableCardCost, loadDeckCatalog, type DeckCard } from './decks'
import CardImage from './CardImage.vue'
import CatalogCardDetails from './CatalogCardDetails.vue'
import { landscapeTeleportTarget } from './mobileViewport'

export interface SingleCardPickerItem {
  id: string
  cardId: string
  number: string
  name: string
  nameZh: string
  cardType: string
  faction?: string
  product?: string
  cost?: number
  disasterLevel?: number
  effect?: string
  imageUrl?: string
  cardImageId?: string
  subtitle?: string
  detailCard?: DeckCard
}

const props = withDefaults(defineProps<{
  title?: string
  items?: SingleCardPickerItem[]
  allowedTypes?: string[]
}>(), { title: '选择卡片', items: undefined, allowedTypes: () => [] })
const emit = defineEmits<{ select: [card: SingleCardPickerItem]; close: [] }>()
const catalogItems = ref<SingleCardPickerItem[]>([])
const loading = ref(!props.items)
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

function fromCatalog(card: DeckCard): SingleCardPickerItem {
  return {
    id: card.id, cardId: card.id, number: card.number, name: card.nameZh, nameZh: card.nameZh,
    cardType: card.cardType, faction: card.faction, product: card.product, cost: card.cost,
    disasterLevel: card.disasterLevel, effect: card.effect, imageUrl: card.imageUrl,
    cardImageId: card.id, detailCard: card,
  }
}

onMounted(async () => {
  if (props.items) return
  try { catalogItems.value = (await loadDeckCatalog()).map(fromCatalog) }
  catch (error) { errorText.value = error instanceof Error ? error.message : '卡牌加载失败' }
  finally { loading.value = false }
})

const sourceItems = computed(() => props.items ?? catalogItems.value)
const available = computed(() => props.allowedTypes.length
  ? sourceItems.value.filter(card => props.allowedTypes.includes(cardTypeFilterKey(card.cardType)))
  : sourceItems.value)
const types = computed(() => [...new Set(available.value.map(card => cardTypeFilterKey(card.cardType)))])
const factions = computed(() => [...new Set(available.value.map(card => card.faction).filter(Boolean) as string[])])
const products = computed(() => [...new Set(available.value.map(card => card.product).filter(Boolean) as string[])])
const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return available.value.filter(card => {
    const matchText = !keyword || [card.name, card.nameZh, card.number, card.cardId, card.effect, card.subtitle,
      card.faction, card.product].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
    const matchType = type.value === 'all' || cardTypeFilterKey(card.cardType) === type.value
    const matchFaction = faction.value === 'all' || card.faction === faction.value
    const matchProduct = product.value === 'all' || card.product === product.value
    const filterCost = card.detailCard ? filterableCardCost(card.detailCard) : card.cost ?? null
    const matchCost = cost.value === 'all' || (filterCost !== null
      && (cost.value === '7+' ? filterCost >= 7 : filterCost === Number(cost.value)))
    const matchDisaster = disaster.value === 'all' || (disaster.value === 'none'
      ? !card.disasterLevel : card.disasterLevel === Number(disaster.value))
    return matchText && matchType && matchFaction && matchProduct && matchCost && matchDisaster
  }).sort((left, right) => left.number.localeCompare(right.number, 'zh-CN'))
})
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <div class="single-card-picker-mask" @click.self="emit('close')">
      <section class="single-card-picker" role="dialog" aria-modal="true" :aria-label="title">
        <header><div><small>SINGLE CARD SEARCH</small><h2>{{ title }}</h2></div><button type="button" aria-label="关闭" @click="emit('close')">×</button></header>
        <div class="single-card-filters">
          <input v-model="query" type="search" placeholder="搜索卡名、编号或效果文字" autofocus>
          <select v-model="type"><option value="all">全部类型</option><option v-for="key in types" :key="key" :value="key">{{ cardTypeLabel(key) }}</option></select>
          <select v-model="faction"><option value="all">全部阵营</option><option v-for="key in factions" :key="key" :value="key">{{ factionLabels[key] ?? key }}</option></select>
          <select v-model="product"><option value="all">全部产品</option><option v-for="value in products" :key="value" :value="value">{{ value }}</option></select>
          <select v-model="cost"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value">{{ value }}</option></select>
          <select v-model="disaster"><option value="all">全部天灾等级</option><option value="none">无天灾等级</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select>
        </div>
        <p class="single-card-result">{{ loading ? '正在读取卡牌图鉴…' : errorText || `${filtered.length} 张符合条件` }}</p>
        <div v-if="!loading && !errorText" class="single-card-grid">
          <article v-for="card in filtered" :key="card.id" class="single-card-result-card" :class="{ horizontal: isHorizontalCardType(card.cardType) }">
            <button class="single-card-image" type="button" :aria-label="card.detailCard ? `查看${card.name}详情` : `选择${card.name}`" @click="card.detailCard ? detailCard = card.detailCard : emit('select', card)">
              <CardImage v-if="card.cardImageId || card.imageUrl" :card-id="card.cardImageId" :legacy-url="card.imageUrl" :alt="card.name" intent="thumb" native-orientation/>
              <span v-else>暂无卡图</span>
            </button>
            <b>{{ card.name }}</b><small>{{ card.number || card.cardId }} · {{ cardTypeLabel(card.cardType) }}</small>
            <span>{{ card.subtitle || [factionLabels[card.faction || ''] || card.faction, card.product].filter(Boolean).join(' · ') }}</span>
            <div class="single-card-actions"><button v-if="card.detailCard" type="button" @click="detailCard = card.detailCard">详情</button><button type="button" @click="emit('select', card)">选择</button></div>
          </article>
          <p v-if="!filtered.length" class="single-card-empty">没有符合条件的卡牌。</p>
        </div>
      </section>
    </div>
  </Teleport>
  <CatalogCardDetails v-if="detailCard" :card="detailCard" @close="detailCard = null"/>
</template>

<style scoped>
.single-card-picker-mask{position:fixed;z-index:3400;inset:0;display:grid;padding:clamp(8px,3vw,36px);background:#020507dc;place-items:center}.single-card-picker{display:grid;width:min(1180px,96vw);height:min(820px,92vh);grid-template-rows:auto auto auto minmax(0,1fr);overflow:hidden;border:1px solid var(--l12-ui-accent-line,#6a5a2a);border-radius:var(--l12-ui-radius-lg,8px);background:var(--l12-ui-panel,#0b1116);color:var(--l12-ui-text,#eff2ef);box-shadow:var(--l12-ui-shadow-dialog,0 30px 100px #000)}.single-card-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid var(--l12-ui-line,#374147)}.single-card-picker h2{margin:4px 0 0;font-size:22px}.single-card-picker small{color:var(--l12-ui-accent,#cfad43);font-size:12px;font-weight:900;letter-spacing:.08em}.single-card-picker>header button{width:38px;height:38px;border:1px solid var(--l12-ui-line-strong,#48545c);background:var(--l12-ui-control,#080d11);color:#fff;font-size:26px}.single-card-filters{display:grid;grid-template-columns:minmax(220px,2fr) repeat(5,minmax(92px,1fr));gap:8px;padding:12px;border-bottom:1px solid var(--l12-ui-line,#303a40)}.single-card-filters input,.single-card-filters select{min-width:0;padding:9px;border:1px solid var(--l12-ui-line-strong,#445159);background:var(--l12-ui-control,#070c10);color:#fff}.single-card-result{margin:0;padding:8px 14px;color:var(--l12-ui-text-muted,#87959b);font-size:12px}.single-card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(126px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.single-card-result-card{display:flex;min-width:0;flex-direction:column;gap:6px;padding:8px;border:1px solid var(--l12-ui-line,#39464d);border-radius:var(--l12-ui-radius-sm,3px);background:var(--l12-ui-panel-raised,#111a21);color:#fff}.single-card-result-card:hover,.single-card-result-card:focus-within{border-color:var(--l12-ui-accent,#e1bd50);box-shadow:0 0 14px #c598383d}.single-card-image{display:grid;width:100%;height:auto;aspect-ratio:5/7;padding:0;overflow:hidden;border:0;background:#050708;place-items:center}.single-card-result-card.horizontal .single-card-image{aspect-ratio:8/5}.single-card-image img,.single-card-image :deep(.l12-card-image){width:100%;height:100%;object-fit:contain}.single-card-result-card>b,.single-card-result-card>small,.single-card-result-card>span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.single-card-result-card>span{color:var(--l12-ui-text-muted,#93a0a6);font-size:12px}.single-card-actions{display:flex;gap:6px;margin-top:auto}.single-card-actions button{min-height:34px;flex:1;border:1px solid var(--l12-ui-line-strong,#536168);background:var(--l12-ui-control,#080d11);color:#fff;font-weight:900}.single-card-actions button:last-child{border-color:var(--l12-ui-accent-line,#9e7e3d);color:var(--l12-ui-accent,#e1bd50)}.single-card-empty{grid-column:1/-1;color:var(--l12-ui-text-muted,#87959b);text-align:center}@media(max-width:760px){.single-card-picker-mask{padding:max(6px,env(safe-area-inset-top)) max(6px,env(safe-area-inset-right)) max(6px,env(safe-area-inset-bottom)) max(6px,env(safe-area-inset-left))}.single-card-picker{width:100%;height:100%;max-height:100%;border-radius:0}.single-card-filters{grid-template-columns:1fr 1fr}.single-card-filters input{grid-column:1/-1}.single-card-grid{grid-template-columns:repeat(auto-fill,minmax(104px,1fr));gap:7px;padding:9px}}
</style>
