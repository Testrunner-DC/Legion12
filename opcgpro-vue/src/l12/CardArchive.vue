<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { groupArchiveCards, type LogicalArchiveCard } from './cardArchiveVersions'
import { loadCardArchiveCatalog, loadOfficialPresetDecks, type DeckCard, type OfficialL12PresetDeck } from './decks'
import CardImage from './CardImage.vue'

type CatalogCard = DeckCard
type PresetDeck = OfficialL12PresetDeck

const typeLabels: Record<string, string> = {
  legion: '军团', tactic: '战术', rune: '士气卡', artifact: '圣物',
  divinity: '主城', master: '主宰', destruction: '天灾',
  token: '衍生卡牌', trial: '试炼卡', unknown: '待识别',
}
const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原',
  asgard: '阿斯加德', taiyangcheng: '太阳城',
  olympus: '奥林匹斯', bijie: '彼界', otherworld: '彼界', disaster: '天灾',
}
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
const selectedLogicalId = ref('')
const selectedVersionId = ref('')
const logicalCards = computed(() => groupArchiveCards(cards.value))
const productOptions = computed(() => [...new Set(cards.value.map(card => card.product))].sort())

onMounted(async () => {
  try {
    ;[cards.value, decks.value] = await Promise.all([loadCardArchiveCatalog(), loadOfficialPresetDecks()])
    const first = logicalCards.value[0]
    if (first) selectLogical(first)
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : '卡牌档案加载失败'
  } finally {
    loading.value = false
  }
})

const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const result = logicalCards.value.filter(entry => entry.versions.some(card => {
    const matchesQuery = !keyword || [card.nameZh, card.number, card.product, card.rarity, card.effect, card.profession, ...(card.traits ?? [])]
      .some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
    return matchesQuery
      && (type.value === 'all' || cardTypeFilterKey(card.cardType) === type.value)
      && (faction.value === 'all' || card.faction === faction.value)
      && (product.value === 'all' || card.product === product.value)
      && (cost.value === 'all' || (cost.value === '7+' ? (card.cost ?? -1) >= 7 : card.cost === Number(cost.value)))
      && (disaster.value === 'all'
        || (disaster.value === 'none' ? !card.disasterLevel : card.disasterLevel === Number(disaster.value)))
  }))
  return result.sort((a, b) => {
    const left = a.defaultVersion
    const right = b.defaultVersion
    if (sort.value === 'name') return left.nameZh.localeCompare(right.nameZh, 'zh-CN')
    if (sort.value === 'cost') return (left.cost ?? 99) - (right.cost ?? 99) || left.number.localeCompare(right.number)
    if (sort.value === 'troops') return (right.troops ?? -1) - (left.troops ?? -1) || left.number.localeCompare(right.number)
    return left.number.localeCompare(right.number)
  })
})

const types = computed(() => [...new Set(cards.value.map(card => cardTypeFilterKey(card.cardType)))]
  .filter(key => typeLabels[key]))
const factions = computed(() => Object.keys(factionLabels).filter(key => cards.value.some(card => card.faction === key)))
const selectedLogical = computed(() => logicalCards.value.find(entry => entry.logicalId === selectedLogicalId.value) ?? null)
const selected = computed(() => selectedLogical.value?.versions.find(card => card.id === selectedVersionId.value)
  ?? selectedLogical.value?.defaultVersion
  ?? null)
const selectedVersionIndex = computed(() => selectedLogical.value && selected.value
  ? selectedLogical.value.versions.findIndex(card => card.id === selected.value?.id)
  : -1)
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

function selectLogical(entry: LogicalArchiveCard) {
  selectedLogicalId.value = entry.logicalId
  selectedVersionId.value = entry.defaultVersion.id
}

