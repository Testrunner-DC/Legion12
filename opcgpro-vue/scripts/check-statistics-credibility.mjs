import assert from 'node:assert/strict'
import fs from 'node:fs'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const Vue = require('vue')
const ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')

const read = relative => fs.readFileSync(new URL(relative, import.meta.url), 'utf8')
const ranked = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.Ranked.cs')
const rankedTests = read('../../TwelveLegions.Platform.Tests/RankedAnalyticsRangeTests.cs')
const releaseVerifier = read('../../ops/windows/verify-l12.ps1')
const platform = read('../src/l12/platform.ts')
const scope = read('../src/l12/site/StatisticsScope.vue')
const rankings = read('../src/l12/site/RankingsPage.vue')
const matrix = read('../src/l12/site/MasterMatchupMatrix.vue')
const publicDeck = read('../src/l12/site/PublicDeckDetailPage.vue')
const adminMaster = read('../src/l12/site/AdminMasterAnalyticsPanel.vue')
const adminCard = read('../src/l12/site/AdminCardAnalyticsPanel.vue')

const checks = [
  ['隔离发布保留统计测试输入', releaseVerifier.includes('Source = "TwelveLegions.Platform.Tests\\RankedAnalyticsRangeTests.cs"; Target = "TwelveLegions.Platform.Tests\\RankedAnalyticsRangeTests.cs"')],
  ['排行只追加权威范围元数据', ranked.includes('DateTimeOffset? FromUtc = null')
    && ranked.includes('DateTimeOffset? UntilUtc = null')
    && ranked.includes('string? SeasonId = null, string? SeasonName = null')
    && ranked.includes('var scopeFrom = rangeStart == DateTimeOffset.MinValue')
    && ranked.includes('var scopeUntil = rangeEnd < now ? rangeEnd : now')],
  ['前端兼容旧排行响应', platform.includes('fromUtc?: string | null')
    && platform.includes('untilUtc?: string | null')
    && rankings.includes('服务端未返回精确起止时间')],
  ['窗口专项锁定滚动与赛季字段', rankedTests.includes('Assert.Equal(now.AddDays(-7), seven.FromUtc)')
    && rankedTests.includes('Assert.Equal(now.AddDays(-30), thirty.FromUtc)')
    && rankedTests.includes('Assert.Equal(operations.Config.Season.Name, season.SeasonName)')],
  ['口径组件仅负责折叠展示', scope.includes('defineProps') && scope.includes('<details v-if="items.length">')
    && scope.includes('.statistics-scope details[open]{grid-column:1/-1}')
    && !scope.includes('position:absolute') && !scope.includes('fetch(') && !scope.includes('platformRequest')],
  ['排行榜显示服务端窗口与统一排除范围', rankings.includes('<StatisticsScope')
    && rankings.includes('parseScopeDate(analytics.value.fromUtc)')
    && rankings.includes("analytics.value.range === 'season'")
    && rankings.includes('tab !== \'history\' && hasAnalytics')
    && rankings.includes('暂扣、作废、系统异常')
    && rankings.includes('未按运营规则版本或卡效版本拆分')],
  ['公开主宰低样本显示百分比与浅色提醒', rankings.includes('const publicMasterSampleMinimum = 30')
    && rankings.includes(':minimum-sample="publicMasterSampleMinimum"')
    && rankings.includes('low-sample-display="muted"')
    && rankings.includes('sampledPercent(row.winRate, row.games)')
    && rankings.includes('samples > 0 ? percent(value)')
    && rankings.includes('场不是结算门槛')],
  ['主宰矩阵低样本显示可选且后台保持原口径', matrix.includes("lowSampleDisplay?: 'hidden' | 'muted'")
    && matrix.includes("lowSampleDisplay: 'hidden'")
    && matrix.includes("[tone, 'low-sample']")
    && matrix.includes('matchup.samples > 0')
    && matrix.includes('不足 ${props.minimumSample} 场，仅显示样本')
    && matrix.includes("initiativeTitle('先手', value.firstWins, value.firstSamples)")
    && matrix.includes('暂无对局')
    && adminMaster.includes('<MasterMatchupMatrix :masters="matrixRows" :cells="matrixCells" :minimum-sample="30"')
    && !adminMaster.includes('low-sample-display')],
  ['近30日最强称号独立标注', rankings.includes('最强玩家<small>近30日</small>')
    && rankings.includes('最强玩家称号另按近 30 日独立口径产生')],
  ['公开牌库显示可公开样本但不补低样本', publicDeck.includes('<StatisticsScope')
    && publicDeck.includes('可展示 ${statistics.games} 场') && publicDeck.includes('statisticsPage.value')
    && publicDeck.includes('低于门槛的组不会返回场次、胜负或胜率')],
  ['后台主宰口径绑定成功响应', adminMaster.includes('<StatisticsScope')
    && adminMaster.includes('appliedScope.value = nextScope')
    && adminMaster.includes('v-if="report && appliedScope"')
    && adminMaster.includes('当前卡效版本') && adminMaster.includes('30 份仅为展示提醒')],
  ['后台单卡口径绑定成功响应', adminCard.includes('<StatisticsScope')
    && adminCard.includes('listAppliedScope.value = nextScope')
    && adminCard.includes('detailAppliedScope.value = nextScope')
    && adminCard.includes('先后手：${query.initiative')
    && adminCard.includes('服务端未返回样本汇总')
    && adminCard.includes('不把低样本相关性写成因果')],
]

