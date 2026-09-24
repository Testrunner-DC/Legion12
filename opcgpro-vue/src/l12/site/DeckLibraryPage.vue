<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { createDeckImageBlob, decodeDeckCode, downloadDeckImage, encodeDeckCode } from './deckShare'
import { deckCountSummary, deleteDeck as deleteSavedDeck, ensureOfficialPrebuiltDecks, loadDeckCatalog, loadOfficialPresetDecks, loadSavedDecks, saveDeck, validateDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { getEffectiveOperationsPolicy, platformState, publicDeckApi, type EffectiveOperationsPolicy, type PublishedDeck, type PublicDeckGuide, type PublicDeckMatchup } from '@/l12/platform'
import { useRoute, useRouter } from 'vue-router'
import DeckProfile from '@/l12/DeckProfile.vue'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import MobileFilterSheet from './MobileFilterSheet.vue'
import { useActionGate } from '@/l12/useActionGate'

const tab = ref<'mine' | 'plaza'>('mine')
const catalog = ref<DeckCard[]>([])
const saved = ref<Record<string, SavedL12Deck>>({})
const published = ref<PublishedDeck[]>([])
const operationsPolicy = ref<EffectiveOperationsPolicy | null>(null)
const query = ref('')
const importCode = ref('')
const notice = ref('')
const publishName = ref('')
const showPublish = ref(false)
const factionFilter = ref('all')
const masterFilter = ref('all')
const legalFilter = ref<'all' | 'legal' | 'illegal'>('all')
const cardFilter = ref('')
const updatedFilter = ref<'all' | '1' | '7' | '30' | '90' | '365'>('all')
const sortMode = ref<'trend' | 'copies' | 'likes' | 'views' | 'latest' | 'name'>('trend')
const plazaFiltersOpen = ref(false)
const imagePreview = ref<{ deck: SavedL12Deck; blob: Blob; url: string } | null>(null)
const deletingMine = ref('')
const mineQuery = ref('')
const mineHomeCityFilter = ref('all')
const mineLegalFilter = ref<'all' | 'legal' | 'illegal'>('all')
const mineSort = ref<'latest' | 'name'>('latest')
const cardPickerOpen = ref(false)
const publishGuide = ref<PublicDeckGuide>({ buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' })
const publishMatchups = ref<PublicDeckMatchup[]>([])
const route = useRoute()
const router = useRouter()
const { pending: actionBusy, isPending: actionPending, run: runAction } = useActionGate()
const publicDeckActionKey = (deckId: string, accountId = platformState.account?.id ?? 'anonymous') =>
  `public-deck:${accountId}:${deckId}`
const returnTo = computed(() => typeof route.query.from === 'string' && route.query.from.startsWith('/') ? route.query.from : '/decks')
const editorLink = (deckName?: string, publicationId?: string) => ({ path: '/deck-editor', query: { ...(deckName ? { deck: deckName } : {}), ...(publicationId ? { published: publicationId } : {}), returnTo: returnTo.value } })

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界',
}
onMounted(async () => {
  restoreFiltersFromRoute()
  try {
    ;[catalog.value, saved.value] = await Promise.all([
      loadDeckCatalog(),
      ensureOfficialPrebuiltDecks(),
    ])
    const [presets, community, policy] = await Promise.all([
      loadOfficialPresetDecks(), publicDeckApi.list(), getEffectiveOperationsPolicy().catch(() => null),
    ])
    operationsPolicy.value = policy
    published.value = [
      ...presets.map((deck, index) => ({ id: `official-${index}`, ownerId: 'official', deck: { ...deck, specialIds: deck.specialIds ?? [], updatedAt: '' }, author: '十二军团官方预组', views: 0, likes: 0, copies: 0, liked: false, official: true, createdAt: '', updatedAt: '' })),
      ...community,
    ]
    await nextTick()
    const savedScroll = sessionStorage.getItem(`l12:deck-library:scroll:${route.fullPath}`)
    if (savedScroll) window.scrollTo({ top: Number(savedScroll) || 0 })
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库页面加载失败'
  }
})

const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const mine = computed(() => Object.values(saved.value).sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)))
const homeCities = computed(() => [...new Set(mine.value.map(deck => deck.masterId))].map(id => byId.value.get(id)).filter(Boolean) as DeckCard[])
const filteredMine = computed(() => {
  const keyword = mineQuery.value.trim().toLocaleLowerCase('zh-CN')
  const values = mine.value.filter(deck => {
    const error = validateDeck(deck, catalog.value, operationsPolicy.value?.cardRestrictions)
    return (!keyword || deck.name.toLocaleLowerCase('zh-CN').includes(keyword))
      && (mineHomeCityFilter.value === 'all' || deck.masterId === mineHomeCityFilter.value)
      && (mineLegalFilter.value === 'all' || (mineLegalFilter.value === 'legal') === !error)
  })
  return [...values].sort((left, right) => mineSort.value === 'name'
    ? left.name.localeCompare(right.name, 'zh-CN') : right.updatedAt.localeCompare(left.updatedAt))
})
const mineFilterActive = computed(() => Boolean(mineQuery.value.trim()) || mineHomeCityFilter.value !== 'all' || mineLegalFilter.value !== 'all' || mineSort.value !== 'latest')
const plazaFactions = computed(() => [...new Set(published.value.map(entry => byId.value.get(entry.deck.masterId)?.faction).filter(Boolean) as string[])])
const plazaMasters = computed(() => [...new Set(published.value.map(entry => entry.deck.masterId))].map(id => byId.value.get(id)).filter(Boolean) as DeckCard[])
const plazaCards = computed(() => [...new Set(published.value.flatMap(entry => [...entry.deck.cardIds, ...entry.deck.moraleIds, ...(entry.deck.specialIds ?? [])]))].map(id => byId.value.get(id)).filter(Boolean) as DeckCard[])
const plazaCardPickerItems = computed<SingleCardPickerItem[]>(() => plazaCards.value.map(card => ({
  id: card.id, cardId: card.id, number: card.number, name: card.nameZh, nameZh: card.nameZh,
  cardType: card.cardType, faction: card.faction, product: card.product, cost: card.cost,
  disasterLevel: card.disasterLevel, effect: card.effect, imageUrl: card.imageUrl, cardImageId: card.id, detailCard: card,
})))
const publishHomeCities = computed(() => catalog.value.filter(card => card.cardType === 'master'))
const filteredPublished = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const updatedAfter = updatedFilter.value === 'all' ? 0 : Date.now() - Number(updatedFilter.value) * 86400000
  const values = published.value.filter(entry => {
    const master = byId.value.get(entry.deck.masterId)
    return (factionFilter.value === 'all' || master?.faction === factionFilter.value)
      && (masterFilter.value === 'all' || entry.deck.masterId === masterFilter.value)
      && (legalFilter.value === 'all' || seasonRequirement(entry).compliant === (legalFilter.value === 'legal'))
      && (!cardFilter.value || [...entry.deck.cardIds, ...entry.deck.moraleIds, ...(entry.deck.specialIds ?? [])].includes(cardFilter.value))
      && (!updatedAfter || !entry.updatedAt || Date.parse(entry.updatedAt) >= updatedAfter)
      && (!keyword || [entry.deck.name, entry.author, master?.nameZh].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword)))
  })
  return [...values].sort((a, b) => sortMode.value === 'name'
    ? a.deck.name.localeCompare(b.deck.name, 'zh-CN')
    : sortMode.value === 'trend'
      ? (b.copies * 4 + b.likes * 3 + (b.views ?? 0)) - (a.copies * 4 + a.likes * 3 + (a.views ?? 0)) || b.updatedAt.localeCompare(a.updatedAt)
      : sortMode.value === 'latest'
    ? b.createdAt.localeCompare(a.createdAt) || a.id.localeCompare(b.id)
    : sortMode.value === 'likes'
      ? b.likes - a.likes || b.createdAt.localeCompare(a.createdAt) || a.id.localeCompare(b.id)
      : sortMode.value === 'views'
        ? (b.views ?? 0) - (a.views ?? 0) || b.createdAt.localeCompare(a.createdAt) || a.id.localeCompare(b.id)
        : b.copies - a.copies || b.createdAt.localeCompare(a.createdAt) || a.id.localeCompare(b.id))
})
const plazaFilterCount = computed(() => [factionFilter.value !== 'all', masterFilter.value !== 'all', legalFilter.value !== 'all', !!cardFilter.value, updatedFilter.value !== 'all', sortMode.value !== 'trend'].filter(Boolean).length)
const plazaFilterSummary = computed(() => [
  factionFilter.value === 'all' ? '' : (factionLabels[factionFilter.value] || factionFilter.value),
  masterFilter.value === 'all' ? '' : byId.value.get(masterFilter.value)?.nameZh,
  legalFilter.value === 'all' ? '' : legalFilter.value === 'legal' ? '符合本赛季' : '不符合本赛季',
  cardFilter.value ? `含${byId.value.get(cardFilter.value)?.nameZh || cardFilter.value}` : '',
  updatedFilter.value === 'all' ? '' : `${updatedFilter.value}天内更新`,
  sortMode.value === 'trend' ? '' : ({ copies: '最多复制', likes: '最多点赞', views: '最多浏览', latest: '最新发布', name: '按名称' } as const)[sortMode.value],
].filter(Boolean).join(' · '))

