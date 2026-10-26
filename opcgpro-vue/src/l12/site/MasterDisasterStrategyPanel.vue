<script setup lang="ts">
import { vHorizontalScrollArrows } from '../horizontalScrollArrows'
import { computed, ref, watch } from 'vue'
import type { DeckCard } from '@/l12/decks'
import { useDisasterRead, type DisasterAnalyticsScope } from '@/l12/disasterAnalytics'
import { disasterStrategyPath, strategyRanking, strategyStepName, type DisasterStrategyReport } from '@/l12/disasterStrategy'

const props = defineProps<{ scope: DisasterAnalyticsScope; cards: DeckCard[]; initialOpponent?: string; initialInitiative?: string }>()
const opponent = ref(props.initialOpponent || '')
const initiative = ref(props.initialInitiative || 'first')
const mode = ref('')
const pool = ref('')
const origin = ref('player')
const mechanismCard = ref('')
const requestPath = () => disasterStrategyPath({ ...props.scope, opponentMasterId: opponent.value,
  initiative: initiative.value, disasterMode: mode.value, disasterPoolId: pool.value, disasterOrigin: origin.value })
const { value: receivedReport, pending, error, load, reset } = useDisasterRead<DisasterStrategyReport>(requestPath)
const report = computed(() => {
  const next = receivedReport.value
  return next && next.masterId === props.scope.masterId && next.opponentMasterId === opponent.value
    && next.initiative === initiative.value && (next.mode || '') === mode.value
    && (next.poolId || '') === pool.value && next.origin === origin.value ? next : null
})
const byId = computed(() => new Map(props.cards.map(card => [card.id, card])))
const masters = computed(() => props.cards.filter(card => card.cardType === 'master'))
const name = (id?: string | number | null) => id ? byId.value.get(String(id))?.nameZh || String(id) : '未记录'
const rate = (value: number | null | undefined) => typeof value === 'number' ? `${(value * 100).toFixed(1)}%` : '—'
const steps = computed(() => [0, 1, 2, 4, 5].map(step => {
  const party = (initiative.value === 'first' ? [0, 2, 4] : [1, 5]).includes(step) ? 'self' : 'opponent'
  const items = report.value?.items.filter(row => row.step === step && row.party === party) || []
  return { step, party, items, ranked: strategyRanking(items, report.value?.minimumOpportunities || 30) }
}))
const referenceCards = computed(() => [...new Set([String(props.scope.masterId || ''), opponent.value, mechanismCard.value])]
  .map(id => byId.value.get(id)).filter((card): card is DeckCard => !!card))
watch([() => props.initialOpponent, () => props.initialInitiative], () => {
  opponent.value = props.initialOpponent || ''; initiative.value = props.initialInitiative || 'first'; reset()
})
watch(requestPath, () => { mechanismCard.value = '' })
async function read() { if (opponent.value) await load() }
</script>

