<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType, normalizeLookupCardType } from './cardPresentation'

interface CatalogCard {
  id: string
  number: string
  nameZh: string
  cardType: string
  product: string
  faction: string
  imageUrl?: string
  cost?: number
  hp?: number
  troops?: number
  disasterLevel?: number
  effect?: string
}

interface PresetDeck {
  name: string
  masterId: string
  cardIds: string[]
  moraleIds: string[]
}

interface LookupCard {
  cardNo: string
  name: string
  type: string
  faction: string
  cost?: number | null
  attack?: number | null
  health?: number | null
  image?: string
  effectText?: string
}

const typeLabels: Record<string, string> = {
  legion: '军团', tactic: '战术', rune: '士气卡', artifact: '圣物',
  divinity: '主城', master: '主宰', destruction: '天灾',
  token: '衍生卡牌', trial: '试炼卡', unknown: '待识别',
}
const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原',
  asgard: '阿斯加德', taiyangcheng: '太阳城',
  olympus: '奥林匹斯', bijie: '彼界', disaster: '天灾',
}
const lookupFactionMap: Record<string, string> = {
  通用: 'universal', 天廷: 'tianting', 高天原: 'gaotianyuan', 阿斯加德: 'asgard',
  太阳城: 'taiyangcheng', 奥林匹斯: 'olympus', 彼界: 'bijie', 天灾: 'disaster',
}
const s1CounterTactics = new Set([
  'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
  'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420',
])

const cards = ref<CatalogCard[]>([])
const decks = ref<PresetDeck[]>([])
const loading = ref(true)
const loadError = ref('')
const query = ref('')
const type = ref('all')
const faction = ref('all')
const cost = ref('all')
const disaster = ref('all')
const product = ref('all')
const sort = ref<'number' | 'cost' | 'troops' | 'name'>('number')
const selected = ref<CatalogCard | null>(null)

onMounted(async () => {
  try {
    const [cardResponse, deckResponse, lookupResponse] = await Promise.all([
      fetch('/data/l12/cards.s1.json'),
      fetch('/data/l12/preset-decks.s1.json'),
      fetch('/data/l12/cards.lookup.json').catch(() => null),
    ])
    if (!cardResponse.ok || !deckResponse.ok) throw new Error('卡牌数据请求失败')
    const rawSeasonOne: CatalogCard[] = await cardResponse.json()
    const seasonOne = rawSeasonOne.map(card => s1CounterTactics.has(card.id)
      ? { ...card, cardType: 'counter-tactic' }
      : card)
    const lookupCards: LookupCard[] = lookupResponse?.ok ? await lookupResponse.json() : []
    const seasonTwo: CatalogCard[] = lookupCards.filter(card => card.cardNo?.startsWith('S02-')).map(card => ({
      id: card.cardNo,
      number: card.cardNo,
      nameZh: card.name,
      cardType: normalizeLookupCardType(card.type, card.name),
      product: 'S02',
      faction: lookupFactionMap[card.faction] ?? card.faction,
      imageUrl: card.image ? `https://twelve-legions-card-lookup.pages.dev${card.image}` : undefined,
      cost: card.cost ?? undefined,
      troops: card.attack ?? undefined,
      hp: card.health ?? undefined,
      effect: card.effectText ?? undefined,
    }))
    cards.value = [...seasonOne, ...seasonTwo]
    decks.value = await deckResponse.json()
    selected.value = cards.value[0] ?? null
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : '卡牌档案加载失败'
  } finally {
    loading.value = false
  }
})

const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const result = cards.value.filter(card => {
    const matchesQuery = !keyword || [card.nameZh, card.number, card.effect]
      .some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
    const matchesType = type.value === 'all' || cardTypeFilterKey(card.cardType) === type.value
    const matchesFaction = faction.value === 'all' || card.faction === faction.value
    const matchesProduct = product.value === 'all' || card.product === product.value
    const matchesCost = cost.value === 'all' || (cost.value === '7+' ? (card.cost ?? -1) >= 7 : card.cost === Number(cost.value))
    const matchesDisaster = disaster.value === 'all'
      || (disaster.value === 'none' ? !card.disasterLevel : card.disasterLevel === Number(disaster.value))
    return matchesQuery && matchesType && matchesFaction && matchesProduct && matchesCost && matchesDisaster
  })
  return result.sort((a, b) => {
    if (sort.value === 'name') return a.nameZh.localeCompare(b.nameZh, 'zh-CN')
    if (sort.value === 'cost') return (a.cost ?? 99) - (b.cost ?? 99) || a.number.localeCompare(b.number)
    if (sort.value === 'troops') return (b.troops ?? -1) - (a.troops ?? -1) || a.number.localeCompare(b.number)
    return a.number.localeCompare(b.number)
  })
})

const types = computed(() => [...new Set(cards.value.map(card => cardTypeFilterKey(card.cardType)))]
  .filter(key => typeLabels[key]))
