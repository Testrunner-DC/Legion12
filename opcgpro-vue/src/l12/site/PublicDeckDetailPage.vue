<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { createDeckImageBlob, deckImageGroups, downloadDeckImage, encodeDeckCode } from './deckShare'
import { automaticExtraCardIdsForMaster, deckCountSummary, loadDeckCatalog, loadOfficialPresetDecks, loadSavedDecks, saveDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { platformState, publicDeckApi, type PublishedDeck, type PublicDeckDetails, type PublicDeckGuide, type PublicDeckVersionChange } from '@/l12/platform'
import DeckProfile from '@/l12/DeckProfile.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import CardDetailContent from '@/l12/CardDetailContent.vue'
import CardImage from '@/l12/CardImage.vue'
import DeckConstructionBrowser, { type ConstructionEntry } from './DeckConstructionBrowser.vue'
import { samplePublicDeckOpeningHand } from './publicDeckHands'
import { preservePublicDeckDetails, publicDeckRouteReference } from './publicDeckEntry'
import { useActionGate } from '@/l12/useActionGate'

const route = useRoute()
const router = useRouter()
const { pending: actionBusy, isPending: actionPending, run: runAction } = useActionGate()
const publicDeckActionKey = (deckId: string, accountId = platformState.account?.id ?? 'anonymous') =>
  `public-deck:${accountId}:${deckId}`
const catalog = ref<DeckCard[]>([])
const entry = ref<PublishedDeck | null>(null)
const notice = ref('')
const loading = ref(true)
const imagePreview = ref<{ blob: Blob; url: string } | null>(null)
const openingHandIds = ref<string[]>([])
const selectedCard = ref<DeckCard | null>(null)
const mobileDetailsOpen = ref(false)
function selectCard(card: DeckCard) {
  selectedCard.value = card
  mobileDetailsOpen.value = window.matchMedia('(max-width:1000px)').matches
}
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const master = computed(() => entry.value ? byId.value.get(entry.value.deck.masterId) : undefined)
const backTo = computed(() => typeof route.query.from === 'string' && route.query.from.startsWith('/decks') ? route.query.from : '/decks?tab=plaza')
const entries = computed<ConstructionEntry[]>(() => {
  if (!entry.value) return []
  const rows: ConstructionEntry[] = []
  const add = (ids: string[], section: string) => {
    const totals = ids.reduce((map, id) => map.set(id, (map.get(id) || 0) + 1), new Map<string, number>())
    totals.forEach((quantity, cardId) => rows.push({ cardId, quantity, section }))
  }
  add(entry.value.deck.cardIds, 'main')
  add(entry.value.deck.moraleIds, 'morale')
  add(entry.value.deck.specialIds ?? [], 'special')
  add(automaticExtraCardIdsForMaster(entry.value.deck.masterId), 'automatic')
  return rows
})
const curve = computed(() => {
  const values = Array(9).fill(0) as number[]
  entries.value.filter(row => row.section === 'main').forEach(row => values[Math.min(8, byId.value.get(row.cardId)?.cost ?? 0)] += row.quantity)
  return values
})
const curveMax = computed(() => Math.max(1, ...curve.value))
const details = computed(() => entry.value?.details ?? emptyDetails())
const hasGuide = computed(() => Object.values(details.value.guide).some(value => value.trim()))
const hasMatchups = computed(() => details.value.matchups.length > 0)
const matchStatisticsRange = computed(() => {
  const statistics = details.value.matchStatistics
  const from = new Date(statistics.from)
  const to = new Date(statistics.to)
  if (!Number.isFinite(from.valueOf()) || !Number.isFinite(to.valueOf()) || from.getUTCFullYear() < 2000)
    return `统计最近 ${statistics.recentDays} 天`
  const format = (value: Date) => value.toLocaleDateString('zh-CN', { timeZone: 'UTC' })
  return `统计最近 ${statistics.recentDays} 天（${format(from)} 至 ${format(to)}）`
})
const sectionTabs = computed(() => [
  { id: 'construction', label: '构筑', visible: true },
  { id: 'guide', label: '指南', visible: hasGuide.value },
  { id: 'matchups', label: '对局建议', visible: hasMatchups.value },
  { id: 'versions', label: '版本', visible: true },
  { id: 'matches', label: '对局', visible: true },
  { id: 'hands', label: '起手', visible: true },
].filter(item => item.visible))
const deckCopies = computed(() => entry.value ? deckImageGroups(entry.value.deck, catalog.value)
  .flatMap(group => Array.from({ length: group.count }, (_, index) => ({
    ...group,
    key: `${group.cardId}:${group.artId || 'original'}:${index}`,
    card: byId.value.get(group.cardId),
  }))).filter(copy => Boolean(copy.card)) : [])
const openingHand = computed(() => {
  const copies = new Map(deckCopies.value.map(copy => [copy.key, copy]))
  return openingHandIds.value.flatMap(key => {
    const copy = copies.get(key)
    return copy ? [copy] : []
  })
})

onMounted(async () => {
  try {
    catalog.value = await loadDeckCatalog()
    const id = String(route.params.deckId || '')
    if (id.startsWith('official-')) {
      const index = Number(id.slice('official-'.length))
      const preset = (await loadOfficialPresetDecks())[index]
      if (!preset) throw new Error('未找到这个官方牌库')
      entry.value = { id, ownerId: 'official', deck: { ...preset, specialIds: preset.specialIds ?? [], updatedAt: '' }, author: '十二军团官方预组', views: 0, likes: 0, copies: 0, liked: false, official: true, createdAt: '', updatedAt: '', details: emptyDetails() }
    } else {
      entry.value = await publicDeckApi.get(id)
      const canonicalReference = publicDeckRouteReference(entry.value)
      if (canonicalReference !== id)
        await router.replace({ name: 'public-deck-detail', params: { deckId: canonicalReference }, query: route.query, hash: route.hash })
      const viewedKey = `l12:public-deck-viewed:${entry.value.id}`
      if (!sessionStorage.getItem(viewedKey)) {
        sessionStorage.setItem(viewedKey, '1')
        void publicDeckApi.recordView(publicDeckRouteReference(entry.value!)).then(value => {
          entry.value = preservePublicDeckDetails(entry.value, value)
        }).catch(() => sessionStorage.removeItem(viewedKey))
      }
    }
    selectedCard.value = master.value ?? byId.value.get(entry.value.deck.cardIds[0] || '') ?? null
    redrawOpeningHand()
  } catch (error) { notice.value = error instanceof Error ? error.message : '公开牌库加载失败' }
  finally { loading.value = false }
})

function uniqueName(base: string) {
  const saved = loadSavedDecks()
  const truncated = base.slice(0, 24)
  if (!saved[truncated]) return truncated
  let index = 2
  for (;;) {
    const suffix = ` ${index++}`
    const name = `${base.slice(0, 24 - suffix.length)}${suffix}`
    if (!saved[name]) return name
  }
}
async function copyToMine() {
  if (!entry.value) return
  const id = entry.value.id
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      if (!entry.value || entry.value.id !== id) return
      const deck = { ...entry.value.deck, name: uniqueName(entry.value.deck.name), publicationId: null, publicationVersion: null, cardIds: [...entry.value.deck.cardIds], moraleIds: [...entry.value.deck.moraleIds], specialIds: [...(entry.value.deck.specialIds ?? [])], updatedAt: new Date().toISOString() }
      const saved = await saveDeck(deck)
      if (accountId === platformState.account?.id && entry.value?.id === id)
        notice.value = `已复制《${saved.name}》到我的牌库`
      if (!entry.value.official) {
        try {
          const updated = await publicDeckApi.recordCopy(publicDeckRouteReference(entry.value))
          if (accountId === platformState.account?.id && entry.value?.id === id)
            entry.value = preservePublicDeckDetails(entry.value, updated)
        } catch { /* 本地复制已经成功；远端统计失败不得反写为复制失败。 */ }
      }
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '复制到我的牌库失败'
    }
  })
}
async function toggleLike() {
  if (!entry.value || entry.value.official) return
  if (!platformState.account) { notice.value = '请先登录账号再点赞'; return }
  const id = entry.value.id
  const reference = publicDeckRouteReference(entry.value)
  const accountId = platformState.account.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      const updated = await publicDeckApi.toggleLike(reference)
      if (accountId === platformState.account?.id && entry.value?.id === id)
        entry.value = preservePublicDeckDetails(entry.value, updated)
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '点赞失败'
    }
  })
}
async function copyCode() {
  if (!entry.value) return
  await navigator.clipboard.writeText(encodeDeckCode(entry.value.deck)); notice.value = '牌库码已复制'
}
async function previewImage() {
  if (!entry.value) return
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  const blob = await createDeckImageBlob(entry.value.deck, catalog.value, { publicUrl: publicDeckUrl() })
  imagePreview.value = { blob, url: URL.createObjectURL(blob) }
}
async function editDeck() {
  if (!entry.value) return
  const saved = await saveDeck({ ...entry.value.deck, cardIds: [...entry.value.deck.cardIds], moraleIds: [...entry.value.deck.moraleIds], specialIds: [...(entry.value.deck.specialIds ?? [])] })
  await router.push({ path: '/deck-editor', query: { deck: saved.name, published: entry.value.id, returnTo: route.fullPath } })
}
async function deleteDeck() {
  if (!entry.value || !window.confirm('确定删除这个公开牌库？')) return
  const id = entry.value.id
  const reference = publicDeckRouteReference(entry.value)
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      await publicDeckApi.delete(reference)
      if (accountId === platformState.account?.id) await router.replace(backTo.value)
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '删除公开牌库失败'
    }
  })
}
function emptyGuide(): PublicDeckGuide {
  return { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
}
function emptyDetails(): PublicDeckDetails {
  return { guide: emptyGuide(), matchups: [], contentRevision: 0, versions: [], matchStatistics: { from: '', to: '', recentDays: 90, games: 0, sampleStatus: 'empty', groups: [] }, matchBindingStatus: 'empty', matchBindingMessage: '这个牌库暂无可核验的版本对局统计。' }
}
function scrollToSection(section: string) {
  document.getElementById(`public-deck-${section}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
function publicDeckUrl() {
  if (!entry.value || entry.value.official || typeof window === 'undefined') return ''
  return new URL(router.resolve({ name: 'public-deck-detail', params: { deckId: publicDeckRouteReference(entry.value) } }).href, window.location.origin).href
}
function redrawOpeningHand() {
  if (!entry.value) return
  openingHandIds.value = samplePublicDeckOpeningHand(deckCopies.value.map(copy => copy.key))
}
function cardName(cardId: string) { return byId.value.get(cardId)?.nameZh || cardId }
function masterName(masterId: string) { return byId.value.get(masterId)?.nameZh || masterId }
function changeLabel(change: PublicDeckVersionChange) {
  const section = { master: '主宰', main: '主牌', morale: '士气', special: '试炼/额外' }[change.section]
  if (change.section === 'master') return `${section}改为 ${cardName(change.cardId)}`
  if (change.previousQuantity === 0) return `${section}新增 ${cardName(change.cardId)} ×${change.currentQuantity}`
  if (change.currentQuantity === 0) return `${section}移除 ${cardName(change.cardId)} ×${change.previousQuantity}`
  return `${section}调整 ${cardName(change.cardId)}：${change.previousQuantity} → ${change.currentQuantity}`
}
function formatTime(value?: string) {
  if (!value) return '—'
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}
function formatRate(value: number) { return `${(value * 100).toFixed(1)}%` }
</script>

<template>
  <main class="public-deck-detail" data-ui-contract="public-deck-detail-page" :aria-busy="actionBusy">
    <router-link class="back-link" :to="backTo">← 返回公开牌库</router-link>
    <p v-if="loading" class="state">正在载入构筑……</p>
    <p v-else-if="!entry" class="state error">{{ notice || '未找到这个公开牌库' }}</p>
    <template v-else>
      <header class="detail-head">
        <DeckProfile :master-id="entry.deck.masterId" :master-name="master?.nameZh" :fallback-url="master?.imageUrl" :name="entry.deck.name" :context="entry.author" :meta="`${deckCountSummary(entry.deck.cardIds, byId).label} 张主牌 · ${entry.deck.moraleIds.length} 张士气`"/>
        <div class="metrics"><span>浏览 {{ entry.views ?? 0 }}</span><span>点赞 {{ entry.likes }}</span><span>复制 {{ entry.copies }}</span><span>{{ entry.seasonCompliant === false ? '不符合本赛季' : '符合本赛季' }}</span></div>
      </header>
      <div class="detail-toolbar">
        <nav class="detail-tabs" aria-label="公开牌库详情内容">
          <button v-for="tab in sectionTabs" :key="tab.id" @click="scrollToSection(tab.id)">{{ tab.label }}</button>
        </nav>
        <div class="actions"><button v-if="!entry.official" :disabled="!platformState.account || actionPending(publicDeckActionKey(entry.id))" @click="toggleLike">♡ {{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : entry.liked ? '取消点赞' : '点赞' }}</button><button @click="copyCode">复制牌库码</button><button @click="previewImage">生成牌库图</button><button v-if="entry.ownerId === platformState.account?.id" @click="editDeck">编辑牌库</button><button v-if="entry.ownerId === platformState.account?.id" class="danger" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="deleteDeck">{{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : '删除公开牌库' }}</button><button class="primary" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="copyToMine">{{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : '复制到我的牌库' }}</button></div>
      </div>
      <section id="public-deck-construction" class="deck-layout detail-anchor-section">
        <aside>
          <section><b>费用曲线</b><div class="curve"><i v-for="(value,index) in curve" :key="index"><span :style="{ height: `${Math.max(4, value / curveMax * 72)}px` }"></span><small>{{ index === 8 ? '8+' : index }}</small><em>{{ value }}</em></i></div></section>
          <section><b>构筑摘要</b><p v-if="entry.deck.cardIds.length">主牌<strong>{{ entry.deck.cardIds.length }}</strong></p><p v-if="entry.deck.moraleIds.length">士气<strong>{{ entry.deck.moraleIds.length }}</strong></p><p v-if="entry.deck.specialIds?.length">额外<strong>{{ entry.deck.specialIds.length }}</strong></p><p v-if="automaticExtraCardIdsForMaster(entry.deck.masterId).length">自动额外<strong>{{ automaticExtraCardIdsForMaster(entry.deck.masterId).length }}</strong></p></section>
          <section id="public-deck-construction-filters" aria-label="构筑筛选"></section>
        </aside>
        <div class="public-deck-main"><DeckConstructionBrowser :entries="entries" :catalog="catalog" :master-faction="master?.faction" :title="`${entry.deck.name} · 全部构筑`" filter-target="#public-deck-construction-filters" hide-header external-details @select="selectCard"/>

      <section v-if="hasGuide" id="public-deck-guide" class="content-panel detail-anchor-section" data-detail-section="guide">
        <header><div><h2>牌库指南</h2><p v-if="details.contentUpdatedAt">作者更新于 {{ formatTime(details.contentUpdatedAt) }} · 修订 {{ details.contentRevision }}</p></div></header>
        <div class="reading-sections">
          <article v-for="item in [['构筑思路',details.guide.buildIdea],['起手建议',details.guide.opening],['关键牌与配合',details.guide.keyCards],['常见展开',details.guide.commonSequence],['替换建议',details.guide.substitutions]].filter(item => item[1])" :key="item[0]"><h3>{{ item[0] }}</h3><p>{{ item[1] }}</p></article>
        </div>
      </section>
      <section v-if="hasMatchups" id="public-deck-matchups" class="content-panel detail-anchor-section" data-detail-section="matchups">
        <header><div><h2>对局建议</h2><p>按对方主宰查看作者提供的思路、关键牌与换牌建议。</p></div></header>
        <div class="matchup-list"><article v-for="row in details.matchups" :key="row.opponentMasterId"><header class="matchup-city"><DeckProfile compact :master-id="row.opponentMasterId" :master-name="masterName(row.opponentMasterId)" :name="`对阵 ${masterName(row.opponentMasterId)}`"/></header><p v-if="row.notes"><b>思路</b>{{ row.notes }}</p><p v-if="row.keyCards"><b>关键牌</b>{{ row.keyCards }}</p><p v-if="row.suggestedSwaps"><b>换牌</b>{{ row.suggestedSwaps }}</p></article></div>
      </section>
      <section id="public-deck-versions" class="content-panel detail-anchor-section" data-detail-section="versions">
        <header><div><h2>全部公开版本</h2></div></header>
        <div v-if="details.versions.length" class="version-list"><details v-for="version in details.versions" :key="version.version" :open="version.version === details.versions[0]?.version"><summary><b>版本 {{ version.version }}</b><span>{{ version.name }}</span><time>{{ formatTime(version.createdAt) }}</time></summary><p v-if="version.version === 1">首次发布</p><ul v-else-if="version.changes.length"><li v-for="change in version.changes" :key="`${change.section}-${change.cardId}`">{{ changeLabel(change) }}</li></ul><p v-else>构筑正文未变化。</p></details></div>
        <p v-else class="empty-copy">尚无可读取的公开版本。</p>
      </section>
      <section id="public-deck-matches" class="content-panel detail-anchor-section" data-detail-section="matches">
        <header><div><h2>版本对局</h2><p>{{ matchStatisticsRange }}</p></div></header>
        <div v-if="details.matchStatistics.groups.length" class="match-stat-list"><article v-for="stat in details.matchStatistics.groups" :key="`${stat.version}-${stat.masterId}-${stat.opponentMasterId}`"><header><b>版本 {{ stat.version }}</b><span>{{ masterName(stat.masterId) }} 对阵 {{ masterName(stat.opponentMasterId) }}</span></header><dl><div><dt>场次</dt><dd>{{ stat.games }}</dd></div><div><dt>胜率</dt><dd>{{ formatRate(stat.winRate) }}</dd></div><div><dt>胜 / 负 / 平</dt><dd>{{ stat.wins }} / {{ stat.losses }} / {{ stat.draws }}</dd></div></dl></article></div>
        <p v-else class="empty-copy">{{ details.matchBindingMessage }}</p>
      </section>
      <section id="public-deck-hands" class="content-panel detail-anchor-section" data-detail-section="hands">
        <header><div><h2>随机起手</h2><p>随机展示当前构筑中的 6 张主牌。</p></div><button @click="redrawOpeningHand">重新抽取</button></header>
        <div class="opening-hand"><article v-for="copy in openingHand" :key="copy.key"><button class="hand-card" :aria-label="`查看${copy.card!.nameZh}详情，${copy.label}`" @click="selectCard(copy.card!)"><CardImage :card-id="copy.cardImageId" :legacy-url="copy.legacyUrl" :alt="`${copy.card!.nameZh} · ${copy.label}`" intent="thumb" fit="contain"/><b>{{ copy.card!.nameZh }}</b><small>{{ copy.label }}</small></button></article></div>
      </section>
        </div>
        <aside class="archive-detail public-card-detail" aria-label="卡牌详情"><CardDetailContent v-if="selectedCard" :card="selectedCard" :show-catalog-only="false"/></aside>
      </section>
</template>
    <p v-if="notice && entry" class="notice">{{ notice }}</p>
    <div v-if="imagePreview && entry" class="preview-mask" @click.self="imagePreview = null"><section><button class="close" @click="imagePreview = null">×</button><img :src="imagePreview.url" alt="牌库图预览"/><footer><button class="primary" @click="downloadDeckImage(entry.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
  </main>
  <CatalogCardDetails v-if="mobileDetailsOpen && selectedCard" :card="selectedCard" :show-catalog-only="false" @close="mobileDetailsOpen = false"/>
</template>

<style scoped>
.deck-layout{grid-template-columns:180px minmax(0,1fr) var(--l12-card-detail-sidebar-width,274px)!important;align-items:start}.public-deck-main{min-width:0}.public-card-detail{position:sticky;top:14px;min-width:0;max-height:calc(100dvh - 28px);overflow:auto}.opening-hand .hand-card{width:100%;min-width:0;padding:0;white-space:normal;border:0;background:transparent}.matchup-city :deep(.deck-profile){width:100%;border:0;background:transparent}@media(max-width:1000px){.deck-layout{grid-template-columns:1fr!important}.public-card-detail{display:none!important}.deck-layout>aside:first-child{grid-template-columns:1fr 1fr}}
.public-deck-detail{box-sizing:border-box;min-height:100%;padding:24px clamp(14px,3vw,48px) 56px;color:#eee;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.back-link{display:inline-block;margin-bottom:14px;color:#80d8dc;text-decoration:none}.detail-head{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:14px;align-items:end;padding:14px;border:1px solid #35434c;background:#101820}.detail-head :deep(.deck-profile){border:0;background:transparent}.metrics{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:7px}.metrics span{padding:6px 8px;border:1px solid #43515a;background:#0a1117;color:#bac4c5;font-size:12px}.detail-tabs{display:flex;gap:6px;margin-top:14px;overflow-x:auto;padding-bottom:2px}.detail-tabs button,.content-panel button,.content-panel select{min-height:38px;padding:7px 11px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:800;white-space:nowrap}.detail-tabs button.active{border-color:#e0bf6d;color:#f4d980}.deck-layout{display:grid;grid-template-columns:220px minmax(0,1fr);gap:14px;margin-top:14px}.deck-layout>aside{display:grid;align-content:start;gap:10px}.deck-layout>aside:first-child section{padding:12px;border:1px solid #35434c;background:#0c141a}.deck-layout>aside:first-child p{display:flex;justify-content:space-between;color:#8c999d;font-size:13px}.deck-layout>aside:first-child strong{color:#eee}.curve{display:flex;height:105px;align-items:end;gap:3px;margin-top:8px}.curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.curve small,.curve em{font-size:11px;font-style:normal}.content-panel{margin-top:14px;padding:16px;border:1px solid #35434c;background:#0c141a}.content-panel>header{display:flex;justify-content:space-between;gap:12px;align-items:start;margin-bottom:14px}.content-panel h2,.content-panel h3,.content-panel p{margin:0}.content-panel header p,.empty-copy,.hand-note{margin-top:5px;color:#8c999d}.content-panel label{display:grid;gap:6px;margin:12px 0;color:#bac4c5;font-weight:700}.content-panel textarea{box-sizing:border-box;width:100%;padding:10px;border:1px solid #4c5a62;background:#081015;color:#eee;font:inherit;line-height:1.6;resize:vertical}.reading-sections,.matchup-list,.version-list,.match-list{display:grid;gap:10px}.reading-sections article,.matchup-list article,.matchup-editor,.version-list details,.match-list article{padding:12px;border:1px solid #35434c;background:#101820}.reading-sections p,.matchup-list p{margin-top:6px;white-space:pre-wrap;line-height:1.65}.matchup-list p b{display:inline-block;min-width:64px;color:#d8c07b}.matchup-city{display:flex;align-items:center;gap:10px;margin-bottom:10px}.matchup-city :deep(.l12-card-image){width:54px;height:54px;flex:none;object-fit:cover;object-position:center 24%;border:1px solid #536169}.matchup-editor{margin-top:10px}.editor-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.content-panel .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.content-panel .danger{border-color:#9e3944;background:#4d171d}.version-list summary{display:grid;grid-template-columns:auto minmax(0,1fr) auto;gap:10px;cursor:pointer}.version-list time,.match-list time{color:#8c999d}.version-list ul{margin:10px 0 0;padding-left:22px}.version-list details>p{margin-top:10px;color:#bac4c5}.match-list article{display:grid;grid-template-columns:auto minmax(0,1fr) auto auto;gap:10px;align-items:center}.mobile-replay{display:none}.opening-hand{display:grid;grid-template-columns:repeat(6,minmax(90px,1fr));gap:10px}.opening-hand article{min-width:0}.opening-hand img{display:block;width:100%;aspect-ratio:5/7;object-fit:contain;background:#050708}.opening-hand b{display:block;overflow-wrap:anywhere;margin-top:5px;text-align:center;line-height:1.4}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px;margin-top:14px;padding-top:14px;border-top:1px solid #35434c}.actions button,.preview-mask button{min-height:40px;padding:8px 12px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.actions .primary,.preview-mask .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.actions .danger{border-color:#9e3944;background:#4d171d}.notice{position:fixed;right:18px;bottom:18px;padding:10px 13px;border:1px solid #d5b45f;background:#251b08;color:#f4d980}.state{display:grid;min-height:45vh;place-items:center;color:#89969a}.state.error{color:#e3a8ad}.preview-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:#010406d9}.preview-mask>section{position:relative;width:min(1100px,94vw);max-height:90vh;border:1px solid #52606a;background:#111923}.preview-mask img{display:block;width:100%;max-height:78vh;object-fit:contain}.preview-mask .close{position:absolute;right:8px;top:8px}.preview-mask footer{display:flex;justify-content:flex-end;padding:10px}
@media(max-width:700px){.public-deck-detail{padding:14px 11px 44px}.detail-head{grid-template-columns:1fr;align-items:start}.metrics{justify-content:flex-start}.detail-tabs{margin-inline:-11px;padding-inline:11px}.deck-layout{grid-template-columns:1fr}.deck-layout>aside{grid-template-columns:1fr 1fr}.content-panel{padding:12px}.content-panel>header{align-items:stretch;flex-direction:column}.editor-actions{flex-wrap:wrap}.editor-actions button{flex:1}.version-list summary{grid-template-columns:auto 1fr}.version-list summary time{grid-column:1/-1}.match-list article{grid-template-columns:1fr}.desktop-replay{display:none}.mobile-replay{display:inline;color:#8c999d}.opening-hand{display:flex;overflow-x:auto;padding-bottom:8px}.opening-hand article{flex:0 0 112px}.actions button{flex:1 1 42%}.notice{position:static}.preview-mask{padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}}
@media(max-width:440px){.deck-layout>aside{grid-template-columns:1fr}.metrics span{font-size:11px}.actions button{font-size:12px}}
.detail-toolbar{display:flex;align-items:flex-start;justify-content:space-between;gap:10px;margin-top:14px}.detail-toolbar .detail-tabs{flex:1;flex-wrap:wrap;margin-top:0;overflow:visible}.detail-toolbar .actions{display:flex;flex:1;flex-wrap:wrap;justify-content:flex-end;margin:0;padding:0;border:0}.detail-anchor-section{scroll-margin-top:16px}.deck-layout.detail-anchor-section{margin-top:14px}
.opening-hand :deep(.l12-card-image){display:block;width:100%;aspect-ratio:5/7;background:#050708}.opening-hand small{display:block;overflow-wrap:anywhere;margin-top:4px;color:#cdbb7d;font-size:11px;text-align:center;line-height:1.35}
.match-stat-list{display:grid;gap:10px}.match-stat-list article{padding:12px;border:1px solid #35434c;background:#101820}.match-stat-list article>header{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px}.match-stat-list article>header span{color:#bac4c5}.match-stat-list dl{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin:10px 0 0}.match-stat-list dl div{padding:8px;border:1px solid #34434a;background:#0a1117}.match-stat-list dt{color:#8c999d;font-size:12px}.match-stat-list dd{margin:4px 0 0;color:#f0d47c;font-size:18px;font-weight:900}
@media(max-width:700px){.match-stat-list dl{grid-template-columns:1fr}}
@media(max-width:900px){.detail-toolbar{flex-direction:column}.detail-toolbar .detail-tabs,.detail-toolbar .actions{width:100%;justify-content:flex-start}.detail-toolbar .actions button{flex:1 1 auto}}
</style>
