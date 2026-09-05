<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { siteContentApi, type Article, type SiteMedia } from '@/l12/platform'
import { articleBodyText } from './articleBlocks'
import { defaultHomeComposition, defaultSiteLegal, parseHomeComposition, parseSiteLegal, type HomeHeroSlide } from './homeContent'

const ready = ref(false)
const composition = ref(defaultHomeComposition())
const legal = ref(defaultSiteLegal())
const news = ref<Article[]>([])
const videos = ref<Article[]>([])
const products = ref<Article[]>([])
const media = ref<SiteMedia[]>([])
const activeIndex = ref(0)
const carouselPaused = ref(false)
const carouselCycle = ref(0)
let carouselTimer = 0
const carouselDuration = 6500

const enabledSlides = computed<HomeHeroSlide[]>(() => {
  const configured = composition.value.heroSlides.filter(item => item.enabled)
  return configured.length ? configured : Array.from({ length: 3 }, (_, index) => ({
    id: `brand-fallback-${index + 1}`, eyebrow: `STC-${String(index + 1).padStart(2, '0')}`, title: `主视觉标题范例 ${index + 1}`, summary: '轮播副标题范例\n可在后台输入换行', footer: '2026.00.00 发布',
    href: '', linkLabel: '', mediaAssetId: '', enabled: true,
  }))
})
const activeSlide = computed(() => enabledSlides.value[activeIndex.value % enabledSlides.value.length]!)
const activeMedia = computed(() => media.value.find(item => item.id === activeSlide.value.mediaAssetId))
const activeSlideLabel = computed(() => activeSlide.value.title.trim() || activeSlide.value.summary.trim() || '首页轮播图')
const hasSlideCopy = computed(() => Boolean(activeSlide.value.eyebrow.trim() || activeSlide.value.title.trim() || activeSlide.value.summary.trim() || activeSlide.value.footer.trim()))
const activeNotices = computed(() => composition.value.notices.filter(item => item.enabled && item.label.trim() && item.href.trim()))
const homeNews = computed(() => news.value.slice(0, 5))
const homeVideos = computed(() => videos.value.slice(0, 5))
const featuredProduct = computed(() => products.value[0])
const newsHeading = computed(() => ['', '最新资讯'].includes(composition.value.newsTitle.trim()) ? '资讯一览' : composition.value.newsTitle.trim())
const videoEyebrow = computed(() => ['', 'COMMUNITY MOVIE'].includes(composition.value.videoEyebrow.trim()) ? 'VIDEO' : composition.value.videoEyebrow.trim())
const videoHeading = computed(() => ['', '社群视频'].includes(composition.value.videoTitle.trim()) ? '最新视频' : composition.value.videoTitle.trim())
const productHeading = computed(() => ['', '商品情报', '最新商品'].includes(composition.value.productTitle.trim()) ? '产品上新' : composition.value.productTitle.trim())
const productCategories = computed(() => {
  const firstByCategory = new Map<string, Article>()
  for (const item of products.value) if (!firstByCategory.has(item.category)) firstByCategory.set(item.category, item)
  return [...firstByCategory.values()]
})

const isInternal = (href?: string) => Boolean(href?.startsWith('/') && !href.startsWith('//'))
const mediaFor = (article?: Article) => media.value.find(item => item.id === article?.mediaAssetId)
const articleHref = (article: Article, fallback: string) => fallback === '/news' ? `/news/${article.id}` : article.link || fallback
const dateLabel = (article: Article) => new Date(article.publishedAt || article.publishAt || article.createdAt).toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' })

