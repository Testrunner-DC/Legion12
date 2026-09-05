<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { articleApi, siteContentApi, type Article, type SiteCategory, type SiteContentKind } from '@/l12/platform'
import ArticleContentRenderer from './ArticleContentRenderer.vue'
import { articleBodyText } from './articleBlocks'

const route = useRoute()
const categories = ref<SiteCategory[]>([])
const entries = ref<Article[]>([])
const category = ref('')
const search = ref('')
const loading = ref(true)
const kind = computed<SiteContentKind>(() => route.name === 'products' ? 'product' : route.name === 'videos' ? 'video' : 'news')
const pageCopy = computed(() => ({
  news: { eyebrow: 'NEWS', title: '资讯一览', description: '官方资讯与站点更新的统一入口。', search: '搜索资讯', all: '全部资讯', empty: '当前筛选下暂无资讯' },
  product: { eyebrow: 'PRODUCTS', title: '产品上新', description: '浏览已经正式发布的产品信息。', search: '搜索产品', all: '全部产品', empty: '当前筛选下暂无产品' },
  video: { eyebrow: 'VIDEO', title: '最新视频', description: '浏览已经正式发布的社群视频。', search: '搜索视频', all: '全部视频', empty: '当前筛选下暂无视频' },
}[kind.value]))
const visible = computed(() => entries.value.filter(item => {
  const categoryMatch = !category.value || item.categoryId === category.value
  const query = search.value.trim().toLocaleLowerCase()
  return categoryMatch && (!query || `${item.title} ${item.summary} ${item.body} ${item.videoAuthorName || ''}`.toLocaleLowerCase().includes(query))
}))
const categoryCounts = computed(() => Object.fromEntries(categories.value.map(item => [item.id, entries.value.filter(entry => entry.categoryId === item.id).length])))

watch(kind, async value => {
  loading.value = true; category.value = ''; search.value = ''
  try { [entries.value, categories.value] = await Promise.all([articleApi.list({ kind: value, limit: 200 }), siteContentApi.categories(value)]) }
  catch { entries.value = []; categories.value = [] }
  finally { loading.value = false }
}, { immediate: true })
</script>

<template>
  <div class="news-page">
    <header><div><small>{{ pageCopy.eyebrow }}</small><h1>{{ pageCopy.title }}</h1><p>{{ pageCopy.description }}栏目由后台动态维护。</p></div><div class="content-page-actions"><router-link v-if="kind !== 'news'" to="/">← 返回主页</router-link><input v-model="search" :placeholder="pageCopy.search"></div></header>
    <nav class="category-tabs"><button :class="{ active: !category }" @click="category = ''">{{ pageCopy.all }}</button><button v-for="item in categories" :key="item.id" :class="{ active: category === item.id }" @click="category = item.id">{{ item.name }}<span>{{ categoryCounts[item.id] || 0 }}</span></button></nav>
    <main class="news-list" :class="{ 'video-grid': kind === 'video', 'product-list': kind === 'product' }">
      <component :is="entry.link ? 'a' : 'article'" v-for="entry in kind === 'video' ? visible : []" :id="`article-${entry.id}`" :key="entry.id" class="video-card" :class="{ pinned: entry.pinned }" :href="entry.link || undefined" :target="entry.link.startsWith('http') ? '_blank' : undefined" rel="noopener">
        <span class="video-cover"><img v-if="entry.coverUrl" :src="entry.coverUrl" :alt="entry.title"><i>▶</i></span>
        <span class="video-card-copy"><b>{{ entry.title }}</b><em v-if="entry.videoAuthorName">作者：{{ entry.videoAuthorName }}</em></span>
      </component>
      <article v-for="entry in kind !== 'video' ? visible : []" :id="`article-${entry.id}`" :key="entry.id" :class="{ pinned: entry.pinned }">
        <img v-if="entry.coverUrl" :src="entry.coverUrl" :alt="entry.title">
        <div class="news-copy"><time>{{ entry.pinned ? '置顶 · ' : '' }}{{ new Date(entry.publishedAt || entry.publishAt || entry.createdAt).toLocaleDateString() }}</time><h2>{{ entry.title }}</h2><p class="summary">{{ entry.summary || articleBodyText(entry.body) }}</p><details v-if="entry.body"><summary>阅读全文</summary><ArticleContentRenderer v-if="kind === 'news'" class="news-body" :body="entry.body" :media="entry.bodyMedia || []"/><p v-else class="news-body">{{ entry.body }}</p></details><footer><span>{{ entry.category }}</span><a v-if="entry.link" :href="entry.link" :target="entry.link.startsWith('http') ? '_blank' : undefined" rel="noopener">相关链接 →</a></footer></div>
      </article>
      <div v-if="!visible.length" class="empty"><b>{{ loading ? '正在读取已发布内容…' : pageCopy.empty }}</b><span v-if="!loading">管理员可在后台对应内容工作台维护稿件。</span></div>
    </main>
  </div>
