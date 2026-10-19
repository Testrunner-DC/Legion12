<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { publicDeckReadApi, type PublicDeckCurrentRead, type SiteMediaEmbed } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import DeckCover from '@/l12/DeckCover.vue'
import ArticleInlineText from './ArticleInlineText.vue'
import { settlePublicDeckRead, validatePublicDeckCurrent } from './publicDeckRead'
import { articleInlineRuns, articleListItems, parseArticleBody, type ArticleBlock, type ArticleCardBlock, type ArticleImageBlock, type ArticlePublicDeckBlock, type ArticleTextBlock } from './articleBlocks'

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
type ArticleRenderItem = { kind: 'cards'; key: string; cards: ArticleCardBlock[] }
  | { kind: 'block'; key: string; block: Exclude<ArticleBlock, ArticleCardBlock> }
const renderItems = computed<ArticleRenderItem[]>(() => {
  const items: ArticleRenderItem[] = []
  let cards: ArticleCardBlock[] = []
  const flushCards = () => {
    if (!cards.length) return
    items.push({ kind: 'cards', key: `cards:${cards[0]!.id}`, cards })
    cards = []
  }
  for (const block of document.value.blocks) {
    if (block.type === 'card') cards.push(block)
    else { flushCards(); items.push({ kind: 'block', key: `block:${block.id}`, block }) }
  }
  flushCards()
  return items
})
const selectedCard = ref<DeckCard | null>(null)
const catalogError = ref('')
let catalogRequest: Promise<DeckCard[]> | null = null
let cardOpenGeneration = 0
let disposed = false

