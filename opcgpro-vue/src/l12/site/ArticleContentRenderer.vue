<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { publicDeckReadApi, type PublicDeckCurrentRead, type SiteMediaEmbed } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import DeckCover from '@/l12/DeckCover.vue'
import ArticleInlineText from './ArticleInlineText.vue'
import { settlePublicDeckRead, validatePublicDeckCurrent } from './publicDeckRead'
import { articleInlineRuns, articleListItems, parseArticleBody, type ArticleCardBlock, type ArticleImageBlock, type ArticlePublicDeckBlock, type ArticleTextBlock } from './articleBlocks'

const props = withDefaults(defineProps<{ body?: string; media?: SiteMediaEmbed[] }>(), { body: '', media: () => [] })
const document = computed(() => parseArticleBody(props.body))
const mediaById = computed(() => new Map(props.media.map(item => [item.id, item])))
const imageFor = (block: ArticleImageBlock) => mediaById.value.get(block.mediaAssetId)
const runs = (block: ArticleTextBlock, from = 0, to = block.text.length) => articleInlineRuns(block.text, block.marks, from, to)
const alignStyle = (block: ArticleTextBlock) => ({ textAlign: block.align })
const catalog = ref<DeckCard[]>([])
const cardById = computed(() => {
  const cards = new Map<string, DeckCard>()
  for (const card of catalog.value) { cards.set(card.id, card); cards.set(card.id.toLocaleUpperCase('en-US'), card) }
  return cards
})
const cardFor = (block: ArticleCardBlock) => cardById.value.get(block.cardId)
  ?? cardById.value.get(block.cardId.toLocaleUpperCase('en-US'))
const selectedCard = ref<DeckCard | null>(null)
const catalogLoading = ref(false)
const catalogError = ref('')
let catalogRequest: Promise<DeckCard[]> | null = null
let cardOpenGeneration = 0
let disposed = false

async function ensureCatalog() {
  if (catalog.value.length) { catalogError.value = ''; return true }
  let request = catalogRequest
  if (!request) {
    catalogLoading.value = true
    catalogError.value = ''
    request = loadDeckCatalog()
    catalogRequest = request
  }
  try {
    const cards = await request
    if (disposed) return false
    catalog.value = cards
    catalogError.value = ''
    return true
  } catch {
    if (!disposed) catalogError.value = '卡牌资料加载失败，点击重试'
    return false
  } finally {
    if (catalogRequest === request) {
      catalogRequest = null
      if (!disposed) catalogLoading.value = false
    }
  }
}

async function openEmbeddedCard(block: ArticleCardBlock) {
  const generation = ++cardOpenGeneration
  const key = embedKey.value
  const cardId = block.cardId
  selectedCard.value = null
  let card = cardFor(block)
  if (!card) {
    if (!await ensureCatalog() || disposed || generation !== cardOpenGeneration || key !== embedKey.value) return
    card = cardById.value.get(cardId) ?? cardById.value.get(cardId.toLocaleUpperCase('en-US'))
  }
  if (disposed || generation !== cardOpenGeneration || key !== embedKey.value) return
  if (card) selectedCard.value = card
  else catalogError.value = '没有找到这张卡牌，点击重试'
}
const cardHint = (block: ArticleCardBlock) => cardFor(block)
  ? `${cardFor(block)?.number || block.cardId} · 点击查看详情`
  : catalogLoading.value ? '正在加载卡牌资料…' : catalogError.value || `${block.cardId} · 点击查看详情`
const deckReads = ref(new Map<string, PublicDeckCurrentRead>())
const deckErrors = ref(new Map<string, string>())
const MAX_EMBEDDED_DECK_READS = 12
const embedKey = computed(() => document.value.blocks
  .filter(block => block.type === 'card' || block.type === 'publicDeck')
  .map(block => block.type === 'card' ? `card:${block.cardId}` : `deck:${block.reference}`).join('\n'))
let embedGeneration = 0
interface EmbedRefreshPlan { generation: number; needsCatalog: boolean; references: string[] }
let latestRefreshPlan: EmbedRefreshPlan | null = null
let refreshRunning = false

const deckReadFor = (block: ArticlePublicDeckBlock) => deckReads.value.get(block.reference)
const deckName = (block: ArticlePublicDeckBlock) => deckReadFor(block)?.summary.name || '公共牌库'
const deckContext = (block: ArticlePublicDeckBlock) => deckReadFor(block)?.summary.author
  || deckErrors.value.get(block.reference) || '正在读取公共牌库…'
const deckMeta = (block: ArticlePublicDeckBlock) => {
  const summary = deckReadFor(block)?.summary
  return summary ? `${summary.counts.main} 主牌 · ${summary.counts.morale} 士气` : block.reference
}

