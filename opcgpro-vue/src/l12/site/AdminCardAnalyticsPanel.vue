<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import {
  adminApi,
  type AdminCardAnalyticsDetail,
  type AdminCardAnalyticsItem,
  type AdminCardAnalyticsPage,
} from '@/l12/platform'

const emit = defineEmits<{ notice: [message: string]; openMatch: [matchId: string] }>()
const page = ref<AdminCardAnalyticsPage>({ items: [], total: 0 })
const detail = ref<AdminCardAnalyticsDetail | null>(null)
const cards = ref<DeckCard[]>([])
const loading = ref(false)
const detailLoading = ref(false)
const filters = ref({ search: '', mode: 'ranked', from: '', to: '', masterId: '', minimumSample: 10 })

const cardById = computed(() => new Map(cards.value.map(card => [card.id, card])))
const selectedCatalogCard = computed(() => detail.value ? cardById.value.get(detail.value.summary.cardId) : undefined)
const summaryMetrics = computed(() => page.value.summary || {})

function percent(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
  return `${(value * 100).toFixed(1)}%`
}
function signedPercent(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
  return `${value >= 0 ? '+' : ''}${(value * 100).toFixed(1)}%`
}
function ratio(value: number, total: number) { return total > 0 ? Math.min(1, Math.max(0, value / total)) : 0 }
function coverageLabel(value: AdminCardAnalyticsItem['coverage'] | AdminCardAnalyticsDetail['coverage']) {
  if (!value) return '暂无覆盖说明'
  if (value.inferredFacts || value.inferredDeckSnapshots) return '含历史推断数据'
  if (value.partialFacts) return '含命令边界推断'
  return '结构化精确事实'
}
function modeLabel(mode: string) {
  return ({ ranked: '排位', casual: '休闲', friendly: '好友房', tournament: '赛事' } as Record<string, string>)[mode] || mode
}
function dateLabel(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}
function resultText(item: AdminCardAnalyticsItem) {
  if (item.sampleSize < filters.value.minimumSample) return '样本不足'
  if (typeof item.winRateDelta !== 'number') return '缺少同条件对照'
  if (item.winRateDelta >= .05) return '正向关联'
  if (item.winRateDelta <= -.05) return '负向关联'
  return '接近对照基线'
}
function resultTone(item: AdminCardAnalyticsItem) {
  if (item.sampleSize < filters.value.minimumSample || typeof item.winRateDelta !== 'number') return 'neutral'
  return item.winRateDelta >= .05 ? 'positive' : item.winRateDelta <= -.05 ? 'negative' : 'neutral'
}
async function loadAnalytics(reset = true) {
  loading.value = true
  try {
    const cursor = reset ? undefined : page.value.nextCursor || undefined
    const next = await adminApi.cardAnalytics({ ...filters.value, cursor, limit: 50 })
    page.value = reset ? next : { ...next, items: [...page.value.items, ...next.items] }
    if (reset) {
      if (next.items[0]) await selectCard(next.items[0].cardId)
      else detail.value = null
    }
  } catch (error) { emit('notice', error instanceof Error ? error.message : '单卡分析加载失败') }
  finally { loading.value = false }
}
async function selectCard(cardId: string) {
  detailLoading.value = true
  try { detail.value = await adminApi.cardAnalyticsDetail(cardId, filters.value) }
  catch (error) { emit('notice', error instanceof Error ? error.message : '单卡分析详情加载失败') }
  finally { detailLoading.value = false }
}

onMounted(async () => {
  try { cards.value = await loadDeckCatalog() }
  catch (error) { emit('notice', error instanceof Error ? error.message : '卡牌档案加载失败') }
  await loadAnalytics(true)
})
</script>

