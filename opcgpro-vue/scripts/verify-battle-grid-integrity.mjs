import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5199/__l12_battle_preview__?special=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15'
const output = process.env.L12_BATTLE_GRID_QA_OUT || path.join(os.tmpdir(), 'l12-battle-grid-integrity')
const viewports = [
  [1024, 768],
  [1280, 720],
  [1366, 768],
  [1440, 900],
  [1680, 1050],
  [1920, 1080],
  [1920, 1200],
  [2532, 1280],
  [2560, 1080],
  [2560, 1440],
  [3440, 1440],
]

fs.mkdirSync(output, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const page = await browser.newPage()
  const results = []
  for (const [width, height] of viewports) {
    await page.setViewportSize({ width, height })
    await page.goto(target, { waitUntil: 'domcontentloaded' })
    await page.locator('.my-half').waitFor()
    await page.waitForTimeout(180)
    const result = await page.evaluate(() => {
      const felt = document.querySelector('.felt-board')
      const enemy = document.querySelector('.opponent-half')
      const mine = document.querySelector('.my-half')
      const observer = document.querySelector('.card-state-transition-layer')
      const rect = element => element.getBoundingClientRect()
      const flowChildren = [...felt.children].filter(element => {
        const style = getComputedStyle(element)
        return style.display !== 'none' && !['absolute', 'fixed'].includes(style.position)
      }).map(element => element.className)
      const rows = getComputedStyle(felt).gridTemplateRows.split(' ').filter(Boolean)
      return {
        layout: document.querySelector('.board-viewport')?.getAttribute('data-l12-battle-layout'),
        rowCount: rows.length,
        rows,
        enemyRow: getComputedStyle(enemy).gridRowStart,
        myRow: getComputedStyle(mine).gridRowStart,
        enemyHeight: rect(enemy).height,
        myHeight: rect(mine).height,
        observerDisplay: getComputedStyle(observer).display,
        flowChildren,
        overflow: {
          x: document.documentElement.scrollWidth > innerWidth + 1,
          y: document.documentElement.scrollHeight > innerHeight + 1,
        },
      }
    })
    assert.equal(result.rowCount, 3, `${width}x${height}: battlefield generated implicit rows ${JSON.stringify(result)}`)
    assert.equal(result.enemyRow, '1', `${width}x${height}: opponent battlefield left row 1 ${JSON.stringify(result)}`)
    assert.equal(result.myRow, '3', `${width}x${height}: player battlefield left row 3 ${JSON.stringify(result)}`)
    assert(Math.abs(result.enemyHeight - result.myHeight) <= 1, `${width}x${height}: battlefield halves are not equal height ${JSON.stringify(result)}`)
    assert(Math.min(result.enemyHeight, result.myHeight) > 100, `${width}x${height}: battlefield half collapsed ${JSON.stringify(result)}`)
    assert.equal(result.observerDisplay, 'none', `${width}x${height}: state observer participates in layout ${JSON.stringify(result)}`)
    assert.deepEqual(result.flowChildren, [
      'battlefield-half opponent-half l12-player-mat side-opponent faction-otherworld',
      'board-seam',
      'battlefield-half my-half l12-player-mat side-my faction-taiyangcheng active-turn',
    ], `${width}x${height}: unexpected direct child participates in battlefield grid ${JSON.stringify(result)}`)
    assert.deepEqual(result.overflow, { x: false, y: false }, `${width}x${height}: document overflow ${JSON.stringify(result)}`)
    if ((width === 2532 && height === 1280) || (width === 3440 && height === 1440)) {
      await page.screenshot({ path: path.join(output, `battle-grid-${width}x${height}.png`) })
    }
    results.push({ width, height, ...result })
  }
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true, results }, null, 2))
  console.log(`Battle grid integrity passed across ${viewports.length} viewport profiles: ${output}`)
} finally {
  await browser.close()
}
