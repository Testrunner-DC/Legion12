<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { createDeckImageBlob, downloadDeckImage, encodeDeckCode } from './deckShare'
import { automaticExtraCardIdsForMaster, deckCountSummary, loadDeckCatalog, loadOfficialPresetDecks, loadSavedDecks, saveDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { platformState, publicDeckApi, type PublishedDeck, type PublicDeckDetails, type PublicDeckGuide, type PublicDeckMatchup, type PublicDeckVersionChange } from '@/l12/platform'
import DeckProfile from '@/l12/DeckProfile.vue'
import DeckConstructionBrowser, { type ConstructionEntry } from './DeckConstructionBrowser.vue'
import { samplePublicDeckOpeningHand } from './publicDeckHands'
import { preservePublicDeckDetails } from './publicDeckEntry'
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
const activeSection = ref<'construction' | 'guide' | 'matchups' | 'versions' | 'matches' | 'hands'>('construction')
const editingContent = ref(false)
const savingContent = computed(() => entry.value ? actionPending(publicDeckActionKey(entry.value.id)) : false)
const guideDraft = ref<PublicDeckGuide>(emptyGuide())
const matchupDraft = ref<PublicDeckMatchup[]>([])
const openingHandIds = ref<string[]>([])
const sectionTabs = [
  { id: 'construction', label: '构筑' }, { id: 'guide', label: '指南' },
  { id: 'matchups', label: '对局建议' }, { id: 'versions', label: '版本' },
  { id: 'matches', label: '对局' }, { id: 'hands', label: '起手' },
] as const
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
const masters = computed(() => catalog.value.filter(card => card.cardType === 'master' || card.cardType === 'divinity'))
const openingHand = computed(() => openingHandIds.value.map(id => byId.value.get(id)).filter((card): card is DeckCard => Boolean(card)))
const isOwner = computed(() => Boolean(entry.value && entry.value.ownerId === platformState.account?.id && !entry.value.official))

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
      const viewedKey = `l12:public-deck-viewed:${id}`
      if (!sessionStorage.getItem(viewedKey)) {
        sessionStorage.setItem(viewedKey, '1')
        void publicDeckApi.recordView(id).then(value => {
          entry.value = preservePublicDeckDetails(entry.value, value)
        }).catch(() => sessionStorage.removeItem(viewedKey))
      }
    }
    redrawOpeningHand()
  } catch (error) { notice.value = error instanceof Error ? error.message : '公开牌库加载失败' }
  finally { loading.value = false }
})