<template>
  <section class="disaster-strategy" aria-label="主宰天灾对阵策略">
    <header><div><h3>天灾禁选 · 对阵策略</h3><p>我方 {{ name(scope.masterId) }}：面对谁，先手或后手，如何准备？历史观察不是必胜推荐。</p></div><button :disabled="pending || !opponent" @click="read">{{ pending ? '读取中…' : report ? '刷新策略观察' : '分析此对阵' }}</button></header>
    <div class="strategy-controls">
      <label>面对的主宰<select v-model="opponent" aria-label="策略对方主宰" @change="read"><option value="">请选择对方主宰</option><option v-for="card in masters" :key="card.id" :value="card.id">{{ card.nameZh }}</option></select></label>
      <label>我方先后手<select v-model="initiative" aria-label="策略我方先后手" @change="read"><option value="first">先手</option><option value="second">后手</option></select></label>
    </div>
    <p v-if="!opponent" class="strategy-empty">先选择面对的主宰。未指定对阵时，不用全体天灾排行冒充针对策略。</p>
    <p v-if="error" role="alert">{{ error }}。若后端尚未提供对阵策略接口，请升级后查看；缺失不等于零禁选。</p>
    <p v-if="receivedReport && !report" role="alert">策略响应与当前对阵筛选不一致，请重新读取；不展示错配数据。</p>
    <template v-if="report">
      <div class="strategy-overview"><strong>{{ name(report.masterId) }} 对 {{ name(report.opponentMasterId) }} · 我方{{ report.initiative === 'first' ? '先手' : '后手' }}</strong><span>{{ report.coverage.distinctMatches }} 场记录 · {{ report.coverage.usableMatches }} 场禁选可用</span></div>
      <p v-if="report.status === 'insufficient'" class="strategy-warning">此对阵样本偏少，下面仅列历史观察，不足以归纳稳定策略。</p>
      <p v-if="!report.items.length">当前筛选没有可用策略观察；历史未知 {{ report.coverage.unknownSamples }} 份、未完成 {{ report.coverage.incompleteSamples }} 份，不等于没有禁选。</p>
      <p v-if="report.privatePickGroupsHidden" class="strategy-warning">当前权限仅返回公开禁用；私密选牌及其条件分组未返回，不代表无人选牌。</p>
      <div class="strategy-steps">
        <article v-for="group in steps" :key="group.step" class="strategy-step">
          <h4><span>{{ group.party === 'self' ? '我方' : '对方' }}</span>{{ strategyStepName(group.step) }}</h4>
          <p v-if="!group.items.length">{{ group.step >= 4 && report.privatePickGroupsHidden ? '私密选择未授权展示' : '当前范围没有可用观察，不能据此给出禁选建议' }}</p>
          <template v-else>
            <p>{{ group.ranked.every(row => row.opportunities < report!.minimumOpportunities) ? '小样本：按机会数列示，不评最优' : '本对阵常见应对，按真实机会比例排序' }}</p>
            <button v-for="row in group.ranked" :key="row.cardId" class="strategy-choice" @click="mechanismCard = row.cardId">
              <span><b>{{ name(row.cardId) }}</b><strong>{{ rate(row.selectionRate) }}</strong></span>
              <i class="rate-track"><i :style="{ width: `${(row.selectionRate || 0) * 100}%` }" /></i>
              <small>{{ row.selections }} / {{ row.opportunities }} 次合法机会 · {{ row.distinctMatches }} 场</small>
              <small v-if="row.comparison.difference !== null">较我方面对其他主宰{{ row.comparison.difference >= 0 ? '高' : '低' }} {{ Math.abs(row.comparison.difference * 100).toFixed(1) }} 个百分点（共同条件 {{ row.comparison.comparableOpportunities }} / {{ row.comparison.totalOpportunities }} 次机会）</small>
              <small v-else>对阵外对照{{ row.comparison.status === 'unknown' ? '无共同条件，无法比较' : '样本不足，暂不比较' }}</small>
            </button>
          </template>
          <details v-if="report.conditions.some(condition => condition.step === group.step && condition.party === group.party)"><summary>在真实候选条件下如何选择</summary>
            <div v-for="condition in report.conditions.filter(item => item.step === group.step && item.party === group.party)" :key="condition.id" class="strategy-condition">
              <p>此前公开禁用：{{ condition.priorPublicBans.map(item => `${strategyStepName(item.step)} ${name(item.cardId)}`).join('；') || '无' }}</p>
              <p v-if="condition.knownPublicCardId">当时已公开天灾：{{ name(condition.knownPublicCardId) }}</p>
              <p>本步实际候选：{{ condition.candidateCardIds.map(name).join('、') }}</p>
              <p>{{ condition.distinctMatches }} 场同条件记录：{{ condition.choices.map(choice => `${name(choice.cardId)} ${choice.selections}/${condition.samples}次`).join('；') }}</p>
              <small>只描述本步当时可知条件；不含对手私密候选、私密选择或未来结果，不是整条推荐路线。</small>
            </div>
          </details>
        </article>
      </div>
      <details class="mechanism-reference"><summary>机制依据核对 · 当前卡文参照</summary><p>尚无审定的全主宰相克解释库；下列原文供核对，不自动断言克制或胜率提升。历史记录可能属于旧版本，当前卡文不冒充当时规则。点击上方天灾可加入对照。</p><article v-for="card in referenceCards" :key="card.id"><b>{{ card.nameZh }}</b><p>{{ card.effect || '当前目录没有可用卡文，不生成机制解释。' }}</p></article></details>
      <details class="strategy-details"><summary>筛选、数据覆盖与完整明细</summary>
        <div class="strategy-controls advanced"><label>天灾模式<select v-model="mode" aria-label="策略天灾模式" @change="pool = ''; read()"><option value="">全部模式</option><option value="all">全部天灾</option><option value="season">赛季天灾</option><option value="random">随机天灾</option><option value="none">无天灾</option></select></label><label>卡池快照<select v-model="pool" aria-label="策略卡池快照" @change="read"><option value="">全部已记录卡池</option><option v-for="item in report.pools" :key="item.id" :value="item.id">{{ item.mode }} · {{ item.cardIds.length }}张 · {{ item.id.slice(0, 8) }}</option></select></label><label>决定来源<select v-model="origin" aria-label="策略决定来源" @change="read"><option value="player">玩家主动选择</option><option value="server-setup-timeout">服务器超时代选（不作玩家偏好）</option></select></label></div>
        <p>基础对阵覆盖：{{ report.coverage.participantSamples }} 份参赛方，{{ report.coverage.distinctMatches }} 场；未知 {{ report.coverage.unknownSamples }}，未完成 {{ report.coverage.incompleteSamples }}，无自主禁选 {{ report.coverage.notApplicableSamples }}。覆盖数不随天灾表筛选改变；镜像两方不是两个独立对局。</p>
        <p>对照仅在相同版本、卡池、我方侧别、执行方、步骤与来源的共同分层中按本对阵机会数加权，非因果比较。30次仅为展示门槛。</p>
        <p>先手随机获得3张候选后，这3张全部移出；后手从剩余池新抽2张，并非承接先手未选的两张。</p>
        <p v-if="report.conditionsTruncated">候选场景仅列频次最高的30组；不代表完整路线或全部场景。</p><p v-if="report.poolsTruncated">卡池仅列最近100个，请缩小日期或版本范围。</p>
        <div v-horizontal-scroll-arrows="'天灾策略明细'" class="strategy-table-scroll" tabindex="0" aria-label="策略完整明细，可横向滚动"><table><thead><tr><th>执行方／步骤</th><th>天灾</th><th>次数／实际机会</th><th>比例</th></tr></thead><tbody><tr v-for="row in report.items" :key="`${row.party}-${row.step}-${row.cardId}`"><td>{{ row.party === 'self' ? '我方' : '对方' }} · {{ strategyStepName(row.step) }}</td><td>{{ name(row.cardId) }}</td><td>{{ row.selections }} / {{ row.opportunities }}</td><td>{{ rate(row.selectionRate) }}</td></tr></tbody></table></div>
      </details>
    </template>
  </section>
