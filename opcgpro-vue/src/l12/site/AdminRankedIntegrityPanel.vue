<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type RankedIntegrityAudit } from '@/l12/platform'

const rows = ref<RankedIntegrityAudit[]>([])
const accountId = ref('')
const matchId = ref('')
const reviewOnly = ref(true)
const loading = ref(false)
const notice = ref('')

function duration(ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000))
  return `${Math.floor(seconds / 60)}分${seconds % 60}秒`
}

async function load() {
  loading.value = true
  notice.value = ''
  try {
    rows.value = await adminApi.rankedIntegrityAudits({ accountId: accountId.value.trim(), matchId: matchId.value.trim(), reviewOnly: reviewOnly.value, limit: 300 })
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
      <div><small>RANKED INTEGRITY</small><h2>排位完整性审计</h2><p>这里只呈现复合风险证据，不自动扣减七曜、封禁或限制正常重复对局。</p></div>
      <div class="filters"><input v-model="accountId" placeholder="账号 ID" @keyup.enter="load"/><input v-model="matchId" placeholder="对局 ID" @keyup.enter="load"/><label><input v-model="reviewOnly" type="checkbox"/>仅需复核</label><button :disabled="loading" @click="load">{{ loading ? '加载中' : '查询' }}</button></div>
    </header>
    <p v-if="notice" class="notice">{{ notice }}</p>
    <div class="integrity-head"><span>时间 / 对局</span><span>双方玩家</span><span>对局证据</span><span>处置</span></div>
    <article v-for="row in rows" :key="row.id" class="integrity-row" :data-review="row.reviewRecommended">
      <span>{{ new Date(row.createdAt).toLocaleString() }}<code>{{ row.matchId }}</code><small>{{ row.seasonId }}</small></span>
      <span><b>{{ row.firstPlayer }}</b><small>{{ row.firstAccountId }}</small><b>{{ row.secondPlayer }}</b><small>{{ row.secondAccountId }}</small></span>
      <span><em v-for="signal in row.signals" :key="signal.code">{{ signal.label }}</em><small>时长 {{ duration(row.durationMs) }} · 有效操作 {{ row.meaningfulCommandCount }} · {{ row.conclusionKind }}</small><code v-if="row.networkCorrelationId">网络关联号 {{ row.networkCorrelationId }}</code></span>
      <span><b>{{ row.reviewRecommended ? '建议人工核对' : '仅留痕' }}</b><small>当前处置：无</small></span>
    </article>
    <div v-if="!loading && !rows.length" class="empty">当前筛选下没有排位风险记录</div>
  </section>
</template>

<style scoped>
.integrity-panel{border:1px solid #35424a;background:#101821;padding:20px}.integrity-panel>header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;border-bottom:1px solid #36434a;padding-bottom:13px}.integrity-panel h2{margin:4px 0}.integrity-panel p{margin:0;color:#7d898e;font-size:10px}.integrity-panel small{display:block;color:#75828a;font-size:9px}.filters{display:flex;align-items:center;justify-content:flex-end;gap:6px;flex-wrap:wrap}.filters input,.filters button{box-sizing:border-box;padding:9px;border:1px solid #4c5961;background:#080e13;color:#fff}.filters label{display:flex;align-items:center;gap:5px;color:#aab3b6;font-size:9px}.filters label input{width:auto}.integrity-head,.integrity-row{display:grid;grid-template-columns:1.1fr 1.1fr 2fr .75fr;gap:12px;padding:10px}.integrity-head{color:#77858b;font-size:9px;font-weight:900}.integrity-row{border-top:1px solid #303c43;color:#c8cecc;font-size:10px}.integrity-row[data-review="true"]{border-left:3px solid #d28f3e;background:#1a140c}.integrity-row span{min-width:0}.integrity-row b,.integrity-row code,.integrity-row small{display:block;margin-top:4px}.integrity-row code{color:#d7b95f;overflow-wrap:anywhere}.integrity-row em{display:inline-block;margin:0 4px 4px 0;padding:4px 6px;border:1px solid #7b5930;background:#241b0d;color:#e6c87b;font-size:8px;font-style:normal}.notice{margin:10px 0;padding:9px;border-left:3px solid #a9404c;background:#281116;color:#efadb4!important}.empty{padding:36px;color:#75828a;text-align:center}@media(max-width:900px){.integrity-panel>header{align-items:stretch;flex-direction:column}.filters{justify-content:flex-start}.integrity-head{display:none}.integrity-row{grid-template-columns:1fr}}
</style>
