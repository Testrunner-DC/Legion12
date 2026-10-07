<script setup lang="ts">
import PagedCollection from './PagedCollection.vue'
import RankedIntegrityActions from './RankedIntegrityActions.vue'
import AdminAccountPicker from './AdminAccountPicker.vue'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { adminApi, type RankedIntegrityAudit } from '@/l12/platform'
import { integrityLabel } from '../rankedIntegrity'

const rows = ref<RankedIntegrityAudit[]>([])
const accountId = ref('')
const matchId = ref('')
const reviewOnly = ref(true)
const loading = ref(false)
const notice = ref('')
const selected = ref<string[]>([])
const activeGroup = ref<{ key: string; name: string; matchIds: Set<string> } | null>(null)
const filteredRows = computed(() => {
  const group = activeGroup.value
  return group ? rows.value.filter(row => group.matchIds.has(row.matchId)) : rows.value
})
let loadGeneration = 0
const groups = computed(() => {
  const result = new Map<string, { key: string; names: string; rows: RankedIntegrityAudit[]; winnerCounts: Map<string, number> }>()
  for (const row of rows.value) {
    const key = [row.firstAccountId, row.secondAccountId].sort().join('|')
    const group = result.get(key) || { key, names: `${row.firstPlayer} / ${row.secondPlayer}`, rows: [], winnerCounts: new Map<string, number>() }
    group.rows.push(row)
    const winner = row.winner === 0 ? row.firstAccountId : row.winner === 1 ? row.secondAccountId : null
    if (winner) group.winnerCounts.set(winner, (group.winnerCounts.get(winner) || 0) + 1)
    result.set(key, group)
  }
  return [...result.values()].filter(group => group.rows.length > 1).sort((a, b) => b.rows.length - a.rows.length)
})
const beneficiaryGroups = computed(() => {
  const result = new Map<string, { key: string; name: string; opponents: Set<string>; rows: RankedIntegrityAudit[] }>()
  for (const row of rows.value) {
    if (row.winner !== 0 && row.winner !== 1) continue
    if (!row.signals.some(signal => ['unilateral-score-transfer', 'repeated-padded-transfer', 'linked-loser-cluster'].includes(signal.code))) continue
    const winnerId = row.winner === 0 ? row.firstAccountId : row.secondAccountId
    const winnerName = row.winner === 0 ? row.firstPlayer : row.secondPlayer
    const opponentId = row.winner === 0 ? row.secondAccountId : row.firstAccountId
    const group = result.get(winnerId) || { key: winnerId, name: winnerName, opponents: new Set<string>(), rows: [] }
    group.opponents.add(opponentId)
    group.rows.push(row)
    result.set(winnerId, group)
  }
  return [...result.values()].filter(group => group.rows.length > 1)
    .sort((a, b) => b.rows.length - a.rows.length)
})
function groupSelectionCount(group: { rows: RankedIntegrityAudit[] }) {
  return Math.min(50, group.rows.filter(selectable).length)
}

function selectGroup(group: { key: string; names?: string; name?: string; rows: RankedIntegrityAudit[] }, kind: 'pair' | 'beneficiary') {
  activeGroup.value = { key: `${kind}:${group.key}`, name: group.names || group.name || group.key,
    matchIds: new Set(group.rows.map(row => row.matchId)) }
  selected.value = group.rows.filter(selectable).slice(0, 50).map(row => row.matchId)
}

function clearGroup() { activeGroup.value = null; selected.value = [] }

function duration(ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000))
  return `${Math.floor(seconds / 60)}分${seconds % 60}秒`
}

function dispositionLabel(value: RankedIntegrityAudit['effectiveDisposition']) {
  return ({ unreviewed: '待处理', review: '复核中', normal: '正常', insufficient: '证据不足',
    'system-error': '系统异常', confirmed: '已确认违规' } as Record<string, string>)[value] || value
}

function selectable(row: RankedIntegrityAudit) {
  return row.effectiveDisposition === 'unreviewed' || row.effectiveDisposition === 'review'
}

