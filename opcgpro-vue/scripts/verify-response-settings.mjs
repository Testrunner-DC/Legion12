import assert from 'node:assert/strict'
import { createRequire } from 'node:module'

const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5201/__l12_battle_preview__'
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
let assertions = 0
const ok = (value, message) => { assert.ok(value, message); assertions++ }
try {
  const page = await browser.newPage()
  page.setDefaultTimeout(10_000)
  for (const fixture of [
    { name: 'desktop', viewport: { width: 1280, height: 720 }, query: '' },
    { name: 'mobile', viewport: { width: 844, height: 390 }, query: 'mobile=1&canvas=1&' },
  ]) {
    await page.setViewportSize(fixture.viewport)
    await page.goto(`${target}?${fixture.query}field=full`, { waitUntil: 'domcontentloaded' })
    await page.getByRole('button', { name: '打开对局工具' }).click()
    await page.getByRole('button', { name: /响应设置/ }).click()
    const labels = await page.locator('.response-settings-form label b').allTextContents()
    assert.deepEqual(labels, ['默认', '仅有效响应', '5秒关闭无效响应']); assertions++
    ok(await page.locator('input[value="default"]').isChecked(), `${fixture.name}: default must be selected`)
    await page.locator('input[value="valid-only"]').check()
    ok(await page.locator('input[value="valid-only"]').isChecked(), `${fixture.name}: valid-only selectable`)
  }

  for (const fixture of [
    { name: 'desktop', viewport: { width: 1280, height: 720 }, query: '' },
    { name: 'mobile', viewport: { width: 844, height: 390 }, query: 'mobile=1&canvas=1&' },
  ]) {
    await page.setViewportSize(fixture.viewport)
    await page.goto(`${target}?${fixture.query}invalid-response-fixture=1`, { waitUntil: 'domcontentloaded' })
    const timer = page.locator('.prompt-panel .prompt-auto-close')
    await timer.waitFor()
    ok(/当前没有有效响应，将在 [0-5] 秒后自动关闭/.test(await timer.innerText()), `${fixture.name}: countdown copy`)
    await page.locator('.prompt-minimize').click()
    const minimized = page.locator('.prompt-minimized-bar .prompt-auto-close')
    await minimized.waitFor()
    ok((await minimized.innerText()).startsWith('当前没有有效响应，将在 '), `${fixture.name}: minimized countdown`)
    await page.waitForTimeout(1100)
    ok((await minimized.innerText()).includes('秒后自动关闭'), `${fixture.name}: countdown remains server-display only`)
  }
  console.log(`Response settings browser checks passed: ${assertions} assertions`)
} finally {
  await browser.close()
}