async function drainEmbedRefreshes() {
  if (refreshRunning) return
  refreshRunning = true
  try {
    while (!disposed && latestRefreshPlan) {
      const plan = latestRefreshPlan
      latestRefreshPlan = null
      if (plan.needsCatalog && !catalog.value.length) await ensureCatalog()
      if (disposed) return
      if (latestRefreshPlan || plan.generation !== embedGeneration) continue
      const references = plan.references.slice(0, MAX_EMBEDDED_DECK_READS)
      const results = await Promise.all(references.map(async reference => ({ reference,
        result: await settlePublicDeckRead(publicDeckReadApi.current(reference), validatePublicDeckCurrent) })))
      if (disposed) return
      if (latestRefreshPlan || plan.generation !== embedGeneration) continue
      const reads = new Map<string, PublicDeckCurrentRead>(), errors = new Map<string, string>()
      for (const reference of plan.references.slice(MAX_EMBEDDED_DECK_READS)) errors.set(reference, '单篇资讯最多读取 12 个公共牌库')
      for (const { reference, result } of results) {
        if (result.status === 'available') reads.set(reference, result.value)
        else errors.set(reference, result.message)
      }
      deckReads.value = reads
      deckErrors.value = errors
    }
  } finally {
    refreshRunning = false
    if (!disposed && latestRefreshPlan) void drainEmbedRefreshes()
  }
}
function scheduleEmbedRefresh() {
  const blocks = document.value.blocks
  latestRefreshPlan = {
    generation: ++embedGeneration,
    needsCatalog: blocks.some(block => block.type === 'card' || block.type === 'publicDeck'),
    references: [...new Set(blocks.flatMap(block => block.type === 'publicDeck' ? [block.reference] : []))],
  }
  cardOpenGeneration += 1
  selectedCard.value = null
  void drainEmbedRefreshes()
}
watch(embedKey, scheduleEmbedRefresh, { immediate: true })

// 点击放大只看桌面交付图；仅归档的原始上传文件不暴露给读者。
const zoomUrl = ref('')
const zoomAlt = ref('')
let zoomTrigger: HTMLElement | null = null
function openZoom(block: ArticleImageBlock) {
  const media = imageFor(block)
  if (!media?.desktopUrl) return
  zoomTrigger = window.document.activeElement instanceof HTMLElement ? window.document.activeElement : null
  zoomUrl.value = media.desktopUrl
  zoomAlt.value = block.alt || media.altText || ''
}
function closeZoom() {
  if (!zoomUrl.value) return
  zoomUrl.value = ''
  zoomTrigger?.focus()
  zoomTrigger = null
}
function onZoomKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') { event.preventDefault(); closeZoom() }
}
watch(zoomUrl, value => {
  if (value) window.addEventListener('keydown', onZoomKeydown)
  else window.removeEventListener('keydown', onZoomKeydown)
})
onBeforeUnmount(() => {
  disposed = true
  embedGeneration += 1
  cardOpenGeneration += 1
  latestRefreshPlan = null
  window.removeEventListener('keydown', onZoomKeydown)
})
</script>

