<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { adminApi, hasPermission, platformState, type BugReport } from '@/l12/platform'
import PagedCollection from './PagedCollection.vue'

type ClosureDisposition = NonNullable<BugReport['closureDisposition']>
type BugPage = Awaited<ReturnType<typeof adminApi.bugsPage>>
const editKeys = ['status', 'priority', 'assignee', 'adminNotes', 'fixCommit', 'regressionTest', 'deployedVersion', 'duplicateOf'] as const
type EditFields = Pick<BugReport, typeof editKeys[number]>
type Draft = { edits: Partial<EditFields>; baseFields: EditFields; baseVersion: number; comment: string; closing: boolean; disposition: ClosureDisposition | '' }

const route = useRoute()
const bugs = ref<BugReport[]>([])
const result = ref<BugPage | null>(null)
const page = ref(1)
const pageSize = 10
const status = ref('')
const priority = ref('')
const search = ref(typeof route.params.bugId === 'string' ? route.params.bugId : '')
const comments = reactive<Record<string, string>>({})
const closing = reactive<Record<string, boolean>>({})
const closureDrafts = reactive<Record<string, ClosureDisposition | ''>>({})
const notice = ref('')
const loading = ref(false)
const loadError = ref('')
const saving = reactive<Record<string, boolean>>({})
// Cache only unsaved editor fields, not full reports/history or a second list.
// Bases and latestRows hold the current page's authoritative read only.
const drafts = new Map<string, Draft>()
const bases = new Map<string, { fields: EditFields; version: number }>()
const latestRows = new Map<string, BugReport>()
let requestSequence = 0, actorGeneration = 0, mounted = false, disposed = false
const mutationSequences = new Map<string, number>()

function fields(item: BugReport): EditFields {
  return Object.fromEntries(editKeys.map(key => [key, item[key]])) as EditFields
}
function actorOwner() {
  const generation = actorGeneration, id = platformState.account?.id, token = platformState.token
  return () => !disposed && !!id && !!token && generation === actorGeneration
    && id === platformState.account?.id && token === platformState.token
}
function captureDrafts() {
  for (const item of bugs.value) {
    const base = bases.get(item.id); if (!base) continue
    const edits = Object.fromEntries(editKeys.filter(key => item[key] !== base.fields[key]).map(key => [key, item[key]])) as Partial<EditFields>
    const draft: Draft = { edits, baseFields: { ...base.fields }, baseVersion: base.version,
      comment: comments[item.id] ?? '', closing: !!closing[item.id], disposition: closureDrafts[item.id] ?? '' }
    if (Object.keys(edits).length || draft.comment || draft.closing || draft.disposition) drafts.set(item.id, draft)
    else drafts.delete(item.id)
  }
}
function installRow(row: BugReport, version: number) {
  const draft = drafts.get(row.id), comparison = fields(row)
  if (draft) for (const key of editKeys) if (Object.hasOwn(draft.edits, key)) Object.assign(comparison, { [key]: draft.baseFields[key] })
  bases.set(row.id, { fields: comparison, version: draft?.baseVersion ?? version })
  latestRows.set(row.id, row)
  comments[row.id] = draft?.comment ?? ''; closing[row.id] = draft?.closing ?? false; closureDrafts[row.id] = draft?.disposition ?? ''
  return { ...row, ...draft?.edits }
}
function invalidateRead() {
  ++requestSequence; result.value = null; bugs.value = []; bases.clear(); latestRows.clear(); loadError.value = ''; loading.value = false
}
function currentRow(item: BugReport) { return actorOwner()() && bugs.value.includes(item) && bases.has(item.id) }
function draftConflict(item: BugReport) { return result.value && bases.get(item.id)?.version !== result.value.platformVersion }
function discardDraft(item: BugReport) {
  if (!currentRow(item) || saving[item.id] || !result.value) return
  const latest = latestRows.get(item.id); if (!latest) return
  captureDrafts(); drafts.delete(item.id)
  bugs.value = bugs.value.map(row => row.id === item.id ? installRow(latest, result.value!.platformVersion) : row)
}

