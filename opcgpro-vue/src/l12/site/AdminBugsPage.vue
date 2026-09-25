<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { adminApi, hasPermission, type BugReport } from '@/l12/platform'
import PagedCollection from './PagedCollection.vue'

const route = useRoute()
const bugs = ref<BugReport[]>([])
const status = ref('')
const priority = ref('')
const search = ref(typeof route.params.bugId === 'string' ? route.params.bugId : '')
const comments = reactive<Record<string, string>>({})
const notice = ref('')
const loading = ref(false)
const actionLabel = (action: string) => ({ created: '建立反馈', status: '状态变更', priority: '优先级变更', assignee: '负责人变更', notes: '处理摘要变更', comment: '追加处理记录', 'fix-commit': '关联修复提交', 'regression-test': '关联回归测试', 'deployed-version': '记录部署版本', 'verified-by': '记录验证人', 'verified-at': '记录验证时间', 'duplicate-of': '关联重复问题' } as Record<string,string>)[action] || action
const closureReady = (item: BugReport) => Boolean(item.duplicateOf?.trim() || (item.fixCommit?.trim() && item.regressionTest?.trim() && item.deployedVersion?.trim() && item.verifiedBy?.trim() && item.verifiedAt))
const closureLabel = (item: BugReport) => item.status === 'closed' ? '已关闭' : item.duplicateOf?.trim() ? '重复关联已填写 · 服务端校验后可关闭' : closureReady(item) ? '证据齐全 · 可关闭' : '证据未齐 · 暂不可关闭'

async function load() {
  loading.value = true
  try { bugs.value = await adminApi.bugs({ status: status.value, priority: priority.value, search: search.value }) }
  catch (cause) { notice.value = cause instanceof Error ? cause.message : 'Bug 列表读取失败' }
  finally { loading.value = false }
}
async function save(item: BugReport) {
  try {
    const updated = await adminApi.updateBug(item.id, { status: item.status, priority: item.priority, assignee: item.assignee, adminNotes: item.adminNotes, comment: comments[item.id], fixCommit: item.fixCommit, regressionTest: item.regressionTest, deployedVersion: item.deployedVersion, verifiedBy: item.verifiedBy, verifiedAt: item.verifiedAt, duplicateOf: item.duplicateOf })
    bugs.value = bugs.value.map(value => value.id === updated.id ? updated : value)
    comments[item.id] = ''
    notice.value = `${item.id} 已更新并写入审计记录`
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '更新失败' }
}
watch(() => route.params.bugId, value => { search.value = typeof value === 'string' ? value : ''; void load() })
onMounted(load)
</script>

