<script setup lang="ts">
import PagedCollection from './PagedCollection.vue'
import { computed, defineAsyncComponent, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { adminApi, authState, canAccessAdmin, hasPermission, platformState, refreshCurrentAccount, type AdminAudit, type AdminCommand, type AtomicAbility, type AtomicCardEffect, type AtomicCoverage, type AuditArchiveOperation, type AuditArchiveRecovery, type AuditArchiveSegment, type BugReport, type EffectAtomDescriptor, type EffectPresentationScene, type PlatformAccount, type ReleaseEnvironment, type ReleaseOperation, type ReleaseRun, type SecurityStatus, type VerifiedReleaseArtifact } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import { cardTypeLabel } from '@/l12/cardPresentation'
const AdminSiteContentPanel = defineAsyncComponent(() => import('./AdminSiteContentPanel.vue'))
const AdminRuleRulingsPanel = defineAsyncComponent(() => import('./AdminRuleRulingsPanel.vue'))
const AdminOperationsPanel = defineAsyncComponent(() => import('./AdminOperationsPanel.vue'))
const AdminRankedIntegrityPanel = defineAsyncComponent(() => import('./AdminRankedIntegrityPanel.vue'))
const AdminMatchesPanel = defineAsyncComponent(() => import('./AdminMatchesPanel.vue'))
const AdminCardAnalyticsPanel = defineAsyncComponent(() => import('./AdminCardAnalyticsPanel.vue'))
const AdminGlobalDataPanel = defineAsyncComponent(() => import('./AdminGlobalDataPanel.vue'))
const AdminMatchGovernancePanel = defineAsyncComponent(() => import('./AdminMatchGovernancePanel.vue'))
const AdminUsernameChangeRequestsPanel = defineAsyncComponent(() => import('./AdminUsernameChangeRequestsPanel.vue'))
const AdminAlternateArtsPanel = defineAsyncComponent(() => import('./AdminAlternateArtsPanel.vue'))
const AdminServerStoragePanel = defineAsyncComponent(() => import('./AdminServerStoragePanel.vue'))
const AdminTournamentWorkbench = defineAsyncComponent(() => import('./AdminTournamentWorkbench.vue'))
const AdminEffectWorkbenchPanel = defineAsyncComponent(() => import('./AdminEffectWorkbenchPanel.vue'))

import { adminSections, visibleAdminSections, type AdminTab } from './adminSections'
import { useSectionScroll } from './useSectionScroll'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
const route = useRoute()
const router = useRouter()
const availableAdminTabs = computed(() => visibleAdminSections(hasPermission))
const adminGroups = computed(() => [...new Set(availableAdminTabs.value.map(item => item.group))])
const requestedTab = computed(() => typeof route.query.section === 'string' ? route.query.section : 'overview')
const selectedSection = computed(() => availableAdminTabs.value.find(item => item.id === requestedTab.value))
const tab = computed(() => selectedSection.value?.id)
const loadedTabs = new Set<AdminTab>()
useSectionScroll(() => platformState.account?.id ?? 'guest')
const adminMatchId = ref(typeof route.query.matchId === 'string' ? route.query.matchId : '')
const bugs = ref<BugReport[]>([])
const accounts = ref<PlatformAccount[]>([])
const accountSearch = ref('')
const showDeletedAccounts = ref(false)
const normalizedAccountSearch = computed(() => accountSearch.value.trim().toLocaleLowerCase('zh-CN'))
const accountMatchesSearch = (account: PlatformAccount) => !normalizedAccountSearch.value
  || [account.username, account.id, account.emailMasked, account.disabledReason]
    .some(value => value?.toLocaleLowerCase('zh-CN').includes(normalizedAccountSearch.value))
const activeAccounts = computed(() => accounts.value.filter(account => !account.deleted && accountMatchesSearch(account)))
const deletedAccounts = computed(() => accounts.value.filter(account => account.deleted && accountMatchesSearch(account)))
const deletedAccountCount = computed(() => accounts.value.filter(account => account.deleted).length)
const statusFilter = ref('')
const priorityFilter = ref('')
const bugSearch = ref('')
const bugComments = reactive<Record<string, string>>({})
const reviewNotes = reactive<Record<string, string>>({})
const notice = ref('')
const audits = ref<AdminAudit[]>([])
const auditCategory = ref('')
const auditOutcome = ref('')
const auditActorId = ref('')
const auditCommandId = ref('')
const auditCorrelationId = ref('')
const commands = ref<AdminCommand[]>([])
const selectedCommand = ref<AdminCommand | null>(null)
const commandStatus = ref('')
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
const presentationDrafts = reactive<Record<string, string>>({})
const presentationSaving = ref('')
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
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const temporaryPassword = ref('')
const temporaryPasswordAccount = ref('')

let adminGeneration = 0
let adminReadFailed = false
async function readAdminResource<T>(read: () => Promise<T>, apply: (value: T) => void) {
  const generation = adminGeneration
  const accountId = platformState.account?.id
  const section = tab.value
  if (!authState.verified || !canAccessAdmin.value || !section) return false
  try {
    const value = await read()
    if (generation !== adminGeneration || accountId !== platformState.account?.id || section !== tab.value || !selectedSection.value) return false
    apply(value); return true
  } catch (error) {
    if (generation === adminGeneration && section === tab.value) {
      adminReadFailed = true
      notice.value = error instanceof Error ? error.message : '加载失败，请重试'
    }
    return false
  }
}
async function loadBugs() {
  if (tab.value !== 'bugs') { loadedTabs.delete('bugs'); return }
  await readAdminResource(() => adminApi.bugs({ status: statusFilter.value, priority: priorityFilter.value, search: bugSearch.value }), value => { bugs.value = value })
}
async function loadAccounts() {
  if (tab.value !== 'accounts') { loadedTabs.delete('accounts'); return }
  await readAdminResource(() => adminApi.accounts(), value => { accounts.value = value })
}
async function updateBug(item: BugReport) {
  if (!hasPermission('admin.bugs.write')) return
  try { const updated = await adminApi.updateBug(item.id, { status: item.status, priority: item.priority, assignee: item.assignee, adminNotes: item.adminNotes, comment: bugComments[item.id] }); bugs.value = bugs.value.map(bug => bug.id === updated.id ? updated : bug); bugComments[item.id] = ''; notice.value = `${item.id} 已更新并写入审计记录` } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' }
}
function bugActionLabel(action: string) { return ({ created: '建立反馈', status: '状态变更', priority: '优先级变更', assignee: '负责人变更', notes: '处理摘要变更', comment: '追加处理记录' } as Record<string, string>)[action] || action }
function switchAdminTab(next: AdminTab) {
  if (!availableAdminTabs.value.some(item => item.id === next)) return
  void router.push({ path: '/admin', query: { section: next } })
}
function onMobileAdminTabChange(event: Event) { switchAdminTab((event.target as HTMLSelectElement).value as AdminTab) }
function openAdminMatch(matchId: string) {
  if (!hasPermission('admin.matches.read')) return
  void router.push({ path: '/admin', query: { section: 'matches', matchId } })
}
function setRole(account: PlatformAccount) {
  if (!hasPermission('admin.accounts.roles.write')) return
  const nextRole = account.role as 'player' | 'admin'
  requestRiskAction({
    title: '确认账号身份变更', target: account.username, targetLabel: '账号',
    impact: nextRole === 'admin' ? '该账号将立即获得全部管理员权限。' : '该账号将立即失去管理员权限，现有会话会在权限版本更新后重新校验。',
    confirmLabel: nextRole === 'admin' ? '授予管理员身份' : '改为玩家身份',
    run: async () => { const result = await adminApi.setRole(account.id, nextRole, account.permissionVersion); notice.value = result.changed ? `${account.username} 的角色已更新为${result.role === 'admin' ? '管理员' : '玩家'}并写入审计` : '角色没有变化'; await loadAccounts() },
  })
}
function revokeAccountSessions(account: PlatformAccount) {
  if (!hasPermission('admin.sessions.revoke')) return
  requestRiskAction({
    title: '撤销全部会话', target: account.username, targetLabel: '账号',
    impact: '该账号在所有设备上的登录会话将立即失效，需重新登录。已撤销的会话无法恢复。',
    run: async () => { const result = await adminApi.revokeSessions(account.id); notice.value = `${account.username} 已撤销 ${result.revokedCount} 个会话` },
  })
}
function setAccountStatus(account: PlatformAccount) {
  if (!hasPermission('admin.accounts.status.write')) return
  const reason = accountStatusReasons[account.id]?.trim()
  if (!reason) { notice.value = '请先填写账号状态变更理由'; return }
  const disable = !account.disabled
  requestRiskAction({
    title: disable ? '禁用账号' : '重新启用账号', target: account.username, targetLabel: '账号',
    impact: disable ? '账号将立即禁止登录，全部有效会话会被撤销。' : '账号将恢复登录资格，原有会话不会恢复。',
    confirmLabel: disable ? '确认禁用' : '确认启用', severity: disable ? 'danger' : 'warning',
    run: async () => { const result = await adminApi.setAccountStatus(account.id, disable, reason, account.permissionVersion); notice.value = `${result.account.username} 已${result.account.disabled ? '禁用' : '启用'}；撤销 ${result.revokedSessions} 个会话并写入审计`; accountStatusReasons[account.id] = ''; await loadAccounts(); await loadSecurity() },
  })
}
async function reviewAbility(ability: AtomicAbility, status: string, note = '') { if (!selectedEffect.value || !hasPermission('admin.effects.review')) return; try { await adminApi.reviewEffect(selectedEffect.value.cardId, { abilityId: ability.abilityId, status, note }); selectedEffect.value = await adminApi.effect(selectedEffect.value.cardId); effectCards.value = effectCards.value.map(card => card.cardId === selectedEffect.value?.cardId ? selectedEffect.value : card); notice.value = `${selectedEffect.value.cardId} ABILITY ${ability.sequence} 审查状态已记录` } catch (error) { notice.value = error instanceof Error ? error.message : '审查记录失败' } }
async function loadAudit() {
  if (tab.value !== 'audit') { loadedTabs.delete('audit'); return }
  await readAdminResource(() => adminApi.audit({ category: auditCategory.value, outcome: auditOutcome.value, actorId: auditActorId.value, commandId: auditCommandId.value, correlationId: auditCorrelationId.value }), value => { audits.value = value })
}
async function loadControlPlane() {
  if (tab.value !== 'commands') { loadedTabs.delete('commands'); return }
  await readAdminResource(() => adminApi.commands({ status: commandStatus.value }), value => { commands.value = value })
}
function resetAccountPassword(account: PlatformAccount) {
  if (!hasPermission('admin.accounts.status.write')) return
  const reason = accountStatusReasons[account.id]?.trim()
  if (!reason) { notice.value = '请先填写账号安全操作理由'; return }
  requestRiskAction({
    title: '生成一次性临时密码', target: account.username, targetLabel: '账号',
    impact: '系统会生成唯一的高强度临时密码并只显示一次，同时撤销该账号全部会话。玩家下次登录后必须立即修改密码。',
    confirmLabel: '生成并撤销会话',
    run: async () => {
      const result = await adminApi.resetAccountPassword(account.id, reason, account.permissionVersion)
      if (!result.temporaryPassword) throw new Error('服务端未返回一次性临时密码；请勿重复操作，先刷新命令记录确认结果')
      notice.value = `${result.account.username} 已生成一次性临时密码并撤销 ${result.revokedSessions} 个会话`
      accountStatusReasons[account.id] = ''; await loadAccounts(); await loadSecurity()
      globalThis.setTimeout(() => {
        temporaryPassword.value = result.temporaryPassword || ''
        temporaryPasswordAccount.value = result.account.username
      }, 0)
    },
  })
}
function deleteAccount(account: PlatformAccount) {
  if (!hasPermission('admin.accounts.status.write')) return
  const reason = accountStatusReasons[account.id]?.trim()
  if (!reason) { notice.value = '请先填写账号删除与数据清理理由'; return }
  requestRiskAction({
    title: '删除账号与清理数据', target: account.username, targetLabel: '账号',
    impact: '账号将被逻辑删除，邮箱、牌库、好友等个人数据会被清理，全部会话立即撤销。后台不提供恢复。',
    confirmLabel: '确认删除与清理',
    run: async () => { const result = await adminApi.deleteAccount(account.id, reason, account.permissionVersion); notice.value = `账号已逻辑删除；撤销 ${result.revokedSessions} 个会话，清理 ${result.removedPrivateRecords} 条私有记录与 ${result.cleanedMatchRecords ?? 0} 场对局身份，并保留脱敏审计`; accountStatusReasons[account.id] = ''; await loadAccounts(); await loadSecurity() },
  })
}
async function copyTemporaryPassword() {
  try { await navigator.clipboard.writeText(temporaryPassword.value); notice.value = '一次性临时密码已复制' }
  catch { notice.value = '复制失败，请手动选择临时密码' }
}
function clearTemporaryPassword() { temporaryPassword.value = ''; temporaryPasswordAccount.value = '' }
async function showCommand(id: string) { try { selectedCommand.value = await adminApi.command(id) } catch (error) { notice.value = error instanceof Error ? error.message : '命令详情加载失败' } }
function availableReleaseArtifacts() { return releaseArtifacts.value.filter(item => item.environments.includes(selectedReleaseEnvironment.value)) }
function selectedEnvironment() { return releaseEnvironments.value.find(item => item.environment === selectedReleaseEnvironment.value) }
function ensureReleaseArtifact() {
  if (!availableReleaseArtifacts().some(item => item.id === selectedReleaseArtifact.value)) selectedReleaseArtifact.value = availableReleaseArtifacts()[0]?.id || ''
}
async function loadReleases() {
  if (tab.value !== 'releases') { loadedTabs.delete('releases'); return }
  releaseLoading.value = true
  try {
    if (hasPermission('releases.read')) {
      if (!await readAdminResource(() => adminApi.releaseArtifacts(), value => { releaseArtifacts.value = value })) return
      if (!await readAdminResource(() => adminApi.releaseRuns(), value => { releaseRuns.value = value })) return
    }
    if (hasPermission('releases.runtime.read')) await readAdminResource(() => adminApi.releaseEnvironments(), value => { releaseEnvironments.value = value })
    if (!releaseEnvironments.value.some(item => item.environment === selectedReleaseEnvironment.value)) selectedReleaseEnvironment.value = releaseEnvironments.value[0]?.environment || 'staging'
    ensureReleaseArtifact()
  } finally { releaseLoading.value = false }
}
async function performSubmitRelease(dryRun: boolean) {
  const environment = selectedEnvironment()
  if (!environment || !selectedReleaseArtifact.value) { notice.value = '请选择目标环境和已验证工件'; return }
  if (!releaseReason.value.trim()) { notice.value = '请填写发布理由'; return }
  try {
    const result = await adminApi.deployRelease(selectedReleaseArtifact.value, environment.environment, environment.version, dryRun, releaseReason.value.trim())
    if ('commandId' in result) notice.value = `发布操作已提交（命令 ${result.commandId}）`
    else { releasePreview.value = result; notice.value = result.applied ? '发布已执行并写入审计' : '发布预演完成，未执行激活' }
    await loadControlPlane(); await loadReleases()
  } catch (error) { notice.value = error instanceof Error ? error.message : '发布命令提交失败'; throw error }
}
function submitRelease(dryRun: boolean) {
  if (dryRun) { void performSubmitRelease(true).catch(() => {}); return }
  const environment = selectedEnvironment()
  if (!hasPermission('releases.execute') || !environment || !selectedReleaseArtifact.value || !releaseReason.value.trim()) {
    notice.value = !releaseReason.value.trim() ? '请填写发布理由' : '请选择目标环境和已验证工件'; return
  }
  const artifact = selectedReleaseArtifact.value
  requestRiskAction({
    title: '执行版本发布', target: `${environment.environment} · ${artifact}`, targetLabel: '环境与工件',
    impact: '该操作会激活已验证工件并执行健康与 WebSocket 检查。请确认 dry-run 计划、目标环境和版本理由均已核对。',
    confirmLabel: '确认执行发布',
    run: () => performSubmitRelease(false),
  })
}
async function performRollbackRelease(run: ReleaseRun, dryRun: boolean) {
  const environment = releaseEnvironments.value.find(item => item.environment === run.environment)
  if (!environment) { notice.value = '目标环境运行态不可用，请刷新'; return }
  if (!releaseReason.value.trim()) { notice.value = '请填写回滚理由'; return }
  try {
    const result = await adminApi.rollbackRelease(run.id, environment.version, dryRun, releaseReason.value.trim())
    if ('commandId' in result) notice.value = `回滚操作已提交（命令 ${result.commandId}）`
    else { releasePreview.value = result; notice.value = result.applied ? '回滚已执行并写入审计' : '回滚预演完成，未执行激活' }
    await loadControlPlane(); await loadReleases()
  } catch (error) { notice.value = error instanceof Error ? error.message : '回滚命令提交失败'; throw error }
}
function rollbackRelease(run: ReleaseRun, dryRun: boolean) {
  if (dryRun) { void performRollbackRelease(run, true).catch(() => {}); return }
  if (!hasPermission('releases.execute')) return
  if (!releaseReason.value.trim()) { notice.value = '请填写回滚理由'; return }
  requestRiskAction({
    title: '执行版本回滚', target: `${run.environment} · ${run.artifactId}`, targetLabel: '运行记录',
    impact: '目标环境将切换离开当前版本并执行回滚后的健康与 WebSocket 检查。',
    confirmLabel: '确认执行回滚',
    run: () => performRollbackRelease(run, false),
  })
}
async function loadSecurity() {
  if (tab.value !== 'security') { loadedTabs.delete('security'); return }
  await Promise.all([
    readAdminResource(() => adminApi.securityStatus(), value => { securityStatus.value = value }),
    readAdminResource(() => adminApi.auditArchives(), value => { auditArchives.value = value }),
  ])
}
async function performArchiveAudit(dryRun: boolean) {
  if (!securityStatus.value) { notice.value = '请先刷新安全状态'; return }
  if (!auditArchiveReason.value.trim()) { notice.value = '请填写审计归档理由'; return }
  try {
    const result = await adminApi.archiveAudit(auditRetentionDays.value, securityStatus.value.platformVersion,
      dryRun, auditArchiveReason.value.trim())
    if ('commandId' in result) notice.value = `审计归档操作已提交（命令 ${result.commandId}）`
    else { auditArchivePreview.value = result; notice.value = result.applied
      ? `审计归档已执行：${result.segment?.eventCount ?? result.eligibleEvents} 条事件已归档，源事件保留`
      : `归档预演完成：${result.eligibleEvents} 条可归档事件，未写文件` }
    await loadControlPlane(); await loadSecurity()
  } catch (error) { notice.value = error instanceof Error ? error.message : '审计归档命令失败'; throw error }
}
function archiveAudit(dryRun: boolean) {
  if (dryRun) { void performArchiveAudit(true).catch(() => {}); return }
  if (!hasPermission('admin.audit.archive')) return
  if (!securityStatus.value) { notice.value = '请先刷新安全状态'; return }
  if (!auditArchiveReason.value.trim()) { notice.value = '请填写审计归档理由'; return }
  requestRiskAction({
    title: '执行审计归档', target: `保留 ${auditRetentionDays.value} 天`, targetLabel: '归档范围',
    impact: '系统会生成独立校验归档段。源审计事件仍会保留，但归档写入会被记录为高风险管理操作。',
    confirmLabel: '确认执行归档', severity: 'warning',
    run: () => performArchiveAudit(false),
  })
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
    if (selectedEffect.value) {
      selectedEffect.value = page.items.find(card => card.cardId === selectedEffect.value?.cardId) ?? selectedEffect.value
      hydratePresentationDrafts(selectedEffect.value)
    }
  } catch (error) { notice.value = error instanceof Error ? error.message : '卡效清单加载失败' }
  finally { effectLoading.value = false }
}
async function selectEffect(card: AtomicCardEffect) {
  try { selectedEffect.value = await adminApi.effect(card.cardId); hydratePresentationDrafts(selectedEffect.value) } catch (error) { notice.value = error instanceof Error ? error.message : '卡效详情加载失败' }
}
function hydratePresentationDrafts(card: AtomicCardEffect | null) {
  Object.keys(presentationDrafts).forEach(key => delete presentationDrafts[key])
  card?.abilities.forEach(ability => (ability.presentations ?? []).forEach(scene => {
    presentationDrafts[scene.sceneId] = scene.effectiveText
  }))
}
async function refreshSelectedEffect(cardId: string) {
  const detail = await adminApi.effect(cardId)
  selectedEffect.value = detail
  effectCards.value = effectCards.value.map(card => card.cardId === cardId ? detail : card)
  hydratePresentationDrafts(detail)
}
async function savePresentation(scene: EffectPresentationScene) {
  if (!selectedEffect.value || presentationSaving.value || !hasPermission('admin.effects.review')) return
  const text = presentationDrafts[scene.sceneId] ?? ''
  if (!text.trim() || text.length > 2000) { notice.value = '动效文案须为 1 至 2000 个字符'; return }
  presentationSaving.value = scene.sceneId
  try {
    await adminApi.saveEffectPresentation(selectedEffect.value.cardId, scene.sceneId, text)
    await refreshSelectedEffect(selectedEffect.value.cardId)
    notice.value = `${presentationSceneName(scene)} 的动效文案已保存并写入审计；仅新建对局使用新文案`
  } catch (error) { notice.value = error instanceof Error ? error.message : '动效文案保存失败' }
  finally { presentationSaving.value = '' }
}
function restorePresentation(scene: EffectPresentationScene) {
  const effect = selectedEffect.value
  if (!effect || presentationSaving.value || !hasPermission('admin.effects.review')) return
  requestRiskAction({
    title: '恢复默认动效文案', target: `${effect.cardId} · ${presentationSceneName(scene)}`, targetLabel: '卡牌场景',
    impact: '人工覆盖文案将被移除；之后新建的对局会使用系统默认文案，进行中对局不受影响。',
    confirmLabel: '确认恢复默认', severity: 'warning',
    run: async () => { presentationSaving.value = scene.sceneId; try { await adminApi.restoreEffectPresentation(effect.cardId, scene.sceneId); await refreshSelectedEffect(effect.cardId); notice.value = `${presentationSceneName(scene)} 已恢复默认文案并写入审计；进行中对局不受影响` } finally { presentationSaving.value = '' } },
  })
}
function validPresentationSegment(scene: EffectPresentationScene) {
  return typeof scene.segmentIndex === 'number' && Number.isInteger(scene.segmentIndex)
    && typeof scene.segmentCount === 'number' && Number.isInteger(scene.segmentCount)
    && scene.segmentIndex >= 1 && scene.segmentCount >= scene.segmentIndex
}
function invalidPresentationSegment(scene: EffectPresentationScene) {
  return (scene.segmentIndex != null || scene.segmentCount != null) && !validPresentationSegment(scene)
}
function visiblePresentationScenes(ability: AtomicAbility) {
  const scenes = ability.presentations ?? []
  const hasFlowEffect = scenes.some(scene => scene.eventType === 'effect' && Boolean(scene.flow?.trim()))
  if (!hasFlowEffect) return scenes
  return scenes.filter(scene => scene.eventType !== 'effect' || Boolean(scene.flow?.trim()))
}
function presentationSceneName(scene: EffectPresentationScene) {
  const context = [
    validPresentationSegment(scene) ? `第 ${scene.segmentIndex}/${scene.segmentCount} 段` : '',
    scene.branchLabel?.trim() ? `分支：${scene.branchLabel.trim()}` : '',
    scene.label,
  ]
  return context.filter(Boolean).join(' · ')
}
function formatPresentationChoices(scene: EffectPresentationScene) {
  return Object.entries(scene.requiredChoices ?? {}).map(([key, value]) => `${key}=${value}`).join('、')
}
function formatPlaceholders(scene: EffectPresentationScene) {
  return scene.placeholders.map(name => `{${name}}`).join('、')
}
function statusLabel(status: string) { return ({ 'no-effect': '无卡效', 'legacy-backed': '旧实现兜底', 'partially-atomized': '部分原子化', 'declarative-ready': '声明就绪', 'runtime-migrated': '运行时迁移', verified: '已验证' } as Record<string, string>)[status] || status }
function reviewLabel(status: string) { return ({ confirmed: '人工确认', 'human-assisted': '人工辅助', rejected: '退回修正', unreviewed: '待人工审查' } as Record<string, string>)[status] || status }
function atomDescriptor(kind: string) { return effectAtoms.value.find(atom => atom.kind === kind) }
function previousEffectsPage() { if (effectPage.value > 1) { effectPage.value--; loadEffects() } }
function nextEffectsPage() { if (effectPage.value * 50 < effectTotal.value) { effectPage.value++; loadEffects() } }
let pendingAdminSection = Promise.resolve()
async function loadCurrentAdminSection() {
  const current = tab.value
  const generation = adminGeneration
  pendingAdminSection = pendingAdminSection.catch(() => {}).then(async () => {
    if (generation !== adminGeneration || current !== tab.value || !authState.verified || !canAccessAdmin.value || !current || loadedTabs.has(current)) return
    adminReadFailed = false
    if (current === 'accounts') await loadAccounts()
    else if (current === 'bugs') await loadBugs()
    else if (current === 'releases') await loadReleases()
    else if (current === 'commands') await loadControlPlane()
    else if (current === 'security') await loadSecurity()
    else if (current === 'audit') await loadAudit()
    if (!adminReadFailed && generation === adminGeneration && current === tab.value) loadedTabs.add(current)
    // Child panels own their reads after lazy mounting; never preload them here.
  })
  await pendingAdminSection
}
async function initializeAdminPage() {
  try { await refreshCurrentAccount(); await loadCurrentAdminSection() } catch { /* Fail closed. */ }
}
watch(() => [platformState.account?.id, platformState.account?.permissionVersion], () => {
  adminGeneration++; loadedTabs.clear()
  accounts.value = []; bugs.value = []; audits.value = []; commands.value = []
  releaseArtifacts.value = []; releaseEnvironments.value = []; releaseRuns.value = []
  securityStatus.value = null; auditArchives.value = []; selectedCommand.value = null
  cancelRiskAction(); clearTemporaryPassword(); notice.value = ''
})
watch(() => [route.query.section, route.query.matchId, authState.verified, platformState.account?.id, platformState.account?.permissionVersion], () => {
  adminGeneration++
  adminMatchId.value = typeof route.query.matchId === 'string' ? route.query.matchId : ''
  void loadCurrentAdminSection()
})
onMounted(() => { void initializeAdminPage() })
</script>