assert.deepEqual(checks.filter(([, passed]) => !passed).map(([label]) => label), [])

// Mount the actual RankingsPage script and template in Vue's in-memory host.
// These are rendered row/model-update checks, not a copied comparator or a
// browser/layout claim. The browser companion retains the geometry checks.
function element(tag, value = '') {
  return { tag, text: value, children: [], parent: null, props: {}, listeners: new Map(),
    tagName: tag.toUpperCase(), value: '', selected: false,
    get options() { return descendants(this, item => item.tag === 'option') },
    addEventListener(name, handler) { this.listeners.set(name, handler) },
    removeEventListener(name) { this.listeners.delete(name) },
  }
}
function descendants(node, predicate) { return [...(predicate(node) ? [node] : []), ...node.children.flatMap(child => descendants(child, predicate))] }
function content(node) { return node.tag === '#comment' ? '' : node.text + node.children.map(content).join('') }
function detach(node) {
  if (node.parent) { node.parent.children.splice(node.parent.children.indexOf(node), 1); node.parent = null }
}
const renderer = Vue.createRenderer({
  createElement: tag => element(tag), createText: value => element('#text', value), createComment: value => element('#comment', value),
  insert(node, parent, anchor = null) { detach(node); node.parent = parent; const index = anchor ? parent.children.indexOf(anchor) : -1;
    if (index < 0) parent.children.push(node); else parent.children.splice(index, 0, node) },
  remove: detach, parentNode: node => node.parent, nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] ?? null,
  setText: (node, value) => { node.text = value },
  setElementText(node, value) { node.children.forEach(child => { child.parent = null }); node.children = []; node.text = value },
  patchProp(node, name, previous, value) { node.props[name] = value; if (name === 'value') { node.value = value; node._value = value } },
  setScopeId() {},
})
function evaluate(code, imports) {
  const compiled = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } })
  const module = { exports: {} }
  new Function('require', 'exports', 'module', 'document', compiled.outputText)(id => {
    if (id === 'vue') return Vue
    assert.ok(Object.hasOwn(imports, id), `unexpected actual RankingsPage dependency ${id}`)
    return imports[id]
  }, module.exports, module, { hidden: true, addEventListener() {}, removeEventListener() {} })
  return module.exports
}
const settle = async () => { await Promise.resolve(); await Vue.nextTick(); await Promise.resolve(); await Vue.nextTick() }
const fixtureState = { masters: [], calls: [] }
const stub = { __esModule: true, default: { render: () => Vue.h('span') } }
const filename = new URL('../src/l12/site/RankingsPage.vue', import.meta.url).pathname
const parsed = parse(rankings, { filename })
assert.deepEqual(parsed.errors, [])
const script = compileScript(parsed.descriptor, { id: 'rankings-rate-regression' })
const template = compileTemplate({ id: 'rankings-rate-regression', filename, source: parsed.descriptor.template.content,
  compilerOptions: { bindingMetadata: script.bindings, hoistStatic: false } })