function uniqueName(base: string) {
  if (!saved.value[base]) return base.slice(0, 24)
  let index = 2
  let value = `${base} ${index}`.slice(0, 24)
  while (saved.value[value]) value = `${base} ${++index}`.slice(0, 24)
  return value
}
async function copyToMine(entry: PublishedDeck) {
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(entry.id, accountId), async () => {
    const deck = { ...entry.deck, name: uniqueName(entry.deck.name), cardIds: [...entry.deck.cardIds], moraleIds: [...entry.deck.moraleIds], specialIds: [...(entry.deck.specialIds ?? [])], updatedAt: new Date().toISOString() }
    try {
      const confirmed = await saveDeck(deck)
      if (accountId === platformState.account?.id) {
        saved.value = loadSavedDecks()
        notice.value = `已复制《${confirmed.name}》到我的牌库`
      }
      if (!entry.official) {
        const updated = await publicDeckApi.recordCopy(entry.id).catch(() => null)
        if (updated && accountId === platformState.account?.id) updatePublished(updated)
      }
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '复制到我的牌库失败'
    }
  })
}
function updatePublished(entry: PublishedDeck) {
  const index = published.value.findIndex(item => item.id === entry.id)
  if (index >= 0) published.value[index] = entry
}
function publishedCopyFor(deck: SavedL12Deck) {
  return published.value.find(item => !item.official && item.ownerId === platformState.account?.id && item.deck.name === deck.name)
}
async function deleteMine(deck: SavedL12Deck) {
  if (deletingMine.value) return
  const stillPublic = publishedCopyFor(deck)
  const message = stillPublic
    ? `确定删除我的牌库《${deck.name}》？公开版本仍会长期保留，并继续显示在公开牌库。`
    : `确定删除我的牌库《${deck.name}》？此操作不会删除任何公开版本。`
  if (!window.confirm(message)) return
  deletingMine.value = deck.name
  try {
    await deleteSavedDeck(deck.name)
    saved.value = loadSavedDecks()
    notice.value = stillPublic ? `已删除本地牌库《${deck.name}》，公开版本保持不变` : `已删除《${deck.name}》`
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '删除牌库失败'
  } finally {
    deletingMine.value = ''
  }
}
function openDeck(entry: PublishedDeck) {
  sessionStorage.setItem(`l12:deck-library:scroll:${route.fullPath}`, String(window.scrollY))
  void router.push({ name: 'public-deck-detail', params: { deckId: entry.id }, query: { from: route.fullPath } })
}
function seasonRequirement(entry: PublishedDeck) {
  if (!entry.official && entry.seasonCompliant !== undefined) return entry.seasonCompliant
    ? { compliant: true, label: '符合本赛季', reason: operationsPolicy.value?.season.name ? `符合${operationsPolicy.value.season.name}构筑要求` : '符合当前赛季构筑要求' }
    : { compliant: false, label: '不符合本赛季', reason: entry.seasonComplianceReason || '不符合当前赛季构筑要求' }
  if (!operationsPolicy.value) return { compliant: false, label: '赛季要求未知', reason: '当前无法读取赛季规则' }
  const reason = validateDeck(entry.deck, catalog.value, operationsPolicy.value.cardRestrictions)
  return reason
    ? { compliant: false, label: '不符合本赛季', reason }
    : { compliant: true, label: '符合本赛季', reason: `符合${operationsPolicy.value.season.name || '当前赛季'}构筑要求` }
}
function deckFaction(entry: PublishedDeck) {
  return byId.value.get(entry.deck.masterId)?.faction || 'universal'
}
async function toggleLike(entry: PublishedDeck) {
  if (entry.official) return
  if (!platformState.account) { notice.value = '请先登录账号再点赞'; return }
  const accountId = platformState.account.id
  await runAction(publicDeckActionKey(entry.id, accountId), async () => {
    try {
      const updated = await publicDeckApi.toggleLike(entry.id)
      if (accountId === platformState.account?.id) updatePublished(updated)
    }
    catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '点赞失败'
    }
  })
}
async function publishDeck() {
  const deck = saved.value[publishName.value]
  if (!deck) return
  if (!platformState.account) { notice.value = '请先登录账号再公开牌库'; return }
  const error = validateDeck(deck, catalog.value)
  if (error) { notice.value = error; return }
  await runAction(`public-deck:publish:${deck.name}`, async () => {
    try {
      const entry = await publicDeckApi.publish(deck)
      const hasContent = Object.values(publishGuide.value).some(value => value.trim()) || publishMatchups.value.length > 0
      if (hasContent) entry.details = await publicDeckApi.updateContent(entry.id, publishGuide.value, publishMatchups.value)
      if (published.value.some(item => item.id === entry.id)) updatePublished(entry)
      else published.value.push(entry)
      showPublish.value = false; tab.value = 'plaza'; notice.value = hasContent ? '牌库与公开内容已同步发布' : '牌库已公开到公开牌库'
    } catch (error) { notice.value = error instanceof Error ? error.message : '公开牌库失败' }
  })
}
async function editPublished(entry: PublishedDeck) {
  const deck = { ...entry.deck, cardIds: [...entry.deck.cardIds], moraleIds: [...entry.deck.moraleIds], specialIds: [...entry.deck.specialIds] }
  try {
    const confirmed = await saveDeck(deck)
    saved.value = loadSavedDecks()
    await router.push(editorLink(confirmed.name, entry.id))
  } catch (error) { notice.value = error instanceof Error ? error.message : '牌库保存失败' }
}
async function deletePublished(entry: PublishedDeck) {
  if (!window.confirm('确定删除这个公开牌库？删除后将不再显示在公开牌库。')) return
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(entry.id, accountId), async () => {
    try {
      await publicDeckApi.delete(entry.id)
      if (accountId === platformState.account?.id) {
        published.value = published.value.filter(item => item.id !== entry.id)
        notice.value = `已从公开牌库删除《${entry.deck.name}》`
      }
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '删除公开牌库失败'
    }
  })
}
async function copyCode(deck: SavedL12Deck) { await navigator.clipboard.writeText(encodeDeckCode(deck)); notice.value = '牌库码已复制' }
async function previewImage(deck: SavedL12Deck) {
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  const blob = await createDeckImageBlob(deck, catalog.value)
  imagePreview.value = { deck, blob, url: URL.createObjectURL(blob) }
}
function closeImagePreview() {
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  imagePreview.value = null
}
async function copyPreviewImage() {
  if (!imagePreview.value) return
  try {
    await navigator.clipboard.write([new ClipboardItem({ 'image/png': imagePreview.value.blob })])
    notice.value = '牌库图已复制到剪贴板'
  } catch { notice.value = '当前浏览器不支持复制图片，请使用下载' }
}
async function importFromCode() {
  try {
    const deck = decodeDeckCode(importCode.value)
    deck.name = uniqueName(deck.name)
    const error = validateDeck(deck, catalog.value)
    if (error) throw new Error(error)
    const confirmed = await saveDeck(deck)
    saved.value = loadSavedDecks(); importCode.value = ''; notice.value = `已导入《${confirmed.name}》`
  } catch (error) { notice.value = error instanceof Error ? error.message : '牌库码导入失败' }
}
function resetPlazaFilters() {
  factionFilter.value = 'all'; masterFilter.value = 'all'; legalFilter.value = 'all'; cardFilter.value = ''; updatedFilter.value = 'all'; sortMode.value = 'trend'
}
function choosePlazaCard(card: SingleCardPickerItem) {
  cardFilter.value = card.cardId
  cardPickerOpen.value = false
}
function resetMineFilters() {
  mineQuery.value = ''; mineHomeCityFilter.value = 'all'; mineLegalFilter.value = 'all'; mineSort.value = 'latest'
}
function addPublishMatchup() {
  const selected = new Set(publishMatchups.value.map(row => row.opponentMasterId))
  const opponentMasterId = publishHomeCities.value.find(card => !selected.has(card.id))?.id ?? ''
  publishMatchups.value.push({ opponentMasterId, notes: '', keyCards: '', suggestedSwaps: '' })
}
function routeValue(key: string, fallback = '') {
  const value = route.query[key]
  return typeof value === 'string' ? value : fallback
}
function restoreFiltersFromRoute() {
  const requestedTab = routeValue('tab', 'mine')
  tab.value = requestedTab === 'plaza' ? 'plaza' : 'mine'
  query.value = routeValue('q')
  masterFilter.value = routeValue('master', 'all')
  factionFilter.value = routeValue('faction', 'all')
  legalFilter.value = ['legal', 'illegal'].includes(routeValue('legal')) ? routeValue('legal') as 'legal' | 'illegal' : 'all'
  cardFilter.value = routeValue('card')
  updatedFilter.value = ['1', '7', '30', '90', '365'].includes(routeValue('updated')) ? routeValue('updated') as typeof updatedFilter.value : 'all'
  sortMode.value = ['copies', 'likes', 'views', 'latest', 'name'].includes(routeValue('sort')) ? routeValue('sort') as typeof sortMode.value : 'trend'
}
function setQueryValue(next: Record<string, string>, key: string, value: string, fallback = '') {
  if (value && value !== fallback) next[key] = value
  else delete next[key]
}
watch([tab, query, masterFilter, factionFilter, legalFilter, cardFilter, updatedFilter, sortMode], () => {
  const next: Record<string, string> = {}
  setQueryValue(next, 'tab', tab.value, 'mine')
  setQueryValue(next, 'q', query.value.trim())
  setQueryValue(next, 'master', masterFilter.value, 'all')
  setQueryValue(next, 'faction', factionFilter.value, 'all')
  setQueryValue(next, 'legal', legalFilter.value, 'all')
  setQueryValue(next, 'card', cardFilter.value)
  setQueryValue(next, 'updated', updatedFilter.value, 'all')
  setQueryValue(next, 'sort', sortMode.value, 'trend')
  const current = new URLSearchParams(Object.entries(route.query).flatMap(([key, value]) => typeof value === 'string' ? [[key, value]] : [])).toString()
  const target = new URLSearchParams(next).toString()
  if (current !== target) void router.replace({ path: '/decks', query: next })
})
watch(() => route.query, restoreFiltersFromRoute, { deep: true })
</script>

