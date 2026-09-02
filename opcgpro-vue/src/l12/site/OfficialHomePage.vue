<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { getPublicContentBatch } from '@/l12/platform'
import { createHomeContent, homeContentFields, newsContentKey, parseNewsEntries, type NewsEntry } from './homeContent'

const content = reactive(createHomeContent())
const ready = ref(false)
const news = ref<NewsEntry[]>([])
onMounted(async () => {
  try {
    const response = await getPublicContentBatch([...homeContentFields.map(field => field.key), newsContentKey])
    const next = createHomeContent()
    homeContentFields.forEach(field => { if (response.values[field.key]?.trim()) next[field.id] = response.values[field.key] })
    Object.assign(content, next)
    news.value = parseNewsEntries(response.values[newsContentKey]).filter(item => item.published && item.title.trim())
      .sort((a, b) => Number(b.pinned) - Number(a.pinned) || Date.parse(b.publishedAt) - Date.parse(a.publishedAt))
  } finally { ready.value = true }
})
const modules = computed(() => [
  { to: '/lobby', en: 'PLAY', title: content.playTitle, text: content.playText },
  { to: '/cards', en: 'DATABASE', title: content.cardsTitle, text: content.cardsText },
  { to: '/decks', en: 'DECKS', title: content.decksTitle, text: content.decksText },
  { to: '/records', en: 'REPLAY', title: content.recordsTitle, text: content.recordsText },
])
const featureLabels = computed(() => content.featureLabels.split(/\r?\n/).map(item => item.trim()).filter(Boolean))
const latestNews = computed(() => news.value[0])
</script>

<template>
  <div v-if="ready" class="official-home page-frame">
    <section class="official-hero">
      <div class="hero-copy">
        <p>{{ content.heroEyebrow }}</p>
        <h1>{{ content.headline }}<br/><span>{{ content.introduction }}</span></h1>
        <div class="hero-actions"><router-link to="/lobby">{{ content.primaryCta }}</router-link><router-link class="secondary" to="/cards">{{ content.secondaryCta }}</router-link></div>
        <ul><li v-for="label in featureLabels" :key="label">{{ label }}</li></ul>
      </div>
      <div class="hero-art"><img src="/assets/l12/card-back-gold.png" alt="十二军团官方卡背"/><i/><i/></div>
    </section>

    <section class="module-grid">
      <router-link v-for="(item,index) in modules" :key="item.to" :to="item.to">
        <small>0{{ index + 1 }} · {{ item.en }}</small><h2>{{ item.title }}</h2><p>{{ item.text }}</p><b>进入 →</b>
      </router-link>
    </section>

    <section class="official-columns">
      <article><header><small>OFFICIAL</small><h2>{{ content.newsTitle }}</h2></header><router-link v-if="latestNews" class="latest-news" to="/news"><b>{{ latestNews.title }}</b><span>{{ latestNews.summary || latestNews.body }}</span><small>{{ latestNews.category }} · {{ new Date(latestNews.publishedAt).toLocaleDateString() }}</small></router-link><div v-else class="empty-block"><template v-if="content.latestNews"><b>最新公告</b><span>{{ content.latestNews }}</span></template><template v-else><b>{{ content.newsEmptyTitle }}</b><span>{{ content.newsEmptyText }}</span></template></div></article>
      <article><header><small>RULES & FAQ</small><h2>{{ content.rulesTitle }}</h2></header><router-link to="/cards">{{ content.cardLinkLabel }} <b>→</b></router-link><a href="#" @click.prevent>{{ content.rulesLinkLabel }} <b>→</b></a><router-link to="/records">{{ content.replayLinkLabel }} <b>→</b></router-link></article>
      <article><header><small>DEVELOPMENT</small><h2>{{ content.developmentTitle }}</h2></header><div class="status-line"><span>对战框架</span><b>{{ content.battleStatus }}</b></div><div class="status-line"><span>S01 卡效</span><b>{{ content.s1Status }}</b></div><div class="status-line"><span>S02 卡效</span><b>{{ content.s2Status }}</b></div><div class="status-line"><span>移动端</span><b>{{ content.mobileStatus }}</b></div></article>
    </section>
  </div>
  <div v-else class="official-home-loading" aria-label="正在加载官网内容"><i/><i/><i/></div>
</template>

