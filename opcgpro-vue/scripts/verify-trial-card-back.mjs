import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_TRIAL_BACK_OUT || path.join(root, 'artifacts', 'trial-card-back')
assert.ok(!fs.existsSync(output), 'Use a fresh evidence directory; never erase previous results')
fs.mkdirSync(output, { recursive:true })

const entry = `
import { createApp, reactive } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'

const trial = (playerIndex) => playerIndex === 0 ? {
  instanceId:'self-hidden-trial', cardId:'S02-06S6', name:'十字军东征', cardType:'trial',
  faction:'otherworld', cost:0, baseTroops:0, troops:0, disasterLevel:0, tapped:false,
  summonRound:0, hidden:true, identityKnown:true, trialProgress:5, trialCompleted:false,
} : {
  instanceId:'opponent-hidden-trial', cardId:'hidden-card', name:'未知试炼', cardType:'trial',
  faction:'otherworld', cost:0, baseTroops:0, troops:0, disasterLevel:0, tapped:false,
  summonRound:0, hidden:true, identityKnown:false, trialProgress:5, trialCompleted:false,
}
const player = playerIndex => ({
  playerIndex, name:playerIndex ? '对手' : '我方', deckName:'', faction:'otherworld',
  master:{ masterId:playerIndex ? 'S02-06M1' : 'S02-06M2', masterName:playerIndex ? '莫瑞甘' : '安格斯·麦·奥格', hp:8, maxHp:8 },
  libraryCount:20, hand:[], handCount:0, morale:[], field:[[null,null,null],[null,null,null]],
  graveyard:[], graveyardCount:0, resolving:[], mulliganDone:true,
  specialZones:{ runes:0, trialLevel:5, trialCapacity:2, godPower:[], trials:[trial(playerIndex)] },
})
const game = reactive({
  matchId:'trial-back-verification', roomCode:'TRIAL', you:0, revision:1,
  activePlayer:0, firstPlayer:0, diceWinner:0, initiativeRolls:[4,2], phase:'Main', round:2, turnSerial:2,
  disasterMode:'none', disasterValue:0, prompts:[], effectStack:[], players:[player(0),player(1)],
  recentEvents:[], legalAttackTargets:{}, stateHash:'trial-1',
})
createApp(GameBoard,{ game, readOnly:true }).mount('#app')
`

const html = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__trial_back_entry.js"></script></body></html>'
const plugin = {
  name:'trial-card-back-verification',
  resolveId(id) { if (id === '/__trial_back_entry.js') return id },
  load(id) { if (id === '/__trial_back_entry.js') return entry },
  configureServer(server) {
    server.middlewares.use('/__trial-card-back', (_request, response) => {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(html)
    })
  },
}

const profiles = [
  { name:'desktop', viewport:{ width:1366, height:768 }, mobile:false },
  { name:'desktop-wide', viewport:{ width:1920, height:1080 }, mobile:false },
  { name:'desktop-ultrawide', viewport:{ width:2560, height:1080 }, mobile:false },
  { name:'mobile-landscape', viewport:{ width:844, height:390 }, mobile:true },
  { name:'mobile-portrait-rotated', viewport:{ width:390, height:844 }, mobile:true },
]
const expectedPath = '/assets/l12/trial-back.png'
const report = { assertions:0, profiles:[], screenshots:[], errors:[] }
const ok = (condition, message) => { assert.ok(condition, message); report.assertions++ }
const imagePath = async locator => new URL(await locator.getAttribute('src'), 'http://127.0.0.1').pathname

