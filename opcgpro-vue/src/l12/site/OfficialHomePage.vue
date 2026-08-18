<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getPublicContent } from '@/l12/platform'

const headline = ref('十二军团')
const introduction = ref('集结、构筑、开战')
const latestNews = ref('')
onMounted(async () => {
  const entries = await Promise.allSettled(['home.headline', 'home.introduction', 'home.latestNews'].map(getPublicContent))
  const values = entries.map(entry => entry.status === 'fulfilled' ? entry.value.value.trim() : '')
  if (values[0]) headline.value = values[0]
  if (values[1]) introduction.value = values[1]
  if (values[2]) latestNews.value = values[2]
})
const modules = [
  { to: '/lobby', en: 'PLAY', title: '在线对战', text: '公开匹配、好友房与单人测试沙盒。' },
  { to: '/cards', en: 'DATABASE', title: '卡牌资料库', text: '按赛季、阵营、类型、费用与天灾等级检索。' },
  { to: '/decks', en: 'DECKS', title: '牌库与分享', text: '构筑、校验、牌库广场、牌库码与牌库图。' },
  { to: '/records', en: 'REPLAY', title: '对局与回放', text: '完整操作记录、JSON 导入导出与只读棋盘回放。' },
]
</script>

<template>
  <div class="official-home page-frame">
    <section class="official-hero">
      <div class="hero-copy">
        <p>LEGION 12 · OFFICIAL WEB PLATFORM</p>
        <h1>{{ headline }}<br/><span>{{ introduction }}</span></h1>
        <div class="hero-actions"><router-link to="/lobby">进入对战大厅</router-link><router-link class="secondary" to="/cards">浏览卡牌图鉴</router-link></div>
        <ul><li>官方网站</li><li>规则与资料库</li><li>在线对战器</li></ul>
      </div>
      <div class="hero-art"><img src="/assets/l12/card-back-gold.png" alt="十二军团官方卡背"/><i/><i/></div>
    </section>

    <section class="module-grid">
      <router-link v-for="(item,index) in modules" :key="item.to" :to="item.to">
        <small>0{{ index + 1 }} · {{ item.en }}</small><h2>{{ item.title }}</h2><p>{{ item.text }}</p><b>进入 →</b>
      </router-link>
    </section>

    <section class="official-columns">
      <article><header><small>OFFICIAL</small><h2>官方资讯</h2></header><div class="empty-block"><template v-if="latestNews"><b>最新公告</b><span>{{ latestNews }}</span></template><template v-else><b>暂无正式资讯</b><span>管理员可在后台发布公告、赛季更新、勘误与赛事信息。</span></template></div></article>
      <article><header><small>RULES & FAQ</small><h2>规则资料</h2></header><router-link to="/cards">卡牌图鉴与原文效果 <b>→</b></router-link><a href="#" @click.prevent>规则书与 FAQ 整理中 <b>→</b></a><router-link to="/records">对局复盘工具 <b>→</b></router-link></article>
      <article><header><small>DEVELOPMENT</small><h2>开发状态</h2></header><div class="status-line"><span>对战框架</span><b>可测试</b></div><div class="status-line"><span>S1 卡效</span><b>回归中</b></div><div class="status-line"><span>S2 卡效</span><b>接入中</b></div><div class="status-line"><span>移动端</span><b>适配中</b></div></article>
    </section>
  </div>
</template>

