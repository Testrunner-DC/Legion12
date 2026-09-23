<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { SiteMediaEmbed } from '@/l12/platform'
import ArticleInlineText from './ArticleInlineText.vue'
import { articleInlineRuns, articleListItems, parseArticleBody, type ArticleImageBlock, type ArticleTextBlock } from './articleBlocks'

const props = withDefaults(defineProps<{ body?: string; media?: SiteMediaEmbed[] }>(), { body: '', media: () => [] })
const document = computed(() => parseArticleBody(props.body))
const mediaById = computed(() => new Map(props.media.map(item => [item.id, item])))
const imageFor = (block: ArticleImageBlock) => mediaById.value.get(block.mediaAssetId)
const runs = (block: ArticleTextBlock, from = 0, to = block.text.length) => articleInlineRuns(block.text, block.marks, from, to)
const alignStyle = (block: ArticleTextBlock) => ({ textAlign: block.align })

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
onBeforeUnmount(() => window.removeEventListener('keydown', onZoomKeydown))
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
    </template>
    <Teleport to="body">
      <div v-if="zoomUrl" class="article-image-zoom" role="dialog" aria-modal="true" aria-label="查看完整图片" @click.self="closeZoom">
        <img class="article-image-zoom__img" :src="zoomUrl" :alt="zoomAlt">
        <button type="button" class="article-image-zoom__close" aria-label="关闭图片预览" @click="closeZoom">×</button>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.article-content{color:inherit;font-size:16px;line-height:1.9;overflow-wrap:anywhere;overflow-x:clip}.article-content p{margin:1.1em 0;white-space:pre-wrap}.article-content h2{margin:1.8em 0 .7em;font-size:1.75em;line-height:1.35}.article-content h3{margin:1.5em 0 .6em;font-size:1.35em;line-height:1.45}.article-content blockquote{margin:1.35em 0;padding:.8em 1.2em;border-left:4px solid #d1ad4e;background:rgba(127,127,127,.09);font-style:italic}.article-content ul,.article-content ol{margin:1em 0;padding-left:1.7em}.article-content li{margin:.4em 0}.article-content figure{margin:1.7em 0;max-inline-size:100%}.article-content .image-zoom-trigger{display:block;box-sizing:border-box;width:100%;max-inline-size:100%;margin:0;padding:0;border:0;background:none;cursor:zoom-in}.article-content .image-zoom-trigger:focus-visible{outline:2px solid #58c7ce;outline-offset:2px}.article-content picture,.article-content img{display:block;max-inline-size:100%}.article-content picture{width:100%}.article-content img{width:100%;height:auto}.article-content figcaption{margin-top:.65em;color:#7b8589;font-size:14px;text-align:center}.article-content :deep(a){color:#3d9fa8;text-decoration:underline;text-underline-offset:3px}.article-content :deep(strong){font-weight:900}.article-content :deep(em){font-style:italic}
.article-content hr{margin:2em 0;border:0;border-top:1px solid rgba(127,127,127,.36)}.article-content :deep(u){text-underline-offset:3px}.article-content :deep(s){opacity:.78}
.article-image-zoom{position:fixed;z-index:4600;inset:0;display:grid;place-items:center;padding:clamp(10px,3vw,32px);background:rgba(2,4,6,.93)}
.article-image-zoom__img{display:block;width:auto;height:auto;max-inline-size:100%;max-block-size:calc(100dvh - 96px);object-fit:contain}
.article-image-zoom__close{position:absolute;top:calc(10px + env(safe-area-inset-top));right:calc(12px + env(safe-area-inset-right));width:42px;height:42px;border:1px solid #5a686f;background:#0b1117;color:#f1eee6;font-size:22px;font-weight:900;cursor:pointer}
</style>
