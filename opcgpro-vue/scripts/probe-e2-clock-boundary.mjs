import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5297/__l12_battle_preview__'
const output = process.env.L12_E2_CLOCK_PROBE_OUT
assert(output && path.isAbsolute(output), 'L12_E2_CLOCK_PROBE_OUT must be an absolute path')
assert(!fs.existsSync(output), `Refusing existing output directory: ${output}`)
fs.mkdirSync(output, { recursive: true })

const cases = [
  { name: 'default', query: 'rankedClock=1&totalMs=1500000&operationMs=240000&disconnected=1&reconnectMs=240000' },
  { name: 'legal-maximum', query: 'rankedClock=1&totalMs=7200000&operationMs=900000&disconnected=1&reconnectMs=900000' },
]
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const report = { schema: 1, viewport: { width: 667, height: 375 }, authority: { totalTimeSecondsMax: 7200, operationTimeSecondsMax: 900, reconnectGraceSecondsMax: 900 }, cases: [] }
try {
  const context = await browser.newContext({ viewport: report.viewport, hasTouch: true })
  const page = await context.newPage()
  for (const item of cases) {
    await page.goto(`${base}?mobile=1&canvas=1&field=full&hand=40&markers=5&piles=40&${item.query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('.mobile-rail-clock').first().waitFor()
    const measurements = await page.locator('.mobile-rail-clock').evaluateAll(clocks => clocks.flatMap((clock, clockIndex) => [...clock.querySelectorAll('strong,span,small,b')].map(element => ({
      clockIndex, tag: element.tagName, text: element.textContent?.trim() ?? '',
      scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
      scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
      clipped: element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1,
    }))))
    const clipped = measurements.filter(entry => entry.clipped)
    await page.screenshot({ path: path.join(output, `${item.name}-667x375.png`) })
    report.cases.push({ name: item.name, measurements, clipped })
  }
  await context.close()
} finally {
  await browser.close()
}
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
fs.writeFileSync(path.join(output, 'report.md'), [
  '# E2 ranked clock boundary probe', '',
  `Viewport: ${report.viewport.width}x${report.viewport.height}`,
  `Authority maxima: total ${report.authority.totalTimeSecondsMax}s; operation ${report.authority.operationTimeSecondsMax}s; reconnect ${report.authority.reconnectGraceSecondsMax}s`, '',
  ...report.cases.flatMap(item => [`## ${item.name}`, `Clipped descendants: ${item.clipped.length}`, ...item.clipped.map(entry => `- clock ${entry.clockIndex} ${entry.tag} “${entry.text}”: ${entry.scrollWidth}x${entry.scrollHeight} > ${entry.clientWidth}x${entry.clientHeight}`), '']),
].join('\n'))
console.log(JSON.stringify(report.cases.map(item => ({ name: item.name, clipped: item.clipped })), null, 2))
