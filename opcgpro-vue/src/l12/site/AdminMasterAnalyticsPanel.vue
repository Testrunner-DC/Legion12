<script setup lang="ts">
import { vHorizontalScrollArrows } from '../horizontalScrollArrows'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import ScopedDisasterAnalytics from './ScopedDisasterAnalytics.vue'
import MasterDisasterStrategyPanel from './MasterDisasterStrategyPanel.vue'
import CardImage from '@/l12/CardImage.vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { masterProfileUrl } from '@/l12/specialAssets'
import { adminApi, authState, platformState, getEffectiveOperationsPolicy, type AdminMasterAnalyticsReport } from '@/l12/platform'
import DeckSnapshotViewer from './DeckSnapshotViewer.vue'
import MasterMatchupMatrix, { type MasterMatchupMatrixCell, type MasterMatchupMatrixMaster } from './MasterMatchupMatrix.vue'
import StatisticsScope from './StatisticsScope.vue'

const emit = defineEmits<{ notice: [message: string] }>()
const cards = ref<DeckCard[]>([])
const report = ref<AdminMasterAnalyticsReport | null>(null)
const selectedMasterId = ref('')
const popularDeck = ref<AdminMasterAnalyticsReport['popularDecks'][number] | null>(null)
const loading = ref(false)
const errorText = ref('')
const sort = ref<'usage-rate'|'win-rate'>('usage-rate')
const range = ref<'7d'|'30d'|'season'>('30d')
const effectVersion = ref<'current'|'all'>('current')
const opponentMasterId = ref('')
const initiative = ref('')
const currentSeason = ref<{ id: string; name: string } | null>(null)
type MasterAppliedScope = {
  from: string
  to: string
  masterId: string
  range: typeof range.value
  sort: typeof sort.value
  seasonId: string
  seasonName: string
  effectVersion: typeof effectVersion.value
  opponentMasterId: string
  initiative: string
  accountKey: string
}
const appliedScope = ref<MasterAppliedScope | null>(null)
const today = new Date()
const dateText = (value: Date) => `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
const from = ref(dateText(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29)))
const to = ref(dateText(today))
const cardById = computed(() => new Map(cards.value.map(card => [card.id, card])))
const selected = computed(() => appliedScope.value?.masterId === selectedMasterId.value
  && report.value?.selectedMasterId === selectedMasterId.value
  ? report.value.items.find(item => item.masterId === selectedMasterId.value) : undefined)
const maximumTrend = computed(() => Math.max(1, ...(report.value?.trend.map(item => item.samples) || [1])))
const masterScopeSummary = computed(() => appliedScope.value?.seasonId
  ? `本赛季 · ${appliedScope.value.seasonName || appliedScope.value.seasonId}`
  : appliedScope.value ? `${appliedScope.value.from || '未设置开始日'} 至 ${appliedScope.value.to || '未设置结束日'}` : '')
const masterScopeSample = computed(() => report.value
  ? `${report.value.items.reduce((total, item) => total + item.participantSamples, 0)} 份参赛方样本 · ${appliedScope.value?.effectVersion === 'all' ? '全部已记录卡效版本' : '查询时当前卡效版本'}` : '')
const masterScopeItems = computed(() => [
  '统计单位为参赛方 × 对局；只计入已结束、胜负明确、卡效版本有效且有精确赛后构筑快照的排位。',
  appliedScope.value?.effectVersion === 'all'
    ? '卡效版本按引擎构建＋卡池标识；全部版本为跨版本描述性观察，主宰总览未按版本分层，不能据此判断平衡变化。'
    : '卡效版本按引擎构建＋卡池标识；当前版本只纳入查询时该版本的记录，不合并旧版本事实。',
  '作废／暂扣对局及禁用／删除账号参与的对局不计；日期范围按对局开始时间筛选。',
  '先手与后手分别使用各自样本分母；不足 30 份时隐藏胜率，30 份仅为展示提醒。',
  '无样本或低样本不代表记录丢失；切换版本会改变纳入范围。接口未返回样本首末时间及排除数量。',
])
const matrixRows = computed<MasterMatchupMatrixMaster[]>(() => (report.value?.items ?? []).map((item, index) => ({
  id: item.masterId, name: masterName(item.masterId), imageUrl: masterProfileUrl(item.masterId), rank: index + 1, winRate: item.winRate,
})))
const matrixCells = computed<MasterMatchupMatrixCell[]>(() => (report.value?.matchups ?? []).map(item => ({
  masterId: item.masterId, opponentMasterId: item.opponentMasterId, samples: item.samples, winRate: item.winRate,
  firstSamples: item.firstSamples ?? undefined, firstWins: item.firstWins ?? undefined,
  secondSamples: item.secondSamples ?? undefined, secondWins: item.secondWins ?? undefined,
})))
function percent(value?: number | null) { return typeof value === 'number' ? `${(value * 100).toFixed(1)}%` : '—' }
function masterName(id: string) { return cardById.value.get(id)?.nameZh || id }
function setRange(next: typeof range.value) {
  range.value = next
  const days = next === '7d' ? 7 : 30
  from.value = next === 'season' ? '' : dateText(new Date(today.getFullYear(), today.getMonth(), today.getDate() - days + 1))
  to.value = next === 'season' ? '' : dateText(today)
  void load()
}
let loadRequest = 0
let disposed = false
function currentScope(): MasterAppliedScope {
  const seasonId = range.value === 'season' ? currentSeason.value?.id || '' : ''
  return { from: from.value, to: to.value, masterId: selectedMasterId.value, range: range.value,
    sort: sort.value, seasonId, seasonName: seasonId === currentSeason.value?.id ? currentSeason.value?.name || '' : '',
    effectVersion: effectVersion.value, opponentMasterId: opponentMasterId.value, initiative: initiative.value,
    accountKey: `${platformState.account?.id || ''}:${platformState.account?.permissionVersion || ''}:${authState.verified}` }
}
function invalidateRequest() {
  ++loadRequest
  report.value = null
  appliedScope.value = null
  popularDeck.value = null
  errorText.value = ''
  loading.value = false
}
watch([from, to, selectedMasterId, range, sort, effectVersion, opponentMasterId, initiative,
  () => currentSeason.value?.id, () => currentSeason.value?.name], invalidateRequest, { flush: 'sync' })
watch([() => platformState.account?.id, () => platformState.account?.permissionVersion,
  () => platformState.token, () => authState.verified], () => {
  invalidateRequest()
}, { flush: 'sync' })
async function load() {
  if (disposed) return
  invalidateRequest()
  const request = loadRequest
  const nextScope = currentScope()
  const scopeKey = JSON.stringify(nextScope)
  const isCurrent = () => !disposed && request === loadRequest && scopeKey === JSON.stringify(currentScope())
  const query = { from: nextScope.from, to: nextScope.to, masterId: nextScope.masterId,
    seasonId: nextScope.seasonId, effectVersion: nextScope.effectVersion, minimumSample: 1, sort: nextScope.sort,
    opponentMasterId: nextScope.opponentMasterId, initiative: nextScope.initiative }
  loading.value = true
  try {
    const next = await adminApi.masterAnalytics(query)
    if (!isCurrent()) return
    if ((next.selectedMasterId || '') !== nextScope.masterId) throw new Error('主宰数据响应与当前查询不一致，请刷新重试')
    report.value = next
    appliedScope.value = nextScope
  } catch (error) {
    if (!isCurrent()) return
    errorText.value = error instanceof Error ? error.message : '主宰数据加载失败'
    emit('notice', errorText.value)
  } finally { if (isCurrent()) loading.value = false }
}
function chooseMaster(id: string) { selectedMasterId.value = id; void load() }
function choosePopularDeck(deck: AdminMasterAnalyticsReport['popularDecks'][number]) {
  if (!disposed && !loading.value && selected.value && appliedScope.value
    && JSON.stringify(appliedScope.value) === JSON.stringify(currentScope())
    && report.value?.popularDecks.includes(deck)) popularDeck.value = deck
}
onBeforeUnmount(() => { disposed = true; invalidateRequest() })
onMounted(async () => {
  const [catalog, policy] = await Promise.allSettled([loadDeckCatalog(), getEffectiveOperationsPolicy()])
  if (disposed) return
  if (catalog.status === 'fulfilled') cards.value = catalog.value
  if (policy.status === 'fulfilled') currentSeason.value = { id: policy.value.season.id, name: policy.value.season.name }
  await load()
})
</script>

<template>
  <section class="master-analytics">
    <div class="master-tools"><fieldset><legend>统计时间</legend><button :class="{ active: range === '7d' }" @click="setRange('7d')">近 7 天</button><button :class="{ active: range === '30d' }" @click="setRange('30d')">近 30 天</button><button :class="{ active: range === 'season' }" :disabled="!currentSeason" @click="setRange('season')">本赛季</button></fieldset><label>卡效版本<select v-model="effectVersion" aria-label="卡效版本" @change="load()"><option value="current">当前版本</option><option value="all">全部已记录版本</option></select></label><label>列表排序<select v-model="sort" @change="load()"><option value="usage-rate">使用率</option><option value="win-rate">胜率</option></select></label><button class="refresh" :disabled="loading" @click="load()">{{ loading ? '读取中…' : '刷新' }}</button></div>
    <p class="version-note">{{ effectVersion === 'current' ? '当前范围只纳入查询时当前“引擎构建＋卡池”版本；切换后无样本不代表丢数。' : '全部版本仅供跨版本描述性观察；主宰总览未按版本分层，不作平衡变化结论。' }}</p>
    <div class="master-tools scope-tools"><label>对方主宰<select v-model="opponentMasterId" aria-label="对方主宰" @change="load()"><option value="">全部主宰</option><option v-for="card in cards.filter(item => item.cardType === 'master')" :key="card.id" :value="card.id">{{ card.nameZh }}</option></select></label><label>先后手<select v-model="initiative" aria-label="先后手" @change="load()"><option value="">全部</option><option value="first">先手</option><option value="second">后手</option></select></label></div>
    <p v-if="errorText" role="alert">{{ errorText }}</p>
    <StatisticsScope v-if="report && appliedScope" :summary="masterScopeSummary" :sample="masterScopeSample" :items="masterScopeItems"/>
    <section class="master-list panel"><header><div><h3>主宰总览</h3><p>使用率按参赛方样本计算；构筑占比按去重构筑快照计算。低于30份的胜率仅供参考。</p></div></header><div class="master-head"><span>主宰</span><span>使用率 / 构筑占比</span><span>整体胜率</span><span>平均时长</span><span>先手 / 后手</span><span>样本</span></div><button v-for="item in report?.items || []" :key="item.masterId" :class="{ selected: selectedMasterId === item.masterId, low: item.participantSamples < 30 }" @click="chooseMaster(item.masterId)"><CardImage :card-id="item.masterId" :legacy-url="cardById.get(item.masterId)?.imageUrl" :alt="masterName(item.masterId)" intent="thumb"/><b>{{ masterName(item.masterId) }}</b><span><small>使用率 / 构筑占比</small><em>{{ percent(item.usageRate) }} / {{ percent(item.deckShare) }}</em></span><span><small>整体胜率</small><em>{{ item.participantSamples >= 30 ? percent(item.winRate) : '—' }}</em></span><span><small>平均时长</small><em>{{ Math.round(item.averageDurationSeconds / 60) }} 分钟</em></span><span><small>先手 / 后手</small><em>{{ item.firstSamples >= 30 ? percent(item.firstWinRate) : '—' }} / {{ item.secondSamples >= 30 ? percent(item.secondWinRate) : '—' }}</em></span><span><small>样本</small><em>{{ item.participantSamples }}</em></span></button><p v-if="loading" class="empty">正在读取主宰数据…</p><p v-else-if="report && !report.items.length" class="empty">所选时段及卡效版本暂无符合口径的排位样本；不代表旧版本记录丢失</p></section>
    <template v-if="selected">
      <section class="selected-summary"><article><small>当前主宰</small><b>{{ masterName(selected.masterId) }}</b><span>以 {{ selected.participantSamples >= 30 ? percent(selected.winRate) : '—' }} 整体胜率作为主宰内单卡比较基准</span></article><article><small>参赛方 / 去重构筑</small><b>{{ selected.participantSamples }} / {{ selected.distinctDecks }}</b><span>统计单位与卡牌数据一致</span></article><article><small>先手 / 后手胜率</small><b>{{ selected.firstSamples >= 30 ? percent(selected.firstWinRate) : '—' }} / {{ selected.secondSamples >= 30 ? percent(selected.secondWinRate) : '—' }}</b><span>{{ selected.firstSamples }} / {{ selected.secondSamples }} 份样本</span></article></section>
      <section class="panel trend"><header><h3>胜率与使用趋势</h3><p>每日柱高表示样本量，标签显示当日胜率。</p></header><div v-horizontal-scroll-arrows="'主宰逐日趋势'" class="trend-bars"><article v-for="day in report?.trend || []" :key="day.date"><i :style="{ height: `${Math.max(3, day.samples / maximumTrend * 100)}%` }"></i><b>{{ day.samples >= 30 ? percent(day.winRate) : '—' }}</b><small>{{ day.date.slice(5) }} · {{ day.samples }}</small></article></div></section>
      <section class="panel"><header><h3>主宰对阵矩阵</h3><p>每格为行主宰对列主宰的胜率；少于30份显示“—”。横向滚动可查看全部对阵。</p></header><div v-horizontal-scroll-arrows="'主宰对阵矩阵'" class="matrix-scroll-access" tabindex="0" aria-label="主宰对阵矩阵，可横向滚动"><MasterMatchupMatrix :masters="matrixRows" :cells="matrixCells" :minimum-sample="30"/></div></section>
      <section class="panel master-cards"><header><h3>主宰内单卡表现</h3><p>上手提升比较同一批携带者“抽到 vs 未抽到”，基准不是固定50%。</p></header><article v-for="item in report?.cards?.items || []" :key="item.cardId"><CardImage :card-id="item.cardId" :legacy-url="cardById.get(item.cardId)?.imageUrl" :alt="cardById.get(item.cardId)?.nameZh || item.cardId" intent="thumb"/><b>{{ cardById.get(item.cardId)?.nameZh || item.cardId }}</b><span>入构 {{ percent(item.inclusionRate) }}</span><span>携带 {{ percent(item.winRate) }}</span><span>抽到 {{ item.gihSamples >= 30 ? percent(item.gihWinRate) : '—' }}</span><span>未上手 {{ item.gnsSamples >= 30 ? percent(item.gnsWinRate) : '—' }}</span><strong>提升 {{ item.gihSamples >= 30 && item.gnsSamples >= 30 ? percent(item.inHandWinRateDelta) : '—' }}</strong></article></section>
      <MasterDisasterStrategyPanel v-if="appliedScope" :scope="{ from: appliedScope.from, to: appliedScope.to, masterId: appliedScope.masterId, seasonId: appliedScope.seasonId, effectVersion: appliedScope.effectVersion }" :cards="cards" :initial-opponent="appliedScope.opponentMasterId" :initial-initiative="appliedScope.initiative" />
      <section class="panel popular"><header><h3>热门构筑</h3><p>按完全一致的赛后构筑快照聚合；查看构筑可核对组成。</p></header><article v-for="(deck, index) in report?.popularDecks || []" :key="deck.signature"><span><b>构筑 {{ index + 1 }}</b><small>{{ deck.samples }} 份 · 胜率 {{ deck.samples >= 30 ? percent(deck.winRate) : '—' }}</small></span><button type="button" @click="choosePopularDeck(deck)">查看构筑</button></article><p v-if="!report?.popularDecks.length" class="empty compact">暂无符合当前范围的构筑快照</p></section>
    </template>
    <DeckSnapshotViewer v-if="popularDeck && selected" :entries="popularDeck.cards" :catalog="cards" :master-id="selected.masterId" :title="`${masterName(selected.masterId)} · 热门构筑`" :deck-name="`${masterName(selected.masterId)} 热门构筑`" eyebrow="统计构筑快照" @close="popularDeck = null" @notice="emit('notice', $event)">
      <ScopedDisasterAnalytics v-if="appliedScope" :key="popularDeck.signature" :scope="{ from: appliedScope.from, to: appliedScope.to, masterId: appliedScope.masterId, opponentMasterId: appliedScope.opponentMasterId, initiative: appliedScope.initiative, seasonId: appliedScope.seasonId, effectVersion: appliedScope.effectVersion }" :deck-signature="popularDeck.signature" :cards="cards" title="所选热门构筑的天灾禁选分析" />
    </DeckSnapshotViewer>
  </section>
</template>

<style scoped>
.version-note{margin:0;padding:10px 12px;color:#aab4b7;font-size:12px;line-height:1.65;overflow-wrap:anywhere}
.master-analytics{--line:#33434d;display:grid;min-width:0;max-width:100%;gap:12px}.master-analytics>*,.panel,.master-tools,.selected-summary article{box-sizing:border-box;min-width:0;max-width:100%;border:1px solid var(--line);background:#0e171f}.master-tools{display:flex;align-items:end;gap:10px;padding:12px}.master-tools fieldset{display:flex;min-width:0;gap:5px;margin:0;padding:5px 7px 7px;border:1px solid #485963}.master-tools legend,.master-tools label{color:#95a2a6;font-size:12px;font-weight:900}.master-tools label{display:grid;min-width:0;gap:5px}.master-tools button,.master-tools select{max-width:100%;min-height:35px;border:1px solid #53636d;background:#071016;color:#fff;padding:6px 10px;font-weight:900}.master-tools button.active{border-color:#ddc36e;color:#ddc36e}.master-tools .refresh{margin-left:auto;color:#ddc36e}.panel{padding:14px}.panel>header{min-width:0;padding-bottom:10px;border-bottom:1px solid #2b3942}.panel h3{margin:0}.panel p{margin:4px 0 0;color:#849298;font-size:12px;overflow-wrap:anywhere}.master-head,.master-list>button{display:grid;grid-template-columns:48px minmax(130px,1fr) repeat(5,minmax(100px,.8fr));align-items:center;gap:8px}.master-head{padding:9px 10px;color:#819097;font-size:11px}.master-head span:first-child{grid-column:1/3}.master-list>button{width:100%;padding:8px 10px;border:0;border-top:1px solid #293840;background:transparent;color:#fff;text-align:left}.master-list>button:hover,.master-list>button.selected{background:#17242d}.master-list>button.selected{box-shadow:inset 3px 0 #ddc36e}.master-list>button.low{opacity:.72}.master-list>button>span>small{display:none}.master-list>button>span>em{font-style:normal}.master-list :deep(.l12-card-image){width:42px;height:58px}.selected-summary{display:grid;min-width:0;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.selected-summary article{display:grid;gap:4px;padding:13px}.selected-summary small,.selected-summary span{color:#829096;font-size:12px}.selected-summary b{font-size:18px}.trend-bars{display:grid;height:180px;max-width:100%;grid-auto-flow:column;grid-auto-columns:56px;justify-content:start;align-items:end;gap:8px;padding-top:15px;overflow-x:auto;overflow-y:hidden}.trend-bars article{display:grid;width:56px;height:100%;grid-template-rows:1fr auto auto;align-items:end;text-align:center}.trend-bars i{min-height:3px;background:linear-gradient(#53b8c0,#d5b85e)}.trend-bars b{font-size:11px}.trend-bars small{color:#7f8d92;font-size:10px}.master-cards article{display:grid;grid-template-columns:42px minmax(140px,1fr) repeat(5,minmax(80px,.7fr));align-items:center;gap:8px;padding:8px;border-top:1px solid #293840;font-size:12px}.master-cards :deep(.l12-card-image){width:38px;height:53px}.master-cards strong{color:#dfc46e}.popular>article{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:11px;border-bottom:1px solid #293840}.popular>article>span{display:grid;min-width:0;gap:3px}.popular>article small{color:#7f8d92}.popular>article button{min-height:40px;padding:8px 12px;border:1px solid #7e6b36;background:#231e10;color:#f0d276;font-weight:900}.empty{padding:24px;color:#809097;text-align:center}.empty.compact{min-height:72px}@media(max-width:900px){.master-head{display:none}.master-list>button{grid-template-columns:42px minmax(0,1fr)}.master-list>button>span{grid-column:2;display:flex;flex-direction:column;align-items:flex-start;gap:1px;min-width:0;color:#dce1de}.master-list>button>span>small{display:block;color:#829096}.master-list>button>span>em{display:block;white-space:normal;color:#fff}.selected-summary{grid-template-columns:1fr}.master-cards article{grid-template-columns:38px minmax(0,1fr) minmax(0,1fr)}.master-cards article span,.master-cards article strong{grid-column:2/-1}.master-tools{flex-wrap:wrap}.master-tools .refresh{margin-left:0}.popular>article{align-items:stretch;flex-direction:column}.popular>article button{width:100%}}
.matrix-scroll-access{box-sizing:border-box;width:100%;min-width:0;max-width:100%;max-height:68vh;overflow:auto;overscroll-behavior-inline:contain;scrollbar-gutter:stable}.matrix-scroll-access:focus-visible{outline:2px solid #64c8ce;outline-offset:2px}.matrix-scroll-access :deep(.master-matchup-matrix){max-height:none;overflow:visible}
</style>