function uniqueName(base: string) {
  const saved = loadSavedDecks()
  if (!saved[base]) return base.slice(0, 24)
  let index = 2
  while (saved[`${base} ${index}`.slice(0, 24)]) index += 1
  return `${base} ${index}`.slice(0, 24)
}
async function copyToMine() {
  if (!entry.value) return
  const id = entry.value.id
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      if (!entry.value || entry.value.id !== id) return
      const deck = { ...entry.value.deck, name: uniqueName(entry.value.deck.name), cardIds: [...entry.value.deck.cardIds], moraleIds: [...entry.value.deck.moraleIds], specialIds: [...(entry.value.deck.specialIds ?? [])], updatedAt: new Date().toISOString() }
      const saved = await saveDeck(deck)
      if (accountId === platformState.account?.id && entry.value?.id === id)
        notice.value = `已复制《${saved.name}》到我的牌库`
      if (!entry.value.official) {
        try {
          const updated = await publicDeckApi.recordCopy(id)
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
  const accountId = platformState.account.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      const updated = await publicDeckApi.toggleLike(id)
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
  const blob = await createDeckImageBlob(entry.value.deck, catalog.value)
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
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      await publicDeckApi.delete(id)
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
  return { guide: emptyGuide(), matchups: [], contentRevision: 0, versions: [], matches: [], matchBindingStatus: 'unavailable', matchBindingMessage: '这个牌库暂无可核验的版本对局记录。' }
}
function beginContentEdit() {
  guideDraft.value = { ...details.value.guide }
  matchupDraft.value = details.value.matchups.map(item => ({ ...item }))
  editingContent.value = true
}
function addMatchup() {
  const selected = new Set(matchupDraft.value.map(item => item.opponentMasterId))
  const opponentMasterId = masters.value.find(item => !selected.has(item.id))?.id ?? ''
  matchupDraft.value.push({ opponentMasterId, notes: '', keyCards: '', suggestedSwaps: '' })
}
async function saveContent() {
  if (!entry.value) return
  const id = entry.value.id
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      const updated = await publicDeckApi.updateContent(id, guideDraft.value, matchupDraft.value)
      if (accountId !== platformState.account?.id || entry.value?.id !== id) return
      entry.value.details = updated
      editingContent.value = false
      notice.value = '指南和对局建议已保存'
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '内容保存失败'
    }
  })
}
function redrawOpeningHand() {
  if (!entry.value) return
  openingHandIds.value = samplePublicDeckOpeningHand(entry.value.deck.cardIds)
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
      <nav class="detail-tabs" aria-label="公开牌库详情内容">
        <button v-for="tab in sectionTabs" :key="tab.id" :class="{ active: activeSection === tab.id }" @click="activeSection = tab.id">{{ tab.label }}</button>
      </nav>
      <section v-if="activeSection === 'construction'" class="deck-layout">
        <aside>
          <section><b>费用曲线</b><div class="curve"><i v-for="(value,index) in curve" :key="index"><span :style="{ height: `${Math.max(4, value / curveMax * 72)}px` }"></span><small>{{ index === 8 ? '8+' : index }}</small><em>{{ value }}</em></i></div></section>
          <section><b>构筑摘要</b><p>主牌<strong>{{ entry.deck.cardIds.length }}</strong></p><p>士气<strong>{{ entry.deck.moraleIds.length }}</strong></p><p>试炼/额外<strong>{{ entry.deck.specialIds?.length || 0 }}</strong></p><p>自动额外<strong>{{ automaticExtraCardIdsForMaster(entry.deck.masterId).length }}</strong></p></section>
        </aside>
        <DeckConstructionBrowser :entries="entries" :catalog="catalog" :master-faction="master?.faction" :title="`${entry.deck.name} · 全部构筑`"/>
      </section>
      <section v-else-if="activeSection === 'guide'" class="content-panel" data-detail-section="guide">
        <header><div><h2>牌库指南</h2><p v-if="details.contentUpdatedAt">作者更新于 {{ formatTime(details.contentUpdatedAt) }} · 修订 {{ details.contentRevision }}</p></div><button v-if="isOwner && !editingContent" @click="beginContentEdit">编辑指南</button></header>
        <template v-if="editingContent">
          <label>构筑思路<textarea v-model="guideDraft.buildIdea" rows="5" maxlength="1200"/></label>
          <label>起手建议<textarea v-model="guideDraft.opening" rows="4" maxlength="1200"/></label>
          <label>关键牌与配合<textarea v-model="guideDraft.keyCards" rows="4" maxlength="1200"/></label>
          <label>常见展开<textarea v-model="guideDraft.commonSequence" rows="4" maxlength="1200"/></label>
          <label>替换建议<textarea v-model="guideDraft.substitutions" rows="4" maxlength="1200"/></label>
          <div class="editor-actions"><button @click="editingContent = false">取消</button><button class="primary" :disabled="savingContent" @click="saveContent">{{ savingContent ? '保存中…' : '保存' }}</button></div>
        </template>
        <div v-else-if="Object.values(details.guide).some(Boolean)" class="reading-sections">
          <article v-for="item in [['构筑思路',details.guide.buildIdea],['起手建议',details.guide.opening],['关键牌与配合',details.guide.keyCards],['常见展开',details.guide.commonSequence],['替换建议',details.guide.substitutions]]" :key="item[0]"><h3>{{ item[0] }}</h3><p>{{ item[1] || '作者暂未填写。' }}</p></article>
        </div>
        <p v-else class="empty-copy">作者暂未填写牌库指南。</p>
      </section>
      <section v-else-if="activeSection === 'matchups'" class="content-panel" data-detail-section="matchups">
        <header><div><h2>对局建议</h2><p>按敌方主宰查看作者提供的思路、关键牌与换牌建议。</p></div><button v-if="isOwner && !editingContent" @click="beginContentEdit">编辑对局建议</button></header>
        <template v-if="editingContent">
          <article v-for="(row,index) in matchupDraft" :key="index" class="matchup-editor">
            <label>敌方主宰<select v-model="row.opponentMasterId"><option value="" disabled>请选择</option><option v-for="candidate in masters" :key="candidate.id" :value="candidate.id">{{ candidate.nameZh }}</option></select></label>
            <label>对局思路<textarea v-model="row.notes" rows="3" maxlength="800"/></label>
            <label>关键牌<textarea v-model="row.keyCards" rows="2" maxlength="800"/></label>
            <label>建议换牌<textarea v-model="row.suggestedSwaps" rows="2" maxlength="800"/></label>
            <button class="danger" @click="matchupDraft.splice(index,1)">移除此项</button>
          </article>
          <div class="editor-actions"><button @click="addMatchup">添加主宰</button><button @click="editingContent = false">取消</button><button class="primary" :disabled="savingContent" @click="saveContent">{{ savingContent ? '保存中…' : '保存' }}</button></div>
        </template>
        <div v-else-if="details.matchups.length" class="matchup-list"><article v-for="row in details.matchups" :key="row.opponentMasterId"><h3>对阵 {{ masterName(row.opponentMasterId) }}</h3><p><b>思路</b>{{ row.notes || '作者暂未填写。' }}</p><p><b>关键牌</b>{{ row.keyCards || '作者暂未填写。' }}</p><p><b>换牌</b>{{ row.suggestedSwaps || '作者暂未填写。' }}</p></article></div>
        <p v-else class="empty-copy">作者暂未添加对局建议。</p>
      </section>
      <section v-else-if="activeSection === 'versions'" class="content-panel" data-detail-section="versions">
        <header><div><h2>全部公开版本</h2><p>版本永久保存；相同构筑重复发布不会制造新版本。</p></div></header>
        <div v-if="details.versions.length" class="version-list"><details v-for="version in details.versions" :key="version.version" :open="version.version === details.versions[0]?.version"><summary><b>版本 {{ version.version }}</b><span>{{ version.name }}</span><time>{{ formatTime(version.createdAt) }}</time></summary><p v-if="version.version === 1">首次发布</p><ul v-else-if="version.changes.length"><li v-for="change in version.changes" :key="`${change.section}-${change.cardId}`">{{ changeLabel(change) }}</li></ul><p v-else>构筑正文未变化。</p></details></div>
        <p v-else class="empty-copy">尚无可读取的公开版本。</p>
      </section>
      <section v-else-if="activeSection === 'matches'" class="content-panel" data-detail-section="matches">
        <header><div><h2>版本对局</h2><p>这里只展示能由权威记录证明属于具体公开版本的对局。</p></div></header>
        <div v-if="details.matches.length" class="match-list"><article v-for="match in details.matches" :key="match.matchId"><b>{{ match.result }}</b><span>版本 {{ match.version }} · 对阵 {{ masterName(match.opponentMasterId) }}</span><time>{{ formatTime(match.playedAt) }}</time><router-link class="desktop-replay" :to="match.replayPath">查看回放</router-link><span class="mobile-replay">请使用电脑端查看回放</span></article></div>
        <p v-else class="empty-copy">{{ details.matchBindingMessage }}</p>
      </section>
      <section v-else class="content-panel" data-detail-section="hands">
        <header><div><h2>随机起手</h2><p>从当前公开版本主牌随机抽取 6 张；不会保存结果或生成真实对局记录。</p></div><button @click="redrawOpeningHand">重新抽取</button></header>
        <div class="opening-hand"><article v-for="(card,index) in openingHand" :key="`${card.id}-${index}`"><img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.nameZh"/><b>{{ card.nameZh }}</b></article></div>
        <p class="hand-note">实战中的可选开局效果、调度和特殊规则仍以对局服务端结算为准。</p>
      </section>
      <footer class="actions"><button v-if="!entry.official" :disabled="!platformState.account || actionPending(publicDeckActionKey(entry.id))" @click="toggleLike">♡ {{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : entry.liked ? '取消点赞' : '点赞' }}</button><button @click="copyCode">复制牌库码</button><button @click="previewImage">生成牌库图</button><button v-if="entry.ownerId === platformState.account?.id" @click="editDeck">编辑</button><button v-if="entry.ownerId === platformState.account?.id" class="danger" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="deleteDeck">{{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : '删除' }}</button><button class="primary" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="copyToMine">{{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : '复制到我的牌库' }}</button></footer>
    </template>
    <p v-if="notice && entry" class="notice">{{ notice }}</p>
    <div v-if="imagePreview && entry" class="preview-mask" @click.self="imagePreview = null"><section><button class="close" @click="imagePreview = null">×</button><img :src="imagePreview.url" alt="牌库图预览"/><footer><button class="primary" @click="downloadDeckImage(entry.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
  </main>
</template>

<style scoped>
.public-deck-detail{box-sizing:border-box;min-height:100%;padding:24px clamp(14px,3vw,48px) 56px;color:#eee;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.back-link{display:inline-block;margin-bottom:14px;color:#80d8dc;text-decoration:none}.detail-head{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:14px;align-items:end;padding:14px;border:1px solid #35434c;background:#101820}.detail-head :deep(.deck-profile){border:0;background:transparent}.metrics{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:7px}.metrics span{padding:6px 8px;border:1px solid #43515a;background:#0a1117;color:#bac4c5;font-size:12px}.detail-tabs{display:flex;gap:6px;margin-top:14px;overflow-x:auto;padding-bottom:2px}.detail-tabs button,.content-panel button,.content-panel select{min-height:38px;padding:7px 11px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:800;white-space:nowrap}.detail-tabs button.active{border-color:#e0bf6d;color:#f4d980}.deck-layout{display:grid;grid-template-columns:220px minmax(0,1fr);gap:14px;margin-top:14px}.deck-layout>aside{display:grid;align-content:start;gap:10px}.deck-layout>aside section{padding:12px;border:1px solid #35434c;background:#0c141a}.deck-layout>aside p{display:flex;justify-content:space-between;color:#8c999d;font-size:13px}.deck-layout>aside strong{color:#eee}.curve{display:flex;height:105px;align-items:end;gap:3px;margin-top:8px}.curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.curve small,.curve em{font-size:11px;font-style:normal}.content-panel{margin-top:14px;padding:16px;border:1px solid #35434c;background:#0c141a}.content-panel>header{display:flex;justify-content:space-between;gap:12px;align-items:start;margin-bottom:14px}.content-panel h2,.content-panel h3,.content-panel p{margin:0}.content-panel header p,.empty-copy,.hand-note{margin-top:5px;color:#8c999d}.content-panel label{display:grid;gap:6px;margin:12px 0;color:#bac4c5;font-weight:700}.content-panel textarea{box-sizing:border-box;width:100%;padding:10px;border:1px solid #4c5a62;background:#081015;color:#eee;font:inherit;line-height:1.6;resize:vertical}.reading-sections,.matchup-list,.version-list,.match-list{display:grid;gap:10px}.reading-sections article,.matchup-list article,.matchup-editor,.version-list details,.match-list article{padding:12px;border:1px solid #35434c;background:#101820}.reading-sections p,.matchup-list p{margin-top:6px;white-space:pre-wrap;line-height:1.65}.matchup-list p b{display:inline-block;min-width:64px;color:#d8c07b}.matchup-editor{margin-top:10px}.editor-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.content-panel .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.content-panel .danger{border-color:#9e3944;background:#4d171d}.version-list summary{display:grid;grid-template-columns:auto minmax(0,1fr) auto;gap:10px;cursor:pointer}.version-list time,.match-list time{color:#8c999d}.version-list ul{margin:10px 0 0;padding-left:22px}.version-list details>p{margin-top:10px;color:#bac4c5}.match-list article{display:grid;grid-template-columns:auto minmax(0,1fr) auto auto;gap:10px;align-items:center}.mobile-replay{display:none}.opening-hand{display:grid;grid-template-columns:repeat(6,minmax(90px,1fr));gap:10px}.opening-hand article{min-width:0}.opening-hand img{display:block;width:100%;aspect-ratio:5/7;object-fit:contain;background:#050708}.opening-hand b{display:block;overflow:hidden;margin-top:5px;text-align:center;text-overflow:ellipsis;white-space:nowrap}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px;margin-top:14px;padding-top:14px;border-top:1px solid #35434c}.actions button,.preview-mask button{min-height:40px;padding:8px 12px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.actions .primary,.preview-mask .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.actions .danger{border-color:#9e3944;background:#4d171d}.notice{position:fixed;right:18px;bottom:18px;padding:10px 13px;border:1px solid #d5b45f;background:#251b08;color:#f4d980}.state{display:grid;min-height:45vh;place-items:center;color:#89969a}.state.error{color:#e3a8ad}.preview-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:#010406d9}.preview-mask>section{position:relative;width:min(1100px,94vw);max-height:90vh;border:1px solid #52606a;background:#111923}.preview-mask img{display:block;width:100%;max-height:78vh;object-fit:contain}.preview-mask .close{position:absolute;right:8px;top:8px}.preview-mask footer{display:flex;justify-content:flex-end;padding:10px}
@media(max-width:700px){.public-deck-detail{padding:14px 11px 44px}.detail-head{grid-template-columns:1fr;align-items:start}.metrics{justify-content:flex-start}.detail-tabs{margin-inline:-11px;padding-inline:11px}.deck-layout{grid-template-columns:1fr}.deck-layout>aside{grid-template-columns:1fr 1fr}.content-panel{padding:12px}.content-panel>header{align-items:stretch;flex-direction:column}.editor-actions{flex-wrap:wrap}.editor-actions button{flex:1}.version-list summary{grid-template-columns:auto 1fr}.version-list summary time{grid-column:1/-1}.match-list article{grid-template-columns:1fr}.desktop-replay{display:none}.mobile-replay{display:inline;color:#8c999d}.opening-hand{display:flex;overflow-x:auto;padding-bottom:8px}.opening-hand article{flex:0 0 112px}.actions button{flex:1 1 42%}.notice{position:static}.preview-mask{padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}}
@media(max-width:440px){.deck-layout>aside{grid-template-columns:1fr}.metrics span{font-size:11px}.actions button{font-size:12px}}
</style>