<style scoped>
.page-frame{min-height:100%;padding:0 clamp(20px,4vw,64px) 54px}.official-home{background:linear-gradient(145deg,rgba(103,18,29,.08),transparent 42%)}.official-hero{position:relative;min-height:470px;display:grid;grid-template-columns:minmax(0,1.2fr) minmax(320px,.8fr);align-items:center;overflow:hidden;border:1px solid rgba(235,230,216,.2);background:radial-gradient(circle at 78% 45%,rgba(39,148,156,.17),transparent 28%),radial-gradient(circle at 24% 80%,rgba(139,24,37,.22),transparent 38%),#0b1115}.official-hero::before{content:'';position:absolute;inset:0;background:linear-gradient(rgba(255,255,255,.022) 1px,transparent 1px),linear-gradient(90deg,rgba(255,255,255,.022) 1px,transparent 1px);background-size:36px 36px}.hero-copy{position:relative;z-index:2;padding:56px clamp(28px,6vw,86px)}.hero-copy>p{color:#5ac6cd;font:900 10px monospace;letter-spacing:.22em}.hero-copy h1{margin:12px 0 26px;font-size:clamp(46px,6vw,82px);line-height:1.02;letter-spacing:.04em}.hero-copy h1 span{color:#e3c276;font-size:.62em}.hero-actions{display:flex;gap:10px}.hero-actions a{padding:13px 20px;border:1px solid #e2c679;background:#e2c679;color:#0b0e10;font-size:13px;font-weight:900;text-decoration:none}.hero-actions a.secondary{border-color:#637078;background:#111920;color:#f2eee4}.hero-copy ul{display:flex;gap:24px;margin:28px 0 0;padding:0;color:#818c91;font-size:11px;font-weight:900;list-style:none}.hero-copy li::before{content:'◆';margin-right:8px;color:#9f2532;font-size:8px}.hero-art{position:relative;z-index:1;display:grid;place-items:center;height:100%}.hero-art img{position:relative;z-index:2;width:min(290px,72%);max-height:400px;object-fit:cover;border:1px solid rgba(255,255,255,.48);box-shadow:18px 20px 0 rgba(117,20,31,.55),-16px -14px 0 rgba(24,111,119,.36),0 35px 80px #000}.hero-art i{position:absolute;width:260px;height:260px;border:1px solid rgba(226,194,118,.35);transform:rotate(45deg)}.hero-art i:last-child{width:340px;height:340px;border-color:rgba(70,189,198,.18)}.module-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-top:14px}.module-grid a{min-height:178px;padding:22px;border:1px solid rgba(235,230,216,.16);background:#101820;color:#eee9df;text-decoration:none;transition:.18s}.module-grid a:hover{transform:translateY(-4px);border-color:#4dbbc3;background:#14232a}.module-grid small{color:#5ac6cd;font:900 9px monospace;letter-spacing:.16em}.module-grid h2{margin:13px 0 7px;font-size:21px}.module-grid p{min-height:42px;margin:0;color:#7d898f;font-size:11px;line-height:1.7}.module-grid b{display:block;margin-top:17px;color:#e3c276;font-size:11px}.official-columns{display:grid;grid-template-columns:1.4fr 1fr 1fr;gap:12px;margin-top:14px}.official-columns>article{min-height:230px;padding:22px;border:1px solid rgba(235,230,216,.16);background:#0d141b}.official-columns header{padding-bottom:13px;border-bottom:1px solid rgba(235,230,216,.1)}.official-columns header small{color:#a52d39;font:900 9px monospace;letter-spacing:.18em}.official-columns h2{margin:4px 0 0;font-size:19px}.empty-block{display:flex;height:130px;flex-direction:column;align-items:center;justify-content:center;border:1px dashed #354149;color:#65727a;text-align:center}.empty-block b{color:#90999e;font-size:13px}.empty-block span{margin-top:6px;font-size:10px}.official-columns a,.status-line{display:flex;align-items:center;justify-content:space-between;padding:13px 3px;border-bottom:1px solid rgba(235,230,216,.1);color:#aab2b5;font-size:11px;text-decoration:none}.official-columns a:hover{color:#fff}.status-line b{color:#e1c06f;font-size:10px}
@media(max-width:1050px){.official-hero{grid-template-columns:1fr .65fr}.module-grid{grid-template-columns:1fr 1fr}.official-columns{grid-template-columns:1fr 1fr}.official-columns>article:first-child{grid-column:1/-1}}
@media(max-width:700px){.page-frame{padding:0 12px 40px}.official-hero{min-height:0;grid-template-columns:1fr}.hero-copy{padding:38px 24px}.hero-copy h1{font-size:44px}.hero-art{display:none}.hero-copy ul{flex-wrap:wrap;gap:10px 18px}.hero-actions{flex-direction:column}.hero-actions a{text-align:center}.module-grid,.official-columns{grid-template-columns:1fr}.official-columns>article:first-child{grid-column:auto}.module-grid a{min-height:150px}}
</style>
