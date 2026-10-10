import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5174/__l12_battle_preview__'
const output = process.env.L12_SLOT_QA_OUT || path.join(os.tmpdir(), 'l12-battle-slot-short-label')
fs.mkdirSync(output, { recursive: true })

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const [name, width, height, query] of [
    ['desktop', 1366, 768, ''],
    ['mobile-landscape', 844, 390, '&mobile=1&canvas=1'],
  ]) {
    const page = await browser.newPage({ viewport: { width, height } })
    await page.goto(`${base}?field=empty${query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('.my-half .formation-slot').first().waitFor()
    const slots = await page.locator('.formation-slot').evaluateAll(elements => elements.map(element => ({
      text: element.textContent?.trim(),
      aria: element.getAttribute('aria-label'),
      width: element.getBoundingClientRect().width,
      height: element.getBoundingClientRect().height,
    })))
    assert.equal(slots.length, 12, `${name}: expected both battlefield halves`)
    const expected = new Set()
    for (const side of ['我方', '对方']) for (const row of ['前排', '后排']) for (const column of ['左格', '中格', '右格']) expected.add(`${side}${row}${column}`)
    for (const slot of slots) {
      assert(['左格', '中格', '右格'].includes(slot.text), `${name}: unexpected visible slot ${JSON.stringify(slot)}`)
      assert(expected.delete(slot.aria), `${name}: duplicate or incomplete accessibility label ${JSON.stringify(slot)}`)
      assert(slot.width > 0 && slot.height > 0, `${name}: collapsed slot ${JSON.stringify(slot)}`)
    }
    assert.equal(expected.size, 0, `${name}: missing positions ${[...expected]}`)
    await page.screenshot({ path: path.join(output, `${name}-empty.png`) })

    await page.goto(`${base}?field=full${query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('.my-half .formation-slot').first().waitFor()
    assert.equal(await page.locator('.formation-slot > span').count(), 0, `${name}: occupied slots gained an extra location label`)
    await page.screenshot({ path: path.join(output, `${name}-occupied.png`) })
    await page.close()
  }
  console.log(`Battle slot short-label browser matrix passed: ${output}`)
} finally {
  await browser.close()
}
