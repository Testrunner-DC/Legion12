<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import SandboxCardPicker, { type SandboxCatalogCard } from '@/l12/game/SandboxCardPicker.vue'
import PagedCollection from './PagedCollection.vue'
import {
  adminApi,
  getEffectiveOperationsPolicy,
  type AdminAnalyticsConfidenceInterval,
  type AdminCardAnalyticsBreakdown,
  type AdminCardAnalyticsDetail,
  type AdminCardAnalyticsItem,
  type AdminCardAnalyticsPage,
  type AdminMatchSummary,
} from '@/l12/platform'

type AnalyticsTab = 'single' | 'list'
type AnalyticsRange = 'all' | '7d' | '30d' | 'season'
type AnalyticsSort = 'name' | 'sampleSize' | 'includedMatches' | 'inclusionRate' | 'winRate' | 'delta'

const emit = defineEmits<{ notice: [message: string]; openMatch: [matchId: string] }>()
const page = ref<AdminCardAnalyticsPage>({ items: [], total: 0 })
const detail = ref<AdminCardAnalyticsDetail | null>(null)
const cards = ref<DeckCard[]>([])
const activeTab = ref<AnalyticsTab>('single')
const range = ref<AnalyticsRange>('30d')
const currentSeason = ref<{ id: string; name: string } | null>(null)
const pickerOpen = ref(false)
const selectedCardId = ref('')
const listSearch = ref('')
const listPage = ref(1)
const listPageSize = 10
const sortKey = ref<AnalyticsSort>('sampleSize')
const sortDirection = ref<'asc' | 'desc'>('desc')
const loading = ref(false)
const detailLoading = ref(false)
function localDateInput(value: Date) {
  const year = value.getFullYear()
  const month = String(value.getMonth() + 1).padStart(2, '0')
  const day = String(value.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}
const today = new Date()
const thirtyDayStart = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29)
const filters = ref({
  mode: 'ranked', from: localDateInput(thirtyDayStart), to: localDateInput(today), masterId: '', opponentMasterId: '',
  initiative: '', rulesVersion: '', effectVersion: 'current', seasonId: '', minimumSample: 10,
})

const cardById = computed(() => new Map(cards.value.map(card => [card.id, card])))
const masterOptions = computed(() => cards.value
  .filter(card => card.cardType === 'master')
  .sort((left, right) => left.nameZh.localeCompare(right.nameZh, 'zh-CN')))
const selectedCatalogCard = computed(() => cardById.value.get(selectedCardId.value || detail.value?.summary.cardId || ''))
const summaryMetrics = computed(() => page.value.summary || {})
const sortedItems = computed(() => [...page.value.items].sort((left, right) => {
  const leftName = cardById.value.get(left.cardId)?.nameZh || left.cardId
  const rightName = cardById.value.get(right.cardId)?.nameZh || right.cardId
  const values: Record<Exclude<AnalyticsSort, 'name'>, [number, number]> = {
    sampleSize: [left.sampleSize, right.sampleSize],
    includedMatches: [left.includedMatches, right.includedMatches],
    inclusionRate: [left.inclusionRate, right.inclusionRate],
    winRate: [left.winRate, right.winRate],
    delta: [left.comparison?.delta ?? Number.NEGATIVE_INFINITY, right.comparison?.delta ?? Number.NEGATIVE_INFINITY],
  }
  const comparison = sortKey.value === 'name'
    ? leftName.localeCompare(rightName, 'zh-CN')
    : values[sortKey.value][0] - values[sortKey.value][1]
  if (comparison !== 0) return sortDirection.value === 'asc' ? comparison : -comparison
  return left.cardId.localeCompare(right.cardId)
}))
const listPageCount = computed(() => Math.max(1, Math.ceil(sortedItems.value.length / listPageSize)))
const visibleListItems = computed(() => sortedItems.value.slice((listPage.value - 1) * listPageSize, listPage.value * listPageSize))
const maximumTiming = computed(() => Math.max(1, ...(detail.value?.turnDistribution || [])
  .flatMap(bucket => [bucket.firstDrawSamples, bucket.firstPlaySamples])))
const maximumQuantity = computed(() => Math.max(1, ...(detail.value?.quantityDistribution || [])
  .map(bucket => bucket.sampleSize)))
const matchupRows = computed(() => Array.from(new Set((detail.value?.matchups || []).map(row => row.masterId))))
const matchupColumns = computed(() => Array.from(new Set((detail.value?.matchups || []).map(row => row.opponentMasterId))))