async function ensureCatalog() {
  if (catalog.value.length) { catalogError.value = ''; return true }
  let request = catalogRequest
  if (!request) {
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
const cardError = (block: ArticleCardBlock) => !cardFor(block) && catalogError.value ? catalogError.value : ''
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
      <template v-for="item in renderItems" :key="item.key">
      <div v-if="item.kind === 'cards'" class="article-card-row">
        <figure v-for="card in item.cards" :key="card.id" class="article-card-figure">
          <button type="button" class="article-card-embed" :aria-label="`查看单卡${cardFor(card)?.nameZh || card.cardId}详情`" @click="openEmbeddedCard(card)">
            <CardImage :card-id="card.cardId" :legacy-url="cardFor(card)?.imageUrl" :alt="cardFor(card)?.nameZh || card.cardId" intent="thumb" native-orientation/>
            <span v-if="cardError(card)" class="article-card-error">{{ cardError(card) }}</span>
          </button>
          <figcaption v-if="card.caption" class="article-card-caption">{{ card.caption }}</figcaption>
        </figure>
      </div>
      <p v-else-if="item.block.type === 'paragraph'" :style="alignStyle(item.block)"><ArticleInlineText :runs="runs(item.block)"/></p>
      <h2 v-else-if="item.block.type === 'h2'" :style="alignStyle(item.block)"><ArticleInlineText :runs="runs(item.block)"/></h2>
      <h3 v-else-if="item.block.type === 'h3'" :style="alignStyle(item.block)"><ArticleInlineText :runs="runs(item.block)"/></h3>
      <blockquote v-else-if="item.block.type === 'quote'" :style="alignStyle(item.block)"><ArticleInlineText :runs="runs(item.block)"/></blockquote>
      <ul v-else-if="item.block.type === 'bulletList'" :style="alignStyle(item.block)"><li v-for="listItem in articleListItems(item.block.text)" :key="`${item.block.id}-${listItem.from}`"><ArticleInlineText :runs="runs(item.block, listItem.from, listItem.to)"/></li></ul>
      <ol v-else-if="item.block.type === 'orderedList'" :style="alignStyle(item.block)"><li v-for="listItem in articleListItems(item.block.text)" :key="`${item.block.id}-${listItem.from}`"><ArticleInlineText :runs="runs(item.block, listItem.from, listItem.to)"/></li></ol>
      <hr v-else-if="item.block.type === 'divider'">
      <figure v-else-if="item.block.type === 'image' && imageFor(item.block)">
        <button type="button" class="image-zoom-trigger" :aria-label="`查看完整图片：${item.block.alt || imageFor(item.block)?.altText || '正文插图'}`" @click="openZoom(item.block)">
          <picture><source media="(max-width: 760px)" :srcset="imageFor(item.block)?.mobileUrl"><img :src="imageFor(item.block)?.desktopUrl" :alt="item.block.alt || imageFor(item.block)?.altText" loading="lazy"></picture>
        </button>
        <figcaption v-if="item.block.caption">{{ item.block.caption }}</figcaption>
      </figure>
      <RouterLink v-else-if="item.block.type === 'publicDeck'" class="article-deck-embed" :aria-label="`查看公共牌库${deckName(item.block)}`" :to="{ name: 'public-deck-detail', params: { deckId: item.block.reference } }">
        <DeckCover :name="deckName(item.block)" :master-id="deckReadFor(item.block)?.summary.masterId" :master-name="deckReadFor(item.block)?.summary.masterName" :faction="deckReadFor(item.block)?.summary.faction" :catalog="catalog" :cover-card-ids="deckReadFor(item.block)?.summary.coverCardIds" :source-card-ids="deckReadFor(item.block)?.deck.cardIds" :context="deckContext(item.block)" :meta="deckMeta(item.block)" badge="公共牌库"/>
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
.article-card-row{--article-card-gap:clamp(10px,2vw,20px);display:flex;flex-wrap:wrap;justify-content:center;align-items:flex-start;gap:var(--article-card-gap);width:min(760px,100%);margin:1.7em auto}.article-card-figure{display:grid;flex:0 0 calc((100% - var(--article-card-gap) - var(--article-card-gap))/3);min-width:0;margin:0!important;padding:0}.article-card-embed{position:relative;display:block;box-sizing:border-box;width:100%;margin:0;padding:0;border:0;background:transparent;color:#f3f1e9;cursor:pointer;transition:transform var(--l12-motion-duration-fast,160ms) ease,filter var(--l12-motion-duration-fast,160ms) ease}.article-card-embed :deep(.l12-card-image){width:100%;max-height:none}.article-card-error{position:absolute;right:5px;bottom:5px;left:5px;padding:5px 7px;background:#150d0de6;color:#f4a5ad;font-size:12px;font-weight:900;line-height:1.35;text-align:center}.article-card-caption{margin-top:8px!important;color:inherit!important;font-size:14px!important;line-height:1.55;text-align:center;white-space:pre-wrap}.article-card-embed:focus-visible,.article-deck-embed:focus-visible{outline:2px solid #58c7ce;outline-offset:3px}.article-deck-embed{display:block;width:min(760px,100%);margin:1.7em auto;color:inherit;text-decoration:none!important}.article-deck-embed :deep(.deck-cover){width:100%}@media(hover:hover) and (pointer:fine){.article-card-embed:hover{z-index:1;transform:translateY(-6px) scale(1.015);filter:drop-shadow(0 16px 14px rgba(0,0,0,.32))}}
.article-image-zoom{position:fixed;z-index:4600;inset:0;display:grid;place-items:center;padding:clamp(10px,3vw,32px);background:rgba(2,4,6,.93)}
.article-image-zoom__img{display:block;width:auto;height:auto;max-inline-size:100%;max-block-size:calc(100dvh - 96px);object-fit:contain}
.article-image-zoom__close{position:absolute;top:calc(10px + env(safe-area-inset-top));right:calc(12px + env(safe-area-inset-right));width:42px;height:42px;border:1px solid #5a686f;background:#0b1117;color:#f1eee6;font-size:22px;font-weight:900;cursor:pointer}
@media(max-width:560px){.article-card-row{--article-card-gap:10px}.article-card-caption{font-size:13px!important}}@media(prefers-reduced-motion:reduce){.article-card-embed{transition:none}.article-card-embed:hover{transform:none;filter:drop-shadow(0 8px 8px rgba(0,0,0,.24))}}
</style>
