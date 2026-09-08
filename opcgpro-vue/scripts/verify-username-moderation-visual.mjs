import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/username-moderation'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import ProfilePage from '/src/l12/site/ProfilePage.vue'
import {authState,platformState} from '/src/l12/platform.ts'
import {l12State} from '/src/l12/net.ts'
import '/src/style.css'
platformState.token='visual-token'
platformState.account={id:'visual-account',username:'测试**玩家',role:'player',createdAt:new Date().toISOString(),publicHistory:true,mustChangeUsername:true,mustChangePassword:false,permissions:[]}
authState.initialized=true;authState.verified=true;l12State.status='offline'
const app=createApp({render:()=>h(ProfilePage)})
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/me',name:'me',component:ProfilePage}]})
app.use(router);await router.push('/me?reason=username-change-required');await router.isReady();app.mount('#app')
`

let browser
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'username-moderation-visual-entry',
  resolveId(id) { if (id === '/__username-moderation__.js') return id },
  load(id) { if (id === '/__username-moderation__.js') return entry },
  configureServer(devServer) {
    devServer.middlewares.use((request, response, next) => {
      if (request.url?.match(/^\/__username-moderation__(\?|$)/)) {
        response.setHeader('Content-Type', 'text/html')
        response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__username-moderation__.js"></script>')
        return
      }
      next()
    })
  },
}], appType: 'spa' })

try {
  await server.listen()
  const address = server.httpServer.address()
  assert(address && typeof address !== 'string')
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of [{ width: 1280, height: 720 }, { width: 390, height: 844 }]) {
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
    await page.goto(`http://127.0.0.1:${address.port}/__username-moderation__`)
    await page.waitForSelector('.username-change-gate')
    const result = await page.evaluate(() => {
      const gate = document.querySelector('.username-change-gate')
      const card = document.querySelector('.username-change-card')
      const actions = [...document.querySelectorAll('.username-change-card button')].map(node => node.textContent?.trim())
      const rect = card.getBoundingClientRect()
      return {
        gateModal: gate?.getAttribute('aria-modal'),
        card: { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom },
        actions,
        pageWidth: document.documentElement.scrollWidth,
        viewportWidth: innerWidth,
        background: getComputedStyle(card).backgroundColor,
        closeButtons: [...document.querySelectorAll('.username-change-card button')]
          .filter(node => ['×', '关闭', '取消'].includes(node.textContent?.trim() || '')).length,
      }
    })
    assert.equal(result.gateModal, 'true')
    assert(result.card.left >= 0 && result.card.right <= viewport.width)
    assert(result.card.top >= 0 && result.card.bottom <= viewport.height)
    assert.equal(result.pageWidth, result.viewportWidth)
    assert.deepEqual(result.actions, ['退出账号', '确认修改'])
    assert.equal(result.closeButtons, 0)
    assert.notEqual(result.background, 'rgb(255, 255, 255)')
    await page.screenshot({ path: path.join(out, `username-change-${viewport.width}x${viewport.height}.png`), fullPage: true })
    await page.close()
    assert.deepEqual(errors, [])
  }
  console.log('Username moderation visual checks passed at 1280x720 and 390x844.')
} finally {
  if (browser) await browser.close()
  await server.close()
}