const dispositionLabels: Record<ClosureDisposition, string> = {
  fixed_verified: '修复已复测',
  data_corrected_verified: '数据修正已核验',
  duplicate: '重复反馈',
  rejected: '不成立或证据不足',
}
const actionLabel = (action: string) => ({
  created: '建立反馈', status: '状态变更', priority: '优先级变更', assignee: '负责人变更',
  notes: '处理摘要变更', comment: '追加处理记录', 'fix-commit': '关联修复提交',
  'regression-test': '关联回归测试', 'deployed-version': '记录部署版本',
  'verified-by': '记录验证人', 'verified-at': '记录验证时间', 'duplicate-of': '关联重复问题',
  'data-correction-proof': '已核验的数据修正证据（本地摘要为操作人绑定）',
  'closure-disposition': '记录关闭类型',
} as Record<string, string>)[action] || action
const displayHistoryValue = (value?: string) => value && (dispositionLabels[value as ClosureDisposition] || value)
const closureLabel = (item: BugReport) => item.status !== 'closed' && item.status !== 'resolved'
  ? '处理中'
  : item.closureDisposition ? dispositionLabels[item.closureDisposition] : '历史关闭'

async function load(nextPage = page.value) {
  if (!actorOwner()()) return
  captureDrafts(); invalidateRead()
  page.value = Math.max(1, Math.trunc(nextPage) || 1)
  const request = requestSequence, ownsActor = actorOwner()
  const query = { page: page.value, pageSize, status: status.value, priority: priority.value, search: search.value.trim() }
  const isCurrent = () => ownsActor() && request === requestSequence
  loading.value = true
  try {
    const next = await adminApi.bugsPage(query)
    if (!isCurrent()) return
    if (!Array.isArray(next.items) || next.pageSize !== pageSize
      || ![next.page, next.total, next.platformVersion].every(Number.isSafeInteger)
      || next.page < 1 || next.total < 0 || next.platformVersion < 0
      || next.page > Math.max(1, Math.ceil(next.total / pageSize))
      || next.items.length !== Math.min(pageSize, Math.max(0, next.total - (next.page - 1) * pageSize))
      || next.items.some(item => !item || typeof item.id !== 'string' || !item.id)
      || new Set(next.items.map(item => item.id)).size !== next.items.length) throw new Error('Bug 分页响应无效，请刷新重试')
    result.value = next; page.value = next.page
    bugs.value = next.items.map(item => installRow(item, next.platformVersion))
  } catch (cause) { if (isCurrent()) loadError.value = cause instanceof Error ? cause.message : 'Bug 列表读取失败' }
  finally { if (isCurrent()) loading.value = false }
}
async function update(item: BugReport, body: Parameters<typeof adminApi.updateBug>[1], message: string, closes: boolean) {
  if (!currentRow(item) || !hasPermission('admin.bugs.write') || saving[item.id]) return
  captureDrafts()
  const base = bases.get(item.id)!, submitted = fields(item), submittedComment = comments[item.id] ?? ''
  const submittedClosing = !!closing[item.id], submittedDisposition = closureDrafts[item.id] ?? ''
  const ownsActor = actorOwner(), sequence = (mutationSequences.get(item.id) ?? 0) + 1
  mutationSequences.set(item.id, sequence); saving[item.id] = true
  const isCurrent = () => ownsActor() && mutationSequences.get(item.id) === sequence
  try {
    const updated = await adminApi.updateBug(item.id, { ...body, expectedVersion: base.version })
    if (!isCurrent()) return
    captureDrafts()
    const draft = drafts.get(item.id)
    if (draft) {
      const current = { ...draft.baseFields, ...draft.edits }
      for (const key of editKeys) if (body[key] !== undefined && current[key] === submitted[key]
        && (body[key] === submitted[key] || closes && key === 'status')) delete draft.edits[key]
      if (body.comment !== undefined && draft.comment === submittedComment) draft.comment = ''
      if (closes && draft.closing === submittedClosing && draft.disposition === submittedDisposition) { draft.closing = false; draft.disposition = '' }
      if (!Object.keys(draft.edits).length && !draft.comment && !draft.closing && !draft.disposition) drafts.delete(item.id)
    }
    // Never insert a mutation result into a different filtered page. This
    // temporary model only prevents recapturing already-submitted fields.
    if (bugs.value.some(row => row.id === item.id)) bugs.value = bugs.value.map(row => row.id === item.id ? installRow(updated, base.version) : row)
    notice.value = message
    await load(page.value)
  } catch (cause) {
    if (isCurrent()) notice.value = (cause instanceof Error ? cause.message : '更新失败') + '；未保存草稿已保留，请刷新核对，或明确放弃本条草稿后再编辑。'
  } finally { if (isCurrent()) saving[item.id] = false }
}
async function save(item: BugReport) {
  await update(item, {
      status: item.status, priority: item.priority, assignee: item.assignee, adminNotes: item.adminNotes,
      comment: comments[item.id], fixCommit: item.fixCommit, regressionTest: item.regressionTest,
      deployedVersion: item.deployedVersion, duplicateOf: item.duplicateOf,
    }, `${item.id} 已更新并写入审计记录`, false)
}
function openClosure(item: BugReport) {
  if (!currentRow(item) || !hasPermission('admin.bugs.write') || saving[item.id]) return
  closureDrafts[item.id] = item.closureDisposition || ''
  closing[item.id] = true
}
function cancelClosure(item: BugReport) {
  if (!currentRow(item) || saving[item.id]) return
  closing[item.id] = false
  closureDrafts[item.id] = ''
}
async function closeBug(item: BugReport) {
  if (!currentRow(item) || !hasPermission('admin.bugs.write') || saving[item.id]) return
  const disposition = closureDrafts[item.id]
  if (!disposition) { notice.value = '请先选择关闭类型'; return }
  // The legacy update endpoint treats omitted assignment/notes as clearing.
  // Keep confirmed values; closing must not implicitly save different drafts.
  const confirmed = latestRows.get(item.id)!
  await update(item, {
      status: 'closed', closureDisposition: disposition,
      assignee: confirmed.assignee ?? '', adminNotes: confirmed.adminNotes ?? '',
      fixCommit: disposition === 'fixed_verified' ? item.fixCommit : undefined,
      regressionTest: disposition === 'fixed_verified' ? item.regressionTest : undefined,
      deployedVersion: disposition === 'fixed_verified' ? item.deployedVersion : undefined,
      duplicateOf: disposition === 'duplicate' ? item.duplicateOf : '',
      comment: disposition === 'rejected' ? comments[item.id] : undefined,
    }, `${item.id} 已按“${dispositionLabels[disposition]}”关闭并写入审计记录`, true)
}
function retireActor() {
  ++actorGeneration; invalidateRead(); drafts.clear(); mutationSequences.clear(); page.value = 1; notice.value = ''
  for (const values of [comments, closing, closureDrafts, saving]) Object.keys(values).forEach(key => delete values[key])
}
watch([status, priority, search], () => { captureDrafts(); invalidateRead(); page.value = 1 }, { flush: 'sync' })
watch(() => route.params.bugId, value => { search.value = typeof value === 'string' ? value : ''; void load(1) }, { flush: 'sync' })
watch([() => platformState.account?.id, () => platformState.token, () => platformState.account?.permissionVersion], () => {
  retireActor(); const ownsActor = actorOwner()
  void nextTick(() => { if (mounted && ownsActor()) void load(1) })
}, { flush: 'sync' })
onMounted(() => { mounted = true; void load() })
onBeforeUnmount(() => { disposed = true; retireActor() })
</script>

