import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const out = path.resolve(root, '../artifacts/deck-editor-saved-layout')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })

const entry = `
import {createApp} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {alternateArtApi,platformState} from '/src/l12/platform.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
const params=new URL(location.href).searchParams
if(params.has('mobile'))document.documentElement.dataset.l12Mobile='true'
const [cards,presets]=await Promise.all([loadDeckCatalog(),loadOfficialPresetDecks()])
const target=cards.find(card=>['legion','tactic','artifact'].includes(card.cardType))
target.effect=('这是用于验证超长卡牌效果文本独立滚动、不会被已保存牌库遮挡的对抗性内容。\\n').repeat(42)
const base=presets.find(deck=>deck.cardIds.length>=40)
if(!base)throw Error('fixture deck missing')
platformState.account={id:'saved-layout-qa',username:'布局验收',role:'player',createdAt:'2026-09-27',publicHistory:true}
alternateArtApi.mine=async()=>[]
const decks={}
for(let index=1;index<=18;index++){
  const name='验收牌库 '+String(index).padStart(2,'0')+' · '+(index%2?'超长名称用于省略与滚动检查':'常规名称')
  decks[name]={...base,name,updatedAt:new Date(2026,8,27,0,index).toISOString()}
}
localStorage.setItem('l12-custom-decks-v1:saved-layout-qa',JSON.stringify(decks))
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/deck-editor',component:Editor},{path:'/decks',component:{template:'<div/>'}}]})
await router.push('/deck-editor')
createApp(Editor).use(router).mount('#app')
`

const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'deck-editor-saved-layout-fixture',
  resolveId(id) { if (id === '/__saved_layout__.js') return id },
  load(id) { if (id === '/__saved_layout__.js') return entry },
  configureServer(vite) { vite.middlewares.use((request, response, next) => {
    if (request.url?.match(/^\/__saved_layout__(\?|$)/)) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__saved_layout__.js"></script>')
      return
    }
    next()
  }) },
}] })

const desktopProfiles = [
  ['desktop-3440x1440', 3440, 1440], ['desktop-1920x1080', 1920, 1080],
  ['desktop-1440x900', 1440, 900], ['desktop-1280x720', 1280, 720], ['desktop-1024x768', 1024, 768],
]
const mobileProfiles = [
  ['phone-430x932', 430, 932, false], ['phone-390x844', 390, 844, false],
  ['phone-360x640', 360, 640, false], ['phone-320x568', 320, 568, false],
  ['landscape-932x430', 932, 430, true], ['landscape-844x390', 844, 390, true],
  ['landscape-667x375', 667, 375, true],
]

