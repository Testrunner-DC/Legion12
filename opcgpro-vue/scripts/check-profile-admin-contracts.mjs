import { readFileSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const profile = read('../src/l12/site/ProfilePage.vue')
const platform = read('../src/l12/platform.ts')
const admin = read('../src/l12/site/AdminPage.vue')
const siteContent = read('../src/l12/site/AdminSiteContentPanel.vue')
const alternateArts = read('../src/l12/site/AdminAlternateArtsPanel.vue')
const ruleReview = read('../src/l12/site/AdminRuleRulingsPanel.vue')
const ruleData = read('../src/l12/data/ruleCenterData.ts')
const ruleCenter = read('../src/l12/site/RuleCenterPage.vue')

const checks = [
  [profile.includes("watch(() => platformState.account?.id, () => loadRenameStatus())")
    && profile.includes('await loadRenameStatus()') && profile.includes('renameStatus.value = null'),
  '改名状态必须随登录账号切换刷新，并在退出时清空'],
  [platform.includes("statistics: () => platformRequest<PlayerStatistics>('/api/me/statistics')")
    && profile.includes('<h2>战绩</h2>') && profile.includes('<b>主宰战绩</b>')
    && profile.includes('playerStatistics.overall.firstGames') && profile.includes('masterProfileUrl(master.masterId)')
    && !profile.includes('统计摘要独立保留，不依赖录像文件') && !profile.includes('同时列出整体与排位表现'),
  '我的页面必须使用独立统计接口，以排行榜同源样式展示战绩和主宰战绩，不暴露存储实现说明'],
  [admin.includes("tab === 'rules'") && admin.includes('§ 规则中心审核')
    && !siteContent.includes("section === 'rules'") && !siteContent.includes("id: 'rules'"),
  '规则中心审核必须与站点内容工作台平级'],
  [alternateArts.includes('FilteredSingleCardPicker') && alternateArts.includes("openArtPicker('base')")
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
