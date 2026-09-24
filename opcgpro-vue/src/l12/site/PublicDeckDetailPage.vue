<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { createDeckImageBlob, downloadDeckImage, encodeDeckCode } from './deckShare'
import { automaticExtraCardIdsForMaster, deckCountSummary, loadDeckCatalog, loadOfficialPresetDecks, loadSavedDecks, saveDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { platformState, publicDeckApi, type PublishedDeck } from '@/l12/platform'
import DeckProfile from '@/l12/DeckProfile.vue'
import DeckConstructionBrowser, { type ConstructionEntry } from './DeckConstructionBrowser.vue'

const route = useRoute()
const router = useRouter()
const catalog = ref<DeckCard[]>([])
const entry = ref<PublishedDeck | null>(null)
const notice = ref('')
const loading = ref(true)
const imagePreview = ref<{ blob: Blob; url: string } | null>(null)
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

onMounted(async () => {
  try {
    catalog.value = await loadDeckCatalog()
    const id = String(route.params.deckId || '')
    if (id.startsWith('official-')) {
      const index = Number(id.slice('official-'.length))
      const preset = (await loadOfficialPresetDecks())[index]
      if (!preset) throw new Error('未找到这个官方牌库')
      entry.value = { id, ownerId: 'official', deck: { ...preset, specialIds: preset.specialIds ?? [], updatedAt: '' }, author: '十二军团官方预组', views: 0, likes: 0, copies: 0, liked: false, official: true, createdAt: '', updatedAt: '' }
    } else {
      entry.value = await publicDeckApi.get(id)
      const viewedKey = `l12:public-deck-viewed:${id}`
      if (!sessionStorage.getItem(viewedKey)) {
        sessionStorage.setItem(viewedKey, '1')
        void publicDeckApi.recordView(id).then(value => { entry.value = value }).catch(() => sessionStorage.removeItem(viewedKey))
      }
    }
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
  try {
    const deck = { ...entry.value.deck, name: uniqueName(entry.value.deck.name), cardIds: [...entry.value.deck.cardIds], moraleIds: [...entry.value.deck.moraleIds], specialIds: [...(entry.value.deck.specialIds ?? [])], updatedAt: new Date().toISOString() }
    const saved = await saveDeck(deck)
    notice.value = `已复制《${saved.name}》到我的牌库`
    if (!entry.value.official) entry.value = await publicDeckApi.recordCopy(entry.value.id)
  } catch (error) { notice.value = error instanceof Error ? error.message : '复制到我的牌库失败' }
}
async function toggleLike() {
  if (!entry.value || entry.value.official) return
  if (!platformState.account) { notice.value = '请先登录账号再点赞'; return }
  try { entry.value = await publicDeckApi.toggleLike(entry.value.id) }
  catch (error) { notice.value = error instanceof Error ? error.message : '点赞失败' }
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
  await publicDeckApi.delete(entry.value.id)
  await router.replace(backTo.value)
}
</script>

<template>
  <main class="public-deck-detail" data-ui-contract="public-deck-detail-page">
    <router-link class="back-link" :to="backTo">← 返回公开牌库</router-link>
    <p v-if="loading" class="state">正在载入构筑……</p>
    <p v-else-if="!entry" class="state error">{{ notice || '未找到这个公开牌库' }}</p>
    <template v-else>
      <header class="detail-head">
        <DeckProfile :master-id="entry.deck.masterId" :master-name="master?.nameZh" :fallback-url="master?.imageUrl" :name="entry.deck.name" :context="entry.author" :meta="`${deckCountSummary(entry.deck.cardIds, byId).label} 张主牌 · ${entry.deck.moraleIds.length} 张士气`"/>
        <div class="metrics"><span>浏览 {{ entry.views ?? 0 }}</span><span>点赞 {{ entry.likes }}</span><span>复制 {{ entry.copies }}</span><span>{{ entry.seasonCompliant === false ? '不符合本赛季' : '符合本赛季' }}</span></div>
      </header>
      <section class="deck-layout">
        <aside>
          <section><b>费用曲线</b><div class="curve"><i v-for="(value,index) in curve" :key="index"><span :style="{ height: `${Math.max(4, value / curveMax * 72)}px` }"></span><small>{{ index === 8 ? '8+' : index }}</small><em>{{ value }}</em></i></div></section>
          <section><b>构筑摘要</b><p>主牌<strong>{{ entry.deck.cardIds.length }}</strong></p><p>士气<strong>{{ entry.deck.moraleIds.length }}</strong></p><p>试炼/额外<strong>{{ entry.deck.specialIds?.length || 0 }}</strong></p><p>自动额外<strong>{{ automaticExtraCardIdsForMaster(entry.deck.masterId).length }}</strong></p></section>
        </aside>
        <DeckConstructionBrowser :entries="entries" :catalog="catalog" :master-faction="master?.faction" :title="`${entry.deck.name} · 全部构筑`"/>
      </section>
      <footer class="actions"><button v-if="!entry.official" :disabled="!platformState.account" @click="toggleLike">♡ {{ entry.liked ? '取消点赞' : '点赞' }}</button><button @click="copyCode">复制牌库码</button><button @click="previewImage">生成牌库图</button><button v-if="entry.ownerId === platformState.account?.id" @click="editDeck">编辑</button><button v-if="entry.ownerId === platformState.account?.id" class="danger" @click="deleteDeck">删除</button><button class="primary" @click="copyToMine">复制到我的牌库</button></footer>
    </template>
    <p v-if="notice && entry" class="notice">{{ notice }}</p>
    <div v-if="imagePreview && entry" class="preview-mask" @click.self="imagePreview = null"><section><button class="close" @click="imagePreview = null">×</button><img :src="imagePreview.url" alt="牌库图预览"/><footer><button class="primary" @click="downloadDeckImage(entry.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
  </main>
</template>

<style scoped>
.public-deck-detail{box-sizing:border-box;min-height:100%;padding:24px clamp(14px,3vw,48px) 56px;color:#eee;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.back-link{display:inline-block;margin-bottom:14px;color:#80d8dc;text-decoration:none}.detail-head{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:14px;align-items:end;padding:14px;border:1px solid #35434c;background:#101820}.detail-head :deep(.deck-profile){border:0;background:transparent}.metrics{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:7px}.metrics span{padding:6px 8px;border:1px solid #43515a;background:#0a1117;color:#bac4c5;font-size:12px}.deck-layout{display:grid;grid-template-columns:220px minmax(0,1fr);gap:14px;margin-top:14px}.deck-layout>aside{display:grid;align-content:start;gap:10px}.deck-layout>aside section{padding:12px;border:1px solid #35434c;background:#0c141a}.deck-layout>aside p{display:flex;justify-content:space-between;color:#8c999d;font-size:13px}.deck-layout>aside strong{color:#eee}.curve{display:flex;height:105px;align-items:end;gap:3px;margin-top:8px}.curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.curve small,.curve em{font-size:11px;font-style:normal}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px;margin-top:14px;padding-top:14px;border-top:1px solid #35434c}.actions button,.preview-mask button{min-height:40px;padding:8px 12px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.actions .primary,.preview-mask .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.actions .danger{border-color:#9e3944;background:#4d171d}.notice{position:fixed;right:18px;bottom:18px;padding:10px 13px;border:1px solid #d5b45f;background:#251b08;color:#f4d980}.state{display:grid;min-height:45vh;place-items:center;color:#89969a}.state.error{color:#e3a8ad}.preview-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:#010406d9}.preview-mask>section{position:relative;width:min(1100px,94vw);max-height:90vh;border:1px solid #52606a;background:#111923}.preview-mask img{display:block;width:100%;max-height:78vh;object-fit:contain}.preview-mask .close{position:absolute;right:8px;top:8px}.preview-mask footer{display:flex;justify-content:flex-end;padding:10px}
@media(max-width:700px){.public-deck-detail{padding:14px 11px 44px}.detail-head{grid-template-columns:1fr;align-items:start}.metrics{justify-content:flex-start}.deck-layout{grid-template-columns:1fr}.deck-layout>aside{grid-template-columns:1fr 1fr}.actions button{flex:1 1 42%}.notice{position:static}.preview-mask{padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}}
@media(max-width:440px){.deck-layout>aside{grid-template-columns:1fr}.metrics span{font-size:11px}.actions button{font-size:12px}}
</style>