function percent(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
  return `${(value * 100).toFixed(1)}%`
}
function signedPercent(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
  return `${value >= 0 ? '+' : ''}${(value * 100).toFixed(1)}`
}
function confidenceLabel(value?: AdminAnalyticsConfidenceInterval | null, signed = false) {
  if (!value) return '—'
  return `${signed ? signedPercent(value.low) : percent(value.low)} – ${signed ? signedPercent(value.high) : percent(value.high)}`
}
function ratio(value: number, total: number) { return total > 0 ? Math.min(1, Math.max(0, value / total)) : 0 }
function coverageLabel(value: AdminCardAnalyticsItem['coverage'] | AdminCardAnalyticsDetail['coverage']) {
  if (!value) return '暂无覆盖说明'
  if (value.inferredFacts || value.inferredDeckSnapshots) return '含历史推断数据'
  if (value.partialFacts) return '精确事实 + 部分覆盖'
  return '结构化精确事实'
}
function metricLabel(metric: string) {
  return ({ inclusion: '构筑收录', draw: '首次抽到', play: '从手牌打出', activation: '效果发动', settlement: '结算状态', 'all-card-facts': '全部卡牌事实' } as Record<string, string>)[metric] || metric
}
function observedLabel(metric: string, count: number) {
  const coverage = detail.value?.summary.usage?.metrics.find(item => item.metric === metric)
  return count > 0 ? `${count} 份已记录` : coverage?.coverageStatus === 'complete' ? '0 份' : '未记录／未知'
}
function observedPercent(metric: string, count: number, total: number) {
  const coverage = detail.value?.summary.usage?.metrics.find(item => item.metric === metric)
  return count > 0 || coverage?.coverageStatus === 'complete' ? percent(ratio(count, total)) : '—'
}
function modeLabel(mode: string) {
  return ({ '': '全部模式', ranked: '排位', casual: '休闲', friendly: '好友房', tournament: '赛事' } as Record<string, string>)[mode] || mode
}
function masterLabel(masterId?: string | null) {
  if (!masterId || masterId === 'unknown') return '未知主宰'
  return cardById.value.get(masterId)?.nameZh || masterId
}
function dateLabel(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}
function resultText(item: AdminCardAnalyticsItem) {
  if (isLowSample(item)) return '低样本，仅供参考'
  if (item.comparison?.delta == null) return '同条件对照不足，无法可靠比较'
  if (item.comparison.uncertainty?.status !== 'available') return '调整差值仅供描述，尚不能确认方向'
  const interval = item.comparison.uncertainty
  if (typeof interval.low === 'number' && interval.low > 0) return '区间支持正向关联，不代表因果'
  if (typeof interval.high === 'number' && interval.high < 0) return '区间支持负向关联，不代表因果'
  return '区间跨过零，方向未确定'
}
function isLowSample(item: AdminCardAnalyticsItem) {
  return item.sampleSize < Math.max(filters.value.minimumSample, 30)
}
function resultTone(item: AdminCardAnalyticsItem) {
  if (isLowSample(item)) return 'neutral'
  const interval = item.comparison?.uncertainty
  if (interval?.status !== 'available' || interval.low == null || interval.high == null) return 'neutral'
  return deltaTone({ low: interval.low, high: interval.high })
}
function deltaTone(value?: AdminAnalyticsConfidenceInterval | null) {
  if (!value) return 'unavailable'
  return value.low > 0 ? 'positive' : value.high < 0 ? 'negative' : 'neutral'
}
function dimensionLabel(value: string) {
  return ({ mode: '模式', master: '使用方主宰', 'opponent-master': '对方主宰', initiative: '先后手', 'rules-version': '运营规则版本', 'effect-version': '卡效版本', season: '赛季' } as Record<string, string>)[value] || value
}
function dimensionValue(row: AdminCardAnalyticsBreakdown) {
  if (row.dimension === 'mode') return modeLabel(row.value)
  if (row.dimension === 'master' || row.dimension === 'opponent-master') return masterLabel(row.value)
  if (row.dimension === 'initiative') return row.value === 'first' ? '先手' : row.value === 'second' ? '后手' : '未知'
  if (row.dimension === 'season' && row.value === 'unassigned') return '未记录赛季'
  return row.value
}
function matchupCell(masterId: string, opponentMasterId: string) {
  return detail.value?.matchups.find(row => row.masterId === masterId && row.opponentMasterId === opponentMasterId)
}
function recentMatchup(match: AdminMatchSummary) {
  return match.players.map(player => masterLabel(player.masterId)).join(' 对阵 ')
}
function recentResult(match: AdminMatchSummary) {
  const owner = match.players.find(player => player.result === 'win')
  if (owner) return `${masterLabel(owner.masterId)}获胜`
  return match.players.every(player => player.result === 'draw') ? '平局' : '已结算'
}

let listRequest = 0
let detailRequest = 0
const analyticsListLimit = 200
const maximumAnalyticsListItems = 2000
function analyticsQuery() { return { ...filters.value } }
function applyRange(next: AnalyticsRange) {
  range.value = next
  filters.value.seasonId = ''
  filters.value.from = ''
  filters.value.to = ''
  const now = new Date()
  if (next === 'season') {
    filters.value.seasonId = currentSeason.value?.id || ''
    return
  }
  if (next === 'all') return
  const days = next === '7d' ? 7 : 30
  filters.value.from = localDateInput(new Date(now.getFullYear(), now.getMonth(), now.getDate() - days + 1))
  filters.value.to = localDateInput(now)
}
function chooseCard(card: SandboxCatalogCard) {
  pickerOpen.value = false
  selectedCardId.value = card.id
  detail.value = null
  void selectCard(card.id)
}
function setSort(next: AnalyticsSort) {
  if (sortKey.value === next) sortDirection.value = sortDirection.value === 'asc' ? 'desc' : 'asc'
  else {
    sortKey.value = next
    sortDirection.value = next === 'name' ? 'asc' : 'desc'
  }
  listPage.value = 1
}
function sortMarker(key: AnalyticsSort) { return sortKey.value === key ? (sortDirection.value === 'asc' ? ' ↑' : ' ↓') : '' }
async function loadAnalytics() {
  const request = ++listRequest
  const query = analyticsQuery()
  const search = listSearch.value.trim()
  loading.value = true
  try {
    const items: AdminCardAnalyticsItem[] = []
    let cursor: string | undefined
    let firstPage: AdminCardAnalyticsPage | null = null
    do {
      const next = await adminApi.cardAnalytics({ ...query, search, cursor, limit: analyticsListLimit })
      if (request !== listRequest) return
      firstPage ||= next
      items.push(...next.items)
      if (next.total > maximumAnalyticsListItems)
        throw new Error(`当前筛选返回 ${next.total} 张卡，超过清单安全上限 ${maximumAnalyticsListItems}`)
      cursor = next.nextCursor || undefined
    } while (cursor && items.length < (firstPage?.total ?? 0))
    if (firstPage && items.length < firstPage.total) {
      throw new Error(`卡牌清单只读取到 ${items.length} / ${firstPage.total} 张，已停止显示不完整排序`)
    }
    page.value = firstPage ? { ...firstPage, items, nextCursor: null } : { items: [], total: 0 }
    listPage.value = 1
  } catch (error) { if (request === listRequest) emit('notice', error instanceof Error ? error.message : '单卡分析加载失败') }
  finally { if (request === listRequest) loading.value = false }
}
async function selectCard(cardId = selectedCardId.value) {
  if (!cardId) return
  selectedCardId.value = cardId
  const request = ++detailRequest
  const query = analyticsQuery()
  detailLoading.value = true
  try {
    const next = await adminApi.cardAnalyticsDetail(cardId, query)
    if (request === detailRequest) detail.value = next
  }
  catch (error) {
    if (request === detailRequest) {
      detail.value = null
      emit('notice', error instanceof Error ? error.message : '单卡分析详情加载失败')
    }
  } finally { if (request === detailRequest) detailLoading.value = false }
}