<template>
  <section class="bugs-page panel">
    <header>
      <div><h2>Bug 分诊与证据闭环</h2><p>日常分诊只填写处理信息；关闭时选择原因，系统会显示对应的必要项目。</p></div>
      <div class="filters">
        <input v-model="search" maxlength="200" placeholder="编号 / 标题 / 玩家 / 对局" @keyup.enter="load(1)">
        <select v-model="status" @change="load(1)"><option value="">全部阶段</option><option value="new">待分诊</option><option value="decision">待裁定</option><option value="implementation">待实施 / 实施中</option><option value="retest">待复测</option><option value="deploy">待部署</option><option value="closed">已关闭</option></select>
        <select v-model="priority" @change="load(1)"><option value="">全部优先级</option><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select>
        <button :disabled="loading" @click="load()">查询</button>
      </div>
    </header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <p v-if="loadError" class="notice" role="alert">{{ loadError }}</p>
    <p v-else-if="loading" class="empty">正在读取 Bug 列表…</p>
    <PagedCollection :items="bugs" :page-size="pageSize" :server-page="page" :server-total="result?.total ?? 0" :busy="loading" @page="load" v-slot="{ items }">
      <article v-for="item in items" :key="item.id" class="bug-row">
        <div class="summary">
          <router-link :to="`/admin/users/bugs/${encodeURIComponent(item.id)}`"><code>{{ item.id }}</code></router-link>
          <h3>{{ item.title }}</h3>
          <span class="closure-state" :data-closed="item.status === 'closed' || item.status === 'resolved'">{{ closureLabel(item) }}</span>
          <small>{{ item.reporterName }} · {{ new Date(item.createdAt).toLocaleString() }}</small>
          <small>客户端 {{ item.clientVersion || item.version || 'unknown-client' }} · 服务端 {{ item.serverVersion || 'legacy-unknown' }} · 引擎 {{ item.engineVersion || 'legacy-unknown' }}</small>
          <p>{{ item.description }}</p>
          <dl v-if="item.fixCommit || item.regressionTest || item.deployedVersion || item.verifiedBy || item.duplicateOf">
            <template v-if="item.fixCommit"><dt>修复提交</dt><dd>{{ item.fixCommit }}</dd></template>
            <template v-if="item.regressionTest"><dt>{{ item.closureDisposition === 'data_corrected_verified' ? '数据核验' : '回归测试' }}</dt><dd>{{ item.regressionTest }}</dd></template>
            <template v-if="item.deployedVersion"><dt>{{ item.closureDisposition === 'data_corrected_verified' ? '核验时版本' : '上线版本' }}</dt><dd>{{ item.deployedVersion }}</dd></template>
            <template v-if="item.verifiedBy && item.verifiedAt"><dt>{{ item.closureDisposition === 'data_corrected_verified' ? '核验记录' : '复测记录' }}</dt><dd>{{ item.verifiedBy }} · {{ new Date(item.verifiedAt).toLocaleString() }}</dd></template>
            <template v-if="item.duplicateOf"><dt>关联反馈</dt><dd>{{ item.duplicateOf }}</dd></template>
          </dl>
          <details><summary>处理记录（{{ item.history.length }}）</summary><ol><li v-for="entry in item.history" :key="entry.id"><b>{{ actionLabel(entry.action) }}</b><span>{{ entry.actorName }} · {{ new Date(entry.createdAt).toLocaleString() }}</span><p>{{ entry.comment || `${displayHistoryValue(entry.fromValue) || '无'} → ${displayHistoryValue(entry.toValue) || '无'}` }}</p></li></ol></details>
        </div>
        <fieldset v-if="hasPermission('admin.bugs.write')" :disabled="saving[item.id]" class="editor">
          <p v-if="draftConflict(item)" class="notice">服务器版本已变化；本条未保存草稿和原提交版本已保留。请核对当前记录，或<button type="button" @click="discardDraft(item)">放弃本条未保存草稿并用当前服务端记录</button>。</p>
          <label><span>处理阶段</span><select v-model="item.status"><option value="new">待分诊</option><option value="decision">待裁定</option><option value="implementation">待实施 / 实施中</option><option value="retest">待复测</option><option value="deploy">待部署</option><option v-if="item.status === 'closed' || item.status === 'resolved'" :value="item.status">已关闭</option></select></label>
          <label><span>优先级</span><select v-model="item.priority"><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select></label>
          <input v-model="item.assignee" placeholder="负责人">
          <textarea v-model="item.adminNotes" rows="2" placeholder="处理摘要"></textarea>
          <textarea v-model="comments[item.id]" rows="2" placeholder="追加处理记录"></textarea>
          <details class="evidence-editor">
            <summary>关联证据（可选）</summary>
            <input v-model="item.fixCommit" placeholder="修复提交">
            <input v-model="item.regressionTest" placeholder="命名回归测试">
            <input v-model="item.deployedVersion" placeholder="当前已上线版本">
            <small>请填写已经上线的版本；系统不会根据修复提交自动推断。</small>
            <input v-model="item.duplicateOf" placeholder="关联的反馈编号">
          </details>
          <button @click="save(item)">保存处理进度</button>
          <button v-if="item.status !== 'closed' && item.status !== 'resolved'" class="close-trigger" @click="openClosure(item)">关闭反馈</button>
          <section v-if="closing[item.id]" class="closure-panel" aria-label="关闭反馈">
            <h4>关闭反馈</h4>
            <select v-model="closureDrafts[item.id]" aria-label="关闭类型">
              <option value="" disabled>选择关闭类型</option>
              <option value="fixed_verified">修复已复测</option>
              <option value="duplicate">重复反馈</option>
              <option value="rejected">不成立或证据不足</option>
            </select>
            <template v-if="closureDrafts[item.id] === 'fixed_verified'">
              <input v-model="item.fixCommit" placeholder="修复提交">
              <input v-model="item.regressionTest" placeholder="命名回归测试">
              <input v-model="item.deployedVersion" placeholder="当前已上线版本">
              <small>复测人与时间会由系统自动记录。</small>
            </template>
            <template v-else-if="closureDrafts[item.id] === 'duplicate'">
              <input v-model="item.duplicateOf" placeholder="另一个真实反馈编号">
            </template>
            <template v-else-if="closureDrafts[item.id] === 'rejected'">
              <textarea v-model="comments[item.id]" rows="3" placeholder="请用一句话说明不成立或证据不足的原因"></textarea>
            </template>
            <div class="closure-actions"><button @click="closeBug(item)">确认关闭</button><button class="secondary" @click="cancelClosure(item)">取消</button></div>
          </section>
        </fieldset>
      </article>
    </PagedCollection>
    <p v-if="result && !bugs.length && !loading" class="empty">暂无符合筛选条件的反馈。</p>
  </section>
