import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = path.resolve(root, '../artifacts/stage5-player-understanding')
const fixturePath = path.join(output, 'authority.json')
assert(fs.existsSync(fixturePath), `Run the Stage5 authoritative fixture test first: ${fixturePath}`)
const fixture = JSON.parse(fs.readFileSync(fixturePath, 'utf8'))
assert.equal(fixture.schema, 1)
const repoRoot = path.resolve(root, '..')
const commit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim()
const probe = path.join(root, 'scripts/probe-battle-log-qianyang.ps1')
const assemblyDirectory = path.join(repoRoot, 'TwelveLegions.Tests/bin/Release/net10.0')
const realLog = scenario => JSON.parse(execFileSync('pwsh', ['-NoProfile', '-File', probe,
  '-Scenario', scenario, '-Configuration', 'Release', '-AssemblyDirectory', assemblyDirectory,
  '-ExpectedCommit', commit], { cwd: repoRoot, encoding: 'utf8' }))
const multi = realLog('empty-draw')
const hidden = realLog('hidden-draw')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')

const snapshots = Object.fromEntries([
  'mulligan', 'firstPriority', 'actor', 'responder', 'spectator',
  'referee', 'negatedActor', 'negatedOpponent', 'recovered', 'invalidated', 'covered',
].map(name => [name, fixture[name]]))
const recordTrajectories = { multiOwner: multi.owner, multiOpponent: multi.opponent,
  multiSpectator: multi.spectator, hiddenSpectator: hidden.spectator }
const entry = `
import { createApp, h, ref } from 'vue'
import { createRouter, createMemoryHistory, RouterView } from 'vue-router'
import GamePage from '/src/l12/GamePage.vue'
import BattleEventLog from '/src/l12/game/BattleEventLog.vue'
import { l12State } from '/src/l12/net.ts'
import { useLandscapeViewport } from '/src/l12/mobileViewport.ts'
import { replayGameAt } from '/src/l12/replayModel.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
const cases = ${JSON.stringify(snapshots)}
const records = ${JSON.stringify(recordTrajectories)}
const params = new URLSearchParams(location.search)
const scene = params.get('scene') || 'responder'
const baseScene = scene === 'timedResponder' ? 'responder' : scene === 'timedNegated' ? 'negatedActor' : scene
const recordOnly = Object.hasOwn(records, scene)
if (!recordOnly && !cases[baseScene] && scene !== 'replay' && scene !== 'legacyReplay')
  throw new Error('Unknown Stage5 scene: ' + scene)
const view = structuredClone(cases[scene === 'replay' || scene === 'legacyReplay' ? 'recovered' : baseScene] || cases.actor)
const spectator = scene === 'spectator' || scene === 'referee' || scene === 'multiSpectator' || scene === 'hiddenSpectator'
if (spectator) view.you = 0 // Visual anchor only; the snapshot was already recipient-projected.
if (scene === 'replay' || scene === 'legacyReplay') {
  const historical = structuredClone(view.recentEvents)
  if (scene === 'legacyReplay') for (const event of historical) delete event.playerSelectedTargets
  const replay = replayGameAt({ match: { matchId: view.matchId, roomCode: view.roomCode },
    viewerPlayerIndex: 1, commands: [{ revision: view.revision,
      state: { ...view, Events: historical, Players: view.players } }] }, 0)
  if (!replay) throw new Error('Stage5 replay could not restore')
  records[scene] = replay.recentEvents
}
if (recordOnly || scene === 'replay' || scene === 'legacyReplay') {
  const events = records[scene]
  const you = scene === 'multiOpponent' || scene === 'replay' || scene === 'legacyReplay' ? 1 : 0
  const neutral = spectator
  createApp({ render: () => h('main', { class: 'stage5-record-page' }, [
    h('h1', '对局记录'), h(BattleEventLog, { events, you, names: ['测试玩家甲', '测试玩家乙'], neutralView: neutral }),
  ]) }).mount('#app')
} else {
  l12State.game = view
  l12State.status = 'online'
  if (scene.startsWith('timed')) l12State.rankedClock = {
    serverUtcMs: Date.now(), receivedAtMs: Date.now(), totalLimitMs: 1800000,
    operationLimitMs: 60000, reconnectLimitMs: 120000,
    players: [0, 1].map(playerIndex => ({ playerIndex, totalRemainingMs: 1200000,
      operationRemainingMs: 45000, acting: playerIndex === view.activePlayer, connected: true })),
  }
  l12State.spectating = spectator
  l12State.observerView = scene === 'referee' ? 'referee' : 'public'
  l12State.room = { roomCode: view.roomCode, yourPlayerIndex: view.you, players:
    view.players.map(player => ({ playerIndex: player.playerIndex, displayName: player.name,
      connected: true })) }
const router = createRouter({ history: createMemoryHistory(), routes: [
  { path: '/live', component: GamePage }, { path: '/lobby', component: { render: () => h('p', '大厅') } },
] })
const app = createApp({ setup() { useLandscapeViewport(ref(true)); return () => h('div', null,
  [h('div', { id: 'l12-landscape-teleports' }), h(RouterView)]) } })
app.use(router)
await router.push('/live')
await router.isReady()
app.mount('#app')
}
window.__stage5 = { scene, view, spectator }
`