</template>

<style scoped>
.disaster-strategy{box-sizing:border-box;min-width:0;max-width:100%;padding:14px;border:1px solid #3b4a53;background:#101820;color:#e8e5db}.disaster-strategy header{display:flex;justify-content:space-between;flex-wrap:wrap;gap:12px}.disaster-strategy h3{margin:0}.disaster-strategy p,.disaster-strategy small{color:#a7b4b8;line-height:1.65;overflow-wrap:anywhere}.disaster-strategy p{font-size:13px}.disaster-strategy button,.disaster-strategy select{box-sizing:border-box;min-height:44px;min-width:0;max-width:100%;padding:8px 12px;border:1px solid #4a5d67;background:#15212a;color:#e8e5db;font:inherit}.disaster-strategy option{background:#101820;color:#e8e5db}.strategy-controls{display:grid;grid-template-columns:2fr 1fr;gap:10px;margin:14px 0}.strategy-controls label{display:grid;gap:6px;min-width:0}.strategy-overview{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px;margin:12px 0}.strategy-overview span{color:#a7b4b8}.strategy-steps{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));align-items:start;gap:10px}.strategy-step{min-width:0;padding:12px;border:1px solid #34434d;background:#0d171e}.strategy-step h4{margin:0;display:flex;gap:8px;flex-wrap:wrap}.strategy-step h4 span{color:#ddc36e}.strategy-choice{display:grid;gap:6px;width:100%;margin:8px 0;text-align:left}.strategy-choice>span{display:flex;justify-content:space-between;flex-wrap:wrap;gap:8px}.rate-track{display:block;height:6px;background:#293d48}.rate-track>i{display:block;height:100%;background:#64c8ce}.disaster-strategy details{margin-top:12px;min-width:0}.disaster-strategy summary{padding:10px 0;cursor:pointer;overflow-wrap:anywhere}.strategy-condition{padding:8px 0;border-top:1px solid #34434d}.mechanism-reference,.strategy-details{padding:0 10px;border:1px solid #34434d}.mechanism-reference article{padding:10px 0;border-top:1px solid #34434d}.mechanism-reference article p{white-space:pre-wrap}.strategy-table-scroll{max-width:100%;overflow:auto}.strategy-table-scroll table{min-width:500px;width:100%;border-collapse:collapse}.strategy-table-scroll th,.strategy-table-scroll td{padding:8px;text-align:left;border-bottom:1px solid #34434d;font-size:12px}.disaster-strategy :focus-visible{outline:2px solid #64c8ce;outline-offset:2px}.strategy-warning{padding:8px;border-left:3px solid #ddc36e}@media(min-width:701px) and (max-width:1000px){.strategy-steps{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:700px){.strategy-controls{grid-template-columns:minmax(0,1fr)}.strategy-steps{grid-template-columns:minmax(0,1fr)}.disaster-strategy header>button{width:100%}}
</style>
