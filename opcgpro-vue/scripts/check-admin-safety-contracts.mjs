import { readFileSync, readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const read = path => readFileSync(join(root, path), 'utf8').replace(/\r\n?/g, '\n')
const siteDir = join(root, 'src/l12/site')
const adminPanels = readdirSync(siteDir).filter(name => /^Admin.*\.vue$/.test(name) || name === 'ImmediateMaintenancePanel.vue')
const adminSources = adminPanels.map(name => [name, read(`src/l12/site/${name}`)])
const accounts = read('src/l12/site/AdminAccountsPage.vue')
const articles = read('src/l12/site/AdminArticlesPanel.vue')
const operations = read('src/l12/site/AdminOperationsPanel.vue')
const maintenance = read('src/l12/site/ImmediateMaintenancePanel.vue')
const username = read('src/l12/site/AdminUsernameChangeRequestsPanel.vue')
const releases = read('src/l12/site/AdminReleasesPage.vue')
const security = read('src/l12/site/AdminSecurityPage.vue')
const riskDialog = read('src/l12/site/AdminRiskActionDialog.vue')
const riskAction = read('src/l12/site/useAdminRiskAction.ts')
const platform = read('src/l12/platform.ts')
const store = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.EmailAuth.cs')
const server = read('../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')

const checks = [
  [adminSources.every(([, source]) => !source.includes('window.confirm')), '后台模块不得继续使用浏览器原生 confirm'],
  [riskDialog.includes('dialog.value?.showModal()') && riskDialog.includes('trapFocus') && riskDialog.includes('trigger.focus()') && riskDialog.includes('role="alert"'), '共享风险弹窗必须支持模态焦点、焦点归还和局部错误'],
  [riskAction.includes('if (!action || riskBusy.value) return') && riskAction.includes('await action.run()') && riskAction.includes("riskError.value = error instanceof Error"), '风险动作必须阻止重复提交，并在失败时保留错误'],
  [accounts.includes("hasPermission('admin.accounts.roles.write')") && accounts.includes("hasPermission('admin.sessions.revoke')") && accounts.includes("hasPermission('admin.accounts.status.write')"), '账号角色、会话与状态动作必须按精确权限裁剪'],
  [articles.includes("action === 'publish' || action === 'withdraw' ? 'admin.content.publish' : 'admin.content.draft'") && articles.includes("hasPermission('admin.content.draft')"), '稿件公开动作和草稿动作必须使用各自权限'],
  [operations.includes("hasPermission('admin.operations.write')") && maintenance.includes("hasPermission('admin.operations.write')"), '运营配置与即时维护必须受写权限保护'],
  [username.includes("hasPermission('admin.accounts.status.write')"), '用户名审核动作必须受账号状态写权限保护'],
  [store.includes('RandomNumberGenerator.GetBytes(16)') && store.includes('revokedIds.Length, temporaryPassword') && !store.includes('"123456"'), '后台重置必须生成至少 128 位随机一次性临时密码，禁止固定密码'],
  [server.includes('MapPost("/api/admin/security/audit-recovery-rehearsal"') && !server.includes('MapGet("/api/admin/security/audit-recovery-rehearsal"') && platform.includes("method: 'POST'"), '审计恢复演练必须使用 POST 语义'],
  [releases.includes('releasePreviewKey.value === releaseInputKey.value') && releases.includes('rollbackPreviewKey.value !== rollbackInputKey(run)') && security.includes('archivePreviewKey.value === archiveInputKey.value'), '发布、回滚与审计归档的执行资格必须绑定当前完整预演输入，任一输入变化都要使旧预演失效'],
]

const failures = checks.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  failures.forEach(message => console.error(`FAIL: ${message}`))
  process.exit(1)
}
console.log(`Admin safety contracts passed (${checks.length}).`)
