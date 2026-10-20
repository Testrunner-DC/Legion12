import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'

const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const board = fs.readFileSync(path.join(frontend, 'src/l12/game/GameBoard.vue'), 'utf8')
const match = board.match(/function statusTexts\(card: Card\) \{[\s\S]*?\n\}\s*<\/script>/)
assert.ok(match, 'the actual shared desktop/mobile status formatter must be present')
const formatter = ts.transpileModule(match[0].replace(/\s*<\/script>$/, ''), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
}).outputText
const oldCard = { instanceId: 'moon-unit', troops: 3000, baseTroops: 3000,
  statusEffects: [{ kind: 'conditional-attack', label: '本回合每次进攻兵力＋1000', source: '月读' }] }
const liveCard = { ...oldCard, statusEffects: [{ kind: 'conditional-attack', label: '本回合每次进攻兵力＋2000', source: '月读' }] }
const props = { game: { players: [{ field: [[liveCard]] }, { field: [[null]] }] } }
const statusTexts = new Function('props', `${formatter}\nreturn statusTexts`)(props)
const before = JSON.stringify(props)
assert.deepEqual(statusTexts(oldCard), ['本回合每次进攻兵力＋2000（来源：月读）。'],
  'an already-open inspector must read the latest authority instance, not its captured old card')
assert.equal(JSON.stringify(props), before, 'formatting must not mutate the live projection')
liveCard.hidden = true
assert.deepEqual(statusTexts(oldCard), [], 'a hidden current instance must not expose its condition')
liveCard.hidden = false
liveCard.statusEffects = []
assert.deepEqual(statusTexts(oldCard), [], 'expiry/removal must not fall back to a captured old status')
props.game.players[0].field[0][0] = null
assert.deepEqual(statusTexts(oldCard), [], 'an instance no longer on the battlefield has no current attack accumulation')
props.game.players[0].field[0][0] = liveCard
liveCard.statusEffects = [{ kind: 'power-up', label: '临时兵力+1000', source: '另一效果' }]
assert.deepEqual(statusTexts(oldCard), [], 'other status families are not duplicated by the conditional-attack extension')
console.log('Conditional attack status: six read-only/current-instance contracts passed.')

// Render the actual shared CardTile SFC, rather than duplicating its status mapper.
// Image modules are inert stubs here; real GamePage interaction remains below.
const tileSource = fs.readFileSync(path.join(frontend, 'src/l12/CardTile.vue'), 'utf8')
const tileRequire = createRequire(import.meta.url)
const { parse, compileScript } = tileRequire('@vue/compiler-sfc')
const vue = tileRequire('vue')
const { renderToString } = tileRequire('@vue/server-renderer')
const { descriptor } = parse(tileSource, { filename: 'CardTile.vue' })
const compiledTile = compileScript(descriptor, { id: 'moon-card-tile-regression', inlineTemplate: true })
const tileModule = { exports: {} }
const inertImage = { __esModule: true, default: { render: () => null } }
const resolveTileImport = id => {
  if (id === 'vue') return vue
  if (id === './CardImage.vue' || id === './RoundCardImage.vue') return inertImage
  if (id === './cardAssets') return { CARD_IMAGE_PLACEHOLDER: 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///w==' }
  if (id === './cardPresentation') return { isHorizontalCardType: () => false }
  throw new Error(`Uncovered real CardTile import: ${id}`)
}
new Function('require', 'module', 'exports', ts.transpileModule(compiledTile.content, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, esModuleInterop: true },
}).outputText)(resolveTileImport, tileModule, tileModule.exports)
const renderTile = async overrides => {
  const card = { instanceId: 'moon-tile', cardId: 'S01-0001', name: '合成军团', cardType: 'legion',
    cost: 4, troops: 3000, baseTroops: 3000, statusEffects: [], statusIcons: [], ...overrides }
  const captured = JSON.stringify(card)
  const html = await renderToString(vue.createSSRApp(tileModule.exports.default, { card }))
  assert.equal(JSON.stringify(card), captured, 'rendering a status must not mutate its authority projection')
  return html
}
const moon = count => ({ kind: 'conditional-attack', label: `本回合每次进攻兵力＋${count * 1000}`, source: '月读' })
const countMoon = html => (html.match(/class="card-status-icon status-conditional-attack"/g) ?? []).length
const one = await renderTile({ statusEffects: [moon(1)] })
assert.equal(countMoon(one), 1, 'a unit with the authoritative Moon buff needs one real rendered status icon')
assert(one.includes('aria-label="本回合每次进攻兵力＋1000（来源：月读）"'))
const two = await renderTile({ statusEffects: [moon(2)] })
assert.equal(countMoon(two), 1); assert(two.includes('title="本回合每次进攻兵力＋2000（来源：月读）"'))
assert.equal(countMoon(await renderTile({ statusEffects: [moon(2), moon(2)], statusIcons: ['conditional-attack'] })), 1)
const combined = await renderTile({ statusEffects: [moon(1), { kind: 'power-up', label: '临时兵力增加', source: '其他效果' }] })
assert.equal(countMoon(combined), 1); assert(combined.includes('card-status-icon status-power-up'))
assert.equal(countMoon(await renderTile({ statusEffects: [], statusIcons: [] })), 0)
assert.equal(countMoon(await renderTile({ hidden: true, identityKnown: false, statusEffects: [moon(2)] })), 0)
assert.equal(countMoon(await renderTile({ statusEffects: [{ kind: 'unrecognized-status', label: '未知状态' }] })), 0)
const legacy = await renderTile({ statusIcons: ['CONDITIONAL-ATTACK'] })
assert.equal(countMoon(legacy), 1); assert(legacy.includes('进攻时兵力增加'))
console.log('Conditional attack shared CardTile: eight actual-SFC rendered status groups passed; zero projection mutations.')