const server = await createServer({ root, plugins:[plugin], server:{ host:'127.0.0.1', port:0 }, logLevel:'error' })
let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ channel:'msedge', headless:true })
  for (const profile of profiles) {
    const context = await browser.newContext({ viewport:profile.viewport, isMobile:profile.mobile, hasTouch:profile.mobile })
    const page = await context.newPage()
    page.on('pageerror', error => report.errors.push(`${profile.name}: ${error.message}`))
    await page.goto(`http://127.0.0.1:${port}/__trial-card-back`, { waitUntil:'networkidle' })
    await page.waitForSelector('[data-l12-game-stage]')

    if (!profile.mobile) {
      const self = page.locator('.side-my .trial-card')
      const opponent = page.locator('.side-opponent .trial-card')
      ok(await imagePath(self.locator('img')) === expectedPath, 'desktop self hidden trial must use dedicated trial back')
      ok(await imagePath(opponent.locator('img')) === expectedPath, 'desktop opponent hidden trial must use dedicated trial back')
      ok(await self.locator('.trial-progress').textContent() === '5', 'desktop self progress must remain visible')
      ok(await opponent.locator('.trial-progress').textContent() === '5', 'desktop opponent progress must remain visible without identity')
      ok(await self.getAttribute('title') === '十字军东征', 'desktop self hidden trial keeps its inspectable identity')
      ok(await opponent.getAttribute('title') === '对方未揭示的试炼', 'desktop opponent hidden trial title must not leak identity')
      await self.click()
      await page.waitForFunction(() => document.querySelector('[data-ui-contract="selected-card-inspector"]')?.textContent?.includes('十字军东征'))
      ok(true, 'desktop self hidden trial remains inspectable')
      await opponent.click({ force:true })
      ok(await page.locator('[data-ui-contract="selected-card-inspector"]').textContent().then(text => text.includes('十字军东征') && !text.includes('未知试炼')),
        'desktop opponent hidden trial remains non-inspectable')
    } else {
      const self = page.locator('.mobile-extra-zone-my .mobile-extra-card')
      const opponent = page.locator('.mobile-extra-zone-opponent .mobile-extra-card')
      await self.waitFor({ state:'attached' })
      ok(await imagePath(self.locator('img')) === expectedPath, `${profile.name} self hidden trial must use dedicated trial back`)
      ok(await imagePath(opponent.locator('img')) === expectedPath, `${profile.name} opponent hidden trial must use dedicated trial back`)
      ok(await self.locator('b').textContent() === '5', `${profile.name} self progress must remain visible`)
      ok(await opponent.locator('b').textContent() === '5', `${profile.name} opponent progress must remain visible without identity`)
      ok(!await self.isDisabled(), `${profile.name} self hidden trial remains inspectable`)
      ok(await opponent.isDisabled(), `${profile.name} opponent hidden trial remains non-inspectable`)
      ok(!await opponent.textContent().then(text => text.includes('十字军东征')), `${profile.name} opponent hidden trial must not leak identity`)
      await self.click()
      await page.locator('.mobile-card-inspector-handle-global').click()
      await page.waitForFunction(() => document.querySelector('.mobile-card-inspector h2')?.textContent?.includes('十字军东征'))
      ok(true, `${profile.name} actual self click opens the correct trial detail`)
      await page.locator('.mobile-card-inspector header button').click()
    }

    const trialImages = page.locator(profile.mobile ? '.mobile-extra-card img.trial-card-back' : '.trial-card img.trial-card-back')
    ok(await trialImages.evaluateAll(images => images.length === 2 && images.every(image => image.complete
      && image.naturalWidth === 1752 && image.naturalHeight === 1255)), `${profile.name} both dedicated images must decode successfully`)

    const screenshot = path.join(output, `${profile.name}.png`)
    await page.screenshot({ path:screenshot, fullPage:true })
    report.screenshots.push(screenshot)
    report.profiles.push(profile.name)
    await context.close()
  }
  assert.deepEqual(report.errors, [])
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(`Trial card-back verification passed: ${report.assertions} assertions across ${report.profiles.length} profiles`)
} catch (error) {
  report.errors.push(String(error))
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  throw error
} finally {
  await browser?.close()
  await server.close()
}
