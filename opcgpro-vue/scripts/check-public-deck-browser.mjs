import fs from 'node:fs'
import path from 'node:path'

const root = process.cwd()
const read = value => fs.readFileSync(path.join(root, value), 'utf8')
const library = read('src/l12/site/DeckLibraryPage.vue')
const detail = read('src/l12/site/PublicDeckDetailPage.vue')
const browser = read('src/l12/site/DeckConstructionBrowser.vue')
const router = read('src/router/index.ts')
const platform = read('src/l12/platform.ts')
const server = read('../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const detailsStore = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.PublicDeckDetails.cs')
const content = read('src/l12/site/PublicDeckContentEditor.vue')
const catalogDetails = read('src/l12/CatalogCardDetails.vue')

const checks = [
  ['公开牌库详情具备独立路由', router.includes("path: '/decks/:deckId'") && router.includes('PublicDeckDetailPage.vue')],
  ['公开牌库详情仅通过短码直读', platform.includes('get: (id: string)') && server.includes('_platform.PublishedDeckByPublicCode(id')],
  ['组合筛选完整', ['masterFilter', 'factionFilter', 'legalFilter', 'cardFilter', 'updatedFilter'].every(key => library.includes(key))],
  ['筛选状态写入URL', ['tab', 'q', 'master', 'faction', 'legal', 'card', 'updated', 'sort'].every(key => library.includes(`'${key}'`))],
  ['浏览器前进后退恢复筛选', library.includes('watch(() => route.query, restoreFiltersFromRoute')],
  ['列表返回位置可恢复', library.includes('l12:deck-library:scroll:') && library.includes('listScrollHost()?.scrollTo') && (library.includes("closest('.site-content')") || library.includes("closest<HTMLElement>('.site-content')"))],
  ['移动端使用筛选抽屉且详情不再嵌套列表', library.includes('MobileFilterSheet') && !library.includes('class="deck-detail"')],
  ['详情页展示完整构筑和摘要', detail.includes('DeckConstructionBrowser') && detail.includes('费用曲线') && detail.includes('构筑摘要')],
  ['公开详情筛选移入摘要下方且原行移除', detail.includes('id="public-deck-construction-filters"') && detail.includes('filter-target="#public-deck-construction-filters"') && detail.includes('hide-header') && browser.includes('class="construction-filter-rail"') && browser.includes('<nav v-else aria-label="构筑筛选">')],
  ['公开详情筛选按搜索、区域、类型纵向排列', browser.indexOf('aria-label="搜索卡名或编号"') < browser.indexOf('aria-label="按区域筛选"') && browser.indexOf('aria-label="按区域筛选"') < browser.indexOf('aria-label="按类型筛选"') && browser.includes('.construction-filter-rail{display:grid;gap:8px}')],
  ['构筑摘要只显示非零类别且试炼归入额外', ['entry.deck.cardIds.length', 'entry.deck.moraleIds.length', 'entry.deck.specialIds?.length', 'automaticExtraCardIdsForMaster(entry.deck.masterId).length'].every(value => detail.includes(`v-if="${value}"`)) && detail.includes('>额外<strong>') && !detail.includes('>试炼/额外<strong>')],
  ['详情页保留核心操作', ['toggleLike', 'copyToMine', 'copyCode', 'previewImage', 'editDeck', 'deleteDeck'].every(key => detail.includes(key))],
  ['构筑详情复用图鉴卡牌详情', browser.includes('CatalogCardDetails') && browser.includes(':show-catalog-only="false"')],
  ['窄屏详情使用安全区', catalogDetails.includes('safe-area-inset') && catalogDetails.includes('max-height:100%')],
  ['详情内容覆盖非空指南与对局建议', ['data-detail-section="guide"', 'data-detail-section="matchups"', 'hasGuide', 'hasMatchups'].every(key => detail.includes(key)) && content.includes('updateContent')],
  ['详情内容覆盖长期版本、准确对局与随机起手', ['data-detail-section="versions"', 'data-detail-section="matches"', 'data-detail-section="hands"'].every(key => detail.includes(key))],
  ['对局记录不拿作者总战绩替代', detailsStore.includes('不会用作者总战绩替代') && detailsStore.includes('"unavailable"')],
  ['对局区使用匿名聚合统计而非单局回放', detail.includes('match-stat-list') && detail.includes('matchStatisticsRange') && !detail.includes('recordedMatchId')],
  ['点赞复制与浏览计数更新不会清空已加载详情', detail.includes('preservePublicDeckDetails(entry.value, value)') && (detail.match(/preservePublicDeckDetails\(entry\.value, updated\)/g)?.length ?? 0) >= 2],
  ['牌库界面对玩家只提供已实装主城，不开放 divinity 模式', content.includes("card.cardType === 'master'") && !content.includes("card.cardType === 'master' || card.cardType === 'divinity'")],
]

for (const [label, passed] of checks) if (!passed) throw new Error(`公开牌库浏览合同失败：${label}`)
console.log(`公开牌库浏览合同通过：${checks.length} 项`)
