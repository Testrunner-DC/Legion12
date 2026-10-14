<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { adminApi, hasPermission, platformState, type AdminCommand } from '@/l12/platform'
import PagedCollection from './PagedCollection.vue'
type CommandPage = Awaited<ReturnType<typeof adminApi.commandsPage>>
const commands = ref<AdminCommand[]>([]), result = ref<CommandPage | null>(null)
const selected = ref<AdminCommand | null>(null), selectedId = ref('')
const status = ref(''), page = ref(1), pageSize = 10
const loading = ref(false), notice = ref(''), detailLoading = ref(false), detailError = ref('')
let listSequence = 0, selectionSequence = 0, actorGeneration = 0, disposed = false, mounted = false
function actorOwner() {
  const generation = actorGeneration, id = platformState.account?.id, token = platformState.token
  return () => !disposed && !!id && !!token && hasPermission('admin.commands.read')
    && generation === actorGeneration && id === platformState.account?.id && token === platformState.token
}
function queryScope() { return JSON.stringify({ page: page.value, pageSize, status: status.value }) }
function closeDetail() { ++selectionSequence; selected.value = null; selectedId.value = ''; detailLoading.value = false; detailError.value = '' }
function invalidateList() { ++listSequence; result.value = null; commands.value = []; loading.value = false; notice.value = ''; closeDetail() }
async function load(nextPage = page.value) {
  if (!actorOwner()()) return
  invalidateList(); page.value = Math.max(1, Math.trunc(nextPage) || 1)
  const sequence = listSequence, ownsActor = actorOwner()
  const query = { page: page.value, pageSize, status: status.value }
  const isCurrent = () => ownsActor() && sequence === listSequence && status.value === query.status
  loading.value = true
  try {
    const next = await adminApi.commandsPage(query)
    if (!isCurrent()) return
    if (!Array.isArray(next.items) || next.pageSize !== pageSize || ![next.page, next.total].every(Number.isSafeInteger)
      || next.page < 1 || next.total < 0 || next.page > Math.max(1, Math.ceil(next.total / pageSize))
      || next.items.length !== Math.min(pageSize, Math.max(0, next.total - (next.page - 1) * pageSize))
      || next.items.some(item => !item || typeof item.id !== 'string' || !item.id)
      || new Set(next.items.map(item => item.id)).size !== next.items.length) throw new Error('命令分页响应无效，请刷新重试')
    result.value = next; page.value = next.page; commands.value = next.items
  } catch (cause) { if (isCurrent()) notice.value = cause instanceof Error ? cause.message : '命令记录读取失败' }
  finally { if (isCurrent()) loading.value = false }
}
async function show(id: string) {
  if (!actorOwner()() || loading.value || !result.value || !commands.value.some(item => item.id === id)) return
  closeDetail(); selectedId.value = id; detailLoading.value = true
  const sequence = selectionSequence, list = listSequence, scope = queryScope(), ownsActor = actorOwner()
  const isCurrent = () => ownsActor() && sequence === selectionSequence && list === listSequence
    && scope === queryScope() && selectedId.value === id && commands.value.some(item => item.id === id)
  try {
    const next = await adminApi.command(id)
    if (!isCurrent()) return
    if (!next || next.id !== id) throw new Error('命令详情回执不匹配，请重新选择')
    selected.value = next
  } catch (cause) { if (isCurrent()) detailError.value = cause instanceof Error ? cause.message : '命令详情读取失败' }
  finally { if (isCurrent()) detailLoading.value = false }
}
watch(status, () => { invalidateList(); page.value = 1 }, { flush: 'sync' })
function retireActor() { ++actorGeneration; invalidateList(); page.value = 1 }
watch([() => platformState.account?.id, () => platformState.token, () => platformState.account?.permissionVersion,
  () => hasPermission('admin.commands.read')], () => {
  retireActor(); const ownsActor = actorOwner()
  void nextTick(() => { if (mounted && ownsActor()) void load(1) })
}, { flush: 'sync' })
onMounted(() => { mounted = true; void load() })
onBeforeUnmount(() => { disposed = true; retireActor() })
</script>
<template>
  <section class="commands-page">
    <header><div><h2>管理操作记录</h2><p>新操作直接执行并写入审计；历史待处理请求不会自动执行。执行结果、关联编号和失败原因集中查看。</p></div><div><select v-model="status" @change="load(1)"><option value="">全部状态</option><option value="requested">历史待处理</option><option value="executed">已执行</option><option value="failed">失败</option><option value="rejected">已拒绝</option></select><button :disabled="loading" @click="load()">刷新</button></div></header>
    <p v-if="notice" class="notice" role="alert">{{ notice }}</p><p v-else-if="loading">正在读取命令记录…</p>
    <div class="layout">
      <section class="panel">
        <PagedCollection :items="commands" :page-size="pageSize" :server-page="page" :server-total="result?.total ?? 0" :busy="loading" @page="load" v-slot="{ items }"><button v-for="command in items" :key="command.id" class="row" @click="show(command.id)"><code>{{ command.id }}</code><b>{{ command.type }}</b><span>{{ command.actorName }} · {{ command.status }}</span><small v-if="command.failureReason">失败：{{ command.failureReason }}</small></button></PagedCollection>
        <p v-if="result && !commands.length && !loading">暂无命令记录。</p>
      </section>
      <section class="panel detail">
        <header v-if="selectedId"><h3>命令详情</h3><button @click="closeDetail">关闭</button></header>
        <p v-if="detailLoading">正在读取命令详情…</p><p v-else-if="detailError" class="notice" role="alert">{{ detailError }}</p>
        <template v-else-if="selected"><dl><dt>类型 / 状态</dt><dd>{{ selected.type }} · {{ selected.status }}</dd><dt>权限 / 作用域</dt><dd>{{ selected.permission }} · {{ selected.scope }}</dd><dt>关联 ID</dt><dd><code>{{ selected.correlationId }}</code></dd><dt>结果</dt><dd>{{ selected.resultMessage || selected.failureReason || '待处理' }}</dd></dl><pre>{{ JSON.stringify({ payload: selected.payload, result: selected.result }, null, 2) }}</pre></template>
        <p v-else>选择一条命令查看详情。</p>
      </section>
    </div>
  </section>
