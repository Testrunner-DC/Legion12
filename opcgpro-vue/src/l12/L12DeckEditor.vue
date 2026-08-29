<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { masterProfileUrl } from './specialAssets'
import { compareDeckCards } from './deckOrdering'
import { createDeckImageBlob, downloadDeckImage } from './site/deckShare'
import {
  MAIN_DECK_TYPES, buildMoraleDeck, deckCountSummary, deleteDeck, doesNotCountTowardMainDeck, effectiveDeckLimit, ensureOfficialPrebuiltDecks, loadDeckCatalog, loadSavedDecks, trialCapacityForMaster,
  saveDeck, validateDeck, type DeckCard, type SavedL12Deck,
} from './decks'
import { platformState, publicDeckApi } from './platform'
import CardImage from './CardImage.vue'
import DeckProfile from './DeckProfile.vue'

const router = useRouter()
const route = useRoute()
const returnTo = computed(() => typeof route.query.returnTo === 'string' && route.query.returnTo.startsWith('/') ? route.query.returnTo : '/decks')
const catalog = ref<DeckCard[]>([])
const savedDecks = ref<Record<string, SavedL12Deck>>({})
const loading = ref(true)
const notice = ref('')
const deckName = ref('新牌库')
const masterId = ref('')
const counts = ref<Record<string, number>>({})
const query = ref('')
const typeFilter = ref('all')
const productFilter = ref('all')
const costFilter = ref('all')
const disasterFilter = ref('all')
const sortMode = ref<'number' | 'cost' | 'troops' | 'name'>('number')
const selected = ref<DeckCard | null>(null)
const activeDeckName = ref<string | null>(null)
const specialIds = ref<string[]>([])
const catalogTab = ref<'master' | 'main' | 'extra'>('master')
const pendingDeleteName = ref('')
const deckImageUrl = ref('')
const deckImageBlob = ref<Blob | null>(null)
const generatingDeckImage = ref(false)
const publicationId = ref(typeof route.query.published === 'string' ? route.query.published : '')

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界',
}
const typeLabels: Record<string, string> = {
  legion: '军团', tactic: '战术', artifact: '圣物',
}

onMounted(async () => {
  try {
    ;[catalog.value, savedDecks.value] = await Promise.all([
      loadDeckCatalog(),
      ensureOfficialPrebuiltDecks(),
    ])
    const requested = typeof router.currentRoute.value.query.deck === 'string' ? router.currentRoute.value.query.deck : ''
    if (requested && savedDecks.value[requested]) loadDeck(savedDecks.value[requested], true)
    else selected.value = mainCards.value[0] ?? null
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库编辑器加载失败'
  } finally {
    loading.value = false
  }
})

const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const masters = computed(() => catalog.value.filter(card => card.cardType === 'master' && card.id !== 'S01-02M2'))
const selectedMaster = computed(() => byId.value.get(masterId.value))
const automaticExtraCards = computed(() => selectedMaster.value?.id === 'S01-02M1'
  ? [byId.value.get('S01-02M2')].filter(Boolean) as DeckCard[]
  : [])
const mainCards = computed(() => catalog.value.filter(card => MAIN_DECK_TYPES.has(card.cardType)))
const countSummary = computed(() => deckCountSummary(
  Object.entries(counts.value).flatMap(([id, count]) => Array(count).fill(id)), byId.value))
const totalCards = computed(() => countSummary.value.counted)
const uncountedCards = computed(() => countSummary.value.uncounted)
const moraleIds = computed(() => buildMoraleDeck(selectedMaster.value, catalog.value))
const trialCapacity = computed(() => trialCapacityForMaster(selectedMaster.value))
const availableTrials = computed(() => catalog.value.filter(card => card.cardType === 'trial'
  && card.faction === selectedMaster.value?.faction))
const selectedTrials = computed(() => specialIds.value.map(id => byId.value.get(id)).filter(Boolean) as DeckCard[])
const entries = computed(() => Object.entries(counts.value)
  .filter(([, count]) => count > 0)
  .map(([id, count]) => ({ card: byId.value.get(id)!, count }))
  .filter(entry => entry.card)
  .sort((a, b) => compareDeckCards(a.card, b.card, selectedMaster.value?.faction)))
