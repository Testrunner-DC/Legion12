import { readFileSync } from 'node:fs'
import { analyzeSource } from './check-performance-architecture.mjs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const profile = read('../src/l12/site/ProfilePage.vue')
const platform = read('../src/l12/platform.ts')
const admin = read('../src/l12/site/AdminPage.vue')
const adminAccounts = read('../src/l12/site/AdminAccountsPage.vue')
const adminReleases = read('../src/l12/site/AdminReleasesPage.vue')
const adminWorkbench = read('../src/l12/site/AdminWorkbenchPage.vue')
const router = read('../src/router/index.ts')
const adminNavigation = read('../src/l12/site/adminSections.ts')
const siteContent = read('../src/l12/site/AdminSiteContentPanel.vue')
const alternateArts = read('../src/l12/site/AdminAlternateArtsPanel.vue')
const ruleReview = read('../src/l12/site/AdminRuleRulingsPanel.vue')
const ruleData = read('../src/l12/data/ruleCenterData.ts')
const riskDialog = read('../src/l12/site/AdminRiskActionDialog.vue')
const riskAction = read('../src/l12/site/useAdminRiskAction.ts')
const sectionScroll = read('../src/l12/site/useSectionScroll.ts')
const ruleCenter = read('../src/l12/site/RuleCenterPage.vue')

const checks = [
  [analyzeSource(profile, 'ProfilePage.vue').maximumParallelPageLoad <= 3 && analyzeSource(adminWorkbench, 'AdminWorkbenchPage.vue').maximumParallelPageLoad <= 3, '本批后台与个人页 AST 扇出必须不超过三路'],
  [profile.includes("{ id: 'overview'") && profile.includes("{ id: 'performance'") && profile.includes("{ id: 'collection'") && profile.includes("{ id: 'security'") && profile.includes('route.query.section') && profile.includes("router.push({ path: '/me'"), '个人四分区必须以 URL 为状态来源'],
  [profile.includes('<small class="form-support">') && profile.indexOf('<small class="form-support">') > profile.indexOf('</label>\n          <button class="primary"')
    && profile.includes('grid-template-rows:auto var(--l12-form-control-height,44px)')
    && profile.includes('.account-form label input{box-sizing:border-box;height:var(--l12-form-control-height,44px)')
    && profile.includes('.account-form .primary{height:var(--l12-form-control-height,44px)')
    && profile.includes('@media(max-width:560px){.account-form{grid-template-columns:1fr}'),
  '登录用户名说明必须独占整行，输入框与提交按钮共用固定控件高度，并在窄屏退化为单列，禁止说明文字再次把用户名输入框顶高'],
  [profile.includes("{ id: '7d', label: '近 7 天' }") && profile.includes("{ id: '30d', label: '近 30 天' }")
    && profile.includes("{ id: 'season', label: '本赛季' }") && profile.includes('route.query.range')
    && profile.includes('playerApi.statistics(range)') && profile.includes('statisticsCache.get(range)')
    && platform.includes("statistics: (range: PlayerStatisticsRange = 'season')")
    && platform.includes('/api/me/statistics?range=${encodeURIComponent(range)}'),
  '总体与主宰战绩必须由同一 URL 时间范围和服务端统计响应驱动，禁止前端切片伪筛选'],
  [profile.includes('!platformState.account || platformState.account.mustChangePassword') && profile.includes('pendingSection = pendingSection') && profile.includes('accountGeneration') && profile.includes('loadedResources.has(key)'), '个人资料必须拒绝访客、隔离账号旧响应、串行分区过渡并复用已读资源'],
  [admin.includes('visibleAdminSections(hasPermission)') && admin.includes('v-for="domain in visibleDomains"') && adminWorkbench.includes('visibleAdminSections(hasPermission)') && router.includes("meta: { adminSection: 'accounts' }"), '后台桌面、手机、工作台入口必须同源权限裁剪，并使用独立子路由'],
  [!router.includes('AdminModulePage.vue') && router.includes("import('@/l12/site/AdminAccountsPage.vue')") && router.includes("import('@/l12/site/AdminBugsPage.vue')") && router.includes("import('@/l12/site/AdminEffectsPage.vue')"), '后台不得首屏全域预载，每个业务路由必须延迟加载自己的组件'],
  [sectionScroll.includes("querySelector<HTMLElement>('.site-content')") && sectionScroll.includes('onBeforeRouteUpdate(save)') && sectionScroll.includes('ResizeObserver') && sectionScroll.includes('onBeforeRouteLeave(save)'), '分区回退必须恢复真正站点滚动容器并等待延迟内容'],
  [adminAccounts.includes('<AdminRiskActionDialog') && adminAccounts.includes('useAdminRiskAction()') && adminAccounts.includes('adminApi.revokeSessions(account.id)') && adminReleases.includes('<AdminRiskActionDialog') && riskAction.includes('if (!action || riskBusy.value) return') && riskAction.includes('riskError.value = error instanceof Error') && riskDialog.includes('showModal()') && riskDialog.includes('trapFocus') && riskDialog.includes('trigger.focus()') && riskDialog.includes('@cancel="cancel"'), '后台风险操作必须共用确认框、锁定重复提交并支持焦点/Esc/失败重试'],

  [profile.includes("watch(() => platformState.account?.id, () => { resetProfileAccountData(); void loadAccountData() }")
    && profile.includes('await loadRenameStatus()') && profile.includes('renameStatus.value = null'),
  '改名状态必须随登录账号切换刷新，并在退出时清空'],
  [platform.includes("statistics: (range: PlayerStatisticsRange = 'season')")
    && profile.includes('<h2>战绩</h2>') && profile.includes('<b>主宰战绩</b>')
    && profile.includes('playerStatistics.overall.firstGames') && profile.includes('masterProfileUrl(master.masterId)')
    && !profile.includes('统计摘要独立保留，不依赖录像文件') && !profile.includes('同时列出整体与排位表现'),
  '我的页面必须使用独立统计接口，以排行榜同源样式展示战绩和主宰战绩，不暴露存储实现说明'],
  [router.includes("AdminRuleRulingsPanel.vue") && adminNavigation.includes("label: '规则审核'")
    && !siteContent.includes("section === 'rules'") && !siteContent.includes("id: 'rules'"),
  '规则中心审核必须与站点内容工作台平级'],
  [alternateArts.includes('SingleCardPicker') && alternateArts.includes("openArtPicker('base')")
    && !alternateArts.includes('<label>原卡<select') && alternateArts.includes('卡名（随原卡，不可单独修改）'),
  '原卡与异画选择必须复用筛选单卡组件，异画卡名不可单独编辑'],
  [ruleReview.includes('publishRuleItem') && ruleReview.includes('审核并发布此项')
    && !ruleReview.includes('@click="publish">正式发布'),
  '规则与裁定必须逐项审核发布，不得保留整批正式发布按钮'],
  [ruleReview.includes('withPendingRulingSeeds(parsedRulings)')
    && ruleData.includes("id: 'RULING-20260922-FENIAN-REPEAT'")
    && ruleData.includes("id: 'RULING-20260922-SIWA-KABA'")
    && ruleData.match(/RULING-20260922-(?:FENIAN-REPEAT|SIWA-KABA)[^\n]+status: 'pending'/g)?.length === 2
    && ruleCenter.includes('parsePublishedRulings') && !ruleCenter.includes('createRulingsDraft'),
  '新增芬尼亚与锡瓦卡巴裁定只能合并到后台待审核草稿，玩家页不得读取内置草稿'],
]

const failures = checks.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`玩家资料与后台契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}
console.log(`玩家资料与后台契约检查通过（${checks.length} 项）`)