const factions = computed(() => Object.keys(factionLabels).filter(key => cards.value.some(card => card.faction === key)))
const selectedDecks = computed(() => {
  if (!selected.value) return []
  const selectedId = selected.value.id
  return decks.value.map(deck => ({
    name: deck.name,
    copies: deck.cardIds.filter(id => id === selectedId).length
      + deck.moraleIds.filter(id => id === selectedId).length
      + (deck.masterId === selectedId ? 1 : 0),
  })).filter(deck => deck.copies > 0)
})

function resetFilters() {
  query.value = ''
  type.value = faction.value = cost.value = disaster.value = product.value = 'all'
  sort.value = 'number'
}
</script>

<template>
  <section class="card-archive grand-panel">
    <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
    <header class="archive-header">
      <div><p class="kicker">CARD ARCHIVE · SEASON 1–2</p><h1>卡牌档案</h1></div>
      <div class="archive-count"><b>{{ filtered.length }}</b><span>/ {{ cards.length }} 张</span></div>
    </header>

    <div class="archive-toolbar">
      <label class="archive-search"><span>搜索</span><input v-model="query" type="search" placeholder="卡名、编号或效果文字"/></label>
      <label><span>类型</span><select v-model="type"><option value="all">全部类型</option><option v-for="key in types" :key="key" :value="key">{{ typeLabels[key] }}</option></select></label>
      <label><span>阵营</span><select v-model="faction"><option value="all">全部阵营</option><option v-for="key in factions" :key="key" :value="key">{{ factionLabels[key] }}</option></select></label>
      <label><span>卡池</span><select v-model="product"><option value="all">S1 + S2</option><option value="S01">S1</option><option value="S02">S2</option></select></label>
      <label><span>费用</span><select v-model="cost"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value" :value="value">{{ value }}</option></select></label>
      <label><span>天灾等级</span><select v-model="disaster"><option value="all">全部</option><option value="none">无</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select></label>
      <label><span>排序</span><select v-model="sort"><option value="number">编号</option><option value="cost">费用</option><option value="troops">兵力</option><option value="name">名称</option></select></label>
      <button class="archive-reset" @click="resetFilters">重置</button>
    </div>

    <div v-if="loading" class="archive-empty">正在载入卡牌数据…</div>
    <div v-else-if="loadError" class="archive-empty error">{{ loadError }}</div>
    <div v-else class="archive-workspace">
      <div class="archive-grid" role="list" aria-label="卡牌搜索结果">
        <button v-for="card in filtered" :key="card.id" role="listitem" class="archive-card"
          :class="[{ selected: selected?.id === card.id, 'landscape-thumbnail': isHorizontalCardType(card.cardType) }, `faction-${card.faction}`]" @click="selected = card">
          <div class="archive-card-image">
            <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.nameZh" loading="lazy"/>
            <div v-else class="archive-card-fallback">XII</div>
            <b v-if="card.cost !== undefined" class="archive-cost">{{ card.cost }}</b>
            <b v-if="card.disasterLevel" class="archive-disaster">{{ card.disasterLevel }}</b>
            <b v-if="card.troops" class="archive-troops">{{ card.troops }}</b>
          </div>
          <span>{{ card.nameZh }}</span><small>{{ card.number }} · {{ cardTypeLabel(card.cardType) }}</small>
        </button>
        <div v-if="!filtered.length" class="archive-empty">没有符合条件的卡牌。</div>
      </div>

      <aside v-if="selected" class="archive-detail">
        <div class="archive-detail-image" :class="{ horizontal: isHorizontalCardType(selected.cardType) }"><img v-if="selected.imageUrl" :src="selected.imageUrl" :alt="selected.nameZh"/><div v-else>XII</div></div>
        <p class="archive-number">{{ selected.number }} · {{ selected.product }}</p>
        <h2>{{ selected.nameZh }}</h2>
        <div class="archive-tags"><span>{{ factionLabels[selected.faction] ?? selected.faction }}</span><span>{{ cardTypeLabel(selected.cardType) }}</span></div>
        <dl>
          <template v-if="selected.cost !== undefined"><dt>费用</dt><dd>{{ selected.cost }}</dd></template>
          <template v-if="selected.troops !== undefined"><dt>兵力</dt><dd>{{ selected.troops }}</dd></template>
          <template v-if="selected.hp !== undefined"><dt>血量</dt><dd>{{ selected.hp }}</dd></template>
          <template v-if="selected.disasterLevel !== undefined"><dt>天灾等级</dt><dd>{{ selected.disasterLevel }}</dd></template>
        </dl>
        <section class="archive-effect"><b>效果</b><p>{{ selected.effect || '无效果文字' }}</p></section>
        <section v-if="selectedDecks.length" class="archive-decks"><b>收录预组</b><p v-for="deck in selectedDecks" :key="deck.name">{{ deck.name }} × {{ deck.copies }}</p></section>
      </aside>
    </div>
  </section>
</template>