if (process.argv.includes('--browser')) {
  const output = process.env.L12_MOON_STATUS_OUT
  const cache = process.env.L12_VITE_CACHE_DIR
  assert.ok(output && cache && path.isAbsolute(output) && path.isAbsolute(cache), 'explicit evidence/cache roots required')
  const quote = text => text.replaceAll("'", "''")
  execFileSync('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile', '-Command', `. '${quote(path.join(frontend, '../ops/windows/L12GeneratedStorage.ps1'))}'; `
      + `$null=Assert-L12GeneratedPath '${quote(output)}' -Purpose 'Moon status real UI evidence'; `
      + `$null=Assert-L12GeneratedPath '${quote(cache)}' -Purpose 'Moon status owned Vite cache'`],
    { stdio: 'pipe', timeout: 15000, windowsHide: true })
  assert.equal(fs.existsSync(output), false, 'do not overwrite existing evidence')
  assert.ok((process.env.TEMP ?? '').replaceAll('\\', '/').startsWith('D:/GPT/Legion12/cache/primary/'),
    'the shared initializer must govern browser temporary files')
  fs.mkdirSync(output, { recursive: true })
  const { createServer } = await import('vite')
  const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const baseFixture = fs.readFileSync(path.join(frontend, 'scripts/verify-batch253-visual.mjs'), 'utf8')
  const marker = 'const entry = `'
  const start = baseFixture.indexOf(marker) + marker.length
  const end = baseFixture.indexOf('const isPicker=', start)
  assert.ok(start >= marker.length && end > start, 'the existing sanitized GamePage fixture must be available')
  const entry = baseFixture.slice(start, end) + `
import {ref as qaRef} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/l12/mobileViewport.css'
const qaOwner=Number(params.get('owner')||0)
const qaTarget=l12State.game.players[qaOwner].field[0][0]
qaTarget.name='月读状态合成军团'
qaTarget.isMasterLegion=false
qaTarget.cardType='legion'
qaTarget.troops=3000
qaTarget.baseTroops=3000
qaTarget.identityKnown=false
window.__moonStatusTarget=qaTarget.instanceId
window.__moonStatusSet=(count,hidden=false,remove=false)=>{
 const old=l12State.game.players[qaOwner].field[0][0]
 l12State.game.players[qaOwner].field[0][0]=remove?null:{...old,hidden,
  statusEffects:count?[{kind:'conditional-attack',label:'本回合每次进攻兵力＋'+count*1000,source:'月读'}]:[],
  statusIcons:count?['conditional-attack']:[]}
 l12State.game.revision++
}
window.__moonStatusSet(1)
window.__moonStatusState=l12State
const app=createApp({setup(){useLandscapeViewport(qaRef(true));return()=>h(GamePage)}})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`
  const html = '<meta name="viewport" content="width=device-width,initial-scale=1">'
    + '<div id="l12-landscape-teleports"></div><div id="app"></div>'
    + '<script type="module" src="/__moon_status_fixture.js"></script>'
  let server, browser
  const results = []
  const report = { status: 'started', results, syntheticProjectionOnly: true,
    realServer: false, physicalDevice: false, backendEvidenceSeparate: true }
  try {
    server = await createServer({ root: frontend, configLoader: 'runner', cacheDir: cache, logLevel: 'error',
      server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'moon-status-real-consumer',
        resolveId(id) { if (id === '/__moon_status_fixture.js') return id },
        load(id) { if (id === '/__moon_status_fixture.js') return entry },
        configureServer(vite) { vite.middlewares.use((request, response, next) => {
          if (!request.url?.startsWith('/__moon_status__')) return next()
          response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(html)
        }) },
      }] })
    await server.listen()
    const port = server.httpServer.address().port
    browser = await chromium.launch({ headless: true, channel: 'msedge' })
    for (const viewport of [{ width: 1366, height: 768 }, { width: 844, height: 390 }])
    for (const mode of [{ owner: 0 }, { owner: 1 }, { owner: 0, spectator: true }]) {
      const mobile = viewport.width === 844
      const name = `${viewport.width}-${mode.spectator ? 'spectator' : 'player'}-owner${mode.owner}`
      const context = await browser.newContext({ viewport, hasTouch: mobile })
      const page = await context.newPage()
      const errors = []
      page.on('pageerror', error => errors.push(error.message))
      page.on('console', message => { if (message.type() === 'error') errors.push(message.text()) })
      await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
        ? route.continue() : route.abort())
      const result = { name, passed: false, errors }
      results.push(result)
      try {
        await page.goto(`http://127.0.0.1:${port}/__moon_status__?owner=${mode.owner}${mode.spectator ? '&spectator=1' : ''}`)
        await page.waitForFunction(() => Boolean(window.__moonStatusTarget))
        const targetId = await page.evaluate(() => window.__moonStatusTarget)
        const target = page.locator(`[data-l12-game-stage] [data-card-instance-id="${targetId}"]`).first()
        await target.waitFor()
        const icon = target.locator('.card-status-icon.status-conditional-attack')
        await icon.waitFor({ state: 'visible' })
        assert.equal(await icon.count(), 1)
        assert.equal(await icon.getAttribute('title'), '本回合每次进攻兵力＋1000（来源：月读）')
        const markerBox = await icon.boundingBox(), cardBox = await target.boundingBox()
        assert.ok(markerBox && cardBox && markerBox.x >= cardBox.x && markerBox.y >= cardBox.y
          && markerBox.x + markerBox.width <= cardBox.x + cardBox.width + 1
          && markerBox.y + markerBox.height <= cardBox.y + cardBox.height + 1,
          'the marker must remain inside its actual scaled card on both viewports')
        result.markerBox = markerBox
        if (mode.owner === 0 && !mode.spectator) {
          await target.screenshot({ path: path.join(output, `${name}-card.png`) })
          await page.screenshot({ path: path.join(output, `${name}-board.png`) })
        }
        if (mobile) {
          await target.tap()
          await page.getByRole('button', { name: '展开卡牌详情', exact: true }).click()
        } else await target.hover()
        const status = page.locator(mobile ? '.mobile-card-inspector .battle-card-status' : '.left-rail .battle-card-status')
        await status.getByText('本回合每次进攻兵力＋1000（来源：月读）。', { exact: true }).waitFor()
        await page.evaluate(() => window.__moonStatusSet(2))
        await page.waitForFunction(id => document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${id}"] .status-conditional-attack`)?.getAttribute('title') === '本回合每次进攻兵力＋2000（来源：月读）', targetId)
        assert.equal(await icon.count(), 1)
        await status.getByText('本回合每次进攻兵力＋2000（来源：月读）。', { exact: true }).waitFor()
        assert.equal(await status.getByText('本回合每次进攻兵力＋1000（来源：月读）。', { exact: true }).count(), 0)
        if (mode.owner === 0 && !mode.spectator)
          await page.screenshot({ path: path.join(output, `${name}.png`) })
        await page.evaluate(() => window.__moonStatusSet(0))
        await icon.waitFor({ state: 'hidden' })
        await page.waitForFunction(() => ![...document.querySelectorAll('.battle-card-status')]
          .some(node => node.textContent.includes('来源：月读')))
        await page.evaluate(() => window.__moonStatusSet(2, true))
        assert.equal(await icon.count(), 0)
        assert.equal(await status.getByText(/来源：月读/).count(), 0)
        await page.evaluate(() => window.__moonStatusSet(2, false, true))
        assert.equal(await target.count(), 0)
        assert.equal(await status.getByText(/来源：月读/).count(), 0)
        result.commands = await page.evaluate(() => window.__sentCommands)
        assert.deepEqual(result.commands, [], 'viewing status must send no gameplay command')
        assert.deepEqual(errors, [])
        result.passed = true
      } finally { await context.close() }
    }
    report.status = 'passed'
  } catch (error) {
    report.status = 'failed'; report.error = String(error?.stack ?? error); throw error
  } finally {
    await browser?.close()
    await server?.close()
    report.browserClosed = !browser?.isConnected()
    report.serverClosed = !server?.httpServer?.listening
    fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n')
  }
  console.log(`Conditional attack status real GamePage: ${results.length} cases passed.`)
}