watch(filters, () => {
  ++detailRequest
  detail.value = null
  detailLoading.value = false
  ++listRequest
  page.value = { items: [], total: 0 }
  listPage.value = 1
  loading.value = false
}, { deep: true, flush: 'sync' })

onMounted(async () => {
  const [catalogResult, policyResult] = await Promise.allSettled([loadDeckCatalog(), getEffectiveOperationsPolicy()])
  if (catalogResult.status === 'fulfilled') cards.value = catalogResult.value
  else emit('notice', catalogResult.reason instanceof Error ? catalogResult.reason.message : '卡牌图鉴加载失败')
  if (policyResult.status === 'fulfilled') {
    currentSeason.value = { id: policyResult.value.season.id, name: policyResult.value.season.name }
  } else emit('notice', '当前赛季读取失败；其余时间范围仍可使用')
})
</script>

<template>
  <section class="card-analytics">
    <header class="module-header">
      <div><small>排位卡牌仪表盘</small><h2>单卡影响分析</h2><p>仅分析新统计格式的已结束排位对局；比较携带表现、使用情况与同条件关联。</p></div>
      <button :disabled="loading || detailLoading || (activeTab === 'single' && !selectedCardId)" @click="activeTab === 'single' ? selectCard() : loadAnalytics()">刷新事实</button>
    </header>

    <nav class="module-tabs" aria-label="单卡分析视图">
      <button :class="{ active: activeTab === 'single' }" @click="activeTab = 'single'">单卡仪表盘</button>
      <button :class="{ active: activeTab === 'list' }" @click="activeTab = 'list'; loadAnalytics()">卡牌数据清单</button>
    </nav>

    <section class="filter-panel" aria-label="单卡分析筛选">
      <label v-if="activeTab === 'list'" class="search">清单搜索<input v-model="listSearch" type="search" placeholder="卡名、编号或 ID" @keyup.enter="loadAnalytics()"/></label>
      <label class="master-filter">1. 使用方主宰<select v-model="filters.masterId"><option value="">全部主宰（允许所有卡牌）</option><option v-for="master in masterOptions" :key="`mine-${master.id}`" :value="master.id">{{ master.nameZh }} · {{ master.id }}</option></select></label>
      <button v-if="activeTab === 'single'" class="card-choice" type="button" @click="pickerOpen = true">
        <CardImage v-if="selectedCatalogCard" :card-id="selectedCatalogCard.id" :legacy-url="selectedCatalogCard.imageUrl" :alt="selectedCatalogCard.nameZh" intent="thumb"/>
        <span><small>2. 指定分析卡牌</small><b>{{ selectedCatalogCard?.nameZh || '从 GM 卡牌图鉴选择' }}</b><em>{{ selectedCatalogCard?.number || '支持全部类型、阵营与卡池' }}</em></span>
      </button>
      <fieldset class="range-filter"><legend>统计时间</legend><button type="button" :class="{ active: range === 'all' }" @click="applyRange('all')">全部</button><button type="button" :class="{ active: range === '7d' }" @click="applyRange('7d')">近 7 天</button><button type="button" :class="{ active: range === '30d' }" @click="applyRange('30d')">近 30 天</button><button type="button" :class="{ active: range === 'season' }" :disabled="!currentSeason" @click="applyRange('season')">{{ currentSeason ? `本赛季 · ${currentSeason.name}` : '本赛季读取中' }}</button></fieldset>
      <label>卡效版本<select v-model="filters.effectVersion" aria-label="卡效版本"><option value="current">当前卡效版本</option><option value="all">全部已记录版本（分层比较）</option></select></label>
      <label>对方主宰<select v-model="filters.opponentMasterId"><option value="">全部主宰</option><option v-for="master in masterOptions" :key="`enemy-${master.id}`" :value="master.id">{{ master.nameZh }} · {{ master.id }}</option></select></label>
      <label>先后手<select v-model="filters.initiative"><option value="">全部</option><option value="first">先手</option><option value="second">后手</option></select></label>
      <label>运营规则版本<input v-model.trim="filters.rulesVersion" placeholder="全部运营规则"/></label>
      <label>最小参赛方样本<input v-model.number="filters.minimumSample" type="number" min="1" max="1000"/></label>
      <button class="query" :disabled="activeTab === 'single' ? (!selectedCardId || detailLoading) : loading" @click="activeTab === 'single' ? selectCard() : loadAnalytics()">{{ activeTab === 'single' ? '查询这张卡' : '刷新完整清单' }}</button>
    </section>
    <p class="sample-contract" data-ui-contract="card-analytics-low-sample-warning">统计单位为“参赛方 × 对局”；同一局双方与重复玩家并非独立样本。低于 {{ Math.max(filters.minimumSample, 30) }} 份仅供参考；没有未收录参赛方时基线显示“—”。只纳入具有明确卡效版本的新排位数据，旧记录不回填。统计缓存最多延迟30秒。</p>

    <div v-if="activeTab === 'list'" class="scope-summary">
      <article><small>有效已结束排位</small><b>{{ summaryMetrics.eligibleMatches ?? 0 }}</b><span>排除非排位、旧统计记录、错误终局与无明确胜者记录</span></article>
      <article><small>参赛方样本</small><b>{{ summaryMetrics.sampleSize ?? 0 }}</b><span>统计单位：参赛方 × 对局</span></article>
      <article><small>达到门槛的卡牌</small><b>{{ page.total }}</b><span>已先筛选再分页</span></article>
      <article><small>事实质量</small><b>按单卡查看</b><span>列表不扫描使用事实；选择卡牌后计算覆盖情况</span></article>
    </div>

    <section v-if="activeTab === 'list'" class="card-list panel-shell">
      <header><div><b>完整筛选清单</b><span>先读取全部 {{ page.total }} 张结果，再进行排序与分页</span></div><em>第 {{ listPage }} / {{ listPageCount }} 页</em></header>
      <div class="list-sort" aria-label="卡牌清单排序">
        <button @click="setSort('name')">卡牌{{ sortMarker('name') }}</button><button @click="setSort('sampleSize')">参赛方样本{{ sortMarker('sampleSize') }}</button><button @click="setSort('includedMatches')">对局数{{ sortMarker('includedMatches') }}</button><button @click="setSort('inclusionRate')">收录率{{ sortMarker('inclusionRate') }}</button><button @click="setSort('winRate')">胜率{{ sortMarker('winRate') }}</button><button @click="setSort('delta')">调整差{{ sortMarker('delta') }}</button>
      </div>
      <button v-for="item in visibleListItems" :key="item.cardId" class="card-row" :data-low-sample="isLowSample(item)" @click="activeTab = 'single'; selectCard(item.cardId)">
        <CardImage :card-id="item.cardId" :legacy-url="cardById.get(item.cardId)?.imageUrl" :alt="cardById.get(item.cardId)?.nameZh || item.cardId" intent="thumb"/>
        <span><b>{{ cardById.get(item.cardId)?.nameZh || item.cardId }}</b><small>{{ item.cardId }}</small></span>
        <span>{{ item.sampleSize }}</span><span>{{ item.includedMatches }}</span><span>{{ percent(item.inclusionRate) }}</span><span>{{ percent(item.winRate) }}</span><span :data-tone="resultTone(item)">{{ signedPercent(item.comparison?.delta) }}</span>
      </button>
      <div v-if="loading" class="empty">正在读取完整筛选清单…</div>
      <div v-else-if="!page.items.length" class="empty">排位数据不足，尚无卡牌达到当前样本门槛</div>
      <footer v-if="page.items.length" class="pagination"><button :disabled="listPage <= 1" @click="listPage--">上一页</button><span>{{ (listPage - 1) * listPageSize + 1 }}–{{ Math.min(listPage * listPageSize, page.items.length) }} / {{ page.items.length }}</span><button :disabled="listPage >= listPageCount" @click="listPage++">下一页</button></footer>
    </section>

      <main v-else class="analysis-detail panel-shell">
        <div v-if="detailLoading" class="empty">正在读取分层事实…</div>
        <template v-else-if="detail">
          <header class="card-heading">
            <CardImage :card-id="detail.summary.cardId" :legacy-url="selectedCatalogCard?.imageUrl" :alt="selectedCatalogCard?.nameZh || detail.summary.cardId" intent="detail" eager/>
            <div><small>{{ detail.summary.cardId }} · {{ selectedCatalogCard?.faction || '未知阵营' }}</small><h3>{{ selectedCatalogCard?.nameZh || detail.summary.cardId }}</h3><p>{{ coverageLabel(detail.coverage) }} · {{ detail.summary.sampleSize }} 份参赛方样本 / {{ detail.summary.includedMatches }} 场</p><em :data-tone="resultTone(detail.summary)">{{ resultText(detail.summary) }}</em></div>
          </header>

          <section class="key-metrics" aria-label="核心指标">
            <article><small>构筑收录率</small><b>{{ percent(detail.summary.inclusionRate) }}</b><span>{{ detail.summary.sampleSize }} / {{ detail.summary.eligibleSampleSize }} 份参赛方</span></article>
            <article><small>平均携带数量</small><b>{{ detail.summary.averageQuantity.toFixed(2) }}</b><span>每份收录构筑中的平均张数</span></article>
            <article><small>原始携带胜率</small><b>{{ percent(detail.summary.winRate) }}</b><span>{{ detail.summary.wins }} 胜 / {{ detail.summary.sampleSize }} 份 · 区间 {{ confidenceLabel(detail.summary.winRateConfidence) }}</span></article>
            <article><small>同条件未携带基线</small><b>{{ percent(detail.summary.comparison?.winRate) }}</b><span>同主宰、卡效版本、对方主宰与先后手分层；原始区间 {{ confidenceLabel(detail.summary.baselineWinRateConfidence) }}</span></article>
            <article><small>调整后胜率差（百分点）</small><b :data-tone="resultTone(detail.summary)">{{ signedPercent(detail.summary.comparison?.delta) }}</b><span>仅比较双方均有样本的条件；不代表因果提升</span></article>
          </section>

          <section class="behavior-metrics" aria-label="真实使用指标">
            <article><small>记录到抽取</small><b>{{ observedPercent('draw', detail.summary.drawnSamples, detail.summary.sampleSize) }}</b><span>{{ observedLabel('draw', detail.summary.drawnSamples) }} / 收录样本</span></article>
            <article><small>记录到手牌打出</small><b>{{ observedPercent('play', detail.summary.playedSamples, detail.summary.sampleSize) }}</b><span>{{ observedLabel('play', detail.summary.playedSamples) }} / 收录样本</span></article>
            <article><small>记录到效果发动</small><b>{{ observedPercent('activation', detail.summary.activatedSamples, detail.summary.sampleSize) }}</b><span>{{ observedLabel('activation', detail.summary.activatedSamples) }} / 收录样本</span></article>
            <article><small>记录到结算状态</small><b>{{ observedPercent('settlement', detail.summary.settledSamples, detail.summary.sampleSize) }}</b><span>{{ observedLabel('settlement', detail.summary.settledSamples) }} / 收录样本</span></article>
          </section>

          <section class="dashboard-panel" aria-label="样本可靠性">
            <header><div><h3>样本可靠性</h3><p>场次多不等于独立玩家多，少数玩家反复使用会影响代表性。</p></div></header>
            <div class="coverage-grid">
              <article><b>独立对局</b><span>{{ detail.summary.sampleStructure?.distinctMatches ?? '—' }} 场</span></article>
              <article><b>可识别独立玩家</b><span>{{ detail.summary.sampleStructure?.distinctPlayers ?? '—' }} 人</span><small>身份缺失样本 {{ detail.summary.sampleStructure?.anonymousPlayerSamples ?? '—' }} 份</small></article>
              <article><b>最大单人样本占比</b><span>{{ percent(detail.summary.sampleStructure?.maximumPlayerContributionRate) }}</span></article>
              <article><b>可比较样本</b><span>携带 {{ detail.summary.comparison?.carriedSamples ?? 0 }} / 未携带 {{ detail.summary.comparison?.comparisonSamples ?? 0 }} 份</span><small>对照不足排除 {{ detail.summary.comparison?.excludedIncludedSamples ?? 0 }} 份携带样本</small></article>
            </div>
          </section>

          <div class="dashboard-pair">
            <section class="dashboard-panel usage-panel">
              <header><div><h3>实际使用情况</h3><p>各项独立统计，军团可能从墓地登场，效果也可能不经手牌打出就发动；未记录不能推断为未发生。</p></div></header>
              <div class="funnel">
                <article><span><b>构筑收录</b><em>{{ detail.summary.sampleSize }}</em></span><i><b style="width:100%"/></i></article>
                <article><span><b>实际抽到</b><em>{{ observedLabel('draw', detail.summary.drawnSamples) }}</em></span><i v-if="detail.summary.drawnSamples"><b :style="{ width: `${ratio(detail.summary.drawnSamples, detail.summary.sampleSize) * 100}%` }"/></i></article>
                <article><span><b>从手牌打出</b><em>{{ observedLabel('play', detail.summary.playedSamples) }}</em></span><i v-if="detail.summary.playedSamples"><b :style="{ width: `${ratio(detail.summary.playedSamples, detail.summary.sampleSize) * 100}%` }"/></i></article>
                <article><span><b>效果发动</b><em>{{ observedLabel('activation', detail.summary.activatedSamples) }}</em></span><i v-if="detail.summary.activatedSamples"><b :style="{ width: `${ratio(detail.summary.activatedSamples, detail.summary.sampleSize) * 100}%` }"/></i></article>
                <article><span><b>出现结算状态</b><em>{{ observedLabel('settlement', detail.summary.settledSamples) }}</em></span><i v-if="detail.summary.settledSamples"><b :style="{ width: `${ratio(detail.summary.settledSamples, detail.summary.sampleSize) * 100}%` }"/></i></article>
              </div>
            </section>

            <section class="dashboard-panel settlement-panel">
              <header><div><h3>结算状态</h3><p>上方为参赛方样本，下方保留事实事件次数。</p></div></header>
              <div class="settlement-grid">
                <article><small>正常结算</small><b>{{ detail.summary.resolvedSamples }}</b><span>{{ detail.summary.resolvedCount }} 次事实</span></article>
                <article><small>被无效</small><b>{{ detail.summary.negatedSamples }}</b><span>{{ detail.summary.negatedCount }} 次事实</span></article>
                <article><small>目标失效／空结算</small><b>{{ detail.summary.fizzledSamples }}</b><span>{{ detail.summary.fizzledCount }} 次事实</span></article>
                <article><small>发动事件</small><b>{{ detail.summary.activatedSamples }}</b><span>{{ detail.summary.activatedCount }} 次事实</span></article>
              </div>
            </section>
          </div>

          <div class="dashboard-pair distributions">
            <section class="dashboard-panel">
              <header><div><h3>携带数量分布</h3><p>数量来自不可变赛后构筑快照。</p></div></header>
              <div v-if="detail.quantityDistribution.length" class="quantity-bars">
                <article v-for="bucket in detail.quantityDistribution" :key="bucket.quantity"><b>{{ bucket.quantity }} 张</b><i><span :style="{ width: `${ratio(bucket.sampleSize, maximumQuantity) * 100}%` }"/></i><em>{{ bucket.sampleSize }} 份 · 胜率 {{ percent(bucket.winRate) }}</em></article>
              </div>
              <div v-else class="empty compact">没有精确携带数量</div>
            </section>
            <section class="dashboard-panel">
              <header><div><h3>首次抽到／打出回合</h3><p>只统计 coverage=exact 的首次时点。</p></div><div class="legend"><span class="draw">抽到</span><span class="play">打出</span></div></header>
              <div v-if="detail.turnDistribution.length" class="turn-chart">
                <article v-for="bucket in detail.turnDistribution" :key="bucket.turn"><b>回合 {{ bucket.turn }}</b><div><i class="draw" :style="{ width: `${ratio(bucket.firstDrawSamples, maximumTiming) * 100}%` }"/><span>{{ bucket.firstDrawSamples }}</span></div><div><i class="play" :style="{ width: `${ratio(bucket.firstPlaySamples, maximumTiming) * 100}%` }"/><span>{{ bucket.firstPlaySamples }}</span></div></article>
              </div>
              <div v-else class="empty compact">没有精确首次抽到／打出事实</div>
            </section>
          </div>

          <section class="dashboard-panel matchup-panel">
            <header><div><h3>主宰对阵热图</h3><p>行是使用方主宰，列是对方主宰；格内显示收录方胜率与参赛方样本。</p></div></header>
            <div v-if="detail.matchups.length" class="heat-scroll">
              <table><thead><tr><th>使用方 ＼ 对方</th><th v-for="opponent in matchupColumns" :key="opponent">{{ masterLabel(opponent) }}</th></tr></thead><tbody><tr v-for="master in matchupRows" :key="master"><th>{{ masterLabel(master) }}</th><td v-for="opponent in matchupColumns" :key="`${master}-${opponent}`" :data-tone="deltaTone(matchupCell(master, opponent)?.winRateDeltaConfidence)"><template v-if="matchupCell(master, opponent)"><b>{{ percent(matchupCell(master, opponent)?.winRate) }}</b><span>{{ matchupCell(master, opponent)?.sampleSize }} 份</span><small>{{ signedPercent(matchupCell(master, opponent)?.winRateDelta) }} · CI {{ confidenceLabel(matchupCell(master, opponent)?.winRateDeltaConfidence, true) }}</small></template><span v-else>—</span></td></tr></tbody></table>
            </div>
            <div v-else class="empty compact">当前样本不足以形成对阵格</div>
          </section>

          <section class="dashboard-panel breakdowns">
            <header><div><h3>条件切片与版本趋势</h3><p>排位内按双方主宰、先后手、卡效版本与赛季查看描述性结果；差值单位为百分点，调整后差值以上方同条件比较为准。</p></div></header>
            <div class="breakdown-head"><span>维度</span><span>条件</span><span>收录样本</span><span>层内总样本</span><span>胜率</span><span>基线</span><span>关联差</span></div>
            <PagedCollection :items="detail.breakdowns" :page-size="10" label="条件切片"><template #default="{ items }"><article v-for="row in items" :key="`${row.dimension}-${row.value}`"><small>{{ dimensionLabel(row.dimension) }}</small><b>{{ dimensionValue(row) }}</b><span>{{ row.sampleSize }}</span><span>{{ row.eligibleSampleSize }}</span><span>{{ percent(row.winRate) }}</span><span>{{ percent(row.baselineWinRate) }}</span><strong :data-tone="deltaTone(row.winRateDeltaConfidence)">{{ signedPercent(row.winRateDelta) }}<small>CI {{ confidenceLabel(row.winRateDeltaConfidence, true) }}</small></strong></article></template></PagedCollection>
            <div v-if="!detail.breakdowns.length" class="empty compact">样本尚不足以形成条件切片</div>
          </section>

          <section class="dashboard-panel quality-panel">
            <header><div><h3>数据质量与覆盖</h3><p>按指标展示观察样本与事实覆盖；旧记录缺失不会被补成零。</p></div><strong>Schema v{{ detail.coverage.schemaVersion }}</strong></header>
            <div class="coverage-grid">
              <article v-for="metric in detail.coverage.metrics" :key="metric.metric"><b>{{ metricLabel(metric.metric) }}</b><span>观察 {{ metric.observedSamples }} / 可用 {{ metric.eligibleSamples }} 份</span><small>精确 {{ metric.exactFacts }} · 部分 {{ metric.partialFacts }} · 推断 {{ metric.inferredFacts }}</small></article>
            </div>
            <p v-if="detail.summary.usage?.metrics.some(metric => metric.eligibleSamples == null)">部分指标的完整可观测样本数未知；这里只展示已记录事实，不计算缺乏可靠分母的成功率。</p>
            <details><summary>已知限制（{{ detail.coverage.limitations.length }}）</summary><ul><li v-for="limitation in detail.coverage.limitations" :key="limitation">{{ limitation }}</li></ul></details>
          </section>

          <section class="dashboard-panel recent-matches">
            <header><div><h3>最近已结束对局</h3><p>只返回已完成记录；分析权限响应已去除账号、昵称和牌库名，下钻另受对局档案权限保护。</p></div></header>
            <button v-for="match in detail.recentMatches" :key="match.matchId" @click="emit('openMatch', match.matchId)"><span><b>{{ recentMatchup(match) }}</b><small>{{ modeLabel(match.modeId) }} · {{ dateLabel(match.endedUtc || match.startedUtc) }} · {{ recentResult(match) }}</small></span><span class="match-id">{{ match.matchId.slice(0, 12) }}</span><em>查看档案 →</em></button>
            <div v-if="!detail.recentMatches.length" class="empty compact">暂无符合当前切片的已结束对局</div>
          </section>
        </template>
        <div v-else class="empty">选择一张卡查看事实仪表盘</div>
      </main>
    <SandboxCardPicker v-if="pickerOpen" title="选择要分析的卡牌" @select="chooseCard" @close="pickerOpen = false"/>
  </section>