</template>
<style scoped>
.commands-page{display:grid;gap:14px}.commands-page>header,.commands-page>header>div{display:flex;align-items:flex-start;justify-content:space-between;gap:10px}.commands-page h2,.commands-page h3{margin:0 0 6px}.commands-page p,.commands-page span,.commands-page small{color:#89979d}.commands-page button,.commands-page select{min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.layout{display:grid;grid-template-columns:minmax(320px,.8fr) minmax(0,1.2fr);gap:14px}.panel{min-width:0;padding:16px;border:1px solid #35424a;background:#0e161d}.row{display:grid;width:100%;grid-template-columns:minmax(0,1fr) minmax(120px,.7fr);gap:5px;border-width:1px 0 0!important;text-align:left}.row code,.row small{grid-column:1/-1;overflow-wrap:anywhere}.detail>header{display:flex;justify-content:space-between}.detail dl{display:grid;grid-template-columns:120px minmax(0,1fr);gap:8px}.detail dd{margin:0;overflow-wrap:anywhere}.detail pre{overflow:auto;padding:10px;background:#080e13;color:#aeb8bc}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
@media(max-width:850px){.commands-page>header{flex-direction:column}.layout{grid-template-columns:1fr}.detail dl{grid-template-columns:1fr}}
.commands-page,.commands-page>header,.commands-page>header>div,.layout,.panel,.row,.detail pre{box-sizing:border-box;min-width:0;max-width:100%}.row b,.row span{min-width:0;overflow-wrap:anywhere}.commands-page>header>div:last-child{flex-wrap:wrap}
</style>
