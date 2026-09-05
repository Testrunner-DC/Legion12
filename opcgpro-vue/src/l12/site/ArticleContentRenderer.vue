<script setup lang="ts">
import { computed } from 'vue'
import type { SiteMediaEmbed } from '@/l12/platform'
import ArticleInlineText from './ArticleInlineText.vue'
import { articleInlineRuns, articleListItems, parseArticleBody, type ArticleImageBlock, type ArticleTextBlock } from './articleBlocks'

const props = withDefaults(defineProps<{ body?: string; media?: SiteMediaEmbed[] }>(), { body: '', media: () => [] })
const document = computed(() => parseArticleBody(props.body))
const mediaById = computed(() => new Map(props.media.map(item => [item.id, item])))
const imageFor = (block: ArticleImageBlock) => mediaById.value.get(block.mediaAssetId)
const runs = (block: ArticleTextBlock, from = 0, to = block.text.length) => articleInlineRuns(block.text, block.marks, from, to)
</script>

<template>
  <div class="article-content">
    <template v-for="block in document.blocks" :key="block.id">
      <p v-if="block.type === 'paragraph'"><ArticleInlineText :runs="runs(block)"/></p>
      <h2 v-else-if="block.type === 'h2'"><ArticleInlineText :runs="runs(block)"/></h2>
      <h3 v-else-if="block.type === 'h3'"><ArticleInlineText :runs="runs(block)"/></h3>
      <blockquote v-else-if="block.type === 'quote'"><ArticleInlineText :runs="runs(block)"/></blockquote>
      <ul v-else-if="block.type === 'bulletList'"><li v-for="item in articleListItems(block.text)" :key="`${block.id}-${item.from}`"><ArticleInlineText :runs="runs(block, item.from, item.to)"/></li></ul>
      <ol v-else-if="block.type === 'orderedList'"><li v-for="item in articleListItems(block.text)" :key="`${block.id}-${item.from}`"><ArticleInlineText :runs="runs(block, item.from, item.to)"/></li></ol>
      <figure v-else-if="block.type === 'image' && imageFor(block)">
        <picture><source media="(max-width: 760px)" :srcset="imageFor(block)?.mobileUrl"><img :src="imageFor(block)?.desktopUrl" :alt="block.alt || imageFor(block)?.altText" loading="lazy"></picture>
        <figcaption v-if="block.caption">{{ block.caption }}</figcaption>
      </figure>
    </template>
  </div>
</template>

<style scoped>
.article-content{color:inherit;font-size:16px;line-height:1.9;overflow-wrap:anywhere}.article-content p{margin:1.1em 0;white-space:pre-wrap}.article-content h2{margin:1.8em 0 .7em;font-size:1.75em;line-height:1.35}.article-content h3{margin:1.5em 0 .6em;font-size:1.35em;line-height:1.45}.article-content blockquote{margin:1.35em 0;padding:.8em 1.2em;border-left:4px solid #d1ad4e;background:rgba(127,127,127,.09);font-style:italic}.article-content ul,.article-content ol{margin:1em 0;padding-left:1.7em}.article-content li{margin:.4em 0}.article-content figure{margin:1.7em 0}.article-content picture,.article-content img{display:block;width:100%}.article-content img{height:auto}.article-content figcaption{margin-top:.65em;color:#7b8589;font-size:13px;text-align:center}.article-content :deep(a){color:#3d9fa8;text-decoration:underline;text-underline-offset:3px}.article-content :deep(strong){font-weight:900}.article-content :deep(em){font-style:italic}
</style>