<template>
  <section class="bugs-page panel">
    <header><div><h2>Bug 分诊与证据闭环</h2><p>按待裁定、待实施、待复测、待部署推进；证据齐全后才显示可关闭，服务端仍会再次校验。</p></div><div class="filters"><input v-model="search" placeholder="编号 / 标题 / 玩家 / 对局" @keyup.enter="load"><select v-model="status" @change="load"><option value="">全部阶段</option><option value="new">待分诊</option><option value="decision">待裁定</option><option value="implementation">待实施 / 实施中</option><option value="retest">待复测</option><option value="deploy">待部署</option><option value="closed">已关闭</option></select><select v-model="priority" @change="load"><option value="">全部优先级</option><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><button :disabled="loading" @click="load">查询</button></div></header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <PagedCollection :items="bugs" v-slot="{ items }"><article v-for="item in items" :key="item.id" class="bug-row"><div class="summary"><router-link :to="`/admin/users/bugs/${encodeURIComponent(item.id)}`"><code>{{ item.id }}</code></router-link><h3>{{ item.title }}</h3><span class="closure-state" :data-ready="closureReady(item)">{{ closureLabel(item) }}</span><small>{{ item.reporterName }} · {{ new Date(item.createdAt).toLocaleString() }}</small><small>客户端 {{ item.clientVersion || item.version || 'unknown-client' }} · 服务端 {{ item.serverVersion || 'legacy-unknown' }} · 引擎 {{ item.engineVersion || 'legacy-unknown' }}</small><p>{{ item.description }}</p><dl><dt>修复提交</dt><dd>{{ item.fixCommit || '待关联' }}</dd><dt>回归测试</dt><dd>{{ item.regressionTest || '待关联' }}</dd><dt>部署版本</dt><dd>{{ item.deployedVersion || '待部署' }}</dd><dt>验证</dt><dd>{{ item.verifiedBy && item.verifiedAt ? `${item.verifiedBy} · ${new Date(item.verifiedAt).toLocaleString()}` : '待复测' }}</dd><template v-if="item.duplicateOf"><dt>重复问题</dt><dd>{{ item.duplicateOf }}</dd></template></dl><details><summary>处理记录（{{ item.history.length }}）</summary><ol><li v-for="entry in item.history" :key="entry.id"><b>{{ actionLabel(entry.action) }}</b><span>{{ entry.actorName }} · {{ new Date(entry.createdAt).toLocaleString() }}</span><p>{{ entry.comment || `${entry.fromValue || '无'} → ${entry.toValue || '无'}` }}</p></li></ol></details></div><div v-if="hasPermission('admin.bugs.write')" class="editor"><select v-model="item.status"><option value="new">待分诊</option><option value="decision">待裁定</option><option value="implementation">待实施 / 实施中</option><option value="retest">待复测</option><option value="deploy">待部署</option><option value="closed">已关闭</option></select><select v-model="item.priority"><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><input v-model="item.assignee" placeholder="负责人"><textarea v-model="item.adminNotes" rows="2" placeholder="处理摘要"></textarea><input v-model="item.fixCommit" placeholder="修复提交"><input v-model="item.regressionTest" placeholder="回归测试"><input v-model="item.deployedVersion" placeholder="部署版本"><input v-model="item.verifiedBy" placeholder="复测人"><input v-model="item.verifiedAt" type="datetime-local" aria-label="复测时间"><input v-model="item.duplicateOf" placeholder="重复问题编号（可选）"><textarea v-model="comments[item.id]" rows="2" placeholder="追加处理记录"></textarea><button @click="save(item)">保存并记录</button></div></article></PagedCollection>
    <p v-if="!bugs.length && !loading" class="empty">暂无符合筛选条件的反馈。</p>
  </section>
</template>

<style scoped>
.panel{padding:20px;border:1px solid #35424a;background:#0e161d}.panel>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.panel h2,.panel h3{margin:0 0 6px}.panel p,.panel small{color:#8d9ba0;line-height:1.6}.filters{display:flex;flex-wrap:wrap;gap:7px}.filters input{min-width:220px}.panel button,.panel input,.panel select,.panel textarea{box-sizing:border-box;min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.bug-row{display:grid;grid-template-columns:minmax(0,1fr) 280px;gap:18px;padding:18px 0;border-top:1px solid #303c43}.summary>a{color:#e1c36e}.closure-state{display:inline-block;margin:2px 0 8px;padding:4px 7px;border:1px solid #6d5b32;color:#d6bd70;font-size:12px}.closure-state[data-ready="true"]{border-color:#357d61;color:#80d6af}.summary dl{display:grid;grid-template-columns:90px minmax(0,1fr);gap:6px;margin:12px 0}.summary dt{color:#77858b}.summary dd{margin:0;overflow-wrap:anywhere}.summary details summary{cursor:pointer;color:#d8ba68}.summary li{margin:8px 0}.summary li span{margin-left:8px;color:#77858b;font-size:12px}.editor{display:grid;align-content:start;gap:8px}.editor button{border-color:#9b8138;color:#f1d576}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}.empty{text-align:center}
@media(max-width:900px){.panel>header{flex-direction:column}.filters,.filters>*{width:100%}.bug-row{grid-template-columns:1fr}}
</style>
