<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getPublicContentBatch } from '@/l12/platform'
import { newsContentKey, parseNewsEntries, type NewsEntry } from './homeContent'

const latestNews = ref('')
const entries = ref<NewsEntry[]>([])
const categoryCounts = computed(() => Object.fromEntries(['官方公告', '规则勘误', '赛季更新', '赛事信息'].map(category => [category, entries.value.filter(entry => entry.category === category).length])))
onMounted(async () => {
  try {
    const response = await getPublicContentBatch(['home.latestNews', newsContentKey])
    latestNews.value = response.values['home.latestNews']?.trim() || ''
    entries.value = parseNewsEntries(response.values[newsContentKey]).filter(entry => entry.published && entry.title.trim())
      .sort((a, b) => Number(b.pinned) - Number(a.pinned) || Date.parse(b.publishedAt) - Date.parse(a.publishedAt))
  } catch {}
})
</script>

<template><div class="news-page"><header><small>NEWS</small><h1>资讯</h1><p>官方公告、规则勘误、赛季更新与赛事信息的统一入口。</p></header><section class="news-layout"><main><article v-for="entry in entries" :key="entry.id"><time>{{ entry.pinned ? '置顶 · ' : '' }}{{ new Date(entry.publishedAt).toLocaleDateString() }}</time><h2>{{ entry.title }}</h2><p>{{ entry.summary || entry.body }}</p><details v-if="entry.body"><summary>阅读全文</summary><p class="news-body">{{ entry.body }}</p></details><div><span>{{ entry.category }}</span></div></article><article v-if="!entries.length && latestNews"><time>最新公告</time><h2>十二军团官方资讯</h2><p>{{ latestNews }}</p><div><span>官方公告</span></div></article><div v-if="!entries.length && !latestNews" class="empty"><b>暂无已发布的正式资讯</b><span>管理员可在后台发布正式内容。</span></div></main><aside><h2>资讯分类</h2><a v-for="category in ['官方公告','规则勘误','赛季更新','赛事信息']" :key="category" href="#" @click.prevent>{{ category }} <span>{{ categoryCounts[category] || 0 }}</span></a><router-link to="/rules">前往规则中心 <span>→</span></router-link></aside></section></div></template>

<style scoped>.news-page{min-height:100%;padding:0 clamp(18px,4vw,64px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.news-page>header{padding:34px 0 22px}.news-page>header small{color:#52c4cb;font:900 9px monospace;letter-spacing:.2em}.news-page h1{margin:6px 0;font-size:32px}.news-page>header p{margin:0;color:#7c898f;font-size:11px}.news-layout{display:grid;grid-template-columns:minmax(0,1fr) 280px;gap:14px}.news-layout main,.news-layout aside{border:1px solid #35424a;background:#0f171f}.news-layout article{padding:26px;border-bottom:1px solid #35424a}.news-layout time{color:#d9b65f;font-size:9px;font-weight:900}.news-layout article h2{margin:8px 0;font-size:21px}.news-layout article p{color:#8c979c;font-size:11px;line-height:1.8;white-space:pre-wrap}.news-layout article>div{display:flex;justify-content:space-between;margin-top:20px;color:#56c4cb;font-size:10px;font-weight:900}.news-layout details{margin-top:16px;color:#c9d0cd}.news-layout summary{cursor:pointer;color:#56c4cb;font-size:10px;font-weight:900}.news-body{padding:12px;border-left:2px solid #4daeb5;background:#0a1116;color:#bcc4c1!important}.empty{display:flex;min-height:320px;flex-direction:column;align-items:center;justify-content:center;color:#707d84;text-align:center}.empty b{color:#9aa3a7}.empty span{margin-top:6px;font-size:10px}.news-layout aside{height:max-content;padding:20px}.news-layout aside h2{margin:0 0 12px;font-size:17px}.news-layout aside a{display:flex;justify-content:space-between;padding:13px 2px;border-bottom:1px solid rgba(235,230,216,.09);color:#a9b1b4;font-size:11px;text-decoration:none}.news-layout aside a span{color:#5ac5cc}@media(max-width:760px){.news-page{padding:0 12px 48px}.news-layout{grid-template-columns:1fr}.news-layout aside{grid-row:1}.news-layout article{padding:20px}}</style>
