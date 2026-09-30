import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

const card = (instanceId, name = '无名的渗透者') => ({
  instanceId, cardId: 'S01-0004', name, cardType: 'legion', faction: '测试',
  cost: 1, baseTroops: 3000, troops: 3000, ownerIndex: 0,
  tapped: false, hidden: false,
})
const placement = (instanceId, extra = {}) => ({ instanceId, ownerPlayerIndex: 0,
  controllerPlayerIndex: 1, row: 0, slot: 0, tapped: false, ...extra })
const put = (fact = placement('infiltrator'), cards = [card('infiltrator')]) => ({
  sequence: 1, type: 'put', playerIndex: 1,
  text: '私有来源格与玩家姓名绝不作为展示依据', cards,
  playerPublicPlacement: fact,
})
const words = row => row?.kind === 'line'
  ? `${row.actor ?? ''}${row.parts.map(part => part.text).join('')}` : ''

assert.equal(words(projectLog([put()], 0, [])[0]),
  '我方〈无名的渗透者〉置入对方前排左格，由对方控制（活跃）')
assert.equal(words(projectLog([put()], 1, [])[0]),
  '对方〈无名的渗透者〉置入我方前排左格，由我方控制（活跃）')
assert.equal(words(projectLog([put()], 0, [], true)[0]),
  '下方〈无名的渗透者〉置入上方前排左格，由上方控制（活跃）')

const horse = put(placement('horse', { ownerPlayerIndex: 1, controllerPlayerIndex: 0,
  row: 1, slot: 2, tapped: true, durationCode: 'until-owner-next-turn-end' }),
[{ ...card('horse', '特洛伊木马'), ownerIndex: 1, tapped: true }])
assert.equal(words(projectLog([horse], 0, [])[0]),
  '对方〈特洛伊木马〉置入我方后排右格，由我方控制（休整）；直到对方下个回合结束')
assert.equal(words(projectLog([horse], 1, [])[0]),
  '我方〈特洛伊木马〉置入对方后排右格，由对方控制（休整）；直到我方下个回合结束')
assert(!words(projectLog([horse], 0, [])[0]).includes('来源格'))

for (const invalid of [
  put(placement('missing')),
  put(placement('infiltrator', { ownerPlayerIndex: 2 })),
  put(placement('infiltrator', { controllerPlayerIndex: 0 })),
  put(placement('infiltrator', { row: 2 })),
  put(placement('infiltrator', { slot: 3 })),
  put(placement('infiltrator', { tapped: true })),
  put(placement('infiltrator', { durationCode: 'secret' })),
  put(placement('infiltrator'), [{ ...card('infiltrator'), hidden: true }]),
  put(placement('infiltrator'), [card('infiltrator'), { ...card('infiltrator'), hidden: true }]),
  { ...put(), playerPublicPlacement: undefined },
]) {
  const text = words(projectLog([invalid], 0, [])[0])
  assert(!/前排|后排|控制|来源格|私有/.test(text), `unsafe placement: ${text}`)
}

const tomb = { sequence: 2, type: 'continuous', playerIndex: 0,
  text: '本回合已有999张：不应从审计文本提取', cards: [card('tomb', '陵墓军团')],
  playerLogSemantic: { sourceInstanceId: 'tomb', actionLabel: '离场',
    outcomeLabel: '本回合含〈陵墓〉名称的军团离场累计1张，〈陵墓圣武士〉登场费用相应降低' },
}
assert(words(projectLog([tomb], 0, [])[0]).includes('累计1张'))
assert(!words(projectLog([tomb], 0, [])[0]).includes('999'))
assert(words(projectLog([{ ...tomb, playerLogSemantic: undefined }], 0, [])[0])
  .includes('详情未记录'))

const replay = replayGameAt({
  match: { matchId: 'placement-replay', roomCode: 'PLACE' },
  commands: [{ state: { Events: [{ Sequence: 1, Type: 'put', PlayerIndex: 1,
    Text: '旧审计文本', Cards: [{ InstanceId: 'infiltrator', CardId: 'S01-0004',
      Name: '无名的渗透者', CardType: 'legion', Faction: '测试', OwnerIndex: 0,
      Tapped: false, Hidden: false }],
    PlayerPublicPlacement: { InstanceId: 'infiltrator', OwnerPlayerIndex: 0,
      ControllerPlayerIndex: 1, Row: 0, Slot: 0, Tapped: false },
  }] }, revision: 1 }], viewerPlayerIndex: 0,
}, 0)
assert.equal(words(projectLog(replay?.recentEvents ?? [], 0, [])[0]),
  '我方〈无名的渗透者〉置入对方前排左格，由对方控制（活跃）')
console.log('stage 4B-3A public placement and tomb log projection verified')