<template>
  <div class="deck-page" :aria-busy="actionBusy">
    <header class="page-head"><div><small>牌库管理</small><h1>牌库</h1><p>构筑、保存、分享并发现公开牌库。</p></div><router-link :to="editorLink()">＋ 新建牌库</router-link></header>
    <div class="deck-tabs"><button :class="{ active: tab === 'mine' }" @click="tab = 'mine'">我的牌库</button><button :class="{ active: tab === 'plaza' }" @click="tab = 'plaza'">公开牌库</button></div>
    <p v-if="notice" class="deck-notice">{{ notice }}</p>

    <template v-if="tab === 'mine'">
      <section class="import-panel"><input v-model="importCode" placeholder="粘贴 L12D1 开头的牌库码"/><button :disabled="!importCode.trim()" @click="importFromCode">导入牌库码</button><button :disabled="!mine.length" @click="showPublish = true">公开牌库</button></section>
      <section v-if="mine.length" class="mine-toolbar"><input v-model="mineQuery" type="search" placeholder="按牌库名称搜索"/><select v-model="mineHomeCityFilter" aria-label="按主城筛选"><option value="all">全部主城</option><option v-for="city in homeCities" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select><select v-model="mineLegalFilter" aria-label="按合法性筛选"><option value="all">全部合法性</option><option value="legal">构筑合法</option><option value="illegal">构筑不合法</option></select><select v-model="mineSort" aria-label="我的牌库排序"><option value="latest">最近更新</option><option value="name">按名称</option></select><span>{{ filteredMine.length }} 个结果</span><button v-if="mineFilterActive" @click="resetMineFilters">清除筛选</button></section>
      <section v-if="filteredMine.length" class="mine-grid"><article v-for="deck in filteredMine" :key="deck.name"><DeckProfile :master-id="deck.masterId" :master-name="byId.get(deck.masterId)?.nameZh" :fallback-url="byId.get(deck.masterId)?.imageUrl" :name="deck.name" :meta="`${deckCountSummary(deck.cardIds, byId).label} 张主牌 · ${deck.moraleIds.length} 张士气`"/><small v-if="publishedCopyFor(deck)" class="mine-public-state">已公开 · 删除本地牌库不会删除公开版本</small><div class="deck-card-actions"><router-link :to="editorLink(deck.name, publishedCopyFor(deck)?.id)">编辑</router-link><details><summary>更多操作</summary><div><button @click="copyCode(deck)">复制牌库码</button><button @click="previewImage(deck)">生成牌库图</button><button class="danger" :disabled="deletingMine === deck.name" @click="deleteMine(deck)">{{ deletingMine === deck.name ? '删除中…' : '删除' }}</button></div></details></div></article></section>
      <div v-else-if="mine.length" class="empty-state"><b>没有符合筛选条件的牌库</b><p>调整名称、主城或合法性筛选后再试。</p><button @click="resetMineFilters">清除筛选</button></div>
      <div v-else class="empty-state"><b>还没有自定义牌库</b><p>从编辑器新建牌库，或粘贴其他玩家分享的牌库码。</p><router-link :to="editorLink()">打开牌库编辑器</router-link></div>
    </template>

    <template v-else>
      <section class="plaza-toolbar"><input v-model="query" placeholder="搜索牌库名称、作者或主城"/><MobileFilterSheet v-model="plazaFiltersOpen" title="牌库筛选与排序" :active-count="plazaFilterCount" @reset="resetPlazaFilters"><div class="plaza-filter-fields"><label>主城<select v-model="masterFilter"><option value="all">全部主城</option><option v-for="city in plazaMasters" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select></label><label>阵营<select v-model="factionFilter"><option value="all">全部阵营</option><option v-for="faction in plazaFactions" :key="faction" :value="faction">{{ factionLabels[faction] || faction }}</option></select></label><label>赛季合法性<select v-model="legalFilter"><option value="all">全部</option><option value="legal">符合本赛季</option><option value="illegal">不符合本赛季</option></select></label><label>包含卡牌<button type="button" class="card-picker-button" @click="cardPickerOpen = true">{{ cardFilter ? `${byId.get(cardFilter)?.nameZh || cardFilter} · ${byId.get(cardFilter)?.number || ''}` : '选择单卡' }}</button></label><label>更新时间<select v-model="updatedFilter"><option value="all">不限时间</option><option value="1">1天内</option><option value="7">7天内</option><option value="30">30天内</option><option value="90">90天内</option><option value="365">365天内</option></select></label><label>排序<select v-model="sortMode"><option value="trend">综合热度</option><option value="copies">最多复制</option><option value="likes">最多点赞</option><option value="views">最多浏览</option><option value="latest">最新发布</option><option value="name">按名称</option></select></label></div><template #apply-label>查看 {{ filteredPublished.length }} 个牌库</template></MobileFilterSheet><div class="plaza-desktop-filters"><select v-model="masterFilter" aria-label="按主城筛选"><option value="all">全部主城</option><option v-for="city in plazaMasters" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select><select v-model="factionFilter" aria-label="按阵营筛选"><option value="all">全部阵营</option><option v-for="faction in plazaFactions" :key="faction" :value="faction">{{ factionLabels[faction] || faction }}</option></select><select v-model="legalFilter" aria-label="按合法性筛选"><option value="all">全部合法性</option><option value="legal">符合本赛季</option><option value="illegal">不符合本赛季</option></select><button type="button" class="card-picker-button" @click="cardPickerOpen = true">{{ cardFilter ? `含：${byId.get(cardFilter)?.nameZh || cardFilter}` : '选择包含卡牌' }}</button><button v-if="cardFilter" type="button" class="card-filter-clear" @click="cardFilter = ''">清除单卡</button><select v-model="updatedFilter" aria-label="按更新时间筛选"><option value="all">不限时间</option><option value="1">1天内</option><option value="7">7天内</option><option value="30">30天内</option><option value="90">90天内</option><option value="365">365天内</option></select><select v-model="sortMode" aria-label="排序"><option value="trend">综合热度</option><option value="copies">最多复制</option><option value="likes">最多点赞</option><option value="views">最多浏览</option><option value="latest">最新发布</option><option value="name">按名称</option></select></div><button :disabled="!mine.length" @click="showPublish = true">发布我的牌库</button></section>
      <button v-if="plazaFilterCount" type="button" class="plaza-filter-summary" @click="plazaFiltersOpen = true">{{ plazaFilterSummary }}</button>
      <div class="plaza-result-line"><b>{{ filteredPublished.length }}</b> 个牌库<span v-if="plazaFilterCount"> · 已启用 {{ plazaFilterCount }} 项筛选</span><button v-if="plazaFilterCount" @click="resetPlazaFilters">清除筛选</button></div>
      <section class="plaza-grid"><article v-for="entry in filteredPublished" :key="entry.id" :class="`faction-${deckFaction(entry)}`"><button class="plaza-summary" @click="openDeck(entry)"><DeckProfile :master-id="entry.deck.masterId" :master-name="byId.get(entry.deck.masterId)?.nameZh" :fallback-url="byId.get(entry.deck.masterId)?.imageUrl" :name="entry.deck.name" :context="entry.author" :meta="`${deckCountSummary(entry.deck.cardIds, byId).label} 主牌 · ${entry.deck.moraleIds.length} 士气`"/></button><footer><span>浏览量 {{ entry.views ?? 0 }}</span><span>点赞 {{ entry.likes }}</span><span>复制 {{ entry.copies }}</span><span class="season-compliance" :class="{ compliant: seasonRequirement(entry).compliant }" :title="seasonRequirement(entry).reason">{{ seasonRequirement(entry).label }}</span><button @click="openDeck(entry)">查看构筑</button><details class="deck-actions-menu"><summary>更多操作</summary><div><button :class="{ liked: entry.liked }" :disabled="entry.official || actionPending(publicDeckActionKey(entry.id))" @click="toggleLike(entry)">♡ {{ entry.liked ? '取消点赞' : '点赞' }}</button></div></details></footer></article></section>
      <div v-if="!filteredPublished.length" class="empty-state"><b>{{ published.length ? '没有符合筛选条件的公开牌库' : '还没有公开牌库' }}</b><p v-if="published.length">调整筛选条件后再试。</p><button v-if="plazaFilterCount" @click="resetPlazaFilters">清除筛选</button></div>
    </template>
    <div v-if="showPublish" class="modal-mask" @click.self="showPublish = false"><section class="publish-modal"><header><h2>公开牌库</h2><button @click="showPublish = false">×</button></header><div class="publish-form"><p>选择一个已保存且合法的牌库；可在首次发布时同步填写公开内容。</p><select v-model="publishName"><option value="">选择牌库</option><option v-for="deck in mine" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select><h3>牌库指南</h3><label>构筑思路<textarea v-model="publishGuide.buildIdea" rows="3" maxlength="1200"/></label><label>起手建议<textarea v-model="publishGuide.opening" rows="2" maxlength="1200"/></label><label>关键牌与配合<textarea v-model="publishGuide.keyCards" rows="2" maxlength="1200"/></label><label>常见展开<textarea v-model="publishGuide.commonSequence" rows="2" maxlength="1200"/></label><label>替换建议<textarea v-model="publishGuide.substitutions" rows="2" maxlength="1200"/></label><section class="publish-matchups"><header><h3>对局建议</h3><button type="button" @click="addPublishMatchup">添加主城</button></header><article v-for="(row,index) in publishMatchups" :key="`${row.opponentMasterId}-${index}`"><label>对方主城<select v-model="row.opponentMasterId"><option value="" disabled>请选择</option><option v-for="city in publishHomeCities" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select></label><label>对局思路<textarea v-model="row.notes" rows="2" maxlength="800"/></label><label>关键牌<textarea v-model="row.keyCards" rows="2" maxlength="800"/></label><label>建议换牌<textarea v-model="row.suggestedSwaps" rows="2" maxlength="800"/></label><button type="button" class="danger" @click="publishMatchups.splice(index,1)">移除</button></article></section></div><button class="primary" :disabled="!publishName || !platformState.account || actionPending(`public-deck:publish:${publishName}`)" @click="publishDeck">{{ actionPending(`public-deck:publish:${publishName}`) ? '公开中…' : '确认公开' }}</button></section></div>
    <div v-if="imagePreview" class="modal-mask image-mask" @click.self="closeImagePreview"><section class="image-preview"><header><div><small>16:9 牌库分享图</small><h2>{{ imagePreview.deck.name }} · 牌库图</h2></div><button @click="closeImagePreview">×</button></header><img :src="imagePreview.url" alt="牌库图预览"/><footer><button @click="copyPreviewImage">复制图片</button><button class="primary" @click="downloadDeckImage(imagePreview.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
    <SingleCardPicker v-if="cardPickerOpen" title="选择公开牌库必须包含的卡牌" :items="plazaCardPickerItems" @select="choosePlazaCard" @close="cardPickerOpen = false"/>
  </div>