<template>
  <section class="card-analytics">
    <header class="module-header">
      <div><small>CARD IMPACT ANALYTICS</small><h2>单卡影响分析</h2><p>分析构筑收录、抽到、打出、发动与结算后的胜负关联；不把相关性描述成因果。</p></div>
      <button :disabled="loading" @click="loadAnalytics(true)">刷新</button>
    </header>

    <section class="filter-panel">
      <label class="search">卡牌<input v-model="filters.search" placeholder="卡名或编号" @keyup.enter="loadAnalytics(true)"/></label>
      <label>数据范围<select v-model="filters.mode"><option value="ranked">排位（平衡默认）</option><option value="casual">休闲</option><option value="friendly">好友房</option><option value="tournament">赛事</option></select></label>
      <label>开始日期<input v-model="filters.from" type="date"/></label>
      <label>结束日期<input v-model="filters.to" type="date"/></label>
      <label>主宰编号<input v-model="filters.masterId" placeholder="全部主宰"/></label>
      <label>最小样本<input v-model.number="filters.minimumSample" type="number" min="1" max="1000"/></label>
      <button class="query" :disabled="loading" @click="loadAnalytics(true)">分析</button>
    </section>

    <div class="scope-summary">
      <article><small>正式对局</small><b>{{ summaryMetrics.eligibleMatches ?? 0 }}</b><span>{{ modeLabel(filters.mode) }}，不含沙盒</span></article>
      <article><small>有数据卡牌</small><b>{{ page.total }}</b><span>符合当前切片</span></article>
      <article><small>精确事实</small><b>{{ summaryMetrics.coverage?.exactFacts ?? 0 }}</b><span>由权威事件直接记录</span></article>
      <article><small>推断／部分事实</small><b>{{ (summaryMetrics.coverage?.inferredFacts ?? 0) + (summaryMetrics.coverage?.partialFacts ?? 0) }}</b><span>不会混作精确指标</span></article>
    </div>

    <div class="analytics-workspace">
      <section class="card-list panel-shell">
        <header><b>卡牌清单</b><span>按样本与编号查询</span></header>
        <button v-for="item in page.items" :key="item.cardId" class="card-row" :class="{ selected: detail?.summary.cardId === item.cardId }" @click="selectCard(item.cardId)">
          <CardImage :card-id="item.cardId" :legacy-url="cardById.get(item.cardId)?.imageUrl" :alt="cardById.get(item.cardId)?.nameZh || item.cardId" intent="thumb"/>
          <span><b>{{ cardById.get(item.cardId)?.nameZh || item.cardId }}</b><small>{{ item.cardId }} · {{ item.includedMatches }} 场收录</small><em>{{ coverageLabel(item.coverage) }}</em></span>
          <span class="row-numbers"><b>{{ percent(item.winRate) }}</b><small :data-tone="resultTone(item)">{{ signedPercent(item.winRateDelta) }}</small></span>
        </button>
        <div v-if="loading" class="empty">正在聚合卡牌事实…</div>
        <div v-else-if="!page.items.length" class="empty">没有达到样本条件的卡牌</div>
        <button v-if="page.nextCursor" class="load-more" :disabled="loading" @click="loadAnalytics(false)">加载更多卡牌</button>
      </section>

      <main class="analysis-detail panel-shell">
        <div v-if="detailLoading" class="empty">正在读取分层分析…</div>
        <template v-else-if="detail">
          <header class="card-heading">
            <CardImage :card-id="detail.summary.cardId" :legacy-url="selectedCatalogCard?.imageUrl" :alt="selectedCatalogCard?.nameZh || detail.summary.cardId" intent="detail" eager/>
            <div><small>{{ detail.summary.cardId }} · {{ selectedCatalogCard?.faction || '未知阵营' }}</small><h3>{{ selectedCatalogCard?.nameZh || detail.summary.cardId }}</h3><p>{{ coverageLabel(detail.coverage) }} · {{ detail.summary.sampleSize }} 场样本</p><em :data-tone="resultTone(detail.summary)">{{ resultText(detail.summary) }}</em></div>
          </header>

          <section class="key-metrics">
            <article><small>入组率</small><b>{{ percent(detail.summary.inclusionRate) }}</b><span>{{ detail.summary.includedMatches }} 场收录</span></article>
            <article><small>入组胜率</small><b>{{ percent(detail.summary.winRate) }}</b><span>{{ detail.summary.wins }} 场获胜</span></article>
            <article><small>同条件基线</small><b>{{ percent(detail.summary.baselineWinRate) }}</b><span>同筛选范围未收录该卡样本</span></article>
            <article><small>胜率关联差</small><b :data-tone="resultTone(detail.summary)">{{ signedPercent(detail.summary.winRateDelta) }}</b><span>不是因果结论</span></article>
          </section>

          <section class="funnel-panel">
            <header><div><h3>使用漏斗</h3><p>同一张卡从构筑机会到成功结算的实际路径。</p></div></header>
            <div class="funnel">
              <article><span><b>构筑收录</b><em>{{ detail.summary.includedMatches }}</em></span><i><b style="width:100%"/></i></article>
              <article><span><b>实际抽到</b><em>{{ detail.summary.drawnMatches }}</em></span><i><b :style="{ width: `${ratio(detail.summary.drawnMatches, detail.summary.includedMatches) * 100}%` }"/></i></article>
              <article><span><b>从手牌打出</b><em>{{ detail.summary.playedMatches }}</em></span><i><b :style="{ width: `${ratio(detail.summary.playedMatches, detail.summary.includedMatches) * 100}%` }"/></i></article>
              <article><span><b>效果发动</b><em>{{ detail.summary.activatedCount }}</em></span><i><b :style="{ width: `${ratio(detail.summary.activatedCount, Math.max(detail.summary.activatedCount, detail.summary.includedMatches)) * 100}%` }"/></i></article>
              <article><span><b>正常结算</b><em>{{ detail.summary.resolvedCount }}</em></span><i><b :style="{ width: `${ratio(detail.summary.resolvedCount, Math.max(1, detail.summary.activatedCount)) * 100}%` }"/></i></article>
            </div>
            <div class="resolution-strip"><span>被无效 <b>{{ detail.summary.negatedCount }}</b></span><span>目标失效／空结算 <b>{{ detail.summary.fizzledCount }}</b></span></div>
          </section>

          <section class="breakdowns">
            <header><div><h3>条件切片</h3><p>按模式、主宰、对阵、先后手与规则版本分开观察，避免总体胜率误导。</p></div></header>
            <div class="breakdown-head"><span>维度</span><span>条件</span><span>样本</span><span>胜率</span><span>基线</span><span>关联差</span></div>
            <article v-for="row in detail.breakdowns" :key="`${row.dimension}-${row.value}`"><small>{{ row.dimension }}</small><b>{{ row.value }}</b><span>{{ row.sampleSize }}</span><span>{{ percent(row.winRate) }}</span><span>{{ percent(row.baselineWinRate) }}</span><strong :data-tone="typeof row.winRateDelta === 'number' && row.winRateDelta >= .05 ? 'positive' : typeof row.winRateDelta === 'number' && row.winRateDelta <= -.05 ? 'negative' : 'neutral'">{{ signedPercent(row.winRateDelta) }}</strong></article>
            <div v-if="!detail.breakdowns?.length" class="empty compact">样本尚不足以形成可靠的条件切片</div>
          </section>

          <section class="recent-matches">
            <header><div><h3>包含该卡的最近对局</h3><p>直接下钻到双方构筑和赛后时间线。</p></div></header>
            <button v-for="match in detail.recentMatches" :key="match.matchId" @click="emit('openMatch', match.matchId)"><span><b>{{ match.players.map(player => player.displayName).join(' VS ') }}</b><small>{{ modeLabel(match.modeId) }} · {{ dateLabel(match.startedUtc) }}</small></span><code>{{ match.matchId.slice(0, 12) }}</code><em>查看对局 →</em></button>
            <div v-if="!detail.recentMatches?.length" class="empty compact">暂无可下钻的新格式对局</div>
          </section>
        </template>
        <div v-else class="empty">选择一张卡查看影响分析</div>
      </main>
    </div>
  </section>