<template>
  <div class="article-content">
    <template v-for="block in document.blocks" :key="block.id">
      <p v-if="block.type === 'paragraph'" :style="alignStyle(block)"><ArticleInlineText :runs="runs(block)"/></p>
      <h2 v-else-if="block.type === 'h2'" :style="alignStyle(block)"><ArticleInlineText :runs="runs(block)"/></h2>
      <h3 v-else-if="block.type === 'h3'" :style="alignStyle(block)"><ArticleInlineText :runs="runs(block)"/></h3>
      <blockquote v-else-if="block.type === 'quote'" :style="alignStyle(block)"><ArticleInlineText :runs="runs(block)"/></blockquote>
      <ul v-else-if="block.type === 'bulletList'" :style="alignStyle(block)"><li v-for="item in articleListItems(block.text)" :key="`${block.id}-${item.from}`"><ArticleInlineText :runs="runs(block, item.from, item.to)"/></li></ul>
      <ol v-else-if="block.type === 'orderedList'" :style="alignStyle(block)"><li v-for="item in articleListItems(block.text)" :key="`${block.id}-${item.from}`"><ArticleInlineText :runs="runs(block, item.from, item.to)"/></li></ol>
      <hr v-else-if="block.type === 'divider'">
      <figure v-else-if="block.type === 'image' && imageFor(block)">
        <button type="button" class="image-zoom-trigger" :aria-label="`查看完整图片：${block.alt || imageFor(block)?.altText || '正文插图'}`" @click="openZoom(block)">
          <picture><source media="(max-width: 760px)" :srcset="imageFor(block)?.mobileUrl"><img :src="imageFor(block)?.desktopUrl" :alt="block.alt || imageFor(block)?.altText" loading="lazy"></picture>
        </button>
        <figcaption v-if="block.caption">{{ block.caption }}</figcaption>
      </figure>
      <button v-else-if="block.type === 'card'" type="button" class="article-card-embed" :aria-label="`查看单卡${cardFor(block)?.nameZh || block.cardId}详情`" @click="openEmbeddedCard(block)">
        <CardImage :card-id="block.cardId" :legacy-url="cardFor(block)?.imageUrl" :alt="cardFor(block)?.nameZh || block.cardId" intent="thumb" native-orientation/>
        <span><small>单卡</small><b>{{ cardFor(block)?.nameZh || block.cardId }}</b><em>{{ cardHint(block) }}</em></span>
      </button>
      <RouterLink v-else-if="block.type === 'publicDeck'" class="article-deck-embed" :aria-label="`查看公共牌库${deckName(block)}`" :to="{ name: 'public-deck-detail', params: { deckId: block.reference } }">
        <DeckCover :name="deckName(block)" :master-id="deckReadFor(block)?.summary.masterId" :master-name="deckReadFor(block)?.summary.masterName" :faction="deckReadFor(block)?.summary.faction" :catalog="catalog" :cover-card-ids="deckReadFor(block)?.summary.coverCardIds" :source-card-ids="deckReadFor(block)?.deck.cardIds" :context="deckContext(block)" :meta="deckMeta(block)" badge="公共牌库"/>
      </RouterLink>
    </template>
    <CatalogCardDetails v-if="selectedCard" :card="selectedCard" @close="selectedCard = null"/>
    <Teleport to="body">
      <div v-if="zoomUrl" class="article-image-zoom ui-state-scope" role="dialog" aria-modal="true" aria-label="查看完整图片" @click.self="closeZoom">
        <img class="article-image-zoom__img" :src="zoomUrl" :alt="zoomAlt">
        <button type="button" class="article-image-zoom__close" aria-label="关闭图片预览" @click="closeZoom">×</button>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.article-content{color:inherit;font-size:16px;line-height:1.9;overflow-wrap:anywhere;overflow-x:clip}.article-content p{margin:1.1em 0;white-space:pre-wrap}.article-content h2{margin:1.8em 0 .7em;font-size:1.75em;line-height:1.35}.article-content h3{margin:1.5em 0 .6em;font-size:1.35em;line-height:1.45}.article-content blockquote{margin:1.35em 0;padding:.8em 1.2em;border-left:4px solid #d1ad4e;background:rgba(127,127,127,.09);font-style:italic}.article-content ul,.article-content ol{margin:1em 0;padding-left:1.7em}.article-content li{margin:.4em 0}.article-content figure{margin:1.7em 0;max-inline-size:100%}.article-content .image-zoom-trigger{display:block;box-sizing:border-box;width:100%;max-inline-size:100%;margin:0;padding:0;border:0;background:none;cursor:zoom-in}.article-content .image-zoom-trigger:focus-visible{outline:2px solid #58c7ce;outline-offset:2px}.article-content picture,.article-content img{display:block;max-inline-size:100%}.article-content picture{width:100%}.article-content img{width:100%;height:auto}.article-content figcaption{margin-top:.65em;color:#7b8589;font-size:14px;text-align:center}.article-content :deep(a){color:#3d9fa8;text-decoration:underline;text-underline-offset:3px}.article-content :deep(strong){font-weight:900}.article-content :deep(em){font-style:italic}
.article-content hr{margin:2em 0;border:0;border-top:1px solid rgba(127,127,127,.36)}.article-content :deep(u){text-underline-offset:3px}.article-content :deep(s){opacity:.78}
.article-card-embed{display:grid;grid-template-columns:minmax(110px,190px) minmax(0,1fr);align-items:center;gap:clamp(16px,4vw,34px);box-sizing:border-box;width:min(620px,100%);margin:1.7em auto;padding:16px;border:1px solid #59676d;background:#0b1218;color:#f3f1e9;text-align:left;cursor:pointer}.article-card-embed :deep(.l12-card-image){width:100%;max-height:270px}.article-card-embed>span{display:grid;gap:8px;min-width:0}.article-card-embed small{color:#d2b85e;font-weight:900;letter-spacing:.08em}.article-card-embed b{overflow-wrap:anywhere;font-size:20px}.article-card-embed em{color:#9da8ac;font-size:13px;font-style:normal}.article-card-embed:hover,.article-card-embed:focus-visible{border-color:#d2b85e;box-shadow:0 16px 36px -24px #000}.article-card-embed:focus-visible,.article-deck-embed:focus-visible{outline:2px solid #58c7ce;outline-offset:3px}.article-deck-embed{display:block;width:min(760px,100%);margin:1.7em auto;color:inherit;text-decoration:none!important}.article-deck-embed :deep(.deck-cover){width:100%}
.article-image-zoom{position:fixed;z-index:4600;inset:0;display:grid;place-items:center;padding:clamp(10px,3vw,32px);background:rgba(2,4,6,.93)}
.article-image-zoom__img{display:block;width:auto;height:auto;max-inline-size:100%;max-block-size:calc(100dvh - 96px);object-fit:contain}
.article-image-zoom__close{position:absolute;top:calc(10px + env(safe-area-inset-top));right:calc(12px + env(safe-area-inset-right));width:42px;height:42px;border:1px solid #5a686f;background:#0b1117;color:#f1eee6;font-size:22px;font-weight:900;cursor:pointer}
@media(max-width:560px){.article-card-embed{grid-template-columns:minmax(88px,120px) minmax(0,1fr);gap:12px;padding:10px}.article-card-embed b{font-size:16px}.article-card-embed em{font-size:12px}}
</style>