const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const master = selectedMaster.value
  return mainCards.value.filter(card => {
    if (master && card.faction !== 'universal' && card.faction !== master.faction) return false
    if (typeFilter.value !== 'all' && cardTypeFilterKey(card.cardType) !== typeFilter.value) return false
    if (productFilter.value !== 'all' && card.product !== productFilter.value) return false
    if (costFilter.value !== 'all' && (costFilter.value === '7+'
      ? (card.cost ?? -1) < 7
      : card.cost !== Number(costFilter.value))) return false
    if (disasterFilter.value !== 'all' && (disasterFilter.value === 'none'
      ? !!card.disasterLevel
      : card.disasterLevel !== Number(disasterFilter.value))) return false
    return !keyword || [card.nameZh, card.number, card.profession, ...(card.traits ?? []), card.effect]
      .some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
  }).sort((a, b) => {
    if (sortMode.value === 'name') return a.nameZh.localeCompare(b.nameZh, 'zh-CN')
    if (sortMode.value === 'cost') return (a.cost ?? 99) - (b.cost ?? 99) || a.number.localeCompare(b.number)
    if (sortMode.value === 'troops') return (b.troops ?? -1) - (a.troops ?? -1) || a.number.localeCompare(b.number)
    return a.number.localeCompare(b.number)
  })
})
const validation = computed(() => validateDeck({
  name: deckName.value, masterId: masterId.value,
  cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
  moraleIds: moraleIds.value,
  specialIds: specialIds.value,
}, catalog.value))
const curve = computed(() => {
  const values = Array(9).fill(0) as number[]
  entries.value.forEach(({ card, count }) => values[Math.min(8, card.cost ?? 0)] += count)
  return values
})
const maxCurve = computed(() => Math.max(1, ...curve.value))

function chooseMaster(id: string) {
  masterId.value = id
  const master = byId.value.get(id)
  const next: Record<string, number> = {}
  Object.entries(counts.value).forEach(([cardId, count]) => {
    const card = byId.value.get(cardId)
    if (card && master && (card.faction === 'universal' || card.faction === master.faction)) next[cardId] = count
  })
  counts.value = next
  const capacity = trialCapacityForMaster(master)
  specialIds.value = specialIds.value.filter(specialId => {
    const card = byId.value.get(specialId)
    return card?.cardType === 'trial' && card.faction === master?.faction
  }).slice(0, capacity)
  catalogTab.value = 'main'
}

function toggleTrial(card: DeckCard) {
  if (specialIds.value.includes(card.id)) {
    specialIds.value = specialIds.value.filter(id => id !== card.id)
    return
  }
  if (specialIds.value.length >= trialCapacity.value) {
    notice.value = `该主宰的试炼区只能携带 ${trialCapacity.value} 张`
    return
  }
  specialIds.value = [...specialIds.value, card.id]
  selected.value = card
  notice.value = ''
}

function add(card: DeckCard) {
  if (!selectedMaster.value) { notice.value = '请先选择主宰'; return }
  const count = counts.value[card.id] || 0
  const limit = effectiveDeckLimit(card, masterId.value)
  if (count >= limit) { notice.value = `同编号卡牌最多 ${limit} 张`; return }
  if (!doesNotCountTowardMainDeck(card) && totalCards.value >= 50) { notice.value = '主牌库最多 50 张'; return }
  counts.value = { ...counts.value, [card.id]: count + 1 }
  selected.value = card
  notice.value = ''
}

function remove(id: string) {
  const next = { ...counts.value }
  if ((next[id] || 0) <= 1) delete next[id]
  else next[id]--
  counts.value = next
}

function newDeck() {
  publicationId.value = ''
  activeDeckName.value = null
  deckName.value = '新牌库'
  masterId.value = ''
  counts.value = {}
  specialIds.value = []
  selected.value = mainCards.value[0] ?? null
  notice.value = '已新建空白牌库'
}

function currentDeck(): SavedL12Deck {
  return {
    name: deckName.value.trim(), masterId: masterId.value,
    cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
    moraleIds: moraleIds.value, specialIds: [...specialIds.value], updatedAt: new Date().toISOString(),
  }
}

function onSave() {
  if (validation.value) { notice.value = validation.value; return }
  const deck = currentDeck()
  saveDeck(deck)
  if (activeDeckName.value && activeDeckName.value !== deck.name) deleteDeck(activeDeckName.value)
  activeDeckName.value = deck.name
  savedDecks.value = loadSavedDecks()
  notice.value = `已保存〈${deck.name}〉，可在房间中选择`
}

function onSaveAs() {
  if (validation.value) { notice.value = validation.value; return }
  const base = `${deckName.value.trim()} 副本`
  let name = base.slice(0, 24)
  let suffix = 2
  while (savedDecks.value[name]) {
    const ending = ` ${suffix++}`
    name = `${base.slice(0, 24 - ending.length)}${ending}`
  }
  const deck = { ...currentDeck(), name }
  saveDeck(deck)
  publicationId.value = ''
  activeDeckName.value = name
  deckName.value = name
  savedDecks.value = loadSavedDecks()
  notice.value = `已另存为〈${name}〉`
}

async function publishCurrentDeck() {
  if (!platformState.account) { notice.value = '请先登录账号，再公开牌库'; return }
  if (validation.value) { notice.value = validation.value; return }
  try {
    const deck = currentDeck()
    saveDeck(deck)
    const wasPublished = !!publicationId.value
    const result = await publicDeckApi.publish(deck, publicationId.value || undefined)
    publicationId.value = result.id
    await router.replace({ query: { ...route.query, deck: deck.name, published: result.id } })
    notice.value = wasPublished ? `已更新公开牌库〈${deck.name}〉` : `已公开〈${deck.name}〉，后续可从此处更新公开版本`
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '公开牌库失败'
  }
}

