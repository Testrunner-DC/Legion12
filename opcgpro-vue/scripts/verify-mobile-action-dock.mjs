import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5197/__l12_battle_preview__'
const out = path.resolve(process.env.L12_DOCK_OUT || '../artifacts/mobile-action-dock/acceptance')
fs.mkdirSync(out, { recursive: true })
const manifest = { target, status: 'running', cases: [], screenshots: [], errors: [] }
const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE ? { headless: true, executablePath: process.env.L12_CHROMIUM_EXECUTABLE } : { headless: true })
const page = await browser.newPage()
const cdp = await page.context().newCDPSession(page)
page.setDefaultTimeout(7000)
page.on('pageerror', error => manifest.errors.push(error.message))
await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())
const zero = { top: 0, right: 0, bottom: 0, left: 0 }
let label = '', insets = zero
async function load(size, query = '', safe = zero) {
  insets = safe
  await cdp.send('Emulation.setPageScaleFactor', { pageScaleFactor: 1 })
  await page.setViewportSize({ width: size[0], height: size[1] })
  await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets: safe })
  await page.goto(`${target}?canvas=1&mobile=1&hand=10&rankedClock=1&${query}`, { waitUntil: 'domcontentloaded' })
  await page.locator('.mobile-battle-dock').waitFor()
  await page.waitForTimeout(80)
}
async function shot(name) {
  const file = `${label}-${name}.png`
  await page.screenshot({ path: path.join(out, file) })
  manifest.screenshots.push(file)
}
async function geometry(name) {
  const report = await page.evaluate(() => {
    const root = document.documentElement, host = document.querySelector('#l12-landscape-teleports')
    const r = e => { const b = e.getBoundingClientRect(); return { left: b.left, top: b.top, right: b.right, bottom: b.bottom } }
    const safe = r(host), dock = document.querySelector('.mobile-battle-dock'), box = r(dock)
    const utilityLane = document.querySelector('.mobile-battle-dock__utility'), utilityBox = r(utilityLane)
    const leftRail = r(document.querySelector('.left-rail')), rightRail = r(document.querySelector('.right-rail'))
    const master = r(document.querySelector('.my-half .mini-master'))
    const relic = r(document.querySelector('.my-half .relic-zone'))
    const pile = r(document.querySelector('.my-half .mat-piles .pile'))
    const handCount = r(document.querySelector('.my-half .mobile-hand-count'))
    const marker = document.querySelector('.my-half .master-marker-track :is(.rune-orb,.canopic-orb)')
    const outside = b => b.left < safe.left - 1 || b.top < safe.top - 1 || b.right > safe.right + 1 || b.bottom > safe.bottom + 1
    const overlap = (a, b) => Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1
    const lanes = [...dock.children].map(r)
    return {
      safe, box, utilityBox, outside: outside(box) || outside(utilityBox), overflow: root.scrollWidth > innerWidth + 1 || root.scrollHeight > innerHeight + 1,
      laneOverlap: lanes.some((a, i) => lanes.slice(i + 1).some(b => overlap(a, b))),
      fieldOverlap: [...document.querySelectorAll('.felt-board,.board-center>.l12-hand:last-child,.left-rail')].some(e => overlap(box, r(e))),
      utility: utilityLane.querySelectorAll('.battle-utility-dock button').length,
      utilityInLeftRail: utilityBox.left >= leftRail.left - 1 && utilityBox.right <= leftRail.right + 1 && Math.abs(utilityBox.bottom - (safe.bottom - 4)) <= 1,
      leftRatio: leftRail.right - leftRail.left > 0 ? (leftRail.right - leftRail.left) / (master.right - master.left) : 0,
      rightRatio: rightRail.right - rightRail.left > 0 ? (rightRail.right - rightRail.left) / (master.right - master.left) : 0,
      relicRatio: (relic.right - relic.left) / (master.right - master.left),
      pileRatio: (pile.right - pile.left) / (master.right - master.left),
      handCountRatio: (handCount.right - handCount.left) / (master.right - master.left),
      markerRatio: marker ? (r(marker).right - r(marker).left) / (master.right - master.left) : null,
      identityExpanded: dock.querySelectorAll('.player-summary').length === 2 && [...dock.querySelectorAll('.player-summary')].every(e => getComputedStyle(e).display !== 'none'),
      identityToggleCount: [...dock.querySelectorAll('button')].filter(e => e.textContent.trim() === '双方信息').length,
      currentActionCount: dock.querySelectorAll('.card-context-actions button').length,
      lanes: [...dock.children].map(e => ({ class: e.className, height: e.clientHeight, scrollHeight: e.scrollHeight })),
    }
  })
  assert.equal(report.outside, false, `${label}/${name}: dock outside ${JSON.stringify(report)}`)
  assert.equal(report.overflow, false, `${label}/${name}: page scroll`)
  assert.equal(report.laneOverlap, false, `${label}/${name}: lane collision`)
  assert.equal(report.fieldOverlap, false, `${label}/${name}: battlefield/hand collision`)
  assert.equal(report.utility, 3, `${label}/${name}: missing utility segment`)
  assert.equal(report.utilityInLeftRail, true, `${label}/${name}: utility is not at left rail bottom ${JSON.stringify(report)}`)
  assert.ok(report.leftRatio >= 1.3 - .02, `${label}/${name}: left rail below 130% of master ${JSON.stringify(report)}`)
  assert.ok(report.rightRatio > 1.08, `${label}/${name}: right rail is not wider than master ${JSON.stringify(report)}`)
  assert.ok(Math.abs(report.relicRatio - .9) <= .035, `${label}/${name}: relic/master ratio drift ${JSON.stringify(report)}`)
  assert.ok(Math.abs(report.pileRatio - .7) <= .035, `${label}/${name}: pile/master ratio drift ${JSON.stringify(report)}`)
  assert.ok(report.handCountRatio >= .75, `${label}/${name}: hand count did not scale with master ${JSON.stringify(report)}`)
  if (report.markerRatio !== null) assert.ok(report.markerRatio >= .3, `${label}/${name}: master marker did not scale ${JSON.stringify(report)}`)
  assert.equal(report.identityExpanded, true, `${label}/${name}: player identities collapsed`)
  assert.equal(report.identityToggleCount, 0, `${label}/${name}: player identity collapse control remained`)
  manifest.cases.push({ label, name, ...report })
}
// Scroll each real action into its own lane, then check hit testing at centre
// and four inset corners. Trial clicks use the browser's actionability checks.
async function reachable(locator) {
  const count = await locator.count()
  for (let i = 0; i < count; i++) {
    const item = locator.nth(i)
    if (!await item.isVisible()) continue
    await item.scrollIntoViewIfNeeded()
    const hit = await item.evaluate(e => {
      const r = e.getBoundingClientRect(), points = [[.5,.5],[.2,.2],[.8,.2],[.2,.8],[.8,.8]]
      return { text: e.getAttribute('aria-label') || e.textContent, rect: r.toJSON(),
        ok: points.every(([x,y]) => { const hit = document.elementFromPoint(r.left+r.width*x,r.top+r.height*y); return hit === e || e.contains(hit) }),
        textFits: !e.textContent.trim() || (e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1) }
    })
    assert.equal(hit.ok, true, `${label}: intercepted ${JSON.stringify(hit)}`)
    assert.equal(hit.textFits, true, `${label}: clipped control ${JSON.stringify(hit)}`)
    if (await item.isEnabled()) await item.click({ trial: true })
  }
}
async function commandIs(type) { assert.equal(await page.evaluate(() => window.__sentCommands.at(-1)?.command?.type), type) }
async function selectedFixture() {
  await page.locator('.my-half .formation-slot .card-tile').first().click()
  assert.equal(await page.locator('.mobile-card-inspector').count(), 0, 'selection opened inspector')
}
async function handSelectionVisible() {
  const report = await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap.selected').evaluate(element => {
    const rect = element.getBoundingClientRect()
    const hand = element.closest('.l12-hand').getBoundingClientRect()
    const felt = document.querySelector('.felt-board').getBoundingClientRect()
    const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + 2)
    return { rect:rect.toJSON(), hand:hand.toJSON(), felt:felt.toJSON(), hit:Boolean(hit && (hit === element || element.contains(hit))) }
  })
  assert.ok(report.rect.top >= report.hand.top + 2, `${label}: selected hand card clipped at top ${JSON.stringify(report)}`)
  assert.ok(report.rect.bottom <= report.hand.bottom + 1, `${label}: selected hand card clipped at bottom ${JSON.stringify(report)}`)
  assert.ok(report.felt.bottom <= report.hand.top + 1, `${label}: battlefield covers hand lane ${JSON.stringify(report)}`)
  assert.equal(report.hit, true, `${label}: selected hand card is covered ${JSON.stringify(report)}`)
}
try {
  const sizes = [[844,390],[780,360],[740,360],[915,412],[932,430],[1024,600],[1024,768],[800,600],[667,320]]
  for (const size of sizes) {
    label = size.join('x')
    await load(size, 'field=full&action-fixture=1')
    await geometry('minimum')
    await reachable(page.locator('.formation-slot,.mini-master,.pile,.resource-morale-summary,.resource-faction-action,.session-disaster-strip button'))
    await reachable(page.locator('.mobile-battle-dock button'))
    await shot('minimum')
    await page.locator('.mobile-battle-dock__tools').evaluate(e => e.scrollTop = 0)
    await selectedFixture()
    await geometry('selection')
    await reachable(page.locator('.card-context-actions button'))
    await page.getByRole('button', { name: '展开卡牌详情', exact: true }).click()
    await reachable(page.locator('.card-context-actions button'))
    await shot('detail-open')
    await page.getByRole('button', { name: '收起详情', exact: true }).click()
    await page.locator('.card-context-actions').getByRole('button', { name: '进攻', exact: true }).click()
    await page.locator('.opponent-half .formation-slot.targetable').first().click()
    await commandIs('attack')
    await load(size, 'field=full&action-fixture=1')
    await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap.playable').first().click()
    await handSelectionVisible()
    const actionOrder = await page.evaluate(() => {
      const action = document.querySelector('.mobile-battle-dock__context .card-context-actions')?.getBoundingClientRect()
      const primary = document.querySelector('.mobile-battle-dock__primary')?.getBoundingClientRect()
      return action && primary ? { gap: primary.top - action.bottom } : null
    })
    assert.ok(actionOrder && actionOrder.gap >= -1 && actionOrder.gap <= 5, `${label}: card actions are not directly above end turn ${JSON.stringify(actionOrder)}`)
    await page.locator('.card-context-actions').getByRole('button', { name: '打出', exact: true }).click()
    await page.locator('.my-half .formation-slot.available').first().click()
    await commandIs('playCard')
    await load(size, 'field=full&action-fixture=1&trials=1&myTrials=1')
    await page.evaluate(() => {
      const p = window.__battleDockFixture.state.game.players[0], card = p.field[0][0]
      p.field[1][0] = null
      p.field[1][1].cardId = 'S02-0510'; p.field[1][1].tapped = true
      card.profession = '骑兵'; card.abilities.push({ id:'trialAdvance', label:'试炼', enabled:true })
      card.ruleActions = [{ id:'cavalryMove', label:'骑兵位移：选择任意合法空格位继续当前操作', enabled:true, targetKeys:['1:0','1:2'] }]
    })
    await selectedFixture()
    assert.ok(await page.locator('.card-context-actions button').count() >= 5, 'missing maximum real actions')
    await reachable(page.locator('.card-context-actions button'))
    await geometry('maximum-real-actions')
    await shot('maximum-actions')
    await page.locator('.card-context-actions').getByRole('button', { name: '发动', exact:true }).click()
    await commandIs('activateAbility')
    await load(size, 'field=full&card-choice=1&choice-count=12')
    await page.locator('.prompt-card-candidate').first().click()
    assert.equal(await page.locator('.mobile-card-inspector').count(), 0)
    await page.locator('.prompt-minimize').click()
    await geometry('prompt-minimized')
    await reachable(page.locator('.mobile-battle-dock button'))
    await shot('prompt-minimized')
    await page.locator('.prompt-minimized-bar button').click()
    assert.equal(await page.locator('.prompt-card-candidate.selected').count(), 1)
    await reachable(page.locator('.prompt-panel button'))
    await shot('prompt-restored')
    await load(size, 'field=full&board-target=1')
    await page.locator('.formation-slot.targetable').first().click()
    await reachable(page.locator('.board-target-controls button'))
    await geometry('board-target')
    await shot('board-target')
    await page.locator('.board-target-controls .primary').click()
    await commandIs('resolvePrompt')
    await load(size, 'field=full&runes=5&morale-payment=1&rune-usable=2')
    await page.locator('.resource-morale-summary').last().click()
    const choices = page.locator('.mobile-rune-row .mobile-morale-choice[aria-disabled="false"]')
    await choices.nth(0).click(); await choices.nth(1).click()
    await reachable(page.locator('.mobile-morale-overlay button'))
    await shot('rune-payment')
    await page.locator('.mobile-morale-overlay').getByRole('button',{name:'最小化',exact:true}).click()
    await geometry('payment-minimized')
    await reachable(page.locator('.mobile-battle-dock__context button'))
    await page.locator('.mobile-morale-restore').click()
    assert.equal(await page.locator('.mobile-rune-row .selected').count(), 2)
    await page.locator('.mobile-morale-overlay').getByRole('button',{name:'确认支付',exact:true}).click()
    await commandIs('resolvePrompt')
    for (const kind of ['support','defense']) {
      await load(size, `field=full&${kind}=1`)
      await geometry(kind)
      await reachable(page.locator('.combat-resolution-panel button'))
      await shot(kind)
      await page.locator('.combat-decision-minimize').click()
      await page.locator('.combat-decision-restore').click()
      await page.locator('.combat-resolution-panel .danger').click()
      await commandIs('resolveDefense')
    }
    console.log(`${label} interactions passed`)
  }
  for (const safe of [{top:0,right:0,bottom:21,left:59},{top:0,right:59,bottom:21,left:0},{top:8,right:44,bottom:21,left:44}]) {
    label = `safe-${safe.left}-${safe.right}`
    await load([844,390], 'field=full&action-fixture=1', safe)
    await selectedFixture(); await geometry('safe-area'); await reachable(page.locator('.mobile-battle-dock button')); await shot('selected')
  }
  // Resize without reload: state, target registration and focus must survive.
  label = 'continuous'
  await load([844,390], 'field=full&action-fixture=1')
  await selectedFixture()
  let seed = 72497
  for (let i=0;i<48;i++) {
    seed = (seed*1664525+1013904223)>>>0
    const width = i<12 ? 735+i : 640+seed%430
    seed = (seed*1664525+1013904223)>>>0
    const height = i<12 ? 355+i : Math.min(300+seed%310, Math.floor(width/1.26))
    await page.setViewportSize({width,height}); await page.waitForTimeout(35)
    await geometry(`size-${width}-${height}`)
    await reachable(page.locator('.card-context-actions button,.battle-utility-dock button,.action-panel button'))
  }
  await page.setViewportSize({width:932,height:430})
  for (const scale of [1.05,1.15,1.25,1]) {
    await cdp.send('Emulation.setPageScaleFactor',{pageScaleFactor:scale}); await page.waitForTimeout(80)
    await geometry(`visualViewport-scale-${scale}`)
    await reachable(page.locator('.card-context-actions button,.battle-utility-dock button'))
    await shot(`scale-${scale}`)
  }
  assert.deepEqual(manifest.errors, [])
  manifest.status='passed'
} catch (error) {
  manifest.errors.push(error.stack); await shot('failure'); throw error
} finally {
  fs.writeFileSync(path.join(out,'manifest.json'),JSON.stringify(manifest,null,2))
  await browser.close()
}
