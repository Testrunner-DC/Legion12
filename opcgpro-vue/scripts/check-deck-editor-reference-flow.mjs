import fs from 'node:fs'

const read = relative => fs.readFileSync(new URL(relative, import.meta.url), 'utf8')
const editor = read('../src/l12/L12DeckEditor.vue')
const decks = read('../src/l12/decks.ts')
const models = read('../../服务端WebSocket/TwelveLegions/Models.cs')
const storage = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.DeckStorage.cs')
const store = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.cs')
const validator = read('../../服务端WebSocket/TwelveLegions/L12DeckValidator.cs')

const checks = [
  ['卡池 / 统计 / 起手使用同一编辑器工作区', ['>卡池</button>', '>统计</button>', '>起手</button>'].every(value => editor.includes(value))],
  ['编辑器仅保留指定英文标题，其余工作区标签中文化', editor.includes('<small>DECK EDITOR</small>') && !['Gallery 卡池', 'Stats 统计', 'Hand 起手', 'CARD POOL', 'DECK STATS', 'OPENING HAND', 'DECK LIST'].some(value => editor.includes(value))],
  ['工作区使用 v-show 保留卡池筛选与滚动节点', ['workspace === \'gallery\'', 'workspace === \'stats\'', 'workspace === \'hand\''].every(value => editor.includes(`v-show="${value}"`))],
  ['起手从未保存的当前构筑抽取且不写后台', editor.includes('samplePublicDeckOpeningHand(entries.value.flatMap') && editor.includes('不修改牌库或生成对局记录')],
  ['移动端为卡池、牌表、统计/起手单任务入口', ['setMobilePane(\'pool\')', 'setMobilePane(\'deck\')', 'setMobilePane(\'insights\')'].every(value => editor.includes(value))],
  ['仅卡池产品使用按钮弹层，其余筛选始终可见', editor.includes('class="pool-selector-trigger"') && editor.includes('v-if="poolSelectorOpen" class="product-filter"') && editor.includes('class="catalog-filter-bar"') && !editor.includes('mobileFiltersOpen') && !editor.includes('卡池（可多选）')],
  ['卡池覆盖名称效果、阵营、类型、产品、费用、兵力、天灾、禁限与排序', ['query', 'factionFilter', 'typeFilter', 'productFilters', 'costFilter', 'troopsFilter', 'disasterFilter', 'legalityFilter', 'sortMode'].every(value => editor.includes(value))],
  ['主宰、主牌、士气、额外区、备选区独立分区', ['data-deck-section="master"', 'data-deck-section="main"', 'data-deck-section="morale"', 'data-deck-section="extra"', 'data-deck-section="bench"'].every(value => editor.includes(value)) && editor.includes('<span>主宰</span>')],
  ['主宰候选排除未实装 divinity 模式', editor.includes("card.cardType === 'master'") && !editor.match(/card\.cardType === 'master'\s*\|\|\s*card\.cardType === 'divinity'/)],
  ['已保存牌库只有一个切换入口且详情可折叠', editor.includes('class="saved-decks-panel"') && editor.includes('<label>已保存牌库<select') && editor.includes('detailCollapsed') && !editor.includes('class="saved-deck-switcher"') && !editor.includes('class="saved-decks-panel grand-panel"')],
  ['起手完整展示卡面、名称、编号与单次概率说明', editor.includes('fit="contain"') && editor.includes('openingHandMeta(card,index)') && editor.includes('等概率、不放回') && editor.includes('overflow-wrap:anywhere')],
  ['移动端次要操作收进更多操作菜单', editor.includes('class="more-actions-trigger"') && editor.includes('class="secondary-actions"')],
  ['分区折叠状态持久保留', editor.includes('l12-deck-editor-sections-v1') && editor.includes('watch(collapsedSections')],
  ['禁限、超量与阵营问题就地显示', editor.includes('entryIssue(entry.card, entry.count)') && editor.includes('operationsRestrictions')],
  ['备选区可从卡池加入并与主牌双向移动', ['addToBench(entry.card)', 'moveMainToBench(entry.card)', 'moveBenchToMain(entry.card)'].every(value => editor.includes(value))],
  ['备选区不计主牌数量与合法性', editor.includes('备选区') && editor.includes('不计入主牌数量与合法性') && !editor.match(/validateDeck\([\s\S]{0,300}benchIds/)],
  ['私人牌库类型与本地缓存保留 benchIds', decks.includes('benchIds?: string[]') && decks.includes('benchIds: (deck.benchIds ?? [])')],
  ['服务端输入与私人牌库视图支持 BenchIds', models.includes('public List<string> BenchIds') && store.includes('IReadOnlyList<string>? BenchIds = null')],
  ['备选区在账号牌库行使用紧凑数量 JSON', storage.includes('bench_cards_json') && storage.includes('CompactDeckCardsJson(deck.BenchIds)') && storage.includes('ExpandCards(reader.GetString(5))')],
  ['备选区不进入公开构筑正文哈希', !storage.match(/NormalizeDeckPayload\([^\n]*BenchIds/) && store.includes('row.BenchIds = deck.BenchIds.ToList()')],
  ['服务端限制未知、异阵营、非主牌与超大备选区', ['备选区最多保存 200 张卡牌', '备选区包含未知卡牌', '不能放入备选区', '与主宰阵营不符'].every(value => validator.includes(value))],
  ['竖屏不再强制提示旋转设备', !editor.includes('横屏编辑更完整') && !editor.includes('orientation:portrait')],
  ['未加入自动保存、撤销重做或离线队列', !/auto.?save|undo|redo|offline.?queue/i.test(editor)],
]

for (const [label, passed] of checks) if (!passed) throw new Error(`牌库编辑器参考流程合同失败：${label}`)
console.log(`牌库编辑器参考流程合同通过：${checks.length} 项`)