if (process.argv.includes('--browser')) {
  const fs = await import('node:fs')
  const path = await import('node:path')
  const { createRequire } = await import('node:module')
  const { fileURLToPath } = await import('node:url')
  const { createServer } = await import('vite')
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
  const require = createRequire(import.meta.url)
  const { chromium } = require(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const output = path.resolve(root, '../artifacts/stage4b3a-placement')
  fs.mkdirSync(output, { recursive: true })
  const previous = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
  const setup = previous.slice(previous.indexOf('const entry = `') + 'const entry = `'.length,
    previous.indexOf('const isPicker='))
  const visibility = fs.readFileSync(path.join(root, 'scripts/verify-referee-admin-visibility.mjs'), 'utf8')
  const entryCode = visibility.slice(visibility.indexOf('const entry = setup +'), visibility.indexOf('const server ='))
  let entry = new Function('setup', `${entryCode}; return entry`)(setup)
  const replayEvents = [
    { Sequence: 21, Type: 'put', PlayerIndex: 1, Text: '绝不可从审计文本显示来源格',
      Cards: [{ InstanceId: 'infiltrator', CardId: 'S01-0004', Name: '无名的渗透者',
        CardType: 'legion', Faction: 'universal', OwnerIndex: 0, Tapped: false, Hidden: false }],
      PlayerPublicPlacement: { InstanceId: 'infiltrator', OwnerPlayerIndex: 0,
        ControllerPlayerIndex: 1, Row: 0, Slot: 0, Tapped: false } },
    { Sequence: 22, Type: 'put', PlayerIndex: 1, Text: '秘密来源格',
      Cards: [{ InstanceId: 'horse', CardId: 'S02-0523', Name: '特洛伊木马',
        CardType: 'legion', Faction: 'universal', OwnerIndex: 1, Tapped: true, Hidden: false }],
      PlayerPublicPlacement: { InstanceId: 'horse', OwnerPlayerIndex: 1,
        ControllerPlayerIndex: 0, Row: 1, Slot: 2, Tapped: true,
        DurationCode: 'until-owner-next-turn-end' } },
  ]
  assert(entry.includes('Events:[]'), 'replay fixture changed')
  entry = entry.replace('Events:[]', `Events:${JSON.stringify(replayEvents)}`)
  entry = entry.replace('const routes=',
    "l12State.gmEnabled=params.has('staleGm');l12State.game.recentEvents=replayGameAt(detail,0).recentEvents;const routes=")
  const server = await createServer({ root, configLoader: 'runner',
    server: { host: '127.0.0.1', port: 0 }, plugins: [{
      name: 'stage4b3a-placement', resolveId(id) { if (id === '/__stage4b3a__.js') return id },
      load(id) { if (id === '/__stage4b3a__.js') return entry },
      configureServer(dev) { dev.middlewares.use((req, res, next) => {
        if (!req.url?.startsWith('/__stage4b3a__?')) return next()
        res.setHeader('Content-Type', 'text/html')
        res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__stage4b3a__.js"></script>')
      }) },
    }] })
  await server.listen()
  const browser = await chromium.launch({ headless: true, channel: 'msedge' })
  try {
    for (const [mode, staleGm, owner, target] of [
      ['public', false, '下方', '上方'], ['referee', false, '下方', '上方'],
      ['admin', false, '下方', '上方'], ['ordinary', false, '我方', '对方'],
      ['ordinary-match', false, '我方', '对方'], ['ordinary', true, '我方', '对方'],
    ]) {
      for (const viewport of [{ width: 1440, height: 900 },
        { width: 390, height: 844 }, { width: 568, height: 320 }]) {
        const page = await browser.newPage({ viewport })
        const errors = []
        page.on('pageerror', error => errors.push(error.message))
        await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
        try {
          await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__stage4b3a__?mode=${mode}${staleGm ? '&staleGm=1' : ''}`)
          if (viewport.width < 600 && ['admin', 'ordinary', 'ordinary-match'].includes(mode)) {
            await page.locator('.replay-mobile-blocked').waitFor()
            assert.deepEqual(errors, [])
            continue
          }
          const trigger = page.locator('.mobile-record-trigger:visible')
          if (await trigger.count()) await trigger.first().click()
          const log = page.locator('.battle-event-log:visible').first()
          await log.waitFor({ timeout: 10000 }).catch(async error => {
            console.error(JSON.stringify({ mode, viewport, errors,
              body: (await page.locator('body').innerText()).slice(0, 1200) }))
            throw error
          })
          const shown = await log.innerText()
          assert(shown.includes(`${owner}〈无名的渗透者〉置入${target}前排左格，由${target}控制（活跃）`),
            `${mode} ${viewport.width}: ${shown}`)
          assert(shown.includes(`${target}〈特洛伊木马〉置入${owner}后排右格，由${owner}控制（休整）；直到${target}下个回合结束`),
            `${mode} ${viewport.width}: ${shown}`)
          assert(!/绝不可|秘密来源格|undefined/.test(shown))
          assert.deepEqual(errors, [])
          const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
          assert(overflow <= 2, `${mode} ${viewport.width}: horizontal overflow ${overflow}`)
          await page.screenshot({ path: path.join(output, `${mode}-${viewport.width}x${viewport.height}.png`) })
        } finally { await page.close() }
      }
    }
    console.log('stage 4B-3A browser roles and three viewport sizes verified')
  } finally { await browser.close(); await server.close() }
}
