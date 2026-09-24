import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5212/sandbox'
const out = path.resolve(process.env.L12_SANDBOX_OUT || '../artifacts/mobile-sandbox-entry/acceptance')
fs.mkdirSync(out, { recursive: true })

const manifest = { target, status: 'running', cases: [], screenshots: [], errors: [] }
const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
  ? { headless: true, executablePath: process.env.L12_CHROMIUM_EXECUTABLE }
  : { headless: true })
const page = await browser.newPage()
const cdp = await page.context().newCDPSession(page)
const zero = { top: 0, right: 0, bottom: 0, left: 0 }
page.setDefaultTimeout(8000)
page.on('pageerror', error => manifest.errors.push(error.message))
await page.route('**/*', route => {
  const url = new URL(route.request().url())
  return ['127.0.0.1', 'localhost'].includes(url.hostname) ? route.continue() : route.abort()
})

let label = ''
async function load(size, safe = zero) {
  await page.setViewportSize({ width: size[0], height: size[1] })
  await cdp.send('Emulation.setPageScaleFactor', { pageScaleFactor: 1 })
  await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets: safe })
  await page.goto(target, { waitUntil: 'domcontentloaded' })
  await page.locator('.sandbox-page').waitFor()
  await page.waitForTimeout(180)
}

async function shot(name) {
  const file = `${label}-${name}.png`
  await page.screenshot({ path: path.join(out, file) })
  manifest.screenshots.push(file)
}

async function reachable(locator) {
  await locator.scrollIntoViewIfNeeded()
  assert.equal(await locator.isVisible(), true, `${label}: control is not visible`)
  let hit = await locator.evaluate(element => {
    const box = element.getBoundingClientRect()
    const x = Math.max(0, Math.min(innerWidth - 1, box.left + box.width / 2))
    const y = Math.max(0, Math.min(innerHeight - 1, box.top + box.height / 2))
    const top = document.elementFromPoint(x, y)
    return box.width > 0 && box.height > 0 && Boolean(top && (top === element || element.contains(top) || top.contains(element)))
  })
  if (!hit) {
    await locator.evaluate(element => element.scrollIntoView({ block:'center', inline:'center' }))
    await page.waitForTimeout(40)
    hit = await locator.evaluate(element => {
      const box = element.getBoundingClientRect()
      const top = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2)
      return box.width > 0 && box.height > 0 && Boolean(top && (top === element || element.contains(top) || top.contains(element)))
    })
  }
  assert.equal(hit, true, `${label}: control center is obstructed`)
}

async function pageGeometry(name) {
  const report = await page.evaluate(() => {
    const root = document.documentElement
    const content = document.querySelector('.site-content')
    const sandbox = document.querySelector('.sandbox-page')
    const panel = document.querySelector('.sandbox-page>section')
    const header = document.querySelector('.site-mobile-head')
    const box = element => { const r = element.getBoundingClientRect(); return { left:r.left, top:r.top, right:r.right, bottom:r.bottom, width:r.width, height:r.height } }
    const c = box(content), s = box(sandbox), p = box(panel), h = box(header)
    const mobile = root.dataset.l12Mobile
    const viewport = root.dataset.l12Viewport
    return {
      mobile, viewport, content:c, sandbox:s, panel:p, header:h,
      rootOverflowX:root.scrollWidth > innerWidth + 1,
      contentOverflowX:content.scrollWidth > content.clientWidth + 1,
      pageCanScroll:sandbox.scrollHeight > sandbox.clientHeight + 1,
      deckButtons:document.querySelectorAll('.sandbox-deck>button').length,
      decks:document.querySelectorAll('.sandbox-deck').length,
      capabilities:document.querySelectorAll('.capabilities article').length,
      disasterOptions:document.querySelectorAll('.sandbox-grid select option').length,
      keyText:sandbox.textContent,
    }
  })
  assert.equal(report.mobile, 'true', `${label}/${name}: logical mobile canvas inactive ${JSON.stringify(report)}`)
  assert.equal(report.viewport, 'landscape', `${label}/${name}: route is not using logical landscape canvas`)
  assert.equal(report.rootOverflowX, false, `${label}/${name}: root horizontal overflow ${JSON.stringify(report)}`)
  assert.equal(report.contentOverflowX, false, `${label}/${name}: content horizontal overflow ${JSON.stringify(report)}`)
  assert.equal(report.decks, 2, `${label}/${name}: both deck panels must remain present`)
  assert.equal(report.deckButtons, 2, `${label}/${name}: both deck selectors must remain present`)
  assert.equal(report.capabilities, 3, `${label}/${name}: sandbox capabilities missing`)
  assert.equal(report.disasterOptions, 4, `${label}/${name}: disaster options missing`)
  assert.ok(report.keyText.includes('测试账号') && report.keyText.includes('建立测试沙盒') && report.keyText.includes('返回对战大厅'), `${label}/${name}: required entry controls missing`)
  assert.ok(report.header.height >= 40 && report.content.top >= report.header.bottom - 1, `${label}/${name}: mobile header/content collision ${JSON.stringify(report)}`)
  assert.ok(report.panel.left >= report.content.left - 1 && report.panel.right <= report.content.right + 1, `${label}/${name}: sandbox panel escaped canvas ${JSON.stringify(report)}`)
  manifest.cases.push({ label, name, ...report })
}