<template>
  <div class="admin-page">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <AdminRiskActionDialog v-if="temporaryPassword" title="一次性临时密码已生成" :target="temporaryPasswordAccount" target-label="账号" impact="该密码只显示这一次。请通过受控渠道交给账号本人；关闭后后台无法再次查看。" confirm-label="我已安全保存" severity="warning" :allow-cancel="false" :busy="false" @confirm="clearTemporaryPassword">
      <div class="one-time-secret"><code>{{ temporaryPassword }}</code><button type="button" @click="copyTemporaryPassword">复制临时密码</button></div>
    </AdminRiskActionDialog>
    <header><div><small>ADMINISTRATION</small><h1>管理后台</h1><p>账号权限、Bug 闭环、官网内容与运营配置。</p></div><router-link to="/me">← 返回我的</router-link></header>
    <section v-if="!authState.initialized || authState.refreshing" class="denied"><b>正在验证管理员权限</b><span>管理数据只会在服务端身份确认后加载。</span></section>
    <section v-else-if="!canAccessAdmin" class="denied"><b>需要管理员权限</b><span>请先在“我的”页面登录管理员账号。</span></section>
    <template v-else>
      <div class="admin-shell">
      <label class="admin-mobile-navigation"><span>后台模块</span><select :value="tab" aria-label="选择后台模块" @change="onMobileAdminTabChange"><option v-for="item in availableAdminTabs" :key="item.id" :value="item.id">{{ item.label }}</option></select></label>
      <aside class="admin-sidebar">
        <nav v-for="group in adminGroups" :key="group"><small>{{ group }}</small><button v-for="item in availableAdminTabs.filter(item => item.group === group)" :key="item.id" :class="{ active: tab === item.id }" :aria-current="tab === item.id ? 'page' : undefined" @click="switchAdminTab(item.id)">{{ item.icon }} {{ item.label }}</button></nav>
      </aside>
      <main class="admin-content">
      <section v-if="!selectedSection" class="denied" role="alert"><b>{{ adminSections.some(item => item.id === requestedTab) ? '无权访问此模块' : '此模块不存在' }}</b><span>请选择可访问的后台模块。</span></section>
      <header v-else class="section-heading"><span>管理后台 / {{ selectedSection.group }}</span><h2>{{ selectedSection.label }}</h2></header>
      <section v-if="tab === 'overview'" class="overview-grid">
        <header class="panel"><div><small>CONTROL CENTER</small><h2>运营总览</h2><p>这里只显示摘要和入口；配置编辑只在对应模块内进行。</p></div></header>
        <button v-for="item in availableAdminTabs.filter(item => item.id !== 'overview')" :key="item.id" class="overview-card" @click="switchAdminTab(item.id)"><small>{{ item.group }}</small><b>{{ item.icon }}</b><span>{{ item.label }}</span></button>
      </section>
      <section v-else-if="tab === 'bugs'" class="panel">
        <header><h2>Bug 反馈</h2><div class="bug-filters"><input v-model="bugSearch" placeholder="编号 / 标题 / 玩家 / 房间 / 对局" @keyup.enter="loadBugs"><select v-model="statusFilter" @change="loadBugs"><option value="">全部状态</option><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="priorityFilter" @change="loadBugs"><option value="">全部优先级</option><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><button @click="loadBugs">查询</button></div></header>
        <PagedCollection :items="bugs" v-slot="{ items: paged25637 }"><article v-for="item in paged25637" :key="item.id" class="bug-row"><div class="bug-summary"><code>{{ item.id }}</code><b>{{ item.title }}</b><span>{{ item.reporterName }} · {{ new Date(item.createdAt).toLocaleString() }}</span><span>客户端 {{ item.clientVersion || item.version || 'unknown-client' }} · 服务端 {{ item.serverVersion || 'legacy-unknown' }} · 引擎 {{ item.engineVersion || 'legacy-unknown' }}</span><p>{{ item.description }}</p><small>{{ item.page }}<template v-if="item.roomCode"> · 房间 {{ item.roomCode }}</template><template v-if="item.matchId"> · <button class="match-link" @click="openAdminMatch(item.matchId)">在对局档案查看 {{ item.matchId }}</button></template></small><details v-if="item.clientDiagnostic || item.connectionDiagnostic || item.diagnostic" class="bug-diagnostics"><summary>自动诊断</summary><dl v-if="item.clientDiagnostic"><dt>HTTP / API</dt><dd>{{ item.clientDiagnostic.httpStatus }} {{ item.clientDiagnostic.httpStatusCode || '' }} / {{ item.clientDiagnostic.apiStatus }} {{ item.clientDiagnostic.apiStatusCode || '' }}</dd><dt>WebSocket</dt><dd>{{ item.clientDiagnostic.webSocketReadyState }} · close {{ item.clientDiagnostic.closeCode || '-' }} {{ item.clientDiagnostic.closeReason || '' }}</dd><dt>恢复</dt><dd>{{ item.clientDiagnostic.recoveryPhase }} · generation {{ item.clientDiagnostic.connectionGeneration || '-' }} · retry {{ item.clientDiagnostic.retryCount }}</dd><dt>心跳</dt><dd>sent {{ item.clientDiagnostic.lastHeartbeatAt || '-' }} · pong {{ item.clientDiagnostic.lastPongAt || '-' }}</dd><dt>认证 / 维护</dt><dd>{{ item.clientDiagnostic.authenticationState }} / {{ item.clientDiagnostic.maintenanceState }}</dd></dl><dl v-if="item.connectionDiagnostic"><dt>服务端认领</dt><dd>{{ item.connectionDiagnostic.decision }} · {{ item.connectionDiagnostic.previousConnectionGeneration ?? '-' }} → {{ item.connectionDiagnostic.connectionGeneration }} · revision {{ item.connectionDiagnostic.recoveryRevision ?? '-' }}</dd><dt v-if="item.connectionDiagnostic.rejectionReason">拒绝原因</dt><dd v-if="item.connectionDiagnostic.rejectionReason">{{ item.connectionDiagnostic.rejectionReason }}</dd></dl><dl v-if="item.diagnostic"><dt>权威对局</dt><dd>{{ item.diagnostic.phase || '-' }} · round {{ item.diagnostic.round ?? '-' }} · revision {{ item.diagnostic.revision ?? '-' }} · prompts {{ item.diagnostic.prompts.length }}</dd></dl></details><details class="bug-history"><summary>处理记录（{{ item.history.length }}）</summary><ol><PagedCollection :items="item.history" :page-size="5" v-slot="{ items: nestedPage }"><li v-for="audit in nestedPage" :key="audit.id"><b>{{ bugActionLabel(audit.action) }}</b><span>{{ audit.actorName }} · {{ new Date(audit.createdAt).toLocaleString() }}</span><p v-if="audit.comment">{{ audit.comment }}</p><code v-else-if="audit.fromValue !== audit.toValue">{{ audit.fromValue || '无' }} → {{ audit.toValue || '无' }}</code></li></PagedCollection></ol></details></div><div v-if="hasPermission('admin.bugs.write')" class="bug-admin"><select v-model="item.status"><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="item.priority"><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><input v-model="item.assignee" placeholder="负责人"/><textarea v-model="item.adminNotes" rows="3" placeholder="当前处理摘要"/><textarea v-model="bugComments[item.id]" rows="3" placeholder="追加处理记录（保存后进入时间线）"/><button @click="updateBug(item)">保存并记录</button></div><div v-else class="read-only-note">当前账号只有 Bug 读取权限。</div></article></PagedCollection>
        <div v-if="!bugs.length" class="empty">暂无符合筛选条件的反馈</div>
      </section>
      <AdminMatchesPanel v-else-if="tab === 'matches' && hasPermission('admin.matches.read')" :initial-match-id="adminMatchId" @notice="notice = $event"/>
      <AdminMatchGovernancePanel v-else-if="tab === 'match-governance' && hasPermission('admin.match-governance.read')" @notice="notice = $event"/>
      <AdminGlobalDataPanel v-else-if="tab === 'global-data' && hasPermission('admin.analytics.read')" @notice="notice = $event"/>
      <AdminCardAnalyticsPanel v-else-if="tab === 'card-analytics' && hasPermission('admin.analytics.read')" @notice="notice = $event" @open-match="openAdminMatch"/>
      <AdminUsernameChangeRequestsPanel v-else-if="tab === 'username-requests' && hasPermission('admin.accounts.read')" @notice="notice = $event"/>
      <AdminAlternateArtsPanel v-else-if="tab === 'alternate-arts' && hasPermission('admin.content.read')" @notice="notice = $event"/>
      <AdminRuleRulingsPanel v-else-if="tab === 'rules' && hasPermission('admin.content.read')" @notice="notice = $event"/>
      <AdminServerStoragePanel v-else-if="tab === 'storage' && hasPermission('admin.security.read')" @notice="notice = $event"/>
      <AdminTournamentWorkbench v-else-if="tab === 'tournaments' && (hasPermission('tournaments.manage') || hasPermission('tournaments.rulings.write'))" admin-mode embedded/>
      <section v-else-if="tab === 'accounts'" class="panel account-panel">
        <header>
          <div><h2>账号、权限与会话</h2><p>账号变更立即执行并完整审计；状态、密码重置与逻辑删除均撤销相关会话，根 Admin 与操作者自身受保护。</p></div>
          <div class="account-toolbar">
            <input v-model="accountSearch" type="search" placeholder="搜索用户名 / ID / 邮箱" aria-label="搜索账号"/>
            <button class="deleted-accounts-trigger" @click="showDeletedAccounts = true">已删除 {{ deletedAccountCount }}</button>
            <button @click="loadAccounts(); loadSecurity()">刷新</button>
          </div>
        </header>
        <div class="account-row head"><b>用户名 / 状态</b><span>建立时间</span><span>长期身份</span><span>有效权限</span><span>操作</span></div>
        <PagedCollection :items="activeAccounts" v-slot="{ items: paged30632 }"><div v-for="account in paged30632" :key="account.id" class="account-row">
          <b>{{ account.username }}<small :data-disabled="account.disabled">{{ account.disabled ? '已禁用' : '正常' }}<template v-if="account.mustChangeUsername"> · 待修改用户名</template><template v-if="account.mustChangePassword"> · 必须修改密码</template><template v-if="account.emailVerified"> · 邮箱 {{ account.emailMasked }}</template><template v-if="account.disabledReason"> · {{ account.disabledReason }}</template></small></b>
          <span class="account-created" data-label="建立时间">{{ new Date(account.createdAt).toLocaleString() }}</span>
          <label class="account-role"><span>长期身份</span><select v-model="account.role" :disabled="account.username === 'Admin' || !hasPermission('admin.accounts.roles.write')"><option value="player">玩家</option><option value="admin">管理员</option></select></label>
          <small class="account-permissions" data-label="有效权限" :title="account.permissions?.join('\n')">{{ account.permissions?.length ?? 0 }} 项</small>
          <span class="account-actions"><input v-if="hasPermission('admin.accounts.status.write')" v-model="accountStatusReasons[account.id]" placeholder="状态 / 重置 / 删除理由"/><button v-if="hasPermission('admin.accounts.roles.write')" :disabled="account.username === 'Admin'" @click="setRole(account)">保存身份</button><button v-if="hasPermission('admin.accounts.status.write')" class="status" :data-disabled="account.disabled" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="setAccountStatus(account)">{{ account.disabled ? '启用账号' : '禁用账号' }}</button><button v-if="hasPermission('admin.sessions.revoke')" class="revoke" @click="revokeAccountSessions(account)">撤销会话</button><button v-if="hasPermission('admin.accounts.status.write')" class="reset" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="resetAccountPassword(account)">重置密码</button><button v-if="hasPermission('admin.accounts.status.write')" class="delete" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="deleteAccount(account)">删除与清理</button></span>
        </div></PagedCollection>
        <div v-if="!activeAccounts.length" class="empty">没有符合条件的有效账号</div>
        <Teleport to="body">
          <div v-if="showDeletedAccounts" class="deleted-accounts-overlay" @click.self="showDeletedAccounts = false">
            <section class="deleted-accounts-dialog" role="dialog" aria-modal="true" aria-label="已删除账号">
              <header><div><small>DELETED ACCOUNTS</small><h2>已删除账号</h2><p>这里只提供原有字段与权限范围内的只读查看，不提供恢复或再次删除操作。</p></div><button aria-label="关闭已删除账号" @click="showDeletedAccounts = false">×</button></header>
              <div class="deleted-account-list">
                <PagedCollection :items="deletedAccounts" v-slot="{ items: paged33222 }"><article v-for="account in paged33222" :key="account.id">
                  <span><b>{{ account.username }}</b><code>{{ account.id }}</code></span>
                  <span><small>删除时间</small><b>{{ account.deletedAt ? new Date(account.deletedAt).toLocaleString() : '未记录' }}</b></span>
                  <span><small>建立时间</small><b>{{ new Date(account.createdAt).toLocaleString() }}</b></span>
                  <span><small>原身份 / 权限</small><b>{{ account.role === 'admin' ? '管理员' : '玩家' }} · {{ account.permissions?.length ?? 0 }} 项</b></span>
                  <span><small>记录</small><b>{{ account.disabledReason || account.emailMasked || '已完成隐私清理' }}</b></span>
                </article></PagedCollection>
                <div v-if="!deletedAccounts.length" class="empty">没有符合当前搜索条件的已删除账号</div>
              </div>
            </section>
          </div>
        </Teleport>
      </section>
      <AdminSiteContentPanel v-else-if="tab === 'content'" @notice="notice = $event"/>
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
                <span class="effect-identity"><CardImage :card-id="card.cardId" :legacy-url="card.imageUrl" :alt="card.name" intent="thumb" fit="cover" object-position="center 30%"/><span><code>{{ card.cardId }}</code><b>{{ card.name }}</b><small>{{ card.faction }} · {{ cardTypeLabel(card.cardType, card.isCounterTactic) }}</small></span></span>
                <span class="effect-count"><b>{{ card.abilities.length }}</b> 能力 / <b>{{ card.atomCount }}</b> 原子<small v-if="card.legacyAtomCount">{{ card.legacyAtomCount }} 个兜底节点</small><em class="review-pill" :data-review="card.reviewStatus">{{ reviewLabel(card.reviewStatus) }}</em></span>
                <span class="status-pill" :data-status="card.migrationStatus">{{ statusLabel(card.migrationStatus) }}</span>
              </button>
              <div v-if="effectLoading" class="empty">正在读取原子清单…</div>
            </div>
            <div class="effect-pagination"><button :disabled="effectPage <= 1" @click="previousEffectsPage">上一页</button><span>第 {{ effectPage }} 页 · 共 {{ effectTotal }} 张</span><button :disabled="effectPage * 50 >= effectTotal" @click="nextEffectsPage">下一页</button></div>
          </section>
          <section class="panel effect-detail">
            <template v-if="selectedEffect">
              <header><div><small>{{ selectedEffect.cardId }} · {{ selectedEffect.product }}</small><h2>{{ selectedEffect.name }}</h2><p>{{ selectedEffect.faction }} · {{ cardTypeLabel(selectedEffect.cardType, selectedEffect.isCounterTactic) }}</p></div><span class="effect-header-status"><em class="review-pill" :data-review="selectedEffect.reviewStatus">{{ reviewLabel(selectedEffect.reviewStatus) }}</em><span class="status-pill" :data-status="selectedEffect.migrationStatus">{{ statusLabel(selectedEffect.migrationStatus) }}</span></span></header>
              <AdminEffectWorkbenchPanel :effect="selectedEffect" @notice="notice = $event"/>
              <div class="original-text"><b>卡面原文</b><p class="l12-effect-body">{{ selectedEffect.effectText || '无效果文本' }}</p></div>
              <article v-for="ability in selectedEffect.abilities" :key="ability.abilityId" class="ability-card">
                <header><span><small>ABILITY {{ ability.sequence }}</small><b>{{ ability.trigger }}</b><em class="execution-model">{{ ability.executionModel }}</em></span><span class="effect-header-status"><em class="review-pill" :data-review="ability.reviewStatus">{{ reviewLabel(ability.reviewStatus) }}</em><span class="status-pill" :data-status="ability.migrationStatus">{{ statusLabel(ability.migrationStatus) }}</span></span></header>
                <div v-if="ability.costText" class="effect-segments">
                  <p><small>COST · 冒号前</small><span class="l12-effect-body">{{ ability.costText }}</span></p>
                  <p><small>效果 · 冒号后</small><span class="l12-effect-body">{{ ability.resolutionText }}</span></p>
                </div>
                <p v-else class="l12-effect-body">{{ ability.resolutionText || ability.text }}</p>
                <section v-if="visiblePresentationScenes(ability).length" class="presentation-editor" data-ui-contract="effect-presentation-editor">
                  <header><div><b>对战卡牌动效文案</b><small>独立于规则参数与卡面原文；保存后只影响新建对局，换行会原样展示。</small></div></header>
                  <article v-for="scene in visiblePresentationScenes(ability)" :key="scene.sceneId" class="presentation-scene">
                    <div class="presentation-heading">
                      <span class="presentation-identity">
                        <span class="presentation-title">
                          <strong v-if="validPresentationSegment(scene)" class="presentation-segment" data-ui-contract="effect-presentation-segment">第 {{ scene.segmentIndex }}/{{ scene.segmentCount }} 段</strong>
                          <b>{{ scene.label }}</b>
                          <mark v-if="scene.branchLabel" class="presentation-branch" data-ui-contract="effect-presentation-branch">分支：{{ scene.branchLabel }}</mark>
                          <mark v-if="invalidPresentationSegment(scene)" class="presentation-metadata-warning">分段元数据异常</mark>
                        </span>
                        <code>{{ scene.eventType }} · {{ scene.trigger }}<template v-if="scene.flow"> · flow {{ scene.flow }}</template></code>
                        <small v-if="formatPresentationChoices(scene)" class="presentation-choices">公开选择条件：{{ formatPresentationChoices(scene) }}</small>
                      </span>
                      <em :data-overridden="scene.overridden">{{ scene.overridden ? '人工覆盖生效' : '沿用默认推导' }}</em>
                    </div>
                    <dl><dt>默认文本</dt><dd>{{ scene.defaultText }}</dd><dt>当前生效</dt><dd>{{ scene.effectiveText }}</dd></dl>
                    <label>编辑文案<textarea v-model="presentationDrafts[scene.sceneId]" rows="5" maxlength="2000" data-ui-contract="effect-presentation-multiline" :aria-label="`${selectedEffect.name} ${presentationSceneName(scene)} 动效文案`" @keydown.enter.stop @keyup.enter.stop></textarea><small>按 Enter 换行；公告与对局动效会保留手动换行。</small></label>
                    <small v-if="scene.placeholders.length" class="presentation-placeholders">允许的公开占位符：{{ formatPlaceholders(scene) }}</small>
                    <div class="presentation-preview"><small>预览</small><strong>{{ presentationDrafts[scene.sceneId] }}</strong></div>
                    <footer><button :disabled="!hasPermission('admin.effects.review') || presentationSaving !== ''" @click="savePresentation(scene)">{{ presentationSaving === scene.sceneId ? '保存中…' : '保存文案' }}</button><button class="restore" :disabled="!scene.overridden || !hasPermission('admin.effects.review') || presentationSaving !== ''" @click="restorePresentation(scene)">恢复默认</button></footer>
                  </article>
                </section>
                <div class="atom-flow">
                  <template v-for="(atom, index) in ability.atoms" :key="atom.atomId">
                    <article class="atom-node" :data-category="atomDescriptor(atom.kind)?.category" :class="{ legacy: atom.kind === 'legacy.resolve' }" :title="atomDescriptor(atom.kind)?.description">
                      <small>{{ atom.stage }} · {{ atomDescriptor(atom.kind)?.category || '原子' }}</small><b>{{ atom.label }}</b><code>{{ atom.kind }}</code>
                      <dl v-if="Object.keys(atom.parameters).length"><template v-for="(value, key) in atom.parameters" :key="key"><dt>{{ key }}</dt><dd>{{ value }}</dd></template></dl>
                    </article><span v-if="index < ability.atoms.length - 1" class="flow-arrow">→</span>
                  </template>
                </div>
                <div v-if="hasPermission('admin.effects.review')" class="ability-review"><textarea v-model="reviewNotes[ability.abilityId]" rows="2" placeholder="人工核对备注（规则书、FAQ、测试证据）"/><button @click="reviewAbility(ability, 'human-assisted', reviewNotes[ability.abilityId])">标记人工辅助</button><button class="confirm" @click="reviewAbility(ability, 'confirmed', reviewNotes[ability.abilityId])">确认拆分</button><button class="reject" @click="reviewAbility(ability, 'rejected', reviewNotes[ability.abilityId])">退回修正</button></div>
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
            <label>变更理由<input v-model="releaseReason" placeholder="写入操作记录与审计"/></label>
            <span class="release-actions"><button :disabled="!hasPermission('releases.execute') || !selectedReleaseArtifact" @click="submitRelease(true)">dry-run</button><button class="confirm" :disabled="!hasPermission('releases.execute') || !selectedReleaseArtifact" @click="submitRelease(false)">执行发布</button></span>
          </div>
          <div v-if="releasePreview" class="release-preview"><b>干运行计划（未激活）</b><span>{{ releasePreview.plan.action }} · {{ releasePreview.plan.environment }} · v{{ releasePreview.plan.environmentVersion }}</span><ol><li v-for="step in releasePreview.plan.steps" :key="step">{{ step }}</li></ol></div>
        </section>
        <section class="panel release-runtime">
          <header><div><h2>运行态只读快照</h2><p>状态、健康检查与 WebSocket 冒烟来自显式适配器观测。</p></div></header>
          <div class="release-environments"><article v-for="environment in releaseEnvironments" :key="environment.environment" :data-state="environment.state"><small>{{ environment.environment }} · v{{ environment.version }}</small><b>{{ environment.state }}</b><code>{{ environment.activeArtifactId || '未激活' }}</code><span>HTTP {{ environment.health.success ? '✓' : '×' }} {{ environment.health.code }} · {{ environment.health.durationMs }}ms</span><span>WS {{ environment.webSocket.success ? '✓' : '×' }} {{ environment.webSocket.code }} · {{ environment.webSocket.durationMs }}ms</span><em>{{ environment.adapterConfigured ? '适配器已配置' : '适配器未配置' }}</em></article></div>
        </section>
        <section v-if="hasPermission('releases.read')" class="panel release-artifacts">
          <header><div><h2>已验证工件</h2><p>清单与 hash 验证证据由受信适配器注入，Web 端没有注册入口。</p></div></header>
          <PagedCollection :items="releaseArtifacts" v-slot="{ items: paged46764 }"><article v-for="artifact in paged46764" :key="artifact.id"><span><code>{{ artifact.id }}</code><b>{{ artifact.commit }}</b><small>{{ new Date(artifact.verifiedAt).toLocaleString() }}</small></span><span><b>{{ artifact.verificationGates.join(' · ') }}</b><small>{{ artifact.environments.join(' / ') }}</small></span><code>sha256 {{ artifact.releaseSha256 }}</code></article></PagedCollection><div v-if="!releaseArtifacts.length" class="empty">适配器未提供可发布工件</div>
        </section>
        <section v-if="hasPermission('releases.read')" class="panel release-runs">
          <header><div><h2>发布、失败与回滚记录</h2><p>每次激活、健康/WS 冒烟和回滚结果均持久化。</p></div></header>
          <PagedCollection :items="releaseRuns" v-slot="{ items: paged47502 }"><article v-for="run in paged47502" :key="run.id"><span><code>{{ run.id }}</code><b>{{ run.action }} · {{ run.environment }} · {{ run.status }}</b><small>{{ run.artifactId }} · v{{ run.environmentVersion }} · {{ new Date(run.completedAt).toLocaleString() }}</small></span><span class="release-checks"><em v-for="check in run.checks" :key="check.kind" :data-ok="check.success">{{ check.kind }} {{ check.success ? '✓' : '×' }} · {{ check.code }}</em></span><span v-if="run.status === 'succeeded' && hasPermission('releases.execute')" class="release-actions"><button @click="rollbackRelease(run, true)">回滚 dry-run</button><button class="reject" @click="rollbackRelease(run, false)">执行回滚</button></span></article></PagedCollection><div v-if="!releaseRuns.length" class="empty">尚无发布运行记录</div>
        </section>
      </section>
      <section v-else-if="tab === 'commands'" class="command-workbench">
        <section class="panel command-panel"><header><div><h2>命令记录</h2><p>新操作直接执行并写入审计；历史待处理请求不会自动执行，需要使用新请求重新提交。</p></div><div><select v-model="commandStatus" @change="loadControlPlane"><option value="">全部状态</option><option value="requested">历史待处理</option><option value="executed">已执行</option><option value="failed">失败</option><option value="rejected">已拒绝</option></select><button @click="loadControlPlane">刷新</button></div></header><PagedCollection :items="commands" v-slot="{ items: paged48905 }"><article v-for="command in paged48905" :key="command.id" class="command-row" @click="showCommand(command.id)"><code>{{ command.id }}</code><b>{{ command.type }}</b><span>{{ command.actorName }} · {{ command.status }} · v{{ command.resourceVersion }}</span><small v-if="command.failureReason">失败：{{ command.failureReason }}</small></article></PagedCollection><div v-if="!commands.length" class="empty">暂无命令记录</div></section>
        <section v-if="selectedCommand" class="panel command-detail"><header><h2>命令详情</h2><button @click="selectedCommand = null">关闭</button></header><dl><dt>类型 / 状态</dt><dd>{{ selectedCommand.type }} · {{ selectedCommand.status }}</dd><dt>权限 / 作用域</dt><dd>{{ selectedCommand.permission }} · {{ selectedCommand.scope }}</dd><dt>关联 ID</dt><dd><code>{{ selectedCommand.correlationId }}</code></dd><dt>结果</dt><dd>{{ selectedCommand.resultMessage || selectedCommand.failureReason || '待处理' }}</dd></dl><pre>{{ JSON.stringify({ payload: selectedCommand.payload, result: selectedCommand.result }, null, 2) }}</pre></section>
      </section>
      <section v-else-if="tab === 'security'" class="security-workbench">
        <section class="panel security-summary"><header><div><h2>安全治理快照</h2><p>平台只有管理员与玩家两种持久身份；这里监控管理操作、账号安全与独立审计状态。</p></div><button @click="loadSecurity">刷新</button></header><div v-if="securityStatus" class="security-metrics"><article><small>活跃管理员</small><b>{{ securityStatus.activeApprovers }}</b><span>具备管理权限的账号</span></article><article><small>高风险审计</small><b>{{ securityStatus.highRiskAuditAvailable ? '可用' : '失败关闭' }}</b><span>保留 {{ securityStatus.auditRetentionDays }} 天</span></article><article><small>账号 / 锁定</small><b>{{ securityStatus.disabledAccounts }} / {{ securityStatus.activeLoginLocks }}</b><span>禁用账号 / 登录锁定</span></article><article><small>平台版本</small><b>v{{ securityStatus.platformVersion }}</b><span>所有管理操作直接执行并留痕</span></article></div><div v-if="securityStatus?.alerts.length" class="security-alerts"><PagedCollection :items="securityStatus.alerts" :page-size="5" v-slot="{ items: nestedPage }"><article v-for="alert in nestedPage" :key="alert.code" :data-severity="alert.severity"><b>{{ alert.code }} · {{ alert.count }}</b><span>{{ alert.message }}</span></article></PagedCollection></div></section>
        <section class="panel security-boundaries"><header><h2>发布恢复与账号保护</h2></header><article><b>服务器离线恢复</b><span>{{ securityStatus?.offlineBootstrapUsed ? '一次性恢复已使用' : securityStatus?.offlineBootstrapEnabled && securityStatus?.offlineBootstrapCredentialConfigured ? '离线恢复入口已显式启用' : '默认关闭或凭据未配置' }}</span><p>仅服务器 CLI 可用，只服务受控恢复；后台操作不要求另一名管理员批准。</p></article><article><b>MFA</b><span>{{ securityStatus?.mfa.enrollmentEnabled ? '已启用' : '未启用' }}</span></article></section>
        <section class="panel security-archive"><header><div><h2>独立审计归档与恢复演练</h2><p>归档为内部派生路径的校验 JSONL；主审计事件不删除，客户端不能指定文件路径。</p></div><button @click="rehearseAuditRecovery">恢复演练</button></header><div v-if="hasPermission('admin.audit.archive')" class="archive-form"><label>保留天数<input v-model.number="auditRetentionDays" type="number" min="30" max="3650"/></label><label>归档理由<input v-model="auditArchiveReason" maxlength="500"/></label><button @click="archiveAudit(true)">dry-run（不写）</button><button class="confirm" @click="archiveAudit(false)">执行归档</button></div><div v-if="auditArchivePreview" class="archive-preview">可归档 {{ auditArchivePreview.eligibleEvents }} 条 · 截止 {{ new Date(auditArchivePreview.archiveBefore).toLocaleString() }} · 源事件保留</div><div v-if="auditRecovery" class="archive-recovery" :data-ok="auditRecovery.success">恢复演练 {{ auditRecovery.success ? '通过' : '失败' }} · {{ auditRecovery.segments }} 段 / {{ auditRecovery.events }} 条<small v-if="auditRecovery.error">{{ auditRecovery.error }}</small></div><PagedCollection :items="auditArchives" v-slot="{ items: paged52622 }"><article v-for="segment in paged52622" :key="segment.id" class="archive-row"><code>{{ segment.id }}</code><span>{{ new Date(segment.from).toLocaleString() }} → {{ new Date(segment.until).toLocaleString() }}</span><b>{{ segment.eventCount }} 条</b><small>sha256 {{ segment.sha256 }}</small></article></PagedCollection><div v-if="!auditArchives.length" class="empty">尚无审计归档段</div></section>
      </section>
      <section v-else-if="tab === 'audit'" class="panel audit-panel"><header><h2>管理与安全审计</h2><div class="audit-filters"><select v-model="auditCategory"><option value="">全部类型</option><option value="account">账号权限</option><option value="session">会话安全</option><option value="security">权限拒绝</option><option value="command">管理命令</option><option value="approval">审批</option><option value="content">内容发布</option><option value="effect">卡效确认</option><option value="effect-presentation">卡牌动效文案</option><option value="release">发布编排</option></select><select v-model="auditOutcome"><option value="">全部结果</option><option value="succeeded">成功</option><option value="pending">待审批</option><option value="executed">已执行</option><option value="failed">失败</option><option value="denied">拒绝</option></select><input v-model="auditActorId" placeholder="操作者 ID"/><input v-model="auditCommandId" placeholder="命令 ID"/><input v-model="auditCorrelationId" placeholder="关联 ID"/><button @click="loadAudit">查询</button></div></header><div class="audit-head"><span>时间 / 操作者</span><span>动作 / 结果</span><span>对象</span><span>变更 / 证据</span></div><PagedCollection :items="audits" v-slot="{ items: paged54206 }"><article v-for="audit in paged54206" :key="audit.id" class="audit-row"><span>{{ new Date(audit.createdAt).toLocaleString() }}<small>{{ audit.actorName }}</small></span><b>{{ audit.category }} · {{ audit.action }}<small>{{ audit.outcome || 'success' }}<template v-if="audit.permission"> · {{ audit.permission }}</template></small></b><code>{{ audit.target }}</code><span>{{ audit.fromValue || '无' }} → {{ audit.toValue || '无' }}<small v-if="audit.comment">{{ audit.comment }}</small><small v-if="audit.reason">原因：{{ audit.reason }}</small><code v-if="audit.correlationId">关联 ID：{{ audit.correlationId }}</code><code v-if="audit.commandId">命令：{{ audit.commandId }}</code></span></article></PagedCollection><div v-if="!audits.length" class="empty">暂无管理操作记录</div></section>
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
.section-heading{margin-bottom:16px}.section-heading span{color:#9aa7ae;font-size:13px}.section-heading h2{margin:6px 0;font-size:22px}
.admin-page{min-height:100%;padding:30px clamp(18px,3vw,46px) 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.admin-page>header{display:flex;align-items:flex-start;justify-content:space-between}.admin-page small{color:#d5b85e;font:900 14px monospace;letter-spacing:.16em}.admin-page h1{margin:5px 0;font-size:30px}.admin-page p{color:#7d898e;font-size:14px;line-height:1.7}.admin-page>header a{color:#e1c36e;text-decoration:none;font-size:14px;font-weight:900}.admin-shell{display:grid;grid-template-columns:220px minmax(0,1fr);gap:16px;margin-top:22px}.admin-sidebar{align-self:start;position:sticky;top:16px;display:grid;gap:5px;padding:12px;border:1px solid #35424a;background:#0b1218}.admin-sidebar nav{display:grid;gap:4px;padding:9px 0;border-bottom:1px solid #26323a}.admin-sidebar nav:last-child{border-bottom:0}.admin-sidebar nav small{padding:0 8px 5px;color:#68757b}.admin-sidebar button,.admin-sidebar a{box-sizing:border-box;width:100%;padding:10px;border:1px solid transparent;background:transparent;color:#9da8ad;text-align:left;text-decoration:none;font:900 14px 'Microsoft YaHei'}.admin-sidebar button:hover,.admin-sidebar a:hover,.admin-sidebar button.active{border-color:#6d5d31;background:#211b0e;color:#f0d579}.admin-content{min-width:0}.overview-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.overview-grid>header{grid-column:1/-1}.overview-card{display:flex;min-height:130px;flex-direction:column;align-items:flex-start;justify-content:flex-end;gap:7px;padding:17px;border:1px solid #35424a;background:#101821;color:#fff;text-align:left;text-decoration:none}.overview-card:hover{border-color:#c1a44e;background:#171b1d}.overview-card b{font-size:22px}.overview-card span{color:#87949a;font-size:14px}.panel,.denied{border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel button,.panel select,.panel input,.panel textarea{border:1px solid #4c5961;background:#080e13;color:#fff;font:700 14px 'Microsoft YaHei';padding:9px}.bug-row{display:grid;grid-template-columns:1fr 280px;gap:18px;padding:18px 0;border-bottom:1px solid #303c43}.bug-summary code{color:#dfc36f}.bug-summary b,.bug-summary span,.bug-summary small{display:block}.bug-summary b{margin:7px 0;font-size:16px}.bug-summary span,.bug-summary small{color:#718087;font-size:14px}.bug-summary p{color:#c7ccca;white-space:pre-wrap;overflow-wrap:anywhere}.bug-admin{display:grid;grid-template-columns:1fr 1fr;gap:7px}.bug-admin input,.bug-admin textarea,.bug-admin button{grid-column:1/-1}.account-row{display:grid;grid-template-columns:1fr 1.5fr 180px 90px minmax(320px,1fr);align-items:center;gap:10px;padding:12px;border-bottom:1px solid #303c43}.account-row.head{color:#7e8a90;font-size:14px}.content-editor label{display:block;margin-top:16px;color:#b8c0c1;font-size:14px;font-weight:900}.content-editor input,.content-editor textarea{box-sizing:border-box;width:100%;margin-top:7px}.denied{display:flex;flex-direction:column;gap:7px;margin-top:22px}.denied span,.empty{color:#7e8a90}.notice{position:sticky;z-index:20;bottom:12px;margin-top:12px;padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
.bug-filters{display:flex;flex-wrap:wrap;gap:7px}.bug-filters input{min-width:240px}.bug-summary a{color:#83d5e4}.bug-summary .match-link{margin-left:4px;padding:0;border:0;background:transparent;color:#83d5e4;font:inherit;text-decoration:underline;cursor:pointer}.bug-history{margin-top:14px;border-top:1px solid #2e3b42;padding-top:10px}.bug-history summary{cursor:pointer;color:#d6bd70;font-size:14px;font-weight:900}.bug-history ol{max-height:220px;overflow:auto;padding-left:20px}.bug-history li{margin:8px 0}.bug-history li b,.bug-history li span{display:inline;margin-right:7px}.bug-history li p{margin:3px 0}.bug-history li code{display:block;color:#a8b3b5}
.bug-diagnostics{margin-top:12px;padding:10px;border:1px solid #34444d;background:#091116}.bug-diagnostics summary{cursor:pointer;color:#83d5e4;font-size:14px;font-weight:900}.bug-diagnostics dl{display:grid;grid-template-columns:110px minmax(0,1fr);gap:5px 10px;margin:9px 0 0;font-size:14px}.bug-diagnostics dt{color:#718087}.bug-diagnostics dd{margin:0;color:#b8c4c6;overflow-wrap:anywhere}
.coverage-strip{display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px}.coverage-strip article{display:flex;flex-direction:column;gap:4px;padding:15px;border:1px solid #39474e;background:#0c141a}.coverage-strip b{font-size:24px}.coverage-strip span{color:#75838a;font-size:14px}.coverage-strip .coverage-warning{border-color:#7b4936;background:#21140f}.coverage-strip .coverage-ready{border-color:#2c6754;background:#0c1c17}.effects-workbench{min-width:0}.effects-layout{display:grid;grid-template-columns:minmax(480px,.9fr) minmax(540px,1.1fr);gap:12px;min-width:0}.effects-list,.effect-detail{min-width:0}.effects-list{display:flex;max-height:calc(100vh - 215px);min-height:560px;flex-direction:column;overflow:hidden}.effects-list>header{flex:none;gap:12px;min-width:0}.effects-list>header>div{min-width:0}.effects-list>header button{flex:none}.effects-list>header p{margin:3px 0;overflow-wrap:anywhere}.effect-filters{display:flex;flex:none;flex-wrap:wrap;gap:7px;margin:14px 0}.effect-filters input{flex:1 1 220px}.effect-filters select{flex:1 1 112px}.effect-filters button{flex:0 0 auto}.effect-filters input,.effect-filters select,.effect-filters button{box-sizing:border-box;min-width:0}.effect-scroll{min-height:0;flex:1;overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain;padding-right:5px;scrollbar-gutter:stable}.effect-scroll:focus-visible{outline:1px solid #d8b95f;outline-offset:2px}.effect-table-head,.effect-row{display:grid;grid-template-columns:minmax(220px,1fr) minmax(125px,145px) minmax(92px,110px);align-items:center;gap:10px}.effect-table-head{padding:7px 10px;color:#6f7d84;font-size:14px;font-weight:900}.effect-row{box-sizing:border-box;width:100%;margin-top:4px;padding:8px 10px!important;text-align:left}.effect-row.selected{border-color:#d8b95f;background:#241e11}.effect-identity{display:flex;align-items:center;min-width:0;gap:9px}.effect-identity .l12-card-image{width:38px;height:52px;border:1px solid #56636a}.effect-identity>span{display:flex;min-width:0;flex-direction:column}.effect-identity code{color:#d8bd6a;font-size:14px}.effect-identity b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.effect-identity small{color:#708087!important;letter-spacing:0!important}.effect-count{display:flex;min-width:0;flex-wrap:wrap;gap:3px;color:#9aa4a7;font-size:14px}.effect-count small{width:100%;color:#c17d60!important;letter-spacing:0!important}.status-pill,.review-pill{justify-self:start;padding:5px 7px;border:1px solid #516068;background:#151e24;color:#b9c2c4;font-size:14px;font-style:normal;font-weight:900;white-space:nowrap}.status-pill[data-status="partially-atomized"]{border-color:#9a742a;background:#2b210d;color:#f0cf73}.status-pill[data-status="legacy-backed"]{border-color:#84424b;background:#291116;color:#ef8994}.status-pill[data-status="declarative-ready"],.status-pill[data-status="verified"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.review-pill[data-review="human-assisted"]{border-color:#72539a;background:#20152c;color:#d9baff}.review-pill[data-review="confirmed"]{border-color:#2f7b89;background:#0a2027;color:#7bd8e7}.review-pill[data-review="unreviewed"]{color:#7d8b91}.effect-header-status{display:flex;flex:none;align-items:center;justify-content:flex-end;gap:6px}.effect-pagination{display:flex;flex:none;align-items:center;justify-content:center;gap:12px;margin-top:14px}.effect-pagination span{color:#78858a;font-size:14px}.effect-detail>header{gap:12px}.effect-detail>header>div{min-width:0}.effect-detail>header>div small{display:block}.original-text{margin:14px 0;padding:14px;border-left:3px solid #d7b85d;background:#0a1116}.original-text p{margin:7px 0 0;color:#cdd2d0}.ability-card{margin-top:12px;padding:13px;border:1px solid #35434a;background:#0a1117}.ability-card>header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.ability-card>header>span:first-child{display:flex;min-width:0;flex-direction:column;gap:3px}.ability-card>header small{letter-spacing:.12em}.ability-card>p{color:#c5ccca;overflow-wrap:anywhere}.effect-segments{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin:10px 0}.effect-segments p{margin:0;padding:9px;border:1px solid #39474e;background:#0d151a}.effect-segments p:first-child{border-color:#85652d;background:#241d0f}.effect-segments small,.effect-segments span{display:block}.effect-segments small{margin-bottom:4px;color:#9b8a58}.effect-segments span{color:#c5ccca}.atom-flow{display:flex;align-items:stretch;gap:5px;overflow-x:auto;padding:8px 1px 13px}.atom-node{flex:0 0 150px;padding:10px;border:1px solid #3e5965;background:#0d1b22}.atom-node[data-category="费用"]{border-color:#85652d;background:#241d0f}.atom-node[data-category="选择"]{border-color:#5f4385;background:#1c1328}.atom-node[data-category="结算"],.atom-node[data-category="数值"]{border-color:#296b69;background:#0b2423}.atom-node.legacy{border-color:#934452;background:#2b1017}.atom-node>small,.atom-node>b,.atom-node>code{display:block}.atom-node>b{margin:5px 0}.atom-node>code{color:#83949b;font-size:14px}.atom-node dl{display:grid;grid-template-columns:auto 1fr;gap:3px;margin:8px 0 0;font-size:14px}.atom-node dt{color:#6e7d82}.atom-node dd{overflow:hidden;margin:0;color:#c6ccca;text-overflow:ellipsis;white-space:nowrap}.flow-arrow{align-self:center;color:#c8a94e;font-size:18px}.ability-card details,.raw-definition{margin-top:10px;border-top:1px solid #2e3a40;padding-top:9px}.ability-card summary,.raw-definition summary{cursor:pointer;color:#d6bd70;font-size:14px;font-weight:900}.ability-card ol{padding-left:20px}.ability-card li{margin:7px 0;color:#c8cecc;font-size:14px}.ability-card li span{display:block;color:#718087}.legacy-note{padding:8px;border-left:2px solid #a04755;background:#251016;color:#e19aa4!important}.raw-definition pre{max-height:360px;overflow:auto;padding:12px;background:#05090c;color:#aeb9b9;font-size:14px;white-space:pre-wrap}.detail-empty{display:grid;min-height:400px;place-items:center;text-align:center}
.coverage-strip{grid-template-columns:repeat(6,1fr)}.coverage-strip .coverage-verified{border-color:#2f7b89;background:#0a2027}
.content-actions{display:flex;gap:7px}.content-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.content-editor label>em{float:right;padding:3px 6px;border:1px solid #3e5c4f;color:#7fd3ae;font-size:14px;font-style:normal}.content-editor label>em[data-status="draft"]{border-color:#876328;color:#efca70}.ability-review{display:grid;grid-template-columns:1fr auto auto auto;gap:6px;margin-top:10px}.ability-review textarea{min-width:0;resize:vertical}.ability-review .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.ability-review .reject{border-color:#84424b;background:#291116;color:#ef8994}.review-pill[data-review="rejected"]{border-color:#84424b;background:#291116;color:#ef8994}.audit-head,.audit-row{display:grid;grid-template-columns:1.25fr .8fr 1fr 1.5fr;gap:12px;padding:10px}.audit-head{color:#77858b;font-size:14px;font-weight:900}.audit-row{align-items:start;border-top:1px solid #303c43;color:#c8cecc;font-size:14px}.audit-row span,.audit-row small{display:block}.audit-row small{margin-top:4px;color:#77858b}.audit-row code{color:#dfc36f;overflow-wrap:anywhere}.account-row{grid-template-columns:1fr 1.3fr 160px 80px minmax(470px,auto)}.account-row>b small{display:block;margin-top:4px;color:#74c99f!important;letter-spacing:0!important}.account-row>b small[data-disabled="true"]{color:#ef8994!important}.account-actions{display:grid;grid-template-columns:minmax(150px,1fr) repeat(5,auto);gap:6px}.account-actions input{min-width:0}.account-actions .status[data-disabled="false"],.account-actions .revoke,.account-actions .delete{border-color:#7e3c45;background:#2b1116;color:#eab5bb}.account-actions .status[data-disabled="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.account-actions .reset{border-color:#8a6b32;background:#20190d;color:#e6cb7b}.account-actions button:disabled{opacity:.4}
.presentation-editor{display:grid;gap:9px;margin:12px 0;padding:12px;border:1px solid #49606a;background:#0d1820}.presentation-editor>header small{display:block;margin-top:4px;color:#8d9ba0!important;letter-spacing:0!important}.presentation-scene{display:grid;gap:9px;padding:11px;border:1px solid #344851;background:#091218}.presentation-heading{display:flex;align-items:flex-start;justify-content:space-between;gap:10px}.presentation-heading>.presentation-identity{display:grid;min-width:0;gap:5px}.presentation-title{display:flex;align-items:center;flex-wrap:wrap;gap:6px}.presentation-segment,.presentation-branch,.presentation-metadata-warning{padding:3px 6px;border:1px solid #4b626b;background:#111e25;color:#9ddce2;font-size:14px;line-height:1.3}.presentation-branch{border-color:#746134;background:#241e10;color:#eed27c}.presentation-metadata-warning{border-color:#89424b;background:#2a1116;color:#f09aa3}.presentation-heading code{color:#8ca0a7;font-size:14px;overflow-wrap:anywhere}.presentation-heading>em{flex:none;padding:4px 6px;border:1px solid #53636a;color:#9fabad;font-size:14px;font-style:normal}.presentation-heading>em[data-overridden="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.presentation-choices{color:#8fb7bd!important;letter-spacing:0!important;overflow-wrap:anywhere}.presentation-scene dl{display:grid;grid-template-columns:76px minmax(0,1fr);gap:5px 9px;margin:0;font-size:14px}.presentation-scene dt{color:#7f8e94}.presentation-scene dd{margin:0;color:#c8d0cf;white-space:pre-wrap;overflow-wrap:anywhere}.presentation-scene label{display:grid;gap:5px;color:#b8c0c1;font-size:14px;font-weight:900}.presentation-scene textarea{box-sizing:border-box;width:100%;resize:vertical;white-space:pre-wrap}.presentation-placeholders{color:#d4ba6b!important;letter-spacing:0!important}.presentation-preview{display:grid;gap:5px;padding:9px;border-left:2px solid #d1b25c;background:#1b170c}.presentation-preview small{color:#a89252!important;letter-spacing:0!important}.presentation-preview strong{min-height:20px;color:#fff2c7;font-size:14px;white-space:pre-wrap;overflow-wrap:anywhere}.presentation-scene footer{display:flex;justify-content:flex-end;gap:7px}.presentation-scene footer .restore{border-color:#7b6330;background:#231c0c;color:#e6ca73}.presentation-scene button:disabled{opacity:.45}
.news-editor{margin-top:18px;padding:14px;border:1px solid #526269;background:#0b1218}.news-editor>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:10px;border-bottom:1px solid #344149}.news-editor h3{margin:0}.news-editor>article{margin-top:12px;padding:12px;border:1px solid #35434a;background:#101a21}.news-editor-row{display:grid;grid-template-columns:minmax(220px,1fr) 130px 190px;gap:8px}.news-editor footer{display:flex;align-items:center;gap:14px;margin-top:10px}.news-editor footer label{display:flex;align-items:center;gap:5px;margin:0}.news-editor footer input{width:auto;margin:0}.news-editor .delete{margin-left:auto;border-color:#84424b;background:#291116;color:#ef8994}
.security-workbench{display:grid;grid-template-columns:1.1fr .9fr;gap:12px}.security-summary,.security-archive{grid-column:1/-1}.security-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:14px}.security-metrics article,.security-boundaries article{display:flex;flex-direction:column;gap:5px;padding:13px;border:1px solid #35434a;background:#0a1117}.security-metrics b{font-size:18px}.security-metrics span,.security-boundaries span,.security-boundaries p{color:#859197;font-size:14px}.security-alerts{display:grid;gap:6px;margin-top:10px}.security-alerts article{display:flex;justify-content:space-between;gap:12px;padding:9px;border:1px solid #7a5c2f;background:#21190d;color:#e7ca79;font-size:14px}.security-alerts article[data-severity="critical"]{border-color:#84424b;background:#291116;color:#ef8994}.security-boundaries{display:grid;gap:8px}.security-boundaries>header{grid-column:1/-1}.security-boundaries article p{margin:0}.archive-form{display:grid;grid-template-columns:140px minmax(240px,1fr) auto auto;align-items:end;gap:7px;margin:14px 0}.archive-form label{display:flex;flex-direction:column;gap:5px;color:#829096;font-size:14px}.archive-form input{box-sizing:border-box;width:100%}.archive-form .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.archive-preview,.archive-recovery{margin:8px 0;padding:9px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584;font-size:14px}.archive-recovery[data-ok="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.archive-recovery small{display:block;margin-top:4px;letter-spacing:0!important}.archive-row{display:grid;grid-template-columns:minmax(210px,1fr) 1.2fr auto minmax(260px,1.2fr);gap:9px;padding:10px;border-top:1px solid #303c43;font-size:14px}.archive-row code,.archive-row small{overflow-wrap:anywhere;color:#dfc36f;letter-spacing:0!important}
.content-preview{display:grid;gap:5px;margin-top:14px;padding:12px;border:1px solid #6b5a2d;background:#1d180c}.content-preview>span{display:grid;grid-template-columns:minmax(180px,1fr) 70px 2fr;gap:8px;font-size:14px}.content-preview em{color:#e5c96f;font-style:normal}.content-preview small{overflow:hidden;color:#929d9f!important;letter-spacing:0!important;text-overflow:ellipsis;white-space:nowrap}.content-history{margin-top:22px;border-top:1px solid #36434a;padding-top:14px}.content-history article{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:9px;border-bottom:1px solid #303c43}.content-history article span{display:flex;min-width:0;flex-direction:column}.content-history code{color:#dfc36f}.content-history small{letter-spacing:0!important}.command-workbench{display:grid;grid-template-columns:1fr 1fr;gap:12px}.command-row{display:grid;grid-template-columns:minmax(210px,1fr) 1fr 1fr;gap:9px;padding:10px;border-bottom:1px solid #303c43;cursor:pointer}.command-row:hover{background:#172129}.command-row code{color:#dfc36f}.command-row small{grid-column:1/-1;color:#ef8994!important;letter-spacing:0!important}.command-detail dl{display:grid;grid-template-columns:120px 1fr;gap:7px;font-size:14px}.command-detail dt{color:#78858a}.command-detail dd{margin:0}.command-detail pre{max-height:420px;overflow:auto;padding:12px;background:#05090c;color:#aeb9b9;font-size:14px;white-space:pre-wrap}.audit-filters{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:6px}.audit-filters input{width:150px}
.release-workbench{display:grid;grid-template-columns:1fr 1fr;gap:12px}.release-compose{grid-column:1/-1}.release-form{display:grid;grid-template-columns:180px minmax(260px,1fr) minmax(260px,1fr) auto;align-items:end;gap:8px;margin-top:14px}.release-form label{display:flex;min-width:0;flex-direction:column;gap:5px;color:#829096;font-size:14px;font-weight:900}.release-form select,.release-form input{box-sizing:border-box;width:100%}.release-actions{display:flex;gap:6px}.release-actions .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.release-actions .reject{border-color:#84424b;background:#291116;color:#ef8994}.release-preview{margin-top:12px;padding:12px;border:1px solid #6b5a2d;background:#1d180c}.release-preview>b,.release-preview>span{display:block}.release-preview ol{display:flex;flex-wrap:wrap;gap:18px;margin:9px 0 0;padding-left:18px;color:#bfc7c6;font-size:14px}.release-environments{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:12px}.release-environments article{display:flex;flex-direction:column;gap:5px;padding:12px;border:1px solid #3d4a51;background:#0a1117}.release-environments article[data-state="healthy"]{border-color:#2f785e}.release-environments article[data-state="degraded"]{border-color:#84424b}.release-environments code,.release-artifacts code,.release-runs code{color:#dfc36f;overflow-wrap:anywhere}.release-environments span,.release-environments em{color:#879398;font-size:14px;font-style:normal}.release-artifacts article,.release-runs article{display:grid;grid-template-columns:minmax(230px,1fr) minmax(250px,1fr) minmax(260px,1.2fr);align-items:center;gap:12px;padding:11px 0;border-bottom:1px solid #303c43}.release-artifacts article span,.release-runs article>span:first-child{display:flex;min-width:0;flex-direction:column;gap:4px}.release-artifacts small,.release-runs small{letter-spacing:0!important}.release-checks{display:flex;flex-wrap:wrap;gap:4px}.release-checks em{padding:4px 6px;border:1px solid #82434c;background:#291116;color:#ef8994;font-size:14px;font-style:normal}.release-checks em[data-ok="true"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}
@media(max-width:1300px){.admin-shell{grid-template-columns:190px minmax(0,1fr)}.overview-grid{grid-template-columns:repeat(2,1fr)}.effects-layout,.command-workbench,.release-workbench,.security-workbench{grid-template-columns:1fr}.coverage-strip{grid-template-columns:repeat(3,1fr)}.release-compose,.security-summary,.security-archive{grid-column:auto}.release-form{grid-template-columns:1fr 1fr}.account-actions{grid-template-columns:1fr 1fr}}@media(max-width:850px){.admin-shell{grid-template-columns:1fr}.admin-sidebar{position:static;grid-template-columns:1fr 1fr}.admin-sidebar nav{align-content:start;border-right:1px solid #26323a;border-bottom:0;padding:7px}.admin-sidebar nav:nth-child(even){border-right:0}.overview-grid{grid-template-columns:1fr}.bug-row{grid-template-columns:1fr}.account-row{grid-template-columns:1fr}.coverage-strip,.release-environments,.security-metrics{grid-template-columns:1fr 1fr}.effect-filters{grid-template-columns:1fr 1fr}.effect-table-head,.effect-row{grid-template-columns:minmax(180px,1fr) 110px}.effect-table-head span:last-child,.effect-row>.status-pill{display:none}.approval-row,.command-row,.content-preview>span,.release-form,.release-artifacts article,.release-runs article,.archive-form,.archive-row{grid-template-columns:1fr}.audit-filters input{width:100%}}@media(max-width:560px){.admin-sidebar{grid-template-columns:1fr}.admin-sidebar nav{border-right:0;border-bottom:1px solid #26323a}.coverage-strip,.release-environments,.security-metrics,.effect-segments{grid-template-columns:1fr}}
.account-panel>header{gap:18px}.account-toolbar{display:flex;align-items:center;justify-content:flex-end;gap:7px}.account-toolbar input{box-sizing:border-box;width:min(300px,32vw)}.deleted-accounts-trigger{border-color:#796330!important;background:#241d0c!important;color:#e4c96f!important}.deleted-accounts-overlay{position:fixed;z-index:4100;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,6,.82);backdrop-filter:blur(6px)}.deleted-accounts-dialog{box-sizing:border-box;width:min(1040px,calc(100vw - 32px));max-height:calc(100vh - 40px);overflow:hidden;border:1px solid #65737a;background:#0b1218;box-shadow:0 28px 80px #000}.deleted-accounts-dialog>header{display:flex;align-items:flex-start;justify-content:space-between;gap:18px;padding:18px;border-bottom:1px solid #35424a}.deleted-accounts-dialog h2{margin:4px 0}.deleted-accounts-dialog p{margin:0}.deleted-accounts-dialog>header>button{flex:0 0 34px;width:34px;height:34px;padding:0;border:1px solid #65737a;background:#111a20;color:#fff;font-size:20px}.deleted-account-list{max-height:calc(100vh - 180px);overflow:auto;padding:8px 18px 18px}.deleted-account-list article{display:grid;grid-template-columns:minmax(190px,1.2fr) repeat(4,minmax(140px,1fr));align-items:center;gap:12px;padding:13px 0;border-bottom:1px solid #2e3a41}.deleted-account-list article>span{display:flex;min-width:0;flex-direction:column;gap:4px}.deleted-account-list code{overflow:hidden;color:#d8bd6a;text-overflow:ellipsis;white-space:nowrap}.deleted-account-list small{color:#77858b!important;letter-spacing:0!important}.deleted-account-list b{overflow-wrap:anywhere;font-size:14px}
.account-role{display:contents}.account-role>span{display:none}
.one-time-secret{display:grid;gap:10px;margin:14px 0;padding:12px;border:1px solid #9d7c36;background:#080e13}.one-time-secret code{user-select:all;color:#ffe09a;font-size:18px;letter-spacing:.08em;overflow-wrap:anywhere}.one-time-secret button{justify-self:start}.read-only-note{align-self:start;padding:10px;border:1px solid #35424a;color:#8d9ba0;font-size:14px}
@media(max-width:850px){.account-panel>header,.account-toolbar{align-items:stretch;flex-direction:column}.account-toolbar input{width:100%}.deleted-account-list article{grid-template-columns:1fr 1fr}.command-workbench{grid-template-columns:1fr}}@media(max-width:560px){.deleted-account-list article{grid-template-columns:1fr}}
.admin-mobile-navigation{display:none}
@media(max-width:850px){
  .admin-page{overflow-x:clip;padding:22px clamp(12px,3.5vw,24px) 56px}
  .admin-page>header{gap:10px}
  .admin-page>header>div{min-width:0}
  .admin-shell{gap:10px;margin-top:14px}
  .admin-mobile-navigation{position:sticky;z-index:30;top:max(8px,env(safe-area-inset-top));display:grid;grid-column:1/-1;grid-template-columns:auto minmax(0,1fr);align-items:center;gap:10px;box-sizing:border-box;width:100%;padding:9px 10px;border:1px solid #56636a;background:rgba(11,18,24,.96);box-shadow:0 10px 28px rgba(0,0,0,.36);backdrop-filter:blur(10px)}
  .admin-mobile-navigation>span{color:#d9bd69;font-size:12px;font-weight:900;white-space:nowrap}
  .admin-mobile-navigation select{box-sizing:border-box;min-width:0;width:100%;padding:9px 34px 9px 10px;border:1px solid #596870;background:#080e13;color:#fff;font:800 13px 'Microsoft YaHei','微软雅黑',sans-serif}
  .admin-sidebar{display:none}
  .admin-content{overflow:visible}
  .panel,.denied{padding:14px}
  .panel>header{align-items:stretch;flex-direction:column;gap:9px}
  .panel>header>button,.panel>header>select{align-self:flex-start}
  .effect-filters{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr)}
  .effect-filters input{grid-column:1/-1}
  .effect-filters input,.effect-filters select,.effect-filters button{width:100%}
  .ability-review{grid-template-columns:1fr 1fr}
  .ability-review textarea{grid-column:1/-1}
  .news-editor-row{grid-template-columns:1fr}
  .news-editor footer{align-items:stretch;flex-wrap:wrap}
  .news-editor .delete{margin-left:0}
  .presentation-heading{align-items:stretch;flex-direction:column}
  .presentation-heading>em{align-self:flex-start}
  .release-actions{flex-wrap:wrap}
}
@media(max-width:560px){
  .admin-page{padding-inline:max(10px,env(safe-area-inset-left));padding-right:max(10px,env(safe-area-inset-right))}
  .admin-page>header{align-items:stretch;flex-direction:column}
  .admin-page>header a{align-self:flex-start}
  .admin-page h1{font-size:24px}
  .overview-card{min-height:104px;padding:14px}
  .coverage-strip,.release-environments,.security-metrics,.effect-filters,.ability-review{grid-template-columns:1fr}
  .effect-filters input{grid-column:auto}
  .account-actions{grid-template-columns:1fr}
  .account-actions input,.account-actions button{box-sizing:border-box;width:100%}
  .account-row.head{display:none}
  .account-row:not(.head){gap:9px;margin-top:9px;border:1px solid #303c43;background:#0a1117}
  .account-created,.account-permissions{display:grid;grid-template-columns:78px minmax(0,1fr);align-items:center;gap:8px}
  .account-created::before,.account-permissions::before{content:attr(data-label);color:#718087;font-size:12px;font-weight:900}
  .account-role{display:grid;grid-template-columns:78px minmax(0,1fr);align-items:center;gap:8px}
  .account-role>span{display:block;color:#718087;font-size:12px;font-weight:900}
  .account-role select{box-sizing:border-box;width:100%}
  .audit-filters{display:grid;grid-template-columns:1fr;width:100%}
  .audit-filters select,.audit-filters input,.audit-filters button{box-sizing:border-box;width:100%}
  .presentation-scene dl{grid-template-columns:1fr}
  .presentation-scene footer{display:grid;grid-template-columns:1fr 1fr}
  .presentation-scene footer button{width:100%}
  .news-editor>header{align-items:stretch;flex-direction:column;gap:8px}
  .news-editor footer{display:grid;grid-template-columns:1fr}
  .release-actions{display:grid;grid-template-columns:1fr}
  .release-actions button{width:100%}
}
</style>
