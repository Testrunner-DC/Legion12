import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/alternate-art-admin-closeout')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,defineComponent,h,ref} from 'vue'
import '/src/style.css'
import AdminAccountPicker from '/src/l12/site/AdminAccountPicker.vue'
import {adminApi,authState,platformState} from '@/l12/platform'
const accounts=[
 {id:'player-0001-unique',username:'桐',role:'player',createdAt:'',publicHistory:false,permissions:[]},
 {id:'player-0002-unique',username:'桐',role:'player',createdAt:'',publicHistory:false,permissions:[]},
 {id:'player-0003-disabled',username:'桐禁用',role:'player',createdAt:'',publicHistory:false,permissions:[],disabled:true},
 {id:'player-0004-deleted',username:'桐删除',role:'player',createdAt:'',publicHistory:false,permissions:[],deleted:true},
 {id:'player-0005-other',username:'青禾',role:'player',createdAt:'',publicHistory:false,permissions:[]},
]
let reads=0
adminApi.accounts=async()=>{reads++;return accounts}
authState.verified=true
platformState.token='qa-token'
platformState.account={id:'admin-a',username:'AdminA',role:'admin',createdAt:'',publicHistory:false,permissions:['admin.accounts.read','admin.content.draft']}
window.__qa={reads:()=>reads,auth:()=>({verified:authState.verified,account:platformState.account})}
const App=defineComponent({setup(){
 const accountId=ref('')
 const secondAccountId=ref('')
 const switchAccount=()=>{platformState.account={...platformState.account,id:'admin-b',username:'AdminB'}}
 return()=>h('main',{class:'qa-altart'},[
  h('h1','异画派发玩家选择'),
  h(AdminAccountPicker,{modelValue:accountId.value,'onUpdate:modelValue':value=>accountId.value=value,label:'玩家账号'}),
  h(AdminAccountPicker,{modelValue:secondAccountId.value,'onUpdate:modelValue':value=>secondAccountId.value=value,label:'第二玩家账号'}),
  h('output',{id:'selected-id'},accountId.value),
  h('button',{id:'switch-account',onClick:switchAccount},'切换管理员账号'),
 ])
}})
createApp(App).mount('#app')
`

let browser
const server = await createServer({
  root, configLoader: 'runner', cacheDir: path.join(root, '.tmp', 'vite-alternate-art-admin-closeout'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'alternate-art-admin-closeout-fixture',
    resolveId(id) { if (id === '/__alternate_art_admin_closeout__.js') return id },
    load(id) { if (id === '/__alternate_art_admin_closeout__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__alternate_art_admin_closeout__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}.qa-altart{display:grid;gap:18px;width:min(720px,calc(100% - 32px));margin:auto;padding:24px 0}.qa-altart>button{min-height:44px}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__alternate_art_admin_closeout__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]
const suffix = viewport => `${viewport.width}x${viewport.height}`
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const report = []
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__alternate_art_admin_closeout__`)
    const input = page.locator('input[role="combobox"]').nth(0)
    const secondInput = page.locator('input[role="combobox"]').nth(1)
    await input.waitFor()
    await page.waitForTimeout(100)
    assert.ok(await page.evaluate(() => window.__qa.reads()) > 0,
      `account candidates were not requested at ${suffix(viewport)}: ${pageErrors.join(' | ')} · ${JSON.stringify(await page.evaluate(() => window.__qa.auth()))} · ${await page.locator('body').innerText()}`)
    const firstControls = await input.getAttribute('aria-controls')
    const secondControls = await secondInput.getAttribute('aria-controls')
    assert.ok(firstControls && secondControls && firstControls !== secondControls, `picker aria-controls ids collided at ${suffix(viewport)}`)
    await input.fill('桐')
    const options = page.getByRole('option')
    await page.waitForFunction(() => document.querySelectorAll('[role="option"]').length === 4)
    assert.equal(await options.count(), 4, `fragment search mismatch at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: 'player-0001-unique' }).count(), 1, `first duplicate id missing at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: 'player-0002-unique' }).count(), 1, `second duplicate id missing at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: '已禁用，不可选择' }).isDisabled(), true, `disabled state missing at ${suffix(viewport)}`)
    assert.equal(await options.filter({ hasText: '已删除，不可选择' }).isDisabled(), true, `deleted state missing at ${suffix(viewport)}`)
    await input.press('Enter')
    assert.equal(await page.locator('#selected-id').textContent(), 'player-0001-unique', `keyboard selection did not submit unique id at ${suffix(viewport)}`)
    await input.fill('不存在的玩家')
    assert.equal(await page.getByText('没有匹配的玩家账号').count(), 1, `empty state missing at ${suffix(viewport)}`)
    await input.fill('青禾')
    await input.press('Enter')
    assert.equal(await page.locator('#selected-id').textContent(), 'player-0005-other', `second selection failed at ${suffix(viewport)}`)
    const readsBeforeSwitch = await page.evaluate(() => window.__qa.reads())
    await page.locator('#switch-account').click()
    await page.waitForFunction(previous => window.__qa.reads() > previous, readsBeforeSwitch)
    assert.equal(await page.locator('#selected-id').textContent(), '', `account switch retained stale player id at ${suffix(viewport)}`)
    await input.fill('桐')
    const box = await page.locator('.admin-account-options').boundingBox()
    assert.ok(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1,
      `candidate layer escaped viewport at ${suffix(viewport)}`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `page overflow at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `account-picker-${suffix(viewport)}.png`), fullPage: true })
    report.push({ viewport, duplicateCandidates: 2, disabledStates: 2, accountSwitchReloaded: true })
  }
  assert.equal(pageErrors.length, 0, `page errors: ${pageErrors.join(' | ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', report, pageErrors }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
