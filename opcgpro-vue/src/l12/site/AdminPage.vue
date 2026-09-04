<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { adminApi, apiBase, authState, canAccessAdmin, hasPermission, platformState, refreshCurrentAccount, type AdminApproval, type AdminAudit, type AdminCommand, type AtomicAbility, type AtomicCardEffect, type AtomicCoverage, type AuditArchiveOperation, type AuditArchiveRecovery, type AuditArchiveSegment, type BugReport, type ContentBatch, type ContentBatchPreview, type ContentEntry, type EffectAtomDescriptor, type PlatformAccount, type ReleaseEnvironment, type ReleaseOperation, type ReleaseRun, type SecurityStatus, type VerifiedReleaseArtifact } from '@/l12/platform'
import { createHomeContent, homeContentFields } from './homeContent'
import CardImage from '@/l12/CardImage.vue'
import AdminArticlesPanel from './AdminArticlesPanel.vue'
import AdminOperationsPanel from './AdminOperationsPanel.vue'
import AdminRankedIntegrityPanel from './AdminRankedIntegrityPanel.vue'

type AdminTab = 'overview' | 'bugs' | 'accounts' | 'content' | 'articles' | 'effects' | 'releases' | 'commands' | 'audit' | 'integrity' | 'security' | 'operations'
const tab = ref<AdminTab>('overview')
const bugs = ref<BugReport[]>([])
const accounts = ref<PlatformAccount[]>([])
const statusFilter = ref('')
const priorityFilter = ref('')
const bugSearch = ref('')
const bugComments = reactive<Record<string, string>>({})
const reviewNotes = reactive<Record<string, string>>({})
const notice = ref('')
const content = reactive(createHomeContent())
const ruleNotice = ref('')
const contentEntries = reactive<Record<string, ContentEntry>>({})
const audits = ref<AdminAudit[]>([])
const auditCategory = ref('')
const auditOutcome = ref('')
const auditActorId = ref('')
const auditCommandId = ref('')
const auditCorrelationId = ref('')
const commands = ref<AdminCommand[]>([])
const approvals = ref<AdminApproval[]>([])
const selectedCommand = ref<AdminCommand | null>(null)
const commandStatus = ref('')
const approvalReasons = reactive<Record<string, string>>({})
const contentPreview = ref<ContentBatchPreview | null>(null)
const contentBatches = ref<ContentBatch[]>([])
const managedContentKeys = [...homeContentFields.map(field => field.key), 'rules.notice']
const effectCards = ref<AtomicCardEffect[]>([])
const effectAtoms = ref<EffectAtomDescriptor[]>([])
const effectCoverage = ref<AtomicCoverage | null>(null)
const selectedEffect = ref<AtomicCardEffect | null>(null)
const effectSearch = ref('')
const effectStatus = ref('')
const effectProduct = ref('')
const effectAtomKind = ref('')
const effectTotal = ref(0)
const effectPage = ref(1)
const effectLoading = ref(false)
const releaseArtifacts = ref<VerifiedReleaseArtifact[]>([])
const releaseEnvironments = ref<ReleaseEnvironment[]>([])
const releaseRuns = ref<ReleaseRun[]>([])
const selectedReleaseEnvironment = ref('staging')
const selectedReleaseArtifact = ref('')
const releaseReason = ref('')
const releasePreview = ref<ReleaseOperation | null>(null)
const releaseLoading = ref(false)
const securityStatus = ref<SecurityStatus | null>(null)
const auditArchives = ref<AuditArchiveSegment[]>([])
const auditArchivePreview = ref<AuditArchiveOperation | null>(null)
const auditRecovery = ref<AuditArchiveRecovery | null>(null)
const auditRetentionDays = ref(365)
const auditArchiveReason = ref('定期安全审计归档')
const accountStatusReasons = reactive<Record<string, string>>({})