function cycleVersion(direction: -1 | 1) {
  const entry = selectedLogical.value
  if (!entry || entry.versions.length < 2) return
  const nextIndex = (selectedVersionIndex.value + direction + entry.versions.length) % entry.versions.length
  selectedVersionId.value = entry.versions[nextIndex].id
}

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
      <div class="archive-count"><b>{{ filtered.length }}</b><span>/ {{ logicalCards.length }} 张</span></div>
    </header>

    <div class="archive-toolbar">
      <label class="archive-search"><span>搜索</span><input v-model="query" type="search" placeholder="卡名、编号或效果文字"/></label>
      <label><span>类型</span><select v-model="type"><option value="all">全部类型</option><option v-for="key in types" :key="key" :value="key">{{ typeLabels[key] }}</option></select></label>
      <label><span>阵营</span><select v-model="faction"><option value="all">全部阵营</option><option v-for="key in factions" :key="key" :value="key">{{ factionLabels[key] }}</option></select></label>
      <label><span>卡池</span><select v-model="product"><option value="all">全部卡池</option><option v-for="value in productOptions" :key="value" :value="value">{{ value }}</option></select></label>
      <label><span>费用</span><select v-model="cost"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value" :value="value">{{ value }}</option></select></label>
      <label><span>天灾等级</span><select v-model="disaster"><option value="all">全部</option><option value="none">无</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select></label>
      <label><span>排序</span><select v-model="sort"><option value="number">编号</option><option value="cost">费用</option><option value="troops">兵力</option><option value="name">名称</option></select></label>
      <button class="archive-reset" @click="resetFilters">重置</button>
    </div>

    <div v-if="loading" class="archive-empty">正在载入卡牌数据…</div>
    <div v-else-if="loadError" class="archive-empty error">{{ loadError }}</div>
    <div v-else class="archive-workspace">
      <div class="archive-grid" role="list" aria-label="卡牌搜索结果">
        <button v-for="entry in filtered" :key="entry.logicalId" role="listitem" class="archive-card"
          :class="[{ selected: selectedLogicalId === entry.logicalId, 'landscape-thumbnail': isHorizontalCardType(entry.defaultVersion.cardType) }, `faction-${entry.defaultVersion.faction}`]" @click="selectLogical(entry)">
          <div class="archive-card-image">
            <CardImage :card-id="entry.defaultVersion.id" :legacy-url="entry.defaultVersion.imageUrl" :alt="entry.defaultVersion.nameZh" intent="thumb"/>
            <b v-if="entry.defaultVersion.cost !== undefined" class="archive-cost">{{ entry.defaultVersion.cost }}</b>
            <b v-if="entry.defaultVersion.disasterLevel" class="archive-disaster">{{ entry.defaultVersion.disasterLevel }}</b>
            <b v-if="entry.defaultVersion.troops" class="archive-troops">{{ entry.defaultVersion.troops }}</b>
            <b v-if="entry.versions.length > 1" class="archive-version-count">{{ entry.versions.length }}</b>
          </div>
          <span>{{ entry.defaultVersion.nameZh }}</span><small>{{ entry.defaultVersion.number }} · {{ cardTypeLabel(entry.defaultVersion.cardType) }}</small>
        </button>
        <div v-if="!filtered.length" class="archive-empty">没有符合条件的卡牌。</div>
      </div>

      <aside v-if="selected" class="archive-detail">
        <div class="archive-detail-image" :class="{ horizontal: isHorizontalCardType(selected.cardType) }">
          <CardImage :card-id="selected.id" :legacy-url="selected.imageUrl" :alt="selected.nameZh" intent="detail" eager/>
          <template v-if="selectedLogical && selectedLogical.versions.length > 1">
            <button class="archive-version-arrow previous" type="button" aria-label="上一版本" title="上一版本" @click="cycleVersion(-1)">‹</button>
            <button class="archive-version-arrow next" type="button" aria-label="下一版本" title="下一版本" @click="cycleVersion(1)">›</button>
            <span class="archive-version-position">{{ selectedVersionIndex + 1 }} / {{ selectedLogical.versions.length }}</span>
          </template>
        </div>
        <p class="archive-number">{{ selected.number }} · {{ selected.product }}</p>
        <h2>{{ selected.nameZh }}</h2>
        <div class="archive-tags"><span v-for="trait in selected.traits" :key="trait">{{ trait }}</span><span>{{ cardTypeLabel(selected.cardType) }}</span><span v-if="selected.profession">{{ selected.profession }}</span><span v-if="selected.rarity">{{ selected.rarity }}</span></div>
        <dl>
          <template v-if="selected.cost !== undefined"><dt>费用</dt><dd>{{ selected.cost }}</dd></template>
          <template v-if="selected.troops !== undefined"><dt>兵力</dt><dd>{{ selected.troops }}</dd></template>
          <template v-if="selected.hp !== undefined"><dt>血量</dt><dd>{{ selected.hp }}</dd></template>
          <template v-if="selected.disasterLevel !== undefined"><dt>天灾等级</dt><dd>{{ selected.disasterLevel }}</dd></template>
          <template v-if="selected.trialValue !== undefined"><dt>试炼值</dt><dd>{{ selected.trialValue }}</dd></template>
        </dl>
        <section class="archive-effect"><b>效果</b><p class="l12-effect-body">{{ selected.effect || '无效果文字' }}</p></section>
        <section v-if="selectedDecks.length" class="archive-decks"><b>收录预组</b><p v-for="deck in selectedDecks" :key="deck.name">{{ deck.name }} × {{ deck.copies }}</p></section>
      </aside>
    </div>
  </section>
</template>
