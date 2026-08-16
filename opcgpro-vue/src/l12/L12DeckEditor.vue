<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import {
  MAIN_DECK_TYPES, buildMoraleDeck, deleteDeck, loadDeckCatalog, loadSavedDecks,
  saveDeck, validateDeck, type DeckCard, type SavedL12Deck,
} from './decks'

interface PresetDeck { name: string; masterId: string; cardIds: string[]; moraleIds: string[] }

const router = useRouter()
const catalog = ref<DeckCard[]>([])
const presets = ref<PresetDeck[]>([])
const savedDecks = ref<Record<string, SavedL12Deck>>({})
const loading = ref(true)
const notice = ref('')
const deckName = ref('新牌库')
const masterId = ref('')
const counts = ref<Record<string, number>>({})
const query = ref('')
const typeFilter = ref('all')
const productFilter = ref('all')
const selected = ref<DeckCard | null>(null)
const activeDeckName = ref<string | null>(null)

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界',
}
const typeLabels: Record<string, string> = {
  legion: '军团', tactic: '战术', artifact: '圣物',
}

onMounted(async () => {
  try {
    const [cards, presetResponse] = await Promise.all([
      loadDeckCatalog(), fetch('/data/l12/preset-decks.s1.json'),
    ])
    catalog.value = cards
    presets.value = presetResponse.ok ? await presetResponse.json() : []
    savedDecks.value = loadSavedDecks()
    const requested = typeof router.currentRoute.value.query.deck === 'string' ? router.currentRoute.value.query.deck : ''
    if (requested && savedDecks.value[requested]) loadDeck(savedDecks.value[requested])
    else selected.value = mainCards.value[0] ?? null
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库编辑器加载失败'
  } finally {
    loading.value = false
  }
})

const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const masters = computed(() => catalog.value.filter(card => card.cardType === 'master'))
const selectedMaster = computed(() => byId.value.get(masterId.value))
const mainCards = computed(() => catalog.value.filter(card => MAIN_DECK_TYPES.has(card.cardType)))
const totalCards = computed(() => Object.entries(counts.value).reduce((sum, [id, value]) => sum + (id === 'S01-0212' ? 0 : value), 0))
const tombGuardCount = computed(() => counts.value['S01-0212'] || 0)
const moraleIds = computed(() => buildMoraleDeck(selectedMaster.value, catalog.value))
const entries = computed(() => Object.entries(counts.value)
  .filter(([, count]) => count > 0)
  .map(([id, count]) => ({ card: byId.value.get(id)!, count }))
  .filter(entry => entry.card)
  .sort((a, b) => (a.card.cost ?? 99) - (b.card.cost ?? 99) || a.card.number.localeCompare(b.card.number)))