let browser
const report = { desktop: [], mobile: [], errors: [] }
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  page.on('pageerror', error => report.errors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
    return route.continue()
  })

  for (const [name, width, height] of desktopProfiles) {
    await page.setViewportSize({ width, height })
    await page.goto(`http://127.0.0.1:${port}/__saved_layout__`)
    await page.locator('.builder-card-detail').waitFor()
    const geometry = await page.evaluate(() => {
      const rect = selector => { const node = document.querySelector(selector); const box = node.getBoundingClientRect(); return { top: box.top, bottom: box.bottom, width: box.width, height: box.height, clientHeight: node.clientHeight, scrollHeight: node.scrollHeight, overflowY: getComputedStyle(node).overflowY } }
      return {
        detail: rect('.deck-detail-panel'), saved: rect('.saved-decks-panel'), savedList: rect('.saved-list'),
        effect: rect('.builder-card-detail .archive-effect .l12-effect-body'),
        triggerVisible: document.querySelector('.mobile-saved-decks-nav-trigger')?.getBoundingClientRect().width > 0,
        bodyWidth: document.documentElement.scrollWidth,
      }
    })
    const ratio = geometry.detail.height / geometry.saved.height
    assert.ok(ratio >= 2.15 && ratio <= 2.85, `${name} 详情/牌库比例不是约5:2：${ratio}`)
    assert.ok(geometry.detail.bottom <= geometry.saved.top + 1, `${name} 两个侧栏区域发生侵入`)
    assert.ok(geometry.savedList.scrollHeight > geometry.savedList.clientHeight, `${name} 长牌库列表未形成独立滚动`)
    assert.equal(geometry.savedList.overflowY, 'auto', `${name} 牌库列表不是纵向滚动容器`)
    assert.ok(geometry.effect.scrollHeight > geometry.effect.clientHeight, `${name} 长效果文本未形成独立滚动`)
    assert.equal(geometry.effect.overflowY, 'auto', `${name} 效果文本不是纵向滚动容器`)
    assert.equal(geometry.triggerVisible, false, `${name} 桌面不应显示移动牌库按钮`)
    assert.ok(geometry.bodyWidth <= width + 1, `${name} 页面横向溢出`)
    await page.screenshot({ path: path.join(out, `${name}.png`) })
    report.desktop.push({ name, width, height, ratio, geometry })
  }

  for (const [name, width, height, mobileCanvas] of mobileProfiles) {
    await page.setViewportSize({ width, height })
    await page.goto(`http://127.0.0.1:${port}/__saved_layout__${mobileCanvas ? '?mobile=1' : ''}`)
    await page.locator('.deck-builder-grid').waitFor()
    assert.equal(await page.locator('.saved-decks-panel').isVisible(), false, `${name} 不应常驻已保存牌库列表`)
    const trigger = mobileCanvas && width > 820
      ? page.locator('.mobile-saved-decks-side-trigger')
      : page.locator('.mobile-saved-decks-nav-trigger')
    await trigger.waitFor()
    await trigger.click()
    const dialog = page.locator('.mobile-saved-decks-dialog')
    await dialog.waitFor()
    const state = await page.evaluate(() => {
      const dialog = document.querySelector('.mobile-saved-decks-dialog')
      const list = document.querySelector('.mobile-saved-decks-list')
      const close = document.querySelector('.mobile-saved-decks-dialog header button')
      const box = dialog.getBoundingClientRect(), closeBox = close.getBoundingClientRect()
      return {
        box: { left: box.left, right: box.right, top: box.top, bottom: box.bottom, width: box.width, height: box.height },
        area: box.width * box.height / (innerWidth * innerHeight),
        listClientHeight: list.clientHeight, listScrollHeight: list.scrollHeight, listOverflow: getComputedStyle(list).overflowY,
        closeWidth: closeBox.width, closeHeight: closeBox.height,
        bodyWidth: document.documentElement.scrollWidth,
      }
    })
    assert.ok(state.box.left >= -1 && state.box.right <= width + 1 && state.box.top >= -1 && state.box.bottom <= height + 1, `${name} 弹框超出可用画面`)
    assert.ok(state.area >= 0.55 && state.area <= 0.79, `${name} 弹框占屏比例不合理：${state.area}`)
    assert.ok(state.listScrollHeight > state.listClientHeight, `${name} 长列表未在弹框内滚动`)
    assert.equal(state.listOverflow, 'auto', `${name} 弹框列表不是独立滚动容器`)
    assert.ok(state.closeWidth >= 40 && state.closeHeight >= 40, `${name} 关闭按钮触控面积不足`)
    assert.ok(state.bodyWidth <= width + 1, `${name} 页面横向溢出`)
    await page.screenshot({ path: path.join(out, `${name}-dialog.png`) })

    const choice = page.locator('.mobile-saved-decks-list>button').nth(1)
    const selectedName = (await choice.locator('.deck-profile').getAttribute('aria-label')) ?? await choice.textContent()
    await choice.click()
    await dialog.waitFor({ state: 'detached' })
    assert.ok((await page.locator('.deck-builder-topbar input').inputValue()).length > 0, `${name} 选牌库后名称未载入`)
    await trigger.click()
    await dialog.waitFor()
    await page.keyboard.press('Escape')
    await dialog.waitFor({ state: 'detached' })
    report.mobile.push({ name, width, height, mobileCanvas, selectedName, state })
  }

  assert.deepEqual(report.errors, [], `页面错误：${report.errors.join(' | ')}`)
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ status: 'passed', desktop: report.desktop.length, mobile: report.mobile.length, assertions: report.desktop.length * 8 + report.mobile.length * 9, screenshots: fs.readdirSync(out).filter(name => name.endsWith('.png')).length, output: out }))
} finally {
  await browser?.close()
  await server.close()
}
