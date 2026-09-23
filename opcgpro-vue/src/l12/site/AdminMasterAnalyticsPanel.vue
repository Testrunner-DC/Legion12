<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { adminApi, getEffectiveOperationsPolicy, type AdminMasterAnalyticsReport } from '@/l12/platform'

const emit = defineEmits<{ notice: [message: string] }>()
const cards = ref<DeckCard[]>([])
const report = ref<AdminMasterAnalyticsReport | null>(null)
const selectedMasterId = ref('')
const loading = ref(false)
const sort = ref<'usage-rate'|'win-rate'>('usage-rate')
const range = ref<'7d'|'30d'|'season'>('30d')
const currentSeason = ref<{ id: string; name: string } | null>(null)
const today = new Date()
const dateText = (value: Date) => `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
const from = ref(dateText(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29)))
const to = ref(dateText(today))
const cardById = computed(() => new Map(cards.value.map(card => [card.id, card])))
const selected = computed(() => report.value?.items.find(item => item.masterId === selectedMasterId.value))
const matrixMasters = computed(() => report.value?.items.map(item => item.masterId) || [])
const maximumTrend = computed(() => Math.max(1, ...(report.value?.trend.map(item => item.samples) || [1])))
function percent(value?: number | null) { return typeof value === 'number' ? `${(value * 100).toFixed(1)}%` : '—' }
function masterName(id: string) { return cardById.value.get(id)?.nameZh || id }
function matchup(masterId: string, opponentMasterId: string) { return report.value?.matchups.find(row => row.masterId === masterId && row.opponentMasterId === opponentMasterId) }
function setRange(next: typeof range.value) {
  range.value = next
  const days = next === '7d' ? 7 : 30
  from.value = next === 'season' ? '' : dateText(new Date(today.getFullYear(), today.getMonth(), today.getDate() - days + 1))
  to.value = next === 'season' ? '' : dateText(today)
  void load()
}
async function load(masterId = selectedMasterId.value) {
  loading.value = true
  try {
    report.value = await adminApi.masterAnalytics({ from: from.value, to: to.value, masterId,
      seasonId: range.value === 'season' ? currentSeason.value?.id : '', effectVersion: 'current', minimumSample: 1, sort: sort.value })
  } catch (error) { emit('notice', error instanceof Error ? error.message : '主宰数据加载失败') }
  finally { loading.value = false }
}
function chooseMaster(id: string) { selectedMasterId.value = id; void load(id) }
onMounted(async () => {
  const [catalog, policy] = await Promise.allSettled([loadDeckCatalog(), getEffectiveOperationsPolicy()])
  if (catalog.status === 'fulfilled') cards.value = catalog.value
  if (policy.status === 'fulfilled') currentSeason.value = { id: policy.value.season.id, name: policy.value.season.name }
  await load()
})
</script>

<template>
  <section class="master-analytics">
    <div class="master-tools"><fieldset><legend>统计时间</legend><button :class="{ active: range === '7d' }" @click="setRange('7d')">近 7 天</button><button :class="{ active: range === '30d' }" @click="setRange('30d')">近 30 天</button><button :class="{ active: range === 'season' }" :disabled="!currentSeason" @click="setRange('season')">本赛季</button></fieldset><label>列表排序<select v-model="sort" @change="load()"><option value="usage-rate">使用率</option><option value="win-rate">胜率</option></select></label><button class="refresh" :disabled="loading" @click="load()">{{ loading ? '读取中…' : '刷新' }}</button></div>
    <section class="master-list panel"><header><div><h3>主宰总览</h3><p>使用率按参赛方样本计算；构筑占比按去重构筑快照计算。低于30份的胜率仅供参考。</p></div></header><div class="master-head"><span>主宰</span><span>使用率 / 构筑占比</span><span>整体胜率</span><span>平均时长</span><span>先手 / 后手</span><span>样本</span></div><button v-for="item in report?.items || []" :key="item.masterId" :class="{ selected: selectedMasterId === item.masterId, low: item.participantSamples < 30 }" @click="chooseMaster(item.masterId)"><CardImage :card-id="item.masterId" :legacy-url="cardById.get(item.masterId)?.imageUrl" :alt="masterName(item.masterId)" intent="thumb"/><b>{{ masterName(item.masterId) }}</b><span><small>使用率 / 构筑占比</small><em>{{ percent(item.usageRate) }} / {{ percent(item.deckShare) }}</em></span><span><small>整体胜率</small><em>{{ item.participantSamples >= 30 ? percent(item.winRate) : '—' }}</em></span><span><small>平均时长</small><em>{{ Math.round(item.averageDurationSeconds / 60) }} 分钟</em></span><span><small>先手 / 后手</small><em>{{ item.firstSamples >= 30 ? percent(item.firstWinRate) : '—' }} / {{ item.secondSamples >= 30 ? percent(item.secondWinRate) : '—' }}</em></span><span><small>样本</small><em>{{ item.participantSamples }}</em></span></button><p v-if="!report?.items.length" class="empty">所选时段暂无符合新版口径的排位样本</p></section>
    <template v-if="selected">
      <section class="selected-summary"><article><small>当前主宰</small><b>{{ masterName(selected.masterId) }}</b><span>以 {{ selected.participantSamples >= 30 ? percent(selected.winRate) : '—' }} 整体胜率作为主宰内单卡比较基准</span></article><article><small>参赛方 / 去重构筑</small><b>{{ selected.participantSamples }} / {{ selected.distinctDecks }}</b><span>统计单位与卡牌数据一致</span></article><article><small>先手 / 后手胜率</small><b>{{ selected.firstSamples >= 30 ? percent(selected.firstWinRate) : '—' }} / {{ selected.secondSamples >= 30 ? percent(selected.secondWinRate) : '—' }}</b><span>{{ selected.firstSamples }} / {{ selected.secondSamples }} 份样本</span></article></section>
      <section class="panel trend"><header><h3>胜率与使用趋势</h3><p>每日柱高表示样本量，标签显示当日胜率。</p></header><div class="trend-bars"><article v-for="day in report?.trend || []" :key="day.date"><i :style="{ height: `${Math.max(3, day.samples / maximumTrend * 100)}%` }"></i><b>{{ day.samples >= 30 ? percent(day.winRate) : '—' }}</b><small>{{ day.date.slice(5) }} · {{ day.samples }}</small></article></div></section>
      <section class="panel"><header><h3>主宰对阵矩阵</h3><p>每格为行主宰对列主宰的胜率；少于30份显示“—”。</p></header><div class="matrix"><table><thead><tr><th>使用方 \ 对方</th><th v-for="id in matrixMasters" :key="id">{{ masterName(id) }}</th></tr></thead><tbody><tr v-for="mine in matrixMasters" :key="mine"><th>{{ masterName(mine) }}</th><td v-for="enemy in matrixMasters" :key="enemy"><template v-if="matchup(mine, enemy)"><b>{{ percent(matchup(mine, enemy)?.winRate) }}</b><small>{{ matchup(mine, enemy)?.samples }} 份</small></template><span v-else>—</span></td></tr></tbody></table></div></section>
      <section class="panel master-cards"><header><h3>主宰内单卡表现</h3><p>上手提升比较同一批携带者“抽到 vs 未抽到”，基准不是固定50%。</p></header><article v-for="item in report?.cards?.items || []" :key="item.cardId"><CardImage :card-id="item.cardId" :legacy-url="cardById.get(item.cardId)?.imageUrl" :alt="cardById.get(item.cardId)?.nameZh || item.cardId" intent="thumb"/><b>{{ cardById.get(item.cardId)?.nameZh || item.cardId }}</b><span>入构 {{ percent(item.inclusionRate) }}</span><span>携带 {{ percent(item.winRate) }}</span><span>抽到 {{ item.gihSamples >= 30 ? percent(item.gihWinRate) : '—' }}</span><span>未上手 {{ item.gnsSamples >= 30 ? percent(item.gnsWinRate) : '—' }}</span><strong>提升 {{ item.gihSamples >= 30 && item.gnsSamples >= 30 ? percent(item.inHandWinRateDelta) : '—' }}</strong></article></section>
      <section class="panel popular"><header><h3>热门构筑</h3><p>按完全一致的赛后构筑快照聚合；展开可核对组成。</p></header><details v-for="(deck, index) in report?.popularDecks || []" :key="deck.signature"><summary><b>构筑 {{ index + 1 }}</b><span>{{ deck.samples }} 份 · 胜率 {{ deck.samples >= 30 ? percent(deck.winRate) : '—' }}</span></summary><div><span v-for="card in deck.cards" :key="`${card.section}-${card.cardId}`">{{ cardById.get(card.cardId)?.nameZh || card.cardId }} ×{{ card.quantity }}</span></div></details></section>
    </template>
  </section>
</template>

<style scoped>
.master-analytics{--line:#33434d;display:grid;gap:12px}.panel,.master-tools,.selected-summary article{border:1px solid var(--line);background:#0e171f}.master-tools{display:flex;align-items:end;gap:10px;padding:12px}.master-tools fieldset{display:flex;gap:5px;margin:0;padding:5px 7px 7px;border:1px solid #485963}.master-tools legend,.master-tools label{color:#95a2a6;font-size:12px;font-weight:900}.master-tools label{display:grid;gap:5px}.master-tools button,.master-tools select{min-height:35px;border:1px solid #53636d;background:#071016;color:#fff;padding:6px 10px;font-weight:900}.master-tools button.active{border-color:#ddc36e;color:#ddc36e}.master-tools .refresh{margin-left:auto;color:#ddc36e}.panel{padding:14px}.panel>header{padding-bottom:10px;border-bottom:1px solid #2b3942}.panel h3{margin:0}.panel p{margin:4px 0 0;color:#849298;font-size:12px}.master-head,.master-list>button{display:grid;grid-template-columns:48px minmax(130px,1fr) repeat(5,minmax(100px,.8fr));align-items:center;gap:8px}.master-head{padding:9px 10px;color:#819097;font-size:11px}.master-head span:first-child{grid-column:1/3}.master-list>button{width:100%;padding:8px 10px;border:0;border-top:1px solid #293840;background:transparent;color:#fff;text-align:left}.master-list>button:hover,.master-list>button.selected{background:#17242d}.master-list>button.selected{box-shadow:inset 3px 0 #ddc36e}.master-list>button.low{opacity:.72}.master-list>button>span>small{display:none}.master-list>button>span>em{font-style:normal}.master-list :deep(.l12-card-image){width:42px;height:58px}.selected-summary{display:grid;grid-template-columns:repeat(3,1fr);gap:8px}.selected-summary article{display:grid;gap:4px;padding:13px}.selected-summary small,.selected-summary span{color:#829096;font-size:12px}.selected-summary b{font-size:18px}.trend-bars{display:flex;height:180px;align-items:end;gap:5px;padding-top:15px;overflow:auto}.trend-bars article{display:grid;flex:1;min-width:46px;height:100%;grid-template-rows:1fr auto auto;align-items:end;text-align:center}.trend-bars i{min-height:3px;background:linear-gradient(#53b8c0,#d5b85e)}.trend-bars b{font-size:11px}.trend-bars small{color:#7f8d92;font-size:10px}.matrix{overflow:auto}.matrix table{min-width:760px;width:100%;border-collapse:collapse}.matrix th,.matrix td{padding:8px;border:1px solid #2d3c44;text-align:center;font-size:11px}.matrix th{background:#111e26}.matrix td b,.matrix td small{display:block}.matrix td small{color:#7f8d92}.master-cards article{display:grid;grid-template-columns:42px minmax(140px,1fr) repeat(5,minmax(80px,.7fr));align-items:center;gap:8px;padding:8px;border-top:1px solid #293840;font-size:12px}.master-cards :deep(.l12-card-image){width:38px;height:53px}.master-cards strong{color:#dfc46e}.popular details{border-bottom:1px solid #293840}.popular summary{display:flex;justify-content:space-between;padding:11px;cursor:pointer}.popular details div{display:flex;flex-wrap:wrap;gap:7px;padding:0 11px 12px}.popular details div span{padding:5px 7px;background:#14222a;font-size:11px}.empty{padding:24px;color:#809097;text-align:center}@media(max-width:900px){.master-head{display:none}.master-list>button{grid-template-columns:42px minmax(0,1fr)}.master-list>button>span{grid-column:2;display:flex;flex-direction:column;align-items:flex-start;gap:1px;min-width:0;color:#dce1de}.master-list>button>span>small{display:block;color:#829096}.master-list>button>span>em{display:block;white-space:normal;color:#fff}.selected-summary{grid-template-columns:1fr}.master-cards article{grid-template-columns:38px 1fr 1fr}.master-cards article span,.master-cards article strong{grid-column:2/-1}.master-tools{flex-wrap:wrap}.master-tools .refresh{margin-left:0}}
</style>