</template>

<style scoped>
.deck-page{min-height:100%;padding:28px clamp(18px,3vw,48px) 58px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-end;justify-content:space-between;margin-bottom:18px}.page-head small{color:#51c3cb;font:900 14px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0 3px;font-size:30px}.page-head p{margin:0;color:#77858c;font-size:14px}.page-head>a{padding:12px 18px;border:1px solid #e2c474;background:#e2c474;color:#0a0e10;font-weight:900;text-decoration:none}.deck-tabs{display:grid;grid-template-columns:1fr 1fr;margin-bottom:14px;border:1px solid #35434c;background:#0b1117}.deck-tabs button{padding:13px;border:0;background:transparent;color:#76848c;font-weight:900}.deck-tabs button.active{background:linear-gradient(135deg,#7c1724,#ad2d39);color:#fff}.import-panel,.plaza-toolbar{display:grid;grid-template-columns:1fr auto auto;gap:8px;margin-bottom:14px;padding:12px;border:1px solid #35434c;background:#101820}.import-panel input,.plaza-toolbar input,.publish-modal select{padding:11px;border:1px solid #46545d;background:#070d12;color:#fff}.import-panel button,.plaza-toolbar button{padding:0 15px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.mine-grid,.plaza-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.mine-grid>article,.plaza-grid>article{overflow:hidden;border:1px solid rgba(235,230,216,.16);background:#101821}.mine-grid>article{padding:14px}.deck-banner{position:relative;height:112px;overflow:hidden;background:linear-gradient(120deg,#1d5860,#5c1723)}.deck-banner .l12-card-image{width:100%;height:100%;filter:saturate(.8) brightness(.72)}.deck-banner::after{content:'';position:absolute;inset:0;background:linear-gradient(90deg,rgba(5,8,10,.1),rgba(5,8,10,.85))}.deck-banner span{position:absolute;z-index:2;right:13px;bottom:12px;font-size:17px;font-weight:900}.mine-grid h2{margin:13px 0 4px;font-size:17px}.mine-grid p{margin:0;color:#78868c;font-size:14px}.mine-grid>article>div:last-child{display:flex;flex-wrap:wrap;gap:6px;margin-top:14px}.mine-grid a,.mine-grid button{padding:7px 9px;border:1px solid #4b5961;background:#0b1218;color:#e8e5dd;font-size:14px;font-weight:900;text-decoration:none}.plaza-summary{display:block;width:100%;padding:12px;border:0;background:transparent;color:#fff;text-align:left}.banner-strip{display:flex;height:82px;overflow:hidden;background:#080e12}.banner-strip .l12-card-image{width:20%;height:100%}.plaza-summary h2{margin:11px 0 4px;font-size:16px}.plaza-summary p,.plaza-summary span{display:block;margin:0;color:#77858b;font-size:14px}.plaza-grid footer{display:flex;align-items:center;justify-content:space-between;padding:9px 12px;border-top:1px solid rgba(235,230,216,.1)}.plaza-grid footer button{border:0;background:transparent;color:#93a0a5;font-size:14px;font-weight:900}.plaza-grid footer button.liked{color:#df6672}.plaza-grid footer span{color:#65727a;font-size:14px}.empty-state{display:grid;min-height:360px;place-items:center;align-content:center;border:1px dashed #34414a;color:#738089;text-align:center}.empty-state b{color:#aab2b4}.empty-state p{font-size:14px}.empty-state a{padding:10px 14px;border:1px solid #57c2c9;color:#78dbe1;font-size:14px;text-decoration:none}.deck-notice{position:fixed;z-index:80;right:18px;bottom:18px;max-width:420px;padding:11px 14px;border:1px solid #d5b45f;background:#251b08;color:#f4d980;font-size:14px;font-weight:900}.modal-mask{position:fixed;z-index:90;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,6,.78);backdrop-filter:blur(8px)}.deck-detail{display:flex;width:min(1040px,95vw);max-height:90vh;flex-direction:column;overflow:hidden;border:1px solid #52606a;background:#111923}.deck-detail>header,.publish-modal header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px;border-bottom:1px solid #354149}.deck-detail header small{color:#52c4cb;font-size:14px}.deck-detail h2{margin:5px 0;font-size:24px}.deck-detail header p{margin:0;color:#7c898f;font-size:14px}.deck-detail header button,.publish-modal header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.detail-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:10px;padding:20px;overflow:auto}.detail-grid article{position:relative;min-width:0}.detail-grid .l12-card-image{display:block;width:100%;aspect-ratio:5/7;background:#070b0f}.detail-grid article>b{position:absolute;right:4px;top:4px;display:grid;width:26px;height:26px;place-items:center;border-radius:50%;background:#e0bf6d;color:#080b0d}.detail-grid span{display:block;margin-top:4px;overflow:hidden;font-size:14px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}.deck-detail>footer{display:flex;justify-content:flex-end;gap:8px;padding:16px 20px;border-top:1px solid #354149}.deck-detail>footer button,.publish-modal>.primary{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.primary{border-color:#e0bf6d!important;background:#e0bf6d!important;color:#090c0e!important}.publish-modal{width:min(500px,94vw);padding-bottom:20px;border:1px solid #52606a;background:#111923}.publish-modal h2{margin:0}.publish-modal p{padding:0 20px;color:#849097;font-size:14px;line-height:1.7}.publish-modal select{width:calc(100% - 40px);margin:0 20px 12px}.publish-modal>.primary{display:block;margin:0 20px 0 auto}
@media(max-width:1050px){.mine-grid,.plaza-grid{grid-template-columns:1fr 1fr}}
@media(max-width:700px){.deck-page{padding:18px 12px 48px}.page-head{align-items:flex-start;flex-direction:column;gap:12px}.import-panel,.plaza-toolbar{grid-template-columns:1fr}.import-panel button,.plaza-toolbar button{padding:11px}.mine-grid,.plaza-grid{grid-template-columns:1fr}.detail-grid{grid-template-columns:repeat(3,1fr)}.deck-detail>footer{flex-wrap:wrap}.deck-detail>footer button{flex:1 1 40%}}
.plaza-toolbar{grid-template-columns:minmax(220px,1fr) 135px 145px auto auto}.plaza-toolbar select{padding:11px;border:1px solid #46545d;background:#070d12;color:#fff}.deck-notice{z-index:100}.deck-detail{width:min(1180px,95vw)}.deck-detail>header,.image-preview header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px;border-bottom:1px solid #354149}.image-preview header small{color:#52c4cb;font-size:14px}.image-preview h2{margin:5px 0;font-size:24px}.image-preview header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.deck-analysis{display:grid;grid-template-columns:230px 1fr;min-height:0;overflow:hidden}.deck-analysis>aside{display:grid;align-content:start;gap:12px;padding:20px;border-right:1px solid #354149;overflow:auto}.deck-analysis>aside>section{padding:12px;border:1px solid #334049;background:#0b1218}.deck-analysis>aside>section>b{font-size:14px}.detail-master{display:flex;gap:10px;align-items:center}.detail-master .l12-card-image{width:72px;aspect-ratio:5/7}.detail-master div{display:grid;gap:4px}.detail-master small,.detail-master span{color:#78868c;font-size:14px}.detail-curve{display:flex;height:92px;align-items:end;gap:3px;margin-top:8px}.detail-curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.detail-curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.detail-curve small,.detail-curve em{font-size:14px;font-style:normal}.detail-curve em{color:#89959a}.deck-analysis>aside>section>p{display:flex;justify-content:space-between;margin:7px 0;color:#89959a;font-size:14px}.deck-analysis>aside>section>p strong{color:#e8e4da}.detail-grid article>b{width:auto;min-width:28px;padding:0 4px}.detail-grid article>small{display:block;margin-top:3px;overflow:hidden;color:#77858c;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.image-mask{z-index:95}.image-preview{width:min(1200px,96vw);max-height:94vh;border:1px solid #52606a;background:#111923}.image-preview>img{display:block;width:100%;max-height:74vh;object-fit:contain;background:#05080a}.image-preview footer{display:flex;justify-content:flex-end;gap:8px;padding:14px 20px;border-top:1px solid #354149}.image-preview footer button{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}
.deck-analysis>.construction-browser{min-width:0;padding:20px;overflow:hidden}
.deck-detail>footer .danger{border-color:#9e3944;background:#4d171d;color:#ffdce0}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:1fr}.deck-analysis{grid-template-columns:1fr;overflow:auto}.deck-analysis>aside{border-right:0;border-bottom:1px solid #354149}.deck-analysis>.construction-browser{padding:14px}}
.mine-grid>article>:deep(.deck-profile){border:0;background:transparent}.plaza-summary>:deep(.deck-profile){border:0;background:transparent}.deck-analysis>aside>:deep(.deck-profile){width:100%}
.plaza-grid footer{--deck-faction:72,84,91;display:grid;grid-template-columns:auto auto auto minmax(112px,1fr) auto;align-items:center;gap:8px;padding:10px 12px;border-top-color:rgba(var(--deck-faction),.48);background:linear-gradient(90deg,rgba(var(--deck-faction),.2),rgba(var(--deck-faction),.08))}.plaza-grid .faction-tianting footer{--deck-faction:34,105,113}.plaza-grid .faction-taiyangcheng footer{--deck-faction:126,91,28}.plaza-grid .faction-asgard footer{--deck-faction:44,79,122}.plaza-grid .faction-gaotianyuan footer{--deck-faction:128,43,54}.plaza-grid .faction-olympus footer{--deck-faction:86,56,126}.plaza-grid .faction-otherworld footer,.plaza-grid .faction-bijie footer{--deck-faction:35,112,83}.plaza-grid footer button{color:#d2d9d8;font-size:14px}.plaza-grid footer button.liked{color:#ff8995}.plaza-grid footer span{color:#c7cecd;font-size:14px;white-space:nowrap}.plaza-grid footer .season-compliance{justify-self:end;color:#f0a9ad;font-weight:900}.plaza-grid footer .season-compliance.compliant{color:#a4e4c8}
@media(max-width:700px){.plaza-grid footer{grid-template-columns:repeat(3,auto);justify-content:space-between}.plaza-grid footer .season-compliance{grid-column:1/3;justify-self:start}}
.plaza-filter-fields{display:grid;gap:14px}.plaza-filter-fields label{display:grid;gap:6px;color:#b9c2c4;font-size:13px;font-weight:900}.plaza-filter-fields select{width:100%;padding:10px;border:1px solid #46545d;background:#070d12;color:#fff}.plaza-desktop-filters{display:contents}.season-only{display:flex!important;align-items:center;gap:7px!important;color:#c9d0d0;font-size:13px;font-weight:900;white-space:nowrap}.season-only input{width:17px;height:17px;min-height:0;padding:0;accent-color:#d1ad50}.plaza-filter-summary{display:none}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:minmax(0,1fr) auto!important;align-items:stretch}.plaza-toolbar>input{min-width:0}.plaza-desktop-filters{display:none}.plaza-toolbar>button:last-child{grid-column:1/-1;min-height:44px}.plaza-filter-summary{display:block;width:100%;margin:-6px 0 12px;padding:8px 10px;border:1px solid #52636a;background:#101a20;color:#c7d8d6;font-size:12px;font-weight:800;text-align:left}.deck-page{overflow-x:clip}.plaza-grid footer{row-gap:7px}.deck-detail,.image-preview{width:100%;max-height:100dvh;border:0}.deck-detail>header,.image-preview header{padding:14px}.deck-detail>footer,.image-preview footer{padding:12px;gap:7px}.deck-detail h2,.image-preview h2{font-size:20px}}
@media(max-width:520px){.deck-page{padding:14px 12px 42px}.page-head{gap:8px;margin-bottom:12px}.page-head small{font-size:11px}.page-head h1{font-size:25px}.page-head p{font-size:12px}.page-head>a{padding:9px 12px;font-size:13px}.deck-tabs{margin-bottom:10px}.deck-tabs button{min-height:44px;padding:9px;font-size:13px}.deck-notice{position:static;max-width:none;margin:0 0 10px;padding:9px 10px;font-size:12px;box-shadow:none}.import-panel,.plaza-toolbar{gap:7px;margin-bottom:10px;padding:9px}.import-panel input,.plaza-toolbar input{padding:10px;font-size:12px}.import-panel button,.plaza-toolbar button{min-height:44px;padding:9px 11px;font-size:13px}.mine-grid,.plaza-grid{gap:9px}.mine-grid>article{padding:11px}.deck-banner{height:96px}.mine-grid h2,.plaza-summary h2{font-size:15px}.mine-grid p,.plaza-summary p,.plaza-summary span,.plaza-grid footer button,.plaza-grid footer span{font-size:12px}.empty-state{min-height:280px}.empty-state p,.empty-state a{font-size:12px}.plaza-filter-summary{margin:-3px 0 9px}}
.plaza-toolbar{grid-template-columns:minmax(220px,1fr) repeat(6,minmax(108px,auto)) auto}.plaza-result-line{display:flex;align-items:center;gap:5px;margin:-4px 0 12px;color:#7d8a8f;font-size:13px}.plaza-result-line b{color:#e8e4da}.plaza-result-line button{margin-left:auto;border:0;background:transparent;color:#75cdd2;font-weight:900}.plaza-filter-fields select{color-scheme:dark}
.mine-public-state{display:block;margin-top:8px;color:#79cfc9;font-size:12px}.mine-grid button.danger{border-color:#8f3d47;background:#2e1519;color:#f2a4ac}
@media(max-width:1180px) and (min-width:701px){.plaza-toolbar{grid-template-columns:minmax(220px,1fr) repeat(3,minmax(110px,1fr))}.plaza-toolbar>button:last-child{grid-column:4}.plaza-desktop-filters{display:contents}}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:minmax(0,1fr) auto!important}.plaza-result-line{font-size:12px}}
.mine-toolbar{display:grid;grid-template-columns:minmax(220px,1fr) repeat(3,minmax(120px,auto)) auto auto;align-items:center;gap:8px;margin-bottom:14px;padding:12px;border:1px solid #35434c;background:#101820}.mine-toolbar input,.mine-toolbar select{min-width:0;padding:10px;border:1px solid #46545d;background:#070d12;color:#fff}.mine-toolbar span{color:#a7b0b1;white-space:nowrap}.mine-toolbar button,.empty-state button,.card-picker-button,.card-filter-clear{min-height:40px;padding:8px 11px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.deck-card-actions{display:flex;flex-wrap:wrap;gap:6px;margin-top:14px}.deck-card-actions details,.deck-actions-menu{display:contents}.deck-card-actions summary,.deck-actions-menu summary{display:none}.deck-card-actions details>div,.deck-actions-menu>div{display:flex;gap:6px}.publish-modal{display:grid;width:min(860px,96vw);max-height:92dvh;grid-template-rows:auto minmax(0,1fr) auto;overflow:hidden}.publish-form{display:grid;gap:10px;overflow:auto;padding:0 20px 16px}.publish-form>p{padding:0}.publish-form h3{margin:8px 0 0}.publish-form label{display:grid;gap:5px;color:#c3cccb;font-weight:800}.publish-form textarea,.publish-form select{box-sizing:border-box;width:100%;padding:9px;border:1px solid #46545d;background:#070d12;color:#fff;font:inherit;resize:vertical}.publish-modal>.primary{margin:12px 20px 0 auto}.publish-matchups{display:grid;gap:10px}.publish-matchups>header{display:flex;align-items:center;justify-content:space-between}.publish-matchups>header button,.publish-matchups article>button{min-height:38px;padding:7px 10px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.publish-matchups article{display:grid;grid-template-columns:minmax(140px,.8fr) repeat(3,minmax(150px,1fr)) auto;align-items:end;gap:8px;padding:10px;border:1px solid #35434c;background:#0b1218}.publish-matchups .danger{border-color:#8f3d47;background:#2e1519}.plaza-filter-fields .card-picker-button{width:100%;overflow:hidden;text-align:left;text-overflow:ellipsis;white-space:nowrap}.plaza-desktop-filters .card-picker-button{min-width:130px}.card-filter-clear{padding-inline:9px}.empty-state button{margin-top:8px}
@media(max-width:900px){.mine-toolbar{grid-template-columns:repeat(2,minmax(0,1fr))}.mine-toolbar input{grid-column:1/-1}.mine-toolbar span{align-self:center}.publish-matchups article{grid-template-columns:1fr 1fr}.publish-matchups article>button{grid-column:1/-1}}
@media(max-width:700px){.mine-toolbar{grid-template-columns:1fr;padding:9px}.mine-toolbar input{grid-column:auto}.deck-card-actions{align-items:stretch}.deck-card-actions>a{flex:1}.deck-card-actions details,.deck-actions-menu{display:block;position:relative;flex:1}.deck-card-actions summary,.deck-actions-menu summary{display:grid;min-height:38px;padding:7px 9px;border:1px solid #4b5961;background:#0b1218;color:#e8e5dd;font-size:13px;font-weight:900;list-style:none;place-items:center}.deck-card-actions details>div,.deck-actions-menu>div{display:none;position:absolute;z-index:8;right:0;bottom:calc(100% + 5px);min-width:160px;padding:6px;border:1px solid #53616a;background:#101820;box-shadow:0 12px 30px #000}.deck-card-actions details[open]>div,.deck-actions-menu[open]>div{display:grid}.deck-card-actions details>div button,.deck-actions-menu>div button{width:100%;min-height:40px}.plaza-grid footer{grid-template-columns:repeat(2,minmax(0,1fr))}.plaza-grid footer>span{white-space:normal}.plaza-grid footer>button,.deck-actions-menu{min-height:40px}.publish-modal{width:100%;max-height:100dvh;border:0}.publish-matchups article{grid-template-columns:1fr}}
</style>

