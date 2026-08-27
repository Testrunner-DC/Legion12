<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { createDeckImageBlob, decodeDeckCode, downloadDeckImage, encodeDeckCode } from './deckShare'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from '../cardPresentation'
import { compareDeckCardIds } from '../deckOrdering'
import { deckCountSummary, ensureOfficialPrebuiltDecks, loadDeckCatalog, loadOfficialPresetDecks, loadSavedDecks, saveDeck, validateDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { platformState, publicDeckApi, type PublishedDeck } from '@/l12/platform'
import { useRoute, useRouter } from 'vue-router'

const tab = ref<'mine' | 'plaza'>('mine')
const catalog = ref<DeckCard[]>([])
const saved = ref<Record<string, SavedL12Deck>>({})
const published = ref<PublishedDeck[]>([])
const selected = ref<PublishedDeck | null>(null)
const query = ref('')
const importCode = ref('')
const notice = ref('')
const publishName = ref('')
const showPublish = ref(false)
const factionFilter = ref('all')
const sortMode = ref<'popular' | 'newest' | 'name'>('popular')
const imagePreview = ref<{ deck: SavedL12Deck; blob: Blob; url: string } | null>(null)
const route = useRoute()
const router = useRouter()
const returnTo = computed(() => typeof route.query.from === 'string' && route.query.from.startsWith('/') ? route.query.from : '/decks')
const editorLink = (deckName?: string, publicationId?: string) => ({ path: '/deck-editor', query: { ...(deckName ? { deck: deckName } : {}), ...(publicationId ? { published: publicationId } : {}), returnTo: returnTo.value } })

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界',
}
onMounted(async () => {
  try {
    catalog.value = await loadDeckCatalog()
    saved.value = await ensureOfficialPrebuiltDecks()
    const [presets, community] = await Promise.all([loadOfficialPresetDecks(), publicDeckApi.list()])
    published.value = [
      ...presets.map((deck, index) => ({ id: `official-${index}`, ownerId: 'official', deck: { ...deck, specialIds: deck.specialIds ?? [], updatedAt: '' }, author: '十二军团官方预组', likes: 0, copies: 0, liked: false, official: true, createdAt: '', updatedAt: '' })),
      ...community,
    ]
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库页面加载失败'
  }
})

const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const mine = computed(() => Object.values(saved.value).sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)))
const plazaFactions = computed(() => [...new Set(published.value.map(entry => byId.value.get(entry.deck.masterId)?.faction).filter(Boolean) as string[])])
const filteredPublished = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const values = published.value.filter(entry => {
    const master = byId.value.get(entry.deck.masterId)
    return (factionFilter.value === 'all' || master?.faction === factionFilter.value)
      && (!keyword || [entry.deck.name, entry.author, master?.nameZh].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword)))
  })
  return [...values].sort((a, b) => sortMode.value === 'name'
    ? a.deck.name.localeCompare(b.deck.name, 'zh-CN')
    : sortMode.value === 'newest' ? b.deck.updatedAt.localeCompare(a.deck.updatedAt)
      : (b.likes + b.copies) - (a.likes + a.copies))
})
const selectedGroups = computed(() => selected.value ? [...selected.value.deck.cardIds.reduce((map, id) => map.set(id, (map.get(id) || 0) + 1), new Map<string, number>())]
  .sort(([left], [right]) => compareDeckCardIds(left, right, byId.value, byId.value.get(selected.value!.deck.masterId)?.faction)) : [])
const selectedCurve = computed(() => {
  const curve = Array(9).fill(0) as number[]
  selectedGroups.value.forEach(([id, count]) => curve[Math.min(8, byId.value.get(id)?.cost ?? 0)] += count)
  return curve
})
const selectedCurveMax = computed(() => Math.max(1, ...selectedCurve.value))
const selectedTypes = computed(() => {
  const totals = new Map<string, number>()
  selectedGroups.value.forEach(([id, count]) => {
    const type = cardTypeFilterKey(byId.value.get(id)?.cardType || 'unknown'); totals.set(type, (totals.get(type) || 0) + count)
  })
  return [...totals].map(([type, count]) => [cardTypeLabel(type), count] as const)
})

