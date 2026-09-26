import { readFileSync } from 'node:fs'
import { analyzeSource } from './check-performance-architecture.mjs'
const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const router = read('../src/router/index.ts')
const shell = read('../src/l12/site/AdminPage.vue')
const workbench = read('../src/l12/site/AdminWorkbenchPage.vue')
const account = read('../src/l12/site/AdminAccountDetailPage.vue')
const accounts = read('../src/l12/site/AdminAccountsPage.vue')
const bugs = read('../src/l12/site/AdminBugsPage.vue')
const storagePanel = read('../src/l12/site/AdminServerStoragePanel.vue')
const globalSummary = read('../src/l12/site/AdminGlobalAnalyticsSummary.vue')
const globalPanel = read('../src/l12/site/AdminGlobalDataPanel.vue')
const navigation = read('../src/l12/site/adminSections.ts')
const profile = read('../src/l12/site/ProfilePage.vue')
const platform = read('../src/l12/platform.ts')
const server = read('../../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const store = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.cs')
const storage = read('../../服务端WebSocket/TwelveLegions/L12ServerStorageMonitor.cs')
const highFanoutPanels = ['AdminSiteContentPanel.vue', 'AdminOperationsPanel.vue', 'AdminAlternateArtsPanel.vue']
  .map(name => [name, read(`../src/l12/site/${name}`)])

const checks = [
  [shell.includes('<router-view v-slot="{ Component }">') && !shell.includes("tab === 'accounts'") && router.includes("path: 'users/accounts'") && router.includes("path: 'system/storage'") && !router.includes('AdminModulePage.vue'), '后台壳必须只承载权限导航与独立业务子路由'],
  [['workbench','users','content','matches','operations','system'].every(id => navigation.includes(`id: '${id}'`)), '后台导航必须稳定划分为六个任务域'],
  [account.includes("{ id: 'profile'") && account.includes("{ id: 'access'") && account.includes("{ id: 'sessions'") && account.includes("{ id: 'audit'") && router.includes("users/accounts/:accountId"), '账号详情必须拥有可分享的四分区路由'],
  [workbench.includes('adminApi.workbenchSummary()') && workbench.includes('recentActivities') && server.includes('AdminAudit(limit: 6)') && server.includes('Capture("storage"') && server.includes('unavailable.Count > 0') && workbench.includes('summary?.partial') && server.includes('L12Authorization.HasPermission(account, L12Permission.AdminBugsRead)') && server.includes('L12Authorization.HasPermission(account, L12Permission.AdminSecurityRead)') && analyzeSource(workbench, 'AdminWorkbenchPage.vue').maximumParallelPageLoad <= 1 && highFanoutPanels.every(([name, source]) => analyzeSource(source, name).maximumParallelPageLoad <= 3), '工作台必须按区块容错聚合，并按权限返回有界的待办、异常和最近活动'],
  [workbench.includes('AdminGlobalAnalyticsSummary') && workbench.includes("hasPermission('admin.analytics.read')") && globalSummary.includes('adminApi.globalAnalytics') && globalSummary.includes('loading') && globalSummary.includes('error') && globalSummary.includes('所选日期没有逐日样本') && globalSummary.includes('<h4>活跃趋势</h4>') && globalSummary.includes('metric-switch'), '工作台必须复用完整的全局指标与活跃趋势组件，并独立处理权限、加载、错误与空数据'],
  [profile.includes('sessionApi.revokeOthers()') && platform.includes("'/api/auth/sessions/others'") && server.includes('MapDelete("/api/auth/sessions/others"') && store.includes('RevokeOtherOwnSessions'), '退出其他设备必须由单次原子端点完成'],
  [bugs.includes('fixCommit: item.fixCommit') && bugs.includes('regressionTest: item.regressionTest') && bugs.includes('待裁定') && bugs.includes('待实施 / 实施中') && bugs.includes('证据齐全 · 可关闭') && server.includes('bug_closure_evidence_required') && store.includes('VerifiedAt'), 'Bug 必须显式推进待裁定、待实施、待复测、待部署，并仅在结构化修复、回归、部署与复测证据齐全后标记可关闭'],
  [account.includes('adminApi.account(accountId.value)') && !account.includes('adminApi.accounts()') && server.includes('MapGet("/api/admin/accounts/{accountId}"'), '账号详情必须使用单账号读取端点，禁止加载全账号列表'],
  [storage.includes('Trend.Add(') && storage.includes('Trend.Count > 120') && storage.includes('"current-process"') && storage.includes('IgnoreInaccessible = true') && storage.includes('FileAttributes.ReparsePoint') && storagePanel.includes('本次服务进程趋势') && storagePanel.includes('采样新鲜度') && storagePanel.includes('权威阈值'), '存储状态必须安全降级并明确返回本次进程内的真实有界采样语义'],
  [!storage.includes('record L12StorageVolumeView(string MountPoint') && !storage.includes('record L12StorageCategoryView(string Id, string Label, string Path') && !storage.includes('record L12ServerStorageView(DateTimeOffset ObservedAt, int ProcessId') && !storagePanel.includes('category.path') && !storagePanel.includes('status.processId') && storagePanel.includes('unavailableSourceCount'), '存储正常视图不得返回原始挂载路径、分类路径或进程号，并必须显式展示不可用来源'],
  [globalPanel.includes('AdminGlobalAnalyticsSummary') && !globalPanel.includes('trendMetrics') && !globalPanel.includes('metric-switch') && !globalSummary.includes('每日活跃趋势') && ['dailyActiveUsers','weeklyActiveUsers','monthlyActiveUsers','dailyMatches','weeklyMatches','monthlyMatches','averageOnline','peakOnline','newUsers','returningUsers','pageViews'].every(metric => globalSummary.includes(`key: '${metric}'`)) && globalSummary.includes('data-zero') && globalSummary.includes('data-missing'), '工作台与全局数据页必须复用同一趋势组件，显式切换实际逐日指标，并区分零值与缺失值'],
  [accounts.includes('adminApi.accounts()') && router.includes("AdminAccountsPage.vue") && router.includes("AdminBugsPage.vue") && router.includes("AdminEffectsPage.vue"), '账号列表与领域页面必须由独立路由组件加载'],
]
const failures = checks.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) { failures.forEach(message => console.error(`FAIL: ${message}`)); process.exit(1) }
console.log(`Admin routing and evidence contracts passed (${checks.length}).`)