const server = await createServer({ root, configLoader: 'runner',
  server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'stage5-player-understanding',
    resolveId(id) { if (id === '/__stage5__.js') return id },
    load(id) { if (id === '/__stage5__.js') return entry },
    configureServer(dev) { dev.middlewares.use((request, response, next) => {
      if (!request.url?.startsWith('/__stage5__?')) return next()
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__stage5__.js"></script>')
    }) },
  }] })
await server.listen()
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
const desktop = { width: 1440, height: 900 }
const phones = [{ width: 430, height: 932 }, { width: 390, height: 844 },
  { width: 320, height: 568 }, { width: 568, height: 320 }, { width: 667, height: 375 }]
try {
  for (const [scene, viewport] of [
    ...Object.keys(snapshots).map(scene => [scene, desktop]),
    ...Object.keys(recordTrajectories).map(scene => [scene, desktop]),
    ['replay', desktop], ['legacyReplay', desktop],
    ...phones.flatMap(viewport => ['responder', 'mulligan', 'negatedActor', 'multiOwner',
      'timedResponder', 'timedNegated']
      .map(scene => [scene, viewport])),
  ]) {
    if (process.argv[2] && process.argv[2] !== `${scene}-${viewport.width}x${viewport.height}`) continue
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    try {
      await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__stage5__?scene=${scene}`)
      await page.waitForFunction(() => document.body.innerText.includes('对局记录')
        || document.body.innerText.includes('返回大厅'))
      if (viewport.width < 761 && ['negatedActor', 'negatedOpponent', 'invalidated', 'timedNegated'].includes(scene))
        await page.locator('.mobile-record-trigger').click()
      const body = await page.locator('body').innerText()
      const readGeometry = () => page.evaluate(() => {
        const selector = '.l12-prompt-overlay:not(.minimized) .prompt-panel, .l12-prompt-overlay:not(.minimized) .waiting-panel, .battle-route-controls, .mobile-battle-dock, .mobile-timed-clocks, .mobile-record-overlay'
        return [...document.querySelectorAll(selector)].filter(element => {
          const style = getComputedStyle(element)
          return style.display !== 'none' && style.visibility !== 'hidden' && element.getBoundingClientRect().width > 0
        }).map(element => {
          const rect = element.getBoundingClientRect()
          return { element: element.className, left: rect.left, right: rect.right,
            top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height }
        })
      })
      const initialGeometry = await readGeometry()
      const routeHitTargets = await page.evaluate(() => [...document.querySelectorAll('.battle-route-controls button')]
        .filter(button => getComputedStyle(button).display !== 'none' && button.getBoundingClientRect().width > 0)
        .map(button => {
          const rect = button.getBoundingClientRect()
          const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
          return { label: button.textContent?.trim(), hittable: hit === button || button.contains(hit) }
        }))
      if (viewport.width < 761) assert(routeHitTargets.every(target => target.hittable),
        `${scene} ${viewport.width}x${viewport.height}: return/surrender hit target blocked`)
      if (scene === 'responder' || scene === 'recovered' || scene === 'firstPriority' || scene === 'timedResponder') {
        for (const required of [fixture.sourceName, fixture.targetName, '已支付', '是否响应', '不响应'])
          assert(body.includes(required), `${scene}: response decision missing ${required}`)
        assert(body.includes('后排右格'), `${scene}: selected slot absent`)
      }
      if (scene === 'mulligan') for (const required of ['选择要换掉的起始手牌', '已选 0 张', '保留全部手牌'])
        assert(body.includes(required), `${scene}: mulligan decision missing ${required}`)
      if (scene === 'actor') assert(body.includes('对手正在决定是否响应'))
      if (scene === 'negatedActor' || scene === 'negatedOpponent' || scene === 'timedNegated')
        for (const required of ['被无效', '费用已支付'])
          assert(body.includes(required), `${scene}: result missing ${required}`)
      if (scene === 'invalidated') assert(body.includes('未能完成'))
      if (scene.startsWith('multi') || scene === 'hiddenSpectator')
        for (const required of ['1段跳过', '1段完成', '抽取1张牌'])
          assert(body.includes(required), `${scene}: segment record missing ${required}`)
      if (scene === 'spectator' || scene === 'referee')
        assert(!body.includes('投降'), `${scene}: spectator has player-only action`)
      fs.mkdirSync(output, { recursive: true })
      const screenshot = path.join(output, `${scene}-${viewport.width}x${viewport.height}.png`)
      await page.screenshot({ path: screenshot })
      const detailButtons = page.getByRole('button', { name: /查看明细/ })
      const decisionOverlay = await page.locator('.l12-prompt-overlay:not(.minimized):visible').count()
      if (decisionOverlay && await page.getByRole('button', { name: '最小化弹框' }).count())
        await page.getByRole('button', { name: '最小化弹框' }).first().click()
      if (await detailButtons.count()) await detailButtons.first().click()
      const expanded = await page.locator('body').innerText()
      if (['actor', 'spectator', 'referee', 'replay'].includes(scene)
        || ((scene === 'responder' || scene === 'recovered') && viewport.width >= 761))
        assert(expanded.includes(fixture.targetName), `${scene}: selected target absent from decision or record detail: ${expanded.slice(-700)}`)
      if (scene === 'covered') assert(expanded.includes('我方已选目标：对方前排中格盖伏卡牌'),
        'covered target should reveal only position and covered status')
      if (scene === 'legacyReplay') assert(!expanded.includes('已选目标'), 'legacy replay invented target')
      const geometry = await readGeometry()
      for (const item of [...initialGeometry, ...geometry])
        assert(item.left >= -2 && item.top >= -2 && item.right <= viewport.width + 2
          && item.bottom <= viewport.height + 2,
        `${scene} ${viewport.width}x${viewport.height}: ${item.element} outside viewport ${JSON.stringify(item)}`)
      const overlays = initialGeometry.filter(item => /prompt-panel|mobile-record-overlay/.test(item.element))
      const controls = initialGeometry.filter(item => /battle-route-controls|mobile-timed-clocks/.test(item.element))
      const layoutIssues = overlays.flatMap(overlay => controls.flatMap(control => {
        const width = Math.min(overlay.right, control.right) - Math.max(overlay.left, control.left)
        const height = Math.min(overlay.bottom, control.bottom) - Math.max(overlay.top, control.top)
        const requiredGap = overlay.element.includes('prompt-panel') ? 8 : 0
        return (width > 0 && height > -requiredGap)
          || (height > 0 && width > -requiredGap)
          ? [{ overlay: overlay.element, control: control.element,
            horizontalSeparation: Math.round(-width), verticalSeparation: Math.round(-height) }] : []
      }))
      assert.deepEqual(errors, [], `${scene}: browser errors`)
      results.push({ scene, viewport, screenshot, initialGeometry, geometry, routeHitTargets, layoutIssues,
        check: 'real engine recipient snapshot or trace mounted in production Vue component' })
    } finally { await page.close() }
  }
  fs.writeFileSync(path.join(output, 'automated-evidence.json'), JSON.stringify(results, null, 2))
  const issues = results.filter(result => result.layoutIssues.length > 0)
  console.log(`Stage5 real-component information checks: ${results.length} scenes; layout overlaps: ${issues.length}`)
  for (const result of issues)
    console.error(`${result.scene} ${result.viewport.width}x${result.viewport.height}: ${JSON.stringify(result.layoutIssues)}`)
  if (issues.length) process.exitCode = 1
} finally { await browser.close(); await server.close() }