const filtered = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const master = selectedMaster.value
  return mainCards.value.filter(card => {
    if (master && card.faction !== 'universal' && card.faction !== master.faction) return false
    if (typeFilter.value !== 'all' && cardTypeFilterKey(card.cardType) !== typeFilter.value) return false
    if (productFilter.value !== 'all' && card.product !== productFilter.value) return false
    return !keyword || [card.nameZh, card.number, card.effect].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
  }).sort((a, b) => a.number.localeCompare(b.number))
})
const validation = computed(() => validateDeck({
  name: deckName.value, masterId: masterId.value,
  cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
  moraleIds: moraleIds.value,
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
}

function add(card: DeckCard) {
  if (!selectedMaster.value) { notice.value = '请先选择主宰'; return }
  const count = counts.value[card.id] || 0
  if (count >= 3) { notice.value = '同编号卡牌最多 3 张'; return }
  if (card.id !== 'S01-0212' && totalCards.value >= 50) { notice.value = '主牌库最多 50 张'; return }
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
  activeDeckName.value = null
  deckName.value = '新牌库'
  masterId.value = ''
  counts.value = {}
  selected.value = mainCards.value[0] ?? null
  notice.value = '已新建空白牌库'
}

function currentDeck(): SavedL12Deck {
  return {
    name: deckName.value.trim(), masterId: masterId.value,
    cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
    moraleIds: moraleIds.value, updatedAt: new Date().toISOString(),
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
  activeDeckName.value = name
  deckName.value = name
  savedDecks.value = loadSavedDecks()
  notice.value = `已另存为〈${name}〉`
}

function loadDeck(deck: SavedL12Deck) {
  activeDeckName.value = deck.name
  deckName.value = deck.name
  masterId.value = deck.masterId
  const next: Record<string, number> = {}
  deck.cardIds.forEach(id => next[id] = (next[id] || 0) + 1)
  counts.value = next
  notice.value = `已载入〈${deck.name}〉`
}

function importPreset(preset: PresetDeck) {
  activeDeckName.value = null
  deckName.value = `${preset.name}·自定义`
  masterId.value = preset.masterId
  const next: Record<string, number> = {}
  preset.cardIds.forEach(id => next[id] = (next[id] || 0) + 1)
  counts.value = next
  notice.value = `已从〈${preset.name}〉建立副本`
}

function onDelete(name = activeDeckName.value ?? '') {
  if (!name) { notice.value = '当前不是已保存牌库'; return }
  deleteDeck(name)
  savedDecks.value = loadSavedDecks()
  if (activeDeckName.value === name) newDeck()
  notice.value = `已删除〈${name}〉`
}
</script>

<template>
  <div class="deck-builder-shell">
    <header class="deck-builder-topbar">
      <button class="back-button" @click="router.push('/decks')">← 返回牌库</button>
      <div><small>GRANDUMI FRAMEWORK · LEGION12 STYLE</small><h1>牌库编辑器</h1></div>
      <label>牌库名称<input v-model="deckName" maxlength="24"/></label>
      <div class="deck-total" :class="{ valid: !validation }"><b>{{ totalCards }}</b><span>/ 40–50<span v-if="tombGuardCount"> ＋ 陵墓守卫 {{ tombGuardCount }}</span></span></div>
      <div class="deck-file-actions">
        <button @click="newDeck">新建牌库</button>
        <button class="primary" :disabled="!!validation" @click="onSave">保存牌库</button>
        <button :disabled="!!validation" @click="onSaveAs">另存为牌库</button>
        <button class="delete-deck" :disabled="!activeDeckName" @click="onDelete()">删除牌库</button>
      </div>
    </header>

    <main v-if="loading" class="deck-loading">正在载入 248 张卡牌…</main>
    <main v-else class="deck-builder-grid">
      <aside class="deck-filter grand-panel">
        <p class="kicker">COMMAND</p><h2>构筑设定</h2>
        <label>主宰<select :value="masterId" @change="chooseMaster(($event.target as HTMLSelectElement).value)">
          <option value="">选择主宰</option>
          <option v-for="master in masters" :key="master.id" :value="master.id">{{ master.nameZh }} · {{ factionLabels[master.faction] }}</option>
        </select></label>
        <article v-if="selectedMaster" class="master-preview">
          <img v-if="selectedMaster.imageUrl" :src="selectedMaster.imageUrl" :alt="selectedMaster.nameZh"/>
          <div><b>{{ selectedMaster.nameZh }}</b><span>{{ factionLabels[selectedMaster.faction] }}</span><small>士气 {{ moraleIds.length }} 张</small></div>
        </article>
        <label>搜索<input v-model="query" placeholder="卡名、编号、效果"/></label>
        <label>类型<select v-model="typeFilter"><option value="all">全部主牌</option><option v-for="(label,key) in typeLabels" :key="key" :value="key">{{ label }}</option></select></label>
        <label>卡池<select v-model="productFilter"><option value="all">S1 + S2</option><option value="S01">S1</option><option value="S02">S2</option></select></label>

        <p class="kicker preset-kicker">STARTER COPY</p>
        <div class="preset-list"><button v-for="preset in presets" :key="preset.name" @click="importPreset(preset)"><b>{{ preset.name }}</b><span>建立可编辑副本</span></button></div>

        <p class="kicker preset-kicker">SAVED DECKS</p>
        <div class="saved-list"><article v-for="deck in savedDecks" :key="deck.name"><button @click="loadDeck(deck)"><b>{{ deck.name }}</b><span>{{ deck.cardIds.length }} 张 · {{ byId.get(deck.masterId)?.nameZh }}</span></button><button class="delete" @click="onDelete(deck.name)">×</button></article><p v-if="!Object.keys(savedDecks).length">暂无本地牌库</p></div>
      </aside>

      <section class="deck-catalog grand-panel">
        <header><div><p class="kicker">CARD POOL</p><h2>可用卡牌</h2></div><span>{{ filtered.length }} 张结果</span></header>
        <div class="deck-card-grid">
          <article v-for="card in filtered" :key="card.id" class="deck-card" :class="{ chosen: counts[card.id], 'landscape-thumbnail': isHorizontalCardType(card.cardType) }" @click="selected = card">
            <button class="card-image" @dblclick.stop="add(card)">
              <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.nameZh" loading="lazy"/>
              <span v-else>XII</span><b v-if="counts[card.id]" class="copy-count">×{{ counts[card.id] }}</b>
            </button>
            <div><b>{{ card.nameZh }}</b><small>{{ card.number }} · {{ cardTypeLabel(card.cardType) }}</small></div>
            <div class="pool-count-controls">
              <button :disabled="!(counts[card.id] || 0)" aria-label="减少一张" @click.stop="remove(card.id)">−</button>
              <strong>{{ counts[card.id] || 0 }}</strong>
              <button :disabled="!masterId || (counts[card.id] || 0) >= 3 || (card.id !== 'S01-0212' && totalCards >= 50)" aria-label="增加一张" @click.stop="add(card)">＋</button>
            </div>
          </article>
        </div>
      </section>

      <aside class="deck-list grand-panel">
        <header><div><p class="kicker">DECK LIST</p><h2>{{ selectedMaster?.nameZh || '未选择主宰' }}</h2></div><b>{{ totalCards }}</b></header>
        <div class="cost-curve"><i v-for="(value,index) in curve" :key="index"><span :style="{height:`${Math.max(4, value / maxCurve * 56)}px`}"></span><b>{{ index === 8 ? '8+' : index }}</b><small>{{ value }}</small></i></div>
        <div class="deck-entries"><article v-for="entry in entries" :key="entry.card.id" @click="selected = entry.card">
          <img v-if="entry.card.imageUrl" class="deck-entry-banner" :src="entry.card.imageUrl" :alt="entry.card.nameZh" loading="lazy"/>
          <span>{{ entry.card.cost ?? '—' }}</span><div><b>{{ entry.card.nameZh }}</b><small>{{ entry.card.number }}</small></div><strong>×{{ entry.count }}</strong>
          <button aria-label="增加一张" :disabled="entry.count >= 3 || (entry.card.id !== 'S01-0212' && totalCards >= 50)" @click.stop="add(entry.card)">＋</button>
          <button aria-label="减少一张" @click.stop="remove(entry.card.id)">−</button>
        </article><p v-if="!entries.length">从中间卡池加入卡牌，双击卡面也可快速加入。</p></div>
        <section v-if="selected" class="builder-card-detail" :class="{ horizontal: isHorizontalCardType(selected.cardType) }">
          <img v-if="selected.imageUrl" :src="selected.imageUrl" :alt="selected.nameZh"/>
          <div><small>{{ selected.number }}</small><h3>{{ selected.nameZh }}</h3><p>{{ selected.effect || '无效果文字' }}</p></div>
        </section>
        <footer :class="{ error: validation }">{{ notice || validation || '牌库合法，可以保存并用于房间对战' }}</footer>
      </aside>
    </main>
  </div>
</template>

<style scoped>
.deck-builder-shell{position:absolute;inset:0;display:flex;flex-direction:column;overflow:hidden;background:radial-gradient(circle at 50% 0,rgba(22,108,120,.2),transparent 38%),linear-gradient(135deg,#080b0d,#160b0d 58%,#071216);color:#eee}
.deck-builder-topbar{height:74px;flex:none;display:flex;align-items:center;gap:18px;padding:10px 20px;border-bottom:1px solid #675f59;background:rgba(8,10,12,.94)}
.deck-builder-topbar>div:nth-child(2){margin-right:auto}.deck-builder-topbar small,.kicker{color:#c7a85d;font-size:10px;font-weight:900;letter-spacing:.18em}.deck-builder-topbar h1{margin:2px 0 0;font-size:23px}.deck-builder-topbar label{display:grid;gap:4px;color:#8d918e;font-size:9px;font-weight:800}.deck-builder-topbar input{width:190px}.deck-builder-topbar button{padding:9px 14px}.deck-builder-topbar .primary{border-color:#e4dfd0;background:#e4dfd0;color:#111;font-weight:900}.deck-builder-topbar .primary:disabled{opacity:.3}.deck-total{display:flex;align-items:baseline;gap:4px;color:#bc5961}.deck-total.valid{color:#5cc1b8}.deck-total b{font-size:25px}.deck-total span{font-size:11px}
.deck-loading{display:grid;flex:1;place-items:center;color:#b7b9b5}.deck-builder-grid{display:grid;grid-template-columns:238px minmax(480px,1fr) 330px;gap:10px;min-height:0;padding:10px}.deck-builder-grid>.grand-panel{min-height:0;padding:13px;border-radius:2px}.deck-builder-grid h2{margin:3px 0 12px;font-size:18px}.deck-filter{overflow:auto}.deck-filter label{display:grid;gap:5px;margin:10px 0;color:#959a96;font-size:10px;font-weight:800}.deck-filter input,.deck-filter select{width:100%}.master-preview{display:flex;gap:9px;align-items:center;padding:8px;border-left:2px solid #42abb3;background:#10191b}.master-preview img{width:46px;height:64px;object-fit:cover;border-radius:1px}.master-preview div{display:grid;gap:3px}.master-preview span,.master-preview small{color:#8f9996;font-size:10px}.preset-kicker{margin-top:18px}.preset-list,.saved-list{display:grid;gap:5px}.preset-list button,.saved-list article{border:1px solid #353c3e;background:#111619}.preset-list button,.saved-list article>button:first-child{display:grid;width:100%;gap:2px;padding:8px;text-align:left}.preset-list span,.saved-list span{color:#737d79;font-size:9px}.saved-list article{display:flex}.saved-list .delete{width:32px;border:0;border-left:1px solid #353c3e;color:#bd5961}.saved-list p{color:#666;font-size:10px}
.deck-catalog{display:flex;flex-direction:column;overflow:hidden}.deck-catalog>header,.deck-list>header{display:flex;align-items:center;justify-content:space-between;flex:none}.deck-catalog>header span{color:#7f8985;font-size:10px}.deck-card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(118px,1fr));gap:9px;overflow:auto;padding:3px 4px 20px}.deck-card{min-width:0;border:1px solid #303638;background:#101416;box-shadow:3px 3px 0 #050607}.deck-card.chosen{border-color:#c5a456}.card-image{position:relative;display:block;width:100%;aspect-ratio:5/7;overflow:hidden;border:0;background:#171d1f}.card-image img{width:100%;height:100%;object-fit:cover}.card-image>span{display:grid;height:100%;place-items:center;font-size:24px}.copy-count{position:absolute;right:4px;top:4px;padding:3px 6px;background:#07181a;color:#71d1d0}.deck-card>div{display:grid;gap:2px;padding:7px}.deck-card>div b{overflow:hidden;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.deck-card>div small{color:#757d79;font-size:8px}.add-card{width:100%;padding:6px;border:0;border-top:1px solid #303638;color:#cdbb89;font-size:9px}.add-card:disabled{color:#4d5351}
.deck-card.landscape-thumbnail .card-image img{position:absolute;left:50%;top:50%;width:140%;height:71.43%;object-fit:contain;transform:translate(-50%,-50%) rotate(90deg);transform-origin:center}.builder-card-detail.horizontal{max-height:160px}.builder-card-detail.horizontal img{width:116px;height:auto;aspect-ratio:8/5;object-fit:contain}
.deck-list{display:flex;flex-direction:column;overflow:hidden}.deck-list>header>b{font-size:27px;color:#65c4c3}.cost-curve{display:flex;height:88px;align-items:end;gap:5px;padding:8px 4px;border-top:1px solid #333;border-bottom:1px solid #333}.cost-curve i{display:grid;flex:1;align-items:end;justify-items:center;height:68px;font-style:normal}.cost-curve i span{width:100%;max-width:22px;background:linear-gradient(#d2b560,#7f6530)}.cost-curve i b,.cost-curve i small{font-size:8px}.cost-curve i small{color:#777}.deck-entries{flex:1;overflow:auto;padding:7px 0}.deck-entries article{display:flex;align-items:center;gap:8px;margin-bottom:4px;padding:5px;border-left:2px solid #3da4ad;background:#111719}.deck-entries article>span{display:grid;width:25px;height:25px;place-items:center;background:#080a0b;color:#eee;font-weight:900}.deck-entries article>div{display:grid;min-width:0;flex:1}.deck-entries article>div b{overflow:hidden;font-size:10px;text-overflow:ellipsis;white-space:nowrap}.deck-entries article small{color:#6e7773;font-size:8px}.deck-entries strong{color:#d4bd7b}.deck-entries button{border:0;color:#ca626a}.deck-entries>p{color:#69716e;font-size:10px;line-height:1.6}.builder-card-detail{display:flex;gap:8px;max-height:130px;padding:8px;border-top:1px solid #3d4241;background:#0b0f10}.builder-card-detail img{width:72px;object-fit:cover}.builder-card-detail div{min-width:0;overflow:auto}.builder-card-detail h3{margin:2px 0 5px;font-size:13px}.builder-card-detail p{margin:0;color:#aeb3af;font-size:9px;line-height:1.55;white-space:pre-line}.builder-card-detail small{color:#6cc5c7;font-size:8px}.deck-list footer{min-height:32px;padding:8px;border-top:1px solid #315854;color:#72c8bd;font-size:10px}.deck-list footer.error{border-color:#673a3d;color:#d2757b}
@media(max-width:1180px){.deck-builder-grid{grid-template-columns:210px minmax(420px,1fr) 290px}.deck-builder-topbar label{display:none}}
@media(max-width:820px){
  .deck-builder-shell{position:fixed;overflow:auto}.deck-builder-topbar{position:sticky;z-index:20;top:0;height:64px;padding:8px;gap:8px}.deck-builder-topbar>div:nth-child(2) small{display:none}.deck-builder-topbar h1{font-size:18px}.deck-builder-topbar button{padding:7px 9px}.deck-total b{font-size:20px}
  .deck-builder-grid{display:flex;flex-direction:column;overflow:visible;padding:8px}.deck-builder-grid>.grand-panel{overflow:visible}.deck-filter{order:1}.deck-catalog{order:2;min-height:72vh}.deck-list{order:3;min-height:70vh}.deck-card-grid{grid-template-columns:repeat(3,minmax(92px,1fr));max-height:68vh}.deck-entries{max-height:46vh}.master-preview img{width:64px;height:90px}
}
.deck-builder-topbar button{padding:8px 11px;border:1px solid #69716e;background:#171c1d;color:#f1eee5;font-weight:900}.deck-builder-topbar button:hover:not(:disabled){border-color:#70d7df;background:#1b565b;color:#fff}.deck-builder-topbar .back-button{border-color:#d7d2c4;background:#e8e3d7;color:#101314}.deck-builder-topbar .primary{border-color:#e4dfd0;background:#e4dfd0;color:#111}.deck-builder-topbar .delete-deck{border-color:#8c343c;color:#f1a3aa}.deck-builder-topbar button:disabled{color:#717775;background:#252929;opacity:.45}.deck-file-actions{display:flex;gap:6px}
.pool-count-controls{display:grid!important;grid-template-columns:1fr 34px 1fr;gap:0!important;padding:0!important;border-top:1px solid #303638}.pool-count-controls button{min-height:30px;border:0;background:#151a1b;color:#e8e4d9;font-size:17px;font-weight:900}.pool-count-controls button:hover:not(:disabled){background:#1d6167;color:#fff}.pool-count-controls strong{display:grid;place-items:center;border-inline:1px solid #303638;background:#090c0d;color:#d7c483;font-size:12px}
.deck-entries article{position:relative;isolation:isolate;gap:7px;margin-bottom:5px;padding:6px;overflow:hidden;border:1px solid #354041;border-left:2px solid #3da4ad}.deck-entry-banner{position:absolute;z-index:-2;inset:0;width:100%;height:100%;object-fit:cover;object-position:center 28%;opacity:.56;filter:saturate(.9) contrast(1.12)}.deck-entries article::after{content:'';position:absolute;z-index:-1;inset:0;background:linear-gradient(90deg,rgba(5,8,9,.91),rgba(9,13,14,.48) 48%,rgba(5,8,9,.88))}.deck-entries article>span{width:27px;height:27px;flex:none}.deck-entries article small{color:#d0d5d1}.deck-entries strong{color:#f0d98e}.deck-entries button{width:27px;height:27px;flex:none;border:1px solid #5c6461;background:#101516;color:#eee;font-size:15px;font-weight:900}.deck-entries button:hover:not(:disabled){border-color:#70d7df;background:#1b565b}
@media(max-width:1180px){.deck-file-actions button{padding:7px 8px;font-size:10px}}
@media(max-width:820px){.deck-builder-topbar{height:auto;min-height:64px;flex-wrap:wrap}.deck-file-actions{order:5;width:100%;display:grid;grid-template-columns:repeat(4,1fr)}}
</style>
