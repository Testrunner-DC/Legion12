import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')

const target = process.argv[2]
  || 'http://127.0.0.1:5181/__l12_battle_preview__?special=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15'
const output = process.env.L12_BATTLE_HITBOX_QA_OUT || path.resolve('../../artifacts/batch291-hitboxes')
fs.mkdirSync(output, { recursive: true })

const viewports = [
  { name: 'desktop-1920', width: 1920, height: 1080 },
  { name: 'desktop-1366', width: 1366, height: 768 },
  { name: 'desktop-1024', width: 1024, height: 768 },
  { name: 'narrow-760', width: 760, height: 900 },
]
const displayScales = [1, 1.25, 1.5]
const hitPoints = [
  { name: 'center', x: 0.5, y: 0.5 },
  { name: 'left-edge', x: 0.08, y: 0.5 },
  { name: 'right-edge', x: 0.92, y: 0.5 },
  { name: 'top-edge', x: 0.5, y: 0.12 },
  { name: 'bottom-edge', x: 0.5, y: 0.88 },
]

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const reports = []

try {
  for (const deviceScaleFactor of displayScales) {
    for (const viewport of viewports) {
      const context = await browser.newContext({
        viewport: { width: viewport.width, height: viewport.height },
        deviceScaleFactor,
      })
      const page = await context.newPage()

      for (const point of hitPoints) {
        await page.goto(target)
        const slot = page.locator('.l12-player-mat.side-my .formation-slot').filter({ has: page.locator('.card-tile') }).first()
        await slot.waitFor()
        await slot.scrollIntoViewIfNeeded()
        const slotBox = await slot.boundingBox()
        assert(slotBox, `${viewport.name}@${deviceScaleFactor}: source legion slot has no box`)
        await page.mouse.click(slotBox.x + slotBox.width / 2, slotBox.y + slotBox.height / 2)

        const action = slot.locator('.field-actions button', { hasText: '移动' })
        await action.waitFor({ state: 'visible' })
        await action.scrollIntoViewIfNeeded()
        const actionBox = await action.boundingBox()
        assert(actionBox, `${viewport.name}@${deviceScaleFactor}: legion action has no box`)
        const clickX = actionBox.x + actionBox.width * point.x
        const clickY = actionBox.y + actionBox.height * point.y
        const hit = await page.evaluate(({ x, y }) => {
          const button = document.querySelector('.l12-player-mat.side-my .formation-slot.source .field-actions button')
          const targetElement = document.elementFromPoint(x, y)
          return {
            isButton: Boolean(button && targetElement && (targetElement === button || button.contains(targetElement))),
            target: targetElement ? `${targetElement.tagName}.${String(targetElement.className || '')}` : null,
          }
        }, { x: clickX, y: clickY })
        assert(hit.isButton, `${viewport.name}@${deviceScaleFactor} ${point.name}: visible legion action is intercepted by ${hit.target}`)

        await page.mouse.click(clickX, clickY)
        await page.waitForFunction(() => document.querySelectorAll('.l12-player-mat.side-my .formation-slot.available').length > 0)
        const destination = page.locator('.l12-player-mat.side-my .formation-slot.available').first()
        await destination.scrollIntoViewIfNeeded()
        const destinationBox = await destination.boundingBox()
        assert(destinationBox, `${viewport.name}@${deviceScaleFactor}: move destination has no box`)
        await page.mouse.click(destinationBox.x + destinationBox.width / 2, destinationBox.y + destinationBox.height / 2)

        const commands = await page.evaluate(() => window.__sentCommands ?? [])
        assert.equal(commands.length, 1, `${viewport.name}@${deviceScaleFactor} ${point.name}: action must submit exactly once`)
        assert.equal(commands[0]?.type, 'gameAction', `${viewport.name}@${deviceScaleFactor} ${point.name}: wrong envelope`)
        assert.equal(commands[0]?.command?.type, 'move', `${viewport.name}@${deviceScaleFactor} ${point.name}: wrong command`)
      }

      await page.goto(target)
      const factionButton = page.locator('.l12-player-mat.side-my .resource-faction-action')
      await factionButton.scrollIntoViewIfNeeded()
      await factionButton.click()
      await page.getByRole('button', { name: '最小化弹框', exact: true }).click()
      await page.locator('.faction-effect-overlay.minimized').waitFor()
      assert.equal(await page.locator('.faction-effect-overlay.minimized').evaluate(element => getComputedStyle(element).pointerEvents), 'none',
        `${viewport.name}@${deviceScaleFactor}: minimized dialog shell must not block the board`)

      const visualSlot = page.locator('.l12-player-mat.side-my .formation-slot').filter({ has: page.locator('.card-tile') }).first()
      await visualSlot.scrollIntoViewIfNeeded()
      const visualSlotBox = await visualSlot.boundingBox()
      assert(visualSlotBox, `${viewport.name}@${deviceScaleFactor}: visual source slot has no box`)
      await page.mouse.click(visualSlotBox.x + visualSlotBox.width / 2, visualSlotBox.y + visualSlotBox.height / 2)
      await visualSlot.locator('.field-actions button', { hasText: '移动' }).waitFor({ state: 'visible' })
      await page.screenshot({ path: path.join(output, `${viewport.name}-${Math.round(deviceScaleFactor * 100)}.png`) })

      reports.push({ viewport: viewport.name, deviceScaleFactor, points: hitPoints.length, modalMinimized: true })
      await context.close()
    }
  }
} finally {
  await browser.close()
}

console.log(`Battle button hitboxes passed: ${reports.length} viewport/scale combinations, ${reports.reduce((sum, item) => sum + item.points, 0)} edge clicks, single-command dispatch preserved. Screenshots: ${output}`)