async function loadBugs() { try { bugs.value = await adminApi.bugs({ status: statusFilter.value, priority: priorityFilter.value, search: bugSearch.value }) } catch (error) { notice.value = error instanceof Error ? error.message : '加载失败' } }
async function loadAccounts() { try { accounts.value = await adminApi.accounts() } catch (error) { notice.value = error instanceof Error ? error.message : '加载失败' } }
async function updateBug(item: BugReport) { try { const updated = await adminApi.updateBug(item.id, { status: item.status, priority: item.priority, assignee: item.assignee, adminNotes: item.adminNotes, comment: bugComments[item.id] }); bugs.value = bugs.value.map(bug => bug.id === updated.id ? updated : bug); bugComments[item.id] = ''; notice.value = `${item.id} 已更新并写入审计记录` } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' } }
function bugActionLabel(action: string) { return ({ created: '建立反馈', status: '状态变更', priority: '优先级变更', assignee: '负责人变更', notes: '处理摘要变更', comment: '追加处理记录' } as Record<string, string>)[action] || action }
function matchJsonUrl(matchId: string) { return `${apiBase()}/api/matches/${encodeURIComponent(matchId)}` }
async function setRole(account: PlatformAccount) { try { const result = await adminApi.setRole(account.id, account.role as 'player' | 'admin', account.permissionVersion); notice.value = result.changed ? `${account.username} 的角色已更新为${result.role === 'admin' ? '管理员' : '玩家'}并写入审计` : '角色没有变化'; await loadAccounts() } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' } }
async function revokeAccountSessions(account: PlatformAccount) {
  try { const result = await adminApi.revokeSessions(account.id); notice.value = `${account.username} 已撤销 ${result.revokedCount} 个会话` }
  catch (error) { notice.value = error instanceof Error ? error.message : '撤销会话失败' }
}
async function setAccountStatus(account: PlatformAccount) {
  const reason = accountStatusReasons[account.id]?.trim()
  if (!reason) { notice.value = '请先填写账号状态变更理由'; return }
  try {
    const result = await adminApi.setAccountStatus(account.id, !account.disabled, reason, account.permissionVersion)
    notice.value = `${result.account.username} 已${result.account.disabled ? '禁用' : '启用'}；撤销 ${result.revokedSessions} 个会话并写入审计`
    accountStatusReasons[account.id] = ''
    await loadAccounts(); await loadSecurity()
  } catch (error) { notice.value = error instanceof Error ? error.message : '账号状态命令提交失败' }
}
async function loadContent() {
  for (const field of homeContentFields) {
    try { const entry = await adminApi.getContent(field.key); contentEntries[field.key] = entry; content[field.id] = entry.draftValue || field.defaultValue } catch {}
  }
  try { const entry = await adminApi.getContent('rules.notice'); contentEntries['rules.notice'] = entry; ruleNotice.value = entry.draftValue } catch {}
  try { contentBatches.value = await adminApi.contentBatches() } catch {}
}
async function saveContentDrafts(showNotice = true) {
  try {
    const entries: ContentEntry[] = []
    for (const field of homeContentFields) entries.push(await adminApi.saveContentDraft(field.key, content[field.id]))
    entries.push(await adminApi.saveContentDraft('rules.notice', ruleNotice.value))
    entries.forEach(entry => { contentEntries[entry.key] = entry })
    if (showNotice) notice.value = '草稿已保存，尚未影响官网'
    return true
  } catch (error) { notice.value = error instanceof Error ? error.message : '保存失败'; return false }
}
async function previewContent() {
  try { if (hasPermission('admin.content.draft') && !(await saveContentDrafts(false))) return; contentPreview.value = await adminApi.previewContent(managedContentKeys); notice.value = '预览完成，未修改已发布内容' }
  catch (error) { notice.value = error instanceof Error ? error.message : '预览失败' }
}
async function publishContent() {
  try {
    if (!(await saveContentDrafts(false))) return
    const result = await adminApi.publishContent(managedContentKeys)
    if ('commandId' in result) notice.value = `批量发布已提交双人审批（命令 ${result.commandId}）`
    else notice.value = result.applied ? '官网内容已发布' : '干运行完成'
    await loadControlPlane()
  } catch (error) { notice.value = error instanceof Error ? error.message : '发布失败' }
}
async function rollbackContent(batch: ContentBatch) {
  try {
    const result = await adminApi.rollbackContent(batch.id)
    notice.value = 'commandId' in result ? `回滚已提交双人审批（命令 ${result.commandId}）` : '内容已回滚'
    await loadControlPlane()
  } catch (error) { notice.value = error instanceof Error ? error.message : '回滚失败' }
}
async function reviewAbility(ability: AtomicAbility, status: string, note = '') { if (!selectedEffect.value) return; try { await adminApi.reviewEffect(selectedEffect.value.cardId, { abilityId: ability.abilityId, status, note }); selectedEffect.value = await adminApi.effect(selectedEffect.value.cardId); effectCards.value = effectCards.value.map(card => card.cardId === selectedEffect.value?.cardId ? selectedEffect.value : card); notice.value = `${selectedEffect.value.cardId} ABILITY ${ability.sequence} 审查状态已记录` } catch (error) { notice.value = error instanceof Error ? error.message : '审查记录失败' } }
async function loadAudit() { try { audits.value = await adminApi.audit({ category: auditCategory.value, outcome: auditOutcome.value, actorId: auditActorId.value, commandId: auditCommandId.value, correlationId: auditCorrelationId.value }) } catch (error) { notice.value = error instanceof Error ? error.message : '审计日志加载失败' } }
async function loadControlPlane() {
  try {
    if (hasPermission('admin.commands.read')) commands.value = await adminApi.commands({ status: commandStatus.value })
    if (hasPermission('admin.approvals.read')) approvals.value = await adminApi.approvals('requested')
    if (hasPermission('admin.content.read')) contentBatches.value = await adminApi.contentBatches()
  } catch (error) { notice.value = error instanceof Error ? error.message : '管理操作记录加载失败' }
}
async function resetAccountPassword(account: PlatformAccount) {
  const reason = accountStatusReasons[account.id]?.trim()
  if (!reason) { notice.value = '请先填写账号安全操作理由'; return }
  if (!window.confirm(`确认将 ${account.username} 的密码重置为临时密码 123456，并撤销全部会话？`)) return
  try {
    const result = await adminApi.resetAccountPassword(account.id, reason, account.permissionVersion)
    notice.value = `${result.account.username} 已重置为临时密码 123456，已撤销 ${result.revokedSessions} 个会话；下次登录必须修改密码`
    accountStatusReasons[account.id] = ''; await loadAccounts(); await loadSecurity()
  } catch (error) { notice.value = error instanceof Error ? error.message : '管理员密码重置失败' }
}
async function deleteAccount(account: PlatformAccount) {
  const reason = accountStatusReasons[account.id]?.trim()
  if (!reason) { notice.value = '请先填写账号删除与数据清理理由'; return }
  if (!window.confirm(`确认逻辑删除 ${account.username} 并清理其邮箱、牌库、好友等个人数据？此操作不能在后台恢复。`)) return
  try {
    const result = await adminApi.deleteAccount(account.id, reason, account.permissionVersion)
    notice.value = `账号已逻辑删除；撤销 ${result.revokedSessions} 个会话，清理 ${result.removedPrivateRecords} 条私有记录与 ${result.cleanedMatchRecords ?? 0} 场对局身份，并保留脱敏审计`
    accountStatusReasons[account.id] = ''; await loadAccounts(); await loadSecurity()
  } catch (error) { notice.value = error instanceof Error ? error.message : '账号删除失败' }
}
async function showCommand(id: string) { try { selectedCommand.value = await adminApi.command(id) } catch (error) { notice.value = error instanceof Error ? error.message : '命令详情加载失败' } }
async function reviewCommand(commandId: string, decision: 'approve' | 'reject') {
  try { selectedCommand.value = await adminApi.reviewApproval(commandId, decision, approvalReasons[commandId] || ''); notice.value = decision === 'approve' ? '受控命令已审批并执行' : '受控命令已拒绝'; await loadControlPlane(); if (hasPermission('admin.content.read')) await loadContent(); if (hasPermission('releases.read') || hasPermission('releases.runtime.read')) await loadReleases() }
  catch (error) { notice.value = error instanceof Error ? error.message : '审批失败' }
}
function availableReleaseArtifacts() { return releaseArtifacts.value.filter(item => item.environments.includes(selectedReleaseEnvironment.value)) }
function selectedEnvironment() { return releaseEnvironments.value.find(item => item.environment === selectedReleaseEnvironment.value) }
function ensureReleaseArtifact() {
  if (!availableReleaseArtifacts().some(item => item.id === selectedReleaseArtifact.value)) selectedReleaseArtifact.value = availableReleaseArtifacts()[0]?.id || ''
}
async function loadReleases() {
  releaseLoading.value = true
  try {
    if (hasPermission('releases.read')) {
      releaseArtifacts.value = await adminApi.releaseArtifacts()
      releaseRuns.value = await adminApi.releaseRuns()
    }
    if (hasPermission('releases.runtime.read')) releaseEnvironments.value = await adminApi.releaseEnvironments()
    if (!releaseEnvironments.value.some(item => item.environment === selectedReleaseEnvironment.value)) selectedReleaseEnvironment.value = releaseEnvironments.value[0]?.environment || 'staging'
    ensureReleaseArtifact()
  } catch (error) { notice.value = error instanceof Error ? error.message : '发布控制面加载失败' }
  finally { releaseLoading.value = false }
}
async function submitRelease(dryRun: boolean) {
  const environment = selectedEnvironment()
  if (!environment || !selectedReleaseArtifact.value) { notice.value = '请选择目标环境和已验证工件'; return }
  if (!releaseReason.value.trim()) { notice.value = '请填写发布理由'; return }
  try {
    const result = await adminApi.deployRelease(selectedReleaseArtifact.value, environment.environment, environment.version, dryRun, releaseReason.value.trim())
    if ('commandId' in result) notice.value = `发布已提交双人审批（命令 ${result.commandId}）`
    else { releasePreview.value = result; notice.value = '发布 dry-run 完成，未执行激活' }
    await loadControlPlane(); await loadReleases()
  } catch (error) { notice.value = error instanceof Error ? error.message : '发布命令提交失败' }
}
async function rollbackRelease(run: ReleaseRun, dryRun: boolean) {
  const environment = releaseEnvironments.value.find(item => item.environment === run.environment)
  if (!environment) { notice.value = '目标环境运行态不可用，请刷新'; return }
  if (!releaseReason.value.trim()) { notice.value = '请填写回滚理由'; return }
  try {
    const result = await adminApi.rollbackRelease(run.id, environment.version, dryRun, releaseReason.value.trim())
    if ('commandId' in result) notice.value = `回滚已提交双人审批（命令 ${result.commandId}）`
    else { releasePreview.value = result; notice.value = '回滚 dry-run 完成，未执行激活' }
    await loadControlPlane(); await loadReleases()
  } catch (error) { notice.value = error instanceof Error ? error.message : '回滚命令提交失败' }
}
async function loadSecurity() {
  try {
    const [status, archives] = await Promise.all([adminApi.securityStatus(), adminApi.auditArchives()])
    securityStatus.value = status; auditArchives.value = archives; auditRetentionDays.value = status.auditRetentionDays
  } catch (error) { notice.value = error instanceof Error ? error.message : '安全治理状态加载失败' }
}
async function archiveAudit(dryRun: boolean) {
  if (!securityStatus.value) { notice.value = '请先刷新安全状态'; return }
  if (!auditArchiveReason.value.trim()) { notice.value = '请填写审计归档理由'; return }
  try {
    const result = await adminApi.archiveAudit(auditRetentionDays.value, securityStatus.value.platformVersion,
      dryRun, auditArchiveReason.value.trim())
    if ('commandId' in result) notice.value = `审计归档已提交双人审批（命令 ${result.commandId}）`
    else { auditArchivePreview.value = result; notice.value = `归档 dry-run 完成：${result.eligibleEvents} 条可归档事件，未写文件` }
    await loadControlPlane(); await loadSecurity()
  } catch (error) { notice.value = error instanceof Error ? error.message : '审计归档命令失败' }
}
async function rehearseAuditRecovery() {
  try { auditRecovery.value = await adminApi.rehearseAuditRecovery(); notice.value = auditRecovery.value.success ? '审计归档恢复演练通过' : `恢复演练失败：${auditRecovery.value.error || '未知错误'}` }
  catch (error) { notice.value = error instanceof Error ? error.message : '恢复演练失败' }
}
async function loadEffects(resetPage = false) {
  if (resetPage) effectPage.value = 1
  effectLoading.value = true
  try {
    const [page, atoms] = await Promise.all([
      adminApi.effects({ search: effectSearch.value, status: effectStatus.value, product: effectProduct.value, atomKind: effectAtomKind.value, page: effectPage.value, pageSize: 50 }),
      effectAtoms.value.length ? Promise.resolve(effectAtoms.value) : adminApi.effectAtoms(),
    ])
    effectCards.value = page.items; effectTotal.value = page.total; effectCoverage.value = page.coverage; effectAtoms.value = atoms
    if (selectedEffect.value) selectedEffect.value = page.items.find(card => card.cardId === selectedEffect.value?.cardId) ?? selectedEffect.value
  } catch (error) { notice.value = error instanceof Error ? error.message : '卡效清单加载失败' }
  finally { effectLoading.value = false }
}
async function selectEffect(card: AtomicCardEffect) {
  try { selectedEffect.value = await adminApi.effect(card.cardId) } catch (error) { notice.value = error instanceof Error ? error.message : '卡效详情加载失败' }
}
function statusLabel(status: string) { return ({ 'no-effect': '无卡效', 'legacy-backed': '旧实现兜底', 'partially-atomized': '部分原子化', 'declarative-ready': '声明就绪', 'runtime-migrated': '运行时迁移', verified: '已验证' } as Record<string, string>)[status] || status }
function reviewLabel(status: string) { return ({ confirmed: '人工确认', 'human-assisted': '人工辅助', rejected: '退回修正', unreviewed: '待人工审查' } as Record<string, string>)[status] || status }
function atomDescriptor(kind: string) { return effectAtoms.value.find(atom => atom.kind === kind) }
function previousEffectsPage() { if (effectPage.value > 1) { effectPage.value--; loadEffects() } }
function nextEffectsPage() { if (effectPage.value * 50 < effectTotal.value) { effectPage.value++; loadEffects() } }
async function initializeAdminPage() {
  try { await refreshCurrentAccount() }
  catch { return }
  if (!canAccessAdmin.value) return
  if (hasPermission('admin.content.read')) loadContent()
  if (hasPermission('admin.effects.read')) loadEffects()
  if (hasPermission('admin.bugs.read')) loadBugs()
  if (hasPermission('admin.accounts.read')) loadAccounts()
  if (hasPermission('admin.audit.read')) loadAudit()
  if (hasPermission('admin.security.read')) loadSecurity()
  if (hasPermission('admin.commands.read')) loadControlPlane()
  if (hasPermission('releases.read') || hasPermission('releases.runtime.read')) loadReleases()
}
onMounted(() => { void initializeAdminPage() })
</script>

<template>
  <div class="admin-page">
    <header><div><small>ADMINISTRATION</small><h1>管理后台</h1><p>账号权限、Bug 闭环、官网内容与运营配置。</p></div><router-link to="/me">← 返回我的</router-link></header>
    <section v-if="!authState.initialized || authState.refreshing" class="denied"><b>正在验证管理员权限</b><span>管理数据只会在服务端身份确认后加载。</span></section>
    <section v-else-if="!canAccessAdmin" class="denied"><b>需要管理员权限</b><span>请先在“我的”页面登录管理员账号。</span></section>
    <template v-else>
      <div class="admin-shell">
      <aside class="admin-sidebar">
        <nav><small>总览</small><button :class="{ active: tab === 'overview' }" @click="tab = 'overview'">▦ 后台概览</button></nav>
        <nav><small>用户与反馈</small><button v-if="hasPermission('admin.accounts.read')" :class="{ active: tab === 'accounts' }" @click="tab = 'accounts'; loadAccounts()">♙ 账号与会话</button><button v-if="hasPermission('admin.bugs.read')" :class="{ active: tab === 'bugs' }" @click="tab = 'bugs'; loadBugs()">⚑ Bug 管理</button></nav>
        <nav><small>站点内容</small><button v-if="hasPermission('admin.content.read')" :class="{ active: tab === 'content' }" @click="tab = 'content'; loadContent()">▤ 官网内容</button><button v-if="hasPermission('admin.content.read')" :class="{ active: tab === 'articles' }" @click="tab = 'articles'">✎ 资讯发布</button></nav>
        <nav><small>游戏与赛事运营</small><button v-if="hasPermission('admin.operations.read')" :class="{ active: tab === 'operations' }" @click="tab = 'operations'">⚙ 游戏运营配置</button><router-link to="/battle/tournaments">♜ 赛事中心</router-link><button v-if="hasPermission('admin.commands.read')" :class="{ active: tab === 'commands' }" @click="tab = 'commands'; loadControlPlane()">⌁ 管理操作记录</button></nav>
        <nav><small>卡牌与规则</small><button v-if="hasPermission('admin.effects.read')" :class="{ active: tab === 'effects' }" @click="tab = 'effects'; loadEffects()">◇ 卡效原子化</button></nav>
        <nav><small>系统与治理</small><button v-if="hasPermission('releases.read') || hasPermission('releases.runtime.read')" :class="{ active: tab === 'releases' }" @click="tab = 'releases'; loadReleases()">⇧ 软件发布</button><button v-if="hasPermission('admin.security.read')" :class="{ active: tab === 'security' }" @click="tab = 'security'; loadSecurity()">◆ 安全状态</button><button v-if="hasPermission('admin.audit.read')" :class="{ active: tab === 'integrity' }" @click="tab = 'integrity'">⚖ 排位完整性</button><button v-if="hasPermission('admin.audit.read')" :class="{ active: tab === 'audit' }" @click="tab = 'audit'; loadAudit()">≡ 审计日志</button></nav>
      </aside>
      <main class="admin-content">
      <section v-if="tab === 'overview'" class="overview-grid">
        <header class="panel"><div><small>CONTROL CENTER</small><h2>运营总览</h2><p>这里只显示摘要和入口；配置编辑只在对应模块内进行。</p></div></header>
        <button class="overview-card" @click="tab='bugs'; loadBugs()"><small>用户与反馈</small><b>{{ bugs.filter(item => item.status !== 'resolved' && item.status !== 'closed').length }}</b><span>未闭环 Bug</span></button>
        <button class="overview-card" @click="tab='accounts'; loadAccounts()"><small>账号与会话</small><b>{{ accounts.length }}</b><span>平台账号</span></button>
        <button class="overview-card" @click="tab='content'; loadContent()"><small>官网内容</small><b>{{ Object.values(contentEntries).filter(item => item.status === 'draft').length }}</b><span>固定文案草稿</span></button>
        <button class="overview-card" @click="tab='articles'"><small>资讯发布</small><b>稿件</b><span>独立编辑、发布与版本恢复</span></button>
        <button class="overview-card" @click="tab='operations'"><small>游戏运营</small><b>版本化</b><span>赛季、天灾、禁限卡、模式与维护</span></button>
        <router-link class="overview-card" to="/battle/tournaments"><small>赛事运营</small><b>临时职权</b><span>主办者与裁判仅对当场赛事生效</span></router-link>
        <button class="overview-card" @click="tab='effects'; loadEffects()"><small>卡牌与规则</small><b>{{ effectCoverage?.verifiedAbilities ?? 0 }}</b><span>已验证原子能力</span></button>
        <button class="overview-card" @click="tab='releases'; loadReleases()"><small>系统与发布</small><b>{{ releaseEnvironments.length }}</b><span>受监控环境</span></button>
        <button class="overview-card" @click="tab='audit'; loadAudit()"><small>安全与审计</small><b>{{ audits.length }}</b><span>当前查询记录</span></button>
      </section>
      <section v-else-if="tab === 'bugs'" class="panel">
        <header><h2>Bug 反馈</h2><div class="bug-filters"><input v-model="bugSearch" placeholder="编号 / 标题 / 玩家 / 房间 / 对局" @keyup.enter="loadBugs"><select v-model="statusFilter" @change="loadBugs"><option value="">全部状态</option><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="priorityFilter" @change="loadBugs"><option value="">全部优先级</option><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><button @click="loadBugs">查询</button></div></header>
        <article v-for="item in bugs" :key="item.id" class="bug-row"><div class="bug-summary"><code>{{ item.id }}</code><b>{{ item.title }}</b><span>{{ item.reporterName }} · {{ new Date(item.createdAt).toLocaleString() }} · {{ item.version }}</span><p>{{ item.description }}</p><small>{{ item.page }}<template v-if="item.roomCode"> · 房间 {{ item.roomCode }}</template><template v-if="item.matchId"> · <a :href="matchJsonUrl(item.matchId)" target="_blank" rel="noopener">查看对局 JSON {{ item.matchId }}</a></template></small><details class="bug-history"><summary>处理记录（{{ item.history.length }}）</summary><ol><li v-for="audit in item.history" :key="audit.id"><b>{{ bugActionLabel(audit.action) }}</b><span>{{ audit.actorName }} · {{ new Date(audit.createdAt).toLocaleString() }}</span><p v-if="audit.comment">{{ audit.comment }}</p><code v-else-if="audit.fromValue !== audit.toValue">{{ audit.fromValue || '无' }} → {{ audit.toValue || '无' }}</code></li></ol></details></div><div class="bug-admin"><select v-model="item.status"><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="item.priority"><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><input v-model="item.assignee" placeholder="负责人"/><textarea v-model="item.adminNotes" rows="3" placeholder="当前处理摘要"/><textarea v-model="bugComments[item.id]" rows="3" placeholder="追加处理记录（保存后进入时间线）"/><button @click="updateBug(item)">保存并记录</button></div></article>
        <div v-if="!bugs.length" class="empty">暂无符合筛选条件的反馈</div>
      </section>
      <section v-else-if="tab === 'accounts'" class="panel"><header><div><h2>账号、权限与会话</h2><p>账号变更立即执行并完整审计；状态、密码重置与逻辑删除均撤销相关会话，根 Admin 与操作者自身受保护。</p></div><button @click="loadAccounts(); loadSecurity()">刷新</button></header><div class="account-row head"><b>用户名 / 状态</b><span>建立时间</span><span>长期身份</span><span>有效权限</span><span>操作</span></div><div v-for="account in accounts" :key="account.id" class="account-row"><b>{{ account.username }}<small :data-disabled="account.disabled">{{ account.deleted ? '已逻辑删除' : account.disabled ? '已禁用' : '正常' }}<template v-if="account.mustChangePassword"> · 必须修改密码</template><template v-if="account.emailVerified"> · 邮箱 {{ account.emailMasked }}</template><template v-if="account.disabledReason"> · {{ account.disabledReason }}</template></small></b><span>{{ new Date(account.createdAt).toLocaleString() }}</span><select v-model="account.role" :disabled="account.username === 'Admin' || account.deleted"><option value="player">玩家</option><option value="admin">管理员</option></select><small :title="account.permissions?.join('\n')">{{ account.permissions?.length ?? 0 }} 项</small><span class="account-actions"><input v-if="hasPermission('admin.accounts.status.write') && !account.deleted" v-model="accountStatusReasons[account.id]" placeholder="状态 / 重置 / 删除理由"/><button :disabled="account.username === 'Admin' || account.deleted" @click="setRole(account)">保存身份</button><button v-if="hasPermission('admin.accounts.status.write') && !account.deleted" class="status" :data-disabled="account.disabled" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="setAccountStatus(account)">{{ account.disabled ? '启用账号' : '禁用账号' }}</button><button v-if="!account.deleted" class="revoke" @click="revokeAccountSessions(account)">撤销会话</button><button v-if="hasPermission('admin.accounts.status.write') && !account.deleted" class="reset" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="resetAccountPassword(account)">重置密码</button><button v-if="hasPermission('admin.accounts.status.write') && !account.deleted" class="delete" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="deleteAccount(account)">删除与清理</button></span></div></section>
      <section v-else-if="tab === 'content'" class="panel content-editor">
        <header><div><h2>官网内容</h2><p>草稿受白名单保护；正式批量发布与回滚必须由另一位审批人复核。</p></div><span class="content-actions"><button v-if="hasPermission('admin.content.draft')" @click="saveContentDrafts()">保存草稿</button><button @click="previewContent">预览 / dry-run</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publishContent">提交批量发布</button></span></header>
        <div v-if="contentPreview" class="content-preview"><b>发布预览（未写入）</b><span v-for="item in contentPreview.items" :key="item.key"><code>{{ item.key }}</code><em>{{ item.wouldChange ? '将更新' : '无变化' }}</em><small>{{ item.publishedValue || '空' }} → {{ item.draftValue || '空' }}</small></span></div>
        <label v-for="field in homeContentFields" :key="field.key">{{ field.label }}<em :data-status="contentEntries[field.key]?.status">{{ contentEntries[field.key]?.status === 'draft' ? '有未发布草稿' : '已发布' }}</em><textarea v-if="field.multiline" v-model="content[field.id]" :rows="field.rows ?? 4"/><input v-else v-model="content[field.id]"/></label>
        <label>规则页公告<em :data-status="contentEntries['rules.notice']?.status">{{ contentEntries['rules.notice']?.status === 'draft' ? '有未发布草稿' : '已发布' }}</em><textarea v-model="ruleNotice" rows="5"/></label>
        <div class="content-history"><h3>发布与回滚批次</h3><article v-for="batch in contentBatches" :key="batch.id"><span><code>{{ batch.id }}</code><b>{{ batch.action }} · {{ batch.status }}</b><small>{{ batch.actorName }} · {{ new Date(batch.createdAt).toLocaleString() }} · {{ batch.items.length }} 项</small></span><button v-if="batch.action === 'publish' && batch.status === 'published' && hasPermission('admin.content.rollback')" @click="rollbackContent(batch)">提交回滚审批</button></article><div v-if="!contentBatches.length" class="empty">尚无发布批次</div></div>
      </section>
      <AdminArticlesPanel v-else-if="tab === 'articles'"/>
      <section v-else-if="tab === 'effects'" class="effects-workbench">
        <div v-if="effectCoverage" class="coverage-strip">
          <article><small>卡牌</small><b>{{ effectCoverage.totalCards }}</b><span>{{ effectCoverage.cardsWithText }} 张含效果</span></article>
          <article><small>能力</small><b>{{ effectCoverage.totalAbilities }}</b><span>按原文时点拆分</span></article>
          <article><small>原子</small><b>{{ effectCoverage.totalAtoms }}</b><span>{{ effectAtoms.length }} 种注册类型</span></article>
          <article class="coverage-warning"><small>旧实现兜底</small><b>{{ effectCoverage.legacyBackedAbilities }}</b><span>迁移完成前保留</span></article>
          <article class="coverage-ready"><small>声明就绪</small><b>{{ effectCoverage.declarativeReadyAbilities }}</b><span>仍需逐卡等价验证</span></article>
          <article class="coverage-verified"><small>实战已验证</small><b>{{ effectCoverage.verifiedAbilities }}</b><span>已由原子程序接管</span></article>
        </div>
        <div class="effects-layout">
          <section class="panel effects-list">
            <header><div><h2>全卡效能力清单</h2><p>后台与规则内核读取同一份原子注册表；状态不会因画出节点而自动视为已迁移。</p></div><button @click="loadEffects()">刷新</button></header>
            <div class="effect-filters">
              <input v-model="effectSearch" placeholder="卡号 / 卡名 / 原文" @keyup.enter="loadEffects(true)"/>
              <select v-model="effectProduct" @change="loadEffects(true)"><option value="">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option></select>
              <select v-model="effectStatus" @change="loadEffects(true)"><option value="">全部状态</option><option value="verified">实战已验证</option><option value="legacy-backed">旧实现兜底</option><option value="partially-atomized">部分原子化</option><option value="declarative-ready">声明就绪</option><option value="no-effect">无卡效</option></select>
              <select v-model="effectAtomKind" @change="loadEffects(true)"><option value="">全部原子</option><option v-for="atom in effectAtoms" :key="atom.kind" :value="atom.kind">{{ atom.label }}</option></select>
              <button @click="loadEffects(true)">查询</button>
            </div>
            <div class="effect-scroll" tabindex="0" aria-label="全卡效能力清单，可上下滚动">
              <div class="effect-table-head"><span>卡牌</span><span>组合</span><span>迁移状态</span></div>
              <button v-for="card in effectCards" :key="card.cardId" class="effect-row" :class="{ selected: selectedEffect?.cardId === card.cardId }" @click="selectEffect(card)">
                <span class="effect-identity"><CardImage :card-id="card.cardId" :legacy-url="card.imageUrl" :alt="card.name" intent="thumb" fit="cover" object-position="center 30%"/><span><code>{{ card.cardId }}</code><b>{{ card.name }}</b><small>{{ card.faction }} · {{ card.cardType }}</small></span></span>
                <span class="effect-count"><b>{{ card.abilities.length }}</b> 能力 / <b>{{ card.atomCount }}</b> 原子<small v-if="card.legacyAtomCount">{{ card.legacyAtomCount }} 个兜底节点</small><em class="review-pill" :data-review="card.reviewStatus">{{ reviewLabel(card.reviewStatus) }}</em></span>
                <span class="status-pill" :data-status="card.migrationStatus">{{ statusLabel(card.migrationStatus) }}</span>
              </button>
              <div v-if="effectLoading" class="empty">正在读取原子清单…</div>
            </div>
            <div class="effect-pagination"><button :disabled="effectPage <= 1" @click="previousEffectsPage">上一页</button><span>第 {{ effectPage }} 页 · 共 {{ effectTotal }} 张</span><button :disabled="effectPage * 50 >= effectTotal" @click="nextEffectsPage">下一页</button></div>
          </section>
          <section class="panel effect-detail">
            <template v-if="selectedEffect">
              <header><div><small>{{ selectedEffect.cardId }} · {{ selectedEffect.product }}</small><h2>{{ selectedEffect.name }}</h2><p>{{ selectedEffect.faction }} · {{ selectedEffect.cardType }}</p></div><span class="effect-header-status"><em class="review-pill" :data-review="selectedEffect.reviewStatus">{{ reviewLabel(selectedEffect.reviewStatus) }}</em><span class="status-pill" :data-status="selectedEffect.migrationStatus">{{ statusLabel(selectedEffect.migrationStatus) }}</span></span></header>
              <div class="original-text"><b>卡面原文</b><p class="l12-effect-body">{{ selectedEffect.effectText || '无效果文本' }}</p></div>
              <article v-for="ability in selectedEffect.abilities" :key="ability.abilityId" class="ability-card">
                <header><span><small>ABILITY {{ ability.sequence }}</small><b>{{ ability.trigger }}</b><em class="execution-model">{{ ability.executionModel }}</em></span><span class="effect-header-status"><em class="review-pill" :data-review="ability.reviewStatus">{{ reviewLabel(ability.reviewStatus) }}</em><span class="status-pill" :data-status="ability.migrationStatus">{{ statusLabel(ability.migrationStatus) }}</span></span></header>
                <p class="l12-effect-body">{{ ability.text }}</p>
                <div class="atom-flow">
                  <template v-for="(atom, index) in ability.atoms" :key="atom.atomId">
                    <article class="atom-node" :data-category="atomDescriptor(atom.kind)?.category" :class="{ legacy: atom.kind === 'legacy.resolve' }" :title="atomDescriptor(atom.kind)?.description">
                      <small>{{ atom.stage }} · {{ atomDescriptor(atom.kind)?.category || '原子' }}</small><b>{{ atom.label }}</b><code>{{ atom.kind }}</code>
                      <dl v-if="Object.keys(atom.parameters).length"><template v-for="(value, key) in atom.parameters" :key="key"><dt>{{ key }}</dt><dd>{{ value }}</dd></template></dl>
                    </article><span v-if="index < ability.atoms.length - 1" class="flow-arrow">→</span>
                  </template>
                </div>
                <div class="ability-review"><textarea v-model="reviewNotes[ability.abilityId]" rows="2" placeholder="人工核对备注（规则书、FAQ、测试证据）"/><button @click="reviewAbility(ability, 'human-assisted', reviewNotes[ability.abilityId])">标记人工辅助</button><button class="confirm" @click="reviewAbility(ability, 'confirmed', reviewNotes[ability.abilityId])">确认拆分</button><button class="reject" @click="reviewAbility(ability, 'rejected', reviewNotes[ability.abilityId])">退回修正</button></div>
                <details><summary>线性执行与迁移守卫</summary><ol><li v-for="atom in ability.atoms" :key="`trace-${atom.atomId}`"><b>{{ atom.order }}. {{ atom.label }}</b><span>{{ atomDescriptor(atom.kind)?.kernelContract }}</span></li></ol><p v-if="ability.hasLegacyFallback" class="legacy-note">执行到 <code>legacy.resolve</code> 时只调用旧权威分支；新旧实现不会同时结算。</p></details>
              </article>
              <details class="raw-definition"><summary>查看原子定义 JSON</summary><pre>{{ JSON.stringify(selectedEffect, null, 2) }}</pre></details>
            </template>
            <div v-else class="empty detail-empty">从左侧选择一张卡牌，查看原文、能力拆分、流程图、参数与迁移守卫。</div>
          </section>
        </div>
      </section>
      <section v-else-if="tab === 'releases'" class="release-workbench">
        <section class="panel release-compose">
          <header><div><h2>声明式发布编排</h2><p>只能选择服务端适配器提供的已验证工件；页面不接受路径、命令、凭据或自报 verified。</p></div><button @click="loadReleases">{{ releaseLoading ? '读取中…' : '刷新' }}</button></header>
          <div class="release-form">
            <label>环境<select v-model="selectedReleaseEnvironment" @change="ensureReleaseArtifact"><option v-for="environment in releaseEnvironments" :key="environment.environment" :value="environment.environment">{{ environment.environment }} · v{{ environment.version }}</option></select></label>
            <label>已验证工件<select v-model="selectedReleaseArtifact"><option v-for="artifact in availableReleaseArtifacts()" :key="artifact.id" :value="artifact.id">{{ artifact.id }} · {{ artifact.commit.slice(0, 10) }}</option></select></label>
            <label>变更理由<input v-model="releaseReason" placeholder="审批人与审计可见"/></label>
            <span class="release-actions"><button :disabled="!hasPermission('releases.execute') || !selectedReleaseArtifact" @click="submitRelease(true)">dry-run</button><button class="confirm" :disabled="!hasPermission('releases.execute') || !selectedReleaseArtifact" @click="submitRelease(false)">提交双人审批</button></span>
          </div>
          <div v-if="releasePreview" class="release-preview"><b>干运行计划（未激活）</b><span>{{ releasePreview.plan.action }} · {{ releasePreview.plan.environment }} · v{{ releasePreview.plan.environmentVersion }}</span><ol><li v-for="step in releasePreview.plan.steps" :key="step">{{ step }}</li></ol></div>
        </section>
        <section class="panel release-runtime">
          <header><div><h2>运行态只读快照</h2><p>状态、健康检查与 WebSocket 冒烟来自显式适配器观测。</p></div></header>
          <div class="release-environments"><article v-for="environment in releaseEnvironments" :key="environment.environment" :data-state="environment.state"><small>{{ environment.environment }} · v{{ environment.version }}</small><b>{{ environment.state }}</b><code>{{ environment.activeArtifactId || '未激活' }}</code><span>HTTP {{ environment.health.success ? '✓' : '×' }} {{ environment.health.code }} · {{ environment.health.durationMs }}ms</span><span>WS {{ environment.webSocket.success ? '✓' : '×' }} {{ environment.webSocket.code }} · {{ environment.webSocket.durationMs }}ms</span><em>{{ environment.adapterConfigured ? '适配器已配置' : '适配器未配置' }}</em></article></div>
        </section>
        <section v-if="hasPermission('releases.read')" class="panel release-artifacts">
          <header><div><h2>已验证工件</h2><p>清单与 hash 验证证据由受信适配器注入，Web 端没有注册入口。</p></div></header>
          <article v-for="artifact in releaseArtifacts" :key="artifact.id"><span><code>{{ artifact.id }}</code><b>{{ artifact.commit }}</b><small>{{ new Date(artifact.verifiedAt).toLocaleString() }}</small></span><span><b>{{ artifact.verificationGates.join(' · ') }}</b><small>{{ artifact.environments.join(' / ') }}</small></span><code>sha256 {{ artifact.releaseSha256 }}</code></article><div v-if="!releaseArtifacts.length" class="empty">适配器未提供可发布工件</div>
        </section>
        <section v-if="hasPermission('releases.read')" class="panel release-runs">
          <header><div><h2>发布、失败与回滚记录</h2><p>每次激活、健康/WS 冒烟和回滚结果均持久化。</p></div></header>
          <article v-for="run in releaseRuns" :key="run.id"><span><code>{{ run.id }}</code><b>{{ run.action }} · {{ run.environment }} · {{ run.status }}</b><small>{{ run.artifactId }} · v{{ run.environmentVersion }} · {{ new Date(run.completedAt).toLocaleString() }}</small></span><span class="release-checks"><em v-for="check in run.checks" :key="check.kind" :data-ok="check.success">{{ check.kind }} {{ check.success ? '✓' : '×' }} · {{ check.code }}</em></span><span v-if="run.status === 'succeeded' && hasPermission('releases.execute')" class="release-actions"><button @click="rollbackRelease(run, true)">回滚 dry-run</button><button class="reject" @click="rollbackRelease(run, false)">提交回滚审批</button></span></article><div v-if="!releaseRuns.length" class="empty">尚无发布运行记录</div>
        </section>
      </section>
      <section v-else-if="tab === 'commands'" class="command-workbench">
        <section v-if="hasPermission('admin.approvals.read')" class="panel approval-panel"><header><div><h2>受控发布待复核</h2><p>仅官网正式发布、版本发布和审计归档进入双人复核；账号与赛事操作直接生效并记录审计。</p></div><button @click="loadControlPlane">刷新</button></header><article v-for="approval in approvals" :key="approval.commandId" class="approval-row"><span><code>{{ approval.commandId }}</code><b>{{ approval.requesterName }}</b><small>{{ new Date(approval.requestedAt).toLocaleString() }}</small></span><input v-model="approvalReasons[approval.commandId]" placeholder="复核理由"/><button @click="showCommand(approval.commandId)">详情</button><button :disabled="approval.requesterId === platformState.account?.id" class="confirm" @click="reviewCommand(approval.commandId, 'approve')">批准并执行</button><button :disabled="approval.requesterId === platformState.account?.id" class="reject" @click="reviewCommand(approval.commandId, 'reject')">拒绝</button></article><div v-if="!approvals.length" class="empty">暂无受控发布待复核</div></section>
        <section class="panel command-panel"><header><h2>命令记录</h2><div><select v-model="commandStatus" @change="loadControlPlane"><option value="">全部状态</option><option value="requested">待审批</option><option value="executed">已执行</option><option value="failed">失败</option><option value="rejected">已拒绝</option></select><button @click="loadControlPlane">刷新</button></div></header><article v-for="command in commands" :key="command.id" class="command-row" @click="showCommand(command.id)"><code>{{ command.id }}</code><b>{{ command.type }}</b><span>{{ command.actorName }} · {{ command.status }} · v{{ command.resourceVersion }}</span><small v-if="command.failureReason">失败：{{ command.failureReason }}</small></article><div v-if="!commands.length" class="empty">暂无命令记录</div></section>
        <section v-if="selectedCommand" class="panel command-detail"><header><h2>命令详情</h2><button @click="selectedCommand = null">关闭</button></header><dl><dt>类型 / 状态</dt><dd>{{ selectedCommand.type }} · {{ selectedCommand.status }}</dd><dt>权限 / 作用域</dt><dd>{{ selectedCommand.permission }} · {{ selectedCommand.scope }}</dd><dt>关联 ID</dt><dd><code>{{ selectedCommand.correlationId }}</code></dd><dt>结果</dt><dd>{{ selectedCommand.resultMessage || selectedCommand.failureReason || '待处理' }}</dd></dl><pre>{{ JSON.stringify({ payload: selectedCommand.payload, result: selectedCommand.result }, null, 2) }}</pre></section>
      </section>
      <section v-else-if="tab === 'security'" class="security-workbench">
        <section class="panel security-summary"><header><div><h2>安全治理快照</h2><p>平台只有管理员与玩家两种持久身份；这里监控受控发布、账号安全与独立审计状态。</p></div><button @click="loadSecurity">刷新</button></header><div v-if="securityStatus" class="security-metrics"><article><small>发布复核管理员</small><b>{{ securityStatus.secondApproverReady ? '就绪' : '缺失' }}</b><span>{{ securityStatus.activeApprovers }} 名可复核管理员</span></article><article><small>高风险审计</small><b>{{ securityStatus.highRiskAuditAvailable ? '可用' : '失败关闭' }}</b><span>保留 {{ securityStatus.auditRetentionDays }} 天</span></article><article><small>账号 / 锁定</small><b>{{ securityStatus.disabledAccounts }} / {{ securityStatus.activeLoginLocks }}</b><span>禁用账号 / 登录锁定</span></article><article><small>待复核发布</small><b>{{ securityStatus.pendingApprovals }}</b><span>平台版本 v{{ securityStatus.platformVersion }}</span></article></div><div v-if="securityStatus?.alerts.length" class="security-alerts"><article v-for="alert in securityStatus.alerts" :key="alert.code" :data-severity="alert.severity"><b>{{ alert.code }} · {{ alert.count }}</b><span>{{ alert.message }}</span></article></div></section>
        <section class="panel security-boundaries"><header><h2>发布恢复与 MFA 边界</h2></header><article><b>发布复核离线恢复</b><span>{{ securityStatus?.offlineBootstrapUsed ? '一次性恢复已使用' : securityStatus?.offlineBootstrapEnabled && securityStatus?.offlineBootstrapCredentialConfigured ? '离线恢复入口已显式启用' : '默认关闭或凭据未配置' }}</span><p>仅服务器 CLI 可用，只服务受控发布恢复；账号与赛事不需要另一人审批。</p></article><article><b>MFA</b><span>{{ securityStatus?.mfa.enrollmentEnabled ? '已启用' : '尚未启用' }} · 保护器 {{ securityStatus?.mfa.mode || 'unavailable' }}</span><p>{{ securityStatus?.mfa.requirement || '加载中' }}</p><p>当前不会收集或写入 TOTP 明文，也不会伪造恢复码能力。</p></article></section>
        <section class="panel security-archive"><header><div><h2>独立审计归档与恢复演练</h2><p>归档为内部派生路径的校验 JSONL；主审计事件不删除，客户端不能指定文件路径。</p></div><button @click="rehearseAuditRecovery">恢复演练</button></header><div v-if="hasPermission('admin.audit.archive')" class="archive-form"><label>保留天数<input v-model.number="auditRetentionDays" type="number" min="30" max="3650"/></label><label>归档理由<input v-model="auditArchiveReason" maxlength="500"/></label><button @click="archiveAudit(true)">dry-run（不写）</button><button class="confirm" @click="archiveAudit(false)">提交双人审批</button></div><div v-if="auditArchivePreview" class="archive-preview">可归档 {{ auditArchivePreview.eligibleEvents }} 条 · 截止 {{ new Date(auditArchivePreview.archiveBefore).toLocaleString() }} · 源事件保留</div><div v-if="auditRecovery" class="archive-recovery" :data-ok="auditRecovery.success">恢复演练 {{ auditRecovery.success ? '通过' : '失败' }} · {{ auditRecovery.segments }} 段 / {{ auditRecovery.events }} 条<small v-if="auditRecovery.error">{{ auditRecovery.error }}</small></div><article v-for="segment in auditArchives" :key="segment.id" class="archive-row"><code>{{ segment.id }}</code><span>{{ new Date(segment.from).toLocaleString() }} → {{ new Date(segment.until).toLocaleString() }}</span><b>{{ segment.eventCount }} 条</b><small>sha256 {{ segment.sha256 }}</small></article><div v-if="!auditArchives.length" class="empty">尚无审计归档段</div></section>
      </section>
      <section v-else-if="tab === 'audit'" class="panel audit-panel"><header><h2>管理与安全审计</h2><div class="audit-filters"><select v-model="auditCategory"><option value="">全部类型</option><option value="account">账号权限</option><option value="session">会话安全</option><option value="security">权限拒绝</option><option value="command">管理命令</option><option value="approval">审批</option><option value="content">内容发布</option><option value="effect">卡效确认</option><option value="release">发布编排</option></select><select v-model="auditOutcome"><option value="">全部结果</option><option value="succeeded">成功</option><option value="pending">待审批</option><option value="executed">已执行</option><option value="failed">失败</option><option value="denied">拒绝</option></select><input v-model="auditActorId" placeholder="操作者 ID"/><input v-model="auditCommandId" placeholder="命令 ID"/><input v-model="auditCorrelationId" placeholder="关联 ID"/><button @click="loadAudit">查询</button></div></header><div class="audit-head"><span>时间 / 操作者</span><span>动作 / 结果</span><span>对象</span><span>变更 / 证据</span></div><article v-for="audit in audits" :key="audit.id" class="audit-row"><span>{{ new Date(audit.createdAt).toLocaleString() }}<small>{{ audit.actorName }}</small></span><b>{{ audit.category }} · {{ audit.action }}<small>{{ audit.outcome || 'success' }}<template v-if="audit.permission"> · {{ audit.permission }}</template></small></b><code>{{ audit.target }}</code><span>{{ audit.fromValue || '无' }} → {{ audit.toValue || '无' }}<small v-if="audit.comment">{{ audit.comment }}</small><small v-if="audit.reason">原因：{{ audit.reason }}</small><code v-if="audit.correlationId">关联 ID：{{ audit.correlationId }}</code><code v-if="audit.commandId">命令：{{ audit.commandId }}</code></span></article><div v-if="!audits.length" class="empty">暂无管理操作记录</div></section>
      <AdminRankedIntegrityPanel v-else-if="tab === 'integrity'"/>
      <AdminOperationsPanel v-else-if="tab === 'operations' && hasPermission('admin.operations.read')" @notice="notice = $event"/>
      <section v-else class="denied"><b>当前模块不可用</b><span>账号没有该模块的服务端权限，或模块尚未选择。</span></section>
      <p v-if="notice" class="notice">{{ notice }}</p>
      </main>
      </div>
    </template>
  </div>
</template>

<style scoped>
.admin-page{min-height:100%;padding:30px clamp(18px,3vw,46px) 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.admin-page>header{display:flex;align-items:flex-start;justify-content:space-between}.admin-page small{color:#d5b85e;font:900 9px monospace;letter-spacing:.16em}.admin-page h1{margin:5px 0;font-size:30px}.admin-page p{color:#7d898e;font-size:11px;line-height:1.7}.admin-page>header a{color:#e1c36e;text-decoration:none;font-size:11px;font-weight:900}.admin-shell{display:grid;grid-template-columns:220px minmax(0,1fr);gap:16px;margin-top:22px}.admin-sidebar{align-self:start;position:sticky;top:16px;display:grid;gap:5px;padding:12px;border:1px solid #35424a;background:#0b1218}.admin-sidebar nav{display:grid;gap:4px;padding:9px 0;border-bottom:1px solid #26323a}.admin-sidebar nav:last-child{border-bottom:0}.admin-sidebar nav small{padding:0 8px 5px;color:#68757b}.admin-sidebar button,.admin-sidebar a{box-sizing:border-box;width:100%;padding:10px;border:1px solid transparent;background:transparent;color:#9da8ad;text-align:left;text-decoration:none;font:900 11px 'Microsoft YaHei'}.admin-sidebar button:hover,.admin-sidebar a:hover,.admin-sidebar button.active{border-color:#6d5d31;background:#211b0e;color:#f0d579}.admin-content{min-width:0}.overview-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.overview-grid>header{grid-column:1/-1}.overview-card{display:flex;min-height:130px;flex-direction:column;align-items:flex-start;justify-content:flex-end;gap:7px;padding:17px;border:1px solid #35424a;background:#101821;color:#fff;text-align:left;text-decoration:none}.overview-card:hover{border-color:#c1a44e;background:#171b1d}.overview-card b{font-size:22px}.overview-card span{color:#87949a;font-size:10px}.panel,.denied{border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel button,.panel select,.panel input,.panel textarea{border:1px solid #4c5961;background:#080e13;color:#fff;font:700 11px 'Microsoft YaHei';padding:9px}.bug-row{display:grid;grid-template-columns:1fr 280px;gap:18px;padding:18px 0;border-bottom:1px solid #303c43}.bug-summary code{color:#dfc36f}.bug-summary b,.bug-summary span,.bug-summary small{display:block}.bug-summary b{margin:7px 0;font-size:16px}.bug-summary span,.bug-summary small{color:#718087;font-size:9px}.bug-summary p{color:#c7ccca;white-space:pre-wrap;overflow-wrap:anywhere}.bug-admin{display:grid;grid-template-columns:1fr 1fr;gap:7px}.bug-admin input,.bug-admin textarea,.bug-admin button{grid-column:1/-1}.account-row{display:grid;grid-template-columns:1fr 1.5fr 180px 90px minmax(320px,1fr);align-items:center;gap:10px;padding:12px;border-bottom:1px solid #303c43}.account-row.head{color:#7e8a90;font-size:9px}.content-editor label{display:block;margin-top:16px;color:#b8c0c1;font-size:10px;font-weight:900}.content-editor input,.content-editor textarea{box-sizing:border-box;width:100%;margin-top:7px}.denied{display:flex;flex-direction:column;gap:7px;margin-top:22px}.denied span,.empty{color:#7e8a90}.notice{position:sticky;z-index:20;bottom:12px;margin-top:12px;padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
.bug-filters{display:flex;flex-wrap:wrap;gap:7px}.bug-filters input{min-width:240px}.bug-summary a{color:#83d5e4}.bug-history{margin-top:14px;border-top:1px solid #2e3b42;padding-top:10px}.bug-history summary{cursor:pointer;color:#d6bd70;font-size:10px;font-weight:900}.bug-history ol{max-height:220px;overflow:auto;padding-left:20px}.bug-history li{margin:8px 0}.bug-history li b,.bug-history li span{display:inline;margin-right:7px}.bug-history li p{margin:3px 0}.bug-history li code{display:block;color:#a8b3b5}
.coverage-strip{display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px}.coverage-strip article{display:flex;flex-direction:column;gap:4px;padding:15px;border:1px solid #39474e;background:#0c141a}.coverage-strip b{font-size:24px}.coverage-strip span{color:#75838a;font-size:9px}.coverage-strip .coverage-warning{border-color:#7b4936;background:#21140f}.coverage-strip .coverage-ready{border-color:#2c6754;background:#0c1c17}.effects-workbench{min-width:0}.effects-layout{display:grid;grid-template-columns:minmax(480px,.9fr) minmax(540px,1.1fr);gap:12px;min-width:0}.effects-list,.effect-detail{min-width:0}.effects-list{display:flex;max-height:calc(100vh - 215px);min-height:560px;flex-direction:column;overflow:hidden}.effects-list>header{flex:none;gap:12px;min-width:0}.effects-list>header>div{min-width:0}.effects-list>header button{flex:none}.effects-list>header p{margin:3px 0;overflow-wrap:anywhere}.effect-filters{display:flex;flex:none;flex-wrap:wrap;gap:7px;margin:14px 0}.effect-filters input{flex:1 1 220px}.effect-filters select{flex:1 1 112px}.effect-filters button{flex:0 0 auto}.effect-filters input,.effect-filters select,.effect-filters button{box-sizing:border-box;min-width:0}.effect-scroll{min-height:0;flex:1;overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain;padding-right:5px;scrollbar-gutter:stable}.effect-scroll:focus-visible{outline:1px solid #d8b95f;outline-offset:2px}.effect-table-head,.effect-row{display:grid;grid-template-columns:minmax(220px,1fr) minmax(125px,145px) minmax(92px,110px);align-items:center;gap:10px}.effect-table-head{padding:7px 10px;color:#6f7d84;font-size:9px;font-weight:900}.effect-row{box-sizing:border-box;width:100%;margin-top:4px;padding:8px 10px!important;text-align:left}.effect-row.selected{border-color:#d8b95f;background:#241e11}.effect-identity{display:flex;align-items:center;min-width:0;gap:9px}.effect-identity .l12-card-image{width:38px;height:52px;border:1px solid #56636a}.effect-identity>span{display:flex;min-width:0;flex-direction:column}.effect-identity code{color:#d8bd6a;font-size:9px}.effect-identity b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.effect-identity small{color:#708087!important;letter-spacing:0!important}.effect-count{display:flex;min-width:0;flex-wrap:wrap;gap:3px;color:#9aa4a7;font-size:10px}.effect-count small{width:100%;color:#c17d60!important;letter-spacing:0!important}.status-pill,.review-pill{justify-self:start;padding:5px 7px;border:1px solid #516068;background:#151e24;color:#b9c2c4;font-size:9px;font-style:normal;font-weight:900;white-space:nowrap}.status-pill[data-status="partially-atomized"]{border-color:#9a742a;background:#2b210d;color:#f0cf73}.status-pill[data-status="legacy-backed"]{border-color:#84424b;background:#291116;color:#ef8994}.status-pill[data-status="declarative-ready"],.status-pill[data-status="verified"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.review-pill[data-review="human-assisted"]{border-color:#72539a;background:#20152c;color:#d9baff}.review-pill[data-review="confirmed"]{border-color:#2f7b89;background:#0a2027;color:#7bd8e7}.review-pill[data-review="unreviewed"]{color:#7d8b91}.effect-header-status{display:flex;flex:none;align-items:center;justify-content:flex-end;gap:6px}.effect-pagination{display:flex;flex:none;align-items:center;justify-content:center;gap:12px;margin-top:14px}.effect-pagination span{color:#78858a;font-size:9px}.effect-detail>header{gap:12px}.effect-detail>header>div{min-width:0}.effect-detail>header>div small{display:block}.original-text{margin:14px 0;padding:14px;border-left:3px solid #d7b85d;background:#0a1116}.original-text p{margin:7px 0 0;color:#cdd2d0}.ability-card{margin-top:12px;padding:13px;border:1px solid #35434a;background:#0a1117}.ability-card>header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.ability-card>header>span:first-child{display:flex;min-width:0;flex-direction:column;gap:3px}.ability-card>header small{letter-spacing:.12em}.ability-card>p{color:#c5ccca;overflow-wrap:anywhere}.atom-flow{display:flex;align-items:stretch;gap:5px;overflow-x:auto;padding:8px 1px 13px}.atom-node{flex:0 0 150px;padding:10px;border:1px solid #3e5965;background:#0d1b22}.atom-node[data-category="费用"]{border-color:#85652d;background:#241d0f}.atom-node[data-category="选择"]{border-color:#5f4385;background:#1c1328}.atom-node[data-category="结算"],.atom-node[data-category="数值"]{border-color:#296b69;background:#0b2423}.atom-node.legacy{border-color:#934452;background:#2b1017}.atom-node>small,.atom-node>b,.atom-node>code{display:block}.atom-node>b{margin:5px 0}.atom-node>code{color:#83949b;font-size:8px}.atom-node dl{display:grid;grid-template-columns:auto 1fr;gap:3px;margin:8px 0 0;font-size:8px}.atom-node dt{color:#6e7d82}.atom-node dd{overflow:hidden;margin:0;color:#c6ccca;text-overflow:ellipsis;white-space:nowrap}.flow-arrow{align-self:center;color:#c8a94e;font-size:18px}.ability-card details,.raw-definition{margin-top:10px;border-top:1px solid #2e3a40;padding-top:9px}.ability-card summary,.raw-definition summary{cursor:pointer;color:#d6bd70;font-size:10px;font-weight:900}.ability-card ol{padding-left:20px}.ability-card li{margin:7px 0;color:#c8cecc;font-size:10px}.ability-card li span{display:block;color:#718087}.legacy-note{padding:8px;border-left:2px solid #a04755;background:#251016;color:#e19aa4!important}.raw-definition pre{max-height:360px;overflow:auto;padding:12px;background:#05090c;color:#aeb9b9;font-size:9px;white-space:pre-wrap}.detail-empty{display:grid;min-height:400px;place-items:center;text-align:center}
.coverage-strip{grid-template-columns:repeat(6,1fr)}.coverage-strip .coverage-verified{border-color:#2f7b89;background:#0a2027}
.content-actions{display:flex;gap:7px}.content-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.content-editor label>em{float:right;padding:3px 6px;border:1px solid #3e5c4f;color:#7fd3ae;font-size:8px;font-style:normal}.content-editor label>em[data-status="draft"]{border-color:#876328;color:#efca70}.ability-review{display:grid;grid-template-columns:1fr auto auto auto;gap:6px;margin-top:10px}.ability-review textarea{min-width:0;resize:vertical}.ability-review .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.ability-review .reject{border-color:#84424b;background:#291116;color:#ef8994}.review-pill[data-review="rejected"]{border-color:#84424b;background:#291116;color:#ef8994}.audit-head,.audit-row{display:grid;grid-template-columns:1.25fr .8fr 1fr 1.5fr;gap:12px;padding:10px}.audit-head{color:#77858b;font-size:9px;font-weight:900}.audit-row{align-items:start;border-top:1px solid #303c43;color:#c8cecc;font-size:10px}.audit-row span,.audit-row small{display:block}.audit-row small{margin-top:4px;color:#77858b}.audit-row code{color:#dfc36f;overflow-wrap:anywhere}.account-row{grid-template-columns:1fr 1.3fr 160px 80px minmax(470px,auto)}.account-row>b small{display:block;margin-top:4px;color:#74c99f!important;letter-spacing:0!important}.account-row>b small[data-disabled="true"]{color:#ef8994!important}.account-actions{display:grid;grid-template-columns:minmax(150px,1fr) repeat(5,auto);gap:6px}.account-actions input{min-width:0}.account-actions .status[data-disabled="false"],.account-actions .revoke,.account-actions .delete{border-color:#7e3c45;background:#2b1116;color:#eab5bb}.account-actions .status[data-disabled="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.account-actions .reset{border-color:#8a6b32;background:#20190d;color:#e6cb7b}.account-actions button:disabled{opacity:.4}
.news-editor{margin-top:18px;padding:14px;border:1px solid #526269;background:#0b1218}.news-editor>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:10px;border-bottom:1px solid #344149}.news-editor h3{margin:0}.news-editor>article{margin-top:12px;padding:12px;border:1px solid #35434a;background:#101a21}.news-editor-row{display:grid;grid-template-columns:minmax(220px,1fr) 130px 190px;gap:8px}.news-editor footer{display:flex;align-items:center;gap:14px;margin-top:10px}.news-editor footer label{display:flex;align-items:center;gap:5px;margin:0}.news-editor footer input{width:auto;margin:0}.news-editor .delete{margin-left:auto;border-color:#84424b;background:#291116;color:#ef8994}
.security-workbench{display:grid;grid-template-columns:1.1fr .9fr;gap:12px}.security-summary,.security-archive{grid-column:1/-1}.security-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:14px}.security-metrics article,.security-boundaries article{display:flex;flex-direction:column;gap:5px;padding:13px;border:1px solid #35434a;background:#0a1117}.security-metrics b{font-size:18px}.security-metrics span,.security-boundaries span,.security-boundaries p{color:#859197;font-size:9px}.security-alerts{display:grid;gap:6px;margin-top:10px}.security-alerts article{display:flex;justify-content:space-between;gap:12px;padding:9px;border:1px solid #7a5c2f;background:#21190d;color:#e7ca79;font-size:9px}.security-alerts article[data-severity="critical"]{border-color:#84424b;background:#291116;color:#ef8994}.security-boundaries{display:grid;gap:8px}.security-boundaries>header{grid-column:1/-1}.security-boundaries article p{margin:0}.archive-form{display:grid;grid-template-columns:140px minmax(240px,1fr) auto auto;align-items:end;gap:7px;margin:14px 0}.archive-form label{display:flex;flex-direction:column;gap:5px;color:#829096;font-size:9px}.archive-form input{box-sizing:border-box;width:100%}.archive-form .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.archive-preview,.archive-recovery{margin:8px 0;padding:9px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584;font-size:9px}.archive-recovery[data-ok="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.archive-recovery small{display:block;margin-top:4px;letter-spacing:0!important}.archive-row{display:grid;grid-template-columns:minmax(210px,1fr) 1.2fr auto minmax(260px,1.2fr);gap:9px;padding:10px;border-top:1px solid #303c43;font-size:9px}.archive-row code,.archive-row small{overflow-wrap:anywhere;color:#dfc36f;letter-spacing:0!important}
.content-preview{display:grid;gap:5px;margin-top:14px;padding:12px;border:1px solid #6b5a2d;background:#1d180c}.content-preview>span{display:grid;grid-template-columns:minmax(180px,1fr) 70px 2fr;gap:8px;font-size:9px}.content-preview em{color:#e5c96f;font-style:normal}.content-preview small{overflow:hidden;color:#929d9f!important;letter-spacing:0!important;text-overflow:ellipsis;white-space:nowrap}.content-history{margin-top:22px;border-top:1px solid #36434a;padding-top:14px}.content-history article{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:9px;border-bottom:1px solid #303c43}.content-history article span{display:flex;min-width:0;flex-direction:column}.content-history code{color:#dfc36f}.content-history small{letter-spacing:0!important}.command-workbench{display:grid;grid-template-columns:1fr 1fr;gap:12px}.approval-panel{grid-column:1/-1}.approval-row{display:grid;grid-template-columns:minmax(230px,1fr) minmax(180px,1fr) auto auto auto;align-items:center;gap:7px;padding:10px;border-bottom:1px solid #303c43}.approval-row span{display:flex;flex-direction:column}.approval-row code{color:#dfc36f}.approval-row small{letter-spacing:0!important}.approval-row .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.approval-row .reject{border-color:#84424b;background:#291116;color:#ef8994}.command-row{display:grid;grid-template-columns:minmax(210px,1fr) 1fr 1fr;gap:9px;padding:10px;border-bottom:1px solid #303c43;cursor:pointer}.command-row:hover{background:#172129}.command-row code{color:#dfc36f}.command-row small{grid-column:1/-1;color:#ef8994!important;letter-spacing:0!important}.command-detail dl{display:grid;grid-template-columns:120px 1fr;gap:7px;font-size:10px}.command-detail dt{color:#78858a}.command-detail dd{margin:0}.command-detail pre{max-height:420px;overflow:auto;padding:12px;background:#05090c;color:#aeb9b9;font-size:9px;white-space:pre-wrap}.audit-filters{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:6px}.audit-filters input{width:150px}
.release-workbench{display:grid;grid-template-columns:1fr 1fr;gap:12px}.release-compose{grid-column:1/-1}.release-form{display:grid;grid-template-columns:180px minmax(260px,1fr) minmax(260px,1fr) auto;align-items:end;gap:8px;margin-top:14px}.release-form label{display:flex;min-width:0;flex-direction:column;gap:5px;color:#829096;font-size:9px;font-weight:900}.release-form select,.release-form input{box-sizing:border-box;width:100%}.release-actions{display:flex;gap:6px}.release-actions .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.release-actions .reject{border-color:#84424b;background:#291116;color:#ef8994}.release-preview{margin-top:12px;padding:12px;border:1px solid #6b5a2d;background:#1d180c}.release-preview>b,.release-preview>span{display:block}.release-preview ol{display:flex;flex-wrap:wrap;gap:18px;margin:9px 0 0;padding-left:18px;color:#bfc7c6;font-size:9px}.release-environments{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:12px}.release-environments article{display:flex;flex-direction:column;gap:5px;padding:12px;border:1px solid #3d4a51;background:#0a1117}.release-environments article[data-state="healthy"]{border-color:#2f785e}.release-environments article[data-state="degraded"]{border-color:#84424b}.release-environments code,.release-artifacts code,.release-runs code{color:#dfc36f;overflow-wrap:anywhere}.release-environments span,.release-environments em{color:#879398;font-size:9px;font-style:normal}.release-artifacts article,.release-runs article{display:grid;grid-template-columns:minmax(230px,1fr) minmax(250px,1fr) minmax(260px,1.2fr);align-items:center;gap:12px;padding:11px 0;border-bottom:1px solid #303c43}.release-artifacts article span,.release-runs article>span:first-child{display:flex;min-width:0;flex-direction:column;gap:4px}.release-artifacts small,.release-runs small{letter-spacing:0!important}.release-checks{display:flex;flex-wrap:wrap;gap:4px}.release-checks em{padding:4px 6px;border:1px solid #82434c;background:#291116;color:#ef8994;font-size:8px;font-style:normal}.release-checks em[data-ok="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}
@media(max-width:1300px){.admin-shell{grid-template-columns:190px minmax(0,1fr)}.overview-grid{grid-template-columns:repeat(2,1fr)}.effects-layout,.command-workbench,.release-workbench,.security-workbench{grid-template-columns:1fr}.coverage-strip{grid-template-columns:repeat(3,1fr)}.release-compose,.security-summary,.security-archive{grid-column:auto}.release-form{grid-template-columns:1fr 1fr}.account-actions{grid-template-columns:1fr 1fr}}@media(max-width:850px){.admin-shell{grid-template-columns:1fr}.admin-sidebar{position:static;grid-template-columns:1fr 1fr}.admin-sidebar nav{align-content:start;border-right:1px solid #26323a;border-bottom:0;padding:7px}.admin-sidebar nav:nth-child(even){border-right:0}.overview-grid{grid-template-columns:1fr}.bug-row{grid-template-columns:1fr}.account-row{grid-template-columns:1fr}.coverage-strip,.release-environments,.security-metrics{grid-template-columns:1fr 1fr}.effect-filters{grid-template-columns:1fr 1fr}.effect-table-head,.effect-row{grid-template-columns:minmax(180px,1fr) 110px}.effect-table-head span:last-child,.effect-row>.status-pill{display:none}.approval-row,.command-row,.content-preview>span,.release-form,.release-artifacts article,.release-runs article,.archive-form,.archive-row{grid-template-columns:1fr}.audit-filters input{width:100%}}@media(max-width:560px){.admin-sidebar{grid-template-columns:1fr}.admin-sidebar nav{border-right:0;border-bottom:1px solid #26323a}.coverage-strip,.release-environments,.security-metrics{grid-template-columns:1fr}}
</style>
