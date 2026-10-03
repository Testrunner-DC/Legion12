import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5198/__l12_battle_preview__'
const output = process.env.L12_COMMANDER_CENTER_OUT
assert.ok(output, 'L12_COMMANDER_CENTER_OUT must point to a fresh controlled evidence directory')
assert.ok(path.isAbsolute(output), 'L12_COMMANDER_CENTER_OUT must be absolute')
assert.ok(!fs.existsSync(output), `Refusing to overwrite existing evidence directory: ${output}`)
fs.mkdirSync(output, { recursive: false })

const report = {
  generatedAt: new Date().toISOString(),
  target,
  status: 'running',
  acceptance: {
    vertical: 'abs((master+relic visual union).centerY - six-slot union.centerY) <= 2 logical px',
    horizontal: 'master+relic visual union remains wholly left of the six-slot union',
    clipping: 'master+relic visual union remains within its battlefield half',
    hit: 'master and relic centers remain topmost-hit reachable before intentional overlays open',
  },
  scenarios: [],
  interactions: [],
}

const mobileScenarios = [
  { name: 'landscape-844x390', width: 844, height: 390 },
  { name: 'landscape-740x360', width: 740, height: 360 },
  { name: 'landscape-915x412', width: 915, height: 412 },
  { name: 'landscape-568x320', width: 568, height: 320 },
  { name: 'portrait-390x844', width: 390, height: 844 },
  { name: 'portrait-360x640', width: 360, height: 640 },
  { name: 'portrait-320x568', width: 320, height: 568 },
  { name: 'dynamic-toolbar-844x342', width: 844, height: 342 },
  { name: 'safe-area', width: 844, height: 390, safe: { top: 10, right: 22, bottom: 14, left: 18 } },
  { name: 'empty-relic', width: 844, height: 390, emptyRelic: true },
  { name: 'tapped-two-extra-relics', width: 844, height: 390, tapped: true, extraRelics: 2 },
  { name: 'max-hand-ranked-clock', width: 844, height: 390, hand: 40 },
  { name: 'response-overlay', width: 740, height: 360, query: '&response-fixture=1', allowBoardCovered: true },
  { name: 'payment-overlay', width: 740, height: 360, query: '&morale-payment=1&runes=3&rune-usable=3&payment-actions=1&inline-rich=1' },
  { name: 'target-overlay', width: 740, height: 360, query: '&board-target=1&target-mixed=1&inline-rich=1&inline-long=1' },
]

function round(value) { return Math.round(value * 100) / 100 }
function errorText(error) { return error instanceof Error ? `${error.name}: ${error.message}\n${error.stack || ''}` : String(error) }

const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
  ? { headless: true, executablePath: process.env.L12_CHROMIUM_EXECUTABLE }
  : { headless: true, channel: 'msedge' })