function uniqueName(base: string) {
  if (!saved.value[base]) return base.slice(0, 24)
  let index = 2
  let value = `${base} ${index}`.slice(0, 24)
  while (saved.value[value]) value = `${base} ${++index}`.slice(0, 24)
  return value
}
function copyToMine(entry: PublishedDeck) {
  const deck = { ...entry.deck, name: uniqueName(entry.deck.name), cardIds: [...entry.deck.cardIds], moraleIds: [...entry.deck.moraleIds], specialIds: [...(entry.deck.specialIds ?? [])], updatedAt: new Date().toISOString() }
  saveDeck(deck); saved.value = loadSavedDecks(); notice.value = `已复制《${deck.name}》到我的牌库`
  if (!entry.official) void publicDeckApi.recordCopy(entry.id).then(updatePublished).catch(() => undefined)
}
function updatePublished(entry: PublishedDeck) {
  const index = published.value.findIndex(item => item.id === entry.id)
  if (index >= 0) published.value[index] = entry
  if (selected.value?.id === entry.id) selected.value = entry
}
async function toggleLike(entry: PublishedDeck) {
  if (entry.official) return
  if (!platformState.account) { notice.value = '请先登录账号再点赞'; return }
  try { updatePublished(await publicDeckApi.toggleLike(entry.id)) }
  catch (error) { notice.value = error instanceof Error ? error.message : '点赞失败' }
}
async function publishDeck() {
  const deck = saved.value[publishName.value]
  if (!deck) return
  if (!platformState.account) { notice.value = '请先登录账号再公开牌库'; return }
  const error = validateDeck(deck, catalog.value)
  if (error) { notice.value = error; return }
  try {
    const entry = await publicDeckApi.publish(deck)
    if (published.value.some(item => item.id === entry.id)) updatePublished(entry)
    else published.value.push(entry)
    showPublish.value = false; tab.value = 'plaza'; selected.value = entry; notice.value = '牌库已公开到公开牌库'
  } catch (error) { notice.value = error instanceof Error ? error.message : '公开牌库失败' }
}
function editPublished(entry: PublishedDeck) {
  const deck = { ...entry.deck, cardIds: [...entry.deck.cardIds], moraleIds: [...entry.deck.moraleIds], specialIds: [...entry.deck.specialIds] }
  saveDeck(deck); saved.value = loadSavedDecks(); selected.value = null
  void router.push(editorLink(deck.name, entry.id))
}
async function deletePublished(entry: PublishedDeck) {
  if (!window.confirm('确定删除这个公开牌库？删除后将不再显示在公开牌库。')) return
  try {
    await publicDeckApi.delete(entry.id)
    published.value = published.value.filter(item => item.id !== entry.id)
    if (selected.value?.id === entry.id) selected.value = null
    notice.value = `已从公开牌库删除《${entry.deck.name}》`
  } catch (error) { notice.value = error instanceof Error ? error.message : '删除公开牌库失败' }
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
function importFromCode() {
  try {
    const deck = decodeDeckCode(importCode.value)
    deck.name = uniqueName(deck.name)
    const error = validateDeck(deck, catalog.value)
    if (error) throw new Error(error)
    saveDeck(deck); saved.value = loadSavedDecks(); importCode.value = ''; notice.value = `已导入《${deck.name}》`
  } catch (error) { notice.value = error instanceof Error ? error.message : '牌库码导入失败' }
}
</script>

<template>
  <div class="deck-page">
    <header class="page-head"><div><small>DECK LIBRARY</small><h1>牌库</h1><p>构筑、保存、分享并发现公开牌库。</p></div><router-link :to="editorLink()">＋ 新建牌库</router-link></header>
    <div class="deck-tabs"><button :class="{ active: tab === 'mine' }" @click="tab = 'mine'">我的牌库</button><button :class="{ active: tab === 'plaza' }" @click="tab = 'plaza'">公开牌库</button></div>

    <template v-if="tab === 'mine'">
      <section class="import-panel"><input v-model="importCode" placeholder="粘贴 L12D1 开头的牌库码"/><button :disabled="!importCode.trim()" @click="importFromCode">导入牌库码</button><button :disabled="!mine.length" @click="showPublish = true">公开牌库</button></section>
      <section v-if="mine.length" class="mine-grid"><article v-for="deck in mine" :key="deck.name"><div class="deck-banner"><img v-if="byId.get(deck.masterId)?.imageUrl" :src="byId.get(deck.masterId)?.imageUrl"/><span>{{ byId.get(deck.masterId)?.nameZh || '主宰' }}</span></div><h2>{{ deck.name }}</h2><p>{{ deckCountSummary(deck.cardIds, byId).label }} 张主牌 · {{ deck.moraleIds.length }} 张士气</p><div><router-link :to="editorLink(deck.name)">编辑</router-link><button @click="copyCode(deck)">复制牌库码</button><button @click="previewImage(deck)">生成牌库图</button></div></article></section>
      <div v-else class="empty-state"><b>还没有自定义牌库</b><p>从编辑器新建牌库，或粘贴其他玩家分享的牌库码。</p><router-link :to="editorLink()">打开牌库编辑器</router-link></div>
    </template>

    <template v-else>
      <section class="plaza-toolbar"><input v-model="query" placeholder="搜索牌库名称、作者或主宰"/><select v-model="factionFilter"><option value="all">全部阵营</option><option v-for="faction in plazaFactions" :key="faction" :value="faction">{{ factionLabels[faction] || faction }}</option></select><select v-model="sortMode"><option value="popular">热门</option><option value="newest">最新</option><option value="name">名称</option></select><button :disabled="!mine.length" @click="showPublish = true">发布我的牌库</button></section>
      <section class="plaza-grid"><article v-for="entry in filteredPublished" :key="entry.id"><button class="plaza-summary" @click="selected = entry"><div class="banner-strip"><img v-for="id in [entry.deck.masterId,...new Set(entry.deck.cardIds)].slice(0,5)" :key="id" v-show="byId.get(id)?.imageUrl" :src="byId.get(id)?.imageUrl"/></div><h2>{{ entry.deck.name }}</h2><p>{{ entry.author }} · {{ byId.get(entry.deck.masterId)?.nameZh }}</p><span>{{ deckCountSummary(entry.deck.cardIds, byId).label }} 主牌 · {{ entry.deck.moraleIds.length }} 士气</span></button><footer><button :class="{ liked: entry.liked }" :disabled="entry.official" @click="toggleLike(entry)">♡ {{ entry.likes }}</button><span>复制 {{ entry.copies }}</span><button @click="selected = entry">查看构筑</button></footer></article></section>
    </template>
    <p v-if="notice" class="deck-notice">{{ notice }}</p>

    <div v-if="selected" class="modal-mask" @click.self="selected = null"><section class="deck-detail"><header><div><small>{{ selected.author }}</small><h2>{{ selected.deck.name }}</h2><p>{{ byId.get(selected.deck.masterId)?.nameZh }} · {{ factionLabels[byId.get(selected.deck.masterId)?.faction || ''] }} · {{ deckCountSummary(selected.deck.cardIds, byId).label }} 张主牌</p></div><button @click="selected = null">×</button></header><div class="deck-analysis"><aside><article class="detail-master"><img v-if="byId.get(selected.deck.masterId)?.imageUrl" :src="byId.get(selected.deck.masterId)?.imageUrl"/><div><small>主宰</small><b>{{ byId.get(selected.deck.masterId)?.nameZh }}</b><span>{{ selected.deck.moraleIds.length }} 张士气</span></div></article><section><b>费用曲线</b><div class="detail-curve"><i v-for="(value,index) in selectedCurve" :key="index"><span :style="{height:`${Math.max(4,value/selectedCurveMax*62)}px`}"></span><small>{{ index === 8 ? '8+' : index }}</small><em>{{ value }}</em></i></div></section><section><b>卡牌类型</b><p v-for="[type,count] in selectedTypes" :key="type"><span>{{ type }}</span><strong>{{ count }}</strong></p></section></aside><div class="detail-grid"><article v-for="[id,count] in selectedGroups" :key="id" :class="{ 'landscape-thumbnail': isHorizontalCardType(byId.get(id)?.cardType) }"><img v-if="byId.get(id)?.imageUrl" :src="byId.get(id)?.imageUrl"/><b>×{{ count }}</b><span>{{ byId.get(id)?.nameZh || id }}</span><small>{{ byId.get(id)?.number || id }}</small></article></div></div><footer><button v-if="!selected.official" :disabled="!platformState.account" @click="toggleLike(selected)">♡ 点赞 {{ selected.likes }}</button><button @click="copyCode(selected.deck)">复制牌库码</button><button @click="previewImage(selected.deck)">生成牌库图</button><button v-if="selected.ownerId === platformState.account?.id" @click="editPublished(selected)">编辑公开牌库</button><button v-if="selected.ownerId === platformState.account?.id" class="danger" @click="deletePublished(selected)">删除公开牌库</button><button class="primary" @click="copyToMine(selected)">复制到我的牌库</button></footer></section></div>
    <div v-if="showPublish" class="modal-mask" @click.self="showPublish = false"><section class="publish-modal"><header><h2>公开牌库</h2><button @click="showPublish = false">×</button></header><p>选择一个已保存且合法的牌库公开展示。公开后可由作者继续编辑或删除。</p><select v-model="publishName"><option value="">选择牌库</option><option v-for="deck in mine" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select><button class="primary" :disabled="!publishName || !platformState.account" @click="publishDeck">确认公开</button></section></div>
    <div v-if="imagePreview" class="modal-mask image-mask" @click.self="closeImagePreview"><section class="image-preview"><header><div><small>16:9 SHARE IMAGE</small><h2>{{ imagePreview.deck.name }} · 牌库图</h2></div><button @click="closeImagePreview">×</button></header><img :src="imagePreview.url" alt="牌库图预览"/><footer><button @click="copyPreviewImage">复制图片</button><button class="primary" @click="downloadDeckImage(imagePreview.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
  </div>
</template>

<style scoped>
.deck-page{min-height:100%;padding:28px clamp(18px,3vw,48px) 58px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-end;justify-content:space-between;margin-bottom:18px}.page-head small{color:#51c3cb;font:900 9px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0 3px;font-size:30px}.page-head p{margin:0;color:#77858c;font-size:11px}.page-head>a{padding:12px 18px;border:1px solid #e2c474;background:#e2c474;color:#0a0e10;font-weight:900;text-decoration:none}.deck-tabs{display:grid;grid-template-columns:1fr 1fr;margin-bottom:14px;border:1px solid #35434c;background:#0b1117}.deck-tabs button{padding:13px;border:0;background:transparent;color:#76848c;font-weight:900}.deck-tabs button.active{background:linear-gradient(135deg,#7c1724,#ad2d39);color:#fff}.import-panel,.plaza-toolbar{display:grid;grid-template-columns:1fr auto auto;gap:8px;margin-bottom:14px;padding:12px;border:1px solid #35434c;background:#101820}.import-panel input,.plaza-toolbar input,.publish-modal select{padding:11px;border:1px solid #46545d;background:#070d12;color:#fff}.import-panel button,.plaza-toolbar button{padding:0 15px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.mine-grid,.plaza-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.mine-grid>article,.plaza-grid>article{overflow:hidden;border:1px solid rgba(235,230,216,.16);background:#101821}.mine-grid>article{padding:14px}.deck-banner{position:relative;height:112px;overflow:hidden;background:linear-gradient(120deg,#1d5860,#5c1723)}.deck-banner img{width:100%;height:100%;object-fit:cover;object-position:center 22%;filter:saturate(.8) brightness(.72)}.deck-banner::after{content:'';position:absolute;inset:0;background:linear-gradient(90deg,rgba(5,8,10,.1),rgba(5,8,10,.85))}.deck-banner span{position:absolute;z-index:2;right:13px;bottom:12px;font-size:17px;font-weight:900}.mine-grid h2{margin:13px 0 4px;font-size:17px}.mine-grid p{margin:0;color:#78868c;font-size:10px}.mine-grid>article>div:last-child{display:flex;flex-wrap:wrap;gap:6px;margin-top:14px}.mine-grid a,.mine-grid button{padding:7px 9px;border:1px solid #4b5961;background:#0b1218;color:#e8e5dd;font-size:9px;font-weight:900;text-decoration:none}.plaza-summary{display:block;width:100%;padding:12px;border:0;background:transparent;color:#fff;text-align:left}.banner-strip{display:flex;height:82px;overflow:hidden;background:#080e12}.banner-strip img{width:20%;height:100%;object-fit:cover;object-position:center 20%}.plaza-summary h2{margin:11px 0 4px;font-size:16px}.plaza-summary p,.plaza-summary span{display:block;margin:0;color:#77858b;font-size:9px}.plaza-grid footer{display:flex;align-items:center;justify-content:space-between;padding:9px 12px;border-top:1px solid rgba(235,230,216,.1)}.plaza-grid footer button{border:0;background:transparent;color:#93a0a5;font-size:10px;font-weight:900}.plaza-grid footer button.liked{color:#df6672}.plaza-grid footer span{color:#65727a;font-size:9px}.empty-state{display:grid;min-height:360px;place-items:center;align-content:center;border:1px dashed #34414a;color:#738089;text-align:center}.empty-state b{color:#aab2b4}.empty-state p{font-size:11px}.empty-state a{padding:10px 14px;border:1px solid #57c2c9;color:#78dbe1;font-size:11px;text-decoration:none}.deck-notice{position:fixed;z-index:80;right:18px;bottom:18px;max-width:420px;padding:11px 14px;border:1px solid #d5b45f;background:#251b08;color:#f4d980;font-size:11px;font-weight:900}.modal-mask{position:fixed;z-index:90;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,6,.78);backdrop-filter:blur(8px)}.deck-detail{display:flex;width:min(1040px,95vw);max-height:90vh;flex-direction:column;overflow:hidden;border:1px solid #52606a;background:#111923}.deck-detail>header,.publish-modal header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px;border-bottom:1px solid #354149}.deck-detail header small{color:#52c4cb;font-size:9px}.deck-detail h2{margin:5px 0;font-size:24px}.deck-detail header p{margin:0;color:#7c898f;font-size:10px}.deck-detail header button,.publish-modal header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.detail-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:10px;padding:20px;overflow:auto}.detail-grid article{position:relative;min-width:0}.detail-grid img{display:block;width:100%;aspect-ratio:5/7;object-fit:contain;background:#070b0f}.detail-grid article>b{position:absolute;right:4px;top:4px;display:grid;width:26px;height:26px;place-items:center;border-radius:50%;background:#e0bf6d;color:#080b0d}.detail-grid span{display:block;margin-top:4px;overflow:hidden;font-size:10px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}.deck-detail>footer{display:flex;justify-content:flex-end;gap:8px;padding:16px 20px;border-top:1px solid #354149}.deck-detail>footer button,.publish-modal>.primary{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.primary{border-color:#e0bf6d!important;background:#e0bf6d!important;color:#090c0e!important}.publish-modal{width:min(500px,94vw);padding-bottom:20px;border:1px solid #52606a;background:#111923}.publish-modal h2{margin:0}.publish-modal p{padding:0 20px;color:#849097;font-size:11px;line-height:1.7}.publish-modal select{width:calc(100% - 40px);margin:0 20px 12px}.publish-modal>.primary{display:block;margin:0 20px 0 auto}
@media(max-width:1050px){.mine-grid,.plaza-grid{grid-template-columns:1fr 1fr}}
@media(max-width:700px){.deck-page{padding:18px 12px 48px}.page-head{align-items:flex-start;flex-direction:column;gap:12px}.import-panel,.plaza-toolbar{grid-template-columns:1fr}.import-panel button,.plaza-toolbar button{padding:11px}.mine-grid,.plaza-grid{grid-template-columns:1fr}.detail-grid{grid-template-columns:repeat(3,1fr)}.deck-detail>footer{flex-wrap:wrap}.deck-detail>footer button{flex:1 1 40%}}
.plaza-toolbar{grid-template-columns:minmax(220px,1fr) 150px 120px auto}.plaza-toolbar select{padding:11px;border:1px solid #46545d;background:#070d12;color:#fff}.deck-notice{z-index:100}.deck-detail{width:min(1180px,95vw)}.deck-detail>header,.image-preview header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px;border-bottom:1px solid #354149}.image-preview header small{color:#52c4cb;font-size:9px}.image-preview h2{margin:5px 0;font-size:24px}.image-preview header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.deck-analysis{display:grid;grid-template-columns:230px 1fr;min-height:0;overflow:hidden}.deck-analysis>aside{display:grid;align-content:start;gap:12px;padding:20px;border-right:1px solid #354149;overflow:auto}.deck-analysis>aside>section{padding:12px;border:1px solid #334049;background:#0b1218}.deck-analysis>aside>section>b{font-size:11px}.detail-master{display:flex;gap:10px;align-items:center}.detail-master img{width:72px;aspect-ratio:5/7;object-fit:cover}.detail-master div{display:grid;gap:4px}.detail-master small,.detail-master span{color:#78868c;font-size:9px}.detail-curve{display:flex;height:92px;align-items:end;gap:3px;margin-top:8px}.detail-curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.detail-curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.detail-curve small,.detail-curve em{font-size:7px;font-style:normal}.detail-curve em{color:#89959a}.deck-analysis>aside>section>p{display:flex;justify-content:space-between;margin:7px 0;color:#89959a;font-size:9px}.deck-analysis>aside>section>p strong{color:#e8e4da}.detail-grid article>b{width:auto;min-width:28px;padding:0 4px}.detail-grid article>small{display:block;margin-top:3px;overflow:hidden;color:#77858c;font-size:8px;text-overflow:ellipsis;white-space:nowrap}.image-mask{z-index:95}.image-preview{width:min(1200px,96vw);max-height:94vh;border:1px solid #52606a;background:#111923}.image-preview>img{display:block;width:100%;max-height:74vh;object-fit:contain;background:#05080a}.image-preview footer{display:flex;justify-content:flex-end;gap:8px;padding:14px 20px;border-top:1px solid #354149}.image-preview footer button{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}
.detail-grid article.landscape-thumbnail{overflow:hidden}.detail-grid article.landscape-thumbnail img{position:relative;left:50%;width:140%;height:auto;aspect-ratio:8/5;transform:translateX(-50%) rotate(90deg);object-fit:contain}
.deck-detail>footer .danger{border-color:#9e3944;background:#4d171d;color:#ffdce0}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:1fr}.deck-analysis{grid-template-columns:1fr}.deck-analysis>aside{border-right:0;border-bottom:1px solid #354149}}
</style>
