<script setup lang="ts">
import PagedCollection from './PagedCollection.vue'
import RankedIntegrityActions from './RankedIntegrityActions.vue'
import { computed, onMounted, ref } from 'vue'
import { adminApi, type RankedIntegrityAudit } from '@/l12/platform'
import { integrityLabel } from '../rankedIntegrity'

const rows = ref<RankedIntegrityAudit[]>([])
const accountId = ref('')
const matchId = ref('')
const reviewOnly = ref(true)
const loading = ref(false)
const notice = ref('')
const selected = ref<string[]>([])
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
function selectGroup(group: { rows: RankedIntegrityAudit[] }) { selected.value = group.rows.slice(0, 50).map(row => row.matchId) }

function duration(ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000))
  return `${Math.floor(seconds / 60)}分${seconds % 60}秒`
}

async function load() {
  loading.value = true
  notice.value = ''
  try {
    rows.value = await adminApi.rankedIntegrityAudits({ accountId: accountId.value.trim(), matchId: matchId.value.trim(), reviewOnly: reviewOnly.value, limit: 300 })
    selected.value = selected.value.filter(id => rows.value.some(row => row.matchId === id))
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '排位完整性审计加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="integrity-panel" data-ui-contract="ranked-integrity-review">
    <header>
      <div><small>RANKED INTEGRITY</small><h2>排位完整性审计</h2><p>风险不等于违规。强复合风险可暂缓收益；人工处置须核查证据，不自动扣减七曜、封禁或限制正常重复对局。</p></div>
      <div class="filters"><input v-model="accountId" placeholder="账号 ID" @keyup.enter="load"/><input v-model="matchId" placeholder="对局 ID" @keyup.enter="load"/><label><input v-model="reviewOnly" type="checkbox"/>仅需复核</label><button :disabled="loading" @click="load">{{ loading ? '加载中' : '查询' }}</button></div>
    </header>
    <p v-if="notice" class="notice">{{ notice }}</p>
    <details v-if="groups.length" class="risk-groups"><summary>相同对手归组（仅当前查询结果，不代表完整对局历史）</summary><button v-for="group in groups" :key="group.key" @click="selectGroup(group)">{{ group.names }} · {{ group.rows.length }}条 · 同一账号最多获胜{{ Math.max(0, ...group.winnerCounts.values()) }}条 · 选择{{ Math.min(50, group.rows.length) }}条</button></details>
    <div class="integrity-head"><span>时间 / 对局</span><span>双方玩家</span><span>对局证据</span><span>处置</span></div>
    <PagedCollection :items="rows" v-slot="{ items: paged1566 }"><article v-for="row in paged1566" :key="row.id" class="integrity-row" :data-review="row.reviewRecommended">
      <span><label><input v-model="selected" type="checkbox" :value="row.matchId"/>选择此局</label>{{ new Date(row.createdAt).toLocaleString() }}<code>{{ row.matchId }}</code><small>{{ row.seasonId }}</small></span>
      <span><b>{{ row.firstPlayer }}</b><small>{{ row.firstAccountId }}</small><b>{{ row.secondPlayer }}</b><small>{{ row.secondAccountId }}</small></span>
      <span><em v-for="signal in row.signals" :key="signal.code">{{ signal.label }}</em><small>时长 {{ duration(row.durationMs) }} · 有效操作 {{ row.meaningfulCommandCount }} · {{ row.conclusionKind }}</small><code v-if="row.networkCorrelationId">网络关联号 {{ row.networkCorrelationId }}</code></span>
      <span><b>{{ row.reviewRecommended ? '建议人工核对' : '仅留痕' }}</b><small>当前处置：{{ row.enforcement === 'none' ? '无' : integrityLabel(row.enforcement) }}</small></span>
    </article></PagedCollection>
    <div v-if="!loading && !rows.length" class="empty">当前筛选下没有排位风险记录</div>
    <RankedIntegrityActions :rows="rows" :selected="selected" @refresh="load"/>
  </section>
</template>

<style scoped>
.risk-groups{margin:12px 0;padding:12px;border:1px solid #55656e;max-height:240px;overflow:auto}.risk-groups button{display:block;max-width:100%;margin:8px 0;padding:10px;border:1px solid #6e654a;background:#201f16;color:#e9ddb5;white-space:normal;text-align:left;font:inherit}
.integrity-panel{border:1px solid #35424a;background:#101821;padding:20px}.integrity-panel>header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;border-bottom:1px solid #36434a;padding-bottom:13px}.integrity-panel h2{margin:4px 0}.integrity-panel p{margin:0;color:#7d898e;font-size:14px}.integrity-panel small{display:block;color:#75828a;font-size:14px}.filters{display:flex;align-items:center;justify-content:flex-end;gap:6px;flex-wrap:wrap}.filters input,.filters button{box-sizing:border-box;padding:9px;border:1px solid #4c5961;background:#080e13;color:#fff}.filters label{display:flex;align-items:center;gap:5px;color:#aab3b6;font-size:14px}.filters label input{width:auto}.integrity-head,.integrity-row{display:grid;grid-template-columns:1.1fr 1.1fr 2fr .75fr;gap:12px;padding:10px}.integrity-head{color:#77858b;font-size:14px;font-weight:900}.integrity-row{border-top:1px solid #303c43;color:#c8cecc;font-size:14px}.integrity-row[data-review="true"]{border-left:3px solid #d28f3e;background:#1a140c}.integrity-row span{min-width:0}.integrity-row b,.integrity-row code,.integrity-row small{display:block;margin-top:4px}.integrity-row code{color:#d7b95f;overflow-wrap:anywhere}.integrity-row em{display:inline-block;margin:0 4px 4px 0;padding:4px 6px;border:1px solid #7b5930;background:#241b0d;color:#e6c87b;font-size:14px;font-style:normal}.notice{margin:10px 0;padding:9px;border-left:3px solid #a9404c;background:#281116;color:#efadb4!important}.empty{padding:36px;color:#75828a;text-align:center}@media(max-width:900px){.integrity-panel>header{align-items:stretch;flex-direction:column}.filters{justify-content:flex-start}.integrity-head{display:none}.integrity-row{grid-template-columns:1fr}}
</style>