function loadDeck(deck: SavedL12Deck, preservePublication = false) {
  if (!preservePublication) publicationId.value = ''
  activeDeckName.value = deck.name
  deckName.value = deck.name
  masterId.value = deck.masterId
  const next: Record<string, number> = {}
  deck.cardIds.forEach(id => next[id] = (next[id] || 0) + 1)
  counts.value = next
  specialIds.value = [...(deck.specialIds ?? [])]
  notice.value = `已载入〈${deck.name}〉`
}

function requestDelete(name = activeDeckName.value ?? '') {
  if (!name) { notice.value = '当前不是已保存牌库'; return }
  pendingDeleteName.value = name
}

function confirmDelete() {
  const name = pendingDeleteName.value
  if (!name) return
  deleteDeck(name)
  savedDecks.value = loadSavedDecks()
  if (activeDeckName.value === name) newDeck()
  notice.value = `已删除〈${name}〉`
  pendingDeleteName.value = ''
}

function resetFilters() {
  query.value = ''
  typeFilter.value = productFilter.value = costFilter.value = disasterFilter.value = 'all'
  sortMode.value = 'number'
}

function closeDeckImage() {
  if (deckImageUrl.value) URL.revokeObjectURL(deckImageUrl.value)
  deckImageUrl.value = ''
  deckImageBlob.value = null
}

async function generateDeckImage() {
  if (validation.value) { notice.value = validation.value; return }
  generatingDeckImage.value = true
  closeDeckImage()
  try {
    deckImageBlob.value = await createDeckImageBlob(currentDeck(), catalog.value)
    deckImageUrl.value = URL.createObjectURL(deckImageBlob.value)
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库图生成失败'
  } finally {
    generatingDeckImage.value = false
  }
}

async function saveGeneratedDeckImage() {
  if (!deckImageBlob.value) return
  await downloadDeckImage(currentDeck(), catalog.value, deckImageBlob.value)
}

onBeforeUnmount(closeDeckImage)
</script>