assert.deepEqual(template.errors, [])
const RankingPage = evaluate(script.content, {
  '@/l12/specialAssets': { masterProfileUrl: id => `synthetic:${id}` },
  '@/l12/RankedIdentityBadge.vue': stub, './RankedMasterTitleRulesModal.vue': stub,
  './MasterMatchupMatrix.vue': stub, './StatisticsScope.vue': stub,
  '@/l12/platform': { platformState: { account: null }, rankedApi: {
    leaderboard: async (faction, range) => { fixtureState.calls.push({ faction, range }); return { players: [],
      analytics: { range, masters: structuredClone(fixtureState.masters), matchups: [],
        summary: { matches: 100, placedPlayers: 0, activeMasters: fixtureState.masters.length } } } },
    history: async () => ({ honors: [], factionTotals: [] }),
  } },
}).default
RankingPage.render = evaluate(template.code, {}).render
const container = element('root')
const app = renderer.createApp(RankingPage)
const warnings = []
app.config.warnHandler = warning => warnings.push(warning)
const row = (id, games, winRate, firstGames = games, firstWinRate = winRate, secondGames = games, secondWinRate = winRate) => ({
  rank: 99, masterId: id, masterName: id, games, wins: games * winRate / 100, losses: games * (100 - winRate) / 100,
  winRate, usageRate: games, firstGames, firstWins: firstGames * firstWinRate / 100, firstWinRate,
  secondGames, secondWins: secondGames * secondWinRate / 100, secondWinRate, strongestPlayer: null, title: null,
})
const rows = () => descendants(container, node => String(node.props.class || '').split(/\s+/).includes('tr'))
const cell = (node, label) => descendants(node, item => item.props['data-label'] === label)[0]
const order = () => rows().map(node => content(descendants(cell(node, '主宰'), item => item.tag === 'small')[0]))
async function chooseSort(sort) {
  const control = descendants(container, node => node.tag === 'select')[0]
  control.props['onUpdate:modelValue'](sort)
  await settle()
}
async function loadRows(values) {
  fixtureState.masters = values
  const refresh = descendants(container, node => node.tag === 'button' && content(node) === '刷新数据')[0]
  refresh.props.onClick(); await settle()
}
let behaviorChecks = 0
async function behavior(name, action) { await action(); behaviorChecks++; console.log(`PASS rankings ${behaviorChecks}: ${name}`) }
try {
  app.mount(container); await settle()
  descendants(container, node => node.tag === 'button' && content(node) === '主宰榜')[0].props.onClick(); await settle()
  await behavior('equal four-game samples sort 50% ahead of 0%', async () => {
    await loadRows([{ ...row('four-zero', 4, 0), masterName: '同4场0%' },
      { ...row('four-half', 4, 50), masterName: '同4场50%' }]); await chooseSort('winRate')
    assert.deepEqual(order(), ['four-half', 'four-zero'])
    assert.deepEqual(rows().map(node => content(cell(node, '胜率'))), ['50.0%', '0.0%'])
  })
  for (const sort of ['winRate', 'firstWinRate', 'secondWinRate']) await behavior(`${sort} orders actual percentages across the 30-sample boundary`, async () => {
    await loadRows([row('large-low', 40, 25), row('small-high', 4, 75), row('zero', 4, 0)]); await chooseSort(sort)
    assert.deepEqual(order(), ['small-high', 'large-low', 'zero'])
  })
  for (const [sort, label] of [['firstWinRate', '先手'], ['secondWinRate', '后手']]) await behavior(`${sort} puts a known 0% before a missing initiative sample`, async () => {
    const absent = row('absent', 100, 80, sort === 'firstWinRate' ? 0 : 100, 100, sort === 'secondWinRate' ? 0 : 100, 100)
    await loadRows([absent, row('known-zero', 4, 0), row('known-half', 4, 50)]); await chooseSort(sort)
    assert.deepEqual(order(), ['known-half', 'known-zero', 'absent'])
    assert.match(content(cell(rows()[1], label)), /^0\.0%/)
    assert.match(content(cell(rows()[2], label)), /^—0\/0$/)
  })
  await behavior('all missing initiative samples retain deterministic total-games tie-break', async () => {
    await loadRows([row('missing-small', 4, 0, 0, 100), row('missing-large', 40, 80, 0, 0)]); await chooseSort('firstWinRate')
    assert.deepEqual(order(), ['missing-large', 'missing-small'])
  })
  await behavior('switching initiative sorts uses the selected percentage, not the overall rate', async () => {
    await loadRows([row('first-high', 40, 25, 20, 100, 20, 0), row('second-high', 40, 50, 20, 0, 20, 100),
      row('balanced', 40, 75, 20, 50, 20, 50)])
    await chooseSort('firstWinRate'); assert.deepEqual(order(), ['first-high', 'balanced', 'second-high'])
    await chooseSort('secondWinRate'); assert.deepEqual(order(), ['second-high', 'balanced', 'first-high'])
  })
  for (const sort of ['winRate', 'firstWinRate', 'secondWinRate']) await behavior(`${sort} ties retain total-games then name tie-breaks`, async () => {
    await loadRows([row('tie-b', 4, 50), row('tie-a', 4, 50), row('tie-big', 40, 50)]); await chooseSort(sort)
    assert.deepEqual(order(), ['tie-big', 'tie-a', 'tie-b'])
  })
  await behavior('games and usage ordering still use their original primary value', async () => {
    const large = row('large', 40, 0), small = { ...row('small', 4, 100), usageRate: 90 }
    await loadRows([small, large]); await chooseSort('games'); assert.deepEqual(order(), ['large', 'small'])
    await chooseSort('usageRate'); assert.deepEqual(order(), ['small', 'large'])
  })
  assert.deepEqual(warnings, [])
  assert(fixtureState.calls.every(call => call.range === 'season' && call.faction === ''), 'sorting must not mutate source/scope')
} finally {
  app.unmount()
}
console.log(`统计可信度契约通过：${checks.length}/${checks.length} 项；真实主宰组件行为 ${behaviorChecks}/${behaviorChecks}`)
export const publicMasterSortingVerified = behaviorChecks === 12
