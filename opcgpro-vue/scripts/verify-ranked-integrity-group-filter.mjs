import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'
import { createHash } from 'node:crypto'

const root = path.resolve(import.meta.dirname, '..')
const requireDependency = createRequire(process.env.L12_NODE_DEPENDENCY_PACKAGE || path.join(root, 'package.json'))
const { createServer } = await import(pathToFileURL(requireDependency.resolve('vite')).href)
const { default: vue } = await import(pathToFileURL(requireDependency.resolve('@vitejs/plugin-vue')).href)
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = path.resolve(process.env.L12_GROUP_TEST_OUT || path.join(root, '../artifacts/integrity-group-filter'))
fs.mkdirSync(output, { recursive: true })
const sources = ['src/l12/site/AdminRankedIntegrityPanel.vue', 'scripts/verify-ranked-integrity-group-filter.mjs']
const hashes = () => sources.map(relative => ({ relative, sha256: createHash('sha256').update(fs.readFileSync(path.join(root, relative))).digest('hex') }))
const before = hashes()
const baseRow = (id, first, second, winner = 0, terminal = false, transfer = false) => ({
  id: `audit-${id}`, matchId: id, seasonId: 'synthetic-season', firstAccountId: first.id, firstPlayer: first.name,
  secondAccountId: second.id, secondPlayer: second.name, winner, durationMs: 60000, meaningfulCommandCount: 3,
  conclusionKind: 'surrender', finalRound: 1, signals: [{ code: transfer ? 'unilateral-score-transfer' : 'very-short-match', label: '合成风险信号' }],
  reviewRecommended: true, effectiveDisposition: terminal ? 'normal' : 'unreviewed', enforcement: 'none', createdAt: '2026-10-08T00:00:00Z',
})
const alpha = { id: 'synthetic-alpha', name: 'Alpha' }, beta = { id: 'synthetic-beta', name: 'Beta' }
const gamma = { id: 'synthetic-gamma', name: 'Gamma' }, delta = { id: 'synthetic-delta', name: 'Delta' }
const rows = []
for (let i = 0; i < 61; i++) {
  rows.push(baseRow(`A-${i}`, i % 2 ? beta : alpha, i % 2 ? alpha : beta, i % 2 ? 1 : 0, i === 0))
  if (i < 20) rows.push(baseRow(`B-${i}`, gamma, delta))
  if (i < 5) rows.push(baseRow(`C-${i}`, alpha, { id: `synthetic-other-${i}`, name: `Other${i}` }, 0, false, true))
}
const entry = `
import { createApp, h } from 'vue';
import Panel from '/src/l12/site/AdminRankedIntegrityPanel.vue';
window.__rows=${JSON.stringify(rows)};window.__plans=[];window.__queries=[];
createApp({render:()=>h(Panel)}).mount('#app');`
const virtual = {
  'platform': `export const adminApi={rankedIntegrityAudits:async(query)=>{window.__queries.push({...query});const plan=window.__plans.shift();if(plan?.delay)await new Promise(resolve=>setTimeout(resolve,plan.delay));if(plan?.error)throw new Error(plan.error);const rows=plan?.rows??window.__rows;return structuredClone(query.reviewOnly?rows.filter(row=>row.reviewRecommended&&!['normal','insufficient','system-error','confirmed'].includes(row.effectiveDisposition)):rows)}};`,
  'actions': `import {h} from 'vue';export default {props:['rows','selected'],render(){return h('div',{class:'selected-fixture','data-selected':this.selected.join(',')},'已选 '+this.selected.length+' 条')}};`,
  'picker': `import {h} from 'vue';export default {props:['modelValue','label'],emits:['update:modelValue','change'],render(){return h('span',this.label)}};`,
  'label': `export const integrityLabel=value=>value;`,
}
const server = await createServer({
  configFile: false, root, cacheDir: path.join(output, 'vite-cache'),
  // This isolated component fixture uses stub imports and transpilation only;
  // project TypeScript/build gates remain the primary task's separate checks.
  oxc: { tsconfig: false },
  resolve: { alias: { vue: path.join(path.dirname(requireDependency.resolve('vue/package.json')), 'dist/vue.runtime.esm-bundler.js') } },
  optimizeDeps: { noDiscovery: true, include: [] },
  server: { host: '127.0.0.1', port: 0, fs: { allow: [root, path.dirname(requireDependency.resolve('vue/package.json')), path.dirname(requireDependency.resolve('@vitejs/plugin-vue'))] } },
  plugins: [{ name: 'integrity-group-fixture', enforce: 'pre',
    resolveId(id, importer) {
      if (id === '/__group_entry__.js') return '\0group-entry'
      if (importer?.endsWith('AdminRankedIntegrityPanel.vue')) {
        const kind = id === '@/l12/platform' ? 'platform' : id === './RankedIntegrityActions.vue' ? 'actions'
          : id === './AdminAccountPicker.vue' ? 'picker' : id === '../rankedIntegrity' ? 'label' : null
        if (kind) return `\0group-${kind}`
      }
    },
    load(id) { return id === '\0group-entry' ? entry : id.startsWith('\0group-') ? virtual[id.slice(7)] : null },
    configureServer(instance) { instance.middlewares.use((request, response, next) => {
      if (request.url?.startsWith('/__group__')) {
        response.setHeader('Content-Type', 'text/html')
        response.end('<meta name="viewport" content="width=device-width,initial-scale=1"><style>body{font-family:Arial,"Microsoft YaHei",sans-serif;background:#101821;color:#ddd}</style><div id="app"></div><script type="module" src="/__group_entry__.js"></script>')
      } else next()
    }) },
  }, vue()],
})
let browser, passed = 0
const errors = []
const checks = []
async function check(name, action) { await action(); checks.push(name); passed++ }
try {
  await server.listen()
  browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
    ? { headless: true, executablePath: process.env.L12_CHROMIUM_EXECUTABLE }
    : { headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  page.on('pageerror', error => { errors.push(error.message); console.error('Fixture browser error:', error.message) })
  page.on('console', message => { if (message.text().includes('[Vue warn]')) errors.push(message.text()) })
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__group__`)
  await page.locator('.integrity-row').first().waitFor()
  const visible = () => page.locator('.integrity-row > span:first-child > code').allTextContents()
  const pagination = () => page.locator('.collection-pagination').innerText()
  const groupA = () => page.locator('.risk-groups').nth(0).getByRole('button', { name: /Alpha \/ Beta/ })
  const groupB = () => page.locator('.risk-groups').nth(0).getByRole('button', { name: /Gamma \/ Delta/ })
  const next = () => page.locator('.collection-pagination').getByRole('button', { name: '下一页' }).click()
  const query = () => page.getByRole('button', { name: '查询', exact: true }).click()
  await page.locator('.filters input[type=checkbox]').uncheck()
  await query()
  await page.waitForFunction(() => document.querySelector('.integrity-row > span:first-child > code')?.textContent === 'A-0')
  const queryCount = await page.evaluate(() => window.__queries.length)
  await page.locator('.risk-groups').nth(0).locator('summary').click()
  await page.locator('.risk-groups').nth(1).locator('summary').click()
  await next(); await next()
  await groupA().click()
  const originalClick = { visible: await visible(), pagination: await pagination(), selected: await page.locator('.selected-fixture').getAttribute('data-selected') }
  fs.writeFileSync(path.join(output, 'first-group-click.json'), JSON.stringify(originalClick, null, 2))
  await page.screenshot({ path: path.join(output, 'first-group-click.png'), fullPage: true })
  await check('group click filters across displayed pages and resets to page one', async () => {
    assert.ok(originalClick.visible.every(id => id.startsWith('A-')), JSON.stringify(originalClick))
    assert.match(originalClick.pagination, /第 1 \/ 7 页.*共 61 条/)
    assert.equal(await page.evaluate(() => window.__queries.length), queryCount, 'group click must not fetch full history')
  })
  await check('more than fifty group members remain browseable; processed rows stay unselected', async () => {
    const selected = (await page.locator('.selected-fixture').getAttribute('data-selected')).split(',')
    assert.equal(selected.length, 50)
    assert.ok(selected.every(id => id.startsWith('A-') && id !== 'A-0'))
    assert.equal(await page.locator('.integrity-row').filter({ has: page.locator('code', { hasText: /^A-0$/ }) }).locator('input[type=checkbox]').isDisabled(), true)
    const found = new Set(await visible())
    for (let i = 1; i < 7; i++) { await next(); for (const id of await visible()) found.add(id) }
    assert.equal(found.size, 61)
    assert.ok([...found].every(id => id.startsWith('A-')))
  })
  await check('clicking the same group again resets to page one', async () => {
    await groupA().click()
    assert.match(await pagination(), /第 1 \/ 7 页.*共 61 条/)
    await next(); await next()
  })
  await check('another group resets the page and keeps correct count', async () => {
    await groupB().click()
    assert.ok((await visible()).every(id => id.startsWith('B-')))
    assert.match(await pagination(), /第 1 \/ 2 页.*共 20 条/)
    await next()
  })
  await check('beneficiary group uses its actual member IDs', async () => {
    await page.locator('.risk-groups').nth(1).getByRole('button', { name: /Alpha/ }).click()
    assert.deepEqual(new Set(await visible()), new Set(['C-0', 'C-1', 'C-2', 'C-3', 'C-4']))
    assert.match(await page.locator('.group-filter').innerText(), /5条.*当前查询结果/)
  })
  await check('clearing a group restores the bounded query collection and page one', async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'filter controls overflow narrow viewport')
    await page.screenshot({ path: path.join(output, 'group-filter-390.png'), fullPage: true })
    await page.getByRole('button', { name: '清除归组筛选' }).click()
    assert.match(await pagination(), /第 1 \/ 9 页.*共 86 条/)
    assert.equal(await page.locator('.group-filter').count(), 0)
    assert.equal(await page.evaluate(() => window.__queries.length), queryCount)
    assert.match(await page.locator('.query-scope').innerText(), /300.*完整对局历史/)
    await page.setViewportSize({ width: 1280, height: 900 })
  })
  await check('query reload invalidates previous group identity and selections', async () => {
    await groupA().click()
    await page.evaluate(value => window.__plans.push({ rows: value }), rows.filter(row => row.matchId.startsWith('B-')).slice(0, 6))
    await query()
    await page.waitForFunction(() => document.querySelectorAll('.integrity-row').length === 6)
    assert.equal(await page.locator('.group-filter').count(), 0)
    assert.equal(await page.locator('.selected-fixture').getAttribute('data-selected'), '')
    assert.ok((await visible()).every(id => id.startsWith('B-')))
  })
  await check('empty and failed reload never present old data as current or full history', async () => {
    await page.evaluate(() => window.__plans.push({ rows: [] }))
    await query(); await page.locator('.empty').waitFor()
    assert.equal(await page.locator('.integrity-row').count(), 0)
    await page.evaluate(() => window.__plans.push({ error: '合成查询失败' }))
    await query(); await page.getByText('合成查询失败', { exact: true }).waitFor()
    assert.equal(await page.locator('.integrity-row').count(), 0)
    assert.equal(await page.locator('.empty').count(), 0)
    assert.equal(await page.locator('.group-filter').count(), 0)
    assert.match(await page.locator('.query-scope').innerText(), /当前查询加载失败/)
  })
  await check('late previous query cannot restore a stale group or overwrite latest rows', async () => {
    await page.evaluate(value => window.__plans.push({ rows: value, delay: 400 }, { rows: value.filter(row => row.matchId.startsWith('B-')).slice(0, 3), delay: 20 }), rows)
    const input = page.getByPlaceholder('对局 ID')
    await input.fill('old'); await input.press('Enter')
    await input.fill('new'); await input.press('Enter')
    await page.waitForFunction(() => document.querySelectorAll('.integrity-row').length === 3)
    await new Promise(resolve => setTimeout(resolve, 500))
    assert.equal(await page.locator('.integrity-row').count(), 3)
    assert.ok((await visible()).every(id => id.startsWith('B-')))
    assert.equal(await page.locator('.group-filter').count(), 0)
  })
  assert.deepEqual(errors, [])
  assert.ok(await page.evaluate(() => window.__queries.every(query => query.limit === 300)), 'existing query limit changed')
  assert.deepEqual(hashes(), before, 'source changed during real component test')
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ status: 'passed', passed, checks, sources: before, errors }, null, 2))
  console.log(`Ranked integrity group filtering: ${passed}/${passed} actual Vue/PagedCollection behavior checks passed.`)
} catch (error) {
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ status: 'failed', passed, checks, sources: before, errors, error: String(error) }, null, 2))
  throw error
} finally { await browser?.close(); await server.close() }