<template>
  <div class="deck-builder-shell">
    <header class="deck-builder-topbar">
      <button class="back-button" @click="router.push(returnTo)">← 返回上一级</button>
      <div><small>GRANDUMI FRAMEWORK · LEGION12 STYLE</small><h1>牌库编辑器</h1></div>
      <label>牌库名称<input v-model="deckName" maxlength="24"/></label>
      <div class="deck-total" :class="{ valid: !validation }"><b>{{ countSummary.label }}</b><span>/ 40–50<span v-if="uncountedCards">（括号内不计构筑）</span></span></div>
      <div class="deck-file-actions">
        <button @click="newDeck">新建牌库</button>
        <button class="primary" :disabled="!!validation" @click="onSave">保存牌库</button>
        <button :disabled="!!validation" @click="onSaveAs">另存为牌库</button>
        <button :disabled="!!validation || generatingDeckImage" @click="generateDeckImage">{{ generatingDeckImage ? '生成中…' : '生成牌库图' }}</button>
        <button :disabled="!!validation" @click="publishCurrentDeck">{{ publicationId ? '更新公开牌库' : '公开牌库' }}</button>
        <button class="delete-deck" :disabled="!activeDeckName" @click="requestDelete()">删除牌库</button>
      </div>
    </header>

    <main v-if="loading" class="deck-loading">正在载入 248 张卡牌…</main>
    <main v-else class="deck-builder-grid">
      <aside class="deck-filter grand-panel">
        <p class="kicker">FILTER</p><h2>筛选</h2>
        <article v-if="selectedMaster" class="master-preview">
          <img :src="masterProfileUrl(selectedMaster.id, selectedMaster.imageUrl)" :alt="selectedMaster.nameZh"/>
          <div><b>{{ selectedMaster.nameZh }}</b><span>{{ factionLabels[selectedMaster.faction] }}</span><small>士气 {{ moraleIds.length }} 张</small></div>
        </article>
        <label>搜索<input v-model="query" placeholder="卡名、编号、效果"/></label>
        <label>类型<select v-model="typeFilter"><option value="all">全部主牌</option><option v-for="(label,key) in typeLabels" :key="key" :value="key">{{ label }}</option></select></label>
        <label>卡池<select v-model="productFilter"><option value="all">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option></select></label>
        <label>费用<select v-model="costFilter"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value" :value="value">{{ value }}</option></select></label>
        <label>天灾等级<select v-model="disasterFilter"><option value="all">全部</option><option value="none">无</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select></label>
        <label>排序<select v-model="sortMode"><option value="number">编号</option><option value="cost">费用</option><option value="troops">兵力</option><option value="name">名称</option></select></label>
        <button class="filter-reset" @click="resetFilters">重置筛选</button>

        <p class="kicker preset-kicker">SAVED DECKS</p>
        <div class="saved-list"><article v-for="deck in savedDecks" :key="deck.name" :class="{ active: deck.name === activeDeckName }"><button @click="loadDeck(deck)"><DeckProfile compact :master-id="deck.masterId" :master-name="byId.get(deck.masterId)?.nameZh" :name="deck.name" :meta="`${deckCountSummary(deck.cardIds, byId).label} 张`"/></button><button class="delete" @click="requestDelete(deck.name)">×</button></article><p v-if="!Object.keys(savedDecks).length">暂无本地牌库</p></div>
      </aside>

      <section class="deck-catalog grand-panel">
        <header><div><p class="kicker">CARD POOL</p><h2>{{ catalogTab === 'master' ? '主宰' : catalogTab === 'main' ? '主牌库' : '额外卡牌' }}</h2></div><span>{{ catalogTab === 'master' ? masters.length : catalogTab === 'main' ? filtered.length : availableTrials.length + automaticExtraCards.length }} 张结果</span></header>
        <nav class="catalog-tabs" aria-label="牌库构筑卡池分类">
          <button :class="{ active: catalogTab === 'master' }" @click="catalogTab = 'master'">主宰</button>
          <button :class="{ active: catalogTab === 'main' }" @click="catalogTab = 'main'">主牌库</button>
          <button :class="{ active: catalogTab === 'extra' }" @click="catalogTab = 'extra'">额外卡牌</button>
        </nav>
        <div v-if="catalogTab === 'master'" class="deck-card-grid">
          <article v-for="master in masters" :key="master.id" class="deck-card" :class="{ chosen: master.id === masterId }" @click="selected = master">
            <button class="card-image" @dblclick.stop="chooseMaster(master.id)"><CardImage :card-id="master.id" :legacy-url="master.imageUrl" :alt="master.nameZh" intent="thumb" fit="cover"/></button>
            <div><b>{{ master.nameZh }}</b><small>{{ master.number }} · {{ factionLabels[master.faction] }}</small></div>
            <button class="choose-special" @click.stop="chooseMaster(master.id)">{{ master.id === masterId ? '已选择' : '选择主宰' }}</button>
          </article>
        </div>
        <div v-else-if="catalogTab === 'main'" class="deck-card-grid">
          <article v-for="card in filtered" :key="card.id" class="deck-card" :class="{ chosen: counts[card.id], 'landscape-thumbnail': isHorizontalCardType(card.cardType) }" @click="selected = card">
            <button class="card-image" @dblclick.stop="add(card)">
              <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" :fit="isHorizontalCardType(card.cardType) ? 'contain' : 'cover'"/>
              <b v-if="counts[card.id]" class="copy-count">×{{ counts[card.id] }}</b>
            </button>
            <div><b>{{ card.nameZh }}</b><small>{{ card.number }} · {{ cardTypeLabel(card.cardType) }}</small></div>
            <div class="pool-count-controls">
              <button :disabled="!(counts[card.id] || 0)" aria-label="减少一张" @click.stop="remove(card.id)">−</button>
              <strong>{{ counts[card.id] || 0 }}</strong>
              <button :disabled="!masterId || (counts[card.id] || 0) >= effectiveDeckLimit(card, masterId) || (!doesNotCountTowardMainDeck(card) && totalCards >= 50)" aria-label="增加一张" @click.stop="add(card)">＋</button>
            </div>
          </article>
        </div>
        <div v-else class="deck-card-grid">
          <article v-for="trial in availableTrials" :key="trial.id" class="deck-card landscape-thumbnail" :class="{ chosen: specialIds.includes(trial.id) }" @click="selected = trial">
            <button class="card-image" @dblclick.stop="toggleTrial(trial)"><CardImage :card-id="trial.id" :legacy-url="trial.imageUrl" :alt="trial.nameZh" intent="thumb"/></button>
            <div><b>{{ trial.nameZh }}</b><small>{{ trial.number }} · 试炼</small></div>
            <button class="choose-special" @click.stop="toggleTrial(trial)">{{ specialIds.includes(trial.id) ? '移出额外区' : '加入额外区' }}</button>
          </article>
          <article v-for="card in automaticExtraCards" :key="card.id" class="deck-card chosen" @click="selected = card">
            <button class="card-image"><CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" fit="cover"/></button>
            <div><b>{{ card.nameZh }}</b><small>{{ card.number }} · 主宰专属</small></div><button class="choose-special" disabled>自动配置</button>
          </article>
          <p v-if="!availableTrials.length && !automaticExtraCards.length" class="empty-extra">当前主宰没有可配置的额外卡牌。</p>
        </div>
      </section>

      <aside class="deck-list grand-panel">
        <header><div><p class="kicker">DECK LIST</p><h2>{{ selectedMaster?.nameZh || '未选择主宰' }}</h2></div><b>{{ countSummary.label }}</b></header>
        <div class="cost-curve"><i v-for="(value,index) in curve" :key="index"><span :style="{height:`${Math.max(4, value / maxCurve * 56)}px`}"></span><b>{{ index === 8 ? '8+' : index }}</b><small>{{ value }}</small></i></div>
        <div class="deck-entries"><article v-for="entry in entries" :key="entry.card.id" @click="selected = entry.card">
          <CardImage class="deck-entry-banner" :card-id="entry.card.id" :legacy-url="entry.card.imageUrl" :alt="entry.card.nameZh" intent="thumb" fit="cover" object-position="center 28%"/>
          <span>{{ entry.card.cost ?? '—' }}</span><div><b>{{ entry.card.nameZh }}</b><small>{{ entry.card.number }}</small></div><strong>×{{ entry.count }}</strong>
          <button aria-label="增加一张" :disabled="entry.count >= effectiveDeckLimit(entry.card, masterId) || (!doesNotCountTowardMainDeck(entry.card) && totalCards >= 50)" @click.stop="add(entry.card)">＋</button>
          <button aria-label="减少一张" @click.stop="remove(entry.card.id)">−</button>
        </article><p v-if="!entries.length">从中间卡池加入卡牌，双击卡面也可快速加入。</p></div>
        <section v-if="trialCapacity" class="selected-trials">
          <header><b>额外区 · 试炼</b><span>{{ selectedTrials.length }}/{{ trialCapacity }}</span></header>
          <button v-for="trial in selectedTrials" :key="trial.id" type="button" @click="selected = trial">
            <CardImage :card-id="trial.id" :legacy-url="trial.imageUrl" :alt="trial.nameZh" intent="thumb"/><span>{{ trial.nameZh }}</span><i @click.stop="toggleTrial(trial)">×</i>
          </button>
        </section>
        <section v-if="automaticExtraCards.length" class="selected-trials automatic-extra-list">
          <header><b>额外区 · 主宰专属</b><span>自动</span></header>
          <button v-for="card in automaticExtraCards" :key="card.id" type="button" @click="selected = card">
            <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb"/><span>{{ card.nameZh }}</span><i>锁定</i>
          </button>
        </section>
        <section v-if="selected" class="builder-card-detail" :class="{ horizontal: isHorizontalCardType(selected.cardType) }">
          <CardImage :card-id="selected.id" :legacy-url="selected.imageUrl" :alt="selected.nameZh" intent="detail" eager/>
          <div><small>{{ selected.number }}</small><h3>{{ selected.nameZh }}</h3>
            <div v-if="selected.traits?.length || selected.profession || selected.trialValue" class="builder-card-tags"><span v-for="trait in selected.traits" :key="trait">{{ trait }}</span><span v-if="selected.profession">{{ selected.profession }}</span><span v-if="selected.trialValue">试炼值 {{ selected.trialValue }}</span></div>
            <p>{{ selected.effect || '无效果文字' }}</p></div>
        </section>
        <footer :class="{ error: validation }">{{ notice || validation || '牌库合法，可以保存并用于房间对战' }}</footer>
      </aside>
    </main>
    <div v-if="pendingDeleteName" class="builder-modal-mask" @click.self="pendingDeleteName = ''">
      <section class="delete-confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="delete-deck-title">
        <h2 id="delete-deck-title">删除〈{{ pendingDeleteName }}〉？</h2>
        <p>牌库删除后不可找回</p>
        <footer><button class="danger" @click="confirmDelete">继续删除</button><button @click="pendingDeleteName = ''">取消</button></footer>
      </section>
    </div>
    <div v-if="deckImageUrl" class="builder-modal-mask" @click.self="closeDeckImage">
      <section class="deck-image-dialog" role="dialog" aria-modal="true" aria-labelledby="deck-image-title">
        <header><h2 id="deck-image-title">牌库图预览</h2><button aria-label="关闭" @click="closeDeckImage">×</button></header>
        <img :src="deckImageUrl" :alt="`${deckName}牌库图`"/>
        <footer><button class="primary" @click="saveGeneratedDeckImage">下载牌库图</button><button @click="closeDeckImage">关闭</button></footer>
      </section>
    </div>
  </div>