<style scoped>
.page-frame{min-height:100%;padding:0 clamp(20px,4vw,64px) 54px}.official-home{background:linear-gradient(145deg,rgba(103,18,29,.08),transparent 42%)}.official-hero{position:relative;min-height:470px;display:grid;grid-template-columns:minmax(0,1.2fr) minmax(320px,.8fr);align-items:center;overflow:hidden;border:1px solid rgba(235,230,216,.2);background:radial-gradient(circle at 78% 45%,rgba(39,148,156,.17),transparent 28%),radial-gradient(circle at 24% 80%,rgba(139,24,37,.22),transparent 38%),#0b1115}.official-hero::before{content:'';position:absolute;inset:0;background:linear-gradient(rgba(255,255,255,.022) 1px,transparent 1px),linear-gradient(90deg,rgba(255,255,255,.022) 1px,transparent 1px);background-size:36px 36px}.hero-copy{position:relative;z-index:2;padding:56px clamp(28px,6vw,86px)}.hero-copy>p{color:#5ac6cd;font:900 10px monospace;letter-spacing:.22em}.hero-copy h1{margin:12px 0 26px;font-size:clamp(46px,6vw,82px);line-height:1.02;letter-spacing:.04em}.hero-copy h1 span{color:#e3c276;font-size:.62em}.hero-actions{display:flex;gap:10px}.hero-actions a{padding:13px 20px;border:1px solid #e2c679;background:#e2c679;color:#0b0e10;font-size:13px;font-weight:900;text-decoration:none}.hero-actions a.secondary{border-color:#637078;background:#111920;color:#f2eee4}.hero-copy ul{display:flex;gap:24px;margin:28px 0 0;padding:0;color:#818c91;font-size:11px;font-weight:900;list-style:none}.hero-copy li::before{content:'◆';margin-right:8px;color:#9f2532;font-size:8px}.hero-art{position:relative;z-index:1;display:grid;place-items:center;height:100%}.hero-art img{position:relative;z-index:2;width:min(290px,72%);max-height:400px;object-fit:cover;border:1px solid rgba(255,255,255,.48);box-shadow:18px 20px 0 rgba(117,20,31,.55),-16px -14px 0 rgba(24,111,119,.36),0 35px 80px #000}.hero-art i{position:absolute;width:260px;height:260px;border:1px solid rgba(226,194,118,.35);transform:rotate(45deg)}.hero-art i:last-child{width:340px;height:340px;border-color:rgba(70,189,198,.18)}.module-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-top:14px}.module-grid a{min-height:178px;padding:22px;border:1px solid rgba(235,230,216,.16);background:#101820;color:#eee9df;text-decoration:none;transition:.18s}.module-grid a:hover{transform:translateY(-4px);border-color:#4dbbc3;background:#14232a}.module-grid small{color:#5ac6cd;font:900 9px monospace;letter-spacing:.16em}.module-grid h2{margin:13px 0 7px;font-size:21px}.module-grid p{min-height:42px;margin:0;color:#7d898f;font-size:11px;line-height:1.7}.module-grid b{display:block;margin-top:17px;color:#e3c276;font-size:11px}.official-columns{display:grid;grid-template-columns:1.4fr 1fr 1fr;gap:12px;margin-top:14px}.official-columns>article{min-height:230px;padding:22px;border:1px solid rgba(235,230,216,.16);background:#0d141b}.official-columns header{padding-bottom:13px;border-bottom:1px solid rgba(235,230,216,.1)}.official-columns header small{color:#a52d39;font:900 9px monospace;letter-spacing:.18em}.official-columns h2{margin:4px 0 0;font-size:19px}.empty-block{display:flex;height:130px;flex-direction:column;align-items:center;justify-content:center;border:1px dashed #354149;color:#65727a;text-align:center}.empty-block b{color:#90999e;font-size:13px}.empty-block span{margin-top:6px;font-size:10px}.official-columns a,.status-line{display:flex;align-items:center;justify-content:space-between;padding:13px 3px;border-bottom:1px solid rgba(235,230,216,.1);color:#aab2b5;font-size:11px;text-decoration:none}.official-columns a:hover{color:#fff}.status-line b{color:#e1c06f;font-size:10px}
.latest-news{height:130px!important;box-sizing:border-box;flex-direction:column!important;align-items:flex-start!important;justify-content:center!important}.latest-news b{color:#eee;font-size:14px}.latest-news span{display:-webkit-box;overflow:hidden;margin-top:7px;color:#89959a;line-height:1.6;-webkit-box-orient:vertical;-webkit-line-clamp:3}.latest-news small{margin-top:9px;color:#d9b65f}.official-home-loading{display:grid;min-height:70vh;place-content:center;gap:12px}.official-home-loading i{width:min(70vw,720px);height:18px;background:linear-gradient(90deg,#111820,#1c282f,#111820);background-size:200% 100%;animation:home-loading 1.2s linear infinite}.official-home-loading i:nth-child(2){width:min(52vw,520px)}.official-home-loading i:nth-child(3){width:min(34vw,340px)}@keyframes home-loading{to{background-position:-200% 0}}
@media(max-width:1050px){.official-hero{grid-template-columns:1fr .65fr}.module-grid{grid-template-columns:1fr 1fr}.official-columns{grid-template-columns:1fr 1fr}.official-columns>article:first-child{grid-column:1/-1}}
@media(max-width:700px){.page-frame{padding:0 12px 40px}.official-hero{min-height:0;grid-template-columns:1fr}.hero-copy{padding:38px 24px}.hero-copy h1{font-size:44px}.hero-art{display:none}.hero-copy ul{flex-wrap:wrap;gap:10px 18px}.hero-actions{flex-direction:column}.hero-actions a{text-align:center}.module-grid,.official-columns{grid-template-columns:1fr}.official-columns>article:first-child{grid-column:auto}.module-grid a{min-height:150px}}
</style>
