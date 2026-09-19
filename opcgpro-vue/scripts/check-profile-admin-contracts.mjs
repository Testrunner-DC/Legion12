import { readFileSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const profile = read('../src/l12/site/ProfilePage.vue')
const platform = read('../src/l12/platform.ts')
const admin = read('../src/l12/site/AdminPage.vue')
const siteContent = read('../src/l12/site/AdminSiteContentPanel.vue')
const alternateArts = read('../src/l12/site/AdminAlternateArtsPanel.vue')
const ruleReview = read('../src/l12/site/AdminRuleRulingsPanel.vue')

const checks = [
  [profile.includes("watch(() => platformState.account?.id, () => loadRenameStatus())")
    && profile.includes('await loadRenameStatus()') && profile.includes('renameStatus.value = null'),
  '改名状态必须随登录账号切换刷新，并在退出时清空'],
  [platform.includes("statistics: () => platformRequest<PlayerStatistics>('/api/me/statistics')")
    && profile.includes('各主宰战绩') && profile.includes('playerStatistics.overall.firstGames'),
  '我的页面必须使用独立统计接口展示总体、先后手及各主宰战绩'],
  [admin.includes("tab === 'rules'") && admin.includes('§ 规则中心审核')
    && !siteContent.includes("section === 'rules'") && !siteContent.includes("id: 'rules'"),
  '规则中心审核必须与站点内容工作台平级'],
  [alternateArts.includes('FilteredSingleCardPicker') && alternateArts.includes("openArtPicker('base')")
    && !alternateArts.includes('<label>原卡<select') && alternateArts.includes('卡名（随原卡，不可单独修改）'),
  '原卡与异画选择必须复用筛选单卡组件，异画卡名不可单独编辑'],
  [ruleReview.includes('publishRuleItem') && ruleReview.includes('审核并发布此项')
    && !ruleReview.includes('@click="publish">正式发布'),
  '规则与裁定必须逐项审核发布，不得保留整批正式发布按钮'],
]

const failures = checks.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  console.error(`玩家资料与后台契约检查失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}
console.log(`玩家资料与后台契约检查通过（${checks.length} 项）`)