function showSlide(index: number, restart = true) {
  activeIndex.value = (index + enabledSlides.value.length) % enabledSlides.value.length
  carouselCycle.value += 1
  if (restart) startCarousel()
}
function startCarousel() {
  window.clearTimeout(carouselTimer)
  if (carouselPaused.value || enabledSlides.value.length < 2 || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
  carouselTimer = window.setTimeout(() => {
    showSlide(activeIndex.value + 1, false)
    startCarousel()
  }, carouselDuration)
}
function toggleCarousel() { carouselPaused.value = !carouselPaused.value; startCarousel() }

onMounted(async () => {
  try {
    const payload = await siteContentApi.home()
    composition.value = parseHomeComposition(payload.composition)
    legal.value = parseSiteLegal(payload.legal)
    news.value = payload.news; videos.value = payload.videos; products.value = payload.products; media.value = payload.media
  } catch {
    composition.value = defaultHomeComposition(); legal.value = defaultSiteLegal()
  } finally { ready.value = true; startCarousel() }
})
onBeforeUnmount(() => window.clearTimeout(carouselTimer))
</script>

<template>
  <div v-if="ready" class="official-home">
    <section class="hero-section" aria-roledescription="carousel" aria-label="首页主视觉">
      <div class="hero-stage" :class="{ 'has-art': activeMedia }" :data-fallback-index="activeIndex % 3">
        <Transition name="hero-fade">
          <picture v-if="activeMedia" :key="activeSlide.id"><source media="(max-width: 760px)" :srcset="activeMedia.mobileUrl"><img :src="activeMedia.desktopUrl" :alt="activeMedia.altText || activeSlide.title"></picture>
          <div v-else :key="activeSlide.id" class="hero-fallback" :class="`fallback-${activeIndex % 3}`" aria-hidden="true"><i/><i/><i/></div>
        </Transition>
        <router-link v-if="isInternal(activeSlide.href)" class="hero-click-target" :to="activeSlide.href" :aria-label="activeSlideLabel"/>
        <a v-else-if="activeSlide.href.trim()" class="hero-click-target" :href="activeSlide.href" target="_blank" rel="noopener" :aria-label="activeSlideLabel"/>
        <Transition name="hero-copy-fade" mode="out-in"><div v-if="hasSlideCopy" :key="activeSlide.id" class="hero-copy"><small v-if="activeSlide.eyebrow">{{ activeSlide.eyebrow }}</small><h1 v-if="activeSlide.title">{{ activeSlide.title }}</h1><p v-if="activeSlide.summary">{{ activeSlide.summary }}</p><strong v-if="activeSlide.footer">{{ activeSlide.footer }}</strong></div></Transition>
        <div v-if="enabledSlides.length > 1" class="carousel-controls" :class="{ 'is-paused': carouselPaused }"><strong>{{ activeIndex + 1 }}/{{ enabledSlides.length }}</strong><span><button v-for="(_, index) in enabledSlides" :key="`${index}-${carouselCycle}`" :class="{ active: index === activeIndex }" :aria-label="`显示第 ${index + 1} 张`" @click="showSlide(index)"/></span><button class="carousel-pause" :aria-label="carouselPaused ? '继续轮播' : '暂停轮播'" @click="toggleCarousel">{{ carouselPaused ? '▶' : 'Ⅱ' }}</button></div>
        <nav v-if="activeNotices.length" class="hero-notices" aria-label="重要通知"><template v-for="item in activeNotices" :key="item.id"><router-link v-if="isInternal(item.href)" :to="item.href" :data-tone="item.tone">{{ item.label }}</router-link><a v-else :href="item.href" target="_blank" rel="noopener" :data-tone="item.tone">{{ item.label }}</a></template></nav>
        <nav v-else class="hero-notices placeholder-notices" aria-label="通知按钮占位范例"><router-link v-for="index in 3" :key="index" to="/news">资讯通知占位 {{ index }}</router-link></nav>
      </div>
    </section>

    <section id="news" class="news-section">
      <header class="section-heading"><div><small>{{ composition.newsEyebrow || 'NEWS' }}</small><h2>{{ newsHeading }}</h2></div><router-link to="/news">更多 <b>→</b></router-link></header>
      <div v-if="homeNews.length" class="featured-editorial-layout">
        <component :is="isInternal(articleHref(homeNews[0]!, '/news')) ? 'router-link' : 'a'" class="home-editorial-card featured-editorial-main" data-kind="news" :to="isInternal(articleHref(homeNews[0]!, '/news')) ? articleHref(homeNews[0]!, '/news') : undefined" :href="isInternal(articleHref(homeNews[0]!, '/news')) ? undefined : articleHref(homeNews[0]!, '/news')" :target="isInternal(articleHref(homeNews[0]!, '/news')) ? undefined : '_blank'" rel="noopener"><picture v-if="mediaFor(homeNews[0])"><source media="(max-width: 760px)" :srcset="mediaFor(homeNews[0])?.mobileUrl"><img :src="mediaFor(homeNews[0])?.desktopUrl" :alt="homeNews[0]!.title"></picture><img v-else-if="homeNews[0]!.coverUrl" :src="homeNews[0]!.coverUrl" :alt="homeNews[0]!.title"><div v-else class="editorial-placeholder">NEWS</div><span class="home-card-copy"><small>{{ homeNews[0]!.category }} · {{ dateLabel(homeNews[0]!) }}</small><b>{{ homeNews[0]!.title }}</b><p>{{ homeNews[0]!.summary || articleBodyText(homeNews[0]!.body) }}</p></span></component>
        <div v-if="homeNews.length > 1" class="featured-editorial-support"><component :is="isInternal(articleHref(item, '/news')) ? 'router-link' : 'a'" v-for="item in homeNews.slice(1)" :key="item.id" class="home-editorial-card" data-kind="news" :to="isInternal(articleHref(item, '/news')) ? articleHref(item, '/news') : undefined" :href="isInternal(articleHref(item, '/news')) ? undefined : articleHref(item, '/news')" :target="isInternal(articleHref(item, '/news')) ? undefined : '_blank'" rel="noopener"><picture v-if="mediaFor(item)"><source media="(max-width: 760px)" :srcset="mediaFor(item)?.mobileUrl"><img :src="mediaFor(item)?.desktopUrl" :alt="item.title"></picture><img v-else-if="item.coverUrl" :src="item.coverUrl" :alt="item.title"><div v-else class="editorial-placeholder">NEWS</div><span class="home-card-copy"><small>{{ dateLabel(item) }} · {{ item.category }}</small><b>{{ item.title }}</b></span></component></div>
      </div>
      <div v-else class="featured-editorial-layout placeholder-example" aria-label="资讯一主四辅布局占位范例"><div class="home-editorial-card featured-editorial-main placeholder-card"><div class="editorial-placeholder">NEWS 01</div><span class="home-card-copy"><small>分类 · 发布时间</small><b>资讯主标题占位</b><p>发布正式资讯后，此处显示摘要。</p></span></div><div class="featured-editorial-support"><div v-for="index in 4" :key="index" class="home-editorial-card placeholder-card"><div class="editorial-placeholder">NEWS {{ String(index + 1).padStart(2, '0') }}</div><span class="home-card-copy"><small>发布时间 · 分类</small><b>资讯标题占位 {{ index }}</b></span></div></div></div>
    </section>

    <section id="products" class="products-section">
      <header class="products-heading"><div><small>{{ composition.productEyebrow }}</small><h2>{{ productHeading }}</h2></div><router-link class="more-link" to="/products">更多 <b>→</b></router-link></header>
      <div v-if="featuredProduct" class="products-feature">
        <div class="product-category-index"><component :is="isInternal(articleHref(item, '/cards')) ? 'router-link' : 'a'" v-for="item in productCategories" :key="item.category" :to="isInternal(articleHref(item, '/cards')) ? articleHref(item, '/cards') : undefined" :href="isInternal(articleHref(item, '/cards')) ? undefined : articleHref(item, '/cards')" :target="isInternal(articleHref(item, '/cards')) ? undefined : '_blank'" rel="noopener"><span><b>{{ item.category }}</b><small>{{ item.summary || '查看该分类的最新商品' }}</small></span><picture v-if="mediaFor(item)"><img :src="mediaFor(item)?.thumbnailUrl" :alt="item.category"></picture><img v-else-if="item.coverUrl" :src="item.coverUrl" :alt="item.category"><i v-else>12</i></component></div>
        <component :is="isInternal(articleHref(featuredProduct, '/cards')) ? 'router-link' : 'a'" class="featured-product" :to="isInternal(articleHref(featuredProduct, '/cards')) ? articleHref(featuredProduct, '/cards') : undefined" :href="isInternal(articleHref(featuredProduct, '/cards')) ? undefined : articleHref(featuredProduct, '/cards')" :target="isInternal(articleHref(featuredProduct, '/cards')) ? undefined : '_blank'" rel="noopener"><picture v-if="mediaFor(featuredProduct)"><source media="(max-width: 760px)" :srcset="mediaFor(featuredProduct)?.mobileUrl"><img :src="mediaFor(featuredProduct)?.desktopUrl" :alt="featuredProduct.title"></picture><img v-else-if="featuredProduct.coverUrl" :src="featuredProduct.coverUrl" :alt="featuredProduct.title"><div v-else class="product-placeholder"><span>12</span></div><span><small>{{ featuredProduct.category }}</small><b>{{ featuredProduct.title }}</b></span></component>
      </div>
      <div v-if="products.length" class="product-track"><component :is="isInternal(articleHref(item, '/cards')) ? 'router-link' : 'a'" v-for="item in products" :key="item.id" :to="isInternal(articleHref(item, '/cards')) ? articleHref(item, '/cards') : undefined" :href="isInternal(articleHref(item, '/cards')) ? undefined : articleHref(item, '/cards')" :target="isInternal(articleHref(item, '/cards')) ? undefined : '_blank'" rel="noopener"><picture v-if="mediaFor(item)"><img :src="mediaFor(item)?.thumbnailUrl" :alt="item.title"></picture><img v-else-if="item.coverUrl" :src="item.coverUrl" :alt="item.title"><div v-else class="product-placeholder"><span>12</span></div><small>{{ item.category }}</small><h3>{{ item.title }}</h3></component></div>
      <div v-else class="products-placeholder placeholder-example" aria-label="产品上新布局占位范例"><div class="products-feature"><div class="product-category-index"><div v-for="index in 3" :key="index"><span><b>商品分类 {{ index }}</b><small>分类说明占位</small></span><i>12</i></div></div><div class="featured-product placeholder-card"><div class="product-placeholder"><span>12</span></div><span><small>主推商品</small><b>产品上新标题占位</b></span></div></div><div class="product-track"><div v-for="index in 3" :key="index" class="placeholder-product-card"><div class="product-placeholder"><span>12</span></div><small>商品分类</small><h3>商品标题占位 {{ index }}</h3></div></div></div>
    </section>

    <section id="community-videos" class="community-video-section">
      <header class="section-heading inverse"><div><small>{{ videoEyebrow }}</small><h2>{{ videoHeading }}</h2></div><router-link to="/videos">更多 <b>→</b></router-link></header>
      <div v-if="homeVideos.length" class="featured-editorial-layout">
        <component :is="!homeVideos[0]!.link ? 'div' : isInternal(homeVideos[0]!.link) ? 'router-link' : 'a'" class="home-editorial-card featured-editorial-main" data-kind="video" :to="isInternal(homeVideos[0]!.link) ? homeVideos[0]!.link : undefined" :href="homeVideos[0]!.link && !isInternal(homeVideos[0]!.link) ? homeVideos[0]!.link : undefined" :target="homeVideos[0]!.link && !isInternal(homeVideos[0]!.link) ? '_blank' : undefined" rel="noopener"><picture v-if="mediaFor(homeVideos[0])"><source media="(max-width: 760px)" :srcset="mediaFor(homeVideos[0])?.mobileUrl"><img :src="mediaFor(homeVideos[0])?.desktopUrl" :alt="homeVideos[0]!.title"></picture><img v-else-if="homeVideos[0]!.coverUrl" :src="homeVideos[0]!.coverUrl" :alt="homeVideos[0]!.title"><div v-else class="editorial-placeholder">VIDEO</div><span class="home-card-copy"><b>{{ homeVideos[0]!.title }}</b><small class="video-meta"><span v-if="homeVideos[0]!.videoAuthorName">{{ homeVideos[0]!.videoAuthorName }}</span><time>{{ dateLabel(homeVideos[0]!) }}</time></small></span></component>
        <div v-if="homeVideos.length > 1" class="featured-editorial-support"><component :is="!item.link ? 'div' : isInternal(item.link) ? 'router-link' : 'a'" v-for="item in homeVideos.slice(1)" :key="item.id" class="home-editorial-card" data-kind="video" :to="isInternal(item.link) ? item.link : undefined" :href="item.link && !isInternal(item.link) ? item.link : undefined" :target="item.link && !isInternal(item.link) ? '_blank' : undefined" rel="noopener"><picture v-if="mediaFor(item)"><source media="(max-width: 760px)" :srcset="mediaFor(item)?.mobileUrl"><img :src="mediaFor(item)?.desktopUrl" :alt="item.title"></picture><img v-else-if="item.coverUrl" :src="item.coverUrl" :alt="item.title"><div v-else class="editorial-placeholder">VIDEO</div><span class="home-card-copy"><b>{{ item.title }}</b><small class="video-meta"><span v-if="item.videoAuthorName">{{ item.videoAuthorName }}</span><time>{{ dateLabel(item) }}</time></small></span></component></div>
      </div>
      <div v-else class="featured-editorial-layout placeholder-example" aria-label="视频一主四辅布局占位范例"><div class="home-editorial-card featured-editorial-main placeholder-card"><div class="editorial-placeholder">VIDEO 01</div><span class="home-card-copy"><b>视频主标题占位</b><small class="video-meta"><span>作者名</span><time>2026/00/00</time></small></span></div><div class="featured-editorial-support"><div v-for="index in 4" :key="index" class="home-editorial-card placeholder-card"><div class="editorial-placeholder">VIDEO {{ String(index + 1).padStart(2, '0') }}</div><span class="home-card-copy"><b>视频标题占位 {{ index }}</b><small class="video-meta"><span>作者名</span><time>2026/00/00</time></small></span></div></div></div>
    </section>

    <footer class="official-footer"><div class="footer-brand"><img src="/favicon.png" alt="十二军团"><span>LEGION 12</span></div><div><p>{{ legal.trademark }}</p><a v-if="legal.contactHref" :href="legal.contactHref" target="_blank" rel="noopener">{{ legal.contactLabel || '联系我们' }}</a><small>{{ legal.copyright }}<template v-if="legal.registration"> · {{ legal.registration }}</template></small></div></footer>
  </div>
  <div v-else class="official-home-loading" aria-label="正在加载官网内容"><i/><i/><i/></div>
</template>

<style scoped>
.official-home{--paper:#f1eee5;--ink:#101416;--red:#a82632;--gold:#d4b451;min-height:100%;overflow:hidden;background:var(--paper);color:var(--ink);font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.official-topbar{position:sticky;z-index:30;top:0;display:flex;height:78px;align-items:center;justify-content:space-between;padding:0 clamp(22px,5vw,78px);border-bottom:1px solid rgba(16,20,22,.16);background:rgba(247,244,235,.94);backdrop-filter:blur(14px)}.official-wordmark{display:flex;align-items:center;gap:12px;color:var(--ink);text-decoration:none}.official-wordmark img{width:43px;height:43px;object-fit:contain;filter:brightness(.14)}.official-wordmark span{display:flex;flex-direction:column}.official-wordmark b{font-size:17px;letter-spacing:.12em}.official-wordmark small{font:800 8px monospace;letter-spacing:.18em}.official-topbar :deep(.official-nav){height:100%}.hero-section{position:relative;background:#0b0e10}.hero-stage{position:relative;min-height:calc(100vh - 78px);overflow:hidden}.hero-stage>picture,.hero-stage>picture img{position:absolute;inset:0;width:100%;height:100%}.hero-stage>picture img{object-fit:cover}.hero-fallback{position:absolute;inset:0;background:radial-gradient(circle at 72% 38%,rgba(212,180,81,.28),transparent 24%),radial-gradient(circle at 68% 42%,#28333a,transparent 48%),linear-gradient(135deg,#141d22 0 50%,#781e29 50% 68%,#0c1114 68%)}.hero-fallback i{position:absolute;width:36vw;height:36vw;border:1px solid rgba(255,255,255,.16);border-radius:50%;right:8vw;top:10vh}.hero-fallback i:nth-child(2){width:24vw;height:24vw;right:14vw;top:20vh}.hero-fallback i:nth-child(3){width:12vw;height:12vw;right:20vw;top:30vh;background:rgba(218,183,81,.18)}.hero-shade{position:absolute;inset:0;background:linear-gradient(90deg,rgba(5,8,10,.88),rgba(5,8,10,.48) 48%,rgba(5,8,10,.12)),linear-gradient(0deg,rgba(5,8,10,.64),transparent 45%)}.hero-copy{position:relative;z-index:2;display:flex;box-sizing:border-box;min-height:calc(100vh - 78px);max-width:820px;flex-direction:column;align-items:flex-start;justify-content:center;padding:80px clamp(26px,8vw,130px) 150px;color:#fff}.hero-copy>small{padding-left:42px;color:#f0d378;font:900 10px monospace;letter-spacing:.28em}.hero-copy>small::before{content:'';position:absolute;width:30px;height:2px;margin:6px 0 0 -42px;background:var(--red)}.hero-copy h1{max-width:760px;margin:18px 0;font-size:clamp(54px,7.5vw,118px);line-height:.96;letter-spacing:.04em;text-shadow:0 8px 38px #000}.hero-copy p{max-width:610px;margin:0 0 28px;color:#d7d7d1;font-size:clamp(13px,1.3vw,18px);line-height:1.8}.hero-copy>a{padding:14px 20px;border:1px solid rgba(255,255,255,.7);background:rgba(8,12,14,.56);color:#fff;text-decoration:none;font-size:12px;font-weight:900}.hero-copy>a b{margin-left:25px;color:#f2d372}.carousel-controls{position:absolute;z-index:4;right:clamp(26px,6vw,90px);bottom:115px;display:flex;align-items:center;gap:15px}.carousel-controls>button{width:42px;height:42px;border:1px solid rgba(255,255,255,.45);border-radius:50%;background:rgba(8,12,14,.6);color:#fff;font-size:24px}.carousel-controls>span{display:flex;gap:7px}.carousel-controls span button{width:28px;height:3px;padding:0;border:0;background:rgba(255,255,255,.4)}.carousel-controls span button.active{background:#f0cf6d}.hero-notices{position:absolute;z-index:5;right:0;bottom:0;left:0;display:flex;overflow-x:auto;gap:1px;padding:0 clamp(20px,6vw,90px);background:rgba(6,9,11,.78);backdrop-filter:blur(12px)}.hero-notices a{display:flex;min-width:min(310px,72vw);align-items:center;gap:11px;padding:19px 22px;border-left:1px solid rgba(255,255,255,.13);color:#ecece7;text-decoration:none;font-size:11px;font-weight:900}.hero-notices a i{display:grid;width:23px;height:23px;place-items:center;border:1px solid #d5b75c;border-radius:50%;color:#f0ce69;font-style:normal}.hero-notices a b{margin-left:auto}.hero-notices a[data-tone="accent"]{background:#9c2530}.hero-notices a[data-tone="dark"]{background:#101719}.news-section,.products-section{padding:100px clamp(26px,8vw,130px);background:var(--paper)}.section-heading{display:grid;grid-template-columns:minmax(240px,.8fr) minmax(280px,1.2fr) auto;align-items:end;gap:35px;margin-bottom:46px}.section-heading small{color:var(--red);font:900 11px monospace;letter-spacing:.32em}.section-heading h2{margin:7px 0 0;font-size:clamp(38px,5vw,68px);letter-spacing:.06em}.section-heading p{max-width:520px;margin:0;color:#62686a;line-height:1.8}.section-heading>a{padding-bottom:8px;border-bottom:2px solid var(--ink);color:var(--ink);text-decoration:none;font-weight:900}.news-stream{display:grid;grid-template-columns:minmax(420px,1.12fr) minmax(340px,.88fr);gap:clamp(30px,5vw,82px)}.lead-news{display:grid;color:var(--ink);text-decoration:none}.lead-news>picture,.lead-news>picture img,.lead-news>img,.lead-news>.news-placeholder{width:100%;aspect-ratio:16/9}.lead-news img{object-fit:cover}.news-placeholder{display:grid;place-items:center;background:linear-gradient(135deg,#1b2529,#982630);color:#e9cd77;font:900 78px Georgia}.lead-news>div{padding:24px 0}.lead-news small,.product-track small{color:var(--red);font:900 10px monospace;letter-spacing:.1em}.lead-news h3{margin:10px 0;font-size:clamp(24px,3vw,40px)}.lead-news p{display:-webkit-box;overflow:hidden;color:#666d6e;line-height:1.8;-webkit-box-orient:vertical;-webkit-line-clamp:3}.lead-news>div>b{font-size:10px;letter-spacing:.16em}.news-lines{border-top:2px solid var(--ink)}.news-lines a{display:grid;grid-template-columns:94px 90px 1fr 25px;align-items:center;gap:12px;padding:22px 3px;border-bottom:1px solid rgba(16,20,22,.2);color:var(--ink);text-decoration:none}.news-lines time{font:700 10px monospace}.news-lines em{color:var(--red);font-size:9px;font-style:normal;font-weight:900}.news-lines b{font-size:13px;line-height:1.55}.community-video-section{position:relative;padding:105px clamp(26px,8vw,130px) 120px;background:linear-gradient(128deg,#10181b,#1a2428 55%,#701b26);color:#fff}.community-video-section::before{content:'MOVIE';position:absolute;right:-1vw;top:-2vw;color:rgba(255,255,255,.035);font:900 clamp(100px,19vw,310px) Arial}.section-heading.inverse{position:relative;z-index:1}.section-heading.inverse small{color:#f0cd68}.section-heading.inverse p{color:#aeb6b7}.video-ribbon{position:relative;z-index:1;display:grid;grid-template-columns:1.35fr 1fr;grid-template-rows:1fr 1fr;gap:12px}.video-ribbon>a{position:relative;min-height:220px;overflow:hidden;color:#fff;text-decoration:none}.video-ribbon>a.feature{grid-row:1/3;min-height:500px}.video-ribbon picture,.video-ribbon picture img,.video-ribbon>a>img,.video-placeholder{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}.video-placeholder{background:linear-gradient(135deg,#333f43,#111719)}.video-ribbon>a::after{content:'';position:absolute;inset:0;background:linear-gradient(0deg,rgba(4,7,8,.88),transparent 62%)}.video-ribbon>a>i{position:absolute;z-index:2;left:25px;top:25px;display:grid;width:52px;height:52px;place-items:center;border:1px solid rgba(255,255,255,.75);border-radius:50%;background:rgba(4,8,10,.42);font-style:normal}.video-ribbon>a>span{position:absolute;z-index:2;right:24px;bottom:24px;left:24px;display:flex;flex-direction:column;gap:6px}.video-ribbon small{color:#e8c85f;font:900 9px monospace}.video-ribbon b{font-size:clamp(17px,2vw,27px)}.video-ribbon em{display:-webkit-box;overflow:hidden;color:#c2c8c7;font-size:10px;font-style:normal;line-height:1.6;-webkit-box-orient:vertical;-webkit-line-clamp:2}.products-section{background:#e6e0d2}.product-track{display:flex;overflow-x:auto;gap:24px;padding:2px 0 24px;scroll-snap-type:x mandatory}.product-track>a{flex:0 0 min(330px,78vw);color:var(--ink);text-decoration:none;scroll-snap-align:start}.product-track picture,.product-track picture img,.product-track>a>img,.product-placeholder{width:100%;aspect-ratio:1/1;object-fit:cover}.product-placeholder{display:grid;place-items:center;background:linear-gradient(145deg,#c6b99a,#7c222b);color:#f1d27a}.product-placeholder span{font:900 92px Georgia}.product-track small{display:block;margin-top:18px}.product-track h3{margin:8px 0;font-size:22px}.product-track p{min-height:44px;color:#676c6d;font-size:11px;line-height:1.7}.product-track b{font-size:10px}.section-empty{display:flex;min-height:260px;flex-direction:column;align-items:center;justify-content:center;border-top:2px solid var(--ink);border-bottom:1px solid rgba(16,20,22,.25);color:#787d7d}.section-empty b{color:var(--ink);font:900 18px monospace;letter-spacing:.2em}.section-empty span{margin-top:10px}.section-empty.inverse{border-color:rgba(255,255,255,.4);color:#93a0a2}.section-empty.inverse b{color:#fff}.official-footer{display:flex;align-items:flex-start;justify-content:space-between;gap:45px;padding:55px clamp(26px,8vw,130px);background:#080c0e;color:#a0a8a8}.official-footer>a{display:flex;align-items:center;gap:13px;color:#fff;text-decoration:none;font:900 13px monospace;letter-spacing:.16em}.official-footer img{width:48px;height:48px;filter:brightness(0) invert(1)}.official-footer>div{max-width:720px;text-align:right}.official-footer p{margin:0 0 12px;font-size:10px;line-height:1.7}.official-footer a{color:#e3c46a}.official-footer small{display:block;margin-top:10px;color:#687375}.official-home-loading{display:grid;min-height:100%;place-content:center;gap:12px;background:#0a0f12}.official-home-loading i{width:min(70vw,720px);height:18px;background:linear-gradient(90deg,#111820,#1c282f,#111820);background-size:200% 100%;animation:home-loading 1.2s linear infinite}.official-home-loading i:nth-child(2){width:min(52vw,520px)}.official-home-loading i:nth-child(3){width:min(34vw,340px)}@keyframes home-loading{to{background-position:-200% 0}}@media(max-width:1000px){.official-wordmark span{display:none}.news-stream{grid-template-columns:1fr}.section-heading{grid-template-columns:1fr auto}.section-heading p{grid-column:1/-1}.video-ribbon{grid-template-columns:1fr 1fr;grid-template-rows:1.3fr 1fr}.video-ribbon>a.feature{grid-column:1/-1;grid-row:auto;min-height:430px}}@media(max-width:760px){.official-topbar{height:auto;min-height:58px;align-items:flex-start;padding:7px 12px}.official-wordmark{padding-top:4px}.official-wordmark img{width:34px;height:34px}.official-topbar :deep(.official-nav){max-width:calc(100vw - 60px)}.hero-stage,.hero-copy{min-height:calc(100svh - 58px)}.hero-stage>picture img{object-position:center}.hero-shade{background:linear-gradient(0deg,rgba(5,8,10,.9),rgba(5,8,10,.18) 80%)}.hero-copy{justify-content:flex-end;padding:80px 22px 165px}.hero-copy h1{font-size:48px}.hero-copy p{font-size:12px}.carousel-controls{right:18px;bottom:116px}.carousel-controls>span{display:none}.hero-notices{padding:0 12px}.hero-notices a{min-width:75vw;padding:16px}.news-section,.products-section,.community-video-section{padding:70px 18px}.section-heading{grid-template-columns:1fr;gap:13px;margin-bottom:28px}.section-heading p{grid-column:auto}.section-heading>a{justify-self:start}.section-heading h2{font-size:40px}.news-lines a{grid-template-columns:75px 70px 1fr}.news-lines a span{display:none}.video-ribbon{display:flex;overflow-x:auto}.video-ribbon>a,.video-ribbon>a.feature{flex:0 0 82vw;min-height:360px}.official-footer{align-items:flex-start;flex-direction:column;padding:45px 22px}.official-footer>div{text-align:left}}
.official-home{--paper:#0a1014;--surface:#0e171d;--surface-2:#121e25;--ink:#eef1ed;--muted:#89969c;--line:#293941;--red:#a82632;--gold:#d4b451;background:var(--paper);color:var(--ink)}
.official-topbar{border-color:var(--line);background:rgba(8,14,18,.94)}
.official-wordmark{color:var(--ink)}
.official-wordmark img{filter:brightness(0) invert(1)}
.hero-click-target{position:absolute;z-index:2;inset:0;cursor:pointer}
.hero-copy{z-index:3;min-height:0;position:absolute;right:auto;bottom:118px;left:clamp(26px,8vw,130px);max-width:min(620px,70vw);padding:20px 24px;border-left:3px solid var(--gold);background:linear-gradient(90deg,rgba(7,12,15,.88),rgba(7,12,15,.16));pointer-events:none}
.hero-copy>small{padding-left:0;color:var(--gold)}
.hero-copy>small::before{display:none}
.hero-copy h1{margin:8px 0 0;font-size:clamp(26px,4vw,58px);line-height:1.08}
.hero-copy p{margin:12px 0 0;color:#d3d9d7;font-size:clamp(12px,1.1vw,16px)}
.hero-copy>small,.hero-copy h1,.hero-copy p,.hero-copy strong{white-space:pre;overflow-wrap:normal;word-break:normal}
.news-section,.products-section{background:var(--paper)}
.products-section{background:var(--surface)}
.section-heading p,.lead-news p,.product-track p{color:var(--muted)}
.section-heading>a{border-color:var(--gold);color:var(--gold)}
.lead-news,.news-lines a,.product-track>a{color:var(--ink)}
.news-lines{border-color:var(--gold)}
.news-lines a{border-color:var(--line)}
.community-video-section{background:linear-gradient(128deg,#0b1216,#111d23 58%,#35131a)}
.product-track picture,.product-track picture img,.product-track>a>img,.product-placeholder{aspect-ratio:4/3}
.product-placeholder{background:linear-gradient(145deg,#18262d,#591923)}
.section-empty{border-color:var(--line);color:var(--muted)}
.section-empty b{color:var(--ink)}
.official-footer{border-top:1px solid var(--line);background:#070b0e}
@media(max-width:760px){.hero-copy{right:18px;bottom:108px;left:18px;max-width:none;padding:14px 16px}.hero-copy h1{font-size:32px}.product-track picture,.product-track picture img,.product-track>a>img,.product-placeholder{aspect-ratio:4/3}}
.hero-stage{min-height:0;aspect-ratio:2460/1440}
.hero-copy{min-height:0}
.news-stream{grid-template-columns:minmax(360px,1.05fr) minmax(0,1.5fr);gap:28px}
.news-lines{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:26px;border-top:0}
.news-lines a{display:block;padding:0;border:0}
.news-lines a>picture,.news-lines a>img,.news-tile-placeholder{display:block;width:100%;aspect-ratio:16/9;object-fit:cover}
.news-tile-placeholder{display:grid;place-items:center;background:linear-gradient(135deg,#152229,#4a1720);color:var(--gold);font:900 36px Georgia}
.news-lines a>span{display:grid;grid-template-columns:auto 1fr;gap:8px 12px;padding:12px 0;border-bottom:1px solid var(--line)}
.news-lines a b{grid-column:1/-1;font-size:13px}
.products-heading{display:flex;align-items:flex-end;justify-content:space-between;gap:28px;margin-bottom:36px}
.products-heading small{color:var(--gold);font:900 11px monospace;letter-spacing:.32em}
.products-heading h2{margin:8px 0 10px;font-size:clamp(38px,5vw,68px);line-height:1;letter-spacing:.06em}
.products-heading p{max-width:620px;color:var(--muted)}
.more-link{flex:none;padding-bottom:8px;border-bottom:2px solid var(--gold);color:var(--gold);font-weight:900;text-decoration:none}
.products-feature{display:grid;grid-template-columns:minmax(320px,.8fr) minmax(420px,1.2fr);gap:32px;margin-bottom:58px}
.product-category-index{display:flex;flex-direction:column;border-top:1px solid var(--line)}
.product-category-index>a{display:grid;grid-template-columns:1fr 76px;align-items:center;gap:20px;min-height:96px;padding:12px 0;border-bottom:1px solid var(--line);color:var(--ink);text-decoration:none}
.product-category-index span{display:flex;flex-direction:column;gap:7px}.product-category-index b{font-size:20px}.product-category-index small{color:var(--muted)}
.product-category-index picture,.product-category-index img,.product-category-index i{width:76px;height:auto;aspect-ratio:4/3;object-fit:cover}
.product-category-index i{display:grid;place-items:center;background:var(--surface-2);color:var(--gold);font-style:normal}
.featured-product{position:relative;display:block;overflow:hidden;min-height:0;aspect-ratio:4/3;color:#fff;text-decoration:none}
.featured-product>picture,.featured-product>picture img,.featured-product>img,.featured-product>.product-placeholder{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
.featured-product::after{content:'';position:absolute;inset:0;background:linear-gradient(0deg,rgba(4,8,10,.88),transparent 58%)}
.featured-product>span{position:absolute;z-index:2;right:26px;bottom:24px;left:26px;display:flex;flex-direction:column;gap:8px}
.featured-product>span small{color:var(--gold)}.featured-product>span b{font-size:clamp(22px,3vw,38px)}
.product-track{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));overflow:visible;gap:24px}
.product-track>a{min-width:0}.product-track h3{font-size:17px}
.video-editorial{display:grid;grid-template-columns:minmax(320px,1.05fr) minmax(0,1.5fr);gap:28px}.video-editorial>*{min-width:0}
.lead-video,.video-tiles>*{position:relative;display:block;overflow:hidden;aspect-ratio:16/9;color:#fff;text-decoration:none}
.lead-video{min-height:0}.video-tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr));align-content:start;gap:26px}.video-tiles>*{min-width:0;min-height:0}
.lead-video>picture,.lead-video>picture img,.lead-video>img,.lead-video>.video-placeholder,.video-tiles picture,.video-tiles picture img,.video-tiles>*>img,.video-tiles .video-placeholder{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
.lead-video::after,.video-tiles>*::after{content:'';position:absolute;inset:0;background:linear-gradient(0deg,rgba(4,8,10,.9),transparent 62%)}
.lead-video>i,.video-tiles i{position:absolute;z-index:2;top:18px;left:18px;display:grid;width:42px;height:42px;place-items:center;border:1px solid var(--gold);border-radius:50%;background:rgba(5,9,11,.7);color:var(--gold);font-style:normal}
.lead-video>span,.video-tiles>*>span{position:absolute;z-index:2;right:20px;bottom:18px;left:20px;display:flex;min-width:0;flex-direction:column;gap:6px}.lead-video small,.video-tiles small,.lead-video em{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.lead-video small,.video-tiles small{color:var(--gold)}.lead-video b,.video-tiles b{display:-webkit-box;overflow:hidden;overflow-wrap:anywhere;-webkit-box-orient:vertical;-webkit-line-clamp:2}.lead-video b{font-size:clamp(22px,3vw,38px)}.lead-video em{color:#b8c1c3;font-style:normal}
@media(max-width:1000px){.news-stream,.products-feature,.video-editorial{grid-template-columns:1fr}.product-track{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:760px){.hero-stage{min-height:0;aspect-ratio:1080/1440}.news-lines,.video-tiles,.product-track{grid-template-columns:1fr}.lead-video,.video-tiles>*{min-height:0;aspect-ratio:16/9}.products-heading{align-items:flex-start;flex-direction:column}.products-heading h2{font-size:40px}.featured-product{min-height:0;aspect-ratio:4/3}}
.community-video-section::before{content:'VIDEO'}
.hero-stage[data-fallback-index="1"] .hero-fallback{background:radial-gradient(circle at 30% 45%,rgba(212,180,81,.24),transparent 24%),linear-gradient(145deg,#10191e 0 52%,#3e171d 52% 70%,#080d10 70%)}
.hero-stage[data-fallback-index="2"] .hero-fallback{background:radial-gradient(circle at 68% 32%,rgba(168,38,50,.3),transparent 27%),linear-gradient(125deg,#080d10 0 42%,#24323a 42% 67%,#151015 67%)}
.placeholder-example{opacity:.82}.placeholder-card,.placeholder-news-tile,.placeholder-product-card,.placeholder-video-tile{pointer-events:none}
.placeholder-image{display:grid;width:100%;aspect-ratio:16/9;place-items:center;background:linear-gradient(135deg,#142129,#3f1720);color:var(--gold);font:900 18px monospace;letter-spacing:.16em}
.lead-news>.placeholder-image{padding:0}
.placeholder-news-tile{display:block}.placeholder-news-tile>span{display:grid;grid-template-columns:auto 1fr;gap:8px 12px;padding:12px 0;border-bottom:1px solid var(--line)}.placeholder-news-tile b{grid-column:1/-1}
.product-category-index>div{display:grid;grid-template-columns:1fr 76px;align-items:center;gap:20px;min-height:96px;padding:12px 0;border-bottom:1px solid var(--line)}
.placeholder-product-card{color:var(--ink)}.placeholder-product-card small{display:block;margin-top:14px;color:var(--gold)}.placeholder-product-card h3{margin:7px 0;font-size:17px}
.placeholder-video-tile{position:relative;min-width:0;min-height:0;overflow:hidden;aspect-ratio:16/9}.placeholder-video-tile::after{content:'';position:absolute;inset:0;background:linear-gradient(0deg,rgba(4,8,10,.9),transparent 62%)}.placeholder-video-tile>i{position:absolute;z-index:2;top:18px;left:18px;display:grid;width:42px;height:42px;place-items:center;border:1px solid var(--gold);border-radius:50%;color:var(--gold);font-style:normal}.placeholder-video-tile>span{position:absolute;z-index:2;right:20px;bottom:18px;left:20px;display:flex;min-width:0;flex-direction:column;gap:6px}.placeholder-video-tile small{color:var(--gold)}
.news-stream>*{min-width:0}.lead-news,.news-lines,.news-lines a,.placeholder-news-tile{min-width:0;overflow:hidden}.lead-news img,.news-lines img{display:block;max-width:100%;object-fit:cover}
.hero-notices{right:auto;bottom:24px;left:50%;width:min(560px,68vw);flex-direction:column-reverse;gap:8px;padding:0;background:transparent;transform:translateX(-50%);backdrop-filter:none}
.hero-notices a{box-sizing:border-box;width:100%;min-width:0;justify-content:center;padding:13px 24px;border:1px solid rgba(212,180,81,.5);border-radius:999px;background:rgba(9,14,18,.9);text-align:center;box-shadow:0 7px 22px rgba(0,0,0,.28)}
.hero-notices a[data-tone="light"]{background:rgba(238,241,237,.94);color:#11171a}.hero-notices a[data-tone="accent"]{background:rgba(156,37,48,.94);color:#fff}.hero-notices a[data-tone="dark"]{background:rgba(9,14,18,.94);color:#fff}.placeholder-notices{opacity:.7}
.carousel-controls{right:auto;bottom:28px;left:clamp(26px,5vw,78px);gap:12px;color:#fff}.carousel-controls strong{min-width:44px;font-size:21px;line-height:1}.carousel-controls>span{display:flex;gap:7px}.carousel-controls span button{width:46px;height:7px;background:rgba(255,255,255,.35)}.carousel-controls span button.active{background:#fff}.carousel-controls>.carousel-pause{width:26px;height:26px;border:1px solid rgba(255,255,255,.75);font-size:11px}
.hero-copy{top:17%;bottom:auto;transform:none}
@media(max-width:760px){.hero-notices{bottom:18px;width:min(520px,82vw)}.hero-notices a{padding:10px 16px}.carousel-controls{bottom:184px;left:18px}.carousel-controls span button{width:28px}.carousel-controls strong{font-size:17px}.hero-copy{top:12%;bottom:auto}}
.hero-copy{top:11%;box-sizing:border-box;width:min(560px,52%);max-width:none;padding:0;border:0;background:none;text-shadow:0 2px 5px rgba(0,0,0,.72)}.hero-copy>small{font-size:clamp(12px,1.25vw,18px);letter-spacing:.06em}.hero-copy h1{margin:5px 0 0;font-size:clamp(28px,3.2vw,52px);line-height:1.12}.hero-copy p{margin:13px 0 0;color:#fff;font-size:clamp(13px,1.3vw,19px);font-weight:700}.hero-copy strong{display:block;margin-top:22px;color:#fff;font-size:clamp(16px,1.8vw,28px);line-height:1.25}
.hero-notices{width:min(280px,34vw);max-height:34%;overflow-y:auto;overscroll-behavior:contain}.hero-notices a{flex:none}
@media(max-width:760px){.hero-copy{top:8%;right:18px;left:18px;width:auto}.hero-copy h1{font-size:30px}.hero-copy strong{margin-top:14px;font-size:17px}.hero-notices{width:min(320px,68vw);max-height:34%}}
.news-section{position:relative;isolation:isolate}
.news-section::before{content:'NEWS';position:absolute;z-index:0;right:-1vw;top:-2vw;color:rgba(255,255,255,.035);font:900 clamp(100px,19vw,310px) Arial,sans-serif;pointer-events:none;user-select:none}
.news-section>.section-heading,.news-section>.news-stream{position:relative;z-index:1}
@media(max-width:760px){.news-section::before{right:-1vw;top:-2vw;font-size:clamp(100px,19vw,310px)}}
.section-heading{grid-template-columns:minmax(0,1fr) auto}
.hero-fallback.fallback-1{background:radial-gradient(circle at 30% 45%,rgba(212,180,81,.24),transparent 24%),linear-gradient(145deg,#10191e 0 52%,#3e171d 52% 70%,#080d10 70%)}
.hero-fallback.fallback-2{background:radial-gradient(circle at 68% 32%,rgba(168,38,50,.3),transparent 27%),linear-gradient(125deg,#080d10 0 42%,#24323a 42% 67%,#151015 67%)}
.hero-fade-enter-active,.hero-fade-leave-active{transition:opacity .9s cubic-bezier(.22,.61,.36,1),transform 1.15s cubic-bezier(.22,.61,.36,1)}
.hero-fade-enter-from{opacity:0;transform:scale(1.018)}
.hero-fade-leave-to{opacity:0;transform:scale(1.006)}
.hero-copy-fade-enter-active,.hero-copy-fade-leave-active{transition:opacity .28s ease,transform .38s ease}
.hero-copy-fade-enter-from,.hero-copy-fade-leave-to{opacity:0;transform:translateY(8px)}
.carousel-controls span button{position:relative;overflow:hidden;background:rgba(255,255,255,.34)}
.carousel-controls span button.active{background:rgba(255,255,255,.34)}
.carousel-controls span button.active::after{content:'';position:absolute;inset:0 auto 0 0;width:100%;background:#fff;transform-origin:left center;animation:hero-carousel-progress 6.5s linear forwards}
.carousel-controls.is-paused span button.active::after{animation-play-state:paused}
@keyframes hero-carousel-progress{from{transform:scaleX(0)}to{transform:scaleX(1)}}
@media(prefers-reduced-motion:reduce){.hero-fade-enter-active,.hero-fade-leave-active,.hero-copy-fade-enter-active,.hero-copy-fade-leave-active{transition:none}.carousel-controls span button.active::after{animation:none;transform:scaleX(1)}}
.hero-copy{top:auto;bottom:96px;left:clamp(26px,5vw,78px);width:min(390px,32vw)}
@media(max-width:760px){.hero-copy{top:auto;right:18px;bottom:230px;left:18px;width:auto}}
.official-footer>.footer-brand{display:flex;align-items:center;gap:13px;color:#fff;font:900 13px monospace;letter-spacing:.16em}
.featured-editorial-layout{display:grid;grid-template-columns:minmax(340px,1.05fr) minmax(0,1.5fr);align-items:start;gap:clamp(28px,4vw,58px)}
.featured-editorial-layout>*{min-width:0}
.featured-editorial-layout>.featured-editorial-main:only-child{grid-column:1/-1}
.featured-editorial-support{display:grid;min-width:0;grid-template-columns:repeat(2,minmax(0,1fr));align-items:start;column-gap:24px;row-gap:30px}
.featured-editorial-support>.home-editorial-card:only-child{grid-column:1/-1}
.home-editorial-card{display:flex;min-width:0;flex-direction:column;border:0;background:transparent;box-shadow:none;color:var(--ink);text-decoration:none}
.home-editorial-card>picture,.home-editorial-card>img,.editorial-placeholder{display:block;width:100%;overflow:hidden;aspect-ratio:16/9}
.home-editorial-card>picture img,.home-editorial-card>img{display:block;width:100%;height:100%;object-fit:cover;transition:filter .2s ease,transform .25s ease}
.editorial-placeholder{display:grid;place-items:center;background:linear-gradient(135deg,#142129,#431820);color:var(--gold);font:900 20px monospace;letter-spacing:.12em}
.home-card-copy{display:flex;min-width:0;flex-direction:column;gap:8px;padding:14px 0 16px;border-bottom:1px solid color-mix(in srgb,var(--line) 78%,transparent)}
.featured-editorial-main .home-card-copy{padding:18px 0 20px}
.home-card-copy small{overflow:hidden;color:var(--gold);font-size:12px;text-overflow:ellipsis;white-space:nowrap}
.home-card-copy b{display:-webkit-box;overflow:hidden;overflow-wrap:anywhere;font-size:17px;line-height:1.45;transition:color .2s ease;-webkit-box-orient:vertical;-webkit-line-clamp:2}
.featured-editorial-main .home-card-copy b{font-size:clamp(20px,2.2vw,30px)}
.home-card-copy p{display:-webkit-box;overflow:hidden;margin:0;color:var(--muted);font-size:13px;line-height:1.6;-webkit-box-orient:vertical;-webkit-line-clamp:2}
.home-editorial-card[data-kind="video"] .home-card-copy{padding-bottom:16px}
.home-editorial-card[data-kind="video"] .home-card-copy small{color:#b8c1c3}
.home-editorial-card:hover>picture img,.home-editorial-card:hover>img{filter:brightness(1.08);transform:scale(1.012)}
.home-editorial-card:hover .home-card-copy b{color:var(--gold)}
@media(max-width:1100px){.featured-editorial-layout{grid-template-columns:1fr}.featured-editorial-support{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:650px){.featured-editorial-layout,.featured-editorial-support{grid-template-columns:1fr;gap:22px}.featured-editorial-support{row-gap:22px}.home-card-copy,.featured-editorial-main .home-card-copy{padding-right:0;padding-left:0}}
.hero-notices{right:clamp(26px,5vw,78px);left:auto;transform:none}
.hero-notices a{border-radius:0}
.hero-notices a{background:rgba(9,14,18,.4)}
.hero-notices a[data-tone="light"]{background:rgba(238,241,237,.4)}
.hero-notices a[data-tone="accent"]{background:rgba(156,37,48,.4)}
.hero-notices a[data-tone="dark"]{background:rgba(9,14,18,.4)}
@media(max-width:760px){.hero-notices{right:18px;left:auto}}
.hero-copy,.hero-copy h1,.hero-copy p,.hero-copy strong{ text-shadow:0 2px 5px rgba(0,0,0,.68) }
.home-editorial-card[data-kind="video"] .video-meta{display:flex;align-items:center;gap:12px}
.home-editorial-card[data-kind="video"] .video-meta time{color:#879296;font:inherit}
</style>