</template>

<style scoped>
.deck-builder-shell{position:absolute;inset:0;display:flex;flex-direction:column;overflow:hidden;background:radial-gradient(circle at 50% 0,rgba(22,108,120,.2),transparent 38%),linear-gradient(135deg,#080b0d,#160b0d 58%,#071216);color:#eee}
.deck-builder-topbar{height:74px;flex:none;display:flex;align-items:center;gap:18px;padding:10px 20px;border-bottom:1px solid #675f59;background:rgba(8,10,12,.94)}
.deck-builder-topbar>div:nth-child(2){margin-right:auto}.deck-builder-topbar small,.kicker{color:#c7a85d;font-size:10px;font-weight:900;letter-spacing:.18em}.deck-builder-topbar h1{margin:2px 0 0;font-size:23px}.deck-builder-topbar label{display:grid;gap:4px;color:#b6bab6;font-size:11px;font-weight:900}.deck-builder-topbar input{width:260px;min-height:38px;padding:8px 11px;font-size:15px;font-weight:900}.deck-builder-topbar button{padding:9px 14px}.deck-builder-topbar .primary{border-color:#e4dfd0;background:#e4dfd0;color:#111;font-weight:900}.deck-builder-topbar .primary:disabled{opacity:.3}.deck-total{display:flex;align-items:baseline;gap:4px;color:#bc5961}.deck-total.valid{color:#5cc1b8}.deck-total b{font-size:25px}.deck-total span{font-size:11px}
.deck-loading{display:grid;flex:1;place-items:center;color:#b7b9b5}.deck-builder-grid{display:grid;grid-template-columns:238px minmax(480px,1fr) 330px;gap:10px;min-height:0;padding:10px}.deck-builder-grid>.grand-panel{min-height:0;padding:13px;border-radius:2px}.deck-builder-grid h2{margin:3px 0 12px;font-size:18px}.deck-filter{overflow:auto}.deck-filter label{display:grid;gap:5px;margin:10px 0;color:#959a96;font-size:10px;font-weight:800}.deck-filter input,.deck-filter select{width:100%}.master-preview{display:flex;gap:9px;align-items:center;padding:8px;border-left:2px solid #42abb3;background:#10191b}.master-preview img{width:52px;height:52px;object-fit:cover;border-radius:2px}.master-preview div{display:grid;gap:3px}.master-preview span,.master-preview small{color:#8f9996;font-size:10px}.preset-kicker{margin-top:18px}.saved-list{display:grid;gap:5px}.saved-list article{border:1px solid #353c3e;background:#111619}.saved-list article>button:first-child{display:grid;width:100%;grid-template-columns:34px minmax(0,1fr);align-items:center;gap:7px;padding:7px;text-align:left}.saved-list article>button:first-child>img{width:34px;height:34px;object-fit:cover;border:1px solid #535e5b;border-radius:2px}.saved-deck-copy{display:grid;min-width:0;gap:2px}.saved-deck-copy b,.saved-deck-copy small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.saved-deck-copy small{color:#89938f;font-size:8px}.saved-list span{color:#737d79;font-size:9px}.saved-list article{display:flex}.saved-list .delete{width:32px;border:0;border-left:1px solid #353c3e;color:#bd5961}.saved-list p{color:#666;font-size:10px}
.deck-catalog{display:flex;flex-direction:column;overflow:hidden}.deck-catalog>header,.deck-list>header{display:flex;align-items:center;justify-content:space-between;flex:none}.deck-catalog>header span{color:#7f8985;font-size:10px}.deck-card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(118px,1fr));gap:9px;overflow:auto;padding:3px 4px 20px}.deck-card{min-width:0;border:1px solid #303638;background:#101416;box-shadow:3px 3px 0 #050607}.deck-card.chosen{border-color:#c5a456}.card-image{position:relative;display:block;width:100%;aspect-ratio:5/7;overflow:hidden;border:0;background:#171d1f}.card-image>.l12-card-image{width:100%;height:100%}.card-image>span{display:grid;height:100%;place-items:center;font-size:24px}.copy-count{position:absolute;right:4px;top:4px;padding:3px 6px;background:#07181a;color:#71d1d0}.deck-card>div{display:grid;gap:2px;padding:7px}.deck-card>div b{overflow:hidden;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.deck-card>div small{color:#757d79;font-size:8px}.add-card{width:100%;padding:6px;border:0;border-top:1px solid #303638;color:#cdbb89;font-size:9px}.add-card:disabled{color:#4d5351}
.deck-card.landscape-thumbnail .card-image>.l12-card-image{position:absolute;left:50%;top:50%;width:140%;height:71.43%;transform:translate(-50%,-50%) rotate(90deg);transform-origin:center}.builder-card-detail.horizontal{max-height:160px}.builder-card-detail.horizontal>.l12-card-image{width:116px;height:auto;aspect-ratio:8/5}
.deck-list{display:flex;flex-direction:column;overflow:hidden}.deck-list>header>b{font-size:27px;color:#65c4c3}.cost-curve{display:flex;height:88px;align-items:end;gap:5px;padding:8px 4px;border-top:1px solid #333;border-bottom:1px solid #333}.cost-curve i{display:grid;flex:1;align-items:end;justify-items:center;height:68px;font-style:normal}.cost-curve i span{width:100%;max-width:22px;background:linear-gradient(#d2b560,#7f6530)}.cost-curve i b,.cost-curve i small{font-size:8px}.cost-curve i small{color:#777}.deck-entries{flex:1;overflow:auto;padding:7px 0}.deck-entries article{display:flex;align-items:center;gap:8px;margin-bottom:4px;padding:5px;border-left:2px solid #3da4ad;background:#111719}.deck-entries article>span{display:grid;width:25px;height:25px;place-items:center;background:#080a0b;color:#eee;font-weight:900}.deck-entries article>div{display:grid;min-width:0;flex:1}.deck-entries article>div b{overflow:hidden;font-size:10px;text-overflow:ellipsis;white-space:nowrap}.deck-entries article small{color:#6e7773;font-size:8px}.deck-entries strong{color:#d4bd7b}.deck-entries button{border:0;color:#ca626a}.deck-entries>p{color:#69716e;font-size:10px;line-height:1.6}.builder-card-detail{display:flex;gap:8px;max-height:130px;padding:8px;border-top:1px solid #3d4241;background:#0b0f10}.builder-card-detail>.l12-card-image{width:72px}.builder-card-detail div{min-width:0;overflow:auto}.builder-card-detail h3{margin:2px 0 5px;font-size:13px}.builder-card-detail p{margin:0;color:#aeb3af;font-size:9px;line-height:1.55;white-space:pre-line}.builder-card-detail small{color:#6cc5c7;font-size:8px}.builder-card-tags{display:flex!important;flex-wrap:wrap;gap:3px;margin:0 0 5px;overflow:visible!important}.builder-card-tags span{padding:2px 4px;border:1px solid #445451;background:#111819;color:#84d1ce;font-size:8px;font-weight:900}.deck-list footer{min-height:32px;padding:8px;border-top:1px solid #315854;color:#72c8bd;font-size:10px}.deck-list footer.error{border-color:#673a3d;color:#d2757b}
@media(max-width:1180px){.deck-builder-grid{grid-template-columns:210px minmax(420px,1fr) 290px}.deck-builder-topbar label{display:none}}
@media(max-width:820px){
  .deck-builder-shell{position:fixed;overflow:auto}.deck-builder-topbar{position:sticky;z-index:20;top:0;height:64px;padding:8px;gap:8px}.deck-builder-topbar>div:nth-child(2) small{display:none}.deck-builder-topbar h1{font-size:18px}.deck-builder-topbar button{padding:7px 9px}.deck-total b{font-size:20px}
  .deck-builder-grid{display:flex;flex-direction:column;overflow:visible;padding:8px}.deck-builder-grid>.grand-panel{overflow:visible}.deck-filter{order:1}.deck-catalog{order:2;min-height:72vh}.deck-list{order:3;min-height:70vh}.deck-card-grid{grid-template-columns:repeat(3,minmax(92px,1fr));max-height:68vh}.deck-entries{max-height:46vh}.master-preview img{width:64px;height:64px}
}
.deck-builder-topbar button{padding:8px 11px;border:1px solid #69716e;background:#171c1d;color:#f1eee5;font-weight:900}.deck-builder-topbar button:hover:not(:disabled){border-color:#70d7df;background:#1b565b;color:#fff}.deck-builder-topbar .back-button{border-color:#d7d2c4;background:#e8e3d7;color:#101314}.deck-builder-topbar .primary{border-color:#e4dfd0;background:#e4dfd0;color:#111}.deck-builder-topbar .delete-deck{border-color:#8c343c;color:#f1a3aa}.deck-builder-topbar button:disabled{color:#717775;background:#252929;opacity:.45}.deck-file-actions{display:flex;gap:6px}.filter-reset{width:100%;min-height:34px;border:1px solid #5d6865;background:#161c1d;color:#eee;font-weight:900}.filter-reset:hover{border-color:#70d7df;background:#1b565b}
.pool-count-controls{display:grid!important;grid-template-columns:1fr 34px 1fr;gap:0!important;padding:0!important;border-top:1px solid #303638}.pool-count-controls button{min-height:30px;border:0;background:#151a1b;color:#e8e4d9;font-size:17px;font-weight:900}.pool-count-controls button:hover:not(:disabled){background:#1d6167;color:#fff}.pool-count-controls strong{display:grid;place-items:center;border-inline:1px solid #303638;background:#090c0d;color:#d7c483;font-size:12px}
.saved-list article{border-color:#424b4d;background:#111619}.saved-list article>button:first-child{background:#111619;color:#f1eee5}.saved-list article>button:first-child:hover,.saved-list article>button:first-child:focus-visible{border-color:#70d7df;background:#18383b;color:#fff}.saved-list b{color:#f1eee5}.saved-list span{color:#aab4b0}.saved-list article.active{border-color:#86e8ee;background:#123e42;box-shadow:inset 3px 0 #86e8ee}.saved-list article.active>button:first-child{background:#123e42;color:#fff}.saved-list article.active span{color:#d5f4f1}.saved-list .delete{background:#211418;color:#f29ba4}.saved-list .delete:hover{background:#6b222b;color:#fff}.saved-list p{color:#929b97}
.deck-entries article{position:relative;isolation:isolate;gap:7px;margin-bottom:5px;padding:6px;overflow:hidden;border:1px solid #354041;border-left:2px solid #3da4ad}.deck-entry-banner{position:absolute;z-index:-2;inset:0;width:100%;height:100%;object-fit:cover;object-position:center 28%;opacity:.56;filter:saturate(.9) contrast(1.12)}.deck-entries article::after{content:'';position:absolute;z-index:-1;inset:0;background:linear-gradient(90deg,rgba(5,8,9,.91),rgba(9,13,14,.48) 48%,rgba(5,8,9,.88))}.deck-entries article>span{width:27px;height:27px;flex:none}.deck-entries article small{color:#d0d5d1}.deck-entries strong{color:#f0d98e}.deck-entries button{width:27px;height:27px;flex:none;border:1px solid #5c6461;background:#101516;color:#eee;font-size:15px;font-weight:900}.deck-entries button:hover:not(:disabled){border-color:#70d7df;background:#1b565b}
@media(max-width:1180px){.deck-file-actions button{padding:7px 8px;font-size:10px}}
@media(max-width:820px){.deck-builder-topbar{height:auto;min-height:64px;flex-wrap:wrap}.deck-file-actions{order:5;width:100%;display:grid;grid-template-columns:repeat(3,1fr)}}
.trial-builder{margin:12px 0;padding:10px;border:1px solid #42605a;background:#0a1212}.trial-builder>header,.selected-trials>header{display:flex;align-items:center;justify-content:space-between}.trial-builder>header span,.selected-trials>header span{color:#78d2be;font-size:10px;font-weight:900}.trial-builder>p{margin:5px 0 9px;color:#84918c;font-size:9px;line-height:1.5}.trial-options{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}.trial-options button{min-width:0;padding:6px;border:1px solid #384744;background:#101817;color:#e9e5dc;text-align:left}.trial-options button.selected{border-color:#6cd5b4;background:#17332c}.trial-options b,.trial-options small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:9px}.trial-options small{margin-top:3px;color:#85908c;font-size:7px}.trial-thumb{position:relative;display:block;width:100%;aspect-ratio:5/7;margin-bottom:5px;overflow:hidden;background:#080b0b}.trial-thumb img{position:absolute;left:50%;top:50%;width:140%;height:71.43%;object-fit:contain;transform:translate(-50%,-50%) rotate(90deg)}
.selected-trials{flex:none;display:grid;gap:4px;padding:8px 0;border-top:1px solid #3d4241}.selected-trials button{display:grid;grid-template-columns:42px 1fr 24px;align-items:center;gap:7px;min-height:34px;border:1px solid #3e514d;background:#101817;color:#eee;text-align:left}.selected-trials .l12-card-image{width:42px;height:30px}.selected-trials span{font-size:9px;font-weight:900}.selected-trials i{display:grid;height:100%;place-items:center;border-left:1px solid #3e514d;color:#db747c;font-style:normal}
.trial-thumb.upright img{width:100%;height:100%;object-fit:cover;transform:translate(-50%,-50%)}.automatic-extra-builder{border-color:#8a6a3d}.automatic-extra-list i{width:auto!important;padding:0 5px!important;color:#cdbb89!important;font-size:7px!important}
.catalog-tabs{display:grid;grid-template-columns:repeat(3,1fr);gap:5px;margin:0 0 10px}.catalog-tabs button,.choose-special{min-height:32px;border:1px solid #48504e;background:#101617;color:#c8cfcb;font-weight:900}.catalog-tabs button.active,.choose-special:hover:not(:disabled){border-color:#73d4d8;background:#194b50;color:#fff}.choose-special{width:100%;border-width:1px 0 0}.empty-extra{grid-column:1/-1;padding:32px;color:#8b9490;text-align:center}
.builder-modal-mask{position:fixed;z-index:1000;inset:0;display:grid;place-items:center;padding:20px;background:rgba(0,4,7,.82);backdrop-filter:blur(6px)}.delete-confirm-dialog,.deck-image-dialog{width:min(92vw,520px);border:1px solid #8b7650;background:#111719;color:#f4f0e6;box-shadow:0 22px 80px #000;padding:22px}.delete-confirm-dialog h2,.deck-image-dialog h2{margin:0;font-size:20px}.delete-confirm-dialog p{margin:18px 0;color:#d9c9af;font-weight:900}.delete-confirm-dialog footer,.deck-image-dialog footer{display:flex;justify-content:center;gap:10px}.delete-confirm-dialog button,.deck-image-dialog button{min-width:120px;padding:10px 16px;border:1px solid #66716e;background:#171d1f;color:#f4f0e6;font-weight:900}.delete-confirm-dialog .danger{border-color:#a23943;background:#681f28;color:#fff}.deck-image-dialog{width:min(94vw,1100px)}.deck-image-dialog>header{display:flex;align-items:center;justify-content:space-between;margin-bottom:14px}.deck-image-dialog>header button{min-width:42px}.deck-image-dialog>img{display:block;width:100%;max-height:70vh;object-fit:contain;background:#070a0c}.deck-image-dialog footer{margin-top:14px}.deck-image-dialog .primary{border-color:#d9bc72;background:#d9bc72;color:#111}
.saved-list article>button:first-child{display:block;padding:0}.saved-list article>button:first-child :deep(.deck-profile){border:0;background:transparent}.saved-list article.active>button:first-child :deep(.deck-profile){background:#123e42}
</style>