</template>

<style scoped>
.news-page{min-height:100%;padding:0 clamp(18px,4vw,64px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.news-page>header{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;padding:34px 0 22px}.news-page>header small{color:#52c4cb;font:900 9px monospace;letter-spacing:.2em}.news-page h1{margin:6px 0;font-size:32px}.news-page>header p{margin:0;color:#7c898f;font-size:11px}.news-page>header input{width:min(320px,38vw);padding:11px;border:1px solid #35424a;background:#080e13;color:#fff}.category-tabs{display:flex;overflow-x:auto;border:1px solid #35424a;background:#0a1117}.category-tabs button{display:flex;align-items:center;justify-content:center;gap:7px;min-width:120px;padding:12px 16px;border:0;background:transparent;color:#7d898f;font-weight:900;white-space:nowrap}.category-tabs button.active{background:#243036;color:#f0d06f}.category-tabs span{display:grid;min-width:18px;height:18px;place-items:center;border-radius:9px;background:#18232a;color:#65cbd0;font-size:8px}.news-list{margin-top:14px;border:1px solid #35424a;background:#0f171f}.news-list article{display:grid;grid-template-columns:minmax(180px,300px) 1fr;min-height:230px;border-bottom:1px solid #35424a}.news-list article:last-child{border-bottom:0}.news-list article>img{width:100%;height:100%;max-height:320px;object-fit:cover}.news-copy{padding:26px}.news-list time{color:#d9b65f;font-size:9px;font-weight:900}.news-list h2{margin:8px 0;font-size:23px}.news-list p{color:#8c979c;font-size:11px;line-height:1.8;white-space:pre-wrap}.summary{display:-webkit-box;overflow:hidden;-webkit-box-orient:vertical;-webkit-line-clamp:3}.news-list details{margin-top:16px;color:#c9d0cd}.news-list summary{cursor:pointer;color:#56c4cb;font-size:10px;font-weight:900}.news-body{padding:12px;border-left:2px solid #4daeb5;background:#0a1116;color:#bcc4c1!important}.news-copy footer{display:flex;justify-content:space-between;margin-top:20px;color:#56c4cb;font-size:10px;font-weight:900}.news-copy footer a{color:#e1c36e;text-decoration:none}.pinned{box-shadow:inset 3px 0 #d6b85e}.empty{display:flex;min-height:320px;flex-direction:column;align-items:center;justify-content:center;color:#707d84;text-align:center}.empty b{color:#9aa3a7}.empty span{margin-top:6px;font-size:10px}@media(max-width:760px){.news-page{padding:0 12px 48px}.news-page>header{align-items:stretch;flex-direction:column}.news-page>header input{box-sizing:border-box;width:100%}.news-list article{grid-template-columns:1fr}.news-list article>img{max-height:210px}.news-copy{padding:20px}}
.content-page-actions{display:flex;align-items:center;gap:12px}.content-page-actions>a{padding:10px 14px;border:1px solid #d9bb69;color:#e3c779;font-size:11px;font-weight:900;text-decoration:none}@media(max-width:760px){.content-page-actions{align-items:stretch;flex-direction:column}.content-page-actions>a{text-align:center}}
.news-list:not(.video-grid) article>img{align-self:start;width:100%;height:auto;max-height:none;aspect-ratio:16/9;object-fit:cover}.news-list.product-list article>img{aspect-ratio:4/3}.news-list.video-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr));gap:22px;padding:22px;border:0;background:transparent}.video-card{display:flex!important;min-width:0;min-height:0!important;flex-direction:column;border:1px solid #35424a!important;background:#0f171f;color:#fff;text-decoration:none}.video-cover{position:relative;display:block;width:100%;aspect-ratio:16/9;overflow:hidden;background:#111c23}.video-cover img{width:100%;height:100%;object-fit:cover}.video-cover i{position:absolute;right:16px;bottom:14px;display:grid;width:42px;height:42px;place-items:center;border:1px solid #e0c26d;border-radius:50%;background:rgba(4,9,12,.75);color:#e0c26d;font-style:normal}.video-card-copy{display:flex;min-width:0;min-height:104px;flex-direction:column;gap:8px;padding:18px}.video-card-copy b{display:-webkit-box;overflow:hidden;overflow-wrap:anywhere;font-size:18px;line-height:1.45;-webkit-box-orient:vertical;-webkit-line-clamp:2}.video-card-copy em{overflow:hidden;color:#c4ccce;font-size:13px;font-style:normal;text-overflow:ellipsis;white-space:nowrap}@media(max-width:640px){.news-list.video-grid{grid-template-columns:1fr;padding:0}.video-card-copy{min-height:92px}}
</style>
