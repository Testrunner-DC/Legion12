import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5174/__l12_battle_preview__?special=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15'
const output = process.env.L12_BATTLE_QA_OUT || path.join(os.tmpdir(), 'l12-battle-responsive')
const viewports = [
  { width: 1920, height: 1080 },
  { width: 1600, height: 900 },
  { width: 1440, height: 900 },
  { width: 1366, height: 768 },
  { width: 1280, height: 720 },
  { width: 1024, height: 768 },
  { width: 2560, height: 1080 },
]

fs.mkdirSync(output, { recursive: true })
let browser
try {
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    return ['127.0.0.1', 'localhost'].includes(url.hostname) ? route.continue() : route.abort()
  })

  const results = []
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(target, { waitUntil: 'networkidle' })
    await page.locator('.side-my .formation-slot').first().waitFor()
    await page.locator('.my-player-clock').waitFor()
    await page.waitForTimeout(250)
    const result = await page.evaluate(() => {
      const box = element => {
        if (!element) return null
        const rect = element.getBoundingClientRect()
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height, cx: rect.left + rect.width / 2 }
      }
      const intersects = (left, right) => Boolean(left && right && left.left < right.right - .5 && left.right > right.left + .5 && left.top < right.bottom - .5 && left.bottom > right.top + .5)
      const union = elements => {
        const boxes = elements.map(box).filter(Boolean)
        return boxes.length ? {
          left: Math.min(...boxes.map(item => item.left)),
          right: Math.max(...boxes.map(item => item.right)),
          top: Math.min(...boxes.map(item => item.top)),
          bottom: Math.max(...boxes.map(item => item.bottom)),
          cx: (Math.min(...boxes.map(item => item.left)) + Math.max(...boxes.map(item => item.right))) / 2,
        } : null
      }
      const myHandCards = [...document.querySelectorAll('.board-center > .l12-hand:last-child .hand-card-wrap')]
      const enemyHandCards = [...document.querySelectorAll('.opponent-hand .card-back')]
      const myHand = union(myHandCards)
      const enemyHand = union(enemyHandCards)
      const myHandContainer = box(document.querySelector('.board-center > .l12-hand:last-child'))
      const myExtra = box(document.querySelector('.my-half .special-lane'))
      const enemyExtra = box(document.querySelector('.opponent-half .special-lane'))
      const myClock = box(document.querySelector('.my-player-clock'))
      const enemyClock = box(document.querySelector('.opponent-player-clock'))
      const fieldSlots = [...document.querySelectorAll('.formation-slot')]
      const fieldCards = [...document.querySelectorAll('.formation-slot .card-tile')]
      const keywordRows = [...document.querySelectorAll('.opponent-half .card-keyword-row')]
      const myKeywordRows = [...document.querySelectorAll('.my-half .card-keyword-row')]
      const keywords = [...document.querySelectorAll('.formation-slot .card-keyword')]
      const statusIcons = [...document.querySelectorAll('.opponent-half .card-status-icon')]
      const statusCard = document.querySelector('.opponent-half .formation-slot .card-tile')
      const cost = statusCard?.querySelector('.card-cost')
      const centerFieldSlot = document.querySelector('.my-half .formation-slot:nth-child(2)')
      const middleMyCard = myHandCards[Math.floor(myHandCards.length / 2)]?.querySelector('.card-tile')
      const middleEnemyCard = enemyHandCards[Math.floor(enemyHandCards.length / 2)]
      const pileCard = document.querySelector('.pile-card')
      const felt = box(document.querySelector('.felt-board'))
      const phase = box(document.querySelector('.phase-column'))
      const playerPanel = box(document.querySelector('.player-panel'))
      const recordLog = box(document.querySelector('.record-log'))
      const stage = box(document.querySelector('.board-stage'))
      return {
        stage,
        viewport: { width: innerWidth, height: innerHeight },
        fieldSquareDelta: Math.max(...fieldSlots.map(element => { const rect = element.getBoundingClientRect(); return Math.abs(rect.width - rect.height) })),
        fieldCardsInside: fieldCards.every(element => { const card = element.getBoundingClientRect(); const slot = element.closest('.formation-slot').getBoundingClientRect(); return card.left >= slot.left - 1 && card.right <= slot.right + 1 && card.top >= slot.top - 1 && card.bottom <= slot.bottom + 1 }),
        phaseAlignment: { top: Math.abs(phase.top - felt.top), bottom: Math.abs(phase.bottom - felt.bottom) },
        keywordRows: keywordRows.map(row => row.children.length),
        myKeywordRows: myKeywordRows.map(row => row.children.length),
        keywordOverflow: keywords.some(element => element.scrollWidth > element.clientWidth),
        keywordMeasurements: keywords.map(element => ({ text: element.textContent?.trim(), clientWidth: element.clientWidth, scrollWidth: element.scrollWidth })),
        statusCount: statusIcons.length,
        statusInside: statusIcons.every(element => { const icon = element.getBoundingClientRect(); const card = statusCard.getBoundingClientRect(); return icon.left >= card.left - 1 && icon.right <= card.right + 1 && icon.top >= card.top - 1 && icon.bottom <= card.bottom + 1 }),
        statusCostGap: statusIcons.length && cost ? statusIcons[0].getBoundingClientRect().top - cost.getBoundingClientRect().bottom : null,
        handCount: { mine: myHandCards.length, enemy: enemyHandCards.length },
        handCenterDelta: Math.abs(myHandContainer.cx - box(centerFieldSlot).cx),
        handVisualFanDrift: Math.abs(myHand.cx - myHandContainer.cx),
        handIntrusions: {
          myExtra: intersects(myHand, myExtra), myClock: intersects(myHand, myClock),
          enemyExtra: intersects(enemyHand, enemyExtra), enemyClock: intersects(enemyHand, enemyClock),
        },
        cardSizeDelta: {
          myWidth: Math.abs(box(middleMyCard).width - box(pileCard).width),
          myHeight: Math.abs(box(middleMyCard).height - box(pileCard).height),
          enemyWidth: Math.abs(box(middleEnemyCard).width - box(pileCard).width),
          enemyHeight: Math.abs(box(middleEnemyCard).height - box(pileCard).height),
        },
        playerPanelHeight: playerPanel.height,
        recordLogHeight: recordLog.height,
        playerSummaryOverflow: [...document.querySelectorAll('.player-summary-primary,.player-summary-meta')].some(element => element.scrollWidth > element.clientWidth),
        playerSummaryTruncation: [...document.querySelectorAll('.player-summary-meta>.rank-badge>span,.player-summary-meta>.title-badge>span')].some(element => element.scrollWidth > element.clientWidth),
        playerSummaryMeasurements: [...document.querySelectorAll('.player-summary-meta>.rank-badge>span,.player-summary-meta>.title-badge>span')].map(element => ({ text: element.textContent?.trim(), clientWidth: element.clientWidth, scrollWidth: element.scrollWidth })),
        documentOverflow: { x: document.documentElement.scrollWidth > innerWidth + 1, y: document.documentElement.scrollHeight > innerHeight + 1 },
      }
    })

    assert.equal(result.handCount.mine, 15, `my hand fixture changed at ${viewport.width}x${viewport.height}`)
    assert.equal(result.handCount.enemy, 15, `enemy hand fixture changed at ${viewport.width}x${viewport.height}`)
    assert.deepEqual(result.keywordRows, [3, 3], `opponent keyword wrapping changed at ${viewport.width}x${viewport.height}`)
    assert.deepEqual(result.myKeywordRows, [2], `my keyword comparison changed at ${viewport.width}x${viewport.height}`)
    assert.equal(result.keywordOverflow, false, `keyword text overflows at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.keywordMeasurements)}`)
    assert.equal(result.statusCount, 4, `status icon fixture changed at ${viewport.width}x${viewport.height}`)
    assert.equal(result.statusInside, true, `status icons leave the card at ${viewport.width}x${viewport.height}`)
    assert(result.statusCostGap >= 2, `status icons touch cost at ${viewport.width}x${viewport.height}: gap ${result.statusCostGap}`)
    assert(result.fieldSquareDelta <= 1, `battlefield cells are not square at ${viewport.width}x${viewport.height}`)
    assert.equal(result.fieldCardsInside, true, `field card leaves its cell at ${viewport.width}x${viewport.height}`)
    assert(result.phaseAlignment.top <= 1.5 && result.phaseAlignment.bottom <= 1.5, `phase track is not aligned with the felt board at ${viewport.width}x${viewport.height}`)
    assert(result.handCenterDelta <= 1.5, `hands are not centered to the middle field column at ${viewport.width}x${viewport.height}`)
    assert(result.handVisualFanDrift <= 6, `visible hand fan drifts too far from its center axis at ${viewport.width}x${viewport.height}`)
    assert(Object.values(result.handIntrusions).every(value => value === false), `hand intrudes into an extra zone or clock at ${viewport.width}x${viewport.height}`)
    assert(Object.values(result.cardSizeDelta).every(value => value <= 1), `hand and pile card sizes differ at ${viewport.width}x${viewport.height}`)
    assert.equal(result.playerSummaryOverflow, false, `two-line player summary overflows at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.playerSummaryMeasurements)}`)
    assert.equal(result.playerSummaryTruncation, false, `rank and title badges truncate at ${viewport.width}x${viewport.height}: ${JSON.stringify(result.playerSummaryMeasurements)}`)
    assert(result.recordLogHeight > result.playerPanelHeight * 2, `player summary did not yield enough record space at ${viewport.width}x${viewport.height}`)
    assert.equal(result.documentOverflow.x, false, `document overflows horizontally at ${viewport.width}x${viewport.height}`)
    assert.equal(result.documentOverflow.y, false, `document overflows vertically at ${viewport.width}x${viewport.height}`)
    await page.screenshot({ path: path.join(output, `battle-${viewport.width}x${viewport.height}.png`), fullPage: false })
    results.push({ ...viewport, ...result })
  }
  if (pageErrors.length) throw new Error(`Page errors: ${pageErrors.join(' | ')}`)
  console.log(JSON.stringify({ output, results }, null, 2))
} finally {
  await browser?.close()
}