let page
try {
  const context = await browser.newContext()
  page = await context.newPage()
  page.setDefaultTimeout(15000)
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  async function loadMobile(scenario, extraQuery = '') {
    await page.setViewportSize({ width: scenario.width, height: scenario.height })
    const hand = scenario.hand ?? 20
    await page.goto(`${target}?mobile=1&canvas=1&field=full&markers=5&piles=40&hand=${hand}&rankedClock=1${scenario.query ?? ''}${extraQuery}`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    await page.waitForTimeout(220)
    if (scenario.safe) {
      await page.evaluate(safe => {
        const probe = [...document.body.children].find(element => element instanceof HTMLElement && element.style.cssText.includes('safe-area-inset'))
        assertProbe(probe)
        probe.style.padding = `${safe.top}px ${safe.right}px ${safe.bottom}px ${safe.left}px`
        window.dispatchEvent(new Event('resize'))
        function assertProbe(value) { if (!(value instanceof HTMLElement)) throw new Error('safe-area probe missing') }
      }, scenario.safe)
      await page.waitForTimeout(140)
    }
    if (scenario.tapped || scenario.extraRelics || scenario.emptyRelic) {
      await page.evaluate(({ tapped, extraRelics, emptyRelic }) => {
        const players = window.__l12State?.game?.players ?? []
        for (const player of players) {
          if (tapped) {
            player.master.tapped = true
            if (player.relic) player.relic.tapped = true
          }
          if (extraRelics && player.relic) player.extraRelics = Array.from({ length: extraRelics }, (_, index) => ({ ...player.relic, instanceId: `${player.relic.instanceId}-extra-${index}`, tapped: index % 2 === 0 }))
          if (emptyRelic) { player.relic = null; player.extraRelics = [] }
        }
      }, scenario)
      await page.waitForTimeout(180)
    }
  }

  async function measure() {
    return page.evaluate(() => {
      const rootStyle = getComputedStyle(document.documentElement)
      const rotated = document.documentElement.dataset.l12Rotated === 'true'
      const viewportLeft = parseFloat(rootStyle.getPropertyValue('--l12-viewport-left')) || 0
      const viewportTop = parseFloat(rootStyle.getPropertyValue('--l12-viewport-top')) || 0
      const logicalHeight = parseFloat(rootStyle.getPropertyValue('--l12-viewport-height')) || innerHeight
      const logicalWidth = parseFloat(rootStyle.getPropertyValue('--l12-viewport-width')) || innerWidth
      const visible = element => {
        if (!(element instanceof HTMLElement)) return false
        const value = element.getBoundingClientRect(), style = getComputedStyle(element)
        return value.width > 0 && value.height > 0 && style.display !== 'none' && style.visibility !== 'hidden'
      }
      const physicalRect = element => element.getBoundingClientRect()
      const rect = element => {
        const value = physicalRect(element)
        const logical = rotated
          ? { left: value.top - viewportTop, top: logicalHeight - (value.right - viewportLeft), width: value.height, height: value.width }
          : { left: value.left - viewportLeft, top: value.top - viewportTop, width: value.width, height: value.height }
        return { ...logical, right: logical.left + logical.width, bottom: logical.top + logical.height, centerX: logical.left + logical.width / 2, centerY: logical.top + logical.height / 2 }
      }
      const union = values => {
        const value = { left: Math.min(...values.map(item => item.left)), top: Math.min(...values.map(item => item.top)), right: Math.max(...values.map(item => item.right)), bottom: Math.max(...values.map(item => item.bottom)) }
        return { ...value, width: value.right - value.left, height: value.bottom - value.top, centerX: (value.left + value.right) / 2, centerY: (value.top + value.bottom) / 2 }
      }
      const hit = element => {
        const value = physicalRect(element)
        const top = document.elementFromPoint(value.left + value.width / 2, value.top + value.height / 2)
        return Boolean(top && (top === element || element.contains(top)))
      }
      const scrollReachable = element => {
        for (let parent = element.parentElement; parent; parent = parent.parentElement) {
          const style = getComputedStyle(parent)
          if (parent.scrollHeight > parent.clientHeight + 1 && ['auto', 'scroll'].includes(style.overflowY)) return true
        }
        return false
      }
      const overlap = (a, b) => Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left)) * Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top))
      const externalBlockers = ['.l12-hand', '.mobile-timed-clocks', '.mobile-extra-zone', '.left-rail', '.right-rail', '.board-target-controls', '.resource-payment-controls'].flatMap(selector => [...document.querySelectorAll(selector)]).filter(visible)
      const halves = [...document.querySelectorAll('.battlefield-half.l12-player-mat')].filter(visible).map((half, index) => {
        const slots = [...half.querySelectorAll('.formation-slot')].filter(visible).map(rect)
        const master = half.querySelector('.mini-master'), relic = half.querySelector('.relic-zone')
        const field = union(slots), group = union([master, relic].filter(visible).map(rect)), halfRect = rect(half)
        const ownMatBlockers = [...half.querySelectorAll('.battle-zone,.mat-piles,.resource-zone')].filter(visible)
        const collisionBlockers = [...new Set([...ownMatBlockers, ...externalBlockers.filter(element => !half.contains(element))])]
        return {
          side: half.classList.contains('opponent-half') ? 'opponent' : index === 0 ? 'opponent' : 'my',
          half: halfRect, field, group, master: rect(master), relic: rect(relic),
          deltaY: group.centerY - field.centerY,
          horizontalGap: field.left - group.right,
          intrusion: Math.max(0, group.right - field.left),
          withinHalf: group.left >= halfRect.left - .5 && group.right <= halfRect.right + .5 && group.top >= halfRect.top - .5 && group.bottom <= halfRect.bottom + .5,
          hitTargets: { master: hit(master), relic: hit(relic) },
          extraRelics: [...half.querySelectorAll('.extra-relic')].filter(visible).map(element => ({ rect: rect(element), hit: hit(element) })),
          collisions: collisionBlockers.filter(element => overlap(group, rect(element)) > 1).map(element => ({ className: String(element.className), area: overlap(group, rect(element)) })),
        }
      })
      const controls = [...document.querySelectorAll('.mobile-card-inspector-handle-global,.mobile-battle-dock__route button,.mobile-battle-dock button,.board-target-controls button,.resource-payment-controls button,.l12-prompt-overlay button')].filter(visible).map(element => ({
        text: (element.textContent || element.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 80), overlay: Boolean(element.closest('.l12-prompt-overlay,.mobile-safe-overlay')), scrollReachable: scrollReachable(element),
        className: String(element.className), rect: rect(element), hit: hit(element),
      }))
      return { viewport: { physicalWidth: innerWidth, physicalHeight: innerHeight, logicalWidth, logicalHeight, rotated }, halves, controls, scroll: { bodyX: document.body.scrollWidth - innerWidth, bodyY: document.body.scrollHeight - innerHeight, documentX: document.documentElement.scrollWidth - innerWidth, documentY: document.documentElement.scrollHeight - innerHeight } }
    })
  }

  function normalize(measured) {
    for (const half of measured.halves) {
      for (const key of ['deltaY', 'horizontalGap', 'intrusion']) half[key] = round(half[key])
      for (const key of ['half', 'field', 'group', 'master', 'relic']) for (const property of Object.keys(half[key])) half[key][property] = round(half[key][property])
      for (const item of half.extraRelics) for (const property of Object.keys(item.rect)) item.rect[property] = round(item.rect[property])
      for (const collision of half.collisions) collision.area = round(collision.area)
    }
    for (const control of measured.controls) for (const property of Object.keys(control.rect)) control.rect[property] = round(control.rect[property])
    return measured
  }

  function assertMobile(name, measured, { allowBoardCovered = false } = {}) {
    assert.equal(measured.halves.length, 2, `${name}: expected two battlefield halves`)
    for (const half of measured.halves) {
      assert.ok(Math.abs(half.deltaY) <= 2, `${name} ${half.side}: vertical delta ${half.deltaY}px`)
      assert.ok(half.horizontalGap >= -.5 && half.intrusion <= .5, `${name} ${half.side}: commander group entered six-slot field`)
      assert.ok(half.withinHalf, `${name} ${half.side}: commander group clipped by battlefield half`)
      if (!allowBoardCovered) {
        assert.ok(half.hitTargets.master, `${name} ${half.side}: master center is obscured`)
        assert.ok(half.hitTargets.relic, `${name} ${half.side}: relic center is obscured`)
      }
      assert.equal(half.collisions.length, 0, `${name} ${half.side}: unexpected external collision ${JSON.stringify(half.collisions)}`)
    }
    const persistentControls = measured.controls.filter(control => control.className.includes('mobile-card-inspector-handle-global') || /返回|投降/.test(control.text))
    assert.ok(persistentControls.every(control => control.hit), `${name}: persistent route/detail control center is obscured`)
    if (allowBoardCovered) {
      const overlayControls = measured.controls.filter(control => control.overlay)
      assert.ok(overlayControls.length > 0, `${name}: blocking overlay has no visible controls`)
      assert.ok(overlayControls.every(control => control.hit || control.scrollReachable), `${name}: blocking overlay control is neither hit-reachable nor scroll-reachable`)
    }
  }

  for (const scenario of mobileScenarios) {
    await loadMobile(scenario)
    const measured = normalize(await measure())
    const result = { ...scenario, file: `${scenario.name}.png`, measured, assertions: 'pending' }
    report.scenarios.push(result)
    assertMobile(scenario.name, measured, scenario)
    await page.screenshot({ path: path.join(output, result.file) })
    result.assertions = 'passed'
  }

  async function interaction(name, setup, action, expectedSelector) {
    const scenario = { name, width: 844, height: 390, ...(setup ?? {}) }
    await loadMobile(scenario)
    const before = normalize(await measure())
    assertMobile(`${name}-before`, before)
    const outcome = await action()
    if (expectedSelector) await page.locator(expectedSelector).waitFor()
    const file = `interaction-${name}.png`
    await page.screenshot({ path: path.join(output, file) })
    report.interactions.push({ name, file, outcome, expectedSelector, visible: expectedSelector ? await page.locator(expectedSelector).isVisible() : true })
  }

  await interaction('target', { query: '&board-target=1&target-mixed=1&inline-rich=1' }, async () => {
    const targetCard = page.locator('.formation-slot.targetable,.formation-slot.payment-resource,.formation-slot.resource-ready').first()
    assert.ok(await targetCard.count(), 'target interaction: no clickable field target')
    await targetCard.click(); await page.waitForTimeout(100); return { clicked: true }
  })
  await interaction('payment', { query: '&morale-payment=1&runes=3&rune-usable=3&payment-actions=1&inline-rich=1' }, async () => {
    const trigger = page.locator('.my-half .mobile-morale-stack-trigger')
    assert.ok(await trigger.count(), 'payment interaction: morale trigger missing')
    await trigger.click(); return { clicked: true }
  }, '.mobile-morale-overlay')
  await interaction('card-detail', {}, async () => {
    await page.locator('.my-half .mini-master').hover()
    await page.locator('.mobile-card-inspector-handle-global').click(); return { clicked: true }
  }, '.mobile-card-inspector')
  await interaction('record-tool', {}, async () => {
    await page.locator('.mobile-record-trigger').click(); return { clicked: true }
  }, '.mobile-record-overlay')

  for (const route of [{ name: 'return', pattern: /返回/ }, { name: 'surrender', pattern: /投降/ }]) {
    await loadMobile({ name: route.name, width: 844, height: 390 })
    const button = page.locator('.mobile-battle-dock__route button').filter({ hasText: route.pattern }).first()
    assert.ok(await button.count(), `${route.name}: route control missing`)
    let dialogText = null
    const handleDialog = dialog => { dialogText = dialog.message(); void dialog.dismiss().catch(() => {}) }
    page.on('dialog', handleDialog)
    await button.click(); await page.waitForTimeout(120)
    page.off('dialog', handleDialog)
    const file = `interaction-${route.name}.png`
    await page.screenshot({ path: path.join(output, file) })
    report.interactions.push({ name: route.name, file, clicked: true, dialogText })
  }

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto(`${target}?field=full&markers=5&piles=40&hand=20&rankedClock=1`, { waitUntil: 'domcontentloaded' })
  await page.locator('[data-l12-battle-layout="desktop"]').waitFor()
  await page.waitForTimeout(240)
  const desktop = normalize(await measure())
  assert.equal(desktop.viewport.rotated, false, 'desktop comparison unexpectedly rotated')
  const desktopFile = 'desktop-1920x1080.png'
  await page.screenshot({ path: path.join(output, desktopFile) })
  report.scenarios.push({ name: 'desktop-1920x1080', width: 1920, height: 1080, file: desktopFile, measured: desktop, assertions: 'recorded; mobile selector is physically isolated' })

  report.status = 'passed'
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  const summary = report.scenarios.flatMap(scenario => scenario.measured.halves.map(half => ({ scenario: scenario.name, side: half.side, deltaY: half.deltaY, horizontalGap: half.horizontalGap, intrusion: half.intrusion, withinHalf: half.withinHalf, masterHit: half.hitTargets.master, relicHit: half.hitTargets.relic })))
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2))
  console.log(JSON.stringify({ output, status: report.status, scenarios: report.scenarios.length, interactions: report.interactions.length, maxMobileDelta: Math.max(...summary.filter(item => !item.scenario.startsWith('desktop-')).map(item => Math.abs(item.deltaY))), minMobileGap: Math.min(...summary.filter(item => !item.scenario.startsWith('desktop-')).map(item => item.horizontalGap)) }, null, 2))
} catch (error) {
  report.status = 'failed'
  report.error = errorText(error)
  try {
    if (page) await page.screenshot({ path: path.join(output, 'failure.png') })
    fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  } catch (evidenceError) {
    report.evidenceError = errorText(evidenceError)
  }
  throw error
} finally {
  await browser.close()
}
