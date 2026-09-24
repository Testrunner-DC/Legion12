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

const checks = [
  ['公开牌库详情具备独立路由', router.includes("path: '/decks/:deckId'") && router.includes('PublicDeckDetailPage.vue')],
  ['公开牌库详情具备直读接口', platform.includes('get: (id: string)') && server.includes('_platform.PublishedDeck(id')],
  ['组合筛选完整', ['masterFilter', 'factionFilter', 'legalFilter', 'cardFilter', 'updatedFilter'].every(key => library.includes(key))],
  ['筛选状态写入URL', ['tab', 'q', 'master', 'faction', 'legal', 'card', 'updated', 'sort'].every(key => library.includes(`'${key}'`))],
  ['浏览器前进后退恢复筛选', library.includes('watch(() => route.query, restoreFiltersFromRoute')],
  ['列表返回位置可恢复', library.includes('l12:deck-library:scroll:') && library.includes('window.scrollTo')],
  ['移动端使用筛选抽屉且详情不再嵌套列表', library.includes('MobileFilterSheet') && !library.includes('class="deck-detail"')],
  ['详情页展示完整构筑和摘要', detail.includes('DeckConstructionBrowser') && detail.includes('费用曲线') && detail.includes('构筑摘要')],
  ['详情页保留核心操作', ['toggleLike', 'copyToMine', 'copyCode', 'previewImage', 'editDeck', 'deleteDeck'].every(key => detail.includes(key))],
  ['构筑详情复用共享卡牌详情', browser.includes('CardDetailContent') && browser.includes(':show-catalog-only="false"')],
  ['窄屏详情使用安全区', browser.includes('construction-detail-mask') && browser.includes('safe-area-inset')],
]

for (const [label, passed] of checks) if (!passed) throw new Error(`公开牌库浏览合同失败：${label}`)
console.log(`公开牌库浏览合同通过：${checks.length} 项`)