</template>

<style scoped>
.card-analytics,.card-analytics :deep(*){font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.card-analytics{--line:#33434d;--panel:#0e171f;--panel-2:#091117;--muted:#839198;--gold:#e0c46f;--cyan:#64c8ce;display:grid;gap:12px;color:#f1f3ef;color-scheme:dark}.module-header,.filter-panel,.scope-summary,.panel-shell{border:1px solid var(--line);background:var(--panel)}.module-header{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:19px 21px;background:linear-gradient(115deg,#101b24,#15160f)}.module-header small,.card-heading small{color:var(--gold);font-size:13px;font-weight:900;letter-spacing:.12em}.module-header h2{margin:4px 0;font-size:clamp(23px,2.2vw,30px)}.module-header p{max-width:850px;margin:0;color:#99a5a8;line-height:1.65}.module-header button,.filter-panel input,.filter-panel select,.filter-panel button{box-sizing:border-box;min-height:40px;border:1px solid #53636d;background:#060d12;color:#f4f3ed;padding:9px 10px;font-size:14px;font-weight:800}.module-header button{flex:none;color:var(--gold)}.filter-panel{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));align-items:end;gap:9px;padding:13px}.filter-panel .search{grid-column:span 2}.filter-panel label{display:flex;min-width:0;flex-direction:column;gap:6px;color:#aab4b7;font-size:13px;font-weight:900}.filter-panel input,.filter-panel select{width:100%;min-width:0}.filter-panel select option{background:#071016;color:#fff}.filter-panel input:focus,.filter-panel select:focus,.filter-panel button:focus-visible{border-color:var(--cyan);outline:2px solid #64c8ce55;outline-offset:1px}.filter-panel .query{border-color:#9a7c30;background:#31270e;color:#f4d77b}.sample-contract{margin:0;padding:10px 13px;border-left:3px solid var(--gold);background:#18170e;color:#b8beb7;font-size:13px;line-height:1.65}.scope-summary{display:grid;grid-template-columns:repeat(4,1fr);gap:1px;background:var(--line)}.scope-summary article{display:flex;min-height:92px;flex-direction:column;justify-content:flex-end;gap:4px;padding:14px;background:#0a131a}.scope-summary small{color:#8f9da2;font-size:13px;font-weight:900}.scope-summary b{font-size:23px}.scope-summary span{color:#74838a;font-size:13px;line-height:1.45}.analytics-workspace{display:grid;grid-template-columns:minmax(300px,.58fr) minmax(640px,1.42fr);align-items:start;gap:12px}.panel-shell{min-width:0}.card-list{max-height:calc(100vh - 160px);overflow:auto}.card-list>header{position:sticky;z-index:2;top:0;display:flex;align-items:center;justify-content:space-between;padding:13px;background:#081118;border-bottom:1px solid var(--line)}.card-list>header div{display:grid;gap:3px}.card-list>header span,.card-list>header em{color:#77878e;font-size:13px;font-style:normal}.card-row{display:grid;width:100%;grid-template-columns:45px minmax(0,1fr) 72px;align-items:center;gap:10px;padding:10px;border:0;border-bottom:1px solid #283740;background:transparent;color:#fff;text-align:left}.card-row:hover,.card-row.selected{background:#17242d}.card-row.selected{box-shadow:inset 3px 0 var(--gold)}.card-row[data-low-sample="true"]{border-left:3px solid #91772f}.card-row>.l12-card-image{width:45px;height:63px}.card-row>span{display:flex;min-width:0;flex-direction:column;gap:3px}.card-row span b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.card-row span small{color:#718189;font-size:12px;letter-spacing:.03em}.card-row span em{color:#78bdc1;font-size:12px;font-style:normal}.row-numbers{text-align:right}.row-numbers b{font-size:16px}.row-numbers small[data-tone="positive"],[data-tone="positive"]{color:#73ddb0!important}.row-numbers small[data-tone="negative"],[data-tone="negative"]{color:#ef8994!important}[data-tone="neutral"]{color:#e1c56e!important}[data-tone="unavailable"]{color:#73838a!important}.load-more{width:100%;padding:13px;border:0;background:#15222a;color:var(--gold);font-weight:900}.analysis-detail{padding:17px}.card-heading{display:grid;grid-template-columns:112px minmax(0,1fr);align-items:center;gap:17px;padding-bottom:16px;border-bottom:1px solid var(--line)}.card-heading>.l12-card-image{width:112px;height:156px}.card-heading h3{margin:6px 0;font-size:clamp(22px,2vw,28px)}.card-heading p{margin:0;color:#8d9a9f;line-height:1.55}.card-heading em{display:inline-block;margin-top:10px;padding:6px 9px;border:1px solid currentColor;background:#071016;font-size:13px;font-style:normal;font-weight:900}.key-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:12px}.key-metrics article{display:flex;min-height:98px;flex-direction:column;justify-content:flex-end;gap:4px;padding:13px;border:1px solid var(--line);background:#071016}.key-metrics small{color:#84939a;font-size:13px;font-weight:900}.key-metrics b{font-size:22px}.key-metrics span{color:#77868c;font-size:12px;line-height:1.5}.dashboard-pair{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:12px}.dashboard-panel{min-width:0;margin-top:12px;padding:14px;border:1px solid var(--line);background:#071016}.dashboard-pair .dashboard-panel{margin-top:0}.dashboard-panel>header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px;padding-bottom:10px;border-bottom:1px solid #2b3942}.dashboard-panel h3{margin:0;font-size:17px}.dashboard-panel p{margin:4px 0 0;color:#7d8c92;font-size:13px;line-height:1.55}.funnel{display:grid;gap:9px;margin-top:13px}.funnel article>span{display:flex;justify-content:space-between;margin-bottom:5px;font-size:13px}.funnel article>span em{color:var(--gold);font-style:normal}.funnel i,.quantity-bars i{display:block;height:8px;overflow:hidden;background:#18262e}.funnel i b,.quantity-bars i span{display:block;height:100%;min-width:2px;background:linear-gradient(90deg,#32878e,var(--gold))}.settlement-grid{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:13px}.settlement-grid article{display:grid;min-height:78px;align-content:end;gap:4px;padding:11px;border:1px solid #2c3c45;background:#0d1820}.settlement-grid small{color:#89979c;font-size:12px}.settlement-grid b{font-size:20px}.settlement-grid span{color:#6f8087;font-size:12px}.quantity-bars,.turn-chart{display:grid;gap:9px;margin-top:13px}.quantity-bars article{display:grid;grid-template-columns:48px minmax(80px,1fr) minmax(120px,auto);align-items:center;gap:9px;font-size:12px}.quantity-bars em{color:#89979c;font-style:normal;text-align:right}.turn-chart{max-height:320px;overflow:auto}.turn-chart article{display:grid;grid-template-columns:68px 1fr 1fr;align-items:center;gap:8px;font-size:12px}.turn-chart article>div{position:relative;display:flex;height:18px;align-items:center;background:#14222a}.turn-chart i{height:100%;min-width:1px}.turn-chart i.draw{background:#47aeb5}.turn-chart i.play{background:#d4b354}.turn-chart article span{position:absolute;right:5px;color:#fff;font-size:11px;font-weight:900}.legend{display:flex;gap:10px;color:#8d999e;font-size:12px}.legend span::before{content:'';display:inline-block;width:8px;height:8px;margin-right:5px}.legend .draw::before{background:#47aeb5}.legend .play::before{background:#d4b354}.heat-scroll{margin-top:12px;overflow:auto}.heat-scroll table{width:100%;min-width:620px;border-collapse:collapse;font-size:12px}.heat-scroll th,.heat-scroll td{min-width:100px;padding:9px;border:1px solid #2c3b44;text-align:center}.heat-scroll th{background:#101c24;color:#b9c3c5}.heat-scroll td{background:#0c171e}.heat-scroll td[data-tone="positive"]{background:#0d342c}.heat-scroll td[data-tone="negative"]{background:#35141c}.heat-scroll td[data-tone="neutral"]{background:#302c17}.heat-scroll td b,.heat-scroll td span,.heat-scroll td small{display:block}.heat-scroll td b{font-size:15px}.heat-scroll td span{margin-top:3px;color:#90a0a5}.heat-scroll td small{margin-top:3px;color:currentColor}.breakdown-head,.breakdowns>article{display:grid;grid-template-columns:105px minmax(150px,1.3fr) 80px 80px 70px 70px 75px;align-items:center;gap:8px;padding:9px}.breakdown-head{margin-top:8px;color:#74838a;font-size:12px;font-weight:900}.breakdowns>article{border-top:1px solid #293840;font-size:12px}.breakdowns>article small{color:#70c1c6;font-weight:900}.breakdowns>article strong{text-align:right}.quality-panel>header>strong{color:var(--gold);font-size:13px}.coverage-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:8px;margin-top:12px}.coverage-grid article{display:grid;gap:5px;padding:11px;border:1px solid #2d3c44;background:#0c171e}.coverage-grid b{font-size:13px}.coverage-grid span,.coverage-grid small{color:#829198;font-size:12px;line-height:1.45}.quality-panel details{margin-top:11px;border:1px solid #2c3b43;background:#0a141a}.quality-panel summary{padding:10px;color:#c6b777;font-size:13px;cursor:pointer}.quality-panel ul{margin:0;padding:0 28px 12px;color:#87969b;font-size:12px;line-height:1.65}.recent-matches>button{display:grid;width:100%;grid-template-columns:minmax(0,1fr) 108px auto;align-items:center;gap:9px;padding:10px;border:0;border-bottom:1px solid #293840;background:transparent;color:#fff;text-align:left}.recent-matches>button:hover,.recent-matches>button:focus-visible{background:#15232b;outline:1px solid var(--cyan);outline-offset:-1px}.recent-matches>button span:first-child{display:flex;min-width:0;flex-direction:column;gap:4px}.recent-matches>button small{color:#74858b;font-size:12px}.recent-matches .match-id{overflow:hidden;color:#75909b;font-size:12px;text-overflow:ellipsis}.recent-matches em{color:var(--gold);font-size:12px;font-style:normal}.empty{display:grid;min-height:170px;place-items:center;color:#718189;text-align:center}.empty.compact{min-height:80px}button:disabled{cursor:not-allowed;opacity:.5}
.key-metrics{grid-template-columns:repeat(5,1fr)}
.key-metrics article,.coverage-grid article{min-width:0;overflow-wrap:anywhere}
.breakdowns>article>b{min-width:0;overflow-wrap:anywhere}
.breakdown-head,.breakdowns>article{grid-template-columns:105px minmax(150px,1.3fr) 80px 80px 70px 70px 105px}
.breakdowns>article strong{display:grid;gap:2px}.breakdowns>article strong small{font-size:10px;font-weight:700}
.module-tabs{display:grid;grid-template-columns:1fr 1fr;border:1px solid var(--line);background:#081118}.module-tabs button{min-height:44px;border:0;border-right:1px solid var(--line);background:transparent;color:#85949a;font-size:14px;font-weight:900}.module-tabs button:last-child{border-right:0}.module-tabs button.active{background:#1b2a32;color:var(--gold);box-shadow:inset 0 -3px var(--gold)}.module-tabs button:focus-visible,.list-sort button:focus-visible,.pagination button:focus-visible{outline:2px solid var(--cyan);outline-offset:-2px}
.filter-panel .master-filter{grid-column:span 2}.filter-panel .card-choice{display:grid;grid-column:span 2;grid-template-columns:46px minmax(0,1fr);align-items:center;gap:10px;text-align:left}.card-choice>.l12-card-image{width:46px;height:64px}.card-choice>span{display:grid;min-width:0;gap:2px}.card-choice small{color:var(--gold);font-size:12px}.card-choice b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.card-choice em{color:#7f8d93;font-size:12px;font-style:normal}.range-filter{display:grid;grid-column:span 3;grid-template-columns:repeat(4,minmax(92px,1fr));gap:5px;min-width:0;margin:0;padding:6px 8px 8px;border:1px solid #40515b}.range-filter legend{padding:0 5px;color:#aab4b7;font-size:13px;font-weight:900}.range-filter button{min-height:34px;padding:6px}.range-filter button.active{border-color:var(--gold);background:#302710;color:#f2d77f}.behavior-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:8px}.behavior-metrics article{display:grid;min-width:0;gap:4px;padding:12px;border:1px solid #2c3c45;background:#0b1820}.behavior-metrics small,.behavior-metrics span{color:#829198;font-size:12px}.behavior-metrics b{font-size:19px}
.card-list{max-height:none;overflow-x:auto}.card-list>header{position:static}.list-sort{display:grid;min-width:900px;grid-template-columns:minmax(285px,1fr) repeat(5,minmax(82px,.45fr));border-bottom:1px solid var(--line);background:#101c24}.list-sort button{min-height:39px;padding:7px;border:0;border-right:1px solid #293840;background:transparent;color:#aeb9bc;font-size:12px;font-weight:900;text-align:center}.list-sort button:first-child{text-align:left}.card-list .card-row{min-width:900px;grid-template-columns:45px minmax(220px,1fr) repeat(5,minmax(82px,.45fr))}.card-list .card-row>span:not(:nth-child(2)){display:block;text-align:center;font-size:13px;font-weight:900}.pagination{position:sticky;left:0;display:flex;align-items:center;justify-content:center;gap:14px;padding:10px;border-top:1px solid var(--line);background:#091218}.pagination button{min-height:34px;padding:6px 14px;border:1px solid #53636d;background:#111d24;color:#e6e8e4;font-weight:900}.pagination span{color:#829198;font-size:13px}
@media(max-width:1500px){.analytics-workspace{grid-template-columns:minmax(290px,.52fr) minmax(600px,1.48fr)}.filter-panel{grid-template-columns:repeat(4,minmax(140px,1fr))}.filter-panel .search{grid-column:span 2}}
@media(max-width:1120px){.analytics-workspace{grid-template-columns:1fr}.key-metrics,.behavior-metrics{grid-template-columns:1fr 1fr}.scope-summary{grid-template-columns:1fr 1fr}.range-filter{grid-column:span 2}}
@media(max-width:820px){.dashboard-pair{grid-template-columns:1fr}.breakdowns{overflow:auto}.breakdown-head,.breakdowns>article{min-width:720px}.module-header{align-items:flex-start}.module-header button{margin-top:3px}}
@media(max-width:650px){.filter-panel{grid-template-columns:1fr 1fr}.filter-panel .search,.filter-panel .master-filter,.filter-panel .card-choice,.range-filter{grid-column:1/-1}.range-filter{grid-template-columns:1fr 1fr}.scope-summary,.key-metrics,.behavior-metrics{grid-template-columns:1fr 1fr}.analysis-detail{padding:12px}.card-heading{grid-template-columns:84px minmax(0,1fr)}.card-heading>.l12-card-image{width:84px;height:117px}.settlement-grid{grid-template-columns:1fr 1fr}.quantity-bars article{grid-template-columns:44px 1fr}.quantity-bars em{grid-column:1/-1;text-align:left}.recent-matches>button{grid-template-columns:1fr auto}.recent-matches .match-id{display:none}}
@media(max-width:430px){.filter-panel,.scope-summary,.key-metrics,.behavior-metrics,.settlement-grid{grid-template-columns:1fr}.module-header{display:grid}.module-header button{width:100%}.turn-chart article{grid-template-columns:62px 1fr}.turn-chart article>div:last-child{grid-column:2}.card-heading{grid-template-columns:72px minmax(0,1fr)}.card-heading>.l12-card-image{width:72px;height:100px}}
.heat-scroll{max-height:520px}.heat-scroll th{position:sticky;top:0;z-index:1}.breakdowns :deep(article){display:grid;grid-template-columns:105px minmax(150px,1.3fr) 80px 80px 70px 70px 105px;align-items:center;gap:8px;padding:9px;border-top:1px solid #293840;font-size:12px}.breakdowns :deep(article>b){min-width:0;overflow-wrap:anywhere}.breakdowns :deep(article small){color:#70c1c6;font-weight:900}.breakdowns :deep(article strong){display:grid;gap:2px;text-align:right}.breakdowns :deep(article strong small){font-size:10px;font-weight:700}@media(max-width:820px){.breakdowns :deep(article){min-width:720px}}
</style>
