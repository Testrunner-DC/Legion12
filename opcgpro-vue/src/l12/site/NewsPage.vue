<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { articleApi, siteContentApi, type Article, type SiteCategory } from '@/l12/platform'

const categories = ref<SiteCategory[]>([])
const entries = ref<Article[]>([])
const category = ref('')
const search = ref('')
const loading = ref(true)
const visible = computed(() => entries.value.filter(item => {
  const categoryMatch = !category.value || item.categoryId === category.value
  const query = search.value.trim().toLocaleLowerCase()
  return categoryMatch && (!query || `${item.title} ${item.summary} ${item.body}`.toLocaleLowerCase().includes(query))
}))
const categoryCounts = computed(() => Object.fromEntries(categories.value.map(item => [item.id, entries.value.filter(entry => entry.categoryId === item.id).length])))

onMounted(async () => {
  try { [entries.value, categories.value] = await Promise.all([articleApi.list({ kind: 'news', limit: 200 }), siteContentApi.categories('news')]) }
  catch { entries.value = []; categories.value = [] }
  finally { loading.value = false }
})
</script>

<template>
  <div class="news-page">
    <header><div><small>NEWS</small><h1>资讯</h1><p>官方公告、规则勘误、赛季更新与赛事信息的统一入口。</p></div><input v-model="search" placeholder="搜索资讯"></header>
    <nav class="category-tabs"><button :class="{ active: !category }" @click="category = ''">全部资讯</button><button v-for="item in categories" :key="item.id" :class="{ active: category === item.id }" @click="category = item.id">{{ item.name }}<span>{{ categoryCounts[item.id] || 0 }}</span></button></nav>
    <main class="news-list">
      <article v-for="entry in visible" :key="entry.id" :class="{ pinned: entry.pinned }">
        <img v-if="entry.coverUrl" :src="entry.coverUrl" :alt="entry.title">
        <div class="news-copy"><time>{{ entry.pinned ? '置顶 · ' : '' }}{{ new Date(entry.publishedAt || entry.publishAt || entry.updatedAt).toLocaleDateString() }}</time><h2>{{ entry.title }}</h2><p class="summary">{{ entry.summary || entry.body }}</p><details v-if="entry.body"><summary>阅读全文</summary><p class="news-body">{{ entry.body }}</p></details><footer><span>{{ entry.category }}</span><a v-if="entry.link" :href="entry.link" :target="entry.link.startsWith('http') ? '_blank' : undefined" rel="noopener">相关链接 →</a></footer></div>
      </article>
      <div v-if="!visible.length" class="empty"><b>{{ loading ? '正在读取正式资讯…' : '当前筛选下暂无资讯' }}</b><span v-if="!loading">管理员可在后台“资讯发布”工作台维护稿件。</span></div>
    </main>
  </div>
</template>

<style scoped>
.news-page{min-height:100%;padding:0 clamp(18px,4vw,64px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.news-page>header{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;padding:34px 0 22px}.news-page>header small{color:#52c4cb;font:900 9px monospace;letter-spacing:.2em}.news-page h1{margin:6px 0;font-size:32px}.news-page>header p{margin:0;color:#7c898f;font-size:11px}.news-page>header input{width:min(320px,38vw);padding:11px;border:1px solid #35424a;background:#080e13;color:#fff}.category-tabs{display:flex;overflow-x:auto;border:1px solid #35424a;background:#0a1117}.category-tabs button{display:flex;align-items:center;justify-content:center;gap:7px;min-width:120px;padding:12px 16px;border:0;background:transparent;color:#7d898f;font-weight:900;white-space:nowrap}.category-tabs button.active{background:#243036;color:#f0d06f}.category-tabs span{display:grid;min-width:18px;height:18px;place-items:center;border-radius:9px;background:#18232a;color:#65cbd0;font-size:8px}.news-list{margin-top:14px;border:1px solid #35424a;background:#0f171f}.news-list article{display:grid;grid-template-columns:minmax(180px,300px) 1fr;min-height:230px;border-bottom:1px solid #35424a}.news-list article:last-child{border-bottom:0}.news-list article>img{width:100%;height:100%;max-height:320px;object-fit:cover}.news-copy{padding:26px}.news-list time{color:#d9b65f;font-size:9px;font-weight:900}.news-list h2{margin:8px 0;font-size:23px}.news-list p{color:#8c979c;font-size:11px;line-height:1.8;white-space:pre-wrap}.summary{display:-webkit-box;overflow:hidden;-webkit-box-orient:vertical;-webkit-line-clamp:3}.news-list details{margin-top:16px;color:#c9d0cd}.news-list summary{cursor:pointer;color:#56c4cb;font-size:10px;font-weight:900}.news-body{padding:12px;border-left:2px solid #4daeb5;background:#0a1116;color:#bcc4c1!important}.news-copy footer{display:flex;justify-content:space-between;margin-top:20px;color:#56c4cb;font-size:10px;font-weight:900}.news-copy footer a{color:#e1c36e;text-decoration:none}.pinned{box-shadow:inset 3px 0 #d6b85e}.empty{display:flex;min-height:320px;flex-direction:column;align-items:center;justify-content:center;color:#707d84;text-align:center}.empty b{color:#9aa3a7}.empty span{margin-top:6px;font-size:10px}@media(max-width:760px){.news-page{padding:0 12px 48px}.news-page>header{align-items:stretch;flex-direction:column}.news-page>header input{box-sizing:border-box;width:100%}.news-list article{grid-template-columns:1fr}.news-list article>img{max-height:210px}.news-copy{padding:20px}}
</style>
