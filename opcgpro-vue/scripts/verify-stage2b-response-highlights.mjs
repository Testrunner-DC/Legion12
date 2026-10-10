import assert from 'node:assert/strict'
import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5201/__l12_battle_preview__'
const output = resolve(import.meta.dirname, '../../artifacts/stage2b-response-highlights')
const viewports = [
  [1280, 720], [320, 568], [360, 800], [390, 844], [430, 932], [568, 320],
  [667, 375], [740, 360], [844, 390], [932, 430], [1024, 768], [480, 800],
]
const screenshots = new Set(['1280x720', '320x568', '568x320'])
const regions = [
  '.my-half', '.opponent-half', '.my-half .battle-zone', '.opponent-half .battle-zone',
  '.my-half .formation', '.opponent-half .formation',
  '.my-half .resource-zone', '.opponent-half .resource-zone',
  '.my-half .resource-morale-stack', '.opponent-half .resource-morale-stack',
]

async function measure(page) {
  return page.evaluate(selectors => {
    const boxes = Object.fromEntries(selectors.map(selector => {
      const node = document.querySelector(selector)
      if (!node) throw new Error(`Missing layout region: ${selector}`)
      const rect = node.getBoundingClientRect()
      return [selector, { x: rect.x, y: rect.y, width: rect.width, height: rect.height }]
    }))
    return { boxes, scrollWidth: document.documentElement.scrollWidth,
      bodyScrollWidth: document.body.scrollWidth, innerWidth: window.innerWidth }
  }, regions)
}

function assertNoHorizontalOverflow(layout, label) {
  assert.ok(layout.scrollWidth <= layout.innerWidth + 1
    && layout.bodyScrollWidth <= layout.innerWidth + 1,
  `${label}: page overflows horizontally (${layout.scrollWidth}/${layout.bodyScrollWidth} > ${layout.innerWidth})`)
}

function assertUnshifted(before, after, label) {
  for (const selector of regions) {
    for (const key of ['x', 'y', 'width', 'height'])
      assert.ok(Math.abs(before.boxes[selector][key] - after.boxes[selector][key]) < 1,
        `${label}: ${selector} ${key} moved or expanded after the target highlight`)
  }
}

await mkdir(output, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const [width, height] of viewports) {
    const profile = `${width}x${height}`
    const page = await browser.newPage({ viewport: { width, height } })
    try {
      const mobile = width !== 1280
      await page.goto(`${base}?canvas=1&morale=8&active=8${mobile ? '&mobile=1' : ''}`, { waitUntil: 'domcontentloaded' })
      await page.locator('.my-half .morale-orb').first().waitFor()
      for (const owner of [0, 1]) {
        const side = owner === 0 ? '.my-half' : '.opponent-half'
        const target = await page.evaluate(index => {
          const game = window.__battleDockFixture.state.game
          const resources = game.players[index].morale.filter(resource => !resource.tapped && !resource.isGodPower)
          if (resources.length < 2) throw new Error('fixture needs identical public resource faces')
          const id = resources[1].instanceId
          game.prompts = [{ promptId: `stage2b-${index}`, playerIndex: 0, kind: 'response',
            text: '是否响应？', validChoices: ['pass'], minChoose: 1, maxChoose: 1,
            continuation: 'stack-response', data: { responseTargetIds: '[]' } }]
          return { id, index: game.players[index].morale.findIndex(resource => resource.instanceId === id) }
        }, owner)
        await page.locator('.l12-prompt-overlay .prompt-minimize').first().waitFor()
        const baseline = await measure(page)
        assertNoHorizontalOverflow(baseline, `${profile} baseline`)
        await page.evaluate(id => {
          const game = window.__battleDockFixture.state.game
          game.prompts[0].data.responseTargetIds = JSON.stringify([id])
        }, target.id)
        await page.locator(`${side} .morale-orb.response-target`).waitFor()
        assert.equal(await page.locator(`${side} .morale-orb.response-target`).count(), 1,
          `${profile}: only one identical-face morale resource is highlighted`)
        assert.equal(await page.locator(`${owner === 0 ? '.opponent-half' : '.my-half'} .morale-orb.response-target`).count(), 0)
        const actualIndex = await page.locator(`${side} .morale-orb`).evaluateAll(nodes =>
          nodes.findIndex(node => node.classList.contains('response-target')))
        assert.equal(actualIndex, target.index, `${profile}: selected instance maps to its own orb`)
        const highlighted = await measure(page)
        assertNoHorizontalOverflow(highlighted, `${profile} highlighted`)
        assertUnshifted(baseline, highlighted, `${profile} expanded`)
        await page.locator('.l12-prompt-overlay .prompt-minimize').first().click()
        assert.equal(await page.locator(`${side} .morale-orb.response-target`).count(), 1,
          `${profile}: minimizing preserves the same target`)
        assertNoHorizontalOverflow(await measure(page), `${profile} minimized`)
        if (owner === 0 && screenshots.has(profile))
          await page.screenshot({ path: resolve(output, `stage2b-${profile}.png`) })
        await page.locator('.prompt-minimized-bar button').click()
        assert.equal(await page.locator(`${side} .morale-orb.response-target`).count(), 1,
          `${profile}: restoring preserves the same target`)
        const restored = await measure(page)
        assertNoHorizontalOverflow(restored, `${profile} restored`)
        assertUnshifted(baseline, restored, `${profile} restored`)
        await page.evaluate(() => { window.__battleDockFixture.state.game.prompts = [] })
        await page.waitForFunction(() => document.querySelectorAll('.morale-orb.response-target').length === 0)
      }
      console.log(`Stage2B response morale highlight passed: ${profile}`)
    } finally { await page.close() }
  }
  console.log(`Representative screenshots: ${output}`)
} finally { await browser.close() }