async function verifyDeckSelector(index) {
  const trigger = page.locator('.sandbox-deck>button').nth(index)
  await reachable(trigger)
  await trigger.click()
  const dialog = page.locator('.saved-deck-selector')
  await dialog.waitFor()
  const report = await dialog.evaluate(element => {
    const r = element.getBoundingClientRect()
    const host = document.querySelector('#l12-landscape-teleports')?.getBoundingClientRect() || { left:0, top:0, right:innerWidth, bottom:innerHeight }
    return {
      inside:r.left >= host.left - 1 && r.top >= host.top - 1 && r.right <= host.right + 1 && r.bottom <= host.bottom + 1,
      rows:element.querySelectorAll('.selector-list>button').length,
      footerButtons:element.querySelectorAll('footer button').length,
      listScrollable:(() => { const list=element.querySelector('.selector-list'); return Boolean(list && getComputedStyle(list).overflowY !== 'visible') })(),
    }
  })
  assert.equal(report.inside, true, `${label}: deck selector outside safe canvas ${JSON.stringify(report)}`)
  assert.equal(report.footerButtons, 2, `${label}: deck selector actions missing`)
  assert.equal(report.listScrollable, true, `${label}: deck selector list cannot scroll`)
  await reachable(dialog.getByRole('button', { name:'取消', exact:true }))
  if (index === 0) await shot('deck-selector')
  const usable = dialog.locator('.selector-list>button:not(:disabled)')
  if (await usable.count()) {
    await usable.first().click()
    const confirm = dialog.getByRole('button', { name:'确认使用', exact:true })
    await reachable(confirm)
    await confirm.click()
  } else {
    await dialog.getByRole('button', { name:'取消', exact:true }).click()
  }
  assert.equal(await page.locator('.saved-deck-selector').count(), 0, `${label}: deck selector did not close`)
}

async function verifySettings() {
  const menu = page.getByRole('button', { name:'打开导航' })
  await reachable(menu)
  await menu.click()
  const drawer = page.locator('#site-mobile-drawer.open')
  await drawer.waitFor()
  const settings = drawer.getByRole('button', { name:'设置', exact:true })
  await reachable(settings)
  await settings.click()
  const dialog = page.locator('.l12-settings-modal')
  await dialog.waitFor()
  await page.waitForTimeout(240)
  assert.equal(await page.locator('#site-mobile-drawer.open').count(), 0, `${label}: navigation drawer remained open over settings`)
  const report = await dialog.evaluate(element => {
    const r=element.getBoundingClientRect()
    return {
      inside:r.left >= -1 && r.top >= -1 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1,
      rows:element.querySelectorAll('.setting-row').length,
      selects:element.querySelectorAll('select').length,
      scrollable:getComputedStyle(element).overflowY !== 'visible',
    }
  })
  assert.equal(report.inside, true, `${label}: settings outside viewport ${JSON.stringify(report)}`)
  assert.equal(report.rows, 5, `${label}: settings rows missing`)
  assert.equal(report.selects, 3, `${label}: settings selects missing`)
  assert.equal(report.scrollable, true, `${label}: settings cannot scroll`)
  await reachable(dialog.getByRole('button', { name:'关闭设置' }))
  await shot('settings')
  await dialog.getByRole('button', { name:'关闭设置' }).click()
  assert.equal(await page.locator('.l12-settings-modal').count(), 0, `${label}: settings did not close`)
  if (await page.locator('#site-mobile-drawer.open').count()) await page.getByRole('button', { name:'关闭导航' }).click()
}

try {
  const cases = [
    { size:[667,320], safe:zero }, { size:[740,360], safe:zero }, { size:[844,390], safe:zero },
    { size:[915,412], safe:zero }, { size:[932,430], safe:zero }, { size:[1024,600], safe:zero },
    { size:[844,390], safe:{ top:0, right:0, bottom:21, left:59 } },
    { size:[844,390], safe:{ top:0, right:59, bottom:21, left:0 } },
  ]
  for (const item of cases) {
    label = `${item.size.join('x')}-safe-${item.safe.left}-${item.safe.right}`
    await load(item.size, item.safe)
    await pageGeometry('initial')
    const select = page.locator('.sandbox-grid select')
    await reachable(select)
    for (const value of ['none','random','all','custom']) await select.selectOption(value)
    await verifyDeckSelector(0)
    await verifyDeckSelector(1)
    await verifySettings()
    await reachable(page.locator('.sandbox-page footer a'))
    await reachable(page.locator('.sandbox-page footer button'))
    await pageGeometry('after-interactions')
    await shot('entry')
  }

  label = 'continuous'
  await load([844,390])
  const sizes = [[640,320],[667,320],[720,340],[740,360],[780,360],[812,375],[844,390],[873,393],[896,414],[915,412],[932,430],[960,450],[1024,576],[1024,600],[900,500],[768,432]]
  for (const size of sizes) {
    await page.setViewportSize({ width:size[0], height:size[1] })
    await page.waitForTimeout(45)
    await pageGeometry(`resize-${size.join('x')}`)
    await reachable(page.locator('.sandbox-deck>button').first())
    await reachable(page.locator('.sandbox-page footer button'))
  }

  assert.deepEqual(manifest.errors, [])
  manifest.status = 'passed'
} catch (error) {
  manifest.errors.push(error.stack)
  await shot('failure')
  throw error
} finally {
  fs.writeFileSync(path.join(out, 'manifest.json'), JSON.stringify(manifest, null, 2))
  await browser.close()
}