</template>

<style scoped>
.card-analytics{display:grid;gap:12px}.module-header,.filter-panel,.scope-summary,.panel-shell{border:1px solid #35424a;background:#101821}.module-header{display:flex;align-items:center;justify-content:space-between;padding:18px 20px}.module-header h2{margin:4px 0}.module-header p{margin:0}.module-header button,.filter-panel input,.filter-panel select,.filter-panel button{border:1px solid #4c5961;background:#080e13;color:#fff;padding:9px;font:700 11px 'Microsoft YaHei'}.filter-panel{display:grid;grid-template-columns:minmax(210px,1.5fr) repeat(5,minmax(120px,1fr)) auto;align-items:end;gap:8px;padding:12px}.filter-panel label{display:flex;min-width:0;flex-direction:column;gap:5px;color:#87949a;font-size:9px;font-weight:900}.filter-panel input,.filter-panel select{box-sizing:border-box;min-width:0;width:100%}.filter-panel .query{border-color:#91752e;background:#2b220d;color:#f1d471}.scope-summary{display:grid;grid-template-columns:repeat(4,1fr);gap:1px;background:#35424a}.scope-summary article{display:flex;min-height:82px;flex-direction:column;justify-content:flex-end;gap:3px;padding:13px;background:#0c141a}.scope-summary b{font-size:21px}.scope-summary span{color:#77858b;font-size:9px}.analytics-workspace{display:grid;grid-template-columns:minmax(340px,.68fr) minmax(650px,1.32fr);align-items:start;gap:12px}.panel-shell{min-width:0}.card-list{max-height:calc(100vh - 185px);overflow:auto}.card-list>header{position:sticky;z-index:2;top:0;display:flex;justify-content:space-between;padding:13px;background:#0b1218;border-bottom:1px solid #35424a}.card-list>header span{color:#738087;font-size:9px}.card-row{display:grid;width:100%;grid-template-columns:44px minmax(0,1fr) 72px;align-items:center;gap:9px;padding:9px;border:0;border-bottom:1px solid #29353c;background:transparent;color:#fff;text-align:left}.card-row:hover,.card-row.selected{background:#172129}.card-row.selected{box-shadow:inset 3px 0 #d0ae4f}.card-row>.l12-card-image{width:44px;height:61px}.card-row>span{display:flex;min-width:0;flex-direction:column;gap:3px}.card-row span b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.card-row span small{color:#718087!important;letter-spacing:0!important}.card-row span em{color:#68aeb2;font-size:8px;font-style:normal}.row-numbers{text-align:right}.row-numbers small[data-tone="positive"],[data-tone="positive"]{color:#72d1a7!important}.row-numbers small[data-tone="negative"],[data-tone="negative"]{color:#e7848e!important}[data-tone="neutral"]{color:#d7bd6b!important}.load-more{width:100%;padding:12px;border:0;background:#182229;color:#d5b85e;font-weight:900}.analysis-detail{padding:17px}.card-heading{display:grid;grid-template-columns:118px minmax(0,1fr);align-items:center;gap:16px;padding-bottom:15px;border-bottom:1px solid #35424a}.card-heading>.l12-card-image{width:118px;height:164px}.card-heading h3{margin:6px 0;font-size:24px}.card-heading p{margin:0}.card-heading em{display:inline-block;margin-top:9px;padding:5px 8px;border:1px solid currentColor;background:#0a1116;font-size:9px;font-style:normal;font-weight:900}.key-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:7px;margin-top:12px}.key-metrics article{display:flex;min-height:92px;flex-direction:column;justify-content:flex-end;gap:4px;padding:12px;border:1px solid #35424a;background:#0a1117}.key-metrics b{font-size:21px}.key-metrics span{color:#76848a;font-size:8px}.funnel-panel,.breakdowns,.recent-matches{margin-top:12px;padding:13px;border:1px solid #35424a;background:#0a1117}.funnel-panel>header,.breakdowns>header,.recent-matches>header{border-bottom:1px solid #2d3940;padding-bottom:9px}.funnel-panel h3,.breakdowns h3,.recent-matches h3{margin:0}.funnel-panel p,.breakdowns p,.recent-matches p{margin:3px 0}.funnel{display:grid;gap:8px;margin-top:12px}.funnel article>span{display:flex;justify-content:space-between;margin-bottom:4px;font-size:9px}.funnel article>span em{color:#d7bd6b;font-style:normal}.funnel i{display:block;height:7px;overflow:hidden;background:#1a252c}.funnel i b{display:block;height:100%;min-width:2px;background:linear-gradient(90deg,#4b999d,#d0b04f)}.resolution-strip{display:flex;gap:8px;margin-top:10px}.resolution-strip span{flex:1;padding:8px;border:1px solid #3d4850;color:#89969b;font-size:9px}.resolution-strip b{float:right;color:#e4c66e}.breakdown-head,.breakdowns>article{display:grid;grid-template-columns:90px minmax(150px,1fr) 60px 70px 70px 75px;align-items:center;gap:7px;padding:8px}.breakdown-head{margin-top:8px;color:#718087;font-size:8px;font-weight:900}.breakdowns>article{border-top:1px solid #28343b;font-size:9px}.breakdowns>article small{color:#67b7bc!important}.breakdowns>article strong{text-align:right}.recent-matches>button{display:grid;width:100%;grid-template-columns:minmax(0,1fr) 100px auto;align-items:center;gap:8px;padding:9px;border:0;border-bottom:1px solid #29353c;background:transparent;color:#fff;text-align:left}.recent-matches>button:hover{background:#172129}.recent-matches>button span{display:flex;min-width:0;flex-direction:column}.recent-matches>button span small{color:#718087!important;letter-spacing:0!important}.recent-matches code{color:#67818a}.recent-matches em{color:#d7bd6b;font-size:9px;font-style:normal}.empty{display:grid;min-height:170px;place-items:center;color:#718087}.empty.compact{min-height:90px}.card-analytics small{color:#d5b85e;font:900 9px monospace;letter-spacing:.12em}
@media(max-width:1450px){.filter-panel{grid-template-columns:repeat(4,minmax(130px,1fr))}.analytics-workspace{grid-template-columns:minmax(320px,.6fr) minmax(580px,1.4fr)}}
@media(max-width:1050px){.analytics-workspace{grid-template-columns:1fr}.card-list{max-height:500px}.key-metrics{grid-template-columns:1fr 1fr}}
@media(max-width:720px){.filter-panel{grid-template-columns:1fr 1fr}.scope-summary{grid-template-columns:1fr 1fr}.card-heading{grid-template-columns:88px minmax(0,1fr)}.card-heading>.l12-card-image{width:88px;height:122px}.breakdown-head,.breakdowns>article{grid-template-columns:70px 1fr 55px}.breakdown-head span:nth-child(n+4),.breakdowns>article>*:nth-child(n+4){display:none}.recent-matches>button{grid-template-columns:1fr auto}.recent-matches code{display:none}}
</style>