</template>

<style scoped>
.panel{padding:20px;border:1px solid #35424a;background:#0e161d}.panel>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.panel h2,.panel h3,.closure-panel h4{margin:0 0 6px}.panel p,.panel small{color:#8d9ba0;line-height:1.6}.filters{display:flex;flex-wrap:wrap;gap:7px}.filters input{min-width:220px}.panel button,.panel input,.panel select,.panel textarea{box-sizing:border-box;min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.bug-row{display:grid;grid-template-columns:minmax(0,1fr) 300px;gap:18px;padding:18px 0;border-top:1px solid #303c43}.summary>a{color:#e1c36e}.closure-state{display:inline-block;margin:2px 0 8px;padding:4px 7px;border:1px solid #52616a;color:#aab8be;font-size:12px}.closure-state[data-closed="true"]{border-color:#357d61;color:#80d6af}.summary dl{display:grid;grid-template-columns:90px minmax(0,1fr);gap:6px;margin:12px 0}.summary dt{color:#77858b}.summary dd{margin:0;overflow-wrap:anywhere}.summary details summary,.evidence-editor>summary{cursor:pointer;color:#d8ba68}.summary li{margin:8px 0}.summary li span{margin-left:8px;color:#77858b;font-size:12px}.editor{display:grid;align-content:start;gap:8px}.editor label{display:grid;gap:4px}.editor label>span{color:#8d9ba0;font-size:12px}.editor button{border-color:#9b8138;color:#f1d576}.evidence-editor{padding:9px;border:1px solid #35424a}.evidence-editor>*:not(summary){width:100%;margin-top:8px}.close-trigger{border-color:#7c4f48!important;color:#efb0a7!important}.closure-panel{display:grid;gap:8px;padding:12px;border:1px solid #7c6332;background:#14110a}.closure-actions{display:grid;grid-template-columns:1fr 1fr;gap:8px}.closure-actions .secondary{border-color:#4b5961;color:#c7d0d4}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}.empty{text-align:center}
@media(max-width:900px){.panel>header{flex-direction:column}.filters,.filters>*{width:100%}.bug-row{grid-template-columns:1fr}}
.bugs-page,.bugs-page>header,.filters,.bug-row,.summary,.editor{box-sizing:border-box;min-width:0;max-width:100%}.filters>*{max-width:100%}.summary{overflow-wrap:anywhere}@media(max-width:900px){.filters input{min-width:0}}
.editor{margin:0;padding:0;border:0}
</style>