async function load() {
  const generation = ++loadGeneration
  loading.value = true
  notice.value = ''
  clearGroup()
  rows.value = []
  try {
    const result = await adminApi.rankedIntegrityAudits({ accountId: accountId.value.trim(), matchId: matchId.value.trim(), reviewOnly: reviewOnly.value, limit: 300 })
    if (generation === loadGeneration) rows.value = result
  } catch (error) {
    if (generation === loadGeneration) notice.value = error instanceof Error ? error.message : '排位完整性审计加载失败'
  } finally {
    if (generation === loadGeneration) loading.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => { loadGeneration++ })
</script>

<template>
  <section class="integrity-panel" data-ui-contract="ranked-integrity-review">
    <header>
      <div><small>RANKED INTEGRITY</small><h2>排位完整性审计</h2><p>风险不等于违规。强复合风险可暂缓收益；人工处置须核查证据，不自动扣减七曜、封禁或限制正常重复对局。</p></div>
      <div class="filters"><AdminAccountPicker v-model="accountId" label="玩家账号" @change="load"/><input v-model="matchId" placeholder="对局 ID" @keyup.enter="load"/><label><input v-model="reviewOnly" type="checkbox"/>仅需复核</label><button :disabled="loading" @click="load">{{ loading ? '加载中' : '查询' }}</button></div>
    </header>
    <p v-if="notice" class="notice">{{ notice }}</p>
    <p class="query-scope"><template v-if="loading">当前查询加载中</template><template v-else-if="notice">当前查询加载失败</template><template v-else>当前查询返回 {{ rows.length }} 条</template>（最多300条），不代表完整对局历史。</p>
    <details v-if="groups.length" class="risk-groups"><summary>相同对手归组（仅当前查询结果，不代表完整对局历史）</summary><button v-for="group in groups" :key="group.key" :aria-pressed="activeGroup?.key === `pair:${group.key}`" @click="selectGroup(group, 'pair')">{{ group.names }} · {{ group.rows.length }}条 · 同一账号最多获胜{{ Math.max(0, ...group.winnerCounts.values()) }}条 · 筛选并选择{{ groupSelectionCount(group) }}条</button></details>
    <details v-if="beneficiaryGroups.length" class="risk-groups"><summary>单向获益账号归组（仅当前查询结果，须人工核对）</summary><button v-for="group in beneficiaryGroups" :key="group.key" :aria-pressed="activeGroup?.key === `beneficiary:${group.key}`" @click="selectGroup(group, 'beneficiary')">{{ group.name }} · 涉及{{ group.opponents.size }}个对手 · {{ group.rows.length }}条风险对局 · 筛选并选择{{ groupSelectionCount(group) }}条</button></details>
    <div v-if="activeGroup" class="group-filter"><span>归组筛选：{{ activeGroup.name }} · {{ filteredRows.length }}条（当前查询结果）</span><button type="button" @click="clearGroup">清除归组筛选</button></div>
    <div class="integrity-head"><span>时间 / 对局</span><span>双方玩家</span><span>对局证据</span><span>处置</span></div>
    <PagedCollection :key="activeGroup?.key || 'all'" :items="filteredRows" v-slot="{ items: paged1566 }"><article v-for="row in paged1566" :key="row.id" class="integrity-row" :data-review="row.reviewRecommended">
      <span><label><input v-model="selected" type="checkbox" :value="row.matchId" :disabled="!selectable(row)"/>选择此局</label>{{ new Date(row.createdAt).toLocaleString() }}<code>{{ row.matchId }}</code><small>{{ row.seasonId }}</small></span>
      <span><b>{{ row.firstPlayer }}</b><small>{{ row.firstAccountId }}</small><b>{{ row.secondPlayer }}</b><small>{{ row.secondAccountId }}</small></span>
      <span><em v-for="signal in row.signals" :key="signal.code">{{ signal.label }}</em><small>时长 {{ duration(row.durationMs) }} · 有效操作 {{ row.meaningfulCommandCount }} · {{ row.finalRound > 0 ? `第 ${row.finalRound} 回合` : '回合未知' }} · {{ row.conclusionKind }}</small><code v-if="row.networkCorrelationId">网络关联号 {{ row.networkCorrelationId }}</code><code v-if="row.browserCorrelationId">浏览器关联号 {{ row.browserCorrelationId }}</code></span>
      <span><b>{{ dispositionLabel(row.effectiveDisposition) }}</b><small>{{ selectable(row) ? (row.reviewRecommended ? '建议人工核对' : '仅留痕') : '已完成处理' }}</small><small>收益状态：{{ row.enforcement === 'none' ? '无' : integrityLabel(row.enforcement) }}</small></span>
    </article></PagedCollection>
    <div v-if="!loading && !notice && !rows.length" class="empty">当前筛选下没有排位风险记录</div>
    <RankedIntegrityActions :rows="rows" :selected="selected" @refresh="load"/>
  </section>
</template>

<style scoped>
.group-filter{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:10px;margin:12px 0;padding:10px;border:1px solid #6e654a;color:#e9ddb5}.group-filter span{min-width:0;overflow-wrap:anywhere}.group-filter button{max-width:100%;min-height:40px;padding:8px 10px;border:1px solid #6e654a;background:#201f16;color:#e9ddb5;font:inherit}.risk-groups button[aria-pressed="true"]{border-color:#d7b95f;background:#302915}.integrity-panel .query-scope{margin-top:12px}
.risk-groups{margin:12px 0;padding:12px;border:1px solid #55656e;max-height:240px;overflow:auto}.risk-groups button{display:block;max-width:100%;margin:8px 0;padding:10px;border:1px solid #6e654a;background:#201f16;color:#e9ddb5;white-space:normal;text-align:left;font:inherit}
.integrity-panel{border:1px solid #35424a;background:#101821;padding:20px}.integrity-panel>header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;border-bottom:1px solid #36434a;padding-bottom:13px}.integrity-panel h2{margin:4px 0}.integrity-panel p{margin:0;color:#7d898e;font-size:14px}.integrity-panel small{display:block;color:#75828a;font-size:14px}.filters{display:flex;align-items:center;justify-content:flex-end;gap:6px;flex-wrap:wrap}.filters input,.filters button{box-sizing:border-box;padding:9px;border:1px solid #4c5961;background:#080e13;color:#fff}.filters label{display:flex;align-items:center;gap:5px;color:#aab3b6;font-size:14px}.filters label input{width:auto}.integrity-head,.integrity-row{display:grid;grid-template-columns:1.1fr 1.1fr 2fr .75fr;gap:12px;padding:10px}.integrity-head{color:#77858b;font-size:14px;font-weight:900}.integrity-row{border-top:1px solid #303c43;color:#c8cecc;font-size:14px}.integrity-row[data-review="true"]{border-left:3px solid #d28f3e;background:#1a140c}.integrity-row span{min-width:0}.integrity-row b,.integrity-row code,.integrity-row small{display:block;margin-top:4px}.integrity-row code{color:#d7b95f;overflow-wrap:anywhere}.integrity-row em{display:inline-block;margin:0 4px 4px 0;padding:4px 6px;border:1px solid #7b5930;background:#241b0d;color:#e6c87b;font-size:14px;font-style:normal}.notice{margin:10px 0;padding:9px;border-left:3px solid #a9404c;background:#281116;color:#efadb4!important}.empty{padding:36px;color:#75828a;text-align:center}@media(max-width:900px){.integrity-panel>header{align-items:stretch;flex-direction:column}.filters{justify-content:flex-start}.integrity-head{display:none}.integrity-row{grid-template-columns:1fr}}
.integrity-panel,.integrity-panel>header,.filters,.integrity-row{box-sizing:border-box;min-width:0;max-width:100%}.filters>*{min-width:0;max-width:100%}@media(max-width:520px){.integrity-panel{padding:14px}.filters{display:grid;grid-template-columns:1fr}.filters input,.filters button{width:100%}}
.filters :deep(.admin-account-picker){min-width:min(320px,100%)}
</style>
