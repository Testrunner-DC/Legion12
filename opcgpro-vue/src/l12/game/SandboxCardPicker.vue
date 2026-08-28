<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType, normalizeLookupCardType } from '../cardPresentation'
import CardImage from '../CardImage.vue'

export interface SandboxCatalogCard {
  id: string
  number: string
  nameZh: string
  cardType: string
  product: string
  faction: string
  imageUrl?: string
  cost?: number
  troops?: number
  disasterLevel?: number
  effect?: string
}

interface LookupCard {
  cardNo: string; name: string; type: string; faction: string; cost?: number | null
  attack?: number | null; disasterLevel?: number | null; image?: string; effectText?: string
}

const props = withDefaults(defineProps<{ title?: string; allowedTypes?: string[] }>(), { title: '选择卡片', allowedTypes: () => [] })
const emit = defineEmits<{ select: [card: SandboxCatalogCard]; close: [] }>()
const cards = ref<SandboxCatalogCard[]>([])
const loading = ref(true)
const errorText = ref('')
const query = ref('')
const type = ref('all')
const faction = ref('all')
const product = ref('all')
const cost = ref('all')
const disaster = ref('all')

const factionMap: Record<string, string> = {
  通用: 'universal', 天廷: 'tianting', 高天原: 'gaotianyuan', 阿斯加德: 'asgard',
  太阳城: 'taiyangcheng', 奥林匹斯: 'olympus', 彼界: 'bijie', 天灾: 'disaster',
}
const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', bijie: '彼界', disaster: '天灾',
}
const s1CounterTactics = new Set(['S01-0016','S01-0017','S01-0018','S01-0019','S01-0020','S01-0021','S01-0120','S01-0223','S01-0224','S01-0320','S01-0420'])

onMounted(async () => {
  try {
    const [s1Response, lookupResponse] = await Promise.all([fetch('/data/l12/cards.s1.json'), fetch('/data/l12/cards.lookup.json')])
    if (!s1Response.ok || !lookupResponse.ok) throw new Error('卡牌数据请求失败')
    const seasonOne = (await s1Response.json() as SandboxCatalogCard[]).map(card => ({
      ...card,
      cardType: s1CounterTactics.has(card.id) ? 'counter-tactic' : card.cardType,
    }))
    const lookup = await lookupResponse.json() as LookupCard[]
    const seasonTwo = lookup.filter(card => card.cardNo?.startsWith('S02-')).map(card => ({
      id: card.cardNo, number: card.cardNo, nameZh: card.name,
      cardType: normalizeLookupCardType(card.type, card.name), product: 'S02',
      faction: factionMap[card.faction] ?? card.faction,
      imageUrl: card.image ? `https://twelve-legions-card-lookup.pages.dev${card.image}` : undefined,
      cost: card.cost ?? undefined, troops: card.attack ?? undefined,
      disasterLevel: card.disasterLevel ?? undefined, effect: card.effectText ?? undefined,
    }))
    cards.value = [...seasonOne, ...seasonTwo]
  } catch (error) {
    errorText.value = error instanceof Error ? error.message : '卡牌加载失败'
  } finally { loading.value = false }
})

const available = computed(() => props.allowedTypes.length
  ? cards.value.filter(card => props.allowedTypes.includes(cardTypeFilterKey(card.cardType)))
  : cards.value)
const types = computed(() => [...new Set(available.value.map(card => cardTypeFilterKey(card.cardType)))])
const factions = computed(() => [...new Set(available.value.map(card => card.faction))])
const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return available.value.filter(card => {
    const matchText = !keyword || [card.nameZh, card.number, card.effect].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
    const matchType = type.value === 'all' || cardTypeFilterKey(card.cardType) === type.value
    const matchFaction = faction.value === 'all' || card.faction === faction.value
    const matchProduct = product.value === 'all' || card.product === product.value
    const matchCost = cost.value === 'all' || (cost.value === '7+' ? (card.cost ?? -1) >= 7 : card.cost === Number(cost.value))
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
          <select v-model="product"><option value="all">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option></select>
          <select v-model="cost"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value">{{ value }}</option></select>
          <select v-model="disaster"><option value="all">全部天灾等级</option><option value="none">无天灾等级</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select>
        </div>
        <p v-if="loading" class="state">正在读取卡牌档案…</p><p v-else-if="errorText" class="state error">{{ errorText }}</p>
        <div v-else class="card-grid">
          <button v-for="card in filtered" :key="card.id" :class="{ horizontal: isHorizontalCardType(card.cardType) }" @click="emit('select', card)">
            <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb"/>
            <b>{{ card.nameZh }}</b><small>{{ card.number }} · {{ cardTypeLabel(card.cardType) }}</small>
          </button>
          <p v-if="!filtered.length" class="state">没有符合条件的卡片</p>
        </div>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.picker-mask{position:fixed;z-index:3200;inset:0;display:grid;padding:42px;background:#020507d9;place-items:center;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.card-picker{display:grid;width:min(1180px,96vw);height:min(820px,90vh);grid-template-rows:auto auto minmax(0,1fr);border:1px solid #6a5a2a;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.card-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid #374147}.card-picker h2{margin:4px 0 0;font-size:20px}.card-picker small{color:#cfad43;font:900 8px monospace;letter-spacing:.16em}.card-picker header button{border:0;background:transparent;color:#fff;font-size:24px}.filters{display:grid;grid-template-columns:minmax(220px,2fr) repeat(5,minmax(92px,1fr));gap:7px;padding:12px;border-bottom:1px solid #303a40}.filters input,.filters select{min-width:0;padding:9px;border:1px solid #445159;background:#070c10;color:#fff;font-weight:800}.card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(116px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.card-grid button{display:grid;min-width:0;gap:4px;padding:6px;border:1px solid #39464d;background:#111a21;color:#fff;text-align:left}.card-grid button:hover{border-color:#e1bd50;box-shadow:0 0 14px #c598383d}.card-grid .l12-card-image,.card-grid button>span{width:100%;aspect-ratio:5/7;background:#050708}.card-grid button.horizontal .l12-card-image{aspect-ratio:8/5}.card-grid b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:10px}.card-grid small{overflow:hidden;color:#87949a;text-overflow:ellipsis;white-space:nowrap;font-size:8px}.state{grid-column:1/-1;margin:auto;color:#829096}.state.error{color:#e28d94}@media(max-width:760px){.picker-mask{padding:8px}.card-picker{width:100%;height:96vh}.filters{grid-template-columns:1fr 1fr}.filters input{grid-column:1/-1}.card-grid{grid-template-columns:repeat(auto-fill,minmax(92px,1fr))}}
</style>
